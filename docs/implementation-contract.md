# M1 pure types implementation contract

This contract covers the next bounded, single-file pure-language slice. It does not complete the full M1 gate in the PRD. The compiler must continue to reject unsupported constructs. Existing active fixtures remain active.

## File ownership

- **Frontend/model:** `src/Al/Syntax.cs` and `src/Al/Parser.cs`. Own lexer, parser, source AST, `Range`, `Diagnostic`, and `Token`. Do not edit driver, semantic checker, or emitter files.
- **Semantics/backend:** `src/Al/Semantics.cs` and `src/Al/Emitter.cs`. Own typed IR, `Compiler.Check`, and C# emission. Do not edit frontend or driver files.
- **Driver/tests:** `src/Al/Program.cs`, fixtures, examples, README, and `docs/diagnostics.md`. Remove the old definitions transferred to frontend and semantics. Keep the CLI entry point and fixture runner. Do not edit frontend or semantics files.

These files share a namespace (the current global namespace). The driver owner must remove the old types in `Program.cs` when new files are ready, without changing their public handoff names. Coordinating that one deletion avoids duplicate definitions during parallel work.

## Exact source AST handoff

Use these record names, constructor argument names, order, and types. Lists may be passed as `List<T>` to `IReadOnlyList<T>` parameters. `Token.Range` remains a one-based range with an exclusive end. `Diagnostic` retains its current JSON shape and `Severity => "error"`.

```csharp
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
    string Name, bool Public, IReadOnlyList<ParameterDecl> Parameters,
    TypeSyntax ReturnType, IReadOnlyList<Stmt> Body, Token At);
internal sealed record ParsedProgram(
    string Module, IReadOnlyList<UnionDecl> Unions, IReadOnlyList<FunctionDecl> Functions);

internal abstract record Expr(Token At);
internal sealed record NumberExpr(Token At, int Value) : Expr(At);
internal sealed record BoolExpr(Token At, bool Value) : Expr(At);
internal sealed record TextExpr(Token At, string Value) : Expr(At);
internal sealed record NameExpr(Token At, string Name) : Expr(At);
internal sealed record BinaryExpr(Token At, string Op, Expr Left, Expr Right) : Expr(At);
internal sealed record CallExpr(Token At, string Name, IReadOnlyList<Expr> Arguments) : Expr(At);
internal sealed record VariantExpr(
    Token At, string UnionName, string VariantName, IReadOnlyList<Expr> Arguments) : Expr(At);
internal sealed record MatchExpr(
    Token At, Expr Value, IReadOnlyList<MatchArm> Arms) : Expr(At);
internal sealed record MatchArm(Pattern Pattern, Expr Body, Token At);
internal abstract record Pattern(Token At);
internal sealed record VariantPattern(
    Token At, string? UnionName, string VariantName,
    IReadOnlyList<string> Bindings) : Pattern(At);
internal sealed record WildcardPattern(Token At) : Pattern(At);

internal abstract record Stmt(Token At);
internal sealed record LetStmt(Token At, string Name, TypeSyntax Type, Expr Value) : Stmt(At);
internal sealed record ReturnStmt(Token At, Expr Value) : Stmt(At);
```

Keep `Lexer.Scan(string source, string file, List<Diagnostic> diagnostics)` and `new Parser(tokens, file, diagnostics).Parse()` returning `ParsedProgram?`. The parser reports syntax failures as diagnostics, not exceptions to the driver. It accepts the already implemented subset plus `bool` literals, quoted `Text` literals, nested types (`Option<i32>`, `Result<Text, E>`), union declarations with zero, positional (`Value(i32)`), or named (`TooLong(max: i32)`) payload fields, variant construction, and match expressions with `=>` arms. A variant may not mix positional and named fields. It continues to require explicit `effects {}` on functions. No user generic declarations or nonempty effects in this stage.

Parse `Choice.Yes` as `VariantExpr(..., "Choice", "Yes", [])` and `Choice.Value(3)` as `VariantExpr(..., "Choice", "Value", [NumberExpr])`. Parse `Some(3)`, `Ok(3)`, and `Err(x)` as `CallExpr`; parse bare `None` as `NameExpr`. The semantic checker may resolve these short forms as builtin constructors only when an expected `Option<T>` or `Result<T,E>` type is available from an explicit local annotation, return type, or call parameter. A function with the same name takes precedence; ambiguous or context-free construction is a diagnostic. Syntax `Option<i32>.Some` and `Result<T,E>.Ok` is deferred. A match arm accepts `Choice.Yes`, `Choice.Value(value)`, `Some(value)`, `None`, `Ok(value)`, `Err(error)`, or `_`; the parser emits `VariantPattern` with null `UnionName` for short builtin names. Pattern bindings are identifiers; payload field names belong to the union declaration and need not match bindings.

## Checker and backend handoff

Keep Compiler.Check(string file, string source) -> CheckResult, CheckResult(CheckedProgram? Program, List<Diagnostic> Diagnostics), and Emitter.Emit(CheckedProgram program, bool executable = true) -> string. On any diagnostic, Program is null. CheckedProgram contains Module, Functions, and Unions; its function collection contains CheckedFunction values. CheckedFunction exposes Name, Parameters, and ReturnType where ReturnType is a resolved semantic type with DisplayName (for example i32 or Option<i32>) and IsI32, IsBool, and IsText. The driver recognizes an executable entrypoint by name main, zero parameters, and a return type whose resolved semantic type reports IsI32, IsBool, or IsText. The emitter consumes only typed IR, never Expr, Stmt, or TypeSyntax source nodes. Every typed expression carries its resolved type and source token; typed variant and match nodes carry resolved union/variant identities and bound payload locals.

The checker supports `i32`, `bool`, `Text`, declared non-generic unions, and compiler-known `Option<T>` and `Result<T,E>` instantiated with supported types. It checks exact assignment/return/argument types, arity, payload types, match scrutinee type, duplicate or invalid variants, arm result type agreement, and exhaustiveness. A source wildcard covers remaining variants only when explicitly written. Arithmetic `+`, `-`, `*` accepts only `i32` and emitted arithmetic remains checked. `null` yields `E_TYPE_MISMATCH`; unknown types or names fail compilation. `E_MATCH_NONEXHAUSTIVE` identifies each omitted variant at the match span. Type errors use `E_TYPE_MISMATCH` with expected and actual type names. The backend emits deterministic C# from resolved semantic types and does not infer types from source spelling.

## Driver contract

All check, build, and run commands call Compiler.Check before doing backend work. al check FILE --json returns schema version 1 with an empty diagnostics array on success; --json is check-only. Missing files and expected file/process failures produce diagnostics instead of an unhandled exception.

al build FILE emits an executable if the checked program contains a supported zero-argument main returning i32, bool, or Text. Otherwise it emits a library, including when a function named main has a different signature. It stages generated projects in unique temporary directories, removes staging output after the build, and copies durable artifacts to a unique out/<source-stem>-<id>/ directory beside the source file.

al run FILE requires the supported main signature. The generated host writes one line containing the returned i32, lowercase bool, or Text value and exits 0. Checked i32 arithmetic overflow prints a generic runtime-fault message to stderr and exits 70. Diagnostics include E_ENTRYPOINT, E_IO, E_PROCESS, E_BUILD, E_TYPE_VISIBILITY, and E_MATCH_ARM_DUPLICATE.
## Acceptance for this slice

Activate fixtures 11–13 and 20, preserving 01–10; keep fixtures 14–19 pending. Add an active wrong-payload-type fixture. Add a runnable positive example using a union payload, bool, Text, Option, Result, and match where each implemented construct is exercised. Verify exact active fixture diagnostic lists, JSON diagnostics for type and match failures, a library build without a supported main, and both runnable examples, including generated C# build and execution. User generic functions, imports/modules, traits, effects, packages, and the complete M1 library acceptance follow in separate stages.
