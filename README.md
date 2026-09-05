# Adaptive Forensic Framework for 5G Network Slicing

This repository contains a research proof of concept for collecting, organising, attributing, protecting and evaluating forensic evidence in a small emulated 5G network. It is designed for a Windows computer running Docker Desktop. It does not use physical radio equipment, a live operator network or real subscriber data.

An external examiner can reproduce the prototype by following the steps below in order. The prototype starts a two-slice OpenAirInterface laboratory, generates controlled traffic, registers the retained evidence, explains each slice decision, records integrity and custody information, and evaluates the completed workflow against predefined scenarios.

## What the prototype demonstrates

| Phase | What it does | Evidence produced |
|---|---|---|
| 1 | Starts a stable 5G core with eMBB and mMTC slice profiles. | Service health, configuration manifest and registration checks. |
| 2 | Starts a virtual base station and two virtual devices, then generates labelled traffic. | Ground-truth ledger, traffic results and resource snapshots. |
| 3 | Registers selected Phase 1 and 2 files as structured evidence without editing the source files. | Queryable evidence index. |
| 4 | Assigns evidence to a slice only when an explicit rule supports the assignment. | Attribution report with rule and rationale for every record. |
| 5 | Applies an approved collection-priority rule after labelled traffic, calculates SHA-256 values and keeps a custody ledger. | Rule audit, hash-verification report, custody ledger and timing log. |
| 6 | Evaluates five controlled scenarios and reports completeness, integrity, custody, adaptation time and observed laboratory overhead. | Evaluation report, scenario results, metric results, overhead observation and limitations register. |

## Research boundary

This is a contained laboratory proof of concept. The "allocation anomaly" in Phase 6 is a data-only replay used to test that the attribution logic refuses an unknown slice context. It does not transmit rogue radio signals, change a real subscriber record, change a slice allocation or interact with an external network.

The overhead figures describe total Docker activity in this small laboratory during the controlled traffic run. They do not claim to isolate a production cost for the forensic program.

## Requirements

Use a Windows 11 computer with:

- Docker Desktop 4.x or later, using the WSL 2 backend.
- WSL 2. Docker Desktop normally creates the `docker-desktop` distribution automatically.
- .NET 10 SDK. Visual Studio 2026 Community is optional for running the prototype, but useful for opening and inspecting the C# project.
- Git for Windows.
- At least 16 GB RAM is recommended because the emulated 5G core, virtual base station and two virtual devices run as containers.
- Internet access the first time to clone the OpenAirInterface source and pull the container images.

### First-time Windows setup

1. Install **Docker Desktop** from Docker's official website. During installation select **Use WSL 2 instead of Hyper-V** when Docker Desktop asks.
2. Start Docker Desktop and wait until the lower-left status says **Engine running**.
3. Open Windows PowerShell and check the prerequisites:

```powershell
wsl.exe --status
docker version
dotnet --version
git --version
```

4. If `wsl.exe --status` reports that WSL or virtualisation is missing, open **PowerShell as Administrator** in this repository and run the supplied prerequisite helper once:

```powershell
.\scripts\Enable-OaiPrerequisites.ps1
```

Restart Windows when the helper tells you to do so, then install Docker Desktop and repeat the checks above. Do not run the helper when WSL 2 and Docker Desktop already work.

## Download the repository and the required OAI source

Run the following from a folder where you keep research projects. The OpenAirInterface source is intentionally not stored in this repository because it is an external upstream dependency. The commands place it in the exact folder expected by the scripts and pin it to the tested revision.

```powershell
git clone https://github.com/glen-mukota/adaptive-forensic-framework-5g-network-slicing.git
Set-Location .\adaptive-forensic-framework-5g-network-slicing
New-Item -ItemType Directory -Force .\third_party | Out-Null
git clone https://gitlab.eurecom.fr/oai/openairinterface5g.git .\third_party\openairinterface5g
git -C .\third_party\openairinterface5g checkout 42bf80e9b25dbf521cc692fa6338cbbbebfbcd1d
```

Open `AdaptiveForensics.sln` in Visual Studio if you want to inspect the C# code. The runnable project is `ForensicFramework.LabBootstrap`.

## Start the full prototype from a clean computer

Open a new PowerShell window in the repository root. Every command below must finish successfully before you continue to the next one.

### 1. Check the local environment and build the C# prototype

```powershell
dotnet build .\AdaptiveForensics.sln --configuration Release
dotnet run --project .\src\ForensicFramework.LabBootstrap -- preflight --output .\artifacts\preflight.json
```

Expected result: `Build succeeded` with zero warnings and errors, followed by `PASS` for Visual Studio or .NET, WSL 2, Docker Engine, the OAI tutorial source and the two-slice patch.

### 2. Prepare and start the two-slice 5G core

```powershell
.\scripts\Prepare-OaiTwoSliceLab.ps1
docker compose --project-name oai-two-slice-forensic-lab -f .\third_party\openairinterface5g\doc\tutorial_resources\oai-cn5g\docker-compose.yaml up -d --wait --wait-timeout 300
.\scripts\Capture-OaiBaselineEvidence.ps1
```

Purpose: these commands apply the supplied two-slice configuration to the pinned OAI source, start the eMBB and mMTC core environment, and capture a Phase 1 baseline. Docker Desktop should show healthy `mysql`, `oai-amf`, `oai-smf`, `oai-upf`, `oai-nrf`, `oai-udm`, `oai-udr`, `oai-ausf`, `oai-ext-dn` and `ims` containers.

### 3. Start the virtual base station and virtual devices

```powershell
.\scripts\Start-Phase2Rfsim.ps1 -WaitTimeoutSeconds 300
docker compose --project-name oai-two-slice-ran -f .\config\phase2\docker-compose.rfsim.yaml ps
```

Purpose: the first command seeds the research-only mMTC subscriber and starts the RF-simulated gNB, the eMBB virtual device and the mMTC virtual device. The `ps` command confirms that the three Phase 2 containers are healthy.

### 4. Generate labelled traffic and run the evidence pipeline

```powershell
.\scripts\Run-Phase2ControlledTraffic.ps1
.\scripts\Run-Phase3EvidenceIngestion.ps1
.\scripts\Run-Phase4SliceAttribution.ps1
.\scripts\Run-Phase5AdaptiveCollection.ps1
.\scripts\Run-Phase6Evaluation.ps1
```

Purpose:

- Phase 2 sends controlled UDP traffic to the two virtual devices and records what should exist.
- Phase 3 creates a structured evidence index while preserving the original files.
- Phase 4 makes a documented slice decision for each record. Shared evidence stays ambiguous instead of being forced into a slice.
- Phase 5 applies the approved collection-priority rule to attributed traffic, calculates SHA-256 values, records custody fields and verifies the hashes again.
- Phase 6 evaluates the retained run against the five predefined scenarios.

## Check the result

The scripts create time-stamped folders under `artifacts\runs`. The newest Phase 6 folder contains the final evaluation evidence. Use these commands to find and read it:

```powershell
$phase6 = Get-ChildItem .\artifacts\runs -Directory -Filter 'phase6-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Get-Content (Join-Path $phase6.FullName 'phase6-summary.json')
Get-Content (Join-Path $phase6.FullName 'metrics.json')
Get-Content (Join-Path $phase6.FullName 'scenario-results.json')
```

Expected Phase 6 result for the supplied configuration:

- `Result` is `completed`.
- Five scenarios and six measures pass.
- Attribution accuracy, normal-operation collection completeness, integrity verification and custody completeness are all calculated from the retained files.
- The evaluation report includes the recorded adaptation time, the observed Docker resource change and explicit limitations.

To inspect a specific result through the C# program:

```powershell
dotnet run --project .\src\ForensicFramework.LabBootstrap -- query-evaluation --report (Join-Path $phase6.FullName 'evaluation-report.json') --scenario-id P6-NORMAL-SLICE-OPERATION
dotnet run --project .\src\ForensicFramework.LabBootstrap -- query-evaluation --report (Join-Path $phase6.FullName 'evaluation-report.json') --metric 'Collection completeness'
```

## Important output folders

| Folder | Main files | Why they matter |
|---|---|---|
| `artifacts\runs\<timestamp>` | `baseline-summary.json`, `container-health.json` | Phase 1 core health and configuration evidence. |
| `artifacts\runs\phase2-<timestamp>` | `scenario-ledger.json`, `*-iperf-client.txt`, `docker-stats-*.txt` | Labelled traffic ground truth and before/after resource snapshots. |
| `artifacts\runs\phase3-<timestamp>` | `evidence-index.json` | Structured, queryable records with original file references. |
| `artifacts\runs\phase4-<timestamp>` | `slice-attribution-report.json` | Slice decision, rule identifier and reason for every record. |
| `artifacts\runs\phase5-<timestamp>` | `custody-ledger.json`, `hash-verification-report.json`, `adaptation-timing-log.json` | Integrity, custody and approved-rule evidence. |
| `artifacts\runs\phase6-<timestamp>` | `evaluation-report.json`, `metrics.json`, `scenario-results.json`, `operational-overhead.json`, `limitations.txt` | Reproducible Phase 6 results and limitations. |

## Stop the laboratory when finished

The commands below stop and remove only the containers created by this prototype. They do not delete the retained `artifacts` evidence folders.

```powershell
docker compose --project-name oai-two-slice-ran -f .\config\phase2\docker-compose.rfsim.yaml down
docker compose --project-name oai-two-slice-forensic-lab -f .\third_party\openairinterface5g\doc\tutorial_resources\oai-cn5g\docker-compose.yaml down
```

## Troubleshooting

| What you see | What to do |
|---|---|
| `Docker Engine` fails in preflight | Start Docker Desktop, wait for **Engine running**, then run preflight again. |
| `WSL2` fails in preflight | Follow the first-time Windows setup section. Confirm CPU virtualisation is enabled in the computer firmware. |
| `Official OAI tutorial source is missing` | Repeat the OAI clone and checkout commands exactly. The required path is `third_party\openairinterface5g`. |
| A core container is unhealthy | Run the two Phase 2 `down` commands above, then repeat the full start sequence from Step 2. |
| A virtual device is not healthy | Check that all core containers are healthy first, run `Start-Phase2Rfsim.ps1` again, then run Phase 2 traffic again. |
| Phase 6 rejects an input run | Run Phases 2 to 5 again in order. Phase 6 checks that each input run completed and that the chain of evidence links correctly. |

## Repository contents and version control

The repository stores the C# source code, PowerShell scripts, configuration, approved rules, Phase 6 scenarios, README and presentation. It does not store container images, the upstream OAI source, transient build output or locally generated evidence runs. The OAI source is reconstructed using the pinned upstream revision shown above; each experiment run recreates its own evidence under `artifacts\runs`.
