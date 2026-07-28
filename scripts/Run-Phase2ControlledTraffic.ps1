[CmdletBinding()]
param(
    [string]$ScenarioFile = (Join-Path $PSScriptRoot '..\config\phase2\scenarios.json'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\runs')
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$scenarioFile = (Resolve-Path $ScenarioFile).Path
$runDirectory = Join-Path $OutputRoot ("phase2-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $runDirectory 'logs') | Out-Null

function Save-CommandOutput {
    param([string]$Path, [scriptblock]$Command)
    $output = & $Command 2>&1
    $exitCode = $LASTEXITCODE
    [System.IO.File]::WriteAllLines($Path, [string[]]$output, (New-Object System.Text.UTF8Encoding($false)))
    if ($exitCode -ne 0) { throw "Command failed while creating $Path" }
}

& dotnet run --project (Join-Path $workspaceRoot 'src\ForensicFramework.LabBootstrap') -- scenario-ledger --config $scenarioFile --output (Join-Path $runDirectory 'scenario-ledger.json')
if ($LASTEXITCODE -ne 0) { throw 'The C# scenario ledger could not be generated.' }

$scenarioConfig = Get-Content -LiteralPath $scenarioFile -Raw | ConvertFrom-Json
$composeFile = Join-Path $workspaceRoot 'config\phase2\docker-compose.rfsim.yaml'
Save-CommandOutput -Path (Join-Path $runDirectory 'phase2-compose-status.txt') -Command { docker compose --project-name oai-two-slice-ran -f $composeFile ps --all }
Save-CommandOutput -Path (Join-Path $runDirectory 'docker-stats-before.txt') -Command { docker stats --no-stream }

foreach ($scenario in $scenarioConfig.scenarios) {
    $health = (& docker inspect $scenario.virtualUe --format '{{.State.Health.Status}}').Trim()
    if ($health -ne 'healthy') { throw "Virtual UE $($scenario.virtualUe) is not healthy." }

    $tunnelCheck = Join-Path $runDirectory "$($scenario.id)-tunnel.txt"
    Save-CommandOutput -Path $tunnelCheck -Command { docker exec $scenario.virtualUe sh -lc "ip -4 addr show dev oaitun_ue1; ip route" }
    $serverCommand = "iperf -s -u -B $($scenario.tunnelAddress) -p 5001 > /tmp/$($scenario.id)-iperf-server.log 2>&1 & echo " + '$!'
    $serverPid = (& docker exec $scenario.virtualUe sh -lc $serverCommand).Trim()
    if ([string]::IsNullOrWhiteSpace($serverPid)) { throw "Unable to start the iperf server for $($scenario.id)." }

    try {
        Save-CommandOutput -Path (Join-Path $runDirectory "$($scenario.id)-iperf-client.txt") -Command {
            docker exec oai-ext-dn iperf -c $scenario.tunnelAddress -u -b "$($scenario.targetRateKbps)K" -t $scenario.durationSeconds -i 1 -p 5001
        }
        Save-CommandOutput -Path (Join-Path $runDirectory "$($scenario.id)-iperf-server.txt") -Command {
            docker exec $scenario.virtualUe sh -lc "cat /tmp/$($scenario.id)-iperf-server.log"
        }
    }
    finally {
        & docker exec $scenario.virtualUe sh -lc "kill $serverPid" 2>$null
    }
}

Save-CommandOutput -Path (Join-Path $runDirectory 'docker-stats-after.txt') -Command { docker stats --no-stream }
foreach ($container in @('phase2-oai-gnb', 'phase2-oai-nr-ue-embb', 'phase2-oai-nr-ue-mmtc', 'oai-amf', 'oai-smf', 'oai-upf')) {
    Save-CommandOutput -Path (Join-Path $runDirectory "logs\$container.log") -Command { docker logs $container }
}

$summary = [PSCustomObject]@{
    CapturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Phase = $scenarioConfig.phase
    Result = 'completed'
    Scenarios = $scenarioConfig.scenarios | ForEach-Object { $_.id }
    RanContainers = @('phase2-oai-gnb', 'phase2-oai-nr-ue-embb', 'phase2-oai-nr-ue-mmtc')
}
$summary | ConvertTo-Json | Set-Content -Path (Join-Path $runDirectory 'phase2-summary.json') -Encoding utf8
Write-Host "Phase 2 controlled-traffic evidence captured: $runDirectory" -ForegroundColor Green
