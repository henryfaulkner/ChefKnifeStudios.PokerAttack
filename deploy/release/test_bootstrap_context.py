import copy
import unittest

from bootstrap_context import Context, decision
from client_publish import trusted_context
from client_artifacts import identity_summary
import test_server_verify as fixtures


class BootstrapContextTests(unittest.TestCase):
    def setUp(self):
        fixture = fixtures.VerifyTests(); fixture.setUp(); self.addCleanup(fixture.doCleanups)
        self.manifest = fixture.manifest
        accepted = fixture.catalog.pointers()[0]['current']
        self.client = copy.deepcopy(fixture.catalog.accepted_manifest(accepted['run_id'], accepted['run_attempt'],
                                    accepted['artifact_id'], accepted['archive_sha256']))
        self.client['workflow'] = copy.deepcopy(self.manifest['workflow'])
        self.env = {**fixture.env, 'DEPLOYMENT_AUTOMATION_ENABLED': 'false', 'GITHUB_EVENT_NAME': 'workflow_dispatch'}
        self.run = {'head_sha': self.manifest['source_sha'], 'head_branch': 'main', 'event': 'workflow_dispatch',
                    'status': 'completed', 'conclusion': 'success', 'run_attempt': 1,
                    'repository': {'full_name': self.manifest['workflow']['repository']},
                    'path': '.github/workflows/migration-ci.yml'}
        self.live = {'format': 'pokerattack-bootstrap-client-status-v1', 'client': identity_summary(self.client),
                     'source_sha': self.manifest['source_sha'], 'pair': {'digest': self.manifest['api']['digest'],
                     'config_sha256': self.manifest['config_sha256']}}
        self.context = Context(self.manifest['source_sha'], self.manifest['workflow'], self.client,
                               lambda *args: self.live, self.api, self.manifest)

    def api(self, path):
        return {'object': {'sha': self.manifest['source_sha']}} if path == 'git/ref/heads/main' else self.run

    def test_actual_main_build_and_root_publication_without_faking_rollout(self):
        self.context.build(self.env, fresh_main=True)
        self.context.candidate(self.env)
        with self.assertRaises(ValueError): trusted_context(self.env)

    def test_untrusted_branch_failed_build_or_different_root_pair_rejected(self):
        for key, value in (('head_branch', 'feature'), ('head_sha', 'a'*40), ('conclusion', 'failure'),
                           ('path', '.github/workflows/server.yml'), ('event', 'pull_request'), ('run_attempt', 2)):
            saved = copy.deepcopy(self.run); self.run[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError): self.context.build(self.env)
            self.run = saved
        self.live['pair']['config_sha256'] = '0'*64
        with self.assertRaises(ValueError): self.context.candidate(self.env)

    def test_matching_fresh_human_decision_is_distinct_from_github_review(self):
        record = {'format': 'pokerattack-root-release-decision-v1', 'action': 'promote',
                  'comment': 'PROMOTE actual-hash 42/1', 'received_unix': 1050}
        proof = decision(record, 'promote', record['comment'], 1000, now=1100)
        self.assertEqual(proof['mechanism'], 'root-operator decision')
        self.assertNotIn('reviewer', proof)
        for bad in ({**record, 'comment': 'different'}, {**record, 'action': 'reset'},
                    {**record, 'received_unix': 999}, {**record, 'received_unix': 1200}):
            with self.assertRaises(ValueError): decision(bad, 'promote', record['comment'], 1000, now=1100)
        with self.assertRaises(ValueError): decision(record, 'promote', record['comment'], 1000, now=2800)


if __name__ == '__main__': unittest.main()
