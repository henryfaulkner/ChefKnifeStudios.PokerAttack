# .NET 10 Big Bang Upgrade — Tasks

## Overview

Tasks to perform the atomic Big Bang upgrade of the `ChefKnifeStudios.PokerAttack` solution from .NET 9.0 → .NET 10.0 (Preview). These tasks apply the strategy rule: combine project file updates + package updates + compilation fixes into a single atomic upgrade task, keep prerequisites separate, and verify Docker/config and runtime separately. Non-automatable manual UI checks are excluded — see Plan and Assessment for manual validation steps.

**Progress**: 4/5 tasks complete (80%) ![80%](https://progress-bar.xyz/80)

## Tasks

### [✓] TASK-001: Verify prerequisites (SDK & Docker) *(Completed: 2025-11-11 12:56)*
**References**: Plan §Step 1: Verify Prerequisites, Plan §Preparation, .github/upgrades/assessment.md

- [✓] (1) Verify .NET 10 SDK is installed on the workstation: `dotnet --list-sdks` and `dotnet --info` (expected `10.0.xxx` or higher) per Plan §Step 1.
- [✓] (2) Verify Docker is available: `docker --version` (if Docker-based validation will be run) per Plan §Step 1. (**Verify**)
- [✓] (3) Verify required CLI tooling is available (e.g., `dotnet` CLI). Do not perform backups or branch operations in this task.

### [✓] TASK-002: Atomic framework and package upgrade (all projects) *(Completed: 2025-11-11 12:55)*
**References**: Plan §Step 2: Update Project Files, Plan §Step 3 Package Update Reference, Plan §Breaking Changes Catalog

- [✓] (1) Update `TargetFramework` to `net10.0` in all projects listed in Plan §Step 2 (all 11 projects). See Plan §Step 2 for exact project list and paths.
- [✓] (2) Update NuGet package references per Plan §Package Update Reference (all package upgrades listed there — critical updates include EF Core → 10.0.0, Blazor packages → 10.0.0, Aspire.AppHost → 13.0.0, OpenTelemetry → 1.14.0-rc.1). Reference Plan §Package Update Reference for per-project mapping.
- [✓] (3) Run `dotnet restore` for the solution (no discovery; use projects listed in Plan §Step 2) and ensure all projects restore successfully. (**Verify**)
- [✓] (4) Build the solution and fix compilation errors per Plan §Step 6 and Plan §Breaking Changes Catalog. Build in dependency order (Plan §2.2 Dependency-Based Ordering) to isolate issues:
    - Build foundation layer, then core, then presentation, then application, then orchestration as in Plan §2.2.
- [✓] (5) Rebuild solution after fixes and confirm solution builds with 0 errors (**Verify**)
- [✓] (6) Document any breaking-change code fixes and reference Plan §Breaking Changes Catalog in commit message notes.

### [✓] TASK-003: Update Dockerfiles and verify image builds *(Completed: 2025-11-11 13:11)*
**References**: Plan §Step 4: Update Dockerfiles, Plan §Step 7: Verify Docker Builds

- [✓] (1) Update Dockerfile base images and EF tool references as specified in Plan §Step 4 (e.g., change SDK/runtime tags from `9.0` → `10.0`, update `dotnet-ef` tool version in Server.Data Dockerfile per Plan).
- [✓] (2) Build Server.Data Docker image and Server.WebAPI Docker image using the paths in Plan §Step 4 / §Step 7:
    - `docker build -f <path-to-Server.Data-Dockerfile> -t poker-attack-data-image .`
    - `docker build -f <path-to-Server.WebAPI-Dockerfile> -t poker-attack-api-image .`
- [✓] (3) Confirm Docker images build successfully (exit code 0 and expected output) (**Verify**)
- [✓] (4) If images fail to build, fix Dockerfile or build steps and re-run image builds; verify success (bounded to one fix-and-rebuild pass) (**Verify**)

### [✓] TASK-004: Automated runtime & integration validation (non-visual) *(Completed: 2025-11-11 13:43)*
**References**: Plan §Step 8 Runtime Validation, Plan §Testing Strategy, Plan §Integration Testing

- [✓] (1) Start required services for automated checks (use `dotnet run` or Docker run as appropriate) per Plan §Step 8 — specifically start `ChefKnifeStudios.PokerAttack.AppHost` and `Server.WebAPI` to enable automated health checks. Do not include manual browser checks.
- [✓] (2) Verify health endpoints respond (HTTP 200) for `Server.WebAPI` and AppHost health endpoints per Plan §Integration Testing (**Verify**)
- [✓] (3) Verify OpenAPI/Swagger endpoint returns 200 and expected schema metadata exists (automated GET to OpenAPI endpoint) (**Verify**)
- [✓] (4) Verify basic SignalR negotiation endpoint or health check (if present) responds (automated HTTP check) per Plan §Integration Testing (**Verify**)
- [✓] (5) Validate EF Core tooling / DB connectivity in automated mode:
    - Run `dotnet ef migrations list` (or other non-destructive EF check) against `Server.Data` project to confirm EF tools function with updated packages per Plan §Server.Data. (**Verify**)
- [✓] (6) If any automated check fails, record failures, apply code/package fixes described in Plan §Breaking Changes Catalog, rebuild, and re-run checks (bounded fix pass). Do not perform manual UI verification tasks in this task.

### [▶] TASK-005: Final commit of atomic upgrade
**References**: Plan §Source Control Strategy, Plan §Commit Strategy (Big Bang)

- [▶] (1) Commit all changes on `upgrade-to-NET10` branch with the atomic commit message recommended in Plan §Source Control Strategy (include summary of projects updated, packages updated, Dockerfile updates, and notable breaking-change fixes).
- [▶] (2) Confirm commit succeeded and push branch if required by workflow (**Verify**)
- [▶] (3) Tag release if specified in Plan §Source Control Strategy (optional per plan). (**Verify**)

---

Generation checklist (applied):
- Strategy batching rules (combine project+package+compilation; separate prerequisites/testing) used from Tasks Generation guidance.
- Plan operations referenced (project list, package matrix, Docker updates, validation order).
- Non-automatable/manual UI items excluded (visual testing, manual browser checks).
- Large lists referenced (Plan §Package Update Reference, Plan §Step 2) instead of duplicating.
- All verifications are deterministic and bounded.
- Tasks are flat, sequential, and LLM-executable.

For manual verification steps (visual or exploratory testing) see Plan §Runtime Validation and Plan §Testing Strategy — those are intentionally omitted from automatable tasks.