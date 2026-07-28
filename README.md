# Adaptive Forensic Framework for 5G Network Slicing

COS700 research prototype for a contained OpenAirInterface (OAI) laboratory. The project implements an evidence-led progression toward an adaptive forensic framework for 5G network slicing.

## Implemented development phases

1. **Phase 1 - Reproducible two-slice OAI core:** OAI AMF, SMF, UPF and support services, configured for eMBB (`SST 1`, `DNN oai`) and mMTC (`SST 3`, `DNN mmtc`).
2. **Phase 2 - Virtual gNB/UE and controlled traffic:** RF-simulated gNB, two virtual NR-UEs, slice-specific tunnel addresses and reproducible UDP `iperf` ground-truth traffic.
3. **Phase 3 - Normalised evidence ingestion:** C#/.NET registration of selected Phase 1/2 artifacts into a structured, queryable evidence index without changing original artifacts.

## Prerequisites

- Visual Studio 2026 or the .NET 10 SDK
- Docker Desktop with WSL2 integration enabled
- Git
- Pinned OAI tutorial source in `third_party/openairinterface5g` (not committed because it is a vendored upstream dependency)

## Reproduce the implemented phases

Run from the workspace root in Windows PowerShell:

```powershell
dotnet build .\AdaptiveForensics.sln --configuration Release
.\scripts\Prepare-OaiTwoSliceLab.ps1
docker compose --project-name oai-two-slice-forensic-lab -f .\third_party\openairinterface5g\doc\tutorial_resources\oai-cn5g\docker-compose.yaml up -d --wait
.\scripts\Capture-OaiBaselineEvidence.ps1
.\scripts\Start-Phase2Rfsim.ps1
.\scripts\Run-Phase2ControlledTraffic.ps1
.\scripts\Run-Phase3EvidenceIngestion.ps1
```

Phase 3 outputs are stored in `artifacts/runs/phase3-<timestamp>/`. The `artifacts/`, `third_party/`, build output and local temporary files are intentionally excluded from Git.

## Research boundaries

This is a research-only lab proof of concept. It uses no physical radio hardware, live operator network, real subscriber data, or production deployment. The repository does not yet claim completed slice-attribution logic, evidence hashing / chain of custody, adaptive control or formal evaluation; those are future stages in the approved implementation plan.
