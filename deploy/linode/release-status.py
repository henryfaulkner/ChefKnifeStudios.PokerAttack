#!/usr/bin/env python3
"""Read-only release status, including interrupted transactions; never print secrets.

Installed root-owned at /opt/pokerattack/release-status.py. A successful process
exit means a diagnostic response, not a healthy application. Consumers must check
consistent/readiness plus pending/lease/transaction before publication.
"""
import datetime
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

STATE = Path('/var/lib/pokerattack')
CONFIG = Path('/etc/pokerattack')
INSTALL = Path('/opt/pokerattack')
DIGEST = re.compile(r'sha256:[a-f0-9]{64}')
HASH = re.compile(r'[a-f0-9]{64}')


def command(*args):
    result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=6, check=True)
    return result.stdout.decode('utf-8')


def status(target):
    if target not in ('production', 'pilot'):
        raise ValueError('Invalid target')
    base = STATE / target
    def stamp(path):
        try:
            value = path.lstat()
            return (value.st_ino, value.st_mtime_ns, value.st_size)
        except FileNotFoundError:
            return None

    watched = [base / name for name in ('active', 'pending', 'known-good', 'previous-good', 'transaction', 'lease')]
    watched.append(base / 'admission' / 'state.json')
    initial_stamps = {p: stamp(p) for p in watched}
    result = {'protocol_version': 1, 'target': target,
              'sampled_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'capabilities': ['structured-status-v1'], 'consistent': True, 'errors': [],
              'pairs': {}, 'transaction': (base / 'transaction').exists(), 'lease': None,
              'maintenance': {'supported': False, 'active': None}, 'runtime': None, 'sessions': None,
              'readiness': {'ready': False, 'identity_matches': False, 'limits_match': False, 'config_matches': False}}

    def error(code):
        result['consistent'] = False
        result['errors'].append(code)

    pointer_identities = {}
    for name in ('active', 'pending', 'known-good', 'previous-good'):
        pointer = base / name
        result['pairs'][name] = None
        try:
            if not pointer.is_symlink():
                if pointer.exists():
                    error('invalid-' + name + '-pointer')
                continue
            release = pointer.resolve(strict=True)
            pointer_identities[name] = str(release)
            if release.parent != (base / 'releases').resolve():
                raise ValueError('Release pointer outside fixed storage')
            image, digest, config = ((release / field).read_text().strip() for field in ('image', 'digest', 'config-id'))
            if not DIGEST.fullmatch(digest) or not HASH.fullmatch(config) or image != (
                    'chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi@' + digest):
                raise ValueError('Invalid recorded identity')
            snapshot_hash = hashlib.sha256((release / 'app.env').read_bytes()).hexdigest()
            result['pairs'][name] = {'digest': digest, 'config_sha256': config, 'image_reference': image,
                                      'snapshot_config_matches': snapshot_hash == config}
            if name == 'active':
                actual_hash = hashlib.sha256((CONFIG / target / 'configs' / (config + '.env')).read_bytes()).hexdigest()
                result['readiness']['config_matches'] = actual_hash == config == snapshot_hash
        except Exception:
            error('unreadable-' + name + '-identity')
    lease = base / 'lease'
    if lease.exists() or lease.is_symlink():
        try:
            if lease.is_symlink() or lease.stat().st_size > 65536:
                raise ValueError('Invalid lease')
            data = json.loads(lease.read_text())
            allowed = ('kind', 'run_id', 'run_attempt', 'digest', 'config_sha256', 'source_sha', 'phase',
                       'approved_unix', 'expires_unix', 'verifier_revoked', 'verifier_issued', 'manifest_sha256', 'evidence_sha256',
                       'client_archive_sha256', 'artifact_id', 'minimum_server_capability',
                       'client_source_run_id', 'client_source_run_attempt',
                       'client_recovery_run_id', 'client_recovery_run_attempt',
                       'client_recovery_artifact_id', 'client_recovery_archive_sha256')
            # Output only bounded scalar fields; never credential/session data.
            result['lease'] = {key: value for key, value in data.items() if key in allowed
                               and isinstance(value, (str, int, bool)) and len(str(value)) <= 256}
        except Exception:
            error('unreadable-lease')
            result['lease'] = {'phase': 'unknown'}
    admission = base / 'admission' / 'state.json'
    if admission.exists() or admission.is_symlink():
        result['maintenance']['supported'] = True
        try:
            if admission.is_symlink() or admission.stat().st_size > 65536:
                raise ValueError('Invalid admission state')
            data = json.loads(admission.read_text())
            if type(data.get('active')) is not bool:
                raise ValueError('Missing admission state')
            result['maintenance']['active'] = data['active']
            for key in ('run_id', 'run_attempt', 'digest', 'config_sha256', 'source_sha'):
                if key in data and isinstance(data[key], (int, str)) and len(str(data[key])) <= 128:
                    result['maintenance'][key] = data[key]
        except Exception:
            error('unreadable-admission')
    if result['lease'] and result['lease'].get('kind') == 'server':
        if result['maintenance']['active'] is not True or any(result['maintenance'].get(key) != result['lease'].get(key)
                for key in ('run_id', 'run_attempt', 'digest', 'config_sha256', 'source_sha')):
            error('lease-admission-context-mismatch')
    try:
        ids = command('docker', 'ps', '-aq', '--filter', f'label=com.docker.compose.project=pokerattack-{target}',
                      '--filter', 'label=com.docker.compose.service=api').split()
        if len(ids) > 1:
            raise ValueError('Multiple target API containers')
        if ids:
            d = json.loads(command('docker', 'inspect', ids[0]))[0]
            labels = json.loads(command('docker', 'image', 'inspect', d['Image'], '--format', '{{json .Config.Labels}}')) or {}
            revision = labels.get('org.opencontainers.image.revision')
            if not isinstance(revision, str) or not re.fullmatch(r'[a-f0-9]{40}', revision):
                revision = None
            runtime = {'container_id': d['Id'], 'image_id': d['Image'], 'image_reference': d['Config']['Image'],
                       'source_revision': revision, 'state': d['State']['Status'],
                       'docker_health': d['State'].get('Health', {}).get('Status', 'not-configured'),
                       'nano_cpus': d['HostConfig']['NanoCpus'], 'memory_bytes': d['HostConfig']['Memory']}
            result['runtime'] = runtime
            result['readiness']['limits_match'] = runtime['nano_cpus'] == 250000000 and runtime['memory_bytes'] == 536870912
            active = result['pairs']['active']
            if active:
                expected_id = command('docker', 'image', 'inspect', active['image_reference'], '--format', '{{.Id}}').strip()
                result['readiness']['identity_matches'] = runtime['image_id'] == expected_id and runtime['image_reference'] == active['image_reference']
            if runtime['state'] == 'running':
                try:
                    port = 8081 if target == 'production' else 8082
                    command('curl', '--silent', '--fail', '--max-time', '5', f'http://127.0.0.1:{port}/health/ready')
                    result['readiness']['ready'] = all(result['readiness'][key] for key in
                                                     ('identity_matches', 'limits_match', 'config_matches'))
                except Exception:
                    pass
                try:
                    raw = command('curl', '--silent', '--fail', '--max-time', '5', f'http://127.0.0.1:{port}/_ops/status')
                    if len(raw) > 4096:
                        raise ValueError('Oversized session observation')
                    counts = json.loads(raw)
                    values = {key: counts[key] for key in ('activeGames', 'lobbies', 'soloGames')}
                    if any(type(value) is not int or not 0 <= value <= 1000000 for value in values.values()):
                        raise ValueError('Invalid session counts')
                    result['sessions'] = values
                except Exception:
                    pass  # Unavailable counts do not imply zero or alter readiness.
    except Exception:
        error('runtime-inspection-failed')
    # Pointers are atomically replaced by mutators. Flag an incoherent sample if
    # they changed while Docker was being queried; callers may retry reads only.
    for name, identity in pointer_identities.items():
        if str((base / name).resolve()) != identity:
            error('state-changed-during-read')
    if result['transaction'] != (base / 'transaction').exists():
        error('transaction-changed-during-read')
    if any(stamp(p) != initial_stamps[p] for p in watched):
        error('state-changed-during-read')
    if not result['consistent']:
        result['readiness']['ready'] = False
    return result


if __name__ == '__main__':
    if len(sys.argv) != 2 or sys.argv[1] not in ('production', 'pilot'):
        sys.exit('Expected a fixed release target.')
    print(json.dumps(status(sys.argv[1]), separators=(',', ':')))
