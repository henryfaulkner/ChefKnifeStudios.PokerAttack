const assert = require('node:assert/strict');

function assertPrivateIsolation(before, after, actor, acknowledged) {
  assert.equal(acknowledged.length, 2);
  assert.ok(acknowledged.every(Boolean), 'Every receiving connection must observe its own later fence');
  assert.equal(after[actor].hands, before[actor].hands + 1, 'Intended player received hand result');
  assert.equal(after[actor].cards, before[actor].cards + 1, 'Intended player received replacement cards');
  const other = 1 - actor;
  assert.equal(after[other].hands, before[other].hands, 'Private result reached another player');
  assert.equal(after[other].cards, before[other].cards, 'Private cards reached another player');
}

module.exports = { assertPrivateIsolation };
