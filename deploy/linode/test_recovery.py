import copy
import importlib.util
from pathlib import Path
import unittest

import secure_state as storage
import test_lease as fixtures

spec=importlib.util.spec_from_file_location('recovery',Path(__file__).with_name('release-recovery.py'))
recovery=importlib.util.module_from_spec(spec);spec.loader.exec_module(recovery)


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        self.fixture=fixtures.LeaseTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups)
        self.live=self.fixture.live
        self.live['sampled_utc']='2026-11-16T03:20:00Z'
        self.saved=self.fixture.base/'releases'/'retained'
        self.saved.mkdir(parents=True,mode=0o700)
        storage.write(self.saved/'release-manifest.json',self.fixture.manifest)
        self.pair={'digest':self.fixture.manifest['api']['digest'],
                   'config_sha256':self.fixture.manifest['config_sha256'],'snapshot_config_matches':True}
        self.live['pairs']['previous-good']=self.pair
        (self.fixture.base/'previous-good').symlink_to(self.saved,target_is_directory=True)

    def test_exact_recorded_manifest_is_read_only_and_no_config_bytes_escape(self):
        before=(self.saved/'release-manifest.json').read_bytes()
        result=recovery.target()
        self.assertEqual(result['manifest'],self.fixture.manifest)
        self.assertEqual(result['recovery_pair'],self.pair)
        self.assertEqual(before,(self.saved/'release-manifest.json').read_bytes())
        self.assertFalse((self.fixture.base/'lease').exists())
        self.assertFalse(self.fixture.closed())

    def test_pending_candidate_selects_known_good_instead_of_previous_good(self):
        self.live['pairs']['pending']=self.live['pairs']['active']
        self.live['pairs']['known-good']=self.pair
        (self.fixture.base/'known-good').symlink_to(self.saved,target_is_directory=True)
        self.assertEqual(recovery.target()['recovery_pair'],self.pair)

    def test_client_transaction_or_changed_saved_manifest_blocks_selection(self):
        for field in ('client','transaction','manifest'):
            if field=='client':storage.write(self.fixture.base/'lease',{'kind':'client'})
            if field=='transaction':self.live['transaction']=True
            if field=='manifest':
                changed=copy.deepcopy(self.fixture.manifest);changed['config_sha256']='0'*64
                storage.write(self.saved/'release-manifest.json',changed)
            with self.subTest(field=field),self.assertRaises(ValueError):recovery.target()
            (self.fixture.base/'lease').unlink(missing_ok=True);self.live['transaction']=False

    def test_missing_or_symlink_manifest_refused(self):
        manifest=self.saved/'release-manifest.json';manifest.unlink()
        with self.assertRaises(FileNotFoundError):recovery.target()
        storage.write(self.saved/'elsewhere.json',self.fixture.manifest)
        manifest.symlink_to(self.saved/'elsewhere.json')
        with self.assertRaises(OSError):recovery.target()


if __name__=='__main__':unittest.main(verbosity=2)
