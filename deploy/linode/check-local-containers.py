"""Disposable local Docker verification. Never reads or contacts a production database."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request
import uuid
from local_docker import assert_local_docker

REPO = Path(__file__).resolve().parents[2]
PREFIX = 'pokerattack-wp05-' + uuid.uuid4().hex[:12]
PRIVATE = Path(tempfile.mkdtemp(prefix=PREFIX))
network, pg, api = (PREFIX + suffix for suffix in ('-net', '-pg', '-api'))
db = 'pokerattack_test_' + uuid.uuid4().hex[:12]
admin_password, runtime_password = secrets.token_hex(32), secrets.token_hex(32)
created = []


def run(*args, **kwargs):
    result = subprocess.run(args, capture_output=True, text=True, **kwargs)
    if result.returncode:
        # Secrets may occur in provider errors; save private detail rather than echoing it.
        (PRIVATE / 'failure.txt').write_text(result.stdout + result.stderr, encoding='utf-8')
        raise RuntimeError('Local fixture command failed; private diagnostic: ' + str(PRIVATE / 'failure.txt'))
    return result.stdout.strip()


def env_file(name, values):
    path = PRIVATE / name
    path.write_text(''.join(f'{key}={value}\n' for key, value in values.items()), encoding='utf-8')
    return path


class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self, *_):
        pass


server = None
server_thread = None
try:
    assert_local_docker()
    run('docker', 'network', 'create', network)
    created.append(('network', network))
    pg_env = env_file('postgres.env', {'POSTGRES_PASSWORD': admin_password, 'POSTGRES_DB': db})
    run('docker', 'run', '-d', '--name', pg, '--network', network, '--tmpfs', '/var/lib/postgresql/data',
        '--env-file', str(pg_env), 'postgres:16')
    created.append(('container', pg))
    for _ in range(60):
        result = subprocess.run(['docker', 'exec', pg, 'pg_isready', '-U', 'postgres', '-d', db], capture_output=True)
        if result.returncode == 0:
            break
        time.sleep(1)
    else:
        raise RuntimeError('Disposable PostgreSQL did not start')
    admin = f'Host={pg};Database={db};Username=postgres;Password={admin_password};SSL Mode=Disable'
    migration_env = env_file('migration.env', {'ConnectionStrings__PokerAttackDB': admin})
    run('docker', 'run', '--rm', '--network', network, '--env-file', str(migration_env), os.environ.get('POKERATTACK_FIXTURE_MIGRATION_IMAGE', 'pokerattack-migrations:wp05-local'))
    sql = (f"CREATE ROLE pokerattack_fixture LOGIN PASSWORD '{runtime_password}';\n"
           'GRANT USAGE ON SCHEMA "PokerAttack" TO pokerattack_fixture;\n'
           'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA "PokerAttack" TO pokerattack_fixture;\n'
           'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA "PokerAttack" TO pokerattack_fixture;\n'
           'GRANT SELECT ON public."__EFMigrationsHistory" TO pokerattack_fixture;\n')
    run('docker', 'exec', '-i', pg, 'psql', '-U', 'postgres', '-d', db, '-v', 'ON_ERROR_STOP=1', input=sql)
    artifact = REPO / 'artifacts' / (PREFIX + '-client')
    # Reserve the local frontend port before configuring its exact CORS origin.
    server = ThreadingHTTPServer(('127.0.0.1', 0), partial(QuietHandler, directory=str(artifact)))
    client_url = f'http://127.0.0.1:{server.server_port}'
    runtime = f'Host={pg};Database={db};Username=pokerattack_fixture;Password={runtime_password};SSL Mode=Disable'
    admission = PRIVATE / 'admission'
    admission.mkdir()
    admission_state = admission / 'state.json'
    admission_state.write_text('{"active":false}\n', encoding='utf-8')
    app_env = env_file('api.env', {
        'ConnectionStrings__PokerAttackDB': runtime, 'ASPNETCORE_ENVIRONMENT': 'Staging',
        'ASPNETCORE_URLS': 'http://0.0.0.0:8081', 'Hosting__AllowedOrigins': client_url,
        'Testing__EnableEndpoints': 'true', 'Sentry__Dsn': '', 'Logging__LogLevel__Default': 'Warning',
        'Hosting__MaintenanceStatePath': '/run/pokerattack-admission/state.json',
    })
    # Bridge networking here is a local Docker Desktop fixture, not Linode's host network.
    run('docker', 'run', '-d', '--name', api, '--network', network, '-p', '127.0.0.1::8081',
        '--cpus', '0.25', '--memory', '512m', '--read-only', '--tmpfs', '/tmp:size=64m,mode=1777',
        '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true', '--env-file', str(app_env),
        '--mount', f'type=bind,source={admission},target=/run/pokerattack-admission,readonly',
        os.environ.get('POKERATTACK_FIXTURE_IMAGE', 'pokerattack-api:wp05-local'))
    created.append(('container', api))
    info = json.loads(run('docker', 'inspect', api))[0]
    candidate = json.loads(run('docker', 'image', 'inspect', os.environ.get('POKERATTACK_FIXTURE_IMAGE', 'pokerattack-api:wp05-local')))[0]
    assert info['Image'] == candidate['Id'], 'Fixture did not run the exact built image'
    if os.environ.get('POKERATTACK_FIXTURE_SOURCE'):
        assert candidate['Config']['Labels']['org.opencontainers.image.revision'] == os.environ['POKERATTACK_FIXTURE_SOURCE']
    assert info['HostConfig']['NanoCpus'] == 250000000
    assert info['HostConfig']['Memory'] == 536870912
    assert info['Config']['User'] not in ('', '0', 'root')
    assert info['HostConfig']['ReadonlyRootfs']
    port = info['NetworkSettings']['Ports']['8081/tcp'][0]['HostPort']
    api_url = f'http://127.0.0.1:{port}'
    for _ in range(60):
        try:
            with urllib.request.urlopen(api_url + '/health/ready', timeout=3) as response:
                assert response.status == 200
                break
        except (OSError, urllib.error.URLError):
            time.sleep(1)
    else:
        (PRIVATE / 'api.log').write_text(run('docker', 'logs', api), encoding='utf-8')
        raise RuntimeError('Local container readiness failed; see ' + str(PRIVATE / 'api.log'))
    # A configured read-only host mount must survive replacement and must not be
    # writable by the non-root application. This is only the randomly named API.
    probe_write = subprocess.run(['docker', 'exec', api, '/bin/sh', '-c',
                                  'printf corrupted > /run/pokerattack-admission/state.json'], capture_output=True)
    assert probe_write.returncode != 0, 'API could modify host admission state'
    admission_state.write_text('{"active":true}\n', encoding='utf-8')
    run('docker', 'restart', api)
    # Docker Desktop may reassign an ephemeral published port on restart.
    restarted = json.loads(run('docker', 'inspect', api))[0]
    api_url = 'http://127.0.0.1:' + restarted['NetworkSettings']['Ports']['8081/tcp'][0]['HostPort']
    for _ in range(60):
        try:
            with urllib.request.urlopen(api_url + '/health/ready', timeout=3) as response:
                assert response.status == 200
                break
        except (OSError, urllib.error.URLError):
            time.sleep(1)
    else:
        raise RuntimeError('Disposable replacement did not become ready')
    run('node', str(REPO / 'deploy/prepare-client-artifact.mjs'), '--source', str(REPO / 'artifacts/client-wp04/wwwroot'),
        '--output', str(artifact), '--api', api_url)
    server_thread = threading.Thread(target=server.serve_forever, daemon=True)
    server_thread.start()
    run('node', str(REPO / 'tests/tools/maintenance-browser.cjs'), client_url, api_url)
    admission_state.write_text('{"active":false}\n', encoding='utf-8')
    result = subprocess.run(['npm.cmd' if os.name == 'nt' else 'npm', '--prefix', 'tests', 'test', '--',
                             'migration-smoke.spec.ts', '--timeout=90000', '--reporter=list'], cwd=REPO,
                            env={**os.environ, 'CLIENT_URL': client_url, 'API_URL_CLEANUP': api_url, 'API_PUBLIC_URL': api_url})
    if result.returncode:
        raise RuntimeError('Local browser smoke failed')
    print('PASS: bundle migration, scoped runtime DB access, readiness, non-root/read-only container, 0.25 CPU/512 MiB, durable read-only maintenance, browser smoke.')
finally:
    if server:
        if server_thread:
            server.shutdown()
            server_thread.join(timeout=5)
        server.server_close()
    # Only exact, randomly generated fixture names are eligible for cleanup.
    for kind, name in reversed(created):
        assert name.startswith(PREFIX + '-')
        subprocess.run(['docker', 'rm', '-f', name] if kind == 'container' else ['docker', 'network', 'rm', name],
                       capture_output=True)
