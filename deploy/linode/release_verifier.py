"""One-shot verifier issuance/revocation. Never persists plaintext credentials."""
import hashlib
import importlib.util
from pathlib import Path
import re
import secrets
import time

import secure_state as storage

spec = importlib.util.spec_from_file_location('release_status', Path(__file__).with_name('release-status.py'))
status_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(status_module)


def matching(run, attempt):
    base = storage.STATE / 'production'
    lease = storage.read(base / 'lease')
    if lease.get('kind') != 'server' or type(lease.get('run_id')) is not int or type(lease.get('run_attempt')) is not int or lease.get('run_id') != int(run) or lease.get('run_attempt') != int(attempt):
        raise ValueError('Wrong lease owner')
    return base, lease


def issue(run, attempt, request):
    with storage.control_lock():
        base, lease = matching(run, attempt)
        if lease.get('bootstrap') and (lease.get('bootstrap_publication_intent') or not lease.get('bootstrap_publications',{}).get('candidate')):
            raise ValueError('Verify bootstrap client publication before issuing its verifier')
        if lease.get('phase') not in ('pending', 'promoted-needs-verification') or lease.get('verifier_issued') or lease.get('verifier_revoked'):
            raise ValueError('Verifier issuance/renewal refused')
        live = status_module.status('production')
        if not live['consistent'] or live['transaction'] or not live['readiness']['ready']:
            raise ValueError('Unresolved runtime state')
        active, pending = live['pairs']['active'], live['pairs']['pending']
        expected = pending if lease['phase'] == 'pending' else live['pairs']['known-good'] if pending is None else None
        if not active or active != expected or active['digest'] != lease.get('digest') or active['config_sha256'] != lease.get('config_sha256'):
            raise ValueError('Wrong pending pair')
        if live['runtime']['source_revision'] != lease.get('source_sha') or not re.fullmatch(r'[a-f0-9]{40}', lease.get('source_sha', '')):
            raise ValueError('Wrong pending source')
        if not isinstance(request, dict) or set(request) != {'players'} or not isinstance(request['players'], list) or len(request['players']) != 3:
            raise ValueError('Expected three scoped verifier identities')
        players = request['players']
        for index, player in enumerate(players):
            if not isinstance(player, dict) or set(player) != {'id', 'name'}:
                raise ValueError('Invalid verifier player')
            if not isinstance(player['id'], str) or not re.fullmatch(r'(?:[a-f0-9]{32}|[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12})', player['id']):
                raise ValueError('Invalid verifier player ID')
            if player['name'] != f'Release-{run}-{attempt}-{index}':
                raise ValueError('Unbound verifier name')
        if len({p['id'] for p in players}) != 3:
            raise ValueError('Duplicate verifier player')
        admission = storage.read(base / 'admission/state.json', 0o644)
        if admission.get('active') is not True or any(admission.get(key) != lease.get(key) for key in
                ('run_id', 'run_attempt', 'digest', 'config_sha256', 'source_sha')):
            raise ValueError('Wrong admission context')
        secret = secrets.token_hex(32)
        verifier_id = secrets.token_hex(16)
        now = int(time.time())
        lease['verifier_issued'] = True
        lease['verifier_revoked'] = False
        lease['verifier_id'] = verifier_id
        # Record issuance first: a lost/crashed response cannot mint a second
        # credential or silently advance its expiration on the same attempt.
        storage.write(base / 'lease', lease)
        admission['verifier'] = {'id': verifier_id, 'token_sha256': hashlib.sha256(secret.encode()).hexdigest(),
                                 'issued_unix': now, 'expires_unix': now + 900, 'revoked': False, 'players': players}
        storage.write(base / 'admission/state.json', admission, 0o644)
        return {'credential': f'{run}.{attempt}.{secret}', 'verifier_id': verifier_id, 'expires_unix': now + 900,
                'players': players}


def revoke(run, attempt):
    with storage.control_lock():
        base, lease = matching(run, attempt)
        admission = storage.read(base / 'admission/state.json', 0o644)
        if admission.get('run_id') != int(run) or admission.get('run_attempt') != int(attempt):
            raise ValueError('Wrong admission owner')
        if 'verifier' in admission:
            admission['verifier']['revoked'] = True
            storage.write(base / 'admission/state.json', admission, 0o644)
        lease['verifier_revoked'] = True
        storage.write(base / 'lease', lease)
        return {'revoked': True, 'run_id': int(run), 'run_attempt': int(attempt)}
