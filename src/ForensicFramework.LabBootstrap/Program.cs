using System.Diagnostics;
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

    private sealed record ReadinessCheck(string Name, bool Passed, string Summary);

    private sealed record CommandResult(bool Found, int ExitCode, string Output, string Error)
    {
        public string Summary => Found ? $"Exit code {ExitCode}." : "Command not found.";
    }
}
