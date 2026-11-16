"""Prepare an isolated build-only source review, preserving the user's tree.

Routine client/server workflows remain inert templates until baseline K02.
Existing migration CI owns stable validation and a manual main-only artifact
publication probe. No job has Linode/SWA credentials or deploy commands.
"""
import copy
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).parents[2]
sys.path.insert(0, str(ROOT / 'deploy/release'))
from test_workflows import WorkflowLoader
import yaml

WORKTREE = ROOT / 'artifacts/I02-bootstrap-source'
BRANCH = 'deployment/bootstrap-validation'


def main():
    if WORKTREE.exists():
        raise ValueError('Existing bootstrap review worktree requires inspection')
    expected = json.loads((ROOT / 'docs/deployment-evidence/I01-current-prerequisites.json').read_bytes())['main']['sha']
    current = subprocess.check_output(['git', 'rev-parse', 'origin/main'], cwd=ROOT, text=True).strip()
    if current != expected:
        raise ValueError('Main changed; inspect before preparing review')
    subprocess.run(['git', 'worktree', 'add', '-b', BRANCH, str(WORKTREE), 'origin/main'], cwd=ROOT, check=True)
    paths = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=ROOT).decode().split('\0')
    exclude = ('.claude/skills/', '.agents/skills/stepwise-instructions/')
    for name in paths:
        if not name or name.startswith(exclude) or name in ('.github/workflows/client.yml', '.github/workflows/server.yml'):
            continue
        source = ROOT / name
        if not source.is_file() or source.is_symlink():
            continue
        target = WORKTREE / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    templates = WORKTREE / 'deploy/release/workflow-templates'
    templates.mkdir(parents=True)
    for name in ('client.yml', 'server.yml'):
        (templates / name).write_bytes((ROOT / '.github/workflows' / name).read_bytes().replace(b'\r\n', b'\n'))
        # A future change must not accidentally publish a historical client path.
        target = WORKTREE / '.github/workflows' / name
        if target.exists():
            raise ValueError('Unexpected enabled delivery workflow in main; inspect before publication')
    client = yaml.load((templates / 'client.yml').read_text(), Loader=WorkflowLoader)
    server = yaml.load((templates / 'server.yml').read_text(), Loader=WorkflowLoader)
    changes = copy.deepcopy(server['jobs']['changes'])
    changes['outputs']['client'] = '${{ steps.detect.outputs.client }}'
    jobs = {'changes': changes,
            'isolated': copy.deepcopy(server['jobs']['isolated']),
            'checks': copy.deepcopy(client['jobs']['checks']),
            'package': copy.deepcopy(client['jobs']['package']),
            'server_validation': copy.deepcopy(server['jobs']['validation']),
            'client_validation': copy.deepcopy(client['jobs']['validation'])}
    jobs['package']['if'] = "needs.changes.outputs.client == 'true'"
    for step in jobs['client_validation']['steps']:
        if 'RESTORE' in step.get('env', {}):
            step['env']['RESTORE'] = 'false'
    publisher = copy.deepcopy(server['jobs']['publish'])
    publisher['name'] = 'Bootstrap artifact publication only'
    publisher['needs'] = ['changes', 'isolated', 'server_validation', 'client_validation']
    publisher['if'] = ("github.repository == 'henryfaulkner/ChefKnifeStudios.PokerAttack' && "
        "github.ref == 'refs/heads/main' && github.event_name == 'workflow_dispatch' && "
        "inputs.bootstrap_artifacts == true && needs.server_validation.result == 'success' && "
        "needs.client_validation.result == 'success' && vars.DEPLOYMENT_AUTOMATION_ENABLED == 'false' && "
        "vars.BOOTSTRAP_ARTIFACT_PUBLICATION_ENABLED == 'true'")
    for step in publisher['steps']:
        if 'server_images.py publish' in step.get('run', ''):
            step['env']['BOOTSTRAP_ARTIFACT_PUBLICATION_ENABLED'] = '${{ vars.BOOTSTRAP_ARTIFACT_PUBLICATION_ENABLED }}'
    jobs['publish_bootstrap'] = publisher
    workflow = {'name': 'Migration build and test',
                'on': {'pull_request': None, 'push': {'branches': ['main']},
                       'workflow_dispatch': {'inputs': {'bootstrap_artifacts': {
                           'description': 'I02: publish exact tested bootstrap images only; no application delivery',
                           'type': 'boolean', 'required': True, 'default': False}}}},
                'permissions': {'contents': 'read'},
                'concurrency': {'group': 'pokerattack-bootstrap-build', 'cancel-in-progress': False},
                'jobs': jobs}
    commands = str(workflow)
    for forbidden in ('LINODE_', 'AZURE_STATIC_WEB_APPS_API_TOKEN', 'server_deliver.py', 'server_promote.py',
                      'server_finish.py', 'static-web-apps-deploy@', 'client_publish.py'):
        if forbidden in commands:
            raise ValueError('Build-only source unexpectedly contains delivery authority')
    path = WORKTREE / '.github/workflows/migration-ci.yml'
    path.write_text(yaml.safe_dump(workflow, sort_keys=False, width=1200), encoding='utf-8', newline='\n')
    report = {'task': 'I02 build-only source prerequisite', 'branch': BRANCH, 'base_main_sha': current,
              'primary_worktree_preserved': True, 'routine_workflows': 'Inert source templates; no client/server delivery workflow published',
              'enabled_workflow': '.github/workflows/migration-ci.yml', 'production_delivery_authority': False,
              'manual_main_artifact_only': True, 'workflow_sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
              'completion': 'Prepared only; must pass local checks, source review, PR CI and main build before claiming real provider authentication.'}
    (ROOT / 'docs/deployment-evidence/I02-bootstrap-source-preparation.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print('Isolated build-only review prepared; primary worktree and routine delivery definitions preserved.')


if __name__ == '__main__':
    main()
