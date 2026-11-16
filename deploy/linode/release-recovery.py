#!/usr/bin/env python3
"""Read only the internally selected recorded recovery manifest; no caller paths."""
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import host_manifests
import secure_state as storage
from release_lease import recovery_target
from release_verifier import status_module


def target():
    base = storage.STATE / 'production'
    with storage.control_lock(), storage.deployment_lock():
        live = status_module.status('production')
        if not live['consistent'] or live['transaction']:
            raise ValueError('Unresolved transaction blocks target selection')
        prior = storage.read(base / 'lease') if (base / 'lease').exists() or (base / 'lease').is_symlink() else None
        if prior and prior.get('kind') != 'server':
            raise ValueError('Client publication owns the slot')
        pair, action = recovery_target(live, prior)
        if not pair or pair.get('snapshot_config_matches') is not True:
            raise ValueError('No intact recorded recovery pair')
        pointer = 'known-good' if pair == live['pairs']['known-good'] else 'previous-good'
        release = (base / pointer).resolve(strict=True)
        if release.parent != (base / 'releases').resolve(strict=True):
            raise ValueError('Invalid protected recovery pointer')
        manifest = host_manifests.server(storage.read(release / 'release-manifest.json'))
        if manifest['api']['digest'] != pair['digest'] or manifest['config_sha256'] != pair['config_sha256']:
            raise ValueError('Recovery manifest differs from recorded pair')
        return {'format':'pokerattack-rollback-target-v1','protocol_version':1,'target':'production',
            'sampled_utc':live['sampled_utc'],'current_pair':live['pairs']['active'],
            'pending_pair':live['pairs']['pending'],'recovery_pair':pair,'replacement_action':action,
            'recovery_from':{key:prior[key] for key in ('run_id','run_attempt','digest','config_sha256')} if prior else None,
            'observed_sessions':live.get('sessions'),'manifest':manifest,
            'manifest_sha256':hashlib.sha256(json.dumps(manifest,sort_keys=True,separators=(',',':')).encode()).hexdigest()}


if __name__=='__main__':
    try:
        if len(sys.argv)!=1:raise ValueError('No caller arguments permitted')
        raw=json.dumps(target(),separators=(',',':'))
        if len(raw.encode())>65536:raise ValueError('Recovery response exceeds its bound')
        print(raw)
    except Exception:
        sys.exit('Recorded rollback target unavailable. Inspect status or use the separately rehearsed root bootstrap recovery path; no state changed.')
