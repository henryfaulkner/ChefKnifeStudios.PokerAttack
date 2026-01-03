# Result Pattern Migration Status

## Overview
Migrating server-side codebase from exception-based error handling to Ardalis.Result pattern with minimal strategic logging.

**Started:** Previous sessions
**Last Updated:** 2026-01-03
**Branch:** server-result-pattern

## Migration Goals
1. Replace exceptions with Result<T> pattern for cleaner error handling
2. Add minimal strategic logging (critical operations only)
3. Improve HTTP status code mapping (404, 409, 400, 500)
4. Enable better debugging with Sentry integration

## Logging Strategy (User-Approved)
- **Level:** Minimal - Critical operations only
- **What to log:**
  - All exceptions with context
  - Security events
  - Critical operations: lobby created/shutdown, game start/end, player joined, round start/end, eliminations, purchases
- **What NOT to log:**
  - Performance metrics
  - Verbose operation details
  - Success paths (except critical operations)

## Result Type Mapping Conventions
| Exception Type | Result Type | HTTP Status |
|---------------|-------------|-------------|
| KeyNotFoundException | Result.NotFound() | 404 |
| InvalidOperationException (conflict) | Result.Conflict() | 409 |
| InvalidOperationException (validation) | Result.Invalid(new ValidationError()) | 400 |
| ApplicationException / General | Result.Error() | 500 |

## Phase 1: Foundation Layer ✅ COMPLETE

### Package Installation
- ✅ Added Ardalis.Result v10.1.0 to BL project
- ✅ Added Ardalis.Result v10.1.0 to Core project

### Repository Pattern Migration
**File:** `Server/ChefKnifeStudios.PokerAttack.Server.Core/Interfaces/IKeyValueRepository.cs`
- ✅ All methods return Result<T> or Result
- Pattern:
  ```csharp
  Task<Result> AddAsync(string key, TValue value, CancellationToken ct = default);
  Task<Result<TValue?>> GetAsync(string key, CancellationToken ct = default);
  Task<Result> UpdateAsync(string key, TValue value, CancellationToken ct = default);
  Task<Result> DeleteAsync(string key, CancellationToken ct = default);
  Task<Result<IReadOnlyDictionary<string, TValue>>> GetAllAsync(CancellationToken ct = default);
  ```

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.Infrastructure/InMemoryKeyValueRepository.cs`
- ✅ Migrated 2 throw sites:
  - AddAsync: Duplicate key → Result.Conflict()
  - UpdateAsync: Missing key → Result.NotFound()

### Utility Classes Migration

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.Infrastructure/PlayerPowers/PlayerPowerEffectRegistry.cs`
- ✅ Get() returns Result<IPlayerPowerEffect>
- Effect not found → Result.NotFound()

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.BL/HandEvaluator.cs`
- ✅ EvaluateHand() returns Result<HandResult>
- Invalid card count → Result.Invalid(new ValidationError())

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.BL/MappingExtensions.cs`
- ✅ MapToDTO for Round returns Result<RoundDTO>
- Null model → Result.Invalid(new ValidationError())

## Phase 2: State Machine Service ✅ COMPLETE

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/GameStateMachineService.cs`
- ✅ Migrated 2 throw sites:
  - TransitionAsync: Game not found → Result.NotFound()
  - TransitionAsync: Invalid transition → Result.Invalid()
- ✅ Added state transition logging:
  ```csharp
  logger.LogInformation(
      "Game state transition: GameId={GameId}, From={FromState}, To={ToState}, Event={GameEvent}",
      gameId, gameState, transition.NextState, gameEvent);
  ```

## Phase 3: Lobby & Shop Services (IN PROGRESS)

### LobbyService ✅ COMPLETE

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/LobbyService.cs`

**Migrated Methods (9 public + 1 private helper):**

1. **CreateLobbyAsync** (Lines 44-89)
   - Return type: `Task<Result<LobbyDTO>>`
   - Migrated: Repository.GetAsync, Repository.AddAsync
   - Logging: ✅ Lobby created with LobbyId, HostPlayerId, HostPlayerName
   - Logging: ⚠️ Warning for duplicate lobby creation attempts

2. **GetLobbyAsync** (Lines 92-99)
   - Return type: `Task<Result<LobbyDTO>>`
   - Migrated: Repository.GetAsync → Result.NotFound()

3. **GetLobbiesAsync** (Lines 101-108)
   - Return type: `Task<Result<IEnumerable<LobbyDTO>>>`
   - Migrated: Repository.GetAllAsync

4. **JoinLobbyAsync** (Lines 110-157)
   - Return type: `Task<Result>`
   - Migrated: Repository.GetAsync, RemovePlayerFromAllLobbiesAsync, Repository.UpdateAsync
   - Logging: ✅ Player joined with LobbyId, PlayerId, PlayerName, TotalPlayers
   - Logging: ⚠️ Warnings for lobby not found, player already in lobby

5. **LeaveLobbyAsync (PlayerDTO)** (Lines 159-213)
   - Return type: `Task<Result>`
   - Migrated: Repository.GetAllAsync, ShutDownLobbyAsync call, Repository.UpdateAsync

6. **LeaveLobbyAsync (string, PlayerDTO)** (Lines 215-257)
   - Return type: `Task<Result>`
   - Migrated: 1 throw site (line 208) → Result.NotFound()
   - Migrated: Repository.GetAsync, ShutDownLobbyAsync call, Repository.UpdateAsync

7. **ShutDownLobbyAsync** (Lines 259-295)
   - Return type: `Task<Result<IEnumerable<PlayerDTO>>>`
   - Migrated: Repository.GetAsync, Repository.DeleteAsync
   - Logging: ✅ Lobby shutdown with LobbyId, Reason

8. **GetPlayersAsync** (Lines 297-304)
   - Return type: `Task<Result<IEnumerable<PlayerDTO>>>`
   - Migrated: Repository.GetAsync

9. **UpdatePlayerAsync** (Lines 306-366)
   - Return type: `Task<Result>`
   - Migrated: 1 throw site (line 301) → Result.NotFound()
   - Migrated: Repository.GetAllAsync, Repository.UpdateAsync

10. **StartGameAsync** (Lines 368-491)
    - Return type: `Task<Result<string>>`
    - Migrated: 3 throw sites:
      - Line 335: GetLobbyAsync → Result.NotFound()
      - Line 344: Active game exists → Result.Conflict()
      - Line 404: Game not found → Result.NotFound()
    - Migrated: Multiple repository operations
    - Logging: ✅ Game started with LobbyId, GameId, GameMode, PlayerCount
    - Logging: ⚠️ Warning for lobby already has active game

11. **RemovePlayerFromAllLobbiesAsync** (Lines 442-497) - Private helper
    - Return type: `Task<Result>`
    - Migrated: Repository.GetAllAsync, Repository.DeleteAsync, Repository.UpdateAsync

**Total throw sites migrated:** 8
- 2x KeyNotFoundException → Result.NotFound()
- 2x InvalidOperationException → Result.Conflict()
- 4x Repository failures → Result.Error()

### LobbyEndpoints ✅ COMPLETE

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/EndpointGroups/LobbyEndpoints.cs`

**Updated Endpoints (8 total):**
1. ✅ GetLobbies - Direct return Result<IEnumerable<LobbyDTO>>
2. ✅ GetLobby - Direct return Result<LobbyDTO>
3. ✅ CreateLobby - Direct return Result<LobbyDTO>
4. ✅ AddPlayer (JoinLobbyAsync) - Direct return Result
5. ✅ RemovePlayer (LeaveLobbyAsync) - Direct return Result (2 branches)
6. ✅ UpdatePlayer - Direct return Result
7. ✅ ShutdownLobby - Direct return Result<IEnumerable<PlayerDTO>>
8. ✅ StartGame - Direct return Result<string>

**Pattern:** All endpoints now directly propagate Result<T> from service without wrapping in Result.Success()

### ShopService ❌ NOT STARTED

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/ShopService.cs`
- ⏳ Estimated: 4 throw sites to migrate
- ⏳ Need to add: Purchase logging (critical operation)

### PlayerPowerService ❌ NOT STARTED

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/PlayerPowerService.cs`
- ⏳ Estimated: 5 throw sites to migrate

## Phase 4: Game Service ❌ NOT STARTED

### GameService

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/GameService.cs`
- ⏳ Estimated: 19 throw sites to migrate
- ⏳ Need to add logging for:
  - Round start/end
  - Player eliminations
  - Game end

**Current Build Errors (Expected):**
- 63 errors related to accessing properties on Result<T> instead of unwrapping .Value
- These will be resolved when GameService and ShopService are migrated

### GameplayEndpoints

**File:** `Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/EndpointGroups/GameplayEndpoints.cs`
- ⏳ Update to handle Result<T> from GameService

## Phase 5: Finalization ❌ NOT STARTED

### Remaining Endpoint Files
- ⏳ Review all endpoint files for Result<T> handling
- ⏳ Ensure consistent error response format

### Testing & Verification
- ⏳ Verify HTTP status code mapping:
  - 404 for NotFound
  - 409 for Conflict
  - 400 for Invalid/ValidationError
  - 500 for Error
- ⏳ Run smoke tests
- ⏳ Test Sentry integration for error logging

## Key Patterns & Gotchas

### Pattern: Unwrapping Repository Results
```csharp
// CORRECT
var result = await repository.GetAsync(id, ct);
if (!result.IsSuccess || result.Value == null)
    return Result.NotFound("Item not found.");

var item = result.Value;
// Use item...

// INCORRECT - Don't access .Value without checking IsSuccess first
var item = await repository.GetAsync(id, ct).Value; // ❌
```

### Pattern: Propagating Results in Endpoints
```csharp
// CORRECT - Direct propagation
group.MapGet("/endpoint", async (IService service) =>
{
    return await service.GetAsync();
});

// INCORRECT - Double wrapping
group.MapGet("/endpoint", async (IService service) =>
{
    var result = await service.GetAsync();
    return Result.Success(result); // ❌ Already a Result<T>
});
```

### Pattern: Handling Multiple Repository Calls
```csharp
// Check each repository operation
var getResult = await repo.GetAsync(id, ct);
if (!getResult.IsSuccess || getResult.Value == null)
    return Result.NotFound();

var updateResult = await repo.UpdateAsync(id, value, ct);
if (!updateResult.IsSuccess)
    return Result.Error("Failed to update.");

return Result.Success();
```

### Gotcha: Enum Types and Result<T>
```csharp
// GameStates is an enum, not a nullable
var stateResult = await gameStateRepository.GetAsync(gameId, ct);

// CORRECT
if (!stateResult.IsSuccess)
    return Result.Error();
var gameState = stateResult.Value;

// INCORRECT
if (!stateResult.Value.HasValue) // ❌ Enums don't have .HasValue
    return Result.Error();
```

### Gotcha: ValidationError Constructor
```csharp
// CORRECT
return Result.Invalid(new ValidationError("Message"));

// INCORRECT
return Result.Invalid("Message"); // ❌ Takes ValidationError, not string
```

## Logging Examples

### Critical Operation Logging (Information Level)
```csharp
logger.LogInformation(
    "Lobby created: LobbyId={LobbyId}, HostPlayerId={HostPlayerId}, HostPlayerName={HostPlayerName}",
    lobbyId, hostPlayer.Id, hostPlayer.Name);

logger.LogInformation(
    "Game started: LobbyId={LobbyId}, GameId={GameId}, GameMode={GameMode}, PlayerCount={PlayerCount}",
    lobbyId, activeGameId, gameMode, lobbyDTO.Players.Count);
```

### Failure/Conflict Logging (Warning Level)
```csharp
logger.LogWarning(
    "Join lobby failed: LobbyId={LobbyId}, PlayerId={PlayerId}, Reason=LobbyNotFound",
    lobbyId, player.Id);

logger.LogWarning(
    "Start game failed: LobbyId={LobbyId}, Reason=LobbyHasActiveGame, GameId={GameId}",
    lobbyId, lobbyDTO.GameId);
```

## Progress Summary

### Completed
- ✅ Phase 1: Foundation layer (repositories, utilities)
- ✅ Phase 2: GameStateMachineService
- ✅ Phase 3: LobbyService (9 methods, 8 throw sites)
- ✅ Phase 3: LobbyEndpoints (8 endpoints)

### In Progress
- 🔄 Phase 3: ShopService (next)
- 🔄 Phase 3: PlayerPowerService (after ShopService)

### Remaining
- ⏳ Phase 4: GameService (19 throw sites) + GameplayEndpoints
- ⏳ Phase 5: Remaining endpoints + testing

### Statistics
- **Total throw sites migrated:** ~15
- **Total methods migrated:** ~15
- **Logging added:** 8 critical operations, 5 warning scenarios
- **Estimated remaining throw sites:** ~28 (4 ShopService + 5 PlayerPowerService + 19 GameService)

## Next Session Action Items

1. **Migrate ShopService:**
   - Update interface with Result<T> return types
   - Migrate 4 throw sites
   - Add purchase logging (critical operation)

2. **Migrate PlayerPowerService:**
   - Update interface with Result<T> return types
   - Migrate 5 throw sites

3. **Test LobbyService:**
   - Verify endpoint integration works
   - Test HTTP status codes map correctly (404, 409, 400, 500)

## Build Status
- ❌ Current build has 63 errors (expected)
- Errors are in GameService and ShopService accessing Result<T> properties
- Will be resolved when those services are migrated
- LobbyService and LobbyEndpoints compile successfully

## References
- **Plan File:** `C:\Users\hfaul\.claude\plans\whimsical-discovering-blanket.md`
- **Ardalis.Result Docs:** https://github.com/ardalis/Result
- **Sentry Integration:** Confirmed working for both client and server
