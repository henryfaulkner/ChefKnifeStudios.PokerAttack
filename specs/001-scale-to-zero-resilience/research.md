# Phase 0 Research: Scale-to-Zero Resilience

All findings verified against the codebase on 2026-08-02. No `NEEDS CLARIFICATION` markers remain.

---

## R1 — Where does live game state actually live?

**Decision**: Move all live state to Postgres behind the existing `IKeyValueRepository<T>`.

**Finding**: `Program.cs:68` registers one open generic:

```csharp
builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));
```

`InMemoryKeyValueRepository<T>` is a `ConcurrentDictionary<string, TValue>` (`InMemoryKeyValueRepository.cs:9`). Five types flow through it: `ActiveGame`, `GamePlayer`, `Lobby`, `GameStates`, `GameModes`. Postgres holds only historical `Game`/`Round`/`RoundScore` for reporting — it is **not** the source of truth for live play.

**Rationale**: The interface is already the right shape (`AddAsync`/`GetAsync`/`UpdateAsync`/`DeleteAsync`/`GetAllAsync`), and every consumer already depends on the abstraction rather than the implementation. A new implementation plus a one-line DI swap externalizes state without touching game logic. This is the single most favourable fact about this codebase for this feature.

**Alternatives considered**:
- **Redis** — rejected on cost. Azure Cache for Redis Basic costs more per month than the idle container being eliminated, which defeats the entire purpose.
- **Rewrite services to take an explicit store** — rejected as unnecessary churn; the seam already exists.
- **Keep in memory, accept the risk** — rejected; this is the defect being fixed.

---

## R2 — Why does the game go dark after a restart?

**Decision**: Client-initiated group rejoin on reconnect, backed by a persisted current-game id.

**Finding**: Four independent failures, any one of which alone is fatal.

1. **No reconnect handler.** `SignalRNotificationService.cs:97` calls `.WithAutomaticReconnect()`, but nothing subscribes to `HubConnection.Reconnected` or `.Closed`. Verified: zero handlers in the client.
2. **`JoinGameGroupAsync` is never called.** The method exists on the client (`:195`) and the hub (`:124`), but has **no client call sites**. Group membership is established server-side only, at lobby-join and game-start in `LobbyService`, which run once during setup.
3. **SignalR groups are in-process** and die with the container. On restart every group is empty, and nothing repopulates them.
4. **`PlayerConnectionTracker` is also empty**, so the server-side `JoinGameGroupForUserAsync` helpers no-op — they early-return when `GetConnections(userId).Count == 0` (`PokerAttackNotificationHelper.cs:148,183`).

**Rationale**: Client-initiated is the reliable direction because the client is the only party that knows the connection was re-established. The server cannot drive this — the information it would need is exactly what the interruption destroyed.

The default `WithAutomaticReconnect()` retry schedule spans only ~30s. A cold start plus image pull can exceed that, so an explicit longer schedule is required or the connection is permanently dead before the server finishes starting. A `Closed` fallback is also needed: `Reconnected` never fires if the connection fully closes rather than reconnecting.

**Alternatives considered**:
- **Server-side re-population from the tracker** — rejected; the tracker is empty after restart (finding 4).
- **Rely on `WithAutomaticReconnect()` alone** — rejected; transport reconnect restores the socket, not group membership, and the app never learns it happened.

---

## R3 — Why do games wedge mid-round?

**Decision**: Persisted `PhaseExpiresAt` plus a reconciler. Delete all five `Task.Delay` progression sites.

**Finding**: Game progression runs on in-process delays:

| Location | Delay | Phase |
|---|---|---|
| `GameService.cs:91` | `RoundTimeMs` | Round timer |
| `GameService.cs:349` | 5000ms hardcoded | Scoreboard dwell |
| `EventNotificationServiceSubscriber.cs:69` | `EliminationTimeMs` | Elimination |
| `EventNotificationServiceSubscriber.cs:76` | `ShopTimeMs` | Shop |
| `EventNotificationServiceSubscriber.cs:84` | `PlayerPowerSelectionTimeMs` | Power selection |

`Task.Delay` is not durable. If the process is evicted mid-round the continuation never runs, no transition fires, and the game wedges permanently. **This is a live bug today** — every deploy or restart during an active round does this to players, independent of scale-to-zero.

**Rationale**: Storing the deadline and reconciling on demand (incoming request, SignalR reconnect, startup) removes the need for the server to be awake to tick. Clients already know phase durations via `GameSettings` and can render countdowns locally.

**Alternatives considered**:
- **Durable-timer service (Azure Durable Functions, Quartz, Hangfire)** — rejected; new dependency and/or cost for what is a deadline column and a few lines of reconciliation.
- **Keep `Task.Delay`, accept wedging** — rejected; it is a current player-facing defect.

---

## R4 — Is scale-to-zero compatible with live WebSockets?

**Decision**: Yes for idle hours, no for active sessions. Accept and design around it.

**Finding**: Two confirmed facts pull in opposite directions:

1. The Container Apps HTTP scaler counts **concurrent requests**, and an open WebSocket counts as in-flight. While players are connected the app **will not** scale in — which is the desired behaviour.
2. Scale-in from the last replica to zero **terminates the replica and drops connections**. There is no "drain the last WebSocket" mode.

**Rationale**: The meaningful idle window is "no games in progress," not "no HTTP requests in N seconds." A session naturally holds the app up; the savings come from the overnight hours when nobody plays at all. That is still the large majority of the month.

This is why the spec states plainly that cost during active play will not reach zero. An open WebSocket means a running replica, by definition — inherent to real-time multiplayer, not a flaw.

**Alternatives considered**: covered in R5.

---

## R5 — Should Azure SignalR Service be adopted?

**Decision**: **No.** This reverses the textbook recommendation, on cost grounds.

**Finding**: Microsoft's own guidance for scale-to-zero with WebSockets is to offload connections to Azure SignalR Service and scale on HTTP/queue depth. But the Free tier ceiling is **20 concurrent connections and 20,000 messages/day** — 20 simultaneous players across the entire game. This app is chatty: per-round broadcasts to every player in every game. Exceeding the free tier means Standard at roughly **$50/month**, far more than the idle container being eliminated.

**Rationale**: Adopting it would increase net cost while the stated goal is to reduce it (FR-022, SC-010). Revisit only if *capacity* — not cost — becomes the driver, i.e. if a single replica can no longer serve the player base.

**Alternatives considered**:
- **Free tier and hope** — rejected; 20 concurrent connections is a real ceiling for a multiplayer-primary app, and hitting it degrades play.
- **Standard tier** — rejected; costs more than it saves.

---

## R6 — Single replica or multi-replica?

**Decision**: Keep `maxReplicas: 1` and sticky sessions. Change only `minReplicas`.

**Finding**: Live config is `minReplicas: 1`, `maxReplicas: 1`, `stickySessions: sticky`, 0.5 vCPU / 1Gi, HTTP scale rule at 10 concurrent requests (never triggers — the floor is already 1). The `stickySessions` setting exists to compensate for in-memory state.

Two code sites hard-assume a single replica: `SignalRNotificationHub.FindPlayerLobbyAsync` and `FindPlayerGameAsync` (`:183–231`) call `GetAllAsync()` and linear-scan every lobby and game on *every* disconnect.

**Rationale**: Multi-replica requires a SignalR backplane — a separate, larger problem. Changing hosting topology and state ownership simultaneously would make failures impossible to attribute. Note that Step 4 makes multi-replica *less* blocked than today by removing the linear scans, without committing to it.

**Alternatives considered**:
- **Go multi-replica now** — rejected; requires the backplane rejected in R5, and doubles the change surface.

---

## R7 — Cold start budget

**Decision**: Target ≤10s to playable, ≤2s perceived with warm-on-intent. **Measure before committing to Step 8.**

**Finding**: Expect 3–8s for a .NET 10 cold start today. Reduction levers, in order of value:

1. **ReadyToRun** (`PublishReadyToRun`, `InvariantGlobalization`, `TieredPGO`) — typically removes 30–50% of JIT-dominated startup. Requires a RID at publish time (`-r linux-x64`) in the Dockerfile.
2. **Production readiness probe.** `MapDefaultEndpoints()` maps `/health` and `/alive` **only when `IsDevelopment()`** (`ServiceDefaults/Extensions.cs:113`). Production therefore has no readiness endpoint, so Container Apps cannot tell when the app is ready and falls back to slower heuristics.
3. **Warm on intent** — fire the readiness endpoint when the user opens the lobby or hovers Play. Especially effective for multiplayer: the host warms the app while waiting for others to join, so the game itself never eats the cold start.
4. **Trim the startup path** — `Program.cs:104–118` maps OpenAPI and Scalar unconditionally. A docs UI does not belong in the production hot start path.

**Rationale**: This is the one success criterion resting on an unmeasured baseline, which is why measurement gates Step 8 rather than following it.

**Alternatives considered**:
- **Cron pinger to stay warm** — rejected; recreates exactly the cost being removed.
- **Native AOT** — rejected for now; incompatible with parts of the EF Core/SignalR stack and a much larger change than ReadyToRun for the remaining margin.

---

## R8 — Disconnect handling under shutdown

**Decision**: Grace period with `disconnected_at`, reconciled like any other deadline.

**Finding**: `OnDisconnectedAsync` (`:43–92`) removes the player from lobby and game *immediately* once their last connection closes, via `LeaveLobbyAsync`/`LeaveGameAsync`. Under scale-to-zero this means a container shutdown evicts every player from their own game on the way down — the shutdown itself would destroy the games it is supposed to preserve.

Also: on the new replica after a restart, the *old* replica's `OnDisconnectedAsync` never ran, and the new one scans empty state and finds nothing to clean up. Cleanup must key off durable state, not process memory.

**Rationale**: 60–120s covers a realistic restart plus reconnect while not leaving remaining players waiting indefinitely on someone who has gone. Reusing the Phase 3 reconciler means no second timing mechanism.

**Important distinction**: `FinishEliminationAsync` calls `LeaveGameAsync` for eliminated players (`GameService.cs:643`). That is *deliberate removal*, not a disconnect, and must remain immediate. The two paths must stay separate — flagged as a risk and given an explicit unit test.

**Alternatives considered**:
- **Never remove on disconnect** — rejected; abandoned players would stall games indefinitely.
- **Immediate removal, as today** — rejected; incompatible with scale-to-zero and already harmful on deploys.

---

## R9 — Cleanup without an always-on process

**Decision**: TTL sweep keyed on `expires_at`, run opportunistically rather than only on a timer.

**Finding**: `CleanupBackgroundService` runs hourly (`_cleanupInterval`) and deletes lobbies older than 24h (`_lobbyMaxAge`), using `DateTime.UtcNow` directly. It only runs while the container is alive — circular once the container is allowed to stop, since an idle system is exactly when cleanup is due and exactly when nothing is running.

**Rationale**: Storing `expires_at` per row lets any wake-up sweep whatever is overdue, and bounds storage growth (FR-014) without depending on continuous uptime. `DateTime.UtcNow` moves to the injected `IClock` so the sweep is unit-testable.

**Alternatives considered**:
- **External scheduler / cron** — rejected; a cron pinger recreates the idle cost (R7).
- **Database-side TTL job** — rejected; adds operational surface on self-hosted Postgres for no gain over an opportunistic sweep.
