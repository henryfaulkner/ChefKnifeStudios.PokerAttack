import copy
import json
from pathlib import Path
import unittest
import alloy_textfile as config


class AlloyTests(unittest.TestCase):
    def setUp(self):
        shape=json.loads((Path(__file__).parents[2]/'docs/deployment-evidence/G01-live-monitoring-shape.json').read_bytes())
        self.raw='\n\n'.join(item['configuration'] for item in shape['blocks'])+'\n'
        self.raw+='\nprometheus.remote_write "fixture" {\n  endpoint {\n    url = "https://example.invalid/{balanced}"\n    basic_auth { password = "PRIVATE-FIXTURE" }\n  }\n}\n'

    def test_preserves_every_existing_byte_except_two_additions_and_inverse_is_exact(self):
        updated=config.prepare(self.raw)
        self.assertEqual(config.rollback(updated),self.raw)
        self.assertEqual(updated.count('/var/lib/linode-docker-metrics'),1)
        self.assertEqual(updated.count('|linode_docker_.*'),1)
        self.assertIn('scrape_interval = "60s"',updated)
        before=self.raw[self.raw.index('prometheus.remote_write "fixture"'):]
        self.assertEqual(updated[updated.index('prometheus.remote_write "fixture"'):],before)

    def test_unexpected_existing_textfile_or_pipeline_rejected(self):
        for raw in (config.prepare(self.raw),self.raw.replace(config.FORWARD,'other.receiver'),
                    self.raw.replace('node_textfile_scrape_error','removed_metric'),self.raw+'\n'+self.raw,
                    self.raw.replace('disable_collectors = [','disable_collectors = ["textfile",')):
            with self.assertRaises(ValueError):config.prepare(raw)

    def test_rollback_preserves_unrelated_followup_changes_and_refuses_modified_addition(self):
        updated=config.prepare(self.raw)+'\n// unrelated later change\n'
        self.assertEqual(config.rollback(updated),self.raw+'\n// unrelated later change\n')
        with self.assertRaises(ValueError):config.rollback(updated.replace('/var/lib/linode-docker-metrics','/other'))


if __name__=='__main__':unittest.main(verbosity=2)
