"""Disposable HTTPS release-gameplay rehearsal; never contacts production.

Windows uses an already trusted localhost development certificate, exported only
into a private temporary directory. This tool neither installs trust nor disables
TLS validation. Linux rehearsal supplies an already trusted disposable cert/key
via POKERATTACK_FIXTURE_CERT/KEY. Only UUID-named local containers are removed.
Synthetic release metadata is fixture-only, never published provenance/evidence.
"""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import ssl
import subprocess
import sys
import tarfile
import tempfile
import threading
import time
import urllib.request
import uuid
from local_docker import assert_local_docker
from fixture_tls import certificate

if sys.argv[1:] not in ([], ['--client-only']):
    raise ValueError('Only the explicit local --client-only mode is supported')
CLIENT_ONLY = sys.argv[1:] == ['--client-only']
RESUME = os.environ.get('POKERATTACK_FIXTURE_CRASH_RESUME') == '1'
if CLIENT_ONLY and RESUME:
    raise ValueError('Crashed-run recovery uses the gameplay fixture only')

REPO = Path(__file__).resolve().parents[2]
PREFIX = 'pokerattack-release-fixture-' + uuid.uuid4().hex[:12]
# GitHub exports RUNNER_TEMP independently of Python's /tmp default. The
# production-safe loader correctly refuses a context outside that private root.
PRIVATE_ROOT = Path(os.environ.get('RUNNER_TEMP') or tempfile.gettempdir()).resolve(strict=True)
PRIVATE = Path(tempfile.mkdtemp(prefix=PREFIX, dir=PRIVATE_ROOT))
NETWORK, PG, API = (PREFIX + suffix for suffix in ('-net', '-pg', '-api'))
created = []
server = thread = None


def run(*arguments, **kwargs):
    result = subprocess.run(arguments, capture_output=True, text=True, **kwargs)
    if result.returncode:
        # Provider errors can contain env values: keep only private diagnostics.
        (PRIVATE / 'failure.txt').write_text(result.stdout + result.stderr, encoding='utf-8')
        if arguments[0] == 'node':
            # Only allowlisted stage names enter CI output; private raw browser
            # and provider diagnostics stay outside repository/artifact uploads.
            sys.path.insert(0, str(REPO / 'deploy/release'))
            from verification_stages import failed_stage
            print('Disposable release failure stage: ' + failed_stage(result.stderr.encode()))
        raise RuntimeError('Disposable fixture command failed; private diagnostics retained')
    return result.stdout.strip()


def env_file(name, values):
    path = PRIVATE / name
    path.write_text(''.join(f'{key}={value}\n' for key, value in values.items()), encoding='utf-8')
    path.chmod(0o600)
    return path


class ClientHandler(SimpleHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def end_headers(self):
        self.send_header('Blazor-Environment', 'prod')
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()


passed = False
tls_material = None
try:
    assert_local_docker()
    tls_material = certificate(PRIVATE)
    cert, key = tls_material.__enter__()
    # The private parent remains owner-only. The unprivileged API can read only
    # these two exact bind-mounted files; it never receives the CA private key.
    cert.chmod(0o644)
    key.chmod(0o644)
    image = os.environ.get('POKERATTACK_FIXTURE_IMAGE', 'pokerattack-api:release-fixture')
    inspected = json.loads(run('docker', 'image', 'inspect', image))[0]
    artifact = REPO / 'artifacts' / (PREFIX + '-client')
    server = ThreadingHTTPServer(('127.0.0.1', 0), partial(ClientHandler, directory=str(artifact)))
    tls = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    tls.load_cert_chain(cert, key)
    server.socket = tls.wrap_socket(server.socket, server_side=True)
    client_url = f'https://localhost:{server.server_port}'
    database = 'pokerattack_test_' + uuid.uuid4().hex[:12]
    password, runtime_password = secrets.token_hex(32), secrets.token_hex(32)
    run('docker', 'network', 'create', NETWORK)
    created.append(('network', NETWORK))
    pg_env = env_file('postgres.env', {'POSTGRES_PASSWORD': password, 'POSTGRES_DB': database})
    run('docker', 'run', '-d', '--name', PG, '--network', NETWORK, '--tmpfs', '/var/lib/postgresql/data',
        '--env-file', str(pg_env), 'postgres:16')
    created.append(('container', PG))
    for _ in range(60):
        if subprocess.run(['docker', 'exec', PG, 'pg_isready', '-U', 'postgres', '-d', database], capture_output=True).returncode == 0:
            break
        time.sleep(1)
    else:
        raise RuntimeError('Disposable database unavailable')
    migration = env_file('migration.env', {'ConnectionStrings__PokerAttackDB':
        f'Host={PG};Database={database};Username=postgres;Password={password};SSL Mode=Disable'})
    run('docker', 'run', '--rm', '--network', NETWORK, '--env-file', str(migration), os.environ.get('POKERATTACK_FIXTURE_MIGRATION_IMAGE', 'pokerattack-migrations:wp05-local'))
    run('docker', 'exec', '-i', PG, 'psql', '-U', 'postgres', '-d', database, '-v', 'ON_ERROR_STOP=1', input=(
        f"CREATE ROLE pokerattack_fixture LOGIN PASSWORD '{runtime_password}';\n"
        'GRANT USAGE ON SCHEMA "PokerAttack" TO pokerattack_fixture;\n'
        'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA "PokerAttack" TO pokerattack_fixture;\n'
        'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA "PokerAttack" TO pokerattack_fixture;\n'
        'GRANT SELECT ON public."__EFMigrationsHistory" TO pokerattack_fixture;\n'))
    players = [{'id': str(uuid.uuid4()), 'name': f'Release-1-1-{index}'} for index in range(3)]
    secret = secrets.token_hex(32)
    now = int(time.time())
    admission = PRIVATE / 'admission'
    admission.mkdir()
    # Explicit synthetic fixture identity; not a Git source/publication claim.
    source, config, digest = os.environ.get('POKERATTACK_FIXTURE_SOURCE', 'c' * 40), 'b' * 64, 'sha256:' + 'a' * 64
    assert len(source) == 40 and all(c in '0123456789abcdef' for c in source)
    assert inspected['Config']['Labels']['org.opencontainers.image.revision'] == source, 'Fixture image revision mismatch'
    assert inspected['Os'] == 'linux' and inspected['Architecture'] == 'amd64'
    state = {'active': not CLIENT_ONLY, 'run_id': 1, 'run_attempt': 1, 'digest': digest, 'config_sha256': config,
             'source_sha': source, 'verifier': {'id': 'd' * 32, 'issued_unix': now, 'expires_unix': now + 900,
              'revoked': False, 'players': players, 'token_sha256': hashlib.sha256(secret.encode()).hexdigest()}}
    (admission / 'state.json').write_text(json.dumps(state), encoding='utf-8')
    app_env = env_file('api.env', {
        'ConnectionStrings__PokerAttackDB': f'Host={PG};Database={database};Username=pokerattack_fixture;Password={runtime_password};SSL Mode=Disable',
        'ASPNETCORE_ENVIRONMENT': 'Production', 'ASPNETCORE_URLS': 'https://0.0.0.0:8443',
        'Kestrel__Certificates__Default__Path': '/fixture/localhost.pem',
        'Kestrel__Certificates__Default__KeyPath': '/fixture/localhost.key',
        'Hosting__AllowedOrigins': client_url, 'AllowedHosts': 'localhost',
        'Hosting__MaintenanceStatePath': '/admission/state.json',
        'Hosting__ReleaseImage': 'chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi@' + digest,
        'Hosting__ReleaseConfigSha256': config, 'POKERATTACK_SOURCE_REVISION': source,
        'Testing__EnableEndpoints': 'false', 'Sentry__Dsn': '', 'Logging__LogLevel__Default': 'Information',
    })
    run('docker', 'run', '-d', '--name', API, '--network', NETWORK, '-p', '127.0.0.1::8443',
        '--cpus', '0.25', '--memory', '512m', '--read-only', '--tmpfs', '/tmp:size=64m,mode=1777',
        '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true', '--env-file', str(app_env),
        '--mount', f'type=bind,source={cert},target=/fixture/localhost.pem,readonly',
        '--mount', f'type=bind,source={key},target=/fixture/localhost.key,readonly',
        '--mount', f'type=bind,source={admission},target=/admission,readonly', image)
    created.append(('container', API))
    info = json.loads(run('docker', 'inspect', API))[0]
    assert info['Image'] == inspected['Id']
    port = info['NetworkSettings']['Ports']['8443/tcp'][0]['HostPort']
    api_url = f'https://localhost:{port}'
    client_tls = ssl.create_default_context(cafile=str(cert))
    for _ in range(60):
        try:
            with urllib.request.urlopen(api_url + '/health/ready', context=client_tls, timeout=3) as response:
                assert response.status == 200
                break
        except OSError:
            time.sleep(1)
    else:
        (PRIVATE / 'api.log').write_text(run('docker', 'logs', API), encoding='utf-8')
        raise RuntimeError('Disposable HTTPS API not ready; private diagnostics retained')
    run('node', str(REPO / 'deploy/prepare-client-artifact.mjs'), '--source', str(REPO / 'artifacts/client-wp04/wwwroot'),
        '--output', str(artifact), '--api', api_url)
    public = {'format': 'pokerattack-client-public-v1', 'source_sha': source, 'run_id': 1, 'run_attempt': 1,
              'minimum_server_capability': 1}
    (artifact / 'release.json').write_text(json.dumps(public), encoding='utf-8')
    archive = PRIVATE / 'client.tar.gz'
    with tarfile.open(archive, 'w:gz') as stream:
        for path in sorted(artifact.rglob('*')):
            if path.is_file(): stream.add(path, arcname=str(path.relative_to(artifact)).replace('\\', '/'))
    packet = {'run_id': 1, 'run_attempt': 1, 'verifier_id': 'd' * 32, 'credential': f'1.1.{secret}', 'players': players,
              'client': {'source_sha': source, 'workflow': {'run_id': 1, 'run_attempt': 1}, 'minimum_server_capability': 1,
                         'archive_sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
                         'settings_files': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in artifact.glob('appsettings*.json')},
                         'settings_sha256': hashlib.sha256((artifact / 'appsettings.json').read_bytes()).hexdigest()}}
    manifest = json.loads((REPO / 'deploy/release/server-manifest.example.json').read_text())
    manifest.update(source_sha=source, config_sha256=config)
    for section in ('api', 'migration'):
        manifest[section].update(source_revision=source, tag=source)
    manifest['api'].update(digest=digest, local_image_id=inspected['Id'])
    packet['server_manifest'] = manifest
    packet['artifact_references'] = ['fixture-only:' + PREFIX]
    packet_path = PRIVATE / 'context.json'
    packet_path.write_text(json.dumps(packet), encoding='utf-8')
    packet_path.chmod(0o600)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    if CLIENT_ONLY:
        client_manifest = {'format': 'pokerattack-client-v1', 'source_sha': source,
            'workflow': manifest['workflow'], 'artifact_id': 10,
            'archive_sha256': packet['client']['archive_sha256'],
            'settings_sha256': packet['client']['settings_sha256'],
            'files': {path.relative_to(artifact).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                      for path in artifact.rglob('*') if path.is_file()},
            'minimum_server_capability': 1, 'verification_references': ['fixture-only:' + PREFIX]}
        client_manifest_path = PRIVATE / 'client-manifest.json'
        client_manifest_path.write_text(json.dumps(client_manifest), encoding='utf-8')
        fault = os.environ.get('POKERATTACK_FIXTURE_CLIENT_FAULT')
        if fault == 'release':
            changed = {**public, 'source_sha': '0' * 40}
            (artifact / 'release.json').write_text(json.dumps(changed), encoding='utf-8')
        elif fault == 'asset':
            with (artifact / 'css/mdc-overrides.css').open('ab') as stream:
                stream.write(b'\n/* deliberately wrong fixture bytes */\n')
        elif fault is not None:
            raise ValueError('Only the fixed release/asset fixture faults are supported')
        client_result_path = PRIVATE / 'client-result.json'
        output = run('node', str(REPO / 'tests/release/client.cjs'), env={**os.environ,
            'NODE_EXTRA_CA_CERTS': str(cert), 'POKERATTACK_RELEASE_MODE': 'isolated',
            'RELEASE_CLIENT_ORIGIN': client_url, 'RELEASE_API_ORIGIN': api_url,
            'POKERATTACK_CLIENT_MANIFEST': str(client_manifest_path),
            'POKERATTACK_CLIENT_RESULT': str(client_result_path)})
        result = json.loads(client_result_path.read_text())
        assert all(result['checks'].values())
        (REPO / 'artifacts/E04-client-browser-result.json').write_text(json.dumps({
            'fixture_only': True, 'image_id': inspected['Id'], 'tls_verification': True,
            'normal_admission': True, 'test_endpoints_enabled': False, 'result': result}, indent=2), encoding='utf-8')
        print(output)
        passed = True
    else:
        prior_counts = None
        if RESUME:
            seeded = subprocess.run(['node', str(REPO / 'tests/release/seed-crashed-fixture.cjs')], capture_output=True,
                text=True, timeout=90, env={**os.environ, 'NODE_EXTRA_CA_CERTS': str(cert),
                    'POKERATTACK_RELEASE_MODE': 'isolated', 'RELEASE_CLIENT_ORIGIN': client_url,
                    'RELEASE_API_ORIGIN': api_url, 'POKERATTACK_RELEASE_CONTEXT': str(packet_path)})
            assert seeded.returncode == 137, 'Local runner did not terminate at its intended fixture point'
            prior_counts = json.loads(run('docker', 'run', '--rm', '--network', 'container:' + API,
                '--mount', f'type=bind,source={cert},target=/fixture.pem,readonly', 'python:3.13-bookworm', 'python', '-c',
                'import json,ssl,urllib.request; '
                'print(urllib.request.urlopen("https://localhost:8443/_ops/status",context=ssl.create_default_context(cafile="/fixture.pem"),timeout=5).read().decode())'))
            assert prior_counts == {'activeGames': 1, 'lobbies': 1, 'soloGames': 1}
            state['verifier']['revoked'] = True
            (admission / 'state.json').write_text(json.dumps(state), encoding='utf-8')
            # Same run/pair/source, later attempt; always fresh random player IDs.
            players = [{'id': str(uuid.uuid4()), 'name': f'Release-1-2-{index}'} for index in range(3)]
            secret = secrets.token_hex(32)
            now = int(time.time())
            state.update(run_attempt=2)
            state['verifier'] = {'id': secrets.token_hex(16), 'issued_unix': now, 'expires_unix': now + 900,
                'revoked': False, 'players': players, 'token_sha256': hashlib.sha256(secret.encode()).hexdigest()}
            (admission / 'state.json').write_text(json.dumps(state), encoding='utf-8')
            packet.update(run_attempt=2, players=players, credential='1.2.' + secret, verifier_id=state['verifier']['id'])
            packet_path.write_text(json.dumps(packet), encoding='utf-8')
        result_path = PRIVATE / 'result.json'
        output = run('node', str(REPO / 'tests/release/run.cjs'), env={**os.environ,
            'NODE_EXTRA_CA_CERTS': str(cert), 'POKERATTACK_RELEASE_MODE': 'isolated',
            'RELEASE_CLIENT_ORIGIN': client_url, 'RELEASE_API_ORIGIN': api_url,
            'POKERATTACK_RELEASE_CONTEXT': str(packet_path), 'POKERATTACK_RELEASE_RESULT': str(result_path)})
        result = json.loads(result_path.read_text())
        assert result['passed'] is True
        observation = run('docker', 'run', '--rm', '--network', 'container:' + API,
            '--mount', f'type=bind,source={cert},target=/fixture.pem,readonly', 'python:3.13-bookworm', 'python', '-c',
            'import json,ssl,time,urllib.request; '
            'ctx=ssl.create_default_context(cafile="/fixture.pem"); '
            'counts=json.load(urllib.request.urlopen("https://localhost:8443/_ops/status",context=ctx,timeout=5)); '
            'print(json.dumps(counts)); '
            'assert counts=={"activeGames":0,"lobbies":0,"soloGames":0}, "Owned sessions remain"')
        counts = json.loads(observation)
        state['verifier']['revoked'] = True
        (admission / 'state.json').write_text(json.dumps(state), encoding='utf-8')
        revoked = run('node', str(REPO / 'tests/release/revoked.cjs'), env={**os.environ,
            'NODE_EXTRA_CA_CERTS': str(cert), 'POKERATTACK_RELEASE_MODE': 'isolated',
            'RELEASE_CLIENT_ORIGIN': client_url, 'RELEASE_API_ORIGIN': api_url,
            'POKERATTACK_RELEASE_CONTEXT': str(packet_path)})
        report = {'fixture_only': True, 'image_id': inspected['Id'], 'image_revision': inspected['Config'].get('Labels', {}).get('org.opencontainers.image.revision'),
                  'synthetic_admission_source': source, 'test_endpoints_enabled': False,
                  'tls_verification': True, 'ephemeral_runner_nss_trust': os.environ.get('POKERATTACK_FIXTURE_EPHEMERAL_TLS') == '1',
                  'verifier_revoked': True, 'revoked_credential_connection_rejected': True,
                  'post_cleanup_private_counts': counts,
                  'crashed_runner_recovered': RESUME, 'prior_crashed_runner_counts': prior_counts,
                  'server_manifest': manifest, 'verifier_window': {key: state['verifier'][key] for key in ('id', 'issued_unix', 'expires_unix')},
                  'result': result}
        (REPO / 'artifacts/D02-gameplay-result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(output)
        print(revoked)
        passed = True
finally:
    if server:
        if thread:
            server.shutdown()
            thread.join(timeout=5)
        server.server_close()
    for kind, name in reversed(created):
        assert name.startswith(PREFIX + '-')
        if kind == 'container' and name == API and not passed:
            diagnostic = subprocess.run(['docker', 'logs', '--tail', '200', API], capture_output=True, text=True)
            (PRIVATE / 'api.log').write_text(diagnostic.stdout + diagnostic.stderr, encoding='utf-8')
        subprocess.run(['docker', 'rm', '-f', name] if kind == 'container' else ['docker', 'network', 'rm', name], capture_output=True)
    if tls_material:
        tls_material.__exit__(None, None, None)
    if passed:
        assert not PRIVATE.is_symlink() and PRIVATE.resolve().parent == PRIVATE_ROOT and PRIVATE.name.startswith(PREFIX)
        shutil.rmtree(PRIVATE)
    else:
        print('Fixture failed; private diagnostics retained outside repository at ' + str(PRIVATE))
