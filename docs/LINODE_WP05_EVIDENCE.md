# WP05 container and release tooling — 2026-11-14

Status: local packaging and controlled release implementation verified. Source base `b6a4d17d8eb42e9f9bee8214021406ff8be22659` plus uncommitted migration changes. No image push, host installation, production deployment or migration.

## Artifacts

- API and self-contained EF bundle Dockerfiles build linux/amd64. Both final images run as UID 1654. The bundle now uses EF 9.0.8 matching Data and runtime-deps 10; its explicit environment connection replaces implicit development-file discovery.
- Root `.dockerignore` excludes environment settings, build outputs and credential/backup files. API publish excludes `appsettings.*.json`. Local ignored development ConnectionStrings were removed after preserving a private copy at `C:\Users\hfaul\PokerAttack-Backups\20261114-wp05\appsettings.Development.before-secret-removal.json`. Configure future local access via environment/user secrets. This does not rotate the previously exposed shared password.
- `deploy/linode/compose.yaml` defines only the API: host networking, loopback 8081/8082, 0.25 CPU/512 MiB, read-only root, private /tmp, dropped capabilities, no-new-privileges, bounded logs and 40s stop grace. Pilot and production have separate projects/configs.
- `pokerattack-deploy`, `pokerattack-ssh-deploy`, `validate-config.py` and README implement fixed-repository digest deployment, protected config hashes, global lock, pre-pull, fresh maintenance/reset acknowledgement, paired snapshots, bounded readiness with image/resource checks, failure recovery and nonzero failed-release result. External gameplay evidence matching the pending digest/config is required for explicit promotion. Pending candidates/interrupted transactions block competing releases.
- Additive Caddy example blocks public test/operator paths and preserves hub/query forwarding. It does not replace the shared proxy or automate DNS.

## Verification

| Check | Observed result |
| --- | --- |
| API Docker build | Passed; local `pokerattack-api:wp05-local`, linux/amd64, UID 1654 |
| EF bundle Docker build | Passed; local `pokerattack-migrations:wp05-local`, linux/amd64, UID 1654 |
| Archive scan of both images | 11 archived layers / 4,097 files; zero privately preserved credential markers and zero environment appsettings files. Checked full connection values and extracted password/account-key markers in memory without printing them |
| Compose config | `docker compose ... config --quiet` passed with local placeholders resolved; no services besides api |
| Bash syntax | Both scripts passed `bash -n` in disposable Linux containers |
| Caddy syntax | `caddy:2 caddy validate` passed; no live proxy reload or certificate request |
| Release failure-path harness | **8 tests passed** in isolated Python Linux container, no Docker socket mounted. Covers pull failure/no stop, startup failure/old pair recovery and failed exit, failed recovery retaining transaction, explicit rollback/pending verification, incomplete external evidence, competing pending deploy, lock contention, maintenance/config permissions, malformed forced SSH commands |
| Real disposable runtime/bundle | Bundle migrated a new temporary PostgreSQL 16 database. API readiness passed using a separately granted non-superuser fixture role; Docker inspection confirmed NanoCpus 250000000, Memory 536870912, non-root/read-only runtime |
| Actual browser smoke against that runtime | **3 passed**: two independent browsers create/join and receive game transition; solo selects/plays a hand; failed hub negotiation shows inert old UI and reload creates a usable new lobby. No live application/database was targeted |
| Cleanup | Only generated fixture containers/network removed. Host DB and other Docker resources untouched |

Reproduction: `py deploy/linode/check-local-containers.py` after building both image tags and publishing client to `artifacts/client-wp04`; `docker run --rm -v "${PWD}:/work:ro" python:3.13-bookworm python /work/deploy/linode/test_release.py` for failure paths. The container fixture uses bridge networking with a loopback-only host port on Docker Desktop; Linode host networking remains live verification in WP07. The fixture's DB role is not evidence that production roles have been fixed.

The second browser run passed after correcting HTTP fixture shutdown, with no teardown thread exception. The GitHub build-only workflow now builds both images and runs these failure/runtime/browser checks; no workflow has been published or executed remotely yet. Existing dependency/compiler/Razor advisories remain visible and are not suppressed.

## Operational handoff

WP06 must first verify shared-host backup/restore protection, then provision scoped roles/configs, pull-only ACR credentials and root-owned tooling. Coordinate the exposed shared postgres credential across ExpenseTracker and TransitJazz rather than blindly rotating it. No production schema change is required by this relocation; compare and rehearse the existing three PokerAttack migrations while preserving shared EF history. Cloud Firewall attachment remains unverified; host UFW was inactive. LISH is owner-confirmed.

WP07 still owes public HTTPS, full game/persistence/private-message proof, live rollback/interruption/reboot and soak/monitoring evidence. The three local browser smoke tests do not satisfy that full pilot gate. WP08 cutover remains separate; production clients still select ACA.
