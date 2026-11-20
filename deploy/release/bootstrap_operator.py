"""Root-only first-release operations with genuine build provenance and decisions.

Prepare is read-only. Begin and promote require distinct, fresh human decision
receipts. No action executes migrations, manufactures gameplay proof, retries an
uncertain mutation, or claims that routine delivery is enabled.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time

from bootstrap_archive import ProvisionalClient
from bootstrap_context import Context, decision, packet_identity
from bootstrap_publish import operator, publish
from blob_store import AzureStore
from client_archive import Archive
from client_artifacts import check_archive, identity_summary
from client_publish import github
from manifests import load_document, validate_document
from review import canonical
from server_images import execution
from ssh_client import Transport, PROFILES
import server_manifest
import server_verify
import server_finish

ROOT = Path(__file__).parents[2]


def load(path, client=False):
    return load_document(Path(path).read_bytes(), 'client-manifest' if client else None)


def save(path, value):
    with Path(path).open('xb') as stream:
        stream.write(canonical(value))
    Path(path).chmod(0o600)


def environment(published, node, private):
    built = published['workflow']
    run = github(f"actions/runs/{built['run_id']}")
    enabled = github('actions/variables/DEPLOYMENT_AUTOMATION_ENABLED')['value']
    # These fields come from the real Actions API, never a fabricated main ref.
    env = {**os.environ, 'GITHUB_SHA': run['head_sha'], 'GITHUB_RUN_ID': str(run['id']),
           'GITHUB_RUN_ATTEMPT': str(run['run_attempt']), 'GITHUB_REF': 'refs/heads/' + run['head_branch'],
           'GITHUB_EVENT_NAME': run['event'], 'GITHUB_REPOSITORY': run['repository']['full_name'],
           'DEPLOYMENT_AUTOMATION_ENABLED': enabled, 'SERVER_OPERATION': 'release', 'RUNNER_TEMP': str(private.resolve()),
           'PATH': str(node.resolve().parent) + os.pathsep + os.environ.get('PATH', '')}
    return env


def transports(key_state):
    state = load(key_state)
    if state['phase'] != 'remote-verified/environment-secrets-installed':
        raise ValueError('Installed role verification not established')
    hosts = ROOT / 'artifacts/I03-known_hosts'
    return {role: Transport(role, state['keys'][role], hosts) for role in PROFILES}


def verification_workspace(workspace, action, requested, actual, built):
    """Keep original proof immutable; only a real later execution may reverify."""
    actions = ('verify', 'promote', 'finish')
    if requested is None:
        if action in actions and actual != built:
            raise ValueError('Explicit actual verification attempt required for resumed evidence')
        return workspace
    if (action not in actions or type(requested) is not int or
            requested <= built or requested != actual):
        raise ValueError('Only actual later verification/promotion/finish execution may resume')
    return workspace / f'verification-resume-{requested}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('prepare', 'inspect', 'begin', 'maintenance', 'deliver',
                                         'candidate', 'verify', 'accept-client', 'promote', 'finish'))
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--node', type=Path, required=True)
    parser.add_argument('--key-state', type=Path, default=ROOT / 'artifacts/I03-key-install-state.json')
    parser.add_argument('--published', type=Path)
    parser.add_argument('--client-manifest', type=Path)
    parser.add_argument('--client-archive', type=Path)
    parser.add_argument('--decision', type=Path)
    parser.add_argument('--drain-seconds', type=int, default=300)
    parser.add_argument('--verification-attempt', type=int,
                        help='Actual successful later GitHub attempt; reverify/promote/finish only, never reset')
    args = parser.parse_args()
    if args.verification_attempt is not None and args.action not in ('verify', 'promote', 'finish'):
        raise ValueError('Verification resume cannot deploy, publish or refresh reset approval')
    version = subprocess.run([str(args.node), '--version'], capture_output=True, timeout=15)
    if version.returncode or not version.stdout.decode().strip().startswith('v22.'):
        raise ValueError('Reviewed Node22 runtime required before bootstrap')
    ws = args.workspace.resolve()
    roles = transports(args.key_state)
    if args.action == 'inspect':
        print(json.dumps(roles['status'].call('status production')))
        return
    if args.action == 'prepare':
        if ws.exists() or not all((args.published, args.client_manifest, args.client_archive)) or not 0 <= args.drain_seconds <= 300:
            raise ValueError('Fresh workspace and exact tested artifact inputs required')
        published, client = load(args.published), load(args.client_manifest, True)
        check_archive(args.client_archive, client)
        ws.mkdir(parents=True, mode=0o700)
        (ws / 'private').mkdir(mode=0o700)
        env = environment(published, args.node, ws / 'private')
        context = Context(published['source_sha'], published['workflow'], client, operator)
        context.build(env, fresh_main=True)
        config = github('actions/variables/APPROVED_CONFIG_SHA256')['value']
        packet = server_manifest.resolve(published, load(ROOT / 'deploy/release/schema-prerequisite.json'),
            roles['status'].call('status production'), config, env, context_validator=context.build)
        recovered = load(ROOT / 'docs/deployment-evidence/I04-runtime-verification.json')
        tooling = load(ROOT / 'docs/deployment-evidence/I03-host-installation.json')['saved_legacy_tooling']
        packet.update(control='root-only-bootstrap; routine delivery remains false', drain_seconds=args.drain_seconds,
                      client=identity_summary(client),
                      recovery={'client': recovered['durable_legacy_archive'], 'source_sha': recovered['source_sha'],
                                'image_id': recovered['image_id'], 'tooling': tooling,
                                'protected_record_sha256': recovered['bootstrap_recovery_sha256']},
                      first_release_limitation='Static maintenance cannot prevent already loaded capability-0 clients creating games; approved deadline reset deliberately ends remaining matches.')
        packet.pop('reset_review')
        digest = hashlib.sha256(canonical(packet)).hexdigest()
        packet['reset_review'] = {'packet_sha256': digest,
            'comment': f"ACK_RESET {digest} {published['workflow']['run_id']}/{published['workflow']['run_attempt']}"}
        for name, value in (('published-images.json', published), ('client-manifest.json', client),
                            ('server-manifest.json', packet['manifest']), ('release-packet.json', packet)):
            save(ws / name, value)
        shutil.copyfile(args.client_archive, ws / 'client.tar.gz')
        print(json.dumps(packet, indent=2))
        return
    packet = packet_identity(load(ws / 'release-packet.json'))
    published, client, manifest = load(ws / 'published-images.json'), load(ws / 'client-manifest.json', True), load(ws / 'server-manifest.json')
    if manifest != packet['manifest'] or identity_summary(client) != packet['client']:
        raise ValueError('Frozen bootstrap inputs differ')
    env = environment(published, args.node, ws / 'private')
    context = Context(manifest['source_sha'], execution(env)[1], client, operator, manifest=manifest)
    run, attempt = context.build(env)
    result_ws = verification_workspace(ws, args.action, args.verification_attempt, attempt,
                                       manifest['workflow']['run_attempt'])
    bound = lambda action: operator(action, run, attempt)
    provisional = ProvisionalClient(client, ws / 'client.tar.gz', bound)
    account = github('actions/variables/CLIENT_ARCHIVE_ACCOUNT')['value']
    expected = load(ROOT / 'docs/deployment-evidence/I02-azure-provisioning.json')['archive']['name']
    if account != expected:
        raise ValueError('Private archive account changed; inspect')
    catalog = Archive(AzureStore(account))
    if args.action == 'begin':
        context.build(env, fresh_main=True)
        if args.decision is None:
            raise ValueError('Fresh reset decision receipt required')
        reset = decision(load(args.decision), 'reset', packet['reset_review']['comment'], packet['review_window_started_unix'])
        ack = {'action': 'bootstrap', 'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256'],
               'source_sha': manifest['source_sha'], 'run_id': run, 'run_attempt': attempt,
               'approved_unix': packet['review_window_started_unix'], 'expires_unix': packet['review_window_expires_unix'],
               'drain_seconds': packet['drain_seconds'], 'policy': 'reset-at-deadline'}
        if (ws / 'begin.json').exists(): raise ValueError('Inspect prior begin; never replay it')
        result = operator('begin-release', run, attempt, {'manifest': manifest, 'acknowledgment': ack})
        save(ws / 'begin.json', {'decision': reset, 'result': result})
    elif args.action == 'maintenance':
        inputs = ROOT / 'artifacts/H05-inputs'
        result = publish('maintenance', inputs / 'maintenance.tar.gz', load(inputs / 'maintenance.json'), ws / 'maintenance-publication', run, attempt)
        save(ws / 'maintenance.json', result)
    elif args.action == 'deliver':
        if not (ws / 'maintenance.json').is_file() or (ws / 'drain.json').exists():
            raise ValueError('Establish static maintenance; inspect any prior delivery before repeating')
        result = operator('drain-release', run, attempt)
        save(ws / 'drain.json', result)
        delivered = roles['deployment'].call(f"deploy production {manifest['api']['digest']} {manifest['config_sha256']} {run} {attempt}", timeout=1040)
        save(ws / 'delivery.json', delivered)
    elif args.action == 'candidate':
        result = publish('candidate', ws / 'client.tar.gz', client, ws / 'candidate-publication', run, attempt)
        save(ws / 'candidate.json', result)
    elif args.action == 'verify':
        # The verifier validates the exact installed pending pair, resumes the
        # lease through its existing evidence capability, then checks the root
        # client publication under that new owner before issuing any credential.
        # A pre-resume candidate() call would incorrectly demand the new owner
        # before that constrained ownership transfer can take place.
        review = server_verify.verify(manifest, env, roles['status'], roles['evidence'], provisional,
                                      result_ws / 'verification',
                                      context_validator=context.build if args.verification_attempt else context.candidate)
        save(result_ws / 'promotion-review.json', review)
    elif args.action == 'accept-client':
        save(ws / 'accepted-client.json', provisional.accept_verified(catalog, run, attempt))
    elif args.action == 'promote':
        context.candidate(env)
        raw = (result_ws / 'verification/evidence.json').read_bytes()
        proof = validate_document('evidence', load_document(raw))
        digest = hashlib.sha256(raw).hexdigest()
        review = load(result_ws / 'promotion-review.json')
        comment = f'PROMOTE {digest} {run}/{attempt}'
        if args.decision is None or review['evidence_sha256'] != digest or review['comment'] != comment:
            raise ValueError('Fresh distinct promotion decision for actual imported proof required')
        approval = decision(load(args.decision), 'promote', comment, proof['verified_unix'])
        live = roles['status'].call('status production')
        lease = server_verify.pending(live, manifest, run, attempt)
        if lease.get('evidence_sha256') != digest or lease.get('verifier_revoked') is not True:
            raise ValueError('Actual import/revocation not established')
        if (result_ws / 'promotion.json').exists(): raise ValueError('Inspect prior promotion; never replay it')
        result = roles['promotion'].call(f"promote production {manifest['api']['digest']} {digest} {run} {attempt}", timeout=150)
        after = roles['status'].call('status production')
        if (result.get('phase') != 'promoted' or after['lease']['phase'] != 'promoted' or after['pairs']['pending']
                or after['pairs']['active'] != after['pairs']['known-good']):
            raise ValueError('Promotion outcome unresolved; retain maintenance')
        save(result_ws / 'promotion.json', {'format': 'pokerattack-promotion-v1', 'phase': 'promoted',
            'overall_release_complete': False, 'workflow': execution(env)[1], 'source_sha': manifest['source_sha'],
            'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256'], 'evidence_sha256': digest,
            'client_source_sha': proof['client_source_sha'], 'client_archive_sha256': proof['client_archive_sha256'], 'review': approval})
    elif args.action == 'finish':
        server_finish.finish(manifest, load(result_ws / 'promotion.json'), env, roles['status'], roles['promotion'],
                             catalog, result_ws / 'completion.json', context_validator=context.candidate)
    print('Bootstrap operation recorded: ' + args.action + '; inspect its exact state before any subsequent action.')


if __name__ == '__main__':
    try:
        main()
    except Exception:
        raise SystemExit('Bootstrap operation incomplete or outcome uncertain; inspect structured status and receipts. No automatic retry, migration, promotion or reopening occurred.')
