'use strict';
const fs = require('node:fs');
const { chromium } = require('@playwright/test');
const { loadContext } = require('./context.cjs');
const { runGameplay } = require('./gameplay.cjs');
const { buildEvidence } = require('./evidence.cjs');

// Dedicated protected entry point. Public output never contains a raw provider
// exception, credential, cookie, private hand or saved browser state.
async function main() {
  let browser;
  let timer, timedOut = false;
  let phase = 'private-context';
  try {
    const packet = loadContext(process.env.POKERATTACK_RELEASE_CONTEXT);
    if (process.env.POKERATTACK_RELEASE_MODE === 'production' && !packet.server_manifest)
      throw Error('Missing trusted server manifest');
    phase = 'browser-launch';
    browser = await chromium.launch();
    timer = setTimeout(() => { timedOut = true; browser.close().catch(() => {}); }, 780000);
    phase = 'gameplay';
    const startedUnix = Math.floor(Date.now() / 1000);
    const result = await runGameplay(browser, packet);
    if (timedOut) throw Error('Verifier execution expired');
    const evidence = packet.server_manifest ? buildEvidence(packet, result, startedUnix) : undefined;
    const filename = process.env.POKERATTACK_RELEASE_RESULT;
    if (!filename) throw Error('Missing private result destination');
    fs.writeFileSync(filename, JSON.stringify({ passed: true, ...result, evidence }) + '\n', { flag: 'wx', mode: 0o600 });
    console.log('PASS: verified release identities, browser WebSockets, multiplayer/solo completion, private-message isolation and owned leave.');
  } catch (error) {
    const safeStage = /^Release verification failed at [a-z-]+; no promotable evidence emitted$/.test(error.message)
      ? error.message : `Release verification failed at ${phase}; no promotable evidence emitted`;
    console.error(safeStage);
    process.exitCode = 1;
  } finally {
    clearTimeout(timer);
    if (browser) {
      try { await browser.close(); }
      catch { console.error('Release verifier browser close failed'); process.exitCode = 1; }
    }
  }
}

if (require.main === module) main();
