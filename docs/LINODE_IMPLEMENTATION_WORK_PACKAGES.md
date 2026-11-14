# PokerAttack Linode implementation work packages

**Status:** WP01 inventory, WP02 local pipeline isolation/baseline and WP03 hosting COMPLETE with documented deployment findings; WP04/WP05 local implementation COMPLETE; WP06 operational preparation IN PROGRESS (fresh encrypted backup and isolated restore verified). Owner confirmed LISH access and accepted recovery defaults. No publication or deployment.

**Owner direction, 2026-11-14:** Use production `api.playpokerattack.com` and pilot `pilot-api.playpokerattack.com`. Use GitHub Actions for future CI/CD, superseding Azure Pipelines delivery below; legacy Azure definitions 39/40/41 are disabled. WP09 must implement equivalent GitHub environment protections, branch controls, credentials and deployment concurrency. Original Azure-specific details are historical until translated in WP09.
**Design:** [Migration design](LINODE_SERVER_MIGRATION_DESIGN_DOCUMENT.md)
**Execution status and evidence:** [Step-by-step checklist](LINODE_MIGRATION_STEP_BY_STEP.md)

These packages divide the agreed migration into reviewable changes and operational runs. Preparing this file does not implement or authorize execution of them. On a later implementation request, use the checklist as the execution record and these packages as task boundaries. Each package has concrete outputs, dependencies, acceptance criteria and a handoff. A package is complete only when its acceptance evidence is recorded; code being ready does not mean deployment happened.

## Fixed decisions

| Item | Implementation requirement |
| --- | --- |
| Host | Existing shared Linode; assume sufficient capacity now. No capacity study, resize, purchase or alternative-host selection prerequisite |
| App allocation | One combined API/SignalR process, **0.25 CPU cores / 0.5 GiB = 512 MiB**; pilot uses the same per-container allocation |
| Compose limits | Service-level `cpus: 0.25`, `mem_limit: 512m`; verify Docker `NanoCpus=250000000`, `Memory=536870912` |
| Database | Existing Linode PostgreSQL, database `expensetrackerdb`, schema `PokerAttack`; keep all other schemas intact |
| Pilot data | Separate disposable database and restricted role, with the same `PokerAttack` schema mappings |
| Networking | Existing Caddy, additive virtual host; proposed app loopback 8081 and pilot 8082, subject to collision check |
| DNS | **Manual owner/operator edits only**, including rollback and retirement. Agent supplies exact record cards and verifies read-only |
| Public hostname | Owner-selected `api.playpokerattack.com`; pilot `pilot-api.playpokerattack.com` |
| CI/client/registry | Owner selected GitHub Actions; retain Azure Static Web Apps and ACR; disable obsolete Azure pipelines |
| Release model | Maintenance stop/start; active matches cannot survive process restart; immutable image/config rollback |

Capacity is an accepted planning assumption, not a claim of measured headroom. Runtime tests still establish that this application works at the specified limits. Report observed failures; do not silently increase the budget or resize infrastructure.

## Package order and status

| Package | Deliverable / suggested change title | Depends on | Checklist coverage | Status |
| --- | --- | --- | --- | --- |
| WP01 | Record verified configuration and manual DNS handoffs | Implementation request | A01–A05 | COMPLETE — inventory/findings recorded; role/credential/firewall remediation remains a deployment gate |
| WP02 | Isolate migration builds and establish a passing test baseline | WP01 | B01; baseline part of B04 | COMPLETE — local baseline and live isolation verified; no publication |
| WP03 | Make API hosting ready for Caddy and production probes | WP02 | Server part of B02; hosting tests in B04 | COMPLETE — 26 isolated hosting tests plus 11 BL tests passed |
| WP04 | Prepare client recovery and isolated multiplayer tests | WP02–WP03 | Client part of B02; B05; client checks in B04 | COMPLETE ? 28 hosting/recovery, 21 guards and 2 artifact checks passed; full pilot smoke remains WP07 |
| WP05 | Add container packaging and controlled release commands | WP03–WP04 | Repository part of B03; remaining B04 | COMPLETE ? both images, scoped disposable runtime, 8 release failure tests and 3 browser smokes passed |
| WP06 | Prepare protected host configuration, backups and pilot database | WP01, WP05 | C01–C04; operational part of B03 | NOT STARTED |
| WP07 | Verify public pilot, rollback and monitoring | WP06; manual M01 | M01, C05, D01–D05 | NOT STARTED |
| WP08 | Perform coordinated production cutover | WP07; manual M02 | M02, E01–E04; R01 if needed | NOT STARTED |
| WP09 | Enable GitHub Actions Linode-only continuous delivery | WP07–WP08 | F02 | NOT STARTED |
| WP10 | Observe production and retire obsolete server resources | WP08; WP09 before retirement | F01, F03–F04 | NOT STARTED |

Manual M01/M02 record preparation belongs to WP01; their execution can happen as soon as names/host IP are verified, before their dependent packages. WP10 observation can run while WP09 is completed. Repository work in WP02–WP05 does not wait for DNS. Shared files such as `Program.cs` and pipeline YAML should be edited in this sequence to keep review and integration straightforward. This breakdown does not request parallel agents.

For every package, record: source SHA, changed files or resource IDs, verification command/results, private evidence location, unresolved items, next package and any required manual handoff. Follow repository rules for commits/PRs. Keep secrets out of package notes.

## WP01 — Configuration inventory and manual DNS handoffs

**Outcome:** a concrete implementation worksheet, without deployment or DNS writes.

**Inputs:** current source, owner-confirmed database topology, current TransitJazz pilot status, selected host and resource budget.

**Tasks:**

1. Complete checklist A01–A03: verify live ACA image/revisions/traffic, served client configuration, pipeline triggers and rollback artifacts. Resolve the protected production connection and verify database `expensetrackerdb`, schema `PokerAttack`, app-role permissions, migration-history ownership and backup arrangements.
2. Complete A04's read-only service/port/access inventory. Reuse the existing host and proxy; do not reopen capacity selection.
3. Resolve production/pilot hostnames and browser origins. Inspect authoritative DNS, current records, CAA and relevant IPv6 state read-only.
4. Produce separate M01/M02 record cards with provider/zone, exact FQDN and relative name, A/AAAA/CNAME choice as appropriate, exact target, TTL, old values, and manual reversal. Identify whether any custom pilot-client DNS is needed; prefer its existing SWA hostname when available.
5. Record the maintenance method, live-game observation method, recovery targets and operator. Distinguish facts confirmed by the owner from live verification results.

**Outputs:** populated checklist worksheet, redacted rollback inventory, two manual DNS record cards and a short list of remaining access/configuration blockers.

**Acceptance:** an implementer can name the app, database, schema, ports, proxy owner, registry, pipeline and rollback targets without guessing. Neither secret exports nor DNS writes occurred.

**Handoff:** WP02 can proceed. Present the completed DNS cards to the owner when the record values are concrete; do not request a generic approval to “set up DNS.”

## WP02 — Pipeline isolation and build/test baseline

**Outcome:** migration preparation can be published without triggering production cleanup, database changes, ACA redeployment or premature client cutover.

**Files to review/change:** `deploy/server-pipeline.yml`, `deploy/e2e-nightly-pipeline.yml`, `deploy/client-pipeline.yml`, `deploy/manual-build-and-deploy-server.ps1`, and `src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ChefKnifeStudios.PokerAttack.Server.BL.Tests.csproj`.

**Tasks:**

1. Apply B01's execution controls before pushing to any triggered branch. Inspect pipeline UI settings and queued runs as well as YAML. Retain build/push and an intentional client publication path.
2. Resolve the test project's `net9.0` references to `net10.0` code with the smallest compatible change. Run existing isolated BL tests and API/client Release builds; record pre-existing failures separately.
3. Keep production migration and destructive E2E stages disabled during preparation. Do not run the existing unguarded Playwright suite against production to establish a baseline.
4. Keep all current live client BaseUri values until WP08. Stage future configuration changes in an artifact/template that cannot automatically publish.

**Acceptance:** controlled build runs cannot alter production; existing tests/builds have a documented passing baseline or explicit blockers. A failed test is not converted into a pass by suppressing its exit code.

**Handoff:** passing baseline SHA, pipeline control evidence and known failures for WP03–WP05. B04 remains unchecked until the new hosting/client/container checks also pass.

## WP03 — API hosting and production boundaries

**Outcome:** the existing API/hub can operate safely behind Caddy with useful deployment health checks.

**Files to review/change:** WebAPI `Program.cs`, `EndpointGroups/TestEndpoints.cs`, `appsettings.json`, `appsettings.Development.json`; ServiceDefaults `Extensions.cs`; a focused hosting extension/health-check class if useful. Proposed new tests live in `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI.Tests/` and must be included in build/test invocation.

**Tasks:**

1. Configure trusted loopback forwarded headers, one hop, and middleware ordering. Add exact-origin configurable CORS and host filtering compatible with public and loopback probes.
2. Add `/health/live` and `/health/ready` in Production. Readiness checks initialization and bounded DB/schema compatibility without executing migrations; return no connection details.
3. Make `/test/cleanup` and `/test/signal-r` fail closed outside explicitly enabled isolated Development/Staging. `Production` must remain protected even when a test flag is misconfigured. Document exact environment names.
4. Set shutdown timeout and expose only a local/operator mechanism for observing active games if current operations lack one. Preserve hub route, query-string player IDs, JSON messages and game behavior.
5. Define any new configuration keys in an environment matrix for WP05; do not add new production credentials to source.

**Validation:** focused integration tests for allowed/denied CORS, trusted/untrusted forwarded headers, host filtering, production test-route absence, readiness failure/recovery and safe health responses; existing BL tests and API Release build. Use disposable PostgreSQL for integration tests.

**Acceptance:** normal gameplay contract preserved, production behavior explicit, failing DB readiness does not trigger a schema change, and probes work with the intended proxy/host configuration.

**Handoff:** tested hosting SHA, configuration matrix and local probe contract. No live deployment occurs in this package.

## WP04 — Client recovery and isolated E2E tests

**Outcome:** users can recover from expected maintenance disconnects, and automated gameplay tests cannot clean production data.

**Files to review/change:** `src/Client/ChefKnifeStudios.PokerAttack.Client.Core/Services/SignalRNotificationService.cs`, relevant WebApp pages/layout (`Layout/MainLayout.razor` if appropriate), client configuration/artifact selection, `tests/helpers/setup.ts`, `tests/playwright.config.ts`, `tests/e2e/multiplayer-game.spec.ts`, and E2E pipeline stages.

**Tasks:**

1. Show an actionable disconnected/session-ended state after retry exhaustion or lost game state. Rejoin only a verified surviving session; otherwise offer reload/new-lobby recovery. Do not imply that reconnect restores in-memory hands or rounds.
2. Prepare an isolated pilot client with both `PokerAttackAPI` and `PokerAttackSignalR` pointing at the pilot backend. Keep production artifacts on ACA until cutover.
3. Require explicit `CLIENT_URL` and cleanup API target for CI, reconcile existing `API_URL` usage, and reject known production cleanup targets. Remove guessed fallback targets in CI.
4. Make staging tests blocking. Permit certificate bypass only for explicit local development; pilot HTTPS uses normal validation.
5. Prepare a maintenance entry artifact and restoration instructions for WP08 without publishing it.

**Validation:** client Release publish; isolated multiplayer/solo smoke when the local fixture is available; fixture/target guard checks; disconnect and fresh-session behavior. Public-proxy and full pilot tests are executed in WP07.

**Acceptance:** browser calls and cleanup consistently target isolated services; a deliberate bad target fails before cleanup; stale sessions do not silently remain playable. Served client environment handling, including `prod`, is documented.

**Handoff:** pilot client artifact recipe, maintenance artifact recipe, test invocation and expected browser recovery behavior.

## WP05 — Container packaging and repeatable release commands

**Outcome:** reviewed repository files ready for installation on Linode; no installation occurs in this package.

**Planned files:**

- `deploy/linode/compose.yaml`: app-only production service using host networking and immutable image.
- `deploy/linode/compose.pilot.yaml` or an equally explicit isolated override: distinct project, port and runtime env path; same resource limits.
- `deploy/linode/release.env.example`, `app.env.example`, `Caddyfile.example`, `README.md`, `.gitignore` and LF rules: placeholders only, manual DNS instructions and shared-proxy ownership.
- `deploy/linode/pokerattack-deploy` and `pokerattack-ssh-deploy`: controlled release/SSH commands.
- API Dockerfile/build context/publish exclusions as necessary; verify the existing Data Dockerfile's bundle separately.

**Tasks:**

1. Encode **`cpus: 0.25` and `mem_limit: 512m`** per app container. Use loopback bind, non-root app execution, bounded logs, restart policy and shutdown grace. Keep PostgreSQL and the existing proxy outside the app deployment lifecycle.
2. Keep production `/etc/pokerattack` runtime files and pilot files isolated and root-only; deliver app credentials outside image layers. Remove embedded development credentials from build inputs/output and prepare a consumer-aware rotation procedure for WP06.
3. Provide an additive Caddy site example, preserving `/cks-notification`, negotiation and query strings, with production test routes blocked. No DNS automation and no second proxy competing for 80/443.
4. Implement digest validation, fixed repository, exclusive host lock, pre-pull, maintenance precondition, matching image/config snapshots, bounded startup, failed-release recovery and nonzero failure exit. Choose a concrete restricted interface for post-release verification: either an on-host smoke validator before promotion, or an explicit validated promotion operation after external checks. Never promote merely because local readiness is green.
5. Keep migrations as a separate explicit operation. Do not modify shared EF history to simplify release code. Document manual recovery after interrupted execution.
6. Build both images off-host, run local container checks against disposable data, inspect publish output for accidental development secrets, and verify release script syntax/isolated failure paths. Complete B04 after all package tests pass.

**Acceptance:** Compose resolves with placeholders replaced and `config --quiet` passes; Docker enforces the exact CPU/memory limits; production image runs without embedded credentials; failed pull leaves the old app running; no script can stop PostgreSQL/shared Caddy or mutate DNS. Live installation/reboot/rollback evidence remains WP07 work.

**Handoff:** tested SHA, built artifacts, configuration matrix, script validation results, secret provisioning instructions and installation commands. WP06 publishes/resolves the registry digest from this tested release.

## WP06 — Backups, host preparation and pilot database

**Outcome:** recoverable existing host with immutable release available and isolated pilot configuration ready.

**Operational scope:** C01–C04, credential handling from B03; no production cutover or DNS edits.

**Tasks:**

1. Obtain and restore-test consistent encrypted off-host backups of shared `expensetrackerdb`, roles/configuration and all databases affected by host work. Verify PokerAttack and other schemas. Preserve shared proxy configuration/certificate storage and current runtime definitions.
2. Verify/reuse the host's Docker/Compose, SSH, console recovery and startup services. Apply only required compatibility preparation; no resize or new VM.
3. Publish the tested release off-host, resolve its digest and establish pull-only ACR access. Create the protected PokerAttack deployment/config directories with correct ownership.
4. Create an isolated pilot DB/role and rehearse migrations there. Check production schema compatibility read-only, including EF-history ownership. Record whether any production schema change is actually necessary.
5. Deliver/rotate credentials using the documented consumer-aware process; verify affected applications remain functional. Populate pilot env and stage production env separately without starting a production process.

**Acceptance:** backups restored in isolation, required host services unchanged in function, digest provenance established, pilot role isolated from production, production connection preserved for ACA rollback, no secrets in evidence.

**Handoff:** protected host paths, digest, backup/restore report, DB compatibility decision and completed M01 record card. DNS completion is handled by the owner/operator in WP07, not by these commands.

## WP07 — Manual pilot DNS, public pilot and recovery proof

**Outcome:** a verified Linode pilot with genuine multiplayer delivery and tested recovery at the specified allocation.

**Manual prerequisite:** owner/operator completes checklist M01. Agent verifies authoritative/public resolution read-only. If DNS is pending, local container/script checks may proceed; public HTTPS/hub acceptance remains pending.

**Tasks:**

1. Install validated app/proxy definitions; add the pilot virtual host to existing Caddy during a coordinated reload, preserving other sites/certificates. Verify existing TransitJazz connections after reload. Restrict API/test exposure as designed.
2. Start the pilot on isolated data; publish isolated client. Complete D01/D02 through valid HTTPS, including CORS negative cases, real negotiated WebSocket notifications, private-player delivery, full multiplayer/solo gameplay and persistent round records.
3. Verify exact resource settings and perform D03's 24-hour runtime soak. Observe behavior at 0.25 CPU / 512 MiB without introducing a host-sizing gate.
4. Install and exercise the release command. Rehearse failed pull/start/health/public delivery, malformed input, lock contention, rollback failure and interrupted execution. Rehearse ACA/client rollback against isolated targets. Perform coordinated reboot/recovery checks after backups.
5. Configure recurring off-host backup/retention, external availability/certificate/resource/error monitoring and verify alert receipt and scheduled backup recovery.

**Acceptance:** all C05/D01–D05 evidence recorded; no production-wide cleanup; correct image and limits; no silent known-good promotion before public gameplay proof; host and shared database recovery demonstrated.

**Handoff:** verified pilot digest, protected known-good pilot configuration, observed timings, monitoring/backup evidence and a production cutover readiness report. Pilot credentials are not a valid production rollback configuration.

## WP08 — Manual production DNS and maintenance cutover

**Outcome:** production client, API and hub consistently use the single Linode game process.

**Manual prerequisite:** owner/operator completes M02 with its exact record card. If done earlier, reuse verified completion. Agent verifies resolution and TLS before any ACA shutdown. Production clients remain on ACA until the coordinated release.

**Tasks:**

1. Complete E01: refresh rollback artifacts, backup age, production schema compatibility, production env, Caddy site/certificate, both client URLs, maintenance entry and exact ACA revision/traffic recovery commands.
2. Complete E02: publish maintenance entry, observe/drain or explicitly terminate sessions, deactivate all active ACA production revisions and verify their replicas stop. Stop pilot. Run only previously reviewed compatible migrations, if required, under a release lock.
3. Complete E03: start one production API/hub at the verified digest and **0.25 CPU / 512 MiB**, using `expensetrackerdb` / `PokerAttack`. Check DB identity, health and controlled gameplay/persistence without cleanup.
4. Complete E04: publish both client BaseUri values together, restore entry, inspect effective served config and actual API/WebSocket destinations, and verify stale-tab reload plus multiplayer/solo smoke.
5. If release gates fail, execute R01: stop Linode production first, restore ACA revision/traffic and both saved client URLs, verify DB/gameplay and retain failure evidence. Do not overwrite the shared database. No automated DNS change is part of rollback.

**Acceptance:** one authoritative production process, valid HTTPS/WSS, expected DB writes, both live client URLs correct, tested runtime limits and UTC cutover recorded. No claim that pre-cutover active matches survived.

**Handoff:** production known-good image/config pair after public verification, rollback package retained, observation start/end dates and current deployment state.

## WP09 — Linode-only GitHub Actions delivery

**Outcome:** subsequent releases follow the tested deployment path and cannot reactivate ACA.

**Files/resources:** server/nightly/manual deployment definitions, Azure Pipelines environment `PokerAttack-Linode-Prod`, restricted SSH identity/host fingerprint, branch controls, secrets/secure files and sequential deployment lock.

**Tasks:**

1. Connect existing test/build/push to isolated staging with blocking E2E, then restricted SSH invoking the fixed-repository digest command. Retain the maintenance gate and public HTTP/SignalR validation before promotion.
2. Configure least-privilege `pa-deploy` key, forced command and sudo permissions; no Docker-group/general shell privilege through CI. Verify runner network reachability without a general build runner on the DB host.
3. Remove unintended ACA deployment, unconditional production migrations and destructive production-test paths. Keep DNS entirely manual.
4. Exercise one coordinated CI release plus invalid-command, concurrent-release and failure/rollback cases. Check failure status and image/config consistency.

**Acceptance:** F02 evidence demonstrates exact digest deployment, fixed limits, production readiness and game delivery, serialized changes and failed-release recovery. If manual CD is deliberately selected, record F02's documented alternative with build/staging gates and proven manual commands.

**Handoff:** enabled/disabled pipeline inventory, credential references, release operator procedure and successful CI/manual release evidence for WP10.

## WP10 — Observation, retirement and operational handover

**Outcome:** sustained production service, recoverable operations and removal only of obsolete server resources.

**Tasks:**

1. Begin F01 observation after WP08, retain ACA rollback artifacts/access for 7–14 days, and verify normal gameplay/persistence, resource behavior, alerts and recurring backups. Maintain the specified app limits.
2. After observation and WP09, inventory exact obsolete ACA resources and their dependencies. Complete F03 retirement within authorized scope; preserve ACR, Static Web Apps, shared Linode/PostgreSQL and other applications.
3. Remove pilot services through their own scoped procedure. Prepare any obsolete pilot DNS removal as a manual record card for the owner; the agent does not delete DNS records.
4. Complete F04: update design/checklist and older staging/test documentation with actual topology, safe release/rollback, DB recovery, operator, monitoring, backup retention and measured costs.

**Acceptance:** observation evidence complete, all applicable checklist steps verified, no remaining obsolete deployment triggers, current restore proof and operator procedures available. Retiring Azure is not a prerequisite for beginning the observation period.

**Final handoff:** concise migration completion report, final source/image/config versions, production URLs, recovery instructions and any separately scoped follow-up work.

## Next implementation task

Start with **WP01**, then **WP02** before publishing changes to triggered branches. The first coding change should establish pipeline/test isolation and the passing baseline; the next should add the API hosting contract. Manual DNS need not delay those repository changes. Package statuses above now reflect the implementation run; later packages remain NOT STARTED until their dependencies and verification are satisfied.
