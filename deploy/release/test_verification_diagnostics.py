import subprocess
import unittest
from unittest.mock import patch

import server_verify


class SafeStageTests(unittest.TestCase):
    def test_known_stage_only_is_preserved(self):
        self.assertEqual(server_verify.failed_stage(b'Release verification failed at browser-signalr; no promotable evidence emitted\n'),'browser-signalr')

    def test_provider_credentials_unrecognized_stage_and_large_output_are_withheld(self):
        for raw in (b'Authorization: PRIVATE-FIXTURE',b'Release verification failed at private-secret; no promotable evidence emitted',
                    b'Release verification failed at '+b'a'*64+b'; no promotable evidence emitted',b'x'*4097):
            self.assertEqual(server_verify.failed_stage(raw),'unknown')

    def test_gameplay_failure_reports_safe_stage_and_keeps_child_authority_stripped(self):
        result=subprocess.CompletedProcess([],1,b'PRIVATE-FIXTURE',b'Release verification failed at served-release-identity; no promotable evidence emitted\n')
        with patch.object(server_verify.subprocess,'run',return_value=result) as run:
            with self.assertRaisesRegex(ValueError,'failed at served-release-identity'):
                server_verify.gameplay('context.json','result.json',{'GH_TOKEN':'PRIVATE-FIXTURE','LINODE_EVIDENCE_PRIVATE_KEY':'PRIVATE-FIXTURE'})
            env=run.call_args.kwargs['env']
            self.assertNotIn('GH_TOKEN',env);self.assertNotIn('LINODE_EVIDENCE_PRIVATE_KEY',env)


if __name__=='__main__':unittest.main(verbosity=2)
