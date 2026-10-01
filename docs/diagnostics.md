# Diagnostics

Compiler diagnostics have stable `code`, `severity`, `message`, `file`, and `range` fields. Ranges are one-based line and column positions with an exclusive end. Driver diagnostics use the same shape. A successful `lang check FILE --json` returns schema version 1 with an empty `diagnostics` array.

| Code | Meaning | Status |
| --- | --- | --- |
| E_SYNTAX | Token or production does not match the grammar | Implemented |
| E_UNSUPPORTED | Valid V1 construct is not yet implemented, including immediate lambdas with unconstrained generic parameter/capture/result types | Implemented |
| E_NAME_UNRESOLVED | Name is not declared in scope, or a qualified reference names an unknown alias, module, or declaration | Implemented |
| E_NAME_DUPLICATE | Duplicate declaration | Implemented |
| E_ACCESS_PRIVATE | A qualified reference crosses a module or package boundary to a private declaration | Implemented |
| E_TRAIT_DECL | Trait has no methods, duplicates a method, or declares an unsupported effectful signature | Implemented for pure static traits |
| E_TRAIT_BOUND | A function type parameter repeats a trait bound | Implemented; unknown and private traits retain `E_NAME_UNRESOLVED` and `E_ACCESS_PRIVATE` |
| E_TRAIT_IMPL_MISSING | No visible implementation satisfies a concrete trait operation or generic bound | Implemented |
| E_TRAIT_IMPL_INACCESSIBLE | A matching implementation exists but is private to another package | Implemented |
| E_TRAIT_IMPL_DUPLICATE | More than one implementation is visible for a trait and closed target in the package graph | Implemented |
| E_TRAIT_IMPL_SIGNATURE | Implementation target, orphan legality, method coverage, purity, or substituted binding signature is invalid | Implemented |
| E_TRAIT_CONSTRAINT_RECURSIVE | Concrete trait dispatch creates a recursive implementation-obligation cycle | Implemented |
| E_ADAPTER | Managed adapter could not be prepared or staged for a generated build or test project | Implemented |
| E_ADAPTER_ASYNC | Managed adapter function is declared async or binds an asynchronous operation | Implemented |
| E_ADAPTER_BODY | Managed adapter function contains a source body | Implemented |
| E_ADAPTER_BRIDGE | Adapter function names a managed adapter bridge the compiler does not support | Implemented |
| E_ADAPTER_DECL | Adapter function binding is missing the required operation assignment or text operation ID | Implemented |
| E_ADAPTER_DUPLICATE_OPERATION | More than one function in a package binds the same managed adapter operation | Implemented |
| E_ADAPTER_EFFECT | Adapter function declares effects, or its catalog operation requires effects or capabilities | Implemented |
| E_ADAPTER_GENERIC | Managed adapter function declares type parameters | Implemented |
| E_ADAPTER_MANIFEST | Adapter function is declared without a managed adapter in the package manifest | Implemented |
| E_ADAPTER_OPERATION | Adapter function binds an operation not supported by its managed adapter bridge | Implemented |
| E_ADAPTER_SIGNATURE | Adapter function parameters or result do not match the catalog operation signature | Implemented |
| E_ADAPTER_VISIBILITY | Managed adapter function is not public | Implemented |
| E_MANIFEST | Package manifest file, schema, key, or value is invalid, including an unsupported or duplicate key/grant, a malformed `[config]` declaration, incomplete process executable pairs or invalid process path/hash syntax, an invalid `http_origin` or `net.client`/origin pairing, non-CLI process configuration/grants, or any application capability/config declaration in a library package | Implemented |
| E_DEPENDENCY | A local dependency path, package kind, package identity, or dependency graph is invalid | Implemented |
| E_LOCK | A required dependency lock is missing, malformed, unreadable, or stale | Implemented |
| E_MANAGED_ADAPTER | Managed adapter descriptor or catalog validation fails, including bridge/target mismatch, invalid package-local assembly path or hash, missing/unreadable or mismatched assembly, or invalid assembly metadata/framework references | Implemented |
| E_MODULE_PATH | A module header does not match its source-root-relative file path | Implemented |
| E_MODULE_DUPLICATE | A package includes more than one source for the same package/module identity | Implemented |
| E_TYPE_MISMATCH | Expression, payload, argument, local, condition, comparison (including equality of unsupported or differently typed values), or return type mismatch; also generic nominal arity, an explicit type argument in a union match pattern, or a function body that may fall through | Implemented for the pure type and control-flow slices |
| E_AWAIT_CONTEXT | `await` appears outside an async function | Implemented |
| E_AWAIT_SYNC | `await` targets a synchronous call or intrinsic | Implemented |
| E_AWAIT_TARGET | `await` target is not an async call or intrinsic | Implemented |
| E_ASYNC_CALL_UNAWAITED | An async call is used without `await` | Implemented |
| E_ASSIGN_IMMUTABLE | Assignment targets a `let` local or immutable loop binding | Implemented |
| E_CLOSURE_CAPTURE_MUTABLE | An immediately invoked lambda captures a rebindable `var` local | Implemented for the one-parameter expression-bodied lambda subset |
| E_TYPE_VISIBILITY | A public function, trait signature, or impl signature exposes a private union, struct, or trait | Implemented |
| E_FIELD_UNKNOWN | A struct initializer or field read names an unknown field | Implemented |
| E_FIELD_MISSING | A struct construction omits a declared field | Implemented |
| E_FIELD_DUPLICATE | A struct construction initializes a field more than once | Implemented |
| E_MATCH_NONEXHAUSTIVE | One or more union variants are missing from a match | Implemented |
| E_MATCH_ARM_DUPLICATE | A match arm is duplicated or follows a wildcard | Implemented |
| E_UNREACHABLE | A statement follows a statement or `if` whose every path returns | Implemented |
| E_ENTRYPOINT | `lang run` has neither a supported zero-argument `main` nor a typed command entry, or a CLI package's entry module is absent or has neither supported entry form | Implemented |
| E_IO | Source, fixture, or generated build files could not be read or written | Implemented |
| E_PROCESS | The .NET build or run process could not be started or waited on | Implemented |
| E_PROCESS_EXECUTABLE | A configured process pin is missing for the current host or its file is absent, outside the root, non-regular, a symlink/reparse point, has a mismatched hash, or lacks the current Linux execute bit; unsupported process hosts also fail | Implemented |
| E_BUILD | The generated C# project failed to compile | Implemented |
| E_BUILD_TARGET | A recognized `--aot` or `--rid` option is malformed or used outside the supported build form, the RID is unsupported or targets a different host OS, or the program lacks an executable entrypoint | Implemented |
| E_EFFECT_EXCEEDED | Inferred direct or transitive effect exceeds a function's declared upper bound; the message includes the shortest known call path | Implemented for filesystem, SQLite database, HTTP client, CLI process runner, environment config, secret reveal, and structured log operations |
| E_EFFECT_UNKNOWN | Effect annotation names a value outside the closed effect vocabulary | Implemented |
| E_EFFECT_DUPLICATE | Effect annotation repeats a value | Implemented |
| E_CAPABILITY_MISSING | An operation requires an opaque capability that is absent, has the wrong type, or is not granted to the CLI command or web route entry | Implemented for `FsRead`, `FsWrite`, `DbRead`, `DbWrite`, `HttpClient`, `Config`, `Secrets`, `Logger`, and root-CLI `ProcessRunner`; root CLI/web grants, required `http_origin`, process pins, and current-host executable validation are checked |
| E_CAPABILITY_SCOPE | A root-package-only startup service or secret type is used in a library package | Implemented for `Config`, `Secrets`, `Logger`, `Secret<Text>`, `ProcessRunner`, `ProcessOutput`, and `ProcessError` scope checks |
| E_CONFIG_KEY | Config accessor key is not a literal declared in the root `[config]` schema | Implemented |
| E_CONFIG_TYPE | Config accessor does not match the declared `Text` or `Secret<Text>` field type | Implemented |
| E_DB_SQL_LITERAL | A SQLite operation's SQL argument is not a string literal | Implemented |
| E_DB_READ_STATEMENT | `DbRead.query_one` does not contain one supported `SELECT` statement without a semicolon | Implemented |
| E_DB_TRANSACTION_STATEMENT | `Transaction.execute` does not contain one supported `INSERT`, `UPDATE`, `DELETE`, or `REPLACE` statement without a semicolon | Implemented |
| E_DB_PARAMETERS | A SQLite operation is missing its parameter struct | Implemented |
| E_DB_RESULT_TYPE | `DbRead.query_one` lacks an expected `Result<Option<Row>, DbError>` type | Implemented |
| E_DB_CODEC_UNSUPPORTED | A SQLite parameter or row struct uses an unsupported shape or field type, including a generic union nested in a codec type | Implemented |
| E_COMMAND_DECL | Typed CLI command declaration is malformed, duplicated, uses an unsupported type/default (including empty or NUL `FilePath` option defaults), appears in a library package, conflicts with `main`, or violates the command-count/argument requirements | Implemented for PR1 typed commands |
| E_COMMAND_HANDLER | Command handler or error formatter is not a fully qualified function with the required generated argument/result signature | Implemented for PR1 typed commands |
| E_ROUTE_DECL | Invalid route placement, method/path/body/status, unknown response variant, or duplicate method/path | Implemented |
| E_ROUTE_BINDING | A route placeholder is repeated, missing a path declaration, has a mismatched or unused declaration, or duplicates/conflicts with another binding name | Implemented |
| E_ROUTE_HANDLER | Missing, duplicate, or signature-incompatible route handler; generic reply unions must be nongeneric | Implemented; generic reply unions use the stable message Route handler reply unions must be nongeneric; unresolved/private qualified references retain `E_NAME_UNRESOLVED`/`E_ACCESS_PRIVATE` |
| E_ROUTE_RESPONSE_DUPLICATE | A response variant is mapped more than once | Implemented |
| E_ROUTE_RESPONSE_MISSING | A declared response variant has no mapping | Implemented |
| E_ROUTE_CODEC_UNSUPPORTED | Unsupported response payload, content format, or JSON shape, including nested generic unions | Implemented |
| E_RESOURCE_ESCAPE | A SQLite transaction handle escapes its `with db.begin() as tx` scope or is used other than as the direct receiver of `execute` or `commit`; a resource handle is stored in a list, a map value, or a rebindable `var` local; a trait implementation target or trait dispatch stores a resource handle; or a lambda parameter, capture, or result contains a resource/capability handle | Implemented for transaction handles, stored nominal shapes, list elements, map values, rebindable locals, static-trait targets/dispatch, and the immediate lambda subset |

Typed CLI runtime parsing reports stable stderr codes and exits 2: `CLI_UNKNOWN_COMMAND`, `CLI_UNKNOWN_OPTION`, `CLI_MISSING_ARGUMENT`, `CLI_MISSING_VALUE`, `CLI_DUPLICATE_OPTION`, or `CLI_INVALID_VALUE`. Empty or NUL `FilePath` arguments and invalid `i32` values report `CLI_INVALID_VALUE`. Control characters in diagnostic subjects are escaped so each parse error stays on one physical stderr line. These are runtime results, not compiler diagnostics. `Ok(Text)` writes stdout and exits 0; formatted `Err(E)` writes stderr and exits 3; unexpected runtime faults exit 70. Top-level and command help exit 0. Ctrl+C cancellation of an async CLI command exits 130.

Startup configuration failures are runtime results, not compiler diagnostics. A missing required environment variable reports only its declared config field name and derived `LANG_CONFIG_<UPPER_SNAKE_KEY>` name; values are never included. Required means the variable is present, so an empty present value is valid. CLI startup exits 78 on a missing required value. A web package fails before opening its listener when startup configuration is missing.

Constrained process failures are typed runtime values, not compiler diagnostics. `ProcessRunner.run_text_async` returns `ProcessError.InvalidArgument` for invalid/NUL-containing arguments, more than 128 arguments, or over 16 KiB combined UTF-8 argument bytes; `InputTooLarge` for stdin over 1 MiB; `OutputTooLarge` when either raw output stream exceeds 1 MiB; `InvalidText` for invalid strict UTF-8 input or output; `StartFailed` for launch, executable hash, or Windows compatibility failures; and `TimedOut` after the fixed 10-second overall timeout. Nonzero process exits return `Ok(ProcessOutput)` with the exit code and both output strings. Host cancellation propagates as cancellation after best-effort process-tree termination and a reap bounded to two seconds; cancellation is not a `ProcessError` variant.

Outbound HTTP failures are typed runtime values, not compiler diagnostics. `HttpClient.get_text_async` returns `HttpError.InvalidTarget`, `Transport`, `Timeout`, `ResponseTooLarge`, or `InvalidText` for the corresponding adapter failures; host cancellation propagates. HTTP status codes, including 4xx and 5xx, remain `HttpResponse` values.

`lang inspect effects PACKAGE SYMBOL --json` reports malformed command usage through the normal usage message. An unknown, malformed, or non-`self` symbol reports structured `E_NAME_UNRESOLVED`. Compiler errors and missing/stale package locks stop inspection before an effects report is emitted.

An unsupported feature is a compilation failure. It never passes as an unchecked construct. The fixture manifest has 104 active entries and 0 pending entries, and bare `lang test` compares each fixture's complete ordered diagnostic-code list. Structural equality is limited to the immutable value types listed in the memory contract; unsupported values and unconstrained type parameters use `E_TYPE_MISMATCH`. Fixtures 61–64 cover immediate lambda execution and immutable capture plus mutable, resource, and generic capture rejection; fixtures 65–73 cover generic-struct arity, fields, inference, recursion, resource escape, and equality; fixtures 74–85 cover generic union syntax, arity, payload substitution, function interactions, recursive growth, resource escape, exhaustiveness, route/SQLite rejection, and forbidden pattern type arguments; fixtures 86–97 cover pure static trait bounds and dispatch, forwarded obligations, private/public visibility, recursive constraints, stored-resource rejection, implementation coherence, and signature validation; fixtures 98–104 cover newtype syntax/arity restrictions, explicit wrapping and projection, type-boundary errors, structural equality, recursive layout, resource representation rejection, and current API/audit metadata. Startup config declarations, accessor checks, grants, snapshots, redaction, and log serialization also have dedicated integration cases. `lang test FILE_OR_PACKAGE` typechecks module-level tests before running them. A non-boolean assertion uses `E_TYPE_MISMATCH`, a duplicate test name in one module uses `E_NAME_DUPLICATE`, and unsupported test-body statements or malformed blocks use the parser's standard `E_UNSUPPORTED` or `E_SYNTAX` codes. Assertion failures are runtime results printed with the test's module and source location; any failure makes the command exit nonzero while later tests continue.
