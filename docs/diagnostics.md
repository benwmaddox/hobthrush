# Diagnostics

Compiler diagnostics have stable `code`, `severity`, `message`, `file`, and `range` fields. Ranges are one-based line and column positions with an exclusive end. Driver diagnostics use the same shape. A successful `lang check FILE --json` returns schema version 1 with an empty `diagnostics` array.

| Code | Meaning | Status |
| --- | --- | --- |
| E_SYNTAX | Token or production does not match the grammar | Implemented |
| E_UNSUPPORTED | Valid V1 construct is not yet implemented | Implemented |
| E_NAME_UNRESOLVED | Name is not declared in scope | Implemented |
| E_NAME_DUPLICATE | Duplicate declaration | Implemented |
| E_MANIFEST | Package manifest file, schema, key, or value is invalid | Implemented |
| E_MODULE_PATH | A module header does not match its source-root-relative file path | Implemented |
| E_IMPORT_UNRESOLVED | An imported module or symbol does not exist in the package | Implemented |
| E_IMPORT_PRIVATE | An import names a declaration that is not public | Implemented |
| E_IMPORT_CONFLICT | Imports are duplicated or conflict with another visible/local name | Implemented |
| E_TYPE_MISMATCH | Expression, payload, argument, local, or return type mismatch | Implemented for the pure type slice |
| E_TYPE_VISIBILITY | A public type signature exposes a private union or struct | Implemented |
| E_FIELD_UNKNOWN | A struct initializer or field read names an unknown field | Implemented |
| E_FIELD_MISSING | A struct construction omits a declared field | Implemented |
| E_FIELD_DUPLICATE | A struct construction initializes a field more than once | Implemented |
| E_MATCH_NONEXHAUSTIVE | One or more union variants are missing from a match | Implemented |
| E_MATCH_ARM_DUPLICATE | A match arm is duplicated or follows a wildcard | Implemented |
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
| E_PACKAGE_LOCK_MISMATCH | Pinned content differs from lockfile | Planned |

An unsupported feature is a compilation failure. It never passes as an unchecked construct. `lang test` compares each active fixture's complete ordered diagnostic-code list; pending fixtures are skipped.
