"""Provision isolated environment-bound identities and private client history.

Uses the current authorized CLI session. Provider output stays in memory; only
resource identities, role scopes, federation policy and secret names are saved.
Real GitHub federation and SWA delivery must subsequently be tested in K.
"""
import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from github import api, REPOSITORY

ROOT = Path(__file__).parents[2]
SUBSCRIPTION = 'd2f70759-7eeb-4ca5-9046-f7c8d562ef8f'
TENANT = '9aafd1d1-65c4-4d23-a613-f66e425a581f'
GROUP = 'poker-attack-rg'
ACCOUNT = 'pareleasearchive' + hashlib.sha256(REPOSITORY.encode()).hexdigest()[:8]
REPORT = ROOT / 'docs/deployment-evidence/I02-azure-provisioning.json'
ENVIRONMENTS = ('server-acr', 'client-swa', 'server-verification', 'server-promotion')


def az(*args):
    result = subprocess.run([shutil.which('az.cmd' if os.name == 'nt' else 'az'), *args,
                             *(() if args[0] == 'ad' else ('--subscription', SUBSCRIPTION)), '--only-show-errors', '-o', 'json'],
                            capture_output=True, timeout=180)
    if result.returncode:
        raise ValueError('Azure provisioning operation rejected: ' + ' '.join(args[:3]))
    return json.loads(result.stdout) if result.stdout.strip() else None


def variable(name, value, environment=None):
    args = ['gh', 'variable', 'set', name, '--repo', REPOSITORY, '--body', value]
    if environment:
        args += ['--env', environment]
    if subprocess.run(args, capture_output=True, timeout=60).returncode:
        raise ValueError('GitHub variable configuration rejected: ' + name)


def assignment(principal, role, scope, kind='ServicePrincipal'):
    existing = az('role', 'assignment', 'list', '--scope', scope,
                  '--query', "[?principalId=='" + principal + "' && roleDefinitionName=='" + role + "']")
    if not existing:
        existing = [az('role', 'assignment', 'create', '--assignee-object-id', principal,
                       '--assignee-principal-type', kind, '--role', role, '--scope', scope)]
    if len(existing) != 1 or existing[0]['scope'].lower() != scope.lower():
        raise ValueError('Ambiguous role scope')
    return {key: existing[0][key] for key in ('id', 'principalId', 'roleDefinitionId', 'scope')}


def main():
    report = {'task': 'I02', 'state': 'IN_PROGRESS', 'identities': {}, 'assignments': [],
              'production_restart': False, 'registry_mode_changed': False,
              'started_utc': datetime.datetime.now(datetime.timezone.utc).isoformat()}
    try:
        account = az('account', 'show')
        if account['id'] != SUBSCRIPTION or account['tenantId'] != TENANT:
            raise ValueError('Wrong Azure subscription/tenant')
        if api('repos/' + REPOSITORY + '/actions/variables/DEPLOYMENT_AUTOMATION_ENABLED')['value'] != 'false':
            raise ValueError('Rollout must remain off while provisioning')
        group = az('group', 'show', '--name', GROUP)
        registry_id = '/subscriptions/' + SUBSCRIPTION + '/resourceGroups/general/providers/Microsoft.ContainerRegistry/registries/chefknife'
        registry = az('rest', '--method', 'get', '--url', 'https://management.azure.com' + registry_id + '?api-version=2025-11-01',
                      '--query', '{id:id,roleAssignmentMode:properties.roleAssignmentMode}')
        if registry['roleAssignmentMode'] != 'LegacyRegistryPermissions':
            raise ValueError('Review changed registry mode before choosing publication role')
        report['registry'] = registry
        accounts = az('storage', 'account', 'list', '--resource-group', GROUP)
        archive = next((item for item in accounts if item['name'] == ACCOUNT), None)
        if archive and archive.get('tags', {}).get('purpose') != 'pokerattack-client-release-archive':
            raise ValueError('Refuse unowned archive account')
        if archive is None:
            archive = az('storage', 'account', 'create', '--name', ACCOUNT, '--resource-group', GROUP,
                         '--location', group['location'], '--sku', 'Standard_LRS', '--kind', 'StorageV2',
                         '--min-tls-version', 'TLS1_2', '--allow-blob-public-access', 'false',
                         '--allow-shared-key-access', 'false', '--https-only', 'true', '--tags',
                         'purpose=pokerattack-client-release-archive', 'repository=' + REPOSITORY)
        az('storage', 'container-rm', 'create', '--resource-group', GROUP, '--storage-account', ACCOUNT,
           '--name', 'pokerattack-client-releases', '--public-access', 'off')
        az('storage', 'account', 'blob-service-properties', 'update', '--resource-group', GROUP,
           '--account-name', ACCOUNT, '--enable-versioning', 'true', '--enable-delete-retention', 'true',
           '--delete-retention-days', '30', '--enable-container-delete-retention', 'true',
           '--container-delete-retention-days', '30')
        scope = archive['id'] + '/blobServices/default/containers/pokerattack-client-releases'
        # Preserve root-controlled bootstrap/recovery access using current login,
        # with no account key, shared password, SAS or anonymous blob access.
        user = az('ad', 'signed-in-user', 'show', '--query', 'id')
        report['assignments'].append(assignment(user, 'Storage Blob Data Contributor', scope, 'User'))
        identities = az('identity', 'list', '--resource-group', GROUP)
        for environment in ENVIRONMENTS:
            name = 'pa-release-' + environment
            identity = next((item for item in identities if item['name'] == name), None)
            if identity and identity.get('tags', {}).get('purpose') != 'pokerattack-release-automation':
                raise ValueError('Refuse unowned identity')
            if identity is None:
                identity = az('identity', 'create', '--name', name, '--resource-group', GROUP,
                              '--location', group['location'], '--tags', 'purpose=pokerattack-release-automation',
                              'repository=' + REPOSITORY, 'environment=' + environment)
            subject = 'repo:' + REPOSITORY + ':environment:' + environment
            federations = az('identity', 'federated-credential', 'list', '--identity-name', name, '--resource-group', GROUP)
            if not federations:
                az('identity', 'federated-credential', 'create', '--name', 'github-environment', '--identity-name', name,
                   '--resource-group', GROUP, '--issuer', 'https://token.actions.githubusercontent.com',
                   '--subject', subject, '--audiences', 'api://AzureADTokenExchange')
                federations = az('identity', 'federated-credential', 'list', '--identity-name', name, '--resource-group', GROUP)
            if len(federations) != 1 or federations[0]['subject'] != subject or federations[0]['issuer'] != 'https://token.actions.githubusercontent.com' or federations[0]['audiences'] != ['api://AzureADTokenExchange']:
                raise ValueError('Unexpected federation; never broaden existing trust')
            if environment == 'server-acr':
                role, resource = 'AcrPush', registry_id
            else:
                role = 'Storage Blob Data Contributor' if environment == 'client-swa' else 'Storage Blob Data Reader'
                resource = scope
            report['assignments'].append(assignment(identity['principalId'], role, resource))
            report['identities'][environment] = {key: identity[key] for key in ('id', 'clientId', 'principalId', 'name')}
            report['identities'][environment]['federation'] = federations[0]
            variable('AZURE_CLIENT_ID', identity['clientId'], environment)
        for name, value in (('AZURE_TENANT_ID', TENANT), ('AZURE_SUBSCRIPTION_ID', SUBSCRIPTION), ('CLIENT_ARCHIVE_ACCOUNT', ACCOUNT)):
            variable(name, value)
        token = az('staticwebapp', 'secrets', 'list', '--name', 'poker-attack-webapp', '--resource-group', GROUP,
                   '--query', 'properties.apiKey')
        if not isinstance(token, str) or not 16 <= len(token) <= 16384:
            raise ValueError('SWA authentication unavailable')
        result = subprocess.run(['gh', 'secret', 'set', 'AZURE_STATIC_WEB_APPS_API_TOKEN', '--repo', REPOSITORY,
                                 '--env', 'client-swa'], input=token.encode(), capture_output=True, timeout=60)
        del token
        if result.returncode:
            raise ValueError('SWA secret installation rejected')
        report['archive'] = az('storage', 'account', 'show', '--name', ACCOUNT, '--resource-group', GROUP,
                               '--query', '{id:id,name:name,allowSharedKeyAccess:allowSharedKeyAccess,allowBlobPublicAccess:allowBlobPublicAccess,minimumTlsVersion:minimumTlsVersion,enableHttpsTrafficOnly:enableHttpsTrafficOnly}')
        report['retention'] = az('storage', 'account', 'blob-service-properties', 'show', '--account-name', ACCOUNT, '--resource-group', GROUP)
        report['installed_secret_names'] = ['client-swa/AZURE_STATIC_WEB_APPS_API_TOKEN']
        report['scope_note'] = 'LegacyRegistryPermissions requires registry-scoped AcrPush; no mode change, no registry administration/deletion, and host pull-only token remains separate. Source publisher accepts only the two fixed PokerAttack repositories.'
        report['state'] = 'PROVISIONED / LIVE GITHUB OIDC AND SWA DELIVERY NOT YET VERIFIED'
        report['pending'] = ['Actual trusted GitHub OIDC authentication and image operations', 'Actual protected SWA publication', 'Archive conditional-write integration readback']
    except Exception as error:
        report['error_type'] = type(error).__name__
        raise
    finally:
        report['recorded_utc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        REPORT.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print('Environment-bound Azure identities/private history/SWA secret provisioned; live delivery still unverified.')


if __name__ == '__main__':
    main()
