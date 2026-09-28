using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record AuditPackageIdentity(string Name, string Version, string Path);

internal sealed record AuditFunctionIdentity(AuditPackageIdentity? Package, string Module, string Name);

internal sealed record AuditPackageInputRecord(
    AuditPackageIdentity Package,
    string Kind,
    string Path,
    string Sha256);

internal sealed record AuditPackageDependency(string Alias, AuditPackageIdentity Package);

internal sealed record AuditPackageSnapshot(
    AuditPackageIdentity Identity,
    string Role,
    string ContentSha256,
    IReadOnlyList<AuditPackageInputRecord> Inputs,
    IReadOnlyList<AuditPackageDependency> Dependencies);

internal sealed record AuditTrustedClaim(
    string Operation,
    string Source,
    IReadOnlyList<string> Effects,
    string Assurance,
    IReadOnlyList<AuditFunctionIdentity> ReachableFrom);

internal sealed record AuditForeignDependency(string Name, string Version, string Ecosystem, string Reason);

internal sealed record AuditReportSnapshot(
    byte[] Json,
    IReadOnlyList<AuditPackageSnapshot> Packages,
    IReadOnlyList<string> ManifestGrants,
    IReadOnlyList<AuditTrustedClaim> TrustedClaims,
    IReadOnlyList<AuditForeignDependency> ForeignDependencies)
{
    public string Sha256 => AuditReport.Hash(Json);
}

internal static class AuditReport
{
    private const int SchemaVersion = 3;
    private const string SqlitePackageName = "Microsoft.Data.Sqlite";
    private const string SqlitePackageVersion = "10.0.12";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] ContentHashDomain = Encoding.UTF8.GetBytes("LANG-AUDIT-PACKAGE-CONTENT\0v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AuditReportSnapshot Create(PackageDependencyGraph graph, CheckedProgram program)
    {
        var packageSnapshots = CreatePackageSnapshots(graph);
        var identities = new Dictionary<string, AuditPackageIdentity?>(StringComparer.Ordinal);
        foreach (var package in packageSnapshots)
            identities.Add(FindNodeId(graph, package.Identity.Path), package.Identity);
        var functions = CreateFunctionFacts(program, identities);
        var claims = CreateTrustedClaims(graph, program, identities);
        var grants = graph.Root.Package.Manifest.Capabilities
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();
        var foreignDependencies = RequiresSqliteDependency(graph.Root.Package, program)
            ? new[]
            {
                new AuditForeignDependency(SqlitePackageName, SqlitePackageVersion, "nuget", "generated_build")
            }
            : [];

        var output = new
        {
            schema_version = SchemaVersion,
            packages = packageSnapshots.Select(package => new
            {
                identity = new
                {
                    name = package.Identity.Name,
                    version = package.Identity.Version,
                    path = package.Identity.Path
                },
                role = package.Role,
                content_sha256 = package.ContentSha256,
                dependencies = package.Dependencies.Select(dependency => new
                {
                    alias = dependency.Alias,
                    package = new
                    {
                        name = dependency.Package.Name,
                        version = dependency.Package.Version,
                        path = dependency.Package.Path
                    }
                }).ToArray(),
                inputs = package.Inputs.Select(input => new
                {
                    kind = input.Kind,
                    path = input.Path,
                    sha256 = input.Sha256
                }).ToArray()
            }).ToArray(),
            compiler = new { functions },
            manifest_grants = grants,
            http_origin = graph.Root.Package.Manifest.HttpOrigin,
            trusted_claims = claims.Select(claim => new
            {
                operation = claim.Operation,
                source = claim.Source,
                effects = claim.Effects,
                assurance = claim.Assurance,
                reachable_from = claim.ReachableFrom.Select(FunctionJson).ToArray()
            }).ToArray(),
            foreign_dependencies = foreignDependencies.Select(dependency => new
            {
                name = dependency.Name,
                version = dependency.Version,
                ecosystem = dependency.Ecosystem,
                reason = dependency.Reason
            }).ToArray()
        };

        return new AuditReportSnapshot(
            Serialize(output),
            packageSnapshots,
            grants,
            claims,
            foreignDependencies);
    }

    public static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string HashNormalizedText(string text) =>
        Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(NormalizeLineEndings(text)))).ToLowerInvariant();

    public static byte[] CreateStandaloneSnapshot(CheckedProgram program, string sourceText)
    {
        var identities = program.Functions
            .Select(function => function.PackageId)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(packageId => packageId, _ => (AuditPackageIdentity?)null, StringComparer.Ordinal);
        var claims = CreateStandaloneTrustedClaims(program);
        var output = new
        {
            schema_version = SchemaVersion,
            inputs = new[]
            {
                new { kind = "source", path = "source", sha256 = HashNormalizedText(sourceText) }
            },
            compiler = new { functions = CreateFunctionFacts(program, identities) },
            manifest_grants = Array.Empty<string>(),
            http_origin = (string?)null,
            trusted_claims = claims.Select(claim => new
            {
                operation = claim.Operation,
                source = claim.Source,
                effects = claim.Effects,
                assurance = claim.Assurance,
                reachable_from = claim.ReachableFrom.Select(FunctionJson).ToArray()
            }).ToArray(),
            foreign_dependencies = RequiresSqliteDependency(null, program)
                ? new[]
                {
                    new
                    {
                        name = SqlitePackageName,
                        version = SqlitePackageVersion,
                        ecosystem = "nuget",
                        reason = "generated_build"
                    }
                }
                : []
        };
        return Serialize(output);
    }

    public static IReadOnlyList<AuditTrustedClaim> CreateStandaloneTrustedClaims(CheckedProgram program)
    {
        var claims = new Dictionary<(string Source, string Operation), HashSet<AuditFunctionIdentity>>();
        var effects = new Dictionary<(string Source, string Operation), IReadOnlyList<string>>();

        void Add(string source, string operation, IReadOnlyList<string> operationEffects, AuditFunctionIdentity? from)
        {
            var key = (source, operation);
            if (!claims.TryGetValue(key, out var reachable))
            {
                reachable = [];
                claims.Add(key, reachable);
                effects.Add(key, operationEffects);
            }
            if (from is not null)
                reachable.Add(from);
        }

        var functionsById = program.Functions.ToDictionary(function => function.Id);
        var roots = new Dictionary<int, CheckedFunction>();
        if (program.EntryFunctionId is int entryId && functionsById.TryGetValue(entryId, out var entry))
            roots.TryAdd(entry.Id, entry);
        foreach (var command in program.Commands)
            if (functionsById.TryGetValue(command.HandlerFunctionId, out var handler))
                roots.TryAdd(handler.Id, handler);
        foreach (var route in program.Routes)
            if (functionsById.TryGetValue(route.HandlerFunctionId, out var handler))
                roots.TryAdd(handler.Id, handler);

        if (program.Commands.Count != 0)
        {
            var commandRoots = program.Commands
                .Select(command => functionsById[command.HandlerFunctionId])
                .DistinctBy(function => function.Id)
                .Select(FunctionReference)
                .ToArray();
            AddHost("cli.argument_decode", commandRoots);
            AddHost("cli.output", commandRoots);
        }

        if (roots.Count == 0)
        {
            foreach (var function in program.Functions.Where(function => function.Public))
                roots.TryAdd(function.Id, function);
        }

        foreach (var root in roots.Values
                     .OrderBy(function => function.Module, StringComparer.Ordinal)
                     .ThenBy(function => function.Name, StringComparer.Ordinal))
        {
            var rootReference = FunctionReference(root);
            foreach (var operation in CheckedReportFacts.FindTrustedAdapterOperations(program, root))
                Add(operation.Trust, operation.Operation, operation.Effects, rootReference);
        }

        return claims.Keys
            .OrderBy(key => key.Source, StringComparer.Ordinal)
            .ThenBy(key => key.Operation, StringComparer.Ordinal)
            .Select(key => new AuditTrustedClaim(
                key.Operation,
                key.Source,
                effects[key].OrderBy(effect => effect, StringComparer.Ordinal).ToArray(),
                "claim_only",
                claims[key]
                    .OrderBy(function => function.Module, StringComparer.Ordinal)
                    .ThenBy(function => function.Name, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        void AddHost(string operation, IReadOnlyList<AuditFunctionIdentity> reachableFrom)
        {
            if (reachableFrom.Count == 0)
            {
                Add("trusted_host", operation, [], null);
                return;
            }
            foreach (var root in reachableFrom)
                Add("trusted_host", operation, [], root);
        }

        AuditFunctionIdentity FunctionReference(CheckedFunction function) =>
            new(null, function.Module, function.Name);
    }

    public static bool RequiresSqliteDependency(LoadedPackage? package, CheckedProgram program) =>
        package?.WebDatabaseOptions is not null ||
        program.Functions.Any(function => function.InferredEffects.Any(effect => effect is "db.read" or "db.write"));

    private static IReadOnlyList<AuditPackageSnapshot> CreatePackageSnapshots(PackageDependencyGraph graph)
    {
        var directDependencies = graph.Root.DependencyIds.Values.ToHashSet(StringComparer.Ordinal);
        var result = new List<AuditPackageSnapshot>(graph.Nodes.Count);
        foreach (var node in graph.Nodes.OrderBy(node => node.RelativePath, StringComparer.Ordinal))
        {
            var identity = Identity(node);
            var inputTexts = new List<(string Kind, string Path, string Text)>
            {
                ("manifest", "lang.toml", node.Package.ManifestText)
            };
            inputTexts.AddRange(node.Package.Sources.Select(source => (
                "source",
                NormalizeRelative(Path.GetRelativePath(node.Package.Root, source.File)),
                source.Text)));
            if (node.Package.WebDatabaseOptions is { } database)
            {
                var schemaPath = node.Package.Manifest.SqliteSchema ?? database.SchemaPath;
                inputTexts.Add(("sqlite_schema", NormalizeRelative(schemaPath), database.SchemaText));
            }

            inputTexts.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
            var inputs = inputTexts.Select(input => new AuditPackageInputRecord(
                identity,
                input.Kind,
                input.Path,
                HashNormalizedText(input.Text))).ToArray();
            var dependencies = node.DependencyIds
                .OrderBy(dependency => dependency.Key, StringComparer.Ordinal)
                .Select(dependency => new AuditPackageDependency(
                    dependency.Key,
                    Identity(graph.ById[dependency.Value])))
                .ToArray();
            var role = ReferenceEquals(node, graph.Root)
                ? "root"
                : directDependencies.Contains(node.Id) ? "direct" : "transitive";

            result.Add(new AuditPackageSnapshot(
                identity,
                role,
                HashPackageContent(inputTexts),
                inputs,
                dependencies));
        }

        return result;
    }

    private static object[] CreateFunctionFacts(
        CheckedProgram program,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities)
    {
        var functionsById = program.Functions.ToDictionary(function => function.Id);
        return program.Functions
            .Select(function =>
            {
                if (!identities.TryGetValue(function.PackageId, out var package))
                    throw new InvalidOperationException("A checked function has no resolved package identity");

                return new
                {
                    Function = function,
                    Identity = new AuditFunctionIdentity(package, function.Module, function.Name)
                };
            })
            .OrderBy(item => item.Identity.Package?.Path ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(item => item.Identity.Module, StringComparer.Ordinal)
            .ThenBy(item => item.Identity.Name, StringComparer.Ordinal)
            .Select(item =>
            {
                var function = item.Function;
                var effectPaths = function.InferredEffectPaths
                    .Select(path => new
                    {
                        effect = path.Effect,
                        steps = path.FunctionIds.Select(functionId =>
                        {
                            if (!functionsById.TryGetValue(functionId, out var step) ||
                                !identities.TryGetValue(step.PackageId, out var package))
                                throw new InvalidOperationException("A checked effect path has no stable function identity");
                            return FunctionStepJson(package, step);
                        }).Append((object)new { kind = "operation", name = path.IntrinsicName }).ToArray()
                    })
                    .OrderBy(path => path.effect, StringComparer.Ordinal)
                    .ToArray();
                var directCalls = function.Calls
                    .Distinct()
                    .Select(call =>
                    {
                        if (!identities.TryGetValue(call.PackageId, out var package))
                            throw new InvalidOperationException("A checked direct call has no resolved package identity");
                        return new AuditFunctionIdentity(package, call.Module, call.Name);
                    })
                    .OrderBy(call => call.Package?.Path ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(call => call.Module, StringComparer.Ordinal)
                    .ThenBy(call => call.Name, StringComparer.Ordinal)
                    .Select(FunctionJson)
                    .ToArray();

                return (object)new
                {
                    package = item.Identity.Package is null ? null : IdentityJson(item.Identity.Package),
                    module = item.Identity.Module,
                    name = item.Identity.Name,
                    visibility = function.Public ? "public" : "private",
                    is_async = function.IsAsync,
                    declared_effects = function.DeclaredEffects.OrderBy(effect => effect, StringComparer.Ordinal).ToArray(),
                    inferred_effects = function.InferredEffects.OrderBy(effect => effect, StringComparer.Ordinal).ToArray(),
                    effect_paths = effectPaths,
                    direct_calls = directCalls,
                    required_capabilities = CheckedReportFacts.RequiredCapabilities(function.InferredEffects)
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<AuditTrustedClaim> CreateTrustedClaims(
        PackageDependencyGraph graph,
        CheckedProgram program,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities)
    {
        var reachable = new Dictionary<(string Source, string Operation), HashSet<AuditFunctionIdentity>>();
        var effectsByOperation = new Dictionary<(string Source, string Operation), IReadOnlyList<string>>();

        void AddClaim(string source, string operation, IReadOnlyList<string> effects, AuditFunctionIdentity? from)
        {
            var key = (source, operation);
            if (!reachable.TryGetValue(key, out var roots))
            {
                roots = [];
                reachable.Add(key, roots);
                effectsByOperation.Add(key, effects);
            }
            if (from is not null)
                roots.Add(from);
        }

        var rootPackage = graph.Root.Package;
        if (rootPackage.Manifest.Kind == "cli" &&
            program.Commands.Any(command => string.Equals(command.PackageId, graph.Root.Id, StringComparison.Ordinal)))
        {
            var commandHandlers = program.Commands
                .Where(command => string.Equals(command.PackageId, graph.Root.Id, StringComparison.Ordinal))
                .Select(command => program.Functions.First(function => function.Id == command.HandlerFunctionId))
                .DistinctBy(function => function.Id)
                .ToArray();
            var reachableFrom = commandHandlers.Select(function => FunctionIdentity(function, identities)).ToArray();
            AddHostClaim("cli.argument_decode", [], reachableFrom);
            AddHostClaim("cli.output", [], reachableFrom);
        }

        if (rootPackage.Manifest.Kind == "web")
        {
            AddHostClaim("web.net_listen", ["net.listen"], []);
            AddHostClaim("web.request_decode", [], []);
            AddHostClaim("web.response_output", [], []);
            if (rootPackage.WebDatabaseOptions is not null)
                AddHostClaim("web.schema_init", ["db.write"], []);
        }

        void AddHostClaim(string operation, IReadOnlyList<string> effects, IReadOnlyList<AuditFunctionIdentity> roots)
        {
            var key = ("trusted_host", operation);
            if (!reachable.TryGetValue(key, out var reached))
            {
                reached = [];
                reachable.Add(key, reached);
                effectsByOperation.Add(key, effects);
            }
            reached.UnionWith(roots);
        }

        var roots = new Dictionary<int, CheckedFunction>();
        if (program.EntryFunctionId is int entryFunctionId &&
            program.Functions.FirstOrDefault(function => function.Id == entryFunctionId) is { } entryFunction)
            roots.TryAdd(entryFunction.Id, entryFunction);
        foreach (var command in program.Commands.Where(command => command.PackageId == graph.Root.Id))
            if (program.Functions.FirstOrDefault(function => function.Id == command.HandlerFunctionId) is { } handler)
                roots.TryAdd(handler.Id, handler);
        foreach (var route in program.Routes)
            if (program.Functions.FirstOrDefault(function => function.Id == route.HandlerFunctionId) is { } handler)
                roots.TryAdd(handler.Id, handler);
        if (roots.Count == 0)
        {
            foreach (var function in program.Functions.Where(function =>
                         function.PackageId == graph.Root.Id && function.Public))
                roots.TryAdd(function.Id, function);
        }

        foreach (var root in roots.Values
                     .OrderBy(function => FunctionIdentity(function, identities).Package?.Path ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(function => function.Module, StringComparer.Ordinal)
                     .ThenBy(function => function.Name, StringComparer.Ordinal))
        {
            var rootIdentity = FunctionIdentity(root, identities);
            foreach (var operation in CheckedReportFacts.FindTrustedAdapterOperations(program, root))
                AddClaim(operation.Trust, operation.Operation, operation.Effects, rootIdentity);
        }

        return reachable.Keys
            .OrderBy(key => key.Source, StringComparer.Ordinal)
            .ThenBy(key => key.Operation, StringComparer.Ordinal)
            .Select(key => new AuditTrustedClaim(
                key.Operation,
                key.Source,
                effectsByOperation[key].OrderBy(effect => effect, StringComparer.Ordinal).ToArray(),
                "claim_only",
                reachable[key]
                    .OrderBy(function => function.Package?.Path ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(function => function.Module, StringComparer.Ordinal)
                    .ThenBy(function => function.Name, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private static AuditFunctionIdentity FunctionIdentity(
        CheckedFunction function,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities)
    {
        if (!identities.TryGetValue(function.PackageId, out var package) || package is null)
            throw new InvalidOperationException("A checked function has no resolved package identity");
        return new AuditFunctionIdentity(package, function.Module, function.Name);
    }

    private static object FunctionJson(AuditFunctionIdentity function) => function.Package is null
        ? new { module = function.Module, name = function.Name }
        : new { package = IdentityJson(function.Package), module = function.Module, name = function.Name };

    private static object FunctionStepJson(AuditPackageIdentity? package, CheckedFunction function) => package is null
        ? new { kind = "function", module = function.Module, name = function.Name }
        : new { kind = "function", package = IdentityJson(package), module = function.Module, name = function.Name };

    private static object IdentityJson(AuditPackageIdentity identity) => new
    {
        name = identity.Name,
        version = identity.Version,
        path = identity.Path
    };

    private static AuditPackageIdentity Identity(ResolvedPackage node) => new(
        node.Package.Manifest.Name,
        node.Package.Manifest.Version,
        node.RelativePath.Length == 0 ? "." : NormalizeRelative(node.RelativePath));

    private static string FindNodeId(PackageDependencyGraph graph, string relativePath) =>
        graph.Nodes.First(node => string.Equals(
            node.RelativePath.Length == 0 ? "." : NormalizeRelative(node.RelativePath),
            relativePath,
            StringComparison.Ordinal)).Id;

    private static string HashPackageContent(IReadOnlyList<(string Kind, string Path, string Text)> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(ContentHashDomain);
        var length = new byte[sizeof(ulong)];
        foreach (var entry in entries)
        {
            AppendField(entry.Path);
            AppendField(NormalizeLineEndings(entry.Text));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void AppendField(string value)
        {
            var bytes = StrictUtf8.GetBytes(value);
            BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)bytes.LongLength);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
    }

    private static byte[] Serialize<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return StrictUtf8.GetBytes(json.TrimEnd('\n') + "\n");
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string NormalizeRelative(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
}
