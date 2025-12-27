# How to Use AI Agents in PokerAttack

## Table of Contents
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Single Agent Testing](#single-agent-testing)
- [Multi-Agent Testing](#multi-agent-testing)
- [Common Use Cases](#common-use-cases)
- [Monitoring & Debugging](#monitoring--debugging)
- [Troubleshooting](#troubleshooting)
- [Best Practices](#best-practices)
- [FAQ](#faq)

---

## Quick Start

### 5-Minute Setup

1. **Ensure the app is running**
   ```bash
   # Start the server and client (from solution root)
   dotnet run
   ```

2. **Open browser to lobbies page with autoPlay parameter**
   ```
   https://localhost:5001/?autoPlay=true
   ```

3. **Watch the agent play**
   - Open browser console (F12) to see agent logs
   - Agent will automatically join/create lobby and play

That's it! The agent is now playing autonomously.

---

## Configuration

### Basic Configuration

Edit `Client.WebApp/wwwroot/appsettings.json`:

```json
{
  "AgentSettings": {
    "Enabled": false,
    "MinThinkTimeMs": 500,
    "MaxThinkTimeMs": 1500,
    "BrainType": "Random",
    "AutoJoinLobby": true,
    "AutoCreateLobby": false,
    "MaxPlayersBeforeStart": 4,
    "AutoStartGameAsHost": true,
    "DefaultGameMode": "Quick"
  }
}
```

### Configuration Options Explained

| Setting | Default | Description | Recommended Values |
|---------|---------|-------------|-------------------|
| `Enabled` | `false` | Global enable/disable switch | `false` (safety), `true` (active use) |
| `MinThinkTimeMs` | `500` | Minimum delay between actions | `200` (fast), `500` (normal), `1000` (slow) |
| `MaxThinkTimeMs` | `1500` | Maximum delay between actions | `1000` (fast), `1500` (normal), `3000` (slow) |
| `BrainType` | `"Random"` | Which decision-making brain to use | `"Random"` (only option currently) |
| `AutoJoinLobby` | `true` | Automatically join available lobbies | `true` (recommended) |
| `AutoCreateLobby` | `false` | Create lobby if none available | `false` (multi-agent), `true` (solo testing) |
| `MaxPlayersBeforeStart` | `4` | How many players before host starts game | `2-8` (depends on test needs) |
| `AutoStartGameAsHost` | `true` | Auto-start game when host | `true` (automated), `false` (manual control) |
| `DefaultGameMode` | `"Quick"` | Game mode for agent-started games | `"Quick"`, `"Standard"` |

### Configuration Scenarios

#### Scenario 1: Fast Automated Testing
```json
{
  "MinThinkTimeMs": 200,
  "MaxThinkTimeMs": 800,
  "AutoJoinLobby": true,
  "AutoCreateLobby": false,
  "MaxPlayersBeforeStart": 2
}
```
**Use Case**: Quick smoke tests, CI/CD integration

#### Scenario 2: Human-Like Behavior Testing
```json
{
  "MinThinkTimeMs": 800,
  "MaxThinkTimeMs": 2500,
  "AutoJoinLobby": true,
  "AutoCreateLobby": false,
  "MaxPlayersBeforeStart": 4
}
```
**Use Case**: UX testing, realistic gameplay simulation

#### Scenario 3: Solo Agent Development
```json
{
  "MinThinkTimeMs": 1000,
  "MaxThinkTimeMs": 2000,
  "AutoJoinLobby": false,
  "AutoCreateLobby": true,
  "MaxPlayersBeforeStart": 1
}
```
**Use Case**: Debugging agent logic, brain development

---

## Single Agent Testing

### Method 1: URL Parameter (Recommended)

**Simplest method** - No configuration changes needed.

1. Start the application
2. Navigate to: `https://localhost:5001/?autoPlay=true`
3. Agent activates automatically

**Advantages**:
- Quick to enable/disable
- No file changes required
- Easy to share test URLs

**Example URLs**:
```
# Basic agent
https://localhost:5001/?autoPlay=true

# Agent with game result parameter
https://localhost:5001/?autoPlay=true&gameResult=winner
```

### Method 2: Configuration File

1. Edit `appsettings.json`:
   ```json
   "AgentSettings": {
     "Enabled": true
   }
   ```

2. Navigate to: `https://localhost:5001/`

3. Agent activates automatically

**Advantages**:
- Persistent across sessions
- Don't need to remember URL parameter

### Monitoring Single Agent

**Browser Console** (F12 → Console tab):
```
[LobbyAutomation] Starting automation for player abc-123
[LobbyAutomation] Joining lobby xyz-456 with 2 players
[AutoPilot] Enabled for player abc-123
[AutoPilot] Run started, beginning action loop
[AutoPilot] Playing hand with 3 cards
[AutoPilot] Discarding 2 cards
[AutoPilot] Activating player power
[AutoPilot] Round ended, stopping action loop
```

**What to Look For**:
- ✅ `[LobbyAutomation] Starting automation` - Agent started
- ✅ `[AutoPilot] Enabled` - Gameplay automation ready
- ✅ `[AutoPilot] Playing hand` - Agent making moves
- ❌ Errors or warnings - Check troubleshooting section

---

## Multi-Agent Testing

### Method 1: Multiple Browser Windows (Manual)

**Best for**: 2-4 agents, visual debugging

1. Open browser window #1: `https://localhost:5001/?autoPlay=true`
2. Open browser window #2: `https://localhost:5001/?autoPlay=true`
3. Open browser window #3: `https://localhost:5001/?autoPlay=true`
4. Watch agents interact

**Tips**:
- Use different browser profiles to simulate different users
- Arrange windows side-by-side to monitor all agents
- Use incognito/private windows for isolated sessions

### Method 2: Playwright Orchestrator (Automated)

**Best for**: 4+ agents, automated testing, CI/CD

#### Prerequisites
```bash
cd src
npm install playwright
```

#### Basic Usage
```bash
node test-orchestrator.js
```

#### Customizing Test Configuration

Edit `test-orchestrator.js`:

```javascript
const CONFIG = {
    baseUrl: 'https://localhost:5001',  // Your server URL
    numAgents: 4,                       // Number of agents to launch
    headless: false,                    // false = see browsers, true = background
    gameTimeoutMs: 600000,              // Max game duration (10 min)
    screenshotOnError: true,            // Capture screenshots on failure
    videoPath: './test-videos'          // Video recording directory
};
```

#### Running Different Test Scenarios

**Quick 2-Agent Test**:
```javascript
numAgents: 2,
gameTimeoutMs: 120000  // 2 minutes
```

**Stress Test (8 Agents)**:
```javascript
numAgents: 8,
headless: true,        // Run in background
gameTimeoutMs: 900000  // 15 minutes
```

**CI/CD Integration**:
```javascript
numAgents: 4,
headless: true,
screenshotOnError: true,
videoPath: './artifacts/test-videos'
```

#### Playwright Output

Console output shows all agent activity:
```
[Orchestrator] Starting multi-agent test with 4 agents
[Orchestrator] Base URL: https://localhost:5001
[Orchestrator] Launching agent 1...
[Agent 1] [LobbyAutomation] Starting automation for player...
[Agent 1] [AutoPilot] Playing hand with 4 cards
[Agent 2] [AutoPilot] Discarding 1 cards
[Agent 3] [AutoPilot] Activating player power
[Orchestrator] All agents returned to lobby - game complete!
[Orchestrator] ✓ Multi-agent test completed successfully
```

---

## Common Use Cases

### Use Case 1: Automated Regression Testing

**Goal**: Verify game mechanics work after code changes

**Setup**:
```json
{
  "MinThinkTimeMs": 200,
  "MaxThinkTimeMs": 500,
  "MaxPlayersBeforeStart": 2
}
```

**Process**:
1. Make code changes
2. Run `node test-orchestrator.js`
3. Check for errors in console/Sentry
4. Verify game completes successfully

**Success Criteria**:
- No JavaScript errors
- No Sentry exceptions
- Agents complete full game
- No UI freezes or crashes

---

### Use Case 2: Load Testing

**Goal**: Test server performance with many concurrent players

**Setup**:
```javascript
// test-orchestrator.js
numAgents: 16,  // Or more
headless: true
```

**Process**:
1. Monitor server metrics (CPU, memory, network)
2. Launch 16+ agents
3. Observe server performance during gameplay
4. Check for SignalR connection issues

**Metrics to Monitor**:
- Server CPU usage
- Memory consumption
- SignalR message throughput
- Database query performance
- Average response times

---

### Use Case 3: Race Condition Detection

**Goal**: Find sync issues in multiplayer state management

**Setup**:
```json
{
  "MinThinkTimeMs": 100,   // Very fast actions
  "MaxThinkTimeMs": 200,
  "MaxPlayersBeforeStart": 8
}
```

**Process**:
1. Launch 8 agents
2. Monitor for state inconsistencies
3. Check Sentry for exceptions
4. Review server logs for errors

**Common Issues to Detect**:
- Players seeing different game states
- Cards dealt incorrectly
- Score calculation errors
- Elimination timing issues

---

### Use Case 4: UX/Performance Testing

**Goal**: Ensure UI remains responsive under load

**Setup**:
```json
{
  "MinThinkTimeMs": 500,
  "MaxThinkTimeMs": 1500,
  "MaxPlayersBeforeStart": 4
}
```

**Process**:
1. Launch agents in visible browser windows
2. Watch UI rendering performance
3. Check for animation jank
4. Monitor network requests

**What to Check**:
- Smooth animations
- Quick state updates
- No layout shifts
- Responsive controls

---

### Use Case 5: Continuous Integration

**Goal**: Automated testing in CI/CD pipeline

**GitHub Actions Example**:
```yaml
name: Agent Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - uses: actions/setup-node@v2
      - name: Install Playwright
        run: |
          cd src
          npm install playwright
      - name: Start Server
        run: dotnet run &
      - name: Wait for Server
        run: sleep 30
      - name: Run Agent Tests
        run: node src/test-orchestrator.js
      - name: Upload Screenshots
        if: failure()
        uses: actions/upload-artifact@v2
        with:
          name: test-screenshots
          path: test-screenshots/
```

---

## Monitoring & Debugging

### Browser Console Logs

**Enable Verbose Logging**:
1. Open DevTools (F12)
2. Go to Console tab
3. Filter for `[AutoPilot]` or `[LobbyAutomation]`

**Log Levels**:
- **Info**: Normal operations (Playing hand, Joining lobby)
- **Warning**: Recoverable issues (Action not implemented)
- **Error**: Failures (Network errors, exceptions)

### Sentry Dashboard

**Finding Agent Events**:
1. Go to Sentry dashboard
2. Filter by tag: `user_type:agent`
3. Review errors and performance

**Common Agent Errors**:
- SignalR disconnection
- State transition failures
- Action validation errors

### Network Tab Monitoring

**What to Monitor**:
1. Open DevTools → Network tab
2. Filter for `SignalR` or `websocket`
3. Watch for:
   - Connection status
   - Message frequency
   - Failed requests

### Performance Monitoring

**Browser Performance Tab**:
1. Open DevTools → Performance tab
2. Record during agent gameplay
3. Look for:
   - Long tasks (>50ms)
   - Layout thrashing
   - Memory leaks

---

## Troubleshooting

### Problem: Agent Doesn't Start

**Symptoms**:
- Navigating to `/?autoPlay=true` does nothing
- No console logs

**Solutions**:
1. ✅ Check `appsettings.json` has `AgentSettings` section
2. ✅ Verify URL has `?autoPlay=true` parameter
3. ✅ Open browser console for error messages
4. ✅ Ensure SignalR connection is established
5. ✅ Check server is running

**Diagnostic Commands**:
```javascript
// In browser console, check if services are registered
console.log(typeof AutoPilotService); // Should not be 'undefined'
```

---

### Problem: Agent Stuck in Lobby

**Symptoms**:
- Agent joins lobby but game never starts
- Logs show "Joining lobby" but nothing after

**Solutions**:
1. ✅ Check `MaxPlayersBeforeStart` setting
2. ✅ Verify `AutoStartGameAsHost: true` if agent is host
3. ✅ Ensure enough agents joined lobby
4. ✅ Check if lobby is full

**Manual Override**:
- Click "Start Game" button manually
- Set `MaxPlayersBeforeStart: 1` for solo testing

---

### Problem: Agent Not Taking Actions

**Symptoms**:
- Game starts but agent doesn't play hands
- No "[AutoPilot] Playing hand" logs

**Solutions**:
1. ✅ Check console for errors
2. ✅ Verify `RunStarted` notification received
3. ✅ Check if cards are dealt to agent
4. ✅ Look for action validation errors

**Debug Logs to Check**:
```
[AutoPilot] Run started, beginning action loop  ← Should see this
[AutoPilot] Playing hand with X cards           ← Should see repeatedly
```

---

### Problem: Multiple Agents Create Multiple Lobbies

**Symptoms**:
- Each agent creates its own lobby
- Agents don't play together

**Solutions**:
1. ✅ Set `AutoCreateLobby: false` for all but one agent
2. ✅ Use different appsettings.json for different agent roles
3. ✅ Add delay between agent launches
4. ✅ Use lobby joining logic instead

**Recommended Setup**:
```json
// Agent 1 (Host)
"AutoCreateLobby": true

// Agents 2-4 (Joiners)
"AutoCreateLobby": false,
"AutoJoinLobby": true
```

---

### Problem: Agent Actions Too Fast/Slow

**Symptoms**:
- Agent spams actions rapidly
- OR agent takes forever to act

**Solutions**:

**Too Fast**:
```json
{
  "MinThinkTimeMs": 1000,
  "MaxThinkTimeMs": 2000
}
```

**Too Slow**:
```json
{
  "MinThinkTimeMs": 200,
  "MaxThinkTimeMs": 500
}
```

---

### Problem: Playwright Test Times Out

**Symptoms**:
- Test runs for max time and exits
- Agents still in game

**Solutions**:
1. ✅ Increase `gameTimeoutMs` in config
2. ✅ Check if agents are stuck
3. ✅ Review console logs for errors
4. ✅ Verify game is progressing

**Increase Timeout**:
```javascript
gameTimeoutMs: 900000  // 15 minutes instead of 10
```

---

## Best Practices

### 1. Start Small, Scale Up

✅ **Do**: Test with 1 agent first, then 2, then 4
❌ **Don't**: Jump straight to 16 agents

**Reason**: Easier to debug issues with fewer agents

---

### 2. Use Descriptive Configuration Names

✅ **Do**: Create `appsettings.FastTest.json`, `appsettings.LoadTest.json`
❌ **Don't**: Keep changing the same `appsettings.json`

**Reason**: Easy to switch between test scenarios

---

### 3. Monitor First, Automate Later

✅ **Do**: Watch agents in visible browsers first
❌ **Don't**: Run headless tests immediately

**Reason**: Visual feedback helps identify issues quickly

---

### 4. Tag Your Test Runs

✅ **Do**: Use unique player names or tags for test runs
❌ **Don't**: Use default random names

**Example**:
```csharp
// Modify agent player name for test tracking
Player.Name = $"Agent-Test-{DateTime.Now:yyyyMMdd-HHmmss}";
```

**Reason**: Easier to correlate Sentry/logs with specific test runs

---

### 5. Clean Up After Tests

✅ **Do**: Close all browser windows, reset settings
❌ **Don't**: Leave 20 browser windows running overnight

**Script**:
```bash
# Kill all Chrome processes after test
pkill -f "chrome"
```

---

### 6. Version Your Test Scripts

✅ **Do**: Keep test-orchestrator.js in version control
❌ **Don't**: Make local changes without committing

**Reason**: Team can reproduce tests consistently

---

### 7. Document Your Test Scenarios

✅ **Do**: Add comments explaining what each config tests
❌ **Don't**: Use mysterious numbers

**Example**:
```javascript
// Scenario: Stress test with 8 concurrent players
// Expected: Server handles load, all agents complete game
// Last run: 2025-12-27, PASSED
const CONFIG = {
    numAgents: 8,
    gameTimeoutMs: 900000
};
```

---

## FAQ

### Q: Can I run agents on a live production server?

**A**: Not recommended. Agents should only run on development/staging environments. They generate synthetic load and could interfere with real players.

---

### Q: How many agents can I run simultaneously?

**A**: Depends on your hardware:
- **Local Dev**: 4-8 agents comfortably
- **CI/CD Server**: 8-16 agents
- **Dedicated Test Server**: 32+ agents

Monitor CPU/memory usage to find your limit.

---

### Q: Do agents count toward player limits?

**A**: Yes, agents are treated as regular players by the server. A 4-player lobby filled with agents has no room for human players.

---

### Q: Can I make agents play smarter?

**A**: Yes! See `AI-AGENT-TODOS.md` for implementing better brains (AggressiveBrain, OptimalBrain). Currently only RandomBrain exists.

---

### Q: Will agents work in production builds?

**A**: Yes, but you should:
1. Set `Enabled: false` in production appsettings.json
2. Only enable via URL parameter for controlled testing
3. Never expose agent URLs to end users

---

### Q: Can agents select powers and buy items?

**A**: Not yet. Those features are marked as TODO. See `AI-AGENT-TODOS.md` for details.

---

### Q: How do I stop an agent?

**Options**:
1. Close the browser window/tab
2. Navigate away from the page
3. Set `?autoPlay=false` in URL
4. Refresh the page without `?autoPlay=true`

---

### Q: Can I control which brain the agent uses?

**A**: Currently no, only RandomBrain exists. Future enhancement will support `?brain=aggressive` URL parameter.

---

### Q: Do agents work offline?

**A**: No, agents require:
- Running server (for SignalR)
- Active network connection
- Blazor WASM client loaded

---

### Q: Are agent actions logged to database?

**A**: No, agent actions are logged to:
- Browser console (client-side)
- Sentry (if errors occur)
- Server logs (same as human players)

Not specifically tracked as "agent" in database.

---

## Quick Reference Card

```
┌─────────────────────────────────────────────────────────────┐
│                    AGENT QUICK REFERENCE                     │
├─────────────────────────────────────────────────────────────┤
│ ENABLE AGENT                                                │
│   URL: /?autoPlay=true                                      │
│                                                             │
│ SINGLE AGENT TEST                                           │
│   1. Start app                                              │
│   2. Open: https://localhost:5001/?autoPlay=true            │
│   3. Watch console (F12)                                    │
│                                                             │
│ MULTI-AGENT TEST                                            │
│   1. cd src                                                 │
│   2. npm install playwright                                 │
│   3. node test-orchestrator.js                              │
│                                                             │
│ MONITOR LOGS                                                │
│   Console: Filter "[AutoPilot]" or "[LobbyAutomation]"     │
│   Sentry: Filter tag "user_type:agent"                     │
│                                                             │
│ COMMON CONFIGS                                              │
│   Fast Test:    MinThink=200,  MaxThink=500                │
│   Normal Test:  MinThink=500,  MaxThink=1500               │
│   Realistic:    MinThink=1000, MaxThink=2500               │
│                                                             │
│ TROUBLESHOOTING                                             │
│   Not starting?     Check appsettings.json                  │
│   Stuck in lobby?   Check MaxPlayersBeforeStart            │
│   No actions?       Check console for errors                │
│   Too fast/slow?    Adjust MinThinkTimeMs/MaxThinkTimeMs   │
└─────────────────────────────────────────────────────────────┘
```

---

## Additional Resources

- **Implementation Details**: See `AI-AGENT-IMPLEMENTATION.md`
- **Development TODOs**: See `AI-AGENT-TODOS.md`
- **Original Feature Design**: See `integrated-ai-agents-feature.md`
- **Test Orchestrator**: See `test-orchestrator.js`

---

**Last Updated**: 2025-12-27
**Version**: 1.0
**Feedback**: Report issues or suggestions to the development team
