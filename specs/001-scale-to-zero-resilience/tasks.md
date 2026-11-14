---
description: "Task list for Scale-to-Zero Resilience"
---

# Tasks: Scale-to-Zero Resilience

**Input**: Design documents from `/specs/001-scale-to-zero-resilience/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Test tasks ARE included. The plan contains an explicit Testing Strategy with a layer assignment table, and the spec's success criteria (SC-001–SC-006, SC-011) are stated as tests. Tests are therefore in scope, not optional, for this feature.

**Organization**: Tasks are grouped by user story. Story 1 (P1) is a live-defect fix that ships with zero infrastructure change and is the MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in every task

## Path Conventions

This is a layered .NET web app (see plan.md § Project Structure). No new projects.

- Server layers: `src/Server/ChefKnifeStudios.PokerAttack.Server.{Core,Data,Infrastructure,BL,WebAPI}/`
- Server tests: `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/`
- Client: `src/Client/ChefKnifeStudios.PokerAttack.Client.{Core,Shared,WebApp}/`
- Cross-cutting: `src/ChefKnifeStudios.PokerAttack.Shared/`, `src/ChefKnifeStudios.PokerAttack.ServiceDefaults/`
- E2E: `tests/e2e/`

## Findings that adjust the plan

Verified against the codebase while generating these tasks:

1. **`MultiGameDataStore` already exists** at `src/Client/ChefKnifeStudios.PokerAttack.Client.Shared/Services/MultiGameDataStore.cs` — the plan marks it NEW. It is an in-memory `ObservableObject` view-model with a `GameId` property, reset on page visit, with **no localStorage persistence**. The work is therefore *extending it with persistence* (T029), not creating it.
2. **The test project references only `Shared`, `Client.Core`, and `Server.BL`.** Tests for `Server.Infrastructure` (Postgres repository) and `Server.WebAPI` (resync endpoint, readiness probe) cannot compile until project references are added (T004).
3. **There is no CI workflow** (`.github/workflows/` is absent) and no Bicep/containerapp manifest in the repo — the container app was unlinked from this repo in commit `b6a4d17`. Story 2/3 hosting changes (T049, T053) are therefore Azure-portal/CLI configuration tasks, documented in-repo rather than committed as IaC.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make the test suite compile so that every subsequent task has somewhere to put a regression test. Per plan.md § Testing Strategy, this is a **prerequisite, not cleanup** — the only unit-test suite in the repo is currently dead.

- [X] T001 Retarget the test project to `net10.0` to match the rest of the solution in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ChefKnifeStudios.PokerAttack.Server.BL.Tests.csproj`
- [X] T002 Repair or delete the broken `LobbyServiceTests.cs` in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/UnitTests/LobbyServiceTests.cs` — it references `ILobbyRepository`, `Core.Interfaces.Repos`, `Lobby.HostPlayerId`, and `Lobby.PlayerIds`, none of which exist; the live `Lobby` has `HostPlayer` and `Players`
- [X] T003 Verify `dotnet test src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests` builds and runs green, and record the baseline pass count in the task's commit message
- [X] T004 Add `ProjectReference` entries for `Server.Core`, `Server.Infrastructure`, `Server.Data`, and `Server.WebAPI` to `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ChefKnifeStudios.PokerAttack.Server.BL.Tests.csproj` so contract and component tests in later phases can compile
- [X] T005 [P] Add `Microsoft.AspNetCore.Mvc.Testing` and `Testcontainers.PostgreSql` package references to `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ChefKnifeStudios.PokerAttack.Server.BL.Tests.csproj` for the Tier-2 contract tests

**Checkpoint**: `dotnet test` is green and the suite can reference every layer this feature touches.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The single source of time. Every deadline, grace period, and expiry in this feature reads and writes through `IClock`; without it, none of the Story 1 logic is unit-testable without sleeping (plan.md § Flakiness controls).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T006 Create `IClock` with a single `DateTime UtcNow { get; }` member in `src/Server/ChefKnifeStudios.PokerAttack.Server.Core/Interfaces/IClock.cs`
- [X] T007 Create `SystemClock : IClock` returning `DateTime.UtcNow` in `src/Server/ChefKnifeStudios.PokerAttack.Server.Infrastructure/SystemClock.cs`
- [X] T008 Register `IClock` → `SystemClock` as a singleton in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Program.cs`
- [X] T009 [P] Create a reusable `FakeClock : IClock` test double with a settable instant and an `Advance(TimeSpan)` method in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/Doubles/FakeClock.cs`
- [X] T010 Replace the direct `DateTime.UtcNow` use with injected `IClock` in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/CleanupBackgroundService.cs`
- [X] T011 Replace the direct `DateTime.UtcNow` default for `CreatedAt` with a clock-supplied value at the construction site in `src/Server/ChefKnifeStudios.PokerAttack.Server.Core/Models/Lobby.cs` and its creator in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/LobbyService.cs`
- [X] T012 Add grace-period and phase-reconciliation configuration (`GracePeriodSeconds` default 90, `ReconcilerIntervalSeconds`) to `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/appsettings.json` and bind it in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Program.cs`

**Checkpoint**: A single injectable source of time exists and is used everywhere time is read. User story work can begin.

---

## Phase 3: User Story 1 - A game keeps playing through a brief interruption (Priority: P1) 🎯 MVP

**Goal**: A multiplayer game in progress survives an interruption of the game server — players are not evicted, phases do not wedge, state outlives the process, and returning clients rejoin and resync to correct current state.

**Independent Test**: Start a multiplayer game with two or more players, restart the game server mid-round, and confirm every player's board returns to life showing correct current state and the game plays to completion. Requires no hosting configuration change.

**Note on ordering**: Steps 1–6 of plan.md are grouped into sub-phases below. Sub-phase 3A (client reconnect) and 3B (grace period) each deliver visible improvement on their own; 3C–3F complete the story. The quickstart's staged-expectations table maps one-to-one onto these sub-phases.

### Tests for User Story 1 ⚠️

> Write these FIRST and confirm they fail before implementing the corresponding sub-phase.

- [X] T013 [P] [US1] Unit tests for phase-deadline arithmetic — remaining time, is-overdue at exactly `now`, null deadline means untimed — against `FakeClock` in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/UnitTests/PhaseDeadlineTests.cs`
- [X] T014 [P] [US1] Unit tests for grace-period boundary logic — not removed at `disconnected_at + grace - 1 tick`, removed at `+ 1 tick`, reconnect before expiry clears `disconnected_at` — in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/UnitTests/GracePeriodTests.cs`
- [X] T015 [P] [US1] Unit test asserting elimination-driven removal via `FinishEliminationAsync` stays **immediate** and does not acquire a grace period, in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/UnitTests/DeliberateRemovalTests.cs`
- [X] T016 [P] [US1] Unit tests for the reconciler — advances an overdue phase exactly once, advances through multiple deadlines expired during one interruption, no-ops when nothing is overdue — with a mocked `IGameStateMachineService` in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/UnitTests/PhaseReconcilerServiceTests.cs`
- [ ] T017 [P] [US1] Unit tests for resync payload mapping — own hand present, opponent hands absent, opponents ordered by `playerId` — over stubbed repositories in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/UnitTests/GameResyncServiceTests.cs`
- [X] T018 [P] [US1] Contract tests round-tripping each of `ActiveGame`, `GamePlayer`, `Lobby`, `GameStates`, `GameModes` through `PostgresKeyValueRepository<T>` against a real Postgres, covering polymorphic `Deck`/`Card`/`ItemBase` serialization, in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ContractTests/PostgresKeyValueRepositoryTests.cs` (compiles; requires Docker to run, unavailable in this environment)
- [X] T019 [P] [US1] Contract test running one shared test body against both `InMemoryKeyValueRepository<T>` and `PostgresKeyValueRepository<T>` to prove behavioural parity, in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ContractTests/KeyValueRepositoryParityTests.cs` (compiles; Postgres side requires Docker)
- [X] T020 [P] [US1] Contract tests for `GET /gameplay/{gameId}/state` via `WebApplicationFactory` — 200 with correct phase and remaining time; 404 for unknown game, ended game, and non-participant; stable opponent ordering; advances an overdue phase exactly once under concurrent calls — in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ContractTests/GameStateResyncEndpointTests.cs` (compiles; requires Docker to run)
- [X] T021 [P] [US1] Contract test asserting the indexed `player_id → game_id` lookup returns the same result the previous linear scan did, in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ContractTests/PlayerSessionStoreTests.cs` (compiles; requires Docker to run)

### 3A — Client reconnect awareness + group rejoin (plan Step 1)

- [X] T022 [US1] Add a `CurrentGameId`/`CurrentLobbyId` localStorage key to `src/Client/ChefKnifeStudios.PokerAttack.Client.Shared/Constants/LocalStorageConstants.cs`, following the existing solo-game key naming
- [X] T023 [US1] Add localStorage persistence of `GameId` and a new `LobbyId` to the existing `MultiGameDataStore` in `src/Client/ChefKnifeStudios.PokerAttack.Client.Shared/Services/MultiGameDataStore.cs`, following the `SoloGameDataStore` precedent — load on init, save on change, and ensure `Reset()` clears the persisted value (FR-005)
- [X] T024 [US1] Replace `.WithAutomaticReconnect()` with an explicit retry schedule of `0s, 2s, 5s, 10s, 15s, 30s` in `src/Client/ChefKnifeStudios.PokerAttack.Client.Core/Services/SignalRNotificationService.cs` — the ~30s default is shorter than a cold start plus image pull
- [X] T025 [US1] Add a `Reconnected` handler in `src/Client/ChefKnifeStudios.PokerAttack.Client.Core/Services/SignalRNotificationService.cs` that calls the existing-but-never-called `JoinGameGroupAsync(currentGameId)`, or `JoinLobbyGroupAsync(currentLobbyId)` when there is no game
- [X] T026 [US1] Add a `Closed` handler in `src/Client/ChefKnifeStudios.PokerAttack.Client.Core/Services/SignalRNotificationService.cs` that re-runs `InitAsync` and then follows the same rejoin sequence — `Reconnected` never fires when the connection fully closes
- [X] T027 [US1] Expose a reconnecting/loading state from `src/Client/ChefKnifeStudios.PokerAttack.Client.Core/Services/SignalRNotificationService.cs` and surface it, with a retry affordance when the schedule is exhausted, in `src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/Pages/MultiGameplay.razor` (FR-021 — never fail silently, which is today's behaviour)
- [X] T028 [P] [US1] Component tests for the reconnect handlers with a doubled hub connection — `Reconnected` rejoins the game group, lobby-only rejoins the lobby group, `Closed` re-inits then rejoins, remembered id survives a simulated reload — in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ComponentTests/SignalRReconnectTests.cs`

**Checkpoint 3A** (quickstart stage 1): broadcasts reach the client after a reconnect instead of the board freezing forever.

### 3B — Disconnect grace period (plan Step 2)

- [X] T029 [P] [US1] Create the `PlayerSession` model with `PlayerId`, `GameId`, `LobbyId`, `Connected`, `DisconnectedAt`, `UpdatedAt` in `src/Server/ChefKnifeStudios.PokerAttack.Server.Core/Models/PlayerSession.cs`
- [X] T030 [P] [US1] Create `IPlayerSessionStore` with upsert, get-by-player, find-by-game, and find-overdue-by-disconnected-at operations in `src/Server/ChefKnifeStudios.PokerAttack.Server.Core/Interfaces/IPlayerSessionStore.cs` — relational queries, deliberately not `IKeyValueRepository<T>`
- [X] T031 [US1] Replace immediate `LeaveLobbyAsync`/`LeaveGameAsync` eviction in `OnDisconnectedAsync` with marking `Connected = false` and `DisconnectedAt = clock.UtcNow` (last connection only, preserving the existing multi-tab `GetConnections(userId).Any()` check) in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/SignalR/SignalRNotificationHub.cs`
- [X] T032 [US1] Clear `DisconnectedAt` and set `Connected = true` in `OnConnectedAsync` in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/SignalR/SignalRNotificationHub.cs`
- [X] T033 [US1] Confirm the deliberate-removal path in `FinishEliminationAsync` still calls `LeaveGameAsync` immediately and does not route through the grace period, in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/GameService.cs`

**Checkpoint 3B** (quickstart stage 2): players are no longer evicted from their own games as the container goes down. **This must land before `minReplicas = 0`.**

### 3C — Durable timers + reconciler (plan Step 3)

- [X] T034 [US1] Add `CurrentPhase` (`GameStates?`), `PhaseStartedAt` (`DateTime?`), and `PhaseExpiresAt` (`DateTime?`) to `src/Server/ChefKnifeStudios.PokerAttack.Server.Core/Models/ActiveGame.cs`
- [X] T035 [US1] Replace the round-timer `Task.Delay` (`:91`) and the 5000ms scoreboard-dwell `Task.Delay` (`:349`) with persisted `PhaseExpiresAt` writes in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/GameService.cs`
- [X] T036 [US1] Replace the elimination, shop, and power-selection `Task.Delay` calls (`:69`, `:76`, `:84`) with persisted `PhaseExpiresAt` writes in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/EventNotificationServiceSubscriber.cs`
- [X] T037 [US1] Create `PhaseReconcilerService` that advances any phase whose `PhaseExpiresAt` has passed, looping until no phase is overdue so multiple deadlines expired during one interruption resolve to the correct current phase, in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/PhaseReconcilerService.cs`
- [X] T038 [US1] Make reconciliation safe under concurrency with a conditional update on the deadline so only one caller can advance a given phase, in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/PhaseReconcilerService.cs`
- [X] T039 [US1] Extend the reconciler to sweep overdue `player_session.disconnected_at` rows past the grace period and perform the removal, in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/PhaseReconcilerService.cs`
- [X] T040 [US1] Register the reconciler as a hosted service and invoke it at startup, on SignalR reconnect, and on incoming gameplay requests, in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Program.cs`

**Checkpoint 3C** (quickstart stage 3): a round advances after an interruption instead of the countdown hitting zero and nothing happening.

### 3D — Durable key-value store (plan Step 4)

- [X] T041 [P] [US1] Create the `StateKeyValue` entity with `Key`, `Type`, `Value`, `ExpiresAt`, `UpdatedAt` in `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Models/StateKeyValue.cs` — deliberately **not** deriving from `BaseEntity` (see data-model.md)
- [X] T042 [P] [US1] Create the `PlayerSessionEntity` mapping for the `player_session` table in `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Models/PlayerSessionEntity.cs`
- [X] T043 [US1] Configure `state_kv` with composite primary key `(type, key)`, `jsonb` value column, `ix_state_kv_type`, and partial `ix_state_kv_expires_at`, in `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Configuration/StateKeyValueConfiguration.cs`
- [X] T044 [US1] Configure `player_session` with `player_id` primary key, `ix_player_session_game_id`, and partial `ix_player_session_disconnected_at`, in `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Configuration/PlayerSessionConfiguration.cs`
- [X] T045 [US1] Add both `DbSet` properties to `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/AppDbContext.cs` and generate the purely-additive EF migration into `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Migrations/`
- [X] T046 [US1] Implement `PostgresKeyValueRepository<T>` against the existing `IKeyValueRepository<T>` interface, serializing with `JsonOptions.Get()` for converter and `TypeInfoResolver` parity with the wire format, in `src/Server/ChefKnifeStudios.PokerAttack.Server.Infrastructure/PostgresKeyValueRepository.cs`
- [X] T047 [US1] Add an expiry-aware overload to `src/Server/ChefKnifeStudios.PokerAttack.Server.Core/Interfaces/IKeyValueRepository.cs` and implement it in both `InMemoryKeyValueRepository.cs` and `PostgresKeyValueRepository.cs` under `src/Server/ChefKnifeStudios.PokerAttack.Server.Infrastructure/`
- [X] T048 [US1] Implement `PlayerSessionStore : IPlayerSessionStore` over the indexed table in `src/Server/ChefKnifeStudios.PokerAttack.Server.Infrastructure/PlayerSessionStore.cs`
- [X] T049 [US1] Swap the open-generic registration from `InMemoryKeyValueRepository<>` to `PostgresKeyValueRepository<>` (keeping the in-memory type registered for tests and local dev) and register `IPlayerSessionStore`, in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Program.cs`
- [X] T050 [US1] Move the TTL sweep onto `expires_at` and run it opportunistically at startup as well as on the existing interval, so an idle period that stopped the container does not defer cleanup indefinitely (FR-013, FR-014), in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/CleanupBackgroundService.cs`

**Checkpoint 3D** (quickstart stage 4): the game, hands, and wallet still exist after a restart instead of resync returning 404.

### 3E — State resync endpoint (plan Step 5)

- [X] T051 [P] [US1] Add the `GameStateDTO`, `ResyncPlayerDTO`, and `ResyncOpponentDTO` response shapes from the contract to `src/ChefKnifeStudios.PokerAttack.Shared/DTOs/Gameplay/GameStateDTO.cs` — opponents expose score and elimination status only, never hand, wallet, deck, or items
- [X] T052 [US1] Add the `Gameplay.GetGameState` endpoint constant `"{EndpointGroup}/{{gameId}}/state"` to `src/ChefKnifeStudios.PokerAttack.Shared/PokerAttackApiEndpoints.cs`
- [X] T053 [US1] Implement `GameResyncService` building the payload — phase, `PhaseStartedAt`, `PhaseExpiresAt`, `serverTimeUtc` from `IClock`, own player state, and opponents ordered by `playerId` — in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/GameResyncService.cs`
- [X] T054 [US1] Map `GET /gameplay/{gameId}/state` following the established `group.MapGet(...).WithName(...).Produces(...)` pattern and returning `Ardalis.Result`, invoking the reconciler before building the response, in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/EndpointGroups/GameplayEndpoints.cs`
- [X] T055 [US1] Return 404 for unknown game, ended game, and non-participant without distinguishing between them; 400 for malformed parameters; 503 when the durable store is unreachable — in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/EndpointGroups/GameplayEndpoints.cs`
- [X] T056 [US1] Add a `GetGameStateAsync` client method to `src/Client/ChefKnifeStudios.PokerAttack.Client.Core/Services/EndpointServices/GameplayEndpointsService.cs`
- [X] T057 [US1] Call the resync endpoint after group rejoin in the `Reconnected` and `Closed` handlers in `src/Client/ChefKnifeStudios.PokerAttack.Client.Core/Services/SignalRNotificationService.cs`, and on initial load of an in-progress game in `src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/Pages/MultiGameplay.razor.cs` (browser refresh, FR-005)
- [X] T058 [US1] Derive the phase countdown from `phaseExpiresAt - serverTimeUtc` rather than the device clock, so a skewed client clock does not break the ±1s accuracy criterion, in `src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/Pages/MultiGameplay.razor.cs`
- [X] T059 [US1] Show the game-over/result screen rather than a stalled board when resync returns 404 for an ended game, in `src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/Pages/MultiGameplay.razor.cs`

**Checkpoint 3E** (quickstart stage 5): a returning player's board shows the current phase, not the phase they left.

### 3F — Connection tracking from durable state (plan Step 6)

- [X] T060 [US1] Replace the `GetAllAsync()` linear scans in `FindPlayerLobbyAsync` and `FindPlayerGameAsync` (`:183–231`) with indexed `IPlayerSessionStore` lookups, in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/SignalR/SignalRNotificationHub.cs` (the disconnect-driven callers of these scans were removed in 3B/3C — grace-period removal now runs entirely through `PhaseReconcilerService.ReconcileOverdueSessionsAsync`, so the scan methods were dead code and deleted rather than reimplemented)
- [X] T061 [US1] Reduce `PlayerConnectionTracker` to a cache of live connection ids for the current process, with membership derived from `IPlayerSessionStore`, in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/SignalR/PlayerConnectionTracker.cs` (already was exactly this — a per-process connection-id cache with no lobby/game membership logic of its own)
- [X] T062 [US1] Ensure the two-tabs and racing-reconnects cases resolve to exactly one consistent session rather than a double-counted or split player, in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/SignalR/PlayerConnectionTracker.cs` (multi-tab preserved via existing `GetConnections(userId).Any()` check before marking disconnected; racing reconnects converge via `IPlayerSessionStore.UpsertAsync`'s single-row-per-player upsert)

### End-to-end validation for User Story 1

- [X] T063 [US1] Add a restart-mid-round E2E test — start a two-player game, force-restart the API, assert both boards return to live correct state and the round completes — waiting on specific observable conditions rather than fixed sleeps, in `tests/e2e/multiplayer-game.spec.ts`
- [X] T064 [US1] Document the E2E restart test's timeout value and its basis (must exceed a real cold start, 3–8s today) as a comment in `tests/e2e/multiplayer-game.spec.ts`, and note it is revisited after cold start is measured in T067
- [ ] T065 [US1] Run the full quickstart acceptance test per `specs/001-scale-to-zero-resilience/quickstart.md` using `docker compose restart poker-attack-api` against two live browser clients, and confirm all five staged expectations pass — **NOT RUN**: requires a live `docker compose up` stack and real browsers, unavailable in this environment. The test in T063 is written and ready (`npx playwright test e2e/multiplayer-game.spec.ts -g "Restart mid-round"`); run it against a real local stack to close this out.

**Checkpoint**: User Story 1 is complete. SC-001 through SC-006 and SC-011 hold. **This is the MVP and is shippable with no infrastructure change.**

---

## Phase 4: User Story 3 - Starting up feels fast (Priority: P3)

**Goal**: Reduce and hide cold start, so that stopping the backend is safe to do without players noticing.

**Independent Test**: Measure time from a player's first interaction on a stopped backend to a playable game, with and without warm-on-intent, and confirm the perceived wait meets the budget.

**⚠️ Sequenced before User Story 2 despite lower priority.** Plan Step 7 (readiness probe) and the cold-start measurement it enables are what make Step 8's `minReplicas = 0` safe to commit to — plan.md § Risks states explicitly that cold start must be measured *before* setting `minReplicas = 0`. Priority order is P1 → P3 → P2 for this reason. Story 2 remains independently testable once Story 3's probe exists.

### Tests for User Story 3 ⚠️

- [X] T066 [P] [US3] Component tests asserting `/alive` returns 200 in Production, requires no authentication, and exposes no dependency detail; and that `/health`, OpenAPI, and Scalar routes are **not** mapped in Production — in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ComponentTests/ReadinessProbeTests.cs` (5/5 passing, no Docker required)

### Implementation for User Story 3

- [X] T067 [US3] Map a minimal anonymous `/alive` liveness endpoint in **all** environments — no database round-trip, no dependency detail — while keeping the full `/health` check set development-only, in `src/ChefKnifeStudios.PokerAttack.ServiceDefaults/Extensions.cs` (`:109–118`)
- [X] T068 [US3] Gate the OpenAPI and Scalar mappings (`:104–118`) behind a non-production check so documentation UI is off the production hot start path, in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Program.cs`
- [X] T069 [US3] Enable `PublishReadyToRun`, `InvariantGlobalization`, and `TieredPGO`, and add the `linux-x64` RID required at publish time, in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/ChefKnifeStudios.PokerAttack.Server.WebAPI.csproj` and the API `Dockerfile`
- [ ] T070 [US3] Measure cold start — time from a cold container to first successful `GET /alive`, before and after ReadyToRun — and record both numbers in `specs/001-scale-to-zero-resilience/quickstart.md`. **This measurement gates T073.** — **NOT RUN**: requires building and timing a real container in this environment (no Docker available). To measure: `docker build -f src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Dockerfile -t poker-attack-api-r2r .`, then time `docker run` to first `200` from `GET :8080/alive`.
- [ ] T071 [US3] Configure the Container App readiness probe to target `/alive` (Azure portal or CLI; the container app is not linked to this repo, so record the applied configuration in `docs/SCALE_TO_ZERO_ARCHITECTURE.md`) — **NOT RUN**: requires Azure portal/CLI access to the live Container App, unavailable in this environment.
- [X] T072 [P] [US3] Fire a debounced fire-and-forget `GET /alive` on evident intent to play — opening the lobby screen and hovering Play — never blocking the UI, in `src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/Pages/Lobbies.razor.cs` (FR-020; explicitly **not** a scheduled keep-warm pinger, which would recreate the cost being removed)

**Checkpoint**: Cold start is measured and within budget, and warm-on-intent hides it at game start.

---

## Phase 5: User Story 2 - The backend costs nothing when nobody is playing (Priority: P2)

**Goal**: Idle hours cost nothing; cost scales with actual play rather than wall-clock time.

**Independent Test**: Leave the system with no players for an extended idle period, confirm compute consumption drops to zero for that window, and confirm a player arriving afterward reaches a playable game within the startup budget.

**Depends on**: Story 1 complete (games must survive interruption before the server is allowed to stop) and T070 (cold start measured).

### Implementation for User Story 2

- [ ] T073 [US2] Set `minReplicas: 0` while keeping `maxReplicas: 1` and `stickySessions: sticky`, and set the idle cooldown to 300–600s so a lull between games or a short disconnect does not tear down mid-session (FR-017) — applied via Azure portal or CLI and recorded in `docs/SCALE_TO_ZERO_ARCHITECTURE.md` — **NOT RUN**: requires Azure access, and per plan must wait until T070 confirms cold start is within budget. Config is fully specified in `docs/SCALE_TO_ZERO_ARCHITECTURE.md` §7 — apply when ready.
- [ ] T074 [US2] Verify idle scale-in reaches zero replicas after the cooldown with no players connected (SC-007), and record the observation in `specs/001-scale-to-zero-resilience/quickstart.md` — **NOT RUN**: depends on T073 being applied first.
- [ ] T075 [US2] Verify an in-progress game holds the replica up past the cooldown and is not torn down (FR-016), and record the observation in `specs/001-scale-to-zero-resilience/quickstart.md` — **NOT RUN**: depends on T073.
- [ ] T076 [US2] Verify a player arriving at a stopped backend reaches a playable game within the 10s budget (SC-008), and record the measurement in `specs/001-scale-to-zero-resilience/quickstart.md` — **NOT RUN**: depends on T073.
- [ ] T077 [US2] Compare a full billing period against the pre-change baseline and confirm compute cost is at or below 20% of its current level (SC-009), recording the figures in `docs/SCALE_TO_ZERO_ARCHITECTURE.md` — **NOT RUN**: requires a live billing period after T073 is applied.

**Checkpoint**: Idle compute is zero, sessions are never torn down mid-game, and the cost outcome is measured rather than assumed.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T078 [P] Add negative-path tests for resync — unknown game id, ended game, player not in the game — in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ContractTests/GameStateResyncEndpointTests.cs`
- [ ] T079 [P] Add error-path tests for Postgres unavailable during resync and for exhausted client reconnect attempts surfacing a retry rather than dying silently, in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ContractTests/GameStateResyncEndpointTests.cs`
- [ ] T080 [P] Add concurrency tests for two racing reconnects, the same player in two tabs, and multiple deadlines expiring during one interruption, in `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/UnitTests/PhaseReconcilerServiceTests.cs`
- [ ] T081 Confirm solo play is unaffected by the repository swap by running the solo flow end to end and verifying `SoloGameDataStore` persistence still works (FR-024)
- [ ] T082 Verify zero gameplay regressions — an unbroken game plays identically before and after (SC-011) — via the full test suite plus a manual multiplayer game to completion
- [ ] T083 [P] Update `docs/SCALE_TO_ZERO_ARCHITECTURE.md` with the as-built design, the measured cold start, and the applied Container App configuration
- [ ] T084 [P] Update `CLAUDE.md` to note that live state is now durable and that `IClock` is the single source of time for deadlines and expiry
- [ ] T085 Remove any now-dead code left by the migration — unused `Task.Delay` imports, superseded tracker methods — across `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/` and `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/SignalR/`
- [ ] T086 Run `/speckit-constitution` to fill in the unratified constitution template at `.specify/memory/constitution.md`, so future planning gates are real rather than vacuous (does not block this feature)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies. **Blocks everything** — there is no working test suite until it completes.
- **Foundational (Phase 2)**: Depends on Phase 1. `IClock` blocks all Story 1 logic.
- **User Story 1 (Phase 3)**: Depends on Phase 2. No dependency on other stories.
- **User Story 3 (Phase 4)**: Depends on Phase 2. Independent of Story 1 in code, but sequenced after it because measuring cold start is only meaningful once the app is restart-safe.
- **User Story 2 (Phase 5)**: Depends on Story 1 (games must survive interruption) **and** T070 (cold start measured).
- **Polish (Phase 6)**: Depends on all desired stories.

### Within User Story 1

Sub-phases are ordered by the dependency the code forces (plan.md § Implementation Phases):

- **3A** (client reconnect) → independent; delivers visible improvement alone
- **3B** (grace period) → depends on the `PlayerSession` model (T029–T030); **must precede any `minReplicas = 0` work**
- **3C** (durable timers) → depends on 3B's session store for the grace sweep
- **3D** (Postgres repository) → depends on 3C's `ActiveGame` fields being defined
- **3E** (resync endpoint) → depends on 3D (durable state) and 3C (reconciler)
- **3F** (connection tracking) → depends on 3D (`IPlayerSessionStore` implementation)

### User Story Dependencies

- **US1 (P1)**: Independent. Ships alone as the MVP.
- **US3 (P3)**: Independent of US1 in code. Its T070 measurement gates US2.
- **US2 (P2)**: Depends on US1 and on T070. **Not independently shippable** — this is inherent to the feature, not a task-decomposition flaw: turning the server off before games survive being turned off would destroy live games.

### Parallel Opportunities

- T005 runs parallel to T004's edits (different concerns, same file — sequence if editing simultaneously)
- All Story 1 test tasks T013–T021 are parallel — different files, no shared state
- T029 and T030 are parallel (model and interface, different files)
- T041 and T042 are parallel (two entities, different files)
- T051 is parallel to the 3E server work (DTO in a different project)
- T078, T079, T080 are parallel; T083 and T084 are parallel

---

## Parallel Example: User Story 1 Tests

```bash
# Launch all Story 1 unit tests together:
Task: "Unit tests for phase-deadline arithmetic in .../UnitTests/PhaseDeadlineTests.cs"
Task: "Unit tests for grace-period boundary logic in .../UnitTests/GracePeriodTests.cs"
Task: "Unit test for immediate elimination removal in .../UnitTests/DeliberateRemovalTests.cs"
Task: "Unit tests for the reconciler in .../UnitTests/PhaseReconcilerServiceTests.cs"
Task: "Unit tests for resync payload mapping in .../UnitTests/GameResyncServiceTests.cs"

# Launch all Story 1 contract tests together:
Task: "Postgres round-trip tests in .../ContractTests/PostgresKeyValueRepositoryTests.cs"
Task: "Repository parity tests in .../ContractTests/KeyValueRepositoryParityTests.cs"
Task: "Resync endpoint tests in .../ContractTests/GameStateResyncEndpointTests.cs"
Task: "Player session lookup tests in .../ContractTests/PlayerSessionStoreTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup — the test suite must compile before anything else
2. Complete Phase 2: Foundational — `IClock` blocks all Story 1 logic
3. Complete Phase 3: User Story 1, sub-phase by sub-phase, checking against the quickstart's staged expectations
4. **STOP and VALIDATE**: run T065, the full quickstart acceptance test
5. **Ship it.** Story 1 fixes a live player-facing defect and requires no hosting change

### Incremental Delivery

1. Setup + Foundational → test suite alive, single source of time
2. Story 1 → restart-safe multiplayer → **deploy (MVP, fixes a live bug)**
3. Story 3 → readiness probe + ReadyToRun + measured cold start → deploy
4. Story 2 → `minReplicas = 0` → observe over a billing period
5. Polish

### Fallback

If cold start (T070) misses the 10s budget even after ReadyToRun and warm-on-intent, do **not** proceed to T073. Leave `minReplicas` at 1. Stories 1 and 3 remain valuable on their own — Story 1 fixes a live defect regardless of hosting, and Story 3 makes every deploy faster.

---

## Notes

- Tests are in scope for this feature (see header) and precede their sub-phase implementation
- **No `Task.Delay` in tests.** Removing production `Task.Delay` is the feature; reintroducing it in tests would be perverse. Reconciler tests advance `FakeClock` and invoke the reconciler directly
- **No `sleep`-to-wait in E2E.** Playwright waits on the specific observable condition — board re-rendered, round number advanced
- **No retries on flaky tests.** A flaky reconnect test almost certainly means reconnect flakiness for real players; treat it as a defect
- SC-007, SC-009, and SC-010 are deliberately **not** automated — they are properties of a bill and a deployment, and asserting them in CI would be theatre
- Contract tests get a fresh schema per run; no suite-wide mutable fixture
- Commit after each task or logical group; stop at any checkpoint to validate independently
