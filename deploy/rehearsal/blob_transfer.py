"""VM managed-identity download from its private, dedicated rehearsal account."""
import hashlib
import http.client
import json
from pathlib import Path
import re
import sys
import urllib.parse
import urllib.request


def download(account,name,expected):
    if not re.fullmatch(r'parehearsal[a-f0-9]{12}',account):raise ValueError('Wrong private rehearsal account')
    if not re.fullmatch(r'[A-Za-z0-9_.-]{1,128}',name) or not re.fullmatch(r'[a-f0-9]{64}',expected):raise ValueError('Invalid owned artifact')
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
    request=urllib.request.Request('http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fstorage.azure.com%2F',headers={'Metadata':'true'})
    with opener.open(request,timeout=10) as response:token=json.load(response)['access_token']
    request=urllib.request.Request('https://'+account+'.blob.core.windows.net/h02/'+urllib.parse.quote(name),
        headers={'Authorization':'Bearer '+token,'x-ms-version':'2023-11-03'})
    path=Path('/var/lib/pa-rehearsal/downloads')/name;temporary=path.with_suffix(path.suffix+'.partial')
    digest=hashlib.sha256();size=0
    try:
        with opener.open(request,timeout=60) as response,temporary.open('xb') as stream:
            while True:
                chunk=response.read(1024*1024)
                if not chunk:break
                size+=len(chunk)
                if size>4*1024*1024*1024:raise ValueError('Oversized rehearsal input')
                digest.update(chunk);stream.write(chunk)
        if digest.hexdigest()!=expected:raise ValueError('Rehearsal artifact checksum differs')
        temporary.chmod(0o600);temporary.replace(path)
    finally:temporary.unlink(missing_ok=True)


def upload(account,name,path):
    if not re.fullmatch(r'parehearsal[a-f0-9]{12}',account) or not re.fullmatch(r'H0[2-5]-[A-Za-z0-9_.-]+\.json',name):
        raise ValueError('Only sanitized rehearsal reports may be uploaded')
    raw=Path(path).read_bytes()
    if len(raw)>4*1024*1024:raise ValueError('Oversized sanitized report')
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
    request=urllib.request.Request('http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fstorage.azure.com%2F',headers={'Metadata':'true'})
    with opener.open(request,timeout=10) as response:token=json.load(response)['access_token']
    request=urllib.request.Request('https://'+account+'.blob.core.windows.net/h02/'+name,data=raw,method='PUT',
        headers={'Authorization':'Bearer '+token,'x-ms-version':'2023-11-03','x-ms-blob-type':'BlockBlob',
                 'Content-Type':'application/json','If-None-Match':'*'})
    with opener.open(request,timeout=30) as response:
        if response.status!=201:raise ValueError('Report upload not confirmed')


if __name__=='__main__':
    try:download(*sys.argv[1:])
    except Exception:raise SystemExit('Private rehearsal download failed; credential material was not logged.')
