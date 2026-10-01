using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record AuditPackageIdentity(string Name, string Version, string Path, PackageSourceIdentity Source);

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

internal sealed record AuditManagedAdapterParameter(string Name, string Type);

internal sealed record AuditManagedAdapterOperation(
    string OperationId,
    string Name,
    IReadOnlyList<AuditManagedAdapterParameter> Parameters,
    string Result,
    bool IsAsync,
    IReadOnlyList<string> Effects,
    IReadOnlyList<string> RequiredCapabilities);

internal sealed record AuditManagedAdapterAssembly(string Identity, string Path, string Sha256);

internal sealed record AuditManagedAdapterProvenance(
    AuditPackageIdentity Package,
    string BridgeId,
    int ContractVersion,
    string TargetFramework,
    string PortabilityTarget,
    IReadOnlyList<AuditManagedAdapterOperation> Operations,
    IReadOnlyList<AuditManagedAdapterAssembly> Assemblies,
    string ClosureSha256);

internal sealed record AuditReportSnapshot(
    byte[] Json,
    IReadOnlyList<AuditPackageSnapshot> Packages,
    IReadOnlyList<string> ManifestGrants,
    IReadOnlyList<AuditTrustedClaim> TrustedClaims,
    IReadOnlyList<AuditForeignDependency> ForeignDependencies,
    IReadOnlyList<AuditManagedAdapterProvenance> ManagedAdapters)
{
    public string Sha256 => AuditReport.Hash(Json);
}

internal static class AuditReport
{
    private const int SchemaVersion = 9;
    private const string SqlitePackageName = "Microsoft.Data.Sqlite";
    private const string SqlitePackageVersion = "10.0.12";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] ContentHashDomain = Encoding.UTF8.GetBytes("LANG-AUDIT-PACKAGE-CONTENT\0v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AuditReportSnapshot Create(
        PackageDependencyGraph graph,
        CheckedProgram program,
        IReadOnlyList<AuditManagedAdapterProvenance>? managedAdapters = null)
    {
        var packageSnapshots = CreatePackageSnapshots(graph);
        var canonicalManagedAdapters = CanonicalizeManagedAdapters(managedAdapters ?? []);
        var identities = new Dictionary<string, AuditPackageIdentity?>(StringComparer.Ordinal);
        foreach (var package in packageSnapshots)
            identities.Add(FindNodeId(graph, package.Identity.Path), package.Identity);
        var stablePackageIdentities = CheckedReportFacts.StablePackageIdentities(graph);
        var functions = CreateFunctionFacts(program, identities, stablePackageIdentities);
        var newtypes = CreateNewtypeFacts(program, identities, stablePackageIdentities);
        var traits = CreateTraitFacts(program, identities, stablePackageIdentities);
        var traitImpls = CreateTraitImplFacts(program, identities, stablePackageIdentities);
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
                identity = IdentityJson(package.Identity),
                role = package.Role,
                content_sha256 = package.ContentSha256,
                dependencies = package.Dependencies.Select(dependency => new
                {
                    alias = dependency.Alias,
                    package = IdentityJson(dependency.Package)
                }).ToArray(),
                inputs = package.Inputs.Select(input => new
                {
                    kind = input.Kind,
                    path = input.Path,
                    sha256 = input.Sha256
                }).ToArray()
            }).ToArray(),
            compiler = new { functions, newtypes, traits, trait_impls = traitImpls },
            manifest_grants = grants,
            config = CheckedReportFacts.ConfigMetadata(program.ConfigFields),
            http_origin = graph.Root.Package.Manifest.HttpOrigin,
            process_executables = ProcessExecutableMetadata(graph.Root.Package.Manifest.ProcessExecutables),
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
            }).ToArray(),
            managed_adapters = ManagedAdapterMetadataJson(canonicalManagedAdapters)
        };

        return new AuditReportSnapshot(
            Serialize(output),
            packageSnapshots,
            grants,
            claims,
            foreignDependencies,
            canonicalManagedAdapters);
    }

    public static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string HashNormalizedText(string text) =>
        Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(NormalizeLineEndings(text)))).ToLowerInvariant();

    public static byte[] CreateStandaloneSnapshot(CheckedProgram program, string sourceText)
    {
        var identities = program.Functions
            .Select(function => function.PackageId)
            .Concat(program.Structs.Select(structure => structure.PackageId))
            .Concat(program.Unions.Select(union => union.PackageId))
            .Concat(program.Newtypes.Select(newtype => newtype.PackageId))
            .Concat(program.Traits.Select(trait => trait.PackageId))
            .Concat(program.TraitImpls.Select(implementation => implementation.PackageId))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(packageId => packageId, _ => (AuditPackageIdentity?)null, StringComparer.Ordinal);
        var stablePackageIdentities = CheckedReportFacts.StablePackageIdentities(program);
        var claims = CreateStandaloneTrustedClaims(program);
        var output = new
        {
            schema_version = SchemaVersion,
            inputs = new[]
            {
                new { kind = "source", path = "source", sha256 = HashNormalizedText(sourceText) }
            },
            compiler = new
            {
                functions = CreateFunctionFacts(program, identities, stablePackageIdentities),
                newtypes = CreateNewtypeFacts(program, identities, stablePackageIdentities),
                traits = CreateTraitFacts(program, identities, stablePackageIdentities),
                trait_impls = CreateTraitImplFacts(program, identities, stablePackageIdentities)
            },
            manifest_grants = Array.Empty<string>(),
            config = CheckedReportFacts.ConfigMetadata(program.ConfigFields),
            http_origin = (string?)null,
            process_executables = Array.Empty<object>(),
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
                : [],
            managed_adapters = Array.Empty<object>()
        };
        return Serialize(output);
    }

    public static AuditManagedAdapterProvenance CreateManagedAdapterProvenance(
        ResolvedPackage package,
        string bridgeId,
        int contractVersion,
        string targetFramework,
        string portabilityTarget,
        IEnumerable<AuditManagedAdapterOperation> operations,
        IEnumerable<AuditManagedAdapterAssembly> assemblies,
        string closureSha256) => CanonicalizeManagedAdapter(new AuditManagedAdapterProvenance(
            Identity(package),
            bridgeId,
            contractVersion,
            targetFramework,
            portabilityTarget,
            operations.ToArray(),
            assemblies.ToArray(),
            closureSha256));

    public static IReadOnlyList<AuditManagedAdapterProvenance> CanonicalizeManagedAdapters(
        IEnumerable<AuditManagedAdapterProvenance> managedAdapters) => managedAdapters
        .Select(CanonicalizeManagedAdapter)
        .OrderBy(adapter => adapter.Package.Path, StringComparer.Ordinal)
        .ThenBy(adapter => adapter.Package.Name, StringComparer.Ordinal)
        .ThenBy(adapter => adapter.Package.Version, StringComparer.Ordinal)
        .ThenBy(adapter => adapter.BridgeId, StringComparer.Ordinal)
        .ToArray();

    internal static object[] ManagedAdapterMetadataJson(
        IReadOnlyList<AuditManagedAdapterProvenance> managedAdapters) => managedAdapters
        .Select(adapter => (object)new
        {
            package = ManagedPackageIdentityJson(adapter.Package),
            bridge_id = adapter.BridgeId,
            contract_version = adapter.ContractVersion,
            target_framework = adapter.TargetFramework,
            portability_target = adapter.PortabilityTarget,
            operations = adapter.Operations.Select(operation => new
            {
                operation_id = operation.OperationId,
                name = operation.Name,
                signature = new
                {
                    parameters = operation.Parameters.Select(parameter => new
                    {
                        name = parameter.Name,
                        type = parameter.Type
                    }).ToArray(),
                    result = operation.Result
                },
                is_async = operation.IsAsync,
                effects = operation.Effects,
                required_capabilities = operation.RequiredCapabilities
            }).ToArray(),
            assemblies = adapter.Assemblies.Select(assembly => new
            {
                identity = assembly.Identity,
                path = assembly.Path,
                sha256 = assembly.Sha256
            }).ToArray(),
            closure_sha256 = adapter.ClosureSha256,
            assurance = "claim_only"
        })
        .ToArray();

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
                    HashNormalizedText(input.Text)))
                .Concat(node.Package.Manifest.ProcessExecutables.Select(pin => new AuditPackageInputRecord(
                    identity,
                    "process_executable",
                    pin.Path,
                    pin.Sha256)))
                .OrderBy(input => input.Path, StringComparer.Ordinal)
                .ThenBy(input => input.Kind, StringComparer.Ordinal)
                .ToArray();
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
                HashPackageContent(inputTexts, node.Package.Manifest.ProcessExecutables),
                inputs,
                dependencies));
        }

        return result;
    }

    private static object[] CreateNewtypeFacts(
        CheckedProgram program,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities,
        IReadOnlyDictionary<string, string> stablePackageIdentities)
    {
        var structsById = program.Structs.ToDictionary(structure => structure.Id);
        var unionsById = program.Unions.ToDictionary(union => union.Id);
        var newtypesById = program.Newtypes.ToDictionary(newtype => newtype.Id);
        return program.Newtypes
            .Select(newtype => new
            {
                Newtype = newtype,
                Id = CheckedReportFacts.StableNewtypeId(newtype, stablePackageIdentities)
            })
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item =>
            {
                var newtype = item.Newtype;
                if (!identities.TryGetValue(newtype.PackageId, out var package))
                    throw new InvalidOperationException("A checked newtype has no resolved package identity");
                return (object)new
                {
                    id = item.Id,
                    package = package is null ? null : IdentityJson(package),
                    module = newtype.Module,
                    name = newtype.Name,
                    visibility = newtype.Public ? "public" : "private",
                    representation = AuditType(newtype.Representation, identities, structsById, unionsById, newtypesById)
                };
            })
            .ToArray();
    }

    private static object[] CreateTraitFacts(
        CheckedProgram program,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities,
        IReadOnlyDictionary<string, string> stablePackageIdentities)
    {
        var structsById = program.Structs.ToDictionary(structure => structure.Id);
        var unionsById = program.Unions.ToDictionary(union => union.Id);
        var newtypesById = program.Newtypes.ToDictionary(newtype => newtype.Id);
        return program.Traits
            .Select(trait => new
            {
                Trait = trait,
                Id = CheckedReportFacts.StableTraitId(trait, stablePackageIdentities)
            })
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item =>
            {
                var trait = item.Trait;
                if (!identities.TryGetValue(trait.PackageId, out var package))
                    throw new InvalidOperationException("A checked trait has no resolved package identity");
                return (object)new
                {
                    id = item.Id,
                    package = package is null ? null : IdentityJson(package),
                    module = trait.Module,
                    name = trait.Name,
                    visibility = trait.Public ? "public" : "private",
                    methods = trait.Methods
                        .Select(method => new
                        {
                            name = method.Name,
                            parameters = method.Parameters
                                .Select(parameter => new
                                {
                                    name = parameter.Name,
                                    type = AuditType(parameter.Type, identities, structsById, unionsById, newtypesById)
                                })
                                .ToArray(),
                            return_type = AuditType(method.ReturnType, identities, structsById, unionsById, newtypesById)
                        })
                        .ToArray()
                };
            })
            .ToArray();
    }

    private static object[] CreateTraitImplFacts(
        CheckedProgram program,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities,
        IReadOnlyDictionary<string, string> stablePackageIdentities)
    {
        var traitsById = program.Traits.ToDictionary(trait => trait.Id);
        var functionsById = program.Functions.ToDictionary(function => function.Id);
        var structsById = program.Structs.ToDictionary(structure => structure.Id);
        var unionsById = program.Unions.ToDictionary(union => union.Id);
        var newtypesById = program.Newtypes.ToDictionary(newtype => newtype.Id);
        return program.TraitImpls
            .OrderBy(implementation => implementation.StableId, StringComparer.Ordinal)
            .Select(implementation =>
            {
                if (!traitsById.TryGetValue(implementation.TraitId, out var trait))
                    throw new InvalidOperationException("A checked trait implementation refers to an unknown trait");
                if (!identities.TryGetValue(implementation.PackageId, out var package))
                    throw new InvalidOperationException("A checked trait implementation has no resolved package identity");
                if (trait.Methods.Count != implementation.BindingFunctionIds.Count)
                    throw new InvalidOperationException("A checked trait implementation has an inconsistent method binding count");

                return (object)new
                {
                    id = implementation.StableId,
                    package = package is null ? null : IdentityJson(package),
                    module = implementation.Module,
                    trait = CheckedReportFacts.StableTraitId(trait, stablePackageIdentities),
                    target = AuditType(implementation.Target, identities, structsById, unionsById, newtypesById),
                    visibility = implementation.Public ? "public" : "private",
                    methods = trait.Methods
                        .Select(method =>
                        {
                            var bindingId = implementation.BindingFunctionIds[method.Id];
                            if (!functionsById.TryGetValue(bindingId, out var bindingFunction))
                                throw new InvalidOperationException("A checked trait implementation binding has no function");
                            return new
                            {
                                name = method.Name,
                                binding_function = FunctionJson(FunctionIdentity(bindingFunction, identities))
                            };
                        })
                        .ToArray()
                };
            })
            .ToArray();
    }

    private static object[] CreateFunctionFacts(
        CheckedProgram program,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities,
        IReadOnlyDictionary<string, string> stablePackageIdentities)
    {
        var functionsById = program.Functions.ToDictionary(function => function.Id);
        var traitsById = program.Traits.ToDictionary(trait => trait.Id);
        var traitImplsById = program.TraitImpls.ToDictionary(implementation => implementation.Id);
        var structsById = program.Structs.ToDictionary(structure => structure.Id);
        var unionsById = program.Unions.ToDictionary(union => union.Id);
        var newtypesById = program.Newtypes.ToDictionary(newtype => newtype.Id);
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
                    type_parameters = function.TypeParameters
                        .Select((typeParameter, ordinal) => new
                        {
                            name = typeParameter.DisplayName,
                            ordinal = typeParameter.TypeParameterOrdinal,
                            bounds = function.TypeParameterBounds[ordinal]
                                .Select(bound => new
                                {
                                    trait = CheckedReportFacts.StableTraitId(traitsById[bound.TraitId], stablePackageIdentities)
                                })
                                .ToArray()
                        })
                        .ToArray(),
                    declared_effects = function.DeclaredEffects.OrderBy(effect => effect, StringComparer.Ordinal).ToArray(),
                    inferred_effects = function.InferredEffects.OrderBy(effect => effect, StringComparer.Ordinal).ToArray(),
                    effect_paths = effectPaths,
                    direct_calls = directCalls,
                    trait_calls = CreateTraitCalls(
                        function,
                        functionsById,
                        traitsById,
                        traitImplsById,
                        identities,
                        stablePackageIdentities,
                        structsById,
                        unionsById,
                        newtypesById),
                    required_capabilities = CheckedReportFacts.RequiredCapabilities(function.InferredEffects)
                };
            })
            .ToArray();
    }

    private static object[] CreateTraitCalls(
        CheckedFunction function,
        IReadOnlyDictionary<int, CheckedFunction> functionsById,
        IReadOnlyDictionary<int, CheckedTrait> traitsById,
        IReadOnlyDictionary<int, CheckedTraitImpl> traitImplsById,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities,
        IReadOnlyDictionary<string, string> stablePackageIdentities,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById)
    {
        var result = new List<object>();
        foreach (var expression in CheckedReportFacts.TypedExpressions(function))
        {
            if (expression is TypedTraitCallExpr traitCall)
            {
                if (!traitsById.TryGetValue(traitCall.TraitId, out var trait) ||
                    trait.Methods.FirstOrDefault(method => method.Id == traitCall.MethodId) is not { } method)
                    throw new InvalidOperationException("A checked trait call refers to an unknown method");
                result.Add(new
                {
                    kind = "method",
                    trait = CheckedReportFacts.StableTraitId(trait, stablePackageIdentities),
                    method = method.Name,
                    witness = AuditMethodWitness(
                        traitCall.Witness,
                        traitCall.TraitId,
                        method,
                        traitsById,
                        traitImplsById,
                        functionsById,
                        identities,
                        stablePackageIdentities,
                        structsById,
                        unionsById,
                        newtypesById)
                });
                continue;
            }

            if (expression is not TypedCallExpr call || !functionsById.TryGetValue(call.FunctionId, out var target))
                continue;

            var targetBounds = target.TypeParameterBounds
                .SelectMany((bounds, parameter) => bounds.Select((bound, ordinal) => (parameter, ordinal, bound)))
                .ToArray();
            if (targetBounds.Length != call.TraitWitnesses.Count)
                throw new InvalidOperationException("A checked call has an inconsistent trait witness count");
            if (targetBounds.Length == 0)
                continue;

            var witnesses = targetBounds.Select((entry, index) => new
            {
                type_parameter_ordinal = entry.parameter,
                bound_ordinal = entry.ordinal,
                trait = CheckedReportFacts.StableTraitId(traitsById[entry.bound.TraitId], stablePackageIdentities),
                witness = AuditWitnessSelection(
                    call.TraitWitnesses[index],
                    entry.bound.TraitId,
                    traitImplsById,
                    identities,
                    structsById,
                    unionsById,
                    newtypesById)
            }).ToArray();
            result.Add(new
            {
                kind = "function_call",
                function = FunctionJson(FunctionIdentity(target, identities)),
                witnesses
            });
        }

        return result.ToArray();
    }

    private static object AuditMethodWitness(
        TypedTraitWitness witness,
        int traitId,
        CheckedTraitMethod method,
        IReadOnlyDictionary<int, CheckedTrait> traitsById,
        IReadOnlyDictionary<int, CheckedTraitImpl> traitImplsById,
        IReadOnlyDictionary<int, CheckedFunction> functionsById,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities,
        IReadOnlyDictionary<string, string> stablePackageIdentities,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById)
    {
        if (witness is TypedForwardedTraitWitness forwarded)
        {
            return new
            {
                kind = "bound",
                type_parameter_ordinal = forwarded.TypeParameterOrdinal,
                bound_ordinal = forwarded.BoundOrdinal
            };
        }

        if (witness is not TypedConcreteTraitWitness concrete ||
            !traitImplsById.TryGetValue(concrete.ImplId, out var implementation) ||
            implementation.TraitId != traitId ||
            method.Id < 0 || method.Id >= implementation.BindingFunctionIds.Count ||
            !functionsById.TryGetValue(implementation.BindingFunctionIds[method.Id], out var bindingFunction))
            throw new InvalidOperationException("A checked trait call has an unresolved implementation binding");

        return new
        {
            kind = "impl",
            id = implementation.StableId,
            target = AuditType(implementation.Target, identities, structsById, unionsById, newtypesById),
            binding_function = FunctionJson(FunctionIdentity(bindingFunction, identities))
        };
    }

    private static object AuditWitnessSelection(
        TypedTraitWitness witness,
        int expectedTraitId,
        IReadOnlyDictionary<int, CheckedTraitImpl> traitImplsById,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById) => witness switch
        {
            TypedConcreteTraitWitness concrete when traitImplsById.TryGetValue(concrete.ImplId, out var implementation) &&
                implementation.TraitId == expectedTraitId => new
                {
                    kind = "impl",
                    id = implementation.StableId,
                    target = AuditType(implementation.Target, identities, structsById, unionsById, newtypesById)
                },
            TypedForwardedTraitWitness forwarded => new
            {
                kind = "bound",
                type_parameter_ordinal = forwarded.TypeParameterOrdinal,
                bound_ordinal = forwarded.BoundOrdinal
            },
            _ => throw new InvalidOperationException("A checked trait witness is unknown")
        };

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
        if (!identities.TryGetValue(function.PackageId, out var package))
            throw new InvalidOperationException("A checked function has no resolved package identity");
        return new AuditFunctionIdentity(package, function.Module, function.Name);
    }

    private static object AuditType(
        LangType type,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById) => type.Kind switch
        {
            LangTypeKind.I32 => new { kind = "primitive", name = "i32" },
            LangTypeKind.Bool => new { kind = "primitive", name = "bool" },
            LangTypeKind.Text => new { kind = "primitive", name = "Text" },
            LangTypeKind.Html => new { kind = "primitive", name = "Html" },
            LangTypeKind.FilePath => new { kind = "primitive", name = "FilePath" },
            LangTypeKind.FsRead => new { kind = "primitive", name = "FsRead" },
            LangTypeKind.FsWrite => new { kind = "primitive", name = "FsWrite" },
            LangTypeKind.Config => new { kind = "primitive", name = "Config" },
            LangTypeKind.Secrets => new { kind = "primitive", name = "Secrets" },
            LangTypeKind.Logger => new { kind = "primitive", name = "Logger" },
            LangTypeKind.ProcessRunner => new { kind = "primitive", name = "ProcessRunner" },
            LangTypeKind.SecretText => new
            {
                kind = "secret",
                item = AuditType(type.Arguments[0], identities, structsById, unionsById, newtypesById)
            },
            LangTypeKind.FsError => new { kind = "primitive", name = "FsError" },
            LangTypeKind.ProcessOutput => new { kind = "primitive", name = "ProcessOutput" },
            LangTypeKind.ProcessError => new { kind = "primitive", name = "ProcessError" },
            LangTypeKind.HttpClient => new { kind = "primitive", name = "HttpClient" },
            LangTypeKind.HttpResponse => new { kind = "primitive", name = "HttpResponse" },
            LangTypeKind.HttpError => new { kind = "primitive", name = "HttpError" },
            LangTypeKind.DbRead => new { kind = "primitive", name = "DbRead" },
            LangTypeKind.DbWrite => new { kind = "primitive", name = "DbWrite" },
            LangTypeKind.Transaction => new { kind = "primitive", name = "Transaction" },
            LangTypeKind.DbError => new { kind = "primitive", name = "DbError" },
            LangTypeKind.TypeParameter when type.TypeParameterOwnerKind == TypeParameterOwnerKind.Trait => new { kind = "self" },
            LangTypeKind.TypeParameter => new
            {
                kind = "type_parameter",
                name = type.DisplayName,
                ordinal = type.TypeParameterOrdinal
            },
            LangTypeKind.List => new
            {
                kind = "list",
                item = AuditType(type.Arguments[0], identities, structsById, unionsById, newtypesById)
            },
            LangTypeKind.Map => new
            {
                kind = "map",
                key = AuditType(type.Arguments[0], identities, structsById, unionsById, newtypesById),
                value = AuditType(type.Arguments[1], identities, structsById, unionsById, newtypesById)
            },
            LangTypeKind.Option => new
            {
                kind = "option",
                item = AuditType(type.Arguments[0], identities, structsById, unionsById, newtypesById)
            },
            LangTypeKind.Result => new
            {
                kind = "result",
                ok = AuditType(type.Arguments[0], identities, structsById, unionsById, newtypesById),
                error = AuditType(type.Arguments[1], identities, structsById, unionsById, newtypesById)
            },
            LangTypeKind.Struct when structsById.TryGetValue(type.StructId, out var structure) => AuditNominalType(
                "struct", structure.PackageId, structure.Module, structure.Name, type.Arguments, identities, structsById, unionsById, newtypesById),
            LangTypeKind.Union when unionsById.TryGetValue(type.UnionId, out var union) => AuditNominalType(
                "union", union.PackageId, union.Module, union.Name, type.Arguments, identities, structsById, unionsById, newtypesById),
            LangTypeKind.Newtype when newtypesById.TryGetValue(type.NewtypeId, out var newtype) => AuditNominalType(
                "newtype", newtype.PackageId, newtype.Module, newtype.Name, [], identities, structsById, unionsById, newtypesById),
            LangTypeKind.Error => throw new InvalidOperationException("An audit report cannot contain an error type"),
            LangTypeKind.Struct or LangTypeKind.Union or LangTypeKind.Newtype => throw new InvalidOperationException("A checked type refers to an unknown declaration"),
            _ => throw new InvalidOperationException($"Unsupported checked type kind '{type.Kind}'")
        };

    private static object AuditNominalType(
        string declarationKind,
        string packageId,
        string module,
        string name,
        IReadOnlyList<LangType> typeArguments,
        IReadOnlyDictionary<string, AuditPackageIdentity?> identities,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById)
    {
        if (!identities.TryGetValue(packageId, out var package))
            throw new InvalidOperationException("A checked nominal type has no resolved package identity");
        return new
        {
            kind = "nominal",
            declaration_kind = declarationKind,
            package = package is null ? null : IdentityJson(package),
            module,
            name,
            type_arguments = typeArguments
                .Select(argument => AuditType(argument, identities, structsById, unionsById, newtypesById))
                .ToArray()
        };
    }

    private static object FunctionJson(AuditFunctionIdentity function) => function.Package is null
        ? new { module = function.Module, name = function.Name }
        : new { package = IdentityJson(function.Package), module = function.Module, name = function.Name };

    private static object FunctionStepJson(AuditPackageIdentity? package, CheckedFunction function) => package is null
        ? new { kind = "function", module = function.Module, name = function.Name }
        : new { kind = "function", package = IdentityJson(package), module = function.Module, name = function.Name };

    internal static object IdentityJson(AuditPackageIdentity identity) => new
    {
        name = identity.Name,
        version = identity.Version,
        path = identity.Path,
        source = SourceJson(identity.Source)
    };

    private static object ManagedPackageIdentityJson(AuditPackageIdentity identity) => new
    {
        name = identity.Name,
        version = identity.Version,
        path = identity.Path,
        source = identity.Source.Kind switch
        {
            PackageSourceKind.Root => (object)new { kind = "root" },
            PackageSourceKind.Path => new { kind = "path", path = identity.Source.Path },
            PackageSourceKind.Git => new
            {
                kind = "git",
                package_id = identity.Path,
                commit = identity.Source.Commit
            },
            _ => throw new InvalidOperationException("Unknown package source identity kind")
        }
    };

    private static AuditManagedAdapterProvenance CanonicalizeManagedAdapter(
        AuditManagedAdapterProvenance adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter.Package);
        if (string.IsNullOrWhiteSpace(adapter.Package.Name) ||
            string.IsNullOrWhiteSpace(adapter.Package.Version) ||
            string.IsNullOrWhiteSpace(adapter.Package.Path))
            throw new InvalidOperationException("Managed adapter provenance requires a stable package identity");
        if (string.IsNullOrWhiteSpace(adapter.BridgeId) || adapter.BridgeId.Any(char.IsControl))
            throw new InvalidOperationException("Managed adapter provenance requires a stable bridge ID");
        if (adapter.ContractVersion <= 0)
            throw new InvalidOperationException("Managed adapter provenance requires a positive contract version");
        if (string.IsNullOrWhiteSpace(adapter.TargetFramework) ||
            string.IsNullOrWhiteSpace(adapter.PortabilityTarget))
            throw new InvalidOperationException("Managed adapter provenance requires a target framework and portability target");

        var operations = adapter.Operations
            .Select(operation =>
            {
                if (string.IsNullOrWhiteSpace(operation.OperationId) ||
                    string.IsNullOrWhiteSpace(operation.Name) ||
                    string.IsNullOrWhiteSpace(operation.Result))
                    throw new InvalidOperationException("Managed adapter operations require an ID, Lang symbol, and result type");
                if (operation.Parameters.Any(parameter =>
                        string.IsNullOrWhiteSpace(parameter.Name) || string.IsNullOrWhiteSpace(parameter.Type)))
                    throw new InvalidOperationException("Managed adapter operation parameters require names and closed Lang types");

                return operation with
                {
                    Parameters = operation.Parameters.ToArray(),
                    Effects = CanonicalNames(operation.Effects),
                    RequiredCapabilities = CanonicalNames(operation.RequiredCapabilities)
                };
            })
            .OrderBy(operation => operation.OperationId, StringComparer.Ordinal)
            .ThenBy(operation => operation.Name, StringComparer.Ordinal)
            .ToArray();
        if (operations.Select(operation => operation.OperationId)
                .Distinct(StringComparer.Ordinal).Count() != operations.Length)
            throw new InvalidOperationException("Managed adapter provenance requires unique operation IDs");

        var assemblies = adapter.Assemblies
            .Select(assembly =>
            {
                if (string.IsNullOrWhiteSpace(assembly.Identity) ||
                    assembly.Identity.IndexOfAny(['/', '\\', ':']) >= 0)
                    throw new InvalidOperationException("Managed adapter assembly identity must not contain a path");
                return assembly with
                {
                    Path = NormalizePackageRelativePath(assembly.Path),
                    Sha256 = CanonicalSha256(assembly.Sha256)
                };
            })
            .OrderBy(assembly => assembly.Identity, StringComparer.Ordinal)
            .ThenBy(assembly => assembly.Path, StringComparer.Ordinal)
            .ToArray();
        if (assemblies.Length == 0 || assemblies.Select(assembly => assembly.Path)
                .Distinct(StringComparer.Ordinal).Count() != assemblies.Length)
            throw new InvalidOperationException("Managed adapter provenance requires unique relative assembly paths");

        return adapter with
        {
            Operations = operations,
            Assemblies = assemblies,
            ClosureSha256 = CanonicalSha256(adapter.ClosureSha256)
        };

        static IReadOnlyList<string> CanonicalNames(IEnumerable<string> names)
        {
            var result = names.ToArray();
            if (result.Any(string.IsNullOrWhiteSpace) || result.Any(name => name.Any(char.IsControl)))
                throw new InvalidOperationException("Managed adapter effect and capability names must be stable tokens");
            return result.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }
    }

    private static string NormalizePackageRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Managed adapter assembly path must be package-relative");
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (normalized[0] == '/' ||
            normalized.StartsWith("//", StringComparison.Ordinal) ||
            (segments[0].Length >= 2 && char.IsAsciiLetter(segments[0][0]) && segments[0][1] == ':') ||
            segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidOperationException("Managed adapter assembly path must be normalized and relative to its package");
        return normalized;
    }

    private static string CanonicalSha256(string value)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Managed adapter provenance requires a 64-digit SHA-256 value");
        return value.ToLowerInvariant();
    }

    private static object SourceJson(PackageSourceIdentity source) => source.Kind switch
    {
        PackageSourceKind.Root => new { kind = "root" },
        PackageSourceKind.Path => new { kind = "path", path = source.Path },
        PackageSourceKind.Git => new { kind = "git", url = source.Url, commit = source.Commit },
        _ => throw new InvalidOperationException("Unknown package source identity kind")
    };

    private static AuditPackageIdentity Identity(ResolvedPackage node) => new(
        node.Package.Manifest.Name,
        node.Package.Manifest.Version,
        StablePackagePath(node),
        node.SourceIdentity);

    private static string StablePackagePath(ResolvedPackage node) => node.SourceIdentity.Kind switch
    {
        PackageSourceKind.Root => ".",
        PackageSourceKind.Path => NormalizeRelative(node.RelativePath),
        PackageSourceKind.Git => PackageSourceCache.StableGitPackagePath(node.SourceIdentity.Url!, node.SourceIdentity.Commit!),
        _ => throw new InvalidOperationException("Unknown package source identity kind")
    };

    private static string FindNodeId(PackageDependencyGraph graph, string relativePath) =>
        graph.Nodes.First(node => string.Equals(
            StablePackagePath(node),
            relativePath,
            StringComparison.Ordinal)).Id;

    private static string HashPackageContent(
        IReadOnlyList<(string Kind, string Path, string Text)> entries,
        IEnumerable<ProcessExecutablePin>? processExecutables = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(ContentHashDomain);
        var length = new byte[sizeof(ulong)];
        foreach (var entry in entries)
        {
            AppendField(entry.Path);
            AppendField(NormalizeLineEndings(entry.Text));
        }
        foreach (var executable in (processExecutables ?? [])
                     .OrderBy(pin => pin.Os == "windows" ? 0 : 1))
        {
            AppendField("process_executable");
            AppendField(executable.Path);
            AppendField(executable.Sha256);
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

    private static object[] ProcessExecutableMetadata(IEnumerable<ProcessExecutablePin> pins) => pins
        .OrderBy(pin => pin.Os == "windows" ? 0 : 1)
        .Select(pin => (object)new
        {
            os = pin.Os,
            path = pin.Path,
            sha256 = pin.Sha256
        })
        .ToArray();

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
