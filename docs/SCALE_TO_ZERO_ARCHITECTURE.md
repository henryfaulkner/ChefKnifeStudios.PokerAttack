# Scale-to-Zero Architecture Analysis

**Goal:** Drive backend cost to ~$0 when nobody is playing, while keeping the delay
before the game is playable acceptably small.

**Current deployment:** Azure Container Apps (Consumption), `poker-attack-api` in
`poker-attack-rg`, ChefKnifeStudios subscription.

**Key constraint:** *Multiplayer is the primary driver of use.* This drives the
whole design. Postgres is self-hosted on Linode and is out of scope for cost.

---

## 1. Why you are paying money right now

Live config, read from Azure:

| Setting | Value | Consequence |
|---|---|---|
| `minReplicas` | **1** | One replica runs 24/7. This is the whole bill. |
| `maxReplicas` | 1 | No horizontal scale. |
| `cpu` / `memory` | 0.5 vCPU / 1Gi | Billed continuously at idle rates. |
| Scale rule | HTTP, 10 concurrent req | Never triggers — floor is already 1. |
| `stickySessions` | `sticky` | Compensating for in-memory state (see §2). |
| Workload profile | Consumption | Correct; scale-to-zero *is* supported here. |

Container Apps Consumption includes a monthly free grant (~180k vCPU-s, ~360k
GiB-s). A 0.5 vCPU / 1Gi replica running continuously burns roughly **1.3M
vCPU-seconds per month** — far past the grant. That idle replica is essentially
the entire server cost.

The client (`poker-attack-webapp`) is already a **Free** Static Web App and costs
nothing.

---

## 2. The two blockers

### Blocker A: all authoritative game state is in process memory

`Program.cs:68`

```csharp
builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));
```

`InMemoryKeyValueRepository<T>` is a `ConcurrentDictionary` in RAM. Everything
live is in there: `Lobby`, `ActiveGame`, `GamePlayer`, `SoloGame`, `GameStates`,
`GameModes`. `PlayerConnectionTracker` is in-process too, and SignalR groups are
in-process — hence `stickySessions: sticky`.

Postgres holds only historical `Game`/`Round`/`RoundScore` rows for reporting. It
is **not** the source of truth for live play. Scaling to zero today destroys every
lobby and every game in progress.

### Blocker B: the server holds wall-clock timers

Game progression runs on `Task.Delay` inside the server process:

| Location | Delay |
|---|---|
| `GameService.cs:91` | `RoundTimeMs` (10s) — round timer |
| `GameService.cs:349` | 5s — scoreboard dwell |
| `EventNotificationServiceSubscriber.cs:69` | `EliminationTimeMs` (15s) |
| `EventNotificationServiceSubscriber.cs:76` | `ShopTimeMs` (15s) |
| `EventNotificationServiceSubscriber.cs:84` | `PlayerPowerSelectionTimeMs` (20s) |

`Task.Delay` is not durable. If the container is evicted mid-round the
continuation never runs and **the game wedges forever** — no round end, no
transition. This is a **live bug today**: every deploy or restart during an
active round already does this to your players. It is independent of
scale-to-zero and worth fixing on its own merits.

### Blocker C: the client never rejoins its SignalR group after reconnect

**Confirmed empirically:** force-stopping and restarting the container does not
resume the game. Traced to root cause — this is not one bug but a chain of four,
and *any one of them alone* is fatal.

**1. There is no `Reconnected` handler.** `WithAutomaticReconnect()` is set
(`SignalRNotificationService.cs:97`), but nothing subscribes to
`HubConnection.Reconnected` or `.Closed`. A full grep across the client returns
zero handlers. So the transport silently reconnects and the app never learns it
happened.

**2. Nothing ever calls `JoinGameGroupAsync`.** The client exposes
`JoinGameGroupAsync` / `JoinLobbyGroupAsync` (`SignalRNotificationService.cs:195`,
`:162`) and the hub exposes matching methods (`SignalRNotificationHub.cs:124`,
`:106`) — but **there are no call sites anywhere in the client.** Group
membership is established *server-side only*, at lobby-join and game-start:

```
LobbyService.cs:82,152  → JoinLobbyGroupForUserAsync(...)
LobbyService.cs:500     → JoinGameGroupForUserAsync(...)   // once, at game start
```

Those run **once**, during game setup. After a restart they never run again.

**3. SignalR groups are in-process and die with the container.** Group membership
lives in the hub's memory. On restart every group is empty. Since the only code
that populates groups is the game-start path in `LobbyService`, and that path is
not re-executed, **the player is never in a group again.** Every subsequent
`BroadcastToGameAsync` goes to an empty group — the server "does not reconnect to
the client" exactly as observed.

**4. `PlayerConnectionTracker` is also empty**, so the
`JoinGameGroupForUserAsync` helpers would no-op anyway: they early-return when
`GetConnections(userId).Count == 0` (`PokerAttackNotificationHelper.cs:148,183`).

**Consequence:** even with durable state and durable timers, the game stays dark
after a restart. Reconnect is a **prerequisite**, not a nice-to-have — and note
this is *already* broken today for ordinary network blips and deploys, not only
for scale-to-zero.

### Also note

`SignalRNotificationHub.FindPlayerLobbyAsync` / `FindPlayerGameAsync` call
`GetAllAsync()` and linear-scan every lobby and game on *every disconnect*. This
hard-assumes one replica holding all state, and will misbehave the moment there
are two.

Worse, on reconnect after a restart, `OnDisconnectedAsync` on the *old* replica
never ran — but on the new one, the first real disconnect will scan empty state
and silently find nothing to clean up. Cleanup must key off durable state, not
process memory.

---

## 3. The honest constraint on multiplayer

I verified this rather than assuming it, because it decides the whole design:

**A scaled-to-zero Container App and a live WebSocket are fundamentally at odds.**

Two confirmed facts:

1. The Container Apps HTTP scaler counts **concurrent requests**, and an open
   WebSocket counts as an in-flight request. So while players are connected, the
   app **will not** scale in — good, that's what you want.
2. But scale-in from the last replica to zero **terminates the replica and drops
   the connections**. There is no "wait for the last WebSocket to close" mode.
   Microsoft's own guidance is to offload connections to Azure SignalR Service
   and scale the app on HTTP/queue depth instead.

**Implication:** With multiplayer primary, the meaningful idle window is "no games
in progress," not "no HTTP requests in the last N seconds." A game session
naturally holds the app up; the savings come from the hours overnight when nobody
is playing at all. That is still the large majority of the month — this is well
worth doing — but it is not "scale to zero between rounds."

### Why Azure SignalR Free tier does not solve it here

The Free tier is **20 concurrent connections and 20,000 messages/day**. That is
20 simultaneous players *total across the whole game*, and this app is chatty
(per-round broadcasts to every player in every game). For a multiplayer-primary
app that is a real ceiling, and blowing past it means the Standard tier at
roughly **$50/month** — which is far more than the idle container you are trying
to eliminate.

**I am reversing my earlier suggestion here.** Azure SignalR is the textbook
answer to this problem, but at your scale it costs more than it saves. Skip it
unless you outgrow a single replica for capacity reasons rather than cost ones.

---

## 4. Recommended architecture

### Phase 1 — Make timers durable (do this first, regardless)

Replace `Task.Delay`-driven progression with a **persisted deadline plus a
reconciler**:

- Store `phase_expires_at` (UTC) on the game record when entering a timed phase.
- On any incoming request, on SignalR reconnect, and on app startup, check for
  games whose deadline has passed and advance them.
- Clients already know the phase duration and can render the countdown locally,
  so the server does not need to be awake to tick.

This fixes the wedging bug now, and it is the prerequisite for the app ever being
allowed to stop. Without it, nothing else here is safe.

### Phase 2 — Externalize state

Implement `PostgresKeyValueRepository<T>` behind the **existing**
`IKeyValueRepository<T>` interface. The interface is already the right shape
(`AddAsync`/`GetAsync`/`UpdateAsync`/`DeleteAsync`/`GetAllAsync`), so this is a
new implementation plus a one-line DI swap — not a rewrite of game logic.

Schema: `state_kv (key text pk, type text, value jsonb, expires_at timestamptz,
updated_at timestamptz)`.

You already have Postgres, a connection string, EF Core, and a migration
pipeline, so this adds no new service and no new cost. Latency is single-digit
milliseconds against 10–20 second game phases — irrelevant.

Two things to fix while you are in there:

- Replace the `GetAllAsync()` disconnect scans with an indexed lookup
  (`player_id → game_id`). The linear scan becomes a real query.
- Move `CleanupBackgroundService`'s TTL sweep onto `expires_at`. Today cleanup
  only runs while the always-on container is alive, which is circular.

**Do not use Redis for this.** Azure Cache for Redis Basic costs more per month
than the idle container you are removing. It would defeat the purpose.

### Phase 3 — Reconnect-survivable multiplayer (the actual blocker)

This is the phase that makes the acceptance test pass. It has four parts,
matching the four failures in Blocker C. Order matters.

**3a. Make the client aware it reconnected.** In `SignalRNotificationService`,
wire up the handlers that do not currently exist:

```csharp
_hubConnection.Reconnected += async connectionId => await OnReconnectedAsync();
_hubConnection.Closed      += async error        => await OnConnectionLostAsync(error);
```

`WithAutomaticReconnect()` only retries for ~30s by default. A cold start plus
image pull can exceed that, so supply an explicit longer retry policy —
otherwise the connection is permanently dead before the server is even up:

```csharp
.WithAutomaticReconnect(new[] {
    TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)
})
```

Also add a `Closed` fallback that re-runs `InitAsync` — `Reconnected` never fires
if the connection is fully closed rather than reconnecting.

**3b. Re-establish group membership on every reconnect.** This is the core fix.
The client already has `JoinGameGroupAsync` / `JoinLobbyGroupAsync` and they are
simply never called. In the reconnect handler, call them with the current
lobby/game id:

```csharp
async Task OnReconnectedAsync()
{
    if (_currentGameId is not null)  await JoinGameGroupAsync(_currentGameId);
    else if (_currentLobbyId is not null) await JoinLobbyGroupAsync(_currentLobbyId);
    await ResyncStateAsync();
}
```

This requires the service to **remember** the current lobby/game id. Track it
when the player joins, and persist it to localStorage so it survives a browser
refresh too — the solo path already does this via `SoloGameDataStore`, so follow
that precedent for multi.

Do **not** rely on the server-side `JoinGameGroupForUserAsync` path for this. It
depends on `PlayerConnectionTracker`, which is empty after restart, so it
silently no-ops. Client-initiated rejoin is the reliable direction because the
client is the one that knows it reconnected.

**3c. Resync game state after rejoining.** Add a rehydration endpoint:

```
GET /gameplay/{gameId}/state?playerId={playerId}
```

returning current phase, `phase_expires_at`, round number, the player's hand,
wallet, score, and opponent summaries. Call it after the group rejoin in 3a/3b.
Rejoining the group alone only restores *future* broadcasts — the client has
missed everything that happened while it was away, so without this it sits with a
stale board.

Deriving the remaining phase time from `phase_expires_at` (Phase 1) is what lets
a returning client land mid-phase with a correct countdown.

**3d. Rebuild connection tracking from durable state.** Either persist
`PlayerConnectionTracker`, or better, derive membership from the durable game
records so it is reconstructed automatically. Keep the in-memory map purely as a
cache of live connection ids.

**Important interaction with `OnDisconnectedAsync`:** today a disconnect
*immediately* removes the player from the lobby and game
(`SignalRNotificationHub.cs:56-82` → `LeaveGameAsync`). With scale-to-zero that
is actively harmful — a container shutdown would evict every player from their
own game as it goes down. Change this to a **grace period**: mark the player
`disconnected_at` and only remove them after, say, 60–120s without a reconnect,
via the same reconciler from Phase 1. Without this change, scaling to zero
guarantees every in-flight game is destroyed on the way down.

### Phase 4 — Turn on scale-to-zero, carefully

```
minReplicas = 0
maxReplicas = 1          # keep at 1 until §2 "also note" is resolved
```

Keep `maxReplicas: 1` and sticky sessions. Going multi-replica requires a SignalR
backplane, which is a separate and larger problem; do not change both at once.

Then tune the **cooldown period** — this is the important knob for you. Set it
generously (e.g. 300–600s) so a brief lull between games or a player's short
disconnect does not tear down the replica mid-session. You are trading a few
minutes of idle compute for not dropping live games. That trade is clearly worth
it.

### Phase 5 — Cold start

Expect **3–8 seconds** for a .NET 10 cold start today. Reduce it in this order:

1. **ReadyToRun.** In the WebAPI `.csproj`:
   ```xml
   <PublishReadyToRun>true</PublishReadyToRun>
   <InvariantGlobalization>true</InvariantGlobalization>
   <TieredPGO>true</TieredPGO>
   ```
   Requires a RID at publish time (`-r linux-x64`) in the Dockerfile. Typically
   removes 30–50% of JIT-dominated startup.

2. **Fix the missing production health probe.** `MapDefaultEndpoints()` in
   `ServiceDefaults` maps `/health` and `/alive` **only when `IsDevelopment()`**.
   Production has no readiness endpoint, so Container Apps cannot tell when the
   app is ready and falls back to slower heuristics. Map a lightweight readiness
   endpoint in production and wire it up as the probe.

3. **Warm on intent.** Fire `GET /alive` when the user opens the lobby screen or
   hovers "Play" — before they commit. The container warms during the seconds
   they spend reading the screen. For multiplayer this is especially effective:
   the host warms the app while waiting for others to join, so the game itself
   never eats the cold start.

4. **Trim the startup path.** Gate OpenAPI/Scalar behind a non-production check;
   it is a docs UI and does not belong in the hot start path.

**Not recommended:** a cron pinger to stay warm. That recreates the cost you are
removing.

---

## 5. Honest cost expectation

| Item | Now | After |
|---|---|---|
| Container App idle (overnight, no games) | continuous | **$0** |
| Container App during sessions | continuous | request/connection-seconds only |
| Static Web App | $0 (Free) | $0 |
| Postgres (Linode) | out of scope | out of scope |
| Azure SignalR | — | **not adopted** (costs more than it saves) |

Realistically: if the game sees a few hours of play a day, you drop to roughly
**10–20% of current compute**, and quite possibly inside the free grant entirely.
The remaining cost scales with actual play, which is the property you asked for.

You will **not** get to zero cost *during* multiplayer sessions — an open
WebSocket means a running replica, by definition. That is inherent to real-time
multiplayer, not a flaw in the setup.

---

## 6. Suggested order

The confirmed restart failure reorders this. Reconnect handling (3a/3b) is now
first: it is the smallest change, it is a **user-visible bug today** on every
deploy and every network blip, and it can ship on its own before any of the
larger work.

1. **Reconnect handlers + group rejoin** (3a, 3b). Small, self-contained, fixes a
   live bug. Ship independently.
2. **Disconnect grace period** (3d note). Prevents shutdown from evicting
   everyone. Must land before `minReplicas = 0`.
3. **Durable timers + reconciler** (Phase 1). Fixes mid-round wedging.
4. **`PostgresKeyValueRepository<T>`** (Phase 2). State survives restart.
5. **State resync endpoint** (3c). Returning clients see a correct board.
6. **Connection tracking from durable state** (3d).
7. **Production health endpoint + ReadyToRun** (Phase 5).
8. **`minReplicas = 0` with a generous cooldown.** Measure real cold start.
9. **Warm-on-intent from the lobby screen.**

Steps 1–6 are the real work and are justified on correctness grounds alone —
they fix bugs your players hit today, independent of cost. Steps 7–9 are small
and deliver the actual savings.

### Acceptance test

**Start a multiplayer game, force a container restart mid-round, confirm the game
resumes.** This currently fails. Track it in stages, since each maps to a step
above:

| Stage | Expected after |
|---|---|
| Client detects reconnect and rejoins group | Step 1 — broadcasts reach the client again |
| Players are not evicted on shutdown | Step 2 |
| Round advances instead of wedging | Step 3 |
| Game/hands/wallet survive restart at all | Step 4 |
| Board is correct, not stale, on return | Step 5 |

Only when all five hold is `minReplicas = 0` safe. Scaling to zero does not
introduce this failure — it just triggers the existing one far more often.

---

## 7. As-built (feature `001-scale-to-zero-resilience`)

This section records what was actually implemented against the plan above, and what remains
as a manual step outside this repo. See `specs/001-scale-to-zero-resilience/` for the full
spec, plan, data model, and contracts.

### Implemented (code, merged)

| Step | Status | Notes |
|---|---|---|
| 1. Client reconnect + group rejoin | Done | `SignalRNotificationService` — explicit retry schedule (0/2/5/10/15/30s), `Reconnected`/`Closed` handlers, `MultiGameDataStore` persists current game/lobby id to localStorage |
| 2. Disconnect grace period | Done | `SignalRNotificationHub.OnDisconnectedAsync` marks `player_session.disconnected_at` instead of evicting; removal happens via the reconciler after `ResilienceSettings.GracePeriodSeconds` (default 90s) |
| 3. Durable timers + reconciler | Done | `ActiveGame.PhaseExpiresAt`/`CurrentPhase`/`PhaseStartedAt`; `PhaseReconcilerService` advances overdue phases on startup, on SignalR reconnect, and on the resync endpoint; all five `Task.Delay` progression sites removed |
| 4. `PostgresKeyValueRepository<T>` | Done | `state_kv` (composite `(type,key)` PK) and `player_session` tables added via a purely-additive EF migration; DI swapped from `InMemoryKeyValueRepository<>` to `PostgresKeyValueRepository<>` in `Program.cs`; `InMemoryKeyValueRepository<>` kept registered for tests/local dev |
| 5. State resync endpoint | Done | `GET /gameplay/{gameId}/state?playerId={playerId}` — phase, deadlines, `serverTimeUtc`, own hand/wallet/score, opponent summaries (score + elimination only) |
| 6. Connection tracking from durable state | Done | The old `GetAllAsync()` linear-scan disconnect helpers were dead code once grace-period removal moved to the reconciler, and were deleted rather than reimplemented; `PlayerConnectionTracker` was already a pure per-process connection-id cache |
| 7. Production readiness probe + ReadyToRun | Done (code) | `/alive` mapped in all environments (anonymous, no DB round-trip); OpenAPI/Scalar gated out of Production; `PublishReadyToRun`/`InvariantGlobalization`/`TieredPGO` + `linux-x64` RID in the WebAPI csproj and Dockerfile |
| 9. Warm on intent | Done | `IWarmupService` fires a debounced (10s), fire-and-forget `GET /alive` when the lobby page loads and on Play-button hover |

### Not done here — requires a live environment

| Step | Blocker | How to complete |
|---|---|---|
| Cold start measurement | No Docker/container runtime available in this session | `docker build -f src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Dockerfile -t poker-attack-api .`, then time from `docker run` to the first successful `GET :8080/alive`. Compare against a build with `PublishReadyToRun` disabled to get the before/after numbers this doc's §5 estimated at 3–8s pre-optimization. Record both here and in `specs/001-scale-to-zero-resilience/quickstart.md`. |
| 8. `minReplicas = 0` with cooldown | Requires Azure portal/CLI access to the live Container App, and **must not** be applied before cold start is measured (see fallback below) | Once cold start is confirmed within budget: set `minReplicas: 0`, keep `maxReplicas: 1` and `stickySessions: sticky`, set the scale cooldown to 300–600s. Then configure the readiness probe to target `/alive`. |
| Cost comparison | Requires a full billing period post-deploy | Compare a full month's Container Apps compute against the pre-change baseline in §1; target ≤20% (SC-009). |

**Fallback if cold start misses budget:** leave `minReplicas` at 1. Steps 1–7 and 9 above are
already merged and valuable independent of hosting — they fix live defects (restart wedging,
group-rejoin failure, immediate eviction on disconnect) regardless of whether scale-to-zero
itself is ever turned on.
