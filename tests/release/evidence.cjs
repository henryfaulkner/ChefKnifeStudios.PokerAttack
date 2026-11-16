const assert = require('node:assert/strict');
const CHECKS = Object.freeze(['https', 'served_client', 'signalr', 'multiplayer', 'solo', 'private_message_isolation']);

function buildEvidence(packet, result, startedUnix, now = Math.floor(Date.now() / 1000)) {
  const manifest = packet.server_manifest;
  assert.equal(manifest.format, 'pokerattack-server-v1');
  assert.equal(manifest.api.repository, 'chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi');
  assert.equal(manifest.migration.repository, 'chefknife.azurecr.io/chefknifestudios.pokerattack.server.data');
  assert.match(manifest.source_sha, /^[a-f0-9]{40}$/);
  assert.match(manifest.config_sha256, /^[a-f0-9]{64}$/);
  assert.match(manifest.api.digest, /^sha256:[a-f0-9]{64}$/);
  assert.match(manifest.migration.digest, /^sha256:[a-f0-9]{64}$/);
  assert.equal(manifest.api.source_revision, manifest.source_sha);
  assert.equal(manifest.migration.source_revision, manifest.source_sha);
  assert.deepEqual(Object.keys(result.checks).sort(), [...CHECKS].sort());
  assert.ok(CHECKS.every(check => result.checks[check] === true));
  assert.ok(Number.isSafeInteger(result.verified_unix) && result.verified_unix >= startedUnix &&
    result.verified_unix <= now && now - result.verified_unix < 60);
  assert.equal(result.client_source_sha, packet.client.source_sha);
  assert.equal(result.client_archive_sha256, packet.client.archive_sha256);
  assert.match(packet.verifier_id, /^[a-f0-9]{32}$/);
  assert.ok(Number.isSafeInteger(packet.run_id) && packet.run_id > 0);
  assert.ok(Number.isSafeInteger(packet.run_attempt) && packet.run_attempt > 0);
  assert.ok(Array.isArray(packet.artifact_references) && packet.artifact_references.length > 0);
  assert.ok(packet.artifact_references.every(reference => typeof reference === 'string' && reference.length > 0 && reference.length <= 512));
  const repository = 'henryfaulkner/ChefKnifeStudios.PokerAttack';
  return { format: 'pokerattack-evidence-v1', source_sha: manifest.source_sha,
    workflow: { repository, run_id: packet.run_id, run_attempt: packet.run_attempt,
      run_url: `https://github.com/${repository}/actions/runs/${packet.run_id}` },
    digest: manifest.api.digest, migration_digest: manifest.migration.digest, config_sha256: manifest.config_sha256,
    client_source_sha: result.client_source_sha, client_archive_sha256: result.client_archive_sha256,
    verifier_id: packet.verifier_id, verified_unix: result.verified_unix,
    checks: { ...result.checks }, artifact_references: [...packet.artifact_references] };
}

module.exports = { buildEvidence, CHECKS };
