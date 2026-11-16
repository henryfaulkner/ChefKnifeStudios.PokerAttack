"""Dependency-free host validation for the versioned release schema.

The JSON Schemas remain the build/review contract. This deliberately narrow host
validator is tested against those schemas and rejects extra security fields.
"""
import re


def fields(value, expected):
    if not isinstance(value, dict) or set(value) != set(expected):
        raise ValueError('Invalid document fields')


def integer(value, maximum=None):
    if type(value) is not int or value < 1 or maximum is not None and value > maximum:
        raise ValueError('Invalid positive integer')


def pattern(value, expression):
    if not isinstance(value, str) or not re.fullmatch(expression, value, re.ASCII):
        raise ValueError('Invalid identity')


def text(value):
    if not isinstance(value, str) or not 1 <= len(value) <= 512:
        raise ValueError('Invalid reference')


def workflow(value):
    fields(value, ('repository', 'run_id', 'run_attempt', 'run_url'))
    integer(value['run_id'], 10**20 - 1)
    integer(value['run_attempt'], 99999)
    if value['repository'] != 'henryfaulkner/ChefKnifeStudios.PokerAttack' or value['run_url'] != (
            f"https://github.com/{value['repository']}/actions/runs/{value['run_id']}"):
        raise ValueError('Wrong workflow reference')


def server(value):
    fields(value, ('format', 'source_sha', 'workflow', 'api', 'migration', 'config_sha256', 'schema', 'verification_references'))
    if value['format'] != 'pokerattack-server-v1':
        raise ValueError('Wrong format')
    pattern(value['source_sha'], r'[a-f0-9]{40}')
    pattern(value['config_sha256'], r'[a-f0-9]{64}')
    workflow(value['workflow'])
    for name, repository in [('api', 'webapi'), ('migration', 'data')]:
        image = value[name]
        fields(image, ('repository', 'digest', 'local_image_id', 'tag', 'source_revision', 'platform'))
        if image['repository'] != 'chefknife.azurecr.io/chefknifestudios.pokerattack.server.' + repository or image['platform'] != 'linux/amd64':
            raise ValueError('Wrong image repository/platform')
        for field in ('digest', 'local_image_id'):
            pattern(image[field], r'sha256:[a-f0-9]{64}')
        if image['tag'] != value['source_sha'] or image['source_revision'] != value['source_sha']:
            raise ValueError('Source mismatch')
    schema = value['schema']
    fields(schema, ('required_migrations', 'compatible_with_known_good', 'review_reference'))
    if schema['compatible_with_known_good'] is not True or not isinstance(schema['required_migrations'], list):
        raise ValueError('Unreviewed schema')
    if len(schema['required_migrations']) > 1000 or len(set(schema['required_migrations'])) != len(schema['required_migrations']):
        raise ValueError('Invalid migration prerequisites')
    for migration in schema['required_migrations']:
        pattern(migration, r'[0-9]{14}_[A-Za-z0-9_]+')
    text(schema['review_reference'])
    if not isinstance(value['verification_references'], list) or not 1 <= len(value['verification_references']) <= 32:
        raise ValueError('Missing isolated evidence')
    for reference in value['verification_references']:
        text(reference)
    return value


def evidence(value):
    fields(value, ('format', 'source_sha', 'workflow', 'digest', 'migration_digest', 'config_sha256',
                   'client_source_sha', 'client_archive_sha256', 'verifier_id', 'verified_unix',
                   'checks', 'artifact_references'))
    if value['format'] != 'pokerattack-evidence-v1':
        raise ValueError('Wrong evidence format')
    workflow(value['workflow'])
    for field in ('source_sha', 'client_source_sha'):
        pattern(value[field], r'[a-f0-9]{40}')
    for field in ('digest', 'migration_digest'):
        pattern(value[field], r'sha256:[a-f0-9]{64}')
    for field in ('config_sha256', 'client_archive_sha256'):
        pattern(value[field], r'[a-f0-9]{64}')
    pattern(value['verifier_id'], r'[a-f0-9]{32}')
    integer(value['verified_unix'])
    fields(value['checks'], ('https', 'served_client', 'signalr', 'multiplayer', 'solo', 'private_message_isolation'))
    if any(result is not True for result in value['checks'].values()):
        raise ValueError('Incomplete verification')
    if not isinstance(value['artifact_references'], list) or not 1 <= len(value['artifact_references']) <= 32:
        raise ValueError('Missing verification artifacts')
    for reference in value['artifact_references']:
        text(reference)
    return value


def client_identity(value):
    fields(value, ('format', 'source_sha', 'workflow', 'artifact_id', 'archive_sha256',
                   'settings_sha256', 'minimum_server_capability', 'settings_files'))
    if value['format'] != 'pokerattack-client-identity-v1':
        raise ValueError('Wrong client identity format')
    pattern(value['source_sha'], r'[a-f0-9]{40}')
    workflow(value['workflow'])
    integer(value['artifact_id'], 10**20 - 1)
    integer(value['minimum_server_capability'])
    for name in ('archive_sha256', 'settings_sha256'):
        pattern(value[name], r'[a-f0-9]{64}')
    files = value['settings_files']
    if not isinstance(files, dict) or not 1 <= len(files) <= 256 or files.get('appsettings.json') != value['settings_sha256']:
        raise ValueError('Invalid shipped settings identity')
    for name, digest in files.items():
        pattern(name, r'appsettings(?:\.[A-Za-z0-9_.-]{1,128})?\.json')
        pattern(digest, r'[a-f0-9]{64}')
    return value
