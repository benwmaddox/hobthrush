using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
return await Driver.RunAsync(args);

internal static class Driver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] == "test")
            return TestFixtures();

        if (args.Length < 2 || args[0] is not ("check" or "build" or "run"))
        {
            PrintUsage();
            return 2;
        }

        var json = args[0] == "check" && args.Skip(2).Contains("--json", StringComparer.Ordinal);
        if ((args[0] == "check" && args.Skip(2).Any(arg => arg != "--json")) ||
            (args[0] != "check" && args.Length != 2))
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

        return await BuildCheckedAsync(result.Program!, file, args[0]);
    }

    private static void PrintUsage() =>
        Console.Error.WriteLine("Usage: al check FILE [--json] | al build FILE | al run FILE | al test");

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
        string command)
    {
        var entry = program.Functions.FirstOrDefault(IsRunnableEntryPoint);
        if (command == "run" && entry is null)
        {
            PrintDiagnostics(
            [
                AtStart(
                    "E_ENTRYPOINT",
                    "al run requires fn main() -> i32, bool, or Text with no parameters",
                    sourceFile)
            ],
            json: false);
            return 1;
        }

        var executable = entry is not null;
        var generatedDirectory = Path.Combine(
            Path.GetTempPath(),
            "al-generated",
            Guid.NewGuid().ToString("N"));

        string projectFile;
        string stagedAssemblyFile;
        try
        {
            Directory.CreateDirectory(generatedDirectory);
            projectFile = Path.Combine(generatedDirectory, "Generated.csproj");
            stagedAssemblyFile = Path.Combine(
                generatedDirectory,
                "bin",
                "Release",
                "net10.0",
                "Generated.dll");
            File.WriteAllText(projectFile, ProjectFileContents(executable));
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
                var outputDirectory = CreateBuildOutputDirectory(sourceFile);
                try
                {
                    CopyBuildArtifacts(
                        Path.GetDirectoryName(stagedAssemblyFile)!,
                        outputDirectory);
                }
                catch (Exception error) when (IsFileError(error))
                {
                    TryCleanupBuildOutputDirectory(sourceFile, outputDirectory);
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
                Directory.GetCurrentDirectory(),
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

    private static string CreateBuildOutputDirectory(string sourceFile)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceFile)!;
        var outputRoot = Path.Combine(sourceDirectory, "out");
        var stem = Path.GetFileNameWithoutExtension(sourceFile);
        return Path.Combine(outputRoot, $"{stem}-{Guid.NewGuid():N}");
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

    private static void TryCleanupBuildOutputDirectory(string sourceFile, string outputDirectory)
    {
        var outputRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "out"));
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

    private static string ProjectFileContents(bool executable)
    {
        var outputType = executable ? "Exe" : "Library";
        return
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            $"    <OutputType>{outputType}</OutputType>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "    <ImplicitUsings>enable</ImplicitUsings>\n" +
            "    <Nullable>enable</Nullable>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n";
    }

    private static void TryCleanupGeneratedDirectory(string generatedDirectory, string sourceFile)
    {
        var generatedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "al-generated"));
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
        foreach (var variable in new[] { "AL_DOTNET", "DOTNET_HOST_PATH" })
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
                if (File.Exists(Path.Combine(directory.FullName, "al.slnx")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "Al", "Al.csproj")))
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
