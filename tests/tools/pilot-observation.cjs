// Manual, fixed-target 30-minute observation. Never run alongside cleanup suites.
const { HubConnectionBuilder, HttpTransportType, LogLevel } = require('@microsoft/signalr');
const { randomUUID } = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

const api = 'https://pilot-api.playpokerattack.com';
const output = path.resolve(__dirname, '../../artifacts/pilot-observation');
fs.mkdirSync(output, { recursive: false }); // Refuse to overwrite earlier evidence.
const connections = [], latencies = [], deliveryTimes = [], failures = [];
let stopping = false;
const report = { target: api, duration_seconds: 1800, samples: 0, games_started: 0 };
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
async function call(route, method = 'GET', data) {
  const started = performance.now();
  const response = await fetch(api + route, { method, redirect: 'error', signal: AbortSignal.timeout(10000),
    headers: data ? { 'Content-Type': 'application/json' } : {}, body: data ? JSON.stringify(data) : undefined });
  latencies.push(performance.now() - started);
  if (!response.ok) throw Error(`${method} ${route}: ${response.status}`);
  const text = await response.text();
  if (route.startsWith('/health/')) return text;
  const result = JSON.parse(text);
  if (result.isSuccess !== true) throw Error(`${method} ${route}: application failure`);
  return result.value;
}
const percentile = (values, fraction) => [...values].sort((a,b) => a-b)[Math.ceil(values.length*fraction)-1];
async function main() {
  const players = Array.from({ length: 20 }, (_, index) => ({ id: randomUUID(), name: `Observation${index}` }));
  const receipts = players.map(() => new Map());
  for (const [index, player] of players.entries()) {
    const hub = new HubConnectionBuilder().withUrl(`${api}/cks-notification?playerId=${player.id}`,
      { transport: HttpTransportType.WebSockets }).configureLogging(LogLevel.Error).build();
    hub.on('ReceivePokerAttackNotification', notification => {
      if (notification.notificationType === 7 || notification.notificationType === 'Test')
        receipts[index].set(notification.payload, performance.now());
    });
    hub.onclose(() => { if (!stopping) failures.push(`Unexpected connection closure: ${index}`); });
    connections.push(hub);
    await hub.start();
  }
  // Real isolated games supplement the earlier complete browser-game results.
  const transportGroups = [randomUUID(), randomUUID()];
  for (let group = 0; group < 2; group++) {
    const members = players.slice(group*10, group*10+10);
    const lobby = await call('/lobby/create-lobby', 'POST', { hostPlayer: members[0] });
    for (const player of members.slice(1)) await call('/lobby/add-player', 'PATCH', { lobbyId: lobby.id, player });
    const game = await call('/lobby/start-game', 'POST', { lobbyId: lobby.id, gameMode: 0 });
    // Separate transport groups persist after the real games finish/eliminate players.
    await Promise.all(connections.slice(group*10, group*10+10).map(async hub => {
        await hub.invoke('JoinGameGroupAsync', game);
        await hub.invoke('JoinGameGroupAsync', transportGroups[group]);
      }));
    report.games_started++;
  }
  report.started_utc = new Date().toISOString();
  const deadline = performance.now() + 1800000;
  do {
    const sample = { utc: new Date().toISOString(), connected: connections.filter(hub => hub.state === 'Connected').length };
    try {
      await call('/health/ready');
      if (sample.connected !== 20) throw Error(`Only ${sample.connected}/20 connections remain`);
      for (let group = 0; group < 2; group++) {
        const token = randomUUID(), start = performance.now();
        await connections[group*10].invoke('BroadcastGameNotification', transportGroups[group], { notificationType: 7, payload: token });
        const until = performance.now() + 10000;
        while (!receipts.slice(group*10,group*10+10).every(items => items.has(token))) {
          if (performance.now() > until) throw Error('Group notification delivery timeout');
          await sleep(10);
        }
        for (const items of receipts.slice(group*10,group*10+10)) {
          deliveryTimes.push(items.get(token)-start); items.delete(token);
        }
      }
      if (report.samples % 4 === 0) {
        const host = execFileSync('ssh', ['-o','BatchMode=yes','-o','IdentitiesOnly=yes','-o','StrictHostKeyChecking=yes',
          '-o','ConnectTimeout=10','-i',path.join(process.env.USERPROFILE,'.ssh/id_ed25519_linode_us_southeast'),
          'root@170.187.142.115', 'docker inspect --format \'{{.Id}} {{.State.Running}} {{.State.OOMKilled}} {{.RestartCount}} {{.HostConfig.NanoCpus}} {{.HostConfig.Memory}}\' pokerattack-pilot-api-1; docker stats --no-stream --format \'{{.Name}} {{.CPUPerc}} {{.MemUsage}} {{.NetIO}} {{.BlockIO}}\' pokerattack-pilot-api-1'],
          { encoding: 'utf8', timeout: 20000, windowsHide: true });
        sample.host = host.trim();
      }
    } catch (error) { failures.push(`${sample.utc}: ${error.message}`); }
    report.samples++;
    fs.appendFileSync(path.join(output, 'samples.jsonl'), JSON.stringify(sample)+'\n');
    fs.writeFileSync(path.join(output, 'progress.json'), JSON.stringify({ ...report, failures }, null, 2));
    if (performance.now() >= deadline) break;
    await sleep(Math.min(15000, deadline-performance.now()));
  } while (true);
  report.completed_utc = new Date().toISOString();
  report.api_roundtrip_p95_ms = percentile(latencies, .95);
  report.group_roundtrip_p95_ms = percentile(deliveryTimes, .95);
  report.passed = failures.length === 0 && report.api_roundtrip_p95_ms < 500 && report.group_roundtrip_p95_ms < 1000;
}
main().catch(error => { failures.push(error.message); report.passed = false; }).finally(async () => {
  stopping = true;
  await Promise.allSettled(connections.map(hub => hub.stop()));
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ ...report, failures }, null, 2));
  process.exitCode = report.passed ? 0 : 1;
});
