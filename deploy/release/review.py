"""Bind GitHub's explicit environment decision to an immutable release packet.

GitHub review history does not provide a reliable decision timestamp. We use the
packet's EARLIER validity anchor, require its exact hash in the approved comment,
and never renew that anchor. An approval can only shorten this 30-minute window.
No runner-start timestamp is represented as the actual human decision time.
"""
import hashlib
import json
import time

from client_publish import trusted_context
from manifests import validate_document
from server_images import execution


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def reset_packet(packet, env, now=None):
    now = int(time.time()) if now is None else now
    trusted_context(env)
    source, workflow = execution(env)
    manifest = validate_document('server-manifest', packet['manifest'])
    operation = packet.get('operation', 'release')
    if operation not in ('release', 'rollback') or operation != env.get('SERVER_OPERATION', 'release'):
        raise ValueError('Wrong explicit release operation')
    if packet.get('execution', workflow) != workflow:
        raise ValueError('Packet belongs to another execution')
    if operation == 'release' and (manifest['source_sha'] != source or manifest['workflow'] != workflow):
        raise ValueError('Reset packet does not identify this trusted execution')
    start, end = packet['review_window_started_unix'], packet['review_window_expires_unix']
    if type(start) is not int or type(end) is not int or end != start + 1800 or not 0 <= now - start < 1800:
        raise ValueError('Reset packet validity window expired; prepare a new packet and obtain a new decision')
    expected_manifest = hashlib.sha256(canonical(manifest)).hexdigest()
    core = {key: value for key, value in packet.items() if key != 'reset_review'}
    digest = hashlib.sha256(canonical(core)).hexdigest()
    if (packet['manifest_sha256'] != expected_manifest or packet['reset_review'] != {
            'packet_sha256': digest, 'comment': f"ACK_RESET {digest} {workflow['run_id']}/{workflow['run_attempt']}"}):
        raise ValueError('Reset packet identity changed after review preparation')
    return manifest


def approved(reviews, environment, comment):
    if not isinstance(reviews, list) or len(reviews) > 1000:
        raise ValueError('Invalid environment review history')
    matching = [review for review in reviews if review.get('comment') == comment and
                any(item.get('name') == environment for item in review.get('environments', []))]
    if any(review.get('state') == 'rejected' for review in matching):
        raise ValueError('Release-specific review was rejected')
    matches = [review for review in matching if review.get('state') == 'approved' and review.get('user', {}).get('login')]
    if not matches:
        raise ValueError('Missing explicit release-specific environment approval comment')
    return {'environment': environment, 'reviewer': matches[-1]['user']['login'], 'comment': comment,
            'decision_timestamp': None, 'timestamp_note': 'GitHub review API does not expose decision time; immutable packet validity anchor is used conservatively.'}
