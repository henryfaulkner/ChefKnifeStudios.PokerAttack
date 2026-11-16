const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { buildEvidence, CHECKS } = require('./evidence.cjs');

function fixture() {
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '../../deploy/release/server-manifest.example.json')));
  const packet = { server_manifest: manifest, run_id: 20, run_attempt: 2, verifier_id: 'd'.repeat(32),
    client: { source_sha: 'e'.repeat(40), archive_sha256: 'f'.repeat(64) }, artifact_references: ['protected-job:20/2'] };
  const result = { checks: Object.fromEntries(CHECKS.map(check => [check, true])), verified_unix: 100,
    client_source_sha: packet.client.source_sha, client_archive_sha256: packet.client.archive_sha256 };
  return { packet, result };
}

test('evidence preserves original build provenance and current verifier execution', () => {
  const { packet, result } = fixture();
  const evidence = buildEvidence(packet, result, 99, 101);
  assert.equal(evidence.source_sha, packet.server_manifest.source_sha);
  assert.equal(evidence.workflow.run_id, 20);
  assert.equal(evidence.workflow.run_attempt, 2);
  assert.equal(evidence.verified_unix, 100);
  assert.ok(!JSON.stringify(evidence).includes('credential'));
});

test('failed, incomplete, substituted or old results cannot become proof', () => {
  for (const fault of [...CHECKS, 'missing-check', 'client', 'archive', 'time', 'future', 'source']) {
    const { packet, result } = fixture();
    if (CHECKS.includes(fault)) result.checks[fault] = false;
    if (fault === 'missing-check') delete result.checks.solo;
    if (fault === 'client') result.client_source_sha = '0'.repeat(40);
    if (fault === 'archive') result.client_archive_sha256 = '0'.repeat(64);
    if (fault === 'time') result.verified_unix = 98;
    if (fault === 'future') result.verified_unix = 102;
    if (fault === 'source') packet.server_manifest.api.source_revision = '0'.repeat(40);
    assert.throws(() => buildEvidence(packet, result, 99, 101), fault);
  }
});
