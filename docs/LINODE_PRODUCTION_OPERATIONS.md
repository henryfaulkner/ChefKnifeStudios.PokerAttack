# PokerAttack production operations

Production API and SignalR share `https://api.playpokerattack.com/` on Linode `170.187.142.115`. Client remains Azure Static Web Apps at apex/www `playpokerattack.com`; ACR remains `chefknife.azurecr.io`. The API binds only `127.0.0.1:8081`, uses `pokerattack_runtime` in `expensetrackerdb`, schema `PokerAttack`, and runs at 0.25 CPU / 512 MiB. No production migrations were required. The isolated pilot is stopped.

The owner chose immediate ACA retirement and deferred shared postgres password rotation on 2026-11-14. Do not try to reactivate a deleted ACA revision. Encrypted historical Azure definitions and the old client ZIP are recovery references, not a currently available server. Recover application failures on Linode using the recorded image/config pair; recover database failures from the shared backups. Never restore an old database merely to roll back app code.

## Access and status

Use the existing trusted root SSH identity `~/.ssh/id_ed25519_linode_us_southeast`; preserve StrictHostKeyChecking. Recovery console: `ssh -t henryfaulkner-cks@lish-us-southeast.linode.com expense-tracker-vm`.

On the VM:

```bash
pokerattack-deploy status production
curl --fail --max-time 5 http://127.0.0.1:8081/health/ready
curl --fail --max-time 5 http://127.0.0.1:8081/_ops/status
docker stats --no-stream pokerattack-production-api-1
```

Public live/ready checks use the production HTTPS hostname. Public `/test/*` and `/_ops/*` return 404. Session counts are private; relational game counts cannot measure live sessions. App memory is volatile: deployment/restart ends matches and requires fresh sessions/reload.

## Release and application recovery

`/var/lib/pokerattack/production/{active,known-good,previous-good,pending}` point to protected image/config snapshots. `/etc/pokerattack/production/configs/<sha>.env` and release snapshots contain credentials; never print or commit them. Compose/validator live under `/opt/pokerattack`, root-only registry auth under `/etc/pokerattack/registry`.

Follow [release tooling](../deploy/linode/README.md): immutable digest and exact config hash, fresh explicit maintenance acknowledgment, host-wide lock, pre-pull, bounded readiness, then matching public gameplay evidence and promotion. A candidate is never known-good based only on local readiness. A failed deploy restores the previous active pair and still returns failure. A pending candidate can be rolled back to known-good; a verified release can roll back to previous-good when that pair exists. The first production release has no older Linode production pair yet.

For recovery of the current known-good process without a new release, inspect the recorded `active`/`known-good` pair under the host lock and run its existing Compose `release.env` with project `pokerattack-production`, preserving the reviewed digest, protected env, limits and loopback port. Verify public health/gameplay afterward. Do not improvise a new image/config or remove an interrupted transaction marker without inspecting it. Preserve PostgreSQL, shared Caddy/TLS volumes and TransitJazz. Never use global Docker restarts, volume prune or automatic schema downgrade as app recovery.

For client releases, build Release, run `node deploy/prepare-production-client.mjs PUBLISHED_WWWROOT NEW_ARTIFACT_DIRECTORY`, then the explicitly authorized `py deploy/publish-client.py release ARTIFACT_DIRECTORY`. Both URLs must be the production origin and SWA header `prod`; verify apex/www served settings and fresh browser WebSockets. The publisher obtains the SWA token in memory and passes it only via child environment. Maintenance publication uses `py deploy/publish-client.py maintenance deploy/maintenance`.

Build/test runs in GitHub Actions. Production deployment is currently the controlled manual release command; GitHub CD environment/key/protection setup remains unfinished. Legacy Azure definitions 39/40/41 are disabled and must not be enabled to deploy this app. Source changes are in PR #12; a production artifact is not evidence that the PR was merged.

## Backups and monitoring

Host `pokerattack-backup.timer` captures all four shared application databases, globals, host/proxy/PokerAttack configs and release state twice daily, retaining seven days. Manual capture: `systemctl start pokerattack-backup.service`; check service Result/ExecMainStatus and `/var/lib/pokerattack/latest-backup`. The operation uses the same lock as releases.

Windows task `PokerAttack-OffHostBackup` transfers completed snapshots hourly/at logon while the owner is logged on. Private directory: `C:\Users\hfaul\PokerAttack-Backups\recurring`. Files are CurrentUser DPAPI encrypted with checksum/decryption verification. Recovery requires the Windows profile/key material. A powered-off/logged-out workstation cannot provide continuous off-host freshness. See WP06 evidence and `deploy/linode/restore-shared-snapshot.py` for the measured isolated shared-database restore procedure; decrypt only into a protected directory first.

Existing Grafana node dashboard: `https://gallantpuffin3113.grafana.net/d/nodes`, instance `expense-tracker-vm`, job `integrations/node_exporter`. It measures the shared host. External HTTPS/certificate/backup freshness alert delivery, coordinated host reboot/interruption rehearsal and full GitHub deployment automation remain open follow-ups. Do not report them as verified. Shared-password rotation remains explicitly deferred; PokerAttack's new role is separate, while the old incident is unresolved.
