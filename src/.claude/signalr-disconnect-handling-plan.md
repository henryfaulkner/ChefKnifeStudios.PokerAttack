# SignalR Disconnect Handling Implementation Plan

## Problem Statement

Mobile devices drop SignalR connections when phones go to sleep. Current system:
- ✅ **Games**: 60-second grace period before elimination (working)
- ❌ **Lobbies**: No disconnect handling at all (broken)
- ❌ **Client errors**: "Connection is not established" exceptions crash user experience

**Goal**: Immediately kick disconnected players from lobbies and games, with graceful client-side error handling. Prepare infrastructure for future rejoin feature.

---

## Current State Analysis

### Server-Side (Working Parts)

**✅ Game Disconnections (60-second grace period)**
- `SignalRNotificationHub.OnDisconnectedAsync` (WebAPI/SignalR/SignalRNotificationHub.cs:42-64)
  - Tracks connection removal via `PlayerConnectionTracker`
  - Checks if player fully disconnected (no remaining connections)
  - Calls `FindPlayerGameAsync` to locate player's active game
  - Calls `PlayerDisconnectionTracker.TrackDisconnection(playerId, gameId)`

- `PlayerDisconnectionTracker` (BL/PlayerDisconnectionTracker.cs)
  - 60-second timer before elimination
  - Cancels timer if player reconnects
  - Calls `GameService.EliminateDisconnectedPlayerAsync` on timeout

- `GameService.EliminateDisconnectedPlayerAsync` (BL/Services/GameService.cs:681-760)
  - Marks player as eliminated
  - Removes from ActiveGame.Players
  - Sends notifications to other players
  - Handles cleanup properly

### Server-Side (Broken Parts)

**❌ Lobby Disconnections (No handling)**
- `FindPlayerGameAsync` (WebAPI/SignalR/SignalRNotificationHub.cs:66-88)
  - **ONLY** searches `ActiveGame` repository
  - Does **NOT** search `Lobby` repository
  - Returns `null` for players in lobbies

- When player disconnects from lobby:
  - Connection removed from tracker
  - `FindPlayerGameAsync` returns `null`
  - `PlayerDisconnectionTracker.TrackDisconnection` **NEVER CALLED**
  - Player remains in lobby forever (ghost player)

**❌ Host Disconnect Behavior**
- `LobbyService.LeaveLobbyAsync` (BL/Services/LobbyService.cs:227-240)
  - When host leaves → entire lobby shuts down
  - When disconnected → nothing happens
  - Inconsistent behavior

### Client-Side (Broken Error Handling)

**❌ All SignalR methods throw exceptions when disconnected**
- `JoinLobbyGroupAsync` (Client.Core/Services/SignalRNotificationService.cs:149-163)
  - Line 153-154: Throws `InvalidOperationException` if not connected
  - Crashes user flow, no recovery

- `JoinGameGroupAsync` (SignalRNotificationService.cs:183-197)
  - Line 187-188: Same exception behavior
  - No retry logic

- **Pattern repeated in all methods**: StartGameAsync, PlayHandAsync, DiscardAsync, etc.
  - All check connection state
  - All throw exceptions
  - None attempt recovery

**✅ Automatic Reconnection Already Enabled**
- Line 97: `.WithAutomaticReconnect()` configured
- Connection will auto-reconnect on its own
- **Problem**: Methods throw before reconnect can happen

---

## Implementation Plan

### Phase 1: Server - Add Lobby Disconnect Detection

**File**: `Server.WebAPI/SignalR/SignalRNotificationHub.cs`

**1.1 Create FindPlayerLobbyAsync method**
```csharp
private async Task<string?> FindPlayerLobbyAsync(string playerId)
{
    // Search through all lobbies to find which one the player is in
    var allLobbiesResult = await lobbyRepository.GetAllAsync();
    if (allLobbiesResult.IsSuccess && allLobbiesResult.Value is not null)
    {
        foreach (var lobby in allLobbiesResult.Value)
        {
            if (lobby.Value.Players.Any(p => p.Id == playerId) ||
                lobby.Value.HostPlayer?.Id == playerId)
            {
                return lobby.Key; // Return lobbyId
            }
        }
    }
    return null;
}
```

**1.2 Update OnDisconnectedAsync to handle both lobbies and games**
```csharp
public override async Task OnDisconnectedAsync(Exception? exception)
{
    var userId = Context.UserIdentifier;
    var connectionId = Context.ConnectionId;

    if (!string.IsNullOrEmpty(userId))
    {
        connectionTracker.Remove(userId, connectionId);

        var remainingConnections = connectionTracker.GetConnections(userId);
        if (!remainingConnections.Any())
        {
            // Player fully disconnected - check both lobby and game
            var lobbyId = await FindPlayerLobbyAsync(userId);
            var gameId = await FindPlayerGameAsync(userId);

            if (!string.IsNullOrEmpty(lobbyId))
            {
                // IMMEDIATE removal from lobby (no grace period)
                await HandleLobbyDisconnect(userId, lobbyId);
            }

            if (!string.IsNullOrEmpty(gameId))
            {
                // 60-second grace period for game (existing behavior)
                disconnectionTracker.TrackDisconnection(userId, gameId);
            }
        }
    }

    await base.OnDisconnectedAsync(exception);
}
```

**1.3 Add HandleLobbyDisconnect method**
```csharp
private async Task HandleLobbyDisconnect(string playerId, string lobbyId)
{
    try
    {
        _logger.LogInformation(
            "Player {playerId} disconnected from lobby {lobbyId}. Removing immediately (no grace period).",
            playerId, lobbyId);

        using var scope = _serviceProvider.CreateScope();
        var lobbyService = scope.ServiceProvider.GetRequiredService<ILobbyService>();

        // Create a minimal PlayerDTO for removal
        var lobbyResult = await lobbyRepository.GetAsync(lobbyId);
        if (lobbyResult.IsSuccess && lobbyResult.Value is not null)
        {
            var lobby = lobbyResult.Value;
            var player = lobby.Players.FirstOrDefault(p => p.Id == playerId)
                        ?? lobby.HostPlayer;

            if (player != null)
            {
                var playerDTO = player.MapToDTO();
                await lobbyService.RemoveLobbyPlayerAsync(playerDTO);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex,
            "Error removing disconnected player {playerId} from lobby {lobbyId}",
            playerId, lobbyId);
    }
}
```

**Dependencies needed**:
- Add `ILobbyService` to constructor
- Add `IServiceProvider` to constructor (for scoping)
- Add `IKeyValueRepository<Lobby>` to constructor

---

### Phase 2: Server - Add Disconnect Logging

**File**: `Server.BL/Services/LobbyService.cs`

**2.1 Add disconnect logging to RemoveLobbyPlayerAsync**
```csharp
public async Task<Result> RemoveLobbyPlayerAsync(PlayerDTO player, CancellationToken ct = default)
{
    // ... existing code ...

    _logger.LogWarning(
        "Player removed from lobby: PlayerId={PlayerId}, PlayerName={PlayerName}, LobbyId={LobbyId}, Reason=Disconnection",
        player.Id, player.Name, lobbyKvp.Value.Key);

    // ... rest of method ...
}
```

**2.2 Add host disconnect logging**
```csharp
if (lobbyKvp.Value.Value.HostPlayer.Id == player.Id)
{
    _logger.LogWarning(
        "Lobby host disconnected, shutting down lobby: HostPlayerId={PlayerId}, LobbyId={LobbyId}",
        player.Id, lobbyKvp.Value.Key);

    // ... existing shutdown logic ...
}
```

---

### Phase 3: Client - Graceful Connection Error Handling

**File**: `Client.Core/Services/SignalRNotificationService.cs`

**3.1 Add connection state helper method**
```csharp
private async Task<bool> EnsureConnectedAsync(string operationName)
{
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
        return true;

    _logger.LogError(
        "SignalR operation '{operation}' attempted but connection not established. State: {state}",
        operationName, _hubConnection?.State.ToString() ?? "null");

    // Don't throw - let automatic reconnect handle it
    return false;
}
```

**3.2 Update JoinLobbyGroupAsync to use graceful handling**
```csharp
public async Task JoinLobbyGroupAsync(string lobbyId)
{
    try
    {
        if (!await EnsureConnectedAsync(nameof(JoinLobbyGroupAsync)))
        {
            _logger.LogWarning(
                "Cannot join lobby group {lobbyId} - connection not established. Automatic reconnect will retry.",
                lobbyId);
            return; // Exit gracefully, don't throw
        }

        await _hubConnection!.InvokeAsync("JoinLobbyGroupAsync", lobbyId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error joining lobby group {lobbyId}", lobbyId);
        // Don't rethrow - log and move on
    }
}
```

**3.3 Update JoinGameGroupAsync**
```csharp
public async Task JoinGameGroupAsync(string gameId)
{
    try
    {
        if (!await EnsureConnectedAsync(nameof(JoinGameGroupAsync)))
        {
            _logger.LogWarning(
                "Cannot join game group {gameId} - connection not established. Automatic reconnect will retry.",
                gameId);
            return;
        }

        await _hubConnection!.InvokeAsync("JoinGameGroupAsync", gameId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error joining game group {gameId}", gameId);
    }
}
```

**3.4 Apply same pattern to all other methods**
- StartGameAsync
- PlayHandAsync
- DiscardAsync
- ActivatePlayerPowerAsync
- TransitionGameStateAsync
- EndGameAsync
- LeaveGameAsync
- LeaveLobbyGroupAsync
- LeaveGameGroupAsync

**Pattern**: Replace `throw new InvalidOperationException()` with graceful logging and return.

---

### Phase 4: Future Rejoin Infrastructure (Prepare, Don't Implement)

**Design Considerations for Future Phase:**

**Data to preserve for rejoin:**
- Last known lobby/game ID
- Player state (score, wallet, hand, etc.)
- Game round number
- Timestamp of disconnect

**Potential approach** (document, don't implement yet):
1. Add `DisconnectReason` enum: `UserInitiated`, `NetworkDrop`, `Timeout`
2. Add `PlayerDisconnectHistory` repository to track disconnects
3. Modify `RemoveLobbyPlayerAsync` to accept disconnect reason
4. When `reason == NetworkDrop`, save state for 5-minute rejoin window
5. Add `RejoinLobbyAsync` and `RejoinGameAsync` methods (future phase)

**Current implementation accommodations:**
- Don't delete `GamePlayer` immediately on disconnect from game (already done)
- Keep disconnect timer mechanism (already exists)
- Log all disconnects with structured data for future analysis

---

## Testing Plan

### Manual Testing

**Lobby Disconnect Test:**
1. Create lobby with 2+ players
2. Disconnect one non-host player (simulate sleep)
3. ✅ Verify player removed from lobby immediately
4. ✅ Verify other players see "PlayerLeft" notification
5. ✅ Verify lobby still functional

**Host Disconnect Test:**
1. Create lobby with host + players
2. Disconnect host player
3. ✅ Verify entire lobby shuts down
4. ✅ Verify all players see "LobbyShutdown" notification

**Game Disconnect Test:**
1. Start game with 2+ players
2. Disconnect one player mid-game
3. ✅ Verify 60-second timer starts
4. ✅ Verify player can reconnect within 60 seconds
5. ✅ Verify player eliminated after 60 seconds if no reconnect

**Client Error Handling Test:**
1. Disconnect client network
2. Try to join lobby group
3. ✅ Verify no exception thrown
4. ✅ Verify warning logged
5. ✅ Reconnect network
6. ✅ Verify automatic reconnect works

### Integration Testing

**Multi-tab test:**
1. Open game in two browser tabs (same player)
2. Close one tab
3. ✅ Verify player stays connected (multiple connections)
4. Close second tab
5. ✅ Verify disconnect logic triggers

---

## Risk Analysis

### Low Risk
- Lobby disconnect handling is new code, won't break existing game behavior
- Client error handling changes throw → log (safer, not riskier)

### Medium Risk
- `FindPlayerLobbyAsync` searches all lobbies (O(n) operation)
  - Mitigation: Lobbies are small, in-memory, fast
  - Future: Add player-to-lobby index if needed

### High Risk
- None identified

---

## Implementation Order

1. ✅ Phase 1.1: Add FindPlayerLobbyAsync (no side effects, just read)
2. ✅ Phase 1.2: Update OnDisconnectedAsync (core logic)
3. ✅ Phase 1.3: Add HandleLobbyDisconnect (cleanup logic)
4. ✅ Phase 2: Add logging (observability)
5. ✅ Phase 3: Client error handling (user experience)
6. ✅ Test thoroughly before deploying

---

## Success Criteria

**Server-Side:**
- [ ] Disconnected players removed from lobbies within 1 second
- [ ] Host disconnect shuts down lobby properly
- [ ] Game disconnect still has 60-second grace period
- [ ] All disconnects logged with structured data

**Client-Side:**
- [ ] No "connection not established" exceptions thrown
- [ ] All connection errors logged as warnings
- [ ] Automatic reconnect works seamlessly
- [ ] User experience is smooth (no crashes)

**Future-Ready:**
- [ ] Disconnect events logged with enough data for rejoin analysis
- [ ] Game player state preserved for future rejoin feature
