// Tests for the keep-alive (src/Cv.Public/keep-alive.js), with a fake database.
//   node --test "tests/Cv.Public.Js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { keepAlive } from '../../src/Cv.Public/keep-alive.js';

function fakeLog() {
  const lines = [];
  return { lines, log: (...a) => lines.push(['log', ...a]), error: (...a) => lines.push(['error', ...a]) };
}

test('a reachable database is logged with the version English serves', async () => {
  const log = fakeLog();

  assert.equal(await keepAlive(async () => 4, log), 4);
  assert.deepEqual(log.lines, [['log', 'keep-alive: database reached; en serves v4']]);
});

test('nothing published still counts: the database was reached', async () => {
  const log = fakeLog();

  assert.equal(await keepAlive(async () => null, log), null);
  assert.match(log.lines[0][1], /database reached; nothing is published/);
});

test('a failure is logged and rethrown, so Cloudflare marks the run as failed', async () => {
  const log = fakeLog();
  const failure = new Error('password authentication failed');

  await assert.rejects(keepAlive(async () => { throw failure; }, log), failure);
  assert.deepEqual(log.lines, [['error', 'keep-alive: could not reach the database', failure]]);
});
