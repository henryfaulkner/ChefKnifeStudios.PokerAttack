'use strict';
const assert = require('node:assert/strict');

// Self-scoped metadata only. Server-recorded IDs, never a name/prefix search or
// global cleanup. Previous attempts use fresh credentials and new player IDs.
async function recoverOwnedSessions(call, endGame, limitMs = 60000) {
  const deadline = Date.now() + limitMs;
  const uuid = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;
  while (Date.now() < deadline) {
    const owned = await call('/release/owned-sessions');
    assert.ok(Number.isInteger(owned.inFlightOperations) && owned.inFlightOperations >= 0);
    for (const field of ['lobbies', 'games', 'solo']) assert.ok(Array.isArray(owned[field]) && owned[field].length <= 64);
    if (owned.inFlightOperations) {
      await new Promise(resolve => setTimeout(resolve, 200));
      continue;
    }
    if (!owned.games.length && !owned.lobbies.length && !owned.solo.length) return;
    for (const id of owned.games) { assert.match(id, uuid); await endGame(id); }
    for (const solo of owned.solo) {
      assert.match(solo.gameId, uuid); assert.match(solo.playerId, uuid);
      await call('/solo/leave-game/' + solo.gameId, 'POST', { playerId: solo.playerId });
    }
    for (const lobby of owned.lobbies) {
      assert.match(lobby.id, /^[A-Z0-9]{6}$/);
      assert.match(lobby.hostPlayer.id, uuid);
      assert.equal(typeof lobby.hostPlayer.name, 'string');
      await call('/lobby/remove-player', 'POST', { lobbyId: lobby.id, player: lobby.hostPlayer });
    }
    // Require a fresh exact empty owner listing after normal leave operations.
  }
  throw Error('Owned session recovery did not complete within its bound');
}

module.exports = { recoverOwnedSessions };
