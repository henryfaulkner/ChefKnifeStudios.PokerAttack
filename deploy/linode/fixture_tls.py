"""Local HTTPS fixture trust, never a production certificate or TLS bypass.

Linux trust creation is opt-in and restricted to disposable GitHub-hosted runners.
The generated CA is removed from Chromium's user NSS store on exit. System trust
is untouched. See Chromium docs/linux/cert_management.md for NSS store selection.
"""
from contextlib import contextmanager
import os
from pathlib import Path
import shutil
import subprocess
import uuid


def checked(*args):
    result = subprocess.run(args, capture_output=True)
    if result.returncode:
        raise RuntimeError('Disposable HTTPS certificate setup failed')


@contextmanager
def certificate(directory):
    cert, key = directory / 'localhost.pem', directory / 'localhost.key'
    installed = []
    try:
        if os.name == 'nt':
            checked('dotnet', 'dev-certs', 'https', '--check', '--trust')
            checked('dotnet', 'dev-certs', 'https', '--export-path', str(cert), '--format', 'Pem', '--no-password', '--quiet')
        elif os.environ.get('POKERATTACK_FIXTURE_EPHEMERAL_TLS') == '1':
            if os.environ.get('GITHUB_ACTIONS') != 'true' or os.environ.get('RUNNER_ENVIRONMENT') != 'github-hosted':
                raise ValueError('Ephemeral NSS trust is restricted to disposable GitHub-hosted runners')
            ca, ca_key = directory / 'ca.pem', directory / 'ca.key'
            request, leaf = directory / 'localhost.csr', directory / 'leaf.pem'
            extensions = directory / 'localhost.ext'
            extensions.write_text('basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\nsubjectAltName=DNS:localhost,IP:127.0.0.1\n')
            checked('openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1', '-subj', '/CN=PokerAttack disposable fixture CA',
                    '-addext', 'basicConstraints=critical,CA:TRUE', '-addext', 'keyUsage=critical,keyCertSign,cRLSign', '-keyout', str(ca_key), '-out', str(ca))
            checked('openssl', 'req', '-new', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=localhost', '-keyout', str(key), '-out', str(request))
            checked('openssl', 'x509', '-req', '-in', str(request), '-CA', str(ca), '-CAkey', str(ca_key), '-CAcreateserial', '-days', '1', '-extfile', str(extensions), '-out', str(leaf))
            cert.write_bytes(leaf.read_bytes() + ca.read_bytes())
            # Pinned Playwright can predate Chromium M146. Cover both NSS store
            # locations without changing HOME, system trust or existing entries.
            databases = {Path.home() / '.pki/nssdb',
                         Path(os.environ.get('XDG_DATA_HOME', str(Path.home() / '.local/share'))) / 'pki/nssdb'}
            for database in databases:
                database.mkdir(parents=True, exist_ok=True)
                if not (database / 'cert9.db').exists():
                    checked('certutil', '-N', '--empty-password', '-d', 'sql:' + str(database))
                nickname = 'pokerattack-fixture-' + uuid.uuid4().hex
                checked('certutil', '-A', '-d', 'sql:' + str(database), '-n', nickname, '-t', 'C,,', '-i', str(ca))
                installed.append((database, nickname))
        else:
            shutil.copyfile(os.environ['POKERATTACK_FIXTURE_CERT'], cert)
            shutil.copyfile(os.environ['POKERATTACK_FIXTURE_KEY'], key)
        cert.chmod(0o600)
        key.chmod(0o600)
        yield cert, key
    finally:
        for database, nickname in reversed(installed):
            checked('certutil', '-D', '-d', 'sql:' + str(database), '-n', nickname)
