'use strict';
// Transitional ROOT operator entry only. Routine CI uses run.cjs and its scoped
// verifier; this probes the saved capability-0 server after exact-pair recovery.
// Every allocation belongs to this invocation. No global cleanup or test route.
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const { chromium } = require('@playwright/test');
const { loadContext } = require('./context.cjs');
const { resolveReleaseTargets, assertRequest } = require('./safety.cjs');
const { assertPrivateIsolation } = require('./isolation.cjs');
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

async function verify(browser, packet) {
  const targets = resolveReleaseTargets(process.env);
  assert.equal(packet.format, 'pokerattack-legacy-bootstrap-context-v1');
  assert.equal(packet.client.format, 'pokerattack-bootstrap-artifact-v1');
  assert.equal(packet.client.mode, 'legacy-recovery');
  assert.match(packet.source_sha, /^[a-f0-9]{40}$/);
  assert.match(packet.digest, /^sha256:[a-f0-9]{64}$/);
  assert.match(packet.config_sha256, /^[a-f0-9]{64}$/);
  const players = Array.from({ length: 3 }, (_, i) => ({ id: crypto.randomUUID(), name: 'Legacy-Recovery-' + i + '-' + crypto.randomBytes(4).toString('hex') }));
  const contexts = [], pages = [];
  let stage = 'legacy-served-bytes', gameId, lobbyId, completed = false, retainedSolo = false;
  const checks = {};
  try {
    for (const origin of targets.clients) {
      const context = await browser.newContext({ ignoreHTTPSErrors: false, serviceWorkers: 'block' });
      contexts.push(context);
      const names = Object.keys(packet.client.files).filter(name =>
        name === 'index.html' || /^appsettings(?:\.[A-Za-z0-9_.-]+)?\.json$/.test(name) ||
        name === '_framework/blazor.webassembly.js' || /^_framework\/ChefKnife[^/]+\.wasm$/.test(name));
      assert.ok(names.length >= 6);
      for (const name of names) {
        const url = origin + '/' + name; assertRequest(targets, url);
        const response = await context.request.get(url, { maxRedirects: 0, timeout: 15000, headers: { 'Cache-Control': 'no-cache', 'Accept-Encoding': 'identity' } });
        assert.equal(response.status(), 200);
        assert.equal(response.headers()['blazor-environment'], 'prod');
        assert.equal(sha(await response.body()), packet.client.files[name]);
      }
    }
    checks.https = true;
    stage = 'legacy-browser-signalr';
    for (let i = 0; i < 2; i++) {
      const context = await browser.newContext({ ignoreHTTPSErrors: false, serviceWorkers: 'block' }); contexts.push(context);
      await context.route('**/*', async route => {
        try { assertRequest(targets, route.request().url(), route.request().method()); }
        catch { return route.abort(); }
        return route.continue();
      });
      const page = await context.newPage(); pages.push(page);
      await page.addInitScript(() => localStorage.setItem('HasSeenTour', 'true'));
      await page.goto(targets.client, { waitUntil: 'domcontentloaded' });
      await page.locator('#createLobbyBtn').waitFor({ state: 'visible', timeout: 45000 });
      await page.addScriptTag({ path: require.resolve('@microsoft/signalr/dist/browser/signalr.min.js') });
      await page.evaluate(async ({ api, player }) => {
        const state = { hands: 0, cards: 0, runs: 0, rounds: 0, finished: false, hand: [], fences: [] };
        const hub = new window.signalR.HubConnectionBuilder().withUrl(api + '/cks-notification?playerId=' + player.id,
          { transport: window.signalR.HttpTransportType.WebSockets, skipNegotiation: true }).configureLogging(window.signalR.LogLevel.None).build();
        hub.on('ReceivePokerAttackNotification', message => {
          const kind = message.notificationType;
          let payload;
          // RoundEnded has an empty payload in capability-0. Its lifecycle
          // event must still count; only card/fence messages need decoded data.
          try { payload = JSON.parse(message.payload); } catch { payload = undefined; }
          if (payload?.legacyFence) state.fences.push(payload.legacyFence);
          if ([8, 'RunStarted'].includes(kind)) { state.runs++; state.hand = payload.Cards ?? payload.cards; }
          if ([9, 'CardsDealt'].includes(kind)) { state.cards++; state.hand = payload; }
          if ([10, 'HandPlayed'].includes(kind)) state.hands++;
          if ([12, 'RoundEnded'].includes(kind)) state.rounds++;
          if ([15, 16, 'GameLost', 'GameWon'].includes(kind)) state.finished = true;
        });
        window.__legacy = { state, hub, player };
        await hub.start();
        if (hub.state !== window.signalR.HubConnectionState.Connected) throw Error('Not connected');
      }, { api: targets.api, player: players[i] });
    }
    checks.signalr = true;
    async function call(route, method, body, allowGone = false) {
      const url = targets.api + route; assertRequest(targets, url, method);
      return pages[0].evaluate(async ({ url, method, body, allowGone }) => {
        const response = await fetch(url, { method, redirect: 'error', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body), signal: AbortSignal.timeout(15000) });
        if (allowGone && response.status === 404) return null;
        if (!response.ok) throw Error('Owned operation failed');
        const result = await response.json();
        if (!result.isSuccess && !allowGone) throw Error('Owned result failed');
        return result.value;
      }, { url, method, body, allowGone });
    }
    stage = 'legacy-multiplayer';
    lobbyId = (await call('/lobby/create-lobby', 'POST', { hostPlayer: players[0] })).id;
    assert.match(lobbyId, /^[A-Z0-9]{6}$/);
    await call('/lobby/add-player', 'PATCH', { lobbyId, player: players[1] });
    gameId = await call('/lobby/start-game', 'POST', { lobbyId, gameMode: 0 });
    assert.match(gameId, /^[a-f0-9-]{36}$/);
    await Promise.all(pages.map(page => page.evaluate(id => window.__legacy.hub.invoke('JoinGameGroupAsync', id), gameId)));
    await Promise.all(pages.map(page => page.waitForFunction(() => window.__legacy.state.hand.length === 8, undefined, { timeout: 90000 })));
    const snapshot = () => Promise.all(pages.map(page => page.evaluate(() => ({ hands: window.__legacy.state.hands, cards: window.__legacy.state.cards }))));
    async function play(actor) {
      await pages[actor].evaluate(async () => {
        const { hub, state, player } = window.__legacy; const card = state.hand[0];
        const suit = card.Suit ?? card.suit, rank = card.Rank ?? card.rank;
        await hub.invoke('PlayHand', player.id, [{
          suit: typeof suit === 'number' ? suit : ['spade', 'club', 'diamond', 'heart'].indexOf(suit),
          rank: typeof rank === 'number' ? rank : ['two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'jack', 'queen', 'king', 'ace'].indexOf(rank) + 2,
          isFaceDown: card.IsFaceDown ?? card.isFaceDown }]);
      });
    }
    stage = 'legacy-private-isolation';
    for (let actor = 0; actor < 2; actor++) {
      const before = await snapshot(); await play(actor);
      const nonce = crypto.randomBytes(16).toString('hex');
      // Capability-0 lacks the caller-only fence. Its established operator
      // protocol uses a subsequent marker ONLY in this invocation's owned game
      // group. Both receiving connections must observe it after PlayHand returns.
      await pages[0].evaluate(({ id, nonce }) => window.__legacy.hub.invoke('BroadcastGameNotification', id,
        { notificationType: 0, payload: JSON.stringify({ legacyFence: nonce }) }), { id: gameId, nonce });
      await Promise.all(pages.map(page => page.waitForFunction(nonce => window.__legacy.state.fences.includes(nonce), nonce, { timeout: 15000 })));
      assertPrivateIsolation(before, await snapshot(), actor, [true, true]);
    }
    checks.private_message_isolation = true;
    stage = 'legacy-multiplayer-completion';
    let known = 1;
    for (let round = 0; round < 8; round++) {
      await pages[0].waitForFunction(known => window.__legacy.state.finished || window.__legacy.state.runs > known, known, { timeout: 150000 });
      const state = await pages[0].evaluate(() => ({ finished: window.__legacy.state.finished, runs: window.__legacy.state.runs }));
      if (state.finished) break;
      known = state.runs; await play(0);
    }
    await Promise.all(pages.map(page => page.waitForFunction(() => window.__legacy.state.finished && window.__legacy.state.rounds >= 1, undefined, { timeout: 600000 })));
    checks.multiplayer = true; completed = true;
    stage = 'legacy-solo';
    const solo = await call('/solo/start-game', 'POST', { playerName: players[2].name });
    assert.equal(solo.cards.length, 8);
    const hand = await call('/solo/play-hand/' + solo.gameId, 'POST', { playerId: solo.playerId, hand: [solo.cards[0]] });
    assert.equal(hand.handsRemaining, solo.handsAvailable - 1);
    assert.equal((await call('/solo/advance-phase/' + solo.gameId, 'POST', {})).newPhase, 'Scoreboard');
    assert.equal((await call('/solo/advance-phase/' + solo.gameId, 'POST', {})).newPhase, 'GameOver');
    checks.solo = true; retainedSolo = true;
    stage = 'legacy-owned-leave';
    try { await pages[0].evaluate(id => window.__legacy.hub.invoke('EndGame', id), gameId); }
    catch { assert.equal(completed, true); } // Terminal games may already be removed normally.
    gameId = undefined;
    for (const player of players.slice(0, 2)) await call('/lobby/remove-player', 'POST', { lobbyId, player }, true);
    lobbyId = undefined;
    return { format: 'pokerattack-legacy-bootstrap-verification-v1', source_sha: packet.source_sha,
      digest: packet.digest, config_sha256: packet.config_sha256, client_archive_sha256: packet.client.archive_sha256,
      ...checks, verified_unix: Math.floor(Date.now() / 1000),
      retained_completed_solo: retainedSolo, cleanup_limitation: 'Legacy has no caller-owned solo leave; completed solo remains until the next separately acknowledged reset.' };
  } catch { throw Error('Legacy verification failed at ' + stage); }
  finally {
    if (gameId && pages[0]) {
      try { await pages[0].evaluate(id => window.__legacy.hub.invoke('EndGame', id), gameId); }
      catch { if (!completed) throw Error('Legacy owned leave failed; retain maintenance'); }
    }
    await Promise.allSettled(pages.map(page => page.evaluate(() => window.__legacy?.hub.stop())));
    for (const context of contexts) await context.close();
  }
}

async function main() {
  let browser, timer;
  try {
    const packet = loadContext(process.env.POKERATTACK_LEGACY_CONTEXT);
    browser = await chromium.launch();
    timer = setTimeout(() => browser.close().catch(() => {}), 780000);
    const result = await verify(browser, packet);
    fs.writeFileSync(process.env.POKERATTACK_LEGACY_RESULT, JSON.stringify(result) + '\n', { flag: 'wx', mode: 0o600 });
    console.log('PASS: legacy recovery verified with owned gameplay; completed solo retained explicitly.');
  } catch (error) {
    // Emit only our fixed stage identifier, never browser/provider exceptions.
    const safe = /^Legacy verification failed at (legacy-[a-z-]+)$/.exec(error.message);
    console.error(safe ? 'Legacy bootstrap failed at ' + safe[1] + '; no proof emitted.' :
      'Legacy bootstrap verification failed before staged checks; no proof emitted.');
    process.exitCode = 1;
  }
  finally { clearTimeout(timer); if (browser) await browser.close(); }
}

if (require.main === module) main();
