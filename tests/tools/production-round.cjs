// Manual bounded production check: two controlled players, one real persisted round.
// No cleanup endpoints, schema changes, timer changes or shared user sessions.
const { HubConnectionBuilder, HttpTransportType, LogLevel } = require('@microsoft/signalr');
const { randomUUID } = require('node:crypto');
const fs = require('node:fs');
const assert = require('node:assert/strict');
const api = 'https://api.playpokerattack.com';
const players = [0,1].map(index => ({ id: randomUUID(), name: `MigrationCheck${index}` }));
const hubs = [], received = players.map(() => ({ cards: [], deals: 0, hands: 0, rounds: 0, fences: new Set() }));
const result = { started_utc: new Date().toISOString(), api };
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(check, ms = 30000) {
  const deadline = Date.now()+ms;
  while (!check()) { if (Date.now()>deadline) throw Error('Production notification timeout'); await sleep(25); }
}
async function call(route, method, data) {
  const response = await fetch(api+route, { method, redirect: 'error', signal: AbortSignal.timeout(10000),
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) });
  assert.equal(response.ok, true);
  const value = await response.json(); assert.equal(value.isSuccess, true); return value.value;
}
async function main() {
  for (const [index, player] of players.entries()) {
    const hub = new HubConnectionBuilder().withUrl(`${api}/cks-notification?playerId=${player.id}`,
      { transport: HttpTransportType.WebSockets }).configureLogging(LogLevel.Error).build();
    hub.on('ReceivePokerAttackNotification', notification => {
      const state = received[index], kind = notification.notificationType;
      if ([9,'CardsDealt'].includes(kind)) { state.cards=JSON.parse(notification.payload);state.deals++; }
      if ([10,'HandPlayed'].includes(kind)) state.hands++;
      if ([12,'RoundEnded'].includes(kind)) state.rounds++;
      if ([7,'Test'].includes(kind)) state.fences.add(notification.payload);
    });
    hubs.push(hub); await hub.start();
  }
  const lobby = await call('/lobby/create-lobby','POST',{hostPlayer:players[0]});
  await call('/lobby/add-player','PATCH',{lobbyId:lobby.id,player:players[1]});
  result.game_id = await call('/lobby/start-game','POST',{lobbyId:lobby.id,gameMode:0});
  await Promise.all(hubs.map(hub=>hub.invoke('JoinGameGroupAsync',result.game_id)));
  await until(()=>received.every(state=>state.cards.length===8));
  for (let actor=0;actor<2;actor++) {
    const before=received.map(state=>({hands:state.hands,deals:state.deals}));
    // Notification payloads use string enums; the hub's typed arguments use numeric enums.
    const card=received[actor].cards[0];
    const suit=card.Suit??card.suit, rank=card.Rank??card.rank;
    const typed={suit:typeof suit==='number'?suit:['spade','club','diamond','heart'].indexOf(suit),
      rank:typeof rank==='number'?rank:['two','three','four','five','six','seven','eight','nine','ten','jack','queen','king','ace'].indexOf(rank)+2,
      isFaceDown:card.IsFaceDown??card.isFaceDown};
    assert.equal(typed.suit>=0 && typed.rank>=2,true);
    await hubs[actor].invoke('PlayHand',players[actor].id,[typed]);
    await until(()=>received[actor].hands===before[actor].hands+1 && received[actor].deals===before[actor].deals+1);
    const fence=randomUUID();
    await hubs[actor].invoke('BroadcastGameNotification',result.game_id,{notificationType:7,payload:fence});
    await until(()=>received.every(state=>state.fences.has(fence)));
    assert.equal(received[1-actor].hands,before[1-actor].hands);
    assert.equal(received[1-actor].deals,before[1-actor].deals);
  }
  await until(()=>received.every(state=>state.rounds>=1),150000);
  result.passed=true; result.private_message_isolation=true; result.round_notification_receipts=received.map(state=>state.rounds);
  result.completed_utc=new Date().toISOString();
}
main().catch(error=>{result.passed=false;result.error=error.message;process.exitCode=1;}).finally(async()=>{
  await Promise.allSettled(hubs.map(hub=>hub.stop()));
  fs.writeFileSync('artifacts/production-round-result.json',JSON.stringify(result,null,2));
  console.log(JSON.stringify(result));
});
