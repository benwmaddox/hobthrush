using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestClockCapability(Harness harness)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("Clock NativeAOT coverage requires Windows x64 or Linux x64.");

        const string librarySource = """
            module time::wall;

            pub struct Holder { value: Clock }
            pub struct Phantom<T> { marker: i32 }
            pub union ClockUse {
                Direct(Clock),
                Optional(Option<Clock>),
                Outcome(Result<Clock, Text>)
            }

            pub fn pass(clock: Clock) -> Clock effects {} {
                return clock;
            }

            pub fn optional(clock: Clock) -> Option<Clock> effects {} {
                return Some(clock);
            }

            pub fn result(clock: Clock) -> Result<Clock, Text> effects {} {
                return Ok(clock);
            }

            pub fn hold(clock: Clock) -> self::time::wall::Holder effects {} {
                return self::time::wall::Holder { value: clock };
            }

            pub fn phantom(value: self::time::wall::Phantom<Clock>) -> i32 effects {} {
                return value.marker;
            }

            pub fn sample(clock: Clock) -> i64 effects { clock.read } {
                return clock.unix_time_ms();
            }

            pub fn forward<T>(clock: Clock, value: T) -> T effects { clock.read } {
                let observed: i64 = clock.unix_time_ms();
                return value;
            }
            """;

        var helperExecutable = await harness.GetProcessFixtureExecutableAsync();
        var hostOs = OperatingSystem.IsWindows() ? "windows" : "linux";
        var runnerRelativePath = OperatingSystem.IsWindows() ? "bin/clock-runner.exe" : "bin/clock-runner";
        var runnerHash = HashSha256(await File.ReadAllBytesAsync(helperExecutable));
        var clockRootManifest = ClockCliManifest(includeClockGrant: false, hostOs, runnerRelativePath, runnerHash);
        var rootPackage = await harness.WritePackageGraphAsync(
            "clock-cli-capability",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(clockRootManifest, ClockCliSources()),
                ["runtime"] = new PackageFixture(
                    LibraryPackageManifest("clock-runtime"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/time/wall.hob"] = librarySource
                    })
            });
        await InstallPinnedExecutableAsync(rootPackage, runnerRelativePath, helperExecutable);

        var dependencyRoot = Path.GetFullPath(Path.Combine(rootPackage, "..", "runtime"));
        var dependencyManifest = await File.ReadAllTextAsync(Path.Combine(dependencyRoot, "hob.toml"));
        AssertTrue(!dependencyManifest.Contains("clock.read", StringComparison.Ordinal),
            "The library may declare and forward Clock effects without owning an application grant.");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "clock-library-grant-rejected",
            LibraryPackageManifest("clock-library-grant") + "\n[capabilities]\nclock.read = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "hob.toml");

        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "clock-cli-missing-grant-lock", rootPackage, "lock"));
        var missingGrant = await harness.InvokePackageDirectoryAsync(
            "clock-cli-missing-grant-check", rootPackage, "check", "--json");
        AssertTrue(missingGrant.ExitCode != 0 &&
            ParseDiagnosticSnapshots(missingGrant.StandardOutput).Any(diagnostic => diagnostic.Code == "E_CAPABILITY_MISSING"),
            $"A CLI handler requesting Clock without a root clock.read grant must be rejected. {Describe(missingGrant)}");

        var missingBuild = await harness.InvokePackageDirectoryAsync(
            "clock-cli-missing-grant-build", rootPackage, "build");
        AssertTrue(missingBuild.ExitCode != 0 && missingBuild.StandardError.Contains("E_CAPABILITY_MISSING", StringComparison.Ordinal),
            $"An ungranted Clock must fail before build output is produced. {Describe(missingBuild)}");
        var outputRoot = Path.Combine(rootPackage, "out");
        AssertTrue(!Directory.Exists(outputRoot) ||
            !Directory.EnumerateFileSystemEntries(outputRoot, "*", SearchOption.AllDirectories).Any(),
            "A rejected clock.read request must not leave build artifacts.");

        var grantedManifest = ClockCliManifest(includeClockGrant: true, hostOs, runnerRelativePath, runnerHash);
        await File.WriteAllTextAsync(Path.Combine(rootPackage, "hob.toml"), grantedManifest);
        var staleLock = await harness.InvokePackageDirectoryAsync(
            "clock-cli-grant-stales-lock", rootPackage, "check", "--json");
        AssertTrue(staleLock.ExitCode != 0 && staleLock.StandardOutput.Contains("E_LOCK", StringComparison.Ordinal),
            $"Changing the root clock.read grant must invalidate the dependency lock. {Describe(staleLock)}");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "clock-cli-grant-refresh-lock", rootPackage, "lock"));

        var check = await harness.InvokePackageDirectoryAsync("clock-cli-check", rootPackage, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var apiResult = await harness.InvokeCompilerCommandAsync("inspect", "api", rootPackage, "--json");
        AssertEqual(0, apiResult.ExitCode, Describe(apiResult));
        using (var apiDocument = JsonDocument.Parse(apiResult.StandardOutput))
        {
            var api = apiDocument.RootElement;
            AssertInspectApiPropertyOrder(api);
            AssertEqual(12, api.GetProperty("schema_version").GetInt32(),
                "Clock metadata must use inspect API schema version 12.");
            AssertJsonStringArray(api.GetProperty("manifest_grants"), ["clock.read", "log.write", "process.spawn"]);

            var sample = api.GetProperty("functions").EnumerateArray()
                .Single(function => function.GetProperty("id").GetString() == "runtime::time::wall::sample");
            var clockType = sample.GetProperty("parameters")[0].GetProperty("type");
            AssertEqual("primitive", clockType.GetProperty("kind").GetString(),
                "Clock should be represented as a primitive opaque capability in API metadata.");
            AssertEqual("Clock", clockType.GetProperty("name").GetString(),
                "The API should preserve the Clock source type name.");
            AssertJsonStringArray(sample.GetProperty("required_capabilities"), ["clock.read"]);

            var command = api.GetProperty("commands").EnumerateArray().Single();
            AssertJsonStringArray(command.GetProperty("required_capabilities"), ["clock.read", "log.write", "process.spawn"]);
            var handler = api.GetProperty("functions").EnumerateArray()
                .Single(function => function.GetProperty("id").GetString() == "self::handlers::run");
            var parameters = handler.GetProperty("parameters").EnumerateArray().ToArray();
            AssertEqual("args", parameters[0].GetProperty("name").GetString(),
                "CLI handlers should keep generated arguments before capabilities.");
            AssertEqual("logger", parameters[1].GetProperty("name").GetString(),
                "Logger should precede Clock in CLI capability parameters.");
            AssertEqual("clock", parameters[2].GetProperty("name").GetString(),
                "Clock should follow Logger and precede ProcessRunner in CLI capability parameters.");
            AssertEqual("runner", parameters[3].GetProperty("name").GetString(),
                "ProcessRunner should follow Clock in CLI capability parameters.");
            AssertEqual("Clock", parameters[2].GetProperty("type").GetProperty("name").GetString(),
                "The handler API should expose the injected Clock type.");
            AssertEqual("ProcessRunner", parameters[3].GetProperty("type").GetProperty("name").GetString(),
                "The handler API should expose ProcessRunner after Clock.");
        }

        var effectsResult = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", rootPackage, "self::handlers::run", "--json");
        AssertEqual(0, effectsResult.ExitCode, Describe(effectsResult));
        using (var effectsDocument = JsonDocument.Parse(effectsResult.StandardOutput))
        {
            var report = effectsDocument.RootElement;
            AssertJsonStringArray(report.GetProperty("inferred_effects"), ["clock.read", "log.write"]);
            AssertJsonStringArray(report.GetProperty("required_capabilities"), ["clock.read", "log.write"]);
            var path = report.GetProperty("effect_paths").EnumerateArray()
                .Single(item => item.GetProperty("effect").GetString() == "clock.read");
            var steps = path.GetProperty("steps").EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty).ToArray();
            AssertEqual(3, steps.Length,
                $"The Clock effect path should cross the root handler and library before the operation: [{string.Join(" -> ", steps)}].");
            AssertEqual("handlers::run", steps[0], "The Clock effect path should start at the CLI handler.");
            AssertTrue(steps[1].Contains("time::wall::sample", StringComparison.Ordinal),
                "The Clock effect path should include the library pass-through function.");
            AssertEqual("Clock.unix_time_ms", steps[2], "The Clock effect path should end at the trusted Clock operation.");
            var operation = report.GetProperty("trusted_operations").EnumerateArray()
                .Single(item => item.GetProperty("operation").GetString() == "Clock.unix_time_ms");
            AssertEqual("trusted_adapter", operation.GetProperty("trust").GetString(),
                "The Clock operation should be identified as a trusted host adapter.");
            AssertJsonStringArray(operation.GetProperty("effects"), ["clock.read"]);
        }

        var auditResult = await harness.InvokeCompilerCommandAsync("audit", rootPackage, "--json");
        AssertEqual(0, auditResult.ExitCode, Describe(auditResult));
        using (var auditDocument = JsonDocument.Parse(auditResult.StandardOutput))
        {
            var audit = auditDocument.RootElement;
            AssertAuditPropertyOrder(audit);
            AssertEqual(10, audit.GetProperty("schema_version").GetInt32(),
                "Clock audit metadata must use schema version 10.");
            AssertJsonStringArray(audit.GetProperty("manifest_grants"), ["clock.read", "log.write", "process.spawn"]);
            AssertTrue(audit.GetProperty("trusted_claims").EnumerateArray().Any(item =>
                    item.GetProperty("operation").GetString() == "Clock.unix_time_ms" &&
                    item.GetProperty("effects").EnumerateArray().Any(effect => effect.GetString() == "clock.read")),
                "Audit should record the Clock wall-clock operation and its clock.read effect.");
        }

        var build = await harness.InvokePackageDirectoryAsync("clock-cli-build", rootPackage, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        var artifact = ParseBuiltArtifact(build, "Built executable: ");
        var commandSchemaPath = Path.Combine(Path.GetDirectoryName(artifact)!, "command-schema.json");
        AssertTrue(File.Exists(commandSchemaPath), "The CLI build should emit command-schema.json.");
        using (var schema = JsonDocument.Parse(await File.ReadAllBytesAsync(commandSchemaPath)))
        {
            AssertEqual(5, schema.RootElement.GetProperty("schema_version").GetInt32(),
                "Clock command capabilities must use command schema version 5.");
            AssertJsonStringArray(schema.RootElement.GetProperty("commands")[0].GetProperty("capabilities"),
                ["log.write", "clock.read", "process.spawn"]);
        }

        var clockProbeLoadContext = ProbeClockTimestamp(artifact);
        for (var attempt = 0; attempt < 10 && clockProbeLoadContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        AssertTrue(!clockProbeLoadContext.IsAlive,
            "The generated Clock probe assembly should unload before the harness removes its temporary files.");

        var managedRun = await harness.InvokePackageDirectoryAsync(
            "clock-cli-managed-run", rootPackage, "run", "--", "now", "sample");
        AssertClockCliOutput(managedRun, "managed CLI");

        var nativeBuild = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "clock-cli-native-aot-build", rootPackage, "build", AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, nativeBuild.ExitCode, Describe(nativeBuild));
        const string nativePrefix = "Built native executable: ";
        AssertTrue(nativeBuild.StandardOutput.StartsWith(nativePrefix, StringComparison.Ordinal), Describe(nativeBuild));
        var nativeExecutable = nativeBuild.StandardOutput[nativePrefix.Length..].TrimEnd('\r', '\n');
        AssertTrue(Path.IsPathFullyQualified(nativeExecutable) && File.Exists(nativeExecutable),
            $"Expected a Clock NativeAOT executable at {nativeExecutable}.");
        AssertEqual(5, JsonDocument.Parse(await File.ReadAllBytesAsync(
                Path.Combine(Path.GetDirectoryName(nativeExecutable)!, "command-schema.json")))
            .RootElement.GetProperty("schema_version").GetInt32(),
            "The NativeAOT Clock command schema must use version 5.");
        var nativeRun = await ExecuteNativeAsync(nativeExecutable, TimeSpan.FromSeconds(30), "now", "sample");
        AssertClockCliOutput(nativeRun, "NativeAOT CLI");

        await TestClockWebInjection(harness);

        var resourcePackage = await harness.WritePackageAsync(
            "clock-resource-escape",
            LibraryPackageManifest("clock-resource-escape"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/invalid.hob"] = """
                    module invalid;

                    pub fn collect(clock: Clock) -> List<Clock> effects {} {
                        return [clock];
                    }

                    pub fn wrong_arity(clock: Clock) -> i64 effects { clock.read } {
                        return clock.unix_time_ms(1);
                    }

                    pub fn without_receiver() -> i64 effects {} {
                        return clock.unix_time_ms();
                    }
                    """
            });
        var resourceEscape = await harness.InvokePackageDirectoryAsync(
            "clock-resource-escape-check", resourcePackage, "check", "--json");
        AssertTrue(resourceEscape.ExitCode != 0 &&
            ParseDiagnosticSnapshots(resourceEscape.StandardOutput).Any(diagnostic => diagnostic.Code == "E_RESOURCE_ESCAPE"),
            $"Clock should retain the existing resource-escape restriction in collections. {Describe(resourceEscape)}");
        var negativeCodes = ParseDiagnosticSnapshots(resourceEscape.StandardOutput)
            .Select(diagnostic => diagnostic.Code).ToArray();
        AssertTrue(negativeCodes.Contains("E_TYPE_MISMATCH", StringComparer.Ordinal),
            $"Clock.unix_time_ms should reject nonzero argument arity. {Describe(resourceEscape)}");
        AssertTrue(negativeCodes.Contains("E_CAPABILITY_MISSING", StringComparer.Ordinal),
            $"Clock.unix_time_ms should require an explicit Clock receiver. {Describe(resourceEscape)}");

    }

    private static IReadOnlyDictionary<string, string> ClockCliSources() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = """
                module app::main;

                command now {
                    help "Read the current UTC Unix time.";
                    argument label: Text help "Invocation label.";
                    handler: self::handlers::run;
                    error: self::handlers::describe;
                }
                """,
            ["src/handlers.hob"] = """
                module handlers;

                pub union ClockError { OutsideExpectedRange }

                pub fn run(
                    args: self::app::main::NowArgs,
                    logger: Logger,
                    clock: Clock,
                    runner: ProcessRunner
                ) -> Result<Text, self::handlers::ClockError> effects { clock.read, log.write } {
                    let logged: bool = logger.info("clock.cli", "reading UTC wall time");
                    let observed: i64 = runtime::time::wall::sample(clock);
                    if observed >= 1700000000000i64 {
                        if observed <= 2000000000000i64 {
                            return Ok("clock-cli-ok");
                        }
                    }
                    return Err(self::handlers::ClockError.OutsideExpectedRange);
                }

                pub fn describe(error: self::handlers::ClockError) -> Text effects {} {
                    return match error {
                        self::handlers::ClockError.OutsideExpectedRange => "outside expected range"
                    };
                }
                """
        };

    private static string ClockCliManifest(bool includeClockGrant, string hostOs, string runnerPath, string runnerHash) =>
        CliPackageManifest()
            .Replace("name = \"harness-package\"", "name = \"clock-cli-root\"", StringComparison.Ordinal)
            + $"process_{hostOs}_path = \"{runnerPath}\"\n"
            + $"process_{hostOs}_sha256 = \"{runnerHash}\"\n"
            + "\n[capabilities]\n"
            + (includeClockGrant ? "clock.read = \"allow\"\n" : string.Empty)
            + "log.write = \"allow\"\nprocess.spawn = \"allow\"\n"
            + "\n[dependencies]\nruntime = \"../runtime\"\n";

    private static void AssertClockCliOutput(ProcessResult result, string context)
    {
        AssertEqual(0, result.ExitCode, $"{context} should execute the granted Clock handler. {Describe(result)}");
        AssertEqual("clock-cli-ok" + Environment.NewLine, result.StandardOutput,
            $"{context} should return only the bounded-time marker, never stringify the timestamp.");
        var lines = result.StandardError.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        AssertEqual(1, lines.Length, $"{context} should emit one structured Logger.info line.");
        using var log = JsonDocument.Parse(lines[0]);
        AssertJsonPropertyOrder(log.RootElement, "level,event,detail");
        AssertEqual("clock.cli", log.RootElement.GetProperty("event").GetString(),
            $"{context} should inject Logger before Clock.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ProbeClockTimestamp(string assemblyPath)
    {
        var loadContext = new AssemblyLoadContext($"clock-probe-{Guid.NewGuid():N}", isCollectible: true);
        var weakReference = new WeakReference(loadContext);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var moduleType = assembly.GetType("HobModule", throwOnError: true)!;
            var clockType = moduleType.GetNestedType("Clock", BindingFlags.Public)
                ?? throw new InvalidOperationException("Generated CLI assembly does not expose the opaque Clock runtime type.");
            var constructor = clockType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null)
                ?? throw new InvalidOperationException("Generated Clock does not have a trusted non-public constructor.");
            var sample = moduleType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name.StartsWith("Function_", StringComparison.Ordinal)
                    && method.ReturnType == typeof(long)
                    && method.GetParameters() is { Length: 1 } parameters
                    && parameters[0].ParameterType == clockType);

            var clock = constructor.Invoke(null);
            var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var observed = sample.Invoke(null, [clock]) is long value
                ? value
                : throw new InvalidOperationException("Clock.unix_time_ms did not return a signed 64-bit value.");
            var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            AssertTrue(before <= observed && observed <= after,
                $"Clock.unix_time_ms must return a UTC Unix wall-clock timestamp bounded by host observations ({before} <= {observed} <= {after}).");
        }
        finally
        {
            loadContext.Unload();
        }

        return weakReference;
    }

    private static async Task TestClockWebInjection(Harness harness)
    {
        const string source = """
            module app::main;

            pub union Reply { Ready(Text), Failed }

            async fn home(logger: Logger, clock: Clock) -> self::app::main::Reply effects { clock.read, log.write } {
                let logged: bool = logger.info("clock.web", "reading UTC wall time");
                let observed: i64 = clock.unix_time_ms();
                if observed >= 1700000000000i64 {
                    if observed <= 2000000000000i64 {
                        return self::app::main::Reply.Ready("clock-web-ok");
                    }
                }
                return self::app::main::Reply.Failed;
            }

            route GET "/" {
                handler: self::app::main::home;
                response Ready: 200 json Text;
                response Failed: 500;
            }
            """;
        const string manifest = "name = \"clock-web-root\"\n"
            + "version = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
            + "\n[capabilities]\nnet.listen = \"allow\"\nclock.read = \"allow\"\nlog.write = \"allow\"\n";
        var packageRoot = await harness.WritePackageAsync(
            "clock-web-injection",
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source
            });

        var check = await harness.InvokePackageDirectoryAsync("clock-web-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));

        var reversedSource = source.Replace(
            "logger: Logger, clock: Clock",
            "clock: Clock, logger: Logger",
            StringComparison.Ordinal);
        AssertTrue(reversedSource != source, "The reversed Clock route fixture should change the capability order.");
        var reversedPackage = await harness.WritePackageAsync(
            "clock-web-reversed-capability-order",
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = reversedSource
            });
        var reversed = await harness.InvokePackageDirectoryAsync(
            "clock-web-reversed-capability-order-check", reversedPackage, "check", "--json");
        AssertTrue(reversed.ExitCode != 0, Describe(reversed));
        AssertTrue(ParseDiagnosticSnapshots(reversed.StandardOutput).Any(diagnostic =>
                diagnostic.Code == "E_ROUTE_HANDLER" &&
                diagnostic.Message == "Route handler capability parameters must appear in FsWrite, DbRead, DbWrite, HttpClient, Config, Secrets, Logger, Clock order"),
            $"Route Clock injection should follow Logger. {Describe(reversed)}");

        var apiResult = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, apiResult.ExitCode, Describe(apiResult));
        using (var apiDocument = JsonDocument.Parse(apiResult.StandardOutput))
        {
            var api = apiDocument.RootElement;
            AssertInspectApiPropertyOrder(api);
            AssertEqual(12, api.GetProperty("schema_version").GetInt32(),
                "Web Clock metadata must use inspect API schema version 12.");
            var route = api.GetProperty("routes").EnumerateArray().Single();
            AssertJsonStringArray(route.GetProperty("required_capabilities"), ["clock.read", "log.write"]);
            var parameters = route.GetProperty("capability_parameters").EnumerateArray().ToArray();
            AssertEqual("log.write", parameters[0].GetProperty("capability").GetString(),
                "Web routes should inject Logger before Clock.");
            AssertEqual("clock.read", parameters[1].GetProperty("capability").GetString(),
                "Web routes should inject Clock after Logger.");
        }

        var port = GetUnusedLoopbackPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(4) };
        using var process = harness.StartWebPackageProcess(
            "clock-web-run", packageRoot, "--urls", baseAddress.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        string stdout = string.Empty;
        string stderr = string.Empty;
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);
            using var response = await client.GetAsync("/");
            AssertEqual(HttpStatusCode.OK, response.StatusCode,
                "The web route should execute with its granted Clock capability.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            AssertEqual("clock-web-ok", body.RootElement.GetString(),
                "The web route should return a bounded-time marker without stringifying the timestamp.");
            assertionsCompleted = true;
        }
        finally
        {
            client.Dispose();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: !assertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await process.WaitForExitAsync(termination.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("The Clock web process did not stop within 10 seconds.");
                }
            }

            stdout = await stdoutTask;
            stderr = await stderrTask;
            await AssertLoopbackPortReleasedAsync(port);
        }

        var logLines = stderr.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("\"event\":\"clock.web\"", StringComparison.Ordinal))
            .ToArray();
        AssertTrue(logLines.Length > 0, $"The web route should receive Logger before Clock. stderr=<{stderr}>");
        using var log = JsonDocument.Parse(logLines[^1]);
        AssertJsonPropertyOrder(log.RootElement, "level,event,detail,request_id");
        AssertTrue(!string.IsNullOrWhiteSpace(log.RootElement.GetProperty("request_id").GetString()),
            "The Clock web logger should retain the request identifier after capability injection.");
        _ = stdout;
    }
}
