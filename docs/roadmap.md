# Roadmap and current claims

The source PRD lives in [PRD.md](PRD.md). The executable is `lang`, and source files use `.lang`.

## M0 - repository and toolchain

- [x] Pin .NET 10 and create a buildable C# repository.
- [x] Record the grammar decisions and canonical source fixtures.
- [x] Configure Windows and Linux CI for Release builds, active compiler fixtures, the integration harness, and runnable examples.

## M1 - pure typed values

- [x] Parse, check, build, and run pure functions with checked `i32` arithmetic.
- [x] Check `bool`, `Text`, declared tagged unions, `Option<T>`, and `Result<T, E>`.
- [x] Type-check union construction and payloads, reject `null`, and check exhaustive matches.
- [x] Lower the supported source into typed semantic IR and emit deterministic C#.
- [x] Build a library DLL when no supported executable entrypoint is present.
- [x] Add immutable non-generic nominal structs with named construction, field checks and reads, and direct-recursion checks.
- [x] Add contextual immutable `List<T>`, homogeneous list literals, `length`, `get`, copy-on-write `append`, exact-delimiter `Text.split`, ordered `for` iteration, narrowly scoped `var` rebinding, and resource-handle exclusion from lists and `var` locals.
- [x] Add strict same-package manifests, fully qualified declaration references, and multi-file module resolution for the implemented pure-language slice.
- [x] Resolve local path dependencies on library packages, qualify direct dependency references by alias, and preserve package-specific module identity.
- [x] Create portable deterministic package locks and reject missing, malformed, or stale locks before package checks and builds.
- [x] Add scoped `if`/`else`, comparison operators with defined precedence, branch-local scopes, guaranteed-return checking, and `E_UNREACHABLE`.
- [x] Add `Text.length` using Unicode scalar counts and `Text.trim()` using Unicode whitespace trimming.
- [x] Add generic functions with type inference from independently typed arguments.
- [x] Add pure module-level language tests and `lang test PACKAGE_DIRECTORY`; dependency tests are typechecked while only root-package tests execute.
- [x] Add the PR1 typed CLI command contract for one command in an executable entry module, including generated arguments, parsing/help, typed result mapping, and deterministic command schema output.
- [ ] Add generic structs and unions, traits, and the remaining core types.

Current fixture status: 47 active, 0 pending. Fixture 16 covers transaction resource escape. Fixtures 43–47 cover the bounded collections and iteration slice. M1 remains incomplete: Git and registry dependencies, generic structs and unions, explicit type arguments, traits, `Map<K, V>`, and the full PRD library/CLI/web requirements remain future work. Broader test features, including property-based testing, JSON test results, executing dependency package tests, and an AOT test runner, remain future work.

The active compiler-fixture set covers fixtures 01-47. Package modules resolve within the root and its recursively declared local library paths using qualified declaration references; source-level imports are not part of the syntax. Git and registry sources, dependency caches, and package installation are not implemented. The schema-v1 package audit and successful-build receipts cover the current local graph and compiler report; they do not complete the full M1 library, generic structs or unions, traits, the complete capability model, or V1 package requirements.

[`examples/text-validation`](../examples/text-validation) is a pure `lib` package precursor for the PRD validation library. It checks and builds `NormalizeError`, `normalize`, and `require<T, E>`, and runs pure language tests for empty/nonempty input, trimming, and both `Option` generic instantiations. Integration consumers exercise same-package calls, dependency test filtering, and lock enforcement. The maintained [`examples/scan-cli`](../examples/scan-cli) package consumes this library through a local path dependency and exercises the `fs.read` grant, typed command injection, and error mapping. Compiler-derived effect inspection, package API inspection, the schema-v1 package audit, and successful-build receipts are implemented. Explicit trusted-host/adapter entries remain claims about recognized integration points, not proofs; adapters are not sandboxes.

## M2 - effects and capabilities (partial)

- [x] Define a closed effect vocabulary and require every function to declare an upper bound.
- [x] Infer direct and transitive effects across qualified calls and recursive call cycles; report a shortest call path when a bound is exceeded.
- [x] Add opaque `FsRead.read_text(Text) -> Result<Text, FsError>` with the `fs.read` effect and exhaustive typed filesystem errors.
- [x] Allow effectful library functions to be checked and built as managed libraries without constructing capabilities in source.
- [x] Grant `fs.read` from CLI package manifests, inject `FsRead` into typed command handlers, include handler-required injected capabilities in schema version 2 and grant edits in lock freshness, and reject an ungranted handler.
- [x] Add bounded SQLite operations for `db.read` and `db.write`, with parameter/row codecs, root web-package grants and route capability injection, and scoped transactions with rollback by default.
- [x] Add the narrow compiler-derived `lang inspect effects PACKAGE_DIRECTORY SYMBOL --json` report, including declared/inferred effects, shortest paths, required capabilities, manifest grants, and trusted operation metadata.
- [x] Add `lang inspect api PACKAGE_DIRECTORY --json` schema v1, with public root/direct declarations, recursive structured types, duplicate-alias source IDs, direct call edges, checked command/route surfaces, grants, and deterministic path-independent output.
- [x] Add `lang audit PACKAGE_DIRECTORY --json` schema v1 for the complete resolved local package graph, including private checked functions, normalized input hashes, distinct grants/effect requirements, claim-only trusted components, and generated foreign dependencies.
- [x] Emit schema-v1 `build-receipt.json` after successful managed and Native AOT builds, with toolchain/input/audit-snapshot hashes and a relative-path hash inventory of copied artifacts.
- [ ] Add async/cancellation-aware adapters and adapters for the remaining effect classes.

This is a partial M2 foundation, not M2 completion. `FsRead` and the SQLite adapter are trusted boundaries. `FsRead` reads the supplied path with `File.ReadAllBytes` and strict UTF-8 decoding; it is not a path-root restriction or security sandbox. SQLite supports constrained literal SQL and typed parameters/rows for configured web packages and libraries; web root grants and opaque `DbRead`/`DbWrite` values are compiler-checked, but they do not sandbox trusted .NET code. Both adapter families remain synchronous in source. `lang audit` reports compiler-checked functions and explicit trusted integration claims; `build-receipt.json` hashes that audit snapshot and the output artifacts. Neither proves adapter internals safe or claims that binaries are reproducible. Async handlers, source-level cancellation, and other effect adapters remain future work.

## Executable deployment options (implemented)

- [x] Configure Workstation GC explicitly as the default for the compiler and generated applications.
- [x] Support optional Native AOT application publishing with `lang build FILE_OR_PACKAGE --aot --rid RID` for `win-x64` on Windows and `linux-x64` on Linux; normal build/run stays managed, Native AOT remains GC-managed, and library-only sources/packages and the compiler tool are excluded.
- [x] Report unsupported AOT targets, option combinations, and cross-OS RID requests with `E_BUILD_TARGET`.

Native AOT requires the matching .NET native toolchain and a build host whose OS matches the RID. It is a publish mode for generated executable applications; shared-library exports remain part of the separate ABI1 proof. PERF1 remains a measurement track; see [the memory and performance contract](memory-and-performance.md).

## Later gates

The compiler supports managed `kind = "web"` packages with static typed `GET`/`POST` routes, a checked `net.listen` grant, deterministic OpenAPI output, and safe Html builders. Routes do not yet bind path or query parameters. The maintained [`examples/web`](../examples/web) package runs on the ASP.NET host, consumes the shared validation library, and demonstrates configured SQLite with parameterized operations, strict row decoding, root grants, transaction-scoped writes, and durable state across host restarts. `DbRead.query_one` reads one literal `SELECT` with zero-or-one row cardinality; `DbWrite.execute` performs a parameterized write; `with db.begin() as tx` scopes write transactions and rolls back by default. Transaction execution is limited to one literal DML statement (`INSERT`, `UPDATE`, `DELETE`, or `REPLACE`) with no semicolon. Fixture 16 and integration coverage exercise `E_RESOURCE_ESCAPE`; the rule applies only to lexical `Transaction` handles and does not implement general ownership or borrowing. The maintained web tests cover commit, implicit rollback, early return, failed-statement rollback, and rejection of a later commit. Adapter operations still block while waiting on the provider API. Cancellation-specific cleanup remains unverified because source handlers cannot express async cancellation. Other scoped resources, broader application grants, Git/registry dependencies, dependency installation, async handlers, and cancellation remain future work. `lang audit` and successful-build receipts describe compiler facts and recognized trust boundaries but do not prove trusted runtime code safe, establish a sandbox, or complete M2 or the V1 promise; grants are not a sandbox. The SQLite adapter does not complete M2 or the V1 promise.

## MS1 - memory-semantics design (incomplete)

MS1 remains incomplete and must precede SH1 and broader mutable-sharing or resource-lifetime semantics that would lock source behavior. The collections slice records a narrow local binding/rebinding decision in [the memory contract](memory-and-performance.md), but it does not establish a general borrow checker or ownership model.

- [ ] Decide assignment and parameter passing, copy/sharing, identity/equality, and mutation, including collections and closures.
- [ ] Decide single-owner/borrow/escape rules for lexical scoped handles only, cleanup on success/error/cancellation, and the interop trust boundary; do not extend ordinary GC-managed objects into a general ownership system.
- [ ] Mark unresolved choices explicitly and write positive/negative language conformance programs before adding mutable-sharing or scoped resource semantics.
- [ ] Keep source value, sharing, identity, equality, mutation, and resource semantics explicit and independent of emitted C# layout; do not promise a physical representation at the language level.

## PERF1 - performance evaluation (not started)

PERF1 is a measurement track, not a guarantee or V1 completion gate. See [the measurement contract](memory-and-performance.md).

- [ ] Compare representative executable CLI workloads built with the ordinary managed Workstation GC configuration and optional Native AOT on the matching host OS; include web/SQLite applications when they are available and AOT-compatible.
- [ ] Measure startup, bytes allocated per operation and collection counts, steady/peak memory, throughput, p95/p99 latency, published size, and build cost.
- [ ] Pin hardware, OS, .NET SDK, RID, native toolchain, workload and input versions; record warmup, repetitions, commands, and raw results.
- [ ] Record compatibility limits and unsupported adapters. Native AOT remains GC-managed, requires a supported RID and native toolchain, and does not cover library-only programs or the compiler tool. Make no numerical performance guarantee.

## ABI1 - C ABI interop proof (not started)

Specify one foreign-boundary contract shared by .NET adapters and C ABI calls. ABI1 is an isolated proof, not production syntax, package support, effect/capability enforcement, or a second backend. Its proposed Native AOT shared-library export is separate from the executable-only `lang build --aot` deployment mode. See [the foreign interop plan](foreign-interop.md).

- [ ] Demonstrate a contract containing signature identity, pinned library hash, library/symbol identity, target OS/architecture ABI and calling convention, ownership/lifetime, error mapping, declared effects, required capabilities, and separately marked trusted claims. Define buffer length/encoding and callback lifetimes as future contract areas, outside the scalar proof.
- [ ] Build a tiny C library from source with an `int32_t` identity function; demonstrate a checked language call through typed foreign IR and reject unsupported or mismatched scalar signatures.
- [ ] Export a pure checked language `i32` transform from a NativeAOT shared library with status plus a caller-owned synchronous `int32_t` out-parameter. Demonstrate that runtime overflow becomes an error status and no exception crosses the ABI. The out-parameter is valid only for the call and is never retained; no user-defined pointer ownership is supported.
- [ ] Call the exported language function from C and Python hosts. After control flow is supported, separately demonstrate a meaningful pure language validation library callable from those hosts; the scalar transform alone is not that demonstration.
- [ ] Pin SDK/toolchain, OS/architecture ABI, inputs and host versions; record raw call timings, artifacts/sizes, build cost, and deployment limits on Windows and Linux. This is bounded evidence, not completion of PERF1 or a backend switch.
- [ ] Keep normal `lang` commands closed to foreign imports. Gate richer effect/capability behavior on M2 and resource-lifetime semantics on MS1.

## SH1 - staged compiler self-hosting (not started)

SH1 is a follow-on after the M1 reusable-library path, M3 useful CLI, and MS1, once M2 effect/capability support is available. PERF1 results should inform its deployment choices. It may proceed alongside M4 and M5; it must not delay or replace their V1 acceptance. Prerequisites include usable control flow, `Text` and collections, generics, modules, diagnostics, and capability-controlled filesystem/process access. SH1 does not complete V1.

- [ ] Build a useful formatter or source tool in lang.
- [ ] Port the lexer and parser, with differential checks against the C# bootstrap.
- [ ] Port the checker, typed IR, and C# emitter; retain C# output and the .NET SDK/runtime. The first-party runtime may stay in C#.
- [ ] Keep stage 0 as the known-good C# bootstrap: it builds stage 1 from lang-authored compiler sources, and stage 1 rebuilds those same sources as stage 2.
- [ ] Pin the bootstrap/compiler sources, toolchain, dependencies, and conformance corpus. On Windows and Linux, run independent positive and negative constraint gates against both stages; compare stage 1 and stage 2 generated C# deterministically and compare behavior/diagnostics.
- [ ] Use only narrow path/line-ending normalization where required, without masking semantic differences; keep the C# bootstrap available for recovery until its replacement is separately justified.

This is a reproducibility and conformance gate, not a claim of identical binaries or a trust proof. It requires neither a native backend nor a rewritten runtime.
