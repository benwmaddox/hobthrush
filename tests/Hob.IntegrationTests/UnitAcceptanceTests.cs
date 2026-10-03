using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestUnitAcceptance(Harness harness)
    {
        await TestUnitRuntimeAndStorage(harness);
        await TestUnitDiagnosticsAndCodecs(harness);
        await TestUnitMetadataAndReceipt(harness);
        await TestUnitEvaluationOrder(harness);
    }

    private static async Task TestUnitRuntimeAndStorage(Harness harness)
    {
        const string source = """
            module harness::unit_values;

            pub struct Unit { marker: i32 }
            pub struct Holder { value: Unit }
            pub struct UserHolder { value: self::harness::unit_values::Unit }
            pub struct Box<T> { value: T }
            pub union UnitCarrier { Present(Unit), Empty }
            pub union UserCarrier { Present(self::harness::unit_values::Unit), Empty }
            pub union GenericCarrier<T> { Present(T), Empty }

            fn empty() -> Unit effects {} { return (); }
            fn pass(value: Unit) -> Unit effects {} { return value; }

            pub fn main() -> i32 effects {} {
                let first: Unit = ();
                let second: Unit = self::harness::unit_values::empty();
                let passed: Unit = self::harness::unit_values::pass(());
                let nested: Unit = ((()));
                let grouped: i32 = (1 + 2);
                let nested_grouped: i32 = ((1 + 2));
                if first == second { } else { return 1; }
                if first == passed { } else { return 2; }
                if first == nested { } else { return 3; }
                if first != second { return 4; }

                let units_left: List<Unit> = [(), first];
                let units_right: List<Unit> = [second, passed];
                if units_left == units_right { } else { return 5; }
                let optional_left: Option<Unit> = Some(());
                let optional_right: Option<Unit> = Some(passed);
                if optional_left == optional_right { } else { return 6; }
                let result_left: Result<Unit, Text> = Ok(());
                let result_right: Result<Unit, Text> = Ok(second);
                if result_left == result_right { } else { return 7; }

                let box_left: self::harness::unit_values::Box<Unit> =
                    self::harness::unit_values::Box<Unit> { value: () };
                let box_right: self::harness::unit_values::Box<Unit> =
                    self::harness::unit_values::Box<Unit> { value: passed };
                if box_left == box_right { } else { return 8; }
                let holder_left: self::harness::unit_values::Holder =
                    self::harness::unit_values::Holder { value: first };
                let holder_right: self::harness::unit_values::Holder =
                    self::harness::unit_values::Holder { value: second };
                if holder_left == holder_right { } else { return 9; }
                let carrier_left: self::harness::unit_values::UnitCarrier =
                    self::harness::unit_values::UnitCarrier.Present(first);
                let carrier_right: self::harness::unit_values::UnitCarrier =
                    self::harness::unit_values::UnitCarrier.Present(second);
                if carrier_left == carrier_right { } else { return 10; }
                let generic_left: self::harness::unit_values::GenericCarrier<Unit> =
                    self::harness::unit_values::GenericCarrier<Unit>.Present(first);
                let generic_right: self::harness::unit_values::GenericCarrier<Unit> =
                    self::harness::unit_values::GenericCarrier<Unit>.Present(second);
                if generic_left == generic_right { } else { return 11; }

                let user_left: self::harness::unit_values::Unit =
                    self::harness::unit_values::Unit { marker: 41 };
                let user_right: self::harness::unit_values::Unit =
                    self::harness::unit_values::Unit { marker: 41 };
                if user_left == user_right { } else { return 12; }
                let user_holder_left: self::harness::unit_values::UserHolder =
                    self::harness::unit_values::UserHolder { value: user_left };
                let user_holder_right: self::harness::unit_values::UserHolder =
                    self::harness::unit_values::UserHolder { value: user_right };
                if user_holder_left == user_holder_right { } else { return 13; }
                let user_carrier_left: self::harness::unit_values::UserCarrier =
                    self::harness::unit_values::UserCarrier.Present(user_holder_left.value);
                let user_carrier_right: self::harness::unit_values::UserCarrier =
                    self::harness::unit_values::UserCarrier.Present(user_holder_right.value);
                if user_carrier_left == user_carrier_right { } else { return 14; }

                return grouped + nested_grouped + user_left.marker;
            }
            """;

        var check = await harness.InvokeAsync("unit-values-check", "check", source, "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));
        var run = await harness.InvokeAsync("unit-values-run", "run", source);
        AssertRunOutput("47" + Environment.NewLine, run);
    }

    private static async Task TestUnitDiagnosticsAndCodecs(Harness harness)
    {
        const string webManifest = "name = \"unit-web-boundary\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n\n[capabilities]\nnet.listen = \"allow\"\n";

        const string unitMain = """
            module app::main;
            pub fn main() -> Unit effects {} { return (); }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "unit-main-package-entrypoint", CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = unitMain
            },
            "src/app/main.hob", "E_ENTRYPOINT",
            "Entry module 'app::main' must declare a zero-argument main returning i32, bool, or Text",
            "module app::main;", "app");
        var unitMainRun = await harness.InvokeAsync("unit-main-entrypoint", "run", unitMain);
        AssertTrue(unitMainRun.ExitCode != 0, $"A Unit-returning main unexpectedly ran. {Describe(unitMainRun)}");
        AssertEqual(string.Empty, unitMainRun.StandardOutput, Describe(unitMainRun));
        var unitMainSourcePath = Path.GetFullPath(harness.LastSourcePath);
        AssertEqual(
            $"{unitMainSourcePath}:1:1: E_ENTRYPOINT: hob run requires fn main() -> i32, bool, or Text with no parameters{Environment.NewLine}",
            unitMainRun.StandardError,
            "A Unit-returning main should be rejected by the existing run entrypoint contract at the source start.");
        AssertNoCompilerArtifacts(Path.GetDirectoryName(unitMainSourcePath)!);

        const string unitCommand = """
            module app::main;
            command inspect {
                help "Inspect a Unit input.";
                argument value: Unit help "Unit input.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string commandHandler = """
            module handlers;
            pub union InspectError { Failed }
            pub fn run(args: self::app::main::InspectArgs) -> Result<Text, self::handlers::InspectError> effects {} {
                return Ok("unused");
            }
            pub fn describe(error: self::handlers::InspectError) -> Text effects {} {
                return match error { self::handlers::InspectError.Failed => "failed" };
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "unit-command-codec", CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = unitCommand,
                ["src/handlers.hob"] = commandHandler
            },
            "src/app/main.hob", "E_COMMAND_DECL",
            "Command input type 'Unit' is not supported; use FilePath, Text, or i32",
            "argument value: Unit", "argument");

        const string routePath = """
            module app::main;
            pub union Reply { Found(Text), Empty }
            fn get(value: Unit) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Found("unused");
            }
            route GET "/items/{value}" {
                path value: Unit;
                handler: self::app::main::get;
                response Found: 200 json Text;
                response Empty: 404;
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "unit-route-path-codec", webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = routePath },
            "src/app/main.hob", "E_ROUTE_BINDING", "Path bindings support only Text and i32",
            "path value: Unit", "Unit");

        const string routeQuery = """
            module app::main;
            pub union Reply { Found(Text), Empty }
            fn get(value: Unit) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Found("unused");
            }
            route GET "/items" {
                query value: Unit;
                handler: self::app::main::get;
                response Found: 200 json Text;
                response Empty: 404;
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "unit-route-query-codec", webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = routeQuery },
            "src/app/main.hob", "E_ROUTE_BINDING",
            "Query bindings support only Text, i32, Option<Text>, and Option<i32>",
            "query value: Unit", "Unit");

        const string routeBody = """
            module app::main;
            pub struct Request { value: Unit }
            pub union Reply { Accepted(Text), Empty }
            fn post(request: self::app::main::Request) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Accepted("ok");
            }
            route POST "/items" {
                body: self::app::main::Request;
                handler: self::app::main::post;
                response Accepted: 200 json Text;
                response Empty: 204;
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "unit-route-body-codec", webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = routeBody },
            "src/app/main.hob", "E_ROUTE_CODEC_UNSUPPORTED",
            "POST route body structs must contain only acyclic i32, bool, Text, and supported struct fields",
            "body: self::app::main::Request", "self");

        const string routeResponse = """
            module app::main;
            pub struct Payload { value: Unit }
            pub union Reply { Found(self::app::main::Payload), Empty }
            fn get() -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Found(self::app::main::Payload { value: () });
            }
            route GET "/items" {
                handler: self::app::main::get;
                response Found: 200 json self::app::main::Payload;
                response Empty: 404;
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "unit-route-response-codec", webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = routeResponse },
            "src/app/main.hob", "E_ROUTE_CODEC_UNSUPPORTED",
            "JSON response type must exactly match a payload with a supported JSON shape",
            "response Found: 200 json self::app::main::Payload", "self");

        const string sqliteManifest = "name = \"unit-sqlite-boundary\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\nsqlite_path = \"data/unit.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n";
        const string sqliteRow = """
            module app::main;
            pub struct Parameters { id: i32 }
            pub struct Row { value: Unit }
            pub union Reply { Ready }
            fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one("SELECT value FROM records", self::app::main::Parameters { id: 1 });
                return self::app::main::Reply.Ready;
            }
            route GET "/" { handler: self::app::main::ready; response Ready: 200; }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "unit-sqlite-row-codec", sqliteManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = sqliteRow,
                ["db/schema.sql"] = "CREATE TABLE records (value INTEGER, id INTEGER);\n"
            },
            "src/app/main.hob", "E_DB_CODEC_UNSUPPORTED",
            "SQLite row struct 'Row' field 'value' has unsupported type 'Unit'; use i32, bool, Text, or Option of one of those scalar types",
            "value: Unit", "value");

        const string sqliteParameter = """
            module app::main;
            pub struct Parameters { value: Unit }
            pub struct Row { id: i32 }
            pub union Reply { Ready }
            fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one("SELECT id FROM records WHERE value = $value", self::app::main::Parameters { value: () });
                return self::app::main::Reply.Ready;
            }
            route GET "/" { handler: self::app::main::ready; response Ready: 200; }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "unit-sqlite-parameter-codec", sqliteManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = sqliteParameter,
                ["db/schema.sql"] = "CREATE TABLE records (value INTEGER, id INTEGER);\n"
            },
            "src/app/main.hob", "E_DB_CODEC_UNSUPPORTED",
            "SQLite parameter struct 'Parameters' field 'value' has unsupported type 'Unit'; use i32, bool, Text, or Option of one of those scalar types",
            "value: Unit", "value");

        const string badReturn = """
            module harness::unit_boundaries;
            pub fn main() -> i32 effects {} { return; }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "unit-return-without-value", badReturn, "E_SYNTAX",
            "Expected expression, found ';'", "return;", ";");

        const string secondConstructor = """
            module harness::unit_boundaries;
            pub fn main() -> i32 effects {} {
                let value: Unit = Unit();
                return 0;
            }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "unit-second-constructor-rejected", secondConstructor, "E_NAME_UNRESOLVED",
            "Declaration 'Unit' must be referenced with a qualified module path", "Unit()", "Unit");

        const string unitTypeMismatch = """
            module harness::unit_boundaries;
            pub fn main() -> i32 effects {} {
                let value: i32 = ();
                return value;
            }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "unit-does-not-convert", unitTypeMismatch, "E_TYPE_MISMATCH",
            "Expected 'i32', found 'Unit'", "= ()", "(");

        const string malformedGrouping = """
            module harness::unit_boundaries;
            pub fn main() -> i32 effects {} {
                let value: i32 = (1 + 2;
                return value;
            }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "unit-malformed-grouping", malformedGrouping, "E_SYNTAX",
            "Expected ')', found ';'", "+ 2;", ";");
    }

    private static async Task TestUnitMetadataAndReceipt(Harness harness)
    {
        const string manifest = "name = \"unit-metadata\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n";
        static string Source(int privateValue) => $$"""
            module unitmeta;
            pub struct UnitHolder { value: Unit }
            pub union UnitChoice { Present(Unit), Empty }
            pub trait UnitWitness {
                fn accepts(value: Self, item: Unit) -> Unit effects {};
            }
            pub fn keep(value: Unit) -> Unit effects {} { return value; }
            fn private_fact() -> i32 effects {} { return {{privateValue}}; }
            """;

        async Task<string> CreatePackage(string caseName, string source, string? lineEnding = null)
        {
            var root = await harness.WritePackageAsync(
                caseName, manifest,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["src/unitmeta.hob"] = source });
            if (lineEnding is not null)
            {
                var path = Path.Combine(root, "src", "unitmeta.hob");
                var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                await File.WriteAllTextAsync(path, normalized.Replace("\n", lineEnding, StringComparison.Ordinal), new UTF8Encoding(false));
            }
            return root;
        }

        async Task<(string Api, string Audit)> Reports(string root)
        {
            var apiRun = await harness.InvokeCompilerCommandAsync("inspect", "api", root, "--json");
            AssertEqual(0, apiRun.ExitCode, Describe(apiRun));
            using (var apiDocument = JsonDocument.Parse(apiRun.StandardOutput))
            {
                var api = apiDocument.RootElement;
                AssertInspectApiPropertyOrder(api);
                AssertEqual(13, api.GetProperty("schema_version").GetInt32(),
                    "Unit API facts must retain inspect API schema version 13.");
                var keep = api.GetProperty("functions").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::unitmeta::keep");
                AssertUnitPrimitiveType(keep.GetProperty("parameters")[0].GetProperty("type"), "API function parameter");
                AssertUnitPrimitiveType(keep.GetProperty("return_type"), "API function return");

                var holder = api.GetProperty("structs").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::unitmeta::UnitHolder");
                AssertEqual("value", holder.GetProperty("fields")[0].GetProperty("name").GetString(),
                    "The Unit field name and order should remain source-facing.");
                AssertUnitPrimitiveType(holder.GetProperty("fields")[0].GetProperty("type"), "API struct field");

                var choice = api.GetProperty("unions").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::unitmeta::UnitChoice");
                var payload = choice.GetProperty("variants").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "Present")
                    .GetProperty("payload")[0].GetProperty("type");
                AssertUnitPrimitiveType(payload, "API union payload");
            }
            var repeatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", root, "--json");
            AssertEqual(apiRun.StandardOutput, repeatedApi.StandardOutput,
                "Repeated Unit API reports must be byte-identical.");

            var auditRun = await harness.InvokeCompilerCommandAsync("audit", root, "--json");
            AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
            using (var auditDocument = JsonDocument.Parse(auditRun.StandardOutput))
            {
                var audit = auditDocument.RootElement;
                AssertAuditPropertyOrder(audit);
                AssertEqual(11, audit.GetProperty("schema_version").GetInt32(),
                    "Unit audit facts must retain audit schema version 11.");
                var trait = audit.GetProperty("compiler").GetProperty("traits").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "UnitWitness");
                var accepts = trait.GetProperty("methods").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "accepts");
                AssertUnitAuditPrimitiveType(accepts.GetProperty("parameters")[1].GetProperty("type"),
                    "Audit trait method argument");
                AssertUnitAuditPrimitiveType(accepts.GetProperty("return_type"),
                    "Audit trait method return");
            }
            var repeatedAudit = await harness.InvokeCompilerCommandAsync("audit", root, "--json");
            AssertEqual(auditRun.StandardOutput, repeatedAudit.StandardOutput,
                "Repeated Unit audit reports must be byte-identical.");
            return (apiRun.StandardOutput, auditRun.StandardOutput);
        }

        var originalRoot = await CreatePackage("unit-metadata-original", Source(1), "\n");
        var original = await Reports(originalRoot);
        var relocatedRoot = await CreatePackage("unit-metadata-relocated", Source(1));
        var relocated = await Reports(relocatedRoot);
        AssertEqual(original.Api, relocated.Api, "Unit API facts must survive package relocation.");
        AssertEqual(original.Audit, relocated.Audit, "Unit audit facts must survive package relocation.");

        var crlfRoot = await CreatePackage("unit-metadata-crlf", Source(1), "\r\n");
        var crlf = await Reports(crlfRoot);
        AssertEqual(original.Api, crlf.Api, "Unit API facts must normalize LF and CRLF source.");
        AssertEqual(original.Audit, crlf.Audit, "Unit audit facts must normalize LF and CRLF source.");

        var editedRoot = await CreatePackage("unit-metadata-private-body-edit", Source(2));
        var edited = await Reports(editedRoot);
        AssertEqual(original.Api, edited.Api, "A private body edit must not change public Unit API facts.");

        var build = await harness.InvokePackageDirectoryAsync("unit-metadata-build", originalRoot, "build");
        var library = ParseBuiltArtifact(build, "Built library: ");
        using var receipt = await AssertBuildReceiptAsync(
            Path.GetDirectoryName(library)!, "managed", null,
            [Path.GetRelativePath(Path.GetDirectoryName(library)!, library).Replace(Path.DirectorySeparatorChar, '/')],
            originalRoot);
        AssertEqual(4, receipt.RootElement.GetProperty("schema_version").GetInt32(),
            "Unit managed builds must retain receipt schema version 4.");
    }

    private static void AssertUnitPrimitiveType(JsonElement type, string label)
    {
        AssertJsonPropertyOrder(type, "kind,name");
        AssertEqual("primitive", type.GetProperty("kind").GetString(),
            $"The {label} should use the existing primitive type shape.");
        AssertEqual("Unit", type.GetProperty("name").GetString(),
            $"The {label} should retain the Unit primitive name.");
    }

    private static void AssertUnitAuditPrimitiveType(JsonElement type, string label)
    {
        AssertAuditTypePropertyOrder(type);
        AssertEqual("primitive", type.GetProperty("kind").GetString(),
            $"The {label} should use the existing audit primitive type shape.");
        AssertEqual("Unit", type.GetProperty("name").GetString(),
            $"The {label} should retain the Unit primitive name.");
    }

    private static async Task TestUnitEvaluationOrder(Harness harness)
    {
        const string manifest = "name = \"unit-evaluation-order\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n\n[capabilities]\nlog.write = \"allow\"\n";
        const string main = """
            module app::main;
            command probe {
                help "Check Unit equality evaluation order.";
                argument mode: i32 help "Probe mode.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string handlers = """
            module handlers;
            pub union ProbeError { Failed }

            async fn eq_left(logger: Logger) -> Unit effects { log.write } {
                let logged: bool = logger.info("eq-left", "");
                return ();
            }
            async fn eq_right(logger: Logger) -> Unit effects { log.write } {
                let logged: bool = logger.info("eq-right", "");
                return ();
            }
            async fn ne_left(logger: Logger) -> Unit effects { log.write } {
                let logged: bool = logger.info("ne-left", "");
                return ();
            }
            async fn ne_right(logger: Logger) -> Unit effects { log.write } {
                let logged: bool = logger.info("ne-right", "");
                return ();
            }
            async fn fault_left(logger: Logger) -> Unit effects { log.write } {
                let logged: bool = logger.info("fault-left", "");
                let overflow: i32 = 2147483647 + 1;
                if overflow == 0 { return (); }
                return ();
            }
            async fn fault_right(logger: Logger) -> Unit effects { log.write } {
                let logged: bool = logger.info("fault-right", "");
                let overflow: i32 = 2147483647 + 1;
                if overflow == 0 { return (); }
                return ();
            }

            pub async fn run(
                args: self::app::main::ProbeArgs,
                logger: Logger
            ) -> Result<Text, self::handlers::ProbeError> effects { log.write } {
                if args.mode == 0 {
                    let equal: bool = await self::handlers::eq_left(logger) == await self::handlers::eq_right(logger);
                    if equal { return Ok("equal"); }
                    return Err(self::handlers::ProbeError.Failed);
                }
                if args.mode == 1 {
                    let unequal: bool = await self::handlers::ne_left(logger) != await self::handlers::ne_right(logger);
                    if unequal { return Err(self::handlers::ProbeError.Failed); }
                    return Ok("not-unequal");
                }
                if args.mode == 2 {
                    let equal: bool = await self::handlers::fault_left(logger) == await self::handlers::eq_right(logger);
                    if equal { return Ok("unexpected"); }
                    return Err(self::handlers::ProbeError.Failed);
                }
                if args.mode == 3 {
                    let equal: bool = await self::handlers::eq_left(logger) == await self::handlers::fault_right(logger);
                    if equal { return Ok("unexpected"); }
                    return Err(self::handlers::ProbeError.Failed);
                }
                return Ok("unknown-mode");
            }
            pub fn describe(error: self::handlers::ProbeError) -> Text effects {} {
                return match error { self::handlers::ProbeError.Failed => "Unit comparison failed" };
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "unit-evaluation-order", manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = main,
                ["src/handlers.hob"] = handlers
            });
        var lockResult = await harness.InvokePackageDirectoryAsync(
            "unit-evaluation-order-lock", packageRoot, "lock");
        AssertEqual(0, lockResult.ExitCode, Describe(lockResult));
        AssertEqual(string.Empty, lockResult.StandardError, Describe(lockResult));
        AssertEqual("No dependencies to lock for package 'unit-evaluation-order'." + Environment.NewLine,
            lockResult.StandardOutput, Describe(lockResult));
        var check = await harness.InvokePackageDirectoryAsync(
            "unit-evaluation-order-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        async Task<ProcessResult> Managed(string mode) => await harness.InvokePackageDirectoryAsync(
            "unit-managed-" + mode, packageRoot, "run", "--", "probe", mode);
        AssertUnitLogRun(await Managed("0"), 0, "equal", ["eq-left", "eq-right"]);
        AssertUnitLogRun(await Managed("1"), 0, "not-unequal", ["ne-left", "ne-right"]);
        AssertUnitLogFault(await Managed("2"), ["fault-left"]);
        AssertUnitLogFault(await Managed("3"), ["eq-left", "fault-right"]);

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64
            || (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("The Unit evaluation-order NativeAOT test requires Windows x64 or Linux x64.");
        var aotBuild = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "unit-evaluation-order-aot", packageRoot, "build", AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, aotBuild.ExitCode, Describe(aotBuild));
        const string executablePrefix = "Built native executable: ";
        AssertTrue(aotBuild.StandardOutput.StartsWith(executablePrefix, StringComparison.Ordinal), Describe(aotBuild));
        var executable = aotBuild.StandardOutput[executablePrefix.Length..].TrimEnd('\r', '\n');
        AssertTrue(Path.IsPathFullyQualified(executable) && File.Exists(executable),
            $"Expected the NativeAOT Unit evaluation-order executable at {executable}.");
        AssertUnitLogRun(await ExecuteNativeAsync(executable, TimeSpan.FromSeconds(30), "probe", "0"),
            0, "equal", ["eq-left", "eq-right"]);
        AssertUnitLogRun(await ExecuteNativeAsync(executable, TimeSpan.FromSeconds(30), "probe", "1"),
            0, "not-unequal", ["ne-left", "ne-right"]);
        AssertUnitLogFault(await ExecuteNativeAsync(executable, TimeSpan.FromSeconds(30), "probe", "2"), ["fault-left"]);
        AssertUnitLogFault(await ExecuteNativeAsync(executable, TimeSpan.FromSeconds(30), "probe", "3"),
            ["eq-left", "fault-right"]);
    }

    private static void AssertUnitLogRun(ProcessResult result, int exitCode, string output, string[] events)
    {
        AssertEqual(exitCode, result.ExitCode, Describe(result));
        AssertEqual(output + Environment.NewLine, result.StandardOutput, Describe(result));
        AssertUnitLoggerEvents(result.StandardError, events, "successful Unit comparison");
    }

    private static void AssertUnitLogFault(ProcessResult result, string[] events)
    {
        AssertEqual(70, result.ExitCode, Describe(result));
        AssertEqual(string.Empty, result.StandardOutput, Describe(result));
        AssertUnitLoggerEvents(result.StandardError, events, "faulting Unit comparison", expectFault: true);
        AssertTrue(!result.StandardError.Contains("Exception", StringComparison.Ordinal)
            && !result.StandardError.Contains(" at ", StringComparison.Ordinal),
            "A checked arithmetic fault during Unit comparison must not expose an exception or stack trace.");
    }

    private static void AssertUnitLoggerEvents(
        string stderr,
        string[] expectedEvents,
        string context,
        bool expectFault = false)
    {
        var lines = stderr.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var eventLines = lines.Where(line => line.StartsWith("{", StringComparison.Ordinal)).ToArray();
        AssertEqual(expectedEvents.Length, eventLines.Length,
            $"{context}: expected exactly {expectedEvents.Length} structured logger events. stderr=<{stderr}>");
        for (var index = 0; index < eventLines.Length; index++)
        {
            using var document = JsonDocument.Parse(eventLines[index]);
            AssertJsonPropertyOrder(document.RootElement, "level,event,detail");
            AssertEqual("info", document.RootElement.GetProperty("level").GetString(),
                $"{context}: logger level mismatch at event {index}.");
            AssertEqual(expectedEvents[index], document.RootElement.GetProperty("event").GetString(),
                $"{context}: event order or count mismatch at event {index}.");
            AssertEqual(string.Empty, document.RootElement.GetProperty("detail").GetString(),
                $"{context}: logger detail mismatch at event {index}.");
        }
        if (expectFault)
        {
            AssertEqual(expectedEvents.Length + 1, lines.Length,
                $"{context}: fault output should follow the expected logger events. stderr=<{stderr}>");
            AssertEqual("Runtime fault", lines[^1], $"{context}: expected the generic runtime fault after its events.");
        }
        else
        {
            AssertEqual(expectedEvents.Length, lines.Length,
                $"{context}: unexpected non-logger stderr output. stderr=<{stderr}>");
        }
    }
}
