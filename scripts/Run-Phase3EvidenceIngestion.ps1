[CmdletBinding()]
param(
    [string]$Phase1Run,
    [string]$Phase2Run,
    [string]$ConfigPath = (Join-Path $PSScriptRoot '..\config\phase3\evidence-ingestion.json'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\runs')
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configPath = (Resolve-Path $ConfigPath).Path

function Get-LatestEvidenceRun {
    param(
        [string]$Root,
        [string]$RequiredFile,
        [string]$NamePattern = '*'
    )

    $candidate = Get-ChildItem -LiteralPath $Root -Directory |
        Where-Object { $_.Name -like $NamePattern -and (Test-Path -LiteralPath (Join-Path $_.FullName $RequiredFile)) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) { throw "No evidence run containing $RequiredFile was found beneath $Root." }
    return $candidate.FullName
}

function Save-QueryOutput {
    param(
        [string]$Path,
        [string[]]$Arguments
    )

    $result = & dotnet run --project (Join-Path $workspaceRoot 'src\ForensicFramework.LabBootstrap') -- query-evidence @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    [System.IO.File]::WriteAllLines($Path, [string[]]$result, (New-Object System.Text.UTF8Encoding($false)))
    if ($exitCode -ne 0) { throw "Evidence query failed while writing $Path." }
}

if ([string]::IsNullOrWhiteSpace($Phase1Run)) {
    $Phase1Run = Get-LatestEvidenceRun -Root (Join-Path $workspaceRoot 'artifacts\runs') -RequiredFile 'baseline-summary.json'
}
if ([string]::IsNullOrWhiteSpace($Phase2Run)) {
    $Phase2Run = Get-LatestEvidenceRun -Root (Join-Path $workspaceRoot 'artifacts\runs') -RequiredFile 'phase2-summary.json' -NamePattern 'phase2-*'
}

$Phase1Run = (Resolve-Path $Phase1Run).Path
$Phase2Run = (Resolve-Path $Phase2Run).Path
$phase2Summary = Get-Content -LiteralPath (Join-Path $Phase2Run 'phase2-summary.json') -Raw | ConvertFrom-Json
if ($phase2Summary.Result -ne 'completed') { throw "The selected Phase 2 evidence run is not complete: $Phase2Run" }

$runDirectory = Join-Path $OutputRoot ('phase3-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$indexPath = Join-Path $runDirectory 'evidence-index.json'

& dotnet run --project (Join-Path $workspaceRoot 'src\ForensicFramework.LabBootstrap') -- ingest-evidence --config $configPath --phase1-run $Phase1Run --phase2-run $Phase2Run --output $indexPath
if ($LASTEXITCODE -ne 0) { throw 'Phase 3 evidence ingestion failed.' }

Save-QueryOutput -Path (Join-Path $runDirectory 'query-oai-smf.txt') -Arguments @('--index', $indexPath, '--network-function', 'oai-smf')
Save-QueryOutput -Path (Join-Path $runDirectory 'query-mmtc-scenario.txt') -Arguments @('--index', $indexPath, '--scenario', 'P2-NORMAL-MMTC-001')
Save-QueryOutput -Path (Join-Path $runDirectory 'query-user-plane-traffic.txt') -Arguments @('--index', $indexPath, '--source-kind', 'user-plane-traffic')

$index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
$unchanged = @($index.Records | Where-Object { -not $_.OriginalArtifactUnchanged }).Count -eq 0
$summary = [PSCustomObject]@{
    CapturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Phase = 'Phase 3 - normalised evidence ingestion'
    Result = if ($unchanged -and $index.RecordCount -ge 27) { 'completed' } else { 'failed' }
    Phase1InputRun = $Phase1Run
    Phase2InputRun = $Phase2Run
    SourceDefinitionCount = $index.SourceDefinitionCount
    RecordCount = $index.RecordCount
    OriginalArtifactsUnchanged = $unchanged
    RetrievalChecks = @(
        'query-oai-smf.txt',
        'query-mmtc-scenario.txt',
        'query-user-plane-traffic.txt'
    )
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDirectory 'phase3-summary.json') -Encoding utf8
if ($summary.Result -ne 'completed') { throw 'Phase 3 acceptance checks did not pass.' }

Write-Host "Phase 3 normalised evidence ingestion completed: $runDirectory" -ForegroundColor Green
