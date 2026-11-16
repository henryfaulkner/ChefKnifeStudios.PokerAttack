"""Stable-check change detection and main-only production eligibility."""
import json
import os
from pathlib import Path
import re
import subprocess

REPOSITORY = 'henryfaulkner/ChefKnifeStudios.PokerAttack'
ROOT_FILES = {'.dockerignore', '.gitignore', 'global.json', 'Directory.Build.props', 'Directory.Build.targets',
              'Directory.Packages.props', 'NuGet.config', 'nuget.config', 'package.json', 'package-lock.json'}


def classify(paths):
    client = server = False
    for path in paths:
        if path.startswith('docs/') or path.lower().endswith('.md'): continue
        if path in ROOT_FILES or path.startswith(('src/ChefKnifeStudios.PokerAttack.Shared/',
                'src/ChefKnifeStudios.PokerAttack.ServiceDefaults/', 'tests/', 'deploy/release/', '.github/actions/', '.config/')):
            client = server = True
        elif path.startswith(('src/Client/', 'deploy/prepare-production-client')) or path == '.github/workflows/client.yml':
            client = True
        elif path.startswith(('src/Server/', 'deploy/linode/', 'deploy/monitoring/')) or path in (
                '.github/workflows/server.yml', '.github/workflows/migration-ci.yml', 'deploy/check-isolated-baseline.py'):
            server = True
        elif path.startswith('deploy/prepare-client-artifact'):
            client = server = True
        elif path.startswith(('.claude/', '.agents/')):
            continue
        else:
            # New build/dependency inputs must not silently evade validation.
            client = server = True
    return {'client': client, 'server': server}


def eligible(event_name, ref, repository):
    return event_name in ('push', 'workflow_dispatch') and ref == 'refs/heads/main' and repository == REPOSITORY


def changed_files(event_name, event, source):
    if not re.fullmatch(r'[a-f0-9]{40}', source): raise ValueError('Invalid source identity')
    if event_name == 'workflow_dispatch': return None
    base = event['pull_request']['base']['sha'] if event_name == 'pull_request' else event.get('before')
    if base is not None and not re.fullmatch(r'[a-f0-9]{40}', base): raise ValueError('Invalid base identity')
    if base and base != '0' * 40:
        return subprocess.check_output(['git', 'diff', '--name-only', base, source], text=True).splitlines()
    return subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', source], text=True).splitlines()


def main():
    event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text())
    name, source, ref = (os.environ[key] for key in ('GITHUB_EVENT_NAME', 'GITHUB_SHA', 'GITHUB_REF'))
    paths = changed_files(name, event, source)
    result = {'client': True, 'server': True} if paths is None else classify(paths)
    result['production_eligible'] = eligible(name, ref, event.get('repository', {}).get('full_name'))
    with Path(os.environ['GITHUB_OUTPUT']).open('a', encoding='utf-8') as stream:
        for key, value in result.items(): stream.write(f'{key}={str(value).lower()}\n')
    print(json.dumps(result))


if __name__ == '__main__': main()
