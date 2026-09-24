using System.Collections.ObjectModel;

internal enum AlTypeKind
{
    Error,
    I32,
    Bool,
    Text,
    Union,
    Option,
    Result
}

internal sealed class AlType : IEquatable<AlType>
{
    private readonly ReadOnlyCollection<AlType> _arguments;

    private AlType(AlTypeKind kind, string displayName, int unionId = -1, IEnumerable<AlType>? arguments = null)
    {
        Kind = kind;
        DisplayName = displayName;
        UnionId = unionId;
        _arguments = Array.AsReadOnly((arguments ?? []).ToArray());
    }

    public AlTypeKind Kind { get; }
    public string DisplayName { get; }
    public IReadOnlyList<AlType> Arguments => _arguments;
    public bool IsI32 => Kind == AlTypeKind.I32;
    public bool IsBool => Kind == AlTypeKind.Bool;
    public bool IsText => Kind == AlTypeKind.Text;
    internal int UnionId { get; }
    internal bool IsError => Kind == AlTypeKind.Error;

    internal static AlType Error { get; } = new(AlTypeKind.Error, "<error>");
    internal static AlType I32 { get; } = new(AlTypeKind.I32, "i32");
    internal static AlType Bool { get; } = new(AlTypeKind.Bool, "bool");
    internal static AlType Text { get; } = new(AlTypeKind.Text, "Text");

    internal static AlType ForUnion(int unionId, string name) => new(AlTypeKind.Union, name, unionId);
    internal static AlType Option(AlType item) => new(AlTypeKind.Option, $"Option<{item.DisplayName}>", arguments: [item]);
    internal static AlType Result(AlType ok, AlType error) => new(AlTypeKind.Result, $"Result<{ok.DisplayName}, {error.DisplayName}>", arguments: [ok, error]);

    public bool Equals(AlType? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || Kind != other.Kind) return false;
        if (Kind == AlTypeKind.Union) return UnionId == other.UnionId;
        if (_arguments.Count != other._arguments.Count) return false;
        for (var i = 0; i < _arguments.Count; i++)
            if (!_arguments[i].Equals(other._arguments[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is AlType other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        if (Kind == AlTypeKind.Union)
        {
            hash.Add(UnionId);
        }
        else
        {
            foreach (var argument in _arguments) hash.Add(argument);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(AlType? left, AlType? right) => Equals(left, right);
    public static bool operator !=(AlType? left, AlType? right) => !Equals(left, right);
}

internal sealed record CheckedVariantField(string? Name, AlType Type, int Index, Token At);
internal sealed record CheckedVariant(int Id, string Name, IReadOnlyList<CheckedVariantField> Fields, Token At);
internal sealed record CheckedUnion(int Id, string Name, bool Public, AlType Type, IReadOnlyList<CheckedVariant> Variants, Token At);
internal sealed record CheckedParameter(string Name, AlType Type, int LocalId, Token At);

internal abstract record TypedExpr(AlType Type, Token At);
internal sealed record TypedNumberExpr(Token At, int Value) : TypedExpr(AlType.I32, At);
internal sealed record TypedBoolExpr(Token At, bool Value) : TypedExpr(AlType.Bool, At);
internal sealed record TypedTextExpr(Token At, string Value) : TypedExpr(AlType.Text, At);
internal sealed record TypedLocalExpr(AlType Type, int LocalId, Token At) : TypedExpr(Type, At);
internal sealed record TypedBinaryExpr(AlType Type, string Op, TypedExpr Left, TypedExpr Right, Token At) : TypedExpr(Type, At);
internal sealed record TypedCallExpr(AlType Type, int FunctionId, IReadOnlyList<TypedExpr> Arguments, Token At) : TypedExpr(Type, At);

internal enum BuiltinVariant
{
    Some,
    None,
    Ok,
    Err
}

internal sealed record TypedBuiltinConstructExpr(
    AlType Type,
    BuiltinVariant Variant,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);

internal sealed record TypedUnionConstructExpr(
    AlType Type,
    int UnionId,
    int VariantId,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);

internal abstract record TypedPattern(Token At);
internal sealed record TypedWildcardPattern(Token At) : TypedPattern(At);
internal sealed record BoundLocal(string Name, int LocalId, AlType Type, Token At);
internal sealed record TypedVariantPattern(
    VariantShape Shape,
    IReadOnlyList<BoundLocal> Bindings,
    Token At) : TypedPattern(At);
internal sealed record TypedMatchArm(TypedPattern Pattern, TypedExpr Body, Token At);
internal sealed record TypedMatchExpr(
    AlType Type,
    TypedExpr Value,
    IReadOnlyList<TypedMatchArm> Arms,
    Token At) : TypedExpr(Type, At);

internal abstract record TypedStmt(Token At);
internal sealed record TypedLetStmt(int LocalId, string Name, AlType Type, TypedExpr Value, Token At) : TypedStmt(At);
internal sealed record TypedReturnStmt(TypedExpr Value, Token At) : TypedStmt(At);

internal sealed class CheckedFunction
{
    internal CheckedFunction(
        int id,
        string name,
        bool isPublic,
        IReadOnlyList<CheckedParameter> parameters,
        AlType returnType,
        IReadOnlyList<TypedStmt> body,
        Token at)
    {
        Id = id;
        Name = name;
        Public = isPublic;
        Parameters = ReadOnly(parameters);
        ReturnType = returnType;
        Body = ReadOnly(body);
        At = at;
    }

    public int Id { get; }
    public string Name { get; }
    public bool Public { get; }
    public IReadOnlyList<CheckedParameter> Parameters { get; }
    public AlType ReturnType { get; }
    internal IReadOnlyList<TypedStmt> Body { get; }
    internal Token At { get; }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());
}

internal sealed class CheckedProgram
{
    internal CheckedProgram(string module, IEnumerable<CheckedFunction> functions, IEnumerable<CheckedUnion> unions)
    {
        Module = module;
        Functions = Array.AsReadOnly(functions.ToArray());
        Unions = Array.AsReadOnly(unions.ToArray());
    }

    public string Module { get; }
    public IReadOnlyList<CheckedFunction> Functions { get; }
    public IReadOnlyList<CheckedUnion> Unions { get; }
}

internal sealed record CheckResult(CheckedProgram? Program, List<Diagnostic> Diagnostics);

internal sealed record VariantShape(
    string Name,
    string Key,
    int? UnionId,
    int VariantId,
    BuiltinVariant? Builtin,
    IReadOnlyList<AlType> PayloadTypes);

internal static class Compiler
{
    public static CheckResult Check(string file, string source)
    {
        var diagnostics = new List<Diagnostic>();
        var tokens = Lexer.Scan(source, file, diagnostics);
        if (diagnostics.Count != 0) return new CheckResult(null, diagnostics);

        var parsed = new Parser(tokens, file, diagnostics).Parse();
        if (parsed is null || diagnostics.Count != 0) return new CheckResult(null, diagnostics);

        return new SemanticChecker(file, diagnostics).Check(parsed);
    }
}

internal sealed class SemanticChecker(string file, List<Diagnostic> diagnostics)
{
    private const int MaximumSemanticDepth = 192;
    private readonly Dictionary<string, UnionSymbol> _unionsByName = new(StringComparer.Ordinal);
    private readonly List<UnionSymbol> _unions = [];
    private readonly Dictionary<string, FunctionSymbol> _functionsByName = new(StringComparer.Ordinal);
    private readonly List<FunctionSymbol> _functions = [];
    private bool _semanticDepthReported;

    public CheckResult Check(ParsedProgram program)
    {
        RegisterUnionHeaders(program.Unions);
        PopulateUnionVariants(program.Unions);
        RegisterFunctions(program.Functions);
        ValidatePublicSignatures();

        foreach (var function in _functions)
            CheckFunctionBody(function);



        if (diagnostics.Count != 0) return new CheckResult(null, diagnostics);

        var unions = _unions.Select(symbol => new CheckedUnion(
            symbol.Id,
            symbol.Declaration.Name,
            symbol.Declaration.Public,
            symbol.Type,
            ReadOnly(symbol.Variants),
            symbol.Declaration.At));
        var functions = _functions.Select(symbol => symbol.CheckedFunction!);
        return new CheckResult(new CheckedProgram(program.Module, functions, unions), diagnostics);
    }

    private void RegisterUnionHeaders(IReadOnlyList<UnionDecl> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (declaration.Name is "i32" or "bool" or "Text" or "Option" or "Result")
            {
                Add("E_NAME_DUPLICATE", $"Type name '{declaration.Name}' is reserved", declaration.At);
                continue;
            }

            if (_unionsByName.ContainsKey(declaration.Name))
            {
                Add("E_NAME_DUPLICATE", $"Union '{declaration.Name}' is already declared", declaration.At);
                continue;
            }

            var symbol = new UnionSymbol(_unions.Count, declaration, AlType.ForUnion(_unions.Count, declaration.Name));
            _unions.Add(symbol);
            _unionsByName.Add(declaration.Name, symbol);
        }
    }

    private void PopulateUnionVariants(IReadOnlyList<UnionDecl> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (!_unionsByName.TryGetValue(declaration.Name, out var symbol) || symbol.Declaration != declaration)
                continue;
            if (declaration.Variants.Count == 0)
                Add("E_TYPE_MISMATCH", $"Union '{declaration.Name}' must declare at least one variant", declaration.At);

            var variantsByName = new HashSet<string>(StringComparer.Ordinal);
            foreach (var variant in declaration.Variants)
            {
                if (!variantsByName.Add(variant.Name))
                {
                    Add("E_NAME_DUPLICATE", $"Variant '{variant.Name}' is already declared on union '{declaration.Name}'", variant.At);
                    continue;
                }

                var fields = new List<CheckedVariantField>();
                var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                for (var fieldIndex = 0; fieldIndex < variant.Fields.Count; fieldIndex++)
                {
                    var field = variant.Fields[fieldIndex];
                    if (field.Name is not null && !fieldNames.Add(field.Name))
                        Add("E_NAME_DUPLICATE", $"Payload field '{field.Name}' is already declared on variant '{variant.Name}'", field.At);

                    fields.Add(new CheckedVariantField(field.Name, ResolveType(field.Type, 0), fieldIndex, field.At));
                }

                symbol.Variants.Add(new CheckedVariant(
                    symbol.Variants.Count,
                    variant.Name,
                    ReadOnly(fields),
                    variant.At));
            }
        }
    }

    private void RegisterFunctions(IReadOnlyList<FunctionDecl> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (_functionsByName.ContainsKey(declaration.Name))
            {
                Add("E_NAME_DUPLICATE", $"Function '{declaration.Name}' is already declared", declaration.At);
                continue;
            }

            var parameters = new List<CheckedParameter>();
            var localNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < declaration.Parameters.Count; i++)
            {
                var parameter = declaration.Parameters[i];
                if (!localNames.Add(parameter.Name))
                    Add("E_NAME_DUPLICATE", $"Parameter '{parameter.Name}' is already declared", parameter.At);
                parameters.Add(new CheckedParameter(parameter.Name, ResolveType(parameter.Type, 0), i, parameter.At));
            }

            var symbol = new FunctionSymbol(
                _functions.Count,
                declaration,
                ReadOnly(parameters),
                ResolveType(declaration.ReturnType, 0));
            _functions.Add(symbol);
            _functionsByName.Add(declaration.Name, symbol);
        }
    }

    private void ValidatePublicSignatures()
    {
        foreach (var function in _functions)
        {
            if (!function.Declaration.Public) continue;
            foreach (var parameter in function.Parameters)
                CheckPublicTypeVisibility(parameter.Type, function.Declaration.Name, parameter.At);
            CheckPublicTypeVisibility(function.ReturnType, function.Declaration.Name, function.Declaration.At);
        }

        foreach (var union in _unions)
        {
            if (!union.Declaration.Public) continue;
            foreach (var variant in union.Variants)
            foreach (var field in variant.Fields)
                CheckPublicTypeVisibility(field.Type, union.Declaration.Name, field.At);
        }
    }

    private void CheckPublicTypeVisibility(AlType type, string owner, Token at)
    {
        if (type.IsError) return;
        if (type.Kind == AlTypeKind.Union &&
            _unions[type.UnionId].Declaration.Public == false)
        {
            Add("E_TYPE_VISIBILITY", $"Public declaration '{owner}' exposes private type '{type.DisplayName}'", at);
            return;
        }

        foreach (var argument in type.Arguments)
            CheckPublicTypeVisibility(argument, owner, at);
    }

    private void CheckFunctionBody(FunctionSymbol function)
    {
        _nextLocalId = function.Parameters.Count;
        var locals = new Dictionary<string, LocalSymbol>(StringComparer.Ordinal);
        foreach (var parameter in function.Parameters)
        {
            if (!locals.ContainsKey(parameter.Name))
                locals.Add(parameter.Name, new LocalSymbol(parameter.LocalId, parameter.Type));
        }

        var body = new List<TypedStmt>();
        foreach (var statement in function.Declaration.Body)
        {
            switch (statement)
            {
                case LetStmt let:
                {
                    var localType = ResolveType(let.Type, 0);
                    var value = CheckExpr(let.Value, localType, locals, 0);
                    var id = _nextLocalId++;
                    if (locals.ContainsKey(let.Name))
                    {
                        Add("E_NAME_DUPLICATE", $"Local '{let.Name}' is already declared in this scope", let.At);
                    }
                    else
                    {
                        locals.Add(let.Name, new LocalSymbol(id, localType));
                    }
                    body.Add(new TypedLetStmt(id, let.Name, localType, value, let.At));
                    break;
                }
                case ReturnStmt ret:
                {
                    var value = CheckExpr(ret.Value, function.ReturnType, locals, 0);
                    body.Add(new TypedReturnStmt(value, ret.At));
                    break;
                }
                default:
                    Add("E_UNSUPPORTED", "Statement is not implemented in this language slice", statement.At);
                    break;
            }
        }

        if (function.Declaration.Body.Count == 0 || function.Declaration.Body[^1] is not ReturnStmt)
            Add("E_TYPE_MISMATCH", "Function must end with a return value", function.Declaration.At);

        function.CheckedFunction = new CheckedFunction(
            function.Id,
            function.Declaration.Name,
            function.Declaration.Public,
            function.Parameters,
            function.ReturnType,
            ReadOnly(body),
            function.Declaration.At);
    }

    private TypedExpr CheckExpr(
        Expr expression,
        AlType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (depth >= MaximumSemanticDepth)
        {
            if (!_semanticDepthReported)
            {
                Add("E_TYPE_MISMATCH", "Expression nesting exceeds the semantic checker limit", expression.At);
                _semanticDepthReported = true;
            }
            return new TypedErrorExpr(expression.At);
        }

        TypedExpr result = expression switch
        {
            NumberExpr number => new TypedNumberExpr(number.At, number.Value),
            BoolExpr boolean => new TypedBoolExpr(boolean.At, boolean.Value),
            TextExpr text => new TypedTextExpr(text.At, text.Value),
            NameExpr name => CheckName(name, expected, locals),
            BinaryExpr binary => CheckBinary(binary, locals, depth + 1),
            CallExpr call => CheckCall(call, expected, locals, depth + 1),
            VariantExpr variant => CheckVariantConstruction(variant, locals, depth + 1),
            MatchExpr match => CheckMatch(match, expected, locals, depth + 1),
            _ => UnsupportedExpr(expression)
        };

        if (expected is not null && !expected.IsError && !result.Type.IsError && result.Type != expected)
            AddMismatch(expected, result.Type, expression.At);
        return result;
    }

    private TypedExpr CheckName(NameExpr expression, AlType? expected, Dictionary<string, LocalSymbol> locals)
    {
        if (locals.TryGetValue(expression.Name, out var local))
            return new TypedLocalExpr(local.Type, local.Id, expression.At);

        if (expression.Name == "null")
        {
            Add("E_TYPE_MISMATCH", "The null literal is not supported; use Option<T>", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Name == "None")
        {
            if (expected is null)
            {
                Add("E_TYPE_MISMATCH", "Constructor 'None' requires an expected type of Option<T>", expression.At);
                return new TypedErrorExpr(expression.At);
            }
            if (expected.IsError) return new TypedErrorExpr(expression.At);
            if (expected.Kind != AlTypeKind.Option)
            {
                AddMismatch(expected, "Option<T>", expression.At);
                return new TypedErrorExpr(expression.At);
            }
            return new TypedBuiltinConstructExpr(expected, BuiltinVariant.None, [], expression.At);
        }

        Add("E_NAME_UNRESOLVED", $"Name '{expression.Name}' is not in scope", expression.At);
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckBinary(BinaryExpr expression, Dictionary<string, LocalSymbol> locals, int depth)
    {
        if (expression.Op is not ("+" or "-" or "*"))
        {
            Add("E_UNSUPPORTED", $"Arithmetic operator '{expression.Op}' is not implemented", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var left = CheckExpr(expression.Left, AlType.I32, locals, depth);
        var right = CheckExpr(expression.Right, AlType.I32, locals, depth);
        return new TypedBinaryExpr(AlType.I32, expression.Op, left, right, expression.At);
    }

    private TypedExpr CheckCall(
        CallExpr expression,
        AlType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (_functionsByName.TryGetValue(expression.Name, out var function))
        {
            if (expression.Arguments.Count != function.Parameters.Count)
                Add("E_TYPE_MISMATCH", $"Function '{expression.Name}' expects {function.Parameters.Count} arguments, got {expression.Arguments.Count}", expression.At);

            var arguments = new List<TypedExpr>();
            for (var i = 0; i < expression.Arguments.Count; i++)
            {
                var argumentType = i < function.Parameters.Count ? function.Parameters[i].Type : null;
                arguments.Add(CheckExpr(expression.Arguments[i], argumentType, locals, depth));
            }
            return new TypedCallExpr(function.ReturnType, function.Id, ReadOnly(arguments), expression.At);
        }

        if (expression.Name is "Some" or "Ok" or "Err")
            return CheckBuiltinCall(expression, expected, locals, depth);

        foreach (var argument in expression.Arguments)
            _ = CheckExpr(argument, null, locals, depth);
        Add("E_NAME_UNRESOLVED", $"Function '{expression.Name}' is not declared", expression.At);
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckBuiltinCall(
        CallExpr expression,
        AlType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var wantsOption = expression.Name == "Some";
        var wantsOk = expression.Name == "Ok";
        var expectedKind = wantsOption ? AlTypeKind.Option : AlTypeKind.Result;
        var expectedName = wantsOption ? "Option<T>" : "Result<T, E>";
        var matchingContext = expected is not null && (expected.IsError || expected.Kind == expectedKind);

        if (expression.Arguments.Count != 1)
            Add("E_TYPE_MISMATCH", $"Constructor '{expression.Name}' expects 1 argument, got {expression.Arguments.Count}", expression.At);

        if (expected is null)
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_TYPE_MISMATCH", $"Constructor '{expression.Name}' requires an expected type of {expectedName}", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (expected.IsError) return new TypedErrorExpr(expression.At);

        if (!matchingContext)
        {
            var value = expression.Arguments.Count == 1
                ? CheckExpr(expression.Arguments[0], null, locals, depth)
                : null;
            for (var i = 1; i < expression.Arguments.Count; i++)
                _ = CheckExpr(expression.Arguments[i], null, locals, depth);

            if (value is null || value.Type.IsError)
            {
                AddMismatch(expected, wantsOption ? "Option<T>" : "Result<T, E>", expression.At);
            }
            else
            {
                var actualType = wantsOption
                    ? AlType.Option(value.Type)
                    : wantsOk
                        ? AlType.Result(value.Type, AlType.Error)
                        : AlType.Result(AlType.Error, value.Type);
                AddMismatch(expected, actualType, expression.At);
            }
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Arguments.Count == 0)
            return new TypedBuiltinConstructExpr(expected, wantsOption ? BuiltinVariant.Some : wantsOk ? BuiltinVariant.Ok : BuiltinVariant.Err, [], expression.At);

        var payloadType = wantsOption || wantsOk ? expected.Arguments[0] : expected.Arguments[1];
        var payload = CheckExpr(expression.Arguments[0], payloadType, locals, depth);
        for (var i = 1; i < expression.Arguments.Count; i++)
            _ = CheckExpr(expression.Arguments[i], null, locals, depth);

        var variant = wantsOption ? BuiltinVariant.Some : wantsOk ? BuiltinVariant.Ok : BuiltinVariant.Err;
        return new TypedBuiltinConstructExpr(expected, variant, ReadOnly([payload]), expression.At);
    }

    private TypedExpr CheckVariantConstruction(
        VariantExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (!_unionsByName.TryGetValue(expression.UnionName, out var union))
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_NAME_UNRESOLVED", $"Union '{expression.UnionName}' is not declared", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var variant = union.Variants.FirstOrDefault(item => item.Name == expression.VariantName);
        if (variant is null)
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_NAME_UNRESOLVED", $"Variant '{expression.VariantName}' is not declared on union '{expression.UnionName}'", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Arguments.Count != variant.Fields.Count)
            Add("E_TYPE_MISMATCH", $"Variant '{expression.UnionName}.{variant.Name}' expects {variant.Fields.Count} payload values, got {expression.Arguments.Count}", expression.At);

        var arguments = new List<TypedExpr>();
        for (var i = 0; i < expression.Arguments.Count; i++)
        {
            var expected = i < variant.Fields.Count ? variant.Fields[i].Type : null;
            arguments.Add(CheckExpr(expression.Arguments[i], expected, locals, depth));
        }
        return new TypedUnionConstructExpr(union.Type, union.Id, variant.Id, ReadOnly(arguments), expression.At);
    }

    private TypedExpr CheckMatch(
        MatchExpr expression,
        AlType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var value = CheckExpr(expression.Value, null, locals, depth);
        var shapes = GetVariantShapes(value.Type);
        if (shapes is null)
            Add("E_TYPE_MISMATCH", $"Match requires a tagged union value, found '{value.Type.DisplayName}'", expression.Value.At);

        var arms = new List<TypedMatchArm>();
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var wildcardSeen = false;
        AlType? inferredResult = expected;

        foreach (var arm in expression.Arms)
        {
            TypedPattern typedPattern;
            var armLocals = new Dictionary<string, LocalSymbol>(locals, StringComparer.Ordinal);
            if (arm.Pattern is WildcardPattern wildcard)
            {
                if (wildcardSeen)
                    Add("E_MATCH_ARM_DUPLICATE", "Wildcard arm is duplicated", wildcard.At);
                wildcardSeen = true;
                typedPattern = new TypedWildcardPattern(wildcard.At);
            }
            else if (arm.Pattern is VariantPattern variantPattern)
            {
                if (wildcardSeen)
                    Add("E_MATCH_ARM_DUPLICATE", "Match arm is unreachable after a wildcard", variantPattern.At);

                var shape = shapes is null ? null : ResolvePatternShape(variantPattern, value.Type, shapes);
                var bindings = new List<BoundLocal>();
                if (shape is not null)
                {
                    if (!covered.Add(shape.Key))
                        Add("E_MATCH_ARM_DUPLICATE", $"Variant arm '{shape.Name}' is duplicated", variantPattern.At);
                    if (variantPattern.Bindings.Count != shape.PayloadTypes.Count)
                        Add("E_TYPE_MISMATCH", $"Pattern '{shape.Name}' expects {shape.PayloadTypes.Count} bindings, got {variantPattern.Bindings.Count}", variantPattern.At);

                    var boundNames = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < variantPattern.Bindings.Count; i++)
                    {
                        var name = variantPattern.Bindings[i];
                        var payloadType = i < shape.PayloadTypes.Count ? shape.PayloadTypes[i] : AlType.Error;
                        if (!boundNames.Add(name))
                            Add("E_NAME_DUPLICATE", $"Pattern binding '{name}' is duplicated", variantPattern.At);
                        var bound = new BoundLocal(name, _nextLocalId++, payloadType, variantPattern.At);
                        bindings.Add(bound);
                        armLocals[name] = new LocalSymbol(bound.LocalId, bound.Type);
                    }
                    typedPattern = new TypedVariantPattern(shape, ReadOnly(bindings), variantPattern.At);
                }
                else
                {
                    typedPattern = new TypedWildcardPattern(variantPattern.At);
                }
            }
            else
            {
                Add("E_UNSUPPORTED", "Match pattern is not implemented", arm.Pattern.At);
                typedPattern = new TypedWildcardPattern(arm.Pattern.At);
            }

            var armExpected = inferredResult;
            var body = CheckExpr(arm.Body, armExpected, armLocals, depth);
            if (inferredResult is null && !body.Type.IsError)
                inferredResult = body.Type;
            arms.Add(new TypedMatchArm(typedPattern, body, arm.At));
        }

        if (shapes is not null && !wildcardSeen)
        {
            var missing = shapes.Where(shape => !covered.Contains(shape.Key)).Select(shape => shape.Name).ToArray();
            if (missing.Length != 0)
                diagnostics.Add(new Diagnostic(
                    "E_MATCH_NONEXHAUSTIVE",
                    $"Match is missing variants: {string.Join(", ", missing)}",
                    file,
                    expression.At.Range));
        }

        return new TypedMatchExpr(inferredResult ?? AlType.Error, value, ReadOnly(arms), expression.At);
    }

    private IReadOnlyList<VariantShape>? GetVariantShapes(AlType type)
    {
        if (type.IsError) return null;
        if (type.Kind == AlTypeKind.Union)
        {
            var union = _unions[type.UnionId];
            return ReadOnly(union.Variants.Select(variant => new VariantShape(
                $"{union.Declaration.Name}.{variant.Name}",
                $"u:{union.Id}:{variant.Id}",
                union.Id,
                variant.Id,
                null,
                ReadOnly(variant.Fields.Select(field => field.Type)))));
        }
        if (type.Kind == AlTypeKind.Option)
        {
            return ReadOnly<VariantShape>([
                new("Some", "option:Some", null, 0, BuiltinVariant.Some, ReadOnly([type.Arguments[0]])),
                new("None", "option:None", null, 1, BuiltinVariant.None, [])
            ]);
        }
        if (type.Kind == AlTypeKind.Result)
        {
            return ReadOnly<VariantShape>([
                new("Ok", "result:Ok", null, 0, BuiltinVariant.Ok, ReadOnly([type.Arguments[0]])),
                new("Err", "result:Err", null, 1, BuiltinVariant.Err, ReadOnly([type.Arguments[1]]))
            ]);
        }
        return null;
    }

    private VariantShape? ResolvePatternShape(
        VariantPattern pattern,
        AlType scrutineeType,
        IReadOnlyList<VariantShape> shapes)
    {
        if (scrutineeType.Kind == AlTypeKind.Union)
        {
            var union = _unions[scrutineeType.UnionId];
            if (pattern.UnionName != union.Declaration.Name)
            {
                var expectedName = union.Declaration.Name + ".<variant>";
                var actualName = pattern.UnionName is null ? pattern.VariantName : pattern.UnionName + "." + pattern.VariantName;
                Add("E_TYPE_MISMATCH", $"Expected pattern from '{expectedName}', found '{actualName}'", pattern.At);
                return null;
            }
        }
        else if (pattern.UnionName is not null)
        {
            Add("E_TYPE_MISMATCH", $"Qualified pattern '{pattern.UnionName}.{pattern.VariantName}' does not match '{scrutineeType.DisplayName}'", pattern.At);
            return null;
        }

        var shape = shapes.FirstOrDefault(item => item.Name == (scrutineeType.Kind == AlTypeKind.Union
            ? $"{pattern.UnionName}.{pattern.VariantName}"
            : pattern.VariantName));
        if (shape is null)
        {
            Add("E_NAME_UNRESOLVED", $"Variant '{pattern.VariantName}' is not valid for '{scrutineeType.DisplayName}'", pattern.At);
            return null;
        }
        return shape;
    }

    private AlType ResolveType(TypeSyntax syntax, int depth)
    {
        if (depth >= MaximumSemanticDepth)
        {
            if (!_semanticDepthReported)
            {
                Add("E_TYPE_MISMATCH", "Type nesting exceeds the semantic checker limit", syntax.At);
                _semanticDepthReported = true;
            }
            return AlType.Error;
        }

        switch (syntax.Name)
        {
            case "i32":
                return NoTypeArguments(syntax, AlType.I32);
            case "bool":
                return NoTypeArguments(syntax, AlType.Bool);
            case "Text":
                return NoTypeArguments(syntax, AlType.Text);
            case "Option":
                if (syntax.Args.Count != 1)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'Option' expects 1 type argument, got {syntax.Args.Count}", syntax.At);
                    return AlType.Error;
                }
                return AlType.Option(ResolveType(syntax.Args[0], depth + 1));
            case "Result":
                if (syntax.Args.Count != 2)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'Result' expects 2 type arguments, got {syntax.Args.Count}", syntax.At);
                    return AlType.Error;
                }
                return AlType.Result(ResolveType(syntax.Args[0], depth + 1), ResolveType(syntax.Args[1], depth + 1));
            default:
                if (_unionsByName.TryGetValue(syntax.Name, out var union))
                {
                    if (syntax.Args.Count != 0)
                    {
                        Add("E_TYPE_MISMATCH", $"Union type '{syntax.Name}' does not take type arguments", syntax.At);
                        return AlType.Error;
                    }
                    return union.Type;
                }
                Add("E_NAME_UNRESOLVED", $"Type '{syntax.Name}' is not declared", syntax.At);
                return AlType.Error;
        }
    }

    private AlType NoTypeArguments(TypeSyntax syntax, AlType type)
    {
        if (syntax.Args.Count != 0)
        {
            Add("E_TYPE_MISMATCH", $"Type '{syntax.Name}' does not take type arguments", syntax.At);
            return AlType.Error;
        }
        return type;
    }


    private void AddMismatch(AlType expected, AlType actual, Token at) =>
        Add("E_TYPE_MISMATCH", $"Expected '{expected.DisplayName}', found '{actual.DisplayName}'", at);


    private void AddMismatch(AlType expected, string actual, Token at) =>
        Add("E_TYPE_MISMATCH", $"Expected '{expected.DisplayName}', found '{actual}'", at);
    private void Add(string code, string message, Token at) =>
        diagnostics.Add(new Diagnostic(code, message, file, at.Range));

    private TypedExpr UnsupportedExpr(Expr expression) { Add("E_UNSUPPORTED", "Expression is not implemented in this language slice", expression.At); return new TypedErrorExpr(expression.At); }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());

    private sealed class UnionSymbol(int id, UnionDecl declaration, AlType type)
    {
        public int Id { get; } = id;
        public UnionDecl Declaration { get; } = declaration;
        public AlType Type { get; } = type;
        public List<CheckedVariant> Variants { get; } = [];
    }

    private sealed class FunctionSymbol(
        int id,
        FunctionDecl declaration,
        IReadOnlyList<CheckedParameter> parameters,
        AlType returnType)
    {
        public int Id { get; } = id;
        public FunctionDecl Declaration { get; } = declaration;
        public IReadOnlyList<CheckedParameter> Parameters { get; } = parameters;
        public AlType ReturnType { get; } = returnType;
        public CheckedFunction? CheckedFunction { get; set; }
    }

    private sealed record LocalSymbol(int Id, AlType Type);

    private int _nextLocalId;
}

internal sealed record TypedErrorExpr(Token At) : TypedExpr(AlType.Error, At);
