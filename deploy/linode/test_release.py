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
 elif args[0]=='inspect': output=m['image'].split('@')[-1]+' '+str(m['running']).lower()+' 250000000 536870912'
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
        self.run_release('status', 'pilot', ok=False)

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
            self.run_release('status', 'pilot', ok=False)

    def test_forced_ssh_command_refuses_shell_and_extra_arguments(self):
        for command in ('status pilot; id', 'deploy pilot latest abc', 'status pilot extra', '$(id)', ''):
            result = subprocess.run(['bash', str(SOURCE / 'pokerattack-ssh-deploy')],
                                    env={**os.environ, 'SSH_ORIGINAL_COMMAND': command}, capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b'Only the documented', result.stderr)


if __name__ == '__main__':
    unittest.main(verbosity=2)
