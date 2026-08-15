[CmdletBinding()]
param(
    [string]$Phase3Run,
    [string]$ConfigPath = (Join-Path $PSScriptRoot '..\config\phase4\slice-attribution-rules.json'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\runs')
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configPath = (Resolve-Path $ConfigPath).Path

function Get-LatestPhase3Run {
    param([string]$Root)

    $candidate = Get-ChildItem -LiteralPath $Root -Directory |
        Where-Object { $_.Name -like 'phase3-*' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'evidence-index.json')) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) { throw 'No completed Phase 3 evidence index was found beneath artifacts\runs.' }
    return $candidate.FullName
}

function Save-QueryOutput {
    param(
        [string]$Path,
        [string[]]$Arguments
    )

    $result = & dotnet run --project (Join-Path $workspaceRoot 'src\ForensicFramework.LabBootstrap') -- query-attribution @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    [System.IO.File]::WriteAllLines($Path, [string[]]$result, (New-Object System.Text.UTF8Encoding($false)))
    if ($exitCode -ne 0) { throw "Attribution query failed while writing $Path." }
}

if ([string]::IsNullOrWhiteSpace($Phase3Run)) {
    $Phase3Run = Get-LatestPhase3Run -Root (Join-Path $workspaceRoot 'artifacts\runs')
}

$Phase3Run = (Resolve-Path $Phase3Run).Path
$phase3Summary = Get-Content -LiteralPath (Join-Path $Phase3Run 'phase3-summary.json') -Raw | ConvertFrom-Json
if ($phase3Summary.Result -ne 'completed') { throw "The selected Phase 3 evidence run is not complete: $Phase3Run" }

$runDirectory = Join-Path $OutputRoot ('phase4-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$reportPath = Join-Path $runDirectory 'slice-attribution-report.json'
$indexPath = Join-Path $Phase3Run 'evidence-index.json'

& dotnet run --project (Join-Path $workspaceRoot 'src\ForensicFramework.LabBootstrap') -- attribute-slices --config $configPath --index $indexPath --output $reportPath
if ($LASTEXITCODE -ne 0) { throw 'Phase 4 slice attribution failed.' }

Save-QueryOutput -Path (Join-Path $runDirectory 'query-embb-attributed.txt') -Arguments @('--report', $reportPath, '--status', 'attributed', '--scenario', 'P2-NORMAL-EMBB-001')
Save-QueryOutput -Path (Join-Path $runDirectory 'query-mmtc-attributed.txt') -Arguments @('--report', $reportPath, '--status', 'attributed', '--scenario', 'P2-NORMAL-MMTC-001')
Save-QueryOutput -Path (Join-Path $runDirectory 'query-shared-ambiguous.txt') -Arguments @('--report', $reportPath, '--status', 'ambiguous', '--rule-id', 'P4-SHARED-MULTI-SCENARIO')

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$summary = [PSCustomObject]@{
    CapturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Phase = 'Phase 4 - slice attribution and transparent rule logic'
    Result = if ($report.Accepted) { 'completed' } else { 'failed' }
    Phase3InputRun = $Phase3Run
    EvidenceIndexPath = $indexPath
    AttributionRulesPath = $configPath
    DecisionCount = $report.DecisionCount
    AttributedCount = $report.AttributedCount
    AmbiguousCount = $report.AmbiguousCount
    UnattributedCount = $report.UnattributedCount
    KnownGroundTruthDecisionCount = $report.KnownGroundTruthDecisionCount
    KnownGroundTruthCorrectCount = $report.KnownGroundTruthCorrectCount
    KnownGroundTruthAccuracyPercent = $report.KnownGroundTruthAccuracyPercent
    QueryChecks = @('query-embb-attributed.txt', 'query-mmtc-attributed.txt', 'query-shared-ambiguous.txt')
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDirectory 'phase4-summary.json') -Encoding utf8
if ($summary.Result -ne 'completed') { throw 'Phase 4 acceptance checks did not pass.' }

Write-Host "Phase 4 slice-attribution evidence captured: $runDirectory" -ForegroundColor Green
