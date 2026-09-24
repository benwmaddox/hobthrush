# lang

`lang` is a temporary working name for an agent-first general-purpose language under construction; the permanent name is pending. The executable is `lang` and source files use `.lang`. This repository is independent of Stasis.

The implemented pure-language slice supports `i32`, `bool`, `Text`, immutable non-generic nominal structs, declared tagged unions, `Option<T>`, `Result<T, E>`, payload and field checks, and exhaustive `match`. Effects must be declared as `effects {}`. The current parser and constructor decisions are described in [docs/grammar.md](docs/grammar.md).

## Bootstrap

Install the .NET 10.0.401 SDK, then from this directory:

```sh
dotnet build lang.slnx --configuration Release
dotnet run --project src/Lang --configuration Release -- check examples/pure/src/main.lang --json
dotnet run --project src/Lang --configuration Release -- run examples/pure/src/main.lang
dotnet run --project src/Lang --configuration Release -- run examples/types/src/main.lang
dotnet run --project src/Lang --configuration Release -- run examples/structs/src/main.lang
dotnet run --project src/Lang --configuration Release -- test
dotnet run --project tests/Lang.IntegrationTests --configuration Release
```

`lang check FILE` checks the source and returns 1 for invalid programs. `lang check FILE --json` prints stable diagnostic codes, source ranges, and severity, including an empty diagnostics array on success. `lang test` checks the active fixtures against their exact expected diagnostic-code lists.

Foreign interop is not implemented in normal `lang` commands. A planned, not-started roadmap gate covers a narrow C ABI proof and shared boundary contract; see [docs/foreign-interop.md](docs/foreign-interop.md).

`lang build FILE` compiles a program with a supported `main() -> i32|bool|Text` as an executable. If that entrypoint is absent, it builds a library DLL. Artifacts are stored in a unique `out/<source-name>-<id>/` directory beside the input file. `lang run FILE` requires the supported entrypoint, prints its returned value and a newline, and exits 0 on success. Checked i32 arithmetic overflow reports a generic runtime fault and exits 70.

The CLI build/run/test commands work for the implemented source subset. Language-level CLI and web declarations remain incomplete; fixtures 14–19 are still pending and are not counted as passing checks.
