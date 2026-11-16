#!/usr/bin/env python3
"""Bounded GET-only Docker snapshot. No exec, mutation or raw inspect output."""
import datetime
import http.client
import json
import math
import os
from pathlib import Path
import re
import socket
import tempfile
import time

PREFIX='linode_docker_'
ID=re.compile(r'[a-f0-9]{64}')
IMAGE_ID=re.compile(r'sha256:[a-f0-9]{64}')
STATES={'created':1,'running':2,'paused':3,'restarting':4,'removing':5,'exited':6,'dead':7}
HEALTH={'none':0,'healthy':1,'unhealthy':2,'starting':3}
MAX_BYTES=4*1024*1024
MAX_CONTAINERS=256
DIRECTORY=Path('/var/lib/linode-docker-metrics')
# Stable monitoring identity of this shared VM; its kernel hostname may be
# "localhost". Match the existing Alloy instance and dashboard default without
# changing the machine hostname or accepting an environment-supplied identity.
HOST='expense-tracker-vm'
METRICS={
    'container_info':('gauge','Allowlisted container and image identity; value 1.'),
    'container_identity_timestamp_seconds':('gauge','Current inventory identity timestamp with the same allowlisted metadata labels.'),
    'container_sample_timestamp_seconds':('gauge','Latest attempt for this container identity, in Unix seconds.'),
    'container_collection_success':('gauge','Complete per-container inspection and running statistics; boolean.'),
    'container_state':('gauge','State: unknown 0 created 1 running 2 paused 3 restarting 4 removing 5 exited 6 dead 7.'),
    'container_health':('gauge','Docker health: not configured 0 healthy 1 unhealthy 2 starting 3 unknown 4.'),
    'container_created_timestamp_seconds':('gauge','Container creation time in Unix seconds.'),
    'container_started_timestamp_seconds':('gauge','Last start time in Unix seconds, if configured.'),
    'container_finished_timestamp_seconds':('gauge','Last finish time in Unix seconds, if configured.'),
    'container_restarts':('gauge','Docker restart count for this incarnation.'),
    'container_oom_killed':('gauge','Docker OOM killed flag for this incarnation; boolean.'),
    'container_exit_code':('gauge','Last exit code for an exited or dead container.'),
    'container_cpu_limit_cores':('gauge','Configured CPU quota in cores; zero means unlimited.'),
    'container_memory_limit_bytes':('gauge','Configured memory limit in bytes; zero means unlimited.'),
    'container_cpu_usage_seconds_total':('counter','Total consumed CPU seconds.'),
    'container_memory_usage_bytes':('gauge','Docker memory usage in bytes.'),
    'container_memory_working_set_bytes':('gauge','Memory usage minus inactive file cache, in bytes.'),
    'container_network_attributable':('gauge','Network counters attributable to this container namespace; boolean.'),
    'container_network_receive_bytes_total':('counter','Received bytes summed across attributable interfaces.'),
    'container_network_transmit_bytes_total':('counter','Transmitted bytes summed across attributable interfaces.'),
    'container_block_read_bytes_total':('counter','Block bytes read, summed across devices.'),
    'container_block_write_bytes_total':('counter','Block bytes written, summed across devices.'),
    'container_readiness':('gauge','Fixed local application readiness: not configured 0 ready 1 not ready 2 unknown 3.'),
    'collection_success':('gauge','All discovered containers collected successfully; boolean.'),
    'inventory_success':('gauge','Current Docker enumeration succeeded; boolean.'),
    'snapshot_timestamp_seconds':('gauge','Latest collection attempt in Unix seconds.'),
    'last_success_timestamp_seconds':('gauge','Last fully successful collection in Unix seconds; zero before first success.'),
    'collection_duration_seconds':('gauge','Collection elapsed seconds.'),
}
HOST_METRICS={'collection_success','inventory_success','snapshot_timestamp_seconds','last_success_timestamp_seconds','collection_duration_seconds'}


def unique(pairs):
    result={}
    for key,value in pairs:
        if key in result:raise ValueError('Duplicate JSON key')
        result[key]=value
    return result


def document(raw,limit=MAX_BYTES):
    if len(raw)>limit:raise ValueError('Oversized response')
    return json.loads(raw,object_pairs_hook=unique,parse_constant=lambda _:(_ for _ in ()).throw(ValueError('Non-finite JSON')))


def label(value,limit=128):
    if not isinstance(value,str) or len(value)>limit or any(ord(c)<32 and c!='\n' for c in value):
        raise ValueError('Invalid metadata label')
    return value


def number(value):
    if type(value) not in (int,float) or not math.isfinite(value) or value<0:raise ValueError('Invalid numeric sample')
    return value


class Metrics:
    def __init__(self):self.lines=[]
    def emit(self,name,labels,value):
        if name not in METRICS:raise ValueError('Unknown metric')
        value=number(value)
        def escape(text):return str(text).replace('\\','\\\\').replace('\n','\\n').replace('"','\\"')
        formatted=','.join(key+'="'+escape(label(val,512))+'"' for key,val in sorted(labels.items()))
        self.lines.append(f'{PREFIX}{name}{{{formatted}}} {value}\n')
    def render(self):
        headers=''.join(f'# HELP {PREFIX}{key} {help_text}\n# TYPE {PREFIX}{key} {kind}\n' for key,(kind,help_text) in METRICS.items())
        raw=(headers+''.join(self.lines)).encode()
        if len(raw)>MAX_BYTES:raise ValueError('Oversized metric snapshot')
        return raw


class UnixConnection(http.client.HTTPConnection):
    def __init__(self,path,timeout):super().__init__('localhost',timeout=timeout);self.path=path
    def connect(self):
        self.sock=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout);self.sock.connect(self.path)


class Docker:
    def __init__(self,path='/var/run/docker.sock',deadline=None):
        self.path=path;self.deadline=deadline or time.monotonic()+20
    def get(self,path):
        if not re.fullmatch(r'/containers/json\?all=1|/containers/[a-f0-9]{64}/(?:json|stats\?stream=false&one-shot=true)|/images/sha256:[a-f0-9]{64}/json',path):
            raise ValueError('Non-allowlisted Docker GET')
        remaining=self.deadline-time.monotonic()
        if remaining<=0:raise TimeoutError('Collection deadline')
        connection=UnixConnection(self.path,min(2,remaining))
        try:
            connection.request('GET','/v1.47'+path)
            response=connection.getresponse()
            if response.status!=200:raise ValueError('Docker GET failed')
            if int(response.getheader('Content-Length','0'))>MAX_BYTES:raise ValueError('Oversized response')
            raw=bytearray()
            while len(raw)<=MAX_BYTES:
                remaining=self.deadline-time.monotonic()
                if remaining<=0:raise TimeoutError('Collection deadline')
                if connection.sock:connection.sock.settimeout(min(2,remaining))
                chunk=response.read1(min(65536,MAX_BYTES+1-len(raw)))
                if not chunk:break
                raw.extend(chunk)
            return document(raw)
        finally:connection.close()


def image_metadata(raw,image_id,now):
    if raw.get('Id')!=image_id:raise ValueError('Image identity differs')
    digests=raw.get('RepoDigests') or []
    if not isinstance(digests,list) or len(digests)>128:raise ValueError('Invalid image digest set')
    digests=[label(value,256) for value in digests]
    if any(not re.fullmatch(r'[^\s@]+@sha256:[a-f0-9]{64}',value) for value in digests):raise ValueError('Invalid registry digest')
    revision=(raw.get('Config',{}).get('Labels') or {}).get('org.opencontainers.image.revision','')
    revision=revision if isinstance(revision,str) and re.fullmatch(r'[a-f0-9]{40}',revision) else 'Not provided'
    return {'digests':sorted(set(digests)),'revision':revision,'sampled_unix':now}


def registry_digest(reference,metadata):
    if '@' in reference:
        return reference if reference in metadata['digests'] else 'Unavailable'
    repository=reference.rsplit(':',1)[0] if ':' in reference.rsplit('/',1)[-1] else reference
    # Docker familiar references may omit docker.io/library. Normalize only
    # these known default aliases; never match a different registry/repository.
    def normalize(repo):
        parts=repo.split('/')
        if len(parts)==1:return 'docker.io/library/'+repo
        if '.' not in parts[0] and ':' not in parts[0] and parts[0]!='localhost':return 'docker.io/'+repo
        return repo
    matching=[value for value in metadata['digests'] if normalize(value.split('@')[0])==normalize(repository)]
    return matching[0] if matching else 'Unavailable'


def info(summary,inspect,metadata):
    cid=summary['Id']
    if not isinstance(cid,str) or not ID.fullmatch(cid):raise ValueError('Invalid container identity')
    config=inspect['Config'];host_config=inspect['HostConfig']
    if inspect['Id']!=cid or not IMAGE_ID.fullmatch(inspect['Image']):raise ValueError('Invalid inspected identity')
    name=label(inspect['Name'].removeprefix('/'))
    labels=config.get('Labels') or {}
    mode=host_config['NetworkMode']
    network=mode if mode in ('host','bridge','none') else 'container' if mode.startswith('container:') else 'other'
    reference=label(config['Image'],512)
    return {'container':name,'compose_project':label(labels.get('com.docker.compose.project','')),
        'compose_service':label(labels.get('com.docker.compose.service','')),'configured_image':reference,
        'image_id':inspect['Image'],'registry_digest':registry_digest(reference,metadata),
        'source_revision':metadata['revision'],'network_mode':network}


def timestamp(value):
    if not isinstance(value,str):raise ValueError('Invalid timestamp')
    if value.startswith('0001-'):return None
    parsed=datetime.datetime.fromisoformat(value.replace('Z','+00:00'))
    if parsed.tzinfo is None:raise ValueError('Unzoned Docker timestamp')
    return number(parsed.timestamp())


def container_metrics(metrics,base,inspect,metadata,summary,stats):
    mapping=info(summary,inspect,metadata)
    metrics.emit('container_info',{**base,**mapping},1)
    state=inspect['State'];host_config=inspect['HostConfig']
    metrics.emit('container_state',base,STATES.get(state['Status'],0))
    health=state.get('Health',{}).get('Status','none')
    metrics.emit('container_health',base,HEALTH.get(health,4))
    for key,suffix in [('Created','created'),('StartedAt','started'),('FinishedAt','finished')]:
        value=timestamp(inspect['Created'] if key=='Created' else state[key])
        if value is not None:metrics.emit('container_'+suffix+'_timestamp_seconds',base,value)
    metrics.emit('container_restarts',base,number(inspect['RestartCount']))
    if type(state['OOMKilled']) is not bool:raise ValueError('Invalid OOM flag')
    metrics.emit('container_oom_killed',base,int(state['OOMKilled']))
    if state['Status'] in ('exited','dead'):metrics.emit('container_exit_code',base,number(state['ExitCode']))
    nano=number(host_config.get('NanoCpus',0));quota=host_config.get('CpuQuota',0);period=host_config.get('CpuPeriod',0)
    cores=nano/1e9 if nano else number(quota)/number(period) if quota>0 and period>0 else 0
    metrics.emit('container_cpu_limit_cores',base,cores)
    metrics.emit('container_memory_limit_bytes',base,number(host_config['Memory']))
    attributable=mapping['network_mode'] not in ('host','container')
    metrics.emit('container_network_attributable',base,int(attributable))
    if stats is None:return mapping
    metrics.emit('container_cpu_usage_seconds_total',base,number(stats['cpu_stats']['cpu_usage']['total_usage'])/1e9)
    memory=stats['memory_stats'];usage=number(memory['usage'])
    metrics.emit('container_memory_usage_bytes',base,usage)
    memory_cache=memory.get('stats',{});inactive=memory_cache.get('inactive_file',memory_cache.get('total_inactive_file'))
    if inactive is not None:metrics.emit('container_memory_working_set_bytes',base,max(0,usage-number(inactive)))
    if attributable and stats.get('networks') is not None:
        for direction,field in [('receive','rx_bytes'),('transmit','tx_bytes')]:
            metrics.emit('container_network_'+direction+'_bytes_total',base,sum(number(value[field]) for value in stats['networks'].values()))
    entries=stats.get('blkio_stats',{}).get('io_service_bytes_recursive')
    if entries is not None:
        for operation in ('read','write'):
            metrics.emit('container_block_'+operation+'_bytes_total',base,sum(number(value['value']) for value in entries if value['op'].lower()==operation))
    return mapping


def readiness(container):
    # Fixed host-owned allowlist. No redirects, proxies, label-selected URL or
    # container command. Docker health and application readiness stay separate.
    if container!='pokerattack-production-api-1':return 0
    connection=http.client.HTTPConnection('127.0.0.1',8081,timeout=1)
    try:
        connection.request('GET','/health/ready')
        response=connection.getresponse()
        return 1 if response.status==200 else 2
    except (OSError,http.client.HTTPException):return 3
    finally:connection.close()


def collect(docker,host,cache=None,now=None,probe=readiness):
    now=time.time() if now is None else now
    cache=cache or {};metrics=Metrics();success=True;used=set()
    containers=docker.get('/containers/json?all=1')
    if not isinstance(containers,list) or len(containers)>MAX_CONTAINERS:raise ValueError('Invalid inventory')
    ids=[item['Id'] for item in containers]
    if any(not isinstance(cid,str) or not ID.fullmatch(cid) for cid in ids) or len(set(ids))!=len(ids):raise ValueError('Invalid inventory identities')
    for summary in containers:
        cid=summary['Id'];base={'host':label(host),'container_id':cid};candidate=Metrics()
        try:
            inspect=docker.get('/containers/'+cid+'/json');image_id=inspect['Image'];used.add(image_id)
            if not IMAGE_ID.fullmatch(image_id):raise ValueError('Invalid image ID')
            metadata=cache.get(image_id)
            if not metadata or not 0<=now-metadata['sampled_unix']<300:
                metadata=image_metadata(docker.get('/images/'+image_id+'/json'),image_id,now)
                cache[image_id]=metadata
            stats=docker.get('/containers/'+cid+'/stats?stream=false&one-shot=true') if inspect['State']['Status']=='running' else None
            mapping=container_metrics(candidate,base,inspect,metadata,summary,stats)
            candidate.emit('container_readiness',base,probe(mapping['container']))
            candidate.emit('container_identity_timestamp_seconds',{**base,**mapping},now)
            candidate.emit('container_collection_success',base,1)
        except Exception:
            success=False;candidate=Metrics()
            # Known inventory identity only; never carry a stale healthy state.
            names=summary.get('Names') or [cid]
            mapping={'container':label(names[0].removeprefix('/')),
                'compose_project':'','compose_service':'','configured_image':'Unavailable','image_id':'Unavailable',
                'registry_digest':'Unavailable','source_revision':'Not provided','network_mode':'unknown'}
            candidate.emit('container_info',{**base,**mapping},1)
            candidate.emit('container_identity_timestamp_seconds',{**base,**mapping},now)
            candidate.emit('container_collection_success',base,0)
        candidate.emit('container_sample_timestamp_seconds',base,now)
        metrics.lines.extend(candidate.lines)
    return metrics,success,{key:value for key,value in cache.items() if key in used}


def atomic(path,raw,mode=0o644):
    if path.is_symlink() or len(raw)>MAX_BYTES:raise ValueError('Unsafe output')
    descriptor,temporary=tempfile.mkstemp(prefix='.docker-snapshot-',dir=path.parent)
    try:
        with os.fdopen(descriptor,'wb') as stream:
            os.fchmod(stream.fileno(),mode);stream.write(raw);stream.flush();os.fsync(stream.fileno())
        os.replace(temporary,path)
        descriptor=os.open(path.parent,os.O_RDONLY|os.O_DIRECTORY)
        try:os.fsync(descriptor)
        finally:os.close(descriptor)
    finally:
        if os.path.exists(temporary):os.unlink(temporary)


def previous(path,host):
    if not path.exists():return [],0
    if path.is_symlink() or path.stat().st_size>MAX_BYTES:raise ValueError('Unsafe prior snapshot')
    lines=path.read_text(encoding='utf-8').splitlines(keepends=True);body=[];last=0
    for line in lines:
        if line.startswith(PREFIX+'last_success_timestamp_seconds{'):
            last=number(float(line.rsplit(' ',1)[1]))
        if line.startswith(PREFIX+'container_'):body.append(line)
    return body,last


def snapshot(docker,directory,host,now=None,clock=time.monotonic):
    start=clock();now=time.time() if now is None else now
    path=directory/'docker.prom';prior,last=previous(path,host)
    cache_path=directory/'image-cache.json';cache={}
    try:
        if not cache_path.is_symlink() and cache_path.is_file():
            if cache_path.stat().st_size>1024*1024:raise ValueError('Oversized image cache')
            cache=document(cache_path.read_bytes(),1024*1024)
            if not isinstance(cache,dict) or len(cache)>MAX_CONTAINERS:raise ValueError('Invalid image cache')
            for image_id,entry in cache.items():
                if not IMAGE_ID.fullmatch(image_id) or set(entry)!={'digests','revision','sampled_unix'}:raise ValueError('Invalid image cache')
                # Re-validate sanitized persisted metadata before using it.
                validated=image_metadata({'Id':image_id,'RepoDigests':entry['digests'],
                    'Config':{'Labels':{'org.opencontainers.image.revision':entry['revision']}}},image_id,number(entry['sampled_unix']))
                if validated!=entry:raise ValueError('Invalid image cache')
    except Exception:cache={}
    try:
        metrics,success,cache=collect(docker,host,cache,now)
        inventory=True
        if success:last=now
    except Exception:
        metrics=Metrics();metrics.lines=prior;inventory=success=False
    for name,value in [('collection_success',int(success)),('inventory_success',int(inventory)),
                       ('snapshot_timestamp_seconds',now),('last_success_timestamp_seconds',last),
                       ('collection_duration_seconds',clock()-start)]:metrics.emit(name,{'host':host},value)
    atomic(path,metrics.render())
    # Cache is only an optimization; its persistence cannot turn a complete
    # truthful snapshot into a failed collection or corrupt its success time.
    try:atomic(cache_path,json.dumps(cache,separators=(',',':')).encode(),0o600)
    except OSError:pass
    return success


def main():
    import sys
    import signal
    import fcntl
    if os.geteuid()!=0 or len(sys.argv)!=1:raise ValueError('Fixed root service only')
    # Root-owned output directory; Alloy has read access only. No environment or
    # command line can redirect socket, output, URLs or host identity.
    for parent in (DIRECTORY,*DIRECTORY.parents):
        st=parent.lstat()
        if parent.is_symlink() or st.st_uid!=0 or st.st_mode&0o022:raise ValueError('Unsafe output directory')
    def deadline(*_):raise TimeoutError('Hard collection deadline')
    descriptor=os.open(DIRECTORY/'collector.lock',os.O_CREAT|os.O_RDWR|os.O_NOFOLLOW,0o600)
    try:
        fcntl.flock(descriptor,fcntl.LOCK_EX|fcntl.LOCK_NB)
        signal.signal(signal.SIGALRM,deadline)
        signal.setitimer(signal.ITIMER_REAL,22)
        try:return 0 if snapshot(Docker(),DIRECTORY,HOST) else 1
        finally:signal.setitimer(signal.ITIMER_REAL,0)
    finally:os.close(descriptor)


if __name__=='__main__':
    try:raise SystemExit(main())
    except Exception:raise SystemExit('Docker snapshot failed; no container state changed.')
