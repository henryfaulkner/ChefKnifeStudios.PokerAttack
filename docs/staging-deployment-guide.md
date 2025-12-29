# Staging Environment Deployment Guide

## Overview

This guide provides instructions for creating a staging Azure Container App environment with **scale-to-zero** cost optimization for running e2e tests safely before production deployment.

**Benefits:**
- Protects production data from test cleanup endpoint
- Tests deployments before they reach production
- Scale-to-zero minimizes cost (~$0-10/month vs $25-55/month)
- Same infrastructure as production for realistic testing

**When to implement:**
- Production traffic increases
- Risk tolerance decreases
- Additional safety is desired
- Budget allows for minimal staging costs

---

## Architecture with Staging

```
Build → Deploy Staging → E2E Tests (Staging) → Deploy Production
                ↓
         Tests MUST PASS
         (blocks production deploy)
```

**Key difference from current setup:**
- Tests run against staging BEFORE production deployment
- Test failures block production deployment (quality gate)
- Cleanup endpoint disabled in production for safety

---

## Cost Optimization: Scale-to-Zero Configuration

**Scale-to-Zero Explanation:**
- Container App scales down to 0 instances when not in use
- Only charged for actual compute time during tests
- Startup time: ~10-30 seconds (cold start)
- Perfect for infrequent e2e testing scenarios

**Cost Comparison:**

| Configuration | Monthly Cost | Notes |
|--------------|--------------|-------|
| No staging | $0 | Current setup (production only) |
| Staging (always-on) | $25-55/month | Min 1 replica always running |
| **Staging (scale-to-zero)** | **$0-10/month** | **RECOMMENDED - Only runs during tests** |

---

## Implementation Steps

### 1. Create Staging Azure Container App (Scale-to-Zero)

#### Option A: Azure Portal

1. **Navigate to Azure Portal → Container Apps**
2. **Click "Create"**
3. **Basics tab:**
   - Subscription: Your subscription
   - Resource Group: `poker-attack-rg`
   - Container App name: `poker-attack-api-staging`
   - Region: East US (same as production)
   - Container Apps Environment: `poker-attack-api-env` (shared) or create new `poker-attack-api-staging-env` (isolated)

4. **Container tab:**
   - Use existing image: `chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi:latest`
   - Name: `api`
   - CPU: 0.5 cores
   - Memory: 1.0 Gi

5. **Ingress tab:**
   - ✅ Enable ingress
   - Accepting traffic from: Anywhere
   - Ingress type: HTTP
   - Target port: 8080 (or your API port)

6. **Scale tab** ⭐ KEY FOR COST SAVINGS:
   - **Min replicas: 0** (enables scale-to-zero)
   - **Max replicas: 1** (staging doesn't need multiple instances)
   - Scale rule: HTTP scaling
     - Concurrent requests: 10

7. **Environment Variables:**
   - `ASPNETCORE_ENVIRONMENT` = `Staging`
   - Add any other required environment variables from production

8. **Review + Create**

#### Option B: Azure CLI

```bash
# Create staging container app with scale-to-zero
az containerapp create \
  --name poker-attack-api-staging \
  --resource-group poker-attack-rg \
  --environment poker-attack-api-env \
  --image chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi:latest \
  --target-port 8080 \
  --ingress external \
  --cpu 0.5 \
  --memory 1.0Gi \
  --min-replicas 0 \
  --max-replicas 1 \
  --env-vars ASPNETCORE_ENVIRONMENT=Staging \
  --registry-server chefknife.azurecr.io
```

**Important:** `--min-replicas 0` enables scale-to-zero!

---

### 2. Configure Database for Staging

#### Option A: Shared Database (Simplest)

Use the same production database with schema separation:
- Pros: Free, simple setup
- Cons: Staging tests could impact production DB performance
- Good for: In-memory data repositories (current architecture)

No changes needed - staging uses same connection string as production.

#### Option B: Separate Staging Database (Recommended for SQL)

Create a separate Azure SQL Database (if using SQL):
```bash
# Create staging database (Basic tier for cost savings)
az sql db create \
  --resource-group poker-attack-rg \
  --server <your-sql-server> \
  --name poker-attack-staging-db \
  --service-objective Basic \
  --max-size 2GB
```

Cost: ~$5/month (Basic tier)

#### Option C: No Database (For In-Memory Only)

If your application uses in-memory repositories:
- No database needed
- Data cleared on container restart
- Perfect for stateless staging environments

**For PokerAttack:** Option C is ideal since you use in-memory repositories!

---

### 3. Azure DevOps Variable Group Setup

Create variable group: `Staging-PokerAttack`

```yaml
Variables:
- TestClientUrl: https://<staging-static-web-app>.azurestaticapps.net/ (or staging subdomain)
- TestApiUrl: https://poker-attack-api-staging.<region>.azurecontainerapps.io/
- ConnectionStrings-PokerAttackDB: <staging-db-connection-string> (if applicable)
```

**Get the staging URLs:**
```bash
# Get staging API URL
az containerapp show \
  --name poker-attack-api-staging \
  --resource-group poker-attack-rg \
  --query properties.configuration.ingress.fqdn \
  --output tsv

# Get staging Static Web App URL (if separate staging deployment)
az staticwebapp show \
  --name <your-static-web-app-name> \
  --resource-group poker-attack-rg \
  --query defaultHostname \
  --output tsv
```

**Note on Client URL:**
- **Option A - Separate staging client:** Deploy to staging slot or separate Static Web App
- **Option B - Use production client with staging API:** Client URL = production, API URL = staging
- **Option C - Subdomain:** Use `staging.henryfaulkner.xyz` (requires DNS configuration)

---

### 4. Code Changes for Production Safety

#### File: `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Program.cs`

**Line 124** - Conditionally register TestEndpoints:

```csharp
// BEFORE:
app.MapTestEndpoints()
   .MapLobbyEndpoints()
   ...

// AFTER:
var endpointBuilder = app.MapLobbyEndpoints()
   .MapGameplayEndpoints()
   .MapPlayerPowerEndpoints()
   .MapShopEndpoints()
   .MapScoringRulesEndpoints()
   .MapUploadEndpoints();

// Only register test endpoints in Development and Staging
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    endpointBuilder.MapTestEndpoints();
    app.Logger.LogWarning("Test endpoints are ENABLED. Environment: {Environment}",
        app.Environment.EnvironmentName);
}
else
{
    app.Logger.LogInformation("Test endpoints are DISABLED in {Environment}",
        app.Environment.EnvironmentName);
}
```

**Why this matters:**
- Production no longer has `/test/cleanup` endpoint
- Tests can only run against staging/development
- Accidental cleanup calls to production return 404

#### File: `src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/EndpointGroups/TestEndpoints.cs`

**Line 55-65** - Add defense-in-depth production check:

```csharp
group.MapPost(Endpoints.Cleanup, async (
    [FromServices] IHostEnvironment env,  // ADD THIS PARAMETER
    [FromServices] IKeyValueRepository<Lobby> lobbyRepository,
    // ... rest of existing parameters
    ) =>
{
    var logger = loggerFactory.CreateLogger(nameof(TestEndpoints));

    // CRITICAL SAFETY CHECK (defense-in-depth)
    if (env.IsProduction())
    {
        logger.LogError("Test cleanup endpoint called in PRODUCTION - BLOCKED");
        return Results.StatusCode(403);
    }

    logger.LogInformation("Test cleanup endpoint called in {Environment} - clearing all repository data",
        env.EnvironmentName);

    // ... rest of existing cleanup logic
});
```

**Double protection:**
1. Endpoint not registered in production (primary)
2. Endpoint blocks production calls if somehow reached (defense-in-depth)

---

### 5. Update Server Pipeline

#### File: `deploy/server-pipeline.yml`

Add staging deployment and testing stages. See full YAML in the implementation plan.

**Key changes:**
1. `DeployStaging` stage deploys to staging with `ASPNETCORE_ENVIRONMENT=Staging`
2. Wait step handles scale-to-zero cold start (~30 seconds)
3. `E2ETestStaging` runs tests against staging (must pass!)
4. `DeployProd` now depends on `E2ETestStaging` (quality gate)

---

### 6. Update Nightly Pipeline

Update `deploy/e2e-nightly-pipeline.yml` to use staging instead of production. Add a wake-up stage to handle scale-to-zero cold start.

---

## Scale-to-Zero Best Practices

### 1. Cold Start Mitigation

**Problem:** First request after scale-to-zero takes 10-30 seconds

**Solutions:**
- Add warmup step in pipeline (shown above)
- Increase timeout for first test
- Use health check endpoint to trigger startup

**Example warmup script:**
```bash
#!/bin/bash
STAGING_URL="https://your-staging-url.azurecontainerapps.io"

echo "Warming up staging environment..."
for i in {1..3}; do
  curl -f -s "$STAGING_URL/health" && break
  echo "Retry $i/3..."
  sleep 10
done
echo "Staging ready!"
```

### 2. Cost Monitoring

**View actual costs:**
```bash
# Get cost analysis for container app
az consumption usage list \
  --resource-group poker-attack-rg \
  --start-date 2025-12-01 \
  --end-date 2025-12-31 \
  --query "[?contains(instanceName, 'staging')]"
```

**Expected costs with scale-to-zero:**
- E2E test runs: 2-3x per day (deployment + nightly)
- Runtime per test: ~10-15 minutes
- Monthly compute time: ~30-60 minutes
- **Estimated cost: $0-5/month** (nearly free!)

### 3. Scale Configuration Optimization

**For infrequent testing (current setup):**
```yaml
Scale Settings:
- Min replicas: 0
- Max replicas: 1
- Scale rule: HTTP (10 concurrent requests)
```

**For frequent testing (future):**
```yaml
Scale Settings:
- Min replicas: 0
- Max replicas: 2
- Scale rule: HTTP (20 concurrent requests)
```

---

## Troubleshooting

### Issue: Tests fail after staging deployment

**Cause:** Cold start not complete

**Solution:**
```yaml
# Add longer wait in pipeline
- script: sleep 45  # Increase from 30 to 45 seconds
  displayName: 'Wait for Cold Start'
```

### Issue: Staging costs more than expected

**Check:**
```bash
# Verify min replicas is 0
az containerapp show \
  --name poker-attack-api-staging \
  --resource-group poker-attack-rg \
  --query properties.template.scale.minReplicas
```

**Fix:**
```bash
# Set min replicas to 0
az containerapp update \
  --name poker-attack-api-staging \
  --resource-group poker-attack-rg \
  --min-replicas 0
```

### Issue: Cleanup endpoint still works in production

**Check environment:**
```bash
# Verify production environment is set correctly
az containerapp show \
  --name poker-attack-api \
  --resource-group poker-attack-rg \
  --query properties.template.containers[0].env
```

**Should see:**
```json
{
  "name": "ASPNETCORE_ENVIRONMENT",
  "value": "Production"
}
```

---

## Migration from Current Setup

### Step 1: Create Staging Environment
1. Create staging container app with scale-to-zero (see Step 1 above)
2. Create `Staging-PokerAttack` variable group (see Step 3 above)
3. Deploy to staging manually to verify

### Step 2: Update Code
1. Update `Program.cs` to conditionally register TestEndpoints
2. Update `TestEndpoints.cs` with production safety check
3. Deploy and verify production no longer has `/test/cleanup`

### Step 3: Update Pipelines
1. Update `server-pipeline.yml` with staging stages
2. Update `e2e-nightly-pipeline.yml` to use staging
3. Test in feature branch first

### Step 4: Validate
1. Run full deployment pipeline
2. Verify staging tests run and pass
3. Verify production deployment waits for staging tests
4. Trigger nightly pipeline manually
5. Monitor first scheduled 2 AM run

### Step 5: Monitor & Optimize
1. Check staging costs after 1 week
2. Adjust cold start timing if needed
3. Tune retry logic based on actual test results

---

## Cost Comparison Summary

| Scenario | Setup | Monthly Cost | Safety Level |
|----------|-------|--------------|--------------|
| **Current (No Staging)** | Simple | $0 | ⚠️ Low (production cleanup risk) |
| **Staging (Always-On)** | Complex | $25-55 | ✅ High (production protected) |
| **Staging (Scale-to-Zero)** | Moderate | $0-10 | ✅ High (production protected) |

**Recommendation:** Staging with scale-to-zero provides the best balance of cost, safety, and simplicity.

---

## When to Implement This Guide

**Implement staging when:**
- ✅ Production has active users
- ✅ Cost of production downtime > cost of staging
- ✅ Need quality gate before production deployment
- ✅ Want to test breaking changes safely

**Delay staging if:**
- ⏸️ Still in early development
- ⏸️ Production traffic is minimal
- ⏸️ Speed of iteration is top priority
- ⏸️ Comfortable with current risk level

**Current recommendation for PokerAttack:** Implement when production traffic grows or when a production incident occurs due to untested deployments.
