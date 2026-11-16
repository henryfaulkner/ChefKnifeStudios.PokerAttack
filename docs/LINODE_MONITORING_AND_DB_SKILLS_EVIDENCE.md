# Grafana and PokerAttack database skills - 2026-11-14

User requested the TransitJazz Grafana/database skills as migration context and a local PokerAttack database version. Created repository-discoverable `.agents/skills/grafana` and `.agents/skills/pokerattack-db`. No external resource, database data, dashboard, credential or permission was modified. Existing untracked `.claude/skills` was left intact.

## Source and local skills

- Grafana source: `C:\Projects\ChefKnifeStudios.TransitJazz\skills\grafana\SKILL.md`, copied with its UI metadata. Added a local monitoring reference identifying the correct stack, dashboard, datasource, variables and shared-host scope.
- Database source: TransitJazz's `SKILL.md`, `references/connection.md` and `scripts/query.ps1`. Adapted the helper to `PokerAttackDB` and added an actual-database guard and output-encoding restoration. New references describe PokerAttack's own models, EF filters, producer code and shared history. Transit statistics definitions and historical capture rules were not copied into PokerAttack.
- New trusted-SSH query helper uses the existing verified VM/root identity and PostgreSQL Unix socket, with no database password transfer. The supplied TransitJazz skill explains app connection resolution; VM SSH address/key/fingerprint come from independent migration inventory rather than an invented field in that skill.
- Both local skill frontmatters passed `skill-creator/scripts/quick_validate.py`; both helper scripts passed PowerShell parsing.
- Narrow read of `/run/cloud-init/instance-data.json` on the existing trusted VM returned platform/cloud `akamai`, instance ID `79395147`, region `us-southeast`. This resolves the requested identity/region without browser access; it is provisioning metadata, not a live Cloud Firewall or billing-plan query. No other metadata fields or user-data were printed.
- Helpers were executed against the selected production database using reviewed SELECT/catalog statements. Each returned `expensetrackerdb`, user `postgres`, `transaction_read_only=on`, `repeatable read`, timezone UTC. The privileged socket route is not proof of app least privilege.
- The app-credential helper independently connected from the workstation with the current ACA connection held only in process memory; `pg_stat_ssl` reported TLS 1.3. No explicit certificate-verification policy was present in the current connection string, so observed encrypted transport does not establish verified server identity.
- Quoted apostrophe/semicolon parameters, empty/null parameters, refusal of an expected-database mismatch, refusal of psql command input, and restoration of temporary connection/parameter environment and output encoding after success/failure all passed. No write/DDL probe was run against production.
- Live schema matches the three relational models: 178 Games, 17701 Rounds, 44655 RoundScores; all rows currently non-deleted. Timestamps are PostgreSQL timestamp-with-time-zone. EF history is in shared `public.__EFMigrationsHistory`. These point-in-time physical/non-deleted counts are not active sessions or completion counts.

## Grafana investigation

The registered connector exposes only `phh-postgres` and found no Linux dashboards. That is a different stack, so it cannot establish shared-VM metrics. The existing `gcx` context `transitjazz` passed its configured authentication/connectivity check and reached `https://gallantpuffin3113.grafana.net`; no credential files or raw HTTP authentication workaround were used.

Dashboard: [Linux node / overview, UID nodes](https://gallantpuffin3113.grafana.net/d/nodes?var-prometheus_datasource=grafanacloud-prom&var-job=integrations%2Fnode_exporter&var-instance=expense-tracker-vm&from=1794665400000&to=1794667200000&timezone=utc).

- Datasource UID `grafanacloud-prom` (`grafanacloud-gallantpuffin3113-prom`).
- Requested exact UTC window **2026-11-14 14:10:00Z through 14:40:00Z**, using the saved dashboard's 30-minute default, queried at one-minute resolution.
- Resolved variables: `instance=expense-tracker-vm`, `job=integrations/node_exporter`, `env/site/cluster=.*`, rate interval `4m`, range `30m`. Render uses All selections for env/site/cluster.
- Read current schema-v2 saved panel queries, then independently executed panel 11 CPU stat, panel 15 memory stat, both panel 19 capacity queries, both panel 21 traffic queries, and panel 7 memory total. Also queried `up{job="integrations/node_exporter",instance="expense-tracker-vm"}` and `time() - timestamp(up{job="integrations/node_exporter",instance="expense-tracker-vm"})`.

| Result | Latest value at 14:40 UTC | Returned coverage |
| --- | ---: | --- |
| CPU usage, panel 11 stat | 3.2667% | 21 samples, 14:20-14:40 UTC |
| Memory usage, panel 15 | 42.4862% | 22 samples, 14:19-14:40 UTC |
| Total host memory, panel 7 | 2063577088 bytes (1.92 GiB) | 22 samples |
| Root filesystem size, panel 19 TOTAL | 52212428800 bytes (48.63 GiB) | 22 samples |
| Root filesystem available, panel 19 FREE | 39308754944 bytes (36.61 GiB) | 22 samples |
| eth0 receive, panel 21 A | 943440.27 bits/second | 21 samples |
| eth0 transmit, panel 21 B | 37393.60 bits/second | 21 samples |
| Exporter up | 1 for every returned sample | 22 samples, 14:19-14:40 UTC |
| Last scrape age at query evaluation | 59.394 seconds | 22 samples |

The new job has no returned samples for the first approximately nine minutes of the requested range; rate panels start another minute later. Do not treat this as a complete 30-minute baseline or silently fill missing data. It matches the previously documented exporter activation time, but this query alone does not diagnose the cause of every gap.

Rendered the fixed-window overview using `gcx dashboards snapshot nodes` with explicit datasource/instance/job and All variables. Inspected the PNG: CPU, memory, filesystem, disk-I/O, load and network panels render; header selects the intended VM/datasource. CPU stat displays 3.3%, memory stat 42.5%, root available 36.6 GiB. Network error/drop panel displays “No errors”; its filters show only positive error/drop rates, so that message is not a complete alert-history audit. The per-CPU timeseries also shows an early brief spike near the beginning of available data; the stat-panel sampled range is not a bound on every panel or sub-minute observation.

Private raw query definitions, resolved PromQL, metric responses, summary and rendered `nodes.png`: `C:\Users\hfaul\AppData\Local\Temp\pokerattack-grafana-check-20261114`. This temporary evidence directory is not a durable backup.

A review copy of the inspected PNG is saved at `artifacts/linode-migration/linux-overview-20261114-1410-1440-utc.png` (ignored build/artifact directory).

These are whole-VM measurements. They do not verify PokerAttack's resource limits, health, SignalR, backups, external alert delivery or runtime soak. Existing Grafana monitoring access is now verified; provider firewall/recovery-console inventory and the remaining WP01 decisions remain separate gates. WP01/WP02 acceptance status is unchanged by this read-only investigation.
