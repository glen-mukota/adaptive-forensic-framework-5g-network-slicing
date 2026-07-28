# Third implementation: normalised evidence ingestion

This stage implements Step 3 of the approved action plan: normalised ingestion of the retained Phase 1 and Phase 2 laboratory artefacts. It registers selected sources as structured, queryable C#/.NET records without changing the original evidence files.

## Scope and alignment

- The C# command `ingest-evidence` validates a source catalogue and registers Phase 1 core health / NRF / baseline evidence and Phase 2 scenario, UE, gNB, AMF, SMF, UPF, traffic and resource artefacts.
- Every indexed record contains a source reference, normalised evidence timestamp, source kind, source component, network function, scenario identifier, preliminary slice context where it is already known, artifact size and a read-only input confirmation.
- The Phase 2 ground-truth ledger supplies the preliminary `SST`, `SD` and `DNN` context for the eMBB and mMTC scenario-specific records. This is retained context, not a Phase 4 attribution decision.
- `query-evidence` retrieves records by network function, source kind or scenario identifier. The acceptance run proves that SMF, mMTC-scenario and user-plane records can be retrieved from the generated index.
- Original artifacts are opened/read only. Per-artifact hashing, custody records, attribution rules, adaptive controls and formal evaluation are deliberately not claimed here; they remain later approved steps.

## Run sequence

```powershell
dotnet build .\AdaptiveForensics.sln --configuration Release
.\scripts\Run-Phase3EvidenceIngestion.ps1
```

The script selects the most recent healthy Phase 1 baseline and completed Phase 2 run unless explicit `-Phase1Run` and `-Phase2Run` directories are supplied. It writes a fresh `artifacts\runs\phase3-<timestamp>` directory containing:

- `evidence-index.json` - the structured, queryable normalised record index;
- `query-oai-smf.txt` - retrieval proof for the SMF source;
- `query-mmtc-scenario.txt` - retrieval proof for `P2-NORMAL-MMTC-001`;
- `query-user-plane-traffic.txt` - retrieval proof for the two iperf records; and
- `phase3-summary.json` - the acceptance summary, source count, record count and read-only check.

## Acceptance evidence

The phase is accepted only when all configured artifacts exist, at least 27 normalised records are written, every input record is marked unchanged, and all three retrieval queries return results. A successful run demonstrates queryable normalised records from the approved selected sources without altering the original artefacts.
