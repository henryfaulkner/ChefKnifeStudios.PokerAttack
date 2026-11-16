"""Credential-free failure-stage extraction shared by operator and CI fixtures."""
import re

FAILURE_STAGES = frozenset(('private-context', 'private-context-validation', 'browser-launch', 'gameplay', 'context-validation', 'tls-client-identity',
    'served-release-identity', 'served-client-settings', 'served-client-assets', 'public-api-readiness', 'browser-signalr',
    'previous-attempt-owned-session-leave', 'lobby-create', 'lobby-join', 'game-start', 'game-groups', 'initial-private-hand',
    'private-message-isolation', 'multiplayer-completion', 'solo-completion', 'owned-session-leave'))


def failed_stage(raw):
    if len(raw) > 4096:
        return 'unknown'
    for line in raw.decode('utf-8', errors='replace').splitlines():
        match = re.fullmatch(r'Release verification failed at ([a-z-]+); no promotable evidence emitted', line)
        if match and match[1] in FAILURE_STAGES:
            return match[1]
    return 'unknown'
