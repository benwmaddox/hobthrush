internal sealed record Range(int StartLine, int StartColumn, int EndLine, int EndColumn);

internal sealed record Diagnostic(string Code, string Message, string File, Range Range)
{
    public string Severity => "error";
}

internal sealed record Token(string Kind, string Text, int Line, int Column, string File)
{
    public Range Range => new(Line, Column, Line, Column + Math.Max(Text.Length, 1));
}

internal sealed record SourceDeclarationRefSyntax(
    string? Root,
    IReadOnlyList<string> Module,
    string Declaration,
    Token At)
{
    public bool IsQualified => Root is not null;
}

internal sealed record TypeSyntax(SourceDeclarationRefSyntax Reference, IReadOnlyList<TypeSyntax> Args, Token At);
internal sealed record TypeParameterSyntax(string Name, Token At);
internal sealed record ParameterDecl(string Name, TypeSyntax Type, Token At);
internal sealed record EffectSyntax(string Name, Token At);
internal sealed record VariantFieldDecl(string? Name, TypeSyntax Type, Token At);
internal sealed record VariantDecl(string Name, IReadOnlyList<VariantFieldDecl> Fields, Token At);
internal sealed record UnionDecl(string Name, bool Public, IReadOnlyList<VariantDecl> Variants, Token At);
internal sealed record StructFieldDecl(string Name, TypeSyntax Type, Token At);
internal sealed record StructDecl(string Name, bool Public, IReadOnlyList<StructFieldDecl> Fields, Token At);
internal abstract record CommandEntrySyntax(Token At);
internal sealed record CommandHelpSyntax(Token At, string Text) : CommandEntrySyntax(At);
internal sealed record CommandArgumentSyntax(Token At, string Name, TypeSyntax Type, string Help) : CommandEntrySyntax(At);
internal sealed record CommandOptionSyntax(
    Token At,
    string Name,
    TypeSyntax Type,
    CommandLiteralSyntax Default,
    string Help) : CommandEntrySyntax(At);
internal sealed record CommandFlagSyntax(Token At, string Name, string Help) : CommandEntrySyntax(At);
internal sealed record CommandHandlerSyntax(Token At, SourceDeclarationRefSyntax Reference) : CommandEntrySyntax(At);
internal sealed record CommandErrorSyntax(Token At, SourceDeclarationRefSyntax Reference) : CommandEntrySyntax(At);
internal abstract record CommandLiteralSyntax(Token At);
internal sealed record CommandTextLiteralSyntax(Token At, string Value) : CommandLiteralSyntax(At);
internal sealed record CommandIntegerLiteralSyntax(Token At, int Value) : CommandLiteralSyntax(At);
internal sealed record CommandBooleanLiteralSyntax(Token At, bool Value) : CommandLiteralSyntax(At);
internal sealed record CommandDecl(string Name, IReadOnlyList<CommandEntrySyntax> Entries, Token At);
internal abstract record RouteItemSyntax(Token At);
internal sealed record RouteBodySyntax(Token At, TypeSyntax Type) : RouteItemSyntax(At);
internal sealed record RouteHandlerSyntax(Token At, SourceDeclarationRefSyntax Reference) : RouteItemSyntax(At);
internal sealed record RouteResponseSyntax(
    Token At,
    Token VariantAt,
    string Variant,
    Token StatusAt,
    int StatusCode,
    Token? FormatAt,
    string? Format,
    TypeSyntax? BodyType) : RouteItemSyntax(At);
internal sealed record RouteDecl(
    Token At,
    Token MethodAt,
    string Method,
    Token PathAt,
    string Path,
    IReadOnlyList<RouteItemSyntax> Items);
internal sealed record FunctionDecl(
    string Name,
    IReadOnlyList<TypeParameterSyntax> TypeParameters,
    bool Public,
    IReadOnlyList<ParameterDecl> Parameters,
    TypeSyntax ReturnType,
    IReadOnlyList<EffectSyntax> Effects,
    IReadOnlyList<Stmt> Body,
    Token At);
internal sealed record ParsedProgram(
    string Module,
    Token ModuleAt,
    string File,
    IReadOnlyList<UnionDecl> Unions,
    IReadOnlyList<FunctionDecl> Functions,
    IReadOnlyList<StructDecl> Structs,
    IReadOnlyList<TestDecl> Tests,
    IReadOnlyList<CommandDecl> Commands,
    IReadOnlyList<RouteDecl> Routes);

internal sealed record TestDecl(
    string Name,
    IReadOnlyList<LetStmt> Setup,
    Expr Assertion,
    Token At,
    Token NameAt,
    Token AssertAt);

internal abstract record Expr(Token At);
internal sealed record NumberExpr(Token At, int Value) : Expr(At);
internal sealed record BoolExpr(Token At, bool Value) : Expr(At);
internal sealed record TextExpr(Token At, string Value) : Expr(At);
internal sealed record ListExpr(Token At, IReadOnlyList<Expr> Items) : Expr(At);
internal sealed record NameExpr(Token At, string Name) : Expr(At);
internal sealed record BinaryExpr(Token At, string Op, Expr Left, Expr Right) : Expr(At);
internal sealed record DeclarationRefExpr(Token At, SourceDeclarationRefSyntax Reference) : Expr(At);
internal sealed record CallExpr(Token At, SourceDeclarationRefSyntax Reference, IReadOnlyList<Expr> Arguments) : Expr(At);
internal sealed record MemberCallExpr(
    Token At,
    Expr Target,
    string Member,
    Token MemberAt,
    IReadOnlyList<Expr> Arguments) : Expr(At);
internal sealed record StructFieldValue(string Name, Expr Value, Token At);
internal sealed record StructConstructExpr(
    Token At,
    SourceDeclarationRefSyntax Reference,
    IReadOnlyList<StructFieldValue> Fields) : Expr(At);
internal sealed record FieldAccessExpr(Token At, Expr Target, string Field) : Expr(At);
internal sealed record MatchExpr(Token At, Expr Value, IReadOnlyList<MatchArm> Arms) : Expr(At);
internal sealed record MatchArm(Pattern Pattern, Expr Body, Token At);

internal abstract record Pattern(Token At);
internal sealed record VariantPattern(
    Token At,
    SourceDeclarationRefSyntax? Union,
    string VariantName,
    IReadOnlyList<string> Bindings) : Pattern(At);
internal sealed record WildcardPattern(Token At) : Pattern(At);

internal abstract record Stmt(Token At);
internal sealed record LetStmt(Token At, string Name, TypeSyntax Type, Expr Value) : Stmt(At);
internal sealed record VarStmt(Token At, string Name, Token NameAt, TypeSyntax Type, Expr Value) : Stmt(At);
internal sealed record AssignmentStmt(Token At, string Name, Token NameAt, Expr Value) : Stmt(At);
internal sealed record ReturnStmt(Token At, Expr Value) : Stmt(At);
internal sealed record ForStmt(
    Token At,
    string Name,
    Token NameAt,
    Expr Collection,
    IReadOnlyList<Stmt> Body) : Stmt(At);
internal sealed record IfStmt(
    Token At,
    Expr Condition,
    IReadOnlyList<Stmt> Then,
    IReadOnlyList<Stmt> Else) : Stmt(At);
internal sealed record WithTransactionStmt(
    Token At,
    Expr Begin,
    string Name,
    Token NameAt,
    IReadOnlyList<Stmt> Body) : Stmt(At);
