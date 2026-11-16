# PokerAttack release tooling

Repository automation is being implemented against [the release contract](../release/CONTRACT.md) and [the authoritative checklist](../../docs/DEPLOYMENT_AUTOMATION_CHECKLIST.md). New role wrappers, admission mounts and lease controls are **local source only until I03/I04**, and the complete toolset must pass H before installation. The live installed snapshot remains the one recorded in production operations/evidence. The repository's legacy combined SSH wrapper now rejects production; future production keys must use the separate status/deployment/evidence/promotion capabilities. Repository production replacement also requires the matching controller lease, while the installed historical root operator path remains the transitional recovery channel until bootstrap.

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

## Controlled release interface

All examples below are on-host root commands. Dedicated CI SSH keys use the forced wrapper with `restrict`/no forwarding/no PTY; sudoers permits only `/usr/local/sbin/pokerattack-deploy`. Provisioning configs, maintenance acknowledgement and verification evidence is an operator task, outside that key's permissions. WP09 adds GitHub environment and branch controls.

1. Review config and image provenance. Registry is fixed to `chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi`; only a lowercase sha256 digest is accepted. First install pull-only registry access with a separate protected Docker credential directory owned by root.
2. Enter client maintenance and observe/drain games or explicitly end them. Create `/etc/pokerattack/<target>/maintenance` mode 0600 containing `sessions-reset-approved`. It expires after 30 minutes; it records a deliberate reset, not a claim that matches survived. Production ACA stopping and client maintenance remain WP08 operator steps; the script cannot infer those from local readiness.
3. Run `pokerattack-deploy deploy pilot sha256:<64hex> <config-sha256>` (or production). It validates config, pre-pulls before stopping, validates Compose, snapshots the pair, checks maintenance, then stops only that target's API and waits at most 30 readiness attempts with bounded curls. Runtime image ID and exact enforced limits are checked. A failed candidate restores the recorded active pair when possible and returns failure even if recovery succeeds.
4. Leave the candidate pending. Perform public TLS, actual SignalR, multiplayer/solo and private-message isolation checks. Save reviewed evidence as root-owned 0600 `/etc/pokerattack/<target>/verification/<sha256>.json`. Fields: `digest` (`sha256:...`), `config_sha256`, 40-hex `source_sha`, `verified_unix`, and booleans `https`, `signalr`, `multiplayer`, `solo`, `private_message_isolation`, all true. Include supporting log/artifact references in additional fields. Hash the exact file bytes. It expires after 30 minutes.
5. Run `pokerattack-deploy promote pilot sha256:<64hex> <evidence-sha256>`. Only the matching active pending pair can be promoted after fresh evidence and a renewed local identity/readiness check. Local health alone cannot create known-good. A pending pair blocks a competing deployment.

`pokerattack-deploy status pilot` reports identity/local readiness without secrets. `rollback pilot` requires fresh maintenance acknowledgement and restores known-good when pending, otherwise previous-good. Rollback itself remains pending external verification. Never automatically downgrade/restore the database during application rollback.

## Interrupted execution and recovery

Lost SSH or killed execution is an unknown outcome. Do not launch another release. The protected `transaction` file records old/candidate directories; it blocks further operations until inspected. Use LISH if SSH is unavailable. Under `flock /var/lib/pokerattack/deploy.lock`, inspect only that target's Compose container, image, readiness and release snapshots. Stop a failed candidate, restart the recorded old pair using its own `release.env`, and verify health/public gameplay before correcting pointers and archiving the transaction file. Do not print app.env. If no recorded old pair exists, stop the failed candidate and retain maintenance for operator inspection; retired ACA is not a recovery authority. Failed recovery retains the transaction; do not remove it merely to make the next job green. No prune/reboot/shared-service stop is a recovery step.

## Automation and baseline introduction

The new source controls are tracked in the [deployment checklist](../../docs/DEPLOYMENT_AUTOMATION_CHECKLIST.md).
Source checks and disposable rehearsals do not mean the production tools or
workflows are installed/enabled. The [bootstrap contract](../release/BOOTSTRAP.md)
describes the root-only publication sequence, immutable legacy recovery bundle,
provisional first-client verification, ordinary manual workflow dispatch, and
lost-response reconciliation. Preserve the established root recovery channel;
capability-separated CI keys cannot invoke that transitional exception.

ACA has been retired and is not an available recovery authority. Recover from
the recorded Linode image/config and its matching saved tooling; an unready first
candidate without a recorded recovery pair must remain closed for inspection.

## Credential coordination

WP01 found shared postgres credentials in PokerAttack and ExpenseTracker ACA, and a TransitJazz postgres connection. Provision/rehearse PokerAttack scoped roles first, then inventory and update every actual shared credential consumer before a coordinated superuser-password rotation. Take protected recovery snapshots and verify each application after the change. Do not rotate postgres while leaving a co-tenant on the old password. The earlier disclosed credential is still pending coordinated rotation; image exclusion alone does not resolve it.
