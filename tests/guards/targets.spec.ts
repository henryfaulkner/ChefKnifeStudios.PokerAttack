import { test, expect } from '@playwright/test';
import { resolveTargets, validateServedApis } from '../helpers/targets';

const local = { CLIENT_URL: 'http://localhost:5173', API_URL_CLEANUP: 'http://127.0.0.1:8082' };

test('explicit loopback targets are accepted', () => {
  expect(resolveTargets(local).ignoreHttpsErrors).toBe(false);
});
test('pilot supports a separately selected public API and loopback cleanup tunnel', () => {
  expect(resolveTargets({ ...local, API_PUBLIC_URL: 'https://pilot-api.playpokerattack.com' }).publicApiUrl)
    .toBe('https://pilot-api.playpokerattack.com');
});
for (const env of [{}, { CLIENT_URL: local.CLIENT_URL }, { API_URL: local.CLIENT_URL, API_URL_CLEANUP: local.API_URL_CLEANUP }]) {
  test(`missing explicit targets fail: ${JSON.stringify(env)}`, () => expect(() => resolveTargets(env)).toThrow());
}
for (const [index, api] of ['https://api.playpokerattack.com', 'https://poker-attack-api.ambitioussmoke-21ab3755.eastus.azurecontainerapps.io',
  'https://pilot-api.playpokerattack.com.evil.example', 'https://arbitrary.example', 'http://pilot-api.playpokerattack.com',
  'https://user:password@pilot-api.playpokerattack.com', 'https://pilot-api.playpokerattack.com/test/cleanup'].entries()) {
  test(`unsafe API target ${index} fails before requests`, () =>
    expect(() => resolveTargets({ ...local, API_URL_CLEANUP: api })).toThrow());
}
for (const client of ['https://playpokerattack.com', 'https://www.playpokerattack.com', 'https://salmon-hill-0056eb60f.2.azurestaticapps.net']) {
  test(`production client is refused: ${client}`, () => expect(() => resolveTargets({ ...local, CLIENT_URL: client })).toThrow());
}
test('TLS bypass requires explicit local-only opt in', () => {
  expect(resolveTargets({ ...local, ALLOW_LOCAL_INSECURE_TLS: 'true' }).ignoreHttpsErrors).toBe(true);
  expect(() => resolveTargets({ ...local, CI: 'true', ALLOW_LOCAL_INSECURE_TLS: 'true' })).toThrow();
  expect(() => resolveTargets({ ...local, API_PUBLIC_URL: 'https://pilot-api.playpokerattack.com', ALLOW_LOCAL_INSECURE_TLS: 'true' })).toThrow();
  expect(() => resolveTargets({ ...local, NODE_TLS_REJECT_UNAUTHORIZED: '0' })).toThrow();
});
const settings = (api: string, hub = api) => ({ AppSettings: { ExternalApis: [
  { Name: 'PokerAttackAPI', BaseUri: api }, { Name: 'PokerAttackSignalR', BaseUri: hub },
] } });
test('served API and hub must both match the selected isolated target', () => {
  const targets = resolveTargets(local);
  expect(() => validateServedApis(settings(targets.publicApiUrl), null, targets)).not.toThrow();
  expect(() => validateServedApis(settings('https://api.playpokerattack.com'), null, targets)).toThrow();
  expect(() => validateServedApis(settings(targets.publicApiUrl, 'https://api.playpokerattack.com'), null, targets)).toThrow();
  expect(() => validateServedApis(settings(targets.publicApiUrl), settings('https://api.playpokerattack.com'), targets)).toThrow();
});
