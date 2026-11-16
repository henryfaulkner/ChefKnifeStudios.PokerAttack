"""Run in socket-free disposable root Linux; all credentials are test fixtures."""
import copy
import json
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

import secure_state as storage
import release_verifier as verifier


class VerifierTests(unittest.TestCase):
    def setUp(self):
        parent = Path('/release-verifier-tests')
        parent.mkdir(exist_ok=True, mode=0o700)
        self.root = parent / uuid.uuid4().hex
        self.base = self.root / 'production'
        (self.base / 'admission').mkdir(parents=True, mode=0o755)
        self.addCleanup(shutil.rmtree, self.root)
        state_patch = patch.object(storage, 'STATE', self.root)
        state_patch.start()
        self.addCleanup(state_patch.stop)
        self.lease = {'kind': 'server', 'phase': 'pending', 'run_id': 123, 'run_attempt': 1,
                      'digest': 'sha256:' + 'a' * 64, 'config_sha256': 'b' * 64, 'source_sha': 'c' * 40}
        storage.write(self.base / 'lease', self.lease)
        admission = {**self.lease, 'active': True}
        storage.write(self.base / 'admission/state.json', admission, 0o644)
        pair = {'digest': self.lease['digest'], 'config_sha256': self.lease['config_sha256']}
        self.live = {'consistent': True, 'transaction': False, 'readiness': {'ready': True},
                     'pairs': {'active': pair, 'pending': pair}, 'runtime': {'source_revision': self.lease['source_sha']}}
        status_patch = patch.object(verifier.status_module, 'status', side_effect=lambda _: copy.deepcopy(self.live))
        status_patch.start()
        self.addCleanup(status_patch.stop)
        self.request = {'players': [{'id': str(uuid.uuid4()), 'name': f'Release-123-1-{i}'} for i in range(3)]}

    def test_issuance_persists_only_hash_and_never_renews(self):
        credential = verifier.issue('123', '1', self.request)
        secret = credential['credential'].split('.')[-1]
        raw = (self.base / 'admission/state.json').read_text() + (self.base / 'lease').read_text()
        self.assertFalse(secret in raw)
        before = storage.read(self.base / 'admission/state.json', 0o644)
        with self.assertRaises(ValueError):
            verifier.issue('123', '1', self.request)
        self.assertEqual(before, storage.read(self.base / 'admission/state.json', 0o644))
        self.assertEqual((self.base / 'lease').stat().st_mode & 0o777, 0o600)
        self.assertEqual((self.base / 'admission/state.json').stat().st_mode & 0o777, 0o644)
        self.assertLessEqual(before['verifier']['expires_unix'] - before['verifier']['issued_unix'], 1800)

    def test_wrong_run_pending_source_and_interrupted_state_refused(self):
        with self.assertRaises(ValueError):
            verifier.issue('124', '1', self.request)
        for key in ('consistent', 'transaction', 'ready', 'source', 'pending'):
            original = copy.deepcopy(self.live)
            if key == 'consistent': self.live['consistent'] = False
            if key == 'transaction': self.live['transaction'] = True
            if key == 'ready': self.live['readiness']['ready'] = False
            if key == 'source': self.live['runtime']['source_revision'] = 'd' * 40
            if key == 'pending': self.live['pairs']['pending'] = None
            with self.subTest(fault=key), self.assertRaises(ValueError):
                verifier.issue('123', '1', self.request)
            self.live = original
        self.assertFalse(storage.read(self.base / 'lease').get('verifier_issued', False))

    def test_player_scope_and_no_path_or_cleanup_authority(self):
        for request in ({'players': self.request['players'], 'cleanup': True}, {'players': []},
                        {'players': [{'id': p['id'], 'name': 'normal-user'} for p in self.request['players']]},
                        {'players': [self.request['players'][0]] * 3}):
            with self.assertRaises(ValueError):
                verifier.issue('123', '1', request)

    def test_revocation_is_idempotent_and_cannot_clear_maintenance(self):
        verifier.issue('123', '1', self.request)
        with self.assertRaises(ValueError):
            verifier.revoke('123', '2')
        verifier.revoke('123', '1')
        verifier.revoke('123', '1')
        admission = storage.read(self.base / 'admission/state.json', 0o644)
        self.assertTrue(admission['active'])
        self.assertTrue(admission['verifier']['revoked'])
        self.assertTrue(storage.read(self.base / 'lease')['verifier_revoked'])
        with self.assertRaises(ValueError):
            verifier.issue('123', '1', self.request)

    def test_unsafe_state_and_symlink_are_rejected(self):
        admission = self.base / 'admission/state.json'
        admission.chmod(0o666)
        with self.assertRaises(ValueError):
            verifier.issue('123', '1', self.request)
        admission.unlink()
        admission.symlink_to(self.base / 'lease')
        with self.assertRaises(OSError):
            verifier.issue('123', '1', self.request)
        with self.assertRaises(ValueError):
            storage.write(admission, {}, 0o644)

    def test_revocation_before_issuance_is_terminal_for_this_attempt(self):
        verifier.revoke('123', '1')
        with self.assertRaises(ValueError):
            verifier.issue('123', '1', self.request)

    def test_promoted_resume_requires_a_new_verification_context(self):
        data = storage.read(self.base / 'lease')
        data['phase'] = 'promoted-needs-verification'
        storage.write(self.base / 'lease', data)
        self.live['pairs']['known-good'] = self.live['pairs']['active']
        self.live['pairs']['pending'] = None
        result = verifier.issue('123', '1', self.request)
        self.assertEqual(len(result['verifier_id']), 32)
        self.assertFalse(storage.read(self.base / 'lease')['verifier_revoked'])


if __name__ == '__main__':
    unittest.main(verbosity=2)
