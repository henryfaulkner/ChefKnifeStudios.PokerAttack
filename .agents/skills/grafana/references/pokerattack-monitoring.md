# Existing shared-VM monitoring

The Grafana skill entrypoint is copied from TransitJazz. Use its read-only boundary. This local reference identifies the existing monitoring context; verify availability before interpreting it as current production evidence.

- Correct Grafana stack: `https://gallantpuffin3113.grafana.net`.
- Existing `gcx` context: `transitjazz`; use `--context transitjazz` rather than changing global defaults. Inspect command help for supported read-only operations; `gcx config check --context transitjazz` is this CLI's connectivity/authentication diagnostic.
- Prometheus datasource UID: `grafanacloud-prom`, named `grafanacloud-gallantpuffin3113-prom`.
- Built-in Linux overview UID: `nodes`, URL `https://gallantpuffin3113.grafana.net/d/nodes`.
- Scope: instance `expense-tracker-vm`, job `integrations/node_exporter`. The earlier job `integrations/unix` has historical data; do not silently merge old/new collection windows.
- Overview variables: `prometheus_datasource=grafanacloud-prom`, `instance=expense-tracker-vm`, `job=integrations/node_exporter`; `cluster`, hidden `env` and hidden `site` can select All. For saved PromQL, resolve their regex values to `.*` when selecting All. Use a 4-minute rate interval with the verified 60-second scrape cadence; inspect current datasource/cadence if changed.
- Saved panel IDs: 11 CPU stat, 12 per-CPU timeseries, 15 memory stat, 16 memory breakdown, 19 disk capacity, 21 network traffic, 22 network errors/drops. IDs are preferable because CPU/memory panel titles are duplicated.
- Newer Grafana APIs can return dashboard schema v2: `spec.elements['panel-11'].spec.data.spec.queries`. Retrieve the current schema rather than assuming legacy `panels[].targets`.

Prefer the registered connector if it reaches this stack. A connector exposing only datasource `phh-postgres` belongs to the separate Personal Health Hub Grafana instance; it cannot establish this VM's node-exporter metrics. Use the already configured `gcx` fallback in that case. Never read credential files or use raw HTTP as an authentication workaround.

The Linux dashboard measures the entire shared VM: PostgreSQL, TransitJazz, Alloy, Caddy and other workloads. It does not prove PokerAttack's image limits, readiness, active sessions, public SignalR delivery, backups or external alert receipt. Keep the PokerAttack per-app allocation 0.25 CPU / 512 MiB separate. A short healthy host sample is not a sizing gate or runtime soak.

When checking the dashboard, preserve a supplied URL's range/variables; otherwise the saved overview defaults to 30 minutes. Record exact UTC bounds, query step, datasource, resolved variables, returned sample coverage and any gaps. A rendered PNG can verify visible panels separately from independently executed PromQL. Do not edit dashboards, exporters or alert rules as part of this check.
