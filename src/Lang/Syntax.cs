internal sealed record Range(int StartLine, int StartColumn, int EndLine, int EndColumn);

internal sealed record Diagnostic(string Code, string Message, string File, Range Range)
{
    public string Severity => "error";
}

internal sealed record Token(string Kind, string Text, int Line, int Column)
{
    public Range Range => new(Line, Column, Line, Column + Math.Max(Text.Length, 1));
}

internal sealed record TypeSyntax(string Name, IReadOnlyList<TypeSyntax> Args, Token At);
internal sealed record ParameterDecl(string Name, TypeSyntax Type, Token At);
internal sealed record VariantFieldDecl(string? Name, TypeSyntax Type, Token At);
internal sealed record VariantDecl(string Name, IReadOnlyList<VariantFieldDecl> Fields, Token At);
internal sealed record UnionDecl(string Name, bool Public, IReadOnlyList<VariantDecl> Variants, Token At);
internal sealed record FunctionDecl(
    string Name,
    bool Public,
    IReadOnlyList<ParameterDecl> Parameters,
    TypeSyntax ReturnType,
    IReadOnlyList<Stmt> Body,
    Token At);
internal sealed record ParsedProgram(
    string Module,
    IReadOnlyList<UnionDecl> Unions,
    IReadOnlyList<FunctionDecl> Functions);

internal abstract record Expr(Token At);
internal sealed record NumberExpr(Token At, int Value) : Expr(At);
internal sealed record BoolExpr(Token At, bool Value) : Expr(At);
internal sealed record TextExpr(Token At, string Value) : Expr(At);
internal sealed record NameExpr(Token At, string Name) : Expr(At);
internal sealed record BinaryExpr(Token At, string Op, Expr Left, Expr Right) : Expr(At);
internal sealed record CallExpr(Token At, string Name, IReadOnlyList<Expr> Arguments) : Expr(At);
internal sealed record VariantExpr(
    Token At,
    string UnionName,
    string VariantName,
    IReadOnlyList<Expr> Arguments) : Expr(At);
internal sealed record MatchExpr(Token At, Expr Value, IReadOnlyList<MatchArm> Arms) : Expr(At);
internal sealed record MatchArm(Pattern Pattern, Expr Body, Token At);

internal abstract record Pattern(Token At);
internal sealed record VariantPattern(
    Token At,
    string? UnionName,
    string VariantName,
    IReadOnlyList<string> Bindings) : Pattern(At);
internal sealed record WildcardPattern(Token At) : Pattern(At);

internal abstract record Stmt(Token At);
internal sealed record LetStmt(Token At, string Name, TypeSyntax Type, Expr Value) : Stmt(At);
internal sealed record ReturnStmt(Token At, Expr Value) : Stmt(At);
