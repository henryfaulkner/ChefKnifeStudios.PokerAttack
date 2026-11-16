"""H04 real Docker lifecycle and installed bounded collector on owned replica."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import sys
import time

from initialize_replica import machine_guard, PRIVATE, ROOT, REGISTRY, run, write


def module():
    spec=importlib.util.spec_from_file_location('snapshot', '/opt/linode-docker-metrics/docker_snapshot.py')
    result=importlib.util.module_from_spec(spec);spec.loader.exec_module(result)
    return result


def samples(path):
    result=[]
    for line in path.read_text().splitlines():
        if not line or line.startswith('#'):continue
        match=re.fullmatch(r'(linode_docker_\w+)\{(.*)\} ([\d.eE+-]+)',line)
        if not match:raise ValueError('Malformed collector exposition')
        labels={key:json.loads('"'+value+'"') for key,value in re.findall(r'(\w+)="((?:\\.|[^"\\])*)"',match[2])}
        result.append((match[1].removeprefix('linode_docker_'),labels,float(match[3])))
    return result


def value(series,name,cid=None):
    matches=[number for metric,labels,number in series if metric==name and (cid is None or labels.get('container_id')==cid)]
    if len(matches)!=1:raise ValueError('Expected one current metric '+name)
    return matches[0]


def main():
    machine_guard();report={'task':'H04','fixture_only':True,'production_mutation':False,'phase':'starting','cases':[]}
    if sys.argv[1:] not in ([],['--repeat-after-fixture-fix'],['--repeat-after-import-inspection']):raise ValueError('Explicit known-outcome repetition only')
    repeat=bool(sys.argv[1:])
    attempt=3 if sys.argv[1:]==['--repeat-after-import-inspection'] else 2 if repeat else 1
    output=PRIVATE/('H04-lifecycle-attempt'+str(attempt)+'.json' if repeat else 'H04-lifecycle.json');owned={};prefix='h04-lifecycle-'
    directory=Path('/var/lib/linode-docker-metrics');metric=directory/'docker.prom'
    try:
        if run('docker','ps','-aq','--filter','name='+prefix):raise ValueError('Inspect prior H04 containers before repeating')
        canaries={name:json.loads(run('docker','inspect',name))[0]['Id'] for name in ('h02-caddy','h02-registry','pokerattack-production-api-1')}
        for role in ('healthy','unhealthy','nohealth','paused','exited','metadata'):
            name=prefix+role
            args=['docker','create','--name',name,'--label','h04-owned=true','--label','private=H04_NEVER_EXPORT',
                  '--env','PASSWORD=H04_NEVER_EXPORT','--cpus','0.10','--memory','32m']
            if role in ('healthy','unhealthy'):
                args+=['--health-cmd','true' if role=='healthy' else 'false','--health-interval','1s','--health-retries','1','--health-timeout','1s']
            image=REGISTRY
            if role=='metadata':
                # The official registry image actually carries a valid source
                # label. Import only our already-created fixture filesystem to
                # obtain a genuinely unlabeled local image with no RepoDigest.
                prior=json.loads(run('docker','inspect',owned['healthy']))[0]
                if prior['Config']['Labels'].get('h04-owned')!='true':raise ValueError('Refuse export of unowned filesystem')
                filesystem=PRIVATE/'H04-owned-registry-filesystem.tar'
                with filesystem.open('wb') as stream:
                    exported=subprocess.run(['docker','export',owned['healthy']],stdout=stream,stderr=subprocess.PIPE,timeout=60)
                if exported.returncode:raise ValueError('Owned filesystem export failed')
                # With the containerd image store, importing WITH a tag creates
                # a local RepoDigest. Import anonymously and configure by local
                # ID so no registry identity is asserted or fabricated.
                image=run('docker','import',str(filesystem))
            args+=['--entrypoint','/bin/sh',image,'-c','exit 7' if role=='exited' else 'while :; do sleep 1; done']
            cid=run(*args);owned[role]=cid;run('docker','start',cid)
        for _ in range(30):
            healthy=json.loads(run('docker','inspect',owned['healthy']))[0]['State']['Health']['Status']
            unhealthy=json.loads(run('docker','inspect',owned['unhealthy']))[0]['State']['Health']['Status']
            if healthy=='healthy' and unhealthy=='unhealthy':break
            time.sleep(1)
        if healthy!='healthy' or unhealthy!='unhealthy':raise ValueError('Actual health states not reached')
        run('docker','pause',owned['paused'])
        install=Path('/opt/linode-docker-metrics');install.mkdir(mode=0o755,exist_ok=True);install.chmod(0o755)
        write(install/'docker_snapshot.py',(ROOT/'deploy/monitoring/docker_snapshot.py').read_bytes(),0o644)
        for name in ('linode-docker-snapshot.service','linode-docker-snapshot.timer'):
            target=Path('/etc/systemd/system')/name
            expected=(ROOT/'deploy/monitoring'/name).read_bytes()
            if target.exists():
                if not repeat or target.is_symlink() or target.read_bytes()!=expected:raise ValueError('Do not replace an existing collector unit')
            else:write(target,expected,0o644)
        run('systemd-analyze','verify','/etc/systemd/system/linode-docker-snapshot.service','/etc/systemd/system/linode-docker-snapshot.timer')
        run('systemctl','daemon-reload');run('systemctl','start','linode-docker-snapshot.service')
        c=module();host=socket.gethostname();series=samples(metric)
        if value(series,'collection_success')!=1:raise ValueError('Installed collector failed')
        for role,cid in owned.items():
            raw=json.loads(run('docker','inspect',cid))[0]
            if value(series,'container_state',cid)!=c.STATES[raw['State']['Status']]:raise ValueError('Docker state differs')
            if value(series,'container_health',cid)!=c.HEALTH[raw['State'].get('Health',{}).get('Status','none')]:raise ValueError('Health differs')
            if value(series,'container_cpu_limit_cores',cid)!=0.1 or value(series,'container_memory_limit_bytes',cid)!=33554432:
                raise ValueError('Configured limits differ')
        if value(series,'container_exit_code',owned['exited'])!=7:raise ValueError('Exit status differs')
        metadata=[labels for name,labels,_ in series if name=='container_info' and labels['container_id']==owned['metadata']][0]
        if metadata['registry_digest']!='Unavailable' or metadata['source_revision']!='Not provided':raise ValueError('Missing metadata fabricated')
        if 'H04_NEVER_EXPORT' in metric.read_text():raise ValueError('Non-allowlisted data escaped')
        if any(set(labels)-{'host','container_id','container','compose_project','compose_service','configured_image','image_id','registry_digest','source_revision','network_mode'} for _,labels,_ in series):
            raise ValueError('Unexpected label cardinality')
        report['cases'].append({'name':'native-systemd-and-Docker-state-health-limits-exit-metadata-allowlist','passed':True,'containers':len(owned),'series_count':len(series)})
        # Compare resource values to the exact independent Docker GET responses
        # consumed by the real collector, avoiding natural CPU/RSS sample drift.
        class RecordedDocker(c.Docker):
            def __init__(self):super().__init__();self.responses={}
            def get(self,path):
                result=super().get(path);self.responses[path]=result;return result
        reader=RecordedDocker();private=PRIVATE/'H04-resource-reference';private.mkdir(mode=0o700)
        if not c.snapshot(reader,private,host):raise ValueError('Reference snapshot incomplete')
        checked=0
        for name,labels,number in samples(private/'docker.prom'):
            cid=labels.get('container_id')
            if cid not in owned.values():continue
            stat=reader.responses.get('/containers/'+cid+'/stats?stream=false&one-shot=true')
            if not stat:continue
            if name=='container_cpu_usage_seconds_total' and number!=stat['cpu_stats']['cpu_usage']['total_usage']/1e9:raise ValueError('CPU reference differs')
            if name=='container_memory_usage_bytes' and number!=stat['memory_stats']['usage']:raise ValueError('Memory reference differs')
            if name in ('container_cpu_usage_seconds_total','container_memory_usage_bytes'):checked+=1
        report['cases'].append({'name':'real-Docker-resource-reference','passed':True,'exact_resource_comparisons':checked,'GET_only_calls':len(reader.responses)})
        old=owned['nohealth'];run('docker','rm','-f',old);owned.pop('nohealth')
        new=run('docker','run','-d','--name',prefix+'nohealth','--label','h04-owned=true','--entrypoint','/bin/sh',REGISTRY,'-c','while :; do sleep 1; done');owned['nohealth']=new
        run('systemctl','start','linode-docker-snapshot.service');series=samples(metric)
        if old==new or any(labels.get('container_id')==old for _,labels,_ in series) or value(series,'container_state',new)!=2:
            raise ValueError('Recreation retained stale identity')
        removed=owned.pop('exited');run('docker','rm',removed);run('systemctl','start','linode-docker-snapshot.service')
        if any(labels.get('container_id')==removed for _,labels,_ in samples(metric)):raise ValueError('Removed identity remains current')
        report['cases'].append({'name':'actual-recreated-and-removed-current-inventory','passed':True,'old_and_new_ids_distinct':True})
        # A failed Unix connection exercises daemon-unavailable semantics without
        # stopping Docker or disrupting any shared fixture service.
        prior=samples(private/'docker.prom');last=value(prior,'last_success_timestamp_seconds')
        if c.snapshot(c.Docker(path=str(PRIVATE/'nonexistent-h04.sock')),private,host):raise ValueError('Unavailable socket reported success')
        failed=samples(private/'docker.prom')
        if value(failed,'inventory_success')!=0 or value(failed,'last_success_timestamp_seconds')!=last:raise ValueError('Failure advanced freshness')
        # Pause exactly before atomic rename, then SIGKILL the real child. The
        # old complete metric file must survive; a new full snapshot repairs it.
        before=metric.read_bytes();marker=PRIVATE/'H04-before-rename'
        code="import sys,time;from pathlib import Path;sys.path.insert(0,'/opt/linode-docker-metrics');import docker_snapshot as c;original=c.os.replace\ndef held(a,b):\n Path("+repr(str(marker))+ ").write_text('before-rename');time.sleep(60);original(a,b)\nc.os.replace=held;c.snapshot(c.Docker(),c.DIRECTORY,"+repr(host)+")"
        child=subprocess.Popen(['/usr/bin/python3','-c',code],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        try:
            for _ in range(100):
                if marker.exists():break
                if child.poll() is not None:raise ValueError('Interruption child failed early')
                time.sleep(0.1)
            if not marker.exists():raise ValueError('Atomic interruption boundary not reached')
            child.kill();child.wait(timeout=5)
        finally:
            if child.poll() is None:child.kill();child.wait(timeout=5)
        if metric.read_bytes()!=before:raise ValueError('Killed collector damaged published snapshot')
        run('systemctl','start','linode-docker-snapshot.service')
        report['cases'].append({'name':'unavailable-socket-and-killed-atomic-writer','passed':True,'last_success_not_advanced_on_failure':True,'previous_snapshot_unchanged_on_SIGKILL':True,'current_snapshot_recovered':value(samples(metric),'collection_success')==1})
        # Measure installed unit at its actual 10% CPU / 64MiB bounds. Repeated
        # starts are sampled without increasing scrape cadence on any live host.
        measurements=[]
        for _ in range(5):
            start=time.monotonic();run('systemctl','start','linode-docker-snapshot.service')
            properties=dict(line.split('=',1) for line in run('systemctl','show','linode-docker-snapshot.service','-p','CPUUsageNSec','-p','MemoryPeak','-p','CPUQuotaPerSecUSec','-p','MemoryMax','-p','Result').splitlines())
            measurements.append({'wall_seconds':time.monotonic()-start,'collection_seconds':value(samples(metric),'collection_duration_seconds'),**properties})
        if any(x['Result']!='success' or x['collection_seconds']>=22 for x in measurements):raise ValueError('Collector exceeded budget')
        process_measurements=[]
        for index in range(3):
            timing=PRIVATE/('H04-process-time-'+str(index)+'.json')
            run('/usr/bin/time','-f','{"user_seconds":%U,"system_seconds":%S,"max_rss_kib":%M,"elapsed_seconds":%e}',
                '-o',str(timing),'/usr/bin/python3','-I',str(install/'docker_snapshot.py'))
            measured=json.loads(timing.read_bytes())
            if measured['max_rss_kib']*1024>=67108864:raise ValueError('Measured process exceeds configured memory budget')
            measured['average_core_percent_at_30s']=(measured['user_seconds']+measured['system_seconds'])/30*100
            process_measurements.append(measured)
        report['overhead']={'unit_runs':measurements,'process_measurements':process_measurements,'timer_seconds':30,'CPU_quota_percent':10,'memory_max_bytes':67108864,'not_a_production_measurement':True}
        after={name:json.loads(run('docker','inspect',name))[0]['Id'] for name in canaries}
        if after!=canaries:raise ValueError('Collector changed monitored application identity')
        report.update(phase='passed',monitored_canary_ids_unchanged=True,collector_source_sha256=hashlib.sha256((install/'docker_snapshot.py').read_bytes()).hexdigest(),dashboard_rendering_and_live_ingestion='not claimed; J remains')
    except Exception as error:
        report.update(phase='failed-or-incomplete',error_type=type(error).__name__);raise
    finally:
        report['fixture_container_cleanup']=[]
        for role,cid in owned.items():
            raw=json.loads(run('docker','inspect',cid))[0]
            if raw['Id']!=cid or raw['Config']['Labels'].get('h04-owned')!='true' or raw['Name']!='/'+prefix+role:
                raise ValueError('Refuse to delete unowned fixture container')
            run('docker','rm','-f',cid);report['fixture_container_cleanup'].append(cid)
        output.write_text(json.dumps(report,indent=2)+'\n')


if __name__=='__main__':main()
