# Sixth implementation: controlled evaluation and refinement

This stage implements Step 6 of the approved action plan. It evaluates the retained Phase 1 to Phase 5 laboratory evidence against predefined scenarios and measures. It does not alter any original evidence source, network configuration, subscriber record or container state.

## Scope and alignment

- The normal-operation scenario checks the two labelled eMBB and mMTC traffic cases against the scenario ledger and attribution report.
- The allocation-anomaly scenario is a data-only replay of an unrecognised slice context. The evaluator must leave it without an individual slice assignment. No rogue gNB activity, radio transmission or slice modification occurs.
- The shared-function scenario checks that shared gNB, core and resource records remain ambiguous instead of contaminating an individual slice attribution.
- The cross-layer scenario checks for radio, control-plane, user-plane and resource evidence types in the shared controlled run.
- The load-triggered scenario checks that the approved Phase 5 collection rule produces one elevated-priority update for each labelled slice and retains the timing.

The evaluator calculates attribution accuracy, normal-operation collection completeness, integrity verification, adaptation latency and custody completeness from the retained reports. It also retains a separate observed-overhead record from Docker resource snapshots. That record describes laboratory activity; it does not claim an isolated production cost for the forensic program.

## Run sequence

Run the completed evidence pipeline first. Then execute:

```powershell
dotnet build .\AdaptiveForensics.sln --configuration Release
.\scripts\Run-Phase6Evaluation.ps1
```

The script chooses the newest completed Phase 1 to Phase 5 run by default. It rejects a run when its summary is incomplete or when the evidence chain does not connect to the preceding run. To evaluate a specific retained chain, pass all five explicit run folders:

```powershell
.\scripts\Run-Phase6Evaluation.ps1 `
  -Phase1Run .\artifacts\runs\<phase1-run> `
  -Phase2Run .\artifacts\runs\<phase2-run> `
  -Phase3Run .\artifacts\runs\<phase3-run> `
  -Phase4Run .\artifacts\runs\<phase4-run> `
  -Phase5Run .\artifacts\runs\<phase5-run>
```

## Evidence produced

The script writes `artifacts\runs\phase6-<timestamp>` containing:

- `evaluation-report.json` - complete Phase 6 result, input checks, scenarios, measures, overhead observation and limitations;
- `input-checks.json` - SHA-256 linked checks for every Phase 1 to Phase 5 input report;
- `scenario-results.json` - expected and observed result for each controlled scenario;
- `metrics.json` - operational definition, calculation, value and verification source for each measure;
- `operational-overhead.json` - Docker before/after observation plus retained-evidence size; and
- `limitations.txt` - laboratory and interpretation limits that must accompany the results.

## Acceptance

The Phase 6 run succeeds only when all five scenarios pass, all six measures pass and every retained Phase 1 to Phase 5 input check is valid. This makes the final result repeatable from the pinned configuration, traffic scripts, approved rules and retained evidence chain.
