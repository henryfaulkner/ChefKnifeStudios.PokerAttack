"""Status fixtures run without any host Docker socket or production connection."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import os
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('release_status', Path(__file__).with_name('release-status.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class StatusTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.state, self.config = root / 'state', root / 'etc'
        self.base = self.state / 'production'
        self.release = self.base / 'releases' / 'fixture'
        self.release.mkdir(parents=True)
        raw = b'PRIVATE-CONFIG-MARKER=secret\n'
        self.config_hash = hashlib.sha256(raw).hexdigest()
        self.digest = 'sha256:' + 'b' * 64
        self.image_id = 'sha256:' + 'c' * 64
        self.image = 'chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi@' + self.digest
        for name, value in [('image', self.image), ('digest', self.digest), ('config-id', self.config_hash)]:
            (self.release / name).write_text(value)
        (self.release / 'app.env').write_bytes(raw)
        configs = self.config / 'production' / 'configs'
        configs.mkdir(parents=True)
        (configs / (self.config_hash + '.env')).write_bytes(raw)
        (self.base / 'active').symlink_to(self.release, target_is_directory=True)
        (self.base / 'known-good').symlink_to(self.release, target_is_directory=True)
        self.docker = {'Id': '1' * 64, 'Image': self.image_id, 'Config': {'Image': self.image,
                       'Env': ['AUTH=PRIVATE-ENV-MARKER']}, 'State': {'Status': 'running'},
                       'HostConfig': {'NanoCpus': 250000000, 'Memory': 536870912}}
        self.ids = ['1' * 64]
        self.healthy = True
        self.sessions = {'activeGames': 1, 'lobbies': 2, 'soloGames': 0, 'private': 'PRIVATE-SESSION-MARKER'}
        for name, value in [('STATE', self.state), ('CONFIG', self.config), ('command', self.command)]:
            mocked = patch.object(module, name, value)
            mocked.start()
            self.addCleanup(mocked.stop)

    def command(self, *args):
        if args[:3] == ('docker', 'ps', '-aq'):
            return '\n'.join(self.ids)
        if args[:2] == ('docker', 'inspect'):
            return json.dumps([self.docker])
        if args[:3] == ('docker', 'image', 'inspect'):
            return self.image_id if args[-1] == '{{.Id}}' else json.dumps({'org.opencontainers.image.revision': 'a' * 40, 'other': 'PRIVATE-LABEL-MARKER'})
        if args[0] == 'curl':
            if args[-1].endswith('/_ops/status'):
                return json.dumps(self.sessions)
            if self.healthy:
                return 'Healthy'
            raise subprocess.CalledProcessError(22, 'curl')
        raise AssertionError('Unexpected status operation')

    def test_healthy_identity_and_no_healthcheck_are_distinct(self):
        result = module.status('production')
        self.assertTrue(result['consistent'])
        self.assertTrue(result['readiness']['ready'])
        self.assertNotEqual(result['runtime']['image_id'], result['pairs']['active']['digest'])
        self.assertEqual(result['runtime']['docker_health'], 'not-configured')
        for forbidden in ('PRIVATE-CONFIG', 'PRIVATE-ENV', 'PRIVATE-LABEL'):
            self.assertNotIn(forbidden, json.dumps(result))

    def test_session_observation_is_aggregate_bounded_and_never_invents_zero(self):
        result = module.status('production')
        self.assertEqual(result['sessions'], {'activeGames': 1, 'lobbies': 2, 'soloGames': 0})
        self.assertNotIn('PRIVATE-SESSION', json.dumps(result))
        self.sessions['activeGames'] = True
        result = module.status('production')
        self.assertIsNone(result['sessions'])
        self.assertTrue(result['readiness']['ready'])

    def test_unhealthy_absent_stopped_and_ambiguous_runtime(self):
        self.healthy = False
        self.assertFalse(module.status('production')['readiness']['ready'])
        self.ids = []
        self.assertIsNone(module.status('production')['runtime'])
        self.ids = ['1' * 64]
        self.docker['State']['Status'] = 'exited'
        self.assertFalse(module.status('production')['readiness']['ready'])
        self.ids.append('2' * 64)
        self.assertFalse(module.status('production')['consistent'])

    def test_pending_and_transaction_remain_diagnostic(self):
        (self.base / 'pending').symlink_to(self.release, target_is_directory=True)
        (self.base / 'transaction').write_text('PRIVATE-TRANSACTION-MARKER')
        (self.base / 'lease').write_text(json.dumps({'run_id': 1, 'run_attempt': 1, 'phase': 'pending', 'verifier_secret': 'PRIVATE-TOKEN-MARKER'}))
        result = module.status('production')
        self.assertTrue(result['transaction'])
        self.assertEqual(result['pairs']['pending'], result['pairs']['active'])
        self.assertEqual(result['lease']['phase'], 'pending')
        self.assertNotIn('PRIVATE-', json.dumps(result))

    def test_wrong_runtime_or_changed_config_cannot_report_ready(self):
        self.docker['Image'] = 'sha256:' + 'd' * 64
        self.assertFalse(module.status('production')['readiness']['ready'])
        self.docker['Image'] = self.image_id
        (self.release / 'app.env').write_text('changed')
        self.assertFalse(module.status('production')['readiness']['ready'])
        self.assertFalse(module.status('production')['pairs']['active']['snapshot_config_matches'])

    def test_daemon_failure_and_bad_pointer_fail_closed(self):
        with patch.object(module, 'command', side_effect=subprocess.TimeoutExpired('docker', 6)):
            result = module.status('production')
            self.assertFalse(result['consistent'])
            self.assertFalse(result['readiness']['ready'])
            self.assertEqual(result['errors'], ['runtime-inspection-failed'])
        (self.base / 'pending').symlink_to(self.config, target_is_directory=True)
        self.assertFalse(module.status('production')['consistent'])
        self.assertIsNone(module.status('production')['pairs']['pending'])

    def test_bad_lease_and_admission_are_explicit_unknown(self):
        (self.base / 'lease').write_text('not-json')
        (self.base / 'admission').mkdir()
        (self.base / 'admission/state.json').write_text('{"active":"false"}')
        result = module.status('production')
        self.assertEqual(result['lease'], {'phase': 'unknown'})
        self.assertEqual(result['maintenance'], {'supported': True, 'active': None})
        self.assertFalse(result['readiness']['ready'])

    def test_state_created_during_read_is_not_a_consistent_idle_sample(self):
        original = self.command
        def concurrent(*args):
            if args[0] == 'curl':
                (self.base / 'lease').write_text('{"phase":"draining"}')
            return original(*args)
        with patch.object(module, 'command', concurrent):
            result = module.status('production')
        self.assertFalse(result['consistent'])
        self.assertFalse(result['readiness']['ready'])

    def test_partial_lease_handoff_is_explicitly_inconsistent(self):
        identity = {'kind': 'server', 'phase': 'promoted', 'run_id': 1, 'run_attempt': 1,
                    'digest': self.digest, 'config_sha256': self.config_hash, 'source_sha': 'a' * 40}
        (self.base / 'lease').write_text(json.dumps(identity))
        (self.base / 'admission').mkdir()
        (self.base / 'admission/state.json').write_text(json.dumps({**identity, 'run_attempt': 2, 'active': True}))
        result = module.status('production')
        self.assertFalse(result['consistent'])
        self.assertIn('lease-admission-context-mismatch', result['errors'])
        self.assertEqual(result['maintenance']['run_attempt'], 2)

    def test_read_only_forced_role_rejects_mutation_and_shell(self):
        wrapper = Path(__file__).with_name('pokerattack-ssh-status').read_text()
        marker = Path(self.temp.name) / 'sudo'
        marker.write_text('#!/bin/sh\nprintf \'%s\\n\' "$@"\n')
        marker.chmod(0o755)
        wrapper = wrapper.replace('/usr/bin/sudo', str(marker))
        for value in ('deploy production', 'status pilot', 'status production extra', 'status production; id',
                      'status  production', 'status production\n', '$(id)', ''):
            p = subprocess.run(['bash', '-c', wrapper], env={**os.environ, 'SSH_ORIGINAL_COMMAND': value}, capture_output=True)
            self.assertNotEqual(p.returncode, 0, value)
        p = subprocess.run(['bash', '-c', wrapper], env={**os.environ, 'SSH_ORIGINAL_COMMAND': 'status production'}, capture_output=True, text=True)
        self.assertEqual(p.returncode, 0)
        self.assertEqual(p.stdout.splitlines(), ['-n', '/usr/local/sbin/pokerattack-status'])


if __name__ == '__main__':
    unittest.main(verbosity=2)
