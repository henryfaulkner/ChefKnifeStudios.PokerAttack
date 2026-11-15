import { test, expect } from '@playwright/test';
import { createServer, Server } from 'node:http';
import { AddressInfo } from 'node:net';
import { cleanupServerData } from '../helpers/setup';

// These HTTP fixtures never contact the real pilot or production.
test.describe('cleanup HTTP boundary', () => {
  let server: Server;
  let origin: string;
  let posts: number;
  let redirects: number;
  let badBase: boolean;
  let badOverride: boolean;
  let redirectCleanup: boolean;
  const envKeys = ['API_PUBLIC_URL', 'ALLOW_LOCAL_INSECURE_TLS', 'NODE_TLS_REJECT_UNAUTHORIZED'];
  let saved: Record<string, string | undefined>;
  test.beforeEach(async () => {
    saved = Object.fromEntries(envKeys.map(key => [key, process.env[key]]));
    envKeys.forEach(key => delete process.env[key]);
    posts = redirects = 0;
    badBase = badOverride = redirectCleanup = false;
    const config = (bad: boolean) => ({ AppSettings: { ExternalApis: ['PokerAttackAPI', 'PokerAttackSignalR'].map(Name => ({
      Name, BaseUri: bad ? 'https://api.playpokerattack.com/' : `${origin}/`,
    })) } });
    server = createServer((req, res) => {
      if (req.url === '/appsettings.json') {
        res.writeHead(200, { 'Content-Type': 'application/json', 'Blazor-Environment': 'prod' });
        res.end(JSON.stringify(config(badBase)));
      } else if (req.url === '/appsettings.prod.json') {
        res.writeHead(200, { 'Content-Type': badOverride ? 'application/json' : 'text/html' });
        res.end(badOverride ? JSON.stringify(config(true)) : '<html>SWA fallback</html>');
      } else if (req.url === '/test/cleanup' && req.method === 'POST') {
        posts++;
        res.writeHead(redirectCleanup ? 307 : 200, redirectCleanup ? { Location: `${origin}/redirected` } : {});
        res.end();
      } else {
        redirects++;
        res.writeHead(200);
        res.end();
      }
    });
    await new Promise<void>(done => server.listen(0, '127.0.0.1', done));
    origin = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  });
  test.afterEach(async () => {
    for (const key of envKeys) {
      if (saved[key] === undefined) delete process.env[key]; else process.env[key] = saved[key];
    }
    await new Promise<void>((done, reject) => server.close(error => error ? reject(error) : done()));
  });
  test('rejects served production URLs before POST', async () => {
    badBase = true;
    await expect(cleanupServerData(origin, origin)).rejects.toThrow('must match');
    expect(posts).toBe(0);
  });
  test('accepts verified isolated settings with prod HTML fallback', async () => {
    await cleanupServerData(origin, origin);
    expect(posts).toBe(1);
  });
  test('rejects production environment override before POST', async () => {
    badOverride = true;
    await expect(cleanupServerData(origin, origin)).rejects.toThrow('must match');
    expect(posts).toBe(0);
  });
  test('does not follow a cleanup redirect', async () => {
    redirectCleanup = true;
    await expect(cleanupServerData(origin, origin)).rejects.toThrow('HTTP 307');
    expect(posts).toBe(1);
    expect(redirects).toBe(0);
  });
});
