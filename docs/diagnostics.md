# Diagnostics

Compiler diagnostics have stable `code`, `severity`, `message`, `file`, and `range` fields. Ranges are one-based line and column positions with an exclusive end. Driver diagnostics use the same shape. A successful `lang check FILE --json` returns schema version 1 with an empty `diagnostics` array.

| Code | Meaning | Status |
| --- | --- | --- |
| E_SYNTAX | Token or production does not match the grammar | Implemented |
| E_UNSUPPORTED | Valid V1 construct is not yet implemented | Implemented |
| E_NAME_UNRESOLVED | Name is not declared in scope | Implemented |
| E_NAME_DUPLICATE | Duplicate declaration | Implemented |
| E_TYPE_MISMATCH | Expression, payload, argument, local, or return type mismatch | Implemented for the pure type slice |
| E_TYPE_VISIBILITY | A public type signature exposes a private union | Implemented |
| E_MATCH_NONEXHAUSTIVE | One or more union variants are missing from a match | Implemented |
| E_MATCH_ARM_DUPLICATE | A match arm is duplicated or follows a wildcard | Implemented |
| E_ENTRYPOINT | `lang run` has no supported zero-argument `main` function | Implemented |
| E_IO | Source, fixture, or generated build files could not be read or written | Implemented |
| E_PROCESS | The .NET build or run process could not be started or waited on | Implemented |
| E_BUILD | The generated C# project failed to compile | Implemented |
| E_EFFECT_EXCEEDED | Inferred effect exceeds public declaration | Planned |
| E_CAPABILITY_MISSING | Required capability absent | Planned |
| E_ROUTE_RESPONSE_MISSING | Response variant has no mapping | Planned |
| E_RESOURCE_ESCAPE | Scoped resource escapes its lexical scope | Planned |
| E_PACKAGE_LOCK_MISMATCH | Pinned content differs from lockfile | Planned |

An unsupported feature is a compilation failure. It never passes as an unchecked construct. `lang test` compares each active fixture's complete ordered diagnostic-code list; pending fixtures are skipped.
