"""I04 verify accepted config and UID1654 read-only mount; freeze old recovery."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

from install_host import ROOT, STATE
from github import REPOSITORY

sys.path.insert(0, str(ROOT / 'deploy/release'))
from bootstrap_artifacts import publication


def main():
    state = json.loads(STATE.read_bytes())
    if state['phase'] != 'remote-verified/environment-secrets-installed':
        raise ValueError('Finish host capability verification first')
    archive = json.loads((ROOT / 'docs/deployment-evidence/I02-private-archive-verification.json').read_bytes())
    if archive['state'] != 'PASS' or not archive['legacy-recovery']['readback_verified']:
        raise ValueError('Freeze a durable, byte-verified compatible client first')
    installed = json.loads((ROOT / 'docs/deployment-evidence/I03-host-installation.json').read_bytes())
    legacy = json.loads((ROOT / 'artifacts/H05-inputs/legacy-recovery.json').read_bytes())
    payload = {'pair': installed['active_pair'], 'tooling': installed['saved_legacy_tooling'],
               'client': publication(legacy)}
    script = 'payload=' + repr(payload) + '\n' + r'''
import datetime,hashlib,json,os,subprocess,sys
from pathlib import Path
sys.path.insert(0,'/opt/pokerattack')
import bootstrap_recovery,secure_state as storage
from release_verifier import status_module
report={'task':'I04','state':'IN_PROGRESS','production_restart':False}
stage='frozen-runtime'
try:
 live=status_module.status('production')
 if not live['readiness']['ready'] or live['lease'] or live['transaction'] or live['pairs']['pending']:
  raise ValueError('Inspect occupied/unready production before runtime provisioning')
 if any(live['pairs']['active'][key]!=value for key,value in payload['pair'].items()):raise ValueError('Accepted pair changed')
 image=live['runtime']['image_id']
 stage='UID1654-read-only-bind'
 name='pokerattack-admission-permission-probe'
 found=subprocess.run(['docker','ps','-aq','--filter','name=^/'+name+'$'],capture_output=True,timeout=15)
 if found.returncode or found.stdout.strip():raise ValueError('Inspect existing owned permission probe')
 command=['docker','run','--rm','--pull','never','--name',name,'--network','none','--read-only',
          '--cap-drop','ALL','--security-opt','no-new-privileges','--user','1654:1654','--memory','32m','--cpus','0.10',
          '--mount','type=bind,src=/var/lib/pokerattack/production/admission,dst=/release-state,readonly',
          '--entrypoint','/bin/sh',image,'-c','test -r /release-state/state.json && test ! -w /release-state/state.json && test ! -w /release-state']
 result=subprocess.run(command,capture_output=True,timeout=45)
 if result.returncode:raise ValueError('Non-root mount permissions failed')
 report['UID1654_readable_and_not_writable']=True
 stage='protected-recovery-record'
 record={'format':'pokerattack-bootstrap-recovery-v1',**payload,
         'source_sha':live['runtime']['source_revision'],'image_id':image}
 if bootstrap_recovery.CONFIG.exists() or bootstrap_recovery.CONFIG.is_symlink():raise ValueError('Inspect existing bootstrap recovery record')
 with storage.control_lock(),storage.deployment_lock():
  current=status_module.status('production')
  if current['runtime']!=live['runtime'] or current['lease'] or current['transaction']:raise ValueError('Runtime changed before recovery freeze')
  storage.write(bootstrap_recovery.CONFIG,record)
  bootstrap_recovery.snapshot(current)
 final=status_module.status('production')
 if final['runtime']!=live['runtime'] or final['pairs']!=live['pairs'] or final['lease'] or final['transaction']:raise ValueError('Runtime changed during provisioning')
 report.update(state='CONFIG / PERMISSIONS / FROZEN RECOVERY VERIFIED',active_pair=payload['pair'],
               bootstrap_recovery_sha256=hashlib.sha256(bootstrap_recovery.CONFIG.read_bytes()).hexdigest(),
               protected_record=str(bootstrap_recovery.CONFIG),source_sha=record['source_sha'],
               image_id=image,final_status=final,acknowledgment_created_or_refreshed=False,
               no_API_authority_in_permission_probe=True)
except Exception as error:
 report.update(state='FAILED OR INCOMPLETE; INSPECT BEFORE RETRY',failure_stage=stage,error_type=type(error).__name__)
finally:
 report['recorded_utc']=datetime.datetime.now(datetime.timezone.utc).isoformat()
 print(json.dumps(report))
if report['state']!='CONFIG / PERMISSIONS / FROZEN RECOVERY VERIFIED':sys.exit(1)
'''
    result = subprocess.run(['ssh', '-T', '-F', os.devnull, '-i', str(Path.home() / '.ssh/id_ed25519_linode_us_southeast'),
                             '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'IdentityAgent=none',
                             '-o', 'StrictHostKeyChecking=yes', '-o', 'ClearAllForwardings=yes',
                             'root@170.187.142.115', 'python3 -'], input=script.encode(), capture_output=True, timeout=100)
    try:
        report = json.loads(result.stdout)
    except Exception:
        raise ValueError('Runtime provisioning outcome unknown; inspect before retrying') from None
    report['durable_legacy_archive'] = archive['legacy-recovery']
    report['installer_sha256'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    (ROOT / 'docs/deployment-evidence/I04-runtime-verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    if result.returncode:
        raise ValueError('Runtime provisioning incomplete at ' + report.get('failure_stage', 'unknown'))
    value = payload['pair']['config_sha256']
    if subprocess.run(['gh', 'variable', 'set', 'APPROVED_CONFIG_SHA256', '--repo', REPOSITORY,
                       '--body', value], capture_output=True, timeout=60).returncode:
        raise ValueError('Approved configuration variable installation rejected')
    print('Accepted config, UID1654 read-only bind and protected old recovery verified; API unchanged, no reset acknowledgment issued.')


if __name__ == '__main__':
    main()
