# Agent Testing Checklist

## What Was Fixed

**Problem**: Agents were trying to join lobbies before SignalR connection was established.

**Error**: `System.InvalidOperationException: SignalR connection is not established.`

**Solution**:
1. Added `IsConnected` property to `ISignalRNotificationService`
2. `LobbyAutomationService` now waits up to 10 seconds for SignalR to connect
3. `AutoPilotService` checks connection before executing actions

---

## How to Test

### Quick Test (Single Agent)

1. **Build the solution**:
   ```bash
   dotnet build
   ```

2. **Start the application**:
   ```bash
   dotnet run
   ```

3. **Open browser with agent enabled**:
   ```
   https://localhost:7150/?autoPlay=true
   ```

4. **Open browser console (F12)** and look for:
   ```
   [LobbyAutomation] Starting automation for player...
   [LobbyAutomation] Waiting for SignalR connection...
   [LobbyAutomation] SignalR connected, proceeding with lobby automation
   [LobbyAutomation] Joining lobby... OR Creating new lobby...
   [AutoPilot] Enabled for player...
   ```

### Expected Behavior

✅ **Success Path**:
```
1. [LobbyAutomation] Starting automation
2. [LobbyAutomation] Waiting for SignalR... (may appear 1-3 times)
3. [LobbyAutomation] SignalR connected
4. [LobbyAutomation] Joining/Creating lobby
5. [AutoPilot] Enabled
6. (When game starts) [AutoPilot] Run started, beginning action loop
7. [AutoPilot] Playing hand with X cards
8. [AutoPilot] Discarding X cards
9. etc.
```

❌ **Failure Scenarios**:

**If SignalR never connects**:
```
[LobbyAutomation] Waiting for SignalR connection...
[LobbyAutomation] Waiting for SignalR connection...
[LobbyAutomation] SignalR connection not established after 10 seconds, aborting
```
→ Check server is running and SignalR URL is correct in appsettings.json

**If agent doesn't act in game**:
```
[AutoPilot] SignalR not connected, skipping action PlayHandAction
```
→ SignalR disconnected during gameplay, check network/server

---

## What You Should See

### Browser UI

**The agent does NOT control the UI like a robot**. You will see:

- ✅ Lobby list updates when agent joins
- ✅ Game state changes when server responds
- ✅ Cards appear/disappear as agent plays
- ✅ Score updates

**You will NOT see**:
- ❌ Mouse cursor moving
- ❌ Buttons being clicked
- ❌ Form fields being filled

### Why?

The agent **bypasses the UI** and calls SignalR methods directly:
```
Agent → SignalR.PlayHandAsync() → Server → Broadcasts update → UI updates
```

NOT:
```
Agent → Clicks "Play Hand" button → Button handler → SignalR → Server
```

---

## Multi-Agent Test

### Playwright Test

1. **Install Playwright** (if not already):
   ```bash
   cd src
   npm install playwright
   ```

2. **Run orchestrator**:
   ```bash
   node test-orchestrator.js
   ```

3. **Watch console output**:
   ```
   [Orchestrator] Starting multi-agent test with 4 agents
   [Agent 1] [LobbyAutomation] Starting automation...
   [Agent 1] [LobbyAutomation] SignalR connected
   [Agent 2] [LobbyAutomation] SignalR connected
   [Agent 3] [LobbyAutomation] SignalR connected
   [Agent 4] [LobbyAutomation] SignalR connected
   [Agent 1] [LobbyAutomation] Creating new lobby
   [Agent 2] [LobbyAutomation] Joining lobby...
   [Agent 3] [LobbyAutomation] Joining lobby...
   [Agent 4] [LobbyAutomation] Joining lobby...
   ```

### Manual Multiple Browsers

1. Open browser window 1: `https://localhost:7150/?autoPlay=true`
2. Wait for agent to create/join lobby
3. Open browser window 2: `https://localhost:7150/?autoPlay=true`
4. Watch agents join same lobby
5. Repeat for more agents

**Tip**: Use incognito/private windows for separate sessions.

---

## Common Issues

### Issue: "Waiting for SignalR" loops forever

**Cause**: SignalR server not running or wrong URL

**Fix**:
1. Check server is running
2. Verify `appsettings.json` has correct SignalR URL:
   ```json
   {
     "ExternalApis": [
       {
         "Name": "PokerAttackSignalR",
         "BaseUri": "https://localhost:7150/"
       }
     ]
   }
   ```

### Issue: Agent creates lobby but doesn't join

**Cause**: `AutoJoinLobby` might be false

**Fix**: Check `appsettings.json`:
```json
{
  "AgentSettings": {
    "AutoJoinLobby": true
  }
}
```

### Issue: All agents create separate lobbies

**Cause**: All agents have `AutoCreateLobby: true`

**Fix**:
- For multi-agent testing, set `AutoCreateLobby: false`
- Have one human player or agent create lobby first
- OR set `AutoCreateLobby: true` and `MaxPlayersBeforeStart: 1` for solo testing

### Issue: Agent joins lobby but game never starts

**Cause**: Not enough players or auto-start disabled

**Fix**:
```json
{
  "AgentSettings": {
    "MaxPlayersBeforeStart": 2,  // Lower threshold
    "AutoStartGameAsHost": true
  }
}
```

### Issue: No [AutoPilot] logs during game

**Possible Causes**:
1. Agent not receiving `RunStarted` notification
2. Agent's action loop exited early
3. No actions available (cards not dealt)

**Debug**:
- Check for `[AutoPilot] Run started` log
- Look for errors in console
- Verify game state is `InGame`

---

## Debugging Tools

### Browser Console Filters

**Filter for agent activity only**:
```
AutoPilot|LobbyAutomation
```

**Filter for errors only**:
```
-level:info -level:debug
```

### Network Tab

1. Open DevTools → Network tab
2. Filter for `websocket` or `signalr`
3. Check connection status
4. Look for failed messages

### Sentry Dashboard

If Sentry is configured:
1. Go to Sentry dashboard
2. Filter by tag: `user_type:agent`
3. Review errors specific to agents

---

## Success Criteria

### Single Agent
- [x] Agent waits for SignalR connection
- [x] Agent joins or creates lobby
- [x] Agent enables autopilot
- [x] Agent plays hands when game starts
- [x] Agent discards cards
- [x] Agent activates powers (when available)
- [x] Agent completes full game without errors

### Multi-Agent (2+ agents)
- [x] Multiple agents connect simultaneously
- [x] Agents join same lobby
- [x] All agents play in same game
- [x] No race conditions or conflicts
- [x] All agents complete game

---

## Next Steps After Successful Test

1. **Implement TODOs**:
   - SelectPlayerPowerAction (Freebie state)
   - PurchaseShopItemAction (Shop state)

2. **Improve Brain**:
   - Better hand selection logic
   - Strategic discard decisions
   - Smart shop purchases

3. **Add More Tests**:
   - Edge case scenarios
   - Load testing with 8+ agents
   - Stress testing with rapid actions

4. **Monitor Production**:
   - Set up Sentry alerts for agent errors
   - Track agent performance metrics
   - Analyze agent behavior patterns

---

## Quick Reference

| What to Check | Where to Look | What You Should See |
|---------------|---------------|---------------------|
| SignalR Connection | Browser Console | `SignalR connected` within 5 seconds |
| Lobby Action | Browser Console | `Joining lobby` or `Creating new lobby` |
| Autopilot Active | Browser Console | `[AutoPilot] Enabled` |
| Game Actions | Browser Console | `Playing hand`, `Discarding` messages |
| Errors | Browser Console | No red errors (warnings OK) |
| UI Updates | Browser Window | Lobby/game state changes visually |

---

**Last Updated**: 2025-12-27
**Version**: 1.1 (SignalR timing fix)
