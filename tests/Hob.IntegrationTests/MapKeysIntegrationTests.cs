using System.Runtime.InteropServices;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestNonTextMapKeys(Harness harness)
    {
        var fixturePath = Path.Combine(harness.RepositoryRoot, "fixtures", "121-valid-map-primitive-keys.hob");
        var source = await File.ReadAllTextAsync(fixturePath);
        var managed = await harness.InvokeAsync("non-text-map-keys-managed", "run", source);
        AssertRunOutput("1000" + Environment.NewLine, managed);

        await TestMapKeyReportsAsync(harness);
        await TestMapKeyRejectionsAsync(harness);

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("Non-Text Map-key NativeAOT coverage requires Windows x64 or Linux x64.");

        var aotBuild = await harness.InvokeWithTimeoutAsync(
            "non-text-map-keys-aot", "build", source, AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, aotBuild.ExitCode, Describe(aotBuild));
        AssertEqual(string.Empty, aotBuild.StandardError, Describe(aotBuild));
        const string builtPrefix = "Built native executable: ";
        AssertTrue(aotBuild.StandardOutput.StartsWith(builtPrefix, StringComparison.Ordinal), Describe(aotBuild));
        AssertTrue(aotBuild.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal), Describe(aotBuild));
        var executablePath = aotBuild.StandardOutput[builtPrefix.Length..^Environment.NewLine.Length];
        AssertTrue(Path.IsPathFullyQualified(executablePath) && File.Exists(executablePath),
            $"Expected the non-Text Map-key NativeAOT executable at {executablePath}.");
        AssertRunOutput("1000" + Environment.NewLine,
            await ExecuteNativeAsync(executablePath, TimeSpan.FromSeconds(30)));
    }

    private static async Task TestMapKeyReportsAsync(Harness harness)
    {
        const string reportSource = """
            module app::main;

            pub trait MapShapes {
                fn nested(values: Map<i32, List<Text>>, owner: Self) -> Map<bool, i64> effects {};
            }

            pub fn copy<T>(values: Map<i32, T>) -> Map<i32, T> effects {} {
                return values;
            }

            pub fn explicit_copy(values: Map<i32, Text>) -> Map<i32, Text> effects {} {
                return self::app::main::copy::<Text>(values);
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "map-key-report-shapes",
            LibraryPackageManifest("map-key-report-shapes"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = reportSource
            });
        var check = await harness.InvokePackageDirectoryAsync("map-key-report-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var apiRun = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, apiRun.ExitCode, Describe(apiRun));
        using var apiDocument = JsonDocument.Parse(apiRun.StandardOutput);
        var api = apiDocument.RootElement;
        AssertEqual(13, api.GetProperty("schema_version").GetInt32(),
            "Concrete Map keys must retain inspect API schema v13.");

        var apiMapShapes = api.GetProperty("traits").EnumerateArray()
            .Single(trait => trait.GetProperty("source_ids").EnumerateArray()
                .Any(id => id.GetString() == "self::app::main::MapShapes"));
        var apiNestedMethod = apiMapShapes.GetProperty("methods").EnumerateArray()
            .Single(method => method.GetProperty("name").GetString() == "nested");
        var apiNestedValues = apiNestedMethod.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "values");
        var apiNestedInput = apiNestedValues.GetProperty("type");
        AssertMapTypeShape(apiNestedInput, "i32", value =>
        {
            AssertEqual("list", value.GetProperty("kind").GetString(), "API Map values must retain recursive List type facts.");
            AssertPrimitiveTypeShape(value.GetProperty("item"), "Text");
        });
        AssertMapTypeShape(apiNestedMethod.GetProperty("return_type"), "bool", value =>
            AssertPrimitiveTypeShape(value, "i64"));

        var apiFunctions = api.GetProperty("functions").EnumerateArray().ToArray();
        var apiCopy = apiFunctions.Single(function => function.GetProperty("id").GetString() == "self::app::main::copy");
        AssertMapTypeShape(apiCopy.GetProperty("parameters")[0].GetProperty("type"), "i32", value =>
        {
            AssertEqual("type_parameter", value.GetProperty("kind").GetString(),
                "API Map values must retain generic value parameters.");
            AssertEqual("T", value.GetProperty("name").GetString(),
                "API Map value type parameter identity must remain stable.");
        });
        var apiExplicitCopy = apiFunctions.Single(function => function.GetProperty("id").GetString() == "self::app::main::explicit_copy");
        var apiCalls = apiExplicitCopy.GetProperty("calls").EnumerateArray().ToArray();
        AssertEqual(1, apiCalls.Length,
            "The explicit generic Map helper should produce one direct call and no synthetic Map call facts.");
        AssertEqual("copy", apiCalls[0].GetProperty("name").GetString(),
            "The explicit generic Map helper should report its actual target function.");

        var auditRun = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
        using var auditDocument = JsonDocument.Parse(auditRun.StandardOutput);
        var audit = auditDocument.RootElement;
        AssertEqual(11, audit.GetProperty("schema_version").GetInt32(),
            "Concrete Map keys must retain audit schema v11.");
        var auditShapes = audit.GetProperty("compiler").GetProperty("traits").EnumerateArray()
            .Single(trait => trait.GetProperty("module").GetString() == "app::main"
                && trait.GetProperty("name").GetString() == "MapShapes");
        var auditNestedMethod = auditShapes.GetProperty("methods").EnumerateArray()
            .Single(method => method.GetProperty("name").GetString() == "nested");
        var auditNestedValues = auditNestedMethod.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "values");
        AssertMapTypeShape(auditNestedValues.GetProperty("type"), "i32", value =>
        {
            AssertEqual("list", value.GetProperty("kind").GetString(), "Audit Map values must retain recursive List type facts.");
            AssertPrimitiveTypeShape(value.GetProperty("item"), "Text");
        });
        AssertMapTypeShape(auditNestedMethod.GetProperty("return_type"), "bool", value =>
            AssertPrimitiveTypeShape(value, "i64"));
        var auditExplicitCopy = audit.GetProperty("compiler").GetProperty("functions").EnumerateArray()
            .Single(function => function.GetProperty("module").GetString() == "app::main"
                && function.GetProperty("name").GetString() == "explicit_copy");
        var auditCalls = auditExplicitCopy.GetProperty("direct_calls").EnumerateArray().ToArray();
        AssertEqual(1, auditCalls.Length,
            "The audit should record the explicit generic Map helper as one real direct call.");
        AssertEqual("copy", auditCalls[0].GetProperty("name").GetString(),
            "The audit direct-call identity should identify the generic helper.");

        await TestInvalidExplicitMapKeyVectorAsync(harness);
    }

    private static async Task TestInvalidExplicitMapKeyVectorAsync(Harness harness)
    {
        const string source = """
            module app::invalid_map_key_call;

            fn read_for<T>(value: Option<T>, fs: FsRead) -> i32 effects { fs.read } {
                let ignored: Result<Text, FsError> = fs.read_text("invalid-map-key-call-input");
                return 1;
            }

            pub fn invalid(fs: FsRead) -> i32 effects {} {
                return self::app::invalid_map_key_call::read_for::<Map<FsRead, i32>>(None, fs);
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "map-key-invalid-explicit-vector",
            LibraryPackageManifest("map-key-invalid-explicit-vector"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/invalid_map_key_call.hob"] = source
            });
        var before = SnapshotTree(packageRoot);
        var check = await harness.InvokePackageDirectoryAsync(
            "map-key-invalid-explicit-vector-check", packageRoot, "check", "--json");
        AssertEqual(1, check.ExitCode, Describe(check));
        var diagnostics = ParseDiagnosticSnapshots(check.StandardOutput);
        AssertTrue(diagnostics.Any(diagnostic => diagnostic.Code == "E_RESOURCE_ESCAPE"),
            "An explicit Map key vector containing FsRead must be rejected as resource-bearing.");
        AssertTrue(diagnostics.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "A resource-invalid explicit Map key vector must not add the helper's fs.read effect to the caller.");
        AssertEqual(before, SnapshotTree(packageRoot),
            "Rejecting an invalid explicit Map key vector must not mutate the package source tree or emit artifacts.");

        var build = await harness.InvokePackageDirectoryAsync(
            "map-key-invalid-explicit-vector-build", packageRoot, "build");
        AssertTrue(build.ExitCode != 0, $"An invalid explicit Map key vector unexpectedly built. {Describe(build)}");
        AssertTrue((build.StandardOutput + build.StandardError).Contains("E_RESOURCE_ESCAPE", StringComparison.Ordinal),
            $"The invalid explicit Map key vector build should report the resource diagnostic. {Describe(build)}");
        AssertTrue(!(build.StandardOutput + build.StandardError).Contains("E_EFFECT_EXCEEDED", StringComparison.Ordinal),
            $"An invalid explicit Map key vector must not emit an effect-exceeded diagnostic. {Describe(build)}");
        AssertEqual(before, SnapshotTree(packageRoot),
            "A failed explicit Map key-vector build must leave source files and artifact state unchanged.");

        static string SnapshotTree(string root) => string.Join('\n', Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Prepend(root)
            .OrderBy(path => path == root ? "." : Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal)
            .Select(path =>
            {
                var relative = path == root ? "." : Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                    return $"D|{relative}";
                return $"F|{relative}|{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))}";
            }));
    }

    private static async Task TestMapKeyRejectionsAsync(Harness harness)
    {
        var resourceFixturePath = Path.Combine(
            harness.RepositoryRoot, "fixtures", "123-invalid-map-resource-keys.hob");
        var resourceFixtureCheck = await harness.InvokeFileAsync(
            "map-resource-key-fixture-check", resourceFixturePath, "check", "--json");
        AssertEqual(1, resourceFixtureCheck.ExitCode, Describe(resourceFixtureCheck));
        AssertEqual(string.Empty, resourceFixtureCheck.StandardError, Describe(resourceFixtureCheck));
        var resourceFixtureDiagnostics = ParseDiagnosticSnapshots(resourceFixtureCheck.StandardOutput);
        AssertEqual(7, resourceFixtureDiagnostics.Length,
            "The resource-key fixture should report five Map-key errors plus the existing two List resource-invariant errors.");
        AssertTrue(resourceFixtureDiagnostics.All(diagnostic => diagnostic.Code == "E_RESOURCE_ESCAPE"),
            "Every direct, nested, or stored resource-bearing Map key and independent List invariant must report E_RESOURCE_ESCAPE.");
        var actualResourceRanges = resourceFixtureDiagnostics
            .Select(diagnostic => (diagnostic.StartLine, diagnostic.StartColumn))
            .OrderBy(range => range.StartLine)
            .ThenBy(range => range.StartColumn)
            .ToArray();
        var expectedResourceRanges = new[]
        {
            (6, 23), (7, 15), (7, 27), (7, 82), (8, 28), (9, 28), (10, 29)
        };
        AssertTrue(actualResourceRanges.SequenceEqual(expectedResourceRanges),
            $"Resource-key diagnostics should stay on the exact key and existing List invariant tokens. Got [{string.Join(", ", actualResourceRanges.Select(range => $"{range.StartLine}:{range.StartColumn}"))}].");
        var mapKeyResourceDiagnostic = resourceFixtureDiagnostics.Single(diagnostic =>
            diagnostic.StartLine == 7 && diagnostic.StartColumn == 27);
        AssertEqual("Map keys cannot contain resource handles, directly or through stored fields",
            mapKeyResourceDiagnostic.Message,
            "A nested resource-bearing Map key should receive the key-specific resource diagnostic.");

        const string wrongKeySource = """
            module harness::map_wrong_key;
            pub fn main() -> i32 effects {} {
                let values: Map<i32, Text> = Map.empty();
                let invalid: Map<i32, Text> = values.set(true, "wrong key");
                return invalid.length;
            }
            """;
        await AssertMapMutationRejectedAsync(harness, "map-wrong-key", wrongKeySource, "true");

        const string wrongValueSource = """
            module harness::map_wrong_value;
            pub fn main() -> i32 effects {} {
                let values: Map<i32, Text> = Map.empty();
                let invalid: Map<i32, Text> = values.set(1, false);
                return invalid.length;
            }
            """;
        await AssertMapMutationRejectedAsync(harness, "map-wrong-value", wrongValueSource, "false");

        const string storedResourceKeySource = """
            module harness::map_stored_resource_key;
            pub struct Stored<T> { value: T }
            pub fn local_annotation() -> i32 effects {} {
                let values: Map<self::harness::map_stored_resource_key::Stored<FsRead>, i32> = Map.empty();
                return values.length;
            }
            """;
        var resourceCheck = await harness.InvokeAsync(
            "map-stored-resource-key-check", "check", storedResourceKeySource, "--json");
        AssertEqual(1, resourceCheck.ExitCode, Describe(resourceCheck));
        AssertEqual(string.Empty, resourceCheck.StandardError, Describe(resourceCheck));
        var resourceDiagnostics = ParseDiagnosticSnapshots(resourceCheck.StandardOutput);
        AssertEqual(1, resourceDiagnostics.Length,
            "A resource-bearing Map local should report once despite both key and local resource validation.");
        AssertEqual("E_RESOURCE_ESCAPE", resourceDiagnostics[0].Code,
            "A stored capability in a Map key must take resource precedence over the unsupported nominal-key error.");
        AssertEqual("Map keys cannot contain resource handles, directly or through stored fields", resourceDiagnostics[0].Message,
            "The key diagnostic should explain recursive stored-field resource reachability.");
        AssertRangeAtToken(storedResourceKeySource, resourceDiagnostics[0], "self", 1);
        var resourceSourceDirectory = Path.GetDirectoryName(Path.GetFullPath(harness.LastSourcePath))!;
        AssertNoCompilerArtifacts(resourceSourceDirectory);
        var resourceBuild = await harness.InvokeFileAsync(
            "map-stored-resource-key-build", harness.LastSourcePath, "build");
        AssertTrue(resourceBuild.ExitCode != 0, Describe(resourceBuild));
        AssertTrue((resourceBuild.StandardOutput + resourceBuild.StandardError).Contains("E_RESOURCE_ESCAPE", StringComparison.Ordinal),
            $"A resource-bearing Map key must stop the build before artifact emission. {Describe(resourceBuild)}");
        AssertNoCompilerArtifacts(resourceSourceDirectory);
    }

    private static async Task AssertMapMutationRejectedAsync(
        Harness harness,
        string caseName,
        string source,
        string offendingToken)
    {
        var check = await harness.InvokeAsync(caseName + "-check", "check", source, "--json");
        AssertEqual(1, check.ExitCode, Describe(check));
        AssertEqual(string.Empty, check.StandardError, Describe(check));
        var diagnostics = ParseDiagnosticSnapshots(check.StandardOutput);
        AssertEqual(1, diagnostics.Length, $"Expected exactly one Map mutation diagnostic. {Describe(check)}");
        AssertEqual("E_TYPE_MISMATCH", diagnostics[0].Code,
            "A Map operation must reject an argument that differs from its exact K or V type.");
        AssertRangeAtToken(source, diagnostics[0], offendingToken, 1);

        var sourcePath = Path.GetFullPath(harness.LastSourcePath);
        var sourceDirectory = Path.GetDirectoryName(sourcePath)!;
        AssertNoCompilerArtifacts(sourceDirectory);
        var build = await harness.InvokeFileAsync(caseName + "-build", sourcePath, "build");
        AssertTrue(build.ExitCode != 0, $"Invalid Map operation unexpectedly built. {Describe(build)}");
        AssertTrue((build.StandardOutput + build.StandardError).Contains("E_TYPE_MISMATCH", StringComparison.Ordinal),
            $"Invalid Map operation should be diagnosed before backend output. {Describe(build)}");
        AssertNoCompilerArtifacts(sourceDirectory);
    }

    private static void AssertMapTypeShape(JsonElement map, string keyName, Action<JsonElement> verifyValue)
    {
        AssertJsonPropertyOrder(map, "kind,key,value");
        AssertEqual("map", map.GetProperty("kind").GetString(), "A Map report type should retain its map kind.");
        AssertPrimitiveTypeShape(map.GetProperty("key"), keyName);
        verifyValue(map.GetProperty("value"));
    }

    private static void AssertPrimitiveTypeShape(JsonElement type, string name)
    {
        AssertEqual("primitive", type.GetProperty("kind").GetString(), "Expected a primitive report type.");
        AssertEqual(name, type.GetProperty("name").GetString(), "Unexpected primitive type in recursive Map report facts.");
    }
}
