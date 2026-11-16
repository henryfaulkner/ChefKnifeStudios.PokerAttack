import copy
import json
from pathlib import Path
import tempfile
import socket
import threading
import time
import unittest
from unittest.mock import patch

import docker_snapshot as collector

CID='a'*64
IID='sha256:'+'b'*64
DIGEST='registry.example/app@sha256:'+'c'*64


class DockerFixture:
    def __init__(self):
        self.containers=[{'Id':CID,'Names':['/example']}]
        self.inspect={'Id':CID,'Name':'/example','Image':IID,'Created':'2026-11-15T00:00:00Z','RestartCount':2,
            'Config':{'Image':'registry.example/app:main','Env':['PASSWORD=NEVER-EXPORT'],
                'Labels':{'secret':'NEVER-EXPORT','com.docker.compose.project':'shared','com.docker.compose.service':'example'}},
            'HostConfig':{'NetworkMode':'bridge','NanoCpus':250000000,'Memory':536870912},
            'State':{'Status':'running','OOMKilled':False,'StartedAt':'2026-11-15T00:01:00Z',
                'FinishedAt':'0001-01-01T00:00:00Z','ExitCode':0,
                'Health':{'Status':'healthy','Log':[{'Output':'NEVER-EXPORT'}]}}}
        self.image={'Id':IID,'RepoDigests':[DIGEST],'Config':{'Labels':{'org.opencontainers.image.revision':'d'*40,'secret':'NEVER-EXPORT'}}}
        self.stats={'cpu_stats':{'cpu_usage':{'total_usage':2500000000}},
            'memory_stats':{'usage':100000,'stats':{'inactive_file':1000}},
            'networks':{'eth0':{'rx_bytes':123,'tx_bytes':456}},
            'blkio_stats':{'io_service_bytes_recursive':[{'op':'Read','value':100},{'op':'Write','value':200}]}}
        self.calls=[];self.fail=None
    def get(self,path):
        self.calls.append(path)
        if path==self.fail:raise TimeoutError('Permission/daemon/deadline failure')
        if path=='/containers/json?all=1':return copy.deepcopy(self.containers)
        if path.endswith('/stats?stream=false&one-shot=true'):return copy.deepcopy(self.stats)
        if path.startswith('/images/'):return copy.deepcopy(self.image)
        if path.startswith('/containers/'):return copy.deepcopy(self.inspect)
        raise AssertionError('Non-GET or unknown path')


def samples(metrics,name):
    return [line for line in metrics.lines if line.startswith(collector.PREFIX+name+'{')]


class CollectorTests(unittest.TestCase):
    def setUp(self):self.docker=DockerFixture()
    def collect(self,cache=None):return collector.collect(self.docker,'fixture-host',cache,1000)

    def test_real_units_full_distinct_identity_and_no_sensitive_fields(self):
        metrics,success,_=self.collect();self.assertTrue(success)
        raw=metrics.render().decode()
        self.assertIn('registry_digest="'+DIGEST+'"',raw)
        self.assertIn('image_id="'+IID+'"',raw)
        self.assertIn('configured_image="registry.example/app:main"',raw)
        for secret in ('PASSWORD','NEVER-EXPORT','Health','mount'):self.assertNotIn(secret,raw)
        self.assertTrue(samples(metrics,'container_cpu_usage_seconds_total')[0].endswith(' 2.5\n'))
        self.assertTrue(samples(metrics,'container_cpu_limit_cores')[0].endswith(' 0.25\n'))
        self.assertTrue(samples(metrics,'container_memory_working_set_bytes')[0].endswith(' 99000\n'))
        self.assertEqual(samples(metrics,'container_finished_timestamp_seconds'),[])

    def test_stopped_paused_and_missing_health_remain_in_inventory_without_running_stats(self):
        for state in ('exited','paused','created'):
            self.docker.inspect['State']['Status']=state
            self.docker.inspect['State'].pop('Health',None)
            metrics,success,_=self.collect();self.assertTrue(success)
            self.assertTrue(samples(metrics,'container_info'))
            self.assertTrue(samples(metrics,'container_health')[0].endswith(' 0\n'))
            self.assertFalse(samples(metrics,'container_cpu_usage_seconds_total'))
            self.assertFalse(any('/stats?' in path for path in self.docker.calls));self.docker.calls=[]

    def test_host_and_container_network_counters_are_omitted_not_zero(self):
        for mode in ('host','container:'+CID):
            self.docker.inspect['HostConfig']['NetworkMode']=mode
            metrics,success,_=self.collect();self.assertTrue(success)
            self.assertFalse(samples(metrics,'container_network_receive_bytes_total'))
            self.assertTrue(samples(metrics,'container_network_attributable')[0].endswith(' 0\n'))

    def test_unlimited_and_missing_metadata_are_explicit_not_fabricated(self):
        self.docker.inspect['HostConfig'].update(NanoCpus=0,Memory=0)
        self.docker.image['RepoDigests']=[];self.docker.image['Config']['Labels']={}
        metrics,success,_=self.collect();self.assertTrue(success)
        self.assertIn('registry_digest="Unavailable"',metrics.render().decode())
        self.assertIn('source_revision="Not provided"',metrics.render().decode())
        self.assertTrue(samples(metrics,'container_cpu_limit_cores')[0].endswith(' 0\n'))
        self.assertTrue(samples(metrics,'container_memory_limit_bytes')[0].endswith(' 0\n'))

    def test_digest_does_not_use_unrelated_repository_or_confuse_local_id(self):
        self.docker.image['RepoDigests']=['unrelated.example/app@sha256:'+'e'*64]
        metrics,success,_=self.collect();self.assertTrue(success)
        self.assertIn('registry_digest="Unavailable"',metrics.render().decode())
        metadata={'digests':['alpine@sha256:'+'f'*64]}
        self.assertEqual(collector.registry_digest('docker.io/library/alpine:latest',metadata),metadata['digests'][0])

    def test_disappearing_container_is_unknown_and_peer_is_collected(self):
        second='e'*64;self.docker.containers.append({'Id':second,'Names':['/survivor']})
        original=self.docker.get
        def get(path):
            if path=='/containers/'+CID+'/json':raise ValueError('Disappeared')
            result=original(path)
            if path=='/containers/'+second+'/json':result['Id']=second;result['Name']='/survivor'
            return result
        with patch.object(self.docker,'get',side_effect=get):metrics,success,_=self.collect()
        self.assertFalse(success)
        self.assertEqual(len(samples(metrics,'container_info')),2)
        self.assertEqual(len(samples(metrics,'container_state')),1)
        self.assertIn(second,samples(metrics,'container_state')[0])

    def test_image_metadata_cache_refreshes_after_five_minutes(self):
        _,_,cache=self.collect();self.docker.calls=[]
        self.collect(cache)
        self.assertFalse(any(path.startswith('/images/') for path in self.docker.calls))
        cache[IID]['sampled_unix']=699;self.docker.calls=[];self.collect(cache)
        self.assertTrue(any(path.startswith('/images/') for path in self.docker.calls))

    def test_escaping_bounded_input_and_malformed_values(self):
        metrics=collector.Metrics();metrics.emit('container_info',{'host':'quote"slash\\line\n'},1)
        self.assertIn('quote\\"slash\\\\line\\n',metrics.render().decode())
        for raw in (b'{"a":1,"a":2}',b'{"a":NaN}',b'x'*(collector.MAX_BYTES+1)):
            with self.assertRaises(ValueError):collector.document(raw)
        self.docker.containers*=257
        with self.assertRaises(ValueError):self.collect()
        with self.assertRaises(ValueError):collector.label('x'*513,512)
        with self.assertRaises(ValueError):collector.label('control\x00')

    def test_unhealthy_and_unknown_health_are_not_healthy(self):
        for status,code in [('unhealthy',2),('starting',3),('unexpected',4)]:
            self.docker.inspect['State']['Health']['Status']=status
            metrics,success,_=self.collect();self.assertTrue(success)
            self.assertTrue(samples(metrics,'container_health')[0].endswith(' '+str(code)+'\n'))

    def test_transport_refuses_mutations_arbitrary_paths_and_expired_deadline(self):
        client=collector.Docker(deadline=1)
        for path in ('/containers/'+CID+'/stop','/containers/'+CID+'/exec','/version','//other','/images/prune'):
            with self.assertRaises(ValueError):client.get(path)
        with self.assertRaises(TimeoutError):client.get('/containers/json?all=1')

    def test_readiness_uses_only_fixed_loopback_mapping_and_stays_separate_from_health(self):
        with patch.object(collector.http.client,'HTTPConnection') as connection:
            self.assertEqual(collector.readiness('other-container'),0)
            connection.assert_not_called()
            connection.return_value.getresponse.return_value.status=200
            self.assertEqual(collector.readiness('pokerattack-production-api-1'),1)
            connection.assert_called_with('127.0.0.1',8081,timeout=1)
            connection.return_value.request.assert_called_with('GET','/health/ready')
            connection.return_value.getresponse.return_value.status=503
            self.assertEqual(collector.readiness('pokerattack-production-api-1'),2)
            connection.return_value.getresponse.side_effect=TimeoutError()
            self.assertEqual(collector.readiness('pokerattack-production-api-1'),3)

    def test_actual_unix_transport_is_get_only_and_times_out_without_response(self):
        with tempfile.TemporaryDirectory() as directory:
            path=str(Path(directory)/'docker.sock')
            with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM) as server:
                server.bind(path);server.listen();requests=[]
                def respond():
                    connection,_=server.accept()
                    with connection:
                        requests.append(connection.recv(4096))
                        connection.sendall(b'HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n[]')
                worker=threading.Thread(target=respond);worker.start()
                self.assertEqual(collector.Docker(path).get('/containers/json?all=1'),[])
                worker.join(timeout=2);self.assertFalse(worker.is_alive())
                self.assertTrue(requests[0].startswith(b'GET /v1.47/containers/json?all=1 HTTP/1.1\r\n'))
                def stall():
                    connection,_=server.accept()
                    with connection:
                        connection.recv(4096);time.sleep(0.2)
                worker=threading.Thread(target=stall);worker.start();start=time.monotonic()
                with self.assertRaises(TimeoutError):collector.Docker(path,deadline=start+0.05).get('/containers/json?all=1')
                self.assertLess(time.monotonic()-start,0.2);worker.join(timeout=2)


class SnapshotTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.directory=Path(self.temp.name);self.docker=DockerFixture()
    def snapshot(self,now):return collector.snapshot(self.docker,self.directory,'fixture-host',now)
    def raw(self):return (self.directory/'docker.prom').read_text()

    def test_daemon_failure_retains_identity_but_marks_unknown_and_preserves_success_time(self):
        self.assertTrue(self.snapshot(1000));self.docker.fail='/containers/json?all=1'
        self.assertFalse(self.snapshot(1030));raw=self.raw()
        self.assertIn('linode_docker_inventory_success{host="fixture-host"} 0',raw)
        self.assertIn('linode_docker_last_success_timestamp_seconds{host="fixture-host"} 1000',raw)
        self.assertIn('container="example"',raw)
        self.assertNotIn('last_success_timestamp_seconds{host="fixture-host"} 1030',raw)

    def test_initial_daemon_failure_is_not_successful_empty_inventory(self):
        self.docker.fail='/containers/json?all=1';self.assertFalse(self.snapshot(1000))
        self.assertIn('linode_docker_collection_success{host="fixture-host"} 0',self.raw())
        self.assertIn('linode_docker_last_success_timestamp_seconds{host="fixture-host"} 0',self.raw())

    def test_partial_failure_does_not_advance_last_success_and_removal_drops_current_identity(self):
        self.snapshot(1000);self.docker.fail='/containers/'+CID+'/json'
        self.assertFalse(self.snapshot(1030))
        self.assertIn('linode_docker_inventory_success{host="fixture-host"} 1',self.raw())
        self.assertIn('linode_docker_last_success_timestamp_seconds{host="fixture-host"} 1000',self.raw())
        self.docker.fail=None;self.docker.containers=[];self.assertTrue(self.snapshot(1060))
        self.assertNotIn('container="example"',self.raw())
        self.assertEqual(json.loads((self.directory/'image-cache.json').read_bytes()),{})

    def test_failed_atomic_replace_preserves_previous_complete_bytes(self):
        self.snapshot(1000);before=(self.directory/'docker.prom').read_bytes()
        with patch.object(collector.os,'replace',side_effect=OSError('Interrupted write')):
            with self.assertRaises(OSError):self.snapshot(1030)
        self.assertEqual(before,(self.directory/'docker.prom').read_bytes())
        self.assertEqual(list(self.directory.glob('.docker-snapshot-*')),[])

    def test_symlink_output_is_refused_and_stopped_collector_never_refreshes_time(self):
        self.snapshot(1000);before=self.raw();self.assertEqual(before,self.raw())
        (self.directory/'docker.prom').unlink();target=self.directory/'other';target.write_text('unchanged')
        (self.directory/'docker.prom').symlink_to(target)
        with self.assertRaises(ValueError):self.snapshot(1030)
        self.assertEqual(target.read_text(),'unchanged')


if __name__=='__main__':unittest.main(verbosity=2)
