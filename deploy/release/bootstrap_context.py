"""Truthful root-operator context while routine production delivery is off.

No CLI flag changes the normal CI entry points. The operator adapter authenticates
through the established root-only bootstrap command and verifies the real main
build run. It never invents a GitHub review, main ref, build run or approval time.
"""
import hashlib
import time

from client_artifacts import identity_summary
from client_publish import github
from manifests import validate_document
from review import canonical
from server_images import execution, REPOSITORY


class Context:
    def __init__(self, source, workflow, client, operator, api=github, manifest=None):
        self.source, self.workflow = source, workflow
        self.client = validate_document('client-manifest', client)
        self.operator, self.api, self.manifest = operator, api, manifest

    def build(self, env, fresh_main=False):
        source, workflow = execution(env)
        if (source != self.source or workflow != self.workflow or
                env.get('GITHUB_REPOSITORY') != REPOSITORY or env.get('GITHUB_REF') != 'refs/heads/main' or
                env.get('GITHUB_EVENT_NAME') != 'workflow_dispatch' or env.get('DEPLOYMENT_AUTOMATION_ENABLED') != 'false'):
            raise ValueError('Root bootstrap requires actual main build provenance and routine delivery off')
        run = self.api(f"actions/runs/{workflow['run_id']}")
        if (run.get('head_sha') != source or run.get('head_branch') != 'main' or run.get('event') != 'workflow_dispatch'
                or run.get('status') != 'completed' or run.get('conclusion') != 'success'
                or run.get('run_attempt') != workflow['run_attempt']
                or run.get('repository', {}).get('full_name') != REPOSITORY
                or run.get('path') != '.github/workflows/migration-ci.yml'):
            raise ValueError('Actual successful main bootstrap build not established')
        client_workflow = self.client['workflow']
        if (self.client['source_sha'] != source or client_workflow['repository'] != REPOSITORY
                or client_workflow['run_id'] != workflow['run_id']
                or not 1 <= client_workflow['run_attempt'] <= workflow['run_attempt']):
            raise ValueError('First client is not the tested bootstrap build artifact')
        if fresh_main and self.api('git/ref/heads/main')['object']['sha'] != source:
            raise ValueError('Bootstrap candidate is behind current main')
        return workflow['run_id'], workflow['run_attempt']

    def candidate(self, env):
        run, attempt = self.build(env)
        if self.manifest is None:
            raise ValueError('No frozen bootstrap manifest')
        current = self.operator('client-status', run, attempt)
        manifest = validate_document('server-manifest', self.manifest)
        if (current.get('format') != 'pokerattack-bootstrap-client-status-v1'
                or current.get('client') != identity_summary(self.client)
                or current.get('source_sha') != manifest['source_sha']
                or current.get('pair', {}).get('digest') != manifest['api']['digest']
                or current.get('pair', {}).get('config_sha256') != manifest['config_sha256']):
            raise ValueError('Root-authenticated bootstrap publication/runtime differs')
        return run, attempt


def decision(record, action, comment, anchor, *, now=None):
    now = int(time.time()) if now is None else now
    if (not isinstance(record, dict) or set(record) != {'format', 'action', 'comment', 'received_unix'}
            or record['format'] != 'pokerattack-root-release-decision-v1' or record['action'] != action
            or record['comment'] != comment or type(record['received_unix']) is not int
            or not anchor <= record['received_unix'] <= now + 5 or not 0 <= now - anchor < 1800):
        raise ValueError('Fresh explicit release-specific operator decision required')
    return {'mechanism': 'root-operator decision', 'comment': comment,
            'decision_received_unix': record['received_unix'], 'validity_anchor_unix': anchor,
            'timestamp_note': 'Validity starts at original packet/proof, never at runner start or retry.'}


def packet_identity(packet):
    digest = hashlib.sha256(canonical({key: value for key, value in packet.items() if key != 'reset_review'})).hexdigest()
    workflow = packet['execution']
    if (packet['manifest_sha256'] != hashlib.sha256(canonical(packet['manifest'])).hexdigest()
            or packet['reset_review'] != {'packet_sha256': digest,
                'comment': f"ACK_RESET {digest} {workflow['run_id']}/{workflow['run_attempt']}"}
            or packet['review_window_expires_unix'] != packet['review_window_started_unix'] + 1800):
        raise ValueError('Immutable bootstrap packet changed')
    return packet
