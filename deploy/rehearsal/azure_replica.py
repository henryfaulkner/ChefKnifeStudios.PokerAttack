"""Own one temporary H02 Linux replica; never alter production resources.

The implementation instruction authorizes disposable rehearsal infrastructure.
State contains only resource identities and an SSH key path, never credentials.
Delete only the recorded dedicated group after checking its subscription/nonce
and ensuring no unrecorded resources were added. No production credentials used.
"""
import datetime
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid

ROOT=Path(__file__).parents[2]
STATE=ROOT/'artifacts/H02-replica/azure-state.json'
SUBSCRIPTION='d2f70759-7eeb-4ca5-9046-f7c8d562ef8f'
TENANT='9aafd1d1-65c4-4d23-a613-f66e425a581f'


def az(*args,timeout=120):
    result=subprocess.run([shutil.which('az.cmd' if os.name=='nt' else 'az'),*args,
        '--subscription',SUBSCRIPTION,'--only-show-errors','-o','json'],capture_output=True,timeout=timeout)
    if result.returncode:
        # No credentials appear in these management commands. Keep full provider
        # diagnostics locally so an authorization/allocation failure is honest.
        STATE.parent.mkdir(parents=True,exist_ok=True)
        (STATE.parent/('azure-failure-'+uuid.uuid4().hex+'.log')).write_bytes(result.stdout+result.stderr)
        raise RuntimeError('Azure replica operation failed; inspect its local provider diagnostic')
    return json.loads(result.stdout) if result.stdout.strip() else None


def save(state):
    STATE.parent.mkdir(parents=True,exist_ok=True)
    STATE.write_text(json.dumps(state,indent=2)+'\n',encoding='utf-8')


def check_account():
    account=az('account','show')
    if account['id']!=SUBSCRIPTION or account['tenantId']!=TENANT:raise ValueError('Wrong rehearsal subscription/tenant')


def create():
    check_account()
    if STATE.exists():raise ValueError('Existing rehearsal state requires inspection; do not create another replica')
    nonce=uuid.uuid4().hex[:12];group='rg-pa-release-rehearsal-'+nonce
    now=datetime.datetime.now(datetime.timezone.utc)
    state={'subscription':SUBSCRIPTION,'group':group,'nonce':nonce,'vm':'pa-rehearsal','account':'parehearsal'+nonce,
        'created_utc':now.isoformat(),'expires_utc':(now+datetime.timedelta(hours=24)).isoformat(),
        'scope':'H02-H05 disposable Linux replica only','phase':'creating','owned_resource_ids':[],'role_assignment_ids':[]}
    save(state)
    az('group','create','--name',group,'--location','eastus','--tags',
        'purpose=pokerattack-deployment-rehearsal','rehearsal_nonce='+nonce,'expires_utc='+state['expires_utc'])
    try:
        storage=az('storage','account','create','--name',state['account'],'--resource-group',group,
            '--location','eastus','--sku','Standard_LRS','--kind','StorageV2','--min-tls-version','TLS1_2',
            '--allow-blob-public-access','false','--allow-shared-key-access','false','--https-only','true')
        state['storage_id']=storage['id'];save(state)
        # Obtain only the signed-in user's object identity, never a token/key.
        result=subprocess.run([shutil.which('az.cmd' if os.name=='nt' else 'az'),'ad','signed-in-user','show',
            '--query','id','-o','tsv','--only-show-errors'],capture_output=True,timeout=60)
        identity=result.stdout.decode().strip()
        if result.returncode or not re.fullmatch(r'[a-f0-9-]{36}',identity):raise ValueError('Signed-in provisioning identity unavailable')
        assignment=az('role','assignment','create','--assignee-object-id',identity,'--assignee-principal-type','User',
            '--role','Storage Blob Data Contributor','--scope',storage['id'])
        state['role_assignment_ids'].append(assignment['id']);save(state)
        key=Path.home()/'.ssh'/('pokerattack-rehearsal-'+nonce)
        result=subprocess.run(['ssh-keygen','-q','-t','ed25519','-N','','-f',str(key)],capture_output=True,timeout=15)
        if result.returncode:raise ValueError('Dedicated rehearsal SSH identity generation failed')
        key.chmod(0o600);state['admin_key_path']=str(key);save(state)
        vm=az('vm','create','--name',state['vm'],'--resource-group',group,'--location','eastus',
            '--image','Ubuntu2404','--size','Standard_B2s','--admin-username','pa-rehearsal',
            '--authentication-type','ssh','--ssh-key-values',str(key)+'.pub',
            '--nsg-rule','NONE','--public-ip-sku','Standard','--storage-sku','StandardSSD_LRS',
            '--os-disk-size-gb','64','--assign-identity','[system]',
            '--tags','purpose=pokerattack-deployment-rehearsal','rehearsal_nonce='+nonce,timeout=900)
        state['vm_id']=vm['id'];state['public_ip']=vm.get('publicIpAddress');save(state)
        principal=az('vm','identity','show','--name',state['vm'],'--resource-group',group)['principalId']
        assignment=az('role','assignment','create','--assignee-object-id',principal,'--assignee-principal-type','ServicePrincipal',
            '--role','Storage Blob Data Contributor','--scope',storage['id'])
        state['role_assignment_ids'].append(assignment['id']);state['phase']='provisioned'
    finally:
        state['owned_resource_ids']=[item['id'] for item in az('resource','list','--resource-group',group)]
        save(state)
    print('Temporary rehearsal VM provisioned; state and cleanup identities recorded in artifacts/H02-replica/azure-state.json.')


def destroy():
    check_account();state=json.loads(STATE.read_bytes());group=state['group']
    if state['subscription']!=SUBSCRIPTION or not re.fullmatch(r'rg-pa-release-rehearsal-[a-f0-9]{12}',group):
        raise ValueError('Refuse unowned group')
    actual=az('group','show','--name',group)
    if actual.get('tags',{}).get('rehearsal_nonce')!=state['nonce'] or actual.get('tags',{}).get('purpose')!='pokerattack-deployment-rehearsal':
        raise ValueError('Rehearsal ownership tags differ')
    current={item['id'] for item in az('resource','list','--resource-group',group)}
    if not current.issubset(set(state['owned_resource_ids'])):raise ValueError('Unrecorded resources block group cleanup')
    for identity in state['role_assignment_ids']:az('role','assignment','delete','--ids',identity)
    az('group','delete','--name',group,'--yes','--no-wait')
    state['phase']='deleting';state['delete_requested_utc']=datetime.datetime.now(datetime.timezone.utc).isoformat();save(state)
    print('Owned rehearsal group deletion requested; read-back must confirm absence before cleanup is complete.')


if __name__=='__main__':
    if sys.argv[1:]==['create']:create()
    elif sys.argv[1:]==['destroy']:destroy()
    else:raise SystemExit('Use create or destroy for the fixed recorded rehearsal only.')
