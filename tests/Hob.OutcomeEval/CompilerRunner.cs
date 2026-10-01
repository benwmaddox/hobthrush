using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace Hob.OutcomeEval;

internal sealed class CompilerRunner(string dotnetPath, string compilerPath, string resultsRoot)
{
    private const int CommandTimeoutSeconds = 120;
    private const int MaxCapturedOutputBytes = 8 * 1024 * 1024;
    private int sequence;
    public string DotnetPath => dotnetPath;
    public string CompilerPath => compilerPath;

    public async Task<(CapturedCommand Capture, CommandExecution Record)> RunAsync(
        string scenarioId,
        string commandId,
        IReadOnlyList<string> compilerArguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null)
    {
        var stdoutCapture = new BoundedOutput(Array.Empty<byte>(), false);
        var stderrCapture = new BoundedOutput(Array.Empty<byte>(), false);
        var timedOut = false;
        int? exitCode = null;

        var startInfo = CreateStartInfo(workingDirectory, compilerArguments, environment, dotnetPath, compilerPath);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Could not start compiler command '{commandId}'.");
            var stdoutTask = ReadBoundedAsync(process.StandardOutput.BaseStream);
            var stderrTask = ReadBoundedAsync(process.StandardError.BaseStream);
            using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(CommandTimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                KillProcessTree(process);
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException) { KillProcessTree(process); }
            }

            try
            {
                var captures = await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(10));
                stdoutCapture = captures[0];
                stderrCapture = captures[1];
            }
            catch (TimeoutException)
            {
                timedOut = true;
                KillProcessTree(process);
                stdoutCapture = new BoundedOutput(Encoding.UTF8.GetBytes("[output capture incomplete after process cleanup]\n"), true);
                stderrCapture = new BoundedOutput(Encoding.UTF8.GetBytes("[output capture incomplete after process cleanup]\n"), true);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            stderrCapture = new BoundedOutput(Encoding.UTF8.GetBytes($"{exception.GetType().Name}: compiler command could not start."), false);
        }

        var stdoutBytes = stdoutCapture.Bytes;
        var stderrBytes = stderrCapture.Bytes;
        var capture = new CapturedCommand(
            exitCode,
            timedOut,
            stdoutBytes,
            stderrBytes,
            stdoutCapture.Truncated,
            stderrCapture.Truncated);
        var record = await RecordCaptureAsync(scenarioId, commandId, compilerArguments, capture);
        return (capture, record);
    }

    public async Task<CommandExecution> RecordCaptureAsync(
        string scenarioId,
        string commandId,
        IReadOnlyList<string> compilerArguments,
        CapturedCommand capture)
    {
        var commandNumber = Interlocked.Increment(ref sequence);
        var evidenceDirectory = Path.Combine(resultsRoot, "commands", SafeSegment(scenarioId));
        Directory.CreateDirectory(evidenceDirectory);
        var stem = $"{commandNumber:D4}-{SafeSegment(commandId)}";
        var stdoutFullPath = Path.Combine(evidenceDirectory, stem + ".stdout.txt");
        var stderrFullPath = Path.Combine(evidenceDirectory, stem + ".stderr.txt");
        await File.WriteAllBytesAsync(stdoutFullPath, capture.StandardOutput);
        await File.WriteAllBytesAsync(stderrFullPath, capture.StandardError);

        return new CommandExecution(
            commandId,
            SafeCommandDescription(compilerArguments),
            capture.ExitCode,
            capture.TimedOut,
            capture.StandardOutputTruncated,
            capture.StandardErrorTruncated,
            new EvidenceFile(RelativeResultPath(stdoutFullPath), Sha256(capture.StandardOutput)),
            new EvidenceFile(RelativeResultPath(stderrFullPath), Sha256(capture.StandardError)));
    }

    public async Task<string> GetSdkVersionAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--version");
        using var process = Process.Start(startInfo)
            ?? throw new EvaluationConfigurationException("Could not start the repository-pinned dotnet executable.");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellation.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellation.Token);
        await process.WaitForExitAsync(cancellation.Token);
        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new EvaluationConfigurationException($"Pinned dotnet --version failed: {stderr}");
        return stdout;
    }

    public static ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        IReadOnlyList<string> compilerArguments,
        IReadOnlyDictionary<string, string>? environment = null,
        string? dotnetPath = null,
        string? compilerPath = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetPath ?? string.Empty,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (string.IsNullOrWhiteSpace(startInfo.FileName) || string.IsNullOrWhiteSpace(compilerPath))
            throw new ArgumentException("Pinned dotnet and compiler paths are required to create a compiler process.");
        startInfo.ArgumentList.Add(compilerPath);
        foreach (var argument in compilerArguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var key in startInfo.Environment.Keys
                     .Where(key => key.StartsWith("HOB_", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
            startInfo.Environment.Remove(key);
        startInfo.Environment["HOB_DOTNET"] = startInfo.FileName;
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
                startInfo.Environment[name] = value;
        }
        return startInfo;
    }

    internal static async Task<BoundedOutput> ReadBoundedAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[64 * 1024];
        using var retained = new MemoryStream();
        var truncated = false;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            var remaining = MaxCapturedOutputBytes - (int)retained.Length;
            if (remaining > 0)
                retained.Write(buffer, 0, Math.Min(read, remaining));
            if (read > remaining)
                truncated = true;
        }

        if (truncated)
        {
            var marker = Encoding.UTF8.GetBytes("\n[output truncated after 8388608 bytes]\n");
            retained.Write(marker);
        }
        return new BoundedOutput(retained.ToArray(), truncated);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private string RelativeResultPath(string fullPath) => Path.GetRelativePath(resultsRoot, fullPath)
        .Replace(Path.DirectorySeparatorChar, '/');

    private static string SafeCommandDescription(IReadOnlyList<string> compilerArguments)
    {
        var args = compilerArguments.Select(argument => Path.IsPathRooted(argument) ? "<candidate>" : argument);
        return "dotnet hob.dll " + string.Join(' ', args);
    }

    internal static string SafeSegment(string value)
    {
        var chars = value.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
            ? character
            : '-').ToArray();
        var segment = new string(chars).Trim('-', '.');
        return segment.Length == 0 ? "item" : segment;
    }

    internal static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

internal sealed record BoundedOutput(byte[] Bytes, bool Truncated);
