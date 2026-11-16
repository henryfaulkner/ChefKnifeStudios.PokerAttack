"""Actual H02 SSH/controller/browser transition on the owned loopback replica.

GitHub review history is simulated explicitly; gameplay proof is produced only
by the real passing production-safe verifier. No production credentials or
endpoints are used. No new validation timestamps or proof booleans are invented.
"""
import datetime
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import sys
import time
import urllib.request
import uuid

from initialize_replica import machine_guard,PRIVATE,ROOT,run

sys.path.insert(0,str(ROOT/'deploy/release'))
from blob_store import LocalStore
from client_archive import Archive
from review import canonical
from server_images import execution
from ssh_client import Transport,PROFILES
import server_manifest
import server_deliver
import server_verify
import server_promote
import server_finish


def transport(role):return Transport(role,PRIVATE/'keys'/role,PRIVATE/'known_hosts')


def denied_remote(role,command):
    result=subprocess.run(['ssh','-T','-F','/dev/null','-i',str(PRIVATE/'keys'/role),
        '-o','BatchMode=yes','-o','IdentitiesOnly=yes','-o','IdentityAgent=none','-o','ClearAllForwardings=yes',
        '-o','UserKnownHostsFile='+str(PRIVATE/'known_hosts'),'-o','StrictHostKeyChecking=yes',
        '-o','ConnectTimeout=5',PROFILES[role][0]+'@170.187.142.115',command],capture_output=True,timeout=15)
    if result.returncode==0:raise ValueError('Forbidden remote command was accepted')
    return {'role':role,'command':command,'exit_code':result.returncode,'denied':True}


def ordinary_admission():
    # Real ordinary HTTP lobby creation/host leave after gate release. A new
    # UUID player is the only owner; no global cleanup or verifier cookie.
    player={'id':str(uuid.uuid4()),'name':'H02 ordinary admission'}
    base='https://api.playpokerattack.com'
    request=urllib.request.Request(base+'/lobby/create-lobby',data=canonical({'hostPlayer':player}),
                                  headers={'Content-Type':'application/json'},method='POST')
    # The route names below are taken from the same shared endpoint constants;
    # keep this fixture honest if the product changes them.
    with urllib.request.urlopen(request,timeout=10) as response:created=json.load(response)
    lobby=created.get('value',created.get('data',created))
    if not isinstance(lobby,dict) or not lobby.get('id'):raise ValueError('Normal lobby creation not confirmed')
    request=urllib.request.Request(base+'/lobby/remove-player',data=canonical({'lobbyId':lobby['id'],'player':player}),
                                  headers={'Content-Type':'application/json'},method='POST')
    with urllib.request.urlopen(request,timeout=10) as response:
        if response.status!=200:raise ValueError('Owned normal lobby did not close')
    return {'normal_lobby_created_and_closed':True}


def main():
    machine_guard()
    fresh=sys.argv[1:]==['--fresh-release','5']
    if sys.argv[1:] and not fresh and not (len(sys.argv)==3 and sys.argv[1]=='--resume-verification' and re.fullmatch(r'[2-9]',sys.argv[2])):
        raise ValueError('Only explicit established verification resumes are supported')
    resume=bool(sys.argv[1:]) and not fresh;attempt=int(sys.argv[2]) if sys.argv[1:] else 1
    prior=json.loads((PRIVATE/'H02-result.json').read_bytes()) if resume else None
    report={'task':'H02','fixture_only':True,'github_review_api_simulated':True,
        'workflow_and_artifact_ids_are_fictional':True,'production_mutation':False,'phase':'starting',
        'started_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'steps':[]}
    output=PRIVATE/(f'H02-result-attempt{attempt}.json' if sys.argv[1:] else 'H02-result.json')
    def stage(name,function):
        started=time.monotonic();result=function()
        report['steps'].append({'name':name,'seconds':round(time.monotonic()-started,3),'passed':True})
        report['phase']=name
        output.write_bytes(canonical(report));return result
    status=transport('status')
    try:
        baseline=json.loads((PRIVATE/'seeded-baseline.json').read_bytes())
        original=prior['manifest'] if resume else json.loads((PRIVATE/'server-manifest.json').read_bytes())
        if fresh:
            repaired=json.loads((PRIVATE/'H02-result-attempt4.json').read_bytes())
            if repaired.get('phase')!='complete' or repaired.get('overall_release_complete') is not True:
                raise ValueError('Repaired release must complete before the clean full transition')
            original=repaired['manifest']
            config_root=Path('/etc/pokerattack/production/configs')
            raw=(config_root/(original['config_sha256']+'.env')).read_bytes()+b'# H02 clean full transition after fixture repairs\n'
            digest=hashlib.sha256(raw).hexdigest();path=config_root/(digest+'.env')
            if path.exists():raise ValueError('Fresh candidate already provisioned; inspect before repeating')
            path.write_bytes(raw);path.chmod(0o600)
            original={**original,'config_sha256':digest}
            report['clean_full_transition_after_fixture_repairs']=True
        inputs=json.loads((PRIVATE/'inputs.json').read_bytes())
        env={**os.environ,'GITHUB_SHA':original['source_sha'],'GITHUB_RUN_ID':str(original['workflow']['run_id']),
            'GITHUB_RUN_ATTEMPT':str(attempt),'GITHUB_REF':'refs/heads/main','GITHUB_EVENT_NAME':'workflow_dispatch',
            'GITHUB_REPOSITORY':'henryfaulkner/ChefKnifeStudios.PokerAttack','SERVER_OPERATION':'release',
            'DEPLOYMENT_AUTOMATION_ENABLED':'true','RUNNER_TEMP':str(PRIVATE/'runner-temp'),
            'HOME':str(Path.home()),
            'RELEASE_DRAIN_SECONDS':'3','APPROVED_CONFIG_SHA256':original['config_sha256'],
            'NODE_EXTRA_CA_CERTS':str(PRIVATE/'tls/ca.crt')}
        report['source_sha']=original['source_sha'];report['api_image_id']=original['api']['local_image_id']
        report['denied_commands']=stage('restricted-SSH-denials',lambda:[denied_remote('status','id'),
            denied_remote('deployment','promote production '+original['api']['digest']+' '+'0'*64+' 1 1'),
            denied_remote('evidence','rollback production 1 1')])
        live=stage('fresh-structured-status',lambda:status.call('status production'))
        workflow=execution(env)[1]
        published={'format':'pokerattack-published-images-v1','source_sha':original['source_sha'],'workflow':original['workflow'],
            'images':{key:original[key] for key in ('api','migration')},'tested_archive_sha256':inputs['artifacts']['images.tar.gz']['sha256']}
        if resume:
            manifest=original
            report.update(manifest=manifest,delivery=prior['delivery'],reset_packet_sha256=prior['reset_packet_sha256'],
                resumed_verification_only=True,original_attempt_failed=True,prior_steps=prior['steps'])
            stage('matching-established-candidate-before-resume',lambda:server_verify.pending(live,manifest,workflow['run_id'],resume_attempt=attempt))
        else:
            packet=stage('immutable-review-packet',lambda:server_manifest.resolve(published,original['schema'],live,original['config_sha256'],env))
            manifest=packet['manifest'];report['manifest']=manifest;report['reset_packet_sha256']=packet['reset_review']['packet_sha256']
            reviews=[{'state':'approved','comment':packet['reset_review']['comment'],
                      'environments':[{'name':'server-linode'}],'user':{'login':'isolated-rehearsal'}}]
            api=lambda path:{'object':{'sha':env['GITHUB_SHA']}} if path=='git/ref/heads/main' else reviews
            report['delivery']=stage('acknowledged-drain-digest-delivery',lambda:server_deliver.deliver(packet,env,status,transport('deployment'),api))
        pending=status.call('status production')
        if not pending['maintenance']['active'] or not pending['pairs']['pending']:raise ValueError('Pending gate not established')
        report['pending_status']=pending
        archive=Archive(LocalStore(PRIVATE/'accepted-clients'))
        directory=PRIVATE/(f'generated-verification-attempt{attempt}' if sys.argv[1:] else 'generated-verification')
        review=stage('real-HTTPS-gameplay-and-evidence-import',lambda:server_verify.verify(manifest,env,status,transport('evidence'),archive,directory))
        raw=(directory/'evidence.json').read_bytes()
        report['evidence_sha256']=hashlib.sha256(raw).hexdigest();report['evidence']=json.loads(raw)
        reviews=[{'state':'approved','comment':review['comment'],
                  'environments':[{'name':'server-promotion'}],'user':{'login':'isolated-rehearsal'}}]
        promotion=stage('explicit-matching-promotion',lambda:server_promote.promote(manifest,raw,review,env,status,transport('promotion'),lambda _:reviews))
        report['promotion']=promotion
        completion=stage('reopening-and-real-public-browser-checks',lambda:server_finish.finish(manifest,promotion,env,status,transport('promotion'),archive,PRIVATE/'completion.json'))
        report['completion']=completion
        report['ordinary_admission']=stage('ordinary-owned-lobby-after-reopening',ordinary_admission)
        final=status.call('status production');report['final_status']=final
        if final.get('sessions')!={'activeGames':0,'lobbies':0,'soloGames':0}:raise ValueError('Ordinary owned fixture sessions remain')
        for name,cid in baseline['canary_containers'].items():
            if json.loads(run('docker','inspect',name))[0]['Id']!=cid:raise ValueError('Shared fixture service was replaced')
        canary=run('/usr/sbin/runuser','-u','postgres','--','psql','-X','-q','-t','-A','-d','transitjazz_fixture','-c','BEGIN READ ONLY; SELECT value FROM canary; ROLLBACK;')
        if canary!='42':raise ValueError('Shared fixture database canary differs')
        completed=json.loads((Path('/var/lib/pokerattack/production/completed')/(env['GITHUB_RUN_ID']+'-'+str(attempt)+'.json')).read_bytes())
        if completed.get('verifier_revoked') is not True:raise ValueError('Verifier revocation not archived')
        report.update(phase='complete',overall_release_complete=completion['overall_release_complete'],
            verifier_revoked=True,other_services_preserved=True,other_database_preserved=True,
            completed_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
            versions={'node':run('node','--version'),'docker':run('docker','--version'),'compose':run('docker','compose','version')})
    except Exception as error:
        report.update(phase='failed-at-'+report['phase'],overall_release_complete=False,error_type=type(error).__name__)
        match=re.fullmatch(r'Protected gameplay failed at ([a-z-]+); no evidence may be imported',str(error))
        if match and (match[1] in server_verify.FAILURE_STAGES or match[1]=='unknown'):report['verification_failure_stage']=match[1]
        try:report['diagnostic_status']=status.call('status production')
        except Exception:report['diagnostic_status']='unavailable; inspect without mutation'
        raise
    finally:
        output.write_bytes(canonical(report))


if __name__=='__main__':main()
