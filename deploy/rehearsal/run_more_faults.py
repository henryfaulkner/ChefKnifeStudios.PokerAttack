"""H03 actual readiness, evidence, promotion and reopening failures on replica."""
import copy
import hashlib
import json
from pathlib import Path
import re
import sys
import time
import uuid

from initialize_replica import machine_guard,PRIVATE,ROOT,run
from run_release import transport
from run_faults import context,packet,decide
sys.path.insert(0,str(ROOT/'deploy/release'))
from blob_store import LocalStore
from client_archive import Archive
from review import canonical
from server_images import execution
import server_manifest,server_deliver,server_verify,server_promote,server_finish


def rejected(function):
    try:function()
    except RuntimeError:return True
    raise ValueError('Injected invalid operation was accepted')


def main():
    machine_guard();status=transport('status');evidence=transport('evidence');promotion=transport('promotion')
    report={'task':'H03','fixture_only':True,'github_reviews_simulated':True,'production_mutation':False,
        'all_H03_cases_complete':False,'phase':'starting','cases':[]}
    path=PRIVATE/'H03-readiness-evidence-finalization.json'
    try:
        before=status.call('status production')
        if before['lease'] or before['maintenance']['active']:raise ValueError('Inspect existing slot before new faults')
        base=Path('/var/lib/pokerattack/production')
        manifest=json.loads((base/'known-good/release-manifest.json').read_bytes())
        raw=(base/'known-good/app.env').read_bytes()
        changed,count=re.subn(rb'Password=[A-Za-z0-9_-]{32,};SSL Mode=Disable',b'Password='+b'INVALID_H03_'*4+b';SSL Mode=Disable',raw)
        if count!=1:raise ValueError('Expected one protected fixture connection')
        digest=hashlib.sha256(changed).hexdigest();config=Path('/etc/pokerattack/production/configs')/(digest+'.env')
        if config.exists():raise ValueError('Readiness fault already provisioned; inspect before repeating')
        config.write_bytes(changed);config.chmod(0o600)
        bad={**manifest,'config_sha256':digest};env=context(bad,10);review=packet(bad,env,status)
        rejected(lambda:server_deliver.deliver(review,env,status,transport('deployment'),decide(review,env)))
        after=status.call('status production')
        if (after['lease']['phase']!='failed' or after['transaction'] or not after['readiness']['ready']
                or after['pairs']['active']!=before['pairs']['active'] or not after['maintenance']['active']):
            raise ValueError('Real readiness failure did not restore exact serving pair while remaining failed')
        runtime=json.loads(run('docker','inspect','pokerattack-production-api-1'))[0]
        declared=[value.split('=',1)[1] for value in runtime['Config']['Env'] if value.startswith('Hosting__ReleaseConfigSha256=')]
        if declared!=[before['pairs']['active']['config_sha256']]:raise ValueError('Actual restored runtime config differs')
        report['cases'].append({'name':'real-candidate-database-readiness-failure','passed':True,'exact_runtime_config_restored':True,
            'failed_attempt_remains_failed':True,'maintenance_retained':True,'status':after})
        report['phase']='readiness-restored';path.write_bytes(canonical(report))
        target=status.call('rollback-target production');manifest=target['manifest'];env=context(manifest,11,'rollback')
        review=server_manifest.resolve_rollback(target,manifest['schema'],env)
        server_deliver.deliver(review,env,status,transport('deployment'),decide(review,env))
        run_id=execution(env)[1]['run_id']
        players=[{'id':str(uuid.uuid4()),'name':f'Release-{run_id}-11-{index}'} for index in range(3)]
        grant=evidence.call(f'verifier-credential production {run_id} 11',{'players':players})
        evidence.call(f'revoke-verifier production {run_id} 11')
        # Negative fixtures copied from actual passing proof, intentionally
        # invalid, never saved/presented as promotable evidence.
        original=json.loads((PRIVATE/'generated-verification-attempt5/evidence.json').read_bytes())
        stale={**original,'workflow':execution(env)[1],'verifier_id':grant['verifier_id'],'verified_unix':int(time.time())-1801}
        rejected(lambda:evidence.call(f'import-evidence production {run_id} 11',stale))
        wrong={**stale,'source_sha':'0'*40,'verified_unix':int(time.time())}
        rejected(lambda:evidence.call(f'import-evidence production {run_id} 11',wrong))
        observed=status.call('status production')
        if observed['lease'].get('evidence_sha256') or not observed['maintenance']['active']:raise ValueError('Invalid evidence changed promotion authority')
        report['cases'].append({'name':'actual-stale-and-wrong-source-import-refusals','passed':True,'no_evidence_imported':True,'verifier_revoked':True})
        archive=Archive(LocalStore(PRIVATE/'accepted-clients'));env=context(manifest,12,'rollback')
        directory=PRIVATE/'H03-negative-evidence-resumed12'
        proof=server_verify.verify(manifest,env,status,evidence,archive,directory)
        raw_proof=(directory/'evidence.json').read_bytes()
        rejected(lambda:promotion.call(f"promote production {manifest['api']['digest']} {'0'*64} {run_id} 12"))
        report['cases'].append({'name':'actual-wrong-import-hash-promotion-refused','passed':True})
        # Inject only the legacy promotion call's failure. Real SSH/controller
        # code must record unknown and keep pending/maintenance; no pointer write.
        legacy=Path('/usr/local/sbin/pokerattack-deploy');saved=PRIVATE/'pokerattack-deploy-before-promotion-fault'
        if saved.exists():raise ValueError('Promotion fault backup already exists')
        saved.write_bytes(legacy.read_bytes());saved.chmod(0o700)
        legacy.write_text('#!/bin/bash\nif [[ $1 == promote && $2 == production ]]; then exit 73; fi\nexec '+str(saved)+' "$@"\n');legacy.chmod(0o755)
        reviews=[{'state':'approved','comment':proof['comment'],'environments':[{'name':'server-promotion'}],'user':{'login':'isolated-rehearsal'}}]
        try:rejected(lambda:server_promote.promote(manifest,raw_proof,proof,env,status,promotion,lambda _:reviews))
        finally:legacy.write_bytes(saved.read_bytes());legacy.chmod(0o755)
        observed=status.call('status production')
        if observed['lease']['phase']!='promotion-unknown' or not observed['maintenance']['active'] or not observed['pairs']['pending']:
            raise ValueError('Promotion failure did not preserve closed pending state')
        report['cases'].append({'name':'real-promotion-command-failure','passed':True,'phase':'promotion-unknown','maintenance_retained':True})
        report['phase']='promotion-failure-retained';path.write_bytes(canonical(report))
        env=context(manifest,13,'rollback');directory=PRIVATE/'H03-promotion-resumed13'
        proof=server_verify.verify(manifest,env,status,evidence,archive,directory)
        raw_proof=(directory/'evidence.json').read_bytes()
        reviews=[{'state':'approved','comment':proof['comment'],'environments':[{'name':'server-promotion'}],'user':{'login':'isolated-rehearsal'}}]
        promoted=server_promote.promote(manifest,raw_proof,proof,env,status,promotion,lambda _:reviews)
        completed=base/'completed';backup=base/'completed-h03-directory-backup'
        if backup.exists():raise ValueError('Finalization backup already exists')
        completed.rename(backup);completed.write_text('H03 intentional non-directory finalization fault\n');completed.chmod(0o600)
        failed=PRIVATE/'H03-failed-finish13.json'
        try:rejected(lambda:server_finish.finish(manifest,promoted,env,status,promotion,archive,failed))
        finally:completed.unlink();backup.rename(completed)
        observed=status.call('status production');failed_result=json.loads(failed.read_bytes())
        if observed['lease']['phase']!='promoted' or not observed['maintenance']['active'] or failed_result['overall_release_complete']:
            raise ValueError('Failed reopening was confused with completed release')
        report['cases'].append({'name':'real-finalization-write-failure-after-promotion','passed':True,
            'promoted_but_closed':True,'original_result_failed':True,'recorded_stage':failed_result['phase']})
        report['repaired_finish']=server_finish.finish(manifest,promoted,env,status,promotion,archive,PRIVATE/'H03-repaired-finish13.json')
        report['final_status']=status.call('status production')
        if report['final_status']['pairs']['previous-good']!=before['pairs']['previous-good']:raise ValueError('Distinct prior recovery pair changed')
        report['phase']='partial-cases-passed';report['original_failure_results_preserved']=True
        report['remaining_H03_cases']=['failed-restoration transaction/inspection recovery','runner or SSH interruption','actual client artifact restoration/failure']
    except Exception as error:
        report.update(phase='failed-or-incomplete',error_type=type(error).__name__)
        try:report['diagnostic_status']=status.call('status production')
        except Exception:report['diagnostic_status']='unavailable'
        raise
    finally:path.write_bytes(canonical(report))


if __name__=='__main__':main()
