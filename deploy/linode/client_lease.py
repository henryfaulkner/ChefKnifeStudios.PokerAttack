"""Exclusive client publication slot. No API lifecycle or maintenance authority."""
import hashlib
import json
import time
import urllib.request

import host_manifests
import secure_state as storage
from release_verifier import status_module


def canonical(document):
    return json.dumps(document, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def available():
    live = status_module.status('production')
    pair = live['pairs']['known-good']
    if (not live['consistent'] or live['transaction'] or live['pairs']['pending'] or
            not live['readiness']['ready'] or not pair or pair != live['pairs']['active'] or
            live['maintenance']['active'] is not False):
        raise ValueError('Unresolved server/maintenance state blocks publication')
    return live


def capability():
    with urllib.request.urlopen('http://127.0.0.1:8081/release/status', timeout=5) as response:
        raw = response.read(4097)
        if response.status != 200 or len(raw) > 4096:
            raise ValueError('Unavailable compatibility prerequisite')
    document = storage.bounded_json(raw)
    if type(document.get('capability')) is not int or document['capability'] < 1 or document.get('maintenance') is not False:
        raise ValueError('Server baseline not compatible/available')
    return document['capability']


def owner(base, run, attempt):
    lease = storage.read(base / 'lease')
    if lease.get('kind') != 'client' or type(lease.get('run_id')) is not int or type(lease.get('run_attempt')) is not int or (
            lease['run_id'] != int(run) or lease['run_attempt'] != int(attempt)):
        raise ValueError('Another run owns the publication slot')
    return lease


def begin(run, attempt, packet, recover=False):
    host_manifests.fields(packet, ('candidate', 'recovery', 'recovery_from') if recover else ('candidate', 'recovery'))
    candidate = host_manifests.client_identity(packet['candidate'])
    recovery = host_manifests.client_identity(packet['recovery']) if packet['recovery'] is not None else None
    packet_hash = hashlib.sha256(canonical(packet)).hexdigest()
    base = storage.STATE / 'production'
    with storage.control_lock(), storage.deployment_lock():
        live = available()
        prior_to_archive = None
        path = base / 'lease'
        if path.exists() or path.is_symlink():
            prior = storage.read(path)
            if prior.get('kind') != 'client': raise ValueError('Server owns the slot; client recovery cannot take it')
            if prior.get('run_id') == int(run) and prior.get('run_attempt') == int(attempt):
                lease = owner(base, run, attempt)
                if lease['packet_sha256'] != packet_hash or live['pairs']['known-good'] != lease['server_pair']:
                    raise ValueError('Publication slot identity differs; inspect before retry')
                return {'phase': 'client-publishing', 'idempotent': True}
            if not recover:
                raise ValueError('Another client run owns the slot')
            host_manifests.integer(prior.get('run_id'), 10**20 - 1)
            host_manifests.integer(prior.get('run_attempt'), 99999)
            host_manifests.pattern(prior.get('client_archive_sha256'), r'[a-f0-9]{64}')
            expected = {key: prior[key] for key in ('run_id', 'run_attempt', 'client_archive_sha256')}
            if (packet['recovery_from'] != expected or any(type(packet['recovery_from'][key]) is not type(value) for key, value in expected.items()) or
                    prior.get('recovery') is None or candidate != prior['recovery'] or
                    recovery not in (prior['recovery'], prior['candidate']) or live['pairs']['known-good'] != prior['server_pair']):
                raise ValueError('Recovery must explicitly restore the interrupted slot recorded predecessor')
            prior_to_archive = prior
        elif recover:
            raise ValueError('No interrupted client slot to recover')
        if (base / 'client-completed' / f'{run}-{attempt}.json').exists():
            raise ValueError('Completed client attempt cannot be reused')
        current_capability = capability()
        if any(identity['minimum_server_capability'] > current_capability for identity in (candidate, recovery) if identity is not None):
            # Fail instead of waiting while holding either GitHub/host slot for
            # a server release that could never acquire it.
            raise ValueError('Required promoted server capability is not installed')
        lease = {'kind': 'client', 'phase': 'client-publishing', 'run_id': int(run), 'run_attempt': int(attempt),
                 'source_sha': candidate['source_sha'], 'client_archive_sha256': candidate['archive_sha256'],
                 'artifact_id': candidate['artifact_id'], 'minimum_server_capability': candidate['minimum_server_capability'],
                 'client_source_run_id': candidate['workflow']['run_id'],
                 'client_source_run_attempt': candidate['workflow']['run_attempt'],
                 'candidate': candidate, 'recovery': recovery, 'packet_sha256': packet_hash,
                 'server_pair': live['pairs']['known-good'], 'digest': live['pairs']['known-good']['digest'],
                 'config_sha256': live['pairs']['known-good']['config_sha256']}
        if recover: lease['recovery_from'] = packet['recovery_from']
        if recovery is not None:
            lease.update(client_recovery_run_id=recovery['workflow']['run_id'],
                         client_recovery_run_attempt=recovery['workflow']['run_attempt'],
                         client_recovery_artifact_id=recovery['artifact_id'],
                         client_recovery_archive_sha256=recovery['archive_sha256'])
        if prior_to_archive is not None:
            prior = prior_to_archive
            storage.write(base / 'interrupted' / f"client-{prior['run_id']}-{prior['run_attempt']}-recovered-by-{run}-{attempt}.json", prior)
        storage.write(path, lease)
        return {'phase': lease['phase'], 'client_archive_sha256': candidate['archive_sha256']}


def resume(run, old_attempt, new_attempt):
    if int(new_attempt) <= int(old_attempt): raise ValueError('Client resume requires a later attempt')
    base = storage.STATE / 'production'
    archive = base / 'interrupted' / f'client-{run}-{old_attempt}-resumed-as-{new_attempt}.json'
    with storage.control_lock(), storage.deployment_lock():
        lease = storage.read(base / 'lease')
        live = available()
        if (lease.get('kind') != 'client' or type(lease.get('run_id')) is not int or type(lease.get('run_attempt')) is not int or
                lease.get('run_id') != int(run) or live['pairs']['known-good'] != lease.get('server_pair')):
            raise ValueError('Cannot resume another or uncertain publication')
        if lease.get('run_attempt') == int(new_attempt):
            prior = storage.read(archive)
            if prior.get('run_attempt') != int(old_attempt) or prior.get('packet_sha256') != lease['packet_sha256']:
                raise ValueError('Resume provenance differs')
            return {'phase': lease['phase'], 'idempotent': True}
        if lease.get('run_attempt') != int(old_attempt): raise ValueError('Wrong client attempt')
        storage.write(archive, lease)
        lease['run_attempt'] = int(new_attempt)
        storage.write(base / 'lease', lease)
        # No upload/rebuild/result assertion occurs here. The protected job must
        # reconcile actual served identity before deciding whether to upload.
        return {'phase': lease['phase'], 'client_archive_sha256': lease['client_archive_sha256']}


def finish(run, attempt, packet):
    host_manifests.fields(packet, ('outcome', 'client', 'verified_unix', 'checks'))
    identity = host_manifests.client_identity(packet['client'])
    if packet['outcome'] not in ('accepted', 'restored'):
        raise ValueError('Only verified publication/restoration can finish')
    if type(packet['verified_unix']) is not int or not 0 <= time.time() - packet['verified_unix'] < 1800:
        raise ValueError('Client verification is not fresh')
    host_manifests.fields(packet['checks'], ('https', 'served_client', 'settings', 'header', 'assets', 'browser', 'signalr'))
    if any(value is not True for value in packet['checks'].values()): raise ValueError('Client verification incomplete')
    packet_hash = hashlib.sha256(canonical(packet)).hexdigest()
    base = storage.STATE / 'production'
    archive = base / 'client-completed' / f'{run}-{attempt}.json'
    with storage.control_lock(), storage.deployment_lock():
        live = available()
        path = base / 'lease'
        if not path.exists() and not path.is_symlink():
            completed = storage.read(archive)
            if (completed.get('kind') != 'client' or completed.get('run_id') != int(run) or
                    completed.get('run_attempt') != int(attempt) or completed.get('finish_sha256') != packet_hash or
                    completed.get('server_pair') != live['pairs']['known-good']):
                raise ValueError('Completed publication no longer matches runtime/result')
            return {'phase': 'client-finished', 'outcome': completed['outcome'], 'idempotent': True}
        lease = owner(base, run, attempt)
        expected = lease['candidate'] if packet['outcome'] == 'accepted' else lease['recovery']
        if expected is None or identity != expected or live['pairs']['known-good'] != lease['server_pair']:
            raise ValueError('Verified client/server differs from reserved publication')
        completed = {**lease, 'phase': 'client-finished', 'outcome': packet['outcome'], 'finish_sha256': packet_hash,
                     'verified_unix': packet['verified_unix']}
        if archive.exists() and storage.read(archive) != completed:
            raise ValueError('Completed client identity already differs')
        storage.write(archive, completed)
        path.unlink()
        return {'phase': 'client-finished', 'outcome': packet['outcome'], 'client_archive_sha256': identity['archive_sha256']}
