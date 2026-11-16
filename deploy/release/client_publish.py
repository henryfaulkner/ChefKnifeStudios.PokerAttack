"""Protected client job mechanics. SWA upload remains the pinned Azure action.

All production operations require trusted main context. This module reserves the
host slot, restores exact archives and commits only real browser results. It never
restarts an API, executes migrations or clears an uncertain server/client lease.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.request

from blob_store import AzureStore
from client_archive import Archive, prefix, record, record_prefix
from client_artifacts import canonical, check_archive, identity_summary, positive
from manifests import load_document, validate_document
from ssh_client import transport

REPOSITORY = 'henryfaulkner/ChefKnifeStudios.PokerAttack'
class PublicationBlocked(ValueError):
    pass


ORIGINS = ('https://playpokerattack.com', 'https://www.playpokerattack.com',
           'https://salmon-hill-0056eb60f.2.azurestaticapps.net')


def trusted_context(environment):
    if (environment.get('GITHUB_REPOSITORY') != REPOSITORY or environment.get('GITHUB_REF') != 'refs/heads/main' or
            environment.get('GITHUB_EVENT_NAME') not in ('push', 'workflow_dispatch') or
            environment.get('DEPLOYMENT_AUTOMATION_ENABLED') != 'true'):
        raise PublicationBlocked('Production publication requires enabled trusted main context')
    run, attempt = int(environment['GITHUB_RUN_ID']), int(environment['GITHUB_RUN_ATTEMPT'])
    positive(run); positive(attempt)
    return run, attempt


def validate_status(status):
    sampled = datetime.datetime.fromisoformat(status['sampled_utc'].replace('Z', '+00:00')).timestamp()
    if not -5 <= time.time() - sampled <= 120:
        raise PublicationBlocked('Stale host status')
    pair = status['pairs']['known-good']
    if (status.get('protocol_version') != 1 or status.get('target') != 'production' or status.get('consistent') is not True or
            status['transaction'] or status['pairs']['pending'] or status['readiness']['ready'] is not True or
            not pair or pair != status['pairs']['active'] or status['maintenance']['active'] is not False):
        raise PublicationBlocked('Unresolved server state blocks client publication')
    return status


def github(path):
    result = subprocess.run(['gh', 'api', 'repos/' + REPOSITORY + '/' + path], capture_output=True, text=True, timeout=30)
    if result.returncode: raise PublicationBlocked('Trusted GitHub provenance/status unavailable')
    return json.loads(result.stdout)


def prior_upload(run, attempt):
    jobs = github(f'actions/runs/{run}/attempts/{attempt}/jobs?per_page=100')['jobs']
    deploy = [job for job in jobs if job['name'] == 'deploy']
    if len(deploy) != 1 or deploy[0]['status'] != 'completed':
        return 'unknown'
    steps = [step for step in deploy[0].get('steps', []) if step['name'] == 'Deploy exact prepared client']
    if not steps or all(step.get('conclusion') == 'skipped' for step in steps): return 'not-started'
    if len(steps) == 1 and steps[0].get('conclusion') == 'success': return 'completed'
    return 'unknown'


def service_ready():
    result = subprocess.run(['az', 'staticwebapp', 'environment', 'list', '--name', 'poker-attack-webapp',
        '--resource-group', 'poker-attack-rg', '--query', "[?name=='default'].status | [0]", '-o', 'json', '--only-show-errors'],
        capture_output=True, text=True, timeout=30)
    if result.returncode or json.loads(result.stdout) != 'Ready':
        raise PublicationBlocked('SWA service operation is unresolved; do not upload again')


def observe(manifest):
    expected = {'format': 'pokerattack-client-public-v1', 'source_sha': manifest['source_sha'],
                'run_id': manifest['workflow']['run_id'], 'run_attempt': manifest['workflow']['run_attempt'],
                'minimum_server_capability': manifest['minimum_server_capability']}
    for origin in ORIGINS:
        try:
            request = urllib.request.Request(origin + '/release.json', headers={'Cache-Control': 'no-cache'})
            # No redirects, insecure TLS, cache-query identity or arbitrary URL.
            class NoRedirect(urllib.request.HTTPRedirectHandler):
                def redirect_request(self, *args): return None
            with urllib.request.build_opener(NoRedirect()).open(request, timeout=10) as response:
                raw = response.read(65537)
                if response.status != 200 or len(raw) > 65536 or load_document(raw) != expected: return False
        except Exception:
            return False
    return True


def served_states(candidate, recovery):
    # Only public immutable release identity is inspected here. Failed/mixed or
    # unknown bodies never authorize overwriting an unrelated publication.
    def identity(manifest):
        return {'format': 'pokerattack-client-public-v1', 'source_sha': manifest['source_sha'],
                'run_id': manifest['workflow']['run_id'], 'run_attempt': manifest['workflow']['run_attempt'],
                'minimum_server_capability': manifest['minimum_server_capability']}
    expected = {'candidate': identity(candidate), 'recovery': identity(recovery)}
    result = []
    for origin in ORIGINS:
        try:
            class NoRedirect(urllib.request.HTTPRedirectHandler):
                def redirect_request(self, *args): return None
            request = urllib.request.Request(origin + '/release.json', headers={'Cache-Control': 'no-cache'})
            with urllib.request.build_opener(NoRedirect()).open(request, timeout=10) as response:
                raw = response.read(65537)
                value = load_document(raw) if response.status == 200 and len(raw) <= 65536 else None
            result.append(next((name for name, document in expected.items() if value == document), 'unknown'))
        except Exception: result.append('unknown')
    return result


def prior_job_completed(run, attempt):
    jobs = github(f'actions/runs/{run}/attempts/{attempt}/jobs?per_page=100')['jobs']
    matches = [job for job in jobs if job['name'] == 'deploy']
    return len(matches) == 1 and matches[0]['status'] == 'completed'


def restore_selection(environment):
    return (int(environment['RESTORE_RUN']), int(environment['RESTORE_ATTEMPT']),
            int(environment['RESTORE_ARTIFACT']), environment['RESTORE_CHECKSUM'])


def download(catalog, identity, archive, manifest_path):
    key = record_prefix(identity)
    manifest, _ = catalog.read(key + '/manifest.json', 'client-manifest')
    if manifest is None or record(manifest) != identity: raise PublicationBlocked('Immutable client manifest changed or missing')
    info = catalog.store.info(key + '/client.tar.gz')
    if not info: raise PublicationBlocked('Recovery/candidate archive is missing')
    catalog.store.download(key + '/client.tar.gz', archive, info['etag'])
    check_archive(archive, manifest)
    manifest_path.write_bytes(canonical(manifest))
    return manifest


def status_identity(lease, recovery=False):
    if recovery:
        if 'client_recovery_archive_sha256' not in lease: return None
        values = (lease['client_recovery_run_id'], lease['client_recovery_run_attempt'],
                  lease['client_recovery_artifact_id'], lease['client_recovery_archive_sha256'])
    else:
        values = (lease['client_source_run_id'], lease['client_source_run_attempt'], lease['artifact_id'], lease['client_archive_sha256'])
    return prefix(*values)


def pending_manifest(catalog, key):
    manifest, _ = catalog.read(key + '/manifest.json', 'client-manifest')
    if manifest is None or record_prefix(record(manifest)) != key: raise PublicationBlocked('Reserved archive provenance unavailable')
    return manifest


def reserve(catalog, status_key, lease_key, workspace, incoming_manifest, incoming_archive, environment):
    run, attempt = trusted_context(environment)
    status = validate_status(status_key.call('status production'))
    lease = status.get('lease')
    operation = environment.get('CLIENT_OPERATION', 'release')
    if operation not in ('release', 'restore') or operation == 'restore' and environment['GITHUB_EVENT_NAME'] != 'workflow_dispatch':
        raise PublicationBlocked('Accepted restoration requires explicit trusted workflow dispatch')
    foreign = lease is not None and lease.get('run_id') != run
    resumed = lease is not None and not foreign
    if lease and (lease.get('kind') != 'client' or foreign and operation != 'restore'):
        raise PublicationBlocked('Another release owns the host slot')
    head = github('git/ref/heads/main')['object']['sha']
    if head != environment['GITHUB_SHA'] and not resumed:
        raise PublicationBlocked('Queued source is behind current main; start a new run')
    workspace = Path(workspace)
    if workspace.exists(): raise PublicationBlocked('Publication directory must be fresh')
    workspace.mkdir(parents=True)
    pointers, _ = catalog.pointers()
    if resumed:
        manifest = pending_manifest(catalog, status_identity(lease))
        if manifest['source_sha'] != lease.get('source_sha'): raise PublicationBlocked('Reserved source differs')
        key = status_identity(lease, recovery=True)
        recovery = pending_manifest(catalog, key) if key else None
        download(catalog, record(manifest), workspace / 'client.tar.gz', workspace / 'manifest.json')
    elif operation == 'restore':
        manifest = catalog.accepted_manifest(*restore_selection(environment))
        download(catalog, record(manifest), workspace / 'client.tar.gz', workspace / 'manifest.json')
        if pointers['current'] is None: raise PublicationBlocked('Accepted current recovery identity missing')
        recovery, _ = catalog.read(record_prefix(pointers['current']) + '/manifest.json', 'client-manifest')
        if recovery is None or record(recovery) != pointers['current']: raise PublicationBlocked('Accepted recovery manifest missing')
        if foreign:
            if record_prefix(record(manifest)) != status_identity(lease, recovery=True):
                raise PublicationBlocked('Interrupted client slot can recover only its exact reserved predecessor')
            if not prior_job_completed(lease['run_id'], lease['run_attempt']):
                raise PublicationBlocked('Previous client job is still running or cannot be identified')
    else:
        manifest = validate_document('client-manifest', incoming_manifest)
        if (manifest['source_sha'] != environment['GITHUB_SHA'] or manifest['workflow']['run_id'] != run or
                manifest['workflow']['run_attempt'] != attempt):
            raise PublicationBlocked('Candidate differs from this trusted build/run')
        if manifest['artifact_id'] != int(environment['CLIENT_CANDIDATE_ARTIFACT_ID']):
            raise PublicationBlocked('Candidate differs from the exact downloaded Actions artifact ID')
        artifact = github(f'actions/artifacts/{manifest["artifact_id"]}')
        if (artifact.get('expired') is not False or artifact.get('workflow_run', {}).get('id') != run or
                artifact['workflow_run'].get('head_sha') != manifest['source_sha']):
            raise PublicationBlocked('Actions artifact provenance is not the matching trusted build')
        check_archive(incoming_archive, manifest)
        catalog.store_candidate(manifest, incoming_archive)
        download(catalog, record(manifest), workspace / 'client.tar.gz', workspace / 'manifest.json')
        if pointers['current'] is None:
            raise PublicationBlocked('Bootstrap must first seed the verified accepted client/recovery archive')
        recovery, _ = catalog.read(record_prefix(pointers['current']) + '/manifest.json', 'client-manifest')
        if recovery is None or record(recovery) != pointers['current']: raise PublicationBlocked('Accepted recovery manifest missing')
    if recovery is not None:
        download(catalog, record(recovery), workspace / 'recovery.tar.gz', workspace / 'recovery-manifest.json')
    check_archive(workspace / 'client.tar.gz', manifest, workspace / 'client')
    service_ready()
    already_served = observe(manifest)
    if resumed:
        previous = prior_upload(run, lease['run_attempt'])
        # A prior completed action is never executed again. A response lost after
        # upload is reconciled by actual served identity. Otherwise only a proven
        # not-started action can upload; ambiguous outcomes stay leased.
        upload = previous == 'not-started' and not already_served
        if upload and head != environment['GITHUB_SHA']:
            raise PublicationBlocked('Unpublished reserved source is stale; verify recovery before releasing its slot')
        if not already_served and previous == 'unknown':
            raise PublicationBlocked('SWA upload outcome unknown; reconcile before any upload/restoration')
        if lease['run_attempt'] < attempt:
            lease_key.call(f'resume-client production {run} {lease["run_attempt"]} {attempt}')
        elif lease['run_attempt'] != attempt: raise PublicationBlocked('Cannot reclaim a later attempt')
    elif foreign:
        old_candidate = {'source_sha': lease['source_sha'], 'workflow': {
            'run_id': lease['client_source_run_id'], 'run_attempt': lease['client_source_run_attempt']},
            'minimum_server_capability': lease['minimum_server_capability']}
        if 'unknown' in served_states(old_candidate, manifest):
            raise PublicationBlocked('Actual SWA publication is not a known reserved identity; inspect before recovery')
        if github('git/ref/heads/main')['object']['sha'] != environment['GITHUB_SHA']:
            raise PublicationBlocked('Recovery source became stale before explicit handoff')
        lease_key.call(f'begin-client-recovery production {run} {attempt}', {
            'candidate': identity_summary(manifest), 'recovery': identity_summary(recovery),
            'recovery_from': {key: lease[key] for key in ('run_id', 'run_attempt', 'client_archive_sha256')}})
        upload = not already_served
    else:
        if recovery is None or not observe(recovery): raise PublicationBlocked('Served predecessor differs from accepted recovery identity')
        if github('git/ref/heads/main')['object']['sha'] != environment['GITHUB_SHA']:
            raise PublicationBlocked('Source became stale before reserving production')
        lease_key.call(f'begin-client production {run} {attempt}', {
            'candidate': identity_summary(manifest), 'recovery': identity_summary(recovery)})
        upload = not already_served
    context = {'run_id': run, 'run_attempt': attempt, 'candidate': identity_summary(manifest),
               'recovery': identity_summary(recovery) if recovery else None, 'upload_needed': upload,
               'server_pair': status['pairs']['known-good'],
               'operation': operation,
               'resumed': resumed,
               'prior_upload_completed': resumed and (previous == 'completed' or already_served)}
    (workspace / 'context.json').write_bytes(canonical(context))
    return context


def recheck(status_key, workspace, environment):
    run, attempt = trusted_context(environment)
    context = load_document((Path(workspace) / 'context.json').read_bytes())
    status = validate_status(status_key.call('status production'))
    lease = status.get('lease') or {}
    if (lease.get('kind') != 'client' or lease.get('run_id') != run or lease.get('run_attempt') != attempt or
            lease.get('client_archive_sha256') != context['candidate']['archive_sha256']):
        raise PublicationBlocked('Client slot changed before publication')
    if context['upload_needed'] and github('git/ref/heads/main')['object']['sha'] != environment['GITHUB_SHA']:
        raise PublicationBlocked('Source became stale immediately before upload')
    service_ready()


def commit(catalog, status_key, lease_key, workspace, result, restored=False):
    workspace = Path(workspace)
    context = load_document((workspace / 'context.json').read_bytes())
    manifest = load_document((workspace / ('recovery-manifest.json' if restored else 'manifest.json')).read_bytes(), 'client-manifest')
    expected = {'https', 'served_client', 'settings', 'header', 'assets', 'browser', 'signalr'}
    if (set(result.get('checks', {})) != expected or any(value is not True for value in result['checks'].values()) or
            type(result.get('verified_unix')) is not int or not 0 <= time.time() - result['verified_unix'] < 1800 or
            result.get('client_source_sha') != manifest['source_sha'] or result.get('client_archive_sha256') != manifest['archive_sha256']):
        raise PublicationBlocked('Client result is failed/stale/substituted')
    run, attempt = context['run_id'], context['run_attempt']
    def held(owner_run, owner_attempt):
        status = validate_status(status_key.call('status production'))
        lease = status.get('lease') or {}
        return (lease.get('kind') == 'client' and lease.get('run_id') == owner_run == run and
                lease.get('run_attempt') == owner_attempt == attempt and lease.get('client_archive_sha256') == context['candidate']['archive_sha256'] and
                status['pairs']['known-good'] == context['server_pair'])
    catalog.lease_check = held
    service_ready()
    catalog.accept(manifest, workspace / ('recovery.tar.gz' if restored else 'client.tar.gz'), run, attempt)
    outcome = 'restored' if restored else 'accepted'
    try:
        return lease_key.call(f'finish-client production {run} {attempt}', {
            'outcome': outcome, 'client': identity_summary(manifest), 'verified_unix': result['verified_unix'], 'checks': result['checks']})
    except Exception:
        # Explicit outcome reconciliation, never another mutation attempt. The
        # desired verified bytes, accepted CAS and idle matching host must agree.
        current = validate_status(status_key.call('status production'))
        pointers, _ = catalog.pointers()
        if (current.get('lease') is None and current['pairs']['known-good'] == context['server_pair'] and
                pointers['current'] == record(manifest) and observe(manifest)):
            service_ready()
            return {'phase': 'client-finished', 'outcome': outcome, 'response_reconciled': True}
        raise


def prepare_restoration(status_key, workspace, publication_outcome):
    workspace = Path(workspace)
    context = load_document((workspace / 'context.json').read_bytes())
    status = validate_status(status_key.call('status production'))
    lease = status.get('lease') or {}
    if (lease.get('kind') != 'client' or lease.get('run_id') != context['run_id'] or
            lease.get('run_attempt') != context['run_attempt'] or status['pairs']['known-good'] != context['server_pair']):
        raise PublicationBlocked('Client slot/server changed; no automatic restoration is permitted')
    if context.get('recovery') is None or not (workspace / 'recovery.tar.gz').is_file():
        raise PublicationBlocked('No recorded recovery archive; leave the attempted publication failed and leased')
    candidate = load_document((workspace / 'manifest.json').read_bytes(), 'client-manifest')
    recovery = load_document((workspace / 'recovery-manifest.json').read_bytes(), 'client-manifest')
    service_ready()
    states = served_states(candidate, recovery)
    if 'unknown' in states:
        raise PublicationBlocked('Unknown SWA identity; resolve publication before any restoration upload')
    if record(candidate) == record(recovery) or all(value == 'recovery' for value in states):
        upload = False
    elif publication_outcome == 'success' or context.get('prior_upload_completed') or all(value == 'candidate' for value in states):
        upload = True
    else:
        raise PublicationBlocked('Upload outcome is ambiguous; do not retry or overwrite it')
    check_archive(workspace / 'recovery.tar.gz', recovery, workspace / 'recovery-client')
    context['restore_upload_needed'] = upload
    (workspace / 'context.json').write_bytes(canonical(context))
    return upload


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=('prepare', 'recheck', 'commit', 'prepare-restoration'))
    parser.add_argument('--restored', action='store_true')
    parser.add_argument('--directory', default='artifacts/client-release')
    parser.add_argument('--manifest', default='artifacts/client-release-input/client-manifest.json')
    parser.add_argument('--archive', default='artifacts/client-release-input/client.tar.gz')
    parser.add_argument('--result', default='artifacts/client-verification.json')
    args = parser.parse_args()
    trusted_context(os.environ)
    root = Path(os.environ['GITHUB_WORKSPACE']).resolve(strict=True)
    workspace = Path(args.directory).resolve()
    if not workspace.is_relative_to(root / 'artifacts') or workspace == root / 'artifacts':
        raise PublicationBlocked('Public deployment bytes must be below workspace artifacts')
    catalog = Archive(AzureStore(os.environ['CLIENT_ARCHIVE_ACCOUNT']))
    with transport('status') as status_key:
        if args.action == 'recheck':
            recheck(status_key, workspace, os.environ)
            return
        if args.action == 'prepare-restoration':
            value = prepare_restoration(status_key, workspace, os.environ.get('PUBLICATION_OUTCOME'))
            with Path(os.environ['GITHUB_OUTPUT']).open('a') as output:
                output.write('restore_upload_needed=' + str(value).lower() + '\n')
            return
        with transport('client-lease') as lease_key:
            execute(args, catalog, status_key, lease_key, workspace)


def execute(args, catalog, status_key, lease_key, workspace):
        if args.action == 'prepare':
            manifest = load_document(Path(args.manifest).read_bytes(), 'client-manifest') if os.environ.get('CLIENT_OPERATION', 'release') == 'release' else None
            value = reserve(catalog, status_key, lease_key, workspace, manifest, args.archive, os.environ)
            with Path(os.environ['GITHUB_OUTPUT']).open('a') as output:
                output.write('upload_needed=' + str(value['upload_needed']).lower() + '\n')
        else:
            result = load_document(Path(args.result).read_bytes())
            outcome = commit(catalog, status_key, lease_key, workspace, result, args.restored)
            (workspace / 'completion.json').write_bytes(canonical(outcome))


if __name__ == '__main__':
    try: main()
    except PublicationBlocked as error:
        raise SystemExit('Client publication blocked: ' + str(error))
    except Exception:
        raise SystemExit('Client publication stopped; inspect host lease, served identity and archive before further mutation.')
