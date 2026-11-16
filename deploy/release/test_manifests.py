"""Contract tests: these fictional records never establish deployment evidence."""
import copy
import json
import unittest

from jsonschema import Draft202012Validator, ValidationError
from manifests import ROOT, load_document, validate_document


class ManifestTests(unittest.TestCase):
    def example(self, kind):
        return json.loads((ROOT / (kind + '.example.json')).read_text(encoding='utf-8'))

    def test_schemas_and_examples(self):
        for kind in ('server-manifest', 'client-manifest', 'evidence'):
            with self.subTest(kind=kind):
                Draft202012Validator.check_schema(json.loads((ROOT / (kind + '.schema.json')).read_text()))
                validate_document(kind, self.example(kind))

    def test_image_source_platform_and_digest_boundaries(self):
        good = self.example('server-manifest')
        for field, value in [('digest', 'latest'), ('source_revision', '0' * 40), ('platform', 'linux/arm64'),
                             ('repository', 'chefknife.azurecr.io/another-app'), ('tag', '0' * 40)]:
            candidate = copy.deepcopy(good)
            candidate['api'][field] = value
            with self.subTest(field=field), self.assertRaises((ValueError, ValidationError)):
                validate_document('server-manifest', candidate)
        self.assertNotEqual(good['api']['local_image_id'], good['api']['digest'])

    def test_non_success_evidence_is_rejected(self):
        for value in (False, 'true', 1, None):
            candidate = self.example('evidence')
            candidate['checks']['private_message_isolation'] = value
            with self.subTest(value=value), self.assertRaises(ValidationError):
                validate_document('evidence', candidate)

    def test_artifact_reference_and_path_injection(self):
        for path in ('../outside', '/absolute', 'a/../../outside', 'a\\b', 'C:/file', './index.html', 'a//b', 'a/'):
            candidate = self.example('client-manifest')
            candidate['files'][path] = '0' * 64
            with self.subTest(path=path), self.assertRaises((ValueError, ValidationError)):
                validate_document('client-manifest', candidate)
        candidate = self.example('client-manifest')
        candidate['artifact_id'] = 'ambiguous-name.zip'
        with self.assertRaises(ValidationError):
            validate_document('client-manifest', candidate)

    def test_run_reference_and_unreviewed_schema(self):
        candidate = self.example('server-manifest')
        candidate['workflow']['run_id'] = 2
        with self.assertRaises(ValueError):
            validate_document('server-manifest', candidate)
        candidate = self.example('server-manifest')
        candidate['schema']['compatible_with_known_good'] = False
        with self.assertRaises(ValidationError):
            validate_document('server-manifest', candidate)

    def test_unknown_fields_duplicate_keys_and_bounded_json(self):
        candidate = self.example('server-manifest')
        candidate['config_path'] = '/etc/other-app/secret'
        with self.assertRaises(ValidationError):
            validate_document('server-manifest', candidate)
        for raw in (b'{"checks":{},"checks":{}}', b' ' * 65537, b'{"x":NaN}'):
            with self.assertRaises(ValueError):
                load_document(raw)


if __name__ == '__main__':
    unittest.main(verbosity=2)
