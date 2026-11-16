"""Validate immutable release documents in trusted build/review tooling.

Install requirements-validation.txt in an isolated environment. This module never
authorizes a release: host identity/freshness and protected job authority must also
be checked at each mutation. No network schema resolution is performed.
"""
import json
from pathlib import Path, PurePosixPath

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parent


def reject_duplicates(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON field')
        result[key] = value
    return result


def load_document(raw, kind=None):
    # Full client manifests contain a bounded per-file map. Host commands still
    # accept at most 64 KiB and receive a separate identity summary, never this map.
    limit = 32 * 1024 * 1024 if kind == 'client-manifest' else 65536
    if len(raw) > limit:
        raise ValueError('Release document exceeds its size limit')
    return json.loads(raw, object_pairs_hook=reject_duplicates,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError('Non-finite JSON number')))


def validate_document(kind, document):
    if kind not in ('server-manifest', 'client-manifest', 'evidence'):
        raise ValueError('Unknown release document type')
    schema = json.loads((ROOT / (kind + '.schema.json')).read_text(encoding='utf-8'))
    Draft202012Validator(schema).validate(document)
    workflow = document['workflow']
    expected_url = f"https://github.com/{workflow['repository']}/actions/runs/{workflow['run_id']}"
    if workflow['run_url'] != expected_url:
        raise ValueError('Workflow reference differs from run identity')
    if kind == 'server-manifest':
        for name in ('api', 'migration'):
            if document[name]['source_revision'] != document['source_sha'] or document[name]['tag'] != document['source_sha']:
                raise ValueError('Image source/tag differs from manifest source')
    if kind == 'client-manifest':
        for name in document['files']:
            if str(PurePosixPath(name)) != name or any(part in ('', '.', '..') for part in name.split('/')):
                raise ValueError('Noncanonical archive member')
        if not {'index.html', 'release.json'}.issubset(document['files']):
            raise ValueError('Client archive is missing required public files')
    return document
