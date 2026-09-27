using System.ComponentModel;
using System.Diagnostics;
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

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] == "test")
            return TestFixtures();

        if (args.Length < 2)
        {
            PrintUsage();
            return 2;
        }

        var hasAotOption = args.Contains("--aot", StringComparer.Ordinal);
        var hasRidOption = args.Contains("--rid", StringComparer.Ordinal);
        if (args[0] is not ("check" or "build" or "run"))
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
            return await RunPackageAsync(args[0], file, json, isAotBuild ? rid : null);

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

        return await BuildCheckedAsync(result.Program!, file, args[0], isAotBuild ? rid : null);
    }

    private static async Task<int> RunPackageAsync(string command, string packageDirectory, bool json, string? aotRid)
    {
        PackageLoadResult loaded;
        try
        {
            loaded = PackageLoader.Load(packageDirectory);
        }
        catch (Exception error) when (IsFileError(error))
        {
            PrintDiagnostics(
            [
                AtStart("E_IO", $"Could not load package directory: {error.Message}", packageDirectory)
            ],
            json);
            return 1;
        }

        if (loaded.Diagnostics.Count != 0)
        {
            PrintDiagnostics(loaded.Diagnostics, json);
            return 1;
        }

        var package = loaded.Package!;
        if (aotRid is not null && package.Manifest.IsLibrary)
        {
            return ReportBuildTargetError(
                "NativeAOT publishing is only supported for cli packages",
                package.ManifestFile);
        }

        var parsed = ParsePackageSources(package);
        if (parsed.Diagnostics.Count != 0)
        {
            PrintDiagnostics(parsed.Diagnostics, json);
            return 1;
        }

        var entryModule = package.Manifest.EntryModule;
        var entryModuleExists = entryModule is null ||
            parsed.Modules.Any(module => string.Equals(module.Module, entryModule, StringComparison.Ordinal));
        var checkedPackage = Compiler.CheckPackage(
            parsed.Modules,
            entryModuleExists ? entryModule : null);
        if (checkedPackage.Diagnostics.Count != 0 || !entryModuleExists)
        {
            var diagnostics = checkedPackage.Diagnostics.ToList();
            if (!entryModuleExists)
            {
                diagnostics.Add(AtStart(
                    "E_ENTRYPOINT",
                    $"Entry module '{entryModule}' is not present under source_root",
                    package.ManifestFile));
            }

            PrintDiagnostics(diagnostics, json);
            return 1;
        }

        if (command == "check")
        {
            if (json)
                PrintDiagnostics([], json: true);
            return 0;
        }

        return await BuildCheckedAsync(
            checkedPackage.Program!,
            package.ManifestFile,
            command,
            aotRid,
            package);
    }

    private static (List<ParsedProgram> Modules, List<Diagnostic> Diagnostics) ParsePackageSources(
        LoadedPackage package)
    {
        var modules = new List<ParsedProgram>(package.Sources.Count);
        var diagnostics = new List<Diagnostic>();
        foreach (var source in package.Sources)
        {
            string text;
            try
            {
                text = File.ReadAllText(source.File, new UTF8Encoding(false, true));
            }
            catch (DecoderFallbackException error)
            {
                diagnostics.Add(AtStart("E_IO", $"Source file is not valid UTF-8: {error.Message}", source.File));
                continue;
            }
            catch (Exception error) when (IsFileError(error))
            {
                diagnostics.Add(AtStart("E_IO", $"Could not read source file: {error.Message}", source.File));
                continue;
            }

            var sourceDiagnostics = new List<Diagnostic>();
            var tokens = Lexer.Scan(text, source.File, sourceDiagnostics);
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
                modules.Add(parsed);
        }

        return (modules, diagnostics);
    }

    private static void PrintUsage() =>
        Console.Error.WriteLine("Usage: lang check FILE_OR_PACKAGE [--json] | lang build FILE_OR_PACKAGE [--aot --rid RID] | lang run FILE_OR_PACKAGE | lang test");

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

    private static async Task<int> BuildCheckedAsync(
        CheckedProgram program,
        string sourceFile,
        string command,
        string? aotRid,
        LoadedPackage? package = null)
    {
        var entry = package is null
            ? program.Functions.FirstOrDefault(IsRunnableEntryPoint)
            : program.EntryFunctionId is { } entryFunctionId
                ? program.Functions.FirstOrDefault(function => function.Id == entryFunctionId)
                : null;
        if (aotRid is not null && entry is null)
        {
            return ReportBuildTargetError(
                "lang build --aot requires fn main() -> i32, bool, or Text with no parameters",
                sourceFile);
        }

        if (command == "run" && entry is null)
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

        var executable = entry is not null;
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
            File.WriteAllText(projectFile, ProjectFileContents(executable, aotRid, assemblyName));
            File.WriteAllText(
                Path.Combine(generatedDirectory, "Program.cs"),
                Emitter.Emit(program, executable));
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
        Directory.CreateDirectory(destinationDirectory);
        foreach (var artifact in Directory.GetFiles(stagedOutputDirectory))
        {
            File.Copy(
                artifact,
                Path.Combine(destinationDirectory, Path.GetFileName(artifact)),
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

    private static string ProjectFileContents(bool executable, string? aotRid, string assemblyName = "Generated")
    {
        var outputType = executable ? "Exe" : "Library";
        return
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
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
