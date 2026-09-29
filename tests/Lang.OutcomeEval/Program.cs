using System.Text.Json;

namespace Lang.OutcomeEval;

internal static class Program
{
    private static readonly string[] RequiredScenarioIds = ["audit-repair", "cli-policy", "web-greeting"];

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            var repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot is null)
                throw new EvaluationConfigurationException("Could not locate eval/corpus.json from the current directory or application path.");

            var corpus = ReadCorpus(repositoryRoot);
            ValidateCandidateOverrides(options, corpus);
            var dotnetPath = GetPinnedDotnetPath(repositoryRoot);

            return await OutcomeEvaluator.RunAsync(options, repositoryRoot, dotnetPath, corpus);
        }
        catch (EvaluationConfigurationException exception)
        {
            Console.Error.WriteLine($"CONFIG_ERROR: {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EVALUATION_ERROR: {exception.Message}");
            return 1;
        }
    }

    private static EvaluationOptions ParseOptions(string[] args)
    {
        string? compiler = null;
        string? results = null;
        string? model = null;
        string? revision = null;
        string? tool = null;
        var verifySeeds = false;
        var candidates = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--verify-seeds")
            {
                if (verifySeeds)
                    throw new EvaluationConfigurationException("--verify-seeds may be specified only once.");
                verifySeeds = true;
                continue;
            }

            if (argument is not ("--compiler" or "--results" or "--agent-model" or "--agent-revision" or "--agent-tool" or "--candidate"))
                throw new EvaluationConfigurationException($"Unknown argument: {argument}");
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
                throw new EvaluationConfigurationException($"Argument {argument} requires a nonempty value.");

            var value = args[index];
            switch (argument)
            {
                case "--compiler": compiler = SetOnce(compiler, value, argument); break;
                case "--results": results = SetOnce(results, value, argument); break;
                case "--agent-model": model = SetOnce(model, value, argument); break;
                case "--agent-revision": revision = SetOnce(revision, value, argument); break;
                case "--agent-tool": tool = SetOnce(tool, value, argument); break;
                case "--candidate":
                    var separator = value.IndexOf('=');
                    if (separator <= 0 || separator == value.Length - 1)
                        throw new EvaluationConfigurationException("--candidate must use <scenario-id>=<path>.");
                    var scenarioId = value[..separator];
                    var candidatePath = value[(separator + 1)..];
                    if (!candidates.TryAdd(scenarioId, candidatePath))
                        throw new EvaluationConfigurationException($"A candidate override was supplied more than once for scenario '{scenarioId}'.");
                    break;
            }
        }

        if (compiler is null || results is null || model is null || revision is null || tool is null)
            throw new EvaluationConfigurationException(
                "Required arguments: --compiler <dll> --results <dir> --agent-model <id> --agent-revision <id> --agent-tool <id>.");
        ValidateIdentityValue(model, "--agent-model");
        ValidateIdentityValue(revision, "--agent-revision");
        ValidateIdentityValue(tool, "--agent-tool");

        var compilerPath = Path.GetFullPath(compiler);
        if (!File.Exists(compilerPath))
            throw new EvaluationConfigurationException($"Compiler DLL does not exist: {compiler}");
        if (!string.Equals(Path.GetExtension(compilerPath), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new EvaluationConfigurationException("--compiler must identify a compiler DLL.");

        return new EvaluationOptions(
            compilerPath,
            Path.GetFullPath(results),
            model,
            revision,
            tool,
            verifySeeds,
            candidates);

        static string SetOnce(string? current, string value, string name)
        {
            if (current is not null)
                throw new EvaluationConfigurationException($"{name} may be specified only once.");
            return value;
        }

        static void ValidateIdentityValue(string value, string name)
        {
            if (value.Length > 160
                || value.Any(char.IsControl)
                || Path.IsPathRooted(value)
                || value.Contains('/')
                || value.Contains('\\'))
                throw new EvaluationConfigurationException($"{name} must be a short identifier, not a path.");
        }
    }

    private static CorpusManifest ReadCorpus(string repositoryRoot)
    {
        var corpusPath = ResolveRepositoryFile(repositoryRoot, "eval/corpus.json");
        if (!File.Exists(corpusPath))
            throw new EvaluationConfigurationException("The evaluation corpus is missing: eval/corpus.json");

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(corpusPath);
        }
        catch (IOException exception)
        {
            throw new EvaluationConfigurationException("Could not read eval/corpus.json.", exception);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var schemaVersion = root.GetProperty("schema_version").GetInt32();
            if (schemaVersion != 1)
                throw new EvaluationConfigurationException($"Unsupported corpus schema version: {schemaVersion}");
            var scenariosElement = root.GetProperty("scenarios");
            if (scenariosElement.ValueKind != JsonValueKind.Array)
                throw new EvaluationConfigurationException("Corpus scenarios must be an array.");

            var scenarios = new List<EvaluationScenario>();
            foreach (var scenarioElement in scenariosElement.EnumerateArray())
            {
                var id = RequiredString(scenarioElement, "id");
                var candidateRoot = NormalizeManifestPath(RequiredString(scenarioElement, "candidate_root"), $"scenario {id} candidate_root");
                string? starterRoot = null;
                if (scenarioElement.TryGetProperty("starter_root", out var starterRootElement))
                {
                    if (starterRootElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(starterRootElement.GetString()))
                        throw new EvaluationConfigurationException($"Scenario '{id}' starter_root must be a nonempty relative path.");
                    starterRoot = NormalizeManifestPath(starterRootElement.GetString()!, $"scenario {id} starter_root");
                }
                var taskFile = NormalizeManifestPath(RequiredString(scenarioElement, "task_file"), $"scenario {id} task_file");
                var checkIds = ReadStringArray(scenarioElement, "check_ids");
                var mutations = ReadMutations(scenarioElement, checkIds);
                JsonElement? smoke = scenarioElement.TryGetProperty("smoke", out var smokeElement)
                    ? smokeElement.Clone()
                    : null;
                JsonElement? fixtureAgent = scenarioElement.TryGetProperty("fixture_agent", out var fixtureAgentElement)
                    ? fixtureAgentElement.Clone()
                    : null;

                var candidatePath = ResolveRepositoryFile(repositoryRoot, candidateRoot);
                var taskPath = ResolveRepositoryFile(repositoryRoot, taskFile);
                if (starterRoot is not null && !Directory.Exists(ResolveRepositoryFile(repositoryRoot, starterRoot)))
                    throw new EvaluationConfigurationException($"Scenario '{id}' starter root is missing: {starterRoot}");
                if (!Directory.Exists(candidatePath))
                    throw new EvaluationConfigurationException($"Scenario '{id}' candidate root is missing: {candidateRoot}");
                if (!File.Exists(taskPath))
                    throw new EvaluationConfigurationException($"Scenario '{id}' task file is missing: {taskFile}");
                scenarios.Add(new EvaluationScenario(
                    id,
                    RequiredString(scenarioElement, "version"),
                    candidateRoot,
                    starterRoot,
                    taskFile,
                    checkIds,
                    mutations,
                    smoke,
                    fixtureAgent));
            }

            var duplicateId = scenarios.GroupBy(scenario => scenario.Id, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() != 1);
            if (duplicateId is not null)
                throw new EvaluationConfigurationException($"Corpus scenario ID is duplicated: {duplicateId.Key}");
            if (!RequiredScenarioIds.Order(StringComparer.Ordinal).SequenceEqual(
                    scenarios.Select(scenario => scenario.Id).Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new EvaluationConfigurationException("The corpus must contain exactly cli-policy, web-greeting, and audit-repair.");

            return new CorpusManifest(schemaVersion, scenarios.OrderBy(scenario => scenario.Id, StringComparer.Ordinal).ToArray(), Sha256(bytes));
        }
        catch (EvaluationConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new EvaluationConfigurationException("The evaluation corpus is malformed or missing a required field.", exception);
        }
    }

    private static IReadOnlyList<EvaluationMutation> ReadMutations(JsonElement scenario, IReadOnlyList<string> checkIds)
    {
        if (!scenario.TryGetProperty("mutations", out var mutationsElement) || mutationsElement.ValueKind != JsonValueKind.Array)
            throw new EvaluationConfigurationException("Each scenario must define a mutations array, even when empty.");

        var mutations = new List<EvaluationMutation>();
        foreach (var mutationElement in mutationsElement.EnumerateArray())
        {
            var id = RequiredString(mutationElement, "id");
            var expectedCheckId = RequiredString(mutationElement, "expected_check_id");
            if (!checkIds.Contains(expectedCheckId, StringComparer.Ordinal))
                throw new EvaluationConfigurationException($"Mutation '{id}' expects undeclared check ID '{expectedCheckId}'.");
            var files = ReadStringArray(mutationElement, "files")
                .Select(path => NormalizeManifestPath(path, $"mutation {id} file"))
                .ToArray();
            if (!mutationElement.TryGetProperty("replace_exactly", out var replacementsElement)
                || replacementsElement.ValueKind != JsonValueKind.Array)
                throw new EvaluationConfigurationException($"Mutation '{id}' must contain a replace_exactly array.");
            var replacements = replacementsElement.EnumerateArray().Select(replacement => new ExactReplacement(
                NormalizeManifestPath(RequiredString(replacement, "path"), $"mutation {id} replacement path"),
                RequiredString(replacement, "old"),
                RequiredStringAllowEmpty(replacement, "new"))).ToArray();
            if (replacements.Length == 0)
                throw new EvaluationConfigurationException($"Mutation '{id}' must contain at least one exact replacement.");

            mutations.Add(new EvaluationMutation(
                id,
                RequiredString(mutationElement, "kind"),
                files,
                replacements,
                expectedCheckId));
        }

        var duplicateId = mutations.GroupBy(mutation => mutation.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicateId is not null)
            throw new EvaluationConfigurationException($"Scenario mutation ID is duplicated: {duplicateId.Key}");
        return mutations.OrderBy(mutation => mutation.Id, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string property)
    {
        var element = parent.GetProperty(property);
        if (element.ValueKind != JsonValueKind.Array)
            throw new EvaluationConfigurationException($"Corpus property '{property}' must be an array.");
        var values = element.EnumerateArray().Select(value =>
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new EvaluationConfigurationException($"Corpus property '{property}' must contain nonempty strings.");
            return value.GetString()!;
        }).ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new EvaluationConfigurationException($"Corpus property '{property}' contains duplicates.");
        return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static string RequiredString(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new EvaluationConfigurationException($"Corpus property '{property}' must be a nonempty string.");
        return value.GetString()!;
    }

    private static string RequiredStringAllowEmpty(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new EvaluationConfigurationException($"Corpus property '{property}' must be a string.");
        return value.GetString()!;
    }

    private static string NormalizeManifestPath(string path, string description)
    {
        if (path.Contains('\\') || Path.IsPathRooted(path))
            throw new EvaluationConfigurationException($"{description} must be a repo-relative forward-slash path.");
        var segments = path.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
            throw new EvaluationConfigurationException($"{description} contains an unsafe path segment.");
        return string.Join('/', segments);
    }

    private static string ResolveRepositoryFile(string repositoryRoot, string relativePath)
    {
        var fullRoot = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new EvaluationConfigurationException($"Repository path escapes the checkout: {relativePath}");
        return fullPath;
    }

    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "eval", "corpus.json"))
                    && File.Exists(Path.Combine(directory.FullName, "src", "Lang", "Lang.csproj")))
                    return directory.FullName;
            }
        }
        return null;
    }

    private static string GetPinnedDotnetPath(string repositoryRoot)
    {
        var local = Path.Combine(
            repositoryRoot,
            ".dotnet",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        // global.json pins the SDK used by the PATH fallback in CI; prefer the
        // repository-local installation when developers have provisioned one.
        return File.Exists(local) ? local : "dotnet";
    }

    private static void ValidateCandidateOverrides(EvaluationOptions options, CorpusManifest corpus)
    {
        var ids = corpus.Scenarios.Select(scenario => scenario.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, path) in options.CandidateOverrides)
        {
            if (!ids.Contains(id))
                throw new EvaluationConfigurationException($"Candidate override references unknown scenario ID '{id}'.");
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
                throw new EvaluationConfigurationException($"Candidate override directory does not exist for '{id}'.");
        }
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}
