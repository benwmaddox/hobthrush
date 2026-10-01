# Product requirements: AI-first general-purpose language, V1

**Status:** Implementation-ready product brief
**Date:** September 24, 2026
**Project name:** Hobthrush; `hob` for the executable and `.hob` for source files.
**Primary target:** Reliable libraries, command-line tools, JSON web services, and small server-rendered web applications.
**Owner decision:** Build this as a new language and toolchain. Do not add features to Stasis or depend on Stasis internals.

**Pre-1.0 contract policy:** Language development is iterative. Source syntax, CLI behavior, manifests, lockfiles, metadata, and inspection schemas may change without backward-compatibility or migration guarantees. Schema numbers identify the current serialized format for tooling and tests; they do not promise support for historical formats. When a contract changes, update the compiler, tests, documentation, and examples together, and regenerate affected artifacts.

## 1. Product thesis

Hobthrush assumes AI systems will do most detailed software work. They read and modify source, so the language, compiler, standard library, and development tools are designed primarily for AI to understand and change. Humans set high-level goals and priorities and may inspect code when needed; routine human source review or per-change approval is not part of the intended development loop.

Build systems that help AI build correctly: enforceable constraints, precise machine-readable feedback, black-box outcome tests, and independent review by other AI systems. Compiler rules reject invalid types, missing error cases, undeclared effects, excess capability use, and incomplete API responses; goals and acceptance criteria keep changes on task. Callers should be able to rely on clear interfaces and test outcomes without reading implementation internals.

Published goals direct AI systems to identify, propose, and prioritize system improvements proactively. Resulting changes must pass project constraints, black-box tests, and independent AI review.

The intended project process puts AI systems in control of detailed intake, technical design, implementation, testing, independent review, acceptance, and maintenance within published goals, permissions, and mandatory gates. People set high-level goals and priorities and may inspect code exceptionally.

**V1 promise:** An AI system can use one toolchain to build a reusable library, a useful CLI, and a small web application. The compiler checks types, exhaustive matches, public function effects, capability access, and declared HTTP response variants. Builds emit machine-readable evidence of checks actually performed. The tools work end to end on Windows and Linux.

This is a productivity-oriented language with managed memory. It permits ordinary dynamic allocation and does not attempt general proof, bounded execution, or machine-code optimization in V1.

### Growth and stability

Before 1.0, source syntax and contracts can change with matching updates to the compiler, tests, documentation, and examples. After 1.0, public language syntax and source semantics should change rarely and only when AI reviewers find a clear need, strong fit with project goals, and an acceptable compatibility path. Compiler, runtime, and tooling internals may continue to be refined more often while preserving observable contracts. The standard library is the primary place for general capability growth when a feature fits there; AI systems review suggestions and coordinate API-compatible changes.

### Why someone would try it

- A library publishes typed APIs and an effect summary that a caller can inspect before using it.
- A CLI gets typed argument parsing, generated help, predictable errors and exit codes, and a single build/run/test workflow.
- A web service gets typed routes, request decoding, exhaustive response mappings, and generated OpenAPI for supported JSON types.
- An AI system can use structured diagnostics, stable symbol identifiers, and an effect/call graph instead of guessing from text alone.
- Foreign dependencies remain usable through adapters, with their trust boundary shown in the audit output.

## 2. Success criteria and limits

V1 is complete only when **three maintained example projects** build and run from a clean checkout:

1. **Library:** A reusable text/validation package consumed by the other examples. It contains public generic APIs, a tagged union, exhaustive matching, tests, and no I/O effects.
2. **CLI:** A file-processing command with typed arguments, generated `--help`, filesystem access through a capability, a useful nonzero exit on bad input, and a machine-readable effect audit.
3. **Web:** A small server-rendered page and JSON API backed by SQLite. It validates request input, escapes untrusted text in HTML, maps every declared normal response variant to one status, handles cancellation/shutdown, and exposes OpenAPI for the JSON route. A public, unauthenticated example is sufficient; do not invent an authentication system for this milestone.

All three must share the same language, module/package format, compiler, standard library conventions, build command, and diagnostic schema. A collection of unrelated demos is not a V1.

**Operational target:** An AI coding system with access to the pinned .NET SDK can use a clean checkout, run the documented bootstrap and `hob test`, and run the CLI and web examples on both supported OSes. Builds should succeed offline once declared dependencies have been restored into the cache. V1 uses the .NET managed runtime with Workstation GC as its default, explicitly selected for the compiler and generated applications. Ordinary `hob build` and `hob run` remain managed; `hob build FILE_OR_PACKAGE --aot --rid RID` optionally publishes a GC-managed Native AOT executable for `win-x64` when built on Windows or `linux-x64` when built on Linux, with the required native toolchain. This applies to executable file programs and CLI packages, not library-only programs/packages or the compiler tool. V1 makes no numerical speed or binary-size guarantee, and performance is not a completion gate; PERF1 requires reproducible measurements before performance claims are made. No formal-security claim is a V1 acceptance criterion.

## 3. Decisions fixed for V1

| Area | V1 decision | Reason |
| --- | --- | --- |
| Implementation | Keep the C# bootstrap compiler and build tool, emit C#, and pin a supported .NET LTS SDK/runtime for V1. SH1 may add a hob-authored compiler; the first-party runtime may remain in C#. | A reliable bootstrap and mature runtime facilities coexist with staged compiler self-hosting. |
| Backend | Type-check and lower into a typed internal IR, then emit C# and compile with the pinned SDK. One backend only; Native AOT is an optional SDK publish mode for generated executable applications. | Ship runnable applications before designing a native or Wasm backend. |
| Memory | Use the .NET GC-managed runtime with Workstation GC explicitly selected. Use scoped handles for files, transactions, and response bodies. Define portable sharing/resource semantics in MS1; emitted C# layout is not a source-level promise. | Application ergonomics with explicit resource cleanup and semantics independent of emitted representation. |
| Null/error | No implicit nullable values; `Option<T>` and `Result<T,E>`. No exceptions for expected failures in language APIs. | Make absent values and routine errors visible in types. |
| Generic code | Generic functions/types and minimal static traits with compile-time dispatch. | Real libraries without runtime reflection. |
| Side effects | Compiler-known effects plus explicit capability values; public APIs declare effects. | Audit the call graph and keep ambient I/O out of ordinary source code. |
| Foreign libraries | Catalog-reviewed .NET adapter declarations with closed Hobthrush signatures and explicit provenance. The first bridge exposes one SHA-256 text operation; audit claims remain `claim_only`. | Avoid a zero-library launch without misrepresenting guarantees. |
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
- Postfix `Result` propagation with `?` in ordinary functions returning the same error type, plus explicit mapping of errors. Lambda and module-level test bodies cannot propagate. Language API failures that a caller is expected to handle must use `Result`; adapter exceptions must be converted to documented error variants at the boundary.
- Scoped resources use a `with` construct. A scoped resource cannot be stored in a global or returned from its scope. Dispose on success, error, and cancellation. In V1 this is lexical lifetime checking, not a general Rust-style borrow checker. Transaction adapters roll back by default unless committed.

**Executable grammar contract:** The canonical grammar and its valid/invalid programs are the current executable source contract. Before 1.0, syntax may be refined iteratively when the PRD, fixtures, compiler consumers, documentation, and maintained examples are updated together. The semantic requirements above remain authoritative unless a scope change is reviewed.

Illustrative library surface:

```text
module text::validation;

pub struct Normalized<T> { value: T }
pub union NormalizeError { Empty, TooLong(max: u32) }
pub union Validation<T> { Valid(T), Invalid(self::text::validation::NormalizeError) }
pub trait Normalize {
    fn normalize(input: Self) -> Result<Self, self::text::validation::NormalizeError> effects {};
}
fn normalize_text(input: Text) -> Result<Text, self::text::validation::NormalizeError> effects {} {
    if input.length == 0 {
        return Err(self::text::validation::NormalizeError.Empty);
    }
    return Ok(input.trim());
}
pub impl self::text::validation::Normalize for Text {
    normalize = self::text::validation::normalize_text;
}
pub fn normalize<T: self::text::validation::Normalize>(input: T) -> self::text::validation::Validation<self::text::validation::Normalized<T>> effects {} {
    return match self::text::validation::Normalize.normalize(input) {
        Ok(value) => self::text::validation::Validation<self::text::validation::Normalized<T>>.Valid(
            self::text::validation::Normalized<T> { value: value }
        ),
        Err(error) => self::text::validation::Validation<self::text::validation::Normalized<T>>.Invalid(error),
    };
}
```

### Current implementation slice and syntax decisions

The immutable `Unit` primitive has one value, written `()`. It passes through ordinary parameters, returns, fields, and supported generic/container storage; structural equality returns true for `==` and false for `!=`. Both operands are evaluated exactly once from left to right, including awaited calls, so their effects and faults retain ordinary sequencing. Empty-parenthesis lookahead leaves grouped expressions unchanged. Unqualified `Unit` is built in, while qualified references can name a user nominal whose final component is also `Unit`. `Unit` is non-resource and appears in the existing API v11 and audit v9 primitive shape; receipt v3 is unchanged. It has no `Unit()` constructor, formatter, supported `main` result, typed-command input, or route, JSON, or SQLite codec.

The bounded immutable byte slice provides unqualified built-in `Bytes` and `BytesError` types with `Bytes.empty()`, `append(i32) -> Result<Bytes, BytesError>`, `length: i32`, and `get(i32) -> Option<i32>`. Appends accept exactly 0 through 255 and preserve prior snapshots; `BytesError.InvalidOctet` is compiler-produced and source-matchable. Values compare by exact octet sequence and compose with generic and nominal values. No byte literal, Text conversion, formatting, or binary CLI, route, SQLite, filesystem, HTTP, or process adapter is implemented.

The parser and checker implement the value-language subset described in [docs/grammar.md](grammar.md): `i32`, `i64`, `u32`, `u64`, `f64`, `bool`, `Text`, `Unit`, immutable nominal structs, including generic structs with explicit arguments, declared generic and non-generic tagged unions, argument-inferred generic functions with ordered static-trait bounds, `Option<T>`, `Result<T, E>`, local `let` and typed `var` declarations with same-type rebinding, returns, scoped `if`/`else`, ordered `for` loops over lists, comparisons, calls, checked arithmetic, field construction and reads, exhaustive `match` expressions, and immediately invoked one-parameter expression-bodied lambdas with explicitly typed parameters. Generic inference uses independently typed arguments; generic struct and union arguments and trait implementation targets are explicit; constructor-driven type inference remains unsupported. Lambda captures of immutable locals use value semantics; capturing `var` reports `E_CLOSURE_CAPTURE_MUTABLE`, and resource/capability parameters, captures, or results report `E_RESOURCE_ESCAPE`. Lambda values cannot be stored or returned; nested and async lambdas remain unsupported. `Text.length` returns an `i32` Unicode scalar count, and `Text.trim()` removes leading and trailing Unicode whitespace. The bounded collection slice provides contextual built-in `List<T>` with immutable homogeneous literals, `length`, `get(i32) -> Option<T>` (including `None` for negative or high indexes), and copy-on-write `append`. `Text.split(separator)` matches the exact delimiter, preserves empty fields, and returns a singleton containing the original text for an empty separator. An empty `[]` literal requires an expected list type; loop variables are immutable and scoped, loop-body calls contribute to inferred effects, and a loop does not guarantee function return. Qualified user types named `List` continue to resolve by their qualified reference. Resource handles cannot be stored in lists, map values, rebindable `var` locals, or the implemented closure subset. Contextual `Map<Text, V>` uses `Map.empty()` with an expected map type; `set(Text, V)` returns a new immutable snapshot, `get(Text)` returns `Option<V>`, `keys()` returns `List<Text>` in ordinal key order, and `length` returns `i32`. Setting an existing key replaces its value in the returned map. Map literals, removal, non-Text keys, index syntax or assignment, list mutation, `break`, and `continue` remain unsupported. MS1 is complete for the selected source contract; its implementation and conformance are recorded in the memory contract. Struct construction uses named fields and requires each declared field exactly once; initializer expressions are evaluated in source order. Direct cycles through bare struct fields are rejected, while recursive paths through `Option`, `Result`, or a tagged union are allowed. Generic structs preserve the same immutable value semantics and have no mutation or identity API. Finite nested types and regular guarded recursion are accepted; direct recursion and unbounded recursive type-argument growth are rejected. Resource and equality properties follow stored fields or union payloads, so unused parameters do not make a nominal type resource-bearing. Generated C# records are a lowering detail, not a source-level allocation, identity, or layout promise.

Generic tagged unions are supported as immutable nominal value types. A declaration such as `pub union Choice<T, E> { Value(T), Failed(E), Empty }` defines owner-scoped parameters; references and constructors provide every type argument explicitly, for example `self::app::main::Choice<List<i32>, Text>.Value([1], "ready")`. There is no constructor inference. Variant payloads substitute the instantiated arguments, while match patterns omit type arguments and use the matched scrutinee's instantiation. Generic union values participate in generic function inference, exhaustive typed-payload matching, immutable structural equality, and the inspect API v11 type-argument shape. Recursion checks operate on exact instantiated types, reject unbounded argument growth, and retain resource/equality properties only for type parameters stored in payloads. Generic route reply unions are rejected with the stable `E_ROUTE_HANDLER` diagnostic; nested route codecs and SQLite parameter/row codecs reject unsupported generic union shapes before emission. The union declarations and API metadata do not add mutation or identity operations.

Nominal newtypes are supported with a nongeneric declaration such as `pub newtype UserId = i32;`. `self::app::ids::UserId.wrap(42)` constructs the distinct wrapper explicitly and `user.value` projects its representation. Newtypes do not implicitly convert to or from their representation or another nominal type, inherit arithmetic or ordering, forward traits, or add mutation/identity operations. Structural equality follows the wrapped value when its representation is comparable, while the wrapper remains unequal in type to that representation. Representations may use closed generic structs and unions; generic newtype declarations are rejected with `E_UNSUPPORTED`. A newtype cannot store an actual capability/resource handle; a phantom generic argument that is not stored remains allowed. Direct struct/newtype value cycles are rejected, guarded recursion through `Option`, `Result`, or tagged unions is supported, and recursive generic growth is rejected. Newtype route and SQLite codecs are unsupported and fail during source checking. Inspect API schema v11 exposes public newtype declarations between structs and unions; audit schema v9 records all checked newtypes between functions and traits. The build receipt remains schema v3 and hashes the current audit snapshot.

Pure static traits are implemented for explicit, closed targets. A trait declares synchronous, nongeneric method signatures with `effects {}` and an implicit `Self` that occurs in at least one value parameter. An `impl Trait for Target` binds every method exactly once to a synchronous, nongeneric, pure function with the substituted signature. Function bounds use `T: TraitA + TraitB`; generic calls pass selected zero-state witnesses or forward bound witnesses, and qualified `Trait.method(value)` operations are statically selected. One visible implementation is allowed for each closed trait/target pair in the package graph; private impls remain package-local, and public impls require a public trait and public nominal target components. The orphan rule requires ownership of the trait or outer nominal target, so scalar and builtin wrapper heads require trait ownership. No trait objects, inheritance, associated types, generic impls, blanket impls, specialization, implicit methods, or runtime lookup are supported. Implementations and trait calls add no mutation or identity API. Inspect API v11 exposes public trait metadata and addressable witnesses; private and dependency-only witness details are omitted from that source-facing report. Audit schema v9 retains private traits and impls, concrete selections, targets, and binding function identities. The successful build receipt stays schema v3 and hashes the current audit snapshot. Generic route reply unions and unsupported generic route/SQLite codec shapes retain their existing early rejection diagnostics.

The bounded effect/capability implementation recognizes the closed vocabulary and checks declared upper bounds against direct and transitive effects. Current filesystem reads include synchronous `FsRead.read_text` and async `FsRead.read_text_async`; both are strict-UTF-8 `fs.read` operations returning `Result<Text, FsError>`. Other current operations include opaque `FsWrite.write_text` and `FsWrite.write_text_async` (`fs.write`), CLI-only `ProcessRunner.run_text_async` (`process.spawn`), `HttpClient.get_text_async` (`net.client`), and SQLite operations `DbRead.query_one` (`db.read`), `DbWrite.execute`, `DbWrite.begin`, `Transaction.execute`, and `Transaction.commit` (`db.write`). CLI packages may grant `fs.read`, `fs.write`, `net.client`, `env.read`, `secret.reveal`, `log.write`, and `process.spawn`; a network grant requires top-level `http_origin`. Command handlers receive requested granted capabilities after generated arguments in `FsRead`, `FsWrite`, `HttpClient`, `Config`, `Secrets`, `Logger`, then `ProcessRunner` order. Configured web packages require `net.listen` and may grant `fs.write`, configured `db.read`/`db.write`, `net.client`, `env.read`, `secret.reveal`, and `log.write`; the network grant also requires `http_origin`. Process pins and `process.spawn` are CLI-only. Route handlers receive requested capabilities after any body in `FsWrite`, `DbRead`, `DbWrite`, `HttpClient`, `Config`, `Secrets`, then `Logger` order. `FsRead` is not injected into web routes. Libraries may expose checked functions with the existing filesystem, database, and HTTP capability parameters, but cannot declare application grants or use/expose `Config`, `Secrets`, `Logger`, `Secret<Text>`, `ProcessRunner`, `ProcessOutput`, or `ProcessError`. `hob inspect effects PACKAGE_DIRECTORY SYMBOL --json` provides a narrow compiler-derived report of one root function's declared/inferred effects, shortest paths, required capabilities, manifest grants, and trusted-operation metadata. `hob inspect api PACKAGE_DIRECTORY --json` provides schema-v11 metadata for public declarations in the root and direct dependencies, plus checked root commands/routes, recursive types, direct calls, and root `http_origin`. The API report keeps root manifest grants separate from inferred effects and required capabilities; API schema v11 and audit schema v9 include configured process pins in `process_executables` after `http_origin`. Neither inspect report proves trusted adapter behavior. The schema-v9 package audit and schema-v3 successful-build receipt are implemented. The synchronous `FsWrite.write_text` and SQLite source APIs remain available. Async functions, host cancellation, `FsRead.read_text_async`, `FsWrite.write_text_async`, and the bounded HTTP client adapter are implemented. Async SQLite is explicitly deferred pending a concurrency and cancellation-cleanup design; other effect-class adapters remain later `#743` work, so M2 remains partial.

[`examples/text-validation`](../examples/text-validation) is a strict pure `lib` package that exposes immutable `Normalized<T>` and `Validation<T>` values, a pure static `Normalize` trait with a closed `Text` implementation, and generic `normalize<T: Normalize>` dispatch. Empty raw text returns `Invalid(NormalizeError.Empty)`; nonempty text is trimmed and wrapped as `Valid(Normalized<Text>)`. The generic `require<T, E>` helper remains, and module-level tests cover empty/nonempty input, Unicode trimming, and generic `Option` calls. Integration tests build it and run same-package, path-dependency, and pinned-Git consumers; the maintained library-package, CLI, and web examples consume its generic validation surface. The public `TooLong(max: u32)` case demonstrates typed transport through the library and exhaustive CLI/web consumer matches; current normalization does not enforce a maximum length. The bounded numeric slice adds explicitly suffixed `i64`, `u32`, and `u64` alongside eager unsuffixed `i32` literals, same-width checked arithmetic, and named checked-result, saturating, and wrapping add/subtract/multiply members for all four integer widths. `ArithmeticError.Overflow` is a closed compiler-produced case. Values support equality and ordering and ordinary generic/nominal storage. The slice also supports an explicit-suffix `f64` binary64 primitive with ASCII decimal tokens of at most 128 characters, finite-literal range checks, same-type IEEE `+`, `-`, `*`, `/`, ordered comparisons, and equality through nested immutable values. NaN is unequal to itself and positive and negative zero compare equal; integer `/` remains rejected at checking. Mixed numeric types, unary minus on unsigned values, conversions, and numeric route, command, or SQLite codecs remain rejected; no f64 wire codec or cross-platform bitwise arithmetic guarantee is claimed.

The implemented package subset loads a strict `hob.toml` and recursively resolves `.hob` modules under normalized `source_root`. User declarations use qualified package/module paths; aliases expose direct dependencies only and there is no source-level import. A `lib` package builds as a managed library; a `cli` package selects its supported main or typed command from `entry_module`. A `web` package selects a route module, requires `net.listen`, and uses the managed host. `hob new lib|cli|web NAME` creates a validated starter. `hob add SOURCE` updates the current package; `hob add PACKAGE_DIRECTORY SOURCE` chooses one explicitly. Dependency sources are local relative paths or Git references pinned to canonical HTTPS/file URLs and exact 40-character lowercase commits. Lock schema v3 records root/path/Git source identities and content hashes and binds catalog-resolved managed adapter provenance. The root identity uses path `.`, path dependencies use portable relative paths, and Git dependencies use the opaque logical ID `git:<64 lowercase hex SHA-256>` derived from canonical URL and exact commit; that ID is not a filesystem or cache path. `hob add` and `hob lock` are the only operations that use the external Git source provider. For Git pins they freshly fetch and verify the exact commit before changing a manifest or writing the lock; a mismatched existing cache is rejected without changing the cache or lock. Git is tooling, not part of Hobthrush syntax, semantics, or runtime. Normal `check`, `build`, `run`, `test`, `inspect effects`, `inspect api`, and `audit` commands validate the project lock and cached content attestation against the exact URL/commit offline and never invoke Git. A valid current lock is required for package graphs with dependencies and root packages with managed adapter declarations; dependency-free packages without adapters may omit a lock. The unkeyed attestation is a local integrity check, not proof of upstream provenance. The package cache defaults under user local application data; `HOB_PACKAGE_CACHE` can override it, but an explicit cache equal to or inside the package root or its `source_root` is rejected before source discovery. Git-sourced packages cannot declare path dependencies. Registries/public package installation, trait objects and excluded advanced trait forms, remaining adapter coverage, and the full V1 package requirements remain incomplete.

Union variants use either named fields, for example TooLong(max: i32), or positional fields, for example Value(i32); a variant cannot mix them. Construct declared variants with `self::app::main::Choice.Yes` or `self::app::main::Choice.Value(3)`. Built-in Option and Result constructors are `Some(value)`, `None`, `Ok(value)`, and `Err(error)`, and their type comes from an expected annotation, return type, or parameter. Match arms are comma-separated and use `=>`. Patterns name qualified union variants, bind payloads positionally, or use the explicit wildcard `_`. Text literals support escaped quote, backslash, newline, carriage return, tab, and NUL characters. The exact productions and unsupported syntax are maintained in docs/grammar.md. Identifier names are contextual: module path and declaration-reference segments and unambiguous member positions accept any identifier token, including words used as grammar keywords elsewhere. Bare declaration, binding, and unqualified local names exclude only true, false, null, match, if, and await; these retain their literal, match, or unsupported-expression behavior. `with` is an ordinary identifier when the grammar expects a name, though a statement beginning with `with` uses the scoped-transaction grammar. User declaration references are always qualified; short built-in names and local values remain allowed. The word `route` is ordinary in other name positions; a top-level `route` declaration dispatches to the contextual route grammar.

For the existing main-based command-line runner, `main` takes no arguments and returns `i32`, `bool`, or `Text`. `hob run` prints the returned value followed by a newline and exits 0; checked integer arithmetic overflow or underflow reports a generic runtime fault and exits 70. `hob build` creates an executable when that supported entrypoint exists and otherwise creates a library DLL. Typed command declarations use the separate parsing and result-to-exit-code contract described in §5.3.
### 4.2 Effects and capabilities

- Define a closed V1 effect vocabulary: `fs.read`, `fs.write`, `process.spawn`, `net.client`, `net.listen`, `db.read`, `db.write`, `env.read`, `clock.read`, `log.write`, and `secret.reveal`. Add a new effect only with a documented adapter and conformance tests.
- Every function has an inferred transitive effect set, including effects of called functions. Public functions must declare an upper bound; the compiler rejects a body or call chain whose inferred set is outside that bound. `effects {}` means no modeled I/O effects, not zero allocation or a proof of mathematical purity.
- A function can perform an effect only when it receives the appropriate capability value, directly or through a capability-bearing service. Referencing a package declaration must not grant ambient I/O. The trusted application root creates capabilities from a manifest and passes them into handlers/commands.
- Record the shortest useful call path explaining each public effect. Handle recursion with a call-graph fixed point. The current `hob inspect effects PACKAGE_DIRECTORY SYMBOL --json` command reports compiler-derived declared/inferred effects, shortest paths, capabilities, manifest grants, and explicitly trusted operation metadata. The current `hob inspect api PACKAGE_DIRECTORY --json` command reports schema-v11 public declarations, direct call identities, checked root command/route metadata, and root `http_origin` for a package graph. Both are narrow inspection commands; the API report has no trusted-operation claims, and neither proves host adapters safe. `hob audit PACKAGE_DIRECTORY --json` reports compiler-derived facts for the complete resolved package graph, including private checked functions, and records host/adapter boundaries as claims only.
- Foreign adapters have explicitly declared effects and may require capability parameters, but the compiler cannot prove the internal .NET code honors its declaration. The build receipt must identify this limit.
- The runtime may issue capabilities only for operations declared in the application manifest. V1 capabilities constrain code written in this language and first-party adapters; they do **not** provide a security sandbox against malicious .NET code in the same process.

**Implementation status:** The compiler recognizes the closed effect vocabulary, requires an annotation on every function, and checks each annotation as an upper bound on inferred direct and transitive effects. Recursive calls are handled through a call-graph fixed point, and `E_EFFECT_EXCEEDED` includes a shortest known call path. Implemented filesystem reads are synchronous `FsRead.read_text(path)` and async `FsRead.read_text_async(path)`, both strict-UTF-8 `fs.read` operations returning `Result<Text, FsError>`. Other implemented operations include `DbRead.query_one` (`db.read`), `DbWrite.execute`, `DbWrite.begin`, `Transaction.execute`, and `Transaction.commit` (`db.write`), plus `HttpClient.get_text_async` (`net.client`). The filesystem adapter accepts `Text` or `FilePath`, reads the supplied path with `File.ReadAllBytes`, maps recognized failures into the closed `FsError` union, and uses strict UTF-8 decoding. `FsRead` is opaque but does not restrict paths or create a security sandbox. SQLite operations use literal SQL and concrete parameter/row structs; `query_one` is a single `SELECT` without a semicolon with zero-or-one strict row decoding, and transaction writes are restricted to one literal DML statement without a semicolon. Web root manifests grant `net.listen` and configured `db.read`/`db.write`; route handlers receive matching database capabilities by signature. These compiler checks and trusted adapters do not prove adapter safety. The narrow `hob inspect effects` report is compiler-derived. `hob inspect api PACKAGE_DIRECTORY --json` now reports schema-v11 public package declarations, recursive types, direct call identities, and checked root command/route metadata, keeping manifest grants distinct from inferred effects and requirements. It contains no trusted-operation claims. The schema-v9 package audit and schema-v3 successful-build receipt are implemented and preserve recognized trust boundaries as claims only. Contextual async functions retain source result type `T` while generated code carries task and cancellation-token state. `await` is allowed only inside async functions and only on async calls or intrinsics; async calls must be awaited (`E_AWAIT_CONTEXT`, `E_AWAIT_SYNC`, `E_AWAIT_TARGET`, `E_ASYNC_CALL_UNAWAITED`). Async CLI handlers receive Ctrl+C cancellation and exit 130; async web handlers/helpers propagate ASP.NET `RequestAborted`, and cancellation ends without a generic HTTP 500. `FsRead.read_text_async` provides cancellable strict-UTF-8 `fs.read`, and `HttpClient.get_text_async` provides a cancellable bounded GET client. `FsWrite.write_text_async` adds cancellation-aware staged writes. SQLite async and other effect-class adapters remain deferred.


**First startup-configuration slice:** Root package manifests may declare `[config]` before `[capabilities]` and `[dependencies]`. Field names use lower_snake_case and values are `Text|required`, `Text|default:<literal>`, or `Secret<Text>|required`; an empty Text default is valid. The app grants are `env.read`, `secret.reveal`, and `log.write`. `HOB_CONFIG_<UPPER_SNAKE_KEY>` variables are snapshotted once before a CLI handler or web listener starts; a required variable must be present, and an empty present value is valid. `Config.get_text` and `Config.get_secret_text` accept only literal keys declared with the matching type. `Secrets.reveal_text` is the only reveal operation and contributes `secret.reveal`; the revealed value is ordinary `Text` without taint tracking. `Secret<Text>` is redacted by standard formatting and logger output. `Logger.info(event, detail)` contributes `log.write` and writes deterministic JSON to stderr, adding the web `request_id` when available. Missing required configuration reports field/environment names only, exits 78 for CLI, and prevents a web listener from starting. `Config`, `Secrets`, `Logger`, and `Secret<Text>` are available only to root-package entry handlers and routes; library packages cannot use or expose these types. API schema v11 and audit schema v9 include field names, source types, and required/default status only, with no runtime values or default literals; `process_executables` follows `http_origin` and records configured pins without full paths. Command schema is v4, inspect-effects is v1, audit is v9, and build-receipt is v3. Metadata and grants do not provide OS-level containment.

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
| `hob new lib|cli|web NAME` | Create a compilable minimal project and tests. |
| `hob check [--json]` | Parse, resolve, type-check, check matches/effects/capabilities/routes, return a stable nonzero failure code. |
| `hob build` / `hob run` | Build through the pinned backend, then optionally run. Do not skip checks. |
| `hob test FILE_OR_PACKAGE` | Run pure language tests; report module/source locations and a nonzero exit on assertion failure. Bare `hob test` runs compiler fixtures. |
| `hob fmt --check` / `hob fmt` | Deterministic formatting; check mode does not modify files. |
| `hob add PATH|GIT_URL#COMMIT` | Add an exact local or pinned-Git dependency and update the lockfile. |
| `hob audit PACKAGE_DIRECTORY --json` | List package graph, hashes, effects, capabilities, adapters, and checked versus trusted claims. |
| `hob inspect effects PACKAGE_DIRECTORY SYMBOL --json` | Print the narrow compiler-derived effect/capability report for one function. |
| `hob inspect api PACKAGE_DIRECTORY --json` | Print the schema-v11 compiler-derived package API and checked command/route report. |

All commands use the same compiler and diagnostic codes. No editor integration or daemon is required. Successful `hob build` emits the executable plus the manifest and receipt described below.

#### Current command implementation

The table above is the V1 target surface, not a list of commands already implemented. `hob new` and `hob add` are implemented; `hob fmt` is not. The package-only schema-v9 `hob audit` is implemented as a narrower surface than the V1 query target. The compiler accepts file/package check, build, and run; package lock/audit; effects/API inspection; and language tests. Audit covers the resolved dependency graph, including private checked functions, and records trusted integration points as claims, not proofs. File and CLI builds may add `--aot --rid RID`; typed-command runs forward application arguments after `--`; web host options use `hob run PACKAGE_DIRECTORY -- --urls URL`. Bare `hob test` retains exact diagnostic-code checking for active compiler fixtures. The managed test runner checks tests during normal typechecking, prints PASS/FAIL results with module/source locations, continues after assertion failures, and returns nonzero if any test fails. Runtime test results are not JSON; there is no property-testing framework or AOT test runner. AOT accepts `win-x64` on Windows and `linux-x64` on Linux and requires a supported main or typed command. Successful audit output uses schema v9; inspect API uses schema v11. Successful managed and Native AOT builds write schema-v3 `build-receipt.json`. File-based artifacts use a unique `out/<source-name>-<id>/` directory beside the source.
### 5.2 Package format and foreign boundary

`hob.toml` declares package name, version, entry kind (`lib`, `cli`, `web`), supported target, dependencies, and application capabilities. `hob.lock` schema v3 records root/path/Git source identities and content hashes and binds managed adapter declarations to catalog-resolved bridge, target, assembly, and closure provenance. The root path is `.`, path dependencies use portable relative paths, and Git dependencies use the opaque logical ID `git:<64 lowercase hex SHA-256>` derived from canonical URL and exact commit; that ID is not a filesystem or cache path. Path package contents remain editable during local development; Git pins identify canonical URL and exact commit. Git is only an external source provider invoked by explicit `hob add` and `hob lock` operations; ordinary `check`, `build`, `run`, `test`, `inspect effects`, `inspect api`, and `audit` commands use the lock and verified cache offline and never invoke Git. Cache validation and the explicit `HOB_PACKAGE_CACHE` root restriction are described in the current implementation status above.

Normal packages contain Hobthrush source and obtain effect summaries from compilation. Milestone `#748` implements one managed adapter package declaration for library packages: `pub adapter fn ... = "sha256.text.hash_utf8";` binds a bodyless, synchronous `Text -> Text` export to the reviewed `hob.sha256-text.v1` catalog. Its `[managed_adapter]` table pins `bridge_id`, target framework `net10.0`, a package-relative assembly path, and the assembly SHA-256; the catalog supplies the portability target `portable-anycpu-il`, assembly identity, operation map, and closure hash. Lock schema v3, audit schema v9, and receipt schema v3 carry the catalog-resolved provenance without physical/cache/workspace paths. The audit and receipt mark adapter evidence `claim_only`: hashes and declarations do not prove implementation behavior, and a managed adapter retains the full authority of the running process without an operating-system sandbox. Other bridge operations, typed errors, richer ownership/lifetime contracts, and wrapper requirements remain part of the broader adapter plan. Ordinary source cannot reference CLR/.NET types, C#, reflection, or arbitrary assemblies. Git is an external source provider used only by explicit add/lock commands, not language behavior. V1 has no remote registry, semver solving, or public publishing. See the [managed adapter contract](foreign-interop.md#managed-net-adapter-package-contract-planned).

A C ABI proof is planned as a separate, not-started gate; it does not add foreign syntax, package support, or imports to normal `hob` commands. The shared contract and proof scope are defined in [the foreign interop plan](foreign-interop.md). The initial proof is limited to by-value `i32`: import a source-built C identity function through typed foreign IR, then export a pure checked language `i32` transform through a NativeAOT shared library with status plus caller-owned out-value error reporting. Exact syntax and entrypoint details remain pending. This proof does not add production effect/capability/resource enforcement or replace the C# emitter/backend. Richer effect/capability checks require M2; richer resource-lifetime rules require MS1.

First-party adapters required for V1: filesystem, process, environment/config, clock, structured logging, HTTP client, HTTP server, JSON, HTML builder, and SQLite. TLS for production servers may terminate at a reverse proxy; the HTTP client adapter uses the platform's normal TLS stack. Do not claim either path is formally verified.

### 5.3 CLI contracts

A command declaration defines positional arguments, named options, flags, typed defaults, help text, a handler, and an error formatter. The implemented slice supports one command per standalone executable source module or CLI package entry module, at least one positional argument, the `FilePath`, `Text`, and `i32` argument/option types, and matching literal defaults; `FilePath` option defaults must be nonempty and contain no NUL. It generates the public nominal `<PascalCaseName>Args` type, validates the fully qualified synchronous or async handler signature `GeneratedArgs -> Result<Text, E>` or, when granted by the CLI manifest, generated arguments followed by optional `FsRead`, `FsWrite`, `HttpClient`, `Config`, `Secrets`, `Logger`, and `ProcessRunner` parameters in that order returning `Result<Text, E>`, and requires the pure formatter signature `E -> Text`. Handlers retain normal declared/inferred effect checks. The implemented CLI capability grants are `fs.read`, `fs.write`, `net.client`, `env.read`, `secret.reveal`, `log.write`, and `process.spawn`; the network grant requires root `http_origin`. Configured web packages separately require `net.listen` and may grant `fs.write`, `net.client`, and configured `db.read`/`db.write` for the bounded adapters; `net.client` also requires root `http_origin`. These web-root grants are checked and inject only requested route capabilities after any body in `FsWrite`, `DbRead`, `DbWrite`, `HttpClient`, `Config`, `Secrets`, `Logger` order. `FsRead` is not injected into web handlers. A missing grant for an effectful handler is rejected. Version-4 `command-schema.json` beside managed and Native AOT build artifacts lists each handler's required capabilities, including `net.client`, that the trusted command entry will inject; an allowed but unused manifest grant is not listed. `hob run FILE_OR_PACKAGE -- APPLICATION_ARGS` forwards arguments to the generated parser. Ctrl+C cancels an async command and exits 130. Main-based runs do not accept this separator. Within command arguments, a standalone `--` ends option parsing and permits subsequent positional values to begin with `-`. Top-level and command `--help` exit 0; unknown command/option, missing argument/value, duplicate option, invalid `i32`, and empty/NUL `FilePath` values report stable `CLI_*` codes and exit 2. Diagnostic subjects escape control characters to remain on one physical line. A successful `Ok(Text)` writes stdout and exits 0, a typed `Err(E)` is rendered by the formatter to stderr and exits 3, and unexpected runtime faults exit 70. Existing `main`-based CLI packages remain supported. Shell completions and man pages are later work.

Illustrative command surface:

```hob
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

The generated `ScanArgs` type and command signature must agree at compile time. This illustrative handler declares an empty effect bound; command handlers otherwise retain the ordinary effect annotation and inference rules, and only the formatter is required to be pure. The maintained [`examples/scan-cli`](../examples/scan-cli) demonstrates an async typed handler awaiting `FsRead.read_text_async`, the `fs.read` manifest grant, generated `FsRead` injection, exhaustive error mapping, and normalization through the validation dependency. Narrow compiler-derived effect and API inspection are available through `hob inspect effects` and `hob inspect api`; the schema-v9 package audit and schema-v3 successful-build receipt are implemented. These reports preserve host/adapter boundaries as claims only; this example makes no sandbox claim.

The current constrained process slice is available only to a root CLI package. Optional Windows and Linux path/SHA-256 pairs are top-level `hob.toml` keys before tables; any configured pair requires the root `process.spawn` grant and a current-host pair. Every declared pin is validated on every host as a normalized root-contained regular non-link file with a matching digest. The selected Linux pin additionally needs an execute permission bit. The selected executable is copied beside the generated CLI artifact as `<manifest assembly name>.process-runner` on Linux or `<manifest assembly name>.process-runner.exe` on Windows, preserving its execute bit. This deterministic suffix avoids colliding with the generated apphost when the application assembly is named `process-runner`; its identity is verified again immediately before spawn. `ProcessRunner`, `ProcessOutput`, and `ProcessError` are opaque root-CLI-package types; same-package helpers may accept and pass them, while standalone, library, and web source cannot use or expose them. Await `ProcessRunner.run_text_async(List<Text>, Text) -> Result<ProcessOutput, ProcessError>`; nonzero child exit codes remain `Ok(ProcessOutput)`. The runner uses an absolute copied executable, shell disabled, `ArgumentList`, empty environment, and artifact-directory working directory. It enforces 128 arguments, 16 KiB combined argument bytes, 1 MiB stdin, 1 MiB for each output stream, strict UTF-8, and a fixed 10-second timeout. Cancellation propagates after best-effort tree termination and a reap capped at two seconds. `StartFailed` includes a Windows launch incompatibility; there is no shell fallback. A pinned binary retains full OS authority; there is no CPU/memory sandbox or guarantee of killing detached descendants. Web, streaming, dynamic executable selection, binary I/O, per-call timeouts, and detached-work support are not implemented. No maintained example exercises this slice. See the [grammar](grammar.md#constrained-process-runner) and [implementation contract](implementation-contract.md#constrained-process-runner-contract-addendum) for exact limits and output/error fields.


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
    path id: i32;
    query name: Text;
    query page: Option<i32>;
    handler: get_user;
    response Found: 200 json User;
    response Missing: 404;
    response Failed: 500 json ErrorBody;
}
```

**Current implementation status:** The implemented route surface accepts `GET` and `POST` templates with whole-segment `{identifier}` placeholders. Path bindings support `Text` and `i32`; query bindings support `Text`, `i32`, `Option<Text>`, and `Option<i32>`. Every path placeholder must have one path declaration using the same spelling; path/query names cannot collide without regard to case. Same-method templates that overlap at equal literal-segment specificity are rejected with `E_ROUTE_DECL`, while more-specific templates and different methods may overlap. Handler order is the POST body when present, path bindings in template order, query bindings in declaration order, then requested capabilities in `FsWrite`, `DbRead`, `DbWrite`, `HttpClient`, `Config`, `Secrets`, and `Logger` order; `FsRead` is not supported on web routes. Missing required query values, duplicate query values, invalid path/query integer values, and integer overflow return 400 with the stable JSON body `{"error":"invalid_request"}`. An omitted optional query value becomes `None`; a supplied value becomes `Some(value)`. Generated OpenAPI includes path/query parameter schemas and required flags; `hob inspect api` schema v11 includes each checked binding's wire name, location, required flag, source type, and handler parameter index. A route using `HttpClient` requires root `net.client = "allow"` and `http_origin`. The bounded outbound adapter sends GET requests only to an origin-relative absolute-path target with an optional query, returns status and strict-UTF-8 body for every HTTP status, disables redirects, cookies, proxies, and credentials, enforces a 1 MiB raw response limit and a 10-second total timeout, and propagates host cancellation. Its origin restriction constrains request authority but is not an SSRF or network sandbox; DNS resolution and trusted runtime code remain boundaries. Normal response mappings are checked against a declared union and support zero-payload responses, supported scalar/struct JSON payloads, or safe `Html` payloads. Generic struct and union route/SQLite codecs are rejected with stable diagnostics; generic route reply unions fail early with `E_ROUTE_HANDLER` and nested response/SQLite shapes use `E_ROUTE_CODEC_UNSUPPORTED`/`E_DB_CODEC_UNSUPPORTED`. The maintained web sample exercises these bindings alongside `POST /api/note` with awaited `FsWrite.write_text_async`, and emits deterministic `openapi.json`; web packages run on the managed ASP.NET host and cannot use Native AOT. The host limits JSON request bodies to 1 MiB, returns 400 for malformed or incompatible JSON and 413 for oversized bodies, and uses 404/405 for unmatched paths/methods. Unexpected handler faults become generic JSON 500 responses with a request ID. Async route handlers/helpers propagate ASP.NET `RequestAborted` through awaited calls; cancellation ends the request without mapping to a generic JSON 500. Configurable server request timeouts and SQLite adapter async/cancellation support remain future work; this current slice does not satisfy the full V1 web contract above.

A safe HTML builder distinguishes escaped text from an HTML node. Rendering an arbitrary `Text` value escapes markup. No public raw-HTML insertion operation exists in ordinary V1 source. Static assets may be served from a declared directory; do not build a CSS/JS pipeline. No authentication, sessions, or CSRF framework is promised in V1, so examples must avoid authenticated or state-changing browser forms.

### 5.5 SQLite and configuration

- SQLite operations accept parameterized statements and typed parameters; do not expose a convenience API that concatenates values into SQL. Validate column names/types/nullability during row decoding at runtime and return a typed error. Static schema-aware SQL checking is later work.
- Provide `DbRead` and `DbWrite` capabilities and transactions whose `with` scope rolls back unless committed. Integration tests cover failed statements and rollback.
- Application configuration is declared centrally with field type, required/default status, and secret status. Validate at startup and emit an example configuration file without values for secrets. `Secret<T>` is redacted by the standard formatter/logger. Reveal requires a capability and is included in the effect audit. A revealed ordinary value can be mishandled by code; V1 does not promise end-to-end taint tracking.
- HTTP host supports shutdown cancellation, request IDs, and structured error logging. Provide a `/health` endpoint in the example as an ordinary declared route, not hidden framework behavior.

**Current implementation status:** CLI and configured web packages may grant `fs.write` using the exact `"allow"` value. Opaque `FsWrite.write_text(Text|FilePath, Text) -> Result<bool, FsError>` writes strict UTF-8 through a same-directory temporary file and overwrite move where supported; it uses host path resolution without package-root confinement or an OS sandbox. Configured web packages can also grant `db.read` and/or `db.write` with package-relative `sqlite_path` and `sqlite_schema`; declaring a schema requires `db.write` for initialization. SQLite-enabled builds conditionally reference pinned `Microsoft.Data.Sqlite` 10.0.12. `DbRead.query_one` accepts a SQL literal containing one `SELECT` without a semicolon, a concrete parameter struct, and an expected `Result<Option<Row>, DbError>` type; row decoding checks the exact columns, scalar types/nullability, and zero-or-one row cardinality. `DbWrite.execute` accepts literal parameterized SQL and a concrete parameter struct. `with db.begin() as tx { ... }` scopes a write transaction: `Transaction.execute` accepts one literal `INSERT`, `UPDATE`, `DELETE`, or `REPLACE` without a semicolon, and scope exit rolls back unless committed. The checker rejects transaction-handle escapes with `E_RESOURCE_ESCAPE` and unsupported transaction SQL with `E_DB_TRANSACTION_STATEMENT`. `FsRead.read_text_async` provides a strict-UTF-8 `fs.read` operation, and the bounded HTTP client provides awaited GET text requests through `net.client` and root `http_origin`. The synchronous filesystem and SQLite language APIs remain available. The maintained web integration exercises async note writes and overwrite. A direct generated-adapter probe cancels at the `Task.Yield` checkpoint immediately after temporary-file creation and checks `OperationCanceledException`, preservation of prior contents, and temporary-file cleanup; separate host tests cover `RequestAborted` propagation through awaited calls. `FsWrite.write_text_async` adds a staged, strict-UTF-8 write with cooperative cancellation, same-directory temporary-file cleanup, and a cancellation check before replacement; full-value encoding, durable flush, and overwrite move cannot be interrupted. Generated SQLite operations block on provider calls, so no async SQLite source surface exists yet. [Microsoft.Data.Sqlite's async methods execute synchronously](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async); a source-level async SQLite surface is deferred until a design can deliver meaningful concurrency and cancellation cleanup; other effect-class adapters remain future `#743` slices. Migrations, static schema-aware SQL checking, other database providers, dynamic configuration reload, secret rotation, arbitrary logging maps and sinks, clock/timestamp operations, and end-to-end secret taint tracking remain future requirements. These current bounded operations do not complete M2 or V1.


The implemented startup-configuration slice is root-package-only; library packages cannot use or expose `Config`, `Secrets`, `Logger`, or `Secret<Text>`. Root `[config]` fields use lower_snake_case names and descriptors `Text|required`, `Text|default:<literal>`, or `Secret<Text>|required`; an empty Text default is valid. The table precedes `[capabilities]` and `[dependencies]`. The app grants are `env.read`, `secret.reveal`, and `log.write`. `HOB_CONFIG_<UPPER_SNAKE_KEY>` values are snapshotted once before CLI handler dispatch or web listen; required means variable presence, so an empty present value is valid. `config.get_text("literal_key")` and `config.get_secret_text("literal_key")` require declared keys with matching descriptors. `secrets.reveal_text` is the only reveal operation and returns plain `Text` with no taint tracking. Secret wrappers are redacted by standard formatting and logger output. `logger.info(event: Text, detail: Text)` returns `bool`, contributes `log.write`, and emits deterministic JSON to stderr with `level`, `event`, and `detail`, plus `request_id` when available. Missing required config reports field and environment names only; CLI exits 78 and a web package fails before opening its listener. API schema v11 and audit schema v9 include sorted field name/type/required/default-status records only, without runtime values or default literals. Command schema v4, inspect-effects schema v1, and build-receipt schema v3 is implemented. Metadata and grants do not provide OS-level containment. Dynamic reload, secret rotation, arbitrary log maps or sinks, clock/timestamps, and end-to-end taint tracking remain outside this slice.

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

A backend must consume only a successfully checked program snapshot. Keep the typed IR and portable language semantics independent of C# syntax and object layout; checks must not depend on emitted source. Emit reproducible source and preserve diagnostics mapping back to original `.hob` spans. See [the memory and performance contract](memory-and-performance.md) for MS1 and PERF1.

**Diagnostic JSON:** Stable `code`, `severity`, `message`, file/range, symbol ID when available, and related locations/call path. Human messages may improve without changing the code. Examples: `E_MATCH_NONEXHAUSTIVE`, `E_EFFECT_EXCEEDED`, `E_CAPABILITY_MISSING`, `E_ROUTE_RESPONSE_MISSING`, `E_RESOURCE_ESCAPE`, `E_TYPE_MISMATCH`. Bad source must never produce a stack trace as the primary compiler diagnostic.

**Semantic IDs:** Public and module-level symbol IDs derive deterministically from locked package identity, module path, declaration kind, and declared name. A body edit or formatting change must not change the ID; a rename or package-identity change may. Local expression IDs are build-scoped and need not survive edits. No semantic-edit mutation API in V1.

**Read-only query interface:** `hob inspect --json` exposes symbols, signatures, callers/callees, declared/inferred effects, required capabilities, and foreign boundaries. Version the JSON schema. The compiler is the source of these facts; do not produce them by grepping generated C#. Current inspect commands provide narrower effect and package API reports. The schema-v9 package audit includes portable source identities and checked functions from the full local graph, including private functions, and records recognized trusted host/adapter integration points as claims; broader V1 query metadata remains pending.

**Build receipt:** Emit one `build-receipt.json` with toolchain version, target, source/package hashes, checks actually passed, effects/capabilities, foreign dependencies and their declared claims, and paths/hashes for the binary, command schema, and OpenAPI if present. It is an audit record, not a formal proof or a cryptographic certificate. Never set `boundsProven`, `contractProven`, `secure`, or `deterministic` based on tests or adapter declarations.

Current implementation: receipt schema v3 records build mode/framework/RID, the package graph, compiler and SDK versions, normalized input hashes, root grants, claim-only trusted components, foreign dependencies, a hash of the canonical audit snapshot, and relative-path hashes for copied artifacts. Its exact fields are specified in the [implementation contract](implementation-contract.md#audit-and-build-receipt-contract). The current receipt does not certify tests, adapter behavior, security, formal bounds, or binary reproducibility.

## 7. AI implementation plan and gates

Keep the repository buildable after every milestone. Each milestone adds a working path from source to an observable result, negative fixtures for its new rules, and JSON diagnostics. Do not create broad stubs marked “implemented.”

| Gate | Deliverable | Must demonstrate |
| --- | --- | --- |
| M0 — Specification fixtures | Grammar draft, 20 tiny valid/invalid examples, error-code registry, repository bootstrap and CI. | Examples include union matching, null rejection, effects, missing capabilities, scope escape, CLI, and route mapping. |
| M1 — Pure library | Parser, modules, types, generics, minimal traits, `Option`/`Result`, matches, tests, typed IR, C# emission. | The library example compiles, is referenced from another package, passes tests, and rejects a missing union arm. |
| M2 — Semantic audit | Effect inference/declarations, capability checks, stable public IDs, `check --json`, `inspect`, first receipt. | A transitive I/O call fails a pure public contract with an exact call path; IDs survive formatting edits. |
| M3 — CLI and packages | Build/run/test/fmt/new/add, lockfile, scoped resources, first-party filesystem/process/config/log adapters. Add optional Native AOT publishing for supported executable applications with an explicit RID. | The example CLI runs, prints help, handles a bad path, reports effects, and builds offline from the cache; an AOT-compatible executable can be published for each supported RID on its matching host OS with the required native toolchain. |
| M4 — Web foundation | Async/await, HTTP server/client, JSON codecs, typed routes, response checking, OpenAPI, HTML builder. | The example returns a valid JSON response, rejects malformed input, catches an unexpected fault, escapes HTML text, and shuts down cleanly. |
| M5 — Persistence and release | SQLite adapter, transactions, web example, remaining audit fields, Windows/Linux CI and packaging. | All three examples and acceptance tests pass from a clean checkout on both OSes. |
| MS1 — Memory-semantics design (complete) | Fix source-level value, sharing, mutation, equality, closure-capture, and lexical resource behavior independently of emitted representation. | Decision matrix and positive/negative conformance for implemented syntax, including immediate lambda capture, are in the [memory contract](memory-and-performance.md). |
| PERF1 — Performance evaluation (not started) | Compare representative executable CLI workloads built with the managed Workstation GC configuration and optional Native AOT; include web/SQLite workloads when available and compatible. | Publish reproducible raw measurements and compatibility limits for startup, memory, throughput, latency, artifact size, and build cost; no numerical performance target is imposed. See [the measurement contract](memory-and-performance.md). |
| ABI1 — C ABI interop proof (not started) | Specify the shared .NET/C ABI contract and demonstrate a by-value `i32` C import and pure language export through NativeAOT. | Reject unsupported/mismatched scalar signatures; demonstrate status/out error mapping and host calls on Windows/Linux. Record bounded deployment/overhead evidence; ABI1 adds no normal language syntax or second backend. See [the foreign interop plan](foreign-interop.md). |
| SH1 — Self-hosted compiler (parallel follow-on after M3 and MS1) | Port the compiler in stages while preserving the C# bootstrap and C# output backend. | On Windows and Linux, stage 0 builds stage 1 and stage 1 rebuilds stage 2; stages 1 and 2 produce deterministic matching C# and equivalent behavior/diagnostics on a pinned conformance corpus. SH1 does not block M4/M5 or complete V1. |

An AI system implementing a gate must submit working code, positive and negative fixtures, updated generated schema/receipt examples, a short list of claims the compiler actually checks, and unresolved limitations. A gate cannot be called complete on an illustrative parser or mocked backend.

### MS1: memory-semantics design (complete)

MS1 fixes source value sharing, structural equality, immutability, the bounded immediate-lambda capture form, and lexical transaction ownership before SH1. It does not implement a general borrow checker or ownership model. The decision matrix and positive/negative conformance are recorded in [the memory contract](memory-and-performance.md). No core MS1 decision remains open.

### PERF1: performance evaluation (not started)

PERF1 is a reproducible evaluation track, not a performance promise or V1 completion gate. Compare managed Workstation GC and Native AOT deployments of the same executable application. Native AOT is a publish-time mode for executable applications only and remains GC-managed; it requires a supported RID and native toolchain. The compiler tool and shared-library exports are excluded (the latter are covered separately by ABI1). Its workload and measurement rules are in [the measurement contract](memory-and-performance.md). Record compatibility limits and unsupported adapters; make no performance guarantee.

### SH1: staged compiler self-hosting (not started)

SH1 may start after M1's reusable-library path, M3's useful CLI, and MS1 are complete, with M2 effect and capability support available. PERF1 results should inform its deployment choices. It is a parallel follow-on after M3: it must not delay or replace M4/M5, and passing SH1 does not redefine V1 completion. Before the port, the language needs usable control flow, `Text` and collection APIs, generics, modules, diagnostics, and capability-controlled filesystem/process access.

Port in three steps: build a useful formatter or source tool in hob; port the lexer and parser with differential checks against the C# bootstrap; then port the checker, typed IR, and C# emitter. Stage 0 is the maintained C# bootstrap that builds compiler stage 1 from hob-authored compiler sources; stage 1 rebuilds those same sources as stage 2. The compiler continues to emit C# and use the pinned .NET SDK/runtime, and the first-party runtime may remain C#.

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
| A11 | `hob audit PACKAGE_DIRECTORY --json` on a program using SQLite. | Separates compiler-checked language code from trusted adapter declarations. |
| A12 | Format or change a function body, then inspect its public symbol ID. | ID unchanged; diagnostics and receipt still refer to current source. |
| A13 | Modify a pinned dependency without updating its lock entry. | Build fails on mismatch; clean cached dependency rebuild works offline. |
| A14 | CLI run with `--help`, invalid flag, expected domain error, and runtime fault. | Generated help; distinct predictable exit behavior and no secret value in error output. |
| A15 | Cancel an active HTTP request and stop the server. | First-party I/O observes cancellation; server exits without a stranded in-language task. |
| A16 | Remove an application's declared `fs.read` capability while its command needs `FsRead`. | Check or startup rejects the application; no capability is silently synthesized. |

A passing test says only what its assertion covers. The current receipt records an audit-snapshot hash but does not record which runtime/integration tests ran or certify their results; the fuller test/check accounting remains a V1 target.

The separate experimental agent outcome grader measures the published task cards and retains machine-readable results and raw command evidence; it is not a language-conformance gate. See [the agent outcome evaluation guide](agent-evals.md).

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
