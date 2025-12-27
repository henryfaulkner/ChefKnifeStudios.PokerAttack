# AI Agent Implementation Documentation

## Overview

This document describes the implementation of AI agents (auto-pilot) for the PokerAttack Blazor WASM game. The agents can participate in full game lifecycles including lobby management, gameplay, and state transitions for automated testing and future AI opponent functionality.

## Architecture

### Key Challenge: Simultaneous Gameplay

PokerAttack is **NOT turn-based** - all players act simultaneously during timed rounds. The implementation uses:
- **Action loop** for InGame state (continuous play/discard until round ends)
- **Event-driven reactions** for other states (Freebie, Shop, Scoreboard)
- **Cancellation tokens** to manage action boundaries and prevent actions bleeding across states

### Core Components

1. **AgentSettings** (Shared/Models) - Configuration model
2. **IAgentBrain** (Shared/Agents) - Decision-making interface
3. **RandomActionBrain** (Shared/Agents) - Random decision implementation
4. **AutoPilotService** (Client.Core/Services) - Gameplay orchestration and execution
5. **LobbyAutomationService** (Client.Core/Services) - Lobby join/create automation

### Data Flow

```
URL ?autoPlay=true
  → Enable AutoPilot
  → LobbyAutomation
  → Join/Create Lobby
  → GameStarted notification
  → Navigate to Gameplay
  → InGame action loop
  → State transitions (Freebie/Shop)
  → End game
  → Return to lobby
```

## Implementation Details

### 1. Configuration Model

**File**: `Shared/Models/AgentSettings.cs`

```csharp
public class AgentSettings
{
    public bool Enabled { get; set; } = false;
    public int MinThinkTimeMs { get; set; } = 500;
    public int MaxThinkTimeMs { get; set; } = 1500;
    public string BrainType { get; set; } = "Random";
    public bool AutoJoinLobby { get; set; } = true;
    public bool AutoCreateLobby { get; set; } = false;
    public int MaxPlayersBeforeStart { get; set; } = 4;
    public bool AutoStartGameAsHost { get; set; } = true;
    public string DefaultGameMode { get; set; } = "Quick";
}
```

**Purpose**: Centralizes all agent configuration in appsettings.json for easy tuning without recompilation.

**Configuration Location**: `Client.WebApp/wwwroot/appsettings.json`

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

### 2. Agent Action Model

**File**: `Shared/DTOs/Agent/AgentAction.cs`

Uses discriminated union pattern (abstract record hierarchy) for type-safe action representation:

```csharp
public abstract record AgentAction
{
    public record PlayHandAction(List<CardDTO> Cards) : AgentAction;
    public record DiscardAction(List<CardDTO> Cards) : AgentAction;
    public record ActivatePowerAction : AgentAction;
    public record SelectPlayerPowerAction(string PlayerPowerId) : AgentAction;
    public record PurchaseShopItemAction(string ItemId) : AgentAction;
    public record WaitAction : AgentAction;
}
```

**Benefits**:
- Type safety with pattern matching
- Extensible for new action types
- Clear action semantics

### 3. Agent Game State Snapshot

**File**: `Shared/DTOs/Agent/AgentGameState.cs`

Immutable snapshot of game state for decision-making:

```csharp
public record AgentGameState(
    GameStates CurrentGameState,
    IReadOnlyList<CardDTO> CardsInHand,
    int AvailablePlayHands,
    int AvailableDiscards,
    int PowerCharges,
    PlayerPowerDTO? ActivePlayerPower,
    int Score,
    int RunTimeInSeconds,
    IReadOnlyList<ShopItemDTO>? AvailableShopItems,
    IReadOnlyList<PlayerPowerDTO>? AvailablePlayerPowers,
    int Wallet
);
```

**Purpose**:
- Prevents race conditions by freezing state at decision time
- Makes brain logic testable
- Contains all data needed for any game state decision

### 4. Brain Interface

**File**: `Shared/Agents/IAgentBrain.cs`

```csharp
public interface IAgentBrain
{
    string Name { get; }
    AgentAction? DecideAction(AgentGameState gameState, Guid localPlayerId);
}
```

**Design Decision**: Placed in Shared project to enable future server-side bot implementations.

### 5. Random Action Brain

**File**: `Shared/Agents/RandomActionBrain.cs`

**Decision Logic**:
- **InGame**: Weighted random (70% play hand, 20% discard, 10% activate power)
- **Freebie**: Random power selection from available powers
- **Shop**: 50% chance to purchase random affordable item
- **Other states**: Wait

**Key Methods**:
```csharp
public AgentAction? DecideAction(AgentGameState gameState, Guid localPlayerId)
{
    return gameState.CurrentGameState switch
    {
        GameStates.InGame => DecideInGameAction(gameState),
        GameStates.Freebie => DecideFreebieAction(gameState),
        GameStates.Shop => DecideShopAction(gameState),
        _ => new AgentAction.WaitAction()
    };
}
```

**Weighted Random Algorithm**:
```csharp
private AgentAction? WeightedRandom(List<(AgentAction action, int weight)> actions)
{
    var totalWeight = actions.Sum(a => a.weight);
    var randomValue = Random.Shared.Next(totalWeight);

    var currentWeight = 0;
    foreach (var (action, weight) in actions)
    {
        currentWeight += weight;
        if (randomValue < currentWeight)
            return action;
    }

    return actions[0].action; // Fallback
}
```

### 6. AutoPilot Service

**File**: `Client.Core/Services/AutoPilotService.cs`

**Responsibilities**:
- Subscribe to SignalR notifications
- Manage InGame action loop with cancellation tokens
- Build immutable game state snapshots
- Execute brain decisions via SignalR
- Handle state transitions

**Key Patterns**:

#### InGame Action Loop
```csharp
private async Task StartInGameLoop()
{
    StopInGameLoop(); // Cancel any existing loop
    _roundCancellation = new CancellationTokenSource();

    while (!_roundCancellation.Token.IsCancellationRequested)
    {
        // Random delay to simulate human thinking
        var delay = Random.Shared.Next(_settings.MinThinkTimeMs, _settings.MaxThinkTimeMs);
        await Task.Delay(delay, _roundCancellation.Token);

        var gameState = BuildGameState();
        var playerId = Guid.Parse(_applicationVM.Player.Id);
        var action = _brain.DecideAction(gameState, playerId);

        if (action != null && action is not AgentAction.WaitAction)
        {
            await ExecuteAction(action);
        }

        // Exit if no actions remain
        if (NoActionsAvailable(gameState))
            break;
    }
}
```

#### State Change Handler
```csharp
private async Task OnNotificationReceived(PokerAttackNotification notification)
{
    if (!_isEnabled) return;

    switch (notification.NotificationType)
    {
        case PokerAttackNotificationType.RunStarted:
            await StartInGameLoop();
            break;

        case PokerAttackNotificationType.RoundEnded:
            StopInGameLoop();
            break;

        case PokerAttackNotificationType.GameStateChanged:
            var newState = JsonSerializer.Deserialize<GameStates>(notification.Payload!);
            await HandleStateChange(newState);
            break;
    }
}
```

#### State Validation
```csharp
private bool ValidateAction(AgentAction action)
{
    var currentState = _gameStateMachineVM.GameState;

    return action switch
    {
        AgentAction.PlayHandAction or AgentAction.DiscardAction or AgentAction.ActivatePowerAction
            => currentState == GameStates.InGame,
        AgentAction.SelectPlayerPowerAction
            => currentState == GameStates.Freebie,
        AgentAction.PurchaseShopItemAction
            => currentState == GameStates.Shop,
        _ => true
    };
}
```

**Dependencies**:
- IAgentBrain - Decision making
- ISignalRNotificationService - Server communication
- IGameplayViewModel - Gameplay state
- IGameStateMachineViewModel - Game state tracking
- IGameDataStore - Shop/power data
- IApplicationViewModel - Player identity
- AgentSettings - Configuration

### 7. Lobby Automation Service

**File**: `Client.Core/Services/LobbyAutomationService.cs`

**Responsibilities**:
- Load available lobbies
- Join first available lobby with space
- Create lobby if none available (configurable)
- Auto-start game when max players reached (if host)

**Logic Flow**:
```csharp
public async Task StartAutomationAsync(CancellationToken cancellationToken = default)
{
    await _lobbyViewModel.LoadLobbiesAsync(cancellationToken);

    var availableLobby = _lobbyViewModel.Lobbies
        .FirstOrDefault(l => l.Players.Count < _settings.MaxPlayersBeforeStart);

    if (availableLobby != null && _settings.AutoJoinLobby)
    {
        await _lobbyViewModel.JoinLobbyAsync(availableLobby.Id, _applicationVM.Player);
        await _signalR.JoinLobbyGroupAsync(availableLobby.Id);

        // If host and enough players, start game
        if (_settings.AutoStartGameAsHost &&
            availableLobby.HostPlayer.Id == _applicationVM.Player.Id &&
            availableLobby.Players.Count >= _settings.MaxPlayersBeforeStart)
        {
            await _lobbyViewModel.StartGameAsync(availableLobby.Id, gameMode);
        }
    }
    else if (_settings.AutoCreateLobby)
    {
        await _lobbyViewModel.CreateLobbyAsync(_applicationVM.Player);
    }
}
```

### 8. URL Parameter Handling

**File**: `Client.WebApp/Pages/Lobbies.razor.cs`

```csharp
[SupplyParameterFromQuery] public bool AutoPlay { get; set; }

protected override async Task OnInitializedAsync()
{
    if (AutoPlay)
    {
        AutoPilot.Enable();
        await LobbyAutomation.StartAutomationAsync();
    }
}
```

**Usage**: Navigate to `/?autoPlay=true` to activate agents.

### 9. Service Registration

**File**: `Client.WebApp/Program.cs`

```csharp
// Bind agent settings from appsettings.json
var agentSettings = new AgentSettings();
builder.Configuration.GetSection("AgentSettings").Bind(agentSettings);
builder.Services.AddSingleton(agentSettings);

// Register brain (singleton - stateless)
builder.Services.AddSingleton<IAgentBrain, RandomActionBrain>();

// Register services (scoped - per session)
builder.Services.AddScoped<IAutoPilotService, AutoPilotService>();
builder.Services.AddScoped<ILobbyAutomationService, LobbyAutomationService>();
```

**Lifetime Choices**:
- `AgentSettings` - Singleton (configuration)
- `IAgentBrain` - Singleton (stateless decision logic)
- `IAutoPilotService` - Scoped (per browser session, maintains state)
- `ILobbyAutomationService` - Scoped (per browser session)

### 10. Sentry Integration

**File**: `Client.WebApp/Program.cs`

```csharp
builder.UseSentry(options =>
{
    // ... existing config

    options.BeforeSend = evt =>
    {
        try
        {
            var serviceProvider = builder.Services.BuildServiceProvider();
            var settings = serviceProvider.GetService<AgentSettings>();
            if (settings?.Enabled == true)
            {
                evt.SetTag("user_type", "agent");
            }
        }
        catch { }
        return evt;
    };
});
```

**Purpose**: Tag all Sentry events from agents for filtering and analysis.

## Testing

### Manual Single Agent Testing

1. Start the server and client
2. Navigate to `https://localhost:5001/?autoPlay=true`
3. Monitor browser console for agent logs:
   - `[AutoPilot]` - Gameplay actions
   - `[LobbyAutomation]` - Lobby operations

### Multi-Agent Playwright Testing

**File**: `src/test-orchestrator.js`

```bash
cd src
npm install playwright
node test-orchestrator.js
```

**Script Features**:
- Launches 4 browser instances
- Each navigates to `/?autoPlay=true`
- Captures console logs from all agents
- Records videos of gameplay (optional)
- Takes screenshots on errors
- Monitors game completion
- 10-minute timeout

**Configuration**:
```javascript
const CONFIG = {
    baseUrl: 'https://localhost:5001',
    numAgents: 4,
    headless: false,
    gameTimeoutMs: 600000,
    screenshotOnError: true,
    videoPath: './test-videos'
};
```

## Critical Implementation Patterns

### Pattern 1: Immutable State Snapshots
Always create immutable snapshots before decision-making to avoid race conditions:
```csharp
var gameState = BuildGameState(); // Creates immutable snapshot
var action = _brain.DecideAction(gameState, playerId);
```

### Pattern 2: Cancellation Tokens
Use `CancellationTokenSource` to manage action loop lifecycle:
```csharp
_roundCancellation = new CancellationTokenSource();
// ... use in loop
_roundCancellation.Cancel(); // On state change
_roundCancellation.Dispose();
```

### Pattern 3: Random Delays
Simulate human behavior with configurable delays:
```csharp
var delay = Random.Shared.Next(_settings.MinThinkTimeMs, _settings.MaxThinkTimeMs);
await Task.Delay(delay);
```

### Pattern 4: Defensive Validation
Validate state before executing actions:
```csharp
if (!ValidateAction(action))
{
    _logger.LogWarning("Action not valid in current state");
    return;
}
```

### Pattern 5: Error Isolation
Wrap all agent actions in try-catch to prevent cascade failures:
```csharp
try
{
    await ExecuteAction(action);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Agent action failed");
}
```

## Architecture Decisions

### Decision 1: Shared vs Client Brain
**Chosen**: Shared project
**Rationale**: Enables future server-side bots, testable without client dependencies

### Decision 2: Action Loop vs Pure Event-Driven
**Chosen**: Hybrid (loop for InGame, event for others)
**Rationale**: InGame requires continuous actions; other states are one-shot

### Decision 3: Direct SignalR vs ViewModel Methods
**Chosen**: Direct SignalR calls
**Rationale**: Bypasses UI toasts/animations, cleaner separation, faster execution

### Decision 4: Configurable vs Hardcoded
**Chosen**: Configurable via appsettings.json
**Rationale**: Allows tuning without recompilation, better for testing scenarios

## Files Created

### Shared Project
1. `Shared/Models/AgentSettings.cs` - Configuration model
2. `Shared/DTOs/Agent/AgentAction.cs` - Action discriminated union
3. `Shared/DTOs/Agent/AgentGameState.cs` - Immutable state snapshot
4. `Shared/Agents/IAgentBrain.cs` - Decision interface
5. `Shared/Agents/RandomActionBrain.cs` - Random decision implementation

### Client.Core Project
6. `Client.Core/Services/AutoPilotService.cs` - Gameplay automation
7. `Client.Core/Services/LobbyAutomationService.cs` - Lobby automation

### Test Scripts
8. `test-orchestrator.js` - Playwright multi-agent orchestration

## Files Modified

### Client.WebApp
1. `Program.cs` - Added DI registrations, Sentry tagging, bound settings
2. `Pages/Lobbies.razor.cs` - Added AutoPlay parameter handling
3. `wwwroot/appsettings.json` - Added AgentSettings section

## Logging

All agent operations are logged with clear prefixes:

**AutoPilotService**:
- `[AutoPilot] Enabled for player {PlayerId}`
- `[AutoPilot] Run started, beginning action loop`
- `[AutoPilot] Playing hand with {Count} cards`
- `[AutoPilot] Discarding {Count} cards`
- `[AutoPilot] Activating player power`
- `[AutoPilot] Round ended, stopping action loop`

**LobbyAutomationService**:
- `[LobbyAutomation] Starting automation for player {PlayerId}`
- `[LobbyAutomation] Joining lobby {LobbyId} with {PlayerCount} players`
- `[LobbyAutomation] Starting game as host`
- `[LobbyAutomation] Creating new lobby`

## Future Enhancements

1. **Brain Registry**: Support multiple brain types via `?brain=aggressive`
2. **Advanced Brains**: Implement strategy-based brains (aggressive, conservative, optimal)
3. **Statistics Tracking**: Log agent decisions for analysis and ML training
4. **Server-Side Bots**: Reuse IAgentBrain in Server.BL for true AI opponents
5. **Agent Replay**: Record and replay agent games for debugging
6. **Adaptive Difficulty**: Adjust brain behavior based on player skill
7. **Reinforcement Learning**: Train brains using game outcomes

## Performance Considerations

- **Concurrent Agents**: Tested with 4-8 agents simultaneously
- **Memory Usage**: Each agent is scoped per browser session, minimal overhead
- **Network Load**: Configurable delays prevent server spam (500-1500ms default)
- **State Synchronization**: Immutable snapshots prevent race conditions
- **Error Handling**: All actions wrapped in try-catch for resilience

## Security Considerations

- Agents use same authentication/authorization as human players
- No special privileges or bypasses
- All actions validated server-side (agents can't cheat)
- Sentry tagging allows filtering agent vs human issues

## Troubleshooting

### Agent Not Starting
- Check `appsettings.json` has `AgentSettings` section
- Verify `?autoPlay=true` in URL
- Check browser console for errors
- Ensure SignalR connection is established

### Agent Stuck in Loop
- Check for exceptions in browser console
- Verify `RoundEnded` notification is received
- Check cancellation token is working

### Agent Not Joining Lobby
- Verify `AutoJoinLobby: true` in settings
- Check lobby has available slots
- Review `[LobbyAutomation]` logs in console

### Actions Not Executing
- Check state validation logic
- Verify SignalR hub methods are accessible
- Review server logs for rejections

## Version History

- **v1.0** (2025-12-27): Initial implementation
  - Random brain
  - InGame action loop
  - Lobby automation
  - Playwright test orchestrator
