# Grammar

The parser accepts the implemented source-language slice below. The wider V1 syntax in [the PRD](PRD.md) remains a design target; unsupported declarations and statements fail with a diagnostic.

```ebnf
module          = "module", module_path, ";", { declaration } ;
module_path     = lexical_identifier, { "::", lexical_identifier } ;
qualified_ref   = namespace_root, "::", module_segment, "::", { module_segment, "::" }, declaration_identifier ;
namespace_root  = "self" | bare_identifier ;
module_segment  = lexical_identifier ;
declaration_identifier = lexical_identifier ;
declaration     = [ "pub" ], ( function | union | struct ) | command_decl | test_declaration | route_decl ;
route_decl      = "route", ( "GET" | "POST" ), text, "{", { route_item }, "}" ;
route_item      = "body", ":", type, ";"
                | "handler", ":", qualified_ref, ";"
                | "response", member_identifier, ":", integer, [ "json", route_json_type | "html" ], ";" ;
route_json_type = "i32" | "bool" | "Text" | qualified_ref ;
command_decl    = "command", bare_identifier, "{", { command_item }, "}" ;
command_item    = "help", text, ";"
                | "argument", bare_identifier, ":", command_type, "help", text, ";"
                | "option", bare_identifier, ":", command_type, "=", (integer | boolean | text), "help", text, ";"
                | "flag", bare_identifier, "help", text, ";"
                | "handler", ":", qualified_ref, ";"
                | "error", ":", qualified_ref, ";" ;
command_type    = "FilePath" | "Text" | "i32" ;

function        = [ "async" ], "fn", bare_identifier, [ "<", type_parameters, ">" ], "(", [ parameters ], ")",
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
builtin_type    = "i32" | "bool" | "Text" | "Html" | "Option" | "Result"
                | "List" | "FsError" | "FsRead" | "FsWrite" | "FilePath" | "DbError" | "DbRead" | "DbWrite"
                | "HttpClient" | "HttpResponse" | "HttpError" | "Config" | "Secrets" | "Logger" | "Secret"
                | "ProcessRunner" | "ProcessOutput" | "ProcessError" ;
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
                | var_declaration
                | assignment
                | "return", expression, ";"
                | if_statement
                | for_statement
                | with_transaction_statement ;
var_declaration = "var", bare_identifier, ":", type, "=", expression, ";" ;
assignment      = bare_identifier, "=", expression, ";" ;
if_statement    = "if", expression, block, [ "else", block ] ;
for_statement   = "for", bare_identifier, "in", expression, block ;
with_transaction_statement = "with", expression, "as", bare_identifier, block ;

expression      = match_expression | equality ;
equality        = comparison, { ("==" | "!="), comparison } ;
comparison      = additive, { ("<" | "<=" | ">" | ">="), additive } ;
additive        = multiplicative, { ("+" | "-"), multiplicative } ;
multiplicative  = unary, { "*", unary } ;
unary           = [ "-" ], postfix | "await", postfix ;
postfix         = primary, { field_access | member_call } ;
field_access    = ".", member_identifier ;
member_call     = ".", member_identifier, "(", [ arguments ], ")" ;
primary         = integer | boolean | text | bare_identifier | qualified_ref | call | struct_construction
                | list_literal | "(", expression, ")" ;
list_literal     = "[", [ arguments ], "]" ;
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
bare_identifier = lexical_identifier except "true", "false", "null", "match", "if", "await" ;
```

The `qualified_ref` primary is a syntactic declaration-reference form, including the base of a zero-payload union variant such as `self::app::main::Choice.Empty`. Semantic checking accepts it as a value only when it resolves to a supported value-producing case; arbitrary function or type declarations are not first-class values. Calls and struct constructions use their separate productions.

## Lists and iteration

In an unqualified type position, `List<T>` names the built-in immutable list; qualified references such as `self::collections::types::List` still resolve to user declarations named `List`. A list literal is `[item, ...]`. Items must have the same exact type. An empty `[]` is accepted only when an expected `List<T>` type supplies its element type. `Map<K, V>` remains part of the wider V1 target and is not implemented.

For a list `items: List<T>`, `items.length` returns `i32`, `items.get(index)` returns `Option<T>` and yields `None` for negative or out-of-range indexes, and `items.append(value)` returns a list with the value appended without changing the original list. `Text.split(separator)` returns `List<Text>`, matches the exact separator, and preserves empty fields. An empty separator returns one item containing the original text.

`for item in items { ... }` visits elements in list order. Its binding is scoped to the loop body and immutable. Calls in the loop body contribute to the enclosing function's inferred effects. A loop does not guarantee that its body runs, so a `return` inside a loop does not establish that the enclosing function returns on every path. `var name: T = value;` declares a rebindable local; assignment uses `name = value;` and requires the same type. `let` bindings and loop bindings are immutable. Resource handles cannot be stored in lists or rebindable `var` locals. List contents have no mutation or index-assignment syntax. `break` and `continue` are not implemented.

Every package source file has one `module` header followed by declarations; source-level imports do not exist, and a legacy top-level `import` is a syntax error. A package manifest chooses the source root; each module path maps to a `.lang` path beneath it by replacing `::` with directory separators. For example, `module text::validation;` maps to `src/text/validation.lang` when `source_root = "src"`. Every user declaration reference has a package root, one or more module segments, and a declaration name separated by `::`: `self::text::validation::normalize` names a declaration in the current package, while `validation::text::validation::normalize` names one in the direct dependency alias `validation`. The alias `self` is reserved. A dependency alias exposes only that direct package; aliases are not re-exported transitively. Built-in types and constructors such as `Option<T>`, `Some`, `None`, `Ok`, `Err`, `FsRead`, `FsWrite`, and `FsError` retain short forms, as do local value expressions. Bare `self` follows ordinary local-name rules and is a namespace root only when followed by `::`. An unqualified user declaration may parse as a name, but only locals, built-ins, and type parameters are valid in that form; user declarations require qualification. Use `.` for value fields, member operations, and union variants, such as `self::catalog::message::Message.Ready(value)`. Module and declaration segments use contextual identifier rules.

Package commands use a package directory rather than a source-file path:

```text
lang check PACKAGE_DIRECTORY [--json]
lang build PACKAGE_DIRECTORY
lang run PACKAGE_DIRECTORY
lang test FILE_OR_PACKAGE
lang lock PACKAGE_DIRECTORY
lang audit PACKAGE_DIRECTORY --json
lang inspect effects PACKAGE_DIRECTORY SYMBOL --json
lang inspect api PACKAGE_DIRECTORY --json
```

Bare `lang test` with no target retains the compiler fixture mode. With a source file or package directory, it checks the program and runs its language-level tests.

## Async functions and cancellation

Functions may be declared with contextual `async fn` or `pub async fn`. An async function declares its source result type `T`; the runtime carries its task and cancellation token internally, and no task type appears in source. `await` is valid only inside an async function and only for an async function call or async intrinsic. An async call must be awaited. The checker reports `E_AWAIT_CONTEXT`, `E_AWAIT_SYNC`, `E_AWAIT_TARGET`, or `E_ASYNC_CALL_UNAWAITED` for those violations.

`FsRead.read_text_async(path)` returns `Result<Text, FsError>`, decodes strict UTF-8, and contributes the existing `fs.read` effect and `FsRead` capability. `await client.get_text_async("/path?query")` returns `Result<HttpResponse, HttpError>` and contributes `net.client`; `HttpResponse` exposes `status: i32` and `body: Text`, and a non-2xx status remains a response. `HttpError` is closed to `InvalidTarget`, `Transport`, `Timeout`, `ResponseTooLarge`, and `InvalidText`. `FsRead.read_text`, `FsWrite`, and SQLite operations remain synchronous source APIs.

Async typed CLI handlers receive cancellation from Ctrl+C; cancellation exits 130. Async web route handlers and helpers receive ASP.NET `RequestAborted` through awaited calls. A canceled request ends without a generic JSON 500. The HTTP client uses that host cancellation token and propagates host cancellation; its independent 10-second total request timeout returns `HttpError.Timeout`. Async/cancellation adapters for filesystem writes, SQLite, and the remaining effect classes remain future `#743` work.

## Checked route declarations

A single-file `lang check FILE` treats the file as a route-capable root. The checked route contract accepts static `GET` and `POST` routes, a path text beginning with `/` and containing none of `{`, `}`, `?`, or `#`, and exactly one fully qualified handler reference. `GET` handlers have no request-body parameter. `POST` routes declare exactly one fully qualified, non-generic struct body type, which is the handler's first parameter. Either handler form may then take optional `FsWrite`, `DbRead`, `DbWrite`, and `HttpClient` capability parameters, in that order; `FsRead` is not supported on web routes. Handlers are non-generic functions returning a declared union. Route body and handler items precede response mappings. Routes with the same method and case-insensitively equal paths are duplicates; different methods may share a path.

Each union variant has exactly one response mapping. Status codes range from 100 through 599. A zero-payload variant has no content format; payload-bearing responses cannot use 1xx, 204, 205, or 304 statuses. A single-payload variant uses `json Type` or `html`; JSON accepts `i32`, `bool`, `Text`, or a fully qualified user struct whose fields recursively use those scalar types. Recursive structs and other unsupported shapes are rejected. The stated JSON type must equal the union payload type. `html` requires an opaque `Html` payload. The managed web runtime provides `html.text(Text)`, `html.heading(Text)`, `html.paragraph(Text)`, `html.concat(Html, Html)`, and `html.document(Text, Html)`; text passed to these builders is escaped, and there is no raw-markup constructor. Handler references use normal qualified-name resolution and visibility checks.

Route declarations are checked into route IR in single-file checks and in the entry module of a `kind = "web"` package. A web package requires an `entry_module` containing at least one route and `[capabilities] net.listen = "allow"`; only that root entry module may declare routes. The current route surface has static paths and `GET`/`POST` methods; it has no incoming path-parameter or query-parameter bindings. A root web package may also grant `fs.write`, `db.read`/`db.write`, and `net.client`; the route entry injects only signature-requested opaque capabilities in `FsWrite`, `DbRead`, `DbWrite`, `HttpClient`, `Config`, `Secrets`, `Logger` order. `net.client` requires a top-level `http_origin`. `FsRead` is not injected into web routes. Web packages build and run as managed ASP.NET applications, and `lang run PACKAGE -- --urls URL` forwards the host URL option. Generated output includes deterministic `openapi.json` beside the managed DLL. The host accepts JSON request bodies up to 1 MiB, returns 400 for malformed or incompatible JSON and 413 for larger bodies, and returns 404/405 for unmatched paths/methods. Unexpected handler faults become a generic JSON 500 with a request ID. Grants and compiler metadata do not create a security sandbox. Web packages do not support Native AOT.

The maintained [`examples/web`](../examples/web) sample uses a configured SQLite database and idempotent schema, the shared validation library, and safe Html builders. `DbRead.query_one` accepts a SQL string literal beginning with a standalone `SELECT` token and containing no semicolon, a concrete parameter struct, and an explicit `Result<Option<Row>, DbError>` expected type; it checks column names/count, types/nullability, and zero-or-one row cardinality. `DbWrite.execute` performs a parameterized statement using a concrete parameter struct. `with db.begin() as tx { ... }` provides a scoped transaction; `Transaction.execute` is limited to one literal `INSERT`, `UPDATE`, `DELETE`, or `REPLACE` statement without a semicolon. SQLite setup uses normalized package-relative `sqlite_path` and `sqlite_schema` values and a conditional pinned `Microsoft.Data.Sqlite` 10.0.12 dependency. `LANG_SQLITE_PATH` overrides the database file for a run. These database operations contribute `db.read`/`db.write` effects and require the matching capability value; web root grants are required to inject database capabilities into route handlers. Async route handlers and helpers propagate ASP.NET `RequestAborted` through awaited calls; cancellation ends the request without a generic JSON 500. SQLite operations remain synchronous source APIs: generated code waits synchronously on provider async APIs, so no async SQLite language surface exists yet. Its concurrency and cancellation-cleanup design remains open; this slice does not complete M2 or V1.

`with db.begin() as tx { ... }` opens a SQLite write transaction and requires `db.write`. `Transaction` is a lexical handle that the checker permits only as the direct receiver of `tx.execute(SQL_LITERAL, Parameters)` or `tx.commit()` inside that scope; aliasing, passing, returning, storing, using it in a generic wrapper, or using it as another receiver reports the implemented `E_RESOURCE_ESCAPE`. `tx.execute` accepts one literal `INSERT`, `UPDATE`, `DELETE`, or `REPLACE` statement with no semicolon; transaction-control, DDL, and other statements report `E_DB_TRANSACTION_STATEMENT`. It returns `Result<i32, DbError>`, while `tx.commit` returns `Result<bool, DbError>`; the first successful commit is `Ok(true)`. A failed execute makes the transaction rollback-only, so a later commit returns `Err(DbError.Statement)`. Leaving without a successful commit rolls back and disposes the transaction on normal scope exit or early function return. This is a narrow lexical resource rule, not a general ownership or borrow checker. Database operations still block from source code; reads use `DbRead.query_one` outside the transaction handle. Async cancellation reaches awaited calls, but SQLite adapter cancellation and cleanup remain future conformance work.

The root `lang.toml` has required keys `name`, `version`, `kind`, and `source_root`, plus `entry_module` for `kind = "cli"` or `kind = "web"`. The optional root `[config]` table declares lower_snake_case field names and descriptor strings `Text|required`, `Text|default:<literal>`, or `Secret<Text>|required`; `Text|default:` declares an empty default. It must appear before `[capabilities]` and `[dependencies]`. The root may also declare optional top-level pairs `process_windows_path`/`process_windows_sha256` and `process_linux_path`/`process_linux_sha256` before any table. Each pair must be complete; paths are normalized package-relative forward-slash file paths, and hashes are 64 lowercase hexadecimal characters. Process pins and `process.spawn` are valid only in a root CLI package. Any declared process pin requires the `process.spawn` grant, and the grant requires the current-host pair. Every declared executable must exist under the package root as a regular non-symlink/non-reparse file and match its hash on every host; a selected Linux executable also needs an execute permission bit. Malformed pair/path/hash syntax is `E_MANIFEST`; missing or invalid executable pins are `E_PROCESS_EXECUTABLE`. CLI packages may grant `fs.read`, `fs.write`, `net.client`, `env.read`, `secret.reveal`, `log.write`, and/or `process.spawn`; web packages require `net.listen` and may grant `fs.write`, `net.client`, configured database capabilities, `env.read`, `secret.reveal`, and/or `log.write`. Libraries cannot declare application grants or use/expose `Config`, `Secrets`, `Logger`, or `Secret<Text>`. Capability values must be the exact string `"allow"`. The root manifest's top-level `http_origin` is required with `net.client` and is invalid without that grant. It must be one absolute HTTPS origin, or HTTP for `localhost` or a loopback IP, with only a root path and no query, fragment, or user info. SQLite web packages must set both `sqlite_path` and `sqlite_schema` to normalized forward-slash package-relative file paths. At a web package root, either database grant requires both keys; declaring `sqlite_schema` also requires `db.write` so startup can apply the schema. Database configuration and grants are valid only for web packages. Library source may expose checked functions that accept `DbRead`, `DbWrite`, or `HttpClient` without application grants; libraries with database operations receive the conditional SQLite provider reference when built. The optional trailing `[dependencies]` table maps package aliases to relative package paths, one per line. Its values are plain double-quoted strings, paths use forward slashes, and aliases are language identifiers unique without regard to case. The table must be the final manifest section. For example:

```toml
[config]
home_dir = "Text|default:"
api_token = "Secret<Text>|required"

[capabilities]
env.read = "allow"
secret.reveal = "allow"

[dependencies]
validation = "../text-validation"
```

The target directory must contain a valid package manifest with `kind = "lib"`. Dependency paths are resolved relative to the manifest that declares them, and dependencies may declare their own local path dependencies. Separate dependency roots cannot share the same package name and version. Path dependencies are local filesystem references; Git sources, registries, caches, `lang add` and other package-install commands are not implemented. Package audits cover the current local package graph; successful-build receipts are emitted for standalone-source and package builds.

The reader supports a simple TOML subset: values are plain double-quoted strings, blank lines and comments outside quoted values are allowed, and escapes are not. `name` must be filesystem-safe; `version` must be non-empty but is not semver-validated; `source_root` must be a normalized, forward-slash relative directory inside the package. `kind` is `"cli"`, `"web"`, or `"lib"`; `entry_module` is a valid `::`-separated module name, such as `app::main`, required for CLI and web packages and forbidden for libraries. Web packages require the `net.listen = "allow"` grant and at least one route in their entry module. SQLite paths must stay inside the package and may not resolve through a symbolic link or reparse point. CLI packages support their existing main or typed-command entry. Web packages are managed-only and require route entry selection. Unknown, missing, duplicate, or invalid keys and values are rejected. Library packages do not have an entrypoint. The dependency alias `self` is reserved. See the [library package](../examples/library-package/lang.toml), [CLI package](../examples/scan-cli/lang.toml), and [web package](../examples/web/lang.toml) for examples.

`lang lock PACKAGE_DIRECTORY` resolves the complete local path graph and writes a deterministic `lang.lock`. The JSON lock records the root name, version, and manifest hash, followed by each dependency's package-relative path, name, version, content hash, and aliases to its direct dependencies. Content hashing includes each dependency's manifest and `.lang` source files in normalized relative-path order; dependency source line endings are normalized, and generated `out/` files are excluded. The paths in the lock remain relative so the package directory can move between workspaces. A package with dependencies must have a valid, current lock before `check`, `build`, or `run`; missing, malformed, and stale locks report `E_LOCK`. Run `lang lock` again to create or update it. Dependency-free packages need no lockfile. Path dependencies work offline.

The resolver loads the root package and its local path dependencies. A declaration reference directly names the package root, module path, and declaration. The `self` root selects the current package; another root selects a declared direct dependency. Same-package declarations are visible by qualified reference, while private declarations remain module-local. A cross-module or cross-package reference to a private declaration reports `E_ACCESS_PRIVATE`; an unknown alias, module, or declaration reports `E_NAME_UNRESOLVED`. A dependency's aliases are not visible to its consumer. Same-named module paths in distinct dependency packages retain separate type and symbol identities.

A union variant uses either named fields such as `TooLong(max: i32)` or positional fields such as `Value(i32)`. One variant cannot mix the two forms. Generic functions may declare type parameters before their parameter list. Calls infer those parameters from independently typed argument expressions; each type parameter must occur in at least one function parameter type. For example, `require<T, E>(value: Option<T>, error: E) -> Result<T, E>` can infer `T` and `E` from an already typed `Option<T>` local and an independently typed error value. Generic function bodies can use their type parameters as values and in supported type constructors, but cannot apply operations that require a concrete type such as `T + T`.

There are no explicit type arguments, generic structs or unions, traits, or constructor-driven generic inference. `Some`, `None`, `Ok`, and `Err` still need an expected built-in type. A direct call such as `require(Some("present"), "fallback")` cannot pass the unresolved `Option<T>` expectation into `Some`; it reports `E_TYPE_MISMATCH` with a constructor-specific message that an expected `Option<T>` type is required. Bind the constructor to an annotated local first, then pass that local to the generic function. `Option<T>` and `Result<T, E>` remain compiler-provided generic types; their type arguments must be supported types.

Identifiers are contextual. A lexical identifier is any token with identifier spelling. A bare identifier is a lexical identifier except `true`, `false`, `null`, `match`, `if`, and `await`; those six retain special expression or pattern behavior and cannot be used as bare declaration, type-root, binding, or function names. Other words that may look keyword-like in grammar positions (including `route`, `return`, `struct`, `pub`, `test`, `assert`, and `with`) are accepted as ordinary names where the surrounding syntax expects a name. `for`, `var`, and `in` are recognized as collection syntax only in their statement positions and can still be ordinary names where the grammar expects an identifier. `List` is the built-in only in an unqualified type position; a qualified `...::List` is a user type reference. Grammar dispatch still treats declaration and statement keywords specially in their positions; top-level `route` dispatches to the contextual route grammar below.

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

Field reads can be chained from a local, qualified function call, parenthesized value, or struct construction, for example `user.address.city`, `self::app::main::make_user().name`, `(user).name`, and `self::app::main::User { name: "Ada", age: 37 }.name`. A dotted call parses as a member call; the implemented operations include union constructors, `FsRead.read_text(path)`, `FsRead.read_text_async(path)`, `FsWrite.write_text(path, value)`, `HttpClient.get_text_async(target)`, `Text.trim()`, `DbRead.query_one`, `DbWrite.execute`, and `DbWrite.begin()`. Within the resulting `with db.begin() as tx` scope, the supported transaction calls are `tx.execute(...)` and `tx.commit()`. The managed web runtime also provides the built-in `html.*` escaped-content builders described above. General user-defined methods are not implemented. A dotted no-call form such as `self::app::main::Choice.Empty` is resolved as a zero-payload union variant only when the left side is a qualified declaration reference; a dotted access rooted in a local remains a value-field read.

A struct may contain itself through `Option`, `Result`, or a tagged union. Cycles made only of direct struct fields are rejected with `E_TYPE_MISMATCH`; a direct recursive field such as `struct Node { next: self::app::main::Node }` is invalid.

A struct construction at the top level of a match scrutinee is ambiguous with the match-arm brace and is disabled there. Write `match (self::app::main::Box { value: self::app::main::Choice.Yes }).value { self::app::main::Choice.Yes => 1, self::app::main::Choice.No => 0 }` to use a struct construction within a union-valued scrutinee. Existing forms such as `match value { Some(x) => x, None => 0 }` continue to use the brace for match arms.

The implemented types are `i32`, `bool`, `Text`, immutable non-generic structs, declared non-generic unions, `Option<T>`, and `Result<T, E>`. Integer `+`, `-`, and `*` use checked `i32` arithmetic. Text literals support the escapes shown above. `text.length` returns an `i32` count of Unicode scalar values; a supplementary character such as 😀 counts once. `text.trim()` removes leading and trailing Unicode whitespace using the pinned runtime's string-trim behavior.

Function bodies support `if condition { ... }` with an optional `else { ... }`. Conditions must have type `bool`. Each branch has its own local scope, and a branch local does not escape. The checker accepts a function only when every path returns a value; an `if` counts as a guaranteed return only when it has an `else` and both branches return. A statement after a guaranteed return reports `E_UNREACHABLE`. A function that may fall through retains `E_TYPE_MISMATCH` with `Function must end with a return value`.

Language tests use `test "name" { ... }` at module scope. A test body may contain typed `let` setup statements followed by exactly one final `assert` whose expression has type `bool`; `return` and `if` are not allowed. Tests are pure and have an empty effect bound, so direct or transitive effectful calls are rejected. Duplicate names are rejected within one module, while different modules may use the same test name. Normal `check` and `build` commands typecheck tests but do not execute them. `lang test PACKAGE_DIRECTORY` runs tests in the selected package; dependency tests are typechecked but only root-package tests run. Output is managed PASS/FAIL text with each test's module and source location and a final count. The runner has no JSON result format, property-testing framework, or AOT mode.

Comparison operators are `==`, `!=`, `<`, `<=`, `>`, and `>=`. They are left-associative, and bind after arithmetic: `*`, then `+`/`-`, then ordering comparisons, then equality comparisons. Equality supports matching `i32`, `bool`, or `Text` operands; ordering supports only `i32`. A comparison returns `bool`. There are no logical operators in this slice.

Use `self::app::main::Choice.Yes` or `self::app::main::Choice.Value(3)` to construct a declared union. Built-in constructors use `Some(value)`, `None`, `Ok(value)`, and `Err(error)`. `None`, `Some`, `Ok`, and `Err` need an expected type from a local annotation, return type, or function parameter. These unqualified spellings always denote built-in constructors; a user function with the same spelling requires a qualified reference and does not shadow them. Fully qualified `Option<T>.Some` and `Result<T, E>.Ok` construction is deferred.

A match covers every union variant, including `Some`/`None` or `Ok`/`Err`; `_` is an explicit wildcard. Payload pattern names bind the payload values and do not need to match named field labels. Match arms use `=>` and are comma-separated.

Every function must declare an `effects { ... }` upper bound. The closed effect vocabulary is `fs.read`, `fs.write`, `process.spawn`, `net.client`, `net.listen`, `db.read`, `db.write`, `env.read`, `clock.read`, `log.write`, and `secret.reveal`. Unknown and repeated names report `E_EFFECT_UNKNOWN` and `E_EFFECT_DUPLICATE`. The checker infers direct and transitive effects through calls, including recursive call cycles, and reports `E_EFFECT_EXCEEDED` when inferred effects exceed the annotation. Its message includes a shortest call path to the operation. In the startup-configuration slice, `config.get_text("key") -> Text` and `config.get_secret_text("key") -> Secret<Text>` contribute `env.read`, `secrets.reveal_text(secret) -> Text` contributes `secret.reveal`, and `logger.info(event: Text, detail: Text) -> bool` contributes `log.write`; `await process_runner.run_text_async(arguments, stdin)` contributes `process.spawn` in a root CLI package. `clock.read` has no implementation. The accessors accept only literal declared keys of the matching descriptor. `Secret<Text>` is the only supported secret type, source code cannot construct a secret wrapper, and library packages cannot use or expose `Config`, `Secrets`, `Logger`, or `Secret<Text>`. `ProcessRunner`, `ProcessOutput`, and `ProcessError` are root-CLI-package-only and cannot appear in standalone, library, or web source.

The implemented effectful operations include synchronous `FsRead.read_text(path)` and async `FsRead.read_text_async(path)`, both strict-UTF-8 `fs.read` operations returning `Result<Text, FsError>`; `FsWrite.write_text(path, value) -> Result<bool, FsError>` (`fs.write`); `HttpClient.get_text_async(target) -> Result<HttpResponse, HttpError>` (`net.client`); `DbRead.query_one` (`db.read`); and `DbWrite.execute`, `DbWrite.begin`, `Transaction.execute`, and `Transaction.commit` (`db.write`). The read operations accept a `Text` or `FilePath` path; `write_text` takes a `Text` value. `FsRead`, `FsWrite`, and `HttpClient` are opaque: source code cannot construct these capabilities. `FsError` is a closed union with zero-payload variants `NotFound`, `PermissionDenied`, `InvalidPath`, `InvalidText`, and `Io`; source code cannot construct these variants, and matches must handle every variant or include an explicit wildcard. The read and write adapters produce these errors. `HttpResponse` exposes `status: i32` and `body: Text`; `HttpError` is closed to `InvalidTarget`, `Transport`, `Timeout`, `ResponseTooLarge`, and `InvalidText`.

The filesystem, SQLite, HTTP client, environment, secret-reveal, logger, and process adapters are trusted runtime boundaries. `FsRead.read_text` reads the supplied path and decodes bytes with strict UTF-8. `FsWrite.write_text` accepts `Text` or `FilePath` plus a `Text` value, encodes the whole value as strict UTF-8 without a BOM, writes and flushes a unique temporary file beside the destination, then calls an overwrite move to replace it. Replacement atomicity depends on the host OS and filesystem. An embedded NUL or path-related `ArgumentException`, `NotSupportedException`, or `PathTooLongException` maps to `InvalidPath`; missing file or parent directory maps to `NotFound`; `SecurityException` or `UnauthorizedAccessException` maps to `PermissionDenied`; an encoding fallback error maps to `InvalidText`; and other `IOException` failures map to `Io`. A leftover temporary file is removed best-effort. `FsWrite` uses host OS path resolution and does not confine writes to a package root or provide a filesystem sandbox. SQLite calls bind values from concrete parameter structs; reads require a single literal `SELECT` without a semicolon and return zero or one strictly decoded row, while writes return a typed `DbError`. `HttpClient.get_text_async` is a GET-only adapter. The configured origin is one absolute HTTPS origin, or HTTP for `localhost` or a loopback IP, with no base path, query, fragment, or user info. Each target must be an origin-relative absolute path (beginning with `/`, but not `//`) and may include a query; control characters, backslashes, fragments, credentials, and authority changes are rejected as `InvalidTarget`. Redirects, cookies, proxy use, and ambient credentials are disabled. The adapter limits raw response bytes to 1 MiB, decodes body text as strict UTF-8, and applies a 10-second total timeout. Timeout returns `HttpError.Timeout`; host cancellation propagates instead of becoming an `HttpError`. Non-success status codes remain `HttpResponse` values. The origin check constrains request authority, but it is not an SSRF/network sandbox; DNS resolution and trusted runtime code remain boundaries. Config values are snapshotted once before a CLI handler or web listener starts. Environment variable names are `LANG_CONFIG_<UPPER_SNAKE_KEY>`; required means presence, so an empty present value is valid. Missing required values identify only the config key and derived environment variable name; CLI exits 78 and web startup fails before listening. `config.get_text("key")` and `config.get_secret_text("key")` require a literal declared key with the matching descriptor. `secrets.reveal_text` is the only reveal operation and converts `Secret<Text>` to ordinary `Text`, which has no taint tracking. `Secret<Text>` is redacted by standard formatting and `logger.info`; `logger.info(event, detail)` writes a deterministic JSON line to stderr with `level`, `event`, and `detail`, plus `request_id` when available in a web request. The startup-config slice has no dynamic reload, rotation, arbitrary logging maps or sinks, clock/timestamps, or end-to-end taint tracking. Metadata and grants do not provide OS-level containment.

Web capabilities are injected from root-package grants. CLI handlers receive requested, granted `FsRead`, `FsWrite`, `HttpClient`, `Config`, `Secrets`, `Logger`, then `ProcessRunner` values after command arguments. Web routes receive requested, granted `FsWrite`, `DbRead`, `DbWrite`, `HttpClient`, `Config`, `Secrets`, then `Logger` values after any request body; `FsRead` is not injected into routes. The first configuration slice is root-package-only; library packages cannot use or expose `Config`, `Secrets`, `Logger`, or `Secret<Text>`. Process pins and `ProcessRunner` are root-CLI-only. CLI packages may also grant `env.read`, `secret.reveal`, and `log.write` alongside their existing `fs.read`, `fs.write`, and `net.client` grants; web packages require `net.listen` and may grant the same three alongside existing `fs.write`, `net.client`, and configured SQLite grants. The network grant still requires root `http_origin`. Grant edits participate in dependency lock freshness. The compiler-derived `lang inspect effects PACKAGE_DIRECTORY SYMBOL --json`, `lang inspect api PACKAGE_DIRECTORY --json`, and `lang audit PACKAGE_DIRECTORY --json` reports are implemented. Inspect API and audit report schema v5 metadata for root configuration fields using only names, source types, and required/default status; they do not expose runtime values or default literals. Both include `process_executables` after `http_origin`, with configured relative-path `{ os, path, sha256 }` records sorted Windows then Linux; audit inputs include every declared executable. API covers root/direct public declarations and checked root command/routes, while audit covers the complete resolved local graph including private function facts and explicit claim-only trusted-host/adapter entries. Both keep grants separate from inferred effects and neither proves trusted runtime behavior. Successful builds continue to write schema-v1 receipts; the command schema is version 4 and its `capabilities` list can include `process.spawn`. Adapters for other vocabulary entries remain deferred. Function bodies accept typed `let` declarations, `return` statements, and bounded `if`/`else` blocks, plus the scoped `with` transaction form above.

## Typed CLI command declarations (PR1)

A standalone source module or a `cli` package's entry module may contain either its existing supported `main() -> i32|bool|Text` or exactly one command declaration. Existing main-based sources and packages keep their behavior. Command handlers use normal function effect annotations and inference; only the error formatter must be pure. A CLI package can grant `fs.read`, `fs.write`, `net.client`, `env.read`, `secret.reveal`, `log.write`, and/or `process.spawn` with entries in `[capabilities]` assigned the exact string `"allow"`; a `net.client` grant requires top-level `http_origin`. The generated command entry passes only granted services requested by the checked handler, in `FsRead`, `FsWrite`, `HttpClient`, `Config`, `Secrets`, `Logger`, then `ProcessRunner` order after its generated argument value. A handler that requests a capability without its grant is rejected with `E_CAPABILITY_MISSING`. These checks do not create a sandbox. Standalone commands and library packages cannot grant capabilities; library packages also cannot use or expose `Config`, `Secrets`, `Logger`, or `Secret<Text>`. `ProcessRunner`, `ProcessOutput`, and `ProcessError` are unavailable outside the root CLI package; same-package helpers may accept or pass them.

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

For command `scan`, the compiler generates a public nominal `ScanArgs` struct in that module. The fully qualified `handler` reference must name a synchronous or async function with source signature `ScanArgs -> Result<Text, E>`, or `ScanArgs` followed by optional granted `FsRead`, optional granted `FsWrite`, and optional granted `HttpClient` parameters, in that order, returning `Result<Text, E>`; the fully qualified `error` reference must name a pure function with signature `E -> Text`. `E` is the handler's error type. User handler and formatter references must be fully qualified.

`lang run FILE_OR_PACKAGE -- APPLICATION_ARGS` forwards all arguments after the separator to a typed command parser in a standalone source module or CLI package entry module. Legacy main-based source and package runs do not accept this application-argument separator. Within command arguments, a standalone `--` ends option parsing; subsequent tokens are positional values even when they begin with `-`, for example `lang run PACKAGE -- scan -- --leading`. Top-level `--help` and `scan --help` print generated help and exit 0. Unknown commands/options, missing required arguments or option values, duplicate options, and invalid `i32` values or empty/NUL `FilePath` values print the stable codes `CLI_UNKNOWN_COMMAND`, `CLI_UNKNOWN_OPTION`, `CLI_MISSING_ARGUMENT`, `CLI_MISSING_VALUE`, `CLI_DUPLICATE_OPTION`, and `CLI_INVALID_VALUE` respectively, then exit 2. Diagnostic subjects escape control characters so one parse error stays on one physical stderr line. An async handler can be canceled by Ctrl+C and exits 130. `Ok(Text)` writes the text to stdout and exits 0. `Err(E)` is passed to the formatter, written to stderr, and exits 3. Unexpected runtime faults exit 70.

When the executable source module or CLI package entry module declares a typed command, managed and Native AOT builds write deterministic UTF-8 `command-schema.json` beside the executable artifact. The JSON uses schema version 4, snake_case property names, declaration/source order for argument, option, and flag arrays, and JSON-native default values. Each command record contains `name`, `help`, `handler`, `error_formatter`, `capabilities`, `arguments`, `options`, and `flags`; `capabilities` lists granted capabilities required by the handler and injected by the trusted command root in handler parameter order, including the `env.read`, `secret.reveal`, and `log.write` services and `process.spawn` when requested by a granted process runner. It does not repeat every manifest grant: unused grants are omitted. Flag records use the type `bool`. The maintained [`examples/scan-cli`](../examples/scan-cli) demonstrates its filesystem grant and typed runtime behavior. The compiler-derived schema-v1 `lang inspect effects PACKAGE_DIRECTORY SYMBOL --json`, schema-v5 `lang inspect api PACKAGE_DIRECTORY --json`, and schema-v5 `lang audit PACKAGE_DIRECTORY --json` reports are implemented. A successful managed or Native AOT `lang build` writes schema-v1 `build-receipt.json`; exact audit and receipt contracts are in the [implementation contract](implementation-contract.md#audit-and-build-receipt-contract). No other process execution modes are implemented; other adapters for the remaining effect vocabulary remain future work.


## Constrained process runner

A root CLI manifest may declare Windows and/or Linux executable pins using complete top-level `process_windows_path`/`process_windows_sha256` and `process_linux_path`/`process_linux_sha256` pairs before any table. Paths are normalized, forward-slash, package-relative file paths. SHA-256 values contain exactly 64 lowercase hexadecimal characters. A process pin requires the root `process.spawn` grant; `process.spawn` requires the current-host pair. A package with neither pins nor that grant remains valid. Every declared pin must resolve within the package root on every host to an existing regular non-symlink/non-reparse file whose digest matches. The selected Linux file also needs an execute permission bit. Malformed/incomplete pairs or invalid path/hash syntax report `E_MANIFEST`; missing current-host pins, invalid files, digest mismatches, unsupported hosts, and missing Linux execute permission report `E_PROCESS_EXECUTABLE`.

The opaque `ProcessRunner` capability is injected only into root CLI command handlers, after `Logger`, and only when granted and requested. Same-package helper functions may accept or pass `ProcessRunner`, `ProcessOutput`, and `ProcessError`. These types cannot appear in standalone, library, or web source. `ProcessRunner.run_text_async(arguments: List<Text>, stdin: Text) -> Result<ProcessOutput, ProcessError>` must be awaited inside an async function and contributes `process.spawn`. `ProcessOutput` exposes `exit_code: i32`, `stdout: Text`, and `stderr: Text`. `ProcessError` is a closed, zero-payload set: `InvalidArgument`, `InputTooLarge`, `OutputTooLarge`, `InvalidText`, `StartFailed`, and `TimedOut`. Invalid, NUL-containing, over-count, or over-budget arguments map to `InvalidArgument`; stdin over 1 MiB maps to `InputTooLarge`; output overflows map to `OutputTooLarge`; invalid strict UTF-8 maps to `InvalidText`; start failures, launch incompatibility, and artifact hash mismatch map to `StartFailed`; the overall timeout maps to `TimedOut`. A nonzero process exit code is still an `Ok(ProcessOutput)`.

The host copies the selected pinned executable beside the generated CLI artifact as `<manifest assembly name>.process-runner` on Linux or `<manifest assembly name>.process-runner.exe` on Windows. This deterministic assembly-qualified suffix avoids colliding with the generated apphost when the application assembly itself is named `process-runner`. It preserves the selected Linux execute bit and verifies identity again immediately before spawn. It invokes the absolute copied path with shell execution disabled and arguments supplied through `ArgumentList`. The child has an empty environment and fixed working directory at the artifact directory; source cannot select an executable, shell, `PATH`, environment, or working directory. Windows launch incompatibility returns `StartFailed` without shell fallback. Limits are 128 arguments, 16 KiB combined strict-UTF-8 argument bytes, 1 MiB strict-UTF-8 stdin, 1 MiB per raw stdout/stderr stream, strict-UTF-8 output decoding, and a fixed 10-second overall timeout. Host cancellation propagates after best-effort process-tree termination and a reap bounded to two seconds.

Inspect API and audit schema v5 expose configured pins in `process_executables` immediately after `http_origin`, ordered Windows then Linux. Each record is `{ os, path, sha256 }`; `path` is relative, and full paths are not reported. Audit inputs include every declared process executable. Command schema v4 may list `process.spawn`; inspect-effects and build-receipt remain v1. Pins, grants, copied identity checks, and bounded invocation do not create OS containment: the selected binary retains full OS authority. There is no web process capability, streaming, dynamic executable selection, binary I/O, caller-selected timeout, detached-work API, CPU/memory sandbox, or guarantee that detached descendants are killed.

## Build and run entrypoints

`lang build FILE` emits an executable when the source contains `fn main()` with no parameters and a return type of `i32`, `bool`, or `Text`, or one typed command declaration. Otherwise it emits a library, including when a function named `main` has another signature. Typed-command builds also write `command-schema.json` beside the executable. Successful managed and Native AOT builds write `build-receipt.json` beside their artifacts. Build artifacts are saved under an `out/<stem>-<id>/` directory beside the source file.

`lang run FILE` requires a supported main entry. The program prints its returned value followed by a newline; `i32`, lowercase `bool`, and `Text` values are printed directly. `lang run FILE -- APPLICATION_ARGS` instead dispatches to a typed command declared in the source. Successful runs exit 0. Checked i32 arithmetic overflow prints a generic runtime-fault message and exits 70.

For `cli` packages, the entry module may instead provide one typed command declaration; `lang run PACKAGE -- APPLICATION_ARGS` dispatches those arguments to its parser. Command parsing and handler result mapping are described above. Both supported entry forms are accepted by Native AOT publishing on the matching host.

`lang audit PACKAGE_DIRECTORY --json` checks a package and its complete resolved local dependency graph and writes a deterministic schema-v5 report to stdout. Usage errors print the normal usage line and exit 2. Package resolution, lock, checker, or input-hashing failures print the JSON diagnostic envelope and exit 1; success exits 0. The report includes private function facts, normalized hashes for each package manifest/source/configured SQLite schema, inferred effect paths, root configuration schema metadata (names, types, required/default status only), root manifest grants, `http_origin`, configured `process_executables`, and explicit trusted-host/adapter claims. It does not expose runtime config values or default literals. Its claims are metadata, not proofs. The [implementation contract](implementation-contract.md#audit-and-build-receipt-contract) defines field order, sorting, hashes, and receipt contents.
