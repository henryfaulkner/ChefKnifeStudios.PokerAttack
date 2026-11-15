"""Publish an explicitly selected maintenance/release artifact to PokerAttack SWA."""
import json
import os
from pathlib import Path
import subprocess
import sys

sys.stdout.reconfigure(encoding='utf-8')
mode, folder = sys.argv[1:]
repo = Path(__file__).resolve().parents[1]
artifact = Path(folder).resolve(strict=True)
if mode == 'maintenance':
    assert artifact == repo / 'deploy/maintenance'
elif mode == 'release':
    assert artifact.is_relative_to(repo / 'artifacts')
    config = json.loads((artifact / 'staticwebapp.config.json').read_text(encoding='utf-8-sig'))
    assert config['globalHeaders']['Blazor-Environment'] == 'prod'
    for file in artifact.glob('appsettings*.json'):
        settings = json.loads(file.read_text(encoding='utf-8-sig'))
        apis = settings.get('AppSettings', {}).get('ExternalApis', [])
        assert len(apis) == 2
        assert {api['Name'] for api in apis} == {'PokerAttackAPI', 'PokerAttackSignalR'}
        assert all(api['BaseUri'] == 'https://api.playpokerattack.com/' for api in apis)
else:
    raise ValueError('Use maintenance or release')
token = subprocess.check_output(['az.cmd', 'staticwebapp', 'secrets', 'list', '-n', 'poker-attack-webapp',
                                 '-g', 'poker-attack-rg', '--query', 'properties.apiKey', '-o', 'tsv'], text=True).strip()
assert token and '\n' not in token
result = subprocess.run(['npx.cmd', '--yes', '--package=node@22', '--package=@azure/static-web-apps-cli@2.1.0',
                         'swa', 'deploy', str(artifact), '--swa-config-location', str(artifact),
                         '--env', 'production', '--no-use-keychain'],
                        cwd=repo, env={**os.environ, 'SWA_CLI_DEPLOYMENT_TOKEN': token},
                        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8', errors='replace')
log = result.stdout.replace(token, '[REDACTED]')
(repo / 'artifacts' / f'swa-{mode}-deployment.log').write_text(log, encoding='utf-8')
print(log[-5000:])
sys.exit(result.returncode)
