# Contract: SignalR Reconnect Behaviour

**Steps 1, 2, 6** (Story 1, P1). Satisfies FR-002, FR-004, FR-006, FR-008.

No hub *method signatures* change. What changes is who calls them, when, and what a disconnect means.

---

## Existing surface (unchanged signatures)

| Hub method | Location | Status today |
|---|---|---|
| `JoinGameGroupAsync(string gameId)` | `SignalRNotificationHub.cs:124` | Exists; **no client call sites** |
| `JoinLobbyGroupAsync(string lobbyId)` | `:106` | Exists; **no client call sites** |
| `LeaveGameGroupAsync(string gameId)` | `:130` | Exists |
| `LeaveLobbyGroupAsync(string lobbyId)` | `:112` | Exists |

The fix is not new hub methods — it is **calling the ones that already exist**.

---

## Client contract (new)

`SignalRNotificationService` must:

1. **Subscribe to `Reconnected` and `Closed`.** Neither is subscribed today; `.WithAutomaticReconnect()` at `:97` restores the transport, but the app never learns it happened.

2. **Supply an explicit retry schedule.** The default spans ~30s — shorter than a cold start plus image pull, so the connection dies permanently before the server is up. Schedule must cover the cold-start budget with margin:

   ```
   0s, 2s, 5s, 10s, 15s, 30s
   ```

3. **Re-establish group membership on every reconnect**, calling the existing client methods with the remembered id:

   ```
   on Reconnected:
     if currentGameId  -> JoinGameGroupAsync(currentGameId)
     elif currentLobbyId -> JoinLobbyGroupAsync(currentLobbyId)
     then resync via GET /gameplay/{gameId}/state
   ```

4. **Handle `Closed` as well as `Reconnected`.** `Reconnected` never fires when the connection fully closes rather than reconnecting; the `Closed` path must re-run `InitAsync` and then follow the same rejoin-and-resync sequence.

5. **Remember the current lobby/game id** in `MultiGameDataStore`, persisted to localStorage following the `SoloGameDataStore` precedent, so a browser refresh recovers too (FR-005).

6. **Surface loading state** while reconnecting, and a retry affordance if the schedule is exhausted (FR-021) — never fail silently, which is today's behaviour.

**Direction is deliberate**: rejoin is client-initiated. The server-side `JoinGameGroupForUserAsync` path early-returns when `GetConnections(userId).Count == 0` (`PokerAttackNotificationHelper.cs:148,183`), and the tracker is empty after a restart, so it silently no-ops.

---

## Server contract change: disconnect is no longer eviction

**Today** (`SignalRNotificationHub.OnDisconnectedAsync`, `:43–92`): when a player's last connection closes, they are removed from their lobby and game *immediately* via `LeaveLobbyAsync`/`LeaveGameAsync`.

**After**: the last connection closing marks `player_session.disconnected_at` and sets `connected = false`. Removal happens only when the grace period (60–120s) elapses without a reconnect, executed by the reconciler.

```
OnConnectedAsync:      connected = true,  disconnected_at = null
OnDisconnectedAsync:   connected = false, disconnected_at = now   (last connection only)
reconciler:            if now > disconnected_at + grace -> remove from game/lobby
```

Multi-tab behaviour is preserved: `disconnected_at` is set only when no connections remain, matching the existing `GetConnections(userId).Any()` check.

### Deliberate removal stays immediate

`FinishEliminationAsync` calls `LeaveGameAsync` for eliminated players (`GameService.cs:643`), and players may leave voluntarily. **These are not disconnects and must not acquire a grace period.** The two paths must remain distinct — an eliminated player lingering for 120s would be a visible regression. Explicitly unit-tested (see the plan's Testing Strategy).

---

## Server contract change: no more linear scans

`FindPlayerLobbyAsync` and `FindPlayerGameAsync` (`:183–231`) call `GetAllAsync()` and scan every lobby and game on every disconnect. Replaced by an indexed lookup on `player_session.game_id` / `lobby_id`.

Two reasons beyond performance: the scan hard-assumes one replica holding all state, and after a restart it scans empty state and silently finds nothing to clean up — cleanup must key off durable state, not process memory.

---

## Contract tests

**Component (Tier 1)** — client handler logic with a doubled hub connection:
- `Reconnected` triggers `JoinGameGroupAsync` with the remembered game id.
- `Reconnected` with only a lobby id triggers `JoinLobbyGroupAsync`.
- `Closed` re-runs init and then rejoins.
- Remembered id survives a simulated page reload via localStorage.

**Unit (Tier 0)** — grace period boundary logic against a stubbed `IClock`:
- Not removed at `disconnected_at + grace - 1 tick`.
- Removed at `disconnected_at + grace + 1 tick`.
- Reconnect before expiry clears `disconnected_at` and cancels removal.
- Elimination-driven removal is immediate regardless of grace.
