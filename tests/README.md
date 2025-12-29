# Poker Attack E2E Tests

End-to-end multiplayer tests for Poker Attack using Playwright.

## Setup

### 1. Install Dependencies

```bash
npm install
npx playwright install chromium
```

### 2. Start Your Server

Before running tests, make sure your Poker Attack server is running:

```bash
cd Server/ChefKnifeStudios.PokerAttack.Server.WebAPI
dotnet run
```

The server should be running at `https://localhost:7150` (or configure via `API_URL` environment variable).

### 3. Run Tests

```bash
# Run all tests
npm test

# Run with headed browser (see what's happening)
npm run test:headed

# Run with Playwright UI (great for debugging)
npm run test:ui

# Debug mode (step through tests)
npm run test:debug
```

## Test Structure

```
tests/
├── e2e/
│   └── multiplayer-game.spec.ts    # Main 4-player, 3-game test
├── helpers/
│   ├── setup.ts                     # Browser context setup
│   └── actions.ts                   # UI interaction helpers
└── README.md                        # This file
```

## The Main Test

`tests/e2e/multiplayer-game.spec.ts` simulates 4 players:
1. Creating a lobby
2. Joining the lobby
3. Playing 3 consecutive games
4. Verifying completion

### Test Flow

```
GAME 1:
├── Host creates lobby
├── Players 2-4 join lobby
├── Host starts game (Quick mode)
├── Freebie round: All select powers
├── Game rounds: Play hands until winner
├── View scoreboard → Elimination
├── Game complete → Back to lobby

GAME 2 & 3:
├── Host starts new game (same lobby)
├── Repeat game flow
```

## Customization Guide

### Adjusting HTML Selectors

If your UI changes, update selectors in `tests/helpers/actions.ts`:

```typescript
// Example: Update lobby button selector
await host.page.click('#createLobbyBtn');  // Current
await host.page.click('.new-selector');    // Update as needed
```

### Customizing Gameplay

Edit the functions in `multiplayer-game.spec.ts`:

**playGameRound()**: Customize how bots play hands
```typescript
// Current: Plays all hands
// Customize: Play smarter strategy
if (currentScore < 1000) {
  await actions.discardCards(player, 3);
} else {
  await actions.playHand(player);
}
```

**handleEliminationOrShop()**: Add shop purchases
```typescript
if (isShop) {
  // Buy items based on wallet
  await actions.buyShopItem(player, 0);
}
```

### Adding Assertions

Add your own assertions throughout:

```typescript
// After playing a hand
const score = await actions.getCurrentScore(player);
expect(score).toBeGreaterThan(0);

// Verify specific game states
expect(player.page.url()).toContain('/gameplay');
```

## Available Helper Functions

### Setup Helpers (tests/helpers/setup.ts)

- `createPlayerContext(browser, id, name)` - Create isolated browser context
- `navigateToHome(player, baseUrl)` - Navigate to homepage
- `cleanupPlayers(players)` - Close all browser contexts
- `wait(ms)` - Pause execution

### Action Helpers (tests/helpers/actions.ts)

**Lobby Actions:**
- `createLobby(host)` - Host creates lobby, returns lobbyId
- `joinLobby(player, lobbyId)` - Player joins lobby
- `leaveLobby(player, lobbyId)` - Player leaves lobby
- `hostStartGame(host, lobbyId)` - Start Quick game
- `hostStartFullGame(host, lobbyId)` - Start Full game
- `hostStartNewGame(host, lobbyId)` - Start subsequent game

**Gameplay Actions:**
- `waitForGameState(player, state)` - Wait for specific state text
- `selectPlayerPower(player, index)` - Select power in freebie round
- `playHand(player)` - Select 5 cards and play
- `discardCards(player, count)` - Discard cards
- `getCurrentScore(player)` - Get current score
- `getAvailablePlayHands(player)` - Get hands remaining
- `getAvailableDiscards(player)` - Get discards remaining

**State Checks:**
- `isOnScoreboard(player)` - Check if viewing scoreboard
- `isInEliminationPhase(player)` - Check if in elimination
- `isGameComplete(player)` - Check if game ended
- `closeGameResultModal(player)` - Close win/lose modal

**Assertions:**
- `assertAllPlayersInGame(players)` - Verify all in /gameplay
- `assertOnLobbyPage(player)` - Verify on lobby page

## Troubleshooting

### Tests failing immediately

- **Check server**: Is it running at `https://localhost:7150`?
- **Check SSL**: Playwright ignores HTTPS errors by default
- **Check ports**: Update `baseURL` in `playwright.config.ts` if using different port

### Tests timing out

- Increase timeouts in `playwright.config.ts`:
  ```typescript
  use: {
    actionTimeout: 30000,  // Default: 10s
    navigationTimeout: 60000,  // Default: 30s
  }
  ```

### Can't find elements

- Run with `--headed` to see what's happening:
  ```bash
  npm run test:headed
  ```
- Use Playwright Inspector:
  ```bash
  npm run test:debug
  ```
- Check selectors in `tests/helpers/actions.ts`

### Game logic issues

- Add `wait()` calls between actions
- Check for race conditions (e.g., clicking before element is ready)
- Verify game state transitions match expectations

## CI/CD Integration

### Azure DevOps

Add to your pipeline:

```yaml
- task: NodeTool@0
  inputs:
    versionSpec: '20.x'

- script: |
    npm ci
    npx playwright install --with-deps chromium
  displayName: 'Install dependencies'

- script: npm test
  displayName: 'Run E2E tests'
  env:
    API_URL: https://localhost:7150
```

### GitHub Actions

Create `.github/workflows/e2e-tests.yml`:

```yaml
name: E2E Tests

on:
  schedule:
    - cron: '0 * * * *'  # Hourly

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '20'
      - run: npm ci
      - run: npx playwright install --with-deps chromium
      - run: npm test
```

## Tips

1. **Debugging**: Use `page.pause()` to pause execution and inspect in browser
2. **Screenshots**: Playwright auto-captures on failure (see `test-results/`)
3. **Video**: Enable in config to record all test runs
4. **Parallel**: Current config uses 1 worker for coordination - increase if splitting tests
5. **Selectors**: Prefer ID selectors (`#createLobbyBtn`) for stability

## Next Steps

- [ ] Customize gameplay strategy in `playGameRound()`
- [ ] Add shop purchases in `handleEliminationOrShop()`
- [ ] Add assertions for specific game rules
- [ ] Set up hourly CI/CD runs
- [ ] Add API-level assertions (e.g., verify game history)
- [ ] Expand to test edge cases (disconnections, timeouts)
- [ ] Add tests for different player counts (2, 3, 5+ players)

## Questions?

See [Playwright Documentation](https://playwright.dev/docs/intro) for more details on:
- [Locators](https://playwright.dev/docs/locators)
- [Assertions](https://playwright.dev/docs/test-assertions)
- [Debugging](https://playwright.dev/docs/debug)
