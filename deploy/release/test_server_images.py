import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import server_images as images

SOURCE = 'c' * 40
IDS = {'api': 'sha256:' + '1' * 64, 'migration': 'sha256:' + '2' * 64}
DIGESTS = {'api': 'sha256:' + '3' * 64, 'migration': 'sha256:' + '4' * 64}
ENV = {'GITHUB_SHA': SOURCE, 'GITHUB_RUN_ID': '42', 'GITHUB_RUN_ATTEMPT': '1'}


class Registry:
    def __init__(self): self.existing, self.events, self.conflict, self.changed = {}, [], None, False
    def name(self, repo): return next(name for name, value in images.REPOS.items() if value == repo)
    def tags(self, repo, source): return self.name(repo) in self.existing
    def digest(self, repo, source):
        return 'sha256:' + 'f' * 64 if self.changed and self.events and self.events[-1][0] == 'lock' else DIGESTS[self.name(repo)]
    def verify(self, repo, digest, source, image_id):
        name = self.name(repo)
        if self.conflict == name: raise ValueError('Different content')
        assert digest == DIGESTS[name] and image_id == IDS[name] and source == SOURCE
        self.events.append(('verify', name))
    def push(self, repo, source, image_id):
        name = self.name(repo); self.events.append(('push', name)); self.existing[name] = True
    def lock(self, repo, source, digest): self.events.append(('lock', self.name(repo)))


class ImageTests(unittest.TestCase):
    def test_build_only_bootstrap_auth_cannot_enable_delivery_or_relax_main_trust(self):
        env = {'GITHUB_REPOSITORY': images.REPOSITORY, 'GITHUB_REF': 'refs/heads/main',
               'GITHUB_EVENT_NAME': 'workflow_dispatch', 'DEPLOYMENT_AUTOMATION_ENABLED': 'false',
               'BOOTSTRAP_ARTIFACT_PUBLICATION_ENABLED': 'true',
               'GITHUB_WORKFLOW_REF': images.REPOSITORY + '/.github/workflows/migration-ci.yml@refs/heads/main'}
        images.publication_context(env)
        for key, value in (('GITHUB_REF', 'refs/heads/feature'), ('GITHUB_EVENT_NAME', 'pull_request'),
                           ('BOOTSTRAP_ARTIFACT_PUBLICATION_ENABLED', 'false'),
                           ('GITHUB_WORKFLOW_REF', images.REPOSITORY + '/.github/workflows/server.yml@refs/heads/main'),
                           ('GITHUB_REPOSITORY', 'other/repository'), ('DEPLOYMENT_AUTOMATION_ENABLED', '')):
            with self.subTest(key=key), self.assertRaises(ValueError):
                images.publication_context({**env, key: value})
        # The production controller's existing context gate remains unchanged.
        from client_publish import trusted_context
        with self.assertRaises(ValueError):
            trusted_context(env)

    def setUp(self):
        self.report = {'format': 'pokerattack-tested-images-v1', 'source_sha': SOURCE,
            'workflow': images.execution(ENV)[1], 'archive_sha256': hashlib.sha256(b'archive').hexdigest(),
            'images': {name: {'local_reference': f'pokerattack-{name if name == "api" else "migrations"}:ci-{SOURCE}',
                             'local_image_id': value} for name, value in IDS.items()}}
        self.registry = Registry()
        self.inspection = patch.object(images, 'inspect_image', side_effect=lambda reference, source, expected=None: expected)
        self.inspection.start(); self.addCleanup(self.inspection.stop)

    def test_tested_image_ids_are_the_pushed_and_recorded_ids(self):
        checked = []
        result = images.publish_images(self.report, self.registry, lambda: checked.append(True))
        self.assertEqual(len(checked), 3)
        for name, item in result['images'].items():
            self.assertEqual(item['local_image_id'], IDS[name])
            self.assertEqual(item['digest'], DIGESTS[name])
            self.assertEqual(item['source_revision'], SOURCE)
            self.assertIn(('push', name), self.registry.events)
            self.assertIn(('lock', name), self.registry.events)

    def test_existing_matching_sha_is_verified_and_never_pushed_again(self):
        self.registry.existing = dict.fromkeys(IDS, True)
        images.publish_images(self.report, self.registry, lambda: None)
        self.assertFalse(any(event[0] == 'push' for event in self.registry.events))

    def test_conflicting_migration_tag_blocks_both_publications(self):
        self.registry.existing['migration'] = True; self.registry.conflict = 'migration'
        with self.assertRaises(ValueError): images.publish_images(self.report, self.registry, lambda: None)
        self.assertFalse(any(event[0] == 'push' for event in self.registry.events))

    def test_main_changed_or_tag_appeared_at_write_boundary_blocks_push(self):
        def stale(): raise ValueError('Stale main')
        with self.assertRaises(ValueError): images.publish_images(self.report, self.registry, stale)
        self.assertEqual(self.registry.events, [])
        calls = []
        def appeared():
            calls.append(True)
            if len(calls) == 2: self.registry.existing['api'] = True
        with self.assertRaises(ValueError): images.publish_images(self.report, self.registry, appeared)
        self.assertEqual(self.registry.events, [])

    def test_changed_digest_after_lock_does_not_emit_publication_record(self):
        self.registry.changed = True
        with self.assertRaises(ValueError): images.publish_images(self.report, self.registry, lambda: None)
        self.assertNotIn(('push', 'migration'), self.registry.events)

    def test_archive_and_execution_provenance_are_checked_before_docker_load(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / 'images.tar.gz'; archive.write_bytes(b'archive')
            images.validate_capture(self.report, archive, ENV)
            # A failed publish job may reuse the earlier successful build of
            # the same run, preserving its original artifact provenance.
            images.validate_capture(self.report, archive, {**ENV, 'GITHUB_RUN_ATTEMPT': '2'})
            for change in ('checksum', 'source', 'attempt', 'reference'):
                bad = copy.deepcopy(self.report)
                if change == 'checksum': bad['archive_sha256'] = '0' * 64
                if change == 'source': bad['source_sha'] = 'a' * 40
                if change == 'attempt': bad['workflow']['run_attempt'] = 2
                if change == 'reference': bad['images']['api']['local_reference'] = 'unrelated:latest'
                with self.assertRaises(ValueError): images.validate_capture(bad, archive, ENV)

    def test_real_inspector_rejects_wrong_platform_revision_and_image_id(self):
        self.inspection.stop()
        raw = {'Id': IDS['api'], 'Os': 'linux', 'Architecture': 'amd64',
               'Config': {'Labels': {'org.opencontainers.image.revision': SOURCE}}}
        for change in ('platform', 'source', 'id'):
            bad = copy.deepcopy(raw)
            if change == 'platform': bad['Architecture'] = 'arm64'
            if change == 'source': bad['Config']['Labels']['org.opencontainers.image.revision'] = 'a' * 40
            if change == 'id': bad['Id'] = IDS['migration']
            with patch.object(images, 'command', return_value=json.dumps([bad]).encode()):
                with self.assertRaises(ValueError): images.inspect_image('fixed:ref', SOURCE, IDS['api'])

    def test_acr_locks_only_exact_tag_and_digest_and_verifies_both(self):
        calls = []
        def run(*args, **kwargs):
            calls.append(args)
            return json.dumps({'writeEnabled': False, 'deleteEnabled': False}).encode() if 'show' in args else b''
        with patch.object(images, 'command', side_effect=run):
            images.Acr().lock(images.REPOS['api'], SOURCE, DIGESTS['api'])
        self.assertEqual(len(calls), 4)
        refs = [args[args.index('--image') + 1] for args in calls]
        self.assertEqual(refs, [images.REPOS['api'] + ':' + SOURCE] * 2 + [images.REPOS['api'] + '@' + DIGESTS['api']] * 2)
        self.assertFalse(any('--repository' in args for args in calls))

    def test_acr_lock_not_installed_is_a_publication_failure(self):
        with patch.object(images, 'command', return_value=b'{"writeEnabled":true,"deleteEnabled":false}'):
            with self.assertRaises(ValueError): images.Acr().lock(images.REPOS['api'], SOURCE, DIGESTS['api'])


if __name__ == '__main__': unittest.main(verbosity=2)
