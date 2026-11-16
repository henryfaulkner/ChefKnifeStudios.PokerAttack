"""Evaluate the actual dashboard PromQL against deterministic isolated fixtures."""
import copy
import datetime
import json
from pathlib import Path
import subprocess
import sys

import yaml
import dashboard
import docker_snapshot as collector
from test_docker_snapshot import DockerFixture,CID

sys.path.insert(0,str(Path(__file__).parents[1]/'linode'))
from local_docker import assert_local_docker

PROMTOOL='prom/prometheus@sha256:49214755b6153f90a597adcbff0252cc61069f8ab69ce8411285cd4a560e8038'
SECOND='e'*64
ROOT=Path(__file__).parents[2]


def expression(expr,selection='.*'):return expr.replace('${host:regex}','fixture-host').replace('${container:regex}',selection)


def inputs(case):
    docker=DockerFixture();docker.containers.append({'Id':SECOND,'Names':['/stopped']})
    original=docker.get
    def get(path):
        result=original(path)
        if path=='/containers/'+SECOND+'/json':
            result.update(Id=SECOND,Name='/stopped')
            result['State'].update(Status='exited');result['State'].pop('Health',None)
            result['HostConfig'].update(NanoCpus=0,Memory=0,NetworkMode='host')
            if case in ('unlimited','recreated'):result['State']['Status']='running'
            if case=='recreated':result['Name']='/example'
        return result
    docker.get=get
    metrics,success,_=collector.collect(docker,'fixture-host',now=0)
    if not success:raise ValueError('Invalid dashboard fixture')
    result=[]
    for line in metrics.lines:
        series,value=line.strip().rsplit(' ',1)
        values=value+'+0x4'
        if any(name in series for name in ('container_identity_timestamp_seconds','container_sample_timestamp_seconds')):
            values='0+60x4'
            if case=='stale':values='0+0x4'
            if case=='daemon' or case=='removed' and SECOND in series:values='0+45x4'
            if case=='recreated' and CID in series:values='0+45x4'
        if 'container_cpu_usage_seconds_total' in series:values='0+6x4'
        if case=='partial' and SECOND in series and 'container_collection_success' in series:values='0+0x4'
        result.append({'series':series,'values':values})
    for name,value in [('snapshot_timestamp_seconds','0+0x4' if case=='stale' else '0+60x4'),
                       ('inventory_success','0+0x4' if case=='daemon' else '1+0x4'),
                       ('collection_success','0+0x4' if case in ('daemon','partial') else '1+0x4'),
                       ('last_success_timestamp_seconds','0+60x4')]:
        result.append({'series':'linode_docker_'+name+'{host="fixture-host"}','values':value})
    if case=='metadata':
        for entry in copy.deepcopy(result):
            if CID in entry['series'] and 'container_identity_timestamp_seconds' in entry['series']:
                entry['series']=entry['series'].replace('configured_image="registry.example/app:main"','configured_image="old.example/app:old"')
                entry['values']='0+45x4';result.append(entry)
    return result


def expected(values):
    return [{'labels':'{host="fixture-host",container_id="'+cid+'"}','value':value} for cid,value in values.items()]


def fixture():
    tests=[]
    for case in ('fresh','stale','daemon','removed','partial','metadata','unlimited','recreated','single','multiple'):
        state={CID:2,SECOND:6};health={CID:1,SECOND:0}
        if case in ('stale','daemon'):state=dict.fromkeys(state,0);health=dict.fromkeys(health,4)
        if case=='partial':state[SECOND]=0;health[SECOND]=4
        if case=='removed':state.pop(SECOND);health.pop(SECOND)
        if case=='unlimited':state[SECOND]=2
        if case=='single':state.pop(SECOND);health.pop(SECOND)
        if case=='recreated':state={SECOND:2};health={SECOND:0}
        selection='example' if case=='single' else 'example|stopped' if case=='multiple' else '.*'
        rewrite=lambda expr:expression(expr,selection)
        expressions=[{'expr':'sum by(host,container_id) ('+rewrite(expr)+')','eval_time':'4m','exp_samples':expected(values)}
                     for expr,values in [(dashboard.STATE,state),(dashboard.HEALTH,health)]]
        visible=case not in ('stale','daemon')
        for expr,value in ((dashboard.CPU,0.1),(dashboard.CPU_PERCENT,40),(dashboard.MEMORY,99000)):
            values={CID:value} if visible else {}
            if case=='unlimited' and expr!=dashboard.CPU_PERCENT:values[SECOND]=value
            if case=='recreated':values={SECOND:value} if expr!=dashboard.CPU_PERCENT else {}
            expressions.append({'expr':'sum by(host,container_id) ('+rewrite(expr)+')','eval_time':'4m',
                                'exp_samples':expected(values)})
        expressions.append({'expr':'count('+rewrite(dashboard.META)+')','eval_time':'4m',
                            'exp_samples':[{'labels':'{}','value':1 if case in ('removed','recreated','single') else 2}]})
        tests.append({'name':case,'interval':'1m','input_series':inputs(case),'promql_expr_test':expressions})
    # Parse/evaluate every shipped target as well as assertions above. This
    # catches syntax mistakes in less prominent lifecycle/resource panels.
    for panel in dashboard.build()['panels']:
        for target in panel['targets']:
            # The comparison discards all values but still requires the complete
            # real query to parse/evaluate; behavior assertions above are separate.
            tests[0]['promql_expr_test'].append({'expr':'('+expression(target['expr'])+') > 1e30',
                                                'eval_time':'4m','exp_samples':[]})
    # Prometheus rate arithmetic may differ by one final mantissa bit. This
    # documented option permits only that difference, not broad tolerances.
    return {'rule_files':[],'evaluation_interval':'1m','fuzzy_compare':True,'tests':tests}


def main():
    assert_local_docker()
    output=ROOT/'artifacts';output.mkdir(exist_ok=True)
    path=output/'G07-dashboard-promql-fixtures.yml'
    path.write_text(yaml.safe_dump(fixture(),sort_keys=False),encoding='utf-8')
    result=subprocess.run(['docker','run','--rm','--network','none','--entrypoint','/bin/promtool',
        '-v',str(path.resolve())+':/fixture.yml:ro',PROMTOOL,'test','rules','/fixture.yml'],capture_output=True,timeout=90)
    (output/'G07-dashboard-promql.log').write_bytes(result.stdout+result.stderr)
    print((result.stdout+result.stderr).decode(errors='replace')[-4000:])
    if result.returncode:raise SystemExit(result.returncode)
    (output/'G07-dashboard-promql-result.json').write_text(json.dumps({'timestamp_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'cases':[item['name'] for item in fixture()['tests']],'promtool':PROMTOOL,
        'behavioral_assertions':60,'all_dashboard_targets_evaluated':True,'exit_code':0},indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':main()
