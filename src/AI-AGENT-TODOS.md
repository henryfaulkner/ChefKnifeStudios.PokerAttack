# AI Agent Implementation TODOs

## Overview

This document tracks remaining work items, known issues, and future enhancements for the AI Agent system.

---

## 🔴 Critical TODOs (Blocks Full Functionality)

### 1. Implement SelectPlayerPowerAction

**Location**: `Client.Core/Services/AutoPilotService.cs:219`

**Current Code**:
```csharp
case AgentAction.SelectPlayerPowerAction selectPower:
    _logger.LogInformation("[AutoPilot] Selecting power {PowerId}", selectPower.PlayerPowerId);
    // TODO: Implement power selection once endpoint is identified
    _logger.LogWarning("[AutoPilot] SelectPlayerPowerAction not yet implemented");
    break;
```

**Required Action**:
- Identify the correct endpoint or service method for selecting a player power during Freebie state
- Likely in `IPlayerPowerEndpointsService` or similar
- May need to trigger UI interaction via `IEventNotificationService`

**Investigation Steps**:
1. Search for existing power selection logic in UI components
2. Check `PlayerPowerList.razor` component for selection handling
3. Review `IPlayerPowerEndpointsService` interface
4. Check SignalR hub for power selection methods

**Expected Implementation**:
```csharp
case AgentAction.SelectPlayerPowerAction selectPower:
    _logger.LogInformation("[AutoPilot] Selecting power {PowerId}", selectPower.PlayerPowerId);
    // Option 1: Direct endpoint call
    await _playerPowerEndpointsService.SelectPowerAsync(playerId, selectPower.PlayerPowerId);

    // OR Option 2: Via SignalR
    await _signalR.SelectPlayerPowerAsync(gameId, playerId, selectPower.PlayerPowerId);

    // OR Option 3: Via event system
    _eventNotificationService.Trigger("PowerSelected", selectPower.PlayerPowerId);
    break;
```

**Impact**: Agents cannot select powers during Freebie state, will be stuck without active power.

**Priority**: HIGH

---

### 2. Implement PurchaseShopItemAction

**Location**: `Client.Core/Services/AutoPilotService.cs:223`

**Current Code**:
```csharp
case AgentAction.PurchaseShopItemAction purchase:
    _logger.LogInformation("[AutoPilot] Purchasing item {ItemId}", purchase.ItemId);
    // TODO: Implement shop purchase once endpoint is identified
    _logger.LogWarning("[AutoPilot] PurchaseShopItemAction not yet implemented");
    break;
```

**Required Action**:
- Identify the correct endpoint for purchasing shop items
- Likely in `IShopEndpointsService`
- Implement purchase logic

**Investigation Steps**:
1. Search for existing shop purchase logic in UI components
2. Check `Shop.razor` component for purchase handling
3. Review `IShopEndpointsService` interface methods
4. Check SignalR hub for shop-related methods

**Expected Implementation**:
```csharp
case AgentAction.PurchaseShopItemAction purchase:
    _logger.LogInformation("[AutoPilot] Purchasing item {ItemId}", purchase.ItemId);

    // Option 1: Direct endpoint call
    await _shopEndpointsService.PurchaseItemAsync(playerId, purchase.ItemId);

    // OR Option 2: Via SignalR
    await _signalR.PurchaseShopItemAsync(gameId, playerId, purchase.ItemId);
    break;
```

**Impact**: Agents cannot purchase shop items, missing strategic advantage from items.

**Priority**: MEDIUM (Shop is optional, agents can still complete games)

---

## 🟡 Medium Priority TODOs

### 3. Add Dependency Injection for Missing Services

**Issue**: AutoPilotService may need additional services that aren't currently injected.

**Location**: `Client.Core/Services/AutoPilotService.cs` constructor

**Required Services** (to be confirmed):
- `IPlayerPowerEndpointsService` - For power selection
- `IShopEndpointsService` - For shop purchases
- `IEventNotificationService` - For triggering UI events (if needed)

**Action Required**:
```csharp
public AutoPilotService(
    IAgentBrain brain,
    ISignalRNotificationService signalR,
    IGameplayViewModel gameplayVM,
    IGameStateMachineViewModel gameStateMachineVM,
    IGameDataStore gameDataStore,
    IApplicationViewModel applicationVM,
    AgentSettings settings,
    ILogger<AutoPilotService> logger,
    // Add these:
    IPlayerPowerEndpointsService playerPowerEndpoints,
    IShopEndpointsService shopEndpoints)
{
    // ... existing initialization
    _playerPowerEndpoints = playerPowerEndpoints;
    _shopEndpoints = shopEndpoints;
}
```

**Priority**: MEDIUM

---

### 4. Handle Edge Cases in LobbyAutomationService

**Location**: `Client.Core/Services/LobbyAutomationService.cs`

**Known Edge Cases**:

#### 4.1 Race Condition: Multiple Agents Joining Same Lobby
**Issue**: Multiple agents may try to join the same lobby simultaneously, potentially exceeding max players.

**Current Code**:
```csharp
var availableLobby = _lobbyViewModel.Lobbies
    .FirstOrDefault(l => l.Players.Count < _settings.MaxPlayersBeforeStart);
```

**Improvement**:
```csharp
// Add buffer to prevent overfilling
var availableLobby = _lobbyViewModel.Lobbies
    .Where(l => l.Players.Count < _settings.MaxPlayersBeforeStart - 1) // Leave room for race
    .OrderBy(l => l.Players.Count) // Prefer emptier lobbies
    .FirstOrDefault();
```

#### 4.2 Host Leaving Before Game Starts
**Issue**: If agent is host and leaves, lobby may become orphaned.

**Suggestion**: Add cleanup/shutdown logic if agent exits unexpectedly.

**Priority**: MEDIUM

---

### 5. Improve RandomActionBrain Decision Quality

**Location**: `Shared/Agents/RandomActionBrain.cs`

**Current Issues**:
- Random hand selection may not form valid poker hands
- Doesn't consider card values or suits
- Discard logic is purely random

**Improvements Needed**:

#### 5.1 Basic Hand Evaluation
```csharp
private List<CardDTO> SelectRandomHand(IReadOnlyList<CardDTO> cardsInHand)
{
    // TODO: Add basic poker hand recognition
    // - Try to form pairs, straights, flushes
    // - Avoid selecting unrelated cards

    // Current: Random selection
    var handSize = Random.Shared.Next(1, Math.Min(6, cardsInHand.Count + 1));
    return cardsInHand.OrderBy(_ => Random.Shared.Next()).Take(handSize).ToList();
}
```

#### 5.2 Intelligent Discard
```csharp
private List<CardDTO> SelectRandomDiscard(IReadOnlyList<CardDTO> cardsInHand)
{
    // TODO: Discard low-value cards first
    // - Prioritize keeping high cards (A, K, Q)
    // - Keep cards that might form hands

    // Current: Random discard
    var discardCount = Random.Shared.Next(1, Math.Min(4, cardsInHand.Count + 1));
    return cardsInHand.OrderBy(_ => Random.Shared.Next()).Take(discardCount).ToList();
}
```

**Priority**: MEDIUM (Works for testing, but poor gameplay)

---

## 🟢 Low Priority TODOs (Future Enhancements)

### 6. Implement Brain Registry

**Goal**: Support multiple brain types via URL parameter.

**Usage**: `/?autoPlay=true&brain=aggressive`

**Implementation**:

#### 6.1 Create Brain Registry Interface
```csharp
// File: Shared/Agents/IAgentBrainRegistry.cs
public interface IAgentBrainRegistry
{
    void Register(string name, IAgentBrain brain);
    IAgentBrain Get(string name);
    IEnumerable<string> ListAvailable();
}
```

#### 6.2 Implement Registry
```csharp
// File: Shared/Agents/AgentBrainRegistry.cs
public class AgentBrainRegistry : IAgentBrainRegistry
{
    private readonly Dictionary<string, IAgentBrain> _brains = new();

    public void Register(string name, IAgentBrain brain) => _brains[name] = brain;
    public IAgentBrain Get(string name) => _brains.TryGetValue(name, out var brain) ? brain : _brains["Random"];
    public IEnumerable<string> ListAvailable() => _brains.Keys;
}
```

#### 6.3 Update Service Registration
```csharp
// File: Client.WebApp/Program.cs
builder.Services.AddSingleton<IAgentBrainRegistry>(sp =>
{
    var registry = new AgentBrainRegistry();
    registry.Register("Random", new RandomActionBrain());
    registry.Register("Aggressive", new AggressiveBrain());
    registry.Register("Conservative", new ConservativeBrain());
    return registry;
});
```

#### 6.4 Update AutoPilotService
```csharp
// Constructor: Inject IAgentBrainRegistry instead of IAgentBrain
// On Enable: Select brain based on URL parameter or settings
```

**Priority**: LOW

---

### 7. Implement Advanced Brain Types

#### 7.1 AggressiveBrain
**Strategy**:
- Always play maximum cards (5-card hands)
- Rarely discard
- Always activate power when available
- Purchase most expensive items in shop

**File**: `Shared/Agents/AggressiveBrain.cs`

#### 7.2 ConservativeBrain
**Strategy**:
- Play small hands (1-2 cards)
- Discard frequently to get better cards
- Only activate power when optimal
- Purchase cheap, defensive items

**File**: `Shared/Agents/ConservativeBrain.cs`

#### 7.3 OptimalBrain
**Strategy**:
- Evaluate poker hand quality
- Make mathematically optimal decisions
- Consider probability and expected value
- Strategic item purchases

**File**: `Shared/Agents/OptimalBrain.cs`

**Priority**: LOW

---

### 8. Add Agent Statistics Tracking

**Goal**: Track agent decisions for analysis and improvement.

**Implementation**:

#### 8.1 Create Statistics Model
```csharp
// File: Shared/Models/AgentStatistics.cs
public class AgentStatistics
{
    public Guid AgentId { get; set; }
    public string BrainType { get; set; }
    public int GamesPlayed { get; set; }
    public int GamesWon { get; set; }
    public int TotalScore { get; set; }
    public int HandsPlayed { get; set; }
    public int DiscardsMade { get; set; }
    public int PowersActivated { get; set; }
    public int ItemsPurchased { get; set; }
    public Dictionary<string, int> ActionCounts { get; set; }
}
```

#### 8.2 Create Statistics Service
```csharp
// File: Client.Core/Services/AgentStatisticsService.cs
public interface IAgentStatisticsService
{
    void RecordAction(AgentAction action);
    void RecordGameResult(bool won, int finalScore);
    AgentStatistics GetStatistics();
    void Reset();
}
```

#### 8.3 Integrate with AutoPilotService
```csharp
// In ExecuteAction method:
await _statisticsService.RecordAction(action);
```

**Priority**: LOW

---

### 9. Server-Side Bot Support

**Goal**: Reuse IAgentBrain for server-side AI opponents.

**Benefits**:
- No browser required
- Lower latency
- Can run 100+ bots for load testing
- True single-player AI opponents

**Implementation**:

#### 9.1 Create Server-Side Bot Service
```csharp
// File: Server.BL/Services/BotService.cs
public class BotService
{
    private readonly IAgentBrain _brain;
    private readonly IGameService _gameService;

    public async Task PlayTurn(string gameId, string botPlayerId)
    {
        var gameState = await BuildGameState(gameId, botPlayerId);
        var action = _brain.DecideAction(gameState, Guid.Parse(botPlayerId));

        switch (action)
        {
            case AgentAction.PlayHandAction playHand:
                await _gameService.PlayHandAsync(botPlayerId, playHand.Cards);
                break;
            // ... other actions
        }
    }
}
```

#### 9.2 Register in Server
```csharp
// File: Server.WebAPI/Program.cs
builder.Services.AddSingleton<IAgentBrain, RandomActionBrain>();
builder.Services.AddScoped<IBotService, BotService>();
```

**Priority**: LOW (Future feature)

---

### 10. Agent Replay System

**Goal**: Record and replay agent games for debugging.

**Implementation**:

#### 10.1 Create Replay Recorder
```csharp
// File: Client.Core/Services/AgentReplayRecorder.cs
public class AgentReplayRecorder
{
    private List<AgentReplayFrame> _frames = new();

    public void RecordFrame(AgentGameState state, AgentAction action)
    {
        _frames.Add(new AgentReplayFrame
        {
            Timestamp = DateTime.UtcNow,
            GameState = state,
            Action = action
        });
    }

    public void SaveReplay(string filename)
    {
        var json = JsonSerializer.Serialize(_frames);
        File.WriteAllText(filename, json);
    }
}
```

#### 10.2 Create Replay Player
```csharp
// File: Client.Core/Services/AgentReplayPlayer.cs
public class AgentReplayPlayer
{
    public async Task PlayReplay(string filename)
    {
        var json = await File.ReadAllTextAsync(filename);
        var frames = JsonSerializer.Deserialize<List<AgentReplayFrame>>(json);

        foreach (var frame in frames)
        {
            // Restore game state and execute action
            await Task.Delay(1000); // Slow playback
        }
    }
}
```

**Priority**: LOW

---

## 🔵 Known Issues

### Issue 1: Agent May Act After State Transition
**Description**: Small race condition window where agent may send action just as state changes.

**Workaround**: State validation in `ExecuteAction` prevents invalid actions.

**Long-term Fix**: Add debounce/cooldown period after state transitions.

**Severity**: LOW (Validation catches it)

---

### Issue 2: Multiple Agents May Create Multiple Lobbies
**Description**: If all agents have `AutoCreateLobby: true` and no lobbies exist, they all create separate lobbies.

**Workaround**: Set only one agent to `AutoCreateLobby: true`.

**Long-term Fix**: Implement lobby creation lock or retry logic.

**Severity**: LOW (User configuration)

---

### Issue 3: Agent Timing May Feel Unnatural
**Description**: Random delays (500-1500ms) may not feel human-like enough.

**Improvement**: Use normal distribution instead of uniform random:
```csharp
// Instead of Random.Shared.Next(min, max)
// Use normal distribution centered at 1000ms with std dev 300ms
var delay = Math.Clamp((int)(Random.Shared.NextNormal(1000, 300)), 500, 2000);
```

**Severity**: LOW (Aesthetic)

---

## 📋 Testing Checklist

### Manual Testing
- [ ] Single agent completes full game loop
- [ ] Agent joins existing lobby
- [ ] Agent creates lobby when none available
- [ ] Agent selects power during Freebie state (BLOCKED by TODO #1)
- [ ] Agent purchases items during Shop state (BLOCKED by TODO #2)
- [ ] Agent handles elimination gracefully
- [ ] Agent disconnects/reconnects properly
- [ ] Browser console shows clear logs
- [ ] Sentry tags events correctly

### Multi-Agent Testing
- [ ] 2 agents complete game together
- [ ] 4 agents complete game together
- [ ] 8 agents complete game together
- [ ] Agents don't overfill lobbies
- [ ] No race conditions or deadlocks
- [ ] Server handles load appropriately
- [ ] No memory leaks after multiple games
- [ ] Playwright orchestrator works

### Edge Case Testing
- [ ] Agent behavior when network disconnects
- [ ] Agent behavior when server restarts mid-game
- [ ] Agent behavior with invalid configurations
- [ ] Agent behavior with missing dependencies
- [ ] Agent behavior during rapid state transitions

---

## 🎯 Prioritization Summary

### Phase 1: Make It Work (Complete Core Functionality)
1. ✅ Core infrastructure
2. ✅ Random brain implementation
3. ✅ Lobby automation
4. ⏳ **SelectPlayerPowerAction** (TODO #1)
5. ⏳ **PurchaseShopItemAction** (TODO #2)

### Phase 2: Make It Right (Improve Quality)
1. Dependency injection cleanup (TODO #3)
2. Edge case handling (TODO #4)
3. Better decision quality (TODO #5)

### Phase 3: Make It Fast (Optimize & Extend)
1. Brain registry (TODO #6)
2. Advanced brains (TODO #7)
3. Statistics tracking (TODO #8)
4. Server-side bots (TODO #9)
5. Replay system (TODO #10)

---

## 📞 Questions for Project Team

1. **Power Selection**: How do users currently select powers in Freebie state? Is it via endpoint or UI event?

2. **Shop Purchases**: What is the endpoint signature for purchasing shop items?

3. **Testing Strategy**: What level of agent intelligence is needed for testing vs AI opponents?

4. **Performance Targets**: What's the expected number of concurrent agents in production testing?

5. **Deployment**: Will agents run in separate containers or same as regular clients?

---

## 📝 Notes

- All TODOs are tracked with `// TODO:` comments in code
- Priority levels: HIGH (blocks functionality), MEDIUM (degrades experience), LOW (nice to have)
- All improvements should maintain backward compatibility
- New brain implementations should implement `IAgentBrain` interface
- All agent actions should be logged for debugging

---

**Last Updated**: 2025-12-27
**Document Version**: 1.0
