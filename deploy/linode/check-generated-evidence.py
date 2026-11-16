"""Accept actual browser-generated fixture proof in socket-free root Linux.

Fixture metadata is explicitly synthetic, not a published production release.
No Docker socket, SSH, network, database or installed host path is used here.
"""
import hashlib
import json
from pathlib import Path
import shutil
import sys
import uuid

import release_evidence as evidence
import release_verifier as verifier
import secure_state as storage

report = storage.bounded_json(Path(sys.argv[1]).read_bytes())
assert report['fixture_only'] is True and report['tls_verification'] is True
assert report['result']['passed'] is True
manifest = report['server_manifest']
assert report['image_id'] == manifest['api']['local_image_id']
assert report['image_revision'] == manifest['source_sha']
parent = Path('/generated-evidence-tests')
parent.mkdir(mode=0o700, exist_ok=True)
root = parent / uuid.uuid4().hex
base = root / 'state/production'
try:
    for directory in (base / 'admission', root / 'etc/production/verification'):
        directory.mkdir(mode=0o700, parents=True)
    storage.STATE = root / 'state'
    evidence.CONFIG = root / 'etc'
    proof = report['result']['evidence']
    lease = {'kind': 'server', 'phase': 'pending', 'run_id': proof['workflow']['run_id'],
             'run_attempt': proof['workflow']['run_attempt'], 'source_sha': manifest['source_sha'],
             'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256'],
             'manifest': manifest, 'manifest_sha256': hashlib.sha256(evidence.canonical(manifest)).hexdigest(),
             'verifier_issued': True, 'verifier_id': proof['verifier_id']}
    storage.write(base / 'lease', lease)
    storage.write(base / 'admission/state.json', {**{key: lease[key] for key in
        ('run_id', 'run_attempt', 'source_sha', 'digest', 'config_sha256')},
        'active': True, 'verifier': {**report['verifier_window'], 'revoked': False}}, 0o644)
    pair = {'digest': lease['digest'], 'config_sha256': lease['config_sha256']}
    verifier.status_module.status = lambda _: {'consistent': True, 'transaction': False,
        'readiness': {'ready': True}, 'pairs': {'active': pair, 'pending': pair},
        'runtime': {'image_id': report['image_id'], 'source_revision': report['image_revision']}}
    raw = json.dumps(proof, separators=(',', ':')).encode()
    result = evidence.import_evidence(str(lease['run_id']), str(lease['run_attempt']), raw)
    assert result['evidence_sha256'] == hashlib.sha256(raw).hexdigest()
    stored = root / 'etc/production/verification' / (result['evidence_sha256'] + '.json')
    assert stored.read_bytes() == raw and stored.stat().st_mode & 0o777 == 0o600
    verifier.revoke(str(lease['run_id']), str(lease['run_attempt']))
    admission = storage.read(base / 'admission/state.json', 0o644)
    assert admission['active'] is True and admission['verifier']['revoked'] is True
    print(json.dumps({'fixture_only': True, 'generated_browser_evidence_accepted': True,
                      'evidence_sha256': result['evidence_sha256'], 'root_mode_0600': True,
                      'verifier_revoked': True, 'maintenance_retained': True}))
finally:
    assert root.parent == parent and len(root.name) == 32
    shutil.rmtree(root)
