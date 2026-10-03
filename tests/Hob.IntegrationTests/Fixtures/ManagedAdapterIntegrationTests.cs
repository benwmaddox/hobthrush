using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static partial class IntegrationTests
{
    private static async Task TestManagedAdapterPackages(Harness harness)
    {
        const string expectedHash =
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        const string adapterSource = """
            module sha;
            pub adapter fn hash_utf8(value: Text) -> Text effects {} = "sha256.text.hash_utf8";
            pub trait Hash { fn hash(value: Self) -> Text effects {}; }
            pub impl self::sha::Hash for Text { hash = self::sha::hash_utf8; }
            """;
        const string consumerSource = """
            module app::main;
            pub fn main() -> Text effects {} {
                let direct_hash: Text = digest::sha::hash_utf8("abc");
                let trait_hash: Text = digest::sha::Hash.hash("abc");
                if direct_hash == trait_hash { return trait_hash; } else { return ""; }
            }
            """;

        var fixtureBuilder = new ManagedAdapterFixtureBuilder(
            harness.TemporaryRoot,
            GetDotnetPath(harness.RepositoryRoot));
        var validAssembly = await fixtureBuilder.BuildAsync(
            "valid",
            ManagedAdapterFixtureBuilder.ValidAdapterSource);
        var validBytes = await File.ReadAllBytesAsync(validAssembly);
        var validHash = Convert.ToHexString(SHA256.HashData(validBytes)).ToLowerInvariant();
        AssertTrue(IsLowerSha256Value(validHash), "The adapter fixture must have a lowercase SHA-256 digest.");

        string AdapterManifest(
            string? hash,
            string path = "lib/Hob.ManagedAdapters.dll",
            string framework = "net10.0",
            string bridge = "hob.sha256-text.v1",
            string kind = "lib",
            bool includeTable = true)
        {
            var manifest = LibraryPackageManifest("sha256-adapter");
            if (kind != "lib")
                manifest = manifest.Replace("kind = \"lib\"", $"kind = \"{kind}\"", StringComparison.Ordinal);
            if (!includeTable)
                return manifest;

            manifest += "\n[managed_adapter]\n"
                + $"bridge_id = \"{bridge}\"\n"
                + $"target_framework = \"{framework}\"\n"
                + $"assembly_path = \"{path}\"\n";
            if (hash is not null)
                manifest += $"assembly_sha256 = \"{hash}\"\n";
            return manifest;
        }

        async Task<(string Root, string AdapterRoot, string Manifest, string AdapterDll)> WriteAdapterPackageAsync(
            string caseName,
            string assembly,
            string manifest,
            string source = adapterSource)
        {
            var root = await harness.WritePackageGraphAsync(
                caseName,
                new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
                {
                    ["root"] = new PackageFixture(
                        CliPackageManifest() + "\n[dependencies]\ndigest = \"../adapter\"\n",
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["src/app/main.hob"] = consumerSource
                        }),
                    ["adapter"] = new PackageFixture(
                        manifest,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["src/sha.hob"] = source
                        })
                });
            var adapterRoot = Path.GetFullPath(Path.Combine(root, "..", "adapter"));
            var adapterDll = Path.Combine(adapterRoot, "lib", "Hob.ManagedAdapters.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(adapterDll)!);
            File.Copy(assembly, adapterDll, overwrite: true);
            return (root, adapterRoot, manifest, adapterDll);
        }

        async Task AssertDiagnosticAsync(
            string caseName,
            string packageRoot,
            string expectedCode,
            string? messageFragment = null)
        {
            var result = await harness.InvokePackageDirectoryAsync(caseName, packageRoot, "check", "--json");
            AssertTrue(result.ExitCode != 0, $"Invalid managed adapter case {caseName} unexpectedly passed. {Describe(result)}");
            AssertEqual(string.Empty, result.StandardError, Describe(result));
            var diagnostic = ParseDiagnosticSnapshots(result.StandardOutput)
                .FirstOrDefault(item => item.Code == expectedCode);
            AssertTrue(diagnostic is not null,
                $"Expected {expectedCode} for managed adapter case {caseName}. {result.StandardOutput}");
            if (messageFragment is not null)
                AssertTrue(diagnostic!.Message.Contains(messageFragment, StringComparison.Ordinal),
                    $"Expected diagnostic text <{messageFragment}> for {caseName}. {result.StandardOutput}");
            AssertTrue(!result.StandardError.Contains("Unhandled exception", StringComparison.Ordinal),
                $"Adapter rejection should be a structured diagnostic. {Describe(result)}");
        }
        var package = await WriteAdapterPackageAsync(
            "managed-adapter-valid",
            validAssembly,
            AdapterManifest(validHash));
        var packageRoot = package.Root;
        var adapterRoot = package.AdapterRoot;
        var adapterDll = package.AdapterDll;
        var lockPath = Path.Combine(packageRoot, "hob.lock");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("managed-adapter-lock", packageRoot, "lock"));
        var firstLockBytes = await File.ReadAllBytesAsync(lockPath);
        var firstLockText = new UTF8Encoding(false, true).GetString(firstLockBytes);
        using (var lockDocument = JsonDocument.Parse(firstLockBytes))
        {
            var root = lockDocument.RootElement;
            AssertEqual(3, root.GetProperty("schema_version").GetInt32(),
                "Managed adapter packages must use lock schema version 3.");
            AssertJsonPropertyOrder(root, "schema_version,root,packages");
            var lockedRoot = root.GetProperty("root");
            AssertJsonPropertyOrder(lockedRoot, "name,version,manifest_sha256,source,managed_adapter,dependencies");
            AssertEqual(JsonValueKind.Null, lockedRoot.GetProperty("managed_adapter").ValueKind,
                "A consumer without an adapter declaration must record a null lock adapter.");
            AssertEqual("../adapter", lockedRoot.GetProperty("dependencies").GetProperty("digest").GetString(),
                "The consumer lock must retain its relative dependency path.");
            var lockedAdapter = root.GetProperty("packages").EnumerateArray().Single();
            AssertJsonPropertyOrder(lockedAdapter,
                "path,name,version,source,content_sha256,managed_adapter,dependencies");
            AssertEqual("../adapter", lockedAdapter.GetProperty("path").GetString(),
                "The adapter lock entry must keep a package-relative path.");
            AssertJsonPropertyOrder(lockedAdapter.GetProperty("source"), "kind,path");
            AssertEqual("path", lockedAdapter.GetProperty("source").GetProperty("kind").GetString(),
                "The adapter dependency should retain its path source identity.");
            var descriptor = lockedAdapter.GetProperty("managed_adapter");
            AssertJsonPropertyOrder(descriptor,
                "bridge_id,catalog_revision,operation_ids,target_framework,portability_target,assembly_path,assembly_sha256,closure_sha256");
            AssertEqual("hob.sha256-text.v1", descriptor.GetProperty("bridge_id").GetString(),
                "The lock must bind the selected bridge.");
            AssertEqual("1", descriptor.GetProperty("catalog_revision").GetString(),
                "The lock must bind the catalog revision.");
            AssertJsonStringArray(descriptor.GetProperty("operation_ids"), ["sha256.text.hash_utf8"]);
            AssertEqual("net10.0", descriptor.GetProperty("target_framework").GetString(),
                "The lock must bind the target framework.");
            AssertEqual("portable-anycpu-il", descriptor.GetProperty("portability_target").GetString(),
                "The lock must bind the portability target.");
            AssertEqual("lib/Hob.ManagedAdapters.dll", descriptor.GetProperty("assembly_path").GetString(),
                "The lock must bind the normalized package-relative assembly path.");
            AssertEqual(validHash, descriptor.GetProperty("assembly_sha256").GetString(),
                "The lock must bind the exact adapter assembly bytes.");
            AssertTrue(IsLowerSha256Value(descriptor.GetProperty("closure_sha256").GetString()),
                "The lock must bind a lowercase catalog closure digest.");
        }
        AssertTrue(!firstLockText.Contains(packageRoot, StringComparison.OrdinalIgnoreCase)
            && !firstLockText.Contains(harness.TemporaryRoot, StringComparison.OrdinalIgnoreCase),
            "The package lock must not serialize an absolute workspace path.");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "managed-adapter-lock-repeat", packageRoot, "lock"));
        var repeatedLockBytes = await File.ReadAllBytesAsync(lockPath);
        AssertTrue(firstLockBytes.SequenceEqual(repeatedLockBytes),
            "Repeated lock generation must be byte-for-byte deterministic.");

        var check = await harness.InvokePackageDirectoryAsync("managed-adapter-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var audit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, audit.ExitCode, Describe(audit));
        AssertEqual(string.Empty, audit.StandardError, Describe(audit));
        using var auditDocument = JsonDocument.Parse(audit.StandardOutput);
        var auditRoot = auditDocument.RootElement;
        AssertAuditPropertyOrder(auditRoot);
        AssertEqual(11, auditRoot.GetProperty("schema_version").GetInt32(),
            "Managed adapter audit reports must use schema version 11.");
        var auditAdapters = auditRoot.GetProperty("managed_adapters");
        AssertSha256AdapterProvenance(auditAdapters, "../adapter", validHash);
        AssertAuditPortable(audit.StandardOutput, packageRoot, harness.TemporaryRoot);
        var repeatedAudit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(audit.StandardOutput, repeatedAudit.StandardOutput,
            "Repeated audit reports must be byte-for-byte deterministic.");

        var managedBuild = await harness.InvokePackageDirectoryAsync("managed-adapter-build", packageRoot, "build");
        AssertEqual(0, managedBuild.ExitCode, Describe(managedBuild));
        var managedArtifact = ParseBuiltArtifact(managedBuild, "Built executable: ");
        var managedOutputDirectory = Path.GetDirectoryName(managedArtifact)!;
        using (var receipt = await AssertBuildReceiptAsync(
                   managedOutputDirectory,
                   "managed",
                   expectedRuntimeIdentifier: null,
                   [Path.GetRelativePath(managedOutputDirectory, managedArtifact).Replace(Path.DirectorySeparatorChar, '/')],
                   packageRoot,
                   adapterRoot,
                   harness.TemporaryRoot))
        {
            AssertEqual(3, receipt.RootElement.GetProperty("schema_version").GetInt32(),
                "Managed adapter builds must use receipt schema version 3.");
            AssertSha256AdapterProvenance(receipt.RootElement.GetProperty("managed_adapters"), "../adapter", validHash);
            AssertEqual(auditAdapters.GetRawText(), receipt.RootElement.GetProperty("managed_adapters").GetRawText(),
                "The receipt must repeat the canonical audit adapter provenance.");
        }

        var managedRun = await harness.InvokePackageDirectoryAsync("managed-adapter-run", packageRoot, "run");
        AssertRunOutput(expectedHash + Environment.NewLine, managedRun);

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("The managed adapter NativeAOT smoke test requires x64 Windows or Linux.");
        var nativeBuild = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "managed-adapter-aot-build",
            packageRoot,
            "build",
            AotPublishTimeout,
            "--aot",
            "--rid",
            CurrentHostAotRid());
        AssertEqual(0, nativeBuild.ExitCode, Describe(nativeBuild));
        var nativeArtifact = ParseBuiltArtifact(nativeBuild, "Built native executable: ");
        AssertEqual(CurrentHostAotRid() == "win-x64" ? "harness-package.exe" : "harness-package",
            Path.GetFileName(nativeArtifact),
            "The adapter NativeAOT executable should use the root package name.");
        var nativeRun = await ExecuteNativeAsync(nativeArtifact, TimeSpan.FromSeconds(30));
        AssertRunOutput(expectedHash + Environment.NewLine, nativeRun);
        var nativeOutputDirectory = Path.GetDirectoryName(nativeArtifact)!;
        using (var receipt = await AssertBuildReceiptAsync(
                   nativeOutputDirectory,
                   "native_aot",
                   CurrentHostAotRid(),
                   [Path.GetRelativePath(nativeOutputDirectory, nativeArtifact).Replace(Path.DirectorySeparatorChar, '/')],
                   packageRoot,
                   adapterRoot,
                   harness.TemporaryRoot))
        {
            AssertSha256AdapterProvenance(receipt.RootElement.GetProperty("managed_adapters"), "../adapter", validHash);
        }

        var closureMatch = Regex.Match(firstLockText, "\"closure_sha256\"\\s*:\\s*\"(?<hash>[0-9a-f]{64})\"");
        AssertTrue(closureMatch.Success, "The adapter lock must contain its catalog closure hash.");
        var alteredHash = new string('0', 64);
        AssertTrue(closureMatch.Groups["hash"].Value != alteredHash, "The closure drift must change the recorded digest.");
        var alteredLock = firstLockText.Replace(closureMatch.Groups["hash"].Value, alteredHash, StringComparison.Ordinal);
        await File.WriteAllTextAsync(lockPath, alteredLock, new UTF8Encoding(false));
        await AssertDiagnosticAsync("managed-adapter-closure-drift", packageRoot, "E_LOCK");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "managed-adapter-lock-repair-closure", packageRoot, "lock"));

        var changedSource = ManagedAdapterFixtureBuilder.ValidAdapterSource.Replace(
            "Encoding.UTF8.GetBytes(text)", "Encoding.Unicode.GetBytes(text)", StringComparison.Ordinal);
        var changedAssembly = await fixtureBuilder.BuildAsync("changed-bytes", changedSource);
        var changedBytes = await File.ReadAllBytesAsync(changedAssembly);
        var changedHash = Convert.ToHexString(SHA256.HashData(changedBytes)).ToLowerInvariant();
        AssertTrue(changedHash != validHash, "The byte drift fixture must change the adapter bytes.");
        var originalManifest = await File.ReadAllTextAsync(Path.Combine(adapterRoot, "hob.toml"));
        File.Copy(changedAssembly, adapterDll, overwrite: true);
        await File.WriteAllTextAsync(
            Path.Combine(adapterRoot, "hob.toml"),
            AdapterManifest(changedHash),
            new UTF8Encoding(false));
        await AssertDiagnosticAsync("managed-adapter-byte-drift", packageRoot, "E_LOCK");
        File.Copy(validAssembly, adapterDll, overwrite: true);
        await File.WriteAllTextAsync(
            Path.Combine(adapterRoot, "hob.toml"),
            originalManifest,
            new UTF8Encoding(false));
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "managed-adapter-lock-repair-bytes", packageRoot, "lock"));

        await TestManagedAdapterRejectionsAsync(harness, fixtureBuilder, validAssembly, validHash, adapterSource, AdapterManifest);
    }
}
