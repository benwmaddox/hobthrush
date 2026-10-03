using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private sealed record WideIntegerType(string Name, string Suffix, BigInteger Minimum, BigInteger Maximum, bool IsSigned);

    private static async Task TestWideIntegers(Harness harness)
    {
        var types = new[]
        {
            CreateWideIntegerType("i64", 64, signed: true),
            CreateWideIntegerType("u32", 32, signed: false),
            CreateWideIntegerType("u64", 64, signed: false)
        };
        var packageRoot = await CreateWideIntegerRuntimePackage(harness, types);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "wide-integers-runtime-lock", packageRoot, "lock"));

        var managed = await harness.InvokePackageDirectoryAsync("wide-integers-managed", packageRoot, "run");
        AssertRunOutput("wide-integers-ok" + Environment.NewLine, managed);

        await TestWideIntegerRuntimeFaults(harness, types);
        await TestWideIntegerDiagnostics(harness);
        await TestWideIntegerCodecBoundaries(harness);
        await TestWideIntegerMetadataAndReceipt(harness);

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("Wide integer NativeAOT tests require Windows x64 or Linux x64.");

        var aot = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "wide-integers-aot", packageRoot, "build", AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, aot.ExitCode, Describe(aot));
        const string executablePrefix = "Built native executable: ";
        AssertTrue(aot.StandardOutput.StartsWith(executablePrefix, StringComparison.Ordinal), Describe(aot));
        var executable = aot.StandardOutput[executablePrefix.Length..].TrimEnd('\r', '\n');
        AssertTrue(Path.IsPathFullyQualified(executable) && File.Exists(executable),
            $"Expected a NativeAOT wide integer executable at {executable}.");
        AssertRunOutput("wide-integers-ok" + Environment.NewLine,
            await ExecuteNativeAsync(executable, TimeSpan.FromSeconds(30)));
        await TestWideIntegerNativeFaults(harness);
    }

    private static WideIntegerType CreateWideIntegerType(string name, int bits, bool signed)
    {
        var magnitude = BigInteger.One << (signed ? bits - 1 : bits);
        return signed
            ? new WideIntegerType(name, name, -magnitude, magnitude - BigInteger.One, true)
            : new WideIntegerType(name, name, BigInteger.Zero, magnitude - BigInteger.One, false);
    }

    private static string FormatWideInteger(BigInteger value, string suffix) =>
        value.ToString(CultureInfo.InvariantCulture) + suffix;

    private static async Task<string> CreateWideIntegerRuntimePackage(
        Harness harness,
        IReadOnlyList<WideIntegerType> types)
    {
        var i64 = types.Single(type => type.Name == "i64");
        var u32 = types.Single(type => type.Name == "u32");
        var u64 = types.Single(type => type.Name == "u64");
        var validationSource = await File.ReadAllTextAsync(Path.Combine(
            harness.RepositoryRoot, "examples", "text-validation", "src", "text", "validation.hob"));
        const string modelSource = """
            module numeric;

            pub struct Bounds { signed: i64, narrow: u32, wide: u64 }
            pub newtype WideKey = u64;
            pub union Number { Signed(i64), Narrow(u32), Wide(u64) }

            pub trait NumericWitness {
                fn accepts(value: Self, signed: i64, narrow: u32, wide: u64) -> bool effects {};
            }

            pub fn identity<T>(value: T) -> T effects {} { return value; }
            pub fn keep_i64(value: i64) -> i64 effects {} { return value; }
            pub fn keep_u32(value: u32) -> u32 effects {} { return value; }
            pub fn keep_u64(value: u64) -> u64 effects {} { return value; }

            fn accepts_i64(value: i64, signed: i64, narrow: u32, wide: u64) -> bool effects {} {
                if value == signed { if narrow == 7u32 { return wide == 9u64; } }
                return false;
            }
            fn accepts_u32(value: u32, signed: i64, narrow: u32, wide: u64) -> bool effects {} {
                if value == narrow { if signed == 42i64 { return wide == 9u64; } }
                return false;
            }
            fn accepts_u64(value: u64, signed: i64, narrow: u32, wide: u64) -> bool effects {} {
                if value == wide { if signed == 42i64 { return narrow == 7u32; } }
                return false;
            }

            pub impl self::numeric::NumericWitness for i64 { accepts = self::numeric::accepts_i64; }
            pub impl self::numeric::NumericWitness for u32 { accepts = self::numeric::accepts_u32; }
            pub impl self::numeric::NumericWitness for u64 { accepts = self::numeric::accepts_u64; }
            """;

        var mainSource = new StringBuilder();
        mainSource.AppendLine("module app::main;");
        mainSource.AppendLine("pub fn main() -> Text effects {} {");
        mainSource.AppendLine($"    let signed_min: i64 = {FormatWideInteger(i64.Minimum, i64.Suffix)};");
        mainSource.AppendLine($"    let signed_max: i64 = {FormatWideInteger(i64.Maximum, i64.Suffix)};");
        mainSource.AppendLine($"    let narrow_min: u32 = {FormatWideInteger(u32.Minimum, u32.Suffix)};");
        mainSource.AppendLine($"    let narrow_max: u32 = {FormatWideInteger(u32.Maximum, u32.Suffix)};");
        mainSource.AppendLine($"    let wide_min: u64 = {FormatWideInteger(u64.Minimum, u64.Suffix)};");
        mainSource.AppendLine($"    let wide_max: u64 = {FormatWideInteger(u64.Maximum, u64.Suffix)};");

        foreach (var type in types)
        {
            var value = type.Name switch
            {
                "i64" => "signed_min",
                "u32" => "narrow_max",
                _ => "wide_max"
            };
            mainSource.AppendLine($"    let copied_{type.Name}: {type.Name} = wide::numeric::identity({value});");
            mainSource.AppendLine($"    if copied_{type.Name} == {value} {{ }} else {{ return \"wide-integers-failed\"; }}");
            mainSource.AppendLine($"    if wide::numeric::keep_{type.Name}({value}) == {value} {{ }} else {{ return \"wide-integers-failed\"; }}");
        }

        var oneI64 = FormatWideInteger(BigInteger.One, i64.Suffix);
        AppendWideCheck(mainSource, $"signed_min == {FormatWideInteger(i64.Minimum, i64.Suffix)}");
        AppendWideCheck(mainSource, $"signed_max == {FormatWideInteger(i64.Maximum, i64.Suffix)}");
        AppendWideCheck(mainSource, "signed_min < signed_max");
        AppendWideCheck(mainSource, "signed_max >= signed_min");
        AppendWideCheck(mainSource, $"signed_min + {oneI64} == {FormatWideInteger(i64.Minimum + BigInteger.One, i64.Suffix)}");
        AppendWideCheck(mainSource, $"signed_max - {oneI64} == {FormatWideInteger(i64.Maximum - BigInteger.One, i64.Suffix)}");
        AppendWideCheck(mainSource, "-42i64 == -42i64");
        AppendWideCheck(mainSource,
            $"({FormatWideInteger(new BigInteger(-3), i64.Suffix)} * {FormatWideInteger(new BigInteger(7), i64.Suffix)}) == {FormatWideInteger(new BigInteger(-3) * new BigInteger(7), i64.Suffix)}");

        var oneU32 = FormatWideInteger(BigInteger.One, u32.Suffix);
        AppendWideCheck(mainSource, "narrow_min == 0u32");
        AppendWideCheck(mainSource, $"narrow_max == {FormatWideInteger(u32.Maximum, u32.Suffix)}");
        AppendWideCheck(mainSource, "narrow_min < narrow_max");
        AppendWideCheck(mainSource, "narrow_max >= narrow_min");
        AppendWideCheck(mainSource, $"narrow_min + {oneU32} == {FormatWideInteger(u32.Minimum + BigInteger.One, u32.Suffix)}");
        AppendWideCheck(mainSource, $"narrow_max - {oneU32} == {FormatWideInteger(u32.Maximum - BigInteger.One, u32.Suffix)}");
        AppendWideCheck(mainSource,
            $"({FormatWideInteger(new BigInteger(3), u32.Suffix)} * {FormatWideInteger(new BigInteger(7), u32.Suffix)}) == {FormatWideInteger(new BigInteger(3) * new BigInteger(7), u32.Suffix)}");

        var oneU64 = FormatWideInteger(BigInteger.One, u64.Suffix);
        AppendWideCheck(mainSource, "wide_min == 0u64");
        AppendWideCheck(mainSource, $"wide_max == {FormatWideInteger(u64.Maximum, u64.Suffix)}");
        AppendWideCheck(mainSource, "wide_min < wide_max");
        AppendWideCheck(mainSource, "wide_max >= wide_min");
        AppendWideCheck(mainSource, $"wide_min + {oneU64} == {FormatWideInteger(u64.Minimum + BigInteger.One, u64.Suffix)}");
        AppendWideCheck(mainSource, $"wide_max - {oneU64} == {FormatWideInteger(u64.Maximum - BigInteger.One, u64.Suffix)}");
        AppendWideCheck(mainSource,
            $"({FormatWideInteger(new BigInteger(3), u64.Suffix)} * {FormatWideInteger(new BigInteger(7), u64.Suffix)}) == {FormatWideInteger(new BigInteger(3) * new BigInteger(7), u64.Suffix)}");

        mainSource.AppendLine("    let default_i32: i32 = 21;");
        AppendWideCheck(mainSource, "default_i32 + 1 == 22");
        mainSource.AppendLine("    let signed_values: List<i64> = [signed_min, signed_max];");
        mainSource.AppendLine("    let signed_values_copy: List<i64> = [signed_min, signed_max];");
        mainSource.AppendLine("    let different_signed_values: List<i64> = [signed_min, signed_min];");
        AppendWideCheck(mainSource, "signed_values.length == 2");
        AppendWideCheck(mainSource, "signed_values == signed_values_copy");
        AppendWideCheck(mainSource, "signed_values != different_signed_values");
        mainSource.AppendLine("    let first_signed: Option<i64> = signed_values.get(0);");
        mainSource.AppendLine("    let expected_first_signed: Option<i64> = Some(signed_min);");
        AppendWideCheck(mainSource, "first_signed == expected_first_signed");
        mainSource.AppendLine("    let empty_narrow_values: Map<Text, u32> = Map.empty();");
        mainSource.AppendLine("    let narrow_values: Map<Text, u32> = empty_narrow_values.set(\"maximum\", narrow_max);");
        mainSource.AppendLine("    let actual_narrow: Option<u32> = narrow_values.get(\"maximum\");");
        mainSource.AppendLine("    let expected_narrow: Option<u32> = Some(narrow_max);");
        AppendWideCheck(mainSource, "actual_narrow == expected_narrow");
        mainSource.AppendLine("    let bounds: wide::numeric::Bounds = wide::numeric::Bounds { signed: signed_min, narrow: narrow_max, wide: wide_max };");
        mainSource.AppendLine("    let equal_bounds: wide::numeric::Bounds = wide::numeric::Bounds { signed: signed_min, narrow: narrow_max, wide: wide_max };");
        mainSource.AppendLine("    let different_bounds: wide::numeric::Bounds = wide::numeric::Bounds { signed: signed_min, narrow: narrow_min, wide: wide_max };");
        AppendWideCheck(mainSource, "bounds == equal_bounds");
        AppendWideCheck(mainSource, "bounds != different_bounds");
        AppendWideCheck(mainSource, "bounds.signed == signed_min");
        AppendWideCheck(mainSource, "bounds.narrow == narrow_max");
        AppendWideCheck(mainSource, "bounds.wide == wide_max");
        mainSource.AppendLine("    let key: wide::numeric::WideKey = wide::numeric::WideKey.wrap(wide_max);");
        AppendWideCheck(mainSource, "key.value == wide_max");
        mainSource.AppendLine("    let selected: wide::numeric::Number = wide::numeric::Number.Wide(wide_max);");
        mainSource.AppendLine("    let equal_selected: wide::numeric::Number = wide::numeric::Number.Wide(wide_max);");
        mainSource.AppendLine("    let different_selected: wide::numeric::Number = wide::numeric::Number.Wide(wide_min);");
        AppendWideCheck(mainSource, "selected == equal_selected");
        AppendWideCheck(mainSource, "selected != different_selected");
        mainSource.AppendLine("    let selected_wide: bool = match selected {");
        mainSource.AppendLine("        wide::numeric::Number.Signed(_) => false,");
        mainSource.AppendLine("        wide::numeric::Number.Narrow(_) => false,");
        mainSource.AppendLine("        wide::numeric::Number.Wide(value) => value == wide_max,");
        mainSource.AppendLine("    };");
        AppendWideCheck(mainSource, "selected_wide");
        AppendWideCheck(mainSource, "wide::numeric::NumericWitness.accepts(42i64, 42i64, 7u32, 9u64)");
        AppendWideCheck(mainSource, "wide::numeric::NumericWitness.accepts(7u32, 42i64, 7u32, 9u64)");
        AppendWideCheck(mainSource, "wide::numeric::NumericWitness.accepts(9u64, 42i64, 7u32, 9u64)");
        mainSource.AppendLine("    let missing: Option<u32> = None;");
        mainSource.AppendLine($"    let too_long: Result<u32, validation::text::validation::NormalizeError> = validation::text::validation::require(missing, validation::text::validation::NormalizeError.TooLong(narrow_max));");
        mainSource.AppendLine("    let carried_max: u32 = match too_long {");
        mainSource.AppendLine("        Ok(_) => 0u32,");
        mainSource.AppendLine("        Err(error) => match error {");
        mainSource.AppendLine("            validation::text::validation::NormalizeError.Empty => 0u32,");
        mainSource.AppendLine("            validation::text::validation::NormalizeError.TooLong(max) => max,");
        mainSource.AppendLine("        },");
        mainSource.AppendLine("    };");
        AppendWideCheck(mainSource, "carried_max == narrow_max");
        mainSource.AppendLine("    return \"wide-integers-ok\";");
        mainSource.AppendLine("}");

        return await harness.WritePackageGraphAsync(
            "wide-integers-runtime",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\nwide = \"../wide\"\nvalidation = \"../validation\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = mainSource.ToString()
                    }),
                ["wide"] = new PackageFixture(
                    LibraryPackageManifest("wide-number-models"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/numeric.hob"] = modelSource
                    }),
                ["validation"] = new PackageFixture(
                    LibraryPackageManifest("text-validation-library"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/text/validation.hob"] = validationSource
                    })
            });
    }

    private static void AppendWideCheck(StringBuilder source, string expression) =>
        source.AppendLine($"    if ({expression}) {{ }} else {{ return \"wide-integers-failed\"; }}");

    private static async Task TestWideIntegerRuntimeFaults(
        Harness harness,
        IReadOnlyList<WideIntegerType> types)
    {
        var cases = new List<(string Name, WideIntegerType Type, string Operator, BigInteger Left, BigInteger Right)>();
        foreach (var type in types)
        {
            if (type.IsSigned)
            {
                cases.Add(($"{type.Name}-add-overflow", type, "+", type.Maximum, BigInteger.One));
                cases.Add(($"{type.Name}-subtract-underflow", type, "-", type.Minimum, BigInteger.One));
                cases.Add(($"{type.Name}-multiply-overflow", type, "*", type.Minimum, -BigInteger.One));
            }
            else
            {
                cases.Add(($"{type.Name}-add-overflow", type, "+", type.Maximum, BigInteger.One));
                cases.Add(($"{type.Name}-subtract-underflow", type, "-", BigInteger.Zero, BigInteger.One));
                cases.Add(($"{type.Name}-multiply-overflow", type, "*", type.Maximum, new BigInteger(2)));
            }
        }

        foreach (var (name, type, operation, left, right) in cases)
        {
            var source = $$"""
                module harness::{{name.Replace('-', '_')}};
                fn apply(left: {{type.Name}}, right: {{type.Name}}) -> {{type.Name}} effects {} {
                    return left {{operation}} right;
                }
                pub fn main() -> bool effects {} {
                    let left: {{type.Name}} = {{FormatWideInteger(left, type.Suffix)}};
                    let right: {{type.Name}} = {{FormatWideInteger(right, type.Suffix)}};
                    let overflowed: {{type.Name}} = self::harness::{{name.Replace('-', '_')}}::apply(left, right);
                    return overflowed == left;
                }
                """;
            await AssertWideIntegerRuntimeFault(harness, name, source);
        }

        const string negateMinimum = """
            module harness::wide_i64_negate_minimum;
            fn negate(value: i64) -> i64 effects {} { return -value; }
            pub fn main() -> bool effects {} {
                let minimum: i64 = -9223372036854775808i64;
                let overflowed: i64 = self::harness::wide_i64_negate_minimum::negate(minimum);
                return overflowed == minimum;
            }
            """;
        await AssertWideIntegerRuntimeFault(harness, "i64-negate-minimum", negateMinimum);
    }

    private static async Task AssertWideIntegerRuntimeFault(Harness harness, string caseName, string source)
    {
        var check = await harness.InvokeAsync("wide-integer-" + caseName + "-check", "check", source, "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));
        var run = await harness.InvokeAsync("wide-integer-" + caseName + "-run", "run", source);
        AssertEqual(70, run.ExitCode, Describe(run));
        AssertEqual(string.Empty, run.StandardOutput, Describe(run));
        AssertEqual("Runtime fault", run.StandardError.TrimEnd('\r', '\n'), Describe(run));
        AssertTrue(!run.StandardError.Contains("Exception", StringComparison.Ordinal)
            && !run.StandardError.Contains(" at ", StringComparison.Ordinal),
            "Checked wide integer faults must not reveal generated exception details or stack traces.");
    }

    private static async Task TestWideIntegerNativeFaults(Harness harness)
    {
        const string source = """
            module app::main;

            pub union FaultError { Failed }

            command fault {
                help "Exercise checked wide integer faults.";
                argument mode: i32 help "Select the numeric width to fault.";
                handler: self::app::main::run;
                error: self::app::main::describe;
            }

            fn negate_i64(value: i64) -> i64 effects {} { return -value; }
            fn add_u32(left: u32, right: u32) -> u32 effects {} { return left + right; }
            fn subtract_u64(left: u64, right: u64) -> u64 effects {} { return left - right; }

            pub fn run(args: self::app::main::FaultArgs) -> Result<Text, self::app::main::FaultError> effects {} {
                if args.mode == 1 {
                    let minimum: i64 = -9223372036854775808i64;
                    let result: i64 = self::app::main::negate_i64(minimum);
                    return Ok("unreachable");
                }
                if args.mode == 2 {
                    let result: u32 = self::app::main::add_u32(4294967295u32, 1u32);
                    return Ok("unreachable");
                }
                if args.mode == 3 {
                    let result: u64 = self::app::main::subtract_u64(0u64, 1u64);
                    return Ok("unreachable");
                }
                return Err(self::app::main::FaultError.Failed);
            }

            pub fn describe(error: self::app::main::FaultError) -> Text effects {} {
                return match error { self::app::main::FaultError.Failed => "invalid mode" };
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "wide-integer-aot-faults",
            CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source
            });
        var build = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "wide-integer-aot-faults-build", packageRoot, "build", AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, build.ExitCode, Describe(build));
        const string executablePrefix = "Built native executable: ";
        AssertTrue(build.StandardOutput.StartsWith(executablePrefix, StringComparison.Ordinal), Describe(build));
        var executable = build.StandardOutput[executablePrefix.Length..].TrimEnd('\r', '\n');
        AssertTrue(Path.IsPathFullyQualified(executable) && File.Exists(executable),
            $"Expected a NativeAOT wide integer fault executable at {executable}.");

        foreach (var (name, mode) in new[] { ("i64-negation", "1"), ("u32-addition", "2"), ("u64-underflow", "3") })
        {
            var run = await ExecuteNativeAsync(executable, TimeSpan.FromSeconds(30), "fault", "--", mode);
            AssertEqual(70, run.ExitCode, $"NativeAOT {name} must use the generic runtime-fault exit. {Describe(run)}");
            AssertEqual(string.Empty, run.StandardOutput, $"NativeAOT {name} must not print a partial result. {Describe(run)}");
            AssertEqual("Runtime fault", run.StandardError.TrimEnd('\r', '\n'),
                $"NativeAOT {name} must preserve the generic runtime-fault message. {Describe(run)}");
            AssertTrue(!run.StandardError.Contains("Exception", StringComparison.Ordinal)
                && !run.StandardError.Contains(" at ", StringComparison.Ordinal),
                $"NativeAOT {name} must not reveal backend exception details or a stack trace.");
        }
    }

    private static async Task TestWideIntegerDiagnostics(Harness harness)
    {
        const string mixedAddition = """
            module harness::wide_mixed_addition;
            fn bad() -> i64 effects {} { return 1i64 + 1u32; }
            """;
        await AssertWideIntegerDiagnosticAsync(harness, "mixed-addition", mixedAddition,
            "E_TYPE_MISMATCH",
            "Arithmetic '+' requires operands with the same integer type; found 'i64' and 'u32'",
            "1i64 + 1u32", "+");

        const string mixedComparison = """
            module harness::wide_mixed_comparison;
            fn bad() -> bool effects {} { return 1i64 < 1u32; }
            """;
        await AssertWideIntegerDiagnosticAsync(harness, "mixed-comparison", mixedComparison,
            "E_TYPE_MISMATCH",
            "Comparison '<' requires operands with the same integer type; found 'i64' and 'u32'",
            "1i64 < 1u32", "<");

        const string mixedEquality = """
            module harness::wide_mixed_equality;
            fn bad() -> bool effects {} { return 1i64 == 1u32; }
            """;
        await AssertWideIntegerDiagnosticAsync(harness, "mixed-equality", mixedEquality,
            "E_TYPE_MISMATCH",
            "Comparison '==' requires matching immutable values with structural equality; found 'i64' and 'u32'",
            "1i64 == 1u32", "==");

        const string mixedInitialization = """
            module harness::wide_mixed_initialization;
            fn bad() -> i64 effects {} {
                let value: i64 = 1u32;
                return value;
            }
            """;
        await AssertWideIntegerDiagnosticAsync(harness, "mixed-initialization", mixedInitialization,
            "E_TYPE_MISMATCH", "Expected 'i64', found 'u32'", "1u32", "1u32");

        const string mixedAssignment = """
            module harness::wide_mixed_assignment;
            fn bad() -> i64 effects {} {
                var value: i64 = 1i64;
                value = 1u32;
                return value;
            }
            """;
        await AssertWideIntegerDiagnosticAsync(harness, "mixed-assignment", mixedAssignment,
            "E_TYPE_MISMATCH", "Expected 'i64', found 'u32'", "value = 1u32", "1u32");

        const string mixedReturn = """
            module harness::wide_mixed_return;
            fn bad() -> i64 effects {} { return 1u32; }
            """;
        await AssertWideIntegerDiagnosticAsync(harness, "mixed-return", mixedReturn,
            "E_TYPE_MISMATCH", "Expected 'i64', found 'u32'", "return 1u32", "1u32");

        const string mixedCall = """
            module harness::wide_mixed_call;
            fn consume(value: i64) -> i64 effects {} { return value; }
            fn bad() -> i64 effects {} { return self::harness::wide_mixed_call::consume(1u32); }
            """;
        await AssertWideIntegerDiagnosticAsync(harness, "mixed-call", mixedCall,
            "E_TYPE_MISMATCH", "Expected 'i64', found 'u32'", "consume(1u32)", "1u32");

        const string unsuffixedDoesNotWiden = """
            module harness::wide_unsuffixed_i32;
            fn bad() -> i64 effects {} { return 5; }
            """;
        await AssertWideIntegerDiagnosticAsync(harness, "unsuffixed-remains-i32", unsuffixedDoesNotWiden,
            "E_TYPE_MISMATCH", "Expected 'i64', found 'i32'", "return 5", "5");

        const string unsignedNegation = """
            module harness::wide_unsigned_negation;
            fn bad() -> u32 effects {} { return -1u32; }
            """;
        await AssertWideIntegerDiagnosticAsync(harness, "unsigned-negation", unsignedNegation,
            "E_TYPE_MISMATCH", "Unary '-' is not supported for 'u32'", "-1u32", "-");

        const string wideUnsignedNegation = """
            module harness::wide_unsigned_negation;
            fn bad() -> u64 effects {} { return -1u64; }
            """;
        await AssertWideIntegerDiagnosticAsync(harness, "wide-unsigned-negation", wideUnsignedNegation,
            "E_TYPE_MISMATCH", "Unary '-' is not supported for 'u64'", "-1u64", "-");

        var literalCases = new (string Name, string Type, string Literal, string Message)[]
        {
            ("i64-positive-overflow", "i64", "9223372036854775808i64", "Integer literal is outside i64 range"),
            ("u32-positive-overflow", "u32", "4294967296u32", "Integer literal is outside u32 range"),
            ("u64-positive-overflow", "u64", "18446744073709551616u64", "Integer literal is outside u64 range")
        };
        foreach (var (name, type, literal, message) in literalCases)
        {
            var source = $"module harness::{name.Replace('-', '_')};\n"
                + $"fn bad() -> {type} effects {{}} {{ return {literal}; }}\n";
            await AssertWideIntegerDiagnosticAsync(harness, name, source,
                "E_TYPE_MISMATCH", message, literal, literal);
        }
    }

    private static async Task TestWideIntegerCodecBoundaries(Harness harness)
    {
        const string cliMain = """
            module app::main;
            command scan {
                help "Scan a numeric input.";
                argument input: u32 help "Unsigned input.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string cliHandlers = """
            module handlers;
            pub union ScanError { Failed }
            pub fn run(args: self::app::main::ScanArgs) -> Result<Text, self::handlers::ScanError> effects {} {
                return Ok("ok");
            }
            pub fn describe(error: self::handlers::ScanError) -> Text effects {} {
                return match error { self::handlers::ScanError.Failed => "scan failed" };
            }
            """;
        var cliRoot = await harness.WritePackageAsync(
            "wide-integer-cli-codec", CliPackageManifest(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = cliMain,
                ["src/handlers.hob"] = cliHandlers
            });
        await AssertWideIntegerPackageDiagnosticAsync(harness, "wide-integer-cli-codec", cliRoot,
            "src/app/main.hob", cliMain, "E_COMMAND_DECL",
            "Command input type 'u32' is not supported; use FilePath, Text, or i32",
            "argument input: u32", "argument");

        const string webManifest = "name = \"wide-integer-web\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n\n[capabilities]\nnet.listen = \"allow\"\n";
        const string routeSource = """
            module app::main;
            pub union Reply { Found(i32), Empty }
            fn get(id: i64) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Found(0);
            }
            route GET "/items/{id}" {
                path id: i64;
                handler: self::app::main::get;
                response Found: 200 json i32;
                response Empty: 404;
            }
            """;
        var routeRoot = await harness.WritePackageAsync(
            "wide-integer-route-codec", webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = routeSource });
        await AssertWideIntegerPackageDiagnosticAsync(harness, "wide-integer-route-codec", routeRoot,
            "src/app/main.hob", routeSource, "E_ROUTE_BINDING", "Path bindings support only Text and i32",
            "path id: i64", "i64");

        const string routePayloadSource = """
            module app::main;
            pub struct Payload { value: u64 }
            pub union Reply { Found(self::app::main::Payload), Empty }
            fn get() -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Found(self::app::main::Payload { value: 1u64 });
            }
            route GET "/items" {
                handler: self::app::main::get;
                response Found: 200 json self::app::main::Payload;
                response Empty: 404;
            }
            """;
        var routePayloadRoot = await harness.WritePackageAsync(
            "wide-integer-route-payload-codec", webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = routePayloadSource });
        await AssertWideIntegerPackageDiagnosticAsync(harness, "wide-integer-route-payload-codec", routePayloadRoot,
            "src/app/main.hob", routePayloadSource, "E_ROUTE_CODEC_UNSUPPORTED",
            "JSON response type must exactly match a payload with a supported JSON shape",
            "response Found: 200 json self::app::main::Payload", "self");

        const string sqliteManifest = "name = \"wide-integer-sqlite\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\nsqlite_path = \"data/wide.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n";
        const string sqliteRowSource = """
            module app::main;
            struct Parameters { id: i32 }
            struct Row { wide: u64 }
            pub union Reply { Ready }
            fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one("SELECT wide FROM records", self::app::main::Parameters { id: 1 });
                return self::app::main::Reply.Ready;
            }
            route GET "/" { handler: self::app::main::ready; response Ready: 200; }
            """;
        var sqliteFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = sqliteRowSource,
            ["db/schema.sql"] = "CREATE TABLE records (wide INTEGER, narrow INTEGER, id INTEGER);\n"
        };
        var sqliteRowRoot = await harness.WritePackageAsync("wide-integer-sqlite-row-codec", sqliteManifest, sqliteFiles);
        await AssertWideIntegerPackageDiagnosticAsync(harness, "wide-integer-sqlite-row-codec", sqliteRowRoot,
            "src/app/main.hob", sqliteRowSource,
            "E_DB_CODEC_UNSUPPORTED",
            "SQLite row struct 'Row' field 'wide' has unsupported type 'u64'; use i32, bool, Text, or Option of one of those scalar types",
            "wide: u64", "wide");

        const string sqliteParameterSource = """
            module app::main;
            struct Parameters { wide: u64 }
            struct Row { id: i32 }
            pub union Reply { Ready }
            fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one("SELECT id FROM records WHERE wide = $wide", self::app::main::Parameters { wide: 1u64 });
                return self::app::main::Reply.Ready;
            }
            route GET "/" { handler: self::app::main::ready; response Ready: 200; }
            """;
        sqliteFiles["src/app/main.hob"] = sqliteParameterSource;
        var sqliteParameterRoot = await harness.WritePackageAsync(
            "wide-integer-sqlite-parameter-codec", sqliteManifest, sqliteFiles);
        await AssertWideIntegerPackageDiagnosticAsync(harness, "wide-integer-sqlite-parameter-codec", sqliteParameterRoot,
            "src/app/main.hob", sqliteParameterSource,
            "E_DB_CODEC_UNSUPPORTED",
            "SQLite parameter struct 'Parameters' field 'wide' has unsupported type 'u64'; use i32, bool, Text, or Option of one of those scalar types",
            "wide: u64", "wide");
    }

    private static async Task<DiagnosticSnapshot> AssertWideIntegerDiagnosticAsync(
        Harness harness,
        string caseName,
        string source,
        string expectedCode,
        string expectedMessage,
        string marker,
        string rangeToken)
    {
        var check = await harness.InvokeAsync("wide-integer-" + caseName + "-check", "check", source, "--json");
        AssertTrue(check.ExitCode != 0, $"Invalid wide integer source unexpectedly checked for {caseName}. {Describe(check)}");
        AssertEqual(string.Empty, check.StandardError, Describe(check));
        var diagnostic = SelectWideIntegerDiagnostic(
            source, ParseDiagnosticSnapshots(check.StandardOutput), expectedCode, expectedMessage,
            marker, rangeToken, caseName);
        var sourcePath = Path.GetFullPath(harness.LastSourcePath);
        AssertEqual(sourcePath, Path.GetFullPath(diagnostic.File), $"Unexpected diagnostic source for {caseName}.");
        var sourceDirectory = Path.GetDirectoryName(sourcePath)!;
        await AssertWideIntegerBuildRejectedAsync(
            harness, caseName, sourcePath, expectedCode, expectedMessage, sourceDirectory);
        return diagnostic;
    }

    private static async Task<DiagnosticSnapshot> AssertWideIntegerPackageDiagnosticAsync(
        Harness harness,
        string caseName,
        string packageRoot,
        string sourceFile,
        string source,
        string expectedCode,
        string expectedMessage,
        string marker,
        string rangeToken)
    {
        var check = await harness.InvokePackageDirectoryAsync("wide-integer-" + caseName + "-check", packageRoot, "check", "--json");
        AssertTrue(check.ExitCode != 0, $"Invalid wide integer package unexpectedly checked for {caseName}. {Describe(check)}");
        AssertEqual(string.Empty, check.StandardError, Describe(check));
        var diagnostic = SelectWideIntegerDiagnostic(
            source, ParseDiagnosticSnapshots(check.StandardOutput), expectedCode, expectedMessage,
            marker, rangeToken, caseName);
        var expectedPath = Path.GetFullPath(Path.Combine(packageRoot, sourceFile));
        AssertEqual(expectedPath, Path.GetFullPath(diagnostic.File), $"Unexpected diagnostic source for {caseName}.");
        await AssertWideIntegerBuildRejectedAsync(
            harness, caseName, packageRoot, expectedCode, expectedMessage, packageRoot);
        return diagnostic;
    }

    private static DiagnosticSnapshot SelectWideIntegerDiagnostic(
        string source,
        IReadOnlyList<DiagnosticSnapshot> diagnostics,
        string expectedCode,
        string expectedMessage,
        string marker,
        string rangeToken,
        string caseName)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        AssertTrue(markerIndex >= 0, $"Expected diagnostic marker '{marker}' was absent for {caseName}.");
        var tokenOffset = marker.IndexOf(rangeToken, StringComparison.Ordinal);
        AssertTrue(tokenOffset >= 0, $"Expected range token '{rangeToken}' was absent in marker '{marker}'.");
        var start = GetLineAndColumn(source, markerIndex + tokenOffset);
        var end = GetLineAndColumn(source, markerIndex + tokenOffset + rangeToken.Length);
        var matches = diagnostics.Where(diagnostic => diagnostic.Code == expectedCode
            && diagnostic.Message == expectedMessage
            && diagnostic.StartLine == start.Line
            && diagnostic.StartColumn == start.Column
            && diagnostic.EndLine == end.Line
            && diagnostic.EndColumn == end.Column).ToArray();
        AssertEqual(1, matches.Length,
            $"Expected one exact {expectedCode} diagnostic for {caseName}; actual: {string.Join(" | ", diagnostics.Select(item => $"{item.Code}: {item.Message} at {item.StartLine}:{item.StartColumn}-{item.EndLine}:{item.EndColumn}"))}");
        return matches[0];
    }

    private static async Task AssertWideIntegerBuildRejectedAsync(
        Harness harness,
        string caseName,
        string target,
        string expectedCode,
        string expectedMessage,
        string artifactRoot)
    {
        var result = Directory.Exists(target)
            ? await harness.InvokePackageDirectoryAsync("wide-integer-" + caseName + "-build", target, "build")
            : await harness.InvokeFileAsync("wide-integer-" + caseName + "-build", target, "build");
        AssertTrue(result.ExitCode != 0, $"Invalid wide integer source unexpectedly built for {caseName}. {Describe(result)}");
        var output = result.StandardOutput + result.StandardError;
        AssertTrue(output.Contains(expectedCode, StringComparison.Ordinal),
            $"Rejected wide integer build must report {expectedCode} for {caseName}. {Describe(result)}");
        AssertTrue(output.Contains(expectedMessage, StringComparison.Ordinal),
            $"Rejected wide integer build must preserve its diagnostic message for {caseName}. {Describe(result)}");
        AssertTrue(!output.Contains("Built executable: ", StringComparison.Ordinal)
            && !output.Contains("Built library: ", StringComparison.Ordinal)
            && !output.Contains("Built native executable: ", StringComparison.Ordinal),
            $"Rejected wide integer build must not announce an artifact for {caseName}. {Describe(result)}");
        AssertNoCompilerArtifacts(artifactRoot);
    }

    private static async Task TestWideIntegerMetadataAndReceipt(Harness harness)
    {
        const string manifest = "name = \"wide-integer-metadata\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n";
        static string Source(int privateValue) => $$"""
            module numeric;
            pub struct Bounds { signed: i64, narrow: u32, wide: u64 }
            pub newtype WideKey = u64;
            pub union Number { Signed(i64), Narrow(u32), Wide(u64) }
            pub trait NumericWitness {
                fn accepts(value: Self, signed: i64, narrow: u32, wide: u64) -> bool effects {};
            }
            pub fn keep_i64(value: i64) -> i64 effects {} { return value; }
            pub fn keep_u32(value: u32) -> u32 effects {} { return value; }
            pub fn keep_u64(value: u64) -> u64 effects {} { return value; }
            fn accepts_i64(value: i64, signed: i64, narrow: u32, wide: u64) -> bool effects {} { return value == signed; }
            fn accepts_u32(value: u32, signed: i64, narrow: u32, wide: u64) -> bool effects {} { return value == narrow; }
            fn accepts_u64(value: u64, signed: i64, narrow: u32, wide: u64) -> bool effects {} { return value == wide; }
            pub impl self::numeric::NumericWitness for i64 { accepts = self::numeric::accepts_i64; }
            pub impl self::numeric::NumericWitness for u32 { accepts = self::numeric::accepts_u32; }
            pub impl self::numeric::NumericWitness for u64 { accepts = self::numeric::accepts_u64; }
            fn private_fact() -> i32 effects {} { return {{privateValue}}; }
            """;

        async Task<string> CreatePackage(string caseName, string source, string? lineEnding = null)
        {
            var packageRoot = await harness.WritePackageAsync(
                caseName, manifest,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["src/numeric.hob"] = source });
            if (lineEnding is not null)
            {
                var sourcePath = Path.Combine(packageRoot, "src", "numeric.hob");
                var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                await File.WriteAllTextAsync(sourcePath,
                    normalized.Replace("\n", lineEnding, StringComparison.Ordinal), new UTF8Encoding(false));
            }
            var lockResult = await harness.InvokePackageDirectoryAsync(caseName + "-lock", packageRoot, "lock");
            AssertEqual(0, lockResult.ExitCode, Describe(lockResult));
            AssertEqual(string.Empty, lockResult.StandardError, Describe(lockResult));
            AssertEqual("No dependencies to lock for package 'wide-integer-metadata'." + Environment.NewLine,
                lockResult.StandardOutput, Describe(lockResult));
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
                    "Wide integer type facts must retain inspect API schema version 13.");
                foreach (var (name, typeName) in new[] { ("keep_i64", "i64"), ("keep_u32", "u32"), ("keep_u64", "u64") })
                {
                    var function = api.GetProperty("functions").EnumerateArray()
                        .Single(item => item.GetProperty("id").GetString() == "self::numeric::" + name);
                    foreach (var type in new[]
                             {
                                 function.GetProperty("parameters")[0].GetProperty("type"),
                                 function.GetProperty("return_type")
                             })
                    {
                        AssertJsonPropertyOrder(type, "kind,name");
                        AssertEqual("primitive", type.GetProperty("kind").GetString(),
                            $"{typeName} API types should use the existing primitive shape.");
                        AssertEqual(typeName, type.GetProperty("name").GetString(),
                            $"The API should retain the {typeName} name.");
                    }
                }
                var bounds = api.GetProperty("structs").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::numeric::Bounds");
                var fieldNames = new[] { "i64", "u32", "u64" };
                for (var index = 0; index < fieldNames.Length; index++)
                {
                    var fieldType = bounds.GetProperty("fields")[index].GetProperty("type");
                    AssertJsonPropertyOrder(fieldType, "kind,name");
                    AssertEqual("primitive", fieldType.GetProperty("kind").GetString(),
                        "Stored wide integer fields should retain the primitive type shape.");
                    AssertEqual(fieldNames[index], fieldType.GetProperty("name").GetString(),
                        "Stored wide integer fields should retain declaration order and name.");
                }
            }
            var repeatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
            AssertEqual(apiRun.StandardOutput, repeatedApi.StandardOutput,
                "Repeated wide integer API reports must be byte-identical.");

            var auditRun = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
            using (var auditDocument = JsonDocument.Parse(auditRun.StandardOutput))
            {
                var audit = auditDocument.RootElement;
                AssertAuditPropertyOrder(audit);
                AssertEqual(11, audit.GetProperty("schema_version").GetInt32(),
                    "Wide integer audit facts must retain schema version 11.");
                var trait = audit.GetProperty("compiler").GetProperty("traits").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "NumericWitness");
                var accepts = trait.GetProperty("methods").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "accepts");
                var parameters = accepts.GetProperty("parameters");
                foreach (var (index, typeName) in new[] { (1, "i64"), (2, "u32"), (3, "u64") })
                {
                    var type = parameters[index].GetProperty("type");
                    AssertAuditTypePropertyOrder(type);
                    AssertEqual("primitive", type.GetProperty("kind").GetString(),
                        "Audit trait parameter facts should use the existing primitive type shape.");
                    AssertEqual(typeName, type.GetProperty("name").GetString(),
                        "Audit trait parameter facts should retain each wide integer type name.");
                }
            }
            var repeatedAudit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertEqual(auditRun.StandardOutput, repeatedAudit.StandardOutput,
                "Repeated wide integer audit reports must be byte-identical.");
            return (apiRun.StandardOutput, auditRun.StandardOutput);
        }

        var originalRoot = await CreatePackage("wide-integer-metadata-lf", Source(1), "\n");
        var original = await Reports("wide-integer-metadata-original", originalRoot);
        var relocatedRoot = await CreatePackage("wide-integer-metadata-relocated", Source(1));
        var relocated = await Reports("wide-integer-metadata-relocated", relocatedRoot);
        AssertEqual(original.Api, relocated.Api, "Wide integer API facts must survive package relocation.");
        AssertEqual(original.Audit, relocated.Audit, "Wide integer audit facts must survive package relocation.");
        var crlfRoot = await CreatePackage("wide-integer-metadata-crlf", Source(1), "\r\n");
        var crlf = await Reports("wide-integer-metadata-crlf", crlfRoot);
        AssertEqual(original.Api, crlf.Api, "Wide integer API facts must normalize LF and CRLF source.");
        AssertEqual(original.Audit, crlf.Audit, "Wide integer audit facts must normalize LF and CRLF source.");
        var editedRoot = await CreatePackage("wide-integer-metadata-body-edit", Source(2));
        var edited = await Reports("wide-integer-metadata-body-edit", editedRoot);
        AssertEqual(original.Api, edited.Api, "A private body edit must not change public wide integer API facts.");

        var build = await harness.InvokePackageDirectoryAsync("wide-integer-metadata-build", originalRoot, "build");
        var library = ParseBuiltArtifact(build, "Built library: ");
        using var receipt = await AssertBuildReceiptAsync(
            Path.GetDirectoryName(library)!, "managed", null,
            [Path.GetRelativePath(Path.GetDirectoryName(library)!, library).Replace(Path.DirectorySeparatorChar, '/')],
            originalRoot);
        AssertEqual(3, receipt.RootElement.GetProperty("schema_version").GetInt32(),
            "Wide integer builds must retain receipt schema version 3.");
    }
}
