using System.Collections.ObjectModel;

internal enum HobTypeKind
{
    Error,
    I32,
    I64,
    U32,
    U64,
    F64,
    ArithmeticError,
    Unit,
    Bool,
    Text,
    Bytes,
    BytesError,
    Html,
    FilePath,
    TypeParameter,
    Union,
    Struct,
    Newtype,
    Option,
    List,
    Map,
    Result,
    FsRead,
    FsWrite,
    Config,
    Secrets,
    Logger,
    ProcessRunner,
    SecretText,
    FsError,
    ProcessOutput,
    ProcessError,
    HttpClient,
    HttpResponse,
    HttpError,
    DbRead,
    DbWrite,
    Transaction,
    DbError
}

internal enum TypeParameterOwnerKind
{
    Function,
    Struct,
    Union,
    Trait
}

internal sealed class HobType : IEquatable<HobType>
{
    private readonly ReadOnlyCollection<HobType> _arguments;

    private HobType(
        HobTypeKind kind,
        string displayName,
        string? nominalName = null,
        TypeParameterOwnerKind typeParameterOwnerKind = TypeParameterOwnerKind.Function,
        int typeParameterOwnerId = -1,
        int typeParameterOrdinal = -1,
        int unionId = -1,
        int structId = -1,
        int newtypeId = -1,
        IEnumerable<HobType>? arguments = null)
    {
        Kind = kind;
        DisplayName = displayName;
        NominalName = nominalName ?? displayName;
        TypeParameterOwnerKind = typeParameterOwnerKind;
        TypeParameterOwnerId = typeParameterOwnerId;
        TypeParameterOrdinal = typeParameterOrdinal;
        UnionId = unionId;
        StructId = structId;
        NewtypeId = newtypeId;
        _arguments = Array.AsReadOnly((arguments ?? []).ToArray());
    }

    public HobTypeKind Kind { get; }
    public string DisplayName { get; }
    internal string NominalName { get; }
    public IReadOnlyList<HobType> Arguments => _arguments;
    public bool IsI32 => Kind == HobTypeKind.I32;
    public bool IsI64 => Kind == HobTypeKind.I64;
    public bool IsU32 => Kind == HobTypeKind.U32;
    public bool IsU64 => Kind == HobTypeKind.U64;
    public bool IsF64 => Kind == HobTypeKind.F64;
    public bool IsArithmeticError => Kind == HobTypeKind.ArithmeticError;
    public bool IsUnit => Kind == HobTypeKind.Unit;
    public bool IsBool => Kind == HobTypeKind.Bool;
    public bool IsText => Kind == HobTypeKind.Text;
    public bool IsBytes => Kind == HobTypeKind.Bytes;
    public bool IsBytesError => Kind == HobTypeKind.BytesError;
    public bool IsHtml => Kind == HobTypeKind.Html;
    public bool IsFilePath => Kind == HobTypeKind.FilePath;
    public bool IsFsRead => Kind == HobTypeKind.FsRead;
    public bool IsFsWrite => Kind == HobTypeKind.FsWrite;
    public bool IsConfig => Kind == HobTypeKind.Config;
    public bool IsSecrets => Kind == HobTypeKind.Secrets;
    public bool IsLogger => Kind == HobTypeKind.Logger;
    public bool IsProcessRunner => Kind == HobTypeKind.ProcessRunner;
    public bool IsProcessOutput => Kind == HobTypeKind.ProcessOutput;
    public bool IsProcessError => Kind == HobTypeKind.ProcessError;
    public bool IsFsError => Kind == HobTypeKind.FsError;
    public bool IsHttpClient => Kind == HobTypeKind.HttpClient;
    public bool IsHttpResponse => Kind == HobTypeKind.HttpResponse;
    public bool IsHttpError => Kind == HobTypeKind.HttpError;
    public bool IsDbRead => Kind == HobTypeKind.DbRead;
    public bool IsDbWrite => Kind == HobTypeKind.DbWrite;
    public bool IsTransaction => Kind == HobTypeKind.Transaction;
    public bool IsList => Kind == HobTypeKind.List;
    public bool IsMap => Kind == HobTypeKind.Map;
    public bool IsDbError => Kind == HobTypeKind.DbError;
    internal int UnionId { get; }
    internal int StructId { get; }
    internal int NewtypeId { get; }
    internal TypeParameterOwnerKind TypeParameterOwnerKind { get; }
    internal int TypeParameterOwnerId { get; }
    internal int TypeParameterOrdinal { get; }
    internal bool IsError => Kind == HobTypeKind.Error;

    internal static HobType Error { get; } = new(HobTypeKind.Error, "<error>");
    internal static HobType I32 { get; } = new(HobTypeKind.I32, "i32");
    internal static HobType I64 { get; } = new(HobTypeKind.I64, "i64");
    internal static HobType U32 { get; } = new(HobTypeKind.U32, "u32");
    internal static HobType U64 { get; } = new(HobTypeKind.U64, "u64");
    internal static HobType F64 { get; } = new(HobTypeKind.F64, "f64");
    internal static HobType ArithmeticError { get; } = new(HobTypeKind.ArithmeticError, "ArithmeticError");
    internal static HobType Unit { get; } = new(HobTypeKind.Unit, "Unit");
    internal static HobType Bool { get; } = new(HobTypeKind.Bool, "bool");
    internal static HobType Text { get; } = new(HobTypeKind.Text, "Text");
    internal static HobType Bytes { get; } = new(HobTypeKind.Bytes, "Bytes");
    internal static HobType BytesError { get; } = new(HobTypeKind.BytesError, "BytesError");
    internal static HobType Html { get; } = new(HobTypeKind.Html, "Html");
    internal static HobType FilePath { get; } = new(HobTypeKind.FilePath, "FilePath");
    internal static HobType FsRead { get; } = new(HobTypeKind.FsRead, "FsRead");
    internal static HobType FsWrite { get; } = new(HobTypeKind.FsWrite, "FsWrite");
    internal static HobType Config { get; } = new(HobTypeKind.Config, "Config");
    internal static HobType Secrets { get; } = new(HobTypeKind.Secrets, "Secrets");
    internal static HobType Logger { get; } = new(HobTypeKind.Logger, "Logger");
    internal static HobType ProcessRunner { get; } = new(HobTypeKind.ProcessRunner, "ProcessRunner");
    internal static HobType SecretText { get; } = new(HobTypeKind.SecretText, "Secret<Text>", arguments: [Text]);
    internal static HobType FsError { get; } = new(HobTypeKind.FsError, "FsError");
    internal static HobType ProcessOutput { get; } = new(HobTypeKind.ProcessOutput, "ProcessOutput");
    internal static HobType ProcessError { get; } = new(HobTypeKind.ProcessError, "ProcessError");
    internal static HobType HttpClient { get; } = new(HobTypeKind.HttpClient, "HttpClient");
    internal static HobType HttpResponse { get; } = new(HobTypeKind.HttpResponse, "HttpResponse");
    internal static HobType HttpError { get; } = new(HobTypeKind.HttpError, "HttpError");
    internal static HobType DbRead { get; } = new(HobTypeKind.DbRead, "DbRead");
    internal static HobType DbWrite { get; } = new(HobTypeKind.DbWrite, "DbWrite");
    internal static HobType Transaction { get; } = new(HobTypeKind.Transaction, "Transaction");
    internal static HobType DbError { get; } = new(HobTypeKind.DbError, "DbError");

    internal static HobType ForTypeParameter(
        TypeParameterOwnerKind ownerKind,
        int ownerId,
        int ordinal,
        string name) =>
        new(
            HobTypeKind.TypeParameter,
            name,
            typeParameterOwnerKind: ownerKind,
            typeParameterOwnerId: ownerId,
            typeParameterOrdinal: ordinal);
    internal static HobType ForUnion(int unionId, string name, IEnumerable<HobType>? arguments = null)
    {
        var typeArguments = (arguments ?? []).ToArray();
        var displayName = typeArguments.Length == 0
            ? name
            : name + "<" + string.Join(", ", typeArguments.Select(argument => argument.DisplayName)) + ">";
        return new HobType(
            HobTypeKind.Union,
            displayName,
            nominalName: name,
            unionId: unionId,
            arguments: typeArguments);
    }
    internal static HobType ForStruct(int structId, string name, IEnumerable<HobType>? arguments = null)
    {
        var typeArguments = (arguments ?? []).ToArray();
        var displayName = typeArguments.Length == 0
            ? name
            : name + "<" + string.Join(", ", typeArguments.Select(argument => argument.DisplayName)) + ">";
        return new HobType(
            HobTypeKind.Struct,
            displayName,
            nominalName: name,
            structId: structId,
            arguments: typeArguments);
    }
    internal static HobType ForNewtype(int newtypeId, string name) =>
        new(HobTypeKind.Newtype, name, nominalName: name, newtypeId: newtypeId);
    internal static HobType Option(HobType item) => new(HobTypeKind.Option, $"Option<{item.DisplayName}>", arguments: [item]);
    internal static HobType List(HobType item) => new(HobTypeKind.List, $"List<{item.DisplayName}>", arguments: [item]);
    internal static HobType Map(HobType key, HobType value) =>
        new(HobTypeKind.Map, $"Map<{key.DisplayName}, {value.DisplayName}>", arguments: [key, value]);
    internal static HobType Result(HobType ok, HobType error) => new(HobTypeKind.Result, $"Result<{ok.DisplayName}, {error.DisplayName}>", arguments: [ok, error]);

    public bool Equals(HobType? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || Kind != other.Kind) return false;
        if (Kind == HobTypeKind.Union)
        {
            if (UnionId != other.UnionId || _arguments.Count != other._arguments.Count) return false;
            for (var i = 0; i < _arguments.Count; i++)
                if (!_arguments[i].Equals(other._arguments[i])) return false;
            return true;
        }
        if (Kind == HobTypeKind.Struct)
        {
            if (StructId != other.StructId || _arguments.Count != other._arguments.Count) return false;
            for (var i = 0; i < _arguments.Count; i++)
                if (!_arguments[i].Equals(other._arguments[i])) return false;
            return true;
        }
        if (Kind == HobTypeKind.Newtype)
            return NewtypeId == other.NewtypeId;
        if (Kind == HobTypeKind.TypeParameter)
            return TypeParameterOwnerKind == other.TypeParameterOwnerKind &&
                   TypeParameterOwnerId == other.TypeParameterOwnerId &&
                   TypeParameterOrdinal == other.TypeParameterOrdinal;
        if (_arguments.Count != other._arguments.Count) return false;
        for (var i = 0; i < _arguments.Count; i++)
            if (!_arguments[i].Equals(other._arguments[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is HobType other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        if (Kind == HobTypeKind.Union)
        {
            hash.Add(UnionId);
            foreach (var argument in _arguments) hash.Add(argument);
        }
        else if (Kind == HobTypeKind.Struct)
        {
            hash.Add(StructId);
            foreach (var argument in _arguments) hash.Add(argument);
        }
        else if (Kind == HobTypeKind.Newtype)
        {
            hash.Add(NewtypeId);
        }
        else if (Kind == HobTypeKind.TypeParameter)
        {
            hash.Add(TypeParameterOwnerKind);
            hash.Add(TypeParameterOwnerId);
            hash.Add(TypeParameterOrdinal);
        }
        else
        {
            foreach (var argument in _arguments) hash.Add(argument);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(HobType? left, HobType? right) => Equals(left, right);
    public static bool operator !=(HobType? left, HobType? right) => !Equals(left, right);
}

internal sealed record CheckedVariantField(string? Name, HobType Type, int Index, Token At);
internal sealed record CheckedVariant(int Id, string Name, IReadOnlyList<CheckedVariantField> Fields, Token At);
internal sealed record CheckedUnion(
    int Id,
    string PackageId,
    string Module,
    string Name,
    bool Public,
    HobType Type,
    IReadOnlyList<HobType> TypeParameters,
    IReadOnlyList<CheckedVariant> Variants,
    Token At);
internal sealed record CheckedStructField(string Name, HobType Type, int Index, Token At);
internal sealed record CheckedStruct(
    int Id,
    string PackageId,
    string Module,
    string Name,
    bool Public,
    HobType Type,
    IReadOnlyList<HobType> TypeParameters,
    IReadOnlyList<CheckedStructField> Fields,
    Token At);
internal sealed record CheckedNewtype(
    int Id,
    string PackageId,
    string Module,
    string Name,
    bool Public,
    HobType Type,
    HobType Representation,
    Token At);
internal sealed record CheckedTraitMethod(
    int Id,
    int TraitId,
    string Name,
    IReadOnlyList<CheckedParameter> Parameters,
    HobType ReturnType,
    Token At);
internal sealed record CheckedTrait(
    int Id,
    string PackageId,
    string Module,
    string Name,
    bool Public,
    HobType SelfType,
    IReadOnlyList<CheckedTraitMethod> Methods,
    Token At);
internal sealed record CheckedTraitBound(int TraitId, Token At);
internal sealed record CheckedTraitImpl(
    int Id,
    string StableId,
    string PackageId,
    string Module,
    bool Public,
    int TraitId,
    HobType Target,
    IReadOnlyList<int> BindingFunctionIds,
    Token At);
internal sealed record CheckedParameter(string Name, HobType Type, int LocalId, Token At);
internal sealed record CheckedDirectCall(string PackageId, string Module, string Name);
internal sealed record CheckedManagedAdapterBinding(string BridgeId, string OperationId);
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
    HobType Type,
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
    HobType ArgsType,
    IReadOnlyList<CheckedCommandInput> Inputs,
    int HandlerFunctionId,
    string HandlerReference,
    bool HandlerIsAsync,
    int ErrorFunctionId,
    string ErrorReference,
    HobType ErrorType,
    IReadOnlyList<CheckedCapabilityParameter> Capabilities,
    Token At);

internal enum CheckedRouteContentKind { Json, Html }
internal enum CheckedRouteBindingKind { Path, Query }
internal sealed record CheckedRouteBinding(
    CheckedRouteBindingKind Kind,
    string WireName,
    string ParameterName,
    HobType Type,
    bool IsOptional,
    int HandlerParameterIndex,
    Token At);
internal enum CheckedCapabilityKind { FsRead, FsWrite, DbRead, DbWrite, HttpClient, Config, Secrets, Logger, ProcessRunner }
internal sealed record CheckedCapabilityParameter(
    CheckedCapabilityKind Kind,
    int HandlerParameterIndex,
    string ParameterName,
    Token At);

internal sealed record CheckedConfigField(
    string Name,
    string EnvironmentName,
    ConfigFieldKind Kind,
    bool Required,
    bool HasDefault,
    string? DefaultValue);

internal sealed record CheckedRouteResponse(
    int VariantId,
    string VariantName,
    int StatusCode,
    CheckedRouteContentKind? ContentKind,
    HobType? PayloadType,
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
        HobType? bodyType,
        IEnumerable<CheckedStructField> bodySchema,
        IEnumerable<CheckedRouteBinding> bindings,
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
        Bindings = Array.AsReadOnly(bindings.ToArray());
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
    public HobType? BodyType { get; }
    public IReadOnlyList<CheckedStructField> BodySchema { get; }
    public IReadOnlyList<CheckedRouteBinding> Bindings { get; }
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

internal abstract record TypedExpr(HobType Type, Token At);
internal sealed record TypedNumberExpr(HobType Type, Token At, string Value) : TypedExpr(Type, At);
internal sealed record TypedUnitExpr(Token At) : TypedExpr(HobType.Unit, At);
internal sealed record TypedBoolExpr(Token At, bool Value) : TypedExpr(HobType.Bool, At);
internal sealed record TypedTextExpr(Token At, string Value) : TypedExpr(HobType.Text, At);
internal sealed record TypedListExpr(HobType Type, IReadOnlyList<TypedExpr> Items, Token At) : TypedExpr(Type, At);
internal sealed record TypedLocalExpr(HobType Type, int LocalId, Token At) : TypedExpr(Type, At);
internal sealed record TypedLambdaInvokeExpr(
    HobType Type,
    HobType ParameterType,
    int ParameterLocalId,
    TypedExpr Argument,
    TypedExpr Body,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedBinaryExpr(HobType Type, string Op, TypedExpr Left, TypedExpr Right, Token At) : TypedExpr(Type, At);
internal sealed record TypedIntegerArithmeticExpr(
    HobType Type,
    HobType ValueType,
    IntegerArithmeticMode Mode,
    IntegerArithmeticOperator Operation,
    TypedExpr Receiver,
    TypedExpr Right,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedUnaryExpr(HobType Type, string Op, TypedExpr Operand, Token At) : TypedExpr(Type, At);
internal sealed record TypedCompareExpr(string Op, TypedExpr Left, TypedExpr Right, Token At) : TypedExpr(HobType.Bool, At);
internal sealed record TypedTextLengthExpr(TypedExpr Target, Token At) : TypedExpr(HobType.I32, At);
internal sealed record TypedTextTrimExpr(TypedExpr Target, Token At) : TypedExpr(HobType.Text, At);
internal sealed record TypedListLengthExpr(TypedExpr Target, Token At) : TypedExpr(HobType.I32, At);
internal sealed record TypedListGetExpr(HobType Type, TypedExpr Target, TypedExpr Index, Token At) : TypedExpr(Type, At);
internal sealed record TypedListAppendExpr(HobType Type, TypedExpr Target, TypedExpr Value, Token At) : TypedExpr(Type, At);
internal sealed record TypedBytesEmptyExpr(Token At) : TypedExpr(HobType.Bytes, At);
internal sealed record TypedBytesLengthExpr(TypedExpr Target, Token At) : TypedExpr(HobType.I32, At);
internal sealed record TypedBytesGetExpr(TypedExpr Target, TypedExpr Index, Token At) : TypedExpr(HobType.Option(HobType.I32), At);
internal sealed record TypedBytesAppendExpr(TypedExpr Target, TypedExpr Octet, Token At)
    : TypedExpr(HobType.Result(HobType.Bytes, HobType.BytesError), At);
internal sealed record TypedMapEmptyExpr(HobType Type, Token At) : TypedExpr(Type, At);
internal sealed record TypedMapSetExpr(
    HobType Type,
    TypedExpr Target,
    TypedExpr Key,
    TypedExpr Value,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedMapGetExpr(HobType Type, TypedExpr Target, TypedExpr Key, Token At) : TypedExpr(Type, At);
internal sealed record TypedMapKeysExpr(TypedExpr Target, Token At) : TypedExpr(HobType.List(HobType.Text), At);
internal sealed record TypedMapLengthExpr(TypedExpr Target, Token At) : TypedExpr(HobType.I32, At);
internal sealed record TypedCallExpr(
    HobType Type,
    int FunctionId,
    bool IsAsync,
    IReadOnlyList<HobType> TypeArguments,
    IReadOnlyList<TypedTraitWitness> TraitWitnesses,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);
internal abstract record TypedTraitWitness;
internal sealed record TypedConcreteTraitWitness(int ImplId) : TypedTraitWitness;
internal sealed record TypedForwardedTraitWitness(int TypeParameterOrdinal, int BoundOrdinal) : TypedTraitWitness;
internal sealed record TypedTraitCallExpr(
    HobType Type,
    int TraitId,
    int MethodId,
    TypedTraitWitness Witness,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);

internal enum BuiltinIntrinsic
{
    FsReadText,
    FsReadTextAsync,
    FsWriteText,
    FsWriteTextAsync,
    HttpGetTextAsync,
    ConfigGetText,
    ConfigGetSecretText,
    SecretsRevealText,
    LoggerInfo,
    ProcessRunTextAsync,
    HtmlText,
    HtmlHeading,
    HtmlParagraph,
    HtmlConcat,
    HtmlDocument,
    TextSplit
}

internal sealed record TypedIntrinsicCallExpr(
    HobType Type,
    BuiltinIntrinsic Intrinsic,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedAwaitExpr(HobType Type, TypedExpr Value, Token At) : TypedExpr(Type, At);
internal sealed record TypedResultPropagateExpr(
    HobType OkType,
    HobType ErrorType,
    TypedExpr Operand,
    Token At) : TypedExpr(OkType, At);

internal enum CheckedDatabaseOperationKind { QueryOne, Execute, TransactionExecute }
internal sealed record CheckedDatabaseOperation(
    CheckedDatabaseOperationKind Kind,
    string Effect,
    string Sql,
    int ParameterStructId,
    int? RowStructId);
internal sealed record TypedDatabaseCallExpr(
    HobType Type,
    TypedExpr Receiver,
    TypedExpr Parameters,
    CheckedDatabaseOperation Operation,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedTransactionCommitExpr(int TransactionLocalId, Token At)
    : TypedExpr(HobType.Result(HobType.Bool, HobType.DbError), At);

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
    BytesInvalidOctet,
    HttpInvalidTarget,
    HttpTransport,
    HttpTimeout,
    HttpResponseTooLarge,
    HttpInvalidText,
    ProcessInvalidArgument,
    ProcessInputTooLarge,
    ProcessOutputTooLarge,
    ProcessInvalidText,
    ProcessStartFailed,
    ProcessTimedOut,
    DbErrorStatement,
    DbErrorRowShape,
    ArithmeticErrorOverflow
}

internal sealed record TypedBuiltinConstructExpr(
    HobType Type,
    BuiltinVariant Variant,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);

internal sealed record TypedUnionConstructExpr(
    HobType Type,
    int UnionId,
    int VariantId,
    IReadOnlyList<TypedExpr> Arguments,
    Token At) : TypedExpr(Type, At);

internal sealed record TypedStructFieldValue(int FieldIndex, TypedExpr Value);
internal sealed record TypedStructConstructExpr(
    HobType Type,
    int StructId,
    IReadOnlyList<TypedStructFieldValue> Fields,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedNewtypeConstructExpr(
    HobType Type,
    int NewtypeId,
    TypedExpr Value,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedNewtypeProjectExpr(
    HobType Type,
    int NewtypeId,
    TypedExpr Target,
    Token At) : TypedExpr(Type, At);
internal sealed record TypedFieldAccessExpr(
    HobType Type,
    TypedExpr Target,
    int FieldIndex,
    Token At) : TypedExpr(Type, At);

internal abstract record TypedPattern(Token At);
internal sealed record TypedWildcardPattern(Token At) : TypedPattern(At);
internal sealed record BoundLocal(string Name, int LocalId, HobType Type, Token At);
internal sealed record TypedVariantPattern(
    VariantShape Shape,
    IReadOnlyList<BoundLocal> Bindings,
    Token At) : TypedPattern(At);
internal sealed record TypedMatchArm(TypedPattern Pattern, TypedExpr Body, Token At);
internal sealed record TypedMatchExpr(
    HobType Type,
    TypedExpr Value,
    IReadOnlyList<TypedMatchArm> Arms,
    Token At) : TypedExpr(Type, At);

internal abstract record TypedStmt(Token At);
internal sealed record TypedLetStmt(int LocalId, string Name, HobType Type, TypedExpr Value, Token At) : TypedStmt(At);
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
        IReadOnlyList<HobType> typeParameters,
        IReadOnlyList<IReadOnlyList<CheckedTraitBound>> typeParameterBounds,
        HobType returnType,
        IReadOnlyList<TypedStmt> body,
        IEnumerable<CheckedDirectCall> calls,
        IReadOnlyList<string> declaredEffects,
        Token at,
        CheckedManagedAdapterBinding? adapterBinding = null)
    {
        Id = id;
        PackageId = packageId;
        Module = module;
        Name = name;
        Public = isPublic;
        IsAsync = isAsync;
        Parameters = ReadOnly(parameters);
        TypeParameters = ReadOnly(typeParameters);
        TypeParameterBounds = Array.AsReadOnly(typeParameterBounds
            .Select(bounds => ReadOnly(bounds))
            .ToArray());
        ReturnType = returnType;
        Body = ReadOnly(body);
        Calls = ReadOnly(calls);
        DeclaredEffects = ReadOnly(declaredEffects);
        AdapterBinding = adapterBinding;
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
    public IReadOnlyList<HobType> TypeParameters { get; }
    public IReadOnlyList<IReadOnlyList<CheckedTraitBound>> TypeParameterBounds { get; }
    public HobType ReturnType { get; }
    public IReadOnlyList<CheckedDirectCall> Calls { get; }
    public IReadOnlyList<string> DeclaredEffects { get; }
    internal CheckedManagedAdapterBinding? AdapterBinding { get; }
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
        IEnumerable<CheckedNewtype> newtypes,
        IEnumerable<CheckedTest>? tests = null,
        IEnumerable<CheckedCommand>? commands = null,
        int? entryCommandId = null,
        IEnumerable<CheckedRoute>? routes = null,
        IEnumerable<CheckedConfigField>? configFields = null,
        IEnumerable<CheckedTrait>? traits = null,
        IEnumerable<CheckedTraitImpl>? traitImpls = null)
    {
        Modules = Array.AsReadOnly(modules.ToArray());
        EntryModule = entryModule;
        EntryFunctionId = entryFunctionId;
        Functions = Array.AsReadOnly(functions.ToArray());
        Unions = Array.AsReadOnly(unions.ToArray());
        Structs = Array.AsReadOnly(structs.ToArray());
        Newtypes = Array.AsReadOnly(newtypes.ToArray());
        Traits = Array.AsReadOnly((traits ?? []).ToArray());
        TraitImpls = Array.AsReadOnly((traitImpls ?? []).ToArray());
        Tests = Array.AsReadOnly((tests ?? []).ToArray());
        Commands = Array.AsReadOnly((commands ?? []).ToArray());
        EntryCommandId = entryCommandId;
        Routes = Array.AsReadOnly((routes ?? []).ToArray());
        ConfigFields = Array.AsReadOnly((configFields ?? []).ToArray());
    }

    // Retained for single-file API compatibility. For a package, this is the selected entry module.
    public string Module => EntryModule ?? (Modules.Count == 1 ? Modules[0] : string.Empty);
    public IReadOnlyList<string> Modules { get; }
    public string? EntryModule { get; }
    public int? EntryFunctionId { get; }
    public IReadOnlyList<CheckedFunction> Functions { get; }
    public IReadOnlyList<CheckedUnion> Unions { get; }
    public IReadOnlyList<CheckedStruct> Structs { get; }
    public IReadOnlyList<CheckedNewtype> Newtypes { get; }
    public IReadOnlyList<CheckedTrait> Traits { get; }
    public IReadOnlyList<CheckedTraitImpl> TraitImpls { get; }
    public IReadOnlyList<CheckedTest> Tests { get; }
    public IReadOnlyList<CheckedCommand> Commands { get; }
    public int? EntryCommandId { get; }
    public IReadOnlyList<CheckedRoute> Routes { get; }
    public IReadOnlyList<CheckedConfigField> ConfigFields { get; }
}

internal sealed record CheckResult(CheckedProgram? Program, List<Diagnostic> Diagnostics);

internal sealed record PackageModuleInput(
    string PackageId,
    ParsedProgram Program,
    IReadOnlyDictionary<string, string> DirectDependencies,
    string PackageDisplayLabel,
    string? ManagedAdapterBridgeId = null);

internal readonly record struct ModuleIdentity(string PackageId, string ModuleName);

internal sealed record VariantShape(
    string Name,
    string Key,
    int? UnionId,
    int VariantId,
    BuiltinVariant? Builtin,
    IReadOnlyList<HobType> PayloadTypes);

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
        bool rootIsWebPackage = false,
        IReadOnlyList<ConfigField>? rootConfigFields = null)
    {
        var diagnostics = new List<Diagnostic>();
        return new SemanticChecker(diagnostics).CheckPackage(
            modules,
            rootPackageId,
            entryModule,
            requireEntry: entryModule is not null,
            rootCapabilities,
            rootIsCliPackage,
            rootIsWebPackage,
            rootConfigFields);
    }
}

internal sealed class SemanticChecker(List<Diagnostic> diagnostics)
{
    private sealed record RouteBindingSpec(
        CheckedRouteBindingKind Kind,
        string WireName,
        HobType Type,
        bool IsOptional,
        Token At);

    private const int MaximumSemanticDepth = 192;
    private const string Sha256TextBridgeId = "hob.sha256-text.v1";
    private const string Sha256TextHashUtf8OperationId = "sha256.text.hash_utf8";
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
    private readonly List<NewtypeSymbol> _newtypes = [];
    private readonly List<TraitSymbol> _traits = [];
    private readonly List<CheckedTraitImpl> _traitImpls = [];
    private readonly List<TraitImplSymbol> _traitImplSymbols = [];
    private readonly List<FunctionSymbol> _functions = [];
    private readonly List<CheckedTest> _tests = [];
    private readonly List<CommandSymbol> _commands = [];
    private readonly List<CheckedRoute> _routes = [];
    private readonly HashSet<int> _activeTransactionLocals = [];
    private readonly HashSet<(string File, int Line, int Column)> _resourceListDiagnosticLocations = [];
    private readonly HashSet<(string File, int Line, int Column)> _resourceMapDiagnosticLocations = [];
    private readonly HashSet<(HobTypeKind Kind, int Id)> _invalidNominalRecursion = [];
    private HashSet<int>? _activeLambdaCaptureLocalIds;
    private bool[] _resourceReachableDeclarations = [];
    private bool[] _illegalListReachableDeclarations = [];
    private bool[] _illegalMapReachableDeclarations = [];
    private readonly Dictionary<ModuleIdentity, ModuleSymbols> _modulesByIdentity = new();
    private readonly Dictionary<string, string> _packageDisplayLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _stablePackageIdentities = new(StringComparer.Ordinal);
    private IReadOnlySet<string> _rootCapabilities = new HashSet<string>(StringComparer.Ordinal);
    private IReadOnlyList<CheckedConfigField> _checkedConfigFields = [];
    private IReadOnlyDictionary<string, ConfigField> _rootConfigFields =
        new Dictionary<string, ConfigField>(StringComparer.Ordinal);
    private ModuleIdentity _currentModule = new(string.Empty, string.Empty);
    private FunctionSymbol? _currentFunction;
    private bool _semanticDepthReported;
    private bool _rootIsCliPackage;
    private bool _rootIsWebPackage;
    private bool _resolvingNewtypeRepresentation;

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
        bool rootIsWebPackage = false,
        IReadOnlyList<ConfigField>? rootConfigFields = null)
    {
        _rootCapabilities = rootCapabilities ?? new HashSet<string>(StringComparer.Ordinal);
        _rootPackageId = rootPackageId;
        _rootIsCliPackage = rootIsCliPackage;
        _rootIsWebPackage = rootIsWebPackage;
        var orderedConfigFields = (rootConfigFields ?? [])
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToArray();
        _rootConfigFields = orderedConfigFields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        _checkedConfigFields = orderedConfigFields
            .Select(field => new CheckedConfigField(
                field.Name,
                "HOB_CONFIG_" + field.Name.ToUpperInvariant(),
                field.Kind,
                field.Required,
                field.HasDefault,
                field.DefaultValue))
            .ToArray();
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
        BuildStablePackageIdentities();

        // Register every declaration header before resolving signatures, so qualified references
        // across modules and packages can see the complete package graph.
        foreach (var module in orderedModules) RegisterUnionHeaders(module);
        foreach (var module in orderedModules) RegisterStructHeaders(module);
        foreach (var module in orderedModules) RegisterNewtypeHeaders(module);
        foreach (var module in orderedModules) RegisterTraitHeaders(module);
        foreach (var module in orderedModules) RegisterFunctionHeaders(module);
        foreach (var module in orderedModules)
            RegisterCommandHeaders(module, rootPackageId, entryModule, rootIsCliPackage || entryModule is not null);

        foreach (var module in orderedModules) PopulateUnionVariants(module);
        foreach (var module in orderedModules) PopulateStructFields(module);
        foreach (var module in orderedModules) PopulateNewtypeRepresentations(module);
        foreach (var module in orderedModules) PopulateTraitSignatures(module);
        ValidateNominalRecursion();
        foreach (var module in orderedModules)
        {
            RegisterFunctionSignatures(module);
        }
        ValidateDuplicateAdapterOperationBindings();
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
        ValidateNewtypeResourceRepresentations();
        foreach (var module in orderedModules)
            RegisterTraitImplementations(module);
        ValidateTraitImplCoherence();
        ValidatePublicSignatures();

        foreach (var function in _functions)
            CheckFunctionBody(function);

        ValidateTraitConstraintRecursion();

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
            symbol.TypeParameters,
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
            symbol.TypeParameters,
            ReadOnly(symbol.Fields),
            symbol.Declaration.At));
        var newtypes = _newtypes.Select(symbol => new CheckedNewtype(
            symbol.Id,
            symbol.PackageId,
            symbol.Module,
            symbol.Declaration.Name,
            symbol.Declaration.Public,
            symbol.Type,
            symbol.Representation,
            symbol.Declaration.At));
        var commands = _commands.Select(command => command.ToCheckedCommand());
        return new CheckResult(new CheckedProgram(
            orderedModules.Select(module => module.Program.Module).ToArray(),
            entry?.ModuleName ?? entryCommand?.ModuleName ?? routeEntryModule,
            entry?.Id,
            functions,
            unions,
            structs,
            newtypes,
            _tests,
            commands,
            entryCommand?.Id,
            _routes,
            _checkedConfigFields,
            _traits.Select(trait => trait.ToCheckedTrait()),
            _traitImpls), diagnostics);
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

    private void BuildStablePackageIdentities()
    {
        _stablePackageIdentities.Clear();
        _stablePackageIdentities[_rootPackageId] = "root";
        if (_rootPackageId == SinglePackageId) return;

        var packageModules = _modulesByIdentity.Values
            .GroupBy(module => module.PackageId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var distinctEdges = packageModules.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.DirectDependencies.Values.Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        var indegree = packageModules.Keys.ToDictionary(package => package, _ => 0, StringComparer.Ordinal);
        foreach (var dependencies in distinctEdges.Values)
            foreach (var dependency in dependencies)
                if (indegree.ContainsKey(dependency)) indegree[dependency]++;

        var ready = new SortedSet<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key), StringComparer.Ordinal);
        while (ready.Count != 0)
        {
            var package = ready.Min!;
            ready.Remove(package);
            if (_stablePackageIdentities.TryGetValue(package, out var parentIdentity))
            {
                foreach (var dependency in packageModules[package].DirectDependencies.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    var candidate = parentIdentity + "/dep:" + dependency.Key;
                    if (!_stablePackageIdentities.TryGetValue(dependency.Value, out var existing) ||
                        string.CompareOrdinal(candidate, existing) < 0)
                        _stablePackageIdentities[dependency.Value] = candidate;
                }
            }
            foreach (var dependency in distinctEdges[package])
            {
                if (!indegree.TryGetValue(dependency, out var remaining)) continue;
                indegree[dependency] = remaining - 1;
                if (remaining == 1) ready.Add(dependency);
            }
        }

        foreach (var package in packageModules.Keys)
            if (!_stablePackageIdentities.ContainsKey(package))
                _stablePackageIdentities[package] = "package:" + _packageDisplayLabels[package];
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

            var unionId = _unions.Count;
            var typeParameters = new List<HobType>();
            var typeParametersByName = new Dictionary<string, HobType>(StringComparer.Ordinal);
            foreach (var (typeParameter, ordinal) in declaration.TypeParameters.Select((parameter, index) => (parameter, index)))
            {
                var type = HobType.ForTypeParameter(TypeParameterOwnerKind.Union, unionId, ordinal, typeParameter.Name);
                typeParameters.Add(type);
                if (!typeParametersByName.TryAdd(typeParameter.Name, type))
                {
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' is already declared", typeParameter.At);
                    continue;
                }

                if (IsReservedTypeParameterName(typeParameter.Name))
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' is reserved", typeParameter.At);
                else if (module.TypeNames.Contains(typeParameter.Name))
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' collides with an existing type name", typeParameter.At);
            }

            var symbol = new UnionSymbol(
                unionId,
                module.Identity,
                declaration,
                HobType.ForUnion(unionId, declaration.Name, typeParameters),
                ReadOnly(typeParameters),
                typeParametersByName);
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

            var structId = _structs.Count;
            var typeParameters = new List<HobType>();
            var typeParametersByName = new Dictionary<string, HobType>(StringComparer.Ordinal);
            foreach (var (typeParameter, ordinal) in declaration.TypeParameters.Select((parameter, index) => (parameter, index)))
            {
                var type = HobType.ForTypeParameter(TypeParameterOwnerKind.Struct, structId, ordinal, typeParameter.Name);
                typeParameters.Add(type);
                if (!typeParametersByName.TryAdd(typeParameter.Name, type))
                {
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' is already declared", typeParameter.At);
                    continue;
                }

                if (IsReservedTypeParameterName(typeParameter.Name))
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' is reserved", typeParameter.At);
                else if (module.TypeNames.Contains(typeParameter.Name))
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' collides with an existing type name", typeParameter.At);
            }

            var symbol = new StructSymbol(
                structId,
                module.Identity,
                declaration,
                HobType.ForStruct(structId, declaration.Name, typeParameters),
                ReadOnly(typeParameters),
                typeParametersByName);
            _structs.Add(symbol);
            module.DeclaredStructs.Add(declaration.Name, symbol);
        }
    }

    private void RegisterNewtypeHeaders(ModuleSymbols module)
    {
        _currentModule = module.Identity;
        foreach (var declaration in module.Program.Newtypes)
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

            var newtypeId = _newtypes.Count;
            var symbol = new NewtypeSymbol(
                newtypeId,
                module.Identity,
                declaration,
                HobType.ForNewtype(newtypeId, declaration.Name));
            _newtypes.Add(symbol);
            module.DeclaredNewtypes.Add(declaration.Name, symbol);
        }
    }

    private void RegisterTraitHeaders(ModuleSymbols module)
    {
        _currentModule = module.Identity;
        foreach (var declaration in module.Program.Traits)
        {
            if (IsReservedTypeName(declaration.Name))
            {
                Add("E_NAME_DUPLICATE", $"Trait name '{declaration.Name}' is reserved", declaration.At);
                continue;
            }
            if (!module.TypeNames.Add(declaration.Name))
            {
                Add("E_NAME_DUPLICATE", $"Type name '{declaration.Name}' is already declared", declaration.At);
                continue;
            }

            var id = _traits.Count;
            var selfType = HobType.ForTypeParameter(TypeParameterOwnerKind.Trait, id, 0, "Self");
            var trait = new TraitSymbol(id, module.Identity, declaration, selfType);
            _traits.Add(trait);
            module.DeclaredTraits.Add(declaration.Name, trait);
        }
    }

    private void PopulateTraitSignatures(ModuleSymbols module)
    {
        _currentModule = module.Identity;
        foreach (var declaration in module.Program.Traits)
        {
            if (!module.DeclaredTraits.TryGetValue(declaration.Name, out var trait) ||
                trait.Declaration != declaration)
                continue;

            if (declaration.Methods.Count == 0)
                Add("E_TRAIT_DECL", $"Trait '{declaration.Name}' must declare at least one method", declaration.At);

            var methodNames = new HashSet<string>(StringComparer.Ordinal);
            var selfTypes = new Dictionary<string, HobType>(StringComparer.Ordinal) { ["Self"] = trait.SelfType };
            foreach (var method in declaration.Methods)
            {
                if (!methodNames.Add(method.Name))
                {
                    Add("E_TRAIT_DECL", $"Trait method '{method.Name}' is declared more than once", method.At);
                    continue;
                }
                if (method.Effects.Count != 0)
                    Add("E_TRAIT_DECL", "Trait methods in this language slice must declare effects {}", method.At);

                var parameters = new List<CheckedParameter>();
                var parameterNames = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < method.Parameters.Count; index++)
                {
                    var parameter = method.Parameters[index];
                    if (!parameterNames.Add(parameter.Name))
                        Add("E_NAME_DUPLICATE", $"Parameter '{parameter.Name}' is already declared", parameter.At);
                    parameters.Add(new CheckedParameter(
                        parameter.Name,
                        ResolveType(parameter.Type, 0, selfTypes),
                        index,
                        parameter.At));
                }
                var returnType = ResolveType(method.ReturnType, 0, selfTypes);
                if (!parameters.Any(parameter => ContainsType(parameter.Type, trait.SelfType)))
                    Add("E_TRAIT_DECL", $"Trait method '{method.Name}' must use Self in at least one parameter", method.At);

                trait.Methods.Add(new CheckedTraitMethod(
                    trait.Methods.Count,
                    trait.Id,
                    method.Name,
                    ReadOnly(parameters),
                    returnType,
                    method.At));
            }
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
                    inputs.Add(new CheckedCommandInput(flag.Name, HobType.Bool, CheckedCommandInputKind.Flag,
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
        var generatedDecl = new StructDecl(generatedName, [], true, generatedFields, command.At);
        var argsType = HobType.ForStruct(_structs.Count, generatedName);
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

    private static TypeSyntax TypeSyntaxForCommandInput(HobType type, Token at)
    {
        var name = type.Kind switch
        {
            HobTypeKind.I32 => "i32",
            HobTypeKind.Bool => "bool",
            HobTypeKind.Text => "Text",
            HobTypeKind.FilePath => "FilePath",
            _ => "<error>"
        };
        return new TypeSyntax(new SourceDeclarationRefSyntax(null, [], name, at), [], at);
    }

    private HobType ResolveCommandInputType(TypeSyntax syntax, Token at)
    {
        if (syntax.Reference.IsQualified || syntax.Args.Count != 0)
        {
            Add("E_COMMAND_DECL", "Command inputs support only FilePath, Text, and i32", at);
            return HobType.Error;
        }

        return syntax.Reference.Declaration switch
        {
            "FilePath" => HobType.FilePath,
            "Text" => HobType.Text,
            "i32" => HobType.I32,
            _ => ReportUnsupportedCommandInputType(syntax.Reference.Declaration, at)
        };
    }

    private HobType ReportUnsupportedCommandInputType(string name, Token at)
    {
        Add("E_COMMAND_DECL", $"Command input type '{name}' is not supported; use FilePath, Text, or i32", at);
        return HobType.Error;
    }

    private static bool CommandDefaultMatches(HobType type, CommandLiteralSyntax value) =>
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
                var validReturn = handler.ReturnType.Kind == HobTypeKind.Result &&
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
                    var hasHttpClient = commandCapabilities.Any(capability => capability.Kind == CheckedCapabilityKind.HttpClient);
                    var hasFsWrite = commandCapabilities.Any(capability => capability.Kind == CheckedCapabilityKind.FsWrite);
                    var hasProcessRunner = commandCapabilities.Any(capability => capability.Kind == CheckedCapabilityKind.ProcessRunner);
                    var hasConfig = commandCapabilities.Any(capability => capability.Kind is
                        CheckedCapabilityKind.Config or CheckedCapabilityKind.Secrets or CheckedCapabilityKind.Logger);
                    var expectation = hasProcessRunner
                        ? "Command handler must take the generated args type, optionally followed by FsRead, FsWrite, HttpClient, Config, Secrets, Logger, and ProcessRunner in that order, and return Result<Text, E> for a concrete error type"
                        : hasConfig
                            ? "Command handler must take the generated args type, optionally followed by FsRead, FsWrite, HttpClient, Config, Secrets, and Logger in that order, and return Result<Text, E> for a concrete error type"
                        : hasHttpClient
                            ? "Command handler must take the generated args type, optionally followed by FsRead, FsWrite, and HttpClient in that order, and return Result<Text, E> for a concrete error type"
                            : hasFsWrite
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

    private static bool IsConcreteSupportedCommandError(HobType type)
    {
        if (type.IsError || type.Kind == HobTypeKind.TypeParameter) return false;
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

        var declaredRoutes = new List<RouteDecl>();
        foreach (var route in module.Program.Routes)
        {
            var diagnosticCount = diagnostics.Count;
            foreach (var previous in declaredRoutes)
            {
                if (StringComparer.Ordinal.Equals(previous.Method, route.Method))
                {
                    if (RoutePathsOverlap(previous, route) &&
                        RouteLiteralCount(previous) == RouteLiteralCount(route))
                    {
                        Add(
                            "E_ROUTE_DECL",
                            $"Route '{route.Method} {route.Path}' overlaps with '{previous.Path}' at equal specificity",
                            route.At);
                        break;
                    }
                }
                else if (!StringComparer.Ordinal.Equals(previous.Path, route.Path) &&
                    RouteTemplateShapesEquivalent(previous, route))
                {
                    Add(
                        "E_ROUTE_DECL",
                        $"Route '{route.Method} {route.Path}' has an equivalent template shape to '{previous.Method} {previous.Path}'; cross-method routes must use identical path spelling",
                        route.At);
                    break;
                }
            }
            declaredRoutes.Add(route);

            var checkedRoute = CheckRoute(route);
            if (checkedRoute is not null && diagnostics.Count == diagnosticCount)
                _routes.Add(checkedRoute);
        }
    }

    private static bool RoutePathsOverlap(RouteDecl left, RouteDecl right)
    {
        if (left.PathSegments.Count != right.PathSegments.Count) return false;
        for (var index = 0; index < left.PathSegments.Count; index++)
        {
            var leftSegment = left.PathSegments[index];
            var rightSegment = right.PathSegments[index];
            if (leftSegment.Placeholder is not null || rightSegment.Placeholder is not null) continue;
            if (!StringComparer.OrdinalIgnoreCase.Equals(leftSegment.Literal, rightSegment.Literal)) return false;
        }
        return true;
    }

    private static int RouteLiteralCount(RouteDecl route) =>
        route.PathSegments.Count(segment => segment.Placeholder is null);

    private static bool RouteTemplateShapesEquivalent(RouteDecl left, RouteDecl right)
    {
        if (left.PathSegments.Count != right.PathSegments.Count) return false;
        for (var index = 0; index < left.PathSegments.Count; index++)
        {
            var leftSegment = left.PathSegments[index];
            var rightSegment = right.PathSegments[index];
            var leftIsPlaceholder = leftSegment.Placeholder is not null;
            var rightIsPlaceholder = rightSegment.Placeholder is not null;
            if (leftIsPlaceholder != rightIsPlaceholder) return false;
            if (leftIsPlaceholder) continue;
            if (!StringComparer.OrdinalIgnoreCase.Equals(leftSegment.Literal, rightSegment.Literal)) return false;
        }
        return true;
    }

    private CheckedRoute? CheckRoute(RouteDecl route)
    {
        var routeDiagnosticCount = diagnostics.Count;
        var bodies = route.Items.OfType<RouteBodySyntax>().ToArray();
        var bindingSpecs = new List<RouteBindingSpec>();
        var bindingDeclarationsValid = CheckRouteBindingDeclarations(route, bindingSpecs);
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

            if (responseStarted && item is RouteBodySyntax or RouteHandlerSyntax or RouteBindingSyntax)
                Add("E_ROUTE_DECL", "Route body, binding, and handler items must appear before response mappings", item.At);
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
                if (!bodyType.IsError && bodyType.Kind == HobTypeKind.Struct)
                {
                    var structure = _structs[bodyType.StructId];
                    if (IsSourceDeclaredStruct(structure))
                    {
                        bodyStructure = structure;
                        if (!ContainsRouteTypeError(bodyType, new HashSet<(HobTypeKind Kind, int Id)>()) &&
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
        var routeBindings = new List<CheckedRouteBinding>();
        var routeCapabilities = new List<CheckedCapabilityParameter>();
        var handlerValid = handlers.Length == 1 && bindingDeclarationsValid;
        if (handlers.Length == 1)
        {
            handler = ResolveFunctionReference(handlers[0].Reference);
            if (handler is null)
            {
                handlerValid = false;
            }
            else
            {
                var firstCapabilityParameter = route.Method == "POST" ? 1 : 0;
                var validParameters = true;
                var bindingParameterErrorReported = false;
                if (route.Method == "POST" && bodyStructure is null)
                {
                    // The missing or invalid POST body already has a route declaration diagnostic.
                    handlerValid = false;
                }
                else if (!bindingDeclarationsValid)
                {
                    // Binding declaration diagnostics are sufficient; do not cascade into handler layout checks.
                    handlerValid = false;
                }
                else
                {
                    var bodyParameterCount = route.Method == "POST" ? 1 : 0;
                    var bodyParameterValid = bodyParameterCount == 0 ||
                        (handler.Parameters.Count > 0 && handler.Parameters[0].Type == bodyStructure!.Type);
                    if (!bodyParameterValid)
                    {
                        validParameters = false;
                    }
                    else
                    {
                        var bindingsValid = CheckRouteBindingParameters(
                            handler,
                            bindingSpecs,
                            bodyParameterCount,
                            handlers[0].Reference.At,
                            routeBindings,
                            out bindingParameterErrorReported);
                        validParameters = bindingsValid;
                        if (bindingsValid)
                        {
                            firstCapabilityParameter = bodyParameterCount + bindingSpecs.Count;
                            validParameters = CheckRouteCapabilityParameters(
                                handler,
                                firstCapabilityParameter,
                                routeCapabilities);
                        }
                    }
                }
                var genericReplyUnion = handler.ReturnType.Kind == HobTypeKind.Union &&
                    _unions[handler.ReturnType.UnionId].TypeParameters.Count != 0;
                if (genericReplyUnion)
                    Add("E_ROUTE_HANDLER", "Route handler reply unions must be nongeneric", handlers[0].Reference.At);

                var validReturn = handler.ReturnType.IsError || handler.ReturnType.Kind == HobTypeKind.Union;
                var validGenericity = handler.TypeParameters.Count == 0;

                if (!validParameters || !validReturn || !validGenericity || genericReplyUnion)
                {
                    var capabilityScanStart = route.Method == "POST" ? 1 : 0;
                    var hasFsWrite = handler.Parameters.Skip(capabilityScanStart).Any(parameter => parameter.Type.IsFsWrite);
                    var hasHttpClient = handler.Parameters.Skip(capabilityScanStart).Any(parameter => parameter.Type.IsHttpClient);
                    var capabilityOrder = HasConfigCapability(handler, capabilityScanStart)
                        ? "FsWrite, DbRead, DbWrite, HttpClient, Config, Secrets, and Logger"
                        : hasHttpClient
                            ? hasFsWrite ? "FsWrite, DbRead, DbWrite, and HttpClient" : "DbRead, DbWrite, and HttpClient"
                            : hasFsWrite ? "FsWrite, DbRead, and DbWrite" : "DbRead and DbWrite";
                    var expectation = route.Method == "GET"
                        ? $"a non-generic function taking optional {capabilityOrder} capabilities in that order, and returning a declared union"
                        : $"a non-generic function taking the route body type followed by optional {capabilityOrder} capabilities in that order, and returning a declared union";
                    if (!bindingParameterErrorReported && !genericReplyUnion)
                        Add("E_ROUTE_HANDLER", $"Route handler must be {expectation}", handlers[0].Reference.At);
                    handlerValid = false;
                }

                if (handler.ReturnType.Kind == HobTypeKind.Union && !genericReplyUnion)
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
            routeBindings,
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

    private bool CheckRouteBindingDeclarations(RouteDecl route, List<RouteBindingSpec> bindings)
    {
        var valid = true;
        var declarationsByName = new Dictionary<string, RouteBindingSyntax>(StringComparer.OrdinalIgnoreCase);
        var uniqueDeclarations = new List<RouteBindingSyntax>();
        var pathDeclarationsByName = new Dictionary<string, RouteBindingSyntax>(StringComparer.OrdinalIgnoreCase);
        var declarationsWithSupportedTypes = new Dictionary<RouteBindingSyntax, HobType>(ReferenceEqualityComparer.Instance);
        var conflictedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var declaration in route.Items.OfType<RouteBindingSyntax>())
        {
            if (declarationsByName.TryGetValue(declaration.NameAt.Text, out var previous))
            {
                var conflict = previous.Kind != declaration.Kind;
                Add(
                    "E_ROUTE_BINDING",
                    conflict
                        ? $"Route path and query bindings cannot both declare '{declaration.NameAt.Text}'"
                        : $"Route {declaration.Kind.ToString().ToLowerInvariant()} binding '{declaration.NameAt.Text}' is declared more than once",
                    declaration.NameAt);
                conflictedNames.Add(declaration.NameAt.Text);
                valid = false;
                continue;
            }

            declarationsByName.Add(declaration.NameAt.Text, declaration);
            uniqueDeclarations.Add(declaration);
            if (declaration.Kind == RouteBindingSyntaxKind.Path)
                pathDeclarationsByName.Add(declaration.NameAt.Text, declaration);

            var type = ResolveRouteBindingType(declaration);
            if (type is null)
            {
                valid = false;
                continue;
            }

            declarationsWithSupportedTypes.Add(declaration, type);
        }

        var placeholderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in route.PathSegments)
        {
            if (segment.Placeholder is not { } placeholder) continue;
            if (!placeholderNames.Add(placeholder))
            {
                Add("E_ROUTE_BINDING", $"Route path placeholder '{placeholder}' is repeated", segment.At);
                valid = false;
                continue;
            }

            if (conflictedNames.Contains(placeholder)) continue;
            if (!pathDeclarationsByName.TryGetValue(placeholder, out var declaration))
            {
                Add("E_ROUTE_BINDING", $"Route path placeholder '{placeholder}' requires exactly one path declaration", segment.At);
                valid = false;
                continue;
            }

            if (!StringComparer.Ordinal.Equals(declaration.NameAt.Text, placeholder))
            {
                Add(
                    "E_ROUTE_BINDING",
                    $"Route path placeholder '{placeholder}' must match its path declaration spelling exactly",
                    declaration.NameAt);
                valid = false;
                continue;
            }

            if (declarationsWithSupportedTypes.TryGetValue(declaration, out var type))
            {
                bindings.Add(new RouteBindingSpec(
                    CheckedRouteBindingKind.Path,
                    placeholder,
                    type,
                    IsOptional: false,
                    declaration.NameAt));
            }
        }

        foreach (var declaration in uniqueDeclarations)
        {
            if (declaration.Kind == RouteBindingSyntaxKind.Path &&
                !conflictedNames.Contains(declaration.NameAt.Text) &&
                !placeholderNames.Contains(declaration.NameAt.Text))
            {
                Add(
                    "E_ROUTE_BINDING",
                    $"Path binding '{declaration.NameAt.Text}' is not used by the route template",
                    declaration.NameAt);
                valid = false;
            }
        }

        foreach (var declaration in uniqueDeclarations)
        {
            if (declaration.Kind != RouteBindingSyntaxKind.Query ||
                !declarationsWithSupportedTypes.TryGetValue(declaration, out var type))
                continue;

            bindings.Add(new RouteBindingSpec(
                CheckedRouteBindingKind.Query,
                declaration.NameAt.Text,
                type,
                IsOptional: type.Kind == HobTypeKind.Option,
                declaration.NameAt));
        }

        return valid;
    }

    private HobType? ResolveRouteBindingType(RouteBindingSyntax binding)
    {
        if (TryResolveRouteScalarType(binding.Type, out var scalarType))
            return scalarType;

        if (binding.Kind == RouteBindingSyntaxKind.Query &&
            IsUnqualifiedTypeNamed(binding.Type, "Option") &&
            binding.Type.Args.Count == 1 &&
            TryResolveRouteScalarType(binding.Type.Args[0], out scalarType))
            return HobType.Option(scalarType);

        Add(
            "E_ROUTE_BINDING",
            binding.Kind == RouteBindingSyntaxKind.Path
                ? "Path bindings support only Text and i32"
                : "Query bindings support only Text, i32, Option<Text>, and Option<i32>",
            binding.Type.At);
        return null;
    }

    private static bool TryResolveRouteScalarType(TypeSyntax syntax, out HobType type)
    {
        type = HobType.Error;
        if (!IsUnqualifiedTypeNamed(syntax, syntax.Reference.Declaration) || syntax.Args.Count != 0)
            return false;

        type = syntax.Reference.Declaration switch
        {
            "Text" => HobType.Text,
            "i32" => HobType.I32,
            _ => HobType.Error
        };
        return !type.IsError;
    }

    private static bool IsUnqualifiedTypeNamed(TypeSyntax syntax, string name) =>
        !syntax.Reference.IsQualified &&
        syntax.Reference.Module.Count == 0 &&
        StringComparer.Ordinal.Equals(syntax.Reference.Declaration, name);

    private bool CheckRouteBindingParameters(
        FunctionSymbol handler,
        IReadOnlyList<RouteBindingSpec> bindingSpecs,
        int firstParameterIndex,
        Token handlerAt,
        List<CheckedRouteBinding> checkedBindings,
        out bool reportedError)
    {
        var valid = true;
        reportedError = false;
        for (var bindingIndex = 0; bindingIndex < bindingSpecs.Count; bindingIndex++)
        {
            var binding = bindingSpecs[bindingIndex];
            var handlerParameterIndex = firstParameterIndex + bindingIndex;
            if (handlerParameterIndex >= handler.Parameters.Count)
            {
                Add(
                    "E_ROUTE_HANDLER",
                    $"Route handler is missing a parameter for {binding.Kind.ToString().ToLowerInvariant()} binding '{binding.WireName}' of type '{binding.Type.DisplayName}'",
                    handlerAt);
                reportedError = true;
                return false;
            }

            var parameter = handler.Parameters[handlerParameterIndex];
            if (parameter.Type != binding.Type)
            {
                Add(
                    "E_ROUTE_HANDLER",
                    $"Route {binding.Kind.ToString().ToLowerInvariant()} binding '{binding.WireName}' expects handler parameter type '{binding.Type.DisplayName}', found '{parameter.Type.DisplayName}'",
                    parameter.At);
                reportedError = true;
                valid = false;
                continue;
            }

            checkedBindings.Add(new CheckedRouteBinding(
                binding.Kind,
                binding.WireName,
                parameter.Name,
                binding.Type,
                binding.IsOptional,
                handlerParameterIndex,
                binding.At));
        }
        return valid;
    }

    private bool CheckRouteCapabilityParameters(
        FunctionSymbol handler,
        int firstCapabilityParameter,
        List<CheckedCapabilityParameter> capabilities)
    {
        var seen = new HashSet<HobType>();
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
                CheckedCapabilityKind.HttpClient => 3,
                CheckedCapabilityKind.Config => 4,
                CheckedCapabilityKind.Secrets => 5,
                CheckedCapabilityKind.Logger => 6,
                _ => -1
            };

            if (kind is null || order < 0)
            {
                var hasHttpClient = handler.Parameters.Skip(firstCapabilityParameter).Any(item => item.Type.IsHttpClient);
                Add(
                    "E_ROUTE_HANDLER",
                    HasConfigCapability(handler, firstCapabilityParameter)
                        ? "Route handlers may receive only FsWrite, DbRead, DbWrite, HttpClient, Config, Secrets, and Logger capability parameters after the request body"
                        : hasHttpClient
                            ? "Route handlers may receive only FsWrite, DbRead, DbWrite, and HttpClient capability parameters after the request body"
                            : "Route handlers may receive only DbRead followed by DbWrite capability parameters after the request body",
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
                var hasHttpClient = handler.Parameters.Skip(firstCapabilityParameter).Any(item => item.Type.IsHttpClient);
                var hasFsWrite = handler.Parameters.Skip(firstCapabilityParameter).Any(item => item.Type.IsFsWrite);
                var orderMessage = HasConfigCapability(handler, firstCapabilityParameter)
                    ? "Route handler capability parameters must appear in FsWrite, DbRead, DbWrite, HttpClient, Config, Secrets, Logger order"
                    : hasHttpClient
                        ? hasFsWrite
                            ? "Route handler capability parameters must appear in FsWrite, DbRead, DbWrite, HttpClient order"
                            : "Route handler capability parameters must appear in DbRead, DbWrite, HttpClient order"
                        : hasFsWrite
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
                CheckedCapabilityKind.HttpClient => 2,
                CheckedCapabilityKind.Config => 3,
                CheckedCapabilityKind.Secrets => 4,
                CheckedCapabilityKind.Logger => 5,
                CheckedCapabilityKind.ProcessRunner => 6,
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

    private static CheckedCapabilityKind? CapabilityKind(HobType type) => type.Kind switch
    {
        HobTypeKind.FsRead => CheckedCapabilityKind.FsRead,
        HobTypeKind.FsWrite => CheckedCapabilityKind.FsWrite,
        HobTypeKind.DbRead => CheckedCapabilityKind.DbRead,
        HobTypeKind.DbWrite => CheckedCapabilityKind.DbWrite,
        HobTypeKind.HttpClient => CheckedCapabilityKind.HttpClient,
        HobTypeKind.Config => CheckedCapabilityKind.Config,
        HobTypeKind.Secrets => CheckedCapabilityKind.Secrets,
        HobTypeKind.Logger => CheckedCapabilityKind.Logger,
        HobTypeKind.ProcessRunner => CheckedCapabilityKind.ProcessRunner,
        _ => null
    };

    private static string CapabilityEffect(CheckedCapabilityKind kind) => kind switch
    {
        CheckedCapabilityKind.FsRead => "fs.read",
        CheckedCapabilityKind.FsWrite => "fs.write",
        CheckedCapabilityKind.DbRead => "db.read",
        CheckedCapabilityKind.DbWrite => "db.write",
        CheckedCapabilityKind.HttpClient => "net.client",
        CheckedCapabilityKind.Config => "env.read",
        CheckedCapabilityKind.Secrets => "secret.reveal",
        CheckedCapabilityKind.Logger => "log.write",
        CheckedCapabilityKind.ProcessRunner => "process.spawn",
        _ => throw new InvalidOperationException("Unknown checked capability")
    };

    private static bool HasConfigCapability(FunctionSymbol handler, int firstCapabilityParameter) =>
        handler.Parameters.Skip(firstCapabilityParameter).Any(parameter =>
            parameter.Type.Kind is HobTypeKind.Config or HobTypeKind.Secrets or HobTypeKind.Logger);

    private CheckedRouteResponse CheckRouteResponse(RouteResponseSyntax response, CheckedVariant variant)
    {
        var contentKind = response.Format switch
        {
            "json" => CheckedRouteContentKind.Json,
            "html" => CheckedRouteContentKind.Html,
            _ => (CheckedRouteContentKind?)null
        };
        HobType? payloadType = variant.Fields.Count == 1 ? variant.Fields[0].Type : null;
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

    private bool IsJsonRouteType(HobType type, HashSet<int> activeStructs)
    {
        if (type.Kind is HobTypeKind.I32 or HobTypeKind.Bool or HobTypeKind.Text)
            return true;
        if (type.Kind != HobTypeKind.Struct)
            return false;
        var structure = _structs[type.StructId];
        if (!IsSourceDeclaredStruct(structure))
            return false;
        if (structure.TypeParameters.Count != 0)
            return false;
        if (!activeStructs.Add(type.StructId))
            return false;

        var result = structure.Fields.All(field => IsJsonRouteType(field.Type, activeStructs));
        activeStructs.Remove(type.StructId);
        return result;
    }

    private bool ContainsRouteTypeError(HobType type, HashSet<(HobTypeKind Kind, int Id)> activeNominals)
    {
        if (type.IsError)
            return true;
        if (type.Kind == HobTypeKind.Struct)
        {
            var identity = (HobTypeKind.Struct, type.StructId);
            if (!activeNominals.Add(identity))
                return false;
            var containsError = _structs[type.StructId].Fields
                .Any(field => ContainsRouteTypeError(field.Type, activeNominals));
            activeNominals.Remove(identity);
            if (containsError)
                return true;
        }
        else if (type.Kind == HobTypeKind.Newtype)
        {
            var identity = (HobTypeKind.Newtype, type.NewtypeId);
            if (!activeNominals.Add(identity))
                return false;
            var containsError = ContainsRouteTypeError(_newtypes[type.NewtypeId].Representation, activeNominals);
            activeNominals.Remove(identity);
            if (containsError)
                return true;
        }
        return type.Arguments.Any(argument => ContainsRouteTypeError(argument, activeNominals));
    }

    private bool IsSourceDeclaredStruct(StructSymbol structure) =>
        _modulesByIdentity[structure.ModuleIdentity].Program.Structs
            .Any(declaration => ReferenceEquals(declaration, structure.Declaration));

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

                    fields.Add(new CheckedVariantField(
                        field.Name,
                        ResolveType(field.Type, 0, symbol.TypeParametersByName),
                        fieldIndex,
                        field.At));
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
                    ResolveType(field.Type, 0, symbol.TypeParametersByName),
                    fieldIndex,
                    field.At));
            }
        }
    }

    private void PopulateNewtypeRepresentations(ModuleSymbols module)
    {
        _currentModule = module.Identity;
        foreach (var declaration in module.Program.Newtypes)
        {
            if (!module.DeclaredNewtypes.TryGetValue(declaration.Name, out var symbol) || symbol.Declaration != declaration)
                continue;
            _resolvingNewtypeRepresentation = true;
            try
            {
                symbol.Representation = ResolveType(declaration.Representation, 0);
            }
            finally
            {
                _resolvingNewtypeRepresentation = false;
            }
        }
    }

    private void ValidateNominalRecursion()
    {
        foreach (var root in _structs)
            VisitRoot(root.Type, true, root.Declaration.At);
        foreach (var root in _newtypes)
            VisitRoot(root.Type, true, root.Declaration.Representation.At);
        foreach (var root in _unions)
            VisitRoot(root.Type, false, root.Declaration.At);

        void VisitRoot(HobType rootType, bool rootValuePath, Token rootAt)
        {
            var pending = new Stack<(HobType Type, bool ValuePath, bool IsExit, bool IsValueNominal, Token At)>();
            var activeNominalTypes = new List<HobType>();
            var activeValueNominalTypes = new List<HobType>();
            pending.Push((rootType, rootValuePath, false, false, rootAt));
            while (pending.Count != 0)
            {
                var frame = pending.Pop();
                if (frame.IsExit)
                {
                    if (frame.IsValueNominal)
                        activeValueNominalTypes.RemoveAt(activeValueNominalTypes.Count - 1);
                    activeNominalTypes.RemoveAt(activeNominalTypes.Count - 1);
                    continue;
                }

                var type = frame.Type;
                if (type.IsError || type.Kind == HobTypeKind.TypeParameter) continue;

                if (type.Kind is HobTypeKind.Option or HobTypeKind.List or HobTypeKind.Map or HobTypeKind.Result)
                {
                    for (var index = type.Arguments.Count - 1; index >= 0; index--)
                        pending.Push((type.Arguments[index], false, false, false, frame.At));
                    continue;
                }

                if (type.Kind is not (HobTypeKind.Struct or HobTypeKind.Union or HobTypeKind.Newtype))
                    continue;

                if (frame.ValuePath && type.Kind is (HobTypeKind.Struct or HobTypeKind.Newtype))
                {
                    var cycleStart = activeValueNominalTypes.FindIndex(active => active == type);
                    if (cycleStart >= 0)
                    {
                        var cycle = activeNominalTypes.SkipWhile(active => active != type).Append(type).ToArray();
                        if (!cycle.Any(IsInvalidNominal))
                            Add(
                                "E_TYPE_MISMATCH",
                                "Structs and newtypes cannot form cycles through only direct value fields; use Option, Result, or a tagged union to break the cycle",
                                frame.At);
                        MarkInvalid(type, cycle);
                        continue;
                    }
                }

                if (activeNominalTypes.Contains(type)) continue;

                var priorInstantiation = activeNominalTypes.LastOrDefault(active =>
                    active.Kind == type.Kind && NominalDeclarationId(active) == NominalDeclarationId(type));
                if (priorInstantiation is not null && HasExpandedTypeArguments(priorInstantiation, type))
                {
                    var expansionPath = activeNominalTypes.Append(type).ToArray();
                    if (!expansionPath.Any(IsInvalidNominal))
                        Add(
                            "E_TYPE_MISMATCH",
                            "Recursive generic nominal types cannot grow their type arguments; use a recursive field that preserves its type arguments",
                            frame.At);
                    MarkInvalid(type, expansionPath);
                    continue;
                }

                activeNominalTypes.Add(type);
                var isValueNominal = frame.ValuePath && type.Kind is (HobTypeKind.Struct or HobTypeKind.Newtype);
                if (isValueNominal) activeValueNominalTypes.Add(type);
                pending.Push((type, frame.ValuePath, true, isValueNominal, frame.At));

                switch (type.Kind)
                {
                    case HobTypeKind.Struct:
                        {
                            var structure = _structs[type.StructId];
                            for (var index = structure.Fields.Count - 1; index >= 0; index--)
                            {
                                var field = structure.Fields[index];
                                pending.Push((
                                    InstantiateStructFieldType(structure, type, field.Type),
                                    frame.ValuePath,
                                    false,
                                    false,
                                    field.At));
                            }
                            break;
                        }
                    case HobTypeKind.Union:
                        {
                            var union = _unions[type.UnionId];
                            var fields = union.Variants.SelectMany(variant => variant.Fields).ToArray();
                            for (var index = fields.Length - 1; index >= 0; index--)
                                pending.Push((
                                    InstantiateUnionFieldType(union, type, fields[index].Type),
                                    false,
                                    false,
                                    false,
                                    fields[index].At));
                            break;
                        }
                    case HobTypeKind.Newtype:
                        {
                            var newtype = _newtypes[type.NewtypeId];
                            pending.Push((newtype.Representation, frame.ValuePath, false, false, newtype.Declaration.Representation.At));
                            break;
                        }
                }
            }
        }

        static int NominalDeclarationId(HobType type) => type.Kind switch
        {
            HobTypeKind.Struct => type.StructId,
            HobTypeKind.Union => type.UnionId,
            HobTypeKind.Newtype => type.NewtypeId,
            _ => -1
        };

        static bool HasExpandedTypeArguments(HobType previous, HobType current)
        {
            if (previous.Arguments.Count != current.Arguments.Count) return false;
            var matchedPrevious = new bool[previous.Arguments.Count];
            var unmatchedCurrent = new List<HobType>();
            foreach (var currentArgument in current.Arguments)
            {
                var exactMatch = -1;
                for (var index = 0; index < previous.Arguments.Count; index++)
                {
                    if (!matchedPrevious[index] && previous.Arguments[index] == currentArgument)
                    {
                        exactMatch = index;
                        break;
                    }
                }
                if (exactMatch >= 0)
                    matchedPrevious[exactMatch] = true;
                else
                    unmatchedCurrent.Add(currentArgument);
            }

            foreach (var currentArgument in unmatchedCurrent)
                foreach (var (previousArgument, index) in previous.Arguments.Select((argument, index) => (argument, index)))
                {
                    if (!matchedPrevious[index] && ContainsType(currentArgument, previousArgument))
                        return true;
                }
            return false;
        }

        bool IsInvalidNominal(HobType type) => type.Kind switch
        {
            HobTypeKind.Struct => _invalidNominalRecursion.Contains((HobTypeKind.Struct, type.StructId)),
            HobTypeKind.Union => _invalidNominalRecursion.Contains((HobTypeKind.Union, type.UnionId)),
            HobTypeKind.Newtype => _invalidNominalRecursion.Contains((HobTypeKind.Newtype, type.NewtypeId)),
            _ => false
        };

        void MarkInvalid(HobType currentType, IEnumerable<HobType> activeTypes)
        {
            Mark(currentType);
            foreach (var active in activeTypes) Mark(active);

            void Mark(HobType type)
            {
                if (type.Kind is HobTypeKind.Struct or HobTypeKind.Union or HobTypeKind.Newtype)
                    _invalidNominalRecursion.Add((type.Kind, NominalDeclarationId(type)));
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

            var typeParameters = new List<HobType>();
            var typeParametersByName = new Dictionary<string, HobType>(StringComparer.Ordinal);
            foreach (var (typeParameter, ordinal) in declaration.TypeParameters.Select((parameter, index) => (parameter, index)))
            {
                var type = HobType.ForTypeParameter(TypeParameterOwnerKind.Function, symbol.Id, ordinal, typeParameter.Name);
                typeParameters.Add(type);

                if (!typeParametersByName.TryAdd(typeParameter.Name, type))
                {
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' is already declared", typeParameter.At);
                    continue;
                }

                if (IsReservedTypeParameterName(typeParameter.Name))
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' is reserved", typeParameter.At);
                else if (module.TypeNames.Contains(typeParameter.Name))
                    Add("E_NAME_DUPLICATE", $"Type parameter '{typeParameter.Name}' collides with an existing type name", typeParameter.At);
            }

            symbol.TypeParameters = ReadOnly(typeParameters);
            symbol.TypeParametersByName = typeParametersByName;

            var boundsByParameter = new List<IReadOnlyList<CheckedTraitBound>>(declaration.TypeParameters.Count);
            foreach (var typeParameter in declaration.TypeParameters)
            {
                var bounds = new List<CheckedTraitBound>();
                var boundIds = new HashSet<int>();
                foreach (var boundReference in typeParameter.TraitBounds ?? [])
                {
                    var trait = ResolveTraitReference(boundReference);
                    if (trait is null) continue;
                    if (!boundIds.Add(trait.Id))
                    {
                        Add("E_TRAIT_BOUND", $"Trait bound '{FormatReference(boundReference)}' is repeated", boundReference.At);
                        continue;
                    }
                    bounds.Add(new CheckedTraitBound(trait.Id, boundReference.At));
                }
                boundsByParameter.Add(ReadOnly(bounds));
            }
            symbol.TypeParameterBounds = ReadOnly(boundsByParameter);

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
            ValidateAdapterFunction(module, symbol);
        }
    }

    private void RegisterTraitImplementations(ModuleSymbols module)
    {
        _currentModule = module.Identity;
        foreach (var declaration in module.Program.Impls)
        {
            var trait = ResolveTraitReference(declaration.Trait);
            var target = ResolveType(declaration.Target, 0, new Dictionary<string, HobType>(StringComparer.Ordinal));
            var valid = trait is not null && !target.IsError;
            if (ContainsTypeParameter(target))
            {
                Add("E_TRAIT_IMPL_SIGNATURE", "Trait implementation targets must be closed types", declaration.Target.At);
                valid = false;
            }
            if (ContainsResourceHandle(target))
            {
                Add("E_RESOURCE_ESCAPE", "Trait implementations cannot target values that store resource or capability handles", declaration.Target.At);
                valid = false;
            }
            if (trait is not null && declaration.Public && !trait.Declaration.Public)
            {
                Add("E_TYPE_VISIBILITY", $"Public implementation exposes private trait '{trait.Declaration.Name}'", declaration.At);
                valid = false;
            }
            if (declaration.Public && !PublicNominalComponents(target))
            {
                Add("E_TYPE_VISIBILITY", $"Public implementation exposes private target type '{target.DisplayName}'", declaration.Target.At);
                valid = false;
            }
            if (trait is not null && module.PackageId != trait.PackageId && !OwnsNominalHead(target, module.PackageId))
            {
                Add("E_TRAIT_IMPL_SIGNATURE", "A trait implementation must be declared by the trait's package or a package that owns a nominal target type", declaration.At);
                valid = false;
            }

            if (trait is null)
            {
                foreach (var binding in declaration.Methods)
                    _ = ResolveFunctionReference(binding.Function);
                continue;
            }

            var methodByName = trait.Methods.ToDictionary(method => method.Name, StringComparer.Ordinal);
            var suppliedMethods = new HashSet<string>(StringComparer.Ordinal);
            var bindingFunctions = new FunctionSymbol?[trait.Methods.Count];
            foreach (var binding in declaration.Methods)
            {
                if (!suppliedMethods.Add(binding.MethodName))
                {
                    Add("E_TRAIT_IMPL_SIGNATURE", $"Trait method '{binding.MethodName}' is bound more than once", binding.At);
                    valid = false;
                    _ = ResolveFunctionReference(binding.Function);
                    continue;
                }
                if (!methodByName.TryGetValue(binding.MethodName, out var method))
                {
                    Add("E_TRAIT_IMPL_SIGNATURE", $"Trait '{trait.Declaration.Name}' has no method '{binding.MethodName}'", binding.MethodAt);
                    valid = false;
                    _ = ResolveFunctionReference(binding.Function);
                    continue;
                }

                var function = ResolveFunctionReference(binding.Function);
                if (function is null)
                {
                    valid = false;
                    continue;
                }
                bindingFunctions[method.Id] = function;
                var substitution = new Dictionary<HobType, HobType> { [trait.SelfType] = target };
                var expectedParameters = method.Parameters.Select(parameter => SubstituteType(parameter.Type, substitution)).ToArray();
                var expectedReturn = SubstituteType(method.ReturnType, substitution);
                var signatureMatches = !function.Declaration.IsAsync && function.TypeParameters.Count == 0 &&
                    function.DeclaredEffects.Count == 0 && function.Parameters.Count == expectedParameters.Length &&
                    function.Parameters.Select(parameter => parameter.Type).SequenceEqual(expectedParameters) &&
                    function.ReturnType == expectedReturn;
                if (!signatureMatches)
                {
                    Add("E_TRAIT_IMPL_SIGNATURE", $"Binding for '{trait.Declaration.Name}.{method.Name}' must name a synchronous, nongeneric, pure function with signature ({string.Join(", ", expectedParameters.Select(type => type.DisplayName))}) -> {expectedReturn.DisplayName}", binding.At);
                    valid = false;
                }
            }

            foreach (var method in trait.Methods)
            {
                if (bindingFunctions[method.Id] is null)
                {
                    Add("E_TRAIT_IMPL_SIGNATURE", $"Trait implementation is missing binding for '{method.Name}'", declaration.At);
                    valid = false;
                }
            }

            if (!valid) continue;
            var stableId = StableTraitImplId(module.PackageId, module.Program.Module, trait, target);
            var checkedImpl = new CheckedTraitImpl(
                _traitImpls.Count,
                stableId,
                module.PackageId,
                module.Program.Module,
                declaration.Public,
                trait.Id,
                target,
                bindingFunctions.Select(function => function!.Id).ToArray(),
                declaration.At);
            _traitImpls.Add(checkedImpl);
            _traitImplSymbols.Add(new TraitImplSymbol(checkedImpl, trait, bindingFunctions.Select(function => function!).ToArray()));
        }
    }

    private bool PublicNominalComponents(HobType type)
    {
        var pending = new Stack<HobType>();
        var visited = new HashSet<HobType>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (current.Kind == HobTypeKind.Struct)
            {
                if (!_structs[current.StructId].Declaration.Public) return false;
            }
            else if (current.Kind == HobTypeKind.Union)
            {
                if (!_unions[current.UnionId].Declaration.Public) return false;
            }
            else if (current.Kind == HobTypeKind.Newtype &&
                     !_newtypes[current.NewtypeId].Declaration.Public)
            {
                return false;
            }

            if (!visited.Add(current)) continue;
            foreach (var argument in current.Arguments)
                pending.Push(argument);
        }
        return true;
    }

    private bool OwnsNominalHead(HobType type, string packageId)
    {
        if (type.Kind == HobTypeKind.Struct && _structs[type.StructId].PackageId == packageId) return true;
        if (type.Kind == HobTypeKind.Union && _unions[type.UnionId].PackageId == packageId) return true;
        if (type.Kind == HobTypeKind.Newtype && _newtypes[type.NewtypeId].PackageId == packageId) return true;
        return false;
    }

    private string StableTraitImplId(string packageId, string module, TraitSymbol trait, HobType target)
    {
        var identity = $"trait-impl-v1|{StablePackageIdentity(packageId)}::{module}|{StablePackageIdentity(trait.PackageId)}::{trait.Module}::{trait.Declaration.Name}|{StableTypeIdentity(target)}";
        return "hob.impl.v1." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private string StablePackageIdentity(string packageId)
    {
        if (_stablePackageIdentities.TryGetValue(packageId, out var identity)) return identity;
        throw new InvalidOperationException("A trait identity package is absent from the checked dependency graph");
    }

    private string StableTypeIdentity(HobType type) => type.Kind switch
    {
        HobTypeKind.Struct => StableNominalIdentity("struct", _structs[type.StructId].PackageId, _structs[type.StructId].Module, _structs[type.StructId].Declaration.Name, type.Arguments),
        HobTypeKind.Union => StableNominalIdentity("union", _unions[type.UnionId].PackageId, _unions[type.UnionId].Module, _unions[type.UnionId].Declaration.Name, type.Arguments),
        HobTypeKind.Newtype => StableNominalIdentity("newtype", _newtypes[type.NewtypeId].PackageId, _newtypes[type.NewtypeId].Module, _newtypes[type.NewtypeId].Declaration.Name, []),
        HobTypeKind.Option or HobTypeKind.List => type.Kind + "<" + StableTypeIdentity(type.Arguments[0]) + ">",
        HobTypeKind.Map or HobTypeKind.Result => type.Kind + "<" + string.Join(",", type.Arguments.Select(StableTypeIdentity)) + ">",
        HobTypeKind.TypeParameter => throw new InvalidOperationException("A trait implementation target must be closed"),
        _ => type.DisplayName
    };

    private string StableNominalIdentity(string kind, string packageId, string module, string name, IReadOnlyList<HobType> arguments) =>
        $"{kind}:{StablePackageIdentity(packageId)}::{module}::{name}" +
        (arguments.Count == 0 ? string.Empty : "<" + string.Join(",", arguments.Select(StableTypeIdentity)) + ">");

    private void ValidateAdapterFunction(ModuleSymbols module, FunctionSymbol symbol)
    {
        var declaration = symbol.Declaration;
        if (!declaration.IsAdapter)
            return;

        var valid = true;
        ManagedAdapterDefinition? adapterDefinition = null;
        if (!declaration.Public)
        {
            Add("E_ADAPTER_VISIBILITY", "Adapter functions must be public", declaration.At);
            valid = false;
        }

        if (module.ManagedAdapterBridgeId is null)
        {
            Add("E_ADAPTER_MANIFEST", "Adapter functions require a managed adapter in the package manifest", declaration.At);
            valid = false;
        }
        else if (module.ManagedAdapterBridgeId != Sha256TextBridgeId ||
                 !ManagedAdapterCatalog.TryGetDefinition(module.ManagedAdapterBridgeId, out adapterDefinition) ||
                 adapterDefinition is null)
        {
            Add("E_ADAPTER_BRIDGE", $"Managed adapter bridge '{module.ManagedAdapterBridgeId}' is not supported by this compiler", declaration.At);
            valid = false;
        }

        var adapterOperation = adapterDefinition?.Operations.SingleOrDefault(
            operation => operation.OperationId == declaration.AdapterOperation);
        if (adapterDefinition is not null &&
            (adapterOperation is null || adapterOperation.OperationId != Sha256TextHashUtf8OperationId))
        {
            Add("E_ADAPTER_OPERATION", $"Adapter operation '{declaration.AdapterOperation ?? string.Empty}' is not supported by this compiler", declaration.At);
            valid = false;
        }

        if (declaration.IsAsync || adapterOperation is { IsAsync: true })
        {
            Add("E_ADAPTER_ASYNC", "Adapter functions must be synchronous", declaration.At);
            valid = false;
        }

        if (declaration.TypeParameters.Count != 0)
        {
            Add("E_ADAPTER_GENERIC", "Adapter functions cannot declare type parameters", declaration.At);
            valid = false;
        }

        if (declaration.TypeParameters.Count == 0 && adapterOperation is not null)
        {
            var signatureTypesValid = symbol.Parameters.All(parameter => !parameter.Type.IsError) && !symbol.ReturnType.IsError;
            if (!signatureTypesValid)
            {
                valid = false;
            }
            else if (symbol.Parameters.Count != adapterOperation.ParameterTypes.Count ||
                     !symbol.Parameters.Select(parameter => parameter.Type.DisplayName)
                         .SequenceEqual(adapterOperation.ParameterTypes, StringComparer.Ordinal) ||
                     symbol.ReturnType.DisplayName != adapterOperation.ReturnType)
            {
                Add("E_ADAPTER_SIGNATURE", "The sha256.text.hash_utf8 adapter must take one Text parameter and return Text", declaration.At);
                valid = false;
            }
        }

        if (declaration.Effects.Count != 0 || adapterOperation is { Effects.Count: > 0 } ||
            adapterOperation is { RequiredCapabilities.Count: > 0 })
        {
            Add("E_ADAPTER_EFFECT", "Adapter functions must declare effects {}", declaration.At);
            valid = false;
        }

        if (declaration.Body.Count != 0)
        {
            Add("E_ADAPTER_BODY", "Adapter functions cannot contain a source body", declaration.At);
            valid = false;
        }

        if (valid)
            symbol.AdapterBinding = new CheckedManagedAdapterBinding(
                adapterDefinition!.BridgeId,
                adapterOperation!.OperationId);
    }

    private void ValidateDuplicateAdapterOperationBindings()
    {
        var adapterFunctions = _functions
            .Where(function => function.AdapterBinding is not null)
            .OrderBy(function => function.PackageId, StringComparer.Ordinal)
            .ThenBy(function => function.AdapterBinding!.OperationId, StringComparer.Ordinal)
            .ThenBy(StableFunctionOrderKey, StringComparer.Ordinal)
            .ThenBy(function => function.Declaration.At.File, StringComparer.Ordinal)
            .ThenBy(function => function.Declaration.At.Line)
            .ThenBy(function => function.Declaration.At.Column)
            .GroupBy(function => (function.PackageId, function.AdapterBinding!.OperationId));

        foreach (var declarations in adapterFunctions)
        {
            var orderedDeclarations = declarations.ToArray();
            if (orderedDeclarations.Length < 2)
                continue;

            var first = orderedDeclarations[0];
            var operationId = first.AdapterBinding!.OperationId;
            foreach (var duplicate in orderedDeclarations.Skip(1))
            {
                Add(
                    "E_ADAPTER_DUPLICATE_OPERATION",
                    $"Managed adapter operation '{operationId}' is already bound by '{FormatFunctionName(first)}' in this package",
                    duplicate.Declaration.At);
            }
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
                ReturnType = HobType.Bool,
                DeclaredEffects = []
            };

            // Synthetic functions participate in local checking and effect inference, but never
            // enter the source function maps used by name lookup and imports.
            _functions.Add(function);
            _tests.Add(new CheckedTest(test.Name, module.PackageId, module.Program.Module, id, test.At));
        }
    }

    private static bool IsReservedTypeName(string name) =>
        name is "i32" or "i64" or "u32" or "u64" or "f64" or "bool" or "Text" or "Html" or "FilePath" or "Option" or "Result" or
            "FsRead" or "FsWrite" or "Config" or "Secrets" or "Logger" or "ProcessRunner" or "Secret" or
            "FsError" or "ProcessOutput" or "ProcessError" or "DbRead" or "DbWrite" or "Transaction" or "DbError" or
            "ArithmeticError";

    private static bool IsReservedTypeParameterName(string name) =>
        IsReservedTypeName(name) || name is "Bytes" or "BytesError" or "Unit";

    private void ValidatePublicSignatures()
    {
        foreach (var function in _functions)
        {
            if (!function.Declaration.Public) continue;
            foreach (var parameter in function.Parameters)
                CheckPublicTypeVisibility(parameter.Type, function.Declaration.Name, parameter.At);
            CheckPublicTypeVisibility(function.ReturnType, function.Declaration.Name, function.Declaration.At);
            foreach (var bound in function.TypeParameterBounds.SelectMany(bounds => bounds))
            {
                var trait = _traits[bound.TraitId];
                if (!trait.Declaration.Public)
                    Add("E_TYPE_VISIBILITY", $"Public declaration '{function.Declaration.Name}' exposes private trait '{trait.Declaration.Name}'", bound.At);
            }
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

        foreach (var newtype in _newtypes)
        {
            if (!newtype.Declaration.Public) continue;
            CheckPublicTypeVisibility(
                newtype.Representation,
                newtype.Declaration.Name,
                newtype.Declaration.Representation.At);
        }

        foreach (var trait in _traits)
        {
            if (!trait.Declaration.Public) continue;
            foreach (var method in trait.Methods)
            {
                foreach (var parameter in method.Parameters)
                    CheckPublicTypeVisibility(parameter.Type, trait.Declaration.Name, parameter.At);
                CheckPublicTypeVisibility(method.ReturnType, trait.Declaration.Name, method.At);
            }
        }
    }

    private void CheckPublicTypeVisibility(HobType type, string owner, Token at)
    {
        if (type.IsError || type.Kind == HobTypeKind.TypeParameter) return;
        if (type.Kind == HobTypeKind.Union &&
            _unions[type.UnionId].Declaration.Public == false)
        {
            Add("E_TYPE_VISIBILITY", $"Public declaration '{owner}' exposes private type '{type.DisplayName}'", at);
            return;
        }
        if (type.Kind == HobTypeKind.Struct &&
            _structs[type.StructId].Declaration.Public == false)
        {
            Add("E_TYPE_VISIBILITY", $"Public declaration '{owner}' exposes private type '{type.DisplayName}'", at);
            return;
        }
        if (type.Kind == HobTypeKind.Newtype &&
            _newtypes[type.NewtypeId].Declaration.Public == false)
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

        IReadOnlyList<TypedStmt> body;
        if (function.Declaration.IsAdapter)
        {
            body = [];
        }
        else
        {
            var checkedBody = new List<TypedStmt>();
            var guaranteesReturn = CheckStatements(function.Declaration.Body, checkedBody, locals);
            if (!guaranteesReturn)
                Add("E_TYPE_MISMATCH", "Function must end with a return value", function.Declaration.At);
            body = ReadOnly(checkedBody);
        }

        function.CheckedFunction = new CheckedFunction(
            function.Id,
            function.PackageId,
            function.ModuleName,
            function.Declaration.Name,
            function.Declaration.Public,
            function.Declaration.IsAsync,
            function.Parameters,
            function.TypeParameters,
            function.TypeParameterBounds,
            function.ReturnType,
            body,
            function.Calls
                .Select(call => new CheckedDirectCall(
                    call.Target.PackageId,
                    call.Target.ModuleName,
                    call.Target.Declaration.Name)),
            function.DeclaredEffects,
            function.Declaration.At,
            function.AdapterBinding);
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
                    var condition = CheckExpr(conditional.Condition, HobType.Bool, locals, 0);
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
                    var itemType = HobType.Error;
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
            database = CheckExpr(beginCall.Target, HobType.DbWrite, locals, 0);
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
            bodyLocals.Add(transaction.Name, new LocalSymbol(transactionLocalId, HobType.Transaction));
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

    private void ValidateResourceListType(HobType type, Token at)
    {
        if (ContainsIllegalResourceList(type) &&
            _resourceListDiagnosticLocations.Add((at.File, at.Line, at.Column)))
            Add("E_RESOURCE_ESCAPE", "Lists cannot contain resource handles, directly or through nested types", at);

        if (ContainsIllegalResourceMap(type) &&
            _resourceMapDiagnosticLocations.Add((at.File, at.Line, at.Column)))
            Add("E_RESOURCE_ESCAPE", "Maps cannot contain resource handles in values, directly or through nested types", at);
    }

    private void BuildResourceGraphSummaries()
    {
        var declarationCount = _structs.Count + _newtypes.Count + _unions.Count;
        var dependents = Enumerable.Range(0, declarationCount)
            .Select(_ => new HashSet<int>())
            .ToArray();
        var reverseDependents = Enumerable.Range(0, declarationCount)
            .Select(_ => new List<int>())
            .ToArray();
        var directlyContainsResource = new bool[declarationCount];
        var listElementTypes = Enumerable.Range(0, declarationCount)
            .Select(_ => new List<HobType>())
            .ToArray();
        var mapValueTypes = Enumerable.Range(0, declarationCount)
            .Select(_ => new List<HobType>())
            .ToArray();

        foreach (var structure in _structs)
            foreach (var field in structure.Fields)
                CollectResourceGraphFacts(
                    field.Type,
                    NominalDeclarationIndex(HobTypeKind.Struct, structure.Id),
                    dependents,
                    directlyContainsResource,
                    listElementTypes,
                    mapValueTypes);

        foreach (var newtype in _newtypes)
            CollectResourceGraphFacts(
                newtype.Representation,
                NominalDeclarationIndex(HobTypeKind.Newtype, newtype.Id),
                dependents,
                directlyContainsResource,
                listElementTypes,
                mapValueTypes);

        foreach (var union in _unions)
            foreach (var variant in union.Variants)
                foreach (var field in variant.Fields)
                    CollectResourceGraphFacts(
                        field.Type,
                        NominalDeclarationIndex(HobTypeKind.Union, union.Id),
                        dependents,
                        directlyContainsResource,
                        listElementTypes,
                        mapValueTypes);

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

        var directlyContainsIllegalMap = new bool[declarationCount];
        for (var declaration = 0; declaration < declarationCount; declaration++)
        foreach (var valueType in mapValueTypes[declaration])
        {
            if (!ContainsResourceHandle(valueType, _resourceReachableDeclarations))
                continue;
            directlyContainsIllegalMap[declaration] = true;
            break;
        }

        _illegalMapReachableDeclarations = ComputeReverseReachability(
            directlyContainsIllegalMap,
            reverseDependents);
    }

    private void ValidateNewtypeResourceRepresentations()
    {
        foreach (var newtype in _newtypes)
        {
            if (newtype.Representation.IsError || !ContainsResourceHandle(newtype.Representation, _resourceReachableDeclarations))
                continue;

            Add(
                "E_RESOURCE_ESCAPE",
                $"Newtype '{newtype.Declaration.Name}' cannot store resource or capability handles",
                newtype.Declaration.Representation.At);
        }
    }

    private void CollectResourceGraphFacts(
        HobType root,
        int sourceDeclaration,
        HashSet<int>[] dependents,
        bool[] directlyContainsResource,
        List<HobType>[] listElementTypes,
        List<HobType>[] mapValueTypes)
    {
        var pending = new Stack<HobType>();
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
            if (type.IsMap)
                mapValueTypes[sourceDeclaration].Add(type.Arguments[1]);

            if (TryGetDeclarationNode(type, out var declaration))
            {
                dependents[sourceDeclaration].Add(declaration);
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

    private bool ContainsIllegalResourceList(HobType type)
    {
        var pending = new Stack<HobType>();
        var visited = new HashSet<HobType>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (current.IsList && ContainsResourceHandle(current.Arguments[0], _resourceReachableDeclarations))
                return true;

            if (current.Kind == HobTypeKind.Struct)
            {
                if (_invalidNominalRecursion.Contains((HobTypeKind.Struct, current.StructId))) continue;
                if (_illegalListReachableDeclarations[NominalDeclarationIndex(current)]) return true;
                if (!visited.Add(current)) continue;
                var structure = _structs[current.StructId];
                foreach (var field in structure.Fields)
                    pending.Push(InstantiateStructFieldType(structure, current, field.Type));
                continue;
            }

            if (current.Kind == HobTypeKind.Union)
            {
                var declaration = NominalDeclarationIndex(current);
                if (_invalidNominalRecursion.Contains((HobTypeKind.Union, current.UnionId))) continue;
                if (_illegalListReachableDeclarations[declaration]) return true;
                if (!visited.Add(current)) continue;
                var union = _unions[current.UnionId];
                foreach (var field in union.Variants.SelectMany(variant => variant.Fields))
                    pending.Push(InstantiateUnionFieldType(union, current, field.Type));
                continue;
            }

            if (current.Kind == HobTypeKind.Newtype)
            {
                var declaration = NominalDeclarationIndex(current);
                if (_invalidNominalRecursion.Contains((HobTypeKind.Newtype, current.NewtypeId))) continue;
                if (_illegalListReachableDeclarations[declaration]) return true;
                if (!visited.Add(current)) continue;
                pending.Push(_newtypes[current.NewtypeId].Representation);
                continue;
            }

            foreach (var argument in current.Arguments)
                pending.Push(argument);
        }

        return false;
    }

    private bool ContainsIllegalResourceMap(HobType type)
    {
        var pending = new Stack<HobType>();
        var visited = new HashSet<HobType>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (current.IsMap && ContainsResourceHandle(current.Arguments[1], _resourceReachableDeclarations))
                return true;

            if (current.Kind == HobTypeKind.Struct)
            {
                if (_invalidNominalRecursion.Contains((HobTypeKind.Struct, current.StructId))) continue;
                if (_illegalMapReachableDeclarations[NominalDeclarationIndex(current)]) return true;
                if (!visited.Add(current)) continue;
                var structure = _structs[current.StructId];
                foreach (var field in structure.Fields)
                    pending.Push(InstantiateStructFieldType(structure, current, field.Type));
                continue;
            }

            if (current.Kind == HobTypeKind.Union)
            {
                var declaration = NominalDeclarationIndex(current);
                if (_invalidNominalRecursion.Contains((HobTypeKind.Union, current.UnionId))) continue;
                if (_illegalMapReachableDeclarations[declaration]) return true;
                if (!visited.Add(current)) continue;
                var union = _unions[current.UnionId];
                foreach (var field in union.Variants.SelectMany(variant => variant.Fields))
                    pending.Push(InstantiateUnionFieldType(union, current, field.Type));
                continue;
            }

            if (current.Kind == HobTypeKind.Newtype)
            {
                var declaration = NominalDeclarationIndex(current);
                if (_invalidNominalRecursion.Contains((HobTypeKind.Newtype, current.NewtypeId))) continue;
                if (_illegalMapReachableDeclarations[declaration]) return true;
                if (!visited.Add(current)) continue;
                pending.Push(_newtypes[current.NewtypeId].Representation);
                continue;
            }

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
            case TypedLambdaInvokeExpr lambda:
                ValidateResourceListType(lambda.ParameterType, lambda.At);
                ValidateResourceListExpression(lambda.Argument);
                ValidateResourceListExpression(lambda.Body);
                break;
            case TypedListExpr list:
                foreach (var item in list.Items)
                    ValidateResourceListExpression(item);
                break;
            case TypedBinaryExpr binary:
                ValidateResourceListExpression(binary.Left);
                ValidateResourceListExpression(binary.Right);
                break;
            case TypedIntegerArithmeticExpr arithmetic:
                ValidateResourceListExpression(arithmetic.Receiver);
                ValidateResourceListExpression(arithmetic.Right);
                break;
            case TypedUnaryExpr unary:
                ValidateResourceListExpression(unary.Operand);
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
            case TypedTraitCallExpr call:
                foreach (var argument in call.Arguments)
                    ValidateResourceListExpression(argument);
                break;
            case TypedAwaitExpr awaited:
                ValidateResourceListExpression(awaited.Value);
                break;
            case TypedResultPropagateExpr propagated:
                ValidateResourceListExpression(propagated.Operand);
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
            case TypedBytesEmptyExpr:
            case TypedUnitExpr:
                break;
            case TypedBytesLengthExpr length:
                ValidateResourceListExpression(length.Target);
                break;
            case TypedBytesGetExpr get:
                ValidateResourceListExpression(get.Target);
                ValidateResourceListExpression(get.Index);
                break;
            case TypedBytesAppendExpr append:
                ValidateResourceListExpression(append.Target);
                ValidateResourceListExpression(append.Octet);
                break;
            case TypedMapEmptyExpr:
                break;
            case TypedMapSetExpr set:
                ValidateResourceListExpression(set.Target);
                ValidateResourceListExpression(set.Key);
                ValidateResourceListExpression(set.Value);
                break;
            case TypedMapGetExpr get:
                ValidateResourceListExpression(get.Target);
                ValidateResourceListExpression(get.Key);
                break;
            case TypedMapKeysExpr keys:
                ValidateResourceListExpression(keys.Target);
                break;
            case TypedMapLengthExpr length:
                ValidateResourceListExpression(length.Target);
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
            case TypedNewtypeConstructExpr constructedNewtype:
                ValidateResourceListExpression(constructedNewtype.Value);
                break;
            case TypedNewtypeProjectExpr projectedNewtype:
                ValidateResourceListExpression(projectedNewtype.Target);
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
        HobType? expected,
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
            NumberExpr number => CheckNumber(number),
            UnitExpr unit => new TypedUnitExpr(unit.At),
            UnaryExpr unary => CheckUnary(unary, locals, depth + 1),
            BoolExpr boolean => new TypedBoolExpr(boolean.At, boolean.Value),
            TextExpr text => new TypedTextExpr(text.At, text.Value),
            ListExpr list => CheckListLiteral(list, expected, locals, depth + 1),
            NameExpr name => CheckName(name, expected, locals),
            LambdaExpr lambda => UnsupportedLambda(lambda),
            LambdaInvokeExpr invocation => CheckLambdaInvoke(invocation, locals, depth + 1),
            DeclarationRefExpr reference => CheckDeclarationReference(reference),
            BinaryExpr binary => CheckBinary(binary, locals, depth + 1),
            CallExpr call => CheckCall(call, expected, locals, depth + 1, isAwaitOperand),
            MemberCallExpr call => CheckMemberCall(call, expected, locals, depth + 1, isAwaitOperand),
            AwaitExpr awaited => CheckAwait(awaited, locals, depth + 1),
            ResultPropagateExpr propagated => CheckResultPropagate(propagated, locals, depth + 1),
            StructConstructExpr structure => CheckStructConstruction(structure, locals, depth + 1),
            UnionConstructExpr union => CheckUnionConstruction(union, locals, depth + 1),
            QualifiedTypeMemberCallExpr member => CheckQualifiedTypeMemberCall(member, expected, locals, depth + 1),
            FieldAccessExpr access => CheckFieldAccess(access, locals, depth + 1),
            MatchExpr match => CheckMatch(match, expected, locals, depth + 1),
            _ => UnsupportedExpr(expression)
        };

        if (expected is not null && !expected.IsError && !result.Type.IsError && result.Type != expected)
            AddMismatch(expected, result.Type, expression.At);
        return result;
    }

    private static TypedExpr CheckNumber(NumberExpr expression)
    {
        var type = expression.LiteralKind switch
        {
            NumericLiteralKind.I32 => HobType.I32,
            NumericLiteralKind.I64 => HobType.I64,
            NumericLiteralKind.U32 => HobType.U32,
            NumericLiteralKind.U64 => HobType.U64,
            NumericLiteralKind.F64 => HobType.F64,
            _ => throw new InvalidOperationException("Unknown numeric literal kind")
        };
        return new TypedNumberExpr(type, expression.At, expression.Value);
    }

    private TypedExpr CheckUnary(UnaryExpr expression, Dictionary<string, LocalSymbol> locals, int depth)
    {
        if (expression.Op != "-")
        {
            Add("E_UNSUPPORTED", $"Unary operator '{expression.Op}' is not implemented", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var operand = CheckExpr(expression.Operand, null, locals, depth);
        if (operand.Type.IsError)
            return new TypedErrorExpr(expression.At);

        if (operand.Type.IsU32 || operand.Type.IsU64)
        {
            Add("E_TYPE_MISMATCH", $"Unary '-' is not supported for '{operand.Type.DisplayName}'", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (operand.Type.IsF64)
            return new TypedUnaryExpr(operand.Type, "-", operand, expression.At);

        if (!operand.Type.IsI32 && !operand.Type.IsI64)
        {
            Add("E_TYPE_MISMATCH", "Arithmetic '-' requires i32 operands", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var zero = new TypedNumberExpr(operand.Type, expression.At, "0");
        return new TypedBinaryExpr(operand.Type, "-", zero, operand, expression.At);
    }

    private TypedExpr UnsupportedLambda(LambdaExpr expression)
    {
        Add("E_UNSUPPORTED", "A lambda expression must be immediately invoked", expression.At);
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckLambdaInvoke(
        LambdaInvokeExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (_activeLambdaCaptureLocalIds is not null)
        {
            Add("E_UNSUPPORTED", "Nested lambda expressions are not implemented", expression.At);
            _ = CheckExpr(expression.Argument, null, locals, depth);
            return new TypedErrorExpr(expression.At);
        }

        var parameterType = ResolveType(
            expression.Lambda.ParameterType,
            0,
            _currentFunction?.TypeParametersByName);
        var argument = CheckExpr(expression.Argument, parameterType, locals, depth);
        if (parameterType.IsError || argument.Type.IsError)
            return new TypedErrorExpr(expression.At);
        if (ContainsTypeParameter(parameterType))
        {
            Add("E_UNSUPPORTED", "Lambda parameter types cannot contain unconstrained type parameters", expression.Lambda.ParameterAt);
            return new TypedErrorExpr(expression.At);
        }
        if (ContainsResourceHandle(parameterType))
        {
            Add("E_RESOURCE_ESCAPE", "Lambda parameters cannot contain resource or capability handles", expression.Lambda.ParameterAt);
            return new TypedErrorExpr(expression.At);
        }

        var parameterLocalId = _nextLocalId++;
        var lambdaLocals = new Dictionary<string, LocalSymbol>(locals, StringComparer.Ordinal)
        {
            [expression.Lambda.ParameterName] = new LocalSymbol(parameterLocalId, parameterType)
        };
        _activeLambdaCaptureLocalIds = locals.Values.Select(local => local.Id).ToHashSet();
        TypedExpr body;
        try
        {
            body = CheckExpr(expression.Lambda.Body, null, lambdaLocals, depth);
        }
        finally
        {
            _activeLambdaCaptureLocalIds = null;
        }

        if (ContainsResourceHandle(body.Type))
        {
            Add("E_RESOURCE_ESCAPE", "Lambda results cannot contain resource or capability handles", expression.Lambda.Body.At);
            return new TypedErrorExpr(expression.At);
        }
        if (ContainsTypeParameter(body.Type))
        {
            Add("E_UNSUPPORTED", "Lambda results cannot contain unconstrained type parameters", expression.Lambda.Body.At);
            return new TypedErrorExpr(expression.At);
        }

        return new TypedLambdaInvokeExpr(
            body.Type,
            parameterType,
            parameterLocalId,
            argument,
            body,
            expression.At);
    }

    private TypedExpr CheckAwait(
        AwaitExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (_activeLambdaCaptureLocalIds is not null)
        {
            Add("E_UNSUPPORTED", "Lambda bodies cannot contain await expressions", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (_currentFunction?.Declaration.IsAsync != true)
            Add("E_AWAIT_CONTEXT", "The 'await' expression is only valid inside an async function", expression.At);

        var diagnosticsBeforeOperand = diagnostics.Count;
        var value = CheckExpr(expression.Value, null, locals, depth, isAwaitOperand: true);
        if (value is TypedCallExpr { IsAsync: true } or
            TypedIntrinsicCallExpr { Intrinsic: BuiltinIntrinsic.FsReadTextAsync or BuiltinIntrinsic.FsWriteTextAsync or
                BuiltinIntrinsic.HttpGetTextAsync or BuiltinIntrinsic.ProcessRunTextAsync })
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

    private TypedExpr CheckResultPropagate(
        ResultPropagateExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var operand = CheckExpr(expression.Operand, null, locals, depth);
        if (_currentFunction is null)
        {
            Add(
                "E_RESULT_PROPAGATION_CONTEXT",
                "The '?' operator is only allowed in an ordinary function body",
                expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (_activeLambdaCaptureLocalIds is not null || _currentFunction.TestName is not null)
        {
            Add(
                "E_RESULT_PROPAGATION_CONTEXT",
                "The '?' operator is not allowed in lambda or test bodies",
                expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (operand.Type.IsError)
            return new TypedErrorExpr(expression.At);
        if (operand.Type.Kind != HobTypeKind.Result)
        {
            Add(
                "E_TYPE_MISMATCH",
                $"The '?' operator requires a Result<T, E> operand, found '{operand.Type.DisplayName}'",
                expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var functionReturnType = _currentFunction.ReturnType;
        if (functionReturnType.Kind != HobTypeKind.Result)
        {
            Add(
                "E_TYPE_MISMATCH",
                "The '?' operator requires the current function to return Result<T, E>",
                expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var errorType = operand.Type.Arguments[1];
        var functionErrorType = functionReturnType.Arguments[1];
        if (errorType != functionErrorType)
        {
            AddMismatch(functionErrorType, errorType, expression.At);
            return new TypedErrorExpr(expression.At);
        }

        return new TypedResultPropagateExpr(operand.Type.Arguments[0], errorType, operand, expression.At);
    }

    private TypedExpr CheckListLiteral(
        ListExpr expression,
        HobType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var expectedItemType = expected is not null && expected.IsList ? expected.Arguments[0] : null;
        if (expression.Items.Count == 0)
        {
            if (expectedItemType is not null)
                return new TypedListExpr(HobType.List(expectedItemType), [], expression.At);
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

        return new TypedListExpr(HobType.List(itemType), ReadOnly(values), expression.At);
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

    private FunctionSymbol? ResolveExplicitLocalFunctionReference(SourceDeclarationRefSyntax reference)
    {
        if (CurrentModule.DeclaredFunctions.TryGetValue(reference.Declaration, out var function))
            return function;

        Add(
            "E_NAME_UNRESOLVED",
            $"Module '{CurrentModule.Program.Module}' does not declare function '{reference.Declaration}'",
            reference.At);
        return null;
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

    private TraitSymbol? ResolveTraitReference(SourceDeclarationRefSyntax reference)
    {
        if (!TryResolveTargetModule(reference, out var target)) return null;
        if (!target!.DeclaredTraits.TryGetValue(reference.Declaration, out var trait))
        {
            Add("E_NAME_UNRESOLVED", $"Module '{target.Program.Module}' does not declare trait '{reference.Declaration}'", reference.At);
            return null;
        }
        if (target.Identity != CurrentModule.Identity && !trait.Declaration.Public)
        {
            Add("E_ACCESS_PRIVATE", $"Trait '{FormatReference(reference)}' is private", reference.At);
            return null;
        }
        return trait;
    }

    private (UnionSymbol? Union, StructSymbol? Struct, NewtypeSymbol? Newtype) ResolveTypeDeclaration(SourceDeclarationRefSyntax reference)
    {
        if (!TryResolveTargetModule(reference, out var target)) return (null, null, null);
        if (target!.DeclaredUnions.TryGetValue(reference.Declaration, out var union))
        {
            if (target.Identity != CurrentModule.Identity && !union.Declaration.Public)
            {
                Add("E_ACCESS_PRIVATE", $"Union '{FormatReference(reference)}' is private", reference.At);
                return (null, null, null);
            }
            return (union, null, null);
        }
        if (target.DeclaredStructs.TryGetValue(reference.Declaration, out var structure))
        {
            if (target.Identity != CurrentModule.Identity && !structure.Declaration.Public)
            {
                Add("E_ACCESS_PRIVATE", $"Struct '{FormatReference(reference)}' is private", reference.At);
                return (null, null, null);
            }
            return (null, structure, null);
        }
        if (target.DeclaredNewtypes.TryGetValue(reference.Declaration, out var newtype))
        {
            if (target.Identity != CurrentModule.Identity && !newtype.Declaration.Public)
            {
                Add("E_ACCESS_PRIVATE", $"Newtype '{FormatReference(reference)}' is private", reference.At);
                return (null, null, null);
            }
            return (null, null, newtype);
        }

        Add("E_NAME_UNRESOLVED", $"Module '{target.Program.Module}' does not declare type '{reference.Declaration}'", reference.At);
        return (null, null, null);
    }

    private TypedExpr CheckName(NameExpr expression, HobType? expected, Dictionary<string, LocalSymbol> locals)
    {
        if (locals.TryGetValue(expression.Name, out var local))
        {
            if (local.Type.IsTransaction)
            {
                Add("E_RESOURCE_ESCAPE", $"Transaction '{expression.Name}' may only be used as the direct receiver of execute() or commit() inside its with scope", expression.At);
                return new TypedErrorExpr(expression.At);
            }
            if (_activeLambdaCaptureLocalIds?.Contains(local.Id) == true)
            {
                if (local.IsMutable)
                    Add("E_CLOSURE_CAPTURE_MUTABLE", $"Lambda cannot capture rebindable local '{expression.Name}'", expression.At);
                if (ContainsTypeParameter(local.Type))
                {
                    Add("E_UNSUPPORTED", "Lambda cannot capture an unconstrained type parameter", expression.At);
                    return new TypedErrorExpr(expression.At);
                }
                if (ContainsResourceHandle(local.Type))
                    Add("E_RESOURCE_ESCAPE", $"Lambda cannot capture resource or capability local '{expression.Name}'", expression.At);
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
            if (expected.Kind != HobTypeKind.Option)
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
        var reference = expression.Type.Reference;
        if (!reference.IsQualified && reference.Declaration == "ProcessOutput")
        {
            foreach (var field in expression.Fields)
                _ = CheckExpr(field.Value, null, locals, depth);

            if (!CheckProcessTypeScope("ProcessOutput", expression.At))
                return new TypedErrorExpr(expression.At);

            Add("E_TYPE_MISMATCH", "ProcessOutput values can only be produced by process operations", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var (union, structure, newtype) = ResolveTypeDeclaration(reference);
        if (structure is null)
        {
            foreach (var value in expression.Fields)
                _ = CheckExpr(value.Value, null, locals, depth);

            if (union is not null)
                Add("E_TYPE_MISMATCH", $"Type '{FormatReference(reference)}' is a union, not a struct", expression.At);
            else if (newtype is not null)
                Add("E_TYPE_MISMATCH", $"Type '{FormatReference(reference)}' is a newtype, not a struct", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var instantiatedType = ResolveType(expression.Type, 0, _currentFunction?.TypeParametersByName);

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

            var fieldType = instantiatedType.IsError
                ? HobType.Error
                : InstantiateStructFieldType(structure, instantiatedType, field.Type);
            var value = CheckExpr(initializer.Value, fieldType.IsError ? null : fieldType, locals, depth);
            if (!supplied.Add(field.Index))
                Add("E_FIELD_DUPLICATE", $"Field '{initializer.Name}' is initialized more than once", initializer.At);
            values.Add(new TypedStructFieldValue(field.Index, value));
        }

        foreach (var field in structure.Fields)
        {
            if (!supplied.Contains(field.Index))
                Add("E_FIELD_MISSING", $"Field '{field.Name}' is missing from construction of '{structure.Declaration.Name}'", expression.At);
        }

        return instantiatedType.IsError
            ? new TypedErrorExpr(expression.At)
            : new TypedStructConstructExpr(instantiatedType, structure.Id, ReadOnly(values), expression.At);
    }

    private TypedExpr CheckQualifiedTypeMemberCall(
        QualifiedTypeMemberCallExpr expression,
        HobType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (!TryResolveTargetModule(expression.Owner, out var module))
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            return new TypedErrorExpr(expression.At);
        }

        if (module!.DeclaredUnions.TryGetValue(expression.Owner.Declaration, out var union))
        {
            if (module.Identity != CurrentModule.Identity && !union.Declaration.Public)
            {
                Add("E_ACCESS_PRIVATE", $"Union '{FormatReference(expression.Owner)}' is private", expression.Owner.At);
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                return new TypedErrorExpr(expression.At);
            }
            return CheckUnionConstruction(
                new UnionConstructExpr(
                    expression.At,
                    new TypeSyntax(expression.Owner, expression.TypeArguments, expression.Owner.At),
                    expression.Member,
                    expression.MemberAt,
                    expression.Arguments),
                locals,
                depth);
        }

        if (module.DeclaredTraits.TryGetValue(expression.Owner.Declaration, out var trait))
        {
            if (module.Identity != CurrentModule.Identity && !trait.Declaration.Public)
            {
                Add("E_ACCESS_PRIVATE", $"Trait '{FormatReference(expression.Owner)}' is private", expression.Owner.At);
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                return new TypedErrorExpr(expression.At);
            }
            if (expression.TypeArguments.Count != 0)
            {
                Add("E_TYPE_MISMATCH", $"Trait '{trait.Declaration.Name}' does not take type arguments", expression.Owner.At);
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                return new TypedErrorExpr(expression.At);
            }
            return CheckTraitOperation(trait, expression.Member, expression.Arguments, expression.At, expression.MemberAt, expected, locals, depth);
        }

        if (module.DeclaredStructs.TryGetValue(expression.Owner.Declaration, out var structure))
        {
            if (module.Identity != CurrentModule.Identity && !structure.Declaration.Public)
                Add("E_ACCESS_PRIVATE", $"Struct '{FormatReference(expression.Owner)}' is private", expression.Owner.At);
            else
                Add("E_TYPE_MISMATCH", $"Struct '{structure.Declaration.Name}' has no static member '{expression.Member}'", expression.MemberAt);
        }
        else if (module.DeclaredNewtypes.TryGetValue(expression.Owner.Declaration, out var newtype))
        {
            if (module.Identity != CurrentModule.Identity && !newtype.Declaration.Public)
            {
                Add("E_ACCESS_PRIVATE", $"Newtype '{FormatReference(expression.Owner)}' is private", expression.Owner.At);
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                return new TypedErrorExpr(expression.At);
            }

            if (expression.TypeArguments.Count != 0)
            {
                Add("E_TYPE_MISMATCH", $"Newtype '{newtype.Declaration.Name}' does not take type arguments", expression.Owner.At);
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                return new TypedErrorExpr(expression.At);
            }

            if (expression.Member != "wrap")
            {
                Add("E_TYPE_MISMATCH", $"Newtype '{newtype.Declaration.Name}' has no static member '{expression.Member}'", expression.MemberAt);
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                return new TypedErrorExpr(expression.At);
            }

            if (expression.Arguments.Count != 1)
                Add("E_TYPE_MISMATCH", $"Newtype '{newtype.Declaration.Name}.wrap' expects 1 argument, got {expression.Arguments.Count}", expression.MemberAt);

            var values = new List<TypedExpr>(expression.Arguments.Count);
            for (var index = 0; index < expression.Arguments.Count; index++)
                values.Add(CheckExpr(
                    expression.Arguments[index],
                    index == 0 ? newtype.Representation : null,
                    locals,
                    depth));

            return expression.Arguments.Count == 1
                ? new TypedNewtypeConstructExpr(newtype.Type, newtype.Id, values[0], expression.At)
                : new TypedErrorExpr(expression.At);
        }
        else
        {
            Add("E_NAME_UNRESOLVED", $"Module '{module.Program.Module}' does not declare union, struct, newtype, or trait '{expression.Owner.Declaration}'", expression.Owner.At);
        }
        foreach (var argument in expression.Arguments)
            _ = CheckExpr(argument, null, locals, depth);
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckTraitOperation(
        TraitSymbol trait,
        string methodName,
        IReadOnlyList<Expr> argumentSyntax,
        Token at,
        Token memberAt,
        HobType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var method = trait.Methods.FirstOrDefault(candidate => candidate.Name == methodName);
        if (method is null)
        {
            foreach (var argument in argumentSyntax)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_NAME_UNRESOLVED", $"Trait '{trait.Declaration.Name}' has no method '{methodName}'", memberAt);
            return new TypedErrorExpr(at);
        }
        if (argumentSyntax.Count != method.Parameters.Count)
            Add("E_TYPE_MISMATCH", $"Trait method '{trait.Declaration.Name}.{methodName}' expects {method.Parameters.Count} arguments, got {argumentSyntax.Count}", memberAt);

        var arguments = new List<TypedExpr>(argumentSyntax.Count);
        for (var index = 0; index < argumentSyntax.Count; index++)
            arguments.Add(CheckExpr(argumentSyntax[index], null, locals, depth));

        var selfBindings = new Dictionary<HobType, HobType>();
        for (var index = 0; index < Math.Min(arguments.Count, method.Parameters.Count); index++)
        {
            var formal = method.Parameters[index].Type;
            var actual = arguments[index].Type;
            if (!TryUnifyType(formal, actual, selfBindings) && !formal.IsError && !actual.IsError)
                AddMismatch(SubstituteType(formal, selfBindings, preserveUnboundTypeParameters: true), actual, argumentSyntax[index].At);
        }
        if (!selfBindings.TryGetValue(trait.SelfType, out var selfType))
        {
            Add("E_TYPE_MISMATCH", $"Trait method '{trait.Declaration.Name}.{methodName}' cannot infer Self from its arguments", memberAt);
            return new TypedErrorExpr(at);
        }

        var witness = ResolveTraitWitness(trait, selfType, memberAt);
        if (witness is null)
            return new TypedErrorExpr(at);
        var returnType = SubstituteType(method.ReturnType, selfBindings, preserveUnboundTypeParameters: true);
        var typed = new TypedTraitCallExpr(returnType, trait.Id, method.Id, witness, ReadOnly(arguments), at);
        if (witness is TypedConcreteTraitWitness concrete && _currentFunction is not null)
        {
            var impl = _traitImpls[concrete.ImplId];
            var bindingId = impl.BindingFunctionIds[method.Id];
            _currentFunction.Calls.Add(new FunctionCallSite(_functions[bindingId], at));
        }
        return typed;
    }

    private TypedTraitWitness? ResolveTraitWitness(TraitSymbol trait, HobType target, Token at)
    {
        if (target.IsError) return null;
        if (target.Kind == HobTypeKind.TypeParameter)
        {
            if (target.TypeParameterOwnerKind == TypeParameterOwnerKind.Function &&
                _currentFunction is not null && target.TypeParameterOwnerId == _currentFunction.Id &&
                target.TypeParameterOrdinal >= 0 && target.TypeParameterOrdinal < _currentFunction.TypeParameterBounds.Count)
            {
                var bounds = _currentFunction.TypeParameterBounds[target.TypeParameterOrdinal];
                var boundOrdinal = bounds.ToList().FindIndex(bound => bound.TraitId == trait.Id);
                if (boundOrdinal >= 0)
                    return new TypedForwardedTraitWitness(target.TypeParameterOrdinal, boundOrdinal);
            }
            Add("E_TRAIT_IMPL_MISSING", $"Type '{target.DisplayName}' has no bound or visible implementation for trait '{trait.Declaration.Name}'", at);
            return null;
        }
        if (ContainsResourceHandle(target))
        {
            Add("E_RESOURCE_ESCAPE", $"Trait '{trait.Declaration.Name}' cannot be dispatched for a value that stores resource or capability handles", at);
            return null;
        }

        var matching = _traitImplSymbols
            .Where(implementation => implementation.Checked.TraitId == trait.Id && implementation.Checked.Target == target)
            .OrderBy(implementation => _packageDisplayLabels[implementation.Checked.PackageId], StringComparer.Ordinal)
            .ThenBy(implementation => implementation.Checked.Module, StringComparer.Ordinal)
            .ThenBy(implementation => implementation.Checked.StableId, StringComparer.Ordinal)
            .ToArray();
        var visible = matching.Where(implementation =>
            implementation.Checked.PackageId == CurrentModule.PackageId ||
            implementation.Checked.Public && PackageCanSee(CurrentModule.PackageId, implementation.Checked.PackageId)).ToArray();
        if (visible.Length > 1)
        {
            return null;
        }
        if (visible.Length == 1)
            return new TypedConcreteTraitWitness(visible[0].Checked.Id);
        if (matching.Length != 0)
        {
            Add("E_TRAIT_IMPL_INACCESSIBLE", $"An implementation of trait '{trait.Declaration.Name}' for type '{target.DisplayName}' exists but is not public to this package", at);
            return null;
        }
        Add("E_TRAIT_IMPL_MISSING", $"No visible implementation of trait '{trait.Declaration.Name}' exists for type '{target.DisplayName}'", at);
        return null;
    }

    private bool PackageCanSee(string fromPackage, string targetPackage)
    {
        var pending = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { fromPackage };
        pending.Enqueue(fromPackage);
        while (pending.TryDequeue(out var package))
        {
            if (package == targetPackage) return true;
            foreach (var module in _modulesByIdentity.Values.Where(candidate => candidate.PackageId == package))
                foreach (var dependency in module.DirectDependencies.Values)
                    if (visited.Add(dependency)) pending.Enqueue(dependency);
        }
        return false;
    }

    private void ValidateTraitImplCoherence()
    {
        var packageIds = _modulesByIdentity.Values.Select(module => module.PackageId)
            .Distinct(StringComparer.Ordinal).ToArray();
        var ordered = _traitImplSymbols.OrderBy(implementation => _packageDisplayLabels[implementation.Checked.PackageId], StringComparer.Ordinal)
            .ThenBy(implementation => implementation.Checked.Module, StringComparer.Ordinal)
            .ThenBy(implementation => implementation.Checked.StableId, StringComparer.Ordinal)
            .ToArray();
        for (var leftIndex = 0; leftIndex < ordered.Length; leftIndex++)
            for (var rightIndex = leftIndex + 1; rightIndex < ordered.Length; rightIndex++)
            {
                var left = ordered[leftIndex].Checked;
                var right = ordered[rightIndex].Checked;
                if (left.TraitId != right.TraitId || left.Target != right.Target) continue;
                var conflictVisible = packageIds.Any(package =>
                    ImplVisibleFrom(left, package) && ImplVisibleFrom(right, package));
                if (conflictVisible)
                    Add("E_TRAIT_IMPL_DUPLICATE", $"Trait '{_traits[left.TraitId].Declaration.Name}' has duplicate implementations for type '{left.Target.DisplayName}' in the visible package graph", right.At);
            }
    }

    private bool ImplVisibleFrom(CheckedTraitImpl implementation, string packageId) =>
        implementation.PackageId == packageId ||
        implementation.Public && PackageCanSee(packageId, implementation.PackageId);

    private void ValidateTraitConstraintRecursion()
    {
        if (_traitImplSymbols.Count == 0) return;
        var functionById = _functions.ToDictionary(function => function.Id);
        var obligations = _traitImplSymbols.ToDictionary(
            implementation => implementation.Checked.Id,
            _ => new HashSet<int>());

        foreach (var implementation in _traitImplSymbols)
            foreach (var bindingFunction in implementation.BindingFunctions)
            {
                var pending = new Stack<(FunctionSymbol Function, IReadOnlyDictionary<(int Parameter, int Bound), int?> Witnesses)>();
                pending.Push((bindingFunction, new Dictionary<(int, int), int?>()));
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (pending.TryPop(out var state))
                {
                    var stateKey = TraitTraversalStateKey(state.Function.Id, state.Witnesses);
                    if (!visited.Add(stateKey)) continue;

                    foreach (var expression in TypedExpressions(state.Function.CheckedFunction?.Body ?? []))
                    {
                        if (expression is TypedTraitCallExpr traitCall)
                        {
                            var selectedImpl = ResolveWitnessImplId(traitCall.Witness, state.Witnesses);
                            if (selectedImpl is int concrete && obligations.ContainsKey(concrete))
                                obligations[implementation.Checked.Id].Add(concrete);
                        }
                        if (expression is not TypedCallExpr call || !functionById.TryGetValue(call.FunctionId, out var target))
                            continue;

                        var targetWitnesses = new Dictionary<(int, int), int?>();
                        var flattenedBounds = target.TypeParameterBounds
                            .SelectMany((bounds, parameter) => bounds.Select((bound, ordinal) => (parameter, ordinal)))
                            .ToArray();
                        for (var index = 0; index < Math.Min(flattenedBounds.Length, call.TraitWitnesses.Count); index++)
                        {
                            var (parameter, ordinal) = flattenedBounds[index];
                            targetWitnesses[(parameter, ordinal)] = ResolveWitnessImplId(call.TraitWitnesses[index], state.Witnesses);
                        }
                        pending.Push((target, targetWitnesses));
                    }
                }
            }

        foreach (var implementation in _traitImplSymbols)
        {
            var start = implementation.Checked.Id;
            var pending = new Stack<int>(obligations[start]);
            var visited = new HashSet<int>();
            var recursive = false;
            while (pending.TryPop(out var current))
            {
                if (current == start)
                {
                    recursive = true;
                    break;
                }
                if (!visited.Add(current) || !obligations.TryGetValue(current, out var next)) continue;
                foreach (var candidate in next) pending.Push(candidate);
            }
            if (recursive)
                Add("E_TRAIT_CONSTRAINT_RECURSIVE", "Trait implementation dispatch forms a recursive constraint cycle", implementation.Checked.At);
        }
    }

    private static int? ResolveWitnessImplId(
        TypedTraitWitness witness,
        IReadOnlyDictionary<(int Parameter, int Bound), int?> environment) => witness switch
        {
            TypedConcreteTraitWitness concrete => concrete.ImplId,
            TypedForwardedTraitWitness forwarded when environment.TryGetValue((forwarded.TypeParameterOrdinal, forwarded.BoundOrdinal), out var implId) => implId,
            _ => null
        };

    private static string TraitTraversalStateKey(
        int functionId,
        IReadOnlyDictionary<(int Parameter, int Bound), int?> environment) =>
        functionId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
        string.Join(",", environment.OrderBy(item => item.Key.Parameter).ThenBy(item => item.Key.Bound)
            .Select(item => $"{item.Key.Parameter}:{item.Key.Bound}={item.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}"));

    private static IEnumerable<TypedExpr> TypedExpressions(IEnumerable<TypedStmt> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case TypedLetStmt let:
                    foreach (var expression in TypedExpressions(let.Value)) yield return expression;
                    break;
                case TypedAssignStmt assignment:
                    foreach (var expression in TypedExpressions(assignment.Value)) yield return expression;
                    break;
                case TypedReturnStmt returned:
                    foreach (var expression in TypedExpressions(returned.Value)) yield return expression;
                    break;
                case TypedIfStmt conditional:
                    foreach (var expression in TypedExpressions(conditional.Condition)) yield return expression;
                    foreach (var expression in TypedExpressions(conditional.ThenBody)) yield return expression;
                    if (conditional.ElseBody is not null)
                        foreach (var expression in TypedExpressions(conditional.ElseBody)) yield return expression;
                    break;
                case TypedForStmt loop:
                    foreach (var expression in TypedExpressions(loop.Collection)) yield return expression;
                    foreach (var expression in TypedExpressions(loop.Body)) yield return expression;
                    break;
                case TypedWithTransactionStmt transaction:
                    foreach (var expression in TypedExpressions(transaction.Database)) yield return expression;
                    foreach (var expression in TypedExpressions(transaction.Body)) yield return expression;
                    break;
            }
        }
    }

    private static IEnumerable<TypedExpr> TypedExpressions(TypedExpr expression)
    {
        yield return expression;
        IEnumerable<TypedExpr> children = expression switch
        {
            TypedLambdaInvokeExpr lambda => [lambda.Argument, lambda.Body],
            TypedListExpr list => list.Items,
            TypedMapSetExpr map => [map.Target, map.Key, map.Value],
            TypedMapGetExpr map => [map.Target, map.Key],
            TypedMapKeysExpr map => [map.Target],
            TypedMapLengthExpr map => [map.Target],
            TypedBinaryExpr binary => [binary.Left, binary.Right],
            TypedIntegerArithmeticExpr arithmetic => [arithmetic.Receiver, arithmetic.Right],
            TypedUnaryExpr unary => [unary.Operand],
            TypedCompareExpr comparison => [comparison.Left, comparison.Right],
            TypedCallExpr call => call.Arguments,
            TypedTraitCallExpr call => call.Arguments,
            TypedAwaitExpr awaited => [awaited.Value],
            TypedResultPropagateExpr propagated => [propagated.Operand],
            TypedDatabaseCallExpr database => [database.Receiver, database.Parameters],
            TypedTextLengthExpr length => [length.Target],
            TypedTextTrimExpr trim => [trim.Target],
            TypedListLengthExpr length => [length.Target],
            TypedListGetExpr get => [get.Target, get.Index],
            TypedListAppendExpr append => [append.Target, append.Value],
            TypedBytesEmptyExpr => [],
            TypedUnitExpr => [],
            TypedBytesLengthExpr length => [length.Target],
            TypedBytesGetExpr get => [get.Target, get.Index],
            TypedBytesAppendExpr append => [append.Target, append.Octet],
            TypedIntrinsicCallExpr intrinsic => intrinsic.Arguments,
            TypedBuiltinConstructExpr builtin => builtin.Arguments,
            TypedUnionConstructExpr union => union.Arguments,
            TypedStructConstructExpr structure => structure.Fields.Select(field => field.Value),
            TypedNewtypeConstructExpr constructedNewtype => [constructedNewtype.Value],
            TypedNewtypeProjectExpr projectedNewtype => [projectedNewtype.Target],
            TypedFieldAccessExpr field => [field.Target],
            TypedMatchExpr match => new[] { match.Value }.Concat(match.Arms.SelectMany(arm => TypedExpressions(arm.Body))),
            _ => []
        };
        foreach (var child in children)
        {
            if (expression is TypedMatchExpr && child != ((TypedMatchExpr)expression).Value)
            {
                yield return child;
                continue;
            }
            foreach (var nested in TypedExpressions(child)) yield return nested;
        }
    }

    private TypedExpr CheckUnionConstruction(
        UnionConstructExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var instantiatedType = ResolveType(expression.UnionType, 0, _currentFunction?.TypeParametersByName);
        if (instantiatedType.IsError)
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            return new TypedErrorExpr(expression.At);
        }

        if (instantiatedType.Kind != HobTypeKind.Union)
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_TYPE_MISMATCH", $"Type '{instantiatedType.DisplayName}' is not a tagged union", expression.UnionType.At);
            return new TypedErrorExpr(expression.At);
        }

        var union = _unions[instantiatedType.UnionId];
        var variant = union.Variants.FirstOrDefault(item => item.Name == expression.VariantName);
        if (variant is null)
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_NAME_UNRESOLVED", $"Variant '{expression.VariantName}' is not declared on union '{union.Declaration.Name}'", expression.VariantAt);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Arguments.Count != variant.Fields.Count)
            Add("E_TYPE_MISMATCH", $"Variant '{union.Declaration.Name}.{variant.Name}' expects {variant.Fields.Count} payload values, got {expression.Arguments.Count}", expression.VariantAt);

        var typedArguments = new List<TypedExpr>();
        for (var index = 0; index < expression.Arguments.Count; index++)
        {
            var expectedType = index < variant.Fields.Count
                ? InstantiateUnionFieldType(union, instantiatedType, variant.Fields[index].Type)
                : null;
            typedArguments.Add(CheckExpr(expression.Arguments[index], expectedType, locals, depth));
        }

        return new TypedUnionConstructExpr(
            instantiatedType,
            union.Id,
            variant.Id,
            ReadOnly(typedArguments),
            expression.At);
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

        if (expression.Target is NameExpr bytesErrorName &&
            !locals.ContainsKey(bytesErrorName.Name) &&
            !CurrentModule.DeclaredFunctions.ContainsKey(bytesErrorName.Name) &&
            bytesErrorName.Name == "BytesError")
        {
            if (expression.Field == "InvalidOctet")
                Add("E_TYPE_MISMATCH", "BytesError variants can only be produced by Bytes operations", expression.At);
            else
                Add("E_NAME_UNRESOLVED", $"Variant '{expression.Field}' is not declared on BytesError", expression.At);
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

        if (expression.Target is NameExpr httpErrorName &&
            !locals.ContainsKey(httpErrorName.Name) &&
            httpErrorName.Name == "HttpError")
        {
            if (IsHttpErrorVariant(expression.Field))
                Add("E_TYPE_MISMATCH", "HttpError variants can only be produced by HTTP operations", expression.At);
            else
                Add("E_NAME_UNRESOLVED", $"Variant '{expression.Field}' is not declared on HttpError", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Target is NameExpr processErrorName &&
            !locals.ContainsKey(processErrorName.Name) &&
            processErrorName.Name == "ProcessError")
        {
            if (!CheckProcessTypeScope("ProcessError", expression.At))
                return new TypedErrorExpr(expression.At);
            if (IsProcessErrorVariant(expression.Field))
                Add("E_TYPE_MISMATCH", "ProcessError variants can only be produced by process operations", expression.At);
            else
                Add("E_NAME_UNRESOLVED", $"Variant '{expression.Field}' is not declared on ProcessError", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Target is NameExpr arithmeticErrorName &&
            !locals.ContainsKey(arithmeticErrorName.Name) &&
            arithmeticErrorName.Name == "ArithmeticError")
        {
            if (expression.Field == "Overflow")
                Add("E_TYPE_MISMATCH", "ArithmeticError.Overflow can only be produced by checked integer arithmetic", expression.At);
            else
                Add("E_NAME_UNRESOLVED", $"Variant '{expression.Field}' is not declared on ArithmeticError", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Target is NameExpr mapName &&
            mapName.Name == "Map" &&
            !locals.ContainsKey(mapName.Name) &&
            !CurrentModule.DeclaredFunctions.ContainsKey(mapName.Name))
        {
            Add("E_FIELD_UNKNOWN", $"Map has no field '{expression.Field}'", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var target = CheckExpr(expression.Target, null, locals, depth);
        if (target.Type.IsError) return new TypedErrorExpr(expression.At);
        if (target.Type.IsText && expression.Field == "length")
            return new TypedTextLengthExpr(target, expression.At);
        if (target.Type.IsBytes && expression.Field == "length")
            return new TypedBytesLengthExpr(target, expression.At);
        if (target.Type.IsBytes)
        {
            Add("E_FIELD_UNKNOWN", $"Bytes has no field '{expression.Field}'", expression.At);
            return new TypedErrorExpr(expression.At);
        }
        if (target.Type.IsList && expression.Field == "length")
            return new TypedListLengthExpr(target, expression.At);
        if (target.Type.IsMap && expression.Field == "length")
            return new TypedMapLengthExpr(target, expression.At);
        if (target.Type.IsMap)
        {
            Add("E_FIELD_UNKNOWN", $"Map has no field '{expression.Field}'", expression.At);
            return new TypedErrorExpr(expression.At);
        }
        if (target.Type.IsHttpResponse)
        {
            if (expression.Field == "status")
                return new TypedFieldAccessExpr(HobType.I32, target, 0, expression.At);
            if (expression.Field == "body")
                return new TypedFieldAccessExpr(HobType.Text, target, 1, expression.At);
            Add("E_FIELD_UNKNOWN", $"HttpResponse has no field '{expression.Field}'", expression.At);
            return new TypedErrorExpr(expression.At);
        }
        if (target.Type.IsProcessOutput)
        {
            var outputField = expression.Field switch
            {
                "exit_code" => (Type: HobType.I32, Index: 0),
                "stdout" => (Type: HobType.Text, Index: 1),
                "stderr" => (Type: HobType.Text, Index: 2),
                _ => (Type: (HobType?)null, Index: -1)
            };
            if (outputField.Type is not null)
                return new TypedFieldAccessExpr(outputField.Type, target, outputField.Index, expression.At);
            Add("E_FIELD_UNKNOWN", $"ProcessOutput has no field '{expression.Field}'", expression.At);
            return new TypedErrorExpr(expression.At);
        }
        if (target.Type.Kind == HobTypeKind.Newtype)
        {
            var newtype = _newtypes[target.Type.NewtypeId];
            if (expression.Field != "value")
            {
                Add("E_FIELD_UNKNOWN", $"Newtype '{newtype.Declaration.Name}' has no field '{expression.Field}'", expression.At);
                return new TypedErrorExpr(expression.At);
            }
            return new TypedNewtypeProjectExpr(newtype.Representation, newtype.Id, target, expression.At);
        }
        if (target.Type.Kind != HobTypeKind.Struct)
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
        return new TypedFieldAccessExpr(
            InstantiateStructFieldType(structure, target.Type, field.Type),
            target,
            field.Index,
            expression.At);
    }

    private TypedExpr CheckBinary(BinaryExpr expression, Dictionary<string, LocalSymbol> locals, int depth)
    {
        if (expression.Op is "==" or "!=")
        {
            var comparedLeft = CheckExpr(expression.Left, null, locals, depth);
            var comparedRight = CheckExpr(expression.Right, null, locals, depth);
            if (comparedLeft.Type.IsError || comparedRight.Type.IsError) return new TypedErrorExpr(expression.At);

            if (comparedLeft.Type == comparedRight.Type && SupportsStructuralEquality(comparedLeft.Type))
                return new TypedCompareExpr(expression.Op, comparedLeft, comparedRight, expression.At);

            Add(
                "E_TYPE_MISMATCH",
                $"Comparison '{expression.Op}' requires matching immutable values with structural equality; found '{comparedLeft.Type.DisplayName}' and '{comparedRight.Type.DisplayName}'",
                expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Op is "<" or "<=" or ">" or ">=")
        {
            var comparedLeft = CheckExpr(expression.Left, null, locals, depth);
            var comparedRight = CheckExpr(expression.Right, null, locals, depth);
            if (comparedLeft.Type.IsError || comparedRight.Type.IsError) return new TypedErrorExpr(expression.At);

            if ((IsIntegerType(comparedLeft.Type) && comparedLeft.Type == comparedRight.Type) ||
                (comparedLeft.Type.IsF64 && comparedRight.Type.IsF64))
                return new TypedCompareExpr(expression.Op, comparedLeft, comparedRight, expression.At);

            if ((comparedLeft.Type.IsF64 || comparedRight.Type.IsF64) &&
                IsNumericType(comparedLeft.Type) && IsNumericType(comparedRight.Type))
            {
                Add(
                    "E_TYPE_MISMATCH",
                    $"Comparison '{expression.Op}' requires operands with the same numeric type; found '{comparedLeft.Type.DisplayName}' and '{comparedRight.Type.DisplayName}'",
                    expression.At);
                return new TypedErrorExpr(expression.At);
            }

            if (IsIntegerType(comparedLeft.Type) && IsIntegerType(comparedRight.Type))
            {
                Add(
                    "E_TYPE_MISMATCH",
                    $"Comparison '{expression.Op}' requires operands with the same integer type; found '{comparedLeft.Type.DisplayName}' and '{comparedRight.Type.DisplayName}'",
                    expression.At);
            }
            else
            {
                Add("E_TYPE_MISMATCH", $"Comparison '{expression.Op}' requires i32 operands", expression.At);
            }
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Op is not ("+" or "-" or "*" or "/"))
        {
            Add("E_UNSUPPORTED", $"Arithmetic operator '{expression.Op}' is not implemented", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        var left = CheckExpr(expression.Left, null, locals, depth);
        var right = CheckExpr(expression.Right, null, locals, depth);
        if (left.Type.IsError || right.Type.IsError)
            return new TypedErrorExpr(expression.At);

        if (expression.Op == "/")
        {
            if (left.Type.IsF64 && right.Type.IsF64)
                return new TypedBinaryExpr(HobType.F64, expression.Op, left, right, expression.At);

            Add("E_UNSUPPORTED", "Division is only supported for f64 operands", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (left.Type.IsF64 && right.Type.IsF64)
            return new TypedBinaryExpr(HobType.F64, expression.Op, left, right, expression.At);

        if (IsIntegerType(left.Type) && left.Type == right.Type)
            return new TypedBinaryExpr(left.Type, expression.Op, left, right, expression.At);

        if (IsIntegerType(left.Type) && IsIntegerType(right.Type))
        {
            Add(
                "E_TYPE_MISMATCH",
                $"Arithmetic '{expression.Op}' requires operands with the same integer type; found '{left.Type.DisplayName}' and '{right.Type.DisplayName}'",
                expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if ((left.Type.IsF64 || right.Type.IsF64) && IsNumericType(left.Type) && IsNumericType(right.Type))
        {
            Add(
                "E_TYPE_MISMATCH",
                $"Arithmetic '{expression.Op}' requires operands with the same numeric type; found '{left.Type.DisplayName}' and '{right.Type.DisplayName}'",
                expression.At);
            return new TypedErrorExpr(expression.At);
        }

        Add("E_TYPE_MISMATCH", $"Arithmetic '{expression.Op}' requires i32 operands", expression.At);
        return new TypedErrorExpr(expression.At);
    }

    private static bool IsIntegerType(HobType type) =>
        type.Kind is HobTypeKind.I32 or HobTypeKind.I64 or HobTypeKind.U32 or HobTypeKind.U64;

    private static bool IsNumericType(HobType type) => IsIntegerType(type) || type.IsF64;

    private bool SupportsStructuralEquality(HobType type)
    {
        if (type.IsError || ContainsResourceHandle(type)) return false;

        var pending = new Stack<HobType>();
        var visited = new HashSet<HobType>();
        pending.Push(type);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (current.IsError) return false;

            switch (current.Kind)
            {
                case HobTypeKind.I32:
                case HobTypeKind.I64:
                case HobTypeKind.U32:
                case HobTypeKind.U64:
                case HobTypeKind.F64:
                case HobTypeKind.ArithmeticError:
                case HobTypeKind.Unit:
                case HobTypeKind.Bool:
                case HobTypeKind.Text:
                case HobTypeKind.Bytes:
                case HobTypeKind.BytesError:
                case HobTypeKind.Html:
                case HobTypeKind.FilePath:
                case HobTypeKind.HttpResponse:
                case HobTypeKind.ProcessOutput:
                case HobTypeKind.FsError:
                case HobTypeKind.ProcessError:
                case HobTypeKind.HttpError:
                case HobTypeKind.DbError:
                    break;
                case HobTypeKind.Option:
                case HobTypeKind.List:
                    if (current.Arguments.Count != 1) return false;
                    pending.Push(current.Arguments[0]);
                    break;
                case HobTypeKind.Map:
                    if (current.Arguments.Count != 2 || !current.Arguments[0].IsText) return false;
                    pending.Push(current.Arguments[1]);
                    break;
                case HobTypeKind.Result:
                    if (current.Arguments.Count != 2) return false;
                    pending.Push(current.Arguments[1]);
                    pending.Push(current.Arguments[0]);
                    break;
                case HobTypeKind.Struct:
                    if (current.StructId < 0 || current.StructId >= _structs.Count ||
                        _invalidNominalRecursion.Contains((HobTypeKind.Struct, current.StructId)))
                        return false;
                    if (!visited.Add(current)) break;
                    var structure = _structs[current.StructId];
                    for (var index = structure.Fields.Count - 1; index >= 0; index--)
                        pending.Push(InstantiateStructFieldType(structure, current, structure.Fields[index].Type));
                    break;
                case HobTypeKind.Newtype:
                    if (current.NewtypeId < 0 || current.NewtypeId >= _newtypes.Count ||
                        _invalidNominalRecursion.Contains((HobTypeKind.Newtype, current.NewtypeId)))
                        return false;
                    if (visited.Add(current))
                        pending.Push(_newtypes[current.NewtypeId].Representation);
                    break;
                case HobTypeKind.Union:
                    if (current.UnionId < 0 || current.UnionId >= _unions.Count ||
                        _invalidNominalRecursion.Contains((HobTypeKind.Union, current.UnionId)))
                        return false;
                    if (!visited.Add(current)) break;
                    var union = _unions[current.UnionId];
                    var fields = union.Variants.SelectMany(variant => variant.Fields).ToArray();
                    for (var index = fields.Length - 1; index >= 0; index--)
                        pending.Push(InstantiateUnionFieldType(union, current, fields[index].Type));
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private TypedExpr CheckCall(
        CallExpr expression,
        HobType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth,
        bool isAwaitOperand = false)
    {
        var reference = expression.Reference;
        var name = reference.Declaration;
        if (!reference.IsQualified && expression.ExplicitTypeArguments.Count != 0 &&
            name is ("Some" or "Ok" or "Err" or "None"))
            return CheckExplicitBuiltinCall(expression, locals, depth);

        if (!reference.IsQualified && name is "Some" or "Ok" or "Err")
            return CheckBuiltinCall(expression, expected, locals, depth);

        var function = !reference.IsQualified && expression.ExplicitTypeArguments.Count != 0
            ? ResolveExplicitLocalFunctionReference(reference)
            : ResolveFunctionReference(reference);
        if (function is not null)
        {
            if (function.Declaration.IsAsync && !isAwaitOperand)
                Add("E_ASYNC_CALL_UNAWAITED", $"Async function '{name}' must be called with 'await'", expression.At);
            else if (!function.Declaration.IsAsync && isAwaitOperand)
                Add("E_AWAIT_SYNC", $"Function '{name}' is synchronous and cannot be awaited", expression.At);

            if (expression.ExplicitTypeArguments.Count != 0)
                return CheckExplicitGenericCall(expression, function, locals, depth);

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

            var inferredTypeArguments = new Dictionary<HobType, HobType>();
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
                    inferredTypeArguments.TryGetValue(typeParameter, out var inferred) ? inferred : HobType.Error).ToArray()
                : Array.Empty<HobType>();
            var traitWitnesses = new List<TypedTraitWitness>();
            var boundsValid = true;
            for (var parameterOrdinal = 0; parameterOrdinal < function.TypeParameterBounds.Count; parameterOrdinal++)
            {
                if (parameterOrdinal >= typeArguments.Length || typeArguments[parameterOrdinal].IsError)
                {
                    boundsValid = false;
                    continue;
                }
                foreach (var bound in function.TypeParameterBounds[parameterOrdinal])
                {
                    var trait = _traits[bound.TraitId];
                    var witness = ResolveTraitWitness(trait, typeArguments[parameterOrdinal], expression.At);
                    if (witness is null)
                        boundsValid = false;
                    else
                        traitWitnesses.Add(witness);
                }
            }
            var signatureTypesValid = !function.ReturnType.IsError && function.Parameters.All(parameter => !parameter.Type.IsError);
            if (hasCorrectArity && signatureTypesValid && diagnostics.Count == diagnosticsBeforeArguments)
                _currentFunction?.Calls.Add(new FunctionCallSite(function, expression.At));

            var instantiatedReturnType = isGeneric
                ? SubstituteType(function.ReturnType, inferredTypeArguments)
                : function.ReturnType;
            var returnType = instantiatedReturnType;
            if (isGeneric && (!hasCorrectArity || diagnostics.Count != diagnosticsBeforeArguments ||
                              typeArguments.Any(ContainsError) || arguments.Any(argument => ContainsError(argument.Type)) || !boundsValid))
                returnType = HobType.Error;

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
                ReadOnly(traitWitnesses),
                ReadOnly(arguments),
                expression.At);
        }

        foreach (var typeArgument in expression.ExplicitTypeArguments)
            _ = ResolveType(typeArgument, 0, _currentFunction?.TypeParametersByName);
        foreach (var argument in expression.Arguments)
            _ = CheckExpr(argument, null, locals, depth);
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckExplicitBuiltinCall(
        CallExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        foreach (var typeArgument in expression.ExplicitTypeArguments)
            _ = ResolveType(typeArgument, 0, _currentFunction?.TypeParametersByName);

        var name = expression.Reference.Declaration;
        Add(
            "E_TYPE_MISMATCH",
            $"Built-in constructor '{name}' does not accept explicit type arguments",
            expression.At);
        var expectedValueCount = name == "None" ? 0 : 1;
        if (expression.Arguments.Count != expectedValueCount)
            Add(
                "E_TYPE_MISMATCH",
                $"Constructor '{name}' expects {expectedValueCount} argument, got {expression.Arguments.Count}",
                expression.At);
        foreach (var argument in expression.Arguments)
            _ = CheckExpr(argument, null, locals, depth);
        return new TypedErrorExpr(expression.At);
    }

    private TypedExpr CheckExplicitGenericCall(
        CallExpr expression,
        FunctionSymbol function,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var name = expression.Reference.Declaration;
        var typeArguments = expression.ExplicitTypeArguments
            .Select(typeArgument => ResolveType(typeArgument, 0, _currentFunction?.TypeParametersByName))
            .ToArray();
        var hasCorrectTypeArity = typeArguments.Length == function.TypeParameters.Count;
        if (!hasCorrectTypeArity)
            Add(
                "E_TYPE_MISMATCH",
                $"Function '{name}' expects {function.TypeParameters.Count} type arguments, got {typeArguments.Length}",
                expression.At);

        if (!hasCorrectTypeArity || typeArguments.Any(ContainsError))
        {
            // An incomplete or unresolved vector must not provide partial context to constructors.
            if (expression.Arguments.Count != function.Parameters.Count)
                Add(
                    "E_TYPE_MISMATCH",
                    $"Function '{name}' expects {function.Parameters.Count} arguments, got {expression.Arguments.Count}",
                    expression.At);
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            return new TypedErrorExpr(expression.At);
        }

        var substitutions = new Dictionary<HobType, HobType>();
        for (var index = 0; index < typeArguments.Length; index++)
            substitutions.Add(function.TypeParameters[index], typeArguments[index]);

        var parameterTypes = function.Parameters
            .Select(parameter => SubstituteType(parameter.Type, substitutions))
            .ToArray();
        var instantiatedReturnType = SubstituteType(function.ReturnType, substitutions);
        var hasCorrectValueArity = expression.Arguments.Count == function.Parameters.Count;
        if (!hasCorrectValueArity)
            Add(
                "E_TYPE_MISMATCH",
                $"Function '{name}' expects {function.Parameters.Count} arguments, got {expression.Arguments.Count}",
                expression.At);

        var diagnosticsBeforeArguments = diagnostics.Count;
        var arguments = new List<TypedExpr>(expression.Arguments.Count);
        for (var index = 0; index < expression.Arguments.Count; index++)
        {
            var expectedArgument = index < parameterTypes.Length && !parameterTypes[index].IsError
                ? parameterTypes[index]
                : null;
            arguments.Add(CheckExpr(expression.Arguments[index], expectedArgument, locals, depth));
        }

        var traitWitnesses = new List<TypedTraitWitness>();
        var boundsValid = true;
        for (var parameterOrdinal = 0; parameterOrdinal < function.TypeParameterBounds.Count; parameterOrdinal++)
        {
            if (parameterOrdinal >= typeArguments.Length || ContainsError(typeArguments[parameterOrdinal]))
            {
                boundsValid = false;
                continue;
            }
            foreach (var bound in function.TypeParameterBounds[parameterOrdinal])
            {
                var trait = _traits[bound.TraitId];
                var witness = ResolveTraitWitness(trait, typeArguments[parameterOrdinal], expression.At);
                if (witness is null)
                    boundsValid = false;
                else
                    traitWitnesses.Add(witness);
            }
        }

        var signatureTypesValid = !function.ReturnType.IsError &&
            function.Parameters.All(parameter => !parameter.Type.IsError);
        var argumentsValid = diagnostics.Count == diagnosticsBeforeArguments &&
            arguments.All(argument => !ContainsError(argument.Type));

        foreach (var parameterType in parameterTypes)
            ValidateResourceListType(parameterType, expression.At);
        ValidateResourceListType(instantiatedReturnType, expression.At);
        for (var index = 0; index < arguments.Count; index++)
            ValidateResourceListType(arguments[index].Type, expression.Arguments[index].At);

        var resourceTypesValid = parameterTypes.All(IsResourceContainerValid) &&
            IsResourceContainerValid(instantiatedReturnType) &&
            arguments.All(argument => IsResourceContainerValid(argument.Type));
        if (hasCorrectValueArity && signatureTypesValid && argumentsValid && boundsValid && resourceTypesValid)
            _currentFunction?.Calls.Add(new FunctionCallSite(function, expression.At));

        var returnType = hasCorrectValueArity && signatureTypesValid && argumentsValid && boundsValid && resourceTypesValid
            ? instantiatedReturnType
            : HobType.Error;
        return new TypedCallExpr(
            returnType,
            function.Id,
            function.Declaration.IsAsync,
            ReadOnly(typeArguments),
            ReadOnly(traitWitnesses),
            ReadOnly(arguments),
            expression.At);
    }

    private bool IsResourceContainerValid(HobType type) =>
        !ContainsIllegalResourceList(type) && !ContainsIllegalResourceMap(type);

    private TypedExpr CheckIntegerArithmeticMemberCall(
        MemberCallExpr expression,
        TypedExpr receiver,
        Dictionary<string, LocalSymbol> locals,
        int depth,
        IntegerArithmeticMode mode,
        IntegerArithmeticOperator operation)
    {
        if (!IsIntegerType(receiver.Type))
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add(
                "E_TYPE_MISMATCH",
                $"Integer arithmetic mode '{expression.Member}' requires an i32, i64, u32, or u64 receiver, found '{receiver.Type.DisplayName}'",
                expression.MemberAt);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Arguments.Count != 1)
        {
            Add(
                "E_TYPE_MISMATCH",
                $"Integer arithmetic mode '{expression.Member}' expects 1 argument, got {expression.Arguments.Count}",
                expression.MemberAt);
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            return new TypedErrorExpr(expression.At);
        }

        var right = CheckExpr(expression.Arguments[0], null, locals, depth);
        if (right.Type.IsError)
            return new TypedErrorExpr(expression.At);
        if (right.Type != receiver.Type)
        {
            Add(
                "E_TYPE_MISMATCH",
                $"Integer arithmetic mode '{expression.Member}' expects '{receiver.Type.DisplayName}' argument, found '{right.Type.DisplayName}'",
                expression.Arguments[0].At);
            return new TypedErrorExpr(expression.At);
        }

        var resultType = mode == IntegerArithmeticMode.Checked
            ? HobType.Result(receiver.Type, HobType.ArithmeticError)
            : receiver.Type;
        return new TypedIntegerArithmeticExpr(
            resultType,
            receiver.Type,
            mode,
            operation,
            receiver,
            right,
            expression.At);
    }

    private TypedExpr CheckMemberCall(
        MemberCallExpr expression,
        HobType? expected,
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
            if (targetName.Name == "Bytes" && !CurrentModule.DeclaredFunctions.ContainsKey(targetName.Name))
                return CheckBytesStaticMemberCall(expression, locals, depth);

            if (targetName.Name == "Map" && !CurrentModule.DeclaredFunctions.ContainsKey(targetName.Name))
                return CheckMapStaticMemberCall(expression, expected, locals, depth);

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

            if (targetName.Name == "BytesError" && !CurrentModule.DeclaredFunctions.ContainsKey(targetName.Name))
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                if (expression.Member == "InvalidOctet")
                    Add("E_TYPE_MISMATCH", "BytesError variants can only be produced by Bytes operations", expression.MemberAt);
                else
                    Add("E_NAME_UNRESOLVED", $"Variant '{expression.Member}' is not declared on BytesError", expression.MemberAt);
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

            if (targetName.Name == "HttpError")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                if (IsHttpErrorVariant(expression.Member))
                    Add("E_TYPE_MISMATCH", "HttpError variants can only be produced by HTTP operations", expression.MemberAt);
                else
                    Add("E_NAME_UNRESOLVED", $"Variant '{expression.Member}' is not declared on HttpError", expression.MemberAt);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "ProcessError")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                if (!CheckProcessTypeScope("ProcessError", expression.At))
                    return new TypedErrorExpr(expression.At);
                if (IsProcessErrorVariant(expression.Member))
                    Add("E_TYPE_MISMATCH", "ProcessError variants can only be produced by process operations", expression.MemberAt);
                else
                    Add("E_NAME_UNRESOLVED", $"Variant '{expression.Member}' is not declared on ProcessError", expression.MemberAt);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "ArithmeticError")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                if (expression.Member == "Overflow")
                    Add("E_TYPE_MISMATCH", "ArithmeticError.Overflow can only be produced by checked integer arithmetic", expression.MemberAt);
                else
                    Add("E_NAME_UNRESOLVED", $"Variant '{expression.Member}' is not declared on ArithmeticError", expression.MemberAt);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "config" && expression.Member is ("get_text" or "get_secret_text"))
            {
                CheckArgumentsWithoutExpectation(expression.Arguments, locals, depth);
                if (CurrentModule.PackageId != _rootPackageId)
                    Add("E_CAPABILITY_SCOPE", "Config intrinsics are available only in the root package", expression.At);
                else
                    Add("E_CAPABILITY_MISSING", $"Intrinsic 'config.{expression.Member}' requires a local or parameter of type 'Config'", expression.At);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "secrets" && expression.Member == "reveal_text")
            {
                CheckArgumentsWithoutExpectation(expression.Arguments, locals, depth);
                if (CurrentModule.PackageId != _rootPackageId)
                    Add("E_CAPABILITY_SCOPE", "Secrets intrinsics are available only in the root package", expression.At);
                else
                    Add("E_CAPABILITY_MISSING", "Intrinsic 'secrets.reveal_text' requires a local or parameter of type 'Secrets'", expression.At);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "logger" && expression.Member == "info")
            {
                CheckArgumentsWithoutExpectation(expression.Arguments, locals, depth);
                if (CurrentModule.PackageId != _rootPackageId)
                    Add("E_CAPABILITY_SCOPE", "Logger intrinsics are available only in the root package", expression.At);
                else
                    Add("E_CAPABILITY_MISSING", "Intrinsic 'logger.info' requires a local or parameter of type 'Logger'", expression.At);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "http" && expression.Member == "get_text_async")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                Add("E_CAPABILITY_MISSING", "Intrinsic 'http.get_text_async' requires a local or parameter of type 'HttpClient'", expression.At);
                return new TypedErrorExpr(expression.At);
            }

            if (targetName.Name == "ProcessRunner" && expression.Member == "run_text_async")
            {
                CheckArgumentsWithoutExpectation(expression.Arguments, locals, depth);
                if (!CheckProcessTypeScope("ProcessRunner", expression.At))
                    return new TypedErrorExpr(expression.At);
                Add("E_CAPABILITY_MISSING", "Intrinsic 'ProcessRunner.run_text_async' requires a local or parameter of type 'ProcessRunner'", expression.At);
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

            if (targetName.Name == "fs" && expression.Member == "write_text_async")
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                Add("E_CAPABILITY_MISSING", "Intrinsic 'fs.write_text_async' requires a local or parameter of type 'FsWrite'", expression.At);
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

        if (IntegerArithmeticMember.TryParse(expression.Member.AsSpan(), out var arithmeticMode, out var arithmeticOperation))
            return CheckIntegerArithmeticMemberCall(expression, receiver, locals, depth, arithmeticMode, arithmeticOperation);

        if (expression.Member is "get_text" or "get_secret_text" or "reveal_text" or "info")
            return CheckConfigCapabilityIntrinsic(expression, receiver, locals, depth);

        if (expression.Member == "get_text_async")
        {
            if (!receiver.Type.IsHttpClient)
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                var capabilityTargetDescription = expression.Target is NameExpr localName && locals.ContainsKey(localName.Name)
                    ? $"Local '{localName.Name}' of type '{receiver.Type.DisplayName}'"
                    : $"Value of type '{receiver.Type.DisplayName}'";
                Add("E_CAPABILITY_MISSING", $"{capabilityTargetDescription} cannot provide capability member 'get_text_async' (requires 'HttpClient')", expression.At);
                return new TypedErrorExpr(expression.At);
            }

            var diagnosticsBeforeCall = diagnostics.Count;
            var hasCorrectArity = expression.Arguments.Count == 1;
            if (!hasCorrectArity)
                Add("E_TYPE_MISMATCH", $"Intrinsic 'HttpClient.get_text_async' expects 1 argument, got {expression.Arguments.Count}", expression.MemberAt);

            var arguments = new List<TypedExpr> { receiver };
            var hasTextTarget = false;
            if (expression.Arguments.Count > 0)
            {
                var target = CheckExpr(expression.Arguments[0], null, locals, depth);
                hasTextTarget = target.Type.IsText;
                if (!target.Type.IsError && !hasTextTarget)
                    Add("E_TYPE_MISMATCH", $"Intrinsic 'HttpClient.get_text_async' expects a Text target, found '{target.Type.DisplayName}'", expression.Arguments[0].At);
                arguments.Add(target);
            }
            else
                arguments.Add(new TypedErrorExpr(expression.MemberAt));
            for (var index = 1; index < expression.Arguments.Count; index++)
                _ = CheckExpr(expression.Arguments[index], null, locals, depth);

            var callIsValid = hasCorrectArity && hasTextTarget && diagnostics.Count == diagnosticsBeforeCall;
            if (!isAwaitOperand)
                Add("E_ASYNC_CALL_UNAWAITED", "Intrinsic 'HttpClient.get_text_async' must be called with 'await'", expression.At);
            if (callIsValid)
                _currentFunction?.DirectEffects.Add(new DirectEffectCall("net.client", "HttpClient.get_text_async", expression.MemberAt));
            return new TypedIntrinsicCallExpr(
                HobType.Result(HobType.HttpResponse, HobType.HttpError),
                BuiltinIntrinsic.HttpGetTextAsync,
                ReadOnly(arguments),
                expression.At);
        }

        if (expression.Member == "run_text_async")
        {
            if (!receiver.Type.IsProcessRunner)
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                var processTargetDescription = expression.Target is NameExpr localName && locals.ContainsKey(localName.Name)
                    ? $"Local '{localName.Name}' of type '{receiver.Type.DisplayName}'"
                    : $"Value of type '{receiver.Type.DisplayName}'";
                Add("E_CAPABILITY_MISSING", $"{processTargetDescription} cannot provide capability member 'run_text_async' (requires 'ProcessRunner')", expression.At);
                return new TypedErrorExpr(expression.At);
            }

            var diagnosticsBeforeCall = diagnostics.Count;
            var hasCorrectArity = expression.Arguments.Count == 2;
            if (!hasCorrectArity)
                Add("E_TYPE_MISMATCH", $"Intrinsic 'ProcessRunner.run_text_async' expects 2 arguments, got {expression.Arguments.Count}", expression.MemberAt);

            var arguments = new List<TypedExpr> { receiver };
            var expectedTypes = new[] { HobType.List(HobType.Text), HobType.Text };
            var argumentTypesValid = true;
            for (var index = 0; index < expectedTypes.Length; index++)
            {
                if (index >= expression.Arguments.Count)
                {
                    arguments.Add(new TypedErrorExpr(expression.MemberAt));
                    argumentTypesValid = false;
                    continue;
                }

                var argument = CheckExpr(expression.Arguments[index], expectedTypes[index], locals, depth);
                arguments.Add(argument);
                argumentTypesValid &= argument.Type == expectedTypes[index];
            }
            for (var index = expectedTypes.Length; index < expression.Arguments.Count; index++)
                _ = CheckExpr(expression.Arguments[index], null, locals, depth);

            var callIsValid = hasCorrectArity && argumentTypesValid && diagnostics.Count == diagnosticsBeforeCall;
            if (!isAwaitOperand)
                Add("E_ASYNC_CALL_UNAWAITED", "Intrinsic 'ProcessRunner.run_text_async' must be called with 'await'", expression.At);
            if (callIsValid)
                _currentFunction?.DirectEffects.Add(new DirectEffectCall("process.spawn", "ProcessRunner.run_text_async", expression.MemberAt));

            return new TypedIntrinsicCallExpr(
                HobType.Result(HobType.ProcessOutput, HobType.ProcessError),
                BuiltinIntrinsic.ProcessRunTextAsync,
                ReadOnly(arguments),
                expression.At);
        }

        if (receiver.Type.IsDbWrite && expression.Member == "begin")
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_RESOURCE_ESCAPE", "A transaction must be opened directly in a 'with db.begin() as tx' scope", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        if (receiver.Type.IsMap)
            return CheckMapMemberCall(expression, receiver, locals, depth);

        if (receiver.Type.IsBytes)
        {
            if (expression.Member is not ("append" or "get"))
            {
                foreach (var argument in expression.Arguments)
                    _ = CheckExpr(argument, null, locals, depth);
                Add("E_FIELD_UNKNOWN", $"Bytes has no member '{expression.Member}'", expression.MemberAt);
                return new TypedErrorExpr(expression.At);
            }

            if (expression.Arguments.Count != 1)
            {
                Add("E_TYPE_MISMATCH", $"Bytes.{expression.Member} expects 1 argument, got {expression.Arguments.Count}", expression.MemberAt);
                for (var index = 0; index < expression.Arguments.Count; index++)
                    _ = CheckExpr(expression.Arguments[index], HobType.I32, locals, depth);
                return new TypedErrorExpr(expression.At);
            }

            var bytesArgument = CheckExpr(expression.Arguments[0], HobType.I32, locals, depth);
            return expression.Member == "append"
                ? new TypedBytesAppendExpr(receiver, bytesArgument, expression.At)
                : new TypedBytesGetExpr(receiver, bytesArgument, expression.At);
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
                    var argumentType = i == 0 ? (get ? HobType.I32 : receiver.Type.Arguments[0]) : null;
                    _ = CheckExpr(expression.Arguments[i], argumentType, locals, depth);
                }
                return new TypedErrorExpr(expression.At);
            }

            if (get)
            {
                var index = CheckExpr(expression.Arguments[0], HobType.I32, locals, depth);
                return new TypedListGetExpr(
                    HobType.Option(receiver.Type.Arguments[0]),
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

            var resultType = HobType.Result(HobType.Text, HobType.FsError);
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

            var resultType = HobType.Result(HobType.Text, HobType.FsError);
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

            var resultType = HobType.Result(HobType.Bool, HobType.FsError);
            if (hasCorrectArity && hasSupportedPathType && hasTextValue && diagnostics.Count == diagnosticsBeforeCall)
                _currentFunction?.DirectEffects.Add(new DirectEffectCall("fs.write", "fs.write_text", expression.MemberAt));
            return new TypedIntrinsicCallExpr(
                resultType,
                BuiltinIntrinsic.FsWriteText,
                ReadOnly(arguments),
                expression.At);
        }

        if (receiver.Type.IsFsWrite && expression.Member == "write_text_async")
        {
            var diagnosticsBeforeCall = diagnostics.Count;
            var hasCorrectArity = expression.Arguments.Count == 2;
            if (!hasCorrectArity)
                Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.write_text_async' expects 2 arguments, got {expression.Arguments.Count}", expression.MemberAt);

            var arguments = new List<TypedExpr> { receiver };
            var hasSupportedPathType = false;
            if (expression.Arguments.Count > 0)
            {
                var path = CheckExpr(expression.Arguments[0], null, locals, depth);
                hasSupportedPathType = path.Type.IsText || path.Type.IsFilePath;
                if (!path.Type.IsError && !hasSupportedPathType)
                    Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.write_text_async' expects a Text or FilePath path, found '{path.Type.DisplayName}'", expression.Arguments[0].At);
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
                    Add("E_TYPE_MISMATCH", $"Intrinsic 'fs.write_text_async' expects a Text value, found '{value.Type.DisplayName}'", expression.Arguments[1].At);
                arguments.Add(value);
            }
            else
                arguments.Add(new TypedErrorExpr(expression.MemberAt));

            for (var i = 2; i < expression.Arguments.Count; i++)
                _ = CheckExpr(expression.Arguments[i], null, locals, depth);

            var resultType = HobType.Result(HobType.Bool, HobType.FsError);
            var callIsValid = hasCorrectArity && hasSupportedPathType && hasTextValue && diagnostics.Count == diagnosticsBeforeCall;
            if (!isAwaitOperand)
                Add("E_ASYNC_CALL_UNAWAITED", "Intrinsic 'fs.write_text_async' must be called with 'await'", expression.At);
            if (callIsValid)
                _currentFunction?.DirectEffects.Add(new DirectEffectCall("fs.write", "fs.write_text_async", expression.MemberAt));
            return new TypedIntrinsicCallExpr(
                resultType,
                BuiltinIntrinsic.FsWriteTextAsync,
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

        if (expression.Member is "read_text" or "read_text_async" or "write_text_async")
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
                    _ = CheckExpr(expression.Arguments[i], i == 0 ? HobType.Text : null, locals, depth);
                return new TypedErrorExpr(expression.At);
            }

            var separator = CheckExpr(expression.Arguments[0], HobType.Text, locals, depth);
            return new TypedIntrinsicCallExpr(
                HobType.List(HobType.Text),
                BuiltinIntrinsic.TextSplit,
                ReadOnly([receiver, separator]),
                expression.At);
        }

        if (receiver.Type.Kind == HobTypeKind.Newtype && expression.Member == "value")
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_TYPE_MISMATCH", "Newtype projection 'value' is a field, not a callable member", expression.MemberAt);
            return new TypedErrorExpr(expression.At);
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

    private TypedExpr CheckMapStaticMemberCall(
        MemberCallExpr expression,
        HobType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (expression.Member != "empty")
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_FIELD_UNKNOWN", $"Map has no member '{expression.Member}'", expression.MemberAt);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Arguments.Count != 0)
        {
            Add("E_TYPE_MISMATCH", $"Map.empty expects 0 arguments, got {expression.Arguments.Count}", expression.MemberAt);
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            return new TypedErrorExpr(expression.At);
        }

        if (expected is null)
        {
            Add("E_TYPE_MISMATCH", "Map.empty() requires an expected type of Map<Text, V>", expression.At);
            return new TypedErrorExpr(expression.At);
        }
        if (expected.IsError || ContainsError(expected))
            return new TypedErrorExpr(expression.At);
        if (!expected.IsMap)
        {
            Add("E_TYPE_MISMATCH", $"Map.empty() requires an expected type of Map<Text, V>, found '{expected.DisplayName}'", expression.At);
            return new TypedErrorExpr(expression.At);
        }

        return new TypedMapEmptyExpr(expected, expression.At);
    }

    private TypedExpr CheckBytesStaticMemberCall(
        MemberCallExpr expression,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        if (expression.Member != "empty")
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_FIELD_UNKNOWN", $"Bytes has no member '{expression.Member}'", expression.MemberAt);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Arguments.Count != 0)
        {
            Add("E_TYPE_MISMATCH", $"Bytes.empty expects 0 arguments, got {expression.Arguments.Count}", expression.MemberAt);
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            return new TypedErrorExpr(expression.At);
        }

        return new TypedBytesEmptyExpr(expression.At);
    }

    private TypedExpr CheckMapMemberCall(
        MemberCallExpr expression,
        TypedExpr receiver,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        HobType[]? expectedTypes = expression.Member switch
        {
            "set" => new[] { HobType.Text, receiver.Type.Arguments[1] },
            "get" => [HobType.Text],
            "keys" => Array.Empty<HobType>(),
            _ => null
        };

        if (expectedTypes is null)
        {
            foreach (var argument in expression.Arguments)
                _ = CheckExpr(argument, null, locals, depth);
            Add("E_FIELD_UNKNOWN", $"Map has no member '{expression.Member}'", expression.MemberAt);
            return new TypedErrorExpr(expression.At);
        }

        if (expression.Arguments.Count != expectedTypes.Length)
        {
            var expectedArgumentCount = expectedTypes.Length == 1
                ? "1 argument"
                : $"{expectedTypes.Length} arguments";
            Add(
                "E_TYPE_MISMATCH",
                $"Map.{expression.Member} expects {expectedArgumentCount}, got {expression.Arguments.Count}",
                expression.MemberAt);
            for (var index = 0; index < expression.Arguments.Count; index++)
                _ = CheckExpr(
                    expression.Arguments[index],
                    index < expectedTypes.Length ? expectedTypes[index] : null,
                    locals,
                    depth);
            return new TypedErrorExpr(expression.At);
        }

        var arguments = new TypedExpr[expectedTypes.Length];
        for (var index = 0; index < expectedTypes.Length; index++)
            arguments[index] = CheckExpr(expression.Arguments[index], expectedTypes[index], locals, depth);

        return expression.Member switch
        {
            "set" => new TypedMapSetExpr(receiver.Type, receiver, arguments[0], arguments[1], expression.At),
            "get" => new TypedMapGetExpr(HobType.Option(receiver.Type.Arguments[1]), receiver, arguments[0], expression.At),
            "keys" => new TypedMapKeysExpr(receiver, expression.At),
            _ => throw new InvalidOperationException("Unknown map member")
        };
    }

    private TypedExpr CheckConfigCapabilityIntrinsic(
        MemberCallExpr expression,
        TypedExpr receiver,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var operation = expression.Member switch
        {
            "get_text" => BuiltinIntrinsic.ConfigGetText,
            "get_secret_text" => BuiltinIntrinsic.ConfigGetSecretText,
            "reveal_text" => BuiltinIntrinsic.SecretsRevealText,
            "info" => BuiltinIntrinsic.LoggerInfo,
            _ => throw new InvalidOperationException("Unknown configuration capability intrinsic")
        };
        var requiredType = operation switch
        {
            BuiltinIntrinsic.ConfigGetText or BuiltinIntrinsic.ConfigGetSecretText => HobType.Config,
            BuiltinIntrinsic.SecretsRevealText => HobType.Secrets,
            BuiltinIntrinsic.LoggerInfo => HobType.Logger,
            _ => throw new InvalidOperationException("Unknown configuration capability intrinsic")
        };
        if (receiver.Type != requiredType)
        {
            CheckArgumentsWithoutExpectation(expression.Arguments, locals, depth);
            var targetDescription = expression.Target is NameExpr localName && locals.ContainsKey(localName.Name)
                ? $"Local '{localName.Name}' of type '{receiver.Type.DisplayName}'"
                : $"Value of type '{receiver.Type.DisplayName}'";
            Add(
                "E_CAPABILITY_MISSING",
                $"{targetDescription} cannot provide capability member '{expression.Member}' (requires '{requiredType.DisplayName}')",
                expression.MemberAt);
            return new TypedErrorExpr(expression.At);
        }

        var expectedCount = operation == BuiltinIntrinsic.LoggerInfo ? 2 : 1;
        var diagnosticsBeforeArguments = diagnostics.Count;
        var hasCorrectArity = expression.Arguments.Count == expectedCount;
        if (!hasCorrectArity)
            Add("E_TYPE_MISMATCH", $"Intrinsic '{requiredType.DisplayName}.{expression.Member}' expects {expectedCount} argument(s), got {expression.Arguments.Count}", expression.MemberAt);

        var arguments = new List<TypedExpr> { receiver };
        var validTypes = true;
        for (var index = 0; index < expectedCount; index++)
        {
            if (index >= expression.Arguments.Count)
            {
                arguments.Add(new TypedErrorExpr(expression.MemberAt));
                validTypes = false;
                continue;
            }

            var expectedType = operation switch
            {
                BuiltinIntrinsic.ConfigGetText or BuiltinIntrinsic.ConfigGetSecretText => HobType.Text,
                BuiltinIntrinsic.SecretsRevealText => HobType.SecretText,
                BuiltinIntrinsic.LoggerInfo => HobType.Text,
                _ => throw new InvalidOperationException("Unknown configuration capability intrinsic")
            };
            var argument = CheckExpr(expression.Arguments[index], expectedType, locals, depth);
            arguments.Add(argument);
            validTypes &= argument.Type == expectedType;

            if (operation is BuiltinIntrinsic.ConfigGetText or BuiltinIntrinsic.ConfigGetSecretText)
            {
                if (expression.Arguments[index] is not TextExpr literal)
                {
                    if (argument.Type.IsText)
                        Add("E_CONFIG_KEY", "Config lookup keys must be string literals declared in the root [config] schema", expression.Arguments[index].At);
                    validTypes = false;
                }
                else if (!_rootConfigFields.TryGetValue(literal.Value, out var field))
                {
                    Add("E_CONFIG_KEY", "Config lookup key is not declared in the root [config] schema", expression.Arguments[index].At);
                    validTypes = false;
                }
                else
                {
                    var expectedKind = operation == BuiltinIntrinsic.ConfigGetText
                        ? ConfigFieldKind.Text
                        : ConfigFieldKind.SecretText;
                    if (field.Kind != expectedKind)
                    {
                        Add("E_CONFIG_TYPE", "Config lookup method does not match the declared field type", expression.Arguments[index].At);
                        validTypes = false;
                    }
                }
            }
        }
        for (var index = expectedCount; index < expression.Arguments.Count; index++)
            _ = CheckExpr(expression.Arguments[index], null, locals, depth);

        var resultType = operation switch
        {
            BuiltinIntrinsic.ConfigGetText or BuiltinIntrinsic.SecretsRevealText => HobType.Text,
            BuiltinIntrinsic.ConfigGetSecretText => HobType.SecretText,
            BuiltinIntrinsic.LoggerInfo => HobType.Bool,
            _ => throw new InvalidOperationException("Unknown configuration capability intrinsic")
        };
        var callIsValid = hasCorrectArity && validTypes && diagnostics.Count == diagnosticsBeforeArguments;
        if (callIsValid)
        {
            var effect = operation switch
            {
                BuiltinIntrinsic.ConfigGetText or BuiltinIntrinsic.ConfigGetSecretText => "env.read",
                BuiltinIntrinsic.SecretsRevealText => "secret.reveal",
                BuiltinIntrinsic.LoggerInfo => "log.write",
                _ => throw new InvalidOperationException("Unknown configuration capability intrinsic")
            };
            var operationName = operation switch
            {
                BuiltinIntrinsic.ConfigGetText => "Config.get_text",
                BuiltinIntrinsic.ConfigGetSecretText => "Config.get_secret_text",
                BuiltinIntrinsic.SecretsRevealText => "Secrets.reveal_text",
                BuiltinIntrinsic.LoggerInfo => "Logger.info",
                _ => throw new InvalidOperationException("Unknown configuration capability intrinsic")
            };
            _currentFunction?.DirectEffects.Add(new DirectEffectCall(effect, operationName, expression.MemberAt));
        }

        return new TypedIntrinsicCallExpr(resultType, operation, ReadOnly(arguments), expression.At);
    }

    private void CheckArgumentsWithoutExpectation(
        IReadOnlyList<Expr> expressions,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        foreach (var argument in expressions)
            _ = CheckExpr(argument, null, locals, depth);
    }

    private TypedExpr CheckTransactionMemberCall(
        MemberCallExpr expression,
        HobType? expected,
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
            var receiver = new TypedLocalExpr(HobType.Transaction, transactionLocal.Id, transactionName.At);
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
        HobType? expected,
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
        HobType resultType;
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
                if (expected.Kind == HobTypeKind.Result &&
                    expected.Arguments[0].Kind == HobTypeKind.Option &&
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

            resultType = validExpectedResult ? expected! : HobType.Error;
        }
        else
        {
            resultType = HobType.Result(HobType.I32, HobType.DbError);
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

    private StructSymbol? CheckDatabaseStruct(HobType type, string role, Token at)
    {
        if (type.IsError) return null;
        if (type.Kind != HobTypeKind.Struct)
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

        if (structure.TypeParameters.Count != 0)
        {
            Add("E_DB_CODEC_UNSUPPORTED", $"SQLite {role} values cannot use generic struct '{structure.Declaration.Name}'", at);
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

    private static bool IsDatabaseScalarType(HobType type) =>
        type.IsI32 || type.IsBool || type.IsText ||
        type.Kind == HobTypeKind.Option &&
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
            "text" => (BuiltinIntrinsic.HtmlText, new[] { HobType.Text }),
            "heading" => (BuiltinIntrinsic.HtmlHeading, new[] { HobType.Text }),
            "paragraph" => (BuiltinIntrinsic.HtmlParagraph, new[] { HobType.Text }),
            "concat" => (BuiltinIntrinsic.HtmlConcat, new[] { HobType.Html, HobType.Html }),
            "document" => (BuiltinIntrinsic.HtmlDocument, new[] { HobType.Text, HobType.Html }),
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
            HobType.Html,
            intrinsic,
            ReadOnly(arguments),
            expression.At);
    }

    private TypedExpr CheckBuiltinCall(
        CallExpr expression,
        HobType? expected,
        Dictionary<string, LocalSymbol> locals,
        int depth)
    {
        var constructorName = expression.Reference.Declaration;
        var wantsOption = constructorName == "Some";
        var wantsOk = constructorName == "Ok";
        var expectedKind = wantsOption ? HobTypeKind.Option : HobTypeKind.Result;
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
                    ? HobType.Option(value.Type)
                    : wantsOk
                        ? HobType.Result(value.Type, HobType.Error)
                        : HobType.Result(HobType.Error, value.Type);
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
        HobType? expected,
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
        HobType? inferredResult = expected;

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
                        var payloadType = i < shape.PayloadTypes.Count ? shape.PayloadTypes[i] : HobType.Error;
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

        return new TypedMatchExpr(inferredResult ?? HobType.Error, value, ReadOnly(arms), expression.At);
    }

    private IReadOnlyList<VariantShape>? GetVariantShapes(HobType type)
    {
        if (type.IsError) return null;
        if (type.Kind == HobTypeKind.Union)
        {
            var union = _unions[type.UnionId];
            return ReadOnly(union.Variants.Select(variant => new VariantShape(
                $"{union.Declaration.Name}.{variant.Name}",
                $"u:{union.Id}:{variant.Id}",
                union.Id,
                variant.Id,
                null,
                ReadOnly(variant.Fields.Select(field => InstantiateUnionFieldType(union, type, field.Type))))));
        }
        if (type.Kind == HobTypeKind.Option)
        {
            return ReadOnly<VariantShape>([
                new("Some", "option:Some", null, 0, BuiltinVariant.Some, ReadOnly([type.Arguments[0]])),
                new("None", "option:None", null, 1, BuiltinVariant.None, [])
            ]);
        }
        if (type.Kind == HobTypeKind.Result)
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
        if (type.IsBytesError)
        {
            return ReadOnly<VariantShape>([
                new("BytesError.InvalidOctet", "byteserror:InvalidOctet", null, 0, BuiltinVariant.BytesInvalidOctet, [])
            ]);
        }
        if (type.IsDbError)
        {
            return ReadOnly<VariantShape>([
                new("DbError.Statement", "dberror:Statement", null, 0, BuiltinVariant.DbErrorStatement, []),
                new("DbError.RowShape", "dberror:RowShape", null, 1, BuiltinVariant.DbErrorRowShape, [])
            ]);
        }
        if (type.IsArithmeticError)
        {
            return ReadOnly<VariantShape>([
                new("ArithmeticError.Overflow", "arithmeticerror:Overflow", null, 0, BuiltinVariant.ArithmeticErrorOverflow, [])
            ]);
        }
        if (type.IsHttpError)
        {
            return ReadOnly<VariantShape>([
                new("HttpError.InvalidTarget", "httperror:InvalidTarget", null, 0, BuiltinVariant.HttpInvalidTarget, []),
                new("HttpError.Transport", "httperror:Transport", null, 1, BuiltinVariant.HttpTransport, []),
                new("HttpError.Timeout", "httperror:Timeout", null, 2, BuiltinVariant.HttpTimeout, []),
                new("HttpError.ResponseTooLarge", "httperror:ResponseTooLarge", null, 3, BuiltinVariant.HttpResponseTooLarge, []),
                new("HttpError.InvalidText", "httperror:InvalidText", null, 4, BuiltinVariant.HttpInvalidText, [])
            ]);
        }
        if (type.IsProcessError)
        {
            return ReadOnly<VariantShape>([
                new("ProcessError.InvalidArgument", "processerror:InvalidArgument", null, 0, BuiltinVariant.ProcessInvalidArgument, []),
                new("ProcessError.InputTooLarge", "processerror:InputTooLarge", null, 1, BuiltinVariant.ProcessInputTooLarge, []),
                new("ProcessError.OutputTooLarge", "processerror:OutputTooLarge", null, 2, BuiltinVariant.ProcessOutputTooLarge, []),
                new("ProcessError.InvalidText", "processerror:InvalidText", null, 3, BuiltinVariant.ProcessInvalidText, []),
                new("ProcessError.StartFailed", "processerror:StartFailed", null, 4, BuiltinVariant.ProcessStartFailed, []),
                new("ProcessError.TimedOut", "processerror:TimedOut", null, 5, BuiltinVariant.ProcessTimedOut, [])
            ]);
        }
        return null;
    }

    private VariantShape? ResolvePatternShape(
        VariantPattern pattern,
        HobType scrutineeType,
        IReadOnlyList<VariantShape> shapes)
    {
        if (scrutineeType.Kind == HobTypeKind.Union)
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
        else if (scrutineeType.IsFsError || scrutineeType.IsBytesError || scrutineeType.IsDbError ||
                 scrutineeType.IsHttpError || scrutineeType.IsProcessError || scrutineeType.IsArithmeticError)
        {
            var builtinErrorName = scrutineeType.IsFsError
                ? "FsError"
                : scrutineeType.IsBytesError ? "BytesError"
                : scrutineeType.IsDbError ? "DbError"
                : scrutineeType.IsHttpError ? "HttpError"
                : scrutineeType.IsProcessError ? "ProcessError" : "ArithmeticError";
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

        var shapeName = scrutineeType.Kind == HobTypeKind.Union || scrutineeType.IsFsError || scrutineeType.IsBytesError || scrutineeType.IsDbError ||
                        scrutineeType.IsHttpError || scrutineeType.IsProcessError || scrutineeType.IsArithmeticError
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

    private static bool ContainsType(HobType type, HobType sought)
    {
        if (type == sought) return true;
        return type.Arguments.Any(argument => ContainsType(argument, sought));
    }

    private static bool ContainsTypeParameter(HobType type) =>
        type.Kind == HobTypeKind.TypeParameter || type.Arguments.Any(ContainsTypeParameter);

    private bool ContainsResourceHandle(HobType type, bool[] resourceReachableDeclarations)
    {
        var pending = new Stack<HobType>();
        var visited = new HashSet<HobType>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (IsResourceHandle(current))
                return true;

            if (current.Kind == HobTypeKind.Struct)
            {
                if (_invalidNominalRecursion.Contains((HobTypeKind.Struct, current.StructId))) continue;
                if (resourceReachableDeclarations[NominalDeclarationIndex(current)])
                    return true;
                if (!visited.Add(current)) continue;
                var structure = _structs[current.StructId];
                foreach (var field in structure.Fields)
                    pending.Push(InstantiateStructFieldType(structure, current, field.Type));
                continue;
            }

            if (current.Kind == HobTypeKind.Union)
            {
                var declaration = NominalDeclarationIndex(current);
                if (_invalidNominalRecursion.Contains((HobTypeKind.Union, current.UnionId))) continue;
                if (resourceReachableDeclarations[declaration]) return true;
                if (!visited.Add(current)) continue;
                var union = _unions[current.UnionId];
                foreach (var field in union.Variants.SelectMany(variant => variant.Fields))
                    pending.Push(InstantiateUnionFieldType(union, current, field.Type));
                continue;
            }

            if (current.Kind == HobTypeKind.Newtype)
            {
                var declaration = NominalDeclarationIndex(current);
                if (_invalidNominalRecursion.Contains((HobTypeKind.Newtype, current.NewtypeId))) continue;
                if (resourceReachableDeclarations[declaration]) return true;
                if (!visited.Add(current)) continue;
                pending.Push(_newtypes[current.NewtypeId].Representation);
                continue;
            }

            foreach (var argument in current.Arguments)
                pending.Push(argument);
        }

        return false;
    }

    private static bool IsResourceHandle(HobType type) =>
        type.Kind is HobTypeKind.FsRead or HobTypeKind.FsWrite or HobTypeKind.Config or HobTypeKind.Secrets or
            HobTypeKind.Logger or HobTypeKind.ProcessRunner or HobTypeKind.DbRead or HobTypeKind.DbWrite or
            HobTypeKind.HttpClient or HobTypeKind.Transaction;

    private bool TryGetDeclarationNode(HobType type, out int declaration)
    {
        if (type.Kind == HobTypeKind.Struct && type.StructId >= 0 && type.StructId < _structs.Count)
        {
            declaration = NominalDeclarationIndex(HobTypeKind.Struct, type.StructId);
            return true;
        }
        if (type.Kind == HobTypeKind.Union && type.UnionId >= 0 && type.UnionId < _unions.Count)
        {
            declaration = NominalDeclarationIndex(HobTypeKind.Union, type.UnionId);
            return true;
        }
        if (type.Kind == HobTypeKind.Newtype && type.NewtypeId >= 0 && type.NewtypeId < _newtypes.Count)
        {
            declaration = NominalDeclarationIndex(HobTypeKind.Newtype, type.NewtypeId);
            return true;
        }

        declaration = -1;
        return false;
    }

    private int NominalDeclarationIndex(HobType type)
    {
        if (!TryGetDeclarationNode(type, out var declaration))
            throw new InvalidOperationException("A nominal resource graph node was requested for a non-nominal type");
        return declaration;
    }

    private int NominalDeclarationIndex(HobTypeKind kind, int id) => kind switch
    {
        HobTypeKind.Struct when id >= 0 && id < _structs.Count => id,
        HobTypeKind.Newtype when id >= 0 && id < _newtypes.Count => _structs.Count + id,
        HobTypeKind.Union when id >= 0 && id < _unions.Count => _structs.Count + _newtypes.Count + id,
        _ => throw new InvalidOperationException("A nominal resource graph index was requested for an invalid declaration")
    };

    private bool ContainsResourceHandle(HobType type) =>
        ContainsResourceHandle(type, _resourceReachableDeclarations);

    private static bool ContainsError(HobType type) =>
        type.IsError || type.Arguments.Any(ContainsError);

    private static bool TryUnifyType(
        HobType formal,
        HobType actual,
        Dictionary<HobType, HobType> bindings)
    {
        if (formal.IsError || actual.IsError) return true;

        if (formal.Kind == HobTypeKind.TypeParameter)
        {
            if (!bindings.TryGetValue(formal, out var previous))
            {
                bindings.Add(formal, actual);
                return true;
            }
            return previous == actual;
        }

        if (formal.Kind is HobTypeKind.Option or HobTypeKind.List or HobTypeKind.Map or HobTypeKind.Result or
            HobTypeKind.Struct or HobTypeKind.Union or HobTypeKind.Newtype)
        {
            if (formal.Kind != actual.Kind ||
                formal.Kind == HobTypeKind.Struct && formal.StructId != actual.StructId ||
                formal.Kind == HobTypeKind.Union && formal.UnionId != actual.UnionId ||
                formal.Kind == HobTypeKind.Newtype && formal.NewtypeId != actual.NewtypeId ||
                formal.Arguments.Count != actual.Arguments.Count)
                return false;
            for (var i = 0; i < formal.Arguments.Count; i++)
                if (!TryUnifyType(formal.Arguments[i], actual.Arguments[i], bindings)) return false;
            return true;
        }

        return formal == actual;
    }

    private static HobType SubstituteType(
        HobType type,
        IReadOnlyDictionary<HobType, HobType> bindings,
        bool preserveUnboundTypeParameters = false)
    {
        if (type.Kind == HobTypeKind.TypeParameter)
            return bindings.TryGetValue(type, out var inferred)
                ? inferred
                : preserveUnboundTypeParameters ? type : HobType.Error;
        if (type.Kind == HobTypeKind.Option)
            return HobType.Option(SubstituteType(type.Arguments[0], bindings, preserveUnboundTypeParameters));
        if (type.Kind == HobTypeKind.List)
            return HobType.List(SubstituteType(type.Arguments[0], bindings, preserveUnboundTypeParameters));
        if (type.Kind == HobTypeKind.Map)
            return HobType.Map(
                SubstituteType(type.Arguments[0], bindings, preserveUnboundTypeParameters),
                SubstituteType(type.Arguments[1], bindings, preserveUnboundTypeParameters));
        if (type.Kind == HobTypeKind.Result)
            return HobType.Result(
                SubstituteType(type.Arguments[0], bindings, preserveUnboundTypeParameters),
                SubstituteType(type.Arguments[1], bindings, preserveUnboundTypeParameters));
        if (type.Kind == HobTypeKind.Union && type.Arguments.Count != 0)
            return HobType.ForUnion(
                type.UnionId,
                type.NominalName,
                type.Arguments.Select(argument => SubstituteType(argument, bindings, preserveUnboundTypeParameters)));
        if (type.Kind == HobTypeKind.Struct && type.Arguments.Count != 0)
            return HobType.ForStruct(
                type.StructId,
                type.NominalName,
                type.Arguments.Select(argument => SubstituteType(argument, bindings, preserveUnboundTypeParameters)));
        return type;
    }

    private static IReadOnlyDictionary<HobType, HobType> StructTypeBindings(StructSymbol structure, HobType instantiatedType)
    {
        var bindings = new Dictionary<HobType, HobType>();
        for (var index = 0; index < structure.TypeParameters.Count; index++)
        {
            if (index < instantiatedType.Arguments.Count)
                bindings[structure.TypeParameters[index]] = instantiatedType.Arguments[index];
        }
        return bindings;
    }

    private static HobType InstantiateStructFieldType(StructSymbol structure, HobType instantiatedType, HobType fieldType) =>
        structure.TypeParameters.Count == 0
            ? fieldType
            : SubstituteType(fieldType, StructTypeBindings(structure, instantiatedType), preserveUnboundTypeParameters: true);

    private static IReadOnlyDictionary<HobType, HobType> UnionTypeBindings(UnionSymbol union, HobType instantiatedType)
    {
        var bindings = new Dictionary<HobType, HobType>();
        for (var index = 0; index < union.TypeParameters.Count; index++)
        {
            if (index < instantiatedType.Arguments.Count)
                bindings[union.TypeParameters[index]] = instantiatedType.Arguments[index];
        }
        return bindings;
    }

    private static HobType InstantiateUnionFieldType(UnionSymbol union, HobType instantiatedType, HobType fieldType) =>
        union.TypeParameters.Count == 0
            ? fieldType
            : SubstituteType(fieldType, UnionTypeBindings(union, instantiatedType), preserveUnboundTypeParameters: true);

    private HobType ResolveType(TypeSyntax syntax, int depth) => ResolveType(syntax, depth, null);

    private HobType ResolveType(
        TypeSyntax syntax,
        int depth,
        IReadOnlyDictionary<string, HobType>? typeParameters)
    {
        if (depth >= MaximumSemanticDepth)
        {
            if (!_semanticDepthReported)
            {
                Add("E_TYPE_MISMATCH", "Type nesting exceeds the semantic checker limit", syntax.At);
                _semanticDepthReported = true;
            }
            return HobType.Error;
        }

        var reference = syntax.Reference;
        var name = reference.Declaration;
        if (!reference.IsQualified && name != "Unit" && typeParameters is not null &&
            typeParameters.TryGetValue(name, out var typeParameter))
            return NoTypeArguments(syntax, typeParameter);

        if (!reference.IsQualified && _resolvingNewtypeRepresentation)
        {
            switch (name)
            {
                case "Bytes":
                    return NoTypeArguments(syntax, HobType.Bytes);
                case "BytesError":
                    return NoTypeArguments(syntax, HobType.BytesError);
                case "ArithmeticError":
                    return NoTypeArguments(syntax, HobType.ArithmeticError);
                case "Config":
                    return NoTypeArguments(syntax, HobType.Config);
                case "Secrets":
                    return NoTypeArguments(syntax, HobType.Secrets);
                case "Logger":
                    return NoTypeArguments(syntax, HobType.Logger);
                case "ProcessRunner":
                    return NoTypeArguments(syntax, HobType.ProcessRunner);
                case "Secret":
                    if (syntax.Args.Count != 1)
                    {
                        Add("E_TYPE_MISMATCH", $"Type 'Secret' expects 1 type argument, got {syntax.Args.Count}", syntax.At);
                        return HobType.Error;
                    }

                    var secretValueType = ResolveType(syntax.Args[0], depth + 1, typeParameters);
                    if (secretValueType.IsError)
                        return HobType.Error;
                    if (!secretValueType.IsText)
                    {
                        Add("E_TYPE_MISMATCH", "Only Secret<Text> is supported", syntax.At);
                        return HobType.Error;
                    }
                    return HobType.SecretText;
                case "ProcessOutput":
                    return NoTypeArguments(syntax, HobType.ProcessOutput);
                case "ProcessError":
                    return NoTypeArguments(syntax, HobType.ProcessError);
                case "Transaction":
                    return NoTypeArguments(syntax, HobType.Transaction);
            }
        }

        if (!reference.IsQualified)
        {
            switch (name)
            {
            case "i32":
                return NoTypeArguments(syntax, HobType.I32);
            case "i64":
                return NoTypeArguments(syntax, HobType.I64);
            case "u32":
                return NoTypeArguments(syntax, HobType.U32);
            case "u64":
                return NoTypeArguments(syntax, HobType.U64);
            case "f64":
                return NoTypeArguments(syntax, HobType.F64);
            case "ArithmeticError":
                return NoTypeArguments(syntax, HobType.ArithmeticError);
            case "Unit":
                return NoTypeArguments(syntax, HobType.Unit);
            case "bool":
                return NoTypeArguments(syntax, HobType.Bool);
            case "Text":
                return NoTypeArguments(syntax, HobType.Text);
            case "Bytes":
                return NoTypeArguments(syntax, HobType.Bytes);
            case "BytesError":
                return NoTypeArguments(syntax, HobType.BytesError);
            case "Html":
                return NoTypeArguments(syntax, HobType.Html);
            case "FilePath":
                return NoTypeArguments(syntax, HobType.FilePath);
            case "FsRead":
                return NoTypeArguments(syntax, HobType.FsRead);
            case "FsWrite":
                return NoTypeArguments(syntax, HobType.FsWrite);
            case "Config":
                return ResolveRootOnlyConfigType(syntax, HobType.Config);
            case "Secrets":
                return ResolveRootOnlyConfigType(syntax, HobType.Secrets);
            case "Logger":
                return ResolveRootOnlyConfigType(syntax, HobType.Logger);
            case "ProcessRunner":
                return ResolveRootOnlyProcessType(syntax, HobType.ProcessRunner);
            case "Secret":
                if (syntax.Args.Count != 1)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'Secret' expects 1 type argument, got {syntax.Args.Count}", syntax.At);
                    return HobType.Error;
                }

                var secretValueType = ResolveType(syntax.Args[0], depth + 1, typeParameters);
                if (secretValueType.IsError)
                    return HobType.Error;
                if (!secretValueType.IsText)
                {
                    Add("E_TYPE_MISMATCH", "Only Secret<Text> is supported", syntax.At);
                    return HobType.Error;
                }
                return ResolveRootOnlyConfigType(syntax, HobType.SecretText, checkTypeArguments: false);
            case "FsError":
                return NoTypeArguments(syntax, HobType.FsError);
            case "ProcessOutput":
                return ResolveRootOnlyProcessType(syntax, HobType.ProcessOutput);
            case "ProcessError":
                return ResolveRootOnlyProcessType(syntax, HobType.ProcessError);
            case "HttpClient":
                return NoTypeArguments(syntax, HobType.HttpClient);
            case "HttpResponse":
                return NoTypeArguments(syntax, HobType.HttpResponse);
            case "HttpError":
                return NoTypeArguments(syntax, HobType.HttpError);
            case "DbRead":
                return NoTypeArguments(syntax, HobType.DbRead);
            case "DbWrite":
                return NoTypeArguments(syntax, HobType.DbWrite);
            case "Transaction":
                Add(
                    "E_RESOURCE_ESCAPE",
                    "Transaction handles cannot appear in source type positions; use them only as direct receivers inside their with scope",
                    syntax.At);
                return HobType.Error;
            case "DbError":
                return NoTypeArguments(syntax, HobType.DbError);
            case "Option":
                if (syntax.Args.Count != 1)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'Option' expects 1 type argument, got {syntax.Args.Count}", syntax.At);
                    return HobType.Error;
                }
                return HobType.Option(ResolveType(syntax.Args[0], depth + 1, typeParameters));
            case "List":
                if (syntax.Args.Count != 1)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'List' expects 1 type argument, got {syntax.Args.Count}", syntax.At);
                    return HobType.Error;
                }
                var listItemType = ResolveType(syntax.Args[0], depth + 1, typeParameters);
                return HobType.List(listItemType);
            case "Map":
                if (syntax.Args.Count != 2)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'Map' expects 2 type arguments, got {syntax.Args.Count}", syntax.At);
                    return HobType.Error;
                }

                var mapKeyType = ResolveType(syntax.Args[0], depth + 1, typeParameters);
                var mapValueType = ResolveType(syntax.Args[1], depth + 1, typeParameters);
                if (mapKeyType.IsError || mapValueType.IsError)
                    return HobType.Error;
                if (!mapKeyType.IsText)
                {
                    Add("E_TYPE_MISMATCH", $"Map keys must have type Text, found '{mapKeyType.DisplayName}'", syntax.Args[0].At);
                    return HobType.Error;
                }
                return HobType.Map(mapKeyType, mapValueType);
            case "Result":
                if (syntax.Args.Count != 2)
                {
                    Add("E_TYPE_MISMATCH", $"Type 'Result' expects 2 type arguments, got {syntax.Args.Count}", syntax.At);
                    return HobType.Error;
                }
                return HobType.Result(
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
            return HobType.Error;
        }

        var (union, structure, newtype) = ResolveTypeDeclaration(reference);
        if (union is not null)
        {
            if (syntax.Args.Count != union.TypeParameters.Count)
            {
                Add(
                    "E_TYPE_MISMATCH",
                    $"Union type '{name}' expects {union.TypeParameters.Count} type arguments, got {syntax.Args.Count}",
                    syntax.At);
                return HobType.Error;
            }

            if (union.TypeParameters.Count == 0)
                return union.Type;

            var arguments = syntax.Args.Select(argument => ResolveType(argument, depth + 1, typeParameters)).ToArray();
            return arguments.Any(ContainsError)
                ? HobType.Error
                : HobType.ForUnion(union.Id, union.Declaration.Name, arguments);
        }
        if (structure is not null)
        {
            if (syntax.Args.Count != structure.TypeParameters.Count)
            {
                Add(
                    "E_TYPE_MISMATCH",
                    $"Struct type '{name}' expects {structure.TypeParameters.Count} type arguments, got {syntax.Args.Count}",
                    syntax.At);
                return HobType.Error;
            }

            if (structure.TypeParameters.Count == 0)
                return structure.Type;

            var arguments = syntax.Args.Select(argument => ResolveType(argument, depth + 1, typeParameters)).ToArray();
            return arguments.Any(ContainsError)
                ? HobType.Error
                : HobType.ForStruct(structure.Id, structure.Declaration.Name, arguments);
        }
        if (newtype is not null)
        {
            if (syntax.Args.Count != 0)
            {
                Add("E_TYPE_MISMATCH", $"Newtype type '{name}' does not take type arguments", syntax.At);
                return HobType.Error;
            }
            return newtype.Type;
        }
        return HobType.Error;
    }

    private HobType NoTypeArguments(TypeSyntax syntax, HobType type)
    {
        if (syntax.Args.Count != 0)
        {
            Add("E_TYPE_MISMATCH", $"Type '{syntax.Reference.Declaration}' does not take type arguments", syntax.At);
            return HobType.Error;
        }
        return type;
    }

    private HobType ResolveRootOnlyConfigType(TypeSyntax syntax, HobType type, bool checkTypeArguments = true)
    {
        if (CurrentModule.PackageId != _rootPackageId)
        {
            Add("E_CAPABILITY_SCOPE", $"Type '{type.DisplayName}' is available only in the root package", syntax.At);
            return HobType.Error;
        }
        return checkTypeArguments ? NoTypeArguments(syntax, type) : type;
    }

    private HobType ResolveRootOnlyProcessType(TypeSyntax syntax, HobType type)
    {
        if (!CheckProcessTypeScope(type.DisplayName, syntax.At))
            return HobType.Error;

        return NoTypeArguments(syntax, type);
    }

    private bool CheckProcessTypeScope(string typeName, Token at)
    {
        if (CurrentModule.PackageId == _rootPackageId && _rootIsCliPackage)
            return true;

        Add("E_CAPABILITY_SCOPE", $"Type '{typeName}' is available only in the root CLI package", at);
        return false;
    }

    private static bool IsFsErrorVariant(string name) =>
        name is "NotFound" or "PermissionDenied" or "InvalidPath" or "InvalidText" or "Io";

    private static bool IsDbErrorVariant(string name) =>
        name is "Statement" or "RowShape";

    private static bool IsHttpErrorVariant(string name) =>
        name is "InvalidTarget" or "Transport" or "Timeout" or "ResponseTooLarge" or "InvalidText";

    private static bool IsProcessErrorVariant(string name) =>
        name is "InvalidArgument" or "InputTooLarge" or "OutputTooLarge" or "InvalidText" or "StartFailed" or "TimedOut";


    private void AddMismatch(HobType expected, HobType actual, Token at) =>
        Add("E_TYPE_MISMATCH", $"Expected '{expected.DisplayName}', found '{actual.DisplayName}'", at);


    private void AddMismatch(HobType expected, string actual, Token at) =>
        Add("E_TYPE_MISMATCH", $"Expected '{expected.DisplayName}', found '{actual}'", at);
    private void Add(string code, string message, Token at) =>
        diagnostics.Add(new Diagnostic(code, message, at.File, at.Range));

    private ModuleSymbols CurrentModule => _modulesByIdentity[_currentModule];

    private TypedExpr UnsupportedExpr(Expr expression) { Add("E_UNSUPPORTED", "Expression is not implemented in this language slice", expression.At); return new TypedErrorExpr(expression.At); }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());

    private sealed class ModuleSymbols(PackageModuleInput input)
    {
        public string PackageId { get; } = input.PackageId;
        public string? ManagedAdapterBridgeId { get; } = input.ManagedAdapterBridgeId;
        public ParsedProgram Program { get; } = input.Program;
        public ModuleIdentity Identity { get; } = new(input.PackageId, input.Program.Module);
        public IReadOnlyDictionary<string, string> DirectDependencies { get; } = input.DirectDependencies
            .ToDictionary(dependency => dependency.Key, dependency => dependency.Value, StringComparer.Ordinal);
        public HashSet<string> TypeNames { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, UnionSymbol> DeclaredUnions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, StructSymbol> DeclaredStructs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, NewtypeSymbol> DeclaredNewtypes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, TraitSymbol> DeclaredTraits { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, FunctionSymbol> DeclaredFunctions { get; } = new(StringComparer.Ordinal);
        public CommandSymbol? Command { get; set; }
    }

    private sealed class UnionSymbol(
        int id,
        ModuleIdentity moduleIdentity,
        UnionDecl declaration,
        HobType type,
        IReadOnlyList<HobType> typeParameters,
        IReadOnlyDictionary<string, HobType> typeParametersByName)
    {
        public int Id { get; } = id;
        public ModuleIdentity ModuleIdentity { get; } = moduleIdentity;
        public string PackageId => ModuleIdentity.PackageId;
        public string Module => ModuleIdentity.ModuleName;
        public UnionDecl Declaration { get; } = declaration;
        public HobType Type { get; } = type;
        public IReadOnlyList<HobType> TypeParameters { get; } = typeParameters;
        public IReadOnlyDictionary<string, HobType> TypeParametersByName { get; } = typeParametersByName;
        public List<CheckedVariant> Variants { get; } = [];
    }

    private sealed class StructSymbol(
        int id,
        ModuleIdentity moduleIdentity,
        StructDecl declaration,
        HobType type,
        IReadOnlyList<HobType>? typeParameters = null,
        IReadOnlyDictionary<string, HobType>? typeParametersByName = null)
    {
        public int Id { get; } = id;
        public ModuleIdentity ModuleIdentity { get; } = moduleIdentity;
        public string PackageId => ModuleIdentity.PackageId;
        public string Module => ModuleIdentity.ModuleName;
        public StructDecl Declaration { get; } = declaration;
        public HobType Type { get; } = type;
        public IReadOnlyList<HobType> TypeParameters { get; } = typeParameters ?? [];
        public IReadOnlyDictionary<string, HobType> TypeParametersByName { get; } =
            typeParametersByName ?? new Dictionary<string, HobType>(StringComparer.Ordinal);
        public List<CheckedStructField> Fields { get; } = [];
    }

    private sealed class NewtypeSymbol(int id, ModuleIdentity moduleIdentity, NewtypeDecl declaration, HobType type)
    {
        public int Id { get; } = id;
        public ModuleIdentity ModuleIdentity { get; } = moduleIdentity;
        public string PackageId => ModuleIdentity.PackageId;
        public string Module => ModuleIdentity.ModuleName;
        public NewtypeDecl Declaration { get; } = declaration;
        public HobType Type { get; } = type;
        public HobType Representation { get; set; } = HobType.Error;
    }

    private sealed class TraitSymbol(
        int id,
        ModuleIdentity moduleIdentity,
        TraitDecl declaration,
        HobType selfType)
    {
        public int Id { get; } = id;
        public ModuleIdentity ModuleIdentity { get; } = moduleIdentity;
        public string PackageId => ModuleIdentity.PackageId;
        public string Module => ModuleIdentity.ModuleName;
        public TraitDecl Declaration { get; } = declaration;
        public HobType SelfType { get; } = selfType;
        public List<CheckedTraitMethod> Methods { get; } = [];

        public CheckedTrait ToCheckedTrait() => new(
            Id,
            PackageId,
            Module,
            Declaration.Name,
            Declaration.Public,
            SelfType,
            ReadOnly(Methods),
            Declaration.At);
    }

    private sealed record TraitImplSymbol(
        CheckedTraitImpl Checked,
        TraitSymbol Trait,
        IReadOnlyList<FunctionSymbol> BindingFunctions);

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
        public HobType ErrorType { get; set; } = HobType.Error;
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
        public IReadOnlyList<HobType> TypeParameters { get; set; } = [];
        public IReadOnlyList<IReadOnlyList<CheckedTraitBound>> TypeParameterBounds { get; set; } = [];
        public IReadOnlyDictionary<string, HobType> TypeParametersByName { get; set; } = new Dictionary<string, HobType>(StringComparer.Ordinal);
        public HobType ReturnType { get; set; } = HobType.Error;
        public IReadOnlyList<string> DeclaredEffects { get; set; } = [];
        public CheckedManagedAdapterBinding? AdapterBinding { get; set; }
        public List<FunctionCallSite> Calls { get; } = [];
        public List<DirectEffectCall> DirectEffects { get; } = [];
        public CheckedFunction? CheckedFunction { get; set; }
    }

    private sealed record FunctionCallSite(FunctionSymbol Target, Token At);
    private sealed record DirectEffectCall(string Effect, string IntrinsicName, Token At);

    private sealed record LocalSymbol(int Id, HobType Type, bool IsMutable = false);

    private int _nextLocalId;
}

internal sealed record TypedErrorExpr(Token At) : TypedExpr(HobType.Error, At);
