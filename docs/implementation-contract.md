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
internal sealed record Token(string Kind, string Text, int Line, int Column, string File)
{
    public Range Range => new(Line, Column, Line, Column + Math.Max(Text.Length, 1));
}

internal sealed record SourceDeclarationRefSyntax(
    string? Root, IReadOnlyList<string> Module, string Declaration, Token At)
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
internal sealed record FunctionDecl(
    string Name, IReadOnlyList<TypeParameterSyntax> TypeParameters, bool Public,
    IReadOnlyList<ParameterDecl> Parameters, TypeSyntax ReturnType, IReadOnlyList<EffectSyntax> Effects,
    IReadOnlyList<Stmt> Body, Token At);
internal sealed record ParsedProgram(
    string Module, Token ModuleAt, string File, IReadOnlyList<UnionDecl> Unions,
    IReadOnlyList<FunctionDecl> Functions, IReadOnlyList<StructDecl> Structs, IReadOnlyList<TestDecl> Tests);
internal sealed record TestDecl(
    string Name, IReadOnlyList<LetStmt> Setup, Expr Assertion, Token At, Token NameAt, Token AssertAt);

internal abstract record Expr(Token At);
internal sealed record NumberExpr(Token At, int Value) : Expr(At);
internal sealed record BoolExpr(Token At, bool Value) : Expr(At);
internal sealed record TextExpr(Token At, string Value) : Expr(At);
internal sealed record NameExpr(Token At, string Name) : Expr(At);
internal sealed record BinaryExpr(Token At, string Op, Expr Left, Expr Right) : Expr(At);
internal sealed record DeclarationRefExpr(Token At, SourceDeclarationRefSyntax Reference) : Expr(At);
internal sealed record CallExpr(
    Token At, SourceDeclarationRefSyntax Reference, IReadOnlyList<Expr> Arguments) : Expr(At);
internal sealed record MemberCallExpr(
    Token At, Expr Target, string Member, Token MemberAt, IReadOnlyList<Expr> Arguments) : Expr(At);
internal sealed record StructFieldValue(string Name, Expr Value, Token At);
internal sealed record StructConstructExpr(
    Token At, SourceDeclarationRefSyntax Reference, IReadOnlyList<StructFieldValue> Fields) : Expr(At);
internal sealed record FieldAccessExpr(Token At, Expr Target, string Field) : Expr(At);
internal sealed record MatchExpr(
    Token At, Expr Value, IReadOnlyList<MatchArm> Arms) : Expr(At);
internal sealed record MatchArm(Pattern Pattern, Expr Body, Token At);
internal abstract record Pattern(Token At);
internal sealed record VariantPattern(
    Token At, SourceDeclarationRefSyntax? Union, string VariantName,
    IReadOnlyList<string> Bindings) : Pattern(At);
internal sealed record WildcardPattern(Token At) : Pattern(At);

internal abstract record Stmt(Token At);
internal sealed record LetStmt(Token At, string Name, TypeSyntax Type, Expr Value) : Stmt(At);
internal sealed record ReturnStmt(Token At, Expr Value) : Stmt(At);
internal sealed record IfStmt(
    Token At, Expr Condition, IReadOnlyList<Stmt> Then, IReadOnlyList<Stmt> Else) : Stmt(At);
```

Keep `Lexer.Scan(string source, string file, List<Diagnostic> diagnostics)` and `new Parser(tokens, file, diagnostics).Parse()` returning `ParsedProgram?`. The parser reports syntax failures as diagnostics, not exceptions to the driver. It accepts the implemented subset plus `bool` literals, quoted `Text` literals, nested types (`Option<i32>`, `Result<Text, E>`), function type-parameter declarations, union declarations with zero, positional (`Value(i32)`), or named (`TooLong(max: i32)`) payload fields, qualified declaration references, and match expressions with `=>` arms. A variant may not mix positional and named fields. It continues to require explicit `effects {}` on functions. Module paths and source declaration references use `::`; top-level `import` declarations are syntax errors.

The reusable source declaration reference is `SourceDeclarationRefSyntax(Root, Module, Declaration, At)`. Unqualified references have a null root and empty module list; qualified references have a root plus module segments and a final declaration segment. The same representation is carried by `TypeSyntax.Reference`, `CallExpr.Reference`, `StructConstructExpr.Reference`, and `DeclarationRefExpr.Reference`; qualified union patterns carry it in `VariantPattern.Union`. A qualified user union constructor is `self::app::main::Choice.Value(3)`; a zero-payload constructor is `self::app::main::Choice.Yes`, represented as a declaration reference followed by a member access. A match arm accepts `self::app::main::Choice.Yes`, `self::app::main::Choice.Value(value)`, `Some(value)`, `None`, `Ok(value)`, `Err(error)`, or `_`. The parser emits a `VariantPattern` with null `Union` for short builtin patterns. Unqualified `Some`, `None`, `Ok`, and `Err` always denote built-in constructors; a user function with one of those spellings requires a qualified reference and does not shadow the short form. Built-in constructors resolve only when an expected `Option<T>` or `Result<T,E>` type is available from an explicit local annotation, return type, or call parameter. Syntax `Option<i32>.Some` and `Result<T,E>.Ok` is deferred. Pattern bindings are identifiers; payload field names belong to the union declaration and need not match bindings.

Identifier recognition is contextual. Any token of lexical kind `id` is a lexical identifier. The parser's bare-name helper accepts lexical identifiers except exactly `true`, `false`, `null`, `match`, `if`, `await`, and `with`. These seven retain their current literal, match, or unsupported-expression handling and are excluded from bare declaration/type-root/function/call/binding/unqualified-pattern names. Other keyword-like spellings, including `route`, `return`, `struct`, and `pub`, are allowed in ordinary name positions; grammar dispatch still recognizes syntax keywords in declaration and statement positions, so top-level route declarations remain unsupported.

Member-name positions use lexical identifiers without the bare-name exclusion: struct field declarations and initializer labels, union variant declarations and named payload labels, and identifiers after `.` in field access, variant construction, and qualified patterns. Module and declaration-reference segments also use lexical identifiers. This policy permits forms such as `struct S { return: i32, route: Text }`, `value.if`, `self::app::main::Choice.null`, and `module route::if;`. A bare `null` remains a type mismatch rather than a name.

## Checker and backend handoff

Keep Compiler.Check(string file, string source) -> CheckResult, CheckResult(CheckedProgram? Program, List<Diagnostic> Diagnostics), and Emitter.Emit(CheckedProgram program, bool executable = true) -> string. On any diagnostic, Program is null. CheckedProgram contains Module, Functions, Unions, and Structs; its function collection contains CheckedFunction values. CheckedFunction exposes Name, Parameters, and ReturnType where ReturnType is a resolved semantic type with DisplayName (for example i32 or Option<i32>) and IsI32, IsBool, and IsText. The driver recognizes an executable entrypoint by name main, zero parameters, and a return type whose resolved semantic type reports IsI32, IsBool, or IsText. The emitter consumes only typed IR, never Expr, Stmt, or TypeSyntax source nodes. Every typed expression carries its resolved type and source token; typed variant and match nodes carry resolved union/variant identities and bound payload locals.

The checker supports `i32`, `bool`, `Text`, immutable non-generic nominal structs, declared non-generic unions, compiler-known `Option<T>` and `Result<T,E>`, and generic functions. Generic function type arguments are inferred from independently typed call arguments; each declared type parameter must occur in a function parameter type. The current slice has no explicit type arguments, generic structs or unions, traits, or constructor-driven inference. In particular, a direct `Some` or `None` expression passed to a generic call retains `E_TYPE_MISMATCH` because no concrete expected `Option<T>` is available. Generic parameters are valid as values and supported type-constructor arguments, but operations requiring a concrete type remain rejected. The checker also checks exact assignment/return/argument types, arity, payload types, match scrutinee type, duplicate or invalid variants, arm result type agreement, and exhaustiveness. A source wildcard covers remaining variants only when explicitly written. Arithmetic `+`, `-`, `*` accepts only `i32` and emitted arithmetic remains checked. Comparisons are `==`, `!=`, `<`, `<=`, `>`, and `>=`: equality accepts matching `i32`, `bool`, or `Text`, while ordering accepts `i32`; comparisons return `bool`. `if` conditions must be `bool`, branch scopes are independent, and a function is guaranteed to return only when a return statement is reached or both branches of an `if`/`else` return. `E_UNREACHABLE` marks a later statement after a guaranteed return. `Text.length` returns an `i32` Unicode scalar count, and `Text.trim()` removes leading and trailing Unicode whitespace per the pinned runtime. `null` yields `E_TYPE_MISMATCH`; unknown types or names fail compilation. A function that can fall through retains `E_TYPE_MISMATCH` with `Function must end with a return value`. `E_MATCH_NONEXHAUSTIVE` identifies each omitted variant at the match span. Type errors use `E_TYPE_MISMATCH` with expected and actual type names. The backend emits deterministic C# from resolved semantic types and does not infer types from source spelling.

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

Preserve active fixtures 01-14 and 20-42; fixtures 15-19 remain pending. The active set includes struct field and initializer diagnostics, direct recursion rejection, a valid recursive wrapper case, transitive effect upper-bound rejection, control-flow/Text acceptance, generic declaration and inference diagnostics, return and scope failures, comparison type errors, unreachable statements, and language-test parsing/type diagnostics. Keep the union/value examples runnable and verify exact active fixture diagnostic lists, JSON diagnostics for type and match failures, a library build without a supported main, and generated C# build and execution for the runnable examples. Generic structs and unions, explicit type arguments, traits, the remaining effect/capability requirements, Git and registry dependency sources, and the complete M1 library acceptance follow in separate stages.

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

A field read may follow a local, call result, parenthesized expression, or struct construction and may chain through nested structs. Methods are unsupported. A no-call dotted expression whose target is a qualified union declaration reference remains the zero-payload union constructor form. Locals shadow value names in dotted field access and dotted-call syntax; local member calls are unsupported rather than treated as variant construction.

The parser does not consume a struct construction as an unparenthesized top-level match scrutinee, because the next `{` begins match arms. Parenthesize the construction explicitly; for example, `match (self::app::main::Box { value: self::app::main::Choice.Yes }).value { self::app::main::Choice.Yes => 1, self::app::main::Choice.No => 0 }`. Other match scrutinees retain their existing grammar.

Direct cycles made only of struct fields are rejected with `E_TYPE_MISMATCH`. A recursive path passing through `Option`, `Result`, or a tagged union is allowed. This is a direct-layout rule, not a general recursive-type or ownership system.

The acceptance set adds active fixtures 22-25 for recursive wrapper values, field type mismatch, duplicate/unknown/missing initializers, and bare recursion. The runnable `examples/structs` program exercises nested and reordered initializers, `Option`, a tagged union, and chained field reads. This slice does not complete M1 or the V1 promise; generic structs and unions, explicit type arguments, traits, application capability grants, effect inspection and receipts, additional adapters, CLI/web features, and the maintained application examples remain separate work.

## Package modules contract addendum

Package commands accept a package directory: `lang check PACKAGE_DIRECTORY [--json]`, `lang build PACKAGE_DIRECTORY`, `lang run PACKAGE_DIRECTORY`, and `lang lock PACKAGE_DIRECTORY`. The root manifest requires `name`, `version`, `kind`, and `source_root`, plus `entry_module` only for CLI packages. An optional trailing `[dependencies]` table maps language identifier aliases to relative package paths, one assignment per line. Every value is a plain double-quoted string. Blank lines and comments outside quoted values are accepted; escapes are not. `kind` is `lib` or `cli`. The package name must be filesystem-safe, the version string must be non-empty, `source_root` must be a normalized forward-slash relative directory within the package, and `entry_module` must be a valid `::`-separated module name, such as `app::main`. Libraries must omit `entry_module`; CLI packages must provide it. The dependency alias `self` is reserved; remaining aliases are case-insensitively unique. Paths are resolved relative to their declaring manifest, and every target must be a library package. Different package roots cannot use the same name and version. Manifest file, schema, key, value, and invalid dependency path errors report `E_MANIFEST` or `E_DEPENDENCY`.

The loader recursively discovers `.lang` files under each package's `source_root`. A relative path with the extension removed and directory separators replaced by `::` must exactly match the source's `module` header. For example, `src/app/main.lang` declares `module app::main;`. A mismatch, invalid module path, or duplicate module path within the same package reports `E_MODULE_PATH`. Module identities include the package root, so equal module paths in different dependencies remain isolated. Each source has one module header followed by declarations; there is no source-level import declaration, and legacy top-level `import` syntax fails with `E_SYNTAX`. Every user declaration reference names a root, one or more module segments, and a declaration separated by `::`: `self::text::validation::normalize` selects the current package, while `validation::text::validation::normalize` selects a directly declared dependency alias. The alias `self` is reserved. User declarations must be qualified even within their declaring module; short forms remain available for built-ins, type parameters, and local values. Module and declaration segments use contextual identifier rules. The `.` separator remains for value fields, member operations, and union variants, such as `self::catalog::message::Message.Ready(value)`. A qualified reference to a private declaration outside its declaring module reports `E_ACCESS_PRIVATE`; an unknown alias, module, or declaration reports `E_NAME_UNRESOLVED`. Aliases are direct to one package and are not re-exported. Qualified union patterns participate in exhaustiveness checking, so adding a variant can produce `E_MATCH_NONEXHAUSTIVE`.

Path dependencies are local filesystem references and work offline. Git sources, registries, dependency caches, `lang add` or other package-install commands, and build receipts are not implemented. `lang lock` resolves the complete local graph and writes deterministic JSON to the root `lang.lock`. The lock records the root's name, version, and normalized manifest hash; dependencies have relative paths from the root, name, version, normalized content hash, and their direct dependency aliases. Content hashes cover the manifest and `.lang` sources of dependency packages. Dependency source line endings are normalized, and `out/` files are not inputs. Lock paths are portable relative paths and contain no workspace-specific absolute paths. A nonempty dependency graph must have a current canonical lock before `check`, `build`, or `run`; missing, malformed, or stale locks report `E_LOCK`. Running `lang lock` creates or updates the file. Dependency-free packages do not need a lockfile.

For `kind = "cli"`, the named entry module must be discovered and must define exactly one supported zero-argument `main() -> i32|bool|Text`. A missing entry module reports `E_ENTRYPOINT` at `lang.toml`; a missing or unsupported `main` reports `E_ENTRYPOINT` at the entry module source; duplicate same-module declarations retain `E_NAME_DUPLICATE`. A `main` in a non-entry module does not select the package entrypoint. Libraries may contain ordinary functions named `main` but have no package entrypoint. Package builds use the managed backend by default; `lang build PACKAGE_DIRECTORY --aot --rid RID` follows the executable AOT rules above and is valid only for CLI packages with the supported entrypoint.

The maintained `examples/library-package` acceptance example contains a CLI entry module that uses qualified references for public functions, structs, and unions in its own package, then calls generic `require` through its sibling `text-validation` path dependency. Integration acceptance also covers deterministic lock creation, missing/malformed/stale lock rejection and refresh, normalized source hashing, graph validation, direct alias visibility and module identity, public and private access, unknown aliases/modules/declarations, unqualified user references, legacy import and dotted-module rejection, module-path/header mismatches, manifest schema rejection, CLI entrypoint rules, JSON diagnostic locations, qualified-union exhaustiveness, managed builds, package AOT argument rejection, and the managed language-test runner. The runner typechecks tests in dependencies but executes only root-package tests, requires current dependency locks, continues after assertion failures, and reports stable module/source locations. It has no JSON runtime result format, property framework, or AOT test runner. This slice supports local offline paths only: Git or registry sources, caches, package-install commands, and build receipts are deferred.

## Control flow and Text contract addendum

Function bodies support `if condition { ... }` with an optional `else { ... }`. Conditions have type `bool`. Each branch starts from the enclosing immutable local scope and branch declarations do not escape. An `if` guarantees a return only when it has an `else` and both branches guarantee a return. A statement after a guaranteed return reports `E_UNREACHABLE`; a function that can fall through reports `E_TYPE_MISMATCH` with `Function must end with a return value`.

Expression precedence from highest to lowest is unary minus, multiplication, addition/subtraction, ordering comparison (`<`, `<=`, `>`, `>=`), then equality (`==`, `!=`). Operators at each binary level are left-associative. Equality requires operands of the same supported scalar type (`i32`, `bool`, or `Text`); ordering requires `i32`. There are no boolean conjunction or disjunction operators in this slice.

`Text.length` returns the number of Unicode scalar values as `i32`, so a supplementary character counts once. `Text.trim()` takes no arguments and uses the pinned .NET runtime's Unicode whitespace trimming. These operations and control flow support the PRD's `examples/text-validation` package shape. That pure library package is a precursor; the final maintained validation library still needs broader source support.

## Effect and filesystem capability addendum

Every function retains a mandatory `effects { ... }` annotation. Its entries come from the closed vocabulary `fs.read`, `fs.write`, `process.spawn`, `net.client`, `net.listen`, `db.read`, `db.write`, `env.read`, `clock.read`, `log.write`, and `secret.reveal`; annotations are upper bounds on inferred effects. The checker propagates effects through direct and qualified calls and recursive call cycles. `E_EFFECT_EXCEEDED` reports the shortest known path to an effectful operation. `E_EFFECT_UNKNOWN` and `E_EFFECT_DUPLICATE` reject unknown and repeated entries.

The implemented operation is `FsRead.read_text(path: Text) -> Result<Text, FsError>` and contributes `fs.read`. `FsRead` is opaque: it can enter source code only through a function parameter; once received, code may pass, return, or store it in ordinary immutable values, but cannot construct one. `FsError` has exactly five zero-payload variants: `NotFound`, `PermissionDenied`, `InvalidPath`, `InvalidText`, and `Io`; source code cannot construct these variants, and `read_text` is their only producer. Matches must be exhaustive unless they explicitly use `_`. The trusted adapter currently calls `File.ReadAllBytes` on the supplied path, maps recognized file/path/permission/I/O failures to the corresponding cases, and decodes bytes using strict UTF-8. `FsRead` is an opaque marker, not a path-root restriction or security sandbox. Its internal behavior is trusted, not proven by effect checking.

Library functions may receive and use `FsRead`, and effectful managed libraries can be built. Application manifest grants and capability injection, effect inspection, build receipts, and adapters for all other effects are deferred. This addendum defines one bounded effect/capability slice and does not claim M2 completion.
