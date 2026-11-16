# PokerAttack deployment automation plan

Planning date: 2026-11-15. This document proposes implementation; it does not record a deployment or provisioned infrastructure.

Execution tracking: [agent implementation checklist](DEPLOYMENT_AUTOMATION_CHECKLIST.md). Follow its ordered tasks and evidence requirements when implementation is authorized; update checkboxes only after verification.

## Recommended outcome

Use two independently triggered GitHub Actions workflows:

- `.github/workflows/client.yml`: test and publish the Blazor client, deploy the exact artifact to Azure Static Web Apps, and verify the served release.
- `.github/workflows/server.yml`: test and publish API/migration images to ACR, deliver the API digest to Linode through restricted SSH, verify gameplay, and explicitly promote the release.

Carry over TransitJazz's image delivery strategy. Preserve PokerAttack's maintenance/reset acknowledgment, pending release, external evidence, and promotion requirements. A server restart ends active matches; this remains a maintenance release, with one authoritative production API process.

The target is automated execution with deliberate release gates: routine releases require no operator shell commands or hand-authored evidence files, but still require reset approval and a separate promotion decision. Completely unattended push-to-production would change the stated approval policy and is outside this recommendation. The initial milestone automates delivery using the existing operator gates; the later milestones automate the mechanics of those gates without removing them.

## Confirmed starting point

| Area | Observed state | Required work |
| --- | --- | --- |
| Client workflow | `client.yml` already builds .NET 10/WASM, prepares production settings, uploads an artifact, deploys SWA, and checks apex/www settings | Add PR checks, production-preparation tests, release identity, browser checks, durable rollback artifacts, and provision its environment/token |
| Server workflow | No `server.yml`; `migration-ci.yml` runs server tests, disposable-container/browser verification, and release-script tests | Build the production server workflow around those existing checks |
| GitHub configuration | Read-only API inspection returned zero repository environments; repository is public, default branch `main` | Provision actual environments, branch restrictions, credentials, and approvals before enabling CD |
| Existing secrets | Repository secret names include older Azure identity and registry credentials; no SWA or Linode deployment secret names were listed | Inspect permissions before reuse; names alone do not prove credentials work or have suitable scope |
| Linode releases | `deploy/linode/pokerattack-deploy` validates image/config, pre-pulls, uses `flock`, checks readiness and limits, stages pending, and supports promotion/rollback | Preserve state machine; add structured status/provenance and narrowly scoped automation interfaces |
| SSH interface | Wrapper allows `deploy`, `promote`, `rollback`, and `status`; it cannot provision maintenance/evidence/config files | Do not assume workflow YAML can perform those missing operations |
| Browser tests | Existing suite rejects production and uses `/test/*` cleanup and SignalR fencing | Keep it isolated; implement a separate production-safe verification suite |
| Legacy delivery | Azure pipeline triggers are disabled; operations docs record retired ACA and unfinished GitHub CD | Keep GitHub as the delivery owner; correct stale WP09 Azure Pipelines references |

Repository files and GitHub metadata were inspected. Live Linode installation, Azure permissions, environment credentials, and rollback pointers were not audited during planning. The recent six runs returned by GitHub were successful migration CI runs; this is not evidence of production CD.

Production identities documented in the repository:

- SWA client: `https://playpokerattack.com`, `https://www.playpokerattack.com`; configured SWA origin `https://salmon-hill-0056eb60f.2.azurestaticapps.net`.
- API/SignalR: `https://api.playpokerattack.com/`; Linode API loopback `127.0.0.1:8081`.
- API image: `chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi`.
- Migration image: `chefknife.azurecr.io/chefknifestudios.pokerattack.server.data`.
- Existing API budget: 0.25 CPU / 512 MiB. Shared PostgreSQL and Caddy remain independently managed.

## Workflow and release ownership

Both workflows run validation on pull requests, publish/deploy from `main`, and support `workflow_dispatch`. PR jobs receive no deployment credentials. Dispatching from another branch must fail production eligibility even if someone can start the workflow.

Client changes include `src/Client/**`, shared contracts, client preparation/tests, workflow configuration, and relevant root build/package configuration. Server changes include `src/Server/**`, shared contracts/service defaults, Docker inputs, release tooling, relevant browser tests, and workflow/build configuration. Shared-contract changes trigger both. Server CI must also run when browser fixtures used by its blocking checks change. Include `.dockerignore`, lockfiles and actual root configuration files in the dependency review.

Use change-detection jobs with stable final required-check names if branch protection requires these checks; avoid a required check remaining pending because the entire workflow was excluded by a path filter. Preserve `migration-ci.yml` until both new workflows demonstrably cover its checks, then remove duplication or retain it only for explicitly separate migration validation. Two deployment workflows does not forbid a build-only CI workflow.

Initially serialize both production workflows with the shared group `pokerattack-production-release` and `cancel-in-progress: false`. Workflow-level serialization is intentionally simple and covers maintenance, verification, approval, promotion and reopening, as well as client publication. It can delay builds; optimize to a durable release lease around deployment later if needed. Do not use nested jobs that reacquire the same group. GitHub concurrency is not a substitute for the host lock or persistent pending/transaction state. The default pending queue can replace older pending runs; record superseded runs and recheck candidate eligibility immediately before mutation. [GitHub concurrency semantics](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency).

For a commit changing both sides, require backward-compatible API/contracts so either publication order works. If a client requires a new server capability, its deployment must wait for a recorded, promoted compatible server release; otherwise fail with the required version, rather than release against an older server. Use expand/contract changes for breaking interfaces. Workflow names and trigger order do not enforce server-first deployment.

Manual production operations must respect the persistent release lease too. A pending candidate or interrupted transaction blocks unrelated client publication as well as another server deployment. This requires a read-only release-status capability for the client workflow; it must not receive the server mutation key.

## Client pipeline

1. Run production-artifact preparation tests. The existing `prepare-client-artifact.test.mjs` tests the pilot helper, not `prepare-production-client.mjs`; add direct production coverage for named API entries, environment overrides and compressed representations.
2. Install the reviewed .NET 10 SDK, WASM workload and Node version; publish the client in Release with the existing compression policy. Pin action revisions and record tool versions.
3. Run `prepare-production-client.mjs`. Validate every shipped settings variant and compressed equivalent: both API entries target the production API, and SWA uses `Blazor-Environment: prod`. Preserve the exact existing environment name.
4. Emit a public, non-secret `release.json` containing source SHA/build identity, plus a private artifact manifest/checksum. Archive the complete deployable directory. Never rebuild a candidate between validation and deployment.
5. In environment `client-swa`, verify no unresolved server release/maintenance transaction, then deploy using `Azure/static-web-apps-deploy`, `skip_app_build: true`, `skip_api_build: true`, empty API/output locations, and the prepared directory as `app_location`. This matches the documented prebuilt-artifact contract. [SWA build configuration](https://learn.microsoft.com/en-us/azure/static-web-apps/build-configuration).
6. With bounded retries, verify apex/www and the configured SWA origin return the expected release SHA, settings, environment header and usable application assets. Run a fresh-browser load and real SignalR connection. A cache-busting query containing the SHA is not proof that the returned bytes belong to that release.
7. Record the successful client release and preserve the previous accepted artifact. Increase the current three-day retention to the operational rollback window, initially 30 days, and protect the last two accepted artifacts even if no release occurs during that window. Use a durable archive if Actions retention cannot guarantee this.

Dispatch supports redeployment of a selected previously accepted artifact by run/artifact identity and checksum. Do not accept arbitrary uploaded ZIPs or download by an ambiguous artifact name. If verification of a new SWA publication fails, restore the recorded previous artifact when available, verify it, and keep the attempted release failed. Verify ambiguous deployment outcomes before retrying an upload.

Client publication does not restart the server. Server publication does not build or release a new client.

## Server pipeline

Logical job sequence:

```text
validate -> build-and-publish -> resolve-release
         -> approve-reset/deploy -> verify-gameplay
         -> approve-promotion/promote -> reopen-and-check
```

### Validate and publish

Reuse the business-logic tests, WebAPI tests against disposable PostgreSQL, target-guard tests, release failure-path tests, and isolated browser/container checks from `migration-ci.yml`. Adapt the container harness to accept the exact image built by this run rather than building a second untracked image. Build the isolated browser fixture as needed; this does not publish a client release.

Build API and migration images for `linux/amd64` using the existing Dockerfiles. Pass `SOURCE_REVISION=$GITHUB_SHA` so both OCI revision labels contain the full source SHA. Test the candidate API image before pushing that same image. Publish both images with full-SHA tags; capture the registry digests and verify repository, platform and revision label. Treat the captured digest as the release identity, not subsequent tag lookup. If an existing SHA tag points elsewhere, fail instead of silently overwriting/re-resolving it.

Store a release manifest containing source SHA, API digest, migration digest, configuration hash, test artifact references, workflow run/attempt and schema prerequisite. The configuration hash identifies a pre-provisioned protected host config; config contents never enter the workflow or Docker build arguments.

Migration image publication does not execute the bundle. Schema changes require their separate reviewed/rehearsed operation. Application deployment must stop when its declared schema prerequisite is unmet, and rollback must never downgrade the database. Schema compatibility must include the current known-good application.

### Deliver the candidate

The production job uses environment `server-linode`, restricted to `main`, with a deliberate reset approval. Its review summary identifies SHA, digest, config hash, current release, live-session observation, expected interruption and recovery target. Do not silently enable deployment from TransitJazz's historical migration branch.

After approval and maintenance preparation, use a private key under `$RUNNER_TEMP`, a separately trusted `known_hosts` entry, strict host-key checking, batch mode, identities-only and bounded connection/keepalive settings. Clean temporary key material on exit. Do not use runtime `ssh-keyscan` as the trust source, a root login, forwarding, SCP, or arbitrary remote commands.

The existing command sent to the restricted account is:

```text
deploy production sha256:<api-digest> <config-sha256>
```

The existing script pre-pulls, validates the config/Compose snapshot, checks maintenance, replaces only the API, and verifies image identity, resource limits and local readiness. Its readiness loop currently has 30 attempts, two-second sleeps and five-second bounded HTTP calls; Docker operations also consume time. Give the job a bounded timeout with recovery headroom, measured in rehearsal, rather than copying TransitJazz's 15-minute health window without inspection.

On success, the candidate remains pending. The workflow must not label the overall release successful at this point.

### Verify and promote

Run the dedicated production suite described below against the public HTTPS API and the currently deployed SWA client. Generate evidence only from completed passing assertions. Record digest, config hash, source SHA, client release SHA, run/attempt, verification timestamp and artifact references.

Environment `server-promotion` provides the separate explicit promotion decision after evidence exists. Following approval, confirm evidence is under 30 minutes old, the active pending image/config still match, and the image revision matches `source_sha`. If approval outlives evidence, require renewed verification and review; never merely rewrite the timestamp.

Import the reviewed evidence through the constrained evidence interface, then issue:

```text
promote production sha256:<api-digest> <evidence-sha256>
```

Confirm known-good identity through structured status, release maintenance, and check public availability and a fresh browser connection. Record successful promotion and reopening separately: an inability to reopen still fails the workflow even if the image was already promoted.

## Automating the gates requires additional implementation

Today the README deliberately reserves maintenance acknowledgment and evidence provisioning to an operator. The existing SSH wrapper cannot perform either. Milestone 1 must retain those operator steps; the target workflow needs reviewed extensions, not an unrestricted key or `sudo sh` workaround.

### Maintenance without making the server workflow own SWA deployment

For the target automation, add application maintenance support: a client maintenance view plus a durable server admission gate. Publish that client capability through `client.yml` before enabling unattended execution of maintenance mechanics. Keep the existing static SWA maintenance package as an operator recovery option.

The admission gate blocks new normal lobbies/games while allowing current games to drain. It reads narrowly scoped host-managed state that survives API replacement; mount only that state read-only with permissions allowing the non-root API to read it. The deployment script retains write authority. During restart and candidate verification, ordinary players see maintenance/reconnect guidance; already loaded clients cannot create new matches merely by bypassing the UI.

Provide a temporary, run-bound verifier credential for the production smoke sessions. Scope it only to admission during this release, expire/revoke it after completion, and never embed it in the static bundle or expose it in logs/browser traces. It must not grant cleanup, database or operator access. Review the session and SignalR handshake paths together so the gate cannot be bypassed through a different entry point.

A constrained `begin-release` operation accepts a fixed production target, digest, config hash and validated run identity. It records the approved reset, enters maintenance, observes the private `/_ops/status` counts, and waits a bounded period for draining. At the deadline it proceeds only under the explicit approved reset policy. It creates the existing mode-0600 `sessions-reset-approved` acknowledgment immediately before replacement and binds additional metadata to this one release. No silent refresh of expired approvals, reuse for another candidate, or claim that restarted matches survived.

The lease/maintenance state persists through runner failure; expiration does not automatically reopen an unverified candidate. A constrained `finish-release` clears it only after the matching known-good release is established and verifier sessions are closed. Rollback requires fresh acknowledgment. The current static maintenance page by itself cannot prevent already loaded browsers from starting games, which is why full automation needs this application work.

### Evidence and command authority

Add a dedicated evidence-import operation accepting size-limited JSON over stdin with a fixed schema. It derives the storage path itself, rejects symlinks/path input, validates identity/freshness and source provenance, and atomically writes root-owned mode-0600 evidence under the existing verification directory. Return its hash, never runtime configuration. The existing validator checks the shape of `source_sha`; extend it to bind that SHA to the release manifest and inspected OCI label.

Separate capabilities for read-only status, reset/deployment, evidence import and promotion. Production workflow keys must reject pilot targets. In particular, the current wrapper permits promotion using the same key as deployment; a reviewed capability split is needed if GitHub's promotion approval is intended to be a separate authority boundary. Host wrappers and sudoers must enforce the split, not just step names.

The evidence importer cannot infer truth from five JSON booleans. Trust belongs to the protected verification job and reviewed verifier code, tied to the exact pending release. Keep the import key out of build/PR jobs; keep the promotion key behind the later approval. Validate the final command grammar and capability model in tests before installing it.

Add machine-readable status with active/pending/known-good/previous-good identities, config hashes, transaction/lease state and health, excluding secrets. Currently `status` can exit successfully while printing failed readiness, so CI must not interpret SSH exit code alone as readiness success. A diagnostic status path should report interrupted transactions safely even when mutations are blocked.

### Production-safe gameplay evidence

Create a separate Playwright configuration and helper set, for example `tests/release/`, with explicit production allowlists and no imports of destructive cleanup helpers. Preserve the existing isolated suite's production prohibitions. The new suite must:

1. Validate real TLS and the intended client/server release identities, including apex/www configuration.
2. Establish actual browser SignalR connections, not only HTTP negotiation.
3. Create uniquely named verifier-owned sessions/lobbies; join and complete a bounded multiplayer scenario and solo run using normal gameplay paths.
4. Assert private hand/card notifications reach the intended player and do not reach a second player. The existing test relies on `/test/signal-r` to fence message order; do not enable that endpoint in production. Implement a normal self-scoped ordered acknowledgment on the connection, with no arbitrary target/payload broadcasting, and test its ordering contract. A guessed sleep or successful lobby join is insufficient evidence.
5. Leave only its own sessions through normal game operations; never enumerate/delete unrelated games or use global cleanup. Synthetic games may create normal persisted game records; label/identify them for audit rather than globally deleting them.
6. Produce sanitized machine-readable results and restricted diagnostic artifacts. Strip verifier credentials and unrelated player data before upload, particularly because the repository is public.

Until these assertions exist and pass in rehearsal, production gameplay evidence remains operator-reviewed. Do not set all evidence flags true after a health request or reuse isolated/pilot evidence to promote a different production pair.

## Provisioning inventory

| Location | Configuration |
| --- | --- |
| `client-swa` environment | `AZURE_STATIC_WEB_APPS_API_TOKEN`, read-only release-status SSH identity, trusted host key; main-only deployment |
| ACR publish identity | Azure client/tenant/subscription IDs, GitHub federation, push access to the two PokerAttack repositories |
| `server-linode` environment | Restricted deploy/reset key, host/port/user, trusted host key, approved production config SHA; main-only and reset review |
| Verification environment/job | Separate scoped verifier/evidence identity, access to sanitized test artifacts; trusted main only |
| `server-promotion` environment | Separate promotion capability; main-only and evidence review |
| Linode | Dedicated unprivileged accounts/keys, root-owned fixed scripts, exact sudo rules, root-protected pull-only registry login, pre-provisioned configs and release state |
| Repository protection | Require client/server validation as applicable; review workflow, release-script and verifier changes; no deployment secrets on fork/PR code |

Prefer Azure OIDC for CI publishing; it requires a matching federated identity and `id-token: write` only in the publish job. Start with `contents: read`; add permissions per job when needed. Existing registry-password secrets are not proof that OIDC is configured. [Azure OIDC setup](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect).

Confirm the ACR permission mode before assigning roles: repository Writer/Reader with repository conditions in ABAC mode, or appropriate registry-scoped push/pull permissions in legacy RBAC mode. Keep the host credential pull-only and separate from publishing. Do not switch a shared registry's permission mode as a side effect of this work. [ACR role modes](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-rbac-built-in-roles-overview).

Provision explicit branch restrictions rather than relying only on YAML. GitHub environments can gate jobs and secret availability; required reviewers are available for this public repository. Preserve an owner-operable approval path instead of enabling self-review prevention that leaves the sole operator unable to release. [Environment protection rules](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments).

## Failure behavior

| Failure | Expected result |
| --- | --- |
| Tests/build/publish/digest verification fail | Production untouched; release fails |
| Pull/config/maintenance validation fails before stop | Existing API remains active; surface the failing prerequisite |
| Candidate local readiness fails | Existing host script attempts restoration of recorded active image/config; workflow remains failed even after recovery |
| Public/gameplay verification fails | Candidate stays pending, maintenance remains, promotion is blocked; operator chooses explicit rollback with fresh reset acknowledgment |
| Evidence stale or pair/source identity mismatched | Reject import/promotion; rerun verification and review as required |
| SSH disconnect, canceled job, host transaction present | Unknown outcome; query structured status/recover under host lock; no blind retry or deletion of markers |
| Rollback requested | Restore known-good if pending, otherwise previous-good; require fresh evidence/promotion for the restored pending pair |
| No valid recovery pair exists | Stop automated release before mutation; bootstrap/recovery requires an explicit reviewed procedure |
| SWA post-deploy verification fails | Restore previous accepted artifact when available; verify restoration and report failure |
| Promotion succeeds, reopening fails | Keep maintenance and report the promoted identity plus reopening failure; repair/reverify before clearing the lease |

Do not place unconditional reopen or rollback in an `always()` step. An interrupted runner cannot prove which image is serving. Recovery touches only PokerAttack's API/release state; it never restarts shared Docker, PostgreSQL or Caddy, prunes shared storage, or restores an old database to undo application code.

## One shared Linode Docker dashboard

The selected observability scope is one **Linode Docker Containers** Grafana dashboard for all Docker containers on the shared VM. It replaces the expanded application telemetry, recurring synthetic monitoring, notification setup and five-/ten-minute observation gates previously proposed in this plan. The existing release-time readiness, gameplay verification, maintenance and promotion requirements remain part of deployment acceptance.

This is planned work only. No dashboard, collector, alert or host configuration was created or changed during this planning update.

### Dashboard experience

Use the existing `https://gallantpuffin3113.grafana.net` stack and `grafanacloud-prom` datasource. Default the host to `expense-tracker-vm`. Name the dashboard **Linode Docker Containers** and keep its definition in source control, proposed path `deploy/monitoring/linode-docker-dashboard.json`.

At the top, provide **Host** and **Container** dropdowns. The container dropdown is discovered from collected Docker inventory, supports one/multiple containers and All, and requires no dashboard edit when a container is added or recreated. Include stopped containers that still exist in Docker so failures do not disappear from the selector. The default time range is the last hour with 30-second UI refresh; fresh data arrival remains limited by collection/scrape cadence.

| Panel | Display |
| --- | --- |
| Collection freshness | Last successful Docker inventory/statistics collection, scrape freshness and explicit stale/no-data status |
| Container overview | One row per selected container: name, Compose project/service when present, current state, health, image reference, start time, uptime and restart count |
| Current image | Configured repository/tag or digest reference, actual local image ID, available registry digest(s), and source revision label when present |
| State | Running, restarting, exited, paused, created, removing or dead; keep lifecycle state separate from health |
| Docker health | Healthy, unhealthy, starting, not configured or unknown; show when the health check last ran if available |
| Application readiness | Optional separate panel for explicitly configured local service probes, initially PokerAttack's `/health/ready`; unmapped containers show Not configured |
| Lifecycle details | Created/started/finished time, exit code, OOM-killed flag, Docker restart count for the current container ID, and recent sampled state changes |
| Resources | CPU cores used, configured CPU quota where set, memory usage and memory limit, network receive/transmit and block read/write rates where available |

For a single selection, show readable identity/details and resource charts. For multiple/All selections, keep separate rows/series per container and show aggregate running/unhealthy/stopped counts. Do not merge different containers' image or health values into one status card. Keep full digests copyable from the details table.

Use Docker's configured image reference and the running container's actual image ID as separate fields. Resolve registry digests from that image ID, not from whatever a mutable tag currently references. An image without a recorded repository digest displays Unavailable; an absent revision label displays Not provided. A source label is useful context, not independent proof of deployment provenance. [Docker inspection](https://docs.docker.com/reference/cli/docker/inspect/).

A selected container that disappears must show Missing/no current data, rather than its last green state. Historical charts may still show its earlier samples. Retain container ID as the incarnation identity so a replacement under the same name does not inherit the previous instance's uptime or restart count.

### Collection needed before the dashboard can work

Read-only investigation on 2026-11-15 found the existing host monitoring healthy, but no metric names beginning `container_` or `docker_` in the selected Prometheus datasource. A bounded query from **14:10 to 14:40 UTC**, at five-minute steps, returned only `up{instance="expense-tracker-vm",job="integrations/node_exporter"}=1` among the inspected host/container metric families. This does not establish Docker state; it establishes that additional collection is required. Access used the configured `gcx --context transitjazz` interface because the registered Grafana connector points to a different stack.

Use the existing Alloy-to-Grafana metrics path. Add one small host-owned Docker snapshot collector rather than a new monitoring stack:

1. Every 30 seconds, enumerate all existing Docker containers, including stopped ones. Use read-only Docker inspection and bounded statistics reads for running containers. Cache image metadata by immutable image ID to avoid repeated work.
2. Publish an allowlisted Prometheus snapshot: inventory/image information, lifecycle state, health, restart count, OOM/exit details, start times, resource counters/gauges, and collection success/timestamp. Proposed metric namespace: `linode_docker_*`. Keep source fields separate from derived values and document units in metric HELP text.
3. Write the snapshot atomically to a dedicated textfile directory readable by Alloy. Configure Alloy's Unix exporter textfile collector and extend the existing metric allowlist to forward these metrics through its current authenticated pipeline. Validate the installed Alloy version/configuration before installation; no new public metrics endpoint or Grafana datasource is needed. [Alloy Unix exporter](https://grafana.com/docs/alloy/latest/reference/components/prometheus/prometheus.exporter.unix/).
4. Record per-container collection failures as Unknown/unavailable while retaining known inventory identity. Publish collector failure/freshness separately. Never publish an empty successful snapshot after a Docker permission error or timeout, and never refresh the last-success timestamp for failed collection.
5. Keep any local readiness probes in an explicit root-owned allowlist of service names and loopback URLs, with bounded timeouts. Do not execute a health command or fetch an arbitrary URL supplied by a container label. Export HTTP readiness separately from Docker's own health status.

A host service/timer owns the required Docker access and outputs only approved metadata/statistics. Alloy reads the snapshot; neither the application nor the deployment key gains Docker access. Do not export environment variables, connection strings, arbitrary labels, health-command output, mount paths or credentials. The collector does not restart containers or run commands inside them.

Docker health is the latest `.State.Health` result; absence means **Not configured**, never Healthy. The current PokerAttack Compose file has no Docker healthcheck, so its Docker health panel will initially show Not configured, while the separately configured readiness probe can report the API's health. Supporting this dashboard does not require recreating all shared containers to add healthchecks. Inspect current Docker health state directly on each collection; do not depend on an unverified exporter that might cache it.

Use monotonic CPU/network/I/O counters and PromQL rates for running containers, with a rate window appropriate to the existing 60-second scrape interval, initially four minutes. Present memory limits/CPU quotas as Unlimited when Docker does not configure them, rather than computing a misleading percentage. For host-network containers, label network attribution as shared/unavailable when it cannot be isolated; do not imply host traffic belongs solely to the selected container.

All current-state panels must honor collector success and sample age. Initially consider data stale after three minutes without a successful snapshot; show Unknown/stale instead of retaining green values. Distinguish an unavailable collector from a stopped container. Restart count and OOM flags describe the current Docker container incarnation, not lifetime deployment history. Sampled state timelines can miss brief transitions between collections; event-by-event auditing is outside this dashboard's initial scope.

### Implementation and acceptance

1. Inspect live Docker/Alloy versions and the existing ingestion configuration; implement the collector, metric contract and service/timer files under `deploy/monitoring/`. Validate using fixtures for running, unhealthy, stopped, missing-healthcheck, missing-digest and collection-failure cases.
2. Install collection additively, preserving the current host metrics and all application containers. Confirm live inventory, image identity and resources arrive in the intended datasource. Test stopped/healthy/unhealthy transitions with disposable containers, not by disrupting production services.
3. Create the single dashboard with dropdowns and panels above. Validate every query against actual collected series, compare values with bounded Docker inspection, and visually verify single/multiple/All selection. Add a new disposable container and verify it appears without editing the dashboard.
4. Test container replacement, removal and collector failure: selections/history remain understandable, current panels do not show stale green results, and resources do not merge unrelated container incarnations. Store the dashboard definition and installation/rollback notes in the repository.
5. Add a filtered dashboard link to each server release summary, alongside the deployed digest, source SHA and existing release verification outcome. The dashboard provides runtime visibility across PokerAttack, TransitJazz, PostgreSQL, Caddy and any other discovered Docker workload.

Completion means one working dashboard can select any inventoried container and accurately show its image, lifecycle, configured health, freshness and available resources. A running/healthy container does not establish public TLS, SignalR delivery or correct gameplay, so those existing deployment checks remain the evidence used for PokerAttack promotion. Grafana availability itself does not become an API readiness dependency or a new production deployment gate. Additional application telemetry, continuous browser probes, notification rules and automated rollback based on charts are deferred.

## Implementation milestones and acceptance

1. **Establish release inventory and access.** Verify installed script revisions, root ownership, pull-only credentials, actual current/known-good image/config, host trust, resource limits, schema state and backup evidence. Create GitHub environments/identities and test allowed/denied commands without restarting production. Fix stale operations/WP09 references. Acceptance: credentials and recovery references are proven, not merely named.
2. **Finish independent client CD.** Extend `client.yml`, production preparation tests, artifact manifest/archive, served-identity and browser checks, and client rollback mode. Acceptance: PR is validation-only; a coordinated main release serves the exact expected artifact on all configured origins; restoring a prior artifact is rehearsed.
3. **Implement server CI and digest delivery.** Add `server.yml`, reuse isolated validation, publish both images, bind provenance, add structured status and restricted deploy credentials. Initially require operator-provisioned maintenance/evidence under the current README policy. Acceptance: CI delivers an exact digest to pending and does not report completed production release before promotion. This is a useful intermediate milestone, not completion of full automation.
4. **Automate maintenance and verification mechanics.** Implement the admission gate/client view, durable lease, constrained reset/evidence interfaces, capability-separated keys, production-safe browser suite and strict provenance validation. Install the maintenance-capable baseline through a controlled release before relying on it to automate subsequent releases. Acceptance: ordinary sessions are blocked/drained as specified; verifier sessions exercise real production paths; evidence matches the running pair; no operator file creation is needed.
5. **Wire explicit promotion and recovery; add the shared dashboard.** Add reviewed promotion, freshness handling, reopening, summaries and artifact retention. Implement the single Linode Docker Containers dashboard and its collection path, and link to the selected container from release summaries. Exercise a complete coordinated GitHub release, a production-safe rollback rehearsal and simultaneous client/server requests. Acceptance: only fresh complete evidence promotes; reopened release has expected image/config/client identities; failed release remains failed after recovery; dashboard identity/state/health and stale-data behavior are verified.
6. **Complete operational handover.** Retire redundant CI and keep legacy deployment definitions disabled, document dispatch/recovery commands and credential rotation, and save release evidence. Acceptance: routine client/server releases need only the documented GitHub actions/decisions, with no ad hoc workstation commands.

Release tests must cover rejected SSH grammar/targets, stale/replayed acknowledgment, stale/wrong evidence, source mismatch, concurrent requests, failed pull, failed readiness, failed recovery, interrupted SSH, expired approval, production-test cleanup prohibition and preservation of shared services. Existing release tests mock Docker; include real container rehearsal and a coordinated Linode exercise before claiming the host rollback path is proven. Fault injection should use disposable/pilot targets first.

Completion means both delivery workflows are enabled and exercised, the actual production identities are recorded, a client artifact restoration and server rollback have evidence, the gate mechanics work without hand-edited privileged files, and the shared Docker dashboard has demonstrated accurate image/state/health reporting with explicit collection freshness. A green build, pushed ACR image, locally healthy pending API, or unused workflow file alone does not meet that definition.

## Repository references

- [Existing client workflow](../.github/workflows/client.yml)
- [Existing migration CI](../.github/workflows/migration-ci.yml)
- [Host release contract](../deploy/linode/README.md)
- [Release script](../deploy/linode/pokerattack-deploy), [SSH wrapper](../deploy/linode/pokerattack-ssh-deploy), [validator](../deploy/linode/validate-config.py), [failure-path tests](../deploy/linode/test_release.py)
- [Production operations](LINODE_PRODUCTION_OPERATIONS.md)
- [Existing work packages, including WP09](LINODE_IMPLEMENTATION_WORK_PACKAGES.md)
- TransitJazz reference inspected locally: `C:/Projects/ChefKnifeStudios.TransitJazz/.github/workflows/server.yml` and `client.yml`.
