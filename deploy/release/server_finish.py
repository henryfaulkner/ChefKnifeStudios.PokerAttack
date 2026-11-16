"""Finish only the promoted/revoked release, then prove public normal admission.

Failures distinguish closed maintenance, uncertain finish and reopened public
verification failure. No automatic rollback, API reset or unconditional reopening.
"""
import argparse
import datetime
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time

from blob_store import AzureStore
from client_archive import Archive, record
from client_publish import trusted_context, validate_status
from manifests import load_document, validate_document
from review import canonical
from server_images import execution
from server_deliver import owned
from ssh_client import transport, PROFILES, UnknownOutcome

ROOT = Path(__file__).resolve().parents[2]
CHECKS = ('https','served_client','settings','header','assets','browser','signalr')


def public_browser(manifest, result, env):
    secret_names = {profile[1] for profile in PROFILES.values()} | {'GH_TOKEN','GITHUB_TOKEN',
        'ACTIONS_ID_TOKEN_REQUEST_TOKEN','ACTIONS_ID_TOKEN_REQUEST_URL','AZURE_FEDERATED_TOKEN_FILE'}
    child = {key:value for key,value in env.items() if key not in secret_names}
    child.update(POKERATTACK_RELEASE_MODE='production', POKERATTACK_CLIENT_MANIFEST=str(manifest),
                 POKERATTACK_CLIENT_RESULT=str(result))
    outcome = subprocess.run(['node',str(ROOT/'tests/release/client.cjs')],capture_output=True,timeout=660,env=child)
    if outcome.returncode: raise ValueError('Public client/normal browser verification failed')


def reopened(live, manifest):
    validate_status(live)
    if (live['lease'] is not None or live['pairs']['active']['digest'] != manifest['api']['digest']
            or live['pairs']['active']['config_sha256'] != manifest['config_sha256']
            or live['runtime']['image_id'] != manifest['api']['local_image_id']
            or live['runtime']['source_revision'] != manifest['source_sha']):
        raise ValueError('Reopened runtime differs from the promoted pair')


def finish(manifest, promotion, env, status_host, promotion_host, catalog, output, runner=public_browser):
    trusted_context(env);_,workflow=execution(env)
    validate_document('server-manifest',manifest)
    expected={'phase':'promoted','workflow':workflow,'source_sha':manifest['source_sha'],
              'digest':manifest['api']['digest'],'config_sha256':manifest['config_sha256']}
    if any(promotion.get(key)!=value for key,value in expected.items()) or promotion.get('overall_release_complete') is not False:
        raise ValueError('Matching completed promotion outcome is required')
    summary={**promotion,'phase':'promoted-not-reopened','overall_release_complete':False,
             'promotion_complete':True,
             'migration_digest':manifest['migration']['digest'],'public_verification':None,
             'dashboard_url':env.get('GRAFANA_DOCKER_DASHBOARD_URL') or None}
    try:
        live=status_host.call('status production')
        lease=owned(live,{'manifest':manifest,'execution':workflow})
        if lease['phase']!='promoted' or lease.get('verifier_revoked') is not True or lease.get('evidence_sha256')!=promotion['evidence_sha256']:
            raise ValueError('Current promotion/revocation evidence differs')
        pointers,etag=catalog.pointers();accepted=pointers['current']
        if accepted is None:raise ValueError('Accepted client disappeared before reopening')
        client=catalog.accepted_manifest(accepted['run_id'],accepted['run_attempt'],accepted['artifact_id'],accepted['archive_sha256'])
        if (record(client)!=accepted or client['source_sha']!=promotion['client_source_sha']
                or client['archive_sha256']!=promotion['client_archive_sha256']):
            raise ValueError('Accepted client differs from promoted verification')
        run,attempt=workflow['run_id'],workflow['run_attempt']
        summary['phase']='finish-outcome-unknown'
        try:
            result=promotion_host.call(f'finish-release production {run} {attempt}',timeout=90)
        except UnknownOutcome:
            # Resolve the serving state read-only; never call finish twice.
            live=status_host.call('status production');reopened(live,manifest)
            summary['finish_response_reconciled']=True
        else:
            if result.get('phase')!='reopened':raise ValueError('Unexpected finish response')
            live=status_host.call('status production');reopened(live,manifest)
        summary['phase']='promoted-and-reopened-public-check-pending'
        with tempfile.TemporaryDirectory(prefix='pokerattack-public-check-',dir=env['RUNNER_TEMP']) as directory:
            directory=Path(directory);directory.chmod(0o700)
            identity,result=directory/'client-manifest.json',directory/'result.json'
            identity.write_bytes(canonical(client));identity.chmod(0o600)
            runner(identity,result,env)
            verification=load_document(result.read_bytes())
            if (set(verification.get('checks',{}))!=set(CHECKS) or any(verification['checks'][key] is not True for key in CHECKS)
                    or not 0<=time.time()-verification['verified_unix']<60
                    or verification['client_source_sha']!=client['source_sha'] or verification['client_archive_sha256']!=client['archive_sha256']):
                raise ValueError('Public normal-admission result does not match the accepted client')
        current,new_etag=catalog.pointers()
        if new_etag!=etag or current['current']!=accepted:raise ValueError('Client archive changed during public verification')
        live=status_host.call('status production');reopened(live,manifest)
        summary.update(phase='complete',overall_release_complete=True,public_verification=verification,
                       known_good=live['pairs']['known-good'],runtime=live['runtime'],
                       completed_utc=datetime.datetime.now(datetime.timezone.utc).isoformat())
        return summary
    except Exception:
        # Preserve the actual stage. Public failure after a successful reopen
        # does not authorize interrupting newly admitted normal games.
        if summary['phase']=='promoted-and-reopened-public-check-pending':summary['phase']='promoted-and-reopened-public-check-failed'
        raise
    finally:
        output.write_bytes(canonical(summary))


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('manifest',type=Path);parser.add_argument('promotion',type=Path);parser.add_argument('output',type=Path)
    args=parser.parse_args();catalog=Archive(AzureStore(os.environ['CLIENT_ARCHIVE_ACCOUNT']))
    try:
        with transport('status') as status,transport('promotion') as promotion:
            finish(load_document(args.manifest.read_bytes()),load_document(args.promotion.read_bytes()),os.environ,
                   status,promotion,catalog,args.output)
    finally:
        if args.output.is_file():
            with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a',encoding='utf-8') as stream:
                stream.write('Server release result (promotion, reopening and public checks are separate):\n\n```json\n'+
                             json.dumps(load_document(args.output.read_bytes()),indent=2)+'\n```\n')


if __name__=='__main__':
    try:main()
    except Exception:
        print('Server finalization failed or is uncertain. Inspect its recorded promotion/reopening/public-check stage; no automatic restart or rollback was attempted.')
        raise SystemExit(1)
