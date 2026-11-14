# Quickstart: Verifying Scale-to-Zero Resilience

How to run the acceptance test that defines this feature, and how to tell which step you have actually finished.

---

## The acceptance test

**Start a multiplayer game, force a container restart mid-round, confirm the game resumes.**

This currently **fails**. It is SC-001, and it is the single measure of whether Story 1 is done.

### Staged expectations

Each stage maps to an implementation step, so a partial pass tells you exactly where you are:

| Stage | Passes after | Symptom if not yet done |
|---|---|---|
| Client detects reconnect and rejoins its group | Step 1 | Broadcasts never reach the client; board frozen forever |
| Players are not evicted on shutdown | Step 2 | Everyone is removed from their game as the container goes down |
| Round advances instead of wedging | Step 3 | Phase never ends; countdown hits zero and nothing happens |
| Game/hands/wallet survive the restart at all | Step 4 | Game does not exist after restart; 404 on resync |
| Board is correct, not stale, on return | Step 5 | Board shows the phase the player left, not the current one |

Only when all five hold is `minReplicas = 0` safe (Step 8).

---

## Running locally

```powershell
# Full stack via Aspire
dotnet run --project src/ChefKnifeStudios.PokerAttack.AppHost

# Or containers
docker compose up
```

### Simulating the interruption

Local restart of the API is the fastest signal and reproduces the same failure — the source analysis confirmed force-stopping and restarting the container does not resume the game:

```powershell
# With two browser clients in an active round, restart only the API
docker compose restart poker-attack-api
```

Watch for: both clients reconnect, rejoin their game group, resync, and the round continues to completion.

---

## Test suites

```powershell
# Unit + component (NUnit + Moq)
dotnet test src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests

# E2E (Playwright)
cd tests
npx playwright test
npx playwright test e2e/multiplayer-game.spec.ts
```

### Known blocker before any of this works

`Server.BL.Tests` **does not currently compile.** `LobbyServiceTests.cs` references `ILobbyRepository`, `Core.Interfaces.Repos`, `Lobby.HostPlayerId`, and `Lobby.PlayerIds` — none of which exist anymore (the live `Lobby` has `HostPlayer` and `Players`). The project also targets `net9.0` while the solution is on .NET 10.

This is pre-existing, not caused by this feature, but it must be repaired before Step 1 — otherwise there is nowhere to put a regression test. See the plan's Testing Strategy.

---

## Verifying the cost outcome (Steps 8–9)

Not automatable — these are properties of a bill, not a test run.

1. **Measure cold start first.** Time from a cold container to first successful `GET /alive`, before and after ReadyToRun. Budget is ≤10s to playable (SC-008). Do this *before* setting `minReplicas = 0`.
2. **Confirm idle scale-in.** After the cooldown (300–600s) with no players, replica count reaches 0 (SC-007).
3. **Confirm sessions hold the app up.** With a game in progress, the replica is *not* torn down past the cooldown (FR-016).
4. **Compare a full billing period.** Target ≤20% of current compute (SC-009).

If cold start misses the budget, warm-on-intent (Step 9) covers the gap. If it still misses, revert `minReplicas` to 1 — Steps 1–6 remain valuable on their own, because they fix a live bug regardless of hosting.

---

## What "done" means per story

| Story | Done when |
|---|---|
| **1 — survives interruption** (P1) | The five acceptance stages above all pass. Ships with no infrastructure change. |
| **2 — zero idle cost** (P2) | Idle compute is zero and a session is never torn down mid-game. |
| **3 — startup feels fast** (P3) | Cold start within budget, and warm-on-intent makes it imperceptible at game start. |
