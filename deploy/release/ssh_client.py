"""Bounded forced-command transport. No shell, mutation retry or key output."""
from contextlib import contextmanager
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

sys.path.insert(0, str(Path(__file__).parents[1] / 'linode'))
from release_commands import parse_ssh
from manifests import load_document

HOST = '170.187.142.115'
PROFILES = {'status': ('pa-status', 'LINODE_STATUS_PRIVATE_KEY'),
            'client-lease': ('pa-client', 'LINODE_CLIENT_LEASE_PRIVATE_KEY'),
            'deployment': ('pa-deploy', 'LINODE_DEPLOY_PRIVATE_KEY'),
            'evidence': ('pa-evidence', 'LINODE_EVIDENCE_PRIVATE_KEY'),
            'promotion': ('pa-promote', 'LINODE_PROMOTION_PRIVATE_KEY')}


class UnknownOutcome(RuntimeError):
    pass


class Transport:
    def __init__(self, role, key, known_hosts):
        if role not in PROFILES: raise ValueError('Invalid fixed SSH capability')
        self.role, self.key, self.known_hosts = role, Path(key), Path(known_hosts)

    def call(self, command, packet=None, timeout=90):
        parse_ssh(self.role, command)
        for path in (self.key, self.known_hosts):
            if path.is_symlink() or not path.is_file(): raise ValueError('Missing private transport material')
        if os.name != 'nt' and self.key.stat().st_mode & 0o077: raise ValueError('SSH key must be mode 0600')
        raw = None if packet is None else json.dumps(packet, separators=(',', ':'), allow_nan=False).encode()
        if raw is not None and len(raw) > 65536: raise ValueError('Host packet exceeds 64 KiB')
        args = ['ssh', '-T', '-F', os.devnull, '-p', '22', '-i', str(self.key),
                '-o', 'UserKnownHostsFile=' + str(self.known_hosts), '-o', 'StrictHostKeyChecking=yes',
                '-o', 'GlobalKnownHostsFile=' + os.devnull,
                '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'IdentityAgent=none',
                '-o', 'PasswordAuthentication=no', '-o', 'KbdInteractiveAuthentication=no',
                '-o', 'ClearAllForwardings=yes', '-o', 'ConnectTimeout=15', '-o', 'ConnectionAttempts=1',
                '-o', 'ServerAliveInterval=15', '-o', 'ServerAliveCountMax=2',
                PROFILES[self.role][0] + '@' + HOST, command]
        environment = {key: value for key, value in os.environ.items()
                       if key not in {profile[1] for profile in PROFILES.values()}}
        try:
            result = subprocess.run(args, input=raw, capture_output=True, timeout=timeout, env=environment)
        except subprocess.TimeoutExpired:
            raise UnknownOutcome('Host response timed out; inspect structured status before any further mutation') from None
        if result.returncode:
            # A lost connection is unknown, even if some stdout was delivered.
            if result.returncode == 255:
                raise UnknownOutcome('SSH response uncertain; inspect structured status before retrying')
            raise RuntimeError('Fixed host command rejected; inspect structured status')
        try: return load_document(result.stdout)
        except Exception: raise UnknownOutcome('Host response invalid; inspect status without retrying mutation') from None


@contextmanager
def transport(role):
    if role not in PROFILES: raise ValueError('Invalid fixed role')
    root = Path(os.environ['RUNNER_TEMP']).resolve(strict=True)
    secret, known = os.environ.get(PROFILES[role][1]), os.environ.get('LINODE_KNOWN_HOSTS')
    if not secret or not known: raise ValueError('Missing provisioned environment transport material')
    # TemporaryDirectory is created only inside the explicitly named runner temp
    # root, with an internally generated name, and removed on normal exit.
    with tempfile.TemporaryDirectory(prefix='pokerattack-ssh-', dir=root) as directory:
        directory = Path(directory)
        assert directory.resolve().parent == root and not directory.is_symlink()
        key, hosts = directory / 'identity', directory / 'known_hosts'
        key.write_text(secret.rstrip() + '\n', encoding='utf-8'); key.chmod(0o600)
        hosts.write_text(known.rstrip() + '\n', encoding='utf-8'); hosts.chmod(0o600)
        yield Transport(role, key, hosts)
