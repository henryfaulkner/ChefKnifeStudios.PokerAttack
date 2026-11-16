"""Install only the bounded Docker snapshot and additive existing Alloy path.

Authenticated Alloy configuration never leaves the host. Failures undo only
this addition; application containers, PostgreSQL and shared Docker stay running.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).parents[2]

REMOTE = r'''
import base64,datetime,hashlib,json,os,stat,subprocess,sys,tempfile,time
from pathlib import Path
report={'task':'I05','state':'IN_PROGRESS','production_API_restart':False,'installed_sha256':{}}
stage='preflight';applied=False;timer_started=False;reload_method=None
def command(*args,timeout=45):
 r=subprocess.run(args,capture_output=True,timeout=timeout)
 if r.returncode:raise ValueError('Fixed monitoring operation rejected')
 return r.stdout.decode().strip()
def inventory():
 return {item['Name']:{'id':item['Id'],'image_id':item['Image'],'state':item['State']['Status']}
         for item in (json.loads(command('docker','inspect',cid))[0] for cid in command('docker','ps','-aq').splitlines())}
def check_parent(path):
 for p in path.parents:
  s=p.lstat()
  if p.is_symlink() or not stat.S_ISDIR(s.st_mode) or s.st_uid!=0 or s.st_mode&0o022:raise ValueError('Unsafe monitoring path')
 if path.exists() or path.is_symlink():
  s=path.lstat()
  if path.is_symlink() or not stat.S_ISREG(s.st_mode) or s.st_uid!=0 or s.st_mode&0o022:raise ValueError('Unsafe monitoring file')
def write(path,raw,mode):
 check_parent(path)
 fd,temp=tempfile.mkstemp(prefix='.docker-install-',dir=path.parent)
 try:
  with os.fdopen(fd,'wb') as out:os.fchmod(out.fileno(),mode);out.write(raw);out.flush();os.fsync(out.fileno())
  os.replace(temp,path)
 finally:
  if os.path.exists(temp):os.unlink(temp)
try:
 if os.geteuid()!=0:raise ValueError('Root operator required')
 before=inventory();postgres=command('systemctl','show','postgresql@16-main','-p','MainPID','--value')
 report['before_containers']=before
 service=Path('/etc/systemd/system/linode-docker-snapshot.service')
 timer=Path('/etc/systemd/system/linode-docker-snapshot.timer')
 if service.exists() or timer.exists():raise ValueError('Inspect existing collector before installation')
 config=Path('/etc/alloy/config.alloy');check_parent(config);raw=config.read_bytes()
 report['alloy_before_sha256']=hashlib.sha256(raw).hexdigest()
 report['alloy_version']=command('alloy','--version')
 directory=Path('/opt/linode-docker-metrics');directory.mkdir(mode=0o755);directory.chmod(0o755)
 output=Path('/var/lib/linode-docker-metrics');output.mkdir(mode=0o755);output.chmod(0o755)
 for name in ('docker_snapshot.py','alloy_textfile.py'):
  p=directory/name;write(p,base64.b64decode(payload[name],validate=True),0o644)
  report['installed_sha256'][str(p)]=hashlib.sha256(p.read_bytes()).hexdigest()
 sys.path.insert(0,str(directory));import alloy_textfile
 stage='exact-additive-config-validation'
 changed=alloy_textfile.prepare(raw.decode())
 if alloy_textfile.rollback(changed)!=raw.decode():raise ValueError('Additive inverse differs from original')
 backups=Path('/etc/alloy/pokerattack-docker-backups');backups.mkdir(mode=0o700);backups.chmod(0o700)
 backup=backups/('before-'+report['alloy_before_sha256']+'.alloy');write(backup,raw,0o600)
 candidate=backups/'candidate.alloy';write(candidate,changed.encode(),0o600)
 command('alloy','validate',str(candidate))
 report['protected_backup']=str(backup)
 stage='systemd-unit-validation'
 for p in (service,timer):
  write(p,base64.b64decode(payload[p.name],validate=True),0o644)
  report['installed_sha256'][str(p)]=hashlib.sha256(p.read_bytes()).hexdigest()
 command('systemd-analyze','verify',str(service),str(timer))
 command('systemctl','daemon-reload')
 stage='bounded-native-collection'
 started=time.monotonic();command('systemctl','start',service.name,timeout=40)
 report['first_collection_wall_seconds']=round(time.monotonic()-started,3)
 metrics=output/'docker.prom'
 if not metrics.is_file() or time.time()-metrics.stat().st_mtime>45:raise ValueError('No fresh installed collection')
 report['metrics_file_sha256']=hashlib.sha256(metrics.read_bytes()).hexdigest()
 report['metrics_lines']=len(metrics.read_text().splitlines())
 report['service_limits_and_result']=command('systemctl','show',service.name,'-p','Result','-p','CPUQuotaPerSecUSec','-p','MemoryMax','-p','MemoryPeak','-p','CPUUsageNSec')
 stage='atomic-Alloy-addition'
 if config.read_bytes()!=raw:raise ValueError('Alloy changed during staging; inspect instead of overwriting')
 write(config,changed.encode(),stat.S_IMODE(config.stat().st_mode));applied=True
 report['alloy_after_sha256']=hashlib.sha256(config.read_bytes()).hexdigest()
 reload_method='reload' if command('systemctl','show','alloy','-p','CanReload','--value')=='yes' else 'restart'
 report['alloy_apply_method']=reload_method
 command('systemctl',reload_method,'alloy',timeout=60)
 if command('systemctl','is-active','alloy')!='active':raise ValueError('Alloy did not remain active')
 command('systemctl','enable','--now',timer.name);timer_started=True
 if command('systemctl','is-active',timer.name)!='active':raise ValueError('Collector timer not active')
 stage='preservation-readback'
 after=inventory()
 if before!=after or postgres!=command('systemctl','show','postgresql@16-main','-p','MainPID','--value'):
  raise ValueError('Shared application service changed during monitoring installation')
 report.update(state='INSTALLED / SCHEDULE ACTIVE / DATASOURCE INGESTION PENDING',after_containers=after,
               shared_services_preserved=True,alloy_active=True,timer_active=True,
               rollback='Stop only new timer; prepare alloy_textfile.rollback(current), validate, atomically replace and reload/restart Alloy. Protected backup is root-only; never overwrite subsequent unrelated changes.')
except Exception as error:
 report.update(state='FAILED OR INCOMPLETE',failure_stage=stage,error_type=type(error).__name__)
 if applied:
  try:
   command('systemctl','disable','--now','linode-docker-snapshot.timer')
   current=config.read_text();reverted=alloy_textfile.rollback(current)
   rollback=backups/'rollback.alloy';write(rollback,reverted.encode(),0o600);command('alloy','validate',str(rollback))
   write(config,reverted.encode(),stat.S_IMODE(config.stat().st_mode));command('systemctl',reload_method or 'restart','alloy',timeout=60)
   report['addition_reverted_and_Alloy_active']=command('systemctl','is-active','alloy')=='active'
  except Exception:report['rollback_requires_inspection']=True
finally:
 report['recorded_utc']=datetime.datetime.now(datetime.timezone.utc).isoformat()
 print(json.dumps(report))
if report['state']!='INSTALLED / SCHEDULE ACTIVE / DATASOURCE INGESTION PENDING':sys.exit(1)
'''


def main():
    files = ('docker_snapshot.py', 'alloy_textfile.py', 'linode-docker-snapshot.service', 'linode-docker-snapshot.timer')
    payload = {name: base64.b64encode((ROOT / 'deploy/monitoring' / name).read_bytes().replace(b'\r\n', b'\n')).decode() for name in files}
    result = subprocess.run(['ssh', '-T', '-F', os.devnull, '-i', str(Path.home() / '.ssh/id_ed25519_linode_us_southeast'),
                             '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'IdentityAgent=none',
                             '-o', 'StrictHostKeyChecking=yes', '-o', 'ClearAllForwardings=yes',
                             'root@170.187.142.115', 'python3 -'], input=('payload=' + repr(payload) + '\n' + REMOTE).encode(),
                            capture_output=True, timeout=210)
    try:
        report = json.loads(result.stdout)
    except Exception:
        raise ValueError('Monitoring installation outcome unknown; inspect before repeating') from None
    report['installer_sha256'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    (ROOT / 'docs/deployment-evidence/I05-monitoring-installation.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    if result.returncode:
        raise ValueError('Monitoring install incomplete at ' + report.get('failure_stage', 'unknown') + '; inspect its report')
    print('Bounded collector installed and scheduled; additive Alloy configuration applied; live datasource verification remains.')


if __name__ == '__main__':
    main()
