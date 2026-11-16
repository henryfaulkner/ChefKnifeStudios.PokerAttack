"""Read-only monitoring shape; never serialize authenticated exporter blocks."""
import datetime
import hashlib
import json
from pathlib import Path
import re
import subprocess


def main():
    raw=Path('/etc/alloy/config.alloy').read_text()
    # Only the known Unix exporter/scrape/allowlist blocks are needed. Do not
    # print source lines from any block containing credential-related tokens.
    blocks=[]
    for match in re.finditer(r'(?m)^(prometheus\.(?:exporter\.unix|scrape|relabel))\s+"([A-Za-z0-9_]+)"\s*\{',raw):
        start=match.start();pos=match.end();depth=1
        while pos<len(raw) and depth:
            if raw[pos]=='{':depth+=1
            if raw[pos]=='}':depth-=1
            pos+=1
        block=raw[start:pos]
        if re.search(r'password|token|authorization|secret|basic_auth|bearer',block,re.I):
            blocks.append({'type':match[1],'name':match[2],'withheld':'contains authentication configuration'})
        else:blocks.append({'type':match[1],'name':match[2],'configuration':block})
    def run(*args):return subprocess.check_output(args,text=True,timeout=10).strip()
    print(json.dumps({'timestamp_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'alloy_version':run('alloy','--version'),'config_sha256':hashlib.sha256(raw.encode()).hexdigest(),
        'service':run('systemctl','show','alloy','-p','User','-p','Group','-p','ActiveState'),
        'docker_version':run('docker','--version'),'blocks':blocks},indent=2))


if __name__=='__main__':main()
