"""Create H02's seeded capable baseline on the dedicated disposable machine.

All inputs are private fixture data. This is not a production bootstrap/promotion
and does not stand in for H05's transition from the actual legacy capabilities.
"""
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import re
import subprocess
import sys
import time
import urllib.request

ROOT=Path('/opt/pa-rehearsal/source')
PRIVATE=Path('/var/lib/pa-rehearsal')
CADDY='caddy@sha256:9c1b76fb701c1f77383d3124d9f626e38bcf83bc1255ed47475d4f2420701d03'
REGISTRY='registry@sha256:a3d8aaa63ed8681a604f1dea0aa03f100d5895b6a58ace528858a7b332415373'


def client_file(index):
    names=json.loads((PRIVATE/'inputs.json').read_bytes())['current_client_files']
    name=names[index]
    if not re.fullmatch(r'[A-Za-z0-9_.-]{1,128}',name):raise ValueError('Invalid owned client fixture file')
    return PRIVATE/'downloads'/name


def run(*args,**kwargs):
    result=subprocess.run(args,capture_output=True,timeout=600,**kwargs)
    if result.returncode:raise ValueError('Isolated replica command failed: '+Path(args[0]).name)
    return result.stdout.decode().strip()


def write(path,raw,mode=0o600):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_bytes(raw.encode() if isinstance(raw,str) else raw);path.chmod(mode)


def machine_guard():
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
    request=urllib.request.Request('http://169.254.169.254/metadata/instance/compute?api-version=2021-02-01',headers={'Metadata':'true'})
    with opener.open(request,timeout=5) as response:compute=json.load(response)
    if not compute['resourceGroupName'].lower().startswith('rg-pa-release-rehearsal-') or compute['name']!='pa-rehearsal':
        raise ValueError('Not the disposable rehearsal VM')
    for name in ('api.playpokerattack.com','playpokerattack.com','www.playpokerattack.com','salmon-hill-0056eb60f.2.azurestaticapps.net','chefknife.azurecr.io'):
        if socket.gethostbyname(name)!='127.0.0.1':raise ValueError('Production-shaped fixture DNS must stay on loopback')
    if '170.187.142.115/32' not in run('ip','address','show','dev','lo'):raise ValueError('SSH fixture target must be loopback')


def install():
    Path('/opt/pokerattack').mkdir(mode=0o755);Path('/opt/pokerattack').chmod(0o755)
    modules=('client_lease.py','host_manifests.py','release-control.py','release-legacy-guard.py','release-recovery.py',
        'release-status.py','release_commands.py','release_evidence.py','release_lease.py','release_verifier.py',
        'secure_state.py','ssh-control.py','validate-config.py','compose.yaml')
    for name in modules:write(Path('/opt/pokerattack')/name,(ROOT/'deploy/linode'/name).read_bytes(),0o644)
    for path in (ROOT/'deploy/linode').glob('pokerattack-*'):
        if path.is_file():write(Path('/usr/local/sbin')/path.name,path.read_bytes(),0o755)
    for name in ('status.sudoers','control.sudoers'):
        target=Path('/etc/sudoers.d')/('pokerattack-'+name.split('.')[0])
        write(target,(ROOT/'deploy/linode'/name).read_bytes(),0o440);run('visudo','-cf',str(target))
    roles={'status':'pa-status','client-lease':'pa-client','deployment':'pa-deploy','evidence':'pa-evidence','promotion':'pa-promote'}
    (PRIVATE/'keys').mkdir(mode=0o700)
    for role,user in roles.items():
        run('useradd','--create-home','--shell','/bin/bash',user)
        key=PRIVATE/'keys'/role
        run('ssh-keygen','-q','-t','ed25519','-N','','-f',str(key))
        forced='/usr/local/sbin/pokerattack-ssh-status' if role=='status' else '/usr/local/sbin/pokerattack-ssh-control '+role
        path=Path('/home')/user/'.ssh';path.mkdir(mode=0o700)
        write(path/'authorized_keys','restrict,command="'+forced+'" '+key.with_suffix('.pub').read_text().strip()+'\n')
        run('chown','-R',user+':'+user,str(path))
    pub=Path('/etc/ssh/ssh_host_ed25519_key.pub').read_text().split()
    write(PRIVATE/'known_hosts','170.187.142.115 '+pub[0]+' '+pub[1]+'\n')
    for path in ('/etc/pokerattack','/etc/pokerattack/registry','/etc/pokerattack/production',
        '/etc/pokerattack/production/configs','/etc/pokerattack/production/verification',
        '/var/lib/pokerattack','/var/lib/pokerattack/production'):
        Path(path).mkdir(exist_ok=True,mode=0o700);Path(path).chmod(0o700)
    for name in ('releases','completed','interrupted','client-completed'):
        (Path('/var/lib/pokerattack/production')/name).mkdir(mode=0o700)
    admission=Path('/var/lib/pokerattack/production/admission');admission.mkdir(mode=0o755);admission.chmod(0o755)
    write(admission/'state.json','{"active":false}\n',0o644)
    write(Path('/etc/pokerattack/registry/config.json'),'{}\n')
    (PRIVATE/'runner-temp').mkdir(mode=0o700)


def certificates():
    tls=PRIVATE/'tls';tls.mkdir(mode=0o700)
    run('openssl','req','-x509','-newkey','rsa:2048','-nodes','-sha256','-days','1','-subj','/CN=Isolated PokerAttack H02 fixture CA',
        '-keyout',str(tls/'ca.key'),'-out',str(tls/'ca.crt'))
    run('openssl','req','-new','-newkey','rsa:2048','-nodes','-subj','/CN=api.playpokerattack.com',
        '-keyout',str(tls/'server.key'),'-out',str(tls/'server.csr'))
    names=['api.playpokerattack.com','playpokerattack.com','www.playpokerattack.com','salmon-hill-0056eb60f.2.azurestaticapps.net','chefknife.azurecr.io']
    write(tls/'extensions','basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\nsubjectAltName='+','.join('DNS:'+name for name in names)+'\n')
    run('openssl','x509','-req','-in',str(tls/'server.csr'),'-CA',str(tls/'ca.crt'),'-CAkey',str(tls/'ca.key'),
        '-CAcreateserial','-days','1','-sha256','-extfile',str(tls/'extensions'),'-out',str(tls/'server.crt'))
    write(Path('/usr/local/share/ca-certificates/pa-h02.crt'),(tls/'ca.crt').read_bytes(),0o644)
    run('update-ca-certificates')
    write(Path('/etc/docker/certs.d/chefknife.azurecr.io/ca.crt'),(tls/'ca.crt').read_bytes(),0o644)
    for nss in (Path('/root/.pki/nssdb'),Path('/root/.local/share/pki/nssdb')):
        nss.mkdir(parents=True,mode=0o700)
        run('certutil','-N','--empty-password','-d','sql:'+str(nss))
        run('certutil','-A','-d','sql:'+str(nss),'-n','pa-h02-isolated','-t','C,,','-i',str(tls/'ca.crt'))
    return tls


def gateway(tls):
    # All listeners stay on VM loopback. The VM NSG also has no public inbound
    # rule; no production traffic or real registry credentials enter this fixture.
    run('docker','run','-d','--name','h02-registry','--network','host','--env','REGISTRY_HTTP_ADDR=127.0.0.1:5000',
        '--env','REGISTRY_HTTP_RELATIVEURLS=true',REGISTRY)
    artifact=PRIVATE/'served-client';artifact.mkdir()
    # The input archive has already passed client_artifacts.check_archive. Use
    # that same bounded extractor to avoid accepting caller tar paths here.
    sys.path.insert(0,str(ROOT/'deploy/release'))
    from client_artifacts import check_archive
    manifest=json.loads(client_file(1).read_bytes())
    artifact.rmdir();check_archive(client_file(0),manifest,artifact)
    configuration='''{
  auto_https disable_redirects
  admin off
}
https://chefknife.azurecr.io {
  bind 127.0.0.1
  tls /tls/server.crt /tls/server.key
  reverse_proxy 127.0.0.1:5000
}
https://api.playpokerattack.com {
  bind 127.0.0.1
  tls /tls/server.crt /tls/server.key
  @private path /_ops/* /test/*
  respond @private 404
  reverse_proxy 127.0.0.1:8081
}
https://playpokerattack.com, https://www.playpokerattack.com, https://salmon-hill-0056eb60f.2.azurestaticapps.net {
  bind 127.0.0.1
  tls /tls/server.crt /tls/server.key
  root * /www
  header Blazor-Environment prod
  header Cache-Control no-store
  try_files {path} /index.html
  file_server
}
'''
    write(PRIVATE/'Caddyfile',configuration,0o644)
    run('docker','run','-d','--name','h02-caddy','--network','host','--mount','type=bind,source='+str(PRIVATE/'Caddyfile')+',target=/etc/caddy/Caddyfile,readonly',
        '--mount','type=bind,source='+str(tls)+',target=/tls,readonly',
        '--mount','type=bind,source='+str(artifact)+',target=/www,readonly',CADDY)
    for _ in range(30):
        try:
            with urllib.request.urlopen('https://chefknife.azurecr.io/v2/',timeout=2) as response:
                if response.status==200:return manifest
        except Exception:time.sleep(1)
    raise ValueError('Isolated TLS registry gateway did not become ready')


def publish_fixture(inputs):
    images={}
    for name,index,repository in [('api',0,'webapi'),('migration',1,'data')]:
        original=inputs['images'][index];reference='chefknife.azurecr.io/chefknifestudios.pokerattack.server.'+repository
        tag=reference+':'+inputs['source_sha']
        run('docker','tag',original['reference'],tag);run('docker','push',tag)
        info=json.loads(run('docker','image','inspect',tag))[0]
        choices=[item for item in info['RepoDigests'] if item.startswith(reference+'@sha256:')]
        if len(choices)!=1 or info['Id']!=original['image_id']:raise ValueError('Isolated registry content differs from tested image')
        digest=choices[0].split('@')[1];run('docker','pull',reference+'@'+digest)
        actual=json.loads(run('docker','image','inspect',reference+'@'+digest))[0]
        if actual['Id']!=original['image_id']:raise ValueError('Pulled fixture image differs')
        images[name]={'repository':reference,'digest':digest,'local_image_id':actual['Id'],'tag':inputs['source_sha'],
            'source_revision':inputs['source_sha'],'platform':'linux/amd64'}
    return images


def database_and_config(inputs):
    admin=secrets.token_urlsafe(32);runtime=secrets.token_urlsafe(32)
    # Explicit initialization of this disposable VM's empty fixture databases.
    # No migration command is reachable from a production release capability.
    run('/usr/sbin/runuser','-u','postgres','--','psql','-v','ON_ERROR_STOP=1',input=(
        "ALTER USER postgres PASSWORD '"+admin+"';\nCREATE DATABASE expensetrackerdb;\nCREATE DATABASE transitjazz_fixture;\n").encode())
    run('/usr/sbin/runuser','-u','postgres','--','psql','-d','transitjazz_fixture','-v','ON_ERROR_STOP=1',
        input=b'CREATE TABLE canary(value integer); INSERT INTO canary VALUES (42);\n')
    migration=PRIVATE/'migration.env'
    write(migration,'ConnectionStrings__PokerAttackDB=Host=127.0.0.1;Database=expensetrackerdb;Username=postgres;Password='+admin+';SSL Mode=Disable\n')
    try:run('docker','run','--rm','--network','host','--env-file',str(migration),inputs['images'][1]['reference'])
    finally:migration.unlink(missing_ok=True)
    run('/usr/sbin/runuser','-u','postgres','--','psql','-d','expensetrackerdb','-v','ON_ERROR_STOP=1',input=(
        "CREATE ROLE pokerattack_runtime LOGIN PASSWORD '"+runtime+"';\nGRANT USAGE ON SCHEMA \"PokerAttack\" TO pokerattack_runtime;\n"
        'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA "PokerAttack" TO pokerattack_runtime;\n'
        'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA "PokerAttack" TO pokerattack_runtime;\n'
        'GRANT SELECT ON public."__EFMigrationsHistory" TO pokerattack_runtime;\n').encode())
    raw=('ASPNETCORE_ENVIRONMENT=Production\nAllowedHosts=api.playpokerattack.com;localhost;127.0.0.1;[::1]\n'
        'Hosting__AllowedOrigins=https://playpokerattack.com;https://www.playpokerattack.com;https://salmon-hill-0056eb60f.2.azurestaticapps.net\n'
        'Testing__EnableEndpoints=false\nSentry__Dsn=\nLogging__LogLevel__Default=Warning\n'
        'ConnectionStrings__PokerAttackDB=Host=127.0.0.1;Database=expensetrackerdb;Username=pokerattack_runtime;Password='+runtime+';SSL Mode=Disable\n')
    result={}
    for kind,comment in [('baseline','# seeded H02 capable baseline\n'),('candidate','# exact H02 candidate configuration\n')]:
        config=(raw+comment).encode();digest=hashlib.sha256(config).hexdigest()
        write(Path('/etc/pokerattack/production/configs')/(digest+'.env'),config);result[kind]=digest
    return result


def seed_baseline(inputs,images,configs,client):
    sys.path.insert(0,str(ROOT/'deploy/release'))
    from blob_store import LocalStore
    from client_archive import Archive
    from review import canonical
    workflow={'repository':'henryfaulkner/ChefKnifeStudios.PokerAttack','run_id':42424242424242,'run_attempt':1,
        'run_url':'https://github.com/henryfaulkner/ChefKnifeStudios.PokerAttack/actions/runs/42424242424242'}
    manifest={'format':'pokerattack-server-v1','source_sha':inputs['source_sha'],'workflow':workflow,
        'api':images['api'],'migration':images['migration'],'config_sha256':configs['candidate'],
        'schema':json.loads((ROOT/'deploy/release/schema-prerequisite.json').read_bytes()),
        'verification_references':['isolated-h02-fixture:actual-generated-proof-required-before-candidate-promotion']}
    base=Path('/var/lib/pokerattack/production');release=base/'releases/seeded-capable-baseline';release.mkdir(mode=0o700)
    config=configs['baseline'];image=images['api']['repository']+'@'+images['api']['digest']
    for name,value in [('image',image),('digest',images['api']['digest']),('config-id',config)]:write(release/name,value+'\n')
    write(release/'app.env',(Path('/etc/pokerattack/production/configs')/(config+'.env')).read_bytes())
    write(release/'release.env',f'API_IMAGE={image}\nAPP_ENV={release}/app.env\nAPP_PORT=8081\nADMISSION_STATE_DIRECTORY={base}/admission\nRELEASE_CONFIG_SHA256={config}\n')
    write(release/'release-manifest.json',canonical({**manifest,'config_sha256':config}))
    (base/'active').symlink_to(release);(base/'known-good').symlink_to(release)
    run('docker','compose','--project-name','pokerattack-production','--env-file',str(release/'release.env'),
        '-f','/opt/pokerattack/compose.yaml','up','-d','--no-deps','api')
    for _ in range(60):
        try:
            with urllib.request.urlopen('http://127.0.0.1:8081/health/ready',timeout=2) as response:
                if response.status==200:break
        except Exception:time.sleep(1)
    else:raise ValueError('Seeded capable baseline not ready')
    with urllib.request.urlopen('http://127.0.0.1:8081/release/status',timeout=3) as response:
        if json.load(response)['maintenance'] is not False:raise ValueError('Seeded API cannot read the open admission state')
    Archive(LocalStore(PRIVATE/'accepted-clients'),lambda *_:True).accept(client,client_file(0),workflow['run_id'],1)
    write(PRIVATE/'server-manifest.json',canonical(manifest))
    baseline=json.loads(run('/usr/local/sbin/pokerattack-status'))
    if not baseline['readiness']['ready'] or baseline['lease']:raise ValueError('Seeded baseline inconsistent')
    write(PRIVATE/'seeded-baseline.json',canonical({'fixture_only':True,'workflow_identity_is_fictional':True,
        'source_sha':inputs['source_sha'],'state':baseline,'configuration_hashes':configs,
        'canary_containers':{name:json.loads(run('docker','inspect',name))[0]['Id'] for name in ('h02-caddy','h02-registry')},
        'database_canary':'transitjazz_fixture.canary value=42'}))


def main():
    machine_guard()
    if (PRIVATE/'seeded-baseline.json').exists():raise ValueError('Inspect established replica instead of initializing again')
    inputs=json.loads((PRIVATE/'inputs.json').read_bytes())
    install();tls=certificates();client=gateway(tls)
    images=publish_fixture(inputs);configs=database_and_config(inputs)
    seed_baseline(inputs,images,configs,client)


if __name__=='__main__':main()
