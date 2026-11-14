# Production cutover evidence - 2026-11-14

Status: production API/client cutover COMPLETE with real browser verification and promoted known-good. ACA app deleted; dedicated environment deletion is accepted and awaiting Azure completion.

Owner explicitly chose **defer shared credential rotation; move production to Linode; remove ACA production**. ExpenseTracker/TransitJazz credentials are unchanged. This overrides the previous planned ACA retention period. Remaining external alert receipt, coordinated reboot/interruption recovery and GitHub deployment automation remain follow-ups; this record does not claim they passed.

## Identity and boundaries

- API source `250b03496c69e33a206e294cbad861a94fa6844a`, image `chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi@sha256:693d3d6a193810000f7ba2c74e51fc14dee641806ebd2c5266bcc471f7154588`.
- Production config `7c830479833d8b9a8ed119a75a73069fd70c1ce510f67c060cfc365f9936355f`, protected root-only snapshot; `Production`, test endpoints false, `pokerattack_runtime` / production `expensetrackerdb`, original production timers retained. No DB schema migration.
- Client source `86c63ee501fd5a6fd60f2fdeb3ea4ee609114b1c`, artifact `artifacts/production-release-linode`, both URLs `https://api.playpokerattack.com/`, environment `prod`; all configuration variants and gzip/Brotli representations rewritten consistently. Fresh Release publish passed with existing dependency advisory retained.
- Exactly one `pokerattack-production-api-1`, host loopback 8081, 250000000 NanoCPUs / 536870912 bytes. Pilot stopped before production start. Existing PostgreSQL, TransitJazz and shared Caddy preserved.

## Execution boundaries

- Production Caddy site added/validated/reloaded **17:31:18Z**, normal HTTPS valid, forwarding loopback 8081; public test/ops blocked. Protected original `/etc/pokerattack/production/caddy-before-production.Caddyfile`. TransitJazz actual MessagePack JoinCityV2(atlanta) replay returned two items after reload, readiness 200.
- Backup/off-host latest `20261114T162424Z`, within agreed age; full earlier logical restore proof remains WP06. ACA app and environment snapshots encrypted with CurrentUser DPAPI under the existing private owner backup directory before retirement.
- Maintenance page verified on apex/www/default SWA. Legacy Azure definitions 39/40/41 independently rechecked disabled.
- **17:34:07Z**: all ACA revisions inactive; replica list empty; old URL /lobby returns 404. Exact existing live session count unavailable on legacy server; maintenance explicitly resets matches. No claim of preserved sessions.
- **17:35:21Z**: production controlled deploy ready locally. Normal public live/ready 200; test cleanup and ops 404; lobby 200. First cold DB readiness attempt timed out and recovered inside the bounded deploy loop.
- Pre-opening controlled two-player public HTTPS/WebSocket round completed **17:41:07Z**; per-player HandPlayed/CardsDealt isolation checked with a subsequent ordered group fence. Read-only production query found game `04be14fd-eea8-4b13-b953-ed45c1facac6`, one round and two scores. No cleanup endpoint called. First manual JS protocol attempt failed because notification string enums were passed back as typed hub arguments; corrected probe converts enums to the numeric hub wire format. That failed game remains one round with zero scores after normal disconnect; no historical data was deleted.
- Maintenance CLI deployment succeeded but its wrapper initially raised a Windows stdout encoding error after deployment; visible maintenance was independently verified before ACA shutdown. Wrapper encoding corrected. SWA CLI used Node 22 because system Node 20.9 could not load current dependencies; token supplied only through child environment, output redacted, never command arguments.
- **17:42:34Z**: production client served on apex, www and default SWA hostname, both named URLs verified new origin with header `prod`. Publication returned success. Browser gameplay/solo verification pending below.

## Retirement scope

Only `poker-attack-api` and its dedicated `poker-attack-api-env` are selected for removal, after production verification. Subscription-wide container-app inventory confirms no other app uses that environment. Keep `poker-attack-webapp`, ACR, PostgreSQL, identities not explicitly inventoried for removal, resource group, DNS zones/records and all other applications. No agent DNS write.

## Public client verification and known-good

`artifacts/production-browser-result.json`: **PASS**, **17:42:35Z-17:44:19Z**, two fresh independent browser contexts using apex and www, both real WebSockets exclusively `api.playpokerattack.com`, no ACA/pilot API requests observed. Created/joined the controlled lobby, selected powers, played hands, both received RoundEnded. Completed solo hand/scoreboard/game-over afterward. Normal context closure removed only these players; no production cleanup endpoint or full E2E suite was run.

Read-only production query for controlled browser game `615ab54e-7cea-498b-8f6a-8a4cd7523362`: **one game, one persisted round, two scores**. Production known-good promoted using protected verification identity `383b408d0cc9c4858020a67a9eba858ca9e135b921e06ecd41933f847502c486`. Running container verified true, OOM false, restart count zero, exact CPU/memory limits. API/DB pair is production-specific, not pilot config. Client/API migration complete; ACA removal follows the explicit owner direction.

CI **43622429347** passed for `86c63ee501fd5a6fd60f2fdeb3ea4ee609114b1c`. Subsequent documentation changes do not change the tested code or deployed artifact.

## Fresh production recovery capture

Post-cutover shared backup **20261114T174700Z** completed with service Result=success, ExecMainStatus=0. Off-host sync completed with checksum/decryption verification and CurrentUser DPAPI encryption in the private recurring backup directory. This capture includes current production known-good configs/state, shared roles and application databases after controlled gameplay. No restore to the live database occurred. Earlier isolated full restore remains the recovery proof.

## Retirement progress

`poker-attack-api` delete completed successfully after public browser verification. A fresh subscription-wide inventory confirmed its dedicated environment had no remaining app consumers. `poker-attack-api-env` deletion accepted; latest intermediate state ScheduledForDelete. Final absence verification is pending. SWA, DNS zones/records, resource group, managed identity and unrelated apps remain.

## Final running-state check

At **17:51:54Z**, production and TransitJazz public readiness both 200. Production running as UID 1654 with read-only root, exact resource limits, zero restarts/OOM, active pair equals known-good with no pending candidate. Pilot stopped. Docker sample: 0.06% CPU, 114.4 MiB / 512 MiB. Private session snapshot: zero active multiplayer games/lobbies, one solo entry retained after the controlled run; no global cleanup was performed. Off-host latest remains fresh `20261114T174700Z`. Root-only aggregate report `/etc/pokerattack/production/cutover-final-check.json`.

## Azure cleanup limitation

At approximately **18:02Z**, the production ACA app remains absent from fresh resource inventory. Environment deletion was accepted at **17:46:11Z** and remains `ScheduledForDelete` after about 15 minutes; its original CLI request is still polling. There are no remaining environment apps/jobs and its VNet/infrastructure-resource-group properties are null, so no associated VNet cleanup applies. No further network/resource-group deletion was attempted. This Azure control-plane cleanup is pending; do not mark F03 complete or claim the environment is absent until a later inventory proves it. Production migration and ACA application removal are independently verified and complete. Private/local status snapshot: `artifacts/aca-retirement-status.json`.
