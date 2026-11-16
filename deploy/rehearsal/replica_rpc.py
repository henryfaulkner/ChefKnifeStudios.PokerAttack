"""Authenticated Azure control-plane execution on only the owned H02 VM."""
import hashlib
import json
from pathlib import Path
import re
import sys

import azure_replica


def invoke(name,script):
    state=json.loads(azure_replica.STATE.read_bytes())
    if state.get('phase')!='provisioned' or not re.fullmatch(r'rg-pa-release-rehearsal-[a-f0-9]{12}',state['group']):
        raise ValueError('No owned provisioned replica')
    digest=hashlib.sha256(script.encode()).hexdigest()
    # Logs stay on the disposable VM, not in the 4 KiB command response. Only a
    # sentinel following a successful script is interpreted as success.
    wrapper='''#!/bin/bash
set -euo pipefail
umask 077
mkdir -p /var/lib/pa-rehearsal/logs
(
'''+script+'\n) > /var/lib/pa-rehearsal/logs/'+name+'.log 2>&1\nprintf "H02_SCRIPT_SUCCESS '+digest+'\\n"\n'
    path=azure_replica.STATE.parent/(name+'.sh');path.write_text(wrapper,encoding='utf-8',newline='\n')
    result=azure_replica.az('vm','run-command','invoke','--resource-group',state['group'],'--name',state['vm'],
        '--command-id','RunShellScript','--scripts','@'+str(path),timeout=1200)
    (azure_replica.STATE.parent/(name+'-control-response.json')).write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    if 'H02_SCRIPT_SUCCESS '+digest not in json.dumps(result):
        raise ValueError('Replica script failed or outcome unknown; inspect its protected log before retrying')
    print(name+': completed on the dedicated replica')


def bootstrap():
    state=json.loads(azure_replica.STATE.read_bytes())
    script=(Path(__file__).with_name('setup-replica.sh')).read_text(encoding='utf-8')
    script=script.replace('__REHEARSAL_GROUP__',state['group']).replace('__REHEARSAL_VM__',state['vm'])
    invoke('setup-replica',script)


if __name__=='__main__':
    if sys.argv[1:]==['bootstrap']:bootstrap()
    else:raise SystemExit('Use bootstrap; other execution is explicit in the rehearsal harness.')
