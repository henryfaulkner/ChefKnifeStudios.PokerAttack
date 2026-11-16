"""Stage one explicitly selected harness on the owned replica; retain failures."""
import json
from pathlib import Path
import sys

import azure_replica
import replica_rpc


def execute(name, report):
    allowed = {
        'resume_interruption_faults': 'H03-restoration-SSH-reconciled.json',
        'run_client_recovery': 'H03-client-restoration.json',
        'run_monitoring_lifecycle': 'H04-lifecycle.json',
        'run_concurrency': 'H05-concurrency.json',
        'run_bootstrap': 'H05-bootstrap.json',
        'resume_bootstrap': 'H05-bootstrap-attempt3.json',
    }
    repeats={'H04-lifecycle-attempt2.json':'--repeat-after-fixture-fix',
             'H04-lifecycle-attempt3.json':'--repeat-after-import-inspection'}
    if name=='run_bootstrap' and report=='H05-bootstrap-attempt2.json':repeats[report]='--resume-pre-seed'
    repeat = name in ('run_monitoring_lifecycle','run_bootstrap') and report in repeats
    if allowed.get(name) != report and not repeat:
        raise ValueError('Select an explicit reviewed fixture/report pair')
    state = json.loads(azure_replica.STATE.read_bytes())
    source = Path(__file__).with_name(name + '.py').read_text(encoding='utf-8')
    script = "/opt/pa-rehearsal/venv/bin/python - <<'PY'\nfrom pathlib import Path\n"
    script += "Path('/opt/pa-rehearsal/" + name + ".py').write_text(" + repr(source) + ",encoding='utf-8')\nPY\n"
    if name in ('run_concurrency','run_bootstrap','resume_bootstrap'):
        script+="/usr/bin/python3 - <<'PY'\nfrom pathlib import Path\n"
        for filename in ('bootstrap_control.py','bootstrap_recovery.py','release_lease.py','release_verifier.py','release_evidence.py','release-control.py'):
            raw=Path(__file__).parents[1].joinpath('linode',filename).read_text(encoding='utf-8')
            script+="path=Path('/opt/pokerattack/"+filename+"');path.write_text("+repr(raw)+",encoding='utf-8');path.chmod(0o644)\n"
        script+="PY\n"
    if name in ('run_bootstrap','resume_bootstrap'):
        script+="/usr/bin/python3 - <<'PY'\nfrom pathlib import Path\n"
        for filename in ('bootstrap_archive.py','bootstrap_artifacts.py','server_verify.py'):
            raw=Path(__file__).parents[1].joinpath('release',filename).read_text(encoding='utf-8')
            script+="Path('/opt/pa-rehearsal/source/deploy/release/"+filename+"').write_text("+repr(raw)+",encoding='utf-8')\n"
        raw=Path(__file__).parents[2].joinpath('tests/release/legacy-bootstrap.cjs').read_text(encoding='utf-8')
        script+="Path('/opt/pa-rehearsal/source/tests/release/legacy-bootstrap.cjs').write_text("+repr(raw)+",encoding='utf-8')\n"
        script+="root=Path('/var/lib/pa-rehearsal/legacy-tools');root.mkdir(mode=0o700,exist_ok=True)\n"
        for filename in ('compose.yaml','validate-config.py','pokerattack-deploy','pokerattack-ssh-deploy'):
            raw=Path(__file__).parents[2].joinpath('artifacts/H05-legacy-capture',filename).read_text(encoding='utf-8')
            script+="path=root/"+repr(filename)+";path.write_text("+repr(raw)+",encoding='utf-8');path.chmod(0o600)\n"
        script+="PY\n"
    script += "/opt/pa-rehearsal/venv/bin/python - <<'PY'\nimport subprocess,sys\nfrom pathlib import Path\n"
    script += "sys.path.insert(0,'/opt/pa-rehearsal')\nfrom blob_transfer import upload\n"
    args=['/opt/pa-rehearsal/venv/bin/python','/opt/pa-rehearsal/'+name+'.py']
    if repeat:args+=[repeats[report]]
    script += "result=subprocess.run(" + repr(args) + ",capture_output=True)\n"
    script += "Path('/var/lib/pa-rehearsal/logs/" + name + "-private.log').write_bytes(result.stdout+result.stderr)\n"
    script += "path=Path('/var/lib/pa-rehearsal/" + report + "')\n"
    script += "if path.is_file():upload(" + repr(state['account']) + ",path.name,path)\nraise SystemExit(result.returncode)\nPY\n"
    replica_rpc.invoke(name + ('-'+report.removeprefix('H04-lifecycle-').removesuffix('.json') if repeat else ''), script)


if __name__ == '__main__':
    execute(*sys.argv[1:])
