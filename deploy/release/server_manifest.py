"""Resolve a review packet without config bytes, DB credentials or mutation keys.

The host performs the actual fixed read-only schema/config prerequisite at begin,
before maintenance or API stop. The runner never executes a migration bundle.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import time

from client_publish import trusted_context, validate_status
from manifests import load_document, validate_document
from server_images import execution, identity, REPOS, REGISTRY
from ssh_client import transport

ROOT = Path(__file__).resolve().parents[2]


def resolve(published, schema, live, config, env=os.environ, source_root=ROOT):
    trusted_context(env)
    source, workflow = execution(env)
    validate_status(live)
    if live['lease'] is not None:
        raise ValueError('Existing durable release/client lease blocks a new candidate')
    counts = live.get('sessions')
    if (not isinstance(counts, dict) or set(counts) != {'activeGames', 'lobbies', 'soloGames'}
            or any(type(value) is not int or not 0 <= value <= 1000000 for value in counts.values())):
        raise ValueError('Current private session observation is unavailable')
    identity(config, r'[a-f0-9]{64}')
    if set(published) != {'format', 'source_sha', 'workflow', 'images', 'tested_archive_sha256'} or published['format'] != 'pokerattack-published-images-v1':
        raise ValueError('Wrong published image record')
    built = published['workflow']
    if (published['source_sha'] != source or not isinstance(built, dict) or set(built) != set(workflow)
            or type(built['run_attempt']) is not int or not 1 <= built['run_attempt'] <= workflow['run_attempt']
            or any(built[key] != value for key, value in workflow.items() if key != 'run_attempt')):
        raise ValueError('Published images are not from this trusted source/run')
    identity(published['tested_archive_sha256'], r'[a-f0-9]{64}')
    if set(published['images']) != set(REPOS):
        raise ValueError('Wrong published image set')
    actual = sorted(path.stem for path in (source_root / 'src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Migrations').glob('*.cs')
                    if re.fullmatch(r'[0-9]{14}_[A-Za-z0-9_]+', path.stem))
    if not actual or schema.get('required_migrations') != actual:
        raise ValueError('Schema prerequisite differs from the reviewed source migration set')
    manifest = {'format': 'pokerattack-server-v1', 'source_sha': source, 'workflow': workflow,
                'api': published['images']['api'], 'migration': published['images']['migration'],
                'config_sha256': config, 'schema': schema,
                'verification_references': [workflow['run_url'] + '/attempts/' + str(built['run_attempt']),
                    'tested-image-archive-sha256:' + published['tested_archive_sha256']]}
    validate_document('server-manifest', manifest)
    # No candidate config bytes or database credentials are available here.
    # Existing local readiness proves the current application/schema baseline;
    # the host rechecks both candidate and retained recovery schema prerequisites.
    snapshot = {'format': 'pokerattack-release-packet-v1', 'operation': 'release', 'execution': workflow, 'manifest': manifest,
                'manifest_sha256': hashlib.sha256(json.dumps(manifest, sort_keys=True, separators=(',', ':')).encode()).hexdigest(),
                'observed_utc': live['sampled_utc'], 'current_pair': live['pairs']['active'],
                'recovery_pair': live['pairs']['known-good'],
                'observed_sessions': counts,
                'review_window_started_unix': int(time.time()),
                'expected_interruption': 'API restart ends remaining matches after the acknowledged bounded drain/reset deadline.',
                'schema_check': 'Host read-only prerequisite before maintenance/stop; migration bundle publication does not execute it.'}
    snapshot['review_window_expires_unix'] = snapshot['review_window_started_unix'] + 1800
    packet_hash = hashlib.sha256(json.dumps(snapshot, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    snapshot['reset_review'] = {'packet_sha256': packet_hash,
        'comment': f"ACK_RESET {packet_hash} {workflow['run_id']}/{workflow['run_attempt']}"}
    return snapshot


def resolve_rollback(target, schema, env=os.environ):
    trusted_context(env)
    _, workflow = execution(env)
    if env.get('SERVER_OPERATION') != 'rollback' or target.get('format') != 'pokerattack-rollback-target-v1':
        raise ValueError('Explicit recorded rollback required')
    sampled = datetime.datetime.fromisoformat(target['sampled_utc'].replace('Z', '+00:00')).timestamp()
    if not 0 <= time.time() - sampled < 120:
        raise ValueError('Recovery observation is stale')
    manifest = validate_document('server-manifest', target['manifest'])
    pair = target['recovery_pair']
    digest = hashlib.sha256(json.dumps(manifest, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    if (digest != target['manifest_sha256'] or manifest['api']['digest'] != pair['digest']
            or manifest['config_sha256'] != pair['config_sha256'] or pair.get('snapshot_config_matches') is not True
            or manifest['schema'] != schema):
        raise ValueError('Recorded target identity or reviewed schema prerequisite differs')
    packet = {'format': 'pokerattack-release-packet-v1', 'operation': 'rollback', 'execution': workflow,
        'manifest': manifest, 'manifest_sha256': digest, 'observed_utc': target['sampled_utc'],
        'current_pair': target['current_pair'], 'pending_pair': target['pending_pair'],
        'recovery_pair': pair, 'recovery_from': target['recovery_from'],
        'observed_sessions': target['observed_sessions'], 'review_window_started_unix': int(time.time()),
        'expected_interruption': 'Explicit rollback restarts only the API after the acknowledged bounded drain/reset deadline. Unavailable counts are not zero.',
        'schema_check': 'Host read-only prerequisite; no migration or database downgrade.'}
    packet['review_window_expires_unix'] = packet['review_window_started_unix'] + 1800
    packet_hash = hashlib.sha256(json.dumps(packet, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    packet['reset_review'] = {'packet_sha256': packet_hash,
        'comment': f"ACK_RESET {packet_hash} {workflow['run_id']}/{workflow['run_attempt']}"}
    return packet


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('published', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    schema = load_document((ROOT / 'deploy/release/schema-prerequisite.json').read_bytes())
    with transport('status') as host:
        if os.environ.get('SERVER_OPERATION', 'release') == 'rollback':
            packet = resolve_rollback(host.call('rollback-target production'), schema)
        else:
            live = host.call('status production')
            packet = resolve(load_document(args.published.read_bytes()), schema, live, os.environ['APPROVED_CONFIG_SHA256'])
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / 'server-manifest.json').write_text(json.dumps(packet['manifest'], sort_keys=True) + '\n', encoding='utf-8')
    (args.output / 'release-packet.json').write_text(json.dumps(packet, sort_keys=True) + '\n', encoding='utf-8')
    with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a', encoding='utf-8') as stream:
        stream.write('Release packet prepared; deployment and promotion have not occurred.\n\n')
        stream.write('Approve server-linode only for this packet, with its exact reset acknowledgment comment. '
                     'The immutable packet window expires after 30 minutes; a delayed runner does not extend it.\n\n')
        stream.write('```json\n' + json.dumps(packet, indent=2) + '\n```\n')


if __name__ == '__main__':
    try: main()
    except Exception:
        print('Release packet rejected; inspect source/config/schema/current-state prerequisites. Production was not changed.')
        raise SystemExit(1)
