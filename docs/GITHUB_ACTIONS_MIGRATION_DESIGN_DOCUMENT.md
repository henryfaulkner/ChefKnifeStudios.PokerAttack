# GitHub Actions Migration Design Document

**Status:** Proposed
**Date:** 2026-08-03
**Scope:** Replace Azure DevOps pipelines (`deploy/*.yml`) with GitHub Actions workflows (`.github/workflows/*.yml`)
**Gold standard:** `C:\Projects\ChefKnifeStudios.TransitJazz` — `.github/workflows/{server,client}.yml`

---

## 1. Motivation

PokerAttack already lives on GitHub (`github.com/henryfaulkner/ChefKnifeStudios.PokerAttack`) but deploys from Azure DevOps. That split means two systems to reason about: code review and issues in one place, build logs and secrets in another. TransitJazz — same Azure resource shape (ACR, Container Apps, Static Web Apps), same .NET 10 Blazor WASM + containerized API stack — runs entirely on GitHub Actions with a markedly simpler pipeline definition.

This document specifies the migration, following TransitJazz's conventions wherever the two projects align and calling out explicitly where PokerAttack's extra requirements force a divergence.

**Goals**

- Deployment triggered from GitHub, secrets managed in GitHub
- Workflow files that read like TransitJazz's (flat `jobs`, `env` block at top, minimal ceremony)
- No behavioral regression: same images, same registry, same Container App, same Static Web App, migrations still run before the API rolls
- Azure DevOps pipelines retired only after a verified green deploy

**Non-goals**

- Changing Azure infrastructure, resource names, or the Docker build itself
- Introducing staging (tracked separately in `docs/staging-deployment-guide.md`)
- Migrating the nightly E2E pipeline (see §7)

---

## 2. Current state (Azure DevOps)

### `deploy/server-pipeline.yml`

Three stages on `ubuntu-latest`, triggered by pushes to `main` and `fix-data-docker`:

1. **BuildAndPublish** — Docker service-connection login → `docker compose build` (env: `CONTAINER_REGISTRY`, `CONTAINER_TAG=$(Build.BuildNumber)`) → push both images.
2. **DeployProd** — `deployment` job bound to the `Prod` environment with variable group `Prod-PokerAttack`. Runs the migration container (`docker run --rm -e CONNECTIONSTRINGS__POKERATTACKDB`), then `AzureContainerApps@1` to deploy `poker-attack-api` in `poker-attack-rg`.
3. **E2ETestProduction** — Node 20, `npm ci`, Playwright chromium, `npm test` against prod URLs. `continueOnError: true`; publishes JUnit results and the HTML report.

### `deploy/client-pipeline.yml`

Two stages, triggered by `main` and `deploy`:

1. **BuildAndPublish** — .NET 10 SDK, `dotnet workload install wasm-tools`, `dotnet publish` the WebApp zipped into a pipeline artifact.
2. **DeployProd** — extract the zip, patch `staticwebapp.config.json` with `jq` to set `globalHeaders["Blazor-Environment"] = "prod"`, deploy via `AzureStaticWebApp@0` using `$(PokerAttackWebDeploymentToken)`.

### `deploy/e2e-nightly-pipeline.yml`

Schedule block is entirely commented out; the pipeline currently only fires on pushes to `main`/`e2e`, duplicating the post-deploy E2E stage. See §7.

### Key resources

| Thing | Value |
|---|---|
| Registry | `chefknife.azurecr.io` |
| API image | `chefknifestudios.pokerattack.server.webapi` |
| Migration image | `chefknifestudios.pokerattack.server.data` |
| Container App | `poker-attack-api` |
| Resource group | `poker-attack-rg` |
| Container App env | `poker-attack-api-env` |
| Client project | `src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp` (net10.0) |

---

## 3. Gold standard (TransitJazz)

`server.yml` is 58 lines and does the whole job:

- `on: push: branches: [main]`
- Top-level `env:` block holding `REGISTRY`, `IMAGE_NAME`, `CONTAINER_APP_NAME`, `RESOURCE_GROUP` — every literal in one place
- **`build-and-push` job** — `docker/login-action@v3` with `ACR_USERNAME`/`ACR_PASSWORD`, `docker compose build` with `CONTAINER_TAG: ${{ github.sha }}`, push `:sha` and re-tag `:latest`. Exposes `image-tag` as a job output.
- **`deploy` job** — `needs: build-and-push`, `azure/login@v2` with `AZURE_CREDENTIALS`, then `azure/cli@v2` running `az containerapp update --image ...:${{ needs.build-and-push.outputs.image-tag }}`

`client.yml` follows the same two-job split: build (setup-dotnet, wasm-tools, `dotnet publish`, `upload-artifact`) then deploy (`download-artifact`, heredoc-write `staticwebapp.config.json`, `Azure/static-web-apps-deploy@v1`).

**Conventions worth carrying over**

1. Commit SHA as the image tag, with a floating `:latest` alongside — immutable and traceable, no build-number counter to maintain.
2. Job outputs to pass the tag from build to deploy rather than recomputing it.
3. Literals hoisted into `env:`; secrets referenced inline.
4. Artifact handoff between jobs instead of one long job.
5. `staticwebapp.config.json` written wholesale via heredoc rather than patched with `jq` — the deployed config is visible in the workflow file.
6. Marketplace actions over shell wherever one exists (`docker/login-action`, `azure/login`, `Azure/static-web-apps-deploy`).

---

## 4. Target design

Two workflows: `.github/workflows/server.yml` and `.github/workflows/client.yml`. The directory already exists and is empty.

### 4.1 Divergences from TransitJazz — and why

| # | Divergence | Rationale |
|---|---|---|
| D1 | Two images built and pushed, not one | PokerAttack ships an EF Core migration bundle image (`server.data`) alongside the API. `docker compose build` already builds both; the push step just lists two images. |
| D2 | Migration container runs as a step inside the `deploy` job | PokerAttack must apply schema changes before the API rolls. **Decision: inline, not a separate job** — keeps the two-job shape, and migration + deploy genuinely are one atomic operation. A failed migration fails the job before `az containerapp update` runs, which is exactly the desired gate. |
| D3 | `Blazor-Environment` header value is `prod`, not `Production` | Matches the value the current ADO pipeline injects. Changing it is a client-behavior change, out of scope here. |
| D4 | Client `staticwebapp.config.json` retains `mimeTypes` | The checked-in config at `src/Client/.../wwwroot/staticwebapp.config.json` sets `.js → application/javascript`. TransitJazz's heredoc has no `mimeTypes`; dropping it would be a silent regression. |
| D5 | Post-deploy E2E job on the server workflow | No TransitJazz equivalent. Preserved from ADO (§7). |

### 4.2 `server.yml`

```yaml
name: Server CI/CD

on:
  push:
    branches: [main]

env:
  REGISTRY: chefknife.azurecr.io
  API_IMAGE_NAME: chefknifestudios.pokerattack.server.webapi
  MIGRATION_IMAGE_NAME: chefknifestudios.pokerattack.server.data
  CONTAINER_APP_NAME: poker-attack-api
  RESOURCE_GROUP: poker-attack-rg

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    outputs:
      image-tag: ${{ steps.set-tag.outputs.tag }}
    steps:
      - uses: actions/checkout@v4

      - name: Log in to ACR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ secrets.ACR_USERNAME }}
          password: ${{ secrets.ACR_PASSWORD }}

      - name: Build Docker images
        env:
          CONTAINER_REGISTRY: ${{ env.REGISTRY }}
          CONTAINER_TAG: ${{ github.sha }}
        run: docker compose build

      - name: Set image tag output
        id: set-tag
        run: echo "tag=${{ github.sha }}" >> $GITHUB_OUTPUT

      - name: Push images
        run: |
          docker push ${{ env.REGISTRY }}/${{ env.API_IMAGE_NAME }}:${{ github.sha }}
          docker tag ${{ env.REGISTRY }}/${{ env.API_IMAGE_NAME }}:${{ github.sha }} ${{ env.REGISTRY }}/${{ env.API_IMAGE_NAME }}:latest
          docker push ${{ env.REGISTRY }}/${{ env.API_IMAGE_NAME }}:latest
          docker push ${{ env.REGISTRY }}/${{ env.MIGRATION_IMAGE_NAME }}:${{ github.sha }}
          docker tag ${{ env.REGISTRY }}/${{ env.MIGRATION_IMAGE_NAME }}:${{ github.sha }} ${{ env.REGISTRY }}/${{ env.MIGRATION_IMAGE_NAME }}:latest
          docker push ${{ env.REGISTRY }}/${{ env.MIGRATION_IMAGE_NAME }}:latest

  deploy:
    needs: build-and-push
    runs-on: ubuntu-latest
    environment: Prod
    steps:
      - name: Log in to ACR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ secrets.ACR_USERNAME }}
          password: ${{ secrets.ACR_PASSWORD }}

      - name: Run DB migrations
        env:
          CONNECTIONSTRINGS__POKERATTACKDB: ${{ secrets.CONNECTIONSTRINGS_POKERATTACKDB }}
        run: |
          docker run --rm -e CONNECTIONSTRINGS__POKERATTACKDB \
            ${{ env.REGISTRY }}/${{ env.MIGRATION_IMAGE_NAME }}:${{ needs.build-and-push.outputs.image-tag }}

      - name: Azure login
        uses: azure/login@v2
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}

      - name: Update Container App
        uses: azure/cli@v2
        with:
          azcliversion: latest
          inlineScript: |
            az containerapp update \
              --name ${{ env.CONTAINER_APP_NAME }} \
              --resource-group ${{ env.RESOURCE_GROUP }} \
              --image ${{ env.REGISTRY }}/${{ env.API_IMAGE_NAME }}:${{ needs.build-and-push.outputs.image-tag }}
```

Notes:

- The deploy job re-logs into ACR because it needs to `docker pull` the migration image; it does not check out the repo (nothing in the working tree is used), matching TransitJazz's deploy job.
- `docker run --rm -e VAR` with no `=value` passes the variable through from the runner env — the secret never appears in the command line or logs. Same trick the ADO pipeline uses.
- `environment: Prod` gives a GitHub deployment record and a place to scope the DB secret and, optionally, require manual approval.

### 4.3 `client.yml`

```yaml
name: Client CI/CD

on:
  push:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      - name: Install WASM tools
        run: dotnet workload install wasm-tools

      - name: Publish Blazor app
        run: |
          dotnet publish \
            src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/ChefKnifeStudios.PokerAttack.Client.WebApp.csproj \
            -c Release /p:BlazorEnableCompression=false

      - uses: actions/upload-artifact@v4
        with:
          name: blazor-client
          path: src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/bin/Release/net10.0/publish/wwwroot

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment: Prod
    steps:
      - uses: actions/download-artifact@v4
        with:
          name: blazor-client
          path: ./client

      - name: Inject production environment header
        run: |
          cat > ./client/staticwebapp.config.json <<'EOF'
          {
            "globalHeaders": {
              "Blazor-Environment": "prod"
            },
            "navigationFallback": {
              "rewrite": "/index.html"
            },
            "mimeTypes": {
              ".js": "application/javascript"
            }
          }
          EOF

      - name: Deploy to Static Web App
        uses: Azure/static-web-apps-deploy@v1
        with:
          azure_static_web_apps_api_token: ${{ secrets.POKERATTACK_WEB_DEPLOYMENT_TOKEN }}
          action: upload
          app_location: ./client
          output_location: ""
          skip_app_build: true
```

`/p:BlazorEnableCompression=false` is carried over from TransitJazz — Static Web Apps serves its own compression, and pre-compressed Brotli assets have historically caused MIME issues there. This is a change from the ADO pipeline, which did not set it; see §8 for the verification step.

---

## 5. Secrets

Configure under **Settings → Secrets and variables → Actions**. Repository-scoped unless noted.

| Secret | Source | Notes |
|---|---|---|
| `ACR_USERNAME` | ACR → Access keys → Username | Enable admin user, or use a scoped token |
| `ACR_PASSWORD` | ACR → Access keys → password | |
| `AZURE_CREDENTIALS` | `az ad sp create-for-rbac --sdk-auth` | Full JSON blob. Scope to `poker-attack-rg` with Contributor |
| `CONNECTIONSTRINGS_POKERATTACKDB` | ADO variable group `Prod-PokerAttack` → `ConnectionStrings-PokerAttackDB` | **Scope to the `Prod` environment**, not the repo |
| `POKERATTACK_WEB_DEPLOYMENT_TOKEN` | Static Web App → Manage deployment token | Was `PokerAttackWebDeploymentToken` |

Service principal creation:

```bash
az ad sp create-for-rbac \
  --name "poker-attack-github-actions" \
  --role contributor \
  --scopes /subscriptions/<SUB_ID>/resourceGroups/poker-attack-rg \
  --sdk-auth
```

GitHub secret names can't contain `-`, hence the underscore renames. Values are unchanged — copy them across from the ADO variable group.

**Authentication approach:** mirrors TransitJazz (ACR admin credentials + service principal JSON). OIDC federated credentials would remove the long-lived secrets and are Microsoft's current recommendation, but they diverge from the gold standard and require Entra app registration. Worth revisiting once both repos can move together.

---

## 6. Branch triggers

The ADO pipelines fire on assorted feature branches (`fix-data-docker`, `deploy`, `e2e`) that accumulated over time. The GitHub workflows deploy from `main` only, matching TransitJazz.

If you want to deploy from a branch during the cutover, add it temporarily:

```yaml
on:
  push:
    branches: [main, some-branch]
```

Prefer `workflow_dispatch` for one-off deploys once the migration settles.

---

## 7. E2E tests

**Decision: port the post-deploy E2E stage; drop the nightly pipeline.**

The nightly pipeline's `schedules:` block is entirely commented out — it currently fires on pushes to `main`/`e2e` and duplicates the post-deploy stage. There is nothing nightly about it, so it is not carried over. If a real scheduled run is wanted later, GitHub's `on: schedule: - cron: '0 2 * * *'` gives it directly, with no pipeline file to resurrect.

Append to `server.yml`:

```yaml
  e2e-tests:
    needs: deploy
    runs-on: ubuntu-latest
    environment: Prod
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-node@v4
        with:
          node-version: '20'

      - name: Install dependencies
        working-directory: tests
        run: npm ci

      - name: Install Playwright browsers
        working-directory: tests
        run: npx playwright install chromium --with-deps

      - name: Run E2E tests against production
        working-directory: tests
        continue-on-error: true
        env:
          CLIENT_URL: ${{ secrets.TEST_CLIENT_URL }}
          API_URL_CLEANUP: ${{ secrets.TEST_API_URL }}
          CI: true
        run: npm test

      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: e2e-report-${{ github.sha }}
          path: |
            tests/playwright-report
            tests/test-results
```

`continue-on-error: true` preserves the ADO `continueOnError` semantics — a failing test surfaces in the run but does not mark the deploy failed. GitHub Actions has no built-in JUnit publisher, so `tests/test-results/junit.xml` rides along in the uploaded artifact. If inline PR annotations become desirable, `dorny/test-reporter` is the usual addition.

Two additional secrets: `TEST_CLIENT_URL` and `TEST_API_URL` (from `TestClientUrl` / `TestApiUrl` in the ADO variable group).

---

## 8. Migration plan

**Phase 1 — Prepare (no cutover)**
1. Create the five secrets in §5 plus the two in §7.
2. Create the `Prod` GitHub Environment; scope `CONNECTIONSTRINGS_POKERATTACKDB`, `TEST_CLIENT_URL`, and `TEST_API_URL` to it. Optionally add a required reviewer.
3. Add `.github/workflows/server.yml` and `client.yml` on a feature branch with `on: workflow_dispatch` only.

**Phase 2 — Verify**
4. Run `client.yml` manually. Confirm the SWA serves the app, `Blazor-Environment: prod` is present on the response, and `.js` assets carry the right MIME type. **Specifically verify the app loads with `BlazorEnableCompression=false`** — this differs from the ADO build.
5. Run `server.yml` manually. Confirm both images land in ACR at the commit SHA, migrations apply, and `poker-attack-api` picks up the new revision.
6. Confirm the E2E job runs and uploads its report.

**Phase 3 — Cut over**
7. Change both workflows to `on: push: branches: [main]`, merge to `main`.
8. Disable the ADO pipelines (`server-pipeline`, `client-pipeline`, `e2e-nightly-pipeline`) — disable, don't delete, until a few deploys have gone green.
9. Push a trivial change to `main`; verify end to end.

**Phase 4 — Clean up**
10. Delete `deploy/server-pipeline.yml`, `deploy/client-pipeline.yml`, `deploy/e2e-nightly-pipeline.yml`.
11. Keep `deploy/manual-build-and-deploy-server.ps1` — it's a useful local escape hatch, independent of any CI system.
12. Delete the ADO pipeline definitions and the `Prod-PokerAttack` variable group once secrets are confirmed migrated.
13. Remove the `PokerAttackSC_2026` and `AzureContainerRegistrySC` service connections.

---

## 9. Risks

| Risk | Mitigation |
|---|---|
| Migration container can't reach the DB from a GitHub runner | Same public egress model as ADO hosted agents. If the DB has an IP allowlist, GitHub's runner ranges differ from ADO's — verify in Phase 2 step 5, before cutover. |
| `BlazorEnableCompression=false` changes client asset delivery | Explicit verification step in Phase 2. Drop the flag if it causes trouble; it is not required by anything else here. |
| ACR admin credentials are long-lived | Accepted for parity with TransitJazz. Rotate on the ACR access-keys blade; revisit OIDC later. |
| Losing ADO's `Prod` environment approval gate | Reproduced by the `Prod` GitHub Environment with a required reviewer. |
| Both CI systems deploying at once during cutover | Phase 3 disables ADO immediately after switching triggers; avoid pushing to `main` between steps 7 and 8. |
| `:latest` re-tagging races with a concurrent run | Deploys pin the SHA tag, so `:latest` is convenience only. Add `concurrency: group: server-deploy` if parallel merges to `main` become common. |

---

## 10. Files affected

**Added**
- `.github/workflows/server.yml`
- `.github/workflows/client.yml`

**Deleted (Phase 4)**
- `deploy/server-pipeline.yml`
- `deploy/client-pipeline.yml`
- `deploy/e2e-nightly-pipeline.yml`

**Unchanged**
- `docker-compose.yml` — already parameterized by `CONTAINER_REGISTRY` / `CONTAINER_TAG`
- Both Dockerfiles
- `src/Client/.../wwwroot/staticwebapp.config.json` — stays `Development` for local runs; the deploy job overwrites it
- `deploy/manual-build-and-deploy-server.ps1`
