"""Verification resume preserves first-release proof and cannot restart delivery."""
from pathlib import Path
import unittest

from bootstrap_operator import verification_workspace


class ResumeTests(unittest.TestCase):
    def test_original_execution_uses_original_workspace(self):
        self.assertEqual(verification_workspace(Path('release'), 'verify', None, 1, 1), Path('release'))

    def test_real_later_execution_has_separate_proof_promotion_and_finish(self):
        for action in ('verify', 'promote', 'finish'):
            with self.subTest(action=action):
                self.assertEqual(verification_workspace(Path('release'), action, 2, 2, 1),
                                 Path('release/verification-resume-2'))

    def test_rejects_delivery_fake_attempt_replay_and_implicit_old_proof(self):
        for action, requested, actual, built in (
            ('begin', 2, 2, 1), ('deliver', 2, 2, 1), ('candidate', 2, 2, 1),
            ('maintenance', 2, 2, 1), ('accept-client', 2, 2, 1),
            ('verify', 2, 1, 1), ('verify', 1, 1, 1), ('verify', True, 2, 1),
            ('promote', None, 2, 1), ('finish', 3, 2, 1)):
            with self.subTest(action=action, requested=requested), self.assertRaises(ValueError):
                verification_workspace(Path('release'), action, requested, actual, built)


if __name__ == '__main__':
    unittest.main()
