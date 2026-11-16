"""H05 actual role/lease overlap and obsolete-candidate refusal on owned VM."""
import copy
import json
from pathlib import Path
import sys

from initialize_replica import machine_guard,PRIVATE,ROOT
from run_release import transport,denied_remote
from run_faults import context,packet,decide,recover
sys.path.insert(0,str(ROOT/'deploy/release'))
from blob_store import LocalStore
from client_archive import Archive
from client_artifacts import identity_summary
from review import canonical
import client_publish
import server_deliver
from server_finish import public_browser


def refused(operation):
    try:operation()
    except (ValueError,RuntimeError):return True
    raise ValueError('Unsafe overlapping or obsolete operation accepted')


def main():
    machine_guard();status=transport('status');client=transport('client-lease');deploy=transport('deployment')
    report={'task':'H05','fixture_only':True,'GitHub_metadata_and_reviews_simulated':True,'production_mutation':False,
            'phase':'starting','cases':[]};output=PRIVATE/'H05-concurrency.json'
    try:
        before=status.call('status production')
        if before['lease'] or before['maintenance']['active']:raise ValueError('Inspect occupied slot before rehearsal')
        manifest=json.loads(Path('/var/lib/pokerattack/production/known-good/release-manifest.json').read_bytes())
        catalog=Archive(LocalStore(PRIVATE/'accepted-clients'))
        pointers,_=catalog.pointers();current=pointers['current']
        accepted=catalog.accepted_manifest(current['run_id'],current['run_attempt'],current['artifact_id'],current['archive_sha256'])
        candidate=json.loads((PRIVATE/'H03-client-publication1/manifest.json').read_bytes())
        run_id,attempt=42424242424400,1
        reservation={'candidate':identity_summary(candidate),'recovery':identity_summary(accepted)}
        env=context(manifest,20);review=packet(manifest,env,status)
        client.call(f'begin-client production {run_id} {attempt}',reservation)
        refused(lambda:server_deliver.deliver(review,env,status,deploy,decide(review,env)))
        acknowledgment={'action':'deploy','digest':manifest['api']['digest'],'config_sha256':manifest['config_sha256'],
                        'source_sha':manifest['source_sha'],'run_id':42424242424242,'run_attempt':20,
                        'approved_unix':review['review_window_started_unix'],'expires_unix':review['review_window_expires_unix'],
                        'drain_seconds':0,'policy':'reset-at-deadline'}
        refused(lambda:deploy.call('begin-release production 42424242424242 20',{'manifest':review['manifest'],'acknowledgment':acknowledgment}))
        held=status.call('status production')
        if held['lease']['kind']!='client' or held['runtime']!=before['runtime'] or held['maintenance']['active']:
            raise ValueError('Server stole client slot or changed runtime')
        report['cases'].append({'name':'server-request-while-client-reserved','passed':True,'no_nested_lock_wait':True,'API_unchanged':True})
        # No publication occurred. Real fresh public browser checks of the exact
        # accepted predecessor authorize ending the reserved slot as restored.
        identity=PRIVATE/'H05-current-client.json';result=PRIVATE/'H05-current-client-verified.json'
        identity.write_bytes(canonical(accepted));public_browser(identity,result,context(manifest,1))
        verified=json.loads(result.read_bytes())
        client.call(f'finish-client production {run_id} {attempt}',{'outcome':'restored','client':identity_summary(accepted),
                    'verified_unix':verified['verified_unix'],'checks':verified['checks']})
        incompatible=copy.deepcopy(reservation);incompatible['candidate']['minimum_server_capability']=2
        refused(lambda:client.call(f'begin-client production {run_id} 2',incompatible))
        if status.call('status production')['lease']:raise ValueError('Incompatible client retained an unusable lease')
        client_env={**context(manifest,1),'GITHUB_RUN_ID':str(candidate['workflow']['run_id']),
                    'CLIENT_CANDIDATE_ARTIFACT_ID':str(candidate['artifact_id'])}
        client_publish.github=lambda _:{'object':{'sha':'f'*40}}
        refused(lambda:client_publish.reserve(catalog,status,client,PRIVATE/'H05-stale-client',candidate,
                    PRIVATE/'H03-new-client.tar.gz',client_env))
        refused(lambda:client_publish.reserve(catalog,status,client,PRIVATE/'H05-wrong-branch',candidate,
                    PRIVATE/'H03-new-client.tar.gz',{**client_env,'GITHUB_REF':'refs/heads/untrusted'}))
        if status.call('status production')['lease']:raise ValueError('Obsolete source held the slot')
        report['cases'].append({'name':'incompatible-stale-branch-candidates','passed':True,'slot_free_without_waiting_for_server':True})
        # The reciprocal overlap is real maintenance plus pending digest delivery.
        review=packet(manifest,env,status)
        server_deliver.deliver(review,env,status,deploy,decide(review,env))
        pending=status.call('status production')
        refused(lambda:client.call(f'begin-client production {run_id} 3',reservation))
        after=status.call('status production')
        if after['pairs']!=pending['pairs'] or after['lease']!=pending['lease'] or not after['maintenance']['active']:
            raise ValueError('Client changed pending server or reopened maintenance')
        for role in ('deployment','client-lease','evidence','promotion'):
            denied_remote(role,'operator-bootstrap client-authorize production 1 1')
        report['cases'].append({'name':'client-request-while-server-pending-and-no-root-bootstrap-key-authority','passed':True,'server_slot_unchanged':True})
        report['recovery']=recover(21,status)
        report['final_status']=status.call('status production');report['phase']='cases-passed'
        report['bootstrap_introduction']='not yet executed; H05 remains incomplete'
    except Exception as error:
        report.update(phase='failed-or-incomplete',error_type=type(error).__name__)
        try:report['diagnostic_status']=status.call('status production')
        except Exception:report['diagnostic_status']='unavailable'
        raise
    finally:output.write_bytes(canonical(report))


if __name__=='__main__':main()
