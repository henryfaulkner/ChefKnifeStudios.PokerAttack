# WP03 - API hosting contract

Status: repository implementation verified, 2026-11-14. Source base `b6a4d17d8eb42e9f9bee8214021406ff8be22659` plus local migration edits; nothing committed, published or deployed.

## Behavior

- `Program.cs` uses the new hosting extension before routing: forwarded headers, exception handling, host filtering, routing, named CORS policy. Only exact IPv4/IPv6 loopback proxy addresses are trusted, with one forwarded hop. Forwarded Host is not accepted. Hub path and JSON/player-ID protocol remain unchanged.
- Production host defaults include the two API names and loopback probes. Browser origins are an explicit semicolon-delimited list; wildcard/path origins fail startup. An empty list grants no browser origin. Development has a separate local-origin override.
- `/health/live` uses the existing live self-check; `/health/ready` checks completed startup, absence of shutdown, known EF migration compatibility and mapped table/column reads in a read-only transaction. A linked three-second cancellation and command timeout bound DB work; health registration has a five-second timeout. It never migrates or creates schema/history. Probe responses are plain status, with no provider exception/connection details.
- `/test/cleanup` and `/test/signal-r` are not mapped unless the environment is exactly Development or Staging, `Testing:EnableEndpoints` is true, and the parsed database is `pokerattack_pilot` or has the `pokerattack_test_` prefix with an identifier suffix. Production and custom `prod` environment remain protected even when enabled. The production shared/development databases do not meet this guard.
- Loopback-only `/_ops/status` reports counts of in-memory active-game, lobby and solo-game repository entries without player identifiers. Forwarded public clients and untrusted peers spoofing forwarded loopback addresses receive 404. Caddy must also block `/_ops/*` publicly in WP05. Counts are operator observations, not a durable session ledger or proof that old sessions survive restart.
- Host shutdown timeout is 30 seconds. Live matches still require maintenance/drain or explicit reset.

## Configuration matrix for WP05

| Key | Production | Pilot/local |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Staging` for isolated pilot; `Development` for local debugging |
| `ASPNETCORE_URLS` | `http://127.0.0.1:8081` | `http://127.0.0.1:8082` for pilot; local fixture uses its own port |
| `AllowedHosts` | `api.playpokerattack.com;localhost;127.0.0.1;[::1]` | Pilot hostname plus loopback; committed defaults allow both API names |
| `Hosting:AllowedOrigins` / env `Hosting__AllowedOrigins` | Exact apex/www playpokerattack.com and default SWA origin | Exact isolated pilot-client origin; initially `http://localhost:5173` |
| `Hosting:DevelopmentAllowedOrigins` | Ignored | Local 7150/5186/7333 and 5173 origins in Development only |
| `Testing:EnableEndpoints` | false; production environment denies even if true | true only when runner access and isolated DB are established |
| `ConnectionStrings:PokerAttackDB` | Protected runtime connection to `expensetrackerdb` / PokerAttack-scoped role | Separate pilot/test DB and restricted role; no production cleanup |

No new credentials added. Existing ignored development connection values still require WP05 build-context cleanup and WP06 coordinated rotation. Shared EF history location/ownership is unchanged.

## Verification

- API Release build passed. Existing dependency-advisory/obsolete API warnings remain visible; none suppressed to claim success.
- New NUnit hosting project is included in the solution and GitHub CI. It uses actual ASP.NET TestServer middleware/routes and a dedicated random-name PostgreSQL 16 container with a random password delivered through process environment, a random loopback-only port and tmpfs storage. Fixture-only migrations/column changes affect only disposable `pokerattack_test_*` databases. Container is removed on completion; no production connection is used.
- Hosting suite: **26 passed, zero failed/skipped**. Covers exact allowed/denied CORS, empty/invalid origins, host rejection, trusted IPv4/IPv6 forwarding and one-hop limit, untrusted spoofing, operator counts/access, production/custom-prod test-route denial, isolated Development/Staging test-route success, startup/shutdown readiness, bounded failed DB, missing migrations without schema creation, compatible schema, broken column and repaired schema.
- Existing BL suite: **11 passed, zero failed/skipped**.
- The new test project explicitly aligns EF Relational 9.0.9 with the WebAPI dependency graph; the initial 9.0.8/9.0.9 reference warning was resolved without changing the production Data project's package versions.
- These are repository/isolated tests, not public pilot HTTPS, resource-limit or multiplayer acceptance. B04 stays open until later client/container checks. B02 stays open until WP04 client recovery is verified.

Forwarding implementation follows [ASP.NET Core proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0); tests use the installed .NET 10 TestServer APIs and inspect actual middleware behavior.

Next package: WP04 client recovery and isolated E2E preparation.
