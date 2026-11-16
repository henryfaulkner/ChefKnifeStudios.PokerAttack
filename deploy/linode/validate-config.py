#!/usr/bin/env python3
"""Validate the deliberately narrow, root-provisioned runtime format; never print values."""
import hashlib
import json
from pathlib import Path
import re
import stat
import sys
import time


def protected(path):
    path = Path(path)
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600:
        raise ValueError('Expected a root-owned mode-0600 regular file')
    # Provisioned directories must also prevent unprivileged replacement.
    for parent in path.parents:
        info = parent.stat()
        if info.st_uid != 0 or stat.S_IMODE(info.st_mode) & 0o022:
            raise ValueError('Unsafe parent directory')
    return path.read_bytes()


def config(target, path, expected):
    raw = protected(path)
    if hashlib.sha256(raw).hexdigest() != expected:
        raise ValueError('Configuration hash differs from requested identity')
    values = {}
    for line in raw.decode('utf-8').splitlines():
        if not line or line.startswith('#'):
            continue
        key, value = line.split('=', 1)
        if key in values or not re.fullmatch(r'[A-Za-z][A-Za-z0-9_]*', key):
            raise ValueError('Duplicate/invalid environment key')
        # Compose env_file would interpolate dollars; require unambiguous raw values.
        if any(character in value for character in '$\r\n\'"'):
            raise ValueError('Quoted/interpolated values are not supported')
        values[key] = value
    pilot = target == 'pilot'
    expected_env = 'Staging' if pilot else 'Production'
    if values.get('ASPNETCORE_ENVIRONMENT') != expected_env or values.get('Testing__EnableEndpoints') != str(pilot).lower():
        raise ValueError('Environment/test boundary mismatch')
    db = 'pokerattack_pilot' if pilot else 'expensetrackerdb'
    role = 'pokerattack_pilot_runtime' if pilot else 'pokerattack_runtime'
    pattern = rf'Host=127\.0\.0\.1;Database={db};Username={role};Password=[A-Za-z0-9_-]{{32,}};SSL Mode=Disable'
    if not re.fullmatch(pattern, values.get('ConnectionStrings__PokerAttackDB', '')):
        raise ValueError('Database must use the reviewed loopback/scoped-role format')
    if values.get('DOTNET_ENVIRONMENT', expected_env) != expected_env:
        raise ValueError('Conflicting DOTNET_ENVIRONMENT')
    hostname = 'pilot-api.playpokerattack.com' if pilot else 'api.playpokerattack.com'
    if values.get('AllowedHosts') != f'{hostname};localhost;127.0.0.1;[::1]':
        raise ValueError('Unexpected host allowlist')
    origins = values.get('Hosting__AllowedOrigins', '').split(';')
    allowed = {'http://localhost:5173'} if pilot else {
        'https://playpokerattack.com', 'https://www.playpokerattack.com',
        'https://salmon-hill-0056eb60f.2.azurestaticapps.net'}
    if not origins or any(origin not in allowed for origin in origins):
        raise ValueError('Unexpected browser origin')


def main():
    action, target, path, identity, *extra = sys.argv[1:]
    if target not in ('pilot', 'production'):
        raise ValueError('Invalid target')
    if action == 'config':
        config(target, path, identity)
    elif action == 'maintenance':
        if protected(path).decode().strip() != 'sessions-reset-approved' or not 0 <= time.time() - Path(path).stat().st_mtime <= 1800:
            raise ValueError('Maintenance/reset acknowledgement absent or expired')
    elif action == 'verification':
        if target == 'production':
            # Production requires imported, run-bound evidence and the separate
            # promotion controller; the historical shape-only proof is pilot-only.
            from release_evidence import legacy_guard
            digest, config_id = extra
            if Path(path) != Path('/etc/pokerattack/production/verification') / (identity + '.json'):
                raise ValueError('Invalid fixed evidence path')
            legacy_guard(digest, config_id, identity)
            return
        raw = protected(path)
        if hashlib.sha256(raw).hexdigest() != identity:
            raise ValueError('Verification identity mismatch')
        evidence = json.loads(raw)
        digest, config_id = extra
        if evidence.get('digest') != digest or evidence.get('config_sha256') != config_id:
            raise ValueError('Verification does not match candidate pair')
        if not 0 <= time.time() - evidence.get('verified_unix', 0) <= 1800:
            raise ValueError('Verification expired')
        if not re.fullmatch(r'[a-f0-9]{40}', evidence.get('source_sha', '')):
            raise ValueError('Missing tested source SHA')
        for check in ('https', 'signalr', 'multiplayer', 'solo', 'private_message_isolation'):
            if evidence.get(check) is not True:
                raise ValueError('External gameplay verification incomplete')
    else:
        raise ValueError('Unknown validation action')


if __name__ == '__main__':
    try:
        main()
    except Exception:
        # Provider, file and config exceptions must never echo secret values.
        sys.exit('Protected configuration/maintenance/verification validation failed.')
