using System.Collections.ObjectModel;

internal enum LangTypeKind
{
    Error,
    I32,
    Bool,
    Text,
    Html,
    FilePath,
    TypeParameter,
    Union,
    Struct,
    Option,
    List,
    Result,
    FsRead,
    FsWrite,
    FsError,
    DbRead,
    DbWrite,
    Transaction,
    DbError
}

internal sealed class LangType : IEquatable<LangType>
{
    private readonly ReadOnlyCollection<LangType> _arguments;

    private LangType(
        LangTypeKind kind,
        string displayName,
        int typeParameterOwnerId = -1,
        int typeParameterOrdinal = -1,
        int unionId = -1,
        int structId = -1,
        IEnumerable<LangType>? arguments = null)
    {
        Kind = kind;
        DisplayName = displayName;
        TypeParameterOwnerId = typeParameterOwnerId;
        TypeParameterOrdinal = typeParameterOrdinal;
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
    public bool IsHtml => Kind == LangTypeKind.Html;
    public bool IsFilePath => Kind == LangTypeKind.FilePath;
    public bool IsFsRead => Kind == LangTypeKind.FsRead;
    public bool IsFsWrite => Kind == LangTypeKind.FsWrite;
    public bool IsFsError => Kind == LangTypeKind.FsError;
    public bool IsDbRead => Kind == LangTypeKind.DbRead;
    public bool IsDbWrite => Kind == LangTypeKind.DbWrite;
    public bool IsTransaction => Kind == LangTypeKind.Transaction;
    public bool IsList => Kind == LangTypeKind.List;
    public bool IsDbError => Kind == LangTypeKind.DbError;
    internal int UnionId { get; }
    internal int StructId { get; }
    internal int TypeParameterOwnerId { get; }
    internal int TypeParameterOrdinal { get; }
    internal bool IsError => Kind == LangTypeKind.Error;

    internal static LangType Error { get; } = new(LangTypeKind.Error, "<error>");
    internal static LangType I32 { get; } = new(LangTypeKind.I32, "i32");
    internal static LangType Bool { get; } = new(LangTypeKind.Bool, "bool");
    internal static LangType Text { get; } = new(LangTypeKind.Text, "Text");
    internal static LangType Html { get; } = new(LangTypeKind.Html, "Html");
    internal static LangType FilePath { get; } = new(LangTypeKind.FilePath, "FilePath");
    internal static LangType FsRead { get; } = new(LangTypeKind.FsRead, "FsRead");
    internal static LangType FsWrite { get; } = new(LangTypeKind.FsWrite, "FsWrite");
    internal static LangType FsError { get; } = new(LangTypeKind.FsError, "FsError");
    internal static LangType DbRead { get; } = new(LangTypeKind.DbRead, "DbRead");
    internal static LangType DbWrite { get; } = new(LangTypeKind.DbWrite, "DbWrite");
    internal static LangType Transaction { get; } = new(LangTypeKind.Transaction, "Transaction");
    internal static LangType DbError { get; } = new(LangTypeKind.DbError, "DbError");

    internal static LangType ForTypeParameter(int ownerId, int ordinal, string name) =>
        new(LangTypeKind.TypeParameter, name, typeParameterOwnerId: ownerId, typeParameterOrdinal: ordinal);
    internal static LangType ForUnion(int unionId, string name) => new(LangTypeKind.Union, name, unionId: unionId);
    internal static LangType ForStruct(int structId, string name) => new(LangTypeKind.Struct, name, structId: structId);
    internal static LangType Option(LangType item) => new(LangTypeKind.Option, $"Option<{item.DisplayName}>", arguments: [item]);
    internal static LangType List(LangType item) => new(LangTypeKind.List, $"List<{item.DisplayName}>", arguments: [item]);
    internal static LangType Result(LangType ok, LangType error) => new(LangTypeKind.Result, $"Result<{ok.DisplayName}, {error.DisplayName}>", arguments: [ok, error]);

    public bool Equals(LangType? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || Kind != other.Kind) return false;
        if (Kind == LangTypeKind.Union) return UnionId == other.UnionId;
        if (Kind == LangTypeKind.Struct) return StructId == other.StructId;
        if (Kind == LangTypeKind.TypeParameter)
            return TypeParameterOwnerId == other.TypeParameterOwnerId && TypeParameterOrdinal == other.TypeParameterOrdinal;
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
        else if (Kind == LangTypeKind.TypeParameter)
        {
            hash.Add(TypeParameterOwnerId);
            hash.Add(TypeParameterOrdinal);
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
internal sealed record CheckedUnion(int Id, string PackageId, string Module, string Name, bool Public, LangType Type, IReadOnlyList<CheckedVariant> Variants, Token At);
internal sealed record CheckedStructField(string Name, LangType Type, int Index, Token At);
internal sealed record CheckedStruct(int Id, string PackageId, string Module, string Name, bool Public, LangType Type, IReadOnlyList<CheckedStructField> Fields, Token At);
internal sealed record CheckedParameter(string Name, LangType Type, int LocalId, Token At);
internal sealed record CheckedDirectCall(string PackageId, string Module, string Name);
internal sealed record CheckedTest(string Name, string PackageId, string Module, int FunctionId, Token At);
internal sealed record CheckedEffectPath
{
    internal CheckedEffectPath(
        string effect,
        IEnumerable<string> steps,
        IEnumerable<int> functionIds,
        string intrinsicName)
    {
        Effect = effect;
        Steps = Array.AsReadOnly(steps.ToArray());
        FunctionIds = Array.AsReadOnly(functionIds.ToArray());
        IntrinsicName = intrinsicName;
    }

    public string Effect { get; }
    public IReadOnlyList<string> Steps { get; }
    internal IReadOnlyList<int> FunctionIds { get; }
    internal string IntrinsicName { get; }
}
internal enum CheckedCommandInputKind { Argument, Option, Flag }
internal enum CheckedCommandLiteralKind { Text, I32, Boolean }
internal sealed record CheckedCommandLiteral(
    CheckedCommandLiteralKind Kind,
    string? TextValue = null,
    int? IntegerValue = null,
    bool? BooleanValue = null);
internal sealed record CheckedCommandInput(
    string Name,
    LangType Type,
    CheckedCommandInputKind Kind,
    int FieldIndex,
    string Help,
    CheckedCommandLiteral? Default);
internal sealed record CheckedCommand(
    int Id,
    string PackageId,
    string Module,
    string Name,
    string Help,
    int ArgsStructId,
    LangType ArgsType,
    IReadOnlyList<CheckedCommandInput> Inputs,
    int HandlerFunctionId,
    string HandlerReference,
    bool HandlerIsAsync,
    int ErrorFunctionId,
    string ErrorReference,
    LangType ErrorType,
    IReadOnlyList<CheckedCapabilityParameter> Capabilities,
    Token At);

internal enum CheckedRouteContentKind { Json, Html }
internal enum CheckedCapabilityKind { FsRead, FsWrite, DbRead, DbWrite }
internal sealed record CheckedCapabilityParameter(
    CheckedCapabilityKind Kind,
    int HandlerParameterIndex,
    string ParameterName,
    Token At);

internal sealed record CheckedRouteResponse(
    int VariantId,
    string VariantName,
    int StatusCode,
    CheckedRouteContentKind? ContentKind,
    LangType? PayloadType,
    Token At,
    Token VariantAt,
    Token StatusAt,
    Token? FormatAt,
    Token? PayloadTypeAt);

internal sealed class CheckedRoute
{
    internal CheckedRoute(
        int id,
        string method,
        string path,
        LangType? bodyType,
        IEnumerable<CheckedStructField> bodySchema,
        IEnumerable<CheckedCapabilityParameter> capabilities,
        int handlerFunctionId,
        string handlerReference,
        bool handlerIsAsync,
        Token handlerAt,
        int replyUnionId,
        IEnumerable<CheckedRouteResponse> responses,
        Token at,
        Token methodAt,
        Token pathAt,
        Token? bodyAt,
        Token? bodyTypeAt)
    {
        Id = id;
        Method = method;
        Path = path;
        BodyType = bodyType;
        BodySchema = Array.AsReadOnly(bodySchema.ToArray());
        Capabilities = Array.AsReadOnly(capabilities.ToArray());
        HandlerFunctionId = handlerFunctionId;
        HandlerReference = handlerReference;
        HandlerIsAsync = handlerIsAsync;
        HandlerAt = handlerAt;
        ReplyUnionId = replyUnionId;
        Responses = Array.AsReadOnly(responses.ToArray());
        At = at;
        MethodAt = methodAt;
        PathAt = pathAt;
        BodyAt = bodyAt;
        BodyTypeAt = bodyTypeAt;
    }

    public int Id { get; }
    public string Method { get; }
    public string Path { get; }
    public LangType? BodyType { get; }
    public IReadOnlyList<CheckedStructField> BodySchema { get; }
    public IReadOnlyList<CheckedCapabilityParameter> Capabilities { get; }
    public int HandlerFunctionId { get; }
    public string HandlerReference { get; }
    public bool HandlerIsAsync { get; }
    public Token HandlerAt { get; }
    public int ReplyUnionId { get; }
    public IReadOnlyList<CheckedRouteResponse> Responses { get; }
    public Token At { get; }
    public Token MethodAt { get; }
    public Token PathAt { get; }
    public Token? BodyAt { get; }
    public Token? BodyTypeAt { get; }
}

internal abstract record TypedExpr(LangType Type, Token At);
internal sealed record TypedNumberExpr(Token At, int Value) : TypedExpr(LangType.I32, At);
internal sealed record TypedBoolExpr(Token At, bool Value) : TypedExpr(LangType.Bool, At);
internal sealed record TypedTextExpr(Token At, string Value) : TypedExpr(LangType.Text, At);
internal sealed record TypedListExpr(LangType Type, IReadOnlyList<TypedExpr> Items, Token At) : TypedExpr(Type, At);
internal sealed record TypedLocalExpr(LangType Type, int LocalId, Token At) : TypedExpr(Type, At);
internal sealed record TypedBinaryExpr(LangType Type, string Op, TypedExpr Left, TypedExpr Right, Token At) : TypedExpr(Type, At);
internal sealed record TypedCompareExpr(string Op, TypedExpr Left, TypedExpr Right, Token At) : TypedExpr(LangType.Bool, At);
internal sealed record TypedTextLengthExpr(TypedExpr Target, Token At) : TypedExpr(LangType.I32, At);
internal sealed record TypedTextTrimExpr(TypedExpr Target, Token At) : TypedExpr(LangType.Text, At);
internal sealed record TypedListLengthExpr(TypedExpr Target, Token At) : TypedExpr(LangType.I32, At);
internal sealed record TypedListGetExpr(LangType Type, TypedExpr Target, TypedExpr Index, Token At) : TypedExpr(Type, At);
internal sealed record TypedListAppendExpr(LangType Type, TypedExpr Target, TypedExpr Value, Token At) : TypedExpr(Type, At);
internal sealed record TypedCallExpr(
    LangType Type,
    int FunctionId,
    bool IsAsync,
    IReadOnlyList<LangType> TypeArguments,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);

internal enum BuiltinIntrinsic
{
    FsReadText,
    FsReadTextAsync,
    FsWriteText,
    HtmlText,
    HtmlHeading,
    HtmlParagraph,
    HtmlConcat,
    HtmlDocument,
    TextSplit
}

internal sealed record TypedIntrinsicCallExpr(
    LangType Type,
    BuiltinIntrinsic Intrinsic,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedAwaitExpr(LangType Type, TypedExpr Value, Token At) : TypedExpr(Type, At);

internal enum CheckedDatabaseOperationKind { QueryOne, Execute, TransactionExecute }
internal sealed record CheckedDatabaseOperation(
    CheckedDatabaseOperationKind Kind,
    string Effect,
    string Sql,
    int ParameterStructId,
    int? RowStructId);
internal sealed record TypedDatabaseCallExpr(
    LangType Type,
    TypedExpr Receiver,
    TypedExpr Parameters,
    CheckedDatabaseOperation Operation,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedTransactionCommitExpr(int TransactionLocalId, Token At)
    : TypedExpr(LangType.Result(LangType.Bool, LangType.DbError), At);

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
    FsErrorIo,
    DbErrorStatement,
    DbErrorRowShape
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
internal sealed record TypedAssignStmt(int LocalId, TypedExpr Value, Token At) : TypedStmt(At);
internal sealed record TypedReturnStmt(TypedExpr Value, Token At) : TypedStmt(At);
internal sealed record TypedIfStmt(
    TypedExpr Condition,
    IReadOnlyList<TypedStmt> ThenBody,
    IReadOnlyList<TypedStmt>? ElseBody,
    Token At) : TypedStmt(At);
internal sealed record TypedForStmt(
    TypedExpr Collection,
    BoundLocal Item,
    IReadOnlyList<TypedStmt> Body,
    Token At) : TypedStmt(At);
internal sealed record TypedWithTransactionStmt(
    TypedExpr Database,
    int TransactionLocalId,
    string TransactionName,
    IReadOnlyList<TypedStmt> Body,
    Token At) : TypedStmt(At);

internal sealed class CheckedFunction
{
    internal CheckedFunction(
        int id,
        string packageId,
        string module,
        string name,
        bool isPublic,
        bool isAsync,
        IReadOnlyList<CheckedParameter> parameters,
        IReadOnlyList<LangType> typeParameters,
        LangType returnType,
        IReadOnlyList<TypedStmt> body,
        IEnumerable<CheckedDirectCall> calls,
        IReadOnlyList<string> declaredEffects,
        Token at)
    {
        Id = id;
        PackageId = packageId;
        Module = module;
        Name = name;
        Public = isPublic;
        IsAsync = isAsync;
        Parameters = ReadOnly(parameters);
        TypeParameters = ReadOnly(typeParameters);
        ReturnType = returnType;
        Body = ReadOnly(body);
        Calls = ReadOnly(calls);
        DeclaredEffects = ReadOnly(declaredEffects);
        InferredEffects = [];
        InferredEffectPaths = [];
        At = at;
    }

    public int Id { get; }
    public string PackageId { get; }
    public string Module { get; }
    public string Name { get; }
    public bool Public { get; }
    public bool IsAsync { get; }
    public IReadOnlyList<CheckedParameter> Parameters { get; }
    public IReadOnlyList<LangType> TypeParameters { get; }
    public LangType ReturnType { get; }
    public IReadOnlyList<CheckedDirectCall> Calls { get; }
    public IReadOnlyList<string> DeclaredEffects { get; }
    public IReadOnlyList<string> InferredEffects { get; private set; }
    public IReadOnlyList<CheckedEffectPath> InferredEffectPaths { get; private set; }
    internal IReadOnlyList<TypedStmt> Body { get; }
    internal Token At { get; }

    internal void SetInferredEffects(
        IEnumerable<string> effects,
        IEnumerable<CheckedEffectPath> paths)
    {
        InferredEffects = ReadOnly(effects);
        InferredEffectPaths = ReadOnly(paths);
    }

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
        IEnumerable<CheckedStruct> structs,
        IEnumerable<CheckedTest>? tests = null,
        IEnumerable<CheckedCommand>? commands = null,
        int? entryCommandId = null,
        IEnumerable<CheckedRoute>? routes = null)
    {
        Modules = Array.AsReadOnly(modules.ToArray());
        EntryModule = entryModule;
        EntryFunctionId = entryFunctionId;
        Functions = Array.AsReadOnly(functions.ToArray());
        Unions = Array.AsReadOnly(unions.ToArray());
        Structs = Array.AsReadOnly(structs.ToArray());
        Tests = Array.AsReadOnly((tests ?? []).ToArray());
        Commands = Array.AsReadOnly((commands ?? []).ToArray());
        EntryCommandId = entryCommandId;
        Routes = Array.AsReadOnly((routes ?? []).ToArray());
    }

    // Retained for single-file API compatibility. For a package, this is the selected entry module.
    public string Module => EntryModule ?? (Modules.Count == 1 ? Modules[0] : string.Empty);
    public IReadOnlyList<string> Modules { get; }
    public string? EntryModule { get; }
    public int? EntryFunctionId { get; }
    public IReadOnlyList<CheckedFunction> Functions { get; }
    public IReadOnlyList<CheckedUnion> Unions { get; }
    public IReadOnlyList<CheckedStruct> Structs { get; }
    public IReadOnlyList<CheckedTest> Tests { get; }
    public IReadOnlyList<CheckedCommand> Commands { get; }
    public int? EntryCommandId { get; }
    public IReadOnlyList<CheckedRoute> Routes { get; }
}

internal sealed record CheckResult(CheckedProgram? Program, List<Diagnostic> Diagnostics);

internal sealed record PackageModuleInput(
    string PackageId,
    ParsedProgram Program,
    IReadOnlyDictionary<string, string> DirectDependencies,
    string PackageDisplayLabel);

internal readonly record struct ModuleIdentity(string PackageId, string ModuleName);

internal sealed record VariantShape(
    string Name,
    string Key,
    int? UnionId,
    int VariantId,
    BuiltinVariant? Builtin,
    IReadOnlyList<LangType> PayloadTypes);

internal static class Compiler
{
    private const string SinglePackageId = "<single-package>";

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
        var inputs = modules.Select(module => new PackageModuleInput(
            SinglePackageId,
            module,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "self")).ToArray();
        return CheckPackage(inputs, SinglePackageId, entryModule);
    }

    public static CheckResult CheckPackage(
        IReadOnlyList<PackageModuleInput> modules,
        string rootPackageId,
        string? entryModule,
        IReadOnlySet<string>? rootCapabilities = null,
        bool rootIsCliPackage = false,
        bool rootIsWebPackage = false)
    {
        var diagnostics = new List<Diagnostic>();
        return new SemanticChecker(diagnostics).CheckPackage(
            modules,
            rootPackageId,
            entryModule,
            requireEntry: entryModule is not null,
            rootCapabilities,
            rootIsCliPackage,
            rootIsWebPackage);
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
    private const string SinglePackageId = "<single-package>";
    private string _rootPackageId = SinglePackageId;
    private readonly List<UnionSymbol> _unions = [];
    private readonly List<StructSymbol> _structs = [];
    private readonly List<FunctionSymbol> _functions = [];
    private readonly List<CheckedTest> _tests = [];
    private readonly List<CommandSymbol> _commands = [];
    private readonly List<CheckedRoute> _routes = [];
    private readonly HashSet<int> _activeTransactionLocals = [];
    private readonly HashSet<(string File, int Line, int Column)> _resourceListDiagnosticLocations = [];
    private bool[] _resourceReachableDeclarations = [];
    private bool[] _illegalListReachableDeclarations = [];
    private readonly Dictionary<ModuleIdentity, ModuleSymbols> _modulesByIdentity = new();
    private readonly Dictionary<string, string> _packageDisplayLabels = new(StringComparer.Ordinal);
    private IReadOnlySet<string> _rootCapabilities = new HashSet<string>(StringComparer.Ordinal);
    private ModuleIdentity _currentModule = new(string.Empty, string.Empty);
    private FunctionSymbol? _currentFunction;
    private bool _semanticDepthReported;
    private bool _rootIsWebPackage;

    public CheckResult CheckSingle(ParsedProgram program) =>
        CheckPackage(
            [new PackageModuleInput(SinglePackageId, program, new Dictionary<string, string>(StringComparer.Ordinal), "self")],
            SinglePackageId,
            program.Module,
            requireEntry: false,
            rootIsWebPackage: true);

    public CheckResult CheckPackage(
        IReadOnlyList<PackageModuleInput> inputs,
        string rootPackageId,
        string? entryModule,
        bool requireEntry,
        IReadOnlySet<string>? rootCapabilities = null,
        bool rootIsCliPackage = false,
        bool rootIsWebPackage = false)
    {
        _rootCapabilities = rootCapabilities ?? new HashSet<string>(StringComparer.Ordinal);
        _rootPackageId = rootPackageId;
        _rootIsWebPackage = rootIsWebPackage;
        BuildPackageDisplayLabels(inputs);
        var orderedModules = new List<ModuleSymbols>(inputs.Count);
        foreach (var input in inputs)
        {
            var identity = new ModuleIdentity(input.PackageId, input.Program.Module);
            if (_modulesByIdentity.ContainsKey(identity))
            {
                Add("E_MODULE_DUPLICATE", $"Module '{input.Program.Module}' is included more than once in package '{input.PackageId}'", input.Program.ModuleAt);
                continue;
            }
            var module = new ModuleSymbols(input);
            _modulesByIdentity.Add(identity, module);
            orderedModules.Add(module);
        }

        // Register every declaration header before resolving signatures, so qualified references
        // across modules and packages can see the complete package graph.
        foreach (var module in orderedModules) RegisterUnionHeaders(module);
        foreach (var module in orderedModules) RegisterStructHeaders(module);
        foreach (var module in orderedModules) RegisterFunctionHeaders(module);
        foreach (var module in orderedModules)
            RegisterCommandHeaders(module, rootPackageId, entryModule, rootIsCliPackage || entryModule is not null);

        foreach (var module in orderedModules) PopulateUnionVariants(module);
        foreach (var module in orderedModules) PopulateStructFields(module);
        ValidateStructRecursion();
        foreach (var module in orderedModules)
        {
            RegisterFunctionSignatures(module);
        }
        foreach (var module in orderedModules)
            RegisterTests(module);
        // Command signatures may refer to handlers declared in any module. Resolve them only
        // after every source function has a fully populated signature, independent of module order.
        foreach (var module in orderedModules)
        {
            RegisterCommandSignatures(module);
        }
        foreach (var module in orderedModules)
        {
            RegisterRoutes(module, rootPackageId, entryModule);
        }

        BuildResourceGraphSummaries();
        ValidatePublicSignatures();

        foreach (var function in _functions)
            CheckFunctionBody(function);

        ValidateResourceListInvariant();
        InferEffectsAndValidateBounds();
        ValidateCommandFormatterEffects();

        FunctionSymbol? entry = null;
        CommandSymbol? entryCommand = null;
        string? routeEntryModule = null;
        if (entryModule is not null)
        {
            var entryIdentity = new ModuleIdentity(rootPackageId, entryModule);
            var entryHasRoutes = false;
            if (_modulesByIdentity.TryGetValue(entryIdentity, out var selectedRootEntryScope))
            {
                entryHasRoutes = selectedRootEntryScope.Program.Routes.Count != 0;
                if (entryHasRoutes)
                {
                    selectedRootEntryScope.DeclaredFunctions.TryGetValue("main", out var selectedMain);
                    if (selectedMain is not null && IsRunnableEntry(selectedMain))
                        Add("E_ROUTE_DECL", "An entry module with routes cannot also declare a runnable main function", selectedMain.Declaration.At);
                    if (selectedRootEntryScope.Command is not null)
                        Add("E_ROUTE_DECL", "An entry module with routes cannot also declare a command", selectedRootEntryScope.Command.Declaration.At);
                }
            }

            if (_rootIsWebPackage && requireEntry)
            {
                if (_modulesByIdentity.TryGetValue(entryIdentity, out var webEntryScope))
                {
                    if (webEntryScope.Program.Routes.Count == 0)
                        Add("E_ENTRYPOINT", $"Web package entry module '{entryModule}' must declare at least one route", webEntryScope.Program.ModuleAt);
                    else if (_routes.Count != 0)
                        routeEntryModule = webEntryScope.Program.Module;
                }
                else
                {
                    var at = inputs.FirstOrDefault(input => input.PackageId == rootPackageId)?.Program.ModuleAt ??
                             inputs.FirstOrDefault()?.Program.ModuleAt ??
                             new Token("id", entryModule, 1, 1, string.Empty);
                    Add("E_ENTRYPOINT", $"Web package entry module '{entryModule}' must exist and declare at least one route", at);
                }
            }
            else
            {
                if (_modulesByIdentity.TryGetValue(entryIdentity, out var standardEntryScope))
                {
                    standardEntryScope.DeclaredFunctions.TryGetValue("main", out var main);
                    if (main is not null && IsRunnableEntry(main))
                    {
                        if (standardEntryScope.Command is not null)
                        {
                            if (!entryHasRoutes)
                                Add("E_COMMAND_DECL", "A module cannot declare both a command and a runnable main function", standardEntryScope.Command.Declaration.At);
                        }
                        else
                            entry = main;
                    }
                    else if (standardEntryScope.Command is not null)
                    {
                        entryCommand = standardEntryScope.Command;
                    }
                }

                if (entry is null && entryCommand is null && requireEntry && !entryHasRoutes)
                {
                    var at = _modulesByIdentity.TryGetValue(entryIdentity, out var entryLocationScope)
                        ? entryLocationScope.Program.ModuleAt
                        : inputs.FirstOrDefault(input => input.PackageId == rootPackageId)?.Program.ModuleAt ??
                          inputs.FirstOrDefault()?.Program.ModuleAt ??
                          new Token("id", entryModule, 1, 1, string.Empty);
                    Add("E_ENTRYPOINT", $"Entry module '{entryModule}' must declare a zero-argument main returning i32, bool, or Text", at);
                }
            }
        }

        if (diagnostics.Count != 0) return new CheckResult(null, diagnostics);

        var unions = _unions.Select(symbol => new CheckedUnion(
            symbol.Id,
            symbol.ModuleIdentity.PackageId,
            symbol.Module,
            symbol.Declaration.Name,
            symbol.Declaration.Public,
            symbol.Type,
            ReadOnly(symbol.Variants),
            symbol.Declaration.At));
        var functions = _functions.Select(symbol => symbol.CheckedFunction!);
        var structs = _structs.Select(symbol => new CheckedStruct(
            symbol.Id,
            symbol.ModuleIdentity.PackageId,
            symbol.Module,
            symbol.Declaration.Name,
            symbol.Declaration.Public,
            symbol.Type,
            ReadOnly(symbol.Fields),
            symbol.Declaration.At));
        var commands = _commands.Select(command => command.ToCheckedCommand());
        return new CheckResult(new CheckedProgram(
            orderedModules.Select(module => module.Program.Module).ToArray(),
            entry?.ModuleName ?? entryCommand?.ModuleName ?? routeEntryModule,
            entry?.Id,
            functions,
            unions,
            structs,
            _tests,
            commands,
            entryCommand?.Id,
            _routes), diagnostics);
    }

    private void BuildPackageDisplayLabels(IReadOnlyList<PackageModuleInput> inputs)
    {
        _packageDisplayLabels.Clear();
        foreach (var input in inputs)
        {
            var label = input.PackageDisplayLabel;
            if (string.IsNullOrWhiteSpace(label) || label.Any(character =>
                    char.IsControl(character) || character is '/' or '\\'))
            {
                throw new InvalidOperationException("Package display labels must be nonempty path-free labels");
            }

            if (_packageDisplayLabels.TryGetValue(input.PackageId, out var existing))
            {
                if (!string.Equals(existing, label, StringComparison.Ordinal))
                    throw new InvalidOperationException("A package ID was associated with inconsistent display labels");
                continue;
            }

            _packageDisplayLabels.Add(input.PackageId, label);
        }
    }

    private static bool IsRunnableEntry(FunctionSymbol function) =>
        function.Declaration.Name == "main" && function.TypeParameters.Count == 0 && function.Parameters.Count == 0 &&
        (function.ReturnType.IsI32 || function.ReturnType.IsBool || function.ReturnType.IsText);

    private void RegisterUnionHeaders(ModuleSymbols module)
    {
        _currentModule = module.Identity;
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

            var symbol = new UnionSymbol(_unions.Count, module.Identity, declaration, LangType.ForUnion(_unions.Count, declaration.Name));
            _unions.Add(symbol);
            module.DeclaredUnions.Add(declaration.Name, symbol);
        }
    }

    private void RegisterStructHeaders(ModuleSymbols module)
    {
        _currentModule = module.Identity;
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

            var symbol = new StructSymbol(_structs.Count, module.Identity, declaration, LangType.ForStruct(_structs.Count, declaration.Name));
            _structs.Add(symbol);
            module.DeclaredStructs.Add(declaration.Name, symbol);
        }
    }

    private void RegisterFunctionHeaders(ModuleSymbols module)
    {
        _currentModule = module.Identity;
        foreach (var declaration in module.Program.Functions)
        {
            if (module.DeclaredFunctions.ContainsKey(declaration.Name))
            {
                Add("E_NAME_DUPLICATE", $"Function '{declaration.Name}' is already declared", declaration.At);
                continue;
            }

            var symbol = new FunctionSymbol(_functions.Count, module.Identity, declaration);
            _functions.Add(symbol);
            module.DeclaredFunctions.Add(declaration.Name, symbol);
        }
    }

    private void RegisterCommandHeaders(
        ModuleSymbols module,
        string rootPackageId,
        string? entryModule,
        bool rootIsCliPackage)
    {
        _currentModule = module.Identity;
        if (module.Program.Commands.Count == 0) return;

        foreach (var duplicate in module.Program.Commands.Skip(1))
            Add("E_COMMAND_DECL", "A module may declare at most one command", duplicate.At);

        if (module.PackageId != SinglePackageId && module.PackageId != rootPackageId)
        {
            foreach (var declaration in module.Program.Commands)
                Add("E_COMMAND_DECL", "Commands must be declared in the root CLI package", declaration.At);
            return;
        }

        if (module.PackageId != SinglePackageId && !rootIsCliPackage)
        {
            foreach (var declaration in module.Program.Commands)
                Add("E_COMMAND_DECL", "Library packages cannot declare commands", declaration.At);
            return;
        }

        if (entryModule is not null &&
            (module.PackageId != rootPackageId || module.Program.Module != entryModule))
        {
            foreach (var declaration in module.Program.Commands)
                Add("E_COMMAND_DECL", "A package command must be declared in the package entry module", declaration.At);
            return;
        }

        var command = module.Program.Commands[0];
        var entries = command.Entries;
        var helps = entries.OfType<CommandHelpSyntax>().ToArray();
        var arguments = entries.OfType<CommandArgumentSyntax>().ToArray();
        var options = entries.OfType<CommandOptionSyntax>().ToArray();
        var flags = entries.OfType<CommandFlagSyntax>().ToArray();
        var handlers = entries.OfType<CommandHandlerSyntax>().ToArray();
        var errors = entries.OfType<CommandErrorSyntax>().ToArray();

        if (helps.Length != 1)
            Add("E_COMMAND_DECL", "A command must declare exactly one help entry", command.At);
        if (arguments.Length == 0)
            Add("E_COMMAND_DECL", "A command must declare at least one positional argument", command.At);
        if (handlers.Length != 1)
            Add("E_COMMAND_DECL", "A command must declare exactly one handler entry", command.At);
        if (errors.Length != 1)
            Add("E_COMMAND_DECL", "A command must declare exactly one error formatter entry", command.At);

        var inputs = new List<CheckedCommandInput>();
        var inputTokens = new List<Token>();
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case CommandArgumentSyntax argument:
                {
                    if (!fieldNames.Add(argument.Name))
                        Add("E_COMMAND_DECL", $"Command input '{argument.Name}' is declared more than once", argument.At);
                    var type = ResolveCommandInputType(argument.Type, argument.At);
                    inputs.Add(new CheckedCommandInput(argument.Name, type, CheckedCommandInputKind.Argument,
                        inputs.Count, argument.Help, null));
                    inputTokens.Add(argument.At);
                    break;
                }
                case CommandOptionSyntax option:
                {
                    if (!fieldNames.Add(option.Name))
                        Add("E_COMMAND_DECL", $"Command input '{option.Name}' is declared more than once", option.At);
                    var type = ResolveCommandInputType(option.Type, option.At);
                    var value = ToCheckedCommandLiteral(option.Default);
                    if (!type.IsError && !CommandDefaultMatches(type, option.Default))
                        Add("E_COMMAND_DECL", $"Default for command option '{option.Name}' does not match its supported type", option.Default.At);
                    inputs.Add(new CheckedCommandInput(option.Name, type, CheckedCommandInputKind.Option,
                        inputs.Count, option.Help, value));
                    inputTokens.Add(option.At);
                    break;
                }
                case CommandFlagSyntax flag:
                {
                    if (!fieldNames.Add(flag.Name))
                        Add("E_COMMAND_DECL", $"Command input '{flag.Name}' is declared more than once", flag.At);
                    inputs.Add(new CheckedCommandInput(flag.Name, LangType.Bool, CheckedCommandInputKind.Flag,
                        inputs.Count, flag.Help, null));
                    inputTokens.Add(flag.At);
                    break;
                }
            }
        }

        var generatedName = CommandArgsTypeName(command.Name);
        if (IsReservedTypeName(generatedName) || module.TypeNames.Contains(generatedName))
        {
            Add("E_COMMAND_DECL", $"Generated command argument type '{generatedName}' collides with a declared type", command.At);
            return;
        }
        module.TypeNames.Add(generatedName);

        var generatedFields = inputs.Select((input, index) => new StructFieldDecl(
            input.Name,
            TypeSyntaxForCommandInput(input.Type, command.At),
            inputTokens[index])).ToArray();
        var generatedDecl = new StructDecl(generatedName, true, generatedFields, command.At);
        var argsType = LangType.ForStruct(_structs.Count, generatedName);
        var argsStruct = new StructSymbol(_structs.Count, module.Identity, generatedDecl, argsType);
        argsStruct.Fields.AddRange(inputs.Select((input, index) => new CheckedStructField(
            input.Name, input.Type, index, generatedFields[index].At)));
        _structs.Add(argsStruct);
        module.DeclaredStructs.Add(generatedName, argsStruct);

        var symbol = new CommandSymbol(
            _commands.Count,
            module.PackageId,
            module.Identity,
            command,
            helps.FirstOrDefault()?.Text ?? string.Empty,
            argsStruct,
            inputs,
            handlers.FirstOrDefault(),
            errors.FirstOrDefault());
        module.Command = symbol;
        _commands.Add(symbol);
    }

    private static string CommandArgsTypeName(string commandName)
    {
        var words = commandName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var name = string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        return name + "Args";
    }

    private static TypeSyntax TypeSyntaxForCommandInput(LangType type, Token at)
    {
        var name = type.Kind switch
        {
            LangTypeKind.I32 => "i32",
            LangTypeKind.Bool => "bool",
            LangTypeKind.Text => "Text",
            LangTypeKind.FilePath => "FilePath",
            _ => "<error>"
        };
        return new TypeSyntax(new SourceDeclarationRefSyntax(null, [], name, at), [], at);
    }

    private LangType ResolveCommandInputType(TypeSyntax syntax, Token at)
    {
        if (syntax.Reference.IsQualified || syntax.Args.Count != 0)
        {
            Add("E_COMMAND_DECL", "Command inputs support only FilePath, Text, and i32", at);
            return LangType.Error;
        }

        return syntax.Reference.Declaration switch
        {
            "FilePath" => LangType.FilePath,
            "Text" => LangType.Text,
            "i32" => LangType.I32,
            _ => ReportUnsupportedCommandInputType(syntax.Reference.Declaration, at)
        };
    }

    private LangType ReportUnsupportedCommandInputType(string name, Token at)
    {
        Add("E_COMMAND_DECL", $"Command input type '{name}' is not supported; use FilePath, Text, or i32", at);
        return LangType.Error;
    }

    private static bool CommandDefaultMatches(LangType type, CommandLiteralSyntax value) =>
        (type.IsI32 && value is CommandIntegerLiteralSyntax) ||
        (type.IsText && value is CommandTextLiteralSyntax) ||
        (type.IsFilePath && value is CommandTextLiteralSyntax path &&
            !string.IsNullOrEmpty(path.Value) && !path.Value.Contains('\0'));

    private static CheckedCommandLiteral ToCheckedCommandLiteral(CommandLiteralSyntax literal) => literal switch
    {
        CommandTextLiteralSyntax text => new CheckedCommandLiteral(CheckedCommandLiteralKind.Text, TextValue: text.Value),
        CommandIntegerLiteralSyntax integer => new CheckedCommandLiteral(CheckedCommandLiteralKind.I32, IntegerValue: integer.Value),
        CommandBooleanLiteralSyntax boolean => new CheckedCommandLiteral(CheckedCommandLiteralKind.Boolean, BooleanValue: boolean.Value),
        _ => throw new InvalidOperationException("Unknown command literal")
    };

    private void RegisterCommandSignatures(ModuleSymbols module)
    {
        _currentModule = module.Identity;
        var command = module.Command;
        if (command is null) return;

        if (command.HandlerSyntax is not null)
        {
            var handler = ResolveFunctionReference(command.HandlerSyntax.Reference);
            if (handler is not null)
            {
                command.HandlerFunction = handler;
                var validReturn = handler.ReturnType.Kind == LangTypeKind.Result &&
                    handler.ReturnType.Arguments.Count == 2 &&
                    handler.ReturnType.Arguments[0].IsText &&
                    IsConcreteSupportedCommandError(handler.ReturnType.Arguments[1]);
                if (validReturn)
                    command.ErrorType = handler.ReturnType.Arguments[1];

                var hasGeneratedArgs = handler.Parameters.Count > 0 &&
                    handler.Parameters[0].Type == command.ArgsStruct.Type;
                var commandCapabilities = new List<CheckedCapabilityParameter>();
                var validParameters = hasGeneratedArgs &&
                    CheckCommandCapabilityParameters(
                        handler,
                        1,
                        commandCapabilities,
                        command.HandlerSyntax.Reference.At);
                command.Capabilities = commandCapabilities;
                if (handler.TypeParameters.Count != 0 || !validParameters || !validReturn)
                {
                    var hasFsWrite = commandCapabilities.Any(capability => capability.Kind == CheckedCapabilityKind.FsWrite);
                    var expectation = hasFsWrite
                        ? "Command handler must take the generated args type, optionally followed by FsRead then FsWrite, and return Result<Text, E> for a concrete error type"
                        : "Command handler must take the generated args type, optionally followed by FsRead, and return Result<Text, E> for a concrete error type";
                    Add("E_COMMAND_HANDLER", expectation, command.HandlerSyntax.Reference.At);
                }
            }
        }

        if (command.ErrorSyntax is not null)
        {
            var formatter = ResolveFunctionReference(command.ErrorSyntax.Reference);
            if (formatter is not null)
            {
                command.ErrorFormatter = formatter;
                var validSignature = formatter.TypeParameters.Count == 0 &&
                    formatter.Parameters.Count == 1 &&
                    (command.ErrorType.IsError || formatter.Parameters[0].Type == command.ErrorType) &&
                    formatter.ReturnType.IsText;
                if (!validSignature)
                    Add("E_COMMAND_HANDLER", "Command error formatter must take exactly E and return Text", command.ErrorSyntax.Reference.At);
                if (formatter.Declaration.IsAsync)
                    Add("E_COMMAND_HANDLER", "Command error formatter must be synchronous", command.ErrorSyntax.Reference.At);
                if (formatter.DeclaredEffects.Count != 0)
                    Add("E_COMMAND_HANDLER", "Command error formatter must declare effects {}", command.ErrorSyntax.Reference.At);
            }
        }
    }

    private static bool IsConcreteSupportedCommandError(LangType type)
    {
        if (type.IsError || type.Kind == LangTypeKind.TypeParameter) return false;
        return type.Arguments.All(IsConcreteSupportedCommandError);
    }

    private void ValidateCommandFormatterEffects()
    {
        foreach (var command in _commands)
        {
            if (command.ErrorFormatter?.CheckedFunction is { InferredEffects.Count: > 0 })
                Add("E_COMMAND_HANDLER", "Command error formatter must be pure", command.ErrorSyntax!.Reference.At);
        }
    }

    private void RegisterRoutes(ModuleSymbols module, string rootPackageId, string? entryModule)
    {
        if (module.Program.Routes.Count == 0) return;

        _currentModule = module.Identity;
        var isRouteEntry = _rootIsWebPackage &&
            module.PackageId == rootPackageId &&
            entryModule is not null &&
            module.Program.Module == entryModule;
        if (!isRouteEntry)
        {
            foreach (var route in module.Program.Routes)
                Add("E_ROUTE_DECL", "Routes may be declared only in the root web package entry module", route.At);
            return;
        }

        var routeKeys = new HashSet<(string Method, string Path)>(RouteKeyComparer.Instance);
        foreach (var route in module.Program.Routes)
        {
            var diagnosticCount = diagnostics.Count;
            if (!routeKeys.Add((route.Method, route.Path)))
                Add("E_ROUTE_DECL", $"Route '{route.Method} {route.Path}' is already declared", route.At);

            var checkedRoute = CheckRoute(route);
            if (checkedRoute is not null && diagnostics.Count == diagnosticCount)
                _routes.Add(checkedRoute);
        }
    }

    private CheckedRoute? CheckRoute(RouteDecl route)
    {
        var routeDiagnosticCount = diagnostics.Count;
        var bodies = route.Items.OfType<RouteBodySyntax>().ToArray();
        var handlers = route.Items.OfType<RouteHandlerSyntax>().ToArray();
        var responses = route.Items.OfType<RouteResponseSyntax>().ToArray();

        var responseStarted = false;
        foreach (var item in route.Items)
        {
            if (item is RouteResponseSyntax)
            {
                responseStarted = true;
                continue;
            }

            if (responseStarted && item is RouteBodySyntax or RouteHandlerSyntax)
                Add("E_ROUTE_DECL", "Route body and handler items must appear before response mappings", item.At);
        }

        for (var i = 1; i < bodies.Length; i++)
            Add("E_ROUTE_DECL", "A route may declare at most one body type", bodies[i].At);
        for (var i = 1; i < handlers.Length; i++)
            Add("E_ROUTE_HANDLER", "A route may declare exactly one handler", handlers[i].At);
        if (handlers.Length == 0)
            Add("E_ROUTE_HANDLER", "A route must declare exactly one handler", route.At);

        StructSymbol? bodyStructure = null;
        if (route.Method == "GET")
        {
            foreach (var body in bodies)
                Add("E_ROUTE_DECL", "GET routes cannot declare a request body", body.At);
        }
        else if (route.Method == "POST")
        {
            if (bodies.Length == 0)
            {
                Add("E_ROUTE_DECL", "POST routes must declare exactly one request body type", route.At);
            }
            else if (bodies[0].Type.Reference.IsQualified == false)
            {
                Add("E_ROUTE_DECL", "Route body types must be fully qualified declared structs", bodies[0].Type.At);
            }
            else if (bodies[0].Type.Args.Count != 0)
            {
                // Still resolve the declaration so visibility and unresolved-name diagnostics are preserved.
                ResolveTypeDeclaration(bodies[0].Type.Reference);
                Add("E_ROUTE_DECL", "Route body types must be non-generic declared structs", bodies[0].Type.At);
            }
            else
            {
                var bodyType = ResolveType(bodies[0].Type, 0);
                if (!bodyType.IsError && bodyType.Kind == LangTypeKind.Struct)
                {
                    var structure = _structs[bodyType.StructId];
                    if (IsSourceDeclaredStruct(structure))
                    {
                        bodyStructure = structure;
                        if (!ContainsRouteTypeError(bodyType, new HashSet<int>()) &&
                            !IsJsonRouteType(bodyType, new HashSet<int>()))
                        {
                            Add("E_ROUTE_CODEC_UNSUPPORTED", "POST route body structs must contain only acyclic i32, bool, Text, and supported struct fields", bodies[0].Type.At);
                        }
                    }
                    else
                        Add("E_ROUTE_DECL", "Route body types must be fully qualified declared structs", bodies[0].Type.At);
                }
                else if (!bodyType.IsError)
                    Add("E_ROUTE_DECL", "Route body types must be fully qualified declared structs", bodies[0].Type.At);
            }
        }

        FunctionSymbol? handler = null;
        UnionSymbol? replyUnion = null;
        var routeCapabilities = new List<CheckedCapabilityParameter>();
        var handlerValid = handlers.Length == 1;
        if (handlers.Length == 1)
        {
            handler = ResolveFunctionReference(handlers[0].Reference);
            if (handler is null)
            {
                handlerValid = false;
            }
            else
            {
                var validParameters = route.Method switch
                {
                    "GET" => CheckRouteCapabilityParameters(handler, 0, routeCapabilities),
                    "POST" when bodyStructure is not null =>
                        handler.Parameters.Count > 0 &&
                        handler.Parameters[0].Type == bodyStructure.Type &&
                        CheckRouteCapabilityParameters(handler, 1, routeCapabilities),
                    // The missing or invalid POST body already has a route declaration diagnostic.
                    "POST" => true,
                    _ => false
                };
                var validReturn = handler.ReturnType.IsError || handler.ReturnType.Kind == LangTypeKind.Union;
                var validGenericity = handler.TypeParameters.Count == 0;

                if (!validParameters || !validReturn || !validGenericity)
                {
                    var hasFsWrite = handler.Parameters.Any(parameter => parameter.Type.IsFsWrite);
                    var capabilityOrder = hasFsWrite ? "FsWrite, DbRead, and DbWrite" : "DbRead and DbWrite";
                    var expectation = route.Method == "GET"
                        ? $"a non-generic function taking optional {capabilityOrder} capabilities in that order, and returning a declared union"
                        : $"a non-generic function taking the route body type followed by optional {capabilityOrder} capabilities in that order, and returning a declared union";
                    Add("E_ROUTE_HANDLER", $"Route handler must be {expectation}", handlers[0].Reference.At);
                    handlerValid = false;
                }

                if (handler.ReturnType.Kind == LangTypeKind.Union)
                    replyUnion = _unions[handler.ReturnType.UnionId];
            }
        }

        var typedResponses = new List<CheckedRouteResponse>();
        var seenResponseNames = new HashSet<string>(StringComparer.Ordinal);
        var coveredVariantIds = new HashSet<int>();
        var suppressMissingResponseCascade = !handlerValid;
        foreach (var response in responses)
        {
            if (!seenResponseNames.Add(response.Variant))
                Add("E_ROUTE_RESPONSE_DUPLICATE", $"Response variant '{response.Variant}' is mapped more than once", response.VariantAt);

            if (response.StatusCode is < 100 or > 599)
                Add("E_ROUTE_DECL", "Response status must be between 100 and 599", response.StatusAt);

            if (replyUnion is null)
                continue;

            var variant = replyUnion.Variants.FirstOrDefault(item => item.Name == response.Variant);
            if (variant is null)
            {
                Add("E_ROUTE_DECL", $"Response variant '{response.Variant}' is not declared by '{replyUnion.Declaration.Name}'", response.VariantAt);
                suppressMissingResponseCascade = true;
                continue;
            }

            coveredVariantIds.Add(variant.Id);
            var typedResponse = CheckRouteResponse(response, variant);
            typedResponses.Add(typedResponse);
        }

        if (replyUnion is not null && !suppressMissingResponseCascade)
        {
            var missing = replyUnion.Variants
                .Where(variant => !coveredVariantIds.Contains(variant.Id))
                .Select(variant => variant.Name)
                .ToArray();
            if (missing.Length != 0)
                Add("E_ROUTE_RESPONSE_MISSING", $"Route responses are missing variants: {string.Join(", ", missing)}", route.At);
        }

        if (diagnostics.Count != routeDiagnosticCount || handler is null || replyUnion is null || !handlerValid)
            return null;

        return new CheckedRoute(
            _routes.Count,
            route.Method,
            route.Path,
            bodyStructure?.Type,
            bodyStructure?.Fields ?? [],
            routeCapabilities,
            handler.Id,
            FormatReference(handlers[0].Reference),
            handler.Declaration.IsAsync,
            handlers[0].At,
            replyUnion.Id,
            typedResponses,
            route.At,
            route.MethodAt,
            route.PathAt,
            bodies.FirstOrDefault()?.At,
            bodies.FirstOrDefault()?.Type.At);
    }

    private bool CheckRouteCapabilityParameters(
        FunctionSymbol handler,
        int firstCapabilityParameter,
        List<CheckedCapabilityParameter> capabilities)
    {
        var seen = new HashSet<LangType>();
        var lastOrder = -1;
        var valid = true;
        for (var index = firstCapabilityParameter; index < handler.Parameters.Count; index++)
        {
            var parameter = handler.Parameters[index];
            var kind = CapabilityKind(parameter.Type);
            var order = kind switch
            {
                CheckedCapabilityKind.FsWrite => 0,
                CheckedCapabilityKind.DbRead => 1,
                CheckedCapabilityKind.DbWrite => 2,
                _ => -1
            };

            if (kind is null || order < 0)
            {
                Add(
                    "E_ROUTE_HANDLER",
                    "Route handlers may receive only DbRead followed by DbWrite capability parameters after the request body",
                    parameter.At);
                valid = false;
                continue;
            }

            if (!seen.Add(parameter.Type))
            {
                Add("E_ROUTE_HANDLER", $"Route handler cannot receive '{parameter.Type.DisplayName}' more than once", parameter.At);
                valid = false;
            }
            if (order < lastOrder)
            {
                var orderMessage = handler.Parameters.Skip(firstCapabilityParameter).Any(item => item.Type.IsFsWrite)
                    ? "Route handler capability parameters must appear in FsWrite, DbRead, DbWrite order"
                    : "Route handler capability parameters must appear in DbRead, DbWrite order";
                Add("E_ROUTE_HANDLER", orderMessage, parameter.At);
                valid = false;
            }
            lastOrder = Math.Max(lastOrder, order);
            capabilities.Add(new CheckedCapabilityParameter(kind.Value, index, parameter.Name, parameter.At));

            var effect = CapabilityEffect(kind.Value);
            if (!_rootCapabilities.Contains(effect))
            {
                Add("E_CAPABILITY_MISSING", $"Route handler requires the root package's {effect} capability grant", parameter.At);
                valid = false;
            }
        }
        return valid;
    }

    private bool CheckCommandCapabilityParameters(
        FunctionSymbol handler,
        int firstCapabilityParameter,
        List<CheckedCapabilityParameter> capabilities,
        Token missingGrantAt)
    {
        var seen = new HashSet<CheckedCapabilityKind>();
        var lastOrder = -1;
        var valid = true;
        for (var index = firstCapabilityParameter; index < handler.Parameters.Count; index++)
        {
            var parameter = handler.Parameters[index];
            var kind = CapabilityKind(parameter.Type);
            var order = kind switch
            {
                CheckedCapabilityKind.FsRead => 0,
                CheckedCapabilityKind.FsWrite => 1,
                _ => -1
            };
            if (kind is null || order < 0)
            {
                valid = false;
                continue;
            }

            if (!seen.Add(kind.Value) || order < lastOrder)
                valid = false;
            lastOrder = Math.Max(lastOrder, order);
            capabilities.Add(new CheckedCapabilityParameter(kind.Value, index, parameter.Name, parameter.At));

            var effect = CapabilityEffect(kind.Value);
            if (!_rootCapabilities.Contains(effect))
            {
                Add(
                    "E_CAPABILITY_MISSING",
                    $"Command handler requires the root package's {effect} capability grant",
                    missingGrantAt);
            }
        }
        return valid;
    }

    private static CheckedCapabilityKind? CapabilityKind(LangType type) => type.Kind switch
    {
        LangTypeKind.FsRead => CheckedCapabilityKind.FsRead,
        LangTypeKind.FsWrite => CheckedCapabilityKind.FsWrite,
        LangTypeKind.DbRead => CheckedCapabilityKind.DbRead,
        LangTypeKind.DbWrite => CheckedCapabilityKind.DbWrite,
        _ => null
    };

    private static string CapabilityEffect(CheckedCapabilityKind kind) => kind switch
    {
        CheckedCapabilityKind.FsRead => "fs.read",
        CheckedCapabilityKind.FsWrite => "fs.write",
        CheckedCapabilityKind.DbRead => "db.read",
        CheckedCapabilityKind.DbWrite => "db.write",
        _ => throw new InvalidOperationException("Unknown checked capability")
    };

    private CheckedRouteResponse CheckRouteResponse(RouteResponseSyntax response, CheckedVariant variant)
    {
        var contentKind = response.Format switch
        {
            "json" => CheckedRouteContentKind.Json,
            "html" => CheckedRouteContentKind.Html,
            _ => (CheckedRouteContentKind?)null
        };
        LangType? payloadType = variant.Fields.Count == 1 ? variant.Fields[0].Type : null;
        var payloadTypeAt = response.BodyType?.At;

        if (ForbidsResponseBody(response.StatusCode) && variant.Fields.Count != 0)
        {
            Add(
                "E_ROUTE_CODEC_UNSUPPORTED",
                $"HTTP status {response.StatusCode} does not allow a response body; map a zero-payload union variant instead",
                response.At);
        }
        else if (variant.Fields.Count == 0)
        {
            if (response.Format is not null || response.BodyType is not null)
                Add("E_ROUTE_CODEC_UNSUPPORTED", "A response variant without a payload cannot declare a content format", response.FormatAt ?? response.At);
        }
        else if (variant.Fields.Count > 1)
        {
            Add("E_ROUTE_CODEC_UNSUPPORTED", "Route responses support only zero-payload or single-payload union variants", response.VariantAt);
        }
        else if (response.Format == "json" && response.BodyType is not null)
        {
            var jsonType = ResolveType(response.BodyType, 0);
            if (!jsonType.IsError && !payloadType!.IsError &&
                (jsonType != payloadType || !IsJsonRouteType(payloadType!, new HashSet<int>())))
            {
                Add("E_ROUTE_CODEC_UNSUPPORTED", "JSON response type must exactly match a payload with a supported JSON shape", response.BodyType.At);
            }
        }
        else if (response.Format == "html" && response.BodyType is null)
        {
            if (!payloadType!.IsHtml)
                Add("E_ROUTE_CODEC_UNSUPPORTED", "The html response format requires an Html payload", response.FormatAt ?? response.At);
        }
        else
        {
            Add("E_ROUTE_CODEC_UNSUPPORTED", "A one-payload response requires 'json PayloadType' or 'html' for an Html payload", response.FormatAt ?? response.VariantAt);
        }

        return new CheckedRouteResponse(
            variant.Id,
            variant.Name,
            response.StatusCode,
            contentKind,
            payloadType,
            response.At,
            response.VariantAt,
            response.StatusAt,
            response.FormatAt,
            payloadTypeAt);
    }

    private static bool ForbidsResponseBody(int statusCode) =>
        statusCode is >= 100 and < 200 or 204 or 205 or 304;

    private bool IsJsonRouteType(LangType type, HashSet<int> activeStructs)
    {
        if (type.Kind is LangTypeKind.I32 or LangTypeKind.Bool or LangTypeKind.Text)
            return true;
        if (type.Kind != LangTypeKind.Struct)
            return false;
        var structure = _structs[type.StructId];
        if (!IsSourceDeclaredStruct(structure))
            return false;
        if (!activeStructs.Add(type.StructId))
            return false;

        var result = structure.Fields.All(field => IsJsonRouteType(field.Type, activeStructs));
        activeStructs.Remove(type.StructId);
        return result;
    }

    private bool ContainsRouteTypeError(LangType type, HashSet<int> activeStructs)
    {
        if (type.IsError)
            return true;
        if (type.Kind == LangTypeKind.Struct)
        {
            if (!activeStructs.Add(type.StructId))
                return false;
            var containsError = _structs[type.StructId].Fields
                .Any(field => ContainsRouteTypeError(field.Type, activeStructs));
            activeStructs.Remove(type.StructId);
            if (containsError)
                return true;
        }
        return type.Arguments.Any(argument => ContainsRouteTypeError(argument, activeStructs));
    }

    private bool IsSourceDeclaredStruct(StructSymbol structure) =>
        _modulesByIdentity[structure.ModuleIdentity].Program.Structs
            .Any(declaration => ReferenceEquals(declaration, structure.Declaration));

    private sealed class RouteKeyComparer : IEqualityComparer<(string Method, string Path)>
    {
        public static RouteKeyComparer Instance { get; } = new();

        public bool Equals((string Method, string Path) left, (string Method, string Path) right) =>
            StringComparer.Ordinal.Equals(left.Method, right.Method) &&
            StringComparer.OrdinalIgnoreCase.Equals(left.Path, right.Path);

        public int GetHashCode((string Method, string Path) route) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(route.Method),
                StringComparer.OrdinalIgnoreCase.GetHashCode(route.Path));
    }

    private void PopulateUnionVariants(ModuleSymbols module)
    {
        _currentModule = module.Identity;
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
        _currentModule = module.Identity;
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
        _currentModule = module.Identity;
        foreach (var declaration in module.Program.Functions)
        {
            if (!module.DeclaredFunctions.TryGetValue(declaration.Name, out var symbol) || symbol.Declaration != declaration)
                continue;

            var typeParameters = new List<LangType>();
            var typeParametersByName = new Dictionary<string, LangType>(StringComparer.Ordinal);
            foreach (var (typeParameter, ordinal) in declaration.TypeParameters.Select((parameter, index) => (parameter, index)))
            {
                var type = LangType.ForTypeParameter(symbol.Id, ordinal, typeParameter.Name);
                typeParameters.Add(type);

                if (!typeParametersByName.TryAdd(typeParameter.Name, type))
                {
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' is already declared", typeParameter.At);
                    continue;
                }

                if (IsReservedTypeName(typeParameter.Name))
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' is reserved", typeParameter.At);
                else if (module.TypeNames.Contains(typeParameter.Name))
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' collides with an existing type name", typeParameter.At);
            }

            symbol.TypeParameters = ReadOnly(typeParameters);
            symbol.TypeParametersByName = typeParametersByName;

            var parameters = new List<CheckedParameter>();
            var localNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < declaration.Parameters.Count; i++)
            {
                var parameter = declaration.Parameters[i];
                if (!localNames.Add(parameter.Name))
                    Add("E_NAME_DUPLICATE", $"Parameter '{parameter.Name}' is already declared", parameter.At);
                parameters.Add(new CheckedParameter(parameter.Name, ResolveType(parameter.Type, 0, typeParametersByName), i, parameter.At));
            }

            symbol.Parameters = ReadOnly(parameters);
            symbol.ReturnType = ResolveType(declaration.ReturnType, 0, typeParametersByName);

            foreach (var (typeParameter, ordinal) in declaration.TypeParameters.Select((parameter, index) => (parameter, index)))
            {
                if (!typeParametersByName.TryGetValue(typeParameter.Name, out var resolvedParameter) ||
                    resolvedParameter.TypeParameterOrdinal != ordinal)
                    continue;

                if (!parameters.Any(parameter => ContainsType(parameter.Type, resolvedParameter)))
                    Add("E_TYPE_MISMATCH", $"Type parameter '{typeParameter.Name}' must appear in at least one function parameter type for inference", typeParameter.At);
            }

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

    private void RegisterTests(ModuleSymbols module)
    {
        _currentModule = module.Identity;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var test in module.Program.Tests)
        {
            if (!names.Add(test.Name))
            {
                Add("E_NAME_DUPLICATE", $"Test '{test.Name}' is already declared in this module", test.NameAt);
                continue;
            }

            var id = _functions.Count;
            var internalName = $"$test_{id}";
            var body = test.Setup
                .Cast<Stmt>()
                .Append(new ReturnStmt(test.AssertAt, test.Assertion))
                .ToArray();
            var declaration = new FunctionDecl(
                internalName,
                [],
                Public: false,
                IsAsync: false,
                Parameters: [],
                new TypeSyntax(new SourceDeclarationRefSyntax(null, [], "bool", test.AssertAt), [], test.AssertAt),
                Effects: [],
                body,
                test.At);
            var function = new FunctionSymbol(id, module.Identity, declaration, test.Name)
            {
                ReturnType = LangType.Bool,
                DeclaredEffects = []
            };

            // Synthetic functions participate in local checking and effect inference, but never
            // enter the source function maps used by name lookup and imports.
            _functions.Add(function);
            _tests.Add(new CheckedTest(test.Name, module.PackageId, module.Program.Module, id, test.At));
        }
    }

    private static bool IsReservedTypeName(string name) =>
        name is "i32" or "bool" or "Text" or "Html" or "FilePath" or "Option" or "Result" or
            "FsRead" or "FsWrite" or "FsError" or "DbRead" or "DbWrite" or "Transaction" or "DbError";

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
        if (type.IsError || type.Kind == LangTypeKind.TypeParameter) return;
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
        _currentModule = function.ModuleIdentity;
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
            function.PackageId,
            function.ModuleName,
            function.Declaration.Name,
            function.Declaration.Public,
            function.Declaration.IsAsync,
            function.Parameters,
            function.TypeParameters,
            function.ReturnType,
            ReadOnly(body),
            function.Calls
                .Select(call => new CheckedDirectCall(
                    call.Target.PackageId,
                    call.Target.ModuleName,
                    call.Target.Declaration.Name)),
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
                    var localType = ResolveType(let.Type, 0, _currentFunction!.TypeParametersByName);
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
                case VarStmt variable:
                {
                    var localType = ResolveType(variable.Type, 0, _currentFunction!.TypeParametersByName);
                    var value = CheckExpr(variable.Value, localType, locals, 0);
                    if (!localType.IsError && ContainsResourceHandle(localType))
                        Add("E_RESOURCE_ESCAPE", "Resource handles cannot be stored in mutable local bindings", variable.NameAt);

                    var id = _nextLocalId++;
                    if (locals.ContainsKey(variable.Name))
                    {
                        Add("E_NAME_DUPLICATE", $"Local '{variable.Name}' is already declared in this scope", variable.NameAt);
                    }
                    else
                    {
                        locals.Add(variable.Name, new LocalSymbol(id, localType, IsMutable: true));
                    }
                    typedStatements.Add(new TypedLetStmt(id, variable.Name, localType, value, variable.At));
                    break;
                }
                case AssignmentStmt assignment:
                {
                    if (!locals.TryGetValue(assignment.Name, out var local))
                    {
                        Add("E_NAME_UNRESOLVED", $"Name '{assignment.Name}' is not in scope", assignment.NameAt);
                        _ = CheckExpr(assignment.Value, null, locals, 0);
                        break;
                    }

                    var value = CheckExpr(assignment.Value, local.Type, locals, 0);
                    if (ContainsResourceHandle(local.Type))
                    {
                        Add("E_RESOURCE_ESCAPE", $"Resource handle '{assignment.Name}' cannot be rebound", assignment.NameAt);
                        break;
                    }
                    if (!local.IsMutable)
                    {
                        Add("E_ASSIGN_IMMUTABLE", $"Cannot assign to immutable local '{assignment.Name}'", assignment.NameAt);
                        break;
                    }

                    typedStatements.Add(new TypedAssignStmt(local.Id, value, assignment.At));
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
                case WithTransactionStmt transaction:
                    guaranteesReturn = CheckTransactionScope(transaction, typedStatements, locals);
                    break;
                case ForStmt loop:
                {
                    var collection = CheckExpr(loop.Collection, null, locals, 0);
                    var itemType = LangType.Error;
                    if (!collection.Type.IsError)
                    {
                        if (!collection.Type.IsList)
                        {
                            Add("E_TYPE_MISMATCH", $"For loops require a List<T> value, found '{collection.Type.DisplayName}'", loop.Collection.At);
                        }
                        else
                        {
                            itemType = collection.Type.Arguments[0];
                            if (ContainsResourceHandle(itemType))
                                Add("E_RESOURCE_ESCAPE", "Resource handles cannot be iterated from a list", loop.NameAt);
                        }
                    }

                    var itemId = _nextLocalId++;
                    var bodyLocals = new Dictionary<string, LocalSymbol>(locals, StringComparer.Ordinal);
                    if (bodyLocals.ContainsKey(loop.Name))
                    {
                        Add("E_NAME_DUPLICATE", $"Local '{loop.Name}' is already declared in this scope", loop.NameAt);
                    }
                    else
                    {
                        bodyLocals.Add(loop.Name, new LocalSymbol(itemId, itemType));
                    }

                    var body = new List<TypedStmt>();
                    _ = CheckStatements(loop.Body, body, bodyLocals);
                    typedStatements.Add(new TypedForStmt(
                        collection,
                        new BoundLocal(loop.Name, itemId, itemType, loop.NameAt),
                        ReadOnly(body),
                        loop.At));
                    // A loop may execute zero times, so its body cannot guarantee a return.
                    break;
                }
                default:
                    Add("E_UNSUPPORTED", "Statement is not implemented in this language slice", statement.At);
                    break;
            }
        }

        return guaranteesReturn;
    }

    private bool CheckTransactionScope(
        WithTransactionStmt transaction,
        List<TypedStmt> typedStatements,
        Dictionary<string, LocalSymbol> locals)
    {
        TypedExpr database;
        if (transaction.Begin is MemberCallExpr beginCall && beginCall.Member == "begin")
        {
            var diagnosticsBeforeReceiver = diagnostics.Count;
            database = CheckExpr(beginCall.Target, LangType.DbWrite, locals, 0);
            if (beginCall.Arguments.Count != 0)
                Add("E_TYPE_MISMATCH", $"Intrinsic 'DbWrite.begin' expects 0 arguments, got {beginCall.Arguments.Count}", beginCall.MemberAt);
            foreach (var argument in beginCall.Arguments)
                _ = CheckExpr(argument, null, locals, 0);

            if (database.Type.IsDbWrite && beginCall.Arguments.Count == 0 && diagnostics.Count == diagnosticsBeforeReceiver)
                _currentFunction?.DirectEffects.Add(new DirectEffectCall("db.write", "DbWrite.begin", beginCall.MemberAt));
        }
        else
        {
            _ = CheckExpr(transaction.Begin, null, locals, 0);
            Add("E_TYPE_MISMATCH", "Transaction scope requires a direct DbWrite.begin() call", transaction.Begin.At);
            database = new TypedErrorExpr(transaction.Begin.At);
        }

        var transactionLocalId = _nextLocalId++;
        var bodyLocals = new Dictionary<string, LocalSymbol>(locals, StringComparer.Ordinal);
        if (bodyLocals.ContainsKey(transaction.Name))
        {
            Add("E_NAME_DUPLICATE", $"Local '{transaction.Name}' is already declared in this scope", transaction.NameAt);
        }
        else
        {
            bodyLocals.Add(transaction.Name, new LocalSymbol(transactionLocalId, LangType.Transaction));
        }

        _activeTransactionLocals.Add(transactionLocalId);
        var body = new List<TypedStmt>();
        bool bodyReturns;
        try
        {
            bodyReturns = CheckStatements(transaction.Body, body, bodyLocals);
        }
        finally
        {
            _activeTransactionLocals.Remove(transactionLocalId);
        }

        typedStatements.Add(new TypedWithTransactionStmt(
            database,
            transactionLocalId,
            transaction.Name,
            ReadOnly(body),
            transaction.At));
        return bodyReturns;
    }

    private void ValidateResourceListInvariant()
    {
        foreach (var union in _unions)
        foreach (var variant in union.Variants)
        foreach (var field in variant.Fields)
            ValidateResourceListType(field.Type, field.At);

        foreach (var structure in _structs)
        foreach (var field in structure.Fields)
            ValidateResourceListType(field.Type, field.At);

        foreach (var function in _functions)
        {
            foreach (var parameter in function.Parameters)
                ValidateResourceListType(parameter.Type, parameter.At);
            ValidateResourceListType(function.ReturnType, function.Declaration.ReturnType.At);
            if (function.CheckedFunction is not null)
                ValidateResourceListStatements(function.CheckedFunction.Body);
        }

        foreach (var command in _commands)
        {
            ValidateResourceListType(command.ArgsStruct.Type, command.Declaration.At);
            foreach (var input in command.Inputs)
                ValidateResourceListType(input.Type, command.Declaration.At);
            ValidateResourceListType(command.ErrorType, command.Declaration.At);
        }

        foreach (var route in _routes)
        {
            if (route.BodyType is not null)
                ValidateResourceListType(route.BodyType, route.BodyTypeAt ?? route.At);
            foreach (var field in route.BodySchema)
                ValidateResourceListType(field.Type, field.At);
            foreach (var response in route.Responses)
                if (response.PayloadType is not null)
                    ValidateResourceListType(response.PayloadType, response.PayloadTypeAt ?? response.At);
        }
    }

    private void ValidateResourceListType(LangType type, Token at)
    {
        if (!ContainsIllegalResourceList(type))
            return;

        if (_resourceListDiagnosticLocations.Add((at.File, at.Line, at.Column)))
            Add("E_RESOURCE_ESCAPE", "Lists cannot contain resource handles, directly or through nested types", at);
    }

    private void BuildResourceGraphSummaries()
    {
        var declarationCount = _structs.Count + _unions.Count;
        var dependents = Enumerable.Range(0, declarationCount)
            .Select(_ => new HashSet<int>())
            .ToArray();
        var reverseDependents = Enumerable.Range(0, declarationCount)
            .Select(_ => new List<int>())
            .ToArray();
        var directlyContainsResource = new bool[declarationCount];
        var listElementTypes = Enumerable.Range(0, declarationCount)
            .Select(_ => new List<LangType>())
            .ToArray();

        foreach (var structure in _structs)
        foreach (var field in structure.Fields)
            CollectResourceGraphFacts(
                field.Type,
                structure.Id,
                dependents,
                directlyContainsResource,
                listElementTypes);

        foreach (var union in _unions)
        foreach (var variant in union.Variants)
        foreach (var field in variant.Fields)
            CollectResourceGraphFacts(
                field.Type,
                _structs.Count + union.Id,
                dependents,
                directlyContainsResource,
                listElementTypes);

        for (var source = 0; source < dependents.Length; source++)
        foreach (var target in dependents[source])
            reverseDependents[target].Add(source);

        _resourceReachableDeclarations = ComputeReverseReachability(
            directlyContainsResource,
            reverseDependents);

        var directlyContainsIllegalList = new bool[declarationCount];
        for (var declaration = 0; declaration < declarationCount; declaration++)
        foreach (var elementType in listElementTypes[declaration])
        {
            if (!ContainsResourceHandle(elementType, _resourceReachableDeclarations))
                continue;
            directlyContainsIllegalList[declaration] = true;
            break;
        }

        _illegalListReachableDeclarations = ComputeReverseReachability(
            directlyContainsIllegalList,
            reverseDependents);
    }

    private void CollectResourceGraphFacts(
        LangType root,
        int sourceDeclaration,
        HashSet<int>[] dependents,
        bool[] directlyContainsResource,
        List<LangType>[] listElementTypes)
    {
        var pending = new Stack<LangType>();
        pending.Push(root);
        while (pending.TryPop(out var type))
        {
            if (IsResourceHandle(type))
            {
                directlyContainsResource[sourceDeclaration] = true;
                continue;
            }

            if (type.IsList)
                listElementTypes[sourceDeclaration].Add(type.Arguments[0]);

            if (type.Kind == LangTypeKind.Struct)
            {
                dependents[sourceDeclaration].Add(type.StructId);
                continue;
            }
            if (type.Kind == LangTypeKind.Union)
            {
                dependents[sourceDeclaration].Add(_structs.Count + type.UnionId);
                continue;
            }

            foreach (var argument in type.Arguments)
                pending.Push(argument);
        }
    }

    private static bool[] ComputeReverseReachability(bool[] roots, IReadOnlyList<List<int>> reverseDependents)
    {
        var reachable = (bool[])roots.Clone();
        var pending = new Queue<int>();
        for (var declaration = 0; declaration < roots.Length; declaration++)
            if (roots[declaration])
                pending.Enqueue(declaration);

        while (pending.TryDequeue(out var target))
        foreach (var dependent in reverseDependents[target])
        {
            if (reachable[dependent])
                continue;
            reachable[dependent] = true;
            pending.Enqueue(dependent);
        }

        return reachable;
    }

    private bool ContainsIllegalResourceList(LangType type)
    {
        var pending = new Stack<LangType>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (current.IsList && ContainsResourceHandle(current.Arguments[0], _resourceReachableDeclarations))
                return true;

            if (TryGetDeclarationNode(current, out var declaration)
                && _illegalListReachableDeclarations[declaration])
                return true;

            if (current.Kind is LangTypeKind.Struct or LangTypeKind.Union)
                continue;

            foreach (var argument in current.Arguments)
                pending.Push(argument);
        }

        return false;
    }

    private void ValidateResourceListStatements(IEnumerable<TypedStmt> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case TypedLetStmt let:
                    ValidateResourceListType(let.Type, let.At);
                    ValidateResourceListExpression(let.Value);
                    break;
                case TypedAssignStmt assignment:
                    ValidateResourceListExpression(assignment.Value);
                    break;
                case TypedReturnStmt ret:
                    ValidateResourceListExpression(ret.Value);
                    break;
                case TypedIfStmt conditional:
                    ValidateResourceListExpression(conditional.Condition);
                    ValidateResourceListStatements(conditional.ThenBody);
                    if (conditional.ElseBody is not null)
                        ValidateResourceListStatements(conditional.ElseBody);
                    break;
                case TypedForStmt loop:
                    ValidateResourceListType(loop.Item.Type, loop.Item.At);
                    ValidateResourceListExpression(loop.Collection);
                    ValidateResourceListStatements(loop.Body);
                    break;
                case TypedWithTransactionStmt transaction:
                    ValidateResourceListExpression(transaction.Database);
                    ValidateResourceListStatements(transaction.Body);
                    break;
            }
        }
    }

    private void ValidateResourceListExpression(TypedExpr expression)
    {
        ValidateResourceListType(expression.Type, expression.At);
        switch (expression)
        {
            case TypedListExpr list:
                foreach (var item in list.Items)
                    ValidateResourceListExpression(item);
                break;
            case TypedBinaryExpr binary:
                ValidateResourceListExpression(binary.Left);
                ValidateResourceListExpression(binary.Right);
                break;
            case TypedCompareExpr comparison:
                ValidateResourceListExpression(comparison.Left);
                ValidateResourceListExpression(comparison.Right);
                break;
            case TypedCallExpr call:
                foreach (var typeArgument in call.TypeArguments)
                    ValidateResourceListType(typeArgument, call.At);
                foreach (var argument in call.Arguments)
                    ValidateResourceListExpression(argument);
                break;
            case TypedAwaitExpr awaited:
                ValidateResourceListExpression(awaited.Value);
                break;
            case TypedDatabaseCallExpr databaseCall:
                ValidateResourceListExpression(databaseCall.Receiver);
                ValidateResourceListExpression(databaseCall.Parameters);
                break;
            case TypedTextLengthExpr length:
                ValidateResourceListExpression(length.Target);
                break;
            case TypedTextTrimExpr trim:
                ValidateResourceListExpression(trim.Target);
                break;
            case TypedListLengthExpr length:
                ValidateResourceListExpression(length.Target);
                break;
            case TypedListGetExpr get:
                ValidateResourceListExpression(get.Target);
                ValidateResourceListExpression(get.Index);
                break;
            case TypedListAppendExpr append:
                ValidateResourceListExpression(append.Target);
                ValidateResourceListExpression(append.Value);
                break;
            case TypedIntrinsicCallExpr intrinsic:
                foreach (var argument in intrinsic.Arguments)
                    ValidateResourceListExpression(argument);
                break;
            case TypedBuiltinConstructExpr builtin:
                foreach (var argument in builtin.Arguments)
                    ValidateResourceListExpression(argument);
                break;
            case TypedUnionConstructExpr variant:
                foreach (var argument in variant.Arguments)
                    ValidateResourceListExpression(argument);
                break;
            case TypedStructConstructExpr structure:
                foreach (var field in structure.Fields)
                    ValidateResourceListExpression(field.Value);
                break;
            case TypedFieldAccessExpr field:
                ValidateResourceListExpression(field.Target);
                break;
            case TypedMatchExpr match:
                ValidateResourceListExpression(match.Value);
                foreach (var arm in match.Arms)
                {
                    if (arm.Pattern is TypedVariantPattern variantPattern)
                    foreach (var binding in variantPattern.Bindings)
                        ValidateResourceListType(binding.Type, binding.At);
                    ValidateResourceListExpression(arm.Body);
                }
                break;
        }
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
            var effectPaths = orderedEffects
                .Select(effect => FindShortestEffectPath(function, effect))
                .ToArray();
            function.CheckedFunction?.SetInferredEffects(orderedEffects, effectPaths);

            var declared = new HashSet<string>(function.DeclaredEffects, StringComparer.Ordinal);
            foreach (var effectPath in effectPaths)
            {
                var effect = effectPath.Effect;
                if (declared.Contains(effect)) continue;
                Add(
                    "E_EFFECT_EXCEEDED",
                    $"Effect '{effect}' is not declared by function '{FormatFunctionName(function)}'; shortest call path: {string.Join(" -> ", effectPath.Steps)}",
                    function.Declaration.At);
            }
        }
    }

    private CheckedEffectPath FindShortestEffectPath(FunctionSymbol root, string effect)
    {
        var queue = new Queue<(FunctionSymbol Function, FunctionSymbol[] Path)>();
        var visited = new HashSet<int> { root.Id };
        queue.Enqueue((root, [root]));

        while (queue.Count != 0)
        {
            var current = queue.Dequeue();
            var direct = current.Function.DirectEffects
                .Where(call => call.Effect == effect)
                .OrderBy(call => call.At.Line)
                .ThenBy(call => call.At.Column)
                .FirstOrDefault();
            if (direct is not null)
            {
                var steps = current.Path.Select(FormatFunctionName).Append(direct.IntrinsicName);
                return new CheckedEffectPath(
                    effect,
                    steps,
                    current.Path.Select(pathFunction => pathFunction.Id),
                    direct.IntrinsicName);
            }

            foreach (var call in current.Function.Calls
                         .OrderBy(call => StableFunctionOrderKey(call.Target), StringComparer.Ordinal)
                         .ThenBy(call => call.At.Line)
                         .ThenBy(call => call.At.Column))
            {
                if (!visited.Add(call.Target.Id)) continue;
                queue.Enqueue((call.Target, current.Path.Append(call.Target).ToArray()));
            }
        }

        throw new InvalidOperationException(
            $"Inferred effect '{effect}' has no direct intrinsic path from '{FormatFunctionName(root)}'");
    }

    private string StableFunctionOrderKey(FunctionSymbol function)
    {
        if (!_packageDisplayLabels.TryGetValue(function.PackageId, out var packageDisplayLabel))
            throw new InvalidOperationException("A function package ID has no validated display label");

        var functionName = function.TestName ?? function.Declaration.Name;
        return $"{packageDisplayLabel}::{function.ModuleName}::{functionName}";
    }

    private string FormatFunctionName(FunctionSymbol function)
    {
        var functionName = function.TestName is { } testName
            ? $"test({testName})"
            : function.Declaration.Name;
        var moduleName = FormatModuleIdentity(function.ModuleIdentity);
        if (function.PackageId == _rootPackageId)
            return $"{moduleName}::{functionName}";
        if (!_packageDisplayLabels.TryGetValue(function.PackageId, out var packageDisplayLabel))
            throw new InvalidOperationException("A function package ID has no validated display label");
        return $"{packageDisplayLabel}::{moduleName}::{functionName}";
    }

    private static string FormatModuleIdentity(ModuleIdentity identity) => identity.ModuleName;

    private TypedExpr CheckExpr(
        Expr expression,
        LangType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth,
        bool isAwaitOperand = false)
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
            ListExpr list => CheckListLiteral(list, expected, locals, depth + 1),
            NameExpr name => CheckName(name, expected, locals),
            DeclarationRefExpr reference => CheckDeclarationReference(reference),
            BinaryExpr binary => CheckBinary(binary, locals, depth + 1),
            CallExpr call => CheckCall(call, expected, locals, depth + 1, isAwaitOperand),
            MemberCallExpr call => CheckMemberCall(call, expected, locals, depth + 1, isAwaitOperand),
            AwaitExpr awaited => CheckAwait(awaited, locals, depth + 1),
            StructConstructExpr structure => CheckStructConstruction(structure, locals, depth + 1),
            FieldAccessExpr access => CheckFieldAccess(access, locals, depth + 1),
            MatchExpr match => CheckMatch(match, expected, locals, depth + 1),
            _ => UnsupportedExpr(expression)
        };

        if (expected is not null && !expected.IsError && !result.Type.IsError && result.Type != expected)
            AddMismatch(expected, result.Type, expression.At);
        return result;
    }

    private TypedExpr CheckAwait(
        AwaitExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (_currentFunction?.Declaration.IsAsync != true)
            Add("E_AWAIT_CONTEXT", "The 'await' expression is only valid inside an async function", expression.At);

        var diagnosticsBeforeOperand = diagnostics.Count;
        var value = CheckExpr(expression.Value, null, locals, depth, isAwaitOperand: true);
        if (value is TypedCallExpr { IsAsync: true } or
            TypedIntrinsicCallExpr { Intrinsic: BuiltinIntrinsic.FsReadTextAsync })
            return new TypedAwaitExpr(value.Type, value, expression.At);

        if (value.Type.IsError)
            return new TypedErrorExpr(expression.At);

        if (expression.Value is CallExpr or MemberCallExpr)
        {
            if (!diagnostics.Skip(diagnosticsBeforeOperand).Any(diagnostic => diagnostic.Code == "E_AWAIT_SYNC"))
                Add("E_AWAIT_SYNC", "The 'await' expression requires an async function call or async intrinsic", expression.At);
        }
        else
        {
            Add("E_AWAIT_TARGET", "The 'await' expression requires an async function call or async intrinsic", expression.At);
        }
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckListLiteral(
        ListExpr expression,
        LangType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var expectedItemType = expected is not null && expected.IsList ? expected.Arguments[0] : null;
        if (expression.Items.Count == 0)
        {
            if (expectedItemType is not null)
                return new TypedListExpr(LangType.List(expectedItemType), [], expression.At);
            if (expected?.IsError == true)
                return new TypedErrorExpr(expression.At);
            if (expected is null)
                Add("E_TYPE_MISMATCH", "Empty list literal requires an expected type of List<T>", expression.At);
            else
                AddMismatch(expected, "List<T>", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var values = new List<TypedExpr>(expression.Items.Count);
        values.Add(CheckExpr(expression.Items[0], expectedItemType, locals, depth));
        var itemType = expectedItemType ?? values[0].Type;
        for (var i = 1; i < expression.Items.Count; i++)
            values.Add(CheckExpr(expression.Items[i], itemType, locals, depth));

        return new TypedListExpr(LangType.List(itemType), ReadOnly(values), expression.At);
    }

    private TypedExpr CheckDeclarationReference(DeclarationRefExpr expression)
    {
        Add("E_NAME_UNRESOLVED", $"Declaration '{FormatReference(expression.Reference)}' is not a value", expression.At);
        return new TypedErrorExpr(expression.At);
    }

    private static string FormatReference(SourceDeclarationRefSyntax reference) =>
        reference.IsQualified
            ? $"{reference.Root}::{string.Join("::", reference.Module.Append(reference.Declaration))}"
            : reference.Declaration;

    private bool TryResolveTargetModule(SourceDeclarationRefSyntax reference, out ModuleSymbols? target)
    {
        target = null;
        if (!reference.IsQualified)
        {
            Add("E_NAME_UNRESOLVED", $"Declaration '{reference.Declaration}' must be referenced with a qualified module path", reference.At);
            return false;
        }

        var current = CurrentModule;
        string targetPackage;
        if (reference.Root == "self")
        {
            targetPackage = current.PackageId;
        }
        else if (!current.DirectDependencies.TryGetValue(reference.Root!, out targetPackage!))
        {
            Add("E_NAME_UNRESOLVED", $"Dependency alias '{reference.Root}' is not declared by package '{current.PackageId}'", reference.At);
            return false;
        }

        var moduleName = string.Join("::", reference.Module);
        var identity = new ModuleIdentity(targetPackage, moduleName);
        if (_modulesByIdentity.TryGetValue(identity, out target)) return true;

        var owner = reference.Root == "self" ? "this package" : $"dependency '{reference.Root}'";
        Add("E_NAME_UNRESOLVED", $"Module '{moduleName}' is not part of {owner}", reference.At);
        return false;
    }

    private FunctionSymbol? ResolveFunctionReference(SourceDeclarationRefSyntax reference)
    {
        if (!TryResolveTargetModule(reference, out var target)) return null;
        if (!target!.DeclaredFunctions.TryGetValue(reference.Declaration, out var function))
        {
            Add("E_NAME_UNRESOLVED", $"Module '{target.Program.Module}' does not declare function '{reference.Declaration}'", reference.At);
            return null;
        }
        if (target.Identity != CurrentModule.Identity && !function.Declaration.Public)
        {
            Add("E_ACCESS_PRIVATE", $"Function '{FormatReference(reference)}' is private", reference.At);
            return null;
        }
        return function;
    }

    private UnionSymbol? ResolveUnionReference(SourceDeclarationRefSyntax reference)
    {
        if (!TryResolveTargetModule(reference, out var target)) return null;
        if (!target!.DeclaredUnions.TryGetValue(reference.Declaration, out var union))
        {
            Add("E_NAME_UNRESOLVED", $"Module '{target.Program.Module}' does not declare union '{reference.Declaration}'", reference.At);
            return null;
        }
        if (target.Identity != CurrentModule.Identity && !union.Declaration.Public)
        {
            Add("E_ACCESS_PRIVATE", $"Union '{FormatReference(reference)}' is private", reference.At);
            return null;
        }
        return union;
    }

    private (UnionSymbol? Union, StructSymbol? Struct) ResolveTypeDeclaration(SourceDeclarationRefSyntax reference)
    {
        if (!TryResolveTargetModule(reference, out var target)) return (null, null);
        if (target!.DeclaredUnions.TryGetValue(reference.Declaration, out var union))
        {
            if (target.Identity != CurrentModule.Identity && !union.Declaration.Public)
            {
                Add("E_ACCESS_PRIVATE", $"Union '{FormatReference(reference)}' is private", reference.At);
                return (null, null);
            }
            return (union, null);
        }
        if (target.DeclaredStructs.TryGetValue(reference.Declaration, out var structure))
        {
            if (target.Identity != CurrentModule.Identity && !structure.Declaration.Public)
            {
                Add("E_ACCESS_PRIVATE", $"Struct '{FormatReference(reference)}' is private", reference.At);
                return (null, null);
            }
            return (null, structure);
        }

        Add("E_NAME_UNRESOLVED", $"Module '{target.Program.Module}' does not declare type '{reference.Declaration}'", reference.At);
        return (null, null);
    }

    private TypedExpr CheckName(NameExpr expression, LangType? expected, Dictionary<string, LocalSymbol> locals)
    {
        if (locals.TryGetValue(expression.Name, out var local))
        {
            if (local.Type.IsTransaction)
            {
                Add("E_RESOURCE_ESCAPE", $"Transaction '{expression.Name}' may only be used as the direct receiver of execute() or commit() inside its with scope", expression.At);
                return new TypedErrorExpr(expression.At);
            }
            return new TypedLocalExpr(local.Type, local.Id, expression.At);
        }

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
        var (union, structure) = ResolveTypeDeclaration(expression.Reference);
        if (structure is null)
        {
            foreach (var value in expression.Fields)
                _ = CheckExpr(value.Value, null, locals, depth);

            if (union is not null)
                Add("E_TYPE_MISMATCH", $"Type '{FormatReference(expression.Reference)}' is a union, not a struct", expression.At);
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
        if (expression.Target is DeclarationRefExpr declarationReference)
        {
            var union = ResolveUnionReference(declarationReference.Reference);
            if (union is null) return new TypedErrorExpr(expression.At);
            return CheckVariantConstruction(
                union,
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
                Add("E_TYPE_MISMATCH", "FsError variants can only be produced by filesystem operations", expression.At);
            else
                Add("E_NAME_UNRESOLVED", $"Variant '{expression.Field}' is not declared on FsError", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Target is NameExpr dbErrorName &&
            !locals.ContainsKey(dbErrorName.Name) &&
            dbErrorName.Name == "DbError")
        {
            if (IsDbErrorVariant(expression.Field))
                Add("E_TYPE_MISMATCH", "DbError variants can only be produced by SQLite operations", expression.At);
            else
                Add("E_NAME_UNRESOLVED", $"Variant '{expression.Field}' is not declared on DbError", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var target = CheckExpr(expression.Target, null, locals, depth);
        if (target.Type.IsError) return new TypedErrorExpr(expression.At);
        if (target.Type.IsText && expression.Field == "length")
            return new TypedTextLengthExpr(target, expression.At);
        if (target.Type.IsList && expression.Field == "length")
            return new TypedListLengthExpr(target, expression.At);
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

        var left = CheckExpr(expression.Left, null, locals, depth);
        var right = CheckExpr(expression.Right, null, locals, depth);
        if (left.Type.IsError || right.Type.IsError)
            return new TypedErrorExpr(expression.At);
        if (!left.Type.IsI32 || !right.Type.IsI32)
        {
            Add("E_TYPE_MISMATCH", $"Arithmetic '{expression.Op}' requires i32 operands", expression.At);
            return new TypedErrorExpr(expression.At);
        }
        return new TypedBinaryExpr(LangType.I32, expression.Op, left, right, expression.At);
    }

    private TypedExpr CheckCall(
        CallExpr expression,
        LangType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth,
        bool isAwaitOperand = false)
    {
        var reference = expression.Reference;
        var name = reference.Declaration;
        if (!reference.IsQualified && name is "Some" or "Ok" or "Err")
            return CheckBuiltinCall(expression, expected, locals, depth);

        var function = ResolveFunctionReference(reference);
        if (function is not null)
        {
            if (function.Declaration.IsAsync && !isAwaitOperand)
                Add("E_ASYNC_CALL_UNAWAITED", $"Async function '{name}' must be called with 'await'", expression.At);
            else if (!function.Declaration.IsAsync && isAwaitOperand)
                Add("E_AWAIT_SYNC", $"Function '{name}' is synchronous and cannot be awaited", expression.At);

            var isGeneric = function.TypeParameters.Count != 0;
            if (expression.Arguments.Count != function.Parameters.Count)
                Add("E_TYPE_MISMATCH", $"Function '{name}' expects {function.Parameters.Count} arguments, got {expression.Arguments.Count}", expression.At);

            var arguments = new List<TypedExpr>();
            var diagnosticsBeforeArguments = diagnostics.Count;
            var hasCorrectArity = expression.Arguments.Count == function.Parameters.Count;
            for (var i = 0; i < expression.Arguments.Count; i++)
            {
                // Generic calls infer from independently checked argument types. Context-dependent
                // constructors therefore keep their existing expected-type diagnostic here.
                var argumentType = !isGeneric && i < function.Parameters.Count
                    ? function.Parameters[i].Type
                    : null;
                arguments.Add(CheckExpr(expression.Arguments[i], argumentType, locals, depth));
            }

            var inferredTypeArguments = new Dictionary<LangType, LangType>();
            if (isGeneric)
            {
                for (var i = 0; i < Math.Min(arguments.Count, function.Parameters.Count); i++)
                {
                    var formal = function.Parameters[i].Type;
                    var actual = arguments[i].Type;
                    if (TryUnifyType(formal, actual, inferredTypeArguments)) continue;
                    if (!formal.IsError && !actual.IsError)
                        AddMismatch(SubstituteType(formal, inferredTypeArguments), actual, expression.Arguments[i].At);
                }
            }

            var typeArguments = isGeneric
                ? function.TypeParameters.Select(typeParameter =>
                    inferredTypeArguments.TryGetValue(typeParameter, out var inferred) ? inferred : LangType.Error).ToArray()
                : Array.Empty<LangType>();
            var signatureTypesValid = !function.ReturnType.IsError && function.Parameters.All(parameter => !parameter.Type.IsError);
            if (hasCorrectArity && signatureTypesValid && diagnostics.Count == diagnosticsBeforeArguments)
                _currentFunction?.Calls.Add(new FunctionCallSite(function, expression.At));

            var instantiatedReturnType = isGeneric
                ? SubstituteType(function.ReturnType, inferredTypeArguments)
                : function.ReturnType;
            var returnType = instantiatedReturnType;
            if (isGeneric && (!hasCorrectArity || diagnostics.Count != diagnosticsBeforeArguments ||
                              typeArguments.Any(ContainsError) || arguments.Any(argument => ContainsError(argument.Type))))
                returnType = LangType.Error;

            if (isGeneric && typeArguments.All(typeArgument => !ContainsError(typeArgument)))
            {
                foreach (var parameter in function.Parameters)
                    ValidateResourceListType(SubstituteType(parameter.Type, inferredTypeArguments), expression.At);
                ValidateResourceListType(instantiatedReturnType, expression.At);
                for (var i = 0; i < arguments.Count; i++)
                    ValidateResourceListType(arguments[i].Type, expression.Arguments[i].At);
            }

            return new TypedCallExpr(
                returnType,
                function.Id,
                function.Declaration.IsAsync,
                ReadOnly(typeArguments),
                ReadOnly(arguments),
                expression.At);
        }

        foreach (var argument in expression.Arguments)
            _ = CheckExpr(argument, null, locals, depth);
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckMemberCall(
        MemberCallExpr expression,
        LangType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth,
        bool isAwaitOperand = false)
    {
        if (expression.Target is DeclarationRefExpr declarationReference)
        {
            var union = ResolveUnionReference(declarationReference.Reference);
            if (union is null)
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                return new TypedErrorExpr(expression.At);
            }
            return CheckVariantConstruction(
                union,
                expression.Member,
                expression.At,
                expression.MemberAt,
                expression.Arguments,
                locals,
                depth);
        }

        if (expression.Target is NameExpr transactionName &&
            locals.TryGetValue(transactionName.Name, out var transactionLocal) &&
            transactionLocal.Type.IsTransaction)
        {
            return CheckTransactionMemberCall(expression, expected, locals, depth, transactionName, transactionLocal);
        }

        if (expression.Target is NameExpr targetName && !locals.ContainsKey(targetName.Name))
        {
            if (targetName.Name == "html" && IsHtmlBuilderMember(expression.Member))
                return CheckHtmlBuilderIntrinsic(expression, locals, depth);

            if (targetName.Name == "FsError")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                if (IsFsErrorVariant(expression.Member))
                    Add("E_TYPE_MISMATCH", "FsError variants can only be produced by filesystem operations", expression.MemberAt);
                else
                    Add("E_NAME_UNRESOLVED", $"Variant '{expression.Member}' is not declared on FsError", expression.MemberAt);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "DbError")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                if (IsDbErrorVariant(expression.Member))
                    Add("E_TYPE_MISMATCH", "DbError variants can only be produced by SQLite operations", expression.MemberAt);
                else
                    Add("E_NAME_UNRESOLVED", $"Variant '{expression.Member}' is not declared on DbError", expression.MemberAt);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "fs" && expression.Member == "read_text")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                Add("E_CAPABILITY_MISSING", "Intrinsic 'fs.read_text' requires a local or parameter of type 'FsRead'", expression.At);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "fs" && expression.Member == "read_text_async")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                Add("E_CAPABILITY_MISSING", "Intrinsic 'fs.read_text_async' requires a local or parameter of type 'FsRead'", expression.At);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "fs" && expression.Member == "write_text")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                Add("E_CAPABILITY_MISSING", "Intrinsic 'fs.write_text' requires a local or parameter of type 'FsWrite'", expression.At);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "db" && IsDatabaseMember(expression.Member))
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                var requiredType = expression.Member == "query_one" ? "DbRead" : "DbWrite";
                Add("E_CAPABILITY_MISSING", $"Intrinsic 'db.{expression.Member}' requires a local or parameter of type '{requiredType}'", expression.At);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "db" && expression.Member == "begin")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                Add("E_RESOURCE_ESCAPE", "A transaction must be opened directly in a 'with db.begin() as tx' scope", expression.At);
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

        if (receiver.Type.IsDbWrite && expression.Member == "begin")
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_RESOURCE_ESCAPE", "A transaction must be opened directly in a 'with db.begin() as tx' scope", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (receiver.Type.IsList && expression.Member is ("get" or "append"))
        {
            var get = expression.Member == "get";
            var operation = get ? "List.get" : "List.append";
            if (expression.Arguments.Count != 1)
            {
                Add("E_TYPE_MISMATCH", $"Intrinsic '{operation}' expects 1 argument, got {expression.Arguments.Count}", expression.MemberAt);
                for (var i = 0; i < expression.Arguments.Count; i++)
                {
                    var argumentType = i == 0 ? (get ? LangType.I32 : receiver.Type.Arguments[0]) : null;
                    _ = CheckExpr(expression.Arguments[i], argumentType, locals, depth);
                }
                return new TypedErrorExpr(expression.At);
            }

            if (get)
            {
                var index = CheckExpr(expression.Arguments[0], LangType.I32, locals, depth);
                return new TypedListGetExpr(
                    LangType.Option(receiver.Type.Arguments[0]),
                    receiver,
                    index,
                    expression.At);
            }

            var value = CheckExpr(expression.Arguments[0], receiver.Type.Arguments[0], locals, depth);
            return new TypedListAppendExpr(receiver.Type, receiver, value, expression.At);
        }

        if (receiver.Type.IsFsRead && expression.Member == "read_text")
        {
            var diagnosticsBeforeCall = diagnostics.Count;
            var hasCorrectArity = expression.Arguments.Count == 1;
            if (!hasCorrectArity)
                Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.read_text' expects 1 argument, got {expression.Arguments.Count}", expression.MemberAt);

            var arguments = new List<TypedExpr> { receiver };
            var hasSupportedPathType = false;
            if (expression.Arguments.Count > 0)
            {
                var path = CheckExpr(expression.Arguments[0], null, locals, depth);
                hasSupportedPathType = path.Type.IsText || path.Type.IsFilePath;
                if (!path.Type.IsError && !hasSupportedPathType)
                    Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.read_text' expects a Text or FilePath argument, found '{path.Type.DisplayName}'", expression.Arguments[0].At);
                arguments.Add(path);
            }
            else
                arguments.Add(new TypedErrorExpr(expression.MemberAt));
            for (var i = 1; i < expression.Arguments.Count; i++)
                _ = CheckExpr(expression.Arguments[i], null, locals, depth);

            var resultType = LangType.Result(LangType.Text, LangType.FsError);
            if (hasCorrectArity && hasSupportedPathType && diagnostics.Count == diagnosticsBeforeCall)
                _currentFunction?.DirectEffects.Add(new DirectEffectCall("fs.read", "fs.read_text", expression.MemberAt));
            return new TypedIntrinsicCallExpr(
                resultType,
                BuiltinIntrinsic.FsReadText,
                ReadOnly(arguments),
                expression.At);
        }

        if (receiver.Type.IsFsRead && expression.Member == "read_text_async")
        {
            var diagnosticsBeforeCall = diagnostics.Count;
            var hasCorrectArity = expression.Arguments.Count == 1;
            if (!hasCorrectArity)
                Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.read_text_async' expects 1 argument, got {expression.Arguments.Count}", expression.MemberAt);

            var arguments = new List<TypedExpr> { receiver };
            var hasSupportedPathType = false;
            if (expression.Arguments.Count > 0)
            {
                var path = CheckExpr(expression.Arguments[0], null, locals, depth);
                hasSupportedPathType = path.Type.IsText || path.Type.IsFilePath;
                if (!path.Type.IsError && !hasSupportedPathType)
                    Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.read_text_async' expects a Text or FilePath argument, found '{path.Type.DisplayName}'", expression.Arguments[0].At);
                arguments.Add(path);
            }
            else
                arguments.Add(new TypedErrorExpr(expression.MemberAt));
            for (var i = 1; i < expression.Arguments.Count; i++)
                _ = CheckExpr(expression.Arguments[i], null, locals, depth);

            var resultType = LangType.Result(LangType.Text, LangType.FsError);
            var callIsValid = hasCorrectArity && hasSupportedPathType && diagnostics.Count == diagnosticsBeforeCall;
            if (!isAwaitOperand)
                Add("E_ASYNC_CALL_UNAWAITED", "Intrinsic 'fs.read_text_async' must be called with 'await'", expression.At);
            if (callIsValid)
                _currentFunction?.DirectEffects.Add(new DirectEffectCall("fs.read", "fs.read_text_async", expression.MemberAt));
            return new TypedIntrinsicCallExpr(
                resultType,
                BuiltinIntrinsic.FsReadTextAsync,
                ReadOnly(arguments),
                expression.At);
        }

        if (receiver.Type.IsFsWrite && expression.Member == "write_text")
        {
            var diagnosticsBeforeCall = diagnostics.Count;
            var hasCorrectArity = expression.Arguments.Count == 2;
            if (!hasCorrectArity)
                Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.write_text' expects 2 arguments, got {expression.Arguments.Count}", expression.MemberAt);

            var arguments = new List<TypedExpr> { receiver };
            var hasSupportedPathType = false;
            if (expression.Arguments.Count > 0)
            {
                var path = CheckExpr(expression.Arguments[0], null, locals, depth);
                hasSupportedPathType = path.Type.IsText || path.Type.IsFilePath;
                if (!path.Type.IsError && !hasSupportedPathType)
                    Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.write_text' expects a Text or FilePath path, found '{path.Type.DisplayName}'", expression.Arguments[0].At);
                arguments.Add(path);
            }
            else
                arguments.Add(new TypedErrorExpr(expression.MemberAt));

            var hasTextValue = false;
            if (expression.Arguments.Count > 1)
            {
                var value = CheckExpr(expression.Arguments[1], null, locals, depth);
                hasTextValue = value.Type.IsText;
                if (!value.Type.IsError && !hasTextValue)
                    Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.write_text' expects a Text value, found '{value.Type.DisplayName}'", expression.Arguments[1].At);
                arguments.Add(value);
            }
            else
                arguments.Add(new TypedErrorExpr(expression.MemberAt));

            for (var i = 2; i < expression.Arguments.Count; i++)
                _ = CheckExpr(expression.Arguments[i], null, locals, depth);

            var resultType = LangType.Result(LangType.Bool, LangType.FsError);
            if (hasCorrectArity && hasSupportedPathType && hasTextValue && diagnostics.Count == diagnosticsBeforeCall)
                _currentFunction?.DirectEffects.Add(new DirectEffectCall("fs.write", "fs.write_text", expression.MemberAt));
            return new TypedIntrinsicCallExpr(
                resultType,
                BuiltinIntrinsic.FsWriteText,
                ReadOnly(arguments),
                expression.At);
        }

        if ((receiver.Type.IsDbRead && expression.Member == "query_one") ||
            (receiver.Type.IsDbWrite && expression.Member == "execute"))
        {
            return CheckDatabaseCall(expression, expected, locals, depth, receiver);
        }

        if (IsDatabaseMember(expression.Member))
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            var capabilityTargetDescription = expression.Target is NameExpr localName && locals.ContainsKey(localName.Name)
                ? $"Local '{localName.Name}' of type '{receiver.Type.DisplayName}'"
                : $"Value of type '{receiver.Type.DisplayName}'";
            var requiredType = expression.Member == "query_one" ? "DbRead" : "DbWrite";
            Add("E_CAPABILITY_MISSING", $"{capabilityTargetDescription} cannot provide database capability member '{expression.Member}' (requires '{requiredType}')", expression.MemberAt);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Member is "read_text" or "read_text_async")
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);

            var message = expression.Target is NameExpr localName && locals.ContainsKey(localName.Name)
                ? $"Local '{localName.Name}' of type '{receiver.Type.DisplayName}' cannot provide capability member '{expression.Member}'"
                : $"Value of type '{receiver.Type.DisplayName}' cannot provide capability member '{expression.Member}'";
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

        if (receiver.Type.IsText && expression.Member == "split")
        {
            if (expression.Arguments.Count != 1)
            {
                Add("E_TYPE_MISMATCH", $"Intrinsic 'Text.split' expects 1 argument, got {expression.Arguments.Count}", expression.MemberAt);
                for (var i = 0; i < expression.Arguments.Count; i++)
                    _ = CheckExpr(expression.Arguments[i], i == 0 ? LangType.Text : null, locals, depth);
                return new TypedErrorExpr(expression.At);
            }

            var separator = CheckExpr(expression.Arguments[0], LangType.Text, locals, depth);
            return new TypedIntrinsicCallExpr(
                LangType.List(LangType.Text),
                BuiltinIntrinsic.TextSplit,
                ReadOnly([receiver, separator]),
                expression.At);
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

    private TypedExpr CheckTransactionMemberCall(
        MemberCallExpr expression,
        LangType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth,
        NameExpr transactionName,
        LocalSymbol transactionLocal)
    {
        if (!_activeTransactionLocals.Contains(transactionLocal.Id) ||
            expression.Member is not ("execute" or "commit"))
        {
            Add(
                "E_RESOURCE_ESCAPE",
                $"Transaction '{transactionName.Name}' may only be used as the direct receiver of execute() or commit() inside its with scope",
                expression.MemberAt);
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Member == "execute")
        {
            var receiver = new TypedLocalExpr(LangType.Transaction, transactionLocal.Id, transactionName.At);
            return CheckDatabaseCall(expression, expected, locals, depth, receiver, transactionExecute: true);
        }

        if (expression.Arguments.Count != 0)
            Add("E_TYPE_MISMATCH", $"Intrinsic 'Transaction.commit' expects 0 arguments, got {expression.Arguments.Count}", expression.MemberAt);
        foreach (var argument in expression.Arguments)
            _ = CheckExpr(argument, null, locals, depth);
        return new TypedTransactionCommitExpr(transactionLocal.Id, expression.At);
    }

    private TypedExpr CheckDatabaseCall(
        MemberCallExpr expression,
        LangType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth,
        TypedExpr receiver,
        bool transactionExecute = false)
    {
        var diagnosticsBeforeCall = diagnostics.Count;
        var queryOne = expression.Member == "query_one";
        var operationName = queryOne ? "DbRead.query_one" : transactionExecute ? "Transaction.execute" : "DbWrite.execute";
        var effect = queryOne ? "db.read" : "db.write";
        var hasCorrectArity = expression.Arguments.Count == 2;
        if (!hasCorrectArity)
            Add("E_TYPE_MISMATCH", $"Intrinsic '{operationName}' expects 2 arguments, got {expression.Arguments.Count}", expression.MemberAt);

        var sql = string.Empty;
        var hasLiteralSql = false;
        TypedExpr? typedParameters = null;
        if (expression.Arguments.Count > 0)
        {
            _ = CheckExpr(expression.Arguments[0], null, locals, depth);
            if (expression.Arguments[0] is TextExpr sqlLiteral)
            {
                sql = sqlLiteral.Value;
                hasLiteralSql = true;
            }
            else
            {
                Add(
                    "E_DB_SQL_LITERAL",
                    $"Intrinsic '{operationName}' requires a SQL string literal; dynamic SQL is not supported",
                    expression.Arguments[0].At);
            }

            if (queryOne && hasLiteralSql && !IsSingleSqliteReadSelect(sql))
            {
                Add(
                    "E_DB_READ_STATEMENT",
                    "DbRead.query_one requires a single SELECT statement beginning with a standalone SELECT token and containing no semicolon",
                    expression.Arguments[0].At);
            }

            if (transactionExecute && hasLiteralSql && !IsSingleSqliteTransactionWrite(sql))
            {
                Add(
                    "E_DB_TRANSACTION_STATEMENT",
                    "Transaction.execute requires one INSERT, UPDATE, DELETE, or REPLACE statement with no semicolon; transaction-control and other SQL statements are not supported",
                    expression.Arguments[0].At);
            }
        }

        StructSymbol? parameterStruct = null;
        if (expression.Arguments.Count > 1)
        {
            typedParameters = CheckExpr(expression.Arguments[1], null, locals, depth);
            parameterStruct = CheckDatabaseStruct(typedParameters.Type, "parameter", expression.Arguments[1].At);
        }
        else
        {
            Add("E_DB_PARAMETERS", $"Intrinsic '{operationName}' requires a parameter struct", expression.MemberAt);
        }

        for (var i = 2; i < expression.Arguments.Count; i++)
            _ = CheckExpr(expression.Arguments[i], null, locals, depth);

        var rowStruct = (StructSymbol?)null;
        LangType resultType;
        var validExpectedResult = true;
        if (queryOne)
        {
            validExpectedResult = false;
            if (expected is null)
            {
                Add(
                    "E_DB_RESULT_TYPE",
                    "DbRead.query_one requires an expected type of Result<Option<fully-qualified RowStruct>, DbError>",
                    expression.At);
            }
            else if (!expected.IsError)
            {
                if (expected.Kind == LangTypeKind.Result &&
                    expected.Arguments[0].Kind == LangTypeKind.Option &&
                    expected.Arguments[1].IsDbError)
                {
                    var rowType = expected.Arguments[0].Arguments[0];
                    rowStruct = CheckDatabaseStruct(rowType, "row", expression.At);
                    validExpectedResult = rowStruct is not null;
                }
                else
                {
                    Add(
                        "E_DB_RESULT_TYPE",
                        "DbRead.query_one requires an expected type of Result<Option<fully-qualified RowStruct>, DbError>",
                        expression.At);
                }
            }

            resultType = validExpectedResult ? expected! : LangType.Error;
        }
        else
        {
            resultType = LangType.Result(LangType.I32, LangType.DbError);
        }

        if (hasCorrectArity && hasLiteralSql && parameterStruct is not null &&
            validExpectedResult && diagnostics.Count == diagnosticsBeforeCall)
        {
            _currentFunction?.DirectEffects.Add(new DirectEffectCall(effect, operationName, expression.MemberAt));
        }

        var operation = new CheckedDatabaseOperation(
            queryOne
                ? CheckedDatabaseOperationKind.QueryOne
                : transactionExecute
                    ? CheckedDatabaseOperationKind.TransactionExecute
                    : CheckedDatabaseOperationKind.Execute,
            effect,
            sql,
            parameterStruct?.Id ?? -1,
            rowStruct?.Id);
        return new TypedDatabaseCallExpr(
            resultType,
            receiver,
            typedParameters ?? new TypedErrorExpr(expression.MemberAt),
            operation,
            expression.At);
    }

    private StructSymbol? CheckDatabaseStruct(LangType type, string role, Token at)
    {
        if (type.IsError) return null;
        if (type.Kind != LangTypeKind.Struct)
        {
            Add(
                "E_DB_CODEC_UNSUPPORTED",
                $"SQLite {role} value must be a concrete declared struct with only i32, bool, Text, or Option scalar fields; found '{type.DisplayName}'",
                at);
            return null;
        }

        var structure = _structs[type.StructId];
        if (!IsSourceDeclaredStruct(structure))
        {
            Add("E_DB_CODEC_UNSUPPORTED", $"SQLite {role} value must use a source-declared struct; '{structure.Declaration.Name}' is generated", at);
            return null;
        }

        var valid = true;
        foreach (var field in structure.Fields)
        {
            if (field.Type.IsError)
            {
                valid = false;
                continue;
            }
            if (IsDatabaseScalarType(field.Type)) continue;
            Add(
                "E_DB_CODEC_UNSUPPORTED",
                $"SQLite {role} struct '{structure.Declaration.Name}' field '{field.Name}' has unsupported type '{field.Type.DisplayName}'; use i32, bool, Text, or Option of one of those scalar types",
                field.At);
            valid = false;
        }
        return valid ? structure : null;
    }

    private static bool IsDatabaseScalarType(LangType type) =>
        type.IsI32 || type.IsBool || type.IsText ||
        type.Kind == LangTypeKind.Option &&
        (type.Arguments[0].IsI32 || type.Arguments[0].IsBool || type.Arguments[0].IsText);

    private static bool IsDatabaseMember(string member) => member is "query_one" or "execute";

    private static bool IsSingleSqliteReadSelect(string sql)
    {
        const int SelectLength = 6;
        var statement = sql.TrimStart();
        return !sql.Contains(';') &&
            statement.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            (statement.Length == SelectLength || char.IsWhiteSpace(statement[SelectLength]));
    }

    private static bool IsSingleSqliteTransactionWrite(string sql)
    {
        if (sql.Contains(';')) return false;
        var firstKeyword = FirstSqliteKeyword(sql);
        return firstKeyword is "INSERT" or "UPDATE" or "DELETE" or "REPLACE";
    }

    private static string? FirstSqliteKeyword(string sql)
    {
        var index = 0;
        while (index < sql.Length)
        {
            while (index < sql.Length && char.IsWhiteSpace(sql[index]))
                index++;

            if (index + 1 < sql.Length && sql[index] == '-' && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not ('\r' or '\n'))
                    index++;
                continue;
            }

            if (index + 1 < sql.Length && sql[index] == '/' && sql[index + 1] == '*')
            {
                var commentEnd = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (commentEnd < 0) return null;
                index = commentEnd + 2;
                continue;
            }

            break;
        }

        if (index >= sql.Length || !(char.IsLetter(sql[index]) || sql[index] == '_'))
            return null;

        var start = index++;
        while (index < sql.Length &&
               (char.IsLetterOrDigit(sql[index]) || sql[index] is '_' or '$' || sql[index] >= '\u0080'))
        {
            index++;
        }

        return sql[start..index].ToUpperInvariant();
    }

    private static bool IsHtmlBuilderMember(string member) =>
        member is "text" or "heading" or "paragraph" or "concat" or "document";

    private TypedExpr CheckHtmlBuilderIntrinsic(
        MemberCallExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var (intrinsic, parameterTypes) = expression.Member switch
        {
            "text" => (BuiltinIntrinsic.HtmlText, new[] { LangType.Text }),
            "heading" => (BuiltinIntrinsic.HtmlHeading, new[] { LangType.Text }),
            "paragraph" => (BuiltinIntrinsic.HtmlParagraph, new[] { LangType.Text }),
            "concat" => (BuiltinIntrinsic.HtmlConcat, new[] { LangType.Html, LangType.Html }),
            "document" => (BuiltinIntrinsic.HtmlDocument, new[] { LangType.Text, LangType.Html }),
            _ => throw new InvalidOperationException("Unknown Html builder intrinsic")
        };

        var hasCorrectArity = expression.Arguments.Count == parameterTypes.Length;
        if (!hasCorrectArity)
        {
            var argumentWord = parameterTypes.Length == 1 ? "argument" : "arguments";
            Add("E_TYPE_MISMATCH", $"Intrinsic 'html.{expression.Member}' expects {parameterTypes.Length} {argumentWord}, got {expression.Arguments.Count}", expression.MemberAt);
        }

        var arguments = new List<TypedExpr>(expression.Arguments.Count);
        for (var i = 0; i < expression.Arguments.Count; i++)
        {
            var argument = CheckExpr(expression.Arguments[i], null, locals, depth);
            arguments.Add(argument);
            if (i < parameterTypes.Length && !argument.Type.IsError && argument.Type != parameterTypes[i])
                AddMismatch(parameterTypes[i], argument.Type, expression.Arguments[i].At);
        }

        return new TypedIntrinsicCallExpr(
            LangType.Html,
            intrinsic,
            ReadOnly(arguments),
            expression.At);
    }

    private TypedExpr CheckBuiltinCall(
        CallExpr expression,
        LangType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var constructorName = expression.Reference.Declaration;
        var wantsOption = constructorName == "Some";
        var wantsOk = constructorName == "Ok";
        var expectedKind = wantsOption ? LangTypeKind.Option : LangTypeKind.Result;
        var expectedName = wantsOption ? "Option<T>" : "Result<T, E>";
        var matchingContext = expected is not null && (expected.IsError || expected.Kind == expectedKind);

        if (expression.Arguments.Count != 1)
            Add("E_TYPE_MISMATCH", $"Constructor '{constructorName}' expects 1 argument, got {expression.Arguments.Count}", expression.At);

        if (expected is null)
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_TYPE_MISMATCH", $"Constructor '{constructorName}' requires an expected type of {expectedName}", expression.At);
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
        UnionSymbol union,
        string member,
        Token at,
        Token memberAt,
        IReadOnlyList<Expr> arguments,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var variant = union.Variants.FirstOrDefault(item => item.Name == member);
        if (variant is null)
        {
            foreach (var argument in arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_NAME_UNRESOLVED", $"Variant '{member}' is not declared on union '{union.Declaration.Name}'", memberAt);
            return new TypedErrorExpr(at);
        }

        if (arguments.Count != variant.Fields.Count)
            Add("E_TYPE_MISMATCH", $"Variant '{union.Declaration.Name}.{variant.Name}' expects {variant.Fields.Count} payload values, got {arguments.Count}", memberAt);

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
        if (type.IsDbError)
        {
            return ReadOnly<VariantShape>([
                new("DbError.Statement", "dberror:Statement", null, 0, BuiltinVariant.DbErrorStatement, []),
                new("DbError.RowShape", "dberror:RowShape", null, 1, BuiltinVariant.DbErrorRowShape, [])
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
            if (pattern.Union is null)
            {
                Add("E_NAME_UNRESOLVED", $"Union '{union.Declaration.Name}' must be referenced with a qualified module path", pattern.At);
                return null;
            }

            var visibleUnion = ResolveUnionReference(pattern.Union);
            if (visibleUnion is null) return null;

            if (visibleUnion.Id != union.Id)
            {
                Add("E_TYPE_MISMATCH", $"Pattern union '{FormatReference(pattern.Union)}' resolves to module '{FormatModuleIdentity(visibleUnion.ModuleIdentity)}', but the matched value uses module '{FormatModuleIdentity(union.ModuleIdentity)}'", pattern.At);
                return null;
            }
        }
        else if (scrutineeType.IsFsError || scrutineeType.IsDbError)
        {
            var builtinErrorName = scrutineeType.IsFsError ? "FsError" : "DbError";
            if (pattern.Union is null || pattern.Union.IsQualified || pattern.Union.Declaration != builtinErrorName)
            {
                Add("E_TYPE_MISMATCH", $"Expected pattern from '{builtinErrorName}.<variant>', found '{pattern.VariantName}'", pattern.At);
                return null;
            }
        }
        else if (pattern.Union is not null)
        {
            Add("E_TYPE_MISMATCH", $"Qualified pattern '{FormatReference(pattern.Union)}.{pattern.VariantName}' does not match '{scrutineeType.DisplayName}'", pattern.At);
            return null;
        }

        var shapeName = scrutineeType.Kind == LangTypeKind.Union || scrutineeType.IsFsError || scrutineeType.IsDbError
            ? $"{pattern.Union!.Declaration}.{pattern.VariantName}"
            : pattern.VariantName;
        var shape = shapes.FirstOrDefault(item => item.Name == shapeName);
        if (shape is null)
        {
            Add("E_NAME_UNRESOLVED", $"Variant '{pattern.VariantName}' is not valid for '{scrutineeType.DisplayName}'", pattern.At);
            return null;
        }
        return shape;
    }

    private static bool ContainsType(LangType type, LangType sought)
    {
        if (type == sought) return true;
        return type.Arguments.Any(argument => ContainsType(argument, sought));
    }

    private bool ContainsResourceHandle(LangType type, bool[] resourceReachableDeclarations)
    {
        var pending = new Stack<LangType>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (IsResourceHandle(current))
                return true;

            if (TryGetDeclarationNode(current, out var declaration))
            {
                if (resourceReachableDeclarations[declaration])
                    return true;
                continue;
            }

            foreach (var argument in current.Arguments)
                pending.Push(argument);
        }

        return false;
    }

    private static bool IsResourceHandle(LangType type) =>
        type.Kind is LangTypeKind.FsRead or LangTypeKind.FsWrite or LangTypeKind.DbRead or LangTypeKind.DbWrite or LangTypeKind.Transaction;

    private bool TryGetDeclarationNode(LangType type, out int declaration)
    {
        if (type.Kind == LangTypeKind.Struct && type.StructId >= 0 && type.StructId < _structs.Count)
        {
            declaration = type.StructId;
            return true;
        }
        if (type.Kind == LangTypeKind.Union && type.UnionId >= 0 && type.UnionId < _unions.Count)
        {
            declaration = _structs.Count + type.UnionId;
            return true;
        }

        declaration = -1;
        return false;
    }

    private bool ContainsResourceHandle(LangType type) =>
        ContainsResourceHandle(type, _resourceReachableDeclarations);

    private static bool ContainsError(LangType type) =>
        type.IsError || type.Arguments.Any(ContainsError);

    private static bool TryUnifyType(
        LangType formal,
        LangType actual,
        Dictionary<LangType, LangType> bindings)
    {
        if (formal.IsError || actual.IsError) return true;

        if (formal.Kind == LangTypeKind.TypeParameter)
        {
            if (!bindings.TryGetValue(formal, out var previous))
            {
                bindings.Add(formal, actual);
                return true;
            }
            return previous == actual;
        }

        if (formal.Kind is LangTypeKind.Option or LangTypeKind.List or LangTypeKind.Result)
        {
            if (formal.Kind != actual.Kind || formal.Arguments.Count != actual.Arguments.Count)
                return false;
            for (var i = 0; i < formal.Arguments.Count; i++)
                if (!TryUnifyType(formal.Arguments[i], actual.Arguments[i], bindings)) return false;
            return true;
        }

        return formal == actual;
    }

    private static LangType SubstituteType(LangType type, IReadOnlyDictionary<LangType, LangType> bindings)
    {
        if (type.Kind == LangTypeKind.TypeParameter)
            return bindings.TryGetValue(type, out var inferred) ? inferred : LangType.Error;
        if (type.Kind == LangTypeKind.Option)
            return LangType.Option(SubstituteType(type.Arguments[0], bindings));
        if (type.Kind == LangTypeKind.List)
            return LangType.List(SubstituteType(type.Arguments[0], bindings));
        if (type.Kind == LangTypeKind.Result)
            return LangType.Result(
                SubstituteType(type.Arguments[0], bindings),
                SubstituteType(type.Arguments[1], bindings));
        return type;
    }

    private LangType ResolveType(TypeSyntax syntax, int depth) => ResolveType(syntax, depth, null);

    private LangType ResolveType(
        TypeSyntax syntax,
        int depth,
        IReadOnlyDictionary<string, LangType>? typeParameters)
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

        var reference = syntax.Reference;
        var name = reference.Declaration;
        if (!reference.IsQualified && typeParameters is not null && typeParameters.TryGetValue(name, out var typeParameter))
            return NoTypeArguments(syntax, typeParameter);

        if (!reference.IsQualified)
        {
            switch (name)
            {
            case "i32":
                return NoTypeArguments(syntax, LangType.I32);
            case "bool":
                return NoTypeArguments(syntax, LangType.Bool);
            case "Text":
                return NoTypeArguments(syntax, LangType.Text);
            case "Html":
                return NoTypeArguments(syntax, LangType.Html);
            case "FilePath":
                return NoTypeArguments(syntax, LangType.FilePath);
            case "FsRead":
                return NoTypeArguments(syntax, LangType.FsRead);
            case "FsWrite":
                return NoTypeArguments(syntax, LangType.FsWrite);
            case "FsError":
                return NoTypeArguments(syntax, LangType.FsError);
            case "DbRead":
                return NoTypeArguments(syntax, LangType.DbRead);
            case "DbWrite":
                return NoTypeArguments(syntax, LangType.DbWrite);
            case "Transaction":
                Add(
                    "E_RESOURCE_ESCAPE",
                    "Transaction handles cannot appear in source type positions; use them only as direct receivers inside their with scope",
                    syntax.At);
                return LangType.Error;
            case "DbError":
                return NoTypeArguments(syntax, LangType.DbError);
            case "Option":
                if (syntax.Args.Count != 1)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'Option' expects 1 type argument, got {syntax.Args.Count}", syntax.At);
                    return LangType.Error;
                }
                return LangType.Option(ResolveType(syntax.Args[0], depth + 1, typeParameters));
            case "List":
                if (syntax.Args.Count != 1)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'List' expects 1 type argument, got {syntax.Args.Count}", syntax.At);
                    return LangType.Error;
                }
                var listItemType = ResolveType(syntax.Args[0], depth + 1, typeParameters);
                return LangType.List(listItemType);
            case "Result":
                if (syntax.Args.Count != 2)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'Result' expects 2 type arguments, got {syntax.Args.Count}", syntax.At);
                    return LangType.Error;
                }
                return LangType.Result(
                    ResolveType(syntax.Args[0], depth + 1, typeParameters),
                    ResolveType(syntax.Args[1], depth + 1, typeParameters));
            }
        }

        if (!reference.IsQualified)
        {
            var detail = CurrentModule.TypeNames.Contains(name)
                ? $"Type '{name}' must be referenced with a qualified module path"
                : $"Type '{name}' is not declared (user declarations must be module-qualified)";
            Add("E_NAME_UNRESOLVED", detail, reference.At);
            return LangType.Error;
        }

        var (union, structure) = ResolveTypeDeclaration(reference);
        if (union is not null)
        {
            if (syntax.Args.Count != 0)
            {
                Add("E_TYPE_MISMATCH", $"Union type '{name}' does not take type arguments", syntax.At);
                return LangType.Error;
            }
            return union.Type;
        }
        if (structure is not null)
        {
            if (syntax.Args.Count != 0)
            {
                Add("E_TYPE_MISMATCH", $"Struct type '{name}' does not take type arguments", syntax.At);
                return LangType.Error;
            }
            return structure.Type;
        }
        return LangType.Error;
    }

    private LangType NoTypeArguments(TypeSyntax syntax, LangType type)
    {
        if (syntax.Args.Count != 0)
        {
            Add("E_TYPE_MISMATCH", $"Type '{syntax.Reference.Declaration}' does not take type arguments", syntax.At);
            return LangType.Error;
        }
        return type;
    }

    private static bool IsFsErrorVariant(string name) =>
        name is "NotFound" or "PermissionDenied" or "InvalidPath" or "InvalidText" or "Io";

    private static bool IsDbErrorVariant(string name) =>
        name is "Statement" or "RowShape";


    private void AddMismatch(LangType expected, LangType actual, Token at) =>
        Add("E_TYPE_MISMATCH", $"Expected '{expected.DisplayName}', found '{actual.DisplayName}'", at);


    private void AddMismatch(LangType expected, string actual, Token at) =>
        Add("E_TYPE_MISMATCH", $"Expected '{expected.DisplayName}', found '{actual}'", at);
    private void Add(string code, string message, Token at) =>
        diagnostics.Add(new Diagnostic(code, message, at.File, at.Range));

    private ModuleSymbols CurrentModule => _modulesByIdentity[_currentModule];

    private TypedExpr UnsupportedExpr(Expr expression) { Add("E_UNSUPPORTED", "Expression is not implemented in this language slice", expression.At); return new TypedErrorExpr(expression.At); }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());

    private sealed class ModuleSymbols(PackageModuleInput input)
    {
        public string PackageId { get; } = input.PackageId;
        public ParsedProgram Program { get; } = input.Program;
        public ModuleIdentity Identity { get; } = new(input.PackageId, input.Program.Module);
        public IReadOnlyDictionary<string, string> DirectDependencies { get; } = input.DirectDependencies
            .ToDictionary(dependency => dependency.Key, dependency => dependency.Value, StringComparer.Ordinal);
        public HashSet<string> TypeNames { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, UnionSymbol> DeclaredUnions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, StructSymbol> DeclaredStructs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, FunctionSymbol> DeclaredFunctions { get; } = new(StringComparer.Ordinal);
        public CommandSymbol? Command { get; set; }
    }

    private sealed class UnionSymbol(int id, ModuleIdentity moduleIdentity, UnionDecl declaration, LangType type)
    {
        public int Id { get; } = id;
        public ModuleIdentity ModuleIdentity { get; } = moduleIdentity;
        public string Module => ModuleIdentity.ModuleName;
        public UnionDecl Declaration { get; } = declaration;
        public LangType Type { get; } = type;
        public List<CheckedVariant> Variants { get; } = [];
    }

    private sealed class StructSymbol(int id, ModuleIdentity moduleIdentity, StructDecl declaration, LangType type)
    {
        public int Id { get; } = id;
        public ModuleIdentity ModuleIdentity { get; } = moduleIdentity;
        public string Module => ModuleIdentity.ModuleName;
        public StructDecl Declaration { get; } = declaration;
        public LangType Type { get; } = type;
        public List<CheckedStructField> Fields { get; } = [];
    }

    private sealed class CommandSymbol(
        int id,
        string packageId,
        ModuleIdentity moduleIdentity,
        CommandDecl declaration,
        string help,
        StructSymbol argsStruct,
        IReadOnlyList<CheckedCommandInput> inputs,
        CommandHandlerSyntax? handlerSyntax,
        CommandErrorSyntax? errorSyntax)
    {
        public int Id { get; } = id;
        public string PackageId { get; } = packageId;
        public ModuleIdentity ModuleIdentity { get; } = moduleIdentity;
        public string ModuleName => ModuleIdentity.ModuleName;
        public CommandDecl Declaration { get; } = declaration;
        public string Help { get; } = help;
        public StructSymbol ArgsStruct { get; } = argsStruct;
        public IReadOnlyList<CheckedCommandInput> Inputs { get; } = Array.AsReadOnly(inputs.ToArray());
        public CommandHandlerSyntax? HandlerSyntax { get; } = handlerSyntax;
        public CommandErrorSyntax? ErrorSyntax { get; } = errorSyntax;
        public FunctionSymbol? HandlerFunction { get; set; }
        public FunctionSymbol? ErrorFormatter { get; set; }
        public LangType ErrorType { get; set; } = LangType.Error;
        public IReadOnlyList<CheckedCapabilityParameter> Capabilities { get; set; } = [];

        public CheckedCommand ToCheckedCommand() => new(
            Id,
            PackageId,
            ModuleName,
            Declaration.Name,
            Help,
            ArgsStruct.Id,
            ArgsStruct.Type,
            Inputs,
            HandlerFunction!.Id,
            SemanticChecker.FormatReference(HandlerSyntax!.Reference),
            HandlerFunction.Declaration.IsAsync,
            ErrorFormatter!.Id,
            SemanticChecker.FormatReference(ErrorSyntax!.Reference),
            ErrorType,
            Array.AsReadOnly(Capabilities.ToArray()),
            Declaration.At);
    }

    private sealed class FunctionSymbol(
        int id,
        ModuleIdentity moduleIdentity,
        FunctionDecl declaration,
        string? testName = null)
    {
        public int Id { get; } = id;
        public ModuleIdentity ModuleIdentity { get; } = moduleIdentity;
        public string PackageId => ModuleIdentity.PackageId;
        public string ModuleName => ModuleIdentity.ModuleName;
        public string Module => ModuleName;
        public FunctionDecl Declaration { get; } = declaration;
        public string? TestName { get; } = testName;
        public IReadOnlyList<CheckedParameter> Parameters { get; set; } = [];
        public IReadOnlyList<LangType> TypeParameters { get; set; } = [];
        public IReadOnlyDictionary<string, LangType> TypeParametersByName { get; set; } = new Dictionary<string, LangType>(StringComparer.Ordinal);
        public LangType ReturnType { get; set; } = LangType.Error;
        public IReadOnlyList<string> DeclaredEffects { get; set; } = [];
        public List<FunctionCallSite> Calls { get; } = [];
        public List<DirectEffectCall> DirectEffects { get; } = [];
        public CheckedFunction? CheckedFunction { get; set; }
    }

    private sealed record FunctionCallSite(FunctionSymbol Target, Token At);
    private sealed record DirectEffectCall(string Effect, string IntrinsicName, Token At);

    private sealed record LocalSymbol(int Id, LangType Type, bool IsMutable = false);

    private int _nextLocalId;
}

internal sealed record TypedErrorExpr(Token At) : TypedExpr(LangType.Error, At);
