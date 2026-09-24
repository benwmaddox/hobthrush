using System.Globalization;
using System.Text;
using System.Text.Json;

internal static class Emitter
{
    public static string Emit(CheckedProgram program, bool executable = true)
    {
        var entry = program.Functions.FirstOrDefault(function =>
            function.Name == "main" && function.Parameters.Count == 0 &&
            (function.ReturnType.IsI32 || function.ReturnType.IsBool || function.ReturnType.IsText));
        if (executable && entry is null)
            throw new InvalidOperationException("Executable emission requires a valid main function");

        var emitter = new SourceEmitter(program);
        return emitter.Emit(entry, executable);
    }

    private sealed class SourceEmitter(CheckedProgram program)
    {
        private readonly StringBuilder _source = new();

        public string Emit(CheckedFunction? entry, bool executable)
        {
            _source.AppendLine("using System;");
            _source.AppendLine("using System.Globalization;");
            _source.AppendLine();
            _source.AppendLine("public static class LangModule");
            _source.AppendLine("{");

            EmitBuiltinTypes();
            foreach (var union in program.Unions) EmitUnion(union);
            foreach (var function in program.Functions) EmitFunction(function);
            EmitArithmeticHelpers();
            if (executable) EmitEntryPoint(entry!);

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

        private void EmitUnion(CheckedUnion union)
        {
            var accessibility = union.Public ? "public" : "private";
            _source.Append("    ").Append(accessibility).Append(" abstract record Union_").Append(union.Id).AppendLine();
            _source.AppendLine("    {");
            foreach (var variant in union.Variants)
            {
                _source.Append("        ").Append(accessibility).Append(" sealed record Variant_")
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

        private void EmitFunction(CheckedFunction function)
        {
            _source.Append("    ").Append(function.Public ? "public" : "private").Append(" static ")
                .Append(EmitType(function.ReturnType)).Append(" Function_").Append(function.Id).Append('(');
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (i != 0) _source.Append(", ");
                var parameter = function.Parameters[i];
                _source.Append(EmitType(parameter.Type)).Append(" Local_").Append(parameter.LocalId);
            }
            _source.AppendLine(")");
            _source.AppendLine("    {");
            foreach (var statement in function.Body) EmitStatement(statement, 2);
            _source.AppendLine("    }");
            _source.AppendLine();
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
            TypedCallExpr call => "Function_" + call.FunctionId.ToString(CultureInfo.InvariantCulture) +
                "(" + string.Join(", ", call.Arguments.Select(EmitExpr)) + ")",
            TypedBuiltinConstructExpr builtin => EmitBuiltinConstruct(builtin),
            TypedUnionConstructExpr variant => EmitUnionConstruct(variant),
            TypedMatchExpr match => EmitMatch(match),
            _ => throw new InvalidOperationException("Unchecked expression reached emitter")
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

        private string EmitBuiltinConstruct(TypedBuiltinConstructExpr expression)
        {
            var variant = expression.Variant switch
            {
                BuiltinVariant.Some => "Some",
                BuiltinVariant.None => "None",
                BuiltinVariant.Ok => "Ok",
                BuiltinVariant.Err => "Err",
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

        private string EmitType(LangType type) => type.Kind switch
        {
            LangTypeKind.I32 => "int",
            LangTypeKind.Bool => "bool",
            LangTypeKind.Text => "string",
            LangTypeKind.Union => "Union_" + type.UnionId.ToString(CultureInfo.InvariantCulture),
            LangTypeKind.Option => "Option<" + EmitType(type.Arguments[0]) + ">",
            LangTypeKind.Result => "Result<" + EmitType(type.Arguments[0]) + ", " + EmitType(type.Arguments[1]) + ">",
            _ => throw new InvalidOperationException("Error type reached emitter")
        };

        private void Indent(int level) => _source.Append(' ', level * 4);
    }
}
