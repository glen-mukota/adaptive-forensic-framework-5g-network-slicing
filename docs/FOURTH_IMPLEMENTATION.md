# Fourth implementation: slice attribution and transparent rule logic

This stage implements Step 4 of the approved action plan. It takes the normalised Phase 3 evidence index and makes an explicit, reviewable decision for every record: attributed to eMBB, attributed to mMTC, ambiguous, or unattributed. It does not implement the later adaptive-control, hashing, integrity or chain-of-custody stage.

## Scope and alignment

- The C# command `attribute-slices` reads a Phase 3 evidence index and the versioned `config/phase4/slice-attribution-rules.json` catalogue.
- Every decision retains the evidence timestamp, network function, source kind, scenario, preliminary S-NSSAI/DNN context, assigned or candidate slice context, decision status, rule identifier, rationale and the inputs considered.
- `P4-SCENARIO-EMBB` maps the labelled eMBB scenario to `SST 1 / SD FFFFFF / DNN oai`; `P4-SCENARIO-MMTC` maps the labelled mMTC scenario to `SST 3 / SD FFFFFF / DNN mmtc`.
- `P4-SHARED-MULTI-SCENARIO` deliberately marks shared gNB, core and resource records as **ambiguous** when they span both scenarios and have no slice-specific context. `P4-MISSING-SLICE-CONTEXT` keeps records with insufficient evidence **unattributed**. The prototype therefore does not guess.
- A built-in conflict safeguard refuses an assignment if a scenario rule conflicts with an available S-NSSAI/DNN context.

## Run sequence

```powershell
dotnet build .\AdaptiveForensics.sln --configuration Release
.\scripts\Run-Phase4SliceAttribution.ps1
```

The script selects the latest completed Phase 3 run unless `-Phase3Run` is supplied. It writes `artifacts\runs\phase4-<timestamp>` containing:

- `slice-attribution-report.json` — the full decision report with inputs, assigned/candidate slice contexts, rule identifiers and reasons;
- `query-embb-attributed.txt` and `query-mmtc-attributed.txt` — retrieval proof for the two labelled scenarios;
- `query-shared-ambiguous.txt` — proof that shared, cross-scenario records remain ambiguous; and
- `phase4-summary.json` — the acceptance result and ground-truth attribution accuracy.

## Acceptance evidence

The stage is accepted only when every Phase 3 record receives a decision, every record with a complete ground-truth S-NSSAI/DNN context is attributed to the matching configured slice, the report records the rule and rationale, and the eMBB, mMTC and shared-ambiguous queries all return results. This is a controlled laboratory attribution result; it does not claim production-network coverage or later integrity/custody controls.
