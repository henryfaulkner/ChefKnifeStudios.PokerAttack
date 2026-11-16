"""Exact saved tooling must keep using its own Compose/validator contract."""
import copy
import hashlib
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

import bootstrap_recovery as recovery
import secure_state as storage


class RecoveryBundleTests(unittest.TestCase):
    def setUp(self):
        self.root=Path('/bootstrap-recovery-tests')/uuid.uuid4().hex;self.root.mkdir(parents=True,mode=0o700)
        self.addCleanup(shutil.rmtree,self.root)
        self.sources={}
        for name in recovery.SOURCES:
            path=self.root/name;path.write_bytes(b'#!/bin/bash\ninstall_dir=/opt/pokerattack\necho saved\n' if name=='pokerattack-deploy' else b'owned legacy fixture\n')
            path.chmod(0o600);self.sources[name]=path
        self.bundle=self.root/'bundles';self.config=self.root/'bootstrap.json'
        for name,value in (('RECOVERY',self.bundle),('CONFIG',self.config)):
            mocked=patch.object(recovery,name,value);mocked.start();self.addCleanup(mocked.stop)
        self.pair={'digest':'sha256:'+'a'*64,'config_sha256':'b'*64}
        self.live={'pairs':{'known-good':self.pair},'runtime':{'source_revision':'c'*40,'image_id':'sha256:'+'d'*64}}
    def record(self):
        value={'format':'pokerattack-bootstrap-recovery-v1','pair':self.pair,'source_sha':'c'*40,'image_id':'sha256:'+'d'*64,
               'client':{'mode':'legacy-recovery','client':None,'archive_sha256':'e'*64,
                         'probes':{'/index.html':'f'*64,'/appsettings.json':'1'*64}},'tooling':recovery.preserve(self.sources)}
        storage.write(self.config,value);return value
    def test_original_and_derived_tools_are_distinct_and_exactly_verified(self):
        record=self.record();directory=recovery.validate_tools(record['tooling'])
        self.assertEqual((directory/'original-pokerattack-deploy').read_bytes(),self.sources['pokerattack-deploy'].read_bytes())
        self.assertIn(('install_dir='+str(directory)).encode(),(directory/'pokerattack-deploy').read_bytes())
        self.assertEqual(recovery.snapshot(self.live),record)
        self.assertEqual(recovery.preserve(self.sources),record['tooling'])
        (directory/'compose.yaml').write_bytes(b'unreviewed replacement\n')
        with self.assertRaises(ValueError):recovery.snapshot(self.live)
    def test_different_runtime_pair_or_tool_path_is_refused(self):
        record=self.record()
        changed=copy.deepcopy(self.live);changed['runtime']['source_revision']='2'*40
        with self.assertRaises(ValueError):recovery.snapshot(changed)
        changed=copy.deepcopy(self.live);changed['pairs']['known-good']['config_sha256']='3'*64
        with self.assertRaises(ValueError):recovery.snapshot(changed)
        record['tooling']['files']['../secrets']='4'*64;storage.write(self.config,record)
        with self.assertRaises(ValueError):recovery.snapshot(self.live)
    def test_symlinked_or_writable_source_cannot_be_preserved(self):
        self.sources['compose.yaml'].chmod(0o666)
        with self.assertRaises(ValueError):recovery.preserve(self.sources)
        self.sources['compose.yaml'].unlink();self.sources['compose.yaml'].symlink_to(self.sources['validate-config.py'])
        with self.assertRaises(OSError):recovery.preserve(self.sources)


if __name__=='__main__':unittest.main()
