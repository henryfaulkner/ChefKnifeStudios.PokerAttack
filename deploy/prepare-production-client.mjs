import { cpSync, existsSync, lstatSync, readFileSync, readdirSync, realpathSync, writeFileSync } from 'node:fs';
import { resolve, relative, isAbsolute, join, sep } from 'node:path';
import { gzipSync, brotliCompressSync } from 'node:zlib';
import { pathToFileURL } from 'node:url';

const apiOrigin = 'https://api.playpokerattack.com/';
const names = ['PokerAttackAPI', 'PokerAttackSignalR'];
function inside(parent, child) {
  const part = relative(parent, child);
  return part === '' || (part !== '..' && !part.startsWith('..' + sep) && !isAbsolute(part));
}
function object(value) { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function present(path) {
  try { lstatSync(path); return true; } catch (error) { if (error.code === 'ENOENT') return false; throw error; }
}
function regularTree(directory) {
  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry), info = lstatSync(path);
    if (info.isDirectory() && !info.isSymbolicLink()) regularTree(path);
    else if (!info.isFile() || info.isSymbolicLink()) throw Error('Published artifacts must contain only regular files/directories');
  }
}

// The third parameter is a trusted test/embedding root; the CLI always uses the
// repository artifacts directory. This prepares bytes and never publishes.
export function prepareProduction(sourcePath, outputPath, artifactRoot = 'artifacts') {
  const source = realpathSync(sourcePath), output = resolve(outputPath), artifacts = realpathSync(artifactRoot);
  if (!lstatSync(source).isDirectory() || !lstatSync(artifacts).isDirectory()) throw Error('Expected artifact directories');
  if (present(output) || inside(source, output) || inside(output, source))
    throw Error('Use a fresh output directory outside source');
  let parent = resolve(output, '..');
  while (!existsSync(parent)) parent = resolve(parent, '..');
  const actualOutput = resolve(realpathSync(parent), relative(parent, output));
  if (!inside(artifacts, actualOutput) || actualOutput === artifacts || inside(source, actualOutput) || inside(actualOutput, source))
    throw Error('Output must be safely inside artifacts and outside source');
  regularTree(source);
  const settings = readdirSync(source).filter(name => /^appsettings(?:\.[A-Za-z0-9_.-]+)?\.json$/.test(name))
    .map(name => [name, JSON.parse(readFileSync(join(source, name), 'utf8'))]);
  const entries = settings.find(([name]) => name === 'appsettings.json')?.[1]?.AppSettings?.ExternalApis;
  if (!Array.isArray(entries) || entries.length !== 2 || entries.some(entry => !object(entry)) ||
      names.some(name => entries.filter(entry => entry.Name === name).length !== 1))
    throw Error('Expected exactly both named API entries');
  for (const [, config] of settings) {
    if (!object(config) || config.AppSettings !== undefined && !object(config.AppSettings))
      throw Error('Invalid settings object');
    const overrides = config.AppSettings?.ExternalApis;
    if (overrides !== undefined && (!Array.isArray(overrides) || overrides.length > entries.length ||
        overrides.some((entry, index) => !object(entry) || entry.Name !== undefined && entry.Name !== entries[index].Name)))
      throw Error('Ambiguous environment API overrides');
    config.AppSettings ??= {};
    config.AppSettings.ExternalApis = entries.map((entry, index) => ({ ...entry, ...overrides?.[index], BaseUri: apiOrigin }));
  }
  const swa = JSON.parse(readFileSync(join(source, 'staticwebapp.config.json'), 'utf8'));
  if (!object(swa) || swa.globalHeaders !== undefined && !object(swa.globalHeaders)) throw Error('Invalid SWA configuration');
  if (Object.values(swa.globalHeaders ?? {}).some(value => typeof value !== 'string')) throw Error('Invalid SWA header value');
  const headers = Object.fromEntries(Object.entries(swa.globalHeaders ?? {}).filter(([key]) =>
    !['blazor-environment', 'cache-control'].includes(key.toLowerCase())));
  swa.globalHeaders = { ...headers, 'Blazor-Environment': 'prod', 'Cache-Control': 'no-cache' };
  // Validate before copying so bad inputs cannot leave deployable output.
  cpSync(source, output, { recursive: true, dereference: false });
  function write(name, value) {
    const bytes = Buffer.from(JSON.stringify(value, null, 2) + '\n');
    writeFileSync(join(output, name), bytes);
    writeFileSync(join(output, name + '.gz'), gzipSync(bytes));
    writeFileSync(join(output, name + '.br'), brotliCompressSync(bytes));
  }
  for (const [name, config] of settings) write(name, config);
  write('staticwebapp.config.json', swa);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  if (process.argv.length !== 4) throw Error('Required: published-wwwroot fresh-artifact-directory');
  prepareProduction(process.argv[2], process.argv[3]);
  console.log('Prepared unpublished production client: both named APIs and SWA environment prod verified.');
}
