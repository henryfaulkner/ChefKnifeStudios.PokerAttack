"""Upload to owned private storage, then download via VM managed identity."""
import json
from pathlib import Path

import azure_replica
import replica_rpc


def main():
    state=json.loads(azure_replica.STATE.read_bytes());output=azure_replica.STATE.parent
    inputs=json.loads((output/'inputs.json').read_bytes())
    azure_replica.az('storage','container','create','--name','h02','--account-name',state['account'],'--auth-mode','login','--public-access','off')
    for name in inputs['artifacts']:
        azure_replica.az('storage','blob','upload','--account-name',state['account'],'--auth-mode','login',
            '--container-name','h02','--name',name,'--file',str(output/name),'--overwrite','false',timeout=600)
    # The small transfer helper contains no credentials; its bearer token is
    # acquired automatically inside the VM and is never printed or persisted.
    helper=Path(__file__).with_name('blob_transfer.py').read_text(encoding='utf-8')
    script="python3 - <<'PY'\nfrom pathlib import Path\nPath('/opt/pa-rehearsal/blob_transfer.py').write_text("+repr(helper)+")\nPY\n"
    for name,item in inputs['artifacts'].items():
        script+='python3 /opt/pa-rehearsal/blob_transfer.py '+state['account']+' '+name+' '+item['sha256']+'\n'
    script+='mkdir -p /opt/pa-rehearsal/source\ntar -xzf /var/lib/pa-rehearsal/downloads/source.tar.gz -C /opt/pa-rehearsal/source\n'
    script+='docker load --input /var/lib/pa-rehearsal/downloads/images.tar.gz\n'
    replica_rpc.invoke('stage-exact-inputs',script)
    print('Private inputs staged and checksum-verified on the owned replica.')


if __name__=='__main__':main()
