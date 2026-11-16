#!/usr/bin/env python3
"""Privileged command boundary. Unimplemented capabilities fail closed.

State-changing handlers are activated only after their separate local tests and
rehearsal. This boundary grants no general command/path/environment authority.
"""
import os
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from release_commands import authorize


def dispatch(role, arguments, handlers):
    action, identities = authorize(role, arguments)
    if action not in handlers:
        raise ValueError('Capability implementation not activated')
    return handlers[action](*identities)


def main():
    if os.geteuid() != 0 or len(sys.argv) < 2:
        raise ValueError('Expected privileged fixed capability')
    from secure_state import bounded_json
    from release_verifier import issue, revoke, status_module
    from release_lease import begin, deploy, rollback, resume, finish
    from release_evidence import import_evidence, promote
    import client_lease
    def packet():
        return bounded_json(sys.stdin.buffer.read(65537))
    def credential(run, attempt):
        return issue(run, attempt, packet())
    if sys.argv[1] == 'operator-bootstrap':
        bootstrap_actions={'client-authorize','client-published','drain-release','client-status','client-acceptance'}
        if len(sys.argv)==6 and sys.argv[2] in bootstrap_actions and sys.argv[3]=='production':
            from host_manifests import pattern
            from bootstrap_control import intent,published,drain,client_status
            pattern(sys.argv[4],r'[1-9][0-9]{0,19}');pattern(sys.argv[5],r'[1-9][0-9]{0,4}')
            run,attempt=sys.argv[4:6]
            action=sys.argv[2]
            if action=='client-authorize':result=intent(run,attempt,packet())
            elif action=='client-published':
                # Legacy source callers on the disposable fixture may omit the
                # hash only while a single unresolved intent exists. New clients
                # always supply it, including read-only lost-response recovery.
                raw=sys.stdin.buffer.read(65537)
                if raw:
                    from host_manifests import fields
                    value=bounded_json(raw);fields(value,('sha256',));expected=value['sha256']
                else:expected=None
                result=published(run,attempt,expected)
            elif action=='drain-release':result=drain(run,attempt)
            else:result=client_status(run,attempt,acceptance=action=='client-acceptance')
            print(json.dumps(result,separators=(',',':')))
            return
        action, identities = authorize('deployment', sys.argv[2:])
        if action != 'begin-release':
            raise ValueError('Root bootstrap only begins the transitional lease')
        result = begin(*identities, packet(), bootstrap=True)
        print(json.dumps(result, separators=(',', ':')))
        return
    result = dispatch(sys.argv[1], sys.argv[2:], {
        'status': lambda: status_module.status('production'),
        'verifier-credential': credential,
        'revoke-verifier': revoke,
        'begin-release': lambda run, attempt: begin(run, attempt, packet()),
        'begin-rollback': lambda run, attempt: begin(run, attempt, packet(), rollback=True),
        'deploy': deploy,
        'rollback': rollback,
        'resume-release': resume,
        'resume-verification': resume,
        'finish-release': finish,
        'import-evidence': lambda run, attempt: import_evidence(run, attempt, sys.stdin.buffer.read(65537)),
        'promote': promote,
        'begin-client': lambda run, attempt: client_lease.begin(run, attempt, packet()),
        'finish-client': lambda run, attempt: client_lease.finish(run, attempt, packet()),
        'resume-client': client_lease.resume,
        'begin-client-recovery': lambda run, attempt: client_lease.begin(run, attempt, packet(), recover=True),
    })
    print(json.dumps(result, separators=(',', ':')))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        # Do not echo stdin, provider exceptions, paths, credentials or payloads.
        sys.exit('Release command rejected; inspect structured status before retrying.')
