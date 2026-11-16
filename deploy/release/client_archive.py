"""Immutable private blobs plus ETag-protected accepted current/previous pointers."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import tempfile
import time

from blob_store import AzureStore, Conflict, file_hash
from client_artifacts import canonical, check_archive, positive
from manifests import load_document, validate_document


def prefix(run, attempt, artifact_id, digest):
    for value in (run, attempt, artifact_id): positive(value)
    if not isinstance(digest, str) or not re.fullmatch(r'[a-f0-9]{64}', digest): raise ValueError('Invalid archive checksum')
    return f'releases/{run}/{attempt}/{artifact_id}/{digest}'


def record(manifest):
    validate_document('client-manifest', manifest)
    workflow = manifest['workflow']
    return {'source_sha': manifest['source_sha'], 'run_id': workflow['run_id'], 'run_attempt': workflow['run_attempt'],
            'artifact_id': manifest['artifact_id'], 'archive_sha256': manifest['archive_sha256'],
            'manifest_sha256': hashlib.sha256(canonical(manifest)).hexdigest()}


def record_prefix(value):
    if set(value) != {'source_sha', 'run_id', 'run_attempt', 'artifact_id', 'archive_sha256', 'manifest_sha256'}:
        raise ValueError('Invalid accepted record')
    for field, length in (('source_sha', 40), ('manifest_sha256', 64)):
        if not isinstance(value[field], str) or not re.fullmatch(r'[a-f0-9]{' + str(length) + '}', value[field]):
            raise ValueError('Invalid accepted identity')
    return prefix(value['run_id'], value['run_attempt'], value['artifact_id'], value['archive_sha256'])


class Archive:
    def __init__(self, store, lease_check=None):
        self.store, self.lease_check = store, lease_check

    def lease(self, run, attempt):
        if self.lease_check is None or self.lease_check(run, attempt) is not True:
            raise ValueError('Matching live client lease required')

    def read(self, key, kind=None):
        info = self.store.info(key)
        if info is None: return None, None
        with tempfile.TemporaryDirectory(prefix='pokerattack-archive-read-') as directory:
            path = Path(directory) / 'document.json'
            self.store.download(key, path, info['etag'])
            result = load_document(path.read_bytes(), kind)
        return result, info['etag']

    def immutable(self, key, source):
        info = self.store.info(key)
        if info is not None:
            with tempfile.TemporaryDirectory(prefix='pokerattack-archive-compare-') as directory:
                path = Path(directory) / 'existing'
                self.store.download(key, path, info['etag'])
                if file_hash(path) != file_hash(source): raise Conflict('Immutable archive identity already has different bytes')
            return
        self.store.upload(key, source, create=True)

    def write_immutable_json(self, key, value):
        with tempfile.TemporaryDirectory(prefix='pokerattack-archive-write-') as directory:
            path = Path(directory) / 'document.json'
            path.write_bytes(canonical(value))
            self.immutable(key, path)

    def store_candidate(self, manifest, archive):
        check_archive(archive, manifest)
        identity = record(manifest)
        key = record_prefix(identity)
        self.immutable(key + '/client.tar.gz', archive)
        self.write_immutable_json(key + '/manifest.json', manifest)
        return identity

    def pointers(self):
        value, etag = self.read('accepted.json')
        if value is None: return {'format': 'pokerattack-accepted-clients-v1', 'current': None, 'previous': None}, None
        if set(value) != {'format', 'current', 'previous'} or value['format'] != 'pokerattack-accepted-clients-v1':
            raise ValueError('Invalid accepted pointer document')
        for name in ('current', 'previous'):
            if value[name] is not None: record_prefix(value[name])
        return value, etag

    def accept(self, manifest, archive, run, attempt):
        # Called only after successful served-byte/browser verification by the
        # protected job. No public boolean/file input can bypass that workflow.
        self.lease(run, attempt)
        identity = self.store_candidate(manifest, archive)
        pointers, etag = self.pointers()
        history_key = 'accepted/' + record_prefix(identity)[len('releases/'):] + '.json'
        if pointers['current'] == identity:
            self.write_immutable_json(history_key, identity)
            return pointers
        result = {'format': pointers['format'], 'current': identity, 'previous': pointers['current']}
        # Hold host lease across verification and this CAS; a lost CAS stops.
        self.lease(run, attempt)
        with tempfile.TemporaryDirectory(prefix='pokerattack-archive-accept-') as directory:
            path = Path(directory) / 'accepted.json'; path.write_bytes(canonical(result))
            self.store.upload('accepted.json', path, etag=etag, create=etag is None)
        # Only successful CAS establishes acceptance. A failed CAS must not leave
        # an accepted history record that could authorize restoring a candidate.
        self.write_immutable_json(history_key, identity)
        return result

    def recover(self, run, attempt, artifact_id, digest, destination):
        manifest = self.accepted_manifest(run, attempt, artifact_id, digest)
        key = prefix(run, attempt, artifact_id, digest)
        with tempfile.TemporaryDirectory(prefix='pokerattack-archive-restore-') as directory:
            archive = Path(directory) / 'client.tar.gz'
            info = self.store.info(key + '/client.tar.gz')
            if not info: raise ValueError('Accepted archive unavailable')
            self.store.download(key + '/client.tar.gz', archive, info['etag'])
            check_archive(archive, manifest, destination)
        return manifest

    def accepted_manifest(self, run, attempt, artifact_id, digest):
        key = prefix(run, attempt, artifact_id, digest)
        identity, _ = self.read('accepted/' + key[len('releases/'):] + '.json')
        if identity is None:
            # Recover a committed pointer even when its history write/response
            # was interrupted. Never infer acceptance from mere uploaded blobs.
            pointers, _ = self.pointers()
            identity = next((value for value in (pointers['current'], pointers['previous'])
                             if value is not None and record_prefix(value) == key), None)
        if identity is None or record_prefix(identity) != key:
            raise ValueError('Selected artifact was never accepted')
        manifest, _ = self.read(key + '/manifest.json', 'client-manifest')
        if manifest is None or record(manifest) != identity: raise ValueError('Accepted manifest changed')
        return manifest

    def prune(self, run, attempt, now=None):
        self.lease(run, attempt)
        pointers, etag = self.pointers()
        pinned = {record_prefix(pointers[name]) for name in ('current', 'previous') if pointers[name] is not None}
        cutoff = (time.time() if now is None else now) - 30 * 86400
        values = self.store.list()
        groups = {}
        for value in values:
            match = re.fullmatch(r'(releases/[1-9][0-9]*/[1-9][0-9]*/[1-9][0-9]*/[a-f0-9]{64})/(client[.]tar[.]gz|manifest[.]json)', value['key'])
            if match: groups.setdefault(match[1], []).append(value)
        deleted = []
        for group, blobs in groups.items():
            if group in pinned or any(blob['modified_unix'] >= cutoff for blob in blobs): continue
            self.lease(run, attempt)
            if self.pointers()[1] != etag: raise Conflict('Accepted pointer changed during retention pass')
            for blob in blobs:
                self.store.delete(blob['key'], blob['etag']); deleted.append(blob['key'])
        return deleted


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--account', required=True)
    commands = parser.add_subparsers(dest='action', required=True)
    store = commands.add_parser('store'); store.add_argument('manifest'); store.add_argument('archive')
    recover = commands.add_parser('recover')
    for name in ('run', 'attempt', 'artifact_id', 'checksum', 'destination'): recover.add_argument(name)
    args = parser.parse_args()
    archive = Archive(AzureStore(args.account))
    if args.action == 'store':
        value = archive.store_candidate(load_document(Path(args.manifest).read_bytes(), 'client-manifest'), args.archive)
        print(json.dumps(value))
    else:
        archive.recover(int(args.run), int(args.attempt), int(args.artifact_id), args.checksum, args.destination)


if __name__ == '__main__': main()
