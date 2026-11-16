# A01 starting state

Inspected 2026-11-15T14:55:34Z. Source `55c3a413151fc9dfae0e8a7cad4c0230cab13e46`, branch `migration/linode-server-hosting`, origin `henryfaulkner/ChefKnifeStudios.PokerAttack`.

`git status --short` initially reported only untracked `.claude/skills/`, `docs/DEPLOYMENT_AUTOMATION_PLAN.md`, and `docs/DEPLOYMENT_AUTOMATION_CHECKLIST.md`. These are preserved. No tracked modifications existed. Ancestor instruction checks covered `C:/AGENTS.md`, `C:/Projects/AGENTS.md`, the repository root, and a hidden-file recursive `rg --files` search: no applicable AGENTS.md found.

Read the complete automation plan/checklist, release README, production operations, migration work-package decisions/status/WP09, both GitHub workflows, and TransitJazz's client/server workflows. The existing implementation includes Docker packaging, host deploy/rollback/promote with protected local files, isolated tests, manual SWA publisher and a client workflow on this branch. No server workflow or monitoring collector exists.

Current source is in open PR [12](https://github.com/henryfaulkner/ChefKnifeStudios.PokerAttack/pull/12); GitHub main is `b6a4d17d8eb42e9f9bee8214021406ff8be22659`. Local artifacts are not evidence of main publication. No source publication/merge performed.

Conflicts resolved for implementation: the README still describes ACA authority during first-deploy recovery and operator-only maintenance/evidence; current operations/plan explicitly retire ACA and require capability-separated automation after bootstrap. Old WP09 Azure Pipelines instructions are historical. Keep the existing controlled path until rehearsed replacement capabilities are installed. The plan's single Docker dashboard supersedes the broader deferred observability program. General implementation authorization does not replace a release-specific reset acknowledgment or promotion decision.

Verification: read-only repository inspection completed. Subsequent inventory results are recorded separately. Next: A02 GitHub inventory, then A03–A06. No production mutation.
