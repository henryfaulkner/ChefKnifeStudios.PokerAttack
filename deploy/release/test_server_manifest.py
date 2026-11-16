import copy
import datetime
import json
from pathlib import Path
import unittest

import server_manifest as manifests
from server_images import REPOS, REGISTRY, execution

SOURCE = 'c' * 40
ENV = {'GITHUB_SHA': SOURCE, 'GITHUB_RUN_ID': '42', 'GITHUB_RUN_ATTEMPT': '1',
       'GITHUB_REF': 'refs/heads/main', 'GITHUB_REPOSITORY': 'henryfaulkner/ChefKnifeStudios.PokerAttack',
       'GITHUB_EVENT_NAME': 'push', 'DEPLOYMENT_AUTOMATION_ENABLED': 'true'}


class ServerManifestTests(unittest.TestCase):
    def setUp(self):
        self.schema = json.loads((Path(__file__).parent / 'schema-prerequisite.json').read_text())
        self.published = {'format': 'pokerattack-published-images-v1', 'source_sha': SOURCE,
            'workflow': execution(ENV)[1], 'tested_archive_sha256': 'a' * 64,
            'images': {name: {'repository': REGISTRY + '/' + repo, 'digest': 'sha256:' + digit * 64,
                'local_image_id': 'sha256:' + digit * 64, 'tag': SOURCE, 'source_revision': SOURCE,
                'platform': 'linux/amd64'} for name, repo, digit in [('api', REPOS['api'], '1'), ('migration', REPOS['migration'], '2')]}}
        pair = {'digest': 'sha256:' + '3' * 64, 'config_sha256': '4' * 64}
        self.live = {'protocol_version': 1, 'target': 'production', 'consistent': True, 'transaction': False,
            'sampled_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'readiness': {'ready': True},
            'maintenance': {'active': False}, 'lease': None, 'sessions': {'activeGames': 1, 'lobbies': 2, 'soloGames': 0},
            'pairs': {'active': pair, 'known-good': pair, 'pending': None}}

    def resolve(self, env=ENV):
        return manifests.resolve(self.published, self.schema, self.live, 'f' * 64, env)

    def test_manifest_binds_exact_images_schema_source_and_observed_recovery(self):
        packet = self.resolve()
        self.assertEqual(packet['manifest']['api'], self.published['images']['api'])
        self.assertEqual(packet['manifest']['schema'], self.schema)
        self.assertEqual(packet['recovery_pair'], self.live['pairs']['known-good'])
        self.assertEqual(len(packet['manifest_sha256']), 64)
        self.assertNotIn('ConnectionStrings', json.dumps(packet))
        resumed = self.resolve({**ENV, 'GITHUB_RUN_ATTEMPT': '2'})
        self.assertEqual(resumed['manifest']['workflow']['run_attempt'], 2)
        self.assertIn('/attempts/1', resumed['manifest']['verification_references'][0])

    def test_wrong_source_revision_repository_or_config_cannot_form_packet(self):
        original = copy.deepcopy(self.published)
        for field, value in [('repository', 'unrelated.azurecr.io/app'), ('source_revision', 'b' * 40), ('platform', 'linux/arm64')]:
            self.published = copy.deepcopy(original); self.published['images']['api'][field] = value
            with self.subTest(field=field), self.assertRaises(Exception): self.resolve()
        self.published = original
        self.published['source_sha'] = 'b' * 40
        with self.assertRaises(ValueError): self.resolve()
        self.published = original
        with self.assertRaises(ValueError): manifests.resolve(self.published, self.schema, self.live, '../config.env', ENV)

    def test_missing_new_or_unapproved_schema_prerequisite_is_rejected(self):
        for mutation in ('missing', 'extra', 'unapproved'):
            schema = copy.deepcopy(self.schema)
            if mutation == 'missing': schema['required_migrations'].pop()
            if mutation == 'extra': schema['required_migrations'].append('20261115110000_NewSchema')
            if mutation == 'unapproved': schema['compatible_with_known_good'] = False
            with self.subTest(mutation=mutation), self.assertRaises(Exception):
                manifests.resolve(self.published, schema, self.live, 'f' * 64, ENV)

    def test_pending_transaction_lease_stale_status_and_nonmain_block_resolution(self):
        original = copy.deepcopy(self.live)
        for field in ('pending', 'transaction', 'lease', 'stale'):
            self.live = copy.deepcopy(original)
            if field == 'pending': self.live['pairs']['pending'] = self.live['pairs']['active']
            if field == 'transaction': self.live['transaction'] = True
            if field == 'lease': self.live['lease'] = {'kind': 'client'}
            if field == 'stale': self.live['sampled_utc'] = '2020-01-01T00:00:00Z'
            with self.subTest(field=field), self.assertRaises(ValueError): self.resolve()
        self.live = original
        with self.assertRaises(ValueError): self.resolve({**ENV, 'GITHUB_REF': 'refs/heads/feature'})

    def test_missing_observation_does_not_claim_zero_sessions(self):
        self.live['sessions'] = None
        with self.assertRaises(ValueError): self.resolve()


if __name__ == '__main__': unittest.main(verbosity=2)
