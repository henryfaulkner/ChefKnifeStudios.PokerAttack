'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { recoverOwnedSessions } = require('./owned.cjs');

test('owned recovery uses only returned IDs and normal leave, then requires empty listing', async () => {
  const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
  const player = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
  const operations = [];
  let reads = 0;
  const call = async (route, method, body) => {
    operations.push({ route, method, body });
    if (route !== '/release/owned-sessions') return {};
    return ++reads === 1 ? { inFlightOperations: 0, games: [id], solo: [{ gameId: id, playerId: player }],
      lobbies: [{ id: 'ABC123', hostPlayer: { id: player, name: 'Release-1-1-0' } }] } :
      { inFlightOperations: 0, games: [], solo: [], lobbies: [] };
  };
  await recoverOwnedSessions(call, async value => operations.push({ endGame: value }));
  assert.equal(reads, 2);
  assert.deepEqual(operations.map(value => value.route || 'EndGame'), ['/release/owned-sessions', 'EndGame',
    '/solo/leave-game/' + id, '/lobby/remove-player', '/release/owned-sessions']);
  assert.ok(!JSON.stringify(operations).includes('/test/'));
});

test('invalid owner metadata fails before emitting a destructive or arbitrary request', async () => {
  const calls = [];
  await assert.rejects(recoverOwnedSessions(async route => {
    calls.push(route);
    return { inFlightOperations: 0, games: ['../unrelated'], solo: [], lobbies: [] };
  }, async () => assert.fail('Invalid game was ended')));
  assert.deepEqual(calls, ['/release/owned-sessions']);
});
