# Feature Specification: Scale-to-Zero Resilience

**Feature Branch**: `001-scale-to-zero-resilience`  
**Created**: 2026-08-02  
**Status**: Draft  
**Input**: User description: "docs/SCALE_TO_ZERO_ARCHITECTURE.md — Drive backend cost to ~$0 when nobody is playing, while keeping the delay before the game is playable acceptably small. Multiplayer is the primary driver of use."

## Overview

Today the game server runs continuously because every piece of live game state — lobbies, hands, wallets, scores, player-to-game membership — exists only in the memory of a single running process. Any interruption to that process destroys every game in progress. This has two consequences:

1. **A live defect.** Players in an active game are silently dropped whenever the server restarts, whether from a deploy, a crash, or an ordinary network blip. The game does not resume; it goes dark and stays dark.
2. **A cost floor.** Because losing the process means losing the games, the server can never be allowed to stop, and so it is paid for 24 hours a day even when nobody has played for hours.

This feature makes an in-progress game survive an interruption of the game server. Fixing the defect is what makes the saving possible: once a game can survive the server going away, the server is allowed to go away when nobody is playing.

The defect is worth fixing on its own merits and ships first. The cost reduction is the payoff that follows from it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A game keeps playing through a brief interruption (Priority: P1)

Four friends are partway through a multiplayer round. The server is interrupted — a deploy goes out, or a player's phone drops from Wi-Fi to cellular for a few seconds. Today, the affected players see the game freeze permanently and have to abandon it. After this change, play continues: the interruption is a visible blip of a few seconds, then the board is live again, showing the correct current state, and the round proceeds.

**Why this priority**: This is a defect players hit today, on every deploy and every network hiccup. It delivers value immediately and independently, with no infrastructure change and no cost work attached. Everything else in this feature depends on it — a game that cannot survive an interruption can never be allowed to be interrupted deliberately.

**Independent Test**: Start a multiplayer game with two or more players, restart the game server mid-round, and confirm every player's board returns to life showing correct, current state and the game plays to completion. Testable with no change to hosting configuration.

**Acceptance Scenarios**:

1. **Given** a multiplayer game is mid-round, **When** the game server is restarted, **Then** every connected player's board resumes showing correct current state and the game continues to completion.
2. **Given** a player's network drops for under the reconnect window, **When** connectivity returns, **Then** their board resumes automatically without the player taking any action.
3. **Given** a player was disconnected while the phase advanced, **When** they reconnect, **Then** they see the current phase and the correct remaining time for it — not the phase they left.
4. **Given** the server is interrupted while a player is disconnected, **When** the player returns within the grace period, **Then** they are still a participant in their game and rejoin it rather than finding themselves removed.
5. **Given** a timed phase is in progress, **When** the server is interrupted and the phase's deadline passes before it returns, **Then** the phase advances on the server's return rather than the game stalling.

---

### User Story 2 - The backend costs nothing when nobody is playing (Priority: P2)

Nobody has played since last night. The owner is billed for the hours of idle capacity anyway. After this change, idle hours cost nothing, and cost scales with actual play instead of wall-clock time.

**Why this priority**: This is the stated goal, but it is only safe once Story 1 holds — turning the server off while games cannot survive being turned off would destroy live games. It is deliberately sequenced second.

**Independent Test**: Leave the system with no players for an extended idle period, confirm compute consumption drops to zero for that window, and confirm a player arriving afterward reaches a playable game within the startup budget.

**Acceptance Scenarios**:

1. **Given** no games are in progress and no players are connected, **When** the idle cooldown elapses, **Then** backend compute consumption for that period is zero.
2. **Given** the backend is idle and stopped, **When** a player opens the game, **Then** they reach a playable state within the cold-start budget defined in Success Criteria.
3. **Given** a multiplayer game is in progress, **When** the idle cooldown would otherwise elapse, **Then** the backend is **not** stopped and the game is unaffected.
4. **Given** a brief lull between two games in the same session, **When** players start a new game shortly after, **Then** they do not experience a cold start.

---

### User Story 3 - Starting up feels fast (Priority: P3)

A player opens the game after a quiet period. The backend has to start. This story ensures that start is short enough to go unnoticed, and where possible happens behind screens the player is already reading.

**Why this priority**: Pure refinement of Story 2's user experience. Story 2 delivers the saving without it; this makes the saving invisible to players rather than merely tolerable.

**Independent Test**: Measure time from a player's first interaction on a stopped backend to a playable game, with and without the warm-on-intent behavior, and confirm the perceived wait meets the budget.

**Acceptance Scenarios**:

1. **Given** the backend is stopped, **When** a player opens a screen that precedes play, **Then** startup begins during that screen rather than when the player commits to playing.
2. **Given** a host is waiting in a lobby for others to join, **When** the game starts, **Then** no startup delay is perceived at game start.
3. **Given** the backend is starting, **When** a player is waiting, **Then** they see a clear indication that the game is loading rather than an apparent failure.

---

### Edge Cases

- **A player never comes back.** After the grace period elapses with no reconnect, the player is removed from their game and the remaining players continue without stalling.
- **Every player disconnects at once** (e.g. the server goes down). No game is destroyed on the way down; games remain resumable for the grace period, and a game nobody returns to is cleaned up rather than persisting forever.
- **A player returns after the game already ended.** They are shown the outcome, not a stalled board or an error.
- **The reconnect window is exceeded** because startup took longer than the client was willing to wait. The client must keep trying long enough to cover a realistic cold start, and must surface a retry path if it ultimately gives up rather than sitting silently dead.
- **Multiple deadlines expire during a single interruption** (e.g. a round timer *and* a subsequent phase timer both pass). Phases advance to the correct current phase rather than replaying or skipping arbitrarily.
- **Two reconnect attempts race**, or a player has the game open in two places. The player ends up in exactly one consistent session, not double-counted or split.
- **A player refreshes their browser** mid-game rather than merely losing the network. They rejoin the same game rather than losing it.
- **Cleanup of abandoned games** must happen even though the server is not continuously running — expiry cannot depend on a process that is allowed to stop.
- **A stale record outlives its game.** Retained state is expired on a defined schedule so storage does not grow without bound.

## Requirements *(mandatory)*

### Functional Requirements

**Surviving interruption**

- **FR-001**: A game in progress MUST remain playable across an interruption of the game server, retaining current phase, round, each player's hand, wallet, and score.
- **FR-002**: The system MUST detect when a player's live connection has been re-established and restore that player's ability to receive game updates without requiring any player action.
- **FR-003**: On reconnecting, a player MUST be brought up to date with everything that changed while they were away, such that their view matches the game's true current state.
- **FR-004**: A player's membership in their game MUST be recoverable after an interruption, rather than depending on information that is lost when the server stops.
- **FR-005**: The system MUST retain a player's game association across a browser refresh so a refreshed player rejoins the same game.

**Not evicting players**

- **FR-006**: A disconnected player MUST NOT be removed from their game immediately; they MUST remain a participant for a defined grace period during which they can reclaim their seat.
- **FR-007**: A player who does not return within the grace period MUST be removed, and the remaining players MUST be able to continue without the game stalling.
- **FR-008**: A planned server shutdown MUST NOT cause players to be removed from their games.

**Timed phases**

- **FR-009**: Every timed phase MUST have a recorded deadline that outlives the server process.
- **FR-010**: A phase whose deadline has passed MUST be advanced when the server is next available, rather than the game remaining stalled indefinitely.
- **FR-011**: A player joining or rejoining mid-phase MUST see the correct remaining time for that phase.
- **FR-012**: Phase countdowns MUST remain accurate to the player without requiring the server to be continuously running.

**Cleanup**

- **FR-013**: Abandoned games and lobbies MUST be cleaned up on a defined schedule that does not depend on the server running continuously.
- **FR-014**: Retained game state MUST have a defined expiry so that storage does not grow without bound.

**Idle cost**

- **FR-015**: The backend MUST consume no compute during periods with no players connected and no games in progress.
- **FR-016**: The backend MUST NOT be stopped while any game is in progress or any player is connected.
- **FR-017**: The idle period before stopping MUST be long enough that a brief lull between games, or a short player disconnect, does not stop the backend mid-session.

**Startup**

- **FR-018**: A player arriving at a stopped backend MUST reach a playable state within the cold-start budget in Success Criteria.
- **FR-019**: The system MUST be able to report when it is ready to serve players, so that traffic is not sent to an instance that is still starting.
- **FR-020**: Startup SHOULD be triggered by a player's evident intent to play, before they commit, so the wait overlaps with screens they are already reading.
- **FR-021**: While the backend is starting, players MUST see an indication that the game is loading rather than an apparent error.

**Constraints**

- **FR-022**: The solution MUST NOT introduce any recurring cost exceeding the idle cost it eliminates. Any added paid service must be justified against the saving it enables.
- **FR-023**: Existing gameplay behavior and rules MUST be unchanged; this feature alters resilience and hosting, not how the game plays.
- **FR-024**: Solo play MUST continue to work unchanged.

### Key Entities

- **Live game state**: The authoritative record of an in-progress game — phase, round, hands, wallets, scores, participants. Must outlive any single server process. Currently exists only in memory.
- **Phase deadline**: The moment a timed phase ends, recorded against the game so that both a returning player and a returning server can determine what should have happened and what happens next.
- **Player session**: The association between a player and the game they belong to, including whether they are currently connected and, if not, when they were last seen — the basis for the grace period.
- **Historical game record**: The existing after-the-fact record used for reporting. Unchanged by this feature and distinct from live game state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: **The headline acceptance test passes.** A multiplayer game is started, the server is forcibly restarted mid-round, and the game resumes and plays to completion for every participant. This test fails today.
- **SC-002**: 100% of games in progress survive a server restart, with every player's hand, wallet, and score intact.
- **SC-003**: A player whose connection drops and returns is back to a live, correct board within 10 seconds of connectivity returning, without taking any action.
- **SC-004**: No game stalls after a server interruption — 0% of interrupted games remain stuck in a phase that should have ended.
- **SC-005**: A player who reconnects mid-phase sees a phase countdown accurate to within 1 second of the true remaining time.
- **SC-006**: No player is removed from a game due to a disconnect shorter than the grace period.
- **SC-007**: Backend compute consumption during a full idle hour with no players is zero.
- **SC-008**: A player arriving at a stopped backend reaches a playable game within 10 seconds; with warm-on-intent, the perceived wait at game start is under 2 seconds.
- **SC-009**: Total backend compute cost falls to no more than 20% of its current level at present play volumes.
- **SC-010**: No new recurring service cost is introduced that exceeds the idle cost eliminated (net monthly cost strictly decreases).
- **SC-011**: Zero regressions in gameplay behavior — an unbroken game plays identically before and after.

## Assumptions

- **Multiplayer is the primary driver of use**, and this shapes the whole design. A game session naturally keeps the backend running; the saving comes from the hours when nobody is playing at all, not from stopping between rounds.
- **Cost during active play is not expected to reach zero.** Real-time multiplayer means an open connection means a running server. That is inherent, not a defect. The target is idle cost, not session cost.
- The existing database is available, already paid for, and out of scope for cost. It is the natural home for state that must outlive the server process, and adds no new service and no new cost.
- **Grace period defaults to 60–120 seconds** for a disconnected player before removal. Long enough to cover a realistic restart plus reconnect, short enough that remaining players are not left waiting on someone who has gone.
- **Idle cooldown defaults to 5–10 minutes** before stopping the backend. This deliberately trades a few minutes of idle compute for not tearing down a session during a lull — the right trade given games are the point.
- **Client reconnect attempts must persist long enough to cover a cold start**, which may exceed common defaults. A client that gives up before the server finishes starting fails the whole feature.
- Cold start is expected in the 3–8 second range before optimization, and the budget above assumes it is reduced from there.
- **Single-instance operation is retained.** Running multiple server instances simultaneously requires coordination that is a separate and larger problem, out of scope here. This feature must not depend on multi-instance operation, and must not make it harder later.
- **Client-initiated reconnect is the reliable direction**, because the client is the party that knows its connection was re-established. Server-initiated restoration depends on information lost in the interruption.
- Players tolerate a short, clearly-indicated loading wait; they do not tolerate a silently frozen board.
- Storage growth from retained live state is bounded by expiry and is negligible at current play volumes.

## Out of Scope

- Running multiple server instances simultaneously, and the coordination that would require.
- Adopting a managed real-time connection service. Evaluated and rejected: its free tier's connection and message ceilings are too low for a multiplayer-primary game of this shape, and its paid tier costs substantially more than the idle cost being eliminated. Revisit only if capacity, not cost, becomes the driver.
- Keeping the backend warm by pinging it on a schedule — this recreates the exact cost being removed.
- Database hosting and its cost.
- Any change to game rules, scoring, or player-facing gameplay.
- Changes to the client hosting arrangement, which already costs nothing.
