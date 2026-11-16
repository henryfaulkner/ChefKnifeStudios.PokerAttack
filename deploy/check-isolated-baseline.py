"""Run the migration-CI baseline locally; all database/browser fixtures are disposable.

Logs live in ignored artifacts/, while the JSON result contains identities and exit
codes only. Never accepts a target URL or a database connection from the caller.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'artifacts' / 'deployment-baseline'


def main():
    global OUTPUT
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--resume', action='store_true', help='Resume the first failed/unrecorded stage; preserve earlier logs.')
    parser.add_argument('--output', type=Path, default=OUTPUT, help='Distinct evidence directory under repository artifacts/.')
    options = parser.parse_args()
    OUTPUT = options.output.resolve()
    if not OUTPUT.is_relative_to((ROOT / 'artifacts').resolve()) or OUTPUT == (ROOT / 'artifacts').resolve():
        raise ValueError('Baseline evidence must stay inside a named artifacts subdirectory')
    OUTPUT.mkdir(parents=True, exist_ok=True)
    python = sys.executable
    npm = shutil.which('npm.cmd' if os.name == 'nt' else 'npm')
    steps = [
        ('business-logic', ['dotnet', 'test', 'src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests', '-c', 'Release']),
        ('api-build', ['dotnet', 'build', 'src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI', '-c', 'Release']),
        ('hosting', ['dotnet', 'test', 'src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI.Tests', '-c', 'Release']),
        ('npm-ci', [npm, 'ci', '--prefix', 'tests']),
        ('guards', [npm, '--prefix', 'tests', 'run', 'test:guards']),
        ('pilot-artifact', ['node', '--test', 'deploy/prepare-client-artifact.test.mjs']),
        ('wasm-tools', ['dotnet', 'workload', 'list'] if 'wasm-tools' in subprocess.check_output(
            ['dotnet', 'workload', 'list'], cwd=ROOT, text=True) else
            ['dotnet', 'workload', 'install', 'wasm-tools', '--skip-manifest-update']),
        ('client-publish', ['dotnet', 'publish', 'src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp', '-c', 'Release', '-o', 'artifacts/client-wp04']),
        ('api-image', ['docker', 'build', '--platform', 'linux/amd64', '-f', 'src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Dockerfile', '-t', 'pokerattack-api:wp05-local', '.']),
        ('migration-image', ['docker', 'build', '--platform', 'linux/amd64', '-f', 'src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Dockerfile', '-t', 'pokerattack-migrations:wp05-local', '.']),
        ('release-tests', ['docker', 'run', '--rm', '-v', f'{ROOT}:/work:ro', 'python:3.13-bookworm', 'python', '/work/deploy/linode/test_release.py']),
        ('browser-install', [npm, 'exec', '--prefix', 'tests', '--', 'playwright', 'install', 'chromium']),
        ('container-browser', [python, 'deploy/linode/check-local-containers.py']),
    ]
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    report = {'started_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'source_sha': source,
              'runner_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'steps': []}
    if options.resume:
        previous = json.loads((OUTPUT / 'results.json').read_text(encoding='utf-8'))
        if previous['source_sha'] != source:
            raise ValueError('Cannot resume across source changes')
        (OUTPUT / ('attempt-' + str(len(list(OUTPUT.glob('attempt-*.json'))) + 1) + '.json')).write_text(
            json.dumps(previous, indent=2) + '\n', encoding='utf-8')
        # This option is for interrupted baseline tooling, before application edits.
        # Source-file hashes must still be checked by the operator before reuse.
        for step in previous['steps']:
            if step['exit_code'] != 0:
                break
            report['steps'].append(step)
        steps = steps[len(report['steps']):]
    elif (OUTPUT / 'results.json').exists():
        raise ValueError('Existing baseline results: use --resume or archive artifacts/deployment-baseline first')
    for name, command in steps:
        print(f'{name}: running', flush=True)
        log_path = OUTPUT / f'{name}.log'
        if log_path.exists():
            archived = OUTPUT / f'{name}.{datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")}.log'
            log_path.rename(archived)
        with log_path.open('w', encoding='utf-8') as log:
            try:
                result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=1800)
                code = result.returncode
            except subprocess.TimeoutExpired:
                code = 124
        report['steps'].append({'name': name, 'command': command, 'exit_code': code,
                                'finished_utc': datetime.datetime.now(datetime.timezone.utc).isoformat()})
        (OUTPUT / 'results.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print(f'{name}: exit {code}', flush=True)
        if code:
            return code
    return 0


if __name__ == '__main__':
    sys.exit(main())
