# WP01 inventory — 2026-11-14

**Current acceptance update:** COMPLETE as configuration inventory, with privilege/credential/firewall findings carried to WP06 deployment gates. Owner explicitly confirmed LISH (`ssh -t henryfaulkner-cks@lish-us-southeast.linode.com expense-tracker-vm`) and accepted recovery defaults with a proportionate hobby-project approach. Owner believes no cloud firewall; attachment remains unverified. Earlier pending notes below are historical. No publication or deployment.

Run: `20261114-wp01`, implementing agent with owner Henry Faulkner. Inventory started approximately 14:00 UTC. Source: `b6a4d17d8eb42e9f9bee8214021406ff8be22659`; implementation branch `migration/linode-server-hosting`. WP01 is IN PROGRESS; unresolved gates below prevent acceptance. No publication, deployment, database mutation or DNS write occurred. Separate B01 safety work disabled the three legacy pipeline definitions after their inventory.

## Source and access

- No AGENTS.md found in checkout or checked ancestors. Existing untracked `.claude/skills/` and three migration planning documents preserved.
- TransitJazz PR [77](https://github.com/henryfaulkner/ChefKnifeStudios.TransitJazz/pull/77) head is `2921782acd35f8f64d6d91bb63cdcf71e4540be6`, beyond planning reference `6dfef670c72232af992416c09816cdbc0cdf86a8`. Its status document reports completed production activation and CI, with browser/recovery/backup follow-ups outstanding. Do not repeat that application's cutover. Its public readiness was independently HTTP 200 during this run.
- Azure CLI session works for subscription `ChefKnifeStudios`, ID `d2f70759-7eeb-4ca5-9046-f7c8d562ef8f`. GitHub CLI session works. Azure DevOps CLI extension installed. Owner supplied organization `henryfaulkner`, project `ChefKnifeStudios.DevOps`, server definition 39. Pipeline metadata reads work; CLI client artifact download failed (TF400813 unauthorized); owner subsequently supplied the ZIP, now saved and verified.
- Root SSH to `170.187.142.115` works with existing `~/.ssh/id_ed25519_linode_us_southeast`, BatchMode and StrictHostKeyChecking enabled. Trusted ED25519 host fingerprint: `SHA256:bV3vsdj3SnjD64ATPEpUNxjoXPrr0eQTut0R1rgDRu4`. Console access and provider firewall state still need verification. No Linode API credential was discovered or used.

## Azure and rollback inventory

- Resource group `poker-attack-rg`, ACA `poker-attack-api`, environment `poker-attack-api-env`. Running status `Running`; revision mode `Single`; active/ready revision `poker-attack-api--0000043`. Revision-list sample showed zero replicas, consistent with configured scale-to-zero; this does not prove gameplay readiness.
- Current image tag: `chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi:main-chefknifestudios-pokerattack-server-20260125.1`.
- ACR resolved digest: `sha256:408302ba8113d37039de40de2c01de0fca23613492df56a124d6e8575356321e`. Retain this repository/digest for rollback; no pull/deploy recovery rehearsal performed.
- FQDN and both saved browser backend URLs: `https://poker-attack-api.ambitioussmoke-21ab3755.eastus.azurecontainerapps.io/`.
- Ingress: external, HTTPS required, Auto transport, no custom domains, sticky affinity, traffic `latestRevision=true, weight=100`. Capture explicit rollback traffic instructions after revalidating revision state at cutover; revision activation alone is insufficient.
- Live allocation: 0.5 CPU / 1 GiB, min replicas 0, max 1, HTTP concurrency threshold 10. This differs from the planning screenshot; Linode's owner-selected 0.25 CPU / 512 MiB remains unchanged. No configured container probes.
- Container settings include `ConnectionStrings__PokerAttackDB`, `ConnectionStrings__AzureStorage`, and four game-timer settings. DB/storage have direct setting values, not secret references; ACA secret-name list was empty. Do not export the resource unfiltered.
- SWA: `poker-attack-webapp`, default hostname `salmon-hill-0056eb60f.2.azurestaticapps.net`; custom domains `playpokerattack.com` and `www.playpokerattack.com` both Ready. Served apex appsettings returned HTTP 200 with `Blazor-Environment: prod` and both backend URLs above. `appsettings.prod.json` returned HTML fallback, not a JSON override. Other origins still require served-setting checks.
- ACA CORS currently lists the default SWA hostname, apex/www `playpokerattack.com`, apex/www `hankfaulkner.com`, and `www.henryfaulkner.xyz`. Do not copy stale origins into the new allowlist without verifying use.
- Owner supplied `C:\Users\hfaul\Downloads\chefknifestudios-pokerattack-webapp-main.zip`. Copied and byte-verified to `C:\Users\hfaul\PokerAttack-Backups\20261114-wp01\chefknifestudios-pokerattack-webapp-main.zip`; SHA256 `1ea9a7e927590b0d23eb9e0c5000823466d7c5232794b4200b431c252fc1e945`. Outer archive contains nested `chefknifestudios-pokerattack-webapp-main/chefknifestudios-pokerattack-webapp.zip`; inner archive has 461 entries including runtime payload. Saved index matches live bytes and saved appsettings matches live JSON; both URLs still ACA. Artifact from client run 1268/source `b6a4d17d8eb42e9f9bee8214021406ff8be22659`. Original CLI TF400813 failure retained as access evidence; owner-supplied copy resolves artifact preservation. Rehearse publication with this exact inner archive in WP07 before cutover.

## Database and credential boundary

- Effective production connection resolved in memory from ACA: host `170.187.142.115`, port 5432, database `expensetrackerdb`, user `postgres`. Authenticated read-only transaction over host loopback independently returned that database/user and TLS 1.3. No explicit SSL mode was present in ACA's connection string; its remote transport validation remains unverified.
- PostgreSQL 16.15, native enabled host service. Extensions: `plpgsql` 1.0, `vector` 0.6.0. Production tables: `PokerAttack.Games` (178), `PokerAttack.Rounds` (17701), `PokerAttack.RoundScores` (44655), all owned by `postgres`; counts are a point-in-time sample.
- EF history is `public.__EFMigrationsHistory`, owned by `postgres`, containing ten rows shared with ExpenseTracker. PokerAttack rows include `20251008022524_Init_Data`, `20251008234907_NoUsers`, `20251015221621_Update_Game_HostPlayerClientId`. Never move, truncate or replace this shared history to simplify deployment.
- The current superuser cannot satisfy the app-scoped permission boundary. Provisioning a restricted production role and separate migration authority requires a consumer-aware plan. Proposed isolated pilot database/role: `pokerattack_pilot` / `pokerattack_pilot_app`; neither exists yet. Never use production for destructive test cleanup.
- Other databases: `expensetrackerdb_dev`, `transitjazz`, `personal_health_hub`, `postgres`. ExpenseTracker shares production database through its own schema.
- Protected existing host backup directory `/var/backups/transitjazz-migration-20261113` contains dumps of the four application databases, globals, PostgreSQL configuration and checksums. TransitJazz reports encrypted off-host copies at `C:\Users\hfaul\TransitJazz-Backups\20261112-190948` and successful isolated data restores. This run verified host filenames only; backup age, off-host recovery, retention, and recurring automation remain WP06 gates.
- Credential incident: a faulty PowerShell dictionary-adapter redaction printed the DB connection credential once in tool output. Corrected code uses explicit .NET accessors and allowlisted metadata. Credential value is deliberately absent from evidence files. Treat it as exposed and coordinate rotation across actual consumers; no shared credential was changed. Inventory the existing credential-bearing development source during B03 as well.

## Shared host and ports

- Host at `170.187.142.115`: Ubuntu 24.04.2 x86-64. Provider label reported by TransitJazz: `expense-tracker-vm`; shell hostname `localhost`. Linode ID/plan/region not independently queried.
- Docker 29.9.3, Compose v5.7.1. Running containers `transitjazz-app-server-1` and `transitjazz-app-proxy-1`, host networking. PostgreSQL and Alloy active; PostgreSQL enabled at boot.
- Listeners: public 22, 80, 443, 5432 (including IPv6); loopback TransitJazz 8080, Caddy admin 2019, Alloy 12345, system DNS 53. TCP 8081/8082 were unused: reserve for PokerAttack production/pilot, binding only `127.0.0.1`.
- UFW inactive; provider firewall and other host filtering not verified. Do not alter public DB reachability until ACA rollback and other consumers are understood.
- Shared Caddy ownership: TransitJazz Compose `/opt/transitjazz/compose.yaml`; config `/opt/transitjazz/Caddyfile`, mounted read-only into `/etc/caddy/Caddyfile`. Preserve volumes `transitjazz-app_caddy_config` and `transitjazz-app_caddy_data`. Future additions need shared-proxy coordination and co-tenant checks; no proxy files changed.
- Snapshot: ~1.9 GiB RAM, ~1.1 GiB available; root filesystem 49 GiB with 37 GiB free; TransitJazz app ~268 MiB / 512 MiB, proxy ~12 MiB / 256 MiB. This is troubleshooting evidence, not a capacity gate.
- Both PokerAttack app instances must use 0.25 CPU / 512 MiB; no host resize planned.

## Manual DNS cards — ready for owner entry

Owner selected production hostname `api.playpokerattack.com`; use distinct pilot hostname `pilot-api.playpokerattack.com`. Provider **Name.com**, zone **playpokerattack.com**. Delegated authority: `ns1cwy.name.com`, `ns2ckr.name.com`, `ns3cjl.name.com`, `ns4gvx.name.com`. Direct authoritative `dig` queries returned NXDOMAIN for both candidate names for A/AAAA/CNAME/CAA; apex CAA returned NOERROR with no answer, so no inherited CAA restriction was observed. Read-only PowerShell DNS checks agree for name existence. Query loop returned answers but initially exited nonzero because Windows appended CR; answer contents are preserved here without misreporting script success.

| Milestone | FQDN | Name.com Host | Type | Exact answer | TTL |
| --- | --- | --- | --- | --- | --- |
| M01 pilot | `pilot-api.playpokerattack.com` | `pilot-api` | A | `170.187.142.115` | 300 seconds |
| M02 production | `api.playpokerattack.com` | `api` | A | `170.187.142.115` | 300 seconds |

Existing record for each: absent. Exact manual reversal: delete only the newly added A record for that relative name. No AAAA, CNAME or CAA addition is required. IPv6 has not passed acceptance. Preserve apex/www and nameservers. Proposed pilot client uses an isolated SWA environment's provided hostname, so no client DNS record is needed; its exact URL is pending provisioning.

Owner reported “done” before approximately 14:27 UTC on 2026-11-14. Both A records were independently verified against authoritative `ns1cwy.name.com` (AA response) and public resolver `1.1.1.1`: `170.187.142.115`, TTL 300. Authoritative AAAA/CNAME/CAA answers were empty for both names. M01/M02 DNS milestones are checked. DNS publication does not publish client URLs or start a game process. Certificates/HTTPS await host preparation.

The original `henryfaulkner.xyz` proposal was rejected after live queries found Afternic nameservers and parking addresses. Do not use it.

## Remaining WP01 acceptance gates and next action

1. Client artifact preservation gate resolved by the owner-supplied ZIP. Rehearse restoration before cutover; CLI artifact access remains limited. Pipeline IDs: 39 server, 40 client, 41 nightly E2E, all now disabled by B01. Variable group `Prod-PokerAttack` ID 8. UI CI settings defer to YAML; PR triggers were enabled. Initial pending/running queues empty. Unrelated pipelines untouched.
2. Verify provider console/firewall access and host identity metadata. Inventory the production DB role/transport and credential consumers sufficiently to prepare scoped replacement and rotation. No production role permission isolation exists today.
3. Owner permits maintenance anytime (2026-11-14); this does not verify recovery-console access. Select exact pilot SWA origin, active-game observation/drain method, rollback time budget, RPO/RTO, backup retention and monitoring owner before dependent operations. Live-game operations are not yet proven.
4. Saved client rollback ZIP and source/checksum are now established above. Keep this private copy intact; stage and verify its SWA `prod` header and both ACA URLs during rollback rehearsal.

WP02 safety preparation proceeded independently under B01, which depends on the completed Azure inventory, while WP01 acceptance remains open. See [WP02 evidence](LINODE_WP02_EVIDENCE.md). No changes will be published until the WP01/WP02 gates are resolved. WP03–WP10 remain not started.

## Follow-up after manual DNS — 2026-11-14

- Apex, www and default SWA each returned HTTP 200 for appsettings, environment `prod`, and both original ACA backend URLs. Legacy Azure pipeline definitions 39/40/41 independently remain disabled. Local Docker responds, version 27.4.0.
- Recovery-console status is not independently verified. The owner's “done” follows a combined DNS/status request; clarification requested before treating it as evidence of console access.
- No applicable Linode connector/API credential was found. Browser-control setup connected to Chrome, but navigation and documented keyboard controls failed before reaching the provider page. No browser settings, account permissions or cloud resources were changed. Requested provider ID/region and attached Cloud Firewall rules from the owner.
- Read-only PostgreSQL inventory identified a current TransitJazz connection to database `transitjazz` as `postgres` from loopback, and PersonalHealthHub Grafana connections as restricted role `phh_grafana`. Login roles also include `phh_backup`, `phh_ingest`, `phh_migrator`; only `postgres` is a superuser. PUBLIC has no CREATE privilege on `public`, `PokerAttack` or `ExpenseTracker`. This inventory is not exhaustive; idle applications do not appear in `pg_stat_activity`.
- Scoped-role plan, not executed: create a fresh PokerAttack runtime role with only schema USAGE, CRUD on `PokerAttack.Games/Rounds/RoundScores`, required sequence USAGE and read-only shared EF history access. Keep migration authority separate. Create the isolated pilot role only in its disposable database; prove it cannot read/write production tables. Do not grant ownership, superuser, role/database creation or privileges on ExpenseTracker. Validate readiness and game persistence under the runtime role before changing ACA settings or switching traffic.
- Shared-password rotation remains a coordinated operation after backup/recovery preparation and identification of all consumers, including the verified TransitJazz superuser connection. No shared password or co-tenant configuration was changed in this follow-up.
- Azure consumer inventory additionally verified `expense-tracker-api` uses the same database `expensetrackerdb`, role `postgres` and password as PokerAttack. Password equality was compared only in process memory; no password or hash was printed. The stopped TransitJazz ACA resource uses a secret reference, which was not dereferenced. These findings rule out a PokerAttack-only shared-password rotation.
- An attempted broader checklist acceptance update was rejected by automatic approval review with only “blocked by policy” as its reason. That command did not run. The subsequent narrow update records verified DNS milestones only; WP01/WP02 acceptance and later packages remain unchanged.
