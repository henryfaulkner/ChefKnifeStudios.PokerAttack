import copy
import json
from pathlib import Path
import unittest

import host_manifests


class HostManifestTests(unittest.TestCase):
    def setUp(self):
        self.manifest = json.loads((Path(__file__).parents[1] / 'release/server-manifest.example.json').read_text())

    def test_schema_example_passes_host_contract(self):
        host_manifests.server(self.manifest)

    def test_identity_repository_platform_and_source_substitution_fail(self):
        for field, value in [('digest', 'latest'), ('source_revision', '0' * 40),
                             ('platform', 'linux/arm64'), ('repository', 'unrelated/image')]:
            candidate = copy.deepcopy(self.manifest)
            candidate['api'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                host_manifests.server(candidate)

    def test_unknown_fields_wrong_run_and_unreviewed_schema_fail(self):
        for fault in ('field', 'run', 'schema'):
            candidate = copy.deepcopy(self.manifest)
            if fault == 'field': candidate['config_path'] = '/arbitrary'
            if fault == 'run': candidate['workflow']['run_id'] = True
            if fault == 'schema': candidate['schema']['compatible_with_known_good'] = False
            with self.subTest(fault=fault), self.assertRaises(ValueError):
                host_manifests.server(candidate)


if __name__ == '__main__':
    unittest.main(verbosity=2)
