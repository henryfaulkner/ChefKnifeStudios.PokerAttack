'use strict';
const { chromium } = require('@playwright/test');
const { loadContext } = require('./context.cjs');
const { resolveReleaseTargets } = require('./safety.cjs');

// Run only after the protected job revoked this run's admission on the host.
// It attempts one of this verifier's own IDs, never another player's session.
async function main() {
  let browser;
  let stage = 'context';
  try {
    const packet = loadContext(process.env.POKERATTACK_RELEASE_CONTEXT);
    const targets = resolveReleaseTargets();
    browser = await chromium.launch();
    const context = await browser.newContext({ ignoreHTTPSErrors: false, serviceWorkers: 'block' });
    stage = 'api-readiness';
    const ready = await context.request.get(targets.api + '/health/ready', { maxRedirects: 0, timeout: 10000 });
    if (ready.status() !== 200) throw Error('API unavailable');
    await context.addCookies([{ name: '__Host-PokerAttackVerifier', value: packet.credential,
      url: targets.api, secure: true, httpOnly: true, sameSite: 'None' }]);
    const page = await context.newPage();
    stage = 'client-load';
    await page.goto(targets.client);
    await page.addScriptTag({ path: require.resolve('@microsoft/signalr/dist/browser/signalr.min.js') });
    stage = 'revoked-admission';
    const rejected = await page.evaluate(async ({ api, id }) => {
      const hub = new window.signalR.HubConnectionBuilder().withUrl(api + '/cks-notification?playerId=' + id,
        { transport: window.signalR.HttpTransportType.WebSockets, skipNegotiation: true })
        .configureLogging(window.signalR.LogLevel.None).build();
      // SignalR transport handshake can complete before OnConnectedAsync rejects
      // admission. An awaited self acknowledgment proves whether dispatch was
      // actually admitted; it has no gameplay/broadcast/cleanup side effect.
      try { await hub.start(); await hub.invoke('AcknowledgeDelivery', 'e'.repeat(32)); await hub.stop(); return false; }
      catch { return true; }
    }, { api: targets.api, id: packet.players[0].id });
    if (!rejected) throw Error('Revoked admission unexpectedly succeeded');
    await context.close();
    console.log('PASS: revoked verifier cannot reconnect.');
  } catch {
    console.error('Revoked verifier connection check failed at ' + stage);
    process.exitCode = 1;
  } finally {
    if (browser) {
      try { await browser.close(); } catch { process.exitCode = 1; }
    }
  }
}
if (require.main === module) main();
