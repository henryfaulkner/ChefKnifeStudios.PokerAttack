"""H05 exact legacy restore, then full capability introduction on owned VM only."""
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import urllib.error
import urllib.request

from initialize_replica import machine_guard,PRIVATE,ROOT,run,write
from run_release import transport
from run_faults import context,packet
sys.path.insert(0,'/opt/pokerattack')
import secure_state as storage
import bootstrap_control
import bootstrap_recovery
import release_lease
sys.path.insert(0,str(ROOT/'deploy/release'))
from bootstrap_artifacts import check as check_bootstrap,publication
from bootstrap_archive import ProvisionalClient
from blob_store import LocalStore
from client_archive import Archive
from client_artifacts import check_archive,identity_summary
from review import canonical
import server_verify,server_promote,server_finish

BASE=Path('/var/lib/pokerattack/production')
RUN=42424242424242


def operator(action,attempt,body=None):
    command=['/usr/bin/python3','-I','/opt/pokerattack/release-control.py','operator-bootstrap',action,'production',str(RUN),str(attempt)]
    result=subprocess.run(command,input=None if body is None else canonical(body),capture_output=True,timeout=390)
    if result.returncode:raise ValueError('Root bootstrap operation rejected: '+action)
    return json.loads(result.stdout)


def point(name,target):
    if target.resolve().parent!=(BASE/'releases').resolve():raise ValueError('Unexpected fixture snapshot')
    pending=BASE/(name+'.h05-next')
    if pending.exists() or pending.is_symlink():raise ValueError('Inspect existing fixture pointer transition')
    pending.symlink_to(target);os.replace(pending,BASE/name)


def publish(source):
    destination=PRIVATE/'served-client'
    if destination.is_symlink() or destination.resolve().parent!=PRIVATE:raise ValueError('Unexpected owned publication root')
    desired={path.relative_to(source).as_posix() for path in source.rglob('*') if path.is_file()}
    from run_client_recovery import publish_files
    publish_files(source)
    # SWA replaces its deployment's file set. Preserve the bind-mounted root,
    # but remove only obsolete regular files in this owned fixture directory.
    for path in destination.rglob('*'):
        if path.is_symlink():raise ValueError('Unsafe fixture publication member')
        if path.is_file() and path.relative_to(destination).as_posix() not in desired:
            if not path.resolve().is_relative_to(destination.resolve()):raise ValueError('Publication cleanup escaped root')
            path.unlink()


def capability_missing():
    try:
        with urllib.request.urlopen('http://127.0.0.1:8081/release/status',timeout=5):pass
    except urllib.error.HTTPError as error:
        if error.code==404:return True
    raise ValueError('Legacy fixture unexpectedly has modern admission')


def approve(manifest,attempt,action):
    env=context(manifest,attempt);review=packet(manifest,env,transport('status'))
    ack={'action':action,'digest':manifest['api']['digest'],'config_sha256':manifest['config_sha256'],
         'source_sha':manifest['source_sha'],'run_id':RUN,'run_attempt':attempt,
         'approved_unix':review['review_window_started_unix'],'expires_unix':review['review_window_expires_unix'],
         'drain_seconds':3,'policy':'reset-at-deadline'}
    return review,ack,env


def begin(manifest,attempt,maintenance_manifest,maintenance):
    review,ack,env=approve(manifest,attempt,'bootstrap')
    if operator('begin-release',attempt,{'manifest':review['manifest'],'acknowledgment':ack})['phase']!='bootstrap-maintenance-pending':
        raise ValueError('Bootstrap drained before static maintenance')
    before=transport('status').call('status production')
    request=publication(maintenance_manifest)
    operator('client-authorize',attempt,request);publish(maintenance);operator('client-published',attempt)
    if transport('status').call('status production')['runtime']!=before['runtime']:raise ValueError('Static publication restarted API')
    if operator('drain-release',attempt)['phase'] not in ('drained','reset-approved'):raise ValueError('Bootstrap drain failed')
    manifest=review['manifest']
    transport('deployment').call(f"deploy production {manifest['api']['digest']} {manifest['config_sha256']} {RUN} {attempt}",timeout=1040)
    return manifest,env


def main():
    machine_guard();status=transport('status');report={'task':'H05','fixture_only':True,'production_mutation':False,
        'GitHub_reviews_and_SWA_file_service_simulated':True,'phase':'starting','cases':[]}
    if sys.argv[1:] not in ([],['--resume-pre-seed']):raise ValueError('Explicit inspected fixture continuation only')
    resume=bool(sys.argv[1:]);output=PRIVATE/('H05-bootstrap-attempt2.json' if resume else 'H05-bootstrap.json')
    try:
        initial=status.call('status production')
        if initial['lease'] or initial['maintenance']['active'] or initial['transaction']:raise ValueError('Inspect occupied fixture before bootstrap')
        modern_manifest=json.loads((BASE/'known-good/release-manifest.json').read_bytes())
        canaries={name:json.loads(run('docker','inspect',name))[0]['Id'] for name in ('h02-caddy','h02-registry')}
        postgres=run('systemctl','show','postgresql@16-main','-p','MainPID','--value')
        maintenance_manifest=json.loads((PRIVATE/'downloads/H05-maintenance.json').read_bytes())
        legacy_manifest=json.loads((PRIVATE/'downloads/H05-legacy-recovery.json').read_bytes())
        maintenance,legacy_client=PRIVATE/'H05-maintenance-client',PRIVATE/'H05-legacy-client'
        if resume:
            previous=json.loads((PRIVATE/'H05-bootstrap.json').read_bytes())
            if previous['phase']!='failed-or-incomplete' or previous.get('legacy_seed') or previous['cases']:
                raise ValueError('Not the inspected pre-seed failure')
            for directory,manifest in ((maintenance,maintenance_manifest),(legacy_client,legacy_manifest)):
                observed={path.relative_to(directory).as_posix():hashlib.sha256(path.read_bytes()).hexdigest()
                          for path in directory.rglob('*') if path.is_file() and not path.is_symlink()}
                if observed!=manifest['files'] or any(path.is_symlink() for path in directory.rglob('*')):
                    raise ValueError('Previously checked artifact bytes changed')
            report['original_fixture_failure_retained']='Gateway containers were stopped by the scheduled VM shutdown; no API replacement occurred. Exact owned gateways resumed separately.'
        else:
            check_bootstrap(PRIVATE/'downloads/H05-maintenance.tar.gz',maintenance_manifest,maintenance)
            check_bootstrap(PRIVATE/'downloads/H05-legacy-recovery.tar.gz',legacy_manifest,legacy_client)
        sources={name:PRIVATE/'legacy-tools'/name for name in bootstrap_recovery.SOURCES}
        tooling=bootstrap_recovery.preserve(sources);old_tools=bootstrap_recovery.validate_tools(tooling)
        inputs=json.loads((PRIVATE/'inputs.json').read_bytes());old=inputs['images'][2]
        raw=json.loads(run('docker','image','inspect',old['reference']))[0]
        if raw['Id']!=old['image_id'] or raw['Config']['Labels']['org.opencontainers.image.revision']!=old['source_revision']:
            raise ValueError('Loaded exact legacy image changed')
        tag='chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi:h05-legacy-'+old['source_revision']
        run('docker','tag',raw['Id'],tag);run('docker','push',tag)
        image=json.loads(run('docker','image','inspect',tag))[0]
        repo='chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi'
        matches=[value for value in image['RepoDigests'] if value.startswith(repo+'@sha256:')]
        if len(set(matches))!=1:raise ValueError('Ambiguous private legacy manifest')
        digest=matches[0].split('@')[1]
        # This deliberately initializes a disposable starting state. It is NOT
        # a production promotion or a substitute for the actual bootstrap below.
        protected=(BASE/'known-good/app.env').read_bytes()+b'# H05 exact capability-0 fixture baseline\n'
        config=hashlib.sha256(protected).hexdigest();config_path=Path('/etc/pokerattack/production/configs')/(config+'.env')
        if config_path.exists():raise ValueError('Legacy seed already exists; inspect rather than repeat')
        write(config_path,protected)
        write(Path('/etc/pokerattack/production/maintenance'),'sessions-reset-approved\n')
        run(str(old_tools/'pokerattack-deploy'),'deploy','production',digest,config)
        seeded=status.call('status production')
        if not seeded['readiness']['ready'] or seeded['runtime']['image_id']!=raw['Id']:raise ValueError('Legacy baseline not ready')
        with storage.control_lock(),storage.deployment_lock():
            target=(BASE/'active').resolve(strict=True)
            if (BASE/'pending').resolve()!=target:raise ValueError('Legacy seed pending identity differs')
            point('known-good',target);(BASE/'pending').unlink()
            if (BASE/'previous-good').is_symlink():(BASE/'previous-good').unlink()  # Fixture starts like actual first production pair.
            storage.write(BASE/'admission/state.json',{'active':False},0o644)
        publish(legacy_client);capability_missing()
        legacy=status.call('status production')
        recovery={'format':'pokerattack-bootstrap-recovery-v1','pair':{'digest':digest,'config_sha256':config},
            'source_sha':old['source_revision'],'image_id':raw['Id'],'client':publication(legacy_manifest),'tooling':tooling}
        storage.write(bootstrap_recovery.CONFIG,recovery);bootstrap_recovery.snapshot(legacy)
        report['legacy_seed']={'explicit_fixture_initialization':True,'source_sha':old['source_revision'],'image_id':raw['Id'],
            'pair':recovery['pair'],'modern_capability_absent':True,'saved_tooling':tooling,'no_admission_mount':True}
        report['phase']='legacy-baseline-ready';output.write_bytes(canonical(report))
        # First real introduction is intentionally abandoned pending, exercising
        # exact old tooling/Compose and compatible static artifact restoration.
        first,env=begin(modern_manifest,22,maintenance_manifest,maintenance)
        report['cases'].append({'name':'root-reserve-static-maintenance-before-drain-and-digest-delivery','passed':True})
        before_restore=status.call('status production')
        fresh_ack={'action':'bootstrap-recovery',**recovery['pair'],'source_sha':old['source_revision'],
            'run_id':RUN,'run_attempt':23,'approved_unix':int(time.time()),'expires_unix':int(time.time())+1800,
            'drain_seconds':0,'policy':'reset-at-deadline'}
        release_lease.fresh(fresh_ack)
        # Root console inspection is the deliberately preserved fallback. It
        # verifies both original snapshot and versioned old tools under locks,
        # then releases locks before invoking the old tool's own flock.
        with storage.control_lock(),storage.deployment_lock():
            prior=storage.read(BASE/'lease')
            if prior['run_attempt']!=22 or prior['phase']!='pending' or not prior['bootstrap']:
                raise ValueError('Unexpected candidate before root legacy recovery')
            bootstrap_recovery.validate_tools(prior['bootstrap_recovery']['tooling'])
            good=(BASE/'known-good').resolve(strict=True)
            if hashlib.sha256((good/'app.env').read_bytes()).hexdigest()!=config or (good/'digest').read_text().strip()!=digest:
                raise ValueError('Recorded old recovery snapshot changed')
            storage.write(BASE/'interrupted/H05-bootstrap22-abandoned-for-legacy23.json',prior)
        write(Path('/etc/pokerattack/production/maintenance'),'sessions-reset-approved\n')
        run(str(old_tools/'pokerattack-deploy'),'rollback','production')
        restored=status.call('status production')
        if restored['runtime']['image_id']!=raw['Id'] or restored['pairs']['active']!=restored['pairs']['known-good']:
            raise ValueError('Saved tooling did not restore exact legacy pair')
        with storage.control_lock(),storage.deployment_lock():
            lease=storage.read(BASE/'lease')
            lease.update(run_attempt=23,phase='bootstrap-legacy-recovered',source_sha=old['source_revision'],
                         digest=digest,config_sha256=config,acknowledgment=fresh_ack)
            storage.write(BASE/'lease',lease)
            storage.write(BASE/'admission/state.json',{'active':True,'run_id':RUN,'run_attempt':23,
                          'source_sha':old['source_revision'],**recovery['pair']},0o644)
        operator('client-authorize',23,recovery['client']);publish(legacy_client);operator('client-published',23)
        capability_missing()
        private=PRIVATE/'runner-temp/H05-legacy';private.mkdir(mode=0o700)
        ctx,result=private/'context.json',private/'result.json'
        write(ctx,canonical({'format':'pokerattack-legacy-bootstrap-context-v1','source_sha':old['source_revision'],
                            **recovery['pair'],'client':legacy_manifest}))
        legacy_env={**context(first,23),'POKERATTACK_RELEASE_MODE':'production','POKERATTACK_LEGACY_CONTEXT':str(ctx),
                    'POKERATTACK_LEGACY_RESULT':str(result)}
        executed=subprocess.run(['node',str(ROOT/'tests/release/legacy-bootstrap.cjs')],capture_output=True,timeout=800,env=legacy_env)
        write(PRIVATE/'logs/H05-legacy-browser-private.log',executed.stdout+executed.stderr)
        if executed.returncode:raise ValueError('Actual legacy browser/gameplay failed')
        proof=json.loads(result.read_bytes())
        if any(proof.get(name) is not True for name in ('https','signalr','multiplayer','solo','private_message_isolation')):
            raise ValueError('Legacy gameplay proof incomplete')
        raw_proof=result.read_bytes();proof_hash=hashlib.sha256(raw_proof).hexdigest()
        write(Path('/etc/pokerattack/production/verification')/(proof_hash+'.json'),raw_proof)
        # Separate explicitly simulated recovery promotion decision matches this
        # actual fresh proof; never hand-author the old validator's booleans.
        run(str(old_tools/'pokerattack-deploy'),'promote','production',digest,proof_hash)
        with storage.control_lock(),storage.deployment_lock():
            lease=storage.read(BASE/'lease')
            if lease['run_attempt']!=23 or lease['phase']!='bootstrap-legacy-recovered':raise ValueError('Recovery owner changed')
            storage.write(BASE/'interrupted/H05-legacy23-verified.json',{**lease,'actual_evidence_sha256':proof_hash})
            storage.write(BASE/'admission/state.json',{'active':False},0o644);(BASE/'lease').unlink()
        report['legacy_recovery']={'passed':True,'fresh_reset':fresh_ack,'actual_proof':proof,'actual_proof_sha256':proof_hash,
            'separate_promotion_decision_simulated':True,'original_attempt22_remains_abandoned':True,
            'saved_tooling_and_original_compose_used':True,'schema_migrations_executed':False}
        report['phase']='legacy-recovery-verified';output.write_bytes(canonical(report))
        # A new explicit first-introduction attempt deliberately resets the
        # completed legacy solo entry under a separate fresh acknowledgment.
        manifest,env=begin(modern_manifest,24,maintenance_manifest,maintenance)
        client_manifest=json.loads((PRIVATE/'downloads/client-manifest-v2.json').read_bytes())
        archive=PRIVATE/'downloads/client-v2.tar.gz';client_dir=PRIVATE/'H05-modern-client'
        check_archive(archive,client_manifest,client_dir)
        candidate={'mode':'candidate','archive_sha256':client_manifest['archive_sha256'],'client':identity_summary(client_manifest),
                   'probes':{'/'+name:client_manifest['files'][name] for name in ('index.html','release.json','appsettings.json')}}
        operator('client-authorize',24,candidate);publish(client_dir);operator('client-published',24)
        catalog=Archive(LocalStore(PRIVATE/'H05-accepted-clients'))
        provisional=ProvisionalClient(client_manifest,archive,lambda action:operator(action,24))
        if catalog.pointers()[0]['current'] is not None:raise ValueError('First client was already accepted')
        directory=PRIVATE/'H05-new-verification24'
        reviewed=server_verify.verify(manifest,env,status,transport('evidence'),provisional,directory)
        proof=json.loads((directory/'evidence.json').read_bytes())
        if not any(ref.startswith('bootstrap-client-artifact:') for ref in proof['artifact_references']):
            raise ValueError('Provisional artifact was falsely claimed accepted')
        provisional.accept_verified(catalog,RUN,24)
        reviews=[{'state':'approved','comment':reviewed['comment'],'environments':[{'name':'server-promotion'}],
                  'user':{'login':'isolated-bootstrap-rehearsal'}}]
        promoted=server_promote.promote(manifest,(directory/'evidence.json').read_bytes(),reviewed,env,status,transport('promotion'),lambda _:reviews)
        complete=server_finish.finish(manifest,promoted,env,status,transport('promotion'),catalog,PRIVATE/'H05-completed24.json')
        final=status.call('status production')
        if final['pairs']['previous-good']['digest']!=digest:raise ValueError('Legacy recovery pair was not retained')
        if any(json.loads(run('docker','inspect',name))[0]['Id']!=cid for name,cid in canaries.items()):raise ValueError('Shared fixture service replaced')
        if postgres!=run('systemctl','show','postgresql@16-main','-p','MainPID','--value'):raise ValueError('Shared PostgreSQL restarted')
        api_ids=run('docker','ps','-q','--filter','name=^/pokerattack-production-api-1$').splitlines()
        if len(api_ids)!=1:raise ValueError('More than one API authority')
        report.update(phase='complete',completion=complete,final_status=final,
            new_client_accepted_only_after_actual_proof=True,one_API_authority=True,shared_services_preserved=True,
            no_database_migrations_or_destructive_cleanup=True)
        report['cases'].append({'name':'legacy-restore-and-compatible-client-with-real-proof','passed':True})
        report['cases'].append({'name':'new-bootstrap-provisional-proof-acceptance-promotion-and-reopen','passed':True})
    except Exception as error:
        report.update(phase='failed-or-incomplete',error_type=type(error).__name__)
        try:report['diagnostic_status']=status.call('status production')
        except Exception:report['diagnostic_status']='unavailable'
        raise
    finally:output.write_bytes(canonical(report))


if __name__=='__main__':main()
