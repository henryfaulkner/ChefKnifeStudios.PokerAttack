# WP02 preparation — 2026-11-14

**Current acceptance update:** COMPLETE for live pipeline isolation and local baseline. Owner explicitly confirmed LISH (`ssh -t henryfaulkner-cks@lish-us-southeast.linode.com expense-tracker-vm`) and accepted recovery defaults with a proportionate hobby-project approach. Owner believes no cloud firewall; attachment remains unverified. Earlier pending notes below are historical. No publication or deployment.

Status: IN PROGRESS; WP01 acceptance and baseline gates remain open. Source `b6a4d17d8eb42e9f9bee8214021406ff8be22659` plus local edits on `migration/linode-server-hosting`. No commit, push, image publication, workflow run or production deployment performed.

## Owner-directed delivery change

The owner requested GitHub Actions delivery similar to TransitJazz, superseding the original Azure Pipelines delivery decision. Retain Azure SWA and ACR. Legacy Azure pipelines must stay disabled while the replacement is prepared. The requested Downloads reference directory was absent; used the existing `C:\Projects\ChefKnifeStudios.TransitJazz` checkout and live PR 77/head verification instead.

WP09 will configure GitHub environment protection, branch restrictions, dedicated restricted SSH identity and pinned host key, deployment concurrency without cancelling active releases, ACR authentication, immutable digest provenance and external gameplay promotion. The host lock and maintenance/rollback requirements still apply. Do not copy TransitJazz credentials, account, deployment paths or game-inappropriate promotion behavior.

## B01 live safety controls

Organization `https://dev.azure.com/henryfaulkner`, project `ChefKnifeStudios.DevOps`:

| Definition | YAML | Previous queue status | Verified new queue status |
| --- | --- | --- | --- |
| 39 PokerAttack - Server | `deploy/server-pipeline.yml` | enabled | disabled |
| 40 PokerAttack - Client | `deploy/client-pipeline.yml` | enabled | disabled |
| 41 henryfaulkner.ChefKnifeStudios.PokerAttack | `deploy/e2e-nightly-pipeline.yml` | enabled | disabled |

Changed through read/modify/PUT of the exact definition with identity checks and preserved revision; narrow response confirmed status for each and independent list query agreed. UI CI triggers inherit YAML; PR triggers were enabled (server/nightly main, client deploy). Initial notStarted/inProgress queue queries returned empty. Other definitions were untouched. Variable group `Prod-PokerAttack` ID 8 was identified without exporting its secret values.

Manual recovery of queue status is possible by read/modify/PUT back to `enabled`, but must not happen until the referenced YAML and release paths are safe. Re-enabling the old main-branch server/nightly definitions now would restore the original deployment/cleanup hazard. Client publication remains intentionally possible through its gated YAML after controlled re-enablement; it is not presently a verified rollback procedure.

## Local changes

- Server Azure YAML: no automatic CI/PR trigger, no deployment/migration or production E2E stages; retains legacy build/push capability but definition stays disabled. Do not run image publication before B03/WP05 removes embedded development credentials.
- Nightly YAML: no trigger/schedule or production variable group/test call; deliberate execution fails with an explanation until isolated WP04 tests exist.
- Client Azure YAML: no automatic trigger, explicit default-false production publication parameter, manual-main-only production stage. Current client endpoint files unchanged.
- Manual server entry point: local build only, required immutable tag, rejects `latest`; no registry publication, migrations or ACA updates.
- BL test project: `net9.0` to `net10.0`, matching its references.
- GitHub `migration-ci.yml`: read-only repository permissions, isolated BL tests, API Release build, client Release artifact; no production credentials, deployment or cleanup jobs. Test failures block the workflow; the local baseline now passes. Workflow not yet published or executed remotely.

## Verification and blockers

- BL first run failed compilation (CS0234/CS0246) because tests used deleted `ILobbyRepository`, old constructor and string players. Updated existing tests to `IKeyValueRepository<Lobby>`, current DTO/model and Result contracts; supplied mocks for the current constructor. Preserved all eleven scenarios, adapting missing-lobby expectation to the current NotFound result. Final `dotnet test ... -c Release --verbosity quiet`: PASSED 11, failed 0, skipped 0. No gameplay implementation changed.
- API Release build: PASSED, zero errors, 22 warnings in the sampled output. Existing dependency advisories and source warnings recorded; no packages changed during this run.
- Client Release publish: PASSED, exit 0; output path `%TEMP%\pokerattack-wp02-client`. This fresh build is not the saved deployed rollback artifact.
- All four YAML files parsed using Azure CLI Python/PyYAML 6.0.2; safety assertions verified server build-only stages, disabled automatic triggers, nightly absence of npm, manual/main/default-false client publication, and read-only GitHub job. PowerShell parser reported no errors. `git diff --check` passed. Hosted workflow execution and GitHub environment protections remain unverified.

Next: finish WP01 provider/console and DB permission/credential/operations gates, then accept WP02. The saved rollback artifact is now verified; restoration rehearsal remains WP07. WP03 hosting implementation has not begun. B04 remains unchecked; later packages add hosting/client/container verification.
