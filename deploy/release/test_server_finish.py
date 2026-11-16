import json
import time
import unittest

import server_finish as finish
import test_server_promote as fixtures
from review import canonical
from ssh_client import UnknownOutcome


class FinishTests(unittest.TestCase):
    def setUp(self):
        self.promotion = fixtures.PromotionTests(); self.promotion.setUp()
        self.addCleanup(self.promotion.doCleanups)
        self.fixture = self.promotion.fixture
        self.outcome = self.promotion.promote()
        self.output = self.fixture.root/'completion.json'
        self.calls, self.private_paths = [], []
        self.fail, self.lost, self.changed = False, False, None

    def call(self, command, packet=None, timeout=None):
        self.calls.append(command)
        if command == 'status production': return self.fixture.host.call(command)
        self.assertEqual(command, 'finish-release production 42 1')
        if self.fail: raise ValueError('Reopen refused')
        state = self.fixture.host.state
        state['lease'] = None; state['maintenance']['active'] = False
        if self.lost: raise UnknownOutcome('Response lost after finish')
        return {'phase':'reopened'}

    def runner(self, identity, result, env):
        self.private_paths.append(identity)
        client = json.loads(identity.read_bytes())
        proof = {'checks':dict.fromkeys(finish.CHECKS,True),'verified_unix':int(time.time()),
                 'client_source_sha':client['source_sha'],'client_archive_sha256':client['archive_sha256']}
        if self.changed: self.changed(proof)
        result.write_bytes(canonical(proof))

    def finish(self, runner=None):
        return finish.finish(self.fixture.manifest,self.outcome,self.fixture.env,self,self,
                             self.fixture.catalog,self.output,runner or self.runner)

    def test_complete_requires_public_checks_after_reopening(self):
        result = self.finish()
        self.assertTrue(result['overall_release_complete'])
        self.assertEqual(result['phase'],'complete')
        self.assertEqual(self.calls.count('finish-release production 42 1'),1)
        self.assertTrue(all(not path.exists() for path in self.private_paths))

    def test_lost_finish_response_reconciles_read_only_without_retry(self):
        self.lost = True
        result = self.finish()
        self.assertTrue(result['finish_response_reconciled'])
        self.assertEqual(self.calls.count('finish-release production 42 1'),1)

    def test_refused_finish_keeps_gate_closed_and_result_incomplete(self):
        self.fail = True
        with self.assertRaises(ValueError): self.finish()
        self.assertTrue(self.fixture.host.state['maintenance']['active'])
        self.assertFalse(json.loads(self.output.read_bytes())['overall_release_complete'])

    def test_public_failure_records_open_state_without_automatic_rollback(self):
        def fail(*_): raise ValueError('Browser failed')
        with self.assertRaises(ValueError): self.finish(fail)
        self.assertFalse(self.fixture.host.state['maintenance']['active'])
        result = json.loads(self.output.read_bytes())
        self.assertEqual(result['phase'],'promoted-and-reopened-public-check-failed')
        self.assertFalse(result['overall_release_complete'])
        self.assertEqual([call for call in self.calls if call!='status production'],['finish-release production 42 1'])

    def test_stale_wrong_or_missing_public_proof_never_completes(self):
        for field in ('stale','client','check'):
            self.setUp()
            def change(proof):
                if field=='stale': proof['verified_unix']-=61
                if field=='client': proof['client_source_sha']='0'*40
                if field=='check': proof['checks']['signalr']=False
            self.changed=change
            with self.subTest(field=field),self.assertRaises(ValueError):self.finish()
            self.assertFalse(json.loads(self.output.read_bytes())['overall_release_complete'])

    def test_unrevoked_or_changed_client_prevents_reopening(self):
        for field in ('revocation','client'):
            self.setUp()
            if field=='revocation':self.fixture.host.state['lease']['verifier_revoked']=False
            else:self.outcome['client_source_sha']='0'*40
            with self.subTest(field=field),self.assertRaises(ValueError):self.finish()
            self.assertNotIn('finish-release production 42 1',self.calls)
            self.assertTrue(self.fixture.host.state['maintenance']['active'])


if __name__=='__main__':unittest.main(verbosity=2)
