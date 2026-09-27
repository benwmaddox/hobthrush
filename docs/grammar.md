# Grammar

The parser accepts the implemented pure-language slice below. The wider V1 syntax in [the PRD](PRD.md) remains a design target; unsupported declarations and statements fail with a diagnostic.

```ebnf
module          = "module", module_path, ";", { import_decl }, { declaration } ;
module_path     = lexical_identifier, { ".", lexical_identifier } ;
import_decl     = "import", module_path, "{", import_symbols, "}", ";" ;
import_symbols  = bare_identifier, { ",", bare_identifier }, [ "," ] ;
declaration     = [ "pub" ], ( function | union | struct ) ;

function        = "fn", bare_identifier, "(", [ parameters ], ")",
                  "->", type, "effects", "{", "}", block ;
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
                | "return", expression, ";" ;

expression      = match_expression | additive ;
additive        = multiplicative, { ("+" | "-"), multiplicative } ;
multiplicative  = unary, { "*", unary } ;
unary           = [ "-" ], postfix ;
postfix         = primary, { ".", member_identifier } ;
primary         = integer | boolean | text | bare_identifier | call
                | variant_construction | struct_construction
                | "(", expression, ")" ;
call            = bare_identifier, "(", [ arguments ], ")" ;
variant_construction
                = bare_identifier, ".", member_identifier, "(", [ arguments ], ")" ;
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

A union variant uses either named fields such as `TooLong(max: i32)` or positional fields such as `Value(i32)`. One variant cannot mix the two forms. User-defined generic unions and functions are not supported. `Option<T>` and `Result<T, E>` are compiler-provided generic types; their type arguments must be supported types.

Identifiers are contextual. A lexical identifier is any token with identifier spelling. A bare identifier is a lexical identifier except `true`, `false`, `null`, `match`, `if`, `await`, and `with`; those seven retain special expression or pattern behavior and cannot be used as bare declaration, type-root, binding, or function names. Other words that may look keyword-like in grammar positions (including `route`, `return`, `struct`, and `pub`) are accepted as ordinary names where the surrounding syntax expects a name. Grammar dispatch still treats declaration and statement keywords specially in their positions, so an unsupported top-level `route` declaration remains unsupported.

Member positions are unambiguous and accept any lexical identifier: struct field declarations and initializers, union variant and named-payload labels, and the name after `.` in field access, qualified types, variant construction, and qualified patterns. Module path segments also accept any lexical identifier. A leading/root type name, constructor name, declaration name, function name, parameter, local binding, or unqualified pattern variant uses a bare identifier. `null` remains invalid as a value; `true` and `false` remain boolean literals.

Structs are immutable, non-generic, nominal struct types. Empty structs are legal. A struct declares named fields, and a construction must initialize every field exactly once by name. For example:

```lang
pub struct User { name: Text, age: i32 }
let user: User = User { age: 37, name: "Ada" };
return user.name;
```

Initializer expressions are evaluated in source order, including when that order differs from the declaration order. Duplicate initializers report `E_FIELD_DUPLICATE`, omitted fields report `E_FIELD_MISSING`, unknown initializers or reads report `E_FIELD_UNKNOWN`, and a value with the wrong field type reports `E_TYPE_MISMATCH`. Duplicate field declarations report `E_NAME_DUPLICATE`.

Field reads can be chained from a local, call, parenthesized value, or struct construction, for example `user.address.city`, `make_user().name`, `(user).name`, and `User { name: "Ada", age: 37 }.name`. Method calls are not implemented. A dotted no-call form such as `Choice.Empty` is resolved as a zero-payload union variant only when the left name is not shadowed by a local. A local name shadows a type name in dotted access and dotted-call syntax.

A struct may contain itself through `Option`, `Result`, or a tagged union. Cycles made only of direct struct fields are rejected with `E_TYPE_MISMATCH`; a bare recursive field such as `struct Node { next: Node }` is invalid.

A struct construction at the top level of a match scrutinee is ambiguous with the match-arm brace and is disabled there. Write `match (Box { value: Choice.Yes }).value { Choice.Yes => 1, Choice.No => 0 }` to use a struct construction within a union-valued scrutinee. Existing forms such as `match value { Some(x) => x, None => 0 }` continue to use the brace for match arms.

The implemented types are `i32`, `bool`, `Text`, immutable non-generic structs, declared non-generic unions, `Option<T>`, and `Result<T, E>`. Integer `+`, `-`, and `*` use checked `i32` arithmetic. Text literals support the escapes shown above.

Use `Choice.Yes` or `Choice.Value(3)` to construct a declared union. Built-in constructors use `Some(value)`, `None`, `Ok(value)`, and `Err(error)`. `None`, `Some`, `Ok`, and `Err` need an expected type from a local annotation, return type, or function parameter. A same-named user function takes precedence. Fully qualified `Option<T>.Some` and `Result<T, E>.Ok` construction is deferred.

A match covers every union variant, including `Some`/`None` or `Ok`/`Err`; `_` is an explicit wildcard. Payload pattern names bind the payload values and do not need to match named field labels. Match arms use `=>` and are comma-separated.

Every function still writes an explicit `effects {}` clause. Only empty effect sets are implemented. Function bodies currently accept typed `let` declarations and `return` statements.

## Build and run entrypoints

`lang build FILE` emits an executable when the source contains `fn main()` with no parameters and a return type of `i32`, `bool`, or `Text`. Otherwise it emits a library, including when a function named `main` has another signature. Build artifacts are saved under an `out/<stem>-<id>/` directory beside the source file.

`lang run FILE` requires that executable entry signature. The program prints its returned value followed by a newline; `i32`, lowercase `bool`, and `Text` values are printed directly. Successful runs exit 0. Checked i32 arithmetic overflow prints a generic runtime-fault message and exits 70.
