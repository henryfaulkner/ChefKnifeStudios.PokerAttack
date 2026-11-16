import json
from pathlib import Path
import re
import unittest

import dashboard
import docker_snapshot


class DashboardTests(unittest.TestCase):
    def setUp(self):self.doc=json.loads(Path(__file__).with_name('linode-docker-dashboard.json').read_bytes())
    def test_definition_matches_generator_and_one_discovered_multiselect_dashboard(self):
        self.assertEqual(self.doc,dashboard.build())
        self.assertEqual(self.doc['title'],'Linode Docker Containers')
        self.assertEqual(self.doc['time'],{'from':'now-1h','to':'now'})
        self.assertEqual(self.doc['refresh'],'30s')
        self.assertEqual([item['name'] for item in self.doc['templating']['list']],['host','container'])
        self.assertTrue(all(item['multi'] and item['includeAll'] for item in self.doc['templating']['list']))
        self.assertIn('label_values(linode_docker_container_info',self.doc['templating']['list'][1]['query'])

    def test_queries_reference_only_actual_contract_metrics_and_keep_incarnations(self):
        for panel in self.doc['panels']:
            for target in panel['targets']:
                expr=target['expr']
                names=re.findall(r'linode_docker_([a-z_]+)',expr)
                self.assertTrue(names)
                self.assertTrue(set(names).issubset(docker_snapshot.METRICS))
                if 'container_' in expr:
                    self.assertIn('container_id',expr)
                    self.assertIn('container_identity_timestamp_seconds',expr)
                if 'rate(' in expr:
                    self.assertIn('[4m]',expr);self.assertIn('container_state',expr)
                    self.assertNotRegex(expr,r'sum\s+by\s*\(container\)')

    def test_unknown_health_unlimited_and_shared_network_are_explicit(self):
        raw=json.dumps(self.doc)
        for text in ('Unknown / stale','Not configured','Shared / unavailable','Unlimited','Not provided'):
            self.assertIn(text,raw)
        self.assertIn('image_id',raw);self.assertIn('registry_digest',raw);self.assertIn('configured_image',raw)
        self.assertIn('container_sample_timestamp_seconds',dashboard.GOOD)
        self.assertIn('180',dashboard.FRESH)
        self.assertIn(' > 0',dashboard.CPU_PERCENT)
        self.assertIn(' > 0',dashboard.MEMORY_PERCENT)


if __name__=='__main__':unittest.main(verbosity=2)
