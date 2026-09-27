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

**Operational target:** A developer who already has the pinned .NET SDK can clone the repository, run the documented bootstrap, execute `lang test`, and run the CLI and web examples on both supported OSes. Builds should succeed offline once declared dependencies have been restored into the cache. V1 uses the .NET managed runtime with Workstation GC as its default, explicitly selected for the compiler and generated applications. Ordinary `lang build` and `lang run` remain managed; `lang build FILE_OR_PACKAGE --aot --rid RID` optionally publishes a GC-managed Native AOT executable for `win-x64` when built on Windows or `linux-x64` when built on Linux, with the required native toolchain. This applies to executable file programs and CLI packages, not library-only programs/packages or the compiler tool. V1 makes no numerical speed or binary-size guarantee, and performance is not a completion gate; PERF1 requires reproducible measurements before performance claims are made. No formal-security claim is a V1 acceptance criterion.

## 3. Decisions fixed for V1

| Area | V1 decision | Reason |
| --- | --- | --- |
| Implementation | Keep the C# bootstrap compiler and build tool, emit C#, and pin a supported .NET LTS SDK/runtime for V1. SH1 may add a lang-authored compiler; the first-party runtime may remain in C#. | A reliable bootstrap and mature runtime facilities coexist with staged compiler self-hosting. |
| Backend | Type-check and lower into a typed internal IR, then emit C# and compile with the pinned SDK. One backend only; Native AOT is an optional SDK publish mode for generated executable applications. | Ship runnable applications before designing a native or Wasm backend. |
| Memory | Use the .NET GC-managed runtime with Workstation GC explicitly selected. Use scoped handles for files, transactions, and response bodies. Define portable sharing/resource semantics in MS1; emitted C# layout is not a source-level promise. | Application ergonomics with explicit resource cleanup and semantics independent of emitted representation. |
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

- UTF-8 modules, qualified declaration references, one package manifest, and separate modules per file. Public declarations form the package API; there is no source-level import declaration.
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
module text::normalize;

pub union NormalizeError {
    Empty,
    TooLong(max: u32),
}

pub fn normalize(input: Text) -> Result<Text, self::text::normalize::NormalizeError>
    effects {} {
    if input.length == 0 {
        return Err(self::text::normalize::NormalizeError.Empty);
    }
    return Ok(input.trim());
}
```

### Current implementation slice and syntax decisions

The parser and checker implement the value-language subset described in [docs/grammar.md](grammar.md): i32, bool, Text, immutable non-generic nominal structs, declared non-generic unions, argument-inferred generic functions, Option<T>, Result<T, E>, local let declarations, returns, scoped if/else, comparisons, calls, checked arithmetic, field construction and reads, and exhaustive match expressions. Generic inference uses independently typed arguments; explicit type arguments, generic structs and unions, traits, and constructor-driven inference remain unsupported. `Text.length` returns an i32 Unicode scalar count, and `Text.trim()` removes leading and trailing Unicode whitespace. Struct construction uses named fields and requires each declared field exactly once; initializer expressions are evaluated in source order. Direct cycles through bare struct fields are rejected, while recursive paths through Option, Result, or a tagged union are allowed. Generated heap-allocated C# records are a prototype lowering, not a source-level allocation, identity, or layout promise. The bounded effects/capabilities implementation recognizes the closed vocabulary and checks declared upper bounds against direct and transitive effects; the only operation is opaque `FsRead.read_text`, which returns `Result<Text, FsError>` with effect `fs.read`. Library functions can receive this capability, and a CLI package can explicitly grant and inject it into a typed command handler. This is a bounded application-grant slice; effect inspection, receipts, other adapters, and web capability declarations remain unimplemented.

[`examples/text-validation`](../examples/text-validation) is a strict pure `lib` package precursor implementing `NormalizeError`, `normalize`, and the argument-inferred generic `require<T, E>` API. It includes pure module-level tests for empty/nonempty normalization, Unicode trimming, and `Option<Text>`/`Option<i32>` generic calls. Integration tests build it and run same-package consumers; the maintained CLI example consumes it through a sibling path dependency. Broader package source support is still required. In particular, the illustrative `TooLong(max: u32)` API remains beyond the current type slice.

The implemented package subset loads a strict `lang.toml` and recursively resolves `.lang` modules under each package's normalized `source_root`. Module paths must match their source-relative paths using `::` separators. Every user declaration reference names the package root, module path, and declaration, as in `self::module::Declaration` or `validation::text::validation::normalize`; `self` selects the current package and dependency aliases select only direct dependencies. The alias `self` is reserved. Private declarations are module-local, and a cross-module or cross-package reference to one reports `E_ACCESS_PRIVATE`. Unknown aliases, modules, or declarations report `E_NAME_UNRESOLVED`. There is no source-level import syntax. `lib` packages build as managed libraries; `cli` packages name an `entry_module` such as `app::main` and may use its supported zero-argument `main() -> i32|bool|Text` or one typed command declaration for build and run. Main-based CLI packages remain supported. A CLI manifest may grant `fs.read` with `[capabilities] fs.read = "allow"`; the typed command entry injects `FsRead` only into a handler whose checked signature accepts it. Capability declarations in library packages are rejected. The typed command layer checks ordinary handler effects and emits deterministic version-2 `command-schema.json`; each command's `capabilities` field lists capabilities required by its handler and injected by the trusted entry, not unused manifest grants. `lang test PACKAGE_DIRECTORY` checks and runs pure test blocks, typechecking tests throughout the dependency graph while executing only root-package tests. A dependency graph uses a portable `lang.lock` created by `lang lock PACKAGE_DIRECTORY`; `check`, `build`, `run`, and `test` reject missing, malformed, or stale locks, including when manifest capability grants change. Dependency-free packages need no lock. Path dependencies are local filesystem references and work offline; Git/registry sources, caches, `lang add`, package-install commands, and build receipts are not implemented. Generic functions are covered by same-package and path-dependency references. Public package graph semantics beyond local path resolution, generic structs and unions, traits, effect inspection and receipts, other capability adapters, and the full V1 package requirements remain incomplete. [`examples/library-package`](../examples/library-package) demonstrates a main-based CLI package with both a same-package module and a sibling library dependency. [`examples/scan-cli`](../examples/scan-cli) exercises the bounded `fs.read` grant and typed command path; neither is the complete V1 CLI acceptance.

Union variants use either named fields, for example TooLong(max: i32), or positional fields, for example Value(i32); a variant cannot mix them. Construct declared variants with `self::app::main::Choice.Yes` or `self::app::main::Choice.Value(3)`. Built-in Option and Result constructors are `Some(value)`, `None`, `Ok(value)`, and `Err(error)`, and their type comes from an expected annotation, return type, or parameter. Match arms are comma-separated and use `=>`. Patterns name qualified union variants, bind payloads positionally, or use the explicit wildcard `_`. Text literals support escaped quote, backslash, newline, carriage return, tab, and NUL characters. The exact productions and unsupported syntax are maintained in docs/grammar.md. Identifier names are contextual: module path and declaration-reference segments and unambiguous member positions accept any identifier token, including words used as grammar keywords elsewhere. Bare declaration, binding, and unqualified local names exclude only true, false, null, match, if, await, and with; these retain their literal, match, or unsupported-expression behavior. User declaration references are always qualified; short built-in names and local values remain allowed. Keyword-like words such as route are ordinary identifiers when the grammar expects a name, while route declarations are not implemented.

For the existing main-based command-line runner, `main` takes no arguments and returns `i32`, `bool`, or `Text`. `lang run` prints the returned value followed by a newline and exits 0; checked `i32` arithmetic overflow reports a generic runtime fault and exits 70. `lang build` creates an executable when that supported entrypoint exists and otherwise creates a library DLL. Typed command declarations use the separate parsing and result-to-exit-code contract described in §5.3.
### 4.2 Effects and capabilities

- Define a closed V1 effect vocabulary: `fs.read`, `fs.write`, `process.spawn`, `net.client`, `net.listen`, `db.read`, `db.write`, `env.read`, `clock.read`, `log.write`, and `secret.reveal`. Add a new effect only with a documented adapter and conformance tests.
- Every function has an inferred transitive effect set, including effects of called functions. Public functions must declare an upper bound; the compiler rejects a body or call chain whose inferred set is outside that bound. `effects {}` means no modeled I/O effects, not zero allocation or a proof of mathematical purity.
- A function can perform an effect only when it receives the appropriate capability value, directly or through a capability-bearing service. Referencing a package declaration must not grant ambient I/O. The trusted application root creates capabilities from a manifest and passes them into handlers/commands.
- Record the shortest useful call path explaining each public effect. Handle recursion with a call-graph fixed point. `lang inspect effects <symbol>` must show declared, inferred, and foreign-declared effects separately.
- Foreign adapters have explicitly declared effects and may require capability parameters, but the compiler cannot prove the internal .NET code honors its declaration. The build receipt must identify this limit.
- The runtime may issue capabilities only for operations declared in the application manifest. V1 capabilities constrain code written in this language and first-party adapters; they do **not** provide a security sandbox against malicious .NET code in the same process.

**Implementation status:** The compiler currently recognizes the closed effect vocabulary above, requires an annotation on every function, and treats each annotation as an upper bound on inferred direct and transitive effects. Recursive calls are handled through a call-graph fixed point, and `E_EFFECT_EXCEEDED` includes a shortest known call path. The only implemented effectful operation is `FsRead.read_text(path) -> Result<Text, FsError>`, accepting either a `Text` or `FilePath` path and contributing `fs.read`. `FsRead` is opaque: source cannot construct it; once received through a function parameter, code may pass, return, or store it in ordinary immutable values. A CLI package may grant `fs.read` in its manifest, and a typed command handler that accepts the capability receives it from the generated entry point. The trusted adapter currently calls `File.ReadAllBytes` on the supplied path, maps recognized filesystem failures to `NotFound`, `PermissionDenied`, `InvalidPath`, or `Io`, and uses strict UTF-8 decoding to map invalid text to `InvalidText`. `FsError` values can only be produced by this operation. `FsRead` is not a path-root restriction or security sandbox. This is a bounded grant/injection slice, not full M2: effect inspection, build receipts, other adapters, and grants for other effect classes remain deferred. Adapter behavior is trusted and is not proven by the effect checker.

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
| `lang test FILE_OR_PACKAGE` | Run pure language tests; report module/source locations and a nonzero exit on assertion failure. Bare `lang test` runs compiler fixtures. |
| `lang fmt --check` / `lang fmt` | Deterministic formatting; check mode does not modify files. |
| `lang add PATH|GIT_URL#COMMIT` | Add an exact local or pinned-Git dependency and update the lockfile. |
| `lang audit [--json]` | List package graph, hashes, effects, capabilities, adapters, and checked versus trusted claims. |
| `lang inspect symbols|calls|effects [--json]` | Query the semantic index for agents and developers. |

All commands use the same compiler and diagnostic codes. No editor integration or daemon is required. Successful `lang build` emits the executable plus the manifest and receipt described below.

#### Current command implementation

The current compiler accepts file-based `lang check FILE [--json]`, `lang build FILE`, `lang run FILE`, and `lang test FILE`, plus package-based `lang check PACKAGE_DIRECTORY [--json]`, `lang build PACKAGE_DIRECTORY`, `lang run PACKAGE_DIRECTORY`, and `lang test PACKAGE_DIRECTORY`. Bare `lang test` retains exact diagnostic-code checking for active compiler fixtures. The managed test runner checks tests as part of normal source/package typechecking, prints PASS/FAIL results with module and source locations, continues after assertion failures, and returns nonzero when any test fails. Runtime test results are not JSON; there is no property-testing framework or AOT test runner. The file and CLI package AOT form is `lang build FILE_OR_PACKAGE --aot --rid RID`; it accepts `win-x64` on Windows and `linux-x64` on Linux and requires either a supported zero-argument `main` or a typed command entry in a standalone source or CLI package. JSON output is supported on check and includes the schema version and ordered diagnostics. File-based build artifacts are copied into a unique `out/<source-name>-<id>/` directory beside the source file. Fixtures 14–15 cover effects and the missing CLI capability grant, fixtures 26–30 cover control flow, comparisons, and Text operations, and fixture 17 checks typed CLI declarations. Fixtures 16, 18, and 19 remain pending for resource and route behavior.
### 5.2 Package format and foreign boundary

`lang.toml` declares package name, version, entry kind (`lib`, `cli`, `web`), supported target, dependencies, and application capabilities. `lang.lock` records resolved dependency identity and content hash. Builds fail if pinned content differs from the lockfile. Workspace path dependencies are content-hashed for the receipt but remain editable during local development. Test a clean offline rebuild with a populated cache.

Normal packages contain language source and obtain effect summaries from compilation. A **foreign adapter package** explicitly declares a pinned .NET dependency, exposed language signatures, capability requirements, and trusted effect claims. It has wrapper tests and an audit entry for every exposed operation. Ordinary source cannot reference .NET types or load assemblies directly. V1 does not provide a remote package registry, semver solving, or public publishing.

A C ABI proof is planned as a separate, not-started gate; it does not add foreign syntax, package support, or imports to normal `lang` commands. The shared contract and proof scope are defined in [the foreign interop plan](foreign-interop.md). The initial proof is limited to by-value `i32`: import a source-built C identity function through typed foreign IR, then export a pure checked language `i32` transform through a NativeAOT shared library with status plus caller-owned out-value error reporting. Exact syntax and entrypoint details remain pending. This proof does not add production effect/capability/resource enforcement or replace the C# emitter/backend. Richer effect/capability checks require M2; richer resource-lifetime rules require MS1.

First-party adapters required for V1: filesystem, process, environment/config, clock, structured logging, HTTP client, HTTP server, JSON, HTML builder, and SQLite. TLS for production servers may terminate at a reverse proxy; the HTTP client adapter uses the platform's normal TLS stack. Do not claim either path is formally verified.

### 5.3 CLI contracts

A command declaration defines positional arguments, named options, flags, typed defaults, help text, a handler, and an error formatter. The implemented slice supports one command per standalone executable source module or CLI package entry module, at least one positional argument, the `FilePath`, `Text`, and `i32` argument/option types, and matching literal defaults; `FilePath` option defaults must be nonempty and contain no NUL. It generates the public nominal `<PascalCaseName>Args` type, validates the fully qualified handler signature `GeneratedArgs -> Result<Text, E>` or, when the CLI manifest grants `fs.read`, `GeneratedArgs, FsRead -> Result<Text, E>`, and requires the pure formatter signature `E -> Text`. Handlers retain normal declared/inferred effect checks. The `fs.read` grant is the only application capability grant implemented; a missing grant for an effectful handler is rejected. Version-2 `command-schema.json` beside managed and Native AOT build artifacts lists each handler's required capabilities that the trusted command entry will inject; an allowed but unused manifest grant is not listed. `lang run FILE_OR_PACKAGE -- APPLICATION_ARGS` forwards arguments to the generated parser. Main-based runs do not accept this separator. Within command arguments, a standalone `--` ends option parsing and permits subsequent positional values to begin with `-`. Top-level and command `--help` exit 0; unknown command/option, missing argument/value, duplicate option, invalid `i32`, and empty/NUL `FilePath` values report stable `CLI_*` codes and exit 2. Diagnostic subjects escape control characters to remain on one physical line. A successful `Ok(Text)` writes stdout and exits 0, a typed `Err(E)` is rendered by the formatter to stderr and exits 3, and unexpected runtime faults exit 70. Existing `main`-based CLI packages remain supported. Shell completions and man pages are later work.

Illustrative command surface:

```lang
module app::main;

pub union ScanError { Failed }

command scan {
    help "Scan files beneath a path.";
    argument input: FilePath help "Path to scan.";
    flag recursive help "Scan subdirectories.";
    handler: self::app::main::scan_files;
    error: self::app::main::describe_error;
}

pub fn scan_files(args: self::app::main::ScanArgs)
    -> Result<Text, self::app::main::ScanError>
    effects {} {
    return Ok("scanned");
}

pub fn describe_error(error: self::app::main::ScanError) -> Text effects {} {
    return match error { self::app::main::ScanError.Failed => "scan failed" };
}
```

The generated `ScanArgs` type and command signature must agree at compile time. This illustrative handler declares an empty effect bound; command handlers otherwise retain the ordinary effect annotation and inference rules, and only the formatter is required to be pure. The maintained [`examples/scan-cli`](../examples/scan-cli) demonstrates the implemented `fs.read` manifest grant, generated `FsRead` injection, exhaustive error mapping, and normalization through the validation dependency. Machine-readable effect inspection and audit remain future work; this example makes no sandbox claim.

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
| M1 — Pure library | Parser, modules, types, generics, minimal traits, `Option`/`Result`, matches, tests, typed IR, C# emission. | The library example compiles, is referenced from another package, passes tests, and rejects a missing union arm. |
| M2 — Semantic audit | Effect inference/declarations, capability checks, stable public IDs, `check --json`, `inspect`, first receipt. | A transitive I/O call fails a pure public contract with an exact call path; IDs survive formatting edits. |
| M3 — CLI and packages | Build/run/test/fmt/new/add, lockfile, scoped resources, first-party filesystem/process/config/log adapters. Add optional Native AOT publishing for supported executable applications with an explicit RID. | The example CLI runs, prints help, handles a bad path, reports effects, and builds offline from the cache; an AOT-compatible executable can be published for each supported RID on its matching host OS with the required native toolchain. |
| M4 — Web foundation | Async/await, HTTP server/client, JSON codecs, typed routes, response checking, OpenAPI, HTML builder. | The example returns a valid JSON response, rejects malformed input, catches an unexpected fault, escapes HTML text, and shuts down cleanly. |
| M5 — Persistence and release | SQLite adapter, transactions, web example, remaining audit fields, Windows/Linux CI and packaging. | All three examples and acceptance tests pass from a clean checkout on both OSes. |
| MS1 — Memory-semantics design (not started) | Specify source-level value, sharing, mutation, and scoped-resource behavior before these semantics are fixed. | Publish a decision matrix and positive/negative conformance programs. Scope resource ownership decisions to lexical handles. MS1 is a prerequisite for SH1. |
| PERF1 — Performance evaluation (not started) | Compare representative executable CLI workloads built with the managed Workstation GC configuration and optional Native AOT; include web/SQLite workloads when available and compatible. | Publish reproducible raw measurements and compatibility limits for startup, memory, throughput, latency, artifact size, and build cost; no numerical performance target is imposed. See [the measurement contract](memory-and-performance.md). |
| ABI1 — C ABI interop proof (not started) | Specify the shared .NET/C ABI contract and demonstrate a by-value `i32` C import and pure language export through NativeAOT. | Reject unsupported/mismatched scalar signatures; demonstrate status/out error mapping and host calls on Windows/Linux. Record bounded deployment/overhead evidence; ABI1 adds no normal language syntax or second backend. See [the foreign interop plan](foreign-interop.md). |
| SH1 — Self-hosted compiler (parallel follow-on after M3 and MS1) | Port the compiler in stages while preserving the C# bootstrap and C# output backend. | On Windows and Linux, stage 0 builds stage 1 and stage 1 rebuilds stage 2; stages 1 and 2 produce deterministic matching C# and equivalent behavior/diagnostics on a pinned conformance corpus. SH1 does not block M4/M5 or complete V1. |

An agent implementing a gate must submit: working code, added positive and negative fixtures, updated generated schema/receipt examples, a short list of claims the compiler actually checks, and unresolved limitations. A gate cannot be called complete on an illustrative parser or mocked backend.

### MS1: memory-semantics design (not started)

MS1 is a design gate, not a claim that a borrow checker or ownership model is implemented. It must precede SH1 and any new mutable-sharing or resource-lifetime semantics that would commit source behavior. Resolve the design questions and required conformance programs in [the memory contract](memory-and-performance.md); list unresolved decisions explicitly.

### PERF1: performance evaluation (not started)

PERF1 is a reproducible evaluation track, not a performance promise or V1 completion gate. Compare managed Workstation GC and Native AOT deployments of the same executable application. Native AOT is a publish-time mode for executable applications only and remains GC-managed; it requires a supported RID and native toolchain. The compiler tool and shared-library exports are excluded (the latter are covered separately by ABI1). Its workload and measurement rules are in [the measurement contract](memory-and-performance.md). Record compatibility limits and unsupported adapters; make no performance guarantee.

### SH1: staged compiler self-hosting (not started)

SH1 may start after M1's reusable-library path, M3's useful CLI, and MS1 are complete, with M2 effect and capability support available. PERF1 results should inform its deployment choices. It is a parallel follow-on after M3: it must not delay or replace M4/M5, and passing SH1 does not redefine V1 completion. Before the port, the language needs usable control flow, `Text` and collection APIs, generics, modules, diagnostics, and capability-controlled filesystem/process access.

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
| A05 | Function calls HTTP client without a network capability. | `E_CAPABILITY_MISSING`; a qualified package reference supplies no authority. |
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
