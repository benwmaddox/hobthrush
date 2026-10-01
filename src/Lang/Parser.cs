using System.Globalization;
using System.Numerics;
using System.Text;

internal static class Lexer
{
    private const int MaximumF64LiteralLength = 128;

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
                tokens.Add(new Token("id", source[start..i], startLine, startColumn, file));
                continue;
            }

            if (c == '.' && i + 1 < source.Length && IsAsciiDigit(source[i + 1]))
            {
                ScanNumericLiteral(source, file, diagnostics, tokens, ref i, ref column, line, leadingDot: true);
                continue;
            }

            if (char.IsDigit(c))
            {
                ScanNumericLiteral(source, file, diagnostics, tokens, ref i, ref column, line);
                continue;
            }

            if (c == '"')
            {
                ScanTextLiteral(source, file, diagnostics, tokens, ref i, ref line, ref column);
                continue;
            }

            if (i + 1 < source.Length && source.AsSpan(i, 2).SequenceEqual("::"))
            {
                tokens.Add(new Token("::", "::", line, column, file));
                i += 2;
                column += 2;
                continue;
            }
            if (i + 1 < source.Length && source.AsSpan(i, 2).SequenceEqual("->"))
            {
                tokens.Add(new Token("->", "->", line, column, file));
                i += 2;
                column += 2;
                continue;
            }
            if (i + 1 < source.Length && source.AsSpan(i, 2).SequenceEqual("=>"))
            {
                tokens.Add(new Token("=>", "=>", line, column, file));
                i += 2;
                column += 2;
                continue;
            }
            if (i + 1 < source.Length &&
                (source.AsSpan(i, 2).SequenceEqual("==") ||
                 source.AsSpan(i, 2).SequenceEqual("!=") ||
                 source.AsSpan(i, 2).SequenceEqual("<=") ||
                 source.AsSpan(i, 2).SequenceEqual(">=")))
            {
                var operation = source.Substring(i, 2);
                tokens.Add(new Token(operation, operation, line, column, file));
                i += 2;
                column += 2;
                continue;
            }

            if (".;:,(){}[]=+*/-<>".IndexOf(c) >= 0)
            {
                tokens.Add(new Token(c.ToString(), c.ToString(), line, column, file));
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

        tokens.Add(new Token("eof", "", line, column, file));
        return tokens;
    }

    private static void ScanNumericLiteral(
        string source,
        string file,
        List<Diagnostic> diagnostics,
        List<Token> tokens,
        ref int i,
        ref int column,
        int line,
        bool leadingDot = false)
    {
        var start = i;
        var startColumn = column;
        var malformed = leadingDot;
        var hasDecimalPoint = leadingDot;
        var hasExponent = false;
        var hasF64Suffix = false;
        var wrongF64Suffix = false;

        if (leadingDot)
        {
            i++;
            column++;
        }

        var integerDigitsStart = i;
        while (i < source.Length && char.IsDigit(source[i]))
        {
            i++;
            column++;
        }
        var integerDigitsEnd = i;

        if (!leadingDot && i < source.Length && source[i] == '.')
        {
            hasDecimalPoint = true;
            i++;
            column++;
        }

        if (hasDecimalPoint)
        {
            var fractionStart = i;
            while (i < source.Length && IsAsciiDigit(source[i]))
            {
                i++;
                column++;
            }
            if (i == fractionStart) malformed = true;
        }

        if (i < source.Length && source[i] is 'e' or 'E')
        {
            hasExponent = true;
            i++;
            column++;
            if (i < source.Length && source[i] is '+' or '-')
            {
                i++;
                column++;
            }

            var exponentStart = i;
            while (i < source.Length && IsAsciiDigit(source[i]))
            {
                i++;
                column++;
            }
            if (i == exponentStart) malformed = true;
        }

        if (HasNumericSuffix(source, i, "f64") && HasIdentifierBoundary(source, i + 3))
        {
            hasF64Suffix = true;
            i += 3;
            column += 3;
        }
        else if (!leadingDot && !hasDecimalPoint && !hasExponent &&
                 (HasNumericSuffix(source, i, "i64") || HasNumericSuffix(source, i, "u32") || HasNumericSuffix(source, i, "u64")) &&
                 HasIdentifierBoundary(source, i + 3))
        {
            i += 3;
            column += 3;
        }
        else if (i < source.Length && (source[i] is 'f' or 'F' ||
                                       (hasDecimalPoint || hasExponent) && char.IsLetterOrDigit(source[i])))
        {
            wrongF64Suffix = source[i] is 'f' or 'F' || hasDecimalPoint || hasExponent;
            malformed = true;
            while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
            {
                i++;
                column++;
            }
        }

        var f64Candidate = leadingDot || hasDecimalPoint || hasExponent || hasF64Suffix || wrongF64Suffix;
        var tokenLength = i - start;
        if (f64Candidate && tokenLength > MaximumF64LiteralLength)
        {
            diagnostics.Add(new Diagnostic(
                "E_NUMERIC_LITERAL_TOO_LONG",
                "f64 literal exceeds the 128-character limit",
                file,
                new Range(line, startColumn, line, startColumn + tokenLength)));
            return;
        }

        if (f64Candidate)
        {
            if (integerDigitsStart != integerDigitsEnd && !IsAsciiDigitSpan(source.AsSpan(integerDigitsStart, integerDigitsEnd - integerDigitsStart)))
                malformed = true;

            var message = malformed
                ? "Malformed f64 literal"
                : !hasF64Suffix
                    ? "Floating-point literals require the f64 suffix"
                    : null;
            if (message is not null)
            {
                diagnostics.Add(new Diagnostic(
                    "E_SYNTAX",
                    message,
                    file,
                    new Range(line, startColumn, line, startColumn + tokenLength)));
                return;
            }
        }

        tokens.Add(new Token("number", source[start..i], line, startColumn, file));
    }

    private static bool HasNumericSuffix(string source, int start, string suffix) =>
        start + suffix.Length <= source.Length && source.AsSpan(start, suffix.Length).SequenceEqual(suffix);

    private static bool HasIdentifierBoundary(string source, int index) =>
        index >= source.Length || !(char.IsLetterOrDigit(source[index]) || source[index] == '_');

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

    private static bool IsAsciiDigitSpan(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
            if (!IsAsciiDigit(character)) return false;
        return true;
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

        tokens.Add(new Token("text", source[start..i], startLine, startColumn, file));
    }
}

internal sealed class Parser
{
    private const int MaximumNestingDepth = 256;

    private static readonly HashSet<string> KnownEffects = new(StringComparer.Ordinal)
    {
        "fs.read",
        "fs.write",
        "process.spawn",
        "net.client",
        "net.listen",
        "db.read",
        "db.write",
        "env.read",
        "clock.read",
        "log.write",
        "secret.reveal"
    };

    private static readonly HashSet<string> BareExpressionKeywords = new(StringComparer.Ordinal)
    {
        "await", "false", "if", "lambda", "match", "null", "true"
    };

    private readonly IReadOnlyList<Token> _tokens;
    private readonly string _file;
    private readonly List<Diagnostic> _diagnostics;
    private readonly Token _emptyEof;
    private int _position;
    private int _nestingDepth;
    private int _statementNestingDepth;
    private readonly Dictionary<Expr, int> _expressionDepth = new(ReferenceEqualityComparer.Instance);

    public Parser(List<Token> tokens, string file, List<Diagnostic> diagnostics)
    {
        _tokens = tokens;
        _file = file;
        _diagnostics = diagnostics;
        _emptyEof = new Token("eof", "", 1, 1, file);
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
    private bool IsRouteKeyword(string text) => Current.Kind == "id" && Current.Text == text;
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

    private SourceDeclarationRefSyntax ParseSourceDeclarationRef()
    {
        var at = ExpectBareIdentifier();
        if (!Is("::"))
            return new SourceDeclarationRefSyntax(null, Array.Empty<string>(), at.Text, at);

        var root = at.Text;
        Take();
        var path = new List<string> { ExpectMemberIdentifier().Text };
        while (Is("::"))
        {
            Take();
            path.Add(ExpectMemberIdentifier().Text);
        }

        if (path.Count < 2)
            Fail(at, "E_SYNTAX", "A qualified declaration reference must include a module and declaration");

        return new SourceDeclarationRefSyntax(root, path[..^1], path[^1], at);
    }

    private void Fail(Token token, string code, string message)
    {
        _diagnostics.Add(new Diagnostic(
            code,
            message,
            string.IsNullOrEmpty(token.File) ? _file : token.File,
            token.Range));
        throw new ParseFailure();
    }

    public ParsedProgram? Parse()
    {
        try
        {
            Expect("module");
            var moduleAt = ExpectModuleSegment();
            var moduleSegments = new List<string> { moduleAt.Text };
            while (Is("::"))
            {
                Take();
                moduleSegments.Add(ExpectModuleSegment().Text);
            }
            var module = string.Join("::", moduleSegments);
            Expect(";");

            var unions = new List<UnionDecl>();
            var functions = new List<FunctionDecl>();
            var structs = new List<StructDecl>();
            var newtypes = new List<NewtypeDecl>();
            var traits = new List<TraitDecl>();
            var impls = new List<ImplDecl>();
            var tests = new List<TestDecl>();
            var commands = new List<CommandDecl>();
            var routes = new List<RouteDecl>();
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
                    functions.Add(ParseFunction(isPublic, isAsync: false));
                }
                else if (Is("async") && LookAhead().Text == "fn")
                {
                    Take();
                    functions.Add(ParseFunction(isPublic, isAsync: true));
                }
                else if (Is("adapter") && LookAhead().Text == "fn")
                {
                    Take();
                    functions.Add(ParseFunction(isPublic, isAsync: false, isAdapter: true));
                }
                else if (Is("adapter") && LookAhead().Text == "async" && LookAhead(2).Text == "fn")
                {
                    Take();
                    Take();
                    functions.Add(ParseFunction(isPublic, isAsync: true, isAdapter: true));
                }
                else if (Is("async") && LookAhead().Text == "adapter" && LookAhead(2).Text == "fn")
                {
                    Take();
                    Take();
                    functions.Add(ParseFunction(isPublic, isAsync: true, isAdapter: true));
                }
                else if (Is("union"))
                {
                    unions.Add(ParseUnion(isPublic));
                }
                else if (Is("struct"))
                {
                    structs.Add(ParseStruct(isPublic));
                }
                else if (Is("newtype"))
                {
                    newtypes.Add(ParseNewtype(isPublic));
                }
                else if (Is("trait"))
                {
                    traits.Add(ParseTrait(isPublic));
                }
                else if (Is("impl"))
                {
                    impls.Add(ParseImpl(isPublic));
                }
                else if (Is("test"))
                {
                    if (isPublic)
                        Fail(Current, "E_SYNTAX", "Test declarations cannot be public");
                    tests.Add(ParseTest());
                }
                else if (Is("command"))
                {
                    commands.Add(ParseCommand(isPublic));
                }
                else if (IsRouteKeyword("route"))
                {
                    routes.Add(ParseRoute(isPublic));
                }
                else
                {
                    var declaration = Current;
                    if (isPublic && Current.Kind == "eof")
                        Fail(Current, "E_SYNTAX", "Expected declaration after 'pub'");
                    if (Is("import"))
                        Fail(Current, "E_SYNTAX", "Imports are not supported; qualify declaration references with '::'");
                    Fail(declaration, "E_UNSUPPORTED", $"Declaration '{declaration.Text}' is not implemented yet");
                }
            }

            return new ParsedProgram(module, moduleAt, _file, unions, functions, structs, newtypes, traits, impls, tests, commands, routes);
        }
        catch (ParseFailure)
        {
            return null;
        }
    }

    private CommandDecl ParseCommand(bool isPublic)
    {
        if (isPublic)
            Fail(Current, "E_COMMAND_DECL", "Commands cannot be public");

        Expect("command");
        var name = ExpectBareIdentifier();
        if (Is("<"))
            Fail(Current, "E_COMMAND_DECL", "Command declarations do not accept type parameters");
        if (!Is("{"))
            Fail(Current, "E_COMMAND_DECL", "Expected '{' to begin command declaration");
        Take();

        var entries = new List<CommandEntrySyntax>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof")
                Fail(Current, "E_COMMAND_DECL", "Unclosed command declaration");

            var entryAt = Current;
            if (Is("help"))
            {
                Take();
                var help = ParseCommandText("command help");
                ExpectCommandPunctuation(";");
                entries.Add(new CommandHelpSyntax(entryAt, help));
            }
            else if (Is("argument"))
            {
                Take();
                var argumentName = ExpectCommandIdentifier("argument name");
                ExpectCommandPunctuation(":");
                var type = ParseType();
                ExpectCommandKeyword("help");
                var help = ParseCommandText("argument help");
                ExpectCommandPunctuation(";");
                entries.Add(new CommandArgumentSyntax(entryAt, argumentName.Text, type, help));
            }
            else if (Is("option"))
            {
                Take();
                var optionName = ExpectCommandIdentifier("option name");
                ExpectCommandPunctuation(":");
                var type = ParseType();
                ExpectCommandPunctuation("=");
                var defaultValue = ParseCommandLiteral();
                ExpectCommandKeyword("help");
                var help = ParseCommandText("option help");
                ExpectCommandPunctuation(";");
                entries.Add(new CommandOptionSyntax(entryAt, optionName.Text, type, defaultValue, help));
            }
            else if (Is("flag"))
            {
                Take();
                var flagName = ExpectCommandIdentifier("flag name");
                ExpectCommandKeyword("help");
                var help = ParseCommandText("flag help");
                ExpectCommandPunctuation(";");
                entries.Add(new CommandFlagSyntax(entryAt, flagName.Text, help));
            }
            else if (Is("handler") || Is("error"))
            {
                var entryKind = Take();
                ExpectCommandPunctuation(":");
                var reference = ParseSourceDeclarationRef();
                if (!reference.IsQualified)
                    Fail(reference.At, "E_COMMAND_DECL", $"The command {entryKind.Text} must be a qualified function reference");
                ExpectCommandPunctuation(";");
                entries.Add(entryKind.Text == "handler"
                    ? new CommandHandlerSyntax(entryAt, reference)
                    : new CommandErrorSyntax(entryAt, reference));
            }
            else
            {
                Fail(Current, "E_COMMAND_DECL", $"Unsupported command entry '{Current.Text}'");
            }
        }

        Take();
        return new CommandDecl(name.Text, entries, name);
    }

    private RouteDecl ParseRoute(bool isPublic)
    {
        if (isPublic)
            Fail(Current, "E_ROUTE_DECL", "Route declarations cannot be public");

        var at = ExpectRouteKeyword("route");
        if (!IsRouteKeyword("GET") && !IsRouteKeyword("POST"))
            Fail(Current, "E_ROUTE_DECL", "Expected route method 'GET' or 'POST'");
        var methodAt = Take();

        if (Current.Kind != "text")
            Fail(Current, "E_ROUTE_DECL", "Expected a static route path text literal");
        var pathAt = Take();
        var path = DecodeText(pathAt);
        var pathSegments = ParseRoutePathSegments(pathAt, path);

        if (!Is("{"))
            Fail(Current, "E_ROUTE_DECL", "Expected '{' to begin route declaration");
        Take();

        var items = new List<RouteItemSyntax>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof")
                Fail(Current, "E_ROUTE_DECL", "Unclosed route declaration");

            if (IsRouteKeyword("body"))
            {
                var itemAt = Take();
                ExpectRoutePunctuation(":");
                var type = ParseRouteQualifiedType("route body type");
                ExpectRoutePunctuation(";");
                items.Add(new RouteBodySyntax(itemAt, type));
            }
            else if (IsRouteKeyword("handler"))
            {
                var itemAt = Take();
                ExpectRoutePunctuation(":");
                var reference = ParseRouteQualifiedReference("route handler");
                ExpectRoutePunctuation(";");
                items.Add(new RouteHandlerSyntax(itemAt, reference));
            }
            else if (IsRouteKeyword("path") || IsRouteKeyword("query"))
            {
                var itemAt = Take();
                var nameAt = ExpectRouteIdentifier($"{itemAt.Text} binding name");
                ExpectRoutePunctuation(":");
                var type = ParseType();
                ExpectRoutePunctuation(";");
                items.Add(new RouteBindingSyntax(
                    itemAt,
                    nameAt,
                    itemAt.Text == "path" ? RouteBindingSyntaxKind.Path : RouteBindingSyntaxKind.Query,
                    type));
            }
            else if (IsRouteKeyword("response"))
            {
                var itemAt = Take();
                var variantAt = ExpectRouteIdentifier("response variant");
                ExpectRoutePunctuation(":");

                if (Current.Kind != "number")
                    Fail(Current, "E_ROUTE_DECL", "Expected numeric response status");
                var statusAt = Take();
                if (!int.TryParse(statusAt.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var statusCode))
                    Fail(statusAt, "E_ROUTE_DECL", "Response status is outside the supported integer range");

                Token? formatAt = null;
                string? format = null;
                TypeSyntax? bodyType = null;
                if (IsRouteKeyword("json"))
                {
                    formatAt = Take();
                    format = formatAt.Text;
                    bodyType = ParseRouteResponseType();
                }
                else if (IsRouteKeyword("html"))
                {
                    formatAt = Take();
                    format = formatAt.Text;
                }

                ExpectRoutePunctuation(";");
                items.Add(new RouteResponseSyntax(
                    itemAt,
                    variantAt,
                    variantAt.Text,
                    statusAt,
                    statusCode,
                    formatAt,
                    format,
                    bodyType));
            }
            else
            {
                Fail(Current, "E_ROUTE_DECL", $"Unsupported route item '{Current.Text}'");
            }
        }

        Take();
        return new RouteDecl(at, methodAt, methodAt.Text, pathAt, path, pathSegments, items);
    }

    private IReadOnlyList<RoutePathSegmentSyntax> ParseRoutePathSegments(Token pathAt, string path)
    {
        if (!path.StartsWith("/", StringComparison.Ordinal) || path.Contains('?') || path.Contains('#'))
            Fail(pathAt, "E_ROUTE_DECL", "Route paths must begin with '/' and cannot contain '?' or '#'");

        if (path == "/") return [];

        var segments = path[1..].Split('/');
        if (segments.Any(segment => segment.Length == 0))
            Fail(pathAt, "E_ROUTE_DECL", "Route paths cannot contain empty segments");

        var result = new List<RoutePathSegmentSyntax>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment.Contains('{') || segment.Contains('}'))
            {
                if (segment.Length >= 3 &&
                    segment[0] == '{' &&
                    segment[^1] == '}' &&
                    IsRouteTemplateIdentifier(segment[1..^1]))
                {
                    result.Add(new RoutePathSegmentSyntax(pathAt, null, segment[1..^1]));
                    continue;
                }

                Fail(pathAt, "E_ROUTE_DECL", "Route placeholders must be whole '{identifier}' path segments");
            }

            result.Add(new RoutePathSegmentSyntax(pathAt, segment, null));
        }

        return result;
    }

    private static bool IsRouteTemplateIdentifier(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_')) return false;
        return value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private TypeSyntax ParseRouteQualifiedType(string description)
    {
        var type = ParseType();
        if (!type.Reference.IsQualified)
            Fail(type.At, "E_ROUTE_DECL", $"The {description} must use a fully qualified type reference");
        return type;
    }

    private TypeSyntax ParseRouteResponseType()
    {
        var type = ParseType();
        if (type.Reference.IsQualified || IsRouteResponseBuiltin(type))
            return type;

        Fail(
            type.At,
            "E_ROUTE_DECL",
            "The JSON response body type must be fully qualified or use the built-in i32, bool, or Text type");
        throw new ParseFailure();
    }

    private static bool IsRouteResponseBuiltin(TypeSyntax type) =>
        !type.Reference.IsQualified &&
        type.Reference.Module.Count == 0 &&
        type.Args.Count == 0 &&
        type.Reference.Declaration is "i32" or "bool" or "Text";

    private SourceDeclarationRefSyntax ParseRouteQualifiedReference(string description)
    {
        var reference = ParseSourceDeclarationRef();
        if (!reference.IsQualified)
            Fail(reference.At, "E_ROUTE_DECL", $"The {description} must be fully qualified");
        return reference;
    }

    private Token ExpectRouteKeyword(string keyword)
    {
        if (IsRouteKeyword(keyword)) return Take();
        Fail(Current, "E_ROUTE_DECL", $"Expected '{keyword}' in route declaration");
        throw new ParseFailure();
    }

    private Token ExpectRouteIdentifier(string description)
    {
        if (IsLexicalIdentifier(Current)) return Take();
        Fail(Current, "E_ROUTE_DECL", $"Expected {description}");
        throw new ParseFailure();
    }

    private Token ExpectRoutePunctuation(string punctuation)
    {
        if (Is(punctuation)) return Take();
        Fail(Current, "E_ROUTE_DECL", $"Expected '{punctuation}' in route declaration");
        throw new ParseFailure();
    }

    private Token ExpectCommandIdentifier(string description)
    {
        if (IsBareIdentifier(Current)) return Take();
        Fail(Current, "E_COMMAND_DECL", $"Expected {description}");
        throw new ParseFailure();
    }

    private Token ExpectCommandKeyword(string keyword)
    {
        if (Is(keyword)) return Take();
        Fail(Current, "E_COMMAND_DECL", $"Expected '{keyword}' in command declaration");
        throw new ParseFailure();
    }

    private Token ExpectCommandPunctuation(string punctuation)
    {
        if (Is(punctuation)) return Take();
        Fail(Current, "E_COMMAND_DECL", $"Expected '{punctuation}' in command declaration");
        throw new ParseFailure();
    }

    private string ParseCommandText(string description)
    {
        if (Current.Kind != "text")
            Fail(Current, "E_COMMAND_DECL", $"Expected a text literal for {description}");
        return DecodeText(Take());
    }

    private CommandLiteralSyntax ParseCommandLiteral()
    {
        if (Current.Kind == "text")
        {
            var text = Take();
            return new CommandTextLiteralSyntax(text, DecodeText(text));
        }
        if (Is("true") || Is("false"))
        {
            var boolean = Take();
            return new CommandBooleanLiteralSyntax(boolean, boolean.Text == "true");
        }

        var negative = Is("-");
        var at = negative ? Take() : Current;
        if (Current.Kind == "number")
        {
            var number = Take();
            var spelling = (negative ? "-" : string.Empty) + number.Text;
            if (!int.TryParse(spelling, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                Fail(number, "E_COMMAND_DECL", "Command integer default is outside i32 range");
            return new CommandIntegerLiteralSyntax(at, value);
        }

        Fail(Current, "E_COMMAND_DECL", "Expected a literal command option default");
        throw new ParseFailure();
    }

    private FunctionDecl ParseFunction(bool isPublic, bool isAsync, bool isAdapter = false)
    {
        Expect("fn");
        var name = ExpectBareIdentifier();
        var typeParameters = ParseFunctionTypeParameters();

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
        var effects = ParseEffects();
        string? adapterOperation = null;
        IReadOnlyList<Stmt> body;
        if (isAdapter)
        {
            if (!Is("="))
                Fail(Current, "E_ADAPTER_DECL", "An adapter function must bind an operation with '= \"operation.id\";'");
            Take();
            if (Current.Kind != "text")
                Fail(Current, "E_ADAPTER_DECL", "Expected a text operation ID after '=' in adapter function");
            adapterOperation = DecodeText(Take());
            Expect(";");
            body = [];
        }
        else
        {
            body = ParseStatementBlock("Unclosed function body");
        }
        return new FunctionDecl(
            name.Text,
            typeParameters,
            isPublic,
            isAsync,
            parameters,
            returnType,
            effects,
            body,
            name,
            isAdapter,
            adapterOperation);
    }

    private TestDecl ParseTest()
    {
        var at = Expect("test");
        if (Current.Kind != "text")
            Fail(Current, "E_SYNTAX", "Expected a text literal after 'test'");
        var nameAt = Take();
        var name = DecodeText(nameAt);

        Expect("{");
        var setup = new List<LetStmt>();
        while (Is("let"))
            setup.Add((LetStmt)ParseStatement());

        if (Current.Kind == "eof")
            Fail(Current, "E_SYNTAX", "Unclosed test body");
        if (!Is("assert"))
        {
            if (Is("return") || Is("if"))
                Fail(Current, "E_UNSUPPORTED", $"Statement '{Current.Text}' is not supported in test bodies");
            if (Is("}"))
                Fail(Current, "E_SYNTAX", "Test body must end with an assert statement");
            Fail(Current, "E_UNSUPPORTED", $"Statement '{Current.Text}' is not supported in test bodies");
        }

        var assertAt = Take();
        var assertion = ParseExpr();
        Expect(";");
        if (!Is("}"))
            Fail(Current, "E_SYNTAX", "The assert statement must be the final statement in a test body");
        Expect("}");

        return new TestDecl(name, setup, assertion, at, nameAt, assertAt);
    }

    private List<TypeParameterSyntax> ParseFunctionTypeParameters() => ParseTypeParameters("generic function", allowTraitBounds: true);

    private List<TypeParameterSyntax> ParseStructTypeParameters() => ParseTypeParameters("generic struct");

    private List<TypeParameterSyntax> ParseUnionTypeParameters() => ParseTypeParameters("generic union");

    private List<TypeParameterSyntax> ParseTypeParameters(string owner, bool allowTraitBounds = false)
    {
        var typeParameters = new List<TypeParameterSyntax>();
        if (!Is("<")) return typeParameters;

        Take();
        if (Is(">")) Fail(Current, "E_SYNTAX", "Expected type parameter");
        if (Current.Kind == "eof")
            Fail(Current, "E_SYNTAX", $"Unclosed {owner} type parameter list");

        while (true)
        {
            var parameter = ExpectBareIdentifier();
            var bounds = new List<SourceDeclarationRefSyntax>();
            if (allowTraitBounds && Is(":"))
            {
                Take();
                while (true)
                {
                    bounds.Add(ParseSourceDeclarationRef());
                    if (!Is("+")) break;
                    Take();
                }
            }
            typeParameters.Add(new TypeParameterSyntax(parameter.Text, parameter, bounds));
            if (Is(">")) break;
            if (Current.Kind == "eof")
                Fail(Current, "E_SYNTAX", $"Unclosed {owner} type parameter list");

            Expect(",");
            if (Is(">")) Fail(Current, "E_SYNTAX", "Expected type parameter after ','");
            if (Current.Kind == "eof")
                Fail(Current, "E_SYNTAX", $"Unclosed {owner} type parameter list");
        }

        Expect(">");
        return typeParameters;
    }

    private List<Stmt> ParseStatementBlock(string unclosedMessage)
    {
        Expect("{");
        var statements = new List<Stmt>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", unclosedMessage);
            statements.Add(ParseStatement());
        }

        Expect("}");
        return statements;
    }

    private Stmt ParseStatement()
    {
        if (Is("let"))
        {
            var at = Take();
            var local = ExpectBareIdentifier();
            Expect(":");
            var type = ParseType();
            Expect("=");
            var value = ParseExpr();
            Expect(";");
            return new LetStmt(at, local.Text, type, value);
        }

        if (Is("return"))
        {
            var at = Take();
            var value = ParseExpr();
            Expect(";");
            return new ReturnStmt(at, value);
        }

        if (Is("if")) return ParseIfStatement();

        if (Is("for") && LookAhead().Kind == "id" && LookAhead(2).Text == "in")
            return ParseForStatement();

        if (Is("with")) return ParseWithTransactionStatement();

        if (Is("var") && LookAhead().Kind == "id" && LookAhead(2).Text == ":")
        {
            var at = Take();
            var local = ExpectBareIdentifier();
            Expect(":");
            var type = ParseType();
            Expect("=");
            var value = ParseExpr();
            Expect(";");
            return new VarStmt(at, local.Text, local, type, value);
        }

        if (IsBareIdentifier(Current) && LookAhead().Text == "=")
        {
            var local = Take();
            Expect("=");
            var value = ParseExpr();
            Expect(";");
            return new AssignmentStmt(local, local.Text, local, value);
        }

        if (Is("await"))
            Fail(Current, "E_UNSUPPORTED", $"Statement '{Current.Text}' is not implemented yet");

        Fail(Current, "E_UNSUPPORTED", $"Statement '{Current.Text}' is not implemented yet");
        throw new ParseFailure();
    }

    private ForStmt ParseForStatement()
    {
        var at = Expect("for");
        if (_statementNestingDepth >= MaximumNestingDepth)
            Fail(at, "E_SYNTAX", "Statement nesting is too deep");

        _statementNestingDepth++;
        try
        {
            var local = ExpectBareIdentifier();
            Expect("in");
            // The following brace begins the loop body, so it cannot begin a
            // struct construction in the collection expression.
            var collection = ParseExpr(allowStructConstruction: false);
            var body = ParseStatementBlock("Unclosed for block");
            return new ForStmt(at, local.Text, local, collection, body);
        }
        finally
        {
            _statementNestingDepth--;
        }
    }

    private IfStmt ParseIfStatement()
    {
        var at = Expect("if");
        if (_statementNestingDepth >= MaximumNestingDepth)
            Fail(at, "E_SYNTAX", "Statement nesting is too deep");

        _statementNestingDepth++;
        try
        {
            // The next brace starts the then block, so a leading identifier cannot
            // consume it as a struct construction.
            var condition = ParseExpr(allowStructConstruction: false);
            var then = ParseStatementBlock("Unclosed if block");
            IReadOnlyList<Stmt> otherwise = Array.Empty<Stmt>();
            if (Is("else"))
            {
                Take();
                otherwise = ParseStatementBlock("Unclosed else block");
            }

            return new IfStmt(at, condition, then, otherwise);
        }
        finally
        {
            _statementNestingDepth--;
        }
    }

    private WithTransactionStmt ParseWithTransactionStatement()
    {
        var at = Expect("with");
        if (_statementNestingDepth >= MaximumNestingDepth)
            Fail(at, "E_SYNTAX", "Statement nesting is too deep");

        _statementNestingDepth++;
        try
        {
            var begin = ParseExpr();
            Expect("as");
            var local = ExpectBareIdentifier();
            var body = ParseStatementBlock("Unclosed transaction body");
            return new WithTransactionStmt(at, begin, local.Text, local, body);
        }
        finally
        {
            _statementNestingDepth--;
        }
    }

    private List<EffectSyntax> ParseEffects()
    {
        Expect("{");
        var effects = new List<EffectSyntax>();
        var seenEffects = new HashSet<string>(StringComparer.Ordinal);
        while (!Is("}"))
        {
            if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed effects list");
            var effect = ParseEffect();
            if (!KnownEffects.Contains(effect.Name))
                Fail(effect.At, "E_EFFECT_UNKNOWN", $"Unknown effect '{effect.Name}'");
            if (!seenEffects.Add(effect.Name))
                Fail(effect.At, "E_EFFECT_DUPLICATE", $"Duplicate effect '{effect.Name}'");
            effects.Add(effect);

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
        return effects;
    }

    private EffectSyntax ParseEffect()
    {
        var at = ExpectModuleSegment();
        var name = at.Text;
        while (Is("."))
        {
            Take();
            name += "." + ExpectModuleSegment().Text;
        }
        return new EffectSyntax(name, at);
    }

    private UnionDecl ParseUnion(bool isPublic)
    {
        Expect("union");
        var name = ExpectBareIdentifier();
        var typeParameters = ParseUnionTypeParameters();
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
        return new UnionDecl(name.Text, typeParameters, isPublic, variants, name);
    }

    private StructDecl ParseStruct(bool isPublic)
    {
        Expect("struct");
        var name = ExpectBareIdentifier();
        var typeParameters = ParseStructTypeParameters();
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
        return new StructDecl(name.Text, typeParameters, isPublic, fields, name);
    }

    private NewtypeDecl ParseNewtype(bool isPublic)
    {
        Expect("newtype");
        var name = ExpectBareIdentifier();
        if (Is("<"))
            Fail(Current, "E_UNSUPPORTED", "Generic newtype declarations are not implemented");
        Expect("=");
        var representation = ParseType();
        Expect(";");
        return new NewtypeDecl(name.Text, isPublic, representation, name);
    }

    private TraitDecl ParseTrait(bool isPublic)
    {
        Expect("trait");
        var name = ExpectBareIdentifier();
        Expect("{");

        var methods = new List<TraitMethodDecl>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof")
                Fail(Current, "E_TRAIT_DECL", "Unclosed trait declaration");
            if (!Is("fn"))
                Fail(Current, "E_TRAIT_DECL", "A trait body may contain only method signatures");

            Take();
            var method = ExpectBareIdentifier();
            if (Is("<"))
                Fail(Current, "E_TRAIT_DECL", "Trait methods cannot declare type parameters");

            Expect("(");
            var parameters = new List<ParameterDecl>();
            while (!Is(")"))
            {
                if (Current.Kind == "eof")
                    Fail(Current, "E_TRAIT_DECL", "Unclosed trait method parameter list");
                var parameter = ExpectBareIdentifier();
                Expect(":");
                parameters.Add(new ParameterDecl(parameter.Text, ParseType(), parameter));
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
            var effects = ParseEffects();
            Expect(";");
            methods.Add(new TraitMethodDecl(method.Text, parameters, returnType, effects, method));
        }

        Expect("}");
        return new TraitDecl(name.Text, isPublic, methods, name);
    }

    private ImplDecl ParseImpl(bool isPublic)
    {
        var at = Expect("impl");
        var trait = ParseSourceDeclarationRef();
        Expect("for");
        var target = ParseType();
        Expect("{");

        var methods = new List<ImplMethodBindingDecl>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof")
                Fail(Current, "E_TRAIT_DECL", "Unclosed trait implementation declaration");
            var method = ExpectBareIdentifier();
            Expect("=");
            var function = ParseSourceDeclarationRef();
            Expect(";");
            methods.Add(new ImplMethodBindingDecl(method.Text, method, function, method));
        }

        Expect("}");
        return new ImplDecl(isPublic, trait, target, methods, at);
    }

    private TypeSyntax ParseType()
    {
        EnterNesting(Current, "Type nesting is too deep");
        try
        {
            var reference = ParseSourceDeclarationRef();

            var arguments = ParseTypeArguments();

            return new TypeSyntax(reference, arguments, reference.At);
        }
        finally
        {
            _nestingDepth--;
        }
    }

    private List<TypeSyntax> ParseTypeArguments()
    {
        var arguments = new List<TypeSyntax>();
        if (!Is("<")) return arguments;

        Take();
        if (Is(">")) Fail(Current, "E_SYNTAX", "Expected type argument");
        while (true)
        {
            arguments.Add(ParseType());
            if (!Is(",")) break;
            Take();
            if (Is(">")) Fail(Current, "E_SYNTAX", "Expected type argument after ','");
        }
        Expect(">");
        return arguments;
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
                    "*" or "/" => 5,
                    "+" or "-" => 4,
                    "<" or "<=" or ">" or ">=" => 3,
                    "==" or "!=" => 2,
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
        if (Is("await"))
        {
            var at = Take();
            var value = ParseExpr(6, allowStructConstruction);
            var depth = ExpressionDepth(value) + 1;
            if (depth > MaximumNestingDepth)
                Fail(at, "E_SYNTAX", "Expression nesting is too deep");
            return ParsePostfix(RegisterExpression(new AwaitExpr(at, value), depth));
        }
        if (Is("["))
        {
            var at = Take();
            var items = new List<Expr>();
            while (!Is("]"))
            {
                if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed list literal");
                items.Add(ParseExpr());
                if (Is(","))
                {
                    Take();
                    if (Is("]")) break;
                }
                else if (!Is("]"))
                {
                    Expect(",");
                }
            }
            Expect("]");
            var depth = 1 + items.Select(ExpressionDepth).DefaultIfEmpty(0).Max();
            return ParsePostfix(RegisterExpression(new ListExpr(at, items), depth));
        }
        if (token.Kind == "number")
        {
            Take();
            return ParsePostfix(RegisterExpression(ParseNumericLiteral(token, token)));
        }
        if (Is("-"))
        {
            var minus = Take();
            if (Current.Kind == "number")
            {
                var magnitudeToken = Take();
                var literalKind = NumericLiteralKindFromToken(magnitudeToken);
                if (literalKind is NumericLiteralKind.I32 or NumericLiteralKind.I64)
                {
                    var negativeLiteral = ParseIntegerLiteral(magnitudeToken, minus, negative: true);
                    return ParsePostfix(RegisterExpression(negativeLiteral));
                }

                var positiveLiteral = RegisterExpression(ParseNumericLiteral(magnitudeToken, magnitudeToken));
                var unaryDepth = ExpressionDepth(positiveLiteral) + 1;
                if (unaryDepth > MaximumNestingDepth)
                    Fail(minus, "E_SYNTAX", "Expression nesting is too deep");
                return ParsePostfix(RegisterExpression(new UnaryExpr(minus, "-", positiveLiteral), unaryDepth));
            }

            var operand = ParseExpr(6, allowStructConstruction);
            var depth = ExpressionDepth(operand) + 1;
            if (depth > MaximumNestingDepth)
                Fail(minus, "E_SYNTAX", "Expression nesting is too deep");
            var negated = RegisterExpression(new UnaryExpr(minus, "-", operand), depth);
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
            if (Is("lambda"))
                return ParsePostfix(ParseLambda(Take()));
            if (Is("if"))
                Fail(token, "E_UNSUPPORTED", $"Expression '{token.Text}' is not implemented yet");
            if (!IsBareIdentifier(token))
                Fail(token, "E_SYNTAX", $"Keyword '{token.Text}' is not an expression");

            var reference = ParseSourceDeclarationRef();
            Expr expression;
            if (reference.IsQualified && IsTypeArgumentListFollowedByUnionVariant())
            {
                expression = ParseQualifiedTypeMemberCall(reference, ParseTypeArguments());
            }
            else if (reference.IsQualified && Is("."))
            {
                expression = ParseQualifiedTypeMemberCall(reference, []);
            }
            else if (allowStructConstruction && IsTypeArgumentListFollowedByStructBrace())
            {
                var type = new TypeSyntax(reference, ParseTypeArguments(), reference.At);
                expression = ParseStructConstruction(type);
            }
            else if (reference.IsQualified && allowStructConstruction && Is("{"))
            {
                expression = ParseStructConstruction(new TypeSyntax(reference, [], reference.At));
            }
            else if (reference.IsQualified && Is("("))
            {
                var arguments = ParseArguments();
                expression = RegisterExpression(new CallExpr(token, reference, arguments),
                    1 + arguments.Select(ExpressionDepth).DefaultIfEmpty(0).Max());
            }
            else if (reference.IsQualified)
            {
                expression = RegisterExpression(new DeclarationRefExpr(token, reference));
            }
            else if (allowStructConstruction && Is("{"))
            {
                expression = ParseStructConstruction(new TypeSyntax(reference, [], reference.At));
            }
            else if (Is("("))
            {
                var arguments = ParseArguments();
                expression = RegisterExpression(new CallExpr(token, reference, arguments),
                    1 + arguments.Select(ExpressionDepth).DefaultIfEmpty(0).Max());
            }
            else
            {
                expression = RegisterExpression(new NameExpr(token, reference.Declaration));
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

    private static NumericLiteralKind NumericLiteralKindFromToken(Token token) =>
        token.Text.EndsWith("f64", StringComparison.Ordinal)
            ? NumericLiteralKind.F64
            : token.Text.EndsWith("i64", StringComparison.Ordinal)
                ? NumericLiteralKind.I64
                : token.Text.EndsWith("u32", StringComparison.Ordinal)
                    ? NumericLiteralKind.U32
                    : token.Text.EndsWith("u64", StringComparison.Ordinal)
                        ? NumericLiteralKind.U64
                        : NumericLiteralKind.I32;

    private NumberExpr ParseNumericLiteral(Token literalAt, Token expressionAt) =>
        NumericLiteralKindFromToken(literalAt) == NumericLiteralKind.F64
            ? ParseF64Literal(literalAt, expressionAt)
            : ParseIntegerLiteral(literalAt, expressionAt);

    private NumberExpr ParseF64Literal(Token literalAt, Token expressionAt)
    {
        var spelling = literalAt.Text[..^3];
        if (!double.TryParse(spelling, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            Fail(literalAt, "E_NUMERIC_LITERAL_RANGE", "f64 literal is outside the finite range");

        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        return new NumberExpr(expressionAt, bits.ToString("X16", CultureInfo.InvariantCulture), NumericLiteralKind.F64);
    }

    private NumberExpr ParseIntegerLiteral(Token literalAt, Token expressionAt, bool negative = false)
    {
        var kind = NumericLiteralKindFromToken(literalAt);
        if (kind == NumericLiteralKind.F64)
            throw new InvalidOperationException("An f64 literal reached integer parsing");
        var suffixLength = kind == NumericLiteralKind.I32 ? 0 : 3;
        var digitsLength = literalAt.Text.Length - suffixLength;
        var significantStart = 0;
        while (significantStart < digitsLength && literalAt.Text[significantStart] == '0')
            significantStart++;
        var significantDigits = digitsLength - significantStart;
        var maximumDigits = kind switch
        {
            NumericLiteralKind.I32 => 10,
            NumericLiteralKind.I64 => 19,
            NumericLiteralKind.U32 => 10,
            NumericLiteralKind.U64 => 20,
            _ => throw new InvalidOperationException("Unknown integer literal kind")
        };
        if (significantDigits > maximumDigits)
            Fail(literalAt, "E_TYPE_MISMATCH", $"Integer literal is outside {IntegerLiteralName(kind)} range");
        var magnitude = BigInteger.Zero;
        if (significantDigits != 0 &&
            !BigInteger.TryParse(literalAt.Text.AsSpan(significantStart, significantDigits), NumberStyles.None, CultureInfo.InvariantCulture, out magnitude))
            Fail(literalAt, "E_TYPE_MISMATCH", $"Integer literal is outside {IntegerLiteralName(kind)} range");

        var value = negative ? -magnitude : magnitude;
        var (minimum, maximum) = kind switch
        {
            NumericLiteralKind.I32 => (new BigInteger(int.MinValue), new BigInteger(int.MaxValue)),
            NumericLiteralKind.I64 => (new BigInteger(long.MinValue), new BigInteger(long.MaxValue)),
            NumericLiteralKind.U32 => (BigInteger.Zero, new BigInteger(uint.MaxValue)),
            NumericLiteralKind.U64 => (BigInteger.Zero, new BigInteger(ulong.MaxValue)),
            _ => throw new InvalidOperationException("Unknown integer literal kind")
        };
        if (value < minimum || value > maximum)
            Fail(literalAt, "E_TYPE_MISMATCH", $"Integer literal is outside {IntegerLiteralName(kind)} range");

        return new NumberExpr(expressionAt, value.ToString(CultureInfo.InvariantCulture), kind);
    }

    private static string IntegerLiteralName(NumericLiteralKind kind) => kind switch
    {
        NumericLiteralKind.I32 => "i32",
        NumericLiteralKind.I64 => "i64",
        NumericLiteralKind.U32 => "u32",
        NumericLiteralKind.U64 => "u64",
        _ => throw new InvalidOperationException("Unknown integer literal kind")
    };

    private bool IsTypeArgumentListFollowedByStructBrace()
    {
        if (!Is("<")) return false;

        var depth = 0;
        for (var index = _position; index < _tokens.Count; index++)
        {
            var kind = _tokens[index].Kind;
            if (kind == "eof") return false;
            if (kind is not ("id" or "::" or "," or "<" or ">")) return false;
            if (kind == "<") depth++;
            else if (kind == ">")
            {
                depth--;
                if (depth == 0)
                    return index + 1 < _tokens.Count && _tokens[index + 1].Kind == "{";
                if (depth < 0) return false;
            }
        }

        return false;
    }

    private bool IsTypeArgumentListFollowedByUnionVariant()
    {
        if (!Is("<")) return false;

        var depth = 0;
        for (var index = _position; index < _tokens.Count; index++)
        {
            var token = _tokens[index];
            if (token.Kind == "eof" || token.Kind is not ("id" or "::" or "," or "<" or ">"))
                return false;
            if (token.Kind == "<")
            {
                depth++;
                continue;
            }

            if (token.Kind != ">") continue;
            depth--;
            if (depth < 0) return false;
            if (depth != 0) continue;

            var dotIndex = index + 1;
            var variantIndex = index + 2;
            return dotIndex < _tokens.Count && _tokens[dotIndex].Text == "." &&
                   variantIndex < _tokens.Count && _tokens[variantIndex].Kind == "id";
        }

        return false;
    }

    private Expr ParseQualifiedTypeMemberCall(
        SourceDeclarationRefSyntax owner,
        IReadOnlyList<TypeSyntax> typeArguments)
    {
        Expect(".");
        var memberAt = ExpectMemberIdentifier();
        var arguments = Is("(") ? ParseArguments() : [];
        var depth = arguments.Select(ExpressionDepth).DefaultIfEmpty(0).Max() + 1;
        return RegisterExpression(
            new QualifiedTypeMemberCallExpr(owner.At, owner, typeArguments, memberAt.Text, memberAt, arguments),
            depth);
    }

    private Expr ParseStructConstruction(TypeSyntax typeName)
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
        return RegisterExpression(new StructConstructExpr(typeName.At, typeName, fields), depth);
    }

    private Expr ParseLambda(Token at)
    {
        Expect("(");
        var parameterAt = ExpectBareIdentifier();
        Expect(":");
        var parameterType = ParseType();
        if (Is(","))
            Fail(Current, "E_UNSUPPORTED", "Lambda expressions currently support exactly one parameter");
        Expect(")");
        Expect("=>");
        var body = ParseExpr();
        return RegisterExpression(
            new LambdaExpr(at, parameterAt.Text, parameterAt, parameterType, body),
            ExpressionDepth(body) + 1);
    }

    private Expr ParsePostfix(Expr expression)
    {
        while (true)
        {
            if (Is("("))
            {
                if (expression is not LambdaExpr)
                    Fail(Current, "E_UNSUPPORTED", "Only an immediately invoked lambda expression is supported");
                var lambda = (LambdaExpr)expression;
                var arguments = ParseArguments();
                if (arguments.Count != 1)
                    Fail(lambda.At, "E_UNSUPPORTED", "Lambda invocation requires exactly one argument");
                var argument = arguments[0];
                var depth = Math.Max(ExpressionDepth(expression), ExpressionDepth(argument)) + 1;
                expression = RegisterExpression(new LambdaInvokeExpr(lambda.At, lambda, argument), depth);
                continue;
            }

            if (!Is("."))
                break;
            Take();
            var fieldAt = ExpectMemberIdentifier();
            if (Is("("))
            {
                var arguments = ParseArguments();
                var depth = Math.Max(
                    ExpressionDepth(expression),
                    arguments.Select(ExpressionDepth).DefaultIfEmpty(0).Max()) + 1;
                expression = RegisterExpression(
                    new MemberCallExpr(expression.At, expression, fieldAt.Text, fieldAt, arguments), depth);
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

        var reference = ParseSourceDeclarationRef();
        if (Is("<"))
            Fail(Current, "E_TYPE_MISMATCH", "Match patterns take type arguments from the matched union value; omit union type arguments");
        SourceDeclarationRefSyntax? union = null;
        var variantName = reference.Declaration;
        if (reference.IsQualified || Is("."))
        {
            if (!Is("."))
                Fail(at, "E_SYNTAX", "A qualified union pattern must name a variant after '.'");
            Take();
            union = reference;
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

        return new VariantPattern(at, union, variantName, bindings);
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
