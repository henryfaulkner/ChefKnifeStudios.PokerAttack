import { Browser, BrowserContext, Page, request } from '@playwright/test';
import { resolveTargets, validateServedApis } from './targets';

/**
 * Setup helper for creating isolated browser contexts for each player
 */

export interface PlayerContext {
  playerId: string;
  playerName: string;
  context: BrowserContext;
  page: Page;
}

/**
 * Creates a new browser context and page for a player
 * This simulates a completely separate user with their own browser session
 */
export async function createPlayerContext(
  browser: Browser,
  playerId: string,
  playerName: string
): Promise<PlayerContext> {
  // Create isolated browser context (separate cookies, localStorage, etc.)
  const context = await browser.newContext({
    // Each player gets their own storage state
    storageState: undefined,
    ignoreHTTPSErrors: resolveTargets().ignoreHttpsErrors,
    // Viewport settings (optional - adjust as needed)
    viewport: { width: 1280, height: 720 },
    // Each context is completely isolated
    permissions: [],
  });

  // Create a new page in this context
  const page = await context.newPage();

  // Optional: Set up console/error logging for debugging
  page.on('console', msg => {
    if (msg.type() === 'error') {
      console.log(`[${playerName}] Console Error:`, msg.text());
    }
  });

  page.on('pageerror', error => {
    console.log(`[${playerName}] Page Error:`, error.message);
  });

  return {
    playerId,
    playerName,
    context,
    page,
  };
}

/**
 * Navigates a player to the game homepage
 * Customize the URL path based on your app's routing
 */
export async function navigateToHome(playerContext: PlayerContext, baseUrl: string) {
  const { page, playerName } = playerContext;

  console.log(`[${playerName}] Navigating to ${baseUrl}`);
  await page.goto(baseUrl);

  // Wait for the page to be fully loaded
  await page.waitForLoadState('networkidle');
}

/**
 * Cleanup helper - closes all player contexts
 */
export async function cleanupPlayers(players: PlayerContext[]) {
  console.log('Cleaning up player contexts...');

  for (const player of players) {
    await player.context.close();
  }
}

/**
 * Wait helper - pauses execution (useful for debugging or waiting between actions)
 */
export async function wait(ms: number) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

/** Clean only an explicitly selected isolated server after checking served client settings. */
export async function cleanupServerData(clientUrl: string, apiUrl?: string) {
  const targets = resolveTargets({ ...process.env, CLIENT_URL: clientUrl, API_URL_CLEANUP: apiUrl });
  const context = await request.newContext({ ignoreHTTPSErrors: targets.ignoreHttpsErrors });
  try {
    const settings = await context.get(`${targets.clientUrl}/appsettings.json`, { maxRedirects: 0 });
    if (!settings.ok()) throw new Error('Cannot verify served client settings; cleanup refused.');
    const base = await settings.json();
    const environment = settings.headers()['blazor-environment'] || 'Production';
    if (!/^[A-Za-z0-9_.-]{1,64}$/.test(environment)) throw new Error('Invalid served environment header; cleanup refused.');
    const override = await context.get(`${targets.clientUrl}/appsettings.${environment}.json`, { maxRedirects: 0 });
    const overrideSettings = override.ok() && (override.headers()['content-type'] || '').includes('application/json')
      ? await override.json() : null;
    validateServedApis(base, overrideSettings, targets);
    const response = await context.post(`${targets.cleanupApiUrl}/test/cleanup`, { maxRedirects: 0 });
    if (!response.ok()) throw new Error(`Isolated cleanup failed: HTTP ${response.status()}`);
    console.log('Isolated server cleanup complete.');
  } finally {
    await context.dispose();
  }
}
