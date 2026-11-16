# Release contract v1

Implements checklist B01–B05. This is the wire/storage contract for the following implementation; it is not an installed capability or approval to restart. Examples are deliberately fictional and cannot be submitted as release evidence.

## Immutable identities and archive (B01)

`server-manifest.schema.json` and `client-manifest.schema.json` define immutable records. Hash exact UTF-8 manifest bytes; never rewrite an existing record to refresh timestamps. API registry digest is distinct from local Docker image ID and the human-readable full-source-SHA tag. Inspect the captured digest's platform and OCI revision after push. Never re-resolve a mutable tag at deployment. The migration image is publication only, never execution authority.

Schema prerequisite contains the exact required migration IDs, known-good compatibility decision and its review reference. Before replacement require the existing read-only database readiness check plus a host-side check of the declared migration IDs. A new schema prerequisite needs separate reviewed/rehearsed migration evidence; no deployment path executes migrations. Rollback never restores/downsamples schema.

Client `release.json` is public and contains only source SHA, run/attempt, artifact format and minimum server capability. The private manifest additionally contains exact artifact/run identity, archive SHA256, per-file checksums, settings SHA256 and verification references. Compute archive checksum outside the archive to avoid self-reference. Preserve the very same archive through validation/publication/restoration; no rebuild between stages. An artifact ID is the numeric Actions artifact ID returned by upload, not its name. Restore selects an accepted manifest using run/attempt/artifact ID plus matching archive checksum; no arbitrary ZIP/URL input.

Selected durable archive: a dedicated private Azure Blob container `pokerattack-client-releases` in a dedicated account provisioned in I02, with public access disabled and OIDC data-plane access restricted to trusted client jobs. Exact account resource ID is an I02 output, not invented live state. Blob keys `releases/<run>/<attempt>/<artifact-id>/<archive-sha256>.tar.gz` and immutable manifest siblings. `accepted.json` holds current/previous accepted manifest identities and uses ETag compare-and-swap while the client lease is held. New candidates never update accepted pointers. Pin the last two accepted archives indefinitely; prune only unpinned history older than 30 days, including failed candidates. Do not use an age-only storage lifecycle rule that can delete pinned releases. Archive/accept operations are idempotent only if bytes and identities match. Validate archive member paths, reject symlinks/duplicates/traversal, verify every file before restoring.

E02 storage detail: actual sibling keys are `releases/<run>/<attempt>/<artifact-id>/<archive-sha256>/client.tar.gz` and `/manifest.json`. The archive checksum is external to its content. Packaging emits `pokerattack-client-candidate-v1` without a fabricated Actions artifact ID; only after upload returns the trusted numeric ID does finalize emit the valid immutable client manifest. A candidate record cannot pass the accepted-manifest schema. Full client per-file manifests are bounded at 32 MiB; server/evidence documents and every host command remain bounded at 64 KiB. Host/client verifier transport uses `pokerattack-client-identity-v1` with source/workflow/artifact/archive/settings/minimum-capability identity and only the shipped settings-file hashes, not the entire file map.

E04 client lease wire detail: begin-client accepts exactly `{ "candidate": <client identity summary>, "recovery": <previous accepted summary or null> }`. It freezes the serving known-good server pair, verifies installed server capability and refuses any server lease/maintenance/pending/transaction, incompatible artifact or competing owner. Idempotence never changes the selected bytes. A rollback/reset capability cannot steal an active client slot. Finish-client accepts exactly `outcome` (accepted/restored), `client` (the matching reserved candidate/recovery summary), `verified_unix` and literal-true `checks` for https, served_client, settings, header, assets, browser and signalr. Protected client-job code supplies the assertions after verifying actual bytes/browser behavior. Freshness and the frozen server pair are rechecked; failures leave the lease and never touch server maintenance. Completion is archived before clearing only that client lease. A lost response is reconciled against the exact archived finish hash. Read-only status includes the client archive/artifact/minimum-capability identity, never credentials.

Accepted pointers change only after successful served-byte/browser verification under the matching client lease and an Azure ETag CAS. Accepted history records are written only after committed acceptance; mere candidate upload or failed CAS never permits restore. If a response/history write is interrupted, the committed current/previous pointer still establishes acceptance, and retry can repair the history. Retention runs under the lease, rechecks the accepted pointer and uses conditional blob deletion; last-two pins have no age expiry. The local backend is rehearsal only. The Azure backend uses current OIDC login, a fixed private container, no account keys/SAS and no unconditional overwrite.

## Host commands and capabilities (B02)

All production CI commands use forced-command keys with `restrict`, no PTY, forwarding, SCP, Docker group or login shell. Root operator recovery remains available via established trusted SSH. Root-owned LF wrappers accept exact single-space ASCII grammar, no quoting, whitespace alternatives, options, extra arguments, shell expansion or pilot targets. Executables and sudoers are fixed, not caller-controlled. JSON stdin is limited to 64 KiB, duplicate keys rejected; no caller-supplied paths. Output is bounded JSON except the explicitly sensitive verifier credential result. `run` is a positive decimal GitHub run ID (max 20 digits); `attempt` positive max 5 digits. Digests lowercase sha256; config/evidence hashes 64 lowercase hex; source exactly 40 lowercase hex.

| Command | Allowed production capability | Input / result | During pending/interrupted state |
| --- | --- | --- | --- |
| `status production` | status, deployment, evidence, promotion, client-lease | No stdin; runtime + recorded pairs, readiness, lease, transaction, capability version; never secrets | Always diagnostic; report errors explicitly rather than infer from exit 0 |
| `begin-release production <run> <attempt>` | deployment | Server manifest + fresh acknowledgment JSON; matching lease/status | Matching lease is idempotent without extending approval; competing lease/pending/transaction rejected |
| `begin-rollback production <run> <attempt>` | deployment | Recovery manifest + fresh rollback acknowledgment + exact `recovery_from` lease identity (or null when idle) | Explicit reviewed handoff only; rejects a mismatched owner/pair or held host lock |
| `deploy production <digest> <config> <run> <attempt>` | deployment | Must match lease/manifest/approval; result pending pair | Repeated matching pending reports existing state; never restarts twice; interrupted refuses mutation |
| `rollback production <run> <attempt>` | deployment | Fresh rollback acknowledgment/manifest, exact selected recovery pair | Allowed for pending; requires current lease and fresh reset approval; interrupted requires operator recovery |
| `verifier-credential production <run> <attempt>` | evidence | One short-lived secret response; hash only mounted to API | Only matching active pending lease; no renewal/reissue after expiration/revocation |
| `import-evidence production <run> <attempt>` | evidence | Evidence schema JSON on stdin; returns protected-file SHA256 | Only matching active pending pair/source/client/run; interrupted rejects |
| `revoke-verifier production <run> <attempt>` | evidence | Revoke only this lease's verifier after closing owned sessions | Allowed for matching lease; does not reopen or change application state |
| `promote production <digest> <evidence-hash> <run> <attempt>` | promotion | No stdin; fresh accepted evidence + live identity/readiness check | Matching promoted pair idempotent only with same evidence; no new deployment/acknowledgment |
| `finish-release production <run> <attempt>` | promotion | Confirm promoted pair, verifier revoked/closed; clears matching maintenance/lease | Refuse unresolved transaction/pending/incorrect known-good |
| `begin-client production <run> <attempt>` | client-lease | Client manifest JSON, exclusive client publication lease | Refuse server lease, maintenance, pending, transaction or incompatible server |
| `finish-client production <run> <attempt>` | client-lease | Checked publication/restoration result; clear own lease only | Never clears a server lease or server maintenance |
| `resume-client production <run> <old-attempt> <new-attempt>` | client-lease | Same run, strictly later attempt, unchanged reserved client/recovery/server identity | Transfers only the client slot; no upload, success claim or server authority; reconcile actual SWA outcome before uploading |
| `begin-client-recovery production <run> <attempt>` | client-lease | Explicit recovery of a completed prior client run; exact prior owner/archive tuple and reserved accepted predecessor, unchanged healthy server pair | Never steals a server lease; reconcile prior workflow/provider/served identities before requesting handoff |
| `resume-release production <run> <old-attempt> <new-attempt>` | deployment | Same run/source/manifest and strictly greater attempt; explicit status-based handoff | Only a known pending/promoted pair with no transaction; invalidates old evidence/verifier, never restarts or refreshes reset approval |

The client receives a read-only status key, plus a distinct minimal client-lease key that can only reserve/release its publication slot. It receives no server deployment/reset/evidence/promotion key. This closes the race between a status read and a manual server release. Client publication holds its host lease through SWA verification/recovery. Manual publishers must use the same client lease; manual server commands must honor either lease. Neither workflow retries a lost mutation response: query status and compare exact identities first. No arbitrary file-write or pilot commands are permitted via production keys. Keep legacy root-only pilot tooling separate.

Control operations take a short non-blocking control lock for state transitions. Server replacement takes the existing host-wide deploy lock. Do not hold that same flock while invoking the legacy command that acquires it; hold the durable lease instead. Lock order control then deploy only where no nested acquisition occurs. Backups retain the deploy lock. Atomic file creation/replacement must reject symlinks in fixed root-owned parents; evidence is root 0600, maintenance admission state root-owned/readable only to API via a dedicated read-only mount. API cannot write any host state.

## Approval, evidence and resume (B03)

Acknowledgment is a reviewed record with exact digest/config/source/run/attempt, `approved_unix`, `expires_unix` (at most 1800 seconds after approval), `drain_seconds` in 0..300, policy `reset-at-deadline`, and action deploy/rollback. Freshness is checked again immediately before replacement. Only a protected trusted job after reset review can submit it. A retry never advances either timestamp. The deliberate policy allows remaining matches to end at deadline, rather than claiming preservation. Enter maintenance first, observe private in-memory counts, drain boundedly; write the legacy 0600 acknowledgment immediately before replacement. A waiting/failed runner leaves maintenance closed, including after approval expires. No timeout reopens it.

State transitions: idle -> leased/maintenance -> draining -> replacement transaction -> pending -> verified -> reviewed/promoted -> verifier-closed/reopened -> idle. Pre-pull/config failure leaves prior API running but maintenance/lease retained for inspection. Failed candidate attempts recorded exact-pair restoration and remains failed; failed restoration retains transaction. Public verification failure leaves pending/maintenance. Expired promotion review returns to fresh verification plus another explicit review, without deploying again or editing evidence timestamps. Diagnostic reads work throughout; manual interrupted recovery must inspect runtime/pairs under the host lock before correcting state.

C05 wire details: begin stdin is `{ "manifest": <server manifest>, "acknowledgment": <record> }`. Acknowledgment fields are exactly `action`, `digest`, `config_sha256`, `source_sha`, `run_id`, `run_attempt`, `approved_unix`, `expires_unix`, `drain_seconds`, `policy`. A normal action is `deploy`. Begin-rollback adds required top-level `recovery_from`, either null when no prior lease exists or the exact prior `run_id`, `run_attempt`, `digest`, `config_sha256`; action is `rollback`. This explicit fresh reviewed recovery is distinct from another deployment run stealing a lease. The immutable manifest keeps original build provenance, while the recovery acknowledgment binds the new execution run. Normal begin/deploy still binds the build run to its execution; resume preserves original manifest/approval bytes and records a new verification attempt separately.

Rollback selects recorded known-good while pending, otherwise previous-good. An explicitly reviewed recovery of a failed/pre-stop candidate with no pending pair instead redeploys its still-serving recorded known-good pair; it never silently downgrades that process. The same image/config replacement still requires fresh reset acknowledgment and new verification/promotion. Every mutator probes/acquires the existing host lock appropriately; the controller never holds that lock while invoking the legacy command that acquires it. Approval is rechecked after pull/Compose validation, just before replacement. Runtime target must have the reviewed source/platform/image ID and release-capability label 1. The label is not independent provenance; source and image identity must also match. Retired capability-0 application fallback uses only the preserved operator recovery tooling under the separately acknowledged bootstrap procedure.

For first introduction only, trusted root may run `/usr/bin/python3 -I /opt/pokerattack/release-control.py operator-bootstrap begin-release production <run> <attempt>` with the same packet and action `bootstrap`. No production key/sudoers role exposes this entry. It permits the old server to lack the admission endpoint, while retaining config/schema/recovery/fresh-reset checks and the operator's static-maintenance sequence. Normal begin requires the maintenance-capable baseline. Provision `admission` root-owned 0755/state 0644 and private `completed`/`interrupted` root-owned 0700 before installation. No deploy command creates an automatically open gate.

H05 source refinement: begin-bootstrap reserves `bootstrap-maintenance-pending` without draining. A frozen compatible legacy-client/old-tool recovery record must match the currently accepted runtime. Root-only client-authorize/client-published prove static maintenance before drain-release, and later bind candidate client publication to verifier/evidence. First verification uses an explicitly provisional artifact; fresh imported/revoked actual proof is required before durable client acceptance. See [exact bootstrap grammar and recovery](BOOTSTRAP.md). Ordinary manual publication dispatches the governed workflow and cannot upload around its lease or enable rollout.

Resuming an already promoted pair sets `promoted-needs-verification`; revoking a credential alone cannot make finish-release succeed. New verification/import and explicit promotion review are required again. Finish requires matching lease/admission context, promoted identity/readiness, revoked verifier and zero remaining post-replacement sessions, archives its state, and opens only then. Lost successful finish replies can be reconciled idempotently against the archived identity. A finalization failure attempts to restore closed admission; if filesystem failures also prevent that, retain the lease and report an unknown outcome for inspection. No automatic timeout, rollback or reopen finalizer is used.

Evidence must bind exact API/migration digest, config, inspected OCI source, run/attempt, current client artifact identity, completed assertion results, issued verifier identity, verified timestamp and sanitized artifact references. All required checks must be literal true, including multiplayer/solo completion and private-message isolation. `verified_unix` no future skew and age <=1800s at import AND promotion. Imported filename derives only from hash of exact JSON bytes. Check manifest source AND running OCI revision AND current pending pointer. Max 64 KiB; reject bad types, unknown security fields, duplicate keys, path traversal, symlinks, stale/failed/substituted data. Idempotent import requires identical bytes. Failed evidence can be retained privately for diagnosis but never used for promotion.

Verifier tokens expire at most 30 minutes after issue, bind run/attempt and release pair and explicitly registered verifier-owned player/session IDs, and grant only admission during maintenance. Use HTTPS Authorization Bearer transport for HTTP/Node; browser WebSockets use a host-only Secure/HttpOnly `__Host-PokerAttackVerifier` cookie installed only in the isolated verifier context. Scope HTTP injection to the exact API origin. Never URL, bundle, console, screenshot/trace/HAR or general SignalR log. Cookie/request captures are prohibited for this context. SignalR connection and subsequent invocations enforce expiration/revocation; already loaded ordinary clients cannot open new matches via another path. Existing normal sessions may continue draining. Credential renewal is refused. To renew stale verification, rerun the workflow with a greater attempt and explicitly resume the same pending pair; revoke/close old verifier sessions and invalidate old evidence before issuing one credential for the new attempt and requesting a new promotion review. Resume is never a deployment retry. It grants no database/cleanup/operator authority.

## Triggers, stale candidates and compatibility (B04)

C06 implementation detail: the host records the canonical server-manifest hash at begin and preserves that original build manifest in the release snapshot before promotion. Evidence keeps the current execution run/attempt (which can differ from the original build after recovery/resume). The importer hashes and stores the exact bounded input bytes under its internally chosen hash filename, permits only one evidence identity per attempt, and checks the report timestamp falls inside the issued verifier's lifetime as well as the 30-minute evidence window. Client SHA/archive identities and assertion truth come from the protected verifier job, not host inference. Importing proof does not promote. Only the later promotion capability can enter `promoting`; production legacy validation additionally requires that controller context and rechecks source/image/manifest. A lost or partial promotion response remains closed as `promotion-unknown`; explicit resume of established matching pointers invalidates old proof and requires fresh verification/review. Already-promoted reverification never rotates previous-good or restarts the API. No timestamp is rewritten to renew evidence.

Both workflows trigger PR, main push and dispatch without workflow-level path filters. Change detection chooses expensive checks; final stable checks `Client validation` / `Server validation` always report, including docs-only changes. PR/fork validation receives no production credentials. All main/dispatch production jobs require `refs/heads/main`, a protected exact environment and `DEPLOYMENT_AUTOMATION_ENABLED == 'true'` (default off). Validate trusted source at mutation time; never deploy a stale source behind current main, except explicit accepted-artifact restore or matching pending resume. Restore uses trusted accepted provenance, not branch bypass.

| Change/event | Client validation | Server validation | Production eligibility |
| --- | --- | --- | --- |
| src/Client or client preparation/release tests | Build, artifact and browser checks | Stable no-change success unless browser fixture is shared | Main after all gates |
| src/Server, Dockerfiles, host tools | Stable no-change success | Full isolated tests/images/browser fixture | Main after all gates |
| Shared contracts / service defaults / root build, dependency or Docker inputs | Both as affected (conservative both for root files) | Full checks | Either order requires backward compatibility |
| tests/browser fixtures/guards/release verifier | Client checks when it consumes fixture | Full checks | Trusted reviewed main only |
| docs-only | Stable no-change success | Stable no-change success | No implicit release |
| PR/fork | Validation only | Validation only | Never |
| Non-main dispatch | Explicit production rejection | Explicit production rejection | Never |

Both workflows use workflow-level `pokerattack-production-release`, `cancel-in-progress: false`; no nested reacquisition. Superseded pending requests are reported; stale candidates recheck main/pending before mutation. GitHub's replaceable pending queue does not replace the durable host lease. A client with a minimum server capability/version must find a promoted compatible server with no unresolved state. If absent, fail/defer with the prerequisite and release the lock so the server workflow can run; never wait holding this group. Shared breaking changes require expand/contract; trigger order is not a dependency.

## First-release bootstrap (B05)

F05 source implementation: the evidence capability may call `resume-verification
production <same-run> <old-attempt> <new-attempt>`. It transfers only an already
established matching pending or promoted pair, while holding both host locks;
it cannot restart/deploy/promote or refresh reset approval. This supports an
explicit GitHub rerun of the **verify job and its dependent promotion job** after
crashed verification, stale evidence or uncertain promotion. Preserve the
original manifest/artifact; do not rerun builds or deploy to renew evidence.
The later attempt revokes the old grant, uses fresh random player IDs and does
real gameplay again. The API retains only authenticated ownership metadata for
exact server-allocated IDs, captured before storage/response; the verifier lists
only that same run/pair's IDs, waits for admitted creations to finish and uses
normal EndGame/lobby leave/solo leave. It never searches by display name, lists
unrelated games, or uses a global cleanup route. No credential is persisted in
this in-memory ownership registry. After completed gameplay it revokes its own
credential and imports exact sanitized bytes. The separate promotion environment
requires `PROMOTE <imported-evidence-sha256> <run>/<attempt>`, rechecks the original
verification time and active identity, and uses only the promotion capability.
Stale evidence requires another real verification and decision, never a new
timestamp. An unknown mutation response is not retried or followed by reopening.

F04 source implementation: `server-status` is a main-only read-only packet job;
`server-acr` holds only image publication identity; `server-linode` separately
holds reset/delivery authority. The resolver emits `ACK_RESET <packet-sha256>
<run>/<attempt>` for the deliberate environment review comment. The exact packet
includes current/recovery pair, observed aggregate counts, source/config/images
and an immutable 30-minute window. GitHub review history supplies the decision
and comment but no reliable decision timestamp. `approved_unix` is therefore the
conservative earlier packet validity anchor, not a fabricated click time; actual
decision time is recorded as unavailable. Packet creation, waiting for approval,
runner setup and draining all consume the same window. An expired packet requires
a new packet and new decision; neither runner start nor retry extends approval.
Delivery stops at ready pending with maintenance closed. A lost response permits
read-only diagnosis and never an automatic mutation retry. SSH bounds are 390s for
bounded draining and 1040s around the root command's 900s limit; actual replica
recovery timing remains H evidence before installation.

Current live baseline is the A04 recorded `693d3d…` image/`7c8304…` config, source `250b034…`; it lacks durable maintenance, scoped verifier/evidence import, public served client identity and ordered self acknowledgment. Current tools also lack separated roles and structured status. Do not call these new APIs before they exist. Legacy ACA is deleted and cannot recover this release.

1. Finish local C–G and complete H on a replica starting from these current capabilities. Provision/install I without restarting production; retain operator access and versioned old scripts. Keep rollout switch off. Save exact old image/config/Compose tooling and compatible current SWA archive to protected recovery storage. Recovery must retain the old Compose contract because the old application has no admission mount support.
2. Assemble K01 packet with exact tested API/migration digests/source, new config hash (if needed), client archive/checksums, schema review, current pair, restored client and old tool/config pair. Read live session/pending/transaction state again. Obtain fresh release-specific reset approval; never reuse broad implementation authorization.
3. Use the transitional operator-controlled static SWA maintenance publisher with a client lease, observing that loaded old clients lack durable admission. Drain privately; at deadline deliberately reset under the approved policy. Preserve one API authority. Deploy new digest with the controlled legacy root path and new read-only admission state; do not rely on new begin-release or verifier until replacement has passed local readiness. No second production process or schema operation.
4. Once new server is running closed to ordinary new sessions, use installed constrained verifier admission and publish the tested maintenance-aware compatible client through the root operator's bootstrap publication path, bound to the existing server lease/run/pair. This is a nested bootstrap operation recorded in that lease, not `begin-client` (which deliberately refuses a server lease). No production CI key can use this exception. Existing old client artifact is recovery input. Verify public served bytes/settings/assets, real WebSockets, full multiplayer/solo and ordered private-message isolation through D, using only owned sessions. If features or recovery fail, retain maintenance and use explicit fresh-acknowledged old-pair recovery with its exact old tooling/Compose, followed by compatible saved-client restoration and external verification.
5. Import fresh matching evidence and present explicit promotion decision. Promote only after that decision and a freshness/identity recheck. Close verifier sessions/revoke credentials, finish matching lease, reopen compatible client and verify fresh connection. Record actual known-good, client archive, tool hashes and capabilities. Only now may K03 publish/activate routine CI mechanics. A promoted-but-unreopened release remains failed/in maintenance until repaired.

Static maintenance is a transitional fallback, not ordinary application admission. The bootstrap replica must demonstrate the root-operated transition from legacy acknowledgment/evidence to constrained APIs; the current production process does not run a rehearsal for this contract. Recovery snapshots and maintenance-specific config are private, never source/artifacts. No unconditional reopen/rollback finalizer is permitted.


F06 local source: the separate promotion capability finishes only a matching
promoted, revoked, zero-session lease. The workflow then checks all served client
origins and a fresh normal browser/SignalR connection. Promotion, reopening and
public checks have separate recorded outcomes; a public-check failure after
reopening does not authorize resetting newly admitted games. A lost finish
response permits one read-only reconciliation, never an automatic retry.

Server dispatch `operation=rollback` publishes no new registry image. The status
role may call only `rollback-target production` to obtain the host-selected
known-good (pending recovery) or previous-good pair and its protected saved
manifest. A failed pre-stop lease selects its own recorded serving known-good
pair. No arbitrary digest, configuration or snapshot path is accepted. The
immutable reset packet binds the current execution separately from original image
build provenance and requires a fresh ACK_RESET decision. Root schema/config
checks precede the reset; no migration or downgrade runs. An already unready API
may have unavailable counts (reported null), and only this acknowledged recovery
may reach its bounded reset deadline without private observations. A ready API
with an unavailable observer fails closed. Recovery must generate new gameplay
evidence and obtain its own PROMOTE decision, then finish and pass public checks.
The original failed release remains failed. A legacy pair lacking a protected
manifest requires the separately rehearsed root bootstrap recovery path.
