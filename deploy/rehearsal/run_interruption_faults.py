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
    report={'task':'H03','fixture_only':True,'github_reviews_simulated':True,'production_mutation':False,
        'all_H03_cases_complete':False,'phase':'starting','cases':[]};output=PRIVATE/'H03-restoration-SSH.json'
    try:
        before=status.call('status production')
        if before['lease'] or before['maintenance']['active']:raise ValueError('Inspect occupied slot before new faults')
        good=(base/'known-good').resolve(strict=True)
        manifest=json.loads((good/'release-manifest.json').read_bytes())
        raw=(good/'app.env').read_bytes()
        changed,count=re.subn(rb'Password=[A-Za-z0-9_-]{32,};SSL Mode=Disable',b'Password='+b'INVALID_RESTORE_'*3+b';SSL Mode=Disable',raw)
        if count!=1:raise ValueError('Expected one fixture runtime connection')
        changed+=b'# H03 deliberate failed restoration\n';digest=hashlib.sha256(changed).hexdigest()
        config=Path('/etc/pokerattack/production/configs')/(digest+'.env')
        if config.exists():raise ValueError('Fault already provisioned; inspect before repeating')
        config.write_bytes(changed);config.chmod(0o600)
        bad={**manifest,'config_sha256':digest};env=context(bad,14);review=packet(bad,env,status)
        wrapper=Path('/usr/local/bin/docker')
        if wrapper.exists() or wrapper.is_symlink():raise ValueError('Refuse to replace existing Docker executable')
        code='''#!/usr/bin/python3
import os,sys
args=sys.argv[1:]
if args and args[0]=='compose' and '--env-file' in args and 'up' in args and args[args.index('--env-file')+1]=='''+repr(str(good/'release.env'))+''':
 sys.exit(76)
os.execv('/usr/bin/docker',['/usr/bin/docker',*args])
'''
        wrapper.write_text(code);wrapper.chmod(0o755)
        failed=False
        try:
            try:server_deliver.deliver(review,env,status,transport('deployment'),decide(review,env))
            except RuntimeError:failed=True
        finally:
            if wrapper.read_text()!=code:raise ValueError('Fault wrapper changed; refuse deletion')
            wrapper.unlink()
        observed=status.call('status production')
        if not failed or not observed['transaction'] or not observed['maintenance']['active'] or observed['readiness']['ready']:
            raise ValueError('Failed restoration did not retain an unready transaction and maintenance')
        report['cases'].append({'name':'actual-candidate-and-restoration-failure','passed':True,'transaction_retained':True,
            'automatic_retry_refused':True,'failed_attempt_stays_failed':True,'status':observed})
        report['phase']='failed-restoration-retained';output.write_bytes(canonical(report))
        # Console recovery is a separate, deliberate inspection. Never blindly
        # remove the marker or retry deployment. Check both snapshot hashes and
        # the actual transaction paths under the two host locks first.
        with secure_state.control_lock(),secure_state.deployment_lock():
            marker=base/'transaction'
            if marker.is_symlink():raise ValueError('Unsafe transaction marker')
            lines=marker.read_text().splitlines()
            if len(lines)!=2 or Path(lines[0])!=good:raise ValueError('Transaction differs from inspected failed operation')
            candidate=Path(lines[1]).resolve(strict=True)
            if candidate.parent!=(base/'releases').resolve() or (candidate/'config-id').read_text().strip()!=digest:
                raise ValueError('Unexpected failed candidate snapshot')
            if hashlib.sha256((candidate/'app.env').read_bytes()).hexdigest()!=digest:raise ValueError('Failed candidate bytes changed')
            expected=before['pairs']['known-good']
            if hashlib.sha256((good/'app.env').read_bytes()).hexdigest()!=expected['config_sha256'] or (good/'digest').read_text().strip()!=expected['digest']:
                raise ValueError('Recorded recovery pair is not intact')
            archived=base/'interrupted/H03-inspected-failed-restoration-14.txt'
            if archived.exists():raise ValueError('Inspection already recorded')
            archived.write_bytes(marker.read_bytes());archived.chmod(0o600)
            report['console_inspection']={'snapshot_hashes_checked':True,'transaction_paths_matched':True,
                'marker_sha256':hashlib.sha256(marker.read_bytes()).hexdigest(),'under_both_host_locks':True,
                'no_runtime_restart_during_inspection':True,'next_action':'fresh acknowledged constrained recovery'}
            marker.unlink()
        report['restoration_recovery']=recover(15,status)
        # Start a normal owned lobby so real begin-release must wait to drain.
        before=status.call('status production');player={'id':str(uuid.uuid4()),'name':'H03 owned lost-SSH lobby'}
        lobby=normal_lobby(player)['id']
        owned=PRIVATE/'H03-interrupted-owned-lobby.json'
        owned.write_bytes(canonical({'player':player,'lobby_id':lobby}));owned.chmod(0o600)
        manifest=json.loads((base/'known-good/release-manifest.json').read_bytes());env=context(manifest,16)
        review=packet(manifest,env,status);run_id=int(env['GITHUB_RUN_ID'])
        ack={'action':'deploy','digest':manifest['api']['digest'],'config_sha256':manifest['config_sha256'],
            'source_sha':manifest['source_sha'],'run_id':run_id,'run_attempt':16,
            'approved_unix':review['review_window_started_unix'],'expires_unix':review['review_window_expires_unix'],
            'drain_seconds':300,'policy':'reset-at-deadline'}
        # This invokes the actual restricted SSH account, then deliberately loses
        # the caller. The durable lease must survive regardless of remote SIGHUP.
        args=['ssh','-T','-F','/dev/null','-i',str(PRIVATE/'keys/deployment'),'-o','BatchMode=yes','-o','IdentitiesOnly=yes',
            '-o','IdentityAgent=none','-o','ClearAllForwardings=yes','-o','StrictHostKeyChecking=yes',
            '-o','UserKnownHostsFile='+str(PRIVATE/'known_hosts'),'-o','ConnectTimeout=5',
            'pa-deploy@170.187.142.115',f'begin-release production {run_id} 16']
        process=subprocess.Popen(args,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
        process.stdin.write(canonical({'manifest':{**manifest,'workflow':{**manifest['workflow'],'run_attempt':16}},'acknowledgment':ack}));process.stdin.close()
        established=False
        for _ in range(20):
            current=status.call('status production')
            if ((current.get('lease') or {}).get('run_attempt')==16 and current['lease']['phase']=='draining'
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
        report['SSH_recovery']=recover(17,status)
        report['final_status']=status.call('status production')
        report['phase']='partial-cases-passed';report['remaining_H03_cases']=['actual client artifact restoration/failure']
    except Exception as error:
        report.update(phase='failed-or-incomplete',error_type=type(error).__name__)
        try:report['diagnostic_status']=status.call('status production')
        except Exception:report['diagnostic_status']='unavailable'
        raise
    finally:output.write_bytes(canonical(report))


if __name__=='__main__':main()
