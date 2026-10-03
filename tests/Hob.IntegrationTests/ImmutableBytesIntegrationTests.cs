using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestImmutableBytes(Harness harness)
    {
        var fixtureDirectory = Path.Combine(harness.RepositoryRoot, "fixtures");
        var validSource = await File.ReadAllTextAsync(Path.Combine(fixtureDirectory, "105-valid-immutable-bytes.hob"));
        var managed = await harness.InvokeAsync("immutable-bytes-managed", "run", validSource);
        AssertRunOutput("bytes-ok" + Environment.NewLine, managed);

        const string boundarySource = """
            module harness::immutable_bytes_bounds;
            fn invalid(value: Result<Bytes, BytesError>) -> bool effects {} {
                return match value {
                    Ok(bytes) => false,
                    Err(error) => match error { BytesError.InvalidOctet => true }
                };
            }
            pub fn main() -> Text effects {} {
                let empty: Bytes = Bytes.empty();
                let original: Bytes = match empty.append(128) {
                    Ok(bytes) => bytes,
                    Err(error) => Bytes.empty()
                };
                let low: bool = self::harness::immutable_bytes_bounds::invalid(original.append(-1));
                let high: bool = self::harness::immutable_bytes_bounds::invalid(original.append(256));
                let minimum: bool = self::harness::immutable_bytes_bounds::invalid(original.append(-2147483648));
                let maximum: bool = self::harness::immutable_bytes_bounds::invalid(original.append(2147483647));
                let expected_octet: Option<i32> = Some(128);
                let no_octet: Option<i32> = None;
                if low == false { return "bytes-bounds-failed"; }
                if original.length != 1 { return "bytes-bounds-failed"; }
                if original.get(0) != expected_octet { return "bytes-bounds-failed"; }
                if high == false { return "bytes-bounds-failed"; }
                if original.length != 1 { return "bytes-bounds-failed"; }
                if original.get(0) != expected_octet { return "bytes-bounds-failed"; }
                if minimum == false { return "bytes-bounds-failed"; }
                if maximum == false { return "bytes-bounds-failed"; }
                if original.length != 1 { return "bytes-bounds-failed"; }
                if original.get(0) != expected_octet { return "bytes-bounds-failed"; }
                if original.get(-1) != no_octet { return "bytes-bounds-failed"; }
                if original.get(1) != no_octet { return "bytes-bounds-failed"; }
                if original.get(-2147483648) != no_octet { return "bytes-bounds-failed"; }
                if original.get(2147483647) != no_octet { return "bytes-bounds-failed"; }
                return "bytes-bounds-ok";
            }
            """;
        AssertRunOutput("bytes-bounds-ok" + Environment.NewLine,
            await harness.InvokeAsync("immutable-bytes-independent-bounds", "run", boundarySource));

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("Immutable Bytes NativeAOT tests require Windows x64 or Linux x64.");

        var aot = await harness.InvokeWithTimeoutAsync(
            "immutable-bytes-aot", "build", validSource, AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, aot.ExitCode, Describe(aot));
        const string executablePrefix = "Built native executable: ";
        AssertTrue(aot.StandardOutput.StartsWith(executablePrefix, StringComparison.Ordinal), Describe(aot));
        var executable = aot.StandardOutput[executablePrefix.Length..].TrimEnd('\r', '\n');
        AssertTrue(Path.IsPathFullyQualified(executable) && File.Exists(executable),
            $"Expected a NativeAOT Bytes executable at {executable}.");
        AssertRunOutput("bytes-ok" + Environment.NewLine,
            await ExecuteNativeAsync(executable, TimeSpan.FromSeconds(30)));

        var dependencyRoot = await harness.WritePackageGraphAsync(
            "immutable-bytes-empty-helper-dependency",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\ncore = \"../core\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = """
                            module app::main;
                            pub fn main() -> i32 effects {} { return core::bytes::empty_length(); }
                            """
                    }),
                ["core"] = new PackageFixture(
                    LibraryPackageManifest("immutable-bytes-core"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/bytes.hob"] = """
                            module bytes;
                            pub fn empty_length() -> i32 effects {} {
                                let value: Bytes = Bytes.empty();
                                return value.length;
                            }
                            """
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "immutable-bytes-empty-helper-lock", dependencyRoot, "lock"));
        AssertRunOutput("0" + Environment.NewLine,
            await harness.InvokePackageDirectoryAsync("immutable-bytes-empty-helper-managed", dependencyRoot, "run"));

        await TestImmutableBytesInvalidFixture(harness, fixtureDirectory);
        await TestImmutableBytesResourceBoundary(harness);
        await TestImmutableBytesWireBoundaries(harness);
        await TestImmutableBytesMetadataAndReceipt(harness);
        await TestImmutableBytesLockFreshness(harness);

        var dependencyAot = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "immutable-bytes-empty-helper-aot", dependencyRoot, "build", AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, dependencyAot.ExitCode, Describe(dependencyAot));
        AssertTrue(dependencyAot.StandardOutput.StartsWith(executablePrefix, StringComparison.Ordinal), Describe(dependencyAot));
        var dependencyExecutable = dependencyAot.StandardOutput[executablePrefix.Length..].TrimEnd('\r', '\n');
        AssertTrue(Path.IsPathFullyQualified(dependencyExecutable) && File.Exists(dependencyExecutable),
            $"Expected an empty-only Bytes helper NativeAOT executable at {dependencyExecutable}.");
        AssertRunOutput("0" + Environment.NewLine,
            await ExecuteNativeAsync(dependencyExecutable, TimeSpan.FromSeconds(30)));
    }

    private static async Task TestImmutableBytesInvalidFixture(Harness harness, string fixtureDirectory)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(fixtureDirectory, "106-invalid-immutable-bytes-basics.hob"));
        var result = await harness.InvokeAsync("immutable-bytes-invalid-fixture", "check", source, "--json");
        AssertTrue(result.ExitCode != 0, Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        var diagnostics = ParseDiagnosticSnapshots(result.StandardOutput);
        var expected = new (string Code, string Message, string Marker, string Token)[]
        {
            ("E_NAME_DUPLICATE", "Type parameter 'Bytes' is reserved", "reserved_builtin_type_parameters<Bytes, BytesError>", "Bytes"),
            ("E_NAME_DUPLICATE", "Type parameter 'BytesError' is reserved", "reserved_builtin_type_parameters<Bytes, BytesError>", "BytesError"),
            ("E_TYPE_MISMATCH", "Bytes.empty expects 0 arguments, got 1", "Bytes.empty(1)", "empty"),
            ("E_TYPE_MISMATCH", "Expected 'i32', found 'bool'", "append(true)", "true"),
            ("E_TYPE_MISMATCH", "Expected 'i32', found 'Text'", "get(\"first\")", "\"first\""),
            ("E_TYPE_MISMATCH", "Bytes.append expects 1 argument, got 0", "value.append()", "append"),
            ("E_FIELD_UNKNOWN", "Bytes has no member 'length'", "value.length()", "length"),
            ("E_FIELD_UNKNOWN", "Bytes has no member 'to_text'", "value.to_text()", "to_text"),
            ("E_FIELD_UNKNOWN", "Bytes has no field 'octets'", "value.octets", "octets"),
            ("E_TYPE_MISMATCH", "Type 'Bytes' does not take type arguments", "Bytes<i32>", "Bytes"),
            ("E_TYPE_MISMATCH", "BytesError variants can only be produced by Bytes operations", "BytesError.InvalidOctet", "InvalidOctet"),
            ("E_TYPE_MISMATCH", "BytesError variants can only be produced by Bytes operations", "BytesError.InvalidOctet()", "InvalidOctet"),
            ("E_NAME_UNRESOLVED", "Variant 'Missing' is not declared on BytesError", "BytesError.Missing;", "Missing"),
            ("E_NAME_UNRESOLVED", "Variant 'Missing' is not declared on BytesError", "BytesError.Missing()", "Missing"),
            ("E_TYPE_MISMATCH", "Comparison '==' requires matching immutable values with structural equality; found 'Bytes' and 'Bytes'", "value == Bytes.empty()", "=="),
            ("E_UNSUPPORTED", "Member calls on local values are not implemented for 'Bytes.empty'", "return Bytes.empty();", "empty")
        };
        AssertEqual(expected.Length, diagnostics.Length, "The Bytes invalid fixture must keep one precise diagnostic per invalid construct.");
        for (var index = 0; index < expected.Length; index++)
        {
            AssertEqual(expected[index].Code, diagnostics[index].Code, $"Unexpected Bytes fixture diagnostic #{index + 1}.");
            AssertEqual(expected[index].Message, diagnostics[index].Message, $"Unexpected Bytes fixture message #{index + 1}.");
            AssertBytesRange(source, diagnostics[index], expected[index].Marker, expected[index].Token);
        }
        AssertNoCompilerArtifacts(Path.GetDirectoryName(harness.LastSourcePath)!);
    }

    private static async Task TestImmutableBytesResourceBoundary(Harness harness)
    {
        const string valid = """
            module harness::immutable_bytes_resource_value;
            struct Box<T> { value: T }
            fn preserve(value: self::harness::immutable_bytes_resource_value::Box<Bytes>) -> List<self::harness::immutable_bytes_resource_value::Box<Bytes>> effects {} {
                return [value];
            }
            pub fn main() -> i32 effects {} {
                let value: self::harness::immutable_bytes_resource_value::Box<Bytes> =
                    self::harness::immutable_bytes_resource_value::Box<Bytes> { value: Bytes.empty() };
                return self::harness::immutable_bytes_resource_value::preserve(value).length;
            }
            """;
        AssertRunOutput("1" + Environment.NewLine,
            await harness.InvokeAsync("immutable-bytes-non-resource", "run", valid));

        const string storedResource = """
            module harness::immutable_bytes_stored_resource;
            struct Carrier { bytes: Bytes, reader: FsRead }
            union ResourceLists { Items(List<self::harness::immutable_bytes_stored_resource::Carrier>) }
            """;
        var diagnostic = await AssertBytesStandaloneDiagnosticAsync(
            harness, "immutable-bytes-do-not-hide-resource", storedResource,
            "E_RESOURCE_ESCAPE", "Lists cannot contain resource handles, directly or through nested types",
            "List<self::harness::immutable_bytes_stored_resource::Carrier>", "List");
        AssertEqual("E_RESOURCE_ESCAPE", diagnostic.Code, "Bytes must not hide a recursively stored FsRead resource.");
    }

    private static async Task TestImmutableBytesWireBoundaries(Harness harness)
    {
        var cliMain = """
            module app::main;
            command echo {
                help "Echo a payload.";
                argument payload: Bytes help "Payload.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string cliHandlers = """
            module handlers;
            pub fn run(args: self::app::main::EchoArgs) -> Result<Text, FsError> effects {} { return Ok("done"); }
            pub fn describe(error: FsError) -> Text effects {} {
                return match error {
                    FsError.NotFound => "not found",
                    FsError.PermissionDenied => "permission denied",
                    FsError.InvalidPath => "invalid path",
                    FsError.InvalidText => "invalid text",
                    FsError.Io => "I/O error"
                };
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "immutable-bytes-cli-wire-rejected", CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = cliMain,
                ["src/handlers.hob"] = cliHandlers
            },
            "src/app/main.hob", "E_COMMAND_DECL", "Command input type 'Bytes' is not supported; use FilePath, Text, or i32",
            "argument payload: Bytes", "argument");

        const string webManifest = "name = \"bytes-web\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n\n[capabilities]\nnet.listen = \"allow\"\n";
        const string routeBinding = """
            module app::main;
            pub union Reply { Found(Text), Empty }
            fn get(payload: Bytes) -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            route GET "/{payload}" {
                path payload: Bytes;
                handler: self::app::main::get;
                response Found: 200 json Text;
                response Empty: 404;
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "immutable-bytes-route-binding-rejected", webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = routeBinding },
            "src/app/main.hob", "E_ROUTE_BINDING", "Path bindings support only Text and i32",
            "path payload: Bytes", "Bytes");

        const string postBody = """
            module app::main;
            pub union Reply { Accepted, Empty }
            fn post(value: Bytes) -> self::app::main::Reply effects {} { return self::app::main::Reply.Accepted; }
            route POST "/items" {
                body: Bytes;
                handler: self::app::main::post;
                response Accepted: 200;
                response Empty: 204;
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "immutable-bytes-post-body-rejected", webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = postBody },
            "src/app/main.hob", "E_ROUTE_DECL", "The route body type must use a fully qualified type reference",
            "body: Bytes", "Bytes");

        const string postNested = """
            module app::main;
            struct Request { payload: Bytes }
            pub union Reply { Accepted, Empty }
            fn post(request: self::app::main::Request) -> self::app::main::Reply effects {} { return self::app::main::Reply.Accepted; }
            route POST "/items" {
                body: self::app::main::Request;
                handler: self::app::main::post;
                response Accepted: 200;
                response Empty: 204;
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "immutable-bytes-post-nested-codec-rejected", webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = postNested },
            "src/app/main.hob", "E_ROUTE_CODEC_UNSUPPORTED",
            "POST route body structs must contain only acyclic i32, bool, Text, and supported struct fields",
            "body: self::app::main::Request", "self");

        const string responsePayload = """
            module app::main;
            struct Payload { bytes: Bytes }
            pub union Reply { Found(self::app::main::Payload), Empty }
            fn get() -> self::app::main::Reply effects {} { return self::app::main::Reply.Empty; }
            route GET "/items" {
                handler: self::app::main::get;
                response Found: 200 json self::app::main::Payload;
                response Empty: 404;
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "immutable-bytes-response-codec-rejected", webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = responsePayload },
            "src/app/main.hob", "E_ROUTE_CODEC_UNSUPPORTED",
            "JSON response type must exactly match a payload with a supported JSON shape",
            "response Found: 200 json self::app::main::Payload", "self");

        const string sqliteManifest = "name = \"bytes-sqlite\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\nsqlite_path = \"data/bytes.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n";
        const string sqliteRow = """
            module app::main;
            struct Parameters {}
            struct Row { payload: Bytes }
            pub union Reply { Ready }
            fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one("SELECT payload FROM records", self::app::main::Parameters {});
                return self::app::main::Reply.Ready;
            }
            route GET "/" { handler: self::app::main::ready; response Ready: 200; }
            """;
        var sqliteFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = sqliteRow,
            ["db/schema.sql"] = "CREATE TABLE records (payload TEXT);\n"
        };
        await AssertBytesPackageDiagnosticAsync(
            harness, "immutable-bytes-sqlite-row-rejected", sqliteManifest, sqliteFiles,
            "src/app/main.hob", "E_DB_CODEC_UNSUPPORTED",
            "SQLite row struct 'Row' field 'payload' has unsupported type 'Bytes'; use i32, bool, Text, or Option of one of those scalar types",
            "payload: Bytes", "payload");

        const string sqliteParameter = """
            module app::main;
            struct Parameters { payload: Bytes }
            struct Row { id: i32 }
            pub union Reply { Ready }
            fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one("SELECT id FROM records WHERE payload = $payload", self::app::main::Parameters { payload: Bytes.empty() });
                return self::app::main::Reply.Ready;
            }
            route GET "/" { handler: self::app::main::ready; response Ready: 200; }
            """;
        sqliteFiles["src/app/main.hob"] = sqliteParameter;
        await AssertBytesPackageDiagnosticAsync(
            harness, "immutable-bytes-sqlite-parameter-rejected", sqliteManifest, sqliteFiles,
            "src/app/main.hob", "E_DB_CODEC_UNSUPPORTED",
            "SQLite parameter struct 'Parameters' field 'payload' has unsupported type 'Bytes'; use i32, bool, Text, or Option of one of those scalar types",
            "payload: Bytes", "payload");

        const string writeText = """
            module harness::immutable_bytes_fswrite;
            pub fn write(writer: FsWrite, payload: Bytes) -> Result<bool, FsError> effects { fs.write } {
                return writer.write_text("payload.txt", payload);
            }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "immutable-bytes-fswrite-text-only", writeText, "E_TYPE_MISMATCH",
            "Intrinsic 'fs.write_text' expects a Text value, found 'Bytes'", "payload);", "payload");

        const string readText = """
            module harness::immutable_bytes_fsread;
            pub fn read(reader: FsRead) -> Result<Bytes, FsError> effects { fs.read } {
                return reader.read_text("payload.txt");
            }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "immutable-bytes-fsread-text-only", readText, "E_TYPE_MISMATCH",
            "Expected 'Result<Bytes, FsError>', found 'Result<Text, FsError>'",
            "reader.read_text(\"payload.txt\")", "reader");

        const string assignment = """
            module harness::immutable_bytes_text_assignment;
            fn convert(payload: Bytes) -> Text effects {} {
                let text: Text = payload;
                return text;
            }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "immutable-bytes-no-text-assignment", assignment, "E_TYPE_MISMATCH",
            "Expected 'Text', found 'Bytes'", "let text: Text = payload", "payload");

        const string returned = """
            module harness::immutable_bytes_text_return;
            fn convert(payload: Bytes) -> Text effects {} { return payload; }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "immutable-bytes-no-text-return", returned, "E_TYPE_MISMATCH",
            "Expected 'Text', found 'Bytes'", "return payload", "payload");

        const string called = """
            module harness::immutable_bytes_text_call;
            fn consume(value: Text) -> i32 effects {} { return value.length; }
            fn invoke(payload: Bytes) -> i32 effects {} { return self::harness::immutable_bytes_text_call::consume(payload); }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "immutable-bytes-no-text-call", called, "E_TYPE_MISMATCH",
            "Expected 'Text', found 'Bytes'", "consume(payload)", "payload");

        const string compared = """
            module harness::immutable_bytes_text_comparison;
            fn compare(payload: Bytes) -> bool effects {} { return payload == "text"; }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "immutable-bytes-no-text-comparison", compared, "E_TYPE_MISMATCH",
            "Comparison '==' requires matching immutable values with structural equality; found 'Bytes' and 'Text'",
            "payload == \"text\"", "==");

        const string http = """
            module harness::immutable_bytes_http_text;
            pub async fn fetch(client: HttpClient, target: Bytes) -> Result<HttpResponse, HttpError> effects { net.client } {
                return await client.get_text_async(target);
            }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "immutable-bytes-http-text-target", http, "E_TYPE_MISMATCH",
            "Intrinsic 'HttpClient.get_text_async' expects a Text target, found 'Bytes'",
            "get_text_async(target)", "target");

        const string httpBody = """
            module harness::immutable_bytes_http_response_body;
            fn body(response: HttpResponse) -> Bytes effects {} { return response.body; }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "immutable-bytes-http-body-is-text", httpBody, "E_TYPE_MISMATCH",
            "Expected 'Bytes', found 'Text'", "return response.body", "body");

        const string processPinContents = "bytes process test pin\n";
        var processPinHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(processPinContents))).ToLowerInvariant();
        var processHost = OperatingSystem.IsWindows() ? "windows" : "linux";
        var processManifest = CliPackageManifest()
            + $"process_{processHost}_path = \"tools/runner\"\n"
            + $"process_{processHost}_sha256 = \"{processPinHash}\"\n\n"
            + "[capabilities]\nprocess.spawn = \"allow\"\n";
        const string processMain = """
            module app::main;
            command run {
                help "Run a process.";
                argument label: Text help "Invocation label.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string processArgs = """
            module handlers;
            pub union RunError { Failed }
            pub fn run(args: self::app::main::RunArgs) -> Result<Text, self::handlers::RunError> effects {} { return Ok(args.label); }
            pub fn describe(error: self::handlers::RunError) -> Text effects {} { return match error { self::handlers::RunError.Failed => "failed" }; }
            async fn invoke(runner: ProcessRunner, payload: Bytes) -> Result<ProcessOutput, ProcessError> effects { process.spawn } {
                let arguments: List<Bytes> = [payload];
                return await runner.run_text_async(arguments, "");
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "immutable-bytes-process-args-text-only", processManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = processMain,
                ["src/handlers.hob"] = processArgs,
                ["tools/runner"] = processPinContents
            },
            "src/handlers.hob", "E_TYPE_MISMATCH", "Expected 'List<Text>', found 'List<Bytes>'",
            "run_text_async(arguments, \"\")", "arguments");

        const string processInput = """
            module handlers;
            pub union RunError { Failed }
            pub fn run(args: self::app::main::RunArgs) -> Result<Text, self::handlers::RunError> effects {} { return Ok(args.label); }
            pub fn describe(error: self::handlers::RunError) -> Text effects {} { return match error { self::handlers::RunError.Failed => "failed" }; }
            async fn invoke(runner: ProcessRunner, payload: Bytes) -> Result<ProcessOutput, ProcessError> effects { process.spawn } {
                return await runner.run_text_async([], payload);
            }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "immutable-bytes-process-input-text-only", processManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = processMain,
                ["src/handlers.hob"] = processInput,
                ["tools/runner"] = processPinContents
            },
            "src/handlers.hob", "E_TYPE_MISMATCH", "Expected 'Text', found 'Bytes'",
            "[], payload", "payload");

        const string processOutput = """
            module handlers;
            pub union RunError { Failed }
            pub fn run(args: self::app::main::RunArgs) -> Result<Text, self::handlers::RunError> effects {} { return Ok(args.label); }
            pub fn describe(error: self::handlers::RunError) -> Text effects {} { return match error { self::handlers::RunError.Failed => "failed" }; }
            fn stdout(output: ProcessOutput) -> Bytes effects {} { return output.stdout; }
            """;
        await AssertBytesPackageDiagnosticAsync(
            harness, "immutable-bytes-process-output-is-text", processManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = processMain,
                ["src/handlers.hob"] = processOutput,
                ["tools/runner"] = processPinContents
            },
            "src/handlers.hob", "E_TYPE_MISMATCH", "Expected 'Bytes', found 'Text'",
            "return output.stdout", "stdout");
    }

    private static async Task TestImmutableBytesMetadataAndReceipt(Harness harness)
    {
        const string manifest = "name = \"immutable-bytes-metadata\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n";
        static string Source(int hiddenValue) => $$"""
            module app::bytes;
            pub struct Snapshot { value: Bytes }
            pub trait ByteRoundTrip { fn copy(value: Self, octet: Bytes) -> Result<Bytes, BytesError> effects {}; }
            pub fn append(value: Bytes, octet: i32) -> Result<Bytes, BytesError> effects {} {
                return value.append(octet);
            }
            // Private comment revision {{hiddenValue}} does not change the public type identity.
            fn hidden() -> i32 effects {} { return {{hiddenValue}}; }
            """;

        async Task<string> CreatePackage(string caseName, string source, string? lineEnding = null)
        {
            var packageRoot = await harness.WritePackageAsync(
                caseName, manifest,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/bytes.hob"] = source });
            if (lineEnding is not null)
            {
                var sourcePath = Path.Combine(packageRoot, "src", "app", "bytes.hob");
                var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                await File.WriteAllTextAsync(sourcePath,
                    normalized.Replace("\n", lineEnding, StringComparison.Ordinal), new UTF8Encoding(false));
            }
            var packageLock = await harness.InvokePackageDirectoryAsync(caseName + "-lock", packageRoot, "lock");
            AssertEqual(0, packageLock.ExitCode, Describe(packageLock));
            AssertEqual(string.Empty, packageLock.StandardError, Describe(packageLock));
            return packageRoot;
        }

        async Task<(string Api, string Audit)> Reports(string caseName, string packageRoot)
        {
            var apiRun = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
            AssertEqual(0, apiRun.ExitCode, Describe(apiRun));
            using (var apiDocument = JsonDocument.Parse(apiRun.StandardOutput))
            {
                var api = apiDocument.RootElement;
                AssertInspectApiPropertyOrder(api);
                AssertEqual(13, api.GetProperty("schema_version").GetInt32(),
                    "Bytes primitive metadata must retain inspect API schema version 13.");
                var append = api.GetProperty("functions").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::app::bytes::append");
                var parameterType = append.GetProperty("parameters")[0].GetProperty("type");
                AssertJsonPropertyOrder(parameterType, "kind,name");
                AssertEqual("primitive", parameterType.GetProperty("kind").GetString(), "Bytes parameters should use the primitive type shape.");
                AssertEqual("Bytes", parameterType.GetProperty("name").GetString(), "The API should preserve the Bytes type name.");
                var result = append.GetProperty("return_type");
                AssertJsonPropertyOrder(result, "kind,ok,error");
                AssertEqual("result", result.GetProperty("kind").GetString(), "Append should retain its Result wrapper.");
                AssertJsonPropertyOrder(result.GetProperty("ok"), "kind,name");
                AssertEqual("primitive", result.GetProperty("ok").GetProperty("kind").GetString(), "The Result success type should be primitive.");
                AssertEqual("Bytes", result.GetProperty("ok").GetProperty("name").GetString(), "Append should return Bytes on success.");
                AssertJsonPropertyOrder(result.GetProperty("error"), "kind,name");
                AssertEqual("primitive", result.GetProperty("error").GetProperty("kind").GetString(), "The Result error type should be primitive.");
                AssertEqual("BytesError", result.GetProperty("error").GetProperty("name").GetString(), "Append should return BytesError on failure.");
                var snapshot = api.GetProperty("structs").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::app::bytes::Snapshot");
                var fieldType = snapshot.GetProperty("fields")[0].GetProperty("type");
                AssertJsonPropertyOrder(fieldType, "kind,name");
                AssertEqual("Bytes", fieldType.GetProperty("name").GetString(), "Stored Bytes fields should retain the primitive type name.");
            }
            var repeatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
            AssertEqual(apiRun.StandardOutput, repeatedApi.StandardOutput,
                "Repeated Bytes API inspections must be byte-identical.");

            var auditRun = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
            using (var auditDocument = JsonDocument.Parse(auditRun.StandardOutput))
            {
                var audit = auditDocument.RootElement;
                AssertAuditPropertyOrder(audit);
                AssertEqual(11, audit.GetProperty("schema_version").GetInt32(),
                    "Bytes primitive metadata must retain audit schema version 11.");
                var byteRoundTrip = audit.GetProperty("compiler").GetProperty("traits").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "ByteRoundTrip");
                var copy = byteRoundTrip.GetProperty("methods").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "copy");
                var parameterType = copy.GetProperty("parameters")[1].GetProperty("type");
                AssertAuditTypePropertyOrder(parameterType);
                AssertEqual("primitive", parameterType.GetProperty("kind").GetString(), "Audit trait method parameters should encode Bytes as a primitive.");
                AssertEqual("Bytes", parameterType.GetProperty("name").GetString(), "Audit trait method parameters should retain the Bytes type name.");
                var returnType = copy.GetProperty("return_type");
                AssertAuditTypePropertyOrder(returnType);
                AssertEqual("result", returnType.GetProperty("kind").GetString(), "Audit trait method facts should preserve the Result type.");
                AssertEqual("Bytes", returnType.GetProperty("ok").GetProperty("name").GetString(), "Audit should preserve the Bytes result type.");
                AssertEqual("BytesError", returnType.GetProperty("error").GetProperty("name").GetString(), "Audit should preserve the BytesError result type.");
            }
            var repeatedAudit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertEqual(auditRun.StandardOutput, repeatedAudit.StandardOutput,
                "Repeated Bytes audit reports must be byte-identical.");
            return (apiRun.StandardOutput, auditRun.StandardOutput);
        }

        var originalRoot = await CreatePackage("immutable-bytes-metadata-lf", Source(1), "\n");
        var original = await Reports("immutable-bytes-metadata-original", originalRoot);
        var relocatedRoot = await CreatePackage("immutable-bytes-metadata-relocated", Source(1));
        var relocated = await Reports("immutable-bytes-metadata-relocated", relocatedRoot);
        AssertEqual(original.Api, relocated.Api, "Bytes API metadata must remain identical after package relocation.");
        AssertEqual(original.Audit, relocated.Audit, "Bytes audit metadata must remain identical after package relocation.");
        var crlfRoot = await CreatePackage("immutable-bytes-metadata-crlf", Source(1), "\r\n");
        var crlf = await Reports("immutable-bytes-metadata-crlf", crlfRoot);
        AssertEqual(original.Api, crlf.Api, "Bytes API metadata must normalize LF and CRLF source inputs.");
        AssertEqual(original.Audit, crlf.Audit, "Bytes audit metadata must normalize LF and CRLF source inputs.");
        var editedRoot = await CreatePackage("immutable-bytes-metadata-body-edit", Source(2));
        var edited = await Reports("immutable-bytes-metadata-body-edit", editedRoot);
        AssertEqual(original.Api, edited.Api, "A private body edit must not change public Bytes API metadata.");

        var build = await harness.InvokePackageDirectoryAsync("immutable-bytes-metadata-build", originalRoot, "build");
        var library = ParseBuiltArtifact(build, "Built library: ");
        using var receipt = await AssertBuildReceiptAsync(
            Path.GetDirectoryName(library)!, "managed", null,
            [Path.GetRelativePath(Path.GetDirectoryName(library)!, library).Replace(Path.DirectorySeparatorChar, '/')],
            originalRoot);
        AssertEqual(4, receipt.RootElement.GetProperty("schema_version").GetInt32(),
            "Bytes must remain covered by build receipt schema 4.");
    }

    private static async Task TestImmutableBytesLockFreshness(Harness harness)
    {
        const string rootSource = """
            module app::main;
            pub fn main() -> i32 effects {} { return core::bytes::length(Bytes.empty()); }
            """;
        const string initialDependency = """
            module bytes;
            pub fn length(value: Bytes) -> i32 effects {} { return value.length; }
            """;
        var packageRoot = await harness.WritePackageGraphAsync(
            "immutable-bytes-lock-freshness",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\ncore = \"../core\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = rootSource }),
                ["core"] = new PackageFixture(
                    LibraryPackageManifest("immutable-bytes-lock-core"),
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/bytes.hob"] = initialDependency })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "immutable-bytes-lock-initial", packageRoot, "lock"));
        var initialCheck = await harness.InvokePackageDirectoryAsync(
            "immutable-bytes-lock-initial-check", packageRoot, "check", "--json");
        AssertEqual(0, initialCheck.ExitCode, Describe(initialCheck));
        var dependencySourcePath = Path.GetFullPath(Path.Combine(
            Directory.GetParent(packageRoot)!.FullName, "core", "src", "bytes.hob"));
        await File.WriteAllTextAsync(dependencySourcePath, initialDependency.Replace(
            "return value.length;", "return value.length + 0;", StringComparison.Ordinal));
        var stale = await harness.InvokePackageDirectoryAsync(
            "immutable-bytes-lock-stale-check", packageRoot, "check", "--json");
        AssertTrue(stale.ExitCode != 0, $"A real dependency input edit must stale the lock. {Describe(stale)}");
        var staleDiagnostics = ParseDiagnosticSnapshots(stale.StandardOutput);
        AssertEqual("E_LOCK", staleDiagnostics.Single().Code,
            "Editing a Bytes dependency must use the existing stale-lock diagnostic.");
        AssertNoCompilerArtifacts(packageRoot);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "immutable-bytes-lock-refresh", packageRoot, "lock"));
        var refreshedCheck = await harness.InvokePackageDirectoryAsync(
            "immutable-bytes-lock-refreshed-check", packageRoot, "check", "--json");
        AssertEqual(0, refreshedCheck.ExitCode, Describe(refreshedCheck));
    }

    private static async Task<DiagnosticSnapshot> AssertBytesStandaloneDiagnosticAsync(
        Harness harness,
        string caseName,
        string source,
        string expectedCode,
        string expectedMessage,
        string marker,
        string rangeToken)
    {
        var result = await harness.InvokeAsync(caseName, "check", source, "--json");
        AssertTrue(result.ExitCode != 0, $"Invalid Bytes source unexpectedly checked. {Describe(result)}");
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        var sourcePath = Path.GetFullPath(harness.LastSourcePath);
        var diagnostic = SelectBytesDiagnostic(
            source, ParseDiagnosticSnapshots(result.StandardOutput), expectedCode, expectedMessage,
            marker, rangeToken, caseName);
        AssertEqual(sourcePath, Path.GetFullPath(diagnostic.File),
            $"Unexpected diagnostic source for {caseName}.");
        AssertBytesRange(source, diagnostic, marker, rangeToken);
        var sourceDirectory = Path.GetDirectoryName(sourcePath)!;
        AssertNoCompilerArtifacts(sourceDirectory);
        await AssertBytesBuildRejectedAsync(
            harness, caseName, sourcePath, expectedCode, expectedMessage, sourceDirectory);
        return diagnostic;
    }

    private static async Task<DiagnosticSnapshot> AssertBytesPackageDiagnosticAsync(
        Harness harness,
        string caseName,
        string manifest,
        IReadOnlyDictionary<string, string> sourceFiles,
        string sourceFile,
        string expectedCode,
        string expectedMessage,
        string marker,
        string rangeToken)
    {
        var packageRoot = await harness.WritePackageAsync(caseName, manifest, sourceFiles);
        if (sourceFiles.ContainsKey("tools/runner") && OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(Path.Combine(packageRoot, "tools", "runner"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        var packageLock = await harness.InvokePackageDirectoryAsync(caseName + "-lock", packageRoot, "lock");
        AssertEqual(0, packageLock.ExitCode, Describe(packageLock));
        AssertEqual(string.Empty, packageLock.StandardError, Describe(packageLock));
        var result = await harness.InvokePackageDirectoryAsync(caseName + "-check", packageRoot, "check", "--json");
        AssertTrue(result.ExitCode != 0, $"Invalid Bytes package unexpectedly checked. {Describe(result)}");
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        var diagnostic = SelectBytesDiagnostic(
            sourceFiles[sourceFile], ParseDiagnosticSnapshots(result.StandardOutput), expectedCode, expectedMessage,
            marker, rangeToken, caseName);
        var sourcePath = Path.GetFullPath(Path.Combine(packageRoot, sourceFile));
        AssertEqual(sourcePath, Path.GetFullPath(diagnostic.File), $"Unexpected diagnostic source for {caseName}.");
        AssertBytesRange(sourceFiles[sourceFile], diagnostic, marker, rangeToken);
        AssertNoCompilerArtifacts(packageRoot);
        await AssertBytesBuildRejectedAsync(
            harness, caseName, packageRoot, expectedCode, expectedMessage, packageRoot);
        return diagnostic;
    }

    private static DiagnosticSnapshot SelectBytesDiagnostic(
        string source,
        IReadOnlyList<DiagnosticSnapshot> diagnostics,
        string expectedCode,
        string expectedMessage,
        string marker,
        string rangeToken,
        string caseName)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        AssertTrue(markerIndex >= 0, $"Expected diagnostic marker '{marker}' was not found for {caseName}.");
        var tokenOffset = marker.IndexOf(rangeToken, StringComparison.Ordinal);
        AssertTrue(tokenOffset >= 0, $"Expected range token '{rangeToken}' was not found inside marker '{marker}'.");
        var start = GetLineAndColumn(source, markerIndex + tokenOffset);
        var end = GetLineAndColumn(source, markerIndex + tokenOffset + rangeToken.Length);
        var matching = diagnostics.Where(item => item.Code == expectedCode
            && item.Message == expectedMessage
            && item.StartLine == start.Line
            && item.StartColumn == start.Column
            && item.EndLine == end.Line
            && item.EndColumn == end.Column).ToArray();
        AssertEqual(1, matching.Length,
            $"Expected exactly one {expectedCode} diagnostic with the expected message and range for {caseName}; got {matching.Length}. " +
            $"Actual diagnostics: {string.Join(" | ", diagnostics.Select(item => $"{item.Code}: {item.Message} at {item.StartLine}:{item.StartColumn}-{item.EndLine}:{item.EndColumn}"))}");
        return matching[0];
    }

    private static async Task AssertBytesBuildRejectedAsync(
        Harness harness,
        string caseName,
        string target,
        string expectedCode,
        string expectedMessage,
        string artifactRoot)
    {
        var result = Directory.Exists(target)
            ? await harness.InvokePackageDirectoryAsync(caseName + "-build", target, "build")
            : await harness.InvokeFileAsync(caseName + "-build", target, "build");
        AssertTrue(result.ExitCode != 0, $"Invalid Bytes source unexpectedly built for {caseName}. {Describe(result)}");
        var output = result.StandardOutput + result.StandardError;
        AssertTrue(output.Contains(expectedCode, StringComparison.Ordinal),
            $"A rejected Bytes build must report {expectedCode} for {caseName}. {Describe(result)}");
        AssertTrue(output.Contains(expectedMessage, StringComparison.Ordinal),
            $"A rejected Bytes build must retain the expected diagnostic message for {caseName}. {Describe(result)}");
        AssertTrue(!output.Contains("Built executable: ", StringComparison.Ordinal)
            && !output.Contains("Built library: ", StringComparison.Ordinal),
            $"A rejected Bytes build must not announce a built artifact for {caseName}. {Describe(result)}");
        AssertNoCompilerArtifacts(artifactRoot);
    }

    private static void AssertBytesRange(string source, DiagnosticSnapshot diagnostic, string marker, string rangeToken)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        AssertTrue(markerIndex >= 0, $"Expected diagnostic marker '{marker}' was not found.");
        var tokenOffset = marker.IndexOf(rangeToken, StringComparison.Ordinal);
        AssertTrue(tokenOffset >= 0, $"Expected range token '{rangeToken}' was not found inside marker '{marker}'.");
        var start = GetLineAndColumn(source, markerIndex + tokenOffset);
        var end = GetLineAndColumn(source, markerIndex + tokenOffset + rangeToken.Length);
        AssertEqual(start.Line, diagnostic.StartLine, $"Unexpected diagnostic start line for {rangeToken}.");
        AssertEqual(start.Column, diagnostic.StartColumn, $"Unexpected diagnostic start column for {rangeToken}.");
        AssertEqual(end.Line, diagnostic.EndLine, $"Unexpected diagnostic end line for {rangeToken}.");
        AssertEqual(end.Column, diagnostic.EndColumn, $"Unexpected diagnostic end column for {rangeToken}.");
    }

    private static void AssertNoCompilerArtifacts(string root)
    {
        var artifactDirectories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is "bin" or "obj" or "artifacts").ToArray();
        AssertEqual(0, artifactDirectories.Length,
            $"Checking a Bytes rejection must not emit build directories: [{string.Join(", ", artifactDirectories)}].");
        var artifactFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".dll" or ".exe" or ".pdb"
                || Path.GetFileName(path) is "build-receipt.json" or "command-schema.json" or "Generated.cs")
            .ToArray();
        AssertEqual(0, artifactFiles.Length,
            $"Checking a Bytes rejection must not emit build artifacts: [{string.Join(", ", artifactFiles)}].");
    }
}
