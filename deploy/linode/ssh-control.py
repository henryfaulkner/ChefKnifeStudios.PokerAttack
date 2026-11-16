#!/usr/bin/env python3
"""Forced command: no shell invocation, role fixed in root-owned authorized_keys."""
import os
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from release_commands import EXECUTABLES, parse_ssh


def main():
    if len(sys.argv) != 2:
        raise ValueError('Expected fixed capability')
    role = sys.argv[1]
    arguments = parse_ssh(role, os.environ.get('SSH_ORIGINAL_COMMAND', ''))
    # The status helper has a no-arguments sudoers contract.
    if arguments == ['status', 'production']:
        return subprocess.call(['/usr/bin/sudo', '-n', '/usr/local/sbin/pokerattack-status'])
    if arguments == ['rollback-target', 'production']:
        return subprocess.call(['/usr/bin/sudo', '-n', '/usr/local/sbin/pokerattack-rollback-target'])
    return subprocess.call(['/usr/bin/sudo', '-n', EXECUTABLES[role], *arguments])


if __name__ == '__main__':
    try:
        sys.exit(main())
    except ValueError:
        sys.exit('Only the fixed production capability commands are allowed.')
