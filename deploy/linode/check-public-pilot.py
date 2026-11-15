"""Manual pilot-only browser verification. Production URLs are never selectable."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import subprocess
import sys
import threading
import time
import urllib.request
import uuid

repo = Path(__file__).resolve().parents[2]
artifact = repo / 'artifacts' / ('public-pilot-client-' + uuid.uuid4().hex[:8])
public = 'https://pilot-api.playpokerattack.com'
source = repo / 'artifacts/client-wp04/wwwroot'
result = subprocess.run(['node', str(repo / 'deploy/prepare-client-artifact.mjs'),
                         '--source', str(source), '--output', str(artifact), '--api', public])
if result.returncode:
    sys.exit(result.returncode)


class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self, *_):
        pass


server = ThreadingHTTPServer(('127.0.0.1', 5173), partial(QuietHandler, directory=str(artifact)))
thread = threading.Thread(target=server.serve_forever, daemon=True)
tunnel = None
with (artifact / 'tunnel.log').open('w') as log:
    try:
        tunnel = subprocess.Popen(['ssh', '-N', '-L', '127.0.0.1:18082:127.0.0.1:8082',
                                   '-o', 'ExitOnForwardFailure=yes', '-o', 'BatchMode=yes',
                                   '-o', 'IdentitiesOnly=yes', '-o', 'StrictHostKeyChecking=yes',
                                   '-i', str(Path.home() / '.ssh/id_ed25519_linode_us_southeast'),
                                   'root@170.187.142.115'], stdin=subprocess.DEVNULL,
                                  stdout=subprocess.DEVNULL, stderr=log)
        for _ in range(20):
            if tunnel.poll() is not None:
                raise RuntimeError('Private pilot tunnel failed; inspect local tunnel log')
            try:
                with urllib.request.urlopen('http://127.0.0.1:18082/health/ready', timeout=3) as response:
                    assert response.status == 200
                    break
            except OSError:
                time.sleep(0.5)
        else:
            raise RuntimeError('Private pilot readiness timeout')
        thread.start()
        spec = 'multiplayer-game.spec.ts' if '--full' in sys.argv[1:] else 'migration-smoke.spec.ts'
        result = subprocess.run(['npm.cmd' if os.name == 'nt' else 'npm', '--prefix', 'tests', 'test', '--', spec, '--reporter=list'],
                                cwd=repo, env={**os.environ, 'CLIENT_URL': 'http://localhost:5173',
                                               'API_URL_CLEANUP': 'http://127.0.0.1:18082', 'API_PUBLIC_URL': public})
        if result.returncode:
            raise RuntimeError('Public pilot browser checks failed')
        print('PASS: isolated client used public pilot HTTPS; cleanup used only the private SSH tunnel.')
    finally:
        if thread.is_alive():
            server.shutdown()
            thread.join(timeout=5)
        server.server_close()
        if tunnel:
            tunnel.terminate()
            tunnel.wait(timeout=10)
