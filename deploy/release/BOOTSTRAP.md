# Transitional introduction and recovery

Source implementation only until checklist I/K installs and verifies it. The
deployment checklist records the actual installed revisions and release state.
Routine CI remains disabled until the acknowledged baseline introduction passes.

The root operator channel is the established trusted root SSH identity with
strict host-key checking. No forced-command CI key exposes `operator-bootstrap`.
Every production API replacement still requires a fresh decision bound to its
exact source, digest, config, run/attempt, drain policy and original 30-minute
window. Promotion is a later separate decision for the actual imported proof.

Before changing installed tooling, use `bootstrap_recovery.preserve()` from the
protected staged source to save the four current tool files. It keeps original
deploy bytes and a separately hashed executable copy whose `install_dir` points
at that versioned recovery bundle. This preserves the old Compose/validator
contract rather than accidentally using the newly installed admission mount.
Provision root-owned mode-0600
`/etc/pokerattack/production/bootstrap-recovery.json` with format
`pokerattack-bootstrap-recovery-v1`, the exact recorded pair, inspected source
and local image ID, compatible legacy-client publication summary, and returned
tooling hashes. Preserve the complete legacy client archive/manifest privately.
`bootstrap_artifacts` uses a distinct format and invents no Actions run or
modern release identity for that historical client. `snapshot()` rechecks the
accepted runtime and all saved tool bytes before reserving bootstrap maintenance.

The root dispatcher grammar is
`python3 -I /opt/pokerattack/release-control.py operator-bootstrap ACTION production RUN ATTEMPT`:

| Action | Input and effect |
| --- | --- |
| `begin-release` | Normal server manifest plus fresh acknowledgment with action `bootstrap`; reserves the durable server slot and closed admission state, returns `bootstrap-maintenance-pending`, does not drain yet. |
| `client-authorize` | Exact mode, archive checksum, fixed-origin probe hashes and bounded modern client identity when applicable; records an immutable publication intent in that server lease. |
| `client-published` | Matching `sha256` in stdin; probes served bytes on all three HTTPS origins and records publication. A matching lost response can be reconciled by fresh reads without another upload. |
| `drain-release` | Requires fresh, actually served static maintenance, rechecks the original reset approval, then observes private sessions with its bounded drain/reset policy. |
| `client-status` | Returns only the established bootstrap candidate client/runtime identity; enables explicitly provisional first verification, not accepted-history fabrication. |
| `client-acceptance` | Additionally requires matching fresh imported gameplay proof, revoked verifier and zero remaining owned sessions before durable client acceptance. |

`bootstrap_publish.py` publishes only within that existing root bootstrap lease.
Its modes are `maintenance`, `candidate`, and the frozen `legacy-recovery`.
Inputs are exact archives/manifests and a fresh workspace. It authorizes once,
obtains SWA authentication in memory, uploads the validated extracted bytes,
then confirms served bytes through the root dispatcher. The SWA token is only a
child environment value; provider output is neither printed nor persisted.
`--reconcile` uses the saved workspace receipt and only probes the existing
publication. It cannot authorize or repeat an uncertain upload. Ordinary manual
releases use `publish-client.py release` or its exact accepted-artifact `restore`
dispatch; they use the same protected workflow/lease and cannot enable rollout.

After static maintenance and drain, the deployment capability installs the
reviewed digest into the same single API authority. Root publishes the exact
modern client under that server lease. Verifier issuance and evidence import
refuse a different or unresolved bootstrap publication. `ProvisionalClient`
supplies that root-bound artifact to actual gameplay verification; its pointer
format and generated evidence reference explicitly say provisional. Only fresh
imported/revoked real proof permits `accept_verified()` to update the real client
archive with its normal conditional writes. The later promotion decision and
ordinary finish/public browser checks then establish the baseline. No migration
bundle executes in this sequence.

If the first promotion proof expires while the candidate remains installed,
rerun the actual build-only GitHub run and wait for its successful later attempt.
Keep the original server manifest, image, client and reset packet unchanged.
Run `bootstrap_operator.py verify` with the same workspace/runtime and explicit
`--verification-attempt N` matching that real GitHub attempt. The existing
evidence capability transfers only the verified pending lease to the later
attempt, revokes old verifier authority, and issues fresh scoped identities.
No drain, reset, image replacement or client upload is repeated. Results go to
`verification-resume-N/` under the original workspace, preserving old proof.
After actual gameplay/import/revocation succeeds, request the new matching
`PROMOTE` decision. Use the same `--verification-attempt N` for `promote` and
`finish`; neither may read the original expired proof. The client already
accepted in the durable catalog remains accepted and is rechecked by finish.

If introduction fails, retain maintenance and inspect the exact transaction,
runtime, lease, saved snapshot hashes and versioned old tools under both host
locks. Archive the failed attempt. A fresh release-specific legacy reset decision
must select only the frozen old pair; release locks before invoking the saved
tool which acquires its own flock. Use its original Compose/validator, restore
only the frozen compatible client, and run `legacy-bootstrap.cjs` through the
private operator context. That separate entry uses strict HTTPS and only its own
normal lobby/game/solo allocations; it calls no cleanup or operator endpoint.
Its actual assertions generate the legacy verification shape. The old server
lacks the scoped solo-leave endpoint, so a completed solo may remain until a
later separately acknowledged reset; do not report it as zero or delete it through
a global route. Promotion/reopening of recovered service still require the
matching fresh evidence and explicit decision. Never edit evidence booleans or
timestamps, remove an uninspected transaction, or reactivate retired ACA.

H05 rehearsals disclose simulated GitHub decisions/SWA file-service publication.
They do not prove real GitHub environment reviews or SWA/ACR credentials; I/K
must verify those separately before enabling routine delivery.
