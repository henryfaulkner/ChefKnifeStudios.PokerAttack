import { cpSync, existsSync, readFileSync, readdirSync, realpathSync, writeFileSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { gzipSync, brotliCompressSync } from 'node:zlib';

const source = realpathSync(process.argv[2]);
const output = resolve(process.argv[3]);
const artifacts = realpathSync('artifacts');
if (!output.startsWith(artifacts + '\\') && !output.startsWith(artifacts + '/')) throw Error('Output must be under artifacts');
if (existsSync(output) || output.startsWith(source)) throw Error('Use a fresh directory outside source');
const base = JSON.parse(readFileSync(join(source, 'appsettings.json'), 'utf8'));
const entries = base.AppSettings.ExternalApis;
if (entries.length !== 2 || new Set(entries.map(api => api.Name)).size !== 2 ||
    !entries.some(api => api.Name === 'PokerAttackAPI') || !entries.some(api => api.Name === 'PokerAttackSignalR'))
  throw Error('Expected both named API entries');
cpSync(source, output, { recursive: true });
function write(name, value) {
  const bytes = Buffer.from(JSON.stringify(value, null, 2) + '\n');
  writeFileSync(join(output, name), bytes);
  writeFileSync(join(output, name + '.gz'), gzipSync(bytes));
  writeFileSync(join(output, name + '.br'), brotliCompressSync(bytes));
}
for (const file of readdirSync(source).filter(name => /^appsettings(?:\.[A-Za-z0-9_.-]+)?\.json$/.test(name))) {
  const config = JSON.parse(readFileSync(join(source, file), 'utf8'));
  const overrides = config.AppSettings?.ExternalApis;
  config.AppSettings ??= {};
  config.AppSettings.ExternalApis = entries.map((api, index) => ({ ...api, ...overrides?.[index], BaseUri: 'https://api.playpokerattack.com/' }));
  if (config.AppSettings.ExternalApis.some(api => !['PokerAttackAPI', 'PokerAttackSignalR'].includes(api.Name)))
    throw Error('Unexpected environment API override');
  write(file, config);
}
const swa = JSON.parse(readFileSync(join(source, 'staticwebapp.config.json'), 'utf8'));
swa.globalHeaders = { ...swa.globalHeaders, 'Blazor-Environment': 'prod', 'Cache-Control': 'no-cache' };
write('staticwebapp.config.json', swa);
console.log('Prepared unpublished production artifact: both endpoints use https://api.playpokerattack.com/');
