#!/usr/bin/env python3
"""Internal fixed-path guard; not a sudo/SSH capability."""
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from release_lease import prepare_legacy

if __name__ == '__main__':
    try:
        if os.geteuid() != 0 or len(sys.argv) != 5 or sys.argv[2] != 'production':
            raise ValueError('Invalid internal guard invocation')
        prepare_legacy(sys.argv[1], sys.argv[3], sys.argv[4])
    except Exception:
        sys.exit('Fresh matching release approval required before replacement.')
