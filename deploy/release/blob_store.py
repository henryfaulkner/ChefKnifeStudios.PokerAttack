"""Private archive backends. Azure CLI uses current OIDC login, never keys/SAS."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import time
from contextlib import contextmanager


class Conflict(ValueError):
    pass


def key_name(key):
    if not isinstance(key, str) or not re.fullmatch(r'[A-Za-z0-9_./-]+', key) or any(part in ('', '.', '..') for part in key.split('/')):
        raise ValueError('Invalid fixed archive key')
    return key


def file_hash(path):
    result = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''): result.update(chunk)
    return result.hexdigest()


class LocalStore:
    """Off-production fixture backend with the same conditional-write contract."""
    def __init__(self, root):
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)

    def info(self, key):
        path = self.root / key_name(key)
        if not path.exists(): return None
        return {'etag': file_hash(path), 'modified_unix': path.stat().st_mtime}

    @contextmanager
    def locked(self):
        path = self.root / '.local-write-lock'
        try: descriptor = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        except FileExistsError: raise Conflict('Concurrent local archive writer')
        try: yield
        finally:
            os.close(descriptor)
            path.unlink()

    def download(self, key, destination, etag=None):
        info = self.info(key)
        if not info or etag is not None and info['etag'] != etag: raise Conflict('Archive changed or missing')
        shutil.copyfile(self.root / key_name(key), destination)
        if file_hash(destination) != info['etag']: raise Conflict('Concurrent archive change')

    def upload(self, key, source, etag=None, create=False):
        with self.locked():
            self._upload(key, source, etag, create)

    def _upload(self, key, source, etag, create):
        path = self.root / key_name(key)
        current = self.info(key)
        if create and current or etag is not None and (not current or current['etag'] != etag):
            raise Conflict('Conditional archive write rejected')
        if not create and etag is None: raise ValueError('Unconditional writes prohibited')
        path.parent.mkdir(parents=True, exist_ok=True)
        descriptor, temporary = tempfile.mkstemp(prefix='.blob-', dir=path.parent)
        os.close(descriptor)
        try:
            shutil.copyfile(source, temporary)
            # Atomic fixture commit; real multi-run exclusion is the host lease,
            # and Azure enforces its own ETag condition atomically.
            os.replace(temporary, path)
        finally:
            if Path(temporary).exists(): Path(temporary).unlink()

    def list(self):
        return [{'key': p.relative_to(self.root).as_posix(), **self.info(p.relative_to(self.root).as_posix())}
                for p in self.root.rglob('*') if p.is_file() and not p.name.startswith('.')]

    def delete(self, key, etag):
        with self.locked():
            current = self.info(key)
            if not current or current['etag'] != etag: raise Conflict('Conditional delete rejected')
            (self.root / key_name(key)).unlink()


class AzureStore:
    CONTAINER = 'pokerattack-client-releases'

    def __init__(self, account):
        if not re.fullmatch(r'[a-z0-9]{3,24}', account): raise ValueError('Invalid provisioned storage account')
        self.account = account

    def command(self, action, *arguments, missing=False):
        name = 'az.cmd' if os.name == 'nt' else 'az'
        executable = shutil.which(name) or name
        result = subprocess.run([executable, 'storage', 'blob', action, '--account-name', self.account,
            '--container-name', self.CONTAINER, '--auth-mode', 'login', '--only-show-errors',
            *arguments, '--output', 'json'], capture_output=True, text=True, timeout=300)
        if result.returncode:
            if missing and 'BlobNotFound' in result.stderr: return None
            if 'ConditionNotMet' in result.stderr or 'BlobAlreadyExists' in result.stderr: raise Conflict('Conditional blob operation rejected')
            raise RuntimeError('Authenticated private blob operation failed')
        return json.loads(result.stdout) if result.stdout.strip() else None

    def info(self, key):
        value = self.command('show', '--name', key_name(key), missing=True)
        if value is None: return None
        import datetime
        properties = value['properties']
        timestamp = datetime.datetime.fromisoformat(properties['lastModified'].replace('Z', '+00:00')).timestamp()
        return {'etag': properties['etag'], 'modified_unix': timestamp}

    def download(self, key, destination, etag=None):
        arguments = ['--name', key_name(key), '--file', str(destination), '--no-progress', '--overwrite', 'true']
        if etag is not None: arguments += ['--if-match', etag]
        self.command('download', *arguments)

    def upload(self, key, source, etag=None, create=False):
        if create:
            condition = ['--if-none-match', '*', '--overwrite', 'false']
        elif etag is not None:
            condition = ['--if-match', etag, '--overwrite', 'true']
        else: raise ValueError('Unconditional blob writes prohibited')
        self.command('upload', '--name', key_name(key), '--file', str(source), '--no-progress', *condition)

    def list(self):
        import datetime
        values = self.command('list')
        return [{'key': key_name(value['name']), 'etag': value['properties']['etag'],
                 'modified_unix': datetime.datetime.fromisoformat(value['properties']['lastModified'].replace('Z', '+00:00')).timestamp()}
                for value in values]

    def delete(self, key, etag):
        self.command('delete', '--name', key_name(key), '--if-match', etag)
