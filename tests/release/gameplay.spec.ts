import { test } from '@playwright/test';
const { loadContext } = require('./context.cjs');
const { runGameplay } = require('./gameplay.cjs');

test('release browsers verify identities and complete only owned gameplay', async ({ browser }) => {
  // Keep any context-loading exception from reporting file contents/credentials.
  let packet;
  try { packet = loadContext(process.env.POKERATTACK_RELEASE_CONTEXT); }
  catch { throw Error('Private verifier context is missing or invalid'); }
  await runGameplay(browser, packet);
});
