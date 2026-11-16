"""Read-only production recovery-tool capture; never copy config/credentials."""
import base64
import datetime
import hashlib
import json
from pathlib import Path
import subprocess

ROOT=Path(__file__).parents[2]
FILES=('/opt/pokerattack/compose.yaml','/opt/pokerattack/validate-config.py',
       '/usr/local/sbin/pokerattack-deploy','/usr/local/sbin/pokerattack-ssh-deploy')


def main():
    destination=ROOT/'artifacts/H05-legacy-capture'
    destination.mkdir(exist_ok=True)
    script='''import base64,datetime,hashlib,json,os,subprocess,urllib.request
from pathlib import Path
files=FILES
result={'inspected_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'read_only':True,'tools':{},'pairs':{}}
for name in files:
 p=Path(name);st=p.stat();raw=p.read_bytes()
 if p.is_symlink() or st.st_uid!=0 or st.st_mode&0o022 or len(raw)>262144:raise ValueError('Unsafe tool input')
 result['tools'][name]={'sha256':hashlib.sha256(raw).hexdigest(),'content_base64':base64.b64encode(raw).decode(),'mode':oct(st.st_mode&0o777)}
base=Path('/var/lib/pokerattack/production')
for name in ('active','known-good','pending','previous-good'):
 p=base/name;result['pairs'][name]=None
 if p.is_symlink():
  result['pairs'][name]={'digest':(p/'digest').read_text().strip(),'config_sha256':(p/'config-id').read_text().strip(),'snapshot_config_sha256':hashlib.sha256((p/'app.env').read_bytes()).hexdigest()}
result['state']={name:(base/name).exists() for name in ('lease','transaction')}
try:
 with urllib.request.urlopen('http://127.0.0.1:8081/health/ready',timeout=5) as response:result['ready']=response.status==200
except Exception:result['ready']=False
result['containers']=[]
ids=subprocess.check_output(['docker','ps','-aq'],timeout=10).decode().splitlines()
for cid in ids:
 raw=json.loads(subprocess.check_output(['docker','inspect',cid],timeout=10))[0]
 result['containers'].append({'container_id':raw['Id'],'name':raw['Name'],'state':raw['State']['Status'],'image_id':raw['Image']})
print(json.dumps(result))
'''.replace('files=FILES','files='+repr(FILES))
    command=['ssh','-T','-F','NUL','-i',str(Path.home()/'.ssh/id_ed25519_linode_us_southeast'),
             '-o','BatchMode=yes','-o','IdentitiesOnly=yes','-o','StrictHostKeyChecking=yes',
             'root@170.187.142.115','python3 -']
    outcome=subprocess.run(command,input=script.encode(),capture_output=True,timeout=90)
    if outcome.returncode:raise ValueError('Read-only legacy capture failed; no credentials or provider output printed')
    report=json.loads(outcome.stdout)
    if set(report['tools'])!=set(FILES):raise ValueError('Unexpected tool set')
    for name,metadata in report['tools'].items():
        raw=base64.b64decode(metadata.pop('content_base64'),validate=True)
        if hashlib.sha256(raw).hexdigest()!=metadata['sha256']:raise ValueError('Captured tool hash differs')
        path=destination/Path(name).name
        if path.exists() and path.read_bytes()!=raw:raise ValueError('Existing capture differs; retain a new dated attempt')
        path.write_bytes(raw)
    (destination/'capture.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    (ROOT/'docs/deployment-evidence/H05-live-legacy-prerequisite.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'ready':report['ready'],'state':report['state'],'active':report['pairs']['active'],'captured_tool_count':len(report['tools'])}))


if __name__=='__main__':main()
