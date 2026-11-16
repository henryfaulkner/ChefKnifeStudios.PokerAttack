"""Read-only, allowlisted host inventory. Run on the VM with python3 via trusted SSH.

Never serialize raw inspect, configuration, authentication, or health output.
"""
import datetime
import hashlib
import json
import os
from pathlib import Path
import stat
import subprocess


def run(*args):
    return subprocess.check_output(args, text=True, timeout=30).strip()


def main():
    base = Path('/var/lib/pokerattack/production')
    result = {'inspected_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'versions': {name: run(*args) for name, args in {
                  'docker': ['docker', '--version'], 'compose': ['docker', 'compose', 'version'],
                  'python': ['python3', '--version'], 'alloy': ['alloy', '--version']}.items()},
              'pairs': {}, 'files': [], 'containers': []}
    for name in ['active', 'pending', 'known-good', 'previous-good']:
        p = base / name
        result['pairs'][name] = None
        if p.is_symlink():
            pair = {'path': str(p.resolve())}
            for field in ['image', 'digest', 'config-id']:
                pair[field] = (p / field).read_text().strip()
            pair['snapshot_config_sha256'] = hashlib.sha256((p / 'app.env').read_bytes()).hexdigest()
            result['pairs'][name] = pair
    result['state'] = {name: (base / name).exists() for name in ['transaction', 'lease']}
    for p in ['/opt/pokerattack', '/opt/pokerattack/compose.yaml', '/opt/pokerattack/validate-config.py',
              '/usr/local/sbin/pokerattack-deploy', '/usr/local/sbin/pokerattack-ssh-deploy',
              '/etc/pokerattack/registry', '/etc/pokerattack/registry/config.json',
              '/etc/pokerattack/production/maintenance', '/etc/alloy/config.alloy']:
        path = Path(p)
        s = path.stat()
        entry = {'path': p, 'uid': s.st_uid, 'gid': s.st_gid, 'mode': oct(stat.S_IMODE(s.st_mode))}
        if path.is_file() and p.startswith(('/opt/', '/usr/local/', '/etc/alloy/')):
            entry['sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
        result['files'].append(entry)
    for cid in run('docker', 'ps', '-aq').splitlines():
        d = json.loads(run('docker', 'inspect', cid))[0]
        entry = {'id': d['Id'], 'name': d['Name'], 'configured_image': d['Config']['Image'],
                 'image_id': d['Image'], 'state': d['State']['Status'],
                 'health': d['State'].get('Health', {}).get('Status', 'not-configured'),
                 'network_mode': d['HostConfig']['NetworkMode'], 'nano_cpus': d['HostConfig']['NanoCpus'],
                 'memory_bytes': d['HostConfig']['Memory'], 'user': d['Config']['User']}
        if entry['name'] == '/pokerattack-production-api-1':
            im = json.loads(run('docker', 'image', 'inspect', d['Image']))[0]
            entry['repo_digests'] = im['RepoDigests']
            entry['source_revision'] = (im['Config'].get('Labels') or {}).get('org.opencontainers.image.revision')
            entry['urls'] = [e for e in d['Config']['Env'] if e.startswith('ASPNETCORE_URLS=')]
        result['containers'].append(entry)
    result['backup_reference'] = Path('/var/lib/pokerattack/latest-backup').read_text().strip()
    result['backup_service'] = run('systemctl', 'show', 'pokerattack-backup.service', '-p', 'Result', '-p', 'ExecMainStatus')
    result['alloy_service'] = run('systemctl', 'show', 'alloy', '-p', 'User', '-p', 'Group', '-p', 'ActiveState')
    # Read only known release fields, never the acknowledgment/config contents.
    active = base / 'active'
    config = (active / 'config-id').read_text().strip()
    validation = subprocess.run(['python3', '/opt/pokerattack/validate-config.py', 'config', 'production',
                                 f'/etc/pokerattack/production/configs/{config}.env', config], capture_output=True, timeout=30)
    result['config_validation_exit'] = validation.returncode
    result['ready'] = subprocess.run(['curl', '--silent', '--fail', '--max-time', '5',
                                      'http://127.0.0.1:8081/health/ready'], capture_output=True).returncode == 0
    for name, image in [('allowed', (active / 'image').read_text().strip()),
                        ('unrelated', 'chefknife.azurecr.io/chefknifestudios.martajazz.server.webapi:latest')]:
        probe = subprocess.run(['docker', '--config', '/etc/pokerattack/registry', 'manifest', 'inspect', image],
                               capture_output=True, timeout=45)
        result[f'registry_{name}_manifest_exit'] = probe.returncode
        if name == 'unrelated':
            result['registry_denial_recognized'] = any(x in probe.stderr.lower() for x in [b'unauthorized', b'denied'])
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
