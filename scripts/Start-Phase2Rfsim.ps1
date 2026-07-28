[CmdletBinding()]
param(
    [int]$WaitTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$composeFile = Join-Path $workspaceRoot 'config\phase2\docker-compose.rfsim.yaml'

& (Join-Path $PSScriptRoot 'Prepare-Phase2Rfsim.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& docker compose --project-name oai-two-slice-ran -f $composeFile up -d --wait --wait-timeout $WaitTimeoutSeconds
if ($LASTEXITCODE -ne 0) { throw 'The Phase 2 virtual gNB/UE deployment did not reach a healthy state.' }

Write-Host 'Phase 2 virtual gNB and both slice-labelled NR-UEs are running.' -ForegroundColor Green
