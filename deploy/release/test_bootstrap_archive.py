"""First client is provisional until actual imported/revoked proof authorizes CAS."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

from bootstrap_archive import ProvisionalClient
from blob_store import LocalStore
from client_archive import Archive,record
from client_artifacts import pack,finalize,identity_summary


class BootstrapArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory();self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name);source=self.root/'client';source.mkdir()
        (source/'index.html').write_text('<html>bootstrap</html>')
        (source/'appsettings.json').write_text(json.dumps({'AppSettings':{'ExternalApis':[
            {'Name':name,'BaseUri':'https://api.playpokerattack.com/'} for name in ('PokerAttackAPI','PokerAttackSignalR')]}}))
        (source/'staticwebapp.config.json').write_text(json.dumps({'globalHeaders':{'Blazor-Environment':'prod'}}))
        self.package=self.root/'client.tar.gz';self.manifest=finalize(pack(source,self.package,'a'*40,10,1),11)
        self.verified=False;self.substituted=False
        self.catalog=Archive(LocalStore(self.root/'store'))
        self.provisional=ProvisionalClient(self.manifest,self.package,self.operator)
    def operator(self,action):
        if action=='client-acceptance' and not self.verified:raise ValueError('Actual proof not imported/revoked')
        client=identity_summary(self.manifest)
        if self.substituted:client['archive_sha256']='f'*64
        return {'format':'pokerattack-bootstrap-client-status-v1','phase':'pending','client':client,
                'publication_sha256':'b'*64,'pair':{'digest':'sha256:'+'c'*64,'config_sha256':'d'*64},
                'source_sha':'e'*40,'verified_for_acceptance':action=='client-acceptance',
                'evidence_sha256':'f'*64 if self.verified else None}
    def test_provisional_is_explicit_and_does_not_accept_before_real_proof(self):
        pointers,etag=self.provisional.pointers()
        self.assertEqual(pointers['format'],'pokerattack-bootstrap-provisional-client-v1')
        self.assertEqual(pointers['current'],record(self.manifest))
        self.assertIsNone(self.catalog.pointers()[0]['current'])
        with self.assertRaises(ValueError):self.provisional.accept_verified(self.catalog,20,1)
        self.assertIsNone(self.catalog.pointers()[0]['current'])
        self.verified=True
        self.assertEqual(self.provisional.accept_verified(self.catalog,20,1)['current'],record(self.manifest))
        self.assertIsNone(self.catalog.lease_check)
        self.assertEqual(self.provisional.pointers()[1],etag)
    def test_substituted_publication_and_wrong_artifact_cannot_be_used(self):
        self.substituted=True
        with self.assertRaises(ValueError):self.provisional.pointers()
        self.substituted=False
        with self.assertRaises(ValueError):self.provisional.accepted_manifest(10,1,12,self.manifest['archive_sha256'])


if __name__=='__main__':unittest.main()
