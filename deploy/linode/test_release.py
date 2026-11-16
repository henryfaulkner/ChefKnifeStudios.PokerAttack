"""Run in a disposable root Linux container. Docker/curl are mocks, never the host socket."""
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import unittest
import uuid

SOURCE = Path(__file__).resolve().parent


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.root = Path('/release-tests') / uuid.uuid4().hex
        for name in ('etc/pilot/configs', 'etc/pilot/verification', 'state', 'opt', 'bin'):
            (self.root / name).mkdir(parents=True, mode=0o700, exist_ok=True)
        script = (SOURCE / 'pokerattack-deploy').read_text()
        script = script.replace('root=/etc/pokerattack', f'root={self.root}/etc')
        script = script.replace('state=/var/lib/pokerattack', f'state={self.root}/state')
        script = script.replace('install_dir=/opt/pokerattack', f'install_dir={self.root}/opt')
        script = script.replace('export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin', f'export PATH={self.root}/bin:/usr/local/bin:/usr/bin:/bin')
        self.script = self.root / 'deploy'
        self.script.write_text(script)
        shutil.copy(SOURCE / 'validate-config.py', self.root / 'opt/validate-config.py')
        status_script = (SOURCE / 'release-status.py').read_text()
        status_script = status_script.replace("STATE = Path('/var/lib/pokerattack')", f"STATE = Path('{self.root}/state')")
        status_script = status_script.replace("CONFIG = Path('/etc/pokerattack')", f"CONFIG = Path('{self.root}/etc')")
        (self.root / 'opt/release-status.py').write_text(status_script)
        (self.root / 'opt/compose.yaml').write_text('services: {}\n')
        self.model = self.root / 'model.json'
        self.model.write_text(json.dumps({'image': '', 'running': False, 'log': []}))
        mock = '''#!/usr/local/bin/python3
import json, os, sys
from pathlib import Path
p=Path(os.environ['MOCK_MODEL']); m=json.loads(p.read_text()); args=sys.argv[1:]; tool=Path(sys.argv[0]).name
m['log'].append([tool,*args]); result=0; output=''
if tool=='docker':
 if args[0]=='pull': result=int(os.environ.get('MOCK_PULL_FAIL','0'))
 elif args[:2]==['image','inspect']: output=args[-1].split('@')[-1]
 elif args[0]=='inspect':
  bad=os.environ.get('MOCK_LIMIT_DIGEST','!') in m['image']
  cpu=os.environ.get('MOCK_NANO_CPUS','250000000') if bad else '250000000'
  memory=os.environ.get('MOCK_MEMORY','536870912') if bad else '536870912'
  output=m['image'].split('@')[-1]+' '+str(m['running']).lower()+' '+cpu+' '+memory
 elif args[0]=='compose':
  offset=args.index('-f')+2; operation=args[offset]; env=Path(args[args.index('--env-file')+1]).read_text(); image=env.splitlines()[0].split('=',1)[1]
  if operation=='ps': output='fixture-container' if m['image'] else ''
  elif operation=='stop': m['running']=False
  elif operation=='up': m['image']=image; m['running']=True
else:
 result=int(not m['running'] or os.environ.get('MOCK_BAD_DIGEST','!') in m['image'] or os.environ.get('MOCK_ALL_UNHEALTHY')=='1')
p.write_text(json.dumps(m)); print(output); sys.exit(result)
'''
        for name in ('docker', 'curl'):
            path = self.root / 'bin' / name
            path.write_text(mock)
            path.chmod(0o755)
        (self.root / 'bin/sleep').write_text('#!/bin/sh\nexit 0\n')
        (self.root / 'bin/sleep').chmod(0o755)
        self.env = {**os.environ, 'MOCK_MODEL': str(self.model)}
        raw = ('ASPNETCORE_ENVIRONMENT=Staging\nTesting__EnableEndpoints=true\n'
               'AllowedHosts=pilot-api.playpokerattack.com;localhost;127.0.0.1;[::1]\n'
               'Hosting__AllowedOrigins=http://localhost:5173\n'
               'ConnectionStrings__PokerAttackDB=Host=127.0.0.1;Database=pokerattack_pilot;'
               'Username=pokerattack_pilot_runtime;Password=' + 'x' * 40 + ';SSL Mode=Disable\n').encode()
        self.config = hashlib.sha256(raw).hexdigest()
        path = self.root / 'etc/pilot/configs' / (self.config + '.env')
        path.write_bytes(raw)
        path.chmod(0o600)
        self.maintenance()

    def maintenance(self):
        path = self.root / 'etc/pilot/maintenance'
        path.write_text('sessions-reset-approved\n')
        path.chmod(0o600)

    def run_release(self, *args, ok=True, **env):
        result = subprocess.run(['bash', str(self.script), *args], env={**self.env, **env}, capture_output=True, text=True)
        self.assertEqual(result.returncode == 0, ok, result.stdout + result.stderr)
        return result

    def deploy(self, digit='a', **kwargs):
        return self.run_release('deploy', 'pilot', 'sha256:' + digit * 64, self.config, **kwargs)

    def promote(self, digit='a', complete=True, ok=True):
        evidence = {'digest': 'sha256:' + digit * 64, 'config_sha256': self.config,
                    'source_sha': 'c' * 40, 'verified_unix': time.time(), 'https': True,
                    'signalr': complete, 'multiplayer': True, 'solo': True, 'private_message_isolation': True}
        raw = json.dumps(evidence).encode()
        identity = hashlib.sha256(raw).hexdigest()
        path = self.root / 'etc/pilot/verification' / (identity + '.json')
        path.write_bytes(raw)
        path.chmod(0o600)
        self.run_release('promote', 'pilot', evidence['digest'], identity, ok=ok)

    def state(self):
        return json.loads(self.model.read_text())

    def test_pending_requires_external_evidence_and_blocks_competing_deploy(self):
        self.deploy()
        self.assertFalse((self.root / 'state/pilot/known-good').exists())
        self.promote(complete=False, ok=False)
        self.deploy('b', ok=False)
        self.promote()
        self.assertTrue((self.root / 'state/pilot/known-good').is_symlink())

    def test_failed_pull_leaves_active_running(self):
        self.deploy(); self.promote()
        before = len(self.state()['log'])
        self.deploy('b', ok=False, MOCK_PULL_FAIL='1')
        state = self.state()
        self.assertTrue(state['running'])
        self.assertIn('sha256:' + 'a' * 64, state['image'])
        self.assertFalse(any('stop' in command for command in state['log'][before:]))

    def test_same_pair_recovery_promotion_preserves_older_distinct_pair(self):
        self.deploy();self.promote()
        original=(self.root/'state/pilot/known-good').resolve()
        self.deploy('b');self.promote('b')
        base=self.root/'state/pilot'
        self.assertEqual((base/'previous-good').resolve(),original)
        # Actual recovery creates a new snapshot of the serving pair. Recreate
        # that pre-promotion state without needing a different fake timestamp.
        repeat=base/'releases/repeated-serving-pair'
        shutil.copytree((base/'known-good').resolve(),repeat)
        (base/'active').unlink();(base/'active').symlink_to(repeat)
        (base/'pending').symlink_to(repeat)
        self.promote('b')
        self.assertEqual((base/'known-good').resolve(),repeat)
        self.assertEqual((base/'previous-good').resolve(),original)

    def test_failed_readiness_recovers_previous_pair_and_returns_failure(self):
        self.deploy(); self.promote()
        self.deploy('b', ok=False, MOCK_BAD_DIGEST='sha256:' + 'b' * 64)
        self.assertTrue(self.state()['running'])
        self.assertIn('sha256:' + 'a' * 64, self.state()['image'])
        self.assertTrue((self.root / 'state/pilot/last-failed-transaction').exists())
        self.assertFalse((self.root / 'state/pilot/transaction').exists())

    def test_failed_recovery_retains_transaction_and_blocks_retry(self):
        self.deploy(); self.promote()
        self.deploy('b', ok=False, MOCK_ALL_UNHEALTHY='1')
        self.assertTrue((self.root / 'state/pilot/transaction').exists())
        diagnostic = json.loads(self.run_release('status', 'pilot').stdout)
        self.assertTrue(diagnostic['transaction'])

    def test_explicit_rollback_restores_pair_but_requires_new_verification(self):
        self.deploy(); self.promote()
        self.deploy('b'); self.promote('b')
        self.run_release('rollback', 'pilot')
        self.assertIn('sha256:' + 'a' * 64, self.state()['image'])
        self.assertTrue((self.root / 'state/pilot/pending').is_symlink())

    def test_maintenance_config_permissions_and_malformed_commands_fail_closed(self):
        self.run_release('deploy', 'pilot', 'latest', self.config, ok=False)
        path = self.root / 'etc/pilot/maintenance'
        path.unlink()
        self.deploy(ok=False)
        self.assertEqual(self.state()['image'], '')
        self.maintenance()
        config = self.root / 'etc/pilot/configs' / (self.config + '.env')
        config.chmod(0o644)
        self.deploy(ok=False)

    def test_lock_contention(self):
        with (self.root / 'state/deploy.lock').open('w') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.deploy(ok=False)
            self.run_release('status', 'pilot')

    def test_bad_config_is_rejected_before_pull_or_service_mutation(self):
        self.deploy(); self.promote()
        config = self.root / 'etc/pilot/configs' / (self.config + '.env')
        config.write_bytes(config.read_bytes() + b'UNREVIEWED=value\n')
        before = self.state()
        self.deploy('b', ok=False)
        self.assertEqual(before, self.state())

    def test_wrong_cpu_or_memory_restores_exact_pair_and_failure_is_retained(self):
        self.deploy(); self.promote()
        pair = (self.root / 'state/pilot/active').resolve()
        original = {name: (pair / name).read_bytes() for name in ('image', 'digest', 'config-id', 'app.env', 'release.env')}
        for limit in ({'MOCK_NANO_CPUS': '0'}, {'MOCK_MEMORY': '1073741824'}):
            with self.subTest(limit=limit):
                # Fixed snapshot names use seconds; separate failed candidates.
                for candidate in (self.root / 'state/pilot/releases').iterdir():
                    if candidate != pair:
                        shutil.rmtree(candidate)
                self.deploy('b', ok=False, MOCK_LIMIT_DIGEST='sha256:' + 'b' * 64, **limit)
                self.assertEqual((self.root / 'state/pilot/active').resolve(), pair)
                self.assertEqual(original, {name: (pair / name).read_bytes() for name in original})
                self.assertTrue(self.state()['running'])
                self.assertFalse((self.root / 'state/pilot/pending').exists())
                self.assertTrue((self.root / 'state/pilot/last-failed-transaction').exists())

    def test_interrupted_transaction_blocks_mutation_but_not_diagnostics(self):
        self.deploy(); self.promote()
        transaction = self.root / 'state/pilot/transaction'
        transaction.write_text('fixture interrupted outcome\n')
        before = self.state()
        self.deploy('b', ok=False)
        self.assertEqual(before, self.state())
        diagnostic = json.loads(self.run_release('status', 'pilot').stdout)
        self.assertTrue(diagnostic['transaction'])
        self.assertEqual(transaction.read_text(), 'fixture interrupted outcome\n')

    def test_all_lifecycle_commands_are_confined_to_api_and_never_schema_or_shared_services(self):
        self.deploy(); self.promote()
        self.deploy('b', ok=False, MOCK_BAD_DIGEST='sha256:' + 'b' * 64)
        for command in self.state()['log']:
            self.assertNotIn(command[1] if len(command) > 1 else '', ('system', 'prune', 'volume', 'restart', 'exec', 'run'))
            if command[:2] == ['docker', 'compose']:
                self.assertEqual(command[command.index('--project-name') + 1], 'pokerattack-pilot')
                operation = command[command.index('-f') + 2:]
                self.assertIn(operation[0], ('config', 'ps', 'stop', 'up'))
                if operation[0] == 'up': self.assertEqual(operation, ['up', '-d', '--no-deps', '--pull', 'never', 'api'])
                if operation[0] == 'stop': self.assertEqual(operation, ['stop', 'api'])

    def test_forced_ssh_command_refuses_shell_and_extra_arguments(self):
        for command in ('status pilot; id', 'deploy pilot latest abc', 'status pilot extra', '$(id)', ''):
            result = subprocess.run(['bash', str(SOURCE / 'pokerattack-ssh-deploy')],
                                    env={**os.environ, 'SSH_ORIGINAL_COMMAND': command}, capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b'Only the documented', result.stderr)

    def test_production_rejects_legacy_marker_without_bound_controller_approval(self):
        for name in ('host_manifests.py', 'release_lease.py', 'release-legacy-guard.py', 'release_verifier.py', 'secure_state.py'):
            shutil.copy(SOURCE / name, self.root / 'opt' / name)
        folder = self.root / 'etc/production/configs'
        folder.mkdir(parents=True, mode=0o700)
        raw = (self.root / 'etc/pilot/configs' / (self.config + '.env')).read_text()
        for old, new in [('ASPNETCORE_ENVIRONMENT=Staging', 'ASPNETCORE_ENVIRONMENT=Production'),
                         ('Testing__EnableEndpoints=true', 'Testing__EnableEndpoints=false'),
                         ('pilot-api.playpokerattack.com', 'api.playpokerattack.com'),
                         ('Hosting__AllowedOrigins=http://localhost:5173', 'Hosting__AllowedOrigins=https://playpokerattack.com'),
                         ('Database=pokerattack_pilot;', 'Database=expensetrackerdb;'),
                         ('Username=pokerattack_pilot_runtime;', 'Username=pokerattack_runtime;')]:
            raw = raw.replace(old, new)
        config = hashlib.sha256(raw.encode()).hexdigest()
        path = folder / (config + '.env')
        path.write_text(raw)
        path.chmod(0o600)
        marker = folder.parent / 'maintenance'
        marker.write_text('sessions-reset-approved\n')
        marker.chmod(0o600)
        self.run_release('deploy', 'production', 'sha256:' + 'a' * 64, config, ok=False,
                         POKERATTACK_RELEASE_RUN='', POKERATTACK_RELEASE_ATTEMPT='')
        self.assertFalse(any('stop' in call or 'up' in call for call in self.state()['log']))
        self.assertEqual(self.state()['image'], '')


if __name__ == '__main__':
    unittest.main(verbosity=2)
