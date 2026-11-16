"""Security invariants across actual workflow documents, not synthetic strings."""
import copy
from pathlib import Path
import re
import unittest

import yaml


class WorkflowLoader(yaml.SafeLoader):
    pass


# GitHub uses YAML 1.2 boolean names; PyYAML's default YAML 1.1 treats `on` as True.
WorkflowLoader.yaml_implicit_resolvers = copy.deepcopy(yaml.SafeLoader.yaml_implicit_resolvers)
for letter, resolvers in WorkflowLoader.yaml_implicit_resolvers.items():
    WorkflowLoader.yaml_implicit_resolvers[letter] = [(tag, pattern) for tag, pattern in resolvers if tag != 'tag:yaml.org,2002:bool']
WorkflowLoader.add_implicit_resolver('tag:yaml.org,2002:bool', re.compile(r'^(?:true|false|True|False|TRUE|FALSE)$'), list('tTfF'))


class WorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = Path(__file__).parents[2]
        def document(name):
            path = cls.root / '.github/workflows' / name
            if not path.is_file():
                # Bootstrap publishes only build CI before K02; reviewed delivery
                # definitions remain inert templates until K03 installs them.
                path = cls.root / 'deploy/release/workflow-templates' / name
            return yaml.load(path.read_text(), Loader=WorkflowLoader)
        cls.client = document('client.yml')
        cls.server = document('server.yml')

    def test_server_checks_cover_retained_baseline_and_builds_have_no_production_authority(self):
        self.assertEqual(self.server['concurrency'], self.client['concurrency'])
        self.assertEqual(self.server['on']['push']['branches'], ['main'])
        self.assertIn('pull_request', self.server['on'])
        self.assertIn('workflow_dispatch', self.server['on'])
        isolated = self.server['jobs']['isolated']
        self.assertNotIn('environment', isolated)
        self.assertNotIn('secrets.', str(isolated))
        self.assertNotIn('write', str(isolated.get('permissions', {})))
        commands = str(isolated['steps'])
        for required in ('Server.BL.Tests', 'Server.WebAPI.Tests', 'test:guards', 'prepare-client-artifact.test.mjs',
                         'prepare-production-client.test.mjs', 'test_*.py', 'check-local-containers.py',
                         'check-release-gameplay.py', 'check-generated-evidence.py', 'SOURCE_REVISION=$GITHUB_SHA'):
            self.assertIn(required, commands)
        self.assertNotIn('az login', commands)
        self.assertNotIn('docker push', commands)
        validation = self.server['jobs']['validation']
        self.assertEqual(validation['name'], 'Server validation')
        self.assertEqual(validation['if'], 'always()')
        self.assertEqual(validation['needs'], ['changes', 'isolated'])
        deploy = self.server['jobs']['deploy']
        self.assertEqual(deploy['environment'], 'server-linode')
        for guard in ("github.ref == 'refs/heads/main'", "vars.DEPLOYMENT_AUTOMATION_ENABLED == 'true'",
                      "github.repository == 'henryfaulkner/ChefKnifeStudios.PokerAttack'"):
            self.assertIn(guard, deploy['if'])

    def test_server_publish_downloads_tested_artifact_and_has_no_deployment_key(self):
        publish = self.server['jobs']['publish']
        self.assertEqual(publish['environment'], 'server-acr')
        self.assertEqual(publish['permissions'], {'contents': 'read', 'actions': 'read', 'id-token': 'write'})
        self.assertNotIn('secrets.', str(publish))
        self.assertNotIn('docker build', str(publish))
        download = next(step for step in publish['steps'] if step.get('uses', '').startswith('actions/download-artifact@'))
        self.assertEqual(download['with']['artifact-ids'], '${{ needs.isolated.outputs.image_artifact_id }}')
        self.assertIn('server_images.py publish', str(publish))

    def test_reset_review_job_has_only_delivery_authority_and_sanitized_finalizer(self):
        jobs = self.server['jobs']
        self.assertEqual(jobs['resolve']['environment'], 'server-status')
        self.assertNotIn('LINODE_DEPLOY_PRIVATE_KEY', str(jobs['resolve']))
        self.assertNotIn('LINODE_EVIDENCE_PRIVATE_KEY', str(jobs['resolve']))
        self.assertEqual(jobs['deploy']['environment'], 'server-linode')
        for forbidden in ('LINODE_EVIDENCE_PRIVATE_KEY', 'LINODE_PROMOTION_PRIVATE_KEY', 'azure/login', 'id-token'):
            self.assertNotIn(forbidden, str(jobs['deploy']))
        self.assertIn('resolve', jobs['deploy']['needs'])
        for step in jobs['deploy']['steps']:
            if 'always()' in step.get('if', ''):
                self.assertTrue(step.get('uses', '').startswith('actions/upload-artifact@'))

    def test_verification_and_promotion_have_separate_keys_and_fresh_review_dependencies(self):
        verify = self.server['jobs']['verify']; promote = self.server['jobs']['promote']
        self.assertEqual(verify['environment'], 'server-verification')
        self.assertEqual(promote['environment'], 'server-promotion')
        for forbidden in ('LINODE_DEPLOY_PRIVATE_KEY', 'LINODE_PROMOTION_PRIVATE_KEY'):
            self.assertNotIn(forbidden, str(verify))
        for forbidden in ('LINODE_DEPLOY_PRIVATE_KEY', 'LINODE_EVIDENCE_PRIVATE_KEY'):
            self.assertNotIn(forbidden, str(promote))
        self.assertIn("needs.verify.result == 'success'", promote['if'])
        self.assertIn('github.run_attempt > 1', verify['if'])
        self.assertNotIn('server_deliver.py', str(verify))
        self.assertNotIn('server_deliver.py', str(promote))
        steps = verify['steps']
        preparation = next(i for i,step in enumerate(steps) if step.get('name') == 'Install browser runtime before issuing short-lived admission')
        credential = next(i for i,step in enumerate(steps) if 'server_verify.py' in step.get('run',''))
        self.assertLess(preparation, credential)
        upload = next(step for step in steps if step.get('id') == 'proof')
        self.assertNotIn('RUNNER_TEMP', str(upload))
        self.assertEqual(upload['with']['path'].splitlines(), [
            'artifacts/server-verification/evidence.json','artifacts/server-verification/promotion-review.json'])

    def test_pr_main_dispatch_and_shared_non_canceling_serialization(self):
        triggers = self.client['on']
        self.assertIn('pull_request', triggers)
        self.assertEqual(triggers['push']['branches'], ['main'])
        self.assertIn('workflow_dispatch', triggers)
        self.assertNotIn('paths', triggers['push'])
        self.assertNotIn('paths', triggers['pull_request'] or {})
        self.assertEqual(self.client['concurrency'], {'group': 'pokerattack-production-release', 'cancel-in-progress': False})
        self.assertEqual(self.client['permissions'], {'contents': 'read'})

    def test_production_gate_does_not_trust_mutable_pr_change_detection_output(self):
        job = self.client['jobs']['deploy']
        self.assertEqual(job['environment'], 'client-swa')
        for clause in ("github.ref == 'refs/heads/main'", "github.repository == 'henryfaulkner/ChefKnifeStudios.PokerAttack'",
                       "github.event_name == 'push'", "github.event_name == 'workflow_dispatch'",
                       "vars.DEPLOYMENT_AUTOMATION_ENABLED == 'true'", "needs.validation.result == 'success'"):
            self.assertIn(clause, job['if'])

    def test_validation_and_build_jobs_have_no_deployment_secrets_or_write_authority(self):
        for name, job in self.client['jobs'].items():
            if name == 'deploy': continue
            with self.subTest(job=name):
                self.assertNotIn('environment', job)
                self.assertNotIn('secrets.', str(job))
                self.assertNotIn('write', str(job.get('permissions', {})))
        validation = self.client['jobs']['validation']
        self.assertEqual(validation['name'], 'Client validation')
        self.assertEqual(validation['if'], 'always()')
        self.assertIn('checks', validation['needs'])
        self.assertIn('package', validation['needs'])

    def test_actions_are_commit_pinned_and_artifacts_not_rebuilt_after_upload(self):
        for name, job in self.client['jobs'].items():
            for step in job['steps']:
                if 'uses' not in step: continue
                self.assertRegex(step['uses'], r'^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[a-f0-9]{40}$')
                if step['uses'].startswith('actions/checkout@'):
                    self.assertIs(step['with']['persist-credentials'], False)
        steps = self.client['jobs']['package']['steps']
        upload = next(i for i, step in enumerate(steps) if step.get('id') == 'upload')
        self.assertEqual(steps[upload]['with']['retention-days'], 30)
        self.assertNotIn('dotnet publish', str(steps[upload + 1:]))
        self.assertIn('steps.upload.outputs.artifact-id', str(steps[upload + 1:]))

    def test_prebuilt_swa_contract_and_commit_require_successful_browser_verification(self):
        steps = self.client['jobs']['deploy']['steps']
        upload = next(step for step in steps if step.get('id') == 'publish')
        self.assertIs(upload['with']['skip_app_build'], True)
        self.assertIs(upload['with']['skip_api_build'], True)
        self.assertEqual(upload['with']['api_location'], '')
        self.assertEqual(upload['with']['output_location'], '')
        self.assertEqual(upload['with']['app_location'], 'artifacts/client-release/client')
        self.assertIn('upload_needed', upload['if'])
        verify = next(i for i, step in enumerate(steps) if step.get('run') == 'node tests/release/client.cjs')
        commit = next(i for i, step in enumerate(steps) if step.get('run') == 'python deploy/release/client_publish.py commit')
        self.assertGreater(commit, verify)
        self.assertNotIn('always()', steps[commit].get('if', ''))
        self.assertNotIn('secrets.', str(steps[verify]))
        for forbidden in ('LINODE_DEPLOY_PRIVATE_KEY', 'LINODE_EVIDENCE_PRIVATE_KEY', 'LINODE_PROMOTION_PRIVATE_KEY'):
            self.assertNotIn(forbidden, str(self.client['jobs']['deploy']))

    def test_restore_selects_accepted_bytes_and_preserves_failed_attempt_result(self):
        steps = self.client['jobs']['deploy']['steps']
        restoration = next(step for step in steps if step.get('id') == 'restore')
        self.assertIn('restore_upload_needed', restoration['if'])
        self.assertEqual(restoration['with']['app_location'], 'artifacts/client-release/recovery-client')
        self.assertIs(restoration['with']['skip_app_build'], True)
        self.assertIs(restoration['with']['skip_api_build'], True)
        recovery = next(step for step in steps if step.get('id') == 'recovery')
        self.assertEqual(recovery['if'], "steps.verify.outcome == 'failure'")
        self.assertNotIn('LINODE_CLIENT_LEASE_PRIVATE_KEY', str(recovery))
        final = next(step for step in steps if step.get('name') == 'Record truthful overall result')
        self.assertEqual(final['if'], 'always()')
        self.assertIn('exit 1', final['run'])
        for mutation in ('python ', 'node ', 'uses:', 'ssh ', 'az '):
            self.assertNotIn(mutation, final['run'])
        for step in steps:
            if step.get('uses', '').startswith('actions/download-artifact@'):
                self.assertEqual(step['if'], "inputs.operation != 'restore'")


if __name__ == '__main__': unittest.main(verbosity=2)
