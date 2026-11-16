# Docker snapshot metric contract

Local implementation contract; installation and live evidence remain I/J.
Namespace `linode_docker_`. No alerts, application telemetry or deployment gates.
The root service samples every 30 seconds; existing Alloy scrapes every 60 seconds.
Fresh means snapshot age below 180 seconds. UI refresh is 30 seconds; rate window
is four minutes. Brief transitions between samples can be missed.

Every container metric has `host` and full 64-hex `container_id`. This identifies
one incarnation; replacement with the same name creates a separate series.
The installed service uses the fixed logical host `expense-tracker-vm`, matching
the existing Alloy instance and dashboard default. It does not change the OS
hostname, which can be `localhost`.
`container_info` additionally maps `container`, `compose_project`,
`compose_service`, `configured_image`, `image_id`, `registry_digest`,
`source_revision` and `network_mode`. Only these fields are allowed. Configured
image is the container's requested tag/digest reference; local `image_id` is the
Docker image identity; `registry_digest` is a matching repository digest from
image inspection, never inferred from the local ID. No matching digest is the
literal `Unavailable`; missing full OCI revision is `Not provided`. No shortened
digest labels. Multiple matching digests select the configured digest if present,
otherwise the lexically first matching repository digest. Other repositories are
not mislabeled as the container's repository.

Labels are escaped for Prometheus (backslash, quote, newline); controls are
rejected. Host/name/Compose labels have at most 128 characters; image references
512; digests 256; revision is 40 lowercase hex or the sentinel. Empty Compose
labels mean not configured. Docker inventory is capped at 256 containers, each
response at 4 MiB, persistent sanitized cache at 1 MiB, output at 4 MiB. Image
metadata cache is keyed by immutable image ID, retained only for discovered
images, and refreshed after five minutes (repository digest metadata can change).
No environments, arbitrary labels, health output, mounts or credentials escape.

All entries below are gauges except the explicitly named counters. HELP/TYPE
lines in each snapshot repeat this contract. Missing resource/timestamp samples
mean unavailable; never turn unavailable input into zero.

| Metric suffix | Unit / meaning |
| --- | --- |
| `container_info` | 1, allowlisted inventory mapping above; includes stopped containers |
| `container_identity_timestamp_seconds` | Unix seconds with the same metadata labels as info; match current snapshot time to avoid old metadata surviving backend lookback |
| `container_sample_timestamp_seconds` | Latest per-incarnation attempt in Unix seconds; must equal current host snapshot time for current-state queries |
| `container_collection_success` | 1 for complete inspect/image/stats, 0 if this container failed; other containers remain usable |
| `container_state` | 0 Unknown, 1 Created, 2 Running, 3 Paused, 4 Restarting, 5 Removing, 6 Exited, 7 Dead |
| `container_health` | 0 Not configured, 1 Healthy, 2 Unhealthy, 3 Starting, 4 Unknown; independent of application readiness |
| `container_created_timestamp_seconds`, `container_started_timestamp_seconds`, `container_finished_timestamp_seconds` | Unix seconds; absent if Docker never set the timestamp |
| `container_restarts` | Docker restart count for this incarnation, not historical deployments |
| `container_oom_killed` | Boolean Docker OOM flag for this incarnation |
| `container_exit_code` | Last exit code, only for exited/dead containers |
| `container_cpu_limit_cores` | Configured CPU quota in cores; 0 explicitly means Unlimited quota (CPU affinity is not quota) |
| `container_memory_limit_bytes` | Configured hard limit; 0 explicitly means Unlimited |
| `container_cpu_usage_seconds_total` | Counter: total CPU nanoseconds / 1e9; rate is used cores; percentage of one core is rate * 100 |
| `container_memory_usage_bytes`, `container_memory_working_set_bytes` | Docker usage and usage minus inactive file cache; working set omitted if cache input unavailable |
| `container_network_attributable` | 0 for host/container shared namespaces, 1 otherwise; shared network counters omitted |
| `container_network_receive_bytes_total`, `container_network_transmit_bytes_total` | Counters summed across interfaces only when attributable |
| `container_block_read_bytes_total`, `container_block_write_bytes_total` | Counters summed across Docker block I/O entries; missing I/O is unavailable |
| `container_readiness` | 0 Not configured, 1 Ready, 2 Not ready (HTTP non-200), 3 Unknown (connection failure); only fixed PokerAttack loopback mapping |
| `collection_success` | Host: 1 only if enumeration and every discovered container succeeded; 0 for partial or total failure |
| `inventory_success` | Host: 1 if current enumeration succeeded; 0 for daemon/permission/parse/timeout failure |
| `snapshot_timestamp_seconds` | Host: last collection attempt time; not evidence of success |
| `last_success_timestamp_seconds` | Host: last completely successful collection; never advanced on failure, 0 before first success |
| `collection_duration_seconds` | Host: bounded collection wall time |

Current panels require inventory success, snapshot age below 180 seconds and
per-container success. Container and identity timestamps must equal the host
snapshot timestamp: this rejects removed containers and superseded image labels
even if an ingestion path delays stale markers. A partial result shows the failing container Unknown and
keeps successful peers visible, while overall collection health is failed and its
last-success time remains unchanged. A daemon failure retains inventory identity
for diagnosis but hides all current states. A stopped collector leaves the prior
snapshot intact and ages out. Removal disappears from the next successful
inventory; historical timelines retain the old ID. Empty successful inventory is
possible only after a valid empty Docker response. A disappeared container during
inspection is Unknown in that cycle and removed on the next inventory.

CPU quota percentage and memory limit percentage exist only with a positive
configured limit. Host network means Shared/unavailable, never zero bytes. No
healthcheck means Not configured, never Healthy. The optional fixed PokerAttack
readiness mapping will be separate from Docker health; no container label can
choose a URL or command. This dashboard never establishes gameplay correctness.

References: [Docker Engine API](https://docs.docker.com/reference/api/engine/),
[Alloy Unix textfile collector](https://grafana.com/docs/alloy/latest/reference/components/prometheus/prometheus.exporter.unix/#textfile).
