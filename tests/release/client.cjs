'use strict';
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const { chromium } = require('@playwright/test');
const { resolveReleaseTargets, assertRequest } = require('./safety.cjs');

function digest(bytes) { return crypto.createHash('sha256').update(bytes).digest('hex'); }
function validate(manifest) {
  assert.equal(manifest.format, 'pokerattack-client-v1');
  assert.match(manifest.source_sha, /^[a-f0-9]{40}$/);
  assert.match(manifest.archive_sha256, /^[a-f0-9]{64}$/);
  assert.equal(manifest.workflow.repository, 'henryfaulkner/ChefKnifeStudios.PokerAttack');
  assert.ok(Number.isSafeInteger(manifest.workflow.run_id) && manifest.workflow.run_id > 0);
  assert.ok(Number.isSafeInteger(manifest.workflow.run_attempt) && manifest.workflow.run_attempt > 0);
  assert.ok(Object.keys(manifest.files).length <= 100000);
  assert.equal(manifest.files['appsettings.json'], manifest.settings_sha256);
  for (const name of ['index.html', 'release.json', 'appsettings.json']) assert.match(manifest.files[name], /^[a-f0-9]{64}$/);
}

async function verifyClient(browser, manifest, env = process.env) {
  const targets = resolveReleaseTargets(env);
  validate(manifest);
  let stage = 'https';
  try {
    for (const origin of targets.clients) {
      const context = await browser.newContext({ ignoreHTTPSErrors: false, serviceWorkers: 'block' });
      try {
        async function get(name) {
          const url = origin + '/' + name;
          assertRequest(targets, url);
          const response = await context.request.get(url, { maxRedirects: 0, timeout: 10000 });
          assert.equal(response.status(), 200);
          assert.equal(response.headers()['blazor-environment'], 'prod');
          const bytes = await response.body();
          assert.equal(digest(bytes), manifest.files[name]);
          return bytes;
        }
        stage = 'served-identity';
        const release = JSON.parse((await get('release.json')).toString('utf8'));
        assert.deepEqual(release, { format: 'pokerattack-client-public-v1', source_sha: manifest.source_sha,
          run_id: manifest.workflow.run_id, run_attempt: manifest.workflow.run_attempt,
          minimum_server_capability: manifest.minimum_server_capability });
        stage = 'settings';
        for (const name of Object.keys(manifest.files).filter(name => /^appsettings(?:\.[A-Za-z0-9_.-]+)?\.json$/.test(name))) {
          const settings = JSON.parse((await get(name)).toString('utf8'));
          const apis = settings.AppSettings?.ExternalApis;
          if (apis !== undefined || name === 'appsettings.json') {
            assert.equal(apis.length, 2);
            assert.deepEqual(apis.map(api => api.Name).sort(), ['PokerAttackAPI', 'PokerAttackSignalR']);
            assert.ok(apis.every(api => api.BaseUri.replace(/\/$/, '') === targets.api));
          }
        }
        stage = 'assets';
        await get('index.html');
        stage = 'browser';
        await context.route('**/*', async route => {
          try { assertRequest(targets, route.request().url(), route.request().method()); }
          catch { return route.abort(); }
          return route.continue();
        });
        const page = await context.newPage();
        const responses = [];
        let invalidAsset = false, assets = 0, wasm = 0;
        const invalidNames = [];
        page.on('response', response => {
          const url = new URL(response.url());
          if (url.origin !== origin || response.status() !== 200) return;
          const name = decodeURIComponent(url.pathname).slice(1) || 'index.html';
          const expected = manifest.files[name];
          if (typeof expected !== 'string') return;
          responses.push(response.body().then(async bytes => {
            let matches = digest(bytes) === expected;
            // Chromium's decoded CSS/JS resource body can omit a UTF-8 BOM.
            // Accept only that precise representation difference, and still
            // require a raw HTTPS read to match the original archived bytes.
            if (!matches && /\.(css|js)$/.test(name) && digest(Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), bytes])) === expected) {
              const url = origin + '/' + name;
              assertRequest(targets, url);
              const raw = await context.request.get(url, { maxRedirects: 0, timeout: 10000, headers: { 'Cache-Control': 'no-cache' } });
              matches = raw.status() === 200 && digest(await raw.body()) === expected;
            }
            if (!matches) { invalidAsset = true; invalidNames.push(name); }
            else { assets++; if (name.endsWith('.wasm')) wasm++; }
          }).catch(() => { invalidAsset = true; invalidNames.push(name + ' (body unavailable)'); }));
        });
        await page.addInitScript(() => localStorage.setItem('HasSeenTour', 'true'));
        await page.goto(origin, { waitUntil: 'domcontentloaded', timeout: 45000 });
        await page.locator('#createLobbyBtn').waitFor({ state: 'visible', timeout: 45000 });
        await page.waitForFunction(() => {
          const node = document.querySelector('.session-content');
          return node && !node.hasAttribute('inert');
        }, undefined, { timeout: 30000 });
        stage = 'signalr';
        await page.addScriptTag({ path: require.resolve('@microsoft/signalr/dist/browser/signalr.min.js') });
        const nonce = crypto.randomBytes(16).toString('hex');
        const connected = await page.evaluate(async ({ api, id, nonce }) => {
          const hub = new window.signalR.HubConnectionBuilder().withUrl(api + '/cks-notification?playerId=' + id,
            { transport: window.signalR.HttpTransportType.WebSockets, skipNegotiation: true })
            .configureLogging(window.signalR.LogLevel.None).build();
          let acknowledged = false;
          hub.on('DeliveryAcknowledged', received => { if (received === nonce) acknowledged = true; });
          try {
            await hub.start();
            await hub.invoke('AcknowledgeDelivery', nonce);
            return acknowledged;
          } finally { await hub.stop(); }
        }, { api: targets.api, id: crypto.randomUUID(), nonce });
        assert.equal(connected, true);
        stage = 'browser-asset-identity';
        await Promise.all(responses);
        if (env.POKERATTACK_RELEASE_MODE === 'isolated' && env.POKERATTACK_FIXTURE_DIAGNOSTICS === 'true')
          console.log(JSON.stringify({ static_assets_checked: assets, wasm_assets_checked: wasm, invalid_asset: invalidAsset, invalid_names: invalidNames }));
        stage = 'browser-asset-checksum';
        assert.equal(invalidAsset, false);
        stage = 'browser-asset-count';
        assert.ok(assets >= 2);
        stage = 'browser-wasm';
        assert.ok(wasm >= 1, 'Loaded application WASM must match the candidate');
      } finally { await context.close(); }
    }
    return { checks: Object.fromEntries(['https', 'served_client', 'settings', 'header', 'assets', 'browser', 'signalr'].map(key => [key, true])),
      verified_unix: Math.floor(Date.now() / 1000), client_source_sha: manifest.source_sha,
      client_archive_sha256: manifest.archive_sha256 };
  } catch {
    throw Error('Client verification failed at ' + stage);
  }
}

async function main() {
  let browser, timer, expired = false;
  try {
    const filename = process.env.POKERATTACK_CLIENT_MANIFEST;
    const info = fs.lstatSync(filename);
    assert.ok(info.isFile() && !info.isSymbolicLink() && info.size <= 32 * 1024 * 1024);
    const manifest = JSON.parse(fs.readFileSync(filename, 'utf8'));
    browser = await chromium.launch();
    timer = setTimeout(() => { expired = true; browser.close().catch(() => {}); }, 600000);
    let result;
    // Retry only reads/fresh browser connections. This code never uploads,
    // restores, creates a lobby/game or changes a host lease.
    for (let attempt = 0; attempt < 3; attempt++) {
      try { result = await verifyClient(browser, manifest); break; }
      catch (error) {
        if (expired || attempt === 2) throw error;
        await new Promise(resolve => setTimeout(resolve, 5000));
      }
    }
    if (expired) throw Error('Client verification expired');
    await browser.close(); browser = undefined; clearTimeout(timer);
    fs.writeFileSync(process.env.POKERATTACK_CLIENT_RESULT, JSON.stringify(result) + '\n', { flag: 'wx', mode: 0o600 });
    console.log('PASS: all client origins, served bytes/assets/settings/header, fresh browsers and actual SignalR acknowledgment.');
  } catch (error) {
    console.error(/^Client verification failed at [a-z-]+$/.test(error.message) ? error.message : 'Client verification failed; no accepted result emitted');
    process.exitCode = 1;
  } finally {
    clearTimeout(timer);
    if (browser) { try { await browser.close(); } catch { process.exitCode = 1; } }
  }
}

module.exports = { verifyClient, validate };
if (require.main === module) main();
