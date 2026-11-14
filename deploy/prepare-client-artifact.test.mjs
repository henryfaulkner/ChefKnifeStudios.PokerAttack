import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gunzipSync, brotliDecompressSync } from 'node:zlib';
import { preparePilot } from './prepare-client-artifact.mjs';

// Leave tiny private temp fixtures for diagnosis; no recursive deletion needed.
function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'pokerattack-artifact-test-'));
  const source = join(root, 'source');
  mkdirSync(source);
  const original = JSON.stringify({ AppSettings: { ExternalApis: ['PokerAttackSignalR', 'PokerAttackAPI'].map(Name => ({
    Name, BaseUri: 'https://old-production.example/',
  })) } });
  writeFileSync(join(source, 'appsettings.json'), original);
  writeFileSync(join(source, 'appsettings.prod.json'), JSON.stringify({ AppSettings: { ExternalApis: [{ BaseUri: 'https://override.example/' }] } }));
  writeFileSync(join(source, 'index.html'), '<html>fixture</html>');
  return { source, output: join(root, 'pilot'), original };
}

test('rewrites both APIs and environment overrides without changing source, including compressed files', () => {
  const { source, output, original } = fixture();
  preparePilot(source, output, 'https://pilot-api.playpokerattack.com');
  assert.equal(readFileSync(join(source, 'appsettings.json'), 'utf8'), original);
  for (const file of ['appsettings.json', 'appsettings.prod.json']) {
    const bytes = readFileSync(join(output, file));
    for (const api of JSON.parse(bytes).AppSettings.ExternalApis)
      assert.equal(api.BaseUri, 'https://pilot-api.playpokerattack.com/');
    assert.deepEqual(gunzipSync(readFileSync(join(output, `${file}.gz`))), bytes);
    assert.deepEqual(brotliDecompressSync(readFileSync(join(output, `${file}.br`))), bytes);
  }
  assert.equal(JSON.parse(readFileSync(join(output, 'staticwebapp.config.json'))).globalHeaders['Blazor-Environment'], 'Pilot');
});
test('rejects production API and ambiguous output paths', () => {
  const { source, output } = fixture();
  assert.throws(() => preparePilot(source, output, 'https://api.playpokerattack.com'), /Pilot API/);
  assert.throws(() => preparePilot(source, source, 'http://localhost:8082'), /new directory/);
  assert.throws(() => preparePilot(source, join(source, 'nested'), 'http://localhost:8082'), /outside/);
  mkdirSync(output);
  assert.throws(() => preparePilot(source, output, 'http://localhost:8082'), /new directory/);
});
