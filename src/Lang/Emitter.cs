using System.Globalization;
using System.Text;
using System.Text.Json;

internal static class Emitter
{
    public static string Emit(CheckedProgram program, bool executable = true)
    {
        var entry = program.EntryFunctionId is int entryId
            ? program.Functions.FirstOrDefault(function =>
                function.Id == entryId && function.Name == "main" && function.TypeParameters.Count == 0 &&
                function.Parameters.Count == 0 &&
                (function.ReturnType.IsI32 || function.ReturnType.IsBool || function.ReturnType.IsText))
            : null;
        var entryCommand = program.EntryCommandId is int commandId
            ? program.Commands.FirstOrDefault(command => command.Id == commandId)
            : null;
        if (executable && entry is null && entryCommand is null)
            throw new InvalidOperationException("Executable emission requires a selected valid entry function");

        var emitter = new SourceEmitter(program);
        return emitter.Emit(entry, executable, command: entryCommand);
    }

    public static string EmitCommandSchema(CheckedProgram program)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteStartArray("commands");
            foreach (var command in program.Commands.OrderBy(command => command.Id))
            {
                writer.WriteStartObject();
                writer.WriteString("name", command.Name);
                writer.WriteString("help", command.Help);
                writer.WriteString("handler", command.HandlerReference);
                writer.WriteString("error_formatter", command.ErrorReference);
                WriteCommandInputs(writer, command, CheckedCommandInputKind.Argument, includeDefault: false);
                WriteCommandInputs(writer, command, CheckedCommandInputKind.Option, includeDefault: true);
                WriteCommandInputs(writer, command, CheckedCommandInputKind.Flag, includeDefault: false);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static void WriteCommandInputs(
        Utf8JsonWriter writer,
        CheckedCommand command,
        CheckedCommandInputKind kind,
        bool includeDefault)
    {
        var propertyName = kind switch
        {
            CheckedCommandInputKind.Argument => "arguments",
            CheckedCommandInputKind.Option => "options",
            CheckedCommandInputKind.Flag => "flags",
            _ => throw new InvalidOperationException("Unknown checked command input kind")
        };

        writer.WriteStartArray(propertyName);
        foreach (var input in command.Inputs.Where(input => input.Kind == kind).OrderBy(input => input.FieldIndex))
        {
            writer.WriteStartObject();
            writer.WriteString("name", input.Name);
            writer.WriteString("type", CommandTypeName(input.Type));
            if (includeDefault)
            {
                writer.WritePropertyName("default");
                WriteCommandLiteral(writer, input.Default);
            }
            writer.WriteString("help", input.Help);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteCommandLiteral(Utf8JsonWriter writer, CheckedCommandLiteral? literal)
    {
        if (literal is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (literal.Kind)
        {
            case CheckedCommandLiteralKind.Text:
                writer.WriteStringValue(literal.TextValue);
                break;
            case CheckedCommandLiteralKind.I32:
                writer.WriteNumberValue(literal.IntegerValue!.Value);
                break;
            case CheckedCommandLiteralKind.Boolean:
                writer.WriteBooleanValue(literal.BooleanValue!.Value);
                break;
            default:
                throw new InvalidOperationException("Unknown checked command literal kind");
        }
    }

    private static string CommandTypeName(LangType type) => type.Kind switch
    {
        LangTypeKind.FilePath => "FilePath",
        LangTypeKind.Text => "Text",
        LangTypeKind.I32 => "i32",
        LangTypeKind.Bool => "bool",
        _ => throw new InvalidOperationException("Unsupported checked command input type")
    };

    public static string EmitTests(CheckedProgram program, string? rootPackageId = null)
    {
        var tests = program.Tests
            .Where(test => rootPackageId is null || test.PackageId == rootPackageId)
            .ToArray();
        var testFunctionIds = tests.Select(test => test.FunctionId).ToHashSet();
        var emitter = new SourceEmitter(program, testFunctionIds);
        return emitter.Emit(entry: null, executable: false, tests: tests);
    }

    private sealed class SourceEmitter(
        CheckedProgram program,
        IReadOnlySet<int>? includedTestFunctionIds = null)
    {
        private readonly StringBuilder _source = new();
        private CheckedFunction? _emittingFunction;
        private readonly HashSet<int> _testFunctionIds = program.Tests
            .Select(test => test.FunctionId)
            .ToHashSet();
        private readonly IReadOnlySet<int> _includedTestFunctionIds = includedTestFunctionIds ?? new HashSet<int>();

        private IEnumerable<CheckedFunction> EmittedFunctions => program.Functions.Where(function =>
            !_testFunctionIds.Contains(function.Id) || _includedTestFunctionIds.Contains(function.Id));

        public string Emit(
            CheckedFunction? entry,
            bool executable,
            IReadOnlyList<CheckedTest>? tests = null,
            CheckedCommand? command = null)
        {
            _source.AppendLine("using System;");
            _source.AppendLine("using System.Globalization;");
            if (UsesFsReadText)
            {
                _source.AppendLine("using System.IO;");
                _source.AppendLine("using System.Security;");
                _source.AppendLine("using System.Text;");
            }
            else if (UsesTextLength || command is not null)
            {
                _source.AppendLine("using System.Text;");
            }
            _source.AppendLine();
            _source.AppendLine("public static class LangModule");
            _source.AppendLine("{");

            EmitBuiltinTypes();
            if (NeedsFilePathType) EmitFilePathType();
            if (NeedsFsReadType) EmitFsReadType();
            if (NeedsFsErrorType) EmitFsErrorType();
            foreach (var union in program.Unions) EmitUnion(union);
            foreach (var structure in program.Structs) EmitStruct(structure);
            foreach (var function in EmittedFunctions) EmitFunction(function);
            if (UsesFsReadText) EmitFsReadTextHelper();
            if (UsesTextLength) EmitTextLengthHelper();
            EmitArithmeticHelpers();
            if (tests is not null)
                EmitTestEntryPoint(tests);
            else if (command is not null)
                EmitCommandEntryPoint(command);
            else if (executable)
                EmitEntryPoint(entry!);

            _source.AppendLine("}");
            return _source.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private void EmitBuiltinTypes()
        {
            _source.AppendLine("    public abstract record Option<T>");
            _source.AppendLine("    {");
            _source.AppendLine("        public sealed record Some(T Value) : Option<T>;");
            _source.AppendLine("        public sealed record None() : Option<T>;");
            _source.AppendLine("    }");
            _source.AppendLine();
            _source.AppendLine("    public abstract record Result<T, E>");
            _source.AppendLine("    {");
            _source.AppendLine("        public sealed record Ok(T Value) : Result<T, E>;");
            _source.AppendLine("        public sealed record Err(E Error) : Result<T, E>;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFsReadType()
        {
            _source.AppendLine("    public sealed class FsRead");
            _source.AppendLine("    {");
            _source.AppendLine("        internal FsRead() { }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFilePathType()
        {
            _source.AppendLine("    public sealed class FilePath");
            _source.AppendLine("    {");
            _source.AppendLine("        internal FilePath(string value) => Value = value;");
            _source.AppendLine("        internal string Value { get; }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFsErrorType()
        {
            _source.AppendLine("    public abstract record FsError");
            _source.AppendLine("    {");
            _source.AppendLine("        public sealed record NotFound() : FsError;");
            _source.AppendLine("        public sealed record PermissionDenied() : FsError;");
            _source.AppendLine("        public sealed record InvalidPath() : FsError;");
            _source.AppendLine("        public sealed record InvalidText() : FsError;");
            _source.AppendLine("        public sealed record Io() : FsError;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitUnion(CheckedUnion union)
        {
            var accessibility = union.Public ? "public" : "private";
            _source.Append("    ").Append(accessibility).Append(" abstract record Union_").Append(union.Id).AppendLine();
            _source.AppendLine("    {");
            foreach (var variant in union.Variants)
            {
                _source.Append("        public sealed record Variant_")
                    .Append(union.Id).Append('_').Append(variant.Id).Append('(');
                for (var i = 0; i < variant.Fields.Count; i++)
                {
                    if (i != 0) _source.Append(", ");
                    _source.Append(EmitType(variant.Fields[i].Type)).Append(" Payload_").Append(i);
                }
                _source.Append(") : Union_").Append(union.Id).AppendLine(";");
            }
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitStruct(CheckedStruct structure)
        {
            _source.Append("    ").Append(structure.Public ? "public" : "private")
                .Append(" sealed record Struct_").Append(structure.Id.ToString(CultureInfo.InvariantCulture)).Append('(');
            for (var i = 0; i < structure.Fields.Count; i++)
            {
                if (i != 0) _source.Append(", ");
                var field = structure.Fields[i];
                _source.Append(EmitType(field.Type)).Append(" Field_")
                    .Append(field.Index.ToString(CultureInfo.InvariantCulture));
            }
            _source.AppendLine(");");
            _source.AppendLine();
        }

        private void EmitFunction(CheckedFunction function)
        {
            _emittingFunction = function;
            _source.Append("    ").Append(function.Public ? "public" : "private").Append(" static ")
                .Append(EmitType(function.ReturnType)).Append(" Function_").Append(function.Id);
            if (function.TypeParameters.Count != 0)
            {
                var typeParameterNames = function.TypeParameters.Select((_, index) => TypeParameterName(index));
                _source.Append('<').Append(string.Join(", ", typeParameterNames)).Append('>');
            }
            _source.Append('(');
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (i != 0) _source.Append(", ");
                var parameter = function.Parameters[i];
                _source.Append(EmitType(parameter.Type)).Append(" Local_").Append(parameter.LocalId);
            }
            _source.AppendLine(")");
            _source.AppendLine("    {");
            EmitStatements(function.Body, 2);
            _source.AppendLine("    }");
            _source.AppendLine();
            _emittingFunction = null;
        }

        private void EmitStatements(IEnumerable<TypedStmt> statements, int indent)
        {
            foreach (var statement in statements) EmitStatement(statement, indent);
        }

        private void EmitStatement(TypedStmt statement, int indent)
        {
            switch (statement)
            {
                case TypedLetStmt let:
                    Indent(indent);
                    _source.Append(EmitType(let.Type)).Append(" Local_").Append(let.LocalId)
                        .Append(" = ").Append(EmitExpr(let.Value)).AppendLine(";");
                    break;
                case TypedReturnStmt ret:
                    Indent(indent);
                    _source.Append("return ").Append(EmitExpr(ret.Value)).AppendLine(";");
                    break;
                case TypedIfStmt conditional:
                    Indent(indent);
                    _source.Append("if (").Append(EmitExpr(conditional.Condition)).AppendLine(")");
                    Indent(indent);
                    _source.AppendLine("{");
                    EmitStatements(conditional.ThenBody, indent + 1);
                    Indent(indent);
                    _source.AppendLine("}");
                    if (conditional.ElseBody is not null)
                    {
                        Indent(indent);
                        _source.AppendLine("else");
                        Indent(indent);
                        _source.AppendLine("{");
                        EmitStatements(conditional.ElseBody, indent + 1);
                        Indent(indent);
                        _source.AppendLine("}");
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown typed statement in emitter");
            }
        }

        private string EmitExpr(TypedExpr expression) => expression switch
        {
            TypedNumberExpr number => number.Value.ToString(CultureInfo.InvariantCulture),
            TypedBoolExpr boolean => boolean.Value ? "true" : "false",
            TypedTextExpr text => JsonSerializer.Serialize(text.Value),
            TypedLocalExpr local => "Local_" + local.LocalId.ToString(CultureInfo.InvariantCulture),
            TypedBinaryExpr binary => EmitBinary(binary),
            TypedCompareExpr comparison => EmitComparison(comparison),
            TypedCallExpr call => EmitCall(call),
            TypedTextLengthExpr length => "TextLength(" + EmitExpr(length.Target) + ")",
            TypedTextTrimExpr trim => "(" + EmitExpr(trim.Target) + ").Trim()",
            TypedIntrinsicCallExpr intrinsic => EmitIntrinsicCall(intrinsic),
            TypedBuiltinConstructExpr builtin => EmitBuiltinConstruct(builtin),
            TypedUnionConstructExpr variant => EmitUnionConstruct(variant),
            TypedStructConstructExpr structure => EmitStructConstruct(structure),
            TypedFieldAccessExpr field => "(" + EmitExpr(field.Target) + ").Field_" +
                field.FieldIndex.ToString(CultureInfo.InvariantCulture),
            TypedMatchExpr match => EmitMatch(match),
            _ => throw new InvalidOperationException("Unchecked expression reached emitter")
        };

        private string EmitCall(TypedCallExpr call)
        {
            var functionName = "Function_" + call.FunctionId.ToString(CultureInfo.InvariantCulture);
            if (call.TypeArguments.Count != 0)
                functionName += "<" + string.Join(", ", call.TypeArguments.Select(EmitType)) + ">";
            return functionName + "(" + string.Join(", ", call.Arguments.Select(EmitExpr)) + ")";
        }

        private string EmitIntrinsicCall(TypedIntrinsicCallExpr expression) => expression.Intrinsic switch
        {
            BuiltinIntrinsic.FsReadText when expression.Arguments.Count == 2 =>
                "ReadText(" + string.Join(", ", expression.Arguments.Select(EmitExpr)) + ")",
            BuiltinIntrinsic.FsReadText =>
                throw new InvalidOperationException("FsRead.read_text requires a receiver and a path"),
            _ => throw new InvalidOperationException("Unknown builtin intrinsic")
        };

        private string EmitBinary(TypedBinaryExpr expression)
        {
            var helper = expression.Op switch
            {
                "+" => "CheckedAdd",
                "-" => "CheckedSubtract",
                "*" => "CheckedMultiply",
                _ => throw new InvalidOperationException("Unknown checked arithmetic operator")
            };
            return helper + "(" + EmitExpr(expression.Left) + ", " + EmitExpr(expression.Right) + ")";
        }

        private string EmitComparison(TypedCompareExpr expression)
        {
            var left = EmitExpr(expression.Left);
            var right = EmitExpr(expression.Right);
            if (expression.Left.Type.IsText)
            {
                if (expression.Op is not ("==" or "!="))
                    throw new InvalidOperationException("Text comparison only supports equality");
                var equals = "string.Equals(" + left + ", " + right + ", StringComparison.Ordinal)";
                return expression.Op == "==" ? equals : "!" + equals;
            }

            return expression.Op switch
            {
                "==" or "!=" or "<" or "<=" or ">" or ">=" =>
                    "(" + left + " " + expression.Op + " " + right + ")",
                _ => throw new InvalidOperationException("Unknown comparison operator")
            };
        }

        private string EmitBuiltinConstruct(TypedBuiltinConstructExpr expression)
        {
            var variant = expression.Variant switch
            {
                BuiltinVariant.Some => "Some",
                BuiltinVariant.None => "None",
                BuiltinVariant.Ok => "Ok",
                BuiltinVariant.Err => "Err",
                BuiltinVariant.FsErrorNotFound or
                BuiltinVariant.FsErrorPermissionDenied or
                BuiltinVariant.FsErrorInvalidPath or
                BuiltinVariant.FsErrorInvalidText or
                BuiltinVariant.FsErrorIo => throw new InvalidOperationException("FsError variants cannot be constructed from source"),
                _ => throw new InvalidOperationException("Unknown builtin union variant")
            };
            return "new " + EmitType(expression.Type) + "." + variant + "(" +
                string.Join(", ", expression.Arguments.Select(EmitExpr)) + ")";
        }

        private string EmitUnionConstruct(TypedUnionConstructExpr expression) =>
            "new Union_" + expression.UnionId.ToString(CultureInfo.InvariantCulture) + ".Variant_" +
            expression.UnionId.ToString(CultureInfo.InvariantCulture) + "_" +
            expression.VariantId.ToString(CultureInfo.InvariantCulture) + "(" +
            string.Join(", ", expression.Arguments.Select(EmitExpr)) + ")";

        private string EmitStructConstruct(TypedStructConstructExpr expression) =>
            "new Struct_" + expression.StructId.ToString(CultureInfo.InvariantCulture) + "(" +
            string.Join(", ", expression.Fields.Select(field =>
                "Field_" + field.FieldIndex.ToString(CultureInfo.InvariantCulture) + ": " + EmitExpr(field.Value))) + ")";

        private string EmitMatch(TypedMatchExpr expression)
        {
            var arms = expression.Arms.Select(arm =>
                EmitPattern(arm.Pattern, expression.Value.Type) + " => " + EmitExpr(arm.Body));
            var fallback = expression.Arms.Any(arm => arm.Pattern is TypedWildcardPattern)
                ? ""
                : "_ => throw new InvalidOperationException(\"Invalid union value\")";
            var allArms = string.IsNullOrEmpty(fallback) ? arms : arms.Append(fallback);
            return "(" + EmitExpr(expression.Value) + ") switch { " + string.Join(", ", allArms) + " }";
        }

        private string EmitPattern(TypedPattern pattern, LangType scrutineeType)
        {
            if (pattern is TypedWildcardPattern) return "_";
            if (pattern is not TypedVariantPattern variant)
                throw new InvalidOperationException("Unknown checked pattern in emitter");

            string variantType;
            if (variant.Shape.Builtin is not null)
            {
                var variantName = variant.Shape.Builtin.Value switch
                {
                    BuiltinVariant.Some => "Some",
                    BuiltinVariant.None => "None",
                    BuiltinVariant.Ok => "Ok",
                    BuiltinVariant.Err => "Err",
                    BuiltinVariant.FsErrorNotFound => "NotFound",
                    BuiltinVariant.FsErrorPermissionDenied => "PermissionDenied",
                    BuiltinVariant.FsErrorInvalidPath => "InvalidPath",
                    BuiltinVariant.FsErrorInvalidText => "InvalidText",
                    BuiltinVariant.FsErrorIo => "Io",
                    _ => throw new InvalidOperationException("Unknown builtin pattern")
                };
                variantType = EmitType(scrutineeType) + "." + variantName;
            }
            else
            {
                var unionId = variant.Shape.UnionId!.Value;
                variantType = "Union_" + unionId.ToString(CultureInfo.InvariantCulture) + ".Variant_" +
                    unionId.ToString(CultureInfo.InvariantCulture) + "_" +
                    variant.Shape.VariantId.ToString(CultureInfo.InvariantCulture);
            }

            if (variant.Bindings.Count == 0) return variantType + " { }";
            return variantType + "(" + string.Join(", ", variant.Bindings.Select(binding =>
                "var Local_" + binding.LocalId.ToString(CultureInfo.InvariantCulture))) + ")";
        }

        private void EmitArithmeticHelpers()
        {
            _source.AppendLine("    private static int CheckedAdd(int left, int right) => checked(left + right);");
            _source.AppendLine("    private static int CheckedSubtract(int left, int right) => checked(left - right);");
            _source.AppendLine("    private static int CheckedMultiply(int left, int right) => checked(left * right);");
            _source.AppendLine();
        }

        private void EmitTextLengthHelper()
        {
            _source.AppendLine("    private static int TextLength(string value)");
            _source.AppendLine("    {");
            _source.AppendLine("        var length = 0;");
            _source.AppendLine("        foreach (var rune in value.EnumerateRunes())");
            _source.AppendLine("            length = checked(length + 1);");
            _source.AppendLine("        return length;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFsReadTextHelper()
        {
            _source.AppendLine("    private static Result<string, FsError> ReadText(FsRead receiver, string path)");
            _source.AppendLine("    {");
            _source.AppendLine("        ArgumentNullException.ThrowIfNull(receiver);");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            var bytes = File.ReadAllBytes(path);");
            _source.AppendLine("            return new Result<string, FsError>.Ok(new UTF8Encoding(false, true).GetString(bytes));");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (FileNotFoundException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.NotFound());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (DirectoryNotFoundException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.NotFound());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (SecurityException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.PermissionDenied());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (UnauthorizedAccessException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.PermissionDenied());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (DecoderFallbackException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidText());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (ArgumentException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (NotSupportedException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (PathTooLongException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (IOException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.Io());");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitEntryPoint(CheckedFunction entry)
        {
            _source.AppendLine("    public static int Main()");
            _source.AppendLine("    {");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            Console.OutputEncoding = new System.Text.UTF8Encoding(false);");

            var call = "Function_" + entry.Id.ToString(CultureInfo.InvariantCulture) + "()";
            if (entry.ReturnType.IsI32)
                _source.Append("            Console.WriteLine(").Append(call).AppendLine(".ToString(CultureInfo.InvariantCulture));");
            else if (entry.ReturnType.IsBool)
                _source.Append("            Console.WriteLine(").Append(call).AppendLine(" ? \"true\" : \"false\");");
            else
                _source.Append("            Console.WriteLine(").Append(call).AppendLine(");");
            _source.AppendLine("            return 0;");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (Exception)");
            _source.AppendLine("        {");
            _source.AppendLine("            Console.Error.WriteLine(\"Runtime fault\");");
            _source.AppendLine("            return 70;");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
        }

        private void EmitCommandEntryPoint(CheckedCommand entryCommand)
        {
            _source.AppendLine("    public static int Main(string[] args)");
            _source.AppendLine("    {");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            Console.OutputEncoding = new System.Text.UTF8Encoding(false);");
            _source.AppendLine("            if (args.Length == 1 && args[0] == \"--help\")");
            _source.AppendLine("            {");
            _source.Append("                Console.WriteLine(").Append(JsonSerializer.Serialize(TopLevelHelp())).AppendLine(");");
            _source.AppendLine("                return 0;");
            _source.AppendLine("            }");
            _source.AppendLine("            if (args.Length == 0)");
            _source.AppendLine("                return CliUsageError(\"CLI_MISSING_ARGUMENT\", \"command\");");
            _source.AppendLine("            if (args[0].StartsWith(\"--\", StringComparison.Ordinal))");
            _source.AppendLine("                return CliUsageError(\"CLI_UNKNOWN_OPTION\", args[0]);");
            _source.AppendLine("            switch (args[0])");
            _source.AppendLine("            {");
            foreach (var command in program.Commands.OrderBy(command => command.Id))
            {
                _source.Append("                case ").Append(JsonSerializer.Serialize(command.Name)).AppendLine(":");
                _source.AppendLine("                    if (args.Length == 2 && args[1] == \"--help\")");
                _source.AppendLine("                    {");
                _source.Append("                        Console.WriteLine(").Append(JsonSerializer.Serialize(CommandHelp(command))).AppendLine(");");
                _source.AppendLine("                        return 0;");
                _source.AppendLine("                    }");
                _source.Append("                    return RunCommand_").Append(command.Id.ToString(CultureInfo.InvariantCulture))
                    .AppendLine("(args[1..]);");
            }
            _source.AppendLine("                default:");
            _source.AppendLine("                    return CliUsageError(\"CLI_UNKNOWN_COMMAND\", args[0]);");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (Exception)");
            _source.AppendLine("        {");
            _source.AppendLine("            Console.Error.WriteLine(\"Runtime fault\");");
            _source.AppendLine("            return 70;");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();

            foreach (var command in program.Commands.OrderBy(command => command.Id))
                EmitCommandParser(command);
            EmitCliUsageError();
        }

        private void EmitCommandParser(CheckedCommand command)
        {
            var inputs = command.Inputs.OrderBy(input => input.FieldIndex).ToArray();
            _source.Append("    private static int RunCommand_")
                .Append(command.Id.ToString(CultureInfo.InvariantCulture)).AppendLine("(string[] args)");
            _source.AppendLine("    {");
            foreach (var input in inputs)
            {
                var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
                if (input.Kind == CheckedCommandInputKind.Flag)
                {
                    _source.Append("        var input_").Append(index).AppendLine(" = false;");
                    _source.Append("        var seen_").Append(index).AppendLine(" = false;");
                }
                else
                {
                    _source.Append("        string? raw_").Append(index).AppendLine(" = null;");
                    _source.Append("        var seen_").Append(index).AppendLine(" = false;");
                }
            }
            _source.AppendLine("        var positionalIndex = 0;");
            _source.AppendLine("        var positionalOnly = false;");
            _source.AppendLine("        for (var argumentIndex = 0; argumentIndex < args.Length; argumentIndex++)");
            _source.AppendLine("        {");
            _source.AppendLine("            var token = args[argumentIndex];");
            _source.AppendLine("            if (!positionalOnly && token == \"--\")");
            _source.AppendLine("            {");
            _source.AppendLine("                positionalOnly = true;");
            _source.AppendLine("                continue;");
            _source.AppendLine("            }");
            _source.AppendLine("            if (!positionalOnly && token.StartsWith(\"--\", StringComparison.Ordinal))");
            _source.AppendLine("            {");
            _source.AppendLine("                var equalsIndex = token.IndexOf('=');");
            _source.AppendLine("                var optionName = equalsIndex < 0 ? token : token[..equalsIndex];");
            _source.AppendLine("                var hasInlineValue = equalsIndex >= 0;");
            _source.AppendLine("                var inlineValue = hasInlineValue ? token[(equalsIndex + 1)..] : string.Empty;");
            _source.AppendLine("                switch (optionName)");
            _source.AppendLine("                {");
            foreach (var input in inputs.Where(input => input.Kind is CheckedCommandInputKind.Option or CheckedCommandInputKind.Flag))
            {
                var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
                var optionName = JsonSerializer.Serialize("--" + input.Name);
                _source.Append("                    case ").Append(optionName).AppendLine(":");
                _source.Append("                        if (seen_").Append(index).AppendLine(")");
                _source.AppendLine("                            return CliUsageError(\"CLI_DUPLICATE_OPTION\", optionName);");
                _source.Append("                        seen_").Append(index).AppendLine(" = true;");
                if (input.Kind == CheckedCommandInputKind.Flag)
                {
                    _source.AppendLine("                        if (hasInlineValue)");
                    _source.AppendLine("                            return CliUsageError(\"CLI_INVALID_VALUE\", optionName);");
                    _source.Append("                        input_").Append(index).AppendLine(" = true;");
                }
                else
                {
                    _source.AppendLine("                        if (hasInlineValue)");
                    _source.AppendLine("                        {");
                    _source.Append("                            raw_").Append(index).AppendLine(" = inlineValue;");
                    _source.AppendLine("                        }");
                    _source.AppendLine("                        else");
                    _source.AppendLine("                        {");
                    _source.AppendLine("                            if (argumentIndex + 1 >= args.Length || args[argumentIndex + 1].StartsWith(\"--\", StringComparison.Ordinal))");
                    _source.AppendLine("                                return CliUsageError(\"CLI_MISSING_VALUE\", optionName);");
                    _source.Append("                            raw_").Append(index).AppendLine(" = args[++argumentIndex];");
                    _source.AppendLine("                        }");
                }
                _source.AppendLine("                        break;");
            }
            _source.AppendLine("                    default:");
            _source.AppendLine("                        return CliUsageError(\"CLI_UNKNOWN_OPTION\", optionName);");
            _source.AppendLine("                }");
            _source.AppendLine("                continue;");
            _source.AppendLine("            }");
            _source.AppendLine("            switch (positionalIndex++)");
            _source.AppendLine("            {");
            var argumentOrdinal = 0;
            foreach (var input in inputs.Where(input => input.Kind == CheckedCommandInputKind.Argument))
            {
                var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
                _source.Append("                case ").Append(argumentOrdinal++.ToString(CultureInfo.InvariantCulture)).AppendLine(":");
                _source.Append("                    raw_").Append(index).AppendLine(" = token;");
                _source.Append("                    seen_").Append(index).AppendLine(" = true;");
                _source.AppendLine("                    break;");
            }
            _source.AppendLine("                default:");
            _source.AppendLine("                    return CliUsageError(\"CLI_INVALID_VALUE\", token);");
            _source.AppendLine("            }");
            _source.AppendLine("        }");

            foreach (var input in inputs)
            {
                var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
                if (input.Kind == CheckedCommandInputKind.Argument)
                {
                    _source.Append("        if (!seen_").Append(index).AppendLine(")");
                    _source.Append("            return CliUsageError(\"CLI_MISSING_ARGUMENT\", ")
                        .Append(JsonSerializer.Serialize(input.Name)).AppendLine(");");
                }
            }

            foreach (var input in inputs.Where(input => input.Kind == CheckedCommandInputKind.Option))
            {
                var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
                _source.Append("        if (!seen_").Append(index).AppendLine(")");
                _source.AppendLine("        {");
                _source.Append("            raw_").Append(index).Append(" = ")
                    .Append(JsonSerializer.Serialize(CommandDefaultString(input))).AppendLine(";");
                _source.AppendLine("        }");
            }

            foreach (var input in inputs.Where(input => input.Kind != CheckedCommandInputKind.Flag))
                EmitCommandInputConversion(input);

            var fields = string.Join(", ", inputs.Select(input =>
                "Field_" + input.FieldIndex.ToString(CultureInfo.InvariantCulture) + ": input_" +
                input.FieldIndex.ToString(CultureInfo.InvariantCulture)));
            var resultType = "Result<string, " + EmitType(command.ErrorType) + ">";
            _source.Append("        var result = Function_").Append(command.HandlerFunctionId.ToString(CultureInfo.InvariantCulture))
                .Append("(new Struct_").Append(command.ArgsStructId.ToString(CultureInfo.InvariantCulture))
                .Append('(').Append(fields).AppendLine("));");
            _source.Append("        if (result is ").Append(resultType).AppendLine(".Ok success)");
            _source.AppendLine("        {");
            _source.AppendLine("            Console.WriteLine(success.Value);");
            _source.AppendLine("            return 0;");
            _source.AppendLine("        }");
            _source.Append("        if (result is ").Append(resultType).AppendLine(".Err failure)");
            _source.AppendLine("        {");
            _source.Append("            var message = Function_").Append(command.ErrorFunctionId.ToString(CultureInfo.InvariantCulture))
                .AppendLine("(failure.Error);");
            _source.AppendLine("            Console.Error.WriteLine(message);");
            _source.AppendLine("            return 3;");
            _source.AppendLine("        }");
            _source.AppendLine("        throw new InvalidOperationException(\"Invalid command handler result\");");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitCommandInputConversion(CheckedCommandInput input)
        {
            var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
            switch (input.Type.Kind)
            {
                case LangTypeKind.Text:
                    _source.Append("        var input_").Append(index).Append(" = raw_").Append(index)
                        .AppendLine(" ?? string.Empty;");
                    break;
                case LangTypeKind.I32:
                    _source.Append("        if (!int.TryParse(raw_").Append(index)
                        .AppendLine(", NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed_" + index + "))");
                    _source.Append("            return CliUsageError(\"CLI_INVALID_VALUE\", ")
                        .Append(JsonSerializer.Serialize(input.Name)).AppendLine(");");
                    _source.Append("        var input_").Append(index).Append(" = parsed_").Append(index).AppendLine(";");
                    break;
                case LangTypeKind.FilePath:
                    _source.Append("        var value_").Append(index).Append(" = raw_").Append(index).AppendLine(" ?? string.Empty;");
                    _source.Append("        if (value_").Append(index).Append(".Length == 0 || value_").Append(index)
                        .AppendLine(".IndexOf('\\0') >= 0)");
                    _source.Append("            return CliUsageError(\"CLI_INVALID_VALUE\", ")
                        .Append(JsonSerializer.Serialize(input.Name)).AppendLine(");");
                    _source.Append("        var input_").Append(index).Append(" = new FilePath(value_").Append(index).AppendLine(");");
                    break;
                default:
                    throw new InvalidOperationException("Unsupported checked command input type");
            }
        }

        private static string CommandDefaultString(CheckedCommandInput input)
        {
            if (input.Default is null)
                throw new InvalidOperationException("Checked command option is missing its default");
            return input.Default.Kind switch
            {
                CheckedCommandLiteralKind.Text => input.Default.TextValue ?? string.Empty,
                CheckedCommandLiteralKind.I32 => input.Default.IntegerValue!.Value.ToString(CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException("Unsupported checked command default")
            };
        }

        private string TopLevelHelp()
        {
            var lines = new List<string> { "Usage: <program> <command> [arguments]", "Commands:" };
            foreach (var command in program.Commands.OrderBy(command => command.Id))
                lines.Add("  " + command.Name + "  " + command.Help);
            lines.Add("  --help  Show this help.");
            return string.Join("\n", lines);
        }

        private static string CommandHelp(CheckedCommand command)
        {
            var lines = new List<string> { "Usage: " + command.Name + " <arguments>", command.Help };
            var arguments = command.Inputs.Where(input => input.Kind == CheckedCommandInputKind.Argument)
                .OrderBy(input => input.FieldIndex).ToArray();
            if (arguments.Length != 0)
            {
                lines.Add("Arguments:");
                lines.AddRange(arguments.Select(input => "  " + input.Name + "  " + input.Help));
            }

            var options = command.Inputs.Where(input => input.Kind == CheckedCommandInputKind.Option)
                .OrderBy(input => input.FieldIndex).ToArray();
            if (options.Length != 0)
            {
                lines.Add("Options:");
                lines.AddRange(options.Select(input => "  --" + input.Name + " <" + CommandTypeName(input.Type) + ">  " + input.Help));
            }

            var flags = command.Inputs.Where(input => input.Kind == CheckedCommandInputKind.Flag)
                .OrderBy(input => input.FieldIndex).ToArray();
            if (flags.Length != 0)
            {
                lines.Add("Flags:");
                lines.AddRange(flags.Select(input => "  --" + input.Name + "  " + input.Help));
            }

            lines.Add("  --help  Show this help.");
            return string.Join("\n", lines);
        }

        private void EmitCliUsageError()
        {
            _source.AppendLine("    private static int CliUsageError(string code, string subject)");
            _source.AppendLine("    {");
            _source.AppendLine("""        Console.Error.WriteLine(code + ": \"" + EscapeCliSubject(subject) + "\"");""");
            _source.AppendLine("        return 2;");
            _source.AppendLine("    }");
            _source.AppendLine();
            _source.AppendLine("    private static string EscapeCliSubject(string value)");
            _source.AppendLine("    {");
            _source.AppendLine("        var escaped = new StringBuilder(value.Length);");
            _source.AppendLine("        foreach (var character in value)");
            _source.AppendLine("        {");
            _source.AppendLine("            switch (character)");
            _source.AppendLine("            {");
            _source.AppendLine("""                case '\\': escaped.Append("\\\\"); break;""");
            _source.AppendLine("""                case '"': escaped.Append("\\\""); break;""");
            _source.AppendLine("                case '\\n': escaped.Append(\"\\\\n\"); break;");
            _source.AppendLine("                case '\\r': escaped.Append(\"\\\\r\"); break;");
            _source.AppendLine("                case '\\t': escaped.Append(\"\\\\t\"); break;");
            _source.AppendLine("                case '\\0': escaped.Append(\"\\\\0\"); break;");
            _source.AppendLine("                default:");
            _source.AppendLine("                    var category = char.GetUnicodeCategory(character);");
            _source.AppendLine("                    if (char.IsControl(character) || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)");
            _source.AppendLine("                        escaped.Append(\"\\\\u\").Append(((int)character).ToString(\"X4\", CultureInfo.InvariantCulture));");
            _source.AppendLine("                    else");
            _source.AppendLine("                        escaped.Append(character);");
            _source.AppendLine("                    break;");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine("        return escaped.ToString();");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitTestEntryPoint(IReadOnlyList<CheckedTest> tests)
        {
            _source.AppendLine("    public static int Main()");
            _source.AppendLine("    {");
            _source.AppendLine("        Console.OutputEncoding = new System.Text.UTF8Encoding(false);");
            _source.AppendLine("        var passed = 0;");
            _source.AppendLine("        var failed = 0;");

            foreach (var test in tests)
            {
                var functionName = "Function_" + test.FunctionId.ToString(CultureInfo.InvariantCulture);
                var module = EscapeDisplayField(test.Module);
                var name = EscapeDisplayField(test.Name);
                var file = EscapeDisplayField(test.At.File);
                var passLabel = JsonSerializer.Serialize("PASS " + module + " :: " + name);
                var failureLabel = JsonSerializer.Serialize(
                    "FAIL " + module + " :: " + name + " (" +
                    file + ":" +
                    test.At.Line.ToString(CultureInfo.InvariantCulture) + ":" +
                    test.At.Column.ToString(CultureInfo.InvariantCulture) + ")");

                _source.AppendLine("        try");
                _source.AppendLine("        {");
                _source.Append("            if (").Append(functionName).AppendLine("())");
                _source.AppendLine("            {");
                _source.AppendLine("                passed++;");
                _source.Append("                Console.WriteLine(").Append(passLabel).AppendLine(");");
                _source.AppendLine("            }");
                _source.AppendLine("            else");
                _source.AppendLine("            {");
                _source.AppendLine("                failed++;");
                _source.Append("                Console.WriteLine(").Append(failureLabel).AppendLine(");");
                _source.AppendLine("            }");
                _source.AppendLine("        }");
                _source.AppendLine("        catch (Exception)");
                _source.AppendLine("        {");
                _source.AppendLine("            failed++;");
                _source.Append("            Console.WriteLine(").Append(failureLabel)
                    .AppendLine(" + \" Runtime fault\");");
                _source.AppendLine("        }");
            }

            _source.AppendLine(
                "        Console.WriteLine(passed.ToString(CultureInfo.InvariantCulture) + " +
                "\" passed, \" + failed.ToString(CultureInfo.InvariantCulture) + \" failed\");");
            _source.AppendLine("        return failed == 0 ? 0 : 1;");
            _source.AppendLine("    }");
        }

        private static string EscapeDisplayField(string value)
        {
            var escaped = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    case '\0':
                        escaped.Append("\\0");
                        break;
                    default:
                        var category = char.GetUnicodeCategory(character);
                        if (char.IsControl(character) ||
                            category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                        {
                            escaped.Append("\\u")
                                .Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            escaped.Append(character);
                        }
                        break;
                }
            }

            return escaped.ToString();
        }

        private string EmitType(LangType type) => type.Kind switch
        {
            LangTypeKind.I32 => "int",
            LangTypeKind.Bool => "bool",
            LangTypeKind.Text => "string",
            LangTypeKind.FilePath => "FilePath",
            LangTypeKind.Union => "Union_" + type.UnionId.ToString(CultureInfo.InvariantCulture),
            LangTypeKind.Struct => "Struct_" + type.StructId.ToString(CultureInfo.InvariantCulture),
            LangTypeKind.Option => "Option<" + EmitType(type.Arguments[0]) + ">",
            LangTypeKind.Result => "Result<" + EmitType(type.Arguments[0]) + ", " + EmitType(type.Arguments[1]) + ">",
            LangTypeKind.TypeParameter => EmitTypeParameter(type),
            LangTypeKind.FsRead => "FsRead",
            LangTypeKind.FsError => "FsError",
            _ => throw new InvalidOperationException("Error type reached emitter")
        };

        private string EmitTypeParameter(LangType type)
        {
            var ordinal = type.TypeParameterOrdinal;
            if (_emittingFunction is null ||
                type.TypeParameterOwnerId != _emittingFunction.Id ||
                ordinal < 0 || ordinal >= _emittingFunction.TypeParameters.Count)
                throw new InvalidOperationException("Type parameter reached emitter outside its defining function");
            return TypeParameterName(ordinal);
        }

        private static string TypeParameterName(int ordinal) =>
            "T" + ordinal.ToString(CultureInfo.InvariantCulture);

        private bool NeedsFilePathType => UsesTypeKind(LangTypeKind.FilePath);

        private bool NeedsFsReadType => UsesTypeKind(LangTypeKind.FsRead) || UsesFsReadText;

        private bool NeedsFsErrorType => UsesTypeKind(LangTypeKind.FsError) || UsesFsReadText;

        private bool UsesFsReadText => EmittedFunctions.Any(function =>
            function.InferredEffects.Contains("fs.read", StringComparer.Ordinal) ||
            EnumerateStatements(function.Body)
                .SelectMany(StatementExpressions)
                .SelectMany(EnumerateExpressions)
                .OfType<TypedIntrinsicCallExpr>()
                .Any(intrinsic => intrinsic.Intrinsic == BuiltinIntrinsic.FsReadText));

        private bool UsesTextLength => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .Any(expression => expression is TypedTextLengthExpr);

        private bool UsesTypeKind(LangTypeKind kind) => EnumerateDeclaredTypes()
            .Any(type => ContainsTypeKind(type, kind));

        private IEnumerable<LangType> EnumerateDeclaredTypes()
        {
            foreach (var union in program.Unions)
            foreach (var variant in union.Variants)
            foreach (var field in variant.Fields)
                yield return field.Type;

            foreach (var structure in program.Structs)
            foreach (var field in structure.Fields)
                yield return field.Type;

            foreach (var function in EmittedFunctions)
            {
                foreach (var parameter in function.Parameters)
                    yield return parameter.Type;
                yield return function.ReturnType;

                foreach (var statement in EnumerateStatements(function.Body))
                    if (statement is TypedLetStmt let)
                        yield return let.Type;

                foreach (var statement in EnumerateStatements(function.Body))
                foreach (var expression in StatementExpressions(statement).SelectMany(EnumerateExpressions))
                {
                    yield return expression.Type;
                    if (expression is TypedCallExpr call)
                    foreach (var typeArgument in call.TypeArguments)
                        yield return typeArgument;
                }
            }
        }

        private static IEnumerable<TypedStmt> EnumerateStatements(IEnumerable<TypedStmt> statements)
        {
            foreach (var statement in statements)
            {
                yield return statement;
                if (statement is not TypedIfStmt conditional) continue;
                foreach (var nested in EnumerateStatements(conditional.ThenBody)) yield return nested;
                if (conditional.ElseBody is not null)
                    foreach (var nested in EnumerateStatements(conditional.ElseBody)) yield return nested;
            }
        }

        private static IEnumerable<TypedExpr> StatementExpressions(TypedStmt statement)
        {
            switch (statement)
            {
                case TypedLetStmt let:
                    yield return let.Value;
                    break;
                case TypedReturnStmt ret:
                    yield return ret.Value;
                    break;
                case TypedIfStmt conditional:
                    yield return conditional.Condition;
                    break;
            }
        }

        private static IEnumerable<TypedExpr> EnumerateExpressions(TypedExpr expression)
        {
            yield return expression;
            switch (expression)
            {
                case TypedBinaryExpr binary:
                    foreach (var nested in EnumerateExpressions(binary.Left)) yield return nested;
                    foreach (var nested in EnumerateExpressions(binary.Right)) yield return nested;
                    break;
                case TypedCompareExpr comparison:
                    foreach (var nested in EnumerateExpressions(comparison.Left)) yield return nested;
                    foreach (var nested in EnumerateExpressions(comparison.Right)) yield return nested;
                    break;
                case TypedCallExpr call:
                    foreach (var argument in call.Arguments)
                    foreach (var nested in EnumerateExpressions(argument)) yield return nested;
                    break;
                case TypedTextLengthExpr length:
                    foreach (var nested in EnumerateExpressions(length.Target)) yield return nested;
                    break;
                case TypedTextTrimExpr trim:
                    foreach (var nested in EnumerateExpressions(trim.Target)) yield return nested;
                    break;
                case TypedIntrinsicCallExpr intrinsic:
                    foreach (var argument in intrinsic.Arguments)
                    foreach (var nested in EnumerateExpressions(argument)) yield return nested;
                    break;
                case TypedBuiltinConstructExpr builtin:
                    foreach (var argument in builtin.Arguments)
                    foreach (var nested in EnumerateExpressions(argument)) yield return nested;
                    break;
                case TypedUnionConstructExpr variant:
                    foreach (var argument in variant.Arguments)
                    foreach (var nested in EnumerateExpressions(argument)) yield return nested;
                    break;
                case TypedStructConstructExpr structure:
                    foreach (var field in structure.Fields)
                    foreach (var nested in EnumerateExpressions(field.Value)) yield return nested;
                    break;
                case TypedFieldAccessExpr field:
                    foreach (var nested in EnumerateExpressions(field.Target)) yield return nested;
                    break;
                case TypedMatchExpr match:
                    foreach (var nested in EnumerateExpressions(match.Value)) yield return nested;
                    foreach (var arm in match.Arms)
                    foreach (var nested in EnumerateExpressions(arm.Body)) yield return nested;
                    break;
            }
        }

        private static bool ContainsTypeKind(LangType type, LangTypeKind kind) =>
            type.Kind == kind || type.Arguments.Any(argument => ContainsTypeKind(argument, kind));

        private void Indent(int level) => _source.Append(' ', level * 4);
    }
}
