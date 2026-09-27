using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text;

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
            ("union payload matching executes", TestUnionMatchOutput),
            ("Option and Result values require exhaustive typed matches", TestOptionResult),
            ("struct constructors support nested and chained field reads", TestStructValues),
            ("struct values compose with Option, Result, and unions", TestStructWrappers),
            ("forward and guarded structs work, including empty library builds", TestForwardAndGuardedRecursion),
            ("direct and mutual struct field cycles are rejected", TestStructCycles),
            ("struct field initializers and reads are checked", TestStructFieldDiagnostics),
            ("duplicate and reserved struct names are rejected", TestStructDeclarationNames),
            ("contextual keywords are identifiers only in their grammar contexts", TestContextualIdentifiers),
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
            ("imported calls carry effects into exact JSON diagnostics", TestImportedEffects),
            ("FsError requires an exhaustive typed match", TestFsErrorExhaustiveness),
            ("effectful FsRead libraries build as managed DLLs", TestEffectfulLibraryBuild),
            ("same-package CLI package checks, builds, and runs imported public values", TestPackageCliRoundTrip),
            ("package modules keep identically named private types isolated", TestPackagePrivateNameIsolation),
            ("package imports enforce module and symbol visibility and conflicts", TestPackageImportDiagnostics),
            ("package manifest schema is strict and reports JSON locations", TestPackageManifestDiagnostics),
            ("package module paths match their source headers", TestPackageModulePathDiagnostic),
            ("CLI entry module and main signature rules are enforced", TestPackageEntryPointDiagnostics),
            ("only the declared package entry module selects main", TestPackageEntrySelection),
            ("imported union variants participate in exhaustive matching", TestPackageImportedUnionExhaustiveness),
            ("package imports are not transitive", TestPackageImportsAreNotTransitive),
            ("library packages build as managed libraries", TestPackageLibraryBuild),
            ("path dependency locks are portable, stable, and required for package commands", TestPathDependencyLockLifecycle),
            ("dependency graphs reject cycles, missing manifests, non-libraries, and duplicate identities", TestDependencyGraphDiagnostics),
            ("dependency aliases enforce direct visibility and preserve module identity", TestDependencyAliasResolution),
            ("dependency imports enforce public and existing symbols", TestDependencyImportDiagnostics),
            ("dependency source roots and reparse paths stay inside package boundaries", TestDependencyFilesystemSafety),
            ("package NativeAOT arguments are validated", TestPackageAotCommandValidation),
            ("maintained package example runs with exact output", TestMaintainedPackageExample),
            ("Text validation package builds and imported generic calls specialize correctly", TestTextValidationExample),
            ("generic inference limits and generic main entry selection are diagnosed", TestGenericFunctionRestrictions),
            ("NativeAOT command validation returns build-target diagnostics", TestAotCommandValidation),
            ("NativeAOT rejects library sources before publishing", TestAotLibraryRejected),
            ("NativeAOT publishes and runs the current-host file executable", TestAotPublishAndRun),
            ("NativeAOT publishes and runs the current-host package executable", TestPackageAotPublishAndRun),
            ("invalid main signatures receive an entrypoint diagnostic", TestInvalidEntrypoint),
            ("LANG_DOTNET launch failures become process diagnostics", TestDotnetLaunchFailure),
            ("concurrent runs keep their generated outputs isolated", TestParallelRuns)
        };

        var failures = 0;
        var skipped = 0;
        try
        {
            foreach (var test in cases)
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

        Console.WriteLine($"{cases.Length - failures - skipped} passed, {skipped} skipped, {failures} failed");
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
        const string source = "module harness.valid_json;\n"
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
        const string boolSource = "module harness.bool_value;\n"
            + "pub fn echo(value: bool) -> bool effects {} { return value; }\n"
            + "pub fn main() -> bool effects {} { let answer: bool = echo(true); return answer; }\n";
        const string falseSource = "module harness.false_value;\n"
            + "pub fn main() -> bool effects {} { return false; }\n";
        const string textSource = """
            module harness.text_value;
            pub fn echo(value: Text) -> Text effects {} { return value; }
            pub fn main() -> Text effects {} {
                let answer: Text = echo("quote: \" slash: \\ line1\nline2 λ 😀");
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
            module harness.comparisons;
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
                                        return choose(true, 21);
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
            module harness.control_member_call;
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
            module harness.non_boolean_condition;
            pub fn main() -> i32 effects {} {
                if 1 { return 1; } else { return 0; }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "non-boolean-condition", nonBooleanCondition, "E_TYPE_MISMATCH");

        const string mismatchedOrderingOperands = """
            module harness.mismatched_ordering;
            pub fn main() -> i32 effects {} {
                if 1 < false { return 1; } else { return 0; }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "mismatched-ordering-operands", mismatchedOrderingOperands, "E_TYPE_MISMATCH");

        const string mismatchedEqualityOperands = """
            module harness.mismatched_equality;
            pub fn main() -> i32 effects {} {
                let same: bool = 1 == "1";
                if same { return 1; } else { return 0; }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "mismatched-equality-operands", mismatchedEqualityOperands, "E_TYPE_MISMATCH");

        const string missingReturn = """
            module harness.if_missing_return;
            pub fn choose(enabled: bool) -> i32 effects {} {
                if enabled { return 1; }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "if-missing-return", missingReturn, "E_TYPE_MISMATCH");

        const string branchLocalEscape = """
            module harness.branch_local_escape;
            pub fn main() -> i32 effects {} {
                if true { let branch_value: i32 = 1; } else { }
                return branch_value;
            }
            """;
        await ExpectDiagnosticsAsync(harness, "branch-local-escape", branchLocalEscape, "E_NAME_UNRESOLVED");

        const string unreachableStatement = """
            module harness.unreachable_statement;
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
            module harness.scalar_length;
            pub fn main() -> i32 effects {} { return "😀".length; }
            """;
        AssertRunOutput("1" + Environment.NewLine,
            await harness.InvokeAsync("unicode-scalar-length", "run", scalarLengthSource));

        var whitespace = "\u00A0\u3000hello 😀\u3000\u00A0";
        var trimSource = $$"""
            module harness.unicode_trim;
            pub fn main() -> Text effects {} { return "{{whitespace}}".trim(); }
            """;
        AssertRunOutput("hello 😀" + Environment.NewLine,
            await harness.InvokeAsync("unicode-whitespace-trim", "run", trimSource));
    }

    private static async Task TestUnionMatchOutput(Harness harness)
    {
        const string source = """
            module harness.union_match;
            pub union Choice { Number(i32), Word(Text), Empty }
            pub fn main() -> i32 effects {} {
                let choice: Choice = Choice.Number(37);
                return match choice {
                    Choice.Number(number) => number,
                    Choice.Word(word) => 0,
                    Choice.Empty => 0,
                };
            }
            """;
        var result = await harness.InvokeAsync("union-match", "run", source);
        AssertRunOutput("37" + Environment.NewLine, result);
    }

    private static async Task TestOptionResult(Harness harness)
    {
        const string optionSource = """
            module harness.option;
            pub fn main() -> i32 effects {} {
                let value: Option<i32> = Some(23);
                return match value {
                    Some(number) => number,
                    None => 0,
                };
            }
            """;
        const string resultSource = """
            module harness.result;
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
            module harness.struct_values;
            pub union Color { Red, Blue }
            pub union Choice { Yes, No }
            pub struct Container { item: Choice }
            pub struct Person { name: Text, age: i32 }
            pub struct Profile { owner: Person }
            pub struct ColorValue { Red: i32 }

            pub fn make_person(name: Text, age: i32) -> Person effects {} {
                return Person { age: age, name: name };
            }
            pub fn person_age(person: Person) -> i32 effects {} { return person.age; }
            pub fn red_value() -> i32 effects {} {
                let color: Color = Color.Red;
                return match color { Color.Red => 1, Color.Blue => 0, };
            }
            pub fn main() -> i32 effects {} {
                let Color: ColorValue = ColorValue { Red: 40 };
                let profile: Profile = Profile { owner: make_person("Ada", 37) };
                let chained: i32 = profile.owner.age;
                let call_field: i32 = make_person("Lin", 2).age;
                let call_argument: i32 = person_age(profile.owner);
                let parenthesized_value: i32 = (profile.owner).age;
                let parenthesized_constructor: i32 = (Person { age: 3, name: "Ada" }).age;
                let matched: i32 = match (Container { item: Choice.Yes }).item {
                    Choice.Yes => Color.Red,
                    Choice.No => 0,
                };
                return chained + call_field + call_argument + parenthesized_value
                    + parenthesized_constructor + matched + red_value();
            }
            """;

        var result = await harness.InvokeAsync("struct-values", "run", source);
        AssertRunOutput("157" + Environment.NewLine, result);
    }

    private static async Task TestStructWrappers(Harness harness)
    {
        const string source = """
            module harness.struct_wrappers;
            pub struct Record { value: i32 }
            pub union BoxedRecord { Present(Record), Empty }

            pub fn option_or_default(value: Option<Record>) -> Record effects {} {
                return match value {
                    Some(record) => record,
                    None => Record { value: 0 },
                };
            }
            pub fn result_or_default(value: Result<Record, Text>) -> Record effects {} {
                return match value {
                    Ok(record) => record,
                    Err(message) => Record { value: 0 },
                };
            }
            pub fn main() -> i32 effects {} {
                let optional: Option<Record> = Some(Record { value: 23 });
                let from_option: Record = option_or_default(optional);
                let result: Result<Record, Text> = Ok(from_option);
                let from_result: Record = result_or_default(result);
                let boxed: BoxedRecord = BoxedRecord.Present(from_result);
                return match boxed {
                    BoxedRecord.Present(record) => record.value,
                    BoxedRecord.Empty => 0,
                };
            }
            """;

        var result = await harness.InvokeAsync("struct-wrappers", "run", source);
        AssertRunOutput("23" + Environment.NewLine, result);
    }

    private static async Task TestForwardAndGuardedRecursion(Harness harness)
    {
        const string source = """
            module harness.forward_guarded_structs;
            pub struct Empty {}
            pub struct Before { after: After }
            pub struct After { value: i32 }
            pub struct OptionalNode { next: Option<OptionalNode> }
            pub struct ResultNode { next: Result<Option<ResultNode>, Text> }
            pub union TreeLink { Branch(TreeBranch), End }
            pub struct TreeBranch { next: TreeLink }

            pub fn main() -> i32 effects {} {
                let empty: Empty = Empty {};
                let optional: OptionalNode = OptionalNode { next: None };
                let result: ResultNode = ResultNode { next: Err("stop") };
                let tree: TreeLink = TreeLink.Branch(TreeBranch { next: TreeLink.End });
                let value: Before = Before { after: After { value: 29 } };
                return value.after.value;
            }
            """;

        var result = await harness.InvokeAsync("forward-guarded-structs", "run", source);
        AssertRunOutput("29" + Environment.NewLine, result);

        const string librarySource = """
            module harness.empty_struct_library;
            pub struct Empty {}
            pub fn make_empty() -> Empty effects {} { return Empty {}; }
            """;
        var library = await harness.InvokeAsync("empty-struct-library", "build", librarySource);
        AssertBuiltDll(library, Path.GetDirectoryName(harness.LastSourcePath)!);
    }

    private static async Task TestStructCycles(Harness harness)
    {
        var cases = new (string Name, string Source)[]
        {
            ("direct-struct-cycle", """
                module harness.direct_struct_cycle;
                pub struct Node { next: Node }
                """),
            ("mutual-struct-cycle", """
                module harness.mutual_struct_cycle;
                pub struct Left { right: Right }
                pub struct Right { left: Left }
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
                module harness.missing_struct_field;
                pub struct Person { name: Text, age: i32 }
                pub fn main() -> i32 effects {} {
                    let person: Person = Person { name: "Ada" };
                    return 0;
                }
                """, "E_FIELD_MISSING"),
            ("unknown-struct-field", """
                module harness.unknown_struct_field;
                pub struct Person { name: Text, age: i32 }
                pub fn main() -> i32 effects {} {
                    let person: Person = Person { name: "Ada", age: 37, nickname: "A" };
                    return 0;
                }
                """, "E_FIELD_UNKNOWN"),
            ("duplicate-struct-initializer-field", """
                module harness.duplicate_struct_initializer_field;
                pub struct Person { name: Text, age: i32 }
                pub fn main() -> i32 effects {} {
                    let person: Person = Person { name: "Ada", age: 37, age: 38 };
                    return 0;
                }
                """, "E_FIELD_DUPLICATE"),
            ("shadowed-dotted-call", """
                module harness.shadowed_dotted_call;
                pub union Choice { Yes(i32), No }
                pub struct ChoiceValue { Yes: i32 }
                pub fn main() -> i32 effects {} {
                    let Choice: ChoiceValue = ChoiceValue { Yes: 1 };
                    return Choice.Yes(2);
                }
                """, "E_UNSUPPORTED"),
            ("wrong-struct-field-type", """
                module harness.wrong_struct_field_type;
                pub struct Person { name: Text, age: i32 }
                pub fn main() -> i32 effects {} {
                    let person: Person = Person { name: "Ada", age: "thirty-seven" };
                    return 0;
                }
                """, "E_TYPE_MISMATCH"),
            ("unknown-struct-member-read", """
                module harness.unknown_struct_member;
                pub struct Person { name: Text, age: i32 }
                pub fn main() -> i32 effects {} {
                    let person: Person = Person { name: "Ada", age: 37 };
                    return person.height;
                }
                """, "E_FIELD_UNKNOWN"),
            ("non-struct-member-read", """
                module harness.non_struct_member;
                pub fn main() -> i32 effects {} {
                    let count: i32 = 3;
                    return count.value;
                }
                """, "E_TYPE_MISMATCH"),
            ("nominal-struct-mismatch", """
                module harness.nominal_struct_mismatch;
                pub struct User { age: i32 }
                pub struct Score { age: i32 }
                pub fn use_user(value: User) -> i32 effects {} { return value.age; }
                pub fn main() -> i32 effects {} { return use_user(Score { age: 37 }); }
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
            module true.false.null.match.if.await.with.route.command.effects.return.fn;
            struct effects { route: i32, return: i32, if: i32, true: i32, null: i32 }
            union Choice { return(null: i32) }

            fn route(command: i32) -> i32 effects {} {
                let return: i32 = command;
                return return;
            }

            fn make_choice(value: effects) -> Choice effects {} {
                return Choice.return(value.route + value.return + value.if + value.true + value.null);
            }

            pub fn main() -> i32 effects {} {
                let value: effects = effects { route: 1, return: 2, if: 3, true: 4, null: 5 };
                let choice: Choice = make_choice(value);
                return match choice {
                    Choice.return(payload) => route(payload),
                };
            }
            """;

        var result = await harness.InvokeAsync("contextual-identifiers", "run", source);
        AssertRunOutput("15" + Environment.NewLine, result);

        const string hardKeywordSource = """
            module harness.hard_keyword_identifier;
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
            module harness.standalone_route;
            route
            """;
        await ExpectDiagnosticsAsync(harness, "standalone-route-declaration", standaloneRouteSource, "E_UNSUPPORTED");
    }

    private static async Task TestStructDeclarationNames(Harness harness)
    {
        var cases = new (string Name, string Source, string ExpectedCode)[]
        {
            ("duplicate-struct-declaration", """
                module harness.duplicate_struct_declaration;
                pub struct Item {}
                pub struct Item { value: i32 }
                """, "E_NAME_DUPLICATE"),
            ("duplicate-struct-field-declaration", """
                module harness.duplicate_struct_field_declaration;
                pub struct Item { value: i32, value: Text }
                """, "E_NAME_DUPLICATE"),
            ("union-struct-name-collision", """
                module harness.union_struct_name_collision;
                pub union Item { Empty }
                pub struct Item { value: i32 }
                """, "E_NAME_DUPLICATE"),
            ("reserved-struct-name", """
                module harness.reserved_struct_name;
                pub struct match { value: i32 }
                """, "E_SYNTAX"),
            ("reserved-builtin-type-names", """
                module harness.reserved_builtin_type_names;
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
                module harness.public_function_private_struct;
                pub struct Public { value: i32 }
                struct Hidden { value: i32 }
                pub fn expose(value: Option<Result<Hidden, Text>>) -> i32 effects {} { return 0; }
                """),
            ("public-union-private-struct", """
                module harness.public_union_private_struct;
                struct Hidden { value: i32 }
                pub union PublicChoice { Wrapped(Option<Result<Hidden, Text>>), Empty }
                """),
            ("public-struct-private-field-type", """
                module harness.public_struct_private_field;
                struct Hidden { value: i32 }
                pub struct PublicBox { value: Option<Result<Hidden, Text>> }
                """)
        };

        foreach (var (name, source) in cases)
            await ExpectDiagnosticsAsync(harness, name, source, "E_TYPE_VISIBILITY");
    }

    private static async Task TestDeepStructFieldChain(Harness harness)
    {
        var fieldChain = "node" + string.Concat(Enumerable.Repeat(".next", 300));
        var source = "module harness.deep_struct_field_chain;\n"
            + "pub struct Node { next: i32 }\n"
            + "pub fn main() -> i32 effects {} { let node: Node = Node { next: 0 }; return " + fieldChain + "; }\n";

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
                module harness.wrong_argument;
                pub fn wants_i32(value: i32) -> i32 effects {} { return value; }
                pub fn main() -> i32 effects {} { return wants_i32(true); }
                """),
            ("wrong-local", """
                module harness.wrong_local;
                pub fn main() -> i32 effects {} { let value: i32 = true; return 0; }
                """),
            ("wrong-return", """
                module harness.wrong_return;
                pub fn value() -> i32 effects {} { return true; }
                pub fn main() -> i32 effects {} { return 0; }
                """),
            ("wrong-binary", """
                module harness.wrong_binary;
                pub fn main() -> i32 effects {} { return 1 + true; }
                """),
            ("wrong-union-payload", """
                module harness.wrong_union_payload;
                pub union Choice { Number(i32) }
                pub fn main() -> i32 effects {} { let value: Choice = Choice.Number(true); return 0; }
                """),
            ("wrong-match-arm", """
                module harness.wrong_match_arm;
                pub union Choice { First, Second }
                pub fn main() -> i32 effects {} {
                    let value: Choice = Choice.First;
                    return match value { Choice.First => 1, Choice.Second => false, };
                }
                """),
            ("option-does-not-implicitly-unwrap", """
                module harness.option_unwrap;
                pub fn unwrap(value: Option<i32>) -> i32 effects {} { return value; }
                pub fn main() -> i32 effects {} { return unwrap(Some(7)); }
                """),
            ("option-arguments-are-invariant", """
                module harness.option_types;
                pub fn use_integer(value: Option<i32>) -> i32 effects {} {
                    return match value { Some(number) => number, None => 0, };
                }
                pub fn main() -> i32 effects {} {
                    let value: Option<Text> = Some("text");
                    return use_integer(value);
                }
                """),
            ("result-type-arguments-are-ordered", """
                module harness.result_types;
                pub fn use_result(value: Result<i32, Text>) -> i32 effects {} {
                    return match value { Ok(number) => number, Err(message) => 0, };
                }
                pub fn main() -> i32 effects {} {
                    let value: Result<Text, i32> = Err(1);
                    return use_result(value);
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
            module harness.missing_match;
            pub union Choice { Yes, No }
            pub fn main() -> i32 effects {} {
                let value: Choice = Choice.Yes;
                return match value { Choice.Yes => 1, };
            }
            """;
        var missingDiagnostics = await ExpectDiagnosticsAsync(harness, "missing-match-arm", missingSource, "E_MATCH_NONEXHAUSTIVE");
        var missing = missingDiagnostics.Single(diagnostic => diagnostic.Code == "E_MATCH_NONEXHAUSTIVE");
        AssertTrue(missing.Message.Contains("Choice.No", StringComparison.Ordinal), "Non-exhaustive diagnostic must name Choice.No.");
        AssertRangeAtToken(missingSource, missing, "match", 1);

        const string duplicateSource = """
            module harness.duplicate_match;
            pub union Choice { Yes, No }
            pub fn main() -> i32 effects {} {
                let value: Choice = Choice.Yes;
                return match value { Choice.Yes => 1, Choice.Yes => 2, Choice.No => 0, };
            }
            """;
        var duplicateDiagnostics = await ExpectDiagnosticsAsync(harness, "duplicate-match-arm", duplicateSource, "E_MATCH_ARM_DUPLICATE");
        var duplicate = duplicateDiagnostics.Single(diagnostic => diagnostic.Code == "E_MATCH_ARM_DUPLICATE");
        AssertRangeAtToken(duplicateSource, duplicate, "Choice", 5);

        const string wrongUnionSource = """
            module harness.wrong_union_pattern;
            pub union Choice { First, Second }
            pub union Other { First, Second }
            pub fn main() -> i32 effects {} {
                let value: Choice = Choice.First;
                return match value { Other.First => 1, Other.Second => 0, };
            }
            """;
        await ExpectDiagnosticsAsync(harness, "wrong-union-pattern", wrongUnionSource, "E_TYPE_MISMATCH", "E_NAME_UNRESOLVED");
        const string aritySource = """
            module harness.match_arity;
            pub union Choice { Number(i32), Empty }
            pub fn main() -> i32 effects {} {
                let value: Choice = Choice.Empty;
                return match value { Choice.Number(first, second) => first, Choice.Empty => 0, };
            }
            """;
        await ExpectDiagnosticsAsync(harness, "wrong-match-payload-arity", aritySource, "E_TYPE_MISMATCH");
    }

    private static async Task TestNull(Harness harness)
    {
        const string expressionSource = """
            module harness.null_expression;
            pub fn main() -> i32 effects {} { return null; }
            """;
        var expressionDiagnostics = await ExpectDiagnosticsAsync(harness, "null-expression", expressionSource, "E_TYPE_MISMATCH");
        AssertRangeAtToken(expressionSource, expressionDiagnostics.Single(diagnostic => diagnostic.Code == "E_TYPE_MISMATCH"), "null", 1);

        const string identifierSource = """
            module harness.null_identifier;
            pub fn main() -> i32 effects {} { let null: i32 = 1; return null; }
            """;
        var identifierDiagnostics = await ExpectDiagnosticsAsync(harness, "null-identifier", identifierSource, "E_TYPE_MISMATCH", "E_SYNTAX");
        AssertRangeAtToken(identifierSource, identifierDiagnostics.Single(), "null", 1);
    }

    private static async Task TestDeepNesting(Harness harness)
    {
        var nestedType = "i32";
        for (var depth = 0; depth < 300; depth++) nestedType = $"Option<{nestedType}>";
        var source = $"module harness.deep_nesting;\npub fn value(input: {nestedType}) -> i32 effects {{}} {{ return 0; }}\n";

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
        var source = "module harness.long_chain;\n"
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
            module harness.negative_one;
            pub fn main() -> i32 effects {} { return -1; }
            """;
        const string minimumSource = """
            module harness.minimum;
            pub fn main() -> i32 effects {} { return -2147483648; }
            """;
        const string negativeCallSource = """
            module harness.negative_call;
            pub fn value() -> i32 effects {} { return 3; }
            pub fn main() -> i32 effects {} { return -value(); }
            """;
        const string tooSmallSource = """
            module harness.too_small;
            pub fn main() -> i32 effects {} { return -2147483649; }
            """;
        const string negatedMinimumSource = """
            module harness.negated_minimum;
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
        const string pureSource = "module harness.effect_pure;\n"
            + "pub fn main() -> i32 effects {} { return 42; }\n";
        AssertRunOutput("42" + Environment.NewLine, await harness.InvokeAsync("effect-pure-success", "run", pureSource));

        const string directSource = "module harness.effect_direct;\n"
            + "pub fn read(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n";
        var directCheck = await harness.InvokeAsync("effect-direct-success", "check", directSource, "--json");
        AssertEqual(0, directCheck.ExitCode, Describe(directCheck));
        AssertEqual(0, ParseDiagnosticSnapshots(directCheck.StandardOutput).Length,
            "A direct fs.read operation inside its declared upper bound should check cleanly.");

        const string directExceededSource = "module harness.effect_direct_exceeded;\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return fs.read_text(\"x\"); }\n";
        var directExceeded = await ExpectDiagnosticsAsync(
            harness, "effect-direct-exceeded", directExceededSource, "E_EFFECT_EXCEEDED");
        AssertEqual(1, directExceeded.Length, "A direct effect outside its upper bound should produce one diagnostic.");
        AssertRangeAtToken(directExceededSource, directExceeded.Single(), "bad", 1);
        AssertEqual(
            "Effect 'fs.read' is not declared by function 'harness.effect_direct_exceeded.bad'; shortest call path: harness.effect_direct_exceeded.bad -> fs.read_text",
            directExceeded.Single().Message,
            "A direct-effect diagnostic should show the shortest path to the operation.");

        const string unknownEffectSource = "module harness.effect_unknown_annotation;\n"
            + "pub fn bad() -> i32 effects { fs.unknown } { return 1; }\n";
        var unknownEffect = await ExpectDiagnosticsAsync(
            harness, "effect-unknown-annotation", unknownEffectSource, "E_EFFECT_UNKNOWN");
        AssertEqual(1, unknownEffect.Length, "An unknown annotation should produce one diagnostic.");

        const string duplicateEffectSource = "module harness.effect_duplicate_annotation;\n"
            + "pub fn bad() -> i32 effects { fs.read, fs.read } { return 1; }\n";
        var duplicateEffect = await ExpectDiagnosticsAsync(
            harness, "effect-duplicate-annotation", duplicateEffectSource, "E_EFFECT_DUPLICATE");
        AssertEqual(1, duplicateEffect.Length, "A repeated annotation should produce one diagnostic.");

        const string missingCapabilitySource = "module harness.effect_missing_capability;\n"
            + "pub fn bad() -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n";
        var missingCapability = await ExpectDiagnosticsAsync(
            harness, "effect-missing-capability", missingCapabilitySource, "E_CAPABILITY_MISSING");
        AssertRangeAtToken(missingCapabilitySource, missingCapability.Single(), "fs", 2);

        const string wrongCapabilitySource = "module harness.effect_wrong_capability;\n"
            + "pub fn bad(value: Text) -> Result<Text, FsError> effects { fs.read } { return value.read_text(\"x\"); }\n";
        var wrongCapability = await ExpectDiagnosticsAsync(
            harness, "effect-wrong-capability", wrongCapabilitySource, "E_CAPABILITY_MISSING");
        AssertRangeAtToken(wrongCapabilitySource, wrongCapability.Single(), "value", 2);

        const string invalidFunctionCallSource = "module harness.effect_invalid_function_call;\n"
            + "fn read(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return read(); }\n";
        var invalidFunctionCall = await ExpectDiagnosticsAsync(
            harness, "effect-invalid-function-call", invalidFunctionCallSource, "E_TYPE_MISMATCH");
        AssertTrue(invalidFunctionCall.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "An invalid-arity function call must not add a transitive effect diagnostic.");

        const string invalidIntrinsicAritySource = "module harness.effect_invalid_intrinsic_arity;\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return fs.read_text(); }\n";
        var invalidIntrinsicArity = await ExpectDiagnosticsAsync(
            harness, "effect-invalid-intrinsic-arity", invalidIntrinsicAritySource, "E_TYPE_MISMATCH");
        AssertTrue(invalidIntrinsicArity.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "An invalid-arity filesystem operation must not seed an inferred effect.");

        const string invalidIntrinsicPathSource = "module harness.effect_invalid_intrinsic_path;\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return fs.read_text(1); }\n";
        var invalidIntrinsicPath = await ExpectDiagnosticsAsync(
            harness, "effect-invalid-intrinsic-path", invalidIntrinsicPathSource, "E_TYPE_MISMATCH");
        AssertTrue(invalidIntrinsicPath.All(diagnostic => diagnostic.Code != "E_EFFECT_EXCEEDED"),
            "A wrong-typed filesystem path must not seed an inferred effect.");

        const string localReceiverSource = "module harness.effect_local_receiver;\n"
            + "pub fn read(FsRead: FsRead) -> Result<Text, FsError> effects { fs.read } { return FsRead.read_text(\"x\"); }\n";
        var localReceiver = await harness.InvokeAsync("effect-local-receiver", "check", localReceiverSource, "--json");
        AssertEqual(0, localReceiver.ExitCode, Describe(localReceiver));
        AssertEqual(0, ParseDiagnosticSnapshots(localReceiver.StandardOutput).Length,
            "The local receiver named FsRead should take precedence over the type name.");
    }

    private static async Task TestEffectInferencePaths(Harness harness)
    {
        const string source = "module harness.effect_paths;\n"
            + "fn leaf(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n"
            + "fn deep_three(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return leaf(fs); }\n"
            + "fn deep_two(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return deep_three(fs); }\n"
            + "fn deep_one(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return deep_two(fs); }\n"
            + "fn near(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return leaf(fs); }\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} {\n"
            + "    let nearer: Result<Text, FsError> = near(fs);\n"
            + "    return deep_one(fs);\n"
            + "}\n";

        var firstRun = await ExpectDiagnosticsAsync(harness, "effect-shortest-path-first", source, "E_EFFECT_EXCEEDED");
        var secondRun = await ExpectDiagnosticsAsync(harness, "effect-shortest-path-second", source, "E_EFFECT_EXCEEDED");
        const string expectedMessage = "Effect 'fs.read' is not declared by function 'harness.effect_paths.bad'; shortest call path: harness.effect_paths.bad -> harness.effect_paths.near -> harness.effect_paths.leaf -> fs.read_text";
        AssertEqual(expectedMessage, firstRun.Single().Message, "The checker should choose the shortest call path.");
        AssertEqual(firstRun.Single().Message, secondRun.Single().Message,
            "Call-path diagnostic content should be deterministic across runs.");
        AssertRangeAtToken(source, firstRun.Single(), "bad", 1);

        const string tiedPathsSource = "module harness.effect_tie;\n"
            + "fn leaf(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n"
            + "fn zeta(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return leaf(fs); }\n"
            + "fn alpha(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return leaf(fs); }\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} {\n"
            + "    let first: Result<Text, FsError> = zeta(fs);\n"
            + "    return alpha(fs);\n"
            + "}\n";
        var tiedPaths = await ExpectDiagnosticsAsync(harness, "effect-shortest-path-tie", tiedPathsSource, "E_EFFECT_EXCEEDED");
        AssertEqual(
            "Effect 'fs.read' is not declared by function 'harness.effect_tie.bad'; shortest call path: harness.effect_tie.bad -> harness.effect_tie.alpha -> harness.effect_tie.leaf -> fs.read_text",
            tiedPaths.Single().Message,
            "Equal-length effect paths should use canonical function ordering, independent of call source order.");

        const string recursiveSource = "module harness.effect_recursive;\n"
            + "fn first(fs: FsRead) -> Result<Text, FsError> effects { fs.read } {\n"
            + "    let next: Result<Text, FsError> = second(fs);\n"
            + "    return fs.read_text(\"x\");\n"
            + "}\n"
            + "fn second(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return first(fs); }\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return second(fs); }\n";
        var recursiveDiagnostics = await ExpectDiagnosticsAsync(
            harness, "effect-recursive-cycle", recursiveSource, "E_EFFECT_EXCEEDED");
        AssertEqual(
            "Effect 'fs.read' is not declared by function 'harness.effect_recursive.bad'; shortest call path: harness.effect_recursive.bad -> harness.effect_recursive.second -> harness.effect_recursive.first -> fs.read_text",
            recursiveDiagnostics.Single().Message,
            "Effect inference should converge through recursive call cycles and retain the shortest path.");
    }

    private static async Task TestImportedEffects(Harness harness)
    {
        const string library = "module io.files;\n"
            + "pub fn load(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return fs.read_text(\"x\"); }\n";
        const string wrapperWithoutEffect = "module app.reader;\n"
            + "import io.files { load };\n"
            + "pub fn bad(fs: FsRead) -> Result<Text, FsError> effects {} { return load(fs); }\n";
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/reader.lang"] = wrapperWithoutEffect,
            ["src/io/files.lang"] = library
        };
        var packageRoot = await harness.WritePackageAsync("effect-imported-exceeded", LibraryPackageManifest(), files);
        var wrapperPath = Path.GetFullPath(Path.Combine(packageRoot, "src", "app", "reader.lang"));
        var result = await harness.InvokePackageDirectoryAsync("effect-imported-exceeded", packageRoot, "check", "--json");
        AssertTrue(result.ExitCode != 0, $"An imported effect outside its upper bound unexpectedly passed. {Describe(result)}");
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        var diagnostics = ParseDiagnosticSnapshots(result.StandardOutput);
        var exceeded = diagnostics.Single(diagnostic => diagnostic.Code == "E_EFFECT_EXCEEDED");
        AssertEqual(wrapperPath, Path.GetFullPath(exceeded.File), "The effect diagnostic should identify the caller module.");
        AssertRangeAtToken(wrapperWithoutEffect, exceeded, "bad", 1);
        AssertEqual(
            "Effect 'fs.read' is not declared by function 'app.reader.bad'; shortest call path: app.reader.bad -> io.files.load -> fs.read_text",
            exceeded.Message,
            "The path should include the imported function and the filesystem operation.");

        var repeat = await harness.InvokePackageDirectoryAsync("effect-imported-exceeded-repeat", packageRoot, "check", "--json");
        var repeated = ParseDiagnosticSnapshots(repeat.StandardOutput).Single(diagnostic => diagnostic.Code == "E_EFFECT_EXCEEDED");
        AssertEqual(exceeded.Message, repeated.Message, "Imported call paths should be stable across repeated checks.");

        const string wrapperWithEffect = "module app.reader;\n"
            + "import io.files { load };\n"
            + "pub fn read(fs: FsRead) -> Result<Text, FsError> effects { fs.read } { return load(fs); }\n";
        var allowedPackage = await harness.WritePackageAsync(
            "effect-imported-allowed",
            LibraryPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/reader.lang"] = wrapperWithEffect,
                ["src/io/files.lang"] = library
            });
        var allowed = await harness.InvokePackageDirectoryAsync("effect-imported-allowed", allowedPackage, "check", "--json");
        AssertEqual(0, allowed.ExitCode, Describe(allowed));
        AssertEqual(0, ParseDiagnosticSnapshots(allowed.StandardOutput).Length,
            "An imported effect inside the caller's upper bound should check cleanly.");
    }

    private static async Task TestFsErrorExhaustiveness(Harness harness)
    {
        const string exhaustiveSource = "module harness.fs_error_exhaustive;\n"
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

        const string incompleteSource = "module harness.fs_error_incomplete;\n"
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
        const string source = "module harness.effectful_library;\n"
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
            module harness.library;
            pub fn square(value: i32) -> i32 effects {} { return value * value; }
            """;
        var result = await harness.InvokeAsync("library-build", "build", source);
        AssertBuiltDll(result, Path.GetDirectoryName(harness.LastSourcePath)!);

        const string mainNamedLibrarySource = """
            module harness.main_named_library;
            pub union U { A }
            pub fn main(value: U) -> U effects {} { return value; }
            """;
        var mainNamedLibrary = await harness.InvokeAsync("main-named-library-build", "build", mainNamedLibrarySource);
        AssertBuiltDll(mainNamedLibrary, Path.GetDirectoryName(harness.LastSourcePath)!);

        var run = await harness.InvokeAsync("main-named-library-run", "run", mainNamedLibrarySource);
        AssertTrue(run.ExitCode != 0, Describe(run));
        AssertEqual(string.Empty, run.StandardOutput, Describe(run));
        AssertTrue(run.StandardError.Contains("E_ENTRYPOINT", StringComparison.Ordinal),
            $"run should reject the non-primitive parameterized main. {Describe(run)}");
    }

    private static async Task TestPackageCliRoundTrip(Harness harness)
    {
        var packageRoot = await harness.WritePackageAsync(
            "package-cli-roundtrip",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/catalog/message.lang"] = """
                    module catalog.message;

                    pub struct Greeting { text: Text }
                    pub union Message { Ready(Greeting), Missing }

                    pub fn greeting() -> Greeting effects {} {
                        return Greeting { text: "ready" };
                    }
                    """,
                ["src/app/main.lang"] = """
                    module app.main;
                    import catalog.message { Greeting, Message, greeting };

                    pub fn main() -> Text effects {} {
                        let welcome: Greeting = greeting();
                        let message: Message = Message.Ready(welcome);
                        return match message {
                            Message.Ready(value) => value.text,
                            Message.Missing => "missing",
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

    private static async Task TestPackagePrivateNameIsolation(Harness harness)
    {
        const string first = """
            module first;
            struct Hidden { value: i32 }
            pub fn first_value() -> i32 effects {} {
                let item: Hidden = Hidden { value: 19 };
                return item.value;
            }
            """;
        const string second = """
            module second;
            struct Hidden { value: i32 }
            pub fn second_value() -> i32 effects {} {
                let item: Hidden = Hidden { value: 23 };
                return item.value;
            }
            """;
        const string main = """
            module app.main;
            import first { first_value };
            import second { second_value };
            pub fn main() -> i32 effects {} { return first_value() + second_value(); }
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

    private static async Task TestPackageImportDiagnostics(Harness harness)
    {
        const string library = """
            module library;
            pub fn present() -> i32 effects {} { return 7; }
            fn hidden() -> i32 effects {} { return 9; }
            """;

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-import-private",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.lang"] = library,
                ["src/app/main.lang"] = """
                    module app.main;
                    import library { hidden };
                    pub fn main() -> i32 effects {} { return hidden(); }
                    """
            },
            "E_IMPORT_PRIVATE",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-import-symbol-missing",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.lang"] = library,
                ["src/app/main.lang"] = """
                    module app.main;
                    import library { absent };
                    pub fn main() -> i32 effects {} { return absent(); }
                    """
            },
            "E_IMPORT_UNRESOLVED",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-import-module-missing",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = """
                    module app.main;
                    import absent.module { value };
                    pub fn main() -> i32 effects {} { return value(); }
                    """
            },
            "E_IMPORT_UNRESOLVED",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-import-duplicate-symbol",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.lang"] = library,
                ["src/app/main.lang"] = """
                    module app.main;
                    import library { present, present };
                    pub fn main() -> i32 effects {} { return present(); }
                    """
            },
            "E_IMPORT_CONFLICT",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-import-local-conflict",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/library.lang"] = library,
                ["src/app/main.lang"] = """
                    module app.main;
                    import library { present };
                    pub fn present() -> i32 effects {} { return 3; }
                    pub fn main() -> i32 effects {} { return present(); }
                    """
            },
            "E_IMPORT_CONFLICT",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-import-cross-module-type-conflict",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/left/types.lang"] = "module left.types; pub union Shared { Available }",
                ["src/right/types.lang"] = "module right.types; pub struct Shared { value: i32 }",
                ["src/app/main.lang"] = """
                    module app.main;
                    import left.types { Shared };
                    import right.types { Shared };
                    pub fn main() -> i32 effects {} { return 1; }
                    """
            },
            "E_IMPORT_CONFLICT",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-import-function-does-not-import-return-type",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/model/choice.lang"] = """
                    module model.choice;
                    pub union Choice { Ready, Waiting }
                    pub fn create() -> Choice effects {} { return Choice.Ready; }
                    """,
                ["src/app/main.lang"] = """
                    module app.main;
                    import model.choice { create };
                    pub fn main() -> i32 effects {} {
                        return match create() {
                            Choice.Ready => 1,
                            Choice.Waiting => 0,
                        };
                    }
                    """
            },
            "E_NAME_UNRESOLVED",
            "src/app/main.lang");

        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-import-local-union-name-mismatch",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/model/choice.lang"] = """
                    module model.choice;
                    pub union Choice { Ready, Waiting }
                    pub fn create() -> Choice effects {} { return Choice.Ready; }
                    """,
                ["src/app/main.lang"] = """
                    module app.main;
                    import model.choice { create };
                    union Choice { Ready, Waiting }
                    pub fn main() -> i32 effects {} {
                        return match create() {
                            Choice.Ready => 1,
                            Choice.Waiting => 0,
                        };
                    }
                    """
            },
            "E_TYPE_MISMATCH",
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
                ["src/app/main.lang"] = "module app.main; pub fn main() -> i32 effects {} { return 1; }"
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
            LibraryPackageManifest() + "entry_module = \"app.main\"\n",
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
                ["src/app/main.lang"] = "module app.main; pub fn main() -> i32 effects {} { return 1; }"
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
                    module elsewhere.main;
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
                ["src/text.validation.lang"] = "module text.validation; pub fn main() -> i32 effects {} { return 1; }"
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
                ["src/app/main.lang"] = "module app.main; pub fn helper() -> i32 effects {} { return 1; }",
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
                ["src/app/main.lang"] = "module app.main; pub fn main(value: i32) -> i32 effects {} { return value; }"
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
                    module app.main;
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
                ["src/app/main.lang"] = "module app.main; pub fn main() -> i32 effects {} { return 17; }",
                ["src/other.lang"] = "module other; pub fn main() -> i32 effects {} { return 99; }"
            });
        AssertRunOutput("17" + Environment.NewLine, result);
    }

    private static async Task TestPackageImportedUnionExhaustiveness(Harness harness)
    {
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-imported-union-exhaustiveness",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/shared/choice.lang"] = """
                    module shared.choice;
                    pub union Choice { First, Second, Third }
                    """,
                ["src/app/main.lang"] = """
                    module app.main;
                    import shared.choice { Choice };
                    pub fn main() -> i32 effects {} {
                        let choice: Choice = Choice.First;
                        return match choice {
                            Choice.First => 1,
                            Choice.Second => 2,
                        };
                    }
                    """
            },
            "E_MATCH_NONEXHAUSTIVE",
            "src/app/main.lang");
    }

    private static async Task TestPackageImportsAreNotTransitive(Harness harness)
    {
        await ExpectPackageJsonDiagnosticAsync(
            harness,
            "package-import-not-transitive",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/base.lang"] = "module base; pub fn answer() -> i32 effects {} { return 42; }",
                ["src/middle.lang"] = """
                    module middle;
                    import base { answer };
                    pub fn wrapped() -> i32 effects {} { return answer(); }
                    """,
                ["src/app/main.lang"] = """
                    module app.main;
                    import middle { wrapped };
                    pub fn main() -> i32 effects {} { return wrapped() + answer(); }
                    """
            },
            "E_NAME_UNRESOLVED",
            "src/app/main.lang");
    }

    private static async Task TestPackageLibraryBuild(Harness harness)
    {
        var packageRoot = await harness.WritePackageAsync(
            "package-library-build",
            LibraryPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/core/math.lang"] = "module core.math; pub fn square(value: i32) -> i32 effects {} { return value * value; }"
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
            + "entry_module = \"app.main\"\n"
            + "\n[dependencies]\n"
            + "validation = \"../validation\"\n";
        const string validationSource = "module text.validation;\n"
            + "pub union NormalizeError { Empty }\n"
            + "pub fn normalize(input: Text) -> Result<Text, NormalizeError> effects {} {\n"
            + "    if input.length == 0 { return Err(NormalizeError.Empty); }\n"
            + "    return Ok(input.trim());\n"
            + "}\n"
            + "pub fn require<T, E>(value: Option<T>, error: E) -> Result<T, E> effects {} {\n"
            + "    return match value { Some(item) => Ok(item), None => Err(error) };\n"
            + "}\n";
        const string consumerSource = "module app.main;\n"
            + "import validation::text.validation { NormalizeError, normalize, require };\n"
            + "pub fn main() -> Text effects {} {\n"
            + "    let normalized: Result<Text, NormalizeError> = normalize(\" ready \");\n"
            + "    let candidate: Option<Text> = match normalized {\n"
            + "        Ok(value) => Some(value),\n"
            + "        Err(error) => None,\n"
            + "    };\n"
            + "    let required: Result<Text, NormalizeError> = require(candidate, NormalizeError.Empty);\n"
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
                ["src/app/main.lang"] = "module app.main; pub fn main() -> i32 effects {} { return 42; }"
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
                        ["src/app/main.lang"] = "module app.main; pub fn main() -> i32 effects {} { return 1; }"
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
                        ["src/app/main.lang"] = "module app.main; pub fn main() -> i32 effects {} { return 1; }"
                    }),
                ["app-dependency"] = new PackageFixture(
                    CliPackageManifest(),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app.main; pub fn main() -> i32 effects {} { return 2; }"
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
                        ["src/app/main.lang"] = "module app.main; pub fn main() -> i32 effects {} { return 1; }"
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
                    "name = \"alias-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app.main\"\n"
                    + "\n[dependencies]\nalpha = \"../alpha\"\nbeta = \"../beta\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app.main;\n"
                            + "import alpha::text.validation { alpha_token, read_alpha };\n"
                            + "import beta::text.validation { beta_token, read_beta };\n"
                            + "pub fn main() -> i32 effects {} { return read_alpha(alpha_token()) + read_beta(beta_token()); }\n"
                    }),
                ["alpha"] = new PackageFixture(LibraryPackageManifest("alpha-library"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.lang"] = "module text.validation;\n"
                        + "pub struct Token { value: i32 }\n"
                        + "pub fn alpha_token() -> Token effects {} { return Token { value: 20 }; }\n"
                        + "pub fn read_alpha(value: Token) -> i32 effects {} { return value.value + 1; }\n"
                }),
                ["beta"] = new PackageFixture(LibraryPackageManifest("beta-library"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.lang"] = "module text.validation;\n"
                        + "pub struct Token { value: i32 }\n"
                        + "pub fn beta_token() -> Token effects {} { return Token { value: 20 }; }\n"
                        + "pub fn read_beta(value: Token) -> i32 effects {} { return value.value + 1; }\n"
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
                    "name = \"identity-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app.main\"\n"
                    + "\n[dependencies]\nalpha = \"../alpha\"\nbeta = \"../beta\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app.main;\n"
                            + "import alpha::text.validation { alpha_token, read_alpha };\n"
                            + "import beta::text.validation { beta_token };\n"
                            + "pub fn main() -> i32 effects {} { return read_alpha(beta_token()); }\n"
                    }),
                ["alpha"] = new PackageFixture(LibraryPackageManifest("identity-alpha"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.lang"] = "module text.validation; pub struct Token { value: i32 }\n"
                        + "pub fn alpha_token() -> Token effects {} { return Token { value: 1 }; }\n"
                        + "pub fn read_alpha(value: Token) -> i32 effects {} { return value.value; }\n"
                }),
                ["beta"] = new PackageFixture(LibraryPackageManifest("identity-beta"), new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/text/validation.lang"] = "module text.validation; pub struct Token { value: i32 }\n"
                        + "pub fn beta_token() -> Token effects {} { return Token { value: 2 }; }\n"
                })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-cross-identity-lock", crossIdentityRoot, "lock"));
        await AssertPackageDiagnosticAsync(harness, "dependency-cross-identity-check", crossIdentityRoot, "E_TYPE_MISMATCH", "src/app/main.lang");

        var transitiveRoot = await harness.WritePackageGraphAsync(
            "dependency-alias-direct-only",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"direct-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app.main\"\n"
                    + "\n[dependencies]\nmid = \"../mid\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app.main;\n"
                            + "import mid::mid.api { wrapped };\n"
                            + "import foundation::foundation { answer };\n"
                            + "pub fn main() -> i32 effects {} { return wrapped() + answer(); }\n"
                    }),
                ["mid"] = new PackageFixture(LibraryPackageManifest("middle-library") + "\n[dependencies]\nfoundation = \"../foundation\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/mid/api.lang"] = "module mid.api; import foundation::foundation { answer }; pub fn wrapped() -> i32 effects {} { return answer(); }"
                    }),
                ["foundation"] = new PackageFixture(LibraryPackageManifest("foundation-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/foundation.lang"] = "module foundation; pub fn answer() -> i32 effects {} { return 21; }"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-transitive-lock", transitiveRoot, "lock"));
        await AssertPackageDiagnosticAsync(harness, "dependency-transitive-alias-check", transitiveRoot, "E_IMPORT_UNRESOLVED", "src/app/main.lang");
    }

    private static async Task TestDependencyImportDiagnostics(Harness harness)
    {
        var packageRoot = await harness.WritePackageGraphAsync(
            "dependency-import-visibility",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"visibility-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app.main\"\n"
                    + "\n[dependencies]\nvalidation = \"../validation\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app.main;\n"
                            + "import validation::text.validation { hidden, absent };\n"
                            + "pub fn main() -> i32 effects {} { return hidden() + absent(); }\n"
                    }),
                ["validation"] = new PackageFixture(LibraryPackageManifest("visibility-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/text/validation.lang"] = "module text.validation;\n"
                            + "pub fn present() -> i32 effects {} { return 1; }\n"
                            + "fn hidden() -> i32 effects {} { return 2; }\n"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-import-visibility-lock", packageRoot, "lock"));
        var visibility = await harness.InvokePackageDirectoryAsync("dependency-import-visibility-check", packageRoot, "check", "--json");
        AssertEqual(1, visibility.ExitCode, Describe(visibility));
        var visibilityDiagnostics = ParseDiagnosticSnapshots(visibility.StandardOutput);
        AssertTrue(visibilityDiagnostics.Any(diagnostic => diagnostic.Code == "E_IMPORT_PRIVATE"),
            $"A private declaration in a path dependency must be rejected. {visibility.StandardOutput}");
        AssertTrue(visibilityDiagnostics.Any(diagnostic => diagnostic.Code == "E_IMPORT_UNRESOLVED"),
            $"A missing declaration in a path dependency must be rejected. {visibility.StandardOutput}");

        var malformedAliasRoot = await harness.WritePackageGraphAsync(
            "dependency-malformed-alias-import",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"malformed-import-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app.main\"\n"
                    + "\n[dependencies]\nvalidation = \"../validation\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app.main; import 42::text.validation { present }; pub fn main() -> i32 effects {} { return present(); }"
                    }),
                ["validation"] = new PackageFixture(LibraryPackageManifest("malformed-import-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/text/validation.lang"] = "module text.validation; pub fn present() -> i32 effects {} { return 1; }"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync("dependency-malformed-alias-lock", malformedAliasRoot, "lock"));
        await AssertPackageDiagnosticAsync(harness, "dependency-malformed-alias-check", malformedAliasRoot, "E_SYNTAX", "src/app/main.lang");
    }

    private static async Task TestDependencyFilesystemSafety(Harness harness)
    {
        var invalidSourceRoot = await harness.WritePackageGraphAsync(
            "dependency-source-root-escape",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    "name = \"source-root-consumer\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app.main\"\n"
                    + "\n[dependencies]\nvalidation = \"../validation\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = "module app.main; pub fn main() -> i32 effects {} { return 1; }"
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
            "module app.main; pub fn main() -> i32 effects {} { return 1; }");
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
        const string main = "module app.main; pub fn main() -> i32 effects {} { return 41; }";
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
        AssertTrue(source.Contains("import validation::text.validation { NormalizeError, normalize, require };", StringComparison.Ordinal),
            "The maintained CLI must import the validation and inferred generic APIs through its dependency alias.");
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
                module app.main;
                import text.validation { NormalizeError, normalize };
                pub fn main() -> Text effects {} {
                    return match normalize("") {
                        Ok(value) => "unexpected success",
                        Err(error) => match error {
                            NormalizeError.Empty => "empty",
                        },
                    };
                }
                """, "empty" + Environment.NewLine),
            ("text-validation-nonempty", $$"""
                module app.main;
                import text.validation { NormalizeError, normalize };
                pub fn main() -> Text effects {} {
                    return match normalize("{{unicodeInput}}") {
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
                    module app.main;
                    import text.validation { NormalizeError, require };

                    pub fn main() -> i32 effects {} {
                        let text_option: Option<Text> = Some("hello 😀");
                        let text_result: Result<Text, NormalizeError> = require(text_option, NormalizeError.Empty);
                        let number_option: Option<i32> = Some(35);
                        let number_result: Result<i32, Text> = require(number_option, "missing");
                        let text_length: i32 = match text_result {
                            Ok(value) => value.length,
                            Err(error) => match error {
                                NormalizeError.Empty => 0,
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
                    module app.main;
                    import text.validation { require };

                    pub fn from_some() -> Result<Text, Text> effects {} {
                        return require(Some("present"), "fallback");
                    }
                    pub fn from_none() -> Result<Text, Text> effects {} {
                        return require(None, "fallback");
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
            module harness.generic_main;
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

    private static string CliPackageManifest(string entryModule = "app.main") =>
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
        const string source = "module harness.aot_cli;\n"
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
        const string source = "module harness.aot_library;\n"
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

        const string source = "module harness.aot_smoke;\n"
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
                ["src/app/main.lang"] = "module app.main; pub fn main() -> i32 effects {} { return 41; }"
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

    private static async Task<ProcessResult> ExecuteNativeAsync(string executablePath, TimeSpan timeout)
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
            module harness.invalid_main;
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
            module harness.process_failure;
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
        const string source = "module harness.overflow;\n"
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
        const string firstSource = "module harness.parallel_a;\n"
            + "pub fn main() -> i32 effects {} { return 17; }\n";
        const string secondSource = "module harness.parallel_b;\n"
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
