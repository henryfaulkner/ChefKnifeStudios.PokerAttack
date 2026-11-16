const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { resolveReleaseTargets, assertRequest, CLIENT_ORIGINS, API_ORIGIN } = require('./safety.cjs');

test('production has only the intended exact HTTPS origins', () => {
  const targets = resolveReleaseTargets({ POKERATTACK_RELEASE_MODE: 'production' });
  assert.deepEqual(targets.clients, CLIENT_ORIGINS);
  assert.equal(targets.api, API_ORIGIN);
  for (const candidate of ['https://example.com', 'http://api.playpokerattack.com',
    'https://api.playpokerattack.com.evil.example', API_ORIGIN + '/test', API_ORIGIN + '?x=1',
    'https://user:password@api.playpokerattack.com', API_ORIGIN + ':443']) {
    assert.throws(() => resolveReleaseTargets({ POKERATTACK_RELEASE_MODE: 'production', RELEASE_API_ORIGIN: candidate }));
  }
});

test('isolated mode cannot target production or arbitrary networks', () => {
  const env = { POKERATTACK_RELEASE_MODE: 'isolated', RELEASE_CLIENT_ORIGIN: 'https://localhost:5173',
    RELEASE_API_ORIGIN: 'https://localhost:8081' };
  assert.equal(resolveReleaseTargets(env).mode, 'isolated');
  for (const candidate of [API_ORIGIN, 'http://localhost:8081', 'https://localhost', 'https://localhost:443',
    'https://127.0.0.1:8081', 'https://192.168.1.1:8081'])
    assert.throws(() => resolveReleaseTargets({ ...env, RELEASE_API_ORIGIN: candidate }));
  assert.throws(() => resolveReleaseTargets({}));
});

test('TLS bypass settings and destructive/operator requests fail closed', () => {
  const env = { POKERATTACK_RELEASE_MODE: 'production' };
  for (const bypass of [{ NODE_TLS_REJECT_UNAUTHORIZED: '0' }, { IGNORE_HTTPS_ERRORS: 'true' },
    { PLAYWRIGHT_CHROMIUM_ARGS: '--ignore-certificate-errors' }, { PLAYWRIGHT_CHROMIUM_ARGS: '--allow-insecure-localhost' }])
    assert.throws(() => resolveReleaseTargets({ ...env, ...bypass }));
  const targets = resolveReleaseTargets(env);
  for (const route of ['/test/cleanup', '/TEST/signal-r', '/%74est/cleanup', '/_ops/status', '/game/cleanup'])
    assert.throws(() => assertRequest(targets, API_ORIGIN + route, 'POST'));
  assert.throws(() => assertRequest(targets, 'https://example.com/'));
  assert.throws(() => assertRequest(targets, CLIENT_ORIGINS[0], 'POST'));
  assert.doesNotThrow(() => assertRequest(targets, API_ORIGIN + '/lobby/create-lobby', 'POST'));
});

test('protocol debug and trace environment cannot expose verifier credentials', () => {
  for (const key of ['DEBUG', 'PWDEBUG', 'NODE_DEBUG', 'PLAYWRIGHT_TRACE_DIR'])
    assert.throws(() => resolveReleaseTargets({ POKERATTACK_RELEASE_MODE: 'production', [key]: 'enabled' }));
});

test('release source stays independent of destructive helpers and sensitive captures', () => {
  const directory = __dirname;
  for (const name of fs.readdirSync(directory).filter(name => /\.(ts|cjs)$/.test(name) && !name.endsWith('.test.cjs'))) {
    const source = fs.readFileSync(path.join(directory, name), 'utf8');
    assert.doesNotMatch(source, /(?:from\s*|require\s*\()\s*['"][^'"]*(?:helpers\/setup|helpers\/actions|e2e\/)/);
    assert.doesNotMatch(source, /ignoreHTTPSErrors\s*:\s*true|recordHar\s*:|\.tracing\.start\(/);
  }
  const config = fs.readFileSync(path.join(directory, '../playwright.release.config.ts'), 'utf8');
  assert.match(config, /ignoreHTTPSErrors:\s*false/);
  for (const feature of ['trace', 'screenshot', 'video']) assert.match(config, new RegExp(feature + ": 'off'"));
});
