"""Bootstrap uploads require root lease authorization; unknown outcomes never retry."""
import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import bootstrap_artifacts as artifacts
import bootstrap_publish as publisher
from client_artifacts import pack,finalize


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory();self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name);source=self.root/'source';source.mkdir()
        (source/'index.html').write_text('<html>client</html>')
        (source/'appsettings.json').write_text(json.dumps({'AppSettings':{'ExternalApis':[
            {'Name':name,'BaseUri':'https://api.playpokerattack.com/'} for name in ('PokerAttackAPI','PokerAttackSignalR')]}}))
        (source/'staticwebapp.config.json').write_text(json.dumps({'globalHeaders':{'Blazor-Environment':'prod'}}))
        self.archive=self.root/'client.tar.gz';self.manifest=finalize(pack(source,self.archive,'a'*40,10,1),11)
        self.calls=[];self.uploads=[];self.unknown=False
    def operator(self,action,run,attempt,packet=None):
        self.calls.append((action,run,attempt,packet))
        if action=='client-authorize':
            import hashlib
            digest=hashlib.sha256(publisher.canonical(packet)).hexdigest()
            return {'sha256':digest,'upload_outcome':'unknown' if self.unknown else 'not-started'}
        return {'phase':'bootstrap-publication-verified'}
    def test_one_authorized_upload_then_matching_hash_probe(self):
        result=publisher.publish('candidate',self.archive,self.manifest,self.root/'publish',20,1,
                                 root_operator=self.operator,uploader=lambda directory:self.uploads.append(directory))
        self.assertEqual(result['phase'],'bootstrap-publication-verified');self.assertEqual(len(self.uploads),1)
        self.assertEqual([call[0] for call in self.calls],['client-authorize','client-published'])
        self.assertEqual(self.calls[1][3]['sha256'],json.loads((self.root/'publish/bootstrap-publication.json').read_bytes())['sha256'])
    def test_unknown_authorization_refuses_upload_and_reconciliation_only_probes(self):
        self.unknown=True;workspace=self.root/'publish'
        with self.assertRaises(publisher.UnknownPublication):
            publisher.publish('candidate',self.archive,self.manifest,workspace,20,1,root_operator=self.operator,uploader=self.uploads.append)
        self.assertEqual(self.uploads,[]);self.calls=[]
        publisher.publish('candidate',self.archive,self.manifest,workspace,20,1,reconcile=True,
                          root_operator=self.operator,uploader=self.uploads.append)
        self.assertEqual([call[0] for call in self.calls],['client-published']);self.assertEqual(self.uploads,[])
        with self.assertRaises(ValueError):publisher.publish('candidate',self.archive,self.manifest,workspace,21,1,reconcile=True,
                          root_operator=self.operator,uploader=self.uploads.append)
    def test_bootstrap_archive_restores_bytes_and_rejects_substituted_or_incomplete_manifest(self):
        source=self.root/'legacy';source.mkdir()
        for name,raw in [('index.html','legacy'),('appsettings.json','{}'),('staticwebapp.config.json','{}')]:
            (source/name).write_text(raw)
        archive=self.root/'legacy.tar.gz';manifest=artifacts.pack(source,archive,'legacy-recovery')
        artifacts.check(archive,manifest,self.root/'restored')
        self.assertEqual((self.root/'restored/index.html').read_text(),'legacy')
        for field,value in [('archive_sha256','f'*64),('files',{'index.html':manifest['files']['index.html']})]:
            with self.subTest(field=field),self.assertRaises(ValueError):artifacts.check(archive,{**manifest,field:value})
    def test_root_transport_cannot_invoke_arbitrary_operations(self):
        for action,run in [('deploy',20),('client-authorize; id',20),('client-authorize',True)]:
            with self.assertRaises(ValueError):publisher.operator(action,run,1)


if __name__=='__main__':unittest.main()
