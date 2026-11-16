"""Explicit H05 continuation after inspected legacy-verifier failure only."""
from run_bootstrap import *

def main():
    machine_guard();status=transport('status')
    prior=json.loads((PRIVATE/'H05-bootstrap-attempt2.json').read_bytes())
    if prior['phase']!='failed-or-incomplete' or len(prior['cases'])!=1 or prior.get('legacy_recovery'):
        raise ValueError('Not the inspected legacy verifier failure')
    report=copy.deepcopy(prior);report.pop('diagnostic_status',None);report.pop('error_type',None)
    report['prior_attempt_retained']='H05-bootstrap-attempt2.json; legacy empty RoundEnded payload was incorrectly ignored by verifier'
    output=PRIVATE/'H05-bootstrap-attempt3.json'
    try:
        live=status.call('status production')
        lease=storage.read(BASE/'lease')
        recovery=storage.read(bootstrap_recovery.CONFIG)
        old_tools=bootstrap_recovery.validate_tools(recovery['tooling'])
        if lease['run_attempt']!=23 or lease['phase']!='bootstrap-legacy-recovered' or live['transaction']:
            raise ValueError('Legacy recovery ownership changed')
        if any(live['sessions'].values()) or not live['readiness']['ready']:
            raise ValueError('Legacy failed verifier left sessions or unready runtime; inspect first')
        if live['pairs']['active']['digest']!=recovery['pair']['digest'] or live['pairs']['active']['config_sha256']!=recovery['pair']['config_sha256']:
            raise ValueError('Frozen recovery pair changed')
        if live['runtime']['source_revision']!=recovery['source_sha'] or live['runtime']['image_id']!=recovery['image_id']:
            raise ValueError('Frozen legacy source/image changed')
        capability_missing()
        old={'source_revision':recovery['source_sha']}
        digest=recovery['pair']['digest']
        fresh_ack=lease['acknowledgment']
        first=json.loads((PRIVATE/'H02-result-attempt5.json').read_bytes())['manifest']
        modern_manifest=copy.deepcopy(first)
        maintenance_manifest=json.loads((PRIVATE/'downloads/H05-maintenance.json').read_bytes())
        legacy_manifest=json.loads((PRIVATE/'downloads/H05-legacy-recovery.json').read_bytes())
        maintenance=PRIVATE/'H05-maintenance-client'
        canaries={name:json.loads(run('docker','inspect',name))[0]['Id'] for name in ('h02-caddy','h02-registry')}
        postgres=run('systemctl','show','postgresql@16-main','-p','MainPID','--value')
        private=PRIVATE/'runner-temp/H05-legacy-resumed';private.mkdir(mode=0o700)
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
