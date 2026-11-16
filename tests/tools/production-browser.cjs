// Manual production verification with controlled fresh browser identities only.
const { chromium, expect: baseExpect } = require('@playwright/test');
const fs = require('node:fs');
const assert = require('node:assert/strict');
const expect = baseExpect.configure({ timeout: 30000 });
const report = { started_utc:new Date().toISOString(), sockets:[], api_hosts:[], round_receipts:[0,0] };
const contexts=[], wrongHosts=[];
let browser;
async function main() {
  browser=await chromium.launch();
  const pages=[];
  for (let index=0;index<2;index++) {
    const context=await browser.newContext();contexts.push(context);
    await context.addInitScript(()=>localStorage.setItem('HasSeenTour','true'));
    const page=await context.newPage();page.setDefaultTimeout(15000);page.setDefaultNavigationTimeout(45000);
    page.on('request', request=>{
      const host=new URL(request.url()).hostname;
      if (host==='api.playpokerattack.com' && !report.api_hosts.includes(host)) report.api_hosts.push(host);
      if (host.endsWith('.azurecontainerapps.io') || host==='pilot-api.playpokerattack.com') wrongHosts.push(host);
    });
    page.on('websocket',socket=>{
      report.sockets.push(new URL(socket.url()).hostname);
      let pending='';
      socket.on('framereceived',frame=>{
        pending+=frame.payload.toString();
        while(pending.includes('\x1e')) {
          const end=pending.indexOf('\x1e'),message=JSON.parse(pending.slice(0,end));pending=pending.slice(end+1);
          if(message.target==='ReceivePokerAttackNotification' && [12,'RoundEnded'].includes(message.arguments[0].notificationType)) report.round_receipts[index]++;
        }
      });
    });
    await page.goto(index===0?'https://playpokerattack.com':'https://www.playpokerattack.com');
    await expect(page.locator('#createLobbyBtn')).toBeVisible();
    await expect(page.locator('.session-content')).not.toHaveAttribute('inert');
    pages.push(page);
  }
  const created=pages[0].waitForResponse(response=>response.url().endsWith('/lobby/create-lobby') && response.request().method()==='POST');
  await pages[0].locator('#createLobbyBtn').click();
  const createdBody=await (await created).json();assert.equal(createdBody.isSuccess,true);
  const lobbyId=createdBody.value.id;
  await expect(pages[0].getByText(`Lobby Id: ${lobbyId}`,{exact:true})).toBeVisible();
  // Scope joining to our own lobby if a real user creates another during verification.
  const panel=pages[1].getByText(`Lobby Id: ${lobbyId}`,{exact:true}).locator('..').locator('..');
  await panel.getByRole('button',{name:'JOIN LOBBY',exact:true}).click();
  await expect(panel.getByRole('button',{name:'JOIN LOBBY',exact:true})).toHaveCount(0);
  await pages[0].getByRole('button',{name:'PLAY QUICK GAME',exact:true}).click();
  for(const page of pages) {
    await expect(page).toHaveURL(/\/multi-gameplay/);
    await expect(page.locator('.player-power-list__card').first()).toBeVisible();
    await page.getByRole('button',{name:'PICK A POWER',exact:true}).first().click();
  }
  report.game_id=new URL(pages[0].url()).searchParams.get('gameid');
  for(const page of pages) {
    await expect(page.locator('.card-hand .card')).toHaveCount(8);
    await page.locator('.card-hand .card').first().click();
    await page.locator('#playHandBtn').click();
    await expect(page.getByText('Hands Available: 4',{exact:true})).toBeVisible();
  }
  await expect.poll(()=>report.round_receipts.every(count=>count>=1),{timeout:150000}).toBe(true);
  report.multiplayer=true;
  // Close our multiplayer sessions normally; no global production cleanup.
  await Promise.all(contexts.map(context=>context.close()));contexts.length=0;
  const soloContext=await browser.newContext();contexts.push(soloContext);
  await soloContext.addInitScript(()=>localStorage.setItem('HasSeenTour','true'));
  const solo=await soloContext.newPage();await solo.goto('https://playpokerattack.com');
  await solo.locator('#playSoloBtn').click();
  await expect(solo).toHaveURL(/\/solo-gameplay/);
  await expect(solo.locator('.card-hand .card')).toHaveCount(8);
  await solo.locator('.card-hand .card').first().click();await solo.locator('#playHandBtn').click();
  await expect(solo.getByText('Hands Available: 4',{exact:true})).toBeVisible();
  await solo.locator('#endRoundBtn').click();await solo.getByRole('button',{name:'CONTINUE',exact:true}).click();
  await expect(solo.getByText('GAME OVER',{exact:true})).toBeVisible();report.solo=true;
  assert.deepEqual(wrongHosts,[]);assert.equal(report.sockets.length>=2,true);
  assert.equal(report.sockets.every(host=>host==='api.playpokerattack.com'),true);
  report.passed=true;report.completed_utc=new Date().toISOString();
}
main().catch(async error=>{
  report.passed=false;report.error=error.message;process.exitCode=1;
  for(const context of contexts) for(const page of context.pages()) await page.screenshot({path:'artifacts/production-browser-failure.png'}).catch(()=>{});
}).finally(async()=>{
  await Promise.allSettled(contexts.map(context=>context.close()));if(browser) await browser.close();
  fs.writeFileSync('artifacts/production-browser-result.json',JSON.stringify(report,null,2));console.log(JSON.stringify(report));
});
