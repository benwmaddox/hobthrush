using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class BuildReceipt
{
    private const int SchemaVersion = 3;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Write(
        string outputDirectory,
        CheckedProgram program,
        string sourceText,
        PackageDependencyGraph? graph,
        string buildMode,
        string? runtimeIdentifier,
        string sdkVersion,
        IReadOnlyList<AuditManagedAdapterProvenance>? managedAdapters = null)
    {
        var snapshot = graph is null ? null : AuditReport.Create(graph, program, managedAdapters);
        var checkedClaims = snapshot?.Json ?? AuditReport.CreateStandaloneSnapshot(program, sourceText);
        var packageInputs = snapshot?.Packages
            .SelectMany(package => package.Inputs)
            .OrderBy(input => input.Package.Path, StringComparer.Ordinal)
            .ThenBy(input => input.Path, StringComparer.Ordinal)
            .Select(input => new
            {
                package = IdentityJson(input.Package),
                kind = input.Kind,
                path = input.Path,
                sha256 = input.Sha256
            })
            .ToArray();
        var inputs = graph is null
            ? new object[]
            {
                new
                {
                    package = (object?)null,
                    kind = "source",
                    path = "source",
                    sha256 = AuditReport.HashNormalizedText(sourceText)
                }
            }
            : packageInputs!.Cast<object>().ToArray();
        var packageGraph = snapshot is null
            ? Array.Empty<object>()
            : snapshot.Packages.Select(package => (object)new
            {
                identity = IdentityJson(package.Identity),
                role = package.Role,
                content_sha256 = package.ContentSha256,
                dependencies = package.Dependencies.Select(dependency => new
                {
                    alias = dependency.Alias,
                    package = IdentityJson(dependency.Package)
                }).ToArray()
            }).ToArray();
        var grants = snapshot?.ManifestGrants ?? Array.Empty<string>();
        var trustedClaims = snapshot?.TrustedClaims ?? AuditReport.CreateStandaloneTrustedClaims(program);
        var trustedComponents = trustedClaims.Select(ClaimJson).ToArray();
        var adapterProvenance = snapshot?.ManagedAdapters ??
            AuditReport.CanonicalizeManagedAdapters(managedAdapters ?? []);
        var foreignDependencies = snapshot?.ForeignDependencies ??
            (AuditReport.RequiresSqliteDependency(null, program)
                ? new[] { new AuditForeignDependency("Microsoft.Data.Sqlite", "10.0.12", "nuget", "generated_build") }
                : []);
        var assembly = typeof(CheckedProgram).Assembly;
        var compilerPath = assembly.Location;
        if (string.IsNullOrEmpty(compilerPath) || !File.Exists(compilerPath))
            throw new IOException("Could not locate the compiler assembly for the build receipt");
        var compilerVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "unknown";
        var compilerSha256 = HashFile(compilerPath);
        var artifacts = EnumerateArtifacts(outputDirectory);

        var receipt = new
        {
            schema_version = SchemaVersion,
            build = new
            {
                mode = buildMode,
                framework = "net10.0",
                runtime_identifier = runtimeIdentifier
            },
            package_graph = packageGraph,
            toolchain = new
            {
                compiler_version = compilerVersion,
                compiler_sha256 = compilerSha256,
                dotnet_sdk_version = sdkVersion,
                foreign_dependencies = foreignDependencies.Select(ForeignDependencyJson).ToArray()
            },
            inputs,
            manifest_grants = grants.OrderBy(grant => grant, StringComparer.Ordinal).ToArray(),
            trusted_components = trustedComponents,
            foreign_dependencies = foreignDependencies.Select(ForeignDependencyJson).ToArray(),
            managed_adapters = AuditReport.ManagedAdapterMetadataJson(adapterProvenance),
            audit_snapshot_sha256 = AuditReport.Hash(checkedClaims),
            artifacts
        };
        var json = JsonSerializer.Serialize(receipt, JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var bytes = StrictUtf8.GetBytes(json.TrimEnd('\n') + "\n");
        var receiptPath = Path.Combine(outputDirectory, "build-receipt.json");
        var temporaryPath = Path.Combine(outputDirectory, ".build-receipt.json.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, receiptPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static object[] EnumerateArtifacts(string outputDirectory) =>
        Directory.EnumerateFiles(
                outputDirectory,
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
                RelativePath = NormalizeRelative(Path.GetRelativePath(outputDirectory, path))
            })
            .Where(artifact => !string.Equals(artifact.RelativePath, "build-receipt.json", StringComparison.Ordinal))
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact => (object)new
            {
                path = artifact.RelativePath,
                sha256 = HashFile(artifact.FullPath)
            })
            .ToArray();

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static object IdentityJson(AuditPackageIdentity identity) =>
        AuditReport.IdentityJson(identity);

    private static object ClaimJson(AuditTrustedClaim claim) => new
    {
        operation = claim.Operation,
        source = claim.Source,
        effects = claim.Effects,
        assurance = claim.Assurance,
        reachable_from = claim.ReachableFrom.Select(function => function.Package is null
            ? (object)new { module = function.Module, name = function.Name }
            : new
            {
                package = IdentityJson(function.Package),
                module = function.Module,
                name = function.Name
            }).ToArray()
    };

    private static object ForeignDependencyJson(AuditForeignDependency dependency) => new
    {
        name = dependency.Name,
        version = dependency.Version,
        ecosystem = dependency.Ecosystem,
        reason = dependency.Reason
    };

    private static string NormalizeRelative(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
}
