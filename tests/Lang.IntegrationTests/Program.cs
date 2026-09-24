using System.Diagnostics;
using System.Text.Json;
using System.Text;

return await IntegrationTests.RunAsync();

internal static class IntegrationTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);

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
            ("invalid main signatures receive an entrypoint diagnostic", TestInvalidEntrypoint),
            ("LANG_DOTNET launch failures become process diagnostics", TestDotnetLaunchFailure),
            ("concurrent runs keep their generated outputs isolated", TestParallelRuns)
        };

        var failures = 0;
        try
        {
            foreach (var test in cases)
            {
                try
                {
                    await test.Run(harness);
                    Console.WriteLine($"PASS {test.Name}");
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

        Console.WriteLine($"{cases.Length - failures} passed, {failures} failed");
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

        using var document = JsonDocument.Parse(result.StandardOutput);
        var diagnosticsElement = document.RootElement.GetProperty("diagnostics");
        var diagnostics = diagnosticsElement.EnumerateArray().Select(item => new DiagnosticSnapshot(
            item.GetProperty("code").GetString() ?? string.Empty,
            item.GetProperty("message").GetString() ?? string.Empty,
            item.GetProperty("file").GetString() ?? string.Empty,
            item.GetProperty("range").GetProperty("startLine").GetInt32(),
            item.GetProperty("range").GetProperty("startColumn").GetInt32(),
            item.GetProperty("range").GetProperty("endLine").GetInt32(),
            item.GetProperty("range").GetProperty("endColumn").GetInt32())).ToArray();
        AssertTrue(diagnostics.Any(diagnostic => expectedCodes.Contains(diagnostic.Code, StringComparer.Ordinal)),
            $"Expected one of [{string.Join(", ", expectedCodes)}]; got {string.Join(", ", diagnostics.Select(diagnostic => diagnostic.Code))}.");
        AssertTrue(diagnostics.All(diagnostic => diagnostic.File == harness.LastSourcePath
            && diagnostic.StartLine > 0 && diagnostic.StartColumn > 0
            && diagnostic.EndLine > 0 && diagnostic.EndColumn > diagnostic.StartColumn),
            "Every diagnostic should include the temporary source path and a non-empty range.");
        return diagnostics;
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

        public async Task<ProcessResult> InvokeFileWithTimeoutAsync(string caseName, string sourcePath, string command, TimeSpan timeout, string? dotnetHostOverride, params string[] additionalArguments)
        {
            var sourceDirectory = Path.GetDirectoryName(sourcePath)
                ?? throw new InvalidOperationException($"Source file {sourcePath} has no parent directory.");
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
            startInfo.ArgumentList.Add(compilerDll);
            startInfo.ArgumentList.Add(command);
            startInfo.ArgumentList.Add(sourcePath);
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

    private sealed record DiagnosticSnapshot(string Code, string Message, string File, int StartLine, int StartColumn, int EndLine, int EndColumn);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
