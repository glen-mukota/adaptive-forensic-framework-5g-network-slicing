[CmdletBinding()]
param(
    [string]$UpstreamRoot = (Join-Path $PSScriptRoot '..\third_party\openairinterface5g'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\runs')
)

$ErrorActionPreference = 'Stop'
$composeDirectory = Join-Path $UpstreamRoot 'doc\tutorial_resources\oai-cn5g'
$composeFile = Join-Path $composeDirectory 'docker-compose.yaml'
$projectName = 'oai-two-slice-forensic-lab'
$containers = @('mysql', 'oai-nrf', 'oai-udr', 'oai-udm', 'oai-ausf', 'oai-amf', 'oai-smf', 'oai-upf', 'oai-ext-dn', 'ims')

if (-not (Test-Path -LiteralPath $composeFile)) {
    throw "Official OAI compose file not found: $composeFile"
}

$runDirectory = Join-Path $OutputRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
$logsDirectory = Join-Path $runDirectory 'logs'
New-Item -ItemType Directory -Force -Path $logsDirectory | Out-Null

function Save-DockerOutput {
    param(
        [string[]]$DockerArguments,
        [string]$Path
    )

    $output = & docker @DockerArguments 2>&1
    $exitCode = $LASTEXITCODE
    [System.IO.File]::WriteAllLines($Path, [string[]]$output, (New-Object System.Text.UTF8Encoding($false)))
    if ($exitCode -ne 0) {
        throw "Docker command failed while writing $Path"
    }
}

$composePrefix = @('compose', '--project-name', $projectName, '-f', $composeFile)
Save-DockerOutput -DockerArguments ($composePrefix + @('ps', '--all')) -Path (Join-Path $runDirectory 'compose-status.txt')
Save-DockerOutput -DockerArguments ($composePrefix + @('config')) -Path (Join-Path $runDirectory 'compose-config.yaml')
Save-DockerOutput -DockerArguments @('version') -Path (Join-Path $runDirectory 'docker-version.txt')
Save-DockerOutput -DockerArguments @('network', 'inspect', 'oai-cn5g-public-net') -Path (Join-Path $runDirectory 'network-inspect.json')

$containerEvidence = foreach ($container in $containers) {
    $raw = & docker inspect $container
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect expected container: $container" }
    $inspect = ($raw | ConvertFrom-Json)[0]
    $health = if ($null -ne $inspect.State.Health) { $inspect.State.Health.Status } else { 'none' }
    [PSCustomObject]@{
        Container = $container
        Image = $inspect.Config.Image
        Status = $inspect.State.Status
        Health = $health
        StartedAt = $inspect.State.StartedAt
    }
}

$containerEvidence | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $runDirectory 'container-health.json') -Encoding utf8
$unhealthy = $containerEvidence | Where-Object { $_.Status -ne 'running' -or $_.Health -ne 'healthy' }
if ($unhealthy) {
    $unhealthy | Format-Table -AutoSize | Out-String | Write-Error
    throw 'Baseline evidence was captured, but the OAI laboratory is not healthy.'
}

foreach ($service in @('mysql', 'oai-nrf', 'oai-amf', 'oai-smf', 'oai-upf')) {
    Save-DockerOutput -DockerArguments ($composePrefix + @('logs', '--no-color', $service)) -Path (Join-Path $logsDirectory "$service.log")
}

$expectedNfTypes = @('UDR', 'UDM', 'AUSF', 'AMF', 'SMF', 'UPF')
$nrfLog = Get-Content -LiteralPath (Join-Path $logsDirectory 'oai-nrf.log')
$registeredNfTypes = foreach ($type in $expectedNfTypes) {
    if ($nrfLog | Select-String -Quiet -Pattern "NF type $type \(HTTP version 2\)") { $type }
}
$missingNfTypes = $expectedNfTypes | Where-Object { $_ -notin $registeredNfTypes }
$registrationVerification = @(
    'NRF registration verification'
    "CapturedAt: $((Get-Date).ToUniversalTime().ToString('o'))"
    "Registered NF types: $($registeredNfTypes -join ', ')"
    "Missing NF types: $($missingNfTypes -join ', ')"
)
[System.IO.File]::WriteAllLines(
    (Join-Path $runDirectory 'nrf-registration-verification.txt'),
    [string[]]$registrationVerification,
    (New-Object System.Text.UTF8Encoding($false))
)
if ($missingNfTypes) {
    throw "The NRF log does not evidence these expected registered functions: $($missingNfTypes -join ', ')"
}

$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& dotnet run --project (Join-Path $workspaceRoot 'src\ForensicFramework.LabBootstrap') -- manifest
if ($LASTEXITCODE -ne 0) { throw 'Unable to generate the laboratory integrity manifest.' }
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'artifacts\lab-manifest.json') -Destination (Join-Path $runDirectory 'lab-manifest.json') -Force

$summary = [PSCustomObject]@{
    CapturedAt = (Get-Date).ToUniversalTime().ToString('o')
    Project = $projectName
    Result = 'healthy'
    ExpectedContainers = $containers
    Configuration = 'eMBB: SST 1 / DNN oai / 10.0.0.0/24; mMTC: SST 3 / DNN mmtc / 10.0.2.0/24'
}
$summary | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $runDirectory 'baseline-summary.json') -Encoding utf8

Write-Host "Healthy two-slice baseline evidence captured: $runDirectory" -ForegroundColor Green
