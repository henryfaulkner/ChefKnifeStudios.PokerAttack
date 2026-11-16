'use strict';
// Deliberately terminates WITHOUT session cleanup, on explicit localhost fixture
// targets only. Never imported by the production verifier or workflows.
const { loadContext } = require('./context.cjs');
const { resolveReleaseTargets, assertRequest } = require('./safety.cjs');

async function main() {
  const targets = resolveReleaseTargets();
  if (targets.mode !== 'isolated') throw Error('Local fixture required');
  const packet = loadContext(process.env.POKERATTACK_RELEASE_CONTEXT);
  async function call(route, method, body) {
    const url = targets.api + route;
    assertRequest(targets, url, method);
    const response = await fetch(url, { method, redirect: 'error', signal: AbortSignal.timeout(15000),
      headers: { Authorization: 'Bearer ' + packet.credential, 'Content-Type': 'application/json' },
      body: JSON.stringify(body) });
    if (!response.ok) throw Error('Fixture operation rejected');
    const result = await response.json();
    if (result.isSuccess !== true) throw Error('Fixture operation failed');
    return result.value;
  }
  const lobby = await call('/lobby/create-lobby', 'POST', { hostPlayer: packet.players[0] });
  await call('/lobby/add-player', 'PATCH', { lobbyId: lobby.id, player: packet.players[1] });
  await call('/lobby/start-game', 'POST', { lobbyId: lobby.id, gameMode: 0 });
  await call('/solo/start-game', 'POST', { playerName: packet.players[2].name });
  console.log('Fixture sessions allocated; terminating this local runner without session cleanup.');
  process.exit(137);
}
main().catch(() => { console.error('Local crashed-run fixture setup failed'); process.exitCode = 1; });
