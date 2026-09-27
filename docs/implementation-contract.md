# M1 pure types implementation contract

This contract covers the next bounded, single-file pure-language slice. It does not complete the full M1 gate in the PRD. The compiler must continue to reject unsupported constructs. Existing active fixtures remain active.

## File ownership

- **Frontend/model:** `src/Lang/Syntax.cs` and `src/Lang/Parser.cs`. Own lexer, parser, source AST, `Range`, `Diagnostic`, and `Token`. Do not edit driver, semantic checker, or emitter files.
- **Semantics/backend:** `src/Lang/Semantics.cs` and `src/Lang/Emitter.cs`. Own typed IR, `Compiler.Check`, and C# emission. Do not edit frontend or driver files.
- **Driver/tests:** `src/Lang/Program.cs`, fixtures, examples, README, and `docs/diagnostics.md`. Remove the old definitions transferred to frontend and semantics. Keep the CLI entry point and fixture runner. Do not edit frontend or semantics files.

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
internal sealed record StructFieldDecl(string Name, TypeSyntax Type, Token At);
internal sealed record StructDecl(string Name, bool Public, IReadOnlyList<StructFieldDecl> Fields, Token At);
internal sealed record FunctionDecl(
    string Name, bool Public, IReadOnlyList<ParameterDecl> Parameters,
    TypeSyntax ReturnType, IReadOnlyList<Stmt> Body, Token At);
internal sealed record ParsedProgram(
    string Module, IReadOnlyList<UnionDecl> Unions, IReadOnlyList<FunctionDecl> Functions,
    IReadOnlyList<StructDecl> Structs);

internal abstract record Expr(Token At);
internal sealed record NumberExpr(Token At, int Value) : Expr(At);
internal sealed record BoolExpr(Token At, bool Value) : Expr(At);
internal sealed record TextExpr(Token At, string Value) : Expr(At);
internal sealed record NameExpr(Token At, string Name) : Expr(At);
internal sealed record BinaryExpr(Token At, string Op, Expr Left, Expr Right) : Expr(At);
internal sealed record CallExpr(Token At, string Name, IReadOnlyList<Expr> Arguments) : Expr(At);
internal sealed record VariantExpr(
    Token At, string UnionName, string VariantName, IReadOnlyList<Expr> Arguments) : Expr(At);
internal sealed record StructFieldValue(string Name, Expr Value, Token At);
internal sealed record StructConstructExpr(
    Token At, string Name, IReadOnlyList<StructFieldValue> Fields) : Expr(At);
internal sealed record FieldAccessExpr(Token At, Expr Target, string Field) : Expr(At);
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

Parse `Choice.Value(3)` as `VariantExpr(..., "Choice", "Value", [NumberExpr])`. A no-call dotted expression such as `Choice.Yes` is `FieldAccessExpr(..., NameExpr("Choice"), "Yes")`; the semantic checker resolves it as a zero-payload union variant when the left name is not shadowed by a local. Parse `Some(3)`, `Ok(3)`, and `Err(x)` as `CallExpr`; parse bare `None` as `NameExpr`. The semantic checker may resolve these short forms as builtin constructors only when an expected `Option<T>` or `Result<T,E>` type is available from an explicit local annotation, return type, or call parameter. A function with the same name takes precedence; ambiguous or context-free construction is a diagnostic. Syntax `Option<i32>.Some` and `Result<T,E>.Ok` is deferred. A match arm accepts `Choice.Yes`, `Choice.Value(value)`, `Some(value)`, `None`, `Ok(value)`, `Err(error)`, or `_`; the parser emits `VariantPattern` with null `UnionName` for short builtin names. Pattern bindings are identifiers; payload field names belong to the union declaration and need not match bindings.

Identifier recognition is contextual. Any token of lexical kind `id` is a lexical identifier. The parser's bare-name helper accepts lexical identifiers except exactly `true`, `false`, `null`, `match`, `if`, `await`, and `with`. These seven retain their current literal, match, or unsupported-expression handling and are excluded from bare declaration/type-root/function/call/binding/unqualified-pattern names. Other keyword-like spellings, including `route`, `return`, `struct`, and `pub`, are allowed in ordinary name positions; grammar dispatch still recognizes syntax keywords in declaration and statement positions, so top-level route declarations remain unsupported.

Member-name positions use lexical identifiers without the bare-name exclusion: struct field declarations and initializer labels, union variant declarations and named payload labels, and identifiers after `.` in field access, variant construction, qualified types, and qualified patterns. Module path segments also use lexical identifiers. This policy permits forms such as `struct S { return: i32, route: Text }`, `value.if`, `Choice.null`, and `module route.if;`. A bare `null` remains a type mismatch rather than a name.

## Checker and backend handoff

Keep Compiler.Check(string file, string source) -> CheckResult, CheckResult(CheckedProgram? Program, List<Diagnostic> Diagnostics), and Emitter.Emit(CheckedProgram program, bool executable = true) -> string. On any diagnostic, Program is null. CheckedProgram contains Module, Functions, Unions, and Structs; its function collection contains CheckedFunction values. CheckedFunction exposes Name, Parameters, and ReturnType where ReturnType is a resolved semantic type with DisplayName (for example i32 or Option<i32>) and IsI32, IsBool, and IsText. The driver recognizes an executable entrypoint by name main, zero parameters, and a return type whose resolved semantic type reports IsI32, IsBool, or IsText. The emitter consumes only typed IR, never Expr, Stmt, or TypeSyntax source nodes. Every typed expression carries its resolved type and source token; typed variant and match nodes carry resolved union/variant identities and bound payload locals.

The checker supports `i32`, `bool`, `Text`, immutable non-generic nominal structs, declared non-generic unions, and compiler-known `Option<T>` and `Result<T,E>` instantiated with supported types. It checks exact assignment/return/argument types, arity, payload types, match scrutinee type, duplicate or invalid variants, arm result type agreement, and exhaustiveness. A source wildcard covers remaining variants only when explicitly written. Arithmetic `+`, `-`, `*` accepts only `i32` and emitted arithmetic remains checked. `null` yields `E_TYPE_MISMATCH`; unknown types or names fail compilation. `E_MATCH_NONEXHAUSTIVE` identifies each omitted variant at the match span. Type errors use `E_TYPE_MISMATCH` with expected and actual type names. The backend emits deterministic C# from resolved semantic types and does not infer types from source spelling.

## Driver contract

All check, build, and run commands call Compiler.Check before doing backend work. lang check FILE --json returns schema version 1 with an empty diagnostics array on success; --json is check-only. Missing files and expected file/process failures produce diagnostics instead of an unhandled exception.

lang build FILE emits an executable if the checked program contains a supported zero-argument main returning i32, bool, or Text. Otherwise it emits a library, including when a function named main has a different signature. It stages generated projects in unique temporary directories, removes staging output after the build, and copies durable artifacts to a unique out/<source-stem>-<id>/ directory beside the source file. The compiler and generated projects explicitly set `ServerGarbageCollection=false`, selecting Workstation GC.

The optional Native AOT deployment form is `lang build FILE_OR_PACKAGE --aot --rid RID`. It applies only to a checked executable application or CLI package with the supported entrypoint; library-only sources and packages and the compiler tool are not AOT targets. The supported RIDs are `win-x64` and `linux-x64`, and the build host OS must match the RID (`win-x64` on Windows, `linux-x64` on Linux). Cross-OS publishing is unsupported and reports `E_BUILD_TARGET`. A matching .NET Native AOT native toolchain is required. The SDK publish output is staged before the native executable is copied to the unique durable output directory as `Generated.exe` on Windows or `Generated` on Linux. Success prints `Built native executable: <absolute path>`; SDK publish output, including Native AOT compatibility warnings, is forwarded to stderr as unstructured text. A successful publish does not guarantee that no warnings were emitted. Normal `lang build FILE_OR_PACKAGE` and `lang run FILE_OR_PACKAGE` retain the managed output path. Native AOT remains GC-managed and does not change source semantics. Shared-library exports are outside this command and remain in the separate ABI1 proof.

Use `E_BUILD_TARGET` when a recognized `--aot` or `--rid` option is missing a required companion, malformed, or used outside the exact build form; when the RID is unsupported; or when the checked program lacks the supported executable entrypoint. The diagnostic messages are:

- Missing or malformed options after `--aot`: `lang build --aot requires --rid RID (supported RIDs: win-x64, linux-x64)`
- Unsupported RID: `Unsupported AOT runtime identifier '<rid>'; supported RIDs: win-x64, linux-x64`
- RID targets a different OS than the current build host: `NativeAOT runtime identifier '<rid>' targets a different OS than the current host; cross-OS publishing is not supported`
- AOT options used outside the exact form: `The --aot and --rid options are only valid with lang build FILE_OR_PACKAGE --aot --rid RID`
- No supported executable entrypoint: `lang build --aot requires fn main() -> i32, bool, or Text with no parameters`

General malformed CLI invocations that do not use a recognized AOT option keep normal usage handling.

lang run FILE requires the supported main signature. The generated host writes one line containing the returned i32, lowercase bool, or Text value and exits 0. Checked i32 arithmetic overflow prints a generic runtime-fault message to stderr and exits 70. Diagnostics include E_ENTRYPOINT, E_IO, E_PROCESS, E_BUILD, E_BUILD_TARGET, E_TYPE_VISIBILITY, and E_MATCH_ARM_DUPLICATE.
## Acceptance for this slice

Preserve active fixtures 01-13 and 20-25; fixtures 14-19 remain pending. The active set includes struct field type and initializer diagnostics, direct recursion rejection, and a valid recursive wrapper case. Keep the union/value examples runnable and verify exact active fixture diagnostic lists, JSON diagnostics for type and match failures, a library build without a supported main, and generated C# build and execution for the runnable examples. User generic functions, traits, effects, external package dependencies and lockfiles, and the complete M1 library acceptance follow in separate stages.

## Struct values contract addendum

This addendum extends and supersedes the `ParsedProgram` handoff shape above: its final constructor parameter is `IReadOnlyList<StructDecl> Structs`, after `Functions`. The rest of the existing parser and checker contracts remain in force.

The exact additional source AST records are:

```csharp
internal sealed record StructFieldDecl(string Name, TypeSyntax Type, Token At);
internal sealed record StructDecl(string Name, bool Public, IReadOnlyList<StructFieldDecl> Fields, Token At);
internal sealed record StructFieldValue(string Name, Expr Value, Token At);
internal sealed record StructConstructExpr(
    Token At, string Name, IReadOnlyList<StructFieldValue> Fields) : Expr(At);
internal sealed record FieldAccessExpr(Token At, Expr Target, string Field) : Expr(At);
```

Structs are immutable, nominal, and non-generic. Empty declarations and constructions are legal. Field declarations have unique names. Each construction names every declared field exactly once; extra or repeated initializers are rejected. The checker reports `E_FIELD_UNKNOWN` for an undeclared initializer or read, `E_FIELD_DUPLICATE` for a repeated initializer, `E_FIELD_MISSING` for an omitted field, and `E_TYPE_MISMATCH` for an incompatible field value. Initializer expressions are evaluated in source order, independent of declaration layout. Public structs and their public field types obey the existing public-type visibility checks.

A field read may follow a local, call result, parenthesized expression, or struct construction and may chain through nested structs. Methods are unsupported. A no-call dotted expression whose target is an unshadowed union type name remains the zero-payload union constructor form. Locals shadow type names in dotted field access and dotted-call syntax; local member calls are unsupported rather than treated as variant construction.

The parser does not consume a struct construction as an unparenthesized top-level match scrutinee, because the next `{` begins match arms. Parenthesize the construction explicitly; for example, `match (Box { value: Choice.Yes }).value { Choice.Yes => 1, Choice.No => 0 }`. Other match scrutinees retain their existing grammar.

Direct cycles made only of struct fields are rejected with `E_TYPE_MISMATCH`. A recursive path passing through `Option`, `Result`, or a tagged union is allowed. This is a direct-layout rule, not a general recursive-type or ownership system.

The acceptance set adds active fixtures 22-25 for recursive wrapper values, field type mismatch, duplicate/unknown/missing initializers, and bare recursion. The runnable `examples/structs` program exercises nested and reordered initializers, `Option`, a tagged union, and chained field reads. This slice does not complete M1 or the V1 promise; user-defined generics, traits, effects, capabilities, CLI/web features, and the maintained application examples remain separate work.

## Package modules contract addendum

Package commands accept a package directory: `lang check PACKAGE_DIRECTORY [--json]`, `lang build PACKAGE_DIRECTORY`, and `lang run PACKAGE_DIRECTORY`. The root manifest uses a strict schema with exactly `name`, `version`, `kind`, `source_root`, and (only for CLI packages) `entry_module`. Every value is a plain double-quoted string. Blank lines and comments outside quoted values are accepted; escapes are not. `kind` is `lib` or `cli`. The package name must be filesystem-safe, the version string must be non-empty, `source_root` must be a normalized forward-slash relative directory within the package, and `entry_module` must be a valid dotted module name. Libraries must omit `entry_module`; CLI packages must provide it. Manifest file, schema, key, and value errors report `E_MANIFEST`.

The loader recursively discovers `.lang` files under `source_root`. A relative path with the extension removed and directory separators replaced by dots must exactly match the source's `module` header. A mismatch, invalid module path, or duplicate module path reports `E_MODULE_PATH`. Each source has one module header, then zero or more imports, then declarations. An import names one module and a non-empty explicit list of symbols: `import text.validation { validate, Error };`. The target module and each symbol must resolve; only public symbols may be imported; duplicate imports and collisions with declarations report `E_IMPORT_CONFLICT`. These errors point to the importing file. An absent module or symbol reports `E_IMPORT_UNRESOLVED`; a private symbol reports `E_IMPORT_PRIVATE`. Imported unions participate in exhaustiveness checking, so adding a variant can produce `E_MATCH_NONEXHAUSTIVE` in an importing module. Imports are module-scoped and non-transitive. External package dependencies and lockfiles are not implemented.

For `kind = "cli"`, the named entry module must be discovered and must define exactly one supported zero-argument `main() -> i32|bool|Text`. A missing entry module reports `E_ENTRYPOINT` at `lang.toml`; a missing or unsupported `main` reports `E_ENTRYPOINT` at the entry module source; duplicate same-module declarations retain `E_NAME_DUPLICATE`. A `main` in a non-entry module does not select the package entrypoint. Libraries may contain ordinary functions named `main` but have no package entrypoint. Package builds use the managed backend by default; `lang build PACKAGE_DIRECTORY --aot --rid RID` follows the executable AOT rules above and is valid only for CLI packages with the supported entrypoint.

The maintained `examples/library-package` acceptance example contains a CLI entry module that imports a public function, struct, and union from another module in the same package. Integration acceptance also covers isolated private names, import visibility and conflicts, unresolved and non-transitive imports, module-path/header mismatches, manifest schema rejection, CLI entrypoint rules, JSON diagnostic file locations, imported-union exhaustiveness, managed package builds, and package AOT argument rejection. It does not imply external package dependency or lockfile support.
