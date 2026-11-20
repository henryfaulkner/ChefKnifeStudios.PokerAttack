"""Publish/render the single owned dashboard using normal authenticated gcx."""
import argparse
import copy
import datetime
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).parents[2]
UID = 'linode-docker-containers'
API = 'dashboard.grafana.app/v1beta1'


def gcx(*args, timeout=60):
    result = subprocess.run(['gcx', *args, '--context', 'transitjazz', '-o', 'json'], capture_output=True, timeout=timeout)
    if result.returncode:
        raise ValueError('Grafana authoring/rendering operation failed; inspect live resource before retry')
    return json.loads(result.stdout)


def get():
    resource = gcx('dashboards', 'get', UID, '--api-version', API)
    if resource['metadata']['annotations'].get('grafana.app/folder') != 'integration---linux-node':
        raise ValueError('Owned dashboard folder changed; inspect')
    return resource


def update(resource, spec, name):
    manifest = {'apiVersion': API, 'kind': 'Dashboard', 'metadata': {
        'name': UID, 'namespace': resource['metadata']['namespace'],
        'resourceVersion': resource['metadata']['resourceVersion'],
        'annotations': {'grafana.app/folder': 'integration---linux-node'}}, 'spec': spec}
    path = ROOT / 'artifacts' / ('J03-' + name + '-resource.json')
    path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return gcx('dashboards', 'update', UID, '--api-version', API, '-f', str(path))


def variable(spec):
    return next(item for item in spec['templating']['list'] if item['name'] == 'container')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=('single', 'other', 'multi', 'all', 'probe', 'publish'))
    parser.add_argument('--phase', default='current')
    parser.add_argument('--panel', type=int)
    args = parser.parse_args()
    if not args.phase.replace('-', '').isalnum():
        raise ValueError('Fixed local snapshot phase name required')
    live = get()
    if args.mode == 'publish':
        manifest = json.loads((ROOT / 'deploy/monitoring/linode-docker-dashboard.resource.json').read_bytes())
        update(live, manifest['spec'], 'source')
        print('Current generated source published to existing UID with optimistic concurrency.')
        return
    restore = None
    if args.mode == 'multi':
        spec = copy.deepcopy(live['spec'])
        restore = copy.deepcopy(variable(spec)['current'])
        selected = {'text': ['pokerattack-production-api-1', 'transitjazz-app-proxy-1'],
                    'value': ['pokerattack-production-api-1', 'transitjazz-app-proxy-1']}
        variable(spec)['current'] = selected
        update(live, spec, 'temporary-multi')
    selections = {'single': 'pokerattack-production-api-1', 'other': 'transitjazz-app-server-1', 'all': '$__all'}
    if args.mode == 'probe':
        state = json.loads((ROOT / 'artifacts/J03-live-probe-state.json').read_bytes())
        selections['probe'] = state['remote']['name']
    directory = ROOT / 'artifacts/J03-snapshots' / (args.phase + '-' + args.mode)
    command = ['dashboards', 'snapshot', UID, '--output-dir', str(directory), '--since', '1h',
               '--var', 'host=expense-tracker-vm', '--width', '1920', '--theme', 'dark', '--timeout', '45s']
    if args.mode in selections:
        command += ['--var', 'container=' + selections[args.mode]]
    if args.panel:
        command += ['--panel', str(args.panel), '--height', '600']
    try:
        rendered = gcx(*command, timeout=55)
        if rendered['summary']['succeeded'] != 1:
            raise ValueError('Rendering did not succeed')
        report = {'task': 'J03/J04', 'mode': args.mode, 'phase': args.phase,
                  'recorded_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  'selection': selected if args.mode == 'multi' else selections.get(args.mode),
                  'renderer_result': rendered, 'visual_inspection': 'Required separately; a rendered file can contain an error page.'}
        path = ROOT / 'docs/deployment-evidence' / ('J03-render-' + args.phase + '-' + args.mode + '.json')
        path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print(rendered['files'][0]['file_path'])
    finally:
        if restore is not None:
            current = get()
            if variable(current['spec'])['current'] != selected:
                raise ValueError('Saved selection changed externally; do not overwrite it')
            spec = copy.deepcopy(current['spec']); variable(spec)['current'] = restore
            update(current, spec, 'restore-default-selection')


if __name__ == '__main__': main()
