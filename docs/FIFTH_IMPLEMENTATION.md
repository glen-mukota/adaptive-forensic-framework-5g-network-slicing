# Fifth implementation: approved adaptive collection, integrity and chain of custody

This stage implements Step 5 of the approved action plan. It uses the accepted Phase 4 attribution report to apply an approved collection-priority rule, calculate SHA-256 values for every registered source, create a custody ledger and immediately verify the hashes on retrieval. It does not alter the original Phase 1 or Phase 2 artifacts and it does not change the existing Phase 3 ingestion or Phase 4 attribution decisions.

## Scope and alignment

- `P5-APPROVED-TRAFFIC-PRIORITY` is the only enabled rule in the catalogue. It fires only when Phase 4 has attributed a controlled user-plane traffic record to a specific laboratory slice.
- The rule elevates the priority of traffic, UE-session and control-plane evidence references for that attributed slice. Ambiguous and unattributed records cannot trigger the rule, preventing cross-slice assumptions.
- Each evidence record receives source reference, collector, acquisition action, timestamp, SHA-256 value and repository-report location. The custody ledger also links entries in sequence through the preceding-entry hash.
- SHA-256 is calculated while the source is read-only. The prototype checks that the source did not change during hashing, then recomputes the hash immediately as a retrieval verification.
- A run is accepted only when an approved rule application is recorded, all Phase 4 decisions have custody entries, required custody fields are complete, every source remains unchanged during hashing and every recomputed hash matches.

## Run sequence

```powershell
dotnet build .\AdaptiveForensics.sln --configuration Release
.\scripts\Run-Phase4SliceAttribution.ps1
.\scripts\Run-Phase5AdaptiveCollection.ps1
```

The script selects the latest completed Phase 4 run unless `-Phase4Run` is supplied. It writes `artifacts\runs\phase5-<timestamp>` containing:

- `adaptive-collection-report.json` — complete Phase 5 report, rule applications, custody ledger and verification results;
- `custody-ledger.json` — the required custody metadata for every registered record;
- `hash-verification-report.json` — stored versus recomputed SHA-256 values;
- `adaptation-timing-log.json` — approved trigger-to-rule-update timing; and
- `phase5-summary.json` — the acceptance result and counts.

## Research boundary

This is a controlled-laboratory implementation of explainable, approved adaptation. It prioritises which approved evidence references should be collected; it does not automatically alter a live network, alter slice configuration or claim production deployment.
