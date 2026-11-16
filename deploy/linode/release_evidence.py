"""Protected evidence import and separately authorized promotion.

The protected verifier job supplies assertion truth and served-client identity.
The host binds that report to immutable build provenance and inspected runtime;
it never manufactures check results or refreshes a verification timestamp.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

import host_manifests
import secure_state as storage
from release_verifier import matching, status_module

CONFIG = Path('/etc/pokerattack')
LEGACY = '/usr/local/sbin/pokerattack-deploy'


def canonical(document):
    return json.dumps(document, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def identity(base, lease, phases):
    if lease.get('phase') not in phases:
        raise ValueError('Wrong verification/promotion phase')
    manifest = host_manifests.server(lease['manifest'])
    if hashlib.sha256(canonical(manifest)).hexdigest() != lease.get('manifest_sha256'):
        raise ValueError('Build manifest changed')
    if any(lease.get(key) != expected for key, expected in (
            ('source_sha', manifest['source_sha']), ('digest', manifest['api']['digest']),
            ('config_sha256', manifest['config_sha256']))):
        raise ValueError('Lease differs from immutable build')
    live = status_module.status('production')
    pair = live['pairs']['active']
    promoted = lease.get('previously_promoted') is True or lease['phase'] == 'promoted'
    expected = live['pairs']['known-good'] if promoted else live['pairs']['pending']
    if (not live['consistent'] or live['transaction'] or not live['readiness']['ready'] or not pair or
            pair != expected or promoted and live['pairs']['pending'] or
            pair['digest'] != lease['digest'] or pair['config_sha256'] != lease['config_sha256']):
        raise ValueError('Active candidate changed or is not ready')
    runtime = live['runtime']
    if runtime['source_revision'] != manifest['source_sha'] or runtime['image_id'] != manifest['api']['local_image_id']:
        raise ValueError('Inspected image differs from tested source/image')
    admission = storage.read(base / 'admission/state.json', 0o644)
    if admission.get('active') is not True or any(admission.get(key) != lease.get(key) for key in
            ('run_id', 'run_attempt', 'digest', 'config_sha256', 'source_sha')):
        raise ValueError('Maintenance context changed')
    return live, admission


def validate(base, lease, document, phases):
    document = host_manifests.evidence(document)
    live, admission = identity(base, lease, phases)
    manifest = lease['manifest']
    expected = {'source_sha': manifest['source_sha'], 'digest': manifest['api']['digest'],
                'migration_digest': manifest['migration']['digest'], 'config_sha256': manifest['config_sha256'],
                'verifier_id': lease.get('verifier_id')}
    if any(document[key] != value for key, value in expected.items()):
        raise ValueError('Evidence differs from build/verifier identity')
    if lease.get('bootstrap'):
        published=lease.get('bootstrap_publications',{}).get('candidate',{}).get('packet',{}).get('client')
        if (lease.get('bootstrap_publication_intent') or not published or
                document['client_source_sha']!=published['source_sha'] or document['client_archive_sha256']!=published['archive_sha256']):
            raise ValueError('Evidence differs from the recorded bootstrap client')
    workflow = document['workflow']
    if workflow['run_id'] != lease['run_id'] or workflow['run_attempt'] != lease['run_attempt']:
        raise ValueError('Evidence belongs to another execution attempt')
    verifier = admission.get('verifier', {})
    if (lease.get('verifier_issued') is not True or verifier.get('id') != document['verifier_id'] or
            type(verifier.get('issued_unix')) is not int or type(verifier.get('expires_unix')) is not int or
            not verifier['issued_unix'] <= document['verified_unix'] <= verifier['expires_unix'] or
            not 0 <= time.time() - document['verified_unix'] < 1800):
        raise ValueError('Evidence expired or outside verifier lifetime')
    return live


def import_evidence(run, attempt, raw):
    # Preserve and hash exact submitted bytes; no caller path or hash is accepted.
    document = storage.bounded_json(raw)
    with storage.control_lock(), storage.deployment_lock():
        base, lease = matching(run, attempt)
        validate(base, lease, document, ('pending', 'promoted-needs-verification'))
        digest = hashlib.sha256(raw).hexdigest()
        if lease.get('evidence_sha256') not in (None, digest):
            raise ValueError('This attempt already imported different evidence')
        directory = CONFIG / 'production/verification'
        storage.trusted_parents(directory / 'placeholder')
        path = directory / (digest + '.json')
        if path.exists() or path.is_symlink():
            if storage.read(path) != document or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
                raise ValueError('Existing evidence bytes differ')
        else:
            storage.write_bytes(path, raw)
        lease['evidence_sha256'] = digest
        storage.write(base / 'lease', lease)
        return {'evidence_sha256': digest, 'phase': lease['phase']}


def saved(base, lease, evidence_hash, phases):
    host_manifests.pattern(evidence_hash, r'[a-f0-9]{64}')
    if lease.get('evidence_sha256') != evidence_hash:
        raise ValueError('Evidence was not imported for this attempt')
    path = CONFIG / 'production/verification' / (evidence_hash + '.json')
    document = storage.read(path)
    if hashlib.sha256(path.read_bytes()).hexdigest() != evidence_hash:
        raise ValueError('Evidence bytes changed')
    return document, validate(base, lease, document, phases)


def legacy_guard(digest, config, evidence_hash):
    """Called only while legacy holds deploy.lock; acquire control in this child.

    The controller releases control before invoking legacy. No other production
    helper can put a lease into promoting or obtain the promotion key.
    """
    run = os.environ.get('POKERATTACK_RELEASE_RUN', '')
    attempt = os.environ.get('POKERATTACK_RELEASE_ATTEMPT', '')
    host_manifests.pattern(run, r'[1-9][0-9]{0,19}')
    host_manifests.pattern(attempt, r'[1-9][0-9]{0,4}')
    with storage.control_lock():
        base, lease = matching(run, attempt)
        if lease['digest'] != digest or lease['config_sha256'] != config:
            raise ValueError('Wrong promotion pair')
        saved(base, lease, evidence_hash, ('promoting',))


def promote(digest, evidence_hash, run, attempt):
    with storage.control_lock(), storage.deployment_lock():
        base, lease = matching(run, attempt)
        if lease['digest'] != digest:
            raise ValueError('Wrong promotion digest')
        _, live = saved(base, lease, evidence_hash, ('pending', 'promoted-needs-verification', 'promoted'))
        if lease['phase'] == 'promoted':
            return {'phase': 'promoted', 'evidence_sha256': evidence_hash, 'idempotent': True}
        # Preserve original manifest alongside the exact image/config snapshot.
        # Reverification does not rewrite original build provenance.
        release = (base / 'active').resolve(strict=True)
        storage.trusted_parents(release / 'release-manifest.json')
        if release.parent != (base / 'releases').resolve(strict=True):
            raise ValueError('Invalid release snapshot')
        manifest_path = release / 'release-manifest.json'
        if manifest_path.exists() or manifest_path.is_symlink():
            if storage.read(manifest_path) != lease['manifest']:
                raise ValueError('Snapshot build provenance differs')
        else:
            storage.write(manifest_path, lease['manifest'])
        already_good = lease['phase'] == 'promoted-needs-verification'
        lease['phase'] = 'promoting'
        storage.write(base / 'lease', lease)
    # Legacy has its own nonblocking host lock. Do not hold it across this call.
    code = 0
    if not already_good:
        environment = {**os.environ, 'POKERATTACK_RELEASE_RUN': run, 'POKERATTACK_RELEASE_ATTEMPT': attempt}
        try:
            code = subprocess.run([LEGACY, 'promote', 'production', digest, evidence_hash],
                                  stdin=subprocess.DEVNULL, capture_output=True, timeout=90, env=environment).returncode
        except subprocess.TimeoutExpired:
            code = 124
    with storage.control_lock(), storage.deployment_lock():
        base, lease = matching(run, attempt)
        # A partial/uncertain promotion never reopens maintenance. Explicit resume
        # must inspect established pointers and create fresh evidence/review.
        live = status_module.status('production')
        good = live['pairs']['known-good']
        passed = (code == 0 and live['consistent'] and not live['transaction'] and live['readiness']['ready'] and
                  not live['pairs']['pending'] and good and live['pairs']['active'] == good and
                  good['digest'] == digest and good['config_sha256'] == lease['config_sha256'] and
                  live['runtime']['source_revision'] == lease['source_sha'] and
                  live['runtime']['image_id'] == lease['manifest']['api']['local_image_id'])
        lease['phase'] = 'promoted' if passed else 'promotion-unknown'
        storage.write(base / 'lease', lease)
        if not passed:
            raise ValueError('Promotion failed or outcome unknown; inspect status')
        return {'phase': 'promoted', 'evidence_sha256': evidence_hash}
