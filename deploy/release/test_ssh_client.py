import json
import os
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from ssh_client import Transport, UnknownOutcome, transport


class TransportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='pokerattack-transport-test-'); self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.key, self.hosts = root / 'identity', root / 'known_hosts'
        self.key.write_text('FAKE FIXTURE KEY'); self.key.chmod(0o600)
        self.hosts.write_text('FAKE FIXTURE TRUST')

    def test_strict_nonroot_forced_command_and_no_secret_environment_or_shell(self):
        client = Transport('client-lease', self.key, self.hosts)
        with patch.dict(os.environ, {'LINODE_CLIENT_LEASE_PRIVATE_KEY': 'DO-NOT-PASS-OR-PRINT'}), patch('ssh_client.subprocess.run', return_value=SimpleNamespace(returncode=0, stdout=b'{"phase":"client-publishing"}')) as invoke:
            self.assertEqual(client.call('begin-client production 90 1', {'candidate': 'fixture'})['phase'], 'client-publishing')
            args, kwargs = invoke.call_args.args[0], invoke.call_args.kwargs
            self.assertIn('StrictHostKeyChecking=yes', args)
            self.assertIn('IdentitiesOnly=yes', args)
            self.assertIn('ConnectionAttempts=1', args)
            self.assertIn('pa-client@170.187.142.115', args)
            self.assertNotIn('shell', kwargs)
            self.assertNotIn('LINODE_CLIENT_LEASE_PRIVATE_KEY', kwargs['env'])
            self.assertEqual(args[-1], 'begin-client production 90 1')
        with self.assertRaises(ValueError): client.call('deploy production sha256:' + 'a' * 64 + ' ' + 'b' * 64 + ' 90 1')
        with self.assertRaises(ValueError): client.call('status production; id')

    def test_lost_malformed_or_timeout_response_is_never_retried(self):
        client = Transport('client-lease', self.key, self.hosts)
        for result in (SimpleNamespace(returncode=255, stdout=b'{"phase":"client-publishing"}'),
                       SimpleNamespace(returncode=0, stdout=b'invalid'),
                       SimpleNamespace(returncode=0, stdout=b' ' * 65537)):
            with patch('ssh_client.subprocess.run', return_value=result) as invoke:
                with self.assertRaises(UnknownOutcome): client.call('begin-client production 90 1')
                self.assertEqual(invoke.call_count, 1)
        with patch('ssh_client.subprocess.run', side_effect=subprocess.TimeoutExpired('ssh', 90)) as invoke:
            with self.assertRaises(UnknownOutcome): client.call('begin-client production 90 1')
            self.assertEqual(invoke.call_count, 1)

    def test_private_temp_material_is_removed_without_echo_and_stdin_is_bounded(self):
        with patch.dict(os.environ, {'RUNNER_TEMP': self.temp.name, 'LINODE_STATUS_PRIVATE_KEY': 'fixture-only',
                                    'LINODE_KNOWN_HOSTS': 'fixture-trust'}):
            with transport('status') as client:
                path = client.key
                self.assertTrue(path.is_file())
                with self.assertRaises(ValueError): client.call('status production', {'large': 'x' * 65536})
            self.assertFalse(path.exists())


if __name__ == '__main__': unittest.main(verbosity=2)
