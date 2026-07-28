# Second implementation: virtual gNB/UE and controlled two-slice traffic

This stage completes the controlled-connectivity element of Step 1 and produces the ground-truth ledger required by Step 2 of the approved implementation plan. It remains a contained laboratory activity: no physical radio hardware, live operator network or real subscriber data is used.

## Scope and alignment

- An OAI RF-simulated gNB connects to the existing OAI AMF on the Phase 1 Docker network.
- Two virtual OAI NR-UEs represent the approved two-slice profiles: eMBB (`SST=1`, `DNN=oai`, `10.0.0.2`) and mMTC (`SST=3`, `DNN=mmtc`, `10.0.2.2`).
- The only traffic is labelled UDP `iperf` generated from the external data network to each UE tunnel. It provides known rate, duration, DNN and S-NSSAI inputs for later evidence-ingestion, attribution, integrity and custody stages.
- The RF-simulator images are fixed by SHA-256 digest. The mMTC test subscription is idempotently provisioned from a research-only SQL file.

## Run sequence

```powershell
dotnet build .\AdaptiveForensics.sln --configuration Release
.\scripts\Start-Phase2Rfsim.ps1
.\scripts\Run-Phase2ControlledTraffic.ps1
```

`Start-Phase2Rfsim.ps1` refuses to run unless Phase 1's MySQL, AMF, SMF, UPF and external-data-network containers are healthy. `Run-Phase2ControlledTraffic.ps1` calls the C# scenario-ledger command, verifies UE tunnel interfaces, runs the two defined `iperf` scenarios and stores the gNB, UE, AMF, SMF, UPF, Docker-status and resource outputs in a timestamped evidence directory.

## Acceptance evidence

The phase is accepted only when the gNB and two UE containers are healthy, both UE tunnels have their expected slice-specific addresses, the two traffic commands complete, and the saved logs show the associated radio/core events. The resulting records are ground truth for the next implementation stage; they are not yet a claim that forensic ingestion, attribution, hashing or chain of custody has been implemented.
