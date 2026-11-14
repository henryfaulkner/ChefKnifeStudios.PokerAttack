# PokerAttack Linode migration: agent execution checklist

**Prepared:** 2026-11-14
**Status:** IN PROGRESS — WP01 inventory, WP02 isolation/baseline and WP03 hosting verified; WP04/WP05 local implementation verified; WP06 backup/host verification recorded; registry/role preparation underway. Owner confirmed LISH and recovery defaults. No publication or deployment.
**Design:** [Migration design document](LINODE_SERVER_MIGRATION_DESIGN_DOCUMENT.md)
**Implementation breakdown:** [Work packages](LINODE_IMPLEMENTATION_WORK_PACKAGES.md)

## Execution contract

This checklist is for a later implementation run. The request that produced it authorizes planning only. Do not execute it until implementation is requested. Once authorized, proceed through dependencies without asking for repeated permission for already-authorized work. Resolve missing material decisions only when they affect the next action.

Checkboxes mean **verified completion**, not “code written” or “command attempted.” After each step, record UTC timestamp, tested commit/digest, redacted evidence location, result and any deviation in the ledger. Leave failed/blocked steps unchecked. If a step is genuinely inapplicable, record the reason and evidence in the ledger before checking it. Never copy a completed TransitJazz checkbox as PokerAttack evidence.

Resume by inspecting the ledger, current source, live image/config and incomplete dependencies. Revalidate time-sensitive facts such as DNS, ACA activity and backup age before mutation. Use the existing shared Linode with capacity assumed sufficient, as instructed by the owner. The API/SignalR container limit is fixed at 0.25 CPU and 512 MiB; no capacity survey, resize or new VM selection is required. Stop dependent work on observed failures; independent preparation may continue. Do not paste this entire document into a shell.

**Execution update (2026-11-14):** WP01 inventory and independent B01 safety controls have started. Owner selected GitHub Actions delivery, superseding Azure Pipelines for the future deployment path. Legacy Azure definitions 39/40/41 are disabled. See [WP01 evidence](LINODE_WP01_EVIDENCE.md) and [WP02 evidence](LINODE_WP02_EVIDENCE.md). WP01 inventory acceptance is now complete with deployment findings carried to WP06; owner LISH/defaults confirmation is recorded below. No publication or deployment has occurred.

**Manual DNS boundary:** M01 and M02 are owner/operator steps. The agent prepares exact records and rollback instructions and verifies resolution read-only; it does not create, edit or delete DNS records through any tool. Record the operator's completion before checking these items. Wait for manual completion only when the next action depends on DNS; continue independent repository preparation meanwhile.

Commands labeled **Workstation / PowerShell** run from `C:\Projects\ChefKnifeStudios.PokerAttack`. **Linode / Bash** commands run over verified SSH. Examples with `REPLACE_...` are templates and must not run until populated. Check command exit codes. Never place secrets in command arguments, Git, screenshots or evidence. Avoid unfiltered environment dumps, `docker inspect`, `docker compose config`, or Azure resource exports; use narrow queries and `docker compose config --quiet`.

Do not run `docker compose down -v`, `docker system prune --volumes`, database rebuilds, or global Docker restarts as application deployment steps. Never alter TransitJazz's app files, database deployment, or secrets to make a PokerAttack deployment work. Proxy changes on a shared host require their own co-tenant checks.

## Run worksheet

Complete before the corresponding steps. Non-secret values only; secret entries identify the protected storage location, never the secret value.

| Field | Selected value |
| --- | --- |
| Run ID / operator / UTC start | `20261114-wp01` / implementing agent with Henry Faulkner / approximately 2026-11-14 14:00 UTC |
| Implementation branch / tested source SHA | `migration/linode-server-hosting` / base `b6a4d17d8eb42e9f9bee8214021406ff8be22659` plus local edits; see WP02 evidence |
| Azure subscription / pipeline project | `ChefKnifeStudios` (`d2f70759-7eeb-4ca5-9046-f7c8d562ef8f`); `henryfaulkner` / `ChefKnifeStudios.DevOps`; GitHub Actions selected for replacement |
| ACA app / resource group | Verified `poker-attack-api` / `poker-attack-rg` |
| ACA old FQDN / revision / traffic / digest | `poker-attack-api.ambitioussmoke-21ab3755.eastus.azurecontainerapps.io` / `poker-attack-api--0000043` / latest 100% / `sha256:408302ba8113d37039de40de2c01de0fca23613492df56a124d6e8575356321e` |
| Saved client artifact / effective environment / old API and hub URLs | Run 1268 owner-provided ZIP saved privately, SHA256 `1ea9a7e927590b0d23eb9e0c5000823466d7c5232794b4200b431c252fc1e945` / served `prod` / both `https://poker-attack-api.ambitioussmoke-21ab3755.eastus.azurecontainerapps.io/` |
| Linode ID / label / region / architecture / plan | VM cloud-init metadata: ID `79395147`, region `us-southeast`, platform Akamai; reference label `expense-tracker-vm`; verified `170.187.142.115`, x86-64, Ubuntu 24.04.2; metrics show 1 CPU / 1.92 GiB RAM, provider billing plan unverified |
| Placement / capacity assumption | Existing shared Linode; owner instructs assuming sufficient capacity; no resize planned |
| Existing proxy owner / config / certificate volume / occupied ports | TransitJazz Compose; `/opt/transitjazz/Caddyfile`; `transitjazz-app_caddy_data` and `_config`; public 22/80/443/5432, loopback 8080/2019/12345/53 |
| SSH account / fingerprint / recovery console / CI network route | Verified root key SSH; ED25519 `SHA256:bV3vsdj3SnjD64ATPEpUNxjoXPrr0eQTut0R1rgDRu4`; owner-confirmed LISH `ssh -t henryfaulkner-cks@lish-us-southeast.linode.com expense-tracker-vm`; restricted CI route remains WP09 |
| Production API hostname / authoritative DNS zone / TTL | Owner-selected `api.playpokerattack.com` / Name.com `playpokerattack.com` / 300 seconds; M02 manually completed and DNS verified 2026-11-14 |
| Pilot API hostname / pilot client URL / pilot origins | `pilot-api.playpokerattack.com`; separate pilot client build initially served at `http://localhost:5173`; M01 manually completed and DNS verified 2026-11-14 |
| Approved production browser origins | Verified SWA custom domains apex/www `playpokerattack.com`; final origin allowlist awaits served checks of remaining sites |
| Production / pilot API loopback ports | `127.0.0.1:8081` / `127.0.0.1:8082`, both unused at inventory |
| Production DB host / database / schema | Verified `170.187.142.115` / `expensetrackerdb` / `PokerAttack`; host-loopback authenticated read-only query passed |
| Production DB role / TLS / schema version / EF history location | Current role `postgres` (superuser: isolation unresolved); authenticated workstation and loopback TLS 1.3 observed, certificate-verification policy not established; `public.__EFMigrationsHistory` shared with ExpenseTracker; see skill evidence |
| Pilot DB / role / connection secret location | Proposed `pokerattack_pilot` / `pokerattack_pilot_app`, not provisioned; protected secret path pending |
| Production runtime / migration secret locations | Current ACA direct DB/storage environment settings; future root-only runtime paths pending WP06; no values recorded |
| Backup location / retention / last restore / durable-data RPO and RTO | Existing `/var/backups/transitjazz-migration-20261113`; owner accepts 7-day retention, DB RPO 24h / recovery 4h and app rollback 15m defaults; PokerAttack backup/recovery verification remains WP06/WP07 |
| API image digest / migration image digest if needed | PENDING |
| App memory/CPU | Owner-selected 512 MiB (0.5 GiB) / 0.25 CPU, combined API + hub; same limits for pilot |
| Pilot load target / runtime observations | Proposed 20 clients / two multiplayer games; results pending; no host-headroom gate |
| Maintenance window / active-game observation / reset message | Owner permits maintenance anytime and confirms LISH; WP03 loopback `/_ops/status` counts support observation; explicitly reset/reload remaining sessions before stop/start; matches do not survive process restart |
| External monitoring destination / operator | Existing Grafana Linux overview verified: `gallantpuffin3113.grafana.net/d/nodes`, instance `expense-tracker-vm`; external app alert delivery/destination and operator decisions remain pending |
| Observation end date / retirement decision | PENDING |

## Phase A — inventory and decisions

### A01 — Record source and local instructions

- [x] A01 complete. Evidence: [WP01 source/access inventory](LINODE_WP01_EVIDENCE.md).

**Actions:** Read current repository/ancestor instructions. Run `git status --short` and `git rev-parse HEAD`; preserve unrelated edits. Re-read the design and TransitJazz PR/status at its current head and record changes from reviewed SHA `6dfef670c72232af992416c09816cdbc0cdf86a8`. Create an implementation branch if needed under repository rules. Do not assume TransitJazz has completed production cutover since the dated report.

**Pass/evidence:** worksheet identifies the run, source revision, applicable instructions and source-of-truth reference; no unrelated changes lost. **Failure:** reconcile unexpected worktree changes before editing affected files. **Depends on:** implementation request.

### A02 — Inventory Azure, clients, pipelines and rollback target

- [x] A02 inventory complete. Current resource/revision/traffic/digest, pipeline controls and client rollback ZIP/source verified; recovery rehearsal remains WP07. See [WP01 evidence](LINODE_WP01_EVIDENCE.md).

**Actions:** Confirm the correct subscription and actual resources. Capture non-secret server allocation, scaling, all active revisions, ingress CORS/custom domains, traffic rules, readiness, secret names/references and required setting names. Save current server digest and a deployable copy of the existing client artifact/configuration privately. Identify pipeline IDs, UI triggers, variable groups, manual deploy scripts and any queued jobs. Fetch the live client settings and headers to establish its actual environment and both backend URLs.

**Workstation / PowerShell — read-only examples:**

```powershell
az account show --query '{name:name,id:id}' -o json
az containerapp show -g poker-attack-rg -n poker-attack-api --query '{ready:properties.latestReadyRevisionName,image:properties.template.containers[0].image,fqdn:properties.configuration.ingress.fqdn,scale:properties.template.scale,resources:properties.template.containers[0].resources,traffic:properties.configuration.ingress.traffic}' -o json
az containerapp revision list -g poker-attack-rg -n poker-attack-api --query '[].{name:name,active:properties.active}' -o json
```

**Pass/evidence:** current live state is distinguished from repository intent; old API/hub URLs, image, revision traffic and client artifact can be restored. **Failure:** correct resource/subscription selection; do not substitute development config. **Depends on:** A01.

### A03 — Identify the actual database and protect its ownership boundary

- [x] A03 inventory complete with a failed privilege finding: database identity, TLS, schema/history, backup references and consumers verified. Existing `postgres` access does not isolate PokerAttack. Runtime/pilot roles and consumer-aware credential rotation must be fixed in C04/WP06 before deployment; they are not claimed complete here.

**Actions:** Use the owner's confirmed topology: database `expensetrackerdb`, schema `PokerAttack`, on the existing shared Linode. Resolve the effective production connection through its existing protected secret store, without printing it; verify it matches that topology. Identify app role, version, extensions, EF history location/ownership, backup process and other consumers of this shared database. Use an authenticated harmless query such as `SELECT current_database(), current_user;` and read `__EFMigrationsHistory` in its actual schema. Do not assume migration history belongs to `PokerAttack` solely because entity tables do. Confirm existing game/round tables and representative counts, plus permissions that prevent writes to unrelated schemas. Identify a separate disposable pilot database and restricted role; reproduce the `PokerAttack` schema there, never use production `expensetrackerdb` for destructive pilot tests. Inventory credential-bearing development settings for rotation/removal in B03.

**Pass/evidence:** worksheet has verified DB identity and ownership, access route, schema level, pilot isolation plan, and rollback connectivity requirements. **Failure:** block deployments and migrations until identity is certain. **Depends on:** A02.

### A04 — Inventory the existing Linode services and ports

- [x] A04 inventory complete. Existing host/SSH/services/ports verified; owner explicitly confirmed LISH access using `ssh -t henryfaulkner-cks@lish-us-southeast.linode.com expense-tracker-vm`. Owner believes no Cloud Firewall is attached; that remains unverified and must be checked before public host changes.

**Actions:** Use the existing shared Linode and assume sufficient capacity. Inspect its PostgreSQL/proxy service ownership, installed Docker/Compose versions, open ports, mounts, disk and startup procedures read-only. Establish operator console access. Record a lightweight resource snapshot for later troubleshooting; do not delay preparation for a capacity study, alternative-host comparison or quote. Reserve unused loopback ports and record the fixed API/hub budget of 0.25 CPU / 512 MiB.

**Linode / Bash — read-only examples:**

```bash
uname -m
cat /etc/os-release
free -h
df -h
sudo ss -lntp
sudo docker ps --format 'table {{.Names}}\t{{.Image}}\t{{.Ports}}'
sudo docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}'
sudo docker compose version
```

**Pass/evidence:** existing host identified, free loopback ports and proxy ownership recorded, required access available, application limits documented. **Failure:** resolve access/service/port conflicts; report concrete runtime issues without changing the owner's placement or resource limits. **Depends on:** A03.

### A05 — Resolve DNS, maintenance and recovery decisions

- [x] A05 decisions recorded. Manual DNS verified; maintenance anytime; owner accepts app rollback 15 minutes, DB RPO 24 hours / recovery 4 hours and backup retention 7 days as defaults, with a proportionate hobby-project approach. Operator Henry Faulkner. Pilot client initially uses a separate local build at `http://localhost:5173`; WP04 provides its recipe. Use loopback session counts to observe/drain games, with explicit reset/reload if restart is needed. External alert delivery and restore proof remain operational verification, not claims made by this inventory.

**Actions:** Determine authoritative nameservers for the selected hostname and inspect existing A/AAAA/CNAME/CAA records read-only. Select an unused production hostname and distinct pilot hostname/client; record TTL and approved origins. Prepare exact manual DNS handoffs for M01/M02; do not save DNS changes. Decide maintenance time, how active games are observed and drained/reset, client reload messaging, rollback budget, durable-data RPO/RTO, backup retention and monitoring owner. Use the design's proposed defaults where suitable and record deviations.

**Workstation / PowerShell — discovery examples:**

```powershell
Resolve-DnsName henryfaulkner.xyz -Type NS
Resolve-DnsName pokerattack-api.henryfaulkner.xyz -Type A
Resolve-DnsName pokerattack-api.henryfaulkner.xyz -Type AAAA
```

**Pass/evidence:** worksheet values needed for implementation are concrete; domain authority and ownership verified. **Failure:** choose a controlled domain/host or leave dependent work blocked. **Depends on:** A02–A04.

## Phase B — prepare and verify code without production deployment

### B01 — Prevent deployment races and destructive production tests

- [x] B01 safety controls verified. Azure definitions 39/40/41 disabled; local obsolete deploy/cleanup paths removed; queues empty at initial check. WP01/A02 rollback artifact gate remains open. See [WP02 evidence](LINODE_WP02_EVIDENCE.md).

**Actions:** Before publishing migration changes to triggered branches, disable ACA deployment and automatic production DB migrations while preserving build/push capability. Disable or retarget both production E2E paths in `server-pipeline.yml` and `e2e-nightly-pipeline.yml`; cancel queued destructive/deploy jobs. Audit `manual-build-and-deploy-server.ps1` and pipeline UI triggers. Coordinate client `main`/`deploy` triggers so URL changes cannot publish prematurely. Keep a deliberate client release/rollback path available.

**Pass/evidence:** a build cannot change production infrastructure, schema or client URLs, and no scheduled/branch job can call production cleanup. Record changed pipeline IDs and the temporary control. **Failure:** pause the relevant pipeline until its behavior is understood. **Depends on:** A02; must precede any push that could trigger those jobs.

### B02 — Add the hosting and recovery contract

- [x] B02 local implementation complete. See [WP03 hosting evidence](LINODE_WP03_EVIDENCE.md) and [WP04 recovery evidence](LINODE_WP04_EVIDENCE.md): 28 hosting/recovery tests pass. Rendered/full-game acceptance remains WP07.

**Actions:** Implement the design's Program/service-default changes: explicit production health routes, bounded DB/schema readiness, exact-origin configured CORS, trusted forwarded headers, host allowlist, shutdown budget, and test-route gating that fails closed. Use actual `/test/signal-r` spelling. Preserve hub path, JSON protocol and player-ID query behavior. Implement the minimal disconnected-session UI/recovery path and operator-only active-game observation if no suitable mechanism exists. Ensure test routes remain absent even if a production enable flag is mistakenly set.

**Pass/evidence:** source diff maps to the design; production/test environments have explicit behavior; failed/restarted sessions cannot silently appear playable. **Failure:** resolve hosting/recovery behavior before packaging. **Depends on:** A03–A05.

### B03 — Prepare container/runtime configuration and credential handling

- [ ] B03 complete.

**Actions:** Add future `deploy/linode` files with separate project/service names, app-only deployment, host loopback binding, immutable-image variables, service-level `cpus: 0.25` and `mem_limit: 512m`, bounded logs, restart/shutdown policy and root-owned secret-file paths. Use the same limits for pilot and production. For the shared proxy, provide an additive site snippet and ownership instructions rather than another proxy service. Keep pilot DB/config/port separate. Verify non-root execution and runtime writable paths. Remove credential-bearing development values from source/build context/published artifacts and coordinate rotation of affected DB/storage credentials across actual consumers; do not change shared credentials blindly. Record only secret-store references.

**Pass/evidence:** no runtime secrets in tracked deployment files or image layers; production bind/limits are explicit; no DB service or volumes managed by app Compose; rotation results tracked without secret values. **Failure:** do not publish a credential-bearing image. **Depends on:** A03–A05, B01–B02.

### B04 — Establish a passing build and focused tests

- [x] B04 local build/test evidence complete. Both linux/amd64 images built, bundle migrated disposable PostgreSQL, scoped runtime readiness and 0.25 CPU/512 MiB limits verified; 28 C# hosting/recovery, 11 BL, 21 guard, 2 artifact, 8 release failure and 3 actual browser smoke tests passed across recorded runs. See [WP05 evidence](LINODE_WP05_EVIDENCE.md). Credential rotation and live host protection remain C01?C04.

**Actions:** Resolve the BL test project's `net9.0` versus referenced `net10.0` mismatch with a scoped compatible change. Build API/client Release outputs and run existing BL tests. Add focused hosting integration tests for readiness failure/recovery, production test-route absence, CORS allow/deny, trusted/untrusted forwarded headers and shutdown behavior as needed. Verify both API and EF-bundle images build; run the bundle only against a disposable database if needed. Do not treat a failed/skipped suite as passing.

**Workstation / PowerShell — after implementation:**

```powershell
dotnet build src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/ChefKnifeStudios.PokerAttack.Server.WebAPI.csproj -c Release
dotnet test src/Server/ChefKnifeStudios.PokerAttack.Server.BL.Tests/ChefKnifeStudios.PokerAttack.Server.BL.Tests.csproj -c Release
dotnet publish src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/ChefKnifeStudios.PokerAttack.Client.WebApp.csproj -c Release
docker compose build
```

Run the new hosting test project explicitly too, using its actual path. Keep configuration sources pointed at disposable services.

**Pass/evidence:** tested SHA, commands, pass/fail/skips, build output and image platform recorded. Migration bundle tested if part of selected release; document any remaining blocker rather than silently omitting its build. **Failure:** fix/retest affected checks; production remains on ACA. **Depends on:** B02–B03.

### B05 — Make E2E tests explicitly isolated and blocking

- [ ] B05 local guards/artifact/smoke prepared and passing; final pilot endpoint/network and blocking release-stage integration remain WP06/WP07/WP09.

**Actions:** Prepare pilot client output whose `PokerAttackAPI` and `PokerAttackSignalR` both target the pilot API. Set `CLIENT_URL` to that client and `API_URL_CLEANUP` to the isolated API; reconcile Playwright's existing `API_URL` baseURL usage. Add guards that reject known production targets and prevent fallback URL guessing for cleanup. Enable test endpoints only on isolated staging, restricted to the test network/runner. Remove `continueOnError`/nonfailing test-result settings for the staging release gate. Restrict TLS bypass to explicit local development; public pilot/release validation must verify certificates. Update obsolete staging/test instructions when implementing.

**Pass/evidence:** tests cannot reset production; browser network traffic and cleanup share the same isolated environment; a failing test blocks release. **Failure:** do not run the suite against live sites. **Depends on:** B01–B04.

## Phase C — protect and prepare the host

### C01 — Back up and prove restore before host changes

- [x] C01 fresh migration recovery snapshot verified: eight encrypted/checksummed off-host files, all four databases/roles/ownership/ACLs restored in isolation, 41 tables and 3,953,618 rows match dump streams; 39.6 seconds. [Evidence and extension/profile limitations](LINODE_WP06_EVIDENCE.md). Recurring off-host automation remains pending.

**Actions:** Create a consistent whole-database backup of `expensetrackerdb`, including `PokerAttack` and all other application schemas, and back up every other database affected by shared-host maintenance, plus roles/globals, extensions/configuration, proxy certificate/config storage and deployment/runtime files. A PokerAttack-only schema dump is supplementary, not sufficient for shared-database recovery. Transfer encrypted copies off-host with checksums. Restore databases into an isolated matching PostgreSQL instance and verify both PokerAttack and unrelated application schemas, representative rows and permissions/role recovery procedure. Protect globals as secrets. Establish recoverable access to encryption keys and record measured restore duration. Never restore into the live DB as a test.

**Pass/evidence:** dated off-host backup, checksum agreement and actual restore results, with RPO/RTO comparison. Archive listing alone fails this gate. **Failure:** do not install/upgrade/resize/reboot the database host. **Depends on:** A03–A05.

### C02 — Verify and prepare the existing host safely

- [x] C02 compatible existing host verified; no upgrade/resize needed. Docker/PostgreSQL/time sync active/enabled, ports free, co-tenants healthy. Provider firewall attachment unverified; preserve networking and verify existing-port access at pilot. [Evidence](LINODE_WP06_EVIDENCE.md).

**Actions:** Reuse the existing host, Docker/Compose and proxy installations. No provisioning or resize is planned. Verify key-based SSH, console recovery, firewall attachment, time sync and startup services. Use Compose 2.30+ if `env_file.format: raw` is chosen. If a required component is missing/incompatible, prepare the smallest installation/upgrade and coordinate any service interruption after C01; preserve all existing volumes, IPs and startup procedures. Never rebuild the database VM.

**Pass/evidence:** selected host/plan verified; PostgreSQL/TransitJazz and other consumers pass their pre/post checks; ports selected in worksheet remain free. **Failure:** recover affected existing services before PokerAttack work. **Depends on:** C01, A04–A05.

### C03 — Publish a tested release and establish least-privilege access

- [ ] C03 complete.

**Actions:** Publish the B04 tested commit via build-only CI or controlled workstation build; do not compile on the database VM. Record build tag, SHA and registry digest; reserve old rollback image. Create a VM pull-only ACR identity scoped to the PokerAttack API repository and migration repository only if needed. Deliver registry/runtime secrets through protected files or stdin. Create `/opt/pokerattack` and mode-0700 `/etc/pokerattack`, with root-owned config. Define pilot equivalents separately. Do not enable CI deployment yet.

**Workstation / PowerShell — digest lookup:**

```powershell
$pokerReleaseTag = 'REPLACE_WITH_TESTED_BUILD_TAG'
az acr repository show -n chefknife --image "chefknifestudios.pokerattack.server.webapi:$pokerReleaseTag" --query digest -o tsv
```

**Pass/evidence:** immutable digest maps to tested SHA; VM can pull it; token cannot push or access unrelated repositories; secret ownership/modes verified. **Failure:** leave old service running and correct access/provenance. **Depends on:** B04, C02.

### C04 — Create the isolated pilot database and verify production compatibility

- [ ] C04 complete.

**Actions:** Create a separate disposable pilot DB and scoped role on the selected safe test DB instance, preserving the `PokerAttack` schema mappings; apply/rehearse the release's migrations there with separate schema credentials. Verify game/round tables, app-role read/write permissions and isolation from production `expensetrackerdb`. Confirm production schema compatibility read-only; compare EF migration history and selected application requirements. Review migration-history ownership in the shared database before selecting any production bundle execution. If production migrations are needed, verify SQL touches only intended PokerAttack objects and approved history entries, review backup and backward compatibility before execution at E02, and separate incompatible schema work into its own release. Do not move history, drop/recreate the shared database or alter unrelated schemas. Verify remote TLS or approved local connection behavior.

**Pass/evidence:** pilot app role cannot write production data; pilot schema supports the tested release; production migration decision is explicit, including “none required” when proven. **Failure:** do not start a pilot with production credentials. **Depends on:** A03, B04, C01–C03.

### M01 — MANUAL: owner/operator publishes pilot DNS

- [x] M01 DNS complete. Owner reported “done”; verified 2026-11-14 at approximately 14:27 UTC against authoritative ns1cwy.name.com and public resolver 1.1.1.1: pilot-api.playpokerattack.com A = 170.187.142.115, TTL 300. Authoritative AAAA/CNAME absent. HTTPS remains C05 work.

**Agent preparation:** Supply a completed DNS record card using the template below, based on A05 and the verified host IP. Identify authoritative provider/zone, pilot FQDN and provider-relative name, type, exact value, TTL (proposed 300 seconds if supported), current record and exact reversal. Prefer an A record to the existing Linode IPv4; include AAAA only when the IPv6 path is ready. Inspect CAA and conflicting CNAME/AAAA records; identify any required changes explicitly. Include a pilot-client record only if a custom client domain is selected; an existing SWA hostname needs none.

**Manual action:** The owner/operator signs into the authoritative provider, applies only the supplied records, preserves other records/nameservers, and reports completion and time. The agent must not perform this write.

**Verification:** After manual completion, query authoritative DNS and public resolution read-only for every changed record; confirm expected addresses and absence of stale conflicting records. Certificate/HTTPS verification occurs in C05 after proxy setup.

| Record card field | Fill before handoff |
| --- | --- |
| Provider / zone / authoritative nameserver | Name.com / `playpokerattack.com` / `ns1cwy.name.com` (also ns2ckr/ns3cjl/ns4gvx.name.com) |
| Purpose / FQDN / provider-relative name | M01 pilot / `pilot-api.playpokerattack.com` / `pilot-api` |
| Record type / exact value / TTL | A / `170.187.142.115` / 300 seconds |
| Existing record(s) / exact manual reversal | Authoritative NXDOMAIN, no A/AAAA/CNAME; rollback: delete only newly added `pilot-api` A record |
| Operator / completion UTC / resolution evidence | Henry Faulkner reported done before approximately 14:27 UTC on 2026-11-14; authoritative/public resolution verified at that time; no agent DNS writes |

**Pass/evidence:** operator completion recorded and authoritative/public resolution matches the handoff. **Failure:** keep C05/D01 pending; owner/operator corrects or reverses DNS manually. Continue independent build/configuration work. **Depends on:** A05, C02; may run while C03–C04 are prepared.

### C05 — Configure pilot HTTPS, proxy and network rules

- [ ] C05 complete.

**Actions:** Validate candidate Compose with `config --quiet`. Verify M01's manually published pilot DNS read-only. Add the pilot site to the existing proxy. Preserve certificate volumes, validate the full proxy config and perform a scheduled reload. Permit public 80/443 and selected SSH route; do not expose API ports or broaden DB access. Restrict staging `/test/*` to the runner/private path while blocking it on the production virtual host. Verify co-tenant websockets after shared-proxy reload.

**Pass/evidence:** pilot certificate validates without bypass, DNS resolves correctly, approved CORS origins are configured, API ports inaccessible externally, and TransitJazz/DB remain healthy. **Failure:** restore the saved proxy/site configuration; any DNS reversal is manual under M01. Do not reset shared proxy storage. **Depends on:** B03, C02–C04, M01.

## Phase D — pilot, runtime behavior and recovery

### D01 — Start pilot and validate health/HTTP boundaries

- [ ] D01 complete.

**Actions:** Start only the isolated pilot API with its selected digest/config. Confirm loopback bind, actual image, `Staging` environment and pilot DB identity through a safe local verification. Check liveness and readiness locally and through public HTTPS; simulate DB unavailability in isolation and confirm readiness goes 503 and recovers. Verify settings/scoring/lobby APIs, allowed and denied CORS preflights. Separately run the same image in Production against the pilot DB to prove `/test/cleanup` and `/test/signal-r` return 404 directly and through the production-policy proxy; restore isolated Staging afterward if needed for E2E.

**Workstation / PowerShell — substitute worksheet hostname/port/IP:**

```powershell
$pokerPilotBase = 'https://REPLACE_WITH_PILOT_HOST'
curl.exe --fail --max-time 15 "$pokerPilotBase/health/ready"
curl.exe --max-time 15 -i -X OPTIONS "$pokerPilotBase/cks-notification/negotiate?negotiateVersion=1" -H 'Origin: https://REPLACE_WITH_PILOT_CLIENT_HOST' -H 'Access-Control-Request-Method: POST' -H 'Access-Control-Request-Headers: content-type,x-signalr-user-agent'
Test-NetConnection REPLACE_WITH_VM_IP -Port 8082
```

**Pass/evidence:** success/negative-path results, exact-origin grant and denied-origin result, real certificate validation, no secret-bearing health payload. **Failure:** isolate DB, bind, proxy, DNS and CORS causes; do not loosen all controls. **Depends on:** C03–C05.

### D02 — Prove real gameplay and SignalR through the pilot client

- [ ] D02 complete.

**Actions:** Deploy the isolated browser artifact and inspect its effective settings/network traffic. Run the guarded E2E suite against it. Using distinct player browser contexts, create/join a lobby, start a game, play/discard, select/activate power, buy shop items, advance rounds and finish. Verify `ReceivePokerAttackNotification`, lobby/game group delivery, private hand messages, and actual PostgreSQL game/round rows. Complete a solo flow. Test same-player multiple tabs, closing one versus the last, network loss/reconnect, and creating a fresh session after server restart. Confirm negotiate and a real WebSocket upgrade; record transport fallback if supported rather than disabling it to hide a proxy failure.

**Pass/evidence:** full-game and solo results, DB persistence proof, real notification receipt per intended player, no cross-player private-message leakage, and understandable reset/reload behavior. A negotiate 200 or health 200 alone fails this gate. **Failure:** fix/retest affected behavior in pilot; keep production on ACA. **Depends on:** B05, D01.

### D03 — Verify the specified limits and complete a pilot soak

- [ ] D03 complete.

**Actions:** Verify the running pilot has Docker `NanoCpus=250000000` and `Memory=536870912` using a narrowly filtered inspect. Run at least 24 hours with representative games, connections, cold starts and normal co-tenant activity at those limits. Start with design targets of 20 clients/two concurrent multiplayer games or actual peak if higher. Measure API p95, notification delay, memory peak, CPU/throttling, OOM/restarts, DB latency, disk/log growth and transfer. Keep isolated pilot and production identities distinct and stop pilot at cutover. Host capacity remains assumed sufficient; this checks deployed behavior, not placement eligibility. Do not automatically raise limits, resize or select another VM.

**Pass/evidence:** exact runtime limits verified, selected targets met (proposed p95 <500ms, notifications <1s), no OOM/restart loop or observed material DB/TransitJazz regression. No host-headroom threshold is required. **Failure:** diagnose and report measured failures; defer release if behavior is broken, without silently changing the specified limits. **Depends on:** D02.

### D04 — Rehearse application deployment, rollback and reboot recovery

- [ ] D04 complete.

**Actions:** Implement and install the root-owned digest-only deploy/SSH wrapper with host lock, protected known-good image/config pairs, pre-pull, maintenance condition, bounded readiness and post-deploy public/SignalR validation. Bootstrap known-good only from the verified pilot. Rehearse success, bad digest/command, failed pull (old server remains), failed startup, local-health/public-hub failure, concurrent deploy, rollback failure and interrupted SSH. Verify unsuccessful releases report failure even if rollback succeeds. Also rehearse R01 against isolated targets, including ACA revision/traffic restoration and publication of the saved client artifact with a test-only backend configuration; do not interrupt live ACA production for this exercise. Reboot the host in a coordinated window after backups; check DB startup, app recovery when DB is delayed, proxy and all co-tenants. Capture live-state loss as expected and verify new-game recovery.

**Pass/evidence:** normal rollback within chosen 15-minute target, correct digest/config restored, no DB/proxy service stopped by app release, full host recovery and failure behavior proven. **Failure:** keep CD disabled and fix the release/recovery path. **Depends on:** C01, D02–D03.

### D05 — Verify ongoing backups and monitoring

- [ ] D05 complete.

**Actions:** Configure recurring encrypted off-host backups and selected retention; verify an actual scheduled run and restore/decryption path. Configure external HTTPS/readiness monitoring, certificate expiry, backup-age, disk/resource and application-error alerts with a named operator. Confirm Sentry/optional OTLP environment labels distinguish pilot/production, logs rotate, and query strings/credentials are not exposed. Trigger a test alert and verify receipt. Record log access and VM/DB recovery procedures.

**Pass/evidence:** scheduled backup, restore proof, alert receipt and runbook locations; no assumption that ACA Log Analytics follows the container. **Failure:** resolve monitoring/backup gaps before cutover. **Depends on:** D03–D04.

## Phase E — production cutover

### M02 — MANUAL: owner/operator publishes production API DNS

**Prepared M02 card:** Name.com zone `playpokerattack.com`; FQDN `api.playpokerattack.com`; provider-relative Host `api`; type **A**; answer **`170.187.142.115`**; TTL **300 seconds**. Authoritative old state NXDOMAIN (A/AAAA/CNAME/CAA), apex CAA absent. No AAAA/CNAME/CAA addition. Manual reversal: delete only newly added `api` A record. Preserve apex/www and nameservers. Owner completion/time and post-change resolution remain pending. [Full record cards](LINODE_WP01_EVIDENCE.md#manual-dns-cards--ready-for-owner-entry).

- [x] M02 DNS complete. Owner reported “done”; verified 2026-11-14 at approximately 14:27 UTC against authoritative ns1cwy.name.com and public resolver 1.1.1.1: api.playpokerattack.com A = 170.187.142.115, TTL 300. Authoritative AAAA/CNAME absent. Apex/www/default SWA still serve both ACA URLs with environment prod. HTTPS remains E01 work.

**Agent preparation:** Complete a separate M01-format record card for the production API hostname, including authoritative provider/zone, exact name/type/value/TTL, existing records and manual reversal. Verify the target is the existing Linode. Keep apex/www client records and nameservers unchanged. Production API DNS can be prepared before cutover because the published client still points at ACA; this step does not authorize switching client URLs.

**Manual action:** Owner/operator creates or updates the supplied production API records and reports completion/time. The agent makes no DNS writes. If records were already created manually with M01, verify them and reuse that evidence rather than requesting duplicate edits.

**Verification:** Query authoritative and public DNS read-only; confirm every record and IPv4/IPv6 behavior. E01 then provisions/verifies the matching proxy virtual host and certificate before any production shutdown.

**Pass/evidence:** manual completion and correct resolution recorded; current production clients still use ACA. **Failure:** owner/operator corrects or reverses records; keep E01–E04 pending while independent work continues. **Depends on:** A05, C02; may be completed early alongside M01.

### E01 — Assemble the cutover and rollback package

- [ ] E01 complete.

**Actions:** Recheck A02–A05 live values, backup age, schema compatibility, all release gates and absence of queued deploy/cleanup jobs. Prepare root-only production env, the tested digest, saved ACA/client rollback artifacts and exact revision/traffic commands. Prepare the client release with both BaseUri values set to the chosen production hostname and no hub suffix; verify published `prod` configuration resolves correctly. Prepare the maintenance landing page and reload/new-game message. Verify M02's manual DNS completion and configure the production Caddy site with test routes denied and valid certificate; scheduled proxy reload requires co-tenant checks. Before the production API starts, this hostname may deliberately return 503. Confirm migration/rollback budget and observation dates in worksheet.

**Pass/evidence:** concrete cutover bundle and recovery commands reviewed against actual resources; HTTPS works; production still uses ACA; no premature client URL publication. **Failure:** defer cutover while keeping ACA active. **Depends on:** all A–D steps, M01–M02.

### E02 — Enter maintenance, stop ACA and finalize schema gate

- [ ] E02 complete.

**Actions:** Publish maintenance entry page to deter new sessions, announce required reload/reset through the agreed mechanism, and observe existing games. Let them finish within the selected drain budget or explicitly record their termination. Recheck that no automated process can restart ACA. Capture active revisions, deactivate all of them, wait for replicas/connections to stop and verify the old endpoint no longer accepts gameplay. Stale tabs must not be able to create games on ACA. Stop the isolated pilot before replacing runtime configuration. If a reviewed compatible schema migration is necessary, take/confirm a fresh backup and run its matching bundle once with separate credentials and the same release lock; verify history afterward. Otherwise record that no migration was run.

**Workstation / PowerShell — after recording actual values and entering maintenance:**

```powershell
$pokerRg = 'poker-attack-rg'
$pokerApp = 'poker-attack-api'
$pokerActiveRevisions = @(az containerapp revision list -g $pokerRg -n $pokerApp --query '[?properties.active].name' -o tsv)
if ($LASTEXITCODE -ne 0) { throw 'Could not inventory active revisions' }
foreach ($pokerRevision in $pokerActiveRevisions) {
    az containerapp revision deactivate -g $pokerRg -n $pokerApp --revision $pokerRevision
    if ($LASTEXITCODE -ne 0) { throw "Failed to deactivate $pokerRevision" }
}
az containerapp revision list -g $pokerRg -n $pokerApp --query '[?properties.active].name' -o tsv
```

Expected: no active revisions, then verified replica stop. Setting minimum replicas to zero is insufficient.

**Pass/evidence:** UTC shutdown boundary, no active ACA game process, explicit game-loss/drain outcome and compatible schema. **Failure:** do not start Linode production; restore ACA if maintenance exceeds budget. **Depends on:** E01.

### E03 — Start exactly one Linode production process

- [ ] E03 complete.

**Actions:** Under the deployment lock, activate protected production config with exact `Production` environment and actual production DB role. Start only `pokerattack-app` server at the selected digest/loopback port. Check local/public health, running image and correct DB identity without dumping secrets. Confirm test routes absent, CORS exact, certificate valid and ACA still stopped. Use a controlled game/client to prove production round persistence and hub delivery before opening public entry. Do not call cleanup or repurpose production as the full test suite's target.

Verify the production container also reports `NanoCpus=250000000` and `Memory=536870912`; API and hub share this allocation.

**Pass/evidence:** one authoritative production process, health and controlled gameplay succeed within five-minute readiness budget, schema/DB settings correct. **Failure:** execute R01 below within maintenance budget; don't repeatedly restart without diagnosis. **Depends on:** E02.

### E04 — Publish both client endpoints and reopen entry

- [ ] E04 complete.

**Actions:** Deploy the prepared client artifact through the existing SWA pipeline, replacing the maintenance entry page when E03 passes. Inspect served `appsettings.json`, environment header and any environment-specific override; both named URLs must use the new hostname. Verify fresh browser API calls and WebSockets target Linode, including apex/www routes actually used. Check stale-tab/reload behavior and discovered caching/service-worker behavior. Complete a multiplayer round and solo smoke using controlled player IDs; verify production DB records. Confirm old ACA URL remains unavailable for gameplay. Record UTC public cutover time.

**Pass/evidence:** actual browser traffic, HTTPS/WSS, settings and gameplay all agree with the release; no split between ACA API and Linode hub; client reload path works. **Failure:** R01 restores both server and client, not just one URL. **Depends on:** E03.

## Recovery branch — use whenever production cutover fails

### R01 — Restore ACA and the matching client (conditional)

- [ ] R01 executed successfully, or marked not required after the observation window with rationale.

**Actions, in order:**

1. Keep/re-enter client maintenance and freeze deployments; capture redacted failure evidence and time.
2. Under the deployment lock, stop only the Linode production API and verify it stopped. Leave PostgreSQL, TransitJazz and Caddy intact. The Linode hostname must not keep accepting game mutations.
3. Verify DB availability and compatibility. If the shared DB VM is down, use the database recovery runbook first; activating ACA cannot repair it. Do not restore a stale DB snapshot for an application failure.
4. Activate the recorded ACA revision, restore its captured traffic assignment for its actual revision mode, verify its image/config and DB connection, and wait for it to serve the original endpoint. Recheck that no other active revision receives traffic.
5. Publish the saved client artifact with both original ACA BaseUri values; restore production entry only after it works. Verify effective served configuration, real browser API/hub destinations, multiplayer/solo and persisted rounds. Require stale tabs to reload.
6. Record UTC recovery, duration and lost live sessions. Leave Linode CD disabled, mark affected E steps incomplete and create a repair/rehearsal plan. No automatic database downgrade.

**Pass/evidence:** exactly one authoritative ACA process, matching live client, game/round success, recovery within selected target. **Failure:** maintain outage status and recover DB/config/traffic based on evidence; do not run both production game processes. **Depends on:** E01 rollback package. This branch should already have been rehearsed against isolated resources in D04.

## Phase F — observation, automation and retirement

### F01 — Prove production stability and rollback readiness

- [ ] F01 complete.

**Actions:** Observe for the selected 7–14 days with normal games, backups, monitoring and co-tenant load. Confirm DB writes, reconnect behavior, resource margins, certificate health and transfer costs. Preserve ACA revision/image/config/DB route and saved client throughout. Verify known-good records now refer to production image/config after full public gameplay checks; do not leave pilot credentials as production rollback config. Rehearse a controlled production app restart only during maintenance, using the D04 recovery procedure; coordinate any additional host reboot with all co-tenants. Mark R01 not required only after this period if no rollback occurred.

**Pass/evidence:** dated observation results, no unresolved release-blocking failures, current recoverable backups and image/config pair. **Failure:** investigate or execute R01; do not retire ACA. **Depends on:** E04.

### F02 — Enable Linode-only deployment in Azure Pipelines

- [ ] F02 complete.

**Actions:** Configure `PokerAttack-Linode-Prod` environment, dedicated secrets/secure file and verified SSH host fingerprint; apply branch controls and exclusive deployment check with sequential behavior. Install restricted `pa-deploy` key/account without Docker group, forced digest-only wrapper and narrow validated sudoers entry. Ensure immutable images come from the tested build and isolated E2E gate. Remove/disable ACA update paths, unconditional production migration and destructive production E2E; revise manual deployment script to match the new target and maintenance contract. Use the proven host lock, maintenance gate and bounded rollback. Test a real authorized maintenance release plus denied command, concurrency and failure behavior through CI. Preserve explicit non-destructive post-release checks; do not mark success solely on SSH exit/local health.

**Pass/evidence:** pipeline deploys only PokerAttack on Linode, validates public health and SignalR, restores failures and reports them correctly, and cannot silently reactivate ACA. **Failure:** retain build-only CI plus the proven manual deploy command; leave this automation item unchecked. If the recorded operating decision explicitly selects manual CD, mark automation not applicable with evidence of the build/staging gates, tested manual deployment and disabled ACA paths; the same maintenance and recovery requirements apply. **Depends on:** D04–D05, E04; may be implemented during F01, but retirement waits for a stable chosen deployment path.

### F03 — Retire only obsolete Azure server resources

- [ ] F03 complete.

**Actions:** After F01, inventory dependencies again and identify exact obsolete ACA resources and dedicated monitoring/network components. Confirm stable Linode operations, backups, cost and recovery; record the retirement decision under the authorized execution scope. Remove only those obsolete resources, disable definitions that recreate them, and retire old deployment credentials/DB allow rules only after confirming no other consumer needs them. Keep ACR, SWA, database and shared resources. Remove obsolete pilot resources through their own scoped procedure, preserving evidence and shared proxy/storage. Do not delete an entire resource group by assumption.

**Pass/evidence:** retired resource IDs and final inventory recorded; production and co-tenants remain healthy; no stale pipeline points to removed infrastructure. **Failure:** defer retirement; continuing to retain a rollback target is safer than an unverified removal. **Depends on:** F01–F02, recurring backup/recovery proof.

### F04 — Close out documentation and status

- [ ] F04 complete.

**Actions:** Update this ledger and design with final topology/decisions, source/image versions, production URLs, operator commands, safe deployment and rollback steps, database recovery, secret rotation, backup/retention, monitoring and measured cost. Correct the older staging guide and test docs to match actual isolation and endpoint behavior. Explain the remaining live-game reset limitation and single-VM availability tradeoff. Preserve failed attempts/deviations rather than rewriting history as all-pass.

**Pass/evidence:** every applicable execution step is verified; outstanding optional follow-ups and owners are explicit; no secrets in committed evidence. Migration completion requires working Linode production, matching clients, safe releases, monitoring and recovery—not merely a built image. **Depends on:** F03.

## Evidence ledger

Add one row per attempt; use private artifact references for sensitive logs and redacted summaries here. Update the top status as `NOT STARTED`, `IN PROGRESS`, `BLOCKED: <step/reason>`, `ROLLED BACK`, or `COMPLETE`.

| Step / attempt | UTC | Commit / digest | Result and measured evidence | Evidence location | Next action / blocker |
| --- | --- | --- | --- | --- | --- |
| Planning only | 2026-11-14 | PokerAttack `b6a4d17`; TransitJazz PR head `6dfef67` | Source/PR review; no execution steps performed | This checklist and linked design | Await implementation request |
| Planning clarification | User follow-up; UTC not recorded | No implementation change | Owner confirms existing Linode hosts production `expensetrackerdb`, schema `PokerAttack`; repository schema mappings agree | Conversation and linked design | Verify live connection, permissions and EF history in A03; execution remains not started |
| Work-package preparation | User follow-up; UTC not recorded | Documentation only | Existing Linode capacity assumed sufficient; API/hub allocation fixed at 0.25 CPU / 512 MiB from supplied screenshot; DNS changes assigned to manual M01/M02 | Conversation, design and work packages | Packages prepared; no implementation, DNS writes or deployment performed |
| WP01 inventory | 2026-11-14 approximately 14:00–14:21 UTC | Read-only Azure/SSH/PostgreSQL/DNS/GitHub inventory; branch created | ACA/client targets and digest recorded; DB TLS authenticated; free ports 8081/8082; rollback artifact denied TF400813; console/permissions/operations unresolved | [WP01 evidence](LINODE_WP01_EVIDENCE.md) | WP01 remains IN PROGRESS; owner artifact access and console/window follow-up pending |
| Owner direction | 2026-11-14 | Delivery design superseded; no deployment | Use `api.playpokerattack.com`; future delivery GitHub Actions similar to TransitJazz | Conversation; WP02 evidence | Translate WP09 environment/concurrency controls to GitHub Actions |
| Independent B01 safety preparation | 2026-11-14 | Disabled Azure definitions 39/40/41; local pipeline/workflow/manual-script/test-project edits | Disabled status independently read back; queues initially empty; no production cleanup/deploy runs | [WP02 evidence](LINODE_WP02_EVIDENCE.md) | No publication until WP01/WP02 gates complete |
| WP02 baseline | 2026-11-14 | Updated obsolete lobby tests to current repository/DTO/Result API; no gameplay change | BL 11 passed, zero skipped; API Release build and client Release publish passed; existing advisories remain | [WP02 evidence](LINODE_WP02_EVIDENCE.md) | WP02 acceptance still waits on parent WP01; B04 later hosting/client/container checks pending |
| Client rollback preservation | 2026-11-14 approximately 14:22 UTC | Owner-provided run 1268 artifact; SHA256 `1ea9a7e927590b0d23eb9e0c5000823466d7c5232794b4200b431c252fc1e945` | Saved private byte-verified copy; nested ZIP has 461 entries; saved index matches live and settings JSON matches live, both URLs ACA | [WP01 artifact inventory](LINODE_WP01_EVIDENCE.md) | Artifact preservation resolved; publication rehearsal pending WP07; owner permits maintenance anytime |
| M01/M02 manual DNS verification | 2026-11-14 approximately 14:27 UTC | No agent DNS writes | Owner reported done; authoritative/public A records both 170.187.142.115, TTL 300; no authoritative AAAA/CNAME conflict. Apex/www/default SWA still prod and both ACA URLs; Azure pipelines 39/40/41 still disabled | [WP01 follow-up](LINODE_WP01_EVIDENCE.md) | DNS milestones complete; HTTPS and remaining WP01 acceptance gates pending |
| WP01 credential-consumer follow-up | 2026-11-14 approximately 14:34 UTC | Read-only PostgreSQL/Azure inventory | ExpenseTracker and PokerAttack share the same postgres credential; live TransitJazz also uses postgres. Restricted-role plan recorded; no shared credential or co-tenant settings changed. Browser controls failed before provider inventory; provider details requested | [WP01 follow-up](LINODE_WP01_EVIDENCE.md) | Complete provider/console inventory and credential/recovery plan before accepting WP01; broader checklist acceptance update was rejected by automatic approval review |
| Grafana and database skill follow-up | 2026-11-14; metric window 14:10-14:40 UTC | Repo-local Grafana/PokerAttack DB skills; read-only inspection | Correct Grafana context authenticated; saved panel queries return data; overview PNG visually checked. Latest CPU 3.3%, memory 42.5%, root available 36.6 GiB; partial-window coverage disclosed. Both DB helpers verified read-only Repeatable Read/UTC; workstation app authentication uses TLS 1.3; 178 games/17701 rounds/44655 scores | [Skills and monitoring evidence](LINODE_MONITORING_AND_DB_SKILLS_EVIDENCE.md) | Skill validators/guard checks pass; provider/console, scoped role/rotation and remaining operational decisions remain open; no production deployment or dashboard mutation |

| Owner acceptance and WP03/WP04 | 2026-11-14 | Local source only; base b6a4d17 plus migration changes | Owner confirms LISH, maintenance anytime and recovery defaults. WP01 inventory/WP02 isolation accepted; WP03 hosting and WP04 local recovery/guards/artifact preparation verified: 28 C# tests, 21 guard tests, 2 artifact tests; client Release publish passed | [WP03 evidence](LINODE_WP03_EVIDENCE.md), [WP04 evidence](LINODE_WP04_EVIDENCE.md) | WP05 packaging underway; B04/B05 retain container/pilot checks; no publication or deployment |

| WP05 local verification | 2026-11-14 | Uncommitted packaging/release code only | API/bundle images built; 11 layers/4097 files scanned with no credential marker/environment config. Compose/Bash/Caddy validate. 8 release failure tests and 3 real disposable browser smokes passed; exact limits/non-root/scoped runtime verified | [WP05 evidence](LINODE_WP05_EVIDENCE.md) | WP06 backup/role/config preparation next; no image push, host installation, proxy reload or production change |

| WP06 recovery and host preparation | 2026-11-14 15:41 UTC snapshot | New protected shared-host backup only; no app/role/proxy changes | Eight off-host DPAPI/checksum roundtrips; all four databases and roles restored with original ownership/ACLs; 41 tables/3953618 rows match dumps, 39.6 seconds. Host services compatible/healthy; ports free | [WP06 evidence](LINODE_WP06_EVIDENCE.md) | Registry provenance and protected role/config provisioning next; recurring backup and credential rotation remain open |
