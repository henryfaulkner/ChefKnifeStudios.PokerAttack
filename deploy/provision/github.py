"""Provision only PokerAttack's default-off GitHub release boundaries.

No credentials are read. Existing unexpected settings require inspection rather
than being silently replaced. Readbacks, including partial failure, are retained.
"""
import datetime
import json
from pathlib import Path
import subprocess

REPOSITORY = 'henryfaulkner/ChefKnifeStudios.PokerAttack'
PREFIX = 'repos/' + REPOSITORY
ENVIRONMENTS = ('client-swa', 'server-acr', 'server-status', 'server-linode',
                'server-verification', 'server-promotion')
REVIEWED = {'server-linode', 'server-promotion'}
REPORT = Path(__file__).parents[2] / 'docs/deployment-evidence/I01-github-provisioning.json'


def api(path, method='GET', body=None):
    args = ['gh', 'api', '--method', method, path]
    if body is not None:
        args += ['--input', '-']
    result = subprocess.run(args, input=None if body is None else json.dumps(body).encode(),
                            capture_output=True, timeout=60)
    if result.returncode:
        raise ValueError('GitHub operation rejected: ' + method + ' ' + path)
    return json.loads(result.stdout) if result.stdout.strip() else None


def main():
    report = {'task': 'I01', 'state': 'IN_PROGRESS', 'production_restart': False,
              'source_publication': False, 'environments': {},
              'started_utc': datetime.datetime.now(datetime.timezone.utc).isoformat()}
    try:
        repo = api(PREFIX)
        user = api('user')
        if (repo['full_name'] != REPOSITORY or repo['default_branch'] != 'main' or
                not repo['permissions']['admin'] or user['login'] != 'henryfaulkner'):
            raise ValueError('Unexpected repository/admin identity')
        existing = api(PREFIX + '/environments')['environments']
        if any(item['name'] not in ENVIRONMENTS for item in existing):
            raise ValueError('Inspect additional existing environments before provisioning')
        for name in ENVIRONMENTS:
            desired = {'wait_timer': 0, 'prevent_self_review': False,
                       'reviewers': [{'type': 'User', 'id': user['id']}] if name in REVIEWED else [],
                       'deployment_branch_policy': {'protected_branches': False, 'custom_branch_policies': True}}
            api(PREFIX + '/environments/' + name, 'PUT', desired)
            endpoint = PREFIX + '/environments/' + name + '/deployment-branch-policies'
            policies = api(endpoint)['branch_policies']
            if not policies:
                api(endpoint, 'POST', {'name': 'main', 'type': 'branch'})
            elif len(policies) != 1 or policies[0]['name'] != 'main' or policies[0]['type'] != 'branch':
                raise ValueError('Inspect unexpected environment branch policies')
            readback = api(PREFIX + '/environments/' + name)
            policies = api(endpoint)['branch_policies']
            if len(policies) != 1 or policies[0]['name'] != 'main' or policies[0]['type'] != 'branch':
                raise ValueError('Main-only restriction not established')
            rules = [rule for rule in readback['protection_rules'] if rule['type'] == 'required_reviewers']
            if name in REVIEWED and (len(rules) != 1 or rules[0]['prevent_self_review'] is not False or
                    [(r['type'], r['reviewer']['id']) for r in rules[0]['reviewers']] != [('User', user['id'])]):
                raise ValueError('Owner release-review boundary not established')
            if name not in REVIEWED and rules:
                raise ValueError('Unexpected additional release review')
            report['environments'][name] = {'settings': readback, 'branch_policies': policies}
        variables = api(PREFIX + '/actions/variables')['variables']
        switch = next((item for item in variables if item['name'] == 'DEPLOYMENT_AUTOMATION_ENABLED'), None)
        if switch is not None and switch['value'] != 'false':
            raise ValueError('Inspect enabled rollout switch; never silently replace it')
        if switch is None:
            api(PREFIX + '/actions/variables', 'POST', {'name': 'DEPLOYMENT_AUTOMATION_ENABLED', 'value': 'false'})
        protection = {'required_status_checks': {'strict': True, 'contexts': ['Client validation', 'Server validation']},
                      'enforce_admins': True, 'required_pull_request_reviews': None, 'restrictions': None,
                      'required_conversation_resolution': True, 'allow_force_pushes': False, 'allow_deletions': False}
        # A02 and the fresh I01 inventory establish that main was unprotected.
        # Preserve any subsequently added protection by refusing it here.
        branch = api(PREFIX + '/branches/main')
        if branch['protected']:
            raise ValueError('Inspect existing main protection before changing it')
        api(PREFIX + '/branches/main/protection', 'PUT', protection)
        report['main_protection'] = api(PREFIX + '/branches/main/protection')
        required = report['main_protection']['required_status_checks']
        if set(required['contexts']) != {'Client validation', 'Server validation'} or not required['strict']:
            raise ValueError('Stable validation protection not established')
        report['rollout_switch'] = api(PREFIX + '/actions/variables/DEPLOYMENT_AUTOMATION_ENABLED')
        if report['rollout_switch']['value'] != 'false':
            raise ValueError('Rollout must remain off')
        report['owner_approval'] = {'login': user['login'], 'repository_admin': True,
                                  'required_reviewer': True, 'self_review_permitted': True,
                                  'actual_release_approval': 'Not requested or granted; K requires fresh release decisions.'}
        report['required_checks_note'] = 'Names match locally validated workflows; actual CI coverage/publication remains K03/L01.'
        report['state'] = 'CONFIGURED / READBACK VERIFIED'
    except Exception as error:
        report['error_type'] = type(error).__name__
        raise
    finally:
        report['recorded_utc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        REPORT.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print('GitHub release environments/main validation configured; rollout remains false. Readback saved.')


if __name__ == '__main__':
    main()
