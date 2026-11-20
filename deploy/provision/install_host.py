"""I03/I04 root installer: preserve legacy recovery, never restart an API.

The authenticated operator channel stages public source and public role keys.
Private keys remain outside the repository and are installed into narrowly
scoped GitHub environment secrets only after actual remote capability checks.
"""
import base64
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid

ROOT = Path(__file__).parents[2]
sys.path.insert(0, str(ROOT / 'deploy/release'))
from ssh_client import PROFILES, Transport
from github import REPOSITORY

STATE = ROOT / 'artifacts/I03-key-install-state.json'
REPORT = ROOT / 'docs/deployment-evidence/I03-host-installation.json'
MODULES = ('client_lease.py', 'host_manifests.py', 'release-control.py', 'release-legacy-guard.py',
           'release-recovery.py', 'release-status.py', 'release_commands.py', 'release_evidence.py',
           'release_lease.py', 'release_verifier.py', 'secure_state.py', 'ssh-control.py',
           'validate-config.py', 'compose.yaml', 'bootstrap_control.py', 'bootstrap_recovery.py')
ROLE_ENVIRONMENTS = {'status': ('client-swa', 'server-status', 'server-linode', 'server-verification', 'server-promotion'),
                     'client-lease': ('client-swa',), 'deployment': ('server-linode',),
                     'evidence': ('server-verification',), 'promotion': ('server-promotion',)}

# This fixed script never handles a private SSH key or exports a configuration.
REMOTE = r'''
import base64,datetime,hashlib,json,os,pwd,stat,subprocess,sys,tempfile
from pathlib import Path
report={'task':'I03/I04','state':'IN_PROGRESS','installed_sha256':{},'production_restart':False}
stage='preflight'
def run(*args):
 r=subprocess.run(args,capture_output=True,timeout=45)
 if r.returncode:raise ValueError('Fixed installer command rejected')
 return r.stdout.decode().strip()
def inventory():
 ids=run('docker','ps','-aq').splitlines()
 return {item['Name']:{'id':item['Id'],'image_id':item['Image'],'state':item['State']['Status']}
         for item in (json.loads(run('docker','inspect',cid))[0] for cid in ids)}
def safe(path):
 for parent in path.parents:
  s=parent.lstat()
  if not stat.S_ISDIR(s.st_mode) or s.st_uid!=0 or s.st_mode&0o022:raise ValueError('Unsafe installer parent')
 if path.exists() or path.is_symlink():
  s=path.lstat()
  if not stat.S_ISREG(s.st_mode) or s.st_uid!=0 or s.st_mode&0o022:raise ValueError('Unsafe installed file')
def write(path,raw,mode):
 safe(path)
 fd,name=tempfile.mkstemp(prefix='.install-',dir=path.parent)
 try:
  with os.fdopen(fd,'wb') as f:os.fchmod(f.fileno(),mode);f.write(raw);f.flush();os.fsync(f.fileno())
  os.replace(name,path)
 finally:
  if os.path.exists(name):os.unlink(name)
try:
 if os.geteuid()!=0:raise ValueError('Root operator required')
 base=Path('/var/lib/pokerattack/production')
 if any((base/name).exists() for name in ('lease','transaction')) or (base/'pending').is_symlink():
  raise ValueError('Inspect occupied production release before installation')
 if (base/'active').resolve(strict=True)!=(base/'known-good').resolve(strict=True):raise ValueError('Serving pair is not accepted')
 current={'digest':(base/'active/digest').read_text().strip(),'config_sha256':hashlib.sha256((base/'active/app.env').read_bytes()).hexdigest()}
 if current!=payload['expected_pair']:raise ValueError('Production pair changed since inspection')
 for name,digest in payload['legacy_sha256'].items():
  p=Path(name);safe(p)
  if hashlib.sha256(p.read_bytes()).hexdigest()!=digest:raise ValueError('Installed legacy tools changed')
 before=inventory();postgres=run('systemctl','show','postgresql@16-main','-p','MainPID','--value')
 report['before']=before;report['active_pair']=current
 stage='source-staging'
 source=Path('/opt/pokerattack/staging')/payload['source_bundle_sha256']
 source.mkdir(mode=0o755,parents=True,exist_ok=True)
 safe(source/'placeholder')
 for name,encoded in payload['files'].items():
  raw=base64.b64decode(encoded,validate=True)
  if '\r' in raw.decode():raise ValueError('Expected LF source')
  p=source/name
  if p.exists() and p.read_bytes()!=raw:raise ValueError('Existing stage differs')
  write(p,raw,0o644)
 sys.path.insert(0,str(source))
 import bootstrap_recovery
 stage='legacy-recovery-preservation'
 report['saved_legacy_tooling']=bootstrap_recovery.preserve()
 bootstrap_recovery.validate_tools(report['saved_legacy_tooling'])
 stage='dedicated-accounts'
 for role,item in payload['roles'].items():
  user=item['user']
  try:pwd.getpwnam(user);raise ValueError('Inspect pre-existing release account')
  except KeyError:pass
  run('useradd','--create-home','--shell','/bin/bash','--comment','PokerAttack restricted '+role,user)
  home=Path('/home')/user;os.chown(home,0,0);home.chmod(0o755)
  ssh=home/'.ssh';ssh.mkdir(mode=0o755);ssh.chmod(0o755)
  forced='/usr/local/sbin/pokerattack-ssh-status' if role=='status' else '/usr/local/sbin/pokerattack-ssh-control '+role
  write(ssh/'authorized_keys',('restrict,command="'+forced+'" '+item['public_key']+'\n').encode(),0o644)
  if run('id','-nG',user)!=user:raise ValueError('Unexpected release account group membership')
 stage='sudoers-validation'
 for name in ('control.sudoers','status.sudoers'):run('visudo','-cf',str(source/name))
 stage='install-reviewed-files'
 backup=Path('/opt/pokerattack/install-backups')/payload['source_bundle_sha256'];backup.mkdir(mode=0o700,parents=True)
 for name,encoded in payload['files'].items():
  if name.endswith('.sudoers'):target=Path('/etc/sudoers.d')/('pokerattack-'+name.split('.')[0]);mode=0o440
  elif name.startswith('pokerattack-'):target=Path('/usr/local/sbin')/name;mode=0o755
  else:target=Path('/opt/pokerattack')/name;mode=0o644
  safe(target)
  if target.exists():write(backup/name,target.read_bytes(),0o600)
  raw=base64.b64decode(encoded,validate=True);write(target,raw,mode)
  report['installed_sha256'][str(target)]=hashlib.sha256(target.read_bytes()).hexdigest()
 run('visudo','-c')
 stage='runtime-permissions'
 admission=base/'admission'
 if admission.exists():raise ValueError('Inspect existing admission directory before initialization')
 admission.mkdir(mode=0o755);admission.chmod(0o755)
 write(admission/'state.json',b'{"active":false}\n',0o644)
 # Read-only verification directory and config snapshots remain root-protected.
 for p in (base/'interrupted',base/'completed',base/'client-completed',Path('/etc/pokerattack/production/verification')):
  p.mkdir(mode=0o700,exist_ok=True)
  if p.is_symlink() or p.stat().st_uid!=0 or p.stat().st_mode&0o077:raise ValueError('Unsafe protected runtime directory')
 run('/usr/bin/python3','-I','/opt/pokerattack/validate-config.py','config','production','/etc/pokerattack/production/configs/'+current['config_sha256']+'.env',current['config_sha256'])
 stage='readback'
 after=inventory()
 if before!=after or postgres!=run('systemctl','show','postgresql@16-main','-p','MainPID','--value'):
  raise ValueError('Service identity changed during installation; inspect')
 report.update(state='INSTALLED / NO APPLICATION RESTART',after=after,shared_services_preserved=True,
               backup_directory=str(backup),source_stage=str(source),admission_directory_mode=oct(admission.stat().st_mode&0o777),
               admission_file_mode=oct((admission/'state.json').stat().st_mode&0o777))
except Exception as error:
 report.update(state='FAILED OR INCOMPLETE; INSPECT BEFORE RETRY',failure_stage=stage,error_type=type(error).__name__)
finally:
 report['recorded_utc']=datetime.datetime.now(datetime.timezone.utc).isoformat()
 print(json.dumps(report))
if report['state']!='INSTALLED / NO APPLICATION RESTART':sys.exit(1)
'''


def main():
    if STATE.exists():
        raise ValueError('Existing key/install state requires inspection; never rerun installation blindly')
    nonce = uuid.uuid4().hex[:12]
    state = {'nonce': nonce, 'keys': {}, 'phase': 'creating-local-keys'}
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    public = {}
    for role, (user, _) in PROFILES.items():
        key = Path.home() / '.ssh' / ('pokerattack-production-' + role + '-' + nonce)
        result = subprocess.run(['ssh-keygen', '-q', '-t', 'ed25519', '-N', '', '-f', str(key)], capture_output=True, timeout=20)
        if result.returncode:
            raise ValueError('Production role key creation failed')
        state['keys'][role] = str(key)
        STATE.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
        public[role] = {'user': user, 'public_key': key.with_suffix('.pub').read_text().strip()}
    files = {name: (ROOT / 'deploy/linode' / name).read_bytes().replace(b'\r\n', b'\n') for name in MODULES}
    files.update({p.name: p.read_bytes().replace(b'\r\n', b'\n') for p in (ROOT / 'deploy/linode').glob('pokerattack-*') if p.is_file()})
    for name in ('control.sudoers', 'status.sudoers'):
        files[name] = (ROOT / 'deploy/linode' / name).read_bytes().replace(b'\r\n', b'\n')
    digest = hashlib.sha256(json.dumps({name: hashlib.sha256(raw).hexdigest() for name, raw in files.items()}, sort_keys=True).encode()).hexdigest()
    inspected = json.loads((ROOT / 'docs/deployment-evidence/H05-live-legacy-prerequisite.json').read_bytes())
    pair = inspected['pairs']['active']
    payload = {'roles': public, 'files': {name: base64.b64encode(raw).decode() for name, raw in files.items()},
               'source_bundle_sha256': digest, 'legacy_sha256': {name: item['sha256'] for name, item in inspected['tools'].items()},
               'expected_pair': {key: pair[key] for key in ('digest', 'config_sha256')}}
    state.update(phase='remote-install-requested', source_bundle_sha256=digest)
    STATE.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    script = 'payload=' + repr(payload) + '\n' + REMOTE
    result = subprocess.run(['ssh', '-T', '-F', os.devnull, '-i', str(Path.home() / '.ssh/id_ed25519_linode_us_southeast'),
                             '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'IdentityAgent=none',
                             '-o', 'StrictHostKeyChecking=yes', '-o', 'ClearAllForwardings=yes',
                             'root@170.187.142.115', 'python3 -'], input=script.encode(), capture_output=True, timeout=240)
    try:
        report = json.loads(result.stdout)
    except Exception:
        raise ValueError('Host outcome unknown; inspect before retrying') from None
    report['source_bundle_sha256'] = digest
    report['installer_sha256'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    REPORT.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    state['phase'] = report['state']
    STATE.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    if result.returncode or report['state'] != 'INSTALLED / NO APPLICATION RESTART':
        raise ValueError('Installation incomplete at ' + report.get('failure_stage', 'unknown') + '; inspect before continuation')
    print('Host tooling installed without API restart; remote capability verification and secret installation remain.')


if __name__ == '__main__':
    main()
