# PokerAttack release tooling

Production automation is installed and enabled on protected `main`. The separate
[client workflow](../../.github/workflows/client.yml) publishes/restores Azure
Static Web Apps artifacts; the [server workflow](../../.github/workflows/server.yml)
publishes exact tested images to ACR and delivers immutable digests to Linode.
K02 bootstrap, K04/K05 actual client/server releases and K06 live client
restoration/API rollback/serialization are verified, including fresh explicit
promotion and reopening. See the [authoritative checklist](../../docs/DEPLOYMENT_AUTOMATION_CHECKLIST.md)
for current evidence and the [release contract](../release/CONTRACT.md) for schemas.
The legacy combined SSH wrapper rejects production. Routine CI uses separate
status, client-lease, deployment, evidence and promotion capabilities. The root
bootstrap path is retained for inspected recovery, not routine delivery.

Production is installed on Linode after the verified pilot and owner-directed cutover. See [production operations](../../docs/LINODE_PRODUCTION_OPERATIONS.md) for current identities, recovery and unfinished operational follow-ups. The owner directed ACA retirement and deferred shared password rotation. Production releases use the protected command below; never start a second authoritative game process.

## Paths and boundaries

Install `compose.yaml` and `validate-config.py` under root-owned `/opt/pokerattack`, deploy command as `/usr/local/sbin/pokerattack-deploy`, and forced SSH command separately. Files must be LF. Keep their parent directories root-owned with no group/other writes. No command manages PostgreSQL, shared Caddy, images belonging to other apps, DNS, or migrations. Host-wide PokerAttack `flock` serializes pilot, production, manual and CI operations.

Provision mode-0600 configs under `/etc/pokerattack/{pilot,production}/configs/<sha256>.env`; filename is the SHA256 of the exact bytes. Provision fresh passwords with at least 32 URL-safe characters. See `app.env.example` for the deliberately narrow connection-string format. Production uses `pokerattack_runtime` in `expensetrackerdb`; pilot uses `pokerattack_pilot_runtime` in `pokerattack_pilot`. Separate roles/configs must exist before startup. Do not reuse the exposed/shared postgres password. `GameSettings` values come from inventory; storage is unused by current server code, so do not copy the old storage credential into the image/config by habit.

Only Production/Staging are used. Pilot CORS is initially `http://localhost:5173`; the production allowlist contains the three verified client origins. The validator refuses duplicate keys, Compose interpolation, quoted values and mismatched environment/database boundaries. Passwords use the generated URL-safe format to avoid ambiguous env/connection parsing.

State lives under `/var/lib/pokerattack/<target>` in protected release directories. Each snapshot contains its immutable image, digest, config hash, copied app.env and generated Compose release.env. Preserve these as secrets in encrypted backup. `active`, `pending`, `known-good`, `previous-good` point to these pairs; never edit them casually.

## Build and validate off-host

```powershell
docker build --platform linux/amd64 -f src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Dockerfile -t pokerattack-api:local .
docker build --platform linux/amd64 -f src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Dockerfile -t pokerattack-migrations:local .
```

Root `.dockerignore` excludes bin/obj, all environment configs, key/env/backup files, and appsettings except the reviewed API base configuration. API csproj also excludes environment configs from publish. `AppDbContextFactory` reads only the explicitly provided connection environment value, with no local secret-file discovery. A null connection supports bundling; executing requires a connection. The bundle is self-contained linux-x64 on runtime-deps 10, run non-root, and never part of API deployment. Rehearse against disposable/restored data; preserve shared EF history. Runtime variables are never build arguments.

The production/pilot use the same Compose file with distinct project names, env snapshots and fixed ports 8081/8082. It creates only `api`, uses host networking and binds Kestrel to loopback. CPU 0.25, memory 512 MiB, read-only root, private /tmp, no capabilities, bounded logs, non-root image user, 40s stop grace (30s application shutdown). No host/DB/proxy volume is managed here. `release.env.example` is illustrative; the script writes actual values from validated identities.

`Caddyfile.example` is additive to `/opt/transitjazz/Caddyfile`, under that proxy owner's Compose lifecycle. Back up, append, validate inside the existing proxy image, then intentionally reload it during WP06/07. Preserve certificate/config volumes and existing sites. Both public sites deny test and operator paths case-insensitively. Use a private SSH tunnel for pilot cleanup. Caddy handles WebSocket upgrades/query strings. No second proxy is installed.

## Routine production releases

Dispatch on trusted `main`, using the installed GitHub authentication:

```powershell
gh workflow run client.yml --ref main -f operation=release
gh workflow run server.yml --ref main -f operation=release
```

Both workflows use `pokerattack-production-release`, `cancel-in-progress: false`,
and a durable host lease. Do not cancel an active release to start another.
GitHub permits only one pending member of a concurrency group; avoid queuing
additional requests over an existing pending request. A host lease survives
runner failure and is not cleared by canceling a run.

Client delivery validates the exact archived artifact, current server readiness
and compatibility before upload, verifies all three client origins and browser
SignalR, then records accepted current/previous pointers. It does not restart API.
For a restoration, choose an actually accepted manifest and its exact identities:

```powershell
gh workflow run client.yml --ref main -f operation=restore -f restore_run=RUN -f restore_attempt=ATTEMPT -f restore_artifact=NUMERIC_ARTIFACT_ID -f restore_checksum=ARCHIVE_SHA256
```

Server delivery builds/tests once, publishes API and migration images with
`server-acr` OIDC, then prepares the numeric `server-release-packet-RUN-ATTEMPT`
artifact through `server-status`. Publication never executes the migration image.
Review the packet's source, tested image IDs/digests, config, schema, current and
recovery pair, session counts and original expiry. A release-specific reset
acknowledgment is required before approving `server-linode`, with its exact comment:
`ACK_RESET PACKET_SHA256 RUN/ATTEMPT`. The original 30-minute packet window must
also cover runner startup and the bounded drain (default 300 seconds). Do not
renew the anchor or reuse a comment from another packet.

The deployment closes admission, drains or resets remaining matches according to
the acknowledged policy, rechecks schema read-only and deploys only the captured
API digest. It leaves a ready candidate pending. `server-verification` runs real
production-safe HTTPS, SignalR, multiplayer, solo, private-message isolation and
served-client checks; it leaves only its own sessions, verifies zero counts,
revokes its credential and imports exact generated proof. No hand-authored pass
booleans or production-wide E2E cleanup are allowed.

Inspect `server-verification-RUN-ATTEMPT` and matching live pending state. A
separate explicit `PROMOTE EVIDENCE_SHA256 RUN/ATTEMPT` decision is required at
`server-promotion`, within the proof's original 30-minute window. After promotion,
CI rechecks accepted client/runtime identity, reopens admission, runs seven public
checks and uploads `server-outcome-RUN-ATTEMPT`. Completion requires actual run
success, `overall_release_complete: true`, matching known-good/runtime, open
admission and no lease. An ACR push or ready pending API alone is incomplete.

GitHub environment review comments must come from the actual release decision.
When authorized to relay a user's exact decision, submit it through the genuine
pending-deployments API; do not invent review history or decision timestamps.
The GitHub history API lacks decision timestamps, so tooling uses the earlier
immutable packet/proof anchor conservatively.

## Installed capabilities

All commands include literal `production` as their second word and reject extra
whitespace, arguments, targets and shell syntax. Packets travel as bounded JSON
on stdin; keys cannot supply paths or obtain a shell. Never print key/config or
verifier credential contents. See [exact grammar](release_commands.py).

| GitHub environment | SSH user / secret | Allowed purpose |
| --- | --- | --- |
| `server-status` | `pa-status` / `LINODE_STATUS_PRIVATE_KEY` | `status production`, `rollback-target production` |
| `client-swa` | `pa-client` / `LINODE_CLIENT_LEASE_PRIVATE_KEY` | Client lease begin/finish/resume/recovery; status |
| `server-linode` | `pa-deploy` / `LINODE_DEPLOY_PRIVATE_KEY` | Begin release/rollback, exact deploy/rollback, guarded resume; status |
| `server-verification` | `pa-evidence` / `LINODE_EVIDENCE_PRIVATE_KEY` | Issue/revoke verifier, import evidence, resume verification; status |
| `server-promotion` | `pa-promote` / `LINODE_PROMOTION_PRIVATE_KEY` | Exact promote, finish-release; status |

`server-acr` uses scoped Azure OIDC, no host SSH. `client-swa` OIDC has only scoped
SWA Reader and private archive-container Blob Contributor; its SWA deployment
secret remains environment protected. Verification/promotion identities read the
private archive. All environments are main-only; server-linode/server-promotion
require the explicit reviews above. Preserve strict installed host trust.

## Application rollback and verification resume

```powershell
gh workflow run server.yml --ref main -f operation=rollback
```

The status capability resolves the recorded known-good when a candidate is
pending, otherwise previous-good. The packet contains the saved manifest and
matching config/schema; it is not a new build of that old source. A new rollback
ACK_RESET is mandatory before its API restart. Restored code remains pending
until fresh gameplay, credential revocation, distinct PROMOTE and reopening pass.
Never downgrade or restore a database to roll back application code.

For expired proof or an interrupted verification/promotion, first inspect
structured status, then rerun the actual verification job and dependents with
`gh run rerun RUN --job VERIFY_JOB_ID`. A genuine later attempt may resume the
same established candidate with fresh verifier IDs/proof and a new PROMOTE.
Do not rerun deployment or change a packet/proof timestamp. If an unapproved
reset packet expires while the host is idle, cancel only that unstarted attempt
and rerun the actual resolve job to obtain a new execution/packet/decision.
Inspect every uncertain mutation before retrying.

Pilot-only legacy root commands remain available for isolated work. They are
not the production CI interface; production requires its matching controller
lease, generated evidence and fresh review controls.

## Interrupted execution and recovery

Lost SSH or killed execution is an unknown outcome. Do not launch another release. The protected `transaction` file records old/candidate directories; it blocks further operations until inspected. Use LISH if SSH is unavailable. Under `flock /var/lib/pokerattack/deploy.lock`, inspect only that target's Compose container, image, readiness and release snapshots. Stop a failed candidate, restart the recorded old pair using its own `release.env`, and verify health/public gameplay before correcting pointers and archiving the transaction file. Do not print app.env. If no recorded old pair exists, stop the failed candidate and retain maintenance for operator inspection; retired ACA is not a recovery authority. Failed recovery retains the transaction; do not remove it merely to make the next job green. No prune/reboot/shared-service stop is a recovery step.

## Bootstrap history, artifacts and monitoring

The [bootstrap contract](../release/BOOTSTRAP.md) describes the completed K02
root-only transition and immutable legacy recovery bundle. Its static maintenance
publication and first-client acceptance exception are historical bootstrap
mechanics, not routine release steps. ACA is retired and cannot be reactivated
as a recovery authority.

GitHub release/test/image artifacts are retained for 30 days. The private Azure
Blob archive (`pareleasearchive1f287758`, `pokerattack-client-releases`) preserves
accepted exact client archives/manifests and CAS current/previous history, with
versioning and 30-day delete retention. No public access, shared keys or SAS
publication is used. Do not replace an accepted artifact with a rebuild.

The single [Linode Docker Containers dashboard](https://gallantpuffin3113.grafana.net/d/linode-docker-containers)
is installed and live-verified, alongside the existing Linux host dashboard.
Select `pokerattack-production-api-1` to compare full image/digest/source and
container incarnation with release evidence. Docker health Not configured is
not Healthy; fixed application readiness is separate. Stale/missing/failed
collection is Unknown, never a green zero. This is visibility, not an additional
release gate. See [collector installation/rollback](../monitoring/README.md).

## Credential coordination

WP01 found shared postgres credentials in PokerAttack and ExpenseTracker ACA, and a TransitJazz postgres connection. Provision/rehearse PokerAttack scoped roles first, then inventory and update every actual shared credential consumer before a coordinated superuser-password rotation. Take protected recovery snapshots and verify each application after the change. Do not rotate postgres while leaving a co-tenant on the old password. The earlier disclosed credential is still pending coordinated rotation; image exclusion alone does not resolve it.
