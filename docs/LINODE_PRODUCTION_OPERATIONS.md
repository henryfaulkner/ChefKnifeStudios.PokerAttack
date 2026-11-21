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

Follow [release tooling](../deploy/linode/README.md): immutable digest and exact config hash, fresh explicit maintenance acknowledgment, host-wide lock, pre-pull, bounded readiness, then matching public gameplay evidence and promotion. A candidate is never known-good based only on local readiness. A failed deploy restores the previous active pair and still returns failure. A pending candidate can be rolled back to known-good; a verified release can roll back to previous-good when that pair exists. K05 established a verified previous-good pair; always read live status before selecting recovery.

For recovery of the current known-good process without a new release, inspect the recorded `active`/`known-good` pair under the host lock and run its existing Compose `release.env` with project `pokerattack-production`, preserving the reviewed digest, protected env, limits and loopback port. Verify public health/gameplay afterward. Do not improvise a new image/config or remove an interrupted transaction marker without inspecting it. Preserve PostgreSQL, shared Caddy/TLS volumes and TransitJazz. Never use global Docker restarts, volume prune or automatic schema downgrade as app recovery.

## Installed GitHub automation

Production CD is enabled on protected `main`. PR14 activated separate client and
server workflows after K02 bootstrap; the stale PR12 is not the release source.
Required `Client validation` and `Server validation` checks cover PR changes;
production dispatch is main-only. `migration-ci.yml` is manual-only build/test
reference, with no deployment/publication authority. Legacy Azure definitions
39/40/41 remain disabled; do not enable ACA deployment or automatic migrations.

K04 client workflow44656069326/3 and K05 server workflow44657798365/2 completed
actual production delivery. K05 source is `c01088c7d289c7329126294268bde3d34bdad426`,
API digest `sha256:37b1cb0775efdc0c85136b6bbd342bd942c4b98002f85eff51440472ba173fa7`,
config hash `7c830479833d8b9a8ed119a75a73069fd70c1ce510f67c060cfc365f9936355f`.
Its prior known-good is API `sha256:464ff4271d74764de98eca11058b19c892b82099d82aa9edf911f3584e21da40`
(source `dec2fb0c13d928a3c3df3031c5a8ef3455d0d02d`, same config). K06 restored and explicitly promoted the prior verified API/client pair through
actual workflow44911036001/1 on2026-11-21. Current API is464ff427... (full digest above),
source dec2fb0..., container60bfa134e2dfa60657b32edb8f56563fa529e968ddbdbbc3853cd8babeebe971;
previous-good is37b1cb... . Current accepted client is original43884112818/1 artifact14233913243,
archive32bfd2e0402846236dc074e09d382945c71acf555105a6a1834e6b46cba2c4d5; previous accepted
client is K04 archivee8915a... . Both sessions and admission are normal, no pending
release/lease remains, and no database migration ran. Controllers were corrected
and reviewed in PR15, source0b354783d91babc76796381d0f38414f45cdd9e7; this controller
source is distinct from the restored image provenance. Always reconcile
[current checklist evidence](DEPLOYMENT_AUTOMATION_CHECKLIST.md) and live status
before a new operation.

Use the [release README](../deploy/linode/README.md) for exact dispatch, artifact
restoration, environment/key capability, reset/promotion and resume commands.
The routine command entry points are:

```powershell
gh workflow run client.yml --ref main -f operation=release
gh workflow run server.yml --ref main -f operation=release
gh workflow run server.yml --ref main -f operation=rollback
```

Client restore additionally requires the four exact accepted run/attempt/artifact/
archive-checksum inputs documented there. Both workflows share concurrency group
`pokerattack-production-release` with no active cancellation, plus durable host
leases. Avoid multiple pending requests; GitHub can replace an older pending run.

Server publication captures tested API/migration digests in ACR. Migration image
publication is not execution. Review the actual reset packet, then supply fresh
`ACK_RESET PACKET_SHA256 RUN/ATTEMPT` at `server-linode`; its original window must
cover runner start/drain. CI closes admission, deploys API only and leaves pending.
Real production gameplay must pass, its own sessions close, verifier credential be
revoked and exact evidence imported before a separate fresh
`PROMOTE EVIDENCE_SHA256 RUN/ATTEMPT` at `server-promotion`. CI then promotes,
reopens, checks public browser/SignalR and records complete. A ready container or
ACR push is insufficient. Human acknowledgment is release-specific, never reused.

All six environments are main-only. `server-acr` uses scoped Azure OIDC;
`client-swa` uses its scoped SWA Reader/archive writer and environment upload
secret; verifier/promotion OIDC only reads the private archive. Host keys are
separate `pa-status`, `pa-client`, `pa-deploy`, `pa-evidence`, `pa-promote`
capabilities with forced commands and no shell/general sudo/Docker group.
The configured `LINODE_KNOWN_HOSTS` must retain trusted host identity. Root access
is for inspected provisioning/recovery, not a replacement routine CI authority.

An interrupted release remains closed until its actual state is reconciled.
Query scoped `status production`; never clear a lease/transaction to make a run
green. Expired proof needs an actual verification-job rerun and new proof/PROMOTE,
not timestamp editing or another deployment. Rollback selects recorded known-good
when pending, otherwise previous-good, and requires its own fresh reset decision,
gameplay and promotion. Application rollback never restores/downgrades a database.

Accepted exact client archives/manifests and CAS pointers are private in Azure
Blob account `pareleasearchive1f287758`, container `pokerattack-client-releases`.
GitHub artifact retention is30days; archive versioning/deletion retention is30days.
Do not publish tokens, env/config files or private gameplay/browser contexts in
source or CI artifacts. Legacy root bootstrap/static maintenance procedures are
historical recovery references; normal admission is now application-controlled.

## Backups and monitoring

Host `pokerattack-backup.timer` captures all four shared application databases, globals, host/proxy/PokerAttack configs and release state twice daily, retaining seven days. Manual capture: `systemctl start pokerattack-backup.service`; check service Result/ExecMainStatus and `/var/lib/pokerattack/latest-backup`. The operation uses the same lock as releases.

Windows task `PokerAttack-OffHostBackup` transfers completed snapshots hourly/at logon while the owner is logged on. Private directory: `C:\Users\hfaul\PokerAttack-Backups\recurring`. Files are CurrentUser DPAPI encrypted with checksum/decryption verification. Recovery requires the Windows profile/key material. A powered-off/logged-out workstation cannot provide continuous off-host freshness. See WP06 evidence and `deploy/linode/restore-shared-snapshot.py` for the measured isolated shared-database restore procedure; decrypt only into a protected directory first.

The shared [Linode Docker Containers dashboard](https://gallantpuffin3113.grafana.net/d/linode-docker-containers)
is installed/live-verified with a container dropdown, full image/source/digest,
state, configured Docker health, separate fixed API readiness, lifecycle,
resources and collection freshness. Collector timer runs every30seconds and the
existing Alloy scrape every60seconds. Current data expires after180seconds;
stale/missing/failed collection is Unknown. Docker health Not configured never
means Healthy. Host networking is Shared/unavailable rather than invented zero
traffic. The dashboard provides visibility and is not an extra release gate.
See [collector installation/rollback](../deploy/monitoring/README.md) and
[J live evidence](deployment-evidence/J03-J04-complete-live-verification.json).

Existing Grafana Linux dashboard `https://gallantpuffin3113.grafana.net/d/nodes`,
instance `expense-tracker-vm`, job `integrations/node_exporter`, remains preserved
and measures the entire shared host. PostgreSQL is host-managed, not a Docker
container. Roll back collector changes additively; preserve host metrics and
remote-write authentication, and do not restart application containers.

External HTTPS/certificate/backup alert delivery and coordinated host reboot
rehearsal remain unrelated operational follow-ups, outside this deployment plan.
Shared-password rotation remains explicitly deferred; PokerAttack's scoped role
is separate, while the old incident is unresolved. No added observability program
or observation gate is required for deployment completion. K06 live restoration, API rollback, fresh gameplay, explicit promotion/reopening
and serialization have passed. Final publication status is recorded in the
[deployment completion report](DEPLOYMENT_AUTOMATION_COMPLETION.md).
