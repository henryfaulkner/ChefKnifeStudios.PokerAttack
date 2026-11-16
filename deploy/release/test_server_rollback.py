import copy
import hashlib
import unittest

import review
import server_manifest
import server_deliver
import test_server_deliver as fixtures
from review import canonical


class RollbackTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.DeliveryTests(); self.fixture.setUp()
        self.env = {**self.fixture.env,'SERVER_OPERATION':'rollback','GITHUB_RUN_ID':'43','GITHUB_EVENT_NAME':'workflow_dispatch'}
        manifest = self.fixture.packet['manifest']
        pair = {'digest':manifest['api']['digest'],'config_sha256':manifest['config_sha256'],'snapshot_config_matches':True}
        self.target = {'format':'pokerattack-rollback-target-v1','sampled_utc':self.fixture.live['sampled_utc'],
            'manifest':manifest,'manifest_sha256':hashlib.sha256(canonical(manifest)).hexdigest(),
            'current_pair':self.fixture.live['pairs']['active'],'pending_pair':None,'recovery_pair':pair,
            'recovery_from':None,'observed_sessions':None}
        self.schema = manifest['schema']
        self.calls = []

    def packet(self):return server_manifest.resolve_rollback(self.target,self.schema,self.env)

    def test_recorded_target_keeps_build_provenance_but_binds_new_reset_execution(self):
        packet = self.packet()
        self.assertEqual(packet['manifest']['workflow']['run_id'],42)
        self.assertEqual(packet['execution']['run_id'],43)
        self.assertIsNone(packet['observed_sessions'])
        self.assertEqual(review.reset_packet(packet,self.env),packet['manifest'])
        with self.assertRaises(ValueError):review.reset_packet(packet,{**self.env,'SERVER_OPERATION':'release'})

    def test_wrong_identity_schema_or_stale_recorded_target_rejected(self):
        original = copy.deepcopy(self.target)
        for field in ('hash','config','schema','stale'):
            self.target = copy.deepcopy(original)
            if field=='hash':self.target['manifest_sha256']='0'*64
            if field=='config':self.target['recovery_pair']['config_sha256']='0'*64
            if field=='schema':self.schema={}
            if field=='stale':self.target['sampled_utc']='2020-01-01T00:00:00Z'
            with self.subTest(field=field),self.assertRaises(ValueError):self.packet()
            self.schema=original['manifest']['schema']

    def call(self,command,packet=None,timeout=None):
        self.calls.append((command,packet))
        if command=='status production':return copy.deepcopy(self.fixture.live)
        if command=='rollback-target production':return copy.deepcopy(self.target)
        if command=='begin-rollback production 43 1':
            self.assertEqual(packet['acknowledgment']['action'],'rollback')
            self.assertIn('recovery_from',packet)
            manifest=packet['manifest']
            self.fixture.live['lease']={'kind':'server','run_id':43,'run_attempt':1,'phase':'reset-approved',
                'source_sha':manifest['source_sha'],'digest':manifest['api']['digest'],'config_sha256':manifest['config_sha256']}
            self.fixture.live['maintenance']['active']=True
            return {'phase':'reset-approved'}
        if command=='rollback production 43 1':
            pair={key:self.target['recovery_pair'][key] for key in ('digest','config_sha256')}
            self.fixture.live['pairs']['active']=self.fixture.live['pairs']['pending']=pair
            self.fixture.live['lease']['phase']='pending'
            manifest=self.target['manifest']
            self.fixture.live['runtime']={'image_id':manifest['api']['local_image_id'],'source_revision':manifest['source_sha']}
            return {'phase':'pending'}
        raise AssertionError('Unexpected command')

    def test_fresh_decision_uses_only_begin_rollback_then_recorded_rollback(self):
        packet=self.packet()
        reviews=[{'state':'approved','comment':packet['reset_review']['comment'],
            'environments':[{'name':'server-linode'}],'user':{'login':'operator'}}]
        api=lambda path: {'object':{'sha':self.env['GITHUB_SHA']}} if path=='git/ref/heads/main' else reviews
        result=server_deliver.deliver(packet,self.env,self,self,api)
        self.assertEqual(result['workflow']['run_id'],43)
        self.assertFalse(result['overall_release_complete'])
        self.assertEqual([command for command,_ in self.calls if command.split()[0] not in ('status','rollback-target')],
                         ['begin-rollback production 43 1','rollback production 43 1'])

    def test_changed_recorded_target_never_begins(self):
        packet=self.packet();self.target['recovery_from']={'run_id':99}
        reviews=[{'state':'approved','comment':packet['reset_review']['comment'],
            'environments':[{'name':'server-linode'}],'user':{'login':'operator'}}]
        api=lambda path:{'object':{'sha':self.env['GITHUB_SHA']}} if path=='git/ref/heads/main' else reviews
        with self.assertRaises(ValueError):server_deliver.deliver(packet,self.env,self,self,api)
        self.assertFalse(any(command.startswith('begin-') for command,_ in self.calls))


if __name__=='__main__':unittest.main(verbosity=2)
