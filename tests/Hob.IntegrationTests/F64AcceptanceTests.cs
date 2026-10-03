using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestF64Acceptance(Harness harness)
    {
        var f64ProbeLoadContext = await TestF64LiteralBitPatterns(harness);
        for (var attempt = 0; attempt < 10 && f64ProbeLoadContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        AssertTrue(!f64ProbeLoadContext.IsAlive,
            "The f64 bit-probe assembly should unload before the harness removes its temporary build files.");
        await TestF64LiteralAndTypeDiagnostics(harness);
        await TestF64CodecBoundaries(harness);
        await TestIntegerDivisionStaysUnsupported(harness);
        await TestF64MetadataAndReceipt(harness);

        var packageRoot = await CreateF64RuntimePackage(harness);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "f64-runtime-lock", packageRoot, "lock"));

        var expectedModes = new[] { "finite-ok", "ieee-ok", "signed-zero-ok", "recursive-ok" };
        for (var mode = 0; mode < expectedModes.Length; mode++)
        {
            var managed = await harness.InvokePackageDirectoryAsync(
                $"f64-managed-{mode}", packageRoot, "run", "--", "probe", mode.ToString(CultureInfo.InvariantCulture));
            AssertRunOutput(expectedModes[mode] + Environment.NewLine, managed);
        }

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64
            || (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
        {
            throw new IntegrationTestSkippedException("F64 NativeAOT tests require Windows x64 or Linux x64.");
        }

        var aot = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "f64-runtime-aot", packageRoot, "build", AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        AssertEqual(0, aot.ExitCode, Describe(aot));
        const string executablePrefix = "Built native executable: ";
        AssertTrue(aot.StandardOutput.StartsWith(executablePrefix, StringComparison.Ordinal), Describe(aot));
        var executable = aot.StandardOutput[executablePrefix.Length..].TrimEnd('\r', '\n');
        AssertTrue(Path.IsPathFullyQualified(executable) && File.Exists(executable),
            $"Expected a NativeAOT f64 executable at {executable}.");
        for (var mode = 0; mode < expectedModes.Length; mode++)
        {
            var native = await ExecuteNativeAsync(
                executable, TimeSpan.FromSeconds(30), "probe", mode.ToString(CultureInfo.InvariantCulture));
            AssertRunOutput(expectedModes[mode] + Environment.NewLine, native);
        }
    }

    private static async Task<WeakReference> TestF64LiteralBitPatterns(Harness harness)
    {
        var accepted128 = "0." + new string('0', 121) + "e0f64";
        AssertEqual(128, accepted128.Length, "The accepted boundary f64 token must be exactly 128 ASCII characters.");

        var source = $$"""
            module bits;
            pub fn probe(index: i32) -> f64 effects {} {
                if index == 0 { return 12f64; }
                if index == 1 { return 1.25f64; }
                if index == 2 { return 1e3f64; }
                if index == 3 { return 1E+3f64; }
                if index == 4 { return 1.25E-1f64; }
                if index == 5 { return 1.00000000000000011102230246251565404236316680908203125f64; }
                if index == 6 { return 1.00000000000000033306690738754696212708950042724609375f64; }
                if index == 7 { return 5e-324f64; }
                if index == 8 { return 1e-324f64; }
                if index == 9 { return -1e-324f64; }
                if index == 10 { return -0f64; }
                if index == 11 { return 1.7976931348623157e308f64; }
                if index == 12 { return 5.25f64 - 2f64; }
                if index == 13 { return 1.5f64 * 2f64; }
                if index == 14 { return 7.5f64 / 2.5f64; }
                if index == 15 { return 1f64 / 0f64; }
                if index == 16 { return -1f64 / 0f64; }
                if index == 17 { return 0f64 / 0f64; }
                if index == 18 { return {{accepted128}}; }
                return 0f64;
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "f64-literal-bit-probe",
            LibraryPackageManifest("f64-literal-bit-probe"),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/bits.hob"] = source });
        var lockResult = await harness.InvokePackageDirectoryAsync("f64-literal-bit-probe-lock", packageRoot, "lock");
        AssertEqual(0, lockResult.ExitCode, Describe(lockResult));

        var build = await harness.InvokePackageDirectoryAsync("f64-literal-bit-probe-build", packageRoot, "build");
        var libraryPath = ParseBuiltArtifact(build, "Built library: ");
        AssertTrue(Path.IsPathFullyQualified(libraryPath) && File.Exists(libraryPath),
            $"Expected the f64 bit-probe library at {libraryPath}.");

        return ProbeF64LiteralBitPatterns(libraryPath);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ProbeF64LiteralBitPatterns(string libraryPath)
    {
        var loadContext = new AssemblyLoadContext($"f64-bit-probe-{Guid.NewGuid():N}", isCollectible: true);
        var weakReference = new WeakReference(loadContext);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(libraryPath));
            var moduleType = assembly.GetType("HobModule", throwOnError: true)!;
            var probe = moduleType.GetMethod("Function_0", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("The f64 bit-probe library does not expose Function_0.");
            var parameters = probe.GetParameters();
            AssertEqual(1, parameters.Length, "The f64 bit probe should expose one selector argument.");
            AssertEqual(typeof(int), parameters[0].ParameterType, "The f64 probe selector should lower from i32 to int.");
            AssertEqual(typeof(double), probe.ReturnType, "The f64 probe result should lower to double.");

            double Read(int index) => probe.Invoke(null, [index]) is double result
                ? result
                : throw new InvalidOperationException($"The f64 bit probe returned a non-double value for index {index}.");

            var expectedBits = new (int Index, ulong Bits, string Label)[]
            {
                (0, 0x4028000000000000UL, "integer-looking decimal"),
                (1, 0x3ff4000000000000UL, "fractional decimal"),
                (2, 0x408f400000000000UL, "lowercase exponent"),
                (3, 0x408f400000000000UL, "uppercase signed exponent"),
                (4, 0x3fc0000000000000UL, "negative exponent"),
                (5, 0x3ff0000000000000UL, "first ties-to-even halfway value"),
                (6, 0x3ff0000000000002UL, "second ties-to-even halfway value"),
                (7, 0x0000000000000001UL, "minimum positive subnormal"),
                (8, 0x0000000000000000UL, "positive underflow to zero"),
                (9, 0x8000000000000000UL, "negative underflow to zero"),
                (10, 0x8000000000000000UL, "negative zero literal"),
                (11, 0x7fefffffffffffffUL, "largest finite value"),
                (12, 0x400a000000000000UL, "subtraction result"),
                (13, 0x4008000000000000UL, "multiplication result"),
                (14, 0x4008000000000000UL, "division result"),
                (15, 0x7ff0000000000000UL, "positive infinity from division by zero"),
                (16, 0xfff0000000000000UL, "negative infinity from division by zero"),
                (18, 0x0000000000000000UL, "128-character finite token")
            };
            foreach (var (index, bits, label) in expectedBits)
                AssertF64Bits(bits, Read(index), label);
            AssertTrue(double.IsNaN(Read(17)), "Zero divided by zero should produce a runtime NaN.");
        }
        finally
        {
            loadContext.Unload();
        }

        return weakReference;
    }

    private static void AssertF64Bits(ulong expected, double actual, string label)
    {
        var actualBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(actual));
        AssertEqual(expected, actualBits, $"Unexpected binary64 bits for {label}.");
    }

    private static async Task<string> CreateF64RuntimePackage(Harness harness)
    {
        const string commandSource = """
            module app::main;
            command probe {
                help "Run f64 value probes.";
                argument mode: i32 help "Probe mode.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string handlerSource = """
            module handlers;
            pub union ProbeError { Failed }

            pub fn run(args: self::app::main::ProbeArgs) -> Result<Text, self::handlers::ProbeError> effects {} {
                if args.mode == 0 {
                    let sum: f64 = 1.25f64 + 2.75f64;
                    let difference: f64 = 5.25f64 - 2f64;
                    let product: f64 = 1.5f64 * 2f64;
                    let quotient: f64 = 7.5f64 / 2.5f64; // Keep the line comment adjacent to the f64 slash case.
                    if sum != 4f64 { return Err(self::handlers::ProbeError.Failed); }
                    if difference != 3.25f64 { return Err(self::handlers::ProbeError.Failed); }
                    if product != 3f64 { return Err(self::handlers::ProbeError.Failed); }
                    if quotient != 3f64 { return Err(self::handlers::ProbeError.Failed); }
                    if core::values::echo(sum) != sum { return Err(self::handlers::ProbeError.Failed); }
                    let generic_roundtrip: f64 = core::values::identity(sum);
                    if generic_roundtrip != sum { return Err(self::handlers::ProbeError.Failed); }
                    return Ok("finite-ok");
                }
                if args.mode == 1 {
                    let nan: f64 = 0f64 / 0f64;
                    let generic_nan: f64 = core::values::identity(nan);
                    if generic_nan == nan { return Err(self::handlers::ProbeError.Failed); }
                    let positive_infinity: f64 = 1f64 / 0f64;
                    let negative_infinity: f64 = -1f64 / 0f64;
                    if nan == nan { return Err(self::handlers::ProbeError.Failed); }
                    if nan != nan { } else { return Err(self::handlers::ProbeError.Failed); }
                    if nan < nan { return Err(self::handlers::ProbeError.Failed); }
                    if nan <= nan { return Err(self::handlers::ProbeError.Failed); }
                    if nan > nan { return Err(self::handlers::ProbeError.Failed); }
                    if nan >= nan { return Err(self::handlers::ProbeError.Failed); }
                    if positive_infinity > 0f64 { } else { return Err(self::handlers::ProbeError.Failed); }
                    if negative_infinity < 0f64 { } else { return Err(self::handlers::ProbeError.Failed); }
                    let opposite_sum: f64 = positive_infinity + negative_infinity;
                    let same_difference: f64 = positive_infinity - positive_infinity;
                    let zero_product: f64 = 0f64 * positive_infinity;
                    let same_quotient: f64 = positive_infinity / positive_infinity;
                    if opposite_sum == opposite_sum { return Err(self::handlers::ProbeError.Failed); }
                    if same_difference == same_difference { return Err(self::handlers::ProbeError.Failed); }
                    if zero_product == zero_product { return Err(self::handlers::ProbeError.Failed); }
                    if same_quotient == same_quotient { return Err(self::handlers::ProbeError.Failed); }
                    let overflow_product: f64 = 1e308f64 * 2f64;
                    if overflow_product > 0f64 { } else { return Err(self::handlers::ProbeError.Failed); }
                    return Ok("ieee-ok");
                }
                if args.mode == 2 {
                    let positive_zero: f64 = 0f64;
                    let negative_zero: f64 = -0f64;
                    if positive_zero != negative_zero { return Err(self::handlers::ProbeError.Failed); }
                    let positive_reciprocal: f64 = 1f64 / positive_zero;
                    let negative_reciprocal: f64 = 1f64 / negative_zero;
                    if positive_reciprocal > 0f64 { } else { return Err(self::handlers::ProbeError.Failed); }
                    if negative_reciprocal < 0f64 { } else { return Err(self::handlers::ProbeError.Failed); }
                    let multiplied_negative_zero: f64 = negative_zero * 1f64;
                    let divided_negative_zero: f64 = negative_zero / 1f64;
                    if 1f64 / multiplied_negative_zero < 0f64 { } else { return Err(self::handlers::ProbeError.Failed); }
                    if 1f64 / divided_negative_zero < 0f64 { } else { return Err(self::handlers::ProbeError.Failed); }
                    return Ok("signed-zero-ok");
                }
                if args.mode == 3 {
                    let nan: f64 = 0f64 / 0f64;
                    let list_left: List<f64> = [nan];
                    let list_right: List<f64> = [nan];
                    if list_left == list_right { return Err(self::handlers::ProbeError.Failed); }
                    if list_left == list_left { return Err(self::handlers::ProbeError.Failed); }
                    let positive_list: List<f64> = [0f64];
                    let negative_list: List<f64> = [-0f64];
                    if positive_list != negative_list { return Err(self::handlers::ProbeError.Failed); }

                    let sample_left: core::values::Sample = core::values::Sample { reading: nan };
                    let sample_right: core::values::Sample = core::values::Sample { reading: nan };
                    if sample_left == sample_right { return Err(self::handlers::ProbeError.Failed); }
                    if sample_left == sample_left { return Err(self::handlers::ProbeError.Failed); }
                    let positive_sample: core::values::Sample = core::values::Sample { reading: 0f64 };
                    let negative_sample: core::values::Sample = core::values::Sample { reading: -0f64 };
                    if positive_sample != negative_sample { return Err(self::handlers::ProbeError.Failed); }
                    let adjacent: f64 = sample_left.reading + 1.25f64;
                    if adjacent == adjacent { return Err(self::handlers::ProbeError.Failed); }

                    let wrapped_left: core::values::Reading = core::values::Reading.wrap(nan);
                    let wrapped_right: core::values::Reading = core::values::Reading.wrap(nan);
                    if wrapped_left == wrapped_right { return Err(self::handlers::ProbeError.Failed); }
                    if wrapped_left == wrapped_left { return Err(self::handlers::ProbeError.Failed); }
                    let positive_wrapped: core::values::Reading = core::values::Reading.wrap(0f64);
                    let negative_wrapped: core::values::Reading = core::values::Reading.wrap(-0f64);
                    if positive_wrapped != negative_wrapped { return Err(self::handlers::ProbeError.Failed); }

                    let union_left: core::values::ScalarValue = core::values::ScalarValue.Number(nan);
                    let union_right: core::values::ScalarValue = core::values::ScalarValue.Number(nan);
                    if union_left == union_right { return Err(self::handlers::ProbeError.Failed); }
                    if union_left == union_left { return Err(self::handlers::ProbeError.Failed); }
                    let positive_union: core::values::ScalarValue = core::values::ScalarValue.Number(0f64);
                    let negative_union: core::values::ScalarValue = core::values::ScalarValue.Number(-0f64);
                    if positive_union != negative_union { return Err(self::handlers::ProbeError.Failed); }
                    let generic_union_left: core::values::GenericValue<f64> = core::values::GenericValue<f64>.Number(nan);
                    let generic_union_right: core::values::GenericValue<f64> = core::values::GenericValue<f64>.Number(nan);
                    if generic_union_left == generic_union_right {
                        return Err(self::handlers::ProbeError.Failed);
                    }
                    if generic_union_left == generic_union_left {
                        return Err(self::handlers::ProbeError.Failed);
                    }
                    let positive_generic_union: core::values::GenericValue<f64> = core::values::GenericValue<f64>.Number(0f64);
                    let negative_generic_union: core::values::GenericValue<f64> = core::values::GenericValue<f64>.Number(-0f64);
                    if positive_generic_union == negative_generic_union { } else {
                        return Err(self::handlers::ProbeError.Failed);
                    }

                    let holder_left: core::values::Holder<f64> = core::values::Holder<f64> { value: nan };
                    let holder_right: core::values::Holder<f64> = core::values::Holder<f64> { value: nan };
                    if holder_left == holder_right { return Err(self::handlers::ProbeError.Failed); }
                    if holder_left == holder_left { return Err(self::handlers::ProbeError.Failed); }
                    let expected_nan: Option<f64> = Some(nan);
                    let another_nan: Option<f64> = Some(nan);
                    if expected_nan == another_nan { return Err(self::handlers::ProbeError.Failed); }
                    if expected_nan == expected_nan { return Err(self::handlers::ProbeError.Failed); }
                    let positive_option: Option<f64> = Some(0f64);
                    let negative_option: Option<f64> = Some(-0f64);
                    if positive_option != negative_option { return Err(self::handlers::ProbeError.Failed); }

                    let empty: Map<Text, f64> = Map.empty();
                    let nan_map_left: Map<Text, f64> = empty.set("value", nan);
                    let nan_map_right: Map<Text, f64> = empty.set("value", nan);
                    if nan_map_left == nan_map_right { return Err(self::handlers::ProbeError.Failed); }
                    if nan_map_left == nan_map_left { return Err(self::handlers::ProbeError.Failed); }

                    let positive_map: Map<Text, f64> = empty.set("zero", 0f64);
                    let negative_map: Map<Text, f64> = empty.set("zero", -0f64);
                    if positive_map != negative_map { return Err(self::handlers::ProbeError.Failed); }

                    let finite_holder_left: core::values::Holder<f64> = core::values::Holder<f64> { value: 0f64 };
                    let finite_holder_right: core::values::Holder<f64> = core::values::Holder<f64> { value: -0f64 };
                    if finite_holder_left == finite_holder_right { } else {
                        return Err(self::handlers::ProbeError.Failed);
                    }
                    return Ok("recursive-ok");
                }
                return Err(self::handlers::ProbeError.Failed);
            }

            pub fn describe(error: self::handlers::ProbeError) -> Text effects {} {
                return match error { self::handlers::ProbeError.Failed => "f64 probe failed" };
            }
            """;
        const string modelSource = """
            module values;
            pub struct Holder<T> { value: T }
            pub struct Sample { reading: f64 }
            pub newtype Reading = f64;
            pub union ScalarValue { Number(f64), Empty }
            pub union GenericValue<T> { Number(T), Empty }

            pub fn echo(value: f64) -> f64 effects {} { return value; }
            pub fn identity<T>(value: T) -> T effects {} { return value; }

            pub trait F64Witness {
                fn accepts(value: Self, expected: f64) -> bool effects {};
            }
            fn accepts_i32(value: i32, expected: f64) -> bool effects {} {
                if expected == 0f64 { return value == 0; }
                return false;
            }
            pub impl self::values::F64Witness for i32 { accepts = self::values::accepts_i32; }
            """;

        return await harness.WritePackageGraphAsync(
            "f64-runtime",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    CliPackageManifest() + "\n[dependencies]\ncore = \"../core\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = commandSource,
                        ["src/handlers.hob"] = handlerSource
                    }),
                ["core"] = new PackageFixture(
                    LibraryPackageManifest("f64-values"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/values.hob"] = modelSource
                    })
            });
    }

    private static string F64InvalidLiteralSource(string caseName, string literal) =>
        $"module harness::{caseName.Replace('-', '_')};\n"
        + $"fn bad() -> f64 effects {{}} {{ return {literal}; }}\n";

    private static async Task TestF64LiteralAndTypeDiagnostics(Harness harness)
    {
        var malformed = new (string Name, string Literal, string Code, string Message)[]
        {
            ("leading-point", ".5f64", "E_SYNTAX", "Malformed f64 literal"),
            ("trailing-point", "1.", "E_SYNTAX", "Malformed f64 literal"),
            ("incomplete-exponent", "1e+f64", "E_SYNTAX", "Malformed f64 literal"),
            ("wrong-float-suffix", "1.0f32", "E_SYNTAX", "Malformed f64 literal"),
            ("missing-decimal-suffix", "1.25", "E_SYNTAX", "Floating-point literals require the f64 suffix"),
            ("missing-exponent-suffix", "1e3", "E_SYNTAX", "Floating-point literals require the f64 suffix")
        };
        foreach (var (name, literal, code, message) in malformed)
        {
            await AssertBytesStandaloneDiagnosticAsync(
                harness, "f64-literal-" + name, F64InvalidLiteralSource("f64_" + name, literal),
                code, message, literal, literal);
        }

        foreach (var literal in new[] { "1.7976931348623159e308f64", "1e309f64" })
        {
            await AssertBytesStandaloneDiagnosticAsync(
                harness, "f64-range-" + literal.Replace('.', '_').Replace('+', '_'),
                F64InvalidLiteralSource("f64_range", literal),
                "E_NUMERIC_LITERAL_RANGE", "f64 literal is outside the finite range", literal, literal);
        }

        var tooLong = "1e" + new string('9', 124) + "f64";
        AssertEqual(129, tooLong.Length, "The rejected f64 boundary token must be exactly 129 ASCII characters.");
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "f64-literal-over-128", F64InvalidLiteralSource("f64_too_long", tooLong),
            "E_NUMERIC_LITERAL_TOO_LONG", "f64 literal exceeds the 128-character limit", tooLong, tooLong);

        const string arithmetic = """
            module harness::f64_mixed_arithmetic;
            fn bad(left: f64, right: i64) -> f64 effects {} { return left + right; }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "f64-mixed-arithmetic", arithmetic,
            "E_TYPE_MISMATCH",
            "Arithmetic '+' requires operands with the same numeric type; found 'f64' and 'i64'",
            "left + right", "+");

        const string comparison = """
            module harness::f64_mixed_comparison;
            fn bad(left: f64, right: u64) -> bool effects {} { return left < right; }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "f64-mixed-comparison", comparison,
            "E_TYPE_MISMATCH",
            "Comparison '<' requires operands with the same numeric type; found 'f64' and 'u64'",
            "left < right", "<");

        const string wideInteger = """
            module harness::f64_mixed_u64;
            fn bad() -> f64 effects {} { return 1u64; }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness, "f64-mixed-u64", wideInteger,
            "E_TYPE_MISMATCH", "Expected 'f64', found 'u64'", "return 1u64", "1u64");
    }

    private static async Task TestIntegerDivisionStaysUnsupported(Harness harness)
    {
        var widths = new[] { "i32", "i64", "u32", "u64" };
        foreach (var width in widths)
        {
            var source = $"module harness::integer_division_{width};\n"
                + $"fn bad(left: {width}, right: {width}) -> {width} effects {{}} {{ return left / right; }}\n";
            await AssertBytesStandaloneDiagnosticAsync(
                harness, "f64-keeps-integer-division-" + width, source,
                "E_UNSUPPORTED", "Division is only supported for f64 operands", "left / right", "/");
        }
    }

    private static async Task TestF64CodecBoundaries(Harness harness)
    {
        const string cliMain = """
            module app::main;
            command inspect {
                help "Inspect a floating value.";
                argument value: f64 help "Floating value.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string cliHandlers = """
            module handlers;
            pub union InspectError { Failed }
            pub fn run(args: self::app::main::InspectArgs) -> Result<Text, self::handlers::InspectError> effects {} {
                return Ok("ok");
            }
            pub fn describe(error: self::handlers::InspectError) -> Text effects {} {
                return match error { self::handlers::InspectError.Failed => "inspect failed" };
            }
            """;
        var cliFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = cliMain,
            ["src/handlers.hob"] = cliHandlers
        };
        await AssertBytesPackageDiagnosticAsync(
            harness, "f64-cli-codec", CliPackageManifest(), cliFiles, "src/app/main.hob",
            "E_COMMAND_DECL", "Command input type 'f64' is not supported; use FilePath, Text, or i32",
            "argument value: f64", "argument");

        const string webManifest = "name = \"f64-web\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n\n[capabilities]\nnet.listen = \"allow\"\n";
        const string routePrefix = """
            module app::main;
            pub union Reply { Found(i32), Empty }
            """;
        var pathSource = routePrefix + """
            fn get(value: f64) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Found(0);
            }
            route GET "/items/{value}" {
                path value: f64;
                handler: self::app::main::get;
                response Found: 200 json i32;
                response Empty: 204;
            }
            """;
        var pathFiles = new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = pathSource };
        await AssertBytesPackageDiagnosticAsync(
            harness, "f64-route-path-codec", webManifest, pathFiles, "src/app/main.hob",
            "E_ROUTE_BINDING", "Path bindings support only Text and i32", "path value: f64", "f64");

        var querySource = routePrefix + """
            fn get(value: f64) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Found(0);
            }
            route GET "/items" {
                query value: f64;
                handler: self::app::main::get;
                response Found: 200 json i32;
                response Empty: 204;
            }
            """;
        var queryFiles = new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = querySource };
        await AssertBytesPackageDiagnosticAsync(
            harness, "f64-route-query-codec", webManifest, queryFiles, "src/app/main.hob",
            "E_ROUTE_BINDING", "Query bindings support only Text, i32, Option<Text>, and Option<i32>",
            "query value: f64", "f64");

        const string bodySource = """
            module app::main;
            pub struct Payload { value: f64 }
            pub union Reply { Accepted, Empty }
            fn post(payload: self::app::main::Payload) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Accepted;
            }
            route POST "/items" {
                body: self::app::main::Payload;
                handler: self::app::main::post;
                response Accepted: 201;
                response Empty: 204;
            }
            """;
        var bodyFiles = new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = bodySource };
        await AssertBytesPackageDiagnosticAsync(
            harness, "f64-route-body-codec", webManifest, bodyFiles, "src/app/main.hob",
            "E_ROUTE_CODEC_UNSUPPORTED", "POST route body structs must contain only acyclic i32, bool, Text, and supported struct fields",
            "body: self::app::main::Payload;", "self");

        const string responseSource = """
            module app::main;
            pub struct Payload { value: f64 }
            pub union Reply { Found(self::app::main::Payload), Empty }
            fn get() -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Found(self::app::main::Payload { value: 1f64 });
            }
            route GET "/items" {
                handler: self::app::main::get;
                response Found: 200 json self::app::main::Payload;
                response Empty: 204;
            }
            """;
        var responseFiles = new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = responseSource };
        await AssertBytesPackageDiagnosticAsync(
            harness, "f64-route-response-codec", webManifest, responseFiles, "src/app/main.hob",
            "E_ROUTE_CODEC_UNSUPPORTED", "JSON response type must exactly match a payload with a supported JSON shape",
            "response Found: 200 json self::app::main::Payload", "self");

        const string sqliteManifest = "name = \"f64-sqlite\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\nsqlite_path = \"data/f64.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n";
        const string sqliteRowSource = """
            module app::main;
            struct Parameters { id: i32 }
            struct Row { value: f64 }
            pub union Reply { Ready }
            fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one("SELECT value FROM records", self::app::main::Parameters { id: 1 });
                return self::app::main::Reply.Ready;
            }
            route GET "/" { handler: self::app::main::ready; response Ready: 200; }
            """;
        var sqliteFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = sqliteRowSource,
            ["db/schema.sql"] = "CREATE TABLE records (value REAL, id INTEGER);\n"
        };
        await AssertBytesPackageDiagnosticAsync(
            harness, "f64-sqlite-row-codec", sqliteManifest, sqliteFiles, "src/app/main.hob",
            "E_DB_CODEC_UNSUPPORTED",
            "SQLite row struct 'Row' field 'value' has unsupported type 'f64'; use i32, bool, Text, or Option of one of those scalar types",
            "value: f64", "value");

        const string sqliteParameterSource = """
            module app::main;
            struct Parameters { value: f64 }
            struct Row { id: i32 }
            pub union Reply { Ready }
            fn ready(db: DbRead) -> self::app::main::Reply effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one("SELECT id FROM records WHERE value = $value", self::app::main::Parameters { value: 1f64 });
                return self::app::main::Reply.Ready;
            }
            route GET "/" { handler: self::app::main::ready; response Ready: 200; }
            """;
        sqliteFiles["src/app/main.hob"] = sqliteParameterSource;
        await AssertBytesPackageDiagnosticAsync(
            harness, "f64-sqlite-parameter-codec", sqliteManifest, sqliteFiles, "src/app/main.hob",
            "E_DB_CODEC_UNSUPPORTED",
            "SQLite parameter struct 'Parameters' field 'value' has unsupported type 'f64'; use i32, bool, Text, or Option of one of those scalar types",
            "value: f64", "value");
    }

    private static string F64MetadataSource(int privateValue) => $$"""
        module numeric;
        pub struct Sample { value: f64 }
        pub newtype Reading = f64;
        pub union NumericValue { Value(f64), Empty }
        pub fn keep(value: f64) -> f64 effects {} { return value; }
        pub trait F64Witness {
            fn accepts(value: Self, expected: f64) -> bool effects {};
        }
        fn accepts_i32(value: i32, expected: f64) -> bool effects {} {
            if expected == 0f64 { return value == 0; }
            return false;
        }
        pub impl self::numeric::F64Witness for i32 { accepts = self::numeric::accepts_i32; }
        fn private_fact() -> i32 effects {} { return {{privateValue}}; }
        """;

    private static async Task TestF64MetadataAndReceipt(Harness harness)
    {
        const string manifest = "name = \"f64-metadata\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n";

        async Task<string> CreatePackage(string caseName, string source, string? lineEnding = null)
        {
            var packageRoot = await harness.WritePackageAsync(
                caseName,
                manifest,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["src/numeric.hob"] = source });
            if (lineEnding is not null)
            {
                var sourcePath = Path.Combine(packageRoot, "src", "numeric.hob");
                var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                await File.WriteAllTextAsync(
                    sourcePath, normalized.Replace("\n", lineEnding, StringComparison.Ordinal), new UTF8Encoding(false));
            }

            var lockResult = await harness.InvokePackageDirectoryAsync(caseName + "-lock", packageRoot, "lock");
            AssertEqual(0, lockResult.ExitCode, Describe(lockResult));
            AssertEqual(string.Empty, lockResult.StandardError, Describe(lockResult));
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
                    "F64 API facts must retain inspect API schema version 13.");

                var keep = api.GetProperty("functions").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::numeric::keep");
                AssertF64PrimitiveType(keep.GetProperty("parameters")[0].GetProperty("type"), "API function parameter");
                AssertF64PrimitiveType(keep.GetProperty("return_type"), "API function return");

                var sample = api.GetProperty("structs").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::numeric::Sample");
                AssertEqual("value", sample.GetProperty("fields")[0].GetProperty("name").GetString(),
                    "The f64 field name/order should remain source-facing.");
                AssertF64PrimitiveType(sample.GetProperty("fields")[0].GetProperty("type"), "API stored field");

                var reading = api.GetProperty("newtypes").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::numeric::Reading");
                AssertJsonPropertyOrder(reading, "id,source_ids,package,representation");
                AssertF64PrimitiveType(reading.GetProperty("representation"), "API newtype representation");

                var numericValue = api.GetProperty("unions").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::numeric::NumericValue");
                AssertJsonPropertyOrder(numericValue, "id,source_ids,package,type_parameters,variants");
                var numberPayload = numericValue.GetProperty("variants").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "Value")
                    .GetProperty("payload")[0].GetProperty("type");
                AssertF64PrimitiveType(numberPayload, "API union payload");
            }

            var repeatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
            AssertEqual(apiRun.StandardOutput, repeatedApi.StandardOutput,
                "Repeated f64 API reports must be byte-identical.");

            var auditRun = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
            using (var auditDocument = JsonDocument.Parse(auditRun.StandardOutput))
            {
                var audit = auditDocument.RootElement;
                AssertAuditPropertyOrder(audit);
                AssertEqual(11, audit.GetProperty("schema_version").GetInt32(),
                    "F64 audit facts must retain audit schema version 11.");
                var trait = audit.GetProperty("compiler").GetProperty("traits").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "F64Witness");
                var accepts = trait.GetProperty("methods").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "accepts");
                var expected = accepts.GetProperty("parameters")[1].GetProperty("type");
                AssertAuditTypePropertyOrder(expected);
                AssertEqual("primitive", expected.GetProperty("kind").GetString(),
                    "Audit trait method arguments should use the existing primitive type shape.");
                AssertEqual("f64", expected.GetProperty("name").GetString(),
                    "Audit trait method arguments should retain the f64 primitive name.");
            }

            var repeatedAudit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertEqual(auditRun.StandardOutput, repeatedAudit.StandardOutput,
                "Repeated f64 audit reports must be byte-identical.");
            return (apiRun.StandardOutput, auditRun.StandardOutput);
        }

        var originalRoot = await CreatePackage("f64-metadata-lf", F64MetadataSource(1), "\n");
        var original = await Reports("f64-metadata-original", originalRoot);
        var relocatedRoot = await CreatePackage("f64-metadata-relocated", F64MetadataSource(1));
        var relocated = await Reports("f64-metadata-relocated", relocatedRoot);
        AssertEqual(original.Api, relocated.Api, "F64 API facts must survive package relocation.");
        AssertEqual(original.Audit, relocated.Audit, "F64 audit facts must survive package relocation.");

        var crlfRoot = await CreatePackage("f64-metadata-crlf", F64MetadataSource(1), "\r\n");
        var crlf = await Reports("f64-metadata-crlf", crlfRoot);
        AssertEqual(original.Api, crlf.Api, "F64 API facts must normalize LF and CRLF source.");
        AssertEqual(original.Audit, crlf.Audit, "F64 audit facts must normalize LF and CRLF source.");

        var editedRoot = await CreatePackage("f64-metadata-private-body-edit", F64MetadataSource(2));
        var edited = await Reports("f64-metadata-private-body-edit", editedRoot);
        AssertEqual(original.Api, edited.Api, "A private body edit must not change public f64 API facts.");

        var build = await harness.InvokePackageDirectoryAsync("f64-metadata-build", originalRoot, "build");
        var library = ParseBuiltArtifact(build, "Built library: ");
        using var receipt = await AssertBuildReceiptAsync(
            Path.GetDirectoryName(library)!, "managed", null,
            [Path.GetRelativePath(Path.GetDirectoryName(library)!, library).Replace(Path.DirectorySeparatorChar, '/')],
            originalRoot);
        AssertEqual(4, receipt.RootElement.GetProperty("schema_version").GetInt32(),
            "F64 managed builds must retain receipt schema version 4.");
    }

    private static void AssertF64PrimitiveType(JsonElement type, string label)
    {
        AssertJsonPropertyOrder(type, "kind,name");
        AssertEqual("primitive", type.GetProperty("kind").GetString(),
            $"The {label} should use the existing primitive type shape.");
        AssertEqual("f64", type.GetProperty("name").GetString(),
            $"The {label} should retain the f64 primitive name.");
    }
}
