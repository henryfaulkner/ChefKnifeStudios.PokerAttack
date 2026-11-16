import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import ci_docker_store as store


class RunnerStoreTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(); self.addCleanup(self.directory.cleanup)
        self.config = Path(self.directory.name)/'daemon.json'
        self.env={'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted'}
        for field,value in [('CONFIG',self.config)]:
            change=patch.object(store,field,value);change.start();self.addCleanup(change.stop)

    def test_developer_or_nonhosted_execution_cannot_modify_docker(self):
        with patch.object(store.sys,'platform','linux'), patch.object(store.os,'geteuid',return_value=0,create=True), patch.object(store,'command') as command:
            for env in ({},{'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'self-hosted'}):
                with self.assertRaises(ValueError):store.configure(env)
            command.assert_not_called()
        self.assertFalse(self.config.exists())

    def test_existing_compatible_store_needs_no_restart_or_config_write(self):
        with patch.object(store.sys,'platform','linux'), patch.object(store.os,'geteuid',return_value=0,create=True), patch.object(store,'compatible',return_value=True), patch.object(store,'command') as command:
            self.assertFalse(store.configure(self.env)['restarted']);command.assert_not_called()
        self.assertFalse(self.config.exists())

    def test_only_disposable_runner_profile_changes_preserving_other_configuration(self):
        self.config.write_text('{"log-driver":"local","features":{"other":true}}')
        with patch.object(store.sys,'platform','linux'), patch.object(store.os,'geteuid',return_value=0,create=True), patch.object(store.os,'fchmod',create=True), patch.object(store,'compatible',side_effect=[False,True]), patch.object(store,'command') as command:
            self.assertTrue(store.configure(self.env)['restarted'])
            command.assert_called_once_with('systemctl','restart','docker')
        config=json.loads(self.config.read_bytes())
        self.assertEqual(config['log-driver'],'local')
        self.assertEqual(config['features'],{'other':True,'containerd-snapshotter':True})

    def test_image_store_probe_requires_exact_containerd_driver(self):
        for value,expected in [([['driver-type','io.containerd.snapshotter.v1']],True),([['Backing Filesystem','extfs']],False)]:
            with patch.object(store,'command',return_value=json.dumps(value).encode()):
                self.assertEqual(store.compatible(),expected)
