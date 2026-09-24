# Product requirements: agent-first general-purpose language, V1

**Status:** Implementation-ready product brief
**Date:** September 24, 2026
**Temporary working name:** `lang` for the executable and `.lang` for source files; the permanent name is pending.
**Primary target:** Reliable libraries, command-line tools, JSON web services, and small server-rendered web applications.
**Owner decision:** Build this as a new language and toolchain. Do not add features to Stasis or depend on Stasis internals.

## 1. Product thesis

Build a general-purpose language in which an AI agent can produce ordinary application code and receive precise, machine-readable feedback about types, error cases, side effects, capabilities, API responses, and foreign-code boundaries. The language must also be pleasant enough for a human developer to inspect and maintain.

**V1 promise:** A developer can implement a reusable library, a useful CLI, and a small web application with one toolchain. The compiler checks types, exhaustive matches, public function effects, capability access, and declared HTTP response variants. The build emits a truthful machine-readable audit of those checks. The tools work end to end on Windows and Linux.

This is a productivity-oriented language with managed memory. It permits ordinary dynamic allocation and does not attempt general proof, bounded execution, or machine-code optimization in V1.

### Why someone would try it

- A library publishes typed APIs and an effect summary that a caller can inspect before using it.
- A CLI gets typed argument parsing, generated help, predictable errors and exit codes, and a single build/run/test workflow.
- A web service gets typed routes, request decoding, exhaustive response mappings, and generated OpenAPI for supported JSON types.
- An agent can use structured diagnostics, stable symbol identifiers, and an effect/call graph instead of guessing from text alone.
- Foreign dependencies remain usable through adapters, with their trust boundary shown in the audit output.

## 2. Success criteria and limits

V1 is complete only when **three maintained example projects** build and run from a clean checkout:

1. **Library:** A reusable text/validation package consumed by the other examples. It contains public generic APIs, a tagged union, exhaustive matching, tests, and no I/O effects.
2. **CLI:** A file-processing command with typed arguments, generated `--help`, filesystem access through a capability, a useful nonzero exit on bad input, and a machine-readable effect audit.
3. **Web:** A small server-rendered page and JSON API backed by SQLite. It validates request input, escapes untrusted text in HTML, maps every declared normal response variant to one status, handles cancellation/shutdown, and exposes OpenAPI for the JSON route. A public, unauthenticated example is sufficient; do not invent an authentication system for this milestone.

All three must share the same language, module/package format, compiler, standard library conventions, build command, and diagnostic schema. A collection of unrelated demos is not a V1.

**Operational target:** A developer who already has the pinned .NET SDK can clone the repository, run the documented bootstrap, execute `lang test`, and run the CLI and web examples on both supported OSes. Builds should succeed offline once declared dependencies have been restored into the cache. V1 makes no numerical speed or binary-size guarantee, and performance is not a completion gate; PERF1 still requires reproducible measurements before performance or representation claims are made. No formal-security claim is a V1 acceptance criterion.

## 3. Decisions fixed for V1

| Area | V1 decision | Reason |
| --- | --- | --- |
| Implementation | Keep the C# bootstrap compiler and build tool, emit C#, and pin a supported .NET LTS SDK/runtime for V1. SH1 may add a lang-authored compiler; the first-party runtime may remain in C#. | A reliable bootstrap and mature runtime facilities coexist with staged compiler self-hosting. |
| Backend | Type-check and lower into a typed internal IR, then emit C# and compile with the pinned SDK. One backend only. | Ship runnable applications before designing a native or Wasm backend. |
| Memory | Use the .NET GC-managed runtime for allocations; ordinary source values may lower to references or inline/value types. Use scoped handles for files, transactions, and response bodies. Define portable sharing/resource semantics in MS1; C# layout is a lowering choice. | Application ergonomics with explicit resource cleanup and semantics independent of emitted representation. |
| Null/error | No implicit nullable values; `Option<T>` and `Result<T,E>`. No exceptions for expected failures in language APIs. | Make absent values and routine errors visible in types. |
| Generic code | Generic functions/types and minimal static traits with compile-time dispatch. | Real libraries without runtime reflection. |
| Side effects | Compiler-known effects plus explicit capability values; public APIs declare effects. | Audit the call graph and keep ambient I/O out of ordinary source code. |
| Foreign libraries | Audited .NET adapters with declared effects/capabilities. Adapter claims are marked trusted, not compiler-proven. | Avoid a zero-library launch without misrepresenting guarantees. |
| Web | JSON routes plus a small safe HTML construction API. Server-rendered pages; no client-side framework or asset bundler. | Cover the user's initial web-app and infrastructure use cases. |
| Database | Parameterized SQLite adapter with runtime row validation. | Provide a useful persistent example; static SQL/schema checking comes later. |
| Packages | Workspace/local packages and Git dependencies pinned to exact commits, with a lockfile and content hashes. | Reproducible consumption without building a public registry. |

These decisions are product requirements for the initial implementation. If an implementation finding forces a change, record the decision and update the examples and acceptance criteria in the same change.

## 4. V1 language requirements

The compiler must reject invalid programs rather than relying on a formatter, linter, or an agent instruction to make them safe.

### 4.1 Core syntax and types

- UTF-8 modules, explicit imports/exports, one package manifest, and separate modules per file. Public declarations form the package API.
- `bool`, `i32`, `i64`, `u32`, `u64`, `f64`, `Text`, `Bytes`, and `Unit`; structs, nominal newtypes, tagged unions, generic types/functions, `List<T>`, and `Map<K,V>`.
- Functions, local immutable `let` and explicit mutable `var`, conditionals, loops, calls, basic lambdas, `async`/`await`, and `match`.
- `Option<T>` and `Result<T,E>` in the core library. `match` must cover every union variant; adding a variant must break matches that are no longer exhaustive. Wildcard arms are permitted only when explicit in source.
- Minimal traits: signatures and statically selected implementations, sufficient for reusable collections and formatting. No trait objects, inheritance, associated-type system, or implicit conversions in V1. Keep trait coherence simple: one visible implementation for a concrete type/trait pair in a package graph.
- No `null` value, implicit numeric narrowing, ordinary-source reflection, macros, or arbitrary .NET calls. An `unsafe` or foreign adapter must use a separate trusted-package mechanism.
- Integer arithmetic checks overflow by default and raises a defined runtime fault; provide explicitly named checked-result, saturating, and wrapping operations. A runtime fault is **not** evidence that every program is total. Floating-point cross-platform determinism is not guaranteed.
- `Result` propagation (`?` or a documented equivalent) and explicit mapping of errors. Language API failures that a caller is expected to handle must use `Result`; adapter exceptions must be converted to documented error variants at the boundary.
- Scoped resources use a `with` construct. A scoped resource cannot be stored in a global or returned from its scope. Dispose on success, error, and cancellation. In V1 this is lexical lifetime checking, not a general Rust-style borrow checker. Transaction adapters roll back by default unless committed.

**Syntax freeze:** Before building the parser beyond a spike, create a small grammar and 20 canonical programs in the repository. These programs, including the examples below, become executable fixtures. The implementation may refine punctuation and naming once during this initial grammar step; it must then update this PRD and fixtures together. Semantic requirements above are authoritative.

Illustrative library surface:

```text
module text.normalize;

pub union NormalizeError {
    Empty,
    TooLong(max: u32),
}

pub fn normalize(input: Text) -> Result<Text, NormalizeError>
    effects {} {
    if input.length == 0 {
        return Err(NormalizeError.Empty);
    }
    return Ok(input.trim());
}
```

### Current implementation slice and syntax decisions

The current parser implements the pure subset described in [docs/grammar.md](grammar.md): i32, bool, Text, immutable non-generic nominal structs, declared non-generic unions, Option<T>, Result<T, E>, local let declarations, returns, calls, checked arithmetic, field construction and reads, and exhaustive match expressions. Struct construction uses named fields and requires each declared field exactly once; initializer expressions are evaluated in source order. Direct cycles through bare struct fields are rejected, while recursive paths through Option, Result, or a tagged union are allowed. Generated heap-allocated C# records are a prototype lowering, not a source-level allocation, identity, or layout promise. User-defined generic functions and types, imports, effects, capabilities, traits, CLI declarations, and routes remain unimplemented requirements for V1.

Union variants use either named fields, for example TooLong(max: i32), or positional fields, for example Value(i32); a variant cannot mix them. Construct declared variants with Choice.Yes or Choice.Value(3). Built-in Option and Result constructors are Some(value), None, Ok(value), and Err(error), and their type comes from an expected annotation, return type, or parameter. Match arms are comma-separated and use =>. Patterns name union variants, bind payloads positionally, or use the explicit wildcard _. Text literals support escaped quote, backslash, newline, carriage return, tab, and NUL characters. The exact productions and unsupported syntax are maintained in docs/grammar.md. Identifier names are contextual: module path segments and unambiguous member positions accept any identifier token, including words used as grammar keywords elsewhere. Bare declaration, type-root, binding, and unqualified pattern names exclude only true, false, null, match, if, await, and with; these retain their literal, match, or unsupported-expression behavior. Keyword-like words such as route are ordinary identifiers when the grammar expects a name, while route declarations are not implemented.

For the current command-line runner, main must take no arguments and return i32, bool, or Text. lang run prints the returned value followed by a newline and exits 0; checked i32 arithmetic overflow reports a generic runtime fault and exits 70. lang build creates an executable when that supported entrypoint exists and otherwise creates a library DLL. This sample output behavior is separate from the eventual typed CLI exit-code mapping in this PRD.
### 4.2 Effects and capabilities

- Define a closed V1 effect vocabulary: `fs.read`, `fs.write`, `process.spawn`, `net.client`, `net.listen`, `db.read`, `db.write`, `env.read`, `clock.read`, `log.write`, and `secret.reveal`. Add a new effect only with a documented adapter and conformance tests.
- Every function has an inferred transitive effect set, including effects of called functions. Public functions must declare an upper bound; the compiler rejects a body or call chain whose inferred set is outside that bound. `effects {}` means no modeled I/O effects, not zero allocation or a proof of mathematical purity.
- A function can perform an effect only when it receives the appropriate capability value, directly or through a capability-bearing service. Importing a package must not grant ambient I/O. The trusted application root creates capabilities from a manifest and passes them into handlers/commands.
- Record the shortest useful call path explaining each public effect. Handle recursion with a call-graph fixed point. `lang inspect effects <symbol>` must show declared, inferred, and foreign-declared effects separately.
- Foreign adapters have explicitly declared effects and may require capability parameters, but the compiler cannot prove the internal .NET code honors its declaration. The build receipt must identify this limit.
- The runtime may issue capabilities only for operations declared in the application manifest. V1 capabilities constrain code written in this language and first-party adapters; they do **not** provide a security sandbox against malicious .NET code in the same process.

Example contract:

```text
pub async fn load_user(db: DbRead, id: UserId)
    -> Result<Option<User>, DbError>
    effects { db.read } {
    // Implementation uses the SQLite adapter and a parameterized query.
}
```

**Negative fixture:** Calling a filesystem-reading function from `effects {}` must report the caller, the transitive effect, and the shortest call path. **Negative fixture:** Calling a network adapter without a network capability must fail compilation.

### 4.3 Async and failure boundaries

- `async` functions have typed results and compose with `await`. HTTP requests and CLI commands receive cancellation from the host; first-party I/O adapters propagate it.
- A request handler must return a declared response variant for ordinary application outcomes. Unexpected runtime faults are caught by the host, logged with a request identifier, and returned as a generic 500 without secret details. Such faults are reported separately from typed response completeness.
- A CLI maps a successful result to exit 0 and a typed command failure to a documented nonzero exit. Unexpected faults use a distinct exit code.
- Detached tasks and general concurrent shared-state features are outside V1. The standard web host may use concurrency internally, but language code cannot spawn untracked background work.

## 5. Libraries, toolchain, and application features

### 5.1 One executable

V1 commands:

| Command | Required behavior |
| --- | --- |
| `lang new lib|cli|web NAME` | Create a compilable minimal project and tests. |
| `lang check [--json]` | Parse, resolve, type-check, check matches/effects/capabilities/routes, return a stable nonzero failure code. |
| `lang build` / `lang run` | Build through the pinned backend, then optionally run. Do not skip checks. |
| `lang test` | Discover and run language tests; report source locations and a nonzero exit on failure. |
| `lang fmt --check` / `lang fmt` | Deterministic formatting; check mode does not modify files. |
| `lang add PATH|GIT_URL#COMMIT` | Add an exact local or pinned-Git dependency and update the lockfile. |
| `lang audit [--json]` | List package graph, hashes, effects, capabilities, adapters, and checked versus trusted claims. |
| `lang inspect symbols|calls|effects [--json]` | Query the semantic index for agents and developers. |

All commands use the same compiler and diagnostic codes. No editor integration or daemon is required. Successful `lang build` emits the executable plus the manifest and receipt described below.

#### Current command implementation

The current compiler accepts lang check FILE [--json], lang build FILE, lang run FILE, and lang test. JSON output is supported on check and includes the schema version and ordered diagnostics. lang test compares active fixture diagnostic codes exactly; it does not yet discover language-level tests. Build artifacts are copied into a unique out/<source-name>-<id>/ directory beside the source file. Fixtures for effects, capabilities, resource scopes, language CLI commands, and routes remain pending.
### 5.2 Package format and foreign boundary

`lang.toml` declares package name, version, entry kind (`lib`, `cli`, `web`), supported target, dependencies, and application capabilities. `lang.lock` records resolved dependency identity and content hash. Builds fail if pinned content differs from the lockfile. Workspace path dependencies are content-hashed for the receipt but remain editable during local development. Test a clean offline rebuild with a populated cache.

Normal packages contain language source and obtain effect summaries from compilation. A **foreign adapter package** explicitly declares a pinned .NET dependency, exposed language signatures, capability requirements, and trusted effect claims. It has wrapper tests and an audit entry for every exposed operation. Ordinary source cannot reference .NET types or load assemblies directly. V1 does not provide a remote package registry, semver solving, or public publishing.

First-party adapters required for V1: filesystem, process, environment/config, clock, structured logging, HTTP client, HTTP server, JSON, HTML builder, and SQLite. TLS for production servers may terminate at a reverse proxy; the HTTP client adapter uses the platform's normal TLS stack. Do not claim either path is formally verified.

### 5.3 CLI contracts

A command declaration defines positional arguments, named options, flags, typed defaults, help text, and a handler. Generate parsing, validation errors, `--help`, and stable exit behavior from it. Command handlers receive only declared capabilities. Provide a machine-readable command schema in the build output. Shell completions and man pages are later work.

Illustrative command surface:

```text
command scan {
    argument input: FilePath;
    flag recursive;
    handler: scan_files;
}

pub fn scan_files(args: ScanArgs, fs: FsRead)
    -> Result<Unit, ScanError>
    effects { fs.read } {
    // Use the passed filesystem capability; report expected failures as Err.
}
```

The generated `ScanArgs` type and command signature must agree at compile time. The application manifest must grant `fs.read` before the trusted root can pass `FsRead` to this command.

### 5.4 Web contracts

A route declaration defines HTTP method, path parameters, optional query and JSON body types, a handler, and a closed union of normal response variants. Each variant maps exactly once to a status, content type, and payload type. Compiler errors cover missing mappings, duplicate mappings, mismatched handler return types, and unsupported parameter codecs. Decode and validate input before calling the handler. Input size and request timeout have configurable safe defaults.

For supported primitive/struct/union JSON shapes, emit an OpenAPI document from the same typed route model. OpenAPI generation must fail explicitly for an unsupported public shape; it must not silently emit a misleading schema. Runtime 500 for a defect or foreign exception is outside the declared normal variants and documented as such.

Illustrative route surface:

```text
pub union GetUserReply {
    Found(User),
    Missing,
    Failed(ErrorBody),
}

route GET "/users/{id}" {
    path id: UserId;
    handler: get_user;
    response Found: 200 json User;
    response Missing: 404;
    response Failed: 500 json ErrorBody;
}
```

A safe HTML builder distinguishes escaped text from an HTML node. Rendering an arbitrary `Text` value escapes markup. No public raw-HTML insertion operation exists in ordinary V1 source. Static assets may be served from a declared directory; do not build a CSS/JS pipeline. No authentication, sessions, or CSRF framework is promised in V1, so examples must avoid authenticated or state-changing browser forms.

### 5.5 SQLite and configuration

- SQLite operations accept parameterized statements and typed parameters; do not expose a convenience API that concatenates values into SQL. Validate column names/types/nullability during row decoding at runtime and return a typed error. Static schema-aware SQL checking is later work.
- Provide `DbRead` and `DbWrite` capabilities and transactions whose `with` scope rolls back unless committed. Integration tests cover failed statements and rollback.
- Application configuration is declared centrally with field type, required/default status, and secret status. Validate at startup and emit an example configuration file without values for secrets. `Secret<T>` is redacted by the standard formatter/logger. Reveal requires a capability and is included in the effect audit. A revealed ordinary value can be mishandled by code; V1 does not promise end-to-end taint tracking.
- HTTP host supports shutdown cancellation, request IDs, and structured error logging. Provide a `/health` endpoint in the example as an ordinary declared route, not hidden framework behavior.

## 6. Compiler architecture and machine interface

Required pipeline:

```text
source + locked packages
  -> parser with source spans
  -> name/type resolution
  -> typed semantic IR
  -> exhaustive-match, resource-scope, effect, capability, route checks
  -> checked program snapshot
  -> C# emitter + first-party runtime
  -> .NET build
  -> executable + schemas + audit receipt
```

A backend must consume only a successfully checked program snapshot. Keep the typed IR and portable language semantics independent of C# syntax and object layout; checks must not depend on emitted source. Emit reproducible source and preserve diagnostics mapping back to original `.lang` spans. See [the memory and performance contract](memory-and-performance.md) for MS1 and PERF1.

**Diagnostic JSON:** Stable `code`, `severity`, `message`, file/range, symbol ID when available, and related locations/call path. Human messages may improve without changing the code. Examples: `E_MATCH_NONEXHAUSTIVE`, `E_EFFECT_EXCEEDED`, `E_CAPABILITY_MISSING`, `E_ROUTE_RESPONSE_MISSING`, `E_RESOURCE_ESCAPE`, `E_TYPE_MISMATCH`. Bad source must never produce a stack trace as the primary compiler diagnostic.

**Semantic IDs:** Public and module-level symbol IDs derive deterministically from locked package identity, module path, declaration kind, and declared name. A body edit or formatting change must not change the ID; a rename or package-identity change may. Local expression IDs are build-scoped and need not survive edits. No semantic-edit mutation API in V1.

**Read-only query interface:** `lang inspect --json` exposes symbols, signatures, callers/callees, declared/inferred effects, required capabilities, and foreign boundaries. Version the JSON schema. The compiler is the source of these facts; do not produce them by grepping generated C#.

**Build receipt:** Emit one `build-receipt.json` with toolchain version, target, source/package hashes, checks actually passed, effects/capabilities, foreign dependencies and their declared claims, and paths/hashes for the binary, command schema, and OpenAPI if present. It is an audit record, not a formal proof or a cryptographic certificate. Never set `boundsProven`, `contractProven`, `secure`, or `deterministic` based on tests or adapter declarations.

## 7. Agent implementation plan and gates

Keep the repository buildable after every milestone. Each milestone adds a working path from source to an observable result, negative fixtures for its new rules, and JSON diagnostics. Do not create broad stubs marked “implemented.”

| Gate | Deliverable | Must demonstrate |
| --- | --- | --- |
| M0 — Specification fixtures | Grammar draft, 20 tiny valid/invalid examples, error-code registry, repository bootstrap and CI. | Examples include union matching, null rejection, effects, missing capabilities, scope escape, CLI, and route mapping. |
| M1 — Pure library | Parser, modules, types, generics, minimal traits, `Option`/`Result`, matches, tests, typed IR, C# emission. | The library example compiles, is imported into another package, passes tests, and rejects a missing union arm. |
| M2 — Semantic audit | Effect inference/declarations, capability checks, stable public IDs, `check --json`, `inspect`, first receipt. | A transitive I/O call fails a pure public contract with an exact call path; IDs survive formatting edits. |
| M3 — CLI and packages | Build/run/test/fmt/new/add, lockfile, scoped resources, first-party filesystem/process/config/log adapters. | The example CLI runs, prints help, handles a bad path, reports effects, and builds offline from the cache. |
| M4 — Web foundation | Async/await, HTTP server/client, JSON codecs, typed routes, response checking, OpenAPI, HTML builder. | The example returns a valid JSON response, rejects malformed input, catches an unexpected fault, escapes HTML text, and shuts down cleanly. |
| M5 — Persistence and release | SQLite adapter, transactions, web example, remaining audit fields, Windows/Linux CI and packaging. | All three examples and acceptance tests pass from a clean checkout on both OSes. |
| MS1 — Memory-semantics design (not started) | Specify source-level value, sharing, mutation, and scoped-resource behavior before representation changes lock semantics. | Publish a decision matrix and positive/negative conformance programs; use focused prototypes to compare representations without requiring two full production lowerings. Scope resource ownership decisions to lexical handles. MS1 is a prerequisite for SH1. |
| PERF1 — Performance evaluation (not started) | Measure representative library/compiler, CLI, and later web/SQLite workloads; evaluate default .NET deployment, NativeAOT, and value-type lowering. | Publish reproducible raw measurements and compatibility limits; no numerical performance target is imposed. See [the measurement contract](memory-and-performance.md). |
| SH1 — Self-hosted compiler (parallel follow-on after M3 and MS1) | Port the compiler in stages while preserving the C# bootstrap and C# output backend. | On Windows and Linux, stage 0 builds stage 1 and stage 1 rebuilds stage 2; stages 1 and 2 produce deterministic matching C# and equivalent behavior/diagnostics on a pinned conformance corpus. SH1 does not block M4/M5 or complete V1. |

An agent implementing a gate must submit: working code, added positive and negative fixtures, updated generated schema/receipt examples, a short list of claims the compiler actually checks, and unresolved limitations. A gate cannot be called complete on an illustrative parser or mocked backend.

### MS1: memory-semantics design (not started)

MS1 is a design gate, not a claim that a borrow checker or ownership model is implemented. It must precede SH1 and any new mutable-sharing or resource-lifetime semantics that would commit source behavior. Resolve the design questions and required conformance programs in [the memory contract](memory-and-performance.md); list unresolved decisions explicitly.

### PERF1: performance evaluation (not started)

PERF1 is a reproducible evaluation track, not a performance promise or V1 completion gate. Its workload, measurement, and comparison rules are in [the measurement contract](memory-and-performance.md). Use evidence to inform representation and deployment choices; record unsupported NativeAOT adapters and compatibility warnings/errors.

### SH1: staged compiler self-hosting (not started)

SH1 may start after M1's reusable-library path, M3's useful CLI, and MS1 are complete, with M2 effect and capability support available. PERF1 results should inform its representation and deployment choices. It is a parallel follow-on after M3: it must not delay or replace M4/M5, and passing SH1 does not redefine V1 completion. Before the port, the language needs usable control flow, `Text` and collection APIs, generics, modules, diagnostics, and capability-controlled filesystem/process access.

Port in three steps: build a useful formatter or source tool in lang; port the lexer and parser with differential checks against the C# bootstrap; then port the checker, typed IR, and C# emitter. Stage 0 is the maintained C# bootstrap that builds compiler stage 1 from lang-authored compiler sources; stage 1 rebuilds those same sources as stage 2. The compiler continues to emit C# and use the pinned .NET SDK/runtime, and the first-party runtime may remain C#.

Acceptance pins the bootstrap/compiler sources, toolchain, dependencies, and conformance corpus. On Windows and Linux, run the existing independent positive and negative constraint gates against both compiler stages; compare stage 1 and stage 2 generated C# deterministically for the same compiler source and corpus, and compare behavior and diagnostics. Normalize paths or line endings only when needed, without hiding semantic differences. Keep the known-good C# bootstrap available for recovery until replacement is separately justified. This gate makes no claim of identical binaries or a trust proof and does not require a native backend or a rewritten runtime.

## 8. Acceptance tests

Automate these in CI; the named behaviors are contractual even if final syntax changes during M0.

| ID | Test | Expected result |
| --- | --- | --- |
| A01 | Clean library/CLI/web builds and tests on Windows and Linux. | All pass with the pinned SDK and locked dependencies. |
| A02 | Add a union variant without updating a `match`. | `E_MATCH_NONEXHAUSTIVE` with the omitted variant and source span. |
| A03 | Assign `Option<User>` to `User` without handling `None`. | Type error; no implicit null-like unwrapping. |
| A04 | Pure public function calls private helper that reads a file. | `E_EFFECT_EXCEEDED` and a helper-to-I/O call path. |
| A05 | Function calls HTTP client without a network capability. | `E_CAPABILITY_MISSING`; ordinary import supplies no authority. |
| A06 | Scoped transaction escapes its scope or is stored globally. | `E_RESOURCE_ESCAPE`; a failed in-scope transaction rolls back in integration test. |
| A07 | Route reply union adds a variant with no status mapping. | `E_ROUTE_RESPONSE_MISSING`; generated OpenAPI remains unavailable until fixed. |
| A08 | JSON route receives malformed body and an oversized body. | Documented client errors; handler not invoked. |
| A09 | Server-rendered page includes `<script>` in a text field. | Literal escaped text in output, no executable element. |
| A10 | Parameterized SQLite query receives a malicious-looking string. | Treated as data; no change to SQL structure. |
| A11 | `lang audit --json` on a program using SQLite. | Separates compiler-checked language code from trusted adapter declarations. |
| A12 | Format or change a function body, then inspect its public symbol ID. | ID unchanged; diagnostics and receipt still refer to current source. |
| A13 | Modify a pinned dependency without updating its lock entry. | Build fails on mismatch; clean cached dependency rebuild works offline. |
| A14 | CLI run with `--help`, invalid flag, expected domain error, and runtime fault. | Generated help; distinct predictable exit behavior and no secret value in error output. |
| A15 | Cancel an active HTTP request and stop the server. | First-party I/O observes cancellation; server exits without a stranded in-language task. |
| A16 | Remove an application's declared `fs.read` capability while its command needs `FsRead`. | Check or startup rejects the application; no capability is silently synthesized. |

A passing test says only what its assertion covers. The receipt must distinguish compile-time checks from these runtime/integration tests.

## 9. Explicitly deferred

- Theorem proving, `requires`/`ensures`, refinement types, resource/termination bounds, and certified packages.
- Native code and Wasm backends; small self-contained executables are a possible later goal, not a V1 claim.
- Stable IDs for all expression nodes; semantic-edit protocol or LSP write operations.
- Full borrow/ownership checking, actors, detached jobs, distributed execution, and a concurrency framework beyond `async`/`await`.
- Compile-time SQL validation, migrations, PostgreSQL, ORM, and generated database models.
- Authentication/authorization framework, browser sessions, CSRF protection, SPA build system, client-side UI framework, and automatic deployment/container generation.
- Public package registry, public publishing, unrestricted NuGet interoperability, and automatic verification of foreign adapters.
- End-to-end information-flow tracking for secrets, formal sandboxing of third-party .NET code, and a claim that effect checks alone make a service secure.

These are candidates for later versions only after the three V1 applications are genuinely useful.

## 10. Scope-control rule

When a requested feature threatens completion, first ask whether it is needed for the library, CLI, or small web example and whether a simple first-party adapter meets the need. Prefer completing and testing one working vertical slice to adding syntax without a running application. Do not weaken a failing check, silently turn a typed failure into a generic success, or mark a trusted-adapter assertion as compiler-proven to satisfy a gate.

**First task for the implementing agent:** Set up the repository, pin the SDK, write the grammar draft and 20 fixtures in M0, then implement the pure library path through M1. Report any semantic ambiguities as concrete proposed decisions with matching fixtures; continue on unblocked work.
