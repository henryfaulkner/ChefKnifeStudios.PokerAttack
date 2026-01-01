# Complete Build and Deploy Script for PokerAttack Server
# This script builds containers, pushes to ACR, runs migrations, and deploys to Azure Container Apps

# Configuration
$CONTAINER_REGISTRY = "chefknife.azurecr.io"
$CONTAINER_TAG = Read-Host -Prompt "Enter container tag (e.g., manual-v1.0.0, or press Enter for 'latest')"

if ([string]::IsNullOrWhiteSpace($CONTAINER_TAG)) {
    $CONTAINER_TAG = "latest"
}

$API_CONTAINER_NAME = "chefknifestudios.pokerattack.server.webapi"
$MIGRATION_CONTAINER_NAME = "chefknifestudios.pokerattack.server.data"
$API_CONTAINER = "${CONTAINER_REGISTRY}/${API_CONTAINER_NAME}:${CONTAINER_TAG}"
$MIGRATION_CONTAINER = "${CONTAINER_REGISTRY}/${MIGRATION_CONTAINER_NAME}:${CONTAINER_TAG}"

# Azure Configuration
$RESOURCE_GROUP = "poker-attack-rg"
$CONTAINER_APP_NAME = "poker-attack-api"
$CONTAINER_APP_ENV = "poker-attack-api-env"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Complete Build and Deploy Pipeline" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Registry: $CONTAINER_REGISTRY" -ForegroundColor Yellow
Write-Host "Tag: $CONTAINER_TAG" -ForegroundColor Yellow
Write-Host "API Container: $API_CONTAINER" -ForegroundColor Yellow
Write-Host "Migration Container: $MIGRATION_CONTAINER" -ForegroundColor Yellow
Write-Host "Resource Group: $RESOURCE_GROUP" -ForegroundColor Yellow
Write-Host "Container App: $CONTAINER_APP_NAME" -ForegroundColor Yellow
Write-Host "========================================`n" -ForegroundColor Cyan

# ============================================
# STAGE 1: BUILD AND PUSH CONTAINERS
# ============================================

# Step 1: Check Azure login
Write-Host "[1/5] Verifying Azure CLI login..." -ForegroundColor Green
$azAccount = az account show 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Not logged into Azure CLI" -ForegroundColor Red
    Write-Host "Please run: az login" -ForegroundColor Yellow
    exit 1
}
Write-Host "Logged in successfully" -ForegroundColor Green

# Step 2: Login to Azure Container Registry
Write-Host "`n[2/5] Logging into Azure Container Registry..." -ForegroundColor Green
az acr login --name chefknife

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to login to Azure Container Registry" -ForegroundColor Red
    Write-Host "Make sure Docker Desktop is running" -ForegroundColor Yellow
    exit 1
}

# Step 3: Build containers using docker compose
Write-Host "`n[3/5] Building containers with docker compose..." -ForegroundColor Green
$env:CONTAINER_REGISTRY = $CONTAINER_REGISTRY
$env:CONTAINER_TAG = $CONTAINER_TAG

docker compose build

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Docker compose build failed" -ForegroundColor Red
    exit 1
}

# Step 4: Push containers to registry
Write-Host "`n[4/5] Pushing containers to registry..." -ForegroundColor Green

Write-Host "Pushing API container..." -ForegroundColor Yellow
docker push $API_CONTAINER

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to push API container" -ForegroundColor Red
    exit 1
}

Write-Host "Pushing migration container..." -ForegroundColor Yellow
docker push $MIGRATION_CONTAINER

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to push migration container" -ForegroundColor Red
    exit 1
}

Write-Host "`nContainers built and pushed successfully!" -ForegroundColor Green

# ============================================
# STAGE 2: RUN MIGRATIONS AND DEPLOY
# ============================================

# Step 5a: Run DB migrations
Write-Host "`n[5/5] Running database migrations and deploying..." -ForegroundColor Green

Write-Host "`nPlease enter your database connection string:" -ForegroundColor Yellow
Write-Host "(Format: Server=...;Database=...;User Id=...;Password=...)" -ForegroundColor Gray
$DB_CONNECTION_STRING = Read-Host -Prompt "Connection String" -AsSecureString
$DB_CONNECTION_STRING_PLAIN = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($DB_CONNECTION_STRING)
)

Write-Host "`nPulling migration container..." -ForegroundColor Yellow
docker pull $MIGRATION_CONTAINER

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to pull migration container" -ForegroundColor Red
    exit 1
}

Write-Host "Running migrations..." -ForegroundColor Yellow
docker run --rm -e CONNECTIONSTRINGS__POKERATTACKDB="$DB_CONNECTION_STRING_PLAIN" $MIGRATION_CONTAINER

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Database migration failed" -ForegroundColor Red
    exit 1
}

Write-Host "Migrations completed successfully" -ForegroundColor Green

# Step 5b: Deploy API Container App
Write-Host "`nDeploying API Container App to Azure..." -ForegroundColor Yellow

az containerapp update `
    --name $CONTAINER_APP_NAME `
    --resource-group $RESOURCE_GROUP `
    --image $API_CONTAINER

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to deploy container app" -ForegroundColor Red
    exit 1
}

# ============================================
# SUCCESS
# ============================================

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "SUCCESS! Complete Deployment Pipeline" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "✓ Built containers" -ForegroundColor Green
Write-Host "✓ Pushed to ACR" -ForegroundColor Green
Write-Host "✓ Ran database migrations" -ForegroundColor Green
Write-Host "✓ Deployed to Azure Container Apps" -ForegroundColor Green
Write-Host "`nDeployed Image: $API_CONTAINER" -ForegroundColor Yellow
Write-Host "Container App: $CONTAINER_APP_NAME" -ForegroundColor Yellow
Write-Host "`nYour application is now running in Azure!" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Green
