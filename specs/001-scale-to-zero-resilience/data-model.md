# Phase 1 Data Model: Scale-to-Zero Resilience

Two new persisted tables plus additive changes to existing in-memory models. Existing historical tables (`Game`, `Round`, `RoundScore`) are **unchanged**.

---

## New table: `state_kv`

Backing store for `PostgresKeyValueRepository<T>`. One table serves all five value types, discriminated by `type`.

| Column | Type | Notes |
|---|---|---|
| `key` | `text` | Primary key. The same key used today by `IKeyValueRepository<T>` (game id, player id, lobby id). |
| `type` | `text` | CLR type discriminator: `ActiveGame`, `GamePlayer`, `Lobby`, `GameStates`, `GameModes`. |
| `value` | `jsonb` | Serialized value, using `JsonOptions.Get()` for converter and `TypeInfoResolver` parity with the wire format. |
| `expires_at` | `timestamptz` | Nullable. TTL sweep target (FR-014). |
| `updated_at` | `timestamptz` | Last write, from `IClock`. |

**Primary key**: `(type, key)` — the current interface scopes keys per `TValue`, so `key` alone is not unique across types. This matters: `ActiveGame` and `GameStates` are both keyed by game id today, and collapsing them into one table without `type` in the key would collide.

**Indexes**:
- `ix_state_kv_type` — supports `GetAllAsync()` per type.
- `ix_state_kv_expires_at` (partial, `WHERE expires_at IS NOT NULL`) — supports the cleanup sweep.

**Schema**: `PokerAttack` (per `DbSchemas.PokerAttack`).

**Note on `BaseEntity`**: `state_kv` deliberately does **not** derive from `BaseEntity`. That base carries an `int Id` identity plus `IsDeleted`/`CreatedOnUtc`/`ModifiedOnUtc`, which suit the historical aggregate-root entities accessed through `IRepository<T>`/Ardalis.Specification. This table is a key-value store with a natural composite key and no soft-delete semantics, reached through `IKeyValueRepository<T>` instead. Forcing it into `BaseEntity` would add a meaningless surrogate key and a soft-delete flag the sweep would have to work around.

---

## New table: `player_session`

Replaces the in-process `PlayerConnectionTracker` as the source of truth for membership, and carries the grace period (FR-006, FR-007).

| Column | Type | Notes |
|---|---|---|
| `player_id` | `text` | Primary key. |
| `game_id` | `text` | Nullable. Current game. |
| `lobby_id` | `text` | Nullable. Current lobby. |
| `connected` | `boolean` | Whether a live connection is currently held. |
| `disconnected_at` | `timestamptz` | Nullable. Set when the last connection closes; cleared on reconnect. Removal is due at `disconnected_at + grace period`. |
| `updated_at` | `timestamptz` | From `IClock`. |

**Indexes**:
- `ix_player_session_game_id` — **this is the index that replaces the `GetAllAsync()` linear scans** in `SignalRNotificationHub.FindPlayerLobbyAsync` / `FindPlayerGameAsync` (`:183–231`).
- `ix_player_session_disconnected_at` (partial, `WHERE disconnected_at IS NOT NULL`) — supports the grace-period sweep.

**Connection ids are deliberately not stored.** They are meaningless across a restart. The in-memory `PlayerConnectionTracker` is retained purely as a cache of live connection ids for the current process (FR-004, Step 6).

---

## Changed model: `ActiveGame`

Currently (`ActiveGame.cs`):

```csharp
public class ActiveGame
{
    public required string Id { get; set; }
    public HashSet<Player> Players { get; set; } = new();
    public int RoundNumber { get; set; } = 0;
}
```

Add:

| Field | Type | Purpose |
|---|---|---|
| `CurrentPhase` | `GameStates?` | Denormalized from the `GameStates` key-value entry so a resync and a reconciliation need one read, not two. |
| `PhaseExpiresAt` | `DateTime?` (UTC) | The persisted deadline replacing `Task.Delay` (FR-009). Null when the phase is untimed. |
| `PhaseStartedAt` | `DateTime?` (UTC) | Lets a returning client compute elapsed as well as remaining, for correct countdown rendering (FR-011). |

**Serialization impact**: `ActiveGame` holds `HashSet<Player>`; `GamePlayer` holds `Deck`, `List<Card>`, and `List<ItemBase>` where `ItemBase` is polymorphic (`ItemWager` among others). JSONB round-tripping must use `JsonOptions.Get()`, which already carries the converters and `TypeInfoResolver` used on the wire. Covered by the contract round-trip tests in the plan's Testing Strategy.

---

## New model: `PlayerSession`

`Core/Models/PlayerSession.cs`, mirroring the `player_session` table. Accessed through a new `IPlayerSessionStore` rather than `IKeyValueRepository<T>`, because its queries are relational (find by `game_id`, find overdue by `disconnected_at`) rather than key lookups — the whole point of introducing the indexes above.

---

## New abstraction: `IClock`

```csharp
public interface IClock { DateTime UtcNow { get; } }
```

Not persisted, but a data-model concern: every timestamp column above is written through it, and every deadline comparison reads through it. Introduced so deadline and grace-period logic is unit-testable without sleeping (see the plan's Flakiness controls). Existing direct uses of `DateTime.UtcNow` — `Lobby.CreatedAt` and `CleanupBackgroundService` — move onto it so there is a single source of time.

---

## Lifecycle and expiry

| Record | Written | Expires |
|---|---|---|
| `state_kv` (`Lobby`) | Lobby creation | 24h after creation (current `_lobbyMaxAge`) |
| `state_kv` (`ActiveGame`, `GamePlayer`, `GameStates`, `GameModes`) | Game start | On `EndGameAsync`; swept if orphaned |
| `player_session` | Player joins lobby/game | On deliberate leave; swept when `disconnected_at` exceeds grace and the player has not returned |

Sweep runs from `CleanupBackgroundService` on the existing interval **and** opportunistically at startup, so an idle period that stopped the container does not defer cleanup indefinitely (R9, FR-013).

---

## Migration

One EF Core migration adding both tables to the `PokerAttack` schema. **Purely additive** — no existing table or column is altered, so it is safe to apply before the code that uses it, and rollback is a drop of two unused tables.
