const test = require('node:test');
const assert = require('node:assert/strict');
const { assertPrivateIsolation } = require('./isolation.cjs');

test('correct private routing passes only after every self fence', () => {
  const before = [{ hands: 0, cards: 1 }, { hands: 0, cards: 1 }];
  const after = [{ hands: 1, cards: 2 }, { hands: 0, cards: 1 }];
  assert.doesNotThrow(() => assertPrivateIsolation(before, after, 0, [true, true]));
  assert.throws(() => assertPrivateIsolation(before, after, 0, [true, false]));
});

test('deliberately misrouted hand or card notification fails the evidence assertion', () => {
  const before = [{ hands: 0, cards: 1 }, { hands: 0, cards: 1 }];
  for (const privateField of ['hands', 'cards']) {
    const after = [{ hands: 1, cards: 2 }, { hands: 0, cards: 1 }];
    after[1][privateField]++;
    assert.throws(() => assertPrivateIsolation(before, after, 0, [true, true]));
  }
});
