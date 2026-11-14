import { test, expect } from '@playwright/test';
import { cleanupServerData } from '../helpers/setup';
import { resolveTargets } from '../helpers/targets';

const targets = resolveTargets();
test.beforeEach(async () => cleanupServerData(targets.clientUrl, targets.cleanupApiUrl));

test('isolated browsers join a lobby and receive the game transition', async ({ browser }) => {
  const contexts = await Promise.all([browser.newContext(), browser.newContext()]);
  try {
    const pages = await Promise.all(contexts.map(async context => {
      await context.addInitScript(() => localStorage.setItem('HasSeenTour', 'true'));
      const page = await context.newPage();
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
      await page.locator('.card-hand .card').first().click();
      await page.locator('#playHandBtn').click();
      await expect(page.getByText('Hands Available: 4', { exact: true })).toBeVisible();
    }));
  } finally {
    await Promise.all(contexts.map(context => context.close()));
  }
});

test('solo starts and can play a selected hand', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('HasSeenTour', 'true'));
  await page.goto(targets.clientUrl);
  await page.locator('#playSoloBtn').click();
  await expect(page).toHaveURL(/\/solo-gameplay/);
  await expect(page.locator('.card-hand .card')).toHaveCount(8);
  await page.locator('.card-hand .card').first().click();
  await expect(page.locator('.card-hand .card.selected')).toHaveCount(1);
  await page.locator('#playHandBtn').click();
  await expect(page.getByText('Hands Available: 4', { exact: true })).toBeVisible();
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
