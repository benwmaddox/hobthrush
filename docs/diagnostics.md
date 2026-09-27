# Diagnostics

Compiler diagnostics have stable `code`, `severity`, `message`, `file`, and `range` fields. Ranges are one-based line and column positions with an exclusive end. Driver diagnostics use the same shape. A successful `lang check FILE --json` returns schema version 1 with an empty `diagnostics` array.

| Code | Meaning | Status |
| --- | --- | --- |
| E_SYNTAX | Token or production does not match the grammar | Implemented |
| E_UNSUPPORTED | Valid V1 construct is not yet implemented | Implemented |
| E_NAME_UNRESOLVED | Name is not declared in scope, or a qualified reference names an unknown alias, module, or declaration | Implemented |
| E_NAME_DUPLICATE | Duplicate declaration | Implemented |
| E_ACCESS_PRIVATE | A qualified reference crosses a module or package boundary to a private declaration | Implemented |
| E_MANIFEST | Package manifest file, schema, key, or value is invalid | Implemented |
| E_DEPENDENCY | A local dependency path, package kind, package identity, or dependency graph is invalid | Implemented |
| E_LOCK | A required dependency lock is missing, malformed, unreadable, or stale | Implemented |
| E_MODULE_PATH | A module header does not match its source-root-relative file path | Implemented |
| E_TYPE_MISMATCH | Expression, payload, argument, local, condition, comparison, or return type mismatch; also a function body that may fall through | Implemented for the pure type and control-flow slices |
| E_TYPE_VISIBILITY | A public type signature exposes a private union or struct | Implemented |
| E_FIELD_UNKNOWN | A struct initializer or field read names an unknown field | Implemented |
| E_FIELD_MISSING | A struct construction omits a declared field | Implemented |
| E_FIELD_DUPLICATE | A struct construction initializes a field more than once | Implemented |
| E_MATCH_NONEXHAUSTIVE | One or more union variants are missing from a match | Implemented |
| E_MATCH_ARM_DUPLICATE | A match arm is duplicated or follows a wildcard | Implemented |
| E_UNREACHABLE | A statement follows a statement or `if` whose every path returns | Implemented |
| E_ENTRYPOINT | `lang run` has no supported zero-argument `main`, or a CLI package's entry module is absent or lacks that signature | Implemented |
| E_IO | Source, fixture, or generated build files could not be read or written | Implemented |
| E_PROCESS | The .NET build or run process could not be started or waited on | Implemented |
| E_BUILD | The generated C# project failed to compile | Implemented |
| E_BUILD_TARGET | A recognized `--aot` or `--rid` option is malformed or used outside the supported build form, the RID is unsupported or targets a different host OS, or the program lacks an executable entrypoint | Implemented |
| E_EFFECT_EXCEEDED | Inferred direct or transitive effect exceeds a function's declared upper bound; the message includes the shortest known call path | Implemented for the `FsRead` slice |
| E_EFFECT_UNKNOWN | Effect annotation names a value outside the closed effect vocabulary | Implemented |
| E_EFFECT_DUPLICATE | Effect annotation repeats a value | Implemented |
| E_CAPABILITY_MISSING | An operation requires an opaque capability that is absent or has the wrong type | Implemented for `FsRead.read_text` |
| E_ROUTE_RESPONSE_MISSING | Response variant has no mapping | Planned |
| E_RESOURCE_ESCAPE | Scoped resource escapes its lexical scope | Planned |

An unsupported feature is a compilation failure. It never passes as an unchecked construct. Bare `lang test` compares each active fixture's complete ordered diagnostic-code list; pending fixtures are skipped. `lang test FILE_OR_PACKAGE` typechecks module-level tests before running them. A non-boolean assertion uses `E_TYPE_MISMATCH`, a duplicate test name in one module uses `E_NAME_DUPLICATE`, and unsupported test-body statements or malformed blocks use the parser's standard `E_UNSUPPORTED` or `E_SYNTAX` codes. Assertion failures are runtime results printed with the test's module and source location; any failure makes the command exit nonzero while later tests continue.
