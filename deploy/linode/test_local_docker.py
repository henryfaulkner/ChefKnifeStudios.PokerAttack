import json
import os
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from local_docker import assert_local_docker


class LocalDockerTests(unittest.TestCase):
    def test_ssh_tcp_and_remote_environment_cannot_run_fixtures(self):
        for endpoint in ('ssh://root@170.187.142.115', 'tcp://170.187.142.115:2376', 'tcp://localhost:2375'):
            result = SimpleNamespace(returncode=0, stdout=json.dumps([{'Endpoints': {'docker': {'Host': endpoint}}}]))
            with patch.dict(os.environ, {}, clear=True), patch('local_docker.subprocess.run', return_value=result):
                with self.assertRaises(ValueError): assert_local_docker()
            with patch.dict(os.environ, {'DOCKER_HOST': endpoint}, clear=True):
                with self.assertRaises(ValueError): assert_local_docker()

    def test_supported_local_daemon_endpoints_are_allowed(self):
        for endpoint in ('unix:///var/run/docker.sock', 'npipe:////./pipe/dockerDesktopLinuxEngine'):
            result = SimpleNamespace(returncode=0, stdout=json.dumps([{'Endpoints': {'docker': {'Host': endpoint}}}]))
            with patch.dict(os.environ, {}, clear=True), patch('local_docker.subprocess.run', return_value=result):
                self.assertTrue(assert_local_docker())
