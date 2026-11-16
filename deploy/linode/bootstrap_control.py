"""Root-only nested publication during the one-time legacy introduction.

No forced-command key or sudo capability exposes these operations. A durable
server lease owns the entire transition; publication never acquires a client
lease or restarts an API. Unknown uploads retain their exact intent for explicit
read-only reconciliation, not an automatic repeat.
"""
import hashlib
import time
import urllib.request

import host_manifests
import secure_state as storage
import release_lease
from release_verifier import matching, status_module

ORIGINS=('https://playpokerattack.com','https://www.playpokerattack.com',
         'https://salmon-hill-0056eb60f.2.azurestaticapps.net')


def owner(run,attempt):
    base,lease=matching(run,attempt)
    if lease.get('bootstrap') is not True:raise ValueError('Not a transitional root bootstrap')
    live=status_module.status('production')
    if not live['consistent'] or live['transaction'] or not live['readiness']['ready']:
        raise ValueError('Inspect unresolved bootstrap runtime')
    admission=storage.read(base/'admission/state.json',0o644)
    if admission.get('active') is not True or any(admission.get(key)!=lease.get(key) for key in ('run_id','run_attempt','digest','config_sha256','source_sha')):
        raise ValueError('Bootstrap maintenance context differs')
    return base,lease,live


def publication(packet):
    host_manifests.fields(packet,('mode','archive_sha256','probes','client'))
    mode=packet['mode']
    paths={'maintenance':{'/index.html'},'candidate':{'/index.html','/release.json','/appsettings.json'},
           'legacy-recovery':{'/index.html','/appsettings.json'}}
    if mode not in paths or not isinstance(packet['probes'],dict) or set(packet['probes'])!=paths[mode]:
        raise ValueError('Invalid fixed publication probes')
    for digest in (packet['archive_sha256'],*packet['probes'].values()):host_manifests.pattern(digest,r'[a-f0-9]{64}')
    if mode=='candidate':
        client=host_manifests.client_identity(packet['client'])
        if client['archive_sha256']!=packet['archive_sha256']:raise ValueError('Client publication differs from exact archive')
        if client['minimum_server_capability']>1:raise ValueError('Bootstrap client requires an unavailable server capability')
    elif packet['client'] is not None:raise ValueError('Legacy/static publication has no invented Actions identity')
    return packet


def intent(run,attempt,packet):
    packet=publication(packet)
    digest=hashlib.sha256(release_lease.canonical(packet)).hexdigest()
    with storage.control_lock(),storage.deployment_lock():
        base,lease,live=owner(run,attempt)
        mode=packet['mode']
        phases={'maintenance':('bootstrap-maintenance-pending',),'candidate':('pending',),
                'legacy-recovery':('bootstrap-legacy-recovered',)}
        if lease['phase'] not in phases[mode]:raise ValueError('Publication is out of bootstrap order')
        if mode=='candidate' and (live['pairs']['active']!=live['pairs']['pending'] or live['pairs']['active']['digest']!=lease['digest']
                or live['pairs']['active']['config_sha256']!=lease['config_sha256'] or live['runtime']['source_revision']!=lease['source_sha']):
            raise ValueError('Candidate runtime differs')
        if mode=='maintenance':release_lease.fresh(lease['acknowledgment'])
        if mode=='legacy-recovery' and packet!=lease.get('bootstrap_recovery',{}).get('client'):
            raise ValueError('Restore only the frozen compatible legacy client')
        prior=lease.get('bootstrap_publication_intent')
        if prior:
            if prior['sha256']!=digest:raise ValueError('Unresolved publication intent; reconcile before another upload')
            return {'phase':'bootstrap-publication-authorized','sha256':digest,'idempotent':True,'upload_outcome':'unknown; inspect served bytes first'}
        if mode in lease.get('bootstrap_publications',{}):raise ValueError('Publication already recorded; never upload it again')
        lease['bootstrap_publication_intent']={'sha256':digest,'packet':packet,'created_unix':int(time.time())}
        storage.write(base/'lease',lease)
        return {'phase':'bootstrap-publication-authorized','sha256':digest,'upload_outcome':'not-started'}


def probe(packet):
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self,*args):return None
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),NoRedirect())
    for origin in ORIGINS:
        for path,digest in packet['probes'].items():
            request=urllib.request.Request(origin+path,headers={'Cache-Control':'no-cache','Accept-Encoding':'identity'})
            with opener.open(request,timeout=5) as response:
                body=response.read(262145)
                if response.status!=200 or len(body)>262144 or hashlib.sha256(body).hexdigest()!=digest:
                    raise ValueError('Served bootstrap artifact differs')
                if packet['mode']=='maintenance':
                    if 'no-store' not in response.headers.get('Cache-Control','').lower():raise ValueError('Static maintenance must not be cached')
                elif response.headers.get('Blazor-Environment')!='prod':raise ValueError('Client environment differs')


def published(run,attempt,expected_sha256=None):
    if expected_sha256 is not None:host_manifests.pattern(expected_sha256,r'[a-f0-9]{64}')
    with storage.control_lock(),storage.deployment_lock():
        base,lease,_=owner(run,attempt)
        pending=lease.get('bootstrap_publication_intent')
        if not pending:
            matches=[record for record in lease.get('bootstrap_publications',{}).values() if record['sha256']==expected_sha256]
            if expected_sha256 is None or len(matches)!=1:raise ValueError('No matching recorded bootstrap publication')
            pending=matches[0]
            packet=publication(pending['packet'])
            probe(packet)
            return {'phase':'bootstrap-publication-verified','mode':packet['mode'],'sha256':pending['sha256'],'response_reconciled':True}
        if expected_sha256 is not None and pending['sha256']!=expected_sha256:raise ValueError('Publication intent differs from the caller artifact')
        packet=publication(pending['packet'])
    # The durable server slot, rather than a long-held flock, serializes upload
    # and probes. Recheck that exact intent under both locks before recording.
    probe(packet)
    with storage.control_lock(),storage.deployment_lock():
        base,lease,_=owner(run,attempt)
        if lease.get('bootstrap_publication_intent')!=pending:raise ValueError('Publication intent changed')
        lease.setdefault('bootstrap_publications',{})[packet['mode']]={**pending,'verified_unix':int(time.time())}
        del lease['bootstrap_publication_intent'];storage.write(base/'lease',lease)
        return {'phase':'bootstrap-publication-verified','mode':packet['mode'],'sha256':pending['sha256']}


def drain(run,attempt):
    with storage.control_lock(),storage.deployment_lock():
        base,lease,live=owner(run,attempt)
        maintenance=lease.get('bootstrap_publications',{}).get('maintenance')
        if (lease['phase']!='bootstrap-maintenance-pending' or not maintenance or lease.get('bootstrap_publication_intent')
                or not 0<=time.time()-maintenance['verified_unix']<1800 or live['pairs']['active']!=live['pairs']['known-good']):
            raise ValueError('Verify static maintenance before draining the legacy server')
        release_lease.fresh(lease['acknowledgment'])
        # Recheck actual served maintenance now, not merely a prior root result.
        probe(maintenance['packet'])
        lease['phase']='draining';storage.write(base/'lease',lease)
    return release_lease.drain(run,attempt,lease['acknowledgment'])


def client_status(run,attempt,acceptance=False):
    with storage.control_lock(),storage.deployment_lock():
        base,lease,live=owner(run,attempt)
        published=lease.get('bootstrap_publications',{}).get('candidate')
        if not published or lease.get('bootstrap_publication_intent') or lease['phase'] not in ('pending','promoted','promoted-needs-verification'):
            raise ValueError('No established bootstrap client/runtime')
        if (not live['pairs']['active'] or live['pairs']['active']['digest']!=lease['digest'] or
                live['pairs']['active']['config_sha256']!=lease['config_sha256'] or live['runtime']['source_revision']!=lease['source_sha']):
            raise ValueError('Bootstrap publication no longer matches the runtime')
        client=host_manifests.client_identity(published['packet']['client'])
        if acceptance:
            from release_evidence import saved
            proof,_=saved(base,lease,lease.get('evidence_sha256'),('pending','promoted','promoted-needs-verification'))
            if lease.get('verifier_revoked') is not True or any(release_lease.counts().values()):
                raise ValueError('Finish owned verification before accepting the bootstrap client')
            if proof['client_source_sha']!=client['source_sha'] or proof['client_archive_sha256']!=client['archive_sha256']:
                raise ValueError('Actual verification is for another bootstrap client')
        return {'format':'pokerattack-bootstrap-client-status-v1','phase':lease['phase'],
                'publication_sha256':published['sha256'],'client':client,'pair':live['pairs']['active'],
                'source_sha':lease['source_sha'],'verified_for_acceptance':acceptance,
                'evidence_sha256':lease.get('evidence_sha256') if acceptance else None}
