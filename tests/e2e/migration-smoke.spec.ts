import { test, expect, request } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { cleanupServerData } from '../helpers/setup';
import { resolveTargets } from '../helpers/targets';

const targets = resolveTargets();
test.beforeEach(async () => cleanupServerData(targets.clientUrl, targets.cleanupApiUrl));

test('lobby actions wait until the hub has connected', async ({ page }) => {
  let allowConnection!: () => void;
  const permit = new Promise<void>(resolve => { allowConnection = resolve; });
  await page.addInitScript(() => localStorage.setItem('HasSeenTour', 'true'));
  await page.route('**/cks-notification/negotiate**', async route => { await permit; await route.continue(); });
  try {
    await page.goto(targets.clientUrl);
    await expect(page.locator('#createLobbyBtn')).toBeVisible();
    await expect(page.locator('.session-content')).toHaveAttribute('inert', '');
    allowConnection();
    await expect(page.locator('.session-content')).not.toHaveAttribute('inert');
    await page.locator('#createLobbyBtn').click();
    await expect(page.getByText(/Lobby Id:/)).toBeVisible();
  } finally { allowConnection(); }
});

test('isolated browsers join a lobby and receive the game transition', async ({ browser }) => {
  const contexts = await Promise.all([browser.newContext(), browser.newContext()]);
  const received = contexts.map(() => ({ hands: 0, cards: 0, fences: new Set<string>() }));
  try {
    const pages = await Promise.all(contexts.map(async (context, index) => {
      await context.addInitScript(() => localStorage.setItem('HasSeenTour', 'true'));
      const page = await context.newPage();
      page.on('websocket', socket => {
        let pending = '';
        socket.on('framereceived', frame => {
          pending += frame.payload.toString();
          while (pending.includes('\x1e')) {
            const end = pending.indexOf('\x1e');
            const message = JSON.parse(pending.slice(0, end));
            pending = pending.slice(end + 1);
            if (message.target !== 'ReceivePokerAttackNotification') continue;
            const notification = message.arguments[0];
            if ([10, 'HandPlayed'].includes(notification.notificationType)) received[index].hands++;
            if ([9, 'CardsDealt'].includes(notification.notificationType)) received[index].cards++;
            if ([7, 'Test'].includes(notification.notificationType)) received[index].fences.add(notification.payload);
          }
        });
      });
      await page.goto(targets.clientUrl);
      await expect(page.locator('#createLobbyBtn')).toBeVisible();
      return page;
    }));
    await pages[0].locator('#createLobbyBtn').click();
    await expect(pages[0].getByText(/Lobby Id:/)).toBeVisible();
    await expect(pages[1].getByRole('button', { name: 'JOIN LOBBY', exact: true })).toBeVisible();
    await pages[1].getByRole('button', { name: 'JOIN LOBBY', exact: true }).click();
    await expect(pages[1].getByRole('button', { name: 'JOIN LOBBY', exact: true })).toHaveCount(0);
    await pages[0].getByRole('button', { name: 'PLAY QUICK GAME', exact: true }).click();
    for (const page of pages) {
      await expect(page).toHaveURL(/\/multi-gameplay/);
      await expect(page.getByRole('alertdialog')).toHaveCount(0);
    }
    // Verify the actual gameplay components render, beyond a successful route change.
    await Promise.all(pages.map(async page => {
      await expect(page.locator('.player-power-list__card').first()).toBeVisible({ timeout: 20000 });
      await page.getByRole('button', { name: 'PICK A POWER', exact: true }).first().click();
      await expect(page.locator('.card-hand .card')).toHaveCount(8, { timeout: 30000 });
    }));
    const gameId = new URL(pages[0].url()).searchParams.get('gameid');
    expect(gameId).toBeTruthy();
    const operator = await request.newContext({ ignoreHTTPSErrors: targets.ignoreHttpsErrors });
    try {
      for (let actor = 0; actor < pages.length; actor++) {
        const other = 1 - actor;
        const before = received.map(state => ({ hands: state.hands, cards: state.cards }));
        await pages[actor].locator('.card-hand .card').first().click();
        await pages[actor].locator('#playHandBtn').click();
        await expect.poll(() => received[actor].hands).toBe(before[actor].hands + 1);
        await expect(pages[actor].getByText('Hands Available: 4', { exact: true })).toBeVisible();
        const fence = randomUUID();
        const response = await operator.post(`${targets.cleanupApiUrl}/test/signal-r`, {
          data: { gameId, message: fence }, maxRedirects: 0,
        });
        expect(response.ok()).toBe(true);
        // A later group message fences earlier per-connection sends: no guessed sleep.
        await expect.poll(() => received.every(state => state.fences.has(fence))).toBe(true);
        expect(received[actor].cards).toBe(before[actor].cards + 1);
        expect(received[other].hands).toBe(before[other].hands);
        expect(received[other].cards).toBe(before[other].cards);
      }
    } finally { await operator.dispose(); }
  } finally {
    await Promise.all(contexts.map(context => context.close()));
  }
});

test('lobbies created during connection or snapshot loading remain joinable', async ({ browser }) => {
  const contexts = await Promise.all([browser.newContext(), browser.newContext(), browser.newContext()]);
  let connect!: () => void;
  let snapshot!: () => void;
  let captured!: () => void;
  const connectionPermit = new Promise<void>(resolve => { connect = resolve; });
  const snapshotPermit = new Promise<void>(resolve => { snapshot = resolve; });
  const snapshotCaptured = new Promise<void>(resolve => { captured = resolve; });
  try {
    for (const context of contexts)
      await context.addInitScript(() => localStorage.setItem('HasSeenTour', 'true'));
    const pages = await Promise.all(contexts.map(context => context.newPage()));
    await pages[1].route('**/cks-notification/negotiate**', async route => {
      await connectionPermit;
      await route.continue();
    });
    await pages[2].route('**/lobby', async route => {
      const response = await route.fetch();
      captured();
      await snapshotPermit;
      await route.fulfill({ response });
    });
    await Promise.all(pages.map(page => page.goto(targets.clientUrl)));
    await snapshotCaptured;
    await expect(pages[1].locator('.session-content')).toHaveAttribute('inert', '');
    await expect(pages[2].locator('.session-content')).not.toHaveAttribute('inert');
    await pages[0].locator('#createLobbyBtn').click();
    await expect(pages[0].getByText(/Lobby Id:/)).toBeVisible();
    connect();
    snapshot();
    for (const page of pages.slice(1)) {
      await expect(page.getByRole('button', { name: 'JOIN LOBBY', exact: true })).toHaveCount(1);
      await page.getByRole('button', { name: 'JOIN LOBBY', exact: true }).click();
      await expect(page.getByRole('button', { name: 'JOIN LOBBY', exact: true })).toHaveCount(0);
    }
  } finally {
    connect();
    snapshot();
    await Promise.all(contexts.map(context => context.close()));
  }
});

test('solo plays a hand and completes a run with a result', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('HasSeenTour', 'true'));
  await page.goto(targets.clientUrl);
  await page.locator('#playSoloBtn').click();
  await expect(page).toHaveURL(/\/solo-gameplay/);
  await expect(page.locator('.card-hand .card')).toHaveCount(8);
  await page.locator('.card-hand .card').first().click();
  await expect(page.locator('.card-hand .card.selected')).toHaveCount(1);
  await page.locator('#playHandBtn').click();
  await expect(page.getByText('Hands Available: 4', { exact: true })).toBeVisible();
  await page.locator('#endRoundBtn').click();
  await expect(page.getByText('You did not meet the threshold...', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'CONTINUE', exact: true }).click();
  await expect(page).not.toHaveURL(/\/solo-gameplay/);
  await expect(page.getByText('GAME OVER', { exact: true })).toBeVisible();
});

test('failed hub connection exposes recovery and reload creates a fresh playable page', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('HasSeenTour', 'true'));
  await page.route('**/cks-notification/**', route => route.abort());
  await page.goto(targets.clientUrl);
  await expect(page.getByRole('alertdialog')).toBeVisible();
  await expect(page.locator('.session-content')).toHaveAttribute('inert', '');
  await page.unroute('**/cks-notification/**');
  await page.getByRole('button', { name: 'Reload and return home' }).click();
  await expect(page.getByRole('alertdialog')).toHaveCount(0);
  await page.locator('#createLobbyBtn').click();
  await expect(page.getByText(/Lobby Id:/)).toBeVisible();
});
