using System.Diagnostics;
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

internal static class IntegrationTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AotPublishTimeout = TimeSpan.FromMinutes(10);

    public static async Task<int> RunAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            Console.Error.WriteLine("Could not locate lang.slnx and src/Lang/Lang.csproj.");
            return 2;
        }

        var compilerDll = Path.Combine(repositoryRoot, "src", "Lang", "bin", "Release", "net10.0", "lang.dll");
        if (!File.Exists(compilerDll))
        {
            Console.Error.WriteLine($"Compiler build not found: {compilerDll}");
            Console.Error.WriteLine("Build the solution before running the integration harness.");
            return 2;
        }

        var dotnet = GetDotnetPath(repositoryRoot);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "lang-integration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        var harness = new Harness(repositoryRoot, compilerDll, dotnet, temporaryRoot);
        var cases = new (string Name, Func<Harness, Task> Run)[]
        {
            ("pure example prints exactly 50", TestPureExample),
            ("valid JSON checks return an empty diagnostics array", TestJsonValidProgram),
            ("JSON diagnostics have a stable schema, range, and failing exit", TestJsonDiagnostics),
            ("primitive main values preserve exact output", TestPrimitiveMainOutput),
            ("comparison precedence and all comparison operators execute", TestComparisonAndControlFlow),
            ("if conditions, operand types, returns, and scopes are checked", TestInvalidControlFlow),
            ("Text length counts Unicode scalars and trim removes Unicode whitespace", TestUnicodeTextOperations),
            ("lists infer generic items, append immutably, split exactly, and preserve iteration order", TestListRuntime),
            ("list loops, immutable assignment, effects, and resource escapes have exact diagnostics", TestListDiagnostics),
            ("union payload matching executes", TestUnionMatchOutput),
            ("Option and Result values require exhaustive typed matches", TestOptionResult),
            ("struct constructors support nested and chained field reads", TestStructValues),
            ("struct values compose with Option, Result, and unions", TestStructWrappers),
            ("forward and guarded structs work, including empty library builds", TestForwardAndGuardedRecursion),
            ("direct and mutual struct field cycles are rejected", TestStructCycles),
            ("struct field initializers and reads are checked", TestStructFieldDiagnostics),
            ("duplicate and reserved struct names are rejected", TestStructDeclarationNames),
            ("contextual keywords are identifiers only in their grammar contexts", TestContextualIdentifiers),
            ("static GET and POST routes produce checked route IR", TestStaticRouteDeclarations),
            ("route signatures, mappings, codecs, names, and placement are checked", TestRouteContractDiagnostics),
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
            ("inspect effects reports SQLite capabilities and trusted adapters", TestSqliteInspectEffects),
            ("inspect api exports a deterministic source-facing package graph", TestInspectApi),
            ("inspect api projects checked web routes and database capabilities", TestInspectApiWebRoutes),
            ("audit reports a portable package graph and structured failures", TestAuditPackage),
            ("qualified calls carry effects into exact JSON diagnostics", TestQualifiedEffects),
            ("FsError requires an exhaustive typed match", TestFsErrorExhaustiveness),
            ("effectful FsRead libraries build as managed DLLs", TestEffectfulLibraryBuild),
            ("managed build receipts bind checked inputs and artifact bytes", TestStandaloneBuildReceipt),
            ("CLI capability grants are validated and included in dependency lock freshness", TestCliCapabilityManifestAndLock),
            ("same-package CLI package checks, builds, and runs qualified public values", TestPackageCliRoundTrip),
            ("typed CLI commands generate deterministic schema and parse application arguments", TestTypedCliCommandRuntime),
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
            ("library packages build as managed libraries", TestPackageLibraryBuild),
            ("path dependency locks are portable, stable, and required for package commands", TestPathDependencyLockLifecycle),
            ("dependency graphs reject cycles, missing manifests, non-libraries, and duplicate identities", TestDependencyGraphDiagnostics),
            ("dependency aliases enforce direct visibility and preserve module identity", TestDependencyAliasResolution),
            ("dependency references enforce public and existing symbols", TestDependencyReferenceDiagnostics),
            ("dependency source roots and reparse paths stay inside package boundaries", TestDependencyFilesystemSafety),
            ("package NativeAOT arguments are validated", TestPackageAotCommandValidation),
            ("maintained package example runs with exact output", TestMaintainedPackageExample),
            ("maintained web package serves typed routes with bounded request handling", TestMaintainedWebExample),
            ("SQLite transactions commit once and roll back on scope exit and early return", TestSqliteTransactions),
            ("SQLite row decoding and failures are enforced at runtime", TestSqliteRowDecoding),
            ("SQLite manifest paths, grants, and generated dependency are validated", TestSqlitePackageContract),
            ("SQLite library operations build without web database configuration", TestSqliteLibraryBuild),
            ("maintained scan CLI receives FsRead and handles typed file and normalization results", TestScanCliExample),
            ("Text validation package builds and qualified generic calls specialize correctly", TestTextValidationExample),
            ("language tests run the text validation suite with exact output", TestManagedLanguageTests),
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
            ("LANG_DOTNET launch failures become process diagnostics", TestDotnetLaunchFailure),
            ("concurrent runs keep their generated outputs isolated", TestParallelRuns)
        };

        // Set LANG_INTEGRATION_TEST_FILTER to a case-insensitive test-name substring while iterating on one case.
        var filter = Environment.GetEnvironmentVariable("LANG_INTEGRATION_TEST_FILTER");
        var selectedCases = string.IsNullOrWhiteSpace(filter)
            ? cases
            : cases.Where(test => test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selectedCases.Length == 0)
        {
            Console.Error.WriteLine($"No integration test matched LANG_INTEGRATION_TEST_FILTER='{filter}'.");
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
        var source = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "examples", "pure", "src", "main.lang"));
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
                return with_field.with;
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
                ["src/app/main.lang"] = source,
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
        AssertEffectPath(scanReport, "fs.read", "app::scan::run -> fs.read_text");

        var trustedOperations = scanReport.GetProperty("trusted_operations").EnumerateArray().ToArray();
        AssertEqual(3, trustedOperations.Length,
            "The scan report should identify the two CLI host operations and the FsRead adapter.");
        AssertTrustedOperation(trustedOperations[0], "cli.argument_decode", "trusted_host", []);
        AssertTrustedOperation(trustedOperations[1], "cli.output", "trusted_host", []);
        AssertTrustedOperation(trustedOperations[2], "FsRead.read_text", "trusted_adapter", ["fs.read"]);

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
                ["src/app/effects.lang"] = loopSource
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
        AssertTrue(malformedInvocation.StandardError.StartsWith("Usage: lang ", StringComparison.Ordinal)
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
                ["src/app/effects.lang"] = pathsSource
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
                        ["src/app/shared.lang"] = "module app::shared;\n"
                            + "pub fn scan(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {\n"
                            + "    return helper::app::shared::scan(fs);\n"
                            + "}\n"
                    }),
                ["helper"] = new PackageFixture(LibraryPackageManifest("inspect-effect-label-helper"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/shared.lang"] = "module app::shared;\n"
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
                ["src/app/broken.lang"] = invalidSource
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
                        ["src/app/effects.lang"] = "module app::effects; pub fn pure() -> i32 effects {} { return 1; }"
                    }),
                ["validation"] = new PackageFixture(
                    LibraryPackageManifest("inspect-validation"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/validation.lang"] = "module validation; pub fn marker() -> i32 effects {} { return 1; }"
                    })
            });
        var missingLock = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", lockPackage,
            "self::app::effects::pure", "--json");
        AssertInspectLockFailure(missingLock, "A missing dependency lock must stop inspection before a report.");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "inspect-effects-create-lock", lockPackage, "lock"));
        var dependencyFile = Path.GetFullPath(Path.Combine(lockPackage, "..", "validation", "src", "validation.lang"));
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
                    result: Result<direct::records::Record, direct::records::Status>
                }
                pub union ApiReply { Found(direct::records::Record), Empty }
                struct HiddenRoot { note: Text }
                fn hidden_root() -> i32 effects {} { return 1; }

                pub fn generic_root<T>(items: List<Option<T>>) -> Result<Option<T>, direct::records::Status> effects {} {
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
                pub fn run(args: self::app::main::ScanArgs, fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
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
                pub struct Record {
                    id: i32,
                    origin: Option<foundation::models::Origin>,
                    revisions: List<Result<i32, foundation::models::Issue>>
                }
                pub union Status { Empty, Failed(foundation::models::Issue), Record(self::records::Record) }
                struct HiddenDirect { secret: Text }
                fn hidden_direct() -> i32 effects {} { return 2; }
                pub fn wrap<T>(items: List<Option<T>>, outcome: Result<T, foundation::models::Issue>) -> Option<List<Result<T, self::records::Record>>> effects {} {
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
                + "\n[capabilities]\nfs.read = \"allow\"\n"
                + "\n[dependencies]\nzeta = \"../direct\"\ndirect = \"../direct\"\n";
            var directManifest = LibraryPackageManifest("api-direct")
                + "\n[dependencies]\nfoundation = \"../foundation\"\n";

            return new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(WithLineEnding(rootManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.lang"] = WithLineEnding(rootSource, lineEnding),
                    ["src/handlers.lang"] = WithLineEnding(handlersSource, lineEnding)
                }),
                ["direct"] = new PackageFixture(WithLineEnding(directManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/records.lang"] = WithLineEnding(recordsSource, lineEnding),
                    ["src/service.lang"] = WithLineEnding(serviceSource, lineEnding)
                }),
                ["foundation"] = new PackageFixture(
                    WithLineEnding(LibraryPackageManifest("api-foundation"), lineEnding),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/models.lang"] = WithLineEnding(foundationSource, lineEnding),
                        ["src/service.lang"] = WithLineEnding(foundationService, lineEnding)
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
        AssertEqual(1, api.GetProperty("schema_version").GetInt32(), "Inspect-api schema version must be 1.");
        AssertEqual("self", api.GetProperty("package").GetProperty("alias").GetString(),
            "The root package must have a source-facing self alias.");
        var dependencies = api.GetProperty("dependencies").EnumerateArray().ToArray();
        AssertEqual(2, dependencies.Length, "Both direct aliases must remain in the dependency list.");
        AssertEqual("direct", dependencies[0].GetProperty("alias").GetString(), "Dependency aliases must be sorted.");
        AssertEqual("zeta", dependencies[1].GetProperty("alias").GetString(), "Dependency aliases must be sorted.");
        AssertJsonStringArray(api.GetProperty("manifest_grants"), ["fs.read"]);

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
        AssertTrue(functionIds.All(id => !id.Contains("hidden", StringComparison.Ordinal)),
            "Private declarations must not appear in the API function list.");
        var structIds = api.GetProperty("structs").EnumerateArray()
            .Select(structure => structure.GetProperty("id").GetString() ?? string.Empty).ToArray();
        AssertTrue(new[] { "direct::records::Record", "self::app::main::Envelope", "self::app::main::ScanArgs" }
                .SequenceEqual(structIds, StringComparer.Ordinal),
            $"Private root and dependency structs must be filtered, got [{string.Join(", ", structIds)}].");
        var unionIds = api.GetProperty("unions").EnumerateArray()
            .Select(union => union.GetProperty("id").GetString() ?? string.Empty).ToArray();
        AssertTrue(new[] { "direct::records::Status", "self::app::main::ApiReply" }
                .SequenceEqual(unionIds, StringComparer.Ordinal),
            $"Private and transitive unions must be filtered, got [{string.Join(", ", unionIds)}].");
        var wrap = functions.Single(function => function.GetProperty("id").GetString() == "direct::records::wrap");
        AssertJsonStringArray(wrap.GetProperty("source_ids"), ["direct::records::wrap", "zeta::records::wrap"]);
        AssertEqual(0, wrap.GetProperty("type_parameters")[0].GetProperty("ordinal").GetInt32(),
            "Generic type parameter ordinals should be stable and source-facing.");
        var wrapResult = wrap.GetProperty("return_type");
        AssertEqual("option", wrapResult.GetProperty("kind").GetString(), "Generic return types should preserve wrappers.");
        AssertEqual("list", wrapResult.GetProperty("item").GetProperty("kind").GetString(),
            "Nested generic return types should preserve List.");
        AssertEqual("result", wrapResult.GetProperty("item").GetProperty("item").GetProperty("kind").GetString(),
            "Nested generic return types should preserve Result.");
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
                ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return true; }"
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
            fn create(request: self::app::main::Request, db: DbRead, writer: DbWrite) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Empty;
            }
            route POST "/records" {
                body: self::app::main::Request;
                handler: self::app::main::create;
                response Created: 201 json self::app::main::Record;
                response Invalid: 400 json Text;
                response Failed: 500 json Text;
                response Empty: 204;
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "inspect-api-web",
            "name = \"inspect-api-web\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "sqlite_path = \"data/api.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
                + "\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = source,
                ["db/schema.sql"] = "CREATE TABLE records (id INTEGER PRIMARY KEY, name TEXT NOT NULL);\n"
            });
        var result = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, result.ExitCode, Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        using var document = JsonDocument.Parse(result.StandardOutput);
        var api = document.RootElement;
        AssertInspectApiPropertyOrder(api);
        AssertJsonStringArray(api.GetProperty("manifest_grants"), ["db.read", "db.write", "net.listen"]);
        AssertEqual(0, api.GetProperty("commands").GetArrayLength(), "Web packages should not invent CLI commands.");
        var route = api.GetProperty("routes").EnumerateArray().Single();
        AssertEqual("POST", route.GetProperty("method").GetString(), "Route methods should retain checked source values.");
        AssertEqual("/records", route.GetProperty("path").GetString(), "Route paths should retain checked source values.");
        AssertEqual("nominal", route.GetProperty("body_type").GetProperty("kind").GetString(),
            "POST routes should expose their checked body type.");
        AssertEqual("self::app::main::Request", route.GetProperty("body_type").GetProperty("source_id").GetString(),
            "POST route bodies should use source-facing type IDs.");
        AssertEqual("self::app::main::create", route.GetProperty("handler").GetString(),
            "Route handler references should use source-facing IDs.");
        AssertJsonStringArray(route.GetProperty("handler_source_ids"), ["self::app::main::create"]);
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

            fn handler(db: DbRead) -> self::app::main::Reply effects { db.read } {
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
                + "\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n"
                + "\n[dependencies]\nzeta = \"../direct\"\ndirect = \"../direct\"\n";
            var directManifest = LibraryPackageManifest("audit-direct") + "\n[dependencies]\nfoundation = \"../foundation\"\n";
            return new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(WithLineEnding(rootManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/app/main.lang"] = WithLineEnding(rootSource, lineEnding),
                    ["db/schema.sql"] = WithLineEnding(schemaText, lineEnding)
                }),
                ["direct"] = new PackageFixture(WithLineEnding(directManifest, lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/service.lang"] = WithLineEnding(directSource, lineEnding)
                }),
                ["foundation"] = new PackageFixture(WithLineEnding(LibraryPackageManifest("audit-foundation"), lineEnding), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/service.lang"] = WithLineEnding(foundationSource, lineEnding)
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
        AssertEqual(1, report.GetProperty("schema_version").GetInt32(), "Audit schema version must be 1.");
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

        AssertJsonStringArray(report.GetProperty("manifest_grants"), ["db.read", "db.write", "net.listen"]);
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
        AssertEqual("through", rootPublic.GetProperty("direct_calls")[0].GetProperty("name").GetString(),
            "Compiler facts should retain direct cross-package calls.");
        var rootHandler = functions.Single(function => function.GetProperty("name").GetString() == "handler");
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

        var dependencySourcePath = Path.Combine(packageRoot, "..", "direct", "src", "service.lang");
        var dependencySource = await File.ReadAllTextAsync(dependencySourcePath);
        await File.WriteAllTextAsync(dependencySourcePath, dependencySource.Replace("return 2;", "return 3;", StringComparison.Ordinal));
        var staleLock = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertAuditDiagnostic(staleLock, "E_LOCK", "A stale dependency lock should fail audit without a partial report.");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("audit-package-refresh-lock", packageRoot, "lock"));

        var rootSourcePath = Path.Combine(packageRoot, "src", "app", "main.lang");
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

        Order(root, "schema_version,packages,compiler,manifest_grants,trusted_claims,foreign_dependencies");
        foreach (var package in root.GetProperty("packages").EnumerateArray())
        {
            Order(package, "identity,role,content_sha256,dependencies,inputs");
            Order(package.GetProperty("identity"), "name,version,path");
            foreach (var dependency in package.GetProperty("dependencies").EnumerateArray())
            {
                Order(dependency, "alias,package");
                Order(dependency.GetProperty("package"), "name,version,path");
            }
            foreach (var input in package.GetProperty("inputs").EnumerateArray())
                Order(input, "kind,path,sha256");
        }
        Order(root.GetProperty("compiler"), "functions");
        foreach (var function in root.GetProperty("compiler").GetProperty("functions").EnumerateArray())
        {
            Order(function, "package,module,name,visibility,declared_effects,inferred_effects,effect_paths,direct_calls,required_capabilities");
            Order(function.GetProperty("package"), "name,version,path");
            foreach (var path in function.GetProperty("effect_paths").EnumerateArray())
            {
                Order(path, "effect,steps");
                foreach (var step in path.GetProperty("steps").EnumerateArray())
                    Order(step, step.TryGetProperty("package", out _) ? "kind,package,module,name" : "kind,name");
            }
            foreach (var call in function.GetProperty("direct_calls").EnumerateArray())
                Order(call, "package,module,name");
        }
        foreach (var claim in root.GetProperty("trusted_claims").EnumerateArray())
        {
            Order(claim, "operation,source,effects,assurance,reachable_from");
            foreach (var reachable in claim.GetProperty("reachable_from").EnumerateArray())
            {
                Order(reachable, "package,module,name");
                Order(reachable.GetProperty("package"), "name,version,path");
            }
        }
        foreach (var dependency in root.GetProperty("foreign_dependencies").EnumerateArray())
            Order(dependency, "name,version,ecosystem,reason");
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
                    AssertTrue(property.Name is not "id" and not "function_id" and not "package_id",
                        $"Audit JSON should use stable symbolic identities instead of numeric identifiers at {path}.{property.Name}.");
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
            "schema_version,package,dependencies,manifest_grants,functions,structs,unions,commands,routes",
            "alias,name,version",
            "name,version",
            "id,source_ids,package,type_parameters,parameters,return_type,declared_effects,inferred_effects,effect_paths,calls,required_capabilities",
            "name,ordinal",
            "name,type",
            "kind,source_id,source_ids,package,module,name",
            "kind,name",
            "effect,steps",
            "source_id,source_ids,package,module,name",
            "id,source_ids,package,fields",
            "id,source_ids,package,variants",
            "name,payload",
            "id,package,help,inputs,handler,handler_source_ids,error_formatter,error_formatter_source_ids,error_type,required_capabilities",
            "name,kind,type,help,default_value",
            "kind,value",
            "kind,name,ordinal",
            "kind,item",
            "kind,ok,error",
            "kind,declaration_kind,source_id,source_ids,package,module,name",
            "method,path,body_type,handler,response_type,handler_source_ids,responses,required_capabilities,capability_parameters",
            "variant,status,content_type,payload_type",
            "name,capability"
        };

        static void Walk(JsonElement element, HashSet<string> allowedOrders)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = element.EnumerateObject().Select(property => property.Name).ToArray();
                var order = string.Join(",", names);
                AssertTrue(allowedOrders.Contains(order),
                    $"Unexpected inspect-api JSON object property order: [{order}].");
                foreach (var property in element.EnumerateObject()) Walk(property.Value, allowedOrders);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray()) Walk(child, allowedOrders);
            }
        }

        Walk(root, allowedOrders);
    }
    private static void AssertApiPortable(string json, JsonElement api, string temporaryRoot)
    {
        AssertTrue(!json.Contains(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            && !Regex.IsMatch(json, @"[A-Za-z]:\\") && !json.Contains('\\'),
            "The source-facing API must not contain absolute filesystem paths.");

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
            ["src/app/reader.lang"] = wrapperWithoutEffect,
            ["src/io/files.lang"] = library
        };
        var packageRoot = await harness.WritePackageAsync("effect-qualified-exceeded", LibraryPackageManifest(), files);
        var wrapperPath = Path.GetFullPath(Path.Combine(packageRoot, "src", "app", "reader.lang"));
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
                ["src/app/reader.lang"] = wrapperWithEffect,
                ["src/io/files.lang"] = library
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
            pub fn read_file(args: self::app::main::ReadArgs, fs: FsRead) -> Result<Text, FsError> effects { fs.read } {
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
            ["src/app/main.lang"] = source
        };

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "cli-fsread-missing-grant",
            CliPackageManifest(),
            sources,
            "E_CAPABILITY_MISSING",
            "src/app/main.lang");

        var noFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "cli-capability-unknown-manifest",
            CliPackageManifest() + "\n[capabilities]\nnet.client = \"allow\"\n",
            noFiles,
            "E_MANIFEST",
            "lang.toml");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "cli-capability-wrong-grant-manifest",
            CliPackageManifest() + "\n[capabilities]\nfs.read = \"deny\"\n",
            noFiles,
            "E_MANIFEST",
            "lang.toml");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "cli-capability-duplicate-grant-manifest",
            CliPackageManifest() + "\n[capabilities]\nfs.read = \"allow\"\nfs.read = \"allow\"\n",
            noFiles,
            "E_MANIFEST",
            "lang.toml");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "library-capability-grant-manifest",
            LibraryPackageManifest() + "\n[capabilities]\nfs.read = \"allow\"\n",
            noFiles,
            "E_MANIFEST",
            "lang.toml");

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
            CliPackageManifest() + "\n[capabilities]\nfs.read = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = argsOnlySource
            });
        var unusedGrantBuild = await harness.InvokePackageDirectoryAsync(
            "cli-fsread-grant-not-used-build", argsOnlyRoot, "build");
        AssertEqual(0, unusedGrantBuild.ExitCode, Describe(unusedGrantBuild));
        var unusedGrantArtifact = unusedGrantBuild.StandardOutput["Built executable: ".Length..].Trim();
        var unusedGrantSchemaPath = Path.Combine(Path.GetDirectoryName(unusedGrantArtifact)!, "command-schema.json");
        using (var unusedGrantSchema = JsonDocument.Parse(await File.ReadAllBytesAsync(unusedGrantSchemaPath)))
        {
            AssertEqual(2, unusedGrantSchema.RootElement.GetProperty("schema_version").GetInt32(),
                "Command schemas with the capabilities field must use version 2.");
            AssertEqual(0, unusedGrantSchema.RootElement.GetProperty("commands")[0]
                    .GetProperty("capabilities").GetArrayLength(),
                "A manifest grant unused by an args-only handler must not appear as an injected capability.");
        }

        const string withGrant = "name = \"harness-package\"\n"
            + "version = \"0.1.0\"\n"
            + "kind = \"cli\"\n"
            + "source_root = \"src\"\n"
            + "entry_module = \"app::main\"\n"
            + "\n[capabilities]\nfs.read = \"allow\"\n"
            + "\n[dependencies]\nvalidation = \"../validation\"\n";
        const string withoutGrant = "name = \"harness-package\"\n"
            + "version = \"0.1.0\"\n"
            + "kind = \"cli\"\n"
            + "source_root = \"src\"\n"
            + "entry_module = \"app::main\"\n"
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
                        ["src/validation.lang"] = "module validation; pub fn marker() -> i32 effects {} { return 1; }"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "cli-fsread-grant-lock-create", graphRoot, "lock"));
        var rootManifestPath = Path.Combine(graphRoot, "lang.toml");
        var lockPath = Path.Combine(graphRoot, "lang.lock");
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
            var moduleType = assembly.GetType("LangModule", throwOnError: true)!;
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
        AssertTrue(firstInputHash != changedRoot.GetProperty("inputs")[0].GetProperty("sha256").GetString(),
            "Changing standalone source content must change the checked input hash.");
        AssertTrue(firstAuditHash != changedRoot.GetProperty("audit_snapshot_sha256").GetString(),
            "Changing standalone source content must change the audit snapshot hash.");
    }

    private static async Task TestPackageCliRoundTrip(Harness harness)
    {
        var packageRoot = await harness.WritePackageAsync(
            "package-cli-roundtrip",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/catalog/message.lang"] = """
                    module catalog::message;

                    pub struct Greeting { text: Text }
                    pub union Message { Ready(self::catalog::message::Greeting), Missing }

                    pub fn greeting() -> self::catalog::message::Greeting effects {} {
                        return self::catalog::message::Greeting { text: "ready" };
                    }
                    """,
                ["src/app/main.lang"] = """
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
                ["src/app/main.lang"] = main,
                ["src/handlers.lang"] = handlers
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
            AssertEqual(2, root.GetProperty("schema_version").GetInt32(), "The command schema version must be 2 after adding capabilities.");
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
                ["src/app/main.lang"] = main,
                ["src/handlers.lang"] = handlers
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
        AssertTrue(legacyMainWithSeparator.StandardError.StartsWith("Usage: lang ", StringComparison.Ordinal),
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
                    ["src/app/main.lang"] = mainSource,
                    ["src/handlers.lang"] = handlerSource
                },
                code,
                "src/app/main.lang");

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
                ["src/app/main.lang"] = main,
                ["src/handlers.lang"] = handlers
            },
            "E_COMMAND_DECL",
            "src/app/main.lang");

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
                ["src/app/main.lang"] = main,
                ["src/handlers.lang"] = wrongHandler
            },
            "E_COMMAND_HANDLER",
            "src/app/main.lang",
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
                ["src/app/main.lang"] = malformed,
                ["src/handlers.lang"] = handlers
            });
        var parserFailure = await harness.InvokePackageDirectoryAsync("typed-cli-malformed-parser-check", malformedPackage, "check", "--json");
        AssertTrue(parserFailure.ExitCode != 0, Describe(parserFailure));
        using var parserDocument = JsonDocument.Parse(parserFailure.StandardOutput);
        AssertTrue(parserDocument.RootElement.GetProperty("diagnostics").EnumerateArray().Any(diagnostic =>
            diagnostic.GetProperty("code").GetString() == "E_COMMAND_DECL"
            && Path.GetFullPath(diagnostic.GetProperty("file").GetString() ?? string.Empty)
                == Path.GetFullPath(Path.Combine(malformedPackage, "src", "app", "main.lang"))),
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
                ["src/first.lang"] = first,
                ["src/second.lang"] = second,
                ["src/app/main.lang"] = main
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
                ["src/library.lang"] = hiddenLibrary,
                ["src/app/main.lang"] = """
                    module app::main;
                    fn read_hidden(value: self::library::HiddenStruct) -> i32 effects {} { return value.value; }
                    pub fn main() -> i32 effects {} { return 0; }
                    """
            },
            "E_ACCESS_PRIVATE",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "private-struct-qualified-construction",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.lang"] = hiddenLibrary,
                ["src/app/main.lang"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} {
                        return self::library::HiddenStruct { value: 7 }.value;
                    }
                    """
            },
            "E_ACCESS_PRIVATE",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "private-union-qualified-constructor",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.lang"] = hiddenLibrary,
                ["src/app/main.lang"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} {
                        let value: i32 = self::library::HiddenUnion.Value(7);
                        return value;
                    }
                    """
            },
            "E_ACCESS_PRIVATE",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "private-union-qualified-pattern",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.lang"] = hiddenLibrary,
                ["src/app/main.lang"] = """
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
            "src/app/main.lang");
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
                ["src/library.lang"] = library,
                ["src/app/main.lang"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} { return self::library::hidden(); }
                    """
            },
            "E_ACCESS_PRIVATE",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-qualified-unknown-module",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} { return self::absent::value(); }
                    """
            },
            "E_NAME_UNRESOLVED",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-qualified-unknown-declaration",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.lang"] = library,
                ["src/app/main.lang"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} { return self::library::absent(); }
                    """
            },
            "E_NAME_UNRESOLVED",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-unqualified-user-function",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = """
                    module app::main;
                    fn helper() -> i32 effects {} { return 7; }
                    pub fn main() -> i32 effects {} { return helper(); }
                    """
            },
            "E_NAME_UNRESOLVED",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-unqualified-user-type",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = """
                    module app::main;
                    struct Value { value: i32 }
                    pub fn main(item: Value) -> i32 effects {} { return item.value; }
                    """
            },
            "E_NAME_UNRESOLVED",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-legacy-import-rejected",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = """
                    module app::main;
                    import library { present };
                    pub fn main() -> i32 effects {} { return 1; }
                    """
            },
            "E_SYNTAX",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-dotted-module-rejected",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = """
                    module app.main;
                    pub fn main() -> i32 effects {} { return 1; }
                    """
            },
            "E_SYNTAX",
            "src/app/main.lang");
    }

    private static async Task TestPackageManifestDiagnostics(Harness harness)
    {
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-missing-entry",
            "name = \"manifest-test\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "lang.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-dotted-entry",
            CliPackageManifest().Replace("entry_module = \"app::main\"", "entry_module = \"app.main\"", StringComparison.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "lang.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-reserved-self-alias",
            CliPackageManifest() + "\n[dependencies]\nself = \"../library\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "lang.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-unknown-key",
            CliPackageManifest() + "dependencies = \"none\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "lang.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-manifest-duplicate-key",
            CliPackageManifest() + "kind = \"lib\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "E_MANIFEST",
            "lang.toml");

        foreach (var reservedPath in new[] { "../CON.lib", "../NUL" })
        {
            await ExpectPackageJsonDiagnosticAsync(
                harness,
                $"package-manifest-reserved-dependency-path-{reservedPath.Replace('/', '-').Replace('.', '-')}",
                CliPackageManifest() + $"\n[dependencies]\nvalidation = \"{reservedPath}\"\n",
                new Dictionary<string, string>(StringComparer.Ordinal),
                "E_MANIFEST",
                "lang.toml");
        }

        var ordinaryDeviceLikePaths = await harness.WritePackageAsync(
            "package-manifest-device-like-dependency-paths",
            CliPackageManifest() + "\n[dependencies]\nconsole = \"../CONSOLE\"\nnullish = \"../NULL\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
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
            "lang.toml");

        var commentManifest = CliPackageManifest()
            .Replace("version = \"0.1.0\"", "version = \"0.1.0#candidate\"", StringComparison.Ordinal)
            .Replace("kind = \"cli\"", "kind = \"cli\" # CLI package", StringComparison.Ordinal);
        var commentPackage = await harness.WritePackageAsync(
            "package-manifest-comments",
            commentManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
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
                ["src/app/main.lang"] = """
                    module elsewhere::main;
                    pub fn main() -> i32 effects {} { return 1; }
                    """
            },
            "E_MODULE_PATH",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-module-dotted-filename",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/text.validation.lang"] = "module text::validation; pub fn main() -> i32 effects {} { return 1; }"
            },
            "E_MODULE_PATH",
            "src/text.validation.lang");
    }

    private static async Task TestPackageEntryPointDiagnostics(Harness harness)
    {
        await ExpectPackageTextDiagnosticAsync(
            harness,
            "package-entry-module-missing",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/other.lang"] = "module other; pub fn main() -> i32 effects {} { return 4; }"
            },
            "run",
            "E_ENTRYPOINT",
            "lang.toml");

        await ExpectPackageTextDiagnosticAsync(
            harness,
            "package-entry-main-missing",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = "module app::main; pub fn helper() -> i32 effects {} { return 1; }",
                ["src/other.lang"] = "module other; pub fn main() -> i32 effects {} { return 4; }"
            },
            "run",
            "E_ENTRYPOINT",
            "src/app/main.lang");

        await ExpectPackageTextDiagnosticAsync(
            harness,
            "package-entry-main-invalid",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = "module app::main; pub fn main(value: i32) -> i32 effects {} { return value; }"
            },
            "run",
            "E_ENTRYPOINT",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-entry-main-duplicate",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} { return 1; }
                    pub fn main() -> i32 effects {} { return 2; }
                    """
            },
            "E_NAME_DUPLICATE",
            "src/app/main.lang");
    }

    private static async Task TestPackageEntrySelection(Harness harness)
    {
        var result = await harness.InvokePackageAsync(
            "package-entry-selection",
            "run",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 17; }",
                ["src/other.lang"] = "module other; pub fn main() -> i32 effects {} { return 99; }"
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
                ["src/shared/choice.lang"] = """
                    module shared::choice;
                    pub union Choice { First, Second, Third }
                    """,
                ["src/app/main.lang"] = """
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
            "src/app/main.lang");
    }

    private static async Task TestPackageQualifiedReferencesCrossModules(Harness harness)
    {
        var packageRoot = await harness.WritePackageAsync(
            "package-qualified-cross-module",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/base.lang"] = "module base; pub fn answer() -> i32 effects {} { return 42; }",
                ["src/middle.lang"] = """
                    module middle;
                    pub fn wrapped() -> i32 effects {} { return self::base::answer(); }
                    """,
                ["src/app/main.lang"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} { return self::middle::wrapped() + self::base::answer(); }
                    """
            });
        var check = await harness.InvokePackageDirectoryAsync("package-qualified-cross-module-check", packageRoot, "check");
        AssertEqual(0, check.ExitCode, Describe(check));
        var run = await harness.InvokePackageDirectoryAsync("package-qualified-cross-module-run", packageRoot, "run");
        AssertRunOutput("84" + Environment.NewLine, run);
    }

    private static async Task TestPackageLibraryBuild(Harness harness)
    {
        var packageRoot = await harness.WritePackageAsync(
            "package-library-build",
            LibraryPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/core/math.lang"] = "module core::math; pub fn square(value: i32) -> i32 effects {} { return value * value; }"
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
                    ["src/app/main.lang"] = consumerSource
                }),
                ["validation"] = new PackageFixture(LibraryPackageManifest("text-validation"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.lang"] = validationSource
                })
            });
        var lockPath = Path.Combine(packageRoot, "lang.lock");

        await AssertPathDependencyLockRequiredAsync(harness, "path-dependency-missing-lock", packageRoot);

        var create = await harness.InvokePackageDirectoryAsync("path-dependency-lock-create", packageRoot, "lock");
        AssertEqual(0, create.ExitCode, Describe(create));
        AssertTrue(File.Exists(lockPath), "lang lock should create lang.lock for a package with dependencies.");
        var firstLockBytes = await File.ReadAllBytesAsync(lockPath);
        AssertTrue(!firstLockBytes.Contains((byte)'\r'), "lang.lock must use LF line endings without carriage returns.");
        AssertTrue(firstLockBytes.Length > 0
            && firstLockBytes[^1] == (byte)'\n'
            && (firstLockBytes.Length == 1 || firstLockBytes[^2] != (byte)'\n'),
            "lang.lock must end with exactly one LF byte.");
        var firstLock = Encoding.UTF8.GetString(firstLockBytes);
        using (var document = JsonDocument.Parse(firstLock))
        {
            var root = document.RootElement.GetProperty("root");
            AssertEqual("../validation", root.GetProperty("dependencies").GetProperty("validation").GetString(),
                "The root dependency path must be portable and relative to the package root.");
            var packages = document.RootElement.GetProperty("packages");
            AssertEqual(1, packages.GetArrayLength(), "The lock should contain the resolved validation package.");
            var dependencyPath = packages[0].GetProperty("path").GetString() ?? string.Empty;
            AssertEqual("../validation", dependencyPath, "Package paths in lang.lock should remain relative.");
            AssertTrue(!Path.IsPathRooted(dependencyPath), "A lock package path must not be absolute.");
        }
        AssertTrue(!firstLock.Contains(packageRoot, StringComparison.OrdinalIgnoreCase)
            && !firstLock.Contains(harness.TemporaryRoot, StringComparison.OrdinalIgnoreCase),
            "The lock must not contain an absolute workspace path.");

        var repeat = await harness.InvokePackageDirectoryAsync("path-dependency-lock-repeat", packageRoot, "lock");
        AssertEqual(0, repeat.ExitCode, Describe(repeat));
        var repeatedLockBytes = await File.ReadAllBytesAsync(lockPath);
        AssertTrue(firstLockBytes.SequenceEqual(repeatedLockBytes), "Repeated lang lock should be byte-for-byte deterministic.");

        await AssertDependencyConsumerWorksAsync(harness, "path-dependency-initial", packageRoot, "ready" + Environment.NewLine);

        File.Delete(lockPath);
        await AssertPathDependencyLockRequiredAsync(harness, "path-dependency-deleted-lock", packageRoot);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("path-dependency-relock-missing", packageRoot, "lock"));
        await AssertPackageCheckPassesAsync(harness, "path-dependency-missing-lock-repaired", packageRoot);

        await File.WriteAllTextAsync(lockPath, "{\"schema_version\":99}\n");
        await AssertPathDependencyLockRequiredAsync(harness, "path-dependency-malformed-lock", packageRoot);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("path-dependency-relock-malformed", packageRoot, "lock"));
        await AssertPackageCheckPassesAsync(harness, "path-dependency-malformed-lock-repaired", packageRoot);

        var dependencySourcePath = Path.Combine(packageRoot, "..", "validation", "src", "text", "validation.lang");
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
                ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 42; }"
            });
        var noDependencies = await harness.InvokePackageDirectoryAsync("dependency-free-lock", dependencyFree, "lock");
        AssertEqual(0, noDependencies.ExitCode, Describe(noDependencies));
        AssertTrue(!File.Exists(Path.Combine(dependencyFree, "lang.lock")),
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
                        ["src/root.lang"] = "module root; pub fn value() -> i32 effects {} { return 1; }"
                    }),
                ["next"] = new PackageFixture(
                    LibraryPackageManifest("cycle-next") + "\n[dependencies]\nback = \"../root\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/next.lang"] = "module next; pub fn value() -> i32 effects {} { return 2; }"
                    })
            });
        await AssertPackageDiagnosticAsync(harness, "dependency-cycle-check", cycleRoot, "E_DEPENDENCY", "../next/lang.toml");

        var missingManifestRoot = await harness.WritePackageGraphAsync(
            "dependency-missing-manifest",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\nmissing = \"../missing\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
                    })
            });
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(missingManifestRoot)!, "missing"));
        await AssertPackageDiagnosticAsync(harness, "dependency-missing-manifest-check", missingManifestRoot, "E_DEPENDENCY", "lang.toml");

        var cliDependencyRoot = await harness.WritePackageGraphAsync(
            "dependency-must-be-library",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\napp = \"../app-dependency\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
                    }),
                ["app-dependency"] = new PackageFixture(
                    CliPackageManifest(),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 2; }"
                    })
            });
        await AssertPackageDiagnosticAsync(harness, "dependency-cli-child-check", cliDependencyRoot, "E_DEPENDENCY", "lang.toml");

        const string duplicateManifest = "name = \"duplicate-library\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n";
        var duplicateIdentityRoot = await harness.WritePackageGraphAsync(
            "dependency-duplicate-name-version",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\nleft = \"../left\"\nright = \"../right\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
                    }),
                ["left"] = new PackageFixture(duplicateManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/left.lang"] = "module left; pub fn value() -> i32 effects {} { return 1; }"
                    }),
                ["right"] = new PackageFixture(duplicateManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/right.lang"] = "module right; pub fn value() -> i32 effects {} { return 2; }"
                    })
            });
        await AssertPackageDiagnosticAsync(harness, "dependency-duplicate-identity-check", duplicateIdentityRoot, "E_DEPENDENCY", "lang.toml");
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
                        ["src/app/main.lang"] = "module app::main;\n"
                            + "pub fn load_from_alpha(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return alpha::text::validation::load(fs); }\n"
                            + "pub fn main() -> i32 effects {} { return alpha::text::validation::read_alpha(alpha::text::validation::alpha_token()) + beta::text::validation::read_beta(beta::text::validation::beta_token()); }\n"
                    }),
                ["alpha"] = new PackageFixture(LibraryPackageManifest("alpha-library"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.lang"] = "module text::validation;\n"
                        + "pub struct Token { value: i32 }\n"
                        + "pub fn alpha_token() -> self::text::validation::Token effects {} { return self::text::validation::Token { value: 20 }; }\n"
                        + "pub fn read_alpha(value: self::text::validation::Token) -> i32 effects {} { return value.value + 1; }\n"
                        + "pub fn load(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n"
                }),
                ["beta"] = new PackageFixture(LibraryPackageManifest("beta-library"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.lang"] = "module text::validation;\n"
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
                        ["src/app/main.lang"] = "module app::main;\n"
                            + "pub fn main() -> i32 effects {} { return alpha::text::validation::read_alpha(beta::text::validation::beta_token()); }\n"
                    }),
                ["alpha"] = new PackageFixture(LibraryPackageManifest("identity-alpha"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.lang"] = "module text::validation; pub struct Token { value: i32 }\n"
                        + "pub fn alpha_token() -> self::text::validation::Token effects {} { return self::text::validation::Token { value: 1 }; }\n"
                        + "pub fn read_alpha(value: self::text::validation::Token) -> i32 effects {} { return value.value; }\n"
                }),
                ["beta"] = new PackageFixture(LibraryPackageManifest("identity-beta"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.lang"] = "module text::validation; pub struct Token { value: i32 }\n"
                        + "pub fn beta_token() -> self::text::validation::Token effects {} { return self::text::validation::Token { value: 2 }; }\n"
                })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-cross-identity-lock", crossIdentityRoot, "lock"));
        await AssertPackageDiagnosticAsync(harness, "dependency-cross-identity-check", crossIdentityRoot, "E_TYPE_MISMATCH", "src/app/main.lang");

        var transitiveRoot = await harness.WritePackageGraphAsync(
            "dependency-alias-direct-only",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"direct-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                    + "\n[dependencies]\nmid = \"../mid\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app::main;\n"
                            + "pub fn main() -> i32 effects {} { return mid::mid::api::wrapped() + foundation::foundation::answer(); }\n"
                    }),
                ["mid"] = new PackageFixture(LibraryPackageManifest("middle-library") + "\n[dependencies]\nfoundation = \"../foundation\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/mid/api.lang"] = "module mid::api; pub fn wrapped() -> i32 effects {} { return foundation::foundation::answer(); }"
                    }),
                ["foundation"] = new PackageFixture(LibraryPackageManifest("foundation-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/foundation.lang"] = "module foundation; pub fn answer() -> i32 effects {} { return 21; }"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-transitive-lock", transitiveRoot, "lock"));
        await AssertPackageDiagnosticAsync(harness, "dependency-transitive-alias-check", transitiveRoot, "E_NAME_UNRESOLVED", "src/app/main.lang");
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
                        ["src/app/main.lang"] = "module app::main;\n"
                            + "pub fn main() -> i32 effects {} { return validation::text::validation::hidden() + validation::text::validation::absent(); }\n"
                    }),
                ["validation"] = new PackageFixture(LibraryPackageManifest("visibility-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/text/validation.lang"] = "module text::validation;\n"
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
                        ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 42::text::validation::present(); }"
                    }),
                ["validation"] = new PackageFixture(LibraryPackageManifest("malformed-reference-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/text/validation.lang"] = "module text::validation; pub fn present() -> i32 effects {} { return 1; }"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-malformed-reference-lock", malformedAliasRoot, "lock"));
        await AssertPackageDiagnosticAsync(harness, "dependency-malformed-reference-check", malformedAliasRoot, "E_SYNTAX", "src/app/main.lang");
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
                        ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 1; }"
                    }),
                ["validation"] = new PackageFixture(
                    "name = \"escaped-source-root\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"../outside\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal))
            });
        await AssertPackageDiagnosticAsync(harness, "dependency-source-root-escape-check", invalidSourceRoot, "E_MANIFEST", "../validation/lang.toml");

        var symlinkPackage = await harness.WritePackageAsync(
            "package-source-root-symlink",
            CliPackageManifest().Replace("source_root = \"src\"", "source_root = \"src/link\"", StringComparison.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));
        var sourceDirectoryLink = Path.Combine(symlinkPackage, "src", "link");
        var externalSourceDirectory = Path.Combine(harness.TemporaryRoot, "external-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(externalSourceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(externalSourceDirectory, "main.lang"),
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

        await AssertPackageDiagnosticAsync(harness, "package-source-root-symlink-check", symlinkPackage, "E_MANIFEST", "lang.toml");
    }

    private static async Task TestPackageAotCommandValidation(Harness harness)
    {
        const string main = "module app::main; pub fn main() -> i32 effects {} { return 41; }";
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.lang"] = main
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
                ["src/library.lang"] = "module library; pub fn value() -> i32 effects {} { return 1; }"
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
        var manifest = await File.ReadAllTextAsync(Path.Combine(packageRoot, "lang.toml"));
        var normalizedManifest = manifest.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var source = await File.ReadAllTextAsync(Path.Combine(packageRoot, "src", "app", "main.lang"));
        AssertTrue(normalizedManifest.Contains("[dependencies]\nvalidation = \"../text-validation\"", StringComparison.Ordinal),
            "The maintained CLI package must resolve validation from its sibling package path.");
        AssertTrue(source.Contains("validation::text::validation::require", StringComparison.Ordinal),
            "The maintained CLI must use qualified validation and inferred generic APIs through its dependency alias.");
        AssertTrue(!File.Exists(Path.Combine(packageRoot, "src", "text", "validation.lang")),
            "The consumer must not contain a copied validation source module.");
        AssertTrue(File.Exists(Path.Combine(packageRoot, "..", "text-validation", "src", "text", "validation.lang")),
            "The imported validation source should live in the sibling package.");
        AssertTrue(File.Exists(Path.Combine(packageRoot, "lang.lock")),
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
        var manifest = await File.ReadAllTextAsync(Path.Combine(packageRoot, "lang.toml"));
        var normalizedManifest = manifest.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        AssertTrue(normalizedManifest.Contains("kind = \"web\"", StringComparison.Ordinal),
            "The maintained web sample must declare kind=web.");
        AssertTrue(normalizedManifest.Contains("entry_module = \"app::main\"", StringComparison.Ordinal),
            "The maintained web sample must declare its route entry module.");
        AssertTrue(normalizedManifest.Contains("sqlite_path = \"data/greeting.sqlite3\"", StringComparison.Ordinal)
            && normalizedManifest.Contains("sqlite_schema = \"db/schema.sql\"", StringComparison.Ordinal),
            "The maintained web sample must declare package-relative SQLite paths.");
        AssertTrue(normalizedManifest.Contains(
                "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"",
                StringComparison.Ordinal),
            "The maintained web sample must explicitly grant listening and both database capabilities.");
        AssertTrue(normalizedManifest.Contains("[dependencies]\nvalidation = \"../text-validation\"", StringComparison.Ordinal),
            "The maintained web sample must consume the shared validation package.");
        AssertTrue(File.Exists(Path.Combine(packageRoot, "lang.lock")),
            "The maintained web sample must include its path dependency lockfile.");
        var schemaPath = Path.Combine(packageRoot, "db", "schema.sql");
        AssertTrue(File.Exists(schemaPath), "The maintained web sample must include its SQLite schema file.");
        var schema = await File.ReadAllTextAsync(schemaPath);
        AssertTrue(schema.Contains("CREATE TABLE IF NOT EXISTS greeting", StringComparison.Ordinal)
            && schema.Contains("ON CONFLICT(id) DO NOTHING", StringComparison.Ordinal),
            "The maintained SQLite schema and seed data must be safe to apply on every startup.");

        var sourcePath = Path.Combine(packageRoot, "src", "app", "main.lang");
        var source = await File.ReadAllTextAsync(sourcePath);
        AssertTrue(source.Contains("html.document", StringComparison.Ordinal)
            && source.Contains("html.heading", StringComparison.Ordinal)
            && source.Contains("html.paragraph", StringComparison.Ordinal)
            && source.Contains("html.concat", StringComparison.Ordinal),
            "The maintained web page must use the safe Html builder API.");
        AssertTrue(source.Contains("validation::text::validation::normalize", StringComparison.Ordinal),
            "The POST handler must normalize its request through the shared validation library.");
        AssertTrue(source.Contains("db.query_one", StringComparison.Ordinal)
            && source.Contains("tx.execute", StringComparison.Ordinal)
            && source.Contains("DbError.Statement", StringComparison.Ordinal)
            && source.Contains("DbError.RowShape", StringComparison.Ordinal)
            && source.Contains("route GET \"/health\"", StringComparison.Ordinal),
            "The maintained web sample must read SQLite, write in a transaction, match typed database errors, and declare /health.");

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
            AssertTrue(paths.TryGetProperty("/reset", out var resetPath)
                && resetPath.TryGetProperty("get", out var resetOperation)
                && resetOperation.GetProperty("responses").TryGetProperty("205", out var resetResponse)
                && !resetResponse.TryGetProperty("content", out _),
                "OpenAPI 205 responses should not advertise a response body.");
            AssertTrue(paths.TryGetProperty("/health", out _),
                "OpenAPI should include the ordinary declared health route.");
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
                ["src/app/main.lang"] = """
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
            "lang.toml");

        var aot = await harness.InvokePackageDirectoryAsync(
            "maintained-web-aot-rejected", packageRoot, "build", "--aot", "--rid", CurrentHostAotRid());
        AssertTrue(aot.ExitCode != 0, Describe(aot));
        AssertEqual(string.Empty, aot.StandardOutput, Describe(aot));
        AssertTrue(aot.StandardError.Contains("E_BUILD_TARGET", StringComparison.Ordinal),
            $"Web packages must be rejected before Native AOT publishing. {Describe(aot)}");

        var databasePath = Path.Combine(harness.TemporaryRoot, "maintained-web.sqlite3");
        var databaseEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LANG_SQLITE_PATH"] = databasePath
        };
        var port = GetUnusedLoopbackPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}");
        var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(4) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "maintained-web-run", packageRoot, databaseEnvironment, "--urls", baseAddress.ToString().TrimEnd('/'));
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
                AssertTrue(html.Contains("Hello from lang", StringComparison.Ordinal),
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

            using (var get = await client.GetAsync("/api/greeting"))
            {
                AssertEqual(HttpStatusCode.OK, get.StatusCode, "The GET JSON route should return its mapped 200 status.");
                AssertEqual("application/json", get.Content.Headers.ContentType?.MediaType,
                    "The GET route should set an application/json content type.");
                using var greeting = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
                AssertEqual("Hello from lang", greeting.RootElement.GetProperty("message").GetString(),
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

            using (var invalid = await client.PostAsync("/api/greeting", JsonBody(
                       "{\"name\":\"  \",\"profile\":{\"city\":\"Paris\"}}")))
                AssertEqual(HttpStatusCode.BadRequest, invalid.StatusCode,
                    "A validation failure should select the mapped Invalid status.");

            using (var malformed = await client.PostAsync("/api/greeting", JsonBody("{broken")))
                AssertEqual(HttpStatusCode.BadRequest, malformed.StatusCode,
                    "Malformed JSON should be rejected with 400.");

            using (var wrongShape = await client.PostAsync("/api/greeting", JsonBody(
                       "{\"name\":42,\"profile\":{\"city\":\"Paris\"}}")))
                AssertEqual(HttpStatusCode.BadRequest, wrongShape.StatusCode,
                    "A JSON body with an incompatible field type should be rejected with 400.");

            using (var unknownField = await client.PostAsync("/api/greeting", JsonBody(
                       "{\"name\":\"Ada\",\"profile\":{\"city\":\"Paris\"},\"extra\":true}")))
                AssertEqual(HttpStatusCode.BadRequest, unknownField.StatusCode,
                    "A JSON body with an unknown field should be rejected with 400.");

            using (var duplicateField = await client.PostAsync("/api/greeting", JsonBody(
                       "{\"name\":\"Ada\",\"name\":\"Grace\",\"profile\":{\"city\":\"Paris\"}}")))
                AssertEqual(HttpStatusCode.BadRequest, duplicateField.StatusCode,
                    "A JSON body with a duplicate field should be rejected with 400.");

            using (var missingField = await client.PostAsync("/api/greeting", JsonBody(
                       "{\"name\":\"Ada\"}")))
                AssertEqual(HttpStatusCode.BadRequest, missingField.StatusCode,
                    "A JSON body missing a required nested field should be rejected with 400.");

            using (var nestedWrongShape = await client.PostAsync("/api/greeting", JsonBody(
                       "{\"name\":\"Ada\",\"profile\":{\"city\":42}}")))
                AssertEqual(HttpStatusCode.BadRequest, nestedWrongShape.StatusCode,
                    "A nested JSON field with an incompatible type should be rejected with 400.");

            var boundaryPrefix = "{\"name\":\"Ada\",\"profile\":{\"city\":\"Paris\"}}";
            var exactlyOneMiB = boundaryPrefix + new string(' ', 1_048_576 - Encoding.UTF8.GetByteCount(boundaryPrefix));
            using (var boundary = await client.PostAsync("/api/greeting", JsonBody(exactlyOneMiB)))
                AssertEqual(HttpStatusCode.Created, boundary.StatusCode,
                    "A valid request body exactly at the 1 MiB limit should be accepted.");

            var oversizedJson = "{\"name\":\"" + new string('x', 1_048_600)
                + "\",\"profile\":{\"city\":\"Paris\"}}";
            using (var oversized = await client.PostAsync("/api/greeting", JsonBody(oversizedJson)))
                AssertEqual((HttpStatusCode)413, oversized.StatusCode,
                    "A request body over the managed host's 1 MiB limit should return 413.");

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
                using var chunkedOversized = await client.SendAsync(chunkedRequest);
                AssertEqual((HttpStatusCode)413, chunkedOversized.StatusCode,
                    "A chunked body over the managed host's 1 MiB limit should return 413.");
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
            AssertEqual("Hello from lang", directResponse.RootElement.GetProperty("message").GetString(),
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

    }

    private static async Task TestSqliteTransactions(Harness harness)
    {
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
            """;
        const string manifest = "name = \"sqlite-transaction-runtime\"\n"
            + "version = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
            + "sqlite_path = \"data/transactions.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
            + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n";
        var packageRoot = await harness.WritePackageAsync(
            "sqlite-transaction-runtime",
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = source,
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
                ["src/app/main.lang"] = "module app::main;\n"
                    + "fn invalid(db: DbWrite) -> bool effects { db.write } {\n"
                    + "    with db.begin() as tx { let alias: Transaction = tx; return true; }\n}\n"
            },
            "E_RESOURCE_ESCAPE",
            "src/app/main.lang");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-transaction-argument-escape",
            "name = \"sqlite-transaction-argument-escape\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = "module app::main;\n"
                    + "fn accept(tx: Transaction) -> bool effects {} { return true; }\n"
                    + "fn invalid(db: DbWrite) -> bool effects { db.write } {\n"
                    + "    with db.begin() as tx { return self::app::main::accept(tx); }\n}\n"
            },
            "E_RESOURCE_ESCAPE",
            "src/app/main.lang");
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-transaction-store-escape",
            "name = \"sqlite-transaction-store-escape\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = "module app::main;\n"
                    + "struct Holder { transaction: Transaction }\n"
                    + "fn invalid(db: DbWrite) -> bool effects { db.write } {\n"
                    + "    with db.begin() as tx {\n"
                    + "        let holder: self::app::main::Holder = self::app::main::Holder { transaction: tx };\n"
                    + "        return true;\n"
                    + "    }\n}\n"
            },
            "E_RESOURCE_ESCAPE",
            "src/app/main.lang");

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
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["LANG_SQLITE_PATH"] = databasePath };
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
                new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = sourceText },
                "E_RESOURCE_ESCAPE",
                "src/app/main.lang");
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
                new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = sourceText },
                "E_DB_TRANSACTION_STATEMENT",
                "src/app/main.lang");
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
                ["src/app/main.lang"] = source,
                ["db/schema.sql"] = schema
            });

        var check = await harness.InvokePackageDirectoryAsync("sqlite-row-decoding-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var databasePath = Path.Combine(harness.TemporaryRoot, "sqlite-row-decoding.sqlite3");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["LANG_SQLITE_PATH"] = databasePath };
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
                    ["src/app/main.lang"] = SqliteReadQuerySource("INSERT INTO records (id, name, note) VALUES (3, 'Injected', NULL) RETURNING id"),
                    ["db/schema.sql"] = schema
                },
                "E_DB_READ_STATEMENT",
                "src/app/main.lang");

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
            ["src/app/main.lang"] = sqliteHandler,
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
            "lang.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-path-escape",
            "name = \"sqlite-path-escape\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "sqlite_path = \"../outside.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n",
            sqliteSourceFiles,
            "E_MANIFEST",
            "lang.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-schema-missing",
            "name = \"sqlite-schema-missing\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "sqlite_path = \"data/sample.sqlite3\"\nsqlite_schema = \"db/missing.sql\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = sqliteHandler },
            "E_MANIFEST",
            "lang.toml");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "sqlite-read-grant-missing",
            "name = \"sqlite-read-grant-missing\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "sqlite_path = \"data/sample.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\ndb.write = \"allow\"\n",
            sqliteSourceFiles,
            "E_CAPABILITY_MISSING",
            "src/app/main.lang");

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
                ["src/app/main.lang"] = dynamicSqlSource,
                ["db/schema.sql"] = sqliteSourceFiles["db/schema.sql"]
            },
            "E_DB_SQL_LITERAL",
            "src/app/main.lang");

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
                    ["src/app/main.lang"] = SqliteReadQuerySource(sql),
                    ["db/schema.sql"] = sqliteSourceFiles["db/schema.sql"]
                },
                "E_DB_READ_STATEMENT",
                "src/app/main.lang");
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
                ["src/app/main.lang"] = unsupportedCodecSource,
                ["db/schema.sql"] = sqliteSourceFiles["db/schema.sql"]
            },
            "E_DB_CODEC_UNSUPPORTED",
            "src/app/main.lang");

        const string nonDatabaseWebSource = "module app::main;\n"
            + "union Reply { Ready }\n"
            + "fn ready() -> self::app::main::Reply effects {} { return self::app::main::Reply.Ready; }\n"
            + "route GET \"/\" { handler: self::app::main::ready; response Ready: 200; }\n";
        var nonDatabaseRoot = await harness.WritePackageAsync(
            "web-without-sqlite-dependency",
            "name = \"web-without-sqlite-dependency\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
                + "[capabilities]\nnet.listen = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = nonDatabaseWebSource });
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
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/repository.lang"] = source });

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
        var manifest = await File.ReadAllTextAsync(Path.Combine(packageRoot, "lang.toml"));
        var normalizedManifest = manifest.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        AssertTrue(normalizedManifest.Contains("[capabilities]\nfs.read = \"allow\"\n", StringComparison.Ordinal),
            "The maintained scanner must explicitly grant fs.read to its CLI application.");
        AssertTrue(normalizedManifest.Contains("[dependencies]\nvalidation = \"../text-validation\"", StringComparison.Ordinal),
            "The scanner must use text-validation through its local dependency alias.");
        AssertTrue(File.Exists(Path.Combine(packageRoot, "lang.lock")),
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
            AssertEqual(2, root.GetProperty("schema_version").GetInt32(), "Scan CLI schema version mismatch.");
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
    }

    private static async Task TestTextValidationExample(Harness harness)
    {
        var packageRoot = Path.Combine(harness.RepositoryRoot, "examples", "text-validation");
        var librarySource = await File.ReadAllTextAsync(
            Path.Combine(packageRoot, "src", "text", "validation.lang"));

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
                pub fn main() -> Text effects {} {
                    return match self::text::validation::normalize("") {
                        Ok(value) => "unexpected success",
                        Err(error) => match error {
                            self::text::validation::NormalizeError.Empty => "empty",
                        },
                    };
                }
                """, "empty" + Environment.NewLine),
            ("text-validation-nonempty", $$"""
                module app::main;
                pub fn main() -> Text effects {} {
                    return match self::text::validation::normalize("{{unicodeInput}}") {
                        Ok(value) => value,
                        Err(error) => "unexpected error",
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
                    ["src/text/validation.lang"] = librarySource,
                    ["src/app/main.lang"] = mainSource
                });
            var consumerCheck = await harness.InvokePackageDirectoryAsync(
                $"{name}-check", consumerRoot, "check", "--json");
            AssertEqual(0, consumerCheck.ExitCode, Describe(consumerCheck));
            AssertEqual(0, ParseDiagnosticSnapshots(consumerCheck.StandardOutput).Length,
                $"The same-package normalization consumer {name} should check cleanly.");

            var run = await harness.InvokePackageDirectoryAsync($"{name}-run", consumerRoot, "run");
            AssertRunOutput(expectedOutput, run);
        }

        var genericConsumer = await harness.WritePackageAsync(
            "text-validation-generic-consumer",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/text/validation.lang"] = librarySource,
                ["src/app/main.lang"] = """
                    module app::main;
                    pub fn main() -> i32 effects {} {
                        let text_option: Option<Text> = Some("hello 😀");
                        let text_result: Result<Text, self::text::validation::NormalizeError> = self::text::validation::require(text_option, self::text::validation::NormalizeError.Empty);
                        let number_option: Option<i32> = Some(35);
                        let number_result: Result<i32, Text> = self::text::validation::require(number_option, "missing");
                        let text_length: i32 = match text_result {
                            Ok(value) => value.length,
                            Err(error) => match error {
                                self::text::validation::NormalizeError.Empty => 0,
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
            "Both inferred generic instantiations and their exhaustive Result matches should check cleanly.");
        var genericRun = await harness.InvokePackageDirectoryAsync(
            "text-validation-generic-consumer-run", genericConsumer, "run");
        AssertRunOutput("42" + Environment.NewLine, genericRun);
    }

    private static async Task TestManagedLanguageTests(Harness harness)
    {
        var packageRoot = Path.Combine(harness.RepositoryRoot, "examples", "text-validation");
        var result = await harness.InvokePackageDirectoryAsync(
            "text-validation-language-tests", packageRoot, "test");
        var expected = string.Join(Environment.NewLine,
        [
            "PASS text::validation :: normalize empty input returns Empty",
            "PASS text::validation :: normalize trims nonempty input",
            "PASS text::validation :: Text trim removes surrounding Unicode whitespace",
            "PASS text::validation :: require preserves a present Option<Text>",
            "PASS text::validation :: require maps a missing Option<Text> to its error",
            "PASS text::validation :: require preserves a present Option<i32>",
            "PASS text::validation :: require maps a missing Option<i32> to its error",
            "7 passed, 0 failed"
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
                ["src/app/tests.lang"] = failureSource
            });
        var failureSourcePath = Path.Combine(failurePackage, "src", "app", "tests.lang");
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
                ["src/app/escaped.lang"] = escapedNameSource
            });
        var escapedNameSourcePath = Path.Combine(escapedNamePackage, "src", "app", "escaped.lang");
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
                ["src/empty/suite.lang"] = "module empty::suite;\n"
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
                ["src/suite/alpha.lang"] = """
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
                ["src/suite/beta.lang"] = """
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
                ["src/validation/invalid_tests.lang"] = invalidAssertions
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
                    ["src/root/tests.lang"] = rootTest
                }),
                ["child"] = new PackageFixture(LibraryPackageManifest("language-test-child"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/child/tests.lang"] = dependencyTest
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

        var dependencySourcePath = Path.Combine(Path.GetDirectoryName(rootPackage)!, "child", "src", "child", "tests.lang");
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
        AssertEqual(47, activeCount, $"Unexpected active fixture count in {manifestPath}.");
        AssertEqual(0, pendingCount, $"Unexpected pending fixture count in {manifestPath}.");
        AssertTrue(fixtures.All(item => item.GetProperty("status").GetString() is "active" or "pending"),
            $"Fixture manifest contains an unknown status: {manifestPath}.");

        var fixtureRun = await harness.InvokeCompilerCommandAsync("test");
        AssertEqual(0, fixtureRun.ExitCode, Describe(fixtureRun));
        AssertTrue(fixtureRun.StandardOutput.StartsWith("PASS 01-valid-constant.lang ", StringComparison.Ordinal),
            Describe(fixtureRun));
        AssertTrue(fixtureRun.StandardOutput.EndsWith("47 active, 0 pending, 0 failed" + Environment.NewLine, StringComparison.Ordinal),
            Describe(fixtureRun));
        AssertEqual(string.Empty, fixtureRun.StandardError, Describe(fixtureRun));

        var emittedCodes = new HashSet<string>(StringComparer.Ordinal);
        var resourceEscape = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "fixtures", "16-resource-escape.lang"));
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
        var sourceDirectory = Path.Combine(harness.RepositoryRoot, "src", "Lang");
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
        const string usagePrefix = "Usage: lang ";
        AssertTrue(usageLine.StartsWith(usagePrefix, StringComparison.Ordinal)
            && !usageLine.Contains('\n') && !usageLine.Contains('\r'), Describe(usage));
        var currentForms = new HashSet<string>(StringComparer.Ordinal)
        {
            "check FILE_OR_PACKAGE [--json]",
            "build FILE_OR_PACKAGE [--aot --rid RID]",
            "run FILE_OR_PACKAGE [-- APP_ARGS]",
            "lock PACKAGE_DIRECTORY",
            "audit PACKAGE_DIRECTORY --json",
            "inspect effects PACKAGE_DIRECTORY SYMBOL --json",
            "inspect api PACKAGE_DIRECTORY --json",
            "test [FILE_OR_PACKAGE]"
        };
        var reportedForms = usageLine[usagePrefix.Length..].Split(" | lang ", StringSplitOptions.None);
        AssertTrue(reportedForms.Length == currentForms.Count && currentForms.SetEquals(reportedForms),
            "The compiler usage command forms drifted from the expected surface. "
            + $"Expected [{string.Join(" | ", currentForms.Order(StringComparer.Ordinal))}], "
            + $"received [{string.Join(" | ", reportedForms)}].");

        static string CommandName(string form) => form.StartsWith("inspect effects ", StringComparison.Ordinal)
            ? "inspect effects"
            : form.StartsWith("inspect api ", StringComparison.Ordinal)
                ? "inspect api"
                : form.StartsWith("audit ", StringComparison.Ordinal)
                    ? "audit"
            : form.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        var currentCommandNames = currentForms.Select(CommandName).ToHashSet(StringComparer.Ordinal);

        var readme = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "README.md"));
        foreach (var command in currentCommandNames)
            AssertTrue(readme.Contains("lang " + command, StringComparison.Ordinal),
                $"README.md must document the current '{command}' command form.");
        foreach (var command in currentCommandNames)
            AssertTrue(grammar.Contains("lang " + command, StringComparison.Ordinal),
                $"docs/grammar.md must document the current '{command}' command form.");

        var packageCommandBlock = ExtractFencedBlockAfter(grammar,
            "Package commands use a package directory rather than a source-file path:", "text");
        var packageCommands = packageCommandBlock.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("lang ", StringComparison.Ordinal))
            .Select(line => CommandName(line["lang ".Length..]))
            .ToHashSet(StringComparer.Ordinal);
        AssertTrue(currentCommandNames.SetEquals(packageCommands),
            "docs/grammar.md package command list must cover exactly the compiler's current commands. "
            + $"Expected [{string.Join(", ", currentCommandNames.Order(StringComparer.Ordinal))}], "
            + $"received [{string.Join(", ", packageCommands.Order(StringComparer.Ordinal))}].");
        AssertTrue(packageCommandBlock.Contains("lang inspect effects PACKAGE_DIRECTORY SYMBOL --json", StringComparison.Ordinal),
            "docs/grammar.md package command list must show the inspect effects form.");
        AssertTrue(packageCommandBlock.Contains("lang inspect api PACKAGE_DIRECTORY --json", StringComparison.Ordinal),
            "docs/grammar.md package command list must show the inspect api form.");
        AssertTrue(packageCommandBlock.Contains("lang audit PACKAGE_DIRECTORY --json", StringComparison.Ordinal),
            "docs/grammar.md package command list must show the audit form.");

        foreach (var unsupportedCommand in new[] { "fmt", "new", "add" })
        {
            var unsupported = await harness.InvokeCompilerCommandAsync(unsupportedCommand);
            AssertEqual(2, unsupported.ExitCode, Describe(unsupported));
            AssertEqual(usage.StandardError, unsupported.StandardError, Describe(unsupported));
            AssertEqual(string.Empty, unsupported.StandardOutput, Describe(unsupported));
        }

        var roadmap = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "docs", "roadmap.md"));
        AssertTrue(Regex.IsMatch(roadmap, @"\b47\s+active\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(roadmap, @"\b0\s+pending\b", RegexOptions.IgnoreCase),
            "docs/roadmap.md must state that all 47 fixtures are active and none are pending.");
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
            harness.RepositoryRoot, "fixtures", "18-route-mapping.lang"));
        var check = await harness.InvokeAsync("static-route-check", "check", source, "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length,
            "Valid static GET and POST routes should typecheck cleanly.");

        var compilerAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.Location, Path.GetFullPath(Path.Combine(
                    harness.RepositoryRoot, "src", "Lang", "bin", "Release", "net10.0", "lang.dll")),
                    StringComparison.OrdinalIgnoreCase))
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(
                harness.RepositoryRoot, "src", "Lang", "bin", "Release", "net10.0", "lang.dll")));
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
            ("route-invalid-path", Add(header, "route GET \"/items/{id}\" { handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-unknown-item", Add(header, "route GET \"/items\" { unknown; }"), "E_ROUTE_DECL"),
            ("route-invalid-status", Add(header, "route GET \"/items\" { handler: self::harness::route_contract::good; response Found: 600 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-get-body", Add(header + "\nstruct Body { value: i32 }", "route GET \"/items\" { body: self::harness::route_contract::Body; handler: self::harness::route_contract::good; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
            ("route-post-missing-body", Add(header, "route POST \"/items\" { handler: self::harness::route_contract::takes_value; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_DECL"),
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
                ["src/app/main.lang"] = """
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
            harness.RepositoryRoot, "src", "Lang", "bin", "Release", "net10.0", "lang.dll"));
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
            var file = Path.Combine(harness.TemporaryRoot, module.Replace("::", Path.DirectorySeparatorChar.ToString()) + ".lang");
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
            .Single(constructor => constructor.GetParameters().Length == 4);
        var moduleListType = typeof(List<>).MakeGenericType(moduleInputType);
        var moduleInputs = Activator.CreateInstance(moduleListType)!;
        var add = moduleListType.GetMethod("Add")!;
        foreach (var parsed in parsedPrograms)
        {
            var moduleInput = moduleInputConstructor.Invoke([
                "root",
                parsed,
                new Dictionary<string, string>(StringComparer.Ordinal),
                "route-root"]);
            add.Invoke(moduleInputs, [moduleInput]);
        }

        var compilerType = assembly.GetType("Compiler", throwOnError: true)!;
        var checkPackage = compilerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "CheckPackage" && method.GetParameters().Length == 6);
        var result = checkPackage.Invoke(null, [moduleInputs, "root", "app::main", null, false, true])
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
            harness.RepositoryRoot, "examples", "text-validation", "src", "text", "validation.lang"));
        var constructorConsumer = await harness.WritePackageAsync(
            "generic-context-dependent-constructors",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/text/validation.lang"] = librarySource,
                ["src/app/main.lang"] = """
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
        AssertBuildTargetRejected(ridWithoutAot, "only valid with lang build");

        var aotOnRun = await harness.InvokeAsync(
            "aot-on-run", "run", source, "--aot", "--rid", CurrentHostAotRid());
        AssertBuildTargetRejected(aotOnRun, "only valid with lang build");

        var aotOnCheck = await harness.InvokeAsync("aot-on-check", "check", source, "--aot");
        AssertBuildTargetRejected(aotOnCheck, "only valid with lang build");

        var aotOnUnknownCommand = await harness.InvokeAsync(
            "aot-on-unknown-command", "publish", source, "--aot", "--rid", CurrentHostAotRid());
        AssertBuildTargetRejected(aotOnUnknownCommand, "only valid with lang build");

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
                ["src/app/main.lang"] = "module app::main; pub fn main() -> i32 effects {} { return 41; }"
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

        const string source = """
            module app::main;

            pub union ScanError { Failed }

            command scan {
                help "Echo a value.";
                argument input: Text help "Value to echo.";
                handler: self::app::main::run;
                error: self::app::main::describe_error;
            }

            pub fn run(args: self::app::main::ScanArgs) -> Result<Text, self::app::main::ScanError> effects {} {
                return Ok(args.input);
            }

            pub fn describe_error(error: self::app::main::ScanError) -> Text effects {} {
                return match error { self::app::main::ScanError.Failed => "scan failed" };
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "command-aot-smoke",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = source
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
            AssertEqual(2, schema.RootElement.GetProperty("schema_version").GetInt32(),
                "The AOT command schema version must be 2.");
            AssertEqual("scan", schema.RootElement.GetProperty("commands")[0].GetProperty("name").GetString(),
                "The AOT command schema should retain its command declaration.");
        }

        var execution = await ExecuteNativeAsync(
            executablePath,
            TimeSpan.FromSeconds(30),
            "scan",
            "--",
            "--leading");
        AssertRunOutput("--leading" + Environment.NewLine, execution);
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
            AssertEqual(2, schema.RootElement.GetProperty("schema_version").GetInt32(),
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

    private static async Task<ProcessResult> ExecuteNativeAsync(string executablePath, TimeSpan timeout, params string[] arguments)
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
        var firstPath = await harness.WriteSourceAsync(sharedSourceDirectory, "first.lang", firstSource);
        var secondPath = await harness.WriteSourceAsync(sharedSourceDirectory, "second.lang", secondSource);
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
                if (File.Exists(Path.Combine(directory.FullName, "lang.slnx"))
                    && File.Exists(Path.Combine(directory.FullName, "src", "Lang", "Lang.csproj")))
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
            "schema_version,build,package_graph,toolchain,inputs,manifest_grants,trusted_components,foreign_dependencies,audit_snapshot_sha256,artifacts");
        AssertEqual(1, root.GetProperty("schema_version").GetInt32(), "Build receipt schema version must be 1.");
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

        foreach (var package in root.GetProperty("package_graph").EnumerateArray())
        {
            AssertJsonPropertyOrder(package, "identity,role,content_sha256,dependencies");
            AssertJsonPropertyOrder(package.GetProperty("identity"), "name,version,path");
            AssertTrue(IsLowerSha256(package.GetProperty("content_sha256").GetString()),
                "Package graph content hashes must be lowercase SHA-256 values.");
            foreach (var dependency in package.GetProperty("dependencies").EnumerateArray())
            {
                AssertJsonPropertyOrder(dependency, "alias,package");
                AssertJsonPropertyOrder(dependency.GetProperty("package"), "name,version,path");
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
                AssertJsonPropertyOrder(identity, "name,version,path");
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
                AssertJsonPropertyOrder(reachable.GetProperty("package"), "name,version,path");
            }
        }
        AssertTrue(root.GetProperty("foreign_dependencies").GetRawText() == toolchain.GetProperty("foreign_dependencies").GetRawText(),
            "Top-level and toolchain foreign dependency records should match.");
        foreach (var dependency in root.GetProperty("foreign_dependencies").EnumerateArray())
            AssertJsonPropertyOrder(dependency, "name,version,ecosystem,reason");
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

    private sealed class Harness(string repositoryRoot, string compilerDll, string dotnet, string temporaryRoot)
    {
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
            await File.WriteAllTextAsync(Path.Combine(packageRoot, "lang.toml"), manifest);
            LastSourcePath = Path.Combine(packageRoot, "lang.toml");
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
                await File.WriteAllTextAsync(Path.Combine(packageRoot, "lang.toml"), package.Value.Manifest);
                LastSourcePath = Path.Combine(packageRoot, "lang.toml");
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

        public Task<ProcessResult> InvokeAsync(string caseName, string command, string source, params string[] additionalArguments) =>
            InvokeWithTimeoutAsync(caseName, command, source, ProcessTimeout, additionalArguments);

        public async Task<ProcessResult> InvokeCompilerCommandAsync(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(compilerDll);
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            startInfo.Environment["LANG_DOTNET"] = dotnet;

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
            var sourcePath = await WriteSourceAsync(sourceDirectory, "main.lang", source);
            return await InvokeFileWithTimeoutAsync(caseName, sourcePath, command, ProcessTimeout, hostOverride);
        }

        public async Task<ProcessResult> InvokeWithTimeoutAsync(string caseName, string command, string source, TimeSpan timeout, params string[] additionalArguments)
        {
            var sourceDirectory = CreateSourceDirectory(caseName);
            var sourcePath = await WriteSourceAsync(sourceDirectory, "main.lang", source);
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
            startInfo.Environment["LANG_DOTNET"] = dotnet;
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
            startInfo.Environment["LANG_DOTNET"] = dotnet;
            startInfo.Environment.Remove("LANG_SQLITE_PATH");

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
                additionalArguments);
        }

        private async Task<ProcessResult> InvokeTargetWithTimeoutAsync(
            string caseName,
            string target,
            string workingDirectory,
            string command,
            TimeSpan timeout,
            string? dotnetHostOverride,
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
            startInfo.Environment["LANG_DOTNET"] = dotnetHostOverride ?? dotnet;

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException($"Could not start the lang compiler process for {caseName}.");

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
    }

    private sealed record PackageFixture(string Manifest, IReadOnlyDictionary<string, string> SourceFiles);
    private sealed record DiagnosticSnapshot(string Code, string Message, string File, int StartLine, int StartColumn, int EndLine, int EndColumn);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class IntegrationTestSkippedException(string message) : Exception(message)
    {
    }
}
