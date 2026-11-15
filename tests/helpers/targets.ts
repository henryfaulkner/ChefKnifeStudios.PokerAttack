export interface TestTargets {
  clientUrl: string;
  cleanupApiUrl: string;
  publicApiUrl: string;
  ignoreHttpsErrors: boolean;
}

const productionClients = new Set([
  'playpokerattack.com', 'www.playpokerattack.com',
  'salmon-hill-0056eb60f.2.azurestaticapps.net',
  'henryfaulkner.xyz', 'www.henryfaulkner.xyz', 'hankfaulkner.com', 'www.hankfaulkner.com',
]);
const loopback = new Set(['localhost', '127.0.0.1', '[::1]']);
const pilotApi = 'pilot-api.playpokerattack.com';

function baseUrl(value: string | undefined, name: string): URL {
  if (!value) throw new Error(`${name} must be explicitly configured; target guessing is disabled.`);
  let url: URL;
  try { url = new URL(value); } catch { throw new Error(`${name} is not a valid HTTP(S) origin.`); }
  if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password ||
      url.search || url.hash || url.pathname !== '/') throw new Error(`${name} must be an HTTP(S) origin without credentials, paths or query strings.`);
  if (url.hostname.endsWith('.')) throw new Error(`${name} must use its canonical hostname.`);
  return url;
}

function isolatedApi(url: URL): boolean {
  return loopback.has(url.hostname) || (url.hostname === pilotApi && url.protocol === 'https:');
}

export function resolveTargets(env: NodeJS.ProcessEnv = process.env): TestTargets {
  const client = baseUrl(env.CLIENT_URL, 'CLIENT_URL');
  const cleanup = baseUrl(env.API_URL_CLEANUP, 'API_URL_CLEANUP');
  const publicApi = baseUrl(env.API_PUBLIC_URL || env.API_URL_CLEANUP, 'API_PUBLIC_URL');
  if (productionClients.has(client.hostname)) throw new Error('Production browser targets are forbidden for destructive E2E. Use an isolated client build.');
  if (!isolatedApi(cleanup) || !isolatedApi(publicApi)) throw new Error('Cleanup/public API must be loopback or the dedicated HTTPS pilot API. Production and arbitrary API targets are forbidden.');
  const insecure = env.ALLOW_LOCAL_INSECURE_TLS === 'true';
  if (env.NODE_TLS_REJECT_UNAUTHORIZED === '0') throw new Error('Global TLS verification bypass is forbidden. Use the explicit local-only setting instead.');
  if (insecure && (env.CI || !loopback.has(client.hostname) || !loopback.has(cleanup.hostname) || !loopback.has(publicApi.hostname)))
    throw new Error('Certificate bypass is allowed only for explicit local development, never CI or public pilot HTTPS.');
  return { clientUrl: client.origin, cleanupApiUrl: cleanup.origin, publicApiUrl: publicApi.origin, ignoreHttpsErrors: insecure };
}

export function validateServedApis(baseSettings: any, overrideSettings: any, targets: TestTargets): void {
  const base = baseSettings?.AppSettings?.ExternalApis;
  const overrides = overrideSettings?.AppSettings?.ExternalApis;
  if (!Array.isArray(base)) throw new Error('Served client API configuration is missing. Cleanup refused.');
  const merged = Array.from({ length: Math.max(base.length, Array.isArray(overrides) ? overrides.length : 0) },
    (_, index) => ({ ...base[index], ...(Array.isArray(overrides) ? overrides[index] : {}) }));
  for (const name of ['PokerAttackAPI', 'PokerAttackSignalR']) {
    const entry = merged.find(item => item.Name === name);
    if (!entry || baseUrl(entry.BaseUri, name).origin !== targets.publicApiUrl)
      throw new Error(`Served ${name} must match the explicitly selected isolated public API before cleanup.`);
  }
}
