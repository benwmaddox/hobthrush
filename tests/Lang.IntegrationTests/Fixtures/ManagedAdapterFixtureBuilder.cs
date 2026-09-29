using System.Diagnostics;
using System.Text;

internal sealed class ManagedAdapterFixtureBuilder(string temporaryRoot, string dotnet)
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(2);

    public static string ValidAdapterSource => """
        using System;
        using System.Security.Cryptography;
        using System.Text;

        namespace Lang.ManagedAdapters;

        public static class Sha256Text
        {
            public static string HashUtf8(string text)
            {
                var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
                return Convert.ToHexString(bytes).ToLowerInvariant();
            }
        }
        """;

    public async Task<string> BuildAsync(
        string caseName,
        string source,
        string assemblyName = "Lang.ManagedAdapters",
        string? additionalReferenceAssembly = null,
        bool suppressGeneratedTargetFrameworkAttribute = false)
    {
        var sourceDirectory = Path.Combine(temporaryRoot, "managed-adapter-fixtures", caseName);
        var outputDirectory = Path.Combine(sourceDirectory, "out");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "Adapter.csproj"),
            CreateProject(assemblyName, additionalReferenceAssembly, suppressGeneratedTargetFrameworkAttribute),
            new UTF8Encoding(false));
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "Adapter.cs"),
            source,
            new UTF8Encoding(false));

        var startInfo = new ProcessStartInfo
        {
            FileName = dotnet,
            WorkingDirectory = sourceDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "build", "Adapter.csproj", "-c", "Release", "--nologo", "-v:q",
                     "-p:RestoreIgnoreFailedSources=true", "-o", outputDirectory
                 })
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start the managed adapter fixture build for {caseName}.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(BuildTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await process.WaitForExitAsync();
            throw new TimeoutException($"Managed adapter fixture build {caseName} exceeded {BuildTimeout}.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Managed adapter fixture build {caseName} failed ({process.ExitCode}). stdout=<{stdout}> stderr=<{stderr}>");

        var assemblyPath = Path.Combine(outputDirectory, assemblyName + ".dll");
        if (!File.Exists(assemblyPath))
            throw new InvalidOperationException($"Managed adapter fixture build did not produce {assemblyPath}.");
        return assemblyPath;
    }

    public Task<string> BuildCustomReferenceAssemblyAsync() =>
        BuildAsync(
            "custom-reference",
            """
            namespace Custom;

            public static class ManagedAdapterMarker
            {
                public static void Touch() { }
            }
            """,
            "CustomAdapterDependency");

    private static string CreateProject(
        string assemblyName,
        string? additionalReferenceAssembly,
        bool suppressGeneratedTargetFrameworkAttribute)
    {
        var reference = additionalReferenceAssembly is null
            ? string.Empty
            : """
                <ItemGroup>
                  <Reference Include="CustomAdapterDependency">
                    <HintPath>__REFERENCE_PATH__</HintPath>
                    <Private>false</Private>
                  </Reference>
                </ItemGroup>
                """.Replace(
                    "__REFERENCE_PATH__",
                    EscapeXml(Path.GetFullPath(additionalReferenceAssembly)),
                    StringComparison.Ordinal);
        var targetFrameworkAttribute = suppressGeneratedTargetFrameworkAttribute
            ? "false"
            : "true";

        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
                <AssemblyName>{EscapeXml(assemblyName)}</AssemblyName>
                <Version>1.0.0</Version>
                <AssemblyVersion>1.0.0.0</AssemblyVersion>
                <FileVersion>1.0.0.0</FileVersion>
                <ImplicitUsings>disable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <Deterministic>true</Deterministic>
                <GenerateTargetFrameworkAttribute>{targetFrameworkAttribute}</GenerateTargetFrameworkAttribute>
              </PropertyGroup>
              {reference}
            </Project>
            """;
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}