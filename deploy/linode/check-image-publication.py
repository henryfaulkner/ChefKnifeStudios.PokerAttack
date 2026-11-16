"""Exact-byte publication rehearsal against a UUID local registry, never ACR.

Requires already built synthetic cccc... API/migration fixture images. No source
publication/provenance claim. Registry tag locks are unit-tested, not provided by
the disposable Distribution registry. Cleanup removes only this UUID container.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tarfile
import hashlib
import re
import time
import urllib.error
import urllib.request
import uuid

from local_docker import assert_local_docker

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'deploy/release'))
import server_images as images

SOURCE = 'c' * 40
ENV = {'GITHUB_SHA': SOURCE, 'GITHUB_RUN_ID': '1', 'GITHUB_RUN_ATTEMPT': '1'}
PREFIX = 'pokerattack-image-fixture-' + uuid.uuid4().hex[:12]
directory = ROOT / 'artifacts' / PREFIX
CRANE = 'gcr.io/go-containerregistry/crane@sha256:e78770b31258a3846f878036d9c1f63fbe4c871f9f56990bf77fd95c013e3c1b'
assert_local_docker()


class LocalRegistry:
    def __init__(self, origin, namespace):
        self.origin, self.namespace, self.pushes, self.locks = origin, namespace, [], []
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def repo(self, repo): return self.namespace + '/' + repo

    def tags(self, repo, source):
        try:
            with self.http.open(self.origin + '/v2/' + self.repo(repo) + '/tags/list', timeout=10) as response:
                return source in (json.load(response).get('tags') or [])
        except urllib.error.HTTPError as error:
            if error.code != 404: raise
            return False

    def digest(self, repo, source):
        request = urllib.request.Request(self.origin + '/v2/' + self.repo(repo) + '/manifests/' + source, method='HEAD',
            headers={'Accept': 'application/vnd.docker.distribution.manifest.v2+json, application/vnd.oci.image.manifest.v1+json, application/vnd.oci.image.index.v1+json'})
        with self.http.open(request, timeout=10) as response:
            return images.identity(response.headers['Docker-Content-Digest'], r'sha256:[a-f0-9]{64}')

    def ref(self, repo, identity, digest=False):
        return self.origin.removeprefix('http://') + '/' + self.repo(repo) + ('@' if digest else ':') + identity

    def crane(self, *args):
        # Docker Desktop's outbound daemon proxy cannot reach the host loopback
        # port. The fixture client shares only this registry's network namespace.
        # --insecure selects the deliberately plain HTTP loopback fixture; this
        # adapter is never used for Azure, production or gameplay TLS validation.
        return images.command('docker', 'run', '--rm', '--network', 'container:' + PREFIX,
            '--mount', f'type=bind,source={directory},target=/fixture', CRANE, '--insecure', *args)

    def verify(self, repo, digest, source, image_id):
        ref = 'localhost:5000/' + self.repo(repo) + '@' + digest
        raw = self.crane('manifest', ref)
        if 'sha256:' + hashlib.sha256(raw.rstrip(b'\n')).hexdigest() != digest:
            raise ValueError('Registry response digest mismatch')
        manifest = json.loads(raw)
        # Docker's classic store uses config IDs; its containerd store can use
        # index IDs. Keep both distinct from the captured registry digest.
        if image_id not in (digest, manifest.get('config', {}).get('digest')):
            raise ValueError('Registry identity differs from tested image ID')
        config = json.loads(self.crane('--platform', 'linux/amd64', 'config', ref))
        if (config['os'], config['architecture']) != ('linux', 'amd64') or config['config'].get('Labels', {}).get('org.opencontainers.image.revision') != source:
            raise ValueError('Registry image platform/source mismatch')
        images.inspect_image(image_id, source, image_id)

    def push(self, repo, source, image_id):
        self.pushes.append(repo)
        archive = directory / (image_id[7:] + '.upload.tar')
        if not archive.exists():
            images.command('docker', 'save', '--output', str(archive), image_id)
        layout = directory / (image_id[7:] + '.layout')
        if not layout.exists():
            layout.mkdir()
            with tarfile.open(archive) as stream:
                for member in stream:
                    name = member.name.rstrip('/')
                    if member.isdir() and name in ('blobs', 'blobs/sha256'):
                        continue
                    if not member.isfile() or not re.fullmatch(r'(?:index[.]json|oci-layout|manifest[.]json|blobs/sha256/[a-f0-9]{64})', name):
                        raise ValueError('Unexpected Docker-generated archive member')
                    target = layout / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with stream.extractfile(member) as data, target.open('xb') as output:
                        import shutil
                        shutil.copyfileobj(data, output)
            # Docker save wraps the original index in a one-entry layout index.
            # Push that ORIGINAL index, preserving its platform/attestation bytes.
            nested = layout / 'blobs/sha256' / image_id[7:]
            if nested.is_file() and json.loads(nested.read_bytes()).get('mediaType', '').endswith('image.index.v1+json'):
                (layout / 'index.json').write_bytes(nested.read_bytes())
        self.crane('push', '--index', '/fixture/' + layout.name, 'localhost:5000/' + self.repo(repo) + ':' + source)

    def lock(self, repo, source, digest):
        # Distribution has no ACR metadata API. This only records requested locks.
        self.locks.append(repo)


created = False
try:
    # A registry-only retry can use an explicitly selected previously captured
    # fixture archive; no production artifact or alternate source is accepted.
    reuse = os.environ.get('POKERATTACK_FIXTURE_REUSE_IMAGES')
    if reuse:
        directory = (ROOT / reuse).resolve(strict=True)
        if directory.parent != (ROOT / 'artifacts').resolve() or not directory.name.startswith('pokerattack-image-fixture-') or len(directory.name) != 38:
            raise ValueError('Reuse must select this rehearsal\'s local artifact directory')
        report = images.load_document((directory / 'tested-images.json').read_bytes())
        archive = directory / 'tested-images.tar.gz'
        images.validate_capture(report, archive, ENV)
    else:
        fixture_env = {**os.environ, 'POKERATTACK_FIXTURE_IMAGE': 'pokerattack-api:ci-' + SOURCE,
                   'POKERATTACK_FIXTURE_MIGRATION_IMAGE': 'pokerattack-migrations:ci-' + SOURCE,
                   'POKERATTACK_FIXTURE_SOURCE': SOURCE}
        check = subprocess.run([sys.executable, str(ROOT / 'deploy/linode/check-release-gameplay.py'), '--client-only'],
                           capture_output=True, text=True, env=fixture_env, timeout=900)
        if check.returncode:
            raise RuntimeError('Exact-image browser fixture failed; no image publication attempted')
        report = images.capture(directory, ENV)
        (directory / 'fixture-browser.json').write_bytes((ROOT / 'artifacts/E04-client-browser-result.json').read_bytes())
    archive = directory / 'tested-images.tar.gz'
    images.validate_capture(report, archive, ENV)
    images.command('docker', 'load', '--input', str(archive))
    images.command('docker', 'run', '-d', '--name', PREFIX, '-p', '127.0.0.1::5000',
                   '--tmpfs', '/var/lib/registry', 'registry:2')
    created = True
    port = json.loads(images.command('docker', 'inspect', PREFIX))[0]['NetworkSettings']['Ports']['5000/tcp'][0]['HostPort']
    origin = 'http://127.0.0.1:' + port
    for _ in range(30):
        try:
            with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(origin + '/v2/', timeout=2) as response:
                assert response.status == 200
            break
        except OSError:
            time.sleep(0.2)
    else:
        raise RuntimeError('Disposable registry did not become ready')
    conflicting = LocalRegistry(origin, 'conflict')
    conflicting.push(images.REPOS['migration'], SOURCE, report['images']['api']['local_image_id'])
    conflicting.pushes.clear()
    try:
        images.publish_images(report, conflicting, lambda: None)
    except ValueError:
        assert conflicting.pushes == [], 'Conflict allowed an API push'
    else:
        raise AssertionError('Actual conflicting immutable SHA was accepted')
    registry = LocalRegistry(origin, 'accepted')
    result = images.publish_images(report, registry, lambda: None)
    assert len(registry.pushes) == 2 and len(registry.locks) == 2
    registry.pushes.clear()
    repeated = images.publish_images(report, registry, lambda: None)
    assert repeated == result and registry.pushes == []
    for name, image in result['images'].items():
        image['repository'] = registry.ref(images.REPOS[name], SOURCE).rsplit(':', 1)[0]
    output = {'fixture_only': True, 'source_identity': 'SYNTHETIC cccc...; not published Git provenance',
              'archive_sha256': report['archive_sha256'], 'exact_image_archive_loaded': True,
              'https_browser_and_disposable_migration_passed': True, 'conflicting_sha_rejected_before_api_push': True,
              'repeated_identical_sha_did_not_push': True, 'registry_digests_and_image_ids_match': True,
              'registry_transport': 'Isolated crane client with plain HTTP loopback registry; production ACR Docker transport not exercised',
              'acr_locks_live_verified': False, 'images': result['images']}
    (ROOT / 'artifacts/F02-image-publication-result.json').write_text(json.dumps(output, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(output))
finally:
    if created:
        assert PREFIX.startswith('pokerattack-image-fixture-') and len(PREFIX) == 38
        subprocess.run(['docker', 'rm', '-f', PREFIX], capture_output=True, timeout=30)
