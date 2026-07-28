[CmdletBinding()]
param(
    [string]$Phase2Directory = (Join-Path $PSScriptRoot '..\config\phase2')
)

$ErrorActionPreference = 'Stop'
$phase2Directory = (Resolve-Path $Phase2Directory).Path
$composeFile = Join-Path $phase2Directory 'docker-compose.rfsim.yaml'
$subscriberSeed = Join-Path $phase2Directory 'seed-mmtc-subscriber.sql'
$requiredCore = @('mysql', 'oai-amf', 'oai-smf', 'oai-upf', 'oai-ext-dn')

foreach ($container in $requiredCore) {
    $health = (& docker inspect $container --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}').Trim()
    if ($health -ne 'healthy') { throw "Phase 1 core is not ready: $container reports '$health'." }
}

& docker compose --project-name oai-two-slice-ran -f $composeFile config -q
if ($LASTEXITCODE -ne 0) { throw 'The Phase 2 RF-simulator Compose definition is invalid.' }

Get-Content -LiteralPath $subscriberSeed -Raw | & docker exec -i mysql mysql -utest -ptest oai_db
if ($LASTEXITCODE -ne 0) { throw 'Unable to provision the research-only mMTC virtual subscriber.' }

$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$preparation = [PSCustomObject]@{
    PreparedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    CoreProject = 'oai-two-slice-forensic-lab'
    RanProject = 'oai-two-slice-ran'
    ComposeFile = $composeFile
    SubscriberSeed = $subscriberSeed
    Result = 'ready'
}
$preparation | ConvertTo-Json | Set-Content -Path (Join-Path $workspaceRoot 'artifacts\phase2-preparation.json') -Encoding utf8
Write-Host 'Phase 2 RF-simulator configuration and research-only mMTC subscriber are ready.' -ForegroundColor Green
