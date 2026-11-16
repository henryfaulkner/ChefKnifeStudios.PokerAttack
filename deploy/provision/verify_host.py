"""Verify installed forced-command boundaries before storing environment keys."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess

from install_host import ROOT, STATE, PROFILES, ROLE_ENVIRONMENTS, Transport
from github import REPOSITORY, api


def secret(name, raw, environment):
    result = subprocess.run(['gh', 'secret', 'set', name, '--repo', REPOSITORY, '--env', environment],
                            input=raw, capture_output=True, timeout=60)
    if result.returncode:
        raise ValueError('Environment secret installation rejected: ' + environment + '/' + name)


def main():
    state = json.loads(STATE.read_bytes())
    if state['phase'] != 'INSTALLED / NO APPLICATION RESTART':
        raise ValueError('Inspect incomplete host installation first')
    report = {'task': 'I03', 'state': 'IN_PROGRESS', 'negative_checks': [], 'installed_secret_names': [],
              'source_bundle_sha256': state['source_bundle_sha256'], 'production_restart': False}
    try:
        trusted = subprocess.run(['ssh-keygen', '-F', '170.187.142.115', '-f', str(Path.home() / '.ssh/known_hosts')],
                                 capture_output=True, timeout=15)
        if trusted.returncode:
            raise ValueError('Missing pre-established host trust; never use runtime keyscan')
        known = b'\n'.join(line for line in trusted.stdout.splitlines() if line and not line.startswith(b'#')) + b'\n'
        hosts = ROOT / 'artifacts/I03-known_hosts'
        hosts.write_bytes(known)
        transports = {role: Transport(role, state['keys'][role], hosts) for role in PROFILES}
        initial = transports['status'].call('status production')
        if (not initial['consistent'] or not initial['readiness']['ready'] or initial['lease'] is not None or
                initial['transaction'] or initial['pairs']['pending'] is not None or initial['maintenance']['active']):
            raise ValueError('Unexpected production state after tooling installation')
        report['before_status'] = initial
        digest, checksum = 'sha256:' + 'a' * 64, 'b' * 64
        cross = {'status': f'deploy production {digest} {checksum} 1 1',
                 'deployment': f'promote production {digest} {checksum} 1 1',
                 'evidence': f'deploy production {digest} {checksum} 1 1',
                 'promotion': 'import-evidence production 1 1',
                 'client-lease': 'begin-release production 1 1'}
        for role, (user, _) in PROFILES.items():
            observed = transports[role].call('status production')
            if observed['runtime']['container_id'] != initial['runtime']['container_id']:
                raise ValueError('Serving API changed during role checks')
            commands = ('id', 'status pilot', 'status production extra', 'status production; id',
                        'operator-bootstrap client-status production 1 1', cross[role])
            for command in commands:
                result = subprocess.run(['ssh', '-T', '-F', os.devnull, '-i', state['keys'][role],
                                         '-o', 'UserKnownHostsFile=' + str(hosts), '-o', 'GlobalKnownHostsFile=' + os.devnull,
                                         '-o', 'StrictHostKeyChecking=yes', '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes',
                                         '-o', 'IdentityAgent=none', '-o', 'ClearAllForwardings=yes',
                                         user + '@170.187.142.115', command], capture_output=True, timeout=20)
                if result.returncode in (0, 255):
                    raise ValueError('Expected explicit role rejection; authentication failure is not proof')
                report['negative_checks'].append({'role': role, 'command': command, 'exit_code': result.returncode})
        final = transports['status'].call('status production')
        if (final['runtime'] != initial['runtime'] or final['pairs'] != initial['pairs'] or final['lease'] is not None or
                final['transaction'] or final['maintenance']['active']):
            raise ValueError('Role checks changed production state')
        report['after_status'] = final
        # Known-host entries are public trust material; workflows intentionally
        # read them from vars, whereas private identities use environment secrets.
        args = ['gh', 'variable', 'set', 'LINODE_KNOWN_HOSTS', '--repo', REPOSITORY]
        if subprocess.run(args, input=known, capture_output=True, timeout=60).returncode:
            raise ValueError('Trusted host variable installation rejected')
        for role, environments in ROLE_ENVIRONMENTS.items():
            raw = Path(state['keys'][role]).read_bytes()
            for environment in environments:
                secret(PROFILES[role][1], raw, environment)
                report['installed_secret_names'].append(environment + '/' + PROFILES[role][1])
        for environment in set(env for envs in ROLE_ENVIRONMENTS.values() for env in envs):
            names = {item['name'] for item in api('repos/' + REPOSITORY + '/environments/' + environment + '/secrets')['secrets']}
            expected = {PROFILES[role][1] for role, envs in ROLE_ENVIRONMENTS.items() if environment in envs}
            if not expected.issubset(names):
                raise ValueError('Environment secret name readback differs')
        report.update(state='INSTALLED / REMOTE CAPABILITIES VERIFIED',
                      known_hosts_sha256=hashlib.sha256(known).hexdigest(),
                      private_keys_storage='Dedicated operator SSH files outside repository; scoped GitHub environment secrets.',
                      legacy_recovery_reference='I03-host-installation.json saved_legacy_tooling; established root access preserved')
        state['phase'] = 'remote-verified/environment-secrets-installed'
        STATE.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    except Exception as error:
        report['error_type'] = type(error).__name__
        raise
    finally:
        report['recorded_utc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        report['verifier_source_sha256'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
        (ROOT / 'docs/deployment-evidence/I03-capability-verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print('Five remote roles verified; 30 cross-role/shell/pilot/bootstrap denials; environment keys installed. API unchanged.')


if __name__ == '__main__':
    main()
