import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gunzipSync, brotliDecompressSync } from 'node:zlib';
import { prepareProduction } from './prepare-production-client.mjs';

function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'pokerattack-production-artifact-'));
  const source = join(root, 'source'), artifacts = join(root, 'artifacts'), output = join(artifacts, 'prepared');
  mkdirSync(source); mkdirSync(artifacts);
  const base = { AppSettings: { ExternalApis: ['PokerAttackSignalR', 'PokerAttackAPI'].map(Name => ({ Name, BaseUri: 'https://old.example/' })) } };
  writeFileSync(join(source, 'appsettings.json'), JSON.stringify(base));
  writeFileSync(join(source, 'appsettings.prod.json'), JSON.stringify({ AppSettings: { ExternalApis: [{ BaseUri: 'https://wrong.example/' }] } }));
  writeFileSync(join(source, 'appsettings.Development.json'), JSON.stringify({ FeatureFlag: false }));
  writeFileSync(join(source, 'staticwebapp.config.json'), JSON.stringify({ globalHeaders: { 'Blazor-Environment': 'Production', 'blazor-environment': 'wrong', 'cache-control': 'max-age=3600', 'X-Test': 'keep' }, navigationFallback: { rewrite: '/index.html' } }));
  writeFileSync(join(source, 'index.html'), '<html>fixture</html>');
  return { root, source, artifacts, output, base };
}

test('rewrites both names and all environment/compressed variants without changing source', () => {
  const f = fixture(), original = readFileSync(join(f.source, 'appsettings.json'));
  prepareProduction(f.source, f.output, f.artifacts);
  assert.deepEqual(readFileSync(join(f.source, 'appsettings.json')), original);
  for (const name of ['appsettings.json', 'appsettings.prod.json', 'appsettings.Development.json', 'staticwebapp.config.json']) {
    const bytes = readFileSync(join(f.output, name));
    assert.deepEqual(gunzipSync(readFileSync(join(f.output, name + '.gz'))), bytes);
    assert.deepEqual(brotliDecompressSync(readFileSync(join(f.output, name + '.br'))), bytes);
    const value = JSON.parse(bytes);
    if (name.startsWith('appsettings')) {
      assert.deepEqual(value.AppSettings.ExternalApis.map(api => api.Name), ['PokerAttackSignalR', 'PokerAttackAPI']);
      assert.ok(value.AppSettings.ExternalApis.every(api => api.BaseUri === 'https://api.playpokerattack.com/'));
    } else {
      assert.equal(value.globalHeaders['Blazor-Environment'], 'prod');
      assert.equal(value.globalHeaders['Cache-Control'], 'no-cache');
      assert.equal(Object.keys(value.globalHeaders).filter(key => key.toLowerCase() === 'blazor-environment').length, 1);
      assert.equal(value.globalHeaders['X-Test'], 'keep');
      assert.equal(value.navigationFallback.rewrite, '/index.html');
    }
  }
});

test('rejects missing, duplicate, unknown or malformed base names before creating output', () => {
  for (const entries of [[], [{ Name: 'PokerAttackAPI' }], [{ Name: 'PokerAttackAPI' }, { Name: 'PokerAttackAPI' }],
    [{ Name: 'PokerAttackAPI' }, { Name: 'Unknown' }], [null, { Name: 'PokerAttackAPI' }]]) {
    const f = fixture(); f.base.AppSettings.ExternalApis = entries;
    writeFileSync(join(f.source, 'appsettings.json'), JSON.stringify(f.base));
    assert.throws(() => prepareProduction(f.source, f.output, f.artifacts));
    assert.equal(existsSync(f.output), false);
  }
});

test('rejects ambiguous environment overrides before creating output', () => {
  for (const overrides of [[{ Name: 'PokerAttackAPI' }], [{}, {}, {}], [null], 'invalid']) {
    const f = fixture();
    writeFileSync(join(f.source, 'appsettings.prod.json'), JSON.stringify({ AppSettings: { ExternalApis: overrides } }));
    assert.throws(() => prepareProduction(f.source, f.output, f.artifacts));
    assert.equal(existsSync(f.output), false);
  }
});

test('rejects existing/overlapping/outside output and symlink escape', () => {
  const f = fixture();
  for (const output of [f.source, join(f.source, 'nested'), f.root, join(f.root, 'outside')])
    assert.throws(() => prepareProduction(f.source, output, f.artifacts));
  mkdirSync(f.output);
  assert.throws(() => prepareProduction(f.source, f.output, f.artifacts));
  const link = join(f.artifacts, 'escape');
  symlinkSync(f.root, link, process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => prepareProduction(f.source, join(link, 'escaped'), f.artifacts));
});

test('rejects symlinked publication content and malformed SWA headers', () => {
  const f = fixture();
  const external = join(f.root, 'external'); mkdirSync(external);
  symlinkSync(external, join(f.source, 'linked'), process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => prepareProduction(f.source, f.output, f.artifacts));
  const bad = fixture();
  writeFileSync(join(bad.source, 'staticwebapp.config.json'), '{"globalHeaders":[]}');
  assert.throws(() => prepareProduction(bad.source, bad.output, bad.artifacts));
  assert.equal(existsSync(bad.output), false);
});
