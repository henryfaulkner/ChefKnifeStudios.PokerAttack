"""Rehearse a protected migration snapshot in a disposable local cluster, never live."""
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import time
import uuid

backup = Path(sys.argv[1]).resolve()
if not re.fullmatch(r'pokerattack-migration-\d{8}T\d{6}Z', backup.name):
    sys.exit('Expected the explicitly named migration backup directory')
for line in (backup / 'SHA256SUMS').read_text().splitlines():
    expected, name = line.split('  ', 1)
    if not re.fullmatch(r'[A-Za-z0-9_.-]+', name):
        sys.exit('Invalid backup manifest')
    if hashlib.sha256((backup / name).read_bytes()).hexdigest() != expected:
        sys.exit('Backup checksum mismatch')

name = 'pokerattack-restore-' + uuid.uuid4().hex[:12]
started = time.monotonic()
report = {'snapshot': backup.name, 'databases': {}, 'roles_recreated': False}
env = {**os.environ, 'POSTGRES_PASSWORD': secrets.token_hex(32)}
created = False


def command(*args, input=None):
    result = subprocess.run(args, input=input, capture_output=True, text=True)
    if result.returncode:
        (backup / 'restore-private-error.txt').write_text(result.stdout + result.stderr, encoding='utf-8')
        raise RuntimeError('Isolated restore failed; private diagnostics saved beside encrypted snapshot')
    return result.stdout


try:
    result = subprocess.run(['docker', 'run', '-d', '--name', name, '--tmpfs', '/var/lib/postgresql/data',
                             '-e', 'POSTGRES_PASSWORD', '-v', f'{backup}:/backup:ro', 'pgvector/pgvector:pg16'],
                            env=env, capture_output=True, text=True)
    if result.returncode:
        sys.exit('Cannot start isolated restore cluster')
    created = True
    for _ in range(60):
        result = subprocess.run(['docker', 'exec', name, 'pg_isready', '-U', 'postgres'], capture_output=True)
        if result.returncode == 0:
            break
        time.sleep(1)
    else:
        raise RuntimeError('Restore cluster startup timed out')
    report['postgres_version'] = command('docker', 'exec', name, 'postgres', '--version').strip()
    # The initialized scratch superuser already exists. Preserve all ALTER ROLE/hash/settings
    # statements and every other role instead of suppressing arbitrary restore errors.
    globals_sql = (backup / 'globals.sql').read_text(encoding='utf-8')
    globals_sql, removed = re.subn(r'^CREATE ROLE postgres;\s*$', '', globals_sql, flags=re.M)
    if removed != 1:
        raise RuntimeError('Unexpected globals format')
    command('docker', 'exec', '-i', name, 'psql', '-X', '-U', 'postgres', '-v', 'ON_ERROR_STOP=1', input=globals_sql)
    report['roles_recreated'] = True
    for database in ('expensetrackerdb', 'expensetrackerdb_dev', 'personal_health_hub', 'transitjazz'):
        command('docker', 'exec', name, 'createdb', '-U', 'postgres', database)
        command('docker', 'exec', name, 'pg_restore', '-U', 'postgres', '--exit-on-error', '--dbname', database, f'/backup/{database}.dump')
        # Count rows in the dump's COPY streams without printing or persisting row values.
        process = subprocess.Popen(['docker', 'exec', name, 'pg_restore', '--file=-', f'/backup/{database}.dump'],
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')
        counts, table, rows = {}, None, 0
        for line in process.stdout:
            if table is not None:
                if line.rstrip('\r\n') == '\\.':
                    counts[table] = rows
                    table = None
                else:
                    rows += 1
            elif line.startswith('COPY '):
                table = line.split(' (', 1)[0][5:]
                identifier = r'(?:"(?:[^\"]|\"\")*"|[a-z_][a-z0-9_]*)'
                if not re.fullmatch(identifier + r'\.' + identifier, table):
                    raise RuntimeError('Unexpected dump table identifier')
                rows = 0
        if process.wait() != 0 or table is not None:
            raise RuntimeError('Dump row verification stream failed')
        for table, expected in counts.items():
            actual = command('docker', 'exec', name, 'psql', '-X', '-U', 'postgres', '-d', database,
                             '-At', '-v', 'ON_ERROR_STOP=1', '-c', f'SELECT count(*) FROM {table};').strip()
            if int(actual) != expected:
                raise RuntimeError('Restored table differs from dump COPY row count')
        report['databases'][database] = {'tables_verified': len(counts), 'rows_verified': sum(counts.values()), 'counts': counts}
    report['restored_extensions'] = command('docker', 'exec', name, 'psql', '-X', '-U', 'postgres', '-d', 'expensetrackerdb',
                                            '-At', '-c', 'SELECT extname,extversion FROM pg_extension ORDER BY extname;').strip().splitlines()
    report['pokerattack_owner'] = command('docker', 'exec', name, 'psql', '-X', '-U', 'postgres', '-d', 'expensetrackerdb',
                                         '-At', '-c', "SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname='PokerAttack';").strip()
    report['duration_seconds'] = round(time.monotonic() - started, 1)
    (backup / 'restore-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({**report, 'databases': {db: {k: v for k, v in detail.items() if k != 'counts'} for db, detail in report['databases'].items()}}, indent=2))
finally:
    if created:
        subprocess.run(['docker', 'rm', '-f', name], capture_output=True)
