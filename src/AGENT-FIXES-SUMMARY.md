# Agent Fixes Summary - 2025-12-27

## Issues Fixed

### Issue 1: SignalR Connection Not Established
**Symptom**: Agents fail with `InvalidOperationException: SignalR connection is not established`

**Root Cause**: Agents tried to join lobbies before SignalR async connection completed

**Fix Applied**:
1. Added `IsConnected` property to `ISignalRNotificationService`
2. `LobbyAutomationService` waits up to 10 seconds for SignalR to connect
3. `AutoPilotService` validates connection before every action

**Files Modified**:
- `Client.Core/Services/SignalRNotificationService.cs` - Added `IsConnected` property
- `Client.Shared/Services/LobbyAutomationService.cs` - Added wait loop
- `Client.Shared/Services/AutoPilotService.cs` - Added connection check

---

### Issue 2: Game Never Starts After Lobby Creation
**Symptom**: Agent creates lobby but game never starts, agents stay in lobby forever

**Root Cause**: After creating lobby, agent didn't:
- Join the lobby's SignalR group
- Monitor for other players joining
- Trigger game start when enough players present

**Fix Applied**:
1. After creating lobby, agent now joins its SignalR group
2. Agent subscribes to `PlayerJoined` notifications
3. When enough players join, host agent automatically starts game

**Files Modified**:
- `Client.Shared/Services/LobbyAutomationService.cs` - Added post-creation lobby management

---

## How to Test

### 1. Rebuild Solution
```bash
dotnet build
```

**Important**: Must rebuild for changes to take effect!

### 2. Run Multi-Agent Test
```bash
cd src
node test-orchestrator.js
```

### 3. Expected Logs

**Agent 1 (Creates Lobby)**:
```
[LobbyAutomation] Starting automation for player...
[LobbyAutomation] Waiting for SignalR connection...  ← NEW
[LobbyAutomation] SignalR connected                   ← NEW
[LobbyAutomation] No available lobbies, creating new lobby
[LobbyAutomation] Joining SignalR group for created lobby... ← NEW
[LobbyAutomation] Waiting for 4 players before starting game ← NEW
```

**Agent 2-4 (Join Lobby)**:
```
[LobbyAutomation] Starting automation for player...
[LobbyAutomation] Waiting for SignalR connection...  ← NEW
[LobbyAutomation] SignalR connected                   ← NEW
[LobbyAutomation] Joining lobby X with Y players
```

**When 4th Player Joins**:
```
[LobbyAutomation] Player joined, reloading lobby state        ← NEW
[LobbyAutomation] Lobby has 4 players, starting game          ← NEW
```

**Game Starts**:
```
[AutoPilot] Run started, beginning action loop
[AutoPilot] Playing hand with 3 cards
[AutoPilot] Discarding 2 cards
...
```

---

## Configuration for Testing

**For 4-Agent Test**, ensure `appsettings.json`:
```json
{
  "AgentSettings": {
    "Enabled": false,
    "MinThinkTimeMs": 500,
    "MaxThinkTimeMs": 1500,
    "BrainType": "Random",
    "AutoJoinLobby": true,
    "AutoCreateLobby": true,      ← Must be true
    "MaxPlayersBeforeStart": 4,   ← Match number of agents
    "AutoStartGameAsHost": true,  ← Must be true
    "DefaultGameMode": "Quick"
  }
}
```

**For 2-Agent Quick Test**, change:
```json
{
  "MaxPlayersBeforeStart": 2  ← Lower threshold
}
```

---

## Troubleshooting

### Problem: Still Getting "SignalR Not Established" Errors

**Solution**: Code wasn't rebuilt
```bash
# Clean and rebuild
dotnet clean
dotnet build
```

**Verify**: Look for "Waiting for SignalR connection..." logs

---

### Problem: Agents Join But Game Never Starts

**Check 1**: Is Agent 1 the host?
- Agent 1 should show "creating new lobby"
- Only the host can start the game

**Check 2**: Player count threshold
```
[LobbyAutomation] Waiting for 4 players before starting game
```
- Make sure you launch exactly that many agents
- Or lower `MaxPlayersBeforeStart` to 2

**Check 3**: AutoStartGameAsHost setting
```json
"AutoStartGameAsHost": true  // Must be true
```

---

### Problem: Multiple Lobbies Created

**Cause**: All agents have `AutoCreateLobby: true` and they race to create lobbies

**Quick Fix**: Add delay between agent launches in `test-orchestrator.js`:
```javascript
// Current: await new Promise(resolve => setTimeout(resolve, 1000));
await new Promise(resolve => setTimeout(resolve, 2000));  // 2 second delay
```

**Better Fix**: First agent creates, others join:
- Keep `AutoCreateLobby: true` globally
- Logic already handles this - first agent creates, others join existing

---

### Problem: Agents Enter Game But Don't Play

**Check**: Look for these logs:
```
[AutoPilot] Run started, beginning action loop  ← Must appear
```

If missing:
1. Check if `RunStarted` notification is being sent
2. Verify AutoPilotService is subscribed to notifications
3. Check game actually entered InGame state

---

## Code Changes Summary

### SignalRNotificationService.cs
```csharp
// Added properties
public HubConnection? HubConnection => _hubConnection;
public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
```

### LobbyAutomationService.cs
```csharp
// Added SignalR wait loop
var waitStart = DateTime.UtcNow;
while (!_signalR.IsConnected && (DateTime.UtcNow - waitStart).TotalSeconds < 10)
{
    _logger.LogInformation("[LobbyAutomation] Waiting for SignalR connection...");
    await Task.Delay(500, cancellationToken);
}

// Added post-lobby-creation logic
await _signalR.JoinLobbyGroupAsync(createdLobby.Id);
_signalR.HandleNotificationReceived += async (notification) =>
{
    if (notification.NotificationType == PokerAttackNotificationType.PlayerJoined)
    {
        // Check player count and start game if ready
    }
};
```

### AutoPilotService.cs
```csharp
// Added connection check before actions
if (!_signalR.IsConnected)
{
    _logger.LogWarning("[AutoPilot] SignalR not connected, skipping action");
    return;
}
```

---

## Success Criteria

✅ **Test Passes When**:
1. All 4 agents connect without SignalR errors
2. Agent 1 creates lobby
3. Agents 2-4 join that lobby
4. Game starts automatically when 4th agent joins
5. All agents play through entire game
6. No exceptions in console
7. Orchestrator reports "game complete"

❌ **Test Fails If**:
1. Any `SignalR connection not established` errors
2. Multiple lobbies created (unless expected)
3. Game never transitions from lobby to gameplay
4. Agents freeze/stop acting
5. Timeout reached (10 minutes)

---

## Next Steps After Successful Test

1. ✅ Verify multi-agent test completes end-to-end
2. ⏳ Implement TODO: `SelectPlayerPowerAction` (Freebie state)
3. ⏳ Implement TODO: `PurchaseShopItemAction` (Shop state)
4. ⏳ Improve RandomActionBrain decision quality
5. ⏳ Add more brain types (Aggressive, Conservative, Optimal)

---

**Files Modified in This Fix**:
- `Client.Core/Services/SignalRNotificationService.cs`
- `Client.Shared/Services/LobbyAutomationService.cs`
- `Client.Shared/Services/AutoPilotService.cs`

**Date**: 2025-12-27
**Version**: 1.2 (Lobby start fix)
