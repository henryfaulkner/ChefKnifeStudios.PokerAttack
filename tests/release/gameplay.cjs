'use strict';
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const { resolveReleaseTargets, assertRequest } = require('./safety.cjs');
const { assertPrivateIsolation } = require('./isolation.cjs');
const { recoverOwnedSessions } = require('./owned.cjs');

function requireIdentity(value, pattern) {
  assert.equal(typeof value, 'string');
  assert.match(value, pattern);
}

async function runGameplay(browser, packet, env = process.env) {
  const targets = resolveReleaseTargets(env);
  const contexts = [], pages = [];
  let stage = 'context-validation', lobbyId, gameId, solo, runCleanup, completed = false;
  const checks = Object.create(null);
  const ownedPlayers = packet.players;
  const uuid = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;
  let failure;
  try {
    requireIdentity(packet.credential, /^[1-9][0-9]{0,19}\.[1-9][0-9]{0,4}\.[a-f0-9]{64}$/);
    requireIdentity(packet.verifier_id, /^[a-f0-9]{32}$/);
    assert.equal(ownedPlayers.length, 3);
    for (const [index, player] of ownedPlayers.entries()) {
      requireIdentity(player.id, uuid);
      assert.equal(player.name, `Release-${packet.run_id}-${packet.run_attempt}-${index}`);
    }
    assert.equal(new Set(ownedPlayers.map(player => player.id)).size, 3);
    assert.equal(packet.credential.split('.').slice(0, 2).join('.'), `${packet.run_id}.${packet.run_attempt}`);
    requireIdentity(packet.client.source_sha, /^[a-f0-9]{40}$/);
    requireIdentity(packet.client.archive_sha256, /^[a-f0-9]{64}$/);
    requireIdentity(packet.client.settings_sha256, /^[a-f0-9]{64}$/);
    stage = 'tls-client-identity';
    const readContext = await browser.newContext({ ignoreHTTPSErrors: false, serviceWorkers: 'block' });
    contexts.push(readContext);
    async function get(url) {
      assertRequest(targets, url);
      const response = await readContext.request.get(url, { maxRedirects: 0, timeout: 15000 });
      assert.equal(response.status(), 200);
      return response;
    }
    for (const client of targets.clients) {
      stage = 'served-release-identity';
      const release = await (await get(client + '/release.json')).json();
      assert.deepEqual(release, { format: 'pokerattack-client-public-v1', source_sha: packet.client.source_sha,
        run_id: packet.client.workflow.run_id, run_attempt: packet.client.workflow.run_attempt,
        minimum_server_capability: packet.client.minimum_server_capability });
      stage = 'served-client-settings';
      const response = await get(client + '/appsettings.json');
      assert.equal(response.headers()['blazor-environment'], 'prod');
      const raw = await response.body();
      assert.equal(crypto.createHash('sha256').update(raw).digest('hex'), packet.client.settings_sha256);
      const settings = JSON.parse(raw.toString('utf8'));
      const apis = settings.AppSettings.ExternalApis;
      assert.equal(apis.length, 2);
      assert.deepEqual(apis.map(api => api.Name).sort(), ['PokerAttackAPI', 'PokerAttackSignalR']);
      assert.ok(apis.every(api => api.BaseUri.replace(/\/$/, '') === targets.api));
      assert.equal(packet.client.settings_files['appsettings.json'], packet.client.settings_sha256);
      for (const [name, hash] of Object.entries(packet.client.settings_files)) {
        assert.match(name, /^appsettings(?:\.[A-Za-z0-9_.-]+)?\.json$/);
        requireIdentity(hash, /^[a-f0-9]{64}$/);
        const variant = await get(client + '/' + name);
        const bytes = await variant.body();
        assert.equal(crypto.createHash('sha256').update(bytes).digest('hex'), hash);
        const overrides = JSON.parse(bytes.toString('utf8')).AppSettings?.ExternalApis;
        if (overrides) {
          assert.equal(overrides.length, 2);
          assert.deepEqual(overrides.map(api => api.Name).sort(), ['PokerAttackAPI', 'PokerAttackSignalR']);
          assert.ok(overrides.every(api => api.BaseUri.replace(/\/$/, '') === targets.api));
        }
      }
      stage = 'served-client-assets';
      const html = await (await get(client + '/')).text();
      assert.match(html, /blazor\.webassembly\.js/);
      const script = await get(client + '/_framework/blazor.webassembly.js');
      assert.ok((await script.body()).length > 1000);
    }
    stage = 'public-api-readiness';
    await get(targets.api + '/health/ready');
    checks.https = true;
    checks.served_client = true;
    stage = 'browser-signalr';
    for (let index = 0; index < 2; index++) {
      const context = await browser.newContext({ ignoreHTTPSErrors: false, serviceWorkers: 'block' });
      contexts.push(context);
      // Host-only, Secure/HttpOnly; never inject this value into page JS, a URL,
      // an app setting, console, capture, screenshot or evidence output.
      await context.addCookies([{ name: '__Host-PokerAttackVerifier', value: packet.credential,
        url: targets.api, secure: true, httpOnly: true, sameSite: 'None' }]);
      await context.route('**/*', async route => {
        try { assertRequest(targets, route.request().url(), route.request().method()); }
        catch { return route.abort(); }
        return route.continue();
      });
      const page = await context.newPage();
      pages.push(page);
      await page.addInitScript(() => localStorage.setItem('HasSeenTour', 'true'));
      await page.goto(targets.client, { waitUntil: 'domcontentloaded' });
      await page.locator('#createLobbyBtn').waitFor({ state: 'visible', timeout: 45000 });
      await page.addScriptTag({ path: require.resolve('@microsoft/signalr/dist/browser/signalr.min.js') });
      await page.evaluate(async ({ api, player }) => {
        const state = { hands: 0, cards: 0, rounds: 0, runs: 0, finished: false, hand: [], acks: [] };
        const hub = new window.signalR.HubConnectionBuilder().withUrl(api + '/cks-notification?playerId=' + player.id,
          { transport: window.signalR.HttpTransportType.WebSockets, skipNegotiation: true })
          .configureLogging(window.signalR.LogLevel.None).build();
        hub.on('DeliveryAcknowledged', nonce => state.acks.push(nonce));
        hub.on('ReceivePokerAttackNotification', message => {
          const kind = message.notificationType;
          if ([8, 'RunStarted'].includes(kind)) {
            state.runs++;
            const payload = JSON.parse(message.payload);
            state.hand = payload.Cards ?? payload.cards;
          }
          if ([9, 'CardsDealt'].includes(kind)) { state.cards++; state.hand = JSON.parse(message.payload); }
          if ([10, 'HandPlayed'].includes(kind)) state.hands++;
          if ([12, 'RoundEnded'].includes(kind)) state.rounds++;
          if ([15, 16, 'GameLost', 'GameWon'].includes(kind)) state.finished = true;
        });
        window.__release = { state, hub, player, api };
        await hub.start();
        if (hub.state !== window.signalR.HubConnectionState.Connected) throw Error('SignalR not connected');
      }, { api: targets.api, player: ownedPlayers[index] });
    }
    checks.signalr = true;
    const call = async (route, method = 'GET', body) => {
      const url = targets.api + route;
      assertRequest(targets, url, method);
      return pages[0].evaluate(async ({ url, method, body }) => {
        const response = await fetch(url, { method, credentials: 'include', redirect: 'error',
          headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
          body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(15000) });
        if (!response.ok) throw Error('Release operation rejected');
        const result = await response.json();
        if (result.isSuccess !== true) throw Error('Release operation failed');
        return result.value;
      }, { url, method, body });
    };
    // This closure remains available to finally only after browser admission.
    runCleanup = async () => {
      await recoverOwnedSessions(call, id => pages[0].evaluate(game => window.__release.hub.invoke('EndGame', game), id));
    };
    stage = 'previous-attempt-owned-session-leave';
    await runCleanup();
    stage = 'lobby-create';
    const lobby = await call('/lobby/create-lobby', 'POST', { hostPlayer: ownedPlayers[0] });
    requireIdentity(lobby.id, /^[A-Z0-9]{6}$/);
    lobbyId = lobby.id;
    stage = 'lobby-join';
    await call('/lobby/add-player', 'PATCH', { lobbyId, player: ownedPlayers[1] });
    stage = 'game-start';
    gameId = await call('/lobby/start-game', 'POST', { lobbyId, gameMode: 0 });
    requireIdentity(gameId, uuid);
    stage = 'game-groups';
    await Promise.all(pages.map(page => page.evaluate(id => window.__release.hub.invoke('JoinGameGroupAsync', id), gameId)));
    stage = 'initial-private-hand';
    await Promise.all(pages.map(page => page.waitForFunction(() => window.__release.state.hand.length === 8, undefined, { timeout: 90000 })));
    const snapshot = () => Promise.all(pages.map(page => page.evaluate(() => ({ hands: window.__release.state.hands,
      cards: window.__release.state.cards }))));
    stage = 'private-message-isolation';
    for (let actor = 0; actor < 2; actor++) {
      const before = await snapshot();
      await pages[actor].evaluate(async () => {
        const { hub, state, player } = window.__release;
        const card = state.hand[0];
        const suit = card.Suit ?? card.suit, rank = card.Rank ?? card.rank;
        const typed = { suit: typeof suit === 'number' ? suit : ['spade','club','diamond','heart'].indexOf(suit),
          rank: typeof rank === 'number' ? rank : ['two','three','four','five','six','seven','eight','nine','ten','jack','queen','king','ace'].indexOf(rank) + 2,
          isFaceDown: card.IsFaceDown ?? card.isFaceDown };
        await hub.invoke('PlayHand', player.id, [typed]);
      });
      // Await the actual operation before sending each receiving connection its
      // own subsequent ordered message. No arbitrary group broadcast or sleeps.
      const nonce = crypto.randomBytes(16).toString('hex');
      await Promise.all(pages.map(page => page.evaluate(nonce => window.__release.hub.invoke('AcknowledgeDelivery', nonce), nonce)));
      await Promise.all(pages.map(page => page.waitForFunction(nonce => window.__release.state.acks.includes(nonce), nonce, { timeout: 15000 })));
      assertPrivateIsolation(before, await snapshot(), actor, [true, true]);
    }
    checks.private_message_isolation = true;
    stage = 'multiplayer-completion';
    // Scores reset each round. Continue normal play for one verifier so later
    // rounds do not tie at zero forever; do not change timers/state on the host.
    let playedRun = 1;
    for (let round = 0; round < 8; round++) {
      await pages[0].waitForFunction(known => window.__release.state.finished || window.__release.state.runs > known,
        playedRun, { timeout: 150000 });
      const state = await pages[0].evaluate(() => ({ finished: window.__release.state.finished, runs: window.__release.state.runs }));
      if (state.finished) break;
      playedRun = state.runs;
      await pages[0].evaluate(async () => {
        const { hub, player, state } = window.__release;
        const card = state.hand[0];
        const suit = card.Suit ?? card.suit, rank = card.Rank ?? card.rank;
        await hub.invoke('PlayHand', player.id, [{
          suit: typeof suit === 'number' ? suit : ['spade','club','diamond','heart'].indexOf(suit),
          rank: typeof rank === 'number' ? rank : ['two','three','four','five','six','seven','eight','nine','ten','jack','queen','king','ace'].indexOf(rank) + 2,
          isFaceDown: card.IsFaceDown ?? card.isFaceDown }]);
      });
    }
    await Promise.all(pages.map(page => page.waitForFunction(() => window.__release.state.finished && window.__release.state.rounds >= 1,
      undefined, { timeout: 600000 })));
    completed = true;
    checks.multiplayer = true;
    stage = 'solo-completion';
    solo = await call('/solo/start-game', 'POST', { playerName: ownedPlayers[2].name });
    requireIdentity(solo.gameId, uuid); requireIdentity(solo.playerId, uuid);
    assert.equal(solo.cards.length, 8);
    const hand = await call('/solo/play-hand/' + solo.gameId, 'POST', { playerId: solo.playerId, hand: [solo.cards[0]] });
    assert.equal(hand.handsRemaining, solo.handsAvailable - 1);
    assert.equal((await call('/solo/advance-phase/' + solo.gameId, 'POST', {})).newPhase, 'Scoreboard');
    assert.equal((await call('/solo/advance-phase/' + solo.gameId, 'POST', {})).newPhase, 'GameOver');
    checks.solo = true;
    stage = 'owned-session-leave';
  } catch {
    failure = stage;
  } finally {
    try { if (runCleanup) await runCleanup(); }
    catch { failure = failure || 'owned-session-leave'; }
    await Promise.allSettled(pages.map(page => page.evaluate(() => window.__release?.hub.stop())));
    for (const context of contexts) {
      try { await context.close(); } catch { failure = failure || 'browser-close'; }
    }
  }
  if (failure) throw Error('Release verification failed at ' + failure + '; no promotable evidence emitted');
  return { checks: { ...checks }, verified_unix: Math.floor(Date.now() / 1000),
    client_source_sha: packet.client.source_sha, client_archive_sha256: packet.client.archive_sha256 };
}

module.exports = { runGameplay };
