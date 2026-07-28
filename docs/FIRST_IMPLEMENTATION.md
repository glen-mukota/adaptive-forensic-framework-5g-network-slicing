# First implementation: reproducible two-slice OAI laboratory

This implementation completes the first approved action-plan stage at development level: it fixes a laboratory profile, pins the upstream OpenAirInterface tutorial source, makes the slice configuration reviewable, and produces preflight and integrity evidence before any experiment is run.

## Authoritative implementation baseline

- OpenAirInterface tutorial source: `oai/openairinterface5G`, branch `develop`, commit `42bf80e9b25dbf521cc692fa6338cbbbebfbcd1d`.
- Official tutorial assets used: `doc/tutorial_resources/oai-cn5g/docker-compose.yaml` and `doc/tutorial_resources/oai-cn5g/conf/config.yaml`.
- OAI services in scope: AMF, SMF, UPF, NRF, MySQL and the external data network.
- Two configured profiles: eMBB (`SST=1`, `DNN=oai`, `10.0.0.0/24`) and mMTC (`SST=3`, `DNN=mmtc`, `10.0.2.0/24`). The common `SD=FFFFFF` is retained from the official baseline; the distinct SST values provide the two-slice separation for the initial proof of concept.

## Visual Studio development asset

Open `AdaptiveForensics.sln` in Visual Studio 2026. The `ForensicFramework.LabBootstrap` project provides three commands:

```powershell
dotnet run --project .\src\ForensicFramework.LabBootstrap -- validate
dotnet run --project .\src\ForensicFramework.LabBootstrap -- manifest
dotnet run --project .\src\ForensicFramework.LabBootstrap -- preflight
```

`validate` checks the two slice definitions and required evidence sources. `manifest` stores both the laboratory-profile SHA-256 value and the SHA-256 value of the applied OAI configuration in `artifacts/lab-manifest.json`. `preflight` stores a machine readiness report in `artifacts/preflight.json`; it must pass before container deployment is attempted.

## Controlled deployment sequence

1. Run `scripts/Enable-OaiPrerequisites.ps1` in an elevated PowerShell session only when the preflight report identifies a missing prerequisite.
2. Use Docker Desktop configured for the WSL2 backend. An Ubuntu user distribution is optional for this Docker-based stage; Docker Desktop provides the required Linux container runtime.
3. Rerun `dotnet run --project .\src\ForensicFramework.LabBootstrap -- preflight` until all checks pass.
4. Run `scripts/Prepare-OaiTwoSliceLab.ps1` once. It validates and applies the version-pinned patch to the official OAI tutorial configuration.
5. Start the official OAI compose project from `third_party/openairinterface5g/doc/tutorial_resources/oai-cn5g` with `docker compose up -d --wait`.
6. Run `scripts/Capture-OaiBaselineEvidence.ps1`. It records the compose state, resolved configuration, Docker network, health summary, core-function logs, NRF registration verification and SHA-256 integrity manifest before adaptive collection or evaluation scenarios are added.

The laboratory is designed to surface, rather than mask, prerequisites and configuration drift. The preparation script is idempotent and normalizes OAI's Linux health-check script when a Windows working tree has introduced CRLF line endings.
