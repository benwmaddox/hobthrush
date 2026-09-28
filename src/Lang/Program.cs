using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

        if (args.Length != 0 && args[0] == "lock")
            return RunLock(args);

        if (args.Length != 0 && args[0] == "inspect")
            return InspectEffects(args);

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

        var resolved = PackageLoader.ResolveGraph(packageDirectory);
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

        if (graph.Nodes.Any(node => node.Package.Manifest.Dependencies.Count != 0))
            Console.WriteLine($"Wrote package lock: {Path.Combine(packageDirectory, "lang.lock")}");
        else
            Console.WriteLine($"No dependencies to lock for package '{graph.Root.Package.Manifest.Name}'.");

        return 0;
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
                graph.Root.Id);
        }

        return await BuildCheckedAsync(
            checkedPackage.Program!,
            package.ManifestFile,
            command,
            aotRid,
            package,
            hasApplicationSeparator,
            applicationArguments);
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
        if (graph.Root.Package.Manifest.Dependencies.Count != 0)
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
            rootIsWebPackage: package.Manifest.Kind == "web");
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
        var requiredCapabilities = inferredEffects
            .Where(effect => effect is "fs.read" or "db.read" or "db.write")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(effect => effect, StringComparer.Ordinal)
            .ToArray();
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

        if (inferredEffects.Contains("fs.read", StringComparer.Ordinal))
        {
            trustedOperations.Add(new TrustedOperation("FsRead.read_text", "trusted_adapter", ["fs.read"]));
        }

        if (inferredEffects.Contains("db.read", StringComparer.Ordinal))
        {
            trustedOperations.Add(new TrustedOperation("DbRead.query_one", "trusted_adapter", ["db.read"]));
        }

        if (inferredEffects.Contains("db.write", StringComparer.Ordinal))
        {
            trustedOperations.Add(new TrustedOperation("DbWrite.execute", "trusted_adapter", ["db.write"]));
        }

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

    private sealed record TrustedOperation(string Operation, string Trust, IReadOnlyList<string> Effects);

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
                        PackageDisplayLabel(package.Package)));
            }
        }

        return (modules, diagnostics);
    }

    private static void PrintUsage() =>
        Console.Error.WriteLine("Usage: lang check FILE_OR_PACKAGE [--json] | lang build FILE_OR_PACKAGE [--aot --rid RID] | lang run FILE_OR_PACKAGE [-- APP_ARGS] | lang lock PACKAGE_DIRECTORY | lang inspect effects PACKAGE_DIRECTORY SYMBOL --json | lang test [FILE_OR_PACKAGE]");

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
        string? rootPackageId = null)
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
            File.WriteAllText(
                projectFile,
                ProjectFileContents(executable: true, aotRid: null, assemblyName: assemblyName));
            File.WriteAllText(
                Path.Combine(generatedDirectory, "Program.cs"),
                Emitter.EmitTests(program, rootPackageId));
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
                    sqlitePackage: usesDatabaseAdapter));
            File.WriteAllText(
                Path.Combine(generatedDirectory, "Program.cs"),
                Emitter.Emit(program, executable, package?.WebDatabaseOptions));
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
                    EnsurePackageOutputPathSafe(package);
                    if (entryCommand is not null)
                    {
                        File.WriteAllText(
                            Path.Combine(stagedPublishDirectory, "command-schema.json"),
                            Emitter.EmitCommandSchema(program),
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    }
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
                }
                catch (Exception error) when (IsFileError(error))
                {
                    TryCleanupBuildOutputDirectory(sourceFile, outputDirectory, package);
                    PrintDiagnostics(
                    [
                        AtStart("E_IO", $"Could not save NativeAOT artifacts: {error.Message}", sourceFile)
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
                    EnsurePackageOutputPathSafe(package);
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
                    CopyBuildArtifacts(
                        Path.GetDirectoryName(stagedAssemblyFile)!,
                        outputDirectory);
                }
                catch (Exception error) when (IsFileError(error))
                {
                    TryCleanupBuildOutputDirectory(sourceFile, outputDirectory, package);
                    PrintDiagnostics(
                    [
                        AtStart("E_IO", $"Could not save build artifacts: {error.Message}", sourceFile)
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
        bool sqlitePackage = false)
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
            "</Project>\n";
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
