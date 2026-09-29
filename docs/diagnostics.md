# Diagnostics

Compiler diagnostics have stable `code`, `severity`, `message`, `file`, and `range` fields. Ranges are one-based line and column positions with an exclusive end. Driver diagnostics use the same shape. A successful `lang check FILE --json` returns schema version 1 with an empty `diagnostics` array.

| Code | Meaning | Status |
| --- | --- | --- |
| E_SYNTAX | Token or production does not match the grammar | Implemented |
| E_UNSUPPORTED | Valid V1 construct is not yet implemented | Implemented |
| E_NAME_UNRESOLVED | Name is not declared in scope, or a qualified reference names an unknown alias, module, or declaration | Implemented |
| E_NAME_DUPLICATE | Duplicate declaration | Implemented |
| E_ACCESS_PRIVATE | A qualified reference crosses a module or package boundary to a private declaration | Implemented |
| E_MANIFEST | Package manifest file, schema, key, or value is invalid, including an unsupported or duplicate key/grant, a malformed `[config]` declaration, an invalid `http_origin` or `net.client`/origin pairing, or any application capability/config declaration in a library package | Implemented |
| E_DEPENDENCY | A local dependency path, package kind, package identity, or dependency graph is invalid | Implemented |
| E_LOCK | A required dependency lock is missing, malformed, unreadable, or stale | Implemented |
| E_MODULE_PATH | A module header does not match its source-root-relative file path | Implemented |
| E_MODULE_DUPLICATE | A package includes more than one source for the same package/module identity | Implemented |
| E_TYPE_MISMATCH | Expression, payload, argument, local, condition, comparison, or return type mismatch; also a function body that may fall through | Implemented for the pure type and control-flow slices |
| E_AWAIT_CONTEXT | `await` appears outside an async function | Implemented |
| E_AWAIT_SYNC | `await` targets a synchronous call or intrinsic | Implemented |
| E_AWAIT_TARGET | `await` target is not an async call or intrinsic | Implemented |
| E_ASYNC_CALL_UNAWAITED | An async call is used without `await` | Implemented |
| E_ASSIGN_IMMUTABLE | Assignment targets a `let` local or immutable loop binding | Implemented |
| E_TYPE_VISIBILITY | A public type signature exposes a private union or struct | Implemented |
| E_FIELD_UNKNOWN | A struct initializer or field read names an unknown field | Implemented |
| E_FIELD_MISSING | A struct construction omits a declared field | Implemented |
| E_FIELD_DUPLICATE | A struct construction initializes a field more than once | Implemented |
| E_MATCH_NONEXHAUSTIVE | One or more union variants are missing from a match | Implemented |
| E_MATCH_ARM_DUPLICATE | A match arm is duplicated or follows a wildcard | Implemented |
| E_UNREACHABLE | A statement follows a statement or `if` whose every path returns | Implemented |
| E_ENTRYPOINT | `lang run` has neither a supported zero-argument `main` nor a typed command entry, or a CLI package's entry module is absent or has neither supported entry form | Implemented |
| E_IO | Source, fixture, or generated build files could not be read or written | Implemented |
| E_PROCESS | The .NET build or run process could not be started or waited on | Implemented |
| E_BUILD | The generated C# project failed to compile | Implemented |
| E_BUILD_TARGET | A recognized `--aot` or `--rid` option is malformed or used outside the supported build form, the RID is unsupported or targets a different host OS, or the program lacks an executable entrypoint | Implemented |
| E_EFFECT_EXCEEDED | Inferred direct or transitive effect exceeds a function's declared upper bound; the message includes the shortest known call path | Implemented for filesystem, SQLite database, HTTP client, environment config, secret reveal, and structured log operations |
| E_EFFECT_UNKNOWN | Effect annotation names a value outside the closed effect vocabulary | Implemented |
| E_EFFECT_DUPLICATE | Effect annotation repeats a value | Implemented |
| E_CAPABILITY_MISSING | An operation requires an opaque capability that is absent, has the wrong type, or is not granted to the CLI command or web route entry | Implemented for `FsRead`, `FsWrite`, `DbRead`, `DbWrite`, `HttpClient`, `Config`, `Secrets`, and `Logger`; root CLI/web grants and required `http_origin` are checked |
| E_CAPABILITY_SCOPE | A root-package-only startup service or secret type is used in a library package | Implemented for `Config`, `Secrets`, `Logger`, and `Secret<Text>` scope checks |
| E_CONFIG_KEY | Config accessor key is not a literal declared in the root `[config]` schema | Implemented |
| E_CONFIG_TYPE | Config accessor does not match the declared `Text` or `Secret<Text>` field type | Implemented |
| E_DB_SQL_LITERAL | A SQLite operation's SQL argument is not a string literal | Implemented |
| E_DB_READ_STATEMENT | `DbRead.query_one` does not contain one supported `SELECT` statement without a semicolon | Implemented |
| E_DB_TRANSACTION_STATEMENT | `Transaction.execute` does not contain one supported `INSERT`, `UPDATE`, `DELETE`, or `REPLACE` statement without a semicolon | Implemented |
| E_DB_PARAMETERS | A SQLite operation is missing its parameter struct | Implemented |
| E_DB_RESULT_TYPE | `DbRead.query_one` lacks an expected `Result<Option<Row>, DbError>` type | Implemented |
| E_DB_CODEC_UNSUPPORTED | A SQLite parameter or row struct uses an unsupported shape or field type | Implemented |
| E_COMMAND_DECL | Typed CLI command declaration is malformed, duplicated, uses an unsupported type/default (including empty or NUL `FilePath` option defaults), appears in a library package, conflicts with `main`, or violates the command-count/argument requirements | Implemented for PR1 typed commands |
| E_COMMAND_HANDLER | Command handler or error formatter is not a fully qualified function with the required generated argument/result signature | Implemented for PR1 typed commands |
| E_ROUTE_DECL | Invalid route placement, method/path/body/status, unknown response variant, or duplicate method/path | Implemented |
| E_ROUTE_HANDLER | Missing, duplicate, or signature-incompatible route handler | Implemented; unresolved/private qualified references retain `E_NAME_UNRESOLVED`/`E_ACCESS_PRIVATE` |
| E_ROUTE_RESPONSE_DUPLICATE | A response variant is mapped more than once | Implemented |
| E_ROUTE_RESPONSE_MISSING | A declared response variant has no mapping | Implemented |
| E_ROUTE_CODEC_UNSUPPORTED | Unsupported response payload, content format, or JSON shape | Implemented |
| E_RESOURCE_ESCAPE | A SQLite transaction handle escapes its `with db.begin() as tx` scope or is used other than as the direct receiver of `execute` or `commit`; a resource handle is stored in a list or rebindable `var` local | Implemented for transaction handles, list elements, and rebindable locals |

Typed CLI runtime parsing reports stable stderr codes and exits 2: `CLI_UNKNOWN_COMMAND`, `CLI_UNKNOWN_OPTION`, `CLI_MISSING_ARGUMENT`, `CLI_MISSING_VALUE`, `CLI_DUPLICATE_OPTION`, or `CLI_INVALID_VALUE`. Empty or NUL `FilePath` arguments and invalid `i32` values report `CLI_INVALID_VALUE`. Control characters in diagnostic subjects are escaped so each parse error stays on one physical stderr line. These are runtime results, not compiler diagnostics. `Ok(Text)` writes stdout and exits 0; formatted `Err(E)` writes stderr and exits 3; unexpected runtime faults exit 70. Top-level and command help exit 0. Ctrl+C cancellation of an async CLI command exits 130.

Startup configuration failures are runtime results, not compiler diagnostics. A missing required environment variable reports only its declared config field name and derived `LANG_CONFIG_<UPPER_SNAKE_KEY>` name; values are never included. Required means the variable is present, so an empty present value is valid. CLI startup exits 78 on a missing required value. A web package fails before opening its listener when startup configuration is missing.

Outbound HTTP failures are typed runtime values, not compiler diagnostics. `HttpClient.get_text_async` returns `HttpError.InvalidTarget`, `Transport`, `Timeout`, `ResponseTooLarge`, or `InvalidText` for the corresponding adapter failures; host cancellation propagates. HTTP status codes, including 4xx and 5xx, remain `HttpResponse` values.

`lang inspect effects PACKAGE SYMBOL --json` reports malformed command usage through the normal usage message. An unknown, malformed, or non-`self` symbol reports structured `E_NAME_UNRESOLVED`. Compiler errors and missing/stale package locks stop inspection before an effects report is emitted.

An unsupported feature is a compilation failure. It never passes as an unchecked construct. The fixture manifest has 48 active entries and 0 pending entries, and bare `lang test` compares each fixture's complete ordered diagnostic-code list. Startup config declarations, accessor checks, grants, snapshots, redaction, and log serialization also have dedicated integration cases. `lang test FILE_OR_PACKAGE` typechecks module-level tests before running them. A non-boolean assertion uses `E_TYPE_MISMATCH`, a duplicate test name in one module uses `E_NAME_DUPLICATE`, and unsupported test-body statements or malformed blocks use the parser's standard `E_UNSUPPORTED` or `E_SYNTAX` codes. Assertion failures are runtime results printed with the test's module and source location; any failure makes the command exit nonzero while later tests continue.
