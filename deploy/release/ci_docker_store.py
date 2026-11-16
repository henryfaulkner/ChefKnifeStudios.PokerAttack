"""Configure only disposable GitHub-hosted Linux runners for exact image IDs.

The containerd store preserves OCI index identities when Docker save/load moves
the tested image to another runner. Never installed/invoked on Linode. Shared
Docker, PostgreSQL, Caddy and local developer Docker are outside this operation.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

CONFIG = Path('/etc/docker/daemon.json')


def command(*args):
    result = subprocess.run(args, capture_output=True, timeout=90)
    if result.returncode: raise RuntimeError('Disposable runner Docker configuration failed')
    return result.stdout


def compatible():
    values = json.loads(command('docker', 'info', '--format', '{{json .DriverStatus}}'))
    return ['driver-type', 'io.containerd.snapshotter.v1'] in (values or [])


def configure(env=os.environ):
    if (sys.platform != 'linux' or os.geteuid() != 0 or env.get('GITHUB_ACTIONS') != 'true'
            or env.get('RUNNER_ENVIRONMENT') != 'github-hosted'):
        raise ValueError('Docker setup is restricted to disposable GitHub-hosted Linux runners')
    if compatible(): return {'containerd_image_store': True, 'restarted': False}
    config = json.loads(CONFIG.read_bytes()) if CONFIG.exists() else {}
    if not isinstance(config, dict) or not isinstance(config.get('features', {}), dict):
        raise ValueError('Unexpected hosted runner Docker configuration')
    config.setdefault('features', {})['containerd-snapshotter'] = True
    CONFIG.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix='.pokerattack-ci-', dir=CONFIG.parent)
    try:
        with os.fdopen(descriptor, 'w', encoding='utf-8') as stream:
            os.fchmod(stream.fileno(), 0o600)
            json.dump(config, stream)
            stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, CONFIG)
    finally:
        if os.path.exists(temporary): os.unlink(temporary)
    command('systemctl', 'restart', 'docker')
    if not compatible(): raise ValueError('Runner did not establish the required image store')
    return {'containerd_image_store': True, 'restarted': True}


if __name__ == '__main__':
    try: print(json.dumps(configure()))
    except Exception:
        print('Disposable runner image-store prerequisite failed; no image may be published or deployed.')
        raise SystemExit(1)
