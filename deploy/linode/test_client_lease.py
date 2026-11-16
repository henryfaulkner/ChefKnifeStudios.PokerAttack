"""Socket-free production-slot fixtures; never contacts Azure, Docker or a DB."""
import copy
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

import client_lease as client
import release_lease as server
import secure_state as storage


class ClientLeaseTests(unittest.TestCase):
    def setUp(self):
        parent = Path('/client-lease-tests'); parent.mkdir(mode=0o700, exist_ok=True)
        self.root = parent / uuid.uuid4().hex
        self.base = self.root / 'production'
        for name in ('client-completed', 'admission', 'completed', 'interrupted'):
            (self.base / name).mkdir(mode=0o700, parents=True, exist_ok=True)
        self.addCleanup(shutil.rmtree, self.root)
        self.now = 1794757000
        pair = {'digest': 'sha256:' + 'a' * 64, 'config_sha256': 'b' * 64}
        self.live = {'consistent': True, 'transaction': False, 'pairs': {'active': pair, 'known-good': pair,
            'pending': None}, 'readiness': {'ready': True}, 'maintenance': {'active': False}}
        self.identity = {'format': 'pokerattack-client-identity-v1', 'source_sha': 'c' * 40,
            'workflow': {'repository': 'henryfaulkner/ChefKnifeStudios.PokerAttack', 'run_id': 1, 'run_attempt': 1,
                'run_url': 'https://github.com/henryfaulkner/ChefKnifeStudios.PokerAttack/actions/runs/1'},
            'artifact_id': 10, 'archive_sha256': 'd' * 64, 'settings_sha256': 'e' * 64,
            'minimum_server_capability': 1, 'settings_files': {'appsettings.json': 'e' * 64}}
        self.recovery = {**self.identity, 'source_sha': 'f' * 40, 'archive_sha256': 'f' * 64}
        self.packet = {'candidate': self.identity, 'recovery': self.recovery}
        for module, field, value in ((storage, 'STATE', self.root), (client.status_module, 'status', lambda _: copy.deepcopy(self.live)),
                                     (client, 'capability', lambda: 1), (client.time, 'time', lambda: self.now)):
            mocked = patch.object(module, field, value); mocked.start(); self.addCleanup(mocked.stop)
        storage.write(self.base / 'admission/state.json', {'active': False}, 0o644)

    def begin(self): return client.begin('90', '1', self.packet)

    def proof(self, outcome='accepted'):
        return {'outcome': outcome, 'client': self.identity if outcome == 'accepted' else self.recovery,
                'verified_unix': self.now, 'checks': dict.fromkeys(('https', 'served_client', 'settings', 'header',
                                                                  'assets', 'browser', 'signalr'), True)}

    def test_slot_is_idempotent_exclusive_and_never_changes_admission(self):
        self.begin()
        self.assertTrue(self.begin()['idempotent'])
        with self.assertRaises(ValueError): client.begin('91', '1', self.packet)
        changed = copy.deepcopy(self.packet); changed['candidate']['archive_sha256'] = '0' * 64
        with self.assertRaises(ValueError): client.begin('90', '1', changed)
        self.assertEqual(storage.read(self.base / 'admission/state.json', 0o644), {'active': False})

    def test_server_pending_unhealthy_maintenance_or_incompatible_baseline_blocks_publication(self):
        for fault in ('pending', 'ready', 'maintenance', 'transaction', 'consistent', 'compatibility'):
            old = copy.deepcopy(self.live)
            if fault == 'pending': self.live['pairs']['pending'] = self.live['pairs']['active']
            if fault == 'ready': self.live['readiness']['ready'] = False
            if fault == 'maintenance': self.live['maintenance']['active'] = True
            if fault == 'transaction': self.live['transaction'] = True
            if fault == 'consistent': self.live['consistent'] = False
            if fault == 'compatibility': self.identity['minimum_server_capability'] = 2
            with self.subTest(fault=fault), self.assertRaises(ValueError): self.begin()
            self.assertFalse((self.base / 'lease').exists())
            self.live = old; self.identity['minimum_server_capability'] = 1

    def test_client_role_cannot_clear_server_lease_or_maintenance(self):
        storage.write(self.base / 'lease', {'kind': 'server', 'run_id': 90, 'run_attempt': 1, 'phase': 'pending'})
        before = storage.read(self.base / 'lease')
        with self.assertRaises(ValueError): self.begin()
        with self.assertRaises(ValueError): client.finish('90', '1', self.proof())
        self.assertEqual(storage.read(self.base / 'lease'), before)

    def test_finish_requires_fresh_complete_exact_verified_identity_and_frozen_server(self):
        self.begin()
        for fault in ('time', 'check', 'identity', 'server'):
            packet = self.proof(); old = copy.deepcopy(self.live)
            if fault == 'time': packet['verified_unix'] -= 1800
            if fault == 'check': packet['checks']['signalr'] = False
            if fault == 'identity': packet['client'] = self.recovery
            if fault == 'server':
                changed = {'digest': 'sha256:' + '0' * 64, 'config_sha256': 'b' * 64}
                self.live['pairs']['active'] = self.live['pairs']['known-good'] = changed
            with self.subTest(fault=fault), self.assertRaises(ValueError): client.finish('90', '1', packet)
            self.assertTrue((self.base / 'lease').exists())
            self.live = old
        self.assertEqual(client.finish('90', '1', self.proof())['outcome'], 'accepted')
        self.assertFalse((self.base / 'lease').exists())
        self.assertTrue(client.finish('90', '1', self.proof())['idempotent'])

    def test_restoration_finishes_only_predeclared_recovery_and_keeps_result_distinct(self):
        self.begin()
        self.assertEqual(client.finish('90', '1', self.proof('restored'))['outcome'], 'restored')
        completed = storage.read(self.base / 'client-completed/90-1.json')
        self.assertEqual(completed['outcome'], 'restored')
        with self.assertRaises(ValueError): self.begin()

    def test_slot_lock_and_stdin_shape_fail_without_side_effects(self):
        with storage.deployment_lock():
            with self.assertRaises(BlockingIOError): self.begin()
        self.packet['path'] = '/etc/other-app'
        with self.assertRaises(ValueError): self.begin()
        self.assertFalse((self.base / 'lease').exists())

    def test_client_resume_keeps_exact_build_recovery_and_lease_without_claiming_upload_success(self):
        self.begin()
        before = storage.read(self.base / 'lease')
        with self.assertRaises(ValueError): client.resume('91', '1', '2')
        client.resume('90', '1', '2')
        after = storage.read(self.base / 'lease')
        self.assertEqual(after['candidate'], before['candidate'])
        self.assertEqual(after['recovery'], before['recovery'])
        self.assertEqual(after['client_source_run_attempt'], 1)
        self.assertEqual(after['run_attempt'], 2)
        self.assertTrue(client.resume('90', '1', '2')['idempotent'])
        self.assertEqual(after['phase'], 'client-publishing')
        with self.assertRaises(ValueError): client.finish('90', '1', self.proof())
        self.assertEqual(client.finish('90', '2', self.proof())['outcome'], 'accepted')

    def test_server_reset_and_explicit_recovery_cannot_steal_client_slot(self):
        self.begin()
        before = storage.read(self.base / 'lease')
        # Use the already validated server fixture envelope shape; rejection must
        # occur on ownership before any schema check/drain/marker/replacement.
        import json
        manifest = json.loads((Path(__file__).parents[1] / 'release/server-manifest.example.json').read_text())
        ack = {'action': 'rollback', 'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256'],
               'source_sha': manifest['source_sha'], 'run_id': 2, 'run_attempt': 1, 'approved_unix': self.now,
               'expires_unix': self.now + 1800, 'drain_seconds': 0, 'policy': 'reset-at-deadline'}
        packet = {'manifest': manifest, 'acknowledgment': ack, 'recovery_from': {
            key: before[key] for key in ('run_id', 'run_attempt', 'digest', 'config_sha256')}}
        with self.assertRaises(ValueError): server.begin('2', '1', packet, rollback=True)
        self.assertEqual(storage.read(self.base / 'lease'), before)

    def test_explicit_client_recovery_restores_only_reserved_predecessor_and_never_server_slot(self):
        self.begin()
        prior = storage.read(self.base / 'lease')
        packet = {'candidate': self.recovery, 'recovery': self.identity,
                  'recovery_from': {key: prior[key] for key in ('run_id', 'run_attempt', 'client_archive_sha256')}}
        wrong = copy.deepcopy(packet); wrong['candidate'] = self.identity
        with self.assertRaises(ValueError): client.begin('91', '1', wrong, recover=True)
        wrong = copy.deepcopy(packet); wrong['recovery_from']['run_id'] = 92
        with self.assertRaises(ValueError): client.begin('91', '1', wrong, recover=True)
        self.assertEqual(client.begin('91', '1', packet, recover=True)['phase'], 'client-publishing')
        self.assertEqual(storage.read(self.base / 'lease')['source_sha'], self.recovery['source_sha'])
        self.assertTrue((self.base / 'interrupted/client-90-1-recovered-by-91-1.json').is_file())
        self.assertEqual(storage.read(self.base / 'admission/state.json', 0o644), {'active': False})


if __name__ == '__main__': unittest.main(verbosity=2)
