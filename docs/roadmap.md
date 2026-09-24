# Roadmap and current claims

The source PRD lives in [PRD.md](PRD.md). The executable is `al`, and source files use `.al`.

## M0 — repository and toolchain

- [x] Pin .NET 10 and create a buildable C# repository.
- [x] Record the grammar decisions and canonical source fixtures.
- [x] Configure Windows and Linux CI for Release builds, active compiler fixtures, the integration harness, and runnable examples.

## M1 — pure typed values

- [x] Parse, check, build, and run pure functions with checked `i32` arithmetic.
- [x] Check `bool`, `Text`, declared tagged unions, `Option<T>`, and `Result<T, E>`.
- [x] Type-check union construction and payloads, reject `null`, and check exhaustive matches.
- [x] Lower the supported source into typed semantic IR and emit deterministic C#.
- [x] Build a library DLL when no supported executable entrypoint is present.
- [ ] Add package manifests, imports, and multi-file module resolution.
- [ ] Add user-defined generics, traits, structs, and the remaining core types.

The M0 all-fixtures acceptance remains incomplete: fixtures 14–19 are still pending. M1 is also incomplete; package imports, user-defined generics, traits, and the maintained library/CLI/web project set remain future work.

The active compiler-fixture set covers fixtures 01–13 and 20–21. A module header names one source file; it does not enable imports or package resolution.

## Later gates

Effect inference and capability checks, scoped resources, typed CLI and web declarations, package locking, SQLite, and the maintained V1 library/CLI/web example projects remain future work. The V1 promise in the PRD is not complete.
