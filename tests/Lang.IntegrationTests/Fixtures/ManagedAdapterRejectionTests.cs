using System.Security.Cryptography;
using System.Text;

internal static partial class IntegrationTests
{
    private static async Task TestManagedAdapterRejectionsAsync(
        Harness harness,
        ManagedAdapterFixtureBuilder fixtureBuilder,
        string validAssembly,
        string validHash,
        string adapterSource,
        Func<string?, string, string, string, string, bool, string> makeManifest)
    {
        const string assemblyPath = "lib/Lang.ManagedAdapters.dll";
        const string consumerSource = """
            module app::main;
            pub fn main() -> Text effects {} {
                return digest::sha::hash_utf8("abc");
            }
            """;

        async Task<(string Root, string AdapterRoot, string AdapterDll)> CreatePackageAsync(
            string caseName,
            string assembly,
            string manifest,
            string source = "")
        {
            var packageRoot = await harness.WritePackageGraphAsync(
                caseName,
                new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
                {
                    ["root"] = new PackageFixture(
                        CliPackageManifest() + "\n[dependencies]\ndigest = \"../adapter\"\n",
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["src/app/main.lang"] = consumerSource
                        }),
                    ["adapter"] = new PackageFixture(
                        manifest,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["src/sha.lang"] = source.Length == 0 ? adapterSource : source
                        })
                });
            var adapterRoot = Path.GetFullPath(Path.Combine(packageRoot, "..", "adapter"));
            var adapterDll = Path.Combine(adapterRoot, "lib", "Lang.ManagedAdapters.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(adapterDll)!);
            File.Copy(assembly, adapterDll, overwrite: true);
            return (packageRoot, adapterRoot, adapterDll);
        }

        async Task ExpectAsync(string caseName, string packageRoot, string code, string? message = null)
        {
            var result = await harness.InvokePackageDirectoryAsync(caseName, packageRoot, "check", "--json");
            AssertTrue(result.ExitCode != 0, $"Invalid adapter case {caseName} unexpectedly passed. {Describe(result)}");
            AssertEqual(string.Empty, result.StandardError, Describe(result));
            var diagnostic = ParseDiagnosticSnapshots(result.StandardOutput)
                .FirstOrDefault(item => item.Code == code);
            AssertTrue(diagnostic is not null, $"Expected {code} for {caseName}. {result.StandardOutput}");
            if (message is not null)
                AssertTrue(diagnostic!.Message.Contains(message, StringComparison.Ordinal),
                    $"Expected diagnostic text <{message}> for {caseName}. {result.StandardOutput}");
            AssertTrue(!result.StandardError.Contains("Unhandled exception", StringComparison.Ordinal),
                $"Invalid adapter metadata should produce a diagnostic. {Describe(result)}");
        }

        async Task ExpectSemanticAsync(string caseName, string source, string code, string? message = null)
        {
            var package = await CreatePackageAsync(
                caseName,
                validAssembly,
                makeManifest(validHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true),
                source);
            AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
                $"{caseName}-lock", package.Root, "lock"));
            await ExpectAsync(caseName, package.Root, code, message);
        }

        var missingHashManifest = makeManifest(null, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true);
        var missingHashPackage = await CreatePackageAsync("managed-adapter-missing-hash", validAssembly, missingHashManifest);
        await ExpectAsync("managed-adapter-missing-hash", missingHashPackage.Root, "E_MANIFEST");

        var wrongHashPackage = await CreatePackageAsync(
            "managed-adapter-wrong-hash",
            validAssembly,
            makeManifest(new string('0', 64), assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-wrong-hash",
            wrongHashPackage.Root,
            "E_MANAGED_ADAPTER",
            "SHA-256 does not match");

        var unsafePathPackage = await CreatePackageAsync(
            "managed-adapter-unsafe-path",
            validAssembly,
            makeManifest(validHash, "../outside.dll", "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-unsafe-path",
            unsafePathPackage.Root,
            "E_MANIFEST",
            "package-relative file path");

        var missingFilePackage = await CreatePackageAsync(
            "managed-adapter-missing-file",
            validAssembly,
            makeManifest(validHash, "lib/missing.dll", "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync("managed-adapter-missing-file", missingFilePackage.Root, "E_MANAGED_ADAPTER");

        var wrongTargetPackage = await CreatePackageAsync(
            "managed-adapter-wrong-target",
            validAssembly,
            makeManifest(validHash, assemblyPath, "net9.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-wrong-target",
            wrongTargetPackage.Root,
            "E_MANAGED_ADAPTER",
            "requires target_framework");

        var unknownBridgePackage = await CreatePackageAsync(
            "managed-adapter-unknown-bridge",
            validAssembly,
            makeManifest(validHash, assemblyPath, "net10.0", "System.Console", "lib", true));
        await ExpectAsync(
            "managed-adapter-unknown-bridge",
            unknownBridgePackage.Root,
            "E_MANAGED_ADAPTER",
            "Unknown managed adapter bridge");

        var nonLibraryPackage = await CreatePackageAsync(
            "managed-adapter-non-library",
            validAssembly,
            makeManifest(validHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "cli", true));
        await ExpectAsync("managed-adapter-non-library", nonLibraryPackage.Root, "E_MANIFEST");

        var noManifestPackage = await CreatePackageAsync(
            "managed-adapter-source-without-manifest",
            validAssembly,
            makeManifest(validHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", false));
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "managed-adapter-source-without-manifest-lock", noManifestPackage.Root, "lock"));
        await ExpectAsync(
            "managed-adapter-source-without-manifest",
            noManifestPackage.Root,
            "E_ADAPTER_MANIFEST");

        var unknownOperation = adapterSource.Replace(
            "sha256.text.hash_utf8",
            "System.Console.WriteLine",
            StringComparison.Ordinal);
        await ExpectSemanticAsync(
            "managed-adapter-impostor-selector",
            unknownOperation,
            "E_ADAPTER_OPERATION",
            "not supported");

        var wrongSignature = adapterSource.Replace(
            "value: Text) -> Text",
            "value: i32) -> Text",
            StringComparison.Ordinal);
        await ExpectSemanticAsync(
            "managed-adapter-source-signature",
            wrongSignature,
            "E_ADAPTER_SIGNATURE");

        var privateAdapterSource = adapterSource.Replace(
            "pub adapter fn",
            "adapter fn",
            StringComparison.Ordinal);
        await ExpectSemanticAsync(
            "managed-adapter-private-function",
            privateAdapterSource,
            "E_ADAPTER_VISIBILITY");

        var genericAdapterSource = adapterSource.Replace(
            "hash_utf8(value: Text) -> Text",
            "hash_utf8<T>(value: T) -> T",
            StringComparison.Ordinal);
        await ExpectSemanticAsync(
            "managed-adapter-generic-function",
            genericAdapterSource,
            "E_ADAPTER_GENERIC");

        var duplicateOperationPackageRoot = await harness.WritePackageGraphAsync(
            "managed-adapter-duplicate-operation",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\ndigest = \"../adapter\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = consumerSource
                    }),
                ["adapter"] = new PackageFixture(
                    makeManifest(validHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/sha.lang"] = adapterSource,
                        ["src/extra.lang"] = """
                            module extra;
                            pub adapter fn hash_utf8(value: Text) -> Text effects {} = "sha256.text.hash_utf8";
                            """
                    })
            });
        var duplicateOperationAdapterDll = Path.Combine(
            duplicateOperationPackageRoot, "..", "adapter", "lib", "Lang.ManagedAdapters.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(duplicateOperationAdapterDll)!);
        File.Copy(validAssembly, duplicateOperationAdapterDll, overwrite: true);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "managed-adapter-duplicate-operation-lock",
            duplicateOperationPackageRoot,
            "lock"));
        await ExpectAsync(
            "managed-adapter-duplicate-operation",
            duplicateOperationPackageRoot,
            "E_ADAPTER_DUPLICATE_OPERATION");
        var effects = adapterSource.Replace("effects {}", "effects { fs.read }", StringComparison.Ordinal);
        await ExpectSemanticAsync("managed-adapter-source-effects", effects, "E_ADAPTER_EFFECT");

        var asynchronous = adapterSource.Replace(
            "pub adapter fn",
            "pub adapter async fn",
            StringComparison.Ordinal);
        await ExpectSemanticAsync("managed-adapter-source-async", asynchronous, "E_ADAPTER_ASYNC");

        var wrongIdentity = await fixtureBuilder.BuildAsync(
            "wrong-identity",
            ManagedAdapterFixtureBuilder.ValidAdapterSource,
            assemblyName: "Lang.ManagedAdapters.Impostor");
        var wrongIdentityHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(wrongIdentity))).ToLowerInvariant();
        var wrongIdentityPackage = await CreatePackageAsync(
            "managed-adapter-wrong-identity",
            wrongIdentity,
            makeManifest(wrongIdentityHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-wrong-identity",
            wrongIdentityPackage.Root,
            "E_MANAGED_ADAPTER",
            "assembly identity");

        const string wrongFrameworkSource = """
            using System;
            using System.Runtime.Versioning;
            using System.Security.Cryptography;
            using System.Text;

            [assembly: TargetFramework(".NETCoreApp,Version=v9.0")]
            namespace Lang.ManagedAdapters;

            public static class Sha256Text
            {
                public static string HashUtf8(string text)
                {
                    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
                    return System.Convert.ToHexString(bytes).ToLowerInvariant();
                }
            }
            """;
        var wrongFrameworkAssembly = await fixtureBuilder.BuildAsync(
            "wrong-assembly-framework",
            wrongFrameworkSource,
            suppressGeneratedTargetFrameworkAttribute: true);
        var wrongFrameworkHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(wrongFrameworkAssembly))).ToLowerInvariant();
        var wrongFrameworkPackage = await CreatePackageAsync(
            "managed-adapter-wrong-assembly-framework",
            wrongFrameworkAssembly,
            makeManifest(wrongFrameworkHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-wrong-assembly-framework",
            wrongFrameworkPackage.Root,
            "E_MANAGED_ADAPTER",
            "TargetFramework");

        const string wrongMemberSource = """
            namespace Lang.ManagedAdapters;
            public static class Sha256Text
            {
                public static object HashUtf8(string text) => text;
            }
            """;
        var wrongMemberAssembly = await fixtureBuilder.BuildAsync("wrong-member", wrongMemberSource);
        var wrongMemberHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(wrongMemberAssembly))).ToLowerInvariant();
        var wrongMemberPackage = await CreatePackageAsync(
            "managed-adapter-wrong-member",
            wrongMemberAssembly,
            makeManifest(wrongMemberHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-wrong-member",
            wrongMemberPackage.Root,
            "E_MANAGED_ADAPTER",
            "catalog method");

        const string wrongTargetTypeShapeSource = """
            using System;
            using System.Security.Cryptography;
            using System.Text;
            namespace Lang.ManagedAdapters;
            public class Sha256Text
            {
                public static string HashUtf8(string text)
                {
                    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
                    return Convert.ToHexString(bytes).ToLowerInvariant();
                }
            }
            """;
        var wrongTargetTypeShapeAssembly = await fixtureBuilder.BuildAsync(
            "wrong-target-type-shape",
            wrongTargetTypeShapeSource);
        var wrongTargetTypeShapeHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(wrongTargetTypeShapeAssembly))).ToLowerInvariant();
        var wrongTargetTypeShapePackage = await CreatePackageAsync(
            "managed-adapter-wrong-target-type-shape",
            wrongTargetTypeShapeAssembly,
            makeManifest(wrongTargetTypeShapeHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-wrong-target-type-shape",
            wrongTargetTypeShapePackage.Root,
            "E_MANAGED_ADAPTER",
            "public, abstract, sealed, non-generic class");
        var extraMethodSource = ManagedAdapterFixtureBuilder.ValidAdapterSource.Replace(
            "public static string HashUtf8(string text)",
            "public static string Extra() => string.Empty;\n            public static string HashUtf8(string text)",
            StringComparison.Ordinal);
        var extraMethodAssembly = await fixtureBuilder.BuildAsync("extra-method", extraMethodSource);
        var extraMethodHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(extraMethodAssembly))).ToLowerInvariant();
        var extraMethodPackage = await CreatePackageAsync(
            "managed-adapter-extra-method",
            extraMethodAssembly,
            makeManifest(extraMethodHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-extra-method",
            extraMethodPackage.Root,
            "E_MANAGED_ADAPTER",
            "public methods beyond");

        var extraTypeSource = ManagedAdapterFixtureBuilder.ValidAdapterSource + "\npublic sealed class ExtraPublicType { }\n";
        var extraTypeAssembly = await fixtureBuilder.BuildAsync("extra-type", extraTypeSource);
        var extraTypeHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(extraTypeAssembly))).ToLowerInvariant();
        var extraTypePackage = await CreatePackageAsync(
            "managed-adapter-extra-type",
            extraTypeAssembly,
            makeManifest(extraTypeHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-extra-type",
            extraTypePackage.Root,
            "E_MANAGED_ADAPTER",
            "public types beyond");

        const string extraFieldSource = """
            namespace Lang.ManagedAdapters;
            public static class Sha256Text
            {
                public static readonly string Extra = string.Empty;
                public static string HashUtf8(string text)
                {
                    var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
                    return System.Convert.ToHexString(bytes).ToLowerInvariant();
                }
            }
            """;
        var extraFieldAssembly = await fixtureBuilder.BuildAsync("extra-field", extraFieldSource);
        var extraFieldHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(extraFieldAssembly))).ToLowerInvariant();
        var extraFieldPackage = await CreatePackageAsync(
            "managed-adapter-extra-field",
            extraFieldAssembly,
            makeManifest(extraFieldHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-extra-field",
            extraFieldPackage.Root,
            "E_MANAGED_ADAPTER",
            "public fields");
        const string pInvokeSource = """
            using System.Runtime.InteropServices;
            namespace Lang.ManagedAdapters;
            public static class Sha256Text
            {
                [DllImport("libc", EntryPoint = "getpid")]
                private static extern int GetProcessId();

                public static string HashUtf8(string text) => GetProcessId().ToString();
            }
            """;
        var pInvokeAssembly = await fixtureBuilder.BuildAsync("p-invoke", pInvokeSource);
        var pInvokeHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(pInvokeAssembly))).ToLowerInvariant();
        var pInvokePackage = await CreatePackageAsync(
            "managed-adapter-p-invoke",
            pInvokeAssembly,
            makeManifest(pInvokeHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync("managed-adapter-p-invoke", pInvokePackage.Root, "E_MANAGED_ADAPTER", "native module references");

        var customAssembly = await fixtureBuilder.BuildCustomReferenceAssemblyAsync();
        const string customReferenceSource = """
            using System;
            using System.Security.Cryptography;
            using System.Text;
            namespace Lang.ManagedAdapters;
            public static class Sha256Text
            {
                public static string HashUtf8(string text)
                {
                    global::Custom.ManagedAdapterMarker.Touch();
                    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
                    return System.Convert.ToHexString(bytes).ToLowerInvariant();
                }
            }
            """;
        var customReferenceAdapter = await fixtureBuilder.BuildAsync(
            "custom-assembly-reference",
            customReferenceSource,
            additionalReferenceAssembly: customAssembly);
        var customReferenceHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(customReferenceAdapter))).ToLowerInvariant();
        var customReferencePackage = await CreatePackageAsync(
            "managed-adapter-custom-reference",
            customReferenceAdapter,
            makeManifest(customReferenceHash, assemblyPath, "net10.0", "lang.sha256-text.v1", "lib", true));
        await ExpectAsync(
            "managed-adapter-custom-reference",
            customReferencePackage.Root,
            "E_MANAGED_ADAPTER",
            "outside the selected .NET 10 reference pack");
    }
}