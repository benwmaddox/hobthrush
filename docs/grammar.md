# Grammar

The parser accepts the implemented pure-language slice below. The wider V1 syntax in [the PRD](PRD.md) remains a design target; unsupported declarations and statements fail with a diagnostic.

```ebnf
module          = "module", module_path, ";", { declaration } ;
module_path     = lexical_identifier, { "::", lexical_identifier } ;
qualified_ref   = namespace_root, "::", module_segment, "::", { module_segment, "::" }, declaration_identifier ;
namespace_root  = "self" | bare_identifier ;
module_segment  = lexical_identifier ;
declaration_identifier = lexical_identifier ;
declaration     = [ "pub" ], ( function | union | struct ) | test_declaration ;

function        = "fn", bare_identifier, [ "<", type_parameters, ">" ], "(", [ parameters ], ")",
                  "->", type, "effects", "{", [ effect, { ",", effect }, [ "," ] ], "}", block ;
type_parameters = bare_identifier, { ",", bare_identifier } ;
effect          = effect_name ;
effect_name     = "fs.read" | "fs.write" | "process.spawn" | "net.client" | "net.listen"
                | "db.read" | "db.write" | "env.read" | "clock.read" | "log.write"
                | "secret.reveal" ;
parameters      = parameter, { ",", parameter }, [ "," ] ;
parameter       = bare_identifier, ":", type ;

type            = type_name, [ "<", type, { ",", type }, ">" ] ;
type_name       = builtin_type | type_parameter | qualified_ref ;
builtin_type    = "i32" | "bool" | "Text" | "Option" | "Result" | "FsError" | "FsRead" ;
type_parameter  = bare_identifier ;

union           = "union", bare_identifier, "{", [ variant, { ",", variant }, [ "," ] ], "}" ;
variant         = member_identifier, [ "(", payload_fields, ")" ] ;
payload_fields  = named_fields | positional_fields ;
named_fields    = named_field, { ",", named_field }, [ "," ] ;
named_field     = member_identifier, ":", type ;
positional_fields = type, { ",", type }, [ "," ] ;

struct          = "struct", bare_identifier, "{", [ struct_fields ], "}" ;
struct_fields   = struct_field, { ",", struct_field }, [ "," ] ;
struct_field    = member_identifier, ":", type ;

test_declaration = "test", text, "{", { test_let }, "assert", expression, ";", "}" ;
test_let         = "let", bare_identifier, ":", type, "=", expression, ";" ;

block           = "{", { statement }, "}" ;
statement       = "let", bare_identifier, ":", type, "=", expression, ";"
                | "return", expression, ";"
                | if_statement ;
if_statement    = "if", expression, block, [ "else", block ] ;

expression      = match_expression | equality ;
equality        = comparison, { ("==" | "!="), comparison } ;
comparison      = additive, { ("<" | "<=" | ">" | ">="), additive } ;
additive        = multiplicative, { ("+" | "-"), multiplicative } ;
multiplicative  = unary, { "*", unary } ;
unary           = [ "-" ], postfix ;
postfix         = primary, { field_access | member_call } ;
field_access    = ".", member_identifier ;
member_call     = ".", member_identifier, "(", [ arguments ], ")" ;
primary         = integer | boolean | text | bare_identifier | qualified_ref | call | struct_construction
                | "(", expression, ")" ;
call            = qualified_ref, "(", [ arguments ], ")" | bare_identifier, "(", [ arguments ], ")" ;
struct_construction
                = qualified_ref, "{", [ field_values ], "}" | bare_identifier, "{", [ field_values ], "}" ;
field_values    = field_value, { ",", field_value }, [ "," ] ;
field_value     = member_identifier, ":", expression ;
arguments       = expression, { ",", expression }, [ "," ] ;

match_expression = "match", expression, "{", match_arm, { ",", match_arm }, [ "," ], "}" ;
match_arm       = pattern, "=>", expression ;
pattern         = "_"
                | bare_identifier, [ "(", [ bindings ], ")" ]
                | builtin_type, ".", member_identifier, [ "(", [ bindings ], ")" ]
                | qualified_ref, ".", member_identifier, [ "(", [ bindings ], ")" ]
                | bare_identifier, ".", member_identifier, [ "(", [ bindings ], ")" ] ;
bindings        = bare_identifier, { ",", bare_identifier }, [ "," ] ;

integer         = digit, { digit } ;
boolean         = "true" | "false" ;
text            = '"', { character | escape }, '"' ;
escape          = '\"' | '\\' | '\n' | '\r' | '\t' | '\0' ;
lexical_identifier = letter | "_", { letter | digit | "_" } ;
member_identifier = lexical_identifier ;
bare_identifier = lexical_identifier except "true", "false", "null", "match", "if", "await", "with" ;
```

The `qualified_ref` primary is a syntactic declaration-reference form, including the base of a zero-payload union variant such as `self::app::main::Choice.Empty`. Semantic checking accepts it as a value only when it resolves to a supported value-producing case; arbitrary function or type declarations are not first-class values. Calls and struct constructions use their separate productions.

Every package source file has one `module` header followed by declarations; source-level imports do not exist, and a legacy top-level `import` is a syntax error. A package manifest chooses the source root; each module path maps to a `.lang` path beneath it by replacing `::` with directory separators. For example, `module text::validation;` maps to `src/text/validation.lang` when `source_root = "src"`. Every user declaration reference has a package root, one or more module segments, and a declaration name separated by `::`: `self::text::validation::normalize` names a declaration in the current package, while `validation::text::validation::normalize` names one in the direct dependency alias `validation`. The alias `self` is reserved. A dependency alias exposes only that direct package; aliases are not re-exported transitively. Built-in types and constructors such as `Option<T>`, `Some`, `None`, `Ok`, `Err`, and `FsError` retain short forms, as do local value expressions. Bare `self` follows ordinary local-name rules and is a namespace root only when followed by `::`. An unqualified user declaration may parse as a name, but only locals, built-ins, and type parameters are valid in that form; user declarations require qualification. Use `.` for value fields, member operations, and union variants, such as `self::catalog::message::Message.Ready(value)`. Module and declaration segments use contextual identifier rules.

Package commands use a package directory rather than a source-file path:

```text
lang check PACKAGE_DIRECTORY [--json]
lang build PACKAGE_DIRECTORY
lang run PACKAGE_DIRECTORY
lang test FILE_OR_PACKAGE
lang lock PACKAGE_DIRECTORY
```

Bare `lang test` with no target retains the compiler fixture mode. With a source file or package directory, it checks the program and runs its language-level tests.

The root `lang.toml` has required keys `name`, `version`, `kind`, and `source_root`, plus `entry_module` for `kind = "cli"`. The optional trailing `[dependencies]` table maps package aliases to relative package paths, one per line. Its values are plain double-quoted strings, paths use forward slashes, and aliases are language identifiers unique without regard to case. The table must be the final manifest section. For example:

```toml
[dependencies]
validation = "../text-validation"
```

The target directory must contain a valid package manifest with `kind = "lib"`. Dependency paths are resolved relative to the manifest that declares them, and dependencies may declare their own local path dependencies. Separate dependency roots cannot share the same package name and version. Path dependencies are local filesystem references; Git sources, registries, caches, `lang add` or other package-install commands, and build receipts are not implemented.

The reader supports a simple TOML subset: values are plain double-quoted strings, blank lines and comments outside quoted values are allowed, and escapes are not. `name` must be filesystem-safe; `version` must be non-empty but is not semver-validated; `source_root` must be a normalized, forward-slash relative directory inside the package. `kind` is `"cli"` or `"lib"`; `entry_module` is a valid `::`-separated module name, such as `app::main`, required for CLI packages and forbidden for libraries. Unknown, missing, duplicate, or invalid keys and values are rejected. A CLI package's entry module must exist and define one supported zero-argument `main() -> i32|bool|Text`. Library packages do not have an entrypoint. The dependency alias `self` is reserved. See [the package example](../examples/library-package/lang.toml) for qualified same-package and dependency references.

`lang lock PACKAGE_DIRECTORY` resolves the complete local path graph and writes a deterministic `lang.lock`. The JSON lock records the root name, version, and manifest hash, followed by each dependency's package-relative path, name, version, content hash, and aliases to its direct dependencies. Content hashing includes each dependency's manifest and `.lang` source files in normalized relative-path order; dependency source line endings are normalized, and generated `out/` files are excluded. The paths in the lock remain relative so the package directory can move between workspaces. A package with dependencies must have a valid, current lock before `check`, `build`, or `run`; missing, malformed, and stale locks report `E_LOCK`. Run `lang lock` again to create or update it. Dependency-free packages need no lockfile. Path dependencies work offline.

The resolver loads the root package and its local path dependencies. A declaration reference directly names the package root, module path, and declaration. The `self` root selects the current package; another root selects a declared direct dependency. Same-package declarations are visible by qualified reference, while private declarations remain module-local. A cross-module or cross-package reference to a private declaration reports `E_ACCESS_PRIVATE`; an unknown alias, module, or declaration reports `E_NAME_UNRESOLVED`. A dependency's aliases are not visible to its consumer. Same-named module paths in distinct dependency packages retain separate type and symbol identities.

A union variant uses either named fields such as `TooLong(max: i32)` or positional fields such as `Value(i32)`. One variant cannot mix the two forms. Generic functions may declare type parameters before their parameter list. Calls infer those parameters from independently typed argument expressions; each type parameter must occur in at least one function parameter type. For example, `require<T, E>(value: Option<T>, error: E) -> Result<T, E>` can infer `T` and `E` from an already typed `Option<T>` local and an independently typed error value. Generic function bodies can use their type parameters as values and in supported type constructors, but cannot apply operations that require a concrete type such as `T + T`.

There are no explicit type arguments, generic structs or unions, traits, or constructor-driven generic inference. `Some`, `None`, `Ok`, and `Err` still need an expected built-in type. A direct call such as `require(Some("present"), "fallback")` cannot pass the unresolved `Option<T>` expectation into `Some`; it reports `E_TYPE_MISMATCH` with a constructor-specific message that an expected `Option<T>` type is required. Bind the constructor to an annotated local first, then pass that local to the generic function. `Option<T>` and `Result<T, E>` remain compiler-provided generic types; their type arguments must be supported types.

Identifiers are contextual. A lexical identifier is any token with identifier spelling. A bare identifier is a lexical identifier except `true`, `false`, `null`, `match`, `if`, `await`, and `with`; those seven retain special expression or pattern behavior and cannot be used as bare declaration, type-root, binding, or function names. Other words that may look keyword-like in grammar positions (including `route`, `return`, `struct`, `pub`, `test`, and `assert`) are accepted as ordinary names where the surrounding syntax expects a name. Grammar dispatch still treats declaration and statement keywords specially in their positions, so an unsupported top-level `route` declaration remains unsupported.

Member positions are unambiguous and accept any lexical identifier: struct field declarations and initializers, union variant and named-payload labels, and the name after `.` in field access, qualified types, variant construction, and qualified patterns. Module path segments also accept any lexical identifier. A leading/root type name, constructor name, declaration name, function name, parameter, local binding, or unqualified pattern variant uses a bare identifier. `null` remains invalid as a value; `true` and `false` remain boolean literals.

Structs are immutable, non-generic, nominal struct types. Empty structs are legal. A struct declares named fields, and a construction must initialize every field exactly once by name. For example:

```lang
pub struct User { name: Text, age: i32 }
pub fn user_name() -> Text effects {} {
    let user: self::app::main::User = self::app::main::User { age: 37, name: "Ada" };
    return user.name;
}
```

Initializer expressions are evaluated in source order, including when that order differs from the declaration order. Duplicate initializers report `E_FIELD_DUPLICATE`, omitted fields report `E_FIELD_MISSING`, unknown initializers or reads report `E_FIELD_UNKNOWN`, and a value with the wrong field type reports `E_TYPE_MISMATCH`. Duplicate field declarations report `E_NAME_DUPLICATE`.

Field reads can be chained from a local, qualified function call, parenthesized value, or struct construction, for example `user.address.city`, `self::app::main::make_user().name`, `(user).name`, and `self::app::main::User { name: "Ada", age: 37 }.name`. A dotted call parses as a member call; the implemented member operations are union constructors, `FsRead.read_text(path)`, and `Text.trim()`. General methods are not implemented. A dotted no-call form such as `self::app::main::Choice.Empty` is resolved as a zero-payload union variant only when the left side is a qualified declaration reference; a dotted access rooted in a local remains a value-field read.

A struct may contain itself through `Option`, `Result`, or a tagged union. Cycles made only of direct struct fields are rejected with `E_TYPE_MISMATCH`; a direct recursive field such as `struct Node { next: self::app::main::Node }` is invalid.

A struct construction at the top level of a match scrutinee is ambiguous with the match-arm brace and is disabled there. Write `match (self::app::main::Box { value: self::app::main::Choice.Yes }).value { self::app::main::Choice.Yes => 1, self::app::main::Choice.No => 0 }` to use a struct construction within a union-valued scrutinee. Existing forms such as `match value { Some(x) => x, None => 0 }` continue to use the brace for match arms.

The implemented types are `i32`, `bool`, `Text`, immutable non-generic structs, declared non-generic unions, `Option<T>`, and `Result<T, E>`. Integer `+`, `-`, and `*` use checked `i32` arithmetic. Text literals support the escapes shown above. `text.length` returns an `i32` count of Unicode scalar values; a supplementary character such as 😀 counts once. `text.trim()` removes leading and trailing Unicode whitespace using the pinned runtime's string-trim behavior.

Function bodies support `if condition { ... }` with an optional `else { ... }`. Conditions must have type `bool`. Each branch has its own local scope, and a branch local does not escape. The checker accepts a function only when every path returns a value; an `if` counts as a guaranteed return only when it has an `else` and both branches return. A statement after a guaranteed return reports `E_UNREACHABLE`. A function that may fall through retains `E_TYPE_MISMATCH` with `Function must end with a return value`.

Language tests use `test "name" { ... }` at module scope. A test body may contain typed `let` setup statements followed by exactly one final `assert` whose expression has type `bool`; `return` and `if` are not allowed. Tests are pure and have an empty effect bound, so direct or transitive effectful calls are rejected. Duplicate names are rejected within one module, while different modules may use the same test name. Normal `check` and `build` commands typecheck tests but do not execute them. `lang test PACKAGE_DIRECTORY` runs tests in the selected package; dependency tests are typechecked but only root-package tests run. Output is managed PASS/FAIL text with each test's module and source location and a final count. The runner has no JSON result format, property-testing framework, or AOT mode.

Comparison operators are `==`, `!=`, `<`, `<=`, `>`, and `>=`. They are left-associative, and bind after arithmetic: `*`, then `+`/`-`, then ordering comparisons, then equality comparisons. Equality supports matching `i32`, `bool`, or `Text` operands; ordering supports only `i32`. A comparison returns `bool`. There are no logical operators in this slice.

Use `self::app::main::Choice.Yes` or `self::app::main::Choice.Value(3)` to construct a declared union. Built-in constructors use `Some(value)`, `None`, `Ok(value)`, and `Err(error)`. `None`, `Some`, `Ok`, and `Err` need an expected type from a local annotation, return type, or function parameter. These unqualified spellings always denote built-in constructors; a user function with the same spelling requires a qualified reference and does not shadow them. Fully qualified `Option<T>.Some` and `Result<T, E>.Ok` construction is deferred.

A match covers every union variant, including `Some`/`None` or `Ok`/`Err`; `_` is an explicit wildcard. Payload pattern names bind the payload values and do not need to match named field labels. Match arms use `=>` and are comma-separated.

Every function must declare an `effects { ... }` upper bound. The closed effect vocabulary is `fs.read`, `fs.write`, `process.spawn`, `net.client`, `net.listen`, `db.read`, `db.write`, `env.read`, `clock.read`, `log.write`, and `secret.reveal`. Unknown and repeated names report `E_EFFECT_UNKNOWN` and `E_EFFECT_DUPLICATE`. The checker infers direct and transitive effects through calls, including recursive call cycles, and reports `E_EFFECT_EXCEEDED` when inferred effects exceed the annotation. Its message includes a shortest call path to the operation.

The only effectful built-in operation in this slice is `FsRead.read_text(path: Text) -> Result<Text, FsError>`, which has effect `fs.read`. `FsRead` is opaque: it can enter source code only through a function parameter; once received, code may pass, return, or store it in ordinary immutable values, but cannot construct one. `FsError` is a closed union with zero-payload variants `NotFound`, `PermissionDenied`, `InvalidPath`, `InvalidText`, and `Io`; source code cannot construct these variants, and matches must handle every variant or include an explicit wildcard. Values are produced by the `read_text` adapter.

The filesystem adapter is a trusted runtime boundary. It currently calls `File.ReadAllBytes` on the supplied path, maps recognized file/path/permission/I/O failures to those `FsError` cases, and decodes bytes with strict UTF-8, mapping invalid text to `InvalidText`. `FsRead` is an opaque marker, not a path-root restriction or security sandbox; the checks do not prove the adapter's internal behavior. A library may accept and use `FsRead`, and builds as a managed library, but no application manifest grant or injection exists yet. `lang inspect effects`, build receipts, and adapters for the other vocabulary entries are deferred. Function bodies accept typed `let` declarations, `return` statements, and bounded `if`/`else` blocks.

## Typed CLI command declarations (PR1)

A standalone source module or a `cli` package's entry module may contain either its existing supported `main() -> i32|bool|Text` or exactly one command declaration. Existing main-based sources and packages keep their behavior. Command handlers use normal function effect annotations and inference; only the error formatter must be pure. PR1 does not grant or inject capabilities, and a command handler's args-only signature cannot receive `FsRead`.

```text
command_declaration ::= "command" identifier "{" command_item* "}"
command_item ::= "help" text_literal ";"
                | "argument" identifier ":" command_type "help" text_literal ";"
                | "option" identifier ":" command_type "=" literal "help" text_literal ";"
                | "flag" identifier "help" text_literal ";"
                | "handler" ":" qualified_ref ";"
                | "error" ":" qualified_ref ";"
command_type ::= "FilePath" | "Text" | "i32"
```

Items may appear in any order. The declaration has exactly one command-level `help`, `handler`, and `error`, at least one positional `argument`, and at most one command is accepted in the entry module. Command and argument/option/flag names are identifiers in contextual grammar positions. Options accept only matching literal defaults: `FilePath` and `Text` use text literals, and `i32` uses an integer literal. A `FilePath` default must be nonempty and contain no NUL. `FilePath` is opaque to source code and is decoded by the CLI parser.

For command `scan`, the compiler generates a nominal `ScanArgs` struct in that module. The fully qualified `handler` reference must name a function with signature `ScanArgs -> Result<Text, E>`; the fully qualified `error` reference must name a pure function with signature `E -> Text`. `E` is the handler's error type. User handler and formatter references must be fully qualified.

`lang run FILE_OR_PACKAGE -- APPLICATION_ARGS` forwards all arguments after the separator to a typed command parser in a standalone source module or CLI package entry module. Legacy main-based source and package runs do not accept this application-argument separator. Within command arguments, a standalone `--` ends option parsing; subsequent tokens are positional values even when they begin with `-`, for example `lang run PACKAGE -- scan -- --leading`. Top-level `--help` and `scan --help` print generated help and exit 0. Unknown commands/options, missing required arguments or option values, duplicate options, and invalid `i32` values or empty/NUL `FilePath` values print the stable codes `CLI_UNKNOWN_COMMAND`, `CLI_UNKNOWN_OPTION`, `CLI_MISSING_ARGUMENT`, `CLI_MISSING_VALUE`, `CLI_DUPLICATE_OPTION`, and `CLI_INVALID_VALUE` respectively, then exit 2. Diagnostic subjects escape control characters so one parse error stays on one physical stderr line. A handler `Ok(Text)` writes the text to stdout and exits 0. `Err(E)` is passed to the formatter, written to stderr, and exits 3. Unexpected runtime faults exit 70.

When the executable source module or CLI package entry module declares a typed command, managed and Native AOT builds write deterministic UTF-8 `command-schema.json` beside the executable artifact. The JSON uses schema version 1, snake_case property names, declaration/source order for argument, option, and flag arrays, and JSON-native default values. Each command record contains `name`, `help`, `handler`, `error_formatter`, `arguments`, `options`, and `flags`; flag records use the type `bool`. PR1 does not inject `FsRead` or other capabilities; manifest grants/injection, the maintained scan example, and an effect audit remain later work.

## Build and run entrypoints

`lang build FILE` emits an executable when the source contains `fn main()` with no parameters and a return type of `i32`, `bool`, or `Text`, or one typed command declaration. Otherwise it emits a library, including when a function named `main` has another signature. Typed-command builds also write `command-schema.json` beside the executable. Build artifacts are saved under an `out/<stem>-<id>/` directory beside the source file.

`lang run FILE` requires a supported main entry. The program prints its returned value followed by a newline; `i32`, lowercase `bool`, and `Text` values are printed directly. `lang run FILE -- APPLICATION_ARGS` instead dispatches to a typed command declared in the source. Successful runs exit 0. Checked i32 arithmetic overflow prints a generic runtime-fault message and exits 70.

For `cli` packages, the entry module may instead provide one typed command declaration; `lang run PACKAGE -- APPLICATION_ARGS` dispatches those arguments to its parser. Command parsing and handler result mapping are described above. Both supported entry forms are accepted by Native AOT publishing on the matching host.
