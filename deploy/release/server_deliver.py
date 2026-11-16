"""Fresh reset review -> constrained begin/deploy -> pending, never promotion.

Unknown SSH outcomes stop mutation. The CLI may collect one diagnostic status
after failure, but never retries deployment, rolls back or reopens automatically.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path

from client_publish import github, validate_status
from manifests import load_document
from review import approved, canonical, reset_packet
from ssh_client import transport


def owned(status, packet):
    manifest = packet['manifest']
    workflow = packet.get('execution', manifest['workflow'])
    expected = {'kind': 'server', 'run_id': workflow['run_id'], 'run_attempt': workflow['run_attempt'],
                'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256'], 'source_sha': manifest['source_sha']}
    lease = status.get('lease') or {}
    if any(lease.get(key) != value for key, value in expected.items()) or not status['consistent'] or status['transaction']:
        raise ValueError('Host release slot does not match this reviewed packet')
    if status['maintenance']['active'] is not True:
        raise ValueError('Matching maintenance gate is not closed')
    return lease


def deliver(packet, env, status_host, deployment_host, api=github):
    manifest = reset_packet(packet, env)
    rollback = packet.get('operation') == 'rollback'
    workflow = packet.get('execution', manifest['workflow'])
    if not rollback and env.get('APPROVED_CONFIG_SHA256') != manifest['config_sha256']:
        raise ValueError('Approved configuration changed after packet resolution')
    run, attempt = workflow['run_id'], workflow['run_attempt']
    def recheck():
        reset_packet(packet, env)
        if api('git/ref/heads/main')['object']['sha'] != env['GITHUB_SHA']:
            raise ValueError('Candidate is behind current main')
        return approved(api(f'actions/runs/{run}/approvals'), 'server-linode', packet['reset_review']['comment'])
    decision = recheck()
    live = status_host.call('status production')
    if rollback:
        target = status_host.call('rollback-target production')
        fields = ('manifest_sha256', 'current_pair', 'pending_pair', 'recovery_pair', 'recovery_from')
        if (not live['consistent'] or live['transaction'] or any(target[key] != packet[key] for key in fields)):
            raise ValueError('Recorded recovery state changed since review')
    else:
        validate_status(live)
        if live['lease'] is not None or live['pairs']['active'] != packet['current_pair'] or live['pairs']['known-good'] != packet['recovery_pair']:
            raise ValueError('Current/recovery identity changed since reset packet preparation')
    drain = env.get('RELEASE_DRAIN_SECONDS', '300')
    if not isinstance(drain, str) or not drain.isascii() or not drain.isdecimal() or not 0 <= int(drain) <= 300:
        raise ValueError('Invalid bounded drain duration')
    ack = {'action': 'rollback' if rollback else 'deploy', 'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256'],
           'source_sha': manifest['source_sha'], 'run_id': run, 'run_attempt': attempt,
           'approved_unix': packet['review_window_started_unix'], 'expires_unix': packet['review_window_expires_unix'],
           'drain_seconds': int(drain), 'policy': 'reset-at-deadline'}
    body = {'manifest': manifest, 'acknowledgment': ack}
    if rollback: body['recovery_from'] = packet['recovery_from']
    verb = 'begin-rollback' if rollback else 'begin-release'
    begin = deployment_host.call(f'{verb} production {run} {attempt}', body, timeout=390)
    if begin.get('phase') not in ('drained', 'reset-approved'):
        raise ValueError('Unexpected begin outcome; inspect status without retrying mutation')
    live = status_host.call('status production')
    lease = owned(live, packet)
    if (lease['phase'] not in ('drained', 'reset-approved') or live['pairs']['active'] != packet['current_pair']
            or live['pairs']['pending'] != (packet.get('pending_pair') if rollback else None)):
        raise ValueError('Host state changed before replacement')
    recheck()  # Original validity window, source and decision; never refreshed.
    command = f"rollback production {run} {attempt}" if rollback else f"deploy production {manifest['api']['digest']} {manifest['config_sha256']} {run} {attempt}"
    result = deployment_host.call(command, timeout=1040)
    live = status_host.call('status production')
    lease = owned(live, packet)
    expected = {'digest': manifest['api']['digest'], 'config_sha256': manifest['config_sha256']}
    active = live['pairs']['active']
    if (result.get('phase') != 'pending' or lease['phase'] != 'pending' or live['readiness']['ready'] is not True
            or not active or active != live['pairs']['pending'] or any(active[key] != value for key, value in expected.items())
            or live['runtime']['image_id'] != manifest['api']['local_image_id'] or live['runtime']['source_revision'] != manifest['source_sha']):
        raise ValueError('Candidate is not the exact tested ready pending pair; maintenance remains closed')
    return {'format': 'pokerattack-delivery-v1', 'phase': 'pending', 'overall_release_complete': False,
            'manifest_sha256': packet['manifest_sha256'], 'reset_packet_sha256': packet['reset_review']['packet_sha256'],
            'source_sha': manifest['source_sha'], **expected, 'workflow': workflow, 'operation': 'rollback' if rollback else 'release', 'review': decision,
            'validity_anchor_unix': ack['approved_unix'], 'expires_unix': ack['expires_unix']}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('packet', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    packet = load_document(args.packet.read_bytes())
    with transport('status') as status, transport('deployment') as deployment:
        try:
            result = deliver(packet, os.environ, status, deployment)
        except Exception:
            # Inspect only. Unknown outcomes never cause another mutation call.
            try:
                diagnostic = status.call('status production')
                args.output.write_text(json.dumps({'phase': 'failed-or-unknown', 'overall_release_complete': False,
                                                   'status': diagnostic}) + '\n', encoding='utf-8')
            except Exception: pass
            raise
        args.output.write_text(json.dumps(result, sort_keys=True) + '\n', encoding='utf-8')


if __name__ == '__main__':
    try: main()
    except Exception:
        print('Server delivery failed or is uncertain. Inspect structured status before any further mutation; maintenance was not automatically cleared.')
        raise SystemExit(1)
