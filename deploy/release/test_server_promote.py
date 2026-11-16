import copy
import hashlib
import json
import unittest
from unittest.mock import patch

from release_commands import parse_ssh
from ssh_client import UnknownOutcome
import server_promote as promoter
import test_server_verify as fixtures


class PromotionHost:
    def __init__(self, host): self.host,self.calls,self.lost=host,[],False
    def call(self, command, packet=None, timeout=None):
        parse_ssh('promotion',command);self.calls.append(command)
        if command=='status production':return self.host.call(command)
        if not command.startswith('promote '):raise AssertionError('Unexpected reopening or deployment')
        state=self.host.state
        state['lease']['phase']='promoted'
        state['pairs']['known-good']=state['pairs']['active'];state['pairs']['pending']=None
        if self.lost:raise UnknownOutcome('Lost promotion response')
        return {'phase':'promoted'}


class PromotionTests(unittest.TestCase):
    def setUp(self):
        self.fixture=fixtures.VerifyTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups)
        self.review=self.fixture.verify();self.raw=(self.fixture.output/'evidence.json').read_bytes()
        self.host=PromotionHost(self.fixture.host)
        self.reviews=[{'state':'approved','comment':self.review['comment'],
                       'environments':[{'name':'server-promotion'}],'user':{'login':'operator'}}]

    def promote(self):
        return promoter.promote(self.fixture.manifest,self.raw,self.review,self.fixture.env,self.host,self.host,lambda _:self.reviews)

    def test_explicit_decision_promotes_only_imported_hash_and_does_not_reopen(self):
        result=self.promote()
        self.assertEqual(result['phase'],'promoted');self.assertFalse(result['overall_release_complete'])
        mutations=[call for call in self.host.calls if call!='status production']
        self.assertEqual(mutations,[f"promote production {self.fixture.manifest['api']['digest']} {hashlib.sha256(self.raw).hexdigest()} 42 1"])
        self.assertTrue(self.fixture.host.state['maintenance']['active'])

    def test_missing_or_wrong_environment_decision_never_promotes(self):
        self.reviews=[]
        with self.assertRaises(ValueError):self.promote()
        self.reviews=[{'state':'approved','comment':self.review['comment'],
                       'environments':[{'name':'server-linode'}],'user':{'login':'operator'}}]
        with self.assertRaises(ValueError):self.promote()
        self.assertEqual(self.host.calls,[])

    def test_stale_evidence_cannot_be_renewed_by_approval_or_runner_start(self):
        with patch.object(promoter.time,'time',return_value=self.review['evidence']['verified_unix']+1800):
            with self.assertRaises(ValueError):self.promote()
        self.assertEqual(self.host.calls,[])

    def test_exact_evidence_bytes_and_review_identity_cannot_change(self):
        self.raw+=b'\n'
        with self.assertRaises(ValueError):self.promote()
        self.assertEqual(self.host.calls,[])

    def test_missing_import_revocation_or_changed_runtime_prevents_promotion(self):
        original=copy.deepcopy(self.fixture.host.state)
        for field in ('import','revocation','runtime'):
            self.fixture.host.state=copy.deepcopy(original)
            if field=='import':self.fixture.host.state['lease']['evidence_sha256']='0'*64
            if field=='revocation':self.fixture.host.state['lease']['verifier_revoked']=False
            if field=='runtime':self.fixture.host.state['runtime']['image_id']='sha256:'+'0'*64
            with self.subTest(field=field),self.assertRaises(ValueError):self.promote()
        self.assertFalse(any(call.startswith('promote ') for call in self.host.calls))

    def test_unknown_promotion_response_is_never_retried_or_reopened(self):
        self.host.lost=True
        with self.assertRaises(UnknownOutcome):self.promote()
        self.assertEqual(sum(call.startswith('promote ') for call in self.host.calls),1)
        self.assertTrue(self.fixture.host.state['maintenance']['active'])
        self.assertEqual(self.fixture.host.state['lease']['phase'],'promoted')


if __name__=='__main__':unittest.main(verbosity=2)
