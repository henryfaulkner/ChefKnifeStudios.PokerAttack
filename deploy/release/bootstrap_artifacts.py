"""Exact static/legacy bootstrap archives, separate from modern accepted clients."""
import gzip
import hashlib
import json
from pathlib import Path,PurePosixPath
import re
import tarfile

MAX_TOTAL=512*1024*1024


def digest(path):
    with Path(path).open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()


def member(name):
    path=PurePosixPath(name)
    if (not isinstance(name,str) or not name or len(name)>512 or path.is_absolute() or '\\' in name or
            any(part in ('','..','.') for part in name.split('/')) or ':' in name or str(path)!=name):
        raise ValueError('Unsafe bootstrap artifact member')
    return name


def pack(directory,archive,mode):
    directory,archive=Path(directory).resolve(strict=True),Path(archive)
    if mode not in ('maintenance','legacy-recovery') or archive.exists() or archive.is_symlink() or directory in archive.resolve().parents:
        raise ValueError('Fresh external bootstrap archive required')
    files={};total=0
    for path in sorted(directory.rglob('*')):
        if path.is_symlink():raise ValueError('Symlinked bootstrap artifact')
        if path.is_dir():continue
        if not path.is_file():raise ValueError('Nonregular bootstrap artifact')
        name=member(path.relative_to(directory).as_posix());size=path.stat().st_size;total+=size
        if total>MAX_TOTAL or len(files)>=100000:raise ValueError('Bootstrap artifact too large')
        files[name]=digest(path)
    required={'index.html','staticwebapp.config.json'}|({'appsettings.json'} if mode=='legacy-recovery' else set())
    if not required.issubset(files):raise ValueError('Bootstrap artifact is incomplete')
    if mode=='legacy-recovery' and 'release.json' in files:raise ValueError('Modern artifacts use their own trusted manifest')
    with archive.open('xb') as raw,gzip.GzipFile(filename='',mode='wb',fileobj=raw,mtime=0) as compressed:
        with tarfile.open(mode='w|',fileobj=compressed,format=tarfile.USTAR_FORMAT) as stream:
            for name in files:
                path=directory/name;info=tarfile.TarInfo(name);info.size=path.stat().st_size;info.mode=0o644;info.mtime=0
                with path.open('rb') as content:stream.addfile(info,content)
    return {'format':'pokerattack-bootstrap-artifact-v1','mode':mode,'archive_sha256':digest(archive),'files':files}


def check(archive,manifest,destination=None):
    if (not isinstance(manifest,dict) or set(manifest)!={'format','mode','archive_sha256','files'} or
            manifest['format']!='pokerattack-bootstrap-artifact-v1' or manifest['mode'] not in ('maintenance','legacy-recovery') or
            not isinstance(manifest['files'],dict) or not 1<=len(manifest['files'])<=100000 or digest(archive)!=manifest['archive_sha256']):
        raise ValueError('Bootstrap archive identity differs')
    required={'index.html','staticwebapp.config.json'}|({'appsettings.json'} if manifest['mode']=='legacy-recovery' else set())
    if not required.issubset(manifest['files']) or Path(archive).stat().st_size>MAX_TOTAL:
        raise ValueError('Incomplete or oversized bootstrap archive')
    for name,checksum in manifest['files'].items():
        member(name)
        if not isinstance(checksum,str) or not re.fullmatch(r'[a-f0-9]{64}',checksum):raise ValueError('Invalid member checksum')
    names=set();total=0
    if destination is not None:
        destination=Path(destination)
        if destination.exists() or destination.is_symlink():raise ValueError('Fresh bootstrap extraction directory required')
        destination.mkdir(parents=True,mode=0o700)
    with tarfile.open(archive,'r:gz') as stream:
        for entry in stream:
            name=member(entry.name)
            if not entry.isfile() or name in names or name not in manifest['files'] or entry.size<0:
                raise ValueError('Unexpected bootstrap archive member')
            total+=entry.size
            if total>MAX_TOTAL:raise ValueError('Expanded bootstrap archive too large')
            names.add(name);content=stream.extractfile(entry);hasher=hashlib.sha256()
            target=None
            if destination is not None:
                target=destination/name;target.parent.mkdir(parents=True,exist_ok=True);target=target.open('xb')
            try:
                for chunk in iter(lambda:content.read(1024*1024),b''):
                    hasher.update(chunk)
                    if target:target.write(chunk)
            finally:
                if target:target.close()
            if hasher.hexdigest()!=manifest['files'][name]:raise ValueError('Bootstrap member checksum differs')
    if names!=set(manifest['files']):raise ValueError('Bootstrap member set differs')
    return True


def publication(manifest):
    names=('index.html',) if manifest['mode']=='maintenance' else ('index.html','appsettings.json')
    return {'mode':manifest['mode'],'archive_sha256':manifest['archive_sha256'],'client':None,
            'probes':{'/'+name:manifest['files'][name] for name in names}}
