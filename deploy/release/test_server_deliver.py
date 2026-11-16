import copy
import datetime
import time
import unittest
from unittest.mock import patch

import review
import server_deliver as delivery
import server_manifest
import test_server_manifest as fixture_data
from ssh_client import UnknownOutcome
ENV, SOURCE = fixture_data.ENV, fixture_data.SOURCE


class Host:
    def __init__(self, live, packet):
        self.live, self.packet, self.calls, self.lost = live, packet, [], None

    def call(self, command, packet=None, timeout=None):
        self.calls.append((command, copy.deepcopy(packet), timeout))
        manifest = self.packet['manifest']
        if command == 'status production': return copy.deepcopy(self.live)
        if command.startswith('begin-release '):
            self.live['lease'] = {'kind': 'server', 'phase': 'drained', 'source_sha': SOURCE,
                'run_id': 42, 'run_attempt': 1, 'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256']}
            self.live['maintenance']['active'] = True
            if self.lost == 'begin': raise UnknownOutcome('Lost begin response')
            return {'phase': 'drained'}
        if command.startswith('deploy '):
            self.live['lease']['phase'] = 'pending'
            pair = {'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256']}
            self.live['pairs']['active'] = self.live['pairs']['pending'] = pair
            self.live['runtime'] = {'image_id': manifest['api']['local_image_id'], 'source_revision': SOURCE}
            if self.lost == 'deploy': raise UnknownOutcome('Lost deploy response')
            return {'phase': 'pending'}
        raise AssertionError('Unexpected authority/command')


class DeliveryTests(unittest.TestCase):
    def setUp(self):
        fixture = fixture_data.ServerManifestTests(); fixture.setUp()
        self.live = fixture.live
        self.packet = fixture.resolve()
        self.env = {**ENV, 'APPROVED_CONFIG_SHA256': 'f' * 64, 'RELEASE_DRAIN_SECONDS': '0'}
        self.reviews = [{'state': 'approved', 'comment': self.packet['reset_review']['comment'],
                         'environments': [{'name': 'server-linode'}], 'user': {'login': 'operator'}}]
        self.host = Host(self.live, self.packet)

    def api(self, path):
        return {'object': {'sha': SOURCE}} if path == 'git/ref/heads/main' else self.reviews

    def deliver(self): return delivery.deliver(self.packet, self.env, self.host, self.host, self.api)

    def test_exact_review_delivers_only_digest_and_leaves_release_pending(self):
        result = self.deliver()
        self.assertEqual(result['phase'], 'pending')
        self.assertIs(result['overall_release_complete'], False)
        mutations = [call for call in self.host.calls if call[0] != 'status production']
        self.assertEqual(len(mutations), 2)
        self.assertEqual(mutations[0][0], 'begin-release production 42 1')
        self.assertEqual(mutations[1][0], f"deploy production {self.packet['manifest']['api']['digest']} {'f'*64} 42 1")
        ack = mutations[0][1]['acknowledgment']
        self.assertEqual(ack['approved_unix'], self.packet['review_window_started_unix'])
        self.assertEqual(ack['expires_unix'], self.packet['review_window_expires_unix'])
        self.assertIsNone(result['review']['decision_timestamp'])
        self.assertTrue(self.live['maintenance']['active'])

    def test_missing_wrong_or_rejected_release_review_never_begins(self):
        good = copy.deepcopy(self.reviews)
        for change in ('missing', 'wrong-comment', 'wrong-environment', 'rejected'):
            self.reviews = copy.deepcopy(good)
            if change == 'missing': self.reviews = []
            if change == 'wrong-comment': self.reviews[0]['comment'] = 'approved'
            if change == 'wrong-environment': self.reviews[0]['environments'][0]['name'] = 'server-promotion'
            if change == 'rejected': self.reviews[0]['state'] = 'rejected'
            with self.subTest(change=change), self.assertRaises(ValueError): self.deliver()
        self.assertEqual(self.host.calls, [])

    def test_packet_tamper_stale_review_config_or_source_blocks_mutation(self):
        original = copy.deepcopy(self.packet)
        self.packet['observed_sessions']['activeGames'] += 1
        with self.assertRaises(ValueError): self.deliver()
        self.packet = original
        with patch.object(review.time, 'time', return_value=self.packet['review_window_expires_unix']):
            with self.assertRaises(ValueError): self.deliver()
        self.env['APPROVED_CONFIG_SHA256'] = 'e' * 64
        with self.assertRaises(ValueError): self.deliver()
        self.env['APPROVED_CONFIG_SHA256'] = 'f' * 64
        with self.assertRaises(ValueError): delivery.deliver(self.packet, self.env, self.host, self.host,
            lambda path: {'object': {'sha': 'b' * 40}} if path == 'git/ref/heads/main' else self.reviews)
        self.assertEqual(self.host.calls, [])

    def test_unknown_begin_or_deploy_is_never_retried_or_reopened(self):
        for where in ('begin', 'deploy'):
            self.setUp(); self.host.lost = where
            with self.subTest(where=where), self.assertRaises(UnknownOutcome): self.deliver()
            mutations = [call[0].split()[0] for call in self.host.calls if call[0] != 'status production']
            self.assertEqual(mutations, ['begin-release'] if where == 'begin' else ['begin-release', 'deploy'])
            self.assertTrue(self.live['maintenance']['active'])

    def test_expiration_during_drain_never_refreshes_or_deploys(self):
        original = self.host.call
        def delayed(command, *args, **kwargs):
            result = original(command, *args, **kwargs)
            if command.startswith('begin-release '): self.clock = self.packet['review_window_expires_unix']
            return result
        self.clock = self.packet['review_window_started_unix']
        with patch.object(self.host, 'call', side_effect=delayed), patch.object(review.time, 'time', side_effect=lambda: self.clock):
            with self.assertRaises(ValueError): self.deliver()
        self.assertFalse(any(call[0].startswith('deploy ') for call in self.host.calls))
        self.assertTrue(self.live['maintenance']['active'])

    def test_changed_serving_pair_or_existing_client_lease_never_begins(self):
        for where in ('pair', 'client-slot'):
            self.setUp()
            if where == 'pair':
                pair = {'digest': 'sha256:' + 'e' * 64, 'config_sha256': '4' * 64}
                self.live['pairs']['active'] = self.live['pairs']['known-good'] = pair
            else:
                self.live['lease'] = {'kind': 'client'}
            with self.subTest(where=where), self.assertRaises(ValueError): self.deliver()
            self.assertFalse(any(call[0].startswith('begin-release ') for call in self.host.calls))

    def test_unexpected_begin_or_mismatched_lease_blocks_replacement(self):
        for where in ('response', 'owner'):
            self.setUp()
            original = self.host.call
            def changed(command, *args, **kwargs):
                result = original(command, *args, **kwargs)
                if command.startswith('begin-release '):
                    if where == 'response': result['phase'] = 'unknown'
                    else: self.live['lease']['run_id'] = 99
                return result
            with patch.object(self.host, 'call', side_effect=changed):
                with self.subTest(where=where), self.assertRaises(ValueError): self.deliver()
            self.assertFalse(any(call[0].startswith('deploy ') for call in self.host.calls))

    def test_wrong_pending_runtime_is_failed_without_promotion_or_reopening(self):
        original = self.host.call
        def wrong_image(command, *args, **kwargs):
            result = original(command, *args, **kwargs)
            if command.startswith('deploy '): self.live['runtime']['image_id'] = 'sha256:' + 'f' * 64
            return result
        with patch.object(self.host, 'call', side_effect=wrong_image):
            with self.assertRaises(ValueError): self.deliver()
        self.assertTrue(self.live['maintenance']['active'])
        self.assertEqual(self.live['lease']['phase'], 'pending')
        self.assertFalse(any('promote ' in call[0] or 'finish-' in call[0] for call in self.host.calls))


if __name__ == '__main__': unittest.main(verbosity=2)
