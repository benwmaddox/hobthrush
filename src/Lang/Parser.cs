using System.Globalization;
using System.Text;

internal static class Lexer
{
    public static List<Token> Scan(string source, string file, List<Diagnostic> diagnostics)
    {
        var tokens = new List<Token>();
        var i = 0;
        var line = 1;
        var column = 1;

        while (i < source.Length)
        {
            var c = source[i];
            if (c == '\r')
            {
                i++;
                if (i >= source.Length || source[i] != '\n')
                {
                    line++;
                    column = 1;
                }
                continue;
            }
            if (c == '\n')
            {
                i++;
                line++;
                column = 1;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                i++;
                column++;
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] is not ('\r' or '\n'))
                {
                    i++;
                    column++;
                }
                continue;
            }

            var start = i;
            var startLine = line;
            var startColumn = column;

            if (char.IsLetter(c) || c == '_')
            {
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                    column++;
                }
                tokens.Add(new Token("id", source[start..i], startLine, startColumn));
                continue;
            }

            if (char.IsDigit(c))
            {
                while (i < source.Length && char.IsDigit(source[i]))
                {
                    i++;
                    column++;
                }
                tokens.Add(new Token("number", source[start..i], startLine, startColumn));
                continue;
            }

            if (c == '"')
            {
                ScanTextLiteral(source, file, diagnostics, tokens, ref i, ref line, ref column);
                continue;
            }

            if (i + 1 < source.Length && source.AsSpan(i, 2).SequenceEqual("->"))
            {
                tokens.Add(new Token("->", "->", line, column));
                i += 2;
                column += 2;
                continue;
            }
            if (i + 1 < source.Length && source.AsSpan(i, 2).SequenceEqual("=>"))
            {
                tokens.Add(new Token("=>", "=>", line, column));
                i += 2;
                column += 2;
                continue;
            }

            if (".;:,(){}=+*-<>".IndexOf(c) >= 0)
            {
                tokens.Add(new Token(c.ToString(), c.ToString(), line, column));
                i++;
                column++;
                continue;
            }

            diagnostics.Add(new Diagnostic(
                "E_UNSUPPORTED",
                $"Character '{c}' is not in the implemented language slice",
                file,
                new Range(line, column, line, column + 1)));
            i++;
            column++;
        }

        tokens.Add(new Token("eof", "", line, column));
        return tokens;
    }

    private static void ScanTextLiteral(
        string source,
        string file,
        List<Diagnostic> diagnostics,
        List<Token> tokens,
        ref int i,
        ref int line,
        ref int column)
    {
        var start = i;
        var startLine = line;
        var startColumn = column;
        i++;
        column++;
        var closed = false;
        var hitRawNewline = false;

        while (i < source.Length)
        {
            var c = source[i];
            if (c is '\r' or '\n')
            {
                diagnostics.Add(new Diagnostic(
                    "E_SYNTAX",
                    "Text literals cannot contain an unescaped newline",
                    file,
                    new Range(startLine, startColumn, line, column)));
                hitRawNewline = true;
                break;
            }
            if (c == '"')
            {
                i++;
                column++;
                closed = true;
                break;
            }
            if (c == '\\')
            {
                var escapeLine = line;
                var escapeColumn = column;
                i++;
                column++;
                if (i >= source.Length)
                {
                    diagnostics.Add(new Diagnostic(
                        "E_SYNTAX",
                        "Text literal ends with an incomplete escape sequence",
                        file,
                        new Range(escapeLine, escapeColumn, line, column)));
                    break;
                }

                var escaped = source[i];
                if (escaped is not ('"' or '\\' or 'n' or 'r' or 't' or '0'))
                {
                    diagnostics.Add(new Diagnostic(
                        "E_SYNTAX",
                        $"Escape sequence '\\{escaped}' is not supported",
                        file,
                        new Range(escapeLine, escapeColumn, line, column + 1)));
                }
                i++;
                column++;
                continue;
            }

            i++;
            column++;
        }

        if (!closed && !hitRawNewline && i >= source.Length)
        {
            diagnostics.Add(new Diagnostic(
                "E_SYNTAX",
                "Unterminated text literal",
                file,
                new Range(startLine, startColumn, line, column)));
        }

        tokens.Add(new Token("text", source[start..i], startLine, startColumn));
    }
}

internal sealed class Parser
{
    private const int MaximumNestingDepth = 256;

    private static readonly HashSet<string> BareExpressionKeywords = new(StringComparer.Ordinal)
    {
        "await", "false", "if", "match", "null", "true", "with"
    };

    private readonly IReadOnlyList<Token> _tokens;
    private readonly string _file;
    private readonly List<Diagnostic> _diagnostics;
    private readonly Token _emptyEof = new("eof", "", 1, 1);
    private int _position;
    private int _nestingDepth;
    private readonly Dictionary<Expr, int> _expressionDepth = new(ReferenceEqualityComparer.Instance);

    public Parser(List<Token> tokens, string file, List<Diagnostic> diagnostics)
    {
        _tokens = tokens;
        _file = file;
        _diagnostics = diagnostics;
    }

    private Token Current => _tokens.Count == 0
        ? _emptyEof
        : _tokens[Math.Clamp(_position, 0, _tokens.Count - 1)];

    private Token LookAhead(int offset = 1)
    {
        if (_tokens.Count == 0) return _emptyEof;
        return _tokens[Math.Clamp(_position + offset, 0, _tokens.Count - 1)];
    }

    private Token Take()
    {
        var token = Current;
        if (token.Kind != "eof" && _position < _tokens.Count) _position++;
        return token;
    }

    private bool Is(string text) => Current.Text == text;
    private static bool IsLexicalIdentifier(Token token) => token.Kind == "id";
    private static bool IsBareIdentifier(Token token) =>
        IsLexicalIdentifier(token) && !BareExpressionKeywords.Contains(token.Text);

    private Token Expect(string text)
    {
        if (Is(text)) return Take();
        Fail(Current, "E_SYNTAX", $"Expected '{text}', found '{Current.Text}'");
        throw new ParseFailure();
    }

    private Token ExpectBareIdentifier()
    {
        if (IsBareIdentifier(Current)) return Take();
        Fail(Current, "E_SYNTAX", "Expected identifier");
        throw new ParseFailure();
    }

    private Token ExpectMemberIdentifier()
    {
        if (IsLexicalIdentifier(Current)) return Take();
        Fail(Current, "E_SYNTAX", "Expected identifier");
        throw new ParseFailure();
    }

    private Token ExpectModuleSegment()
    {
        if (IsLexicalIdentifier(Current)) return Take();
        Fail(Current, "E_SYNTAX", "Expected identifier");
        throw new ParseFailure();
    }

    private void Fail(Token token, string code, string message)
    {
        _diagnostics.Add(new Diagnostic(code, message, _file, token.Range));
        throw new ParseFailure();
    }

    public ParsedProgram? Parse()
    {
        try
        {
            Expect("module");
            var module = ParseQualifiedName();
            Expect(";");

            var unions = new List<UnionDecl>();
            var functions = new List<FunctionDecl>();
            var structs = new List<StructDecl>();
            while (Current.Kind != "eof")
            {
                var isPublic = false;
                if (Is("pub"))
                {
                    Take();
                    isPublic = true;
                }

                if (Is("fn"))
                {
                    functions.Add(ParseFunction(isPublic));
                }
                else if (Is("union"))
                {
                    unions.Add(ParseUnion(isPublic));
                }
                else if (Is("struct"))
                {
                    structs.Add(ParseStruct(isPublic));
                }
                else
                {
                    var declaration = Current;
                    if (isPublic && Current.Kind == "eof")
                        Fail(Current, "E_SYNTAX", "Expected declaration after 'pub'");
                    Fail(declaration, "E_UNSUPPORTED", $"Declaration '{declaration.Text}' is not implemented yet");
                }
            }

            return new ParsedProgram(module, unions, functions, structs);
        }
        catch (ParseFailure)
        {
            return null;
        }
    }

    private string ParseQualifiedName()
    {
        var name = ExpectModuleSegment().Text;
        while (Is("."))
        {
            Take();
            name += "." + ExpectModuleSegment().Text;
        }
        return name;
    }

    private FunctionDecl ParseFunction(bool isPublic)
    {
        if (Is("async"))
            Fail(Current, "E_UNSUPPORTED", "Async functions are not implemented yet");
        Expect("fn");
        var name = ExpectBareIdentifier();
        if (Is("<")) Fail(Current, "E_UNSUPPORTED", "Generic functions are not implemented yet");

        Expect("(");
        var parameters = new List<ParameterDecl>();
        while (!Is(")"))
        {
            if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed parameter list");
            var parameter = ExpectBareIdentifier();
            Expect(":");
            var type = ParseType();
            parameters.Add(new ParameterDecl(parameter.Text, type, parameter));
            if (Is(","))
            {
                Take();
                if (Is(")")) break;
            }
            else if (!Is(")"))
            {
                Expect(",");
            }
        }
        Expect(")");
        Expect("->");
        var returnType = ParseType();
        Expect("effects");
        ParseEffects();
        Expect("{");

        var body = new List<Stmt>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed function body");
            if (Is("let"))
            {
                var at = Take();
                var local = ExpectBareIdentifier();
                Expect(":");
                var type = ParseType();
                Expect("=");
                var value = ParseExpr();
                Expect(";");
                body.Add(new LetStmt(at, local.Text, type, value));
            }
            else if (Is("return"))
            {
                var at = Take();
                var value = ParseExpr();
                Expect(";");
                body.Add(new ReturnStmt(at, value));
            }
            else if (Is("var") || Is("if") || Is("with") || Is("await"))
            {
                Fail(Current, "E_UNSUPPORTED", $"Statement '{Current.Text}' is not implemented yet");
            }
            else
            {
                Fail(Current, "E_UNSUPPORTED", $"Statement '{Current.Text}' is not implemented yet");
            }
        }

        Expect("}");
        return new FunctionDecl(name.Text, isPublic, parameters, returnType, body, name);
    }

    private void ParseEffects()
    {
        Expect("{");
        if (Is("}"))
        {
            Take();
            return;
        }

        var firstEffect = Current;
        while (!Is("}") && Current.Kind != "eof")
        {
            ParseQualifiedName();
            if (Is("}")) break;
            Expect(",");
        }
        if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed effects list");
        Fail(firstEffect, "E_UNSUPPORTED", "Nonempty effects are not implemented yet");
    }

    private UnionDecl ParseUnion(bool isPublic)
    {
        Expect("union");
        var name = ExpectBareIdentifier();
        if (Is("<")) Fail(Current, "E_UNSUPPORTED", "Generic unions are not implemented yet");
        Expect("{");

        var variants = new List<VariantDecl>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed union declaration");
            var variantAt = ExpectMemberIdentifier();
            var fields = new List<VariantFieldDecl>();
            if (Is("("))
            {
                Take();
                bool? namedFields = null;
                while (!Is(")"))
                {
                    if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed union variant payload");

                    string? fieldName;
                    TypeSyntax fieldType;
                    Token fieldAt;
                    if (IsLexicalIdentifier(Current) && LookAhead().Text == ":")
                    {
                        fieldAt = Take();
                        fieldName = fieldAt.Text;
                        Expect(":");
                        fieldType = ParseType();
                        if (namedFields == false)
                            Fail(fieldAt, "E_SYNTAX", "A variant cannot mix named and positional payload fields");
                        namedFields = true;
                    }
                    else
                    {
                        fieldType = ParseType();
                        fieldAt = fieldType.At;
                        fieldName = null;
                        if (namedFields == true)
                            Fail(fieldAt, "E_SYNTAX", "A variant cannot mix named and positional payload fields");
                        namedFields = false;
                    }
                    fields.Add(new VariantFieldDecl(fieldName, fieldType, fieldAt));

                    if (Is(","))
                    {
                        Take();
                        if (Is(")")) break;
                    }
                    else if (!Is(")"))
                    {
                        Expect(",");
                    }
                }
                Expect(")");
            }

            variants.Add(new VariantDecl(variantAt.Text, fields, variantAt));
            if (Is(","))
            {
                Take();
                if (Is("}")) break;
            }
            else if (!Is("}"))
            {
                Expect(",");
            }
        }

        Expect("}");
        return new UnionDecl(name.Text, isPublic, variants, name);
    }

    private StructDecl ParseStruct(bool isPublic)
    {
        Expect("struct");
        var name = ExpectBareIdentifier();
        if (Is("<")) Fail(Current, "E_UNSUPPORTED", "Generic structs are not implemented yet");
        Expect("{");

        var fields = new List<StructFieldDecl>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed struct declaration");
            var fieldAt = ExpectMemberIdentifier();
            Expect(":");
            var fieldType = ParseType();
            fields.Add(new StructFieldDecl(fieldAt.Text, fieldType, fieldAt));
            if (Is(","))
            {
                Take();
                if (Is("}")) break;
            }
            else if (!Is("}"))
            {
                Expect(",");
            }
        }

        Expect("}");
        return new StructDecl(name.Text, isPublic, fields, name);
    }

    private TypeSyntax ParseType()
    {
        EnterNesting(Current, "Type nesting is too deep");
        try
        {
            var at = ExpectBareIdentifier();
            var name = at.Text;
            while (Is("."))
            {
                Take();
                name += "." + ExpectMemberIdentifier().Text;
            }

            var arguments = new List<TypeSyntax>();
            if (Is("<"))
            {
                Take();
                if (Is(">")) Fail(Current, "E_SYNTAX", "Expected type argument");
                while (true)
                {
                    arguments.Add(ParseType());
                    if (Is(","))
                    {
                        Take();
                        continue;
                    }
                    break;
                }
                Expect(">");
            }

            return new TypeSyntax(name, arguments, at);
        }
        finally
        {
            _nestingDepth--;
        }
    }

    private Expr ParseExpr(int minPrecedence = 0, bool allowStructConstruction = true)
    {
        EnterNesting(Current, "Expression nesting is too deep");
        try
        {
            var left = ParsePrimary(allowStructConstruction);
            var leftDepth = ExpressionDepth(left);
            while (true)
            {
                var precedence = Current.Text switch
                {
                    "*" => 2,
                    "+" or "-" => 1,
                    _ => 0
                };
                if (precedence == 0 || precedence < minPrecedence) break;

                var op = Take();
                var right = ParseExpr(precedence + 1, allowStructConstruction);
                var depth = Math.Max(leftDepth, ExpressionDepth(right)) + 1;
                if (depth > MaximumNestingDepth)
                    Fail(op, "E_SYNTAX", "Expression nesting is too deep");
                left = RegisterExpression(new BinaryExpr(op, op.Text, left, right), depth);
                leftDepth = depth;
            }
            return left;
        }
        finally
        {
            _nestingDepth--;
        }
    }

    private Expr ParsePrimary(bool allowStructConstruction)
    {
        var token = Current;
        if (token.Kind == "number")
        {
            Take();
            if (!int.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                Fail(token, "E_TYPE_MISMATCH", "Integer literal is outside i32 range");
            return ParsePostfix(RegisterExpression(new NumberExpr(token, value)));
        }
        if (Is("-"))
        {
            var minus = Take();
            if (Current.Kind == "number")
            {
                var magnitudeToken = Take();
                if (!uint.TryParse(magnitudeToken.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var magnitude)
                    || magnitude > 2147483648u)
                {
                    Fail(magnitudeToken, "E_TYPE_MISMATCH", "Integer literal is outside i32 range");
                }
                var value = magnitude == 2147483648u ? int.MinValue : -(int)magnitude;
                return ParsePostfix(RegisterExpression(new NumberExpr(minus, value)));
            }

            var operand = ParseExpr(3, allowStructConstruction);
            var zero = RegisterExpression(new NumberExpr(minus, 0));
            var depth = Math.Max(ExpressionDepth(zero), ExpressionDepth(operand)) + 1;
            if (depth > MaximumNestingDepth)
                Fail(minus, "E_SYNTAX", "Expression nesting is too deep");
            var negated = RegisterExpression(new BinaryExpr(minus, "-", zero, operand), depth);
            return ParsePostfix(negated);
        }
        if (token.Kind == "text")
        {
            Take();
            return ParsePostfix(RegisterExpression(new TextExpr(token, DecodeText(token))));
        }
        if (token.Kind == "id")
        {
            if (Is("true") || Is("false"))
            {
                Take();
                return ParsePostfix(RegisterExpression(new BoolExpr(token, token.Text == "true")));
            }
            if (Is("null"))
                Fail(token, "E_TYPE_MISMATCH", "The null literal is not supported; use Option<T>");
            if (Is("match"))
                return ParsePostfix(ParseMatch(Take()));
            if (Is("await") || Is("if") || Is("with"))
                Fail(token, "E_UNSUPPORTED", $"Expression '{token.Text}' is not implemented yet");
            if (!IsBareIdentifier(token))
                Fail(token, "E_SYNTAX", $"Keyword '{token.Text}' is not an expression");

            Take();
            Expr expression;
            if (allowStructConstruction && Is("{"))
            {
                expression = ParseStructConstruction(token);
            }
            else if (Is("("))
            {
                var arguments = ParseArguments();
                expression = RegisterExpression(new CallExpr(token, token.Text, arguments),
                    1 + arguments.Select(ExpressionDepth).DefaultIfEmpty(0).Max());
            }
            else
            {
                expression = RegisterExpression(new NameExpr(token, token.Text));
            }
            return ParsePostfix(expression);
        }
        if (Is("("))
        {
            Take();
            // Parentheses make a struct construction unambiguous as a match scrutinee.
            var expression = ParseExpr();
            Expect(")");
            return ParsePostfix(expression);
        }

        Fail(token, "E_SYNTAX", $"Expected expression, found '{token.Text}'");
        throw new ParseFailure();
    }

    private Expr ParseStructConstruction(Token typeName)
    {
        Expect("{");
        var fields = new List<StructFieldValue>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed struct construction");
            var fieldAt = ExpectMemberIdentifier();
            Expect(":");
            var value = ParseExpr();
            fields.Add(new StructFieldValue(fieldAt.Text, value, fieldAt));
            if (Is(","))
            {
                Take();
                if (Is("}")) break;
            }
            else if (!Is("}"))
            {
                Expect(",");
            }
        }
        Expect("}");
        var depth = 1 + fields.Select(field => ExpressionDepth(field.Value)).DefaultIfEmpty(0).Max();
        return RegisterExpression(new StructConstructExpr(typeName, typeName.Text, fields), depth);
    }

    private Expr ParsePostfix(Expr expression)
    {
        while (Is("."))
        {
            Take();
            var fieldAt = ExpectMemberIdentifier();
            if (Is("("))
            {
                if (expression is not NameExpr typeName)
                {
                    Fail(fieldAt, "E_UNSUPPORTED", "Method calls are not implemented yet");
                    throw new ParseFailure();
                }

                var arguments = ParseArguments();
                var depth = 1 + arguments.Select(ExpressionDepth).DefaultIfEmpty(0).Max();
                expression = RegisterExpression(
                    new VariantExpr(typeName.At, typeName.Name, fieldAt.Text, arguments), depth);
                continue;
            }

            var fieldDepth = ExpressionDepth(expression) + 1;
            if (fieldDepth > MaximumNestingDepth)
                Fail(fieldAt, "E_SYNTAX", "Expression nesting is too deep");
            expression = RegisterExpression(new FieldAccessExpr(fieldAt, expression, fieldAt.Text), fieldDepth);
        }

        return expression;
    }

    private int ExpressionDepth(Expr expression) =>
        _expressionDepth.TryGetValue(expression, out var depth) ? depth : 1;

    private Expr RegisterExpression(Expr expression, int depth = 1)
    {
        if (depth > MaximumNestingDepth)
            Fail(expression.At, "E_SYNTAX", "Expression nesting is too deep");
        _expressionDepth[expression] = depth;
        return expression;
    }

    private List<Expr> ParseArguments()
    {
        Expect("(");
        var arguments = new List<Expr>();
        while (!Is(")"))
        {
            if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed argument list");
            arguments.Add(ParseExpr());
            if (Is(","))
            {
                Take();
                if (Is(")")) break;
            }
            else if (!Is(")"))
            {
                Expect(",");
            }
        }
        Expect(")");
        return arguments;
    }

    private Expr ParseMatch(Token at)
    {
        // The following brace begins the match arms unless construction is explicitly grouped.
        var value = ParseExpr(allowStructConstruction: false);
        Expect("{");
        var arms = new List<MatchArm>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed match expression");
            var pattern = ParsePattern();
            Expect("=>");
            var body = ParseExpr();
            arms.Add(new MatchArm(pattern, body, pattern.At));
            if (Is(","))
            {
                Take();
                if (Is("}")) break;
            }
            else if (!Is("}"))
            {
                Expect(",");
            }
        }
        Expect("}");
        var depth = Math.Max(
            ExpressionDepth(value),
            arms.Select(arm => ExpressionDepth(arm.Body)).DefaultIfEmpty(0).Max()) + 1;
        return RegisterExpression(new MatchExpr(at, value, arms), depth);
    }

    private Pattern ParsePattern()
    {
        var at = Current;
        if (Is("_"))
        {
            Take();
            return new WildcardPattern(at);
        }
        if (Is("null"))
            Fail(at, "E_TYPE_MISMATCH", "The null literal is not supported; use Option<T>");
        if (!IsBareIdentifier(at))
        {
            Fail(at, "E_SYNTAX", "Expected match pattern");
        }

        Take();
        string? unionName = null;
        var variantName = at.Text;
        if (Is("."))
        {
            Take();
            unionName = variantName;
            variantName = ExpectMemberIdentifier().Text;
        }

        var bindings = new List<string>();
        if (Is("("))
        {
            Take();
            while (!Is(")"))
            {
                if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed pattern payload");
                bindings.Add(ExpectBareIdentifier().Text);
                if (Is(","))
                {
                    Take();
                    if (Is(")")) break;
                }
                else if (!Is(")"))
                {
                    Expect(",");
                }
            }
            Expect(")");
        }

        return new VariantPattern(at, unionName, variantName, bindings);
    }

    private string DecodeText(Token token)
    {
        var raw = token.Text;
        var end = raw.Length > 0 && raw[^1] == '"' ? raw.Length - 1 : raw.Length;
        var start = raw.Length > 0 && raw[0] == '"' ? 1 : 0;
        var decoded = new StringBuilder(Math.Max(end - start, 0));
        for (var i = start; i < end; i++)
        {
            var c = raw[i];
            if (c != '\\')
            {
                decoded.Append(c);
                continue;
            }

            if (++i >= end) Fail(token, "E_SYNTAX", "Text literal ends with an incomplete escape sequence");
            switch (raw[i])
            {
                case '"': decoded.Append('"'); break;
                case '\\': decoded.Append('\\'); break;
                case 'n': decoded.Append('\n'); break;
                case 'r': decoded.Append('\r'); break;
                case 't': decoded.Append('\t'); break;
                case '0': decoded.Append('\0'); break;
                default: Fail(token, "E_SYNTAX", $"Escape sequence '\\{raw[i]}' is not supported"); break;
            }
        }
        return decoded.ToString();
    }

    private void EnterNesting(Token at, string message)
    {
        if (_nestingDepth >= MaximumNestingDepth)
            Fail(at, "E_SYNTAX", message);
        _nestingDepth++;
    }
}

internal sealed class ParseFailure : Exception;
