using System.Diagnostics;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hob.OutcomeEval;

internal static class OutcomeEvaluator
{
    private const int MaxCandidateFileCount = 20_000;
    private const long MaxCandidateFileBytes = 32L * 1024 * 1024;
    private const long MaxCandidateTotalBytes = 512L * 1024 * 1024;
    private const int CandidateBufferBytes = 64 * 1024;
    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static async Task<int> RunAsync(
        EvaluationOptions options,
        string repositoryRoot,
        string dotnetPath,
        CorpusManifest corpus)
    {
        var resultsRoot = Path.GetFullPath(options.ResultsDirectory);
        Directory.CreateDirectory(resultsRoot);
        if (Directory.EnumerateFileSystemEntries(resultsRoot).Any())
            throw new EvaluationConfigurationException("The results directory must be empty so evidence from separate runs cannot mix.");

        var tempRoot = Path.Combine(Path.GetTempPath(), "hob-outcome-eval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            if (!string.Equals(dotnetPath, "dotnet", StringComparison.OrdinalIgnoreCase))
                dotnetPath = Path.GetFullPath(dotnetPath);
            else
                File.Copy(Path.Combine(repositoryRoot, "global.json"), Path.Combine(tempRoot, "global.json"));

            var compilerBytes = await File.ReadAllBytesAsync(options.CompilerPath);
            var runner = new CompilerRunner(dotnetPath, options.CompilerPath, resultsRoot);
            var sdkVersion = await runner.GetSdkVersionAsync();
            var scenarioResults = new List<ScenarioResult>();
            foreach (var scenario in corpus.Scenarios.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                var isOverride = options.CandidateOverrides.TryGetValue(scenario.Id, out var overridePath);
                var candidatePath = isOverride
                    ? Path.GetFullPath(overridePath!)
                    : ResolveRepoPath(repositoryRoot, scenario.CandidateRoot);
                var candidateLabel = isOverride ? "override" : "reference";
                var workRoot = Path.Combine(tempRoot, "candidate-" + CompilerRunner.SafeSegment(scenario.Id));
                try
                {
                    var packageRoot = CopyCandidate(candidatePath, workRoot);
                    var sourceHash = HashDirectory(packageRoot);
                    scenarioResults.Add(await EvaluateScenarioAsync(
                        tempRoot,
                        packageRoot,
                        candidateLabel,
                        sourceHash,
                        scenario,
                        runner,
                        resultsRoot));
                }
                finally
                {
                    DeleteTemporaryDirectory(tempRoot, workRoot);
                }
            }

            var seedResults = options.VerifySeeds
                ? await EvaluateSeedsAsync(repositoryRoot, tempRoot, corpus, runner, resultsRoot)
                : Array.Empty<SeedResult>();
            var referencesPassed = scenarioResults.All(result => result.Passed);
            var seedsPassed = !options.VerifySeeds || seedResults.All(seed => seed.Detected);
            var fixtureIdentity = string.Equals(options.AgentModel, "reference-fixture", StringComparison.Ordinal)
                && string.Equals(options.AgentRevision, "local-checkout", StringComparison.Ordinal)
                && string.Equals(options.AgentTool, "outcome-eval", StringComparison.Ordinal);
            var results = new ResultsDocument(
                1,
                new HarnessIdentity(
                    "1.0.0",
                    sdkVersion,
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.ProcessArchitecture.ToString()),
                new CandidateAgentIdentity(
                    fixtureIdentity ? "fixture" : "agent",
                    options.AgentModel,
                    options.AgentRevision,
                    options.AgentTool),
                new CompilerIdentity(Path.GetFileName(options.CompilerPath), CompilerRunner.Sha256(compilerBytes)),
                new CorpusIdentity("eval/corpus.json", corpus.Sha256, corpus.SchemaVersion, corpus.Scenarios.Count),
                options.VerifySeeds,
                options.VerifySeeds ? (seedsPassed ? "passed" : "failed") : "not_requested",
                referencesPassed,
                seedsPassed,
                referencesPassed && seedsPassed,
                scenarioResults.OrderBy(result => result.Id, StringComparer.Ordinal).ToArray(),
                seedResults.OrderBy(seed => seed.ScenarioId, StringComparer.Ordinal)
                    .ThenBy(seed => seed.Id, StringComparer.Ordinal).ToArray());

            var json = JsonSerializer.Serialize(results, ResultJsonOptions) + "\n";
            var resultBytes = new UTF8Encoding(false).GetBytes(json);
            var resultPath = Path.Combine(resultsRoot, "results.json");
            await File.WriteAllBytesAsync(resultPath, resultBytes);
            EnsureNoAbsolutePathLeak(resultBytes, repositoryRoot, tempRoot, options);
            return results.Passed ? 0 : 1;
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<ScenarioResult> EvaluateScenarioAsync(
        string tempRoot,
        string packageRoot,
        string candidateRootLabel,
        string candidateHash,
        EvaluationScenario scenario,
        CompilerRunner runner,
        string resultsRoot)
    {
        var commands = new List<CommandExecution>();
        var checks = new List<AcceptanceCheck>();
        var artifacts = new List<ArtifactIdentity>();
        var check = await RunCompilerAsync(runner, scenario.Id, "check", ["check", packageRoot, "--json"], packageRoot);
        commands.Add(check.Record);
        var build = await RunCompilerAsync(runner, scenario.Id, "build", ["build", packageRoot], packageRoot);
        commands.Add(build.Record);
        var builtArtifact = ParseBuiltArtifact(build.Capture);
        var buildDirectory = builtArtifact is not null && File.Exists(builtArtifact)
            ? Path.GetDirectoryName(builtArtifact)
            : null;
        if (buildDirectory is not null)
            artifacts.AddRange(await CopyArtifactTreeAsync(buildDirectory, resultsRoot, scenario.Id, "build"));

        var isWeb = GetSmokeString(scenario, "kind") == "web";
        CapturedCommand? tests = null;
        if (!isWeb)
        {
            var testRun = await RunCompilerAsync(runner, scenario.Id, "test", ["test", packageRoot], packageRoot);
            commands.Add(testRun.Record);
            tests = testRun.Capture;
        }

        var apiRun = await RunCompilerAsync(runner, scenario.Id, "inspect-api", ["inspect", "api", packageRoot, "--json"], packageRoot);
        commands.Add(apiRun.Record);
        var auditRun = await RunCompilerAsync(runner, scenario.Id, "audit", ["audit", packageRoot, "--json"], packageRoot);
        commands.Add(auditRun.Record);

        using var api = ParseJson(apiRun.Capture.StandardOutput);
        using var audit = ParseJson(auditRun.Capture.StandardOutput);
        var apiSymbol = GetInspectSymbol(scenario, api?.RootElement);
        CapturedCommand? effectsCapture = null;
        if (apiSymbol is not null)
        {
            var effectsRun = await RunCompilerAsync(
                runner,
                scenario.Id,
                "inspect-effects",
                ["inspect", "effects", packageRoot, apiSymbol, "--json"],
                packageRoot);
            commands.Add(effectsRun.Record);
            effectsCapture = effectsRun.Capture;
        }
        using var effects = effectsCapture is null ? null : ParseJson(effectsCapture.StandardOutput);

        var cliBehavior = scenario.Id == "cli-policy"
            ? await RunCliInputsAsync(packageRoot, tempRoot, runner, scenario.Id)
            : null;
        var auditBehavior = scenario.Id == "audit-repair"
            ? await RunAuditInputsAsync(packageRoot, tempRoot, runner, scenario.Id)
            : null;
        if (cliBehavior is not null)
            commands.AddRange(cliBehavior.Commands);
        if (auditBehavior is not null)
            commands.AddRange(auditBehavior.Commands);

        var receiptDetail = false;
        var receiptValid = buildDirectory is not null && VerifyReceipt(buildDirectory, out receiptDetail);
        using var commandSchema = buildDirectory is null
            ? null
            : ReadJsonFile(Path.Combine(buildDirectory, "command-schema.json"));
        var commandSchemaValid = commandSchema is not null && GetInt(commandSchema.RootElement, "schema_version") == 5;
        var baseCheckPasses = IsSuccessful(check.Capture)
            && DiagnosticsAreEmpty(check.Capture)
            && IsSuccessful(build.Capture)
            && builtArtifact is not null
            && File.Exists(builtArtifact)
            && IsSuccessful(apiRun.Capture)
            && GetInt(api?.RootElement, "schema_version") == 13
            && VerifyWebRequestSettings(api?.RootElement, isWeb)
            && IsSuccessful(auditRun.Capture)
            && GetInt(audit?.RootElement, "schema_version") == 11
            && effectsCapture is not null
            && IsSuccessful(effectsCapture)
            && (scenario.Id != "web-greeting" || effects is not null
                && JsonArrayContains(effects.RootElement, "required_capabilities", "db.write")
                && JsonArrayContains(effects.RootElement, "inferred_effects", "db.write"))
            && (scenario.Id != "audit-repair" || VerifyAuditRootInputs(audit?.RootElement))
            && (isWeb || tests is not null && IsSuccessful(tests))
            && (scenario.Id != "audit-repair" || HasExpectedAuditLanguageTests(tests))
            && receiptValid
            && (isWeb || commandSchemaValid);

        var scenarioPrefix = scenario.Id + ".";
        switch (scenario.Id)
        {
            case "cli-policy":
                AddCheck(checks, scenarioPrefix + "check-clean", baseCheckPasses,
                    "check/build/API v13/audit v11/effects and generated v5 schema/v3 receipt passed", Evidence(check, build, apiRun, auditRun));
                AddCheck(checks, scenarioPrefix + "command-schema", commandSchemaValid && HasCommand(buildDirectory, "decide", "fs.read"),
                    "v5 command schema declares decide with fs.read", Evidence(build));
                AddCheck(checks, scenarioPrefix + "priority-order", cliBehavior?.PriorityOrderPassed == true,
                    "evaluator-owned mixed-marker files return REVOKED in both orders", cliBehavior?.EvidencePaths ?? []);
                break;
            case "audit-repair":
                AddCheck(checks, scenarioPrefix + "check-clean", baseCheckPasses,
                    "check/build/API v13/audit v11/effects and generated v5 schema/v3 receipt passed", Evidence(check, build, apiRun, auditRun));
                AddCheck(checks, scenarioPrefix + "expected-diagnostics", IsSuccessful(check.Capture) && DiagnosticsAreEmpty(check.Capture),
                    "clean reference candidate has no compiler diagnostics before the constraint seed is applied", Evidence(check));
                var effectPassed = effects is not null
                    && IsSuccessful(effectsCapture!)
                    && JsonArrayContains(effects.RootElement, "required_capabilities", "fs.read")
                    && JsonArrayContains(effects.RootElement, "inferred_effects", "fs.read")
                    && JsonArrayContains(effects.RootElement, "declared_effects", "fs.read")
                    && JsonTextContains(effects.RootElement, "fs.read_text_async");
                AddCheck(checks, scenarioPrefix + "inspect-effects", effectPassed,
                    "selected function reports declared and inferred fs.read through async read", EvidenceFor(commands, "inspect-effects"));
                var auditPassed = GetInt(audit?.RootElement, "schema_version") == 11
                    && JsonArrayContains(audit!.RootElement.GetProperty("manifest_grants"), "fs.read")
                    && JsonTextContains(audit.RootElement, "fs.read_text_async")
                    && JsonTextContains(audit.RootElement, "claim_only")
                    && audit.RootElement.TryGetProperty("managed_adapters", out var auditAdapters)
                    && auditAdapters.ValueKind == JsonValueKind.Array
                    && auditAdapters.GetArrayLength() == 0;
                AddCheck(checks, scenarioPrefix + "audit-report", auditPassed,
                    "audit v11 records the fs.read grant, claim-only operation, portable root identity, and adapter array", Evidence(auditRun));
                AddCheck(checks, scenarioPrefix + "receipt-v4", receiptValid && commandSchemaValid && receiptDetail,
                    "build receipt v4 records successful compiler/build stages and carries portable package identities and adapter array", Evidence(build));
                break;
            case "web-greeting":
                AddCheck(checks, scenarioPrefix + "check-clean", baseCheckPasses,
                    "check/build/API v13/audit v11/effects and generated v5 schema/v4 receipt passed", Evidence(check, build, apiRun, auditRun));
                break;
        }

        if (scenario.Id == "cli-policy")
        {
            var smokeArgs = GetSmokeStringArray(scenario, "run_args");
            var runResult = await RunCompilerAsync(runner, scenario.Id, "run", ["run", packageRoot, "--", .. smokeArgs], packageRoot);
            commands.Add(runResult.Record);
            var expected = "GRANTED";
            var runtimePassed = IsSuccessful(runResult.Capture)
                && HasSingleLineOutput(runResult.Capture.StandardOutput, expected)
                && runResult.Capture.StandardError.Length == 0;
            runtimePassed &= cliBehavior?.AllPassed == true;
            AddCheck(checks, scenarioPrefix + "runtime", runtimePassed,
                "manifest smoke and evaluator-owned input cases return exactly one expected line", Evidence(runResult).Concat(
                    cliBehavior?.EvidencePaths ?? []).ToArray());
        }
        else if (scenario.Id == "audit-repair")
        {
            AddCheck(checks, scenarioPrefix + "runtime", auditBehavior?.AllPassed == true,
                "evaluator-owned files produce the trimmed first line, EMPTY, and the stable missing-file error",
                auditBehavior?.EvidencePaths ?? []);
        }
        else if (scenario.Id == "web-greeting")
        {
            var web = await RunWebSmokeAsync(scenario, packageRoot, tempRoot, runner, scenario.Id, resultsRoot, artifacts, verifyPersistence: true);
            commands.AddRange(web.Commands);
            AddCheck(checks, scenarioPrefix + "request-limits",
                VerifyWebRequestSettings(api?.RootElement, isWeb: true),
                "API v13 exposes the effective default body cap and request deadline after http_origin",
                Evidence(apiRun));
            AddCheck(checks, scenarioPrefix + "openapi", buildDirectory is not null && VerifyOpenApi(Path.Combine(buildDirectory, "openapi.json")),
                "generated OpenAPI declares the health, greeting, home, and bound greeting routes with parameter schemas", ArtifactEvidence(artifacts, scenario.Id, "openapi.json"));
            var routesPass = VerifyApiRoutes(api?.RootElement);
            AddCheck(checks, scenarioPrefix + "api-routes", routesPass,
                "inspect API v13 lists the GET/POST greeting and health routes", Evidence(apiRun));
            AddCheck(checks, scenarioPrefix + "route-bindings", VerifyApiRouteBindings(api?.RootElement) && web.RouteBindings,
                "inspect API v13 and OpenAPI describe path, required query, and optional query bindings that work over HTTP", Evidence(apiRun).Concat(web.EvidencePaths).ToArray());
            AddCheck(checks, scenarioPrefix + "trim-and-persistence", web.TrimmedAndPersisted,
                "POST trims both fields, blank input preserves the row, and saved data survives restart", web.EvidencePaths);
            AddCheck(checks, scenarioPrefix + "safe-html", web.SafeHtml,
                "HTML renders stored user text as escaped text", web.EvidencePaths);
        }

        var readme = File.Exists(Path.Combine(packageRoot, "README.md"))
            ? await File.ReadAllTextAsync(Path.Combine(packageRoot, "README.md"))
            : string.Empty;
        var readmeEvidence = await WriteEvidenceAsync(
            resultsRoot,
            $"evidence/{scenario.Id}/README.md",
            readme);
        switch (scenario.Id)
        {
            case "cli-policy":
                var cliDocs = readme.Contains("| Contains `REVOKE`, with or without `GRANT` | `REVOKED` |", StringComparison.Ordinal)
                    && readme.Contains("| Contains `GRANT` and no `REVOKE` | `GRANTED` |", StringComparison.Ordinal)
                    && readme.Contains("| Contains neither marker | `UNDECIDED` |", StringComparison.Ordinal);
                AddCheck(checks, scenarioPrefix + "docs-table", cliDocs, "README decision table agrees with all three outcomes", [readmeEvidence]);
                break;
            case "audit-repair":
                var docsClaims = readme.Contains("`claim_only`", StringComparison.Ordinal)
                    && readme.Contains("does not prove adapter behavior", StringComparison.Ordinal)
                    && readme.Contains("operating-system sandbox", StringComparison.Ordinal)
                    && !readme.Contains("proves adapter behavior", StringComparison.Ordinal);
                AddCheck(checks, scenarioPrefix + "docs-claims", docsClaims, "README describes claim-only assurance and sandbox limits", [readmeEvidence]);
                break;
            case "web-greeting":
                var webDocs = readme.Contains("| GET | `/health` | 200", StringComparison.Ordinal)
                    && readme.Contains("| GET | `/api/greeting` | 200 JSON when saved; otherwise 404 |", StringComparison.Ordinal)
                    && readme.Contains("| POST | `/api/greeting` | 201 JSON when saved; 400 when name or city is blank after trimming |", StringComparison.Ordinal)
                    && readme.Contains("| GET | `/` | 200 HTML", StringComparison.Ordinal);
                AddCheck(checks, scenarioPrefix + "docs-table", webDocs, "README route table matches the runtime contract", [readmeEvidence]);
                break;
        }

        var missingIds = scenario.CheckIds.Except(checks.Select(value => value.Id), StringComparer.Ordinal).ToArray();
        if (missingIds.Length != 0)
            throw new EvaluationConfigurationException($"Evaluator did not produce declared checks for '{scenario.Id}': {string.Join(", ", missingIds)}");
        if (checks.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != checks.Count)
            throw new EvaluationConfigurationException($"Evaluator produced duplicate check IDs for '{scenario.Id}'.");

        return new ScenarioResult(
            scenario.Id,
            scenario.Version,
            candidateRootLabel,
            candidateHash,
            checks.All(value => value.Passed),
            commands,
            checks.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray(),
            artifacts.OrderBy(value => value.Path, StringComparer.Ordinal).ToArray(),
            scenario.FixtureAgent);
    }

    private static async Task<SeedResult[]> EvaluateSeedsAsync(
        string repositoryRoot,
        string tempRoot,
        CorpusManifest corpus,
        CompilerRunner runner,
        string resultsRoot)
    {
        var results = new List<SeedResult>();
        foreach (var scenario in corpus.Scenarios.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            foreach (var mutation in scenario.Mutations.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                var candidateSource = ResolveRepoPath(repositoryRoot, scenario.CandidateRoot);
                var workRoot = Path.Combine(
                    tempRoot,
                    "seed-" + CompilerRunner.SafeSegment(scenario.Id) + "-" + CompilerRunner.SafeSegment(mutation.Id));
                try
                {
                    var packageRoot = CopyCandidate(candidateSource, workRoot);
                    ApplyMutation(packageRoot, mutation);
                    var candidateHash = HashDirectory(packageRoot);
                    var scenarioKey = "seed-" + scenario.Id + "-" + mutation.Id;
                    var expectedDetected = false;
                    var observed = new List<string>();
                    string evidencePath;

                    switch (mutation.Kind)
                    {
                        case "documentation":
                        {
                            var readmePath = Path.Combine(packageRoot, "README.md");
                            var readme = await File.ReadAllTextAsync(readmePath);
                            var passes = CheckDocumentation(scenario.Id, readme);
                            expectedDetected = !passes;
                            var evidence = await WriteEvidenceAsync(resultsRoot, $"seeds/{scenarioKey}/README.md", readme);
                            evidencePath = evidence;
                            break;
                        }
                        case "semantic" when scenario.Id == "cli-policy":
                        {
                            var behavior = await RunCliInputsAsync(packageRoot, workRoot, runner, scenarioKey);
                            evidencePath = behavior.EvidencePaths.FirstOrDefault() ?? "";
                            expectedDetected = !behavior.PriorityOrderPassed;
                            break;
                        }
                        case "semantic" when scenario.Id == "web-greeting":
                        {
                            var smoke = await RunWebSmokeAsync(
                                scenario,
                                packageRoot,
                                workRoot,
                                runner,
                                scenarioKey,
                                resultsRoot,
                                artifacts: null,
                                verifyPersistence: false,
                                evidencePrefix: $"seeds/{scenarioKey}/web-smoke");
                            expectedDetected = !smoke.TrimmedAndPersisted;
                            evidencePath = smoke.EvidencePaths.FirstOrDefault() ?? "";
                            break;
                        }
                        case "constraint" when scenario.Id == "audit-repair":
                        {
                            var checkRun = await RunCompilerAsync(runner, scenarioKey, "check", ["check", packageRoot, "--json"], packageRoot);
                            var codes = ReadDiagnosticCodes(checkRun.Capture);
                            expectedDetected = codes.Contains("E_CAPABILITY_MISSING", StringComparer.Ordinal)
                                && codes.Contains("E_EFFECT_EXCEEDED", StringComparer.Ordinal);
                            evidencePath = checkRun.Record.Stdout.Path;
                            if (expectedDetected)
                                observed.Add(mutation.ExpectedCheckId);
                            break;
                        }
                        default:
                            throw new EvaluationConfigurationException($"Unsupported seed kind '{mutation.Kind}' for '{scenario.Id}'.");
                    }

                    if (expectedDetected && observed.Count == 0)
                        observed.Add(mutation.ExpectedCheckId);
                    results.Add(new SeedResult(
                        scenario.Id,
                        mutation.Id,
                        mutation.Kind,
                        "reference",
                        candidateHash,
                        mutation.ExpectedCheckId,
                        expectedDetected,
                        observed.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                        evidencePath));
                }
                finally
                {
                    DeleteTemporaryDirectory(tempRoot, workRoot);
                }
            }
        }
        return results.ToArray();
    }

    private static void ApplyMutation(string packageRoot, EvaluationMutation mutation)
    {
        var declaredFiles = mutation.Files.ToHashSet(StringComparer.Ordinal);
        foreach (var replacement in mutation.ReplaceExactly)
        {
            if (!declaredFiles.Contains(replacement.Path))
                throw new EvaluationConfigurationException($"Mutation '{mutation.Id}' replacement path is not listed in files[].");
            var fullPath = ResolvePackagePath(packageRoot, replacement.Path);
            if (!File.Exists(fullPath))
                throw new EvaluationConfigurationException($"Mutation '{mutation.Id}' input file is missing: {replacement.Path}");
            var content = File.ReadAllText(fullPath);
            var occurrences = CountOccurrences(content, replacement.Old);
            if (occurrences != 1)
                throw new EvaluationConfigurationException($"Mutation '{mutation.Id}' expected one exact match in {replacement.Path}, found {occurrences}.");
            File.WriteAllText(fullPath, content.Replace(replacement.Old, replacement.New, StringComparison.Ordinal), new UTF8Encoding(false));
        }
    }

    private static async Task<(CapturedCommand Capture, CommandExecution Record)> RunCompilerAsync(
        CompilerRunner runner,
        string scenarioId,
        string commandId,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null) =>
        await runner.RunAsync(scenarioId, commandId, arguments, workingDirectory, environment, timeout);

    private static async Task<CliBehaviorResult> RunCliInputsAsync(
        string packageRoot,
        string tempRoot,
        CompilerRunner runner,
        string scenarioId)
    {
        var inputDirectory = Path.Combine(tempRoot, "inputs-" + CompilerRunner.SafeSegment(scenarioId));
        Directory.CreateDirectory(inputDirectory);
        (string Id, string Content, string Expected)[] cases =
        [
            ("grant-first", "GRANT\nREVOKE\n", "REVOKED"),
            ("revoke-first", "REVOKE\nGRANT\n", "REVOKED"),
            ("neither", "\n other \n", "UNDECIDED"),
            ("trimmed-grant", " \n\tGRANT \t\n", "GRANTED"),
            ("case-sensitive", "grant\nrevoke\n", "UNDECIDED")
        ];
        var passed = new Dictionary<string, bool>(StringComparer.Ordinal);
        var commands = new List<CommandExecution>();
        var evidencePaths = new List<string>();
        foreach (var inputCase in cases)
        {
            var inputPath = Path.Combine(inputDirectory, inputCase.Id + ".txt");
            await File.WriteAllTextAsync(inputPath, inputCase.Content, new UTF8Encoding(false));
            var run = await RunCompilerAsync(
                runner,
                scenarioId,
                "run-policy-" + inputCase.Id,
                ["run", packageRoot, "--", "decide", inputPath],
                packageRoot);
            commands.Add(run.Record);
            evidencePaths.Add(run.Record.Stdout.Path);
            evidencePaths.Add(run.Record.Stderr.Path);
            passed[inputCase.Id] = IsSuccessful(run.Capture)
                && run.Capture.StandardError.Length == 0
                && HasSingleLineOutput(run.Capture.StandardOutput, inputCase.Expected);
        }
        var missingInputPath = Path.Combine(inputDirectory, "missing-policy.txt");
        var missing = await RunCompilerAsync(
            runner,
            scenarioId,
            "run-policy-missing-file",
            ["run", packageRoot, "--", "decide", missingInputPath],
            packageRoot);
        commands.Add(missing.Record);
        evidencePaths.Add(missing.Record.Stdout.Path);
        evidencePaths.Add(missing.Record.Stderr.Path);
        passed["missing-file"] = missing.Capture.ExitCode is not null and not 0
            && !missing.Capture.TimedOut
            && !missing.Capture.StandardOutputTruncated
            && !missing.Capture.StandardErrorTruncated
            && missing.Capture.StandardOutput.Length == 0
            && HasSingleLineOutput(missing.Capture.StandardError, "Unable to read policy file");
        return new CliBehaviorResult(
            passed.Values.All(value => value),
            passed["grant-first"] && passed["revoke-first"],
            commands,
            evidencePaths);
    }

    private static async Task<AuditBehaviorResult> RunAuditInputsAsync(
        string packageRoot,
        string tempRoot,
        CompilerRunner runner,
        string scenarioId)
    {
        var inputDirectory = Path.Combine(tempRoot, "inputs-" + CompilerRunner.SafeSegment(scenarioId));
        Directory.CreateDirectory(inputDirectory);
        (string Id, string Content, string Expected)[] cases =
        [
            ("trim-first-line", "\n \t\n  OutcomeEval first marker  \nsecond line\n", "OutcomeEval first marker"),
            ("empty-file", " \n\t\n", "EMPTY")
        ];
        var commands = new List<CommandExecution>();
        var evidencePaths = new List<string>();
        var passed = true;
        foreach (var inputCase in cases)
        {
            var inputPath = Path.Combine(inputDirectory, inputCase.Id + ".txt");
            await File.WriteAllTextAsync(inputPath, inputCase.Content, new UTF8Encoding(false));
            var run = await RunCompilerAsync(
                runner,
                scenarioId,
                "run-reader-" + inputCase.Id,
                ["run", packageRoot, "--", "first", inputPath],
                packageRoot);
            commands.Add(run.Record);
            evidencePaths.Add(run.Record.Stdout.Path);
            evidencePaths.Add(run.Record.Stderr.Path);
            passed &= IsSuccessful(run.Capture)
                && run.Capture.StandardError.Length == 0
                && HasSingleLineOutput(run.Capture.StandardOutput, inputCase.Expected);
        }

        var missingPath = Path.Combine(inputDirectory, "must-not-exist.txt");
        var missing = await RunCompilerAsync(
            runner,
            scenarioId,
            "run-reader-missing-file",
            ["run", packageRoot, "--", "first", missingPath],
            packageRoot);
        commands.Add(missing.Record);
        evidencePaths.Add(missing.Record.Stdout.Path);
        evidencePaths.Add(missing.Record.Stderr.Path);
        var expectedError = HasSingleLineOutput(missing.Capture.StandardError, "Unable to read input file")
            && missing.Capture.StandardOutput.Length == 0;
        if (!expectedError)
            expectedError = HasSingleLineOutput(missing.Capture.StandardOutput, "Unable to read input file")
                && missing.Capture.StandardError.Length == 0;
        passed &= missing.Capture.ExitCode is not null and not 0
            && !missing.Capture.TimedOut
            && !missing.Capture.StandardOutputTruncated
            && !missing.Capture.StandardErrorTruncated
            && expectedError;
        return new AuditBehaviorResult(passed, commands, evidencePaths);
    }

    private static bool HasSingleLineOutput(byte[] bytes, string expected)
    {
        var output = Decode(bytes);
        return output == expected + "\n" || output == expected + "\r\n";
    }

    private static bool HasExpectedAuditLanguageTests(CapturedCommand? capture)
    {
        if (capture is null || !IsSuccessful(capture))
            return false;

        var output = Decode(capture.StandardOutput).Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var expectedTestsPassed = lines.Contains(
                "PASS app::reader :: the first nonempty line is trimmed",
                StringComparer.Ordinal)
            && lines.Contains(
                "PASS app::reader :: an all-whitespace input has the empty result",
                StringComparer.Ordinal);
        if (!expectedTestsPassed || lines.Length == 0)
            return false;

        var summary = lines[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return summary.Length == 4
            && int.TryParse(summary[0], out var passed)
            && summary[1] == "passed,"
            && int.TryParse(summary[2], out var failed)
            && summary[3] == "failed"
            && passed >= 2
            && failed == 0;
    }

    private sealed record CliBehaviorResult(
        bool AllPassed,
        bool PriorityOrderPassed,
        IReadOnlyList<CommandExecution> Commands,
        IReadOnlyList<string> EvidencePaths);

    private sealed record AuditBehaviorResult(
        bool AllPassed,
        IReadOnlyList<CommandExecution> Commands,
        IReadOnlyList<string> EvidencePaths);

    private static string CopyCandidate(string sourceRoot, string workRoot)
    {
        if ((File.GetAttributes(sourceRoot) & FileAttributes.ReparsePoint) != 0)
            throw CandidateLimitException("candidate root is a symbolic link or reparse point");
        var sourceFiles = GetCandidateFiles(sourceRoot);
        Directory.CreateDirectory(workRoot);
        var targetRoot = Path.Combine(workRoot, "package");
        Directory.CreateDirectory(targetRoot);
        long copiedBytes = 0;
        foreach (var source in sourceFiles)
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            var target = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            CopyCandidateFile(source, target, ref copiedBytes);
        }
        return targetRoot;
    }

    private static void CopyCandidateFile(string source, string target, ref long totalBytes)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CandidateBufferBytes, FileOptions.SequentialScan);
        var expectedLength = input.Length;
        if (expectedLength > MaxCandidateFileBytes)
            throw CandidateLimitException($"file exceeds {MaxCandidateFileBytes} bytes");
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, CandidateBufferBytes, FileOptions.SequentialScan);
        var buffer = new byte[CandidateBufferBytes];
        long copiedForFile = 0;
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            copiedForFile += read;
            totalBytes += read;
            if (copiedForFile > MaxCandidateFileBytes || totalBytes > MaxCandidateTotalBytes)
                throw CandidateLimitException("candidate data grew beyond the configured byte limits while it was copied");
            output.Write(buffer, 0, read);
        }
        if (copiedForFile != expectedLength)
            throw new EvaluationConfigurationException("A candidate file changed while it was being copied; provide a stable candidate tree.");
    }

    private static string HashDirectory(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        foreach (var file in GetCandidateFiles(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, CandidateBufferBytes, FileOptions.SequentialScan);
            var expectedLength = stream.Length;
            BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, expectedLength);
            hash.AppendData(lengthBytes);
            var buffer = new byte[CandidateBufferBytes];
            long readForFile = 0;
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                readForFile += read;
                hash.AppendData(buffer.AsSpan(0, read));
            }
            if (readForFile != expectedLength)
                throw new EvaluationConfigurationException("A candidate file changed while it was being hashed; provide a stable candidate tree.");
            hash.AppendData([0]);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string[] GetCandidateFiles(string root)
    {
        var files = new List<string>();
        long totalBytes = 0;
        CollectCandidateFiles(root, files, ref totalBytes);
        return files.OrderBy(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal).ToArray();
    }

    private static void CollectCandidateFiles(string root, ICollection<string> files, ref long totalBytes)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw CandidateLimitException("symbolic links and reparse points are not accepted");
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                if (Path.GetFileName(entry) is ".git" or "out" or "bin" or "obj" or "node_modules")
                    continue;
                CollectCandidateFiles(entry, files, ref totalBytes);
            }
            else if (!string.Equals(Path.GetFileName(entry), "global.json", StringComparison.OrdinalIgnoreCase))
            {
                var length = new FileInfo(entry).Length;
                if (length > MaxCandidateFileBytes)
                    throw CandidateLimitException($"file exceeds {MaxCandidateFileBytes} bytes");
                totalBytes += length;
                if (totalBytes > MaxCandidateTotalBytes)
                    throw CandidateLimitException($"total candidate content exceeds {MaxCandidateTotalBytes} bytes");
                files.Add(entry);
                if (files.Count > MaxCandidateFileCount)
                    throw CandidateLimitException($"candidate contains more than {MaxCandidateFileCount} files");
            }
        }
    }

    private static EvaluationConfigurationException CandidateLimitException(string detail) =>
        new($"Candidate tree rejected: {detail}.");

    private static void DeleteTemporaryDirectory(string tempRoot, string targetRoot)
    {
        var fullTemp = Path.GetFullPath(tempRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullTarget = Path.GetFullPath(targetRoot);
        var relative = Path.GetRelativePath(fullTemp, fullTarget);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Temporary cleanup target escapes the evaluator workspace.");
        if (!Directory.Exists(fullTarget))
            return;
        try { Directory.Delete(fullTarget, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string ResolveRepoPath(string repositoryRoot, string relativePath)
    {
        var fullRoot = Path.GetFullPath(repositoryRoot);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new EvaluationConfigurationException("A corpus path escapes the repository root.");
        return fullPath;
    }

    private static string ResolvePackagePath(string packageRoot, string relativePath)
    {
        var root = Path.GetFullPath(packageRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new EvaluationConfigurationException("A mutation path escapes the candidate package.");
        return fullPath;
    }

    private static int CountOccurrences(string input, string value)
    {
        if (value.Length == 0)
            return 0;
        var count = 0;
        var index = 0;
        while ((index = input.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string? ParseBuiltArtifact(CapturedCommand capture)
    {
        if (!IsSuccessful(capture))
            return null;
        var output = Decode(capture.StandardOutput);
        const string prefix = "Built executable: ";
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return line is null ? null : line[prefix.Length..].Trim();
    }

    private static async Task<IReadOnlyList<ArtifactIdentity>> CopyArtifactTreeAsync(
        string artifactRoot,
        string resultsRoot,
        string scenarioId,
        string artifactKind)
    {
        var destinationRoot = Path.Combine(resultsRoot, "artifacts", CompilerRunner.SafeSegment(scenarioId), CompilerRunner.SafeSegment(artifactKind));
        Directory.CreateDirectory(destinationRoot);
        var results = new List<ArtifactIdentity>();
        foreach (var source in EnumerateArtifactFiles(artifactRoot).OrderBy(path => Path.GetRelativePath(artifactRoot, path).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(artifactRoot, source);
            var target = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var bytes = await File.ReadAllBytesAsync(source);
            await File.WriteAllBytesAsync(target, bytes);
            results.Add(new ArtifactIdentity(
                Path.GetRelativePath(resultsRoot, target).Replace(Path.DirectorySeparatorChar, '/'),
                CompilerRunner.Sha256(bytes),
                bytes.LongLength));
        }
        return results;
    }

    private static IEnumerable<string> EnumerateArtifactFiles(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                foreach (var nested in EnumerateArtifactFiles(entry))
                    yield return nested;
            }
            else
            {
                yield return entry;
            }
        }
    }

    private static bool VerifyReceipt(string buildDirectory, out bool detail)
    {
        detail = false;
        using var receipt = ReadJsonFile(Path.Combine(buildDirectory, "build-receipt.json"));
        if (receipt is null || GetInt(receipt.RootElement, "schema_version") != 4
            || !receipt.RootElement.TryGetProperty("package_graph", out var packageGraph)
            || packageGraph.ValueKind != JsonValueKind.Array
            || !packageGraph.EnumerateArray().Any(package =>
                package.TryGetProperty("identity", out var identity)
                && TryGetString(identity, "path", out var path)
                && path == "."
                && identity.TryGetProperty("source", out var source)
                && TryGetString(source, "kind", out var sourceKind)
                && sourceKind == "root")
            || !receipt.RootElement.TryGetProperty("managed_adapters", out var managedAdapters)
            || managedAdapters.ValueKind != JsonValueKind.Array
            || !receipt.RootElement.TryGetProperty("artifacts", out var artifacts)
            || artifacts.ValueKind != JsonValueKind.Array)
            return false;

        if (!receipt.RootElement.TryGetProperty("build", out var build)
            || build.ValueKind != JsonValueKind.Object
            || !TryGetString(build, "mode", out var buildMode)
            || buildMode != "managed"
            || !build.TryGetProperty("runtime_identifier", out var runtimeIdentifier)
            || runtimeIdentifier.ValueKind != JsonValueKind.Null
            || !receipt.RootElement.TryGetProperty("performed_checks", out var performedChecks)
            || performedChecks.ValueKind != JsonValueKind.Array)
            return false;

        var expectedChecks = new List<string> { "compiler.package_graph_resolve" };
        var lockWasRequired = packageGraph.EnumerateArray().Any(package =>
                package.TryGetProperty("dependencies", out var dependencies)
                && dependencies.ValueKind == JsonValueKind.Array
                && dependencies.GetArrayLength() != 0)
            || managedAdapters.GetArrayLength() != 0;
        if (lockWasRequired)
            expectedChecks.Add("compiler.package_lock_validate");
        expectedChecks.Add("compiler.package_sources_parse");
        expectedChecks.Add("compiler.semantic_check");
        expectedChecks.Add("generated.managed_build");
        var actualChecks = performedChecks.EnumerateArray()
            .Select(check => check.ValueKind == JsonValueKind.String ? check.GetString() : null)
            .ToArray();
        if (!actualChecks.SequenceEqual(expectedChecks, StringComparer.Ordinal))
            return false;

        foreach (var artifact in artifacts.EnumerateArray())
        {
            if (!TryGetString(artifact, "path", out var relativePath)
                || !TryGetString(artifact, "sha256", out var expectedHash)
                || relativePath.Contains('\\')
                || Path.IsPathRooted(relativePath)
                || relativePath.Split('/').Any(segment => segment is "" or "." or ".."))
                return false;
            var fullPath = Path.GetFullPath(Path.Combine(buildDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(fullPath) || (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                return false;
            if (!string.Equals(CompilerRunner.Sha256(File.ReadAllBytes(fullPath)), expectedHash, StringComparison.Ordinal))
                return false;
        }

        detail = artifacts.GetArrayLength() > 0;
        return detail;
    }

    private static bool HasCommand(string? buildDirectory, string commandName, string capability)
    {
        if (buildDirectory is null)
            return false;
        using var schema = ReadJsonFile(Path.Combine(buildDirectory, "command-schema.json"));
        if (schema is null || GetInt(schema.RootElement, "schema_version") != 5
            || !schema.RootElement.TryGetProperty("commands", out var commands)
            || commands.ValueKind != JsonValueKind.Array)
            return false;
        return commands.EnumerateArray().Any(command => TryGetString(command, "name", out var name)
            && name == commandName && JsonArrayContains(command, "capabilities", capability));
    }

    private static bool VerifyOpenApi(string path)
    {
        using var openApi = ReadJsonFile(path);
        if (openApi is null || !TryGetString(openApi.RootElement, "openapi", out var version) || version != "3.1.0"
            || !openApi.RootElement.TryGetProperty("paths", out var paths))
            return false;
        return HasStatus(paths, "/health", "get", 200)
            && HasStatus(paths, "/api/greeting", "get", 200)
            && HasStatus(paths, "/api/greeting", "get", 404)
            && HasStatus(paths, "/api/greeting", "post", 201)
            && HasStatus(paths, "/api/greeting", "post", 400)
            && HasStatus(paths, "/api/greeting/{id}", "get", 200)
            && HasOpenApiRouteParameters(paths)
            && HasStatus(paths, "/", "get", 200);
    }

    private static bool VerifyWebRequestSettings(JsonElement? api, bool isWeb)
    {
        if (api is null
            || !api.Value.TryGetProperty("max_request_body_bytes", out var maxRequestBodyBytes)
            || !api.Value.TryGetProperty("request_timeout_ms", out var requestTimeoutMs))
            return false;

        if (!isWeb)
            return maxRequestBodyBytes.ValueKind == JsonValueKind.Null
                && requestTimeoutMs.ValueKind == JsonValueKind.Null;

        return maxRequestBodyBytes.ValueKind == JsonValueKind.Number
            && maxRequestBodyBytes.TryGetInt32(out var bodyLimit)
            && bodyLimit == 1_048_576
            && requestTimeoutMs.ValueKind == JsonValueKind.Number
            && requestTimeoutMs.TryGetInt32(out var timeoutMs)
            && timeoutMs == 30_000;
    }

    private static bool VerifyApiRoutes(JsonElement? api)
    {
        if (api is null || GetInt(api.Value, "schema_version") != 13
            || !api.Value.TryGetProperty("routes", out var routes)
            || routes.ValueKind != JsonValueKind.Array)
            return false;
        return HasApiRoute(routes, "GET", "/health", 200)
            && HasApiRoute(routes, "GET", "/api/greeting", 404)
            && HasApiRoute(routes, "GET", "/api/greeting", 200)
            && HasApiRoute(routes, "GET", "/api/greeting/{id}", 200)
            && HasApiRoute(routes, "GET", "/api/greeting/{id}", 404)
            && HasApiRoute(routes, "POST", "/api/greeting", 201)
            && HasApiRoute(routes, "POST", "/api/greeting", 400)
            && HasApiRoute(routes, "GET", "/", 200);
    }

    private static bool VerifyApiRouteBindings(JsonElement? api)
    {
        if (api is null || GetInt(api.Value, "schema_version") != 13
            || !api.Value.TryGetProperty("routes", out var routes)
            || routes.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var route in routes.EnumerateArray())
        {
            if (!TryGetString(route, "method", out var method) || method != "GET"
                || !TryGetString(route, "path", out var path) || path != "/api/greeting/{id}"
                || !route.TryGetProperty("parameters", out var parameters)
                || parameters.ValueKind != JsonValueKind.Array || parameters.GetArrayLength() != 3)
                continue;

            return HasApiParameter(parameters[0], "id", "path", required: true, "i32", optional: false, handlerIndex: 0)
                && HasApiParameter(parameters[1], "name", "query", required: true, "Text", optional: false, handlerIndex: 1)
                && HasApiParameter(parameters[2], "city", "query", required: false, "Text", optional: true, handlerIndex: 2);
        }

        return false;
    }

    private static bool HasApiParameter(
        JsonElement parameter,
        string name,
        string location,
        bool required,
        string typeName,
        bool optional,
        int handlerIndex)
    {
        if (!TryGetString(parameter, "name", out var actualName) || actualName != name
            || !TryGetString(parameter, "in", out var actualLocation) || actualLocation != location
            || !parameter.TryGetProperty("required", out var requiredValue)
            || requiredValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || requiredValue.GetBoolean() != required
            || GetInt(parameter, "handler_parameter_index") != handlerIndex
            || !parameter.TryGetProperty("type", out var type))
            return false;

        if (!optional)
            return IsApiPrimitiveType(type, typeName);

        return TryGetString(type, "kind", out var kind) && kind == "option"
            && type.TryGetProperty("item", out var item)
            && IsApiPrimitiveType(item, typeName);
    }

    private static bool IsApiPrimitiveType(JsonElement type, string typeName) =>
        TryGetString(type, "kind", out var kind) && kind == "primitive"
        && TryGetString(type, "name", out var name) && name == typeName;

    private static bool HasOpenApiRouteParameters(JsonElement paths)
    {
        if (!paths.TryGetProperty("/api/greeting/{id}", out var pathItem)
            || !pathItem.TryGetProperty("get", out var operation)
            || !operation.TryGetProperty("parameters", out var parameters)
            || parameters.ValueKind != JsonValueKind.Array || parameters.GetArrayLength() != 3)
            return false;

        return HasOpenApiParameter(parameters[0], "id", "path", required: true, "integer")
            && HasOpenApiParameter(parameters[1], "name", "query", required: true, "string")
            && HasOpenApiParameter(parameters[2], "city", "query", required: false, "string");
    }

    private static bool HasOpenApiParameter(JsonElement parameter, string name, string location, bool required, string schemaType) =>
        TryGetString(parameter, "name", out var actualName) && actualName == name
        && TryGetString(parameter, "in", out var actualLocation) && actualLocation == location
        && parameter.TryGetProperty("required", out var requiredValue)
        && requiredValue.ValueKind is JsonValueKind.True or JsonValueKind.False
        && requiredValue.GetBoolean() == required
        && parameter.TryGetProperty("schema", out var schema)
        && TryGetString(schema, "type", out var actualSchemaType)
        && actualSchemaType == schemaType;

    private static bool HasApiRoute(JsonElement routes, string method, string path, int status)
    {
        foreach (var route in routes.EnumerateArray())
        {
            if (!TryGetString(route, "method", out var candidateMethod) || candidateMethod != method
                || !TryGetString(route, "path", out var candidatePath) || candidatePath != path
                || !route.TryGetProperty("responses", out var responses)
                || responses.ValueKind != JsonValueKind.Array)
                continue;
            if (responses.EnumerateArray().Any(response => GetInt(response, "status") == status))
                return true;
        }
        return false;
    }

    private static bool HasStatus(JsonElement paths, string path, string method, int status)
    {
        if (!paths.TryGetProperty(path, out var pathItem) || !pathItem.TryGetProperty(method, out var operation)
            || !operation.TryGetProperty("responses", out var responses))
            return false;
        return responses.TryGetProperty(status.ToString(System.Globalization.CultureInfo.InvariantCulture), out _);
    }

    private static string? GetInspectSymbol(EvaluationScenario scenario, JsonElement? api)
    {
        if (scenario.Smoke is { } smoke && smoke.ValueKind == JsonValueKind.Object
            && smoke.TryGetProperty("inspect_symbol", out var symbol) && symbol.ValueKind == JsonValueKind.String)
            return symbol.GetString();
        if (api is null)
            return null;
        if (GetSmokeString(scenario, "kind") == "web")
        {
            if (!api.Value.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
                return null;
            foreach (var route in routes.EnumerateArray())
                if (TryGetString(route, "method", out var method) && method == "POST"
                    && TryGetString(route, "path", out var path) && path == "/api/greeting"
                    && TryGetString(route, "handler", out var handler))
                    return handler;
            return null;
        }
        if (!api.Value.TryGetProperty("commands", out var commands) || commands.GetArrayLength() == 0)
            return null;
        return TryGetString(commands[0], "handler", out var commandHandler) ? commandHandler : null;
    }

    private static bool CheckDocumentation(string scenarioId, string readme) => scenarioId switch
    {
        "cli-policy" => readme.Contains("| Contains `REVOKE`, with or without `GRANT` | `REVOKED` |", StringComparison.Ordinal)
            && readme.Contains("| Contains `GRANT` and no `REVOKE` | `GRANTED` |", StringComparison.Ordinal)
            && readme.Contains("| Contains neither marker | `UNDECIDED` |", StringComparison.Ordinal),
        "web-greeting" => readme.Contains("| GET | `/api/greeting/{id}?name=…&city=…` | 200 JSON when ID/name and optional city match; otherwise 404; 400 for missing or invalid input |", StringComparison.Ordinal)
            && readme.Contains("| POST | `/api/greeting` | 201 JSON when saved; 400 when name or city is blank after trimming |", StringComparison.Ordinal),
        "audit-repair" => readme.Contains("`claim_only`", StringComparison.Ordinal)
            && readme.Contains("does not prove adapter behavior", StringComparison.Ordinal)
            && readme.Contains("operating-system sandbox", StringComparison.Ordinal)
            && !readme.Contains("proves adapter behavior", StringComparison.Ordinal),
        _ => false
    };

    private static string[] GetSmokeStringArray(EvaluationScenario scenario, string property)
    {
        if (scenario.Smoke is not { } smoke || smoke.ValueKind != JsonValueKind.Object
            || !smoke.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new EvaluationConfigurationException($"Scenario '{scenario.Id}' smoke.{property} must be an array.");
        return value.EnumerateArray().Select(item => item.GetString()
            ?? throw new EvaluationConfigurationException($"Scenario '{scenario.Id}' smoke.{property} contains a non-string."))
            .ToArray();
    }

    private static string? GetSmokeString(EvaluationScenario scenario, string property)
    {
        if (scenario.Smoke is not { } smoke || smoke.ValueKind != JsonValueKind.Object
            || !smoke.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    private static JsonDocument? ParseJson(byte[] content)
    {
        try { return JsonDocument.Parse(content); }
        catch (JsonException) { return null; }
    }

    private static JsonDocument? ReadJsonFile(string path)
    {
        try { return File.Exists(path) ? JsonDocument.Parse(File.ReadAllBytes(path)) : null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    private static bool DiagnosticsAreEmpty(CapturedCommand capture)
    {
        using var document = ParseJson(capture.StandardOutput);
        return document is not null
            && document.RootElement.TryGetProperty("diagnostics", out var diagnostics)
            && diagnostics.ValueKind == JsonValueKind.Array
            && diagnostics.GetArrayLength() == 0;
    }

    private static IReadOnlyList<string> ReadDiagnosticCodes(CapturedCommand capture)
    {
        using var document = ParseJson(capture.StandardOutput);
        if (document is null || !document.RootElement.TryGetProperty("diagnostics", out var diagnostics)
            || diagnostics.ValueKind != JsonValueKind.Array)
            return [];
        var codes = new List<string>();
        foreach (var diagnostic in diagnostics.EnumerateArray())
            if (TryGetString(diagnostic, "code", out var code))
                codes.Add(code);
            else if (TryGetString(diagnostic, "id", out code))
                codes.Add(code);
        return codes;
    }

    private static bool IsSuccessful(CapturedCommand capture) => capture.ExitCode == 0
        && !capture.TimedOut
        && !capture.StandardOutputTruncated
        && !capture.StandardErrorTruncated;

    private static bool JsonArrayContains(JsonElement root, string property, string expected)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return false;
        return JsonArrayContains(array, expected);
    }

    private static bool JsonArrayContains(JsonElement array, string expected) => array.ValueKind == JsonValueKind.Array
        && array.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == expected);

    private static bool JsonTextContains(JsonElement root, string expected) => root.ToString().Contains(expected, StringComparison.Ordinal);

    private static int GetInt(JsonElement? root, string property) => root is { } value ? GetInt(value, property) : -1;

    private static int GetInt(JsonElement root, string property) => root.TryGetProperty(property, out var value)
        && value.TryGetInt32(out var integer) ? integer : -1;

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var propertyValue)
            && propertyValue.ValueKind == JsonValueKind.String)
        {
            value = propertyValue.GetString()!;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static string Decode(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static void AddCheck(
        ICollection<AcceptanceCheck> checks,
        string id,
        bool passed,
        string detail,
        IReadOnlyList<string> evidence) => checks.Add(new AcceptanceCheck(id, passed, detail, evidence));

    private static IReadOnlyList<string> Evidence(params (CapturedCommand Capture, CommandExecution Record)[] runs) =>
        runs.SelectMany(run => new[] { run.Record.Stdout.Path, run.Record.Stderr.Path }).ToArray();

    private static IReadOnlyList<string> Evidence((CapturedCommand Capture, CommandExecution Record) run) =>
        [run.Record.Stdout.Path, run.Record.Stderr.Path];

    private static IReadOnlyList<string> EvidenceFor(IEnumerable<CommandExecution> commands, string commandId)
    {
        var command = commands.FirstOrDefault(value => value.Id == commandId);
        return command is null ? [] : [command.Stdout.Path, command.Stderr.Path];
    }

    private static IReadOnlyList<string> ArtifactEvidence(IEnumerable<ArtifactIdentity> artifacts, string scenarioId, string suffix) => artifacts
        .Where(value => value.Path.StartsWith($"artifacts/{CompilerRunner.SafeSegment(scenarioId)}/", StringComparison.Ordinal)
            && value.Path.EndsWith(suffix, StringComparison.Ordinal))
        .Select(value => value.Path)
        .ToArray();

    private static async Task<string> WriteEvidenceAsync(string resultsRoot, string relativePath, string content)
    {
        var safePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(resultsRoot, safePath));
        var relative = Path.GetRelativePath(resultsRoot, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new EvaluationConfigurationException("Evidence path escapes the results directory.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, new UTF8Encoding(false));
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void EnsureNoAbsolutePathLeak(byte[] resultBytes, string repositoryRoot, string tempRoot, EvaluationOptions options)
    {
        var json = Encoding.UTF8.GetString(resultBytes);
        var forbidden = new[] { Path.GetFullPath(repositoryRoot), Path.GetFullPath(tempRoot), Path.GetFullPath(options.ResultsDirectory) }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        foreach (var absolutePath in forbidden)
            if (json.Contains(absolutePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The result document contains an absolute filesystem path.");
        using var document = JsonDocument.Parse(resultBytes);
        if (ContainsAbsolutePath(document.RootElement))
            throw new InvalidOperationException("The result document contains an absolute filesystem path.");
    }

    private static bool ContainsAbsolutePath(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => Path.IsPathRooted(element.GetString() ?? string.Empty),
        JsonValueKind.Array => element.EnumerateArray().Any(ContainsAbsolutePath),
        JsonValueKind.Object => element.EnumerateObject().Any(property => ContainsAbsolutePath(property.Value)),
        _ => false
    };

    private static bool VerifyAuditRootInputs(JsonElement? audit) => audit is not null
        && audit.Value.TryGetProperty("packages", out var packages)
        && packages.ValueKind == JsonValueKind.Array
        && packages.EnumerateArray().Any(package =>
            package.TryGetProperty("identity", out var identity)
            && TryGetString(identity, "path", out var path)
            && path == "."
            && identity.TryGetProperty("source", out var source)
            && TryGetString(source, "kind", out var sourceKind)
            && sourceKind == "root"
            && package.TryGetProperty("inputs", out var inputs)
            && inputs.ValueKind == JsonValueKind.Array
            && inputs.EnumerateArray().Any(input => TryGetString(input, "kind", out var kind) && kind == "manifest"));

    private static async Task<WebSmokeResult> RunWebSmokeAsync(
        EvaluationScenario scenario,
        string packageRoot,
        string tempRoot,
        CompilerRunner runner,
        string commandScenarioId,
        string resultsRoot,
        ICollection<ArtifactIdentity>? artifacts,
        bool verifyPersistence,
        string? evidencePrefix = null)
    {
        var commands = new List<CommandExecution>();
        var transcript = new StringBuilder();
        var prefix = evidencePrefix ?? $"probes/{scenario.Id}/web-runtime";
        var databaseDirectory = Path.Combine(tempRoot, "web-database");
        Directory.CreateDirectory(databaseDirectory);
        var databasePath = Path.Combine(databaseDirectory, "greeting.sqlite3");
        var url = $"http://127.0.0.1:{GetFreePort()}";
        var healthPath = GetSmokeString(scenario, "health_path") ?? "/health";
        var startup = await StartWebServerAsync(
            scenario,
            packageRoot,
            runner,
            commandScenarioId,
            resultsRoot,
            databasePath,
            url,
            commands,
            "run-http-1");

        var healthy = false;
        var initiallyMissing = false;
        var trimmed = false;
        var blankPreserves = false;
        var safeHtml = false;
        var routeBindings = false;
        var persisted = !verifyPersistence;
        try
        {
            if (startup.Server is not null)
            {
                using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(3) };
                healthy = await WaitForHealthAsync(client, healthPath, startup.Server.Process);
                transcript.AppendLine($"health={healthy}");
                if (healthy)
                {
                    var before = await GetStatusOnlyAsync(client, "/api/greeting");
                    initiallyMissing = before == (int)HttpStatusCode.NotFound;
                    transcript.AppendLine($"initial_greeting_status={before}");

                    var save = await PostGreetingAsync(client, "  Ada  ", "  Paris  ");
                    transcript.AppendLine($"trim_save_status={save.StatusCode}");
                    trimmed = save.StatusCode == (int)HttpStatusCode.Created && !save.Truncated
                        && GreetingEquals(save.Body, "Ada", "Paris");

                    var invalid = await PostGreetingAsync(client, "  ", "Changed");
                    transcript.AppendLine($"blank_save_status={invalid.StatusCode}");
                    var afterInvalid = await GetBodyAsync(client, "/api/greeting");
                    blankPreserves = invalid.StatusCode == (int)HttpStatusCode.BadRequest && !invalid.Truncated
                        && afterInvalid.StatusCode == (int)HttpStatusCode.OK && !afterInvalid.Truncated
                        && GreetingEquals(afterInvalid.Body, "Ada", "Paris");
                    transcript.AppendLine($"blank_preserves={blankPreserves}");

                    var dynamicLookup = await GetBodyAsync(client, "/api/greeting/1?name=Ada");
                    var dynamicLookupWithOptionalCity = await GetBodyAsync(client, "/api/greeting/1?name=Ada&city=Paris");
                    var missingRequiredQuery = await GetBodyAsync(client, "/api/greeting/1?city=Paris");
                    var invalidPathValue = await GetBodyAsync(client, "/api/greeting/not-an-int?name=Ada");
                    routeBindings = dynamicLookup.StatusCode == (int)HttpStatusCode.OK && !dynamicLookup.Truncated
                        && GreetingEquals(dynamicLookup.Body, "Ada", "Paris")
                        && dynamicLookupWithOptionalCity.StatusCode == (int)HttpStatusCode.OK && !dynamicLookupWithOptionalCity.Truncated
                        && GreetingEquals(dynamicLookupWithOptionalCity.Body, "Ada", "Paris")
                        && IsStableInvalidRequest(missingRequiredQuery)
                        && IsStableInvalidRequest(invalidPathValue);
                    transcript.AppendLine($"dynamic_lookup_status={dynamicLookup.StatusCode}");
                    transcript.AppendLine($"dynamic_lookup_optional_city_status={dynamicLookupWithOptionalCity.StatusCode}");
                    transcript.AppendLine($"missing_required_query_status={missingRequiredQuery.StatusCode}");
                    transcript.AppendLine($"invalid_path_value_status={invalidPathValue.StatusCode}");
                    transcript.AppendLine($"route_bindings={routeBindings}");

                    const string adversarialName = "<b>safe & sound</b>";
                    const string adversarialCity = "<img src=x?m=1&n=2>";
                    var unsafeSave = await PostGreetingAsync(client, adversarialName, adversarialCity);
                    var pageResponse = await GetBodyAsync(client, "/");
                    var escapedName = "&lt;b&gt;safe &amp; sound&lt;/b&gt;";
                    var escapedCity = "&lt;img src=x?m=1&amp;n=2&gt;";
                    safeHtml = unsafeSave.StatusCode == (int)HttpStatusCode.Created && !unsafeSave.Truncated
                        && GreetingEquals(unsafeSave.Body, adversarialName, adversarialCity)
                        && pageResponse.StatusCode == (int)HttpStatusCode.OK && !pageResponse.Truncated
                        && pageResponse.Body.Contains(escapedName, StringComparison.Ordinal)
                        && pageResponse.Body.Contains(escapedCity, StringComparison.Ordinal)
                        && !pageResponse.Body.Contains(adversarialName, StringComparison.Ordinal)
                        && !pageResponse.Body.Contains(adversarialCity, StringComparison.Ordinal);
                    transcript.AppendLine($"safe_html={safeHtml}");
                    transcript.AppendLine($"page_status={pageResponse.StatusCode}");
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            transcript.AppendLine($"probe_error={exception.GetType().Name}");
        }
        finally
        {
            if (startup.Server is not null)
                commands.Add(await StopWebServerAsync(startup.Server, runner, commandScenarioId, "run-http-1"));
        }

        if (healthy && verifyPersistence)
        {
            var restart = await StartWebServerAsync(
                scenario,
                packageRoot,
                runner,
                commandScenarioId,
                resultsRoot,
                databasePath,
                $"http://127.0.0.1:{GetFreePort()}",
                commands,
                "run-http-2");
            try
            {
                if (restart.Server is not null)
                {
                    using var client = new HttpClient
                    {
                        BaseAddress = new Uri(restart.Server.Url),
                        Timeout = TimeSpan.FromSeconds(3)
                    };
                    var restartHealthy = await WaitForHealthAsync(client, healthPath, restart.Server.Process);
                    var afterRestart = await GetBodyAsync(client, "/api/greeting");
                    persisted = restartHealthy && afterRestart.StatusCode == (int)HttpStatusCode.OK
                        && !afterRestart.Truncated
                        && GreetingEquals(afterRestart.Body, "<b>safe & sound</b>", "<img src=x?m=1&n=2>");
                    transcript.AppendLine($"restart_health={restartHealthy}");
                    transcript.AppendLine($"persisted_status={afterRestart.StatusCode}");
                    transcript.AppendLine($"persisted={persisted}");
                }
                else
                {
                    persisted = false;
                    transcript.AppendLine("restart_health=false");
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
            {
                persisted = false;
                transcript.AppendLine($"restart_error={exception.GetType().Name}");
            }
            finally
            {
                if (restart.Server is not null)
                    commands.Add(await StopWebServerAsync(restart.Server, runner, commandScenarioId, "run-http-2"));
            }
        }

        var processOutputClean = commands.All(command => !command.TimedOut
            && !command.StandardOutputTruncated
            && !command.StandardErrorTruncated);
        var trimmedAndPersisted = healthy && initiallyMissing && trimmed && blankPreserves && persisted && processOutputClean;
        safeHtml &= processOutputClean;
        routeBindings &= processOutputClean;
        var relativeEvidence = await WriteEvidenceAsync(resultsRoot, prefix + ".txt", transcript.ToString());
        if (artifacts is not null)
        {
            var evidenceFullPath = Path.Combine(resultsRoot, relativeEvidence.Replace('/', Path.DirectorySeparatorChar));
            var bytes = await File.ReadAllBytesAsync(evidenceFullPath);
            artifacts.Add(new ArtifactIdentity(relativeEvidence, CompilerRunner.Sha256(bytes), bytes.LongLength));
        }
        var paths = new List<string> { relativeEvidence };
        paths.AddRange(commands.SelectMany(command => new[] { command.Stdout.Path, command.Stderr.Path }));
        return new WebSmokeResult(trimmedAndPersisted, safeHtml, routeBindings, commands, paths);
    }

    private static async Task<WebServerStart> StartWebServerAsync(
        EvaluationScenario scenario,
        string packageRoot,
        CompilerRunner runner,
        string commandScenarioId,
        string resultsRoot,
        string databasePath,
        string url,
        ICollection<CommandExecution> commands,
        string commandId)
    {
        var hostArgs = GetSmokeStringArray(scenario, "host_args");
        if (!hostArgs.Contains("<loopback>", StringComparer.Ordinal))
            throw new EvaluationConfigurationException($"Scenario '{scenario.Id}' smoke.host_args must contain <loopback>.");
        var replacedArgs = hostArgs.Select(argument => argument.Replace("<loopback>", url, StringComparison.Ordinal)).ToArray();
        string[] compilerArgs = ["run", packageRoot, "--", .. replacedArgs];
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HOB_SQLITE_PATH"] = databasePath
        };
        var startInfo = CompilerRunner.CreateStartInfo(packageRoot, compilerArgs, environment, runner.DotnetPath, runner.CompilerPath);
        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                return new WebServerStart(null);
            }
            var stdout = CompilerRunner.ReadBoundedAsync(process.StandardOutput.BaseStream);
            var stderr = CompilerRunner.ReadBoundedAsync(process.StandardError.BaseStream);
            var server = new WebServerSession(process, url, compilerArgs, stdout, stderr);
            var healthPath = GetSmokeString(scenario, "health_path") ?? "/health";
            using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(2) };
            if (await WaitForHealthAsync(client, healthPath, process))
                return new WebServerStart(server);
            commands.Add(await StopWebServerAsync(server, runner, commandScenarioId, commandId));
            return new WebServerStart(null);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            if (process is not null)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                process.Dispose();
            }
            var capture = new CapturedCommand(null, false,
                Array.Empty<byte>(), Encoding.UTF8.GetBytes($"web host failed to start ({exception.GetType().Name})."), false, false);
            commands.Add(await runner.RecordCaptureAsync(commandScenarioId, commandId, compilerArgs, capture));
            return new WebServerStart(null);
        }
    }

    private static async Task<CommandExecution> StopWebServerAsync(
        WebServerSession server,
        CompilerRunner runner,
        string commandScenarioId,
        string commandId)
    {
        var timedOut = false;
        try
        {
            if (!server.Process.HasExited)
                server.Process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        try
        {
            await server.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            timedOut = true;
            try { server.Process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            try { await server.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
        }

        var stdout = await FinishCaptureAsync(server.StandardOutput);
        var stderr = await FinishCaptureAsync(server.StandardError);
        var capture = new CapturedCommand(
            server.Process.HasExited ? server.Process.ExitCode : null,
            timedOut,
            stdout.Bytes,
            stderr.Bytes,
            stdout.Truncated,
            stderr.Truncated);
        var record = await runner.RecordCaptureAsync(commandScenarioId, commandId, server.Arguments, capture);
        server.Process.Dispose();
        return record;
    }

    private static async Task<BoundedOutput> FinishCaptureAsync(Task<BoundedOutput> captureTask)
    {
        try { return await captureTask.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException)
        {
            return new BoundedOutput(Encoding.UTF8.GetBytes("[output capture incomplete after process cleanup]\n"), true);
        }
    }

    private static async Task<bool> WaitForHealthAsync(HttpClient client, string healthPath, Process process)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline && !process.HasExited)
        {
            try
            {
                if (await GetStatusOnlyAsync(client, healthPath) == (int)HttpStatusCode.OK)
                    return true;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        return false;
    }

    private static async Task<int> GetStatusOnlyAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        return (int)response.StatusCode;
    }

    private static Task<ProbeHttpResponse> GetBodyAsync(HttpClient client, string path) =>
        SendAndReadBodyAsync(client, new HttpRequestMessage(HttpMethod.Get, path));

    private static bool IsStableInvalidRequest(ProbeHttpResponse response) =>
        response.StatusCode == (int)HttpStatusCode.BadRequest
        && !response.Truncated
        && response.Body == "{\"error\":\"invalid_request\"}";

    private static Task<ProbeHttpResponse> PostGreetingAsync(HttpClient client, string name, string city)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/greeting")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { name, city }),
                Encoding.UTF8,
                "application/json")
        };
        return SendAndReadBodyAsync(client, request);
    }

    private static async Task<ProbeHttpResponse> SendAndReadBodyAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request)
        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
        {
            var (body, truncated) = await ReadBoundedHttpBodyAsync(response.Content);
            return new ProbeHttpResponse((int)response.StatusCode, body, truncated);
        }
    }

    private static async Task<(string Body, bool Truncated)> ReadBoundedHttpBodyAsync(HttpContent content)
    {
        const int maximumBytes = 1024 * 1024;
        var buffer = new byte[16 * 1024];
        using var retained = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var stream = await content.ReadAsStreamAsync(timeout.Token);
        while (retained.Length <= maximumBytes)
        {
            var remainingWithProbeByte = maximumBytes - (int)retained.Length + 1;
            var requested = Math.Min(buffer.Length, remainingWithProbeByte);
            var read = await stream.ReadAsync(buffer.AsMemory(0, requested), timeout.Token);
            if (read == 0)
                return (Encoding.UTF8.GetString(retained.ToArray()), false);
            var remaining = maximumBytes - (int)retained.Length;
            var retainedCount = Math.Min(read, remaining);
            if (retainedCount > 0)
                retained.Write(buffer, 0, retainedCount);
            if (read > remaining)
                return (Encoding.UTF8.GetString(retained.ToArray()), true);
        }
        return (Encoding.UTF8.GetString(retained.ToArray()), true);
    }

    private static bool GreetingEquals(string json, string name, string city)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryGetString(document.RootElement, "name", out var actualName) && actualName == name
                && TryGetString(document.RootElement, "city", out var actualCity) && actualCity == city;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record WebSmokeResult(
        bool TrimmedAndPersisted,
        bool SafeHtml,
        bool RouteBindings,
        IReadOnlyList<CommandExecution> Commands,
        IReadOnlyList<string> EvidencePaths);

    private sealed record ProbeHttpResponse(int StatusCode, string Body, bool Truncated);

    private sealed record WebServerStart(WebServerSession? Server);

    private sealed record WebServerSession(
        Process Process,
        string Url,
        string[] Arguments,
        Task<BoundedOutput> StandardOutput,
        Task<BoundedOutput> StandardError);

}
