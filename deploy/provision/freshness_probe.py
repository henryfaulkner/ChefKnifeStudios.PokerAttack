"""J04 pause only the new collector; leave Alloy/host/application collection up."""
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid

ROOT = Path(__file__).parents[2]
STATE = ROOT / 'artifacts/J04-freshness-state.json'

REMOTE = r'''
import datetime,json,re,subprocess,time
from pathlib import Path
if not re.fullmatch('[a-f0-9]{12}',nonce):raise ValueError('Invalid owned freshness probe')
unit='linode-docker-snapshot-qa-resume-'+nonce
def run(*args):
 r=subprocess.run(args,capture_output=True,timeout=45)
 if r.returncode:raise ValueError('Freshness probe control failed; inspect before repeating')
 return r.stdout.decode().strip()
def containers():
 result={}
 for name in ('pokerattack-production-api-1','pokerattack-pilot-api-1','transitjazz-app-server-1','transitjazz-app-proxy-1'):
  d=json.loads(run('docker','inspect',name))[0];result[name]={'container_id':d['Id'],'state':d['State']['Status']}
 return result
if action=='pause':
 if run('systemctl','is-active','linode-docker-snapshot.timer')!='active':raise ValueError('Inspect inactive collector before stale test')
 # A transient failsafe restores only this timer after six minutes if the
 # local operator session is interrupted. No persistent service/program added.
 run('systemd-run','--unit='+unit,'--on-active=6m','--property=Type=oneshot','/usr/bin/systemctl','start','linode-docker-snapshot.timer')
 run('systemctl','stop','linode-docker-snapshot.timer')
elif action=='restore':
 run('systemctl','start','linode-docker-snapshot.timer')
 run('systemctl','start','linode-docker-snapshot.service')
 # Only the exact generated transient failsafe is stopped; it may have fired.
 subprocess.run(['systemctl','stop',unit+'.timer'],capture_output=True,timeout=15)
else:raise ValueError('Fixed pause/restore action required')
metrics=Path('/var/lib/linode-docker-metrics/docker.prom').read_text()
stamp=re.search(r'^linode_docker_snapshot_timestamp_seconds\{host="expense-tracker-vm"\} ([0-9.]+)$',metrics,re.M)
if not stamp:raise ValueError('Expected installed snapshot missing')
print(json.dumps({'task':'J04','action':action,'recorded_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'snapshot_unix':float(stamp[1]),'alloy_active':run('systemctl','is-active','alloy'),
 'alloy_main_pid':run('systemctl','show','alloy','-p','MainPID','--value'),
 'postgres_main_pid':run('systemctl','show','postgresql@16-main','-p','MainPID','--value'),
 'shared_containers':containers(),'failsafe_unit':unit,'failsafe_restoration_seconds':360,
 'timer_status':subprocess.run(['systemctl','is-active','linode-docker-snapshot.timer'],capture_output=True,text=True).stdout.strip()}))
'''


def main():
    action = sys.argv[1]
    if action == 'pause':
        if STATE.exists(): raise ValueError('Existing freshness state requires inspection')
        state = {'nonce': uuid.uuid4().hex[:12], 'phase': 'pause-requested'}
        STATE.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    elif action == 'restore':
        state = json.loads(STATE.read_bytes())
    else:
        raise ValueError('Fixed pause/restore action required')
    script = 'nonce=' + repr(state['nonce']) + '\naction=' + repr(action) + '\n' + REMOTE
    result = subprocess.run(['ssh', '-T', '-F', os.devnull, '-i', str(Path.home() / '.ssh/id_ed25519_linode_us_southeast'),
                             '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'StrictHostKeyChecking=yes',
                             'root@170.187.142.115', 'python3 -'], input=script.encode(), capture_output=True, timeout=110)
    if result.returncode: raise ValueError('Freshness probe outcome unknown; inspect/restore only the new collector timer')
    report = json.loads(result.stdout)
    state.update(phase=action, **{action: report})
    STATE.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    (ROOT / 'docs/deployment-evidence' / ('J04-' + action + '.json')).write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__': main()
