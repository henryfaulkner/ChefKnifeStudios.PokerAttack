# Shared Linode Docker snapshot

Installed and live-verified in I/J (see checklist evidence). One collector and one **Linode Docker Containers**
dashboard use the existing Alloy Unix exporter, 60-second scrape and authenticated
forwarding path. No public metrics listener, datasource, container healthcheck or
application restart is added. PostgreSQL is host-managed and is not invented as a
Docker dropdown entry. See [metric contract](METRICS.md) and the authoritative
[checklist](../../docs/DEPLOYMENT_AUTOMATION_CHECKLIST.md).

The root-only oneshot service runs every 30 seconds, uses only allowlisted Docker
GET requests over its Unix socket, and has a 20-second request budget, 22-second
hard alarm and 25-second systemd bound. It is capped at 64 MiB / 10% of one CPU.
It never executes a command in a container. Only the service has Docker access;
Alloy reads mode-0644 `docker.prom` from the root-owned mode-0755 directory
`/var/lib/linode-docker-metrics`. Private mode-0600 cache contains only sanitized
image metadata. Neither deployment roles nor applications gain socket/group
access. The service holds its own nonblocking collector lock, separate from
release/backup locks, and cannot block API deployment.

One optional, fixed readiness mapping checks `pokerattack-production-api-1` at
`http://127.0.0.1:8081/health/ready` with a one-second timeout, no redirects or
environment proxies. Other containers say Not configured. Container labels
cannot choose URLs or commands. Docker health and readiness are separate fields.

Installation (I/J, after H rehearsals) places the root-owned script under
`/opt/linode-docker-metrics`, units under `/etc/systemd/system`, and validates both
with `systemd-analyze verify`. Stage the current Alloy configuration in a private
host directory outside the repository; never print or publish its authenticated
blocks. Apply `alloy_textfile.prepare` to that exact current configuration: it
adds `textfile { directory = "/var/lib/linode-docker-metrics" }` to
`prometheus.exporter.unix "integrations_node_exporter"` and appends only
`|linode_docker_.*` to its existing keep regex. The existing scrape interval,
discovery, host-metric allowlist, otelcol forwarding and remote-write credentials
remain byte-for-byte unchanged. Unexpected/already configured components cause
refusal, requiring reconciliation rather than overwriting them.

Run the installed-version `alloy validate` on the staged configuration before an
atomic replacement and Alloy reload. Save exact before/after hashes and protected
host backup reference in installation evidence. Enable only the new timer, sample
once, confirm inventory including stopped containers, and verify the namespace in
the intended existing datasource. G/H local checks do not claim live ingestion.

Rollback stops/disables only `linode-docker-snapshot.timer` and waits for its
bounded service. Stage `alloy_textfile.rollback(current_config)` in the private
directory and validate before replacement/reload; this removes only the exact
Docker directory/namespace additions, retaining subsequent unrelated config
changes. If additions differ, stop and reconcile rather than restore an old full
config over unrelated changes. Remove only this collector's owned metric/cache
files after Alloy no longer reads them. Do not delete shared exporter state,
restart Docker, alter host dashboards or touch application containers.

Local checks: `python -m unittest discover -s deploy/monitoring -v` in Linux.
Disposable lifecycle/overhead and actual dashboard query checks passed H04/J; see
[the recorded live evidence](../../docs/deployment-evidence/J03-J04-complete-live-verification.json).
The dashboard is runtime visibility, never a reset, promotion, readiness or
automatic-rollback gate.
