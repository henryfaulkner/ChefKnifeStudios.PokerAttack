"""Local C05 mechanics; no Docker socket, SSH, database or production URLs."""
import copy
import contextlib
import json
import os
from pathlib import Path
import shutil
import subprocess
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid

import secure_state as storage
import release_lease as lease
SCHEMA_READY = lease.schema_ready
APPLICATION_READY = lease.application_ready


class LeaseTests(unittest.TestCase):
    def setUp(self):
        parent = Path('/release-lease-tests')
        parent.mkdir(exist_ok=True, mode=0o700)
        self.root = parent / uuid.uuid4().hex
        self.state = self.root / 'state'
        self.base = self.state / 'production'
        for name in ('admission', 'completed', 'interrupted'):
            (self.base / name).mkdir(parents=True, mode=0o700)
        self.config = self.root / 'etc'
        (self.config / 'production').mkdir(parents=True, mode=0o700)
        self.addCleanup(shutil.rmtree, self.root)
        self.manifest = json.loads((Path(__file__).parents[1] / 'release/server-manifest.example.json').read_text())
        self.now = 1794757000
        self.packet = {'manifest': self.manifest, 'acknowledgment': {
            'action': 'deploy', 'digest': self.manifest['api']['digest'], 'config_sha256': self.manifest['config_sha256'],
            'source_sha': self.manifest['source_sha'], 'run_id': 1, 'run_attempt': 1,
            'approved_unix': self.now, 'expires_unix': self.now + 1800, 'drain_seconds': 0, 'policy': 'reset-at-deadline'}}
        self.old = {'digest': 'sha256:' + '1' * 64, 'config_sha256': '2' * 64}
        self.live = {'consistent': True, 'transaction': False, 'readiness': {'ready': True},
                     'pairs': {'active': self.old, 'known-good': self.old, 'pending': None, 'previous-good': None}}
        self.observed = {'activeGames': 0, 'lobbies': 0, 'soloGames': 0}
        for module, field, value in [(storage, 'STATE', self.state), (lease, 'CONFIG', self.config),
                                      (lease.time, 'time', lambda: self.now),
                                      (lease.status_module, 'status', lambda _: copy.deepcopy(self.live)),
                                      (lease, 'counts', lambda: self.observed),
                                      (lease, 'schema_ready', lambda _: None),
                                      (lease, 'application_ready', lambda: None),
                                      (lease.validator, 'config', lambda *args: None)]:
            mocked = patch.object(module, field, value)
            mocked.start()
            self.addCleanup(mocked.stop)
        storage.write(self.base / 'admission/state.json', {'active': False}, 0o644)

    def begin(self):
        return lease.begin('1', '1', self.packet)

    def test_api_must_observe_the_open_gate_before_normal_begin(self):
        # An unreadable admission directory fails closed inside the API even
        # when root can read active=false. Detect that mismatch before closing
        # maintenance or permitting replacement, not after verifier admission.
        for maintenance in (True,None):
            response=SimpleNamespace(status=200,read=lambda _:json.dumps({'capability':1,'maintenance':maintenance}).encode())
            with patch.object(lease.urllib.request,'urlopen',return_value=contextlib.nullcontext(response)),patch.object(lease,'application_ready',side_effect=APPLICATION_READY):
                with self.subTest(maintenance=maintenance),self.assertRaises(ValueError):self.begin()
            self.assertFalse(self.closed());self.assertFalse((self.base/'lease').exists())
        response=SimpleNamespace(status=200,read=lambda _:b'{"capability":1,"maintenance":false}')
        with patch.object(lease.urllib.request,'urlopen',return_value=contextlib.nullcontext(response)),patch.object(lease,'application_ready',side_effect=APPLICATION_READY):
            self.assertEqual(self.begin()['phase'],'drained')

    def test_unavailable_counts_only_allow_fresh_acknowledged_unready_rollback(self):
        self.packet['acknowledgment']['action']='rollback'
        self.packet['recovery_from']=None
        target={'digest':self.manifest['api']['digest'],'config_sha256':self.manifest['config_sha256']}
        self.live['pairs']['previous-good']=target
        self.live['readiness']['ready']=False
        with patch.object(lease,'counts',side_effect=ValueError('API down')):
            result=lease.begin('1','1',self.packet,rollback=True)
        self.assertEqual(result,{'phase':'reset-approved','session_observation':None})
        self.assertTrue(self.closed())

    def test_ready_rollback_does_not_treat_unavailable_counts_as_empty(self):
        self.packet['acknowledgment']['action']='rollback'
        self.packet['recovery_from']=None
        self.live['pairs']['previous-good']={'digest':self.manifest['api']['digest'],'config_sha256':self.manifest['config_sha256']}
        with patch.object(lease,'counts',side_effect=ValueError('Observer failed')):
            with self.assertRaises(ValueError):lease.begin('1','1',self.packet,rollback=True)
        self.assertEqual(storage.read(self.base/'lease')['phase'],'drain-failed')
        self.assertTrue(self.closed())

    def closed(self):
        return storage.read(self.base / 'admission/state.json', 0o644)['active']

    def test_begin_binds_approval_and_idempotent_retry_never_refreshes_it(self):
        self.assertEqual(self.begin()['phase'], 'drained')
        self.assertTrue(self.closed())
        before = storage.read(self.base / 'lease')
        self.now += 2000
        self.assertTrue(self.begin()['idempotent'])
        self.assertEqual(before, storage.read(self.base / 'lease'))
        with self.assertRaises(ValueError):
            lease.deploy(self.manifest['api']['digest'], self.manifest['config_sha256'], '1', '1')
        self.assertTrue(self.closed())

    def test_new_run_cannot_steal_or_reapprove_existing_lease(self):
        self.begin()
        other = copy.deepcopy(self.packet)
        other['acknowledgment']['run_id'] = 2
        other['manifest']['workflow']['run_id'] = 2
        other['manifest']['workflow']['run_url'] = other['manifest']['workflow']['run_url'][:-1] + '2'
        with self.assertRaises(ValueError):
            lease.begin('2', '1', other)
        changed = copy.deepcopy(self.packet)
        changed['acknowledgment']['expires_unix'] -= 1
        with self.assertRaises(ValueError):
            lease.begin('1', '1', changed)

    def test_stale_wrong_pair_and_schema_failure_do_not_enter_maintenance(self):
        original = copy.deepcopy(self.packet)
        for field, value in [('approved_unix', self.now - 1801), ('digest', 'sha256:' + '0' * 64),
                              ('source_sha', '0' * 40), ('run_attempt', 2), ('drain_seconds', 301)]:
            self.packet = copy.deepcopy(original)
            self.packet['acknowledgment'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.begin()
            self.assertFalse(self.closed())
        self.packet = original
        with patch.object(lease, 'schema_ready', side_effect=ValueError('schema prerequisite')):
            with self.assertRaises(ValueError):
                self.begin()
        self.assertFalse(self.closed())

    def test_wrong_config_is_rejected_before_schema_lease_or_maintenance(self):
        with patch.object(lease.validator, 'config', side_effect=ValueError('wrong config')), patch.object(lease, 'schema_ready') as schema:
            with self.assertRaises(ValueError): self.begin()
            schema.assert_not_called()
        self.assertFalse(self.closed())
        self.assertFalse((self.base / 'lease').exists())

    def test_schema_query_is_fixed_readonly_and_preserves_recovery_prerequisite(self):
        release = self.base / 'releases/recovery'
        release.mkdir(parents=True, mode=0o700)
        (self.base / 'known-good').symlink_to(release, target_is_directory=True)
        prior = copy.deepcopy(self.manifest)
        prior['schema']['required_migrations'] = ['20251008022524_Init_Data']
        storage.write(release / 'release-manifest.json', prior)
        candidate = copy.deepcopy(self.manifest)
        candidate['schema']['required_migrations'] = ['20251008234907_NoUsers']
        response = SimpleNamespace(stdout=b'20251008234907_NoUsers\n')
        with patch.object(lease.subprocess, 'run', return_value=response) as call:
            with self.assertRaises(ValueError): SCHEMA_READY(candidate)
            args = call.call_args.args[0]
            self.assertIn('BEGIN READ ONLY;', args[-1])
            self.assertIn('statement_timeout = 3000', args[-1])
            self.assertNotIn('20251008234907', args[-1], 'Caller data became SQL')
            response.stdout += b'20251008022524_Init_Data\n'
            SCHEMA_READY(candidate)

    def test_bounded_drain_records_explicit_reset_and_observer_failure_stays_closed(self):
        self.observed['activeGames'] = 1
        self.assertEqual(self.begin()['phase'], 'reset-approved')
        self.assertTrue(self.closed())
        self.assertFalse((self.config / 'production/maintenance').exists(), 'Not provisioned before replacement')
        (self.base / 'lease').unlink()
        with patch.object(lease, 'counts', side_effect=ValueError('observer unavailable')):
            with self.assertRaises(ValueError):
                self.begin()
        self.assertTrue(self.closed())
        self.assertEqual(storage.read(self.base / 'lease')['phase'], 'drain-failed')

    def legacy(self, arguments, **kwargs):
        if arguments[0] == 'docker':
            return SimpleNamespace(stdout=json.dumps({'Config': {'Labels': {'org.opencontainers.image.revision': self.manifest['source_sha'],
                                  'com.chefknifestudios.pokerattack.release-capability': '1'}},
                                  'Os': 'linux', 'Architecture': 'amd64', 'Id': self.manifest['api']['local_image_id']}).encode())
        self.assertEqual(arguments, [lease.LEGACY, 'deploy', 'production', self.manifest['api']['digest'], self.manifest['config_sha256']])
        # If deploy held the same lock across its nested call this guard fails.
        with patch.dict(os.environ, kwargs['env']):
            lease.prepare_legacy('deploy', self.manifest['api']['digest'], self.manifest['config_sha256'])
        self.live['pairs']['active'] = {'digest': self.manifest['api']['digest'], 'config_sha256': self.manifest['config_sha256']}
        self.live['pairs']['pending'] = self.live['pairs']['active']
        return SimpleNamespace(returncode=0)

    def test_replacement_provisions_fresh_marker_without_nested_lock_and_retry_does_not_restart(self):
        self.begin()
        with patch.object(lease.subprocess, 'run', side_effect=self.legacy) as command:
            result = lease.deploy(self.manifest['api']['digest'], self.manifest['config_sha256'], '1', '1')
            self.assertEqual(result['phase'], 'pending')
            count = command.call_count
            self.assertTrue(lease.deploy(self.manifest['api']['digest'], self.manifest['config_sha256'], '1', '1')['idempotent'])
            self.assertEqual(command.call_count, count)
        self.assertEqual((self.config / 'production/maintenance').read_text(), 'sessions-reset-approved\n')
        self.assertTrue(self.closed())

    def test_unknown_response_and_failed_release_remain_closed_and_failed(self):
        self.begin()
        with patch.object(lease.subprocess, 'run', side_effect=subprocess.TimeoutExpired('legacy', 900)):
            with self.assertRaises(ValueError):
                lease.deploy(self.manifest['api']['digest'], self.manifest['config_sha256'], '1', '1')
        self.assertEqual(storage.read(self.base / 'lease')['phase'], 'unknown')
        self.assertTrue(self.closed())
        with self.assertRaises(ValueError):
            lease.deploy(self.manifest['api']['digest'], self.manifest['config_sha256'], '1', '1')

    def test_finish_requires_matching_promotion_revocation_and_closed_sessions(self):
        self.begin()
        with self.assertRaises(ValueError):
            lease.finish('1', '1')
        data = storage.read(self.base / 'lease')
        data['phase'], data['verifier_revoked'] = 'promoted', True
        storage.write(self.base / 'lease', data)
        pair = {'digest': data['digest'], 'config_sha256': data['config_sha256']}
        self.live['pairs']['active'] = self.live['pairs']['known-good'] = pair
        self.observed['soloGames'] = 1
        with self.assertRaises(ValueError):
            lease.finish('1', '1')
        self.assertTrue(self.closed())
        self.observed['soloGames'] = 0
        self.assertEqual(lease.finish('1', '1')['phase'], 'reopened')
        self.assertFalse(self.closed())
        self.assertFalse((self.base / 'lease').exists())
        self.assertTrue((self.base / 'completed/1-1.json').exists())
        self.assertTrue(lease.finish('1', '1')['idempotent'])

    def test_host_control_lock_refuses_concurrent_begin(self):
        with storage.control_lock():
            with self.assertRaises(BlockingIOError):
                self.begin()
        with storage.deployment_lock():
            with self.assertRaises(BlockingIOError):
                self.begin()
        self.assertFalse(self.closed())

    def test_verification_resume_cannot_race_an_existing_legacy_operation(self):
        self.begin()
        data = storage.read(self.base / 'lease')
        pair = {'digest': data['digest'], 'config_sha256': data['config_sha256']}
        self.live['pairs']['active'] = self.live['pairs']['pending'] = pair
        data['phase'] = 'pending'; storage.write(self.base / 'lease', data)
        with storage.deployment_lock():
            with self.assertRaises(BlockingIOError): lease.resume('1', '1', '2')
        self.assertEqual(storage.read(self.base / 'lease')['run_attempt'], 1)
        self.assertEqual(lease.resume('1', '1', '2')['phase'], 'pending')

    def test_root_bootstrap_introduces_capabilities_without_requiring_old_server_features(self):
        with patch.object(lease, 'application_ready', side_effect=ValueError('legacy baseline')):
            with self.assertRaises(ValueError):
                self.begin()
            self.assertFalse(self.closed())
            packet = copy.deepcopy(self.packet)
            packet['acknowledgment']['action'] = 'bootstrap'
            with patch('bootstrap_recovery.snapshot',return_value={'fixture_only':True}):
                self.assertEqual(lease.begin('1', '1', packet, bootstrap=True)['phase'], 'bootstrap-maintenance-pending')
        self.assertTrue(storage.read(self.base / 'lease')['bootstrap'])

    def test_explicit_rollback_handoff_requires_new_fresh_review_of_the_recorded_pair(self):
        self.begin()
        previous = storage.read(self.base / 'lease')
        candidate = {'digest': self.manifest['api']['digest'], 'config_sha256': self.manifest['config_sha256']}
        self.live['pairs']['active'] = self.live['pairs']['pending'] = candidate
        target = copy.deepcopy(self.manifest)
        target['api']['digest'], target['config_sha256'] = self.old['digest'], self.old['config_sha256']
        packet = {'manifest': target, 'acknowledgment': {**self.packet['acknowledgment'], 'action': 'rollback',
                  'digest': target['api']['digest'], 'config_sha256': target['config_sha256'], 'run_id': 2},
                  'recovery_from': {key: previous[key] for key in ('run_id', 'run_attempt', 'digest', 'config_sha256')}}
        bad = copy.deepcopy(packet)
        bad['acknowledgment']['expires_unix'] = self.now
        with self.assertRaises(ValueError):
            lease.begin('2', '1', bad, rollback=True)
        bad = copy.deepcopy(packet)
        bad['recovery_from']['run_id'] = 99
        with self.assertRaises(ValueError):
            lease.begin('2', '1', bad, rollback=True)
        self.assertEqual(storage.read(self.base / 'lease')['run_id'], 1)
        self.assertEqual(lease.begin('2', '1', packet, rollback=True)['phase'], 'drained')
        self.manifest = target
        def replacement(arguments, **kwargs):
            if arguments[0] == 'docker':
                return self.legacy(arguments, **kwargs)
            self.assertEqual(arguments, [lease.LEGACY, 'rollback', 'production'])
            with patch.dict(os.environ, kwargs['env']):
                lease.prepare_legacy('rollback', target['api']['digest'], target['config_sha256'])
            self.live['pairs']['active'] = self.live['pairs']['pending'] = self.old
            return SimpleNamespace(returncode=0)
        with patch.object(lease.subprocess, 'run', side_effect=replacement):
            self.assertEqual(lease.rollback('2', '1')['phase'], 'pending')
        self.assertTrue(self.closed())
        self.assertEqual(storage.read(self.base / 'lease')['acknowledgment']['action'], 'rollback')

    def test_resume_revokes_old_evidence_without_restart_or_timestamp_forgery(self):
        self.begin()
        data = storage.read(self.base / 'lease')
        data.update(phase='deploying', evidence_sha256='f' * 64, verifier_issued=True)
        storage.write(self.base / 'lease', data)
        active = {'digest': self.manifest['api']['digest'], 'config_sha256': self.manifest['config_sha256']}
        self.live['pairs']['active'] = self.live['pairs']['pending'] = active
        admission = storage.read(self.base / 'admission/state.json', 0o644)
        admission['verifier'] = {'revoked': False}
        storage.write(self.base / 'admission/state.json', admission, 0o644)
        self.now += 2000  # Reset approval has expired; resume must not replace API.
        with patch.object(lease.subprocess, 'run', side_effect=AssertionError('No process during resume')):
            result = lease.resume('1', '1', '2')
        self.assertFalse(result['reset_approval_refreshed'])
        resumed = storage.read(self.base / 'lease')
        self.assertEqual(resumed['acknowledgment'], data['acknowledgment'])
        self.assertEqual(resumed['manifest'], data['manifest'])
        self.assertEqual(resumed['run_attempt'], 2)
        self.assertNotIn('evidence_sha256', resumed)
        self.assertTrue(storage.read(self.base / 'admission/state.json', 0o644)['verifier']['revoked'])
        with self.assertRaises(ValueError):
            lease.resume('1', '1', '2')  # Unknown response requires status inspection.

    def test_reopening_failure_restores_closed_gate_and_preserves_lease(self):
        self.begin()
        data = storage.read(self.base / 'lease')
        data.update(phase='promoted', verifier_revoked=True)
        storage.write(self.base / 'lease', data)
        pair = {'digest': data['digest'], 'config_sha256': data['config_sha256']}
        self.live['pairs']['active'] = self.live['pairs']['known-good'] = pair
        original = Path.unlink
        def failure(path, *args, **kwargs):
            if path == self.base / 'lease':
                raise OSError('fixture finalization failure')
            return original(path, *args, **kwargs)
        with patch.object(Path, 'unlink', failure):
            with self.assertRaises(OSError):
                lease.finish('1', '1')
        self.assertTrue(self.closed())
        self.assertTrue((self.base / 'lease').exists())

    def test_resume_after_promotion_requires_new_verification_and_review(self):
        self.begin()
        data = storage.read(self.base / 'lease')
        data.update(phase='promoted', verifier_revoked=True)
        storage.write(self.base / 'lease', data)
        pair = {'digest': data['digest'], 'config_sha256': data['config_sha256']}
        self.live['pairs']['active'] = self.live['pairs']['known-good'] = pair
        result = lease.resume('1', '1', '2')
        self.assertEqual(result['phase'], 'promoted-needs-verification')
        with self.assertRaises(ValueError):
            lease.finish('1', '2')

    def test_partial_handoff_cannot_reopen_using_old_promotion(self):
        self.begin()
        data = storage.read(self.base / 'lease')
        data.update(phase='promoted', verifier_revoked=True)
        storage.write(self.base / 'lease', data)
        pair = {'digest': data['digest'], 'config_sha256': data['config_sha256']}
        self.live['pairs']['active'] = self.live['pairs']['known-good'] = pair
        admission = storage.read(self.base / 'admission/state.json', 0o644)
        admission['run_attempt'] = 2
        storage.write(self.base / 'admission/state.json', admission, 0o644)
        with self.assertRaises(ValueError):
            lease.finish('1', '1')
        self.assertTrue(self.closed())

    def test_pre_stop_failure_recovers_current_known_pair_with_fresh_explicit_acknowledgment(self):
        self.begin()
        prior = storage.read(self.base / 'lease')
        prior['phase'] = 'failed'
        storage.write(self.base / 'lease', prior)
        target = copy.deepcopy(self.manifest)
        target['api']['digest'], target['config_sha256'] = self.old['digest'], self.old['config_sha256']
        packet = {'manifest': target, 'acknowledgment': {**self.packet['acknowledgment'], 'action': 'rollback',
                  'digest': target['api']['digest'], 'config_sha256': target['config_sha256'], 'run_id': 2},
                  'recovery_from': {key: prior[key] for key in ('run_id', 'run_attempt', 'digest', 'config_sha256')}}
        lease.begin('2', '1', packet, rollback=True)
        self.assertEqual(storage.read(self.base / 'lease')['replacement_action'], 'deploy')
        self.manifest = target
        with patch.object(lease.subprocess, 'run', side_effect=self.legacy):
            self.assertEqual(lease.rollback('2', '1')['phase'], 'pending')
        self.assertTrue(self.closed())


if __name__ == '__main__':
    unittest.main(verbosity=2)
