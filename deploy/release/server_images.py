"""Capture tested image bytes and publish only those bytes to fixed ACR repos.

No migration execution, deployment SSH or configuration access. Registry failures
stop this attempt; an uncertain push is inspected on the next explicit run.
"""
import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

from manifests import load_document

REPOSITORY = 'henryfaulkner/ChefKnifeStudios.PokerAttack'
REGISTRY = 'chefknife.azurecr.io'
REPOS = {'api': 'chefknifestudios.pokerattack.server.webapi', 'migration': 'chefknifestudios.pokerattack.server.data'}


def identity(value, expression):
    if not isinstance(value, str) or not re.fullmatch(expression, value):
        raise ValueError('Invalid image publication identity')
    return value


def command(*args, timeout=600):
    result = subprocess.run(args, capture_output=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError('Image operation failed; inspect registry state before another explicit attempt')
    return result.stdout


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def execution(env=os.environ):
    source = identity(env['GITHUB_SHA'], r'[a-f0-9]{40}')
    run = int(identity(env['GITHUB_RUN_ID'], r'[1-9][0-9]{0,19}'))
    attempt = int(identity(env['GITHUB_RUN_ATTEMPT'], r'[1-9][0-9]{0,4}'))
    return source, {'repository': REPOSITORY, 'run_id': run, 'run_attempt': attempt,
                    'run_url': f'https://github.com/{REPOSITORY}/actions/runs/{run}'}


def inspect_image(reference, source, expected_id=None):
    data = json.loads(command('docker', 'image', 'inspect', reference))
    if not isinstance(data, list) or len(data) != 1:
        raise ValueError('Ambiguous image inspection')
    image = data[0]
    image_id = identity(image['Id'], r'sha256:[a-f0-9]{64}')
    if (image['Os'], image['Architecture']) != ('linux', 'amd64'):
        raise ValueError('Wrong image platform')
    if image['Config'].get('Labels', {}).get('org.opencontainers.image.revision') != source:
        raise ValueError('Wrong image source revision')
    if expected_id is not None and image_id != expected_id:
        raise ValueError('Loaded/registry image differs from tested image')
    return image_id


def capture(directory, env=os.environ):
    from ci_docker_store import compatible
    if not compatible(): raise ValueError('Exact image archive requires the containerd image-store runner profile')
    source, workflow = execution(env)
    directory.mkdir(parents=True, exist_ok=False)
    images = {name: {'local_reference': f'pokerattack-{name if name == "api" else "migrations"}:ci-{source}'} for name in REPOS}
    for image in images.values():
        image['local_image_id'] = inspect_image(image['local_reference'], source)
    raw, archive = directory / 'tested-images.tar', directory / 'tested-images.tar.gz'
    command('docker', 'save', '--output', str(raw), *(image['local_image_id'] for image in images.values()))
    with raw.open('rb') as stream, archive.open('xb') as output, gzip.GzipFile(filename='', mode='wb', fileobj=output, mtime=0) as zipped:
        shutil.copyfileobj(stream, zipped)
    raw.unlink()
    report = {'format': 'pokerattack-tested-images-v1', 'source_sha': source, 'workflow': workflow,
              'archive_sha256': sha256(archive), 'images': images}
    (directory / 'tested-images.json').write_text(json.dumps(report, sort_keys=True) + '\n', encoding='utf-8')
    return report


def validate_capture(report, archive, env=os.environ):
    source, workflow = execution(env)
    if set(report) != {'format', 'source_sha', 'workflow', 'archive_sha256', 'images'} or report['format'] != 'pokerattack-tested-images-v1':
        raise ValueError('Invalid tested image record')
    built_workflow = report['workflow']
    if (not isinstance(built_workflow, dict) or set(built_workflow) != set(workflow)
            or type(built_workflow.get('run_attempt')) is not int or not 1 <= built_workflow['run_attempt'] <= workflow['run_attempt']
            or any(built_workflow[key] != value for key, value in workflow.items() if key != 'run_attempt')
            or report['source_sha'] != source):
        raise ValueError('Tested artifact is not from this execution')
    if identity(report['archive_sha256'], r'[a-f0-9]{64}') != sha256(archive):
        raise ValueError('Tested image archive checksum mismatch')
    if set(report['images']) != set(REPOS):
        raise ValueError('Wrong image set')
    for name, image in report['images'].items():
        if set(image) != {'local_reference', 'local_image_id'}:
            raise ValueError('Invalid tested image record')
        if image['local_reference'] != f'pokerattack-{name if name == "api" else "migrations"}:ci-{source}':
            raise ValueError('Wrong local image reference')
        identity(image['local_image_id'], r'sha256:[a-f0-9]{64}')
    return source, workflow


class Acr:
    def tags(self, repo, source):
        result = json.loads(command('az', 'acr', 'repository', 'show-tags', '--name', 'chefknife', '--repository', repo,
                                    '--query', f"[?@=='{source}']", '--output', 'json', '--only-show-errors'))
        if result not in ([], [source]):
            raise ValueError('Unexpected registry tag response')
        return bool(result)

    def digest(self, repo, source):
        result = json.loads(command('az', 'acr', 'repository', 'show', '--name', 'chefknife', '--image', repo + ':' + source,
                                    '--output', 'json', '--only-show-errors'))
        return identity(result['digest'], r'sha256:[a-f0-9]{64}')

    def verify(self, repo, digest, source, image_id):
        ref = REGISTRY + '/' + repo + '@' + identity(digest, r'sha256:[a-f0-9]{64}')
        command('docker', 'pull', '--platform', 'linux/amd64', ref)
        inspect_image(ref, source, image_id)

    def push(self, repo, source, image_id):
        ref = REGISTRY + '/' + repo + ':' + source
        command('docker', 'tag', image_id, ref)
        command('docker', 'push', ref)

    def lock(self, repo, source, digest):
        # Tag and manifest attributes are separate in ACR. Protect only these
        # exact release identities, never the shared repository or registry.
        for image in (repo + ':' + source, repo + '@' + digest):
            command('az', 'acr', 'repository', 'update', '--name', 'chefknife', '--image', image,
                    '--write-enabled', 'false', '--delete-enabled', 'false', '--output', 'none', '--only-show-errors')
            attrs = json.loads(command('az', 'acr', 'repository', 'show', '--name', 'chefknife', '--image', image,
                                       '--query', 'changeableAttributes', '--output', 'json', '--only-show-errors'))
            if attrs.get('writeEnabled') is not False or attrs.get('deleteEnabled') is not False:
                raise ValueError('Registry did not establish immutable image protection')


def publish_images(report, registry, recheck):
    source = report['source_sha']
    result = {}
    # Inspect BOTH immutable tags before any push; an existing conflicting
    # migration tag must not allow even the API publication to start.
    existing = {}
    for name, repo in REPOS.items():
        image_id = report['images'][name]['local_image_id']
        inspect_image(image_id, source, image_id)
        if registry.tags(repo, source):
            digest = registry.digest(repo, source)
            registry.verify(repo, digest, source, image_id)
            existing[name] = digest
    recheck()
    for name, repo in REPOS.items():
        image_id = report['images'][name]['local_image_id']
        if name not in existing:
            recheck()
            # Check again at the write boundary; never overwrite a discovered tag.
            if registry.tags(repo, source):
                raise ValueError('SHA tag appeared during publication; inspect before retry')
            registry.push(repo, source, image_id)
        digest = existing.get(name) or registry.digest(repo, source)
        registry.verify(repo, digest, source, image_id)
        registry.lock(repo, source, digest)
        if registry.digest(repo, source) != digest:
            raise ValueError('SHA tag changed during publication')
        result[name] = {'repository': REGISTRY + '/' + repo, 'digest': digest, 'local_image_id': image_id,
                        'tag': source, 'source_revision': source, 'platform': 'linux/amd64'}
    return {'format': 'pokerattack-published-images-v1', 'source_sha': source, 'workflow': report['workflow'],
            'images': result, 'tested_archive_sha256': report['archive_sha256']}


def publication_context(env):
    main = (env.get('GITHUB_REPOSITORY') == REPOSITORY and env.get('GITHUB_REF') == 'refs/heads/main'
            and env.get('GITHUB_EVENT_NAME') in ('push', 'workflow_dispatch'))
    # I02 must exercise real federation before the first API reset. The existing
    # build-only migration workflow may publish its exact tested images without
    # enabling delivery. It has no Linode/SWA authority or downstream deployment.
    bootstrap = (env.get('GITHUB_EVENT_NAME') == 'workflow_dispatch'
        and env.get('GITHUB_WORKFLOW_REF') == REPOSITORY + '/.github/workflows/migration-ci.yml@refs/heads/main'
        and env.get('BOOTSTRAP_ARTIFACT_PUBLICATION_ENABLED') == 'true'
        and env.get('DEPLOYMENT_AUTOMATION_ENABLED') == 'false')
    if not main or not (env.get('DEPLOYMENT_AUTOMATION_ENABLED') == 'true' or bootstrap):
        raise ValueError('Image publication requires enabled trusted main delivery or the fixed build-only bootstrap workflow')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('operation', choices=['capture', 'publish'])
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    if args.operation == 'capture':
        capture(args.directory)
        return
    env = os.environ
    publication_context(env)
    archive = args.directory / 'tested-images.tar.gz'
    from ci_docker_store import compatible
    if not compatible(): raise ValueError('Publication runner image store differs from the tested runner profile')
    report = load_document((args.directory / 'tested-images.json').read_bytes())
    source, _ = validate_capture(report, archive)
    def recheck():
        data = json.loads(command('gh', 'api', f'repos/{REPOSITORY}/git/ref/heads/main', timeout=30))
        if data['object']['sha'] != source:
            raise ValueError('Image candidate is behind current main')
    recheck()
    # Isolate Docker's registry login material from every other job/tool.
    with tempfile.TemporaryDirectory(prefix='pokerattack-acr-', dir=env['RUNNER_TEMP']) as private:
        os.chmod(private, 0o700)
        original = env.get('DOCKER_CONFIG')
        env['DOCKER_CONFIG'] = private
        try:
            command('docker', 'load', '--input', str(archive))
            command('az', 'acr', 'login', '--name', 'chefknife', '--only-show-errors')
            result = publish_images(report, Acr(), recheck)
            (args.directory / 'published-images.json').write_text(json.dumps(result, sort_keys=True) + '\n', encoding='utf-8')
        finally:
            if original is None: env.pop('DOCKER_CONFIG', None)
            else: env['DOCKER_CONFIG'] = original


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('Image capture/publication failed; no deployment was attempted. Inspect image/registry identity before another explicit attempt.')
        raise SystemExit(1)
