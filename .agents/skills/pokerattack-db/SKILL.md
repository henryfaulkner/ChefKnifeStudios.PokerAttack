---
name: pokerattack-db
description: Connects to PokerAttack PostgreSQL and explains persisted games, rounds, scores, schema and migration state. Use when querying PokerAttack data, verifying database access, investigating persistence or gathering migration evidence; live lobbies and sessions require application inspection.
---

# PokerAttack database

Resolve application source paths from the PokerAttack repository root. This skill is specific to database `expensetrackerdb`, schema `PokerAttack`, on the existing shared Linode. Development and disposable pilot databases are separate targets; never infer production from development settings.

1. Read [connection.md](references/connection.md) for the selected target and credential source. Use an existing `ConnectionStrings__PokerAttackDB`, an explicitly selected appsettings file, or existing libpq configuration. Keep credentials in memory; never print a connection string or obtain one from a cheat sheet.
2. Read [tables.md](references/tables.md) before interpreting data. Verify current columns and EF history against the selected database; checked-in migrations do not establish live state.
3. Use `scripts/query.ps1` for an authenticated app connection. With existing verified SSH access, `scripts/query-vm.ps1` runs reviewed read-only SQL through the VM's local PostgreSQL account without transferring a DB password. This privileged route does not establish app-role permissions.
4. Both helpers run reviewed SQL in a bounded, read-only Repeatable Read transaction and roll it back. They check database identity before the supplied SQL. Use explicit columns, deterministic ordering and a reasonable display limit. They are not sandboxes for arbitrary SQL or untrusted database functions.
5. Use [queries.md](references/queries.md) for inventory and persistence recipes. State target/schema, UTC bounds, soft-delete handling, counts and limitations. Default to aggregate results; omit player identifiers/display names unless the request needs them.

From the repository root:

```powershell
& ./.agents/skills/pokerattack-db/scripts/query-vm.ps1 -Sql @'
SELECT current_database(), current_user,
       current_setting('transaction_read_only') AS read_only,
       current_setting('transaction_isolation') AS isolation;
'@
```

The VM helper defaults to the previously verified host `170.187.142.115`, root SSH key `~/.ssh/id_ed25519_linode_us_southeast`, database `expensetrackerdb`. It requires existing trusted SSH host keys. See the connection reference before choosing another target.

## Interpretation and ownership

- `Games` are persisted starts, not proof of completion or currently active games. Integer `Id` is the DB key; `ClientId` identifies the in-memory game.
- `Rounds` and `RoundScores` are saved by multiplayer `EndRoundAsync`. A score is the value captured at that round boundary, not a durable event log of individual hands or wallet actions.
- Lobbies, active multiplayer state, hands, decks and solo games live in singleton in-memory repositories. Database counts cannot measure or recover these sessions.
- Normal EF reads exclude `IsDeleted=true`. Raw SQL does not; state whether a report includes deleted rows and filter all participating tables for a live-data report.
- `public.__EFMigrationsHistory` is shared with ExpenseTracker. Discover its actual location/contents and compare only PokerAttack migration IDs. Never move, clear or replace shared history.
- Queries do not authorize migrations, cleanup, role changes, credential rotation, backups/restores or co-tenant modifications. Perform those only within an explicitly authorized operational task.

Keep the shared `ExpenseTracker` schema and other databases intact. Production uses the restricted `pokerattack_runtime` role; legacy shared postgres password rotation is explicitly deferred. Distinguish privileged VM peer access from runtime-role verification, and prefer an existing restricted reader when available.
