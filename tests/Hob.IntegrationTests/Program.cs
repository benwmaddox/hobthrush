using System.Diagnostics;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Sockets;

return await IntegrationTests.RunAsync();

internal static partial class IntegrationTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AotPublishTimeout = TimeSpan.FromMinutes(10);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int NativeKill(int processId, int signal);

    private static int KillWithSignal(int processId, int signal) => NativeKill(processId, signal);

    public static async Task<int> RunAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            Console.Error.WriteLine("Could not locate hobthrush.slnx and src/Hob/Hob.csproj.");
            return 2;
        }

        var compilerDll = Path.Combine(repositoryRoot, "src", "Hob", "bin", "Release", "net10.0", "hob.dll");
        if (!File.Exists(compilerDll))
        {
            Console.Error.WriteLine($"Compiler build not found: {compilerDll}");
            Console.Error.WriteLine("Build the solution before running the integration harness.");
            return 2;
        }

        var dotnet = GetDotnetPath(repositoryRoot);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "hob-integration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        var harness = new Harness(repositoryRoot, compilerDll, dotnet, temporaryRoot);
        var cases = new (string Name, Func<Harness, Task> Run)[]
        {
            ("pure example prints exactly 50", TestPureExample),
            ("valid JSON checks return an empty diagnostics array", TestJsonValidProgram),
            ("JSON diagnostics have a stable schema, range, and failing exit", TestJsonDiagnostics),
            ("primitive main values preserve exact output", TestPrimitiveMainOutput),
            ("comparison precedence and all comparison operators execute", TestComparisonAndControlFlow),
            ("immutable values use recursive structural equality without exposing identity", TestStructuralEquality),
            ("immediately invoked lambdas capture immutable values and reject unsafe captures", TestLambdaCaptures),
            ("if conditions, operand types, returns, and scopes are checked", TestInvalidControlFlow),
            ("Text length counts Unicode scalars and trim removes Unicode whitespace", TestUnicodeTextOperations),
            ("lists infer generic items, append immutably, split exactly, and preserve iteration order", TestListRuntime),
            ("immutable maps preserve snapshots, generic operations, nested values, and ordinal keys in managed and NativeAOT runtimes", TestMapRuntime),
            ("non-Text Map keys are ordered, persistent, structurally equal, and preserve API/audit shapes in managed and NativeAOT runtimes", TestNonTextMapKeys),
            ("list loops, immutable assignment, effects, and resource escapes have exact diagnostics", TestListDiagnostics),
            ("union payload matching executes", TestUnionMatchOutput),
            ("Option and Result values require exhaustive typed matches", TestOptionResult),
            ("struct constructors support nested and chained field reads", TestStructValues),
            ("struct values compose with Option, Result, and unions", TestStructWrappers),
            ("generic immutable structs substitute fields, compare structurally, and run in managed and NativeAOT builds", TestGenericStructs),
            ("generic tagged unions substitute payloads, match exhaustively, compare structurally, and run in managed and NativeAOT builds", TestGenericUnions),
            ("nominal newtypes preserve boundaries, traits, resources, and current metadata", TestNominalNewtypes),
            ("immutable Bytes preserve octets, snapshots, equality, and bounds", TestImmutableBytes),
            ("wide integers enforce literal, type, and checked arithmetic boundaries", TestWideIntegers),
            ("named integer arithmetic returns checked errors or saturates and wraps at every width", TestIntegerArithmeticModes),
            ("f64 literals and IEEE arithmetic preserve nested value semantics in managed and NativeAOT builds", TestF64Acceptance),
            ("Unit is one immutable value across calls and generic storage", TestUnitAcceptance),
            ("Result postfix propagation evaluates once and preserves error cleanup and effects", TestResultPropagationAcceptance),
            ("static traits bind closed targets, forward ordered witnesses, and detect recursive obligations", TestStaticTraits),
            ("forward and guarded structs work, including empty library builds", TestForwardAndGuardedRecursion),
            ("direct and mutual struct field cycles are rejected", TestStructCycles),
            ("struct field initializers and reads are checked", TestStructFieldDiagnostics),
            ("duplicate and reserved struct names are rejected", TestStructDeclarationNames),
            ("contextual keywords are identifiers only in their grammar contexts", TestContextualIdentifiers),
            ("async functions support contextual names, generic calls, and recursive awaits", TestAsyncManagedRuntime),
            ("await diagnostics identify invalid targets without cascades", TestAsyncDiagnostics),
            ("static GET and POST routes produce checked route IR", TestStaticRouteDeclarations),
            ("route signatures, mappings, codecs, names, and placement are checked", TestRouteContractDiagnostics),
            ("typed route bindings preserve path, query, request-body, and capability handler order", TestRouteBindingCompiler),
            ("route templates reject malformed, ambiguous, unsupported, and misbound inputs", TestRouteBindingDiagnostics),
            ("typed route bindings strictly decode values through a live ASP.NET host", TestRouteBindingRuntime),
            ("opaque Html signatures build as managed libraries", TestHtmlManagedLibraryBuild),
            ("public APIs reject nested private struct types", TestStructVisibility),
            ("deep field chains produce a structured diagnostic", TestDeepStructFieldChain),
            ("type mismatches fail before generated code", TestTypeMismatchContexts),
            ("match validation reports omissions, duplicates, and payload arity", TestInvalidMatches),
            ("null is rejected as both an expression and identifier", TestNull),
            ("deep nesting produces a structured diagnostic within the timeout", TestDeepNesting),
            ("10,000-term additive chains fail with an expression-depth diagnostic", TestLongBinaryChain),
            ("signed i32 literals and unary negation are checked", TestSignedI32),
            ("checked i32 overflow exits through the generic runtime fault contract", TestCheckedOverflow),
            ("library build writes a durable DLL without a main function", TestLibraryBuild),
            ("effect annotations are closed upper bounds and enforce FsRead capabilities", TestEffectAnnotationsAndCapabilities),
            ("direct, transitive, recursive effects use deterministic shortest paths", TestEffectInferencePaths),
            ("inspect effects reports deterministic compiler-derived paths and trusted boundaries", TestInspectEffects),
            ("async FsRead calls retain effect paths and trusted adapter provenance", TestAsyncEffects),
            ("HttpClient checks, manifests, bounded loopback responses, and cancellation", TestHttpClientContracts),
            ("HttpClient maps bounded loopback responses and host cancellation", TestHttpClientRuntimeAdapter),
            ("inspect effects reports SQLite capabilities and trusted adapters", TestSqliteInspectEffects),
            ("inspect api exports a deterministic source-facing package graph", TestInspectApi),
            ("inspect api projects checked web routes and database capabilities", TestInspectApiWebRoutes),
            ("inspect graph reports a deterministic full checked package and call graph", TestInspectGraph),
            ("audit reports a portable package graph and structured failures", TestAuditPackage),
            ("qualified calls carry effects into exact JSON diagnostics", TestQualifiedEffects),
            ("FsError requires an exhaustive typed match", TestFsErrorExhaustiveness),
            ("effectful FsRead libraries build as managed DLLs", TestEffectfulLibraryBuild),
            ("async FsRead adapter maps results and honors pre-canceled tokens", TestAsyncFsReadAdapter),
            ("async FsWrite signatures, effects, and trusted reports are checked", TestAsyncFsWriteSemantics),
            ("async FsWrite adapter maps strict text and cancellation runtime outcomes", TestAsyncFsWriteRuntimeAdapter),
            ("async FsWrite CLI publishes managed and NativeAOT command schemas and writes", TestAsyncFsWriteCliRuntime),
            ("managed FsWrite maps strict UTF-8 writes and filesystem errors", TestFsWriteManagedLibrary),
            ("managed build receipts bind checked inputs and artifact bytes", TestStandaloneBuildReceipt),
            ("build receipts list successful compiler and build stages and omit failed builds", TestBuildReceiptPerformedChecks),
            ("CLI capability grants including FsWrite are validated and included in dependency lock freshness", TestCliCapabilityManifestAndLock),
            ("same-package CLI package checks, builds, and runs qualified public values", TestPackageCliRoundTrip),
            ("typed CLI commands generate deterministic schema and parse application arguments", TestTypedCliCommandRuntime),
            ("async CLI handlers await FsRead results and format typed failures", TestAsyncCliCommandRuntime),
            ("FsWrite CLI grants, reports, receipts, and web route projections are checked", TestFsWritePackageContracts),
            ("typed startup config manifests, effects, reports, and redaction are checked", TestConfigManifestAndReports),
            ("secret-free config example is deterministic, escaped, and never reads runtime secrets", TestConfigExample),
            ("CLI config defaults, empty values, explicit secret reveal, logging, and NativeAOT are checked", TestConfigCliRuntime),
            ("web config and logger injection work in an async route handler", TestConfigWebAsyncRuntime),
            ("clock.read capability is bounded, granted, reported, and injected in CLI and web hosts", TestClockCapability),
            ("pinned process manifests, effects, reports, receipts, and locks are checked", TestProcessManifestAndReports),
            ("CLI ProcessRunner preserves literal arguments, bounds I/O, isolates child context, and publishes to NativeAOT", TestProcessRunnerManagedAndAot),
            ("typed CLI command identifiers remain contextual", TestCommandContextualIdentifiers),
            ("typed CLI declarations validate entries, signatures, and parser spans", TestTypedCliCommandDiagnostics),
            ("package modules keep identically named private types isolated", TestPackagePrivateNameIsolation),
            ("private struct and union access paths report package visibility", TestQualifiedPrivateTypeAccessPaths),
            ("qualified references enforce package visibility and resolution", TestPackageQualifiedReferenceDiagnostics),
            ("package manifest schema is strict and reports JSON locations", TestPackageManifestDiagnostics),
            ("package module paths match their source headers", TestPackageModulePathDiagnostic),
            ("CLI entry module and main signature rules are enforced", TestPackageEntryPointDiagnostics),
            ("only the declared package entry module selects main", TestPackageEntrySelection),
            ("qualified union variants participate in exhaustive matching", TestPackageQualifiedUnionExhaustiveness),
            ("same-package declarations use qualified cross-module references", TestPackageQualifiedReferencesCrossModules),
            ("generic structs construct and substitute fields across dependency aliases", TestGenericStructDependency),
            ("explicit generic function arguments preserve context, witnesses, effects, and single evaluation", TestExplicitGenericFunctionArguments),
            ("library packages build as managed libraries", TestPackageLibraryBuild),
            ("new and add workflows create projects and resolve pinned Git dependencies offline", TestProjectWorkflow),
            ("path dependency locks are portable, stable, and required for package commands", TestPathDependencyLockLifecycle),
            ("maintained generic source identity remains current across locks and cached Git inputs", TestGenericSourceIdentityLifecycle),
            ("qualified HTTP effects cross dependencies and require root authority", TestQualifiedHttpDependencyAuthority),
            ("Secret<Text> stays redacted across CLI exit classes", TestSecretCanaryExitMatrix),
            ("managed adapter packages lock and report closed provenance, execute, reject drift, and publish to NativeAOT", TestManagedAdapterPackages),
            ("dependency graphs reject cycles, missing manifests, non-libraries, and duplicate identities", TestDependencyGraphDiagnostics),
            ("dependency aliases enforce direct visibility and preserve module identity", TestDependencyAliasResolution),
            ("dependency references enforce public and existing symbols", TestDependencyReferenceDiagnostics),
            ("dependency source roots and reparse paths stay inside package boundaries", TestDependencyFilesystemSafety),
            ("package NativeAOT arguments are validated", TestPackageAotCommandValidation),
            ("maintained package example runs with exact output", TestMaintainedPackageExample),
            ("maintained web package serves typed routes with bounded request handling", TestMaintainedWebExample),
            ("configurable web request limits and request deadlines", TestConfigurableWebRequestLimits),
            ("SQLite transactions commit once and roll back on scope exit and early return", TestSqliteTransactions),
            ("SQLite row decoding and failures are enforced at runtime", TestSqliteRowDecoding),
            ("SQLite manifest paths, grants, and generated dependency are validated", TestSqlitePackageContract),
            ("SQLite library operations build without web database configuration", TestSqliteLibraryBuild),
            ("maintained scan CLI awaits FsRead and handles typed file and normalization results", TestScanCliExample),
            ("Text validation package builds and qualified generic calls specialize correctly", TestTextValidationExample),
            ("language tests run the text validation suite with exact output", TestManagedLanguageTests),
            ("hob fmt is deterministic and check mode preserves source bytes", TestSourceFormatter),
            ("language tests report failures and continue, including empty and multi-module suites", TestManagedLanguageTestOutcomes),
            ("language tests validate assertions, dependency selection, and locks", TestManagedLanguageTestPackageRules),
            ("agent-facing docs track active fixtures, emitted diagnostics, grammar, and commands", TestSpecificationDriftOracle),
            ("generic inference limits and generic main entry selection are diagnosed", TestGenericFunctionRestrictions),
            ("NativeAOT command validation returns build-target diagnostics", TestAotCommandValidation),
            ("NativeAOT rejects library sources before publishing", TestAotLibraryRejected),
            ("NativeAOT publishes and runs the current-host file executable", TestAotPublishAndRun),
            ("NativeAOT publishes and runs the current-host package executable", TestPackageAotPublishAndRun),
            ("NativeAOT publishes and runs a typed command with its schema", TestCommandAotPublishAndRun),
            ("NativeAOT publishes and runs the maintained FsRead scan CLI", TestScanCliAotPublishAndRun),
            ("invalid main signatures receive an entrypoint diagnostic", TestInvalidEntrypoint),
            ("HOB_DOTNET launch failures become process diagnostics", TestDotnetLaunchFailure),
            ("concurrent runs keep their generated outputs isolated", TestParallelRuns)
        };
        AssertEqual(132, cases.Length, "The integration registry count should match the current accepted suite.");

        // Set HOB_INTEGRATION_TEST_FILTER to a case-insensitive test-name substring while iterating on one case.
        var filter = Environment.GetEnvironmentVariable("HOB_INTEGRATION_TEST_FILTER");
        var selectedCases = string.IsNullOrWhiteSpace(filter)
            ? cases
            : cases.Where(test => test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selectedCases.Length == 0)
        {
            Console.Error.WriteLine($"No integration test matched HOB_INTEGRATION_TEST_FILTER='{filter}'.");
            Directory.Delete(temporaryRoot, recursive: true);
            return 2;
        }

        var failures = 0;
        var skipped = 0;
        try
        {
            foreach (var test in selectedCases)
            {
                try
                {
                    await test.Run(harness);
                    Console.WriteLine($"PASS {test.Name}");
                }
                catch (IntegrationTestSkippedException exception)
                {
                    skipped++;
                    Console.WriteLine($"SKIP {test.Name}: {exception.Message}");
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine($"Could not remove temporary integration files at {temporaryRoot}: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                Console.Error.WriteLine($"Could not remove temporary integration files at {temporaryRoot}: {exception.Message}");
            }
        }

        Console.WriteLine($"{selectedCases.Length - failures - skipped} passed, {skipped} skipped, {failures} failed");
        return failures == 0 ? 0 : 1;
    }

    private static async Task TestPureExample(Harness harness)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "examples", "pure", "src", "main.hob"));
        var result = await harness.InvokeAsync("pure-example", "run", source);
        AssertEqual(0, result.ExitCode, Describe(result));
        AssertEqual("50" + Environment.NewLine, result.StandardOutput, Describe(result));
    }

    private static async Task TestJsonDiagnostics(Harness harness)
    {
        const string source = "module harness;\n"
            + "pub fn main() -> i32 effects {} {\n"
            + "    return 1\n"
            + "}\n";
        var result = await harness.InvokeAsync("json-diagnostics", "check", source, "--json");

        AssertEqual(1, result.ExitCode, Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        AssertEqual(JsonValueKind.Object, root.ValueKind, "Diagnostic JSON root must be an object.");
        AssertEqual(1, root.GetProperty("schemaVersion").GetInt32(), "Unexpected diagnostic schema version.");
        var diagnostics = root.GetProperty("diagnostics");
        AssertEqual(JsonValueKind.Array, diagnostics.ValueKind, "diagnostics must be an array.");
        AssertEqual(1, diagnostics.GetArrayLength(), "Expected exactly one parser diagnostic.");

        var diagnostic = diagnostics[0];
        AssertEqual("E_SYNTAX", diagnostic.GetProperty("code").GetString(), "Unexpected diagnostic code.");
        AssertEqual("error", diagnostic.GetProperty("severity").GetString(), "Unexpected diagnostic severity.");
        AssertTrue(!string.IsNullOrWhiteSpace(diagnostic.GetProperty("message").GetString()), "Diagnostic message must be present.");
        AssertEqual(harness.LastSourcePath, diagnostic.GetProperty("file").GetString(), "Diagnostic should identify the temporary source file.");

        var range = diagnostic.GetProperty("range");
        AssertEqual(4, range.GetProperty("startLine").GetInt32(), "Unexpected diagnostic start line.");
        AssertEqual(1, range.GetProperty("startColumn").GetInt32(), "Unexpected diagnostic start column.");
        AssertEqual(4, range.GetProperty("endLine").GetInt32(), "Unexpected diagnostic end line.");
        AssertEqual(2, range.GetProperty("endColumn").GetInt32(), "Unexpected diagnostic end column.");
    }

    private static async Task TestJsonValidProgram(Harness harness)
    {
        const string source = "module harness::valid_json;\n"
            + "pub fn main() -> i32 effects {} { return 1; }\n";
        var result = await harness.InvokeAsync("json-valid", "check", source, "--json");

        AssertEqual(0, result.ExitCode, Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        using var document = JsonDocument.Parse(result.StandardOutput);
        AssertEqual(1, document.RootElement.GetProperty("schemaVersion").GetInt32(), "Unexpected diagnostic schema version.");
        AssertEqual(0, document.RootElement.GetProperty("diagnostics").GetArrayLength(), "Valid source should produce an empty diagnostics array.");
    }

    private static async Task TestPrimitiveMainOutput(Harness harness)
    {
        const string boolSource = "module harness::bool_value;\n"
            + "pub fn echo(value: bool) -> bool effects {} { return value; }\n"
            + "pub fn main() -> bool effects {} { let answer: bool = self::harness::bool_value::echo(true); return answer; }\n";
        const string falseSource = "module harness::false_value;\n"
            + "pub fn main() -> bool effects {} { return false; }\n";
        const string textSource = """
            module harness::text_value;
            pub fn echo(value: Text) -> Text effects {} { return value; }
            pub fn main() -> Text effects {} {
                let answer: Text = self::harness::text_value::echo("quote: \" slash: \\ line1\nline2 λ 😀");
                return answer;
            }
            """;

        var boolResult = await harness.InvokeAsync("bool-output", "run", boolSource);
        AssertRunOutput("true" + Environment.NewLine, boolResult);
        var falseResult = await harness.InvokeAsync("false-output", "run", falseSource);
        AssertRunOutput("false" + Environment.NewLine, falseResult);
        var textResult = await harness.InvokeAsync("text-output", "run", textSource);
        AssertRunOutput("quote: \" slash: \\ line1\nline2 λ 😀" + Environment.NewLine, textResult);
    }

    private static async Task TestComparisonAndControlFlow(Harness harness)
    {
        const string source = """
            module harness::comparisons;
            pub fn choose(enabled: bool, value: i32) -> i32 effects {} {
                if enabled {
                    let doubled: i32 = value * 2;
                    if value > 0 {
                        return doubled;
                    } else {
                        return value;
                    }
                } else {
                    return 0;
                }
            }
            pub fn main() -> i32 effects {} {
                if 1 + 2 * 3 < 8 == true {
                    if 8 != 9 {
                        if 2 < 3 {
                            if 3 <= 3 {
                                if 4 > 3 {
                                    if 4 >= 4 {
                                        return self::harness::comparisons::choose(true, 21);
                                    } else {
                                        return 5;
                                    }
                                } else {
                                    return 6;
                                }
                            } else {
                                return 7;
                            }
                        } else {
                            return 8;
                        }
                    } else {
                        return 9;
                    }
                } else {
                    return 10;
                }
            }
            """;

        AssertRunOutput("42" + Environment.NewLine,
            await harness.InvokeAsync("comparisons-and-control-flow", "run", source));

        const string memberCallSource = """
            module harness::control_member_call;
            pub fn read_if_empty(fs: FsRead, path: Text) -> Result<Text, FsError> effects { fs.read } {
                let loaded: Result<Text, FsError> = fs.read_text(path);
                if path.length == 0 {
                    return loaded;
                } else {
                    return loaded;
                }
            }
            """;
        var memberCallCheck = await harness.InvokeAsync(
            "control-flow-fsread-member-call", "check", memberCallSource, "--json");
        AssertEqual(0, memberCallCheck.ExitCode, Describe(memberCallCheck));
        AssertEqual(0, ParseDiagnosticSnapshots(memberCallCheck.StandardOutput).Length,
            "The FsRead member call must remain valid alongside if statements.");
    }

    private static async Task TestStructuralEquality(Harness harness)
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(harness.RepositoryRoot, "fixtures", "58-structural-equality.hob"));
        var result = await harness.InvokeAsync("structural-equality", "run", source);
        AssertRunOutput("0" + Environment.NewLine, result);
    }

    private static async Task TestLambdaCaptures(Harness harness)
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(harness.RepositoryRoot, "fixtures", "61-lambda-value-capture.hob"));
        var result = await harness.InvokeAsync("lambda-value-capture", "run", source);
        AssertRunOutput("42" + Environment.NewLine, result);

        var mutableCapture = await File.ReadAllTextAsync(
            Path.Combine(harness.RepositoryRoot, "fixtures", "62-lambda-var-capture-rejected.hob"));
        await ExpectDiagnosticsAsync(
            harness,
            "lambda-var-capture",
            mutableCapture,
            "E_CLOSURE_CAPTURE_MUTABLE");

        var resourceCapture = await File.ReadAllTextAsync(
            Path.Combine(harness.RepositoryRoot, "fixtures", "63-lambda-resource-capture-rejected.hob"));
        await ExpectDiagnosticsAsync(
            harness,
            "lambda-resource-capture",
            resourceCapture,
            "E_RESOURCE_ESCAPE");

        var genericCapture = await File.ReadAllTextAsync(
            Path.Combine(harness.RepositoryRoot, "fixtures", "64-lambda-generic-capture-rejected.hob"));
        await ExpectDiagnosticsAsync(
            harness,
            "lambda-generic-capture",
            genericCapture,
            "E_UNSUPPORTED");
    }

    private static async Task TestInvalidControlFlow(Harness harness)
    {
        const string nonBooleanCondition = """
            module harness::non_boolean_condition;
            pub fn main() -> i32 effects {} {
                if 1 { return 1; } else { return 0; }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "non-boolean-condition", nonBooleanCondition, "E_TYPE_MISMATCH");

        const string mismatchedOrderingOperands = """
            module harness::mismatched_ordering;
            pub fn main() -> i32 effects {} {
                if 1 < false { return 1; } else { return 0; }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "mismatched-ordering-operands", mismatchedOrderingOperands, "E_TYPE_MISMATCH");

        const string mismatchedEqualityOperands = """
            module harness::mismatched_equality;
            pub fn main() -> i32 effects {} {
                let same: bool = 1 == "1";
                if same { return 1; } else { return 0; }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "mismatched-equality-operands", mismatchedEqualityOperands, "E_TYPE_MISMATCH");

        const string missingReturn = """
            module harness::if_missing_return;
            pub fn choose(enabled: bool) -> i32 effects {} {
                if enabled { return 1; }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "if-missing-return", missingReturn, "E_TYPE_MISMATCH");

        const string branchLocalEscape = """
            module harness::branch_local_escape;
            pub fn main() -> i32 effects {} {
                if true { let branch_value: i32 = 1; } else { }
                return branch_value;
            }
            """;
        await ExpectDiagnosticsAsync(harness, "branch-local-escape", branchLocalEscape, "E_NAME_UNRESOLVED");

        const string unreachableStatement = """
            module harness::unreachable_statement;
            pub fn main() -> i32 effects {} {
                if true { return 1; } else { return 2; }
                return 3;
            }
            """;
        await ExpectDiagnosticsAsync(harness, "unreachable-after-returning-if", unreachableStatement, "E_UNREACHABLE");
    }

    private static async Task TestUnicodeTextOperations(Harness harness)
    {
        const string scalarLengthSource = """
            module harness::scalar_length;
            pub fn main() -> i32 effects {} { return "😀".length; }
            """;
        AssertRunOutput("1" + Environment.NewLine,
            await harness.InvokeAsync("unicode-scalar-length", "run", scalarLengthSource));

        var whitespace = "\u00A0\u3000hello 😀\u3000\u00A0";
        var trimSource = $$"""
            module harness::unicode_trim;
            pub fn main() -> Text effects {} { return "{{whitespace}}".trim(); }
            """;
        AssertRunOutput("hello 😀" + Environment.NewLine,
            await harness.InvokeAsync("unicode-whitespace-trim", "run", trimSource));
    }

    private static async Task TestListRuntime(Harness harness)
    {
        const string source = """
            module harness::collections_runtime;
            fn first<T>(items: List<T>) -> Option<T> effects {} {
                for item in items {
                    return Some(item);
                }
                return None;
            }
            fn intAt(items: List<i32>, index: i32, expected: i32) -> bool effects {} {
                return match items.get(index) {
                    Some(value) => value == expected,
                    None => false,
                };
            }
            fn textAt(items: List<Text>, index: i32, expected: Text) -> bool effects {} {
                return match items.get(index) {
                    Some(value) => value == expected,
                    None => false,
                };
            }
            fn noInteger(value: Option<i32>) -> bool effects {} {
                return match value {
                    Some(_) => false,
                    None => true,
                };
            }
            fn firstIntegerIs(items: List<i32>, expected: i32) -> bool effects {} {
                return match self::harness::collections_runtime::first(items) {
                    Some(value) => value == expected,
                    None => false,
                };
            }
            fn firstTextIs(items: List<Text>, expected: Text) -> bool effects {} {
                return match self::harness::collections_runtime::first(items) {
                    Some(value) => value == expected,
                    None => false,
                };
            }
            fn firstIntegerIsNone(items: List<i32>) -> bool effects {} {
                return match self::harness::collections_runtime::first(items) {
                    Some(_) => false,
                    None => true,
                };
            }
            fn splitPreservesFields() -> bool effects {} {
                let fields: List<Text> = ",a,,b,".split(",");
                if fields.length != 5 { return false; }
                if self::harness::collections_runtime::textAt(fields, 0, "") { } else { return false; }
                if self::harness::collections_runtime::textAt(fields, 1, "a") { } else { return false; }
                if self::harness::collections_runtime::textAt(fields, 2, "") { } else { return false; }
                if self::harness::collections_runtime::textAt(fields, 3, "b") { } else { return false; }
                if self::harness::collections_runtime::textAt(fields, 4, "") { } else { return false; }
                return true;
            }
            fn emptySeparatorIsSingleton() -> bool effects {} {
                let fields: List<Text> = "abc".split("");
                if fields.length == 1 {
                    return self::harness::collections_runtime::textAt(fields, 0, "abc");
                } else {
                    return false;
                }
            }
            fn foldInIterationOrder(items: List<i32>) -> i32 effects {} {
                var encoded: i32 = 0;
                for item in items {
                    encoded = encoded * 10 + item;
                }
                return encoded;
            }
            pub fn main() -> i32 effects {} {
                let emptyNumbers: List<i32> = [];
                let numbers: List<i32> = [3, 1, 4];
                let words: List<Text> = ["left", "right"];

                if emptyNumbers.length == 0 { } else { return 1; }
                if numbers.length == 3 { } else { return 2; }
                if self::harness::collections_runtime::intAt(numbers, 0, 3) { } else { return 3; }
                if self::harness::collections_runtime::intAt(numbers, 1, 1) { } else { return 4; }
                if self::harness::collections_runtime::intAt(numbers, 2, 4) { } else { return 5; }
                if self::harness::collections_runtime::noInteger(numbers.get(-1)) { } else { return 6; }
                if self::harness::collections_runtime::noInteger(numbers.get(9)) { } else { return 7; }

                var expanded: List<i32> = numbers.append(2);
                expanded = expanded.append(5);
                if expanded.length == 5 { } else { return 8; }
                if self::harness::collections_runtime::intAt(expanded, 3, 2) { } else { return 9; }
                if self::harness::collections_runtime::intAt(expanded, 4, 5) { } else { return 10; }
                if numbers.length == 3 { } else { return 11; }
                if self::harness::collections_runtime::intAt(numbers, 2, 4) { } else { return 12; }

                if self::harness::collections_runtime::firstIntegerIs(numbers, 3) { } else { return 13; }
                if self::harness::collections_runtime::firstTextIs(words, "left") { } else { return 14; }
                if self::harness::collections_runtime::firstIntegerIsNone(emptyNumbers) { } else { return 15; }
                if self::harness::collections_runtime::textAt(words, 1, "right") { } else { return 16; }
                if self::harness::collections_runtime::splitPreservesFields() { } else { return 17; }
                if self::harness::collections_runtime::emptySeparatorIsSingleton() { } else { return 18; }
                if self::harness::collections_runtime::foldInIterationOrder([1, 2, 3]) == 123 { } else { return 19; }
                return 42;
            }
            """;

        var result = await harness.InvokeAsync("collections-runtime", "run", source);
        AssertRunOutput("42" + Environment.NewLine, result);
    }

    private static async Task TestMapRuntime(Harness harness)
    {
        const string source = """
            module harness::map_runtime;

            pub struct Map { value: i32 }

            fn lookup<T>(values: Map<Text, T>, key: Text) -> Option<T> effects {} {
                return values.get(key);
            }
            fn store<T>(values: Map<Text, T>, key: Text, value: T) -> Map<Text, T> effects {} {
                return values.set(key, value);
            }
            fn hasInt(values: Map<Text, i32>, key: Text, expected: i32) -> bool effects {} {
                return match self::harness::map_runtime::lookup(values, key) {
                    Some(value) => value == expected,
                    None => false,
                };
            }
            fn lacksKey(values: Map<Text, i32>, key: Text) -> bool effects {} {
                return match self::harness::map_runtime::lookup(values, key) {
                    Some(_) => false,
                    None => true,
                };
            }
            fn textAt(values: List<Text>, index: i32, expected: Text) -> bool effects {} {
                return match values.get(index) {
                    Some(value) => value == expected,
                    None => false,
                };
            }
            fn nestedValueIs(values: Map<Text, Map<Text, i32>>, key: Text, innerKey: Text, expected: i32) -> bool effects {} {
                return match values.get(key) {
                    Some(inner) => match inner.get(innerKey) {
                        Some(value) => value == expected,
                        None => false,
                    },
                    None => false,
                };
            }
            fn localMapShadow() -> bool effects {} {
                let Map: Map<Text, i32> = Map.empty();
                return Map.length == 0;
            }
            fn qualifiedMapCollision() -> bool effects {} {
                let user: self::harness::map_runtime::Map = self::harness::map_runtime::Map { value: 7 };
                return user.value == 7;
            }

            pub fn main() -> i32 effects {} {
                let empty: Map<Text, i32> = Map.empty();
                if empty.length == 0 { } else { return 1; }

                let original: Map<Text, i32> = empty.set("shared", 1);
                let overwritten: Map<Text, i32> = original.set("shared", 2);
                if original.length == 1 { } else { return 2; }
                if self::harness::map_runtime::hasInt(original, "shared", 1) { } else { return 3; }
                if overwritten.length == 1 { } else { return 4; }
                if self::harness::map_runtime::hasInt(overwritten, "shared", 2) { } else { return 5; }
                if self::harness::map_runtime::lacksKey(empty, "shared") { } else { return 6; }

                var rebound: Map<Text, i32> = original;
                rebound = self::harness::map_runtime::store(rebound, "shared", 3);
                rebound = self::harness::map_runtime::store(rebound, "new", 4);
                if rebound.length == 2 { } else { return 7; }
                if self::harness::map_runtime::hasInt(rebound, "shared", 3) { } else { return 8; }
                if self::harness::map_runtime::hasInt(rebound, "new", 4) { } else { return 9; }
                if self::harness::map_runtime::hasInt(original, "shared", 1) { } else { return 10; }
                if self::harness::map_runtime::lacksKey(rebound, "missing") { } else { return 11; }

                let inner: Map<Text, i32> = Map.empty();
                let innerWithValue: Map<Text, i32> = inner.set("answer", 42);
                let outer: Map<Text, Map<Text, i32>> = Map.empty();
                let nested: Map<Text, Map<Text, i32>> = outer.set("child", innerWithValue);
                if self::harness::map_runtime::nestedValueIs(nested, "child", "answer", 42) { } else { return 12; }

                let composed: Text = "é";
                let decomposed: Text = "é";
                var ordered: Map<Text, i32> = Map.empty();
                ordered = ordered.set(composed, 1);
                ordered = ordered.set("a", 2);
                ordered = ordered.set(decomposed, 3);
                ordered = ordered.set("A", 4);
                ordered = ordered.set("z", 5);
                let keys: List<Text> = ordered.keys();
                if keys.length == 5 { } else { return 13; }
                if self::harness::map_runtime::textAt(keys, 0, "A") { } else { return 14; }
                if self::harness::map_runtime::textAt(keys, 1, "a") { } else { return 15; }
                if self::harness::map_runtime::textAt(keys, 2, decomposed) { } else { return 16; }
                if self::harness::map_runtime::textAt(keys, 3, "z") { } else { return 17; }
                if self::harness::map_runtime::textAt(keys, 4, composed) { } else { return 18; }
                if self::harness::map_runtime::hasInt(ordered, composed, 1) { } else { return 19; }
                if self::harness::map_runtime::hasInt(ordered, decomposed, 3) { } else { return 20; }
                if self::harness::map_runtime::localMapShadow() { } else { return 21; }
                if self::harness::map_runtime::qualifiedMapCollision() { } else { return 22; }
                return 42;
            }
            """;

        var managed = await harness.InvokeAsync("immutable-map-managed", "run", source);
        AssertRunOutput("42" + Environment.NewLine, managed);

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("The NativeAOT map test requires Windows x64 or Linux x64.");

        var build = await harness.InvokeWithTimeoutAsync(
            "immutable-map-aot", "build", source, AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, build.ExitCode, Describe(build));
        const string prefix = "Built native executable: ";
        AssertTrue(build.StandardOutput.StartsWith(prefix, StringComparison.Ordinal), Describe(build));
        AssertTrue(build.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal), Describe(build));
        var executablePath = build.StandardOutput[prefix.Length..^Environment.NewLine.Length];
        AssertTrue(Path.IsPathFullyQualified(executablePath) && File.Exists(executablePath),
            $"Expected the NativeAOT map executable at {executablePath}.");
        var native = await ExecuteNativeAsync(executablePath, TimeSpan.FromSeconds(30));
        AssertRunOutput("42" + Environment.NewLine, native);
    }

    private static async Task TestListDiagnostics(Harness harness)
    {
        const string immutableSource = """
            module harness::collections_immutable;
            fn immutableLocal() -> i32 effects {} {
                let local_only: i32 = 1;
                local_only = 2;
                return local_only;
            }
            fn immutableParameter(parameter_only: i32) -> i32 effects {} {
                parameter_only = 2;
                return parameter_only;
            }
            fn immutableLoopItem() -> i32 effects {} {
                let values: List<i32> = [1];
                for loop_only in values {
                    loop_only = 2;
                }
                return 0;
            }
            """;
        var immutableDiagnostics = await ExpectDiagnosticsAsync(
            harness, "collections-immutable-assignment", immutableSource, "E_ASSIGN_IMMUTABLE");
        var immutableAssignments = immutableDiagnostics
            .Where(diagnostic => diagnostic.Code == "E_ASSIGN_IMMUTABLE").ToArray();
        AssertEqual(3, immutableAssignments.Length,
            "let locals, parameters, and loop bindings must all remain immutable.");
        AssertRangeAtToken(immutableSource,
            immutableAssignments.Single(diagnostic => diagnostic.Message.Contains("local_only", StringComparison.Ordinal)),
            "local_only", 2);
        AssertRangeAtToken(immutableSource,
            immutableAssignments.Single(diagnostic => diagnostic.Message.Contains("parameter_only", StringComparison.Ordinal)),
            "parameter_only", 2);
        AssertRangeAtToken(immutableSource,
            immutableAssignments.Single(diagnostic => diagnostic.Message.Contains("loop_only", StringComparison.Ordinal)),
            "loop_only", 2);

        const string loopEffectSource = """
            module harness::collections_loop_effect;
            pub fn effectFromLoop(fs: FsRead, values: List<i32>) -> i32 effects {} {
                for item in values {
                    let loaded: Result<Text, FsError> = fs.read_text("unused");
                }
                return 0;
            }
            """;
        var effectDiagnostics = await ExpectDiagnosticsAsync(
            harness, "collections-loop-effect", loopEffectSource, "E_EFFECT_EXCEEDED");
        var loopEffect = effectDiagnostics.Single(diagnostic => diagnostic.Code == "E_EFFECT_EXCEEDED");
        AssertRangeAtToken(loopEffectSource, loopEffect, "effectFromLoop", 1);
        AssertTrue(loopEffect.Message.Contains("fs.read_text", StringComparison.Ordinal),
            "The effect path should include the filesystem call inside the loop.");

        const string loopMissingReturnSource = """
            module harness::collections_loop_return;
            pub fn loopMaySkip(values: List<i32>) -> i32 effects {} {
                for item in values {
                    return item;
                }
            }
            """;
        var missingReturnDiagnostics = await ExpectDiagnosticsAsync(
            harness, "collections-loop-missing-return", loopMissingReturnSource, "E_TYPE_MISMATCH");
        var missingReturn = missingReturnDiagnostics.Single(diagnostic =>
            diagnostic.Code == "E_TYPE_MISMATCH" && diagnostic.Message.Contains("must end with a return", StringComparison.Ordinal));
        AssertRangeAtToken(loopMissingReturnSource, missingReturn, "loopMaySkip", 1);

        const string loopScopeSource = """
            module harness::collections_loop_scope;
            pub fn loopBindingDoesNotEscape() -> i32 effects {} {
                let values: List<i32> = [7];
                for scoped_item in values { }
                return scoped_item;
            }
            """;
        var scopeDiagnostics = await ExpectDiagnosticsAsync(
            harness, "collections-loop-scope", loopScopeSource, "E_NAME_UNRESOLVED");
        AssertRangeAtToken(loopScopeSource,
            scopeDiagnostics.Single(diagnostic => diagnostic.Code == "E_NAME_UNRESOLVED"),
            "scoped_item", 2);

        const string genericListEscapeSource = """
            module harness::collections_resource;
            fn singleton<T>(value: T) -> List<T> effects {} {
                return [value];
            }
            pub fn resourceList(fs: FsRead) -> i32 effects {} {
                return self::harness::collections_resource::singleton(fs).length;
            }
            """;
        var listEscapeDiagnostics = await ExpectDiagnosticsAsync(
            harness, "collections-generic-list-resource-escape", genericListEscapeSource, "E_RESOURCE_ESCAPE");
        AssertRangeAtToken(genericListEscapeSource,
            listEscapeDiagnostics.Single(diagnostic => diagnostic.Code == "E_RESOURCE_ESCAPE"),
            "self", 1);

        const string unionResourceListSource = """
            module harness::collections_union_resource;
            union Capability { Reader(FsRead) }
            union Envelope { Nested(self::harness::collections_union_resource::Capability) }
            union ResourceLists { Items(List<self::harness::collections_union_resource::Envelope>) }
            """;
        var unionListEscapeDiagnostics = await ExpectDiagnosticsAsync(
            harness, "collections-union-list-resource-escape", unionResourceListSource, "E_RESOURCE_ESCAPE");
        var unionListEscape = unionListEscapeDiagnostics.Single(diagnostic => diagnostic.Code == "E_RESOURCE_ESCAPE");
        AssertRangeAtToken(unionResourceListSource, unionListEscape, "List", 1);

        const string forwardStructResourceListSource = """
            module harness::collections_forward_resource;
            union ResourceLists { Items(List<self::harness::collections_forward_resource::LateCapability>) }
            struct LateCapability { database: Option<DbRead> }
            """;
        var forwardStructEscapeDiagnostics = await ExpectDiagnosticsAsync(
            harness, "collections-forward-struct-list-resource-escape", forwardStructResourceListSource,
            "E_RESOURCE_ESCAPE");
        var forwardStructEscape = forwardStructEscapeDiagnostics.Single(diagnostic => diagnostic.Code == "E_RESOURCE_ESCAPE");
        AssertRangeAtToken(forwardStructResourceListSource, forwardStructEscape, "List", 1);

        const string mutableResourceEscapeSource = """
            module harness::collections_mutable_resource;
            pub fn resourceVar(fs: FsRead) -> i32 effects {} {
                var mutable_fs: FsRead = fs;
                return 0;
            }
            """;
        var mutableEscapeDiagnostics = await ExpectDiagnosticsAsync(
            harness, "collections-mutable-resource-escape", mutableResourceEscapeSource, "E_RESOURCE_ESCAPE");
        AssertRangeAtToken(mutableResourceEscapeSource,
            mutableEscapeDiagnostics.Single(diagnostic => diagnostic.Code == "E_RESOURCE_ESCAPE"),
            "mutable_fs", 1);
    }

    private static async Task TestUnionMatchOutput(Harness harness)
    {
        const string source = """
            module harness::union_match;
            pub union Choice { Number(i32), Word(Text), Empty }
            pub fn main() -> i32 effects {} {
                let choice: self::harness::union_match::Choice = self::harness::union_match::Choice.Number(37);
                return match choice {
                    self::harness::union_match::Choice.Number(number) => number,
                    self::harness::union_match::Choice.Word(word) => 0,
                    self::harness::union_match::Choice.Empty => 0,
                };
            }
            """;
        var result = await harness.InvokeAsync("union-match", "run", source);
        AssertRunOutput("37" + Environment.NewLine, result);
    }

    private static async Task TestOptionResult(Harness harness)
    {
        const string optionSource = """
            module harness::option;
            pub fn main() -> i32 effects {} {
                let value: Option<i32> = Some(23);
                return match value {
                    Some(number) => number,
                    None => 0,
                };
            }
            """;
        const string resultSource = """
            module harness::result;
            pub fn main() -> i32 effects {} {
                let value: Result<i32, Text> = Ok(31);
                return match value {
                    Ok(number) => number,
                    Err(message) => 0,
                };
            }
            """;

        var optionResult = await harness.InvokeAsync("option-match", "run", optionSource);
        AssertRunOutput("23" + Environment.NewLine, optionResult);
        var resultResult = await harness.InvokeAsync("result-match", "run", resultSource);
        AssertRunOutput("31" + Environment.NewLine, resultResult);
    }

    private static async Task TestStructValues(Harness harness)
    {
        const string source = """
            module harness::struct_values;
            pub union Color { Red, Blue }
            pub union Choice { Yes, No }
            pub struct Container { item: self::harness::struct_values::Choice }
            pub struct Person { name: Text, age: i32 }
            pub struct Profile { owner: self::harness::struct_values::Person }
            pub struct ColorValue { Red: i32 }

            pub fn make_person(name: Text, age: i32) -> self::harness::struct_values::Person effects {} {
                return self::harness::struct_values::Person { age: age, name: name };
            }
            pub fn person_age(person: self::harness::struct_values::Person) -> i32 effects {} { return person.age; }
            pub fn red_value() -> i32 effects {} {
                let color: self::harness::struct_values::Color = self::harness::struct_values::Color.Red;
                return match color { self::harness::struct_values::Color.Red => 1, self::harness::struct_values::Color.Blue => 0, };
            }
            pub fn main() -> i32 effects {} {
                let Color: self::harness::struct_values::ColorValue = self::harness::struct_values::ColorValue { Red: 40 };
                let profile: self::harness::struct_values::Profile = self::harness::struct_values::Profile { owner: self::harness::struct_values::make_person("Ada", 37) };
                let chained: i32 = profile.owner.age;
                let call_field: i32 = self::harness::struct_values::make_person("Lin", 2).age;
                let call_argument: i32 = self::harness::struct_values::person_age(profile.owner);
                let parenthesized_value: i32 = (profile.owner).age;
                let parenthesized_constructor: i32 = (self::harness::struct_values::Person { age: 3, name: "Ada" }).age;
                let matched: i32 = match (self::harness::struct_values::Container { item: self::harness::struct_values::Choice.Yes }).item {
                    self::harness::struct_values::Choice.Yes => Color.Red,
                    self::harness::struct_values::Choice.No => 0,
                };
                return chained + call_field + call_argument + parenthesized_value
                    + parenthesized_constructor + matched + self::harness::struct_values::red_value();
            }
            """;

        var result = await harness.InvokeAsync("struct-values", "run", source);
        AssertRunOutput("157" + Environment.NewLine, result);
    }

    private static async Task TestStructWrappers(Harness harness)
    {
        const string source = """
            module harness::struct_wrappers;
            pub struct Record { value: i32 }
            pub union BoxedRecord { Present(self::harness::struct_wrappers::Record), Empty }

            pub fn option_or_default(value: Option<self::harness::struct_wrappers::Record>) -> self::harness::struct_wrappers::Record effects {} {
                return match value {
                    Some(record) => record,
                    None => self::harness::struct_wrappers::Record { value: 0 },
                };
            }
            pub fn result_or_default(value: Result<self::harness::struct_wrappers::Record, Text>) -> self::harness::struct_wrappers::Record effects {} {
                return match value {
                    Ok(record) => record,
                    Err(message) => self::harness::struct_wrappers::Record { value: 0 },
                };
            }
            pub fn main() -> i32 effects {} {
                let optional: Option<self::harness::struct_wrappers::Record> = Some(self::harness::struct_wrappers::Record { value: 23 });
                let from_option: self::harness::struct_wrappers::Record = self::harness::struct_wrappers::option_or_default(optional);
                let result: Result<self::harness::struct_wrappers::Record, Text> = Ok(from_option);
                let from_result: self::harness::struct_wrappers::Record = self::harness::struct_wrappers::result_or_default(result);
                let boxed: self::harness::struct_wrappers::BoxedRecord = self::harness::struct_wrappers::BoxedRecord.Present(from_result);
                return match boxed {
                    self::harness::struct_wrappers::BoxedRecord.Present(record) => record.value,
                    self::harness::struct_wrappers::BoxedRecord.Empty => 0,
                };
            }
            """;

        var result = await harness.InvokeAsync("struct-wrappers", "run", source);
        AssertRunOutput("23" + Environment.NewLine, result);
    }

    private static async Task TestGenericStructs(Harness harness)
    {
        const string source = """
            module harness::generic_structs;
            pub struct Box<T> { value: T }
            pub struct Pair<Left, Right> { left: Left, right: Right }
            pub struct Holder { nested: self::harness::generic_structs::Box<self::harness::generic_structs::Box<i32>> }
            pub struct Node<T> { value: T, next: Option<self::harness::generic_structs::Node<T>> }
            pub struct Flip<Left, Right> {
                left: Left,
                right: Right,
                next: Option<self::harness::generic_structs::Flip<Right, Left>>
            }
            pub struct MutualA<Left, Right> {
                child: Option<self::harness::generic_structs::MutualB<Left, Right>>,
                value: Right
            }
            pub struct MutualB<Left, Right> {
                parent: Option<self::harness::generic_structs::MutualA<Right, Left>>
            }
            pub struct PermutedA<Left, Right> {
                child: Option<self::harness::generic_structs::PermutedB<Right, Left>>,
                value: Right
            }
            pub struct PermutedB<Left, Right> {
                parent: Option<self::harness::generic_structs::PermutedA<Left, Right>>
            }
            pub struct Tag<T> { value: i32 }
            pub struct Tagged<T> { tag: self::harness::generic_structs::Tag<T> }
            pub struct Phantom<T> {}

            pub fn swap<Left, Right>(pair: self::harness::generic_structs::Pair<Left, Right>) -> self::harness::generic_structs::Pair<Right, Left> effects {} {
                return self::harness::generic_structs::Pair<Right, Left> { left: pair.right, right: pair.left };
            }
            pub fn unbox<T>(value: self::harness::generic_structs::Box<T>) -> T effects {} { return value.value; }
            pub fn same_tag<T>(left: self::harness::generic_structs::Tag<T>, right: self::harness::generic_structs::Tag<T>) -> bool effects {} {
                return left == right;
            }
            pub fn same_tagged<T>(left: self::harness::generic_structs::Tagged<T>, right: self::harness::generic_structs::Tagged<T>) -> bool effects {} {
                return left == right;
            }
            pub fn ignore_resource(value: self::harness::generic_structs::Phantom<FsRead>) -> i32 effects {} { return 1; }

            pub fn main() -> i32 effects {} {
                let nested: self::harness::generic_structs::Box<self::harness::generic_structs::Box<i32>> =
                    self::harness::generic_structs::Box<self::harness::generic_structs::Box<i32>> {
                        value: self::harness::generic_structs::Box<i32> { value: 3 }
                    };
                let holder: self::harness::generic_structs::Holder = self::harness::generic_structs::Holder {
                    nested: self::harness::generic_structs::Box<self::harness::generic_structs::Box<i32>> {
                        value: self::harness::generic_structs::Box<i32> { value: 5 }
                    }
                };
                let pair: self::harness::generic_structs::Pair<i32, Text> =
                    self::harness::generic_structs::Pair<i32, Text> { left: 10, right: "value" };
                let swapped: self::harness::generic_structs::Pair<Text, i32> =
                    self::harness::generic_structs::swap(pair);
                let first_items: List<i32> = [1, 2];
                let second_items: List<i32> = [1, 2];
                let first_box: self::harness::generic_structs::Box<List<i32>> =
                    self::harness::generic_structs::Box<List<i32>> { value: first_items };
                let second_box: self::harness::generic_structs::Box<List<i32>> =
                    self::harness::generic_structs::Box<List<i32>> { value: second_items };
                if first_box == second_box { } else { return 1; }
                let node: self::harness::generic_structs::Node<i32> =
                    self::harness::generic_structs::Node<i32> { value: 7, next: None };
                let flip: self::harness::generic_structs::Flip<i32, bool> =
                    self::harness::generic_structs::Flip<i32, bool> { left: 2, right: true, next: None };
                let mutual_a: self::harness::generic_structs::MutualA<i32, bool> =
                    self::harness::generic_structs::MutualA<i32, bool> { child: None, value: true };
                let permuted_a: self::harness::generic_structs::PermutedA<i32, bool> =
                    self::harness::generic_structs::PermutedA<i32, bool> { child: None, value: true };
                let expected_flip: self::harness::generic_structs::Flip<i32, bool> =
                    self::harness::generic_structs::Flip<i32, bool> { left: 2, right: true, next: None };
                let expected_mutual_a: self::harness::generic_structs::MutualA<i32, bool> =
                    self::harness::generic_structs::MutualA<i32, bool> { child: None, value: true };
                let expected_permuted_a: self::harness::generic_structs::PermutedA<i32, bool> =
                    self::harness::generic_structs::PermutedA<i32, bool> { child: None, value: true };
                let tag_a: self::harness::generic_structs::Tag<FsRead> =
                    self::harness::generic_structs::Tag<FsRead> { value: 11 };
                let tag_b: self::harness::generic_structs::Tag<FsRead> =
                    self::harness::generic_structs::Tag<FsRead> { value: 11 };
                let tagged_a: self::harness::generic_structs::Tagged<FsRead> =
                    self::harness::generic_structs::Tagged<FsRead> { tag: tag_a };
                let tagged_b: self::harness::generic_structs::Tagged<FsRead> =
                    self::harness::generic_structs::Tagged<FsRead> { tag: tag_b };
                if self::harness::generic_structs::same_tag(tag_a, tag_b) { } else { return 2; }
                if self::harness::generic_structs::same_tagged(tagged_a, tagged_b) { } else { return 3; }
                if flip == expected_flip { } else { return 4; }
                if mutual_a == expected_mutual_a { } else { return 5; }
                if permuted_a == expected_permuted_a { } else { return 6; }
                let unused_resource: self::harness::generic_structs::Phantom<FsRead> =
                    self::harness::generic_structs::Phantom<FsRead> {};
                let leaf: i32 = self::harness::generic_structs::unbox(nested.value);
                return leaf + holder.nested.value.value + swapped.right + node.value + flip.left
                    + self::harness::generic_structs::ignore_resource(unused_resource);
            }
            """;

        var managed = await harness.InvokeAsync("generic-structs-managed", "run", source);
        AssertRunOutput("28" + Environment.NewLine, managed);

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("The generic-struct NativeAOT test requires Windows x64 or Linux x64.");

        var build = await harness.InvokeWithTimeoutAsync(
            "generic-structs-aot", "build", source, AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, build.ExitCode, Describe(build));
        const string prefix = "Built native executable: ";
        AssertTrue(build.StandardOutput.StartsWith(prefix, StringComparison.Ordinal), Describe(build));
        AssertTrue(build.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal), Describe(build));
        var executablePath = build.StandardOutput[prefix.Length..^Environment.NewLine.Length];
        AssertTrue(Path.IsPathFullyQualified(executablePath) && File.Exists(executablePath),
            $"Expected the generic-struct NativeAOT executable at {executablePath}.");
        var native = await ExecuteNativeAsync(executablePath, TimeSpan.FromSeconds(30));
        AssertRunOutput("28" + Environment.NewLine, native);

        var unsupportedEquality = """
            module harness::generic_struct_equality_unbound;
            pub struct Box<T> { value: T }
            pub fn same<T>(left: self::harness::generic_struct_equality_unbound::Box<T>, right: self::harness::generic_struct_equality_unbound::Box<T>) -> bool effects {} {
                return left == right;
            }
            """;
        await ExpectDiagnosticsAsync(harness, "generic-struct-equality-unbound", unsupportedEquality, "E_TYPE_MISMATCH");

        const string unrelatedResourceEquality = """
            module harness::generic_struct_equality_resource;
            pub struct Box<T> { value: T }
            pub struct HoldsResource { box: self::harness::generic_struct_equality_resource::Box<FsRead> }
            pub fn main() -> i32 effects {} {
                if 1 == 1 { return 2; }
                return 3;
            }
            """;
        var unrelatedResourceRun = await harness.InvokeAsync(
            "generic-struct-equality-unrelated-resource", "run", unrelatedResourceEquality);
        AssertRunOutput("2" + Environment.NewLine, unrelatedResourceRun);

        const string storedResourceEquality = """
            module harness::generic_struct_equality_stored_resource;
            pub struct Box<T> { value: T }
            pub fn same(
                left: self::harness::generic_struct_equality_stored_resource::Box<FsRead>,
                right: self::harness::generic_struct_equality_stored_resource::Box<FsRead>
            ) -> bool effects {} {
                return left == right;
            }
            """;
        await ExpectDiagnosticsAsync(
            harness, "generic-struct-equality-stored-resource", storedResourceEquality, "E_TYPE_MISMATCH");
    }

    private static async Task TestGenericUnions(Harness harness)
    {
        const string source = """
            module harness::generic_unions;
            pub struct Box<T> { value: T }
            pub union Maybe<T> { Some(T), None }
            pub struct Envelope<T> { choice: self::harness::generic_unions::Maybe<self::harness::generic_unions::Box<T>> }
            pub union Tree<T> { Leaf(T), Branch(List<self::harness::generic_unions::Tree<T>>) }
            pub union Tag<T> { Mark(i32) }
            pub union Cycle<First, Second> {
                Next(self::harness::generic_unions::Cycle<Second, First>),
                Done(Second)
            }
            pub struct CompareA<Left, Right> {
                child: Option<self::harness::generic_unions::CompareB<Right, Left>>,
                value: Left
            }
            pub union CompareB<Left, Right> {
                Parent(Option<self::harness::generic_unions::CompareA<Right, Left>>),
                Value(Left),
                Empty
            }

            pub fn project<T>(value: self::harness::generic_unions::Maybe<T>) -> Option<T> effects {} {
                return match value {
                    self::harness::generic_unions::Maybe.Some(item) => Some(item),
                    self::harness::generic_unions::Maybe.None => None
                };
            }
            pub fn same_tag<T>(left: self::harness::generic_unions::Tag<T>, right: self::harness::generic_unions::Tag<T>) -> bool effects {} {
                return left == right;
            }
            pub fn main() -> i32 effects {} {
                let maybe: self::harness::generic_unions::Maybe<i32> =
                    self::harness::generic_unions::Maybe<i32>.Some(9);
                let projected: Option<i32> = self::harness::generic_unions::project(maybe);
                let projected_value: i32 = match projected {
                    Some(value) => value,
                    None => 0
                };
                let envelope: self::harness::generic_unions::Envelope<i32> =
                    self::harness::generic_unions::Envelope<i32> {
                        choice: self::harness::generic_unions::Maybe<self::harness::generic_unions::Box<i32>>.Some(
                            self::harness::generic_unions::Box<i32> { value: 5 })
                    };
                let contained: i32 = match envelope.choice {
                    self::harness::generic_unions::Maybe.Some(boxed) => boxed.value,
                    self::harness::generic_unions::Maybe.None => 0
                };
                let tree: self::harness::generic_unions::Tree<i32> =
                    self::harness::generic_unions::Tree<i32>.Branch([
                        self::harness::generic_unions::Tree<i32>.Leaf(4)]);
                let tree_count: i32 = match tree {
                    self::harness::generic_unions::Tree.Leaf(value) => value,
                    self::harness::generic_unions::Tree.Branch(children) => children.length
                };
                let list_left: self::harness::generic_unions::Maybe<List<i32>> =
                    self::harness::generic_unions::Maybe<List<i32>>.Some([1, 2]);
                let list_right: self::harness::generic_unions::Maybe<List<i32>> =
                    self::harness::generic_unions::Maybe<List<i32>>.Some([1, 2]);
                if list_left == list_right { } else { return 1; }
                let leaf_a: self::harness::generic_unions::CompareA<i32, Text> =
                    self::harness::generic_unions::CompareA<i32, Text> { child: None, value: 7 };
                let leaf_b: self::harness::generic_unions::CompareA<i32, Text> =
                    self::harness::generic_unions::CompareA<i32, Text> { child: None, value: 7 };
                let nested_a: self::harness::generic_unions::CompareA<i32, Text> =
                    self::harness::generic_unions::CompareA<i32, Text> {
                        child: Some(self::harness::generic_unions::CompareB<Text, i32>.Parent(Some(leaf_a))),
                        value: 3
                    };
                let nested_b: self::harness::generic_unions::CompareA<i32, Text> =
                    self::harness::generic_unions::CompareA<i32, Text> {
                        child: Some(self::harness::generic_unions::CompareB<Text, i32>.Parent(Some(leaf_b))),
                        value: 3
                    };
                if nested_a == nested_b { } else { return 2; }
                let cycle_left: self::harness::generic_unions::Cycle<Text, i32> =
                    self::harness::generic_unions::Cycle<Text, i32>.Next(
                        self::harness::generic_unions::Cycle<i32, Text>.Next(
                            self::harness::generic_unions::Cycle<Text, i32>.Done(10)));
                let cycle_right: self::harness::generic_unions::Cycle<Text, i32> =
                    self::harness::generic_unions::Cycle<Text, i32>.Next(
                        self::harness::generic_unions::Cycle<i32, Text>.Next(
                            self::harness::generic_unions::Cycle<Text, i32>.Done(10)));
                if cycle_left == cycle_right { } else { return 4; }
                let tag_left: self::harness::generic_unions::Tag<FsRead> =
                    self::harness::generic_unions::Tag<FsRead>.Mark(11);
                let tag_right: self::harness::generic_unions::Tag<FsRead> =
                    self::harness::generic_unions::Tag<FsRead>.Mark(11);
                if self::harness::generic_unions::same_tag(tag_left, tag_right) { } else { return 3; }
                return projected_value + contained + tree_count;
            }
            """;

        var managed = await harness.InvokeAsync("generic-unions-managed", "run", source);
        AssertRunOutput("15" + Environment.NewLine, managed);

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("The generic-union NativeAOT test requires Windows x64 or Linux x64.");

        var build = await harness.InvokeWithTimeoutAsync(
            "generic-unions-aot", "build", source, AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, build.ExitCode, Describe(build));
        const string prefix = "Built native executable: ";
        AssertTrue(build.StandardOutput.StartsWith(prefix, StringComparison.Ordinal), Describe(build));
        var executablePath = build.StandardOutput[prefix.Length..].TrimEnd('\r', '\n');
        AssertTrue(Path.IsPathFullyQualified(executablePath) && File.Exists(executablePath),
            $"Expected the generic-union NativeAOT executable at {executablePath}.");
        AssertRunOutput("15" + Environment.NewLine,
            await ExecuteNativeAsync(executablePath, TimeSpan.FromSeconds(30)));

        const string storedResource = """
            module harness::generic_union_stored_resource;
            pub union Carrier<T> { Hold(T), Empty }
            pub fn escape(value: self::harness::generic_union_stored_resource::Carrier<FsRead>) -> List<self::harness::generic_union_stored_resource::Carrier<FsRead>> effects {} {
                return [value];
            }
        """;
        await ExpectDiagnosticsAsync(harness, "generic-union-stored-resource", storedResource, "E_RESOURCE_ESCAPE");

        const string permutedStoredResource = """
            module harness::generic_union_permuted_resource;
            pub union Cycle<First, Second> {
                Next(self::harness::generic_union_permuted_resource::Cycle<Second, First>),
                Done(Second)
            }
            pub fn same(
                left: self::harness::generic_union_permuted_resource::Cycle<FsRead, i32>,
                right: self::harness::generic_union_permuted_resource::Cycle<FsRead, i32>
            ) -> bool effects {} {
                return left == right;
            }
            """;
        await ExpectDiagnosticsAsync(
            harness, "generic-union-permuted-stored-resource", permutedStoredResource, "E_TYPE_MISMATCH");

        const string crossKindGrowth = """
            module harness::generic_union_cross_kind_growth;
            pub struct Holder<T> { item: self::harness::generic_union_cross_kind_growth::Growing<T> }
            pub union Growing<T> {
                Again(self::harness::generic_union_cross_kind_growth::Holder<List<T>>),
                Done
            }
            """;
        await ExpectDiagnosticsAsync(harness, "generic-union-cross-kind-growth", crossKindGrowth, "E_TYPE_MISMATCH");

        var rotationParameters = Enumerable.Range(0, 193).Select(index => $"T{index}").ToArray();
        var rotatedParameters = rotationParameters.Skip(1).Append(rotationParameters[0]);
        var longPermutation = "module harness::generic_union_long_permutation;\n"
            + $"pub union Rotate<{string.Join(", ", rotationParameters)}> {{\n"
            + $"    Next(self::harness::generic_union_long_permutation::Rotate<{string.Join(", ", rotatedParameters)}>),\n"
            + "    Done\n}\n";
        var longPermutationCheck = await harness.InvokeAsync(
            "generic-union-long-permutation", "check", longPermutation);
        AssertEqual(0, longPermutationCheck.ExitCode, Describe(longPermutationCheck));

        const string unrelatedResourceEquality = """
            module harness::generic_union_equality_resource;
            pub union Choice<T> { Value(T), Empty }
            pub union Tag<T> { Mark(i32) }
            pub fn main() -> i32 effects {} {
                if 1 == 1 { return 2; }
                return 3;
            }
            """;
        AssertRunOutput("2" + Environment.NewLine,
            await harness.InvokeAsync("generic-union-unrelated-resource-equality", "run", unrelatedResourceEquality));

        var genericReply = """
            module harness::generic_union_route_reply;
            pub union Reply<T> { Found(T), Empty }
            route GET "/items" {
                handler: self::harness::generic_union_route_reply::get_items;
                response Found: 200 json Text;
                response Empty: 204;
            }
            pub fn get_items() -> self::harness::generic_union_route_reply::Reply<Text> effects {} {
                return self::harness::generic_union_route_reply::Reply<Text>.Found("ok");
            }
            """;
        var routeDiagnostics = await ExpectDiagnosticsAsync(
            harness, "generic-union-route-reply", genericReply, "E_ROUTE_HANDLER");
        AssertEqual("Route handler reply unions must be nongeneric",
            routeDiagnostics.Single().Message,
            "A generic reply union must fail early with one stable route-handler diagnostic.");

        var nestedRouteCodec = """
            module harness::generic_union_route_codec;
            pub union Payload<T> { Value(T) }
            pub struct Response { payload: self::harness::generic_union_route_codec::Payload<Text> }
            pub union Reply { Found(self::harness::generic_union_route_codec::Response), Empty }
            route GET "/items" {
                handler: self::harness::generic_union_route_codec::get_items;
                response Found: 200 json self::harness::generic_union_route_codec::Response;
                response Empty: 204;
            }
            pub fn get_items() -> self::harness::generic_union_route_codec::Reply effects {} {
                return self::harness::generic_union_route_codec::Reply.Empty;
            }
            """;
        await ExpectDiagnosticsAsync(harness, "generic-union-route-codec", nestedRouteCodec, "E_ROUTE_CODEC_UNSUPPORTED");

        var sqliteCodec = """
            module harness::generic_union_sqlite_codec;
            pub union Value<T> { Item(T) }
            pub struct Parameters { value: self::harness::generic_union_sqlite_codec::Value<i32> }
            pub struct Row { value: self::harness::generic_union_sqlite_codec::Value<i32> }
            pub fn read(db: DbRead, parameters: self::harness::generic_union_sqlite_codec::Parameters) -> Result<Option<self::harness::generic_union_sqlite_codec::Row>, DbError> effects { db.read } {
                return db.query_one("SELECT value FROM records", parameters);
            }
            """;
        await ExpectDiagnosticsAsync(harness, "generic-union-sqlite-codec", sqliteCodec, "E_DB_CODEC_UNSUPPORTED");

        const string rootManifest = """
            name = "generic-union-root"
            version = "0.1.0"
            kind = "cli"
            source_root = "src"
            entry_module = "app::main"

            [dependencies]
            models = "../models"
            """;
        const string rootSource = """
            module app::main;
            pub fn roundtrip(value: models::choices::Choice<List<i32>>) -> models::choices::Choice<List<i32>> effects {} {
                return value;
            }
            pub fn main() -> i32 effects {} {
                let choice: models::choices::Choice<List<i32>> =
                    models::choices::Choice<List<i32>>.Value([17]);
                let copied: models::choices::Choice<List<i32>> = self::app::main::roundtrip(choice);
                return match copied {
                    models::choices::Choice.Value(values) => match values.get(0) {
                        Some(value) => value,
                        None => 0
                    },
                    models::choices::Choice.Empty => 0
                };
            }
            """;
        var dependencyRoot = await harness.WritePackageGraphAsync(
            "generic-union-dependency",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(rootManifest, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = rootSource
                }),
                ["models"] = new PackageFixture(LibraryPackageManifest("generic-union-models"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/choices.hob"] = """
                            module choices;
                            pub union Choice<T> { Value(T), Empty }
                            """
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "generic-union-dependency-lock", dependencyRoot, "lock"));
        var dependencyRun = await harness.InvokePackageDirectoryAsync("generic-union-dependency-run", dependencyRoot, "run");
        AssertRunOutput("17" + Environment.NewLine, dependencyRun);
        var apiRun = await harness.InvokeCompilerCommandAsync("inspect", "api", dependencyRoot, "--json");
        AssertEqual(0, apiRun.ExitCode, Describe(apiRun));
        using (var apiDocument = JsonDocument.Parse(apiRun.StandardOutput))
        {
            var api = apiDocument.RootElement;
            AssertInspectApiPropertyOrder(api);
            AssertEqual(13, api.GetProperty("schema_version").GetInt32(), "Generic-union inspect API must use schema version 13.");
            var union = api.GetProperty("unions").EnumerateArray().Single();
            AssertJsonPropertyOrder(union, "id,source_ids,package,type_parameters,variants");
            AssertEqual("T", union.GetProperty("type_parameters")[0].GetProperty("name").GetString(),
                "Generic union declarations should expose ordered type parameter names.");
            AssertEqual(0, union.GetProperty("type_parameters")[0].GetProperty("ordinal").GetInt32(),
                "Generic union type parameter ordinals should be zero-based.");
            var function = api.GetProperty("functions").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == "self::app::main::roundtrip");
            var argument = function.GetProperty("parameters")[0].GetProperty("type");
            AssertEqual("nominal", argument.GetProperty("kind").GetString(),
                "API function types should retain generic union nominal identity.");
            var listArgument = argument.GetProperty("type_arguments")[0];
            AssertEqual("list", listArgument.GetProperty("kind").GetString(),
                "API union arguments should serialize nested generic types recursively.");
            AssertEqual("i32", listArgument.GetProperty("item").GetProperty("name").GetString(),
                "API union arguments should preserve their nested primitive type.");
        }
        var repeatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", dependencyRoot, "--json");
        AssertEqual(apiRun.StandardOutput, repeatedApi.StandardOutput,
            "Repeated generic-union inspect API calls should be byte-identical.");
        var auditRun = await harness.InvokeCompilerCommandAsync("audit", dependencyRoot, "--json");
        AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
        using var auditDocument = JsonDocument.Parse(auditRun.StandardOutput);
        AssertEqual(11, auditDocument.RootElement.GetProperty("schema_version").GetInt32(),
            "The current audit contract with trait facts must use version 11.");
    }

    private static async Task TestStaticTraits(Harness harness)
    {
        async Task<string> Fixture(string name) => await File.ReadAllTextAsync(
            Path.Combine(harness.RepositoryRoot, "fixtures", name));

        var scalar = await Fixture("86-valid-static-trait-scalar.hob");
        AssertRunOutput("70" + Environment.NewLine,
            await harness.InvokeAsync("static-traits-scalar", "run", scalar));

        var closedTargets = await Fixture("87-valid-static-trait-closed-targets.hob");
        var (managed, generatedSource) = await InvokeCapturingGeneratedSourceAsync(
            harness,
            "static-traits-closed-targets-managed",
            "run",
            closedTargets);
        AssertRunOutput("151" + Environment.NewLine, managed);
        AssertTrue(generatedSource.Contains("static abstract ", StringComparison.Ordinal),
            "Static-trait generated source must use C# static-abstract interface members.");
        AssertTrue(generatedSource.Contains("private readonly struct Impl_", StringComparison.Ordinal),
            "Each concrete implementation must lower to a zero-state value witness.");
        AssertTrue(generatedSource.Contains("where W0_0 : struct, Trait_", StringComparison.Ordinal) &&
                   generatedSource.Contains("where W1_0 : struct, Trait_", StringComparison.Ordinal),
            "Generic forwarding must use ordered constrained witness type parameters.");
        AssertTrue(!generatedSource.Contains("System.Func<", StringComparison.Ordinal) &&
                   !generatedSource.Contains("DynamicInvoke", StringComparison.Ordinal) &&
                   !generatedSource.Contains("System.Reflection", StringComparison.Ordinal) &&
                   !generatedSource.Contains("GetType(", StringComparison.Ordinal) &&
                   !generatedSource.Contains("object ", StringComparison.Ordinal),
            "Trait lowering must not introduce delegates, reflection, runtime lookup, or interface objects.");

        var privateSignature = await Fixture("88-valid-static-trait-private-signature.hob");
        AssertRunOutput("4" + Environment.NewLine,
            await harness.InvokeAsync("static-traits-private-signature", "run", privateSignature));
        var phantom = await Fixture("89-valid-static-trait-phantom-resource.hob");
        AssertRunOutput("true" + Environment.NewLine,
            await harness.InvokeAsync("static-traits-phantom-resource", "run", phantom));
        var unusedBound = await Fixture("90-valid-static-trait-unused-bound.hob");
        AssertRunOutput("23" + Environment.NewLine,
            await harness.InvokeAsync("static-traits-unused-bound", "run", unusedBound));

        const string reportSource = """
            module app::main;
            pub trait Measure { fn measure(value: Self) -> i32 effects {}; }
            fn measure_i32(value: i32) -> i32 effects {} { return value * 3; }
            fn measure_text(value: Text) -> i32 effects {} { return 2; }
            pub impl self::app::main::Measure for i32 { measure = self::app::main::measure_i32; }
            impl self::app::main::Measure for Text { measure = self::app::main::measure_text; }
            trait Hidden { fn hidden(value: Self) -> i32 effects {}; }
            fn hidden_i32(value: i32) -> i32 effects {} { return value + 1; }
            impl self::app::main::Hidden for i32 { hidden = self::app::main::hidden_i32; }
            fn hidden_forward<T: self::app::main::Hidden>(value: T) -> i32 effects {} {
                return self::app::main::Hidden.hidden(value);
            }
            pub fn dispatch<T: self::app::main::Measure>(value: T) -> i32 effects {} {
                return self::app::main::Measure.measure(value);
            }
            pub fn main() -> i32 effects {} {
                return self::app::main::Measure.measure(5)
                    + self::app::main::Measure.measure("hidden")
                    + self::app::main::dispatch(5)
                    + self::app::main::hidden_forward(1);
            }
            """;
        var reportManifest = CliPackageManifest();
        var reportFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = reportSource
        };
        var reportRoot = await harness.WritePackageAsync("static-traits-report", reportManifest, reportFiles);
        AssertRunOutput("34" + Environment.NewLine,
            await harness.InvokePackageDirectoryAsync("static-traits-report-run", reportRoot, "run"));
        var apiRun = await harness.InvokeCompilerCommandAsync("inspect", "api", reportRoot, "--json");
        AssertEqual(0, apiRun.ExitCode, Describe(apiRun));
        using var apiDocument = JsonDocument.Parse(apiRun.StandardOutput);
        var api = apiDocument.RootElement;
        AssertInspectApiPropertyOrder(api);
        AssertEqual(13, api.GetProperty("schema_version").GetInt32(),
            "Trait metadata should use current inspect API schema version 13.");
        var measureTraitId = "hob.trait.v1.root::app::main::Measure";
        var apiTraits = api.GetProperty("traits").EnumerateArray().ToArray();
        AssertEqual(1, apiTraits.Length, "The API should expose the one public source trait.");
        var apiTrait = apiTraits[0];
        AssertEqual(measureTraitId, apiTrait.GetProperty("id").GetString(),
            "Trait identities should be portable semantic IDs, not graph-local numbers.");
        AssertEqual("self::app::main::Measure", apiTrait.GetProperty("source_ids")[0].GetString(),
            "Public traits should retain their source-facing declaration identity.");
        var apiMethod = apiTrait.GetProperty("methods")[0];
        AssertEqual("measure", apiMethod.GetProperty("name").GetString(),
            "Trait method order and names should follow the declaration.");
        AssertEqual("self", apiMethod.GetProperty("parameters")[0].GetProperty("type").GetProperty("kind").GetString(),
            "Implicit Self should serialize as its own source-level type kind.");
        var apiImpls = api.GetProperty("trait_impls").EnumerateArray().ToArray();
        AssertEqual(1, apiImpls.Length, "The public API should filter the package-private Text implementation.");
        AssertEqual(measureTraitId, apiImpls[0].GetProperty("trait").GetString(),
            "Public impls should refer to the stable trait identity.");
        AssertTrue(Regex.IsMatch(apiImpls[0].GetProperty("id").GetString() ?? string.Empty,
                "^hob\\.impl\\.v1\\.[0-9a-f]{64}$", RegexOptions.CultureInvariant),
            "Implementation identities should be stable semantic digests.");
        AssertEqual("public", apiImpls[0].GetProperty("visibility").GetString(),
            "The API should preserve public implementation visibility.");
        AssertEqual("measure", apiImpls[0].GetProperty("methods")[0].GetString(),
            "The API should expose fulfilled method names without binding-function identities.");
        var apiFunctions = api.GetProperty("functions").EnumerateArray().ToArray();
        var dispatch = apiFunctions.Single(function => function.GetProperty("id").GetString() == "self::app::main::dispatch");
        var dispatchParameter = dispatch.GetProperty("type_parameters")[0];
        AssertJsonPropertyOrder(dispatchParameter, "name,ordinal,bounds");
        AssertEqual(0, dispatchParameter.GetProperty("ordinal").GetInt32(),
            "Function type parameter ordinals should remain source ordered.");
        AssertEqual(measureTraitId, dispatchParameter.GetProperty("bounds")[0].GetProperty("trait").GetString(),
            "Trait bounds should retain source order and stable identities.");
        var dispatchCall = dispatch.GetProperty("trait_calls")[0];
        AssertEqual("method", dispatchCall.GetProperty("kind").GetString(),
            "Generic method dispatch should remain explicit in checked report facts.");
        AssertEqual("bound", dispatchCall.GetProperty("witness").GetProperty("kind").GetString(),
            "Generic method dispatch should identify the forwarded bound witness.");
        AssertEqual(0, dispatchCall.GetProperty("witness").GetProperty("type_parameter_ordinal").GetInt32(),
            "Forwarded trait witnesses should preserve the type parameter ordinal.");
        var apiMain = apiFunctions.Single(function => function.GetProperty("id").GetString() == "self::app::main::main");
        var apiMainCalls = apiMain.GetProperty("trait_calls").EnumerateArray().ToArray();
        var privateDispatch = apiMainCalls.Single(call => call.GetProperty("kind").GetString() == "method"
            && call.GetProperty("witness").ValueKind == JsonValueKind.Null);
        AssertEqual(measureTraitId, privateDispatch.GetProperty("trait").GetString(),
            "A public call through a private impl should retain the trait operation while hiding the impl identity.");
        AssertTrue(apiMainCalls.Any(call => call.GetProperty("kind").GetString() == "function_call"
                && call.GetProperty("witnesses")[0].GetProperty("witness").GetProperty("kind").GetString() == "impl"),
            "Public API call-site facts should retain addressable concrete witnesses.");
        AssertTrue(!apiRun.StandardOutput.Contains("measure_text", StringComparison.Ordinal),
            "Public API calls must not expose private impl binding function names.");
        AssertTrue(!apiRun.StandardOutput.Contains("::Hidden", StringComparison.Ordinal)
                && !apiRun.StandardOutput.Contains("hidden_i32", StringComparison.Ordinal),
            "Public API call facts must not expose private trait identities or impl binding names.");
        var auditRun = await harness.InvokeCompilerCommandAsync("audit", reportRoot, "--json");
        AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
        using var auditDocument = JsonDocument.Parse(auditRun.StandardOutput);
        var audit = auditDocument.RootElement;
        AssertAuditPropertyOrder(audit);
        AssertEqual(11, audit.GetProperty("schema_version").GetInt32(),
            "Trait compiler facts should use current audit schema version 11.");
        var compilerFacts = audit.GetProperty("compiler");
        AssertEqual(2, compilerFacts.GetProperty("traits").GetArrayLength(),
            "Audit should retain public and private trait declarations.");
        var auditImpls = compilerFacts.GetProperty("trait_impls").EnumerateArray().ToArray();
        AssertEqual(3, auditImpls.Length, "Audit should retain public and private impl facts.");
        var privateImpl = auditImpls.Single(implementation =>
            implementation.GetProperty("target").GetProperty("name").GetString() == "Text");
        AssertEqual("Text", privateImpl.GetProperty("target").GetProperty("name").GetString(),
            "Audit should retain the closed target of a private implementation.");
        AssertEqual("measure_text", privateImpl.GetProperty("methods")[0].GetProperty("binding_function").GetProperty("name").GetString(),
            "Audit should retain the bound function identity for private implementations.");
        AssertTrue(compilerFacts.GetProperty("traits").EnumerateArray()
                .Any(trait => trait.GetProperty("name").GetString() == "Hidden"),
            "Audit should retain private trait identities that the API filters.");
        var auditMain = compilerFacts.GetProperty("functions").EnumerateArray()
            .Single(function => function.GetProperty("name").GetString() == "main");
        AssertTrue(auditMain.GetProperty("trait_calls").EnumerateArray()
                .Any(call => call.GetProperty("kind").GetString() == "method"
                    && call.GetProperty("witness").GetProperty("kind").GetString() == "impl"
                    && call.GetProperty("witness").GetProperty("binding_function").GetProperty("name").GetString() == "measure_text"),
            "Audit method-dispatch facts should connect a concrete witness to its binding function.");
        AssertTrue(auditMain.GetProperty("direct_calls").EnumerateArray()
                .Any(call => call.GetProperty("name").GetString() == "measure_text"),
            "Audit call closure should retain private implementation binding edges.");
        var repeatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", reportRoot, "--json");
        AssertEqual(apiRun.StandardOutput, repeatedApi.StandardOutput,
            "Trait API output should be byte-identical on repeated inspection.");
        var relocatedRoot = await harness.WritePackageAsync("static-traits-report-relocated", reportManifest, reportFiles);
        var relocatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", relocatedRoot, "--json");
        AssertEqual(apiRun.StandardOutput, relocatedApi.StandardOutput,
            "Trait API IDs should remain deterministic after relocating an identical package.");
        var relocatedAudit = await harness.InvokeCompilerCommandAsync("audit", relocatedRoot, "--json");
        AssertEqual(auditRun.StandardOutput, relocatedAudit.StandardOutput,
            "Audit trait and impl identities should remain deterministic after package relocation.");

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("The static-trait NativeAOT test requires Windows x64 or Linux x64.");
        var aotBuild = await harness.InvokeWithTimeoutAsync(
            "static-traits-closed-targets-aot", "build", closedTargets, AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, aotBuild.ExitCode, Describe(aotBuild));
        const string builtPrefix = "Built native executable: ";
        AssertTrue(aotBuild.StandardOutput.StartsWith(builtPrefix, StringComparison.Ordinal), Describe(aotBuild));
        var executablePath = aotBuild.StandardOutput[builtPrefix.Length..].TrimEnd('\r', '\n');
        AssertTrue(Path.IsPathFullyQualified(executablePath) && File.Exists(executablePath),
            $"Expected the static-trait NativeAOT executable at {executablePath}.");
        AssertRunOutput("151" + Environment.NewLine,
            await ExecuteNativeAsync(executablePath, TimeSpan.FromSeconds(30)));

        var publicPrivate = await Fixture("91-trait-public-private-signature.hob");
        await ExpectDiagnosticsAsync(harness, "static-traits-public-private-signature", publicPrivate, "E_TYPE_VISIBILITY");
        var recursive = await Fixture("92-trait-recursive-obligation.hob");
        await ExpectDiagnosticsAsync(harness, "static-traits-recursive-obligation", recursive, "E_TRAIT_CONSTRAINT_RECURSIVE");
        var forwardedCycle = await Fixture("93-trait-forwarded-cycle.hob");
        await ExpectDiagnosticsAsync(harness, "static-traits-forwarded-cycle", forwardedCycle, "E_TRAIT_CONSTRAINT_RECURSIVE");
        var storedResource = await Fixture("94-trait-stored-resource.hob");
        await ExpectDiagnosticsAsync(harness, "static-traits-stored-resource", storedResource, "E_RESOURCE_ESCAPE");
        var duplicate = await Fixture("95-trait-duplicate-no-call.hob");
        await ExpectDiagnosticsAsync(harness, "static-traits-duplicate-no-call", duplicate, "E_TRAIT_IMPL_DUPLICATE");
        var badSignature = await Fixture("96-trait-impl-signature.hob");
        await ExpectDiagnosticsAsync(harness, "static-traits-impl-signature", badSignature, "E_TRAIT_IMPL_SIGNATURE");
        var missing = await Fixture("97-trait-impl-missing.hob");
        await ExpectDiagnosticsAsync(harness, "static-traits-impl-missing", missing, "E_TRAIT_IMPL_MISSING");

        const string dependencyManifest = """
            name = "static-trait-consumer"
            version = "0.1.0"
            kind = "cli"
            source_root = "src"
            entry_module = "app::main"

            [dependencies]
            core = "../core"
            """;
        const string dependencySource = """
            module app::main;
            pub fn main() -> i32 effects {} {
                return core::traits::Measure.measure(8);
            }
            """;
        const string traitDependencySource = """
            module traits;
            pub trait Measure { fn measure(value: Self) -> i32 effects {}; }
            fn measure_i32(value: i32) -> i32 effects {} { return value * 10; }
            pub impl self::traits::Measure for i32 { measure = self::traits::measure_i32; }
            """;
        var dependencyRoot = await harness.WritePackageGraphAsync(
            "static-traits-dependency",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(dependencyManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = dependencySource }),
                ["core"] = new PackageFixture(LibraryPackageManifest("static-trait-core"),
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/traits.hob"] = traitDependencySource })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "static-traits-dependency-lock", dependencyRoot, "lock"));
        AssertRunOutput("80" + Environment.NewLine,
            await harness.InvokePackageDirectoryAsync("static-traits-dependency-run", dependencyRoot, "run"));

        const string orphanManifest = """
            name = "static-trait-orphan-app"
            version = "0.1.0"
            kind = "lib"
            source_root = "src"

            [dependencies]
            remote = "../remote"
            """;
        const string orphanSource = """
            module app::model;
            struct Local { value: i32 }
            fn read_local(value: List<self::app::model::Local>) -> i32 effects {} { return 0; }
            impl remote::traits::Read for List<self::app::model::Local> { read = self::app::model::read_local; }
            """;
        const string remoteTrait = """
            module traits;
            pub trait Read { fn read(value: Self) -> i32 effects {}; }
            """;
        var orphanRoot = await harness.WritePackageGraphAsync(
            "static-traits-orphan-wrapper",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(orphanManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/model.hob"] = orphanSource }),
                ["remote"] = new PackageFixture(LibraryPackageManifest("static-trait-remote"),
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/traits.hob"] = remoteTrait })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "static-traits-orphan-lock", orphanRoot, "lock"));
        var orphanCheck = await harness.InvokePackageDirectoryAsync(
            "static-traits-orphan-check", orphanRoot, "check", "--json");
        AssertEqual(1, orphanCheck.ExitCode, Describe(orphanCheck));
        var orphanDiagnostics = ParseDiagnosticSnapshots(orphanCheck.StandardOutput);
        AssertEqual(1, orphanDiagnostics.Length, Describe(orphanCheck));
        AssertEqual("E_TRAIT_IMPL_SIGNATURE", orphanDiagnostics[0].Code,
            "The orphan rule must reject a foreign trait on a builtin wrapper around a local nominal.");
    }

    private static async Task<(ProcessResult Result, string Source)> InvokeCapturingGeneratedSourceAsync(
        Harness harness,
        string caseName,
        string command,
        string source)
    {
        var generatedRoot = Path.Combine(Path.GetTempPath(), "hob-generated");
        Directory.CreateDirectory(generatedRoot);
        var existingDirectories = Directory.EnumerateDirectories(generatedRoot)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var processTask = harness.InvokeAsync(caseName, command, source);
        string? capturedSource = null;
        async Task<string?> CaptureCurrentSourceAsync()
        {
            foreach (var generatedDirectory in Directory.EnumerateDirectories(generatedRoot)
                         .Select(Path.GetFullPath)
                         .Where(path => !existingDirectories.Contains(path)))
            {
                var sourcePath = Path.Combine(generatedDirectory, "Program.cs");
                if (!File.Exists(sourcePath)) continue;
                try
                {
                    var candidate = await File.ReadAllTextAsync(sourcePath);
                    if (candidate.Contains("interface Trait_", StringComparison.Ordinal))
                        return candidate;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            return null;
        }

        while (!processTask.IsCompleted && capturedSource is null)
        {
            capturedSource = await CaptureCurrentSourceAsync();
            if (capturedSource is null) await Task.Delay(10);
        }

        var result = await processTask;
        capturedSource ??= await CaptureCurrentSourceAsync();
        AssertTrue(capturedSource is not null,
            $"The compiler should leave its generated static-trait C# available while the build runs. " +
            $"exit={result.ExitCode}, stdout=<{result.StandardOutput}>, stderr=<{result.StandardError}>");
        return (result, capturedSource!);
    }

    private static async Task TestForwardAndGuardedRecursion(Harness harness)
    {
        const string source = """
            module harness::forward_guarded_structs;
            pub struct Empty {}
            pub struct Before { after: self::harness::forward_guarded_structs::After }
            pub struct After { value: i32 }
            pub struct OptionalNode { next: Option<self::harness::forward_guarded_structs::OptionalNode> }
            pub struct ResultNode { next: Result<Option<self::harness::forward_guarded_structs::ResultNode>, Text> }
            pub union TreeLink { Branch(self::harness::forward_guarded_structs::TreeBranch), End }
            pub struct TreeBranch { next: self::harness::forward_guarded_structs::TreeLink }

            pub fn main() -> i32 effects {} {
                let empty: self::harness::forward_guarded_structs::Empty = self::harness::forward_guarded_structs::Empty {};
                let optional: self::harness::forward_guarded_structs::OptionalNode = self::harness::forward_guarded_structs::OptionalNode { next: None };
                let result: self::harness::forward_guarded_structs::ResultNode = self::harness::forward_guarded_structs::ResultNode { next: Err("stop") };
                let tree: self::harness::forward_guarded_structs::TreeLink = self::harness::forward_guarded_structs::TreeLink.Branch(self::harness::forward_guarded_structs::TreeBranch { next: self::harness::forward_guarded_structs::TreeLink.End });
                let value: self::harness::forward_guarded_structs::Before = self::harness::forward_guarded_structs::Before { after: self::harness::forward_guarded_structs::After { value: 29 } };
                return value.after.value;
            }
            """;

        var result = await harness.InvokeAsync("forward-guarded-structs", "run", source);
        AssertRunOutput("29" + Environment.NewLine, result);

        const string librarySource = """
            module harness::empty_struct_library;
            pub struct Empty {}
            pub fn make_empty() -> self::harness::empty_struct_library::Empty effects {} { return self::harness::empty_struct_library::Empty {}; }
            """;
        var library = await harness.InvokeAsync("empty-struct-library", "build", librarySource);
        AssertBuiltDll(library, Path.GetDirectoryName(harness.LastSourcePath)!);
    }

    private static async Task TestStructCycles(Harness harness)
    {
        var cases = new (string Name, string Source)[]
        {
            ("direct-struct-cycle", """
                module harness::direct_struct_cycle;
                pub struct Node { next: self::harness::direct_struct_cycle::Node }
                """),
            ("mutual-struct-cycle", """
                module harness::mutual_struct_cycle;
                pub struct Left { right: self::harness::mutual_struct_cycle::Right }
                pub struct Right { left: self::harness::mutual_struct_cycle::Left }
                """)
        };

        foreach (var (name, source) in cases)
            await ExpectDiagnosticsAsync(harness, name, source, "E_TYPE_MISMATCH");
    }

    private static async Task TestStructFieldDiagnostics(Harness harness)
    {
        var cases = new (string Name, string Source, string ExpectedCode)[]
        {
            ("missing-struct-field", """
                module harness::missing_struct_field;
                pub struct Person { name: Text, age: i32 }
                pub fn main() -> i32 effects {} {
                    let person: self::harness::missing_struct_field::Person = self::harness::missing_struct_field::Person { name: "Ada" };
                    return 0;
                }
                """, "E_FIELD_MISSING"),
            ("unknown-struct-field", """
                module harness::unknown_struct_field;
                pub struct Person { name: Text, age: i32 }
                pub fn main() -> i32 effects {} {
                    let person: self::harness::unknown_struct_field::Person = self::harness::unknown_struct_field::Person { name: "Ada", age: 37, nickname: "A" };
                    return 0;
                }
                """, "E_FIELD_UNKNOWN"),
            ("duplicate-struct-initializer-field", """
                module harness::duplicate_struct_initializer_field;
                pub struct Person { name: Text, age: i32 }
                pub fn main() -> i32 effects {} {
                    let person: self::harness::duplicate_struct_initializer_field::Person = self::harness::duplicate_struct_initializer_field::Person { name: "Ada", age: 37, age: 38 };
                    return 0;
                }
                """, "E_FIELD_DUPLICATE"),
            ("shadowed-dotted-call", """
                module harness::shadowed_dotted_call;
                pub union Choice { Yes(i32), No }
                pub struct ChoiceValue { Yes: i32 }
                pub fn main() -> i32 effects {} {
                    let Choice: self::harness::shadowed_dotted_call::ChoiceValue = self::harness::shadowed_dotted_call::ChoiceValue { Yes: 1 };
                    return Choice.Yes(2);
                }
                """, "E_UNSUPPORTED"),
            ("wrong-struct-field-type", """
                module harness::wrong_struct_field_type;
                pub struct Person { name: Text, age: i32 }
                pub fn main() -> i32 effects {} {
                    let person: self::harness::wrong_struct_field_type::Person = self::harness::wrong_struct_field_type::Person { name: "Ada", age: "thirty-seven" };
                    return 0;
                }
                """, "E_TYPE_MISMATCH"),
            ("unknown-struct-member-read", """
                module harness::unknown_struct_member;
                pub struct Person { name: Text, age: i32 }
                pub fn main() -> i32 effects {} {
                    let person: self::harness::unknown_struct_member::Person = self::harness::unknown_struct_member::Person { name: "Ada", age: 37 };
                    return person.height;
                }
                """, "E_FIELD_UNKNOWN"),
            ("non-struct-member-read", """
                module harness::non_struct_member;
                pub fn main() -> i32 effects {} {
                    let count: i32 = 3;
                    return count.value;
                }
                """, "E_TYPE_MISMATCH"),
            ("nominal-struct-mismatch", """
                module harness::nominal_struct_mismatch;
                pub struct User { age: i32 }
                pub struct Score { age: i32 }
                pub fn use_user(value: self::harness::nominal_struct_mismatch::User) -> i32 effects {} { return value.age; }
                pub fn main() -> i32 effects {} { return self::harness::nominal_struct_mismatch::use_user(self::harness::nominal_struct_mismatch::Score { age: 37 }); }
                """, "E_TYPE_MISMATCH")
        };

        foreach (var (name, source, expectedCode) in cases)
        {
            var diagnostics = await ExpectDiagnosticsAsync(harness, name, source, expectedCode);
            AssertTrue(diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
                $"{name} did not produce {expectedCode}.");
        }
    }

    private static async Task TestContextualIdentifiers(Harness harness)
    {
        const string source = """
            module true::false::null::match::if::await::with::route::command::effects::return::fn;
            struct effects { route: i32, return: i32, if: i32, true: i32, null: i32 }
            struct WithField { with: i32 }
            union Choice { return(null: i32) }

            fn route(command: i32) -> i32 effects {} {
                let return: i32 = command;
                let route: i32 = return;
                return route;
            }

            fn with(value: i32) -> i32 effects {} {
                let with: i32 = value;
                return with;
            }

            fn adapter(adapter: i32) -> i32 effects {} {
                return adapter;
            }
            fn make_choice(value: self::true::false::null::match::if::await::with::route::command::effects::return::fn::effects) -> self::true::false::null::match::if::await::with::route::command::effects::return::fn::Choice effects {} {
                return self::true::false::null::match::if::await::with::route::command::effects::return::fn::Choice.return(value.route + value.return + value.if + value.true + value.null);
            }

            pub fn main() -> i32 effects {} {
                let value: self::true::false::null::match::if::await::with::route::command::effects::return::fn::effects = self::true::false::null::match::if::await::with::route::command::effects::return::fn::effects { route: 1, return: 2, if: 3, true: 4, null: 5 };
                let choice: self::true::false::null::match::if::await::with::route::command::effects::return::fn::Choice = self::true::false::null::match::if::await::with::route::command::effects::return::fn::make_choice(value);
                let payload_value: i32 = match choice {
                    self::true::false::null::match::if::await::with::route::command::effects::return::fn::Choice.return(payload) => self::true::false::null::match::if::await::with::route::command::effects::return::fn::route(payload),
                };
                let with_field: self::true::false::null::match::if::await::with::route::command::effects::return::fn::WithField = self::true::false::null::match::if::await::with::route::command::effects::return::fn::WithField { with: self::true::false::null::match::if::await::with::route::command::effects::return::fn::with(payload_value) };
                return self::true::false::null::match::if::await::with::route::command::effects::return::fn::adapter(with_field.with);
            }
            """;

        var result = await harness.InvokeAsync("contextual-identifiers", "run", source);
        AssertRunOutput("15" + Environment.NewLine, result);

        const string hardKeywordSource = """
            module harness::hard_keyword_identifier;
            fn main() -> i32 effects {} {
                let if: i32 = 1;
                return 0;
            }
            """;
        var hardKeywordDiagnostics = await ExpectDiagnosticsAsync(
            harness, "hard-keyword-identifier", hardKeywordSource, "E_SYNTAX");
        AssertEqual("E_SYNTAX", hardKeywordDiagnostics.Single().Code,
            "A hard expression keyword must remain unavailable as a bare binding.");

        const string standaloneRouteSource = """
            module harness::standalone_route;
            route
            """;
        await ExpectDiagnosticsAsync(harness, "malformed-route-declaration", standaloneRouteSource, "E_ROUTE_DECL");
    }

    private static async Task TestAsyncManagedRuntime(Harness harness)
    {
        const string source = """
            module app::main;

            pub async fn identity<T>(value: T) -> T effects {} {
                return value;
            }

            fn async(value: i32) -> i32 effects {} {
                return value;
            }

            async fn descend(value: i32) -> i32 effects {} {
                if value == 0 { return 0; }
                return await self::app::main::descend(value - 1) + 1;
            }

            async fn is_even(value: i32) -> bool effects {} {
                if value == 0 { return true; }
                return await self::app::main::is_odd(value - 1);
            }

            async fn is_odd(value: i32) -> bool effects {} {
                if value == 0 { return false; }
                return await self::app::main::is_even(value - 1);
            }

            pub async fn main() -> i32 effects {} {
                let sum: i32 = await self::app::main::identity(20) + await self::app::main::identity(22);
                let text: Text = await self::app::main::identity("λ");
                let contextual_name: i32 = self::app::main::async(0);
                let recursive: i32 = await self::app::main::descend(2);
                let even: bool = await self::app::main::is_even(4);
                let odd: bool = await self::app::main::is_odd(3);
                if even {
                    if odd {
                        return sum + text.length + contextual_name + recursive;
                    }
                }
                return 0;
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "async-runtime-package",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source
            });

        var check = await harness.InvokePackageDirectoryAsync("async-runtime-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var build = await harness.InvokePackageDirectoryAsync("async-runtime-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        var artifact = ParseBuiltArtifact(build, "Built executable: ");
        using var receipt = await AssertBuildReceiptAsync(
            Path.GetDirectoryName(artifact)!,
            "managed",
            expectedRuntimeIdentifier: null,
            [Path.GetRelativePath(Path.GetDirectoryName(artifact)!, artifact).Replace(Path.DirectorySeparatorChar, '/')],
            packageRoot);

        var run = await harness.InvokePackageDirectoryAsync("async-runtime-run", packageRoot, "run");
        AssertRunOutput("45" + Environment.NewLine, run);
    }

    private static async Task TestAsyncDiagnostics(Harness harness)
    {
        var cases = new (string Name, string Source, string[] Codes, string RangeToken, int Occurrence)[]
        {
            ("await-outside-async", """
                module harness::await_outside_async;
                async fn target() -> i32 effects {} { return 1; }
                pub fn main() -> i32 effects {} { return await self::harness::await_outside_async::target(); }
                """, ["E_AWAIT_CONTEXT"], "await", 1),
            ("await-sync-call", """
                module harness::await_sync_call;
                fn sync_target() -> i32 effects {} { return 1; }
                pub async fn main() -> i32 effects {} { return await self::harness::await_sync_call::sync_target(); }
                """, ["E_AWAIT_SYNC"], "self", 1),
            ("await-non-call", """
                module harness::await_non_call;
                pub async fn main() -> i32 effects {} {
                    let value: i32 = 1;
                    return await value;
                }
                """, ["E_AWAIT_TARGET"], "await", 1),
            ("unawaited-async-call", """
                module harness::unawaited_async_call;
                async fn target() -> i32 effects {} { return 1; }
                pub async fn main() -> i32 effects {} { return self::harness::unawaited_async_call::target(); }
                """, ["E_ASYNC_CALL_UNAWAITED"], "self", 1),
            ("await-unresolved-target", """
                module harness::await_unresolved_target;
                pub async fn main() -> i32 effects {} { return await self::harness::await_unresolved_target::missing(); }
                """, ["E_NAME_UNRESOLVED"], "self", 1),
            ("await-missing-fsread-capability", """
                module harness::await_missing_fsread;
                pub async fn main() -> Result<Text, FsError> effects { fs.read } {
                    return await fs.read_text_async("missing.txt");
                }
                """, ["E_CAPABILITY_MISSING"], "fs", 2)
        };

        foreach (var testCase in cases)
        {
            var diagnostics = await ExpectDiagnosticsAsync(
                harness,
                testCase.Name,
                testCase.Source,
                testCase.Codes);
            var actualCodes = diagnostics.Select(diagnostic => diagnostic.Code).ToArray();
            AssertTrue(testCase.Codes.SequenceEqual(actualCodes, StringComparer.Ordinal),
                $"{testCase.Name} should produce exactly [{string.Join(", ", testCase.Codes)}], got [{string.Join(", ", actualCodes)}].");
            AssertRangeAtToken(testCase.Source, diagnostics.Single(), testCase.RangeToken, testCase.Occurrence);
        }
    }

    private static async Task TestStructDeclarationNames(Harness harness)
    {
        var cases = new (string Name, string Source, string ExpectedCode)[]
        {
            ("duplicate-struct-declaration", """
                module harness::duplicate_struct_declaration;
                pub struct Item {}
                pub struct Item { value: i32 }
                """, "E_NAME_DUPLICATE"),
            ("duplicate-struct-field-declaration", """
                module harness::duplicate_struct_field_declaration;
                pub struct Item { value: i32, value: Text }
                """, "E_NAME_DUPLICATE"),
            ("union-struct-name-collision", """
                module harness::union_struct_name_collision;
                pub union Item { Empty }
                pub struct Item { value: i32 }
                """, "E_NAME_DUPLICATE"),
            ("reserved-struct-name", """
                module harness::reserved_struct_name;
                pub struct match { value: i32 }
                """, "E_SYNTAX"),
            ("reserved-builtin-type-names", """
                module harness::reserved_builtin_type_names;
                pub struct i32 {}
                pub struct bool {}
                pub struct Text {}
                pub struct Option {}
                pub struct Result {}
                """, "E_NAME_DUPLICATE"),
        };

        foreach (var (name, source, expectedCode) in cases)
        {
            var diagnostics = await ExpectDiagnosticsAsync(harness, name, source, expectedCode);
            if (name == "reserved-builtin-type-names")
                AssertEqual(5, diagnostics.Count(diagnostic => diagnostic.Code == "E_NAME_DUPLICATE"),
                    "Each built-in type name must reject a struct redeclaration.");
        }
    }

    private static async Task TestStructVisibility(Harness harness)
    {
        var cases = new (string Name, string Source)[]
        {
            ("public-function-private-struct", """
                module harness::public_function_private_struct;
                pub struct Public { value: i32 }
                struct Hidden { value: i32 }
                pub fn expose(value: Option<Result<self::harness::public_function_private_struct::Hidden, Text>>) -> i32 effects {} { return 0; }
                """),
            ("public-union-private-struct", """
                module harness::public_union_private_struct;
                struct Hidden { value: i32 }
                pub union PublicChoice { Wrapped(Option<Result<self::harness::public_union_private_struct::Hidden, Text>>), Empty }
                """),
            ("public-struct-private-field-type", """
                module harness::public_struct_private_field;
                struct Hidden { value: i32 }
                pub struct PublicBox { value: Option<Result<self::harness::public_struct_private_field::Hidden, Text>> }
                """)
        };

        foreach (var (name, source) in cases)
            await ExpectDiagnosticsAsync(harness, name, source, "E_TYPE_VISIBILITY");
    }

    private static async Task TestDeepStructFieldChain(Harness harness)
    {
        var fieldChain = "node" + string.Concat(Enumerable.Repeat(".next", 300));
        var source = "module harness::deep_struct_field_chain;\n"
            + "pub struct Node { next: i32 }\n"
            + "pub fn main() -> i32 effects {} { let node: self::harness::deep_struct_field_chain::Node = self::harness::deep_struct_field_chain::Node { next: 0 }; return " + fieldChain + "; }\n";

        var diagnostics = await ExpectDiagnosticsAsync(harness, "deep-struct-field-chain", source, "E_SYNTAX");
        var deep = diagnostics.Single(diagnostic => diagnostic.Code == "E_SYNTAX");
        AssertEqual("Expression nesting is too deep", deep.Message,
            "A long field chain should produce the structured expression-depth diagnostic.");
    }

    private static async Task TestTypeMismatchContexts(Harness harness)
    {
        var cases = new (string Name, string Source)[]
        {
            ("wrong-argument", """
                module harness::wrong_argument;
                pub fn wants_i32(value: i32) -> i32 effects {} { return value; }
                pub fn main() -> i32 effects {} { return self::harness::wrong_argument::wants_i32(true); }
                """),
            ("wrong-local", """
                module harness::wrong_local;
                pub fn main() -> i32 effects {} { let value: i32 = true; return 0; }
                """),
            ("wrong-return", """
                module harness::wrong_return;
                pub fn value() -> i32 effects {} { return true; }
                pub fn main() -> i32 effects {} { return 0; }
                """),
            ("wrong-binary", """
                module harness::wrong_binary;
                pub fn main() -> i32 effects {} { return 1 + true; }
                """),
            ("wrong-union-payload", """
                module harness::wrong_union_payload;
                pub union Choice { Number(i32) }
                pub fn main() -> i32 effects {} { let value: self::harness::wrong_union_payload::Choice = self::harness::wrong_union_payload::Choice.Number(true); return 0; }
                """),
            ("wrong-match-arm", """
                module harness::wrong_match_arm;
                pub union Choice { First, Second }
                pub fn main() -> i32 effects {} {
                    let value: self::harness::wrong_match_arm::Choice = self::harness::wrong_match_arm::Choice.First;
                    return match value { self::harness::wrong_match_arm::Choice.First => 1, self::harness::wrong_match_arm::Choice.Second => false, };
                }
                """),
            ("option-does-not-implicitly-unwrap", """
                module harness::option_unwrap;
                pub fn unwrap(value: Option<i32>) -> i32 effects {} { return value; }
                pub fn main() -> i32 effects {} { return self::harness::option_unwrap::unwrap(Some(7)); }
                """),
            ("option-arguments-are-invariant", """
                module harness::option_types;
                pub fn use_integer(value: Option<i32>) -> i32 effects {} {
                    return match value { Some(number) => number, None => 0, };
                }
                pub fn main() -> i32 effects {} {
                    let value: Option<Text> = Some("text");
                    return self::harness::option_types::use_integer(value);
                }
                """),
            ("result-type-arguments-are-ordered", """
                module harness::result_types;
                pub fn use_result(value: Result<i32, Text>) -> i32 effects {} {
                    return match value { Ok(number) => number, Err(message) => 0, };
                }
                pub fn main() -> i32 effects {} {
                    let value: Result<Text, i32> = Err(1);
                    return self::harness::result_types::use_result(value);
                }
                """)
        };

        foreach (var (name, source) in cases)
        {
            var diagnostics = await ExpectDiagnosticsAsync(harness, name, source, "E_TYPE_MISMATCH");
            AssertTrue(diagnostics.Any(diagnostic => diagnostic.Code == "E_TYPE_MISMATCH"),
                $"{name} did not produce E_TYPE_MISMATCH.");
        }

        var badArgumentRun = await harness.InvokeAsync("wrong-argument-before-backend", "run", cases[0].Source);
        AssertRunRejectedBeforeBackend(badArgumentRun, "E_TYPE_MISMATCH");
        var badMatchRun = await harness.InvokeAsync("wrong-match-before-backend", "run", cases[5].Source);
        AssertRunRejectedBeforeBackend(badMatchRun, "E_TYPE_MISMATCH");
    }

    private static async Task TestInvalidMatches(Harness harness)
    {
        const string missingSource = """
            module harness::missing_match;
            pub union Choice { Yes, No }
            pub fn main() -> i32 effects {} {
                let value: self::harness::missing_match::Choice = self::harness::missing_match::Choice.Yes;
                return match value { self::harness::missing_match::Choice.Yes => 1, };
            }
            """;
        var missingDiagnostics = await ExpectDiagnosticsAsync(harness, "missing-match-arm", missingSource, "E_MATCH_NONEXHAUSTIVE");
        var missing = missingDiagnostics.Single(diagnostic => diagnostic.Code == "E_MATCH_NONEXHAUSTIVE");
        AssertTrue(missing.Message.Contains("Choice.No", StringComparison.Ordinal), "Non-exhaustive diagnostic must name Choice.No.");
        AssertRangeAtToken(missingSource, missing, "match", 1);

        const string duplicateSource = """
            module harness::duplicate_match;
            pub union Choice { Yes, No }
            pub fn main() -> i32 effects {} {
                let value: self::harness::duplicate_match::Choice = self::harness::duplicate_match::Choice.Yes;
                return match value { self::harness::duplicate_match::Choice.Yes => 1, self::harness::duplicate_match::Choice.Yes => 2, self::harness::duplicate_match::Choice.No => 0, };
            }
            """;
        var duplicateDiagnostics = await ExpectDiagnosticsAsync(harness, "duplicate-match-arm", duplicateSource, "E_MATCH_ARM_DUPLICATE");
        var duplicate = duplicateDiagnostics.Single(diagnostic => diagnostic.Code == "E_MATCH_ARM_DUPLICATE");
        AssertRangeAtToken(duplicateSource, duplicate, "self", 4);

        const string wrongUnionSource = """
            module harness::wrong_union_pattern;
            pub union Choice { First, Second }
            pub union Other { First, Second }
            pub fn main() -> i32 effects {} {
                let value: self::harness::wrong_union_pattern::Choice = self::harness::wrong_union_pattern::Choice.First;
                return match value { self::harness::wrong_union_pattern::Other.First => 1, self::harness::wrong_union_pattern::Other.Second => 0, };
            }
            """;
        await ExpectDiagnosticsAsync(harness, "wrong-union-pattern", wrongUnionSource, "E_TYPE_MISMATCH", "E_NAME_UNRESOLVED");
        const string aritySource = """
            module harness::match_arity;
            pub union Choice { Number(i32), Empty }
            pub fn main() -> i32 effects {} {
                let value: self::harness::match_arity::Choice = self::harness::match_arity::Choice.Empty;
                return match value { self::harness::match_arity::Choice.Number(first, second) => first, self::harness::match_arity::Choice.Empty => 0, };
            }
            """;
        await ExpectDiagnosticsAsync(harness, "wrong-match-payload-arity", aritySource, "E_TYPE_MISMATCH");
    }

    private static async Task TestNull(Harness harness)
    {
        const string expressionSource = """
            module harness::null_expression;
            pub fn main() -> i32 effects {} { return null; }
            """;
        var expressionDiagnostics = await ExpectDiagnosticsAsync(harness, "null-expression", expressionSource, "E_TYPE_MISMATCH");
        AssertRangeAtToken(expressionSource, expressionDiagnostics.Single(diagnostic => diagnostic.Code == "E_TYPE_MISMATCH"), "null", 1);

        const string identifierSource = """
            module harness::null_identifier;
            pub fn main() -> i32 effects {} { let null: i32 = 1; return null; }
            """;
        var identifierDiagnostics = await ExpectDiagnosticsAsync(harness, "null-identifier", identifierSource, "E_TYPE_MISMATCH", "E_SYNTAX");
        AssertRangeAtToken(identifierSource, identifierDiagnostics.Single(), "null", 1);
    }

    private static async Task TestDeepNesting(Harness harness)
    {
        var nestedType = "i32";
        for (var depth = 0; depth < 300; depth++) nestedType = $"Option<{nestedType}>";
        var source = $"module harness::deep_nesting;\npub fn value(input: {nestedType}) -> i32 effects {{}} {{ return 0; }}\n";

        var result = await harness.InvokeWithTimeoutAsync("deep-nesting", "check", source, TimeSpan.FromSeconds(15), "--json");
        AssertTrue(result.ExitCode != 0, Describe(result));
        using var document = JsonDocument.Parse(result.StandardOutput);
        var diagnostics = document.RootElement.GetProperty("diagnostics");
        AssertTrue(diagnostics.EnumerateArray().Any(diagnostic =>
            diagnostic.GetProperty("code").GetString() == "E_SYNTAX"
            && diagnostic.GetProperty("message").GetString() == "Type nesting is too deep"),
            "Deep nesting should produce the structured type-depth diagnostic.");
        var range = diagnostics[0].GetProperty("range");
        AssertTrue(range.GetProperty("startLine").GetInt32() >= 1
            && range.GetProperty("startColumn").GetInt32() >= 1
            && range.GetProperty("endColumn").GetInt32() > range.GetProperty("startColumn").GetInt32(),
            "Deep nesting diagnostic must include a non-empty source range.");
    }

    private static async Task<DiagnosticSnapshot[]> ExpectDiagnosticsAsync(Harness harness, string caseName, string source, params string[] expectedCodes)
    {
        var result = await harness.InvokeAsync(caseName, "check", source, "--json");
        AssertTrue(result.ExitCode != 0, $"Invalid source unexpectedly succeeded. {Describe(result)}");
        AssertEqual(string.Empty, result.StandardError, Describe(result));

        var diagnostics = ParseDiagnosticSnapshots(result.StandardOutput);
        AssertTrue(diagnostics.Any(diagnostic => expectedCodes.Contains(diagnostic.Code, StringComparer.Ordinal)),
            $"Expected one of [{string.Join(", ", expectedCodes)}]; got {string.Join(", ", diagnostics.Select(diagnostic => diagnostic.Code))}.");
        AssertTrue(diagnostics.All(diagnostic => diagnostic.File == harness.LastSourcePath
            && diagnostic.StartLine > 0 && diagnostic.StartColumn > 0
            && diagnostic.EndLine > 0 && diagnostic.EndColumn > diagnostic.StartColumn),
            "Every diagnostic should include the temporary source path and a non-empty range.");
        return diagnostics;
    }

    private static DiagnosticSnapshot[] ParseDiagnosticSnapshots(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("diagnostics").EnumerateArray().Select(item => new DiagnosticSnapshot(
            item.GetProperty("code").GetString() ?? string.Empty,
            item.GetProperty("message").GetString() ?? string.Empty,
            item.GetProperty("file").GetString() ?? string.Empty,
            item.GetProperty("range").GetProperty("startLine").GetInt32(),
            item.GetProperty("range").GetProperty("startColumn").GetInt32(),
            item.GetProperty("range").GetProperty("endLine").GetInt32(),
            item.GetProperty("range").GetProperty("endColumn").GetInt32())).ToArray();
    }

    private static void AssertRangeAtToken(string source, DiagnosticSnapshot diagnostic, string tokenText, int occurrence)
    {
        var position = PositionOf(source, OccurrenceIndex(source, tokenText, occurrence));
        AssertEqual(position.Line, diagnostic.StartLine, $"Unexpected diagnostic start line for token {tokenText}.");
        AssertEqual(position.Column, diagnostic.StartColumn, $"Unexpected diagnostic start column for token {tokenText}.");
        AssertEqual(position.Line, diagnostic.EndLine, $"Unexpected diagnostic end line for token {tokenText}.");
        AssertEqual(position.Column + tokenText.Length, diagnostic.EndColumn, $"Unexpected diagnostic end column for token {tokenText}.");
    }

    private static int OccurrenceIndex(string source, string token, int occurrence)
    {
        var searchFrom = 0;
        var found = 0;
        while (searchFrom <= source.Length - token.Length)
        {
            var index = source.IndexOf(token, searchFrom, StringComparison.Ordinal);
            if (index < 0) break;
            var previousIsIdentifier = index > 0 && IsIdentifierCharacter(source[index - 1]);
            var nextIndex = index + token.Length;
            var nextIsIdentifier = nextIndex < source.Length && IsIdentifierCharacter(source[nextIndex]);
            if (!previousIsIdentifier && !nextIsIdentifier)
            {
                found++;
                if (found == occurrence) return index;
            }
            searchFrom = index + token.Length;
        }

        throw new InvalidOperationException($"Could not find token occurrence {occurrence} of {token} in source.");
    }

    private static bool IsIdentifierCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static (int Line, int Column) PositionOf(string source, int index)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < index; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                column = 1;
            }
            else if (source[i] != '\r')
            {
                column++;
            }
        }

        return (line, column);
    }

    private static void AssertRunOutput(string expectedOutput, ProcessResult result)
    {
        AssertEqual(0, result.ExitCode, Describe(result));
        AssertEqual(expectedOutput, result.StandardOutput, Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
    }

    private static void AssertRunRejectedBeforeBackend(ProcessResult result, string diagnosticCode)
    {
        AssertTrue(result.ExitCode != 0, Describe(result));
        AssertEqual(string.Empty, result.StandardOutput, Describe(result));
        AssertTrue(result.StandardError.Contains(diagnosticCode, StringComparison.Ordinal),
            $"Expected {diagnosticCode} before backend execution. {Describe(result)}");
        AssertTrue(!result.StandardError.Contains("Unhandled exception", StringComparison.Ordinal)
            && !result.StandardError.Contains(" at ", StringComparison.Ordinal),
            $"The compiler should report a diagnostic instead of a backend stack trace. {Describe(result)}");
    }
    private static async Task TestLongBinaryChain(Harness harness)
    {
        var expression = string.Join(" + ", Enumerable.Repeat("1", 10_000));
        var source = "module harness::long_chain;\n"
            + "pub fn main() -> i32 effects {} { return " + expression + "; }\n";
        var result = await harness.InvokeWithTimeoutAsync("long-binary-chain", "check", source, TimeSpan.FromSeconds(15), "--json");

        AssertTrue(result.ExitCode != 0, Describe(result));
        using var document = JsonDocument.Parse(result.StandardOutput);
        var diagnostics = document.RootElement.GetProperty("diagnostics");
        AssertTrue(diagnostics.EnumerateArray().Any(diagnostic =>
            diagnostic.GetProperty("code").GetString() == "E_SYNTAX"
            && diagnostic.GetProperty("message").GetString() == "Expression nesting is too deep"),
            "A long left-associated expression must return the parser depth diagnostic.");
    }

    private static async Task TestSignedI32(Harness harness)
    {
        const string negativeOneSource = """
            module harness::negative_one;
            pub fn main() -> i32 effects {} { return -1; }
            """;
        const string minimumSource = """
            module harness::minimum;
            pub fn main() -> i32 effects {} { return -2147483648; }
            """;
        const string negativeCallSource = """
            module harness::negative_call;
            pub fn value() -> i32 effects {} { return 3; }
            pub fn main() -> i32 effects {} { return -self::harness::negative_call::value(); }
            """;
        const string tooSmallSource = """
            module harness::too_small;
            pub fn main() -> i32 effects {} { return -2147483649; }
            """;
        const string negatedMinimumSource = """
            module harness::negated_minimum;
            pub fn main() -> i32 effects {} { return -(-2147483648); }
            """;

        AssertRunOutput("-1" + Environment.NewLine, await harness.InvokeAsync("negative-one", "run", negativeOneSource));
        AssertRunOutput("-2147483648" + Environment.NewLine, await harness.InvokeAsync("minimum-i32", "run", minimumSource));
        AssertRunOutput("-3" + Environment.NewLine, await harness.InvokeAsync("negative-call", "run", negativeCallSource));
        await ExpectDiagnosticsAsync(harness, "too-small-i32", tooSmallSource, "E_TYPE_MISMATCH");

        var negatedMinimum = await harness.InvokeAsync("negated-minimum-overflow", "run", negatedMinimumSource);
        AssertEqual(70, negatedMinimum.ExitCode, Describe(negatedMinimum));
        AssertEqual("Runtime fault" + Environment.NewLine, negatedMinimum.StandardError, Describe(negatedMinimum));
    }
    private static async Task TestEffectAnnotationsAndCapabilities(Harness harness)
    {
        const string pureSource = "module harness::effect_pure;\n"
            + "pub fn main() -> i32 effects {} { return 42; }\n";
        AssertRunOutput("42" + Environment.NewLine, await harness.InvokeAsync("effect-pure-success", "run", pureSource));

        const string directSource = "module harness::effect_direct;\n"
            + "pub fn read(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n";
        var directCheck = await harness.InvokeAsync("effect-direct-success", "check", directSource, "--json");
        AssertEqual(0, directCheck.ExitCode, Describe(directCheck));
        AssertEqual(0, ParseDiagnosticSnapshots(directCheck.StandardOutput).Length,
            "A direct fs.read operation inside its declared upper bound should check cleanly.");

        const string directExceededSource = "module harness::effect_direct_exceeded;\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return fs.read_text(\"x\"); }\n";
        var directExceeded = await ExpectDiagnosticsAsync(
            harness, "effect-direct-exceeded", directExceededSource, "E_EFFECT_EXCEEDED");
        AssertEqual(1, directExceeded.Length, "A direct effect outside its upper bound should produce one diagnostic.");
        AssertRangeAtToken(directExceededSource, directExceeded.Single(), "bad", 1);
        AssertEqual(
            "Effect 'fs.read' is not declared by function 'harness::effect_direct_exceeded::bad'; shortest call path: harness::effect_direct_exceeded::bad -> fs.read_text",
            directExceeded.Single().Message,
            "A direct-effect diagnostic should show the shortest path to the operation.");

        const string unknownEffectSource = "module harness::effect_unknown_annotation;\n"
            + "pub fn bad() -> i32 effects { fs.unknown } { return 1; }\n";
        var unknownEffect = await ExpectDiagnosticsAsync(
            harness, "effect-unknown-annotation", unknownEffectSource, "E_EFFECT_UNKNOWN");
        AssertEqual(1, unknownEffect.Length, "An unknown annotation should produce one diagnostic.");

        const string duplicateEffectSource = "module harness::effect_duplicate_annotation;\n"
            + "pub fn bad() -> i32 effects { fs.read, fs.read } { return 1; }\n";
        var duplicateEffect = await ExpectDiagnosticsAsync(
            harness, "effect-duplicate-annotation", duplicateEffectSource, "E_EFFECT_DUPLICATE");
        AssertEqual(1, duplicateEffect.Length, "A repeated annotation should produce one diagnostic.");

        const string missingCapabilitySource = "module harness::effect_missing_capability;\n"
            + "pub fn bad() -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n";
        var missingCapability = await ExpectDiagnosticsAsync(
            harness, "effect-missing-capability", missingCapabilitySource, "E_CAPABILITY_MISSING");
        AssertRangeAtToken(missingCapabilitySource, missingCapability.Single(), "fs", 2);

        const string wrongCapabilitySource = "module harness::effect_wrong_capability;\n"
            + "pub fn bad(value: Text) -> Result<Text, FsError> effects { fs.read } { return value.read_text(\"x\"); }\n";
        var wrongCapability = await ExpectDiagnosticsAsync(
            harness, "effect-wrong-capability", wrongCapabilitySource, "E_CAPABILITY_MISSING");
        AssertRangeAtToken(wrongCapabilitySource, wrongCapability.Single(), "value", 2);

        const string invalidFunctionCallSource = "module harness::effect_invalid_function_call;\n"
            + "fn read(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return self::harness::effect_invalid_function_call::read(); }\n";
        var invalidFunctionCall = await ExpectDiagnosticsAsync(
            harness, "effect-invalid-function-call", invalidFunctionCallSource, "E_TYPE_MISMATCH");
        AssertTrue(invalidFunctionCall.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "An invalid-arity function call must not add a transitive effect diagnostic.");

        const string invalidIntrinsicAritySource = "module harness::effect_invalid_intrinsic_arity;\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return fs.read_text(); }\n";
        var invalidIntrinsicArity = await ExpectDiagnosticsAsync(
            harness, "effect-invalid-intrinsic-arity", invalidIntrinsicAritySource, "E_TYPE_MISMATCH");
        AssertTrue(invalidIntrinsicArity.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "An invalid-arity filesystem operation must not seed an inferred effect.");

        const string invalidIntrinsicPathSource = "module harness::effect_invalid_intrinsic_path;\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return fs.read_text(1); }\n";
        var invalidIntrinsicPath = await ExpectDiagnosticsAsync(
            harness, "effect-invalid-intrinsic-path", invalidIntrinsicPathSource, "E_TYPE_MISMATCH");
        AssertTrue(invalidIntrinsicPath.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "A wrong-typed filesystem path must not seed an inferred effect.");

        const string localReceiverSource = "module harness::effect_local_receiver;\n"
            + "pub fn read(FsRead: FsRead) -> Result<Text, FsError> effects { fs.read } { return FsRead.read_text(\"x\"); }\n";
        var localReceiver = await harness.InvokeAsync("effect-local-receiver", "check", localReceiverSource, "--json");
        AssertEqual(0, localReceiver.ExitCode, Describe(localReceiver));
        AssertEqual(0, ParseDiagnosticSnapshots(localReceiver.StandardOutput).Length,
            "The local receiver named FsRead should take precedence over the type name.");
    }

    private static async Task TestEffectInferencePaths(Harness harness)
    {
        const string source = "module harness::effect_paths;\n"
            + "fn leaf(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n"
            + "fn deep_three(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return self::harness::effect_paths::leaf(fs); }\n"
            + "fn deep_two(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return self::harness::effect_paths::deep_three(fs); }\n"
            + "fn deep_one(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return self::harness::effect_paths::deep_two(fs); }\n"
            + "fn near(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return self::harness::effect_paths::leaf(fs); }\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} {\n"
            + "    let nearer: Result<Text, FsError> = self::harness::effect_paths::near(fs);\n"
            + "    return self::harness::effect_paths::deep_one(fs);\n"
            + "}\n";

        var firstRun = await ExpectDiagnosticsAsync(harness, "effect-shortest-path-first", source, "E_EFFECT_EXCEEDED");
        var secondRun = await ExpectDiagnosticsAsync(harness, "effect-shortest-path-second", source, "E_EFFECT_EXCEEDED");
        const string expectedMessage = "Effect 'fs.read' is not declared by function 'harness::effect_paths::bad'; shortest call path: harness::effect_paths::bad -> harness::effect_paths::near -> harness::effect_paths::leaf -> fs.read_text";
        AssertEqual(expectedMessage, firstRun.Single().Message, "The checker should choose the shortest call path.");
        AssertEqual(firstRun.Single().Message, secondRun.Single().Message,
            "Call-path diagnostic content should be deterministic across runs.");
        AssertRangeAtToken(source, firstRun.Single(), "bad", 1);

        const string tiedPathsSource = "module harness::effect_tie;\n"
            + "fn leaf(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n"
            + "fn zeta(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return self::harness::effect_tie::leaf(fs); }\n"
            + "fn alpha(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return self::harness::effect_tie::leaf(fs); }\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} {\n"
            + "    let first: Result<Text, FsError> = self::harness::effect_tie::zeta(fs);\n"
            + "    return self::harness::effect_tie::alpha(fs);\n"
            + "}\n";
        var tiedPaths = await ExpectDiagnosticsAsync(harness, "effect-shortest-path-tie", tiedPathsSource, "E_EFFECT_EXCEEDED");
        AssertEqual(
            "Effect 'fs.read' is not declared by function 'harness::effect_tie::bad'; shortest call path: harness::effect_tie::bad -> harness::effect_tie::alpha -> harness::effect_tie::leaf -> fs.read_text",
            tiedPaths.Single().Message,
            "Equal-length effect paths should use canonical function ordering, independent of call source order.");

        const string recursiveSource = "module harness::effect_recursive;\n"
            + "fn first(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {\n"
            + "    let next: Result<Text, FsError> = self::harness::effect_recursive::second(fs);\n"
            + "    return fs.read_text(\"x\");\n"
            + "}\n"
            + "fn second(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return self::harness::effect_recursive::first(fs); }\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return self::harness::effect_recursive::second(fs); }\n";
        var recursiveDiagnostics = await ExpectDiagnosticsAsync(
            harness, "effect-recursive-cycle", recursiveSource, "E_EFFECT_EXCEEDED");
        AssertEqual(
            "Effect 'fs.read' is not declared by function 'harness::effect_recursive::bad'; shortest call path: harness::effect_recursive::bad -> harness::effect_recursive::second -> harness::effect_recursive::first -> fs.read_text",
            recursiveDiagnostics.Single().Message,
            "Effect inference should converge through recursive call cycles and retain the shortest path.");
    }

    private static async Task TestSqliteInspectEffects(Harness harness)
    {
        const string source = "module app::main;\n"
            + "struct Parameters { id: i32 }\n"
            + "struct Row { id: i32 }\n"
            + "union Reply { Ready }\n"
            + "fn load_record(db: DbRead) -> Result<Option<self::app::main::Row>, DbError> effects { db.read } {\n"
            + "    let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one(\"SELECT id FROM sample WHERE id = $id\", self::app::main::Parameters { id: 1 });\n"
            + "    return loaded;\n"
            + "}\n"
            + "struct WriteParameters { id: i32 }\n"
            + "fn write_record(db: DbWrite) -> Result<i32, DbError> effects { db.write } {\n"
            + "    let written: Result<i32, DbError> = db.execute(\"INSERT INTO sample (id) VALUES ($id)\", self::app::main::WriteParameters { id: 2 });\n"
            + "    return written;\n"
            + "}\n"
            + "fn transaction_record(db: DbWrite) -> Result<bool, DbError> effects { db.write } {\n"
            + "    with db.begin() as tx {\n"
            + "        let written: Result<i32, DbError> = tx.execute(\"INSERT INTO sample (id) VALUES ($id)\", self::app::main::WriteParameters { id: 3 });\n"
            + "        return match written { Ok(count) => tx.commit(), Err(error) => Err(error) };\n"
            + "    }\n"
            + "}\n"
            + "fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {\n"
            + "    let loaded: Result<Option<self::app::main::Row>, DbError> = self::app::main::load_record(db);\n"
            + "    return self::app::main::Reply.Ready;\n"
            + "}\n"
            + "route GET \"/\" { handler: self::app::main::ready; response Ready: 200; }\n";
        var packageRoot = await harness.WritePackageAsync(
            "sqlite-inspect-effects",
            "name = \"sqlite-inspect-effects\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "sqlite_path = \"data/inspect.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source,
                ["db/schema.sql"] = "CREATE TABLE IF NOT EXISTS sample (id INTEGER PRIMARY KEY);\n"
            });

        var read = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::app::main::load_record", "--json");
        AssertEqual(0, read.ExitCode, Describe(read));
        using (var readJson = JsonDocument.Parse(read.StandardOutput))
        {
            var report = readJson.RootElement;
            AssertEqual("db.read", JsonStringArrayText(report.GetProperty("declared_effects")), "Declared DB read effects should be visible.");
            AssertEqual("db.read", JsonStringArrayText(report.GetProperty("inferred_effects")), "Inferred DB read effects should be visible.");
            AssertEqual("db.read", JsonStringArrayText(report.GetProperty("required_capabilities")), "DbRead should require the db.read grant.");
            AssertEqual("db.read,db.write,net.listen", JsonStringArrayText(report.GetProperty("manifest_grants")), "Manifest grants should remain visible in sorted order.");
            var operations = report.GetProperty("trusted_operations").EnumerateArray().ToArray();
            AssertEqual(1, operations.Length, "A read-only SQLite function should report only the DbRead adapter.");
            AssertEqual("DbRead.query_one", operations[0].GetProperty("operation").GetString(), "The read adapter operation should be named.");
            AssertEqual("trusted_adapter", operations[0].GetProperty("trust").GetString(), "The read adapter should be identified as trusted runtime code.");
            AssertEqual("db.read", JsonStringArrayText(operations[0].GetProperty("effects")), "The read adapter should carry db.read.");
        }

        var write = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::app::main::write_record", "--json");
        AssertEqual(0, write.ExitCode, Describe(write));
        using (var writeJson = JsonDocument.Parse(write.StandardOutput))
        {
            var report = writeJson.RootElement;
            AssertEqual("db.write", JsonStringArrayText(report.GetProperty("declared_effects")), "Declared DB write effects should be visible.");
            AssertEqual("db.write", JsonStringArrayText(report.GetProperty("inferred_effects")), "Inferred DB write effects should be visible.");
            AssertEqual("db.write", JsonStringArrayText(report.GetProperty("required_capabilities")), "DbWrite should require the db.write grant.");
            var operations = report.GetProperty("trusted_operations").EnumerateArray().ToArray();
            AssertEqual(1, operations.Length, "A write-only SQLite function should report only the DbWrite adapter.");
            AssertEqual("DbWrite.execute", operations[0].GetProperty("operation").GetString(), "The write adapter operation should be named.");
            AssertEqual("trusted_adapter", operations[0].GetProperty("trust").GetString(), "The write adapter should be identified as trusted runtime code.");
            AssertEqual("db.write", JsonStringArrayText(operations[0].GetProperty("effects")), "The write adapter should carry db.write.");
        }

        var transaction = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::app::main::transaction_record", "--json");
        AssertEqual(0, transaction.ExitCode, Describe(transaction));
        using (var transactionJson = JsonDocument.Parse(transaction.StandardOutput))
        {
            var report = transactionJson.RootElement;
            AssertEqual("db.write", JsonStringArrayText(report.GetProperty("inferred_effects")),
                "A transaction-only function should retain its db.write effect.");
            var operations = report.GetProperty("trusted_operations").EnumerateArray().ToArray();
            AssertEqual("DbWrite.begin,Transaction.commit,Transaction.execute",
                string.Join(",", operations.Select(operation => operation.GetProperty("operation").GetString())),
                "Inspect effects should report the actual transaction boundary and operations, not DbWrite.execute.");
            AssertTrue(operations.All(operation =>
                    operation.GetProperty("trust").GetString() == "trusted_adapter"
                    && JsonStringArrayText(operation.GetProperty("effects")) == "db.write"),
                "Every transaction adapter operation should be labeled as trusted db.write runtime code.");
        }

        static string JsonStringArrayText(JsonElement element) =>
            string.Join(",", element.EnumerateArray().Select(value => value.GetString()));
    }

    private static async Task TestInspectEffects(Harness harness)
    {
        var scanPackage = Path.Combine(harness.RepositoryRoot, "examples", "scan-cli");
        var scan = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", scanPackage, "self::app::scan::run", "--json");
        AssertEqual(0, scan.ExitCode, Describe(scan));
        AssertEqual(string.Empty, scan.StandardError, Describe(scan));
        using var scanJson = JsonDocument.Parse(scan.StandardOutput);
        var scanReport = scanJson.RootElement;
        AssertEqual(1, scanReport.GetProperty("schema_version").GetInt32(),
            "Inspect-effects JSON schema version must be 1.");
        AssertEqual("self::app::scan::run", scanReport.GetProperty("symbol").GetString(),
            "The report should identify the requested function symbol.");
        AssertJsonStringArray(scanReport.GetProperty("declared_effects"), ["fs.read"]);
        AssertJsonStringArray(scanReport.GetProperty("inferred_effects"), ["fs.read"]);
        AssertJsonStringArray(scanReport.GetProperty("required_capabilities"), ["fs.read"]);
        AssertJsonStringArray(scanReport.GetProperty("manifest_grants"), ["fs.read"]);
        AssertEffectPath(scanReport, "fs.read", "app::scan::run -> fs.read_text_async");

        var trustedOperations = scanReport.GetProperty("trusted_operations").EnumerateArray().ToArray();
        AssertEqual(3, trustedOperations.Length,
            "The scan report should identify the two CLI host operations and the FsRead adapter.");
        AssertTrustedOperation(trustedOperations[0], "cli.argument_decode", "trusted_host", []);
        AssertTrustedOperation(trustedOperations[1], "cli.output", "trusted_host", []);
        AssertTrustedOperation(trustedOperations[2], "FsRead.read_text_async", "trusted_adapter", ["fs.read"]);

        var repeatedScan = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", scanPackage, "self::app::scan::run", "--json");
        AssertEqual(0, repeatedScan.ExitCode, Describe(repeatedScan));
        AssertEqual(scan.StandardOutput, repeatedScan.StandardOutput,
            "Repeated inspect-effects calls must produce byte-identical JSON.");

        const string loopSource = """
            module app::effects;
            pub fn loop_read(fs: FsRead, paths: List<Text>) -> i32 effects { fs.read } {
                for path in paths {
                    let loaded: Result<Text, FsError> = fs.read_text(path);
                }
                return 0;
            }
            """;
        var loopPackage = await harness.WritePackageAsync(
            "inspect-effects-list-loop",
            LibraryPackageManifest("inspect-effects-list-loop"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/effects.hob"] = loopSource
            });
        var loopRead = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", loopPackage, "self::app::effects::loop_read", "--json");
        AssertEqual(0, loopRead.ExitCode, Describe(loopRead));
        AssertEqual(string.Empty, loopRead.StandardError, Describe(loopRead));
        using (var loopJson = JsonDocument.Parse(loopRead.StandardOutput))
        {
            var loopReport = loopJson.RootElement;
            AssertJsonStringArray(loopReport.GetProperty("inferred_effects"), ["fs.read"]);
            AssertEffectPath(loopReport, "fs.read", "app::effects::loop_read -> fs.read_text");
            var operations = loopReport.GetProperty("trusted_operations").EnumerateArray().ToArray();
            AssertEqual(1, operations.Length,
                "A loop-only filesystem read should report exactly one trusted adapter operation.");
            AssertTrustedOperation(operations[0], "FsRead.read_text", "trusted_adapter", ["fs.read"]);
        }

        var pure = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", scanPackage, "self::app::scan::describe_error", "--json");
        AssertEqual(0, pure.ExitCode, Describe(pure));
        using (var pureJson = JsonDocument.Parse(pure.StandardOutput))
        {
            var pureReport = pureJson.RootElement;
            AssertJsonStringArray(pureReport.GetProperty("declared_effects"), []);
            AssertJsonStringArray(pureReport.GetProperty("inferred_effects"), []);
            AssertEqual(0, pureReport.GetProperty("effect_paths").GetArrayLength(),
                "A pure function should have no effect paths.");
            AssertJsonStringArray(pureReport.GetProperty("required_capabilities"), []);
            AssertJsonStringArray(pureReport.GetProperty("manifest_grants"), ["fs.read"]);
        }

        var malformedInvocation = await harness.InvokeCompilerCommandAsync("inspect", "effects");
        AssertEqual(2, malformedInvocation.ExitCode, Describe(malformedInvocation));
        AssertEqual(string.Empty, malformedInvocation.StandardOutput, Describe(malformedInvocation));
        AssertTrue(malformedInvocation.StandardError.StartsWith("Usage: hob ", StringComparison.Ordinal)
            && malformedInvocation.StandardError.Contains("inspect effects", StringComparison.Ordinal),
            $"A malformed inspect invocation should show its usage. {Describe(malformedInvocation)}");

        await AssertInspectUnresolvedSymbolAsync(scanPackage, "validation::text::validation::normalize");
        await AssertInspectUnresolvedSymbolAsync(scanPackage, "self::app");
        await AssertInspectUnresolvedSymbolAsync(scanPackage, "self::app::scan::missing");

        const string pathsSource = """
            module app::effects;
            fn leaf(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                return fs.read_text("path");
            }
            fn zeta(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                return self::app::effects::leaf(fs);
            }
            fn alpha(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                return self::app::effects::leaf(fs);
            }
            pub fn transitive(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                let first: Result<Text, FsError> = self::app::effects::zeta(fs);
                return self::app::effects::alpha(fs);
            }
            fn cycle_first(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                return self::app::effects::cycle_second(fs);
            }
            fn cycle_second(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                let prior: Result<Text, FsError> = self::app::effects::cycle_first(fs);
                return fs.read_text("path");
            }
            pub fn recursive(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                return self::app::effects::cycle_first(fs);
            }
            fn private_root() -> i32 effects {} { return 23; }
            """;
        var pathPackage = await harness.WritePackageAsync(
            "inspect-effects-transitive-recursive",
            LibraryPackageManifest("inspect-effects-paths"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/effects.hob"] = pathsSource
            });
        var transitive = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", pathPackage,
            "self::app::effects::transitive", "--json");
        AssertEqual(0, transitive.ExitCode, Describe(transitive));
        using (var transitiveJson = JsonDocument.Parse(transitive.StandardOutput))
            AssertEffectPath(transitiveJson.RootElement, "fs.read",
                "app::effects::transitive -> app::effects::alpha -> app::effects::leaf -> fs.read_text");
        var repeatedTransitive = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", pathPackage,
            "self::app::effects::transitive", "--json");
        AssertEqual(0, repeatedTransitive.ExitCode, Describe(repeatedTransitive));
        AssertEqual(transitive.StandardOutput, repeatedTransitive.StandardOutput,
            "Repeated inspection must preserve the canonical transitive shortest path byte-for-byte.");

        var recursive = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", pathPackage,
            "self::app::effects::recursive", "--json");
        AssertEqual(0, recursive.ExitCode, Describe(recursive));
        using (var recursiveJson = JsonDocument.Parse(recursive.StandardOutput))
            AssertEffectPath(recursiveJson.RootElement, "fs.read",
                "app::effects::recursive -> app::effects::cycle_first -> app::effects::cycle_second -> fs.read_text");
        var repeatedRecursive = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", pathPackage,
            "self::app::effects::recursive", "--json");
        AssertEqual(0, repeatedRecursive.ExitCode, Describe(repeatedRecursive));
        AssertEqual(recursive.StandardOutput, repeatedRecursive.StandardOutput,
            "Repeated inspection must preserve the canonical recursive shortest path byte-for-byte.");

        var privateRoot = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", pathPackage, "self::app::effects::private_root", "--json");
        AssertEqual(0, privateRoot.ExitCode, Describe(privateRoot));
        using (var privateRootJson = JsonDocument.Parse(privateRoot.StandardOutput))
        {
            AssertEqual("self::app::effects::private_root",
                privateRootJson.RootElement.GetProperty("symbol").GetString(),
                "Inspection should resolve a private root-package function by its fully qualified symbol.");
            AssertJsonStringArray(privateRootJson.RootElement.GetProperty("inferred_effects"), []);
        }

        const string labelRootManifest = "name = \"inspect-effect-label-root\"\n"
            + "version = \"0.1.0\"\n"
            + "kind = \"lib\"\n"
            + "source_root = \"src\"\n"
            + "\n[dependencies]\nhelper = \"../helper\"\n";
        var labelPackage = await harness.WritePackageGraphAsync(
            "inspect-effect-dependency-label",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(labelRootManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/shared.hob"] = "module app::shared;\n"
                            + "pub fn scan(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {\n"
                            + "    return helper::app::shared::scan(fs);\n"
                            + "}\n"
                    }),
                ["helper"] = new PackageFixture(LibraryPackageManifest("inspect-effect-label-helper"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/shared.hob"] = "module app::shared;\n"
                            + "pub fn scan(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {\n"
                            + "    return fs.read_text(\"path\");\n"
                            + "}\n"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "inspect-effect-dependency-label-lock", labelPackage, "lock"));
        var labeledDependency = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", labelPackage, "self::app::shared::scan", "--json");
        AssertEqual(0, labeledDependency.ExitCode, Describe(labeledDependency));
        using (var labeledJson = JsonDocument.Parse(labeledDependency.StandardOutput))
        {
            var path = labeledJson.RootElement.GetProperty("effect_paths").EnumerateArray()
                .Single(candidate => candidate.GetProperty("effect").GetString() == "fs.read");
            var steps = path.GetProperty("steps").EnumerateArray()
                .Select(step => step.GetString() ?? string.Empty)
                .ToArray();
            AssertTrue(steps.SequenceEqual(
                    [
                        "app::shared::scan",
                        "inspect-effect-label-helper@0.1.0::app::shared::scan",
                        "fs.read_text"
                    ],
                    StringComparer.Ordinal),
                $"Dependency calls should use stable package labels even when module/function names collide. Steps: {string.Join(" -> ", steps)}");
            AssertTrue(steps[0] != steps[1],
                "A dependency step must remain distinguishable from the root step with the same module/function name.");
            AssertTrue(steps.All(step => !Path.IsPathRooted(step)
                    && !step.Contains(labelPackage, StringComparison.Ordinal)),
                "Effect paths must not expose package directories or absolute filesystem paths.");
        }

        const string invalidSource = "module app::broken; pub fn broken() -> i32 effects {} { return true; }";
        var invalidPackage = await harness.WritePackageAsync(
            "inspect-effects-compiler-errors",
            LibraryPackageManifest("inspect-effects-invalid"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/broken.hob"] = invalidSource
            });
        var compilerError = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", invalidPackage,
            "self::app::broken::broken", "--json");
        AssertTrue(compilerError.ExitCode != 0, "Inspection must reject a source package with compiler errors.");
        AssertEqual(string.Empty, compilerError.StandardError, Describe(compilerError));
        var compilerDiagnostics = ParseDiagnosticSnapshots(compilerError.StandardOutput);
        AssertTrue(compilerDiagnostics.Any(diagnostic => diagnostic.Code == "E_TYPE_MISMATCH"),
            $"The compiler diagnostic should be returned before an effects report. {compilerError.StandardOutput}");
        using (var compilerJson = JsonDocument.Parse(compilerError.StandardOutput))
            AssertTrue(!compilerJson.RootElement.TryGetProperty("declared_effects", out _),
                "A compiler error must prevent a partial effect report.");

        const string rootManifest = "name = \"inspect-lock-root\"\n"
            + "version = \"0.1.0\"\n"
            + "kind = \"lib\"\n"
            + "source_root = \"src\"\n"
            + "\n[dependencies]\nvalidation = \"../validation\"\n";
        var lockPackage = await harness.WritePackageGraphAsync(
            "inspect-effects-lock",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(rootManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/effects.hob"] = "module app::effects; pub fn pure() -> i32 effects {} { return 1; }"
                    }),
                ["validation"] = new PackageFixture(
                    LibraryPackageManifest("inspect-validation"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/validation.hob"] = "module validation; pub fn marker() -> i32 effects {} { return 1; }"
                    })
            });
        var missingLock = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", lockPackage,
            "self::app::effects::pure", "--json");
        AssertInspectLockFailure(missingLock, "A missing dependency lock must stop inspection before a report.");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "inspect-effects-create-lock", lockPackage, "lock"));
        var dependencyFile = Path.GetFullPath(Path.Combine(lockPackage, "..", "validation", "src", "validation.hob"));
        await File.WriteAllTextAsync(dependencyFile,
            "module validation; pub fn marker() -> i32 effects {} { return 2; }");
        var staleLock = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", lockPackage,
            "self::app::effects::pure", "--json");
        AssertInspectLockFailure(staleLock, "A stale dependency lock must stop inspection before a report.");

        async Task AssertInspectUnresolvedSymbolAsync(string package, string symbol)
        {
            var result = await harness.InvokeCompilerCommandAsync(
                "inspect", "effects", package, symbol, "--json");
            AssertEqual(1, result.ExitCode,
                $"Inspecting unresolved symbol '{symbol}' should return a compiler diagnostic. {Describe(result)}");
            AssertEqual(string.Empty, result.StandardError, Describe(result));
            using var diagnosticsJson = JsonDocument.Parse(result.StandardOutput);
            AssertEqual(1, diagnosticsJson.RootElement.GetProperty("schemaVersion").GetInt32(),
                "Symbol lookup errors should use the stable diagnostics JSON schema.");
            var diagnostics = ParseDiagnosticSnapshots(result.StandardOutput);
            AssertEqual(1, diagnostics.Length, "Each unresolved symbol should produce one structured diagnostic.");
            AssertEqual("E_NAME_UNRESOLVED", diagnostics[0].Code,
                $"Expected E_NAME_UNRESOLVED for inspect symbol '{symbol}'. {result.StandardOutput}");
        }

        static void AssertJsonStringArray(JsonElement element, string[] expected)
        {
            var actual = element.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
            AssertTrue(expected.SequenceEqual(actual, StringComparer.Ordinal),
                $"Expected JSON string array [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }

        static void AssertEffectPath(JsonElement report, string effect, string expectedSteps)
        {
            var matching = report.GetProperty("effect_paths").EnumerateArray()
                .Single(path => path.GetProperty("effect").GetString() == effect);
            var steps = matching.GetProperty("steps").EnumerateArray()
                .Select(step => step.GetString() ?? string.Empty);
            AssertEqual(expectedSteps, string.Join(" -> ", steps),
                $"Unexpected shortest path for effect '{effect}'.");
        }

        static void AssertTrustedOperation(JsonElement operation, string expectedOperation, string expectedTrust, string[] expectedEffects)
        {
            AssertEqual(expectedOperation, operation.GetProperty("operation").GetString(),
                "Trusted operation names and order should be stable.");
            AssertEqual(expectedTrust, operation.GetProperty("trust").GetString(),
                $"Unexpected trust classification for {expectedOperation}.");
            AssertJsonStringArray(operation.GetProperty("effects"), expectedEffects);
        }

        static void AssertInspectLockFailure(ProcessResult result, string message)
        {
            AssertTrue(result.ExitCode != 0, message + " " + Describe(result));
            AssertTrue(result.StandardOutput.Contains("E_LOCK", StringComparison.Ordinal)
                || result.StandardError.Contains("E_LOCK", StringComparison.Ordinal),
                message + " Expected E_LOCK. " + Describe(result));
            AssertTrue(!result.StandardOutput.Contains("declared_effects", StringComparison.Ordinal),
                message + " A report must not be emitted for a stale or missing lock.");
        }
    }

    private static async Task TestAsyncEffects(Harness harness)
    {
        const string source = """
            module app::async_effects;

            async fn read_leaf(fs: FsRead, path: Text) -> Result<Text, FsError> effects { fs.read } {
                return await fs.read_text_async(path);
            }

            pub async fn read_root(fs: FsRead, path: Text) -> Result<Text, FsError> effects { fs.read } {
                return await self::app::async_effects::read_leaf(fs, path);
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "async-inspect-effects",
            LibraryPackageManifest("async-inspect-effects"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/async_effects.hob"] = source
            });

        var result = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::app::async_effects::read_root", "--json");
        AssertEqual(0, result.ExitCode, Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        using var document = JsonDocument.Parse(result.StandardOutput);
        var report = document.RootElement;
        AssertEqual(1, report.GetProperty("schema_version").GetInt32(),
            "Async calls must preserve inspect-effects schema version 1.");
        AssertJsonStringArray(report.GetProperty("declared_effects"), ["fs.read"]);
        AssertJsonStringArray(report.GetProperty("inferred_effects"), ["fs.read"]);
        AssertJsonStringArray(report.GetProperty("required_capabilities"), ["fs.read"]);
        var effectPath = report.GetProperty("effect_paths").EnumerateArray()
            .Single(path => path.GetProperty("effect").GetString() == "fs.read");
        var effectSteps = effectPath.GetProperty("steps").EnumerateArray()
            .Select(step => step.GetString() ?? string.Empty);
        AssertEqual("app::async_effects::read_root -> app::async_effects::read_leaf -> fs.read_text_async",
            string.Join(" -> ", effectSteps),
            "The inferred async effect path should cross the async function before the adapter.");
        var operations = report.GetProperty("trusted_operations").EnumerateArray().ToArray();
        AssertEqual(1, operations.Length, "An async FsRead call should report one reachable adapter operation.");
        AssertEqual("FsRead.read_text_async", operations[0].GetProperty("operation").GetString(),
            "The async filesystem adapter should have a stable operation name.");
        AssertEqual("trusted_adapter", operations[0].GetProperty("trust").GetString(),
            "The async filesystem adapter should retain its trust classification.");
        AssertEqual("fs.read", string.Join(",", operations[0].GetProperty("effects").EnumerateArray()
            .Select(effect => effect.GetString())),
            "The async filesystem adapter should carry fs.read.");
    }

    private static async Task TestInspectGraph(Harness harness)
    {
        const string rootSource = """
            module app::main;
            pub struct RootBox<T> { item: a::models::Item<T> }
            pub union RootEvent { Empty, Item(a::models::Item<Text>) }
            pub newtype RootId = a::models::Item<i32>;
            pub trait Render { fn render(value: Self) -> Text effects {}; }
            fn render_i32(value: i32) -> Text effects {} { return "number"; }
            pub impl self::app::main::Render for i32 { render = self::app::main::render_i32; }
            pub fn measure<T: self::app::main::Render>(value: T) -> Text effects {} {
                return self::app::main::Render.render(value);
            }
            pub fn forward_measure<T: self::app::main::Render>(value: T) -> Text effects {} {
                return self::app::main::measure(value);
            }
            pub fn concrete_measure(value: i32) -> Text effects {} {
                return self::app::main::Render.render(value);
            }
            pub fn concrete_call(value: i32) -> Text effects {} {
                return self::app::main::measure(value);
            }
            pub fn recursive(value: i32) -> i32 effects {} {
                if value == 0 { return 0; }
                return self::app::main::recursive(value - 1);
            }
            pub fn call_a(value: i32) -> i32 effects {} { return a::service::first(value); }
            pub fn call_b(value: i32) -> i32 effects {} { return b::helper::second(value); }
            command scan {
                help "Read a package graph fixture.";
                argument path: FilePath help "Path to read.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string handlersSource = """
            module handlers;
            pub fn run(args: self::app::main::ScanArgs, fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                return a::service::read(fs);
            }
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
        const string aModels = """
            module models;
            pub struct Item<T> { value: T, shared: shared::models::Shared<T> }
            pub union ItemStatus { Missing, Invalid(Text) }
            pub newtype ItemName = shared::models::Shared<Text>;
            trait Internal { fn inspect(value: Self) -> i32 effects {}; }
            """;
        const string aService = """
            module service;
            pub fn first(value: i32) -> i32 effects {} { return shared::graph::recursive(value); }
            pub fn read(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                return shared::service::read(fs);
            }
            """;
        const string bHelper = """
            module helper;
            pub fn second(value: i32) -> i32 effects {} { return shared::graph::recursive(value); }
            """;
        const string sharedGraph = """
            module graph;
            pub fn recursive(value: i32) -> i32 effects {} {
                if value == 0 { return 0; }
                return self::graph::recursive(value - 1);
            }
            """;
        const string sharedModels = """
            module models;
            pub struct Shared<T> { value: T }
            pub union SharedState<T> { Empty, Value(T) }
            pub newtype SharedText = Text;
            pub trait SharedValue { fn value(item: Self) -> Text effects {}; }
            """;
        const string sharedService = """
            module service;
            pub fn read(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                return fs.read_text("item.txt");
            }
            """;

        static string WithLineEnding(string text, string lineEnding) =>
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", lineEnding, StringComparison.Ordinal);

        Dictionary<string, PackageFixture> Graph(string lineEnding, bool reverseDependencies = false, bool bodyEdit = false)
        {
            var rootManifest = "name = \"inspect-graph-root\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "\n[capabilities]\nfs.read = \"allow\"\n\n[dependencies]\n"
                + (reverseDependencies ? "b = \"../b\"\na = \"../a\"\n" : "a = \"../a\"\nb = \"../b\"\n");
            var aManifest = LibraryPackageManifest("inspect-graph-a") + "\n[dependencies]\nshared = \"../shared\"\n";
            var bManifest = LibraryPackageManifest("inspect-graph-b") + "\n[dependencies]\nshared = \"../shared\"\n";
            var transformedRoot = bodyEdit
                ? rootSource.Replace("return \"number\";", "return \"changed body\";", StringComparison.Ordinal)
                : rootSource;
            return new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(WithLineEnding(rootManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = WithLineEnding(transformedRoot, lineEnding),
                    ["src/handlers.hob"] = WithLineEnding(handlersSource, lineEnding)
                }),
                ["a"] = new PackageFixture(WithLineEnding(aManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/models.hob"] = WithLineEnding(aModels, lineEnding),
                    ["src/service.hob"] = WithLineEnding(aService, lineEnding)
                }),
                ["b"] = new PackageFixture(WithLineEnding(bManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/helper.hob"] = WithLineEnding(bHelper, lineEnding)
                }),
                ["shared"] = new PackageFixture(WithLineEnding(LibraryPackageManifest("inspect-graph-shared"), lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/graph.hob"] = WithLineEnding(sharedGraph, lineEnding),
                    ["src/models.hob"] = WithLineEnding(sharedModels, lineEnding),
                    ["src/service.hob"] = WithLineEnding(sharedService, lineEnding)
                })
            };
        }

        static async Task LockAsync(Harness harness, string label, string packageRoot)
        {
            var result = await harness.InvokePackageDirectoryAsync(label, packageRoot, "lock");
            AssertEqual(0, result.ExitCode, Describe(result));
        }

        static async Task<ProcessResult> InspectAsync(Harness harness, string packageRoot) =>
            await harness.InvokeCompilerCommandAsync("inspect", packageRoot, "--json");

        static void AssertDiagnostic(ProcessResult result, string code, string context)
        {
            AssertEqual(1, result.ExitCode, context + " " + Describe(result));
            AssertEqual(string.Empty, result.StandardError, context + " " + Describe(result));
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            AssertEqual(1, root.GetProperty("schemaVersion").GetInt32(), context + " should use diagnostic schema v1.");
            AssertTrue(!root.TryGetProperty("schema_version", out _) && !root.TryGetProperty("symbols", out _),
                context + " must not emit a partial success report.");
            AssertTrue(root.GetProperty("diagnostics").EnumerateArray().Any(item => item.GetProperty("code").GetString() == code),
                context + $" should include {code}. {result.StandardOutput}");
        }

        var missingLockRoot = await harness.WritePackageGraphAsync("inspect-graph-missing-lock", Graph("\n"));
        AssertDiagnostic(await InspectAsync(harness, missingLockRoot), "E_LOCK", "Inspect graph without a dependency lock");
        await LockAsync(harness, "inspect-graph-create-lock", missingLockRoot);

        var first = await InspectAsync(harness, missingLockRoot);
        AssertEqual(0, first.ExitCode, Describe(first));
        AssertEqual(string.Empty, first.StandardError, Describe(first));
        using var firstDocument = JsonDocument.Parse(first.StandardOutput);
        var report = firstDocument.RootElement;
        AssertJsonPropertyOrder(report,
            "schema_version,root_package_id,packages,symbols,trait_implementations,trait_calls,generic_call_witnesses,trait_binding_edges,manifest_grants,trusted_claims,foreign_dependencies,managed_adapters");
        AssertEqual(1, report.GetProperty("schema_version").GetInt32(), "The complete graph report should use schema v1.");
        AssertEqual("root", report.GetProperty("root_package_id").GetString(), "The root package has one stable ID.");
        AssertEqual(4, report.GetProperty("packages").GetArrayLength(), "Every package in the diamond graph should appear once.");
        AssertJsonPropertyOrder(report.GetProperty("packages")[0], "id,name,version,dependencies");
        var packageIds = report.GetProperty("packages").EnumerateArray()
            .Select(package => package.GetProperty("id").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        AssertTrue(packageIds.SetEquals(["root", "root/dep:a", "root/dep:b", "root/dep:a/dep:shared"]),
            "The shared dependency should use the ordinal-smallest alias path in the diamond.");
        AssertTrue(report.GetProperty("packages").EnumerateArray().All(package =>
                package.GetProperty("dependencies").EnumerateArray().All(dependency =>
                    packageIds.Contains(dependency.GetProperty("package_id").GetString() ?? string.Empty))),
            "Every serialized package edge must resolve to a package row.");
        AssertTrue(report.GetProperty("packages").EnumerateArray()
                .Where(package => package.GetProperty("id").GetString() is "root/dep:a" or "root/dep:b")
                .All(package => package.GetProperty("dependencies")[0].GetProperty("package_id").GetString() == "root/dep:a/dep:shared"),
            "Both sides of the dependency diamond must preserve their edge to the one canonical shared node.");
        AssertJsonStringArray(report.GetProperty("manifest_grants"), ["fs.read"]);
        AssertTrue(!first.StandardOutput.Contains(harness.TemporaryRoot, StringComparison.Ordinal),
            "Successful graph JSON must not expose relocated checkout or integration-temp paths.");

        var symbols = report.GetProperty("symbols").EnumerateArray().ToArray();
        var symbolIds = symbols.Select(symbol => symbol.GetProperty("id").GetString() ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        AssertEqual(symbols.Length, symbolIds.Count, "Stable symbol IDs should be unique.");
        AssertTrue(symbols.All(symbol => symbol.GetProperty("id").ValueKind == JsonValueKind.String),
            "Stable IDs must not expose compiler-local numeric declaration IDs.");
        var functions = symbols.Where(symbol => symbol.GetProperty("kind").GetString() == "function").ToArray();
        var functionIds = functions.Select(function => function.GetProperty("id").GetString() ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        var traitIds = symbols.Where(symbol => symbol.GetProperty("kind").GetString() == "trait")
            .Select(symbol => symbol.GetProperty("id").GetString() ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        foreach (var function in functions)
        {
            AssertJsonPropertyOrder(function,
                "id,kind,package_id,module,name,visibility,is_async,signature,declared_effects,inferred_effects,effect_paths,required_capabilities,calls,callers");
            AssertJsonPropertyOrder(function.GetProperty("signature"), "type_parameters,parameters,return_type");
            var functionId = function.GetProperty("id").GetString()!;
            var expectedCallers = functions.Where(candidate => candidate.GetProperty("calls").EnumerateArray()
                    .Any(call => call.GetString() == functionId))
                .Select(candidate => candidate.GetProperty("id").GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray();
            AssertEqual(string.Join("\n", expectedCallers),
                string.Join("\n", function.GetProperty("callers").EnumerateArray().Select(item => item.GetString())),
                $"Reverse callers for {functionId} must be exactly the inverse of direct calls.");
            AssertTrue(function.GetProperty("calls").EnumerateArray().All(call => functionIds.Contains(call.GetString() ?? string.Empty)),
                $"Every direct call from {functionId} must resolve to a function symbol.");
            foreach (var caller in function.GetProperty("callers").EnumerateArray())
                AssertTrue(functionIds.Contains(caller.GetString() ?? string.Empty), "Every caller reference must resolve.");
            AssertTrue(function.GetProperty("id").ValueKind == JsonValueKind.String
                && !function.TryGetProperty("local_id", out _), "Function symbols must not expose local numeric IDs.");

            foreach (var typeParameter in function.GetProperty("signature").GetProperty("type_parameters").EnumerateArray())
                foreach (var bound in typeParameter.GetProperty("bounds").EnumerateArray())
                    AssertTrue(traitIds.Contains(bound.GetString() ?? string.Empty), "Every function bound must resolve to a trait symbol.");
            AssertTypeReferences(function.GetProperty("signature"), symbolIds);
            foreach (var path in function.GetProperty("effect_paths").EnumerateArray())
                foreach (var step in path.GetProperty("steps").EnumerateArray())
                    if (step.GetProperty("kind").GetString() == "function")
                        AssertTrue(functionIds.Contains(step.GetProperty("symbol_id").GetString() ?? string.Empty),
                            "Every effect-path step must resolve to a function symbol.");
        }

        var recursiveId = "hob.function.v1.root::app::main::recursive";
        var recursive = functions.Single(function => function.GetProperty("id").GetString() == recursiveId);
        AssertTrue(recursive.GetProperty("calls").EnumerateArray().Any(call => call.GetString() == recursiveId)
            && recursive.GetProperty("callers").EnumerateArray().Any(caller => caller.GetString() == recursiveId),
            "Recursive call edges must include the same stable ID in both directions.");

        var graphCalls = report.GetProperty("trait_calls").EnumerateArray().ToArray();
        var measureId = "hob.trait.v1.root::app::main::Render";
        AssertTrue(graphCalls.Any(call => call.GetProperty("trait_id").GetString() == measureId
                && call.GetProperty("witness").GetProperty("kind").GetString() == "bound"),
            "Generic trait dispatch should retain its forwarded bound witness.");
        var implementationCalls = graphCalls.Where(call => call.GetProperty("witness").GetProperty("kind").GetString() == "implementation").ToArray();
        AssertTrue(implementationCalls.Length == 1
                && implementationCalls[0].GetProperty("witness").GetProperty("implementation_id").GetString() ==
                    report.GetProperty("trait_implementations")[0].GetProperty("id").GetString(),
            "Concrete trait dispatch should report its stable implementation identity.");
        AssertTrue(report.GetProperty("trait_binding_edges").EnumerateArray().Any(edge =>
                edge.GetProperty("caller_function_id").GetString() == "hob.function.v1.root::app::main::concrete_measure"
                && edge.GetProperty("callee_function_id").GetString() == "hob.function.v1.root::app::main::render_i32"),
            "A concrete method dispatch should have an explicit binding edge.");
        var concreteCallId = "hob.function.v1.root::app::main::concrete_call";
        var genericWitness = report.GetProperty("generic_call_witnesses").EnumerateArray().Single(witness =>
            witness.GetProperty("caller_function_id").GetString() == concreteCallId);
        AssertEqual("implementation", genericWitness.GetProperty("witnesses")[0].GetProperty("selection").GetProperty("kind").GetString(),
            "Concrete generic-call witnesses should remain distinct from method binding edges.");
        var forwardedCallId = "hob.function.v1.root::app::main::forward_measure";
        var forwardedWitness = report.GetProperty("generic_call_witnesses").EnumerateArray().Single(witness =>
            witness.GetProperty("caller_function_id").GetString() == forwardedCallId);
        AssertEqual("bound", forwardedWitness.GetProperty("witnesses")[0].GetProperty("selection").GetProperty("kind").GetString(),
            "Forwarded generic witnesses must not invent a concrete implementation edge.");
        AssertTrue(functions.Single(function => function.GetProperty("id").GetString() == concreteCallId)
                .GetProperty("calls").EnumerateArray().Any(call => call.GetString() == "hob.function.v1.root::app::main::measure"),
            "A generic function invocation remains an ordinary direct call.");

        var implementations = report.GetProperty("trait_implementations").EnumerateArray().ToArray();
        var implementationIds = implementations.Select(implementation => implementation.GetProperty("id").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var implementation in implementations)
        {
            AssertTrue(traitIds.Contains(implementation.GetProperty("trait_id").GetString() ?? string.Empty),
                "Every implementation trait reference must resolve.");
            AssertTypeReferences(implementation.GetProperty("target_type"), symbolIds);
            foreach (var binding in implementation.GetProperty("bindings").EnumerateArray())
                AssertTrue(functionIds.Contains(binding.GetProperty("callee_function_id").GetString() ?? string.Empty),
                    "Every implementation binding must resolve to a function symbol.");
        }
        foreach (var call in graphCalls)
        {
            AssertTrue(functionIds.Contains(call.GetProperty("caller_function_id").GetString() ?? string.Empty),
                "Every trait caller must resolve.");
            AssertTrue(traitIds.Contains(call.GetProperty("trait_id").GetString() ?? string.Empty),
                "Every trait call must resolve its trait symbol.");
            if (call.GetProperty("witness").GetProperty("kind").GetString() == "implementation")
            {
                AssertTrue(implementationIds.Contains(call.GetProperty("witness").GetProperty("implementation_id").GetString() ?? string.Empty),
                    "Every concrete trait witness must resolve to an implementation.");
                AssertTypeReferences(call.GetProperty("witness").GetProperty("target_type"), symbolIds);
            }
        }
        foreach (var witness in report.GetProperty("generic_call_witnesses").EnumerateArray())
        {
            AssertTrue(functionIds.Contains(witness.GetProperty("caller_function_id").GetString() ?? string.Empty)
                && functionIds.Contains(witness.GetProperty("callee_function_id").GetString() ?? string.Empty),
                "Every generic call endpoint must resolve to a function symbol.");
            foreach (var entry in witness.GetProperty("witnesses").EnumerateArray())
            {
                AssertTrue(traitIds.Contains(entry.GetProperty("trait_id").GetString() ?? string.Empty),
                    "Every generic witness bound must resolve to a trait symbol.");
                var selection = entry.GetProperty("selection");
                if (selection.GetProperty("kind").GetString() == "implementation")
                {
                    AssertTrue(implementationIds.Contains(selection.GetProperty("implementation_id").GetString() ?? string.Empty),
                        "Every concrete generic witness must resolve to an implementation.");
                    AssertTypeReferences(selection.GetProperty("target_type"), symbolIds);
                }
            }
        }
        foreach (var edge in report.GetProperty("trait_binding_edges").EnumerateArray())
        {
            AssertTrue(functionIds.Contains(edge.GetProperty("caller_function_id").GetString() ?? string.Empty)
                && functionIds.Contains(edge.GetProperty("callee_function_id").GetString() ?? string.Empty)
                && traitIds.Contains(edge.GetProperty("trait_id").GetString() ?? string.Empty)
                && implementationIds.Contains(edge.GetProperty("implementation_id").GetString() ?? string.Empty),
                "Every trait-binding edge reference must resolve.");
        }

        var trustedClaims = report.GetProperty("trusted_claims").EnumerateArray().ToArray();
        AssertTrue(trustedClaims.Any(claim => claim.GetProperty("source").GetString() == "trusted_host"
                && claim.GetProperty("assurance").GetString() == "claim_only"
                && claim.GetProperty("operation").GetString() == "cli.output"),
            "Host operations should be reported as claim-only trusted claims.");
        AssertTrue(trustedClaims.Any(claim => claim.GetProperty("source").GetString() == "trusted_adapter"
                && claim.GetProperty("assurance").GetString() == "claim_only"
                && claim.GetProperty("operation").GetString() == "FsRead.read_text"
                && claim.GetProperty("effects").EnumerateArray().Any(effect => effect.GetString() == "fs.read")),
            "Filesystem adapter operations should remain claim-only and separate from source effects.");
        foreach (var claim in trustedClaims)
            foreach (var functionId in claim.GetProperty("reachable_from").EnumerateArray())
                AssertTrue(functionIds.Contains(functionId.GetString() ?? string.Empty), "Trusted claim roots must resolve to function symbols.");

        foreach (var symbol in symbols)
        {
            if (symbol.GetProperty("kind").GetString() == "struct")
                foreach (var field in symbol.GetProperty("fields").EnumerateArray())
                    AssertTypeReferences(field.GetProperty("type"), symbolIds);
            if (symbol.GetProperty("kind").GetString() == "union")
                foreach (var variant in symbol.GetProperty("variants").EnumerateArray())
                    foreach (var field in variant.GetProperty("fields").EnumerateArray())
                        AssertTypeReferences(field.GetProperty("type"), symbolIds);
            if (symbol.GetProperty("kind").GetString() == "newtype")
                AssertTypeReferences(symbol.GetProperty("representation"), symbolIds);
            if (symbol.GetProperty("kind").GetString() == "trait")
                foreach (var method in symbol.GetProperty("methods").EnumerateArray())
                {
                    AssertTypeReferences(method.GetProperty("return_type"), symbolIds);
                    foreach (var parameter in method.GetProperty("parameters").EnumerateArray())
                        AssertTypeReferences(parameter.GetProperty("type"), symbolIds);
                }
        }
        AssertStableIdValues(report);
        static void AssertTypeReferences(JsonElement element, IReadOnlySet<string> symbolIds)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("kind", out var kindProperty))
                return;
            var kind = kindProperty.GetString();
            switch (kind)
            {
                case "nominal":
                    AssertTrue(symbolIds.Contains(element.GetProperty("symbol_id").GetString() ?? string.Empty),
                        "Every nominal type reference must resolve to a declaration symbol.");
                    foreach (var argument in element.GetProperty("type_arguments").EnumerateArray())
                        AssertTypeReferences(argument, symbolIds);
                    break;
                case "list":
                case "option":
                case "secret":
                    AssertTypeReferences(element.GetProperty("item"), symbolIds);
                    break;
                case "map":
                    AssertTypeReferences(element.GetProperty("key"), symbolIds);
                    AssertTypeReferences(element.GetProperty("value"), symbolIds);
                    break;
                case "result":
                    AssertTypeReferences(element.GetProperty("ok"), symbolIds);
                    AssertTypeReferences(element.GetProperty("error"), symbolIds);
                    break;
            }
        }

        static void AssertStableIdValues(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name is "id" or "package_id" or "symbol_id" or "trait_id" or "implementation_id" or
                        "caller_function_id" or "callee_function_id" or "root_package_id")
                        AssertEqual(JsonValueKind.String, property.Value.ValueKind,
                            $"Stable reference field '{property.Name}' must not expose a compiler-local number.");
                    AssertStableIdValues(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) AssertStableIdValues(item);
            }
        }

        var repeated = await InspectAsync(harness, missingLockRoot);
        AssertEqual(first.StandardOutput, repeated.StandardOutput, "Repeated inspect graph output should be byte-identical.");
        var relocatedRoot = await harness.WritePackageGraphAsync("inspect-graph-relocated-crlf", Graph("\r\n", reverseDependencies: true));
        await LockAsync(harness, "inspect-graph-relocated-lock", relocatedRoot);
        var relocated = await InspectAsync(harness, relocatedRoot);
        AssertEqual(0, relocated.ExitCode, Describe(relocated));
        AssertEqual(first.StandardOutput, relocated.StandardOutput,
            "Equivalent relocated, CRLF, reverse-alias-order graphs should report byte-identical stable data.");

        var bodyEditedRoot = await harness.WritePackageGraphAsync("inspect-graph-body-edit", Graph("\n", bodyEdit: true));
        await LockAsync(harness, "inspect-graph-body-edit-lock", bodyEditedRoot);
        var bodyEdited = await InspectAsync(harness, bodyEditedRoot);
        AssertEqual(0, bodyEdited.ExitCode, Describe(bodyEdited));
        using var bodyEditedDocument = JsonDocument.Parse(bodyEdited.StandardOutput);
        var bodyEditedIds = bodyEditedDocument.RootElement.GetProperty("symbols").EnumerateArray()
            .Select(symbol => symbol.GetProperty("id").GetString() ?? string.Empty);
        AssertEqual(string.Join("\n", symbolIds.Order(StringComparer.Ordinal)),
            string.Join("\n", bodyEditedIds.Order(StringComparer.Ordinal)),
            "Body-only edits must preserve all stable graph symbol IDs.");

        var sharedSource = Path.GetFullPath(Path.Combine(missingLockRoot, "..", "shared", "src", "graph.hob"));
        await File.AppendAllTextAsync(sharedSource, "\n// stale after lock\n");
        AssertDiagnostic(await InspectAsync(harness, missingLockRoot), "E_LOCK", "Inspect graph with a stale dependency lock");

        var invalidManifestRoot = await harness.WritePackageAsync(
            "inspect-graph-invalid-manifest",
            "name = \"broken\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal));
        AssertDiagnostic(await InspectAsync(harness, invalidManifestRoot), "E_MANIFEST", "Inspect graph with a malformed manifest");
        var invalidSourceRoot = await harness.WritePackageAsync(
            "inspect-graph-invalid-source",
            LibraryPackageManifest("inspect-graph-invalid-source"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/invalid.hob"] = "module invalid; pub fn broken() -> i32 effects {} { return \"wrong\"; }"
            });
        AssertDiagnostic(await InspectAsync(harness, invalidSourceRoot), "E_TYPE_MISMATCH", "Inspect graph with invalid checked source");

        var api = await harness.InvokeCompilerCommandAsync("inspect", "api", relocatedRoot, "--json");
        AssertEqual(0, api.ExitCode, Describe(api));
        using var apiDocument = JsonDocument.Parse(api.StandardOutput);
        AssertEqual(13, apiDocument.RootElement.GetProperty("schema_version").GetInt32(),
            "The full inspect graph must not change inspect API schema v13.");
        var audit = await harness.InvokeCompilerCommandAsync("audit", relocatedRoot, "--json");
        AssertEqual(0, audit.ExitCode, Describe(audit));
        using var auditDocument = JsonDocument.Parse(audit.StandardOutput);
        AssertEqual(11, auditDocument.RootElement.GetProperty("schema_version").GetInt32(),
            "The full inspect graph must not change audit schema v11.");
    }

    private static async Task TestInspectApi(Harness harness)
    {
        var usage = await harness.InvokeCompilerCommandAsync();
        var wrongArguments = await harness.InvokeCompilerCommandAsync("inspect", "api");
        AssertEqual(2, wrongArguments.ExitCode, Describe(wrongArguments));
        AssertEqual(string.Empty, wrongArguments.StandardOutput, Describe(wrongArguments));
        AssertEqual(usage.StandardError, wrongArguments.StandardError, Describe(wrongArguments));
        AssertTrue(wrongArguments.StandardError.Contains("inspect api PACKAGE_DIRECTORY --json", StringComparison.Ordinal),
            "Malformed inspect-api arguments should print the API command form.");

        static string WithLineEnding(string text, string lineEnding) =>
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", lineEnding, StringComparison.Ordinal);

        Dictionary<string, PackageFixture> ApiGraph(string lineEnding)
        {
            const string rootSource = """
                module app::main;
                pub struct Envelope {
                    records: List<Option<direct::records::Record>>,
                    result: Result<direct::records::Record, direct::records::Status>,
                    snapshots: Map<Text, List<Option<direct::records::Record>>>
                }
                pub struct GenericEnvelope<T> { box: direct::records::Box<T> }
                pub union ApiReply { Found(direct::records::Record), Empty }
                struct HiddenRoot { note: Text }
                fn hidden_root() -> i32 effects {} { return 1; }

                pub async fn generic_root<T>(items: List<Option<T>>) -> Result<Option<T>, direct::records::Status> effects {} {
                    return Err(direct::records::Status.Empty);
                }

                command scan {
                    help "Scan records.";
                    argument path: FilePath help "Path to scan.";
                    option limit: i32 = 3 help "Maximum records.";
                    flag recursive help "Scan recursively.";
                    handler: self::handlers::run;
                    error: self::handlers::describe;
                }
                """;
            const string handlersSource = """
                module handlers;
                pub async fn run(args: self::app::main::ScanArgs, fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                    let loaded: Result<direct::records::Record, FsError> = direct::service::read_record(fs);
                    return Ok("scan complete");
                }
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
            const string recordsSource = """
                module records;
                pub struct Box<T> { value: T }
                pub struct Record {
                    id: i32,
                    origin: Option<foundation::models::Origin>,
                    revisions: List<Result<i32, foundation::models::Issue>>
                }
                pub union Status { Empty, Failed(foundation::models::Issue), Record(self::records::Record) }
                struct HiddenDirect { secret: Text }
                fn hidden_direct() -> i32 effects {} { return 2; }
                pub fn wrap<T>(items: List<Option<T>>, outcome: Result<T, foundation::models::Issue>) -> Option<List<Map<Text, Result<T, self::records::Record>>>> effects {} {
                    return None;
                }
                """;
            const string serviceSource = """
                module service;
                pub fn read_record(fs: FsRead) -> Result<self::records::Record, FsError> effects { fs.read } {
                    let loaded: Result<Text, FsError> = foundation::service::read(fs);
                    return match loaded {
                        Ok(text) => Ok(self::records::Record {
                            id: text.length,
                            origin: Some(foundation::models::Origin { value: text.length }),
                            revisions: []
                        }),
                        Err(error) => Err(error)
                    };
                }
                """;
            const string foundationSource = """
                module models;
                pub struct Origin { value: i32 }
                pub union Issue { Missing, Failed(i32) }
                """;
            const string foundationService = """
                module service;
                pub fn read(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
                    return fs.read_text("record.txt");
                }
                """;

            var rootManifest = "name = \"api-root\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "http_origin = \"https://api.example.test\"\n"
                + "\n[capabilities]\nfs.read = \"allow\"\nnet.client = \"allow\"\n"
                + "\n[dependencies]\nzeta = \"../direct\"\ndirect = \"../direct\"\n";
            var directManifest = LibraryPackageManifest("api-direct")
                + "\n[dependencies]\nfoundation = \"../foundation\"\n";

            return new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(WithLineEnding(rootManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = WithLineEnding(rootSource, lineEnding),
                    ["src/handlers.hob"] = WithLineEnding(handlersSource, lineEnding)
                }),
                ["direct"] = new PackageFixture(WithLineEnding(directManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/records.hob"] = WithLineEnding(recordsSource, lineEnding),
                    ["src/service.hob"] = WithLineEnding(serviceSource, lineEnding)
                }),
                ["foundation"] = new PackageFixture(
                    WithLineEnding(LibraryPackageManifest("api-foundation"), lineEnding),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/models.hob"] = WithLineEnding(foundationSource, lineEnding),
                        ["src/service.hob"] = WithLineEnding(foundationService, lineEnding)
                    })
            };
        }

        var packageRoot = await harness.WritePackageGraphAsync("inspect-api-lf", ApiGraph("\n"));
        var missingLock = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertInspectApiDiagnostic(missingLock, "E_LOCK", "A missing dependency lock must be returned as JSON diagnostics.");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("inspect-api-lock", packageRoot, "lock"));

        var first = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, first.ExitCode, Describe(first));
        AssertEqual(string.Empty, first.StandardError, Describe(first));
        using var json = JsonDocument.Parse(first.StandardOutput);
        var api = json.RootElement;
        AssertInspectApiPropertyOrder(api);
        AssertEqual(13, api.GetProperty("schema_version").GetInt32(), "Inspect-api schema version must be 13.");
        AssertEqual("self", api.GetProperty("package").GetProperty("alias").GetString(),
            "The root package must have a source-facing self alias.");
        var dependencies = api.GetProperty("dependencies").EnumerateArray().ToArray();
        AssertEqual(2, dependencies.Length, "Both direct aliases must remain in the dependency list.");
        AssertEqual("direct", dependencies[0].GetProperty("alias").GetString(), "Dependency aliases must be sorted.");
        AssertEqual("zeta", dependencies[1].GetProperty("alias").GetString(), "Dependency aliases must be sorted.");
        AssertJsonStringArray(api.GetProperty("manifest_grants"), ["fs.read", "net.client"]);
        AssertEqual("https://api.example.test", api.GetProperty("http_origin").GetString(),
            "The API report should expose the configured root HTTP origin.");

        var functions = api.GetProperty("functions").EnumerateArray().ToArray();
        var functionIds = functions.Select(function => function.GetProperty("id").GetString() ?? string.Empty).ToArray();
        var expectedFunctionIds = new[]
        {
            "direct::records::wrap",
            "direct::service::read_record",
            "self::app::main::generic_root",
            "self::handlers::describe",
            "self::handlers::run"
        };
        AssertTrue(expectedFunctionIds.SequenceEqual(functionIds, StringComparer.Ordinal),
            $"Only public root and direct dependency functions should be exposed, got [{string.Join(", ", functionIds)}].");
        AssertTrue(functions.Single(function => function.GetProperty("id").GetString() == "self::app::main::generic_root")
                .GetProperty("is_async").GetBoolean(),
            "The API should project async on generic functions.");
        AssertTrue(functionIds.All(id => !id.Contains("hidden", StringComparison.Ordinal)),
            "Private declarations must not appear in the API function list.");
        var structIds = api.GetProperty("structs").EnumerateArray()
            .Select(structure => structure.GetProperty("id").GetString() ?? string.Empty).ToArray();
        AssertTrue(new[] { "direct::records::Box", "direct::records::Record", "self::app::main::Envelope", "self::app::main::GenericEnvelope", "self::app::main::ScanArgs" }
                .SequenceEqual(structIds, StringComparer.Ordinal),
            $"Private root and dependency structs must be filtered, got [{string.Join(", ", structIds)}].");
        var genericBox = api.GetProperty("structs").EnumerateArray()
            .Single(structure => structure.GetProperty("id").GetString() == "direct::records::Box");
        AssertJsonPropertyOrder(genericBox, "id,source_ids,package,type_parameters,fields");
        AssertEqual("T", genericBox.GetProperty("type_parameters")[0].GetProperty("name").GetString(),
            "Generic struct declarations should expose their parameter name.");
        AssertEqual(0, genericBox.GetProperty("type_parameters")[0].GetProperty("ordinal").GetInt32(),
            "Generic struct parameter ordinals should be zero-based.");
        var genericEnvelope = api.GetProperty("structs").EnumerateArray()
            .Single(structure => structure.GetProperty("id").GetString() == "self::app::main::GenericEnvelope");
        var genericEnvelopeField = genericEnvelope.GetProperty("fields")[0].GetProperty("type");
        AssertJsonPropertyOrder(genericEnvelopeField,
            "kind,declaration_kind,source_id,source_ids,package,module,name,type_arguments");
        AssertEqual("direct::records::Box", genericEnvelopeField.GetProperty("source_id").GetString(),
            "A generic nominal field should retain its dependency source identity.");
        AssertJsonPropertyOrder(genericEnvelopeField.GetProperty("type_arguments")[0], "kind,name,ordinal");
        AssertEqual("type_parameter", genericEnvelopeField.GetProperty("type_arguments")[0].GetProperty("kind").GetString(),
            "Generic nominal field arguments should retain their recursive type shape.");
        AssertEqual("T", genericEnvelopeField.GetProperty("type_arguments")[0].GetProperty("name").GetString(),
            "Generic nominal field arguments should retain their parameter name.");
        var unionIds = api.GetProperty("unions").EnumerateArray()
            .Select(union => union.GetProperty("id").GetString() ?? string.Empty).ToArray();
        AssertTrue(new[] { "direct::records::Status", "self::app::main::ApiReply" }
                .SequenceEqual(unionIds, StringComparer.Ordinal),
            $"Private and transitive unions must be filtered, got [{string.Join(", ", unionIds)}].");
        var wrap = functions.Single(function => function.GetProperty("id").GetString() == "direct::records::wrap");
        AssertJsonStringArray(wrap.GetProperty("source_ids"), ["direct::records::wrap", "zeta::records::wrap"]);
        var wrapTypeParameter = wrap.GetProperty("type_parameters")[0];
        AssertJsonPropertyOrder(wrapTypeParameter, "name,ordinal,bounds");
        AssertEqual(0, wrapTypeParameter.GetProperty("bounds").GetArrayLength(),
            "Unbounded generic functions should expose an empty ordered bounds array.");
        AssertEqual(0, wrapTypeParameter.GetProperty("ordinal").GetInt32(),
            "Generic type parameter ordinals should be stable and source-facing.");
        var wrapResult = wrap.GetProperty("return_type");
        AssertEqual("option", wrapResult.GetProperty("kind").GetString(), "Generic return types should preserve wrappers.");
        AssertEqual("list", wrapResult.GetProperty("item").GetProperty("kind").GetString(),
            "Nested generic return types should preserve List.");
        var wrapMap = wrapResult.GetProperty("item").GetProperty("item");
        AssertJsonPropertyOrder(wrapMap, "kind,key,value");
        AssertEqual("map", wrapMap.GetProperty("kind").GetString(), "Generic return types should preserve Map.");
        AssertEqual("primitive", wrapMap.GetProperty("key").GetProperty("kind").GetString(),
            "Map keys should retain their recursive type shape.");
        AssertEqual("Text", wrapMap.GetProperty("key").GetProperty("name").GetString(),
            "Map keys should retain the Text key type.");
        AssertEqual("result", wrapMap.GetProperty("value").GetProperty("kind").GetString(),
            "Nested generic return types should preserve Result.");
        AssertEqual("type_parameter", wrapMap.GetProperty("value").GetProperty("ok").GetProperty("kind").GetString(),
            "Map values should preserve nested generic type parameters.");
        var envelope = api.GetProperty("structs").EnumerateArray()
            .Single(structure => structure.GetProperty("id").GetString() == "self::app::main::Envelope");
        var snapshotsType = envelope.GetProperty("fields")[2].GetProperty("type");
        AssertJsonPropertyOrder(snapshotsType, "kind,key,value");
        AssertEqual("map", snapshotsType.GetProperty("kind").GetString(), "Struct fields should preserve Map.");
        AssertEqual("primitive", snapshotsType.GetProperty("key").GetProperty("kind").GetString(),
            "Map field key should be a structured primitive type.");
        var mapFieldValue = snapshotsType.GetProperty("value");
        AssertEqual("list", mapFieldValue.GetProperty("kind").GetString(), "Nested Map values should preserve List.");
        var mapFieldOption = mapFieldValue.GetProperty("item");
        AssertEqual("option", mapFieldOption.GetProperty("kind").GetString(),
            "Nested Map/List values should preserve Option.");
        AssertEqual("nominal", mapFieldOption.GetProperty("item").GetProperty("kind").GetString(),
            "Nested Map values should preserve nominal dependency types.");
        var origin = api.GetProperty("structs").EnumerateArray()
            .Single(structure => structure.GetProperty("id").GetString() == "direct::records::Record")
            .GetProperty("fields")[1].GetProperty("type").GetProperty("item");
        AssertEqual("nominal", origin.GetProperty("kind").GetString(), "Transitive nominal types must remain structured.");
        AssertEqual(JsonValueKind.Null, origin.GetProperty("source_id").ValueKind,
            "Transitive-only nominal references must not invent a root source id.");
        AssertEqual(0, origin.GetProperty("source_ids").GetArrayLength(),
            "Transitive-only nominal references must have no usable root aliases.");
        AssertEqual("api-foundation", origin.GetProperty("package").GetProperty("name").GetString(),
            "Transitive nominal references should retain package identity.");
        var directRecord = api.GetProperty("structs").EnumerateArray()
            .Single(structure => structure.GetProperty("id").GetString() == "direct::records::Record");
        AssertJsonStringArray(directRecord.GetProperty("source_ids"),
            ["direct::records::Record", "zeta::records::Record"]);
        AssertTrue(!functions.Any(function => function.GetProperty("id").GetString() == "foundation::service::read"),
            "Transitive-only functions must not be exported as direct API declarations.");
        AssertTrue(!api.GetProperty("structs").EnumerateArray().Any(structure =>
                structure.GetProperty("id").GetString() == "foundation::models::Origin"),
            "Transitive-only types must remain references rather than exported package declarations.");

        var run = functions.Single(function => function.GetProperty("id").GetString() == "self::handlers::run");
        AssertTrue(run.GetProperty("is_async").GetBoolean(), "The API should project an async function marker.");
        AssertJsonStringArray(run.GetProperty("required_capabilities"), ["fs.read"]);
        var directCall = run.GetProperty("calls")[0];
        AssertEqual("direct::service::read_record", directCall.GetProperty("source_id").GetString(),
            "A direct call should use its first sorted source alias.");
        AssertJsonStringArray(directCall.GetProperty("source_ids"),
            ["direct::service::read_record", "zeta::service::read_record"]);
        var effectSteps = run.GetProperty("effect_paths")[0].GetProperty("steps").EnumerateArray().ToArray();
        AssertEqual(4, effectSteps.Length, "The inferred effect path should cross both direct and transitive functions.");
        AssertEqual("self::handlers::run", effectSteps[0].GetProperty("source_id").GetString(),
            "The effect path should identify its root function.");
        AssertEqual("direct::service::read_record", effectSteps[1].GetProperty("source_id").GetString(),
            "The effect path should identify its direct dependency function.");
        AssertEqual(JsonValueKind.Null, effectSteps[2].GetProperty("source_id").ValueKind,
            "Transitive effect steps must not invent a source id.");
        AssertEqual(0, effectSteps[2].GetProperty("source_ids").GetArrayLength(),
            "Transitive effect steps must have no usable aliases.");
        AssertEqual("api-foundation", effectSteps[2].GetProperty("package").GetProperty("name").GetString(),
            "Transitive effect steps should retain package identity.");
        AssertEqual("operation", effectSteps[3].GetProperty("kind").GetString(),
            "The effect path should end at the compiler operation.");
        var transitiveCall = functions.Single(function =>
                function.GetProperty("id").GetString() == "direct::service::read_record")
            .GetProperty("calls").EnumerateArray().Single();
        AssertEqual(JsonValueKind.Null, transitiveCall.GetProperty("source_id").ValueKind,
            "Transitive direct-call targets must not invent a root source id.");
        AssertEqual(0, transitiveCall.GetProperty("source_ids").GetArrayLength(),
            "Transitive direct-call targets must have no usable root aliases.");

        var command = api.GetProperty("commands").EnumerateArray().Single();
        AssertEqual("self::app::main::scan", command.GetProperty("id").GetString(), "Command IDs must use source names.");
        AssertEqual("self::handlers::run", command.GetProperty("handler").GetString(), "The command handler should retain its source ID.");
        AssertTrue(command.GetProperty("handler_is_async").GetBoolean(), "The API should project the command handler's async marker.");
        AssertJsonStringArray(command.GetProperty("handler_source_ids"), ["self::handlers::run"]);
        AssertEqual("self::handlers::describe", command.GetProperty("error_formatter").GetString(),
            "The command error formatter should retain its source ID.");
        AssertJsonStringArray(command.GetProperty("required_capabilities"), ["fs.read"]);
        var inputNames = command.GetProperty("inputs").EnumerateArray()
            .Select(input => input.GetProperty("name").GetString() ?? string.Empty).ToArray();
        AssertTrue(new[] { "path", "limit", "recursive" }.SequenceEqual(inputNames, StringComparer.Ordinal),
            "Typed command inputs should retain declaration order and source names.");
        AssertApiPortable(first.StandardOutput, api, harness.TemporaryRoot);

        var repeated = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(first.StandardOutput, repeated.StandardOutput,
            "Repeated inspect-api calls must produce byte-identical JSON.");

        var relocatedRoot = await harness.WritePackageGraphAsync("inspect-api-crlf", ApiGraph("\r\n"));
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("inspect-api-relocated-lock", relocatedRoot, "lock"));
        var relocated = await harness.InvokeCompilerCommandAsync("inspect", "api", relocatedRoot, "--json");
        AssertEqual(0, relocated.ExitCode, Describe(relocated));
        AssertEqual(first.StandardOutput, relocated.StandardOutput,
            "Equivalent relocated CRLF and LF dependency graphs must emit identical API JSON.");

        var invalidPackage = await harness.WritePackageAsync(
            "inspect-api-compiler-error",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return true; }"
            });
        var compilerError = await harness.InvokeCompilerCommandAsync("inspect", "api", invalidPackage, "--json");
        AssertInspectApiDiagnostic(compilerError, "E_TYPE_MISMATCH",
            "Compiler errors must return structured diagnostics without a partial API report.");
    }

    private static async Task TestInspectApiWebRoutes(Harness harness)
    {
        const string source = """
            module app::main;
            pub struct Request { id: i32, name: Text }
            pub struct Record { id: i32, name: Text }
            pub union Reply { Created(self::app::main::Record), Invalid(Text), Failed(Text), Empty }
            async fn create(request: self::app::main::Request, id: i32, q: Option<Text>, db: DbRead, writer: DbWrite) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Empty;
            }
            route POST "/records/{id}" {
                body: self::app::main::Request;
                path id: i32;
                query q: Option<Text>;
                handler: self::app::main::create;
                response Created: 201 json self::app::main::Record;
                response Invalid: 400 json Text;
                response Failed: 500 json Text;
                response Empty: 204;
            }
            """;
        const string manifest = "name = \"inspect-api-web\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
            + "sqlite_path = \"data/api.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
            + "\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n";
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = source,
            ["db/schema.sql"] = "CREATE TABLE records (id INTEGER PRIMARY KEY, name TEXT NOT NULL);\n"
        };
        var packageRoot = await harness.WritePackageAsync("inspect-api-web", manifest, files);
        var result = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, result.ExitCode, Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        using var document = JsonDocument.Parse(result.StandardOutput);
        var api = document.RootElement;
        AssertInspectApiPropertyOrder(api);
        AssertEqual(13, api.GetProperty("schema_version").GetInt32(), "Inspect-api schema version must be 13.");
        AssertJsonStringArray(api.GetProperty("manifest_grants"), ["db.read", "db.write", "net.listen"]);
        AssertEqual(JsonValueKind.Null, api.GetProperty("http_origin").ValueKind,
            "A package without HTTP client access should project a null HTTP origin.");
        AssertEqual(0, api.GetProperty("commands").GetArrayLength(), "Web packages should not invent CLI commands.");
        var route = api.GetProperty("routes").EnumerateArray().Single();
        AssertEqual("POST", route.GetProperty("method").GetString(), "Route methods should retain checked source values.");
        AssertEqual("/records/{id}", route.GetProperty("path").GetString(), "Route paths should retain checked source templates.");
        AssertEqual("nominal", route.GetProperty("body_type").GetProperty("kind").GetString(),
            "POST routes should expose their checked body type.");
        AssertEqual("self::app::main::Request", route.GetProperty("body_type").GetProperty("source_id").GetString(),
            "POST route bodies should use source-facing type IDs.");
        AssertEqual("self::app::main::create", route.GetProperty("handler").GetString(),
            "Route handler references should use source-facing IDs.");
        AssertTrue(route.GetProperty("handler_is_async").GetBoolean(), "The API should project the route handler's async marker.");
        AssertJsonStringArray(route.GetProperty("handler_source_ids"), ["self::app::main::create"]);
        var routeParameters = route.GetProperty("parameters").EnumerateArray().ToArray();
        AssertEqual(2, routeParameters.Length, "The API should project each checked path and query binding.");
        AssertJsonPropertyOrder(routeParameters[0], "name,in,required,type,handler_parameter_index");
        AssertEqual("id", routeParameters[0].GetProperty("name").GetString(), "Path binding wire name mismatch.");
        AssertEqual("path", routeParameters[0].GetProperty("in").GetString(), "Path binding location mismatch.");
        AssertTrue(routeParameters[0].GetProperty("required").GetBoolean(), "Path bindings are always required.");
        AssertEqual(1, routeParameters[0].GetProperty("handler_parameter_index").GetInt32(),
            "POST path binding indexes must account for the request body at handler position 0.");
        AssertEqual("primitive", routeParameters[0].GetProperty("type").GetProperty("kind").GetString(),
            "Path binding types should use the structured API primitive type shape.");
        AssertEqual("i32", routeParameters[0].GetProperty("type").GetProperty("name").GetString(),
            "Path binding type name mismatch.");
        AssertEqual("q", routeParameters[1].GetProperty("name").GetString(), "Query binding wire name mismatch.");
        AssertEqual("query", routeParameters[1].GetProperty("in").GetString(), "Query binding location mismatch.");
        AssertTrue(!routeParameters[1].GetProperty("required").GetBoolean(), "Option query bindings should be optional.");
        AssertEqual(2, routeParameters[1].GetProperty("handler_parameter_index").GetInt32(),
            "Query binding indexes must follow the POST body and preceding path bindings.");
        AssertEqual("option", routeParameters[1].GetProperty("type").GetProperty("kind").GetString(),
            "Option query binding types should preserve their option wrapper in inspect-api.");
        AssertEqual("Text", routeParameters[1].GetProperty("type").GetProperty("item").GetProperty("name").GetString(),
            "Option query binding item type mismatch.");
        var responseType = route.GetProperty("response_type");
        AssertEqual("nominal", responseType.GetProperty("kind").GetString(),
            "Route response unions should use the structured nominal type shape.");
        AssertEqual("union", responseType.GetProperty("declaration_kind").GetString(),
            "Route response type should retain its declaration kind.");
        AssertEqual("self::app::main::Reply", responseType.GetProperty("source_id").GetString(),
            "Route response unions should use source-facing IDs.");
        AssertJsonStringArray(responseType.GetProperty("source_ids"), ["self::app::main::Reply"]);
        AssertJsonStringArray(route.GetProperty("required_capabilities"), ["db.read", "db.write"]);
        var parameters = route.GetProperty("capability_parameters").EnumerateArray().ToArray();
        AssertEqual(2, parameters.Length, "Route capability parameters should preserve their checked order.");
        AssertEqual("db", parameters[0].GetProperty("name").GetString(), "DbRead capability parameter name mismatch.");
        AssertEqual("db.read", parameters[0].GetProperty("capability").GetString(), "DbRead capability mapping mismatch.");
        AssertEqual("writer", parameters[1].GetProperty("name").GetString(), "DbWrite capability parameter name mismatch.");
        AssertEqual("db.write", parameters[1].GetProperty("capability").GetString(), "DbWrite capability mapping mismatch.");
        var responses = route.GetProperty("responses").EnumerateArray().ToArray();
        AssertEqual(4, responses.Length, "All response mappings should be projected.");
        AssertEqual("Created,Invalid,Failed,Empty",
            string.Join(",", responses.Select(response => response.GetProperty("variant").GetString())),
            "Response mappings should preserve declaration order.");
        AssertEqual(201, responses[0].GetProperty("status").GetInt32(), "Created status mismatch.");
        AssertEqual("json", responses[0].GetProperty("content_type").GetString(), "Created content type mismatch.");
        AssertEqual("self::app::main::Record", responses[0].GetProperty("payload_type").GetProperty("source_id").GetString(),
            "Nominal route payloads should preserve source IDs.");
        AssertEqual(204, responses[3].GetProperty("status").GetInt32(), "Empty response status mismatch.");
        AssertEqual(JsonValueKind.Null, responses[3].GetProperty("payload_type").ValueKind,
            "A bodyless response should project a null payload type.");
        AssertApiPortable(result.StandardOutput, api, harness.TemporaryRoot);

        var relocatedRoot = await harness.WritePackageAsync("inspect-api-web-relocated", manifest, files);
        var relocated = await harness.InvokeCompilerCommandAsync("inspect", "api", relocatedRoot, "--json");
        AssertEqual(0, relocated.ExitCode, Describe(relocated));
        AssertEqual(result.StandardOutput, relocated.StandardOutput,
            "Equivalent inspect-api reports with dynamic route parameters must be independent of package location.");
    }

    private static async Task TestAuditPackage(Harness harness)
    {
        const string rootSource = """
            module app::main;
            struct Parameters { id: i32 }
            struct Row { id: i32 }
            pub union Reply { Ready }

            pub fn root_public() -> i32 effects {} { return direct::service::through(); }
            fn root_private() -> i32 effects {} { return self::app::main::root_public(); }

            fn load(db: DbRead) -> Result<Option<self::app::main::Row>, DbError> effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one(
                    "SELECT id FROM sample WHERE id = $id",
                    self::app::main::Parameters { id: 1 }
                );
                return loaded;
            }

            async fn handler(db: DbRead) -> self::app::main::Reply effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = self::app::main::load(db);
                return self::app::main::Reply.Ready;
            }

            route GET "/" { handler: self::app::main::handler; response Ready: 200; }
            """;
        const string directSource = """
            module service;
            pub fn through() -> i32 effects {} { return foundation::service::value(); }
            fn direct_private() -> i32 effects {} { return 2; }
            """;
        const string foundationSource = """
            module service;
            pub fn value() -> i32 effects {} { return 42; }
            fn foundation_private() -> i32 effects {} { return 1; }
            """;
        const string schema = "CREATE TABLE sample (id INTEGER PRIMARY KEY);\n";

        Dictionary<string, PackageFixture> MakeGraph(string lineEnding, string schemaText)
        {
            static string WithLineEnding(string text, string ending) =>
                text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", ending, StringComparison.Ordinal);

            var rootManifest = "name = \"audit-root\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "sqlite_path = \"data/audit.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
                + "http_origin = \"https://api.example.test\"\n"
                + "\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\nnet.client = \"allow\"\n"
                + "\n[dependencies]\nzeta = \"../direct\"\ndirect = \"../direct\"\n";
            var directManifest = LibraryPackageManifest("audit-direct") + "\n[dependencies]\nfoundation = \"../foundation\"\n";
            return new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(WithLineEnding(rootManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = WithLineEnding(rootSource, lineEnding),
                    ["db/schema.sql"] = WithLineEnding(schemaText, lineEnding)
                }),
                ["direct"] = new PackageFixture(WithLineEnding(directManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/service.hob"] = WithLineEnding(directSource, lineEnding)
                }),
                ["foundation"] = new PackageFixture(WithLineEnding(LibraryPackageManifest("audit-foundation"), lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/service.hob"] = WithLineEnding(foundationSource, lineEnding)
                })
            };
        }

        var packageRoot = await harness.WritePackageGraphAsync("audit-package-lf", MakeGraph("\n", schema));
        var badArguments = await harness.InvokeCompilerCommandAsync("audit", packageRoot);
        AssertEqual(2, badArguments.ExitCode, Describe(badArguments));
        AssertEqual(string.Empty, badArguments.StandardOutput, Describe(badArguments));
        AssertTrue(badArguments.StandardError.Contains("audit PACKAGE_DIRECTORY --json", StringComparison.Ordinal),
            $"Malformed audit arguments should print the audit command form. {Describe(badArguments)}");

        var missingLock = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertAuditDiagnostic(missingLock, "E_LOCK", "A missing dependency lock should be returned as JSON diagnostics.");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("audit-package-lock", packageRoot, "lock"));

        var first = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, first.ExitCode, Describe(first));
        AssertEqual(string.Empty, first.StandardError, Describe(first));
        using var document = JsonDocument.Parse(first.StandardOutput);
        var report = document.RootElement;
        AssertAuditPropertyOrder(report);
        AssertEqual(11, report.GetProperty("schema_version").GetInt32(), "Audit schema version must be 11.");
        AssertEqual(0, report.GetProperty("managed_adapters").GetArrayLength(),
            "Ordinary packages must report an empty managed adapter provenance array.");
        var packages = report.GetProperty("packages").EnumerateArray().ToArray();
        AssertEqual(3, packages.Length, "The root, direct, and transitive packages must each appear once.");
        AssertPackage(packages.Single(package => package.GetProperty("role").GetString() == "root"), "audit-root", ".");
        AssertPackage(packages.Single(package => package.GetProperty("role").GetString() == "direct"), "audit-direct", "../direct");
        AssertPackage(packages.Single(package => package.GetProperty("role").GetString() == "transitive"), "audit-foundation", "../foundation");

        var rootPackage = packages.Single(package => package.GetProperty("role").GetString() == "root");
        var aliases = rootPackage.GetProperty("dependencies").EnumerateArray().ToArray();
        AssertEqual("direct,zeta", string.Join(",", aliases.Select(item => item.GetProperty("alias").GetString())),
            "Aliases for a shared direct dependency must be retained and sorted deterministically.");
        AssertTrue(aliases.All(item => item.GetProperty("package").GetProperty("path").GetString() == "../direct"),
            "Duplicate aliases should resolve to the same package identity.");
        var schemaInput = rootPackage.GetProperty("inputs").EnumerateArray()
            .Single(input => input.GetProperty("kind").GetString() == "sqlite_schema");
        AssertEqual("db/schema.sql", schemaInput.GetProperty("path").GetString(),
            "Configured SQLite schema should be included as a checked package input.");

        AssertJsonStringArray(report.GetProperty("manifest_grants"), ["db.read", "db.write", "net.client", "net.listen"]);
        AssertEqual("https://api.example.test", report.GetProperty("http_origin").GetString(),
            "The audit report should expose the configured root HTTP origin.");
        var functions = report.GetProperty("compiler").GetProperty("functions").EnumerateArray().ToArray();
        var functionFacts = functions.Select(function =>
                $"{function.GetProperty("package").GetProperty("name").GetString()}::{function.GetProperty("name").GetString()}:{function.GetProperty("visibility").GetString()}")
            .ToArray();
        var expectedFunctionFacts = new[]
        {
            "audit-root::handler:private", "audit-root::load:private", "audit-root::root_private:private", "audit-root::root_public:public",
            "audit-direct::direct_private:private", "audit-direct::through:public",
            "audit-foundation::foundation_private:private", "audit-foundation::value:public"
        };
        AssertTrue(expectedFunctionFacts.SequenceEqual(functionFacts, StringComparer.Ordinal),
            $"Compiler facts should include public and private functions from all packages in deterministic order. Got [{string.Join(", ", functionFacts)}].");
        var rootPublic = functions.Single(function => function.GetProperty("name").GetString() == "root_public");
        AssertEqual(false, rootPublic.GetProperty("is_async").GetBoolean(),
            "The audit should retain false for synchronous functions.");
        AssertEqual("through", rootPublic.GetProperty("direct_calls")[0].GetProperty("name").GetString(),
            "Compiler facts should retain direct cross-package calls.");
        var rootHandler = functions.Single(function => function.GetProperty("name").GetString() == "handler");
        AssertTrue(rootHandler.GetProperty("is_async").GetBoolean(),
            "The audit should report asynchronous route handlers.");
        AssertJsonStringArray(rootHandler.GetProperty("required_capabilities"), ["db.read"]);

        var claims = report.GetProperty("trusted_claims").EnumerateArray().ToArray();
        var queryClaim = claims.Single(claim => claim.GetProperty("operation").GetString() == "DbRead.query_one");
        AssertEqual("trusted_adapter", queryClaim.GetProperty("source").GetString(), "Adapter claims should remain distinct from compiler facts.");
        AssertEqual("claim_only", queryClaim.GetProperty("assurance").GetString(), "Adapter claims should preserve their assurance label.");
        AssertJsonStringArray(queryClaim.GetProperty("effects"), ["db.read"]);
        AssertEqual("handler", queryClaim.GetProperty("reachable_from")[0].GetProperty("name").GetString(),
            "The route handler should be recorded as reaching its database adapter.");
        var hostClaim = claims.Single(claim => claim.GetProperty("operation").GetString() == "web.schema_init");
        AssertEqual("trusted_host", hostClaim.GetProperty("source").GetString(), "Host claims should remain distinct from adapter claims.");
        AssertJsonStringArray(hostClaim.GetProperty("effects"), ["db.write"]);
        AssertEqual(0, hostClaim.GetProperty("reachable_from").GetArrayLength(),
            "Host claims without a checked caller should have an empty reachable_from list.");

        var foreign = report.GetProperty("foreign_dependencies").EnumerateArray().Single();
        AssertEqual("Microsoft.Data.Sqlite", foreign.GetProperty("name").GetString(),
            "Configured SQLite should appear as a generated foreign build dependency.");
        AssertEqual("generated_build", foreign.GetProperty("reason").GetString(),
            "SQLite dependency provenance should be explicit.");
        AssertAuditPortable(first.StandardOutput, packageRoot, harness.TemporaryRoot);

        var repeated = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(first.StandardOutput, repeated.StandardOutput, "Repeated audit calls must produce byte-identical JSON.");

        var relocatedRoot = await harness.WritePackageGraphAsync("audit-package-crlf", MakeGraph("\r\n", schema));
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("audit-package-relocated-lock", relocatedRoot, "lock"));
        var relocated = await harness.InvokeCompilerCommandAsync("audit", relocatedRoot, "--json");
        AssertEqual(0, relocated.ExitCode, Describe(relocated));
        AssertEqual(first.StandardOutput, relocated.StandardOutput,
            "Equivalent relocated CRLF and LF package graphs, including SQLite schema, must emit identical audit JSON.");

        var runtimeDatabase = Path.Combine(packageRoot, "data", "audit.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimeDatabase)!);
        await File.WriteAllBytesAsync(runtimeDatabase, [0, 1, 2, 3, 4]);
        var withRuntimeDatabase = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(first.StandardOutput, withRuntimeDatabase.StandardOutput,
            "Runtime SQLite database bytes must not change checked package inputs or the audit report.");

        var schemaPath = Path.Combine(packageRoot, "db", "schema.sql");
        await File.WriteAllTextAsync(schemaPath, "CREATE TABLE sample (id INTEGER PRIMARY KEY, name TEXT NOT NULL);\n");
        var changedSchema = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, changedSchema.ExitCode, Describe(changedSchema));
        using (var changedDocument = JsonDocument.Parse(changedSchema.StandardOutput))
        {
            var changedRoot = changedDocument.RootElement.GetProperty("packages").EnumerateArray()
                .Single(package => package.GetProperty("role").GetString() == "root");
            var changedInput = changedRoot.GetProperty("inputs").EnumerateArray()
                .Single(input => input.GetProperty("kind").GetString() == "sqlite_schema");
            AssertTrue(schemaInput.GetProperty("sha256").GetString() != changedInput.GetProperty("sha256").GetString(),
                "A semantic SQLite schema edit must change its checked input hash.");
            AssertTrue(rootPackage.GetProperty("content_sha256").GetString() != changedRoot.GetProperty("content_sha256").GetString(),
                "A semantic SQLite schema edit must change the package content hash.");
        }

        var dependencySourcePath = Path.Combine(packageRoot, "..", "direct", "src", "service.hob");
        var dependencySource = await File.ReadAllTextAsync(dependencySourcePath);
        await File.WriteAllTextAsync(dependencySourcePath, dependencySource.Replace("return 2;", "return 3;", StringComparison.Ordinal));
        var staleLock = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertAuditDiagnostic(staleLock, "E_LOCK", "A stale dependency lock should fail audit without a partial report.");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("audit-package-refresh-lock", packageRoot, "lock"));

        var rootSourcePath = Path.Combine(packageRoot, "src", "app", "main.hob");
        var invalidSource = (await File.ReadAllTextAsync(rootSourcePath))
            .Replace("return direct::service::through();", "return true;", StringComparison.Ordinal);
        AssertTrue(invalidSource != await File.ReadAllTextAsync(rootSourcePath), "The test must introduce a compiler error.");
        await File.WriteAllTextAsync(rootSourcePath, invalidSource);
        var compilerError = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertAuditDiagnostic(compilerError, "E_TYPE_MISMATCH", "Compiler errors should return JSON diagnostics without an audit report.");

        static void AssertPackage(JsonElement package, string name, string path)
        {
            AssertEqual(name, package.GetProperty("identity").GetProperty("name").GetString(), "Unexpected audit package identity.");
            AssertEqual(path, package.GetProperty("identity").GetProperty("path").GetString(), "Package paths should remain relative to the audit root.");
            AssertTrue(package.GetProperty("content_sha256").GetString() is { Length: 64 }, "Package content hashes should be lowercase SHA-256 values.");
        }
    }

    private static void AssertAuditDiagnostic(ProcessResult result, string code, string message)
    {
        AssertEqual(1, result.ExitCode, message + " " + Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        using var diagnostics = JsonDocument.Parse(result.StandardOutput);
        AssertEqual(1, diagnostics.RootElement.GetProperty("schemaVersion").GetInt32(), "Audit failures should use diagnostics JSON schema version 1.");
        AssertTrue(ParseDiagnosticSnapshots(result.StandardOutput).Any(diagnostic => diagnostic.Code == code),
            $"Expected {code} in audit diagnostics. {result.StandardOutput}");
        AssertTrue(!diagnostics.RootElement.TryGetProperty("schema_version", out _)
            && !diagnostics.RootElement.TryGetProperty("compiler", out _)
            && !diagnostics.RootElement.TryGetProperty("packages", out _),
            "Audit failures must not emit a partial provenance report.");
    }

    private static void AssertAuditPropertyOrder(JsonElement root)
    {
        static void Order(JsonElement element, string expected) =>
            AssertEqual(expected, string.Join(",", element.EnumerateObject().Select(property => property.Name)),
                "Audit JSON property order is part of the deterministic report contract.");

        Order(root, "schema_version,packages,compiler,manifest_grants,config,http_origin,max_request_body_bytes,request_timeout_ms,process_executables,trusted_claims,foreign_dependencies,managed_adapters");
        foreach (var pin in root.GetProperty("process_executables").EnumerateArray())
            Order(pin, "os,path,sha256");
        foreach (var field in root.GetProperty("config").EnumerateArray())
            Order(field, "name,source_type,required,has_default");
        foreach (var package in root.GetProperty("packages").EnumerateArray())
        {
            Order(package, "identity,role,content_sha256,dependencies,inputs");
            AssertPackageIdentitySource(package.GetProperty("identity"));
            foreach (var dependency in package.GetProperty("dependencies").EnumerateArray())
            {
                Order(dependency, "alias,package");
                AssertPackageIdentitySource(dependency.GetProperty("package"));
            }
            foreach (var input in package.GetProperty("inputs").EnumerateArray())
                Order(input, "kind,path,sha256");
        }
        Order(root.GetProperty("compiler"), "functions,newtypes,traits,trait_impls");
        foreach (var newtype in root.GetProperty("compiler").GetProperty("newtypes").EnumerateArray())
        {
            Order(newtype, "id,package,module,name,visibility,representation");
            AssertPackageIdentitySource(newtype.GetProperty("package"));
            AssertAuditTypePropertyOrder(newtype.GetProperty("representation"));
        }
        foreach (var function in root.GetProperty("compiler").GetProperty("functions").EnumerateArray())
        {
            Order(function, "package,module,name,visibility,is_async,type_parameters,declared_effects,inferred_effects,effect_paths,direct_calls,trait_calls,required_capabilities");
            AssertPackageIdentitySource(function.GetProperty("package"));
            foreach (var path in function.GetProperty("effect_paths").EnumerateArray())
            {
                Order(path, "effect,steps");
                foreach (var step in path.GetProperty("steps").EnumerateArray())
                    Order(step, step.TryGetProperty("package", out _) ? "kind,package,module,name" : "kind,name");
            }
            foreach (var call in function.GetProperty("direct_calls").EnumerateArray())
                Order(call, "package,module,name");
            foreach (var typeParameter in function.GetProperty("type_parameters").EnumerateArray())
            {
                Order(typeParameter, "name,ordinal,bounds");
                foreach (var bound in typeParameter.GetProperty("bounds").EnumerateArray())
                    Order(bound, "trait");
            }
            foreach (var call in function.GetProperty("trait_calls").EnumerateArray())
            {
                var kind = call.GetProperty("kind").GetString();
                if (kind == "method")
                {
                    Order(call, "kind,trait,method,witness");
                    var witness = call.GetProperty("witness");
                    Order(witness, witness.GetProperty("kind").GetString() == "impl"
                        ? "kind,id,target,binding_function"
                        : "kind,type_parameter_ordinal,bound_ordinal");
                }
                else
                {
                    Order(call, "kind,function,witnesses");
                    foreach (var witness in call.GetProperty("witnesses").EnumerateArray())
                    {
                        Order(witness, "type_parameter_ordinal,bound_ordinal,trait,witness");
                        var selection = witness.GetProperty("witness");
                        Order(selection, selection.GetProperty("kind").GetString() == "impl"
                            ? "kind,id,target"
                            : "kind,type_parameter_ordinal,bound_ordinal");
                    }
                }
            }
        }
        foreach (var trait in root.GetProperty("compiler").GetProperty("traits").EnumerateArray())
        {
            Order(trait, "id,package,module,name,visibility,methods");
            foreach (var method in trait.GetProperty("methods").EnumerateArray())
            {
                Order(method, "name,parameters,return_type");
                foreach (var parameter in method.GetProperty("parameters").EnumerateArray())
                    Order(parameter, "name,type");
            }
        }
        foreach (var implementation in root.GetProperty("compiler").GetProperty("trait_impls").EnumerateArray())
        {
            Order(implementation, "id,package,module,trait,target,visibility,methods");
            AssertPackageIdentitySource(implementation.GetProperty("package"));
            AssertAuditTypePropertyOrder(implementation.GetProperty("target"));
            foreach (var method in implementation.GetProperty("methods").EnumerateArray())
                Order(method, "name,binding_function");
        }
        foreach (var claim in root.GetProperty("trusted_claims").EnumerateArray())
        {
            Order(claim, "operation,source,effects,assurance,reachable_from");
            foreach (var reachable in claim.GetProperty("reachable_from").EnumerateArray())
            {
                Order(reachable, "package,module,name");
                AssertPackageIdentitySource(reachable.GetProperty("package"));
            }
        }
        foreach (var dependency in root.GetProperty("foreign_dependencies").EnumerateArray())
            Order(dependency, "name,version,ecosystem,reason");
        AssertManagedAdapterProvenanceOrder(root.GetProperty("managed_adapters"));
    }

    private static void AssertAuditTypePropertyOrder(JsonElement type)
    {
        var kind = type.GetProperty("kind").GetString();
        switch (kind)
        {
            case "primitive":
                AssertJsonPropertyOrder(type, "kind,name");
                break;
            case "nominal":
                AssertJsonPropertyOrder(type, "kind,declaration_kind,package,module,name,type_arguments");
                if (type.GetProperty("package").ValueKind == JsonValueKind.Object)
                    AssertPackageIdentitySource(type.GetProperty("package"));
                foreach (var argument in type.GetProperty("type_arguments").EnumerateArray())
                    AssertAuditTypePropertyOrder(argument);
                break;
            case "list":
            case "option":
            case "secret":
                AssertJsonPropertyOrder(type, "kind,item");
                AssertAuditTypePropertyOrder(type.GetProperty("item"));
                break;
            case "map":
                AssertJsonPropertyOrder(type, "kind,key,value");
                AssertAuditTypePropertyOrder(type.GetProperty("key"));
                AssertAuditTypePropertyOrder(type.GetProperty("value"));
                break;
            case "result":
                AssertJsonPropertyOrder(type, "kind,ok,error");
                AssertAuditTypePropertyOrder(type.GetProperty("ok"));
                AssertAuditTypePropertyOrder(type.GetProperty("error"));
                break;
            case "type_parameter":
                AssertJsonPropertyOrder(type, "kind,name,ordinal");
                break;
            case "self":
                AssertJsonPropertyOrder(type, "kind");
                break;
            default:
                AssertTrue(false, $"Unexpected audit type kind <{kind}>.");
                break;
        }
    }

    private static void AssertPackageIdentitySource(JsonElement identity)
    {
        AssertJsonPropertyOrder(identity, "name,version,path,source");
        var stablePath = identity.GetProperty("path").GetString() ?? string.Empty;
        var source = identity.GetProperty("source");
        var kind = source.GetProperty("kind").GetString();
        switch (kind)
        {
            case "root":
                AssertJsonPropertyOrder(source, "kind");
                AssertEqual(".", stablePath, "A root package identity must use the portable '.' path.");
                break;
            case "path":
                AssertJsonPropertyOrder(source, "kind,path");
                var sourcePath = source.GetProperty("path").GetString() ?? string.Empty;
                AssertEqual(stablePath, sourcePath,
                    "A path package identity must retain the same portable path in its source metadata.");
                AssertTrue(!string.IsNullOrEmpty(sourcePath)
                           && !Path.IsPathFullyQualified(sourcePath)
                           && !sourcePath.Contains('\\'),
                    $"A path package source must be relative and portable, got <{sourcePath}>.");
                break;
            case "git":
                AssertJsonPropertyOrder(source, "kind,url,commit");
                AssertTrue(Regex.IsMatch(stablePath, "^git:[0-9a-f]{64}$", RegexOptions.CultureInvariant),
                    $"A Git package identity must use a stable digest path, got <{stablePath}>.");
                var url = source.GetProperty("url").GetString() ?? string.Empty;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri is null)
                {
                    AssertTrue(false, $"A Git package identity must contain an absolute URL, got <{url}>.");
                    return;
                }

                AssertTrue((uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile)
                           && uri.UserInfo.Length == 0
                           && uri.Query.Length == 0
                           && uri.Fragment.Length == 0
                           && string.Equals(uri.AbsoluteUri, url, StringComparison.Ordinal),
                    $"A Git package source URL must be canonical, credential-free, and portable, got <{url}>.");
                var commit = source.GetProperty("commit").GetString() ?? string.Empty;
                AssertTrue(Regex.IsMatch(commit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant),
                    $"A Git package identity must retain a full lowercase commit, got <{commit}>.");
                break;
            default:
                AssertTrue(false, $"Unknown package source identity kind <{kind}>.");
                break;
        }
    }

    private static void AssertAuditPortable(string json, string packageRoot, string temporaryRoot)
    {
        AssertTrue(!json.Contains(packageRoot, StringComparison.OrdinalIgnoreCase)
            && !json.Contains(temporaryRoot, StringComparison.OrdinalIgnoreCase),
            "Audit JSON must not leak absolute workspace paths.");
        using var document = JsonDocument.Parse(json);
        Visit(document.RootElement, "$");

        static void Visit(JsonElement element, string path)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    AssertTrue(property.Name is not "function_id" and not "package_id",
                        $"Audit JSON should use stable symbolic identities instead of numeric identifiers at {path}.{property.Name}.");
                    if (property.Name == "id")
                    {
                        var id = property.Value.GetString() ?? string.Empty;
                        AssertTrue(id.StartsWith("hob.trait.v1.", StringComparison.Ordinal)
                                   || id.StartsWith("hob.newtype.v1.", StringComparison.Ordinal)
                                   || id.StartsWith("hob.impl.v1.", StringComparison.Ordinal),
                            $"Audit declaration IDs should be portable trait or impl identities, got <{id}> at {path}.id.");
                    }
                    Visit(property.Value, $"{path}.{property.Name}");
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Visit(item, $"{path}[{index++}]");
            }
            else if (element.ValueKind == JsonValueKind.String && path.EndsWith(".path", StringComparison.Ordinal))
            {
                var value = element.GetString() ?? string.Empty;
                AssertTrue(!Path.IsPathFullyQualified(value) && !value.Contains('\\'),
                    $"Audit paths must be portable relative paths; got <{value}>.");
            }
        }
    }

    private static void AssertInspectApiDiagnostic(ProcessResult result, string code, string message)
    {
        AssertEqual(1, result.ExitCode, message + " " + Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        using var diagnostics = JsonDocument.Parse(result.StandardOutput);
        AssertEqual(1, diagnostics.RootElement.GetProperty("schemaVersion").GetInt32(),
            "Inspect-api failures should use the compiler diagnostics JSON schema.");
        AssertTrue(ParseDiagnosticSnapshots(result.StandardOutput).Any(diagnostic => diagnostic.Code == code),
            $"Expected {code} in inspect-api diagnostics. {result.StandardOutput}");
        AssertTrue(!diagnostics.RootElement.TryGetProperty("schema_version", out _),
            "An inspection failure must not emit a partial API schema.");
    }

    private static void AssertInspectApiPropertyOrder(JsonElement root)
    {
        var allowedOrders = new HashSet<string>(StringComparer.Ordinal)
        {
            "schema_version,package,dependencies,manifest_grants,config,http_origin,max_request_body_bytes,request_timeout_ms,process_executables,functions,structs,newtypes,unions,traits,trait_impls,commands,routes",
            "os,path,sha256",
            "name,source_type,required,has_default",
            "alias,name,version",
            "name,version",
            "id,source_ids,package,is_async,type_parameters,parameters,return_type,declared_effects,inferred_effects,effect_paths,calls,trait_calls,required_capabilities",
            "name,ordinal,bounds",
            "name,ordinal",
            "trait",
            "name,type",
            "kind,declaration_kind,source_id,source_ids,package,module,name,type_arguments",
            "kind,source_id,source_ids,package,module,name",
            "kind,name",
            "effect,steps",
            "source_id,source_ids,package,module,name",
            "id,source_ids,package,type_parameters,fields",
            "id,source_ids,package,representation",
            "id,source_ids,package,type_parameters,variants",
            "id,source_ids,package,methods",
            "name,parameters,return_type",
            "id,package,module,trait,target,visibility,methods",
            "kind,trait,method,witness",
            "kind,id,target",
            "kind,type_parameter_ordinal,bound_ordinal",
            "kind,function,witnesses",
            "type_parameter_ordinal,bound_ordinal,trait,witness",
            "kind",
            "name,payload",
            "id,package,help,inputs,handler,handler_is_async,handler_source_ids,error_formatter,error_formatter_source_ids,error_type,required_capabilities",
            "name,kind,type,help,default_value",
            "kind,value",
            "kind,name,ordinal",
            "kind,item",
            "kind,key,value",
            "kind,ok,error",
            "method,path,body_type,handler,handler_is_async,response_type,handler_source_ids,parameters,responses,required_capabilities,capability_parameters",
            "name,in,required,type,handler_parameter_index",
            "variant,status,content_type,payload_type",
            "name,capability"
        };

        static void Walk(JsonElement element, HashSet<string> allowedOrders, string? requiredOrder = null)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = element.EnumerateObject().Select(property => property.Name).ToArray();
                var order = string.Join(",", names);
                if (requiredOrder is not null)
                {
                    AssertEqual(requiredOrder, order,
                        "Function type parameters must expose their ordered trait bounds, including an empty array.");
                }
                AssertTrue(allowedOrders.Contains(order),
                    $"Unexpected inspect-api JSON object property order: [{order}].");
                var isFunction = names.Contains("is_async", StringComparer.Ordinal)
                    && names.Contains("type_parameters", StringComparer.Ordinal)
                    && names.Contains("parameters", StringComparer.Ordinal)
                    && names.Contains("return_type", StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    var childRequiredOrder = isFunction && property.Name == "type_parameters"
                        ? "name,ordinal,bounds"
                        : null;
                    Walk(property.Value, allowedOrders, childRequiredOrder);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray()) Walk(child, allowedOrders, requiredOrder);
            }
        }

        Walk(root, allowedOrders);
    }
    private static void AssertApiPortable(string json, JsonElement api, string temporaryRoot)
    {
        AssertTrue(!json.Contains(temporaryRoot, StringComparison.OrdinalIgnoreCase),
            "The source-facing API must not contain the temporary workspace path.");
        AssertTrue(!Regex.IsMatch(json, @"[A-Za-z]:\\"),
            "The source-facing API must not contain an absolute drive path.");

        static void Walk(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "id")
                        AssertEqual(JsonValueKind.String, property.Value.ValueKind,
                            "Declaration ids must be source strings, never compiler numeric ids.");
                    else if (property.Name.EndsWith("_id", StringComparison.Ordinal))
                        AssertTrue(property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null,
                            $"{property.Name} must be a source string or null, never a compiler numeric id.");
                    else if (property.Name.EndsWith("_ids", StringComparison.Ordinal))
                        AssertTrue(property.Value.ValueKind == JsonValueKind.Array
                            && property.Value.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String),
                            $"{property.Name} must contain only source string ids.");
                    Walk(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray()) Walk(child);
            }
        }

        Walk(api);
    }

    private static async Task TestQualifiedEffects(Harness harness)
    {
        const string library = "module io::files;\n"
            + "pub fn load(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n";
        const string wrapperWithoutEffect = "module app::reader;\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return self::io::files::load(fs); }\n";
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/reader.hob"] = wrapperWithoutEffect,
            ["src/io/files.hob"] = library
        };
        var packageRoot = await harness.WritePackageAsync("effect-qualified-exceeded", LibraryPackageManifest(), files);
        var wrapperPath = Path.GetFullPath(Path.Combine(packageRoot, "src", "app", "reader.hob"));
        var result = await harness.InvokePackageDirectoryAsync("effect-qualified-exceeded", packageRoot, "check", "--json");
        AssertTrue(result.ExitCode != 0, $"A qualified effectful call outside its upper bound unexpectedly passed. {Describe(result)}");
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        var diagnostics = ParseDiagnosticSnapshots(result.StandardOutput);
        var exceeded = diagnostics.Single(diagnostic => diagnostic.Code == "E_EFFECT_EXCEEDED");
        AssertEqual(wrapperPath, Path.GetFullPath(exceeded.File), "The effect diagnostic should identify the caller module.");
        AssertRangeAtToken(wrapperWithoutEffect, exceeded, "bad", 1);
        AssertEqual(
            "Effect 'fs.read' is not declared by function 'app::reader::bad'; shortest call path: app::reader::bad -> io::files::load -> fs.read_text",
            exceeded.Message,
            "The path should include the qualified function and the filesystem operation.");

        var repeat = await harness.InvokePackageDirectoryAsync("effect-qualified-exceeded-repeat", packageRoot, "check", "--json");
        var repeated = ParseDiagnosticSnapshots(repeat.StandardOutput).Single(diagnostic => diagnostic.Code == "E_EFFECT_EXCEEDED");
        AssertEqual(exceeded.Message, repeated.Message, "Imported call paths should be stable across repeated checks.");

        const string wrapperWithEffect = "module app::reader;\n"
            + "pub fn read(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return self::io::files::load(fs); }\n";
        var allowedPackage = await harness.WritePackageAsync(
            "effect-qualified-allowed",
            LibraryPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/reader.hob"] = wrapperWithEffect,
                ["src/io/files.hob"] = library
            });
        var allowed = await harness.InvokePackageDirectoryAsync("effect-qualified-allowed", allowedPackage, "check", "--json");
        AssertEqual(0, allowed.ExitCode, Describe(allowed));
        AssertEqual(0, ParseDiagnosticSnapshots(allowed.StandardOutput).Length,
            "A qualified effectful call inside the caller's upper bound should check cleanly.");
    }

    private static async Task TestFsErrorExhaustiveness(Harness harness)
    {
        const string exhaustiveSource = "module harness::fs_error_exhaustive;\n"
            + "pub fn display(error: FsError) -> Text effects {} {\n"
            + "    return match error {\n"
            + "        FsError.NotFound => \"not found\",\n"
            + "        FsError.PermissionDenied => \"permission denied\",\n"
            + "        FsError.InvalidPath => \"invalid path\",\n"
            + "        FsError.InvalidText => \"invalid text\",\n"
            + "        FsError.Io => \"I/O error\",\n"
            + "    };\n"
            + "}\n";
        var valid = await harness.InvokeAsync("fs-error-exhaustive", "check", exhaustiveSource, "--json");
        AssertEqual(0, valid.ExitCode, Describe(valid));
        AssertEqual(0, ParseDiagnosticSnapshots(valid.StandardOutput).Length,
            "FsError's five declared variants should form an exhaustive match.");

        const string incompleteSource = "module harness::fs_error_incomplete;\n"
            + "pub fn display(error: FsError) -> Text effects {} {\n"
            + "    return match error { FsError.NotFound => \"not found\" };\n"
            + "}\n";
        var incomplete = await ExpectDiagnosticsAsync(
            harness, "fs-error-incomplete", incompleteSource, "E_MATCH_NONEXHAUSTIVE");
        AssertTrue(incomplete.Single().Message.Contains("PermissionDenied", StringComparison.Ordinal)
            && incomplete.Single().Message.Contains("Io", StringComparison.Ordinal),
            "The non-exhaustive FsError diagnostic should name omitted variants.");
    }

    private static async Task TestEffectfulLibraryBuild(Harness harness)
    {
        const string source = "module harness::effectful_library;\n"
            + "pub fn read(fs: FsRead, path: Text) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(path); }\n";
        var check = await harness.InvokeAsync("effectful-library-check", "check", source, "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length,
            "The declared direct filesystem effect should pass library checking.");

        var build = await harness.InvokeAsync("effectful-library-build", "build", source);
        AssertBuiltDll(build, Path.GetDirectoryName(harness.LastSourcePath)!);

        var dllPath = build.StandardOutput["Built library: ".Length..].Trim();
        var probeDirectory = Path.Combine(harness.TemporaryRoot, $"fs-read-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeDirectory);
        var validPath = Path.Combine(probeDirectory, "valid.txt");
        var missingPath = Path.Combine(probeDirectory, "missing.txt");
        var invalidTextPath = Path.Combine(probeDirectory, "invalid-utf8.txt");
        const string validText = "runtime mapping λ";
        await File.WriteAllTextAsync(validPath, validText, new UTF8Encoding(false, true));
        await File.WriteAllBytesAsync(invalidTextPath, new byte[] { 0xC3, 0x28 });

        var loadContext = ProbeFsReadRuntimeMappings(dllPath, validPath, missingPath, invalidTextPath, validText);
        for (var attempt = 0; attempt < 10 && loadContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        AssertTrue(!loadContext.IsAlive, "The generated library's collectible load context should unload after the trusted probe.");
        Directory.Delete(probeDirectory, recursive: true);
    }

    private static async Task TestAsyncFsReadAdapter(Harness harness)
    {
        const string source = """
            module harness::async_fsread;

            pub async fn read(fs: FsRead, path: Text) -> Result<Text, FsError> effects { fs.read } {
                return await fs.read_text_async(path);
            }
            """;
        var check = await harness.InvokeAsync("async-fsread-check", "check", source, "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var build = await harness.InvokeAsync("async-fsread-build", "build", source);
        AssertBuiltDll(build, Path.GetDirectoryName(harness.LastSourcePath)!);
        var dllPath = ParseBuiltArtifact(build, "Built library: ");
        var probeDirectory = Path.Combine(harness.TemporaryRoot, $"async-fsread-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeDirectory);
        var validPath = Path.Combine(probeDirectory, "valid.txt");
        var missingPath = Path.Combine(probeDirectory, "missing.txt");
        var invalidTextPath = Path.Combine(probeDirectory, "invalid-utf8.txt");
        const string validText = "async adapter λ 😀";
        await File.WriteAllTextAsync(validPath, validText, new UTF8Encoding(false, true));
        await File.WriteAllBytesAsync(invalidTextPath, [0xC3, 0x28]);

        try
        {
            var loadContext = ProbeAsyncFsReadRuntimeMappings(
                dllPath, validPath, missingPath, invalidTextPath, validText);
            for (var attempt = 0; attempt < 10 && loadContext.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            AssertTrue(!loadContext.IsAlive,
                "The generated async FsRead library's collectible load context should unload after the trusted probe.");
        }
        finally
        {
            Directory.Delete(probeDirectory, recursive: true);
        }
    }

    private static async Task TestAsyncFsWriteSemantics(Harness harness)
    {
        const string main = """
            module app::main;

            command save {
                help "Save UTF-8 text to a path.";
                argument output: FilePath help "Destination path.";
                argument value: Text help "Text to write.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string handlers = """
            module handlers;

            pub async fn write_file(
                writer: FsWrite,
                path: FilePath,
                value: Text
            ) -> Result<bool, FsError> effects { fs.write } {
                return await writer.write_text_async(path, value);
            }

            pub async fn run(
                args: self::app::main::SaveArgs,
                writer: FsWrite,
            ) -> Result<Text, FsError> effects { fs.write } {
                return match await self::handlers::write_file(writer, args.output, args.value) {
                    Ok(written) => Ok("saved"),
                    Err(error) => Err(error)
                };
            }

            pub fn describe(error: FsError) -> Text effects {} {
                return match error {
                    FsError.NotFound => "not found",
                    FsError.PermissionDenied => "permission denied",
                    FsError.InvalidPath => "invalid path",
                    FsError.InvalidText => "invalid text",
                    FsError.Io => "I/O error",
                };
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "async-fswrite-report",
            CliPackageManifest() + "\n[capabilities]\nfs.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = main,
                ["src/handlers.hob"] = handlers
            });

        var check = await harness.InvokePackageDirectoryAsync("async-fswrite-report-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var apiResult = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, apiResult.ExitCode, Describe(apiResult));
        using (var apiDocument = JsonDocument.Parse(apiResult.StandardOutput))
        {
            var command = apiDocument.RootElement.GetProperty("commands").EnumerateArray().Single();
            AssertJsonStringArray(command.GetProperty("required_capabilities"), ["fs.write"]);
            var run = apiDocument.RootElement.GetProperty("functions").EnumerateArray()
                .Single(function => function.GetProperty("id").GetString() == "self::handlers::run");
            AssertJsonStringArray(run.GetProperty("required_capabilities"), ["fs.write"]);
        }

        var effectResult = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::handlers::run", "--json");
        AssertEqual(0, effectResult.ExitCode, Describe(effectResult));
        using (var effects = JsonDocument.Parse(effectResult.StandardOutput))
        {
            var report = effects.RootElement;
            AssertJsonStringArray(report.GetProperty("declared_effects"), ["fs.write"]);
            AssertJsonStringArray(report.GetProperty("inferred_effects"), ["fs.write"]);
            AssertJsonStringArray(report.GetProperty("required_capabilities"), ["fs.write"]);
            var effectPath = report.GetProperty("effect_paths").EnumerateArray()
                .Single(path => path.GetProperty("effect").GetString() == "fs.write");
            AssertEqual("handlers::run -> handlers::write_file -> fs.write_text_async",
                string.Join(" -> ", effectPath.GetProperty("steps").EnumerateArray()
                    .Select(step => step.GetString() ?? string.Empty)),
                "The async write report should preserve the transitive effect path.");
            var operation = report.GetProperty("trusted_operations").EnumerateArray()
                .Single(item => item.GetProperty("operation").GetString() == "FsWrite.write_text_async");
            AssertEqual("trusted_adapter", operation.GetProperty("trust").GetString(),
                "Async FsWrite should be identified as a trusted adapter operation.");
            AssertJsonStringArray(operation.GetProperty("effects"), ["fs.write"]);
        }

        var auditResult = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, auditResult.ExitCode, Describe(auditResult));
        using (var audit = JsonDocument.Parse(auditResult.StandardOutput))
        {
            var operation = audit.RootElement.GetProperty("trusted_claims").EnumerateArray()
                .Single(item => item.GetProperty("operation").GetString() == "FsWrite.write_text_async");
            AssertJsonStringArray(operation.GetProperty("effects"), ["fs.write"]);
        }

        const string unawaited = """
            module harness::async_fswrite_unawaited;
            pub async fn write(writer: FsWrite, path: Text, value: Text) -> Result<bool, FsError> effects { fs.write } {
                let pending: Result<bool, FsError> = writer.write_text_async(path, value);
                return pending;
            }
            """;
        var unawaitedDiagnostics = await ExpectDiagnosticsAsync(
            harness, "async-fswrite-unawaited", unawaited, "E_ASYNC_CALL_UNAWAITED");
        AssertTrue(unawaitedDiagnostics.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "An unawaited write should have its direct async-call diagnostic without a spurious effect-bound diagnostic.");

        const string outsideAsync = """
            module harness::async_fswrite_outside_async;
            pub fn write(writer: FsWrite, path: Text, value: Text) -> Result<bool, FsError> effects { fs.write } {
                return await writer.write_text_async(path, value);
            }
            """;
        await ExpectDiagnosticsAsync(harness, "async-fswrite-outside-async", outsideAsync, "E_AWAIT_CONTEXT");

        const string wrongValue = """
            module harness::async_fswrite_wrong_value;
            pub async fn write(writer: FsWrite, path: Text, value: i32) -> Result<bool, FsError> effects {} {
                return await writer.write_text_async(path, value);
            }
            """;
        var typeDiagnostics = await ExpectDiagnosticsAsync(
            harness, "async-fswrite-wrong-value", wrongValue, "E_TYPE_MISMATCH");
        AssertTrue(typeDiagnostics.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "A wrongly typed write value must not seed fs.write effect inference.");
    }

    private static async Task TestHttpClientContracts(Harness harness)
    {
        const string source = """
            module app::main;

            pub async fn fetch(client: HttpClient, target: Text) -> Result<HttpResponse, HttpError> effects { net.client } {
                return await client.get_text_async(target);
            }

            pub fn status(response: HttpResponse) -> i32 effects {} {
                return response.status;
            }

            pub fn body(response: HttpResponse) -> Text effects {} {
                return response.body;
            }

            pub fn describe(error: HttpError) -> Text effects {} {
                return match error {
                    HttpError.InvalidTarget => "invalid target",
                    HttpError.Transport => "transport",
                    HttpError.Timeout => "timeout",
                    HttpError.ResponseTooLarge => "too large",
                    HttpError.InvalidText => "invalid text"
                };
            }

            pub fn main() -> i32 effects {} { return 0; }
            """;
        var validPackage = await harness.WritePackageAsync(
            "http-client-contracts",
            CliPackageManifest()
                + "http_origin = \"https://api.example.test\"\n"
                + "\n[capabilities]\nnet.client = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source
            });
        var validCheck = await harness.InvokePackageDirectoryAsync("http-client-contracts-check", validPackage, "check", "--json");
        AssertEqual(0, validCheck.ExitCode, Describe(validCheck));
        AssertEqual(0, ParseDiagnosticSnapshots(validCheck.StandardOutput).Length,
            "HttpClient, HttpResponse fields, and exhaustive HttpError matching should type check.");

        const string unawaitedSource = """
            module harness::http_unawaited;
            pub async fn fetch(client: HttpClient, target: Text) -> Result<HttpResponse, HttpError> effects { net.client } {
                let pending: Result<HttpResponse, HttpError> = client.get_text_async(target);
                return pending;
            }
            """;
        var unawaited = await ExpectDiagnosticsAsync(
            harness, "http-client-unawaited", unawaitedSource, "E_ASYNC_CALL_UNAWAITED");
        AssertTrue(unawaited.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "A valid but unawaited HTTP operation should not add an effect-bound diagnostic.");

        const string wrongArgumentSource = """
            module harness::http_wrong_argument;
            pub async fn fetch(client: HttpClient) -> Result<HttpResponse, HttpError> effects {} {
                return await client.get_text_async(1);
            }
            """;
        var wrongArgument = await ExpectDiagnosticsAsync(
            harness, "http-client-wrong-argument", wrongArgumentSource, "E_TYPE_MISMATCH");
        AssertTrue(wrongArgument.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "An invalid HTTP target must not seed a net.client effect.");

        const string exceededSource = """
            module harness::http_effect_exceeded;
            pub async fn fetch(client: HttpClient, target: Text) -> Result<HttpResponse, HttpError> effects {} {
                return await client.get_text_async(target);
            }
            """;
        await ExpectDiagnosticsAsync(harness, "http-client-effect-exceeded", exceededSource, "E_EFFECT_EXCEEDED");

        const string missingReceiverSource = """
            module harness::http_missing_receiver;
            pub async fn fetch(target: Text) -> Result<HttpResponse, HttpError> effects {} {
                return await http.get_text_async(target);
            }
            """;
        await ExpectDiagnosticsAsync(harness, "http-client-missing-receiver", missingReceiverSource, "E_CAPABILITY_MISSING");

        var noFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        var clientGrantOnly = CliPackageManifest() + "\n[capabilities]\nnet.client = \"allow\"\n";
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "http-client-missing-origin",
            clientGrantOnly,
            noFiles,
            "E_CAPABILITY_MISSING",
            "hob.toml");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "http-client-orphan-origin",
            CliPackageManifest() + "http_origin = \"https://api.example.test\"\n",
            noFiles,
            "E_MANIFEST",
            "hob.toml");
        foreach (var (name, origin) in new[]
        {
            ("external-http", "http://api.example.test"),
            ("origin-path", "https://api.example.test/v1"),
            ("origin-query", "https://api.example.test/?x=1"),
            ("origin-fragment", "https://api.example.test/#part"),
            ("origin-user-info", "https://user:pass@api.example.test/")
        })
        {
            await ExpectPackageJsonDiagnosticAsync(
                harness,
                $"http-client-invalid-{name}",
                CliPackageManifest() + $"http_origin = \"{origin}\"\n\n[capabilities]\nnet.client = \"allow\"\n",
                noFiles,
                "E_MANIFEST",
                "hob.toml");
        }

        const string rootSource = "module app::main; pub fn main() -> i32 effects {} { return 1; }\n";
        const string originalOrigin = "https://first.example.test";
        const string replacementOrigin = "https://second.example.test";
        var originalManifest = CliPackageManifest()
            + $"http_origin = \"{originalOrigin}\"\n\n[capabilities]\nnet.client = \"allow\"\n"
            + "\n[dependencies]\nvalidation = \"../validation\"\n";
        var lockPackage = await harness.WritePackageGraphAsync(
            "http-client-origin-lock",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(originalManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = rootSource }),
                ["validation"] = new PackageFixture(LibraryPackageManifest("http-lock-validation"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/validation.hob"] = "module validation; pub fn marker() -> i32 effects {} { return 1; }"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "http-client-origin-lock-create", lockPackage, "lock"));
        var lockManifestPath = Path.Combine(lockPackage, "hob.toml");
        await File.WriteAllTextAsync(lockManifestPath, originalManifest.Replace(originalOrigin, replacementOrigin, StringComparison.Ordinal));
        var staleOrigin = await harness.InvokePackageDirectoryAsync(
            "http-client-origin-lock-stale", lockPackage, "check", "--json");
        AssertTrue(staleOrigin.ExitCode != 0
            && staleOrigin.StandardOutput.Contains("E_LOCK", StringComparison.Ordinal),
            $"Changing http_origin must stale the package lock. {Describe(staleOrigin)}");

        const string commandHandlerSource = """
            module app::main;
            command fetch {
                help "Fetch one HTTP target.";
                argument target: Text help "Relative request target.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string commandCapabilitySource = """
            module handlers;
            pub async fn run(args: self::app::main::FetchArgs, client: HttpClient) -> Result<Text, HttpError> effects { net.client } {
                return match await client.get_text_async(args.target) {
                    Ok(response) => Ok(response.body),
                    Err(error) => Err(error)
                };
            }
            pub fn describe(error: HttpError) -> Text effects {} {
                return match error {
                    HttpError.InvalidTarget => "invalid target",
                    HttpError.Transport => "transport",
                    HttpError.Timeout => "timeout",
                    HttpError.ResponseTooLarge => "too large",
                    HttpError.InvalidText => "invalid text"
                };
            }
            """;
        var missingGrantPackage = await harness.WritePackageAsync(
            "http-client-command-missing-grant",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = commandHandlerSource,
                ["src/handlers.hob"] = commandCapabilitySource
            });
        var missingGrant = await harness.InvokePackageDirectoryAsync(
            "http-client-command-missing-grant-check", missingGrantPackage, "check", "--json");
        AssertTrue(missingGrant.ExitCode != 0, Describe(missingGrant));
        AssertTrue(ParseDiagnosticSnapshots(missingGrant.StandardOutput)
                .Any(diagnostic => diagnostic.Code == "E_CAPABILITY_MISSING"),
            $"An injected CLI HttpClient without net.client should fail capability checking. {Describe(missingGrant)}");

        const string routeSource = """
            module app::main;
            pub union Reply { Ready, Failed }
            pub async fn fetch(client: HttpClient) -> self::app::main::Reply effects { net.client } {
                let response: Result<HttpResponse, HttpError> = await client.get_text_async("/");
                return self::app::main::Reply.Ready;
            }
            route GET "/" {
                handler: self::app::main::fetch;
                response Ready: 200;
                response Failed: 500;
            }
            """;
        var routeMissingGrantPackage = await harness.WritePackageAsync(
            "http-client-route-missing-grant",
            "name = \"http-route-missing-grant\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "\n[capabilities]\nnet.listen = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = routeSource
            });
        var routeMissingGrant = await harness.InvokePackageDirectoryAsync(
            "http-client-route-missing-grant-check", routeMissingGrantPackage, "check", "--json");
        AssertTrue(routeMissingGrant.ExitCode != 0, Describe(routeMissingGrant));
        AssertTrue(ParseDiagnosticSnapshots(routeMissingGrant.StandardOutput)
                .Any(diagnostic => diagnostic.Code == "E_CAPABILITY_MISSING"),
            $"An injected route HttpClient without net.client should fail capability checking. {Describe(routeMissingGrant)}");

        var webClientPackage = await harness.WritePackageAsync(
            "http-client-route-projection",
            "name = \"http-route-projection\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "http_origin = \"https://api.example.test\"\n"
                + "\n[capabilities]\nnet.listen = \"allow\"\nnet.client = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = routeSource
            });
        var webClientApi = await harness.InvokeCompilerCommandAsync("inspect", "api", webClientPackage, "--json");
        AssertEqual(0, webClientApi.ExitCode, Describe(webClientApi));
        using (var apiDocument = JsonDocument.Parse(webClientApi.StandardOutput))
        {
            var api = apiDocument.RootElement;
            AssertInspectApiPropertyOrder(api);
            AssertEqual(13, api.GetProperty("schema_version").GetInt32(),
                "The HTTP web route API should use schema version 13.");
            AssertEqual("https://api.example.test", api.GetProperty("http_origin").GetString(),
                "The web API should expose its configured HTTP origin.");
            AssertJsonStringArray(api.GetProperty("manifest_grants"), ["net.client", "net.listen"]);
            var route = api.GetProperty("routes").EnumerateArray().Single();
            AssertJsonStringArray(route.GetProperty("required_capabilities"), ["net.client"]);
            var parameter = route.GetProperty("capability_parameters").EnumerateArray().Single();
            AssertEqual("client", parameter.GetProperty("name").GetString(),
                "The web API should retain the injected HttpClient parameter name.");
            AssertEqual("net.client", parameter.GetProperty("capability").GetString(),
                "The web API should map the injected HttpClient to net.client.");
        }
    }

    private static async Task TestHttpClientRuntimeAdapter(Harness harness)
    {
        await using var server = new RawHttpServer();
        const string source = """
            module app::main;

            command get {
                help "Fetch one HTTP target.";
                argument target: Text help "Relative request target.";
                handler: self::app::main::run;
                error: self::app::main::describe;
            }

            pub async fn fetch(client: HttpClient, target: Text) -> Result<HttpResponse, HttpError> effects { net.client } {
                return await client.get_text_async(target);
            }

            pub async fn run(args: self::app::main::GetArgs, client: HttpClient) -> Result<Text, HttpError> effects { net.client } {
                return match await self::app::main::fetch(client, args.target) {
                    Ok(response) => Ok(response.body),
                    Err(error) => Err(error)
                };
            }

            pub fn describe(error: HttpError) -> Text effects {} {
                return match error {
                    HttpError.InvalidTarget => "invalid target",
                    HttpError.Transport => "transport",
                    HttpError.Timeout => "timeout",
                    HttpError.ResponseTooLarge => "too large",
                    HttpError.InvalidText => "invalid text"
                };
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "http-client-runtime",
            CliPackageManifest()
                + $"http_origin = \"{server.Origin}\"\n"
                + "\n[capabilities]\nnet.client = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source
            });
        var check = await harness.InvokePackageDirectoryAsync("http-client-runtime-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        var apiResult = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, apiResult.ExitCode, Describe(apiResult));
        using (var apiDocument = JsonDocument.Parse(apiResult.StandardOutput))
        {
            var api = apiDocument.RootElement;
            AssertInspectApiPropertyOrder(api);
            AssertEqual(13, api.GetProperty("schema_version").GetInt32(),
                "Inspect-api with an HTTP capability should use schema version 13.");
            AssertEqual(server.Origin, api.GetProperty("http_origin").GetString(),
                "Inspect-api should retain the root HTTP origin.");
            AssertJsonStringArray(api.GetProperty("manifest_grants"), ["net.client"]);
            var function = api.GetProperty("functions").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == "self::app::main::fetch");
            AssertJsonStringArray(function.GetProperty("required_capabilities"), ["net.client"]);
            var clientType = function.GetProperty("parameters")[0].GetProperty("type");
            AssertEqual("primitive", clientType.GetProperty("kind").GetString(),
                "The API should expose HttpClient as a built-in primitive type.");
            AssertEqual("HttpClient", clientType.GetProperty("name").GetString(),
                "The API should preserve the HttpClient source type name.");
            var resultType = function.GetProperty("return_type");
            AssertEqual("result", resultType.GetProperty("kind").GetString(),
                "The API should preserve the HTTP Result wrapper.");
            AssertEqual("HttpResponse", resultType.GetProperty("ok").GetProperty("name").GetString(),
                "The API should expose HttpResponse as the successful HTTP result type.");
            AssertEqual("HttpError", resultType.GetProperty("error").GetProperty("name").GetString(),
                "The API should expose HttpError as the failed HTTP result type.");
            var command = api.GetProperty("commands").EnumerateArray().Single();
            AssertEqual("self::app::main::get", command.GetProperty("id").GetString(),
                "The API should retain the typed HTTP command.");
            AssertJsonStringArray(command.GetProperty("required_capabilities"), ["net.client"]);
            AssertJsonStringArray(api.GetProperty("functions").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == "self::app::main::run")
                .GetProperty("required_capabilities"), ["net.client"]);
        }

        var effectsResult = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::app::main::fetch", "--json");
        AssertEqual(0, effectsResult.ExitCode, Describe(effectsResult));
        using (var effectsDocument = JsonDocument.Parse(effectsResult.StandardOutput))
        {
            var report = effectsDocument.RootElement;
            AssertEqual(1, report.GetProperty("schema_version").GetInt32(),
                "HTTP effect reports should preserve schema version 1.");
            AssertJsonStringArray(report.GetProperty("declared_effects"), ["net.client"]);
            AssertJsonStringArray(report.GetProperty("inferred_effects"), ["net.client"]);
            var operation = report.GetProperty("trusted_operations").EnumerateArray()
                .Single(item => item.GetProperty("operation").GetString() == "HttpClient.get_text_async");
            AssertEqual("trusted_adapter", operation.GetProperty("trust").GetString(),
                "The HTTP operation should be identified as a trusted adapter.");
            AssertJsonStringArray(operation.GetProperty("effects"), ["net.client"]);
        }

        var auditResult = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, auditResult.ExitCode, Describe(auditResult));
        using (var auditDocument = JsonDocument.Parse(auditResult.StandardOutput))
        {
            var audit = auditDocument.RootElement;
            AssertAuditPropertyOrder(audit);
            AssertEqual(11, audit.GetProperty("schema_version").GetInt32(),
                "Audit reports with an HTTP capability should use schema version 11.");
            AssertEqual(server.Origin, audit.GetProperty("http_origin").GetString(),
                "Audit should retain the root HTTP origin.");
            AssertJsonStringArray(audit.GetProperty("manifest_grants"), ["net.client"]);
            AssertTrue(audit.GetProperty("trusted_claims").EnumerateArray()
                    .Any(item => item.GetProperty("operation").GetString() == "HttpClient.get_text_async"
                        && JsonStringArrayText(item.GetProperty("effects")) == "net.client"),
                "Audit should report the reachable HttpClient trusted adapter claim.");
        }

        var build = await harness.InvokePackageDirectoryAsync("http-client-runtime-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        var artifact = ParseBuiltArtifact(build, "Built executable: ");

        var loadContext = ProbeHttpClientRuntimeMappings(artifact, server.Origin, server);
        for (var attempt = 0; attempt < 10 && loadContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        AssertTrue(!loadContext.IsAlive,
            "The generated HTTP package's collectible load context should unload after the trusted probe.");

        static string JsonStringArrayText(JsonElement element) =>
            string.Join(",", element.EnumerateArray().Select(value => value.GetString()));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ProbeHttpClientRuntimeMappings(
        string dllPath,
        string origin,
        RawHttpServer server)
    {
        var loadContext = new AssemblyLoadContext($"http-client-probe-{Guid.NewGuid():N}", isCollectible: true);
        var weakReference = new WeakReference(loadContext);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var moduleType = assembly.GetType("HobModule", throwOnError: true)!;
            var clientType = moduleType.GetNestedType("HttpClientCapability", BindingFlags.Public)
                ?? throw new InvalidOperationException("Generated package does not expose its nested HttpClient runtime type.");
            var clientConstructor = clientType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(string), typeof(CancellationToken)],
                modifiers: null)
                ?? throw new InvalidOperationException("Generated HttpClient has no trusted origin and cancellation-token constructor.");
            var fetch = moduleType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name.StartsWith("Function_", StringComparison.Ordinal)
                    && method.ReturnType.IsGenericType
                    && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                    && method.GetParameters() is { Length: 3 } parameters
                    && parameters[0].ParameterType == clientType
                    && parameters[1].ParameterType == typeof(string)
                    && parameters[2].ParameterType == typeof(CancellationToken));

            Task InvokeFetch(string target, CancellationToken cancellationToken)
            {
                var client = clientConstructor.Invoke([origin, cancellationToken]);
                return fetch.Invoke(null, [client, target, cancellationToken]) as Task
                    ?? throw new InvalidOperationException("Generated HttpClient.get_text_async did not return a Task.");
            }

            static object Complete(Task task)
            {
                task.GetAwaiter().GetResult();
                return task.GetType().GetProperty("Result")?.GetValue(task)
                    ?? throw new InvalidOperationException("Generated HttpClient.get_text_async returned no Result value.");
            }

            static object CompleteWithin(Task task, TimeSpan watchdog)
            {
                task.WaitAsync(watchdog).GetAwaiter().GetResult();
                return Complete(task);
            }

            static object AssertSuccess(object result, int status, string expectedBody)
            {
                AssertTrue(result.GetType().Name.StartsWith("Ok", StringComparison.Ordinal),
                    $"An HTTP response should map to Result.Ok, got {result.GetType().FullName}.");
                var response = result.GetType().GetProperty("Value")?.GetValue(result)
                    ?? throw new InvalidOperationException("Result.Ok did not expose its HttpResponse value.");
                AssertEqual(status, response.GetType().GetProperty("Field_0")?.GetValue(response) as int?,
                    "HttpResponse.status should preserve the received HTTP status.");
                AssertEqual(expectedBody, response.GetType().GetProperty("Field_1")?.GetValue(response) as string,
                    "HttpResponse.body should preserve strict UTF-8 response text.");
                return response;
            }

            static string AssertError(object result, string expectedVariant)
            {
                AssertTrue(result.GetType().Name.StartsWith("Err", StringComparison.Ordinal),
                    $"An HTTP adapter failure should map to Result.Err, got {result.GetType().FullName}.");
                var error = result.GetType().GetProperty("Error")?.GetValue(result)
                    ?? throw new InvalidOperationException("Result.Err did not expose its HttpError value.");
                var variant = error.GetType().Name;
                AssertTrue(variant.StartsWith(expectedVariant, StringComparison.Ordinal),
                    $"Expected HttpError.{expectedVariant}, got {error.GetType().FullName}.");
                return variant;
            }

            AssertSuccess(Complete(InvokeFetch("/ok?from=runtime", CancellationToken.None)),
                200, "loopback λ");
            AssertSuccess(Complete(InvokeFetch("/not-found", CancellationToken.None)),
                404, "not found");
            AssertSuccess(Complete(InvokeFetch("/server-error", CancellationToken.None)),
                503, "server error");

            var requestsBeforeInvalidTarget = server.Requests.Count;
            AssertError(Complete(InvokeFetch("//example.invalid/path", CancellationToken.None)), "InvalidTarget");
            AssertEqual(requestsBeforeInvalidTarget, server.Requests.Count,
                "An invalid target should be rejected before opening a loopback connection.");

            AssertSuccess(Complete(InvokeFetch("/redirect", CancellationToken.None)), 302, "redirect body");
            AssertTrue(!server.Requests.Any(request => request.Target == "/followed"),
                "The HTTP adapter must return redirects without following them.");

            AssertSuccess(Complete(InvokeFetch("/cookie", CancellationToken.None)), 200, "cookie set");
            var cookieRequest = server.Requests.Single(request => request.Target == "/cookie");
            AssertSuccess(Complete(InvokeFetch("/after-cookie", CancellationToken.None)), 200, "after cookie");
            var afterCookie = server.Requests.Single(request => request.Target == "/after-cookie");
            AssertEqual(cookieRequest.RemotePort, afterCookie.RemotePort,
                "The cookie check should use a reused HTTP connection.");
            AssertTrue(!afterCookie.Headers.ContainsKey("Cookie") && !afterCookie.Headers.ContainsKey("Authorization"),
                "A reused connection must not send cookies or ambient authorization credentials.");

            var exactLimit = new string('x', 1_048_576);
            AssertSuccess(Complete(InvokeFetch("/exact-limit", CancellationToken.None)), 200, exactLimit);
            AssertError(Complete(InvokeFetch("/over-limit", CancellationToken.None)), "ResponseTooLarge");
            AssertError(Complete(InvokeFetch("/invalid-text", CancellationToken.None)), "InvalidText");

            AssertError(Complete(InvokeFetch("/reset", CancellationToken.None)), "Transport");

            using (var preCanceled = new CancellationTokenSource())
            {
                preCanceled.Cancel();
                var observed = false;
                try
                {
                    _ = Complete(InvokeFetch("/pre-canceled", preCanceled.Token));
                }
                catch (OperationCanceledException)
                {
                    observed = true;
                }
                AssertTrue(observed, "A pre-canceled host token must propagate OperationCanceledException.");
                AssertTrue(!server.Requests.Any(request => request.Target == "/pre-canceled"),
                    "A pre-canceled host token must stop before sending a request.");
            }

            using (var inFlightCancellation = new CancellationTokenSource())
            {
                var call = InvokeFetch("/hold-cancel", inFlightCancellation.Token);
                server.WaitForRequestAsync("/hold-cancel").WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                inFlightCancellation.Cancel();
                server.WaitForClientDisconnectAsync("/hold-cancel").WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                var observed = false;
                try
                {
                    _ = CompleteWithin(call, TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                    observed = true;
                }
                AssertTrue(observed, "An in-flight host cancellation must propagate OperationCanceledException.");
            }

            var timeoutCall = InvokeFetch("/hold-timeout", CancellationToken.None);
            server.WaitForRequestAsync("/hold-timeout").WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            object timeoutResult;
            try
            {
                timeoutResult = CompleteWithin(timeoutCall, TimeSpan.FromSeconds(15));
            }
            finally
            {
                server.ReleaseHeldRequest("/hold-timeout");
            }
            AssertError(timeoutResult, "Timeout");
        }
        finally
        {
            loadContext.Unload();
        }

        return weakReference;
    }

    private static async Task TestFsWriteManagedLibrary(Harness harness)
    {
        const string source = "module harness::fs_write_library;\n"
            + "pub fn write(fs: FsWrite, path: Text, value: Text) -> Result<bool, FsError> effects { fs.write } { return fs.write_text(path, value); }\n";
        var check = await harness.InvokeAsync("fs-write-library-check", "check", source, "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length,
            "A declared direct filesystem write effect should pass library checking.");

        var build = await harness.InvokeAsync("fs-write-library-build", "build", source);
        AssertBuiltDll(build, Path.GetDirectoryName(harness.LastSourcePath)!);

        var dllPath = ParseBuiltArtifact(build, "Built library: ");
        var probeDirectory = Path.Combine(harness.TemporaryRoot, $"fs-write-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeDirectory);
        var loadContext = ProbeFsWriteRuntimeMappings(dllPath, probeDirectory);
        for (var attempt = 0; attempt < 10 && loadContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        AssertTrue(!loadContext.IsAlive, "The generated FsWrite library's collectible load context should unload after the trusted probe.");
        Directory.Delete(probeDirectory, recursive: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ProbeFsWriteRuntimeMappings(string dllPath, string probeDirectory)
    {
        var loadContext = new AssemblyLoadContext($"fs-write-probe-{Guid.NewGuid():N}", isCollectible: true);
        var weakReference = new WeakReference(loadContext);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var moduleType = assembly.GetType("HobModule", throwOnError: true)!;
            var fsWriteType = moduleType.GetNestedType("FsWrite", BindingFlags.Public)
                ?? throw new InvalidOperationException("Generated library does not expose its nested opaque FsWrite runtime type.");
            var fsWriteConstructor = fsWriteType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null)
                ?? throw new InvalidOperationException("Generated FsWrite has no trusted non-public constructor.");
            var fsWrite = fsWriteConstructor.Invoke(null);
            var writeFunction = moduleType.GetMethod("Function_0", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Generated library does not contain Function_0.");

            object InvokeWrite(string path, string value) => writeFunction.Invoke(null, [fsWrite, path, value])
                ?? throw new InvalidOperationException("Generated write_text returned null instead of Result<bool, FsError>.");

            void AssertSuccess(object result, string operation)
            {
                AssertTrue(result.GetType().Name.StartsWith("Ok", StringComparison.Ordinal),
                    $"{operation} should map to Result.Ok, got {result.GetType().FullName}.");
                AssertEqual(true, result.GetType().GetProperty("Value")?.GetValue(result) as bool?,
                    $"{operation} should return true in Result.Ok.");
            }

            string AssertError(object result, string expectedVariant, string operation)
            {
                AssertTrue(result.GetType().Name.StartsWith("Err", StringComparison.Ordinal),
                    $"{operation} should map to Result.Err, got {result.GetType().FullName}.");
                var error = result.GetType().GetProperty("Error")?.GetValue(result)
                    ?? throw new InvalidOperationException($"{operation} did not expose its FsError value.");
                var variant = error.GetType().Name;
                AssertTrue(variant.StartsWith(expectedVariant, StringComparison.Ordinal),
                    $"{operation} should map to FsError.{expectedVariant}, got {error.GetType().FullName}.");
                return variant;
            }

            void AssertNoTemporaryFiles(string operation)
            {
                AssertTrue(!Directory.EnumerateFiles(probeDirectory, "*.tmp", SearchOption.TopDirectoryOnly).Any(),
                    $"{operation} must not leave a temporary file beside the destination.");
            }

            var destination = Path.Combine(probeDirectory, "target.txt");
            const string unicodeText = "strict UTF-8 λ";
            AssertSuccess(InvokeWrite(destination, "longer existing contents"), "Initial overwrite");
            AssertSuccess(InvokeWrite(destination, unicodeText), "Unicode write");
            var expectedBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetBytes(unicodeText);
            AssertTrue(File.ReadAllBytes(destination).AsSpan().SequenceEqual(expectedBytes),
                "FsWrite should write strict UTF-8 bytes without a BOM and replace longer contents exactly.");
            AssertNoTemporaryFiles("A successful write");

            AssertSuccess(InvokeWrite(destination, "x"), "Short overwrite");
            AssertTrue(File.ReadAllBytes(destination).SequenceEqual([(byte)'x']),
                "Replacing a longer destination with shorter text should leave only the exact new bytes.");
            AssertNoTemporaryFiles("A shorter overwrite");

            var preservedBytes = File.ReadAllBytes(destination);
            AssertError(InvokeWrite(destination, "\uD800"), "InvalidText", "An unpaired surrogate write");
            AssertTrue(File.ReadAllBytes(destination).SequenceEqual(preservedBytes),
                "Invalid text must fail before changing the destination.");
            AssertNoTemporaryFiles("An invalid text write");

            var missingParentPath = Path.Combine(probeDirectory, "missing-parent", "child.txt");
            AssertError(InvokeWrite(missingParentPath, "value"), "NotFound", "A write to a missing parent directory");
            AssertTrue(!Directory.Exists(Path.GetDirectoryName(missingParentPath)),
                "A write to a missing parent must not create directories.");
            AssertNoTemporaryFiles("A write to a missing parent");

            AssertError(InvokeWrite(string.Empty, "value"), "InvalidPath", "An empty path write");
            AssertError(InvokeWrite("invalid\0path", "value"), "InvalidPath", "A NUL path write");
            AssertNoTemporaryFiles("Invalid path writes");
        }
        finally
        {
            loadContext.Unload();
        }

        return weakReference;
    }

    private static async Task TestAsyncFsWriteRuntimeAdapter(Harness harness)
    {
        const string source = "module harness::async_fs_write_library;\n"
            + "pub async fn write(fs: FsWrite, path: Text, value: Text) -> Result<bool, FsError> effects { fs.write } { return await fs.write_text_async(path, value); }\n";
        var check = await harness.InvokeAsync("async-fs-write-library-check", "check", source, "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var build = await harness.InvokeAsync("async-fs-write-library-build", "build", source);
        AssertBuiltDll(build, Path.GetDirectoryName(harness.LastSourcePath)!);
        var dllPath = ParseBuiltArtifact(build, "Built library: ");
        var probeDirectory = Path.Combine(harness.TemporaryRoot, $"async-fs-write-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeDirectory);
        try
        {
            var loadContext = ProbeAsyncFsWriteRuntimeMappings(dllPath, probeDirectory);
            for (var attempt = 0; attempt < 10 && loadContext.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            AssertTrue(!loadContext.IsAlive,
                "The generated async FsWrite library's collectible load context should unload after the trusted probe.");
        }
        finally
        {
            Directory.Delete(probeDirectory, recursive: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ProbeAsyncFsWriteRuntimeMappings(string dllPath, string probeDirectory)
    {
        var loadContext = new AssemblyLoadContext($"async-fs-write-probe-{Guid.NewGuid():N}", isCollectible: true);
        var weakReference = new WeakReference(loadContext);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var moduleType = assembly.GetType("HobModule", throwOnError: true)!;
            var fsWriteType = moduleType.GetNestedType("FsWrite", BindingFlags.Public)
                ?? throw new InvalidOperationException("Generated async library does not expose its nested opaque FsWrite runtime type.");
            var fsWriteConstructor = fsWriteType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(CancellationToken)],
                modifiers: null)
                ?? throw new InvalidOperationException("Generated async FsWrite has no trusted cancellation-token constructor.");
            var writeFunction = moduleType.GetMethod("Function_0", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Generated async library does not contain Function_0.");
            var parameters = writeFunction.GetParameters();
            AssertEqual(4, parameters.Length, "The async FsWrite function should append one hidden cancellation-token parameter.");
            AssertEqual(typeof(CancellationToken), parameters[^1].ParameterType,
                "The async FsWrite function's hidden final argument should be CancellationToken.");

            Task StartWrite(string path, string value, CancellationToken token)
            {
                var fsWrite = fsWriteConstructor.Invoke([token]);
                return writeFunction.Invoke(null, [fsWrite, path, value, token]) as Task
                    ?? throw new InvalidOperationException("Generated write_text_async did not return a Task.");
            }

            static object GetResult(Task task) => task.GetType().GetProperty("Result")?.GetValue(task)
                ?? throw new InvalidOperationException("Generated write_text_async returned no Result value.");

            object InvokeWrite(string path, string value, CancellationToken token = default)
            {
                var task = StartWrite(path, value, token);
                task.GetAwaiter().GetResult();
                return GetResult(task);
            }

            static void AssertSuccess(object result, string operation)
            {
                AssertTrue(result.GetType().Name.StartsWith("Ok", StringComparison.Ordinal),
                    $"{operation} should map to Result.Ok, got {result.GetType().FullName}.");
                AssertEqual(true, result.GetType().GetProperty("Value")?.GetValue(result) as bool?,
                    $"{operation} should return true in Result.Ok.");
            }

            static void AssertError(object result, string expectedVariant, string operation)
            {
                AssertTrue(result.GetType().Name.StartsWith("Err", StringComparison.Ordinal),
                    $"{operation} should map to Result.Err, got {result.GetType().FullName}.");
                var error = result.GetType().GetProperty("Error")?.GetValue(result)
                    ?? throw new InvalidOperationException($"{operation} did not expose its FsError value.");
                AssertTrue(error.GetType().Name.StartsWith(expectedVariant, StringComparison.Ordinal),
                    $"{operation} should map to FsError.{expectedVariant}, got {error.GetType().FullName}.");
            }

            void AssertNoTemporaryFiles(string operation)
            {
                AssertTrue(!Directory.EnumerateFiles(probeDirectory, "*.tmp", SearchOption.TopDirectoryOnly).Any(),
                    $"{operation} must not leave a temporary file beside the destination.");
            }

            var destination = Path.Combine(probeDirectory, "target.txt");
            const string unicodeText = "async strict UTF-8 λ 😀";
            AssertSuccess(InvokeWrite(destination, unicodeText), "Async create");
            var expectedBytes = new UTF8Encoding(false, true).GetBytes(unicodeText);
            AssertTrue(File.ReadAllBytes(destination).AsSpan().SequenceEqual(expectedBytes),
                "Async FsWrite should create exact strict UTF-8 bytes without a BOM.");
            AssertNoTemporaryFiles("A successful async create");

            AssertSuccess(InvokeWrite(destination, "short λ"), "Async overwrite");
            var overwrittenBytes = new UTF8Encoding(false, true).GetBytes("short λ");
            AssertTrue(File.ReadAllBytes(destination).AsSpan().SequenceEqual(overwrittenBytes),
                "Async FsWrite should truncate longer destination contents on overwrite.");
            AssertNoTemporaryFiles("A successful async overwrite");

            AssertSuccess(InvokeWrite(destination, string.Empty), "Async empty write");
            AssertEqual(0L, new FileInfo(destination).Length, "An empty Text value should create or overwrite with an empty file.");
            AssertNoTemporaryFiles("A successful async empty write");

            AssertSuccess(InvokeWrite(destination, "preserved"), "Async restore before error mapping");
            var preservedBytes = File.ReadAllBytes(destination);
            AssertError(InvokeWrite(destination, "\uD800"), "InvalidText", "An unpaired surrogate async write");
            AssertTrue(File.ReadAllBytes(destination).AsSpan().SequenceEqual(preservedBytes),
                "Invalid UTF-16 input must fail before changing the existing destination.");
            AssertNoTemporaryFiles("An invalid-text async write");

            var missingParentPath = Path.Combine(probeDirectory, "missing-parent", "child.txt");
            AssertError(InvokeWrite(missingParentPath, "value"), "NotFound", "An async write to a missing parent directory");
            AssertTrue(!Directory.Exists(Path.GetDirectoryName(missingParentPath)),
                "An async write to a missing parent must not create directories.");
            AssertError(InvokeWrite(string.Empty, "value"), "InvalidPath", "An empty async destination path");
            AssertError(InvokeWrite("invalid\0path", "value"), "InvalidPath", "A NUL async destination path");
            AssertNoTemporaryFiles("Async filesystem error mappings");

            using (var canceledBeforeStart = new CancellationTokenSource())
            {
                canceledBeforeStart.Cancel();
                var observed = false;
                try
                {
                    _ = InvokeWrite(destination, "must not replace", canceledBeforeStart.Token);
                }
                catch (OperationCanceledException)
                {
                    observed = true;
                }
                catch (TargetInvocationException exception) when (exception.InnerException is OperationCanceledException)
                {
                    observed = true;
                }
                AssertTrue(observed, "A pre-canceled async FsWrite token should propagate OperationCanceledException.");
                AssertTrue(File.ReadAllBytes(destination).AsSpan().SequenceEqual(preservedBytes),
                    "A pre-canceled async write must leave the destination unchanged.");
                AssertNoTemporaryFiles("A pre-canceled async write");
            }

            var previousContext = SynchronizationContext.Current;
            using (var synchronizationContext = new QueuedSynchronizationContext())
            using (var cancelAfterTempCreation = new CancellationTokenSource())
            {
                Task? pending = null;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                    pending = StartWrite(destination, "must not commit", cancelAfterTempCreation.Token);
                    AssertTrue(synchronizationContext.HasPendingCallbacks,
                        "The async write should queue its continuation after temporary-file creation to the captured synchronization context.");
                    var stagedFiles = Directory.EnumerateFiles(probeDirectory, "*.tmp", SearchOption.TopDirectoryOnly).ToArray();
                    AssertEqual(1, stagedFiles.Length,
                        "The deterministic cancellation point should have exactly one same-directory temporary file created.");
                    AssertTrue(File.ReadAllBytes(destination).AsSpan().SequenceEqual(preservedBytes),
                        "The destination must retain its old bytes until the write commits.");

                    cancelAfterTempCreation.Cancel();
                    synchronizationContext.RunUntilCompleted(pending, TimeSpan.FromSeconds(5));
                    var observed = false;
                    try
                    {
                        pending.GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        observed = true;
                    }
                    AssertTrue(observed, "Cancellation after temporary-file creation should propagate OperationCanceledException.");
                    AssertTrue(File.ReadAllBytes(destination).AsSpan().SequenceEqual(preservedBytes),
                        "Cancellation after temporary-file creation must leave the destination unchanged.");
                    AssertNoTemporaryFiles("A write canceled after temporary-file creation");
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previousContext);
                }
            }
        }
        finally
        {
            loadContext.Unload();
        }

        return weakReference;
    }

    private static async Task TestAsyncFsWriteCliRuntime(Harness harness)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64
            || (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("The async FsWrite NativeAOT CLI smoke test targets Windows x64 and Linux x64 hosts.");

        const string main = "module app::main;\n"
            + "command save { help \"Save UTF-8 text.\"; argument output: FilePath help \"Destination.\"; argument value: Text help \"Contents.\"; handler: self::handlers::run; error: self::handlers::describe; }\n";
        const string handlers = "module handlers;\n"
            + "pub async fn run(args: self::app::main::SaveArgs, writer: FsWrite) -> Result<Text, FsError> effects { fs.write } {\n"
            + "    return match await writer.write_text_async(args.output, args.value) {\n"
            + "        Ok(written) => Ok(\"saved\"),\n"
            + "        Err(error) => Err(error)\n"
            + "    };\n"
            + "}\n"
            + "pub fn describe(error: FsError) -> Text effects {} { return match error {\n"
            + "    FsError.NotFound => \"not found\", FsError.PermissionDenied => \"permission denied\",\n"
            + "    FsError.InvalidPath => \"invalid path\", FsError.InvalidText => \"invalid text\", FsError.Io => \"I/O error\"\n"
            + "}; }\n";
        var packageRoot = await harness.WritePackageAsync(
            "async-fs-write-cli",
            CliPackageManifest() + "\n[capabilities]\nfs.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = main,
                ["src/handlers.hob"] = handlers
            });

        var check = await harness.InvokePackageDirectoryAsync("async-fs-write-cli-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));
        var build = await harness.InvokePackageDirectoryAsync("async-fs-write-cli-managed-build", packageRoot, "build");
        var managedExecutable = ParseBuiltArtifact(build, "Built executable: ");
        var managedOutputDirectory = Path.GetDirectoryName(managedExecutable)!;
        var managedSchemaPath = Path.Combine(managedOutputDirectory, "command-schema.json");
        AssertTrue(File.Exists(managedSchemaPath), $"Expected managed command schema at {managedSchemaPath}.");
        var managedSchemaBytes = await File.ReadAllBytesAsync(managedSchemaPath);
        using (var schema = JsonDocument.Parse(managedSchemaBytes))
        {
            AssertEqual(5, schema.RootElement.GetProperty("schema_version").GetInt32(),
                "Async FsWrite managed CLI builds must preserve command schema version 5.");
            var command = schema.RootElement.GetProperty("commands").EnumerateArray().Single();
            AssertEqual("save", command.GetProperty("name").GetString(), "The async FsWrite command schema should retain its name.");
            AssertJsonStringArray(command.GetProperty("capabilities"), ["fs.write"]);
        }

        var destinationDirectory = Path.Combine(harness.TemporaryRoot, $"async-fs-write-cli-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(destinationDirectory, "managed.txt");
        const string firstValue = "managed async FsWrite λ 😀";
        var firstRun = await harness.InvokePackageDirectoryAsync(
            "async-fs-write-cli-create", packageRoot, "run", "--", "save", destination, firstValue);
        AssertRunOutput("saved" + Environment.NewLine, firstRun);
        AssertTrue(File.ReadAllBytes(destination).AsSpan().SequenceEqual(new UTF8Encoding(false, true).GetBytes(firstValue)),
            "The managed async CLI should create exact UTF-8 bytes without a BOM.");
        AssertTrue(!Directory.EnumerateFiles(destinationDirectory, "*.tmp", SearchOption.TopDirectoryOnly).Any(),
            "A successful managed async CLI write must leave no temporary file.");

        const string replacement = "short λ";
        var overwrite = await harness.InvokePackageDirectoryAsync(
            "async-fs-write-cli-overwrite", packageRoot, "run", "--", "save", destination, replacement);
        AssertRunOutput("saved" + Environment.NewLine, overwrite);
        AssertTrue(File.ReadAllBytes(destination).AsSpan().SequenceEqual(new UTF8Encoding(false, true).GetBytes(replacement)),
            "The managed async CLI should truncate an existing longer file to exact replacement bytes.");
        AssertTrue(!Directory.EnumerateFiles(destinationDirectory, "*.tmp", SearchOption.TopDirectoryOnly).Any(),
            "A successful managed async CLI overwrite must leave no temporary file.");

        var nativeBuild = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "async-fs-write-cli-aot-build",
            packageRoot,
            "build",
            AotPublishTimeout,
            "--aot",
            "--rid",
            CurrentHostAotRid());
        AssertEqual(0, nativeBuild.ExitCode, Describe(nativeBuild));
        const string nativePrefix = "Built native executable: ";
        AssertTrue(nativeBuild.StandardOutput.StartsWith(nativePrefix, StringComparison.Ordinal), Describe(nativeBuild));
        AssertTrue(nativeBuild.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal), Describe(nativeBuild));
        var nativeExecutable = nativeBuild.StandardOutput[nativePrefix.Length..^Environment.NewLine.Length];
        AssertTrue(Path.IsPathFullyQualified(nativeExecutable) && File.Exists(nativeExecutable),
            $"Expected async FsWrite NativeAOT executable at {nativeExecutable}.");
        var nativeOutputDirectory = Path.GetDirectoryName(nativeExecutable)!;
        var nativeSchemaPath = Path.Combine(nativeOutputDirectory, "command-schema.json");
        AssertTrue(File.Exists(nativeSchemaPath), $"Expected NativeAOT command schema at {nativeSchemaPath}.");
        var nativeSchemaBytes = await File.ReadAllBytesAsync(nativeSchemaPath);
        AssertTrue(managedSchemaBytes.AsSpan().SequenceEqual(nativeSchemaBytes),
            "Managed and NativeAOT async FsWrite command schemas must be byte-for-byte identical.");
        using var receipt = await AssertBuildReceiptAsync(
            nativeOutputDirectory,
            "native_aot",
            CurrentHostAotRid(),
            [Path.GetRelativePath(nativeOutputDirectory, nativeExecutable).Replace(Path.DirectorySeparatorChar, '/'), "command-schema.json"],
            packageRoot);
        AssertEqual(4, receipt.RootElement.GetProperty("schema_version").GetInt32(),
            "Async FsWrite NativeAOT builds must use build receipt schema version 4.");

        var nativeDestination = Path.Combine(destinationDirectory, "native.txt");
        const string nativeValue = "native async FsWrite λ 😀";
        var nativeRun = await ExecuteNativeAsync(
            nativeExecutable,
            TimeSpan.FromSeconds(30),
            "save",
            nativeDestination,
            nativeValue);
        AssertRunOutput("saved" + Environment.NewLine, nativeRun);
        AssertTrue(File.ReadAllBytes(nativeDestination).AsSpan().SequenceEqual(new UTF8Encoding(false, true).GetBytes(nativeValue)),
            "The NativeAOT async CLI should create exact UTF-8 bytes without a BOM.");
        AssertTrue(!Directory.EnumerateFiles(destinationDirectory, "*.tmp", SearchOption.TopDirectoryOnly).Any(),
            "A successful NativeAOT async CLI write must leave no temporary file.");
    }

    private static async Task TestCliCapabilityManifestAndLock(Harness harness)
    {
        const string source = """
            module app::main;
            command read {
                help "Read a file.";
                argument input: FilePath help "File to read.";
                handler: self::app::main::read_file;
                error: self::app::main::describe_error;
            }
            pub fn read_file(args: self::app::main::ReadArgs, fs: FsRead, writer: FsWrite) -> Result<Text, FsError> effects { fs.read, fs.write } {
                let written: Result<bool, FsError> = writer.write_text(args.input, "locked");
                return fs.read_text(args.input);
            }
            pub fn describe_error(error: FsError) -> Text effects {} {
                return match error {
                    FsError.NotFound => "not found",
                    FsError.PermissionDenied => "permission denied",
                    FsError.InvalidPath => "invalid path",
                    FsError.InvalidText => "invalid text",
                    FsError.Io => "I/O error",
                };
            }
            """;
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = source
        };

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "cli-fsread-missing-grant",
            CliPackageManifest(),
            sources,
            "E_CAPABILITY_MISSING",
            "src/app/main.hob");

        var noFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "cli-capability-unknown-manifest",
            CliPackageManifest() + "\n[capabilities]\nnet.unknown = \"allow\"\n",
            noFiles,
            "E_MANIFEST",
            "hob.toml");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "cli-capability-wrong-grant-manifest",
            CliPackageManifest() + "\n[capabilities]\nfs.read = \"deny\"\n",
            noFiles,
            "E_MANIFEST",
            "hob.toml");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "cli-capability-duplicate-grant-manifest",
            CliPackageManifest() + "\n[capabilities]\nfs.read = \"allow\"\nfs.read = \"allow\"\n",
            noFiles,
            "E_MANIFEST",
            "hob.toml");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "library-capability-grant-manifest",
            LibraryPackageManifest() + "\n[capabilities]\nfs.read = \"allow\"\n",
            noFiles,
            "E_MANIFEST",
            "hob.toml");

        const string argsOnlySource = """
            module app::main;
            pub union EchoError { Failed }
            command echo {
                help "Echo one value.";
                argument input: Text help "Value to echo.";
                handler: self::app::main::echo;
                error: self::app::main::describe_error;
            }
            pub fn echo(args: self::app::main::EchoArgs) -> Result<Text, self::app::main::EchoError> effects {} {
                return Ok(args.input);
            }
            pub fn describe_error(error: self::app::main::EchoError) -> Text effects {} {
                return match error { self::app::main::EchoError.Failed => "failed" };
            }
            """;
        var argsOnlyRoot = await harness.WritePackageAsync(
            "cli-fsread-grant-not-used",
            CliPackageManifest() + "\n[capabilities]\nfs.read = \"allow\"\nfs.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = argsOnlySource
            });
        var unusedGrantBuild = await harness.InvokePackageDirectoryAsync(
            "cli-fsread-grant-not-used-build", argsOnlyRoot, "build");
        AssertEqual(0, unusedGrantBuild.ExitCode, Describe(unusedGrantBuild));
        var unusedGrantArtifact = unusedGrantBuild.StandardOutput["Built executable: ".Length..].Trim();
        var unusedGrantSchemaPath = Path.Combine(Path.GetDirectoryName(unusedGrantArtifact)!, "command-schema.json");
        using (var unusedGrantSchema = JsonDocument.Parse(await File.ReadAllBytesAsync(unusedGrantSchemaPath)))
        {
            AssertEqual(5, unusedGrantSchema.RootElement.GetProperty("schema_version").GetInt32(),
                "Command schemas with the capabilities field must use version 4.");
            AssertEqual(0, unusedGrantSchema.RootElement.GetProperty("commands")[0]
                    .GetProperty("capabilities").GetArrayLength(),
                "Unused fs.read and fs.write grants must not appear as injected capabilities for an args-only handler.");
        }

        const string withGrant = "name = \"harness-package\"\n"
            + "version = \"0.1.0\"\n"
            + "kind = \"cli\"\n"
            + "source_root = \"src\"\n"
            + "entry_module = \"app::main\"\n"
            + "\n[capabilities]\nfs.read = \"allow\"\nfs.write = \"allow\"\n"
            + "\n[dependencies]\nvalidation = \"../validation\"\n";
        const string withoutGrant = "name = \"harness-package\"\n"
            + "version = \"0.1.0\"\n"
            + "kind = \"cli\"\n"
            + "source_root = \"src\"\n"
            + "entry_module = \"app::main\"\n"
            + "\n[capabilities]\nfs.read = \"allow\"\n"
            + "\n[dependencies]\nvalidation = \"../validation\"\n";
        var graphRoot = await harness.WritePackageGraphAsync(
            "cli-fsread-grant-lock",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(withGrant, sources),
                ["validation"] = new PackageFixture(
                    LibraryPackageManifest("validation-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/validation.hob"] = "module validation; pub fn marker() -> i32 effects {} { return 1; }"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "cli-fsread-grant-lock-create", graphRoot, "lock"));
        var rootManifestPath = Path.Combine(graphRoot, "hob.toml");
        var lockPath = Path.Combine(graphRoot, "hob.lock");
        AssertTrue(File.Exists(lockPath), "The dependency lock should exist before changing the app grant.");

        await File.WriteAllTextAsync(rootManifestPath, withoutGrant);
        var staleAfterRemovingGrant = await harness.InvokePackageDirectoryAsync(
            "cli-fsread-grant-lock-stale-remove", graphRoot, "check", "--json");
        AssertTrue(staleAfterRemovingGrant.ExitCode != 0
            && staleAfterRemovingGrant.StandardOutput.Contains("E_LOCK", StringComparison.Ordinal),
            $"Changing a capability grant must stale the package lock. {Describe(staleAfterRemovingGrant)}");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "cli-fsread-grant-lock-refresh-remove", graphRoot, "lock"));
        var missingAfterRefresh = await harness.InvokePackageDirectoryAsync(
            "cli-fsread-grant-no-grant-after-refresh", graphRoot, "check", "--json");
        AssertTrue(missingAfterRefresh.ExitCode != 0
            && missingAfterRefresh.StandardOutput.Contains("E_CAPABILITY_MISSING", StringComparison.Ordinal),
            $"After refreshing the lock, the absent grant should fail capability checking. {Describe(missingAfterRefresh)}");
        var missingWriteDiagnostics = ParseDiagnosticSnapshots(missingAfterRefresh.StandardOutput);
        AssertEqual(1, missingWriteDiagnostics.Length, Describe(missingAfterRefresh));
        AssertEqual("Command handler requires the root package's fs.write capability grant",
            missingWriteDiagnostics[0].Message,
            "Removing only fs.write should identify the missing write grant after lock refresh.");

        await File.WriteAllTextAsync(rootManifestPath, withGrant);
        var staleAfterAddingGrant = await harness.InvokePackageDirectoryAsync(
            "cli-fsread-grant-lock-stale-add", graphRoot, "check", "--json");
        AssertTrue(staleAfterAddingGrant.ExitCode != 0
            && staleAfterAddingGrant.StandardOutput.Contains("E_LOCK", StringComparison.Ordinal),
            $"Adding a capability grant must also stale the package lock. {Describe(staleAfterAddingGrant)}");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "cli-fsread-grant-lock-refresh-add", graphRoot, "lock"));
        var grantedAfterRefresh = await harness.InvokePackageDirectoryAsync(
            "cli-fsread-grant-check-after-refresh", graphRoot, "check", "--json");
        AssertEqual(0, grantedAfterRefresh.ExitCode, Describe(grantedAfterRefresh));
        AssertEqual(0, ParseDiagnosticSnapshots(grantedAfterRefresh.StandardOutput).Length, Describe(grantedAfterRefresh));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ProbeFsReadRuntimeMappings(
        string dllPath,
        string validPath,
        string missingPath,
        string invalidTextPath,
        string validText)
    {
        var loadContext = new AssemblyLoadContext($"fs-read-probe-{Guid.NewGuid():N}", isCollectible: true);
        var weakReference = new WeakReference(loadContext);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var moduleType = assembly.GetType("HobModule", throwOnError: true)!;
            var fsReadType = moduleType.GetNestedType("FsRead", BindingFlags.Public)
                ?? throw new InvalidOperationException("Generated library does not expose its nested opaque FsRead runtime type.");
            var fsReadConstructor = fsReadType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null)
                ?? throw new InvalidOperationException("Generated FsRead has no trusted non-public constructor.");
            var fsRead = fsReadConstructor.Invoke(null);
            var readFunction = moduleType.GetMethod("Function_0", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Generated library does not contain Function_0.");

            object InvokeRead(string path) => readFunction.Invoke(null, [fsRead, path])
                ?? throw new InvalidOperationException("Generated read_text returned null instead of Result<Text, FsError>.");

            var success = InvokeRead(validPath);
            AssertTrue(success.GetType().Name.StartsWith("Ok", StringComparison.Ordinal),
                $"A readable UTF-8 file should map to Result.Ok, got {success.GetType().FullName}.");
            AssertEqual(validText, success.GetType().GetProperty("Value")?.GetValue(success) as string,
                "Result.Ok should carry the strictly decoded UTF-8 text.");

            var missing = InvokeRead(missingPath);
            AssertTrue(missing.GetType().Name.StartsWith("Err", StringComparison.Ordinal),
                $"A missing file should map to Result.Err, got {missing.GetType().FullName}.");
            var missingError = missing.GetType().GetProperty("Error")?.GetValue(missing)
                ?? throw new InvalidOperationException("Result.Err did not expose its FsError value.");
            AssertTrue(missingError.GetType().Name.StartsWith("NotFound", StringComparison.Ordinal),
                $"A missing file should map to FsError.NotFound, got {missingError.GetType().FullName}.");

            var invalidText = InvokeRead(invalidTextPath);
            AssertTrue(invalidText.GetType().Name.StartsWith("Err", StringComparison.Ordinal),
                $"Invalid UTF-8 should map to Result.Err, got {invalidText.GetType().FullName}.");
            var invalidTextError = invalidText.GetType().GetProperty("Error")?.GetValue(invalidText)
                ?? throw new InvalidOperationException("Result.Err did not expose its FsError value.");
            AssertTrue(invalidTextError.GetType().Name.StartsWith("InvalidText", StringComparison.Ordinal),
                $"Invalid UTF-8 should map to FsError.InvalidText, got {invalidTextError.GetType().FullName}.");
        }
        finally
        {
            loadContext.Unload();
        }

        return weakReference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ProbeAsyncFsReadRuntimeMappings(
        string dllPath,
        string validPath,
        string missingPath,
        string invalidTextPath,
        string validText)
    {
        var loadContext = new AssemblyLoadContext($"async-fs-read-probe-{Guid.NewGuid():N}", isCollectible: true);
        var weakReference = new WeakReference(loadContext);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var moduleType = assembly.GetType("HobModule", throwOnError: true)!;
            var fsReadType = moduleType.GetNestedType("FsRead", BindingFlags.Public)
                ?? throw new InvalidOperationException("Generated async library does not expose its nested FsRead runtime type.");
            var fsReadConstructor = fsReadType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(CancellationToken)],
                modifiers: null)
                ?? throw new InvalidOperationException("Generated FsRead has no trusted cancellation-token constructor.");
            var readFunction = moduleType.GetMethod("Function_0", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Generated async library does not contain Function_0.");

            object InvokeRead(string path, CancellationToken cancellationToken)
            {
                var fsRead = fsReadConstructor.Invoke([cancellationToken]);
                var task = readFunction.Invoke(null, [fsRead, path, cancellationToken]) as Task
                    ?? throw new InvalidOperationException("Generated read_text_async did not return a Task.");
                task.GetAwaiter().GetResult();
                return task.GetType().GetProperty("Result")?.GetValue(task)
                    ?? throw new InvalidOperationException("Generated read_text_async returned no Result value.");
            }

            static void AssertSuccess(object result, string expectedText)
            {
                AssertTrue(result.GetType().Name.StartsWith("Ok", StringComparison.Ordinal),
                    $"A readable UTF-8 file should map to Result.Ok, got {result.GetType().FullName}.");
                AssertEqual(expectedText, result.GetType().GetProperty("Value")?.GetValue(result) as string,
                    "Result.Ok should carry the strictly decoded UTF-8 text.");
            }

            static string AssertError(object result, string expectedVariant)
            {
                AssertTrue(result.GetType().Name.StartsWith("Err", StringComparison.Ordinal),
                    $"A filesystem failure should map to Result.Err, got {result.GetType().FullName}.");
                var error = result.GetType().GetProperty("Error")?.GetValue(result)
                    ?? throw new InvalidOperationException("Result.Err did not expose its FsError value.");
                var variant = error.GetType().Name;
                AssertTrue(variant.StartsWith(expectedVariant, StringComparison.Ordinal),
                    $"Expected FsError.{expectedVariant}, got {error.GetType().FullName}.");
                return variant;
            }

            AssertSuccess(InvokeRead(validPath, CancellationToken.None), validText);
            AssertError(InvokeRead(missingPath, CancellationToken.None), "NotFound");
            AssertError(InvokeRead(invalidTextPath, CancellationToken.None), "InvalidText");

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var cancellationObserved = false;
            try
            {
                _ = InvokeRead(validPath, canceled.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved = true;
            }
            AssertTrue(cancellationObserved,
                "A pre-canceled FsRead token should propagate OperationCanceledException from read_text_async.");
        }
        finally
        {
            loadContext.Unload();
        }

        return weakReference;
    }

    private static async Task TestLibraryBuild(Harness harness)
    {
        const string source = """
            module harness::library;
            pub fn square(value: i32) -> i32 effects {} { return value * value; }
            """;
        var result = await harness.InvokeAsync("library-build", "build", source);
        AssertBuiltDll(result, Path.GetDirectoryName(harness.LastSourcePath)!);

        const string mainNamedLibrarySource = """
            module harness::main_named_library;
            pub union U { A }
            pub fn main(value: self::harness::main_named_library::U) -> self::harness::main_named_library::U effects {} { return value; }
            """;
        var mainNamedLibrary = await harness.InvokeAsync("main-named-library-build", "build", mainNamedLibrarySource);
        AssertBuiltDll(mainNamedLibrary, Path.GetDirectoryName(harness.LastSourcePath)!);

        var run = await harness.InvokeAsync("main-named-library-run", "run", mainNamedLibrarySource);
        AssertTrue(run.ExitCode != 0, Describe(run));
        AssertEqual(string.Empty, run.StandardOutput, Describe(run));
        AssertTrue(run.StandardError.Contains("E_ENTRYPOINT", StringComparison.Ordinal),
            $"run should reject the non-primitive parameterized main. {Describe(run)}");
    }

    private static async Task TestStandaloneBuildReceipt(Harness harness)
    {
        const string source = "module harness::build_receipt; pub fn main() -> i32 effects {} { return 42; }\n";
        var run = await harness.InvokeAsync("build-receipt-standalone-run", "run", source);
        AssertRunOutput("42" + Environment.NewLine, run);
        var sourcePath = harness.LastSourcePath;
        var sourceDirectory = Path.GetDirectoryName(sourcePath)!;
        AssertTrue(!Directory.Exists(Path.Combine(sourceDirectory, "out")),
            "A standalone run must not create a durable output directory or receipt.");

        var firstBuild = await harness.InvokeFileAsync("build-receipt-standalone-build", sourcePath, "build");
        AssertEqual(0, firstBuild.ExitCode, Describe(firstBuild));
        var firstArtifact = ParseBuiltArtifact(firstBuild, "Built executable: ");
        var firstOutputDirectory = Path.GetDirectoryName(firstArtifact)!;
        using var firstReceipt = await AssertBuildReceiptAsync(
            firstOutputDirectory,
            "managed",
            expectedRuntimeIdentifier: null,
            [Path.GetRelativePath(firstOutputDirectory, firstArtifact).Replace(Path.DirectorySeparatorChar, '/')],
            sourceDirectory);
        AssertEqual(0, firstReceipt.RootElement.GetProperty("package_graph").GetArrayLength(),
            "Standalone build receipts should identify an empty package graph.");
        AssertEqual(1, firstReceipt.RootElement.GetProperty("inputs").GetArrayLength(),
            "A standalone receipt should contain exactly one checked source input.");
        AssertEqual("source", firstReceipt.RootElement.GetProperty("inputs")[0].GetProperty("path").GetString(),
            "The standalone source should use the stable logical input name.");
        AssertEqual(JsonValueKind.Null, firstReceipt.RootElement.GetProperty("inputs")[0].GetProperty("package").ValueKind,
            "Standalone inputs should not invent a package identity.");
        var firstInputHash = firstReceipt.RootElement.GetProperty("inputs")[0].GetProperty("sha256").GetString();
        var firstAuditHash = firstReceipt.RootElement.GetProperty("audit_snapshot_sha256").GetString();

        var receiptsBeforeRun = Directory.EnumerateFiles(Path.Combine(sourceDirectory, "out"), "build-receipt.json", SearchOption.AllDirectories).Count();
        var runAfterBuild = await harness.InvokeFileAsync("build-receipt-standalone-run-after-build", sourcePath, "run");
        AssertRunOutput("42" + Environment.NewLine, runAfterBuild);
        var receiptsAfterRun = Directory.EnumerateFiles(Path.Combine(sourceDirectory, "out"), "build-receipt.json", SearchOption.AllDirectories).Count();
        AssertEqual(receiptsBeforeRun, receiptsAfterRun, "A standalone run must not create a build receipt.");

        await File.WriteAllTextAsync(sourcePath, source.Replace("return 42;", "return 43;", StringComparison.Ordinal));
        var changedBuild = await harness.InvokeFileAsync("build-receipt-standalone-changed-build", sourcePath, "build");
        AssertEqual(0, changedBuild.ExitCode, Describe(changedBuild));
        var changedArtifact = ParseBuiltArtifact(changedBuild, "Built executable: ");
        var changedOutputDirectory = Path.GetDirectoryName(changedArtifact)!;
        using var changedReceipt = await AssertBuildReceiptAsync(
            changedOutputDirectory,
            "managed",
            expectedRuntimeIdentifier: null,
            [Path.GetRelativePath(changedOutputDirectory, changedArtifact).Replace(Path.DirectorySeparatorChar, '/')],
            sourceDirectory);
        var changedRoot = changedReceipt.RootElement;
        AssertEqual(firstReceipt.RootElement.GetProperty("performed_checks").GetRawText(),
            changedRoot.GetProperty("performed_checks").GetRawText(),
            "Repeated standalone builds should report the same ordered completed-check IDs.");
        AssertTrue(firstInputHash != changedRoot.GetProperty("inputs")[0].GetProperty("sha256").GetString(),
            "Changing standalone source content must change the checked input hash.");
        AssertTrue(firstAuditHash != changedRoot.GetProperty("audit_snapshot_sha256").GetString(),
            "Changing standalone source content must change the audit snapshot hash.");
    }

    private static async Task TestBuildReceiptPerformedChecks(Harness harness)
    {
        const string mainSource = "module app::main; pub fn main() -> i32 effects {} { return 41; }\n";
        var dependencyFreePackage = await harness.WritePackageAsync(
            "build-receipt-checks-no-lock",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = mainSource
            });
        var dependencyFreeBuild = await harness.InvokePackageDirectoryAsync(
            "build-receipt-checks-no-lock-build", dependencyFreePackage, "build");
        AssertEqual(0, dependencyFreeBuild.ExitCode, Describe(dependencyFreeBuild));
        var dependencyFreeArtifact = ParseBuiltArtifact(dependencyFreeBuild, "Built executable: ");
        var dependencyFreeOutput = Path.GetDirectoryName(dependencyFreeArtifact)!;
        using (var receipt = await AssertBuildReceiptAsync(
                   dependencyFreeOutput,
                   "managed",
                   expectedRuntimeIdentifier: null,
                   [Path.GetRelativePath(dependencyFreeOutput, dependencyFreeArtifact).Replace(Path.DirectorySeparatorChar, '/')],
                   dependencyFreePackage))
        {
            AssertJsonStringArray(receipt.RootElement.GetProperty("performed_checks"),
                [
                    "compiler.package_graph_resolve",
                    "compiler.package_sources_parse",
                    "compiler.semantic_check",
                    "generated.managed_build"
                ]);
        }

        var packageRoot = await harness.WritePackageGraphAsync(
            "build-receipt-checks-required-lock",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest().Replace("name = \"harness-package\"", "name = \"receipt-root\"", StringComparison.Ordinal)
                        + "\n[dependencies]\nsupport = \"../support\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = mainSource
                    }),
                ["support"] = new PackageFixture(
                    LibraryPackageManifest("receipt-support"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/support.hob"] = "module support; pub fn marker() -> i32 effects {} { return 1; }\n"
                    })
            });
        var lockResult = await harness.InvokePackageDirectoryAsync(
            "build-receipt-checks-required-lock-create", packageRoot, "lock");
        AssertLockCommandSucceeded(lockResult);

        var successfulBuild = await harness.InvokePackageDirectoryAsync(
            "build-receipt-checks-required-lock-build", packageRoot, "build");
        AssertEqual(0, successfulBuild.ExitCode, Describe(successfulBuild));
        var artifact = ParseBuiltArtifact(successfulBuild, "Built executable: ");
        var outputDirectory = Path.GetDirectoryName(artifact)!;
        using (var receipt = await AssertBuildReceiptAsync(
                   outputDirectory,
                   "managed",
                   expectedRuntimeIdentifier: null,
                   [Path.GetRelativePath(outputDirectory, artifact).Replace(Path.DirectorySeparatorChar, '/')],
                   packageRoot))
        {
            AssertJsonStringArray(receipt.RootElement.GetProperty("performed_checks"),
                [
                    "compiler.package_graph_resolve",
                    "compiler.package_lock_validate",
                    "compiler.package_sources_parse",
                    "compiler.semantic_check",
                    "generated.managed_build"
                ]);
        }

        var outputRoot = Path.Combine(packageRoot, "out");
        var outputDirectoriesBeforeFailure = Directory.EnumerateDirectories(outputRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var receiptPathsBeforeFailure = Directory.EnumerateFiles(outputRoot, "build-receipt.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        AssertEqual(1, receiptPathsBeforeFailure.Length,
            "The successful package build should leave one receipt in its unique output directory.");
        var receiptBytesBeforeFailure = receiptPathsBeforeFailure.ToDictionary(
            path => path,
            File.ReadAllBytes,
            StringComparer.Ordinal);

        var missingHost = Path.Combine(harness.TemporaryRoot, "missing-build-receipt-dotnet-host.exe");
        var failedBuild = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "build-receipt-checks-failed-build",
            packageRoot,
            "build",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HOB_DOTNET"] = missingHost
            });
        AssertTrue(failedBuild.ExitCode != 0, Describe(failedBuild));
        AssertTrue(failedBuild.StandardError.Contains("E_PROCESS", StringComparison.Ordinal),
            $"A failed generated build should return the process diagnostic. {Describe(failedBuild)}");

        var outputDirectoriesAfterFailure = Directory.EnumerateDirectories(outputRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();
        AssertTrue(outputDirectoriesBeforeFailure.SequenceEqual(outputDirectoriesAfterFailure, StringComparer.Ordinal),
            "A failed build must not create a new output directory beside earlier successful output.");
        var receiptPathsAfterFailure = Directory.EnumerateFiles(outputRoot, "build-receipt.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        AssertTrue(receiptPathsBeforeFailure.SequenceEqual(receiptPathsAfterFailure, StringComparer.Ordinal),
            "A failed build must not create a new successful receipt; a prior receipt may remain in its original output directory.");
        foreach (var receiptPath in receiptPathsBeforeFailure)
        {
            var receiptBytesAfterFailure = await File.ReadAllBytesAsync(receiptPath);
            AssertTrue(receiptBytesBeforeFailure[receiptPath].AsSpan().SequenceEqual(receiptBytesAfterFailure),
                "A failed build must leave prior successful receipt bytes unchanged.");
        }
    }

    private static async Task TestPackageCliRoundTrip(Harness harness)
    {
        var packageRoot = await harness.WritePackageAsync(
            "package-cli-roundtrip",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/catalog/message.hob"] = """
                    module catalog::message;

                    pub struct Greeting { text: Text }
                    pub union Message { Ready(self::catalog::message::Greeting), Missing }

                    pub fn greeting() -> self::catalog::message::Greeting effects {} {
                        return self::catalog::message::Greeting { text: "ready" };
                    }
                    """,
                ["src/app/main.hob"] = """
                    module app::main;

                    pub fn main() -> Text effects {} {
                        let welcome: self::catalog::message::Greeting = self::catalog::message::greeting();
                        let message: self::catalog::message::Message = self::catalog::message::Message.Ready(welcome);
                        return match message {
                            self::catalog::message::Message.Ready(value) => value.text,
                            self::catalog::message::Message.Missing => "missing",
                        };
                    }
                    """
            });

        var check = await harness.InvokePackageDirectoryAsync("package-cli-check", packageRoot, "check");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(string.Empty, check.StandardOutput, Describe(check));
        AssertEqual(string.Empty, check.StandardError, Describe(check));

        var build = await harness.InvokePackageDirectoryAsync("package-cli-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        const string prefix = "Built executable: ";
        AssertTrue(build.StandardOutput.StartsWith(prefix, StringComparison.Ordinal), Describe(build));
        var artifact = build.StandardOutput[prefix.Length..].Trim();
        AssertTrue(Path.IsPathFullyQualified(artifact), $"Expected a full managed artifact path. {Describe(build)}");
        AssertTrue(File.Exists(artifact), $"Expected package build artifact at {artifact}. {Describe(build)}");

        var run = await harness.InvokePackageDirectoryAsync("package-cli-run", packageRoot, "run");
        AssertRunOutput("ready" + Environment.NewLine, run);
    }

    private static async Task TestTypedCliCommandRuntime(Harness harness)
    {
        const string main = """
            module app::main;

            command scan {
                help "Scan a path.";
                argument input: FilePath help "Path to scan.";
                option label: Text = "ok" help "Output label.";
                option limit: i32 = 1 help "Scan limit.";
                option base: FilePath = "." help "Base directory.";
                flag recursive help "Scan recursively.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string handlers = """
            module handlers;

            pub union ScanError { Failed }

            pub fn run(args: self::app::main::ScanArgs) -> Result<Text, self::handlers::ScanError> effects {} {
                if args.label == "fail" { return Err(self::handlers::ScanError.Failed); }
                if args.label == "fault" {
                    let overflow: i32 = args.limit + 2147483647;
                    return Ok(args.label);
                }
                return Ok(args.label);
            }

            pub fn describe(error: self::handlers::ScanError) -> Text effects {} {
                return match error { self::handlers::ScanError.Failed => "scan failed" };
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "typed-cli-runtime",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = main,
                ["src/handlers.hob"] = handlers
            });

        var check = await harness.InvokePackageDirectoryAsync("typed-cli-check", packageRoot, "check");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(string.Empty, check.StandardError, Describe(check));

        var firstBuild = await harness.InvokePackageDirectoryAsync("typed-cli-build", packageRoot, "build");
        AssertEqual(0, firstBuild.ExitCode, Describe(firstBuild));
        AssertTrue(firstBuild.StandardOutput.StartsWith("Built executable: ", StringComparison.Ordinal), Describe(firstBuild));
        var artifact = firstBuild.StandardOutput["Built executable: ".Length..].Trim();
        AssertTrue(Path.IsPathFullyQualified(artifact) && File.Exists(artifact),
            $"Expected the managed command executable at {artifact}. {Describe(firstBuild)}");
        var schemaPath = Path.Combine(Path.GetDirectoryName(artifact)!, "command-schema.json");
        AssertTrue(File.Exists(schemaPath), $"Expected command schema beside the executable: {schemaPath}");
        using var firstReceipt = await AssertBuildReceiptAsync(
            Path.GetDirectoryName(artifact)!,
            "managed",
            expectedRuntimeIdentifier: null,
            [Path.GetRelativePath(Path.GetDirectoryName(artifact)!, artifact).Replace(Path.DirectorySeparatorChar, '/'), "command-schema.json"],
            packageRoot);
        AssertEqual(1, firstReceipt.RootElement.GetProperty("package_graph").GetArrayLength(),
            "A root-only package receipt should contain one root package identity.");
        var cliClaims = firstReceipt.RootElement.GetProperty("trusted_components").EnumerateArray()
            .Where(claim => claim.GetProperty("source").GetString() == "trusted_host")
            .ToArray();
        AssertTrue(new[] { "cli.argument_decode", "cli.output" }.All(operation => cliClaims.Any(claim =>
                claim.GetProperty("operation").GetString() == operation
                && claim.GetProperty("assurance").GetString() == "claim_only"
                && claim.GetProperty("effects").GetArrayLength() == 0
                && claim.GetProperty("reachable_from").GetArrayLength() == 1
                && claim.GetProperty("reachable_from")[0].GetProperty("name").GetString() == "run")),
            "Typed CLI receipts should distinguish argument decoding and output as claim-only host components reachable from the command handler.");
        var firstSchemaBytes = await File.ReadAllBytesAsync(schemaPath);
        AssertTrue(firstSchemaBytes.Length > 0 && firstSchemaBytes[^1] == (byte)'\n'
            && !firstSchemaBytes.Contains((byte)'\r'), "Command schema must be UTF-8 JSON with LF line endings and a final newline.");
        using (var schemaDocument = JsonDocument.Parse(firstSchemaBytes))
        {
            var root = schemaDocument.RootElement;
            AssertEqual(5, root.GetProperty("schema_version").GetInt32(), "The command schema must use version 5 with ProcessRunner capability metadata.");
            var commands = root.GetProperty("commands");
            AssertEqual(1, commands.GetArrayLength(), "The entry module should define one command.");
            var command = commands[0];
            AssertEqual("scan", command.GetProperty("name").GetString(), "Schema command name mismatch.");
            AssertEqual("Scan a path.", command.GetProperty("help").GetString(), "Schema command help mismatch.");
            AssertEqual("self::handlers::run", command.GetProperty("handler").GetString(), "Schema handler must preserve its qualified reference.");
            AssertEqual("self::handlers::describe", command.GetProperty("error_formatter").GetString(), "Schema error formatter must preserve its qualified reference.");

            var arguments = command.GetProperty("arguments");
            AssertEqual(1, arguments.GetArrayLength(), "Positional argument schema count mismatch.");
            AssertEqual("input", arguments[0].GetProperty("name").GetString(), "Positional argument order/name mismatch.");
            AssertEqual("FilePath", arguments[0].GetProperty("type").GetString(), "FilePath must remain explicit in the schema.");
            AssertEqual("Path to scan.", arguments[0].GetProperty("help").GetString(), "Argument help mismatch.");

            var options = command.GetProperty("options");
            AssertEqual(3, options.GetArrayLength(), "Options must retain source declaration order.");
            AssertEqual("label", options[0].GetProperty("name").GetString(), "First option name/order mismatch.");
            AssertEqual("Text", options[0].GetProperty("type").GetString(), "Text option type mismatch.");
            AssertEqual("ok", options[0].GetProperty("default").GetString(), "Text option default mismatch.");
            AssertEqual("limit", options[1].GetProperty("name").GetString(), "Second option name/order mismatch.");
            AssertEqual("i32", options[1].GetProperty("type").GetString(), "i32 option type mismatch.");
            AssertEqual(1, options[1].GetProperty("default").GetInt32(), "i32 option default mismatch.");
            AssertEqual("base", options[2].GetProperty("name").GetString(), "Third option name/order mismatch.");
            AssertEqual("FilePath", options[2].GetProperty("type").GetString(), "FilePath option type mismatch.");
            AssertEqual(".", options[2].GetProperty("default").GetString(), "FilePath option default mismatch.");

            var flags = command.GetProperty("flags");
            AssertEqual(1, flags.GetArrayLength(), "Flag schema count mismatch.");
            AssertEqual("recursive", flags[0].GetProperty("name").GetString(), "Flag schema mismatch.");
            AssertEqual("bool", flags[0].GetProperty("type").GetString(), "Flags must have boolean schema types.");
            AssertEqual(0, command.GetProperty("capabilities").GetArrayLength(),
                "An args-only handler should require no injected capabilities.");
        }

        var secondBuild = await harness.InvokePackageDirectoryAsync("typed-cli-build-repeat", packageRoot, "build");
        AssertEqual(0, secondBuild.ExitCode, Describe(secondBuild));
        var secondArtifact = secondBuild.StandardOutput["Built executable: ".Length..].Trim();
        var secondSchemaPath = Path.Combine(Path.GetDirectoryName(secondArtifact)!, "command-schema.json");
        AssertTrue(File.Exists(secondSchemaPath), $"Repeated build did not emit {secondSchemaPath}.");
        var secondSchemaBytes = await File.ReadAllBytesAsync(secondSchemaPath);
        AssertTrue(firstSchemaBytes.SequenceEqual(secondSchemaBytes),
            "Equivalent package builds must emit byte-for-byte deterministic command schemas.");

        var topHelp = await harness.InvokePackageDirectoryAsync("typed-cli-top-help", packageRoot, "run", "--", "--help");
        AssertEqual(0, topHelp.ExitCode, Describe(topHelp));
        AssertTrue(topHelp.StandardOutput.Contains("scan", StringComparison.Ordinal)
            && topHelp.StandardOutput.Contains("Scan a path.", StringComparison.Ordinal), Describe(topHelp));
        var commandHelp = await harness.InvokePackageDirectoryAsync("typed-cli-command-help", packageRoot, "run", "--", "scan", "--help");
        AssertEqual(0, commandHelp.ExitCode, Describe(commandHelp));
        AssertTrue(commandHelp.StandardOutput.Contains("input", StringComparison.Ordinal)
            && commandHelp.StandardOutput.Contains("--limit", StringComparison.Ordinal)
            && commandHelp.StandardOutput.Contains("--recursive", StringComparison.Ordinal), Describe(commandHelp));

        var success = await harness.InvokePackageDirectoryAsync(
            "typed-cli-success", packageRoot, "run", "--", "scan", "sample.txt", "--label", "ready", "--limit", "7", "--recursive");
        AssertRunOutput("ready" + Environment.NewLine, success);
        var defaults = await harness.InvokePackageDirectoryAsync(
            "typed-cli-defaults", packageRoot, "run", "--", "scan", "sample.txt");
        AssertRunOutput("ok" + Environment.NewLine, defaults);
        var leadingDash = await harness.InvokePackageDirectoryAsync(
            "typed-cli-leading-dash", packageRoot, "run", "--", "scan", "--", "--leading");
        AssertRunOutput("ok" + Environment.NewLine, leadingDash);

        var formattedError = await harness.InvokePackageDirectoryAsync(
            "typed-cli-formatted-error", packageRoot, "run", "--", "scan", "sample.txt", "--label", "fail");
        AssertEqual(3, formattedError.ExitCode, Describe(formattedError));
        AssertEqual(string.Empty, formattedError.StandardOutput, Describe(formattedError));
        AssertEqual("scan failed" + Environment.NewLine, formattedError.StandardError, Describe(formattedError));

        var runtimeFault = await harness.InvokePackageDirectoryAsync(
            "typed-cli-runtime-fault", packageRoot, "run", "--", "scan", "sample.txt", "--label", "fault");
        AssertEqual(70, runtimeFault.ExitCode, Describe(runtimeFault));
        AssertEqual(string.Empty, runtimeFault.StandardOutput, Describe(runtimeFault));
        AssertEqual("Runtime fault" + Environment.NewLine, runtimeFault.StandardError, Describe(runtimeFault));

        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync("typed-cli-unknown-command", packageRoot, "run", "--", "bogus"),
            "CLI_UNKNOWN_COMMAND");
        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync("typed-cli-unknown-option", packageRoot, "run", "--", "scan", "sample.txt", "--unknown"),
            "CLI_UNKNOWN_OPTION");
        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync("typed-cli-missing-argument", packageRoot, "run", "--", "scan"),
            "CLI_MISSING_ARGUMENT");
        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync("typed-cli-missing-value", packageRoot, "run", "--", "scan", "sample.txt", "--limit"),
            "CLI_MISSING_VALUE");
        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync("typed-cli-duplicate-option", packageRoot, "run", "--", "scan", "sample.txt", "--limit", "1", "--limit", "2"),
            "CLI_DUPLICATE_OPTION");
        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync("typed-cli-invalid-i32", packageRoot, "run", "--", "scan", "sample.txt", "--limit", "one"),
            "CLI_INVALID_VALUE");
        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync("typed-cli-invalid-filepath", packageRoot, "run", "--", "scan", string.Empty),
            "CLI_INVALID_VALUE");

        var escapedSubject = await harness.InvokePackageDirectoryAsync(
            "typed-cli-escaped-diagnostic", packageRoot, "run", "--", "unknown\n\tforged");
        AssertEqual(2, escapedSubject.ExitCode, Describe(escapedSubject));
        AssertEqual(string.Empty, escapedSubject.StandardOutput, Describe(escapedSubject));
        AssertEqual("CLI_UNKNOWN_COMMAND: \"unknown\\n\\tforged\"" + Environment.NewLine, escapedSubject.StandardError,
            "Control characters in CLI diagnostic subjects must be escaped on one physical stderr line.");
        AssertEqual(2, Directory.EnumerateFiles(Path.Combine(packageRoot, "out"), "build-receipt.json", SearchOption.AllDirectories).Count(),
            "Typed CLI run commands should not add build receipts after the two explicit builds.");
    }

    private static async Task TestAsyncCliCommandRuntime(Harness harness)
    {
        const string main = """
            module app::main;

            command read {
                help "Read a UTF-8 text file.";
                argument input: FilePath help "Path to read.";
                handler: self::handlers::read_file;
                error: self::handlers::describe;
            }
            """;
        const string handlers = """
            module handlers;

            pub async fn read_file(
                args: self::app::main::ReadArgs,
                fs: FsRead,
            ) -> Result<Text, FsError> effects { fs.read } {
                return await fs.read_text_async(args.input);
            }

            pub fn describe(error: FsError) -> Text effects {} {
                return match error {
                    FsError.NotFound => "missing",
                    FsError.PermissionDenied => "permission denied",
                    FsError.InvalidPath => "invalid path",
                    FsError.InvalidText => "invalid text",
                    FsError.Io => "I/O error",
                };
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "async-cli-runtime",
            CliPackageManifest() + "\n[capabilities]\nfs.read = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = main,
                ["src/handlers.hob"] = handlers
            });

        var check = await harness.InvokePackageDirectoryAsync("async-cli-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var build = await harness.InvokePackageDirectoryAsync("async-cli-build", packageRoot, "build");
        var executable = ParseBuiltArtifact(build, "Built executable: ");
        var outputDirectory = Path.GetDirectoryName(executable)!;
        var schemaPath = Path.Combine(outputDirectory, "command-schema.json");
        AssertTrue(File.Exists(schemaPath), $"Expected async CLI command schema beside the executable: {schemaPath}");
        using var receipt = await AssertBuildReceiptAsync(
            outputDirectory,
            "managed",
            expectedRuntimeIdentifier: null,
            [Path.GetRelativePath(outputDirectory, executable).Replace(Path.DirectorySeparatorChar, '/'), "command-schema.json"],
            packageRoot);
        AssertEqual(4, receipt.RootElement.GetProperty("schema_version").GetInt32(),
            "Async managed CLI builds must use build receipt schema version 4.");

        using (var schema = JsonDocument.Parse(await File.ReadAllBytesAsync(schemaPath)))
        {
            var root = schema.RootElement;
            AssertEqual(5, root.GetProperty("schema_version").GetInt32(),
                "Async handlers must preserve command schema version 5.");
            var command = root.GetProperty("commands").EnumerateArray().Single();
            AssertEqual("read", command.GetProperty("name").GetString(),
                "The async command schema should retain its declared name.");
            AssertEqual("self::handlers::read_file", command.GetProperty("handler").GetString(),
                "The async command schema should retain the handler reference.");
            AssertJsonStringArray(command.GetProperty("capabilities"), ["fs.read"]);
        }

        var temporaryDirectory = Path.Combine(harness.TemporaryRoot, $"async-cli-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var validPath = Path.Combine(temporaryDirectory, "valid.txt");
            var invalidPath = Path.Combine(temporaryDirectory, "invalid-utf8.txt");
            const string expectedText = "async command λ 😀";
            await File.WriteAllTextAsync(validPath, expectedText, new UTF8Encoding(false, true));
            await File.WriteAllBytesAsync(invalidPath, [0xC3, 0x28]);

            var success = await harness.InvokePackageDirectoryAsync(
                "async-cli-success", packageRoot, "run", "--", "read", validPath);
            AssertRunOutput(expectedText + Environment.NewLine, success);

            var invalidText = await harness.InvokePackageDirectoryAsync(
                "async-cli-invalid-utf8", packageRoot, "run", "--", "read", invalidPath);
            AssertEqual(3, invalidText.ExitCode, Describe(invalidText));
            AssertEqual(string.Empty, invalidText.StandardOutput, Describe(invalidText));
            AssertEqual("invalid text" + Environment.NewLine, invalidText.StandardError,
                "The async CLI should pass FsError.InvalidText to its formatter and preserve CLI failure output.");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static async Task TestFsWritePackageContracts(Harness harness)
    {
        const string commandSource = """
            module app::main;
            command save {
                help "Write and read a file.";
                argument output: FilePath help "Destination path.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string handlerSource = """
            module handlers;
            pub union SaveError { Failed }

            pub fn run(args: self::app::main::SaveArgs, fs: FsRead, writer: FsWrite) -> Result<Text, self::handlers::SaveError> effects { fs.read, fs.write } {
                let written: Result<bool, FsError> = writer.write_text(args.output, "roundtrip λ");
                return match fs.read_text(args.output) {
                    Ok(text) => Ok(text),
                    Err(error) => Err(self::handlers::SaveError.Failed)
                };
            }

            pub fn describe(error: self::handlers::SaveError) -> Text effects {} {
                return match error { self::handlers::SaveError.Failed => "save failed" };
            }
            """;
        const string bothGrants = "\n[capabilities]\nfs.read = \"allow\"\nfs.write = \"allow\"\n";
        var commandSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = commandSource,
            ["src/handlers.hob"] = handlerSource
        };
        var packageRoot = await harness.WritePackageAsync(
            "fs-write-cli",
            CliPackageManifest() + bothGrants,
            commandSources);

        var check = await harness.InvokePackageDirectoryAsync("fs-write-cli-check", packageRoot, "check");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(string.Empty, check.StandardError, Describe(check));

        var artifactBuild = await harness.InvokePackageDirectoryAsync("fs-write-cli-build", packageRoot, "build");
        AssertEqual(0, artifactBuild.ExitCode, Describe(artifactBuild));
        var artifact = ParseBuiltArtifact(artifactBuild, "Built executable: ");
        var outputPath = Path.Combine(harness.TemporaryRoot, "fs-write-cli-output.txt");
        var run = await harness.InvokePackageDirectoryAsync("fs-write-cli-run", packageRoot, "run", "--", "save", outputPath);
        AssertRunOutput("roundtrip λ" + Environment.NewLine, run);
        var expectedOutputBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetBytes("roundtrip λ");
        AssertTrue(File.ReadAllBytes(outputPath).AsSpan().SequenceEqual(expectedOutputBytes),
            "The FsRead/FsWrite CLI handler should round-trip exact strict UTF-8 bytes without a BOM.");

        var schemaPath = Path.Combine(Path.GetDirectoryName(artifact)!, "command-schema.json");
        var schemaBytes = await File.ReadAllBytesAsync(schemaPath);
        AssertTrue(schemaBytes.Length > 0 && schemaBytes[^1] == (byte)'\n' && !schemaBytes.Contains((byte)'\r'),
            "The FsWrite command schema should remain UTF-8 JSON with LF line endings and a final newline.");
        using (var schemaDocument = JsonDocument.Parse(schemaBytes))
        {
            var schema = schemaDocument.RootElement;
            AssertJsonPropertyOrder(schema, "schema_version,commands");
            AssertEqual(5, schema.GetProperty("schema_version").GetInt32(),
                "ProcessRunner command metadata must use the current schema version 5.");
            var command = schema.GetProperty("commands").EnumerateArray().Single();
            AssertJsonPropertyOrder(command, "name,help,handler,error_formatter,capabilities,arguments,options,flags");
            AssertEqual("save", command.GetProperty("name").GetString(), "The command schema should retain its declared name.");
            AssertJsonStringArray(command.GetProperty("capabilities"), ["fs.read", "fs.write"]);
        }

        using (var receipt = await AssertBuildReceiptAsync(
                   Path.GetDirectoryName(artifact)!,
                   "managed",
                   expectedRuntimeIdentifier: null,
                   [Path.GetRelativePath(Path.GetDirectoryName(artifact)!, artifact).Replace(Path.DirectorySeparatorChar, '/'), "command-schema.json"],
                   packageRoot))
        {
            var root = receipt.RootElement;
            AssertJsonStringArray(root.GetProperty("manifest_grants"), ["fs.read", "fs.write"]);
            var writeClaim = root.GetProperty("trusted_components").EnumerateArray()
                .Single(claim => claim.GetProperty("operation").GetString() == "FsWrite.write_text");
            AssertFsWriteClaim(writeClaim, "handlers", "run");
        }

        var inspectEffects = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::handlers::run", "--json");
        AssertEqual(0, inspectEffects.ExitCode, Describe(inspectEffects));
        using (var reportDocument = JsonDocument.Parse(inspectEffects.StandardOutput))
        {
            var report = reportDocument.RootElement;
            AssertJsonStringArray(report.GetProperty("declared_effects"), ["fs.read", "fs.write"]);
            AssertJsonStringArray(report.GetProperty("inferred_effects"), ["fs.read", "fs.write"]);
            AssertJsonStringArray(report.GetProperty("required_capabilities"), ["fs.read", "fs.write"]);
            AssertJsonStringArray(report.GetProperty("manifest_grants"), ["fs.read", "fs.write"]);
            var writePath = report.GetProperty("effect_paths").EnumerateArray()
                .Single(path => path.GetProperty("effect").GetString() == "fs.write");
            AssertEqual("handlers::run -> fs.write_text",
                string.Join(" -> ", writePath.GetProperty("steps").EnumerateArray().Select(step => step.GetString())),
                "The FsWrite effect report should retain the direct path to fs.write_text.");
            var writeOperation = report.GetProperty("trusted_operations").EnumerateArray()
                .Single(operation => operation.GetProperty("operation").GetString() == "FsWrite.write_text");
            AssertEqual("trusted_adapter", writeOperation.GetProperty("trust").GetString(),
                "inspect-effects should classify FsWrite.write_text as a trusted adapter.");
            AssertJsonStringArray(writeOperation.GetProperty("effects"), ["fs.write"]);
        }

        var inspectApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, inspectApi.ExitCode, Describe(inspectApi));
        using (var apiDocument = JsonDocument.Parse(inspectApi.StandardOutput))
        {
            var api = apiDocument.RootElement;
            var command = api.GetProperty("commands").EnumerateArray().Single();
            AssertJsonStringArray(command.GetProperty("required_capabilities"), ["fs.read", "fs.write"]);
            var handler = api.GetProperty("functions").EnumerateArray()
                .Single(function => function.GetProperty("id").GetString() == "self::handlers::run");
            AssertJsonStringArray(handler.GetProperty("required_capabilities"), ["fs.read", "fs.write"]);
        }

        var audit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, audit.ExitCode, Describe(audit));
        using (var auditDocument = JsonDocument.Parse(audit.StandardOutput))
        {
            var report = auditDocument.RootElement;
            AssertJsonStringArray(report.GetProperty("manifest_grants"), ["fs.read", "fs.write"]);
            var writeClaim = report.GetProperty("trusted_claims").EnumerateArray()
                .Single(claim => claim.GetProperty("operation").GetString() == "FsWrite.write_text");
            AssertFsWriteClaim(writeClaim, "handlers", "run");
            var handler = report.GetProperty("compiler").GetProperty("functions").EnumerateArray()
                .Single(function => function.GetProperty("name").GetString() == "run");
            AssertJsonStringArray(handler.GetProperty("required_capabilities"), ["fs.read", "fs.write"]);
        }

        async Task AssertCommandHandlerError(string caseName, string signature, bool replaceBody = false)
        {
            var handler = replaceBody
                ? handlerSource.Replace(
                    "args: self::app::main::SaveArgs, fs: FsRead, writer: FsWrite",
                    signature,
                    StringComparison.Ordinal)
                    .Replace(
                        "let written: Result<bool, FsError> = writer.write_text(args.output, \"roundtrip λ\");\n    return match fs.read_text(args.output) {\n        Ok(text) => Ok(text),\n        Err(error) => Err(self::handlers::SaveError.Failed)\n    };",
                        "return Ok(\"unused\");",
                        StringComparison.Ordinal)
                : handlerSource.Replace(
                    "args: self::app::main::SaveArgs, fs: FsRead, writer: FsWrite",
                    signature,
                    StringComparison.Ordinal);
            var invalidPackage = await harness.WritePackageAsync(
                caseName,
                CliPackageManifest() + bothGrants,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = commandSource,
                    ["src/handlers.hob"] = handler
                });
            var result = await harness.InvokePackageDirectoryAsync(caseName, invalidPackage, "check", "--json");
            AssertTrue(result.ExitCode != 0, $"Invalid CLI capability signature unexpectedly checked. {Describe(result)}");
            var diagnostics = ParseDiagnosticSnapshots(result.StandardOutput);
            AssertTrue(diagnostics.Any(diagnostic => diagnostic.Code == "E_COMMAND_HANDLER"),
                $"Expected E_COMMAND_HANDLER for signature '{signature}'. {Describe(result)}");
        }

        await AssertCommandHandlerError(
            "fs-write-cli-reversed-capabilities",
            "args: self::app::main::SaveArgs, writer: FsWrite, fs: FsRead");
        await AssertCommandHandlerError(
            "fs-write-cli-duplicate-capability",
            "args: self::app::main::SaveArgs, fs: FsRead, second: FsRead, writer: FsWrite");
        await AssertCommandHandlerError(
            "fs-write-cli-wrong-capability-type",
            "args: self::app::main::SaveArgs, fs: FsRead, writer: Text, capability: FsWrite",
            replaceBody: true);

        var missingGrantPackage = await harness.WritePackageAsync(
            "fs-write-cli-missing-grant",
            CliPackageManifest() + "\n[capabilities]\nfs.read = \"allow\"\n",
            commandSources);
        var missingGrant = await harness.InvokePackageDirectoryAsync(
            "fs-write-cli-missing-grant-check", missingGrantPackage, "check", "--json");
        AssertTrue(missingGrant.ExitCode != 0, Describe(missingGrant));
        var missingGrantDiagnostics = ParseDiagnosticSnapshots(missingGrant.StandardOutput);
        AssertEqual(1, missingGrantDiagnostics.Length, Describe(missingGrant));
        AssertEqual("E_CAPABILITY_MISSING", missingGrantDiagnostics[0].Code, Describe(missingGrant));
        AssertEqual("Command handler requires the root package's fs.write capability grant",
            missingGrantDiagnostics[0].Message, "The missing FsWrite grant should have the stable command capability diagnostic.");

        const string webSource = """
            module app::main;
            pub union Reply { Written }
            pub fn write(fs: FsWrite, reader: DbRead, writer: DbWrite) -> self::app::main::Reply effects { fs.write } {
                let saved: Result<bool, FsError> = fs.write_text("route-output.txt", "written");
                return self::app::main::Reply.Written;
            }
            route GET "/write" {
                handler: self::app::main::write;
                response Written: 204;
            }
            """;
        const string webManifest = "name = \"fs-write-web\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
            + "sqlite_path = \"data/web.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
            + "\n[capabilities]\nnet.listen = \"allow\"\nfs.write = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n";
        var webPackage = await harness.WritePackageAsync(
            "fs-write-web-api",
            webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = webSource,
                ["db/schema.sql"] = "CREATE TABLE web_rows (id INTEGER PRIMARY KEY);\n"
            });
        var webApiResult = await harness.InvokeCompilerCommandAsync("inspect", "api", webPackage, "--json");
        AssertEqual(0, webApiResult.ExitCode, Describe(webApiResult));
        using (var webApiDocument = JsonDocument.Parse(webApiResult.StandardOutput))
        {
            var route = webApiDocument.RootElement.GetProperty("routes").EnumerateArray().Single();
            AssertJsonStringArray(route.GetProperty("required_capabilities"), ["db.read", "db.write", "fs.write"]);
            var parameters = route.GetProperty("capability_parameters").EnumerateArray().ToArray();
            AssertEqual("fs.write", parameters[0].GetProperty("capability").GetString(), "FsWrite must remain first in route parameter order.");
            AssertEqual("db.read", parameters[1].GetProperty("capability").GetString(), "DbRead must remain second in route parameter order.");
            AssertEqual("db.write", parameters[2].GetProperty("capability").GetString(), "DbWrite must remain third in route parameter order.");
        }

        var reversedWebPackage = await harness.WritePackageAsync(
            "fs-write-web-reversed-capabilities",
            webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = webSource.Replace(
                    "fs: FsWrite, reader: DbRead, writer: DbWrite",
                    "reader: DbRead, fs: FsWrite, writer: DbWrite",
                    StringComparison.Ordinal),
                ["db/schema.sql"] = "CREATE TABLE web_rows (id INTEGER PRIMARY KEY);\n"
            });
        var reversedWeb = await harness.InvokePackageDirectoryAsync(
            "fs-write-web-reversed-check", reversedWebPackage, "check", "--json");
        AssertTrue(reversedWeb.ExitCode != 0, Describe(reversedWeb));
        AssertTrue(ParseDiagnosticSnapshots(reversedWeb.StandardOutput).Any(diagnostic => diagnostic.Code == "E_ROUTE_HANDLER"),
            $"A DbRead-before-FsWrite route signature should be rejected. {Describe(reversedWeb)}");

        var webWithoutFsWriteGrant = await harness.WritePackageAsync(
            "fs-write-web-missing-grant",
            webManifest.Replace("fs.write = \"allow\"\n", string.Empty, StringComparison.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = webSource,
                ["db/schema.sql"] = "CREATE TABLE web_rows (id INTEGER PRIMARY KEY);\n"
            });
        var missingWebGrant = await harness.InvokePackageDirectoryAsync(
            "fs-write-web-missing-grant-check", webWithoutFsWriteGrant, "check", "--json");
        AssertTrue(missingWebGrant.ExitCode != 0, Describe(missingWebGrant));
        var webGrantDiagnostics = ParseDiagnosticSnapshots(missingWebGrant.StandardOutput);
        AssertTrue(webGrantDiagnostics.Any(diagnostic => diagnostic.Code == "E_CAPABILITY_MISSING"
            && diagnostic.Message == "Route handler requires the root package's fs.write capability grant"),
            $"A route's missing FsWrite grant should have the stable capability diagnostic. {Describe(missingWebGrant)}");

        static void AssertFsWriteClaim(JsonElement claim, string module, string function)
        {
            AssertEqual("trusted_adapter", claim.GetProperty("source").GetString(),
                "FsWrite.write_text should remain a trusted adapter claim.");
            AssertEqual("claim_only", claim.GetProperty("assurance").GetString(),
                "FsWrite.write_text should remain claim-only in generated evidence.");
            AssertJsonStringArray(claim.GetProperty("effects"), ["fs.write"]);
            var reachable = claim.GetProperty("reachable_from").EnumerateArray().Single();
            AssertEqual(module, reachable.GetProperty("module").GetString(),
                "The FsWrite claim should be reachable from the checked handler module.");
            AssertEqual(function, reachable.GetProperty("name").GetString(),
                "The FsWrite claim should be reachable from the checked handler function.");
        }
    }

    private static async Task TestConfigManifestAndReports(Harness harness)
    {
        var invalidManifests = new (string Name, string Manifest)[]
        {
            ("config-invalid-syntax", CliPackageManifest()
                + "\n[config\nname = \"Text|required\"\n"),
            ("config-after-capabilities", CliPackageManifest()
                + "\n[capabilities]\nenv.read = \"allow\"\n[config]\nname = \"Text|required\"\n"),
            ("config-unknown-subsection", CliPackageManifest()
                + "\n[config.name]\nvalue = \"Text|required\"\n"),
            ("config-duplicate-field", CliPackageManifest()
                + "\n[config]\nname = \"Text|required\"\nname = \"Text|default:other\"\n"),
            ("config-invalid-name", CliPackageManifest()
                + "\n[config]\n\"api-token\" = \"Secret<Text>|required\"\n"),
            ("config-invalid-descriptor", CliPackageManifest()
                + "\n[config]\nname = \"Text|optional\"\n"),
            ("config-secret-default", CliPackageManifest()
                + "\n[config]\ntoken = \"Secret<Text>|default:unsafe\"\n"),
            ("config-library-section", LibraryPackageManifest("config-library")
                + "\n[config]\nname = \"Text|required\"\n")
        };
        foreach (var (name, manifest) in invalidManifests)
        {
            await ExpectPackageJsonDiagnosticAsync(
                harness,
                name,
                manifest,
                new Dictionary<string, string>(StringComparer.Ordinal),
                "E_MANIFEST",
                "hob.toml");
        }

        var configSourceHeader = "module app::main;\n"
            + "pub fn main() -> i32 effects {} { return 0; }\n"
            + "fn probe(config: Config, key: Text) -> Text effects { env.read } { return ";
        var configKeyCases = new (string Name, string Expression, string Code)[]
        {
            ("config-unknown-key", "config.get_text(\"missing\")", "E_CONFIG_KEY"),
            ("config-wrong-field-kind", "config.get_text(\"token\")", "E_CONFIG_TYPE"),
            ("config-wrong-secret-field-kind", "config.get_secret_text(\"name\")", "E_CONFIG_TYPE"),
            ("config-nonliteral-key", "config.get_text(key)", "E_CONFIG_KEY")
        };
        foreach (var (name, expression, code) in configKeyCases)
        {
            var packageRoot = await harness.WritePackageAsync(
                name,
                ConfigCliManifest(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = configSourceHeader + expression + "; }\n"
                });
            var result = await harness.InvokePackageDirectoryAsync(name, packageRoot, "check", "--json");
            AssertTrue(result.ExitCode != 0, $"Invalid config lookup unexpectedly succeeded. {Describe(result)}");
            AssertTrue(ParseDiagnosticSnapshots(result.StandardOutput).Any(diagnostic => diagnostic.Code == code),
                $"Expected {code} for config lookup {expression}. {Describe(result)}");
        }

        const string secretCanary = "secret-canary-config-diagnostic-8f31d4";
        var diagnosticPackage = await harness.WritePackageAsync(
            "config-diagnostic-redaction",
            ConfigCliManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = configSourceHeader + "config.get_text(\"missing\"); }\n"
            });
        var diagnostic = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "config-diagnostic-redaction",
            diagnosticPackage,
            "check",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HOB_CONFIG_TOKEN"] = secretCanary
            },
            "--json");
        AssertTrue(!diagnostic.StandardOutput.Contains(secretCanary, StringComparison.Ordinal)
            && !diagnostic.StandardError.Contains(secretCanary, StringComparison.Ordinal),
            "Compile diagnostics must not include a runtime secret value from HOB_CONFIG_TOKEN.");

        var wrongOrderSource = Regex.Replace(
            ConfigCliSource,
            @"config: Config,\s+secrets: Secrets,\s+logger: Logger",
            "logger: Logger,\n            config: Config,\n            secrets: Secrets",
            RegexOptions.CultureInvariant);
        AssertTrue(wrongOrderSource != ConfigCliSource,
            "The wrong-order fixture must actually change the command handler signature.");
        var wrongOrderPackage = await harness.WritePackageAsync(
            "config-cli-wrong-capability-order",
            ConfigCliManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = wrongOrderSource
            });
        var wrongOrder = await harness.InvokePackageDirectoryAsync(
            "config-cli-wrong-capability-order",
            wrongOrderPackage,
            "check",
            "--json");
        AssertTrue(wrongOrder.ExitCode != 0, Describe(wrongOrder));
        AssertTrue(ParseDiagnosticSnapshots(wrongOrder.StandardOutput)
                .Any(diagnostic => diagnostic.Code == "E_COMMAND_HANDLER"),
            $"CLI capability parameters must place Config, Secrets, and Logger in declaration order. {Describe(wrongOrder)}");

        foreach (var grant in new[] { "env.read", "secret.reveal", "log.write" })
        {
            var manifest = ConfigCliManifest().Replace($"{grant} = \"allow\"\n", string.Empty, StringComparison.Ordinal);
            var packageRoot = await harness.WritePackageAsync(
                $"config-cli-missing-{grant.Replace('.', '-')}",
                manifest,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = ConfigCliSource
                });
            var result = await harness.InvokePackageDirectoryAsync(
                $"config-cli-missing-{grant.Replace('.', '-')}", packageRoot, "check", "--json");
            AssertTrue(result.ExitCode != 0, Describe(result));
            AssertTrue(ParseDiagnosticSnapshots(result.StandardOutput)
                    .Any(diagnostic => diagnostic.Code == "E_CAPABILITY_MISSING"),
                $"Config feature use without the {grant} grant should fail capability checking. {Describe(result)}");
        }

        var libraryPackage = await harness.WritePackageGraphAsync(
            "config-library-type-scope",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\nconfig_library = \"../config-library\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 0; }\n"
                    }),
                ["config-library"] = new PackageFixture(
                    LibraryPackageManifest("config-library-type-scope"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/config.hob"] = "module config;\n"
                            + "pub fn read(config: Config) -> Text effects { env.read } { return \"hidden\"; }\n"
                            + "pub fn expose(secret: Secret<Text>) -> Secret<Text> effects {} { return secret; }\n"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "config-library-type-scope-lock", libraryPackage, "lock"));
        var libraryCheck = await harness.InvokePackageDirectoryAsync(
            "config-library-type-scope", libraryPackage, "check", "--json");
        AssertTrue(libraryCheck.ExitCode != 0, Describe(libraryCheck));
        AssertTrue(ParseDiagnosticSnapshots(libraryCheck.StandardOutput)
                .Any(diagnostic => diagnostic.Code == "E_CAPABILITY_SCOPE"),
            $"Libraries must not use or expose the config and secret types. {Describe(libraryCheck)}");

        static Dictionary<string, PackageFixture> MakeGraph()
        {
            var rootManifest = ConfigCliManifest().Replace(
                    "name = \"config-harness\"",
                    "name = \"config-report-root\"",
                    StringComparison.Ordinal)
                + "\n[dependencies]\nsupport = \"../support\"\n";
            return new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(rootManifest, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = ConfigCliSource
                }),
                ["support"] = new PackageFixture(
                    LibraryPackageManifest("config-report-support"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/support.hob"] = "module support; pub fn value() -> i32 effects {} { return 1; }\n"
                    })
            };
        }

        var firstRoot = await harness.WritePackageGraphAsync("config-report-first", MakeGraph());
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("config-report-first-lock", firstRoot, "lock"));
        var firstCheck = await harness.InvokePackageDirectoryAsync("config-report-first-check", firstRoot, "check", "--json");
        AssertEqual(0, firstCheck.ExitCode, Describe(firstCheck));

        const string reportCanary = "secret-canary-config-report-2d7ce5";
        var reportEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_CONFIG_TOKEN"] = reportCanary
        };
        var api = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            reportEnvironment, "inspect", "api", firstRoot, "--json");
        AssertEqual(0, api.ExitCode, Describe(api));
        using (var apiDocument = ParseConfigJson(api.StandardOutput, "inspect api"))
        {
            var root = apiDocument.RootElement;
            AssertInspectApiPropertyOrder(root);
            AssertEqual(13, root.GetProperty("schema_version").GetInt32(), "Inspect-api schema version must be 13.");
            AssertConfigFieldProjection(root.GetProperty("config"));
            AssertJsonStringArray(root.GetProperty("manifest_grants"), ["env.read", "log.write", "secret.reveal"]);
            AssertApiPortable(api.StandardOutput, root, harness.TemporaryRoot);
        }

        var audit = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            reportEnvironment, "audit", firstRoot, "--json");
        AssertEqual(0, audit.ExitCode, Describe(audit));
        using (var auditDocument = ParseConfigJson(audit.StandardOutput, "audit"))
        {
            var root = auditDocument.RootElement;
            AssertAuditPropertyOrder(root);
            AssertEqual(11, root.GetProperty("schema_version").GetInt32(), "Audit schema version must be 11.");
            AssertConfigFieldProjection(root.GetProperty("config"));
            AssertJsonStringArray(root.GetProperty("manifest_grants"), ["env.read", "log.write", "secret.reveal"]);
        }

        var effects = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            reportEnvironment, "inspect", "effects", firstRoot, "self::app::main::run", "--json");
        AssertEqual(0, effects.ExitCode, Describe(effects));
        using (var effectsDocument = ParseConfigJson(effects.StandardOutput, "inspect effects"))
        {
            var root = effectsDocument.RootElement;
            AssertEqual(1, root.GetProperty("schema_version").GetInt32(),
                "Config and logger effects should preserve inspect-effects schema version 1.");
            AssertJsonStringArray(root.GetProperty("inferred_effects"), ["env.read", "log.write", "secret.reveal"]);
            AssertJsonStringArray(root.GetProperty("required_capabilities"), ["env.read", "log.write", "secret.reveal"]);
            AssertTrue(root.GetProperty("effect_paths").EnumerateArray()
                    .Any(path => path.GetProperty("effect").GetString() == "env.read"
                        && path.GetProperty("steps").EnumerateArray()
                            .Any(step => (step.GetString() ?? string.Empty).Contains("read_values", StringComparison.Ordinal))),
                "The command's env.read effect should retain its transitive path through read_values.");
            AssertTrue(root.GetProperty("effect_paths").EnumerateArray()
                    .Any(path => path.GetProperty("effect").GetString() == "secret.reveal"
                        && path.GetProperty("steps").EnumerateArray()
                            .Any(step => (step.GetString() ?? string.Empty).Contains("reveal_token", StringComparison.Ordinal))),
                "The command's secret.reveal effect should retain its transitive path through reveal_token.");
        }

        var build = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            reportEnvironment, "build", firstRoot);
        var executable = ParseBuiltArtifact(build, "Built executable: ");
        var outputDirectory = Path.GetDirectoryName(executable)!;
        var schemaPath = Path.Combine(outputDirectory, "command-schema.json");
        using (var receipt = await AssertBuildReceiptAsync(
                   outputDirectory,
                   "managed",
                   expectedRuntimeIdentifier: null,
                   [Path.GetRelativePath(outputDirectory, executable).Replace(Path.DirectorySeparatorChar, '/'), "command-schema.json"],
                   firstRoot))
        {
            AssertEqual(4, receipt.RootElement.GetProperty("schema_version").GetInt32(),
                "Config fields should use build receipt schema version 4.");
            AssertJsonStringArray(receipt.RootElement.GetProperty("manifest_grants"), ["env.read", "log.write", "secret.reveal"]);
        }
        var commandSchema = await File.ReadAllTextAsync(schemaPath);
        using (var schemaDocument = ParseConfigJson(commandSchema, "command schema"))
        {
            var root = schemaDocument.RootElement;
            AssertEqual(5, root.GetProperty("schema_version").GetInt32(),
                "Command schema must use version 5 for config, secret, and process capability metadata.");
            AssertJsonStringArray(root.GetProperty("commands")[0].GetProperty("capabilities"),
                ["env.read", "secret.reveal", "log.write"]);
        }

        foreach (var report in new[] { api.StandardOutput, audit.StandardOutput, effects.StandardOutput, commandSchema })
            AssertTrue(!report.Contains(reportCanary, StringComparison.Ordinal),
                "Inspect and command schema output must not contain runtime secret values.");
        AssertTrue(!api.StandardOutput.Contains("Text|default:normal", StringComparison.Ordinal)
            && !audit.StandardOutput.Contains("Text|default:normal", StringComparison.Ordinal)
            && !api.StandardOutput.Contains("\"normal\"", StringComparison.Ordinal)
            && !audit.StandardOutput.Contains("\"normal\"", StringComparison.Ordinal),
            "API and audit config metadata must omit descriptor strings and default literals.");
        await AssertNoTextInFilesAsync(outputDirectory, reportCanary);

        var relocatedRoot = await harness.WritePackageGraphAsync("config-report-relocated", MakeGraph());
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("config-report-relocated-lock", relocatedRoot, "lock"));
        var relocatedApi = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            reportEnvironment, "inspect", "api", relocatedRoot, "--json");
        var relocatedAudit = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            reportEnvironment, "audit", relocatedRoot, "--json");
        AssertEqual(0, relocatedApi.ExitCode, Describe(relocatedApi));
        AssertEqual(0, relocatedAudit.ExitCode, Describe(relocatedAudit));
        AssertEqual(api.StandardOutput, relocatedApi.StandardOutput,
            "Equivalent config package graphs at different roots must produce identical API JSON.");
        AssertEqual(audit.StandardOutput, relocatedAudit.StandardOutput,
            "Equivalent config package graphs at different roots must produce identical audit JSON.");

        var changedManifestPath = Path.Combine(firstRoot, "hob.toml");
        var changedManifest = (await File.ReadAllTextAsync(changedManifestPath))
            .Replace("mode = \"Text|default:normal\"", "mode = \"Text|default:changed\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(changedManifestPath, changedManifest);
        var staleLock = await harness.InvokePackageDirectoryAsync("config-report-stale-lock", firstRoot, "check", "--json");
        AssertTrue(staleLock.ExitCode != 0, Describe(staleLock));
        AssertTrue(ParseDiagnosticSnapshots(staleLock.StandardOutput)
                .Any(diagnostic => diagnostic.Code == "E_LOCK"),
            $"Changing a config descriptor must make the existing package lock stale. {Describe(staleLock)}");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("config-report-refresh-lock", firstRoot, "lock"));
        var refreshedCheck = await harness.InvokePackageDirectoryAsync("config-report-refreshed-check", firstRoot, "check", "--json");
        AssertEqual(0, refreshedCheck.ExitCode, Describe(refreshedCheck));
    }

    private static async Task TestConfigExample(Harness harness)
    {
        const string secretCanary = "config-example-secret-canary-92c3";
        const string nameCanary = "config-example-name-canary-a170";
        const string modeCanary = "config-example-mode-canary-f680";
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_CONFIG_NAME"] = nameCanary,
            ["HOB_CONFIG_MODE"] = modeCanary,
            ["HOB_CONFIG_TOKEN"] = secretCanary
        };

        var defaultManifest = ConfigCliManifest().Replace(
            "Text|default:normal",
            "Text|default:keep $HOME # café = literal",
            StringComparison.Ordinal);
        var lockSentinel = new UTF8Encoding(false).GetBytes("lock sentinel\n");
        var dependencyManifest = defaultManifest + "\n[dependencies]\nmissing = \"../missing-library\"\n";
        var firstRoot = await harness.WritePackageAsync(
            "config-example-first",
            dependencyManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 0; }\n"
            });
        var lockPath = Path.Combine(firstRoot, "hob.lock");
        await File.WriteAllBytesAsync(lockPath, lockSentinel);

        Task<ProcessResult> GenerateExampleAsync(string packageRoot, IReadOnlyDictionary<string, string>? overrides = null) =>
            harness.InvokeCompilerCommandAtDirectoryWithEnvironmentAsync(
                packageRoot,
                overrides,
                "config",
                "example",
                packageRoot);

        var first = await GenerateExampleAsync(firstRoot);
        AssertEqual(0, first.ExitCode, Describe(first));
        AssertEqual(string.Empty, first.StandardError, Describe(first));

        var outputPath = Path.Combine(firstRoot, ".env.example");
        var outputBytes = await File.ReadAllBytesAsync(outputPath);
        var expectedText = "# Generated from hob.toml [config].\n"
            + "# Informational only; Hob does not load this file.\n\n"
            + "HOB_CONFIG_MODE=\"keep \\$HOME # café = literal\"\n"
            + "HOB_CONFIG_NAME=\"\"\n"
            + "# HOB_CONFIG_TOKEN is secret; supply it through deployment configuration.\n";
        var expectedBytes = new UTF8Encoding(false).GetBytes(expectedText);
        AssertTrue(outputBytes.AsSpan().SequenceEqual(expectedBytes),
            "The config example must have exact sorted UTF-8-no-BOM LF bytes, literal-dollar escaping, and no secret assignment.");
        var outputText = Encoding.UTF8.GetString(outputBytes);
        foreach (var canary in new[] { secretCanary, nameCanary, modeCanary })
        {
            AssertTrue(!outputText.Contains(canary, StringComparison.Ordinal),
                $"The config example file must not contain the runtime value {canary}.");
        }
        AssertTrue(!outputBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble),
            "The config example must not include a UTF-8 BOM.");
        AssertTrue(!outputText.Contains('\r'), "The config example must use LF line endings on every host.");
        AssertTrue(!outputText.Contains("HOB_CONFIG_TOKEN=", StringComparison.Ordinal),
            "Secret config fields must be comments, never assignments.");
        var lockAfter = await File.ReadAllBytesAsync(lockPath);
        AssertTrue(lockSentinel.AsSpan().SequenceEqual(lockAfter),
            "Generating a config example must not validate, create, or mutate the package lock.");

        var repeatedRoot = await harness.WritePackageAsync(
            "config-example-repeat",
            dependencyManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 0; }\n"
            });
        var repeated = await GenerateExampleAsync(repeatedRoot, environment);
        AssertEqual(0, repeated.ExitCode, Describe(repeated));
        foreach (var canary in new[] { secretCanary, nameCanary, modeCanary })
        {
            AssertTrue(!repeated.StandardOutput.Contains(canary, StringComparison.Ordinal)
                && !repeated.StandardError.Contains(canary, StringComparison.Ordinal),
                $"The config example command must not print the runtime value {canary}.");
        }
        var repeatedBytes = await File.ReadAllBytesAsync(Path.Combine(repeatedRoot, ".env.example"));
        foreach (var canary in new[] { secretCanary, nameCanary, modeCanary })
            AssertTrue(!Encoding.UTF8.GetString(repeatedBytes).Contains(canary, StringComparison.Ordinal),
                $"The config example file must not contain the runtime value {canary}.");
        AssertTrue(outputBytes.AsSpan().SequenceEqual(repeatedBytes),
            "Equivalent root config schemas must produce identical bytes across package roots.");

        var invalidRoot = await harness.WritePackageAsync(
            "config-example-invalid",
            CliPackageManifest() + "\n[config]\nname = \"Text|optional\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 0; }\n"
            });
        var invalid = await GenerateExampleAsync(invalidRoot);
        AssertTrue(invalid.ExitCode != 0, $"Malformed config unexpectedly generated an example. {Describe(invalid)}");
        AssertTrue(invalid.StandardError.Contains("E_MANIFEST", StringComparison.Ordinal), Describe(invalid));
        AssertTrue(!File.Exists(Path.Combine(invalidRoot, ".env.example")),
            "Invalid config must fail before creating the output file.");

        var existingRoot = await harness.WritePackageAsync(
            "config-example-existing",
            defaultManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 0; }\n"
            });
        var existingPath = Path.Combine(existingRoot, ".env.example");
        var existingSentinel = new UTF8Encoding(false).GetBytes("preserve existing example\n");
        await File.WriteAllBytesAsync(existingPath, existingSentinel);
        var existing = await GenerateExampleAsync(existingRoot);
        AssertTrue(existing.ExitCode != 0, $"Existing output was unexpectedly overwritten. {Describe(existing)}");
        AssertTrue(existing.StandardError.Contains("E_IO", StringComparison.Ordinal), Describe(existing));
        var existingAfter = await File.ReadAllBytesAsync(existingPath);
        AssertTrue(existingSentinel.AsSpan().SequenceEqual(existingAfter),
            "An existing config example must remain byte-identical after a refused write.");

        var symlinkRoot = await harness.WritePackageAsync(
            "config-example-symlink",
            defaultManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 0; }\n"
            });
        var symlinkPath = Path.Combine(symlinkRoot, ".env.example");
        var symlinkTarget = Path.Combine(harness.TemporaryRoot, "config-example-symlink-target.txt");
        const string symlinkSentinel = "preserve symlink target\n";
        await File.WriteAllTextAsync(symlinkTarget, symlinkSentinel, new UTF8Encoding(false));
        var symlinkCreated = false;
        try
        {
            File.CreateSymbolicLink(symlinkPath, symlinkTarget);
            symlinkCreated = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.WriteLine("SKIP config example symlink subcase: this host does not permit file symbolic links.");
        }

        if (symlinkCreated)
        {
            var symlink = await GenerateExampleAsync(symlinkRoot);
            AssertTrue(symlink.ExitCode != 0, $"A symlink at the output path was unexpectedly followed. {Describe(symlink)}");
            AssertTrue(symlink.StandardError.Contains("E_IO", StringComparison.Ordinal), Describe(symlink));
            AssertEqual(symlinkSentinel, await File.ReadAllTextAsync(symlinkTarget),
                "Refusing a symlink output must leave its target byte-identical.");
        }

        var raceRoot = await harness.WritePackageAsync(
            "config-example-race",
            defaultManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 0; }\n"
            });
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => GenerateExampleAsync(raceRoot)));
        AssertEqual(1, attempts.Count(result => result.ExitCode == 0),
            "Concurrent config example creators must have exactly one winner.");
        foreach (var refused in attempts.Where(result => result.ExitCode != 0))
        {
            AssertTrue(refused.StandardError.Contains("E_IO", StringComparison.Ordinal),
                $"Every concurrent losing creator should receive an I/O diagnostic. {Describe(refused)}");
        }
        var raceBytes = await File.ReadAllBytesAsync(Path.Combine(raceRoot, ".env.example"));
        AssertTrue(expectedBytes.AsSpan().SequenceEqual(raceBytes),
            "The concurrent winner must publish the complete deterministic example bytes.");
    }

    private static async Task TestConfigCliRuntime(Harness harness)
    {
        var unusedSecretsPackage = await harness.WritePackageAsync(
            "config-unused-secrets-injection",
            CliPackageManifest().Replace("harness-package", "config-unused-secrets-injection", StringComparison.Ordinal)
                + "\n[capabilities]\nsecret.reveal = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module app::main;
                    pub union ShowError { Failed }
                    command show {
                        help "Show a value without reading a secret.";
                        argument label: Text help "Value to return.";
                        handler: self::app::main::run;
                        error: self::app::main::describe;
                    }
                    pub fn run(args: self::app::main::ShowArgs, secrets: Secrets) -> Result<Text, self::app::main::ShowError> effects {} {
                        return Ok(args.label);
                    }
                    pub fn describe(error: self::app::main::ShowError) -> Text effects {} {
                        return match error {
                            self::app::main::ShowError.Failed => "failed"
                        };
                    }
                    """
            });
        var unusedSecretsCheck = await harness.InvokePackageDirectoryAsync(
            "config-unused-secrets-injection-check", unusedSecretsPackage, "check", "--json");
        AssertEqual(0, unusedSecretsCheck.ExitCode, Describe(unusedSecretsCheck));
        var unusedSecretsBuild = await harness.InvokePackageDirectoryAsync(
            "config-unused-secrets-injection-build", unusedSecretsPackage, "build");
        var unusedSecretsExecutable = ParseBuiltArtifact(unusedSecretsBuild, "Built executable: ");
        AssertTrue(File.Exists(unusedSecretsExecutable), Describe(unusedSecretsBuild));

        var packageRoot = await harness.WritePackageAsync(
            "config-cli-runtime",
            ConfigCliManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = ConfigCliSource
            });
        var check = await harness.InvokePackageDirectoryAsync("config-cli-runtime-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));

        var build = await harness.InvokePackageDirectoryAsync("config-cli-runtime-build", packageRoot, "build");
        var managedExecutable = ParseBuiltArtifact(build, "Built executable: ");
        var managedOutputDirectory = Path.GetDirectoryName(managedExecutable)!;
        var managedSchemaBytes = await File.ReadAllBytesAsync(Path.Combine(managedOutputDirectory, "command-schema.json"));
        using (var receipt = await AssertBuildReceiptAsync(
                   managedOutputDirectory,
                   "managed",
                   expectedRuntimeIdentifier: null,
                   [Path.GetRelativePath(managedOutputDirectory, managedExecutable).Replace(Path.DirectorySeparatorChar, '/'), "command-schema.json"],
                   packageRoot))
            AssertEqual(4, receipt.RootElement.GetProperty("schema_version").GetInt32(),
                "Managed config CLI receipts should use schema version 4.");

        const string secretCanary = "secret-canary-runtime-0a91e6";
        var defaultEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_CONFIG_NAME"] = "Ada",
            ["HOB_CONFIG_TOKEN"] = secretCanary
        };
        var defaultRun = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "config-cli-default-runtime", packageRoot, "run", defaultEnvironment, "--", "inspect", "startup");
        AssertEqual(0, defaultRun.ExitCode, Describe(defaultRun));
        AssertEqual("Ada" + Environment.NewLine, defaultRun.StandardOutput, Describe(defaultRun));
        AssertConfigCliLog(defaultRun.StandardError, "Default managed run");
        AssertTrue(!defaultRun.StandardOutput.Contains(secretCanary, StringComparison.Ordinal)
            && !defaultRun.StandardError.Contains(secretCanary, StringComparison.Ordinal),
            "A config secret must remain absent from ordinary CLI output and logs.");

        var emptyEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_CONFIG_NAME"] = string.Empty,
            ["HOB_CONFIG_MODE"] = "normal",
            ["HOB_CONFIG_TOKEN"] = secretCanary
        };
        var emptyRun = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "config-cli-empty-required-runtime", packageRoot, "run", emptyEnvironment, "--", "inspect", "startup");
        AssertEqual(0, emptyRun.ExitCode, Describe(emptyRun));
        AssertEqual(Environment.NewLine, emptyRun.StandardOutput,
            "An empty required environment value must be retained as empty text instead of treated as missing.");
        AssertConfigCliLog(emptyRun.StandardError, Describe(emptyRun));
        AssertEqual(defaultRun.StandardError, emptyRun.StandardError,
            "Repeated managed invocations with identical logger inputs must produce byte-identical JSON.");

        var emptyDefaultEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_CONFIG_NAME"] = "Ada",
            ["HOB_CONFIG_MODE"] = string.Empty,
            ["HOB_CONFIG_TOKEN"] = secretCanary
        };
        var emptyDefaultRun = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "config-cli-empty-default-runtime", packageRoot, "run", emptyDefaultEnvironment, "--", "inspect", "startup");
        AssertEqual(0, emptyDefaultRun.ExitCode, Describe(emptyDefaultRun));
        AssertEqual("unexpected-mode" + Environment.NewLine, emptyDefaultRun.StandardOutput,
            "An explicitly empty optional value must override its nonempty default.");
        AssertConfigCliLog(emptyDefaultRun.StandardError, Describe(emptyDefaultRun));

        var revealRun = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "config-cli-explicit-reveal", packageRoot, "run", defaultEnvironment, "--", "inspect", "startup", "--reveal");
        AssertEqual(0, revealRun.ExitCode, Describe(revealRun));
        AssertEqual(secretCanary + Environment.NewLine, revealRun.StandardOutput,
            "Secrets.reveal_text should expose a secret only on the explicit reveal command path.");
        AssertConfigCliLog(revealRun.StandardError, Describe(revealRun));

        var missingEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_CONFIG_TOKEN"] = secretCanary
        };
        var missing = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "config-cli-required-missing", packageRoot, "run", missingEnvironment, "--", "inspect", "startup");
        AssertEqual(78, missing.ExitCode, Describe(missing));
        AssertEqual(string.Empty, missing.StandardOutput, Describe(missing));
        AssertTrue(missing.StandardError.Contains("Missing required configuration field: name (HOB_CONFIG_NAME)", StringComparison.Ordinal),
            $"Missing required config should fail with exit 78 and identify only the declared field/environment name. {Describe(missing)}");
        AssertTrue(!missing.StandardError.Contains(secretCanary, StringComparison.Ordinal),
            "Missing-config diagnostics must not reveal any supplied secret value.");

        var missingSecretEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_CONFIG_NAME"] = "Ada",
            ["HOB_CONFIG_MODE"] = "normal"
        };
        var missingSecret = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "config-cli-required-secret-missing", packageRoot, "run", missingSecretEnvironment,
            "--", "inspect", "startup");
        AssertEqual(78, missingSecret.ExitCode, Describe(missingSecret));
        AssertEqual(string.Empty, missingSecret.StandardOutput, Describe(missingSecret));
        AssertTrue(missingSecret.StandardError.Contains(
                "Missing required configuration field: token (HOB_CONFIG_TOKEN)", StringComparison.Ordinal),
            $"A missing required secret should fail with exit 78 and identify only its declared field and environment name. {Describe(missingSecret)}");

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new IntegrationTestSkippedException("The config NativeAOT smoke test targets x64 hosts only.");

        var aotBuild = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "config-cli-aot-build",
            packageRoot,
            "build",
            AotPublishTimeout,
            "--aot",
            "--rid",
            CurrentHostAotRid());
        AssertEqual(0, aotBuild.ExitCode, Describe(aotBuild));
        const string nativePrefix = "Built native executable: ";
        AssertTrue(aotBuild.StandardOutput.StartsWith(nativePrefix, StringComparison.Ordinal), Describe(aotBuild));
        var nativeExecutable = aotBuild.StandardOutput[nativePrefix.Length..].Trim();
        AssertTrue(Path.IsPathFullyQualified(nativeExecutable) && File.Exists(nativeExecutable),
            $"Expected a NativeAOT config CLI executable at {nativeExecutable}.");
        var nativeOutputDirectory = Path.GetDirectoryName(nativeExecutable)!;
        var nativeSchemaBytes = await File.ReadAllBytesAsync(Path.Combine(nativeOutputDirectory, "command-schema.json"));
        AssertTrue(managedSchemaBytes.SequenceEqual(nativeSchemaBytes),
            "Managed and NativeAOT command schemas must remain byte-for-byte identical.");
        using (var receipt = await AssertBuildReceiptAsync(
                   nativeOutputDirectory,
                   "native_aot",
                   CurrentHostAotRid(),
                   [Path.GetRelativePath(nativeOutputDirectory, nativeExecutable).Replace(Path.DirectorySeparatorChar, '/'), "command-schema.json"],
                   packageRoot))
            AssertEqual(4, receipt.RootElement.GetProperty("schema_version").GetInt32(),
                "NativeAOT config CLI receipts should use schema version 4.");

        var nativeRun = await ExecuteNativeWithEnvironmentAsync(
            nativeExecutable,
            TimeSpan.FromSeconds(30),
            defaultEnvironment,
            "inspect",
            "startup",
            "--reveal");
        AssertEqual(0, nativeRun.ExitCode, Describe(nativeRun));
        AssertEqual(secretCanary + Environment.NewLine, nativeRun.StandardOutput, Describe(nativeRun));
        AssertConfigCliLog(nativeRun.StandardError, "NativeAOT run");
        await AssertNoTextInFilesAsync(Path.Combine(packageRoot, "out"), secretCanary);
    }

    private static async Task TestConfigWebAsyncRuntime(Harness harness)
    {
        const string source = """
            module app::main;
            pub union Reply { Ready(Text) }

            async fn home(config: Config, secrets: Secrets, logger: Logger) -> self::app::main::Reply effects { env.read, log.write } {
                let greeting: Text = config.get_text("greeting");
                let token: Secret<Text> = config.get_secret_text("token");
                let logged: bool = logger.info("web\nstart", "ready");
                return self::app::main::Reply.Ready(greeting);
            }

            route GET "/" {
                handler: self::app::main::home;
                response Ready: 200 json Text;
            }
            """;
        const string manifest = "name = \"config-web-runtime\"\n"
            + "version = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
            + "\n[config]\ngreeting = \"Text|required\"\ntoken = \"Secret<Text>|required\"\n"
            + "\n[capabilities]\nnet.listen = \"allow\"\nenv.read = \"allow\"\nsecret.reveal = \"allow\"\nlog.write = \"allow\"\n";
        var packageRoot = await harness.WritePackageAsync(
            "config-web-runtime",
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source
            });
        var check = await harness.InvokePackageDirectoryAsync("config-web-runtime-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));

        var build = await harness.InvokePackageDirectoryAsync("config-web-runtime-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        var executable = ParseBuiltArtifact(build, "Built executable: ");
        var outputDirectory = Path.GetDirectoryName(executable)!;
        using var receipt = await AssertBuildReceiptAsync(
            outputDirectory,
            "managed",
            expectedRuntimeIdentifier: null,
            [Path.GetRelativePath(outputDirectory, executable).Replace(Path.DirectorySeparatorChar, '/'), "openapi.json"],
            packageRoot);

        var wrongOrderSource = Regex.Replace(
            source,
            @"config: Config,\s*secrets: Secrets,\s*logger: Logger",
            "logger: Logger, config: Config, secrets: Secrets",
            RegexOptions.CultureInvariant);
        AssertTrue(wrongOrderSource != source,
            "The wrong-order web fixture must actually change the route handler signature.");
        var wrongOrderPackage = await harness.WritePackageAsync(
            "config-web-wrong-capability-order",
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = wrongOrderSource
            });
        var wrongOrder = await harness.InvokePackageDirectoryAsync(
            "config-web-wrong-capability-order-check", wrongOrderPackage, "check", "--json");
        AssertTrue(wrongOrder.ExitCode != 0, Describe(wrongOrder));
        AssertTrue(ParseDiagnosticSnapshots(wrongOrder.StandardOutput).Any(diagnostic =>
                diagnostic.Code == "E_ROUTE_HANDLER"
                && diagnostic.Message == "Route handler capability parameters must appear in FsWrite, DbRead, DbWrite, HttpClient, Config, Secrets, Logger order"),
            $"Web config injection must follow the route capability parameter order. {Describe(wrongOrder)}");

        const string secretCanary = "secret-canary-web-injection-48ca20";
        var port = GetUnusedLoopbackPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(4) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "config-web-runtime-run",
            packageRoot,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HOB_CONFIG_GREETING"] = "web greeting",
                ["HOB_CONFIG_TOKEN"] = secretCanary
            },
            "--urls",
            baseAddress.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        string stdout = string.Empty;
        string stderr = string.Empty;
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);
            using var response = await client.GetAsync("/");
            AssertEqual(HttpStatusCode.OK, response.StatusCode, "The async config route should return its declared status.");
            AssertEqual("application/json", response.Content.Headers.ContentType?.MediaType,
                "The async config route should serialize its typed Text payload as JSON.");
            using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                AssertEqual("web greeting", body.RootElement.GetString(),
                    "The generated web host should inject the startup Config value into its async route handler.");
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
                    throw new TimeoutException("The config web process did not stop within 10 seconds.");
                }
            }

            stdout = await stdoutTask;
            stderr = await stderrTask;
            await AssertLoopbackPortReleasedAsync(port);
        }

        AssertTrue(!stdout.Contains(secretCanary, StringComparison.Ordinal)
            && !stderr.Contains(secretCanary, StringComparison.Ordinal),
            "Web startup, route output, and non-reveal logger lines must not expose the supplied secret.");
        var structuredLog = stderr.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Contains("\"event\":\"web\\nstart\"", StringComparison.Ordinal));
        AssertTrue(structuredLog is not null, $"Expected a structured web log line. stderr=<{stderr}>");
        using var logDocument = JsonDocument.Parse(structuredLog!);
        AssertJsonPropertyOrder(logDocument.RootElement, "level,event,detail,request_id");
        AssertEqual("info", logDocument.RootElement.GetProperty("level").GetString(), "Web logger level mismatch.");
        AssertEqual("web\nstart", logDocument.RootElement.GetProperty("event").GetString(), "Web logger event mismatch.");
        AssertEqual("ready", logDocument.RootElement.GetProperty("detail").GetString(), "Web logger detail mismatch.");
        AssertTrue(!string.IsNullOrWhiteSpace(logDocument.RootElement.GetProperty("request_id").GetString()),
            "Web Logger.info lines must include the trusted request identifier last.");
    }

    private static IReadOnlyDictionary<string, string> ProcessRunnerSources()
    {
        const string main = """
            module app::main;

            command run {
                help "Run a pinned process fixture.";
                argument label: Text help "Invocation label.";
                option mode: Text = "args" help "Fixture mode.";
                option first: Text = "" help "First literal argument.";
                option second: Text = "" help "Second literal argument.";
                option input: FilePath = "payload.txt" help "Input text file.";
                option marker: Text = "child.pid" help "Child synchronization marker.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string handlers = """
            module handlers;

            pub union RunError { Failed }

            fn error_text(error: ProcessError) -> Text effects {} {
                return match error {
                    ProcessError.InvalidArgument => "InvalidArgument",
                    ProcessError.InputTooLarge => "InputTooLarge",
                    ProcessError.OutputTooLarge => "OutputTooLarge",
                    ProcessError.InvalidText => "InvalidText",
                    ProcessError.StartFailed => "StartFailed",
                    ProcessError.TimedOut => "TimedOut"
                };
            }

            fn stdout_text(result: Result<ProcessOutput, ProcessError>) -> Text effects {} {
                return match result {
                    Ok(output) => output.stdout,
                    Err(error) => self::handlers::error_text(error)
                };
            }

            fn stderr_text(result: Result<ProcessOutput, ProcessError>) -> Text effects {} {
                return match result {
                    Ok(output) => output.stderr,
                    Err(error) => self::handlers::error_text(error)
                };
            }

            fn exit_output_text(output: ProcessOutput) -> Text effects {} {
                if output.exit_code == 7 { return "exit=7"; }
                return "wrong-exit";
            }

            fn exit_text(result: Result<ProcessOutput, ProcessError>) -> Text effects {} {
                return match result {
                    Ok(output) => self::handlers::exit_output_text(output),
                    Err(error) => self::handlers::error_text(error)
                };
            }

            fn read_input(fs: FsRead, path: FilePath) -> Text effects { fs.read } {
                return match fs.read_text(path) {
                    Ok(text) => text,
                    Err(error) => ""
                };
            }

            async fn invoke(runner: ProcessRunner, arguments: List<Text>, stdin: Text) -> Result<ProcessOutput, ProcessError> effects { process.spawn } {
                return await runner.run_text_async(arguments, stdin);
            }

            pub async fn run(args: self::app::main::RunArgs, fs: FsRead, logger: Logger, runner: ProcessRunner) -> Result<Text, self::handlers::RunError> effects { fs.read, log.write, process.spawn } {
                let logged: bool = logger.info("process.runner", "invoking pinned executable");

                if args.mode == "stdin" {
                    let payload: Text = self::handlers::read_input(fs, args.input);
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["stdin"], payload);
                    return Ok(self::handlers::stdout_text(result));
                }
                if args.mode == "stdin-exact" {
                    let payload: Text = self::handlers::read_input(fs, args.input);
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["stdin"], payload);
                    return Ok(self::handlers::stdout_text(result));
                }
                if args.mode == "stderr" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["streams"], "");
                    return Ok(self::handlers::stderr_text(result));
                }
                if args.mode == "exit" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["exit"], "");
                    return Ok(self::handlers::exit_text(result));
                }
                if args.mode == "meta" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["meta"], "");
                    return Ok(self::handlers::stdout_text(result));
                }
                if args.mode == "stdout-exact" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["stdout", "1048576"], "");
                    return Ok(self::handlers::stdout_text(result));
                }
                if args.mode == "stdout-over" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["stdout", "1048577"], "");
                    return Ok(self::handlers::stdout_text(result));
                }
                if args.mode == "stderr-exact" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["stderr", "1048576"], "");
                    return Ok(self::handlers::stderr_text(result));
                }
                if args.mode == "stderr-over" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["stderr", "1048577"], "");
                    return Ok(self::handlers::stderr_text(result));
                }
                if args.mode == "invalid-utf8" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["invalid-utf8"], "");
                    return Ok(self::handlers::stdout_text(result));
                }
                if args.mode == "timeout" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["timeout", args.marker], "");
                    return Ok(self::handlers::stdout_text(result));
                }
                if args.mode == "empty-argv" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, [], "");
                    return Ok(self::handlers::stdout_text(result));
                }
                if args.mode == "args" {
                    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["args", args.first, args.second], "");
                    return Ok(self::handlers::stdout_text(result));
                }

                // PROCESS_LIMIT_CASES
                let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, ["args", args.first, args.second], "");
                return Ok(self::handlers::stdout_text(result));
            }

            pub fn describe(error: self::handlers::RunError) -> Text effects {} {
                return match error { self::handlers::RunError.Failed => "process failed" };
            }
            """;

        var limitCases = new StringBuilder();
        AppendCase("argc-exact", ["count", .. Enumerable.Repeat("x", 127)]);
        AppendCase("argc-over", ["count", .. Enumerable.Repeat("x", 128)]);
        AppendCase("argv-bytes-exact", ["size", new string('x', 16_380)]);
        AppendCase("argv-bytes-over", ["size", new string('x', 16_381)]);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = main,
            ["src/handlers.hob"] = handlers.Replace("// PROCESS_LIMIT_CASES", limitCases.ToString().TrimEnd(), StringComparison.Ordinal)
        };

        void AppendCase(string mode, string[] arguments)
        {
            var renderedArguments = string.Join(", ", arguments.Select(argument => "\"" + argument + "\""));
            limitCases.Append("if (args.mode == \"").Append(mode).AppendLine("\") {");
            limitCases.Append("    let result: Result<ProcessOutput, ProcessError> = await self::handlers::invoke(runner, [")
                .Append(renderedArguments).AppendLine("], \"\");");
            limitCases.AppendLine("    return Ok(self::handlers::stdout_text(result));");
            limitCases.AppendLine("}");
        }
    }

    private static string ProcessRunnerTestSource() => ProcessRunnerSources()["src/handlers.hob"];

    private static string ProcessCliManifest(
        string name,
        (string Os, string Path, string Sha256) pin,
        bool includeProcessGrant = true,
        string processGrantValue = "allow") =>
        ProcessCliManifest(name, [pin], includeProcessGrant, processGrantValue);

    private static string ProcessCliManifest(
        string name,
        (string Os, string Path, string Sha256) firstPin,
        (string Os, string Path, string Sha256) secondPin) =>
        ProcessCliManifest(name, [firstPin, secondPin]);

    private static string ProcessCliManifest(
        string name,
        IReadOnlyList<(string Os, string Path, string Sha256)> pins,
        bool includeProcessGrant = true,
        string processGrantValue = "allow")
    {
        var manifest = CliPackageManifest("app::main")
            .Replace("name = \"harness-package\"", $"name = \"{name}\"", StringComparison.Ordinal);
        foreach (var os in new[] { "windows", "linux" })
        {
            var pin = pins.SingleOrDefault(candidate => candidate.Os == os);
            if (pin.Os is null)
                continue;
            manifest += $"process_{os}_path = \"{pin.Path}\"\nprocess_{os}_sha256 = \"{pin.Sha256}\"\n";
        }

        manifest += "\n[capabilities]\nfs.read = \"allow\"\nlog.write = \"allow\"\n";
        if (includeProcessGrant)
            manifest += $"process.spawn = \"{processGrantValue}\"\n";
        return manifest;
    }

    private static async Task InstallPinnedExecutableAsync(string packageRoot, string relativePath, string sourceExecutable)
    {
        var destination = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(sourceExecutable, destination, overwrite: true);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static string HashSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string ProcessRunnerCopyName(string assemblyName) =>
        assemblyName + ".process-runner" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);

    private static string JsonQuote(string value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.GetRawText();
    }

    private static void AssertProcessRunText(string expectedOutput, ProcessResult result, string context)
    {
        AssertEqual(0, result.ExitCode, $"{context}: generated command should return success. {Describe(result)}");
        AssertEqual(expectedOutput, result.StandardOutput, $"{context}: ProcessRunner result text mismatch.");
        AssertProcessLoggerLine(result.StandardError, context);
    }

    private static void AssertProcessArgumentRun(ProcessResult result, string context, params string[] expectedArguments)
    {
        AssertEqual(0, result.ExitCode, $"{context}: generated command should return success. {Describe(result)}");
        using var arguments = JsonDocument.Parse(result.StandardOutput);
        AssertEqual(JsonValueKind.Array, arguments.RootElement.ValueKind, $"{context}: helper output must be a JSON array.");
        var actual = arguments.RootElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        AssertTrue(expectedArguments.SequenceEqual(actual, StringComparer.Ordinal),
            $"{context}: child arguments were changed. Expected [{string.Join(" | ", expectedArguments)}], got [{string.Join(" | ", actual)}].");
        AssertProcessLoggerLine(result.StandardError, context);
    }

    private static void AssertProcessLoggerLine(string stderr, string context)
    {
        var lines = stderr.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        AssertEqual(1, lines.Length, $"{context}: expected the handler's single structured log line.");
        using var log = JsonDocument.Parse(lines[0]);
        AssertJsonPropertyOrder(log.RootElement, "level,event,detail");
        AssertEqual("info", log.RootElement.GetProperty("level").GetString(), $"{context}: log level mismatch.");
        AssertEqual("process.runner", log.RootElement.GetProperty("event").GetString(), $"{context}: log event mismatch.");
        AssertEqual("invoking pinned executable", log.RootElement.GetProperty("detail").GetString(), $"{context}: log detail mismatch.");
    }

    private static void AssertUnixExecutableIfRequired(string executablePath, string message)
    {
        if (!OperatingSystem.IsLinux())
            return;
        AssertTrue(File.Exists(executablePath), $"Expected generated process executable {executablePath}.");
        const UnixFileMode executeBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        AssertTrue((File.GetUnixFileMode(executablePath) & executeBits) != 0, message);
    }

    private static void AssertPackageJsonDiagnostic(ProcessResult result, string expectedCode, string message)
    {
        AssertTrue(result.ExitCode != 0, message + " Invalid package unexpectedly succeeded. " + Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        var diagnostics = ParseDiagnosticSnapshots(result.StandardOutput);
        AssertTrue(diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            message + $" Expected {expectedCode}. {result.StandardOutput}");
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        if (File.Exists(path))
            return;

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Synchronization file {path} has no parent directory.");
        var name = Path.GetFileName(path);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(directory, name)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        watcher.Created += (_, _) => observed.TrySetResult();
        watcher.Changed += (_, _) => observed.TrySetResult();
        watcher.Renamed += (_, _) => observed.TrySetResult();
        if (File.Exists(path))
            return;

        try
        {
            await observed.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            if (!File.Exists(path))
                throw new TimeoutException($"Timed out waiting for synchronization file {path}.");
        }
    }

    private static async Task AssertProcessTreeWasReapedAsync(string markerPath)
    {
        AssertTrue(File.Exists(markerPath), $"The child process did not publish its PID marker: {markerPath}.");
        AssertTrue(int.TryParse(await File.ReadAllTextAsync(markerPath), out var processId) && processId > 0,
            $"The child PID marker was invalid: {await File.ReadAllTextAsync(markerPath)}.");
        try
        {
            using var child = Process.GetProcessById(processId);
            if (!child.HasExited)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await child.WaitForExitAsync(timeout.Token);
            }
            AssertTrue(child.HasExited, $"ProcessRunner left child process {processId} running after completion.");
        }
        catch (ArgumentException)
        {
            // The OS already removed the process before it could be opened.
        }

        AssertTrue(!File.Exists(markerPath + ".late"), "A killed process-tree descendant must not continue running after ProcessRunner returns.");
    }

    private static async Task TestProcessManifestAndReports(Harness harness)
    {
        var helperExecutable = await harness.GetProcessFixtureExecutableAsync();
        var helperBytes = await File.ReadAllBytesAsync(helperExecutable);
        var helperHash = HashSha256(helperBytes);
        var alternatePinBytes = new byte[] { 0x50, 0x49, 0x4e, 0x00, 0x01 };
        var alternatePinHash = HashSha256(alternatePinBytes);
        var hostOs = OperatingSystem.IsWindows() ? "windows" : "linux";
        var otherOs = hostOs == "windows" ? "linux" : "windows";
        var rootManifest = ProcessCliManifest(
                "process-report-root",
                ("windows", "tools/windows-pin", hostOs == "windows" ? helperHash : alternatePinHash),
                ("linux", "tools/linux-pin", hostOs == "linux" ? helperHash : alternatePinHash))
            + "\n[dependencies]\nsupport = \"../support\"\n";
        var packages = new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
        {
            ["root"] = new PackageFixture(rootManifest, ProcessRunnerSources()),
            ["support"] = new PackageFixture(
                LibraryPackageManifest("process-report-support"),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/support.hob"] = "module support; pub fn value() -> i32 effects {} { return 1; }\n"
                })
        };
        var packageRoot = await harness.WritePackageGraphAsync("process-report", packages);
        await InstallPinnedExecutableAsync(packageRoot, hostOs == "windows" ? "tools/windows-pin" : "tools/linux-pin", helperExecutable);
        var alternatePath = Path.Combine(packageRoot, hostOs == "windows" ? "tools/linux-pin" : "tools/windows-pin");
        Directory.CreateDirectory(Path.GetDirectoryName(alternatePath)!);
        await File.WriteAllBytesAsync(alternatePath, alternatePinBytes);

        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("process-report-lock", packageRoot, "lock"));
        var check = await harness.InvokePackageDirectoryAsync("process-report-check", packageRoot, "check");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(string.Empty, check.StandardError, Describe(check));

        var api = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, api.ExitCode, Describe(api));
        using (var apiDocument = JsonDocument.Parse(api.StandardOutput))
        {
            var root = apiDocument.RootElement;
            AssertInspectApiPropertyOrder(root);
            AssertEqual(13, root.GetProperty("schema_version").GetInt32(), "Inspect-api schema version must be 13 with pinned process metadata.");
            var pins = root.GetProperty("process_executables").EnumerateArray().ToArray();
            AssertEqual("windows,linux", string.Join(",", pins.Select(pin => pin.GetProperty("os").GetString())),
                "API process pins must be ordered Windows then Linux.");
            for (var index = 0; index < pins.Length; index++)
            {
                AssertJsonPropertyOrder(pins[index], "os,path,sha256");
                var expectedPath = index == 0 ? "tools/windows-pin" : "tools/linux-pin";
                var expectedHash = index == 0
                    ? (hostOs == "windows" ? helperHash : alternatePinHash)
                    : (hostOs == "linux" ? helperHash : alternatePinHash);
                AssertEqual(expectedPath, pins[index].GetProperty("path").GetString(), "API pins must use package-relative paths.");
                AssertEqual(expectedHash, pins[index].GetProperty("sha256").GetString(), "API pins must retain the declared content hash.");
            }
            AssertApiPortable(api.StandardOutput, root, harness.TemporaryRoot);
        }

        var effects = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::handlers::run", "--json");
        AssertEqual(0, effects.ExitCode, Describe(effects));
        using (var effectsDocument = JsonDocument.Parse(effects.StandardOutput))
        {
            var root = effectsDocument.RootElement;
            AssertEqual(1, root.GetProperty("schema_version").GetInt32(), "Inspect-effects schema stays at version 1.");
            var processPath = root.GetProperty("effect_paths").EnumerateArray()
                .Single(path => path.GetProperty("effect").GetString() == "process.spawn");
            var steps = processPath.GetProperty("steps").EnumerateArray().Select(step => step.GetString() ?? string.Empty).ToArray();
            AssertTrue(steps.Any(step => step.Contains("invoke", StringComparison.Ordinal))
                && steps.Any(step => step.Contains("run_text_async", StringComparison.Ordinal)),
                $"Process effects should show the transitive helper and intrinsic path. {effects.StandardOutput}");
        }

        var audit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, audit.ExitCode, Describe(audit));
        using (var auditDocument = JsonDocument.Parse(audit.StandardOutput))
        {
            var root = auditDocument.RootElement;
            AssertAuditPropertyOrder(root);
            AssertEqual(11, root.GetProperty("schema_version").GetInt32(), "Audit schema version must be 11 with pinned process metadata.");
            var pins = root.GetProperty("process_executables").EnumerateArray().ToArray();
            AssertEqual("windows,linux", string.Join(",", pins.Select(pin => pin.GetProperty("os").GetString())),
                "Audit process pins must use deterministic OS ordering.");
            var rootPackage = root.GetProperty("packages").EnumerateArray().Single(package => package.GetProperty("role").GetString() == "root");
            var processInputs = rootPackage.GetProperty("inputs").EnumerateArray()
                .Where(input => input.GetProperty("kind").GetString() == "process_executable")
                .ToArray();
            AssertEqual(2, processInputs.Length, "Audit inputs must include every declared OS executable pin.");
            AssertTrue(processInputs.Any(input => input.GetProperty("path").GetString() == "tools/windows-pin"
                    && input.GetProperty("sha256").GetString() == (hostOs == "windows" ? helperHash : alternatePinHash))
                && processInputs.Any(input => input.GetProperty("path").GetString() == "tools/linux-pin"
                    && input.GetProperty("sha256").GetString() == (hostOs == "linux" ? helperHash : alternatePinHash)),
                "Audit process executable inputs must use the relative pin paths and validated content hashes.");
            AssertTrue(!audit.StandardOutput.Contains(harness.TemporaryRoot, StringComparison.OrdinalIgnoreCase),
                "Audit output must not expose the temporary package path.");
        }

        var repeatedAudit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(audit.StandardOutput, repeatedAudit.StandardOutput, "Repeated process audit output must be byte-identical.");

        var relocatedRoot = await harness.WritePackageGraphAsync("process-report-relocated", packages);
        await InstallPinnedExecutableAsync(relocatedRoot, hostOs == "windows" ? "tools/windows-pin" : "tools/linux-pin", helperExecutable);
        var relocatedAlternate = Path.Combine(relocatedRoot, hostOs == "windows" ? "tools/linux-pin" : "tools/windows-pin");
        Directory.CreateDirectory(Path.GetDirectoryName(relocatedAlternate)!);
        await File.WriteAllBytesAsync(relocatedAlternate, alternatePinBytes);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("process-report-relocated-lock", relocatedRoot, "lock"));
        var relocatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", relocatedRoot, "--json");
        AssertEqual(api.StandardOutput, relocatedApi.StandardOutput,
            "The process API projection must remain byte-identical after package relocation.");
        var relocatedAudit = await harness.InvokeCompilerCommandAsync("audit", relocatedRoot, "--json");
        AssertEqual(audit.StandardOutput, relocatedAudit.StandardOutput,
            "The process audit projection must remain byte-identical after package relocation.");

        var build = await harness.InvokePackageDirectoryAsync("process-report-build", packageRoot, "build");
        var managedArtifact = ParseBuiltArtifact(build, "Built executable: ");
        var outputDirectory = Path.GetDirectoryName(managedArtifact)!;
        var processCopyName = ProcessRunnerCopyName("process-report-root");
        var processCopyPath = Path.Combine(outputDirectory, processCopyName);
        AssertTrue(File.Exists(processCopyPath), $"Managed package build should include the pinned runner copy at {processCopyPath}.");
        AssertTrue((await File.ReadAllBytesAsync(processCopyPath)).SequenceEqual(helperBytes),
            "The managed artifact runner copy must be byte-identical to the declared executable.");
        AssertUnixExecutableIfRequired(processCopyPath, "Managed process runner copies must be executable on Linux.");
        var commandSchemaPath = Path.Combine(outputDirectory, "command-schema.json");
        using (var schema = JsonDocument.Parse(await File.ReadAllBytesAsync(commandSchemaPath)))
        {
            AssertEqual(5, schema.RootElement.GetProperty("schema_version").GetInt32(), "Process commands require schema version 5.");
            var capabilities = schema.RootElement.GetProperty("commands")[0].GetProperty("capabilities");
            AssertJsonStringArray(capabilities, ["fs.read", "log.write", "process.spawn"]);
        }
        using (var receipt = await AssertBuildReceiptAsync(
                   outputDirectory,
                   "managed",
                   expectedRuntimeIdentifier: null,
                   [Path.GetRelativePath(outputDirectory, managedArtifact).Replace(Path.DirectorySeparatorChar, '/'), "command-schema.json", processCopyName],
                   packageRoot))
        {
            AssertEqual(4, receipt.RootElement.GetProperty("schema_version").GetInt32(), "Process build receipts use schema version 4.");
            AssertTrue(receipt.RootElement.GetProperty("inputs").EnumerateArray().Any(input =>
                    input.GetProperty("kind").GetString() == "process_executable"
                    && input.GetProperty("sha256").GetString() == helperHash),
                "The build receipt must bind the pinned executable as a checked package input.");
        }

        var unusedPinPackage = await harness.WritePackageAsync(
            "process-unused-pin",
            ProcessCliManifest("process-unused", (hostOs, "tools/runner", helperHash)),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 0; }\n"
            });
        await InstallPinnedExecutableAsync(unusedPinPackage, "tools/runner", helperExecutable);
        var unusedPinBuild = await harness.InvokePackageDirectoryAsync("process-unused-pin-build", unusedPinPackage, "build");
        var unusedPinArtifact = ParseBuiltArtifact(unusedPinBuild, "Built executable: ");
        var unusedPinCopy = Path.Combine(Path.GetDirectoryName(unusedPinArtifact)!, ProcessRunnerCopyName("process-unused"));
        AssertTrue(File.Exists(unusedPinCopy),
            "A configured selected process pin must be copied into a build even when no handler injects ProcessRunner.");
        AssertTrue(HashSha256(await File.ReadAllBytesAsync(unusedPinCopy)) == helperHash,
            "An unused selected pin must retain its declared bytes in the managed artifact.");

        var changedPinBytes = (byte[])alternatePinBytes.Clone();
        changedPinBytes[0] ^= 0x01;
        await File.WriteAllBytesAsync(alternatePath, changedPinBytes);
        var changedManifest = rootManifest.Replace(
            $"{(hostOs == "windows" ? "process_linux_sha256" : "process_windows_sha256")} = \"{alternatePinHash}\"",
            $"{(hostOs == "windows" ? "process_linux_sha256" : "process_windows_sha256")} = \"{HashSha256(changedPinBytes)}\"",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "hob.toml"), changedManifest);
        var staleLock = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertPackageJsonDiagnostic(staleLock, "E_LOCK", "Changing a declared process pin must stale the dependency lock snapshot.");

        await AssertProcessManifestDiagnosticsAsync(harness, helperExecutable, helperHash, ProcessRunnerSources());
    }

    private static async Task TestProcessRunnerManagedAndAot(Harness harness)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new IntegrationTestSkippedException("The ProcessRunner native helper and package smoke test target x64 hosts only.");

        var helperExecutable = await harness.GetProcessFixtureExecutableAsync();
        var helperBytes = await File.ReadAllBytesAsync(helperExecutable);
        var helperHash = HashSha256(helperBytes);
        var hostOs = OperatingSystem.IsWindows() ? "windows" : "linux";
        var packageRoot = await harness.WritePackageAsync(
            "process-runner-runtime",
            ProcessCliManifest("process-runtime", (hostOs, "tools/runner", helperHash)),
            ProcessRunnerSources());
        await InstallPinnedExecutableAsync(packageRoot, "tools/runner", helperExecutable);
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "payload.txt"), "small stdin λ", new UTF8Encoding(false, true));

        var check = await harness.InvokePackageDirectoryAsync("process-runner-runtime-check", packageRoot, "check");
        AssertEqual(0, check.ExitCode, Describe(check));
        var managedBuild = await harness.InvokePackageDirectoryAsync("process-runner-managed-build", packageRoot, "build");
        var managedArtifact = ParseBuiltArtifact(managedBuild, "Built executable: ");
        var managedDirectory = Path.GetDirectoryName(managedArtifact)!;
        var managedCopy = Path.Combine(managedDirectory, ProcessRunnerCopyName("process-runtime"));
        AssertUnixExecutableIfRequired(managedCopy, "The copied managed build runner must preserve executable permissions on Linux.");
        var managedSchema = await File.ReadAllBytesAsync(Path.Combine(managedDirectory, "command-schema.json"));

        var literalOne = "space and ; shell $(literal) `ticks`";
        var literalTwo = "quote \" slash \\ λ";
        var argvRun = await harness.RunManagedArtifactAsync(
            managedArtifact,
            packageRoot,
            "run", "process-test", "--mode", "args", "--first", literalOne, "--second", literalTwo);
        AssertProcessArgumentRun(argvRun, "literal argument handling", literalOne, literalTwo);

        var stdinRun = await harness.RunManagedArtifactAsync(
            managedArtifact, packageRoot, "run", "process-test", "--mode", "stdin");
        AssertProcessRunText("small stdin λ" + Environment.NewLine, stdinRun, "stdin forwarding");

        var stderrRun = await harness.RunManagedArtifactAsync(
            managedArtifact, packageRoot, "run", "process-test", "--mode", "stderr");
        AssertProcessRunText("child stderr λ" + Environment.NewLine, stderrRun, "stderr capture");

        var nonzeroRun = await harness.RunManagedArtifactAsync(
            managedArtifact, packageRoot, "run", "process-test", "--mode", "exit");
        AssertProcessRunText("exit=7" + Environment.NewLine, nonzeroRun, "nonzero child exit");

        var markerName = "HOB_PROCESS_TEST_CANARY";
        var markerValue = "inherited-parent-only";
        var metaRun = await harness.RunManagedArtifactWithEnvironmentAsync(
            managedArtifact,
            packageRoot,
            new Dictionary<string, string>(StringComparer.Ordinal) { [markerName] = markerValue },
            "run", "process-test", "--mode", "meta");
        AssertEqual(0, metaRun.ExitCode, Describe(metaRun));
        using (var meta = JsonDocument.Parse(metaRun.StandardOutput))
        {
            AssertEqual(Path.GetFullPath(managedDirectory), meta.RootElement.GetProperty("cwd").GetString(),
                "The child working directory must be the generated artifact directory.");
            AssertEqual("<missing>", meta.RootElement.GetProperty("marker").GetString(),
                "Pinned children must receive an empty environment rather than inheriting application variables.");
        }
        AssertProcessLoggerLine(metaRun.StandardError, "empty child environment and artifact cwd");

        foreach (var (mode, expectedText) in new[]
                 {
                     ("argc-exact", "127" + Environment.NewLine),
                     ("argv-bytes-exact", "16384" + Environment.NewLine),
                     ("stdin-exact", string.Empty),
                     ("stdout-exact", string.Empty),
                     ("stderr-exact", string.Empty)
                 })
        {
            if (mode == "stdin-exact")
                await File.WriteAllTextAsync(Path.Combine(packageRoot, "payload.txt"), new string('x', 1_048_576), new UTF8Encoding(false, true));
            else if (mode is "stdout-exact" or "stderr-exact")
                await File.WriteAllTextAsync(Path.Combine(packageRoot, "payload.txt"), string.Empty, new UTF8Encoding(false, true));
            var exact = await harness.RunManagedArtifactAsync(managedArtifact, packageRoot, "run", "process-test", "--mode", mode);
            if (mode == "stdin-exact")
                AssertProcessRunText(new string('x', 1_048_576) + Environment.NewLine, exact, "exact stdin and stdout limits");
            else if (mode == "stdout-exact")
                AssertProcessRunText(new string('x', 1_048_576) + Environment.NewLine, exact, "exact stdout limit");
            else if (mode == "stderr-exact")
                AssertProcessRunText(new string('e', 1_048_576) + Environment.NewLine, exact, "exact stderr limit");
            else
                AssertProcessRunText(expectedText, exact, $"{mode} limit");
        }

        await File.WriteAllTextAsync(Path.Combine(packageRoot, "payload.txt"), new string('x', 1_048_577), new UTF8Encoding(false, true));
        AssertProcessRunText("InputTooLarge" + Environment.NewLine,
            await harness.RunManagedArtifactAsync(managedArtifact, packageRoot, "run", "process-test", "--mode", "stdin"),
            "stdin over limit");
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "payload.txt"), string.Empty, new UTF8Encoding(false, true));

        foreach (var mode in new[] { "argc-over", "argv-bytes-over", "stdout-over", "stderr-over", "invalid-utf8" })
        {
            var result = await harness.RunManagedArtifactAsync(managedArtifact, packageRoot, "run", "process-test", "--mode", mode);
            var expected = mode switch
            {
                "argc-over" or "argv-bytes-over" => "InvalidArgument",
                "stdout-over" or "stderr-over" => "OutputTooLarge",
                _ => "InvalidText"
            };
            AssertProcessRunText(expected + Environment.NewLine, result, $"{mode} boundary");
        }
        AssertProcessArgumentRun(
            await harness.RunManagedArtifactAsync(managedArtifact, packageRoot, "run", "process-test", "--mode", "empty-argv"),
            "empty argument list");

        var markerPath = Path.Combine(harness.TemporaryRoot, $"process-timeout-{Guid.NewGuid():N}.pid");
        var timeoutRun = await harness.RunManagedArtifactWithTimeoutAsync(
            managedArtifact,
            packageRoot,
            TimeSpan.FromSeconds(20),
            "run", "process-test", "--mode", "timeout", "--marker", markerPath);
        AssertProcessRunText("TimedOut" + Environment.NewLine, timeoutRun, "fixed process timeout");
        await AssertProcessTreeWasReapedAsync(markerPath);

        var managedProcessCopyHash = HashSha256(await File.ReadAllBytesAsync(managedCopy));
        AssertEqual(helperHash, managedProcessCopyHash, "Managed builds must carry an unchanged pinned process executable.");
        var tamperedCopy = (await File.ReadAllBytesAsync(managedCopy)).ToArray();
        tamperedCopy[0] ^= 0x01;
        await File.WriteAllBytesAsync(managedCopy, tamperedCopy);
        AssertProcessRunText("StartFailed" + Environment.NewLine,
            await harness.RunManagedArtifactAsync(managedArtifact, packageRoot, "run", "process-test", "--mode", "args"),
            "copied runner tamper rejection");
        await File.WriteAllBytesAsync(managedCopy, helperBytes);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(managedCopy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var badExecutableBytes = Encoding.UTF8.GetBytes("not a native executable\n");
        var badPackage = await harness.WritePackageAsync(
            "process-runner-start-failure",
            ProcessCliManifest("process-start-failure", (hostOs, "tools/bad-runner", HashSha256(badExecutableBytes))),
            ProcessRunnerSources());
        var badExecutablePath = Path.Combine(badPackage, "tools", "bad-runner");
        Directory.CreateDirectory(Path.GetDirectoryName(badExecutablePath)!);
        await File.WriteAllBytesAsync(badExecutablePath, badExecutableBytes);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(badExecutablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var badBuild = await harness.InvokePackageDirectoryAsync("process-runner-start-failure-build", badPackage, "build");
        var badArtifact = ParseBuiltArtifact(badBuild, "Built executable: ");
        AssertProcessRunText("StartFailed" + Environment.NewLine,
            await harness.RunManagedArtifactAsync(badArtifact, badPackage, "run", "process-test", "--mode", "args"),
            "incompatible process executable without shell fallback");

        if (OperatingSystem.IsLinux())
        {
            var cancellableMarker = Path.Combine(harness.TemporaryRoot, $"process-cancel-{Guid.NewGuid():N}.pid");
            using var process = harness.StartManagedArtifactProcess(
                managedArtifact,
                packageRoot,
                null,
                "run", "process-test", "--mode", "timeout", "--marker", cancellableMarker);
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await WaitForFileAsync(cancellableMarker, TimeSpan.FromSeconds(5));
            AssertEqual(0, KillWithSignal(process.Id, 2), "Could not send SIGINT to the async command for ProcessRunner cancellation.");
            using var cancellationWait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await process.WaitForExitAsync(cancellationWait.Token);
            await stdoutTask;
            await stderrTask;
            AssertEqual(130, process.ExitCode, "Host cancellation should propagate after the ProcessRunner child tree is reaped.");
            await AssertProcessTreeWasReapedAsync(cancellableMarker);
        }

        var nativeBuild = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "process-runner-native-build", packageRoot, "build", AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        var nativeExecutable = ParseBuiltArtifact(nativeBuild, "Built native executable: ");
        var nativeDirectory = Path.GetDirectoryName(nativeExecutable)!;
        var nativeCopy = Path.Combine(nativeDirectory, ProcessRunnerCopyName("process-runtime"));
        AssertTrue(File.Exists(nativeCopy), $"NativeAOT output must include its process runner copy at {nativeCopy}.");
        AssertUnixExecutableIfRequired(nativeCopy, "NativeAOT process runner copies must be executable on Linux.");
        var nativeSchema = await File.ReadAllBytesAsync(Path.Combine(nativeDirectory, "command-schema.json"));
        AssertTrue(managedSchema.SequenceEqual(nativeSchema), "Managed and NativeAOT process command schemas must be byte-identical.");
        var nativeRun = await ExecuteNativeWithEnvironmentAsync(
            nativeExecutable,
            TimeSpan.FromSeconds(30),
            new Dictionary<string, string>(StringComparer.Ordinal) { [markerName] = markerValue },
            "run", "process-test", "--mode", "args", "--first", literalOne, "--second", literalTwo);
        AssertProcessArgumentRun(nativeRun, "NativeAOT literal argument handling", literalOne, literalTwo);
        using (var nativeReceipt = await AssertBuildReceiptAsync(
                   nativeDirectory,
                   "native_aot",
                   CurrentHostAotRid(),
                   [Path.GetRelativePath(nativeDirectory, nativeExecutable).Replace(Path.DirectorySeparatorChar, '/'), "command-schema.json", Path.GetFileName(nativeCopy)],
                   packageRoot))
            AssertTrue(nativeReceipt.RootElement.GetProperty("inputs").EnumerateArray().Any(input =>
                    input.GetProperty("kind").GetString() == "process_executable"
                    && input.GetProperty("sha256").GetString() == helperHash),
                "NativeAOT receipts must bind the declared process executable input.");
    }

    private static async Task AssertProcessManifestDiagnosticsAsync(
        Harness harness,
        string helperExecutable,
        string helperHash,
        IReadOnlyDictionary<string, string> sourceFiles)
    {
        var hostOs = OperatingSystem.IsWindows() ? "windows" : "linux";
        var processKey = hostOs == "windows" ? "process_windows_path" : "process_linux_path";
        var hashKey = hostOs == "windows" ? "process_windows_sha256" : "process_linux_sha256";
        var processPair = (hostOs, "tools/runner", helperHash);
        var validManifest = ProcessCliManifest("process-diagnostic", processPair);

        foreach (var (name, manifest, code) in new[]
                 {
                     ("incomplete", CliPackageManifest() + $"{processKey} = \"tools/runner\"\n", "E_MANIFEST"),
                     ("malformed-hash", CliPackageManifest() + $"{processKey} = \"tools/runner\"\n{hashKey} = \"BAD\"\n\n[capabilities]\nprocess.spawn = \"allow\"\n", "E_MANIFEST"),
                     ("traversal", ProcessCliManifest("process-traversal", (hostOs, "../runner", helperHash)), "E_MANIFEST"),
                     ("missing-grant", ProcessCliManifest("process-no-grant", processPair, includeProcessGrant: false), "E_MANIFEST"),
                     ("missing-pin", CliPackageManifest() + "\n[capabilities]\nprocess.spawn = \"allow\"\n", "E_PROCESS_EXECUTABLE"),
                     ("wrong-grant", ProcessCliManifest("process-wrong-grant", processPair, processGrantValue: "deny"), "E_MANIFEST"),
                     ("wrong-section", CliPackageManifest() + "\n[capabilities]\nprocess.spawn = \"allow\"\n" + $"{processKey} = \"tools/runner\"\n{hashKey} = \"{helperHash}\"\n", "E_MANIFEST"),
                 })
        {
            var result = await harness.InvokePackageAsync(
                $"process-manifest-{name}", "check", manifest, sourceFiles, "--json");
            AssertPackageJsonDiagnostic(result, code, $"Process manifest case '{name}' must report {code}.");
        }

        var missingFilePackage = await harness.WritePackageAsync(
            "process-manifest-missing-file", ProcessCliManifest("process-missing-file", (hostOs, "tools/missing", helperHash)), sourceFiles);
        AssertPackageJsonDiagnostic(
            await harness.InvokePackageDirectoryAsync("process-manifest-missing-file-check", missingFilePackage, "check", "--json"),
            "E_PROCESS_EXECUTABLE", "A declared process pin must resolve to an existing file.");

        var mismatchPackage = await harness.WritePackageAsync(
            "process-manifest-mismatched-hash", ProcessCliManifest("process-mismatch", (hostOs, "tools/runner", new string('0', 64))), sourceFiles);
        await InstallPinnedExecutableAsync(mismatchPackage, "tools/runner", helperExecutable);
        AssertPackageJsonDiagnostic(
            await harness.InvokePackageDirectoryAsync("process-manifest-mismatched-hash-check", mismatchPackage, "check", "--json"),
            "E_PROCESS_EXECUTABLE", "A process pin with a mismatched SHA-256 must be rejected.");

        var unsupportedOrder = ProcessRunnerTestSource().Replace(
            "fs: FsRead, logger: Logger, runner: ProcessRunner",
            "fs: FsRead, runner: ProcessRunner, logger: Logger",
            StringComparison.Ordinal);
        AssertTrue(unsupportedOrder != ProcessRunnerTestSource(), "The ProcessRunner capability order fixture must be modified.");
        var orderFiles = sourceFiles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        orderFiles["src/handlers.hob"] = unsupportedOrder;
        var orderPackage = await harness.WritePackageAsync(
            "process-manifest-wrong-order", validManifest, orderFiles);
        await InstallPinnedExecutableAsync(orderPackage, "tools/runner", helperExecutable);
        AssertPackageJsonDiagnostic(
            await harness.InvokePackageDirectoryAsync("process-manifest-wrong-order-check", orderPackage, "check", "--json"),
            "E_COMMAND_HANDLER", "ProcessRunner must be injected after Logger in command handler order.");

        const string noGrantSource = """
            module handlers;
            pub union RunError { Failed }
            pub async fn run(args: self::app::main::RunArgs, runner: ProcessRunner) -> Result<Text, self::handlers::RunError> effects { process.spawn } {
                let result: Result<ProcessOutput, ProcessError> = await runner.run_text_async([], "");
                return match result { Ok(output) => Ok(output.stdout), Err(error) => Ok("process-error") };
            }
            pub fn describe(error: self::handlers::RunError) -> Text effects {} { return match error { self::handlers::RunError.Failed => "failed" }; }
            """;
        var noGrantPackage = await harness.WritePackageAsync(
            "process-semantic-missing-grant",
            CliPackageManifest("app::main"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; command run { help \"Run a process.\"; argument label: Text help \"Invocation label.\"; handler: self::handlers::run; error: self::handlers::describe; }\n",
                ["src/handlers.hob"] = noGrantSource
            });
        AssertPackageJsonDiagnostic(
            await harness.InvokePackageDirectoryAsync("process-semantic-missing-grant-check", noGrantPackage, "check", "--json"),
            "E_CAPABILITY_MISSING", "ProcessRunner use without a process.spawn grant must be rejected by source checking.");

        var unawaitedSource = ProcessRunnerTestSource().Replace(
            "return await runner.run_text_async(arguments, stdin);",
            "return runner.run_text_async(arguments, stdin);",
            StringComparison.Ordinal);
        var unawaitedFiles = new Dictionary<string, string>(sourceFiles, StringComparer.Ordinal)
        {
            ["src/handlers.hob"] = unawaitedSource[unawaitedSource.IndexOf("module handlers;", StringComparison.Ordinal)..]
        };
        var unawaitedPackage = await harness.WritePackageAsync(
            "process-semantic-unawaited", validManifest, unawaitedFiles);
        await InstallPinnedExecutableAsync(unawaitedPackage, "tools/runner", helperExecutable);
        AssertPackageJsonDiagnostic(
            await harness.InvokePackageDirectoryAsync("process-semantic-unawaited-check", unawaitedPackage, "check", "--json"),
            "E_ASYNC_CALL_UNAWAITED", "ProcessRunner async calls must be awaited.");

        var wrongTypeSource = ProcessRunnerTestSource().Replace(
            "[\"args\", args.first, args.second]",
            "[\"args\", 1, args.second]",
            StringComparison.Ordinal);
        var wrongTypeFiles = sourceFiles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        wrongTypeFiles["src/handlers.hob"] = wrongTypeSource;
        var wrongTypePackage = await harness.WritePackageAsync(
            "process-semantic-wrong-type", validManifest, wrongTypeFiles);
        await InstallPinnedExecutableAsync(wrongTypePackage, "tools/runner", helperExecutable);
        AssertPackageJsonDiagnostic(
            await harness.InvokePackageDirectoryAsync("process-semantic-wrong-type-check", wrongTypePackage, "check", "--json"),
            "E_TYPE_MISMATCH", "ProcessRunner argument lists must be typed as List<Text>.");

        var libraryPackage = await harness.WritePackageAsync(
            "process-library-scope",
            LibraryPackageManifest("process-library-scope"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/lib.hob"] = "module lib; pub fn escape(runner: ProcessRunner) -> i32 effects {} { return 1; }\n"
            });
        AssertPackageJsonDiagnostic(
            await harness.InvokePackageDirectoryAsync("process-library-scope-check", libraryPackage, "check", "--json"),
            "E_CAPABILITY_SCOPE", "ProcessRunner types must stay out of libraries.");

        if (OperatingSystem.IsLinux())
        {
            var nonExecutablePackage = await harness.WritePackageAsync(
                "process-linux-non-executable",
                ProcessCliManifest("process-linux-mode", processPair),
                sourceFiles);
            await InstallPinnedExecutableAsync(nonExecutablePackage, "tools/runner", helperExecutable);
            File.SetUnixFileMode(
                Path.Combine(nonExecutablePackage, "tools", "runner"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            AssertPackageJsonDiagnostic(
                await harness.InvokePackageDirectoryAsync("process-linux-non-executable-check", nonExecutablePackage, "check", "--json"),
                "E_PROCESS_EXECUTABLE", "A selected Linux pin must have an execute permission bit.");
        }

        var symlinkPackage = await harness.WritePackageAsync(
            "process-symlink-pin", ProcessCliManifest("process-symlink", processPair), sourceFiles);
        var symlinkPath = Path.Combine(symlinkPackage, "tools", "runner");
        Directory.CreateDirectory(Path.GetDirectoryName(symlinkPath)!);
        try
        {
            File.CreateSymbolicLink(symlinkPath, helperExecutable);
            AssertPackageJsonDiagnostic(
                await harness.InvokePackageDirectoryAsync("process-symlink-pin-check", symlinkPackage, "check", "--json"),
                "E_PROCESS_EXECUTABLE", "A process pin must not follow a symbolic link.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException or NotSupportedException)
        {
            // Symbolic-link creation is not available in some Windows environments without Developer Mode.
        }
    }

    private static void AssertConfigCliLog(string stderr, string context)
    {
        var lines = stderr.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        AssertEqual(1, lines.Length, $"{context}: Logger.info should emit exactly one JSON line.");
        using var document = JsonDocument.Parse(lines[0]);
        AssertJsonPropertyOrder(document.RootElement, "level,event,detail");
        AssertEqual("info", document.RootElement.GetProperty("level").GetString(), $"{context}: logger level mismatch.");
        AssertEqual("boot\nready", document.RootElement.GetProperty("event").GetString(), $"{context}: logger event escaping mismatch.");
        AssertEqual("quote: \" tab:\t slash:\\", document.RootElement.GetProperty("detail").GetString(),
            $"{context}: logger detail escaping mismatch.");
    }

    private static async Task TestCommandContextualIdentifiers(Harness harness)
    {
        const string main = """
            module app::main;
            command route {
                help "Route a value.";
                argument return: Text help "Value to route.";
                option struct: i32 = 1 help "Contextual integer.";
                flag assert help "Contextual flag.";
                handler: self::handlers::route;
                error: self::handlers::describe;
            }
            """;
        const string handlers = """
            module handlers;
            pub union RouteError { Failed }
            pub fn route(args: self::app::main::RouteArgs) -> Result<Text, self::handlers::RouteError> effects {} {
                let amount: i32 = args.struct;
                if args.assert { return Ok(args.return); }
                return Ok(args.return);
            }
            pub fn describe(error: self::handlers::RouteError) -> Text effects {} {
                return match error { self::handlers::RouteError.Failed => "route failed" };
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "typed-cli-contextual-identifiers",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = main,
                ["src/handlers.hob"] = handlers
            });

        var check = await harness.InvokePackageDirectoryAsync("typed-cli-contextual-check", packageRoot, "check");
        AssertEqual(0, check.ExitCode, Describe(check));
        var run = await harness.InvokePackageDirectoryAsync("typed-cli-contextual-run", packageRoot, "run", "--", "route", "contextual", "--assert");
        AssertRunOutput("contextual" + Environment.NewLine, run);
        var leadingDash = await harness.InvokePackageDirectoryAsync(
            "typed-cli-contextual-leading-dash", packageRoot, "run", "--", "route", "--", "--leading");
        AssertRunOutput("--leading" + Environment.NewLine, leadingDash);

        const string standalone = """
            module standalone;
            pub union EchoError { Failed }
            command echo {
                help "Echo one value.";
                argument value: Text help "Value to echo.";
                handler: self::standalone::run;
                error: self::standalone::describe;
            }
            pub fn run(args: self::standalone::EchoArgs) -> Result<Text, self::standalone::EchoError> effects {} {
                return Ok(args.value);
            }
            pub fn describe(error: self::standalone::EchoError) -> Text effects {} {
                return match error { self::standalone::EchoError.Failed => "echo failed" };
            }
            """;
        var standaloneRun = await harness.InvokeAsync(
            "typed-cli-standalone-file-run",
            "run",
            standalone,
            "--",
            "echo",
            "--",
            "--leading");
        AssertRunOutput("--leading" + Environment.NewLine, standaloneRun);

        const string legacyMain = "module standalone_main; pub fn main() -> Text effects {} { return \"legacy\"; }";
        var legacyMainWithSeparator = await harness.InvokeAsync(
            "typed-cli-legacy-main-separator",
            "run",
            legacyMain,
            "--",
            "unexpected");
        AssertEqual(2, legacyMainWithSeparator.ExitCode, Describe(legacyMainWithSeparator));
        AssertEqual(string.Empty, legacyMainWithSeparator.StandardOutput, Describe(legacyMainWithSeparator));
        AssertTrue(legacyMainWithSeparator.StandardError.StartsWith("Usage: hob ", StringComparison.Ordinal),
            "A separator must remain invalid for legacy main-based source execution.");
    }

    private static async Task TestTypedCliCommandDiagnostics(Harness harness)
    {
        const string main = """
            module app::main;
            command scan {
                help "Scan a path.";
                argument input: FilePath help "Path to scan.";
                option limit: i32 = 1 help "Scan limit.";
                flag recursive help "Scan recursively.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string handlers = """
            module handlers;
            pub union ScanError { Failed }
            pub fn run(args: self::app::main::ScanArgs) -> Result<Text, self::handlers::ScanError> effects {} { return Ok("done"); }
            pub fn describe(error: self::handlers::ScanError) -> Text effects {} {
                return match error { self::handlers::ScanError.Failed => "scan failed" };
            }
            """;

        async Task ExpectError(string caseName, string mainSource, string handlerSource, string code) =>
            await ExpectPackageJsonDiagnosticAsync(
                harness,
                caseName,
                CliPackageManifest(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = mainSource,
                    ["src/handlers.hob"] = handlerSource
                },
                code,
                "src/app/main.hob");

        await ExpectError("typed-cli-duplicate-entry", main.Replace(
            "argument input: FilePath help \"Path to scan.\";",
            "argument input: FilePath help \"Path to scan.\";\n    argument input: Text help \"Second input.\";",
            StringComparison.Ordinal), handlers, "E_COMMAND_DECL");

        await ExpectError("typed-cli-duplicate-option", main.Replace(
            "option limit: i32 = 1 help \"Scan limit.\";",
            "option limit: i32 = 1 help \"Scan limit.\";\n    option limit: i32 = 2 help \"Second limit.\";",
            StringComparison.Ordinal), handlers, "E_COMMAND_DECL");

        await ExpectError("typed-cli-missing-entry", main.Replace(
            "error: self::handlers::describe;\n", string.Empty, StringComparison.Ordinal), handlers, "E_COMMAND_DECL");

        await ExpectError("typed-cli-unsupported-type", main.Replace(
            "argument input: FilePath", "argument input: bool", StringComparison.Ordinal), handlers, "E_COMMAND_DECL");

        await ExpectError("typed-cli-wrong-default", main.Replace(
            "option limit: i32 = 1", "option limit: i32 = \"one\"", StringComparison.Ordinal), handlers, "E_COMMAND_DECL");

        await ExpectError("typed-cli-empty-filepath-default", main.Replace(
            "option limit: i32 = 1 help \"Scan limit.\";",
            "option root: FilePath = \"\" help \"Root path.\";\n    option limit: i32 = 1 help \"Scan limit.\";",
            StringComparison.Ordinal), handlers, "E_COMMAND_DECL");

        await ExpectError("typed-cli-nul-filepath-default", main.Replace(
            "option limit: i32 = 1 help \"Scan limit.\";",
            "option root: FilePath = \"\\0\" help \"Root path.\";\n    option limit: i32 = 1 help \"Scan limit.\";",
            StringComparison.Ordinal), handlers, "E_COMMAND_DECL");

        await ExpectError("typed-cli-no-positional-arguments", main.Replace(
            "argument input: FilePath help \"Path to scan.\";\n", string.Empty, StringComparison.Ordinal), handlers, "E_COMMAND_DECL");

        await ExpectError("typed-cli-duplicate-command", main + "\n" + main[(main.IndexOf("command scan", StringComparison.Ordinal))..], handlers, "E_COMMAND_DECL");

        await ExpectError("typed-cli-main-conflict", main + "\npub fn main() -> i32 effects {} { return 0; }\n", handlers, "E_COMMAND_DECL");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "typed-cli-library-command",
            LibraryPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = main,
                ["src/handlers.hob"] = handlers
            },
            "E_COMMAND_DECL",
            "src/app/main.hob");

        var wrongHandler = handlers.Replace(
            "-> Result<Text, self::handlers::ScanError> effects {} { return Ok(\"done\"); }",
            "-> Text effects {} { return \"done\"; }",
            StringComparison.Ordinal);
        await ExpectPackageJsonDiagnosticAtAsync(
            harness,
            "typed-cli-wrong-handler-signature",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = main,
                ["src/handlers.hob"] = wrongHandler
            },
            "E_COMMAND_HANDLER",
            "src/app/main.hob",
            main,
            "self");

        var wrongFormatter = handlers.Replace(
            "pub fn describe(error: self::handlers::ScanError) -> Text effects {} {\n    return match error { self::handlers::ScanError.Failed => \"scan failed\" };\n}",
            "pub fn describe(error: Text) -> Text effects {} { return error; }",
            StringComparison.Ordinal);
        await ExpectError("typed-cli-wrong-formatter-signature", main, wrongFormatter, "E_COMMAND_HANDLER");

        var effectfulFormatter = handlers.Replace(
            "pub fn describe(error: self::handlers::ScanError) -> Text effects {} {",
            "pub fn describe(error: self::handlers::ScanError) -> Text effects { fs.read } {",
            StringComparison.Ordinal);
        await ExpectError("typed-cli-effectful-formatter", main, effectfulFormatter, "E_COMMAND_HANDLER");

        var privateHandler = handlers.Replace("pub fn run(", "fn run(", StringComparison.Ordinal);
        await ExpectError("typed-cli-private-handler", main, privateHandler, "E_ACCESS_PRIVATE");

        var malformed = main.Replace("help \"Scan a path.\";", "help \"Scan a path.\"", StringComparison.Ordinal);
        var malformedPackage = await harness.WritePackageAsync(
            "typed-cli-malformed-parser-entry",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = malformed,
                ["src/handlers.hob"] = handlers
            });
        var parserFailure = await harness.InvokePackageDirectoryAsync("typed-cli-malformed-parser-check", malformedPackage, "check", "--json");
        AssertTrue(parserFailure.ExitCode != 0, Describe(parserFailure));
        using var parserDocument = JsonDocument.Parse(parserFailure.StandardOutput);
        AssertTrue(parserDocument.RootElement.GetProperty("diagnostics").EnumerateArray().Any(diagnostic =>
            diagnostic.GetProperty("code").GetString() == "E_COMMAND_DECL"
            && Path.GetFullPath(diagnostic.GetProperty("file").GetString() ?? string.Empty)
                == Path.GetFullPath(Path.Combine(malformedPackage, "src", "app", "main.hob"))),
            $"Malformed command syntax should return a source-located E_COMMAND_DECL diagnostic. {parserFailure.StandardOutput}");
    }

    private static void AssertCliParseFailure(ProcessResult result, string code)
    {
        AssertEqual(2, result.ExitCode, Describe(result));
        AssertEqual(string.Empty, result.StandardOutput, Describe(result));
        AssertTrue(result.StandardError.Contains(code, StringComparison.Ordinal),
            $"Expected stable CLI diagnostic {code}. {Describe(result)}");
    }

    private static async Task TestPackagePrivateNameIsolation(Harness harness)
    {
        const string first = """
            module first;
            struct Hidden { value: i32 }
            pub fn first_value() -> i32 effects {} {
                let item: self::first::Hidden = self::first::Hidden { value: 19 };
                return item.value;
            }
            """;
        const string second = """
            module second;
            struct Hidden { value: i32 }
            pub fn second_value() -> i32 effects {} {
                let item: self::second::Hidden = self::second::Hidden { value: 23 };
                return item.value;
            }
            """;
        const string main = """
            module app::main;
            pub fn main() -> i32 effects {} { return self::first::first_value() + self::second::second_value(); }
            """;

        var result = await harness.InvokePackageAsync(
            "package-private-name-isolation",
            "run",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/first.hob"] = first,
                ["src/second.hob"] = second,
                ["src/app/main.hob"] = main
            });
        AssertRunOutput("42" + Environment.NewLine, result);
    }

    private static async Task TestQualifiedPrivateTypeAccessPaths(Harness harness)
    {
        const string hiddenLibrary = """
            module library;
            struct HiddenStruct { value: i32 }
            union HiddenUnion { Value(i32) }
            """;

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "private-struct-qualified-type-access",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.hob"] = hiddenLibrary,
                ["src/app/main.hob"] = """
                    module app::main;
                    fn read_hidden(value: self::library::HiddenStruct) -> i32 effects {} { return value.value; }
                    pub fn main() -> i32 effects {} { return 0; }
                    """
            },
            "E_ACCESS_PRIVATE",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "private-struct-qualified-construction",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.hob"] = hiddenLibrary,
                ["src/app/main.hob"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} {
                        return self::library::HiddenStruct { value: 7 }.value;
                    }
                    """
            },
            "E_ACCESS_PRIVATE",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "private-union-qualified-constructor",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.hob"] = hiddenLibrary,
                ["src/app/main.hob"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} {
                        let value: i32 = self::library::HiddenUnion.Value(7);
                        return value;
                    }
                    """
            },
            "E_ACCESS_PRIVATE",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "private-union-qualified-pattern",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.hob"] = hiddenLibrary,
                ["src/app/main.hob"] = """
                    module app::main;
                    pub union VisibleUnion { Empty }
                    pub fn main() -> i32 effects {} {
                        let choice: self::app::main::VisibleUnion = self::app::main::VisibleUnion.Empty;
                        return match choice {
                            self::library::HiddenUnion.Value(value) => value,
                            _ => 0,
                        };
                    }
                    """
            },
            "E_ACCESS_PRIVATE",
            "src/app/main.hob");
    }

    private static async Task TestPackageQualifiedReferenceDiagnostics(Harness harness)
    {
        const string library = """
            module library;
            pub fn present() -> i32 effects {} { return 7; }
            fn hidden() -> i32 effects {} { return 9; }
            """;

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-qualified-private-reference",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.hob"] = library,
                ["src/app/main.hob"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} { return self::library::hidden(); }
                    """
            },
            "E_ACCESS_PRIVATE",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-qualified-unknown-module",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} { return self::absent::value(); }
                    """
            },
            "E_NAME_UNRESOLVED",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-qualified-unknown-declaration",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.hob"] = library,
                ["src/app/main.hob"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} { return self::library::absent(); }
                    """
            },
            "E_NAME_UNRESOLVED",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-unqualified-user-function",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module app::main;
                    fn helper() -> i32 effects {} { return 7; }
                    pub fn main() -> i32 effects {} { return helper(); }
                    """
            },
            "E_NAME_UNRESOLVED",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-unqualified-user-type",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module app::main;
                    struct Value { value: i32 }
                    pub fn main(item: Value) -> i32 effects {} { return item.value; }
                    """
            },
            "E_NAME_UNRESOLVED",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-legacy-import-rejected",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module app::main;
                    import library { present };
                    pub fn main() -> i32 effects {} { return 1; }
                    """
            },
            "E_SYNTAX",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-dotted-module-rejected",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module app.main;
                    pub fn main() -> i32 effects {} { return 1; }
                    """
            },
            "E_SYNTAX",
            "src/app/main.hob");
    }

    private static async Task TestPackageManifestDiagnostics(Harness harness)
    {
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-missing-entry",
            "name = \"manifest-test\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "hob.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-dotted-entry",
            CliPackageManifest().Replace("entry_module = \"app::main\"", "entry_module = \"app.main\"", StringComparison.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "hob.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-reserved-self-alias",
            CliPackageManifest() + "\n[dependencies]\nself = \"../library\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "hob.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-unknown-key",
            CliPackageManifest() + "dependencies = \"none\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "hob.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-duplicate-key",
            CliPackageManifest() + "kind = \"lib\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "hob.toml");

        foreach (var reservedPath in new[] { "../CON.lib", "../NUL" })
        {
            await ExpectPackageJsonDiagnosticAsync(
                harness,
                $"package-manifest-reserved-dependency-path-{reservedPath.Replace('/', '-').Replace('.', '-')}",
                CliPackageManifest() + $"\n[dependencies]\nvalidation = \"{reservedPath}\"\n",
                new Dictionary<string, string>(StringComparer.Ordinal),
                "E_MANIFEST",
                "hob.toml");
        }

        var ordinaryDeviceLikePaths = await harness.WritePackageAsync(
            "package-manifest-device-like-dependency-paths",
            CliPackageManifest() + "\n[dependencies]\nconsole = \"../CONSOLE\"\nnullish = \"../NULL\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
            });
        var ordinaryDeviceLikeCheck = await harness.InvokePackageDirectoryAsync(
            "package-manifest-device-like-dependency-paths-check",
            ordinaryDeviceLikePaths,
            "check",
            "--json");
        AssertTrue(ordinaryDeviceLikeCheck.ExitCode != 0, Describe(ordinaryDeviceLikeCheck));
        var ordinaryDeviceLikeDiagnostics = ParseDiagnosticSnapshots(ordinaryDeviceLikeCheck.StandardOutput);
        AssertTrue(ordinaryDeviceLikeDiagnostics.Any(diagnostic => diagnostic.Code == "E_DEPENDENCY")
            && ordinaryDeviceLikeDiagnostics.All(diagnostic => diagnostic.Code != "E_MANIFEST"),
            $"Ordinary names adjacent to reserved device names must pass manifest path validation. {ordinaryDeviceLikeCheck.StandardOutput}");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-library-entry",
            LibraryPackageManifest() + "entry_module = \"app::main\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "hob.toml");

        var commentManifest = CliPackageManifest()
            .Replace("version = \"0.1.0\"", "version = \"0.1.0#candidate\"", StringComparison.Ordinal)
            .Replace("kind = \"cli\"", "kind = \"cli\" # CLI package", StringComparison.Ordinal);
        var commentPackage = await harness.WritePackageAsync(
            "package-manifest-comments",
            commentManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
            });
        var commentCheck = await harness.InvokePackageDirectoryAsync(
            "package-manifest-comments-check",
            commentPackage,
            "check");
        AssertEqual(0, commentCheck.ExitCode, Describe(commentCheck));
        AssertEqual(string.Empty, commentCheck.StandardError, Describe(commentCheck));
    }

    private static async Task TestPackageModulePathDiagnostic(Harness harness)
    {
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-module-path-mismatch",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module elsewhere::main;
                    pub fn main() -> i32 effects {} { return 1; }
                    """
            },
            "E_MODULE_PATH",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-module-dotted-filename",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/text.validation.hob"] = "module text::validation; pub fn main() -> i32 effects {} { return 1; }"
            },
            "E_MODULE_PATH",
            "src/text.validation.hob");
    }

    private static async Task TestPackageEntryPointDiagnostics(Harness harness)
    {
        await ExpectPackageTextDiagnosticAsync(
            harness,
            "package-entry-module-missing",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/other.hob"] = "module other; pub fn main() -> i32 effects {} { return 4; }"
            },
            "run",
            "E_ENTRYPOINT",
            "hob.toml");

        await ExpectPackageTextDiagnosticAsync(
            harness,
            "package-entry-main-missing",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn helper() -> i32 effects {} { return 1; }",
                ["src/other.hob"] = "module other; pub fn main() -> i32 effects {} { return 4; }"
            },
            "run",
            "E_ENTRYPOINT",
            "src/app/main.hob");

        await ExpectPackageTextDiagnosticAsync(
            harness,
            "package-entry-main-invalid",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main(value: i32) -> i32 effects {} { return value; }"
            },
            "run",
            "E_ENTRYPOINT",
            "src/app/main.hob");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-entry-main-duplicate",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} { return 1; }
                    pub fn main() -> i32 effects {} { return 2; }
                    """
            },
            "E_NAME_DUPLICATE",
            "src/app/main.hob");
    }

    private static async Task TestPackageEntrySelection(Harness harness)
    {
        var result = await harness.InvokePackageAsync(
            "package-entry-selection",
            "run",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 17; }",
                ["src/other.hob"] = "module other; pub fn main() -> i32 effects {} { return 99; }"
            });
        AssertRunOutput("17" + Environment.NewLine, result);
    }

    private static async Task TestPackageQualifiedUnionExhaustiveness(Harness harness)
    {
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-qualified-union-exhaustiveness",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/shared/choice.hob"] = """
                    module shared::choice;
                    pub union Choice { First, Second, Third }
                    """,
                ["src/app/main.hob"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} {
                        let choice: self::shared::choice::Choice = self::shared::choice::Choice.First;
                        return match choice {
                            self::shared::choice::Choice.First => 1,
                            self::shared::choice::Choice.Second => 2,
                        };
                    }
                    """
            },
            "E_MATCH_NONEXHAUSTIVE",
            "src/app/main.hob");
    }

    private static async Task TestPackageQualifiedReferencesCrossModules(Harness harness)
    {
        var packageRoot = await harness.WritePackageAsync(
            "package-qualified-cross-module",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/base.hob"] = "module base; pub fn answer() -> i32 effects {} { return 42; }",
                ["src/middle.hob"] = """
                    module middle;
                    pub fn wrapped() -> i32 effects {} { return self::base::answer(); }
                    """,
                ["src/app/main.hob"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} { return self::middle::wrapped() + self::base::answer(); }
                    """
            });
        var check = await harness.InvokePackageDirectoryAsync("package-qualified-cross-module-check", packageRoot, "check");
        AssertEqual(0, check.ExitCode, Describe(check));
        var run = await harness.InvokePackageDirectoryAsync("package-qualified-cross-module-run", packageRoot, "run");
        AssertRunOutput("84" + Environment.NewLine, run);
    }

    private static async Task TestGenericStructDependency(Harness harness)
    {
        var packageRoot = await harness.WritePackageGraphAsync(
            "generic-struct-dependency",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\nmodel = \"../model\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = """
                            module app::main;
                            pub fn main() -> i32 effects {} {
                                let inner: model::types::Box<i32> = model::types::Box<i32> { value: 19 };
                                let outer: model::types::Box<model::types::Box<i32>> =
                                    model::types::Box<model::types::Box<i32>> { value: inner };
                                return model::types::get(outer.value);
                            }
                            """
                    }),
                ["model"] = new PackageFixture(
                    LibraryPackageManifest("generic-model"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/types.hob"] = """
                            module types;
                            pub struct Box<T> { value: T }
                            pub fn make<T>(value: T) -> self::types::Box<T> effects {} {
                                return self::types::Box<T> { value: value };
                            }
                            pub fn get<T>(value: self::types::Box<T>) -> T effects {} {
                                return value.value;
                            }
                            """
                    })
            });

        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "generic-struct-dependency-lock", packageRoot, "lock"));
        var check = await harness.InvokePackageDirectoryAsync(
            "generic-struct-dependency-check", packageRoot, "check");
        AssertEqual(0, check.ExitCode, Describe(check));
        var run = await harness.InvokePackageDirectoryAsync(
            "generic-struct-dependency-run", packageRoot, "run");
        AssertRunOutput("19" + Environment.NewLine, run);
    }

    private static async Task TestPackageLibraryBuild(Harness harness)
    {
        var packageRoot = await harness.WritePackageAsync(
            "package-library-build",
            LibraryPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/core/math.hob"] = "module core::math; pub fn square(value: i32) -> i32 effects {} { return value * value; }"
            });
        var check = await harness.InvokePackageDirectoryAsync("package-library-check", packageRoot, "check");
        AssertEqual(0, check.ExitCode, Describe(check));
        var build = await harness.InvokePackageDirectoryAsync("package-library-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        AssertTrue(build.StandardOutput.StartsWith("Built library: ", StringComparison.Ordinal), Describe(build));
        var artifact = build.StandardOutput["Built library: ".Length..].Trim();
        AssertTrue(Path.IsPathFullyQualified(artifact), $"Expected a full managed library path. {Describe(build)}");
        AssertTrue(File.Exists(artifact), $"Expected package library artifact at {artifact}. {Describe(build)}");
    }

    private static async Task TestProjectWorkflow(Harness harness)
    {
        var templateWorkspace = Path.Combine(harness.TemporaryRoot, "project-workflow-templates");
        Directory.CreateDirectory(templateWorkspace);
        foreach (var (kind, name) in new[]
                 {
                     ("lib", "created-library"),
                     ("cli", "created-cli"),
                     ("web", "created-web")
                 })
        {
            var create = await harness.InvokeCompilerCommandAtDirectoryWithEnvironmentAsync(
                templateWorkspace, null, "new", kind, name);
            AssertEqual(0, create.ExitCode, Describe(create));
            var packageRoot = Path.Combine(templateWorkspace, name);
            AssertTrue(Directory.Exists(packageRoot), $"hob new {kind} should create {packageRoot}.");

            var manifestPath = Path.Combine(packageRoot, "hob.toml");
            var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
            AssertTrue(manifestBytes.Length < 3 ||
                       !(manifestBytes[0] == 0xEF && manifestBytes[1] == 0xBB && manifestBytes[2] == 0xBF),
                "Generated manifests must be UTF-8 without a byte-order mark.");
            var manifestText = Encoding.UTF8.GetString(manifestBytes);
            AssertTrue(manifestText.Contains($"kind = \"{kind}\"", StringComparison.Ordinal),
                $"Generated {kind} manifest must retain its package kind.");

            var sources = Directory.EnumerateFiles(Path.Combine(packageRoot, "src"), "*.hob", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            AssertTrue(sources.Length > 0, $"Generated {kind} package must include source.");
            foreach (var sourcePath in sources)
            {
                var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
                AssertTrue(sourceBytes.Length < 3 ||
                           !(sourceBytes[0] == 0xEF && sourceBytes[1] == 0xBB && sourceBytes[2] == 0xBF),
                    "Generated sources must be UTF-8 without a byte-order mark.");
                var source = Encoding.UTF8.GetString(sourceBytes);
                AssertTrue(source.Contains("test \"", StringComparison.Ordinal),
                    $"Generated {kind} source must include at least one language test.");
                if (kind == "web")
                    AssertTrue(source.Contains("route GET \"/health\"", StringComparison.Ordinal),
                        "The generated web source must declare its health route.");
            }
            if (kind == "web")
                AssertTrue(manifestText.Contains("net.listen = \"allow\"", StringComparison.Ordinal),
                    "The generated web manifest must grant net.listen.");

            var check = await harness.InvokePackageDirectoryAsync($"new-{kind}-check", packageRoot, "check");
            AssertEqual(0, check.ExitCode, Describe(check));
            var tests = await harness.InvokePackageDirectoryAsync($"new-{kind}-test", packageRoot, "test");
            AssertEqual(0, tests.ExitCode, Describe(tests));
            AssertTrue(tests.StandardOutput.Contains("1 passed, 0 failed", StringComparison.Ordinal),
                $"Generated {kind} package must run its language test. {Describe(tests)}");
        }

        var overwriteParent = Path.Combine(harness.TemporaryRoot, "project-workflow-overwrite");
        var existingTarget = Path.Combine(overwriteParent, "kept");
        Directory.CreateDirectory(existingTarget);
        var sentinelPath = Path.Combine(existingTarget, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "keep this file");
        var refusedOverwrite = await harness.InvokeCompilerCommandAtDirectoryWithEnvironmentAsync(
            overwriteParent, null, "new", "cli", "kept");
        AssertEqual(1, refusedOverwrite.ExitCode, Describe(refusedOverwrite));
        AssertEqual("keep this file", await File.ReadAllTextAsync(sentinelPath),
            "hob new must preserve an existing target directory exactly.");
        AssertTrue(!File.Exists(Path.Combine(existingTarget, "hob.toml")),
            "A refused project creation must not partially populate the existing target.");
        AssertEqual(0, Directory.EnumerateDirectories(overwriteParent, ".kept.hob-new-*").Count(),
            "A refused project creation must not leave staging directories.");

        var invalidName = await harness.InvokeCompilerCommandAtDirectoryWithEnvironmentAsync(
            overwriteParent, null, "new", "lib", "CON");
        AssertEqual(1, invalidName.ExitCode, Describe(invalidName));
        AssertTrue(!Directory.Exists(Path.Combine(overwriteParent, "CON")),
            "A reserved filesystem name must not be created.");

        var packageGraph = await harness.WritePackageGraphAsync(
            "project-workflow-path-add",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"path-add-root\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                    + "\n[capabilities]\nfs.read = \"allow\"\n"
                    + "\n[dependencies]\nbase = \"../base\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main;\n"
                            + "pub fn main() -> Text effects {} { return new_helper::starter::lib::identity(\"added\"); }\n"
                            + "test \"added path package is visible\" { assert self::app::main::main() == \"added\"; }\n"
                    }),
                ["base"] = new PackageFixture(LibraryPackageManifest("base-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/base.hob"] = "module base; pub fn marker() -> i32 effects {} { return 1; }"
                    }),
                ["incoming"] = new PackageFixture(LibraryPackageManifest("New-Helper"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/starter/lib.hob"] = "module starter::lib;\n"
                            + "pub fn identity(value: Text) -> Text effects {} { return value; }\n"
                    })
            });
        var packageManifestPath = Path.Combine(packageGraph, "hob.toml");
        var originalPackageManifest = await File.ReadAllTextAsync(packageManifestPath);
        var dependencyHeader = originalPackageManifest.IndexOf("[dependencies]", StringComparison.Ordinal);
        AssertTrue(dependencyHeader >= 0, "The path-add fixture must include a dependency section after capabilities.");
        var pathAdd = await harness.InvokeCompilerCommandAtDirectoryWithEnvironmentAsync(
            packageGraph, null, "add", "../incoming");
        AssertEqual(0, pathAdd.ExitCode, Describe(pathAdd));
        var pathManifest = await File.ReadAllTextAsync(packageManifestPath);
        var expectedPathManifest = originalPackageManifest + "new_helper = \"../incoming\"\n";
        AssertEqual(expectedPathManifest, pathManifest,
            "Current-directory hob add must insert the dependency inside [dependencies] and preserve unrelated text.");

        var pathLockPath = Path.Combine(packageGraph, "hob.lock");
        var pathLockBytes = await File.ReadAllBytesAsync(pathLockPath);
        using (var pathLock = JsonDocument.Parse(pathLockBytes))
        {
            AssertEqual(3, pathLock.RootElement.GetProperty("schema_version").GetInt32(),
                "hob add must write package-lock schema version 3.");
            var lockedNames = pathLock.RootElement.GetProperty("packages").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString())
                .Order(StringComparer.Ordinal)
                .ToArray();
            AssertTrue(lockedNames.SequenceEqual(["New-Helper", "base-library"]),
                "hob add must lock the incoming dependency and the existing dependency.");
        }
        var pathCheck = await harness.InvokePackageDirectoryAsync("project-workflow-path-add-check", packageGraph, "check");
        AssertEqual(0, pathCheck.ExitCode, Describe(pathCheck));
        var pathBuild = await harness.InvokePackageDirectoryAsync("project-workflow-path-add-build", packageGraph, "build");
        AssertEqual(0, pathBuild.ExitCode, Describe(pathBuild));
        var pathTests = await harness.InvokePackageDirectoryAsync("project-workflow-path-add-test", packageGraph, "test");
        AssertEqual(0, pathTests.ExitCode, Describe(pathTests));
        AssertTrue(pathTests.StandardOutput.Contains("1 passed, 0 failed", StringComparison.Ordinal), Describe(pathTests));

        var beforeDuplicateManifest = await File.ReadAllBytesAsync(packageManifestPath);
        var beforeDuplicateLock = await File.ReadAllBytesAsync(pathLockPath);
        var duplicateAdd = await harness.InvokeCompilerCommandAtDirectoryWithEnvironmentAsync(
            packageGraph, null, "add", "../incoming");
        AssertEqual(1, duplicateAdd.ExitCode, Describe(duplicateAdd));
        var duplicateManifestAfter = await File.ReadAllBytesAsync(packageManifestPath);
        var duplicateLockAfter = await File.ReadAllBytesAsync(pathLockPath);
        AssertTrue(beforeDuplicateManifest.SequenceEqual(duplicateManifestAfter),
            "Rejecting a duplicate source must leave the manifest byte-for-byte unchanged.");
        AssertTrue(beforeDuplicateLock.SequenceEqual(duplicateLockAfter),
            "Rejecting a duplicate source must leave the lock byte-for-byte unchanged.");

        var explicitRoot = await harness.WritePackageAsync(
            "project-workflow-explicit-add",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 42; }"
            });
        var explicitDependency = Path.Combine(Path.GetDirectoryName(explicitRoot)!, "explicit-dependency");
        Directory.CreateDirectory(Path.Combine(explicitDependency, "src"));
        await File.WriteAllTextAsync(Path.Combine(explicitDependency, "hob.toml"), LibraryPackageManifest("explicit-helper"));
        await File.WriteAllTextAsync(
            Path.Combine(explicitDependency, "src", "helper.hob"),
            "module helper; pub fn value() -> i32 effects {} { return 7; }");
        var explicitAdd = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            null, "add", explicitRoot, "../explicit-dependency");
        AssertEqual(0, explicitAdd.ExitCode, Describe(explicitAdd));
        var explicitManifest = await File.ReadAllTextAsync(Path.Combine(explicitRoot, "hob.toml"));
        AssertTrue(explicitManifest.Contains("explicit_helper = \"../explicit-dependency\"", StringComparison.Ordinal),
            "The explicit PACKAGE_DIRECTORY SOURCE form must persist a relative path dependency.");
        var explicitCheck = await harness.InvokePackageDirectoryAsync(
            "project-workflow-explicit-add-check", explicitRoot, "check");
        AssertEqual(0, explicitCheck.ExitCode, Describe(explicitCheck));

        var rollbackRoot = await harness.WritePackageAsync(
            "project-workflow-add-rollback",
            CliPackageManifest() + "\n[dependencies]\nmissing = \"../missing\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
            });
        var rollbackDependency = Path.Combine(Path.GetDirectoryName(rollbackRoot)!, "rollback-dependency");
        Directory.CreateDirectory(Path.Combine(rollbackDependency, "src"));
        await File.WriteAllTextAsync(Path.Combine(rollbackDependency, "hob.toml"), LibraryPackageManifest("rollback-helper"));
        await File.WriteAllTextAsync(
            Path.Combine(rollbackDependency, "src", "helper.hob"),
            "module helper; pub fn value() -> i32 effects {} { return 7; }");
        var rollbackManifestPath = Path.Combine(rollbackRoot, "hob.toml");
        var rollbackManifestBytes = await File.ReadAllBytesAsync(rollbackManifestPath);
        var rollbackLockPath = Path.Combine(rollbackRoot, "hob.lock");
        var rollbackLockBytes = Encoding.UTF8.GetBytes("preserve previous lock bytes\n");
        await File.WriteAllBytesAsync(rollbackLockPath, rollbackLockBytes);
        var failedAdd = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            null, "add", rollbackRoot, "../rollback-dependency");
        AssertEqual(1, failedAdd.ExitCode, Describe(failedAdd));
        var rollbackManifestAfter = await File.ReadAllBytesAsync(rollbackManifestPath);
        var rollbackLockAfter = await File.ReadAllBytesAsync(rollbackLockPath);
        AssertTrue(rollbackManifestBytes.SequenceEqual(rollbackManifestAfter),
            "A failed dependency graph update must restore the manifest byte-for-byte.");
        AssertTrue(rollbackLockBytes.SequenceEqual(rollbackLockAfter),
            "A failed dependency graph update must restore the prior lock byte-for-byte.");

        var invalidGitRoot = await harness.WritePackageAsync(
            "project-workflow-invalid-git",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
            });
        var invalidGitManifestPath = Path.Combine(invalidGitRoot, "hob.toml");
        var invalidGitManifest = await File.ReadAllBytesAsync(invalidGitManifestPath);
        foreach (var invalidSource in new[]
                 {
                     "https://example.invalid/library#deadbeef",
                     "git+https://example.invalid/library#main"
                 })
        {
            var invalidAdd = await harness.InvokeCompilerCommandAtDirectoryWithEnvironmentAsync(
                invalidGitRoot, null, "add", invalidSource);
            AssertEqual(1, invalidAdd.ExitCode, Describe(invalidAdd));
            var invalidGitManifestAfter = await File.ReadAllBytesAsync(invalidGitManifestPath);
            AssertTrue(invalidGitManifest.SequenceEqual(invalidGitManifestAfter),
                "A short commit or branch name must be rejected without modifying the manifest.");
            AssertTrue(!File.Exists(Path.Combine(invalidGitRoot, "hob.lock")),
                "An invalid Git pin must not create a lockfile.");
        }

        var gitRepository = Path.Combine(harness.TemporaryRoot, "project-workflow-local-git");
        Directory.CreateDirectory(Path.Combine(gitRepository, "src", "greeting"));
        await File.WriteAllTextAsync(
            Path.Combine(gitRepository, "hob.toml"),
            "name = \"Git-Library\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n");
        await File.WriteAllTextAsync(
            Path.Combine(gitRepository, "src", "greeting", "lib.hob"),
            GenericValidationLibrarySource("greeting::lib"));

        var gitHome = Path.Combine(harness.TemporaryRoot, "project-workflow-git-home");
        var gitConfig = Path.Combine(harness.TemporaryRoot, "project-workflow-gitconfig");
        var gitHooks = Path.Combine(harness.TemporaryRoot, "project-workflow-git-hooks");
        Directory.CreateDirectory(gitHome);
        Directory.CreateDirectory(gitHooks);
        await File.WriteAllTextAsync(gitConfig, string.Empty);
        var gitEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = gitConfig,
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["HOME"] = gitHome,
            ["XDG_CONFIG_HOME"] = Path.Combine(gitHome, ".config")
        };
        async Task<string> Git(params string[] arguments)
        {
            var result = await InvokeExternalProcessAsync("git", gitRepository, gitEnvironment, arguments);
            AssertEqual(0, result.ExitCode, Describe(result));
            return result.StandardOutput.Trim();
        }

        await Git("init", "--quiet");
        await Git("config", "user.name", "Hob Integration Tests");
        await Git("config", "user.email", "hob-integration@example.invalid");
        await Git("config", "core.hooksPath", gitHooks);
        await Git("config", "commit.gpgsign", "false");
        await Git("add", "--all");
        await Git("commit", "--quiet", "-m", "pinned package fixture");
        var commit = await Git("rev-parse", "--verify", "HEAD");
        AssertTrue(Regex.IsMatch(commit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant),
            $"The local Git fixture must produce a full lowercase commit ID, got '{commit}'.");
        var gitUrl = new Uri(Path.GetFullPath(gitRepository) + Path.DirectorySeparatorChar).AbsoluteUri;
        var gitSource = $"git+{gitUrl}#{commit}";

        var gitCache = Path.Combine(harness.TemporaryRoot, "package-cache-original");
        var hostileGitExecPath = Path.Combine(harness.TemporaryRoot, "hostile-git-exec-path");
        var packageCacheEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_PACKAGE_CACHE"] = gitCache,
            ["GIT_EXEC_PATH"] = hostileGitExecPath
        };
        var gitConsumerRoot = await harness.WritePackageAsync(
            "project-workflow-git-lock",
            CliPackageManifest() + $"\n[dependencies]\ngit_library = \"{gitSource}\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main;\n"
                    + "pub fn main() -> Text effects {} { return match git_library::greeting::lib::normalize(\" cached \") { git_library::greeting::lib::Validation.Valid(value) => value.value, git_library::greeting::lib::Validation.Invalid(error) => \"invalid\" }; }\n"
                    + "test \"pinned package call\" { assert self::app::main::main() == \"cached\"; }\n"
            });
        var createGitLock = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-lock-fetch", gitConsumerRoot, "lock", packageCacheEnvironment);
        AssertEqual(0, createGitLock.ExitCode, Describe(createGitLock));

        var gitLockPath = Path.Combine(gitConsumerRoot, "hob.lock");
        var gitLockBytes = await File.ReadAllBytesAsync(gitLockPath);
        var gitLockText = Encoding.UTF8.GetString(gitLockBytes);
        using (var gitLock = JsonDocument.Parse(gitLockBytes))
        {
            var root = gitLock.RootElement.GetProperty("root");
            AssertEqual(3, gitLock.RootElement.GetProperty("schema_version").GetInt32(),
                "A fetched Git package must be written in lock schema version 3.");
            AssertEqual("root", root.GetProperty("source").GetProperty("kind").GetString(),
                "The Git lock root must carry a root source identity.");
            var lockedRelative = root.GetProperty("dependencies").GetProperty("git_library").GetString() ?? string.Empty;
            AssertTrue(Regex.IsMatch(lockedRelative, "^git:[0-9a-f]{64}$", RegexOptions.CultureInvariant)
                       && !Path.IsPathRooted(lockedRelative),
                "Git dependency lock paths must use their portable cache identity.");
            var dependency = gitLock.RootElement.GetProperty("packages").EnumerateArray().Single();
            var source = dependency.GetProperty("source");
            AssertEqual("git", source.GetProperty("kind").GetString(),
                "A pinned dependency lock entry must identify a Git source.");
            AssertEqual(gitUrl, source.GetProperty("url").GetString(),
                "The lock must retain the canonical pinned Git URL.");
            AssertEqual(commit, source.GetProperty("commit").GetString(),
                "The lock must retain the exact full commit.");
            AssertEqual(lockedRelative, dependency.GetProperty("path").GetString(),
                "The locked Git package path must be the source's stable cache identity.");
        }
        AssertTrue(!gitLockText.Contains(Path.GetFullPath(gitCache), StringComparison.OrdinalIgnoreCase),
            "The package lock must not serialize an absolute package-cache path.");
        var lockBeforeNestedCache = await File.ReadAllBytesAsync(gitLockPath);
        var nestedCacheRoot = Path.Combine(gitConsumerRoot, "src", "nested-package-cache");
        var nestedCacheEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_PACKAGE_CACHE"] = nestedCacheRoot
        };
        var nestedCacheLock = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-nested-cache-lock", gitConsumerRoot, "lock", nestedCacheEnvironment);
        AssertEqual(1, nestedCacheLock.ExitCode, Describe(nestedCacheLock));
        AssertTrue(nestedCacheLock.StandardError.Contains("E_DEPENDENCY", StringComparison.Ordinal),
            "hob lock must reject a package cache under the package source root.");
        AssertTrue(!Directory.Exists(nestedCacheRoot),
            "A rejected cache root under the package source must not be created.");
        var lockAfterNestedCache = await File.ReadAllBytesAsync(gitLockPath);
        AssertTrue(lockBeforeNestedCache.SequenceEqual(lockAfterNestedCache),
            "Rejecting a cache root under the package source must preserve the existing lock.");

        var gitAddRoot = await harness.WritePackageAsync(
            "project-workflow-git-add",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main;\n"
                    + "pub fn main() -> Text effects {} { return match git_library::greeting::lib::normalize(\" cached \") { git_library::greeting::lib::Validation.Valid(value) => value.value, git_library::greeting::lib::Validation.Invalid(error) => \"invalid\" }; }\n"
                    + "test \"pinned package call\" { assert self::app::main::main() == \"cached\"; }\n"
            });
        var gitAdd = await harness.InvokeCompilerCommandAtDirectoryWithEnvironmentAsync(
            gitAddRoot, packageCacheEnvironment, "add", $"{gitUrl}#{commit}");
        AssertEqual(0, gitAdd.ExitCode, Describe(gitAdd));
        var gitAddManifest = await File.ReadAllTextAsync(Path.Combine(gitAddRoot, "hob.toml"));
        AssertTrue(gitAddManifest.Contains($"git_library = \"{gitSource}\"", StringComparison.Ordinal),
            "hob add must persist a canonical Git URL and the full exact commit.");

        var cacheEntries = Directory.EnumerateDirectories(Path.Combine(gitCache, "git")).ToArray();
        AssertEqual(1, cacheEntries.Length, "The cache must contain one stable entry for the pinned URL and commit.");
        var cacheEntryName = Path.GetFileName(cacheEntries[0]);
        var relocatedCache = Path.Combine(harness.TemporaryRoot, "package-cache-relocated");
        var relocatedEntry = Path.Combine(relocatedCache, "git", cacheEntryName);
        CopyDirectory(cacheEntries[0], relocatedEntry);

        var rehashedTamperedCache = Path.Combine(harness.TemporaryRoot, "package-cache-rehashed-tampered");
        var rehashedTamperedEntry = Path.Combine(rehashedTamperedCache, "git", cacheEntryName);
        CopyDirectory(cacheEntries[0], rehashedTamperedEntry);
        var rehashedTamperedSourceRoot = Path.Combine(rehashedTamperedEntry, "source");
        var rehashedTamperedSource = Path.Combine(rehashedTamperedSourceRoot, "src", "greeting", "lib.hob");
        await File.AppendAllTextAsync(rehashedTamperedSource, "\n");
        var rehashedAttestationPath = Path.Combine(rehashedTamperedEntry, "attestation.json");
        var canonicalAttestationBytes = await File.ReadAllBytesAsync(Path.Combine(cacheEntries[0], "attestation.json"));
        string rehashedAttestationText;
        using (var canonicalAttestation = JsonDocument.Parse(canonicalAttestationBytes))
        {
            var root = canonicalAttestation.RootElement;
            rehashedAttestationText = JsonSerializer.Serialize(new
            {
                schema_version = root.GetProperty("schema_version").GetInt32(),
                url = root.GetProperty("url").GetString(),
                commit = root.GetProperty("commit").GetString(),
                content_sha256 = HashPackageCacheSource(rehashedTamperedSourceRoot)
            }, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        }
        await File.WriteAllTextAsync(rehashedAttestationPath, rehashedAttestationText, new UTF8Encoding(false));
        var rehashedSourceBeforeLock = await File.ReadAllBytesAsync(rehashedTamperedSource);
        var rehashedAttestationBeforeLock = await File.ReadAllBytesAsync(rehashedAttestationPath);
        var rehashedCacheEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_PACKAGE_CACHE"] = rehashedTamperedCache
        };
        var rehashedLockBefore = await File.ReadAllBytesAsync(gitLockPath);
        var rehashedTamperedLock = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-rehashed-tampered-lock", gitConsumerRoot, "lock", rehashedCacheEnvironment);
        AssertEqual(1, rehashedTamperedLock.ExitCode, Describe(rehashedTamperedLock));
        AssertTrue(rehashedTamperedLock.StandardError.Contains("E_DEPENDENCY", StringComparison.Ordinal),
            "hob lock must reject altered cache bytes even when their unkeyed content hash is recomputed.");
        var rehashedSourceAfterLock = await File.ReadAllBytesAsync(rehashedTamperedSource);
        var rehashedAttestationAfterLock = await File.ReadAllBytesAsync(rehashedAttestationPath);
        AssertTrue(rehashedSourceBeforeLock.SequenceEqual(rehashedSourceAfterLock)
                   && rehashedAttestationBeforeLock.SequenceEqual(rehashedAttestationAfterLock),
            "Rejecting rehashed altered cache contents must preserve the cache source and attestation byte-for-byte.");
        var rehashedLockAfter = await File.ReadAllBytesAsync(gitLockPath);
        AssertTrue(rehashedLockBefore.SequenceEqual(rehashedLockAfter),
            "Rejecting rehashed altered cache contents must preserve the previous package lock byte-for-byte.");

        var rehashedTamperedCheck = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-rehashed-tampered-check", gitConsumerRoot, "check",
            rehashedCacheEnvironment, "--json");
        AssertEqual(1, rehashedTamperedCheck.ExitCode, Describe(rehashedTamperedCheck));
        var rehashedTamperedDiagnostics = ParseDiagnosticSnapshots(rehashedTamperedCheck.StandardOutput);
        AssertTrue(rehashedTamperedDiagnostics.Any(diagnostic => diagnostic.Code == "E_LOCK"),
            "Offline check must reject rehashed altered cache bytes against the unchanged lock.");

        File.Copy(
            Path.Combine(cacheEntries[0], "source", "src", "greeting", "lib.hob"),
            rehashedTamperedSource,
            overwrite: true);
        File.Copy(
            Path.Combine(cacheEntries[0], "attestation.json"),
            rehashedAttestationPath,
            overwrite: true);
        var restoredCanonicalCheck = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-restored-canonical-check", gitConsumerRoot, "check",
            rehashedCacheEnvironment, "--json");
        AssertEqual(0, restoredCanonicalCheck.ExitCode, Describe(restoredCanonicalCheck));
        AssertEqual(0, ParseDiagnosticSnapshots(restoredCanonicalCheck.StandardOutput).Length,
            Describe(restoredCanonicalCheck));
        DeleteReadOnlyDirectoryTree(Path.Combine(gitRepository, ".git"));
        var oldRepository = gitRepository + "-offline";
        Directory.Move(gitRepository, oldRepository);
        var consumers = new[] { gitConsumerRoot, gitAddRoot };
        foreach (var consumer in consumers)
        {
            var check = await harness.InvokePackageDirectoryWithEnvironmentAsync(
                "project-workflow-git-offline-check", consumer, "check", packageCacheEnvironment, "--json");
            AssertEqual(0, check.ExitCode, Describe(check));
            AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

            var build = await harness.InvokePackageDirectoryWithEnvironmentAsync(
                "project-workflow-git-offline-build", consumer, "build", packageCacheEnvironment);
            AssertEqual(0, build.ExitCode, Describe(build));
            AssertTrue(!build.StandardOutput.Contains(Path.GetFullPath(gitCache), StringComparison.OrdinalIgnoreCase),
                "A package build must not expose the absolute cache path.");

            var tests = await harness.InvokePackageDirectoryWithEnvironmentAsync(
                "project-workflow-git-offline-test", consumer, "test", packageCacheEnvironment);
            AssertEqual(0, tests.ExitCode, Describe(tests));
            AssertTrue(tests.StandardOutput.Contains("1 passed, 0 failed", StringComparison.Ordinal), Describe(tests));

            var audit = await harness.InvokePackageDirectoryWithEnvironmentAsync(
                "project-workflow-git-offline-audit", consumer, "audit", packageCacheEnvironment, "--json");
            AssertEqual(0, audit.ExitCode, Describe(audit));
            using (var auditDocument = JsonDocument.Parse(audit.StandardOutput))
            {
                AssertEqual(11, auditDocument.RootElement.GetProperty("schema_version").GetInt32(),
                    "Audit reports with source identities must use schema version 11.");
                var identity = auditDocument.RootElement.GetProperty("packages").EnumerateArray()
                    .Single(package => package.GetProperty("role").GetString() == "direct")
                    .GetProperty("identity");
                AssertPackageIdentitySource(identity);
                AssertEqual("git", identity.GetProperty("source").GetProperty("kind").GetString(),
                    "Audit package identities must preserve Git provenance.");
                AssertEqual(commit, identity.GetProperty("source").GetProperty("commit").GetString(),
                    "Audit package identities must preserve the exact Git commit.");
            }
            AssertTrue(!audit.StandardOutput.Contains(Path.GetFullPath(gitCache), StringComparison.OrdinalIgnoreCase),
                "Audit output must not expose the absolute package-cache path.");

            var api = await harness.InvokeCompilerCommandWithEnvironmentAsync(
                packageCacheEnvironment, "inspect", "api", consumer, "--json");
            AssertEqual(0, api.ExitCode, Describe(api));
            AssertTrue(!api.StandardOutput.Contains(Path.GetFullPath(gitCache), StringComparison.OrdinalIgnoreCase),
                "Inspect output must not expose the absolute package-cache path.");
            AssertGenericTraitReportFacts(api.StandardOutput, audit.StandardOutput, "git_library", expectVisibleImpl: true);

            var effects = await harness.InvokeCompilerCommandWithEnvironmentAsync(
                packageCacheEnvironment, "inspect", "effects", consumer,
                "self::app::main::main", "--json");
            AssertEqual(0, effects.ExitCode, Describe(effects));
            AssertTrue(!effects.StandardOutput.Contains(Path.GetFullPath(gitCache), StringComparison.OrdinalIgnoreCase),
                "Inspect-effects output must not expose the absolute package-cache path.");
        }

        var relocatedEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_PACKAGE_CACHE"] = relocatedCache
        };
        var relocatedCheck = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-relocated-check", gitConsumerRoot, "check", relocatedEnvironment, "--json");
        AssertEqual(0, relocatedCheck.ExitCode, Describe(relocatedCheck));
        var relocatedAudit = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-relocated-audit", gitConsumerRoot, "audit", relocatedEnvironment, "--json");
        AssertEqual(0, relocatedAudit.ExitCode, Describe(relocatedAudit));
        var relocatedApi = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            relocatedEnvironment, "inspect", "api", gitConsumerRoot, "--json");
        AssertEqual(0, relocatedApi.ExitCode, Describe(relocatedApi));
        AssertGenericTraitReportFacts(relocatedApi.StandardOutput, relocatedAudit.StandardOutput,
            "git_library", expectVisibleImpl: true);
        AssertTrue(!relocatedApi.StandardOutput.Contains(Path.GetFullPath(relocatedCache), StringComparison.OrdinalIgnoreCase),
            "Relocated inspect-api output must not expose the configured cache path.");
        var originalApi = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            packageCacheEnvironment, "inspect", "api", gitConsumerRoot, "--json");
        AssertEqual(0, originalApi.ExitCode, Describe(originalApi));
        AssertEqual(originalApi.StandardOutput, relocatedApi.StandardOutput,
            "Equivalent Git cache contents at different cache roots must produce identical API JSON.");
        var originalAudit = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-original-audit", gitConsumerRoot, "audit", packageCacheEnvironment, "--json");
        AssertEqual(originalAudit.StandardOutput, relocatedAudit.StandardOutput,
            "Equivalent cache contents at different cache roots must produce identical audit JSON.");
        AssertTrue(!gitLockText.Contains(Path.GetFullPath(relocatedCache), StringComparison.OrdinalIgnoreCase)
                   && !relocatedAudit.StandardOutput.Contains(Path.GetFullPath(relocatedCache), StringComparison.OrdinalIgnoreCase),
            "Lock and audit outputs must not depend on the configured cache root.");

        var missingCache = Path.Combine(harness.TemporaryRoot, "package-cache-missing");
        var missingEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_PACKAGE_CACHE"] = missingCache
        };
        var missingCacheCheck = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-missing-cache", gitConsumerRoot, "check", missingEnvironment, "--json");
        AssertEqual(1, missingCacheCheck.ExitCode, Describe(missingCacheCheck));
        var missingCacheDiagnostics = ParseDiagnosticSnapshots(missingCacheCheck.StandardOutput);
        AssertTrue(missingCacheDiagnostics.Length > 0
                   && missingCacheDiagnostics.All(diagnostic => diagnostic.Code == "E_DEPENDENCY"),
            "A missing offline cache must fail dependency resolution before source compilation.");

        var tamperedCache = Path.Combine(harness.TemporaryRoot, "package-cache-tampered");
        var tamperedEntry = Path.Combine(tamperedCache, "git", cacheEntryName);
        CopyDirectory(cacheEntries[0], tamperedEntry);
        var tamperedSource = Path.Combine(tamperedEntry, "source", "src", "greeting", "lib.hob");
        await File.AppendAllTextAsync(tamperedSource, "\n");
        var tamperedEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_PACKAGE_CACHE"] = tamperedCache
        };
        var tamperedCheck = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-tampered-cache", gitConsumerRoot, "check", tamperedEnvironment, "--json");
        AssertEqual(1, tamperedCheck.ExitCode, Describe(tamperedCheck));
        var tamperedDiagnostics = ParseDiagnosticSnapshots(tamperedCheck.StandardOutput);
        AssertTrue(tamperedDiagnostics.Length > 0
                   && tamperedDiagnostics.All(diagnostic => diagnostic.Code == "E_DEPENDENCY"),
            "A tampered cache must fail attestation before source compilation.");
        var priorLock = await File.ReadAllBytesAsync(gitLockPath);
        var tamperedLock = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "project-workflow-git-tampered-lock", gitConsumerRoot, "lock", tamperedEnvironment);
        AssertEqual(1, tamperedLock.ExitCode, Describe(tamperedLock));
        var tamperedLockAfter = await File.ReadAllBytesAsync(gitLockPath);
        AssertTrue(priorLock.SequenceEqual(tamperedLockAfter),
            "hob lock must not legitimize tampered cached source or rewrite the existing lock.");
    }

    private static string HashPackageCacheSource(string sourceRoot)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("HOB-GIT-CACHE-TREE\0v1"));
        Span<byte> lengthBytes = stackalloc byte[sizeof(ulong)];
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                     .Select(fullPath => (
                         RelativePath: Path.GetRelativePath(sourceRoot, fullPath)
                             .Replace(Path.DirectorySeparatorChar, '/')
                             .Replace(Path.AltDirectorySeparatorChar, '/'),
                         FullPath: fullPath))
                     .OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            var pathBytes = Encoding.UTF8.GetBytes(file.RelativePath);
            BinaryPrimitives.WriteUInt64BigEndian(lengthBytes, (ulong)pathBytes.LongLength);
            hash.AppendData(lengthBytes);
            hash.AppendData(pathBytes);
            var content = File.ReadAllBytes(file.FullPath);
            BinaryPrimitives.WriteUInt64BigEndian(lengthBytes, (ulong)content.LongLength);
            hash.AppendData(lengthBytes);
            hash.AppendData(content);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path.Length))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void DeleteReadOnlyDirectoryTree(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(fullDirectory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
        }
        var rootAttributes = File.GetAttributes(fullDirectory);
        if ((rootAttributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(fullDirectory, rootAttributes & ~FileAttributes.ReadOnly);
        Directory.Delete(fullDirectory, recursive: true);
    }

    private static async Task<ProcessResult> InvokeExternalProcessAsync(
        string executable,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
                startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start external process '{executable}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
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
            throw new TimeoutException($"External process '{executable}' exceeded {ProcessTimeout}.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task TestPathDependencyLockLifecycle(Harness harness)
    {
        const string consumerManifest = "name = \"lock-consumer\"\n"
            + "version = \"0.1.0\"\n"
            + "kind = \"cli\"\n"
            + "source_root = \"src\"\n"
            + "entry_module = \"app::main\"\n"
            + "\n[dependencies]\n"
            + "validation = \"../validation\"\n";
        const string validationSource = "module text::validation;\n"
            + "pub union NormalizeError { Empty }\n"
            + "pub fn normalize(input: Text) -> Result<Text, self::text::validation::NormalizeError> effects {} {\n"
            + "    if input.length == 0 { return Err(self::text::validation::NormalizeError.Empty); }\n"
            + "    return Ok(input.trim());\n"
            + "}\n"
            + "pub fn require<T, E>(value: Option<T>, error: E) -> Result<T, E> effects {} {\n"
            + "    return match value { Some(item) => Ok(item), None => Err(error) };\n"
            + "}\n";
        const string consumerSource = "module app::main;\n"
            + "pub fn main() -> Text effects {} {\n"
            + "    let normalized: Result<Text, validation::text::validation::NormalizeError> = validation::text::validation::normalize(\" ready \");\n"
            + "    let candidate: Option<Text> = match normalized {\n"
            + "        Ok(value) => Some(value),\n"
            + "        Err(error) => None,\n"
            + "    };\n"
            + "    let required: Result<Text, validation::text::validation::NormalizeError> = validation::text::validation::require(candidate, validation::text::validation::NormalizeError.Empty);\n"
            + "    return match required { Ok(value) => value, Err(error) => \"ready\" };\n"
            + "}\n";
        var packageRoot = await harness.WritePackageGraphAsync(
            "path-dependency-lock",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(consumerManifest, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = consumerSource
                }),
                ["validation"] = new PackageFixture(LibraryPackageManifest("text-validation"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.hob"] = validationSource
                })
            });
        var lockPath = Path.Combine(packageRoot, "hob.lock");

        await AssertPathDependencyLockRequiredAsync(harness, "path-dependency-missing-lock", packageRoot);

        var create = await harness.InvokePackageDirectoryAsync("path-dependency-lock-create", packageRoot, "lock");
        AssertEqual(0, create.ExitCode, Describe(create));
        AssertTrue(File.Exists(lockPath), "hob lock should create hob.lock for a package with dependencies.");
        var firstLockBytes = await File.ReadAllBytesAsync(lockPath);
        AssertTrue(!firstLockBytes.Contains((byte)'\r'), "hob.lock must use LF line endings without carriage returns.");
        AssertTrue(firstLockBytes.Length > 0
            && firstLockBytes[^1] == (byte)'\n'
            && (firstLockBytes.Length == 1 || firstLockBytes[^2] != (byte)'\n'),
            "hob.lock must end with exactly one LF byte.");
        var firstLock = Encoding.UTF8.GetString(firstLockBytes);
        using (var document = JsonDocument.Parse(firstLock))
        {
            AssertEqual(3, document.RootElement.GetProperty("schema_version").GetInt32(),
                "Package locks with source identities must use schema version 3.");
            var root = document.RootElement.GetProperty("root");
            AssertEqual("root", root.GetProperty("source").GetProperty("kind").GetString(),
                "The root package lock entry must identify the root source.");
            AssertEqual("../validation", root.GetProperty("dependencies").GetProperty("validation").GetString(),
                "The root dependency path must be portable and relative to the package root.");
            var packages = document.RootElement.GetProperty("packages");
            AssertEqual(1, packages.GetArrayLength(), "The lock should contain the resolved validation package.");
            var dependencyPath = packages[0].GetProperty("path").GetString() ?? string.Empty;
            AssertEqual("../validation", dependencyPath, "Package paths in hob.lock should remain relative.");
            AssertTrue(!Path.IsPathRooted(dependencyPath), "A lock package path must not be absolute.");
            var source = packages[0].GetProperty("source");
            AssertEqual("path", source.GetProperty("kind").GetString(),
                "A local dependency lock entry must identify a path source.");
            AssertEqual("../validation", source.GetProperty("path").GetString(),
                "A path source identity must be portable and relative to the package root.");
        }
        AssertTrue(!firstLock.Contains(packageRoot, StringComparison.OrdinalIgnoreCase)
            && !firstLock.Contains(harness.TemporaryRoot, StringComparison.OrdinalIgnoreCase),
            "The lock must not contain an absolute workspace path.");

        var repeat = await harness.InvokePackageDirectoryAsync("path-dependency-lock-repeat", packageRoot, "lock");
        AssertEqual(0, repeat.ExitCode, Describe(repeat));
        var repeatedLockBytes = await File.ReadAllBytesAsync(lockPath);
        AssertTrue(firstLockBytes.SequenceEqual(repeatedLockBytes), "Repeated hob lock should be byte-for-byte deterministic.");

        await AssertDependencyConsumerWorksAsync(harness, "path-dependency-initial", packageRoot, "ready" + Environment.NewLine);

        File.Delete(lockPath);
        await AssertPathDependencyLockRequiredAsync(harness, "path-dependency-deleted-lock", packageRoot);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("path-dependency-relock-missing", packageRoot, "lock"));
        await AssertPackageCheckPassesAsync(harness, "path-dependency-missing-lock-repaired", packageRoot);

        await File.WriteAllTextAsync(lockPath, "{\"schema_version\":99}\n");
        await AssertPathDependencyLockRequiredAsync(harness, "path-dependency-malformed-lock", packageRoot);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("path-dependency-relock-malformed", packageRoot, "lock"));
        await AssertPackageCheckPassesAsync(harness, "path-dependency-malformed-lock-repaired", packageRoot);

        var dependencySourcePath = Path.Combine(packageRoot, "..", "validation", "src", "text", "validation.hob");
        var originalDependencySource = await File.ReadAllTextAsync(dependencySourcePath);
        var changedDependencySource = originalDependencySource.Replace("input.trim()", "input", StringComparison.Ordinal);
        AssertTrue(changedDependencySource != originalDependencySource, "The test must edit a dependency source file.");
        await File.WriteAllTextAsync(dependencySourcePath, changedDependencySource);
        await AssertPathDependencyLockRequiredAsync(harness, "path-dependency-stale-source", packageRoot);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("path-dependency-relock-source", packageRoot, "lock"));

        var relockedSource = await File.ReadAllTextAsync(lockPath);
        AssertTrue(relockedSource != firstLock, "Updating after a dependency source edit should update the recorded content hash.");
        await File.WriteAllTextAsync(dependencySourcePath, relockedSource.Length == 0
            ? changedDependencySource
            : changedDependencySource.Replace("\n", "\r\n", StringComparison.Ordinal));
        var crlfCheck = await harness.InvokePackageDirectoryAsync("path-dependency-crlf-only", packageRoot, "check", "--json");
        AssertEqual(0, crlfCheck.ExitCode, Describe(crlfCheck));
        AssertEqual(0, ParseDiagnosticSnapshots(crlfCheck.StandardOutput).Length,
            "A CRLF-only dependency source rewrite must preserve its lock hash.");

        var ignoredOutput = Path.Combine(packageRoot, "out", "generated.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredOutput)!);
        await File.WriteAllTextAsync(ignoredOutput, "generated output is not a package source");
        var outputCheck = await harness.InvokePackageDirectoryAsync("path-dependency-out-edit", packageRoot, "check", "--json");
        AssertEqual(0, outputCheck.ExitCode, Describe(outputCheck));
        AssertEqual(0, ParseDiagnosticSnapshots(outputCheck.StandardOutput).Length,
            "Editing out/ must not change the dependency content hash.");

        await AssertDependencyConsumerWorksAsync(harness, "path-dependency-final", packageRoot, " ready " + Environment.NewLine);

        var dependencyFree = await harness.WritePackageAsync(
            "dependency-free-package-lock",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 42; }"
            });
        var noDependencies = await harness.InvokePackageDirectoryAsync("dependency-free-lock", dependencyFree, "lock");
        AssertEqual(0, noDependencies.ExitCode, Describe(noDependencies));
        AssertTrue(!File.Exists(Path.Combine(dependencyFree, "hob.lock")),
            "A dependency-free package should not need or create a lockfile.");
    }

    private static async Task TestDependencyGraphDiagnostics(Harness harness)
    {
        var cycleRoot = await harness.WritePackageGraphAsync(
            "dependency-cycle",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    LibraryPackageManifest("cycle-root") + "\n[dependencies]\nnext = \"../next\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/root.hob"] = "module root; pub fn value() -> i32 effects {} { return 1; }"
                    }),
                ["next"] = new PackageFixture(
                    LibraryPackageManifest("cycle-next") + "\n[dependencies]\nback = \"../root\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/next.hob"] = "module next; pub fn value() -> i32 effects {} { return 2; }"
                    })
            });
        await AssertPackageDiagnosticAsync(harness, "dependency-cycle-check", cycleRoot, "E_DEPENDENCY", "../next/hob.toml");

        var missingManifestRoot = await harness.WritePackageGraphAsync(
            "dependency-missing-manifest",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\nmissing = \"../missing\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
                    })
            });
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(missingManifestRoot)!, "missing"));
        await AssertPackageDiagnosticAsync(harness, "dependency-missing-manifest-check", missingManifestRoot, "E_DEPENDENCY", "hob.toml");

        var cliDependencyRoot = await harness.WritePackageGraphAsync(
            "dependency-must-be-library",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\napp = \"../app-dependency\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
                    }),
                ["app-dependency"] = new PackageFixture(
                    CliPackageManifest(),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 2; }"
                    })
            });
        await AssertPackageDiagnosticAsync(harness, "dependency-cli-child-check", cliDependencyRoot, "E_DEPENDENCY", "hob.toml");

        const string duplicateManifest = "name = \"duplicate-library\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n";
        var duplicateIdentityRoot = await harness.WritePackageGraphAsync(
            "dependency-duplicate-name-version",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\nleft = \"../left\"\nright = \"../right\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
                    }),
                ["left"] = new PackageFixture(duplicateManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/left.hob"] = "module left; pub fn value() -> i32 effects {} { return 1; }"
                    }),
                ["right"] = new PackageFixture(duplicateManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/right.hob"] = "module right; pub fn value() -> i32 effects {} { return 2; }"
                    })
            });
        await AssertPackageDiagnosticAsync(harness, "dependency-duplicate-identity-check", duplicateIdentityRoot, "E_DEPENDENCY", "hob.toml");
    }

    private static async Task TestDependencyAliasResolution(Harness harness)
    {
        var sharedModuleRoot = await harness.WritePackageGraphAsync(
            "dependency-shared-module-identity",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"alias-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                    + "\n[dependencies]\nalpha = \"../alpha\"\nbeta = \"../beta\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main;\n"
                            + "pub fn load_from_alpha(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return alpha::text::validation::load(fs); }\n"
                            + "pub fn main() -> i32 effects {} { return alpha::text::validation::read_alpha(alpha::text::validation::alpha_token()) + beta::text::validation::read_beta(beta::text::validation::beta_token()); }\n"
                    }),
                ["alpha"] = new PackageFixture(LibraryPackageManifest("alpha-library"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.hob"] = "module text::validation;\n"
                        + "pub struct Token { value: i32 }\n"
                        + "pub fn alpha_token() -> self::text::validation::Token effects {} { return self::text::validation::Token { value: 20 }; }\n"
                        + "pub fn read_alpha(value: self::text::validation::Token) -> i32 effects {} { return value.value + 1; }\n"
                        + "pub fn load(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n"
                }),
                ["beta"] = new PackageFixture(LibraryPackageManifest("beta-library"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.hob"] = "module text::validation;\n"
                        + "pub struct Token { value: i32 }\n"
                        + "pub fn beta_token() -> self::text::validation::Token effects {} { return self::text::validation::Token { value: 20 }; }\n"
                        + "pub fn read_beta(value: self::text::validation::Token) -> i32 effects {} { return value.value + 1; }\n"
                })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-shared-module-lock", sharedModuleRoot, "lock"));
        var sharedModuleRun = await harness.InvokePackageDirectoryAsync("dependency-shared-module-run", sharedModuleRoot, "run");
        AssertRunOutput("42" + Environment.NewLine, sharedModuleRun);

        var crossIdentityRoot = await harness.WritePackageGraphAsync(
            "dependency-module-identities-are-isolated",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"identity-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                    + "\n[dependencies]\nalpha = \"../alpha\"\nbeta = \"../beta\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main;\n"
                            + "pub fn main() -> i32 effects {} { return alpha::text::validation::read_alpha(beta::text::validation::beta_token()); }\n"
                    }),
                ["alpha"] = new PackageFixture(LibraryPackageManifest("identity-alpha"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.hob"] = "module text::validation; pub struct Token { value: i32 }\n"
                        + "pub fn alpha_token() -> self::text::validation::Token effects {} { return self::text::validation::Token { value: 1 }; }\n"
                        + "pub fn read_alpha(value: self::text::validation::Token) -> i32 effects {} { return value.value; }\n"
                }),
                ["beta"] = new PackageFixture(LibraryPackageManifest("identity-beta"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.hob"] = "module text::validation; pub struct Token { value: i32 }\n"
                        + "pub fn beta_token() -> self::text::validation::Token effects {} { return self::text::validation::Token { value: 2 }; }\n"
                })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-cross-identity-lock", crossIdentityRoot, "lock"));
        await AssertPackageDiagnosticAsync(harness, "dependency-cross-identity-check", crossIdentityRoot, "E_TYPE_MISMATCH", "src/app/main.hob");

        var transitiveRoot = await harness.WritePackageGraphAsync(
            "dependency-alias-direct-only",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"direct-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                    + "\n[dependencies]\nmid = \"../mid\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main;\n"
                            + "pub fn main() -> i32 effects {} { return mid::mid::api::wrapped() + foundation::foundation::answer(); }\n"
                    }),
                ["mid"] = new PackageFixture(LibraryPackageManifest("middle-library") + "\n[dependencies]\nfoundation = \"../foundation\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/mid/api.hob"] = "module mid::api; pub fn wrapped() -> i32 effects {} { return foundation::foundation::answer(); }"
                    }),
                ["foundation"] = new PackageFixture(LibraryPackageManifest("foundation-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/foundation.hob"] = "module foundation; pub fn answer() -> i32 effects {} { return 21; }"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-transitive-lock", transitiveRoot, "lock"));
        await AssertPackageDiagnosticAsync(harness, "dependency-transitive-alias-check", transitiveRoot, "E_NAME_UNRESOLVED", "src/app/main.hob");
    }

    private static async Task TestDependencyReferenceDiagnostics(Harness harness)
    {
        var packageRoot = await harness.WritePackageGraphAsync(
            "dependency-reference-visibility",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"visibility-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                    + "\n[dependencies]\nvalidation = \"../validation\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main;\n"
                            + "pub fn main() -> i32 effects {} { return validation::text::validation::hidden() + validation::text::validation::absent(); }\n"
                    }),
                ["validation"] = new PackageFixture(LibraryPackageManifest("visibility-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/text/validation.hob"] = "module text::validation;\n"
                            + "pub fn present() -> i32 effects {} { return 1; }\n"
                            + "fn hidden() -> i32 effects {} { return 2; }\n"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-reference-visibility-lock", packageRoot, "lock"));
        var visibility = await harness.InvokePackageDirectoryAsync("dependency-reference-visibility-check", packageRoot, "check", "--json");
        AssertEqual(1, visibility.ExitCode, Describe(visibility));
        var visibilityDiagnostics = ParseDiagnosticSnapshots(visibility.StandardOutput);
        AssertTrue(visibilityDiagnostics.Any(diagnostic => diagnostic.Code == "E_ACCESS_PRIVATE"),
            $"A private declaration in a path dependency must be rejected. {visibility.StandardOutput}");
        AssertTrue(visibilityDiagnostics.Any(diagnostic => diagnostic.Code == "E_NAME_UNRESOLVED"),
            $"A missing declaration in a path dependency must be rejected. {visibility.StandardOutput}");

        var malformedAliasRoot = await harness.WritePackageGraphAsync(
            "dependency-malformed-qualified-reference",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"malformed-reference-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                    + "\n[dependencies]\nvalidation = \"../validation\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 42::text::validation::present(); }"
                    }),
                ["validation"] = new PackageFixture(LibraryPackageManifest("malformed-reference-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/text/validation.hob"] = "module text::validation; pub fn present() -> i32 effects {} { return 1; }"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-malformed-reference-lock", malformedAliasRoot, "lock"));
        await AssertPackageDiagnosticAsync(harness, "dependency-malformed-reference-check", malformedAliasRoot, "E_SYNTAX", "src/app/main.hob");
    }

    private static async Task TestDependencyFilesystemSafety(Harness harness)
    {
        var invalidSourceRoot = await harness.WritePackageGraphAsync(
            "dependency-source-root-escape",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"source-root-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                    + "\n[dependencies]\nvalidation = \"../validation\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
                    }),
                ["validation"] = new PackageFixture(
                    "name = \"escaped-source-root\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"../outside\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal))
            });
        await AssertPackageDiagnosticAsync(harness, "dependency-source-root-escape-check", invalidSourceRoot, "E_MANIFEST", "../validation/hob.toml");

        var symlinkPackage = await harness.WritePackageAsync(
            "package-source-root-symlink",
            CliPackageManifest().Replace("source_root = \"src\"", "source_root = \"src/link\"", StringComparison.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));
        var sourceDirectoryLink = Path.Combine(symlinkPackage, "src", "link");
        var externalSourceDirectory = Path.Combine(harness.TemporaryRoot, "external-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(externalSourceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(externalSourceDirectory, "main.hob"),
            "module app::main; pub fn main() -> i32 effects {} { return 1; }");
        try
        {
            Directory.CreateSymbolicLink(sourceDirectoryLink, externalSourceDirectory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Console.WriteLine("SKIP source_root reparse-point subcase: this host does not permit directory symbolic links.");
            return;
        }

        await AssertPackageDiagnosticAsync(harness, "package-source-root-symlink-check", symlinkPackage, "E_MANIFEST", "hob.toml");
    }

    private static async Task TestPackageAotCommandValidation(Harness harness)
    {
        const string main = "module app::main; pub fn main() -> i32 effects {} { return 41; }";
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = main
        };
        var missingRid = await harness.InvokePackageAsync(
            "package-aot-missing-rid", "build", CliPackageManifest(), files, "--aot");
        AssertBuildTargetRejected(missingRid, "requires --rid RID");

        var unsupportedRid = await harness.InvokePackageAsync(
            "package-aot-unsupported-rid", "build", CliPackageManifest(), files, "--aot", "--rid", "osx-x64");
        AssertBuildTargetRejected(unsupportedRid, "Unsupported AOT runtime identifier");

        var libraryRoot = await harness.WritePackageAsync(
            "package-aot-library-rejected",
            LibraryPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.hob"] = "module library; pub fn value() -> i32 effects {} { return 1; }"
            });
        var libraryAot = await harness.InvokePackageDirectoryAsync(
            "package-aot-library-rejected",
            libraryRoot,
            "build",
            "--aot",
            "--rid",
            CurrentHostAotRid());
        AssertBuildTargetRejected(libraryAot, "only supported for cli packages");
        AssertTrue(!Directory.Exists(Path.Combine(libraryRoot, "out")),
            "A library package must be rejected before publish output is created.");
    }

    private static async Task TestMaintainedPackageExample(Harness harness)
    {
        var packageRoot = Path.Combine(harness.RepositoryRoot, "examples", "library-package");
        var manifest = await File.ReadAllTextAsync(Path.Combine(packageRoot, "hob.toml"));
        var normalizedManifest = manifest.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var source = await File.ReadAllTextAsync(Path.Combine(packageRoot, "src", "app", "main.hob"));
        AssertTrue(normalizedManifest.Contains("[dependencies]\nvalidation = \"../text-validation\"", StringComparison.Ordinal),
            "The maintained CLI package must resolve validation from its sibling package path.");
        AssertTrue(source.Contains("validation::text::validation::require", StringComparison.Ordinal),
            "The maintained CLI must use qualified validation and inferred generic APIs through its dependency alias.");
        AssertTrue(!File.Exists(Path.Combine(packageRoot, "src", "text", "validation.hob")),
            "The consumer must not contain a copied validation source module.");
        AssertTrue(File.Exists(Path.Combine(packageRoot, "..", "text-validation", "src", "text", "validation.hob")),
            "The imported validation source should live in the sibling package.");
        AssertTrue(File.Exists(Path.Combine(packageRoot, "hob.lock")),
            "The maintained path-dependent package should include its generated lockfile.");

        var check = await harness.InvokePackageDirectoryAsync("maintained-package-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));
        var build = await harness.InvokePackageDirectoryAsync("maintained-package-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        var run = await harness.InvokePackageDirectoryAsync("maintained-package-run", packageRoot, "run");
        AssertRunOutput("ready" + Environment.NewLine, run);
    }

    private static async Task TestMaintainedWebExample(Harness harness)
    {
        var packageRoot = Path.Combine(harness.RepositoryRoot, "examples", "web");
        var manifest = await File.ReadAllTextAsync(Path.Combine(packageRoot, "hob.toml"));
        var normalizedManifest = manifest.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        AssertTrue(normalizedManifest.Contains("kind = \"web\"", StringComparison.Ordinal),
            "The maintained web sample must declare kind=web.");
        AssertTrue(normalizedManifest.Contains("entry_module = \"app::main\"", StringComparison.Ordinal),
            "The maintained web sample must declare its route entry module.");
        AssertTrue(normalizedManifest.Contains("sqlite_path = \"data/greeting.sqlite3\"", StringComparison.Ordinal)
            && normalizedManifest.Contains("sqlite_schema = \"db/schema.sql\"", StringComparison.Ordinal),
            "The maintained web sample must declare package-relative SQLite paths.");
        AssertTrue(normalizedManifest.Contains(
                "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\nfs.write = \"allow\"",
                StringComparison.Ordinal),
            "The maintained web sample must explicitly grant listening, both database capabilities, and filesystem writes.");
        AssertTrue(normalizedManifest.Contains("[dependencies]\nvalidation = \"../text-validation\"", StringComparison.Ordinal),
            "The maintained web sample must consume the shared validation package.");
        AssertTrue(File.Exists(Path.Combine(packageRoot, "hob.lock")),
            "The maintained web sample must include its path dependency lockfile.");
        var schemaPath = Path.Combine(packageRoot, "db", "schema.sql");
        AssertTrue(File.Exists(schemaPath), "The maintained web sample must include its SQLite schema file.");
        var schema = await File.ReadAllTextAsync(schemaPath);
        AssertTrue(schema.Contains("CREATE TABLE IF NOT EXISTS greeting", StringComparison.Ordinal)
            && schema.Contains("ON CONFLICT(id) DO NOTHING", StringComparison.Ordinal),
            "The maintained SQLite schema and seed data must be safe to apply on every startup.");

        var sourcePath = Path.Combine(packageRoot, "src", "app", "main.hob");
        var source = await File.ReadAllTextAsync(sourcePath);
        AssertTrue(source.Contains("html.document", StringComparison.Ordinal)
            && source.Contains("html.heading", StringComparison.Ordinal)
            && source.Contains("html.paragraph", StringComparison.Ordinal)
            && source.Contains("html.concat", StringComparison.Ordinal),
            "The maintained web page must use the safe Html builder API.");
        AssertTrue(source.Contains("validation::text::validation::normalize", StringComparison.Ordinal),
            "The POST handler must normalize its request through the shared validation library.");
        AssertTrue(source.Contains("Validation.Valid", StringComparison.Ordinal)
            && source.Contains(".value", StringComparison.Ordinal),
            "The POST handler must match the generic validation union and explicitly project Normalized<Text>.value.");
        AssertTrue(source.Contains("db.query_one", StringComparison.Ordinal)
            && source.Contains("tx.execute", StringComparison.Ordinal)
            && source.Contains("DbError.Statement", StringComparison.Ordinal)
            && source.Contains("DbError.RowShape", StringComparison.Ordinal)
            && source.Contains("await writer.write_text_async", StringComparison.Ordinal)
            && source.Contains("route POST \"/api/note\"", StringComparison.Ordinal)
            && source.Contains("route GET \"/api/greeting/{id}\"", StringComparison.Ordinal)
            && source.Contains("path id: i32;", StringComparison.Ordinal)
            && source.Contains("query name: Text;", StringComparison.Ordinal)
            && source.Contains("query city: Option<Text>;", StringComparison.Ordinal)
            && source.Contains("route GET \"/health\"", StringComparison.Ordinal),
            "The maintained web sample must exercise typed dynamic SQLite lookup bindings, transactional writes, typed database errors, an async filesystem write route, and /health.");

        var check = await harness.InvokePackageDirectoryAsync("maintained-web-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var firstBuild = await harness.InvokePackageDirectoryAsync("maintained-web-build-first", packageRoot, "build");
        var firstArtifact = AssertBuiltWebApplication(firstBuild, packageRoot);
        var firstDependencyManifest = await File.ReadAllTextAsync(Path.ChangeExtension(firstArtifact, ".deps.json"));
        AssertBuiltSqliteRuntimeAssets(firstArtifact, firstDependencyManifest);
        var firstOpenApiPath = Path.Combine(Path.GetDirectoryName(firstArtifact)!, "openapi.json");
        AssertTrue(File.Exists(firstOpenApiPath), $"Expected the generated OpenAPI artifact at {firstOpenApiPath}.");
        var firstOpenApiBytes = await File.ReadAllBytesAsync(firstOpenApiPath);
        using (var document = JsonDocument.Parse(firstOpenApiBytes))
        {
            var paths = document.RootElement.GetProperty("paths");
            AssertTrue(paths.TryGetProperty("/api/greeting", out var greetingPath),
                "OpenAPI should describe the SQLite-backed greeting route.");
            AssertTrue(greetingPath.TryGetProperty("get", out _) && greetingPath.TryGetProperty("post", out _),
                "OpenAPI should describe both GET and POST mappings at the shared static path.");
            AssertTrue(paths.TryGetProperty("/api/greeting/{id}", out var dynamicGreetingPath),
                "OpenAPI should describe the dynamic SQLite-backed greeting lookup route.");
            var dynamicGreetingGet = dynamicGreetingPath.GetProperty("get");
            var greetingParameters = dynamicGreetingGet.GetProperty("parameters").EnumerateArray().ToArray();
            AssertEqual(3, greetingParameters.Length,
                "The dynamic greeting route should publish its path, required query, and optional query parameters.");
            AssertOpenApiParameter(greetingParameters[0], "id", "path", true, "integer", "int32");
            AssertOpenApiParameter(greetingParameters[1], "name", "query", true, "string", null);
            AssertOpenApiParameter(greetingParameters[2], "city", "query", false, "string", null);
            AssertTrue(paths.TryGetProperty("/reset", out var resetPath)
                && resetPath.TryGetProperty("get", out var resetOperation)
                && resetOperation.GetProperty("responses").TryGetProperty("205", out var resetResponse)
                && !resetResponse.TryGetProperty("content", out _),
                "OpenAPI 205 responses should not advertise a response body.");
            AssertTrue(paths.TryGetProperty("/health", out _),
                "OpenAPI should include the ordinary declared health route.");
            AssertTrue(paths.TryGetProperty("/api/note", out var openApiNotePath),
                "OpenAPI should include the async note POST route.");
            AssertTrue(openApiNotePath.TryGetProperty("post", out var notePost),
                "OpenAPI should expose a POST operation for the async note route.");
            AssertTrue(notePost.TryGetProperty("requestBody", out var noteBody)
                && noteBody.GetProperty("required").GetBoolean(),
                "OpenAPI should describe the async note POST route and its required request body.");
            AssertTrue(notePost.GetProperty("responses").TryGetProperty("204", out var noteSaved)
                && !noteSaved.TryGetProperty("content", out _),
                "The async note route's successful 204 response should not advertise a body.");
        }

        var secondBuild = await harness.InvokePackageDirectoryAsync("maintained-web-build-second", packageRoot, "build");
        var secondArtifact = AssertBuiltWebApplication(secondBuild, packageRoot);
        var secondDependencyManifest = await File.ReadAllTextAsync(Path.ChangeExtension(secondArtifact, ".deps.json"));
        AssertBuiltSqliteRuntimeAssets(secondArtifact, secondDependencyManifest);
        var secondOpenApiPath = Path.Combine(Path.GetDirectoryName(secondArtifact)!, "openapi.json");
        AssertTrue(File.Exists(secondOpenApiPath), $"Expected the generated OpenAPI artifact at {secondOpenApiPath}.");
        var secondOpenApiBytes = await File.ReadAllBytesAsync(secondOpenApiPath);
        AssertTrue(firstOpenApiBytes.SequenceEqual(secondOpenApiBytes),
            "Equivalent web builds must emit byte-for-byte deterministic OpenAPI documents.");

        var runtimeConfigPath = Path.ChangeExtension(firstArtifact, ".runtimeconfig.json");
        AssertTrue(File.Exists(runtimeConfigPath), $"Expected the managed web runtime configuration at {runtimeConfigPath}.");
        using (var runtimeConfig = JsonDocument.Parse(await File.ReadAllBytesAsync(runtimeConfigPath)))
        {
            var runtimeOptions = runtimeConfig.RootElement.GetProperty("runtimeOptions");
            var configProperties = runtimeOptions.GetProperty("configProperties");
            AssertTrue(configProperties.TryGetProperty("System.GC.Server", out var serverGc)
                && !serverGc.GetBoolean(),
                "The generated web application must explicitly use Workstation GC.");
            var frameworks = new List<JsonElement>();
            if (runtimeOptions.TryGetProperty("frameworks", out var frameworkList))
                frameworks.AddRange(frameworkList.EnumerateArray());
            else if (runtimeOptions.TryGetProperty("framework", out var singleFramework))
                frameworks.Add(singleFramework);
            AssertTrue(frameworks.Any(framework =>
                    framework.TryGetProperty("name", out var frameworkName)
                    && frameworkName.GetString() == "Microsoft.AspNetCore.App"),
                "The managed web artifact must reference the ASP.NET Core shared framework.");
        }

        var missingGrantManifest = "name = \"web-missing-grant\"\n"
            + "version = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n";
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "web-missing-net-listen-grant",
            missingGrantManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module app::main;
                    union Reply { Ok }
                    fn handler() -> self::app::main::Reply effects {} {
                        return self::app::main::Reply.Ok;
                    }
                    route GET "/" {
                        handler: self::app::main::handler;
                        response Ok: 200;
                    }
                    """
            },
            "E_CAPABILITY_MISSING",
            "hob.toml");

        var aot = await harness.InvokePackageDirectoryAsync(
            "maintained-web-aot-rejected", packageRoot, "build", "--aot", "--rid", CurrentHostAotRid());
        AssertTrue(aot.ExitCode != 0, Describe(aot));
        AssertEqual(string.Empty, aot.StandardOutput, Describe(aot));
        AssertTrue(aot.StandardError.Contains("E_BUILD_TARGET", StringComparison.Ordinal),
            $"Web packages must be rejected before Native AOT publishing. {Describe(aot)}");

        var databasePath = Path.Combine(harness.TemporaryRoot, "maintained-web.sqlite3");
        var databaseEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_SQLITE_PATH"] = databasePath
        };
        var runtimeCopyRoot = Path.Combine(harness.TemporaryRoot, $"maintained-web-runtime-{Guid.NewGuid():N}");
        var runtimePackageRoot = Path.Combine(runtimeCopyRoot, "web");
        var runtimeDependencyRoot = Path.Combine(runtimeCopyRoot, "text-validation");
        CopyMaintainedPackageInputs(packageRoot, runtimePackageRoot, includeSqliteSchema: true);
        CopyMaintainedPackageInputs(Path.Combine(harness.RepositoryRoot, "examples", "text-validation"), runtimeDependencyRoot);
        var runtimeSourcePath = Path.Combine(runtimePackageRoot, "src", "app", "main.hob");
        var runtimeSource = (await File.ReadAllTextAsync(runtimeSourcePath))
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        const string createHandlerSignature =
            "fn create(request: self::app::main::GreetingRequest, db: DbWrite) -> self::app::main::CreateReply effects { db.write } {";
        AssertTrue(runtimeSource.Contains(createHandlerSignature, StringComparison.Ordinal),
            "The copied web source must retain the expected create handler signature before N3 instrumentation.");
        var instrumentedSource = runtimeSource.Replace(createHandlerSignature,
            "fn create(request: self::app::main::GreetingRequest, writer: FsWrite, db: DbWrite) -> self::app::main::CreateReply effects { fs.write, db.write } {\n"
            + "    let invoked: Result<bool, FsError> = writer.write_text(\"data/create-handler-invoked.txt\", \"invoked\");",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(runtimeSourcePath, instrumentedSource, new UTF8Encoding(false));
        var runtimeCopyLock = await harness.InvokePackageDirectoryAsync(
            "maintained-web-runtime-instrumented-lock", runtimePackageRoot, "lock");
        AssertEqual(0, runtimeCopyLock.ExitCode, Describe(runtimeCopyLock));
        var port = GetUnusedLoopbackPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}");
        var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(4) };
        var noteDirectory = Path.Combine(runtimePackageRoot, "data");
        Directory.CreateDirectory(noteDirectory);
        var notePath = Path.Combine(noteDirectory, "async-note.txt");
        var invocationMarkerPath = Path.Combine(noteDirectory, "create-handler-invoked.txt");
        const string invocationMarker = "invoked";
        void ResetInvocationMarker()
        {
            if (File.Exists(invocationMarkerPath))
                File.Delete(invocationMarkerPath);
            AssertTrue(!File.Exists(invocationMarkerPath),
                "The invocation marker must be absent immediately before each calibrated request.");
        }
        void AssertInvocationMarker(string requestDescription)
        {
            AssertTrue(File.Exists(invocationMarkerPath),
                $"The {requestDescription} should enter the create handler and write its invocation marker.");
            AssertEqual(invocationMarker, File.ReadAllText(invocationMarkerPath),
                $"The {requestDescription} should write the exact constant invocation marker.");
        }
        async Task AssertRejectedBeforeCreateHandlerAsync(
            string requestDescription,
            Func<Task<HttpResponseMessage>> send,
            HttpStatusCode expectedStatus)
        {
            ResetInvocationMarker();
            using var response = await send();
            AssertEqual(expectedStatus, response.StatusCode, $"The {requestDescription} should be rejected with {expectedStatus}.");
            AssertTrue(!File.Exists(invocationMarkerPath),
                $"The {requestDescription} must be rejected before the handler changes persisted state.");
        }
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "maintained-web-run", runtimePackageRoot, databaseEnvironment, "--urls", baseAddress.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);

            using (var page = await client.GetAsync("/"))
            {
                AssertEqual(HttpStatusCode.OK, page.StatusCode, "The HTML route should return 200.");
                AssertEqual("text/html", page.Content.Headers.ContentType?.MediaType,
                    "The HTML route should set a text/html content type.");
                var html = await page.Content.ReadAsStringAsync();
                AssertTrue(html.Contains("Hello from hob", StringComparison.Ordinal),
                    $"The server-rendered page should read its initial greeting from SQLite. Received: {html}");
                AssertTrue(!html.Contains("<script>", StringComparison.Ordinal),
                    "The HTML response must not render user text as markup.");
            }

            using (var health = await client.GetAsync("/health"))
            {
                AssertEqual(HttpStatusCode.OK, health.StatusCode, "The ordinary health route should return 200.");
                AssertEqual(0, (await health.Content.ReadAsByteArrayAsync()).Length,
                    "The health route should not return an undeclared body.");
            }

            const string initialNote = "web async FsWrite λ 😀";
            using (var saved = await client.PostAsync("/api/note", JsonBody(JsonSerializer.Serialize(new { content = initialNote }))))
                AssertEqual(HttpStatusCode.NoContent, saved.StatusCode,
                    "The async note route should return its declared 204 after writing.");
            var initialNoteBytes = new UTF8Encoding(false, true).GetBytes(initialNote);
            AssertTrue(File.ReadAllBytes(notePath).AsSpan().SequenceEqual(initialNoteBytes),
                "The async note route should persist exact UTF-8 bytes without a BOM.");

            const string shorterNote = "shorter λ";
            using (var saved = await client.PostAsync("/api/note", JsonBody(JsonSerializer.Serialize(new { content = shorterNote }))))
                AssertEqual(HttpStatusCode.NoContent, saved.StatusCode,
                    "The async note route should overwrite an existing destination.");
            var preservedNoteBytes = new UTF8Encoding(false, true).GetBytes(shorterNote);
            AssertTrue(File.ReadAllBytes(notePath).AsSpan().SequenceEqual(preservedNoteBytes),
                "The async note route should truncate longer prior contents on overwrite.");
            AssertTrue(!Directory.EnumerateFiles(noteDirectory, "*.tmp", SearchOption.TopDirectoryOnly).Any(),
                "Successful async web writes must leave no temporary files beside the destination.");

            using (var get = await client.GetAsync("/api/greeting"))
            {
                AssertEqual(HttpStatusCode.OK, get.StatusCode, "The GET JSON route should return its mapped 200 status.");
                AssertEqual("application/json", get.Content.Headers.ContentType?.MediaType,
                    "The GET route should set an application/json content type.");
                using var greeting = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
                AssertEqual("Hello from hob", greeting.RootElement.GetProperty("message").GetString(),
                    "The GET route should serialize its nested response struct.");
                AssertEqual("London", greeting.RootElement.GetProperty("profile").GetProperty("city").GetString(),
                    "The GET route should serialize its nested profile.");
            }

            using (var reset = await client.GetAsync("/reset"))
            {
                AssertEqual((HttpStatusCode)205, reset.StatusCode, "The no-content route should return 205.");
                AssertEqual(0, (await reset.Content.ReadAsByteArrayAsync()).Length,
                    "A 205 response must not serialize a body.");
                AssertTrue(reset.Content.Headers.ContentType is null,
                    "A 205 response must not set a content type.");
            }

            ResetInvocationMarker();
            using (var created = await client.PostAsync("/api/greeting", JsonBody(
                       "{\"name\":\" Ada \",\"profile\":{\"city\":\"Paris\"}}")))
            {
                AssertEqual(HttpStatusCode.Created, created.StatusCode,
                    "A valid POST body should select the mapped Created status.");
                using var response = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
                AssertEqual("Ada", response.RootElement.GetProperty("message").GetString(),
                    "The POST handler should normalize input through the validation library.");
                AssertEqual("Paris", response.RootElement.GetProperty("profile").GetProperty("city").GetString(),
                    "The POST route should preserve its nested JSON request and response values.");
            }
            AssertInvocationMarker("valid success request");

            using (var lookupWithoutOptionalCity = await client.GetAsync("/api/greeting/1?name=Ada"))
            {
                AssertEqual(HttpStatusCode.OK, lookupWithoutOptionalCity.StatusCode,
                    "A required path i32 and query Text should match when the optional city query is absent.");
                using var response = JsonDocument.Parse(await lookupWithoutOptionalCity.Content.ReadAsStringAsync());
                AssertEqual("Ada", response.RootElement.GetProperty("message").GetString(),
                    "The dynamic route should load the record selected by its path binding.");
                AssertEqual("Paris", response.RootElement.GetProperty("profile").GetProperty("city").GetString(),
                    "An absent optional city query should be passed as None to the lookup handler.");
            }

            using (var lookupWithOptionalCity = await client.GetAsync("/api/greeting/1?name=Ada&city=Paris"))
                AssertEqual(HttpStatusCode.OK, lookupWithOptionalCity.StatusCode,
                    "A matching present optional city query should select the Found response.");
            using (var lookupWithEmptyCity = await client.GetAsync("/api/greeting/1?name=Ada&city="))
                AssertEqual(HttpStatusCode.NotFound, lookupWithEmptyCity.StatusCode,
                    "A present empty Option<Text> query should reach the handler as Some(empty text).");
            using (var lookupWithNonmatchingValues = await client.GetAsync("/api/greeting/1?name=Grace&city=Paris"))
                AssertEqual(HttpStatusCode.NotFound, lookupWithNonmatchingValues.StatusCode,
                    "Nonmatching present query values should select the declared Missing response.");
            await AssertRouteBindingBadRequestAsync(client, "/api/greeting/1");
            await AssertRouteBindingBadRequestAsync(client, "/api/greeting/1?name=Ada&name=Grace");
            await AssertRouteBindingBadRequestAsync(client, "/api/greeting/1?name=Ada&city=Paris&city=Rome");
            await AssertRouteBindingBadRequestAsync(client, "/api/greeting/not-an-integer?name=Ada");
            await AssertRouteBindingBadRequestAsync(client, "/api/greeting/2147483648?name=Ada");

            const string maliciousName = "' OR 1=1; DROP TABLE greeting; -- <script>alert('x')</script>";
            var maliciousBody = JsonSerializer.Serialize(new
            {
                name = maliciousName,
                profile = new { city = "Paris" }
            });
            using (var maliciousPost = await client.PostAsync("/api/greeting", JsonBody(maliciousBody)))
            {
                AssertEqual(HttpStatusCode.Created, maliciousPost.StatusCode,
                    "A malicious-looking string should be accepted as a parameter value.");
                using var response = JsonDocument.Parse(await maliciousPost.Content.ReadAsStringAsync());
                AssertEqual(maliciousName, response.RootElement.GetProperty("message").GetString(),
                    "The malicious-looking string should round-trip unchanged through a parameterized write.");
            }

            using (var maliciousGet = await client.GetAsync("/api/greeting"))
            {
                AssertEqual(HttpStatusCode.OK, maliciousGet.StatusCode,
                    "A parameterized write must leave the SQLite table structure intact.");
                using var response = JsonDocument.Parse(await maliciousGet.Content.ReadAsStringAsync());
                AssertEqual(maliciousName, response.RootElement.GetProperty("message").GetString(),
                    "A malicious-looking value should read back as data.");
            }

            using (var page = await client.GetAsync("/"))
            {
                var html = await page.Content.ReadAsStringAsync();
                AssertTrue(html.Contains("&lt;script&gt;alert(&#x27;x&#x27;)&lt;/script&gt;", StringComparison.Ordinal),
                    $"Persisted untrusted text should render escaped in HTML. Received: {html}");
                AssertTrue(!html.Contains("<script>", StringComparison.Ordinal),
                    "Persisted user text must not render an executable script element.");
            }

            ResetInvocationMarker();
            using (var invalid = await client.PostAsync("/api/greeting", JsonBody(
                       "{\"name\":\"  \",\"profile\":{\"city\":\"Paris\"}}")))
                AssertEqual(HttpStatusCode.BadRequest, invalid.StatusCode,
                    "A validation failure should select the mapped Invalid status.");
            AssertInvocationMarker("validly decoded domain-error request");

            await AssertRejectedBeforeCreateHandlerAsync(
                "malformed JSON", () => client.PostAsync("/api/greeting", JsonBody("{broken")), HttpStatusCode.BadRequest);

            await AssertRejectedBeforeCreateHandlerAsync(
                "incompatible top-level JSON type", () => client.PostAsync("/api/greeting", JsonBody("[]")), HttpStatusCode.BadRequest);

            await AssertRejectedBeforeCreateHandlerAsync(
                "incompatible top-level field type",
                () => client.PostAsync("/api/greeting", JsonBody("{\"name\":42,\"profile\":{\"city\":\"Paris\"}}")),
                HttpStatusCode.BadRequest);

            await AssertRejectedBeforeCreateHandlerAsync(
                "unknown JSON field",
                () => client.PostAsync("/api/greeting", JsonBody("{\"name\":\"Ada\",\"profile\":{\"city\":\"Paris\"},\"extra\":true}")),
                HttpStatusCode.BadRequest);

            await AssertRejectedBeforeCreateHandlerAsync(
                "duplicate JSON field",
                () => client.PostAsync("/api/greeting", JsonBody("{\"name\":\"Ada\",\"name\":\"Grace\",\"profile\":{\"city\":\"Paris\"}}")),
                HttpStatusCode.BadRequest);

            await AssertRejectedBeforeCreateHandlerAsync(
                "missing JSON field",
                () => client.PostAsync("/api/greeting", JsonBody("{\"name\":\"Ada\"}")),
                HttpStatusCode.BadRequest);

            await AssertRejectedBeforeCreateHandlerAsync(
                "incompatible nested JSON field type",
                () => client.PostAsync("/api/greeting", JsonBody("{\"name\":\"Ada\",\"profile\":{\"city\":42}}")),
                HttpStatusCode.BadRequest);

            var boundaryPrefix = "{\"name\":\"Ada\",\"profile\":{\"city\":\"Paris\"}}";
            var exactlyOneMiB = boundaryPrefix + new string(' ', 1_048_576 - Encoding.UTF8.GetByteCount(boundaryPrefix));
            ResetInvocationMarker();
            using (var boundary = await client.PostAsync("/api/greeting", JsonBody(exactlyOneMiB)))
                AssertEqual(HttpStatusCode.Created, boundary.StatusCode,
                    "A valid request body exactly at the 1 MiB limit should be accepted.");
            AssertInvocationMarker("valid 1 MiB boundary request");

            var oversizedJson = "{\"name\":\"" + new string('x', 1_048_600)
                + "\",\"profile\":{\"city\":\"Paris\"}}";
            await AssertRejectedBeforeCreateHandlerAsync(
                "oversized Content-Length JSON", () => client.PostAsync("/api/greeting", JsonBody(oversizedJson)), (HttpStatusCode)413);

            var oversizedBytes = Encoding.UTF8.GetBytes(oversizedJson);
            using (var chunkedContent = new StreamContent(new NonSeekableMemoryStream(oversizedBytes)))
            using (var chunkedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/greeting")
            {
                Content = chunkedContent
            })
            {
                chunkedContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                chunkedRequest.Headers.TransferEncodingChunked = true;
                AssertTrue(chunkedContent.Headers.ContentLength is null,
                    "The oversized stream request must not declare Content-Length.");
                await AssertRejectedBeforeCreateHandlerAsync(
                    "oversized chunked JSON", () => client.SendAsync(chunkedRequest), (HttpStatusCode)413);
            }

            using (var missing = await client.GetAsync("/not-found"))
                AssertEqual(HttpStatusCode.NotFound, missing.StatusCode, "An unmapped path should return 404.");

            using (var wrongMethod = await client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/greeting")))
                AssertEqual(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode,
                    "A static route with a different method should return 405.");

            using (var fault = await client.GetAsync("/fault"))
            {
                AssertEqual(HttpStatusCode.InternalServerError, fault.StatusCode,
                    "An unexpected checked runtime overflow should map to a generic 500 response.");
                AssertEqual("application/json", fault.Content.Headers.ContentType?.MediaType,
                    "The unexpected-fault response should be JSON.");
                if (!fault.Headers.TryGetValues("X-Request-Id", out var requestIds) || requestIds is null)
                    throw new InvalidOperationException("The generic 500 response should include an X-Request-Id header.");
                var requestId = requestIds.Single();
                AssertTrue(!string.IsNullOrWhiteSpace(requestId) && requestId.Length <= 128
                    && !requestId.Any(char.IsControl),
                    "The fault request ID should be a short, nonempty header-safe value.");
                var faultBody = await fault.Content.ReadAsStringAsync();
                AssertEqual("{\"error\":\"internal_server_error\"}", faultBody,
                    "The unexpected-fault response should expose only its fixed safe body.");
                AssertTrue(!faultBody.Contains("OverflowException", StringComparison.Ordinal)
                    && !faultBody.Contains(" at ", StringComparison.Ordinal)
                    && !faultBody.Contains(harness.RepositoryRoot, StringComparison.OrdinalIgnoreCase)
                    && !faultBody.Contains("2147483647", StringComparison.Ordinal),
                    "The generic fault response must not leak exception details, stack traces, source paths, or values.");
            }

            using (var persist = await client.PostAsync("/api/greeting", JsonBody(
                       "{\"name\":\"Persisted after restart\",\"profile\":{\"city\":\"Copenhagen\"}}")))
                AssertEqual(HttpStatusCode.Created, persist.StatusCode,
                    "A valid greeting should be written before the host restarts.");

            assertionsCompleted = true;
        }
        finally
        {
            // Close the keep-alive client before asking the managed host to drain and exit.
            client.Dispose();
            if (!process.HasExited)
            {
                // On the successful path, the generated host must observe the wrapper exit and stop itself.
                // If an assertion failed, kill the complete tree so a broken host cannot leak into later cases.
                process.Kill(entireProcessTree: !assertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await process.WaitForExitAsync(termination.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("The managed web process did not terminate within 10 seconds after a tree kill.");
                }
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
            if (Directory.Exists(runtimeCopyRoot)) Directory.Delete(runtimeCopyRoot, recursive: true);
        }

        var restartedPort = GetUnusedLoopbackPort();
        var restartedAddress = new Uri($"http://127.0.0.1:{restartedPort}");
        using var restartedClient = new HttpClient { BaseAddress = restartedAddress, Timeout = TimeSpan.FromSeconds(4) };
        using var restartedProcess = harness.StartWebPackageProcessWithEnvironment(
            "maintained-web-restart", packageRoot, databaseEnvironment, "--urls", restartedAddress.ToString().TrimEnd('/'));
        var restartedStdoutTask = restartedProcess.StandardOutput.ReadToEndAsync();
        var restartedStderrTask = restartedProcess.StandardError.ReadToEndAsync();
        var restartAssertionsCompleted = false;
        try
        {
            await WaitForWebServerAsync(restartedProcess, restartedClient, restartedStdoutTask, restartedStderrTask);
            using (var persisted = await restartedClient.GetAsync("/api/greeting"))
            {
                AssertEqual(HttpStatusCode.OK, persisted.StatusCode,
                    "A greeting must remain available after the web host restarts.");
                using var response = JsonDocument.Parse(await persisted.Content.ReadAsStringAsync());
                AssertEqual("Persisted after restart", response.RootElement.GetProperty("message").GetString(),
                    "SQLite changes should persist across server restarts.");
                AssertEqual("Copenhagen", response.RootElement.GetProperty("profile").GetProperty("city").GetString(),
                    "The persisted nested value should survive a server restart.");
            }

            restartAssertionsCompleted = true;
        }
        finally
        {
            restartedClient.Dispose();
            if (!restartedProcess.HasExited)
            {
                restartedProcess.Kill(entireProcessTree: !restartAssertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await restartedProcess.WaitForExitAsync(termination.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("The maintained web host did not terminate within 10 seconds after restart.");
                }
            }

            await Task.WhenAll(restartedStdoutTask, restartedStderrTask);
            await AssertLoopbackPortReleasedAsync(restartedPort);
        }

        var unrelatedWorkingDirectory = Path.Combine(harness.TemporaryRoot, "different-current-directory");
        Directory.CreateDirectory(unrelatedWorkingDirectory);
        var directPort = GetUnusedLoopbackPort();
        var directAddress = new Uri($"http://127.0.0.1:{directPort}");
        using var directClient = new HttpClient { BaseAddress = directAddress, Timeout = TimeSpan.FromSeconds(4) };
        using var directProcess = harness.StartBuiltWebArtifactProcess(
            "maintained-web-direct-artifact", secondArtifact, unrelatedWorkingDirectory,
            "--urls", directAddress.ToString().TrimEnd('/'));
        var directStdoutTask = directProcess.StandardOutput.ReadToEndAsync();
        var directStderrTask = directProcess.StandardError.ReadToEndAsync();
        var directAssertionsCompleted = false;
        try
        {
            await WaitForWebServerAsync(directProcess, directClient, directStdoutTask, directStderrTask);
            using var directRead = await directClient.GetAsync("/api/greeting");
            AssertEqual(HttpStatusCode.OK, directRead.StatusCode,
                "A directly launched artifact should initialize and read SQLite from its AppContext base directory.");
            using var directResponse = JsonDocument.Parse(await directRead.Content.ReadAsStringAsync());
            AssertEqual("Hello from hob", directResponse.RootElement.GetProperty("message").GetString(),
                "The directly launched artifact should apply its embedded schema and seed data.");
            var appBaseDatabase = Path.Combine(Path.GetDirectoryName(secondArtifact)!, "data", "greeting.sqlite3");
            AssertTrue(File.Exists(appBaseDatabase),
                $"The default database should be based at the built artifact's AppContext directory: {appBaseDatabase}");
            AssertTrue(!File.Exists(Path.Combine(unrelatedWorkingDirectory, "data", "greeting.sqlite3")),
                "The default database path must not follow an unrelated process working directory.");
            directAssertionsCompleted = true;
        }
        finally
        {
            directClient.Dispose();
            if (!directProcess.HasExited)
            {
                directProcess.Kill(entireProcessTree: !directAssertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await directProcess.WaitForExitAsync(termination.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("The directly launched web artifact did not terminate within 10 seconds.");
                }
            }

            await Task.WhenAll(directStdoutTask, directStderrTask);
            await AssertLoopbackPortReleasedAsync(directPort);
        }

        var unmappedGraphRoot = Path.Combine(harness.TemporaryRoot, $"maintained-web-unmapped-{Guid.NewGuid():N}");
        var unmappedPackageRoot = Path.Combine(unmappedGraphRoot, "web");
        CopyMaintainedPackageInputs(packageRoot, unmappedPackageRoot, includeSqliteSchema: true);
        CopyMaintainedPackageInputs(Path.Combine(harness.RepositoryRoot, "examples", "text-validation"),
            Path.Combine(unmappedGraphRoot, "text-validation"));
        var unmappedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        const string createReplyDeclaration =
            "union CreateReply { Created(self::app::main::Greeting), Invalid(Text), StorageFailure(Text) }";
        AssertTrue(unmappedSource.Contains(createReplyDeclaration, StringComparison.Ordinal),
            "The maintained web fixture must retain its expected CreateReply declaration.");
        unmappedSource = unmappedSource.Replace(createReplyDeclaration,
            "union CreateReply { Created(self::app::main::Greeting), Invalid(Text), StorageFailure(Text), Unmapped }",
            StringComparison.Ordinal);
        const string createResponseMapping =
            "    response StorageFailure: 500 json Text;\n}\n\nroute POST \"/api/note\"";
        AssertTrue(unmappedSource.Contains(createResponseMapping, StringComparison.Ordinal),
            "The maintained web fixture must retain the expected create-route response mapping.");
        var unmappedSourcePath = Path.Combine(unmappedPackageRoot, "src", "app", "main.hob");
        var unmappedMapping = "    response StorageFailure: 500 json Text;\n    response Unmapped: 409;\n}\n\nroute POST \"/api/note\"";
        await File.WriteAllTextAsync(unmappedSourcePath, unmappedSource, new UTF8Encoding(false));
        var unmappedLock = await harness.InvokePackageDirectoryAsync(
            "maintained-web-unmapped-reply-lock", unmappedPackageRoot, "lock");
        AssertEqual(0, unmappedLock.ExitCode, Describe(unmappedLock));

        var unmappedCheck = await harness.InvokePackageDirectoryAsync(
            "maintained-web-unmapped-reply-check", unmappedPackageRoot, "check", "--json");
        AssertEqual(1, unmappedCheck.ExitCode, Describe(unmappedCheck));
        var unmappedDiagnostics = ParseDiagnosticSnapshots(unmappedCheck.StandardOutput);
        AssertEqual(1, unmappedDiagnostics.Length, Describe(unmappedCheck));
        AssertEqual("E_ROUTE_RESPONSE_MISSING", unmappedDiagnostics[0].Code, Describe(unmappedCheck));
        AssertEqual("Route responses are missing variants: Unmapped", unmappedDiagnostics[0].Message, Describe(unmappedCheck));
        AssertRangeAtToken(unmappedSource, unmappedDiagnostics[0], "route", 4);
        var unmappedBuild = await harness.InvokePackageDirectoryAsync(
            "maintained-web-unmapped-reply-build", unmappedPackageRoot, "build");
        AssertEqual(1, unmappedBuild.ExitCode, Describe(unmappedBuild));
        AssertTrue(!Directory.Exists(Path.Combine(unmappedPackageRoot, "out")),
            "An unmapped route reply must not produce a build artifact.");
        AssertTrue(!Directory.EnumerateFiles(unmappedPackageRoot, "openapi.json", SearchOption.AllDirectories).Any()
            && !Directory.EnumerateFiles(unmappedPackageRoot, "build-receipt.json", SearchOption.AllDirectories).Any(),
            "An unmapped route reply must not produce OpenAPI or a build receipt.");

        var mappedSource = unmappedSource.Replace(createResponseMapping, unmappedMapping, StringComparison.Ordinal);
        await File.WriteAllTextAsync(unmappedSourcePath, mappedSource, new UTF8Encoding(false));
        var mappedLock = await harness.InvokePackageDirectoryAsync(
            "maintained-web-mapped-reply-lock", unmappedPackageRoot, "lock");
        AssertEqual(0, mappedLock.ExitCode, Describe(mappedLock));
        var mappedCheck = await harness.InvokePackageDirectoryAsync(
            "maintained-web-mapped-reply-check", unmappedPackageRoot, "check", "--json");
        AssertEqual(0, mappedCheck.ExitCode, Describe(mappedCheck));
        var mappedBuild = await harness.InvokePackageDirectoryAsync(
            "maintained-web-mapped-reply-build", unmappedPackageRoot, "build");
        var mappedArtifact = AssertBuiltWebApplication(mappedBuild, unmappedPackageRoot);
        var mappedOpenApiPath = Path.Combine(Path.GetDirectoryName(mappedArtifact)!, "openapi.json");
        using (var mappedOpenApi = JsonDocument.Parse(await File.ReadAllBytesAsync(mappedOpenApiPath)))
        {
            var responses = mappedOpenApi.RootElement.GetProperty("paths").GetProperty("/api/greeting")
                .GetProperty("post").GetProperty("responses");
            AssertTrue(responses.TryGetProperty("409", out _),
                "Adding the Unmapped response mapping must restore successful checking and generated OpenAPI.");
        }

    }

    private static async Task TestSqliteTransactions(Harness harness)
    {
        await using var cancellationServer = new RawHttpServer();
        const string source = """
            module app::main;

            struct WriteParameters { id: i32, value: Text }
            struct EmptyParameters {}
            struct CountRow { total: i32 }

            union CommitReply { Committed(bool), Failure }
            union SimpleReply { Done, Failure }
            union FailedReply { ExpectedCommitFailure, CommitSucceeded(bool), Failure }
            union StateReply { Count(i32), Failure }

            fn commit_first(db: DbWrite) -> self::app::main::CommitReply effects { db.write } {
                with db.begin() as tx {
                    let written: Result<i32, DbError> = tx.execute(
                        "INSERT INTO record (id, value) VALUES ($id, $value)",
                        self::app::main::WriteParameters { id: 1, value: "committed" }
                    );
                    return match written {
                        Ok(count) => match tx.commit() {
                            Ok(committed) => self::app::main::CommitReply.Committed(committed),
                            Err(error) => self::app::main::CommitReply.Failure
                        },
                        Err(error) => self::app::main::CommitReply.Failure
                    };
                }
            }

            fn rollback_by_scope_exit(db: DbWrite) -> self::app::main::SimpleReply effects { db.write } {
                with db.begin() as tx {
                    let written: Result<i32, DbError> = tx.execute(
                        "INSERT INTO record (id, value) VALUES ($id, $value)",
                        self::app::main::WriteParameters { id: 2, value: "implicit rollback" }
                    );
                    let succeeded: bool = match written {
                        Ok(count) => true,
                        Err(error) => false
                    };
                    if succeeded == false {
                        return self::app::main::SimpleReply.Failure;
                    }
                }
                return self::app::main::SimpleReply.Done;
            }

            fn failed_statement_blocks_commit(db: DbWrite) -> self::app::main::FailedReply effects { db.write } {
                with db.begin() as tx {
                    let first: Result<i32, DbError> = tx.execute(
                        "INSERT INTO record (id, value) VALUES ($id, $value)",
                        self::app::main::WriteParameters { id: 3, value: "must roll back" }
                    );
                    let first_succeeded: bool = match first {
                        Ok(count) => true,
                        Err(error) => false
                    };
                    if first_succeeded == false {
                        return self::app::main::FailedReply.Failure;
                    }

                    let duplicate: Result<i32, DbError> = tx.execute(
                        "INSERT INTO record (id, value) VALUES ($id, $value)",
                        self::app::main::WriteParameters { id: 3, value: "duplicate" }
                    );
                    let duplicate_succeeded: bool = match duplicate {
                        Ok(count) => true,
                        Err(error) => false
                    };
                    if duplicate_succeeded {
                        return self::app::main::FailedReply.Failure;
                    }

                    let commit_result: Result<bool, DbError> = tx.commit();
                    return match commit_result {
                        Ok(committed) => self::app::main::FailedReply.CommitSucceeded(committed),
                        Err(error) => match error {
                            DbError.Statement => self::app::main::FailedReply.ExpectedCommitFailure,
                            DbError.RowShape => self::app::main::FailedReply.Failure
                        }
                    };
                }
            }

            fn rollback_by_early_return(db: DbWrite) -> self::app::main::SimpleReply effects { db.write } {
                with db.begin() as tx {
                    let written: Result<i32, DbError> = tx.execute(
                        "INSERT INTO record (id, value) VALUES ($id, $value)",
                        self::app::main::WriteParameters { id: 4, value: "early return" }
                    );
                    return match written {
                        Ok(count) => self::app::main::SimpleReply.Done,
                        Err(error) => self::app::main::SimpleReply.Failure
                    };
                }
            }

            fn rollback_on_runtime_error(db: DbWrite) -> self::app::main::SimpleReply effects { db.write } {
                with db.begin() as tx {
                    let written: Result<i32, DbError> = tx.execute(
                        "INSERT INTO record (id, value) VALUES ($id, $value)",
                        self::app::main::WriteParameters { id: 5, value: "runtime error" }
                    );
                    let overflow: i32 = 2147483647 + 1;
                    return self::app::main::SimpleReply.Done;
                }
            }

            async fn rollback_on_cancellation(db: DbWrite, client: HttpClient) -> self::app::main::SimpleReply effects { db.write, net.client } {
                with db.begin() as tx {
                    let written: Result<i32, DbError> = tx.execute(
                        "INSERT INTO record (id, value) VALUES ($id, $value)",
                        self::app::main::WriteParameters { id: 6, value: "cancellation" }
                    );
                    let response: Result<HttpResponse, HttpError> = await client.get_text_async("/hold-cancel");
                    return match response {
                        Ok(value) => self::app::main::SimpleReply.Failure,
                        Err(error) => self::app::main::SimpleReply.Failure
                    };
                }
            }

            async fn rollback_on_deadline(db: DbWrite, client: HttpClient) -> self::app::main::SimpleReply effects { db.write, net.client } {
                with db.begin() as tx {
                    let written: Result<i32, DbError> = tx.execute(
                        "INSERT INTO record (id, value) VALUES ($id, $value)",
                        self::app::main::WriteParameters { id: 8, value: "deadline" }
                    );
                    let response: Result<HttpResponse, HttpError> = await client.get_text_async("/hold-deadline");
                    return match response {
                        Ok(value) => self::app::main::SimpleReply.Failure,
                        Err(error) => self::app::main::SimpleReply.Failure
                    };
                }
            }

            fn commit_after_unwound_scope(db: DbWrite) -> self::app::main::SimpleReply effects { db.write } {
                with db.begin() as tx {
                    let written: Result<i32, DbError> = tx.execute(
                        "INSERT INTO record (id, value) VALUES ($id, $value)",
                        self::app::main::WriteParameters { id: 7, value: "after unwind" }
                    );
                    return match written {
                        Ok(count) => match tx.commit() {
                            Ok(committed) => self::app::main::SimpleReply.Done,
                            Err(error) => self::app::main::SimpleReply.Failure
                        },
                        Err(error) => self::app::main::SimpleReply.Failure
                    };
                }
            }

            fn state(db: DbRead) -> self::app::main::StateReply effects { db.read } {
                let loaded: Result<Option<self::app::main::CountRow>, DbError> = db.query_one(
                    "SELECT COUNT(*) AS total FROM record",
                    self::app::main::EmptyParameters {}
                );
                return match loaded {
                    Ok(value) => match value {
                        Some(row) => self::app::main::StateReply.Count(row.total),
                        None => self::app::main::StateReply.Failure
                    },
                    Err(error) => self::app::main::StateReply.Failure
                };
            }

            route GET "/" {
                handler: self::app::main::state;
                response Count: 200 json i32;
                response Failure: 500;
            }

            route GET "/commit" {
                handler: self::app::main::commit_first;
                response Committed: 200 json bool;
                response Failure: 500;
            }

            route GET "/rollback" {
                handler: self::app::main::rollback_by_scope_exit;
                response Done: 200;
                response Failure: 500;
            }

            route GET "/failed" {
                handler: self::app::main::failed_statement_blocks_commit;
                response ExpectedCommitFailure: 200;
                response CommitSucceeded: 409 json bool;
                response Failure: 500;
            }

            route GET "/early-return" {
                handler: self::app::main::rollback_by_early_return;
                response Done: 200;
                response Failure: 500;
            }

            route GET "/runtime-error" {
                handler: self::app::main::rollback_on_runtime_error;
                response Done: 200;
                response Failure: 500;
            }

            route GET "/cancel" {
                handler: self::app::main::rollback_on_cancellation;
                response Done: 200;
                response Failure: 500;
            }

            route GET "/deadline" {
                handler: self::app::main::rollback_on_deadline;
                response Done: 200;
                response Failure: 500;
            }

            route GET "/commit-after-unwind" {
                handler: self::app::main::commit_after_unwound_scope;
                response Done: 200;
                response Failure: 500;
            }
            """;
        var manifest = "name = \"sqlite-transaction-runtime\"\n"
            + "version = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
            + "sqlite_path = \"data/transactions.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
            + $"http_origin = \"{cancellationServer.Origin}\"\n"
            + "request_timeout_ms = 2500\n"
            + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\nnet.client = \"allow\"\n";
        var packageRoot = await harness.WritePackageAsync(
            "sqlite-transaction-runtime",
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source,
                ["db/schema.sql"] = "CREATE TABLE IF NOT EXISTS record (id INTEGER PRIMARY KEY, value TEXT NOT NULL);\n"
            });

        var check = await harness.InvokePackageDirectoryAsync("sqlite-transaction-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-transaction-alias-escape",
            "name = \"sqlite-transaction-alias-escape\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main;\n"
                    + "fn invalid(db: DbWrite) -> bool effects { db.write } {\n"
                    + "    with db.begin() as tx { let alias: Transaction = tx; return true; }\n}\n"
            },
            "E_RESOURCE_ESCAPE",
            "src/app/main.hob");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-transaction-argument-escape",
            "name = \"sqlite-transaction-argument-escape\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main;\n"
                    + "fn accept(tx: Transaction) -> bool effects {} { return true; }\n"
                    + "fn invalid(db: DbWrite) -> bool effects { db.write } {\n"
                    + "    with db.begin() as tx { return self::app::main::accept(tx); }\n}\n"
            },
            "E_RESOURCE_ESCAPE",
            "src/app/main.hob");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-transaction-store-escape",
            "name = \"sqlite-transaction-store-escape\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main;\n"
                    + "struct Holder { transaction: Transaction }\n"
                    + "fn invalid(db: DbWrite) -> bool effects { db.write } {\n"
                    + "    with db.begin() as tx {\n"
                    + "        let holder: self::app::main::Holder = self::app::main::Holder { transaction: tx };\n"
                    + "        return true;\n"
                    + "    }\n}\n"
            },
            "E_RESOURCE_ESCAPE",
            "src/app/main.hob");

        await ExpectTransactionTypeEscapeAsync(
            "sqlite-transaction-parameter-type",
            "fn invalid(tx: Transaction) -> bool effects {} { return true; }\n");
        await ExpectTransactionTypeEscapeAsync(
            "sqlite-transaction-local-type",
            "fn invalid(db: DbWrite) -> bool effects { db.write } {\n"
                + "    with db.begin() as tx { let alias: Transaction = tx; return true; }\n}\n");
        await ExpectTransactionTypeEscapeAsync(
            "sqlite-transaction-struct-field-type",
            "struct Holder { transaction: Transaction }\n");
        await ExpectTransactionTypeEscapeAsync(
            "sqlite-transaction-union-payload-type",
            "union Holder { Stored(Transaction), Empty }\n");
        await ExpectTransactionTypeEscapeAsync(
            "sqlite-transaction-generic-wrapper-type",
            "struct Holder { transaction: Option<Transaction> }\n");
        await ExpectTransactionTypeEscapeAsync(
            "sqlite-transaction-recursive-return-type",
            "fn recursive() -> Transaction effects {} { return self::app::main::recursive(); }\n");

        await ExpectTransactionSqlDiagnosticAsync("sqlite-transaction-sql-commit", "COMMIT");
        await ExpectTransactionSqlDiagnosticAsync("sqlite-transaction-sql-rollback", "ROLLBACK");
        await ExpectTransactionSqlDiagnosticAsync("sqlite-transaction-sql-begin", "BEGIN");
        await ExpectTransactionSqlDiagnosticAsync(
            "sqlite-transaction-sql-multiple-statements",
            "INSERT INTO sample DEFAULT VALUES; DELETE FROM sample");

        var databasePath = Path.Combine(harness.TemporaryRoot, "sqlite-transactions.sqlite3");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["HOB_SQLITE_PATH"] = databasePath };
        var port = GetUnusedLoopbackPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(4) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "sqlite-transaction-run", packageRoot, environment, "--urls", address.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);
            AssertEqual(0, await ReadTransactionCountAsync(client), "A new SQLite transaction fixture should start with no rows.");

            using (var committed = await client.GetAsync("/commit"))
            {
                AssertEqual(HttpStatusCode.OK, committed.StatusCode, "The transaction commit route should return success.");
                AssertEqual("true", await committed.Content.ReadAsStringAsync(),
                    "The first successful transaction commit should return Ok(true).");
            }
            AssertEqual(1, await ReadTransactionCountAsync(client), "A committed write must persist after leaving its scope.");

            using (var rolledBack = await client.GetAsync("/rollback"))
                AssertEqual(HttpStatusCode.OK, rolledBack.StatusCode, "A write-only transaction should be allowed to leave scope without committing.");
            AssertEqual(1, await ReadTransactionCountAsync(client), "Normal scope exit without commit must roll back its write.");

            using (var failed = await client.GetAsync("/failed"))
                AssertEqual(HttpStatusCode.OK, failed.StatusCode,
                    "A failed statement should make a subsequent commit return Err(DbError.Statement).");
            AssertEqual(1, await ReadTransactionCountAsync(client),
                "A failed statement must roll back an earlier successful write in the same transaction.");

            using (var earlyReturn = await client.GetAsync("/early-return"))
                AssertEqual(HttpStatusCode.OK, earlyReturn.StatusCode, "The early-return transaction route should report its successful write.");
            AssertEqual(1, await ReadTransactionCountAsync(client), "An early function return must leave the transaction uncommitted and roll it back.");

            using (var runtimeError = await client.GetAsync("/runtime-error"))
                AssertEqual(HttpStatusCode.InternalServerError, runtimeError.StatusCode,
                    "A runtime fault inside a transaction should unwind as an HTTP 500.");
            AssertEqual(1, await ReadTransactionCountAsync(client),
                "A runtime error must roll back writes made earlier in its transaction scope.");

            using (var cancellation = new CancellationTokenSource())
            {
                var canceledRequest = client.GetAsync("/cancel", cancellation.Token);
                await cancellationServer.WaitForRequestAsync("/hold-cancel").WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
                await cancellationServer.WaitForClientDisconnectAsync("/hold-cancel").WaitAsync(TimeSpan.FromSeconds(5));
                var cancellationObserved = false;
                try
                {
                    using var canceledResponse = await canceledRequest.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                    // The disconnected request is expected to unwind its lexical transaction scope.
                    cancellationObserved = true;
                }
                catch (HttpRequestException)
                {
                    // ASP.NET may close the response stream when RequestAborted is observed.
                    cancellationObserved = true;
                }
                AssertTrue(cancellationObserved,
                    "Canceling the request must abort the awaited HTTP adapter while its transaction is open; the upstream held the request until that abort was observed.");
            }

            using (var deadline = await client.GetAsync("/deadline").WaitAsync(TimeSpan.FromSeconds(5)))
            {
                AssertEqual(HttpStatusCode.GatewayTimeout, deadline.StatusCode,
                    "An inbound deadline that cancels awaited work inside a transaction must return 504.");
                AssertEqual("{\"error\":\"request_timeout\"}", await deadline.Content.ReadAsStringAsync(),
                    "A deadline inside a transaction should use the stable request-timeout JSON body.");
                AssertTrue(deadline.Headers.Contains("X-Request-Id"),
                    "A transaction deadline response should preserve its request identifier.");
            }
            await cancellationServer.WaitForClientDisconnectAsync("/hold-deadline").WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(1, await ReadTransactionCountAsync(client),
                "A deadline must roll back the write in its open transaction before sending 504.");

            using (var recovered = await client.GetAsync("/commit-after-unwind"))
                AssertEqual(HttpStatusCode.OK, recovered.StatusCode,
                    "A subsequent transaction should commit after cancellation and deadline have unwound prior scopes.");
            AssertEqual(2, await ReadTransactionCountAsync(client),
                "Cancellation and deadline must each roll back their writes and release SQLite for a later commit.");

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
                    throw new TimeoutException("The SQLite transaction fixture host did not stop within 10 seconds.");
                }
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
        }

        static async Task<int> ReadTransactionCountAsync(HttpClient client)
        {
            using var response = await client.GetAsync("/");
            AssertEqual(HttpStatusCode.OK, response.StatusCode, "The transaction state route should return 200.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetInt32();
        }

        async Task ExpectTransactionTypeEscapeAsync(string caseName, string declaration)
        {
            var sourceText = "module app::main;\n" + declaration;
            await ExpectPackageJsonDiagnosticAsync(
                harness,
                caseName,
                "name = \"" + caseName + "\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = sourceText },
                "E_RESOURCE_ESCAPE",
                "src/app/main.hob");
        }

        async Task ExpectTransactionSqlDiagnosticAsync(string caseName, string sql)
        {
            var sourceText = "module app::main;\n"
                + "struct Parameters {}\n"
                + "fn invalid(db: DbWrite) -> bool effects { db.write } {\n"
                + "    with db.begin() as tx {\n"
                + "        let result: Result<i32, DbError> = tx.execute("
                + JsonSerializer.Serialize(sql)
                + ", self::app::main::Parameters {});\n"
                + "        return true;\n"
                + "    }\n"
                + "}\n";
            await ExpectPackageJsonDiagnosticAsync(
                harness,
                caseName,
                "name = \"" + caseName + "\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = sourceText },
                "E_DB_TRANSACTION_STATEMENT",
                "src/app/main.hob");
        }
    }

    private static async Task TestSqliteRowDecoding(Harness harness)
    {
        var queryCases = new[]
        {
            new SqliteRowCase("missing-column", "IdNameRow", "SELECT id FROM records WHERE id = 1", "\"unexpected row\"", HttpStatusCode.UnprocessableEntity),
            new SqliteRowCase("extra-column", "IdRow", "SELECT id, name FROM records WHERE id = 1", "\"unexpected row\"", HttpStatusCode.UnprocessableEntity),
            new SqliteRowCase("duplicate-column", "IdNameRow", "SELECT id AS id, id AS id FROM records WHERE id = 1", "\"unexpected row\"", HttpStatusCode.UnprocessableEntity),
            new SqliteRowCase("wrong-type", "IdRow", "SELECT 'not-an-integer' AS id FROM records WHERE id = 1", "\"unexpected row\"", HttpStatusCode.UnprocessableEntity),
            new SqliteRowCase("required-null", "IdNameRow", "SELECT id, NULL AS name FROM records WHERE id = 1", "\"unexpected row\"", HttpStatusCode.UnprocessableEntity),
            new SqliteRowCase("optional-null", "OptionalRow", "SELECT id, note FROM records WHERE id = 1", "self::app::main::display_optional(row.note)", HttpStatusCode.OK, "NULL"),
            new SqliteRowCase("multiple-rows", "IdNameRow", "SELECT id, name FROM records", "\"unexpected row\"", HttpStatusCode.UnprocessableEntity),
            new SqliteRowCase("statement-failure", "IdNameRow", "SELECT id, name FROM absent_records", "\"unexpected row\"", HttpStatusCode.ServiceUnavailable),
            new SqliteRowCase("verify-read-unchanged", "IdNameRow", "SELECT id, name FROM records WHERE id = 3", "\"unexpected row\"", HttpStatusCode.NotFound),
            new SqliteRowCase("no-row", "IdNameRow", "SELECT id, name FROM records WHERE id = 999", "\"unexpected row\"", HttpStatusCode.NotFound)
        };

        var handlers = string.Join("\n", queryCases.Select(SqliteQueryHandler));
        var routes = string.Join("\n", queryCases.Select(SqliteQueryRoute));
        var source = $$"""
            module app::main;
            struct NoParameters {}
            struct IdRow { id: i32 }
            struct IdNameRow { id: i32, name: Text }
            struct OptionalRow { id: i32, note: Option<Text> }
            union DbCheckReply { Found(Text), Missing, StatementFailure, RowShapeFailure }
            union ReadyReply { Ready }

            fn display_optional(value: Option<Text>) -> Text effects {} {
                return match value { Some(text) => text, None => "NULL" };
            }

            fn ready() -> self::app::main::ReadyReply effects {} {
                return self::app::main::ReadyReply.Ready;
            }

            {{handlers}}

            route GET "/" {
                handler: self::app::main::ready;
                response Ready: 200;
            }

            {{routes}}
            """;
        var schema = "CREATE TABLE IF NOT EXISTS records (id INTEGER PRIMARY KEY, name TEXT NOT NULL, note TEXT NULL);\n"
            + "INSERT INTO records (id, name, note) VALUES (1, 'Ada', NULL) ON CONFLICT(id) DO NOTHING;\n"
            + "INSERT INTO records (id, name, note) VALUES (2, 'Grace', 'memo') ON CONFLICT(id) DO NOTHING;\n";
        var packageRoot = await harness.WritePackageAsync(
            "sqlite-row-decoding",
            "name = \"sqlite-row-decoding\"\n"
                + "version = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "sqlite_path = \"data/rows.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source,
                ["db/schema.sql"] = schema
            });

        var check = await harness.InvokePackageDirectoryAsync("sqlite-row-decoding-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var databasePath = Path.Combine(harness.TemporaryRoot, "sqlite-row-decoding.sqlite3");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["HOB_SQLITE_PATH"] = databasePath };
        var port = GetUnusedLoopbackPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(4) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "sqlite-row-decoding-run", packageRoot, environment, "--urls", address.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);
            await ExpectPackageJsonDiagnosticAsync(
                harness,
                "sqlite-read-mutation-rejected",
                "name = \"sqlite-read-mutation-rejected\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                    + "sqlite_path = \"data/rows.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
                    + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = SqliteReadQuerySource("INSERT INTO records (id, name, note) VALUES (3, 'Injected', NULL) RETURNING id"),
                    ["db/schema.sql"] = schema
                },
                "E_DB_READ_STATEMENT",
                "src/app/main.hob");

            foreach (var testCase in queryCases)
            {
                using var response = await client.GetAsync("/" + testCase.Name);
                AssertEqual(testCase.ExpectedStatus, response.StatusCode,
                    $"SQLite row case '{testCase.Name}' returned an unexpected status.");
                if (testCase.ExpectedBody is not null)
                {
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    AssertEqual(testCase.ExpectedBody, body.RootElement.GetString(),
                        $"SQLite row case '{testCase.Name}' returned an unexpected decoded value.");
                }
            }

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
                    throw new TimeoutException("The SQLite row fixture host did not stop within 10 seconds.");
                }
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
        }
    }

    private static string SqliteQueryHandler(SqliteRowCase testCase) => $$"""
        fn check_{{testCase.Name.Replace("-", "_", StringComparison.Ordinal)}}(db: DbRead) -> self::app::main::DbCheckReply effects { db.read } {
            let loaded: Result<Option<self::app::main::{{testCase.RowType}}>, DbError> = db.query_one(
                "{{testCase.Sql}}",
                self::app::main::NoParameters {}
            );
            return match loaded {
                Ok(value) => match value {
                    Some(row) => self::app::main::DbCheckReply.Found({{testCase.FoundExpression}}),
                    None => self::app::main::DbCheckReply.Missing
                },
                Err(error) => match error {
                    DbError.Statement => self::app::main::DbCheckReply.StatementFailure,
                    DbError.RowShape => self::app::main::DbCheckReply.RowShapeFailure
                }
            };
        }
        """;

    private static string SqliteQueryRoute(SqliteRowCase testCase) => $$"""
        route GET "/{{testCase.Name}}" {
            handler: self::app::main::check_{{testCase.Name.Replace("-", "_", StringComparison.Ordinal)}};
            response Found: 200 json Text;
            response Missing: 404;
            response StatementFailure: 503;
            response RowShapeFailure: 422;
        }
        """;

    private static string SqliteReadQuerySource(string sql) => $$"""
        module app::main;
        struct Parameters {}
        struct Row { id: i32 }
        union Reply { Ready }

        fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {
            let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one(
                "{{sql}}",
                self::app::main::Parameters {}
            );
            return self::app::main::Reply.Ready;
        }

        route GET "/" {
            handler: self::app::main::ready;
            response Ready: 200;
        }
        """;

    private static async Task TestSqlitePackageContract(Harness harness)
    {
        const string sqliteHandler = "module app::main;\n"
            + "union Reply { Ready }\n"
            + "fn ready(db: DbRead) -> self::app::main::Reply effects {} { return self::app::main::Reply.Ready; }\n"
            + "route GET \"/\" { handler: self::app::main::ready; response Ready: 200; }\n";
        var sqliteSourceFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = sqliteHandler,
            ["db/schema.sql"] = "CREATE TABLE IF NOT EXISTS sample (id INTEGER PRIMARY KEY);\n"
        };
        var sqliteManifest = "name = \"sqlite-contract\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
            + "sqlite_path = \"data/sample.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
            + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n";

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-capability-without-paths",
            "name = \"sqlite-capability-without-paths\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\n",
            sqliteSourceFiles,
            "E_CAPABILITY_MISSING",
            "hob.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-path-escape",
            "name = \"sqlite-path-escape\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "sqlite_path = \"../outside.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n",
            sqliteSourceFiles,
            "E_MANIFEST",
            "hob.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-schema-missing",
            "name = \"sqlite-schema-missing\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "sqlite_path = \"data/sample.sqlite3\"\nsqlite_schema = \"db/missing.sql\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = sqliteHandler },
            "E_MANIFEST",
            "hob.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-read-grant-missing",
            "name = \"sqlite-read-grant-missing\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "sqlite_path = \"data/sample.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\ndb.write = \"allow\"\n",
            sqliteSourceFiles,
            "E_CAPABILITY_MISSING",
            "src/app/main.hob");

        const string dynamicSqlSource = "module app::main;\n"
            + "struct Parameters {}\nstruct Row { id: i32 }\nunion Reply { Ready }\n"
            + "fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {\n"
            + "    let sql: Text = \"SELECT id FROM sample\";\n"
            + "    let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one(sql, self::app::main::Parameters {});\n"
            + "    return self::app::main::Reply.Ready;\n}\n"
            + "route GET \"/\" { handler: self::app::main::ready; response Ready: 200; }\n";
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-dynamic-sql-rejected",
            sqliteManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = dynamicSqlSource,
                ["db/schema.sql"] = sqliteSourceFiles["db/schema.sql"]
            },
            "E_DB_SQL_LITERAL",
            "src/app/main.hob");

        foreach (var (caseName, sql) in new[]
        {
            ("sqlite-multi-statement-read-rejected", "SELECT id FROM sample; DROP TABLE sample"),
            ("sqlite-attach-read-rejected", "ATTACH DATABASE 'outside.sqlite3' AS outside")
        })
        {
            await ExpectPackageJsonDiagnosticAsync(
                harness,
                caseName,
                sqliteManifest,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.hob"] = SqliteReadQuerySource(sql),
                    ["db/schema.sql"] = sqliteSourceFiles["db/schema.sql"]
                },
                "E_DB_READ_STATEMENT",
                "src/app/main.hob");
        }

        const string unsupportedCodecSource = "module app::main;\n"
            + "struct Parameters {}\nstruct Row { path: FilePath }\nunion Reply { Ready }\n"
            + "fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {\n"
            + "    let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one(\"SELECT 'x' AS path\", self::app::main::Parameters {});\n"
            + "    return self::app::main::Reply.Ready;\n}\n"
            + "route GET \"/\" { handler: self::app::main::ready; response Ready: 200; }\n";
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-unsupported-codec-rejected",
            sqliteManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = unsupportedCodecSource,
                ["db/schema.sql"] = sqliteSourceFiles["db/schema.sql"]
            },
            "E_DB_CODEC_UNSUPPORTED",
            "src/app/main.hob");

        const string genericRowSource = "module app::main;\n"
            + "struct Parameters {}\nstruct Row<T> { id: T }\nunion Reply { Ready }\n"
            + "fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {\n"
            + "    let loaded: Result<Option<self::app::main::Row<i32>>, DbError> = db.query_one(\"SELECT 1 AS id\", self::app::main::Parameters {});\n"
            + "    return self::app::main::Reply.Ready;\n}\n"
            + "route GET \"/\" { handler: self::app::main::ready; response Ready: 200; }\n";
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-generic-row-rejected",
            sqliteManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = genericRowSource,
                ["db/schema.sql"] = sqliteSourceFiles["db/schema.sql"]
            },
            "E_DB_CODEC_UNSUPPORTED",
            "src/app/main.hob");

        const string nonDatabaseWebSource = "module app::main;\n"
            + "union Reply { Ready }\n"
            + "fn ready() -> self::app::main::Reply effects {} { return self::app::main::Reply.Ready; }\n"
            + "route GET \"/\" { handler: self::app::main::ready; response Ready: 200; }\n";
        var nonDatabaseRoot = await harness.WritePackageAsync(
            "web-without-sqlite-dependency",
            "name = \"web-without-sqlite-dependency\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = nonDatabaseWebSource });
        var noDatabaseBuild = await harness.InvokePackageDirectoryAsync(
            "web-without-sqlite-dependency-build", nonDatabaseRoot, "build");
        var noDatabaseArtifact = AssertBuiltWebApplication(noDatabaseBuild, nonDatabaseRoot);
        var noDatabaseDeps = await File.ReadAllTextAsync(Path.ChangeExtension(noDatabaseArtifact, ".deps.json"));
        AssertTrue(!noDatabaseDeps.Contains("Microsoft.Data.Sqlite", StringComparison.Ordinal),
            "Web packages without SQLite configuration must not reference Microsoft.Data.Sqlite.");
    }

    private static async Task TestSqliteLibraryBuild(Harness harness)
    {
        const string source = "module app::repository;\n"
            + "pub struct LookupParameters { id: i32 }\n"
            + "pub struct Row { id: i32 }\n"
            + "pub struct InsertParameters { id: i32, name: Text }\n"
            + "pub fn find(db: DbRead, id: i32) -> Result<Option<self::app::repository::Row>, DbError> effects { db.read } {\n"
            + "    let loaded: Result<Option<self::app::repository::Row>, DbError> = db.query_one(\n"
            + "        \"SELECT id FROM record WHERE id = $id\",\n"
            + "        self::app::repository::LookupParameters { id: id }\n"
            + "    );\n"
            + "    return loaded;\n"
            + "}\n"
            + "pub fn insert(db: DbWrite, id: i32, name: Text) -> Result<i32, DbError> effects { db.write } {\n"
            + "    let written: Result<i32, DbError> = db.execute(\n"
            + "        \"INSERT INTO record (id, name) VALUES ($id, $name)\",\n"
            + "        self::app::repository::InsertParameters { id: id, name: name }\n"
            + "    );\n"
            + "    return written;\n"
            + "}\n";
        var packageRoot = await harness.WritePackageAsync(
            "sqlite-library-adapter",
            "name = \"sqlite-library-adapter\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/repository.hob"] = source });

        var check = await harness.InvokePackageDirectoryAsync("sqlite-library-adapter-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var build = await harness.InvokePackageDirectoryAsync("sqlite-library-adapter-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        const string prefix = "Built library: ";
        AssertTrue(build.StandardOutput.StartsWith(prefix, StringComparison.Ordinal), Describe(build));
        var artifact = build.StandardOutput[prefix.Length..].Trim();
        AssertTrue(Path.IsPathFullyQualified(artifact) && File.Exists(artifact),
            $"Expected a managed database library at {artifact}. {Describe(build)}");
        var dependencyManifestPath = Path.ChangeExtension(artifact, ".deps.json");
        AssertTrue(File.Exists(dependencyManifestPath),
            "A built SQLite library should include its generated dependency manifest.");
        var dependencyManifest = await File.ReadAllTextAsync(dependencyManifestPath);
        AssertTrue(dependencyManifest.Contains("Microsoft.Data.Sqlite/10.0.12", StringComparison.Ordinal),
            "A library that uses database operations must emit the pinned SQLite provider reference without web manifest configuration.");
    }

    private sealed record SqliteRowCase(
        string Name,
        string RowType,
        string Sql,
        string FoundExpression,
        HttpStatusCode ExpectedStatus,
        string? ExpectedBody = null);

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    private sealed class NonSeekableMemoryStream(byte[] content) : MemoryStream(content, writable: false)
    {
        public override bool CanSeek => false;
    }

    private sealed class RawHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentQueue<RawHttpRequest> _requests = new();
        private readonly ConcurrentBag<Task> _connections = [];
        private readonly ConcurrentDictionary<string, TaskCompletionSource<RawHttpRequest>> _requestSignals = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _holdReleases = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _clientDisconnectSignals = new(StringComparer.Ordinal);
        private readonly Task _acceptLoop;

        public RawHttpServer()
        {
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Origin = $"http://127.0.0.1:{port}";
            _acceptLoop = AcceptLoopAsync();
        }

        public string Origin { get; }
        public IReadOnlyCollection<RawHttpRequest> Requests => _requests.ToArray();

        public Task<RawHttpRequest> WaitForRequestAsync(string target) =>
            _requestSignals.GetOrAdd(target, static _ => NewCompletion<RawHttpRequest>()).Task;

        public Task WaitForClientDisconnectAsync(string target) =>
            _clientDisconnectSignals.GetOrAdd(target, static _ => NewCompletion<bool>()).Task;

        public void ReleaseHeldRequest(string target) =>
            _holdReleases.GetOrAdd(target, static _ => NewCompletion<bool>()).TrySetResult(true);

        public async ValueTask DisposeAsync()
        {
            _stopping.Cancel();
            _listener.Stop();
            foreach (var release in _holdReleases.Values)
                release.TrySetResult(true);
            try
            {
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }
            try
            {
                await Task.WhenAll(_connections.ToArray()).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or IOException or ObjectDisposedException or TimeoutException)
            {
            }
            _stopping.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stopping.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                    _connections.Add(HandleConnectionAsync(client));
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
            }
            catch (SocketException) when (_stopping.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_stopping.IsCancellationRequested)
            {
            }
        }

        private async Task HandleConnectionAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    while (!_stopping.IsCancellationRequested)
                    {
                        var header = await ReadHeaderAsync(stream, _stopping.Token);
                        if (header is null)
                            return;
                        var lines = header.Split("\r\n", StringSplitOptions.None);
                        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (requestLine.Length < 2)
                            return;
                        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var line in lines.Skip(1))
                        {
                            var separator = line.IndexOf(':');
                            if (separator > 0)
                                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                        }
                        var remotePort = (client.Client.RemoteEndPoint as IPEndPoint)?.Port ?? 0;
                        var request = new RawHttpRequest(requestLine[1], headers, remotePort);
                        _requests.Enqueue(request);
                        _requestSignals.GetOrAdd(request.Target, static _ => NewCompletion<RawHttpRequest>())
                            .TrySetResult(request);

                        if (request.Target is "/hold-cancel" or "/hold-cancel-managed" or "/hold-cancel-native" or "/hold-deadline")
                        {
                            await ObserveClientDisconnectAsync(request.Target, stream, _stopping.Token);
                            return;
                        }

                        if (request.Target == "/hold-timeout")
                            await _holdReleases.GetOrAdd(request.Target, static _ => NewCompletion<bool>()).Task
                                .WaitAsync(_stopping.Token);

                        if (request.Target == "/reset")
                        {
                            client.Client.LingerState = new LingerOption(enable: true, seconds: 0);
                            return;
                        }

                        var response = CreateResponse(request.Target);
                        var reason = response.Status switch
                        {
                            200 => "OK",
                            302 => "Found",
                            404 => "Not Found",
                            _ => "Response"
                        };
                        var responseHeaders = new StringBuilder()
                            .Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(reason).Append("\r\n")
                            .Append("Content-Length: ").Append(response.Body.Length).Append("\r\n")
                            .Append("Content-Type: text/plain; charset=utf-8\r\n")
                            .Append("Connection: keep-alive\r\n");
                        foreach (var (name, value) in response.Headers)
                            responseHeaders.Append(name).Append(": ").Append(value).Append("\r\n");
                        responseHeaders.Append("\r\n");
                        var headBytes = Encoding.ASCII.GetBytes(responseHeaders.ToString());
                        await stream.WriteAsync(headBytes, _stopping.Token);
                        await stream.WriteAsync(response.Body, _stopping.Token);
                        await stream.FlushAsync(_stopping.Token);
                    }
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
                {
                }
            }
        }

        private async Task ObserveClientDisconnectAsync(
            string target,
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            try
            {
                var buffer = new byte[1];
                while (await stream.ReadAsync(buffer.AsMemory(), cancellationToken) != 0)
                {
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
            {
            }

            if (!cancellationToken.IsCancellationRequested)
                _clientDisconnectSignals.GetOrAdd(target, static _ => NewCompletion<bool>()).TrySetResult(true);
        }

        private static async Task<string?> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>(1024);
            var one = new byte[1];
            while (bytes.Count < 64 * 1024)
            {
                var read = await stream.ReadAsync(one.AsMemory(), cancellationToken);
                if (read == 0)
                    return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
                bytes.Add(one[0]);
                var count = bytes.Count;
                if (count >= 4 && bytes[count - 4] == (byte)'\r' && bytes[count - 3] == (byte)'\n'
                    && bytes[count - 2] == (byte)'\r' && bytes[count - 1] == (byte)'\n')
                    return Encoding.ASCII.GetString(bytes.ToArray());
            }
            throw new IOException("The test HTTP request headers exceeded the parser limit.");
        }

        private static RawHttpResponse CreateResponse(string target) => target switch
        {
            "/ok?from=runtime" => TextResponse(200, "loopback λ"),
            "/aot?source=typed-command" => TextResponse(200, "native loopback λ"),
            "/not-found" => TextResponse(404, "not found"),
            "/server-error" => TextResponse(503, "server error"),
            "/redirect" => new RawHttpResponse(302, Encoding.UTF8.GetBytes("redirect body"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Location"] = "/followed" }),
            "/followed" => TextResponse(200, "followed"),
            "/cookie" => new RawHttpResponse(200, Encoding.UTF8.GetBytes("cookie set"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Set-Cookie"] = "session=private; Path=/" }),
            "/after-cookie" => TextResponse(200, "after cookie"),
            "/exact-limit" => new RawHttpResponse(200, Enumerable.Repeat((byte)'x', 1_048_576).ToArray(), EmptyHeaders),
            "/over-limit" => new RawHttpResponse(200, Enumerable.Repeat((byte)'x', 1_048_577).ToArray(), EmptyHeaders),
            "/invalid-text" => new RawHttpResponse(200, [0xC3, 0x28], EmptyHeaders),
            _ => TextResponse(404, "unexpected path")
        };

        private static RawHttpResponse TextResponse(int status, string body) =>
            new(status, Encoding.UTF8.GetBytes(body), EmptyHeaders);

        private static IReadOnlyDictionary<string, string> EmptyHeaders { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static TaskCompletionSource<T> NewCompletion<T>() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record RawHttpRequest(string Target, IReadOnlyDictionary<string, string> Headers, int RemotePort);
    private sealed record RawHttpResponse(int Status, byte[] Body, IReadOnlyDictionary<string, string> Headers);

    private static int GetUnusedLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForWebServerAsync(
        Process process,
        HttpClient client,
        Task<string> stdoutTask,
        Task<string> stderrTask)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (process.HasExited)
            {
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                throw new InvalidOperationException(
                    $"The maintained web host exited before becoming ready (exit {process.ExitCode}). stdout=<{stdout}> stderr=<{stderr}>");
            }

            try
            {
                using var response = await client.GetAsync("/");
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The maintained web host did not become ready within 30 seconds.");
    }

    private static void CopyMaintainedPackageInputs(string sourceRoot, string destinationRoot, bool includeSqliteSchema = false)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var name in new[] { "hob.toml", "hob.lock" })
        {
            var sourcePath = Path.Combine(sourceRoot, name);
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, Path.Combine(destinationRoot, name));
        }

        CopyTree(Path.Combine(sourceRoot, "src"), Path.Combine(destinationRoot, "src"));
        if (includeSqliteSchema && Directory.Exists(Path.Combine(sourceRoot, "db")))
            CopyTree(Path.Combine(sourceRoot, "db"), Path.Combine(destinationRoot, "db"));

        static void CopyTree(string sourceDirectory, string destinationDirectory)
        {
            if (!Directory.Exists(sourceDirectory))
                return;

            Directory.CreateDirectory(destinationDirectory);
            foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory))
                File.Copy(sourcePath, Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)));
            foreach (var childDirectory in Directory.EnumerateDirectories(sourceDirectory))
                CopyTree(childDirectory, Path.Combine(destinationDirectory, Path.GetFileName(childDirectory)));
        }
    }

    private static async Task AssertLoopbackPortReleasedAsync(int port)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            var probe = new TcpListener(IPAddress.Loopback, port);
            try
            {
                probe.Start();
                probe.Stop();
                return;
            }
            catch (SocketException)
            {
                probe.Stop();
                await Task.Delay(100);
            }
        }

        throw new InvalidOperationException($"The web host kept loopback port {port} open 20 seconds after process termination.");
    }

    private static string AssertBuiltWebApplication(ProcessResult result, string packageRoot)
    {
        AssertEqual(0, result.ExitCode, Describe(result));
        const string prefix = "Built executable: ";
        AssertTrue(result.StandardOutput.StartsWith(prefix, StringComparison.Ordinal), Describe(result));
        AssertTrue(result.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal), Describe(result));
        var artifactPath = result.StandardOutput[prefix.Length..^Environment.NewLine.Length];
        AssertEqual(prefix + artifactPath + Environment.NewLine, result.StandardOutput,
            "A web package build should print only its executable artifact line.");
        AssertTrue(Path.IsPathFullyQualified(artifactPath) && File.Exists(artifactPath),
            $"Expected a durable managed web executable at {artifactPath}.");
        AssertEqual(".dll", Path.GetExtension(artifactPath), "Managed web builds should retain a DLL artifact.");
        var outputDirectory = Path.GetFullPath(Path.GetDirectoryName(artifactPath)!);
        var expectedOutputRoot = Path.GetFullPath(Path.Combine(packageRoot, "out"));
        AssertEqual(expectedOutputRoot, Path.GetDirectoryName(outputDirectory),
            "The web DLL should be written below the package's durable out directory.");
        AssertTrue(File.Exists(Path.ChangeExtension(artifactPath, ".deps.json")),
            "A managed web app should include its dependency manifest.");
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        return artifactPath;
    }

    private static void AssertBuiltSqliteRuntimeAssets(string artifactPath, string dependencyManifest)
    {
        foreach (var dependency in new[]
        {
            "Microsoft.Data.Sqlite/10.0.12",
            "SQLitePCLRaw.bundle_e_sqlite3/",
            "SQLitePCLRaw.core/",
            "SQLitePCLRaw.provider.e_sqlite3/"
        })
        {
            AssertTrue(dependencyManifest.Contains(dependency, StringComparison.Ordinal),
                $"A SQLite-enabled web artifact must include managed runtime dependency {dependency}.");
        }

        var (runtimeIdentifier, assetFileName) = CurrentSqliteRuntimeAsset();
        var outputDirectory = Path.GetDirectoryName(artifactPath)!;
        var runtimeAssetPath = Path.Combine(outputDirectory, "runtimes", runtimeIdentifier, "native", assetFileName);
        var rootAssetPath = Path.Combine(outputDirectory, assetFileName);
        AssertTrue(File.Exists(runtimeAssetPath) || File.Exists(rootAssetPath),
            $"A SQLite-enabled web artifact must include the current platform native asset at either "
                + $"{runtimeAssetPath} or {rootAssetPath}.");
    }

    private static (string RuntimeIdentifier, string AssetFileName) CurrentSqliteRuntimeAsset()
    {
        var runtimePrefix = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "osx"
                    : throw new InvalidOperationException("SQLite runtime asset assertions require Windows, Linux, or macOS.");
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => throw new InvalidOperationException(
                $"SQLite runtime asset assertions do not support architecture {RuntimeInformation.ProcessArchitecture}.")
        };
        var assetFileName = runtimePrefix switch
        {
            "win" => "e_sqlite3.dll",
            "linux" => "libe_sqlite3.so",
            "osx" => "libe_sqlite3.dylib",
            _ => throw new InvalidOperationException($"Unsupported SQLite runtime platform {runtimePrefix}.")
        };

        return ($"{runtimePrefix}-{architecture}", assetFileName);
    }

    private static async Task TestScanCliExample(Harness harness)
    {
        var packageRoot = Path.Combine(harness.RepositoryRoot, "examples", "scan-cli");
        var manifest = await File.ReadAllTextAsync(Path.Combine(packageRoot, "hob.toml"));
        var normalizedManifest = manifest.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        AssertTrue(normalizedManifest.Contains("[capabilities]\nfs.read = \"allow\"\n", StringComparison.Ordinal),
            "The maintained scanner must explicitly grant fs.read to its CLI application.");
        AssertTrue(normalizedManifest.Contains("[dependencies]\nvalidation = \"../text-validation\"", StringComparison.Ordinal),
            "The scanner must use text-validation through its local dependency alias.");
        var source = await File.ReadAllTextAsync(Path.Combine(packageRoot, "src", "app", "scan.hob"));
        AssertTrue(source.Contains("pub async fn run", StringComparison.Ordinal)
            && source.Contains("await fs.read_text_async(args.input)", StringComparison.Ordinal),
            "The maintained scanner must use the cancellation-aware async FsRead adapter.");
        AssertTrue(File.Exists(Path.Combine(packageRoot, "hob.lock")),
            "The maintained scanner path dependency should have a canonical lockfile.");

        var check = await harness.InvokePackageDirectoryAsync("scan-cli-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var build = await harness.InvokePackageDirectoryAsync("scan-cli-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        AssertTrue(build.StandardOutput.StartsWith("Built executable: ", StringComparison.Ordinal), Describe(build));
        var executablePath = build.StandardOutput["Built executable: ".Length..].Trim();
        AssertTrue(Path.IsPathFullyQualified(executablePath) && File.Exists(executablePath),
            $"Expected the managed scan CLI executable at {executablePath}.");
        var schemaPath = Path.Combine(Path.GetDirectoryName(executablePath)!, "command-schema.json");
        AssertTrue(File.Exists(schemaPath), $"Expected scan CLI command schema beside its executable: {schemaPath}");
        using (var schema = JsonDocument.Parse(await File.ReadAllBytesAsync(schemaPath)))
        {
            var root = schema.RootElement;
            AssertEqual(5, root.GetProperty("schema_version").GetInt32(), "Scan CLI schema version mismatch.");
            var command = root.GetProperty("commands")[0];
            AssertEqual("scan", command.GetProperty("name").GetString(), "Scan CLI command schema name mismatch.");
            var capabilities = command.GetProperty("capabilities");
            AssertEqual(1, capabilities.GetArrayLength(), "The scanner schema must report one required capability.");
            AssertEqual("fs.read", capabilities[0].GetString(), "The scanner schema must declare its filesystem read requirement.");
            AssertEqual("FilePath", command.GetProperty("arguments")[0].GetProperty("type").GetString(),
                "The scan input must remain an opaque FilePath in the command schema.");
            AssertEqual("bool", command.GetProperty("flags")[0].GetProperty("type").GetString(),
                "The normalize flag must be represented as a bool input.");
        }

        var temporaryDirectory = Path.Combine(harness.TemporaryRoot, $"scan-cli-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var textPath = Path.Combine(temporaryDirectory, "input.txt");
        var emptyPath = Path.Combine(temporaryDirectory, "empty.txt");
        var invalidUtf8Path = Path.Combine(temporaryDirectory, "invalid-utf8.txt");
        await File.WriteAllTextAsync(textPath, "  scan λ 😀  ", new UTF8Encoding(false, true));
        await File.WriteAllTextAsync(emptyPath, string.Empty, new UTF8Encoding(false, true));
        await File.WriteAllBytesAsync(invalidUtf8Path, [0xC3, 0x28]);

        var raw = await harness.InvokePackageDirectoryAsync(
            "scan-cli-raw", packageRoot, "run", "--", "scan", textPath);
        AssertRunOutput("  scan λ 😀  " + Environment.NewLine, raw);
        var normalized = await harness.InvokePackageDirectoryAsync(
            "scan-cli-normalized", packageRoot, "run", "--", "scan", textPath, "--normalize");
        AssertRunOutput("scan λ 😀" + Environment.NewLine, normalized);

        var topHelp = await harness.InvokePackageDirectoryAsync(
            "scan-cli-top-help", packageRoot, "run", "--", "--help");
        AssertEqual(0, topHelp.ExitCode, Describe(topHelp));
        AssertTrue(topHelp.StandardOutput.Contains("scan", StringComparison.Ordinal)
            && topHelp.StandardOutput.Contains("Read a UTF-8 text file", StringComparison.Ordinal), Describe(topHelp));
        var commandHelp = await harness.InvokePackageDirectoryAsync(
            "scan-cli-command-help", packageRoot, "run", "--", "scan", "--help");
        AssertEqual(0, commandHelp.ExitCode, Describe(commandHelp));
        AssertTrue(commandHelp.StandardOutput.Contains("input", StringComparison.Ordinal)
            && commandHelp.StandardOutput.Contains("--normalize", StringComparison.Ordinal), Describe(commandHelp));

        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync(
            "scan-cli-unknown-command", packageRoot, "run", "--", "unknown"), "CLI_UNKNOWN_COMMAND");
        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync(
            "scan-cli-missing-argument", packageRoot, "run", "--", "scan"), "CLI_MISSING_ARGUMENT");
        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync(
            "scan-cli-empty-filepath", packageRoot, "run", "--", "scan", string.Empty), "CLI_INVALID_VALUE");
        AssertCliParseFailure(await harness.InvokePackageDirectoryAsync(
            "scan-cli-duplicate-flag", packageRoot, "run", "--", "scan", textPath, "--normalize", "--normalize"),
            "CLI_DUPLICATE_OPTION");

        async Task AssertTypedFailure(string caseName, string path, string expectedMessage)
        {
            await AssertTypedFailureWithMessages(caseName, path, expectedMessage);
        }

        async Task AssertTypedFailureWithMessages(string caseName, string path, params string[] expectedMessages)
        {
            var result = await harness.InvokePackageDirectoryAsync(caseName, packageRoot, "run", "--", "scan", path, "--normalize");
            AssertEqual(3, result.ExitCode, Describe(result));
            AssertEqual(string.Empty, result.StandardOutput, Describe(result));
            AssertTrue(expectedMessages.Any(message => result.StandardError == message + Environment.NewLine),
                $"Expected one typed filesystem/domain error from [{string.Join(", ", expectedMessages)}]. {Describe(result)}");
        }

        await AssertTypedFailure("scan-cli-missing-file", Path.Combine(temporaryDirectory, "missing.txt"), "File not found");
        await AssertTypedFailure("scan-cli-empty-normalized-content", emptyPath, "File contains no text to normalize");
        await AssertTypedFailure("scan-cli-invalid-utf8", invalidUtf8Path, "File is not valid UTF-8");
        await AssertTypedFailureWithMessages(
            "scan-cli-directory-path",
            temporaryDirectory,
            "Permission denied",
            "File read failed",
            "Invalid file path");

        var missingGrantGraphRoot = Path.Combine(harness.TemporaryRoot, $"scan-cli-missing-grant-{Guid.NewGuid():N}");
        var missingGrantRoot = Path.Combine(missingGrantGraphRoot, "scan-cli");
        var missingGrantDependency = Path.Combine(missingGrantGraphRoot, "text-validation");
        CopyMaintainedPackageInputs(packageRoot, missingGrantRoot);
        CopyMaintainedPackageInputs(Path.Combine(harness.RepositoryRoot, "examples", "text-validation"), missingGrantDependency);
        var missingGrantManifestPath = Path.Combine(missingGrantRoot, "hob.toml");
        var originalManifest = await File.ReadAllTextAsync(missingGrantManifestPath);
        var noGrantManifest = originalManifest.Replace("[capabilities]\nfs.read = \"allow\"\n", "[capabilities]\n", StringComparison.Ordinal);
        AssertTrue(noGrantManifest != originalManifest,
            "The scanner capability fixture must contain the expected root fs.read grant.");
        await File.WriteAllTextAsync(missingGrantManifestPath, noGrantManifest, new UTF8Encoding(false));
        var staleGrantCheck = await harness.InvokePackageDirectoryAsync(
            "scan-cli-missing-grant-stale-check", missingGrantRoot, "check", "--json");
        AssertEqual(1, staleGrantCheck.ExitCode, Describe(staleGrantCheck));
        var staleGrantDiagnostics = ParseDiagnosticSnapshots(staleGrantCheck.StandardOutput);
        AssertEqual(1, staleGrantDiagnostics.Length, Describe(staleGrantCheck));
        AssertEqual("E_LOCK", staleGrantDiagnostics[0].Code, Describe(staleGrantCheck));

        var noGrantLock = await harness.InvokePackageDirectoryAsync(
            "scan-cli-missing-grant-relock", missingGrantRoot, "lock");
        AssertEqual(0, noGrantLock.ExitCode, Describe(noGrantLock));
        var missingGrantCheck = await harness.InvokePackageDirectoryAsync(
            "scan-cli-missing-grant-check", missingGrantRoot, "check", "--json");
        AssertEqual(1, missingGrantCheck.ExitCode, Describe(missingGrantCheck));
        var missingGrantDiagnostics = ParseDiagnosticSnapshots(missingGrantCheck.StandardOutput);
        AssertEqual(1, missingGrantDiagnostics.Length, Describe(missingGrantCheck));
        AssertEqual("E_CAPABILITY_MISSING", missingGrantDiagnostics[0].Code, Describe(missingGrantCheck));
        AssertEqual("Command handler requires the root package's fs.read capability grant",
            missingGrantDiagnostics[0].Message, Describe(missingGrantCheck));
        var commandSource = await File.ReadAllTextAsync(Path.Combine(missingGrantRoot, "src", "app", "main.hob"));
        AssertRangeAtToken(commandSource, missingGrantDiagnostics[0], "self", 1);
        var missingGrantBuild = await harness.InvokePackageDirectoryAsync(
            "scan-cli-missing-grant-build", missingGrantRoot, "build");
        AssertTrue(missingGrantBuild.ExitCode != 0, Describe(missingGrantBuild));
        AssertTrue((missingGrantBuild.StandardOutput + missingGrantBuild.StandardError)
                .Contains("E_CAPABILITY_MISSING", StringComparison.Ordinal)
            && (missingGrantBuild.StandardOutput + missingGrantBuild.StandardError)
                .Contains("Command handler requires the root package's fs.read capability grant", StringComparison.Ordinal),
            $"A missing root fs.read grant must reject the build with its stable diagnostic. {Describe(missingGrantBuild)}");
        AssertTrue(!missingGrantBuild.StandardOutput.Contains("Built executable: ", StringComparison.Ordinal),
            "A missing root fs.read grant must not print a success artifact prefix.");
        var missingGrantOutput = Path.Combine(missingGrantRoot, "out");
        AssertTrue(!Directory.Exists(missingGrantOutput)
            || !Directory.EnumerateFileSystemEntries(missingGrantOutput, "*", SearchOption.AllDirectories).Any(),
            "A missing root fs.read grant must fail before leaving any build output.");

        await File.WriteAllTextAsync(missingGrantManifestPath, originalManifest, new UTF8Encoding(false));
        var staleRestoredGrant = await harness.InvokePackageDirectoryAsync(
            "scan-cli-restored-grant-stale-check", missingGrantRoot, "check", "--json");
        AssertGenericLockDiagnostic(staleRestoredGrant, "restored scanner fs.read grant");
        var restoredGrantLock = await harness.InvokePackageDirectoryAsync(
            "scan-cli-restored-grant-relock", missingGrantRoot, "lock");
        AssertEqual(0, restoredGrantLock.ExitCode, Describe(restoredGrantLock));
        var restoredGrantCheck = await harness.InvokePackageDirectoryAsync(
            "scan-cli-restored-grant-check", missingGrantRoot, "check", "--json");
        AssertEqual(0, restoredGrantCheck.ExitCode, Describe(restoredGrantCheck));
        var restoredGrantBuild = await harness.InvokePackageDirectoryAsync(
            "scan-cli-restored-grant-build", missingGrantRoot, "build");
        AssertEqual(0, restoredGrantBuild.ExitCode, Describe(restoredGrantBuild));
        var restoredExecutablePath = ParseBuiltArtifact(restoredGrantBuild, "Built executable: ");
        using (var restoredSchema = JsonDocument.Parse(
                   await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(restoredExecutablePath)!, "command-schema.json"))))
        {
            var restoredCapabilities = restoredSchema.RootElement.GetProperty("commands")[0].GetProperty("capabilities");
            AssertEqual(1, restoredCapabilities.GetArrayLength(),
                "Restoring the root grant must restore exactly the scanner's fs.read command requirement.");
            AssertEqual("fs.read", restoredCapabilities[0].GetString(),
                "The relocked scanner command schema must require only fs.read.");
        }
        var restoredNormalizedRun = await harness.InvokePackageDirectoryAsync(
            "scan-cli-restored-grant-normalized-run", missingGrantRoot,
            "run", "--", "scan", textPath, "--normalize");
        AssertRunOutput("scan λ 😀" + Environment.NewLine, restoredNormalizedRun);
    }

    private static async Task TestTextValidationExample(Harness harness)
    {
        var packageRoot = Path.Combine(harness.RepositoryRoot, "examples", "text-validation");
        var librarySource = await File.ReadAllTextAsync(
            Path.Combine(packageRoot, "src", "text", "validation.hob"));

        var check = await harness.InvokePackageDirectoryAsync(
            "text-validation-example-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length,
            "The maintained validation library should check with no diagnostics.");

        var build = await harness.InvokePackageDirectoryAsync(
            "text-validation-example-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        AssertTrue(build.StandardOutput.StartsWith("Built library: ", StringComparison.Ordinal),
            $"The pure library package should build a managed library. {Describe(build)}");
        var artifact = build.StandardOutput["Built library: ".Length..].Trim();
        AssertTrue(Path.IsPathFullyQualified(artifact) && File.Exists(artifact),
            $"Expected the built library DLL at {artifact}. {Describe(build)}");

        var unicodeInput = "\u00A0\u3000hello 😀\u3000\u00A0";
        var cases = new (string Name, string MainSource, string ExpectedOutput)[]
        {
            ("text-validation-empty", """
                module app::main;
                fn describe_too_long(max: u32) -> Text effects {} {
                    if max == 0u32 { return "too long with zero limit"; } else { return "too long"; }
                }
                pub fn main() -> Text effects {} {
                    return match self::text::validation::normalize("") {
                        self::text::validation::Validation.Valid(value) => "unexpected success",
                        self::text::validation::Validation.Invalid(error) => match error {
                            self::text::validation::NormalizeError.Empty => "empty",
                            self::text::validation::NormalizeError.TooLong(max) => self::app::main::describe_too_long(max),
                        },
                    };
                }
                """, "empty" + Environment.NewLine),
            ("text-validation-nonempty", $$"""
                module app::main;
                pub fn main() -> Text effects {} {
                    return match self::text::validation::normalize("{{unicodeInput}}") {
                        self::text::validation::Validation.Valid(value) => value.value,
                        self::text::validation::Validation.Invalid(error) => "unexpected error",
                    };
                }
                """, "hello 😀" + Environment.NewLine)
        };

        foreach (var (name, mainSource, expectedOutput) in cases)
        {
            var consumerRoot = await harness.WritePackageAsync(
                name,
                CliPackageManifest(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.hob"] = librarySource,
                    ["src/app/main.hob"] = mainSource
                });
            var consumerCheck = await harness.InvokePackageDirectoryAsync(
                $"{name}-check", consumerRoot, "check", "--json");
            AssertEqual(0, consumerCheck.ExitCode, Describe(consumerCheck));
            AssertEqual(0, ParseDiagnosticSnapshots(consumerCheck.StandardOutput).Length,
                $"The same-package normalization consumer {name} should check cleanly.");

            var run = await harness.InvokePackageDirectoryAsync($"{name}-run", consumerRoot, "run");
            AssertRunOutput(expectedOutput, run);
        }

        var nominalOptionSource = """
            module app::main;
            fn return_normalized(candidate: Option<validation::text::validation::Normalized<Text>>) ->
                validation::text::validation::Normalized<Text> effects {} {
                return candidate;
            }
            pub fn main() -> i32 effects {} {
                let normalized: validation::text::validation::Validation<validation::text::validation::Normalized<Text>> =
                    validation::text::validation::normalize("ready");
                let maybe: Option<validation::text::validation::Normalized<Text>> = match normalized {
                    validation::text::validation::Validation.Valid(value) => Some(value),
                    validation::text::validation::Validation.Invalid(error) => None,
                };
                return self::app::main::return_normalized(maybe).value.length;
            }
            """;
        var nominalOptionManifest = CliPackageManifest()
            .Replace("name = \"harness-package\"", "name = \"text-validation-option-is-not-normalized\"", StringComparison.Ordinal)
            + "\n[dependencies]\nvalidation = \"../validation\"\n";
        var nominalOptionRoot = await harness.WritePackageGraphAsync(
            "text-validation-option-is-not-normalized",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(nominalOptionManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = nominalOptionSource
                    }),
                ["validation"] = new PackageFixture(LibraryPackageManifest("text-validation-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/text/validation.hob"] = librarySource
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "text-validation-option-is-not-normalized-lock", nominalOptionRoot, "lock"));
        var nominalOptionCheck = await harness.InvokePackageDirectoryAsync(
            "text-validation-option-is-not-normalized-check", nominalOptionRoot, "check", "--json");
        AssertEqual(1, nominalOptionCheck.ExitCode, Describe(nominalOptionCheck));
        using (var mismatchDocument = JsonDocument.Parse(nominalOptionCheck.StandardOutput))
        {
            AssertJsonPropertyOrder(mismatchDocument.RootElement, "schemaVersion,diagnostics");
            AssertEqual(1, mismatchDocument.RootElement.GetProperty("schemaVersion").GetInt32(),
                "The cross-package N1 failure must use diagnostics schema 1.");
            AssertEqual(1, mismatchDocument.RootElement.GetProperty("diagnostics").GetArrayLength(),
                $"The cross-package N1 failure must contain exactly one diagnostic. {Describe(nominalOptionCheck)}");
        }
        var nominalOptionDiagnostics = ParseDiagnosticSnapshots(nominalOptionCheck.StandardOutput);
        AssertEqual(1, nominalOptionDiagnostics.Length, Describe(nominalOptionCheck));
        AssertEqual("E_TYPE_MISMATCH", nominalOptionDiagnostics[0].Code, Describe(nominalOptionCheck));
        AssertEqual(Path.GetFullPath(Path.Combine(nominalOptionRoot, "src", "app", "main.hob")),
            Path.GetFullPath(nominalOptionDiagnostics[0].File),
            "The cross-package nominal mismatch must point to the current consumer source.");
        AssertRangeAtToken(nominalOptionSource, nominalOptionDiagnostics[0], "candidate", 2);
        var nominalOptionBuild = await harness.InvokePackageDirectoryAsync(
            "text-validation-option-is-not-normalized-build", nominalOptionRoot, "build");
        AssertTrue(nominalOptionBuild.ExitCode != 0, Describe(nominalOptionBuild));
        AssertTrue((nominalOptionBuild.StandardOutput + nominalOptionBuild.StandardError)
                .Contains("E_TYPE_MISMATCH", StringComparison.Ordinal),
            $"The cross-package nominal mismatch build must report E_TYPE_MISMATCH. {Describe(nominalOptionBuild)}");
        AssertTrue(!nominalOptionBuild.StandardOutput.Contains("Built executable: ", StringComparison.Ordinal),
            "A rejected cross-package nominal mismatch build must not print a success artifact prefix.");
        var nominalOptionOutput = Path.Combine(nominalOptionRoot, "out");
        AssertTrue(!Directory.Exists(nominalOptionOutput)
            || !Directory.EnumerateFiles(nominalOptionOutput, "*", SearchOption.AllDirectories).Any(),
            "A rejected cross-package nominal mismatch must not leave an artifact, receipt, or schema.");

        var genericConsumer = await harness.WritePackageAsync(
            "text-validation-generic-consumer",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/text/validation.hob"] = librarySource,
                ["src/app/main.hob"] = """
                    module app::main;
                    fn too_long_length(max: u32) -> i32 effects {} {
                        if max == 0u32 { return -1; } else { return 0; }
                    }
                    pub fn main() -> i32 effects {} {
                        let normalized: self::text::validation::Validation<self::text::validation::Normalized<Text>> = self::text::validation::normalize("hello 😀");
                        let normalized_value: self::text::validation::Normalized<Text> = match normalized {
                            self::text::validation::Validation.Valid(value) => value,
                            self::text::validation::Validation.Invalid(error) => self::text::validation::Normalized<Text> { value: "" },
                        };
                        let text_option: Option<self::text::validation::Normalized<Text>> = Some(normalized_value);
                        let text_result: Result<self::text::validation::Normalized<Text>, self::text::validation::NormalizeError> = self::text::validation::require(text_option, self::text::validation::NormalizeError.Empty);
                        let number_option: Option<i32> = Some(35);
                        let number_result: Result<i32, Text> = self::text::validation::require(number_option, "missing");
                        let text_length: i32 = match text_result {
                            Ok(value) => value.value.length,
                            Err(error) => match error {
                                self::text::validation::NormalizeError.Empty => 0,
                                self::text::validation::NormalizeError.TooLong(max) =>
                                    self::app::main::too_long_length(max),
                            },
                        };
                        return match number_result {
                            Ok(number) => number + text_length,
                            Err(message) => 0,
                        };
                    }
                    """
            });
        var genericCheck = await harness.InvokePackageDirectoryAsync(
            "text-validation-generic-consumer-check", genericConsumer, "check", "--json");
        AssertEqual(0, genericCheck.ExitCode, Describe(genericCheck));
        AssertEqual(0, ParseDiagnosticSnapshots(genericCheck.StandardOutput).Length,
            "Generic trait dispatch, explicit generic wrappers, and both require instantiations should check cleanly.");
        var genericRun = await harness.InvokePackageDirectoryAsync(
            "text-validation-generic-consumer-run", genericConsumer, "run");
        AssertRunOutput("42" + Environment.NewLine, genericRun);
    }

    private static string GenericValidationLibrarySource(string modulePath) => $$"""
        module {{modulePath}};

        pub struct Normalized<T> { value: T }

        pub union NormalizeError { Empty }

        pub union Validation<T> { Valid(T), Invalid(self::{{modulePath}}::NormalizeError) }

        pub trait Normalize {
            fn normalize(input: Self) -> Result<Self, self::{{modulePath}}::NormalizeError> effects {};
        }

        fn normalize_text(input: Text) -> Result<Text, self::{{modulePath}}::NormalizeError> effects {} {
            if input.length == 0 {
                return Err(self::{{modulePath}}::NormalizeError.Empty);
            }

            return Ok(input.trim());
        }

        pub impl self::{{modulePath}}::Normalize for Text {
            normalize = self::{{modulePath}}::normalize_text;
        }

        pub fn normalize<T: self::{{modulePath}}::Normalize>(input: T) -> self::{{modulePath}}::Validation<self::{{modulePath}}::Normalized<T>> effects {} {
            return match self::{{modulePath}}::Normalize.normalize(input) {
                Ok(value) => self::{{modulePath}}::Validation<self::{{modulePath}}::Normalized<T>>.Valid(
                    self::{{modulePath}}::Normalized<T> { value: value }
                ),
                Err(error) => self::{{modulePath}}::Validation<self::{{modulePath}}::Normalized<T>>.Invalid(error),
            };
        }

        pub fn require<T, E>(value: Option<T>, error: E) -> Result<T, E> effects {} {
            return match value {
                Some(item) => Ok(item),
                None => Err(error),
            };
        }
        """;

    private static void AssertGenericTraitReportFacts(
        string apiJson,
        string auditJson,
        string dependencyAlias,
        bool expectVisibleImpl)
    {
        using var apiDocument = JsonDocument.Parse(apiJson);
        using var auditDocument = JsonDocument.Parse(auditJson);
        var api = apiDocument.RootElement;
        var audit = auditDocument.RootElement;
        AssertInspectApiPropertyOrder(api);
        AssertAuditPropertyOrder(audit);
        AssertEqual(13, api.GetProperty("schema_version").GetInt32(),
            "The maintained generic library uses inspect API schema 13.");
        AssertEqual(11, audit.GetProperty("schema_version").GetInt32(),
            "The maintained generic library uses audit schema 11.");

        bool HasSourceId(JsonElement declaration, string sourceId) =>
            declaration.TryGetProperty("source_ids", out var sourceIds)
            && sourceIds.ValueKind == JsonValueKind.Array
            && sourceIds.EnumerateArray()
                .Any(candidate => string.Equals(candidate.GetString(), sourceId, StringComparison.Ordinal));

        var normalize = api.GetProperty("functions").EnumerateArray()
            .Single(item => item.GetProperty("source_ids").EnumerateArray()
                .Any(sourceId => sourceId.GetString()?.StartsWith(dependencyAlias + "::", StringComparison.Ordinal) == true
                    && sourceId.GetString()?.EndsWith("::normalize", StringComparison.Ordinal) == true));
        var normalizeSourceId = normalize.GetProperty("source_ids").EnumerateArray()
            .Select(sourceId => sourceId.GetString() ?? string.Empty)
            .Single(sourceId => sourceId.StartsWith(dependencyAlias + "::", StringComparison.Ordinal)
                && sourceId.EndsWith("::normalize", StringComparison.Ordinal));
        var normalizeIdentitySuffix = normalizeSourceId[(dependencyAlias.Length + 2)..];
        var finalSeparator = normalizeIdentitySuffix.LastIndexOf("::", StringComparison.Ordinal);
        AssertTrue(finalSeparator > 0, "The generic normalize API identity must contain its declaring module.");
        var modulePath = normalizeIdentitySuffix[..finalSeparator];
        var normalizedSourceId = $"{dependencyAlias}::{modulePath}::Normalized";
        var validationSourceId = $"{dependencyAlias}::{modulePath}::Validation";
        var normalized = api.GetProperty("structs").EnumerateArray()
            .Single(item => HasSourceId(item, normalizedSourceId));
        AssertJsonPropertyOrder(normalized, "id,source_ids,package,type_parameters,fields");
        var normalizedParameter = normalized.GetProperty("type_parameters")[0];
        AssertJsonPropertyOrder(normalizedParameter, "name,ordinal");
        AssertEqual("T", normalizedParameter.GetProperty("name").GetString(),
            "Normalized<T> must preserve its declared generic parameter name.");
        AssertEqual(0, normalizedParameter.GetProperty("ordinal").GetInt32(),
            "Normalized<T> must preserve its zero-based type parameter ordinal.");
        var normalizedField = normalized.GetProperty("fields")[0];
        AssertEqual("value", normalizedField.GetProperty("name").GetString(),
            "Normalized<T> must expose its value field in declaration order.");
        AssertEqual("type_parameter", normalizedField.GetProperty("type").GetProperty("kind").GetString(),
            "Normalized<T>.value must retain the generic parameter in API types.");
        AssertEqual("T", normalizedField.GetProperty("type").GetProperty("name").GetString(),
            "Normalized<T>.value must refer to its declared parameter.");

        var validation = api.GetProperty("unions").EnumerateArray()
            .Single(item => HasSourceId(item, validationSourceId));
        AssertJsonPropertyOrder(validation, "id,source_ids,package,type_parameters,variants");
        var validationParameter = validation.GetProperty("type_parameters")[0];
        AssertJsonPropertyOrder(validationParameter, "name,ordinal");
        AssertEqual("T", validationParameter.GetProperty("name").GetString(),
            "Validation<T> must preserve its declared generic parameter.");
        var validVariant = validation.GetProperty("variants").EnumerateArray()
            .Single(variant => variant.GetProperty("name").GetString() == "Valid");
        AssertEqual("type_parameter", validVariant.GetProperty("payload")[0].GetProperty("type").GetProperty("kind").GetString(),
            "Validation<T>.Valid must retain its generic payload type.");
        AssertEqual("T", validVariant.GetProperty("payload")[0].GetProperty("type").GetProperty("name").GetString(),
            "Validation<T>.Valid must refer to its declared parameter.");

        var trait = api.GetProperty("traits").EnumerateArray()
            .Single(item => HasSourceId(item, $"{dependencyAlias}::{modulePath}::Normalize"));
        var traitId = trait.GetProperty("id").GetString() ?? string.Empty;
        AssertTrue(traitId.StartsWith("hob.trait.v1.", StringComparison.Ordinal),
            "The Normalize trait must use a stable semantic identity.");
        var traitMethod = trait.GetProperty("methods").EnumerateArray().Single();
        AssertEqual("normalize", traitMethod.GetProperty("name").GetString(),
            "The Normalize trait method name should remain stable.");
        AssertEqual("self", traitMethod.GetProperty("parameters")[0].GetProperty("type").GetProperty("kind").GetString(),
            "The Normalize method parameter should remain the implicit Self type.");
        var traitResult = traitMethod.GetProperty("return_type");
        AssertEqual("result", traitResult.GetProperty("kind").GetString(),
            "Normalize.normalize must return Result<Self, NormalizeError>.");
        AssertEqual("self", traitResult.GetProperty("ok").GetProperty("kind").GetString(),
            "Normalize.normalize must return Self on success; only generic normalize adds the wrapper.");
        AssertEqual("NormalizeError", traitResult.GetProperty("error").GetProperty("name").GetString(),
            "Normalize.normalize must retain the declared NormalizeError failure type.");

        var apiImpls = api.GetProperty("trait_impls").EnumerateArray()
            .Where(item => item.GetProperty("trait").GetString() == traitId).ToArray();
        if (expectVisibleImpl)
        {
            AssertEqual(1, apiImpls.Length,
                "The public closed Text implementation should be addressable from the root API.");
            AssertEqual("public", apiImpls[0].GetProperty("visibility").GetString(),
                "The closed Text implementation should remain public.");
            AssertEqual("Text", apiImpls[0].GetProperty("target").GetProperty("name").GetString(),
                "The public implementation should target Text.");
            AssertEqual("normalize", apiImpls[0].GetProperty("methods")[0].GetString(),
                "The API should report the fulfilled method without exposing its binding function.");
        }
        else
        {
            AssertEqual(0, apiImpls.Length,
                "A package-private implementation must not appear in the source-facing API.");
        }

        var apiFunctions = api.GetProperty("functions").EnumerateArray().ToArray();
        AssertTrue(HasSourceId(normalize, normalizeSourceId),
            "The dependency generic normalize function should retain its source-facing identity.");
        var normalizeTypeParameter = normalize.GetProperty("type_parameters")[0];
        AssertJsonPropertyOrder(normalizeTypeParameter, "name,ordinal,bounds");
        AssertEqual(traitId, normalizeTypeParameter.GetProperty("bounds")[0].GetProperty("trait").GetString(),
            "Generic normalize must retain its ordered Normalize bound.");
        var returnType = normalize.GetProperty("return_type");
        AssertEqual("nominal", returnType.GetProperty("kind").GetString(),
            "Generic normalize must expose its nominal Validation return type.");
        AssertEqual("Validation", returnType.GetProperty("name").GetString(),
            "Generic normalize must return Validation<Normalized<T>>.");
        var normalizedArgument = returnType.GetProperty("type_arguments")[0];
        AssertEqual("nominal", normalizedArgument.GetProperty("kind").GetString(),
            "The Validation payload must retain its nested nominal Normalized type.");
        AssertEqual("Normalized", normalizedArgument.GetProperty("name").GetString(),
            "The Validation payload must be Normalized<T>.");
        AssertEqual("type_parameter", normalizedArgument.GetProperty("type_arguments")[0].GetProperty("kind").GetString(),
            "The nested Normalized argument must retain the function's generic type parameter.");
        var forwardedCall = normalize.GetProperty("trait_calls").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == "method");
        AssertEqual(traitId, forwardedCall.GetProperty("trait").GetString(),
            "Generic Normalize dispatch must identify the checked trait.");
        AssertEqual("normalize", forwardedCall.GetProperty("method").GetString(),
            "Generic Normalize dispatch must identify the selected method.");
        var forwardedWitness = forwardedCall.GetProperty("witness");
        AssertEqual("bound", forwardedWitness.GetProperty("kind").GetString(),
            "Generic Normalize dispatch must forward its bound witness.");
        AssertEqual(0, forwardedWitness.GetProperty("type_parameter_ordinal").GetInt32(),
            "The forwarded witness must identify its type parameter ordinal.");
        AssertEqual(0, forwardedWitness.GetProperty("bound_ordinal").GetInt32(),
            "The forwarded witness must identify its bound ordinal.");

        var apiMain = apiFunctions.Single(item => HasSourceId(item, "self::app::main::main"));
        var apiNormalizeCall = apiMain.GetProperty("calls").EnumerateArray().Single(call =>
            call.GetProperty("module").GetString() == modulePath
            && call.GetProperty("name").GetString() == "normalize");
        AssertTrue(apiNormalizeCall.GetProperty("source_ids").EnumerateArray()
                .Any(sourceId => sourceId.GetString() == normalizeSourceId),
            "Public API direct calls must preserve the dependency source identity of generic normalize.");
        AssertTrue(!apiMain.GetProperty("calls").EnumerateArray()
                .Any(call => call.GetProperty("name").GetString() == "normalize_text"),
            "The public API must not expose a private trait implementation binding as a source-level call.");
        var concreteApiWitness = apiMain.GetProperty("trait_calls").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == "function_call")
            .GetProperty("witnesses").EnumerateArray()
            .Single(item => item.GetProperty("trait").GetString() == traitId)
            .GetProperty("witness");
        if (expectVisibleImpl)
        {
            AssertEqual("impl", concreteApiWitness.GetProperty("kind").GetString(),
                "A public root-to-dependency call should expose the addressable closed implementation witness.");
            AssertEqual(apiImpls[0].GetProperty("id").GetString(), concreteApiWitness.GetProperty("id").GetString(),
                "The generic call witness should use the API implementation identity.");
        }
        else
        {
            AssertEqual(JsonValueKind.Null, concreteApiWitness.ValueKind,
                "A hidden implementation witness must not disclose a private identity through the API.");
        }
        AssertTrue(!apiJson.Contains("normalize_text", StringComparison.Ordinal),
            "The public API must not expose the private Normalize binding function.");

        var compiler = audit.GetProperty("compiler");
        var auditTrait = compiler.GetProperty("traits").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == traitId);
        var auditImpl = compiler.GetProperty("trait_impls").EnumerateArray()
            .Single(item => item.GetProperty("trait").GetString() == traitId
                && item.GetProperty("target").GetProperty("name").GetString() == "Text");
        var bindingFunction = auditImpl.GetProperty("methods")[0].GetProperty("binding_function");
        if (expectVisibleImpl)
            AssertEqual(apiImpls[0].GetProperty("id").GetString(), auditImpl.GetProperty("id").GetString(),
                "API and audit should share the portable closed implementation identity.");
        AssertEqual("normalize_text", bindingFunction.GetProperty("name").GetString(),
            "Audit must retain the private Normalize binding function identity.");
        var auditNormalize = compiler.GetProperty("functions").EnumerateArray()
            .Single(item => item.GetProperty("module").GetString() == modulePath
                && item.GetProperty("name").GetString() == "normalize");
        AssertEqual("normalize", auditNormalize.GetProperty("name").GetString(),
            "Audit must include the generic Normalize dispatch function.");
        var auditForwarded = auditNormalize.GetProperty("trait_calls").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == "method");
        AssertEqual(auditTrait.GetProperty("id").GetString(), auditForwarded.GetProperty("trait").GetString(),
            "Audit must retain the stable trait identity on forwarded method dispatch.");
        AssertEqual("bound", auditForwarded.GetProperty("witness").GetProperty("kind").GetString(),
            "Audit must retain the generic function's forwarded bound witness.");
        var auditMain = compiler.GetProperty("functions").EnumerateArray()
            .Single(item => item.GetProperty("module").GetString() == "app::main"
                && item.GetProperty("name").GetString() == "main");
        var auditGenericCall = auditMain.GetProperty("direct_calls").EnumerateArray().Single(call =>
            call.GetProperty("module").GetString() == modulePath
            && call.GetProperty("name").GetString() == "normalize"
            && call.GetProperty("package").GetProperty("name").GetString()
                == auditNormalize.GetProperty("package").GetProperty("name").GetString()
            && call.GetProperty("package").GetProperty("version").GetString()
                == auditNormalize.GetProperty("package").GetProperty("version").GetString()
            && call.GetProperty("package").GetProperty("path").GetString()
                == auditNormalize.GetProperty("package").GetProperty("path").GetString());
        AssertEqual("normalize", auditGenericCall.GetProperty("name").GetString(),
            "Audit direct-call facts must retain the source-level generic normalize call.");
        var concreteAuditWitness = auditMain.GetProperty("trait_calls").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == "function_call")
            .GetProperty("witnesses").EnumerateArray()
            .Single(item => item.GetProperty("trait").GetString() == auditTrait.GetProperty("id").GetString())
            .GetProperty("witness");
        AssertEqual("impl", concreteAuditWitness.GetProperty("kind").GetString(),
            "Audit must retain the concrete Text implementation selected by the generic call.");
        AssertEqual(auditImpl.GetProperty("id").GetString(), concreteAuditWitness.GetProperty("id").GetString(),
            "Audit's concrete witness must point to the exact checked implementation identity.");
        AssertEqual("primitive", concreteAuditWitness.GetProperty("target").GetProperty("kind").GetString(),
            "Audit's concrete witness must retain the implementation's primitive target kind.");
        AssertEqual(auditImpl.GetProperty("target").GetProperty("name").GetString(),
            concreteAuditWitness.GetProperty("target").GetProperty("name").GetString(),
            "Audit's concrete witness must retain the closed target from its implementation declaration.");
        AssertEqual(modulePath, bindingFunction.GetProperty("module").GetString(),
            "The implementation binding identity must retain its declaring module.");
        var bindingPackage = bindingFunction.GetProperty("package");
        var auditBindingFunction = compiler.GetProperty("functions").EnumerateArray().Single(candidate =>
            candidate.GetProperty("module").GetString() == modulePath
            && candidate.GetProperty("name").GetString() == "normalize_text"
            && candidate.GetProperty("package").GetProperty("name").GetString() == bindingPackage.GetProperty("name").GetString()
            && candidate.GetProperty("package").GetProperty("version").GetString() == bindingPackage.GetProperty("version").GetString()
            && candidate.GetProperty("package").GetProperty("path").GetString() == bindingPackage.GetProperty("path").GetString());
        AssertEqual(bindingFunction.GetProperty("name").GetString(), auditBindingFunction.GetProperty("name").GetString(),
            "Audit function facts must include the private implementation binding selected by the concrete witness.");
    }

    private static async Task TestSourceFormatter(Harness harness)
    {
        static string[] SnapshotTree(string root)
        {
            var paths = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Prepend(root)
                .ToArray();
            return paths
                .OrderBy(path => path == root
                    ? "."
                    : Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal)
                .Select(path =>
                {
                    var relative = path == root
                        ? "."
                        : Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                    var timestamp = File.GetLastWriteTimeUtc(path).Ticks;
                    if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                        return $"D|{relative}|{timestamp}";

                    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                    return $"F|{relative}|{timestamp}|{hash}";
                })
                .ToArray();
        }

        static void AssertTreeUnchanged(string[] before, string[] after, string context) =>
            AssertEqual(string.Join('\n', before), string.Join('\n', after), context);

        var repositoryRoot = FindRepositoryRoot()
            ?? throw new InvalidOperationException("Could not locate the Hobthrush repository root.");
        var formatterFixtureRoot = Path.Combine(repositoryRoot, "tests", "Hob.IntegrationTests", "Fixtures", "SourceFormatter");
        var goldenInput = await File.ReadAllTextAsync(Path.Combine(formatterFixtureRoot, "unstyled.hob"));
        var goldenExpected = await File.ReadAllBytesAsync(Path.Combine(formatterFixtureRoot, "expected.hob"));
        var goldenDirectory = Path.Combine(harness.TemporaryRoot, "formatter-golden");
        Directory.CreateDirectory(goldenDirectory);
        var goldenPath = await harness.WriteSourceAsync(goldenDirectory, "main.hob", goldenInput);
        var goldenFormat = await harness.InvokeFileAsync("formatter-golden", goldenPath, "fmt");
        AssertEqual(0, goldenFormat.ExitCode, Describe(goldenFormat));
        var formattedGoldenBytes = await File.ReadAllBytesAsync(goldenPath);
        AssertTrue(goldenExpected.SequenceEqual(formattedGoldenBytes),
            "The source formatter must match the canonical golden output byte for byte.");

        var goldenTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(goldenPath, goldenTime);
        Directory.SetLastWriteTimeUtc(goldenDirectory, goldenTime);
        var goldenSnapshot = SnapshotTree(goldenDirectory);
        var cleanFileCheck = await harness.InvokeFileAsync("formatter-golden-check", goldenPath, "fmt", "--check");
        AssertEqual(0, cleanFileCheck.ExitCode, Describe(cleanFileCheck));
        AssertTreeUnchanged(goldenSnapshot, SnapshotTree(goldenDirectory),
            "A clean file check must preserve bytes, timestamps, and directory contents.");

        var crlfPath = Path.Combine(goldenDirectory, "crlf.hob");
        var unrelatedPath = Path.Combine(goldenDirectory, "unrelated.txt");
        var crlfSource = "\uFEFF" + goldenInput.Replace("\n", "\r\n", StringComparison.Ordinal);
        await File.WriteAllBytesAsync(crlfPath, Encoding.UTF8.GetBytes(crlfSource));
        await File.WriteAllTextAsync(unrelatedPath, "leave this file alone\n");
        File.SetLastWriteTimeUtc(crlfPath, goldenTime);
        var dirtySnapshot = SnapshotTree(goldenDirectory);
        var dirtyFileCheck = await harness.InvokeFileAsync("formatter-crlf-dirty-check", crlfPath, "fmt", "--check");
        AssertEqual(1, dirtyFileCheck.ExitCode, Describe(dirtyFileCheck));
        AssertTrue(dirtyFileCheck.StandardError.Contains("Would reformat:", StringComparison.Ordinal),
            "A dirty file check must identify the file that needs formatting.");
        AssertTreeUnchanged(dirtySnapshot, SnapshotTree(goldenDirectory),
            "A dirty file check must preserve source bytes, timestamps, and directory contents.");

        var crlfFormat = await harness.InvokeFileAsync("formatter-crlf-format", crlfPath, "fmt");
        AssertEqual(0, crlfFormat.ExitCode, Describe(crlfFormat));
        var formattedCrlfBytes = await File.ReadAllBytesAsync(crlfPath);
        AssertTrue(goldenExpected.SequenceEqual(formattedCrlfBytes),
            "Formatting CRLF UTF-8 with a BOM must write canonical UTF-8 without a BOM and LF endings.");
        AssertEqual("leave this file alone\n", await File.ReadAllTextAsync(unrelatedPath),
            "File mode must not touch adjacent files.");

        var invalidUtf8Path = Path.Combine(goldenDirectory, "invalid-utf8.hob");
        var invalidUtf8 = new byte[] { 0xC3, 0x28 };
        await File.WriteAllBytesAsync(invalidUtf8Path, invalidUtf8);
        var invalidEncoding = await harness.InvokeFileAsync("formatter-invalid-utf8", invalidUtf8Path, "fmt");
        AssertEqual(1, invalidEncoding.ExitCode, Describe(invalidEncoding));
        AssertTrue(invalidEncoding.StandardError.Contains("not valid UTF-8", StringComparison.Ordinal),
            "Invalid UTF-8 must have a stable source diagnostic.");
        var invalidUtf8After = await File.ReadAllBytesAsync(invalidUtf8Path);
        AssertTrue(invalidUtf8.SequenceEqual(invalidUtf8After),
            "Invalid UTF-8 input must remain byte-identical.");

        var packageManifest = LibraryPackageManifest("formatter-package")
            + "\n[managed_adapter]\nbridge_id = \"hob.sha256-text.v1\"\n"
            + "target_framework = \"net10.0\"\n"
            + "assembly_path = \"missing/Formatter.Adapter.dll\"\n"
            + $"assembly_sha256 = \"{new string('a', 64)}\"\n"
            + "\n[dependencies]\nlocal = \"../local-dependency\"\nmissing = \"../missing-dependency\"\n";
        const string sourceA = "module a; pub fn value() -> i32 effects {} { return 1; }";
        const string sourceZ = "module z; pub fn value() -> i32 effects {} { return 2; }";
        var packageRoot = await harness.WritePackageAsync(
            "formatter-package",
            packageManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/z.hob"] = sourceZ,
                ["src/a.hob"] = sourceA
            });
        var packageParent = Path.GetDirectoryName(packageRoot)
            ?? throw new InvalidOperationException("The formatter test package has no parent directory.");
        var dependencyRoot = Path.Combine(packageParent, "local-dependency");
        var missingDependencyRoot = Path.Combine(packageParent, "missing-dependency");
        Directory.CreateDirectory(Path.Combine(dependencyRoot, "src"));
        await File.WriteAllTextAsync(Path.Combine(dependencyRoot, "hob.toml"), LibraryPackageManifest("formatter-dependency"));
        var dependencySource = Path.Combine(dependencyRoot, "src", "dependency.hob");
        const string dependencyText = "module dependency; pub fn value() -> i32 effects {} { return 3; }";
        await File.WriteAllTextAsync(dependencySource, dependencyText);
        var lockPath = Path.Combine(packageRoot, "hob.lock");
        await File.WriteAllTextAsync(lockPath, "lock sentinel\n");
        var unrelatedPackagePath = Path.Combine(packageRoot, "obj", "generated.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelatedPackagePath)!);
        await File.WriteAllTextAsync(unrelatedPackagePath, "generated sentinel\n");

        var packageSnapshot = SnapshotTree(packageRoot);
        var dependencySnapshot = SnapshotTree(dependencyRoot);
        var packageDirtyCheck = await harness.InvokeCompilerCommandAsync("fmt", packageRoot, "--check");
        AssertEqual(1, packageDirtyCheck.ExitCode, Describe(packageDirtyCheck));
        var orderedA = packageDirtyCheck.StandardError.IndexOf(Path.Combine(packageRoot, "src", "a.hob"), StringComparison.Ordinal);
        var orderedZ = packageDirtyCheck.StandardError.IndexOf(Path.Combine(packageRoot, "src", "z.hob"), StringComparison.Ordinal);
        AssertTrue(orderedA >= 0 && orderedZ > orderedA,
            "Package check output must follow ordinal source-path order.");
        AssertTreeUnchanged(packageSnapshot, SnapshotTree(packageRoot),
            "A dirty package check must preserve source bytes, timestamps, and directory contents.");
        AssertTreeUnchanged(dependencySnapshot, SnapshotTree(dependencyRoot),
            "Formatting the root package must not inspect or modify dependency source files.");

        var packageFormat = await harness.InvokeCompilerCommandAsync("fmt", packageRoot);
        AssertEqual(0, packageFormat.ExitCode, Describe(packageFormat));
        AssertEqual("module a;\n\npub fn value() -> i32 effects {} {\n    return 1;\n}\n",
            await File.ReadAllTextAsync(Path.Combine(packageRoot, "src", "a.hob")),
            "Package mode must format root package sources.");
        AssertEqual(dependencyText, await File.ReadAllTextAsync(dependencySource),
            "Package mode must leave a path dependency's source unchanged.");
        AssertEqual(packageManifest, await File.ReadAllTextAsync(Path.Combine(packageRoot, "hob.toml")),
            "Package mode must not format the manifest.");
        AssertEqual("lock sentinel\n", await File.ReadAllTextAsync(lockPath),
            "Package mode must not format the lockfile.");
        AssertEqual("generated sentinel\n", await File.ReadAllTextAsync(unrelatedPackagePath),
            "Package mode must not change unrelated generated files.");
        AssertTreeUnchanged(dependencySnapshot, SnapshotTree(dependencyRoot),
            "Formatting must leave all dependency files and timestamps unchanged.");
        AssertTrue(!Directory.Exists(missingDependencyRoot),
            "Formatting must not fetch or create a missing path dependency.");
        var cleanPackageSnapshot = SnapshotTree(packageRoot);
        var cleanPackageCheck = await harness.InvokeCompilerCommandAsync("fmt", packageRoot, "--check");
        AssertEqual(0, cleanPackageCheck.ExitCode, Describe(cleanPackageCheck));
        AssertTreeUnchanged(cleanPackageSnapshot, SnapshotTree(packageRoot),
            "A clean package check must preserve file hashes, timestamps, and directory contents.");

        var processPackage = await harness.WritePackageAsync(
            "formatter-process-package",
            """
            name = "formatter-process-package"
            version = "0.1.0"
            kind = "cli"
            source_root = "src"
            entry_module = "app"
            process_windows_path = "bin/missing-runner.exe"
            process_windows_sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"

            [capabilities]
            process.spawn = "allow"
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/main.hob"] = "module app; pub fn main()->i32 effects {}{return 42;}"
            });
        var processFormat = await harness.InvokeCompilerCommandAsync("fmt", processPackage);
        AssertEqual(0, processFormat.ExitCode, Describe(processFormat));
        AssertEqual(
            "module app;\n\npub fn main() -> i32 effects {} {\n    return 42;\n}\n",
            await File.ReadAllTextAsync(Path.Combine(processPackage, "src", "main.hob")),
            "Formatting must not inspect a configured but unavailable process executable.");

        var malformedPackage = await harness.WritePackageAsync(
            "formatter-malformed-package",
            LibraryPackageManifest("formatter-malformed"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/a.hob"] = sourceA,
                ["src/z.hob"] = "module z; fn broken( -> i32 effects {} { return 1; }"
            });
        var malformedSnapshot = SnapshotTree(malformedPackage);
        var malformedFormat = await harness.InvokeCompilerCommandAsync("fmt", malformedPackage);
        AssertEqual(1, malformedFormat.ExitCode, Describe(malformedFormat));
        AssertTrue(malformedFormat.StandardError.Contains("E_SYNTAX", StringComparison.Ordinal),
            "Malformed package source must report the ordinary parser diagnostic.");
        AssertTreeUnchanged(malformedSnapshot, SnapshotTree(malformedPackage),
            "A malformed module must prevent every source in the package from being rewritten.");

        using var fixtureManifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "fixtures", "manifest.json")));
        var syntaxErrors = new HashSet<string>(StringComparer.Ordinal)
        {
            "E_SYNTAX", "E_UNSUPPORTED", "E_NUMERIC_LITERAL_RANGE", "E_NUMERIC_LITERAL_TOO_LONG",
            "E_COMMAND_DECL", "E_ROUTE_DECL", "E_TRAIT_DECL"
        };
        var parserRejectedFixtureFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "09-literal-overflow.hob",
            "20-null-rejected.hob",
            "85-generic-union-pattern-type-arguments.hob",
            "108-invalid-wide-integer-literal-range.hob"
        };
        var fixtureSources = new Dictionary<string, string>(StringComparer.Ordinal);
        var fixtureIndex = 0;
        var activeFixtureCount = 0;
        var syntaxDiagnosticFixtureCount = 0;
        var parserRejectedFixtureFilesSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fixture in fixtureManifest.RootElement.EnumerateArray())
        {
            if (fixture.GetProperty("status").GetString() != "active")
                continue;
            activeFixtureCount++;

            var codes = new List<string>();
            if (fixture.TryGetProperty("expectedCode", out var expectedCode) && expectedCode.ValueKind == JsonValueKind.String)
                codes.Add(expectedCode.GetString()!);
            if (fixture.TryGetProperty("expectedCodes", out var expectedCodes) && expectedCodes.ValueKind == JsonValueKind.Array)
                codes.AddRange(expectedCodes.EnumerateArray().Select(code => code.GetString()!));
            var fixtureFile = fixture.GetProperty("file").GetString()!;
            if (codes.Any(syntaxErrors.Contains))
            {
                syntaxDiagnosticFixtureCount++;
                continue;
            }
            if (parserRejectedFixtureFiles.Contains(fixtureFile))
            {
                parserRejectedFixtureFilesSeen.Add(fixtureFile);
                continue;
            }

            var fixturePath = Path.Combine(repositoryRoot, "fixtures", fixtureFile);
            fixtureSources[$"src/module{fixtureIndex++:D3}.hob"] = await File.ReadAllTextAsync(fixturePath);
        }

        AssertEqual(126, activeFixtureCount,
            "The active formatter fixture corpus must stay aligned with the checked-in fixture manifest.");
        AssertEqual(11, syntaxDiagnosticFixtureCount,
            "Syntax-error fixture exclusions must stay aligned with the checked-in manifest.");
        AssertEqual(string.Join('\n', parserRejectedFixtureFiles.Order(StringComparer.Ordinal)),
            string.Join('\n', parserRejectedFixtureFilesSeen.Order(StringComparer.Ordinal)),
            "Only the four active fixtures rejected by Parser.Parse should be excluded from formatter coverage.");
        AssertEqual(111, fixtureSources.Count,
            "Every other active fixture should be included, including semantic-error cases.");
        foreach (var fixtureFile in parserRejectedFixtureFiles)
        {
            var rejectedSource = Path.Combine(repositoryRoot, "fixtures", fixtureFile);
            var rejectedFormat = await harness.InvokeFileAsync(
                $"formatter-parser-rejected-{Path.GetFileNameWithoutExtension(fixtureFile)}",
                rejectedSource,
                "fmt",
                "--check");
            AssertEqual(1, rejectedFormat.ExitCode, Describe(rejectedFormat));
            AssertTrue(rejectedFormat.StandardError.Contains("E_TYPE_MISMATCH", StringComparison.Ordinal),
                $"{fixtureFile} should retain Parser.Parse's E_TYPE_MISMATCH diagnostic.");
        }
        var fixturePackage = await harness.WritePackageAsync(
            "formatter-active-fixtures",
            LibraryPackageManifest("formatter-active-fixtures"),
            fixtureSources);
        var fixtureFormat = await harness.InvokeCompilerCommandAsync("fmt", fixturePackage);
        AssertEqual(0, fixtureFormat.ExitCode, Describe(fixtureFormat));
        var formattedFixtureSnapshot = SnapshotTree(fixturePackage);
        var fixtureCheck = await harness.InvokeCompilerCommandAsync("fmt", fixturePackage, "--check");
        AssertEqual(0, fixtureCheck.ExitCode, Describe(fixtureCheck));
        AssertTreeUnchanged(formattedFixtureSnapshot, SnapshotTree(fixturePackage),
            "Formatting all parser-valid active fixtures must be idempotent and check-only.");

        var usage = await harness.InvokeCompilerCommandAsync("fmt");
        AssertEqual(2, usage.ExitCode, Describe(usage));
        AssertEqual("Usage: hob fmt FILE_OR_PACKAGE [--check]" + Environment.NewLine,
            usage.StandardError, "The formatter must report its command-specific usage contract.");
        var checkWithoutTarget = await harness.InvokeCompilerCommandAsync("fmt", "--check");
        AssertEqual(2, checkWithoutTarget.ExitCode, Describe(checkWithoutTarget));
        AssertEqual(usage.StandardError, checkWithoutTarget.StandardError,
            "Check mode without a target must report command usage.");
    }

    private static async Task TestManagedLanguageTests(Harness harness)
    {
        var packageRoot = Path.Combine(harness.RepositoryRoot, "examples", "text-validation");
        var result = await harness.InvokePackageDirectoryAsync(
            "text-validation-language-tests", packageRoot, "test");
        var expected = string.Join(Environment.NewLine,
        [
            "PASS text::validation :: generic normalize empty input returns Invalid",
            "PASS text::validation :: generic normalize trims nonempty input",
            "PASS text::validation :: Text trim removes surrounding Unicode whitespace",
            "PASS text::validation :: require preserves a present Option<Text>",
            "PASS text::validation :: require maps a missing Option<Text> to its error",
            "PASS text::validation :: require preserves a TooLong maximum as u32",
            "PASS text::validation :: require preserves a present Option<i32>",
            "PASS text::validation :: require maps a missing Option<i32> to its error",
            "8 passed, 0 failed"
        ]) + Environment.NewLine;

        AssertEqual(0, result.ExitCode, Describe(result));
        AssertEqual(expected, result.StandardOutput, Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
    }

    private static async Task TestManagedLanguageTestOutcomes(Harness harness)
    {
        var failureSource = """
            module app::tests;
            test "first assertion fails" {
                assert false;
            }
            test "later test runs" {
                assert true;
            }
            """;
        var failurePackage = await harness.WritePackageAsync(
            "language-tests-failure",
            LibraryPackageManifest("language-tests-failure"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/tests.hob"] = failureSource
            });
        var failureSourcePath = Path.Combine(failurePackage, "src", "app", "tests.hob");
        var failure = await harness.InvokePackageDirectoryAsync(
            "language-tests-failure-run", failurePackage, "test");
        var failureDisplayPath = failureSourcePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        var expectedFailure = string.Join(Environment.NewLine,
        [
            $"FAIL app::tests :: first assertion fails ({failureDisplayPath}:2:1)",
            "PASS app::tests :: later test runs",
            "1 passed, 1 failed"
        ]) + Environment.NewLine;
        AssertEqual(1, failure.ExitCode, Describe(failure));
        AssertEqual(expectedFailure, failure.StandardOutput, Describe(failure));
        AssertEqual(string.Empty, failure.StandardError, Describe(failure));

        var escapedNameSource = """
            module app::escaped;
            test "attempt\nPASS injected\n0 passed, 99 failed\t\\tail" {
                assert false;
            }
            test "after escaped name" {
                assert true;
            }
            """;
        var escapedNamePackage = await harness.WritePackageAsync(
            "language-tests-escaped-name",
            LibraryPackageManifest("language-tests-escaped-name"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/escaped.hob"] = escapedNameSource
            });
        var escapedNameSourcePath = Path.Combine(escapedNamePackage, "src", "app", "escaped.hob");
        var escapedName = await harness.InvokePackageDirectoryAsync(
            "language-tests-escaped-name-run", escapedNamePackage, "test");
        var escapedNameDisplayPath = escapedNameSourcePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        var expectedEscapedName = string.Join(Environment.NewLine,
        [
            $"FAIL app::escaped :: attempt\\nPASS injected\\n0 passed, 99 failed\\t\\\\tail ({escapedNameDisplayPath}:2:1)",
            "PASS app::escaped :: after escaped name",
            "1 passed, 1 failed"
        ]) + Environment.NewLine;
        AssertEqual(1, escapedName.ExitCode, Describe(escapedName));
        AssertEqual(expectedEscapedName, escapedName.StandardOutput, Describe(escapedName));
        AssertEqual(3,
            escapedName.StandardOutput.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries).Length,
            "Escaped test-name controls must remain visible escapes on a single physical result line.");

        var emptyPackage = await harness.WritePackageAsync(
            "language-tests-empty",
            LibraryPackageManifest("language-tests-empty"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/empty/suite.hob"] = "module empty::suite;\n"
            });
        var empty = await harness.InvokePackageDirectoryAsync(
            "language-tests-empty-run", emptyPackage, "test");
        AssertEqual(0, empty.ExitCode, Describe(empty));
        AssertEqual("0 passed, 0 failed" + Environment.NewLine, empty.StandardOutput, Describe(empty));

        var multiModulePackage = await harness.WritePackageAsync(
            "language-tests-multiple-modules",
            LibraryPackageManifest("language-tests-multiple-modules"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/suite/alpha.hob"] = """
                    module suite::alpha;
                    struct Names { test: bool, assert: bool }
                    fn test(assert: bool) -> bool effects {} {
                        let names: self::suite::alpha::Names = self::suite::alpha::Names { test: assert, assert: true };
                        return names.test == names.assert;
                    }
                    test "same name" {
                        let result: bool = self::suite::alpha::test(false);
                        assert result == false;
                    }
                    """,
                ["src/suite/beta.hob"] = """
                    module suite::beta;
                    test "same name" {
                        assert true;
                    }
                    """
            });
        var multiModule = await harness.InvokePackageDirectoryAsync(
            "language-tests-multiple-modules-run", multiModulePackage, "test");
        var expectedMultiModule = string.Join(Environment.NewLine,
        [
            "PASS suite::alpha :: same name",
            "PASS suite::beta :: same name",
            "2 passed, 0 failed"
        ]) + Environment.NewLine;
        AssertEqual(0, multiModule.ExitCode, Describe(multiModule));
        AssertEqual(expectedMultiModule, multiModule.StandardOutput, Describe(multiModule));
    }

    private static async Task TestManagedLanguageTestPackageRules(Harness harness)
    {
        const string invalidAssertions = """
            module validation::invalid_tests;
            test "duplicate assertion name" {
                assert 1;
            }
            test "duplicate assertion name" {
                assert true;
            }
            """;
        var invalidPackage = await harness.WritePackageAsync(
            "language-tests-invalid-assertions",
            LibraryPackageManifest("language-tests-invalid-assertions"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/validation/invalid_tests.hob"] = invalidAssertions
            });
        var invalid = await harness.InvokePackageDirectoryAsync(
            "language-tests-invalid-assertions-check", invalidPackage, "check", "--json");
        AssertEqual(1, invalid.ExitCode, Describe(invalid));
        var invalidCodes = ParseDiagnosticSnapshots(invalid.StandardOutput)
            .Select(diagnostic => diagnostic.Code).ToArray();
        AssertTrue(invalidCodes.Contains("E_TYPE_MISMATCH", StringComparer.Ordinal),
            $"A non-bool assertion should fail typechecking. {Describe(invalid)}");
        AssertTrue(invalidCodes.Contains("E_NAME_DUPLICATE", StringComparer.Ordinal),
            $"A repeated test name in one module should fail. {Describe(invalid)}");

        const string rootManifest = """
            name = "language-test-root"
            version = "0.1.0"
            kind = "lib"
            source_root = "src"

            [dependencies]
            child = "../child"
            """;
        const string rootTest = """
            module root::tests;
            test "root test only" { assert true; }
            """;
        const string dependencyTest = """
            module child::tests;
            test "dependency test is typechecked but not run" { assert false; }
            """;
        var rootPackage = await harness.WritePackageGraphAsync(
            "language-tests-dependency-rules",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(rootManifest, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/root/tests.hob"] = rootTest
                }),
                ["child"] = new PackageFixture(LibraryPackageManifest("language-test-child"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/child/tests.hob"] = dependencyTest
                    })
            });
        var missingLock = await harness.InvokePackageDirectoryAsync(
            "language-tests-missing-lock", rootPackage, "test");
        AssertEqual(1, missingLock.ExitCode, Describe(missingLock));
        AssertTrue(missingLock.StandardError.Contains("E_LOCK", StringComparison.Ordinal), Describe(missingLock));

        var createLock = await harness.InvokePackageDirectoryAsync(
            "language-tests-create-lock", rootPackage, "lock");
        AssertEqual(0, createLock.ExitCode, Describe(createLock));
        var rootOnly = await harness.InvokePackageDirectoryAsync(
            "language-tests-root-only", rootPackage, "test");
        AssertEqual(0, rootOnly.ExitCode, Describe(rootOnly));
        AssertEqual("PASS root::tests :: root test only" + Environment.NewLine + "1 passed, 0 failed" + Environment.NewLine,
            rootOnly.StandardOutput, Describe(rootOnly));

        var dependencySourcePath = Path.Combine(Path.GetDirectoryName(rootPackage)!, "child", "src", "child", "tests.hob");
        await File.WriteAllTextAsync(dependencySourcePath, """
            module child::tests;
            test "dependency assertion must typecheck" { assert 1; }
            """);
        var staleLock = await harness.InvokePackageDirectoryAsync(
            "language-tests-stale-lock", rootPackage, "test");
        AssertEqual(1, staleLock.ExitCode, Describe(staleLock));
        AssertTrue(staleLock.StandardError.Contains("E_LOCK", StringComparison.Ordinal), Describe(staleLock));

        var refreshLock = await harness.InvokePackageDirectoryAsync(
            "language-tests-refresh-lock", rootPackage, "lock");
        AssertEqual(0, refreshLock.ExitCode, Describe(refreshLock));
        var dependencyTypecheck = await harness.InvokePackageDirectoryAsync(
            "language-tests-dependency-typecheck", rootPackage, "test");
        AssertEqual(1, dependencyTypecheck.ExitCode, Describe(dependencyTypecheck));
        AssertTrue(dependencyTypecheck.StandardError.Contains("E_TYPE_MISMATCH", StringComparison.Ordinal),
            Describe(dependencyTypecheck));
        AssertTrue(dependencyTypecheck.StandardError.Contains(dependencySourcePath, StringComparison.Ordinal),
            Describe(dependencyTypecheck));
        AssertEqual(string.Empty, dependencyTypecheck.StandardOutput, Describe(dependencyTypecheck));
    }

    private static async Task TestSpecificationDriftOracle(Harness harness)
    {
        var manifestPath = Path.Combine(harness.RepositoryRoot, "fixtures", "manifest.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var fixtures = manifest.RootElement.EnumerateArray().ToArray();
        var activeCount = fixtures.Count(item => item.GetProperty("status").GetString() == "active");
        var pendingCount = fixtures.Count(item => item.GetProperty("status").GetString() == "pending");
        AssertEqual(126, activeCount, $"Unexpected active fixture count in {manifestPath}.");
        AssertEqual(0, pendingCount, $"Unexpected pending fixture count in {manifestPath}.");
        AssertTrue(fixtures.All(item => item.GetProperty("status").GetString() is "active" or "pending"),
            $"Fixture manifest contains an unknown status: {manifestPath}.");

        var fixtureRun = await harness.InvokeCompilerCommandAsync("test");
        AssertEqual(0, fixtureRun.ExitCode, Describe(fixtureRun));
        AssertTrue(fixtureRun.StandardOutput.StartsWith("PASS 01-valid-constant.hob ", StringComparison.Ordinal),
            Describe(fixtureRun));
        AssertTrue(fixtureRun.StandardOutput.EndsWith("126 active, 0 pending, 0 failed" + Environment.NewLine, StringComparison.Ordinal),
            Describe(fixtureRun));
        AssertEqual(string.Empty, fixtureRun.StandardError, Describe(fixtureRun));

        var emittedCodes = new HashSet<string>(StringComparer.Ordinal);
        var resourceEscape = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "fixtures", "16-resource-escape.hob"));
        foreach (var diagnostic in await ExpectDiagnosticsAsync(
                     harness, "spec-drift-resource-escape", resourceEscape, "E_RESOURCE_ESCAPE"))
            emittedCodes.Add(diagnostic.Code);

        string MakeReadSource(string sql, string resultType = "Result<Option<self::app::spec::Row>, DbError>", string rowFields = "id: i32") => $$"""
            module app::spec;
            struct Parameters { id: i32 }
            struct Row { {{rowFields}} }
            fn probe(db: DbRead, sql: Text) -> bool effects { db.read } {
                let loaded: {{resultType}} = db.query_one({{sql}}, self::app::spec::Parameters { id: 1 });
                return true;
            }
            """;

        var dbDiagnosticPrograms = new (string Code, string Source)[]
        {
            ("E_DB_SQL_LITERAL", MakeReadSource("sql")),
            ("E_DB_READ_STATEMENT", MakeReadSource(JsonSerializer.Serialize("UPDATE records SET id = 1"))),
            ("E_DB_RESULT_TYPE", MakeReadSource(JsonSerializer.Serialize("SELECT id FROM records"), "i32")),
            ("E_DB_CODEC_UNSUPPORTED", MakeReadSource(
                JsonSerializer.Serialize("SELECT path FROM records"),
                rowFields: "path: FilePath")),
            ("E_DB_PARAMETERS", """
                module app::spec;
                fn probe(db: DbWrite) -> bool effects { db.write } {
                    let written: Result<i32, DbError> = db.execute("INSERT INTO records (id) VALUES (1)");
                    return true;
                }
                """),
            ("E_DB_TRANSACTION_STATEMENT", """
                module app::spec;
                struct Parameters { id: i32 }
                fn probe(db: DbWrite) -> bool effects { db.write } {
                    with db.begin() as tx {
                        let written: Result<i32, DbError> = tx.execute("COMMIT", self::app::spec::Parameters { id: 1 });
                        return true;
                    }
                }
                """)
        };
        foreach (var (code, source) in dbDiagnosticPrograms)
        {
            foreach (var diagnostic in await ExpectDiagnosticsAsync(harness, $"spec-drift-{code}", source, code))
                emittedCodes.Add(diagnostic.Code);
        }

        var diagnosticDocumentation = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "docs", "diagnostics.md"));
        var documentedDiagnosticStatuses = ParseDiagnosticTableStatuses(diagnosticDocumentation);
        var sourceDiagnosticCodes = new HashSet<string>(StringComparer.Ordinal);
        var sourceDirectory = Path.Combine(harness.RepositoryRoot, "src", "Hob");
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var source = await File.ReadAllTextAsync(sourcePath);
            foreach (Match match in Regex.Matches(source, "\"(?<code>E_[A-Z0-9_]+)\""))
                sourceDiagnosticCodes.Add(match.Groups["code"].Value);
        }
        AssertTrue(sourceDiagnosticCodes.IsSupersetOf(emittedCodes),
            "A diagnostic emitted by a compiler witness has no quoted implementation literal.");

        var implementedDocumentationCodes = documentedDiagnosticStatuses
            .Where(entry => entry.Value.StartsWith("Implemented", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        var undocumentedCodes = sourceDiagnosticCodes.Except(implementedDocumentationCodes, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var unsupportedImplementedRows = implementedDocumentationCodes.Except(sourceDiagnosticCodes, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        AssertTrue(undocumentedCodes.Length == 0 && unsupportedImplementedRows.Length == 0,
            "docs/diagnostics.md must mark exactly the quoted compiler E_* literals as Implemented. "
            + $"Missing implemented rows: [{string.Join(", ", undocumentedCodes)}]; "
            + $"implemented rows without a source literal: [{string.Join(", ", unsupportedImplementedRows)}].");

        var grammar = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "docs", "grammar.md"));
        var formalGrammar = ExtractFencedBlock(grammar, "ebnf");
        var declarationProduction = ExtractEbnfProduction(formalGrammar, "declaration");
        AssertTrue(Regex.IsMatch(declarationProduction, @"\bcommand_decl\b"),
            "docs/grammar.md root EBNF declaration production must include command_decl.");
        var statementProduction = ExtractEbnfProduction(formalGrammar, "statement");
        AssertTrue(Regex.IsMatch(statementProduction, @"\bwith_transaction_statement\b"),
            "docs/grammar.md root EBNF statement production must include with_transaction_statement.");

        var cliGrammar = ExtractMarkdownSection(grammar, "## Typed CLI command declarations (PR1)");
        foreach (var production in new[] { "command_declaration ::=", "command_item ::=", "command_type ::= " })
            AssertTrue(cliGrammar.Contains(production, StringComparison.Ordinal),
                $"docs/grammar.md must document the typed command production '{production}'.");

        var routeSection = ExtractMarkdownSection(grammar, "## Checked route declarations");
        var hasTransactionParagraph = routeSection.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries)
            .Any(paragraph => paragraph.Contains("with db.begin() as tx", StringComparison.Ordinal)
                && paragraph.Contains("E_RESOURCE_ESCAPE", StringComparison.Ordinal)
                && paragraph.Contains("E_DB_TRANSACTION_STATEMENT", StringComparison.Ordinal));
        AssertTrue(hasTransactionParagraph,
            "docs/grammar.md must describe the scoped `with db.begin() as tx` form and its current rejection codes.");

        var usage = await harness.InvokeCompilerCommandAsync();
        AssertEqual(2, usage.ExitCode, Describe(usage));
        var usageLine = usage.StandardError.TrimEnd('\r', '\n');
        const string usagePrefix = "Usage: hob ";
        AssertTrue(usageLine.StartsWith(usagePrefix, StringComparison.Ordinal)
            && !usageLine.Contains('\n') && !usageLine.Contains('\r'), Describe(usage));
        var currentForms = new HashSet<string>(StringComparer.Ordinal)
        {
            "new lib|cli|web NAME",
            "add SOURCE",
            "add PACKAGE_DIRECTORY SOURCE",
            "fmt FILE_OR_PACKAGE [--check]",
            "check FILE_OR_PACKAGE [--json]",
            "build FILE_OR_PACKAGE [--aot --rid RID]",
            "run FILE_OR_PACKAGE [-- APP_ARGS]",
            "lock PACKAGE_DIRECTORY",
            "config example PACKAGE_DIRECTORY",
            "audit PACKAGE_DIRECTORY --json",
            "inspect PACKAGE_DIRECTORY --json",
            "inspect effects PACKAGE_DIRECTORY SYMBOL --json",
            "inspect api PACKAGE_DIRECTORY --json",
            "test [FILE_OR_PACKAGE]"
        };
        var reportedForms = usageLine[usagePrefix.Length..].Split(" | hob ", StringSplitOptions.None);
        AssertTrue(reportedForms.Length == currentForms.Count && currentForms.SetEquals(reportedForms),
            "The compiler usage command forms drifted from the expected surface. "
            + $"Expected [{string.Join(" | ", currentForms.Order(StringComparer.Ordinal))}], "
            + $"received [{string.Join(" | ", reportedForms)}].");

        static string CommandName(string form) => form.StartsWith("inspect effects ", StringComparison.Ordinal)
            ? "inspect effects"
            : form.StartsWith("inspect api ", StringComparison.Ordinal)
                ? "inspect api"
                : form.StartsWith("inspect ", StringComparison.Ordinal)
                    ? "inspect"
                : form.StartsWith("audit ", StringComparison.Ordinal)
                    ? "audit"
            : form.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        var currentCommandNames = currentForms.Select(CommandName).ToHashSet(StringComparer.Ordinal);

        var readme = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "README.md"));
        foreach (var command in currentCommandNames)
            AssertTrue(readme.Contains("hob " + command, StringComparison.Ordinal),
                $"README.md must document the current '{command}' command form.");
        foreach (var command in currentCommandNames)
            AssertTrue(grammar.Contains("hob " + command, StringComparison.Ordinal),
                $"docs/grammar.md must document the current '{command}' command form.");

        var packageCommandBlock = ExtractFencedBlockAfter(grammar,
            "Package commands use a package directory rather than a source-file path:", "text");
        var packageCommands = packageCommandBlock.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("hob ", StringComparison.Ordinal))
            .Select(line => CommandName(line["hob ".Length..]))
            .ToHashSet(StringComparer.Ordinal);
        var packageCommandNames = currentCommandNames.Except(["new", "add"], StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        AssertTrue(packageCommandNames.SetEquals(packageCommands),
            "docs/grammar.md package command list must cover exactly the compiler's current commands. "
            + $"Expected [{string.Join(", ", packageCommandNames.Order(StringComparer.Ordinal))}], "
            + $"received [{string.Join(", ", packageCommands.Order(StringComparer.Ordinal))}].");
        AssertTrue(packageCommandBlock.Contains("hob inspect effects PACKAGE_DIRECTORY SYMBOL --json", StringComparison.Ordinal),
            "docs/grammar.md package command list must show the inspect effects form.");
        AssertTrue(packageCommandBlock.Contains("hob inspect PACKAGE_DIRECTORY --json", StringComparison.Ordinal),
            "docs/grammar.md package command list must show the full inspect graph form.");
        AssertTrue(packageCommandBlock.Contains("hob inspect api PACKAGE_DIRECTORY --json", StringComparison.Ordinal),
            "docs/grammar.md package command list must show the inspect api form.");
        AssertTrue(packageCommandBlock.Contains("hob audit PACKAGE_DIRECTORY --json", StringComparison.Ordinal),
            "docs/grammar.md package command list must show the audit form.");
        AssertTrue(grammar.Contains("hob new lib|cli|web NAME", StringComparison.Ordinal),
            "docs/grammar.md must document package creation.");
        AssertTrue(grammar.Contains("hob add SOURCE", StringComparison.Ordinal),
            "docs/grammar.md must document adding a dependency from the current package directory.");

        foreach (var unsupportedCommand in new[] { "unknown-command" })
        {
            var unsupported = await harness.InvokeCompilerCommandAsync(unsupportedCommand);
            AssertEqual(2, unsupported.ExitCode, Describe(unsupported));
            AssertEqual(usage.StandardError, unsupported.StandardError, Describe(unsupported));
            AssertEqual(string.Empty, unsupported.StandardOutput, Describe(unsupported));
        }

        var roadmap = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "docs", "roadmap.md"));
        AssertTrue(Regex.IsMatch(roadmap, @"\b126\s+active\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(roadmap, @"\b0\s+pending\b", RegexOptions.IgnoreCase),
            "docs/roadmap.md must state that all 126 fixtures are active and none are pending.");
    }

    private static Dictionary<string, string> ParseDiagnosticTableStatuses(string markdown)
    {
        var lines = markdown.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var headerIndex = Array.FindIndex(lines, line => line.TrimStart().StartsWith("| Code |", StringComparison.Ordinal));
        AssertTrue(headerIndex >= 0, "docs/diagnostics.md is missing its code/status table.");

        var statuses = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = headerIndex + 1; index < lines.Length && lines[index].TrimStart().StartsWith('|'); index++)
        {
            var cells = lines[index].Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 4 || !cells[1].StartsWith("E_", StringComparison.Ordinal))
                continue;
            AssertTrue(statuses.TryAdd(cells[1], cells[^2]),
                $"docs/diagnostics.md contains duplicate table rows for {cells[1]}.");
        }
        return statuses;
    }

    private static string ExtractFencedBlock(string markdown, string language)
    {
        var match = Regex.Match(markdown,
            $@"(?ms)^```{Regex.Escape(language)}\r?\n(?<body>.*?)^```\s*$");
        AssertTrue(match.Success, $"Markdown fenced block not found: {language}");
        return match.Groups["body"].Value;
    }

    private static string ExtractFencedBlockAfter(string markdown, string anchor, string language)
    {
        var anchorIndex = markdown.IndexOf(anchor, StringComparison.Ordinal);
        AssertTrue(anchorIndex >= 0, $"Markdown anchor not found: {anchor}");
        var opening = markdown.IndexOf("```" + language, anchorIndex, StringComparison.Ordinal);
        AssertTrue(opening >= 0, $"Markdown {language} fenced block not found after: {anchor}");
        var contentStart = markdown.IndexOf('\n', opening + 3 + language.Length);
        AssertTrue(contentStart >= 0, $"Markdown fenced block has no body after: {anchor}");
        var closing = markdown.IndexOf("```", contentStart + 1, StringComparison.Ordinal);
        AssertTrue(closing >= 0, $"Markdown fenced block is not closed after: {anchor}");
        return markdown[(contentStart + 1)..closing].TrimEnd('\r');
    }

    private static string ExtractEbnfProduction(string ebnf, string name)
    {
        var lines = ebnf.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var start = Array.FindIndex(lines, line => Regex.IsMatch(line,
            $@"^\s*{Regex.Escape(name)}\s*="));
        AssertTrue(start >= 0, $"Root EBNF production not found: {name}");
        var end = start + 1;
        for (; end < lines.Length; end++)
        {
            if (Regex.IsMatch(lines[end], @"^\s*[A-Za-z_][A-Za-z0-9_]*\s*="))
                break;
        }
        return string.Join(Environment.NewLine, lines[start..end]);
    }

    private static string ExtractMarkdownSection(string markdown, string heading)
    {
        var start = markdown.IndexOf(heading, StringComparison.Ordinal);
        AssertTrue(start >= 0, $"Markdown section not found: {heading}");
        var nextHeading = markdown.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return nextHeading < 0 ? markdown[start..] : markdown[start..nextHeading];
    }

    private static async Task TestStaticRouteDeclarations(Harness harness)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            harness.RepositoryRoot, "fixtures", "18-route-mapping.hob"));
        var check = await harness.InvokeAsync("static-route-check", "check", source, "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length,
            "Valid static GET and POST routes should typecheck cleanly.");

        var compilerAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.Location, Path.GetFullPath(Path.Combine(
                    harness.RepositoryRoot, "src", "Hob", "bin", "Release", "net10.0", "hob.dll")),
                    StringComparison.OrdinalIgnoreCase))
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(
                harness.RepositoryRoot, "src", "Hob", "bin", "Release", "net10.0", "hob.dll")));
        var compilerType = compilerAssembly.GetType("Compiler", throwOnError: true)!;
        var checkMethod = compilerType.GetMethod("Check", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Compiler.Check(string, string) was not found.");
        var checkResult = checkMethod.Invoke(null, [harness.LastSourcePath, source])
            ?? throw new InvalidOperationException("Compiler.Check returned no result.");
        var checkedProgram = checkResult.GetType().GetProperty("Program")?.GetValue(checkResult)
            ?? throw new InvalidOperationException("Compiler.Check did not produce a checked route program.");
        var routeValue = checkedProgram.GetType().GetProperty("Routes")?.GetValue(checkedProgram)
            ?? throw new InvalidOperationException("CheckedProgram does not expose route IR.");
        var routes = ((System.Collections.IEnumerable)routeValue).Cast<object>().ToArray();
        AssertEqual(2, routes.Length, "The checked IR should preserve both source routes.");

        var get = routes[0];
        var post = routes[1];
        AssertEqual("GET", get.GetType().GetProperty("Method")?.GetValue(get)?.ToString(),
            "The first route should retain its HTTP method.");
        AssertEqual("/items", get.GetType().GetProperty("Path")?.GetValue(get)?.ToString(),
            "The first route should retain its static path.");
        var getResponses = ((System.Collections.IEnumerable)get.GetType().GetProperty("Responses")!.GetValue(get)!)
            .Cast<object>().ToArray();
        AssertEqual(2, getResponses.Length, "The checked GET route should retain both response mappings.");
        AssertEqual(200, (int)getResponses[0].GetType().GetProperty("StatusCode")!.GetValue(getResponses[0])!,
            "The checked JSON response should retain its status.");
        AssertEqual("Json", getResponses[0].GetType().GetProperty("ContentKind")?.GetValue(getResponses[0])?.ToString(),
            "The checked response should retain the JSON content kind.");

        AssertEqual("POST", post.GetType().GetProperty("Method")?.GetValue(post)?.ToString(),
            "The second route should retain its HTTP method.");
        AssertTrue(post.GetType().GetProperty("BodyType")?.GetValue(post) is not null,
            "The checked POST route should retain its body type.");
        AssertEqual(1, ((System.Collections.IEnumerable)post.GetType().GetProperty("BodySchema")!.GetValue(post)!)
            .Cast<object>().Count(), "The checked POST route should retain its body schema.");
    }

    private static async Task TestRouteContractDiagnostics(Harness harness)
    {
        const string header = """
            module harness::route_contract;
            union Reply { Found(Text), Empty }
            fn good() -> self::harness::route_contract::Reply effects {} {
                return self::harness::route_contract::Reply.Found("ok");
            }
            fn scalar() -> i32 effects {} { return 1; }
            fn takes_value(value: i32) -> self::harness::route_contract::Reply effects {} {
                return self::harness::route_contract::Reply.Found("ok");
            }
            """;
        static string Add(string header, string tail) => header + "\n" + tail;
        var cases = new (string Name, string Source, string Code)[]
        {
            ("route-invalid-method", Add(header, "route PUT \"/items\" { handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-invalid-path", Add(header, "route GET \"/items/{id}\" { handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_BINDING"),
            ("route-unknown-item", Add(header, "route GET \"/items\" { unknown; }"), "E_ROUTE_DECL"),
            ("route-invalid-status", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 600 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-get-body", Add(header + "\nstruct Body { value: i32 }", "route GET \"/items\" { body: self::harness::route_contract::Body; handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-post-missing-body", Add(header, "route POST \"/items\" { handler: self::harness::route_contract::takes_value; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-generic-post-body", Add(header + "\nstruct GenericBody<T> { value: T }", "route POST \"/items\" { body: self::harness::route_contract::GenericBody<i32>; handler: self::harness::route_contract::takes_value; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-duplicate-body", Add(header + "\nstruct Body { value: i32 }\nfn post(body: self::harness::route_contract::Body) -> self::harness::route_contract::Reply effects {} { return self::harness::route_contract::Reply.Empty; }", "route POST \"/items\" { body: self::harness::route_contract::Body; body: self::harness::route_contract::Body; handler: self::harness::route_contract::post; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-missing-handler", Add(header, "route GET \"/items\" { response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_HANDLER"),
            ("route-duplicate-handler", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_HANDLER"),
            ("route-handler-arity", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::takes_value; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_HANDLER"),
            ("route-handler-return", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::scalar; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_HANDLER"),
            ("route-handler-unresolved", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::missing; response Found: 200 json Text; response Empty: 204; }"), "E_NAME_UNRESOLVED"),
            ("route-unknown-response-variant", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Other: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-duplicate-response", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 200 json Text; response Found: 201 json Text; response Empty: 204; }"), "E_ROUTE_RESPONSE_DUPLICATE"),
            ("route-response-payload-mismatch", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 200 json i32; response Empty: 204; }"), "E_ROUTE_CODEC_UNSUPPORTED"),
            ("route-response-html-content-mismatch", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 200 html; response Empty: 204; }"), "E_ROUTE_CODEC_UNSUPPORTED"),
            ("route-no-content-status-payload", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 205 json Text; response Empty: 204; }"), "E_ROUTE_CODEC_UNSUPPORTED"),
            ("route-with-main", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; }\npub fn main() -> i32 effects {} { return 0; }"), "E_ROUTE_DECL"),
            ("route-with-command", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; }\ncommand route {}"), "E_ROUTE_DECL"),
            ("route-duplicate-method-path", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; } route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-duplicate-case-insensitive-method-path", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; } route GET \"/ITEMS\" { handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL")
        };

        foreach (var (name, source, code) in cases)
            await ExpectDiagnosticsAsync(harness, name, source, code);

        var routeOnlyPackage = CheckRoutePackageWithWebContext(harness, new[]
        {
            ("app::main", """
                module app::main;
                union Reply { Found(Text), Empty }
                fn get() -> self::app::main::Reply effects {} {
                    return self::app::main::Reply.Found("ok");
                }
                route GET "/items" {
                    handler: self::app::main::get;
                    response Found: 200 json Text;
                    response Empty: 204;
                }
                """)
        });
        AssertEqual(0, routeOnlyPackage.DiagnosticCodes.Length,
            $"A route-only web package should check successfully. Got [{string.Join(", ", routeOnlyPackage.DiagnosticCodes)}].");
        var checkedRouteOnlyPackage = routeOnlyPackage.Program
            ?? throw new InvalidOperationException("A valid route-only web package should produce a CheckedProgram.");
        AssertEqual("app::main", checkedRouteOnlyPackage.GetType().GetProperty("EntryModule")?.GetValue(checkedRouteOnlyPackage)?.ToString(),
            "A route-only web package should retain its entry module.");
        AssertEqual(1, ((System.Collections.IEnumerable)checkedRouteOnlyPackage.GetType().GetProperty("Routes")!.GetValue(checkedRouteOnlyPackage)!)
            .Cast<object>().Count(), "A route-only web package should retain its route IR.");
        AssertTrue(checkedRouteOnlyPackage.GetType().GetProperty("EntryFunctionId")!.GetValue(checkedRouteOnlyPackage) is null,
            "A route-only web package should not select a function entry.");
        AssertTrue(checkedRouteOnlyPackage.GetType().GetProperty("EntryCommandId")!.GetValue(checkedRouteOnlyPackage) is null,
            "A route-only web package should not select a command entry.");

        var webEntryWithoutRoutes = CheckRoutePackageWithWebContext(harness, new[]
        {
            ("app::main", "module app::main;")
        });
        AssertTrue(webEntryWithoutRoutes.DiagnosticCodes.Contains("E_ENTRYPOINT", StringComparer.Ordinal),
            "A web entry without routes must report E_ENTRYPOINT.");

        const string routeEntrySource = """
            module app::main;
            union Reply { Found(Text), Empty }
            fn get() -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Found("ok");
            }
            route GET "/items" {
                handler: self::app::main::get;
                response Found: 200 json Text;
                response Empty: 204;
            }
            """;
        var webRouteAndMain = CheckRoutePackageWithWebContext(harness, new[]
        {
            ("app::main", routeEntrySource + "\npub fn main() -> i32 effects {} { return 0; }\n")
        });
        AssertTrue(webRouteAndMain.DiagnosticCodes.Contains("E_ROUTE_DECL", StringComparer.Ordinal),
            "A web route entry cannot also declare a runnable main function.");
        AssertTrue(!webRouteAndMain.DiagnosticCodes.Contains("E_ENTRYPOINT", StringComparer.Ordinal),
            "A valid route declaration must prevent an E_ENTRYPOINT cascade when main is rejected.");

        var webRouteAndCommand = CheckRoutePackageWithWebContext(harness, new[]
        {
            ("app::main", routeEntrySource + "\ncommand route {}\n")
        });
        AssertTrue(webRouteAndCommand.DiagnosticCodes.Contains("E_ROUTE_DECL", StringComparer.Ordinal),
            "A web route entry cannot also declare a command.");
        AssertTrue(!webRouteAndCommand.DiagnosticCodes.Contains("E_ENTRYPOINT", StringComparer.Ordinal),
            "A valid route declaration must prevent an E_ENTRYPOINT cascade when a command is rejected.");

        var invalidWebRoute = CheckRoutePackageWithWebContext(harness, new[]
        {
            ("app::main", """
                module app::main;
                union Reply { Found(Text), Empty }
                fn get() -> self::app::main::Reply effects {} {
                    return self::app::main::Reply.Found("ok");
                }
                route GET "/items" {
                    handler: self::app::main::get;
                    response Found: 600 json Text;
                    response Empty: 204;
                }
                """)
        });
        AssertTrue(invalidWebRoute.DiagnosticCodes.Contains("E_ROUTE_DECL", StringComparer.Ordinal),
            "An invalid declared web route must preserve its specific route diagnostic.");
        AssertTrue(!invalidWebRoute.DiagnosticCodes.Contains("E_ENTRYPOINT", StringComparer.Ordinal),
            "An invalid declared web route must not add an E_ENTRYPOINT cascade.");

        var privateHandlerResult = CheckRoutePackageWithWebContext(harness, new[]
        {
            ("app::main", """
                module app::main;
                pub union Reply { Found(Text), Empty }
                route GET "/private" {
                    handler: self::app::handlers::get;
                    response Found: 200 json Text;
                    response Empty: 404;
                }
                """),
            ("app::handlers", """
                module app::handlers;
                fn get() -> self::app::main::Reply effects {} {
                    return self::app::main::Reply.Found("ok");
                }
                """)
        });
        AssertTrue(privateHandlerResult.DiagnosticCodes.Contains("E_ACCESS_PRIVATE", StringComparer.Ordinal),
            $"A route handler in another module must obey private declaration visibility. Got [{string.Join(", ", privateHandlerResult.DiagnosticCodes)}].");

        var nonEntryRouteResult = CheckRoutePackageWithWebContext(harness, new[]
        {
            ("app::main", """
                module app::main;
                pub fn main() -> i32 effects {} { return 0; }
                """),
            ("app::other", """
                module app::other;
                union Reply { Found(Text), Empty }
                fn good() -> self::app::other::Reply effects {} {
                    return self::app::other::Reply.Found("ok");
                }
                route GET "/other" {
                    handler: self::app::other::good;
                    response Found: 200 json Text;
                    response Empty: 404;
                }
                """)
        });
        AssertTrue(nonEntryRouteResult.DiagnosticCodes.Contains("E_ROUTE_DECL", StringComparer.Ordinal),
            "A route in a non-entry module must be rejected even when the package has a route-capable root entry.");
        var unsupportedJson = """
            module harness::route_unsupported_json;
            struct Envelope { value: Option<Text> }
            union Reply { Value(self::harness::route_unsupported_json::Envelope), Empty }
            fn good() -> self::harness::route_unsupported_json::Reply effects {} {
                return self::harness::route_unsupported_json::Reply.Empty;
            }
            route GET "/items" {
                handler: self::harness::route_unsupported_json::good;
                response Value: 200 json self::harness::route_unsupported_json::Envelope;
                response Empty: 204;
            }
            """;
        await ExpectDiagnosticsAsync(harness, "route-unsupported-json-shape", unsupportedJson, "E_ROUTE_CODEC_UNSUPPORTED");

        const string genericJson = """
            module harness::route_generic_json;
            struct Envelope<T> { value: T }
            union Reply { Value(self::harness::route_generic_json::Envelope<i32>), Empty }
            fn good() -> self::harness::route_generic_json::Reply effects {} {
                return self::harness::route_generic_json::Reply.Empty;
            }
            route GET "/items" {
                handler: self::harness::route_generic_json::good;
                response Value: 200 json self::harness::route_generic_json::Envelope<i32>;
                response Empty: 204;
            }
            """;
        await ExpectDiagnosticsAsync(harness, "route-generic-json-shape", genericJson, "E_ROUTE_CODEC_UNSUPPORTED");

        var recursiveJson = """
            module harness::route_recursive_json;
            struct Node { child: Option<self::harness::route_recursive_json::Node> }
            union Reply { Value(self::harness::route_recursive_json::Node), Empty }
            fn good() -> self::harness::route_recursive_json::Reply effects {} {
                return self::harness::route_recursive_json::Reply.Empty;
            }
            route GET "/items" {
                handler: self::harness::route_recursive_json::good;
                response Value: 200 json self::harness::route_recursive_json::Node;
                response Empty: 204;
            }
            """;
        await ExpectDiagnosticsAsync(harness, "route-recursive-json-shape", recursiveJson, "E_ROUTE_CODEC_UNSUPPORTED");

        var unsupportedRequestBody = """
            module harness::route_unsupported_request_body;
            union Choice { Selected(Text), Empty }
            struct Request {
                optional: Option<Text>,
                choice: self::harness::route_unsupported_request_body::Choice
            }
            union Reply { Accepted, Empty }
            route POST "/items" {
                body: self::harness::route_unsupported_request_body::Request;
                handler: self::harness::route_unsupported_request_body::post;
                response Accepted: 200;
                response Empty: 204;
            }
            fn post(request: self::harness::route_unsupported_request_body::Request) -> self::harness::route_unsupported_request_body::Reply effects {} {
                return self::harness::route_unsupported_request_body::Reply.Accepted;
            }
            """;
        var unsupportedRequestBodyDiagnostics = await ExpectDiagnosticsAsync(
            harness, "route-unsupported-request-body", unsupportedRequestBody, "E_ROUTE_CODEC_UNSUPPORTED");
        AssertRangeAtToken(
            unsupportedRequestBody,
            unsupportedRequestBodyDiagnostics.Single(diagnostic => diagnostic.Code == "E_ROUTE_CODEC_UNSUPPORTED"),
            "self",
            2);

        var recursiveRequestBody = """
            module harness::route_recursive_request_body;
            struct Node { next: Option<self::harness::route_recursive_request_body::Node> }
            struct Wrapper { node: self::harness::route_recursive_request_body::Node }
            union Reply { Accepted, Empty }
            route POST "/items" {
                body: self::harness::route_recursive_request_body::Wrapper;
                handler: self::harness::route_recursive_request_body::post;
                response Accepted: 200;
                response Empty: 204;
            }
            fn post(request: self::harness::route_recursive_request_body::Wrapper) -> self::harness::route_recursive_request_body::Reply effects {} {
                return self::harness::route_recursive_request_body::Reply.Accepted;
            }
            """;
        var recursiveRequestBodyDiagnostics = await ExpectDiagnosticsAsync(
            harness, "route-recursive-request-body", recursiveRequestBody, "E_ROUTE_CODEC_UNSUPPORTED");
        AssertRangeAtToken(
            recursiveRequestBody,
            recursiveRequestBodyDiagnostics.Single(diagnostic => diagnostic.Code == "E_ROUTE_CODEC_UNSUPPORTED"),
            "self",
            3);

        var htmlRequestBody = """
            module harness::route_html_request_body;
            struct Request { page: Html }
            union Reply { Accepted, Empty }
            route POST "/page" {
                body: self::harness::route_html_request_body::Request;
                handler: self::harness::route_html_request_body::post;
                response Accepted: 200;
                response Empty: 204;
            }
            fn post(request: self::harness::route_html_request_body::Request) -> self::harness::route_html_request_body::Reply effects {} {
                return self::harness::route_html_request_body::Reply.Accepted;
            }
            """;
        var htmlRequestBodyDiagnostics = await ExpectDiagnosticsAsync(
            harness, "route-html-request-body", htmlRequestBody, "E_ROUTE_CODEC_UNSUPPORTED");
        AssertRangeAtToken(
            htmlRequestBody,
            htmlRequestBodyDiagnostics.Single(diagnostic => diagnostic.Code == "E_ROUTE_CODEC_UNSUPPORTED"),
            "self",
            1);

        var packageRoot = await harness.WritePackageAsync(
            "route-outside-web-entry",
            LibraryPackageManifest("route-library"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = """
                    module app::main;
                    pub union Reply { Found(Text), Empty }
                    fn good() -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
                    route GET "/items" { handler: self::app::main::good; response Found: 200 json Text; response Empty: 204; }
                    """
            });
        var packageCheck = await harness.InvokePackageDirectoryAsync("route-library-context", packageRoot, "check", "--json");
        AssertEqual(1, packageCheck.ExitCode, Describe(packageCheck));
        AssertTrue(ParseDiagnosticSnapshots(packageCheck.StandardOutput).Any(diagnostic => diagnostic.Code == "E_ROUTE_DECL"),
            "A library package must reject route declarations outside a route-capable web entry context.");
    }

    private static async Task TestHtmlManagedLibraryBuild(Harness harness)
    {
        var source = """
            module harness::opaque_html;
            struct HtmlBox { page: Html }
            union HtmlReply { Rendered(Html) }
            fn identity(value: Html) -> Html effects {} { return value; }
            fn preserve(value: self::harness::opaque_html::HtmlBox) -> self::harness::opaque_html::HtmlBox effects {} {
                return value;
            }
            fn render(value: Html) -> self::harness::opaque_html::HtmlReply effects {} {
                return self::harness::opaque_html::HtmlReply.Rendered(self::harness::opaque_html::identity(value));
            }
            """;
        var check = await harness.InvokeAsync("opaque-html-library-check", "check", source, "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        var build = await harness.InvokeAsync("opaque-html-library-build", "build", source);
        AssertEqual(0, build.ExitCode, Describe(build));
        AssertTrue(build.StandardOutput.StartsWith("Built library: ", StringComparison.Ordinal), Describe(build));
        var artifact = build.StandardOutput["Built library: ".Length..].Trim();
        AssertTrue(Path.IsPathFullyQualified(artifact), $"Expected a full managed library path. {Describe(build)}");
        AssertTrue(File.Exists(artifact), $"Expected managed library artifact at {artifact}. {Describe(build)}");
    }
    private static (object? Program, string[] DiagnosticCodes) CheckRoutePackageWithWebContext(
        Harness harness,
        IReadOnlyList<(string Module, string Source)> sources)
    {
        var compilerPath = Path.GetFullPath(Path.Combine(
            harness.RepositoryRoot, "src", "Hob", "bin", "Release", "net10.0", "hob.dll"));
        var assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
                string.Equals(candidate.Location, compilerPath, StringComparison.OrdinalIgnoreCase))
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(compilerPath);
        var diagnosticType = assembly.GetType("Diagnostic", throwOnError: true)!;
        var diagnosticsListType = typeof(List<>).MakeGenericType(diagnosticType);
        var diagnostics = Activator.CreateInstance(diagnosticsListType)!;

        var lexerType = assembly.GetType("Lexer", throwOnError: true)!;
        var scan = lexerType.GetMethod("Scan", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Lexer.Scan was not found.");
        var parserType = assembly.GetType("Parser", throwOnError: true)!;
        var parserConstructor = parserType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 3);
        var parse = parserType.GetMethod("Parse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Parser.Parse was not found.");

        var parsedPrograms = new List<object>();
        foreach (var (module, source) in sources)
        {
            var file = Path.Combine(harness.TemporaryRoot, module.Replace("::", Path.DirectorySeparatorChar.ToString()) + ".hob");
            var tokens = scan.Invoke(null, [source, file, diagnostics])
                ?? throw new InvalidOperationException("Lexer.Scan returned no tokens.");
            var parser = parserConstructor.Invoke([tokens, file, diagnostics]);
            parsedPrograms.Add(parse.Invoke(parser, null)
                ?? throw new InvalidOperationException($"Could not parse route module '{module}'."));
        }

        var diagnosticCount = (int)diagnosticsListType.GetProperty("Count")!.GetValue(diagnostics)!;
        if (diagnosticCount != 0)
            throw new InvalidOperationException("Route package test input produced parser diagnostics.");

        var moduleInputType = assembly.GetType("PackageModuleInput", throwOnError: true)!;
        var moduleInputConstructor = moduleInputType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 5);
        var moduleListType = typeof(List<>).MakeGenericType(moduleInputType);
        var moduleInputs = Activator.CreateInstance(moduleListType)!;
        var add = moduleListType.GetMethod("Add")!;
        foreach (var parsed in parsedPrograms)
        {
            var moduleInput = moduleInputConstructor.Invoke([
                "root",
                parsed,
                new Dictionary<string, string>(StringComparer.Ordinal),
                "route-root",
                null]);
            add.Invoke(moduleInputs, [moduleInput]);
        }

        var compilerType = assembly.GetType("Compiler", throwOnError: true)!;
        var checkPackage = compilerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "CheckPackage" && method.GetParameters().Length == 7);
        var routeCapabilities = new HashSet<string>(["db.read", "db.write"], StringComparer.Ordinal);
        var result = checkPackage.Invoke(null, [moduleInputs, "root", "app::main", routeCapabilities, false, true, null])
            ?? throw new InvalidOperationException("Compiler.CheckPackage returned no result.");
        var resultDiagnostics = result.GetType().GetProperty("Diagnostics")!.GetValue(result)
            ?? throw new InvalidOperationException("Compiler.CheckPackage returned no diagnostics list.");
        var diagnosticCodes = ((System.Collections.IEnumerable)resultDiagnostics).Cast<object>()
            .Select(item => item.GetType().GetProperty("Code")?.GetValue(item)?.ToString() ?? string.Empty)
            .ToArray();
        var program = result.GetType().GetProperty("Program")?.GetValue(result);
        return (program, diagnosticCodes);
    }
    private static async Task TestGenericFunctionRestrictions(Harness harness)
    {
        var librarySource = await File.ReadAllTextAsync(Path.Combine(
            harness.RepositoryRoot, "examples", "text-validation", "src", "text", "validation.hob"));
        var constructorConsumer = await harness.WritePackageAsync(
            "generic-context-dependent-constructors",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/text/validation.hob"] = librarySource,
                ["src/app/main.hob"] = """
                    module app::main;
                    pub fn from_some() -> Result<Text, Text> effects {} {
                        return self::text::validation::require(Some("present"), "fallback");
                    }
                    pub fn from_none() -> Result<Text, Text> effects {} {
                        return self::text::validation::require(None, "fallback");
                    }
                    pub fn main() -> Text effects {} { return "unused"; }
                    """
            });
        var constructorCheck = await harness.InvokePackageDirectoryAsync(
            "generic-context-dependent-constructors-check", constructorConsumer, "check", "--json");
        AssertTrue(constructorCheck.ExitCode != 0, Describe(constructorCheck));
        AssertEqual(string.Empty, constructorCheck.StandardError, Describe(constructorCheck));
        var constructorDiagnostics = ParseDiagnosticSnapshots(constructorCheck.StandardOutput);
        AssertEqual(2, constructorDiagnostics.Length,
            "Direct Some and None arguments should each report one inference diagnostic.");
        AssertTrue(constructorDiagnostics.All(diagnostic =>
                diagnostic.Code == "E_TYPE_MISMATCH"
                && diagnostic.Message.Contains("requires an expected type of Option<T>", StringComparison.Ordinal)),
            $"Direct context-dependent constructors should have the stable Option<T> diagnostic. {constructorCheck.StandardOutput}");
        AssertTrue(constructorDiagnostics.Any(diagnostic => diagnostic.Message.Contains("Some", StringComparison.Ordinal))
            && constructorDiagnostics.Any(diagnostic => diagnostic.Message.Contains("None", StringComparison.Ordinal)),
            "The inference diagnostic should identify both Some and None constructors.");

        const string genericMain = """
            module harness::generic_main;
            pub fn main<T>(value: T) -> i32 effects {} { return 7; }
            """;
        var genericMainBuild = await harness.InvokeAsync("generic-main-library-build", "build", genericMain);
        AssertBuiltDll(genericMainBuild, Path.GetDirectoryName(harness.LastSourcePath)!);
        var genericMainRun = await harness.InvokeAsync("generic-main-entrypoint", "run", genericMain);
        AssertTrue(genericMainRun.ExitCode != 0, Describe(genericMainRun));
        AssertEqual(string.Empty, genericMainRun.StandardOutput, Describe(genericMainRun));
        AssertTrue(genericMainRun.StandardError.Contains("E_ENTRYPOINT", StringComparison.Ordinal),
            $"A generic main function must not select the executable entrypoint. {Describe(genericMainRun)}");
    }

    private static async Task AssertDependencyConsumerWorksAsync(
        Harness harness,
        string caseName,
        string packageRoot,
        string expectedOutput)
    {
        var check = await harness.InvokePackageDirectoryAsync($"{caseName}-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));
        var build = await harness.InvokePackageDirectoryAsync($"{caseName}-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        var run = await harness.InvokePackageDirectoryAsync($"{caseName}-run", packageRoot, "run");
        AssertRunOutput(expectedOutput, run);
    }

    private static async Task AssertPackageCheckPassesAsync(Harness harness, string caseName, string packageRoot)
    {
        var check = await harness.InvokePackageDirectoryAsync(caseName, packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));
    }

    private static async Task AssertPathDependencyLockRequiredAsync(Harness harness, string caseName, string packageRoot)
    {
        foreach (var command in new[] { "check", "build", "run" })
        {
            var result = command == "check"
                ? await harness.InvokePackageDirectoryAsync($"{caseName}-{command}", packageRoot, command, "--json")
                : await harness.InvokePackageDirectoryAsync($"{caseName}-{command}", packageRoot, command);
            AssertTrue(result.ExitCode != 0, $"{command} unexpectedly accepted an invalid dependency lock. {Describe(result)}");
            if (command == "check")
            {
                var diagnostics = ParseDiagnosticSnapshots(result.StandardOutput);
                AssertTrue(diagnostics.Any(diagnostic => diagnostic.Code == "E_LOCK"),
                    $"check should report E_LOCK for a missing, malformed, or stale lock. {result.StandardOutput}");
                AssertEqual(string.Empty, result.StandardError, Describe(result));
            }
            else
            {
                AssertEqual(string.Empty, result.StandardOutput, Describe(result));
                AssertTrue(result.StandardError.Contains("E_LOCK", StringComparison.Ordinal),
                    $"{command} should report E_LOCK for a missing, malformed, or stale lock. {Describe(result)}");
            }
            AssertTrue(!result.StandardError.Contains("Unhandled exception", StringComparison.Ordinal)
                && !result.StandardError.Contains(" at ", StringComparison.Ordinal),
                $"A lock failure should be reported as a diagnostic. {Describe(result)}");
        }
    }

    private static void AssertLockCommandSucceeded(ProcessResult result)
    {
        AssertEqual(0, result.ExitCode, Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        AssertTrue(result.StandardOutput.StartsWith("Wrote package lock: ", StringComparison.Ordinal), Describe(result));
    }

    private static async Task AssertPackageDiagnosticAsync(
        Harness harness,
        string caseName,
        string packageRoot,
        string expectedCode,
        string expectedFile)
    {
        var result = await harness.InvokePackageDirectoryAsync(caseName, packageRoot, "check", "--json");
        AssertTrue(result.ExitCode != 0, $"Invalid package unexpectedly succeeded. {Describe(result)}");
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        using var document = JsonDocument.Parse(result.StandardOutput);
        AssertEqual(1, document.RootElement.GetProperty("schemaVersion").GetInt32(),
            "Package diagnostics must use JSON schema version 1.");
        var expectedPath = Path.GetFullPath(Path.Combine(packageRoot, expectedFile));
        var found = document.RootElement.GetProperty("diagnostics").EnumerateArray().Any(diagnostic =>
            diagnostic.GetProperty("code").GetString() == expectedCode
            && Path.GetFullPath(diagnostic.GetProperty("file").GetString() ?? string.Empty) == expectedPath
            && diagnostic.GetProperty("severity").GetString() == "error"
            && diagnostic.GetProperty("range").GetProperty("startLine").GetInt32() > 0
            && diagnostic.GetProperty("range").GetProperty("startColumn").GetInt32() > 0);
        AssertTrue(found, $"Expected {expectedCode} in {expectedPath}. {result.StandardOutput}");
    }

    private static string CliPackageManifest(string entryModule = "app::main") =>
        "name = \"harness-package\"\n" +
        "version = \"0.1.0\"\n" +
        "kind = \"cli\"\n" +
        "source_root = \"src\"\n" +
        $"entry_module = \"{entryModule}\"\n";

    private static string ConfigCliManifest() =>
        CliPackageManifest().Replace("name = \"harness-package\"", "name = \"config-harness\"", StringComparison.Ordinal)
        + "\n[config]\n"
        + "name = \"Text|required\"\n"
        + "mode = \"Text|default:normal\"\n"
        + "token = \"Secret<Text>|required\"\n"
        + "\n[capabilities]\n"
        + "env.read = \"allow\"\n"
        + "log.write = \"allow\"\n"
        + "secret.reveal = \"allow\"\n";

    private const string ConfigCliSource = """
        module app::main;

        pub union InspectError { Failed }

        command inspect {
            help "Read configured text and optionally reveal a secret.";
            argument label: Text help "Label for this invocation.";
            flag reveal help "Reveal the configured secret explicitly.";
            handler: self::app::main::run;
            error: self::app::main::describe;
        }

        fn read_values(config: Config) -> Text effects { env.read } {
            let name: Text = config.get_text("name");
            let mode: Text = config.get_text("mode");
            if mode == "normal" {
                return name;
            } else {
                return "unexpected-mode";
            }
        }

        fn reveal_token(config: Config, secrets: Secrets) -> Text effects { env.read, secret.reveal } {
            let token: Secret<Text> = config.get_secret_text("token");
            return secrets.reveal_text(token);
        }

        pub fn run(
            args: self::app::main::InspectArgs,
            config: Config,
            secrets: Secrets,
            logger: Logger
        ) -> Result<Text, self::app::main::InspectError> effects { env.read, log.write, secret.reveal } {
            let logged: bool = logger.info("boot\nready", "quote: \" tab:\t slash:\\");
            if args.reveal {
                return Ok(self::app::main::reveal_token(config, secrets));
            } else {
                return Ok(self::app::main::read_values(config));
            }
        }

        pub fn describe(error: self::app::main::InspectError) -> Text effects {} {
            return match error {
                self::app::main::InspectError.Failed => "failed"
            };
        }
        """;

    private static void AssertConfigFieldProjection(JsonElement config)
    {
        var fields = config.EnumerateArray().ToArray();
        AssertEqual(3, fields.Length, "Config reports should describe each declared config field exactly once.");
        var byName = fields.ToDictionary(field => field.GetProperty("name").GetString() ?? string.Empty, StringComparer.Ordinal);
        AssertTrue(byName.Keys.SequenceEqual(["mode", "name", "token"], StringComparer.Ordinal),
            "Config report fields must be sorted by source name.");
        foreach (var field in fields)
            AssertJsonPropertyOrder(field, "name,source_type,required,has_default");

        AssertEqual("Text", byName["mode"].GetProperty("source_type").GetString(), "Defaulted config fields must project as Text.");
        AssertEqual(false, byName["mode"].GetProperty("required").GetBoolean(), "A defaulted config field is not required.");
        AssertEqual(true, byName["mode"].GetProperty("has_default").GetBoolean(), "A defaulted config field must set has_default.");
        AssertEqual("Text", byName["name"].GetProperty("source_type").GetString(), "Required config fields must project as Text.");
        AssertEqual(true, byName["name"].GetProperty("required").GetBoolean(), "A required text field must set required.");
        AssertEqual(false, byName["name"].GetProperty("has_default").GetBoolean(), "A required text field has no default.");
        AssertEqual("Secret<Text>", byName["token"].GetProperty("source_type").GetString(), "Secret config fields must retain their source type.");
        AssertEqual(true, byName["token"].GetProperty("required").GetBoolean(), "A required secret field must set required.");
        AssertEqual(false, byName["token"].GetProperty("has_default").GetBoolean(), "A required secret field has no default.");
    }

    private static JsonDocument ParseConfigJson(string json, string artifact)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The config {artifact} output was not valid JSON: <{json}>", exception);
        }
    }

    private static async Task AssertNoTextInFilesAsync(string directory, string text)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var contents = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
            AssertTrue(!contents.Contains(text, StringComparison.Ordinal),
                $"Machine artifact {Path.GetFileName(path)} must not contain a runtime secret value.");
        }
    }

    private static string LibraryPackageManifest(string name = "harness-library") =>
        $"name = \"{name}\"\n" +
        "version = \"0.1.0\"\n" +
        "kind = \"lib\"\n" +
        "source_root = \"src\"\n";

    private static async Task ExpectPackageJsonDiagnosticAsync(
        Harness harness,
        string caseName,
        string manifest,
        IReadOnlyDictionary<string, string> sourceFiles,
        string expectedCode,
        string expectedFile)
    {
        var packageRoot = await harness.WritePackageAsync(caseName, manifest, sourceFiles);
        var result = await harness.InvokePackageDirectoryAsync(caseName, packageRoot, "check", "--json");
        AssertTrue(result.ExitCode != 0, $"Invalid package unexpectedly succeeded. {Describe(result)}");
        AssertEqual(string.Empty, result.StandardError, Describe(result));

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        AssertEqual(1, root.GetProperty("schemaVersion").GetInt32(), "Package diagnostics must use JSON schema version 1.");
        var expectedPath = Path.GetFullPath(Path.Combine(packageRoot, expectedFile));
        var found = root.GetProperty("diagnostics").EnumerateArray().Any(diagnostic =>
            diagnostic.GetProperty("code").GetString() == expectedCode &&
            Path.GetFullPath(diagnostic.GetProperty("file").GetString() ?? string.Empty) == expectedPath &&
            diagnostic.GetProperty("severity").GetString() == "error" &&
            diagnostic.GetProperty("range").GetProperty("startLine").GetInt32() > 0 &&
            diagnostic.GetProperty("range").GetProperty("startColumn").GetInt32() > 0);
        AssertTrue(found,
            $"Expected {expectedCode} in {expectedPath} with a structured range. {result.StandardOutput}");
    }

    private static async Task ExpectPackageJsonDiagnosticAtAsync(
        Harness harness,
        string caseName,
        string manifest,
        IReadOnlyDictionary<string, string> sourceFiles,
        string expectedCode,
        string expectedFile,
        string source,
        string markedSpan)
    {
        var packageRoot = await harness.WritePackageAsync(caseName, manifest, sourceFiles);
        var result = await harness.InvokePackageDirectoryAsync(caseName, packageRoot, "check", "--json");
        AssertTrue(result.ExitCode != 0, $"Invalid package unexpectedly succeeded. {Describe(result)}");
        AssertEqual(string.Empty, result.StandardError, Describe(result));

        using var document = JsonDocument.Parse(result.StandardOutput);
        var expectedPath = Path.GetFullPath(Path.Combine(packageRoot, expectedFile));
        var diagnostic = document.RootElement.GetProperty("diagnostics").EnumerateArray().FirstOrDefault(item =>
            item.GetProperty("code").GetString() == expectedCode
            && Path.GetFullPath(item.GetProperty("file").GetString() ?? string.Empty) == expectedPath);
        AssertTrue(diagnostic.ValueKind == JsonValueKind.Object,
            $"Expected {expectedCode} in {expectedPath}. {result.StandardOutput}");

        var index = source.IndexOf(markedSpan, StringComparison.Ordinal);
        AssertTrue(index >= 0, $"Test marker was not found in the command source: {markedSpan}");
        var (startLine, startColumn) = GetLineAndColumn(source, index);
        var (endLine, endColumn) = GetLineAndColumn(source, index + markedSpan.Length);
        var range = diagnostic.GetProperty("range");
        AssertEqual(startLine, range.GetProperty("startLine").GetInt32(), "Diagnostic start line mismatch.");
        AssertEqual(startColumn, range.GetProperty("startColumn").GetInt32(), "Diagnostic start column mismatch.");
        AssertEqual(endLine, range.GetProperty("endLine").GetInt32(), "Diagnostic end line mismatch.");
        AssertEqual(endColumn, range.GetProperty("endColumn").GetInt32(), "Diagnostic end column mismatch.");
    }

    private static (int Line, int Column) GetLineAndColumn(string source, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private static async Task ExpectPackageTextDiagnosticAsync(
        Harness harness,
        string caseName,
        string manifest,
        IReadOnlyDictionary<string, string> sourceFiles,
        string command,
        string expectedCode,
        string expectedFile)
    {
        var packageRoot = await harness.WritePackageAsync(caseName, manifest, sourceFiles);
        var result = await harness.InvokePackageDirectoryAsync(caseName, packageRoot, command);
        AssertTrue(result.ExitCode != 0, $"Invalid package unexpectedly succeeded. {Describe(result)}");
        AssertEqual(string.Empty, result.StandardOutput, Describe(result));
        var expectedPath = Path.GetFullPath(Path.Combine(packageRoot, expectedFile));
        AssertTrue(result.StandardError.Contains(expectedPath + ":", StringComparison.Ordinal) &&
                   result.StandardError.Contains(expectedCode, StringComparison.Ordinal),
            $"Expected {expectedCode} located in {expectedPath}. {Describe(result)}");
        AssertTrue(!result.StandardError.Contains("Unhandled exception", StringComparison.Ordinal) &&
                   !result.StandardError.Contains(" at ", StringComparison.Ordinal),
            $"Package entrypoint errors should not leak a backend stack trace. {Describe(result)}");
    }

    private static async Task TestAotCommandValidation(Harness harness)
    {
        const string source = "module harness::aot_cli;\n"
            + "pub fn main() -> i32 effects {} { return 41; }\n";

        var missingRid = await harness.InvokeAsync("aot-missing-rid", "build", source, "--aot");
        AssertBuildTargetRejected(missingRid, "requires --rid RID");

        var unsupportedRid = await harness.InvokeAsync(
            "aot-unsupported-rid", "build", source, "--aot", "--rid", "osx-x64");
        AssertBuildTargetRejected(unsupportedRid, "Unsupported AOT runtime identifier");

        var ridWithoutAot = await harness.InvokeAsync(
            "rid-without-aot", "build", source, "--rid", "win-x64");
        AssertBuildTargetRejected(ridWithoutAot, "only valid with hob build");

        var aotOnRun = await harness.InvokeAsync(
            "aot-on-run", "run", source, "--aot", "--rid", CurrentHostAotRid());
        AssertBuildTargetRejected(aotOnRun, "only valid with hob build");

        var aotOnCheck = await harness.InvokeAsync("aot-on-check", "check", source, "--aot");
        AssertBuildTargetRejected(aotOnCheck, "only valid with hob build");

        var aotOnUnknownCommand = await harness.InvokeAsync(
            "aot-on-unknown-command", "publish", source, "--aot", "--rid", CurrentHostAotRid());
        AssertBuildTargetRejected(aotOnUnknownCommand, "only valid with hob build");

        var mismatchedOsRid = CurrentHostAotRid() == "win-x64" ? "linux-x64" : "win-x64";
        var crossOsPublish = await harness.InvokeAsync(
            "aot-cross-os", "build", source, "--aot", "--rid", mismatchedOsRid);
        AssertBuildTargetRejected(crossOsPublish, "cross-OS publishing is not supported");
        var sourceDirectory = Path.GetDirectoryName(harness.LastSourcePath)!;
        AssertTrue(!Directory.Exists(Path.Combine(sourceDirectory, "out")),
            "A cross-OS AOT target must be rejected before creating output or starting publish.");
    }

    private static async Task TestAotLibraryRejected(Harness harness)
    {
        const string source = "module harness::aot_library;\n"
            + "pub fn square(value: i32) -> i32 effects {} { return value * value; }\n";
        var result = await harness.InvokeAsync(
            "aot-library", "build", source, "--aot", "--rid", CurrentHostAotRid());

        AssertBuildTargetRejected(result, "requires fn main()");
        var sourceDirectory = Path.GetDirectoryName(harness.LastSourcePath)!;
        AssertTrue(!Directory.Exists(Path.Combine(sourceDirectory, "out")),
            "A library source must be rejected before an output directory is created.");
    }

    private static async Task TestAotPublishAndRun(Harness harness)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new IntegrationTestSkippedException("The NativeAOT file smoke test targets x64 hosts only.");

        const string source = "module harness::aot_smoke;\n"
            + "pub fn main() -> i32 effects {} { return 41; }\n";
        var result = await harness.InvokeWithTimeoutAsync(
            "aot-smoke", "build", source, AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());

        AssertEqual(0, result.ExitCode, Describe(result));
        const string prefix = "Built native executable: ";
        AssertTrue(result.StandardOutput.StartsWith(prefix, StringComparison.Ordinal),
            $"Expected NativeAOT build output to start with <{prefix}>. {Describe(result)}");
        AssertTrue(result.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal),
            $"Expected one output line containing the native executable path. {Describe(result)}");
        var executablePath = result.StandardOutput[prefix.Length..^Environment.NewLine.Length];
        AssertEqual(prefix + executablePath + Environment.NewLine, result.StandardOutput,
            "NativeAOT build output should contain only the documented artifact line.");
        AssertTrue(Path.IsPathFullyQualified(executablePath),
            $"Expected a fully qualified native executable path, got <{executablePath}>.");
        AssertTrue(File.Exists(executablePath), $"Expected NativeAOT to create {executablePath}.");
        AssertEqual(CurrentHostAotRid() == "win-x64" ? "Generated.exe" : "Generated",
            Path.GetFileName(executablePath), "Unexpected native executable file name for the current host.");

        var outputDirectory = Path.GetDirectoryName(executablePath)!;
        var expectedOutputRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(harness.LastSourcePath)!, "out"));
        AssertEqual(expectedOutputRoot, Path.GetFullPath(Path.GetDirectoryName(outputDirectory)!),
            "The native executable should remain in the source tree's durable out directory.");
        AssertTrue(Path.GetFileName(outputDirectory).StartsWith("main-", StringComparison.Ordinal),
            $"Expected a unique main-<id> output directory, got <{outputDirectory}>.");

        using var receipt = await AssertBuildReceiptAsync(
            outputDirectory,
            "native_aot",
            CurrentHostAotRid(),
            [Path.GetRelativePath(outputDirectory, executablePath).Replace(Path.DirectorySeparatorChar, '/')],
            Path.GetDirectoryName(harness.LastSourcePath)!);

        var execution = await ExecuteNativeAsync(executablePath, TimeSpan.FromSeconds(30));
        AssertRunOutput("41" + Environment.NewLine, execution);
    }

    private static async Task TestPackageAotPublishAndRun(Harness harness)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new IntegrationTestSkippedException("The NativeAOT package smoke test targets x64 hosts only.");

        var packageRoot = await harness.WritePackageAsync(
            "package-aot-smoke",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = "module app::main; pub fn main() -> i32 effects {} { return 41; }"
            });
        var result = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "package-aot-smoke",
            packageRoot,
            "build",
            AotPublishTimeout,
            "--aot",
            "--rid",
            CurrentHostAotRid());

        AssertEqual(0, result.ExitCode, Describe(result));
        const string prefix = "Built native executable: ";
        AssertTrue(result.StandardOutput.StartsWith(prefix, StringComparison.Ordinal),
            $"Expected NativeAOT build output to start with <{prefix}>. {Describe(result)}");
        AssertTrue(result.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal),
            $"Expected one output line containing the native executable path. {Describe(result)}");
        var executablePath = result.StandardOutput[prefix.Length..^Environment.NewLine.Length];
        AssertEqual(prefix + executablePath + Environment.NewLine, result.StandardOutput,
            "NativeAOT build output should contain only the documented artifact line.");
        AssertTrue(Path.IsPathFullyQualified(executablePath),
            $"Expected a fully qualified native executable path, got <{executablePath}>.");
        AssertTrue(File.Exists(executablePath), $"Expected NativeAOT to create {executablePath}.");
        AssertEqual(CurrentHostAotRid() == "win-x64" ? "harness-package.exe" : "harness-package",
            Path.GetFileName(executablePath), "Unexpected package native executable name for the current host.");

        var outputDirectory = Path.GetDirectoryName(executablePath)!;
        var expectedOutputRoot = Path.GetFullPath(Path.Combine(packageRoot, "out"));
        AssertEqual(expectedOutputRoot, Path.GetFullPath(Path.GetDirectoryName(outputDirectory)!),
            "The native executable should remain in the package's durable out directory.");
        AssertTrue(Path.GetFileName(outputDirectory).StartsWith("harness-package-", StringComparison.Ordinal),
            $"Expected a unique package-name-<id> output directory, got <{outputDirectory}>.");

        var execution = await ExecuteNativeAsync(executablePath, TimeSpan.FromSeconds(30));
        AssertRunOutput("41" + Environment.NewLine, execution);
    }

    private static async Task TestCommandAotPublishAndRun(Harness harness)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new IntegrationTestSkippedException("The NativeAOT command smoke test targets x64 hosts only.");

        await using var server = new RawHttpServer();
        const string source = """
            module app::main;

            command scan {
                help "Fetch text from an HTTP target.";
                argument target: Text help "Relative HTTP target.";
                handler: self::app::main::run;
                error: self::app::main::describe;
            }

            pub async fn run(args: self::app::main::ScanArgs, client: HttpClient) -> Result<Text, HttpError> effects { net.client } {
                return match await client.get_text_async(args.target) {
                    Ok(response) => Ok(response.body),
                    Err(error) => Err(error)
                };
            }

            pub fn describe(error: HttpError) -> Text effects {} {
                return match error {
                    HttpError.InvalidTarget => "invalid target",
                    HttpError.Transport => "transport",
                    HttpError.Timeout => "timeout",
                    HttpError.ResponseTooLarge => "too large",
                    HttpError.InvalidText => "invalid text"
                };
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "command-aot-smoke",
            CliPackageManifest()
                + $"http_origin = \"{server.Origin}\"\n"
                + "\n[capabilities]\nnet.client = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source
            });
        var result = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "command-aot-smoke",
            packageRoot,
            "build",
            AotPublishTimeout,
            "--aot",
            "--rid",
            CurrentHostAotRid());

        AssertEqual(0, result.ExitCode, Describe(result));
        const string prefix = "Built native executable: ";
        AssertTrue(result.StandardOutput.StartsWith(prefix, StringComparison.Ordinal), Describe(result));
        AssertTrue(result.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal), Describe(result));
        var executablePath = result.StandardOutput[prefix.Length..^Environment.NewLine.Length];
        AssertEqual(prefix + executablePath + Environment.NewLine, result.StandardOutput,
            "Typed-command NativeAOT build output should contain only the documented artifact line.");
        AssertTrue(Path.IsPathFullyQualified(executablePath) && File.Exists(executablePath),
            $"Expected a native command executable at {executablePath}.");

        var schemaPath = Path.Combine(Path.GetDirectoryName(executablePath)!, "command-schema.json");
        AssertTrue(File.Exists(schemaPath), $"Expected command schema beside the NativeAOT executable: {schemaPath}");
        using var receipt = await AssertBuildReceiptAsync(
            Path.GetDirectoryName(executablePath)!,
            "native_aot",
            CurrentHostAotRid(),
            [Path.GetRelativePath(Path.GetDirectoryName(executablePath)!, executablePath).Replace(Path.DirectorySeparatorChar, '/'), "command-schema.json"],
            packageRoot);
        AssertEqual(1, receipt.RootElement.GetProperty("package_graph").GetArrayLength(),
            "A typed-command NativeAOT receipt should identify its root package.");
        using (var schema = JsonDocument.Parse(await File.ReadAllBytesAsync(schemaPath)))
        {
            AssertEqual(5, schema.RootElement.GetProperty("schema_version").GetInt32(),
                "The AOT command schema version must be 5.");
            AssertEqual("scan", schema.RootElement.GetProperty("commands")[0].GetProperty("name").GetString(),
                "The AOT command schema should retain its command declaration.");
            AssertJsonStringArray(schema.RootElement.GetProperty("commands")[0].GetProperty("capabilities"), ["net.client"]);
        }
        AssertJsonStringArray(receipt.RootElement.GetProperty("manifest_grants"), ["net.client"]);
        var httpClaim = receipt.RootElement.GetProperty("trusted_components").EnumerateArray()
            .Single(item => item.GetProperty("operation").GetString() == "HttpClient.get_text_async");
        AssertJsonStringArray(httpClaim.GetProperty("effects"), ["net.client"]);

        var execution = await ExecuteNativeAsync(
            executablePath,
            TimeSpan.FromSeconds(30),
            "scan",
            "--",
            "/aot?source=typed-command");
        AssertRunOutput("native loopback λ" + Environment.NewLine, execution);
    }

    private static async Task TestScanCliAotPublishAndRun(Harness harness)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new IntegrationTestSkippedException("The maintained scan CLI NativeAOT smoke test targets x64 hosts only.");

        var packageRoot = Path.Combine(harness.RepositoryRoot, "examples", "scan-cli");
        var temporaryDirectory = Path.Combine(harness.TemporaryRoot, $"scan-cli-aot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var inputPath = Path.Combine(temporaryDirectory, "aot-input.txt");
        await File.WriteAllTextAsync(inputPath, "  Native AOT λ  ", new UTF8Encoding(false, true));

        var result = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "scan-cli-aot-build",
            packageRoot,
            "build",
            AotPublishTimeout,
            "--aot",
            "--rid",
            CurrentHostAotRid());
        AssertEqual(0, result.ExitCode, Describe(result));
        const string prefix = "Built native executable: ";
        AssertTrue(result.StandardOutput.StartsWith(prefix, StringComparison.Ordinal), Describe(result));
        AssertTrue(result.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal), Describe(result));
        var executablePath = result.StandardOutput[prefix.Length..^Environment.NewLine.Length];
        AssertEqual(prefix + executablePath + Environment.NewLine, result.StandardOutput,
            "Maintained scan CLI AOT build output should contain only the executable path line.");
        AssertTrue(Path.IsPathFullyQualified(executablePath) && File.Exists(executablePath),
            $"Expected the NativeAOT scan CLI executable at {executablePath}.");

        var schemaPath = Path.Combine(Path.GetDirectoryName(executablePath)!, "command-schema.json");
        AssertTrue(File.Exists(schemaPath), $"Expected scan CLI AOT schema beside the executable: {schemaPath}");
        using (var schema = JsonDocument.Parse(await File.ReadAllBytesAsync(schemaPath)))
        {
            AssertEqual(5, schema.RootElement.GetProperty("schema_version").GetInt32(),
                "AOT scan command schema version mismatch.");
            var command = schema.RootElement.GetProperty("commands")[0];
            AssertEqual("scan", command.GetProperty("name").GetString(), "AOT scan schema command mismatch.");
            var capabilities = command.GetProperty("capabilities");
            AssertEqual(1, capabilities.GetArrayLength(), "AOT schema must retain required capabilities.");
            AssertEqual("fs.read", capabilities[0].GetString(), "AOT schema must record the FsRead grant requirement.");
        }

        var execution = await ExecuteNativeAsync(
            executablePath,
            TimeSpan.FromSeconds(30),
            "scan",
            inputPath,
            "--normalize");
        AssertRunOutput("Native AOT λ" + Environment.NewLine, execution);
    }

    private static string CurrentHostAotRid() => OperatingSystem.IsWindows()
        ? "win-x64"
        : OperatingSystem.IsLinux()
            ? "linux-x64"
            : throw new PlatformNotSupportedException("The NativeAOT integration smoke test requires Windows x64 or Linux x64.");

    private static void AssertBuildTargetRejected(ProcessResult result, string expectedMessage)
    {
        AssertTrue(result.ExitCode != 0, Describe(result));
        AssertEqual(string.Empty, result.StandardOutput, Describe(result));
        AssertTrue(result.StandardError.Contains("E_BUILD_TARGET", StringComparison.Ordinal)
            && result.StandardError.Contains(expectedMessage, StringComparison.Ordinal),
            $"Expected E_BUILD_TARGET with <{expectedMessage}>. {Describe(result)}");
        AssertTrue(!result.StandardError.Contains("Unhandled exception", StringComparison.Ordinal)
            && !result.StandardError.Contains(" at ", StringComparison.Ordinal),
            $"The compiler should report a diagnostic instead of a backend stack trace. {Describe(result)}");
    }

    private static Task<ProcessResult> ExecuteNativeAsync(string executablePath, TimeSpan timeout, params string[] arguments) =>
        ExecuteNativeWithEnvironmentAsync(executablePath, timeout, null, arguments);

    private static async Task<ProcessResult> ExecuteNativeWithEnvironmentAsync(
        string executablePath,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var name in startInfo.Environment.Keys
                     .Where(name => name.StartsWith("HOB_CONFIG_", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
            startInfo.Environment.Remove(name);
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
                startInfo.Environment[name] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start NativeAOT executable {executablePath}.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
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
            var timedOutStdout = await stdoutTask;
            var timedOutStderr = await stderrTask;
            throw new TimeoutException(
                $"NativeAOT executable timed out after {timeout}. stdout=<{timedOutStdout}>; stderr=<{timedOutStderr}>");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void AssertBuiltDll(ProcessResult result, string sourceDirectory)
    {
        AssertEqual(0, result.ExitCode, Describe(result));
        const string prefix = "Built library: ";
        AssertTrue(result.StandardOutput.StartsWith(prefix, StringComparison.Ordinal),
            $"Expected build output to start with <{prefix}>. {Describe(result)}");

        var pathWithNewline = result.StandardOutput[prefix.Length..];
        AssertTrue(pathWithNewline.EndsWith(Environment.NewLine, StringComparison.Ordinal),
            $"Expected a single output line containing the built DLL path. {Describe(result)}");
        var dllPath = pathWithNewline[..^Environment.NewLine.Length];
        AssertEqual(prefix + dllPath + Environment.NewLine, result.StandardOutput, "Build output should contain only the documented artifact line.");
        AssertTrue(Path.IsPathFullyQualified(dllPath), $"Expected a fully qualified built DLL path, got <{dllPath}>.");
        AssertTrue(File.Exists(dllPath), $"Expected the library build to create {dllPath}.");

        var buildDirectory = Path.GetDirectoryName(dllPath)
            ?? throw new InvalidOperationException($"Built DLL path {dllPath} has no parent directory.");
        var expectedOutputRoot = Path.GetFullPath(Path.Combine(sourceDirectory, "out"));
        AssertEqual(expectedOutputRoot, Path.GetFullPath(Path.GetDirectoryName(buildDirectory)!),
            "The built DLL should remain in the source tree's durable out directory.");
        AssertTrue(Path.GetFileName(buildDirectory).StartsWith("main-", StringComparison.Ordinal),
            $"Expected a unique main-<id> build directory, got <{buildDirectory}>.");
        AssertEqual(string.Empty, result.StandardError, Describe(result));
    }

    private static async Task TestInvalidEntrypoint(Harness harness)
    {
        const string source = """
            module harness::invalid_main;
            pub fn main(value: i32) -> i32 effects {} { return value; }
            """;
        var result = await harness.InvokeAsync("invalid-main", "run", source);
        AssertTrue(result.ExitCode != 0, Describe(result));
        AssertEqual(string.Empty, result.StandardOutput, Describe(result));
        AssertTrue(result.StandardError.Contains("E_ENTRYPOINT", StringComparison.Ordinal)
            && result.StandardError.Contains("no parameters", StringComparison.Ordinal),
            $"Invalid main signature should receive the entrypoint diagnostic. {Describe(result)}");
        AssertTrue(!result.StandardError.Contains("Unhandled exception", StringComparison.Ordinal)
            && !result.StandardError.Contains(" at ", StringComparison.Ordinal),
            "Invalid entrypoint should be reported before generated backend execution.");
    }

    private static async Task TestDotnetLaunchFailure(Harness harness)
    {
        const string source = """
            module harness::process_failure;
            pub fn main() -> i32 effects {} { return 7; }
            """;
        var badHost = Path.Combine(harness.TemporaryRoot, "missing-dotnet-host.exe");
        var result = await harness.InvokeUsingHostOverrideAsync("dotnet-launch-failure", "run", source, badHost);
        AssertTrue(result.ExitCode != 0, Describe(result));
        AssertEqual(string.Empty, result.StandardOutput, Describe(result));
        AssertTrue(result.StandardError.Contains("E_PROCESS", StringComparison.Ordinal),
            $"A child-process launch failure should produce E_PROCESS. {Describe(result)}");
        AssertTrue(!result.StandardError.Contains("Unhandled exception", StringComparison.Ordinal)
            && !result.StandardError.Contains(" at ", StringComparison.Ordinal),
            "A child-process launch failure should not leak a stack trace.");
    }
    private static async Task TestCheckedOverflow(Harness harness)
    {
        const string source = "module harness::overflow;\n"
            + "pub fn main() -> i32 effects {} { return 2147483647 + 1; }\n";

        var check = await harness.InvokeAsync("checked-overflow-check", "check", source);
        AssertEqual(0, check.ExitCode, Describe(check));

        var result = await harness.InvokeAsync("checked-overflow-run", "run", source);
        AssertEqual(70, result.ExitCode, Describe(result));
        AssertEqual("Runtime fault", result.StandardError.TrimEnd(), Describe(result));
        AssertTrue(!result.StandardError.Contains("Exception", StringComparison.Ordinal)
            && !result.StandardError.Contains(" at ", StringComparison.Ordinal), "Runtime fault output must not include exception details or a stack trace.");
    }

    private static async Task TestParallelRuns(Harness harness)
    {
        const string firstSource = "module harness::parallel_a;\n"
            + "pub fn main() -> i32 effects {} { return 17; }\n";
        const string secondSource = "module harness::parallel_b;\n"
            + "pub fn main() -> i32 effects {} { return 29; }\n";

        var sharedSourceDirectory = harness.CreateSourceDirectory("parallel-runs");
        var firstPath = await harness.WriteSourceAsync(sharedSourceDirectory, "first.hob", firstSource);
        var secondPath = await harness.WriteSourceAsync(sharedSourceDirectory, "second.hob", secondSource);
        var first = harness.InvokeFileAsync("parallel-run-a", firstPath, "run");
        var second = harness.InvokeFileAsync("parallel-run-b", secondPath, "run");
        var results = await Task.WhenAll(first, second);

        AssertEqual(0, results[0].ExitCode, Describe(results[0]));
        AssertEqual("17" + Environment.NewLine, results[0].StandardOutput, Describe(results[0]));
        AssertEqual(0, results[1].ExitCode, Describe(results[1]));
        AssertEqual("29" + Environment.NewLine, results[1].StandardOutput, Describe(results[1]));
    }

    private static string? FindRepositoryRoot()
    {
        var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var start in starts)
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "hobthrush.slnx"))
                    && File.Exists(Path.Combine(directory.FullName, "src", "Hob", "Hob.csproj")))
                {
                    return directory.FullName;
                }
            }
        }

        return null;
    }

    private static string GetDotnetPath(string repositoryRoot)
    {
        var localDotnet = Path.Combine(repositoryRoot, ".dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(localDotnet) ? localDotnet : "dotnet";
    }

    private static string LastNonEmptyLine(string text) => text
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault() ?? string.Empty;

    private static string Describe(ProcessResult result) =>
        $"exit={result.ExitCode}; stdout=<{result.StandardOutput}>; stderr=<{result.StandardError}>";

    private static void AssertEqual<T>(T expected, T? actual, string details)
    {
        if (!EqualityComparer<T?>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected <{expected}> but got <{actual}>. {details}");
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertJsonStringArray(JsonElement element, string[] expected)
    {
        var actual = element.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
        AssertTrue(expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"Expected JSON string array [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    private static string ParseBuiltArtifact(ProcessResult result, string prefix)
    {
        AssertTrue(result.StandardOutput.StartsWith(prefix, StringComparison.Ordinal), Describe(result));
        var artifact = result.StandardOutput[prefix.Length..].Trim();
        AssertTrue(Path.IsPathFullyQualified(artifact) && File.Exists(artifact),
            $"Expected a fully qualified built artifact path. {Describe(result)}");
        return artifact;
    }

    private static async Task<JsonDocument> AssertBuildReceiptAsync(
        string outputDirectory,
        string expectedMode,
        string? expectedRuntimeIdentifier,
        string[] requiredArtifactPaths,
        params string[] forbiddenAbsolutePaths)
    {
        var receiptPath = Path.Combine(outputDirectory, "build-receipt.json");
        AssertTrue(File.Exists(receiptPath), $"Expected managed build receipt at {receiptPath}.");
        var bytes = await File.ReadAllBytesAsync(receiptPath);
        AssertTrue(bytes.Length > 0 && bytes[^1] == (byte)'\n' && !bytes.Contains((byte)'\r'),
            "Build receipts must be UTF-8 JSON with LF line endings and a final newline.");
        var json = new UTF8Encoding(false, true).GetString(bytes);
        foreach (var forbiddenPath in forbiddenAbsolutePaths)
            AssertTrue(!json.Contains(forbiddenPath, StringComparison.OrdinalIgnoreCase),
                $"Build receipts must not leak an absolute workspace path: {forbiddenPath}");
        AssertTrue(!Regex.IsMatch(json, @"(?i)(?<![0-9a-f])[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?![0-9a-f])")
            && !Regex.IsMatch(json, @"(?i)(?<![0-9a-f])[0-9a-f]{32}(?![0-9a-f])"),
            "Build receipts must not contain generated GUIDs.");

        var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        AssertJsonPropertyOrder(root,
            "schema_version,build,performed_checks,package_graph,toolchain,inputs,manifest_grants,trusted_components,foreign_dependencies,managed_adapters,audit_snapshot_sha256,artifacts");
        AssertEqual(4, root.GetProperty("schema_version").GetInt32(), "Build receipt schema version must be 4.");
        var build = root.GetProperty("build");
        AssertJsonPropertyOrder(build, "mode,framework,runtime_identifier");
        AssertEqual(expectedMode, build.GetProperty("mode").GetString(), "Unexpected build receipt mode.");
        AssertEqual("net10.0", build.GetProperty("framework").GetString(), "Unexpected receipt target framework.");
        if (expectedRuntimeIdentifier is null)
            AssertEqual(JsonValueKind.Null, build.GetProperty("runtime_identifier").ValueKind,
                "Managed builds should have a null runtime identifier.");
        else
            AssertEqual(expectedRuntimeIdentifier, build.GetProperty("runtime_identifier").GetString(),
                "NativeAOT receipts should identify the selected runtime.");

        var packageGraph = root.GetProperty("package_graph");
        var expectedPerformedChecks = new List<string>();
        if (packageGraph.GetArrayLength() == 0)
        {
            expectedPerformedChecks.Add("compiler.source_parse");
        }
        else
        {
            expectedPerformedChecks.Add("compiler.package_graph_resolve");
            var packageLockWasRequired = packageGraph.EnumerateArray().Any(package =>
                    package.GetProperty("dependencies").GetArrayLength() != 0)
                || root.GetProperty("managed_adapters").GetArrayLength() != 0;
            if (packageLockWasRequired)
                expectedPerformedChecks.Add("compiler.package_lock_validate");
            expectedPerformedChecks.Add("compiler.package_sources_parse");
        }
        expectedPerformedChecks.Add("compiler.semantic_check");
        expectedPerformedChecks.Add(expectedMode == "managed"
            ? "generated.managed_build"
            : "generated.native_aot_publish");
        AssertJsonStringArray(root.GetProperty("performed_checks"), expectedPerformedChecks.ToArray());

        foreach (var package in packageGraph.EnumerateArray())
        {
            AssertJsonPropertyOrder(package, "identity,role,content_sha256,dependencies");
            AssertPackageIdentitySource(package.GetProperty("identity"));
            AssertTrue(IsLowerSha256(package.GetProperty("content_sha256").GetString()),
                "Package graph content hashes must be lowercase SHA-256 values.");
            foreach (var dependency in package.GetProperty("dependencies").EnumerateArray())
            {
                AssertJsonPropertyOrder(dependency, "alias,package");
                AssertPackageIdentitySource(dependency.GetProperty("package"));
            }
        }

        var toolchain = root.GetProperty("toolchain");
        AssertJsonPropertyOrder(toolchain, "compiler_version,compiler_sha256,dotnet_sdk_version,foreign_dependencies");
        AssertTrue(!string.IsNullOrWhiteSpace(toolchain.GetProperty("compiler_version").GetString()),
            "Build receipts should identify the compiler version.");
        AssertTrue(IsLowerSha256(toolchain.GetProperty("compiler_sha256").GetString()),
            "Build receipts should identify the compiler bytes with SHA-256.");
        AssertTrue(!string.IsNullOrWhiteSpace(toolchain.GetProperty("dotnet_sdk_version").GetString()),
            "Build receipts should identify the .NET SDK version.");

        string? previousInputKey = null;
        foreach (var input in root.GetProperty("inputs").EnumerateArray())
        {
            AssertJsonPropertyOrder(input, "package,kind,path,sha256");
            var packagePath = ".";
            if (input.GetProperty("package").ValueKind != JsonValueKind.Null)
            {
                var identity = input.GetProperty("package");
                AssertPackageIdentitySource(identity);
                packagePath = identity.GetProperty("path").GetString() ?? string.Empty;
            }
            AssertTrue(IsPortableRelativePath(input.GetProperty("path").GetString(), allowParentSegments: false),
                "Receipt input paths must be relative package paths.");
            AssertTrue(IsLowerSha256(input.GetProperty("sha256").GetString()),
                "Receipt input hashes must be lowercase SHA-256 values.");
            var inputKey = packagePath + "\0" + input.GetProperty("path").GetString();
            AssertTrue(previousInputKey is null || StringComparer.Ordinal.Compare(previousInputKey, inputKey) <= 0,
                "Receipt inputs should be sorted by package and relative path.");
            previousInputKey = inputKey;
        }

        var grants = root.GetProperty("manifest_grants").EnumerateArray()
            .Select(grant => grant.GetString() ?? string.Empty).ToArray();
        AssertTrue(grants.SequenceEqual(grants.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "Receipt manifest grants should be sorted.");
        foreach (var claim in root.GetProperty("trusted_components").EnumerateArray())
        {
            AssertJsonPropertyOrder(claim, "operation,source,effects,assurance,reachable_from");
            foreach (var reachable in claim.GetProperty("reachable_from").EnumerateArray())
            {
                AssertJsonPropertyOrder(reachable, "package,module,name");
                AssertPackageIdentitySource(reachable.GetProperty("package"));
            }
        }
        AssertTrue(root.GetProperty("foreign_dependencies").GetRawText() == toolchain.GetProperty("foreign_dependencies").GetRawText(),
            "Top-level and toolchain foreign dependency records should match.");
        foreach (var dependency in root.GetProperty("foreign_dependencies").EnumerateArray())
            AssertJsonPropertyOrder(dependency, "name,version,ecosystem,reason");
        AssertManagedAdapterProvenanceOrder(root.GetProperty("managed_adapters"));
        AssertTrue(IsLowerSha256(root.GetProperty("audit_snapshot_sha256").GetString()),
            "Build receipts should bind the checked audit snapshot with SHA-256.");

        var artifacts = root.GetProperty("artifacts").EnumerateArray().ToArray();
        var artifactPaths = artifacts.Select(artifact => artifact.GetProperty("path").GetString() ?? string.Empty).ToArray();
        AssertTrue(artifactPaths.SequenceEqual(artifactPaths.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "Receipt artifact paths must be sorted.");
        AssertEqual(artifactPaths.Length, artifactPaths.Distinct(StringComparer.Ordinal).Count(),
            "Receipt artifact paths must be unique.");
        AssertTrue(!artifactPaths.Contains("build-receipt.json", StringComparer.Ordinal),
            "The receipt must not hash itself.");
        AssertTrue(requiredArtifactPaths.All(path => artifactPaths.Contains(path, StringComparer.Ordinal)),
            $"Receipt artifacts must include [{string.Join(", ", requiredArtifactPaths)}].");
        foreach (var artifact in artifacts)
        {
            AssertJsonPropertyOrder(artifact, "path,sha256");
            var relativePath = artifact.GetProperty("path").GetString() ?? string.Empty;
            AssertTrue(IsPortableRelativePath(relativePath, allowParentSegments: false),
                $"Artifact paths must be normalized relative paths: {relativePath}.");
            AssertTrue(IsLowerSha256(artifact.GetProperty("sha256").GetString()),
                $"Artifact {relativePath} must have a lowercase SHA-256 hash.");
            var filePath = Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            AssertTrue(File.Exists(filePath), $"Receipt artifact is missing: {filePath}.");
            var actualHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(filePath))).ToLowerInvariant();
            AssertEqual(artifact.GetProperty("sha256").GetString(), actualHash,
                $"Receipt artifact hash does not match {relativePath}.");
        }

        var actualArtifactPaths = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(receiptPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(outputDirectory, path)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        AssertTrue(artifactPaths.SequenceEqual(actualArtifactPaths, StringComparer.Ordinal),
            "Receipt artifact entries should cover every build output file except build-receipt.json.");

        Visit(root, "$");
        return document;

        static bool IsLowerSha256(string? value) =>
            value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

        static bool IsPortableRelativePath(string? value, bool allowParentSegments)
        {
            if (string.IsNullOrEmpty(value) || Path.IsPathFullyQualified(value) || value.Contains('\\'))
                return false;
            return allowParentSegments || !value.Split('/').Any(segment => segment is ".." or "");
        }

        static void Visit(JsonElement element, string path)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    var name = property.Name;
                    AssertTrue(name is not "id" and not "function_id" and not "package_id",
                        $"Receipt should use symbolic identities instead of numeric IDs at {path}.{name}.");
                    AssertTrue(!Regex.IsMatch(name, @"(?i)(timestamp|built_at|created_at)"),
                        $"Receipt should not include timestamps at {path}.{name}.");
                    Visit(property.Value, $"{path}.{name}");
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Visit(item, $"{path}[{index++}]");
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                var value = element.GetString() ?? string.Empty;
                AssertTrue(!Path.IsPathFullyQualified(value), $"Receipt contains an absolute path at {path}: {value}");
                if (path.EndsWith(".path", StringComparison.Ordinal))
                    AssertTrue(IsPortableRelativePath(value, allowParentSegments: true), $"Receipt path is not portable at {path}: {value}");
            }
        }
    }

    private static void AssertJsonPropertyOrder(JsonElement element, string expected)
    {
        AssertEqual(expected, string.Join(",", element.EnumerateObject().Select(property => property.Name)),
            "JSON property order should match the serialized contract.");
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        private readonly AutoResetEvent posted = new(false);

        public bool HasPendingCallbacks => !callbacks.IsEmpty;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            callbacks.Enqueue((callback, state));
            posted.Set();
        }

        public void RunUntilCompleted(Task task, TimeSpan timeout)
        {
            _ = task.ContinueWith(
                _ => posted.Set(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            var deadline = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (!callbacks.TryDequeue(out var work))
                {
                    var remaining = timeout - deadline.Elapsed;
                    if (remaining <= TimeSpan.Zero || !posted.WaitOne(remaining))
                        throw new TimeoutException($"The generated async write did not finish within {timeout}.");
                    continue;
                }

                var previous = Current;
                SetSynchronizationContext(this);
                try
                {
                    work.Callback(work.State);
                }
                finally
                {
                    SetSynchronizationContext(previous);
                }
            }
        }

        public void Dispose() => posted.Dispose();
    }

    private sealed class Harness(string repositoryRoot, string compilerDll, string dotnet, string temporaryRoot)
    {
        private Task<string>? processFixtureBuild;

        public string RepositoryRoot => repositoryRoot;
        public string LastSourcePath { get; private set; } = string.Empty;

        public string CreateSourceDirectory(string caseName)
        {
            var caseRoot = Path.Combine(temporaryRoot, $"{caseName}-{Guid.NewGuid():N}");
            var sourceDirectory = Path.Combine(caseRoot, "work", "src");
            Directory.CreateDirectory(sourceDirectory);
            return sourceDirectory;
        }

        public async Task<string> WriteSourceAsync(string sourceDirectory, string fileName, string source)
        {
            var sourcePath = Path.Combine(sourceDirectory, fileName);
            await File.WriteAllTextAsync(sourcePath, source);
            LastSourcePath = sourcePath;
            return sourcePath;
        }

        public async Task<string> WritePackageAsync(
            string caseName,
            string manifest,
            IReadOnlyDictionary<string, string> sourceFiles)
        {
            var packageRoot = Path.Combine(temporaryRoot, $"{caseName}-{Guid.NewGuid():N}", "package");
            Directory.CreateDirectory(Path.Combine(packageRoot, "src"));
            await File.WriteAllTextAsync(Path.Combine(packageRoot, "hob.toml"), manifest);
            LastSourcePath = Path.Combine(packageRoot, "hob.toml");
            foreach (var sourceFile in sourceFiles.OrderBy(file => file.Key, StringComparer.Ordinal))
            {
                var relativePath = sourceFile.Key.Replace('/', Path.DirectorySeparatorChar);
                var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, sourceFile.Value);
                LastSourcePath = path;
            }

            return packageRoot;
        }

        public async Task<string> WritePackageGraphAsync(
            string caseName,
            IReadOnlyDictionary<string, PackageFixture> packages)
        {
            var workspaceRoot = Path.Combine(temporaryRoot, $"{caseName}-{Guid.NewGuid():N}", "workspace");
            Directory.CreateDirectory(workspaceRoot);
            foreach (var package in packages.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var relativeRoot = package.Key.Replace('/', Path.DirectorySeparatorChar);
                var packageRoot = Path.GetFullPath(Path.Combine(workspaceRoot, relativeRoot));
                Directory.CreateDirectory(Path.Combine(packageRoot, "src"));
                await File.WriteAllTextAsync(Path.Combine(packageRoot, "hob.toml"), package.Value.Manifest);
                LastSourcePath = Path.Combine(packageRoot, "hob.toml");
                foreach (var sourceFile in package.Value.SourceFiles.OrderBy(file => file.Key, StringComparer.Ordinal))
                {
                    var relativePath = sourceFile.Key.Replace('/', Path.DirectorySeparatorChar);
                    var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllTextAsync(path, sourceFile.Value);
                    LastSourcePath = path;
                }
            }

            return Path.Combine(workspaceRoot, "root");
        }

        public string TemporaryRoot => temporaryRoot;

        public Task<string> GetProcessFixtureExecutableAsync() =>
            processFixtureBuild ??= BuildProcessFixtureExecutableAsync();

        private async Task<string> BuildProcessFixtureExecutableAsync()
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new IntegrationTestSkippedException("The generated native process fixture targets x64 hosts only.");

            var sourceDirectory = Path.Combine(temporaryRoot, "process-fixture-source");
            var outputDirectory = Path.Combine(temporaryRoot, "process-fixture-publish");
            Directory.CreateDirectory(sourceDirectory);
            await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "process-fixture.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <PublishAot>true</PublishAot>
                    <StripSymbols>true</StripSymbols>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "Program.cs"), ProcessFixtureProgramSource);

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
                         "publish", "process-fixture.csproj", "-c", "Release", "-r", CurrentHostAotRid(),
                         "--self-contained", "true", "--nologo", "-v:q", "-p:RestoreIgnoreFailedSources=true",
                         "-o", outputDirectory
                     })
                startInfo.ArgumentList.Add(argument);
            startInfo.Environment["HOB_DOTNET"] = dotnet;
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("Could not start the temporary NativeAOT ProcessRunner helper build.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(AotPublishTimeout);
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
                throw new TimeoutException($"NativeAOT ProcessRunner helper publish exceeded {AotPublishTimeout}.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Temporary NativeAOT ProcessRunner helper build failed ({process.ExitCode}). stdout=<{stdout}> stderr=<{stderr}>");

            var executable = Path.Combine(outputDirectory, OperatingSystem.IsWindows() ? "process-fixture.exe" : "process-fixture");
            if (!File.Exists(executable))
                throw new InvalidOperationException($"Temporary NativeAOT ProcessRunner helper was not published at {executable}. stdout=<{stdout}> stderr=<{stderr}>");
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return executable;
        }

        private const string ProcessFixtureProgramSource = """
            using System.Diagnostics;
            using System.Text;

            if (args.Length == 0)
            {
                Console.WriteLine("[]");
                return 0;
            }

            return await RunAsync(args);

            static async Task<int> RunAsync(string[] arguments)
            {
                Console.InputEncoding = new UTF8Encoding(false, true);
                Console.OutputEncoding = new UTF8Encoding(false);
                var mode = arguments[0];
                switch (mode)
                {
                    case "args":
                        Console.WriteLine("[" + string.Join(",", arguments.Skip(1).Select(JsonQuote)) + "]");
                        return 0;
                    case "count":
                        Console.Write(arguments.Length - 1);
                        return 0;
                    case "size":
                        Console.Write(arguments.Sum(value => Encoding.UTF8.GetByteCount(value)));
                        return 0;
                    case "stdin":
                        Console.Out.Write(await Console.In.ReadToEndAsync());
                        return 0;
                    case "streams":
                        Console.Out.Write("child stdout λ");
                        Console.Error.Write("child stderr λ");
                        return 0;
                    case "exit":
                        Console.Out.Write("child exited nonzero");
                        Console.Error.Write("child exit diagnostic");
                        return 7;
                    case "meta":
                        Console.WriteLine("{\"cwd\":" + JsonQuote(Environment.CurrentDirectory)
                            + ",\"marker\":" + JsonQuote(Environment.GetEnvironmentVariable("HOB_PROCESS_TEST_CANARY") ?? "<missing>") + "}");
                        return 0;
                    case "stdout":
                        Console.Out.Write(new string('x', int.Parse(arguments[1], System.Globalization.CultureInfo.InvariantCulture)));
                        return 0;
                    case "stderr":
                        Console.Error.Write(new string('e', int.Parse(arguments[1], System.Globalization.CultureInfo.InvariantCulture)));
                        return 0;
                    case "invalid-utf8":
                        await Console.OpenStandardOutput().WriteAsync(new byte[] { 0xff }, 0, 1);
                        return 0;
                    case "timeout":
                        var childStart = new ProcessStartInfo
                        {
                            FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Native helper process path is unavailable."),
                            UseShellExecute = false
                        };
                        childStart.ArgumentList.Add("child");
                        childStart.ArgumentList.Add(arguments[1]);
                        using (var child = Process.Start(childStart) ?? throw new InvalidOperationException("Could not start the descendant fixture."))
                            await File.WriteAllTextAsync(arguments[1], child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        await Task.Delay(Timeout.InfiniteTimeSpan);
                        return 0;
                    case "child":
                        await Task.Delay(TimeSpan.FromMinutes(2));
                        await File.WriteAllTextAsync(arguments[1] + ".late", "descendant survived");
                        return 0;
                    default:
                        return 64;
                }
            }

            static string JsonQuote(string value) => "\"" + value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal) + "\"";
            """;

        public async Task<ProcessResult> RunManagedArtifactAsync(
            string artifactPath,
            string workingDirectory,
            params string[] arguments) =>
            await RunManagedArtifactWithTimeoutAsync(artifactPath, workingDirectory, ProcessTimeout, arguments);

        public Task<ProcessResult> RunManagedArtifactWithEnvironmentAsync(
            string artifactPath,
            string workingDirectory,
            IReadOnlyDictionary<string, string>? environment,
            params string[] arguments) =>
            RunManagedArtifactWithTimeoutAndEnvironmentAsync(artifactPath, workingDirectory, ProcessTimeout, environment, arguments);

        public Task<ProcessResult> RunManagedArtifactWithTimeoutAsync(
            string artifactPath,
            string workingDirectory,
            TimeSpan timeout,
            params string[] arguments) =>
            RunManagedArtifactWithTimeoutAndEnvironmentAsync(artifactPath, workingDirectory, timeout, null, arguments);

        private async Task<ProcessResult> RunManagedArtifactWithTimeoutAndEnvironmentAsync(
            string artifactPath,
            string workingDirectory,
            TimeSpan timeout,
            IReadOnlyDictionary<string, string>? environment,
            string[] arguments)
        {
            using var process = StartManagedArtifactProcess(artifactPath, workingDirectory, environment, arguments);
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cancellation.Token);
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
                throw new TimeoutException($"Managed artifact {artifactPath} did not exit within {timeout}.");
            }

            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }

        public Process StartManagedArtifactProcess(
            string artifactPath,
            string workingDirectory,
            IReadOnlyDictionary<string, string>? environment,
            params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(artifactPath);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            startInfo.Environment["HOB_DOTNET"] = dotnet;
            RemoveConfigEnvironment(startInfo);
            if (environment is not null)
            {
                foreach (var (name, value) in environment)
                    startInfo.Environment[name] = value;
            }

            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException($"Could not start managed artifact {artifactPath}.");
            }
            return process;
        }

        public Task<ProcessResult> InvokeAsync(string caseName, string command, string source, params string[] additionalArguments) =>
            InvokeWithTimeoutAsync(caseName, command, source, ProcessTimeout, additionalArguments);

        public Task<ProcessResult> InvokeCompilerCommandAsync(params string[] arguments) =>
            InvokeCompilerCommandWithEnvironmentAsync(null, arguments);

        public async Task<ProcessResult> InvokeCompilerCommandWithEnvironmentAsync(
                IReadOnlyDictionary<string, string>? environment,
                params string[] arguments)
                => await InvokeCompilerCommandAtDirectoryWithEnvironmentAsync(
                    repositoryRoot, environment, arguments);

        public async Task<ProcessResult> InvokeCompilerCommandAtDirectoryWithEnvironmentAsync(
            string workingDirectory,
            IReadOnlyDictionary<string, string>? environment,
            params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(compilerDll);
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            startInfo.Environment["HOB_DOTNET"] = dotnet;
            RemoveConfigEnvironment(startInfo);
            if (environment is not null)
            {
                foreach (var (name, value) in environment)
                    startInfo.Environment[name] = value;
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("Could not start the language compiler command.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var cancellation = new CancellationTokenSource(ProcessTimeout);
            try
            {
                await process.WaitForExitAsync(cancellation.Token);
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
                var timedOutStdout = await stdoutTask;
                var timedOutStderr = await stderrTask;
                throw new TimeoutException(
                    $"Compiler command timed out after {ProcessTimeout}. stdout=<{timedOutStdout}>; stderr=<{timedOutStderr}>");
            }

            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }

        public async Task<ProcessResult> InvokeUsingHostOverrideAsync(string caseName, string command, string source, string hostOverride)
        {
            var sourceDirectory = CreateSourceDirectory(caseName);
            var sourcePath = await WriteSourceAsync(sourceDirectory, "main.hob", source);
            return await InvokeFileWithTimeoutAsync(caseName, sourcePath, command, ProcessTimeout, hostOverride);
        }

        public async Task<ProcessResult> InvokeWithTimeoutAsync(string caseName, string command, string source, TimeSpan timeout, params string[] additionalArguments)
        {
            var sourceDirectory = CreateSourceDirectory(caseName);
            var sourcePath = await WriteSourceAsync(sourceDirectory, "main.hob", source);
            return await InvokeFileWithTimeoutAsync(caseName, sourcePath, command, timeout, null, additionalArguments);
        }

        public Task<ProcessResult> InvokeFileAsync(string caseName, string sourcePath, string command, params string[] additionalArguments) =>
            InvokeFileWithTimeoutAsync(caseName, sourcePath, command, ProcessTimeout, null, additionalArguments);

        public async Task<ProcessResult> InvokePackageAsync(
            string caseName,
            string command,
            string manifest,
            IReadOnlyDictionary<string, string> sourceFiles,
            params string[] additionalArguments)
        {
            var packageRoot = await WritePackageAsync(caseName, manifest, sourceFiles);
            return await InvokePackageDirectoryAsync(caseName, packageRoot, command, additionalArguments);
        }

        public Task<ProcessResult> InvokePackageDirectoryAsync(
            string caseName,
            string packageRoot,
            string command,
            params string[] additionalArguments) =>
            InvokeTargetWithTimeoutAsync(
                caseName,
                packageRoot,
                packageRoot,
                command,
                ProcessTimeout,
                null,
                null,
                additionalArguments);

        public Task<ProcessResult> InvokePackageDirectoryWithEnvironmentAsync(
            string caseName,
            string packageRoot,
            string command,
            IReadOnlyDictionary<string, string> environment,
            params string[] additionalArguments) =>
            InvokeTargetWithTimeoutAsync(
                caseName,
                packageRoot,
                packageRoot,
                command,
                ProcessTimeout,
                null,
                environment,
                additionalArguments);

        public Process StartWebPackageProcess(string caseName, string packageRoot, params string[] applicationArguments)
        {
            return StartWebPackageProcessWithEnvironment(caseName, packageRoot, null, applicationArguments);
        }

        public Process StartWebPackageProcessWithEnvironment(
            string caseName,
            string packageRoot,
            IReadOnlyDictionary<string, string>? environment,
            params string[] applicationArguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = packageRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(compilerDll);
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add(packageRoot);
            startInfo.ArgumentList.Add("--");
            foreach (var argument in applicationArguments)
                startInfo.ArgumentList.Add(argument);
            startInfo.Environment["HOB_DOTNET"] = dotnet;
            RemoveConfigEnvironment(startInfo);
            if (environment is not null)
            {
                foreach (var (name, value) in environment)
                    startInfo.Environment[name] = value;
            }

            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException($"Could not start the managed web process for {caseName}.");
            }

            return process;
        }

        public Process StartBuiltWebArtifactProcess(
            string caseName,
            string artifactPath,
            string workingDirectory,
            params string[] applicationArguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(artifactPath);
            foreach (var argument in applicationArguments)
                startInfo.ArgumentList.Add(argument);
            startInfo.Environment["HOB_DOTNET"] = dotnet;
            startInfo.Environment.Remove("HOB_SQLITE_PATH");

            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException($"Could not start the built web artifact for {caseName}.");
            }
            process.StandardInput.Close();

            return process;
        }

        public Task<ProcessResult> InvokePackageDirectoryWithTimeoutAsync(
            string caseName,
            string packageRoot,
            string command,
            TimeSpan timeout,
            params string[] additionalArguments) =>
            InvokeTargetWithTimeoutAsync(
                caseName,
                packageRoot,
                packageRoot,
                command,
                timeout,
                null,
                null,
                additionalArguments);

        public async Task<ProcessResult> InvokeFileWithTimeoutAsync(string caseName, string sourcePath, string command, TimeSpan timeout, string? dotnetHostOverride, params string[] additionalArguments)
        {
            var sourceDirectory = Path.GetDirectoryName(sourcePath)
                ?? throw new InvalidOperationException($"Source file {sourcePath} has no parent directory.");
            return await InvokeTargetWithTimeoutAsync(
                caseName,
                sourcePath,
                sourceDirectory,
                command,
                timeout,
                dotnetHostOverride,
                null,
                additionalArguments);
        }

        private async Task<ProcessResult> InvokeTargetWithTimeoutAsync(
            string caseName,
            string target,
            string workingDirectory,
            string command,
            TimeSpan timeout,
            string? dotnetHostOverride,
            IReadOnlyDictionary<string, string>? environment,
            params string[] additionalArguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(compilerDll);
            startInfo.ArgumentList.Add(command);
            startInfo.ArgumentList.Add(target);
            foreach (var argument in additionalArguments) startInfo.ArgumentList.Add(argument);
            startInfo.Environment["HOB_DOTNET"] = dotnetHostOverride ?? dotnet;
            RemoveConfigEnvironment(startInfo);
            if (environment is not null)
            {
                foreach (var (name, value) in environment)
                    startInfo.Environment[name] = value;
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException($"Could not start the hob compiler process for {caseName}.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cancellation.Token);
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
                var timedOutStdout = await stdoutTask;
                var timedOutStderr = await stderrTask;
                throw new TimeoutException($"Compiler timed out after {timeout}. stdout=<{timedOutStdout}>; stderr=<{timedOutStderr}>");
            }

            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }

        private static void RemoveConfigEnvironment(ProcessStartInfo startInfo)
        {
            foreach (var name in startInfo.Environment.Keys
                         .Where(name => name.StartsWith("HOB_CONFIG_", StringComparison.OrdinalIgnoreCase))
                         .ToArray())
                startInfo.Environment.Remove(name);
        }
    }

    private sealed record PackageFixture(string Manifest, IReadOnlyDictionary<string, string> SourceFiles);
    private sealed record DiagnosticSnapshot(string Code, string Message, string File, int StartLine, int StartColumn, int EndLine, int EndColumn);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class IntegrationTestSkippedException(string message) : Exception(message)
    {
    }
}
