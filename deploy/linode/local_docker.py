"""Refuse remote Docker contexts before any disposable-fixture mutation."""
import json
import os
import subprocess

LOCAL_ENDPOINTS = {'unix:///var/run/docker.sock', 'npipe:////./pipe/dockerDesktopLinuxEngine',
                   'npipe:////./pipe/docker_engine'}


def assert_local_docker():
    if os.environ.get('DOCKER_HOST') and os.environ['DOCKER_HOST'] not in LOCAL_ENDPOINTS:
        raise ValueError('Disposable fixtures require a local Docker endpoint')
    if os.environ.get('DOCKER_TLS_VERIFY') or os.environ.get('DOCKER_CERT_PATH'):
        raise ValueError('Remote Docker credentials are prohibited for fixtures')
    result = subprocess.run(['docker', 'context', 'inspect'], capture_output=True, text=True, timeout=10)
    if result.returncode: raise ValueError('Local Docker context unavailable')
    values = json.loads(result.stdout)
    if len(values) != 1 or values[0]['Endpoints']['docker']['Host'] not in LOCAL_ENDPOINTS:
        raise ValueError('Disposable fixtures refuse a remote Docker context')
    return True
