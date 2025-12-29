import { Browser, BrowserContext, Page } from '@playwright/test';

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

/**
 * Cleanup helper - clears all repository data on the server
 * Call this before tests to ensure a clean state
 *
 * @param clientUrl - The client URL (where the browser navigates)
 * @param apiUrl - Optional API URL (for production). If not provided, derives from clientUrl
 */
export async function cleanupServerData(clientUrl: string, apiUrl?: string) {
  console.log('Calling server cleanup endpoint to clear all data...');

  // Determine cleanup URL
  let cleanupUrl: string;
  if (apiUrl) {
    // Production: Use provided API URL
    cleanupUrl = `${apiUrl}/test/cleanup`;
  } else {
    // Local dev: Derive from client URL (port 7150 → 7333)
    const derivedApiUrl = clientUrl.replace('7150', '7333');
    cleanupUrl = `${derivedApiUrl}/test/cleanup`;
  }

  console.log(`Cleanup endpoint: ${cleanupUrl}`);

  try {
    // Temporarily disable SSL verification for self-signed certificates
    const originalValue = process.env.NODE_TLS_REJECT_UNAUTHORIZED;
    process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

    try {
      const response = await fetch(cleanupUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      if (!response.ok) {
        throw new Error(`Cleanup failed with status: ${response.status}`);
      }

      console.log('✓ Server data cleanup complete');
    } finally {
      // Restore original value
      if (originalValue !== undefined) {
        process.env.NODE_TLS_REJECT_UNAUTHORIZED = originalValue;
      } else {
        delete process.env.NODE_TLS_REJECT_UNAUTHORIZED;
      }
    }
  } catch (error) {
    console.error('Failed to cleanup server data:', error);
    throw error;
  }
}
