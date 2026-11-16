"""Preserve exact legacy tools and bind first introduction to its recovery pair."""
import hashlib
import os
from pathlib import Path
import stat

import host_manifests
import secure_state as storage

RECOVERY=Path('/opt/pokerattack/recovery')
CONFIG=Path('/etc/pokerattack/production/bootstrap-recovery.json')
SOURCES={'compose.yaml':Path('/opt/pokerattack/compose.yaml'),
         'validate-config.py':Path('/opt/pokerattack/validate-config.py'),
         'pokerattack-deploy':Path('/usr/local/sbin/pokerattack-deploy'),
         'pokerattack-ssh-deploy':Path('/usr/local/sbin/pokerattack-ssh-deploy')}


def canonical(value):
    import json
    return json.dumps(value,sort_keys=True,separators=(',',':')).encode()


def read_tool(path):
    storage.trusted_parents(path)
    descriptor=os.open(path,os.O_RDONLY|os.O_NOFOLLOW)
    with os.fdopen(descriptor,'rb') as stream:
        info=os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_uid!=0 or info.st_mode&0o022 or info.st_size>262144:
            raise ValueError('Untrusted legacy tool')
        return stream.read(262145)


def preserve(sources=None):
    if os.geteuid()!=0:raise ValueError('Root operator only')
    sources=SOURCES if sources is None else sources
    if set(sources)!=set(SOURCES):raise ValueError('Unexpected recovery tool set')
    originals={name:read_tool(path) for name,path in sources.items()}
    hashes={name:hashlib.sha256(raw).hexdigest() for name,raw in originals.items()}
    digest=hashlib.sha256(canonical(hashes)).hexdigest();directory=RECOVERY/digest
    if originals['pokerattack-deploy'].count(b'install_dir=/opt/pokerattack\n')!=1:
        raise ValueError('Legacy tooling contract differs; inspect before preservation')
    # Keep original bytes as evidence and a separately hashed runnable copy.
    # Merely saving the old script is insufficient: its hard-coded Compose and
    # validator directory would otherwise resolve to newly installed tooling.
    files={**originals,'original-pokerattack-deploy':originals['pokerattack-deploy']}
    files['pokerattack-deploy']=originals['pokerattack-deploy'].replace(b'install_dir=/opt/pokerattack\n',
        ('install_dir='+str(directory)+'\n').encode())
    RECOVERY.mkdir(parents=True,mode=0o700,exist_ok=True);storage.trusted_parents(RECOVERY/'placeholder')
    directory.mkdir(mode=0o700,exist_ok=True)
    storage.trusted_parents(directory/'placeholder')
    for name,raw in files.items():
        path=directory/name
        if path.exists() or path.is_symlink():
            if read_tool(path)!=raw:raise ValueError('Existing recovery bundle differs')
        else:
            storage.write_bytes(path,raw,0o700 if name=='pokerattack-deploy' else 0o600)
    return {'bundle_sha256':digest,'files':{name:hashlib.sha256(raw).hexdigest() for name,raw in files.items()}}


def validate_tools(value):
    host_manifests.fields(value,('bundle_sha256','files'))
    host_manifests.pattern(value['bundle_sha256'],r'[a-f0-9]{64}')
    names=set(SOURCES)|{'original-pokerattack-deploy'}
    if not isinstance(value['files'],dict) or set(value['files'])!=names:raise ValueError('Incomplete recovery tooling')
    originals={name:value['files']['original-pokerattack-deploy' if name=='pokerattack-deploy' else name] for name in SOURCES}
    if hashlib.sha256(canonical(originals)).hexdigest()!=value['bundle_sha256']:raise ValueError('Recovery bundle identity differs')
    directory=RECOVERY/value['bundle_sha256']
    for name,digest in value['files'].items():
        host_manifests.pattern(digest,r'[a-f0-9]{64}')
        if hashlib.sha256(read_tool(directory/name)).hexdigest()!=digest:raise ValueError('Saved recovery tool bytes changed')
    return directory


def snapshot(live):
    record=storage.read(CONFIG)
    host_manifests.fields(record,('format','pair','source_sha','image_id','client','tooling'))
    if record['format']!='pokerattack-bootstrap-recovery-v1':raise ValueError('Wrong recovery format')
    host_manifests.fields(record['pair'],('digest','config_sha256'))
    host_manifests.pattern(record['pair']['digest'],r'sha256:[a-f0-9]{64}')
    host_manifests.pattern(record['pair']['config_sha256'],r'[a-f0-9]{64}')
    host_manifests.pattern(record['source_sha'],r'[a-f0-9]{40}')
    host_manifests.pattern(record['image_id'],r'sha256:[a-f0-9]{64}')
    good=live['pairs']['known-good']
    if (not good or any(good[key]!=value for key,value in record['pair'].items()) or
            live['runtime']['source_revision']!=record['source_sha'] or live['runtime']['image_id']!=record['image_id']):
        raise ValueError('Preserved recovery differs from the currently accepted runtime')
    from bootstrap_control import publication
    publication(record['client'])
    if record['client']['mode']!='legacy-recovery':raise ValueError('Expected exact compatible legacy client')
    validate_tools(record['tooling'])
    return record
