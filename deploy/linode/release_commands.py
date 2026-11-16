"""Shared strict grammar for both sides of the sudo privilege boundary."""
import re

DIGEST = r'sha256:[a-f0-9]{64}'
HASH = r'[a-f0-9]{64}'
RUN = r'[1-9][0-9]{0,19}'
ATTEMPT = r'[1-9][0-9]{0,4}'
GRAMMAR = {
    'status': {'status': (), 'rollback-target': ()},
    'deployment': {
        'status': (), 'begin-release': (RUN, ATTEMPT),
        'begin-rollback': (RUN, ATTEMPT),
        'deploy': (DIGEST, HASH, RUN, ATTEMPT), 'rollback': (RUN, ATTEMPT),
        'resume-release': (RUN, ATTEMPT, ATTEMPT),
    },
    'evidence': {'status': (), 'verifier-credential': (RUN, ATTEMPT), 'import-evidence': (RUN, ATTEMPT),
                 'revoke-verifier': (RUN, ATTEMPT), 'resume-verification': (RUN, ATTEMPT, ATTEMPT)},
    'promotion': {'status': (), 'promote': (DIGEST, HASH, RUN, ATTEMPT), 'finish-release': (RUN, ATTEMPT)},
    'client-lease': {'status': (), 'begin-client': (RUN, ATTEMPT), 'finish-client': (RUN, ATTEMPT),
                     'resume-client': (RUN, ATTEMPT, ATTEMPT), 'begin-client-recovery': (RUN, ATTEMPT)},
}
EXECUTABLES = {role: '/usr/local/sbin/pokerattack-' + role for role in GRAMMAR}


def authorize(role, arguments):
    if role not in GRAMMAR or len(arguments) < 2:
        raise ValueError('Unknown capability/command')
    action, target, *identities = arguments
    if target != 'production' or action not in GRAMMAR[role]:
        raise ValueError('Capability denied')
    patterns = GRAMMAR[role][action]
    if len(identities) != len(patterns) or any(not re.fullmatch(pattern, value, flags=re.ASCII)
                                              for pattern, value in zip(patterns, identities)):
        raise ValueError('Invalid command identities')
    return action, identities


def parse_ssh(role, original):
    if not isinstance(original, str) or len(original) > 512 or not original.isascii():
        raise ValueError('Invalid command length/encoding')
    # Exact split deliberately rejects tabs, newlines, repeated/trailing spaces.
    arguments = original.split(' ')
    authorize(role, arguments)
    return arguments
