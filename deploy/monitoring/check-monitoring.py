"""Reproducible Linux unit/Alloy/PromQL validation, without host mutation."""
import datetime
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import alloy_textfile
sys.path.insert(0,str(Path(__file__).parents[1]/'linode'))
from local_docker import assert_local_docker

ROOT=Path(__file__).parents[2]
ALLOY='grafana/alloy@sha256:8ab871206bd2d8f89277f53abffee5f452a293980788d52f85fa183a87a505a4'


def main():
    assert_local_docker()
    output=ROOT/'artifacts';output.mkdir(exist_ok=True);results=[]
    def run(name,args,timeout=240):
        result=subprocess.run(args,cwd=ROOT,capture_output=True,timeout=timeout)
        path=output/(name+'.log');path.write_bytes(result.stdout+result.stderr)
        results.append({'name':name,'exit_code':result.returncode,'log':path.relative_to(ROOT).as_posix(),
                        'log_sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
        if result.returncode:raise ValueError(name+' failed; inspect isolated log')
    run('monitoring-unit-tests',['docker','run','--rm','--network','none','-v',str(ROOT)+':/work:ro',
        '-w','/work/deploy/monitoring','python:3.13-bookworm','python','-m','unittest','discover','-v'])
    run('monitoring-validator-build',['docker','build','-f','deploy/monitoring/validation.Dockerfile',
        '-t','pokerattack-monitoring-validation:fixture','deploy/monitoring'])
    code='''import pathlib,shutil,subprocess
root=pathlib.Path('/tmp/validated-units');root.mkdir()
files=[]
for name in ('linode-docker-snapshot.service','linode-docker-snapshot.timer'):
 p=root/name;shutil.copyfile(pathlib.Path('/units')/name,p);p.chmod(0o644);files.append(str(p))
subprocess.run(['systemd-analyze','--version'],check=True)
raise SystemExit(subprocess.call(['systemd-analyze','verify',*files]))
'''
    run('monitoring-unit-validation',['docker','run','--rm','--network','none','-v',
        str(ROOT/'deploy/monitoring')+':/units:ro','pokerattack-monitoring-validation:fixture','python3','-c',code])
    shape=json.loads((ROOT/'docs/deployment-evidence/G01-live-monitoring-shape.json').read_bytes())
    raw='\n\n'.join(item['configuration'] for item in shape['blocks'] if item['name']=='integrations_node_exporter')+'''
discovery.relabel "integrations_node_exporter" {
  targets = prometheus.exporter.unix.integrations_node_exporter.targets
  rule { target_label = "instance"
         replacement = "expense-tracker-vm" }
}
otelcol.receiver.prometheus "integrations_node_exporter" {
  output { metrics = [otelcol.exporter.prometheus.fixture.input] }
}
otelcol.exporter.prometheus "fixture" { forward_to = [] }
'''
    fixture=output/'monitoring-alloy-fixture.alloy'
    fixture.write_text(alloy_textfile.prepare(raw),encoding='utf-8')
    run('monitoring-alloy-version',['docker','run','--rm','--network','none',ALLOY,'--version'])
    run('monitoring-alloy-validation',['docker','run','--rm','--network','none','-v',str(fixture)+':/fixture.alloy:ro',ALLOY,'validate','/fixture.alloy'])
    run('monitoring-dashboard-queries',[sys.executable,'deploy/monitoring/check-dashboard.py'])
    (output/'monitoring-checks.json').write_text(json.dumps({'timestamp_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope':'Local disposable validators only; no installed/live claim','checks':results},indent=2)+'\n',encoding='utf-8')
    print('Monitoring source checks passed: Linux behavior, systemd, installed-version Alloy and dashboard PromQL.')


if __name__=='__main__':main()
