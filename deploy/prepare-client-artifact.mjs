import { cpSync, existsSync, readFileSync, readdirSync, realpathSync, writeFileSync } from 'node:fs';
import { resolve, relative, isAbsolute, join } from 'node:path';
import { gzipSync, brotliCompressSync } from 'node:zlib';
import { parseArgs } from 'node:util';
import { pathToFileURL } from 'node:url';

function inside(parent, child) {
  const path = relative(parent, child);
  return path === '' || (!path.startsWith('..') && !isAbsolute(path));
}

// Makes a separate, unpublished pilot artifact. Production selection belongs to cutover.
export function preparePilot(sourcePath, outputPath, apiOrigin) {
  const source = realpathSync(sourcePath);
  const output = resolve(outputPath);
  const api = new URL(apiOrigin);
  if (!['http:', 'https:'].includes(api.protocol) || api.username || api.password || api.pathname !== '/' || api.search || api.hash ||
      !(['localhost', '127.0.0.1', '[::1]'].includes(api.hostname) || (api.origin === 'https://pilot-api.playpokerattack.com')))
    throw new Error('Pilot API must be a loopback origin or https://pilot-api.playpokerattack.com.');
  if (existsSync(output) || inside(source, output) || inside(output, source))
    throw new Error('Output must be a new directory outside the source tree.');
  // Resolve existing parents as well, so symlinks cannot bypass the overlap check.
  let parent = resolve(output, '..');
  while (!existsSync(parent)) parent = resolve(parent, '..');
  if (inside(source, realpathSync(parent))) throw new Error('Output parent overlaps source.');
  const files = readdirSync(source).filter(name => /^appsettings(?:\.[A-Za-z0-9_.-]+)?\.json$/.test(name));
  const settings = files.map(name => [name, JSON.parse(readFileSync(join(source, name), 'utf8'))]);
  const base = settings.find(([name]) => name === 'appsettings.json')?.[1];
  const entries = base?.AppSettings?.ExternalApis;
  for (const name of ['PokerAttackAPI', 'PokerAttackSignalR']) {
    if (!Array.isArray(entries) || entries.filter(entry => entry.Name === name).length !== 1)
      throw new Error(`Base configuration must contain exactly one ${name}.`);
  }
  for (const [, config] of settings) {
    // Blazor merges arrays by index. Write the complete API entries in each override.
    const overrides = config?.AppSettings?.ExternalApis;
    if (overrides !== undefined && (!Array.isArray(overrides) || overrides.length > entries.length))
      throw new Error('Environment API overrides must match the base array.');
    const merged = entries.map((entry, index) => ({ ...entry, ...(overrides?.[index] || {}) }));
    for (const name of ['PokerAttackAPI', 'PokerAttackSignalR']) {
      const match = merged.filter(entry => entry.Name === name);
      if (match.length !== 1) throw new Error(`Ambiguous environment configuration for ${name}.`);
      match[0].BaseUri = `${api.origin}/`;
    }
    config.AppSettings ??= {};
    config.AppSettings.ExternalApis = merged;
    // Isolated fixture failures must not pollute the production client Sentry stream.
    config.Sentry = { ...config.Sentry, Dsn: '' };
  }
  cpSync(source, output, { recursive: true, dereference: true });
  for (const [name, config] of settings) {
    const bytes = Buffer.from(`${JSON.stringify(config, null, 2)}\n`);
    writeFileSync(join(output, name), bytes);
    writeFileSync(join(output, `${name}.gz`), gzipSync(bytes));
    writeFileSync(join(output, `${name}.br`), brotliCompressSync(bytes));
  }
  // This is a standalone pilot client, including when served by SWA.
  const swaPath = join(output, 'staticwebapp.config.json');
  const swa = existsSync(swaPath) ? JSON.parse(readFileSync(swaPath, 'utf8')) : {};
  swa.globalHeaders = { ...swa.globalHeaders, 'Blazor-Environment': 'Pilot' };
  swa.navigationFallback ??= { rewrite: '/index.html' };
  const swaBytes = Buffer.from(`${JSON.stringify(swa, null, 2)}\n`);
  writeFileSync(swaPath, swaBytes);
  for (const [extension, compress] of [['gz', gzipSync], ['br', brotliCompressSync]])
    if (existsSync(`${swaPath}.${extension}`)) writeFileSync(`${swaPath}.${extension}`, compress(swaBytes));
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const { values } = parseArgs({ options: { source: { type: 'string' }, output: { type: 'string' }, api: { type: 'string' } } });
  if (!values.source || !values.output || !values.api) throw new Error('Required: --source published/wwwroot --output NEW_DIRECTORY --api PILOT_ORIGIN');
  preparePilot(values.source, values.output, values.api);
  console.log('Prepared isolated pilot artifact. No publication performed.');
}
