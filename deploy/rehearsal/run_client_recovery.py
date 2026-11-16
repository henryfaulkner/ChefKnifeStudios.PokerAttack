"""H03 exact-artifact client recovery with real lease/HTTPS/browser verification.

Only the owned fixture file server substitutes for SWA publication/ARM state;
GitHub metadata is explicitly simulated. No real SWA token or API restart.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time

from initialize_replica import machine_guard,PRIVATE,ROOT,run
from run_release import transport
from run_faults import context
sys.path.insert(0,str(ROOT/'deploy/release'))
from blob_store import LocalStore
from client_archive import Archive,record
from client_artifacts import check_archive,pack,finalize
from review import canonical
import client_publish as publisher
from server_finish import public_browser


def publish_files(directory):
    destination=PRIVATE/'served-client'
    if destination.is_symlink() or destination.resolve().parent!=PRIVATE:raise ValueError('Wrong fixture publication root')
    for source in sorted(directory.rglob('*')):
        if not source.is_file():continue
        if source.is_symlink():raise ValueError('Invalid fixture artifact')
        target=destination/source.relative_to(directory)
        target.parent.mkdir(parents=True,exist_ok=True)
        descriptor,temporary=tempfile.mkstemp(prefix='.h03-client-',dir=target.parent)
        try:
            with os.fdopen(descriptor,'wb') as stream:stream.write(source.read_bytes());stream.flush();os.fsync(stream.fileno())
            os.replace(temporary,target)
        finally:Path(temporary).unlink(missing_ok=True)


def check_browser(manifest_path,result_path,env):
    public_browser(manifest_path,result_path,env)
    return json.loads(result_path.read_bytes())


def main():
    machine_guard();status=transport('status');lease=transport('client-lease')
    report={'task':'H03','fixture_only':True,'github_metadata_and_SWA_publication_simulated':True,
        'actual_SSH_lease_TLS_and_browser_checks':True,'production_mutation':False,'phase':'starting',
        'overall_attempt_success':False,'cases':[]};output=PRIVATE/'H03-client-restoration.json'
    try:
        before=status.call('status production')
        if before['lease'] or before['maintenance']['active']:raise ValueError('Occupied server/client slot')
        manifest=json.loads(Path('/var/lib/pokerattack/production/known-good/release-manifest.json').read_bytes())
        catalog=Archive(LocalStore(PRIVATE/'accepted-clients'))
        pointers,_=catalog.pointers();accepted=pointers['current']
        predecessor=catalog.accepted_manifest(accepted['run_id'],accepted['run_attempt'],accepted['artifact_id'],accepted['archive_sha256'])
        source=PRIVATE/'H03-new-client-source';archive=PRIVATE/'H03-new-client.tar.gz'
        check_archive(PRIVATE/'downloads/client-v2.tar.gz',predecessor,source)
        (source/'release.json').unlink()  # New explicitly fictional build identity, compiled bytes unchanged.
        run_id,artifact_id=42424242424300,42424242424301
        candidate=finalize(pack(source,archive,manifest['source_sha'],run_id,1),artifact_id)
        env={**context(manifest,1),'GITHUB_RUN_ID':str(run_id),'CLIENT_OPERATION':'release',
             'CLIENT_CANDIDATE_ARTIFACT_ID':str(artifact_id)}
        def github(path):
            if path=='git/ref/heads/main':return {'object':{'sha':manifest['source_sha']}}
            if path.startswith('actions/artifacts/'):return {'expired':False,'workflow_run':{'id':run_id,'head_sha':manifest['source_sha']}}
            if '/jobs?' in path:return {'jobs':[{'name':'deploy','status':'completed','steps':[
                {'name':'Deploy exact prepared client','conclusion':'success'}]}]}
            raise ValueError('Unexpected simulated GitHub operation')
        def service_ready():
            if not json.loads(run('docker','inspect','h02-caddy'))[0]['State']['Running']:raise ValueError('Fixture service unresolved')
        publisher.github=github;publisher.service_ready=service_ready
        workspace=PRIVATE/'H03-client-publication1'
        reserved=publisher.reserve(catalog,status,lease,workspace,candidate,archive,env)
        publisher.recheck(status,workspace,env);publish_files(workspace/'client')
        # A real served-byte mismatch; never replace it with a fake failed result.
        index=PRIVATE/'served-client/index.html';index.write_bytes(index.read_bytes()+b'\n<!-- H03 deliberately wrong served bytes -->\n')
        failed=False
        try:check_browser(workspace/'manifest.json',PRIVATE/'H03-client-candidate-result.json',env)
        except ValueError:failed=True
        if not failed or catalog.pointers()[0]['current']!=accepted:raise ValueError('Failed client was accepted')
        report['cases'].append({'name':'actual-served-byte-verification-failure','passed':True,'candidate_not_accepted':True})
        if not publisher.prepare_restoration(status,workspace,'success'):raise ValueError('Exact predecessor restoration was not selected')
        # A real local write failure models a failed restoration upload. Only
        # this owned static file is immutable; API/database/other apps unaffected.
        release=PRIVATE/'served-client/release.json'
        run('chattr','+i',str(release));restore_failed=False
        try:
            try:publish_files(workspace/'recovery-client')
            except OSError:restore_failed=True
        finally:run('chattr','-i',str(release))
        held=status.call('status production')
        if not restore_failed or not held['lease'] or held['lease']['kind']!='client' or held['runtime']['container_id']!=before['runtime']['container_id']:
            raise ValueError('Failed restoration did not preserve lease/API')
        report['cases'].append({'name':'actual-client-restoration-write-failure','passed':True,'lease_retained':True,'api_unchanged':True})
        report['phase']='failed-restoration-leased';output.write_bytes(canonical(report))
        # Explicit next-attempt reconciliation first reads actual HTTPS identity;
        # no blind repeat of candidate publication or ambiguous source selection.
        states=publisher.served_states(candidate,predecessor)
        if 'unknown' in states:raise ValueError('Unknown fixture identity requires inspection, not another upload')
        resumed_env={**env,'GITHUB_RUN_ATTEMPT':'2'};resumed=PRIVATE/'H03-client-publication2'
        reserved=publisher.reserve(catalog,status,lease,resumed,None,None,resumed_env)
        if reserved['upload_needed']:raise ValueError('Completed candidate upload must not repeat')
        if publisher.prepare_restoration(status,resumed,'success'):publish_files(resumed/'recovery-client')
        verification=check_browser(resumed/'recovery-manifest.json',PRIVATE/'H03-restored-client-result.json',resumed_env)
        result=publisher.commit(catalog,status,lease,resumed,verification,restored=True)
        final=status.call('status production')
        if (result['outcome']!='restored' or final['lease'] is not None or final['runtime']['container_id']!=before['runtime']['container_id']
                or catalog.pointers()[0]['current']!=accepted):raise ValueError('Exact restoration or unchanged API not established')
        report.update(phase='cases-passed',original_failed_attempt_remains_failed=True,
            restored_exact_archive_sha256=predecessor['archive_sha256'],real_verification=verification,
            source_compiled_bytes_rebuilt=False,api_restarted=False,final_status=final)
        report['cases'].append({'name':'explicit-resume-exact-predecessor-restoration','passed':True,'fresh_real_browser_checks':True,
            'no_candidate_reupload':True,'lease_finished':True})
    except Exception as error:
        report.update(phase='failed-or-incomplete',error_type=type(error).__name__)
        try:report['diagnostic_status']=status.call('status production')
        except Exception:report['diagnostic_status']='unavailable'
        raise
    finally:output.write_bytes(canonical(report))


if __name__=='__main__':main()
