[CmdletBinding()]
param(
    [string]$UpstreamRoot = (Join-Path $PSScriptRoot '..\third_party\openairinterface5g')
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$patch = Join-Path $workspaceRoot 'config\oai-two-slice-config.patch'
$composeDirectory = Join-Path $UpstreamRoot 'doc\tutorial_resources\oai-cn5g'

if (-not (Test-Path -LiteralPath $patch)) { throw "Two-slice patch not found: $patch" }
if (-not (Test-Path -LiteralPath (Join-Path $composeDirectory 'docker-compose.yaml'))) {
    throw "Official OAI tutorial source is missing. Expected: $composeDirectory"
}

Push-Location $UpstreamRoot
try {
    & git apply --check $patch 2>$null
    if ($LASTEXITCODE -eq 0) {
        & git apply $patch
        if ($LASTEXITCODE -ne 0) { throw 'Unable to apply the two-slice configuration patch.' }
        $configurationState = 'applied'
    }
    else {
        & git apply --reverse --check $patch 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'The pinned OAI configuration patch neither applies nor matches the current source tree.' }
        $configurationState = 'already applied'
    }
}
finally {
    Pop-Location
}

$mysqlHealthcheck = Join-Path $composeDirectory 'healthscripts\mysql-healthcheck.sh'
$healthcheckContents = [System.IO.File]::ReadAllText($mysqlHealthcheck)
$normalizedContents = $healthcheckContents -replace "`r`n", "`n"
[System.IO.File]::WriteAllText(
    $mysqlHealthcheck,
    $normalizedContents,
    (New-Object System.Text.UTF8Encoding($false))
)

Write-Host "Two-slice OAI configuration $configurationState at $composeDirectory" -ForegroundColor Green
Write-Host 'Normalized the Linux MySQL health-check script for a Windows-hosted Docker bind mount.'
Write-Host 'Run Docker Compose after the preflight report passes.'
