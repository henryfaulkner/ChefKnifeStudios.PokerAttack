import { test, expect } from '@playwright/test';
import { createPlayerContext, navigateToHome, cleanupPlayers, cleanupServerData, wait, PlayerContext } from '../helpers/setup';
import * as actions from '../helpers/actions';

/**
 * E2E Multiplayer Test - 4 Players, 3 Consecutive Games
 *
 * This test simulates 4 real users playing 3 complete games back-to-back.
 * Flow: Create Lobby → All Join → Play Game 1 → Play Game 2 → Play Game 3 → End
 */

test.describe('4-Player Multiplayer Game - 3 Consecutive Games', () => {
  test('should complete 3 full games with 4 players', async ({ browser }) => {
    // Get URLs from environment variables
    // CLIENT_URL: Where the browser navigates (frontend)
    // API_URL_CLEANUP: Where the cleanup endpoint is (backend API)
    // For backward compatibility, fall back to API_URL for local dev
    const clientUrl = process.env.CLIENT_URL || process.env.API_URL || 'https://localhost:7150';
    const apiUrl = process.env.API_URL_CLEANUP; // Optional - for production

    console.log(`Client URL (browser navigation): ${clientUrl}`);
    if (apiUrl) {
      console.log(`API URL (cleanup endpoint): ${apiUrl}`);
    } else {
      console.log('API URL: Deriving from client URL (local dev mode)');
    }

    // ============================================================================
    // ARRANGE - Setup Phase (handled for you)
    // ============================================================================
    console.log('\n=== SETUP: Cleaning up server data ===');
    await cleanupServerData(clientUrl, apiUrl);

    console.log('\n=== SETUP: Creating 4 player contexts ===');

    const players: PlayerContext[] = [];

    // Create 4 separate browser contexts (simulating 4 different users)
    players.push(await createPlayerContext(browser, 'player-1', 'Host Player'));
    players.push(await createPlayerContext(browser, 'player-2', 'Player Two'));
    players.push(await createPlayerContext(browser, 'player-3', 'Player Three'));
    players.push(await createPlayerContext(browser, 'player-4', 'Player Four'));

    console.log(`✓ Created ${players.length} player contexts`);

    // Navigate all players to the home page
    console.log('\n=== SETUP: Navigating players to home page ===');
    await Promise.all(
      players.map(player => navigateToHome(player, clientUrl))
    );
    console.log('✓ All players navigated to home page');

    // Shared state for test coordination
    let lobbyId: string | null = null;

    try {
      // ============================================================================
      // ACT & ASSERT - Game Loop (you implement this)
      // ============================================================================

      const TOTAL_GAMES = 3;

      for (let gameNumber = 1; gameNumber <= TOTAL_GAMES; gameNumber++) {
        console.log(`\n\n${'='.repeat(80)}`);
        console.log(`GAME ${gameNumber} of ${TOTAL_GAMES}`);
        console.log('='.repeat(80));

        // --------------------------------------------------------------------
        // Phase 1: Lobby Creation & Joining
        // --------------------------------------------------------------------
        if (gameNumber === 1) {
          console.log('\n--- PHASE 1: Creating and Joining Lobby ---');

          // TODO: Implement lobby creation
          lobbyId = await createLobby(players[0]);

          // TODO: Implement joining lobby for players 2, 3, 4
          await joinLobby(players[1], lobbyId);
          await joinLobby(players[2], lobbyId);
          await joinLobby(players[3], lobbyId);

          console.log(`✓ All players joined lobby: ${lobbyId}`);
        } else {
          console.log('\n--- PHASE 1: Starting New Game (same lobby) ---');
          // For games 2 and 3, players are already in the lobby
          await hostStartNewGame(players[0], lobbyId);
        }

        // --------------------------------------------------------------------
        // Phase 2: Game Start
        // --------------------------------------------------------------------
        console.log('\n--- PHASE 2: Starting Game ---');

        if (gameNumber === 1) {
          // Host starts the first game
          await hostStartGame(players[0], lobbyId);
        }

        // TODO: Add assertions that all players see the game started
        await assertAllPlayersInGame(players);

        // --------------------------------------------------------------------
        // Phase 3: Play Through Game States
        // --------------------------------------------------------------------
        console.log('\n--- PHASE 3: Playing Game ---');

        // TODO: Implement gameplay for each state
        // Your game flow: Freebie → InGame → Scoreboard → Elimination/Shop → repeat

        // Freebie Round
        await playFreebieRound(players);

        // Main Game Rounds (until winner)
        let gameComplete = false;
        let roundNumber = 1;
        const MAX_ROUNDS = 20; // Safety limit to prevent infinite loops

        while (!gameComplete && roundNumber <= MAX_ROUNDS) {
          console.log(`\n  Round ${roundNumber}:`);

          // Play round
          await playGameRound(players, roundNumber);

          // Check scoreboard
          await viewScoreboard(players);

          // Check if game is complete (1 player remaining)
          gameComplete = await checkGameComplete(players);

          if (!gameComplete) {
            // Handle elimination or shop phase
            await handleEliminationOrShop(players, roundNumber);

            // Check again after elimination phase - game might have ended during elimination
            gameComplete = await checkGameComplete(players);
            if (gameComplete) {
              console.log('  ✓ Game completed during elimination phase');
            }
          }

          roundNumber++;
        }

        if (!gameComplete) {
          console.log(`  ⚠ Warning: Reached max rounds (${MAX_ROUNDS}) without game completion`);
        }

        // --------------------------------------------------------------------
        // Phase 4: Game End
        // --------------------------------------------------------------------
        console.log('\n--- PHASE 4: Game Complete ---');

        // TODO: Add assertions for game completion
        await assertGameComplete(players, gameNumber);

        console.log(`✓ Game ${gameNumber} completed successfully`);

        // Brief pause between games
        if (gameNumber < TOTAL_GAMES) {
          console.log('\nWaiting before next game...');
          await wait(2000);
        }
      }

      // ============================================================================
      // Final Assertions
      // ============================================================================
      console.log('\n\n' + '='.repeat(80));
      console.log('ALL GAMES COMPLETE - Running Final Assertions');
      console.log('='.repeat(80));

      // TODO: Add final assertions (e.g., verify 3 games were recorded)
      await assertThreeGamesCompleted(players);

      console.log('\n✓ Test completed successfully!');

    } finally {
      // ============================================================================
      // CLEANUP - Teardown Phase (handled for you)
      // ============================================================================
      console.log('\n=== CLEANUP: Closing browser contexts ===');
      await cleanupPlayers(players);
      console.log('✓ Cleanup complete');
    }
  });
});

// ============================================================================
// Action Functions - Customize these based on your gameplay needs
// ============================================================================

/**
 * Host creates a new lobby
 */
async function createLobby(host: PlayerContext): Promise<string> {
  return await actions.createLobby(host);
}

/**
 * Player joins an existing lobby
 */
async function joinLobby(player: PlayerContext, lobbyId: string): Promise<void> {
  await actions.joinLobby(player, lobbyId);
}

/**
 * Host starts the first game from lobby
 */
async function hostStartGame(host: PlayerContext, lobbyId: string): Promise<void> {
  await actions.hostStartGame(host, lobbyId);
}

/**
 * Host starts a new game (for games 2 and 3)
 */
async function hostStartNewGame(host: PlayerContext, lobbyId: string): Promise<void> {
  // Ensure any result modals are closed before starting new game
  const hasModal = await actions.isGameComplete(host);
  if (hasModal) {
    console.log(`  [${host.playerName}] Closing lingering result modal before starting new game...`);
    await actions.closeGameResultModal(host);
    await wait(1000); // Wait for modal to fully close
  }

  await actions.hostStartNewGame(host, lobbyId);
}

/**
 * Assert all players see the game has started
 */
async function assertAllPlayersInGame(players: PlayerContext[]): Promise<void> {
  await actions.assertAllPlayersInGame(players);
}

/**
 * Play the freebie round
 * TODO: Customize this based on your game requirements
 */
async function playFreebieRound(players: PlayerContext[]): Promise<void> {
  console.log('  Playing freebie round...');

  // Wait for freebie state
  await actions.waitForGameState(players[0], 'WAITING ON OTHER PLAYERS');

  // Each player selects a power
  await Promise.all(
    players.map((player, index) => actions.selectPlayerPower(player, index % 3))
  );

  console.log('  ✓ All players selected powers');

  // Wait for game to transition to InGame state after all powers selected
  console.log('  Waiting for game to transition to InGame state...');
  await wait(3000); // Give time for SignalR events to propagate
}

/**
 * Play a game round
 * TODO: Customize gameplay strategy (currently plays all hands randomly)
 */
async function playGameRound(players: PlayerContext[], roundNumber: number): Promise<void> {
  console.log(`  Playing round ${roundNumber}...`);

  // Wait for round to start
  console.log('    Waiting for round to be ready...');
  await wait(3000);

  // Determine which players are still active (not eliminated)
  const activePlayers: PlayerContext[] = [];
  const eliminatedPlayers: PlayerContext[] = [];

  for (const player of players) {
    try {
      // Check if player is on gameplay page (might have been eliminated and sent to lobby)
      const url = player.page.url();
      if (!url.includes('/gameplay')) {
        console.log(`    [${player.playerName}] Not on gameplay page - eliminated or game ended`);
        eliminatedPlayers.push(player);
        continue;
      }

      // Check if play hand button exists
      const hasPlayButton = await player.page.locator('#playHandBtn').isVisible({ timeout: 5000 }).catch(() => false);
      if (hasPlayButton) {
        activePlayers.push(player);
        console.log(`    [${player.playerName}] Active in round ${roundNumber}`);
      } else {
        eliminatedPlayers.push(player);
        console.log(`    [${player.playerName}] Eliminated - no play button`);
      }
    } catch (error) {
      console.log(`    [${player.playerName}] Error checking status: ${error}`);
      eliminatedPlayers.push(player);
    }
  }

  console.log(`    ${activePlayers.length} active players, ${eliminatedPlayers.length} eliminated`);

  // Only active players play hands IN PARALLEL
  if (activePlayers.length > 0) {
    await Promise.all(
      activePlayers.map(async (player) => {
        try {
          // Play hands until we run out
          let handsRemaining = await actions.getAvailablePlayHands(player);

          while (handsRemaining > 0) {
            await actions.playHand(player);
            await wait(500);
            handsRemaining = await actions.getAvailablePlayHands(player);
          }

          console.log(`    ✓ [${player.playerName}] finished playing`);
        } catch (error) {
          console.log(`    [${player.playerName}] stopped playing: ${error}`);
        }
      })
    );
  } else {
    console.log('    No active players - round skipped or game ended');
  }
}

/**
 * View the scoreboard after a round
 */
async function viewScoreboard(players: PlayerContext[]): Promise<void> {
  console.log('  Viewing scoreboard...');

  // Wait for scoreboard to appear (rounds have time limits, so we need to wait)
  // Timeout should be longer than the round duration
  const scoreboardAppeared = await actions.waitForScoreboard(players[0], 60000); // 60 second timeout

  if (scoreboardAppeared) {
    await wait(3000); // View scoreboard for 3 seconds
    console.log('  ✓ Scoreboard viewed');
  } else {
    console.log('  ⚠ Scoreboard did not appear - game may have ended');
  }
}

/**
 * Check if the game is complete
 */
async function checkGameComplete(players: PlayerContext[]): Promise<boolean> {
  console.log('  Checking if game is complete...');

  // Wait for game state to update and SignalR to propagate
  await wait(5000);

  // Count how many players are on lobby vs gameplay
  let playersOnLobby = 0;
  let playersInGame = 0;

  for (const player of players) {
    try {
      const url = player.page.url();
      if (url.includes('/gameplay')) {
        playersInGame++;
        console.log(`  [${player.playerName}] still on gameplay page`);
      } else {
        playersOnLobby++;
        console.log(`  [${player.playerName}] on lobby page`);
      }
    } catch (error) {
      console.log(`  [${player.playerName}] error checking URL: ${error}`);
    }
  }

  // Game is complete ONLY if ALL players are back on lobby
  // Note: Eliminated players may have result modals but game continues for others
  const allPlayersOnLobby = playersOnLobby === players.length;

  if (allPlayersOnLobby) {
    console.log(`  ✓ Game is complete! (All ${playersOnLobby} players on lobby)`);
    return true;
  }

  if (playersOnLobby > 0) {
    console.log(`  ${playersOnLobby} player(s) on lobby (eliminated), ${playersInGame} still playing - game continues`);
  }

  console.log('  Game continues to next round');
  return false;
}

/**
 * Handle elimination or shop phase
 * TODO: Add shop purchases if needed
 */
async function handleEliminationOrShop(players: PlayerContext[], roundNumber: number): Promise<void> {
  console.log('  Handling elimination/shop phase...');

  // Check if we're in elimination phase
  const isElimination = await actions.isInEliminationPhase(players[0]);

  if (isElimination) {
    console.log('  Elimination phase - waiting...');
    await wait(5000); // Wait for elimination to complete
  }

  // TODO: Add shop logic here if in Full game mode
  // const isShop = await player.page.locator('text=SHOP').isVisible();
  // if (isShop) { ... }
}

/**
 * Assert a single game is complete
 */
async function assertGameComplete(players: PlayerContext[], gameNumber: number): Promise<void> {
  console.log(`Asserting game ${gameNumber} complete...`);

  // Handle result modals and ensure all players are back on lobby page
  for (const player of players) {
    try {
      const currentUrl = player.page.url();

      // If still on gameplay page, wait for navigation to lobby
      if (currentUrl.includes('/gameplay')) {
        console.log(`  [${player.playerName}] Still on gameplay, waiting for navigation...`);
        await player.page.waitForURL(/\/$/, { timeout: 20000, waitUntil: 'domcontentloaded' }).catch((err) => {
          console.log(`  ⚠ [${player.playerName}] Navigation timeout: ${err.message}`);
        });
      } else {
        console.log(`  [${player.playerName}] Already on lobby page`);
      }

      // Give UI time to render modal if present
      await wait(1000);

      // Check for result modal and close it
      const hasModal = await actions.isGameComplete(player);
      if (hasModal) {
        console.log(`  [${player.playerName}] Closing result modal...`);
        await actions.closeGameResultModal(player);
        console.log(`  ✓ [${player.playerName}] Modal closed`);
      } else {
        console.log(`  [${player.playerName}] No modal found`);
      }

      // Verify on lobby page
      await actions.assertOnLobbyPage(player);
      console.log(`  ✓ [${player.playerName}] Confirmed on lobby page`);
    } catch (error) {
      console.error(`  ✗ [${player.playerName}] Error during game completion: ${error}`);
      // Take screenshot for debugging
      await player.page.screenshot({ path: `test-results/game${gameNumber}-${player.playerName}-error.png` });
      throw error; // Re-throw to fail the test
    }
  }

  console.log('  ✓ All players back on lobby page');

  // Wait for lobby to update (GameId cleared) before starting next game
  console.log('  Waiting for lobby state to update...');
  await wait(3000);
}

/**
 * Final assertion that 3 games were completed
 * TODO: Add more robust assertions (e.g., check game history API)
 */
async function assertThreeGamesCompleted(players: PlayerContext[]): Promise<void> {
  console.log('Asserting 3 games completed...');

  // For now, just verify all players are on lobby page
  for (const player of players) {
    await actions.assertOnLobbyPage(player);
  }

  // TODO: Add API call to verify game history
  // const response = await fetch(`${baseUrl}/api/game-history`);
  // const games = await response.json();
  // expect(games.length).toBeGreaterThanOrEqual(3);

  console.log('  ✓ Test completed - all players on lobby page');
}
