# Implementation Plan: Scale-to-Zero Resilience

**Branch**: `001-scale-to-zero-resilience` | **Date**: 2026-08-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-scale-to-zero-resilience/spec.md`

## Summary

Make an in-progress multiplayer game survive an interruption of the game server, then allow the server to stop when nobody is playing.

The technical approach follows the dependency order forced by the code: the client must first *notice* it reconnected and rejoin its SignalR group (today nothing does), disconnects must stop evicting players instantly, timed phases must move from in-process `Task.Delay` to persisted deadlines with a reconciler, and live state must move from `ConcurrentDictionary` to Postgres behind the existing `IKeyValueRepository<T>` interface. Only once all of that holds is `minReplicas = 0` safe.

The load-bearing insight is that `IKeyValueRepository<T>` is already the correct seam. Every piece of live state — `ActiveGame`, `GamePlayer`, `Lobby`, `GameStates`, `GameModes` — is already behind it, and `Program.cs:68` registers the in-memory implementation with a single open-generic line. Externalizing state is a new implementation plus a one-line DI swap, not a rewrite of game logic.

## Technical Context

**Language/Version**: C# / .NET 10 (server + client); test project currently targets net9.0
**Primary Dependencies**: ASP.NET Core Minimal APIs, SignalR (self-hosted, no backplane), Blazor WebAssembly, EF Core + Npgsql, Ardalis.Result, Ardalis.Specification, Blazored.LocalStorage, CommunityToolkit.Mvvm, Sentry, .NET Aspire (AppHost + ServiceDefaults)
**Storage**: PostgreSQL (self-hosted on Linode). Existing `AppDbContext`, `PokerAttack` schema, EF migrations pipeline. Currently holds only historical `Game`/`Round`/`RoundScore`; this feature adds live state.
**Testing**: NUnit 4 + Moq 4 (`Server.BL.Tests`), Playwright E2E (`tests/e2e/multiplayer-game.spec.ts`). See [Testing Strategy](#testing-strategy).
**Target Platform**: Linux container on Azure Container Apps (Consumption profile); client is a Blazor WASM Static Web App (Free tier)
**Project Type**: Web application — Blazor WASM client + ASP.NET Core API with SignalR real-time layer
**Performance Goals**: Cold start ≤10s to playable; ≤2s perceived with warm-on-intent; reconnect-to-live-board ≤10s; phase countdown accurate to ±1s
**Constraints**: Single replica only (`maxReplicas: 1`, sticky sessions retained — no SignalR backplane). No new paid services. Postgres round-trip latency (single-digit ms) is negligible against 10–30s game phases. No gameplay behavior changes.
**Scale/Scope**: Small player base; a handful of concurrent lobbies/games. 5 key-value types to externalize, 5 `Task.Delay` sites to make durable, 1 client SignalR service to make reconnect-aware.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is an **unfilled template** — every principle is still a `[PRINCIPLE_N_NAME]` placeholder. There are no ratified project principles to check against, so no gate can pass or fail on its merits.

**Gate result: PASS (vacuous).** Recorded honestly rather than claimed as a real check.

In the absence of a constitution, this plan holds itself to the constraints the spec actually states:

| Self-imposed gate | Status |
|---|---|
| No new paid services (FR-022) | PASS — reuses existing Postgres; Redis and Azure SignalR explicitly rejected |
| No gameplay behavior change (FR-023) | PASS — no scoring, dealing, or rules logic touched |
| Solo play unaffected (FR-024) | PASS — solo already persists via `SoloGameDataStore`; repository swap is transparent to it |
| Single-replica assumption not deepened | PASS — Step 4 removes two `GetAllAsync()` linear scans, making multi-replica *less* blocked than today |
| Each user story independently shippable | PASS — Story 1 (Steps 1–6) ships with zero infrastructure change |

**Post-Phase 1 re-check**: PASS. The design adds one table, one repository implementation, one hosted service, and one endpoint. No new project, service, or paid dependency.

**Recommendation**: run `/speckit-constitution` at some point. It does not block this feature, but every future `/speckit-plan` will hit this same vacuous gate until it is filled in.

## Project Structure

### Documentation (this feature)

```text
specs/001-scale-to-zero-resilience/
├── plan.md              # This file
├── research.md          # Phase 0 output — decisions + rejected alternatives
├── data-model.md        # Phase 1 output — state_kv, player_session, phase deadlines
├── quickstart.md        # Phase 1 output — how to run the acceptance test
├── contracts/
│   ├── game-state-resync.md    # GET /gameplay/{gameId}/state
│   ├── signalr-hub.md          # Hub method + reconnect contract changes
│   └── health-readiness.md     # Production readiness probe
├── checklists/
│   └── requirements.md  # Created by /speckit-specify
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

Existing structure, with this feature's touchpoints marked:

```text
src/
├── ChefKnifeStudios.PokerAttack.AppHost/          # Aspire orchestration
├── ChefKnifeStudios.PokerAttack.ServiceDefaults/
│   └── Extensions.cs                              # CHANGE: health endpoints gated on IsDevelopment() (:113)
├── ChefKnifeStudios.PokerAttack.Shared/
│   └── PokerAttackApiEndpoints.cs                 # CHANGE: add Gameplay.GetGameState
│
├── Client/
│   ├── ChefKnifeStudios.PokerAttack.Client.Core/
│   │   └── Services/
│   │       └── SignalRNotificationService.cs      # CHANGE: Reconnected/Closed handlers, retry policy, rejoin
│   ├── ChefKnifeStudios.PokerAttack.Client.Shared/
│   │   ├── Constants/LocalStorageConstants.cs     # CHANGE: add multiplayer session key
│   │   └── Services/
│   │       ├── SoloGameDataStore.cs               # PRECEDENT to follow
│   │       └── MultiGameDataStore.cs              # NEW: persist current lobby/game id
│   └── ChefKnifeStudios.PokerAttack.Client.WebApp/
│
└── Server/
    ├── ChefKnifeStudios.PokerAttack.Server.Core/
    │   ├── Interfaces/
    │   │   ├── IKeyValueRepository.cs             # CHANGE: expiry-aware overload
    │   │   ├── IPlayerSessionStore.cs             # NEW
    │   │   └── IClock.cs                          # NEW: injectable time (see Testing Strategy)
    │   └── Models/
    │       ├── ActiveGame.cs                      # CHANGE: PhaseExpiresAt, CurrentPhase
    │       └── PlayerSession.cs                   # NEW
    │
    ├── ChefKnifeStudios.PokerAttack.Server.Data/
    │   ├── Models/StateKeyValue.cs                # NEW: state_kv entity
    │   ├── Configuration/StateKeyValueConfiguration.cs  # NEW
    │   └── Migrations/                            # NEW migration
    │
    ├── ChefKnifeStudios.PokerAttack.Server.Infrastructure/
    │   ├── InMemoryKeyValueRepository.cs          # KEEP (tests/local)
    │   └── PostgresKeyValueRepository.cs          # NEW
    │
    ├── ChefKnifeStudios.PokerAttack.Server.BL/
    │   ├── CleanupBackgroundService.cs            # CHANGE: sweep expires_at
    │   ├── PhaseReconcilerService.cs              # NEW: advance expired phases
    │   └── Services/
    │       ├── GameService.cs                     # CHANGE: remove Task.Delay (:91, :349)
    │       ├── EventNotificationServiceSubscriber.cs  # CHANGE: remove Task.Delay (:69,:76,:84)
    │       └── GameResyncService.cs               # NEW: rehydration payload
    │
    ├── ChefKnifeStudios.PokerAttack.Server.WebAPI/
    │   ├── Program.cs                             # CHANGE: DI swap (:68), readiness, OpenAPI gating
    │   ├── EndpointGroups/GameplayEndpoints.cs    # CHANGE: add state resync endpoint
    │   └── SignalR/
    │       ├── SignalRNotificationHub.cs          # CHANGE: grace period, drop GetAllAsync scans (:183–231)
    │       └── PlayerConnectionTracker.cs         # CHANGE: cache over durable sessions
    │
    └── ChefKnifeStudios.PokerAttack.Server.BL.Tests/UnitTests/   # REPAIR + NEW tests

tests/e2e/
└── multiplayer-game.spec.ts                       # CHANGE: add restart-mid-round test
```

**Structure Decision**: Existing layered server (`Core` → `Data`/`Infrastructure` → `BL` → `WebAPI`) plus Blazor WASM client is retained unchanged. No new projects. New code lands in the layer that already owns its concern: the durable repository in `Infrastructure` (alongside the in-memory one it replaces), the entity and migration in `Data`, the reconciler and resync in `BL`, and reconnect handling in `Client.Core`. This keeps the DI swap at `Program.cs:68` as the single switch between in-memory and durable state.

## Implementation Phases

The spec's three user stories map to nine implementation steps. Steps 1–6 deliver Story 1 (resilience — a live bug fix, shippable with zero infrastructure change). Steps 7–9 deliver Stories 2 and 3 (cost and cold start).

### Step 1 — Client reconnect awareness + group rejoin *(Story 1, P1)*

**The smallest change that fixes the most visible bug.** `SignalRNotificationService.cs` calls `.WithAutomaticReconnect()` at line 97 but subscribes to neither `Reconnected` nor `Closed`. And `JoinGameGroupAsync`/`JoinLobbyGroupAsync` exist on both client (`:195`, `:162`) and hub (`:124`, `:106`) but **have no client call sites** — group membership is established server-side only, once, at lobby-join and game-start via `LobbyService`. After a restart those never re-run, so the player is in no group and every `BroadcastToGameAsync` reaches an empty group.

- Add `Reconnected` and `Closed` handlers.
- Supply an explicit retry policy — the ~30s default is shorter than a cold start plus image pull, so the connection dies before the server is even up.
- Track current lobby/game id in a new `MultiGameDataStore`, persisted to localStorage following the `SoloGameDataStore` precedent, so a browser refresh also recovers.
- On reconnect, call the existing (currently dead) client rejoin methods.

Client-initiated rejoin, not server-initiated: `JoinGameGroupForUserAsync` early-returns when `GetConnections(userId).Count == 0` (`PokerAttackNotificationHelper.cs:148,183`), and the tracker is empty after a restart, so the server path silently no-ops.

### Step 2 — Disconnect grace period *(Story 1, P1)*

`SignalRNotificationHub.OnDisconnectedAsync` (`:43–92`) removes a player from lobby and game *immediately* on last-connection-close. Under scale-to-zero this is actively harmful: container shutdown would evict every player from their own game on the way down.

Replace immediate eviction with `disconnected_at` marking; removal happens only after the grace period elapses without a reconnect, via the Step 3 reconciler. **Must land before `minReplicas = 0`.**

### Step 3 — Durable timers + reconciler *(Story 1, P1)*

Five `Task.Delay` calls drive game progression: `GameService.cs:91` (round), `GameService.cs:349` (scoreboard dwell), and `EventNotificationServiceSubscriber.cs:69/76/84` (elimination, shop, power selection). `Task.Delay` is not durable — if the container is evicted mid-round the continuation never runs and the game wedges forever. This is a live bug today on every deploy.

Replace with persisted `PhaseExpiresAt` plus a reconciler that advances overdue phases on incoming request, on SignalR reconnect, and on startup. Clients already know phase durations and render countdowns locally, so the server need not be awake to tick.

### Step 4 — `PostgresKeyValueRepository<T>` *(Story 1, P1)*

New implementation behind the existing `IKeyValueRepository<T>`; swap the open-generic registration at `Program.cs:68`. Schema in [data-model.md](./data-model.md).

Two fixes while in there:
- Replace the `GetAllAsync()` linear scans in `FindPlayerLobbyAsync`/`FindPlayerGameAsync` (`SignalRNotificationHub.cs:183–231`) with an indexed `player_id → game_id` lookup.
- Move `CleanupBackgroundService`'s TTL sweep onto `expires_at`. Today cleanup only runs while the always-on container is alive — circular reasoning that breaks the moment the container is allowed to stop.

### Step 5 — State resync endpoint *(Story 1, P1)*

Rejoining a group only restores *future* broadcasts; the client has missed everything that happened while away. Add `GET /gameplay/{gameId}/state` returning phase, `PhaseExpiresAt`, round, hand, wallet, score, opponent summaries. Contract in [contracts/game-state-resync.md](./contracts/game-state-resync.md).

### Step 6 — Connection tracking from durable state *(Story 1, P1)*

Derive membership from durable game records; keep the in-memory map purely as a cache of live connection ids.

### Step 7 — Production readiness probe + ReadyToRun *(Story 3, P3)*

`MapDefaultEndpoints()` maps `/health` and `/alive` **only when `IsDevelopment()`** (`ServiceDefaults/Extensions.cs:113`). Production has no readiness endpoint, so Container Apps cannot tell when the app is ready and falls back to slower heuristics. Map a lightweight readiness endpoint in production, wire it as the probe, enable ReadyToRun, and gate OpenAPI/Scalar (`Program.cs:104–118`) out of the production start path.

### Step 8 — `minReplicas = 0` with generous cooldown *(Story 2, P2)*

Set `minReplicas = 0`, keep `maxReplicas = 1` and sticky sessions. Tune cooldown to 300–600s so a lull between games or a short disconnect does not tear down mid-session. Measure real cold start before and after.

### Step 9 — Warm on intent *(Story 3, P3)*

Fire the readiness endpoint when the user opens the lobby screen or hovers Play, so the container warms while they read. Especially effective for multiplayer: the host warms the app while waiting for others to join.

## Testing Strategy

Written against the `util-testing` standard (`.claude/skills/util-testing/`): classification by layer, SMURF trade-offs, execution tiers, double selection, and the flakiness rules.

### Blocker: the existing test project does not compile

`Server.BL.Tests/UnitTests/LobbyServiceTests.cs` references `ILobbyRepository`, `Core.Interfaces.Repos`, `Lobby.HostPlayerId`, and `Lobby.PlayerIds`. **None of these exist in the current codebase** — the live `Lobby` has `HostPlayer` and `Players` (`Lobby.cs:6-7`), and there is no `ILobbyRepository` anywhere in `Core` or `BL`. The project also targets `net9.0` while the rest of the solution is on .NET 10.

This is a pre-existing defect, not one this feature introduces, but it makes the *only* unit-test suite dead. **Repairing it is a prerequisite for Step 1**, not optional cleanup — there is otherwise nowhere to put a regression test. Task ordering must reflect that.

### Layer assignment

Coverage is pushed to the lowest layer that can prove the behaviour. The critical judgement here: **most of this feature's risk is in time and interruption**, which are exactly the two things that make higher-layer tests flaky. So the design goal is to make the reconciler and grace period provable at the *unit* layer via an injected clock, leaving E2E to prove only the one thing it uniquely can — that a real container restart is survivable.

| Behaviour | Layer | Tier | Why this layer |
|---|---|---|---|
| Phase deadline arithmetic; is-a-phase-overdue | Unit | 0 | Pure function of clock + stored deadline. No I/O needed. |
| Reconciler advances an overdue phase exactly once | Unit | 0 | State machine call verified via mock; no real timer. |
| Grace period: not evicted before expiry, evicted after | Unit | 0 | Clock-driven boundary logic — the highest-value unit tests in the feature. |
| Deliberate removal (elimination loser) still immediate | Unit | 0 | Guards the Step 2 regression risk noted under Risks. |
| Resync payload shape for a mid-phase rejoin | Unit | 0 | Mapping logic over stubbed repositories. |
| `PostgresKeyValueRepository<T>` round-trips each stored type | Contract | 2 | Crosses a real boundary (Npgsql + JSONB polymorphic serialization). Cannot be proven with a double — a stub would test the stub. |
| Repository contract parity: in-memory vs. Postgres behave identically | Contract | 2 | One shared test body run against both implementations. Protects the DI swap at `Program.cs:68`, which is the whole premise of Step 4. |
| Indexed `player_id → game_id` lookup replaces linear scan | Contract | 2 | Correctness against real query semantics. |
| Resync endpoint returns 200/404 with correct body | Contract | 2 | API surface tested via `WebApplicationFactory`. |
| Production readiness probe responds outside Development | Component | 1 | Cheap host-configuration test; catches the `IsDevelopment()` regression directly. |
| Client rejoins group on `Reconnected` | Component | 1 | `MultiGameDataStore` + handler logic with a doubled hub connection. |
| **Game survives a real container restart mid-round** | E2E | 2 | SC-001. The one behaviour no lower layer can prove — needs real infra, a real WebSocket, and a real process kill. |
| Cold start to playable ≤10s | Performance | 3 | Measured, not asserted in CI. Gates Step 8. |
| Idle compute reaches zero | Manual/observational | — | Verified from the Azure bill and metrics over a real idle window; not automatable in CI. |

### Doubles

The existing suite is **mockist** (Moq, `Verify` on interactions) and this feature stays mockist — per the standard, don't switch style mid-project.

| Dependency | Double | Rationale |
|---|---|---|
| `IClock` (new) | Stub with fixed instant | Every deadline and grace-period test sets time explicitly. |
| `IKeyValueRepository<T>` in BL unit tests | Stub / Spy | Canned state in; assert calls out. |
| `IGameStateMachineService` in reconciler tests | Mock (loose) | The interaction *is* the behaviour: did the overdue phase advance, exactly once. |
| `IPokerAttackNotificationHelper` | Spy | Assert broadcasts happened without a real hub. |
| Postgres in contract tests | **Real** (Testcontainers or a disposable local database) | The serialization boundary is the point; a fake would prove nothing. |
| `InMemoryKeyValueRepository<T>` | Fake (already exists) | Keep it — it is a legitimate fake for BL-layer tests and for local dev. This is why Step 4 keeps rather than deletes it. |

### Flakiness controls

This feature is unusually exposed to the standard's flakiness causes — it is *about* time, reconnection, and concurrency. Controls are designed in, not retrofitted:

- **Clock**: introduce `IClock` in `Core.Interfaces` and route *all* deadline and grace-period logic through it. `DateTime.UtcNow` appears today in `Lobby.CreatedAt` and `CleanupBackgroundService`; those move to the injected clock. A single source of time, per the standard.
- **No `Task.Delay` in tests.** Removing production `Task.Delay` is the feature; reintroducing it in tests would be perverse. Reconciler tests advance a fake clock and invoke the reconciler directly.
- **No sleep-to-wait in E2E.** Playwright waits on the specific observable condition (board re-rendered, round number advanced), never a fixed duration.
- **No retries.** Per the cardinal rule, a flaky reconnect test is treated as a real defect — reconnect flakiness in the test almost certainly means reconnect flakiness for players.
- **Ordering**: opponent summaries in the resync payload are ordered at the source so assertions are deterministic.
- **Shared state**: contract tests get a fresh schema/database per run; no suite-wide mutable fixture.
- **Timeouts**: the E2E restart test needs an explicitly justified timeout — it must exceed a real cold start (3–8s today, target ≤10s). Document the chosen value and its basis in the test, and revisit it after Step 8 measures the real number.

### Coverage adequacy

Against the standard's six categories:

1. **Happy path** — restart mid-round, game resumes (SC-001).
2. **Negative/invalid** — resync for an unknown game id, for a game that already ended, for a player not in the game.
3. **Boundaries** — reconnect at grace-period expiry ± 1 tick; deadline exactly at `now`; zero remaining players after elimination.
4. **Error paths** — Postgres unavailable during resync; reconnect attempts exhausted (surface retry, don't die silently).
5. **Concurrency** — two reconnects racing; the same player in two tabs; multiple deadlines expired during one interruption (spec edge cases).
6. **Time-sensitive** — the whole reconciler and grace period; covered by the clock-injection tests above.

### Mapping to spec success criteria

| Criterion | Proven by |
|---|---|
| SC-001 restart mid-round resumes | E2E restart test |
| SC-002 state survives restart | Contract round-trip + E2E |
| SC-003 back to live board ≤10s | E2E with justified timeout |
| SC-004 no game stalls | Unit reconciler tests + E2E |
| SC-005 countdown ±1s | Unit deadline arithmetic |
| SC-006 no eviction inside grace period | Unit grace-period boundary tests |
| SC-007 zero idle compute | Observational (Azure metrics) |
| SC-008 cold start budget | Performance measurement, Step 8 |
| SC-009 cost ≤20% | Observational (billing, post-deploy) |
| SC-010 no new recurring cost | Design review — no paid service added |
| SC-011 no gameplay regression | Existing suite (once repaired) + E2E |

SC-007, SC-009, and SC-010 are deliberately **not** automated. They are properties of a bill and a deployment, and pretending to assert them in CI would be theatre.

## Key Design Decisions

Full rationale in [research.md](./research.md). Summary:

| Decision | Rationale |
|---|---|
| Postgres for live state, not Redis | Redis Basic costs more per month than the idle container being removed — it would defeat the purpose. Postgres already exists, is paid for, and single-digit-ms latency is irrelevant against 10–30s phases. |
| No Azure SignalR Service | Free tier is 20 concurrent connections / 20k messages per day; this app broadcasts per-round to every player in every game. Standard tier is ~$50/mo — far more than the idle container. Reverses the textbook answer on cost grounds. |
| Client-initiated group rejoin | The client is the party that knows it reconnected. The server-side path depends on `PlayerConnectionTracker`, which is empty after a restart. |
| Persisted deadline + reconciler, not a durable-timer service | No new dependency; the reconciler is a few lines and reuses the existing hosted-service pattern. |
| Introduce `IClock` | Required to make deadline and grace-period logic unit-testable without sleeping. Directly serves the flakiness controls above. |
| `maxReplicas` stays 1 | Multi-replica needs a SignalR backplane — a separate, larger problem. Do not change two things at once. |
| Keep `InMemoryKeyValueRepository` | Still the right fake for unit tests and local dev; the DI line is the switch. |

## Risks

| Risk | Mitigation |
|---|---|
| Cold start exceeds the 10s budget (only success criterion resting on an unmeasured baseline) | Measure in Step 8 *before* committing to `minReplicas = 0`. If ReadyToRun is insufficient, warm-on-intent (Step 9) covers the gap; if that fails too, raise `minReplicas` back to 1 — Steps 1–6 remain valuable regardless. |
| Serializing `Deck`/`Card`/`ItemBase` polymorphic types to JSONB | `JsonOptions.Get()` already carries the converters and `TypeInfoResolver` used across the wire; reuse it. Covered by the contract round-trip tests in Step 4. |
| Grace period interacts badly with elimination | `FinishEliminationAsync` calls `LeaveGameAsync` for losers — that path is deliberate removal, not disconnect, and must stay immediate. Explicit unit test listed above. |
| Test project is dead, so regressions land unnoticed | Repair it as a prerequisite to Step 1 (see Testing Strategy). |
| WebSocket keeps app alive, so savings are overnight-only | Accepted and stated in the spec. Not a flaw — inherent to real-time multiplayer. |

## Complexity Tracking

No constitution violations to justify — the constitution is unfilled (see Constitution Check). No new projects, no new services, no new paid dependencies are introduced by this plan.
