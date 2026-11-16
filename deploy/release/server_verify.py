"""Protected gameplay and exact-byte evidence import, never promotion/restart.

Fresh attempts can resume verification of the SAME established pair through the
evidence capability. Old player IDs are never reused. Browser/private context and
credentials stay under RUNNER_TEMP; only validated sanitized evidence is copied.
"""
import argparse
import datetime
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import tempfile
import time
import uuid

from blob_store import AzureStore
from client_archive import Archive, record
from client_artifacts import identity_summary
from client_publish import trusted_context
from manifests import load_document, validate_document
from review import canonical
from server_images import execution
from ssh_client import transport, PROFILES

ROOT = Path(__file__).resolve().parents[2]
FAILURE_STAGES = frozenset(('private-context','browser-launch','gameplay','context-validation','tls-client-identity',
    'served-release-identity','served-client-settings','served-client-assets','public-api-readiness','browser-signalr',
    'previous-attempt-owned-session-leave','lobby-create','lobby-join','game-start','game-groups','initial-private-hand',
    'private-message-isolation','multiplayer-completion','solo-completion','owned-session-leave'))


def failed_stage(raw):
    # Only hard-coded stage names survive, never provider exceptions, browser
    # output, URLs, headers or credentials. This is release diagnosis, not proof.
    if len(raw)>4096:return 'unknown'
    for line in raw.decode('utf-8',errors='replace').splitlines():
        match=re.fullmatch(r'Release verification failed at ([a-z-]+); no promotable evidence emitted',line)
        if match and match[1] in FAILURE_STAGES:return match[1]
    return 'unknown'


def pending(status, manifest, run, attempt=None, resume_attempt=None):
    sampled = datetime.datetime.fromisoformat(status['sampled_utc'].replace('Z', '+00:00')).timestamp()
    if (not -5 <= time.time() - sampled <= 120 or status.get('protocol_version') != 1 or status.get('target') != 'production'
            or status.get('consistent') is not True or status['transaction']):
        raise ValueError('Uncertain/stale candidate state')
    lease = status.get('lease') or {}
    expected = {'kind': 'server', 'run_id': run, 'source_sha': manifest['source_sha'],
                'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256'],
                'manifest_sha256': hashlib.sha256(canonical(manifest)).hexdigest()}
    if any(lease.get(key) != value for key, value in expected.items()) or attempt is not None and lease.get('run_attempt') != attempt:
        raise ValueError('Candidate lease differs from the original tested manifest/execution')
    resuming = (resume_attempt is not None and type(lease.get('run_attempt')) is int and lease['run_attempt'] < resume_attempt)
    allowed = ('pending', 'promoted-needs-verification') + (('promoted', 'promoting', 'promotion-unknown', 'unknown', 'deploying', 'failed') if resuming else ())
    if lease.get('phase') not in allowed or status['maintenance']['active'] is not True:
        raise ValueError('Candidate is not in a closed verification phase')
    pair = status['pairs']['active']
    target = status['pairs']['pending'] if status['pairs']['pending'] else status['pairs']['known-good']
    if (not pair or pair != target or status['readiness']['ready'] is not True
            or lease['phase'] == 'pending' and not status['pairs']['pending']
            or lease['phase'] == 'promoted-needs-verification' and status['pairs']['pending']
            or pair['digest'] != expected['digest'] or pair['config_sha256'] != expected['config_sha256']
            or status['runtime']['image_id'] != manifest['api']['local_image_id']
            or status['runtime']['source_revision'] != manifest['source_sha']):
        raise ValueError('Runtime differs from the exact tested pair/source')
    return lease


def gameplay(context, output, env):
    secret_names = {profile[1] for profile in PROFILES.values()} | {'GH_TOKEN', 'GITHUB_TOKEN',
        'ACTIONS_ID_TOKEN_REQUEST_TOKEN', 'ACTIONS_ID_TOKEN_REQUEST_URL', 'AZURE_FEDERATED_TOKEN_FILE'}
    child = {key: value for key, value in env.items() if key not in secret_names}
    child.update(POKERATTACK_RELEASE_MODE='production', POKERATTACK_RELEASE_CONTEXT=str(context),
                 POKERATTACK_RELEASE_RESULT=str(output))
    result = subprocess.run(['node', str(ROOT / 'tests/release/run.cjs')], capture_output=True, timeout=820, env=child)
    if result.returncode:
        raise ValueError('Protected gameplay failed at '+failed_stage(result.stderr)+'; no evidence may be imported')


def verify(manifest, env, status_host, evidence_host, archive, output, runner=gameplay):
    trusted_context(env)
    source, workflow = execution(env)
    validate_document('server-manifest', manifest)
    if manifest['source_sha'] != source and env.get('SERVER_OPERATION', 'release') != 'rollback':
        raise ValueError('Unexpected verification source')
    run, attempt = workflow['run_id'], workflow['run_attempt']
    live = status_host.call('status production')
    lease = pending(live, manifest, run, resume_attempt=attempt)
    if type(lease.get('run_attempt')) is not int or lease['run_attempt'] > attempt:
        raise ValueError('Cannot reuse an earlier verification execution')
    if lease['run_attempt'] < attempt:
        result = evidence_host.call(f"resume-verification production {run} {lease['run_attempt']} {attempt}")
        if result.get('phase') not in ('pending', 'promoted-needs-verification'):
            raise ValueError('Unestablished verification resume')
        live = status_host.call('status production')
        lease = pending(live, manifest, run, attempt)
    if lease.get('verifier_issued') or lease.get('verifier_revoked'):
        raise ValueError('This attempt already issued/revoked a verifier; explicitly rerun verification as a later attempt')
    pointers, etag = archive.pointers()
    client_stage='bootstrap-client-artifact' if pointers.get('format')=='pokerattack-bootstrap-provisional-client-v1' else 'accepted-client-artifact'
    accepted = pointers['current']
    if accepted is None: raise ValueError('Compatible accepted client bootstrap is missing')
    client = archive.accepted_manifest(accepted['run_id'], accepted['run_attempt'], accepted['artifact_id'], accepted['archive_sha256'])
    if record(client) != accepted: raise ValueError('Accepted client identity changed')
    summary = identity_summary(client)
    players = [{'id': str(uuid.uuid4()), 'name': f'Release-{run}-{attempt}-{index}'} for index in range(3)]
    issued = revoked_attempted = False
    output.mkdir(parents=True, exist_ok=False)
    with tempfile.TemporaryDirectory(prefix='pokerattack-verifier-', dir=env['RUNNER_TEMP']) as directory:
        private = Path(directory); private.chmod(0o700)
        context, result_path = private / 'context.json', private / 'result.json'
        try:
            grant = evidence_host.call(f'verifier-credential production {run} {attempt}', {'players': players})
            issued = True
            if (grant.get('players') != players or not isinstance(grant.get('credential'), str)
                    or not grant['credential'].startswith(f'{run}.{attempt}.') or not isinstance(grant.get('verifier_id'), str)
                    or type(grant.get('expires_unix')) is not int or not time.time() < grant['expires_unix'] <= time.time() + 905):
                raise ValueError('Invalid bounded credential response')
            packet = {'run_id': run, 'run_attempt': attempt, 'credential': grant['credential'],
                'verifier_id': grant['verifier_id'], 'players': players, 'client': summary, 'server_manifest': manifest,
                'artifact_references': [workflow['run_url'] + '/attempts/' + str(attempt),
                    client_stage+':' + str(client['artifact_id']) + ':' + client['archive_sha256']]}
            context.write_bytes(canonical(packet)); context.chmod(0o600)
            runner(context, result_path, env)
            result = load_document(result_path.read_bytes())
            if result.get('passed') is not True: raise ValueError('Incomplete gameplay result')
            proof = validate_document('evidence', result['evidence'])
            expected = {'workflow': workflow, 'source_sha': manifest['source_sha'], 'digest': manifest['api']['digest'],
                'migration_digest': manifest['migration']['digest'], 'config_sha256': manifest['config_sha256'],
                'verifier_id': grant['verifier_id'], 'client_source_sha': summary['source_sha'],
                'client_archive_sha256': summary['archive_sha256']}
            if (any(proof[key] != value for key, value in expected.items())
                    or not 0 <= time.time() - proof['verified_unix'] < 60 or time.time() >= grant['expires_unix']):
                raise ValueError('Generated proof differs from this completed verification')
            live = status_host.call('status production'); pending(live, manifest, run, attempt)
            if live.get('sessions') != {'activeGames': 0, 'lobbies': 0, 'soloGames': 0}:
                raise ValueError('Verifier sessions remain; never substitute global cleanup')
            current, after_etag = archive.pointers()
            if after_etag != etag or current['current'] != accepted:
                raise ValueError('Accepted client changed during gameplay')
            revoked_attempted = True
            if evidence_host.call(f'revoke-verifier production {run} {attempt}').get('revoked') is not True:
                raise ValueError('Credential revocation unconfirmed')
            imported = evidence_host.call(f'import-evidence production {run} {attempt}', proof)
            raw = json.dumps(proof, separators=(',', ':'), allow_nan=False).encode()
            digest = hashlib.sha256(raw).hexdigest()
            if imported.get('evidence_sha256') != digest:
                raise ValueError('Imported exact-byte evidence hash mismatch')
            (output / 'evidence.json').write_bytes(raw)
            review = {'format': 'pokerattack-promotion-review-v1', 'evidence_sha256': digest, 'evidence': proof,
                'manifest_sha256': hashlib.sha256(canonical(manifest)).hexdigest(),
                'comment': f"PROMOTE {digest} {run}/{attempt}", 'overall_release_complete': False,
                'expires_unix': proof['verified_unix'] + 1800}
            (output / 'promotion-review.json').write_bytes(canonical(review))
            return review
        finally:
            if issued and not revoked_attempted:
                # Revocation only; no reopen/rollback/restart and no retry after
                # an uncertain revocation response. Later attempts clean own IDs.
                revoked_attempted = True
                evidence_host.call(f'revoke-verifier production {run} {attempt}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('manifest', type=Path); parser.add_argument('output', type=Path)
    args = parser.parse_args()
    manifest = load_document(args.manifest.read_bytes())
    archive = Archive(AzureStore(os.environ['CLIENT_ARCHIVE_ACCOUNT']))
    with transport('status') as status, transport('evidence') as evidence:
        result = verify(manifest, os.environ, status, evidence, archive, args.output)
    with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a', encoding='utf-8') as stream:
        stream.write('Gameplay passed and credential revoked; explicit promotion is still required.\n\n```json\n' +
            json.dumps(result, indent=2) + '\n```\n')


if __name__ == '__main__':
    try: main()
    except Exception as error:
        if re.fullmatch(r'Protected gameplay failed at ([a-z-]+); no evidence may be imported',str(error)):
            stage=str(error).split(' at ',1)[1].split(';',1)[0]
            if stage in FAILURE_STAGES or stage=='unknown':print('Gameplay failure stage: '+stage)
        print('Protected verification failed or is uncertain. Maintenance remains closed. Inspect status; a later verification attempt requires fresh gameplay and a new promotion decision.')
        raise SystemExit(1)
