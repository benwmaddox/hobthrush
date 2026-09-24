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
- [ ] Add package manifests, imports, and multi-file module resolution.
- [ ] Add user-defined generics, traits, and the remaining core types.

The M0 all-fixtures acceptance remains incomplete: fixtures 14-19 are still pending. M1 is also incomplete; package imports, user-defined generics, traits, and the maintained library/CLI/web project set remain future work.

The active compiler-fixture set covers fixtures 01-13 and 20-25. Fixtures 14-19 remain pending. A module header names one source file; it does not enable imports or package resolution. Struct support is a bounded pure-language slice and does not complete the M1 library, imports, generics, traits, effects, or package requirements.

## Later gates

Effect inference and capability checks, scoped resources, typed CLI and web declarations, package locking, SQLite, and the maintained V1 library/CLI/web example projects remain future work. The V1 promise in the PRD is not complete.

## SH1 - staged compiler self-hosting (not started)

SH1 is a follow-on after the M1 reusable-library path and M3 useful CLI, once M2 effect/capability support is available. It may proceed alongside M4 and M5; it must not delay or replace their V1 acceptance. Prerequisites include usable control flow, `Text` and collections, generics, modules, diagnostics, and capability-controlled filesystem/process access. SH1 does not complete V1.

- [ ] Build a useful formatter or source tool in lang.
- [ ] Port the lexer and parser, with differential checks against the C# bootstrap.
- [ ] Port the checker, typed IR, and C# emitter; retain C# output and the .NET SDK/runtime. The first-party runtime may stay in C#.
- [ ] Keep stage 0 as the known-good C# bootstrap: it builds stage 1 from lang-authored compiler sources, and stage 1 rebuilds those same sources as stage 2.
- [ ] Pin the bootstrap/compiler sources, toolchain, dependencies, and conformance corpus. On Windows and Linux, run independent positive and negative constraint gates against both stages; compare stage 1 and stage 2 generated C# deterministically and compare behavior/diagnostics.
- [ ] Use only narrow path/line-ending normalization where required, without masking semantic differences; keep the C# bootstrap available for recovery until its replacement is separately justified.

This is a reproducibility and conformance gate, not a claim of identical binaries or a trust proof. It requires neither a native backend nor a rewritten runtime.
