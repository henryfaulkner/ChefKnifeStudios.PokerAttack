"""Separate explicit promotion decision, bound to fresh imported evidence bytes.

This stage never deploys, rewrites evidence time or automatically opens admission.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import time

from client_publish import github, trusted_context
from manifests import load_document, validate_document
from review import approved, canonical
from server_deliver import owned
from server_verify import pending
from server_images import execution
from ssh_client import transport


def promote(manifest, raw, review, env, status_host, promotion_host, api=github):
    trusted_context(env)
    _, workflow = execution(env)
    proof = validate_document('evidence', load_document(raw))
    validate_document('server-manifest', manifest)
    digest = hashlib.sha256(raw).hexdigest()
    expected = {'workflow': workflow, 'digest': manifest['api']['digest'], 'migration_digest': manifest['migration']['digest'],
                'config_sha256': manifest['config_sha256'], 'source_sha': manifest['source_sha']}
    if any(proof[key] != value for key, value in expected.items()):
        raise ValueError('Reviewed evidence differs from this execution/manifest')
    comment = f"PROMOTE {digest} {workflow['run_id']}/{workflow['run_attempt']}"
    if (set(review) != {'format', 'evidence_sha256', 'evidence', 'manifest_sha256', 'comment', 'overall_release_complete', 'expires_unix'}
            or review['format'] != 'pokerattack-promotion-review-v1' or review['evidence'] != proof
            or review['evidence_sha256'] != digest or review['comment'] != comment
            or review['manifest_sha256'] != hashlib.sha256(canonical(manifest)).hexdigest()
            or review['overall_release_complete'] is not False or review['expires_unix'] != proof['verified_unix'] + 1800):
        raise ValueError('Promotion review does not match immutable evidence bytes')
    def fresh():
        if not 0 <= time.time() - proof['verified_unix'] < 1800:
            raise ValueError('Evidence expired; rerun verification and its dependent review, never deploy another candidate or rewrite time')
    fresh()
    run, attempt = workflow['run_id'], workflow['run_attempt']
    decision = approved(api(f'actions/runs/{run}/approvals'), 'server-promotion', comment)
    live = status_host.call('status production')
    lease = pending(live, manifest, run, attempt)
    if lease.get('evidence_sha256') != digest or lease.get('verifier_revoked') is not True:
        raise ValueError('Matching evidence import/revocation is not established')
    fresh()
    result = promotion_host.call(f"promote production {manifest['api']['digest']} {digest} {run} {attempt}", timeout=150)
    live = status_host.call('status production')
    lease = owned(live, {'manifest': {**manifest, 'workflow': workflow}})
    good = live['pairs']['known-good']
    if (result.get('phase') != 'promoted' or lease['phase'] != 'promoted' or lease.get('evidence_sha256') != digest
            or not good or live['pairs']['pending'] or live['pairs']['active'] != good
            or good['digest'] != manifest['api']['digest'] or good['config_sha256'] != manifest['config_sha256']
            or live['runtime']['image_id'] != manifest['api']['local_image_id']
            or live['runtime']['source_revision'] != manifest['source_sha'] or live['readiness']['ready'] is not True):
        raise ValueError('Promotion outcome not established; keep maintenance and inspect status')
    return {'format': 'pokerattack-promotion-v1', 'phase': 'promoted', 'overall_release_complete': False,
            'workflow': workflow, 'source_sha': manifest['source_sha'], 'digest': manifest['api']['digest'],
            'config_sha256': manifest['config_sha256'], 'evidence_sha256': digest,
            'client_source_sha': proof['client_source_sha'], 'client_archive_sha256': proof['client_archive_sha256'], 'review': decision}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('manifest', type=Path); parser.add_argument('evidence', type=Path)
    parser.add_argument('review', type=Path); parser.add_argument('output', type=Path)
    args = parser.parse_args()
    with transport('status') as status, transport('promotion') as promotion:
        try:
            result = promote(load_document(args.manifest.read_bytes()), args.evidence.read_bytes(),
                load_document(args.review.read_bytes()), os.environ, status, promotion)
        except Exception:
            try:
                args.output.write_bytes(canonical({'phase':'promotion-failed-or-unknown','overall_release_complete':False,
                                                  'status':status.call('status production')}))
            except Exception: pass
            raise
    args.output.write_bytes(canonical(result))


if __name__ == '__main__':
    try: main()
    except Exception:
        print('Promotion failed, expired or is uncertain. Maintenance was not reopened. Inspect status; rerun the verification job and its dependents for fresh evidence/review.')
        raise SystemExit(1)
