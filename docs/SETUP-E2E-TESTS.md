# E2E Test Integration Setup Guide

This guide walks you through setting up end-to-end tests in your Azure DevOps pipelines.

## Overview

The implementation adds:
- **Post-deployment smoke tests** that run after production deployment
- **Nightly scheduled tests** at 2:00 AM
- Test results published to Azure DevOps for tracking

## Prerequisites

- Production environment already deployed (client on Azure Static Web Apps, API on Azure Container Apps)
- Azure DevOps project with existing pipelines
- Access to Azure DevOps variable groups

## Step 1: Update Azure DevOps Variable Group

### 1.1 Navigate to Variable Groups

1. Open your Azure DevOps project
2. Go to **Pipelines** → **Library**
3. Find and click on the **`Prod-PokerAttack`** variable group

### 1.2 Add Required Variables

Add the following two variables:

| Variable Name | Value | Description |
|--------------|-------|-------------|
| `TestClientUrl` | `https://henryfaulkner.xyz` (or `https://www.henryfaulkner.xyz`) | Production client URL where tests navigate |
| `TestApiUrl` | `https://poker-attack-api.ambitioussmoke-21ab3755.eastus.azurecontainerapps.io` | Production API URL for cleanup endpoint |

**To get your API URL:**
```bash
# Run this in Azure CLI or Cloud Shell
az containerapp show \
  --name poker-attack-api \
  --resource-group poker-attack-rg \
  --query properties.configuration.ingress.fqdn \
  --output tsv
```

This will output something like: `poker-attack-api.ambitioussmoke-21ab3755.eastus.azurecontainerapps.io`

Then your full API URL is: `https://poker-attack-api.ambitioussmoke-21ab3755.eastus.azurecontainerapps.io`

**To get your Client URL:**
Your client URL is your production website domain (e.g., `https://henryfaulkner.xyz`)

### 1.3 Save the Variable Group

Click **Save** to save the variable group changes.

## Step 2: Set Up the Nightly Pipeline in Azure DevOps

### 2.1 Create the Pipeline

1. Go to **Pipelines** → **Pipelines**
2. Click **New pipeline**
3. Select **Azure Repos Git** (or your repository source)
4. Select your repository
5. Choose **Existing Azure Pipelines YAML file**
6. Browse to: `/deploy/e2e-nightly-pipeline.yml`
7. Click **Continue**
8. Click **Run** to create the pipeline (it will run once)

### 2.2 Rename the Pipeline (Optional)

1. Find the newly created pipeline in your pipeline list
2. Click the **...** menu next to the pipeline name
3. Select **Rename/move**
4. Rename to: **E2E Nightly Tests**
5. Click **Save**

### 2.3 Verify the Schedule

The pipeline is configured to run at 2:00 AM daily. To verify:

1. Open the **E2E Nightly Tests** pipeline
2. Click **Edit**
3. Look at the top of the YAML file - you should see:
   ```yaml
   schedules:
   - cron: '0 2 * * *'
     displayName: 'Nightly E2E Test Run'
   ```

**Note:** Azure DevOps uses UTC time for CRON schedules. If you're in a different timezone, you may need to adjust:
- **EST (UTC-5):** Use `0 7 * * *` for 2 AM EST
- **PST (UTC-8):** Use `0 10 * * *` for 2 AM PST
- **CST (UTC-6):** Use `0 8 * * *` for 2 AM CST

Current setting `0 2 * * *` runs at 2 AM UTC.

## Step 3: Verify Server Pipeline Updates

The server pipeline (`server-pipeline.yml`) has been updated to include e2e tests after production deployment.

**No action needed** - the next time you push to `main` or `fix-data-docker` branches, the pipeline will automatically run e2e tests after deploying to production.

## Step 4: Test the Setup

### 4.1 Test the Nightly Pipeline

Don't wait until 2 AM! Test it now:

1. Go to **Pipelines** → **Pipelines**
2. Find **E2E Nightly Tests**
3. Click **Run pipeline**
4. Click **Run**
5. Watch the pipeline execute

**Expected result:** Tests should run and publish results (pass or fail).

### 4.2 Test the Deployment Pipeline

1. Make a small change to your code (or trigger pipeline manually)
2. Push to `main` branch
3. Watch the **Server Pipeline** run
4. After the `DeployProd` stage completes, you should see `E2ETestProduction` stage start
5. Tests will run against production

**Expected result:**
- Tests run against production
- Results are published
- Pipeline continues even if tests fail (logged as warnings)

## Step 5: View Test Results

### 5.1 View Results from Pipeline Run

1. Open any pipeline run that includes e2e tests
2. Click on the **Tests** tab
3. You'll see test results with pass/fail status
4. Click on individual tests to see details

### 5.2 View Test Reports (HTML)

1. Open a pipeline run
2. Go to the **Summary** tab (or **Pipelines** tab)
3. Scroll down to **Related artifacts**
4. Click on `e2e-test-report-<buildnumber>` or `nightly-e2e-report-<buildnumber>`
5. Download the artifact
6. Extract the ZIP file
7. Open `index.html` in a browser to see the detailed Playwright HTML report

### 5.3 View Test Artifacts (Screenshots/Videos)

If tests fail, screenshots and videos are captured:

1. Open a failed pipeline run
2. Go to **Related artifacts**
3. Download `test-artifacts-<buildnumber>`
4. Extract to view screenshots and failure videos

## Troubleshooting

### Issue: Tests fail with "Cannot navigate to URL"

**Cause:** `TestClientUrl` is incorrect or client is not deployed

**Solution:**
1. Verify `TestClientUrl` in variable group matches your production client URL
2. Try navigating to the URL in your browser - does it work?
3. Check that client deployment is successful

### Issue: Tests fail with "Cleanup endpoint failed"

**Cause:** `TestApiUrl` is incorrect or API is not deployed

**Solution:**
1. Verify `TestApiUrl` in variable group matches your production API URL
2. Try accessing `https://<your-api-url>/health` in browser - does it respond?
3. Check that API deployment is successful
4. Verify the cleanup endpoint exists: `https://<your-api-url>/test/cleanup`

### Issue: Nightly pipeline doesn't run at 2 AM

**Causes:**
1. CRON timezone is UTC, not your local timezone
2. Pipeline needs at least one manual run before schedule activates
3. Azure DevOps has limits on scheduled pipelines

**Solutions:**
1. Adjust CRON expression for your timezone (see Step 2.3)
2. Run the pipeline manually once
3. Check Azure DevOps service status

### Issue: "Variable group not found" error

**Cause:** Pipeline doesn't have permission to access variable group

**Solution:**
1. Go to **Pipelines** → **Library**
2. Click on `Prod-PokerAttack` variable group
3. Click **Pipeline permissions**
4. Add both pipelines:
   - Server Pipeline (your existing server pipeline)
   - E2E Nightly Tests
5. Click **Save**

## Test Behavior

### Post-Deployment Tests (Server Pipeline)

- **When:** After every production deployment
- **Runs against:** Production environment
- **Failure behavior:** Tests are logged but DON'T block deployment
- **Duration:** ~6-9 minutes
- **Purpose:** Smoke test to verify deployment worked

### Nightly Tests

- **When:** 2:00 AM daily (UTC by default)
- **Runs against:** Production environment
- **Failure behavior:** Logged but doesn't fail pipeline
- **Duration:** ~6-9 minutes
- **Purpose:** Regression testing to catch issues overnight

### What the Tests Do

The e2e tests simulate a full multiplayer game session:
- 4 players join a lobby
- Play 3 consecutive poker games
- Validate game flow, state transitions, and SignalR communication
- Test takes 6-9 minutes to complete

**Production Impact:**
- Tests clear in-memory game data before running (via `/test/cleanup` endpoint)
- Creates test lobbies and games that are visible briefly (6-9 min)
- Minimal impact since tests run at 2 AM (low traffic)

## Monitoring Best Practices

1. **Check test results weekly**
   - Review nightly test trends
   - Investigate any failures

2. **Set up notifications (optional)**
   - Azure DevOps → Project Settings → Notifications
   - Create alert for "A test run fails"
   - Email yourself on nightly failures

3. **Review HTML reports for failures**
   - Download artifacts when tests fail
   - Screenshots help debug issues quickly

## Next Steps (Future Enhancements)

When production traffic increases or you need additional safety:

1. **Implement staging environment** (see `staging-deployment-guide.md`)
   - Protects production from test cleanup endpoint
   - Runs tests BEFORE production deployment
   - Costs ~$0-10/month with scale-to-zero

2. **Add test retry logic** (optional)
   - Update `playwright.config.ts` to increase retries for flaky tests
   - Current setting: 2 retries in CI

3. **Add more test scenarios**
   - Additional multiplayer scenarios
   - Edge case testing
   - Performance testing

## Summary

You've successfully set up:
- ✅ Post-deployment smoke tests in server pipeline
- ✅ Nightly scheduled tests at 2 AM
- ✅ Test result tracking in Azure DevOps
- ✅ HTML reports and failure artifacts

**Total setup time:** ~15-30 minutes

**Questions?** Check the plan files:
- `C:\Users\hfaul\.claude\plans\silly-marinating-flask.md` - Full implementation plan
- `C:\Users\hfaul\.claude\plans\staging-deployment-guide.md` - Future staging setup
