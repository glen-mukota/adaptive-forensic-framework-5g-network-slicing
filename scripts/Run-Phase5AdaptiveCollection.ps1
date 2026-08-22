[CmdletBinding()]
param(
    [string]$Phase4Run,
    [string]$ConfigPath = (Join-Path $PSScriptRoot '..\config\phase5\adaptive-collection-rules.json'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\runs')
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configPath = (Resolve-Path $ConfigPath).Path

function Get-LatestPhase4Run {
    param([string]$Root)

    $candidate = Get-ChildItem -LiteralPath $Root -Directory |
        Where-Object { $_.Name -like 'phase4-*' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'slice-attribution-report.json')) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) { throw 'No completed Phase 4 attribution report was found beneath artifacts\runs.' }
    return $candidate.FullName
}

function Save-QueryOutput {
    param(
        [string]$Path,
        [string[]]$Arguments
    )

    $result = & dotnet run --project (Join-Path $workspaceRoot 'src\ForensicFramework.LabBootstrap') -- query-adaptive-collection @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    [System.IO.File]::WriteAllLines($Path, [string[]]$result, (New-Object System.Text.UTF8Encoding($false)))
    if ($exitCode -ne 0) { throw "Adaptive collection query failed while writing $Path." }
}

if ([string]::IsNullOrWhiteSpace($Phase4Run)) {
    $Phase4Run = Get-LatestPhase4Run -Root (Join-Path $workspaceRoot 'artifacts\runs')
}

$Phase4Run = (Resolve-Path $Phase4Run).Path
$phase4Summary = Get-Content -LiteralPath (Join-Path $Phase4Run 'phase4-summary.json') -Raw | ConvertFrom-Json
if ($phase4Summary.Result -ne 'completed') { throw "The selected Phase 4 attribution run is not complete: $Phase4Run" }

$runDirectory = Join-Path $OutputRoot ('phase5-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$reportPath = Join-Path $runDirectory 'adaptive-collection-report.json'
$attributionReportPath = Join-Path $Phase4Run 'slice-attribution-report.json'

& dotnet run --project (Join-Path $workspaceRoot 'src\ForensicFramework.LabBootstrap') -- apply-adaptive-collection --config $configPath --attribution-report $attributionReportPath --output $reportPath
if ($LASTEXITCODE -ne 0) { throw 'Phase 5 adaptive collection, integrity and custody processing failed.' }

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$report.CustodyLedger | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'custody-ledger.json') -Encoding utf8
$report.HashVerificationResults | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'hash-verification-report.json') -Encoding utf8
$report.RuleApplications | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'adaptation-timing-log.json') -Encoding utf8
Save-QueryOutput -Path (Join-Path $runDirectory 'query-approved-rule.txt') -Arguments @('--report', $reportPath, '--rule-id', 'P5-APPROVED-TRAFFIC-PRIORITY')
Save-QueryOutput -Path (Join-Path $runDirectory 'query-first-custody-entry.txt') -Arguments @('--report', $reportPath, '--record-id', $report.CustodyLedger[0].RecordId)

$summary = [PSCustomObject]@{
    CapturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Phase = 'Phase 5 - approved adaptive collection, integrity and chain of custody'
    Result = if ($report.Accepted) { 'completed' } else { 'failed' }
    Phase4InputRun = $Phase4Run
    AttributionReportPath = $attributionReportPath
    AdaptiveRuleCatalogPath = $configPath
    ApprovedRuleApplicationCount = $report.ApprovedRuleApplicationCount
    CustodyEntryCount = $report.CustodyEntryCount
    CompleteCustodyEntryCount = $report.CompleteCustodyEntryCount
    VerifiedHashCount = $report.VerifiedHashCount
    TriggerToRuleUpdateLatencyMilliseconds = $report.TriggerToRuleUpdateLatencyMilliseconds
    EvidenceOutputs = @('adaptive-collection-report.json', 'custody-ledger.json', 'hash-verification-report.json', 'adaptation-timing-log.json')
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runDirectory 'phase5-summary.json') -Encoding utf8
if ($summary.Result -ne 'completed') { throw 'Phase 5 acceptance checks did not pass.' }

Write-Host "Phase 5 adaptive collection, integrity and custody evidence captured: $runDirectory" -ForegroundColor Green
