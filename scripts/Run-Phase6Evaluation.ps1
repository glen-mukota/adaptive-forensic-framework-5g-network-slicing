[CmdletBinding()]
param(
    [string]$Phase1Run,
    [string]$Phase2Run,
    [string]$Phase3Run,
    [string]$Phase4Run,
    [string]$Phase5Run,
    [string]$ConfigPath = (Join-Path $PSScriptRoot '..\config\phase6\evaluation-scenarios.json'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\runs')
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configPath = (Resolve-Path $ConfigPath).Path

function Get-LatestCompletedRun {
    param(
        [string]$Root,
        [string]$NamePattern,
        [string]$SummaryFile
    )

    $candidate = Get-ChildItem -LiteralPath $Root -Directory |
        Where-Object { $_.Name -like $NamePattern -and (Test-Path -LiteralPath (Join-Path $_.FullName $SummaryFile)) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) { throw "No run containing $SummaryFile was found beneath $Root." }
    return $candidate.FullName
}

function Assert-CompletedSummary {
    param(
        [string]$RunDirectory,
        [string]$SummaryFile,
        [string]$ExpectedResult
    )

    $summaryPath = Join-Path $RunDirectory $SummaryFile
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    if ($summary.Result -ne $ExpectedResult) {
        throw "The required input run is not ready: $summaryPath reports '$($summary.Result)', expected '$ExpectedResult'."
    }
    return $summary
}

function Save-QueryOutput {
    param(
        [string]$Path,
        [string[]]$Arguments
    )

    $result = & dotnet run --project (Join-Path $workspaceRoot 'src\ForensicFramework.LabBootstrap') -- query-evaluation @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    [System.IO.File]::WriteAllLines($Path, [string[]]$result, (New-Object System.Text.UTF8Encoding($false)))
    if ($exitCode -ne 0) { throw "Evaluation query failed while writing $Path." }
}

if ([string]::IsNullOrWhiteSpace($Phase1Run)) {
    $Phase1Run = Get-LatestCompletedRun -Root $OutputRoot -NamePattern '20*' -SummaryFile 'baseline-summary.json'
}
if ([string]::IsNullOrWhiteSpace($Phase2Run)) {
    $Phase2Run = Get-LatestCompletedRun -Root $OutputRoot -NamePattern 'phase2-*' -SummaryFile 'phase2-summary.json'
}
if ([string]::IsNullOrWhiteSpace($Phase3Run)) {
    $Phase3Run = Get-LatestCompletedRun -Root $OutputRoot -NamePattern 'phase3-*' -SummaryFile 'phase3-summary.json'
}
if ([string]::IsNullOrWhiteSpace($Phase4Run)) {
    $Phase4Run = Get-LatestCompletedRun -Root $OutputRoot -NamePattern 'phase4-*' -SummaryFile 'phase4-summary.json'
}
if ([string]::IsNullOrWhiteSpace($Phase5Run)) {
    $Phase5Run = Get-LatestCompletedRun -Root $OutputRoot -NamePattern 'phase5-*' -SummaryFile 'phase5-summary.json'
}

$Phase1Run = (Resolve-Path $Phase1Run).Path
$Phase2Run = (Resolve-Path $Phase2Run).Path
$Phase3Run = (Resolve-Path $Phase3Run).Path
$Phase4Run = (Resolve-Path $Phase4Run).Path
$Phase5Run = (Resolve-Path $Phase5Run).Path

$null = Assert-CompletedSummary -RunDirectory $Phase1Run -SummaryFile 'baseline-summary.json' -ExpectedResult 'healthy'
$null = Assert-CompletedSummary -RunDirectory $Phase2Run -SummaryFile 'phase2-summary.json' -ExpectedResult 'completed'
$null = Assert-CompletedSummary -RunDirectory $Phase3Run -SummaryFile 'phase3-summary.json' -ExpectedResult 'completed'
$null = Assert-CompletedSummary -RunDirectory $Phase4Run -SummaryFile 'phase4-summary.json' -ExpectedResult 'completed'
$null = Assert-CompletedSummary -RunDirectory $Phase5Run -SummaryFile 'phase5-summary.json' -ExpectedResult 'completed'

$runDirectory = Join-Path $OutputRoot ('phase6-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$reportPath = Join-Path $runDirectory 'evaluation-report.json'

& dotnet run --project (Join-Path $workspaceRoot 'src\ForensicFramework.LabBootstrap') -- evaluate-prototype `
    --config $configPath `
    --phase1-run $Phase1Run `
    --phase2-run $Phase2Run `
    --phase3-run $Phase3Run `
    --phase4-run $Phase4Run `
    --phase5-run $Phase5Run `
    --output $reportPath
if ($LASTEXITCODE -ne 0) { throw 'Phase 6 controlled evaluation failed.' }

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$report.InputChecks | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'input-checks.json') -Encoding utf8
$report.Scenarios | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'scenario-results.json') -Encoding utf8
$report.Metrics | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'metrics.json') -Encoding utf8
$report.OperationalOverhead | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'operational-overhead.json') -Encoding utf8
$report.Limitations | Set-Content -LiteralPath (Join-Path $runDirectory 'limitations.txt') -Encoding utf8
Save-QueryOutput -Path (Join-Path $runDirectory 'query-normal-scenario.txt') -Arguments @('--report', $reportPath, '--scenario-id', 'P6-NORMAL-SLICE-OPERATION')
Save-QueryOutput -Path (Join-Path $runDirectory 'query-completeness-metric.txt') -Arguments @('--report', $reportPath, '--metric', 'Collection completeness')

$summary = [PSCustomObject]@{
    CapturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Phase = 'Phase 6 - controlled evaluation and refinement'
    Result = if ($report.Accepted) { 'completed' } else { 'failed' }
    Phase1InputRun = $Phase1Run
    Phase2InputRun = $Phase2Run
    Phase3InputRun = $Phase3Run
    Phase4InputRun = $Phase4Run
    Phase5InputRun = $Phase5Run
    ScenarioCount = @($report.Scenarios).Count
    PassedScenarioCount = @($report.Scenarios | Where-Object Passed).Count
    MetricCount = @($report.Metrics).Count
    PassedMetricCount = @($report.Metrics | Where-Object Passed).Count
    EvidenceOutputs = @('evaluation-report.json', 'input-checks.json', 'scenario-results.json', 'metrics.json', 'operational-overhead.json', 'limitations.txt')
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'phase6-summary.json') -Encoding utf8
if ($summary.Result -ne 'completed') { throw 'Phase 6 evaluation acceptance checks did not pass.' }

Write-Host "Phase 6 evaluation evidence captured: $runDirectory" -ForegroundColor Green
