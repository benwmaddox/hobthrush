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
- [x] Add strict same-package manifests, explicit imports, and multi-file module resolution for the implemented pure-language slice.
- [ ] Add user-defined generics, traits, and the remaining core types.

The M0 all-fixtures acceptance remains incomplete: fixtures 14-19 are still pending. M1 is also incomplete; external dependencies and lockfiles, user-defined generics, traits, effects, and the maintained library/CLI/web project set remain future work.

The active compiler-fixture set covers fixtures 01-13 and 20-25. Fixtures 14-19 remain pending. Package modules currently resolve only within one package using an explicit import list; this does not implement external package dependencies or locking. Struct support and package resolution are bounded pure-language slices and do not complete the M1 library, generics, traits, effects, or package requirements.

## Executable deployment options (implemented)

- [x] Configure Workstation GC explicitly as the default for the compiler and generated applications.
- [x] Support optional Native AOT application publishing with `lang build FILE_OR_PACKAGE --aot --rid RID` for `win-x64` on Windows and `linux-x64` on Linux; normal build/run stays managed, Native AOT remains GC-managed, and library-only sources/packages and the compiler tool are excluded.
- [x] Report unsupported AOT targets, option combinations, and cross-OS RID requests with `E_BUILD_TARGET`.

Native AOT requires the matching .NET native toolchain and a build host whose OS matches the RID. It is a publish mode for generated executable applications; shared-library exports remain part of the separate ABI1 proof. PERF1 remains a measurement track; see [the memory and performance contract](memory-and-performance.md).

## Later gates

Effect inference and capability checks, scoped resources, typed CLI and web declarations, package locking, SQLite, and the maintained V1 library/CLI/web example projects remain future work. The V1 promise in the PRD is not complete.

## MS1 - memory-semantics design (not started)

MS1 is incomplete and must precede SH1 and any new mutable-sharing or resource-lifetime semantics that would lock source behavior. See [the memory contract](memory-and-performance.md). It does not claim that a general borrow checker or ownership model is implemented.

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
