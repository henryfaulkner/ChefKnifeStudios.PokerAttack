# Retained CI check ownership

Reviewed 2026-11-21 against actual protected main `c01088c7d289c7329126294268bde3d34bdad426`.
[Live workflow/source/disabled Azure inventory](L01-workflow-inventory.json).
This is prepared L01 handover evidence; K06 and final source handover remain incomplete.

| Historical migration CI check | Current routine owner |
| --- | --- |
| Isolated business logic tests, API build, disposable PostgreSQL API tests | server.yml / isolated / Test business logic and API hosting with disposable PostgreSQL |
| E2E target guards | client.yml / checks and server.yml / isolated; npm test:guards |
| Client artifact preparation | client.yml / checks and server.yml / isolated; both preparation test files |
| Release script failure paths | server.yml / isolated; full deploy/linode test discovery in native Linux container |
| Client WASM publish | client.yml / package for client changes; server.yml / isolated publishes an isolated browser fixture |
| API and migration image builds | server.yml / isolated, tested images captured once and passed to publisher |
| Disposable containers and retained browser smoke | server.yml / isolated / check-local-containers.py |
| Uploaded validation client fixture | server.yml builds fixture for isolated tests; client.yml packages exact production archive/manifest for eligible releases |
| New production-safe gameplay and credential lifecycle | server.yml isolated check-release-gameplay.py plus actual protected verify job |
| New collector/dashboard native checks | server.yml isolated check-monitoring.py |

The manual-only migration-ci.yml retains build/test reference checks with Bootstrap-prefixed
validation names, no GitHub environment, Azure login, ACR publication, host deployment or
promotion jobs. It cannot claim routine required-check ownership or launch duplicate CD.
Exact main file hashes match the reviewed source after normalizing Git working-tree CRLF.
The primary working tree's clean old migration-ci.yml was reconciled to those exact already
published LF bytes; no main publication occurred during active K06 recovery.

Both routine workflows trigger pull_request without top-level path filtering. Their stable
Client validation/Server validation jobs use always() and enforce successful change detection
and required relevant checks. Docs-only, irrelevant changes and restore-specific package
skips therefore do not strand protected required contexts. Change classification source and
its boundary tests were exercised by actual K03 PR validation and K04/K05/K06 runs.
Production requires trusted repository/main, push or dispatch, enabled rollout and relevant
change/explicit operation; a PR cannot deploy. All actions with release authority are pinned.

Actual GitHub inventory lists exactly client.yml, server.yml and the manual reference;
main has strict Client validation and Server validation contexts and enforce_admins=true.
Actual Azure DevOps read-only show confirms definitions39/40/41 queueStatus=disabled.
No routine workflow calls ACA deployment, DNS changes or a production migration runner.
The migration image is published as an immutable reviewed artifact, never executed by CD.
