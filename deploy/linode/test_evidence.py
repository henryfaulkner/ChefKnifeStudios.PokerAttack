"""Socket-free root Linux fixtures. Never uses Docker/SSH/network credentials."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid

import host_manifests
import release_evidence as evidence
import release_lease
import secure_state as storage


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        parent = Path('/release-evidence-tests')
        parent.mkdir(exist_ok=True, mode=0o700)
        self.root = parent / uuid.uuid4().hex
        self.base = self.root / 'state/production'
        self.release = self.base / 'releases/candidate'
        for directory in (self.release, self.base / 'admission', self.base / 'interrupted',
                          self.root / 'etc/production/verification'):
            directory.mkdir(parents=True, mode=0o700)
        (self.base / 'active').symlink_to(self.release)
        self.addCleanup(shutil.rmtree, self.root)
        self.manifest = json.loads((Path(__file__).parents[1] / 'release/server-manifest.example.json').read_text())
        self.now = 1794757000
        self.lease = {'kind': 'server', 'phase': 'pending', 'run_id': 20, 'run_attempt': 2,
                      'digest': self.manifest['api']['digest'], 'config_sha256': self.manifest['config_sha256'],
                      'source_sha': self.manifest['source_sha'], 'manifest': self.manifest,
                      'manifest_sha256': hashlib.sha256(evidence.canonical(self.manifest)).hexdigest(),
                      'verifier_id': 'd' * 32, 'verifier_issued': True}
        storage.write(self.base / 'lease', self.lease)
        self.admission = {key: self.lease[key] for key in ('run_id', 'run_attempt', 'digest', 'config_sha256', 'source_sha')}
        self.admission.update(active=True, verifier={'id': 'd' * 32, 'issued_unix': self.now - 100,
                                                    'expires_unix': self.now + 800, 'revoked': False})
        storage.write(self.base / 'admission/state.json', self.admission, 0o644)
        pair = {'digest': self.lease['digest'], 'config_sha256': self.lease['config_sha256']}
        self.old = {'digest': 'sha256:' + 'f' * 64, 'config_sha256': 'e' * 64}
        self.live = {'consistent': True, 'transaction': False, 'readiness': {'ready': True},
                     'pairs': {'active': pair, 'pending': pair, 'known-good': self.old},
                     'runtime': {'source_revision': self.manifest['source_sha'],
                                 'image_id': self.manifest['api']['local_image_id']}}
        self.document = {'format': 'pokerattack-evidence-v1', 'source_sha': self.manifest['source_sha'],
                         'workflow': {'repository': self.manifest['workflow']['repository'], 'run_id': 20,
                                      'run_attempt': 2, 'run_url': 'https://github.com/henryfaulkner/ChefKnifeStudios.PokerAttack/actions/runs/20'},
                         'digest': self.lease['digest'], 'migration_digest': self.manifest['migration']['digest'],
                         'config_sha256': self.lease['config_sha256'], 'client_source_sha': 'e' * 40,
                         'client_archive_sha256': 'f' * 64, 'verifier_id': 'd' * 32, 'verified_unix': self.now,
                         'checks': dict.fromkeys(('https', 'served_client', 'signalr', 'multiplayer', 'solo',
                                                 'private_message_isolation'), True), 'artifact_references': ['protected-job:20/2']}
        for module, name, value in ((storage, 'STATE', self.root / 'state'), (evidence, 'CONFIG', self.root / 'etc'),
                                    (evidence.time, 'time', lambda: self.now),
                                    (evidence.status_module, 'status', lambda _: copy.deepcopy(self.live))):
            mocked = patch.object(module, name, value)
            mocked.start()
            self.addCleanup(mocked.stop)

    def raw(self, document=None):
        return json.dumps(document or self.document).encode()

    def import_valid(self):
        return evidence.import_evidence('20', '2', self.raw())['evidence_sha256']

    def test_bootstrap_proof_must_match_recorded_published_client(self):
        self.lease['bootstrap']=True
        storage.write(self.base/'lease',self.lease)
        with self.assertRaises(ValueError):self.import_valid()
        published={'source_sha':self.document['client_source_sha'],'archive_sha256':self.document['client_archive_sha256']}
        self.lease['bootstrap_publications']={'candidate':{'packet':{'client':published}}}
        self.lease['bootstrap_publication_intent']={'unresolved':True}
        storage.write(self.base/'lease',self.lease)
        with self.assertRaises(ValueError):self.import_valid()
        del self.lease['bootstrap_publication_intent']
        published['archive_sha256']='1'*64;storage.write(self.base/'lease',self.lease)
        with self.assertRaises(ValueError):self.import_valid()
        published['archive_sha256']=self.document['client_archive_sha256'];storage.write(self.base/'lease',self.lease)
        self.assertEqual(len(self.import_valid()),64)

    def legacy(self, arguments, **kwargs):
        self.assertEqual(arguments, [evidence.LEGACY, 'promote', 'production', self.lease['digest'], self.imported])
        # Simulate the actual legacy deploy-lock/validator handshake. Acquiring
        # both here catches a nested controller lock deadlock.
        with storage.deployment_lock(), patch.dict(os.environ, kwargs['env']):
            evidence.legacy_guard(self.lease['digest'], self.lease['config_sha256'], self.imported)
            self.live['pairs']['known-good'] = self.live['pairs']['active']
            self.live['pairs']['pending'] = None
        return SimpleNamespace(returncode=0)

    def test_valid_import_hash_exact_bytes_permissions_and_separate_promotion(self):
        self.imported = self.import_valid()
        path = self.root / 'etc/production/verification' / (self.imported + '.json')
        self.assertEqual(path.read_bytes(), self.raw())
        self.assertEqual(path.stat().st_mode & 0o777, 0o600)
        self.assertEqual(path.stat().st_uid, 0)
        self.assertEqual(self.imported, self.import_valid())
        self.assertIsNotNone(self.live['pairs']['pending'])
        with patch.object(evidence.subprocess, 'run', side_effect=self.legacy):
            result = evidence.promote(self.lease['digest'], self.imported, '20', '2')
        self.assertEqual(result['phase'], 'promoted')
        self.assertTrue(storage.read(self.base / 'admission/state.json', 0o644)['active'])
        self.assertEqual(storage.read(self.release / 'release-manifest.json'), self.manifest)
        with patch.object(evidence.subprocess, 'run', side_effect=AssertionError('No repeat mutation')):
            self.assertTrue(evidence.promote(self.lease['digest'], self.imported, '20', '2')['idempotent'])

    def test_wrong_identity_client_shape_failed_checks_and_extra_path_rejected(self):
        for field, value in (('digest', 'sha256:' + '0' * 64), ('migration_digest', 'sha256:' + '0' * 64),
                              ('config_sha256', '0' * 64), ('source_sha', '0' * 40), ('verifier_id', '0' * 32),
                              ('client_source_sha', '../client'), ('client_archive_sha256', '../archive'),
                              ('path', '/tmp/evidence'), ('verified_unix', True)):
            candidate = copy.deepcopy(self.document)
            candidate[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                evidence.import_evidence('20', '2', self.raw(candidate))
        for check in self.document['checks']:
            candidate = copy.deepcopy(self.document)
            candidate['checks'][check] = False
            with self.subTest(check=check), self.assertRaises(ValueError):
                evidence.import_evidence('20', '2', self.raw(candidate))
        self.assertNotIn('evidence_sha256', storage.read(self.base / 'lease'))

    def test_duplicate_nonfinite_oversize_and_wrong_execution_rejected(self):
        for raw in (b'{"format":1,"format":2}', b'{"value":NaN}', b' ' * 65537, b'[]'):
            with self.assertRaises((ValueError, TypeError)):
                evidence.import_evidence('20', '2', raw)
        for run, attempt in (('21', '2'), ('20', '1')):
            with self.assertRaises(ValueError):
                evidence.import_evidence(run, attempt, self.raw())
        self.document['workflow']['run_attempt'] = 1
        with self.assertRaises(ValueError):
            self.import_valid()

    def test_freshness_verifier_window_and_runtime_binding(self):
        original = copy.deepcopy(self.document)
        for timestamp in (self.now + 1, self.now - 1800, self.now - 101):
            self.document['verified_unix'] = timestamp
            with self.subTest(timestamp=timestamp), self.assertRaises(ValueError):
                self.import_valid()
        self.document = original
        for fault in ('source_revision', 'image_id', 'pending', 'transaction', 'ready', 'manifest'):
            old = copy.deepcopy(self.live)
            if fault in ('source_revision', 'image_id'): self.live['runtime'][fault] = 'substituted'
            if fault == 'pending': self.live['pairs']['pending'] = None
            if fault == 'transaction': self.live['transaction'] = True
            if fault == 'ready': self.live['readiness']['ready'] = False
            if fault == 'manifest':
                changed = copy.deepcopy(self.lease)
                changed['manifest']['source_sha'] = '0' * 40
                storage.write(self.base / 'lease', changed)
            with self.subTest(fault=fault), self.assertRaises(ValueError):
                self.import_valid()
            self.live = old
            storage.write(self.base / 'lease', self.lease)

    def test_symlink_path_and_changed_evidence_cannot_replace_proof(self):
        digest = hashlib.sha256(self.raw()).hexdigest()
        path = self.root / 'etc/production/verification' / (digest + '.json')
        path.symlink_to(self.base / 'lease')
        with self.assertRaises((ValueError, OSError)):
            self.import_valid()
        path.unlink()
        self.imported = self.import_valid()
        self.document['client_source_sha'] = '0' * 40
        with self.assertRaises(ValueError):
            self.import_valid()
        altered = storage.read(path)
        altered['client_archive_sha256'] = '0' * 64
        storage.write(path, altered)
        with self.assertRaises(ValueError):
            evidence.promote(self.lease['digest'], self.imported, '20', '2')

    def test_stale_promotion_does_not_mutate_or_refresh_evidence(self):
        digest = self.import_valid()
        self.now += 1800
        with patch.object(evidence.subprocess, 'run', side_effect=AssertionError('No mutation')):
            with self.assertRaises(ValueError):
                evidence.promote(self.lease['digest'], digest, '20', '2')
        self.assertEqual(storage.read(self.base / 'lease')['phase'], 'pending')

    def test_legacy_requires_controller_context_even_for_valid_import(self):
        digest = self.import_valid()
        with patch.dict(os.environ, {'POKERATTACK_RELEASE_RUN': '20', 'POKERATTACK_RELEASE_ATTEMPT': '2'}):
            with self.assertRaises(ValueError):
                evidence.legacy_guard(self.lease['digest'], self.lease['config_sha256'], digest)
        spec = importlib.util.spec_from_file_location('evidence_config_test', Path(__file__).with_name('validate-config.py'))
        validator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(validator)
        with patch.object(validator.sys, 'argv', ['validator', 'verification', 'production', '/tmp/proof', digest,
                                                self.lease['digest'], self.lease['config_sha256']]):
            with self.assertRaises(ValueError):
                validator.main()

    def test_failed_or_lost_promotion_remains_closed_and_can_resume_established_pair(self):
        self.imported = self.import_valid()
        def lost(*args, **kwargs):
            self.legacy(*args, **kwargs)
            raise evidence.subprocess.TimeoutExpired(args[0], 90)
        with patch.object(evidence.subprocess, 'run', side_effect=lost):
            with self.assertRaises(ValueError):
                evidence.promote(self.lease['digest'], self.imported, '20', '2')
        self.assertEqual(storage.read(self.base / 'lease')['phase'], 'promotion-unknown')
        self.assertTrue(storage.read(self.base / 'admission/state.json', 0o644)['active'])
        release_lease.resume('20', '2', '3')
        resumed = storage.read(self.base / 'lease')
        self.assertEqual(resumed['phase'], 'promoted-needs-verification')
        self.assertNotIn('evidence_sha256', resumed)
        self.assertEqual(resumed['manifest'], self.manifest)

    def test_reviewed_promoted_reverification_never_redeploys_or_rotates_previous(self):
        changed = copy.deepcopy(self.lease)
        changed.update(phase='promoted-needs-verification', previously_promoted=True)
        storage.write(self.base / 'lease', changed)
        self.live['pairs']['known-good'] = self.live['pairs']['active']
        self.live['pairs']['pending'] = None
        digest = self.import_valid()
        with patch.object(evidence.subprocess, 'run', side_effect=AssertionError('No redeploy')):
            self.assertEqual(evidence.promote(self.lease['digest'], digest, '20', '2')['phase'], 'promoted')

    def test_host_schema_examples_match(self):
        example = json.loads((Path(__file__).parents[1] / 'release/evidence.example.json').read_text())
        host_manifests.evidence(example)


if __name__ == '__main__':
    unittest.main(verbosity=2)
