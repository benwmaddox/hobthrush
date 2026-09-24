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
            Console.Error.WriteLine("Could not locate al.slnx and src/Al/Al.csproj.");
            return 2;
        }

        var compilerDll = Path.Combine(repositoryRoot, "src", "Al", "bin", "Release", "net10.0", "al.dll");
        if (!File.Exists(compilerDll))
        {
            Console.Error.WriteLine($"Compiler build not found: {compilerDll}");
            Console.Error.WriteLine("Build the solution before running the integration harness.");
            return 2;
        }

        var dotnet = GetDotnetPath(repositoryRoot);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "al-integration-tests", Guid.NewGuid().ToString("N"));
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
            ("type mismatches fail before generated code", TestTypeMismatchContexts),
            ("match validation reports omissions, duplicates, and payload arity", TestInvalidMatches),
            ("null is rejected as both an expression and identifier", TestNull),
            ("deep nesting produces a structured diagnostic within the timeout", TestDeepNesting),
            ("10,000-term additive chains fail with an expression-depth diagnostic", TestLongBinaryChain),
            ("signed i32 literals and unary negation are checked", TestSignedI32),
            ("checked i32 overflow exits through the generic runtime fault contract", TestCheckedOverflow),
            ("library build writes a durable DLL without a main function", TestLibraryBuild),
            ("invalid main signatures receive an entrypoint diagnostic", TestInvalidEntrypoint),
            ("AL_DOTNET launch failures become process diagnostics", TestDotnetLaunchFailure),
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
        var source = await File.ReadAllTextAsync(Path.Combine(harness.RepositoryRoot, "examples", "pure", "src", "main.al"));
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
        var firstPath = await harness.WriteSourceAsync(sharedSourceDirectory, "first.al", firstSource);
        var secondPath = await harness.WriteSourceAsync(sharedSourceDirectory, "second.al", secondSource);
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
                if (File.Exists(Path.Combine(directory.FullName, "al.slnx"))
                    && File.Exists(Path.Combine(directory.FullName, "src", "Al", "Al.csproj")))
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
            var sourcePath = await WriteSourceAsync(sourceDirectory, "main.al", source);
            return await InvokeFileWithTimeoutAsync(caseName, sourcePath, command, ProcessTimeout, hostOverride);
        }

        public async Task<ProcessResult> InvokeWithTimeoutAsync(string caseName, string command, string source, TimeSpan timeout, params string[] additionalArguments)
        {
            var sourceDirectory = CreateSourceDirectory(caseName);
            var sourcePath = await WriteSourceAsync(sourceDirectory, "main.al", source);
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
            startInfo.Environment["AL_DOTNET"] = dotnetHostOverride ?? dotnet;

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException($"Could not start the al compiler process for {caseName}.");

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
