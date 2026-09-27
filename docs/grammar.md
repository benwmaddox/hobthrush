# Grammar

The parser accepts the implemented pure-language slice below. The wider V1 syntax in [the PRD](PRD.md) remains a design target; unsupported declarations and statements fail with a diagnostic.

```ebnf
module          = "module", module_path, ";", { import_decl }, { declaration } ;
module_path     = lexical_identifier, { ".", lexical_identifier } ;
import_decl     = "import", module_path, "{", import_symbols, "}", ";" ;
import_symbols  = bare_identifier, { ",", bare_identifier }, [ "," ] ;
declaration     = [ "pub" ], ( function | union | struct ) ;

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
type_name       = bare_identifier, { ".", member_identifier } ;

union           = "union", bare_identifier, "{", [ variant, { ",", variant }, [ "," ] ], "}" ;
variant         = member_identifier, [ "(", payload_fields, ")" ] ;
payload_fields  = named_fields | positional_fields ;
named_fields    = named_field, { ",", named_field }, [ "," ] ;
named_field     = member_identifier, ":", type ;
positional_fields = type, { ",", type }, [ "," ] ;

struct          = "struct", bare_identifier, "{", [ struct_fields ], "}" ;
struct_fields   = struct_field, { ",", struct_field }, [ "," ] ;
struct_field    = member_identifier, ":", type ;

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
primary         = integer | boolean | text | bare_identifier | call | struct_construction
                | "(", expression, ")" ;
call            = bare_identifier, "(", [ arguments ], ")" ;
struct_construction
                = bare_identifier, "{", [ field_values ], "}" ;
field_values    = field_value, { ",", field_value }, [ "," ] ;
field_value     = member_identifier, ":", expression ;
arguments       = expression, { ",", expression }, [ "," ] ;

match_expression = "match", expression, "{", match_arm, { ",", match_arm }, [ "," ], "}" ;
match_arm       = pattern, "=>", expression ;
pattern         = "_"
                | bare_identifier, [ "(", [ bindings ], ")" ]
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

Every package source file has one `module` header. Imports, when present, follow that header and precede declarations. A package manifest chooses the source root; each module path maps to a `.lang` path beneath it by replacing dots with directory separators. For example, `module text.validation;` maps to `src/text/validation.lang` when `source_root = "src"`. Imports name one module and an explicit, non-empty list of symbols. No aliases, wildcard imports, or transitive imports are supported.

Package commands use a package directory rather than a source-file path:

```text
lang check PACKAGE_DIRECTORY [--json]
lang build PACKAGE_DIRECTORY
lang run PACKAGE_DIRECTORY
```

The root `lang.toml` has a strict schema: exactly the keys `name`, `version`, `kind`, and `source_root`, plus `entry_module` for `kind = "cli"`. The reader supports a simple TOML subset: values are plain double-quoted strings, blank lines and comments outside quoted values are allowed, and escapes are not. `name` must be filesystem-safe; `version` must be non-empty but is not semver-validated; `source_root` must be a normalized, forward-slash relative directory inside the package. `kind` is `"cli"` or `"lib"`; `entry_module` is a valid dotted module name, required for CLI packages and forbidden for libraries. Unknown, missing, duplicate, or invalid keys and values are rejected. A CLI package's entry module must exist and define one supported zero-argument `main() -> i32|bool|Text`. Library packages do not have an entrypoint. See [the package example](../examples/library-package/lang.toml) for a complete same-package import.

The current resolver loads modules from one package only. It does not resolve external dependencies, lockfiles, or package registries. A same-package import makes only the listed public declarations available in the importing module. Private declarations remain module-local, and importing a module does not make its own imports visible to downstream modules.

A union variant uses either named fields such as `TooLong(max: i32)` or positional fields such as `Value(i32)`. One variant cannot mix the two forms. Generic functions may declare type parameters before their parameter list. Calls infer those parameters from independently typed argument expressions; each type parameter must occur in at least one function parameter type. For example, `require<T, E>(value: Option<T>, error: E) -> Result<T, E>` can infer `T` and `E` from an already typed `Option<T>` local and an independently typed error value. Generic function bodies can use their type parameters as values and in supported type constructors, but cannot apply operations that require a concrete type such as `T + T`.

There are no explicit type arguments, generic structs or unions, traits, or constructor-driven generic inference. `Some`, `None`, `Ok`, and `Err` still need an expected built-in type. A direct call such as `require(Some("present"), "fallback")` cannot pass the unresolved `Option<T>` expectation into `Some`; it reports `E_TYPE_MISMATCH` with a constructor-specific message that an expected `Option<T>` type is required. Bind the constructor to an annotated local first, then pass that local to the generic function. `Option<T>` and `Result<T, E>` remain compiler-provided generic types; their type arguments must be supported types.

Identifiers are contextual. A lexical identifier is any token with identifier spelling. A bare identifier is a lexical identifier except `true`, `false`, `null`, `match`, `if`, `await`, and `with`; those seven retain special expression or pattern behavior and cannot be used as bare declaration, type-root, binding, or function names. Other words that may look keyword-like in grammar positions (including `route`, `return`, `struct`, and `pub`) are accepted as ordinary names where the surrounding syntax expects a name. Grammar dispatch still treats declaration and statement keywords specially in their positions, so an unsupported top-level `route` declaration remains unsupported.

Member positions are unambiguous and accept any lexical identifier: struct field declarations and initializers, union variant and named-payload labels, and the name after `.` in field access, qualified types, variant construction, and qualified patterns. Module path segments also accept any lexical identifier. A leading/root type name, constructor name, declaration name, function name, parameter, local binding, or unqualified pattern variant uses a bare identifier. `null` remains invalid as a value; `true` and `false` remain boolean literals.

Structs are immutable, non-generic, nominal struct types. Empty structs are legal. A struct declares named fields, and a construction must initialize every field exactly once by name. For example:

```lang
pub struct User { name: Text, age: i32 }
let user: User = User { age: 37, name: "Ada" };
return user.name;
```

Initializer expressions are evaluated in source order, including when that order differs from the declaration order. Duplicate initializers report `E_FIELD_DUPLICATE`, omitted fields report `E_FIELD_MISSING`, unknown initializers or reads report `E_FIELD_UNKNOWN`, and a value with the wrong field type reports `E_TYPE_MISMATCH`. Duplicate field declarations report `E_NAME_DUPLICATE`.

Field reads can be chained from a local, call, parenthesized value, or struct construction, for example `user.address.city`, `make_user().name`, `(user).name`, and `User { name: "Ada", age: 37 }.name`. A dotted call parses as a member call; the implemented member operations are union constructors, `FsRead.read_text(path)`, and `Text.trim()`. General methods are not implemented. A receiver local takes precedence over a matching type name, so local receiver resolution remains deterministic. A dotted no-call form such as `Choice.Empty` is resolved as a zero-payload union variant only when the left name is not shadowed by a local.

A struct may contain itself through `Option`, `Result`, or a tagged union. Cycles made only of direct struct fields are rejected with `E_TYPE_MISMATCH`; a bare recursive field such as `struct Node { next: Node }` is invalid.

A struct construction at the top level of a match scrutinee is ambiguous with the match-arm brace and is disabled there. Write `match (Box { value: Choice.Yes }).value { Choice.Yes => 1, Choice.No => 0 }` to use a struct construction within a union-valued scrutinee. Existing forms such as `match value { Some(x) => x, None => 0 }` continue to use the brace for match arms.

The implemented types are `i32`, `bool`, `Text`, immutable non-generic structs, declared non-generic unions, `Option<T>`, and `Result<T, E>`. Integer `+`, `-`, and `*` use checked `i32` arithmetic. Text literals support the escapes shown above. `text.length` returns an `i32` count of Unicode scalar values; a supplementary character such as 😀 counts once. `text.trim()` removes leading and trailing Unicode whitespace using the pinned runtime's string-trim behavior.

Function bodies support `if condition { ... }` with an optional `else { ... }`. Conditions must have type `bool`. Each branch has its own local scope, and a branch local does not escape. The checker accepts a function only when every path returns a value; an `if` counts as a guaranteed return only when it has an `else` and both branches return. A statement after a guaranteed return reports `E_UNREACHABLE`. A function that may fall through retains `E_TYPE_MISMATCH` with `Function must end with a return value`.

Comparison operators are `==`, `!=`, `<`, `<=`, `>`, and `>=`. They are left-associative, and bind after arithmetic: `*`, then `+`/`-`, then ordering comparisons, then equality comparisons. Equality supports matching `i32`, `bool`, or `Text` operands; ordering supports only `i32`. A comparison returns `bool`. There are no logical operators in this slice.

Use `Choice.Yes` or `Choice.Value(3)` to construct a declared union. Built-in constructors use `Some(value)`, `None`, `Ok(value)`, and `Err(error)`. `None`, `Some`, `Ok`, and `Err` need an expected type from a local annotation, return type, or function parameter. A same-named user function takes precedence. Fully qualified `Option<T>.Some` and `Result<T, E>.Ok` construction is deferred.

A match covers every union variant, including `Some`/`None` or `Ok`/`Err`; `_` is an explicit wildcard. Payload pattern names bind the payload values and do not need to match named field labels. Match arms use `=>` and are comma-separated.

Every function must declare an `effects { ... }` upper bound. The closed effect vocabulary is `fs.read`, `fs.write`, `process.spawn`, `net.client`, `net.listen`, `db.read`, `db.write`, `env.read`, `clock.read`, `log.write`, and `secret.reveal`. Unknown and repeated names report `E_EFFECT_UNKNOWN` and `E_EFFECT_DUPLICATE`. The checker infers direct and transitive effects through calls, including recursive call cycles, and reports `E_EFFECT_EXCEEDED` when inferred effects exceed the annotation. Its message includes a shortest call path to the operation.

The only effectful built-in operation in this slice is `FsRead.read_text(path: Text) -> Result<Text, FsError>`, which has effect `fs.read`. `FsRead` is opaque: it can enter source code only through a function parameter; once received, code may pass, return, or store it in ordinary immutable values, but cannot construct one. `FsError` is a closed union with zero-payload variants `NotFound`, `PermissionDenied`, `InvalidPath`, `InvalidText`, and `Io`; source code cannot construct these variants, and matches must handle every variant or include an explicit wildcard. Values are produced by the `read_text` adapter.

The filesystem adapter is a trusted runtime boundary. It currently calls `File.ReadAllBytes` on the supplied path, maps recognized file/path/permission/I/O failures to those `FsError` cases, and decodes bytes with strict UTF-8, mapping invalid text to `InvalidText`. `FsRead` is an opaque marker, not a path-root restriction or security sandbox; the checks do not prove the adapter's internal behavior. A library may accept and use `FsRead`, and builds as a managed library, but no application manifest grant or injection exists yet. `lang inspect effects`, build receipts, and adapters for the other vocabulary entries are deferred. Function bodies accept typed `let` declarations, `return` statements, and bounded `if`/`else` blocks.

## Build and run entrypoints

`lang build FILE` emits an executable when the source contains `fn main()` with no parameters and a return type of `i32`, `bool`, or `Text`. Otherwise it emits a library, including when a function named `main` has another signature. Build artifacts are saved under an `out/<stem>-<id>/` directory beside the source file.

`lang run FILE` requires that executable entry signature. The program prints its returned value followed by a newline; `i32`, lowercase `bool`, and `Text` values are printed directly. Successful runs exit 0. Checked i32 arithmetic overflow prints a generic runtime-fault message and exits 70.
