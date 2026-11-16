"""Exact-byte client packaging/restoration. No network, publication or secrets."""
import argparse
import gzip
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import tarfile
import tempfile

from manifests import load_document, validate_document

REPOSITORY = 'henryfaulkner/ChefKnifeStudios.PokerAttack'
MAX_FILE = 512 * 1024 * 1024
MAX_TOTAL = 2 * 1024 * 1024 * 1024
MAX_ARCHIVE = 1024 * 1024 * 1024


def canonical(document):
    return (json.dumps(document, sort_keys=True, separators=(',', ':'), allow_nan=False) + '\n').encode()


def sha(path):
    value = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(chunk)
    return value.hexdigest()


def positive(value):
    if type(value) is not int or not 0 < value < 10**20:
        raise ValueError('Expected numeric trusted artifact/run identity')
    return value


def member_name(name):
    if (not re.fullmatch(r'[A-Za-z0-9_./@+ -]+', name) or str(PurePosixPath(name)) != name or
            name.startswith('/') or any(part in ('', '.', '..') or part.endswith(('.', ' ')) or
                re.fullmatch(r'(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?', part, re.IGNORECASE)
                for part in name.split('/'))):
        raise ValueError('Unsafe/noncanonical client member')
    return name


def public_identity(source, run, attempt, minimum):
    if not isinstance(source, str) or not re.fullmatch(r'[a-f0-9]{40}', source):
        raise ValueError('Expected full source SHA')
    positive(run); positive(attempt); positive(minimum)
    if attempt > 99999:
        raise ValueError('Invalid attempt')
    return {'format': 'pokerattack-client-public-v1', 'source_sha': source, 'run_id': run,
            'run_attempt': attempt, 'minimum_server_capability': minimum}


def pack(directory, archive, source, run, attempt, minimum=1):
    directory, archive = Path(directory).resolve(strict=True), Path(archive)
    identity = public_identity(source, run, attempt, minimum)
    if archive.exists() or archive.is_symlink() or directory in archive.resolve().parents:
        raise ValueError('Archive must be fresh and outside candidate')
    for name in ('release.json', 'release.json.gz', 'release.json.br'):
        if (directory / name).exists() or (directory / name).is_symlink():
            raise ValueError('Cannot restamp an existing release')
    settings = load_document((directory / 'appsettings.json').read_bytes())
    apis = settings['AppSettings']['ExternalApis']
    if len(apis) != 2 or {entry['Name'] for entry in apis} != {'PokerAttackAPI', 'PokerAttackSignalR'} or any(
            entry['BaseUri'] != 'https://api.playpokerattack.com/' for entry in apis):
        raise ValueError('Client is not prepared for production')
    swa = load_document((directory / 'staticwebapp.config.json').read_bytes())
    if swa['globalHeaders']['Blazor-Environment'] != 'prod':
        raise ValueError('Incorrect Blazor environment')
    (directory / 'release.json').write_bytes(canonical(identity))
    files, folded, total = {}, set(), 0
    for path in sorted(directory.rglob('*')):
        if path.is_symlink():
            raise ValueError('Client symlinks are prohibited')
        if path.is_dir():
            continue
        if not path.is_file():
            raise ValueError('Nonregular client input')
        name = member_name(path.relative_to(directory).as_posix())
        if name.casefold() in folded:
            raise ValueError('Case-colliding client files')
        folded.add(name.casefold())
        size = path.stat().st_size
        total += size
        if size > MAX_FILE or total > MAX_TOTAL or len(files) >= 100000:
            raise ValueError('Client exceeds bounded archive limits')
        files[name] = sha(path)
    if not {'index.html', 'release.json', 'appsettings.json'}.issubset(files):
        raise ValueError('Required client files missing')
    with archive.open('xb') as raw, gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=0) as compressed:
        with tarfile.open(mode='w|', fileobj=compressed, format=tarfile.USTAR_FORMAT) as stream:
            for name in files:
                path = directory / name
                info = tarfile.TarInfo(name)
                info.size, info.mode, info.mtime = path.stat().st_size, 0o644, 0
                with path.open('rb') as content:
                    stream.addfile(info, content)
    if archive.stat().st_size > MAX_ARCHIVE:
        raise ValueError('Compressed archive too large')
    document = {'format': 'pokerattack-client-candidate-v1', 'source_sha': source,
                'workflow': {'repository': REPOSITORY, 'run_id': run, 'run_attempt': attempt,
                             'run_url': f'https://github.com/{REPOSITORY}/actions/runs/{run}'},
                'archive_sha256': sha(archive), 'settings_sha256': files['appsettings.json'], 'files': files,
                'minimum_server_capability': minimum, 'verification_references': [f'build:{run}/{attempt}']}
    # Validate all final fields without pretending an Actions artifact ID exists.
    finalize(document, 1)
    return document


def finalize(candidate, artifact_id):
    positive(artifact_id)
    if candidate.get('format') != 'pokerattack-client-candidate-v1' or 'artifact_id' in candidate:
        raise ValueError('Expected an unbound candidate, not a restamped manifest')
    result = {**candidate, 'format': 'pokerattack-client-v1', 'artifact_id': artifact_id}
    return validate_document('client-manifest', result)


def check_archive(archive, manifest, destination=None):
    validate_document('client-manifest', manifest)
    archive = Path(archive)
    if not archive.is_file() or archive.is_symlink() or archive.stat().st_size > MAX_ARCHIVE or sha(archive) != manifest['archive_sha256']:
        raise ValueError('Client archive identity differs')
    if destination is not None:
        destination = Path(destination)
        if destination.exists() or destination.is_symlink():
            raise ValueError('Restore destination must be fresh')
        destination.parent.mkdir(parents=True, exist_ok=True)
        staging = Path(tempfile.mkdtemp(prefix='.client-restore-', dir=destination.parent))
    else:
        staging = None
    seen, folded, total = set(), set(), 0
    release = None
    try:
        with tarfile.open(archive, mode='r|gz') as stream:
            for member in stream:
                name = member_name(member.name)
                if (not member.isfile() or member.sparse is not None or name in seen or name.casefold() in folded or
                        name not in manifest['files'] or member.size < 0 or member.size > MAX_FILE):
                    raise ValueError('Unsafe/duplicate/unexpected archive member')
                seen.add(name); folded.add(name.casefold()); total += member.size
                if len(seen) > 100000 or total > MAX_TOTAL:
                    raise ValueError('Expanded archive too large')
                content = stream.extractfile(member)
                value = hashlib.sha256()
                public_bytes = bytearray() if name == 'release.json' else None
                target = None
                if staging:
                    target_path = staging / name
                    target_path.parent.mkdir(parents=True, exist_ok=True)
                    target = target_path.open('xb')
                try:
                    for chunk in iter(lambda: content.read(1024 * 1024), b''):
                        value.update(chunk)
                        if public_bytes is not None:
                            if len(public_bytes) + len(chunk) > 65536:
                                raise ValueError('Public identity too large')
                            public_bytes.extend(chunk)
                        if target: target.write(chunk)
                finally:
                    if target: target.close()
                    content.close()
                if value.hexdigest() != manifest['files'][name]:
                    raise ValueError('Client member checksum differs')
                if public_bytes is not None: release = load_document(public_bytes)
        if seen != set(manifest['files']) or manifest['files']['appsettings.json'] != manifest['settings_sha256']:
            raise ValueError('Missing client files/settings')
        workflow = manifest['workflow']
        if release != public_identity(manifest['source_sha'], workflow['run_id'], workflow['run_attempt'], manifest['minimum_server_capability']):
            raise ValueError('Served identity differs from private archive manifest')
        if staging:
            staging.rename(destination)
            staging = None
        return True
    finally:
        if staging is not None:
            # Only this internally created staging directory can be removed.
            assert staging.parent.resolve() == destination.parent.resolve() and staging.name.startswith('.client-restore-') and not staging.is_symlink()
            shutil.rmtree(staging)


def identity_summary(manifest):
    validate_document('client-manifest', manifest)
    result = {key: manifest[key] for key in ('source_sha', 'workflow', 'artifact_id', 'archive_sha256',
                                            'settings_sha256', 'minimum_server_capability')}
    result['format'] = 'pokerattack-client-identity-v1'
    result['settings_files'] = {name: digest for name, digest in manifest['files'].items()
                               if re.fullmatch(r'appsettings(?:\.[A-Za-z0-9_.-]+)?\.json', name)}
    if len(canonical(result)) > 65536:
        raise ValueError('Client identity summary exceeds host bound')
    return result


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest='action', required=True)
    create = sub.add_parser('pack')
    for name in ('directory', 'archive', 'candidate', 'source', 'run', 'attempt'):
        create.add_argument(name)
    create.add_argument('--minimum', type=int, default=1)
    bind = sub.add_parser('finalize')
    for name in ('candidate', 'artifact_id', 'manifest'): bind.add_argument(name)
    check = sub.add_parser('check')
    check.add_argument('archive'); check.add_argument('manifest'); check.add_argument('--restore')
    summary = sub.add_parser('summary'); summary.add_argument('manifest'); summary.add_argument('output')
    args = parser.parse_args()
    if args.action == 'pack':
        result = pack(args.directory, args.archive, args.source, int(args.run), int(args.attempt), args.minimum)
        with Path(args.candidate).open('xb') as stream: stream.write(canonical(result))
    elif args.action == 'finalize':
        result = finalize(load_document(Path(args.candidate).read_bytes(), 'client-manifest'), int(args.artifact_id))
        with Path(args.manifest).open('xb') as stream: stream.write(canonical(result))
    else:
        manifest = load_document(Path(args.manifest).read_bytes(), 'client-manifest')
        if args.action == 'check': check_archive(args.archive, manifest, args.restore)
        else:
            with Path(args.output).open('xb') as stream: stream.write(canonical(identity_summary(manifest)))


if __name__ == '__main__':
    main()
