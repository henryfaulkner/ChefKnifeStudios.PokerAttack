"""Both pre-sudo and root dispatch use the same strict capability contract."""
import importlib.util
from pathlib import Path
import unittest
import subprocess
import os

from release_commands import GRAMMAR, authorize, parse_ssh

spec = importlib.util.spec_from_file_location('release_control', Path(__file__).with_name('release-control.py'))
control = importlib.util.module_from_spec(spec)
spec.loader.exec_module(control)

EXAMPLES = {
    'status': 'status production',
    'rollback-target': 'rollback-target production',
    'begin-release': 'begin-release production 123 1',
    'begin-rollback': 'begin-rollback production 123 1',
    'deploy': 'deploy production sha256:' + 'a' * 64 + ' ' + 'b' * 64 + ' 123 1',
    'rollback': 'rollback production 123 1',
    'resume-release': 'resume-release production 123 1 2',
    'resume-verification': 'resume-verification production 123 1 2',
    'verifier-credential': 'verifier-credential production 123 1',
    'import-evidence': 'import-evidence production 123 1',
    'revoke-verifier': 'revoke-verifier production 123 1',
    'promote': 'promote production sha256:' + 'a' * 64 + ' ' + 'b' * 64 + ' 123 1',
    'finish-release': 'finish-release production 123 1',
    'begin-client': 'begin-client production 123 1',
    'finish-client': 'finish-client production 123 1',
    'resume-client': 'resume-client production 123 1 2',
    'begin-client-recovery': 'begin-client-recovery production 124 1',
}


class CommandTests(unittest.TestCase):
    def test_every_role_action_combination_at_both_boundaries(self):
        for role in GRAMMAR:
            for action, text in EXAMPLES.items():
                with self.subTest(role=role, action=action):
                    handlers = {action: lambda *args: 'authorized'}
                    if action in GRAMMAR[role]:
                        arguments = parse_ssh(role, text)
                        self.assertEqual(control.dispatch(role, arguments, handlers), 'authorized')
                    else:
                        with self.assertRaises(ValueError):
                            parse_ssh(role, text)
                        with self.assertRaises(ValueError):
                            control.dispatch(role, text.split(' '), handlers)

    def test_shell_whitespace_extra_and_pilot_targets(self):
        for role in GRAMMAR:
            for text in ('status pilot', 'status production; id', 'status production extra', 'status  production',
                         'status\tproduction', 'status production\n', '\nstatus production', 'status production ',
                         '$(id)', '`id`', 'sh', '', 'status production && true', 'status production\x00',
                         'status production > /tmp/pwn', 'status productioné'):
                with self.subTest(role=role, command=text), self.assertRaises(ValueError):
                    parse_ssh(role, text)

    def test_run_bounds_and_identity_grammar(self):
        valid = EXAMPLES['deploy']
        for text in (valid.replace(' 123 ', ' 0 '), valid.replace(' 123 ', ' ' + '1' * 21 + ' '),
                     valid.replace(' 123 ', ' -1 '), valid.replace(' 123 ', ' 01 '),
                     valid[:-1] + '0', valid[:-1] + '1' * 6, valid.replace('a' * 64, 'a' * 63),
                     valid.replace('a' * 64, 'A' * 64), valid.replace('b' * 64, '../config')):
            with self.subTest(command=text), self.assertRaises(ValueError):
                parse_ssh('deployment', text)

    def test_unknown_role_and_unactivated_handlers_fail_closed(self):
        with self.assertRaises(ValueError):
            parse_ssh('root', 'status production')
        with self.assertRaises(ValueError):
            control.dispatch('deployment', EXAMPLES['begin-release'].split(' '), {})

    def test_root_helpers_fix_roles_and_sudoers_do_not_grant_interpreters(self):
        root = Path(__file__).parent
        for role in ('deployment', 'evidence', 'promotion', 'client-lease'):
            raw = (root / ('pokerattack-' + role)).read_bytes()
            self.assertNotIn(b'\r', raw)
            self.assertIn(f'/usr/bin/python3 -I /opt/pokerattack/release-control.py {role} "$@"'.encode(), raw)
        rules = (root / 'control.sudoers').read_text()
        for forbidden in ('SETENV:', '/bin/bash', '/bin/sh', '/usr/bin/python', 'docker', 'ALL=(ALL)'):
            self.assertNotIn(forbidden, '\n'.join(line for line in rules.splitlines() if not line.startswith('#')))
        self.assertNotIn('pokerattack-promotion', next(line for line in rules.splitlines() if line.startswith('pa-deploy ')))

    def test_legacy_combined_key_cannot_target_production(self):
        path = Path(__file__).with_name('pokerattack-ssh-deploy')
        for action in ('status production', EXAMPLES['deploy'], EXAMPLES['promote'], 'rollback production'):
            result = subprocess.run(['bash', str(path)], env={**os.environ, 'SSH_ORIGINAL_COMMAND': action}, capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b'capability-separated', result.stderr)


if __name__ == '__main__':
    unittest.main(verbosity=2)
