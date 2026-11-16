import copy
import datetime
import hashlib
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch

from blob_store import LocalStore
from client_archive import Archive
from client_artifacts import pack, finalize
from review import canonical
from ssh_client import UnknownOutcome
from release_commands import parse_ssh
import server_verify as verifier
import test_server_manifest as fixtures


class Host:
    def __init__(self, manifest):
        pair = {'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256']}
        self.state = {'protocol_version': 1, 'target': 'production', 'consistent': True, 'transaction': False, 'maintenance': {'active': True},
            'readiness': {'ready': True}, 'runtime': {'image_id': manifest['api']['local_image_id'], 'source_revision': manifest['source_sha']},
            'sessions': {'activeGames': 0, 'lobbies': 0, 'soloGames': 0},
            'pairs': {'active': pair, 'pending': pair, 'known-good': {'digest': 'sha256:'+'d'*64, 'config_sha256': 'e'*64}},
            'lease': {'kind': 'server', 'run_id': 42, 'run_attempt': 1, 'phase': 'pending', 'source_sha': manifest['source_sha'], **pair,
                      'manifest_sha256': hashlib.sha256(canonical(manifest)).hexdigest(), 'verifier_issued': False, 'verifier_revoked': False}}
        self.calls, self.lost = [], None

    def call(self, command, packet=None, timeout=None):
        parse_ssh('evidence', command)
        self.calls.append((command, copy.deepcopy(packet)))
        if command == 'status production':
            return {**copy.deepcopy(self.state), 'sampled_utc': datetime.datetime.now(datetime.timezone.utc).isoformat()}
        parts = command.split(); action = parts[0]; lease = self.state['lease']
        if action == 'resume-verification':
            lease.update(run_attempt=int(parts[4]), verifier_issued=False, verifier_revoked=False,
                phase='pending' if self.state['pairs']['pending'] else 'promoted-needs-verification')
            lease.pop('evidence_sha256', None)
            result = {'phase': lease['phase']}
        elif action == 'verifier-credential':
            lease['verifier_issued'] = True
            result = {'credential': f"42.{lease['run_attempt']}."+'e'*64, 'verifier_id': 'f'*32,
                      'players': packet['players'], 'expires_unix': int(time.time())+900}
        elif action == 'revoke-verifier':
            lease['verifier_revoked'] = True; result = {'revoked': True}
        elif action == 'import-evidence':
            assert lease['verifier_revoked'] is True
            lease['evidence_sha256'] = hashlib.sha256(json.dumps(packet, separators=(',', ':')).encode()).hexdigest()
            result = {'evidence_sha256': lease['evidence_sha256']}
        else: raise AssertionError('Unexpected mutation authority')
        if action == self.lost: raise UnknownOutcome('Fixture lost response')
        return result


class VerifyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        fixture = fixtures.ServerManifestTests(); fixture.setUp()
        self.manifest = fixture.resolve()['manifest']; self.env = {**fixtures.ENV, 'RUNNER_TEMP': str(self.root)}
        self.host = Host(self.manifest)
        source = self.root/'client'; source.mkdir()
        (source/'index.html').write_text('fixture')
        (source/'staticwebapp.config.json').write_text('{"globalHeaders":{"Blazor-Environment":"prod"}}')
        (source/'appsettings.json').write_text(json.dumps({'AppSettings': {'ExternalApis': [
            {'Name': name, 'BaseUri': 'https://api.playpokerattack.com/'} for name in ('PokerAttackAPI','PokerAttackSignalR')]}}))
        archive = self.root/'client.tar.gz'
        client = finalize(pack(source, archive, 'c'*40, 3, 1), 30)
        self.catalog = Archive(LocalStore(self.root/'blobs'), lambda *_: True)
        self.catalog.accept(client, archive, 3, 1)
        self.output = self.root/'result'; self.private_paths = []; self.change = None

    def runner(self, context, result, env):
        self.private_paths.append(context)
        if os.name != 'nt': self.assertEqual(context.stat().st_mode & 0o777, 0o600)
        packet = json.loads(context.read_bytes()); manifest = packet['server_manifest']
        self.assertEqual(len({player['id'] for player in packet['players']}), 3)
        proof = {'format': 'pokerattack-evidence-v1', 'source_sha': manifest['source_sha'],
            'workflow': {**manifest['workflow'], 'run_attempt': packet['run_attempt']},
            'digest': manifest['api']['digest'], 'migration_digest': manifest['migration']['digest'],
            'config_sha256': manifest['config_sha256'], 'client_source_sha': packet['client']['source_sha'],
            'client_archive_sha256': packet['client']['archive_sha256'], 'verifier_id': packet['verifier_id'],
            'verified_unix': int(time.time()), 'checks': dict.fromkeys(
                ('https','served_client','signalr','multiplayer','solo','private_message_isolation'), True),
            'artifact_references': packet['artifact_references']}
        if self.change: self.change(proof)
        result.write_bytes(canonical({'passed': True, 'evidence': proof}))

    def verify(self, runner=None):
        return verifier.verify(self.manifest,self.env,self.host,self.host,self.catalog,self.output,runner or self.runner)

    def test_only_completed_bound_proof_is_imported_and_private_material_is_removed(self):
        result = self.verify()
        raw = (self.output/'evidence.json').read_bytes()
        self.assertEqual(hashlib.sha256(raw).hexdigest(),result['evidence_sha256'])
        self.assertTrue(self.host.state['lease']['verifier_revoked'])
        self.assertTrue(all(not path.exists() for path in self.private_paths))
        public = ''.join(path.read_text() for path in self.output.glob('*.json'))
        self.assertNotIn('42.1.'+'e'*64, public)
        self.assertNotIn('Release-42-', public)
        self.assertFalse(result['overall_release_complete'])

    def test_failed_browser_revokes_own_token_but_never_imports_or_reopens(self):
        def failure(*args): raise ValueError('Browser assertion failed')
        with self.assertRaises(ValueError): self.verify(failure)
        actions = [call[0].split()[0] for call in self.host.calls]
        self.assertEqual(actions.count('revoke-verifier'),1)
        self.assertNotIn('import-evidence',actions)
        self.assertTrue(self.host.state['maintenance']['active'])
        self.assertEqual(list(self.output.glob('*.json')),[])

    def test_wrong_or_stale_proof_never_imports(self):
        for field,value in [('verified_unix',int(time.time())-61),('source_sha','b'*40),('client_archive_sha256','b'*64)]:
            self.setUp(); self.change=lambda proof: proof.update({field:value})
            with self.subTest(field=field), self.assertRaises(ValueError): self.verify()
            self.assertFalse(any(call[0].startswith('import-evidence') for call in self.host.calls))
            self.assertTrue(self.host.state['lease']['verifier_revoked'])

    def test_later_attempt_resumes_only_matching_pair_without_redeployment(self):
        self.host.state['lease'].update(verifier_issued=True,verifier_revoked=True)
        self.env['GITHUB_RUN_ATTEMPT']='2'
        result=self.verify()
        actions=[call[0].split()[0] for call in self.host.calls]
        self.assertIn('resume-verification',actions)
        self.assertEqual(result['evidence']['workflow']['run_attempt'],2)
        self.assertNotIn('deploy',actions)
        self.assertNotIn('begin-release',actions)
        self.assertEqual(self.manifest['workflow']['run_attempt'],1,'Original build provenance unchanged')

    def test_promoted_closed_pair_resumes_fresh_verification_without_restart(self):
        self.host.state['lease'].update(phase='promotion-unknown',verifier_issued=True,verifier_revoked=True)
        self.host.state['pairs']['known-good']=self.host.state['pairs']['active'];self.host.state['pairs']['pending']=None
        self.env['GITHUB_RUN_ATTEMPT']='2'
        result=self.verify()
        self.assertEqual(result['evidence']['workflow']['run_attempt'],2)
        self.assertEqual(self.host.state['lease']['phase'],'promoted-needs-verification')

    def test_unknown_resume_or_import_is_not_retried(self):
        for action in ('resume-verification','import-evidence'):
            self.setUp();self.host.lost=action
            if action=='resume-verification':self.env['GITHUB_RUN_ATTEMPT']='2'
            with self.subTest(action=action),self.assertRaises(UnknownOutcome):self.verify()
            actions=[call[0].split()[0] for call in self.host.calls]
            self.assertEqual(actions.count(action),1)
            self.assertEqual(actions.count('verifier-credential'),0 if action=='resume-verification' else 1)

    def test_changed_client_or_remaining_sessions_blocks_evidence(self):
        for where in ('client','sessions'):
            self.setUp()
            if where=='client':
                value,etag=self.catalog.pointers()
                self.catalog.pointers=lambda: (value,etag if not self.private_paths else 'changed')
            else:self.host.state['sessions']['soloGames']=1
            with self.subTest(where=where),self.assertRaises(ValueError):self.verify()
            self.assertFalse(any(call[0].startswith('import-evidence') for call in self.host.calls))

    def test_wrong_runtime_or_duplicate_attempt_cannot_issue_admission(self):
        for where in ('runtime','issued','manifest'):
            self.setUp()
            if where=='runtime':self.host.state['runtime']['image_id']='sha256:'+'a'*64
            if where=='issued':self.host.state['lease']['verifier_issued']=True
            if where=='manifest':self.host.state['lease']['manifest_sha256']='0'*64
            with self.subTest(where=where),self.assertRaises(ValueError):self.verify()
            self.assertFalse(any(call[0].startswith('verifier-credential') for call in self.host.calls))


if __name__=='__main__':unittest.main(verbosity=2)
