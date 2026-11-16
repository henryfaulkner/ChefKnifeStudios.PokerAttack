"""Fixed-path root state primitives shared by constrained host handlers."""
from contextlib import contextmanager
import fcntl
import json
import os
from pathlib import Path
import stat
import tempfile

STATE = Path('/var/lib/pokerattack')


def trusted_parents(path):
    for parent in Path(path).parents:
        info = parent.lstat()
        if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
            raise ValueError('Unsafe state directory')


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON field')
        result[key] = value
    return result


def bounded_json(raw):
    if len(raw) > 65536:
        raise ValueError('Oversize state input')
    return json.loads(raw, object_pairs_hook=unique_object,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError('Non-finite JSON')))


def read(path, mode=0o600):
    path = Path(path)
    trusted_parents(path)
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(descriptor, 'rb') as stream:
        info = os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != mode or info.st_size > 65536:
            raise ValueError('Unsafe state file')
        return bounded_json(stream.read(65537))


def write(path, document, mode=0o600):
    raw = (json.dumps(document, separators=(',', ':'), allow_nan=False) + '\n').encode()
    write_bytes(path, raw, mode)


def write_bytes(path, raw, mode=0o600):
    path = Path(path)
    trusted_parents(path)
    if path.exists() or path.is_symlink():
        info = path.lstat()
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != mode:
            raise ValueError('Unsafe destination')
    if len(raw) > 65536:
        raise ValueError('Oversize state output')
    descriptor, temporary = tempfile.mkstemp(prefix='.state-', dir=path.parent)
    try:
        with os.fdopen(descriptor, 'wb') as stream:
            os.fchmod(stream.fileno(), mode)
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


@contextmanager
def control_lock():
    with _lock('control.lock'):
        yield


@contextmanager
def deployment_lock():
    with _lock('deploy.lock'):
        yield


@contextmanager
def _lock(name):
    if name not in ('control.lock', 'deploy.lock'):
        raise ValueError('Invalid fixed lock')
    path = STATE / name
    trusted_parents(path)
    descriptor = os.open(path, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        info = os.fstat(descriptor)
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600:
            raise ValueError('Unsafe control lock')
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield
    finally:
        os.close(descriptor)
