const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const assert = require('node:assert/strict');

function loadContext(filename, env = process.env) {
  const root = fs.realpathSync(env.RUNNER_TEMP || os.tmpdir());
  const real = fs.realpathSync(filename);
  const relative = path.relative(root, real);
  const info = fs.lstatSync(filename);
  assert.ok(relative && !relative.startsWith('..') && !path.isAbsolute(relative), 'Context must be private runner temporary data');
  assert.ok(info.isFile() && !info.isSymbolicLink() && info.size <= 65536, 'Unsafe release context');
  if (process.platform !== 'win32') assert.equal(info.mode & 0o077, 0, 'Release context must be owner-only');
  const packet = JSON.parse(fs.readFileSync(real, 'utf8'));
  assert.equal(typeof packet, 'object');
  return packet;
}

module.exports = { loadContext };
