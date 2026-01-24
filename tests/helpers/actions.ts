import { expect } from '@playwright/test';
import { PlayerContext } from './setup';

/**
 * Action Helpers - Implementations based on your Blazor UI
 * These functions interact with the actual HTML elements in your web app
 */

// ============================================================================
// LOBBY ACTIONS
// ============================================================================

/**
 * Host creates a new lobby
 * Flow: Click "CREATE LOBBY" button → Extract lobby ID from accordion
 */
export async function createLobby(host: PlayerContext): Promise<string> {
  console.log(`[${host.playerName}] Creating lobby...`);

  // Click the "CREATE LOBBY" button (Id="createLobbyBtn")
  await host.page.click('#createLobbyBtn');

  // Wait for the lobby to appear in the list
  // The lobby list uses MatAccordion with lobby IDs displayed as "Lobby Id: {id}"
  await host.page.waitForSelector('text=Lobby Id:', { timeout: 10000 });

  // Extract the lobby ID from the first lobby entry
  // The text is in format "Lobby Id: {id}", extract just the ID part
  const lobbyIdLocator = host.page.locator('text=/Lobby Id: /').first();
  const lobbyIdText = await lobbyIdLocator.textContent();
  const lobbyId = lobbyIdText?.replace('Lobby Id: ', '').trim() || '';

  if (!lobbyId) {
    throw new Error('Failed to extract lobby ID after creating lobby');
  }

  console.log(`[${host.playerName}] Created lobby: ${lobbyId}`);
  return lobbyId;
}

/**
 * Player joins an existing lobby
 * Flow: Find lobby in list → Click "JOIN LOBBY" button
 */
export async function joinLobby(player: PlayerContext, lobbyId: string): Promise<void> {
  console.log(`[${player.playerName}] Joining lobby: ${lobbyId}`);

  // Wait for the lobby to appear in the list
  await player.page.waitForSelector(`text=Lobby Id: ${lobbyId}`, { timeout: 10000 });

  // Find and click the "JOIN LOBBY" button for this specific lobby
  // The button appears next to the lobby ID text
  const joinButton = player.page.locator(`text=Lobby Id: ${lobbyId}`).locator('..').locator('..').locator('button:has-text("JOIN LOBBY")');
  await joinButton.click();

  // Wait for join to complete - the JOIN LOBBY button should disappear
  await expect(joinButton).not.toBeVisible({ timeout: 10000 });

  console.log(`[${player.playerName}] Joined lobby successfully`);
}

/**
 * Player leaves the lobby
 */
export async function leaveLobby(player: PlayerContext, lobbyId: string): Promise<void> {
  console.log(`[${player.playerName}] Leaving lobby: ${lobbyId}`);

  const leaveButton = player.page.locator('button:has-text("LEAVE LOBBY")');
  await leaveButton.click();

  console.log(`[${player.playerName}] Left lobby`);
}

/**
 * Host starts the first game from lobby (Quick Mode)
 */
export async function hostStartGame(host: PlayerContext, lobbyId: string): Promise<void> {
  console.log(`[${host.playerName}] Starting game (Quick Mode)...`);

  // Click "PLAY QUICK GAME" button
  const startButton = host.page.locator('button:has-text("PLAY QUICK GAME")');
  await startButton.click();

  // Wait for navigation to /multi-gameplay (use domcontentloaded instead of load for Blazor apps)
  await host.page.waitForURL(/\/multi-gameplay(\?.*)?$/, { timeout: 15000, waitUntil: 'domcontentloaded' });

  console.log(`[${host.playerName}] Game started, navigated to gameplay page`);
}

/**
 * Host starts the first game from lobby (Full Mode)
 */
export async function hostStartFullGame(host: PlayerContext, lobbyId: string): Promise<void> {
  console.log(`[${host.playerName}] Starting game (Full Mode)...`);

  const startButton = host.page.locator('button:has-text("PLAY FULL GAME")');
  await startButton.click();

  await host.page.waitForURL(/\/multi-gameplay(\?.*)?$/, { timeout: 15000, waitUntil: 'domcontentloaded' });

  console.log(`[${host.playerName}] Full game started`);
}

/**
 * Host starts a new game (for games 2 and 3)
 * After a game ends, players return to lobby page - same flow as first game
 */
export async function hostStartNewGame(host: PlayerContext, lobbyId: string): Promise<void> {
  console.log(`[${host.playerName}] Starting new game...`);

  // Same as starting first game - use quick mode for consistency
  await hostStartGame(host, lobbyId);
}

// ============================================================================
// GAMEPLAY ACTIONS
// ============================================================================

/**
 * Wait for game to start and reach a specific state
 */
export async function waitForGameState(player: PlayerContext, expectedState: string): Promise<void> {
  console.log(`[${player.playerName}] Waiting for game state: ${expectedState}`);

  // Game states are displayed as text on the gameplay page
  // e.g., "GAME STARTING", "WAITING ON OTHER PLAYERS' SELECTION", etc.
  await player.page.waitForSelector(`text=${expectedState}`, { timeout: 30000 });

  console.log(`[${player.playerName}] Reached state: ${expectedState}`);
}

/**
 * Select a player power during Freebie phase
 */
export async function selectPlayerPower(player: PlayerContext, powerIndex: number = 0): Promise<void> {
  console.log(`[${player.playerName}] Selecting player power (index ${powerIndex})...`);

  // Wait for player powers to load
  await player.page.waitForSelector('.player-power-list__card', { timeout: 10000 });

  // Get all available power cards
  const powerCards = player.page.locator('.player-power-list__card');
  const count = await powerCards.count();

  if (count === 0) {
    throw new Error('No player powers available');
  }

  // Use provided index or select first available
  const selectedIndex = Math.min(powerIndex, count - 1);
  const powerCard = powerCards.nth(selectedIndex);

  // Click "PICK A POWER" button
  const pickButton = powerCard.locator('button:has-text("PICK A POWER")');
  await pickButton.click();

  // Wait for selection to complete - check if button becomes disabled or disappears
  await Promise.race([
    pickButton.waitFor({ state: 'hidden', timeout: 5000 }).catch(() => {}),
    pickButton.waitFor({ state: 'disabled', timeout: 5000 }).catch(() => {}),
    player.page.waitForTimeout(2000), // Fallback: just wait 2 seconds
  ]);

  console.log(`[${player.playerName}] Selected power at index ${selectedIndex}`);
}

/**
 * Play a poker hand (selects 5 cards and clicks PLAY HAND)
 */
export async function playHand(player: PlayerContext): Promise<void> {
  console.log(`[${player.playerName}] Playing hand...`);

  // Wait for game to be in InGame state
  await player.page.waitForSelector('#playHandBtn', { timeout: 10000 });

  // Select 5 cards from hand
  // Cards are rendered in CardHand component - need to click on card elements
  const cards = player.page.locator('.card-hand .card'); // Adjust selector based on actual HTML
  const cardCount = await cards.count();

  if (cardCount < 5) {
    throw new Error(`Not enough cards in hand: ${cardCount}`);
  }

  // Click first 5 cards to select them
  for (let i = 0; i < 5; i++) {
    await cards.nth(i).click();
  }

  // Click "PLAY HAND" button
  await player.page.click('#playHandBtn');

  // Wait briefly for action to complete
  await player.page.waitForTimeout(500);

  console.log(`[${player.playerName}] Played hand`);
}

/**
 * Discard cards (selects cards and clicks DISCARD)
 */
export async function discardCards(player: PlayerContext, cardCount: number = 5): Promise<void> {
  console.log(`[${player.playerName}] Discarding ${cardCount} cards...`);

  const cards = player.page.locator('.card-hand .card');
  const availableCards = await cards.count();

  const toDiscard = Math.min(cardCount, availableCards);

  // Select cards to discard
  for (let i = 0; i < toDiscard; i++) {
    await cards.nth(i).click();
  }

  // Click "DISCARD" button
  await player.page.click('#discardBtn');

  await player.page.waitForTimeout(500);

  console.log(`[${player.playerName}] Discarded ${toDiscard} cards`);
}

/**
 * Get current score from sidebar
 */
export async function getCurrentScore(player: PlayerContext): Promise<number> {
  const scoreText = await player.page.locator('text=Score:').textContent();
  const score = parseInt(scoreText?.replace('Score: ', '') || '0');
  return score;
}

/**
 * Get available play hands remaining
 */
export async function getAvailablePlayHands(player: PlayerContext): Promise<number> {
  const handsText = await player.page.locator('text=Hands Available:').textContent();
  const hands = parseInt(handsText?.replace('Hands Available: ', '') || '0');
  return hands;
}

/**
 * Get available discards remaining
 */
export async function getAvailableDiscards(player: PlayerContext): Promise<number> {
  const discardsText = await player.page.locator('text=Discards Available:').textContent();
  const discards = parseInt(discardsText?.replace('Discards Available: ', '') || '0');
  return discards;
}

/**
 * Check if player is viewing scoreboard
 */
export async function isOnScoreboard(player: PlayerContext): Promise<boolean> {
  try {
    const scoreboardVisible = await player.page.locator('text=DISPLAYING SCORES').isVisible();
    return scoreboardVisible;
  } catch (error) {
    // Page may have closed or navigated
    return false;
  }
}

/**
 * Wait for scoreboard to appear (polls until visible or timeout)
 */
export async function waitForScoreboard(player: PlayerContext, timeoutMs: number = 30000): Promise<boolean> {
  console.log(`[${player.playerName}] Waiting for scoreboard...`);

  try {
    await player.page.waitForSelector('text=DISPLAYING SCORES', {
      timeout: timeoutMs,
      state: 'visible'
    });
    console.log(`[${player.playerName}] Scoreboard appeared`);
    return true;
  } catch (error) {
    console.log(`[${player.playerName}] Scoreboard did not appear within timeout`);
    return false;
  }
}

/**
 * Check if player is in elimination phase
 */
export async function isInEliminationPhase(player: PlayerContext): Promise<boolean> {
  const eliminationVisible = await player.page.locator('text=ELIMINATING LOW SCORERS').isVisible();
  return eliminationVisible;
}

/**
 * Check if game is complete (back on lobby page with result modal)
 */
export async function isGameComplete(player: PlayerContext): Promise<boolean> {
  // Game complete when we see the result modal on the lobby page
  const wonModal = await player.page.locator('.multi-game-result-modal:has-text("You are a winner")').isVisible();
  const lostModal = await player.page.locator('.multi-game-result-modal:has-text("You are a loser")').isVisible();
  return wonModal || lostModal;
}

/**
 * Close the game result modal
 */
export async function closeGameResultModal(player: PlayerContext): Promise<void> {
  console.log(`[${player.playerName}] Closing game result modal...`);

  const closeButton = player.page.locator('.multi-game-result-modal button:has-text("CLOSE")');
  await closeButton.click();

  // Wait for modal to close
  await expect(closeButton).not.toBeVisible({ timeout: 5000 });

  console.log(`[${player.playerName}] Modal closed`);
}

// ============================================================================
// ASSERTION HELPERS
// ============================================================================

/**
 * Assert all players are on the gameplay page
 */
export async function assertAllPlayersInGame(players: PlayerContext[]): Promise<void> {
  console.log('Asserting all players in game...');

  for (const player of players) {
    // Wait for navigation to /multi-gameplay (SignalR event triggers this)
    // Use a longer timeout since SignalR events may take a moment
    await player.page.waitForURL(/\/multi-gameplay(\?.*)?$/, { timeout: 20000, waitUntil: 'domcontentloaded' });
    console.log(`  ✓ [${player.playerName}] navigated to gameplay page`);

    // Wait for game UI to be visible (any game state indicator)
    const gameStateVisible = await Promise.race([
      player.page.waitForSelector('text=GAME STARTING', { timeout: 10000 }).then(() => true).catch(() => false),
      player.page.waitForSelector('text=WAITING ON OTHER PLAYERS', { timeout: 10000 }).then(() => true).catch(() => false),
      player.page.waitForSelector('#playHandBtn', { timeout: 10000 }).then(() => true).catch(() => false),
    ]);

    expect(gameStateVisible).toBe(true);
    console.log(`  ✓ [${player.playerName}] game UI is visible`);
  }

  console.log('✓ All players confirmed in game');
}

/**
 * Assert player is back on lobby page
 */
export async function assertOnLobbyPage(player: PlayerContext): Promise<void> {
  expect(player.page.url()).toContain('/');
  expect(player.page.url()).not.toContain('/multi-gameplay');
  await expect(player.page.locator('#createLobbyBtn')).toBeVisible();
}
