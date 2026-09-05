using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ForensicFramework.LabBootstrap;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static int Main(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
        var root = FindSolutionRoot();
        var configPath = GetOption(args, "--config") ?? Path.Combine(root, "config", "lab-profile.json");
        var outputPath = GetOption(args, "--output");

        return command switch
        {
            "validate" => Validate(configPath),
            "manifest" => WriteManifest(configPath, outputPath ?? Path.Combine(root, "artifacts", "lab-manifest.json")),
            "preflight" => WritePreflight(root, configPath, outputPath ?? Path.Combine(root, "artifacts", "preflight.json")),
            "scenario-ledger" => WriteScenarioLedger(
                GetOption(args, "--config") ?? Path.Combine(root, "config", "phase2", "scenarios.json"),
                outputPath ?? Path.Combine(root, "artifacts", "phase2-scenario-ledger.json")),
            "ingest-evidence" => WriteEvidenceIndex(
                GetOption(args, "--config") ?? Path.Combine(root, "config", "phase3", "evidence-ingestion.json"),
                GetOption(args, "--phase1-run"),
                GetOption(args, "--phase2-run"),
                outputPath ?? Path.Combine(root, "artifacts", "phase3-evidence-index.json")),
            "query-evidence" => QueryEvidence(
                GetOption(args, "--index"),
                GetOption(args, "--network-function"),
                GetOption(args, "--source-kind"),
                GetOption(args, "--scenario")),
            "attribute-slices" => WriteSliceAttributionReport(
                GetOption(args, "--config") ?? Path.Combine(root, "config", "phase4", "slice-attribution-rules.json"),
                GetOption(args, "--index"),
                outputPath ?? Path.Combine(root, "artifacts", "phase4-attribution-report.json")),
            "query-attribution" => QueryAttribution(
                GetOption(args, "--report"),
                GetOption(args, "--status"),
                GetOption(args, "--rule-id"),
                GetOption(args, "--scenario")),
            "apply-adaptive-collection" => WriteAdaptiveCollectionReport(
                GetOption(args, "--config") ?? Path.Combine(root, "config", "phase5", "adaptive-collection-rules.json"),
                GetOption(args, "--attribution-report"),
                outputPath ?? Path.Combine(root, "artifacts", "phase5-adaptive-collection-report.json")),
            "query-adaptive-collection" => QueryAdaptiveCollection(
                GetOption(args, "--report"),
                GetOption(args, "--rule-id"),
                GetOption(args, "--record-id")),
            "evaluate-prototype" => WriteEvaluationReport(
                GetOption(args, "--config") ?? Path.Combine(root, "config", "phase6", "evaluation-scenarios.json"),
                GetOption(args, "--phase1-run"),
                GetOption(args, "--phase2-run"),
                GetOption(args, "--phase3-run"),
                GetOption(args, "--phase4-run"),
                GetOption(args, "--phase5-run"),
                outputPath ?? Path.Combine(root, "artifacts", "phase6-evaluation-report.json")),
            "query-evaluation" => QueryEvaluation(
                GetOption(args, "--report"),
                GetOption(args, "--scenario-id"),
                GetOption(args, "--metric")),
            "help" or "--help" or "-h" => PrintHelp(),
            _ => Fail($"Unknown command '{command}'. Run 'help' to list supported commands.")
        };
    }

    private static int PrintHelp()
    {
        Console.WriteLine("ForensicFramework.LabBootstrap");
        Console.WriteLine("  validate  [--config <path>]             Validate the two-slice laboratory profile.");
        Console.WriteLine("  manifest  [--config <path>] [--output <path>]  Write a SHA-256 configuration manifest.");
        Console.WriteLine("  preflight [--config <path>] [--output <path>]  Check Visual Studio, WSL2 and Docker readiness.");
        Console.WriteLine("  scenario-ledger [--config <path>] [--output <path>]  Validate and write the Phase 2 ground-truth ledger.");
        Console.WriteLine("  ingest-evidence --phase1-run <path> --phase2-run <path> [--config <path>] [--output <path>]  Register normalised Phase 1/2 evidence records.");
        Console.WriteLine("  query-evidence --index <path> [--network-function <name>] [--source-kind <kind>] [--scenario <id>]  Retrieve indexed evidence records.");
        Console.WriteLine("  attribute-slices --index <path> [--config <path>] [--output <path>]  Apply transparent Phase 4 slice-attribution rules.");
        Console.WriteLine("  query-attribution --report <path> [--status <status>] [--rule-id <id>] [--scenario <id>]  Retrieve attribution decisions.");
        Console.WriteLine("  apply-adaptive-collection --attribution-report <path> [--config <path>] [--output <path>]  Apply approved Phase 5 collection rules and create integrity/custody evidence.");
        Console.WriteLine("  query-adaptive-collection --report <path> [--rule-id <id>] [--record-id <id>]  Retrieve Phase 5 rule and custody evidence.");
        Console.WriteLine("  evaluate-prototype --phase1-run <path> --phase2-run <path> --phase3-run <path> --phase4-run <path> --phase5-run <path> [--config <path>] [--output <path>]  Evaluate all controlled Phase 6 scenarios and measures.");
        Console.WriteLine("  query-evaluation --report <path> [--scenario-id <id>] [--metric <name>]  Retrieve Phase 6 evaluation results.");
        return 0;
    }

    private static int Validate(string configPath)
    {
        var result = LoadAndValidate(configPath, out _);
        WriteValidationResult(result);
        return result.IsValid ? 0 : 1;
    }

    private static int WriteManifest(string configPath, string outputPath)
    {
        var validation = LoadAndValidate(configPath, out var profile);
        WriteValidationResult(validation);
        if (!validation.IsValid || profile is null)
        {
            return 1;
        }

        var manifest = new LabManifest(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ConfigurationPath: Path.GetFullPath(configPath),
            ConfigurationSha256: ComputeSha256(configPath),
            OaiConfigurationPath: ResolveSolutionPath(profile.OaiConfigurationRelativePath),
            OaiConfigurationSha256: ComputeSha256(ResolveSolutionPath(profile.OaiConfigurationRelativePath)),
            EnvironmentName: profile.EnvironmentName,
            UpstreamRevision: profile.UpstreamRevision,
            Slices: profile.Slices,
            EvidenceSources: profile.EvidenceSources,
            RequiredServices: profile.RequiredServices);

        WriteJson(outputPath, manifest);
        Console.WriteLine($"LAB_MANIFEST_WRITTEN {Path.GetFullPath(outputPath)}");
        return 0;
    }

    private static int WritePreflight(string root, string configPath, string outputPath)
    {
        var validation = LoadAndValidate(configPath, out _);
        var checks = new List<ReadinessCheck>
        {
            CheckVisualStudio(),
            CheckDotNet(),
            CheckProcess("WSL2", "wsl.exe", "--status", output =>
                !output.Contains("unable to start", StringComparison.OrdinalIgnoreCase) &&
                !output.Contains("no installed distributions", StringComparison.OrdinalIgnoreCase)),
            CheckProcess("Docker Engine", "docker.exe", "version --format {{.Server.Version}}", output =>
                !string.IsNullOrWhiteSpace(output)),
            CheckFile("OAI tutorial source", Path.Combine(root, "third_party", "openairinterface5g", "doc", "tutorial_resources", "oai-cn5g", "docker-compose.yaml")),
            CheckFile("Two-slice OAI patch", Path.Combine(root, "config", "oai-two-slice-config.patch"))
        };

        var report = new PreflightReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ConfigurationValid: validation.IsValid,
            ValidationErrors: validation.Errors,
            Checks: checks,
            ReadyForContainerDeployment: validation.IsValid && checks.All(check => check.Passed));

        WriteJson(outputPath, report);
        foreach (var check in checks)
        {
            Console.WriteLine($"{(check.Passed ? "PASS" : "BLOCKED")} {check.Name}: {check.Summary}");
        }
        Console.WriteLine($"PREFLIGHT_REPORT_WRITTEN {Path.GetFullPath(outputPath)}");
        return report.ReadyForContainerDeployment ? 0 : 2;
    }

    private static int WriteScenarioLedger(string scenarioPath, string outputPath)
    {
        if (!File.Exists(scenarioPath)) return Fail($"Scenario configuration not found: {Path.GetFullPath(scenarioPath)}");

        ScenarioCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<ScenarioCatalog>(File.ReadAllText(scenarioPath), JsonOptions);
        }
        catch (JsonException error)
        {
            return Fail($"Invalid scenario JSON: {error.Message}");
        }

        var errors = new List<string>();
        if (catalog is null) errors.Add("Scenario configuration is empty.");
        else
        {
            if (string.IsNullOrWhiteSpace(catalog.Phase)) errors.Add("phase is required.");
            if (catalog.Scenarios is null || catalog.Scenarios.Length < 2) errors.Add("At least two labelled scenarios are required.");
            foreach (var scenario in catalog.Scenarios ?? [])
            {
                if (string.IsNullOrWhiteSpace(scenario.Id)) errors.Add("Each scenario requires an id.");
                if (string.IsNullOrWhiteSpace(scenario.Dnn)) errors.Add($"Scenario '{scenario.Id}' requires a DNN.");
                if (scenario.Sst is < 0 or > 255) errors.Add($"Scenario '{scenario.Id}' has an invalid SST.");
                if (!Regex.IsMatch(scenario.Sd ?? string.Empty, "^[0-9A-Fa-f]{6}$")) errors.Add($"Scenario '{scenario.Id}' requires a six-digit hexadecimal SD.");
                if (scenario.TargetRateKbps <= 0 || scenario.DurationSeconds <= 0) errors.Add($"Scenario '{scenario.Id}' requires a positive rate and duration.");
                if (scenario.ExpectedArtifacts is null || scenario.ExpectedArtifacts.Length == 0) errors.Add($"Scenario '{scenario.Id}' requires expected artefacts.");
            }
        }

        if (errors.Count > 0)
        {
            foreach (var error in errors) Console.Error.WriteLine($"SCENARIO_VALIDATION_ERROR {error}");
            return 1;
        }

        var ledger = new ScenarioLedger(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ScenarioDefinitionPath: Path.GetFullPath(scenarioPath),
            ScenarioDefinitionSha256: ComputeSha256(scenarioPath),
            Phase: catalog!.Phase,
            Purpose: catalog.Purpose,
            Scenarios: catalog.Scenarios!);
        WriteJson(outputPath, ledger);
        Console.WriteLine("SCENARIO_CATALOG_VALID");
        Console.WriteLine($"SCENARIO_LEDGER_WRITTEN {Path.GetFullPath(outputPath)}");
        return 0;
    }

    private static int WriteEvidenceIndex(string configPath, string? phase1Run, string? phase2Run, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(phase1Run) || string.IsNullOrWhiteSpace(phase2Run))
        {
            return Fail("ingest-evidence requires both --phase1-run and --phase2-run.");
        }

        if (!File.Exists(configPath)) return Fail($"Evidence-ingestion configuration not found: {Path.GetFullPath(configPath)}");
        if (!Directory.Exists(phase1Run)) return Fail($"Phase 1 evidence directory not found: {Path.GetFullPath(phase1Run)}");
        if (!Directory.Exists(phase2Run)) return Fail($"Phase 2 evidence directory not found: {Path.GetFullPath(phase2Run)}");

        EvidenceIngestionCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<EvidenceIngestionCatalog>(File.ReadAllText(configPath), JsonOptions);
        }
        catch (JsonException error)
        {
            return Fail($"Invalid evidence-ingestion JSON: {error.Message}");
        }

        var errors = ValidateEvidenceIngestionCatalog(catalog);
        if (errors.Count > 0)
        {
            foreach (var error in errors) Console.Error.WriteLine($"INGESTION_CONFIG_ERROR {error}");
            return 1;
        }

        var inputRuns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["phase1"] = Path.GetFullPath(phase1Run),
            ["phase2"] = Path.GetFullPath(phase2Run)
        };
        var records = new List<NormalisedEvidenceRecord>();
        var ingestedAt = DateTimeOffset.UtcNow;

        foreach (var source in catalog!.Sources!)
        {
            var inputRoot = inputRuns[source.InputRun];
            var artifactPath = ResolveInputArtifact(inputRoot, source.RelativePath);
            if (!File.Exists(artifactPath))
            {
                Console.Error.WriteLine($"MISSING_REQUIRED_ARTIFACT {source.Id}: {artifactPath}");
                return 1;
            }

            records.AddRange(RegisterEvidenceSource(source, artifactPath, ingestedAt));
        }

        if (records.Count < catalog.MinimumRecordCount)
        {
            return Fail($"Ingestion produced {records.Count} records; the catalog requires at least {catalog.MinimumRecordCount}.");
        }

        var index = new EvidenceIndex(
            Phase: catalog.Phase,
            Purpose: catalog.Purpose,
            IngestedAtUtc: ingestedAt,
            IngestionConfigurationPath: Path.GetFullPath(configPath),
            InputRuns: inputRuns,
            SourceDefinitionCount: catalog.Sources!.Length,
            RecordCount: records.Count,
            Records: records);
        WriteJson(outputPath, index);
        Console.WriteLine($"EVIDENCE_INGESTION_VALID records={records.Count} sources={catalog.Sources.Length}");
        Console.WriteLine($"EVIDENCE_INDEX_WRITTEN {Path.GetFullPath(outputPath)}");
        return 0;
    }

    private static int QueryEvidence(string? indexPath, string? networkFunction, string? sourceKind, string? scenario)
    {
        if (string.IsNullOrWhiteSpace(indexPath)) return Fail("query-evidence requires --index <path>.");
        if (!File.Exists(indexPath)) return Fail($"Evidence index not found: {Path.GetFullPath(indexPath)}");

        EvidenceIndex? index;
        try
        {
            index = JsonSerializer.Deserialize<EvidenceIndex>(File.ReadAllText(indexPath), JsonOptions);
        }
        catch (JsonException error)
        {
            return Fail($"Invalid evidence index JSON: {error.Message}");
        }

        if (index?.Records is null) return Fail("Evidence index is empty or does not contain records.");
        var results = index.Records.Where(record =>
                (string.IsNullOrWhiteSpace(networkFunction) || string.Equals(record.NetworkFunction, networkFunction, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(sourceKind) || string.Equals(record.SourceKind, sourceKind, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(scenario) || string.Equals(record.ScenarioId, scenario, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Console.WriteLine($"EVIDENCE_QUERY_MATCHES {results.Length}");
        Console.WriteLine(JsonSerializer.Serialize(results, JsonOptions));
        return results.Length > 0 ? 0 : 2;
    }

    private static int WriteSliceAttributionReport(string configPath, string? indexPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(indexPath)) return Fail("attribute-slices requires --index <path>.");
        if (!File.Exists(indexPath)) return Fail($"Evidence index not found: {Path.GetFullPath(indexPath)}");
        if (!File.Exists(configPath)) return Fail($"Slice-attribution rules not found: {Path.GetFullPath(configPath)}");

        EvidenceIndex? evidenceIndex;
        SliceAttributionCatalog? catalog;
        try
        {
            evidenceIndex = JsonSerializer.Deserialize<EvidenceIndex>(File.ReadAllText(indexPath), JsonOptions);
            catalog = JsonSerializer.Deserialize<SliceAttributionCatalog>(File.ReadAllText(configPath), JsonOptions);
        }
        catch (JsonException error)
        {
            return Fail($"Invalid Phase 4 JSON: {error.Message}");
        }

        if (evidenceIndex?.Records is null || evidenceIndex.Records.Count == 0)
        {
            return Fail("Evidence index is empty or does not contain records.");
        }

        var errors = ValidateSliceAttributionCatalog(catalog);
        if (errors.Count > 0)
        {
            foreach (var error in errors) Console.Error.WriteLine($"ATTRIBUTION_CONFIG_ERROR {error}");
            return 1;
        }

        var attributedAt = DateTimeOffset.UtcNow;
        var decisions = evidenceIndex.Records
            .Select(record => AttributeRecord(record, catalog!, attributedAt))
            .ToArray();

        var groundTruthDecisions = decisions.Where(decision => HasCompleteSliceContext(decision.PreliminarySliceContext)).ToArray();
        var correctGroundTruthDecisions = groundTruthDecisions.Where(decision =>
            string.Equals(decision.DecisionStatus, "attributed", StringComparison.OrdinalIgnoreCase) &&
            ContextsMatch(decision.PreliminarySliceContext, decision.AssignedSliceContext)).ToArray();
        var accepted = groundTruthDecisions.Length > 0 &&
            groundTruthDecisions.Length == correctGroundTruthDecisions.Length &&
            decisions.Length == evidenceIndex.Records.Count &&
            decisions.All(decision => !string.IsNullOrWhiteSpace(decision.RuleId));

        var report = new SliceAttributionReport(
            Phase: catalog!.Phase,
            Purpose: catalog.Purpose,
            AttributedAtUtc: attributedAt,
            EvidenceIndexPath: Path.GetFullPath(indexPath),
            EvidenceIndexSha256: ComputeSha256(indexPath),
            AttributionRulesPath: Path.GetFullPath(configPath),
            AttributionRulesSha256: ComputeSha256(configPath),
            InputRecordCount: evidenceIndex.Records.Count,
            DecisionCount: decisions.Length,
            AttributedCount: decisions.Count(decision => string.Equals(decision.DecisionStatus, "attributed", StringComparison.OrdinalIgnoreCase)),
            AmbiguousCount: decisions.Count(decision => string.Equals(decision.DecisionStatus, "ambiguous", StringComparison.OrdinalIgnoreCase)),
            UnattributedCount: decisions.Count(decision => string.Equals(decision.DecisionStatus, "unattributed", StringComparison.OrdinalIgnoreCase)),
            KnownGroundTruthDecisionCount: groundTruthDecisions.Length,
            KnownGroundTruthCorrectCount: correctGroundTruthDecisions.Length,
            KnownGroundTruthAccuracyPercent: groundTruthDecisions.Length == 0 ? 0 : Math.Round(correctGroundTruthDecisions.Length * 100.0 / groundTruthDecisions.Length, 2),
            Accepted: accepted,
            Decisions: decisions);

        WriteJson(outputPath, report);
        Console.WriteLine($"SLICE_ATTRIBUTION_REPORT_WRITTEN {Path.GetFullPath(outputPath)}");
        if (!accepted)
        {
            return Fail("Phase 4 attribution acceptance checks did not pass.");
        }

        Console.WriteLine($"SLICE_ATTRIBUTION_VALID decisions={decisions.Length} groundTruth={groundTruthDecisions.Length} accuracy={report.KnownGroundTruthAccuracyPercent:F2}");
        return 0;
    }

    private static int QueryAttribution(string? reportPath, string? status, string? ruleId, string? scenario)
    {
        if (string.IsNullOrWhiteSpace(reportPath)) return Fail("query-attribution requires --report <path>.");
        if (!File.Exists(reportPath)) return Fail($"Attribution report not found: {Path.GetFullPath(reportPath)}");

        SliceAttributionReport? report;
        try
        {
            report = JsonSerializer.Deserialize<SliceAttributionReport>(File.ReadAllText(reportPath), JsonOptions);
        }
        catch (JsonException error)
        {
            return Fail($"Invalid attribution report JSON: {error.Message}");
        }

        if (report?.Decisions is null) return Fail("Attribution report is empty or does not contain decisions.");
        var results = report.Decisions.Where(decision =>
                (string.IsNullOrWhiteSpace(status) || string.Equals(decision.DecisionStatus, status, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(ruleId) || string.Equals(decision.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(scenario) || string.Equals(decision.ScenarioId, scenario, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Console.WriteLine($"ATTRIBUTION_QUERY_MATCHES {results.Length}");
        Console.WriteLine(JsonSerializer.Serialize(results, JsonOptions));
        return results.Length > 0 ? 0 : 2;
    }

    private static int WriteAdaptiveCollectionReport(string configPath, string? attributionReportPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(attributionReportPath)) return Fail("apply-adaptive-collection requires --attribution-report <path>.");
        if (!File.Exists(attributionReportPath)) return Fail($"Attribution report not found: {Path.GetFullPath(attributionReportPath)}");
        if (!File.Exists(configPath)) return Fail($"Adaptive-collection rules not found: {Path.GetFullPath(configPath)}");

        SliceAttributionReport? attributionReport;
        AdaptiveCollectionCatalog? catalog;
        try
        {
            attributionReport = JsonSerializer.Deserialize<SliceAttributionReport>(File.ReadAllText(attributionReportPath), JsonOptions);
            catalog = JsonSerializer.Deserialize<AdaptiveCollectionCatalog>(File.ReadAllText(configPath), JsonOptions);
        }
        catch (JsonException error)
        {
            return Fail($"Invalid Phase 5 JSON: {error.Message}");
        }

        if (attributionReport?.Decisions is null || attributionReport.Decisions.Count == 0)
        {
            return Fail("Attribution report is empty or does not contain decisions.");
        }

        if (!attributionReport.Accepted)
        {
            return Fail("The supplied Phase 4 attribution report did not pass its acceptance checks.");
        }

        var errors = ValidateAdaptiveCollectionCatalog(catalog);
        if (errors.Count > 0)
        {
            foreach (var error in errors) Console.Error.WriteLine($"ADAPTIVE_COLLECTION_CONFIG_ERROR {error}");
            return 1;
        }

        var approvedCatalog = catalog!;
        var outputFullPath = Path.GetFullPath(outputPath);
        var observedAt = DateTimeOffset.UtcNow;
        var ruleApplications = ApplyApprovedCollectionRules(attributionReport.Decisions, approvedCatalog, observedAt);
        var custodyLedger = BuildCustodyLedger(attributionReport.Decisions, approvedCatalog, outputFullPath);
        var verificationResults = VerifyCustodyLedger(custodyLedger);
        var completeCustodyCount = custodyLedger.Count(entry => HasCompleteCustodyMetadata(entry));
        var allSourcesUnchanged = custodyLedger.All(entry => entry.SourceUnchangedDuringHash);
        var allHashesMatch = verificationResults.All(result => result.SourceExists && result.HashMatches);
        var applicationsAreApproved = ruleApplications.All(application => approvedCatalog.Rules!.Any(rule =>
            string.Equals(rule.Id, application.RuleId, StringComparison.OrdinalIgnoreCase) && rule.Approved && rule.Enabled));
        var accepted = ruleApplications.Count > 0 &&
            applicationsAreApproved &&
            completeCustodyCount == custodyLedger.Count &&
            custodyLedger.Count == attributionReport.Decisions.Count &&
            allSourcesUnchanged &&
            allHashesMatch;

        var report = new AdaptiveCollectionReport(
            Phase: approvedCatalog.Phase,
            Purpose: approvedCatalog.Purpose,
            CollectedAtUtc: DateTimeOffset.UtcNow,
            AttributionReportPath: Path.GetFullPath(attributionReportPath),
            AttributionReportSha256: ComputeSha256(attributionReportPath),
            AdaptiveRuleCatalogPath: Path.GetFullPath(configPath),
            AdaptiveRuleCatalogSha256: ComputeSha256(configPath),
            Collector: approvedCatalog.Collector,
            IntegrityAlgorithm: approvedCatalog.IntegrityAlgorithm,
            InputDecisionCount: attributionReport.Decisions.Count,
            ApprovedRuleApplicationCount: ruleApplications.Count,
            CustodyEntryCount: custodyLedger.Count,
            CompleteCustodyEntryCount: completeCustodyCount,
            VerifiedHashCount: verificationResults.Count(result => result.SourceExists && result.HashMatches),
            TriggerToRuleUpdateLatencyMilliseconds: ruleApplications.Count == 0 ? 0 : ruleApplications.Max(application => application.AdaptationLatencyMilliseconds),
            Accepted: accepted,
            RuleApplications: ruleApplications,
            CustodyLedger: custodyLedger,
            HashVerificationResults: verificationResults);

        WriteJson(outputFullPath, report);
        Console.WriteLine($"ADAPTIVE_COLLECTION_REPORT_WRITTEN {outputFullPath}");
        if (!accepted)
        {
            return Fail("Phase 5 adaptive collection, integrity and custody acceptance checks did not pass.");
        }

        Console.WriteLine($"ADAPTIVE_COLLECTION_VALID applications={ruleApplications.Count} custody={custodyLedger.Count} hashes={report.VerifiedHashCount}");
        return 0;
    }

    private static int QueryAdaptiveCollection(string? reportPath, string? ruleId, string? recordId)
    {
        if (string.IsNullOrWhiteSpace(reportPath)) return Fail("query-adaptive-collection requires --report <path>.");
        if (!File.Exists(reportPath)) return Fail($"Adaptive collection report not found: {Path.GetFullPath(reportPath)}");

        AdaptiveCollectionReport? report;
        try
        {
            report = JsonSerializer.Deserialize<AdaptiveCollectionReport>(File.ReadAllText(reportPath), JsonOptions);
        }
        catch (JsonException error)
        {
            return Fail($"Invalid adaptive collection report JSON: {error.Message}");
        }

        if (report?.CustodyLedger is null || report.RuleApplications is null)
        {
            return Fail("Adaptive collection report is empty or incomplete.");
        }

        var applications = report.RuleApplications
            .Where(application => string.IsNullOrWhiteSpace(ruleId) || string.Equals(application.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var entries = report.CustodyLedger
            .Where(entry => string.IsNullOrWhiteSpace(recordId) || string.Equals(entry.RecordId, recordId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Console.WriteLine($"ADAPTIVE_RULE_QUERY_MATCHES {applications.Length}");
        Console.WriteLine($"CUSTODY_QUERY_MATCHES {entries.Length}");
        Console.WriteLine(JsonSerializer.Serialize(new { applications, entries }, JsonOptions));
        return applications.Length > 0 || entries.Length > 0 ? 0 : 2;
    }

    private static int WriteEvaluationReport(
        string configPath,
        string? phase1Run,
        string? phase2Run,
        string? phase3Run,
        string? phase4Run,
        string? phase5Run,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(phase1Run) || string.IsNullOrWhiteSpace(phase2Run) ||
            string.IsNullOrWhiteSpace(phase3Run) || string.IsNullOrWhiteSpace(phase4Run) ||
            string.IsNullOrWhiteSpace(phase5Run))
        {
            return Fail("evaluate-prototype requires --phase1-run, --phase2-run, --phase3-run, --phase4-run and --phase5-run.");
        }

        if (!File.Exists(configPath)) return Fail($"Evaluation configuration not found: {Path.GetFullPath(configPath)}");

        var stopwatch = Stopwatch.StartNew();
        EvaluationCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<EvaluationCatalog>(File.ReadAllText(configPath), JsonOptions);
        }
        catch (JsonException error)
        {
            return Fail($"Invalid Phase 6 JSON: {error.Message}");
        }

        var catalogErrors = ValidateEvaluationCatalog(catalog);
        if (catalogErrors.Count > 0)
        {
            foreach (var error in catalogErrors) Console.Error.WriteLine($"EVALUATION_CONFIG_ERROR {error}");
            return 1;
        }

        var phase1Directory = Path.GetFullPath(phase1Run);
        var phase2Directory = Path.GetFullPath(phase2Run);
        var phase3Directory = Path.GetFullPath(phase3Run);
        var phase4Directory = Path.GetFullPath(phase4Run);
        var phase5Directory = Path.GetFullPath(phase5Run);
        var phase1SummaryPath = Path.Combine(phase1Directory, "baseline-summary.json");
        var phase2SummaryPath = Path.Combine(phase2Directory, "phase2-summary.json");
        var phase3SummaryPath = Path.Combine(phase3Directory, "phase3-summary.json");
        var evidenceIndexPath = Path.Combine(phase3Directory, "evidence-index.json");
        var phase4SummaryPath = Path.Combine(phase4Directory, "phase4-summary.json");
        var attributionReportPath = Path.Combine(phase4Directory, "slice-attribution-report.json");
        var phase5SummaryPath = Path.Combine(phase5Directory, "phase5-summary.json");
        var adaptiveReportPath = Path.Combine(phase5Directory, "adaptive-collection-report.json");
        var beforeStatsPath = Path.Combine(phase2Directory, "docker-stats-before.txt");
        var afterStatsPath = Path.Combine(phase2Directory, "docker-stats-after.txt");
        var attributionRulesPath = ResolveSolutionPath("config/phase4/slice-attribution-rules.json");

        var requiredFiles = new[]
        {
            phase1SummaryPath, phase2SummaryPath, phase3SummaryPath, evidenceIndexPath,
            phase4SummaryPath, attributionReportPath, phase5SummaryPath, adaptiveReportPath,
            beforeStatsPath, afterStatsPath, attributionRulesPath
        };
        var missingFiles = requiredFiles.Where(path => !File.Exists(path)).ToArray();
        if (missingFiles.Length > 0)
        {
            foreach (var path in missingFiles) Console.Error.WriteLine($"EVALUATION_INPUT_MISSING {path}");
            return 1;
        }

        try
        {
            var phase1Summary = DeserializeRequired<Phase1BaselineSummary>(phase1SummaryPath, "Phase 1 baseline summary");
            var phase2Summary = DeserializeRequired<Phase2TrafficSummary>(phase2SummaryPath, "Phase 2 traffic summary");
            var phase3Summary = DeserializeRequired<Phase3IngestionSummary>(phase3SummaryPath, "Phase 3 ingestion summary");
            var evidenceIndex = DeserializeRequired<EvidenceIndex>(evidenceIndexPath, "Phase 3 evidence index");
            var phase4Summary = DeserializeRequired<Phase4AttributionSummary>(phase4SummaryPath, "Phase 4 attribution summary");
            var attributionReport = DeserializeRequired<SliceAttributionReport>(attributionReportPath, "Phase 4 attribution report");
            var phase5Summary = DeserializeRequired<Phase5AdaptiveSummary>(phase5SummaryPath, "Phase 5 adaptive summary");
            var adaptiveReport = DeserializeRequired<AdaptiveCollectionReport>(adaptiveReportPath, "Phase 5 adaptive report");
            var attributionCatalog = DeserializeRequired<SliceAttributionCatalog>(attributionRulesPath, "Phase 4 rule catalogue");

            var inputChecks = BuildEvaluationInputChecks(
                phase1Summary, phase2Summary, phase3Summary, phase4Summary, phase5Summary,
                attributionReport, adaptiveReport,
                phase1Directory, phase2Directory, phase3Directory, phase4Directory, phase5Directory,
                phase1SummaryPath, phase2SummaryPath, phase3SummaryPath, evidenceIndexPath,
                phase4SummaryPath, attributionReportPath, phase5SummaryPath, adaptiveReportPath);

            if (inputChecks.Any(check => !check.Passed))
            {
                foreach (var check in inputChecks.Where(check => !check.Passed))
                {
                    Console.Error.WriteLine($"EVALUATION_INPUT_CHECK_FAILED {check.Name}: {check.Detail}");
                }
                return 1;
            }

            var validatedCatalog = catalog!;
            var scenarios = validatedCatalog.Scenarios!;
            var normalScenario = GetEvaluationScenario(scenarios, "normal-slice-operation");
            var anomalyScenario = GetEvaluationScenario(scenarios, "allocation-anomaly-replay");
            var sharedScenario = GetEvaluationScenario(scenarios, "shared-function-isolation");
            var crossLayerScenario = GetEvaluationScenario(scenarios, "cross-layer-evidence");
            var loadScenario = GetEvaluationScenario(scenarios, "load-triggered-adaptation");

            var scenarioResults = new List<EvaluationScenarioResult>
            {
                EvaluateNormalSliceOperation(normalScenario, phase2Summary, evidenceIndex, attributionReport),
                EvaluateAllocationAnomalyReplay(anomalyScenario, attributionCatalog),
                EvaluateSharedFunctionIsolation(sharedScenario, attributionReport),
                EvaluateCrossLayerEvidence(crossLayerScenario, evidenceIndex),
                EvaluateLoadTriggeredAdaptation(loadScenario, adaptiveReport)
            };

            var normalResult = scenarioResults.Single(result => result.Type == "normal-slice-operation");
            var overhead = CreateOperationalOverheadObservation(
                beforeStatsPath, afterStatsPath,
                CalculateDirectoryBytes(phase3Directory) + CalculateDirectoryBytes(phase4Directory) + CalculateDirectoryBytes(phase5Directory),
                stopwatch.ElapsedMilliseconds);
            var metrics = BuildEvaluationMetrics(normalResult, attributionReport, adaptiveReport, overhead);
            var accepted = scenarioResults.All(result => result.Passed) && metrics.All(metric => metric.Passed);
            stopwatch.Stop();
            overhead = overhead with { EvaluationExecutionMilliseconds = stopwatch.ElapsedMilliseconds };

            var report = new PrototypeEvaluationReport(
                Phase: validatedCatalog.Phase,
                Purpose: validatedCatalog.Purpose,
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                EvaluationConfigurationPath: Path.GetFullPath(configPath),
                EvaluationConfigurationSha256: ComputeSha256(configPath),
                Accepted: accepted,
                InputChecks: inputChecks,
                Scenarios: scenarioResults,
                Metrics: metrics,
                OperationalOverhead: overhead,
                Limitations: validatedCatalog.Limitations!,
                Reproducibility: "The report links a retained Phase 1 baseline, Phase 2 traffic run, Phase 3 index, Phase 4 attribution report and Phase 5 integrity/custody report. Re-running the supplied scripts recreates the same evaluation workflow under the documented laboratory conditions.");

            WriteJson(outputPath, report);
            Console.WriteLine($"EVALUATION_REPORT_WRITTEN {Path.GetFullPath(outputPath)}");
            if (!accepted)
            {
                return Fail("Phase 6 evaluation acceptance checks did not pass.");
            }

            Console.WriteLine($"EVALUATION_VALID scenarios={scenarioResults.Count} attribution={attributionReport.KnownGroundTruthAccuracyPercent:F2}% completeness={metrics.Single(metric => metric.Name == "Collection completeness").Value:F2}% integrity={metrics.Single(metric => metric.Name == "Integrity verification").Value:F2}%");
            return 0;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or JsonException or FormatException)
        {
            return Fail($"Phase 6 evaluation failed: {error.Message}");
        }
    }

    private static int QueryEvaluation(string? reportPath, string? scenarioId, string? metricName)
    {
        if (string.IsNullOrWhiteSpace(reportPath)) return Fail("query-evaluation requires --report <path>.");
        if (!File.Exists(reportPath)) return Fail($"Evaluation report not found: {Path.GetFullPath(reportPath)}");

        PrototypeEvaluationReport? report;
        try
        {
            report = JsonSerializer.Deserialize<PrototypeEvaluationReport>(File.ReadAllText(reportPath), JsonOptions);
        }
        catch (JsonException error)
        {
            return Fail($"Invalid evaluation report JSON: {error.Message}");
        }

        if (report?.Scenarios is null || report.Metrics is null)
        {
            return Fail("Evaluation report is empty or incomplete.");
        }

        var scenarios = report.Scenarios
            .Where(scenario => string.IsNullOrWhiteSpace(scenarioId) || string.Equals(scenario.Id, scenarioId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var metrics = report.Metrics
            .Where(metric => string.IsNullOrWhiteSpace(metricName) || string.Equals(metric.Name, metricName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Console.WriteLine($"EVALUATION_SCENARIO_QUERY_MATCHES {scenarios.Length}");
        Console.WriteLine($"EVALUATION_METRIC_QUERY_MATCHES {metrics.Length}");
        Console.WriteLine(JsonSerializer.Serialize(new { accepted = report.Accepted, scenarios, metrics, overhead = report.OperationalOverhead }, JsonOptions));
        return scenarios.Length > 0 || metrics.Length > 0 ? 0 : 2;
    }

    private static T DeserializeRequired<T>(string path, string name)
    {
        var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        return value ?? throw new InvalidOperationException($"{name} is empty: {path}");
    }

    private static IReadOnlyList<EvaluationInputCheck> BuildEvaluationInputChecks(
        Phase1BaselineSummary phase1,
        Phase2TrafficSummary phase2,
        Phase3IngestionSummary phase3,
        Phase4AttributionSummary phase4,
        Phase5AdaptiveSummary phase5,
        SliceAttributionReport attributionReport,
        AdaptiveCollectionReport adaptiveReport,
        string phase1Directory,
        string phase2Directory,
        string phase3Directory,
        string phase4Directory,
        string phase5Directory,
        string phase1SummaryPath,
        string phase2SummaryPath,
        string phase3SummaryPath,
        string evidenceIndexPath,
        string phase4SummaryPath,
        string attributionReportPath,
        string phase5SummaryPath,
        string adaptiveReportPath)
    {
        return
        [
            CreateInputCheck("Phase 1 baseline", phase1SummaryPath,
                string.Equals(phase1.Result, "healthy", StringComparison.OrdinalIgnoreCase),
                $"Result={phase1.Result}; expected healthy."),
            CreateInputCheck("Phase 2 controlled traffic", phase2SummaryPath,
                string.Equals(phase2.Result, "completed", StringComparison.OrdinalIgnoreCase) && phase2.Scenarios?.Length >= 2,
                $"Result={phase2.Result}; scenarios={phase2.Scenarios?.Length ?? 0}."),
            CreateInputCheck("Phase 3 evidence ingestion", phase3SummaryPath,
                string.Equals(phase3.Result, "completed", StringComparison.OrdinalIgnoreCase) && phase3.OriginalArtifactsUnchanged &&
                string.Equals(Path.GetFullPath(phase3.Phase1InputRun), phase1Directory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFullPath(phase3.Phase2InputRun), phase2Directory, StringComparison.OrdinalIgnoreCase),
                $"Result={phase3.Result}; records={phase3.RecordCount}; originals unchanged={phase3.OriginalArtifactsUnchanged}."),
            CreateInputCheck("Phase 3 evidence index", evidenceIndexPath,
                phase3.RecordCount > 0,
                $"Indexed records={phase3.RecordCount}."),
            CreateInputCheck("Phase 4 attribution", phase4SummaryPath,
                string.Equals(phase4.Result, "completed", StringComparison.OrdinalIgnoreCase) && attributionReport.Accepted &&
                string.Equals(Path.GetFullPath(phase4.Phase3InputRun), phase3Directory, StringComparison.OrdinalIgnoreCase),
                $"Result={phase4.Result}; accuracy={phase4.KnownGroundTruthAccuracyPercent:F2}%."),
            CreateInputCheck("Phase 4 attribution report", attributionReportPath,
                attributionReport.Accepted && attributionReport.DecisionCount == phase4.DecisionCount,
                $"Accepted={attributionReport.Accepted}; decisions={attributionReport.DecisionCount}."),
            CreateInputCheck("Phase 5 adaptive collection", phase5SummaryPath,
                string.Equals(phase5.Result, "completed", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFullPath(phase5.Phase4InputRun), phase4Directory, StringComparison.OrdinalIgnoreCase),
                $"Result={phase5.Result}; rule applications={phase5.ApprovedRuleApplicationCount}."),
            CreateInputCheck("Phase 5 integrity and custody report", adaptiveReportPath,
                adaptiveReport.Accepted && adaptiveReport.CustodyEntryCount == adaptiveReport.CompleteCustodyEntryCount &&
                adaptiveReport.CustodyEntryCount == adaptiveReport.VerifiedHashCount,
                $"Accepted={adaptiveReport.Accepted}; custody={adaptiveReport.CompleteCustodyEntryCount}/{adaptiveReport.CustodyEntryCount}; hashes={adaptiveReport.VerifiedHashCount}."),
        ];
    }

    private static EvaluationInputCheck CreateInputCheck(string name, string path, bool passed, string detail) =>
        new(name, Path.GetFullPath(path), ComputeSha256(path), passed, detail);

    private static EvaluationScenarioDefinition GetEvaluationScenario(IReadOnlyList<EvaluationScenarioDefinition> scenarios, string type) =>
        scenarios.Single(scenario => string.Equals(scenario.Type, type, StringComparison.OrdinalIgnoreCase));

    private static EvaluationScenarioResult EvaluateNormalSliceOperation(
        EvaluationScenarioDefinition scenario,
        Phase2TrafficSummary phase2,
        EvidenceIndex index,
        SliceAttributionReport attributionReport)
    {
        var scenarioIds = scenario.ScenarioIds!;
        var expected = scenarioIds.Length * scenario.MinimumEvidenceItems;
        var presentTrafficScenarios = scenarioIds.Count(id => phase2.Scenarios!.Contains(id, StringComparer.OrdinalIgnoreCase));
        var observed = 0;
        var allAttributed = true;
        foreach (var id in scenarioIds)
        {
            var records = index.Records.Where(record => string.Equals(record.ScenarioId, id, StringComparison.OrdinalIgnoreCase)).ToArray();
            observed += Math.Min(records.Length, scenario.MinimumEvidenceItems);
            var recordIds = records.Select(record => record.RecordId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var decisions = attributionReport.Decisions.Where(decision => recordIds.Contains(decision.RecordId)).ToArray();
            allAttributed &= records.Length >= scenario.MinimumEvidenceItems && decisions.Length == records.Length &&
                decisions.All(decision => string.Equals(decision.DecisionStatus, "attributed", StringComparison.OrdinalIgnoreCase));
        }

        var passed = presentTrafficScenarios == scenarioIds.Length && observed == expected && allAttributed;
        return new EvaluationScenarioResult(
            scenario.Id,
            scenario.Type,
            scenario.Purpose,
            passed,
            expected,
            observed,
            $"{presentTrafficScenarios}/{scenarioIds.Length} labelled traffic scenarios completed; all selected records were attributed to their declared slice.",
            ["phase2-summary.json", "evidence-index.json", "slice-attribution-report.json"]);
    }

    private static EvaluationScenarioResult EvaluateAllocationAnomalyReplay(
        EvaluationScenarioDefinition scenario,
        SliceAttributionCatalog attributionCatalog)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var replayRecord = new NormalisedEvidenceRecord(
            RecordId: scenario.Id,
            IngestedAtUtc: timestamp,
            EvidenceTimestampUtc: timestamp,
            SourceReference: ResolveSolutionPath("config/phase6/evaluation-scenarios.json"),
            SourceKind: "allocation-anomaly-replay",
            SourceComponent: "Phase 6 controlled evidence replay",
            NetworkFunction: "laboratory-orchestrator",
            ScenarioId: scenario.Id,
            PreliminarySliceContext: scenario.SyntheticPreliminarySliceContext,
            ArtifactSizeBytes: 0,
            OriginalArtifactUnchanged: true,
            Observation: "Synthetic, data-only allocation-context anomaly used for evaluation. No radio transmission, subscriber action or slice change occurs.");
        var decision = AttributeRecord(replayRecord, attributionCatalog, timestamp);
        var passed = string.Equals(decision.DecisionStatus, scenario.ExpectedDecisionStatus, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(decision.RuleId, scenario.ExpectedRuleId, StringComparison.OrdinalIgnoreCase) &&
            decision.AssignedSliceContext is null;
        return new EvaluationScenarioResult(
            scenario.Id,
            scenario.Type,
            scenario.Purpose,
            passed,
            1,
            1,
            $"Replay decision={decision.DecisionStatus}; rule={decision.RuleId}; assigned slice={(decision.AssignedSliceContext?.SliceId ?? "none")}.",
            ["slice-attribution-rules.json", "evaluation-scenarios.json"]);
    }

    private static EvaluationScenarioResult EvaluateSharedFunctionIsolation(
        EvaluationScenarioDefinition scenario,
        SliceAttributionReport attributionReport)
    {
        var decisions = attributionReport.Decisions
            .Where(decision => scenario.ScenarioIds!.Contains(decision.ScenarioId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var observed = decisions.Count(decision =>
            string.Equals(decision.DecisionStatus, scenario.ExpectedDecisionStatus, StringComparison.OrdinalIgnoreCase) &&
            decision.AssignedSliceContext is null);
        var passed = decisions.Length >= scenario.MinimumEvidenceItems && observed == decisions.Length;
        return new EvaluationScenarioResult(
            scenario.Id,
            scenario.Type,
            scenario.Purpose,
            passed,
            scenario.MinimumEvidenceItems,
            observed,
            $"{observed}/{decisions.Length} shared records remained {scenario.ExpectedDecisionStatus} with no individual slice assignment.",
            ["slice-attribution-report.json"]);
    }

    private static EvaluationScenarioResult EvaluateCrossLayerEvidence(EvaluationScenarioDefinition scenario, EvidenceIndex index)
    {
        var requiredSourceKinds = scenario.RequiredSourceKinds ?? throw new InvalidOperationException($"Scenario '{scenario.Id}' has no required source kinds.");
        var sourceKinds = index.Records
            .Where(record => scenario.ScenarioIds!.Contains(record.ScenarioId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .Select(record => record.SourceKind)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var observed = requiredSourceKinds.Count(sourceKind => sourceKinds.Contains(sourceKind));
        var passed = observed == requiredSourceKinds.Length;
        return new EvaluationScenarioResult(
            scenario.Id,
            scenario.Type,
            scenario.Purpose,
            passed,
            requiredSourceKinds.Length,
            observed,
            $"Required cross-layer source kinds present: {string.Join(", ", requiredSourceKinds.Where(sourceKinds.Contains))}.",
            ["evidence-index.json"]);
    }

    private static EvaluationScenarioResult EvaluateLoadTriggeredAdaptation(EvaluationScenarioDefinition scenario, AdaptiveCollectionReport adaptiveReport)
    {
        var applications = adaptiveReport.RuleApplications
            .Where(application => string.Equals(application.RuleId, scenario.ExpectedRuleId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var expectedSlices = scenario.ExpectedSliceIds!;
        var matchingSlices = expectedSlices.Count(sliceId => applications.Any(application =>
            string.Equals(application.SliceId, sliceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(application.CollectionPriority, scenario.ExpectedCollectionPriority, StringComparison.OrdinalIgnoreCase)));
        var passed = applications.Length >= scenario.MinimumEvidenceItems && matchingSlices == expectedSlices.Length;
        return new EvaluationScenarioResult(
            scenario.Id,
            scenario.Type,
            scenario.Purpose,
            passed,
            expectedSlices.Length,
            matchingSlices,
            $"{applications.Length} approved rule applications recorded; {matchingSlices}/{expectedSlices.Length} expected slice contexts received {scenario.ExpectedCollectionPriority} collection priority.",
            ["adaptive-collection-report.json", "adaptation-timing-log.json"]);
    }

    private static IReadOnlyList<EvaluationMetric> BuildEvaluationMetrics(
        EvaluationScenarioResult normalResult,
        SliceAttributionReport attributionReport,
        AdaptiveCollectionReport adaptiveReport,
        OperationalOverheadObservation overhead)
    {
        var attributionAccuracy = Percentage(attributionReport.KnownGroundTruthCorrectCount, attributionReport.KnownGroundTruthDecisionCount);
        var collectionCompleteness = Percentage(normalResult.ObservedEvidenceItems, normalResult.ExpectedEvidenceItems);
        var integrityVerification = Percentage(adaptiveReport.VerifiedHashCount, adaptiveReport.CustodyEntryCount);
        var custodyCompleteness = Percentage(adaptiveReport.CompleteCustodyEntryCount, adaptiveReport.CustodyEntryCount);
        return
        [
            new EvaluationMetric("Attribution accuracy", "Correctly assigned labelled records divided by labelled ground-truth records.", attributionReport.KnownGroundTruthCorrectCount, attributionReport.KnownGroundTruthDecisionCount, attributionAccuracy, "percent", attributionReport.KnownGroundTruthDecisionCount > 0 && attributionAccuracy == 100, "slice-attribution-report.json"),
            new EvaluationMetric("Collection completeness", "Collected normal-operation evidence items divided by the predefined items required for the two labelled traffic scenarios.", normalResult.ObservedEvidenceItems, normalResult.ExpectedEvidenceItems, collectionCompleteness, "percent", normalResult.Passed && collectionCompleteness == 100, "evidence-index.json and scenario ledger"),
            new EvaluationMetric("Integrity verification", "Recomputed SHA-256 values that match the stored value divided by hashed custody entries.", adaptiveReport.VerifiedHashCount, adaptiveReport.CustodyEntryCount, integrityVerification, "percent", adaptiveReport.CustodyEntryCount > 0 && integrityVerification == 100, "hash-verification-report.json"),
            new EvaluationMetric("Adaptation latency", "Maximum elapsed time from an approved observed traffic event to the matching collection-priority update.", adaptiveReport.TriggerToRuleUpdateLatencyMilliseconds, 1, adaptiveReport.TriggerToRuleUpdateLatencyMilliseconds, "milliseconds", adaptiveReport.ApprovedRuleApplicationCount > 0, "adaptation-timing-log.json"),
            new EvaluationMetric("Custody completeness", "Custody entries containing all required source, collector, action, timestamp, hash and storage fields divided by all custody entries.", adaptiveReport.CompleteCustodyEntryCount, adaptiveReport.CustodyEntryCount, custodyCompleteness, "percent", adaptiveReport.CustodyEntryCount > 0 && custodyCompleteness == 100, "custody-ledger.json"),
            new EvaluationMetric("Operational overhead", "Change in total Docker CPU percentage across the 13 controlled laboratory containers between the before and after traffic snapshots. The separate overhead observation reports memory, network, block-write and retained-evidence figures; it does not claim an isolated production cost for the forensic logic.", overhead.CpuPercentDelta, 1, overhead.CpuPercentDelta, "CPU percentage points", overhead.BeforeContainerCount > 0 && overhead.AfterContainerCount > 0, "docker-stats-before.txt, docker-stats-after.txt and operational-overhead.json")
        ];
    }

    private static double Percentage(long numerator, long denominator) => denominator == 0 ? 0 : Math.Round(numerator * 100.0 / denominator, 2);

    private static OperationalOverheadObservation CreateOperationalOverheadObservation(
        string beforeStatsPath,
        string afterStatsPath,
        long evidenceRepositoryBytes,
        long evaluationExecutionMilliseconds)
    {
        var before = ParseDockerStatsSnapshot(beforeStatsPath);
        var after = ParseDockerStatsSnapshot(afterStatsPath);
        return new OperationalOverheadObservation(
            BeforeContainerCount: before.ContainerCount,
            AfterContainerCount: after.ContainerCount,
            CpuPercentBefore: before.TotalCpuPercent,
            CpuPercentAfter: after.TotalCpuPercent,
            CpuPercentDelta: Math.Round(after.TotalCpuPercent - before.TotalCpuPercent, 2),
            MemoryMiBBefore: before.TotalMemoryMiB,
            MemoryMiBAfter: after.TotalMemoryMiB,
            MemoryMiBDelta: Math.Round(after.TotalMemoryMiB - before.TotalMemoryMiB, 2),
            NetworkBytesBefore: before.TotalNetworkBytes,
            NetworkBytesAfter: after.TotalNetworkBytes,
            NetworkBytesDelta: after.TotalNetworkBytes - before.TotalNetworkBytes,
            BlockWriteBytesBefore: before.TotalBlockWriteBytes,
            BlockWriteBytesAfter: after.TotalBlockWriteBytes,
            BlockWriteBytesDelta: after.TotalBlockWriteBytes - before.TotalBlockWriteBytes,
            EvidenceRepositoryBytes: evidenceRepositoryBytes,
            EvaluationExecutionMilliseconds: evaluationExecutionMilliseconds,
            Interpretation: "The before and after Docker snapshots show the total controlled-laboratory activity during the traffic run. They support an overhead observation but do not isolate a causal production cost of the forensic logic.");
    }

    private static DockerStatsSnapshot ParseDockerStatsSnapshot(string path)
    {
        var entries = new List<DockerStatsEntry>();
        foreach (var line in File.ReadLines(path).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var columns = Regex.Split(line.Trim(), @"\s{2,}");
            if (columns.Length < 8)
            {
                throw new FormatException($"Unexpected Docker stats row in {path}: {line}");
            }

            var memoryParts = columns[3].Split('/', StringSplitOptions.TrimEntries);
            var networkParts = columns[5].Split('/', StringSplitOptions.TrimEntries);
            var blockParts = columns[6].Split('/', StringSplitOptions.TrimEntries);
            if (memoryParts.Length != 2 || networkParts.Length != 2 || blockParts.Length != 2)
            {
                throw new FormatException($"Unexpected Docker stats measurement in {path}: {line}");
            }

            entries.Add(new DockerStatsEntry(
                columns[1],
                ParseDecimal(columns[2].Trim().TrimEnd('%')),
                ParseDockerBytes(memoryParts[0]),
                ParseDockerBytes(networkParts[0]) + ParseDockerBytes(networkParts[1]),
                ParseDockerBytes(blockParts[1])));
        }

        if (entries.Count == 0) throw new FormatException($"No Docker statistics were found in {path}.");
        return new DockerStatsSnapshot(
            Path.GetFullPath(path),
            entries.Count,
            Math.Round(entries.Sum(entry => entry.CpuPercent), 2),
            Math.Round(entries.Sum(entry => entry.MemoryBytes) / 1024.0 / 1024.0, 2),
            entries.Sum(entry => entry.NetworkBytes),
            entries.Sum(entry => entry.BlockWriteBytes));
    }

    private static double ParseDecimal(string text) =>
        double.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static long ParseDockerBytes(string value)
    {
        var match = Regex.Match(value.Trim(), @"^(?<number>[0-9]+(?:[\.,][0-9]+)?)\s*(?<unit>B|kB|KB|MB|GB|TB|KiB|MiB|GiB|TiB)$", RegexOptions.IgnoreCase);
        if (!match.Success) throw new FormatException($"Unsupported Docker size '{value}'.");
        var number = ParseDecimal(match.Groups["number"].Value);
        var multiplier = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "B" => 1d,
            "KB" => 1_000d,
            "MB" => 1_000_000d,
            "GB" => 1_000_000_000d,
            "TB" => 1_000_000_000_000d,
            "KIB" => 1_024d,
            "MIB" => 1_048_576d,
            "GIB" => 1_073_741_824d,
            "TIB" => 1_099_511_627_776d,
            _ => throw new FormatException($"Unsupported Docker size unit in '{value}'.")
        };
        return checked((long)Math.Round(number * multiplier, MidpointRounding.AwayFromZero));
    }

    private static long CalculateDirectoryBytes(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);

    private static IReadOnlyList<string> ValidateEvaluationCatalog(EvaluationCatalog? catalog)
    {
        var errors = new List<string>();
        if (catalog is null)
        {
            errors.Add("Evaluation configuration is empty.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(catalog.Phase)) errors.Add("phase is required.");
        if (string.IsNullOrWhiteSpace(catalog.Purpose)) errors.Add("purpose is required.");
        if (catalog.Scenarios is null || catalog.Scenarios.Length != 5) errors.Add("Exactly five controlled evaluation scenarios are required.");
        if (catalog.Limitations is null || catalog.Limitations.Length == 0) errors.Add("At least one evaluation limitation is required.");
        var requiredTypes = new[] { "normal-slice-operation", "allocation-anomaly-replay", "shared-function-isolation", "cross-layer-evidence", "load-triggered-adaptation" };
        var scenarioTypes = catalog.Scenarios?.Select(scenario => scenario.Type).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
        foreach (var type in requiredTypes)
        {
            if (!scenarioTypes.Contains(type)) errors.Add($"Missing required evaluation scenario type '{type}'.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scenario in catalog.Scenarios ?? [])
        {
            if (string.IsNullOrWhiteSpace(scenario.Id)) errors.Add("Each evaluation scenario requires an id.");
            else if (!ids.Add(scenario.Id)) errors.Add($"Duplicate evaluation scenario id: {scenario.Id}.");
            if (string.IsNullOrWhiteSpace(scenario.Type)) errors.Add($"Scenario '{scenario.Id}' requires a type.");
            if (string.IsNullOrWhiteSpace(scenario.Purpose)) errors.Add($"Scenario '{scenario.Id}' requires a purpose.");
            if (scenario.MinimumEvidenceItems <= 0) errors.Add($"Scenario '{scenario.Id}' requires a positive minimumEvidenceItems value.");
            if (scenario.Type is "normal-slice-operation" or "shared-function-isolation" or "cross-layer-evidence")
            {
                if (scenario.ScenarioIds is null || scenario.ScenarioIds.Length == 0) errors.Add($"Scenario '{scenario.Id}' requires scenarioIds.");
            }
            if (scenario.Type == "allocation-anomaly-replay")
            {
                if (!HasCompleteSliceContext(scenario.SyntheticPreliminarySliceContext)) errors.Add($"Scenario '{scenario.Id}' requires a complete syntheticPreliminarySliceContext.");
                if (string.IsNullOrWhiteSpace(scenario.ExpectedDecisionStatus) || string.IsNullOrWhiteSpace(scenario.ExpectedRuleId)) errors.Add($"Scenario '{scenario.Id}' requires expected decision status and rule id.");
            }
            if (scenario.Type == "shared-function-isolation" && string.IsNullOrWhiteSpace(scenario.ExpectedDecisionStatus)) errors.Add($"Scenario '{scenario.Id}' requires expectedDecisionStatus.");
            if (scenario.Type == "cross-layer-evidence" && (scenario.RequiredSourceKinds is null || scenario.RequiredSourceKinds.Length == 0)) errors.Add($"Scenario '{scenario.Id}' requires requiredSourceKinds.");
            if (scenario.Type == "load-triggered-adaptation")
            {
                if (string.IsNullOrWhiteSpace(scenario.ExpectedRuleId) || string.IsNullOrWhiteSpace(scenario.ExpectedCollectionPriority)) errors.Add($"Scenario '{scenario.Id}' requires an expected rule id and collection priority.");
                if (scenario.ExpectedSliceIds is null || scenario.ExpectedSliceIds.Length == 0) errors.Add($"Scenario '{scenario.Id}' requires expectedSliceIds.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateAdaptiveCollectionCatalog(AdaptiveCollectionCatalog? catalog)
    {
        var errors = new List<string>();
        if (catalog is null)
        {
            errors.Add("Adaptive-collection configuration is empty.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(catalog.Phase)) errors.Add("phase is required.");
        if (string.IsNullOrWhiteSpace(catalog.Purpose)) errors.Add("purpose is required.");
        if (string.IsNullOrWhiteSpace(catalog.Collector)) errors.Add("collector is required.");
        if (!string.Equals(catalog.IntegrityAlgorithm, "SHA-256", StringComparison.OrdinalIgnoreCase)) errors.Add("integrityAlgorithm must be SHA-256.");
        var requiredFields = new[] { "source", "collector", "action", "timestamp", "hash", "storageLocation" };
        foreach (var field in requiredFields)
        {
            if (catalog.RequiredCustodyFields is null || !catalog.RequiredCustodyFields.Contains(field, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"requiredCustodyFields must include '{field}'.");
            }
        }

        if (catalog.Rules is null || catalog.Rules.Length == 0)
        {
            errors.Add("At least one adaptive collection rule is required.");
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var priorities = new HashSet<int>();
        var approvedRuleCount = 0;
        foreach (var rule in catalog.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id)) errors.Add("Each adaptive rule requires an id.");
            else if (!ids.Add(rule.Id)) errors.Add($"Duplicate adaptive rule id: {rule.Id}.");
            if (!priorities.Add(rule.Priority)) errors.Add($"Duplicate adaptive rule priority: {rule.Priority}.");
            if (string.IsNullOrWhiteSpace(rule.Rationale)) errors.Add($"Rule '{rule.Id}' requires a rationale.");
            if (rule.Trigger is null) errors.Add($"Rule '{rule.Id}' requires a trigger.");
            else
            {
                if (string.IsNullOrWhiteSpace(rule.Trigger.EventType)) errors.Add($"Rule '{rule.Id}' trigger requires an eventType.");
                if (rule.Trigger.SourceKinds is null || rule.Trigger.SourceKinds.Length == 0) errors.Add($"Rule '{rule.Id}' trigger requires sourceKinds.");
                if (rule.Trigger.MinimumAttributedRecordCount <= 0) errors.Add($"Rule '{rule.Id}' trigger requires a positive minimumAttributedRecordCount.");
            }
            if (rule.Action is null) errors.Add($"Rule '{rule.Id}' requires an action.");
            else
            {
                if (rule.Action.CollectionPriority is not ("baseline" or "elevated" or "high")) errors.Add($"Rule '{rule.Id}' has unsupported collectionPriority '{rule.Action.CollectionPriority}'.");
                if (rule.Action.SourceKinds is null || rule.Action.SourceKinds.Length == 0) errors.Add($"Rule '{rule.Id}' action requires sourceKinds.");
                if (string.IsNullOrWhiteSpace(rule.Action.Description)) errors.Add($"Rule '{rule.Id}' action requires a description.");
            }
            if (rule.Approved && rule.Enabled) approvedRuleCount++;
        }

        if (approvedRuleCount == 0) errors.Add("At least one enabled, approved adaptive collection rule is required.");
        return errors;
    }

    private static IReadOnlyList<AdaptiveRuleApplication> ApplyApprovedCollectionRules(
        IReadOnlyList<AttributionDecision> decisions,
        AdaptiveCollectionCatalog catalog,
        DateTimeOffset observedAt)
    {
        var applications = new List<AdaptiveRuleApplication>();
        foreach (var rule in catalog.Rules!
                     .Where(candidate => candidate.Approved && candidate.Enabled)
                     .OrderBy(candidate => candidate.Priority))
        {
            var matching = decisions.Where(decision =>
                    string.Equals(decision.DecisionStatus, "attributed", StringComparison.OrdinalIgnoreCase) &&
                    decision.AssignedSliceContext is not null &&
                    rule.Trigger!.SourceKinds!.Contains(decision.SourceKind, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            var groups = rule.Trigger.RequireAssignedSlice
                ? matching.GroupBy(decision => decision.AssignedSliceContext!.SliceId, StringComparer.OrdinalIgnoreCase)
                : matching.GroupBy(_ => "all-slices", StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups.Where(candidate => candidate.Count() >= rule.Trigger.MinimumAttributedRecordCount))
            {
                var updatedAt = DateTimeOffset.UtcNow;
                applications.Add(new AdaptiveRuleApplication(
                    RuleId: rule.Id,
                    EventType: rule.Trigger.EventType,
                    SliceId: rule.Trigger.RequireAssignedSlice ? group.Key : null,
                    TriggerObservedAtUtc: observedAt,
                    RuleUpdatedAtUtc: updatedAt,
                    AdaptationLatencyMilliseconds: Math.Max(0, (long)(updatedAt - observedAt).TotalMilliseconds),
                    EvidenceRecordIds: group.Select(decision => decision.RecordId).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    CollectionPriority: rule.Action!.CollectionPriority,
                    PrioritisedSourceKinds: rule.Action.SourceKinds!,
                    Rationale: rule.Rationale));
            }
        }

        return applications;
    }

    private static IReadOnlyList<CustodyEntry> BuildCustodyLedger(
        IReadOnlyList<AttributionDecision> decisions,
        AdaptiveCollectionCatalog catalog,
        string storageLocation)
    {
        var entries = new List<CustodyEntry>();
        string? previousEntrySha256 = null;
        var sequence = 0;
        foreach (var decision in decisions.OrderBy(decision => decision.RecordId, StringComparer.Ordinal))
        {
            sequence++;
            var sourcePath = decision.SourceReference;
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Evidence source referenced by '{decision.RecordId}' is unavailable.", sourcePath);
            }

            var before = new FileInfo(sourcePath);
            var sizeBefore = before.Length;
            var writeBefore = before.LastWriteTimeUtc;
            var sha256 = ComputeSha256(sourcePath);
            var after = new FileInfo(sourcePath);
            var unchangedDuringHash = sizeBefore == after.Length && writeBefore == after.LastWriteTimeUtc;
            if (!unchangedDuringHash)
            {
                throw new InvalidOperationException($"Evidence source changed while SHA-256 was calculated: {sourcePath}");
            }

            var timestamp = DateTimeOffset.UtcNow;
            var action = "Acquired read-only reference, calculated SHA-256 and registered custody metadata.";
            var entryMaterial = string.Join("|", sequence, decision.RecordId, sourcePath, catalog.Collector, action, timestamp.ToString("O"), sha256, storageLocation, previousEntrySha256 ?? string.Empty);
            var entrySha256 = ComputeTextSha256(entryMaterial);
            entries.Add(new CustodyEntry(
                Sequence: sequence,
                RecordId: decision.RecordId,
                SourceReference: sourcePath,
                Collector: catalog.Collector,
                Action: action,
                TimestampUtc: timestamp,
                Sha256: sha256,
                ArtifactSizeBytes: sizeBefore,
                StorageLocation: storageLocation,
                PreviousEntrySha256: previousEntrySha256,
                EntrySha256: entrySha256,
                SourceUnchangedDuringHash: unchangedDuringHash));
            previousEntrySha256 = entrySha256;
        }

        return entries;
    }

    private static IReadOnlyList<HashVerificationResult> VerifyCustodyLedger(IReadOnlyList<CustodyEntry> custodyLedger)
    {
        return custodyLedger.Select(entry =>
        {
            var sourceExists = File.Exists(entry.SourceReference);
            var actualSha256 = sourceExists ? ComputeSha256(entry.SourceReference) : string.Empty;
            return new HashVerificationResult(
                RecordId: entry.RecordId,
                SourceReference: entry.SourceReference,
                ExpectedSha256: entry.Sha256,
                ActualSha256: actualSha256,
                HashMatches: sourceExists && string.Equals(entry.Sha256, actualSha256, StringComparison.OrdinalIgnoreCase),
                VerifiedAtUtc: DateTimeOffset.UtcNow,
                ArtifactSizeBytes: sourceExists ? new FileInfo(entry.SourceReference).Length : 0,
                SourceExists: sourceExists);
        }).ToArray();
    }

    private static bool HasCompleteCustodyMetadata(CustodyEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.SourceReference) &&
        !string.IsNullOrWhiteSpace(entry.Collector) &&
        !string.IsNullOrWhiteSpace(entry.Action) &&
        entry.TimestampUtc != default &&
        !string.IsNullOrWhiteSpace(entry.Sha256) &&
        !string.IsNullOrWhiteSpace(entry.StorageLocation) &&
        !string.IsNullOrWhiteSpace(entry.EntrySha256);

    private static IReadOnlyList<string> ValidateSliceAttributionCatalog(SliceAttributionCatalog? catalog)
    {
        var errors = new List<string>();
        if (catalog is null)
        {
            errors.Add("Slice-attribution configuration is empty.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(catalog.Phase)) errors.Add("phase is required.");
        if (string.IsNullOrWhiteSpace(catalog.Purpose)) errors.Add("purpose is required.");
        if (catalog.Slices is null || catalog.Slices.Length < 2) errors.Add("At least two configured slice contexts are required.");
        if (catalog.Rules is null || catalog.Rules.Length == 0) errors.Add("At least one attribution rule is required.");

        var sliceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snssai = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slice in catalog.Slices ?? [])
        {
            if (string.IsNullOrWhiteSpace(slice.Id)) errors.Add("Each configured slice requires an id.");
            else if (!sliceIds.Add(slice.Id)) errors.Add($"Duplicate configured slice id: {slice.Id}.");
            if (string.IsNullOrWhiteSpace(slice.Name)) errors.Add($"Slice '{slice.Id}' requires a name.");
            if (string.IsNullOrWhiteSpace(slice.SliceType)) errors.Add($"Slice '{slice.Id}' requires a sliceType.");
            if (slice.Sst is < 0 or > 255) errors.Add($"Slice '{slice.Id}' has an invalid SST.");
            if (!Regex.IsMatch(slice.Sd ?? string.Empty, "^[0-9A-Fa-f]{6}$")) errors.Add($"Slice '{slice.Id}' requires a six-digit hexadecimal SD.");
            if (string.IsNullOrWhiteSpace(slice.Dnn)) errors.Add($"Slice '{slice.Id}' requires a DNN.");
            if (!snssai.Add($"{slice.Sst}:{slice.Sd}:{slice.Dnn}")) errors.Add($"Duplicate configured S-NSSAI/DNN context for slice '{slice.Id}'.");
        }

        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var priorities = new HashSet<int>();
        foreach (var rule in catalog.Rules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.Id)) errors.Add("Each attribution rule requires an id.");
            else if (!ruleIds.Add(rule.Id)) errors.Add($"Duplicate attribution rule id: {rule.Id}.");
            if (!priorities.Add(rule.Priority)) errors.Add($"Duplicate attribution rule priority: {rule.Priority}.");
            if (string.IsNullOrWhiteSpace(rule.Rationale)) errors.Add($"Rule '{rule.Id}' requires a rationale.");
            if (rule.Decision is not ("attributed" or "ambiguous" or "unattributed")) errors.Add($"Rule '{rule.Id}' has unsupported decision '{rule.Decision}'.");
            if (string.Equals(rule.Decision, "attributed", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(rule.SliceId) && !rule.UsePreliminarySliceContext)
            {
                errors.Add($"Attributed rule '{rule.Id}' requires sliceId or usePreliminarySliceContext.");
            }
            if (!string.IsNullOrWhiteSpace(rule.SliceId) && !sliceIds.Contains(rule.SliceId)) errors.Add($"Rule '{rule.Id}' references unknown slice '{rule.SliceId}'.");
            if (string.Equals(rule.Decision, "ambiguous", StringComparison.OrdinalIgnoreCase) && (rule.CandidateSliceIds is null || rule.CandidateSliceIds.Length < 2))
            {
                errors.Add($"Ambiguous rule '{rule.Id}' requires at least two candidateSliceIds.");
            }
            foreach (var candidate in rule.CandidateSliceIds ?? [])
            {
                if (!sliceIds.Contains(candidate)) errors.Add($"Rule '{rule.Id}' references unknown candidate slice '{candidate}'.");
            }
        }

        return errors;
    }

    private static AttributionDecision AttributeRecord(NormalisedEvidenceRecord record, SliceAttributionCatalog catalog, DateTimeOffset attributedAt)
    {
        var rule = catalog.Rules!
            .OrderBy(candidate => candidate.Priority)
            .FirstOrDefault(candidate => RuleMatches(candidate.When, record));

        if (rule is null)
        {
            return BuildAttributionDecision(record, attributedAt, "unattributed", "P4-NO-MATCH", "No configured rule matched the record; no slice is assigned.", null, []);
        }

        if (string.Equals(rule.Decision, "attributed", StringComparison.OrdinalIgnoreCase))
        {
            var assigned = ResolveAssignedSlice(rule, record, catalog.Slices!);
            if (assigned is null)
            {
                var status = HasCompleteSliceContext(record.PreliminarySliceContext) ? "ambiguous" : "unattributed";
                var safeguardId = HasCompleteSliceContext(record.PreliminarySliceContext) ? "P4-UNRECOGNISED-SNSSAI" : "P4-MISSING-SLICE-CONTEXT";
                var safeguardReason = HasCompleteSliceContext(record.PreliminarySliceContext)
                    ? "The supplied S-NSSAI/DNN context is not one of the configured laboratory slices; no assignment is made."
                    : "The record has no complete S-NSSAI/DNN context for the selected rule; no assignment is made.";
                return BuildAttributionDecision(record, attributedAt, status, safeguardId, safeguardReason, null, []);
            }

            if (HasCompleteSliceContext(record.PreliminarySliceContext) && !ContextsMatch(record.PreliminarySliceContext, assigned))
            {
                return BuildAttributionDecision(record, attributedAt, "ambiguous", "P4-CONTEXT-CONFLICT", $"Rule '{rule.Id}' selected a slice that conflicts with the record's supplied S-NSSAI/DNN context; no assignment is made.", null, [assigned]);
            }

            return BuildAttributionDecision(record, attributedAt, "attributed", rule.Id, rule.Rationale, assigned, []);
        }

        if (string.Equals(rule.Decision, "ambiguous", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = (rule.CandidateSliceIds ?? [])
                .Select(candidateId => catalog.Slices!.Single(slice => string.Equals(slice.Id, candidateId, StringComparison.OrdinalIgnoreCase)))
                .Select(ToSliceContext)
                .ToArray();
            return BuildAttributionDecision(record, attributedAt, "ambiguous", rule.Id, rule.Rationale, null, candidates);
        }

        return BuildAttributionDecision(record, attributedAt, "unattributed", rule.Id, rule.Rationale, null, []);
    }

    private static bool RuleMatches(AttributionRuleCondition? condition, NormalisedEvidenceRecord record)
    {
        if (condition is null) return true;
        if (!string.IsNullOrWhiteSpace(condition.ScenarioId) && !string.Equals(condition.ScenarioId, record.ScenarioId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(condition.NetworkFunction) && !string.Equals(condition.NetworkFunction, record.NetworkFunction, StringComparison.OrdinalIgnoreCase)) return false;
        if (condition.RequirePreliminarySliceContext is true && !HasCompleteSliceContext(record.PreliminarySliceContext)) return false;
        if (condition.RequirePreliminarySliceContext is false && HasCompleteSliceContext(record.PreliminarySliceContext)) return false;
        return true;
    }

    private static AttributionSliceContext? ResolveAssignedSlice(AttributionRule rule, NormalisedEvidenceRecord record, AttributionSlice[] slices)
    {
        if (rule.UsePreliminarySliceContext)
        {
            var matchingSlice = slices.SingleOrDefault(slice => ContextsMatch(record.PreliminarySliceContext, ToSliceContext(slice)));
            return matchingSlice is null ? null : ToSliceContext(matchingSlice);
        }

        var directSlice = slices.SingleOrDefault(slice => string.Equals(slice.Id, rule.SliceId, StringComparison.OrdinalIgnoreCase));
        return directSlice is null ? null : ToSliceContext(directSlice);
    }

    private static AttributionDecision BuildAttributionDecision(NormalisedEvidenceRecord record, DateTimeOffset attributedAt, string decisionStatus, string ruleId, string rationale, AttributionSliceContext? assigned, IReadOnlyList<AttributionSliceContext> candidates) =>
        new(
            RecordId: record.RecordId,
            SourceReference: record.SourceReference,
            AttributedAtUtc: attributedAt,
            EvidenceTimestampUtc: record.EvidenceTimestampUtc,
            NetworkFunction: record.NetworkFunction,
            SourceKind: record.SourceKind,
            ScenarioId: record.ScenarioId,
            PreliminarySliceContext: record.PreliminarySliceContext,
            AssignedSliceContext: assigned,
            CandidateSliceContexts: candidates,
            DecisionStatus: decisionStatus,
            RuleId: ruleId,
            Rationale: rationale,
            DecisionInputs: BuildDecisionInputs(record));

    private static string[] BuildDecisionInputs(NormalisedEvidenceRecord record) =>
    [
        $"recordId={record.RecordId}",
        $"evidenceTimestampUtc={record.EvidenceTimestampUtc:O}",
        $"networkFunction={record.NetworkFunction}",
        $"scenarioId={record.ScenarioId ?? "not supplied"}",
        HasCompleteSliceContext(record.PreliminarySliceContext)
            ? $"preliminarySnssai=SST {record.PreliminarySliceContext!.Sst} / SD {record.PreliminarySliceContext.Sd} / DNN {record.PreliminarySliceContext.Dnn}"
            : "preliminarySnssai=not complete"
    ];

    private static AttributionSliceContext ToSliceContext(AttributionSlice slice) =>
        new(slice.Id, slice.Name, slice.SliceType, slice.Sst, slice.Sd, slice.Dnn);

    private static bool HasCompleteSliceContext(PreliminarySliceContext? context) =>
        context is not null && context.Sst.HasValue && !string.IsNullOrWhiteSpace(context.Sd) && !string.IsNullOrWhiteSpace(context.Dnn);

    private static bool ContextsMatch(PreliminarySliceContext? preliminary, AttributionSliceContext? assigned) =>
        HasCompleteSliceContext(preliminary) && assigned is not null &&
        preliminary!.Sst == assigned.Sst &&
        string.Equals(preliminary.Sd, assigned.Sd, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(preliminary.Dnn, assigned.Dnn, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ValidateEvidenceIngestionCatalog(EvidenceIngestionCatalog? catalog)
    {
        var errors = new List<string>();
        if (catalog is null)
        {
            errors.Add("Evidence-ingestion configuration is empty.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(catalog.Phase)) errors.Add("phase is required.");
        if (string.IsNullOrWhiteSpace(catalog.Purpose)) errors.Add("purpose is required.");
        if (catalog.MinimumRecordCount <= 0) errors.Add("minimumRecordCount must be positive.");
        if (catalog.Sources is null || catalog.Sources.Length == 0) errors.Add("At least one evidence source is required.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in catalog.Sources ?? [])
        {
            if (string.IsNullOrWhiteSpace(source.Id)) errors.Add("Each evidence source requires an id.");
            else if (!ids.Add(source.Id)) errors.Add($"Duplicate evidence source id: {source.Id}.");
            if (!string.Equals(source.InputRun, "phase1", StringComparison.OrdinalIgnoreCase) && !string.Equals(source.InputRun, "phase2", StringComparison.OrdinalIgnoreCase)) errors.Add($"Source '{source.Id}' must use phase1 or phase2 inputRun.");
            if (string.IsNullOrWhiteSpace(source.RelativePath)) errors.Add($"Source '{source.Id}' requires relativePath.");
            if (string.IsNullOrWhiteSpace(source.SourceKind)) errors.Add($"Source '{source.Id}' requires sourceKind.");
            if (string.IsNullOrWhiteSpace(source.SourceComponent)) errors.Add($"Source '{source.Id}' requires sourceComponent.");
            if (string.IsNullOrWhiteSpace(source.NetworkFunction)) errors.Add($"Source '{source.Id}' requires networkFunction.");
            if (source.Expansion is not ("single" or "container-health" or "scenario-ledger")) errors.Add($"Source '{source.Id}' has unsupported expansion '{source.Expansion}'.");
        }

        return errors;
    }

    private static string ResolveInputArtifact(string inputRoot, string relativePath)
    {
        var root = Path.GetFullPath(inputRoot);
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var relative = Path.GetRelativePath(root, path);
        if (relative.Equals("..", StringComparison.Ordinal) || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Configured artifact path escapes its evidence directory: {relativePath}");
        }
        return path;
    }

    private static IEnumerable<NormalisedEvidenceRecord> RegisterEvidenceSource(EvidenceSourceDefinition source, string artifactPath, DateTimeOffset ingestedAt)
    {
        return source.Expansion switch
        {
            "container-health" => RegisterContainerHealth(source, artifactPath, ingestedAt),
            "scenario-ledger" => RegisterScenarioLedger(source, artifactPath, ingestedAt),
            _ => [CreateEvidenceRecord(source, artifactPath, ingestedAt, source.Id, source.NetworkFunction, source.ScenarioId, source.PreliminarySliceContext, null)]
        };
    }

    private static IEnumerable<NormalisedEvidenceRecord> RegisterContainerHealth(EvidenceSourceDefinition source, string artifactPath, DateTimeOffset ingestedAt)
    {
        using var document = ParseReadOnlyJson(artifactPath);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException($"Container-health evidence must be a JSON array: {artifactPath}");
        var records = new List<NormalisedEvidenceRecord>();
        var index = 0;
        foreach (var container in document.RootElement.EnumerateArray())
        {
            index++;
            var name = JsonString(container, "Container") ?? source.NetworkFunction;
            var status = JsonString(container, "Status") ?? "unknown";
            var health = JsonString(container, "Health") ?? "unknown";
            records.Add(CreateEvidenceRecord(source, artifactPath, ingestedAt, $"{source.Id}-{index:D2}", name, source.ScenarioId, source.PreliminarySliceContext, $"Container status={status}; health={health}."));
        }
        return records;
    }

    private static IEnumerable<NormalisedEvidenceRecord> RegisterScenarioLedger(EvidenceSourceDefinition source, string artifactPath, DateTimeOffset ingestedAt)
    {
        using var document = ParseReadOnlyJson(artifactPath);
        if (!document.RootElement.TryGetProperty("Scenarios", out var scenarios) || scenarios.ValueKind != JsonValueKind.Array) throw new InvalidOperationException($"Scenario ledger does not contain a Scenarios array: {artifactPath}");
        var records = new List<NormalisedEvidenceRecord>();
        foreach (var scenario in scenarios.EnumerateArray())
        {
            var scenarioId = JsonString(scenario, "Id") ?? throw new InvalidOperationException($"Scenario ledger entry has no Id: {artifactPath}");
            var context = new PreliminarySliceContext(
                Label: JsonString(scenario, "SliceName"),
                Sst: JsonInt(scenario, "Sst"),
                Sd: JsonString(scenario, "Sd"),
                Dnn: JsonString(scenario, "Dnn"));
            records.Add(CreateEvidenceRecord(source, artifactPath, ingestedAt, $"{source.Id}-{scenarioId}", source.NetworkFunction, scenarioId, context, "Ground-truth scenario definition registered; no attribution decision has been applied."));
        }
        return records;
    }

    private static JsonDocument ParseReadOnlyJson(string path)
    {
        var before = new FileInfo(path);
        var beforeLength = before.Length;
        var beforeWrite = before.LastWriteTimeUtc;
        var document = JsonDocument.Parse(File.ReadAllText(path));
        var after = new FileInfo(path);
        if (beforeLength != after.Length || beforeWrite != after.LastWriteTimeUtc)
        {
            document.Dispose();
            throw new InvalidOperationException($"Input artifact changed while being read: {path}");
        }
        return document;
    }

    private static NormalisedEvidenceRecord CreateEvidenceRecord(EvidenceSourceDefinition source, string artifactPath, DateTimeOffset ingestedAt, string recordId, string networkFunction, string? scenarioId, PreliminarySliceContext? preliminarySliceContext, string? observation)
    {
        var artifact = new FileInfo(artifactPath);
        return new NormalisedEvidenceRecord(
            RecordId: recordId,
            IngestedAtUtc: ingestedAt,
            EvidenceTimestampUtc: new DateTimeOffset(DateTime.SpecifyKind(artifact.LastWriteTimeUtc, DateTimeKind.Utc)),
            SourceReference: artifact.FullName,
            SourceKind: source.SourceKind,
            SourceComponent: source.SourceComponent,
            NetworkFunction: networkFunction,
            ScenarioId: scenarioId,
            PreliminarySliceContext: preliminarySliceContext,
            ArtifactSizeBytes: artifact.Length,
            OriginalArtifactUnchanged: true,
            Observation: observation ?? "Artifact registered without modifying the original source.");
    }

    private static string? JsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static int? JsonInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value) ? value : null;

    private static ReadinessCheck CheckVisualStudio()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new[]
        {
            Path.Combine(programFiles, "Microsoft Visual Studio", "18", "Community", "Common7", "IDE", "devenv.exe"),
            Path.Combine(programFilesX86, "Microsoft Visual Studio", "18", "Community", "Common7", "IDE", "devenv.exe")
        };
        var devenv = candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        return CheckFile("Visual Studio 2026", devenv);
    }

    private static ReadinessCheck CheckDotNet()
    {
        var result = RunProcess("dotnet.exe", "--version");
        return result.Found && result.ExitCode == 0
            ? new ReadinessCheck(".NET SDK", true, result.Output.Trim())
            : new ReadinessCheck(".NET SDK", false, result.Summary);
    }

    private static ReadinessCheck CheckProcess(string name, string executable, string arguments, Func<string, bool> isHealthy)
    {
        var result = RunProcess(executable, arguments);
        if (!result.Found)
        {
            return new ReadinessCheck(name, false, $"{executable} is not installed or not on PATH.");
        }

        var output = (result.Output + Environment.NewLine + result.Error).Replace("\0", string.Empty);
        var healthy = result.ExitCode == 0 && isHealthy(output);
        var summary = !healthy && output.Contains("unable to start", StringComparison.OrdinalIgnoreCase)
            ? "WSL2 reports that firmware virtualization is disabled."
            : string.IsNullOrWhiteSpace(output) ? $"Exit code {result.ExitCode}." : FirstLine(output);
        return new ReadinessCheck(name, healthy, summary);
    }

    private static ReadinessCheck CheckFile(string name, string path) =>
        File.Exists(path)
            ? new ReadinessCheck(name, true, Path.GetFullPath(path))
            : new ReadinessCheck(name, false, $"Missing: {Path.GetFullPath(path)}");

    private static CommandResult RunProcess(string executable, string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(executable, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(15_000);
            return new CommandResult(true, process.ExitCode, output, error);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new CommandResult(false, -1, string.Empty, string.Empty);
        }
    }

    private static ValidationResult LoadAndValidate(string configPath, out LabProfile? profile)
    {
        profile = null;
        var errors = new List<string>();
        if (!File.Exists(configPath))
        {
            errors.Add($"Configuration file not found: {Path.GetFullPath(configPath)}");
            return new ValidationResult(false, errors);
        }

        try
        {
            profile = JsonSerializer.Deserialize<LabProfile>(File.ReadAllText(configPath), JsonOptions);
        }
        catch (JsonException error)
        {
            errors.Add($"Invalid JSON: {error.Message}");
            return new ValidationResult(false, errors);
        }

        if (profile is null)
        {
            errors.Add("Configuration is empty.");
            return new ValidationResult(false, errors);
        }

        if (string.IsNullOrWhiteSpace(profile.EnvironmentName)) errors.Add("environmentName is required.");
        if (string.IsNullOrWhiteSpace(profile.UpstreamRevision)) errors.Add("upstreamRevision is required.");
        if (string.IsNullOrWhiteSpace(profile.OaiConfigurationRelativePath)) errors.Add("oaiConfigurationRelativePath is required.");
        if (profile.Slices is null || profile.Slices.Length < 2) errors.Add("At least two slice profiles are required.");
        if (profile.EvidenceSources is null || profile.EvidenceSources.Length == 0) errors.Add("At least one evidence source is required.");
        if (profile.RequiredServices is null || profile.RequiredServices.Length == 0) errors.Add("requiredServices is required.");

        if (profile.Slices is not null)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slice in profile.Slices)
            {
                if (string.IsNullOrWhiteSpace(slice.Name)) errors.Add("Each slice requires a name.");
                if (string.IsNullOrWhiteSpace(slice.Dnn)) errors.Add($"Slice '{slice.Name}' requires a DNN.");
                if (slice.Sst is < 0 or > 255) errors.Add($"Slice '{slice.Name}' has an invalid SST.");
                if (!Regex.IsMatch(slice.Sd ?? string.Empty, "^[0-9A-Fa-f]{6}$")) errors.Add($"Slice '{slice.Name}' requires a six-digit hexadecimal SD.");
                if (!ids.Add($"{slice.Sst}:{slice.Sd}")) errors.Add($"Duplicate S-NSSAI: {slice.Sst}:{slice.Sd}.");
            }
        }

        var requiredKinds = new[] { "amf-log", "smf-log", "upf-log", "packet-metadata", "experiment-metadata" };
        var actualKinds = profile.EvidenceSources?.Select(source => source.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
        foreach (var requiredKind in requiredKinds)
        {
            if (!actualKinds.Contains(requiredKind)) errors.Add($"Missing required evidence source: {requiredKind}.");
        }

        foreach (var service in new[] { "oai-amf", "oai-smf", "oai-upf" })
        {
            if (profile.RequiredServices is null || !profile.RequiredServices.Contains(service, StringComparer.OrdinalIgnoreCase)) errors.Add($"Missing required service: {service}.");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    private static void WriteValidationResult(ValidationResult result)
    {
        if (result.IsValid)
        {
            Console.WriteLine("LAB_CONFIG_VALID");
            return;
        }

        foreach (var error in result.Errors) Console.Error.WriteLine($"VALIDATION_ERROR {error}");
    }

    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private static string ComputeTextSha256(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private static void WriteJson<T>(string outputPath, T value)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static string? GetOption(string[] args, string option)
    {
        var index = Array.FindIndex(args, argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < args.Length - 1 ? args[index + 1] : null;
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AdaptiveForensics.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string ResolveSolutionPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(FindSolutionRoot(), relativePath));

    private static string FirstLine(string text) =>
        text.Replace("\0", string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "No output.";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private sealed record LabProfile(
        string EnvironmentName,
        string UpstreamRevision,
        string OaiConfigurationRelativePath,
        SliceProfile[] Slices,
        EvidenceSource[] EvidenceSources,
        string[] RequiredServices);

    private sealed record SliceProfile(string Name, int Sst, string Sd, string Dnn, string UeSubnet, string TrafficProfile);

    private sealed record EvidenceSource(string Id, string Kind, string Component, string CollectionMethod, string ExpectedArtifact);

    private sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);

    private sealed record LabManifest(
        DateTimeOffset GeneratedAtUtc,
        string ConfigurationPath,
        string ConfigurationSha256,
        string OaiConfigurationPath,
        string OaiConfigurationSha256,
        string EnvironmentName,
        string UpstreamRevision,
        SliceProfile[] Slices,
        EvidenceSource[] EvidenceSources,
        string[] RequiredServices);

    private sealed record PreflightReport(
        DateTimeOffset GeneratedAtUtc,
        bool ConfigurationValid,
        IReadOnlyList<string> ValidationErrors,
        IReadOnlyList<ReadinessCheck> Checks,
        bool ReadyForContainerDeployment);

    private sealed record ScenarioCatalog(string Phase, string Purpose, ScenarioDefinition[] Scenarios);

    private sealed record ScenarioDefinition(
        string Id,
        string SliceName,
        int Sst,
        string Sd,
        string Dnn,
        string VirtualUe,
        string TunnelAddress,
        int TargetRateKbps,
        int DurationSeconds,
        string[] ExpectedArtifacts);

    private sealed record ScenarioLedger(
        DateTimeOffset GeneratedAtUtc,
        string ScenarioDefinitionPath,
        string ScenarioDefinitionSha256,
        string Phase,
        string Purpose,
        ScenarioDefinition[] Scenarios);

    private sealed record EvidenceIngestionCatalog(
        string Phase,
        string Purpose,
        int MinimumRecordCount,
        EvidenceSourceDefinition[] Sources);

    private sealed record EvidenceSourceDefinition(
        string Id,
        string InputRun,
        string RelativePath,
        string SourceKind,
        string SourceComponent,
        string NetworkFunction,
        string? ScenarioId,
        PreliminarySliceContext? PreliminarySliceContext,
        string Expansion);

    private sealed record PreliminarySliceContext(
        string? Label,
        int? Sst,
        string? Sd,
        string? Dnn);

    private sealed record NormalisedEvidenceRecord(
        string RecordId,
        DateTimeOffset IngestedAtUtc,
        DateTimeOffset EvidenceTimestampUtc,
        string SourceReference,
        string SourceKind,
        string SourceComponent,
        string NetworkFunction,
        string? ScenarioId,
        PreliminarySliceContext? PreliminarySliceContext,
        long ArtifactSizeBytes,
        bool OriginalArtifactUnchanged,
        string Observation);

    private sealed record EvidenceIndex(
        string Phase,
        string Purpose,
        DateTimeOffset IngestedAtUtc,
        string IngestionConfigurationPath,
        IReadOnlyDictionary<string, string> InputRuns,
        int SourceDefinitionCount,
        int RecordCount,
        IReadOnlyList<NormalisedEvidenceRecord> Records);

    private sealed record SliceAttributionCatalog(
        string Phase,
        string Purpose,
        AttributionSlice[] Slices,
        AttributionRule[] Rules);

    private sealed record AttributionSlice(
        string Id,
        string Name,
        string SliceType,
        int Sst,
        string Sd,
        string Dnn);

    private sealed record AttributionRule(
        string Id,
        int Priority,
        string Decision,
        string? SliceId,
        bool UsePreliminarySliceContext,
        string[]? CandidateSliceIds,
        AttributionRuleCondition? When,
        string Rationale);

    private sealed record AttributionRuleCondition(
        string? ScenarioId,
        string? NetworkFunction,
        bool? RequirePreliminarySliceContext);

    private sealed record AttributionSliceContext(
        string SliceId,
        string SliceName,
        string SliceType,
        int Sst,
        string Sd,
        string Dnn);

    private sealed record AttributionDecision(
        string RecordId,
        string SourceReference,
        DateTimeOffset AttributedAtUtc,
        DateTimeOffset EvidenceTimestampUtc,
        string NetworkFunction,
        string SourceKind,
        string? ScenarioId,
        PreliminarySliceContext? PreliminarySliceContext,
        AttributionSliceContext? AssignedSliceContext,
        IReadOnlyList<AttributionSliceContext> CandidateSliceContexts,
        string DecisionStatus,
        string RuleId,
        string Rationale,
        IReadOnlyList<string> DecisionInputs);

    private sealed record SliceAttributionReport(
        string Phase,
        string Purpose,
        DateTimeOffset AttributedAtUtc,
        string EvidenceIndexPath,
        string EvidenceIndexSha256,
        string AttributionRulesPath,
        string AttributionRulesSha256,
        int InputRecordCount,
        int DecisionCount,
        int AttributedCount,
        int AmbiguousCount,
        int UnattributedCount,
        int KnownGroundTruthDecisionCount,
        int KnownGroundTruthCorrectCount,
        double KnownGroundTruthAccuracyPercent,
        bool Accepted,
        IReadOnlyList<AttributionDecision> Decisions);

    private sealed record AdaptiveCollectionCatalog(
        string Phase,
        string Purpose,
        string Collector,
        string IntegrityAlgorithm,
        string[] RequiredCustodyFields,
        AdaptiveCollectionRule[] Rules);

    private sealed record AdaptiveCollectionRule(
        string Id,
        int Priority,
        bool Approved,
        bool Enabled,
        AdaptiveCollectionTrigger Trigger,
        AdaptiveCollectionAction Action,
        string Rationale);

    private sealed record AdaptiveCollectionTrigger(
        string EventType,
        string[] SourceKinds,
        int MinimumAttributedRecordCount,
        bool RequireAssignedSlice);

    private sealed record AdaptiveCollectionAction(
        string CollectionPriority,
        string[] SourceKinds,
        string Description);

    private sealed record AdaptiveRuleApplication(
        string RuleId,
        string EventType,
        string? SliceId,
        DateTimeOffset TriggerObservedAtUtc,
        DateTimeOffset RuleUpdatedAtUtc,
        long AdaptationLatencyMilliseconds,
        IReadOnlyList<string> EvidenceRecordIds,
        string CollectionPriority,
        IReadOnlyList<string> PrioritisedSourceKinds,
        string Rationale);

    private sealed record CustodyEntry(
        int Sequence,
        string RecordId,
        string SourceReference,
        string Collector,
        string Action,
        DateTimeOffset TimestampUtc,
        string Sha256,
        long ArtifactSizeBytes,
        string StorageLocation,
        string? PreviousEntrySha256,
        string EntrySha256,
        bool SourceUnchangedDuringHash);

    private sealed record HashVerificationResult(
        string RecordId,
        string SourceReference,
        string ExpectedSha256,
        string ActualSha256,
        bool HashMatches,
        DateTimeOffset VerifiedAtUtc,
        long ArtifactSizeBytes,
        bool SourceExists);

    private sealed record AdaptiveCollectionReport(
        string Phase,
        string Purpose,
        DateTimeOffset CollectedAtUtc,
        string AttributionReportPath,
        string AttributionReportSha256,
        string AdaptiveRuleCatalogPath,
        string AdaptiveRuleCatalogSha256,
        string Collector,
        string IntegrityAlgorithm,
        int InputDecisionCount,
        int ApprovedRuleApplicationCount,
        int CustodyEntryCount,
        int CompleteCustodyEntryCount,
        int VerifiedHashCount,
        long TriggerToRuleUpdateLatencyMilliseconds,
        bool Accepted,
        IReadOnlyList<AdaptiveRuleApplication> RuleApplications,
        IReadOnlyList<CustodyEntry> CustodyLedger,
        IReadOnlyList<HashVerificationResult> HashVerificationResults);

    private sealed record EvaluationCatalog(
        string Phase,
        string Purpose,
        EvaluationScenarioDefinition[] Scenarios,
        string[] Limitations);

    private sealed record EvaluationScenarioDefinition(
        string Id,
        string Type,
        string Purpose,
        int MinimumEvidenceItems,
        string[]? ScenarioIds,
        string? ExpectedDecisionStatus,
        string? ExpectedRuleId,
        string? ExpectedCollectionPriority,
        PreliminarySliceContext? SyntheticPreliminarySliceContext,
        string[]? RequiredSourceKinds,
        string[]? ExpectedSliceIds);

    private sealed record Phase1BaselineSummary(string Result, string[] ExpectedContainers);

    private sealed record Phase2TrafficSummary(string Result, string[] Scenarios, string[] RanContainers);

    private sealed record Phase3IngestionSummary(
        string Result,
        string Phase1InputRun,
        string Phase2InputRun,
        int RecordCount,
        bool OriginalArtifactsUnchanged);

    private sealed record Phase4AttributionSummary(
        string Result,
        string Phase3InputRun,
        int DecisionCount,
        int KnownGroundTruthDecisionCount,
        int KnownGroundTruthCorrectCount,
        double KnownGroundTruthAccuracyPercent);

    private sealed record Phase5AdaptiveSummary(
        string Result,
        string Phase4InputRun,
        int ApprovedRuleApplicationCount,
        int CustodyEntryCount,
        int CompleteCustodyEntryCount,
        int VerifiedHashCount,
        long TriggerToRuleUpdateLatencyMilliseconds);

    private sealed record EvaluationInputCheck(
        string Name,
        string Path,
        string Sha256,
        bool Passed,
        string Detail);

    private sealed record EvaluationScenarioResult(
        string Id,
        string Type,
        string Purpose,
        bool Passed,
        int ExpectedEvidenceItems,
        int ObservedEvidenceItems,
        string ResultSummary,
        IReadOnlyList<string> EvidenceReferences);

    private sealed record EvaluationMetric(
        string Name,
        string OperationalDefinition,
        double Numerator,
        double Denominator,
        double Value,
        string Unit,
        bool Passed,
        string VerificationArtifact);

    private sealed record DockerStatsEntry(
        string Name,
        double CpuPercent,
        long MemoryBytes,
        long NetworkBytes,
        long BlockWriteBytes);

    private sealed record DockerStatsSnapshot(
        string SourcePath,
        int ContainerCount,
        double TotalCpuPercent,
        double TotalMemoryMiB,
        long TotalNetworkBytes,
        long TotalBlockWriteBytes);

    private sealed record OperationalOverheadObservation(
        int BeforeContainerCount,
        int AfterContainerCount,
        double CpuPercentBefore,
        double CpuPercentAfter,
        double CpuPercentDelta,
        double MemoryMiBBefore,
        double MemoryMiBAfter,
        double MemoryMiBDelta,
        long NetworkBytesBefore,
        long NetworkBytesAfter,
        long NetworkBytesDelta,
        long BlockWriteBytesBefore,
        long BlockWriteBytesAfter,
        long BlockWriteBytesDelta,
        long EvidenceRepositoryBytes,
        long EvaluationExecutionMilliseconds,
        string Interpretation);

    private sealed record PrototypeEvaluationReport(
        string Phase,
        string Purpose,
        DateTimeOffset EvaluatedAtUtc,
        string EvaluationConfigurationPath,
        string EvaluationConfigurationSha256,
        bool Accepted,
        IReadOnlyList<EvaluationInputCheck> InputChecks,
        IReadOnlyList<EvaluationScenarioResult> Scenarios,
        IReadOnlyList<EvaluationMetric> Metrics,
        OperationalOverheadObservation OperationalOverhead,
        IReadOnlyList<string> Limitations,
        string Reproducibility);

    private sealed record ReadinessCheck(string Name, bool Passed, string Summary);

    private sealed record CommandResult(bool Found, int ExitCode, string Output, string Error)
    {
        public string Summary => Found ? $"Exit code {ExitCode}." : "Command not found.";
    }
}
