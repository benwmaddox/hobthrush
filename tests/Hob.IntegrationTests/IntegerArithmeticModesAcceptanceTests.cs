using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private sealed record ArithmeticWidth(string Name, int Bits, bool IsSigned, BigInteger Minimum, BigInteger Maximum);
    private sealed record ArithmeticPair(BigInteger Left, BigInteger Right);
    private sealed record ArithmeticFaultSelector(string Name, int Selector);
    private sealed record ArithmeticTraceSelector(string Name, int Selector, string[] Events);
    private sealed record ArithmeticDiagnosticExpectation(string Code, string Message, string Marker, string RangeToken);

    private enum ArithmeticOperation
    {
        Add,
        Subtract,
        Multiply
    }

    private static async Task TestIntegerArithmeticModes(Harness harness)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64
            || (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
        {
            throw new IntegrationTestSkippedException(
                "Named integer arithmetic NativeAOT acceptance requires Windows x64 or Linux x64.");
        }

        var widths = new[]
        {
            CreateArithmeticWidth("i32", 32, signed: true),
            CreateArithmeticWidth("i64", 64, signed: true),
            CreateArithmeticWidth("u32", 32, signed: false),
            CreateArithmeticWidth("u64", 64, signed: false)
        };

        await TestIntegerArithmeticDiagnostics(harness, widths);
        await TestArithmeticF64ScannerPreservation(harness);
        await TestIntegerDivisionStaysUnsupported(harness);

        var packageRoot = await harness.WritePackageAsync(
            "integer-arithmetic-modes",
            CliPackageManifest() + "\n[capabilities]\nlog.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = CreateIntegerArithmeticRuntimeSource(widths)
            });
        AssertIntegerArithmeticNoDependencyLock(await harness.InvokePackageDirectoryAsync(
            "integer-arithmetic-modes-lock", packageRoot, "lock"), "harness-package");

        var check = await harness.InvokePackageDirectoryAsync(
            "integer-arithmetic-modes-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        await TestIntegerArithmeticMetadata(harness);

        var managedBuild = await harness.InvokePackageDirectoryAsync(
            "integer-arithmetic-modes-managed-build", packageRoot, "build");
        var managedArtifact = ParseBuiltArtifact(managedBuild, "Built executable: ");
        var managedDirectory = Path.GetDirectoryName(managedArtifact)!;
        using (var receipt = await AssertBuildReceiptAsync(
                   managedDirectory,
                   "managed",
                   expectedRuntimeIdentifier: null,
                   [Path.GetRelativePath(managedDirectory, managedArtifact).Replace(Path.DirectorySeparatorChar, '/'),
                    "command-schema.json"],
                   packageRoot))
        {
            AssertEqual(3, receipt.RootElement.GetProperty("schema_version").GetInt32(),
                "Managed integer arithmetic builds must retain receipt schema 3.");
        }

        var positive = await harness.RunManagedArtifactAsync(
            managedArtifact, packageRoot, "probe", "0");
        AssertRunOutput("integer-arithmetic-ok" + Environment.NewLine, positive);

        var traces = IntegerArithmeticTraceSelectors();
        var managedRuns = new Dictionary<string, ProcessResult>(StringComparer.Ordinal)
        {
            ["positive"] = positive
        };
        foreach (var trace in traces)
        {
            var run = await harness.RunManagedArtifactAsync(
                managedArtifact, packageRoot, "probe", trace.Selector.ToString(CultureInfo.InvariantCulture));
            AssertUnitLogRun(run, 0, "trace-ok", trace.Events);
            managedRuns.Add(trace.Name, run);
        }

        var defaultFaults = IntegerArithmeticDefaultFaultSelectors(widths);
        AssertEqual(12, defaultFaults.Count, "Default overflow acceptance must cover every width and operator family.");
        foreach (var fault in defaultFaults)
        {
            var run = await harness.RunManagedArtifactAsync(
                managedArtifact, packageRoot, "probe", fault.Selector.ToString(CultureInfo.InvariantCulture));
            AssertIntegerArithmeticRuntimeFault(run, "managed " + fault.Name);
            managedRuns.Add(fault.Name, run);
        }

        var operandFaults = IntegerArithmeticOperandFaultSelectors();
        AssertEqual(6, operandFaults.Count, "Operand-boundary acceptance must cover both sides of all three mode families.");
        foreach (var fault in operandFaults)
        {
            var run = await harness.RunManagedArtifactAsync(
                managedArtifact, packageRoot, "probe", fault.Selector.ToString(CultureInfo.InvariantCulture));
            AssertIntegerArithmeticFaultWithEvents(run, fault.Events, "managed " + fault.Name);
            managedRuns.Add(fault.Name, run);
        }

        var aotBuild = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "integer-arithmetic-modes-aot-build", packageRoot, "build", AotPublishTimeout,
            "--aot", "--rid", CurrentHostAotRid());
        var nativeArtifact = ParseBuiltArtifact(aotBuild, "Built native executable: ");
        var nativeDirectory = Path.GetDirectoryName(nativeArtifact)!;
        AssertTrue(Path.IsPathFullyQualified(nativeArtifact) && File.Exists(nativeArtifact),
            $"Expected a NativeAOT integer arithmetic executable at {nativeArtifact}.");
        var managedSchema = await File.ReadAllBytesAsync(Path.Combine(managedDirectory, "command-schema.json"));
        var nativeSchema = await File.ReadAllBytesAsync(Path.Combine(nativeDirectory, "command-schema.json"));
        AssertTrue(managedSchema.SequenceEqual(nativeSchema),
            "Managed and NativeAOT integer arithmetic command schemas must be byte-identical.");
        using (var receipt = await AssertBuildReceiptAsync(
                   nativeDirectory,
                   "native_aot",
                   CurrentHostAotRid(),
                   [Path.GetRelativePath(nativeDirectory, nativeArtifact).Replace(Path.DirectorySeparatorChar, '/'),
                    "command-schema.json"],
                   packageRoot))
        {
            AssertEqual(3, receipt.RootElement.GetProperty("schema_version").GetInt32(),
                "NativeAOT integer arithmetic builds must retain receipt schema 3.");
        }

        var nativePositive = await ExecuteNativeAsync(
            nativeArtifact, TimeSpan.FromSeconds(30), "probe", "0");
        AssertRunOutput("integer-arithmetic-ok" + Environment.NewLine, nativePositive);
        AssertArithmeticBackendParity(managedRuns["positive"], nativePositive, "positive matrix");

        foreach (var trace in traces)
        {
            var native = await ExecuteNativeAsync(
                nativeArtifact, TimeSpan.FromSeconds(30), "probe",
                trace.Selector.ToString(CultureInfo.InvariantCulture));
            AssertUnitLogRun(native, 0, "trace-ok", trace.Events);
            AssertArithmeticBackendParity(managedRuns[trace.Name], native, trace.Name);
        }

        foreach (var fault in defaultFaults)
        {
            var native = await ExecuteNativeAsync(
                nativeArtifact, TimeSpan.FromSeconds(30), "probe",
                fault.Selector.ToString(CultureInfo.InvariantCulture));
            AssertIntegerArithmeticRuntimeFault(native, "NativeAOT " + fault.Name);
            AssertArithmeticBackendParity(managedRuns[fault.Name], native, fault.Name);
        }

        foreach (var fault in operandFaults)
        {
            var native = await ExecuteNativeAsync(
                nativeArtifact, TimeSpan.FromSeconds(30), "probe",
                fault.Selector.ToString(CultureInfo.InvariantCulture));
            AssertIntegerArithmeticFaultWithEvents(native, fault.Events, "NativeAOT " + fault.Name);
            AssertArithmeticBackendParity(managedRuns[fault.Name], native, fault.Name);
        }
    }

    private static ArithmeticWidth CreateArithmeticWidth(string name, int bits, bool signed)
    {
        var modulus = BigInteger.One << bits;
        var minimum = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
        var maximum = signed ? (BigInteger.One << (bits - 1)) - BigInteger.One : modulus - BigInteger.One;
        return new ArithmeticWidth(name, bits, signed, minimum, maximum);
    }

    private static IReadOnlyList<ArithmeticPair> ArithmeticPairs(
        ArithmeticWidth width,
        ArithmeticOperation operation)
    {
        var pairs = new List<ArithmeticPair>();
        void Add(BigInteger left, BigInteger right)
        {
            if (left < width.Minimum || left > width.Maximum || right < width.Minimum || right > width.Maximum)
                throw new InvalidOperationException($"The {width.Name} arithmetic oracle contains an out-of-range input.");
            pairs.Add(new ArithmeticPair(left, right));
        }

        switch (operation)
        {
            case ArithmeticOperation.Add:
                Add(BigInteger.Zero, BigInteger.Zero);
                Add(BigInteger.One, new BigInteger(2));
                Add(width.Maximum - BigInteger.One, BigInteger.One);
                Add(width.Maximum, BigInteger.Zero);
                Add(width.Maximum, BigInteger.One);
                Add(width.Minimum, BigInteger.One);
                if (width.IsSigned)
                {
                    Add(width.Minimum, -BigInteger.One);
                    Add(-BigInteger.One, BigInteger.One);
                }
                else
                {
                    Add(BigInteger.Zero, BigInteger.One);
                }
                break;
            case ArithmeticOperation.Subtract:
                Add(BigInteger.Zero, BigInteger.Zero);
                Add(BigInteger.One, BigInteger.One);
                Add(width.Maximum, BigInteger.One);
                Add(width.Maximum, width.Maximum);
                Add(width.Minimum, BigInteger.Zero);
                Add(width.Minimum + BigInteger.One, BigInteger.One);
                Add(BigInteger.Zero, BigInteger.One);
                if (width.IsSigned)
                {
                    Add(width.Maximum, -BigInteger.One);
                    Add(-BigInteger.One, BigInteger.One);
                }
                else
                {
                    Add(width.Maximum, BigInteger.One);
                }
                break;
            case ArithmeticOperation.Multiply:
                Add(BigInteger.Zero, width.Maximum);
                Add(BigInteger.One, width.Maximum);
                Add(new BigInteger(2), new BigInteger(2));
                Add(width.Maximum, new BigInteger(2));
                Add(width.Maximum, BigInteger.One);
                Add(width.Maximum, BigInteger.Zero);
                if (width.IsSigned)
                {
                    Add(width.Minimum, -BigInteger.One);
                    Add(width.Minimum, BigInteger.One);
                    Add(width.Minimum, new BigInteger(2));
                    Add(width.Maximum, -BigInteger.One);
                    Add(width.Minimum / new BigInteger(2), new BigInteger(2));
                }
                else
                {
                    Add(width.Maximum / new BigInteger(2), new BigInteger(2));
                    Add(new BigInteger(2), BigInteger.One);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        return pairs;
    }

    private static BigInteger ArithmeticResult(BigInteger left, BigInteger right, ArithmeticOperation operation) =>
        operation switch
        {
            ArithmeticOperation.Add => left + right,
            ArithmeticOperation.Subtract => left - right,
            ArithmeticOperation.Multiply => left * right,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static BigInteger SaturateArithmeticResult(BigInteger result, ArithmeticWidth width) =>
        BigInteger.Min(width.Maximum, BigInteger.Max(width.Minimum, result));

    private static BigInteger WrapArithmeticResult(BigInteger result, ArithmeticWidth width)
    {
        var modulus = BigInteger.One << width.Bits;
        var wrapped = (result % modulus + modulus) % modulus;
        if (width.IsSigned && wrapped >= (modulus >> 1))
            wrapped -= modulus;
        return wrapped;
    }

    private static string FormatArithmeticLiteral(BigInteger value, ArithmeticWidth width)
    {
        var literal = value.ToString(CultureInfo.InvariantCulture);
        return width.Name == "i32" ? literal : literal + width.Name;
    }

    private static string ArithmeticModeName(ArithmeticOperation operation, string mode) =>
        mode + "_" + (operation switch
        {
            ArithmeticOperation.Add => "add",
            ArithmeticOperation.Subtract => "sub",
            ArithmeticOperation.Multiply => "mul",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        });

    private static string CreateIntegerArithmeticRuntimeSource(IReadOnlyList<ArithmeticWidth> widths)
    {
        var source = new StringBuilder();
        source.AppendLine("module app::main;");
        source.AppendLine("pub struct ArithmeticRecord { outcome: Result<i32, ArithmeticError> }");
        source.AppendLine("pub union ArithmeticCarrier { Value(Result<i32, ArithmeticError>), Empty }");
        source.AppendLine("pub union ProbeError { Failed }");
        source.AppendLine("command probe {");
        source.AppendLine("    help \"Run named integer arithmetic acceptance probes.\";");
        source.AppendLine("    argument mode: i32 help \"Select an arithmetic probe.\";");
        source.AppendLine("    handler: self::app::main::run;");
        source.AppendLine("    error: self::app::main::describe;");
        source.AppendLine("}");

        foreach (var width in widths)
        {
            source.AppendLine($"fn checked_value_{width.Name}(value: Result<{width.Name}, ArithmeticError>, expected: {width.Name}) -> bool effects {{}} {{");
            source.AppendLine("    return match value {");
            source.AppendLine("        Ok(actual) => actual == expected,");
            source.AppendLine("        Err(error) => match error { ArithmeticError.Overflow => false },");
            source.AppendLine("    };");
            source.AppendLine("}");
            source.AppendLine($"fn checked_overflow_{width.Name}(value: Result<{width.Name}, ArithmeticError>) -> bool effects {{}} {{");
            source.AppendLine("    return match value {");
            source.AppendLine("        Ok(_) => false,");
            source.AppendLine("        Err(error) => match error { ArithmeticError.Overflow => true },");
            source.AppendLine("    };");
            source.AppendLine("}");
            source.AppendLine($"fn pass_{width.Name}(value: {width.Name}) -> {width.Name} effects {{}} {{ return value; }}");
        }

        source.AppendLine("fn preserve_result<T, E>(value: Result<T, E>) -> Result<T, E> effects {} { return value; }");
        source.AppendLine("fn is_twelve(value: i32) -> bool effects {} { return value == 12; }");
        source.AppendLine("fn trace_value(logger: Logger, event: Text, value: i32) -> i32 effects { log.write } {");
        source.AppendLine("    let logged: bool = logger.info(event, \"\");");
        source.AppendLine("    return value;");
        source.AppendLine("}");
        source.AppendLine("fn trace_overflow(logger: Logger, event: Text) -> i32 effects { log.write } {");
        source.AppendLine("    let logged: bool = logger.info(event, \"\");");
        source.AppendLine("    let overflow: i32 = 2147483647 + 1;");
        source.AppendLine("    return overflow;");
        source.AppendLine("}");
        source.AppendLine("pub fn metadata_checked_add(left: i32, right: i32) -> Result<i32, ArithmeticError> effects {} {");
        source.AppendLine("    return left.checked_add(right);");
        source.AppendLine("}");
        source.AppendLine("pub fn metadata_forward<T>(value: Result<T, ArithmeticError>) -> Result<T, ArithmeticError> effects {} {");
        source.AppendLine("    return value;");
        source.AppendLine("}");
        source.AppendLine("pub trait ArithmeticWitness {");
        source.AppendLine("    fn preserve(value: Self, error: Result<i32, ArithmeticError>) -> ArithmeticError effects {};");
        source.AppendLine("}");

        source.AppendLine("pub fn direct_checked_success() -> Result<i32, ArithmeticError> effects {} {");
        source.AppendLine("    let value: i32 = 1.checked_add(2)?;");
        source.AppendLine("    return Ok(value);");
        source.AppendLine("}");
        source.AppendLine("pub fn direct_checked_overflow() -> Result<i32, ArithmeticError> effects {} {");
        source.AppendLine("    let value: i32 = 2147483647.checked_add(1)?;");
        source.AppendLine("    return Ok(value);");
        source.AppendLine("}");
        source.AppendLine("pub fn local_checked_add(left: i32, right: i32) -> Result<i32, ArithmeticError> effects {} {");
        source.AppendLine("    let value: i32 = left.checked_add(right)?;");
        source.AppendLine("    return Ok(value);");
        source.AppendLine("}");

        source.AppendLine("pub fn run(args: self::app::main::ProbeArgs, logger: Logger) -> Result<Text, self::app::main::ProbeError> effects { log.write } {");
        source.AppendLine("    if args.mode == 0 {");
        AppendArithmeticMatrixChecks(source, widths);
        AppendArithmeticDirectLiteralChecks(source);
        source.AppendLine("        let from_direct: Result<i32, ArithmeticError> = self::app::main::direct_checked_success();");
        source.AppendLine("        if (self::app::main::checked_value_i32(from_direct, 3)) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let from_overflow: Result<i32, ArithmeticError> = self::app::main::direct_checked_overflow();");
        source.AppendLine("        if (self::app::main::checked_overflow_i32(from_overflow)) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let from_local: Result<i32, ArithmeticError> = self::app::main::local_checked_add(8, 9);");
        source.AppendLine("        if (self::app::main::checked_value_i32(from_local, 17)) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let raw_error: Result<i32, ArithmeticError> = 2147483647.checked_add(1);");
        source.AppendLine("        let forwarded_error: Result<i32, ArithmeticError> = self::app::main::preserve_result(raw_error);");
        source.AppendLine("        if (self::app::main::checked_overflow_i32(forwarded_error)) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let metadata_error: Result<i32, ArithmeticError> = self::app::main::metadata_forward(forwarded_error);");
        source.AppendLine("        if (self::app::main::checked_overflow_i32(metadata_error)) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let independent_error: Result<i32, ArithmeticError> = (-2147483648).checked_sub(1);");
        source.AppendLine("        if (forwarded_error == independent_error) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let success_three: Result<i32, ArithmeticError> = 1.checked_add(2);");
        source.AppendLine("        let success_four: Result<i32, ArithmeticError> = 1.checked_add(3);");
        source.AppendLine("        if (success_three != success_four) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        if (success_three != forwarded_error) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let error_list: List<Result<i32, ArithmeticError>> = [forwarded_error];");
        source.AppendLine("        let independent_error_list: List<Result<i32, ArithmeticError>> = [independent_error];");
        source.AppendLine("        if (error_list == independent_error_list) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let maybe_error: Option<Result<i32, ArithmeticError>> = error_list.get(0);");
        source.AppendLine("        let list_preserved: bool = match maybe_error {");
        source.AppendLine("            Some(value) => self::app::main::checked_overflow_i32(value),");
        source.AppendLine("            None => false,");
        source.AppendLine("        };");
        source.AppendLine("        if (list_preserved) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let record: self::app::main::ArithmeticRecord = self::app::main::ArithmeticRecord { outcome: forwarded_error };");
        source.AppendLine("        let independent_record: self::app::main::ArithmeticRecord = self::app::main::ArithmeticRecord { outcome: independent_error };");
        source.AppendLine("        if (record == independent_record) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        if (self::app::main::checked_overflow_i32(record.outcome)) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let carrier: self::app::main::ArithmeticCarrier = self::app::main::ArithmeticCarrier.Value(forwarded_error);");
        source.AppendLine("        let independent_carrier: self::app::main::ArithmeticCarrier = self::app::main::ArithmeticCarrier.Value(independent_error);");
        source.AppendLine("        if (carrier == independent_carrier) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let union_preserved: bool = match carrier {");
        source.AppendLine("            self::app::main::ArithmeticCarrier.Value(value) => self::app::main::checked_overflow_i32(value),");
        source.AppendLine("            self::app::main::ArithmeticCarrier.Empty => false,");
        source.AppendLine("        };");
        source.AppendLine("        if (union_preserved) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let finite: f64 = 1.25f64;");
        source.AppendLine("        if (finite == 1.25f64) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        let infinity: f64 = 1f64 / 0f64;");
        source.AppendLine("        if (infinity > 0f64) {} else { return Err(self::app::main::ProbeError.Failed); }");
        source.AppendLine("        return Ok(\"integer-arithmetic-ok\");");
        source.AppendLine("    }");

        AppendArithmeticTraceCases(source);
        AppendArithmeticOperandFaultCases(source);
        AppendArithmeticDefaultFaultCases(source, widths);

        source.AppendLine("    return Err(self::app::main::ProbeError.Failed);");
        source.AppendLine("}");
        source.AppendLine("pub fn describe(error: self::app::main::ProbeError) -> Text effects {} {");
        source.AppendLine("    return match error { self::app::main::ProbeError.Failed => \"integer arithmetic acceptance failed\" };");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendArithmeticMatrixChecks(StringBuilder source, IReadOnlyList<ArithmeticWidth> widths)
    {
        foreach (var width in widths)
        {
            foreach (var operation in Enum.GetValues<ArithmeticOperation>())
            {
                var checkedMethod = ArithmeticModeName(operation, "checked");
                var saturatingMethod = ArithmeticModeName(operation, "saturating");
                var wrappingMethod = ArithmeticModeName(operation, "wrapping");
                var pairIndex = 0;
                foreach (var pair in ArithmeticPairs(width, operation))
                {
                    var suffix = $"{width.Name}_{operation.ToString().ToLowerInvariant()}_{pairIndex}";
                    var leftName = "left_" + suffix;
                    var rightName = "right_" + suffix;
                    source.AppendLine($"        let {leftName}: {width.Name} = {FormatArithmeticLiteral(pair.Left, width)};");
                    source.AppendLine($"        let {rightName}: {width.Name} = {FormatArithmeticLiteral(pair.Right, width)};");
                    var left = pairIndex % 3 == 0
                        ? $"self::app::main::pass_{width.Name}({leftName})"
                        : leftName;
                    var right = pairIndex % 3 == 0
                        ? $"self::app::main::pass_{width.Name}({rightName})"
                        : rightName;
                    var exact = ArithmeticResult(pair.Left, pair.Right, operation);
                    var overflow = exact < width.Minimum || exact > width.Maximum;
                    var checkedAssertion = overflow
                        ? $"self::app::main::checked_overflow_{width.Name}({left}.{checkedMethod}({right}))"
                        : $"self::app::main::checked_value_{width.Name}({left}.{checkedMethod}({right}), {FormatArithmeticLiteral(exact, width)})";
                    AppendArithmeticAssertion(source, checkedAssertion);
                    AppendArithmeticAssertion(source,
                        $"{left}.{saturatingMethod}({right}) == {FormatArithmeticLiteral(SaturateArithmeticResult(exact, width), width)}");
                    AppendArithmeticAssertion(source,
                        $"{left}.{wrappingMethod}({right}) == {FormatArithmeticLiteral(WrapArithmeticResult(exact, width), width)}");
                    pairIndex++;
                }
            }
        }
    }

    private static void AppendArithmeticDirectLiteralChecks(StringBuilder source)
    {
        foreach (var operation in Enum.GetValues<ArithmeticOperation>())
        {
            var exact = ArithmeticResult(BigInteger.One, new BigInteger(2), operation);
            var checkedMethod = ArithmeticModeName(operation, "checked");
            var saturatingMethod = ArithmeticModeName(operation, "saturating");
            var wrappingMethod = ArithmeticModeName(operation, "wrapping");
            AppendArithmeticAssertion(source,
                $"self::app::main::checked_value_i32(1.{checkedMethod}(2), {FormatArithmeticLiteral(exact, CreateArithmeticWidth("i32", 32, true))})");
            AppendArithmeticAssertion(source,
                $"1.{saturatingMethod}(2) == {FormatArithmeticLiteral(exact, CreateArithmeticWidth("i32", 32, true))}");
            AppendArithmeticAssertion(source,
                $"1.{wrappingMethod}(2) == {FormatArithmeticLiteral(exact, CreateArithmeticWidth("i32", 32, true))}");
        }

        AppendArithmeticAssertion(source, "self::app::main::checked_value_i32(-1.checked_add(2), 1)");
        AppendArithmeticAssertion(source, "self::app::main::checked_value_i32((-1).checked_add(2), 1)");
        AppendArithmeticAssertion(source, "self::app::main::checked_value_i32((1).checked_sub(2), -1)");
        AppendArithmeticAssertion(source, "self::app::main::checked_value_i32((-1).checked_sub(2), -3)");
        AppendArithmeticAssertion(source, "(1).saturating_mul(2) == 2");
        AppendArithmeticAssertion(source, "(-1).wrapping_add(2) == 1");
        AppendArithmeticAssertion(source, "self::app::main::checked_value_i32(1. // numeric-dot trivia\n            checked_add(2), 3)");
        AppendArithmeticAssertion(source, "self::app::main::checked_value_i32(1.checked_add // call-opener trivia\n            (2), 3)");
        AppendArithmeticAssertion(source, "self::app::main::checked_value_i64(-1i64.checked_add(2i64), 1i64)");
    }

    private static void AppendArithmeticTraceCases(StringBuilder source)
    {
        source.AppendLine("    if args.mode == 100 {");
        source.AppendLine("        let result: Result<i32, ArithmeticError> = self::app::main::trace_value(logger, \"checked-left\", 10).checked_add(self::app::main::trace_value(logger, \"checked-right\", 20));");
        source.AppendLine("        let record: self::app::main::ArithmeticRecord = self::app::main::ArithmeticRecord { outcome: result };");
        source.AppendLine("        if (self::app::main::checked_value_i32(record.outcome, 30)) { return Ok(\"trace-ok\"); }");
        source.AppendLine("        return Err(self::app::main::ProbeError.Failed);");
        source.AppendLine("    }");
        source.AppendLine("    if args.mode == 101 {");
        source.AppendLine("        let result: Result<i32, ArithmeticError> = self::app::main::trace_value(logger, \"checked-overflow-left\", 2147483647).checked_add(self::app::main::trace_value(logger, \"checked-overflow-right\", 1));");
        source.AppendLine("        let forwarded: Result<i32, ArithmeticError> = self::app::main::preserve_result(result);");
        source.AppendLine("        if (self::app::main::checked_overflow_i32(forwarded)) { return Ok(\"trace-ok\"); }");
        source.AppendLine("        return Err(self::app::main::ProbeError.Failed);");
        source.AppendLine("    }");
        source.AppendLine("    if args.mode == 102 {");
        source.AppendLine("        let result: i32 = self::app::main::trace_value(logger, \"saturating-left\", 8).saturating_add(self::app::main::trace_value(logger, \"saturating-right\", 4));");
        source.AppendLine("        if (self::app::main::is_twelve(result)) { return Ok(\"trace-ok\"); }");
        source.AppendLine("        return Err(self::app::main::ProbeError.Failed);");
        source.AppendLine("    }");
        source.AppendLine("    if args.mode == 103 {");
        source.AppendLine("        let result: i32 = self::app::main::trace_value(logger, \"wrapping-left\", 8).wrapping_add(self::app::main::trace_value(logger, \"wrapping-right\", 4));");
        source.AppendLine("        if (self::app::main::is_twelve(result)) { return Ok(\"trace-ok\"); }");
        source.AppendLine("        return Err(self::app::main::ProbeError.Failed);");
        source.AppendLine("    }");
    }

    private static void AppendArithmeticOperandFaultCases(StringBuilder source)
    {
        var cases = new (int Selector, string Mode, string Left, string Right, string[] Events)[]
        {
            (110, "checked", "self::app::main::trace_overflow(logger, \"checked-left-fault\")", "self::app::main::trace_value(logger, \"checked-right-unreached\", 1)", ["checked-left-fault"]),
            (111, "checked", "self::app::main::trace_value(logger, \"checked-left-before-right-fault\", 1)", "self::app::main::trace_overflow(logger, \"checked-right-fault\")", ["checked-left-before-right-fault", "checked-right-fault"]),
            (112, "saturating", "self::app::main::trace_overflow(logger, \"saturating-left-fault\")", "self::app::main::trace_value(logger, \"saturating-right-unreached\", 1)", ["saturating-left-fault"]),
            (113, "saturating", "self::app::main::trace_value(logger, \"saturating-left-before-right-fault\", 1)", "self::app::main::trace_overflow(logger, \"saturating-right-fault\")", ["saturating-left-before-right-fault", "saturating-right-fault"]),
            (114, "wrapping", "self::app::main::trace_overflow(logger, \"wrapping-left-fault\")", "self::app::main::trace_value(logger, \"wrapping-right-unreached\", 1)", ["wrapping-left-fault"]),
            (115, "wrapping", "self::app::main::trace_value(logger, \"wrapping-left-before-right-fault\", 1)", "self::app::main::trace_overflow(logger, \"wrapping-right-fault\")", ["wrapping-left-before-right-fault", "wrapping-right-fault"])
        };

        foreach (var (selector, mode, left, right, _) in cases)
        {
            source.AppendLine($"    if args.mode == {selector} {{");
            if (mode == "checked")
                source.AppendLine($"        let result: Result<i32, ArithmeticError> = {left}.{mode}_add({right});");
            else
                source.AppendLine($"        let result: i32 = {left}.{mode}_add({right});");
            source.AppendLine("        return Ok(\"unexpected\");");
            source.AppendLine("    }");
        }
    }

    private static void AppendArithmeticDefaultFaultCases(StringBuilder source, IReadOnlyList<ArithmeticWidth> widths)
    {
        var selector = 1;
        foreach (var width in widths)
        {
            var one = FormatArithmeticLiteral(BigInteger.One, width);
            var two = FormatArithmeticLiteral(new BigInteger(2), width);
            var zero = FormatArithmeticLiteral(BigInteger.Zero, width);
            var maximum = FormatArithmeticLiteral(width.Maximum, width);
            var minimum = FormatArithmeticLiteral(width.Minimum, width);
            var addRight = one;
            var subtractLeft = width.IsSigned ? minimum : zero;
            var subtractRight = one;
            var multiplyLeft = width.IsSigned ? minimum : maximum;
            var multiplyRight = width.IsSigned ? FormatArithmeticLiteral(-BigInteger.One, width) : two;
            foreach (var (name, left, op, right) in new[]
                     {
                         ("add", maximum, "+", addRight),
                         ("subtract", subtractLeft, "-", subtractRight),
                         ("multiply", multiplyLeft, "*", multiplyRight)
                     })
            {
                source.AppendLine($"    if args.mode == {selector} {{");
                source.AppendLine($"        let overflow_{width.Name}_{name}: {width.Name} = {left} {op} {right};");
                source.AppendLine("        return Ok(\"unexpected\");");
                source.AppendLine("    }");
                selector++;
            }
        }
    }

    private static void AppendArithmeticAssertion(StringBuilder source, string expression) =>
        source.AppendLine($"        if ({expression}) {{ }} else {{ return Err(self::app::main::ProbeError.Failed); }}");

    private static void AssertIntegerArithmeticNoDependencyLock(ProcessResult result, string packageName)
    {
        AssertEqual(0, result.ExitCode, Describe(result));
        AssertEqual(string.Empty, result.StandardError, Describe(result));
        AssertEqual($"No dependencies to lock for package '{packageName}'." + Environment.NewLine,
            result.StandardOutput, Describe(result));
    }

    private static async Task TestArithmeticF64ScannerPreservation(Harness harness)
    {
        foreach (var (name, literal, token) in new[]
                 {
                     ("member-name", "1.f64()", "1.f64"),
                     ("exponent-like-member", "1.e3()", "1.e3"),
                     ("unknown-mode", "1.not_an_arithmetic_mode(2)", "1.not_an_arithmetic_mode")
                 })
        {
            await AssertBytesStandaloneDiagnosticAsync(
                harness,
                "integer-arithmetic-f64-scanner-" + name,
                F64InvalidLiteralSource("integer_arithmetic_" + name.Replace('-', '_'), literal),
                "E_SYNTAX",
                "Malformed f64 literal",
                literal,
                token);
        }
    }

    private static async Task TestIntegerArithmeticDiagnostics(
        Harness harness,
        IReadOnlyList<ArithmeticWidth> widths)
    {
        string[] modes =
        [
            "checked_add", "checked_sub", "checked_mul",
            "saturating_add", "saturating_sub", "saturating_mul",
            "wrapping_add", "wrapping_sub", "wrapping_mul"
        ];

        var aritySource = new StringBuilder("module harness::arithmetic_mode_arity;\n");
        var arityExpected = new List<ArithmeticDiagnosticExpectation>();
        foreach (var mode in modes)
        {
            var resultType = mode.StartsWith("checked_", StringComparison.Ordinal)
                ? "Result<i32, ArithmeticError>"
                : "i32";
            var missingMarker = $"value.{mode}()";
            aritySource.AppendLine($"fn missing_{mode}(value: i32) -> {resultType} effects {{}} {{ return {missingMarker}; }}");
            arityExpected.Add(new ArithmeticDiagnosticExpectation(
                "E_TYPE_MISMATCH", $"Integer arithmetic mode '{mode}' expects 1 argument, got 0",
                missingMarker, mode));
            var extraMarker = $"value.{mode}(1, 2)";
            aritySource.AppendLine($"fn extra_{mode}(value: i32) -> {resultType} effects {{}} {{ return {extraMarker}; }}");
            arityExpected.Add(new ArithmeticDiagnosticExpectation(
                "E_TYPE_MISMATCH", $"Integer arithmetic mode '{mode}' expects 1 argument, got 2",
                extraMarker, mode));
        }
        await AssertArithmeticDiagnosticSetAsync(
            harness, "integer-arithmetic-mode-arity", aritySource.ToString(), arityExpected);

        var widthSource = new StringBuilder("module harness::arithmetic_mode_widths;\n");
        var widthExpected = new List<ArithmeticDiagnosticExpectation>();
        foreach (var receiver in widths)
        {
            foreach (var argument in widths.Where(candidate => candidate.Name != receiver.Name))
            {
                var leftName = $"left_{receiver.Name}_{argument.Name}";
                var rightName = $"right_{receiver.Name}_{argument.Name}";
                var marker = $"{leftName}.checked_add({rightName})";
                var resultType = $"Result<{receiver.Name}, ArithmeticError>";
                widthSource.AppendLine(
                    $"fn bad_{receiver.Name}_{argument.Name}({leftName}: {receiver.Name}, {rightName}: {argument.Name}) -> {resultType} effects {{}} {{ return {marker}; }}");
                widthExpected.Add(new ArithmeticDiagnosticExpectation(
                    "E_TYPE_MISMATCH",
                    $"Integer arithmetic mode 'checked_add' expects '{receiver.Name}' argument, found '{argument.Name}'",
                    marker,
                    rightName));
            }
        }
        await AssertArithmeticDiagnosticSetAsync(
            harness, "integer-arithmetic-mode-widths", widthSource.ToString(), widthExpected);

        const string floatReceiver = """
            module harness::arithmetic_mode_f64_receiver;
            fn bad(value: f64) -> f64 effects {} { return value.wrapping_add(1f64); }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness,
            "integer-arithmetic-mode-f64-receiver",
            floatReceiver,
            "E_TYPE_MISMATCH",
            "Integer arithmetic mode 'wrapping_add' requires an i32, i64, u32, or u64 receiver, found 'f64'",
            "value.wrapping_add(1f64)",
            "wrapping_add");

        const string floatArgument = """
            module harness::arithmetic_mode_f64_argument;
            fn bad(value: i32) -> Result<i32, ArithmeticError> effects {} { return value.checked_add(1f64); }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness,
            "integer-arithmetic-mode-f64-argument",
            floatArgument,
            "E_TYPE_MISMATCH",
            "Integer arithmetic mode 'checked_add' expects 'i32' argument, found 'f64'",
            "value.checked_add(1f64)",
            "1f64");

        const string forgedError = """
            module harness::arithmetic_error_forge;
            fn bad() -> ArithmeticError effects {} { return ArithmeticError.Overflow(); }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness,
            "integer-arithmetic-forged-error",
            forgedError,
            "E_TYPE_MISMATCH",
            "ArithmeticError.Overflow can only be produced by checked integer arithmetic",
            "ArithmeticError.Overflow()",
            "Overflow");

        const string forgedErrorField = """
            module harness::arithmetic_error_forge_field;
            fn bad() -> ArithmeticError effects {} { return ArithmeticError.Overflow; }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness,
            "integer-arithmetic-forged-error-field",
            forgedErrorField,
            "E_TYPE_MISMATCH",
            "ArithmeticError.Overflow can only be produced by checked integer arithmetic",
            "ArithmeticError.Overflow;",
            "Overflow");

        const string redeclaredError = """
            module harness::arithmetic_error_redeclaration;
            union ArithmeticError { Forged }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness,
            "integer-arithmetic-redeclared-error",
            redeclaredError,
            "E_NAME_DUPLICATE",
            "Type name 'ArithmeticError' is reserved",
            "union ArithmeticError",
            "ArithmeticError");

        const string shadowedError = """
            module harness::arithmetic_error_type_parameter;
            fn bad<ArithmeticError>(value: ArithmeticError) -> i32 effects {} { return 0; }
            """;
        await AssertBytesStandaloneDiagnosticAsync(
            harness,
            "integer-arithmetic-shadowed-error",
            shadowedError,
            "E_NAME_DUPLICATE",
            "Type parameter 'ArithmeticError' is reserved",
            "bad<ArithmeticError>",
            "ArithmeticError");
    }

    private static async Task AssertArithmeticDiagnosticSetAsync(
        Harness harness,
        string caseName,
        string source,
        IReadOnlyList<ArithmeticDiagnosticExpectation> expected)
    {
        var check = await harness.InvokeAsync(
            caseName + "-check", "check", source, "--json");
        AssertTrue(check.ExitCode != 0, $"Invalid arithmetic-mode source unexpectedly checked for {caseName}. {Describe(check)}");
        AssertEqual(string.Empty, check.StandardError, Describe(check));
        var diagnostics = ParseDiagnosticSnapshots(check.StandardOutput);
        AssertEqual(expected.Count, diagnostics.Length,
            $"Unexpected arithmetic diagnostic count for {caseName}: {string.Join(" | ", diagnostics.Select(item => $"{item.Code}: {item.Message} at {item.StartLine}:{item.StartColumn}-{item.EndLine}:{item.EndColumn}"))}");
        var sourcePath = Path.GetFullPath(harness.LastSourcePath);
        foreach (var item in expected)
        {
            var diagnostic = SelectBytesDiagnostic(
                source, diagnostics, item.Code, item.Message, item.Marker, item.RangeToken, caseName);
            AssertEqual(sourcePath, Path.GetFullPath(diagnostic.File),
                $"Unexpected source path for {caseName} diagnostic {item.Message}.");
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath)!;
        AssertNoCompilerArtifacts(sourceDirectory);
        var build = await harness.InvokeFileAsync(caseName + "-build", sourcePath, "build");
        AssertTrue(build.ExitCode != 0, $"Invalid arithmetic-mode source unexpectedly built for {caseName}. {Describe(build)}");
        var output = build.StandardOutput + build.StandardError;
        var first = expected[0];
        AssertTrue(output.Contains(first.Code, StringComparison.Ordinal)
            && output.Contains(first.Message, StringComparison.Ordinal),
            $"Rejected arithmetic-mode build must preserve {first.Code}: {first.Message} for {caseName}. {Describe(build)}");
        AssertTrue(!output.Contains("Built executable: ", StringComparison.Ordinal)
            && !output.Contains("Built library: ", StringComparison.Ordinal)
            && !output.Contains("Built native executable: ", StringComparison.Ordinal),
            $"Rejected arithmetic-mode source must not announce an artifact for {caseName}. {Describe(build)}");
        AssertNoCompilerArtifacts(sourceDirectory);
    }

    private static IReadOnlyList<ArithmeticTraceSelector> IntegerArithmeticTraceSelectors() =>
    [
        new ArithmeticTraceSelector("checked-trace", 100, ["checked-left", "checked-right"]),
        new ArithmeticTraceSelector("checked-overflow-trace", 101, ["checked-overflow-left", "checked-overflow-right"]),
        new ArithmeticTraceSelector("saturating-trace", 102, ["saturating-left", "saturating-right"]),
        new ArithmeticTraceSelector("wrapping-trace", 103, ["wrapping-left", "wrapping-right"])
    ];

    private static IReadOnlyList<ArithmeticFaultSelector> IntegerArithmeticDefaultFaultSelectors(
        IReadOnlyList<ArithmeticWidth> widths)
    {
        var selectors = new List<ArithmeticFaultSelector>();
        var selector = 1;
        foreach (var width in widths)
        {
            selectors.Add(new ArithmeticFaultSelector($"{width.Name}-addition", selector++));
            selectors.Add(new ArithmeticFaultSelector($"{width.Name}-subtraction", selector++));
            selectors.Add(new ArithmeticFaultSelector($"{width.Name}-multiplication", selector++));
        }
        return selectors;
    }

    private static IReadOnlyList<ArithmeticTraceSelector> IntegerArithmeticOperandFaultSelectors() =>
    [
        new ArithmeticTraceSelector("checked-receiver-fault", 110, ["checked-left-fault"]),
        new ArithmeticTraceSelector("checked-rhs-fault", 111, ["checked-left-before-right-fault", "checked-right-fault"]),
        new ArithmeticTraceSelector("saturating-receiver-fault", 112, ["saturating-left-fault"]),
        new ArithmeticTraceSelector("saturating-rhs-fault", 113, ["saturating-left-before-right-fault", "saturating-right-fault"]),
        new ArithmeticTraceSelector("wrapping-receiver-fault", 114, ["wrapping-left-fault"]),
        new ArithmeticTraceSelector("wrapping-rhs-fault", 115, ["wrapping-left-before-right-fault", "wrapping-right-fault"])
    ];

    private static void AssertIntegerArithmeticRuntimeFault(ProcessResult run, string context)
    {
        AssertEqual(70, run.ExitCode, $"{context} must use the generic runtime-fault exit. {Describe(run)}");
        AssertEqual(string.Empty, run.StandardOutput, $"{context} must not print partial output. {Describe(run)}");
        AssertEqual("Runtime fault", run.StandardError.TrimEnd('\r', '\n'),
            $"{context} must expose only the generic runtime-fault message. {Describe(run)}");
        AssertTrue(!run.StandardError.Contains("Exception", StringComparison.Ordinal)
            && !run.StandardError.Contains(" at ", StringComparison.Ordinal),
            $"{context} must not expose a backend exception type or stack trace.");
    }

    private static void AssertIntegerArithmeticFaultWithEvents(
        ProcessResult run,
        string[] events,
        string context)
    {
        AssertEqual(70, run.ExitCode, $"{context} must remain an uncaught operand runtime fault. {Describe(run)}");
        AssertEqual(string.Empty, run.StandardOutput, $"{context} must not print partial output. {Describe(run)}");
        AssertUnitLoggerEvents(run.StandardError, events, context, expectFault: true);
        AssertTrue(!run.StandardError.Contains("Exception", StringComparison.Ordinal)
            && !run.StandardError.Contains(" at ", StringComparison.Ordinal),
            $"{context} must not expose a backend exception type or stack trace.");
    }

    private static void AssertArithmeticBackendParity(ProcessResult managed, ProcessResult native, string context)
    {
        AssertEqual(managed.ExitCode, native.ExitCode, $"Managed and NativeAOT exit codes differ for {context}.");
        AssertEqual(managed.StandardOutput, native.StandardOutput, $"Managed and NativeAOT stdout differ for {context}.");
        AssertEqual(managed.StandardError, native.StandardError, $"Managed and NativeAOT stderr differs for {context}.");
    }

    private static async Task TestIntegerArithmeticMetadata(Harness harness)
    {
        const string manifest = "name = \"integer-arithmetic-metadata\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n";
        const string source = """
            module numeric;
            pub struct ArithmeticRecord { outcome: Result<i32, ArithmeticError> }
            pub fn checked_add(left: i32, right: i32) -> Result<i32, ArithmeticError> effects {} {
                return left.checked_add(right);
            }
            pub fn forward<T>(value: Result<T, ArithmeticError>) -> Result<T, ArithmeticError> effects {} {
                return value;
            }
            pub trait ArithmeticWitness {
                fn preserve(value: Self, error: Result<i32, ArithmeticError>) -> ArithmeticError effects {};
            }
            """;

        async Task<string> CreatePackage(string caseName, string? lineEnding = null)
        {
            var root = await harness.WritePackageAsync(
                caseName,
                manifest,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["src/numeric.hob"] = source });
            if (lineEnding is not null)
            {
                var path = Path.Combine(root, "src", "numeric.hob");
                var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                await File.WriteAllTextAsync(path,
                    normalized.Replace("\n", lineEnding, StringComparison.Ordinal), new UTF8Encoding(false));
            }
            AssertIntegerArithmeticNoDependencyLock(
                await harness.InvokePackageDirectoryAsync(caseName + "-lock", root, "lock"),
                "integer-arithmetic-metadata");
            return root;
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
                    "ArithmeticError metadata must use inspect API schema 13.");
                var checkedAdd = api.GetProperty("functions").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::numeric::checked_add");
                AssertArithmeticApiResultType(checkedAdd.GetProperty("return_type"), "i32", "API checked-add result");
                var forward = api.GetProperty("functions").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::numeric::forward");
                AssertArithmeticApiResultType(forward.GetProperty("return_type"), "T", "API generic result",
                    genericSuccessType: true);
                var record = api.GetProperty("structs").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "self::numeric::ArithmeticRecord");
                var field = record.GetProperty("fields").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "outcome");
                AssertArithmeticApiResultType(field.GetProperty("type"), "i32", "API nominal storage result");
            }
            var repeatedApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
            AssertEqual(apiRun.StandardOutput, repeatedApi.StandardOutput,
                $"Repeated {caseName} API reports must be byte-identical.");

            var auditRun = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
            using (var auditDocument = JsonDocument.Parse(auditRun.StandardOutput))
            {
                var audit = auditDocument.RootElement;
                AssertAuditPropertyOrder(audit);
                AssertEqual(11, audit.GetProperty("schema_version").GetInt32(),
                    "ArithmeticError audit facts must use schema 11.");
                var trait = audit.GetProperty("compiler").GetProperty("traits").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "ArithmeticWitness");
                var method = trait.GetProperty("methods").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == "preserve");
                AssertAuditTypePropertyOrder(method.GetProperty("parameters")[0].GetProperty("type"));
                AssertEqual("self", method.GetProperty("parameters")[0].GetProperty("type").GetProperty("kind").GetString(),
                    "audit trait Self parameter should retain the existing shape.");
                AssertArithmeticAuditResultType(method.GetProperty("parameters")[1].GetProperty("type"),
                    "i32", "audit trait Result parameter");
                AssertArithmeticAuditPrimitiveType(method.GetProperty("return_type"), "audit trait error return");
            }
            var repeatedAudit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertEqual(auditRun.StandardOutput, repeatedAudit.StandardOutput,
                $"Repeated {caseName} audit reports must be byte-identical.");
            return (apiRun.StandardOutput, auditRun.StandardOutput);
        }

        var referenceRoot = await CreatePackage("integer-arithmetic-metadata-reference", "\n");
        var reference = await Reports("integer-arithmetic-metadata-reference", referenceRoot);
        var relocatedRoot = await CreatePackage("integer-arithmetic-metadata-relocated");
        var relocated = await Reports("integer-arithmetic-metadata-relocated", relocatedRoot);
        AssertEqual(reference.Api, relocated.Api, "Arithmetic API facts must survive package relocation.");
        AssertEqual(reference.Audit, relocated.Audit, "Arithmetic audit facts must survive package relocation.");
        var crlfRoot = await CreatePackage("integer-arithmetic-metadata-crlf", "\r\n");
        var crlf = await Reports("integer-arithmetic-metadata-crlf", crlfRoot);
        AssertEqual(reference.Api, crlf.Api, "Arithmetic API facts must be independent of LF/CRLF input.");
        AssertEqual(reference.Audit, crlf.Audit, "Arithmetic audit facts must be independent of LF/CRLF input.");
    }

    private static void AssertArithmeticApiResultType(
        JsonElement type,
        string okName,
        string label,
        bool genericSuccessType = false)
    {
        AssertJsonPropertyOrder(type, "kind,ok,error");
        AssertEqual("result", type.GetProperty("kind").GetString(), $"{label} should retain the Result type node.");
        var ok = type.GetProperty("ok");
        AssertJsonPropertyOrder(ok, genericSuccessType ? "kind,name,ordinal" : "kind,name");
        AssertEqual(genericSuccessType ? "type_parameter" : "primitive", ok.GetProperty("kind").GetString(),
            $"{label} success type should retain its actual API node kind.");
        AssertEqual(okName, ok.GetProperty("name").GetString(), $"{label} success type name mismatch.");
        if (genericSuccessType)
            AssertEqual(0, ok.GetProperty("ordinal").GetInt32(), $"{label} type parameter ordinal mismatch.");
        AssertArithmeticApiPrimitiveType(type.GetProperty("error"), label + " error type");
    }

    private static void AssertArithmeticAuditResultType(JsonElement type, string okName, string label)
    {
        AssertAuditTypePropertyOrder(type);
        AssertEqual("result", type.GetProperty("kind").GetString(), $"{label} should retain the Result audit node.");
        var ok = type.GetProperty("ok");
        AssertAuditTypePropertyOrder(ok);
        AssertEqual("primitive", ok.GetProperty("kind").GetString(), $"{label} success type should be primitive.");
        AssertEqual(okName, ok.GetProperty("name").GetString(), $"{label} success type name mismatch.");
        AssertArithmeticAuditPrimitiveType(type.GetProperty("error"), label + " error type");
    }

    private static void AssertArithmeticApiPrimitiveType(JsonElement type, string label)
    {
        AssertJsonPropertyOrder(type, "kind,name");
        AssertEqual("primitive", type.GetProperty("kind").GetString(), $"{label} must use the primitive type shape.");
        AssertEqual("ArithmeticError", type.GetProperty("name").GetString(), $"{label} must name ArithmeticError.");
    }

    private static void AssertArithmeticAuditPrimitiveType(JsonElement type, string label)
    {
        AssertAuditTypePropertyOrder(type);
        AssertEqual("primitive", type.GetProperty("kind").GetString(), $"{label} must use the audit primitive shape.");
        AssertEqual("ArithmeticError", type.GetProperty("name").GetString(), $"{label} must name ArithmeticError.");
    }

}
