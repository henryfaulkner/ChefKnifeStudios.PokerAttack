"""Root-bound SWA publication within an existing acknowledged bootstrap lease."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

from bootstrap_artifacts import check as check_bootstrap,publication
from client_artifacts import check_archive,identity_summary
from manifests import load_document
from review import canonical

ROOT=Path(__file__).resolve().parents[2]
OPERATOR_ACTIONS={'client-authorize','client-published','client-status','client-acceptance','begin-release','drain-release'}


class UnknownPublication(RuntimeError):pass


def operator(action,run,attempt,packet=None):
    if action not in OPERATOR_ACTIONS or type(run) is not int or not 1<=run<=9007199254740991 or type(attempt) is not int or not 1<=attempt<=99999:
        raise ValueError('Invalid fixed root bootstrap operation')
    raw=None if packet is None else canonical(packet)
    if raw is not None and len(raw)>65536:raise ValueError('Oversized root bootstrap packet')
    command=['ssh','-T','-F',os.devnull,'-i',str(Path.home()/'.ssh/id_ed25519_linode_us_southeast'),
             '-o','BatchMode=yes','-o','IdentitiesOnly=yes','-o','IdentityAgent=none','-o','StrictHostKeyChecking=yes',
             '-o','ClearAllForwardings=yes','-o','ConnectTimeout=15',
             '-o','ServerAliveInterval=15','-o','ServerAliveCountMax=2','root@170.187.142.115',
             f'python3 -I /opt/pokerattack/release-control.py operator-bootstrap {action} production {run} {attempt}']
    try:result=subprocess.run(command,input=raw,capture_output=True,timeout=390 if action=='drain-release' else 90)
    except subprocess.TimeoutExpired:raise UnknownPublication('Inspect root publication state; never repeat the upload automatically') from None
    if result.returncode==255:raise UnknownPublication('Root response uncertain; reconcile exact served bytes')
    if result.returncode:raise ValueError('Root bootstrap operation rejected')
    try:return load_document(result.stdout)
    except Exception:raise UnknownPublication('Root response invalid; reconcile without another upload') from None


def upload(directory):
    az=shutil.which('az.cmd' if os.name=='nt' else 'az')
    npx=shutil.which('npx.cmd' if os.name=='nt' else 'npx')
    if not az or not npx:raise ValueError('Authenticated Azure CLI and Node tooling required')
    service=subprocess.run([az,'staticwebapp','environment','list','--name','poker-attack-webapp','--resource-group','poker-attack-rg',
        '--query',"[?name=='default'].status | [0]",'-o','json','--only-show-errors'],capture_output=True,timeout=30)
    if service.returncode or json.loads(service.stdout)!='Ready':raise UnknownPublication('SWA service is unresolved; do not upload')
    response=subprocess.run([az,'staticwebapp','secrets','list','--name','poker-attack-webapp','--resource-group','poker-attack-rg',
        '--query','properties.apiKey','-o','tsv','--only-show-errors'],capture_output=True,timeout=30)
    if response.returncode:raise ValueError('SWA publication authentication unavailable')
    token=response.stdout.decode().strip()
    if not re.fullmatch(r'[A-Za-z0-9+/_=-]{16,16384}',token):raise ValueError('Unexpected SWA credential format')
    child={key:value for key,value in os.environ.items() if not (key.endswith('_PRIVATE_KEY') or key in ('GH_TOKEN','GITHUB_TOKEN','ACTIONS_ID_TOKEN_REQUEST_TOKEN'))}
    child['SWA_CLI_DEPLOYMENT_TOKEN']=token
    # Only this child receives the token. Provider output is never printed or
    # persisted: it may contain transient upload URLs or authentication material.
    try:
        result=subprocess.run([npx,'--yes','--package=node@22.23.5','--package=@azure/static-web-apps-cli@2.1.0',
            'swa','deploy',str(directory),'--swa-config-location',str(directory),'--env','production','--no-use-keychain'],
            capture_output=True,env=child,cwd=ROOT,timeout=900)
    except subprocess.TimeoutExpired:raise UnknownPublication('SWA upload response unknown; use explicit reconciliation') from None
    if result.returncode:raise UnknownPublication('SWA upload failed or is unresolved; retain bootstrap maintenance and inspect served bytes')


def publish(mode,archive,manifest,workspace,run,attempt,reconcile=False,root_operator=operator,uploader=upload):
    workspace=Path(workspace)
    if reconcile:
        if workspace.is_symlink():raise ValueError('Unsafe publication workspace')
        record=load_document((workspace/'bootstrap-publication.json').read_bytes())
        if record['run_id']!=run or record['run_attempt']!=attempt:raise ValueError('Publication receipt belongs to another bootstrap')
        # This path is only read/probe reconciliation. Never authorize another
        # upload when the original child or SSH reply was uncertain.
        return root_operator('client-published',run,attempt,{'sha256':record['sha256']})
    if workspace.exists() or workspace.is_symlink():raise ValueError('Fresh bootstrap publication workspace required')
    workspace.mkdir(parents=True,mode=0o700)
    if mode=='candidate':
        check_archive(archive,manifest,workspace/'client')
        packet={'mode':mode,'archive_sha256':manifest['archive_sha256'],'client':identity_summary(manifest),
                'probes':{'/'+name:manifest['files'][name] for name in ('index.html','release.json','appsettings.json')}}
    else:
        if mode not in ('maintenance','legacy-recovery') or manifest.get('mode')!=mode:raise ValueError('Wrong static/legacy publication')
        check_bootstrap(archive,manifest,workspace/'client');packet=publication(manifest)
        if mode=='maintenance':
            expected={path.relative_to(ROOT/'deploy/maintenance').as_posix():hashlib.sha256(path.read_bytes()).hexdigest()
                for path in (ROOT/'deploy/maintenance').rglob('*') if path.is_file()}
            if expected!=manifest['files']:raise ValueError('Use the reviewed repository maintenance page')
    digest=hashlib.sha256(canonical(packet)).hexdigest()
    record={'run_id':run,'run_attempt':attempt,'sha256':digest,'packet':packet}
    receipt=workspace/'bootstrap-publication.json';receipt.write_bytes(canonical(record));receipt.chmod(0o600)
    authorization=root_operator('client-authorize',run,attempt,packet)
    if authorization.get('sha256')!=digest or authorization.get('upload_outcome')!='not-started':
        raise UnknownPublication('Existing or uncertain publication; reconcile exact bytes before any upload')
    uploader(workspace/'client')
    return root_operator('client-published',run,attempt,{'sha256':digest})


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode',choices=('maintenance','candidate','legacy-recovery'))
    for name in ('archive','manifest','workspace'):parser.add_argument('--'+name,type=Path,required=True)
    parser.add_argument('--run',type=int,required=True);parser.add_argument('--attempt',type=int,required=True)
    parser.add_argument('--reconcile',action='store_true')
    args=parser.parse_args();manifest=load_document(args.manifest.read_bytes())
    result=publish(args.mode,args.archive,manifest,args.workspace,args.run,args.attempt,args.reconcile)
    print(json.dumps(result))


if __name__=='__main__':
    try:main()
    except (ValueError,RuntimeError,subprocess.TimeoutExpired):sys.exit('Bootstrap publication incomplete; retain the lease and reconcile before further mutation.')
