"""H03 real SSH/host failures on only the owned disposable replica."""
import copy
import datetime
import json
import os
from pathlib import Path
import sys
import time

from initialize_replica import machine_guard,PRIVATE,ROOT
from run_release import transport
sys.path.insert(0,'/opt/pokerattack')
import secure_state
sys.path.insert(0,str(ROOT/'deploy/release'))
from blob_store import LocalStore
from client_archive import Archive
from review import canonical
from server_images import execution
import server_manifest,server_deliver,server_verify,server_promote,server_finish


def context(manifest,attempt,operation='release'):
    return {**os.environ,'GITHUB_SHA':manifest['source_sha'],'GITHUB_RUN_ID':'42424242424242',
        'GITHUB_RUN_ATTEMPT':str(attempt),'GITHUB_REF':'refs/heads/main','GITHUB_EVENT_NAME':'workflow_dispatch',
        'GITHUB_REPOSITORY':'henryfaulkner/ChefKnifeStudios.PokerAttack','SERVER_OPERATION':operation,
        'DEPLOYMENT_AUTOMATION_ENABLED':'true','RUNNER_TEMP':str(PRIVATE/'runner-temp'),'HOME':str(Path.home()),
        'RELEASE_DRAIN_SECONDS':'3','APPROVED_CONFIG_SHA256':manifest['config_sha256'],
        'NODE_EXTRA_CA_CERTS':str(PRIVATE/'tls/ca.crt')}


def decide(packet,env):
    reviews=[{'state':'approved','comment':packet['reset_review']['comment'],
              'environments':[{'name':'server-linode'}],'user':{'login':'isolated-rehearsal'}}]
    return lambda path:{'object':{'sha':env['GITHUB_SHA']}} if path=='git/ref/heads/main' else reviews


def packet(manifest,env,status):
    published={'format':'pokerattack-published-images-v1','source_sha':manifest['source_sha'],
        'workflow':{**execution(env)[1],'run_attempt':1},'images':{key:manifest[key] for key in ('api','migration')},
        'tested_archive_sha256':json.loads((PRIVATE/'inputs.json').read_bytes())['artifacts']['images.tar.gz']['sha256']}
    return server_manifest.resolve(published,manifest['schema'],status.call('status production'),manifest['config_sha256'],env)


def recover(attempt,status):
    target=status.call('rollback-target production');manifest=target['manifest'];env=context(manifest,attempt,'rollback')
    review=server_manifest.resolve_rollback(target,manifest['schema'],env)
    delivery=server_deliver.deliver(review,env,status,transport('deployment'),decide(review,env))
    manifest=review['manifest'];directory=PRIVATE/('H03-verification-'+str(attempt))
    archive=Archive(LocalStore(PRIVATE/'accepted-clients'))
    proof=server_verify.verify(manifest,env,status,transport('evidence'),archive,directory)
    raw=(directory/'evidence.json').read_bytes()
    reviews=[{'state':'approved','comment':proof['comment'],'environments':[{'name':'server-promotion'}],
              'user':{'login':'isolated-rehearsal'}}]
    promoted=server_promote.promote(manifest,raw,proof,env,status,transport('promotion'),lambda _:reviews)
    complete=server_finish.finish(manifest,promoted,env,status,transport('promotion'),archive,PRIVATE/('H03-completion-'+str(attempt)+'.json'))
    return {'fresh_reset':True,'recorded_target':target['recovery_pair'],'delivery':delivery,
            'new_real_evidence_sha256':proof['evidence_sha256'],'separate_promotion':True,'completion':complete}


def main():
    machine_guard();status=transport('status');report={'task':'H03','fixture_only':True,'github_reviews_simulated':True,
        'production_mutation':False,'phase':'starting','all_H03_cases_complete':False,'cases':[],
        'started_utc':datetime.datetime.now(datetime.timezone.utc).isoformat()}
    if sys.argv[1:] not in ([],['--repeat-after-fix']):raise ValueError('Explicit known-outcome repetition only')
    repeat=bool(sys.argv[1:]);first=8 if repeat else 6
    path=PRIVATE/('H03-pull-lock-recovery-attempt2.json' if repeat else 'H03-pull-lock-recovery.json')
    try:
        previous=json.loads((PRIVATE/'H02-result-attempt5.json').read_bytes())
        if previous['phase']!='complete':raise ValueError('H02 must complete first')
        manifest=copy.deepcopy(previous['manifest']);env=context(manifest,first)
        before=status.call('status production')
        if before['lease'] or before['maintenance']['active']:raise ValueError('Inspect existing slot before fault injection')
        # A real nonblocking host lock, not a mock Docker/sudo executable.
        review=packet(manifest,env,status)
        with secure_state.deployment_lock():
            denied=False
            try:server_deliver.deliver(review,env,status,transport('deployment'),decide(review,env))
            except RuntimeError:denied=True
            if not denied:raise ValueError('Host lock contention did not reject begin')
            during=status.call('status production')
            if during['lease'] or during['maintenance']['active'] or during['runtime']['container_id']!=before['runtime']['container_id']:
                raise ValueError('Lock contention mutated the serving release')
        report['cases'].append({'name':'real-host-lock-contention','passed':True,'diagnostics_available':True,'api_unchanged':True})
        # Registry returns a real missing-manifest error. Keep the exact tested
        # local ID/source but intentionally request a nonexistent digest.
        bad=copy.deepcopy(manifest);bad['api']['digest']='sha256:'+'e'*64
        review=packet(bad,env,status);failed=False
        try:server_deliver.deliver(review,env,status,transport('deployment'),decide(review,env))
        except RuntimeError:failed=True
        after=status.call('status production')
        if (not failed or after['lease']['phase']!='failed' or after['runtime']['container_id']!=before['runtime']['container_id']
                or after['pairs']['active']!=before['pairs']['active'] or not after['maintenance']['active'] or after['transaction']):
            raise ValueError('Failed pull did not preserve the API and durable closed failure')
        report['cases'].append({'name':'real-missing-registry-manifest-before-stop','passed':True,'api_unchanged':True,
            'failed_attempt_stays_failed':True,'maintenance_retained':True,'status':after})
        report['phase']='failed-pull-preserved-serving-pair';path.write_bytes(canonical(report))
        report['recovery']=recover(first+1,status)
        final=status.call('status production')
        report['final_status']=final
        report['previous_good_preserved']=final['pairs']['previous-good']==before['pairs']['previous-good']
        if not report['previous_good_preserved']:raise ValueError('Same-pair recovery discarded the older distinct previous-good pair')
        report['phase']='partial-cases-passed';report['cases_passed']=2
    except Exception as error:
        report['phase']='failed-or-incomplete';report['error_type']=type(error).__name__
        # Only this explicitly known assertion is public diagnosis; no provider
        # output or private context can escape into the report.
        if str(error)=='Same-pair recovery discarded the older distinct previous-good pair':report['failure']='previous-good rotated to a duplicate pair'
        try:report['diagnostic_status']=status.call('status production')
        except Exception:report['diagnostic_status']='unavailable'
        raise
    finally:path.write_bytes(canonical(report))


if __name__=='__main__':main()
