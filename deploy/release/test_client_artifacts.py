import copy
import hashlib
import io
import json
import os
from pathlib import Path
import tarfile
import tempfile
import time
import unittest
from unittest.mock import patch

from blob_store import AzureStore, Conflict, LocalStore
from client_archive import Archive, prefix, record
from client_artifacts import canonical, check_archive, finalize, identity_summary, pack, sha
from manifests import load_document


class ClientArtifactTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='pokerattack-client-archive-test-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.store = LocalStore(self.root / 'blobs')
        self.held = True
        self.archive = Archive(self.store, lambda run, attempt: self.held and (run, attempt) == (90, 1))

    def build(self, run=1, artifact_id=10):
        directory = self.root / f'candidate-{run}'
        directory.mkdir()
        settings = {'AppSettings': {'ExternalApis': [{'Name': name, 'BaseUri': 'https://api.playpokerattack.com/'}
                    for name in ('PokerAttackAPI', 'PokerAttackSignalR')]}}
        (directory / 'appsettings.json').write_text(json.dumps(settings))
        (directory / 'staticwebapp.config.json').write_text('{"globalHeaders":{"Blazor-Environment":"prod"}}')
        (directory / 'index.html').write_bytes(b'<html>actual candidate bytes</html>')
        (directory / 'asset.wasm').write_bytes(bytes(range(256)) * 10)
        archive = self.root / f'{run}.tar.gz'
        candidate = pack(directory, archive, 'c' * 40, run, 1)
        manifest = finalize(candidate, artifact_id)
        return directory, archive, candidate, manifest

    def test_candidate_needs_numeric_artifact_id_and_roundtrips_exact_bytes(self):
        directory, archive, candidate, manifest = self.build()
        self.assertEqual(candidate['format'], 'pokerattack-client-candidate-v1')
        for invalid in ('some-artifact-name', True, 0):
            with self.assertRaises(ValueError): finalize(candidate, invalid)
        output = self.root / 'restored'
        self.assertTrue(check_archive(archive, manifest, output))
        for name in manifest['files']:
            self.assertEqual((directory / name).read_bytes(), (output / name).read_bytes())
        with self.assertRaises(ValueError): pack(directory, self.root / 'restamp.tar.gz', 'c' * 40, 1, 1)

    def test_exact_archive_is_preserved_and_restoration_requires_acceptance(self):
        _, archive, _, manifest = self.build()
        self.archive.store_candidate(manifest, archive)
        with self.assertRaises(ValueError): self.archive.recover(1, 1, 10, manifest['archive_sha256'], self.root / 'unaccepted')
        accepted = self.archive.accept(manifest, archive, 90, 1)
        self.assertEqual(accepted['current'], record(manifest))
        restored = self.root / 'accepted-restore'
        self.assertEqual(self.archive.recover(1, 1, 10, manifest['archive_sha256'], restored), manifest)
        self.assertEqual((restored / 'index.html').read_bytes(), b'<html>actual candidate bytes</html>')
        self.assertEqual(self.archive.accept(manifest, archive, 90, 1), accepted)
        for identity in ((2, 1, 10, manifest['archive_sha256']), (1, 1, 11, manifest['archive_sha256']), (1, 1, 10, '0' * 64)):
            with self.assertRaises(ValueError): self.archive.recover(*identity, self.root / 'wrong')

    def test_last_two_accepted_survive_older_than_retention_and_unpinned_candidate_prunes(self):
        first = self.build(1, 10); second = self.build(2, 20); pending = self.build(3, 30)
        self.archive.accept(first[3], first[1], 90, 1)
        self.archive.accept(second[3], second[1], 90, 1)
        self.archive.store_candidate(pending[3], pending[1])
        old = time.time() - 40 * 86400
        for path in self.store.root.rglob('*'):
            if path.is_file(): os.utime(path, (old, old))
        deleted = self.archive.prune(90, 1)
        self.assertEqual(len(deleted), 2)
        self.assertTrue(all(key.startswith('releases/3/') for key in deleted))
        self.archive.recover(1, 1, 10, first[3]['archive_sha256'], self.root / 'old-first')
        self.archive.recover(2, 1, 20, second[3]['archive_sha256'], self.root / 'old-second')
        self.held = False
        with self.assertRaises(ValueError): self.archive.prune(90, 1)
        with self.assertRaises(ValueError): self.archive.accept(pending[3], pending[1], 90, 1)

    def test_failed_cas_does_not_make_candidate_accepted_and_never_overwrites_current(self):
        first = self.build(1); second = self.build(2)
        self.archive.accept(first[3], first[1], 90, 1)
        before = (self.store.root / 'accepted.json').read_bytes()
        original = self.store.upload
        def conflict(key, *args, **kwargs):
            if key == 'accepted.json': raise Conflict('Injected concurrent accepted pointer change')
            return original(key, *args, **kwargs)
        with patch.object(self.store, 'upload', side_effect=conflict):
            with self.assertRaises(Conflict): self.archive.accept(second[3], second[1], 90, 1)
        self.assertEqual(before, (self.store.root / 'accepted.json').read_bytes())
        with self.assertRaises(ValueError): self.archive.recover(2, 1, 10, second[3]['archive_sha256'], self.root / 'bad-cas-restore')

    def test_interrupted_history_write_uses_only_committed_current_or_previous_pointer(self):
        item = self.build()
        original = self.archive.write_immutable_json
        def fail_history(key, value):
            if key.startswith('accepted/'): raise OSError('Injected history write interruption')
            return original(key, value)
        with patch.object(self.archive, 'write_immutable_json', side_effect=fail_history):
            with self.assertRaises(OSError): self.archive.accept(item[3], item[1], 90, 1)
        self.archive.recover(1, 1, 10, item[3]['archive_sha256'], self.root / 'lost-response-restore')
        self.archive.accept(item[3], item[1], 90, 1)

    def test_corrupt_archive_or_manifest_and_wrong_settings_cannot_restore(self):
        _, archive, _, manifest = self.build()
        wrong = copy.deepcopy(manifest); wrong['archive_sha256'] = '0' * 64
        with self.assertRaises(ValueError): check_archive(archive, wrong, self.root / 'wrong-checksum')
        self.archive.accept(manifest, archive, 90, 1)
        key = prefix(1, 1, 10, manifest['archive_sha256'])
        (self.store.root / key / 'client.tar.gz').write_bytes(b'corrupt archive')
        with self.assertRaises(ValueError): self.archive.recover(1, 1, 10, manifest['archive_sha256'], self.root / 'corrupt-restore')
        with self.assertRaises(Conflict): self.archive.store_candidate(manifest, archive)

    def test_malicious_tar_paths_links_duplicates_and_corrupt_member_fail_without_output(self):
        _, _, _, manifest = self.build()
        for fault in ('path', 'symlink', 'duplicate', 'content'):
            path = self.root / (fault + '.tar.gz')
            with tarfile.open(path, 'w:gz') as stream:
                member = tarfile.TarInfo('../outside' if fault == 'path' else 'index.html')
                content = b'incorrect content'
                member.size = len(content)
                if fault == 'symlink': member.type = tarfile.SYMTYPE; member.linkname = '../outside'; member.size = 0
                stream.addfile(member, io.BytesIO(content))
                if fault == 'duplicate': stream.addfile(member, io.BytesIO(content))
            changed = copy.deepcopy(manifest); changed['archive_sha256'] = sha(path)
            destination = self.root / ('restore-' + fault)
            with self.assertRaises(ValueError): check_archive(path, changed, destination)
            self.assertFalse(destination.exists())
        self.assertFalse(list(self.root.glob('.client-restore-*')))

    def test_large_manifest_is_bounded_separately_and_host_summary_remains_small(self):
        _, _, _, manifest = self.build()
        manifest['files'].update({f'assets/{i}.wasm': 'd' * 64 for i in range(1500)})
        raw = canonical(manifest)
        self.assertGreater(len(raw), 65536)
        with self.assertRaises(ValueError): load_document(raw)
        self.assertEqual(load_document(raw, 'client-manifest'), manifest)
        summary = identity_summary(manifest)
        self.assertLess(len(canonical(summary)), 65536)
        self.assertNotIn('files', summary)
        self.assertEqual(summary['settings_files'], {'appsettings.json': manifest['settings_sha256']})

    def test_azure_adapter_has_fixed_container_oidc_and_atomic_conditions_without_secret_arguments(self):
        class Result:
            returncode, stdout, stderr = 0, '{}', ''
        with patch('blob_store.subprocess.run', return_value=Result()) as command:
            store = AzureStore('pokerattackfixture123')
            store.upload('accepted.json', '/runner/private/pointer', etag='"reviewed-etag"')
            args = command.call_args.args[0]
            self.assertIn('--if-match', args)
            self.assertEqual(args[args.index('--auth-mode') + 1], 'login')
            self.assertEqual(args[args.index('--container-name') + 1], 'pokerattack-client-releases')
            self.assertNotIn('--account-key', args); self.assertNotIn('--sas-token', args)
            with self.assertRaises(ValueError): store.upload('accepted.json', '/runner/private/pointer')
            with self.assertRaises(ValueError): store.info('../other-container')


if __name__ == '__main__': unittest.main(verbosity=2)
