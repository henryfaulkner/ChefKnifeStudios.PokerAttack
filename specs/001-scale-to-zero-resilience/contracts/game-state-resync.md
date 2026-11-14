# Contract: Game State Resync Endpoint

**Step 5** (Story 1, P1). Satisfies FR-003 and FR-011.

Rejoining a SignalR group only restores *future* broadcasts. A client returning after an interruption has missed everything that happened while away and would otherwise sit on a stale board. This endpoint rehydrates it.

---

## Endpoint

```
GET /gameplay/{gameId}/state?playerId={playerId}
```

Added to `PokerAttackApiEndpoints.Gameplay` alongside the existing constants:

```csharp
public const string GetGameState = $"{EndpointGroup}/{{gameId}}/state";
```

Mapped in `GameplayEndpoints.MapGameplayEndpoints`, following the established `group.MapGet(...).WithName(...).Produces(...)` pattern and returning `Ardalis.Result` like its siblings.

**Called by the client**: after group rejoin in the `Reconnected` handler, and on initial load of an in-progress game (browser refresh, FR-005).

---

## Response — 200 OK

```jsonc
{
  "gameId": "…",
  "roundNumber": 3,
  "currentPhase": "InGame",          // GameStates
  "phaseStartedAt": "2026-08-02T14:22:31Z",
  "phaseExpiresAt": "2026-08-02T14:23:01Z",  // null when untimed
  "serverTimeUtc": "2026-08-02T14:22:46Z",   // see Clock skew below

  "player": {
    "playerId": "…",
    "cardsInHand": [ /* CardDTO */ ],
    "score": 1240,
    "wallet": 380,
    "handsRemaining": 3,
    "discardsRemaining": 2,
    "isEliminating": false,
    "isEliminated": false,
    "playerPower": { /* nullable */ },
    "purchasedItems": [ /* ItemBase */ ]
  },

  "opponents": [
    { "playerId": "…", "name": "…", "score": 980, "isEliminating": false, "isEliminated": false }
  ]
}
```

**`opponents` is ordered by `playerId`** so assertions are deterministic — an ordering guarantee at the source, per the flakiness rules in the plan's Testing Strategy.

**Opponents expose score and elimination status only.** Never another player's hand, wallet, deck, or items — the existing broadcast payloads do not leak these, and resync must not become a side channel that reveals what live play conceals.

---

## Clock skew

`serverTimeUtc` is returned so the client derives remaining phase time as `phaseExpiresAt - serverTimeUtc` rather than trusting its own clock. A device with a skewed clock would otherwise render a countdown that is wrong by the skew, breaking SC-005 (±1s) for reasons unrelated to the server.

---

## Error responses

| Status | When | Client behaviour |
|---|---|---|
| `404 Not Found` | Unknown `gameId`, or the game has ended and been cleaned up | Show the game-over/result screen, not a stalled board (spec edge case: "player returns after the game already ended") |
| `404 Not Found` | `playerId` is not a participant in this game | Return to lobby list |
| `400 Bad Request` | Missing or malformed parameters | Log; treat as unrecoverable for this game |
| `503 Service Unavailable` | Durable store unreachable | Retry with backoff; keep the loading indicator (FR-021) rather than showing a false failure |

The 404-for-non-participant case deliberately does **not** distinguish itself from an unknown game — it avoids confirming to a caller whether an arbitrary game id exists.

---

## Side effect: reconciliation

This endpoint is one of the reconciler trigger points (Step 3). Before building the response it advances any phase whose `phaseExpiresAt` has passed, so a returning client receives the *current* phase rather than the stale one it left.

This makes the endpoint non-idempotent in effect, though not in response shape: two rapid calls return the same body, but the first may have advanced the game. That is intentional — a returning player is precisely when reconciliation is most needed. Reconciliation must be safe under concurrent calls (spec edge case: "two reconnect attempts race"), enforced by a conditional update on the deadline so only one caller can advance a given phase.

---

## Contract tests

Per the plan's layer assignment, tested at **contract level (Tier 2)** via `WebApplicationFactory`:

- Returns 200 with correct phase and remaining time for a mid-phase rejoin.
- Returns the requesting player's own hand, and never an opponent's hand.
- `opponents` ordering is stable across calls.
- Returns 404 for unknown game, ended game, and non-participant.
- Advances an overdue phase exactly once when called concurrently.
