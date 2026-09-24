# Diagnostic codes

Compiler diagnostics have stable `code`, `severity`, `message`, `file`, `range`, and optional `symbolId`, `related`, and `callPath` fields. Ranges are one-based line and column positions with an exclusive end.

| Code | Meaning | Status |
| --- | --- | --- |
| E_SYNTAX | Token or production does not match the grammar | Implemented |
| E_NAME_UNRESOLVED | Name is not declared in scope | Implemented |
| E_NAME_DUPLICATE | Duplicate declaration | Implemented |
| E_TYPE_MISMATCH | Expression or return type mismatch | Implemented for `i32` slice |
| E_MATCH_NONEXHAUSTIVE | Missing union variant | Planned |
| E_EFFECT_EXCEEDED | Inferred effect exceeds public declaration | Planned |
| E_CAPABILITY_MISSING | Required capability absent | Planned |
| E_ROUTE_RESPONSE_MISSING | Response variant has no mapping | Planned |
| E_RESOURCE_ESCAPE | Scoped resource escapes its lexical scope | Planned |
| E_PACKAGE_LOCK_MISMATCH | Pinned content differs from lockfile | Planned |
| E_UNSUPPORTED | Valid V1 construct not yet implemented | Implemented |

An unsupported feature is a compilation failure. It never passes as an unchecked construct.
