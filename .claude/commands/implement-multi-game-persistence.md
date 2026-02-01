# Multiplayer Game State Persistence Plan

## Overview
Implement persistence of multiplayer game state through browser refresh. When a player refreshes during a multiplayer game, they should seamlessly rejoin rather than being kicked from the game.

**Philosophy**: Don't fight the architecture, work with it. SignalR already has automatic reconnect - we just need to preserve state on both sides.

---

## Current Architecture Summary

### What Works
- GameId is in URL query param (`/multi-gameplay?GameId={id}`) - survives refresh
- SignalR configured with `.WithAutomaticReconnect()` - handles network blips
- `PlayerConnectionTracker` supports multi-tab (doesn't disconnect until ALL tabs close)
- Server stores full game state in memory (`ActiveGame`, `GamePlayer` repositories)

### What's Broken
| Problem | Location | Impact |
|---------|----------|--------|
| `MultiGameDataStore.Reset()` called on page init | `MultiGameplay.razor.cs:61` | Wipes all client state |
| Immediate player removal on disconnect | `SignalRNotificationHub.OnDisconnectedAsync()` | Player kicked from game |
| No `GetMultiGameState` endpoint | Server | Can't restore state after refresh |
| No localStorage backup for GameId | Client | Can't auto-redirect if URL lost |

---

## Implementation Iterations

### Iteration 1: Client-Side State Restoration (Simplest First)
**Goal**: Preserve GameId and restore state from server on refresh.

#### 1.1 Add localStorage Key
**File**: `Client.Shared/Constants/LocalStorageConstants.cs`
```csharp
public const string MultiGameStateKey = "MultiGameState";
```

#### 1.2 Create MultiGameDataStore Persistence
**File**: `Client.Shared/Services/MultiGameDataStore.cs`

Add to interface:
```csharp
void SaveToLocalStorage();
bool TryRestoreFromLocalStorage();
void ClearFromLocalStorage();
```
j
What to persist (minimal):
```csharp
public class PersistedMultiGameState
{
    public string? GameId { get; init; }
    public string? PlayerId { get; init; }
    public long SavedTimestamp { get; init; }
}
```

#### 1.3 Modify MultiGameplay Page Init
**File**: `Client.WebApp/Pages/MultiGameplay.razor.cs`

Change from:
```csharp
MultiGameDataStore.Reset();
MultiGameDataStore.GameId = GameId;
await MultiGameplayViewModel.StartGameAsync(...);
```

To:
```csharp
// Try to restore existing game
if (MultiGameDataStore.TryRestoreFromLocalStorage() && MultiGameDataStore.GameId == GameId)
{
    // Rejoin existing game
    var restored = await MultiGameplayViewModel.RejoinGameAsync();
    if (restored) { SetupEventHandlers(); return; }
}

// Fresh start
MultiGameDataStore.Reset();
MultiGameDataStore.GameId = GameId;
await MultiGameplayViewModel.StartGameAsync(...);
MultiGameDataStore.SaveToLocalStorage();
```

#### 1.4 Clear State on Game End
**File**: `Client.Shared/ViewModels/MultiGameplayViewModel.cs`

On receiving `GameWon` or `GameLost` notification:
```csharp
_multiGameDataStore.ClearFromLocalStorage();
```

---

### Iteration 2: Server-Side GetMultiGameState Endpoint
**Goal**: Allow client to fetch full game state after reconnect.

#### 2.1 Add DTO
**File**: `Shared/DTOs/MultiGameStateDTO.cs`
```csharp
public class MultiGameStateDTO
{
    public string GameId { get; init; }
    public string PlayerId { get; init; }
    public string GameState { get; init; }  // InGame, Shop, etc.
    public int RoundNumber { get; init; }
    public int Score { get; init; }
    public int Wallet { get; init; }
    public int HandsAvailable { get; init; }
    public int DiscardsAvailable { get; init; }
    public int PowerCharges { get; init; }
    public List<CardDTO> Cards { get; init; }
    public PlayerPowerDTO? ActivePower { get; init; }
    public int? RoundTimeRemainingSeconds { get; init; }  // null if not in timed phase
}
```

#### 2.2 Add Endpoint
**File**: `Server.WebAPI/EndpointGroups/GameplayEndpoints.cs`
```csharp
group.MapGet("/GetMultiGameState", async (
    [FromQuery] string gameId,
    [FromQuery] string playerId,
    IGameService gameService,
    CancellationToken ct) =>
{
    var result = await gameService.GetMultiGameStateAsync(gameId, playerId, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
});
```

#### 2.3 Add Service Method
**File**: `Server.BL/Services/GameService.cs`
```csharp
public async Task<Result<MultiGameStateDTO>> GetMultiGameStateAsync(
    string gameId, string playerId, CancellationToken ct)
{
    var activeGame = await _activeGameRepository.GetAsync(gameId, ct);
    if (activeGame is null) return Result.Fail("Game not found");

    if (!activeGame.Players.Any(p => p.Id == playerId))
        return Result.Fail("Player not in game");

    var gamePlayer = await _gamePlayerRepository.GetAsync(playerId, ct);
    var gameState = await _gameStateRepository.GetAsync(gameId, ct);

    return Result.Ok(new MultiGameStateDTO
    {
        GameId = gameId,
        PlayerId = playerId,
        GameState = gameState.ToString(),
        // ... map all fields from gamePlayer
    });
}
```

#### 2.4 Add Client Endpoint Service
**File**: `Client.Core/Services/EndpointServices/GameplayEndpointsService.cs`
```csharp
Task<Result<MultiGameStateDTO>> GetMultiGameStateAsync(
    string gameId, string playerId, CancellationToken ct);
```

#### 2.5 Add RejoinGameAsync to ViewModel
**File**: `Client.Shared/ViewModels/MultiGameplayViewModel.cs`
```csharp
public async Task<bool> RejoinGameAsync(CancellationToken ct = default)
{
    var state = await _endpointsService.GetMultiGameStateAsync(
        _multiGameDataStore.GameId,
        _applicationViewModel.Player.Id,
        ct);

    if (!state.IsSuccess) return false;

    // Populate ViewModel from state
    Score = state.Value.Score;
    AvailablePlayHands = state.Value.HandsAvailable;
    AvailableDiscards = state.Value.DiscardsAvailable;
    PowerCharges = state.Value.PowerCharges;
    RefreshCardsInHand(state.Value.Cards);
    // ... etc

    // Rejoin SignalR group
    await _signalRNotificationService.JoinGameGroupAsync(_multiGameDataStore.GameId);

    return true;
}
```

---

### Iteration 3: Server-Side Grace Period (Most Complex)
**Goal**: Don't immediately kick players on disconnect. Give them time to reconnect.

#### 3.1 Add Player Status to GamePlayer
**File**: `Server.Core/Models/GamePlayer.cs`
```csharp
public enum PlayerConnectionStatus { Connected, Away, Disconnected }

public PlayerConnectionStatus ConnectionStatus { get; set; } = PlayerConnectionStatus.Connected;
public DateTimeOffset? DisconnectedAt { get; set; }
```

#### 3.2 Modify Disconnect Handler
**File**: `Server.WebAPI/SignalR/SignalRNotificationHub.cs`

Change `HandleGameDisconnect()`:
```csharp
// Instead of immediately removing player:
// await _gameService.LeaveGameAsync(gameId, userId, ct);

// Mark as "away" with timestamp:
await _gameService.MarkPlayerAwayAsync(gameId, userId, ct);

// Notify other players
await Clients.Group(gameId).SendAsync("PlayerAway", userId);
```

#### 3.3 Add MarkPlayerAwayAsync
**File**: `Server.BL/Services/GameService.cs`
```csharp
public async Task MarkPlayerAwayAsync(string gameId, string playerId, CancellationToken ct)
{
    var gamePlayer = await _gamePlayerRepository.GetAsync(playerId, ct);
    gamePlayer.ConnectionStatus = PlayerConnectionStatus.Away;
    gamePlayer.DisconnectedAt = DateTimeOffset.UtcNow;
    await _gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
}
```

#### 3.4 Add Reconnect Handler
**File**: `Server.WebAPI/SignalR/SignalRNotificationHub.cs`

In `OnConnectedAsync()` or via explicit hub method:
```csharp
public async Task ReconnectToGame(string gameId)
{
    var userId = Context.UserIdentifier;
    var result = await _gameService.ReconnectPlayerAsync(gameId, userId, ct);

    if (result.IsSuccess)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
        await Clients.Group(gameId).SendAsync("PlayerReconnected", userId);
    }
}
```

#### 3.5 Add ReconnectPlayerAsync
**File**: `Server.BL/Services/GameService.cs`
```csharp
public async Task<Result> ReconnectPlayerAsync(string gameId, string playerId, CancellationToken ct)
{
    var gamePlayer = await _gamePlayerRepository.GetAsync(playerId, ct);
    if (gamePlayer is null) return Result.Fail("Player not found");

    if (gamePlayer.ConnectionStatus == PlayerConnectionStatus.Disconnected)
        return Result.Fail("Player was fully removed from game");

    gamePlayer.ConnectionStatus = PlayerConnectionStatus.Connected;
    gamePlayer.DisconnectedAt = null;
    await _gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);

    return Result.Ok();
}
```

#### 3.6 Add Cleanup for Away Players
**File**: `Server.Infrastructure/BackgroundServices/CleanupBackgroundService.cs`

Add to periodic cleanup:
```csharp
// Remove players who have been "away" for > 60 seconds
var awayThreshold = DateTimeOffset.UtcNow.AddSeconds(-60);
foreach (var game in activeGames)
{
    var awayPlayers = game.Players
        .Where(p => p.ConnectionStatus == PlayerConnectionStatus.Away
                    && p.DisconnectedAt < awayThreshold);

    foreach (var player in awayPlayers)
    {
        await _gameService.LeaveGameAsync(game.Id, player.Id, ct);
        // Notify remaining players
    }
}
```

---

## Files to Modify (Summary)

### Client
| File | Change |
|------|--------|
| `Constants/LocalStorageConstants.cs` | Add `MultiGameStateKey` |
| `Services/MultiGameDataStore.cs` | Add persistence methods |
| `ViewModels/MultiGameplayViewModel.cs` | Add `RejoinGameAsync()` |
| `Pages/MultiGameplay.razor.cs` | Restore on init instead of reset |
| `Core/EndpointServices/GameplayEndpointsService.cs` | Add `GetMultiGameStateAsync()` |

### Server
| File | Change |
|------|--------|
| `Shared/DTOs/MultiGameStateDTO.cs` | New DTO |
| `Core/Models/GamePlayer.cs` | Add `ConnectionStatus`, `DisconnectedAt` |
| `WebAPI/EndpointGroups/GameplayEndpoints.cs` | Add `GetMultiGameState` |
| `BL/Services/GameService.cs` | Add `GetMultiGameStateAsync`, `MarkPlayerAwayAsync`, `ReconnectPlayerAsync` |
| `WebAPI/SignalR/SignalRNotificationHub.cs` | Modify disconnect, add reconnect |
| `Infrastructure/BackgroundServices/CleanupBackgroundService.cs` | Clean up away players |

---

## Open Questions for User

1. **Grace period duration**: How long should a player be "away" before being kicked? (Suggested: 60 seconds)

2. **UI for other players**: Should other players see when someone is "away" vs fully disconnected?

3. **Timer behavior**: When a player reconnects mid-round, should they:
   - Get the remaining server time (might be very short)
   - Get a small grace period added
   - Just continue from where they were

4. **Edge case - all players disconnect**: Should the game pause, or continue with bots, or just end?

---

## Recommended Implementation Order

1. **Iteration 1** (Client-only, quick win): Save/restore GameId, basic state preservation
2. **Iteration 2** (Server endpoint): GetMultiGameState for full state restoration
3. **Iteration 3** (Server grace period): Don't kick immediately, allow reconnect window

Each iteration is independently valuable and can be tested before moving to the next.
