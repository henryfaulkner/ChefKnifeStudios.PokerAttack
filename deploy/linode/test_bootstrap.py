"""Root publication ordering and immutable reconciliation; no external network."""
import copy
import hashlib
import json
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

import bootstrap_control as bootstrap
import secure_state as storage
import release_lease
import release_verifier


class BootstrapTests(unittest.TestCase):
    def setUp(self):
        self.root=Path('/bootstrap-tests')/uuid.uuid4().hex;self.base=self.root/'production'
        (self.base/'admission').mkdir(parents=True,mode=0o755)
        self.addCleanup(shutil.rmtree,self.root)
        mocked=patch.object(storage,'STATE',self.root);mocked.start();self.addCleanup(mocked.stop)
        self.now=1794757000
        self.pair={'digest':'sha256:'+'a'*64,'config_sha256':'b'*64}
        self.lease={'kind':'server','bootstrap':True,'phase':'bootstrap-maintenance-pending','run_id':1,'run_attempt':1,
                    **self.pair,'source_sha':'c'*40,'acknowledgment':{'approved_unix':self.now,'expires_unix':self.now+1800,'drain_seconds':0,'policy':'reset-at-deadline'}}
        self.live={'consistent':True,'transaction':False,'readiness':{'ready':True},
                   'pairs':{'active':self.pair,'known-good':self.pair,'pending':None},'runtime':{'source_revision':'c'*40}}
        for owner,name,value in ((bootstrap.status_module,'status',lambda _:copy.deepcopy(self.live)),(bootstrap.time,'time',lambda:self.now),
                                  (release_lease,'counts',lambda:{'activeGames':0,'lobbies':0,'soloGames':0})):
            mocked=patch.object(owner,name,value);mocked.start();self.addCleanup(mocked.stop)
        self.save()
        self.packet={'mode':'maintenance','archive_sha256':'d'*64,'probes':{'/index.html':'e'*64},'client':None}
        self.client=json.loads((Path(__file__).parents[1]/'release/client-manifest.example.json').read_text())
        # The host consumes the bounded identity summary, never the full file map.
        self.client={key:self.client[key] for key in ('source_sha','workflow','artifact_id','archive_sha256','settings_sha256','minimum_server_capability')}
        self.client.update(format='pokerattack-client-identity-v1',settings_files={'appsettings.json':self.client['settings_sha256']})
    def save(self):
        storage.write(self.base/'lease',self.lease)
        storage.write(self.base/'admission/state.json',{'active':True,**{key:self.lease[key] for key in ('run_id','run_attempt','digest','config_sha256','source_sha')}},0o644)
    def test_cannot_drain_before_verified_static_publication(self):
        with self.assertRaises(ValueError):bootstrap.drain('1','1')
        bootstrap.intent('1','1',self.packet)
        with self.assertRaises(ValueError):bootstrap.drain('1','1')
        with patch.object(bootstrap,'probe',side_effect=ValueError('actual bytes differ')):
            with self.assertRaises(ValueError):bootstrap.published('1','1')
        self.assertIn('bootstrap_publication_intent',storage.read(self.base/'lease'))
        with patch.object(bootstrap,'probe'):
            bootstrap.published('1','1')
            self.assertEqual(bootstrap.drain('1','1')['phase'],'drained')
        self.assertTrue(storage.read(self.base/'admission/state.json',0o644)['active'])
    def test_unknown_upload_reconciles_same_intent_and_refuses_replacement(self):
        original=bootstrap.intent('1','1',self.packet)
        resumed=bootstrap.intent('1','1',self.packet)
        self.assertTrue(resumed['idempotent']);self.assertEqual(original['sha256'],resumed['sha256'])
        with self.assertRaises(ValueError):bootstrap.intent('1','1',{**self.packet,'archive_sha256':'f'*64})
        with patch.object(bootstrap,'probe'):bootstrap.published('1','1')
        with self.assertRaises(ValueError):bootstrap.intent('1','1',self.packet)
    def test_regular_lease_wrong_owner_and_arbitrary_probe_refused(self):
        for packet in ({**self.packet,'probes':{'https://evil.example':'e'*64}}, {**self.packet,'client':self.client}):
            with self.assertRaises(ValueError):bootstrap.intent('1','1',packet)
        with self.assertRaises(ValueError):bootstrap.intent('2','1',self.packet)
        self.lease['bootstrap']=False;self.save()
        with self.assertRaises(ValueError):bootstrap.intent('1','1',self.packet)
    def test_candidate_requires_pending_runtime_and_blocks_verifier_until_publication(self):
        packet={'mode':'candidate','archive_sha256':self.client['archive_sha256'],'client':self.client,
                'probes':{name:'e'*64 for name in ('/index.html','/release.json','/appsettings.json')}}
        with self.assertRaises(ValueError):bootstrap.intent('1','1',packet)
        self.lease['phase']='pending';self.save();self.live['pairs']['pending']=self.pair
        players={'players':[{'id':str(uuid.uuid4()),'name':'Release-1-1-'+str(i)} for i in range(3)]}
        with self.assertRaises(ValueError):release_verifier.issue('1','1',players)
        bootstrap.intent('1','1',packet)
        with self.assertRaises(ValueError):release_verifier.issue('1','1',players)
        with patch.object(bootstrap,'probe'):bootstrap.published('1','1')
        self.assertIn('credential',release_verifier.issue('1','1',players))
    def test_expired_static_observation_does_not_refresh_reset_ack(self):
        bootstrap.intent('1','1',self.packet)
        with patch.object(bootstrap,'probe'):bootstrap.published('1','1')
        self.now+=1801
        with self.assertRaises(ValueError):bootstrap.drain('1','1')
        self.assertEqual(storage.read(self.base/'lease')['acknowledgment']['approved_unix'],1794757000)

    def test_lost_publication_response_reconciles_only_matching_hash_without_timestamp_refresh(self):
        authorized=bootstrap.intent('1','1',self.packet)
        with patch.object(bootstrap,'probe'):
            bootstrap.published('1','1',authorized['sha256'])
            before=(self.base/'lease').read_bytes();self.now+=20
            result=bootstrap.published('1','1',authorized['sha256'])
            self.assertTrue(result['response_reconciled'])
            self.assertEqual((self.base/'lease').read_bytes(),before)
            with self.assertRaises(ValueError):bootstrap.published('1','1','0'*64)


if __name__=='__main__':unittest.main()
