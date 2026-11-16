'use strict';

// Independent from isolated E2E helpers. No cleanup or arbitrary production URL.
const CLIENT_ORIGINS = Object.freeze([
  'https://playpokerattack.com', 'https://www.playpokerattack.com',
  'https://salmon-hill-0056eb60f.2.azurestaticapps.net',
]);
const API_ORIGIN = 'https://api.playpokerattack.com';

function origin(value) {
  let url;
  try { url = new URL(value); } catch { throw Error('Release target must be an exact HTTPS origin'); }
  if (url.protocol !== 'https:' || url.username || url.password || url.search || url.hash ||
      !['', '/'].includes(url.pathname) || value !== url.origin)
    throw Error('Release target must be an exact HTTPS origin');
  return url;
}

function resolveReleaseTargets(env = process.env) {
  if (env.DEBUG || env.PWDEBUG || env.NODE_DEBUG || env.PLAYWRIGHT_TRACE_DIR)
    throw Error('Protocol debugging/capture is prohibited for verifier credentials');
  if (env.NODE_TLS_REJECT_UNAUTHORIZED === '0' || env.IGNORE_HTTPS_ERRORS === 'true' ||
      /--ignore-certificate-errors|--allow-insecure-localhost/.test(env.PLAYWRIGHT_CHROMIUM_ARGS || ''))
    throw Error('TLS verification cannot be disabled');
  if (env.POKERATTACK_RELEASE_MODE === 'production') {
    const client = env.RELEASE_CLIENT_ORIGIN || CLIENT_ORIGINS[0];
    const api = env.RELEASE_API_ORIGIN || API_ORIGIN;
    origin(client); origin(api);
    if (!CLIENT_ORIGINS.includes(client) || api !== API_ORIGIN)
      throw Error('Production release target is not allowlisted');
    return Object.freeze({ mode: 'production', client, api, clients: [...CLIENT_ORIGINS] });
  }
  if (env.POKERATTACK_RELEASE_MODE === 'isolated') {
    const client = origin(env.RELEASE_CLIENT_ORIGIN);
    const api = origin(env.RELEASE_API_ORIGIN);
    if ([client, api].some(url => url.hostname !== 'localhost' || Number(url.port) < 1024))
      throw Error('Isolated release tests require explicit localhost HTTPS ports');
    return Object.freeze({ mode: 'isolated', client: client.origin, api: api.origin, clients: [client.origin] });
  }
  throw Error('Choose an explicit production or isolated release mode');
}

function assertRequest(targets, url, method = 'GET') {
  const parsed = new URL(url);
  const path = decodeURIComponent(parsed.pathname).toLowerCase();
  if (path.split('/').some(part => part === 'test' || part === '_ops') || /cleanup/.test(path))
    throw Error('Destructive/operator routes are prohibited in release verification');
  if (![targets.api, ...targets.clients].includes(parsed.origin) || parsed.username || parsed.password)
    throw Error('Release request escaped the allowlist');
  if (parsed.origin !== targets.api && !['GET', 'HEAD'].includes(method.toUpperCase()))
    throw Error('Client verification is read-only');
}

module.exports = { CLIENT_ORIGINS, API_ORIGIN, resolveReleaseTargets, assertRequest };
