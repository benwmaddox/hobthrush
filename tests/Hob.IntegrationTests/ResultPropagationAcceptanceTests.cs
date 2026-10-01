using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestResultPropagationAcceptance(Harness harness)
    {
        await TestResultPropagationDiagnostics(harness);
        await TestResultPropagationSqliteRollback(harness);
        await TestResultPropagationCliRuntimeAndReports(harness);
    }

    private static string ResultPropagationCoreSource(int privateValue) => $$"""
        module propagation;

        pub fn forward<T, E>(value: Result<T, E>) -> Result<T, E> effects {} {
            let returned: T = value?;
            return Ok(returned);
        }

        fn private_fact() -> i32 effects {} { return {{privateValue}}; }
        """;

    private static string ResultPropagationHandlerSource => """
        module handlers;

        pub union PropagationError { Tagged(i32) }
        pub struct ResourceCarrier<T> { reader: T }
        pub struct Empty<T> {}
        pub struct OrderedValues { first: i32, middle: i32, last: i32 }

        fn wrap<T, E>(value: Result<T, E>) -> Result<T, E> effects {} {
            let returned: T = core::propagation::forward(value)?;
            return Ok(returned);
        }

        fn manual<T, E>(value: Result<T, E>) -> Result<T, E> effects {} {
            return match value {
                Ok(item) => Ok(item),
                Err(error) => Err(error)
            };
        }

        fn manual_wrap<T, E>(value: Result<T, E>) -> Result<T, E> effects {} {
            let returned: Result<T, E> = self::handlers::manual(value);
            return match returned {
                Ok(item) => Ok(item),
                Err(error) => Err(error)
            };
        }

        fn mark(event: Text, value: i32, logger: Logger) -> i32 effects { log.write } {
            let logged: bool = logger.info(event, "");
            return value;
        }

        fn fail(logger: Logger, event: Text) -> Result<i32, self::handlers::PropagationError> effects { log.write } {
            let logged: bool = logger.info(event, "");
            return Err(self::handlers::PropagationError.Tagged(23));
        }

        fn consume(first: i32, middle: i32, last: i32, logger: Logger) -> i32 effects { log.write } {
            let logged: bool = logger.info("call-enter", "");
            return first + middle + last;
        }

        fn consume_ordered(value: self::handlers::OrderedValues, logger: Logger) -> i32 effects { log.write } {
            let logged: bool = logger.info("struct-enter", "");
            return value.first + value.middle + value.last;
        }

        fn recurse(depth: i32, logger: Logger) -> Result<i32, self::handlers::PropagationError> effects { log.write } {
            if depth == 0 {
                return Err(self::handlers::PropagationError.Tagged(23));
            }
            let child: Result<i32, self::handlers::PropagationError> =
                self::handlers::recurse(depth - 1, logger);
            let logged: bool = logger.info("after-child", "");
            let value: i32 = child?;
            return Ok(value + 1);
        }

        async fn maybe_fail(fail_now: bool) -> Result<i32, self::handlers::PropagationError> effects {} {
            if fail_now {
                return Err(self::handlers::PropagationError.Tagged(23));
            }
            return Ok(42);
        }

        async fn await_cancel(client: HttpClient, logger: Logger, target: Text) -> Result<Text, HttpError> effects { net.client, log.write } {
            let started: bool = logger.info("cancel-start", "");
            let response: HttpResponse = await client.get_text_async(target)?;
            return Ok(response.body);
        }

        fn direct_reader(reader: FsRead) -> Result<i32, FsRead> effects {} {
            let failure: Result<i32, FsRead> = Err(reader);
            return self::handlers::wrap(failure);
        }

        fn direct_reader_manual(reader: FsRead) -> Result<i32, FsRead> effects {} {
            let failure: Result<i32, FsRead> = Err(reader);
            return self::handlers::manual_wrap(failure);
        }

        fn carrier_reader(reader: FsRead) -> Result<i32, self::handlers::ResourceCarrier<FsRead>> effects {} {
            let failure: Result<i32, self::handlers::ResourceCarrier<FsRead>> =
                Err(self::handlers::ResourceCarrier<FsRead> { reader: reader });
            return self::handlers::wrap(failure);
        }

        fn carrier_reader_manual(reader: FsRead) -> Result<i32, self::handlers::ResourceCarrier<FsRead>> effects {} {
            let failure: Result<i32, self::handlers::ResourceCarrier<FsRead>> =
                Err(self::handlers::ResourceCarrier<FsRead> { reader: reader });
            return self::handlers::manual_wrap(failure);
        }

        fn resource_text(reader: FsRead, path: Text) -> Result<Text, self::handlers::PropagationError> effects { fs.read } {
            let loaded: Result<Text, FsError> = reader.read_text(path);
            return match loaded {
                Ok(text) => Ok(text),
                Err(error) => Err(self::handlers::PropagationError.Tagged(77))
            };
        }

        fn effect_leaf(logger: Logger) -> Result<i32, self::handlers::PropagationError> effects { log.write } {
            let logged: bool = logger.info("effect-leaf", "");
            return Ok(1);
        }

        fn effect_chain(logger: Logger) -> Result<Text, self::handlers::PropagationError> effects { log.write } {
            let value: i32 = self::handlers::effect_leaf(logger)?;
            let logged: bool = logger.info("effect-after", "");
            return Ok("effect-complete");
        }

        pub async fn run(
            args: self::app::main::ProbeArgs,
            reader: FsRead,
            client: HttpClient,
            logger: Logger
        ) -> Result<Text, self::handlers::PropagationError> effects { fs.read, net.client, log.write } {
            let phantom: self::handlers::Empty<FsRead> = self::handlers::Empty<FsRead> {};

            if args.mode == "trace-call" {
                let value: i32 = self::handlers::consume(
                    self::handlers::mark("call-first", 1, logger),
                    self::handlers::fail(logger, "call-failing")?,
                    self::handlers::mark("call-last", 3, logger),
                    logger
                );
                if value == 6 { return Ok("call-complete"); }
                return Err(self::handlers::PropagationError.Tagged(90));
            }
            if args.mode == "trace-struct" {
                let value: i32 = self::handlers::consume_ordered(
                    self::handlers::OrderedValues {
                        first: self::handlers::mark("struct-first", 1, logger),
                        middle: self::handlers::fail(logger, "struct-failing")?,
                        last: self::handlers::mark("struct-last", 3, logger)
                    },
                    logger
                );
                if value == 6 { return Ok("struct-complete"); }
                return Err(self::handlers::PropagationError.Tagged(90));
            }
            if args.mode == "trace-binary" {
                let value: i32 = (self::handlers::mark("binary-left", 1, logger)
                    + self::handlers::fail(logger, "binary-failing")?)
                    + self::handlers::mark("binary-right", 3, logger);
                if value == 6 { return Ok("binary-complete"); }
                return Err(self::handlers::PropagationError.Tagged(90));
            }
            if args.mode == "recursive" {
                let returned: Result<i32, self::handlers::PropagationError> =
                    self::handlers::recurse(3, logger);
                let value: i32 = returned?;
                return Ok("recursive-unexpected");
            }
            if args.mode == "generic-ok" {
                let input: Result<i32, self::handlers::PropagationError> = Ok(42);
                let value: i32 = self::handlers::wrap(input)?;
                if value == 42 { return Ok("generic-42"); }
                return Err(self::handlers::PropagationError.Tagged(90));
            }
            if args.mode == "generic-error" {
                let input: Result<i32, self::handlers::PropagationError> =
                    Err(self::handlers::PropagationError.Tagged(23));
                let value: i32 = self::handlers::wrap(input)?;
                return Ok("generic-unexpected");
            }
            if args.mode == "await-prefix-ok" {
                let value: i32 = await self::handlers::maybe_fail(false)?;
                if value == 42 { return Ok("await-42"); }
                return Err(self::handlers::PropagationError.Tagged(90));
            }
            if args.mode == "await-prefix-error" {
                let value: i32 = await self::handlers::maybe_fail(true)?;
                return Ok("await-unexpected");
            }
            if args.mode == "await-grouped-ok" {
                let value: i32 = (await self::handlers::maybe_fail(false))?;
                if value == 42 { return Ok("await-42"); }
                return Err(self::handlers::PropagationError.Tagged(90));
            }
            if args.mode == "await-grouped-error" {
                let value: i32 = (await self::handlers::maybe_fail(true))?;
                return Ok("await-unexpected");
            }
            if args.mode == "resource-forward-direct" {
                let preserved: Result<i32, FsRead> = self::handlers::direct_reader(reader);
                let loaded: Result<Text, self::handlers::PropagationError> = match preserved {
                    Ok(value) => Ok("unexpected"),
                    Err(recovered) => self::handlers::resource_text(recovered, args.path)
                };
                return loaded;
            }
            if args.mode == "resource-manual-direct" {
                let preserved: Result<i32, FsRead> = self::handlers::direct_reader_manual(reader);
                let loaded: Result<Text, self::handlers::PropagationError> = match preserved {
                    Ok(value) => Ok("unexpected"),
                    Err(recovered) => self::handlers::resource_text(recovered, args.path)
                };
                return loaded;
            }
            if args.mode == "resource-forward-carrier" {
                let preserved: Result<i32, self::handlers::ResourceCarrier<FsRead>> =
                    self::handlers::carrier_reader(reader);
                let loaded: Result<Text, self::handlers::PropagationError> = match preserved {
                    Ok(value) => Ok("unexpected"),
                    Err(recovered) => self::handlers::resource_text(recovered.reader, args.path)
                };
                return loaded;
            }
            if args.mode == "resource-manual-carrier" {
                let preserved: Result<i32, self::handlers::ResourceCarrier<FsRead>> =
                    self::handlers::carrier_reader_manual(reader);
                let loaded: Result<Text, self::handlers::PropagationError> = match preserved {
                    Ok(value) => Ok("unexpected"),
                    Err(recovered) => self::handlers::resource_text(recovered.reader, args.path)
                };
                return loaded;
            }
            if args.mode == "resource-success" {
                let successful: Result<FsRead, Text> = Ok(reader);
                let preserved: Result<FsRead, Text> = self::handlers::wrap(successful);
                let loaded: Result<Text, self::handlers::PropagationError> = match preserved {
                    Ok(recovered) => self::handlers::resource_text(recovered, args.path),
                    Err(message) => Err(self::handlers::PropagationError.Tagged(91))
                };
                return loaded;
            }
            if args.mode == "effects" {
                let value: Text = self::handlers::effect_chain(logger)?;
                return Ok(value);
            }
            if args.mode == "cancel" {
                let pending: Result<Text, HttpError> =
                    await self::handlers::await_cancel(client, logger, args.path);
                return match pending {
                    Ok(value) => Ok("cancel-unexpected"),
                    Err(error) => Err(self::handlers::PropagationError.Tagged(88))
                };
            }
            return Err(self::handlers::PropagationError.Tagged(99));
        }

        pub fn describe(error: self::handlers::PropagationError) -> Text effects {} {
            let code: i32 = match error {
                self::handlers::PropagationError.Tagged(value) => value
            };
            if code == 23 { return "tagged-23"; }
            if code == 77 { return "tagged-77"; }
            if code == 88 { return "tagged-88"; }
            if code == 90 { return "tagged-90"; }
            if code == 91 { return "tagged-91"; }
            return "tagged-other";
        }
        """;

    private static string ResultPropagationCliMainSource => """
        module app::main;
        command probe {
            help "Exercise typed Result propagation.";
            argument mode: Text help "Select a propagation probe.";
            argument path: Text help "File used by resource preservation probes.";
            handler: self::handlers::run;
            error: self::handlers::describe;
        }
        """;

    private static async Task TestResultPropagationCliRuntimeAndReports(Harness harness)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64
            || (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("Result propagation NativeAOT acceptance requires Windows x64 or Linux x64.");

        await using var cancellationServer = new RawHttpServer();
        const string cliManifestPrefix = "name = \"result-propagation-cli\"\n"
            + "version = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n";
        var manifest = cliManifestPrefix
            + $"http_origin = \"{cancellationServer.Origin}\"\n"
            + "[capabilities]\nfs.read = \"allow\"\nnet.client = \"allow\"\nlog.write = \"allow\"\n"
            + "[dependencies]\ncore = \"../core\"\n";
        var packageRoot = await harness.WritePackageGraphAsync(
            "result-propagation-runtime",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(manifest,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = ResultPropagationCliMainSource,
                        ["src/handlers.hob"] = ResultPropagationHandlerSource
                    }),
                ["core"] = new PackageFixture(
                    "name = \"result-propagation-core\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/propagation.hob"] = ResultPropagationCoreSource(1)
                    })
            });
        var lockResult = await harness.InvokePackageDirectoryAsync(
            "result-propagation-runtime-lock", packageRoot, "lock");
        AssertLockCommandSucceeded(lockResult);
        var check = await harness.InvokePackageDirectoryAsync(
            "result-propagation-runtime-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var apiRun = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, apiRun.ExitCode, Describe(apiRun));
        using (var api = JsonDocument.Parse(apiRun.StandardOutput))
        {
            AssertInspectApiPropertyOrder(api.RootElement);
            AssertEqual(11, api.RootElement.GetProperty("schema_version").GetInt32(),
                "Postfix Result propagation must retain inspect API schema 11.");
            var forward = api.RootElement.GetProperty("functions").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == "core::propagation::forward");
            var result = forward.GetProperty("return_type");
            AssertEqual("result", result.GetProperty("kind").GetString(),
                "The public generic forward API must keep its existing Result type shape.");
            AssertEqual("T", result.GetProperty("ok").GetProperty("name").GetString(),
                "The generic forward success type must remain T.");
            AssertEqual("E", result.GetProperty("error").GetProperty("name").GetString(),
                "The generic forward error type must remain E.");
        }
        var repeatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(apiRun.StandardOutput, repeatedApi.StandardOutput,
            "Repeated Result propagation API output must be byte-identical.");

        var effects = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::handlers::effect_chain", "--json");
        AssertEqual(0, effects.ExitCode, Describe(effects));
        using (var report = JsonDocument.Parse(effects.StandardOutput))
        {
            AssertEqual(1, report.RootElement.GetProperty("schema_version").GetInt32(),
                "Propagation effects must retain effects schema 1.");
            AssertJsonStringArray(report.RootElement.GetProperty("inferred_effects"), ["log.write"]);
            AssertJsonStringArray(report.RootElement.GetProperty("required_capabilities"), ["log.write"]);
            var logPath = report.RootElement.GetProperty("effect_paths").EnumerateArray()
                .Single(path => path.GetProperty("effect").GetString() == "log.write");
            var logSteps = string.Join(" -> ", logPath.GetProperty("steps").EnumerateArray()
                .Select(step => step.GetString() ?? string.Empty));
            AssertEqual("handlers::effect_chain -> Logger.info", logSteps,
                "Propagation should preserve the established shortest path to Logger.info.");
        }
        var repeatedEffects = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::handlers::effect_chain", "--json");
        AssertEqual(effects.StandardOutput, repeatedEffects.StandardOutput,
            "Repeated propagation effects output must be byte-identical.");

        var auditRun = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
        using (var audit = JsonDocument.Parse(auditRun.StandardOutput))
        {
            AssertAuditPropertyOrder(audit.RootElement);
            AssertEqual(9, audit.RootElement.GetProperty("schema_version").GetInt32(),
                "Typed Result control flow must retain audit schema 9.");
            var compiler = audit.RootElement.GetProperty("compiler");
            var effectChain = compiler.GetProperty("functions").EnumerateArray()
                .Single(item => item.GetProperty("module").GetString() == "handlers"
                    && item.GetProperty("name").GetString() == "effect_chain");
            var forwardedCalls = effectChain.GetProperty("direct_calls").EnumerateArray()
                .Where(item => item.GetProperty("module").GetString() == "handlers"
                    && item.GetProperty("name").GetString() == "effect_leaf").ToArray();
            AssertEqual(1, forwardedCalls.Length,
                "The call wrapped by postfix propagation must be recorded exactly once in audit facts.");
            var coreForward = compiler.GetProperty("functions").EnumerateArray()
                .Single(item => item.GetProperty("module").GetString() == "propagation"
                    && item.GetProperty("name").GetString() == "forward");
            var typeParameters = coreForward.GetProperty("type_parameters").EnumerateArray().ToArray();
            AssertEqual(2, typeParameters.Length,
                "Audit facts must retain the generic forward function's T and E parameters.");
            AssertEqual("T", typeParameters[0].GetProperty("name").GetString(),
                "The first generic audit parameter must remain T.");
            AssertEqual("E", typeParameters[1].GetProperty("name").GetString(),
                "The second generic audit parameter must remain E.");
        }
        var repeatedAudit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(auditRun.StandardOutput, repeatedAudit.StandardOutput,
            "Repeated propagation audit output must be byte-identical.");

        var metadataReference = await CreateResultPropagationMetadataGraphAsync(
            harness, "result-propagation-metadata-reference", ResultPropagationCoreSource(1));
        var metadataRelocated = await CreateResultPropagationMetadataGraphAsync(
            harness, "result-propagation-relocated", ResultPropagationCoreSource(1), lineEnding: "\r\n");
        var referenceApi = await harness.InvokeCompilerCommandAsync("inspect", "api", metadataReference, "--json");
        var referenceAudit = await harness.InvokeCompilerCommandAsync("audit", metadataReference, "--json");
        var relocatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", metadataRelocated, "--json");
        var relocatedAudit = await harness.InvokeCompilerCommandAsync("audit", metadataRelocated, "--json");
        AssertEqual(0, referenceApi.ExitCode, Describe(referenceApi));
        AssertEqual(0, referenceAudit.ExitCode, Describe(referenceAudit));
        AssertEqual(0, relocatedApi.ExitCode, Describe(relocatedApi));
        AssertEqual(0, relocatedAudit.ExitCode, Describe(relocatedAudit));
        AssertEqual(referenceApi.StandardOutput, relocatedApi.StandardOutput,
            "Result propagation API IDs and shapes must be independent of relocation and CRLF input.");
        AssertEqual(referenceAudit.StandardOutput, relocatedAudit.StandardOutput,
            "Result propagation audit facts must be independent of relocation and CRLF input.");

        var coreSourcePath = Path.GetFullPath(Path.Combine(packageRoot, "..", "core", "src", "propagation.hob"));
        var coreSource = await File.ReadAllTextAsync(coreSourcePath);
        await File.WriteAllTextAsync(coreSourcePath,
            coreSource.Replace("return 1;", "return 2;", StringComparison.Ordinal), new UTF8Encoding(false));
        var staleLock = await harness.InvokePackageDirectoryAsync(
            "result-propagation-stale-lock", packageRoot, "check", "--json");
        AssertTrue(staleLock.ExitCode != 0, "A propagated generic dependency body edit must stale the path lock.");
        AssertEqual("E_LOCK", ParseDiagnosticSnapshots(staleLock.StandardOutput).Single().Code,
            "A changed propagation dependency must use the existing E_LOCK contract.");
        AssertNoCompilerArtifacts(packageRoot);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "result-propagation-refresh-lock", packageRoot, "lock"));
        var refreshedCheck = await harness.InvokePackageDirectoryAsync(
            "result-propagation-refreshed-check", packageRoot, "check", "--json");
        AssertEqual(0, refreshedCheck.ExitCode, Describe(refreshedCheck));
        var editedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(apiRun.StandardOutput, editedApi.StandardOutput,
            "A dependency implementation-only edit must leave the public Result API unchanged.");

        var managedBuild = await harness.InvokePackageDirectoryAsync(
            "result-propagation-managed-build", packageRoot, "build");
        var managedArtifact = ParseBuiltArtifact(managedBuild, "Built executable: ");
        var managedDirectory = Path.GetDirectoryName(managedArtifact)!;
        using (var receipt = await AssertBuildReceiptAsync(
                   managedDirectory, "managed", null,
                   [Path.GetRelativePath(managedDirectory, managedArtifact).Replace(Path.DirectorySeparatorChar, '/'),
                    "command-schema.json"], packageRoot))
            AssertEqual(3, receipt.RootElement.GetProperty("schema_version").GetInt32(),
                "Managed propagation builds must retain receipt schema 3.");

        var resourcePath = Path.Combine(harness.TemporaryRoot, "result-propagation-resource.txt");
        const string resourcePayload = "preserved reader λ";
        await File.WriteAllTextAsync(resourcePath, resourcePayload, new UTF8Encoding(false, true));
        var modes = new (string Mode, int ExitCode, string Output, string[] Events, string? Error)[]
        {
            ("trace-call", 3, string.Empty, ["call-first", "call-failing"], "tagged-23"),
            ("trace-struct", 3, string.Empty, ["struct-first", "struct-failing"], "tagged-23"),
            ("trace-binary", 3, string.Empty, ["binary-left", "binary-failing"], "tagged-23"),
            ("recursive", 3, string.Empty, ["after-child", "after-child", "after-child"], "tagged-23"),
            ("generic-ok", 0, "generic-42", [], null),
            ("generic-error", 3, string.Empty, [], "tagged-23"),
            ("await-prefix-ok", 0, "await-42", [], null),
            ("await-prefix-error", 3, string.Empty, [], "tagged-23"),
            ("await-grouped-ok", 0, "await-42", [], null),
            ("await-grouped-error", 3, string.Empty, [], "tagged-23"),
            ("effects", 0, "effect-complete", ["effect-leaf", "effect-after"], null),
            ("resource-forward-direct", 0, resourcePayload, [], null),
            ("resource-manual-direct", 0, resourcePayload, [], null),
            ("resource-forward-carrier", 0, resourcePayload, [], null),
            ("resource-manual-carrier", 0, resourcePayload, [], null),
            ("resource-success", 0, resourcePayload, [], null)
        };

        foreach (var (mode, exitCode, output, events, error) in modes)
        {
            var managed = await harness.RunManagedArtifactAsync(
                managedArtifact, packageRoot, "probe", mode, resourcePath);
            AssertPropagationCliResult(managed, exitCode, output, events, error, $"managed {mode}");
        }

        var nativeBuild = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "result-propagation-native-build", packageRoot, "build", AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        var nativeArtifact = ParseBuiltArtifact(nativeBuild, "Built native executable: ");
        var nativeDirectory = Path.GetDirectoryName(nativeArtifact)!;
        var managedSchema = await File.ReadAllBytesAsync(Path.Combine(managedDirectory, "command-schema.json"));
        var nativeSchema = await File.ReadAllBytesAsync(Path.Combine(nativeDirectory, "command-schema.json"));
        AssertTrue(managedSchema.SequenceEqual(nativeSchema),
            "Managed and NativeAOT propagation command schemas must be byte-identical.");
        using (var receipt = await AssertBuildReceiptAsync(
                   nativeDirectory, "native_aot", CurrentHostAotRid(),
                   [Path.GetRelativePath(nativeDirectory, nativeArtifact).Replace(Path.DirectorySeparatorChar, '/'),
                    "command-schema.json"], packageRoot))
            AssertEqual(3, receipt.RootElement.GetProperty("schema_version").GetInt32(),
                "NativeAOT propagation builds must retain receipt schema 3.");

        foreach (var (mode, exitCode, output, events, error) in modes)
        {
            var managed = await harness.RunManagedArtifactAsync(
                managedArtifact, packageRoot, "probe", mode, resourcePath);
            var native = await ExecuteNativeAsync(
                nativeArtifact, TimeSpan.FromSeconds(30), "probe", mode, resourcePath);
            AssertPropagationCliResult(native, exitCode, output, events, error, $"NativeAOT {mode}");
            AssertEqual(managed.StandardOutput, native.StandardOutput,
                $"Managed and NativeAOT stdout differ for {mode}.");
            AssertEqual(managed.StandardError, native.StandardError,
                $"Managed and NativeAOT logger/error output differs for {mode}.");
        }

        if (OperatingSystem.IsLinux())
        {
            await AssertPropagationCancellationAsync(
                harness, cancellationServer, managedArtifact, packageRoot, "/hold-cancel-managed", nativeArtifact, native: false);
            await AssertPropagationCancellationAsync(
                harness, cancellationServer, managedArtifact, packageRoot, "/hold-cancel-native", nativeArtifact, native: true);
        }
    }

    private static async Task<string> CreateResultPropagationMetadataGraphAsync(
        Harness harness,
        string caseName,
        string coreSource,
        string? lineEnding = null)
    {
        const string manifest = "name = \"result-propagation-cli\"\n"
            + "version = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
            + "http_origin = \"http://127.0.0.1:48327\"\n"
            + "[capabilities]\nfs.read = \"allow\"\nnet.client = \"allow\"\nlog.write = \"allow\"\n"
            + "[dependencies]\ncore = \"../core\"\n";
        var root = await harness.WritePackageGraphAsync(
            caseName,
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(manifest,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = ResultPropagationCliMainSource,
                        ["src/handlers.hob"] = ResultPropagationHandlerSource
                    }),
                ["core"] = new PackageFixture(
                    "name = \"result-propagation-core\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/propagation.hob"] = coreSource
                    })
            });
        if (lineEnding is not null)
        {
            var workspace = Path.GetDirectoryName(root)!;
            foreach (var path in Directory.EnumerateFiles(workspace, "*.hob", SearchOption.AllDirectories))
            {
                var source = await File.ReadAllTextAsync(path);
                var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                await File.WriteAllTextAsync(path,
                    normalized.Replace("\n", lineEnding, StringComparison.Ordinal), new UTF8Encoding(false));
            }
        }
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            caseName + "-lock", root, "lock"));
        return root;
    }

    private static void AssertPropagationCliResult(
        ProcessResult result,
        int exitCode,
        string standardOutput,
        string[] expectedEvents,
        string? formattedError,
        string context)
    {
        AssertEqual(exitCode, result.ExitCode, $"{context}: unexpected exit. {Describe(result)}");
        AssertEqual(standardOutput + (standardOutput.Length == 0 ? string.Empty : Environment.NewLine),
            result.StandardOutput, $"{context}: unexpected stdout. {Describe(result)}");
        var lines = result.StandardError.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var jsonLines = lines.Where(line => line.StartsWith("{", StringComparison.Ordinal)).ToArray();
        AssertEqual(expectedEvents.Length, jsonLines.Length,
            $"{context}: expected exactly {expectedEvents.Length} logger lines. {Describe(result)}");
        for (var index = 0; index < expectedEvents.Length; index++)
        {
            using var item = JsonDocument.Parse(jsonLines[index]);
            AssertJsonPropertyOrder(item.RootElement, "level,event,detail");
            AssertEqual("info", item.RootElement.GetProperty("level").GetString(), context);
            AssertEqual(expectedEvents[index], item.RootElement.GetProperty("event").GetString(),
                $"{context}: logger order mismatch at event {index}.");
            AssertEqual(string.Empty, item.RootElement.GetProperty("detail").GetString(), context);
        }

        if (formattedError is not null)
        {
            AssertEqual(expectedEvents.Length + 1, lines.Length,
                $"{context}: expected one typed CLI error after the logger events. {Describe(result)}");
            AssertEqual(formattedError, lines[^1], $"{context}: wrong error payload reached the formatter.");
        }
        else
        {
            AssertEqual(expectedEvents.Length, lines.Length,
                $"{context}: unexpected stderr after structured events. {Describe(result)}");
        }
    }

    private static async Task AssertPropagationCancellationAsync(
        Harness harness,
        RawHttpServer server,
        string managedArtifact,
        string packageRoot,
        string cancelTarget,
        string nativeArtifact,
        bool native)
    {
        Process process;
        if (native)
        {
            var start = new ProcessStartInfo
            {
                FileName = nativeArtifact,
                WorkingDirectory = packageRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "probe", "cancel", cancelTarget })
                start.ArgumentList.Add(argument);
            process = new Process { StartInfo = start };
        }
        else
        {
            process = harness.StartManagedArtifactProcess(
                managedArtifact, packageRoot, null, "probe", "cancel", cancelTarget);
        }

        using (process)
        {
            var started = !native;
            Task<string>? stdoutTask = null;
            Task<string>? stderrTask = null;
            var validationFailed = false;
            try
            {
                if (native)
                {
                    started = process.Start();
                    AssertTrue(started, "Could not start the NativeAOT propagation cancellation probe.");
                }

                stdoutTask = process.StandardOutput.ReadToEndAsync();
                stderrTask = process.StandardError.ReadToEndAsync();
                await server.WaitForRequestAsync(cancelTarget).WaitAsync(TimeSpan.FromSeconds(8));
                AssertEqual(0, KillWithSignal(process.Id, 2),
                    $"Could not send SIGINT to the {(native ? "NativeAOT" : "managed")} `?` cancellation probe.");
                using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(exitTimeout.Token);
                await server.WaitForClientDisconnectAsync(cancelTarget).WaitAsync(TimeSpan.FromSeconds(8));
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                AssertEqual(130, process.ExitCode,
                    $"Host cancellation must remain cancellation instead of becoming a typed Err. stdout=<{stdout}> stderr=<{stderr}>");
                AssertEqual(string.Empty, stdout,
                    "A canceled awaited operation must not produce partial command output.");
                var lines = stderr.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
                AssertEqual(1, lines.Length,
                    $"Cancellation should leave only the pre-await logger event; no typed formatter or runtime-fault line is allowed. stderr=<{stderr}>");
                using var log = JsonDocument.Parse(lines[0]);
                AssertEqual("cancel-start", log.RootElement.GetProperty("event").GetString(),
                    "The cancellation probe must enter the awaited call after emitting its marker.");
                AssertTrue(!stderr.Contains("tagged-", StringComparison.Ordinal)
                    && !stderr.Contains("Runtime fault", StringComparison.Ordinal)
                    && !stderr.Contains(" at ", StringComparison.Ordinal),
                    "Host cancellation must not become a typed error, generic fault, or stack trace.");
            }
            catch
            {
                validationFailed = true;
                throw;
            }
            finally
            {
                if (started)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            try
                            {
                                process.Kill(entireProcessTree: true);
                            }
                            catch (InvalidOperationException) when (process.HasExited)
                            {
                            }

                            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            await process.WaitForExitAsync(cleanupTimeout.Token);
                        }

                        if (stdoutTask is not null && stderrTask is not null)
                            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    catch when (validationFailed)
                    {
                        // Cleanup stays bounded and best-effort without replacing the original assertion or timeout.
                    }
                }
            }
        }
    }

    private static async Task TestResultPropagationSqliteRollback(Harness harness)
    {
        const string manifest = "name = \"result-propagation-sqlite\"\nversion = \"0.1.0\"\n"
            + "kind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
            + "sqlite_path = \"data/propagation.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n\n"
            + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n";
        const string source = """
            module app::main;

            struct Parameters { id: i32, value: Text }
            struct EmptyParameters {}
            struct CountRow { total: i32 }
            pub union Reply { Count(i32), Recovered, Failed, Unexpected }

            fn rollback_on_error(writer: DbWrite) -> Result<bool, DbError> effects { db.write } {
                with writer.begin() as tx {
                    let first: i32 = tx.execute(
                        "INSERT INTO records (id, value) VALUES ($id, $value)",
                        self::app::main::Parameters { id: 1, value: "first" }
                    )?;
                    let duplicate: i32 = tx.execute(
                        "INSERT INTO records (id, value) VALUES ($id, $value)",
                        self::app::main::Parameters { id: 1, value: "duplicate" }
                    )?;
                    let committed: bool = tx.commit()?;
                    return Ok(committed);
                }
            }

            fn read_count(reader: DbRead) -> Result<i32, DbError> effects { db.read } {
                let loaded: Result<Option<self::app::main::CountRow>, DbError> = reader.query_one(
                    "SELECT COUNT(*) AS total FROM records",
                    self::app::main::EmptyParameters {}
                );
                let row: Option<self::app::main::CountRow> = loaded?;
                return match row {
                    Some(value) => Ok(value.total),
                    None => Ok(-1)
                };
            }

            fn insert_after_unwind(writer: DbWrite) -> Result<bool, DbError> effects { db.write } {
                with writer.begin() as tx {
                    let inserted: i32 = tx.execute(
                        "INSERT INTO records (id, value) VALUES ($id, $value)",
                        self::app::main::Parameters { id: 2, value: "after unwind" }
                    )?;
                    let committed: bool = tx.commit()?;
                    return Ok(committed);
                }
            }

                fn verify_reacquired(reader: DbRead, writer: DbWrite) -> self::app::main::Reply effects { db.read, db.write } {
                let before: Result<i32, DbError> = self::app::main::read_count(reader);
                let initial: i32 = match before {
                    Ok(value) => value,
                    Err(error) => -1
                };
                if initial != 0 { return self::app::main::Reply.Failed; }

                let inserted: Result<bool, DbError> = self::app::main::insert_after_unwind(writer);
                let committed: bool = match inserted {
                    Ok(value) => value,
                    Err(error) => false
                };
                if committed == false { return self::app::main::Reply.Failed; }

                let after: Result<i32, DbError> = self::app::main::read_count(reader);
                let final_count: i32 = match after {
                    Ok(value) => value,
                    Err(error) => -1
                };
                if final_count == 1 { return self::app::main::Reply.Recovered; }
                return self::app::main::Reply.Failed;
            }

            fn propagate(db: DbRead, writer: DbWrite) -> self::app::main::Reply effects { db.read, db.write } {
                let attempt: Result<bool, DbError> = self::app::main::rollback_on_error(writer);
                return match attempt {
                    Ok(committed) => self::app::main::Reply.Unexpected,
                    Err(error) => match error {
                        DbError.Statement => self::app::main::verify_reacquired(db, writer),
                        DbError.RowShape => self::app::main::Reply.Failed
                    }
                };
            }

            fn state(reader: DbRead) -> self::app::main::Reply effects { db.read } {
                let count: Result<i32, DbError> = self::app::main::read_count(reader);
                return match count {
                    Ok(value) => self::app::main::Reply.Count(value),
                    Err(error) => self::app::main::Reply.Failed
                };
            }

            route GET "/" {
                handler: self::app::main::state;
                response Count: 200 json i32;
                response Failed: 500;
                response Recovered: 500;
                response Unexpected: 500;
            }
            route GET "/propagate" {
                handler: self::app::main::propagate;
                response Recovered: 200;
                response Failed: 500;
                response Unexpected: 409;
                response Count: 500 json i32;
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "result-propagation-sqlite",
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source,
                ["db/schema.sql"] = "CREATE TABLE IF NOT EXISTS records (id INTEGER PRIMARY KEY, value TEXT NOT NULL);\n"
            });
        var check = await harness.InvokePackageDirectoryAsync(
            "result-propagation-sqlite-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var unsupportedAot = await harness.InvokePackageDirectoryAsync(
            "result-propagation-sqlite-aot-contract", packageRoot, "build", "--aot", "--rid", CurrentHostAotRid());
        AssertTrue(unsupportedAot.ExitCode != 0, "The current web package contract must reject NativeAOT before emission.");
        AssertTrue((unsupportedAot.StandardOutput + unsupportedAot.StandardError).Contains("E_BUILD_TARGET", StringComparison.Ordinal),
            $"SQLite web AOT is outside the supported deployment target; the established diagnostic must remain explicit. {Describe(unsupportedAot)}");
        AssertNoCompilerArtifacts(packageRoot);

        var port = GetUnusedLoopbackPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(5) };
        var databasePath = Path.Combine(harness.TemporaryRoot, "result-propagation-managed.sqlite3");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["HOB_SQLITE_PATH"] = databasePath };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "result-propagation-sqlite-managed", packageRoot, environment, "--urls", address.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);
            AssertEqual(0, await ReadSqliteCountAsync(client), "The propagation transaction must start from an empty database.");
            using var response = await client.GetAsync("/propagate");
            AssertEqual(HttpStatusCode.OK, response.StatusCode,
                "The duplicate insert's typed DbError must reach the wrapper after rolling back the first insert.");
            AssertEqual(1, await ReadSqliteCountAsync(client),
                "Propagation must roll back the first transaction, release its connection, and allow a later transaction to commit.");
            assertionsCompleted = true;
        }
        finally
        {
            client.Dispose();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: !assertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(termination.Token); }
                catch (OperationCanceledException) { throw new TimeoutException("The managed propagation SQLite host did not stop."); }
            }
            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
        }

        async Task<int> ReadSqliteCountAsync(HttpClient http)
        {
            using var response = await http.GetAsync("/");
            AssertEqual(HttpStatusCode.OK, response.StatusCode, "The state route should return the current row count.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetInt32();
        }
    }

    private static async Task TestResultPropagationDiagnostics(Harness harness)
    {
        var cases = new (string Name, string Source, string Code, string Message, string Marker, string Token)[]
        {
            (
                "option operand",
                "module harness::result_option_operand;\n"
                    + "fn invalid(value: Option<i32>) -> Result<i32, Text> effects {} {\n"
                    + "    let item: i32 = value?;\n"
                    + "    return Ok(item);\n"
                    + "}\n",
                "E_TYPE_MISMATCH",
                "The '?' operator requires a Result<T, E> operand, found 'Option<i32>'",
                "value?",
                "?"),
            (
                "non-Result operand",
                "module harness::result_non_result_operand;\n"
                    + "fn invalid(value: i32) -> Result<i32, Text> effects {} {\n"
                    + "    let item: i32 = value?;\n"
                    + "    return Ok(item);\n"
                    + "}\n",
                "E_TYPE_MISMATCH",
                "The '?' operator requires a Result<T, E> operand, found 'i32'",
                "value?",
                "?"),
            (
                "non-Result return context",
                "module harness::result_non_result_return;\n"
                    + "fn invalid(value: Result<i32, Text>) -> i32 effects {} {\n"
                    + "    let item: i32 = value?;\n"
                    + "    return item;\n"
                    + "}\n",
                "E_TYPE_MISMATCH",
                "The '?' operator requires the current function to return Result<T, E>",
                "value?",
                "?"),
            (
                "mismatched error type",
                "module harness::result_mismatched_error;\n"
                    + "fn invalid(value: Result<i32, bool>) -> Result<i32, Text> effects {} {\n"
                    + "    let item: i32 = value?;\n"
                    + "    return Ok(item);\n"
                    + "}\n",
                "E_TYPE_MISMATCH",
                "Expected 'Text', found 'bool'",
                "value?",
                "?"),
            (
                "lambda context",
                "module harness::result_lambda_context;\n"
                    + "fn invalid(value: Result<i32, Text>) -> Result<i32, Text> effects {} {\n"
                    + "    let item: i32 = (lambda(input: Result<i32, Text>) => input?)(value);\n"
                    + "    return Ok(item);\n"
                    + "}\n",
                "E_RESULT_PROPAGATION_CONTEXT",
                "The '?' operator is not allowed in lambda or test bodies",
                "input?",
                "?"),
            (
                "test context",
                "module harness::result_test_context;\n"
                    + "test \"propagation is not available in a test body\" {\n"
                    + "    let value: Result<i32, Text> = Ok(1);\n"
                    + "    assert value? == 1;\n"
                    + "}\n",
                "E_RESULT_PROPAGATION_CONTEXT",
                "The '?' operator is not allowed in lambda or test bodies",
                "value?",
                "?")
        };

        foreach (var (name, source, code, message, marker, token) in cases)
        {
            await AssertBytesStandaloneDiagnosticAsync(
                harness,
                "result-propagation-" + name.Replace(' ', '-'),
                source,
                code,
                message,
                marker,
                token);
        }
    }
}
