"""J03 lifecycle of one owned sleeping container; never run an application API."""
import datetime
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid

ROOT = Path(__file__).parents[2]
STATE = ROOT / 'artifacts/J03-live-probe-state.json'

REMOTE = r'''
import datetime,json,os,re,subprocess,sys
from pathlib import Path
if not re.fullmatch('[a-f0-9]{12}',nonce):raise ValueError('Invalid owned probe')
name='linode-docker-dashboard-probe-'+nonce
record=Path('/var/lib/linode-docker-metrics')/('dashboard-probe-'+nonce+'.json')
def run(*args):
 r=subprocess.run(args,capture_output=True,timeout=45)
 if r.returncode:raise ValueError('Owned probe command failed; inspect before retry')
 return r.stdout.decode().strip()
def inspect(cid):return json.loads(run('docker','inspect',cid))[0]
def protected():
 return {n:{'id':inspect(n)['Id'],'state':inspect(n)['State']['Status']} for n in
         ('pokerattack-production-api-1','pokerattack-pilot-api-1','transitjazz-app-server-1','transitjazz-app-proxy-1')}
before=protected()
if action=='create':
 if record.exists() or record.is_symlink() or run('docker','ps','-aq','--filter','name=^/'+name+'$'):
  raise ValueError('Inspect existing owned probe before creation')
 state={'nonce':nonce,'name':name,'image_id':inspect('transitjazz-app-proxy-1')['Image'],'incarnations':[]}
else:
 if record.is_symlink() or record.stat().st_uid!=0 or record.stat().st_mode&0o077:raise ValueError('Unsafe probe state')
 state=json.loads(record.read_bytes())
 live=inspect(state['container_id'])
 if live['Name']!='/'+name or live['Config']['Labels'].get('pokerattack.dashboard-probe')!=nonce:
  raise ValueError('Owned probe identity changed')
 if action=='stop':run('docker','stop','--time','3',state['container_id'])
 elif action=='recreate':
  if live['State']['Status']!='exited':raise ValueError('Observe stopped probe before recreation')
  run('docker','rm',state['container_id'])
 elif action=='remove':
  run('docker','rm','-f',state['container_id']);state['removed']=True
 elif action!='inspect':raise ValueError('Unsupported fixed probe action')
if action in ('create','recreate'):
 cid=run('docker','run','-d','--pull','never','--name',name,'--read-only','--network','none',
         '--cap-drop','ALL','--security-opt','no-new-privileges','--user','65534:65534','--memory','16m','--cpus','0.05',
         '--tmpfs','/data:rw,noexec,nosuid,size=1m','--tmpfs','/config:rw,noexec,nosuid,size=1m',
         '--label','pokerattack.dashboard-probe='+nonce,'--entrypoint','/bin/sh',state['image_id'],'-c','exec sleep 1200')
 state['container_id']=cid;state['incarnations'].append(cid)
record.write_text(json.dumps(state)+'\n');record.chmod(0o600)
run('systemctl','start','linode-docker-snapshot.service')
after=protected()
if before!=after:raise ValueError('Shared container identity/state changed during probe')
result={'task':'J03','action':action,'state':state,'shared_containers_unchanged':True,
        'sample_triggered':True,'recorded_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'no_application_API_process':True,'no_host_ports_or_network_or_persistent_volumes':True}
if action!='remove':
 live=inspect(state['container_id']);result['Docker']={'container_id':live['Id'],'status':live['State']['Status'],
     'health':live['State'].get('Health',{}).get('Status','none'),'image_id':live['Image'],
     'memory_bytes':live['HostConfig']['Memory'],'nano_cpus':live['HostConfig']['NanoCpus']}
print(json.dumps(result))
'''


def main():
    action = sys.argv[1]
    if action not in ('create', 'stop', 'recreate', 'remove', 'inspect'):
        raise ValueError('Fixed owned probe action required')
    if action == 'create':
        if STATE.exists(): raise ValueError('Inspect previously recorded live probe before creating another')
        state = {'nonce': uuid.uuid4().hex[:12], 'phase': 'creation-requested'}
        STATE.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    else:
        state = json.loads(STATE.read_bytes())
    script = 'nonce=' + repr(state['nonce']) + '\naction=' + repr(action) + '\n' + REMOTE
    result = subprocess.run(['ssh', '-T', '-F', os.devnull, '-i', str(Path.home() / '.ssh/id_ed25519_linode_us_southeast'),
                             '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'StrictHostKeyChecking=yes',
                             'root@170.187.142.115', 'python3 -'], input=script.encode(), capture_output=True, timeout=100)
    if result.returncode:
        raise ValueError('Owned probe outcome uncertain; inspect its exact recorded nonce/name/ID before another action')
    report = json.loads(result.stdout)
    state.update(phase=action, remote=report['state'])
    STATE.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    name = 'J03-probe-' + action + '-' + str(len(report['state']['incarnations'])) + '.json'
    path = ROOT / 'docs/deployment-evidence' / name
    if path.exists(): raise ValueError('Retain prior probe evidence; choose an explicit new inspected report')
    path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__': main()
