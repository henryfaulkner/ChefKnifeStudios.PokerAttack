"""Production orchestration boundary fixtures; no network/credentials/SWA upload."""
import copy
import datetime
import json
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch

from blob_store import LocalStore
from client_archive import Archive
from client_artifacts import finalize, pack
import client_publish as publish


class Host:
    def __init__(self):
        pair = {'digest': 'sha256:' + 'a' * 64, 'config_sha256': 'b' * 64}
        self.state = {'protocol_version': 1, 'target': 'production', 'consistent': True, 'transaction': False,
            'lease': None, 'pairs': {'active': pair, 'known-good': pair, 'pending': None},
            'maintenance': {'active': False}, 'readiness': {'ready': True}}
        self.calls = []
        self.finish_error_after_clear = False

    def call(self, command, packet=None):
        self.calls.append(command)
        if command == 'status production':
            return {**copy.deepcopy(self.state), 'sampled_utc': datetime.datetime.now(datetime.timezone.utc).isoformat()}
        parts = command.split(' ')
        if parts[0] in ('begin-client', 'begin-client-recovery'):
            candidate, recovery = packet['candidate'], packet['recovery']
            self.state['lease'] = {'kind': 'client', 'run_id': int(parts[2]), 'run_attempt': int(parts[3]),
                'client_archive_sha256': candidate['archive_sha256'], 'source_sha': candidate['source_sha'],
                'artifact_id': candidate['artifact_id'], 'minimum_server_capability': candidate['minimum_server_capability'],
                'client_source_run_id': candidate['workflow']['run_id'], 'client_source_run_attempt': candidate['workflow']['run_attempt']}
            if recovery:
                self.state['lease'].update(client_recovery_run_id=recovery['workflow']['run_id'],
                    client_recovery_run_attempt=recovery['workflow']['run_attempt'], client_recovery_artifact_id=recovery['artifact_id'],
                    client_recovery_archive_sha256=recovery['archive_sha256'])
            return {'phase': 'client-publishing'}
        if parts[0] == 'resume-client':
            self.state['lease']['run_attempt'] = int(parts[4])
            return {'phase': 'client-publishing'}
        if parts[0] == 'finish-client':
            self.state['lease'] = None
            if self.finish_error_after_clear:
                from ssh_client import UnknownOutcome
                raise UnknownOutcome('Injected lost response after completion')
            return {'phase': 'client-finished', 'outcome': packet['outcome']}
        raise AssertionError('Unexpected host authority used')


class ClientPublishTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='pokerattack-client-publish-test-'); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.host = Host()
        self.catalog = Archive(LocalStore(self.root / 'blobs'), lambda *_: True)
        self.old = self.build(2); self.new = self.build(3)
        self.catalog.accept(self.old[0], self.old[1], 90, 1)
        self.env = {'GITHUB_REPOSITORY': publish.REPOSITORY, 'GITHUB_REF': 'refs/heads/main',
            'GITHUB_EVENT_NAME': 'push', 'DEPLOYMENT_AUTOMATION_ENABLED': 'true', 'GITHUB_RUN_ID': '3',
            'GITHUB_RUN_ATTEMPT': '1', 'GITHUB_SHA': 'c' * 40}
        self.env['CLIENT_CANDIDATE_ARTIFACT_ID'] = '30'
        self.workspace = self.root / 'publication'
        self.head = 'c' * 40
        for name, value in (('github', lambda path: {'expired': False, 'workflow_run': {'id': 3, 'head_sha': self.head}}
                            if path.startswith('actions/artifacts/') else {'object': {'sha': self.head}}), ('service_ready', lambda: None),
                            ('observe', lambda manifest: manifest['workflow']['run_id'] == 2)):
            mocked = patch.object(publish, name, value); mocked.start(); self.addCleanup(mocked.stop)

    def build(self, run):
        directory = self.root / f'build-{run}'; directory.mkdir()
        (directory / 'index.html').write_text('<html>fixture</html>')
        (directory / 'appsettings.json').write_text(json.dumps({'AppSettings': {'ExternalApis': [
            {'Name': name, 'BaseUri': 'https://api.playpokerattack.com/'} for name in ('PokerAttackAPI', 'PokerAttackSignalR')]}}))
        (directory / 'staticwebapp.config.json').write_text('{"globalHeaders":{"Blazor-Environment":"prod"}}')
        archive = self.root / f'build-{run}.tar.gz'
        return finalize(pack(directory, archive, 'c' * 40, run, 1), run * 10), archive

    def prepare(self):
        return publish.reserve(self.catalog, self.host, self.host, self.workspace, self.new[0], self.new[1], self.env)

    def result(self):
        return {'checks': dict.fromkeys(('https', 'served_client', 'settings', 'header', 'assets', 'browser', 'signalr'), True),
                'verified_unix': int(time.time()), 'client_source_sha': self.new[0]['source_sha'],
                'client_archive_sha256': self.new[0]['archive_sha256']}

    def test_exact_candidate_and_recovery_reserved_then_real_result_commits_and_finishes(self):
        context = self.prepare()
        self.assertTrue(context['upload_needed'])
        self.assertEqual((self.workspace / 'client/index.html').read_text(), '<html>fixture</html>')
        self.assertTrue((self.workspace / 'recovery.tar.gz').is_file())
        self.assertEqual(publish.commit(self.catalog, self.host, self.host, self.workspace, self.result())['phase'], 'client-finished')
        self.assertIsNone(self.host.state['lease'])
        self.assertEqual(self.catalog.pointers()[0]['current']['run_id'], 3)
        self.assertFalse(any(command.startswith(('deploy ', 'promote ', 'rollback ', 'finish-release ')) for command in self.host.calls))

    def test_stale_or_unresolved_source_or_predecessor_blocks_before_host_or_upload(self):
        for fault in ('main', 'pending', 'maintenance', 'predecessor'):
            self.head = '0' * 40 if fault == 'main' else 'c' * 40
            if fault == 'pending': self.host.state['pairs']['pending'] = self.host.state['pairs']['active']
            if fault == 'maintenance': self.host.state['maintenance']['active'] = True
            with patch.object(publish, 'observe', return_value=fault != 'predecessor'):
                with self.subTest(fault=fault), self.assertRaises(ValueError): self.prepare()
            self.assertFalse(any(command.startswith('begin-client ') for command in self.host.calls))
            self.host.state['pairs']['pending'] = None; self.host.state['maintenance']['active'] = False

    def test_pr_fork_nonmain_and_disabled_contexts_cannot_request_production(self):
        for key, value in (('GITHUB_EVENT_NAME', 'pull_request'), ('GITHUB_REF', 'refs/heads/other'),
                           ('GITHUB_REPOSITORY', 'fork/repo'), ('DEPLOYMENT_AUTOMATION_ENABLED', 'false')):
            with self.subTest(key=key), self.assertRaises(ValueError): publish.trusted_context({**self.env, key: value})

    def test_failed_or_substituted_browser_result_retains_slot_and_accepted_predecessor(self):
        self.prepare()
        for fault in ('checks', 'age', 'source', 'archive'):
            result = self.result()
            if fault == 'checks': result['checks']['assets'] = False
            if fault == 'age': result['verified_unix'] -= 1801
            if fault == 'source': result['client_source_sha'] = '0' * 40
            if fault == 'archive': result['client_archive_sha256'] = '0' * 64
            with self.subTest(fault=fault), self.assertRaises(ValueError):
                publish.commit(self.catalog, self.host, self.host, self.workspace, result)
            self.assertEqual(self.catalog.pointers()[0]['current']['run_id'], 2)
            self.assertIsNotNone(self.host.state['lease'])

    def test_recheck_rejects_new_main_before_swa_action_and_never_clears_slot(self):
        self.prepare(); self.head = '0' * 40
        with self.assertRaises(ValueError): publish.recheck(self.host, self.workspace, self.env)
        self.assertIsNotNone(self.host.state['lease'])

    def test_dispatch_restores_only_accepted_identity_without_rebuilding_or_new_artifact(self):
        self.env.update(GITHUB_EVENT_NAME='workflow_dispatch', CLIENT_OPERATION='restore', RESTORE_RUN='2',
            RESTORE_ATTEMPT='1', RESTORE_ARTIFACT='20', RESTORE_CHECKSUM=self.old[0]['archive_sha256'])
        context = publish.reserve(self.catalog, self.host, self.host, self.workspace, None, '/does/not/exist.zip', self.env)
        self.assertEqual(context['candidate']['archive_sha256'], self.old[0]['archive_sha256'])
        self.assertFalse(context['upload_needed'])

    def test_unaccepted_or_wrong_checksum_restore_is_rejected_before_host_mutation(self):
        self.env.update(GITHUB_EVENT_NAME='workflow_dispatch', CLIENT_OPERATION='restore', RESTORE_RUN='2',
            RESTORE_ATTEMPT='1', RESTORE_ARTIFACT='20', RESTORE_CHECKSUM='0' * 64)
        with self.assertRaises(ValueError):
            publish.reserve(self.catalog, self.host, self.host, self.workspace, None, '/arbitrary.zip', self.env)
        self.assertFalse(any(command.startswith('begin-client ') for command in self.host.calls))

    def test_failed_candidate_restores_verified_predecessor_and_records_restored_outcome(self):
        self.prepare()
        with patch.object(publish, 'served_states', return_value=['candidate'] * 3):
            self.assertTrue(publish.prepare_restoration(self.host, self.workspace, 'success'))
        proof = self.result(); proof['client_archive_sha256'] = self.old[0]['archive_sha256']
        outcome = publish.commit(self.catalog, self.host, self.host, self.workspace, proof, restored=True)
        self.assertEqual(outcome['outcome'], 'restored')
        self.assertEqual(self.catalog.pointers()[0]['current']['run_id'], 2)
        self.assertIsNone(self.host.state['lease'])

    def test_unknown_or_mixed_lost_upload_response_does_not_trigger_restoration(self):
        self.prepare()
        for states in (['unknown'] * 3, ['candidate', 'recovery', 'candidate']):
            with patch.object(publish, 'served_states', return_value=states):
                with self.assertRaises(ValueError): publish.prepare_restoration(self.host, self.workspace, 'failure')
            self.assertIsNotNone(self.host.state['lease'])
        with patch.object(publish, 'served_states', return_value=['recovery'] * 3):
            self.assertFalse(publish.prepare_restoration(self.host, self.workspace, 'failure'))

    def test_missing_recovery_archive_stops_before_publication(self):
        from client_archive import record_prefix, record
        key = record_prefix(record(self.old[0])) + '/client.tar.gz'
        (self.catalog.store.root / key).unlink()
        with self.assertRaises(ValueError): self.prepare()
        self.assertIsNone(self.host.state['lease'])

    def test_frozen_server_change_prevents_acceptance_cas_before_finish(self):
        self.prepare()
        changed = {'digest': 'sha256:' + '0' * 64, 'config_sha256': 'b' * 64}
        self.host.state['pairs']['active'] = self.host.state['pairs']['known-good'] = changed
        with self.assertRaises(ValueError): publish.commit(self.catalog, self.host, self.host, self.workspace, self.result())
        self.assertEqual(self.catalog.pointers()[0]['current']['run_id'], 2)

    def test_lost_finish_response_is_resolved_by_reads_without_repeating_mutation(self):
        self.prepare(); self.host.finish_error_after_clear = True
        with patch.object(publish, 'observe', return_value=True):
            outcome = publish.commit(self.catalog, self.host, self.host, self.workspace, self.result())
        self.assertTrue(outcome['response_reconciled'])
        self.assertEqual(sum(command.startswith('finish-client ') for command in self.host.calls), 1)

    def test_explicit_new_restore_run_recovers_only_prior_reserved_predecessor(self):
        self.prepare()
        self.env.update(GITHUB_RUN_ID='91', GITHUB_EVENT_NAME='workflow_dispatch', CLIENT_OPERATION='restore',
            RESTORE_RUN='2', RESTORE_ATTEMPT='1', RESTORE_ARTIFACT='20', RESTORE_CHECKSUM=self.old[0]['archive_sha256'])
        with patch.object(publish, 'prior_job_completed', return_value=True), patch.object(publish, 'served_states', return_value=['candidate'] * 3):
            context = publish.reserve(self.catalog, self.host, self.host, self.root / 'recovery', None, '/no-free-upload.zip', self.env)
        self.assertEqual(context['candidate']['archive_sha256'], self.old[0]['archive_sha256'])
        self.assertTrue(any(command.startswith('begin-client-recovery production 91 ') for command in self.host.calls))
        self.assertFalse(any(command.startswith(('deploy ', 'rollback ', 'promote ')) for command in self.host.calls))


if __name__ == '__main__': unittest.main(verbosity=2)
