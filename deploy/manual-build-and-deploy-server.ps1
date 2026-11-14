# Legacy entry point retained for build-only preparation. No migrations or ACA updates.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,127}$')]
    [string]$ContainerTag
)

$ErrorActionPreference = 'Stop'
if ($ContainerTag -eq 'latest') { throw 'Use an immutable release tag instead of latest.' }

$env:CONTAINER_REGISTRY = 'chefknife.azurecr.io'
$env:CONTAINER_TAG = $ContainerTag
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    docker compose build
    if ($LASTEXITCODE -ne 0) { throw 'Docker build failed.' }
    Write-Host 'Built locally. Publishing awaits WP05 credential checks; deployment awaits pilot acceptance.'
}
finally {
    Pop-Location
}
