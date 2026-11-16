"""Durable maintenance leases. No operation automatically reopens on failure.

All paths are fixed/root-owned. Callers supply identities and bounded JSON only.
The control lock protects state; legacy replacement acquires the separate existing
deploy lock itself, after the controller releases the short control lock.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.request

import host_manifests
import secure_state as storage
from release_verifier import status_module, matching

CONFIG = Path('/etc/pokerattack')
LEGACY = '/usr/local/sbin/pokerattack-deploy'

spec = importlib.util.spec_from_file_location('config_validator', Path(__file__).with_name('validate-config.py'))
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


def canonical(document):
    return json.dumps(document, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def fresh(ack):
    for key in ('approved_unix', 'expires_unix', 'drain_seconds'):
        if type(ack.get(key)) is not int:
            raise ValueError('Invalid acknowledgment time')
    now = int(time.time())
    if not 0 <= now - ack['approved_unix'] < 1800 or not now < ack['expires_unix'] <= ack['approved_unix'] + 1800:
        raise ValueError('Expired reset acknowledgment')
    if not 0 <= ack['drain_seconds'] <= 300 or ack.get('policy') != 'reset-at-deadline':
        raise ValueError('Unapproved drain/reset policy')


def counts():
    with urllib.request.urlopen('http://127.0.0.1:8081/_ops/status', timeout=5) as response:
        raw = response.read(4097)
        if response.status != 200 or len(raw) > 4096:
            raise ValueError('Unavailable session observation')
    result = storage.bounded_json(raw)
    values = {key: result[key] for key in ('activeGames', 'lobbies', 'soloGames')}
    if any(type(value) is not int or not 0 <= value <= 1000000 for value in values.values()):
        raise ValueError('Invalid session observation')
    return values


def schema_ready(manifest):
    # Read-only constant SQL. No database credentials, caller SQL, migrations,
    # history relocation or shared schema changes are possible through this path.
    sql = 'BEGIN READ ONLY; SET LOCAL statement_timeout = 3000; SELECT "MigrationId" FROM public."__EFMigrationsHistory"; ROLLBACK;'
    result = subprocess.run(['/usr/sbin/runuser', '-u', 'postgres', '--', '/usr/bin/psql', '-X', '-q', '-t', '-A',
                             '-v', 'ON_ERROR_STOP=1', '-d', 'expensetrackerdb', '-c', sql],
                            capture_output=True, timeout=8, check=True)
    applied = set(result.stdout.decode().splitlines())
    required = set(manifest['schema']['required_migrations'])
    known_good = storage.STATE / 'production/known-good'
    if known_good.exists():
        release = known_good.resolve(strict=True)
        if release.parent != (storage.STATE / 'production/releases').resolve(strict=True):
            raise ValueError('Recovery release outside protected snapshot storage')
        prior_manifest = release / 'release-manifest.json'
        if prior_manifest.exists() or prior_manifest.is_symlink():
            prior = host_manifests.server(storage.read(prior_manifest))
            required.update(prior['schema']['required_migrations'])
    # The legacy bootstrap pair has no manifest. Its existing /health/ready
    # check establishes its prerequisite; do not fabricate a historical manifest.
    if not required.issubset(applied):
        raise ValueError('Unmet schema prerequisite')


def application_ready():
    with urllib.request.urlopen('http://127.0.0.1:8081/release/status', timeout=5) as response:
        raw = response.read(4097)
        if response.status != 200 or len(raw) > 4096:
            raise ValueError('Maintenance-capable baseline not installed')
    state = storage.bounded_json(raw)
    if type(state.get('capability')) is not int or state['capability'] < 1 or state.get('maintenance') is not False:
        raise ValueError('Maintenance-capable baseline not installed')


def recovery_target(live, prior):
    target = live['pairs']['known-good'] if live['pairs']['pending'] else live['pairs']['previous-good']
    action = 'rollback'
    if (prior and not live['pairs']['pending'] and prior.get('phase') in
            ('failed', 'drain-failed', 'drained', 'reset-approved', 'draining', 'unknown', 'deploying') and
            live['pairs']['active'] == live['pairs']['known-good'] == prior.get('recovery_pair')):
        target, action = live['pairs']['known-good'], 'deploy'
    return target, action


def begin(run, attempt, packet, rollback=False, bootstrap=False):
    expected_fields = ('manifest', 'acknowledgment', 'recovery_from') if rollback else ('manifest', 'acknowledgment')
    host_manifests.fields(packet, expected_fields)
    manifest = host_manifests.server(packet['manifest'])
    ack = packet['acknowledgment']
    host_manifests.fields(ack, ('action', 'digest', 'config_sha256', 'source_sha', 'run_id', 'run_attempt',
                               'approved_unix', 'expires_unix', 'drain_seconds', 'policy'))
    expected_action = 'rollback' if rollback else 'bootstrap' if bootstrap else 'deploy'
    if ack['action'] != expected_action:
        raise ValueError('Wrong reset approval action')
    expected = {'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256'],
                'source_sha': manifest['source_sha'], 'run_id': int(run), 'run_attempt': int(attempt)}
    if any(ack[key] != value or type(ack[key]) is not type(value) for key, value in expected.items()):
        raise ValueError('Acknowledgment identity mismatch')
    if not rollback and (manifest['workflow']['run_id'] != int(run) or manifest['workflow']['run_attempt'] != int(attempt)):
        raise ValueError('Wrong workflow provenance')
    packet_hash = hashlib.sha256(canonical(packet)).hexdigest()
    base = storage.STATE / 'production'
    with storage.control_lock():
        if (base / 'completed' / f'{run}-{attempt}.json').exists():
            raise ValueError('Run/attempt already completed; use a new attempt')
        prior = storage.read(base / 'lease') if (base / 'lease').exists() or (base / 'lease').is_symlink() else None
        if prior and prior.get('kind') != 'server':
            raise ValueError('Client publication owns the slot; server recovery cannot steal it')
        if prior and prior.get('run_id') == int(run) and prior.get('run_attempt') == int(attempt):
            if prior.get('packet_sha256') == packet_hash:
                return {'phase': prior['phase'], 'idempotent': True}
            raise ValueError('Cannot refresh an existing approval')
        if prior and not rollback:
            raise ValueError('Another run owns the lease')
        if rollback:
            expected_prior = {key: prior.get(key) for key in ('run_id', 'run_attempt', 'digest', 'config_sha256')} if prior else None
            if packet['recovery_from'] != expected_prior:
                raise ValueError('Recovery must explicitly identify the previous lease')
        fresh(ack)
        # Never transfer a lease while legacy replacement/recovery or backup
        # holds the host lock. This probe is non-blocking and is released before
        # invoking a command that acquires that same lock itself.
        with storage.deployment_lock():
            pass
        live = status_module.status('production')
        if not live['consistent'] or live['transaction'] or (not rollback and (live['pairs']['pending'] or not live['readiness']['ready'])):
            raise ValueError('Unresolved current release')
        if not live['pairs']['known-good'] or not rollback and live['pairs']['active'] != live['pairs']['known-good']:
            raise ValueError('Missing current recovery pair')
        recovery = live['pairs']['known-good']
        replacement_action = 'rollback' if rollback else 'deploy'
        if rollback:
            # A failed/pre-stop candidate may never have become pending. Recover
            # its recorded serving known-good pair explicitly, without silently
            # downgrading to an unrelated previous-good release. This is a fresh
            # acknowledged redeployment of the same pair, followed by verification.
            recovery, replacement_action = recovery_target(live, prior)
            if not recovery or recovery['digest'] != expected['digest'] or recovery['config_sha256'] != expected['config_sha256']:
                raise ValueError('Rollback manifest differs from recorded recovery pair')
        validator.config('production', CONFIG / 'production/configs' / (manifest['config_sha256'] + '.env'), manifest['config_sha256'])
        schema_ready(manifest)
        if not bootstrap and not rollback:
            application_ready()
        if bootstrap:
            from bootstrap_recovery import snapshot
            preserved=snapshot(live)
        lease = {'kind': 'server', **expected, 'phase': 'bootstrap-maintenance-pending' if bootstrap else 'draining', 'manifest': manifest,
                 'manifest_sha256': hashlib.sha256(canonical(manifest)).hexdigest(),
                 'acknowledgment': ack, 'packet_sha256': packet_hash, 'verifier_issued': False, 'verifier_revoked': False,
                 'approved_unix': ack['approved_unix'], 'expires_unix': ack['expires_unix'],
                 'recovery_pair': recovery, 'replacement_action': replacement_action, 'bootstrap': bootstrap}
        if bootstrap:lease['bootstrap_recovery']=preserved
        if prior:
            archive = base / 'interrupted' / f"{prior['run_id']}-{prior['run_attempt']}-recovered-by-{run}-{attempt}.json"
            storage.write(archive, prior)
        storage.write(base / 'lease', lease)
        storage.write(base / 'admission/state.json', {'active': True, **expected}, 0o644)
    if bootstrap:
        # The old application cannot enforce this admission file. Reserve the
        # server slot first; root must prove static maintenance on every client
        # origin before the separately invoked bounded drain can begin.
        return {'phase': 'bootstrap-maintenance-pending'}
    return drain(run, attempt, ack, unavailable_reset=rollback and not live['readiness']['ready'])


def drain(run, attempt, ack, unavailable_reset=False):
    # Durable lease survives runner/SSH loss; no host-wide deployment lock is held
    # during waiting. Another release cannot acquire this lease while draining.
    deadline = time.monotonic() + ack['drain_seconds']
    try:
        while True:
            fresh(ack)
            try:
                observed = counts()
            except Exception:
                if not unavailable_reset: raise
                observed = None  # Never label an unavailable observer as zero.
            if observed is not None and not any(observed.values()) or time.monotonic() >= deadline:
                break
            time.sleep(min(1, max(0, deadline - time.monotonic())))
        with storage.control_lock():
            base, lease = matching(run, attempt)
            lease['phase'] = 'drained' if observed is not None and not any(observed.values()) else 'reset-approved'
            lease['session_observation'] = observed
            storage.write(base / 'lease', lease)
        return {'phase': lease['phase'], 'session_observation': observed}
    except Exception:
        # Even an unavailable observer leaves the durable gate closed.
        with storage.control_lock():
            base, lease = matching(run, attempt)
            lease['phase'] = 'drain-failed'
            storage.write(base / 'lease', lease)
        raise


def prepare_legacy(action, digest, config):
    """Called after pre-pull/Compose validation, immediately before replacement."""
    run, attempt = os.environ.get('POKERATTACK_RELEASE_RUN', ''), os.environ.get('POKERATTACK_RELEASE_ATTEMPT', '')
    host_manifests.pattern(run, r'[1-9][0-9]{0,19}')
    host_manifests.pattern(attempt, r'[1-9][0-9]{0,4}')
    with storage.control_lock():
        base, lease = matching(run, attempt)
        if action != lease.get('replacement_action') or lease['phase'] != 'deploying' or lease['digest'] != digest or lease['config_sha256'] != config:
            raise ValueError('Replacement differs from approved lease')
        admission = storage.read(base / 'admission/state.json', 0o644)
        if admission.get('active') is not True or any(admission.get(key) != lease.get(key) for key in
                ('run_id', 'run_attempt', 'digest', 'config_sha256', 'source_sha')):
            raise ValueError('Admission state differs from approved lease')
        fresh(lease['acknowledgment'])
        image = 'chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi@' + digest
        inspected = subprocess.run(['docker', 'image', 'inspect', image, '--format', '{{json .}}'], capture_output=True, timeout=15, check=True)
        info = json.loads(inspected.stdout)
        if info['Config'].get('Labels', {}).get('org.opencontainers.image.revision') != lease['source_sha'] or info['Os'] != 'linux' or info['Architecture'] != 'amd64':
            raise ValueError('Candidate source/platform mismatch')
        if info['Id'] != lease['manifest']['api']['local_image_id']:
            raise ValueError('Candidate image differs from tested image')
        if info['Config'].get('Labels', {}).get('com.chefknifestudios.pokerattack.release-capability') != '1':
            raise ValueError('Target lacks the rehearsed maintenance/verifier capability')
        schema_ready(lease['manifest'])
        validator.config('production', CONFIG / 'production/configs' / (config + '.env'), config)
        # Freshness belongs to the reviewed approval, not the marker mtime.
        storage.write_bytes(CONFIG / 'production/maintenance', b'sessions-reset-approved\n')


def deploy(digest, config, run, attempt, action='deploy'):
    with storage.control_lock():
        base, lease = matching(run, attempt)
        if lease['digest'] != digest or lease['config_sha256'] != config:
            raise ValueError('Wrong deployment identity')
        if lease.get('replacement_action') != action:
            raise ValueError('Wrong replacement action')
        live = status_module.status('production')
        if live['transaction'] or not live['consistent']:
            raise ValueError('Inspect interrupted outcome before retry')
        if lease['phase'] == 'pending' and live['readiness']['ready'] and live['pairs']['active'] and live['pairs']['active'] == live['pairs']['pending'] and live['pairs']['active']['digest'] == digest and live['pairs']['active']['config_sha256'] == config:
            return {'phase': 'pending', 'idempotent': True}
        if lease['phase'] not in ('drained', 'reset-approved') or action == 'deploy' and live['pairs']['pending']:
            raise ValueError('Unsafe deployment/retry phase')
        fresh(lease['acknowledgment'])
        lease['phase'] = 'deploying'
        storage.write(base / 'lease', lease)
    environment = {**os.environ, 'POKERATTACK_RELEASE_RUN': run, 'POKERATTACK_RELEASE_ATTEMPT': attempt}
    try:
        arguments = [LEGACY, 'deploy', 'production', digest, config] if action == 'deploy' else [LEGACY, 'rollback', 'production']
        result = subprocess.run(arguments, stdin=subprocess.DEVNULL,
                                capture_output=True, timeout=900, env=environment)
        code = result.returncode
    except subprocess.TimeoutExpired:
        code = 124
    with storage.control_lock():
        _, lease = matching(run, attempt)
        live = status_module.status('production')
        pair = live['pairs']['active']
        passed = code == 0 and live['consistent'] and not live['transaction'] and live['readiness']['ready'] and (
            pair and pair == live['pairs']['pending'] and pair['digest'] == digest and pair['config_sha256'] == config)
        lease['phase'] = 'pending' if passed else 'unknown' if code == 124 or live['transaction'] else 'failed'
        lease['last_exit_code'] = code
        storage.write(base / 'lease', lease)
        if not passed:
            raise ValueError('Release failed or outcome unknown; maintenance retained')
        return {'phase': 'pending', 'digest': digest, 'config_sha256': config}


def rollback(run, attempt):
    with storage.control_lock():
        _, lease = matching(run, attempt)
        digest, config = lease['digest'], lease['config_sha256']
        if lease['acknowledgment']['action'] != 'rollback':
            raise ValueError('Fresh explicit rollback approval required')
        action = lease['replacement_action']
    return deploy(digest, config, run, attempt, action=action)


def resume(run, old_attempt, new_attempt):
    if int(new_attempt) <= int(old_attempt):
        raise ValueError('Resume requires a later attempt')
    with storage.control_lock(), storage.deployment_lock():
        base, lease = matching(run, old_attempt)
        live = status_module.status('production')
        active = live['pairs']['active']
        if not live['consistent'] or live['transaction'] or not active or not live['readiness']['ready']:
            raise ValueError('Uncertain runtime requires inspection')
        if active['digest'] != lease['digest'] or active['config_sha256'] != lease['config_sha256']:
            raise ValueError('Cannot resume an uninstalled or different candidate')
        if active == live['pairs']['pending']:
            phase = 'pending'
        elif active == live['pairs']['known-good'] and not live['pairs']['pending'] and lease['phase'] in ('promoted', 'promoted-needs-verification', 'promoting', 'promotion-unknown'):
            phase = 'promoted-needs-verification'
        else:
            raise ValueError('Candidate outcome is not established')
        old = {**lease}
        admission = storage.read(base / 'admission/state.json', 0o644)
        if 'verifier' in admission:
            admission['verifier']['revoked'] = True
        admission['run_attempt'] = int(new_attempt)
        admission['active'] = True
        lease.update(run_attempt=int(new_attempt), phase=phase, verifier_issued=False, verifier_revoked=False)
        if phase == 'promoted-needs-verification':
            lease['previously_promoted'] = True
        for field in ('evidence_sha256', 'verifier_id'):
            lease.pop(field, None)
        storage.write(base / 'interrupted' / f'{run}-{old_attempt}-resumed-as-{new_attempt}.json', old)
        # Revoke the old verifier before transferring the private lease owner.
        storage.write(base / 'admission/state.json', admission, 0o644)
        storage.write(base / 'lease', lease)
        return {'phase': phase, 'run_id': int(run), 'run_attempt': int(new_attempt), 'reset_approval_refreshed': False}


def finish(run, attempt):
    with storage.control_lock():
        base = storage.STATE / 'production'
        archive = base / 'completed' / f'{run}-{attempt}.json'
        if not (base / 'lease').exists() and not (base / 'lease').is_symlink():
            completed = storage.read(archive)
            live = status_module.status('production')
            good = live['pairs']['known-good']
            admission = storage.read(base / 'admission/state.json', 0o644)
            if (completed.get('run_id') != int(run) or completed.get('run_attempt') != int(attempt) or
                completed.get('phase') != 'promoted' or admission.get('active') is not False or
                not live['consistent'] or live['transaction'] or live['pairs']['pending'] or not live['readiness']['ready'] or
                not good or good != live['pairs']['active'] or good['digest'] != completed.get('digest') or good['config_sha256'] != completed.get('config_sha256')):
                raise ValueError('Completed release no longer matches runtime')
            return {'phase': 'reopened', 'digest': good['digest'], 'config_sha256': good['config_sha256'], 'idempotent': True}
        base, lease = matching(run, attempt)
        if lease['phase'] != 'promoted' or lease.get('verifier_revoked') is not True:
            raise ValueError('Promotion/revocation incomplete')
        if lease.get('bootstrap') and (lease.get('bootstrap_publication_intent') or not lease.get('bootstrap_publications',{}).get('candidate')):
            raise ValueError('Bootstrap publication unresolved')
        live = status_module.status('production')
        good = live['pairs']['known-good']
        if not live['consistent'] or live['transaction'] or live['pairs']['pending'] or not live['readiness']['ready'] or (
            not good or live['pairs']['active'] != good or good['digest'] != lease['digest'] or good['config_sha256'] != lease['config_sha256']):
            raise ValueError('Promoted identity not ready')
        if any(counts().values()):
            raise ValueError('Verifier sessions must close before reopening')
        admission = storage.read(base / 'admission/state.json', 0o644)
        if admission.get('active') is not True or any(admission.get(key) != lease.get(key) for key in
                ('run_id', 'run_attempt', 'digest', 'config_sha256', 'source_sha')):
            raise ValueError('Admission state differs from promoted lease')
        # Archive before reopening and preserve the previous closed state so an
        # unsuccessful finalization can close admission again. A failure of that
        # compensating write is an unknown state requiring diagnostic inspection.
        if archive.exists() and storage.read(archive) != lease:
            raise ValueError('Completed identity already exists with different bytes')
        storage.write(archive, lease)
        closed = storage.read(base / 'admission/state.json', 0o644)
        closed['active'] = True
        try:
            storage.write(base / 'admission/state.json', {'active': False}, 0o644)
            (base / 'lease').unlink()
        except Exception:
            storage.write(base / 'admission/state.json', closed, 0o644)
            raise
        return {'phase': 'reopened', 'digest': good['digest'], 'config_sha256': good['config_sha256']}
