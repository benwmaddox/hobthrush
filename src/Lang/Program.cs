using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
return await Driver.RunAsync(args);

internal static class Driver
{
    private static readonly HashSet<string> SupportedAotRids = new(StringComparer.Ordinal)
    {
        "win-x64",
        "linux-x64"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly UTF8Encoding StrictLabelUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const string LabelHexDigits = "0123456789ABCDEF";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] == "test")
            return TestFixtures();

        if (args.Length != 0 && args[0] == "new")
        {
            if (args.Length != 3 || args[1] is not ("lib" or "cli" or "web"))
            {
                PrintUsage();
                return 2;
            }

            return ReportProjectWorkflow(ProjectWorkflow.Create(args[1], args[2], Environment.CurrentDirectory));
        }

        if (args.Length != 0 && args[0] == "add")
        {
            if (args.Length == 2)
                return ReportProjectWorkflow(ProjectWorkflow.Add(Environment.CurrentDirectory, args[1]));

            if (args.Length == 3)
                return ReportProjectWorkflow(ProjectWorkflow.Add(args[1], args[2]));

            PrintUsage();
            return 2;
        }

        if (args.Length != 0 && args[0] == "lock")
            return RunLock(args);

        if (args.Length != 0 && args[0] == "audit")
            return AuditPackage(args);

        if (args.Length != 0 && args[0] == "inspect")
            return args.Length > 1 && args[1] == "api"
                ? InspectApi(args)
                : InspectEffects(args);

        var hasApplicationSeparator = false;
        string[] applicationArguments = [];
        if (args.Length != 0 && args[0] == "run")
        {
            var separatorIndex = -1;
            for (var index = 2; index < args.Length; index++)
            {
                if (args[index] == "--")
                {
                    separatorIndex = index;
                    break;
                }
            }

            if (separatorIndex >= 0)
            {
                hasApplicationSeparator = true;
                applicationArguments = args[(separatorIndex + 1)..];
                args = args[..separatorIndex];
            }
        }

        if (args.Length < 2)
        {
            PrintUsage();
            return 2;
        }

        var hasAotOption = args.Contains("--aot", StringComparer.Ordinal);
        var hasRidOption = args.Contains("--rid", StringComparer.Ordinal);
        if (args[0] == "test" && (hasAotOption || hasRidOption))
        {
            PrintUsage();
            return 2;
        }
        if (args[0] is not ("check" or "build" or "run" or "test"))
        {
            if (hasAotOption || hasRidOption)
            {
                return ReportBuildTargetError(
                    "The --aot and --rid options are only valid with lang build FILE_OR_PACKAGE --aot --rid RID",
                    args[1]);
            }

            PrintUsage();
            return 2;
        }

        var isAotBuild = args[0] == "build" && hasAotOption && hasRidOption;
        string? rid = null;
        if (hasAotOption || hasRidOption)
        {
            if (args[0] != "build")
            {
                return ReportBuildTargetError(
                    "The --aot and --rid options are only valid with lang build FILE_OR_PACKAGE --aot --rid RID",
                    args[1]);
            }

            if (hasRidOption && !hasAotOption)
            {
                return ReportBuildTargetError(
                    "The --aot and --rid options are only valid with lang build FILE_OR_PACKAGE --aot --rid RID",
                    args[1]);
            }

            if (args.Length != 5 || args[2] != "--aot" || args[3] != "--rid")
            {
                return ReportBuildTargetError(
                    "lang build --aot requires --rid RID (supported RIDs: win-x64, linux-x64)",
                    args[1]);
            }

            rid = args[4];
            if (!SupportedAotRids.Contains(rid))
            {
                return ReportBuildTargetError(
                    $"Unsupported AOT runtime identifier '{rid}'; supported RIDs: win-x64, linux-x64",
                    args[1]);
            }

            if (!IsCurrentHostAotRid(rid))
            {
                return ReportBuildTargetError(
                    $"NativeAOT runtime identifier '{rid}' targets a different OS than the current host; " +
                    "cross-OS publishing is not supported",
                    args[1]);
            }
        }

        var json = args[0] == "check" && args.Skip(2).Contains("--json", StringComparer.Ordinal);
        if ((args[0] == "check" && args.Skip(2).Any(arg => arg != "--json")) ||
            (!isAotBuild && args[0] != "check" && args.Length != 2))
        {
            PrintUsage();
            return 2;
        }

        string file;
        try
        {
            file = Path.GetFullPath(args[1]);
        }
        catch (Exception error) when (IsFileError(error))
        {
            PrintDiagnostics([AtStart("E_IO", $"Invalid source path: {error.Message}", args[1])], json);
            return 1;
        }

        if (Directory.Exists(file))
        {
            return await RunPackageAsync(
                args[0], file, json, isAotBuild ? rid : null,
                hasApplicationSeparator, applicationArguments);
        }

        string source;
        try
        {
            source = File.ReadAllText(file);
        }
        catch (Exception error) when (IsFileError(error))
        {
            PrintDiagnostics([AtStart("E_IO", $"Could not read source file: {error.Message}", file)], json);
            return 1;
        }

        var result = Compiler.Check(file, source);
        if (result.Diagnostics.Count != 0)
        {
            PrintDiagnostics(result.Diagnostics, json);
            return 1;
        }

        if (args[0] == "check")
        {
            if (json)
                PrintDiagnostics([], json: true);
            return 0;
        }

        if (args[0] == "test")
            return await BuildTestsAsync(result.Program!, file);

        return await BuildCheckedAsync(
            result.Program!, file, args[0], isAotBuild ? rid : null,
            sourceText: source,
            hasApplicationSeparator: hasApplicationSeparator,
            applicationArguments: applicationArguments);
    }

    private static int RunLock(string[] args)
    {
        if (args.Length != 2)
        {
            PrintUsage();
            return 2;
        }

        string packageDirectory;
        try
        {
            packageDirectory = Path.GetFullPath(args[1]);
        }
        catch (Exception error) when (IsFileError(error))
        {
            PrintDiagnostics(
            [
                AtStart("E_IO", $"Invalid package directory: {error.Message}", args[1])
            ],
            json: false);
            return 1;
        }

        var resolved = PackageLoader.ResolveGraph(packageDirectory, PackageResolutionMode.AllowFetch);
        if (resolved.Diagnostics.Count != 0 || resolved.Graph is null)
        {
            List<Diagnostic> diagnostics = resolved.Diagnostics.Count != 0
                ? resolved.Diagnostics
                : [AtStart("E_DEPENDENCY", "Could not resolve package dependency graph", packageDirectory)];
            PrintDiagnostics(diagnostics, json: false);
            return 1;
        }

        var graph = resolved.Graph!;
        var writeDiagnostics = PackageLock.Write(graph);
        if (writeDiagnostics.Count != 0)
        {
            PrintDiagnostics(writeDiagnostics, json: false);
            return 1;
        }

        var validationDiagnostics = PackageLock.Validate(graph);
        if (validationDiagnostics.Count != 0)
        {
            PrintDiagnostics(validationDiagnostics, json: false);
            return 1;
        }

        if (graph.Nodes.Any(node =>
                node.Package.Manifest.Dependencies.Count != 0 || node.Package.ManagedAdapter is not null))
            Console.WriteLine($"Wrote package lock: {Path.Combine(packageDirectory, "lang.lock")}");
        else
            Console.WriteLine($"No dependencies to lock for package '{graph.Root.Package.Manifest.Name}'.");

        return 0;
    }

    private static int AuditPackage(string[] args)
    {
        if (args.Length != 3 || args[2] != "--json")
        {
            PrintUsage();
            return 2;
        }

        string packageDirectory;
        try
        {
            packageDirectory = Path.GetFullPath(args[1]);
        }
        catch (Exception error) when (IsFileError(error))
        {
            PrintDiagnostics([AtStart("E_IO", $"Invalid package directory: {error.Message}", args[1])], json: true);
            return 1;
        }

        var resolved = ResolvePackageGraph(packageDirectory);
        if (resolved.Diagnostics.Count != 0 || resolved.Graph is null)
        {
            List<Diagnostic> diagnostics = resolved.Diagnostics.Count != 0
                ? resolved.Diagnostics
                : [AtStart("E_DEPENDENCY", "Could not resolve package dependency graph", packageDirectory)];
            PrintDiagnostics(diagnostics, json: true);
            return 1;
        }

        var graph = resolved.Graph!;
        var checkedPackage = CheckPackageGraph(graph);
        if (checkedPackage.Diagnostics.Count != 0 || checkedPackage.Program is null)
        {
            PrintDiagnostics(checkedPackage.Diagnostics, json: true);
            return 1;
        }

        try
        {
            var adapterProvenance = CreateManagedAdapterProvenance(graph, checkedPackage.Program!);
            var snapshot = AuditReport.Create(graph, checkedPackage.Program!, adapterProvenance);
            Console.Write(Encoding.UTF8.GetString(snapshot.Json));
            return 0;
        }
        catch (Exception error) when (IsFileError(error) || error is EncoderFallbackException)
        {
            PrintDiagnostics(
            [
                AtStart("E_IO", $"Could not hash package inputs for audit: {error.Message}", graph.Root.Package.ManifestFile)
            ],
            json: true);
            return 1;
        }
    }

    private static async Task<int> RunPackageAsync(
        string command,
        string packageDirectory,
        bool json,
        string? aotRid,
        bool hasApplicationSeparator = false,
        string[]? applicationArguments = null)
    {
        var resolved = ResolvePackageGraph(packageDirectory);
        if (resolved.Diagnostics.Count != 0 || resolved.Graph is null)
        {
            List<Diagnostic> diagnostics = resolved.Diagnostics.Count != 0
                ? resolved.Diagnostics
                : [AtStart("E_DEPENDENCY", "Could not resolve package dependency graph", packageDirectory)];
            PrintDiagnostics(diagnostics, json);
            return 1;
        }

        var graph = resolved.Graph!;
        var package = graph.Root.Package;
        if (aotRid is not null && package.Manifest.Kind == "web")
        {
            return ReportBuildTargetError(
                "NativeAOT publishing is not supported for web packages",
                package.ManifestFile);
        }

        if (aotRid is not null && package.Manifest.IsLibrary)
        {
            return ReportBuildTargetError(
                "NativeAOT publishing is only supported for cli packages",
                package.ManifestFile);
        }

        var checkedPackage = CheckPackageGraph(graph);
        if (checkedPackage.Diagnostics.Count != 0)
        {
            PrintDiagnostics(checkedPackage.Diagnostics, json);
            return 1;
        }

        if (command == "check")
        {
            if (json)
                PrintDiagnostics([], json: true);
            return 0;
        }

        if (command == "test")
        {
            return await BuildTestsAsync(
                checkedPackage.Program!,
                package.ManifestFile,
                package,
                graph.Root.Id,
                graph);
        }

        return await BuildCheckedAsync(
            checkedPackage.Program!,
            package.ManifestFile,
            command,
            aotRid,
            package,
            graph,
            sourceText: string.Empty,
            hasApplicationSeparator: hasApplicationSeparator,
            applicationArguments: applicationArguments);
    }

    private static (PackageDependencyGraph? Graph, List<Diagnostic> Diagnostics) ResolvePackageGraph(
        string packageDirectory)
    {
        var resolved = PackageLoader.ResolveGraph(packageDirectory);
        if (resolved.Diagnostics.Count != 0 || resolved.Graph is null)
        {
            List<Diagnostic> diagnostics = resolved.Diagnostics.Count != 0
                ? resolved.Diagnostics
                : [AtStart("E_DEPENDENCY", "Could not resolve package dependency graph", packageDirectory)];
            return (null, diagnostics);
        }

        var graph = resolved.Graph!;
        if (graph.Nodes.Any(node =>
                node.Package.Manifest.Dependencies.Count != 0 || node.Package.ManagedAdapter is not null))
        {
            var lockDiagnostics = PackageLock.Validate(graph);
            if (lockDiagnostics.Count != 0)
                return (null, lockDiagnostics);
        }

        return (graph, []);
    }

    private static CheckResult CheckPackageGraph(PackageDependencyGraph graph)
    {
        var parsed = ParsePackageSources(graph);
        if (parsed.Diagnostics.Count != 0)
            return new CheckResult(null, parsed.Diagnostics);

        var package = graph.Root.Package;
        var entryModule = package.Manifest.EntryModule;
        var entryModuleExists = entryModule is null ||
            parsed.Modules.Any(module =>
                string.Equals(module.PackageId, graph.Root.Id, StringComparison.Ordinal) &&
                string.Equals(module.Program.Module, entryModule, StringComparison.Ordinal));
        var checkedPackage = Compiler.CheckPackage(
            parsed.Modules,
            graph.Root.Id,
            entryModuleExists ? entryModule : null,
            package.Manifest.Capabilities,
            rootIsCliPackage: package.Manifest.Kind == "cli",
            rootIsWebPackage: package.Manifest.Kind == "web",
            rootConfigFields: package.Manifest.ConfigFields);
        if (checkedPackage.Diagnostics.Count == 0 && entryModuleExists)
            return checkedPackage;

        var diagnostics = checkedPackage.Diagnostics.ToList();
        if (!entryModuleExists)
        {
            diagnostics.Add(AtStart(
                "E_ENTRYPOINT",
                $"Entry module '{entryModule}' is not present under source_root",
                package.ManifestFile));
        }

        return new CheckResult(null, diagnostics);
    }

    private static int InspectEffects(string[] args)
    {
        if (args.Length != 5 || args[1] != "effects" || args[4] != "--json")
        {
            PrintUsage();
            return 2;
        }

        string packageDirectory;
        try
        {
            packageDirectory = Path.GetFullPath(args[2]);
        }
        catch (Exception error) when (IsFileError(error))
        {
            PrintDiagnostics(
            [
                AtStart("E_IO", $"Invalid package directory: {error.Message}", args[2])
            ],
            json: true);
            return 1;
        }

        var resolved = ResolvePackageGraph(packageDirectory);
        if (resolved.Diagnostics.Count != 0 || resolved.Graph is null)
        {
            PrintDiagnostics(resolved.Diagnostics, json: true);
            return 1;
        }

        var graph = resolved.Graph!;
        var checkedPackage = CheckPackageGraph(graph);
        if (checkedPackage.Diagnostics.Count != 0 || checkedPackage.Program is null)
        {
            PrintDiagnostics(checkedPackage.Diagnostics, json: true);
            return 1;
        }

        if (!TryParseInspectSymbol(args[3], out var module, out var functionName))
        {
            PrintDiagnostics(
            [
                AtStart(
                    "E_NAME_UNRESOLVED",
                    $"Function symbol '{args[3]}' is not a supported self root-package symbol",
                    graph.Root.Package.ManifestFile)
            ],
            json: true);
            return 1;
        }

        var sourceFile = graph.Root.Package.Sources
            .FirstOrDefault(source => string.Equals(source.Module, module, StringComparison.Ordinal))?.File;
        var function = sourceFile is null
            ? null
            : checkedPackage.Program.Functions.FirstOrDefault(candidate =>
                string.Equals(candidate.Module, module, StringComparison.Ordinal) &&
                string.Equals(candidate.Name, functionName, StringComparison.Ordinal) &&
                string.Equals(candidate.At.File, sourceFile, StringComparison.Ordinal));
        if (function is null)
        {
            PrintDiagnostics(
            [
                AtStart(
                    "E_NAME_UNRESOLVED",
                    $"Function symbol '{args[3]}' could not be resolved in the root package",
                    graph.Root.Package.ManifestFile)
            ],
            json: true);
            return 1;
        }

        var inferredEffects = function.InferredEffects;
        var requiredCapabilities = CheckedReportFacts.RequiredCapabilities(inferredEffects);
        var manifestGrants = graph.Root.Package.Manifest.Capabilities
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();
        var trustedOperations = new List<TrustedOperation>();
        if (checkedPackage.Program.Commands.Any(command =>
                string.Equals(command.PackageId, graph.Root.Id, StringComparison.Ordinal) &&
                command.HandlerFunctionId == function.Id))
        {
            trustedOperations.Add(new TrustedOperation("cli.argument_decode", "trusted_host", []));
            trustedOperations.Add(new TrustedOperation("cli.output", "trusted_host", []));
        }

        trustedOperations.AddRange(FindTrustedAdapterOperations(checkedPackage.Program, function));

        var output = new
        {
            schema_version = 1,
            symbol = args[3],
            declared_effects = function.DeclaredEffects,
            inferred_effects = inferredEffects,
            effect_paths = function.InferredEffectPaths
                .Select(path => new { effect = path.Effect, steps = path.Steps })
                .ToArray(),
            required_capabilities = requiredCapabilities,
            manifest_grants = manifestGrants,
            trusted_operations = trustedOperations
        };
        Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
        return 0;
    }

    private static int InspectApi(string[] args)
    {
        if (args.Length != 4 || args[1] != "api" || args[3] != "--json")
        {
            PrintUsage();
            return 2;
        }

        string packageDirectory;
        try
        {
            packageDirectory = Path.GetFullPath(args[2]);
        }
        catch (Exception error) when (IsFileError(error))
        {
            PrintDiagnostics(
            [
                AtStart("E_IO", $"Invalid package directory: {error.Message}", args[2])
            ],
            json: true);
            return 1;
        }

        var resolved = ResolvePackageGraph(packageDirectory);
        if (resolved.Diagnostics.Count != 0 || resolved.Graph is null)
        {
            PrintDiagnostics(resolved.Diagnostics, json: true);
            return 1;
        }

        var graph = resolved.Graph!;
        var checkedPackage = CheckPackageGraph(graph);
        if (checkedPackage.Diagnostics.Count != 0 || checkedPackage.Program is null)
        {
            PrintDiagnostics(checkedPackage.Diagnostics, json: true);
            return 1;
        }

        var program = checkedPackage.Program;
        var packageIdentities = graph.Nodes.ToDictionary(
            node => node.Id,
            node => new ApiPackageIdentity(node.Package.Manifest.Name, node.Package.Manifest.Version),
            StringComparer.Ordinal);
        var sourceAliasesByPackageId = graph.Root.DependencyIds
            .GroupBy(dependency => dependency.Value, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(dependency => dependency.Key)
                    .OrderBy(alias => alias, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        sourceAliasesByPackageId[graph.Root.Id] = ["self"];
        var packageReferences = new Dictionary<string, ApiPackageReference>(StringComparer.Ordinal)
        {
            [graph.Root.Id] = new(
                "self",
                packageIdentities[graph.Root.Id].Name,
                packageIdentities[graph.Root.Id].Version)
        };
        var dependencies = graph.Root.DependencyIds
            .OrderBy(dependency => dependency.Key, StringComparer.Ordinal)
            .Select(dependency =>
            {
                var package = packageIdentities[dependency.Value];
                packageReferences.TryAdd(dependency.Value, new ApiPackageReference(
                    dependency.Key,
                    package.Name,
                    package.Version));
                return new ApiPackageReference(dependency.Key, package.Name, package.Version);
            })
            .ToArray();
        var visiblePackageIds = packageReferences.Keys.ToHashSet(StringComparer.Ordinal);
        var functionsById = program.Functions.ToDictionary(function => function.Id);
        var unionsById = program.Unions.ToDictionary(union => union.Id);
        var structsById = program.Structs.ToDictionary(structure => structure.Id);

        var functions = program.Functions
            .Where(function => function.Public && visiblePackageIds.Contains(function.PackageId))
            .Select(function => new
            {
                id = ApiDeclarationId(function.PackageId, function.Module, function.Name, packageReferences),
                source_ids = ApiDeclarationIds(function.PackageId, function.Module, function.Name, sourceAliasesByPackageId),
                package = packageReferences[function.PackageId],
                is_async = function.IsAsync,
                type_parameters = function.TypeParameters
                    .Select(typeParameter => new
                    {
                        name = typeParameter.DisplayName,
                        ordinal = typeParameter.TypeParameterOrdinal
                    })
                    .ToArray(),
                parameters = function.Parameters
                    .Select(parameter => new
                    {
                        name = parameter.Name,
                        type = ApiType(parameter.Type, packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById)
                    })
                    .ToArray(),
                return_type = ApiType(function.ReturnType, packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById),
                declared_effects = function.DeclaredEffects,
                inferred_effects = function.InferredEffects,
                effect_paths = function.InferredEffectPaths
                    .Select(path => new
                    {
                        effect = path.Effect,
                        steps = ApiEffectPathSteps(path, functionsById, packageReferences, packageIdentities, sourceAliasesByPackageId)
                    })
                    .ToArray(),
                calls = ApiDirectCalls(function, packageIdentities, sourceAliasesByPackageId),
                required_capabilities = RequiredCapabilities(function.InferredEffects)
            })
            .OrderBy(function => function.id, StringComparer.Ordinal)
            .ToArray();

        var structs = program.Structs
            .Where(structure => structure.Public && visiblePackageIds.Contains(structure.PackageId))
            .Select(structure => new
            {
                id = ApiDeclarationId(structure.PackageId, structure.Module, structure.Name, packageReferences),
                source_ids = ApiDeclarationIds(structure.PackageId, structure.Module, structure.Name, sourceAliasesByPackageId),
                package = packageReferences[structure.PackageId],
                type_parameters = structure.TypeParameters
                    .Select(typeParameter => new
                    {
                        name = typeParameter.DisplayName,
                        ordinal = typeParameter.TypeParameterOrdinal
                    })
                    .ToArray(),
                fields = structure.Fields
                    .Select(field => new
                    {
                        name = field.Name,
                        type = ApiType(field.Type, packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById)
                    })
                    .ToArray()
            })
            .OrderBy(structure => structure.id, StringComparer.Ordinal)
            .ToArray();

        var unions = program.Unions
            .Where(union => union.Public && visiblePackageIds.Contains(union.PackageId))
            .Select(union => new
            {
                id = ApiDeclarationId(union.PackageId, union.Module, union.Name, packageReferences),
                source_ids = ApiDeclarationIds(union.PackageId, union.Module, union.Name, sourceAliasesByPackageId),
                package = packageReferences[union.PackageId],
                type_parameters = union.TypeParameters
                    .Select(typeParameter => new
                    {
                        name = typeParameter.DisplayName,
                        ordinal = typeParameter.TypeParameterOrdinal
                    })
                    .ToArray(),
                variants = union.Variants
                    .Select(variant => new
                    {
                        name = variant.Name,
                        payload = variant.Fields
                            .Select(field => new
                            {
                                name = field.Name,
                                type = ApiType(field.Type, packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById)
                            })
                            .ToArray()
                    })
                    .ToArray()
            })
            .OrderBy(union => union.id, StringComparer.Ordinal)
            .ToArray();

        var commands = program.Commands
            .Where(command => command.PackageId == graph.Root.Id)
            .Select(command => new
            {
                id = ApiDeclarationId(command.PackageId, command.Module, command.Name, packageReferences),
                package = packageReferences[command.PackageId],
                help = command.Help,
                inputs = command.Inputs
                    .Select(input => new
                    {
                        name = input.Name,
                        kind = input.Kind.ToString().ToLowerInvariant(),
                        type = ApiType(input.Type, packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById),
                        help = input.Help,
                        default_value = ApiCommandDefault(input.Default)
                })
                .ToArray(),
                handler = ApiFunctionId(command.HandlerFunctionId, functionsById, packageReferences),
                handler_is_async = command.HandlerIsAsync,
                handler_source_ids = ApiFunctionSourceIds(command.HandlerFunctionId, functionsById, sourceAliasesByPackageId),
                error_formatter = ApiFunctionId(command.ErrorFunctionId, functionsById, packageReferences),
                error_formatter_source_ids = ApiFunctionSourceIds(command.ErrorFunctionId, functionsById, sourceAliasesByPackageId),
                error_type = ApiType(command.ErrorType, packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById),
                required_capabilities = command.Capabilities
                    .Select(capability => capability.Kind switch
                    {
                        CheckedCapabilityKind.FsRead => "fs.read",
                        CheckedCapabilityKind.FsWrite => "fs.write",
                        CheckedCapabilityKind.HttpClient => "net.client",
                        CheckedCapabilityKind.Config => "env.read",
                        CheckedCapabilityKind.Secrets => "secret.reveal",
                        CheckedCapabilityKind.Logger => "log.write",
                        CheckedCapabilityKind.ProcessRunner => "process.spawn",
                        _ => throw new InvalidOperationException("Unknown checked command capability")
                    })
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(capability => capability, StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(command => command.id, StringComparer.Ordinal)
            .ToArray();

        var routes = program.Routes
            .Select(route => new
            {
                method = route.Method,
                path = route.Path,
                body_type = route.BodyType is null
                    ? null
                    : ApiType(route.BodyType, packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById),
                handler = ApiFunctionId(route.HandlerFunctionId, functionsById, packageReferences),
                handler_is_async = route.HandlerIsAsync,
                response_type = ApiNominalType(
                    "union",
                    unionsById[route.ReplyUnionId].PackageId,
                    unionsById[route.ReplyUnionId].Module,
                    unionsById[route.ReplyUnionId].Name,
                    [],
                    packageReferences,
                    packageIdentities,
                    sourceAliasesByPackageId,
                    structsById,
                    unionsById),
                handler_source_ids = ApiFunctionSourceIds(route.HandlerFunctionId, functionsById, sourceAliasesByPackageId),
                parameters = route.Bindings
                    .OrderBy(binding => binding.HandlerParameterIndex)
                    .Select(binding => new
                    {
                        name = binding.WireName,
                        @in = binding.Kind switch
                        {
                            CheckedRouteBindingKind.Path => "path",
                            CheckedRouteBindingKind.Query => "query",
                            _ => throw new InvalidOperationException("Unknown checked route binding kind")
                        },
                        required = binding.Kind == CheckedRouteBindingKind.Path || !binding.IsOptional,
                        type = ApiType(binding.Type, packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById),
                        handler_parameter_index = binding.HandlerParameterIndex
                    })
                    .ToArray(),
                responses = route.Responses
                    .Select(response => new
                    {
                        variant = response.VariantName,
                        status = response.StatusCode,
                        content_type = response.ContentKind?.ToString().ToLowerInvariant(),
                        payload_type = response.PayloadType is null
                            ? null
                            : ApiType(response.PayloadType, packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById)
                    })
                    .ToArray(),
                required_capabilities = route.Capabilities
                    .Select(capability => capability.Kind switch
                    {
                        CheckedCapabilityKind.FsWrite => "fs.write",
                        CheckedCapabilityKind.DbRead => "db.read",
                        CheckedCapabilityKind.DbWrite => "db.write",
                        CheckedCapabilityKind.HttpClient => "net.client",
                        CheckedCapabilityKind.Config => "env.read",
                        CheckedCapabilityKind.Secrets => "secret.reveal",
                        CheckedCapabilityKind.Logger => "log.write",
                        _ => throw new InvalidOperationException("Unknown checked route capability")
                    })
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(capability => capability, StringComparer.Ordinal)
                    .ToArray(),
                capability_parameters = route.Capabilities
                    .Select(capability => new
                    {
                        name = capability.ParameterName,
                        capability = capability.Kind switch
                        {
                            CheckedCapabilityKind.FsWrite => "fs.write",
                            CheckedCapabilityKind.DbRead => "db.read",
                            CheckedCapabilityKind.DbWrite => "db.write",
                            CheckedCapabilityKind.HttpClient => "net.client",
                            CheckedCapabilityKind.Config => "env.read",
                            CheckedCapabilityKind.Secrets => "secret.reveal",
                            CheckedCapabilityKind.Logger => "log.write",
                            _ => throw new InvalidOperationException("Unknown checked route capability")
                        }
                    })
                    .ToArray()
            })
            .OrderBy(route => route.method, StringComparer.Ordinal)
            .ThenBy(route => route.path, StringComparer.Ordinal)
            .ToArray();

        var output = new
        {
            schema_version = 9,
            package = packageReferences[graph.Root.Id],
            dependencies,
            manifest_grants = graph.Root.Package.Manifest.Capabilities
                .OrderBy(capability => capability, StringComparer.Ordinal)
                .ToArray(),
            config = CheckedReportFacts.ConfigMetadata(program.ConfigFields),
            http_origin = graph.Root.Package.Manifest.HttpOrigin,
            process_executables = graph.Root.Package.Manifest.ProcessExecutables
                .OrderBy(pin => pin.Os == "windows" ? 0 : 1)
                .Select(pin => new
                {
                    os = pin.Os,
                    path = pin.Path,
                    sha256 = pin.Sha256
                })
                .ToArray(),
            functions,
            structs,
            unions,
            commands,
            routes
        };
        Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
        return 0;
    }

    private static object ApiType(
        LangType type,
        IReadOnlyDictionary<string, ApiPackageReference> packageReferences,
        IReadOnlyDictionary<string, ApiPackageIdentity> packageIdentities,
        IReadOnlyDictionary<string, string[]> sourceAliasesByPackageId,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById) => type.Kind switch
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
            LangTypeKind.SecretText => new
            {
                kind = "secret",
                item = ApiType(type.Arguments[0], packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById)
            },
            LangTypeKind.FsError => new { kind = "primitive", name = "FsError" },
            LangTypeKind.HttpClient => new { kind = "primitive", name = "HttpClient" },
            LangTypeKind.HttpResponse => new { kind = "primitive", name = "HttpResponse" },
            LangTypeKind.HttpError => new { kind = "primitive", name = "HttpError" },
            LangTypeKind.ProcessRunner => new { kind = "primitive", name = "ProcessRunner" },
            LangTypeKind.ProcessOutput => new { kind = "primitive", name = "ProcessOutput" },
            LangTypeKind.ProcessError => new { kind = "primitive", name = "ProcessError" },
            LangTypeKind.DbRead => new { kind = "primitive", name = "DbRead" },
            LangTypeKind.DbWrite => new { kind = "primitive", name = "DbWrite" },
            LangTypeKind.Transaction => new { kind = "primitive", name = "Transaction" },
            LangTypeKind.DbError => new { kind = "primitive", name = "DbError" },
            LangTypeKind.TypeParameter => new
            {
                kind = "type_parameter",
                name = type.DisplayName,
                ordinal = type.TypeParameterOrdinal
            },
            LangTypeKind.List => new
            {
                kind = "list",
                item = ApiType(type.Arguments[0], packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById)
            },
            LangTypeKind.Map => new
            {
                kind = "map",
                key = ApiType(type.Arguments[0], packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById),
                value = ApiType(type.Arguments[1], packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById)
            },
            LangTypeKind.Option => new
            {
                kind = "option",
                item = ApiType(type.Arguments[0], packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById)
            },
            LangTypeKind.Result => new
            {
                kind = "result",
                ok = ApiType(type.Arguments[0], packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById),
                error = ApiType(type.Arguments[1], packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById)
            },
            LangTypeKind.Struct when structsById.TryGetValue(type.StructId, out var structure) =>
                        ApiNominalType(
                            "struct",
                            structure.PackageId,
                            structure.Module,
                            structure.Name,
                            type.Arguments,
                            packageReferences,
                            packageIdentities,
                            sourceAliasesByPackageId,
                            structsById,
                            unionsById),
            LangTypeKind.Union when unionsById.TryGetValue(type.UnionId, out var union) =>
                        ApiNominalType(
                            "union",
                            union.PackageId,
                            union.Module,
                            union.Name,
                            type.Arguments,
                            packageReferences,
                            packageIdentities,
                            sourceAliasesByPackageId,
                            structsById,
                            unionsById),
            LangTypeKind.Error => throw new InvalidOperationException("A checked API cannot contain an error type"),
            LangTypeKind.Struct or LangTypeKind.Union => throw new InvalidOperationException("A checked type refers to an unknown declaration"),
            _ => throw new InvalidOperationException($"Unsupported checked type kind '{type.Kind}'")
        };

    private static object ApiNominalType(
        string declarationKind,
        string packageId,
        string module,
        string name,
        IReadOnlyList<LangType> typeArguments,
        IReadOnlyDictionary<string, ApiPackageReference> packageReferences,
        IReadOnlyDictionary<string, ApiPackageIdentity> packageIdentities,
        IReadOnlyDictionary<string, string[]> sourceAliasesByPackageId,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById)
    {
        if (!packageIdentities.TryGetValue(packageId, out var package))
            throw new InvalidOperationException("A checked nominal type has no resolved package identity");

        var sourceIds = ApiDeclarationIds(packageId, module, name, sourceAliasesByPackageId);
        return new
        {
            kind = "nominal",
            declaration_kind = declarationKind,
            source_id = sourceIds.FirstOrDefault(),
            source_ids = sourceIds,
            package,
            module,
            name,
            type_arguments = typeArguments
                .Select(argument => ApiType(argument, packageReferences, packageIdentities, sourceAliasesByPackageId, structsById, unionsById))
                .ToArray()
        };
    }

    private static object[] ApiEffectPathSteps(
        CheckedEffectPath path,
        IReadOnlyDictionary<int, CheckedFunction> functionsById,
        IReadOnlyDictionary<string, ApiPackageReference> packageReferences,
        IReadOnlyDictionary<string, ApiPackageIdentity> packageIdentities,
        IReadOnlyDictionary<string, string[]> sourceAliasesByPackageId)
    {
        var steps = new List<object>(path.FunctionIds.Count + 1);
        foreach (var functionId in path.FunctionIds)
        {
            if (!functionsById.TryGetValue(functionId, out var function) ||
                !packageIdentities.TryGetValue(function.PackageId, out var package))
                throw new InvalidOperationException("A checked effect path refers to unresolved package metadata");

            var sourceIds = ApiDeclarationIds(function.PackageId, function.Module, function.Name, sourceAliasesByPackageId);
            steps.Add(new
            {
                kind = "function",
                source_id = sourceIds.FirstOrDefault(),
                source_ids = sourceIds,
                package,
                module = function.Module,
                name = function.Name
            });
        }

        steps.Add(new { kind = "operation", name = path.IntrinsicName });
        return steps.ToArray();
    }

    private static object[] ApiDirectCalls(
        CheckedFunction function,
        IReadOnlyDictionary<string, ApiPackageIdentity> packageIdentities,
        IReadOnlyDictionary<string, string[]> sourceAliasesByPackageId)
    {
        return function.Calls
            .Distinct()
            .Select(call =>
            {
                if (!packageIdentities.TryGetValue(call.PackageId, out var package))
                    throw new InvalidOperationException("A checked direct call has no resolved package identity");

                return new
                {
                    Call = call,
                    Package = package,
                    SourceIds = ApiDeclarationIds(call.PackageId, call.Module, call.Name, sourceAliasesByPackageId)
                };
            })
            .OrderBy(call => call.SourceIds.FirstOrDefault() ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(call => call.Package.Name, StringComparer.Ordinal)
            .ThenBy(call => call.Package.Version, StringComparer.Ordinal)
            .ThenBy(call => call.Call.Module, StringComparer.Ordinal)
            .ThenBy(call => call.Call.Name, StringComparer.Ordinal)
            .Select(call => (object)new
            {
                source_id = call.SourceIds.FirstOrDefault(),
                source_ids = call.SourceIds,
                package = call.Package,
                module = call.Call.Module,
                name = call.Call.Name
            })
            .ToArray();
    }

    private static string ApiFunctionId(
        int functionId,
        IReadOnlyDictionary<int, CheckedFunction> functionsById,
        IReadOnlyDictionary<string, ApiPackageReference> packageReferences)
    {
        if (!functionsById.TryGetValue(functionId, out var function))
            throw new InvalidOperationException("A checked application reference points to an unknown function");
        return ApiDeclarationId(function.PackageId, function.Module, function.Name, packageReferences);
    }

    private static string[] ApiFunctionSourceIds(
        int functionId,
        IReadOnlyDictionary<int, CheckedFunction> functionsById,
        IReadOnlyDictionary<string, string[]> sourceAliasesByPackageId)
    {
        if (!functionsById.TryGetValue(functionId, out var function))
            throw new InvalidOperationException("A checked application reference points to an unknown function");
        return ApiDeclarationIds(function.PackageId, function.Module, function.Name, sourceAliasesByPackageId);
    }

    private static string ApiDeclarationId(
        string packageId,
        string module,
        string name,
        IReadOnlyDictionary<string, ApiPackageReference> packageReferences)
    {
        if (!packageReferences.TryGetValue(packageId, out var package))
            throw new InvalidOperationException("A source-facing API reference has no root dependency alias");
        return $"{package.Alias}::{module}::{name}";
    }

    private static string[] ApiDeclarationIds(
        string packageId,
        string module,
        string name,
        IReadOnlyDictionary<string, string[]> sourceAliasesByPackageId) =>
        sourceAliasesByPackageId.TryGetValue(packageId, out var aliases)
            ? aliases.Select(alias => $"{alias}::{module}::{name}").ToArray()
            : [];

    private static object? ApiCommandDefault(CheckedCommandLiteral? value) => value switch
    {
        null => null,
        { Kind: CheckedCommandLiteralKind.Text } text => new { kind = "text", value = text.TextValue },
        { Kind: CheckedCommandLiteralKind.I32 } integer => new { kind = "i32", value = integer.IntegerValue },
        { Kind: CheckedCommandLiteralKind.Boolean } boolean => new { kind = "bool", value = boolean.BooleanValue },
        _ => throw new InvalidOperationException("Unknown checked command default value")
    };

    private sealed record ApiPackageReference(string Alias, string Name, string Version);
    private sealed record ApiPackageIdentity(string Name, string Version);

    private static string[] RequiredCapabilities(IEnumerable<string> effects) =>
        CheckedReportFacts.RequiredCapabilities(effects);

    private static IReadOnlyList<TrustedOperation> FindTrustedAdapterOperations(
        CheckedProgram program,
        CheckedFunction root) =>
        CheckedReportFacts.FindTrustedAdapterOperations(program, root);

    private static IReadOnlyList<AuditManagedAdapterProvenance> CreateManagedAdapterProvenance(
        PackageDependencyGraph? graph,
        CheckedProgram program)
    {
        if (graph is null)
            return [];

        var provenance = new List<AuditManagedAdapterProvenance>();
        foreach (var node in graph.Nodes)
        {
            if (node.Package.ManagedAdapter is not { } adapter)
                continue;

            var functionsByOperation = program.Functions
                .Where(function => function.PackageId == node.Id &&
                    function.AdapterBinding is { } binding && binding.BridgeId == adapter.BridgeId)
                .GroupBy(function => function.AdapterBinding!.OperationId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(function => function.Module, StringComparer.Ordinal)
                        .ThenBy(function => function.Name, StringComparer.Ordinal)
                        .First(),
                    StringComparer.Ordinal);

            var operations = new List<AuditManagedAdapterOperation>();
            foreach (var definition in adapter.Definition.Operations.OrderBy(
                         operation => operation.OperationId,
                         StringComparer.Ordinal))
            {
                if (!functionsByOperation.TryGetValue(definition.OperationId, out var function) ||
                    function.AdapterBinding is not { } binding)
                    continue;

                operations.Add(new AuditManagedAdapterOperation(
                    binding.OperationId,
                    $"{function.Module}::{function.Name}",
                    function.Parameters
                        .Select(parameter => new AuditManagedAdapterParameter(
                            parameter.Name,
                            parameter.Type.DisplayName))
                        .ToArray(),
                    function.ReturnType.DisplayName,
                    function.IsAsync,
                    definition.Effects,
                    definition.RequiredCapabilities));
            }

            var assemblyIdentity =
                $"{adapter.Definition.AssemblyName}, Version={adapter.Definition.AssemblyVersion}, " +
                $"Culture={adapter.Definition.AssemblyCulture}, " +
                $"PublicKeyToken={adapter.Definition.AssemblyPublicKeyToken}";
            var contractVersion = int.Parse(
                adapter.Definition.CatalogRevision,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture);
            provenance.Add(AuditReport.CreateManagedAdapterProvenance(
                node,
                adapter.BridgeId,
                contractVersion,
                adapter.TargetFramework,
                adapter.PortabilityTarget,
                operations,
                [new AuditManagedAdapterAssembly(
                    assemblyIdentity,
                    adapter.AssemblyPath,
                    adapter.AssemblySha256)],
                adapter.ClosureSha256));
        }

        return AuditReport.CanonicalizeManagedAdapters(provenance);
    }

    private static bool TryParseInspectSymbol(string symbol, out string module, out string function)
    {
        module = string.Empty;
        function = string.Empty;
        var segments = symbol.Split("::", StringSplitOptions.None);
        if (segments.Length < 3 || segments[0] != "self" || !segments.Skip(1).All(IsLanguageIdentifier))
            return false;

        module = string.Join("::", segments.Skip(1).SkipLast(1));
        function = segments[^1];
        return true;
    }

    private static bool IsLanguageIdentifier(string identifier) =>
        identifier.Length != 0 &&
        (char.IsLetter(identifier[0]) || identifier[0] == '_') &&
        identifier.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static string PackageDisplayLabel(LoadedPackage package) =>
        $"{PercentEncodeLabelComponent(package.Manifest.Name)}@{PercentEncodeLabelComponent(package.Manifest.Version)}";

    private static string PercentEncodeLabelComponent(string value)
    {
        // Preserve RFC 3986 unreserved bytes and percent-encode all others with uppercase hex.
        var bytes = StrictLabelUtf8.GetBytes(value);
        var encoded = new StringBuilder(bytes.Length);
        foreach (var valueByte in bytes)
        {
            if (IsUnreservedLabelByte(valueByte))
            {
                encoded.Append((char)valueByte);
                continue;
            }

            encoded.Append('%');
            encoded.Append(LabelHexDigits[valueByte >> 4]);
            encoded.Append(LabelHexDigits[valueByte & 0x0F]);
        }

        return encoded.ToString();
    }

    private static bool IsUnreservedLabelByte(byte value) =>
        (value >= (byte)'A' && value <= (byte)'Z') ||
        (value >= (byte)'a' && value <= (byte)'z') ||
        (value >= (byte)'0' && value <= (byte)'9') ||
        value == (byte)'-' || value == (byte)'.' || value == (byte)'_' || value == (byte)'~';

    private static (List<PackageModuleInput> Modules, List<Diagnostic> Diagnostics) ParsePackageSources(
        PackageDependencyGraph graph)
    {
        var sourceCount = graph.Nodes.Sum(node => node.Package.Sources.Count);
        var modules = new List<PackageModuleInput>(sourceCount);
        var diagnostics = new List<Diagnostic>();
        foreach (var package in graph.Nodes)
        {
            foreach (var source in package.Package.Sources)
            {
                var sourceDiagnostics = new List<Diagnostic>();
                var tokens = Lexer.Scan(source.Text, source.File, sourceDiagnostics);
                ParsedProgram? parsed = null;
                if (sourceDiagnostics.Count == 0 && (tokens.Count == 0 || tokens[0].Text != "module"))
                {
                    var at = tokens.Count == 0
                        ? new Range(1, 1, 1, 1)
                        : tokens[0].Range;
                    sourceDiagnostics.Add(new Diagnostic(
                        "E_MODULE_PATH",
                        $"Source path module '{source.Module}' requires a matching module declaration",
                        source.File,
                        at));
                }
                else if (sourceDiagnostics.Count == 0)
                    parsed = new Parser(tokens, source.File, sourceDiagnostics).Parse();

                if (parsed is null && sourceDiagnostics.Count == 0)
                    sourceDiagnostics.Add(AtStart("E_SYNTAX", "Could not parse package module", source.File));

                if (parsed is not null && !string.Equals(parsed.Module, source.Module, StringComparison.Ordinal))
                {
                    sourceDiagnostics.Add(new Diagnostic(
                        "E_MODULE_PATH",
                        $"Module header '{parsed.Module}' does not match source path module '{source.Module}'",
                        parsed.ModuleAt.File,
                        parsed.ModuleAt.Range));
                }

                diagnostics.AddRange(sourceDiagnostics);
                if (parsed is not null)
                    modules.Add(new PackageModuleInput(
                        package.Id,
                        parsed,
                        package.DependencyIds,
                        PackageDisplayLabel(package.Package),
                        package.Package.ManagedAdapter?.Definition.BridgeId));
            }
        }

        return (modules, diagnostics);
    }

    private static void PrintUsage() =>
        Console.Error.WriteLine("Usage: lang new lib|cli|web NAME | lang add SOURCE | lang add PACKAGE_DIRECTORY SOURCE | lang check FILE_OR_PACKAGE [--json] | lang build FILE_OR_PACKAGE [--aot --rid RID] | lang run FILE_OR_PACKAGE [-- APP_ARGS] | lang lock PACKAGE_DIRECTORY | lang audit PACKAGE_DIRECTORY --json | lang inspect effects PACKAGE_DIRECTORY SYMBOL --json | lang inspect api PACKAGE_DIRECTORY --json | lang test [FILE_OR_PACKAGE]");

    private static int ReportProjectWorkflow(ProjectWorkflowResult result)
    {
        if (result.Diagnostics.Count != 0)
            PrintDiagnostics(result.Diagnostics, json: false);
        if (result.Message is not null)
            Console.WriteLine(result.Message);
        return result.ExitCode;
    }

    private static int ReportBuildTargetError(string message, string file)
    {
        PrintDiagnostics([AtStart("E_BUILD_TARGET", message, file)], json: false);
        return 1;
    }

    private static void PrintDiagnostics(IReadOnlyList<Diagnostic> diagnostics, bool json)
    {
        if (diagnostics.Count == 0 && !json)
            return;

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, diagnostics }, JsonOptions));
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            Console.Error.WriteLine(
                $"{diagnostic.File}:{diagnostic.Range.StartLine}:{diagnostic.Range.StartColumn}: " +
                $"{diagnostic.Code}: {diagnostic.Message}");
        }
    }

    private static Diagnostic AtStart(string code, string message, string file) =>
        new(code, message, file, new Range(1, 1, 1, 1));

    private static bool IsFileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static async Task<int> BuildTestsAsync(
        CheckedProgram program,
        string sourceFile,
        LoadedPackage? package = null,
        string? rootPackageId = null,
        PackageDependencyGraph? packageGraph = null)
    {
        const string assemblyName = "GeneratedTests";
        var generatedDirectory = Path.Combine(
            Path.GetTempPath(),
            "lang-generated",
            Guid.NewGuid().ToString("N"));
        var projectFile = Path.Combine(generatedDirectory, "Generated.csproj");
        var stagedAssemblyFile = Path.Combine(
            generatedDirectory,
            "bin",
            "Release",
            "net10.0",
            assemblyName + ".dll");

        try
        {
            Directory.CreateDirectory(generatedDirectory);
            var adapterReferences = StageManagedAdapters(packageGraph, package, generatedDirectory);
            File.WriteAllText(
                projectFile,
                ProjectFileContents(
                    executable: true,
                    aotRid: null,
                    assemblyName: assemblyName,
                    managedAdapters: adapterReferences));
            File.WriteAllText(
                Path.Combine(generatedDirectory, "Program.cs"),
                Emitter.EmitTests(program, rootPackageId));
        }
        catch (ManagedAdapterPreparationException error)
        {
            PrintDiagnostics(
            [
                AtStart("E_ADAPTER", error.Message, sourceFile)
            ],
            json: false);
            TryCleanupGeneratedDirectory(generatedDirectory, sourceFile);
            return 1;
        }
        catch (Exception error) when (IsFileError(error))
        {
            PrintDiagnostics(
            [
                AtStart("E_IO", $"Could not write generated test project: {error.Message}", sourceFile)
            ],
            json: false);
            TryCleanupGeneratedDirectory(generatedDirectory, sourceFile);
            return 1;
        }

        try
        {
            var dotnet = ResolveDotnetHost();
            var build = await ExecAsync(
                dotnet,
                ["build", projectFile, "--nologo", "-v:q", "--configuration", "Release"],
                FindRoot() ?? Directory.GetCurrentDirectory(),
                sourceFile);

            if (build is null)
                return 1;

            if (build.ExitCode != 0)
            {
                PrintDiagnostics(
                [
                    AtStart("E_BUILD", "The generated managed test project failed to build", sourceFile)
                ],
                json: false);
                WriteProcessOutputToError(build);
                return build.ExitCode;
            }

            var run = await ExecAsync(
                dotnet,
                [stagedAssemblyFile],
                package?.Root ?? Directory.GetCurrentDirectory(),
                sourceFile,
                forwardOutput: true);

            return run?.ExitCode ?? 1;
        }
        finally
        {
            TryCleanupGeneratedDirectory(generatedDirectory, sourceFile);
        }
    }

    private static async Task<int> BuildCheckedAsync(
        CheckedProgram program,
        string sourceFile,
        string command,
        string? aotRid,
        LoadedPackage? package = null,
        PackageDependencyGraph? packageGraph = null,
        string sourceText = "",
        bool hasApplicationSeparator = false,
        string[]? applicationArguments = null)
    {
        var isWebPackage = package?.Manifest.Kind == "web";
        var entry = package is null
            ? program.Functions.FirstOrDefault(IsRunnableEntryPoint)
            : program.EntryFunctionId is { } entryFunctionId
                ? program.Functions.FirstOrDefault(function => function.Id == entryFunctionId)
                : null;
        var entryCommand = program.EntryCommandId is int entryCommandId
            ? program.Commands.FirstOrDefault(command => command.Id == entryCommandId)
            : null;
        var needsProcessRunner = program.Commands.Any(command =>
            command.Capabilities.Any(capability => capability.Kind == CheckedCapabilityKind.ProcessRunner));
        var selectedProcessExecutable = package?.SelectedProcessExecutable;
        var processRunnerPin = needsProcessRunner ? selectedProcessExecutable : null;
        if (needsProcessRunner && processRunnerPin is null)
        {
            PrintDiagnostics(
            [
                AtStart("E_PROCESS_EXECUTABLE", "The current-host process executable pin is unavailable", sourceFile)
            ],
            json: false);
            return 1;
        }
        var processRunnerOptions = processRunnerPin is null
            ? null
            : new ProcessRunnerRuntimeOptions(
                ProcessRunnerArtifactFileName(processRunnerPin.Os, package?.Manifest.Name ?? "Generated"),
                processRunnerPin.Sha256);
        if (isWebPackage && program.Routes.Count == 0)
        {
            PrintDiagnostics(
            [
                AtStart(
                    "E_ENTRYPOINT",
                    "Web package entry module must declare at least one route",
                    sourceFile)
            ],
            json: false);
            return 1;
        }

        if (aotRid is not null && isWebPackage)
        {
            return ReportBuildTargetError(
                "NativeAOT publishing is not supported for web packages",
                sourceFile);
        }

        if (aotRid is not null && entry is null && entryCommand is null)
        {
            return ReportBuildTargetError(
                "lang build --aot requires fn main() -> i32, bool, or Text with no parameters, or a command entry",
                sourceFile);
        }

        if (command == "run" && hasApplicationSeparator && isWebPackage)
        {
            if (applicationArguments is not { Length: 2 } ||
                applicationArguments[0] != "--urls" ||
                string.IsNullOrWhiteSpace(applicationArguments[1]))
            {
                PrintUsage();
                return 2;
            }
        }
        else if (command == "run" && hasApplicationSeparator && entryCommand is null)
        {
            PrintUsage();
            return 2;
        }

        if (command == "run" && entry is null && entryCommand is null && !isWebPackage)
        {
            PrintDiagnostics(
            [
                AtStart(
                    "E_ENTRYPOINT",
                    "lang run requires fn main() -> i32, bool, or Text with no parameters",
                    sourceFile)
            ],
            json: false);
            return 1;
        }

        var managedAdapters = GetManagedAdapterDescriptors(packageGraph, package);
        var unsupportedAotAdapter = aotRid is null
            ? null
            : managedAdapters.FirstOrDefault(adapter => !adapter.Definition.AotSupported);
        if (unsupportedAotAdapter is not null)
        {
            return ReportBuildTargetError(
                $"Managed adapter '{unsupportedAotAdapter.Definition.BridgeId}' does not support NativeAOT publishing",
                sourceFile);
        }

        var executable = isWebPackage || entry is not null || entryCommand is not null;
        var usesDatabaseAdapter = package?.WebDatabaseOptions is not null || UsesDatabaseAdapter(program);
        var assemblyName = package?.Manifest.Name ?? "Generated";
        var generatedDirectory = Path.Combine(
            Path.GetTempPath(),
            "lang-generated",
            Guid.NewGuid().ToString("N"));

        string projectFile;
        string stagedAssemblyFile;
        var stagedPublishDirectory = Path.Combine(generatedDirectory, "publish");
        try
        {
            Directory.CreateDirectory(generatedDirectory);
            if (aotRid is not null)
                Directory.CreateDirectory(stagedPublishDirectory);
            var adapterReferences = StageManagedAdapters(managedAdapters, generatedDirectory);
            projectFile = Path.Combine(generatedDirectory, "Generated.csproj");
            stagedAssemblyFile = Path.Combine(
                generatedDirectory,
                "bin",
                "Release",
                "net10.0",
                assemblyName + ".dll");
            File.WriteAllText(
                projectFile,
                ProjectFileContents(
                    executable,
                    aotRid,
                    assemblyName,
                    webPackage: isWebPackage,
                    sqlitePackage: usesDatabaseAdapter,
                    managedAdapters: adapterReferences));
            File.WriteAllText(
                Path.Combine(generatedDirectory, "Program.cs"),
                Emitter.Emit(
                    program,
                    executable,
                    package?.WebDatabaseOptions,
                    package?.Manifest.HttpOrigin,
                    processRunnerOptions));
        }
        catch (ManagedAdapterPreparationException error)
        {
            PrintDiagnostics(
            [
                AtStart("E_ADAPTER", error.Message, sourceFile)
            ],
            json: false);
            TryCleanupGeneratedDirectory(generatedDirectory, sourceFile);
            return 1;
        }
        catch (Exception error) when (IsFileError(error))
        {
            PrintDiagnostics(
            [
                AtStart("E_IO", $"Could not write generated project: {error.Message}", sourceFile)
            ],
            json: false);
            TryCleanupGeneratedDirectory(generatedDirectory, sourceFile);
            return 1;
        }

        try
        {
            var dotnet = ResolveDotnetHost();
            if (aotRid is not null)
            {
                var publish = await ExecAsync(
                    dotnet,
                    [
                        "publish",
                        projectFile,
                        "--nologo",
                        "-v:q",
                        "--configuration", "Release",
                        "--runtime", aotRid,
                        "--output", stagedPublishDirectory
                    ],
                    FindRoot() ?? Directory.GetCurrentDirectory(),
                    sourceFile);

                if (publish is null)
                    return 1;

                if (publish.ExitCode != 0)
                {
                    PrintDiagnostics(
                    [
                        AtStart("E_BUILD", "The generated NativeAOT project failed to publish", sourceFile)
                    ],
                    json: false);
                    WriteProcessOutputToError(publish);
                    return publish.ExitCode;
                }

                WriteProcessOutputToError(publish);

                var outputDirectory = CreateBuildOutputDirectory(sourceFile, package);
                var executableName = NativeExecutableName(aotRid, assemblyName);
                var executablePath = Path.Combine(outputDirectory, executableName);
                try
                {
                    EnsureBuildOutputPathSafe(sourceFile, outputDirectory, package);
                    if (entryCommand is not null)
                    {
                        File.WriteAllText(
                            Path.Combine(stagedPublishDirectory, "command-schema.json"),
                            Emitter.EmitCommandSchema(program),
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    }
                    EnsureBuildOutputPathSafe(sourceFile, outputDirectory, package);
                    if (selectedProcessExecutable is not null)
                        CopyProcessExecutable(selectedProcessExecutable, stagedPublishDirectory, assemblyName);
                    CopyBuildArtifacts(stagedPublishDirectory, outputDirectory);
                    if (!File.Exists(executablePath))
                    {
                        TryCleanupBuildOutputDirectory(sourceFile, outputDirectory, package);
                        PrintDiagnostics(
                        [
                            AtStart(
                                "E_BUILD",
                                $"The generated NativeAOT publish did not produce {executableName}",
                                sourceFile)
                        ],
                        json: false);
                        return 1;
                    }

                    if (!OperatingSystem.IsWindows() && aotRid == "linux-x64")
                    {
                        File.SetUnixFileMode(
                            executablePath,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }

                    if (command == "build")
                    {
                        var sdkVersion = await GetSdkVersionForReceiptAsync(dotnet, sourceFile);
                        if (sdkVersion is null)
                        {
                            TryCleanupBuildOutputDirectory(sourceFile, outputDirectory, package);
                            return 1;
                        }

                        BuildReceipt.Write(
                            outputDirectory,
                            program,
                            sourceText,
                            packageGraph,
                            "native_aot",
                            aotRid,
                            sdkVersion,
                            CreateManagedAdapterProvenance(packageGraph, program));
                    }
                }
                catch (ProcessExecutablePreparationException)
                {
                    TryCleanupBuildOutputDirectory(sourceFile, outputDirectory, package);
                    PrintProcessExecutableDiagnostic(sourceFile);
                    return 1;
                }
                catch (Exception error) when (IsFileError(error))
                {
                    TryCleanupBuildOutputDirectory(sourceFile, outputDirectory, package);
                    PrintDiagnostics(
                    [
                        AtStart("E_IO", $"Could not save NativeAOT artifacts or build receipt: {error.Message}", sourceFile)
                    ],
                    json: false);
                    return 1;
                }

                Console.WriteLine($"Built native executable: {Path.GetFullPath(executablePath)}");
                return 0;
            }

            var build = await ExecAsync(
                dotnet,
                ["build", projectFile, "--nologo", "-v:q", "--configuration", "Release"],
                FindRoot() ?? Directory.GetCurrentDirectory(),
                sourceFile);

            if (build is null)
                return 1;

            if (build.ExitCode != 0)
            {
                PrintDiagnostics(
                [
                    AtStart("E_BUILD", "The generated C# project failed to build", sourceFile)
                ],
                json: false);
                WriteProcessOutputToError(build);
                return build.ExitCode;
            }

            if (command == "build")
            {
                var outputDirectory = CreateBuildOutputDirectory(sourceFile, package);
                try
                {
                    EnsureBuildOutputPathSafe(sourceFile, outputDirectory, package);
                    if (entryCommand is not null)
                    {
                        File.WriteAllText(
                            Path.Combine(Path.GetDirectoryName(stagedAssemblyFile)!, "command-schema.json"),
                            Emitter.EmitCommandSchema(program),
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    }
                    if (isWebPackage)
                    {
                        File.WriteAllText(
                            Path.Combine(Path.GetDirectoryName(stagedAssemblyFile)!, "openapi.json"),
                            Emitter.EmitOpenApi(program),
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    }
                    EnsureBuildOutputPathSafe(sourceFile, outputDirectory, package);
                    var stagedOutputDirectory = Path.GetDirectoryName(stagedAssemblyFile)!;
                    if (selectedProcessExecutable is not null)
                        CopyProcessExecutable(selectedProcessExecutable, stagedOutputDirectory, assemblyName);
                    CopyBuildArtifacts(
                        stagedOutputDirectory,
                        outputDirectory);
                    var sdkVersion = await GetSdkVersionForReceiptAsync(dotnet, sourceFile);
                    if (sdkVersion is null)
                    {
                        TryCleanupBuildOutputDirectory(sourceFile, outputDirectory, package);
                        return 1;
                    }

                    BuildReceipt.Write(
                        outputDirectory,
                        program,
                        sourceText,
                        packageGraph,
                        "managed",
                        runtimeIdentifier: null,
                        sdkVersion,
                        CreateManagedAdapterProvenance(packageGraph, program));
                }
                catch (ProcessExecutablePreparationException)
                {
                    TryCleanupBuildOutputDirectory(sourceFile, outputDirectory, package);
                    PrintProcessExecutableDiagnostic(sourceFile);
                    return 1;
                }
                catch (Exception error) when (IsFileError(error))
                {
                    TryCleanupBuildOutputDirectory(sourceFile, outputDirectory, package);
                    PrintDiagnostics(
                    [
                        AtStart("E_IO", $"Could not save build artifacts or build receipt: {error.Message}", sourceFile)
                    ],
                    json: false);
                    return 1;
                }

                Console.WriteLine(
                    $"Built {(executable ? "executable" : "library")}: " +
                    Path.Combine(outputDirectory, Path.GetFileName(stagedAssemblyFile)));
                return 0;
            }

            var runArguments = BuildApplicationArguments(stagedAssemblyFile, applicationArguments);
            if (processRunnerPin is not null)
            {
                try
                {
                    CopyProcessExecutable(processRunnerPin, Path.GetDirectoryName(stagedAssemblyFile)!, assemblyName);
                }
                catch (ProcessExecutablePreparationException)
                {
                    PrintProcessExecutableDiagnostic(sourceFile);
                    return 1;
                }
            }
            if (isWebPackage)
            {
                return await ExecWebHostAsync(
                    dotnet,
                    runArguments,
                    package?.Root ?? Directory.GetCurrentDirectory(),
                    sourceFile,
                    GetRunSqlitePathOverride(package)) ?? 1;
            }

            var run = await ExecAsync(
                dotnet,
                runArguments,
                package?.Root ?? Directory.GetCurrentDirectory(),
                sourceFile,
                forwardOutput: true);

            return run?.ExitCode ?? 1;
        }
        finally
        {
            TryCleanupGeneratedDirectory(generatedDirectory, sourceFile);
        }
    }

    private static List<string> BuildApplicationArguments(string executable, IReadOnlyList<string>? arguments)
    {
        var result = new List<string> { executable };
        if (arguments is not null)
            result.AddRange(arguments);
        return result;
    }

    private static async Task<string?> GetSdkVersionForReceiptAsync(string dotnet, string sourceFile)
    {
        var sdk = await ExecAsync(
            dotnet,
            ["--version"],
            FindRoot() ?? Directory.GetCurrentDirectory(),
            sourceFile);
        if (sdk is null)
            return null;
        var version = sdk.StandardOutput.Trim();
        if (sdk.ExitCode == 0 && version.Length != 0)
            return version;

        PrintDiagnostics(
        [
            AtStart("E_PROCESS", "Could not determine the .NET SDK version for the build receipt", sourceFile)
        ],
        json: false);
        WriteProcessOutputToError(sdk);
        return null;
    }

    private static bool UsesDatabaseAdapter(CheckedProgram program) =>
        program.Functions.Any(function =>
            function.InferredEffects.Any(effect => effect is "db.read" or "db.write"));

    private static string? GetRunSqlitePathOverride(LoadedPackage? package)
    {
        if (package?.Manifest.Kind != "web" || package.WebDatabaseOptions is not { } databaseOptions ||
            Environment.GetEnvironmentVariable("LANG_SQLITE_PATH") is not null)
            return null;

        return Path.GetFullPath(Path.Combine(
            package.Root,
            databaseOptions.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool IsRunnableEntryPoint(CheckedFunction function) =>
        function.Name == "main" &&
        function.Parameters.Count == 0 &&
        (function.ReturnType.IsI32 || function.ReturnType.IsBool || function.ReturnType.IsText);

    private static string NativeExecutableName(string rid, string assemblyName) =>
        rid.StartsWith("win-", StringComparison.Ordinal) ? assemblyName + ".exe" : assemblyName;

    private static bool IsCurrentHostAotRid(string rid) =>
        (rid.StartsWith("win-", StringComparison.Ordinal) && OperatingSystem.IsWindows()) ||
        (rid.StartsWith("linux-", StringComparison.Ordinal) && OperatingSystem.IsLinux());

    private static string CreateBuildOutputDirectory(string sourceFile, LoadedPackage? package = null)
    {
        var outputBase = package?.Root ?? Path.GetDirectoryName(sourceFile)!;
        var outputRoot = Path.Combine(outputBase, "out");
        var stem = package?.Manifest.Name ?? Path.GetFileNameWithoutExtension(sourceFile);
        return Path.Combine(outputRoot, $"{stem}-{Guid.NewGuid():N}");
    }

    private static void EnsurePackageOutputPathSafe(LoadedPackage? package)
    {
        if (package is null)
            return;

        var outputRoot = Path.Combine(package.Root, "out");
        if (Directory.Exists(outputRoot) &&
            (File.GetAttributes(outputRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Package out directory cannot be a symbolic link or reparse point");
        }
    }

    private static void EnsureBuildOutputPathSafe(
        string sourceFile,
        string outputDirectory,
        LoadedPackage? package)
    {
        if (package is not null)
        {
            EnsurePackageOutputPathSafe(package);
            return;
        }

        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceFile)) ??
            throw new IOException("Standalone source has no containing directory");
        var outputRoot = Path.GetFullPath(Path.Combine(sourceDirectory, "out"));
        var destination = Path.GetFullPath(outputDirectory);
        if (!IsWithinDirectory(sourceDirectory, outputRoot) || !IsWithinDirectory(outputRoot, destination))
            throw new IOException("Standalone build output resolves outside the source output directory");
        if (HasReparsePointOnPath(sourceDirectory) || HasReparsePointOnPath(destination))
            throw new IOException("Standalone build output cannot resolve through a symbolic link or reparse point");
    }

    private static bool IsWithinDirectory(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var prefix = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(prefix, comparison);
    }

    private static bool HasReparsePointOnPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(fullPath) ??
            throw new IOException("Standalone output path has no filesystem root");
        var current = pathRoot;
        if (HasReparsePoint(current))
            return true;

        foreach (var segment in fullPath[pathRoot.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (HasReparsePoint(current))
                return true;
            if (!File.Exists(current) && !Directory.Exists(current))
                return false;
        }

        return false;
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string ProcessRunnerArtifactFileName(string os, string assemblyName) =>
        assemblyName + ".process-runner" +
        (string.Equals(os, "windows", StringComparison.Ordinal) ? ".exe" : "");

    private static IReadOnlyList<ManagedAdapterDescriptor> GetManagedAdapterDescriptors(
        PackageDependencyGraph? packageGraph,
        LoadedPackage? package)
    {
        if (packageGraph is not null)
        {
            return packageGraph.Nodes
                .Select(node => node.Package.ManagedAdapter)
                .OfType<ManagedAdapterDescriptor>()
                .ToArray();
        }

        return package?.ManagedAdapter is { } adapter ? [adapter] : [];
    }

    private static IReadOnlyList<ManagedAdapterProjectReference> StageManagedAdapters(
        PackageDependencyGraph? packageGraph,
        LoadedPackage? package,
        string generatedDirectory) =>
        StageManagedAdapters(GetManagedAdapterDescriptors(packageGraph, package), generatedDirectory);

    private static IReadOnlyList<ManagedAdapterProjectReference> StageManagedAdapters(
        IReadOnlyList<ManagedAdapterDescriptor> adapters,
        string generatedDirectory)
    {
        if (adapters.Count == 0)
            return [];

        var output = new List<ManagedAdapterProjectReference>();
        foreach (var group in adapters
                     .GroupBy(adapter => adapter.Definition.AssemblyName, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var distinctHashes = group
                .Select(adapter => adapter.AssemblySha256)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinctHashes.Length != 1)
            {
                throw new ManagedAdapterPreparationException(
                    "Conflicting validated managed adapter DLLs share the same assembly identity.");
            }

            var adapter = group.First();
            var expectedHash = distinctHashes[0];
            if (!IsSha256Digest(expectedHash) || !IsSimpleAssemblyName(adapter.Definition.AssemblyName))
            {
                throw new ManagedAdapterPreparationException(
                    "A validated managed adapter descriptor is incomplete.");
            }

            try
            {
                var sourcePath = Path.GetFullPath(adapter.SourceAssemblyPath);
                if (!File.Exists(sourcePath) || Directory.Exists(sourcePath) || HasReparsePointOnPath(sourcePath))
                    throw new IOException();

                var sourceAttributes = File.GetAttributes(sourcePath);
                if ((sourceAttributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0 ||
                    !string.Equals(HashFileSha256(sourcePath), expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException();
                }

                var relativePath = Path.Combine(
                    "adapters",
                    expectedHash.ToLowerInvariant(),
                    adapter.Definition.AssemblyName + ".dll");
                var stagedPath = Path.Combine(generatedDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.Copy(sourcePath, stagedPath, overwrite: false);
                if (!string.Equals(HashFileSha256(stagedPath), expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException();

                output.Add(new ManagedAdapterProjectReference(
                    adapter.Definition.AssemblyName,
                    relativePath.Replace(Path.DirectorySeparatorChar, '/')));
            }
            catch (Exception error) when (IsFileError(error) || error is CryptographicException)
            {
                throw new ManagedAdapterPreparationException(
                    "A validated managed adapter DLL could not be staged or its digest did not match.");
            }
        }

        return output;
    }

    private static bool IsSha256Digest(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool IsSimpleAssemblyName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value is not "." and not ".." &&
        value.All(character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_');

    private static string HashFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class ProcessExecutablePreparationException : IOException
    {
        public ProcessExecutablePreparationException()
            : base("The pinned process executable could not be prepared") { }
    }

    private sealed class ManagedAdapterPreparationException(string message) : Exception(message);

    private sealed record ManagedAdapterProjectReference(string AssemblyName, string HintPath);

    private static void PrintProcessExecutableDiagnostic(string sourceFile) =>
        PrintDiagnostics(
        [
            AtStart("E_PROCESS_EXECUTABLE", "The pinned process executable could not be prepared", sourceFile)
        ],
        json: false);

    private static void CopyProcessExecutable(ProcessExecutablePin pin, string stagedOutputDirectory, string assemblyName)
    {
        try
        {
            if (pin.FullPath is null)
                throw new IOException();

            var sourcePath = Path.GetFullPath(pin.FullPath);
            if (!File.Exists(sourcePath) || Directory.Exists(sourcePath) || HasReparsePointOnPath(sourcePath))
                throw new IOException();

            var sourceAttributes = File.GetAttributes(sourcePath);
            if ((sourceAttributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
                throw new IOException();

            var sourceUnixMode = UnixFileMode.None;
            if (OperatingSystem.IsLinux())
            {
                sourceUnixMode = File.GetUnixFileMode(sourcePath);
                const UnixFileMode executeBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((sourceUnixMode & executeBits) == 0)
                    throw new IOException();
            }

            if (!string.Equals(HashProcessExecutable(sourcePath), pin.Sha256, StringComparison.Ordinal))
                throw new IOException();

            var destinationPath = Path.Combine(stagedOutputDirectory, ProcessRunnerArtifactFileName(pin.Os, assemblyName));
            File.Copy(sourcePath, destinationPath, overwrite: false);
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(destinationPath, sourceUnixMode);
                if (File.GetUnixFileMode(destinationPath) != sourceUnixMode)
                    throw new IOException();
            }

            if (!string.Equals(HashProcessExecutable(destinationPath), pin.Sha256, StringComparison.Ordinal) ||
                !string.Equals(HashProcessExecutable(sourcePath), pin.Sha256, StringComparison.Ordinal))
                throw new IOException();
        }
        catch (Exception error) when (IsFileError(error) || error is CryptographicException)
        {
            throw new ProcessExecutablePreparationException();
        }
    }

    private static string HashProcessExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void CopyBuildArtifacts(string stagedOutputDirectory, string destinationDirectory)
    {
        var stagedRoot = Path.GetFullPath(stagedOutputDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var destinationPrefix = Path.EndsInDirectorySeparator(destinationRoot)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;
        var artifacts = Directory.EnumerateFiles(
                stagedRoot,
                "*",
                new EnumerationOptions
                {
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = false,
                    ReturnSpecialDirectories = false
                })
            .Select(path => new
            {
                FullPath = path,
                RelativePath = Path.GetRelativePath(stagedRoot, path)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/')
            })
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        Directory.CreateDirectory(destinationRoot);
        foreach (var artifact in artifacts)
        {
            var outputPath = Path.GetFullPath(Path.Combine(
                destinationRoot,
                artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!outputPath.StartsWith(destinationPrefix, comparison))
                throw new IOException("Generated runtime artifact resolves outside its output directory");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.Copy(
                artifact.FullPath,
                outputPath,
                overwrite: false);
        }
    }

    private static void TryCleanupBuildOutputDirectory(
        string sourceFile,
        string outputDirectory,
        LoadedPackage? package = null)
    {
        var outputBase = package?.Root ?? Path.GetDirectoryName(sourceFile)!;
        var outputRoot = Path.GetFullPath(Path.Combine(outputBase, "out"));
        var path = Path.GetFullPath(outputDirectory);
        var rootPrefix = Path.EndsInDirectorySeparator(outputRoot)
            ? outputRoot
            : outputRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!path.StartsWith(rootPrefix, comparison))
        {
            Console.Error.WriteLine(
                $"{sourceFile}:1:1: E_IO: Refusing to remove build output outside the source output directory");
            return;
        }

        if (package is null)
        {
            try
            {
                EnsureBuildOutputPathSafe(sourceFile, path, package: null);
            }
            catch (Exception error) when (IsFileError(error))
            {
                Console.Error.WriteLine(
                    $"{sourceFile}:1:1: E_IO: Refusing to remove standalone build output through an unsafe path: {error.Message}");
                return;
            }
        }

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception error) when (IsFileError(error))
        {
            Console.Error.WriteLine(
                $"{sourceFile}:1:1: E_IO: Could not remove partial build output: {error.Message}");
        }
    }

    private static string ProjectFileContents(
        bool executable,
        string? aotRid,
        string assemblyName = "Generated",
        bool webPackage = false,
        bool sqlitePackage = false,
        IReadOnlyList<ManagedAdapterProjectReference>? managedAdapters = null)
    {
        var outputType = executable ? "Exe" : "Library";
        return
            $"<Project Sdk=\"{(webPackage ? "Microsoft.NET.Sdk.Web" : "Microsoft.NET.Sdk")}\">\n" +
            "  <PropertyGroup>\n" +
            $"    <OutputType>{outputType}</OutputType>\n" +
            $"    <AssemblyName>{assemblyName}</AssemblyName>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "    <ServerGarbageCollection>false</ServerGarbageCollection>\n" +
            "    <ImplicitUsings>enable</ImplicitUsings>\n" +
            "    <Nullable>enable</Nullable>\n" +
            (aotRid is null
                ? string.Empty
                : $"    <RuntimeIdentifier>{aotRid}</RuntimeIdentifier>\n" +
                  "    <PublishAot>true</PublishAot>\n" +
                  "    <SelfContained>true</SelfContained>\n") +
            "  </PropertyGroup>\n" +
            (sqlitePackage
                ? "  <ItemGroup>\n" +
                  "    <PackageReference Include=\"Microsoft.Data.Sqlite\" Version=\"10.0.12\" />\n" +
                  "  </ItemGroup>\n"
                : string.Empty) +
            ManagedAdapterReferencesContents(managedAdapters) +
            "</Project>\n";
    }

    private static string ManagedAdapterReferencesContents(
        IReadOnlyList<ManagedAdapterProjectReference>? managedAdapters)
    {
        if (managedAdapters is not { Count: > 0 })
            return string.Empty;

        var contents = new StringBuilder("  <ItemGroup>\n");
        foreach (var adapter in managedAdapters)
        {
            contents.Append("    <Reference Include=\"")
                .Append(System.Security.SecurityElement.Escape(adapter.AssemblyName))
                .Append("\">\n")
                .Append("      <HintPath>")
                .Append(System.Security.SecurityElement.Escape(adapter.HintPath))
                .Append("</HintPath>\n")
                .Append("      <Private>true</Private>\n")
                .Append("    </Reference>\n");
        }

        contents.Append("  </ItemGroup>\n");
        return contents.ToString();
    }

    private static void TryCleanupGeneratedDirectory(string generatedDirectory, string sourceFile)
    {
        var generatedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "lang-generated"));
        var outputDirectory = Path.GetFullPath(generatedDirectory);
        var rootPrefix = Path.EndsInDirectorySeparator(generatedRoot)
            ? generatedRoot
            : generatedRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!outputDirectory.StartsWith(rootPrefix, comparison))
        {
            Console.Error.WriteLine(
                $"{sourceFile}:1:1: E_IO: Refusing to remove generated output outside the temporary output root");
            return;
        }

        try
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
        catch (Exception error) when (IsFileError(error))
        {
            Console.Error.WriteLine(
                $"{sourceFile}:1:1: E_IO: Could not remove generated run output: {error.Message}");
        }
    }

    private static string ResolveDotnetHost()
    {
        foreach (var variable in new[] { "LANG_DOTNET", "DOTNET_HOST_PATH" })
        {
            var configuredHost = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(configuredHost))
                return configuredHost;
        }

        var hostName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var starts = new[] { FindRoot(), AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in starts.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (var directory = new DirectoryInfo(start!); directory is not null; directory = directory.Parent)
            {
                var localHost = Path.Combine(directory.FullName, ".dotnet", hostName);
                if (File.Exists(localHost))
                    return localHost;
            }
        }

        return "dotnet";
    }

    private static async Task<ProcessResult?> ExecAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string sourceFile,
        bool forwardOutput = false)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = executable;
            process.StartInfo.WorkingDirectory = workingDirectory;
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            process.StartInfo.StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, stderrTask);

            var result = new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
            if (forwardOutput)
            {
                Console.Out.Write(result.StandardOutput);
                Console.Error.Write(result.StandardError);
            }
            return result;
        }
        catch (Exception error) when (IsProcessError(error))
        {
            PrintDiagnostics(
            [
                AtStart(
                    "E_PROCESS",
                    $"Could not start or wait for '{Path.GetFileName(executable)}': {error.Message}",
                    sourceFile)
            ],
            json: false);
            return null;
        }
    }

    private static async Task<int?> ExecWebHostAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string sourceFile,
        string? sqlitePathOverride = null)
    {
        Process? process = null;
        ConsoleCancelEventHandler? cancelHandler = null;
        PosixSignalRegistration? sigtermHandler = null;
        var shutdownRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false;

        try
        {
            process = new Process();
            process.StartInfo.FileName = executable;
            process.StartInfo.WorkingDirectory = workingDirectory;
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            process.StartInfo.StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            process.StartInfo.Environment["LANG_PARENT_PROCESS_ID"] =
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (sqlitePathOverride is not null &&
                !process.StartInfo.Environment.ContainsKey("LANG_SQLITE_PATH"))
                process.StartInfo.Environment["LANG_SQLITE_PATH"] = sqlitePathOverride;

            cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdownRequested.TrySetResult(true);
            };
            Console.CancelKeyPress += cancelHandler;

            if (!OperatingSystem.IsWindows())
            {
                sigtermHandler = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
                {
                    context.Cancel = true;
                    shutdownRequested.TrySetResult(true);
                });
            }

            if (!process.Start())
                throw new InvalidOperationException("The web host process did not start");
            started = true;

            var stdoutTask = ForwardLiveOutputAsync(process.StandardOutput, Console.Out);
            var stderrTask = ForwardLiveOutputAsync(process.StandardError, Console.Error);
            var exitTask = process.WaitForExitAsync();
            var completedTask = await Task.WhenAny(exitTask, shutdownRequested.Task);
            if (completedTask == shutdownRequested.Task)
            {
                TryCloseWebHostStandardInput(process);
                if (!process.HasExited)
                {
                    if (!OperatingSystem.IsWindows())
                        _ = SendUnixSignal(process.Id, 15); // SIGTERM

                    try
                    {
                        await exitTask.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (TimeoutException)
                    {
                        TryKillProcessTree(process);
                        await WaitAfterForcedTerminationAsync(process);
                    }
                }
                else
                    await exitTask;
            }
            else
            {
                await exitTask;
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            return process.ExitCode;
        }
        catch (Exception error) when (IsProcessError(error) || error is InvalidOperationException)
        {
            PrintDiagnostics(
            [
                AtStart(
                    "E_PROCESS",
                    $"Could not start or wait for '{Path.GetFileName(executable)}': {error.Message}",
                    sourceFile)
            ],
            json: false);
            return null;
        }
        finally
        {
            if (cancelHandler is not null)
                Console.CancelKeyPress -= cancelHandler;
            sigtermHandler?.Dispose();

            if (started && process is not null)
            {
                TryCloseWebHostStandardInput(process);
                if (!process.HasExited)
                {
                    if (!OperatingSystem.IsWindows())
                        _ = SendUnixSignal(process.Id, 15); // SIGTERM
                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (TimeoutException)
                    {
                        TryKillProcessTree(process);
                        await WaitAfterForcedTerminationAsync(process);
                    }
                }
            }

            process?.Dispose();
        }
    }

    private static async Task ForwardLiveOutputAsync(StreamReader reader, TextWriter writer)
    {
        var buffer = new char[1024];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0)
                return;

            await writer.WriteAsync(buffer.AsMemory(0, read));
            await writer.FlushAsync();
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            // The process may have exited while shutdown was being escalated.
        }
    }

    private static void TryCloseWebHostStandardInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            // Shutdown may race with the child exiting or closing its input stream.
        }
    }

    private static async Task WaitAfterForcedTerminationAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // The forced process-tree termination is best effort if the OS cannot reap the child.
        }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendUnixSignal(int processId, int signal);

    private static bool IsProcessError(Exception error) =>
        error is Win32Exception or IOException or UnauthorizedAccessException;

    private static void WriteProcessOutputToError(ProcessResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            Console.Error.WriteLine(result.StandardOutput.TrimEnd());
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            Console.Error.WriteLine(result.StandardError.TrimEnd());
    }

    private static int TestFixtures()
    {
        var root = FindRoot();
        if (root is null)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "manifest.json");
            PrintDiagnostics([AtStart("E_IO", "Cannot find fixtures/manifest.json", path)], json: false);
            return 2;
        }

        var manifest = Path.Combine(root, "fixtures", "manifest.json");
        FixtureCase[] cases;
        try
        {
            cases = JsonSerializer.Deserialize<FixtureCase[]>(File.ReadAllText(manifest), JsonOptions) ?? [];
        }
        catch (Exception error) when (IsFileError(error) || error is JsonException)
        {
            PrintDiagnostics(
            [
                AtStart("E_IO", $"Could not read fixture manifest: {error.Message}", manifest)
            ],
            json: false);
            return 2;
        }

        var failures = 0;
        foreach (var item in cases.Where(testCase => testCase.Status == "active"))
        {
            var file = Path.GetFullPath(Path.Combine(root, "fixtures", item.File));
            string[] actual;
            try
            {
                actual = Compiler.Check(file, File.ReadAllText(file)).Diagnostics
                    .Select(diagnostic => diagnostic.Code)
                    .ToArray();
            }
            catch (Exception error) when (IsFileError(error))
            {
                actual = ["E_IO"];
                Console.Error.WriteLine(
                    $"{file}:1:1: E_IO: Could not read fixture: {error.Message}");
            }

            var expected = item.ExpectedCodes ??
                (item.ExpectedCode is null ? [] : [item.ExpectedCode]);
            var okay = actual.SequenceEqual(expected, StringComparer.Ordinal);
            Console.WriteLine(
                $"{(okay ? "PASS" : "FAIL")} {item.File} " +
                $"expected={(expected.Length == 0 ? "valid" : string.Join(',', expected))} " +
                $"actual={(actual.Length == 0 ? "valid" : string.Join(',', actual))}");
            if (!okay)
                failures++;
        }

        Console.WriteLine(
            $"{cases.Count(testCase => testCase.Status == "active")} active, " +
            $"{cases.Count(testCase => testCase.Status == "pending")} pending, {failures} failed");
        return failures == 0 ? 0 : 1;
    }

    private static string? FindRoot()
    {
        var starts = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in starts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "lang.slnx")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "Lang", "Lang.csproj")))
                {
                    return directory.FullName;
                }
            }
        }

        return null;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

internal sealed record FixtureCase(
    string File,
    string Status,
    string? ExpectedCode,
    string[]? ExpectedCodes = null);
