using System.Collections.ObjectModel;

internal enum LangTypeKind
{
    Error,
    I32,
    Bool,
    Text,
    Union,
    Struct,
    Option,
    Result,
    FsRead,
    FsError
}

internal sealed class LangType : IEquatable<LangType>
{
    private readonly ReadOnlyCollection<LangType> _arguments;

    private LangType(
        LangTypeKind kind,
        string displayName,
        int unionId = -1,
        int structId = -1,
        IEnumerable<LangType>? arguments = null)
    {
        Kind = kind;
        DisplayName = displayName;
        UnionId = unionId;
        StructId = structId;
        _arguments = Array.AsReadOnly((arguments ?? []).ToArray());
    }

    public LangTypeKind Kind { get; }
    public string DisplayName { get; }
    public IReadOnlyList<LangType> Arguments => _arguments;
    public bool IsI32 => Kind == LangTypeKind.I32;
    public bool IsBool => Kind == LangTypeKind.Bool;
    public bool IsText => Kind == LangTypeKind.Text;
    public bool IsFsRead => Kind == LangTypeKind.FsRead;
    public bool IsFsError => Kind == LangTypeKind.FsError;
    internal int UnionId { get; }
    internal int StructId { get; }
    internal bool IsError => Kind == LangTypeKind.Error;

    internal static LangType Error { get; } = new(LangTypeKind.Error, "<error>");
    internal static LangType I32 { get; } = new(LangTypeKind.I32, "i32");
    internal static LangType Bool { get; } = new(LangTypeKind.Bool, "bool");
    internal static LangType Text { get; } = new(LangTypeKind.Text, "Text");
    internal static LangType FsRead { get; } = new(LangTypeKind.FsRead, "FsRead");
    internal static LangType FsError { get; } = new(LangTypeKind.FsError, "FsError");

    internal static LangType ForUnion(int unionId, string name) => new(LangTypeKind.Union, name, unionId);
    internal static LangType ForStruct(int structId, string name) => new(LangTypeKind.Struct, name, structId: structId);
    internal static LangType Option(LangType item) => new(LangTypeKind.Option, $"Option<{item.DisplayName}>", arguments: [item]);
    internal static LangType Result(LangType ok, LangType error) => new(LangTypeKind.Result, $"Result<{ok.DisplayName}, {error.DisplayName}>", arguments: [ok, error]);

    public bool Equals(LangType? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || Kind != other.Kind) return false;
        if (Kind == LangTypeKind.Union) return UnionId == other.UnionId;
        if (Kind == LangTypeKind.Struct) return StructId == other.StructId;
        if (_arguments.Count != other._arguments.Count) return false;
        for (var i = 0; i < _arguments.Count; i++)
            if (!_arguments[i].Equals(other._arguments[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is LangType other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        if (Kind == LangTypeKind.Union)
        {
            hash.Add(UnionId);
        }
        else if (Kind == LangTypeKind.Struct)
        {
            hash.Add(StructId);
        }
        else
        {
            foreach (var argument in _arguments) hash.Add(argument);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(LangType? left, LangType? right) => Equals(left, right);
    public static bool operator !=(LangType? left, LangType? right) => !Equals(left, right);
}

internal sealed record CheckedVariantField(string? Name, LangType Type, int Index, Token At);
internal sealed record CheckedVariant(int Id, string Name, IReadOnlyList<CheckedVariantField> Fields, Token At);
internal sealed record CheckedUnion(int Id, string Name, bool Public, LangType Type, IReadOnlyList<CheckedVariant> Variants, Token At);
internal sealed record CheckedStructField(string Name, LangType Type, int Index, Token At);
internal sealed record CheckedStruct(int Id, string Name, bool Public, LangType Type, IReadOnlyList<CheckedStructField> Fields, Token At);
internal sealed record CheckedParameter(string Name, LangType Type, int LocalId, Token At);

internal abstract record TypedExpr(LangType Type, Token At);
internal sealed record TypedNumberExpr(Token At, int Value) : TypedExpr(LangType.I32, At);
internal sealed record TypedBoolExpr(Token At, bool Value) : TypedExpr(LangType.Bool, At);
internal sealed record TypedTextExpr(Token At, string Value) : TypedExpr(LangType.Text, At);
internal sealed record TypedLocalExpr(LangType Type, int LocalId, Token At) : TypedExpr(Type, At);
internal sealed record TypedBinaryExpr(LangType Type, string Op, TypedExpr Left, TypedExpr Right, Token At) : TypedExpr(Type, At);
internal sealed record TypedCompareExpr(string Op, TypedExpr Left, TypedExpr Right, Token At) : TypedExpr(LangType.Bool, At);
internal sealed record TypedTextLengthExpr(TypedExpr Target, Token At) : TypedExpr(LangType.I32, At);
internal sealed record TypedTextTrimExpr(TypedExpr Target, Token At) : TypedExpr(LangType.Text, At);
internal sealed record TypedCallExpr(LangType Type, int FunctionId, IReadOnlyList<TypedExpr> Arguments, Token At) : TypedExpr(Type, At);

internal enum BuiltinIntrinsic
{
    FsReadText
}

internal sealed record TypedIntrinsicCallExpr(
    LangType Type,
    BuiltinIntrinsic Intrinsic,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);

internal enum BuiltinVariant
{
    Some,
    None,
    Ok,
    Err,
    FsErrorNotFound,
    FsErrorPermissionDenied,
    FsErrorInvalidPath,
    FsErrorInvalidText,
    FsErrorIo
}

internal sealed record TypedBuiltinConstructExpr(
    LangType Type,
    BuiltinVariant Variant,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);

internal sealed record TypedUnionConstructExpr(
    LangType Type,
    int UnionId,
    int VariantId,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);

internal sealed record TypedStructFieldValue(int FieldIndex, TypedExpr Value);
internal sealed record TypedStructConstructExpr(
    LangType Type,
    int StructId,
    IReadOnlyList<TypedStructFieldValue> Fields,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedFieldAccessExpr(
    LangType Type,
    TypedExpr Target,
    int FieldIndex,
    Token At) : TypedExpr(Type, At);

internal abstract record TypedPattern(Token At);
internal sealed record TypedWildcardPattern(Token At) : TypedPattern(At);
internal sealed record BoundLocal(string Name, int LocalId, LangType Type, Token At);
internal sealed record TypedVariantPattern(
    VariantShape Shape,
    IReadOnlyList<BoundLocal> Bindings,
    Token At) : TypedPattern(At);
internal sealed record TypedMatchArm(TypedPattern Pattern, TypedExpr Body, Token At);
internal sealed record TypedMatchExpr(
    LangType Type,
    TypedExpr Value,
    IReadOnlyList<TypedMatchArm> Arms,
    Token At) : TypedExpr(Type, At);

internal abstract record TypedStmt(Token At);
internal sealed record TypedLetStmt(int LocalId, string Name, LangType Type, TypedExpr Value, Token At) : TypedStmt(At);
internal sealed record TypedReturnStmt(TypedExpr Value, Token At) : TypedStmt(At);
internal sealed record TypedIfStmt(
    TypedExpr Condition,
    IReadOnlyList<TypedStmt> ThenBody,
    IReadOnlyList<TypedStmt>? ElseBody,
    Token At) : TypedStmt(At);

internal sealed class CheckedFunction
{
    internal CheckedFunction(
        int id,
        string module,
        string name,
        bool isPublic,
        IReadOnlyList<CheckedParameter> parameters,
        LangType returnType,
        IReadOnlyList<TypedStmt> body,
        IReadOnlyList<string> declaredEffects,
        Token at)
    {
        Id = id;
        Module = module;
        Name = name;
        Public = isPublic;
        Parameters = ReadOnly(parameters);
        ReturnType = returnType;
        Body = ReadOnly(body);
        DeclaredEffects = ReadOnly(declaredEffects);
        InferredEffects = [];
        At = at;
    }

    public int Id { get; }
    public string Module { get; }
    public string Name { get; }
    public bool Public { get; }
    public IReadOnlyList<CheckedParameter> Parameters { get; }
    public LangType ReturnType { get; }
    public IReadOnlyList<string> DeclaredEffects { get; }
    public IReadOnlyList<string> InferredEffects { get; internal set; }
    internal IReadOnlyList<TypedStmt> Body { get; }
    internal Token At { get; }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());
}

internal sealed class CheckedProgram
{
    internal CheckedProgram(
        IReadOnlyList<string> modules,
        string? entryModule,
        int? entryFunctionId,
        IEnumerable<CheckedFunction> functions,
        IEnumerable<CheckedUnion> unions,
        IEnumerable<CheckedStruct> structs)
    {
        Modules = Array.AsReadOnly(modules.ToArray());
        EntryModule = entryModule;
        EntryFunctionId = entryFunctionId;
        Functions = Array.AsReadOnly(functions.ToArray());
        Unions = Array.AsReadOnly(unions.ToArray());
        Structs = Array.AsReadOnly(structs.ToArray());
    }

    // Retained for single-file API compatibility. For a package, this is the selected entry module.
    public string Module => EntryModule ?? (Modules.Count == 1 ? Modules[0] : string.Empty);
    public IReadOnlyList<string> Modules { get; }
    public string? EntryModule { get; }
    public int? EntryFunctionId { get; }
    public IReadOnlyList<CheckedFunction> Functions { get; }
    public IReadOnlyList<CheckedUnion> Unions { get; }
    public IReadOnlyList<CheckedStruct> Structs { get; }
}

internal sealed record CheckResult(CheckedProgram? Program, List<Diagnostic> Diagnostics);

internal sealed record VariantShape(
    string Name,
    string Key,
    int? UnionId,
    int VariantId,
    BuiltinVariant? Builtin,
    IReadOnlyList<LangType> PayloadTypes);

internal static class Compiler
{
    public static CheckResult Check(string file, string source)
    {
        var diagnostics = new List<Diagnostic>();
        var tokens = Lexer.Scan(source, file, diagnostics);
        if (diagnostics.Count != 0) return new CheckResult(null, diagnostics);

        var parsed = new Parser(tokens, file, diagnostics).Parse();
        if (parsed is null || diagnostics.Count != 0) return new CheckResult(null, diagnostics);

        return new SemanticChecker(diagnostics).CheckSingle(parsed);
    }

    public static CheckResult CheckPackage(IReadOnlyList<ParsedProgram> modules, string? entryModule)
    {
        var diagnostics = new List<Diagnostic>();
        return new SemanticChecker(diagnostics).CheckPackage(modules, entryModule, requireEntry: entryModule is not null);
    }
}

internal sealed class SemanticChecker(List<Diagnostic> diagnostics)
{
    private const int MaximumSemanticDepth = 192;
    private static readonly string[] EffectVocabulary =
    [
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
    ];
    private readonly Dictionary<(string Module, string Name), UnionSymbol> _unionsByModuleAndName = new();
    private readonly List<UnionSymbol> _unions = [];
    private readonly Dictionary<(string Module, string Name), StructSymbol> _structsByModuleAndName = new();
    private readonly List<StructSymbol> _structs = [];
    private readonly Dictionary<(string Module, string Name), FunctionSymbol> _functionsByModuleAndName = new();
    private readonly List<FunctionSymbol> _functions = [];
    private readonly Dictionary<string, ModuleSymbols> _modulesByName = new(StringComparer.Ordinal);
    private string _currentModule = string.Empty;
    private FunctionSymbol? _currentFunction;
    private bool _semanticDepthReported;

    public CheckResult CheckSingle(ParsedProgram program) =>
        CheckPackage([program], program.Module, requireEntry: false);

    public CheckResult CheckPackage(
        IReadOnlyList<ParsedProgram> programs,
        string? entryModule,
        bool requireEntry)
    {
        var orderedModules = new List<ModuleSymbols>(programs.Count);
        foreach (var program in programs)
        {
            if (_modulesByName.ContainsKey(program.Module))
            {
                Add("E_MODULE_PATH", $"Module '{program.Module}' is included more than once", program.ModuleAt);
                continue;
            }
            var module = new ModuleSymbols(program);
            _modulesByName.Add(program.Module, module);
            orderedModules.Add(module);
        }

        // Register every declaration header before resolving signatures, so forward references and
        // import cycles see the same complete package symbol set.
        foreach (var module in orderedModules) RegisterUnionHeaders(module);
        foreach (var module in orderedModules) RegisterStructHeaders(module);
        foreach (var module in orderedModules) RegisterFunctionHeaders(module);
        foreach (var module in orderedModules) BindImports(module);

        foreach (var module in orderedModules) PopulateUnionVariants(module);
        foreach (var module in orderedModules) PopulateStructFields(module);
        ValidateStructRecursion();
        foreach (var module in orderedModules) RegisterFunctionSignatures(module);

        ValidatePublicSignatures();

        foreach (var function in _functions)
            CheckFunctionBody(function);

        InferEffectsAndValidateBounds();

        FunctionSymbol? entry = null;
        if (entryModule is not null)
        {
            if (_modulesByName.TryGetValue(entryModule, out var entryScope) &&
                entryScope.DeclaredFunctions.TryGetValue("main", out var main) &&
                IsRunnableEntry(main))
            {
                entry = main;
            }
            else if (requireEntry)
            {
                var at = _modulesByName.TryGetValue(entryModule, out entryScope)
                    ? entryScope.Program.ModuleAt
                    : programs.FirstOrDefault()?.ModuleAt ?? new Token("id", entryModule, 1, 1, string.Empty);
                Add("E_ENTRYPOINT", $"Entry module '{entryModule}' must declare a zero-argument main returning i32, bool, or Text", at);
            }
        }

        if (diagnostics.Count != 0) return new CheckResult(null, diagnostics);

        var unions = _unions.Select(symbol => new CheckedUnion(
            symbol.Id,
            symbol.Declaration.Name,
            symbol.Declaration.Public,
            symbol.Type,
            ReadOnly(symbol.Variants),
            symbol.Declaration.At));
        var functions = _functions.Select(symbol => symbol.CheckedFunction!);
        var structs = _structs.Select(symbol => new CheckedStruct(
            symbol.Id,
            symbol.Declaration.Name,
            symbol.Declaration.Public,
            symbol.Type,
            ReadOnly(symbol.Fields),
            symbol.Declaration.At));
        return new CheckResult(new CheckedProgram(
            orderedModules.Select(module => module.Program.Module).ToArray(),
            entry?.Module,
            entry?.Id,
            functions,
            unions,
            structs), diagnostics);
    }

    private static bool IsRunnableEntry(FunctionSymbol function) =>
        function.Declaration.Name == "main" && function.Parameters.Count == 0 &&
        (function.ReturnType.IsI32 || function.ReturnType.IsBool || function.ReturnType.IsText);

    private void RegisterUnionHeaders(ModuleSymbols module)
    {
        _currentModule = module.Program.Module;
        foreach (var declaration in module.Program.Unions)
        {
            if (IsReservedTypeName(declaration.Name))
            {
                Add("E_NAME_DUPLICATE", $"Type name '{declaration.Name}' is reserved", declaration.At);
                continue;
            }

            if (!module.TypeNames.Add(declaration.Name))
            {
                Add("E_NAME_DUPLICATE", $"Type name '{declaration.Name}' is already declared", declaration.At);
                continue;
            }

            var symbol = new UnionSymbol(_unions.Count, module.Program.Module, declaration, LangType.ForUnion(_unions.Count, declaration.Name));
            _unions.Add(symbol);
            module.DeclaredUnions.Add(declaration.Name, symbol);
            module.VisibleUnions.Add(declaration.Name, symbol);
            module.VisibleTypeNames.Add(declaration.Name);
            _unionsByModuleAndName.Add((module.Program.Module, declaration.Name), symbol);
        }
    }

    private void RegisterStructHeaders(ModuleSymbols module)
    {
        _currentModule = module.Program.Module;
        foreach (var declaration in module.Program.Structs)
        {
            if (IsReservedTypeName(declaration.Name))
            {
                Add("E_NAME_DUPLICATE", $"Type name '{declaration.Name}' is reserved", declaration.At);
                continue;
            }

            if (!module.TypeNames.Add(declaration.Name))
            {
                Add("E_NAME_DUPLICATE", $"Type name '{declaration.Name}' is already declared", declaration.At);
                continue;
            }

            var symbol = new StructSymbol(_structs.Count, module.Program.Module, declaration, LangType.ForStruct(_structs.Count, declaration.Name));
            _structs.Add(symbol);
            module.DeclaredStructs.Add(declaration.Name, symbol);
            module.VisibleStructs.Add(declaration.Name, symbol);
            module.VisibleTypeNames.Add(declaration.Name);
            _structsByModuleAndName.Add((module.Program.Module, declaration.Name), symbol);
        }
    }

    private void RegisterFunctionHeaders(ModuleSymbols module)
    {
        _currentModule = module.Program.Module;
        foreach (var declaration in module.Program.Functions)
        {
            if (module.DeclaredFunctions.ContainsKey(declaration.Name))
            {
                Add("E_NAME_DUPLICATE", $"Function '{declaration.Name}' is already declared", declaration.At);
                continue;
            }

            var symbol = new FunctionSymbol(_functions.Count, module.Program.Module, declaration);
            _functions.Add(symbol);
            module.DeclaredFunctions.Add(declaration.Name, symbol);
            module.VisibleFunctions.Add(declaration.Name, symbol);
            _functionsByModuleAndName.Add((module.Program.Module, declaration.Name), symbol);
        }
    }

    private void BindImports(ModuleSymbols module)
    {
        _currentModule = module.Program.Module;
        foreach (var import in module.Program.Imports)
        {
            if (!_modulesByName.TryGetValue(import.Module, out var target))
            {
                if (import.Symbols.Count == 0)
                    Add("E_IMPORT_UNRESOLVED", $"Module '{import.Module}' is not part of this package", import.ModuleAt);
                foreach (var imported in import.Symbols)
                    Add("E_IMPORT_UNRESOLVED", $"Module '{import.Module}' is not part of this package", imported.At);
                continue;
            }

            foreach (var imported in import.Symbols)
            {
                var found = false;
                var hasPublicMatch = false;
                if (_functionsByModuleAndName.TryGetValue((import.Module, imported.Name), out var function))
                {
                    found = true;
                    if (function.Declaration.Public)
                    {
                        hasPublicMatch = true;
                        BindImported(module.VisibleFunctions, module.DeclaredFunctions, imported, function, "function");
                    }
                }
                if (_unionsByModuleAndName.TryGetValue((import.Module, imported.Name), out var union))
                {
                    found = true;
                    if (union.Declaration.Public)
                    {
                        hasPublicMatch = true;
                        BindImportedType(module, imported, union);
                    }
                }
                if (_structsByModuleAndName.TryGetValue((import.Module, imported.Name), out var structure))
                {
                    found = true;
                    if (structure.Declaration.Public)
                    {
                        hasPublicMatch = true;
                        BindImportedType(module, imported, structure);
                    }
                }
                if (!found)
                    Add("E_IMPORT_UNRESOLVED", $"Module '{import.Module}' does not declare a function or type named '{imported.Name}'", imported.At);
                else if (!hasPublicMatch)
                    Add("E_IMPORT_PRIVATE", $"All declarations named '{imported.Name}' in module '{import.Module}' are private", imported.At);
            }
        }
    }

    private void BindImported<T>(
        Dictionary<string, T> visible,
        Dictionary<string, T> declared,
        ImportSymbol imported,
        T symbol,
        string kind)
    {
        if (declared.ContainsKey(imported.Name) || !visible.TryAdd(imported.Name, symbol))
        {
            Add("E_IMPORT_CONFLICT", $"Imported {kind} '{imported.Name}' collides with a local declaration or another import", imported.At);
        }
    }

    private void BindImportedType(ModuleSymbols module, ImportSymbol imported, UnionSymbol symbol)
    {
        if (!module.VisibleTypeNames.Add(imported.Name))
        {
            Add("E_IMPORT_CONFLICT", $"Imported type '{imported.Name}' collides with a local declaration or another import", imported.At);
            return;
        }
        module.VisibleUnions.Add(imported.Name, symbol);
    }

    private void BindImportedType(ModuleSymbols module, ImportSymbol imported, StructSymbol symbol)
    {
        if (!module.VisibleTypeNames.Add(imported.Name))
        {
            Add("E_IMPORT_CONFLICT", $"Imported type '{imported.Name}' collides with a local declaration or another import", imported.At);
            return;
        }
        module.VisibleStructs.Add(imported.Name, symbol);
    }

    private void PopulateUnionVariants(ModuleSymbols module)
    {
        _currentModule = module.Program.Module;
        foreach (var declaration in module.Program.Unions)
        {
            if (!module.DeclaredUnions.TryGetValue(declaration.Name, out var symbol) || symbol.Declaration != declaration)
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

    private void PopulateStructFields(ModuleSymbols module)
    {
        _currentModule = module.Program.Module;
        foreach (var declaration in module.Program.Structs)
        {
            if (!module.DeclaredStructs.TryGetValue(declaration.Name, out var symbol) || symbol.Declaration != declaration)
                continue;

            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            for (var fieldIndex = 0; fieldIndex < declaration.Fields.Count; fieldIndex++)
            {
                var field = declaration.Fields[fieldIndex];
                if (!fieldNames.Add(field.Name))
                    Add("E_NAME_DUPLICATE", $"Field '{field.Name}' is already declared on struct '{declaration.Name}'", field.At);

                symbol.Fields.Add(new CheckedStructField(
                    field.Name,
                    ResolveType(field.Type, 0),
                    fieldIndex,
                    field.At));
            }
        }
    }

    private void ValidateStructRecursion()
    {
        var state = new byte[_structs.Count];
        var reported = false;

        foreach (var root in _structs)
        {
            if (state[root.Id] != 0) continue;
            var stack = new Stack<(StructSymbol Symbol, int NextField)>();
            state[root.Id] = 1;
            stack.Push((root, 0));

            while (stack.Count != 0)
            {
                var frame = stack.Pop();
                if (frame.NextField >= frame.Symbol.Fields.Count)
                {
                    state[frame.Symbol.Id] = 2;
                    continue;
                }

                stack.Push((frame.Symbol, frame.NextField + 1));
                var field = frame.Symbol.Fields[frame.NextField];
                if (field.Type.Kind != LangTypeKind.Struct) continue;

                var target = _structs[field.Type.StructId];
                if (state[target.Id] == 1)
                {
                    if (!reported)
                    {
                        Add("E_TYPE_MISMATCH", "Structs cannot form cycles through only direct struct fields; use Option, Result, or a tagged union to break the cycle", field.At);
                        reported = true;
                    }
                }
                else if (state[target.Id] == 0)
                {
                    state[target.Id] = 1;
                    stack.Push((target, 0));
                }
            }
        }
    }

    private void RegisterFunctionSignatures(ModuleSymbols module)
    {
        _currentModule = module.Program.Module;
        foreach (var declaration in module.Program.Functions)
        {
            if (!module.DeclaredFunctions.TryGetValue(declaration.Name, out var symbol) || symbol.Declaration != declaration)
                continue;

            var parameters = new List<CheckedParameter>();
            var localNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < declaration.Parameters.Count; i++)
            {
                var parameter = declaration.Parameters[i];
                if (!localNames.Add(parameter.Name))
                    Add("E_NAME_DUPLICATE", $"Parameter '{parameter.Name}' is already declared", parameter.At);
                parameters.Add(new CheckedParameter(parameter.Name, ResolveType(parameter.Type, 0), i, parameter.At));
            }

            symbol.Parameters = ReadOnly(parameters);
            symbol.ReturnType = ResolveType(declaration.ReturnType, 0);
            var declaredEffects = new HashSet<string>(StringComparer.Ordinal);
            foreach (var effect in declaration.Effects)
            {
                if (!EffectVocabulary.Contains(effect.Name, StringComparer.Ordinal))
                {
                    Add("E_EFFECT_UNKNOWN", $"Unknown effect '{effect.Name}'", effect.At);
                    continue;
                }
                if (!declaredEffects.Add(effect.Name))
                    Add("E_EFFECT_DUPLICATE", $"Effect '{effect.Name}' is declared more than once", effect.At);
            }
            symbol.DeclaredEffects = EffectVocabulary.Where(declaredEffects.Contains).ToArray();
        }
    }

    private static bool IsReservedTypeName(string name) =>
        name is "i32" or "bool" or "Text" or "Option" or "Result" or "FsRead" or "FsError";

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

        foreach (var structure in _structs)
        {
            if (!structure.Declaration.Public) continue;
            foreach (var field in structure.Fields)
                CheckPublicTypeVisibility(field.Type, structure.Declaration.Name, field.At);
        }
    }

    private void CheckPublicTypeVisibility(LangType type, string owner, Token at)
    {
        if (type.IsError) return;
        if (type.Kind == LangTypeKind.Union &&
            _unions[type.UnionId].Declaration.Public == false)
        {
            Add("E_TYPE_VISIBILITY", $"Public declaration '{owner}' exposes private type '{type.DisplayName}'", at);
            return;
        }
        if (type.Kind == LangTypeKind.Struct &&
            _structs[type.StructId].Declaration.Public == false)
        {
            Add("E_TYPE_VISIBILITY", $"Public declaration '{owner}' exposes private type '{type.DisplayName}'", at);
            return;
        }

        foreach (var argument in type.Arguments)
            CheckPublicTypeVisibility(argument, owner, at);
    }

    private void CheckFunctionBody(FunctionSymbol function)
    {
        _currentModule = function.Module;
        _currentFunction = function;
        _nextLocalId = function.Parameters.Count;
        var locals = new Dictionary<string, LocalSymbol>(StringComparer.Ordinal);
        foreach (var parameter in function.Parameters)
        {
            if (!locals.ContainsKey(parameter.Name))
                locals.Add(parameter.Name, new LocalSymbol(parameter.LocalId, parameter.Type));
        }

        var body = new List<TypedStmt>();
        var guaranteesReturn = CheckStatements(function.Declaration.Body, body, locals);

        if (!guaranteesReturn)
            Add("E_TYPE_MISMATCH", "Function must end with a return value", function.Declaration.At);

        function.CheckedFunction = new CheckedFunction(
            function.Id,
            function.Module,
            function.Declaration.Name,
            function.Declaration.Public,
            function.Parameters,
            function.ReturnType,
            ReadOnly(body),
            function.DeclaredEffects,
            function.Declaration.At);
    }

    private bool CheckStatements(
        IReadOnlyList<Stmt> statements,
        List<TypedStmt> typedStatements,
        Dictionary<string, LocalSymbol> locals)
    {
        var guaranteesReturn = false;
        foreach (var statement in statements)
        {
            if (guaranteesReturn)
            {
                Add("E_UNREACHABLE", "Statement is unreachable after a guaranteed return", statement.At);
                continue;
            }

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
                    typedStatements.Add(new TypedLetStmt(id, let.Name, localType, value, let.At));
                    break;
                }
                case ReturnStmt ret:
                {
                    var value = CheckExpr(ret.Value, _currentFunction!.ReturnType, locals, 0);
                    typedStatements.Add(new TypedReturnStmt(value, ret.At));
                    guaranteesReturn = true;
                    break;
                }
                case IfStmt conditional:
                {
                    var condition = CheckExpr(conditional.Condition, LangType.Bool, locals, 0);
                    var thenLocals = new Dictionary<string, LocalSymbol>(locals, StringComparer.Ordinal);
                    var thenBody = new List<TypedStmt>();
                    var thenReturns = CheckStatements(conditional.Then, thenBody, thenLocals);

                    IReadOnlyList<TypedStmt>? elseBody = null;
                    var elseReturns = false;
                    if (conditional.Else.Count != 0)
                    {
                        var elseLocals = new Dictionary<string, LocalSymbol>(locals, StringComparer.Ordinal);
                        var typedElseBody = new List<TypedStmt>();
                        elseReturns = CheckStatements(conditional.Else, typedElseBody, elseLocals);
                        elseBody = ReadOnly(typedElseBody);
                    }

                    typedStatements.Add(new TypedIfStmt(
                        condition,
                        ReadOnly(thenBody),
                        elseBody,
                        conditional.At));
                    guaranteesReturn = conditional.Else.Count != 0 && thenReturns && elseReturns;
                    break;
                }
                default:
                    Add("E_UNSUPPORTED", "Statement is not implemented in this language slice", statement.At);
                    break;
            }
        }

        return guaranteesReturn;
    }

    private void InferEffectsAndValidateBounds()
    {
        var inferred = _functions.ToDictionary(
            function => function.Id,
            function => new HashSet<string>(function.DirectEffects.Select(call => call.Effect), StringComparer.Ordinal));

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var function in _functions)
            {
                var functionEffects = inferred[function.Id];
                foreach (var call in function.Calls)
                foreach (var effect in inferred[call.Target.Id])
                    if (functionEffects.Add(effect)) changed = true;
            }
        }

        foreach (var function in _functions)
        {
            var orderedEffects = EffectVocabulary.Where(inferred[function.Id].Contains).ToArray();
            if (function.CheckedFunction is { } checkedFunction)
                checkedFunction.InferredEffects = ReadOnly(orderedEffects);

            var declared = new HashSet<string>(function.DeclaredEffects, StringComparer.Ordinal);
            foreach (var effect in orderedEffects)
            {
                if (declared.Contains(effect)) continue;
                var path = FindShortestEffectPath(function, effect);
                Add(
                    "E_EFFECT_EXCEEDED",
                    $"Effect '{effect}' is not declared by function '{FormatFunctionName(function)}'; shortest call path: {path}",
                    function.Declaration.At);
            }
        }
    }

    private string FindShortestEffectPath(FunctionSymbol root, string effect)
    {
        var queue = new Queue<(FunctionSymbol Function, FunctionSymbol[] Path)>();
        var visited = new HashSet<int> { root.Id };
        queue.Enqueue((root, [root]));

        while (queue.Count != 0)
        {
            var current = queue.Dequeue();
            var direct = current.Function.DirectEffects
                .Where(call => call.Effect == effect)
                .OrderBy(call => call.At.File, StringComparer.Ordinal)
                .ThenBy(call => call.At.Line)
                .ThenBy(call => call.At.Column)
                .FirstOrDefault();
            if (direct is not null)
            {
                var parts = current.Path.Select(FormatFunctionName).Append(direct.IntrinsicName);
                return string.Join(" -> ", parts);
            }

            foreach (var call in current.Function.Calls
                         .OrderBy(call => call.Target.Module, StringComparer.Ordinal)
                         .ThenBy(call => call.Target.Declaration.Name, StringComparer.Ordinal)
                         .ThenBy(call => call.At.File, StringComparer.Ordinal)
                         .ThenBy(call => call.At.Line)
                         .ThenBy(call => call.At.Column))
            {
                if (!visited.Add(call.Target.Id)) continue;
                queue.Enqueue((call.Target, current.Path.Append(call.Target).ToArray()));
            }
        }

        return FormatFunctionName(root) + " -> fs.read_text";
    }

    private static string FormatFunctionName(FunctionSymbol function) =>
        $"{function.Module}.{function.Declaration.Name}";

    private TypedExpr CheckExpr(
        Expr expression,
        LangType? expected,
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
            MemberCallExpr call => CheckMemberCall(call, locals, depth + 1),
            StructConstructExpr structure => CheckStructConstruction(structure, locals, depth + 1),
            FieldAccessExpr access => CheckFieldAccess(access, locals, depth + 1),
            MatchExpr match => CheckMatch(match, expected, locals, depth + 1),
            _ => UnsupportedExpr(expression)
        };

        if (expected is not null && !expected.IsError && !result.Type.IsError && result.Type != expected)
            AddMismatch(expected, result.Type, expression.At);
        return result;
    }

    private TypedExpr CheckName(NameExpr expression, LangType? expected, Dictionary<string, LocalSymbol> locals)
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
            if (expected.Kind != LangTypeKind.Option)
            {
                AddMismatch(expected, "Option<T>", expression.At);
                return new TypedErrorExpr(expression.At);
            }
            return new TypedBuiltinConstructExpr(expected, BuiltinVariant.None, [], expression.At);
        }

        Add("E_NAME_UNRESOLVED", $"Name '{expression.Name}' is not in scope", expression.At);
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckStructConstruction(
        StructConstructExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var scope = CurrentModule;
        if (!scope.VisibleStructs.TryGetValue(expression.Name, out var structure))
        {
            foreach (var value in expression.Fields)
                _ = CheckExpr(value.Value, null, locals, depth);

            if (scope.VisibleUnions.ContainsKey(expression.Name))
                Add("E_TYPE_MISMATCH", $"Type '{expression.Name}' is a union, not a struct", expression.At);
            else
                Add("E_NAME_UNRESOLVED", $"Struct '{expression.Name}' is not declared", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var values = new List<TypedStructFieldValue>();
        var supplied = new HashSet<int>();
        foreach (var initializer in expression.Fields)
        {
            var field = structure.Fields.FirstOrDefault(item => item.Name == initializer.Name);
            if (field is null)
            {
                _ = CheckExpr(initializer.Value, null, locals, depth);
                Add("E_FIELD_UNKNOWN", $"Struct '{structure.Declaration.Name}' has no field '{initializer.Name}'", initializer.At);
                continue;
            }

            var value = CheckExpr(initializer.Value, field.Type, locals, depth);
            if (!supplied.Add(field.Index))
                Add("E_FIELD_DUPLICATE", $"Field '{initializer.Name}' is initialized more than once", initializer.At);
            values.Add(new TypedStructFieldValue(field.Index, value));
        }

        foreach (var field in structure.Fields)
        {
            if (!supplied.Contains(field.Index))
                Add("E_FIELD_MISSING", $"Field '{field.Name}' is missing from construction of '{structure.Declaration.Name}'", expression.At);
        }

        return new TypedStructConstructExpr(structure.Type, structure.Id, ReadOnly(values), expression.At);
    }

    private TypedExpr CheckFieldAccess(
        FieldAccessExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (expression.Target is NameExpr typeName &&
            !locals.ContainsKey(typeName.Name) &&
            CurrentModule.VisibleUnions.ContainsKey(typeName.Name))
        {
            return CheckVariantConstruction(
                typeName.Name,
                expression.Field,
                expression.At,
                expression.At,
                [],
                locals,
                depth);
        }

        if (expression.Target is NameExpr fsErrorName &&
            !locals.ContainsKey(fsErrorName.Name) &&
            fsErrorName.Name == "FsError")
        {
            if (IsFsErrorVariant(expression.Field))
                Add("E_TYPE_MISMATCH", "FsError variants can only be produced by fs.read_text", expression.At);
            else
                Add("E_NAME_UNRESOLVED", $"Variant '{expression.Field}' is not declared on FsError", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var target = CheckExpr(expression.Target, null, locals, depth);
        if (target.Type.IsError) return new TypedErrorExpr(expression.At);
        if (target.Type.IsText && expression.Field == "length")
            return new TypedTextLengthExpr(target, expression.At);
        if (target.Type.Kind != LangTypeKind.Struct)
        {
            Add("E_TYPE_MISMATCH", $"Field access requires a struct value, found '{target.Type.DisplayName}'", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var structure = _structs[target.Type.StructId];
        var field = structure.Fields.FirstOrDefault(item => item.Name == expression.Field);
        if (field is null)
        {
            Add("E_FIELD_UNKNOWN", $"Struct '{structure.Declaration.Name}' has no field '{expression.Field}'", expression.At);
            return new TypedErrorExpr(expression.At);
        }
        return new TypedFieldAccessExpr(field.Type, target, field.Index, expression.At);
    }

    private TypedExpr CheckBinary(BinaryExpr expression, Dictionary<string, LocalSymbol> locals, int depth)
    {
        if (expression.Op is "==" or "!=")
        {
            var comparedLeft = CheckExpr(expression.Left, null, locals, depth);
            var comparedRight = CheckExpr(expression.Right, null, locals, depth);
            if (comparedLeft.Type.IsError || comparedRight.Type.IsError) return new TypedErrorExpr(expression.At);

            if (comparedLeft.Type == comparedRight.Type &&
                (comparedLeft.Type.IsI32 || comparedLeft.Type.IsBool || comparedLeft.Type.IsText))
                return new TypedCompareExpr(expression.Op, comparedLeft, comparedRight, expression.At);

            Add(
                "E_TYPE_MISMATCH",
                $"Comparison '{expression.Op}' requires matching operands of type i32, bool, or Text",
                expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Op is "<" or "<=" or ">" or ">=")
        {
            var comparedLeft = CheckExpr(expression.Left, null, locals, depth);
            var comparedRight = CheckExpr(expression.Right, null, locals, depth);
            if (comparedLeft.Type.IsError || comparedRight.Type.IsError) return new TypedErrorExpr(expression.At);

            if (comparedLeft.Type.IsI32 && comparedRight.Type.IsI32)
                return new TypedCompareExpr(expression.Op, comparedLeft, comparedRight, expression.At);

            Add("E_TYPE_MISMATCH", $"Comparison '{expression.Op}' requires i32 operands", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Op is not ("+" or "-" or "*"))
        {
            Add("E_UNSUPPORTED", $"Arithmetic operator '{expression.Op}' is not implemented", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var left = CheckExpr(expression.Left, LangType.I32, locals, depth);
        var right = CheckExpr(expression.Right, LangType.I32, locals, depth);
        return new TypedBinaryExpr(LangType.I32, expression.Op, left, right, expression.At);
    }

    private TypedExpr CheckCall(
        CallExpr expression,
        LangType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (CurrentModule.VisibleFunctions.TryGetValue(expression.Name, out var function))
        {
            if (expression.Arguments.Count != function.Parameters.Count)
                Add("E_TYPE_MISMATCH", $"Function '{expression.Name}' expects {function.Parameters.Count} arguments, got {expression.Arguments.Count}", expression.At);

            var arguments = new List<TypedExpr>();
            var diagnosticsBeforeArguments = diagnostics.Count;
            var hasCorrectArity = expression.Arguments.Count == function.Parameters.Count;
            for (var i = 0; i < expression.Arguments.Count; i++)
            {
                var argumentType = i < function.Parameters.Count ? function.Parameters[i].Type : null;
                arguments.Add(CheckExpr(expression.Arguments[i], argumentType, locals, depth));
            }
            var signatureTypesValid = !function.ReturnType.IsError && function.Parameters.All(parameter => !parameter.Type.IsError);
            if (hasCorrectArity && signatureTypesValid && diagnostics.Count == diagnosticsBeforeArguments)
                _currentFunction?.Calls.Add(new FunctionCallSite(function, expression.At));
            return new TypedCallExpr(function.ReturnType, function.Id, ReadOnly(arguments), expression.At);
        }

        if (expression.Name is "Some" or "Ok" or "Err")
            return CheckBuiltinCall(expression, expected, locals, depth);

        foreach (var argument in expression.Arguments)
            _ = CheckExpr(argument, null, locals, depth);
        Add("E_NAME_UNRESOLVED", $"Function '{expression.Name}' is not declared", expression.At);
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckMemberCall(
        MemberCallExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (expression.Target is NameExpr targetName && !locals.ContainsKey(targetName.Name))
        {
            if (CurrentModule.VisibleUnions.ContainsKey(targetName.Name))
                return CheckVariantConstruction(
                    targetName.Name,
                    expression.Member,
                    expression.At,
                    expression.MemberAt,
                    expression.Arguments,
                    locals,
                    depth);

            if (targetName.Name == "FsError")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                if (IsFsErrorVariant(expression.Member))
                    Add("E_TYPE_MISMATCH", "FsError variants can only be produced by fs.read_text", expression.MemberAt);
                else
                    Add("E_NAME_UNRESOLVED", $"Variant '{expression.Member}' is not declared on FsError", expression.MemberAt);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "fs" && expression.Member == "read_text")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                Add("E_CAPABILITY_MISSING", "Intrinsic 'fs.read_text' requires a local or parameter of type 'FsRead'", expression.At);
                return new TypedErrorExpr(expression.At);
            }
        }

        var diagnosticsBeforeReceiver = diagnostics.Count;
        var receiver = CheckExpr(expression.Target, null, locals, depth);
        if (receiver.Type.IsError)
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            return new TypedErrorExpr(expression.At);
        }

        if (receiver.Type.IsFsRead && expression.Member == "read_text")
        {
            var diagnosticsBeforeCall = diagnosticsBeforeReceiver;
            var hasCorrectArity = expression.Arguments.Count == 1;
            if (!hasCorrectArity)
                Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.read_text' expects 1 argument, got {expression.Arguments.Count}", expression.MemberAt);

            var arguments = new List<TypedExpr> { receiver };
            if (expression.Arguments.Count > 0)
                arguments.Add(CheckExpr(expression.Arguments[0], LangType.Text, locals, depth));
            else
                arguments.Add(new TypedErrorExpr(expression.MemberAt));
            for (var i = 1; i < expression.Arguments.Count; i++)
                _ = CheckExpr(expression.Arguments[i], null, locals, depth);

            var resultType = LangType.Result(LangType.Text, LangType.FsError);
            if (hasCorrectArity && diagnostics.Count == diagnosticsBeforeCall)
                _currentFunction?.DirectEffects.Add(new DirectEffectCall("fs.read", "fs.read_text", expression.MemberAt));
            return new TypedIntrinsicCallExpr(
                resultType,
                BuiltinIntrinsic.FsReadText,
                ReadOnly(arguments),
                expression.At);
        }

        if (expression.Member == "read_text")
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);

            var message = expression.Target is NameExpr localName && locals.ContainsKey(localName.Name)
                ? $"Local '{localName.Name}' of type '{receiver.Type.DisplayName}' cannot provide capability member 'read_text'"
                : $"Value of type '{receiver.Type.DisplayName}' cannot provide capability member 'read_text'";
            Add("E_CAPABILITY_MISSING", message, expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (receiver.Type.IsText && expression.Member == "trim")
        {
            if (expression.Arguments.Count != 0)
            {
                Add("E_TYPE_MISMATCH", $"Intrinsic 'Text.trim' expects 0 arguments, got {expression.Arguments.Count}", expression.MemberAt);
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                return new TypedErrorExpr(expression.At);
            }
            return new TypedTextTrimExpr(receiver, expression.At);
        }

        foreach (var argument in expression.Arguments)
            _ = CheckExpr(argument, null, locals, depth);

        var targetDescription = expression.Target is NameExpr name && locals.ContainsKey(name.Name)
            ? name.Name
            : receiver.Type.DisplayName;
        var unsupportedMessage = expression.Target is NameExpr local && locals.ContainsKey(local.Name)
            ? $"Member calls on local values are not implemented for '{targetDescription}.{expression.Member}'"
            : $"Member calls on values are not implemented for '{targetDescription}.{expression.Member}'";
        Add("E_UNSUPPORTED", unsupportedMessage, expression.MemberAt);
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckBuiltinCall(
        CallExpr expression,
        LangType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var wantsOption = expression.Name == "Some";
        var wantsOk = expression.Name == "Ok";
        var expectedKind = wantsOption ? LangTypeKind.Option : LangTypeKind.Result;
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
                    ? LangType.Option(value.Type)
                    : wantsOk
                        ? LangType.Result(value.Type, LangType.Error)
                        : LangType.Result(LangType.Error, value.Type);
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
        string qualifier,
        string member,
        Token at,
        Token memberAt,
        IReadOnlyList<Expr> arguments,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (!CurrentModule.VisibleUnions.TryGetValue(qualifier, out var union))
        {
            foreach (var argument in arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_NAME_UNRESOLVED", $"Union '{qualifier}' is not declared", at);
            return new TypedErrorExpr(at);
        }

        var variant = union.Variants.FirstOrDefault(item => item.Name == member);
        if (variant is null)
        {
            foreach (var argument in arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_NAME_UNRESOLVED", $"Variant '{member}' is not declared on union '{qualifier}'", memberAt);
            return new TypedErrorExpr(at);
        }

        if (arguments.Count != variant.Fields.Count)
            Add("E_TYPE_MISMATCH", $"Variant '{qualifier}.{variant.Name}' expects {variant.Fields.Count} payload values, got {arguments.Count}", memberAt);

        var typedArguments = new List<TypedExpr>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var expected = i < variant.Fields.Count ? variant.Fields[i].Type : null;
            typedArguments.Add(CheckExpr(arguments[i], expected, locals, depth));
        }
        return new TypedUnionConstructExpr(union.Type, union.Id, variant.Id, ReadOnly(typedArguments), at);
    }

    private TypedExpr CheckMatch(
        MatchExpr expression,
        LangType? expected,
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
        var invalidPatternSeen = false;
        LangType? inferredResult = expected;

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
                if (shape is null) invalidPatternSeen = true;
                var bindings = new List<BoundLocal>();
                if (shape is not null)
                {
                    if (!covered.Add(shape.Key))
                        Add("E_MATCH_ARM_DUPLICATE", $"Variant arm '{shape.Name}' is duplicated", variantPattern.At);
                    if (variantPattern.Bindings.Count != shape.PayloadTypes.Count)
                    {
                        Add("E_TYPE_MISMATCH", $"Pattern '{shape.Name}' expects {shape.PayloadTypes.Count} bindings, got {variantPattern.Bindings.Count}", variantPattern.At);
                        invalidPatternSeen = true;
                    }

                    var boundNames = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < variantPattern.Bindings.Count; i++)
                    {
                        var name = variantPattern.Bindings[i];
                        var payloadType = i < shape.PayloadTypes.Count ? shape.PayloadTypes[i] : LangType.Error;
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

        if (shapes is not null && !wildcardSeen && !invalidPatternSeen)
        {
            var missing = shapes.Where(shape => !covered.Contains(shape.Key)).Select(shape => shape.Name).ToArray();
            if (missing.Length != 0)
                Add("E_MATCH_NONEXHAUSTIVE", $"Match is missing variants: {string.Join(", ", missing)}", expression.At);
        }

        return new TypedMatchExpr(inferredResult ?? LangType.Error, value, ReadOnly(arms), expression.At);
    }

    private IReadOnlyList<VariantShape>? GetVariantShapes(LangType type)
    {
        if (type.IsError) return null;
        if (type.Kind == LangTypeKind.Union)
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
        if (type.Kind == LangTypeKind.Option)
        {
            return ReadOnly<VariantShape>([
                new("Some", "option:Some", null, 0, BuiltinVariant.Some, ReadOnly([type.Arguments[0]])),
                new("None", "option:None", null, 1, BuiltinVariant.None, [])
            ]);
        }
        if (type.Kind == LangTypeKind.Result)
        {
            return ReadOnly<VariantShape>([
                new("Ok", "result:Ok", null, 0, BuiltinVariant.Ok, ReadOnly([type.Arguments[0]])),
                new("Err", "result:Err", null, 1, BuiltinVariant.Err, ReadOnly([type.Arguments[1]]))
            ]);
        }
        if (type.IsFsError)
        {
            return ReadOnly<VariantShape>([
                new("FsError.NotFound", "fserror:NotFound", null, 0, BuiltinVariant.FsErrorNotFound, []),
                new("FsError.PermissionDenied", "fserror:PermissionDenied", null, 1, BuiltinVariant.FsErrorPermissionDenied, []),
                new("FsError.InvalidPath", "fserror:InvalidPath", null, 2, BuiltinVariant.FsErrorInvalidPath, []),
                new("FsError.InvalidText", "fserror:InvalidText", null, 3, BuiltinVariant.FsErrorInvalidText, []),
                new("FsError.Io", "fserror:Io", null, 4, BuiltinVariant.FsErrorIo, [])
            ]);
        }
        return null;
    }

    private VariantShape? ResolvePatternShape(
        VariantPattern pattern,
        LangType scrutineeType,
        IReadOnlyList<VariantShape> shapes)
    {
        if (scrutineeType.Kind == LangTypeKind.Union)
        {
            var union = _unions[scrutineeType.UnionId];
            if (pattern.UnionName is null)
            {
                var expectedName = union.Declaration.Name + ".<variant>";
                Add("E_TYPE_MISMATCH", $"Expected pattern from '{expectedName}', found '{pattern.VariantName}'", pattern.At);
                return null;
            }

            if (!CurrentModule.VisibleUnions.TryGetValue(pattern.UnionName, out var visibleUnion))
            {
                Add("E_NAME_UNRESOLVED", $"Union '{pattern.UnionName}' is not declared or imported in this module", pattern.At);
                return null;
            }

            if (visibleUnion.Id != union.Id)
            {
                Add("E_TYPE_MISMATCH", $"Pattern union '{pattern.UnionName}' resolves to module '{visibleUnion.Module}', but the matched value uses module '{union.Module}'", pattern.At);
                return null;
            }
        }
        else if (scrutineeType.IsFsError)
        {
            if (pattern.UnionName != "FsError")
            {
                Add("E_TYPE_MISMATCH", $"Expected pattern from 'FsError.<variant>', found '{pattern.VariantName}'", pattern.At);
                return null;
            }
        }
        else if (pattern.UnionName is not null)
        {
            Add("E_TYPE_MISMATCH", $"Qualified pattern '{pattern.UnionName}.{pattern.VariantName}' does not match '{scrutineeType.DisplayName}'", pattern.At);
            return null;
        }

        var shapeName = scrutineeType.Kind == LangTypeKind.Union || scrutineeType.IsFsError
            ? $"{pattern.UnionName}.{pattern.VariantName}"
            : pattern.VariantName;
        var shape = shapes.FirstOrDefault(item => item.Name == shapeName);
        if (shape is null)
        {
            Add("E_NAME_UNRESOLVED", $"Variant '{pattern.VariantName}' is not valid for '{scrutineeType.DisplayName}'", pattern.At);
            return null;
        }
        return shape;
    }

    private LangType ResolveType(TypeSyntax syntax, int depth)
    {
        if (depth >= MaximumSemanticDepth)
        {
            if (!_semanticDepthReported)
            {
                Add("E_TYPE_MISMATCH", "Type nesting exceeds the semantic checker limit", syntax.At);
                _semanticDepthReported = true;
            }
            return LangType.Error;
        }

        switch (syntax.Name)
        {
            case "i32":
                return NoTypeArguments(syntax, LangType.I32);
            case "bool":
                return NoTypeArguments(syntax, LangType.Bool);
            case "Text":
                return NoTypeArguments(syntax, LangType.Text);
            case "FsRead":
                return NoTypeArguments(syntax, LangType.FsRead);
            case "FsError":
                return NoTypeArguments(syntax, LangType.FsError);
            case "Option":
                if (syntax.Args.Count != 1)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'Option' expects 1 type argument, got {syntax.Args.Count}", syntax.At);
                    return LangType.Error;
                }
                return LangType.Option(ResolveType(syntax.Args[0], depth + 1));
            case "Result":
                if (syntax.Args.Count != 2)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'Result' expects 2 type arguments, got {syntax.Args.Count}", syntax.At);
                    return LangType.Error;
                }
                return LangType.Result(ResolveType(syntax.Args[0], depth + 1), ResolveType(syntax.Args[1], depth + 1));
            default:
                if (CurrentModule.VisibleUnions.TryGetValue(syntax.Name, out var union))
                {
                    if (syntax.Args.Count != 0)
                    {
                        Add("E_TYPE_MISMATCH", $"Union type '{syntax.Name}' does not take type arguments", syntax.At);
                        return LangType.Error;
                    }
                    return union.Type;
                }
                if (CurrentModule.VisibleStructs.TryGetValue(syntax.Name, out var structure))
                {
                    if (syntax.Args.Count != 0)
                    {
                        Add("E_TYPE_MISMATCH", $"Struct type '{syntax.Name}' does not take type arguments", syntax.At);
                        return LangType.Error;
                    }
                    return structure.Type;
                }
                Add("E_NAME_UNRESOLVED", $"Type '{syntax.Name}' is not declared", syntax.At);
                return LangType.Error;
        }
    }

    private LangType NoTypeArguments(TypeSyntax syntax, LangType type)
    {
        if (syntax.Args.Count != 0)
        {
            Add("E_TYPE_MISMATCH", $"Type '{syntax.Name}' does not take type arguments", syntax.At);
            return LangType.Error;
        }
        return type;
    }

    private static bool IsFsErrorVariant(string name) =>
        name is "NotFound" or "PermissionDenied" or "InvalidPath" or "InvalidText" or "Io";


    private void AddMismatch(LangType expected, LangType actual, Token at) =>
        Add("E_TYPE_MISMATCH", $"Expected '{expected.DisplayName}', found '{actual.DisplayName}'", at);


    private void AddMismatch(LangType expected, string actual, Token at) =>
        Add("E_TYPE_MISMATCH", $"Expected '{expected.DisplayName}', found '{actual}'", at);
    private void Add(string code, string message, Token at) =>
        diagnostics.Add(new Diagnostic(code, message, at.File, at.Range));

    private ModuleSymbols CurrentModule => _modulesByName[_currentModule];

    private TypedExpr UnsupportedExpr(Expr expression) { Add("E_UNSUPPORTED", "Expression is not implemented in this language slice", expression.At); return new TypedErrorExpr(expression.At); }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());

    private sealed class ModuleSymbols(ParsedProgram program)
    {
        public ParsedProgram Program { get; } = program;
        public HashSet<string> TypeNames { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, UnionSymbol> DeclaredUnions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, StructSymbol> DeclaredStructs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, FunctionSymbol> DeclaredFunctions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, UnionSymbol> VisibleUnions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, StructSymbol> VisibleStructs { get; } = new(StringComparer.Ordinal);
        public HashSet<string> VisibleTypeNames { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, FunctionSymbol> VisibleFunctions { get; } = new(StringComparer.Ordinal);
    }

    private sealed class UnionSymbol(int id, string module, UnionDecl declaration, LangType type)
    {
        public int Id { get; } = id;
        public string Module { get; } = module;
        public UnionDecl Declaration { get; } = declaration;
        public LangType Type { get; } = type;
        public List<CheckedVariant> Variants { get; } = [];
    }

    private sealed class StructSymbol(int id, string module, StructDecl declaration, LangType type)
    {
        public int Id { get; } = id;
        public string Module { get; } = module;
        public StructDecl Declaration { get; } = declaration;
        public LangType Type { get; } = type;
        public List<CheckedStructField> Fields { get; } = [];
    }

    private sealed class FunctionSymbol(int id, string module, FunctionDecl declaration)
    {
        public int Id { get; } = id;
        public string Module { get; } = module;
        public FunctionDecl Declaration { get; } = declaration;
        public IReadOnlyList<CheckedParameter> Parameters { get; set; } = [];
        public LangType ReturnType { get; set; } = LangType.Error;
        public IReadOnlyList<string> DeclaredEffects { get; set; } = [];
        public List<FunctionCallSite> Calls { get; } = [];
        public List<DirectEffectCall> DirectEffects { get; } = [];
        public CheckedFunction? CheckedFunction { get; set; }
    }

    private sealed record FunctionCallSite(FunctionSymbol Target, Token At);
    private sealed record DirectEffectCall(string Effect, string IntrinsicName, Token At);

    private sealed record LocalSymbol(int Id, LangType Type);

    private int _nextLocalId;
}

internal sealed record TypedErrorExpr(Token At) : TypedExpr(LangType.Error, At);
