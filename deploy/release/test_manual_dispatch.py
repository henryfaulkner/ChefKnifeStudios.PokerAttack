"""Manual entry cannot upload around workflow leases or enable production."""
import json
import unittest
import manual_client as manual


class ManualDispatchTests(unittest.TestCase):
    def setUp(self):self.calls=[];self.enabled='true'
    def runner(self,args):
        self.calls.append(args)
        if args[0]=='workflow':return b''
        if args[1].endswith('/DEPLOYMENT_AUTOMATION_ENABLED'):return json.dumps({'value':self.enabled}).encode()
        if args[1].endswith('/client.yml'):return json.dumps({'path':'.github/workflows/client.yml','state':'active'}).encode()
        return json.dumps({'full_name':manual.REPOSITORY,'default_branch':'main'}).encode()
    def test_manual_release_dispatches_only_fixed_main_workflow(self):
        result=manual.dispatch('release',runner=self.runner)
        self.assertEqual(result['ref'],'main')
        self.assertEqual(self.calls[-1],['workflow','run','client.yml','--repo',manual.REPOSITORY,'--ref','main','-f','operation=release'])
    def test_disabled_rollout_cannot_be_enabled_or_dispatched(self):
        self.enabled='false'
        with self.assertRaises(ValueError):manual.dispatch('release',runner=self.runner)
        self.assertTrue(all(call[0]=='api' and '--method' not in call for call in self.calls))
    def test_restore_carries_only_bounded_exact_accepted_identity(self):
        selected={'run':1,'attempt':2,'artifact':3,'checksum':'a'*64}
        manual.dispatch('restore',selected,self.runner)
        self.assertIn('restore_checksum='+'a'*64,self.calls[-1])
        for key,value in [('run',True),('artifact',0),('checksum','../secret')]:
            with self.subTest(key=key),self.assertRaises(ValueError):manual.dispatch('restore',{**selected,key:value},self.runner)
    def test_unleased_maintenance_and_arbitrary_directories_are_refused(self):
        with self.assertRaises(ValueError):manual.dispatch('maintenance',runner=self.runner)
        with self.assertRaises(ValueError):manual.dispatch('release',{'directory':'artifacts/client'},self.runner)
        self.assertEqual(self.calls,[])


if __name__=='__main__':unittest.main()
