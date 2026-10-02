using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestExplicitGenericFunctionArguments(Harness harness)
    {
        var fixturePath = Path.Combine(harness.RepositoryRoot, "fixtures", "119-valid-explicit-generic-calls.hob");
        var fixtureSource = await File.ReadAllTextAsync(fixturePath);
        AssertRunOutput("60" + Environment.NewLine,
            await harness.InvokeAsync("explicit-generic-calls-managed", "run", fixtureSource));

        var moduleTestRoot = await harness.WritePackageAsync(
            "explicit-generic-call-module-test",
            LibraryPackageManifest("explicit-generic-call-module-test"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module app::main;

                    fn identity<T>(value: T) -> T effects {} { return value; }

                    test "same-module explicit generic call" {
                        let value: i32 = identity::<i32>(9);
                        assert value == 9;
                    }
                    """
            });
        var moduleTestRun = await harness.InvokePackageDirectoryAsync(
            "explicit-generic-call-module-test-run", moduleTestRoot, "test");
        AssertEqual(0, moduleTestRun.ExitCode, Describe(moduleTestRun));
        AssertEqual("PASS app::main :: same-module explicit generic call" + Environment.NewLine
            + "1 passed, 0 failed" + Environment.NewLine, moduleTestRun.StandardOutput, Describe(moduleTestRun));

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("Explicit generic-call NativeAOT coverage requires Windows x64 or Linux x64.");

        var aotBuild = await harness.InvokeWithTimeoutAsync(
            "explicit-generic-calls-aot", "build", fixtureSource, AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, aotBuild.ExitCode, Describe(aotBuild));
        const string builtPrefix = "Built native executable: ";
        AssertTrue(aotBuild.StandardOutput.StartsWith(builtPrefix, StringComparison.Ordinal), Describe(aotBuild));
        AssertTrue(aotBuild.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal), Describe(aotBuild));
        var executablePath = aotBuild.StandardOutput[builtPrefix.Length..^Environment.NewLine.Length];
        AssertTrue(Path.IsPathFullyQualified(executablePath) && File.Exists(executablePath),
            $"Expected the explicit generic-call NativeAOT executable at {executablePath}.");
        AssertRunOutput("60" + Environment.NewLine,
            await ExecuteNativeAsync(executablePath, TimeSpan.FromSeconds(30)));

        const string commandSource = """
            module app::main;

            pub union RunError { Failed }

            command once {
                help "Check explicit generic call argument evaluation order.";
                argument marker: Text help "Unused command argument.";
                handler: self::app::main::run;
                error: self::app::main::describe_error;
            }

            fn pair<T>(left: T, right: T) -> T effects {} {
                return right;
            }

            pub fn run(args: self::app::main::OnceArgs, logger: Logger) -> Result<Text, self::app::main::RunError> effects { log.write } {
                let result: bool = pair::<bool>(
                    logger.info("first", "left argument"),
                    logger.info("second", "right argument"));
                return Ok("done");
            }

            pub fn describe_error(error: self::app::main::RunError) -> Text effects {} {
                return match error { self::app::main::RunError.Failed => "failed" };
            }
            """;
        var commandRoot = await harness.WritePackageAsync(
            "explicit-generic-call-single-evaluation",
            CliPackageManifest() + "\n[capabilities]\nlog.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = commandSource
            });
        var commandRun = await harness.InvokePackageDirectoryAsync(
            "explicit-generic-call-single-evaluation-run", commandRoot, "run", "--", "once", "unused");
        AssertEqual(0, commandRun.ExitCode, Describe(commandRun));
        AssertEqual("done" + Environment.NewLine, commandRun.StandardOutput, Describe(commandRun));
        var logLines = commandRun.StandardError
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        AssertEqual(2, logLines.Length,
            $"Both explicit generic-call arguments should run exactly once and write one log each. stderr=<{commandRun.StandardError}>");
        foreach (var line in logLines)
            using (JsonDocument.Parse(line)) { }
        using (var firstLog = JsonDocument.Parse(logLines[0]))
        using (var secondLog = JsonDocument.Parse(logLines[1]))
        {
            AssertEqual("first", firstLog.RootElement.GetProperty("event").GetString(),
                "Explicit generic call arguments must evaluate from left to right.");
            AssertEqual("second", secondLog.RootElement.GetProperty("event").GetString(),
                "Explicit generic call arguments must preserve source order.");
        }

        const string genericSource = """
            module app::generic;

            pub trait Measure { fn measure(value: Self) -> i32 effects {}; }

            fn measure_i32(value: i32) -> i32 effects {} {
                return value * 10;
            }

            pub impl self::app::generic::Measure for i32 {
                measure = self::app::generic::measure_i32;
            }

            pub fn target<T: self::app::generic::Measure>(value: T, path: Text, fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                let score: i32 = self::app::generic::Measure.measure(value);
                return fs.read_text(path);
            }
            """;
        const string callersSource = """
            module app::main;

            pub fn explicit_call(value: i32, path: Text, fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                return self::app::generic::target::<i32>(value, path, fs);
            }

            pub fn inferred_call(value: i32, path: Text, fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                return self::app::generic::target(value, path, fs);
            }
            """;
        var reportRoot = await harness.WritePackageAsync(
            "explicit-generic-call-reports",
            LibraryPackageManifest("explicit-generic-call-reports"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/generic.hob"] = genericSource,
                ["src/app/main.hob"] = callersSource
            });
        var reportCheck = await harness.InvokePackageDirectoryAsync(
            "explicit-generic-call-reports-check", reportRoot, "check", "--json");
        AssertEqual(0, reportCheck.ExitCode, Describe(reportCheck));
        AssertEqual(0, ParseDiagnosticSnapshots(reportCheck.StandardOutput).Length, Describe(reportCheck));

        var apiRun = await harness.InvokeCompilerCommandAsync("inspect", "api", reportRoot, "--json");
        AssertEqual(0, apiRun.ExitCode, Describe(apiRun));
        using var apiDocument = JsonDocument.Parse(apiRun.StandardOutput);
        var api = apiDocument.RootElement;
        AssertEqual(11, api.GetProperty("schema_version").GetInt32(),
            "Explicit generic calls must retain inspect API schema v11.");
        var apiFunctions = api.GetProperty("functions").EnumerateArray().ToArray();
        var explicitApi = apiFunctions.Single(function => function.GetProperty("id").GetString() == "self::app::main::explicit_call");
        var inferredApi = apiFunctions.Single(function => function.GetProperty("id").GetString() == "self::app::main::inferred_call");
        AssertEqual(explicitApi.GetProperty("calls").GetRawText(), inferredApi.GetProperty("calls").GetRawText(),
            "Explicit and inferred calls must retain identical public direct-call identities.");
        AssertEqual(explicitApi.GetProperty("trait_calls").GetRawText(), inferredApi.GetProperty("trait_calls").GetRawText(),
            "Explicit and inferred calls must select identical public trait witnesses.");
        AssertEqual("target", explicitApi.GetProperty("calls")[0].GetProperty("name").GetString(),
            "The direct-call report should identify the selected generic function.");
        AssertEqual("impl", explicitApi.GetProperty("trait_calls")[0].GetProperty("witnesses")[0]
                .GetProperty("witness").GetProperty("kind").GetString(),
            "The concrete explicit type argument should select the same closed trait implementation.");

        var auditRun = await harness.InvokeCompilerCommandAsync("audit", reportRoot, "--json");
        AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
        using var auditDocument = JsonDocument.Parse(auditRun.StandardOutput);
        var audit = auditDocument.RootElement;
        AssertEqual(9, audit.GetProperty("schema_version").GetInt32(),
            "Explicit generic calls must retain audit schema v9.");
        var auditFunctions = audit.GetProperty("compiler").GetProperty("functions").EnumerateArray().ToArray();
        var explicitAudit = auditFunctions.Single(function => function.GetProperty("module").GetString() == "app::main"
            && function.GetProperty("name").GetString() == "explicit_call");
        var inferredAudit = auditFunctions.Single(function => function.GetProperty("module").GetString() == "app::main"
            && function.GetProperty("name").GetString() == "inferred_call");
        AssertEqual(explicitAudit.GetProperty("direct_calls").GetRawText(), inferredAudit.GetProperty("direct_calls").GetRawText(),
            "Explicit and inferred calls must retain identical audit function identities.");
        AssertEqual(explicitAudit.GetProperty("trait_calls").GetRawText(), inferredAudit.GetProperty("trait_calls").GetRawText(),
            "Explicit and inferred calls must retain identical audit witnesses.");
        AssertEqual(explicitAudit.GetProperty("inferred_effects").GetRawText(), inferredAudit.GetProperty("inferred_effects").GetRawText(),
            "Explicit and inferred calls must infer the same transitive effects.");
        AssertJsonStringArray(explicitAudit.GetProperty("inferred_effects"), ["fs.read"]);

        const string invalidSource = """
            module harness::explicit_generic_call_errors;

            fn identity<T>(value: T) -> T effects {} { return value; }
            async fn async_identity<T>(value: T) -> T effects {} { return value; }

            pub fn invalid_type_vector() -> i32 effects {} {
                return identity::<i32, Text>(Some("must remain uncontextualized"));
            }

            pub fn unawaited() -> i32 effects {} {
                return async_identity::<i32>(1);
            }

            pub async fn await_sync() -> i32 effects {} {
                return await identity::<i32>(1);
            }

            pub fn explicit_builtin() -> Option<Text> effects {} {
                return Some::<Text>("not an ordinary generic function");
            }
            """;
        var invalidCheck = await harness.InvokeAsync(
            "explicit-generic-call-invalid-check", "check", invalidSource, "--json");
        AssertEqual(1, invalidCheck.ExitCode, Describe(invalidCheck));
        AssertEqual(string.Empty, invalidCheck.StandardError, Describe(invalidCheck));
        var invalidDiagnostics = ParseDiagnosticSnapshots(invalidCheck.StandardOutput);
        AssertTrue(invalidDiagnostics.Any(diagnostic => diagnostic.Code == "E_TYPE_MISMATCH"
                && diagnostic.Message.Contains("expects 1 type arguments, got 2", StringComparison.Ordinal)),
            "A wrong explicit type arity must be diagnosed.");
        AssertTrue(invalidDiagnostics.Any(diagnostic => diagnostic.Code == "E_TYPE_MISMATCH"
                && diagnostic.Message.Contains("requires an expected type of Option<T>", StringComparison.Ordinal)),
            "An incomplete type vector must not provide partial constructor context.");
        AssertTrue(invalidDiagnostics.Any(diagnostic => diagnostic.Code == "E_ASYNC_CALL_UNAWAITED"),
            "An explicit generic async call must retain the unawaited-call diagnostic.");
        AssertTrue(invalidDiagnostics.Any(diagnostic => diagnostic.Code == "E_AWAIT_SYNC"),
            "Awaiting an explicit generic synchronous call must retain the sync-call diagnostic.");
        AssertTrue(invalidDiagnostics.Any(diagnostic => diagnostic.Code == "E_TYPE_MISMATCH"
                && diagnostic.Message.Contains("does not accept explicit type arguments", StringComparison.Ordinal)),
            "Built-in constructors must reject direct explicit type arguments.");
        var invalidSourcePath = harness.LastSourcePath;
        var invalidSourceDirectory = Path.GetDirectoryName(invalidSourcePath)!;
        var invalidFiles = Directory.EnumerateFiles(invalidSourceDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(invalidSourceDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        AssertTrue(invalidFiles.SequenceEqual(["main.hob"], StringComparer.Ordinal),
            "Invalid explicit generic calls must not create compiler artifacts.");

        const string malformedCallTemplate = """
            module harness::malformed_explicit_generic_call;
            fn identity<T>(value: T) -> T effects {} { return value; }
            pub fn main() -> i32 effects {} { return $CALL$; }
            """;
        var malformedCalls = new (string Name, string Call)[]
        {
            ("missing closing angle", "identity::<i32(1)"),
            ("empty type vector", "identity::<>(1)"),
            ("trailing type comma", "identity::<i32,>(1)"),
            ("missing call parentheses", "identity::<i32>")
        };
        foreach (var (name, call) in malformedCalls)
        {
            var malformedSource = malformedCallTemplate.Replace("$CALL$", call, StringComparison.Ordinal);
            var malformedCheck = await harness.InvokeAsync(
                $"explicit-generic-call-{name.Replace(' ', '-')}", "check", malformedSource, "--json");
            AssertEqual(1, malformedCheck.ExitCode, Describe(malformedCheck));
            var malformedDiagnostics = ParseDiagnosticSnapshots(malformedCheck.StandardOutput);
            AssertTrue(malformedDiagnostics.Any(diagnostic => diagnostic.Code == "E_SYNTAX"),
                $"Malformed explicit-call syntax '{call}' must fail with E_SYNTAX; got {string.Join(" | ", malformedDiagnostics.Select(diagnostic => diagnostic.Code))}.");
            var malformedDirectory = Path.GetDirectoryName(harness.LastSourcePath)!;
            var malformedFiles = Directory.EnumerateFiles(malformedDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(malformedDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            AssertTrue(malformedFiles.SequenceEqual(["main.hob"], StringComparer.Ordinal),
                $"Malformed explicit-call syntax '{call}' must not create compiler artifacts.");
        }

        const string accessMain = """
            module app::main;
            pub fn main() -> i32 effects {} { return 0; }
            pub fn hidden_target() -> i32 effects {} {
                return self::app::generic::hidden::<i32>(1);
            }
            pub fn hidden_type() -> i32 effects {} {
                return self::app::generic::identity::<self::app::model::Hidden>(1);
            }
            """;
        const string accessGeneric = """
            module app::generic;
            pub fn identity<T>(value: T) -> T effects {} { return value; }
            fn hidden<T>(value: T) -> T effects {} { return value; }
            """;
        const string accessModel = """
            module app::model;
            struct Hidden { value: i32 }
            """;
        var accessRoot = await harness.WritePackageAsync(
            "explicit-generic-call-private-access",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = accessMain,
                ["src/app/generic.hob"] = accessGeneric,
                ["src/app/model.hob"] = accessModel
            });
        var accessBefore = SnapshotTree(accessRoot);
        var accessCheck = await harness.InvokePackageDirectoryAsync(
            "explicit-generic-call-private-access-check", accessRoot, "check", "--json");
        AssertEqual(1, accessCheck.ExitCode, Describe(accessCheck));
        var accessDiagnostics = ParseDiagnosticSnapshots(accessCheck.StandardOutput);
        AssertTrue(accessDiagnostics.Any(diagnostic => diagnostic.Code == "E_ACCESS_PRIVATE"
                && diagnostic.Message.Contains("function", StringComparison.OrdinalIgnoreCase)),
            "An explicit call must enforce target function visibility across modules.");
        AssertTrue(accessDiagnostics.Any(diagnostic => diagnostic.Code == "E_ACCESS_PRIVATE"
                && diagnostic.Message.Contains("Struct", StringComparison.OrdinalIgnoreCase)),
            $"Explicit type arguments must enforce type visibility across modules. Diagnostics: {string.Join(" | ", accessDiagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"))}");
        AssertEqual(accessBefore, SnapshotTree(accessRoot),
            "A package rejected for explicit-call visibility must not create or modify artifacts.");

        const string resourceSource = """
            module app::resources;

            fn read_for<T>(value: Option<List<T>>, fs: FsRead) -> i32 effects { fs.read } {
                let ignored: Result<Text, FsError> = fs.read_text("explicit-generic-resource-input");
                return 1;
            }

            pub fn invalid_resource_substitution(fs: FsRead) -> i32 effects {} {
                return self::app::resources::read_for::<FsRead>(None, fs);
            }
            """;
        var resourceRoot = await harness.WritePackageAsync(
            "explicit-generic-call-resource-validation",
            LibraryPackageManifest("explicit-generic-call-resource-validation"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/resources.hob"] = resourceSource
            });
        var resourceBefore = SnapshotTree(resourceRoot);
        var resourceCheck = await harness.InvokePackageDirectoryAsync(
            "explicit-generic-call-resource-validation-check", resourceRoot, "check", "--json");
        AssertEqual(1, resourceCheck.ExitCode, Describe(resourceCheck));
        var resourceDiagnostics = ParseDiagnosticSnapshots(resourceCheck.StandardOutput);
        AssertTrue(resourceDiagnostics.Any(diagnostic => diagnostic.Code == "E_RESOURCE_ESCAPE"),
            "A concrete resource-bearing list substitution must remain rejected.");
        AssertTrue(!resourceDiagnostics.Any(diagnostic => diagnostic.Code == "E_EFFECT_EXCEEDED"),
            "A resource-invalid explicit call must not add the callee's effect facts to its caller.");
        AssertEqual(resourceBefore, SnapshotTree(resourceRoot),
            "A package rejected for resource-bearing explicit types must not create or modify artifacts.");

        static string SnapshotTree(string root) => string.Join('\n', Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Prepend(root)
            .OrderBy(path => path == root ? "." : Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal)
            .Select(path =>
            {
                var relative = path == root ? "." : Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                    return $"D|{relative}";
                return $"F|{relative}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}";
            }));
    }
}
