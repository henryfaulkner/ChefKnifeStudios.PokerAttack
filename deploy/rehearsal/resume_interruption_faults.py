"""H03 actual failed restoration and lost SSH, with inspected explicit recovery."""
import copy
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import time
import urllib.request
import uuid

from initialize_replica import machine_guard,PRIVATE,ROOT,run
from run_release import transport
from run_faults import context,packet,decide,recover
sys.path.insert(0,'/opt/pokerattack')
import secure_state
from review import canonical
import server_deliver


def normal_lobby(player,lobby=None):
    body={'hostPlayer':player} if lobby is None else {'lobbyId':lobby,'player':player}
    route='/lobby/create-lobby' if lobby is None else '/lobby/remove-player'
    request=urllib.request.Request('https://api.playpokerattack.com'+route,data=canonical(body),method='POST',headers={'Content-Type':'application/json'})
    with urllib.request.urlopen(request,timeout=10) as response:result=json.load(response)
    if result.get('isSuccess') is not True:raise ValueError('Owned normal lobby operation failed')
    return result.get('value')


def main():
    machine_guard();status=transport('status');base=Path('/var/lib/pokerattack/production')
    report=json.loads((PRIVATE/'H03-restoration-SSH.json').read_bytes())
    output=PRIVATE/'H03-restoration-SSH-reconciled.json'
    try:
        observed=status.call('status production')
        if not observed['consistent'] or not observed['maintenance']['active'] or (observed.get('lease') or {}).get('run_attempt')!=16:
            raise ValueError('Interrupted attempt16 differs; inspect without retry')
        report['prior_harness_failure']={'type':'AttributeError','cause':'status polling read absent lease during begin race','original_failure_preserved':True}
        report['interrupted_runner_status']=observed
        report['inspected_runner_recovery']=recover(17,status)
        # Repeat only the SSH interruption case after explicit recovery.
        # An owned ordinary lobby makes begin-release wait for drain.
        before=status.call('status production');player={'id':str(uuid.uuid4()),'name':'H03 owned lost-SSH lobby'}
        lobby=normal_lobby(player)['id']
        owned=PRIVATE/'H03-interrupted-owned-lobby-18.json'
        owned.write_bytes(canonical({'player':player,'lobby_id':lobby}));owned.chmod(0o600)
        manifest=json.loads((base/'known-good/release-manifest.json').read_bytes());env=context(manifest,18)
        review=packet(manifest,env,status);run_id=int(env['GITHUB_RUN_ID'])
        ack={'action':'deploy','digest':manifest['api']['digest'],'config_sha256':manifest['config_sha256'],
            'source_sha':manifest['source_sha'],'run_id':run_id,'run_attempt':18,
            'approved_unix':review['review_window_started_unix'],'expires_unix':review['review_window_expires_unix'],
            'drain_seconds':300,'policy':'reset-at-deadline'}
        # This invokes the actual restricted SSH account, then deliberately loses
        # the caller. The durable lease must survive regardless of remote SIGHUP.
        args=['ssh','-T','-F','/dev/null','-i',str(PRIVATE/'keys/deployment'),'-o','BatchMode=yes','-o','IdentitiesOnly=yes',
            '-o','IdentityAgent=none','-o','ClearAllForwardings=yes','-o','StrictHostKeyChecking=yes',
            '-o','UserKnownHostsFile='+str(PRIVATE/'known_hosts'),'-o','ConnectTimeout=5',
            'pa-deploy@170.187.142.115',f'begin-release production {run_id} 18']
        process=subprocess.Popen(args,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
        process.stdin.write(canonical({'manifest':{**manifest,'workflow':{**manifest['workflow'],'run_attempt':18}},'acknowledgment':ack}));process.stdin.close()
        established=False
        for _ in range(20):
            current=status.call('status production')
            if ((current.get('lease') or {}).get('run_attempt')==18 and current['lease']['phase']=='draining'
                    and current['consistent'] and current['maintenance']['active']):established=True;break
            if process.poll() is not None:break
            time.sleep(0.2)
        if not established:raise ValueError('Real SSH drain did not establish before interruption')
        process.kill();process.wait(timeout=10)
        lost=status.call('status production')
        if not lost['maintenance']['active'] or not lost['lease'] or lost['runtime']['container_id']!=before['runtime']['container_id']:
            raise ValueError('Lost SSH did not preserve serving API and durable gate/lease')
        report['cases'].append({'name':'real-SSH-client-killed-after-durable-begin','passed':True,'api_unchanged':True,
            'gate_and_lease_persisted':True,'no_automatic_retry_or_reopen':True})
        normal_lobby(player,lobby)  # Normal exact-owner leave only.
        report['SSH_recovery']=recover(19,status)
        report['final_status']=status.call('status production')
        report['phase']='partial-cases-passed';report['remaining_H03_cases']=['actual client artifact restoration/failure']
    except Exception as error:
        report.update(phase='failed-or-incomplete',error_type=type(error).__name__)
        try:report['diagnostic_status']=status.call('status production')
        except Exception:report['diagnostic_status']='unavailable'
        raise
    finally:output.write_bytes(canonical(report))

if __name__=='__main__':main()
