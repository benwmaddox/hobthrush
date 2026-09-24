# al

`al` is an agent-first general-purpose language under construction. The executable is `al` and source files use `.al`. This repository is independent of Stasis.

The implemented pure-language slice supports `i32`, `bool`, `Text`, declared tagged unions, `Option<T>`, `Result<T, E>`, payload type checks, and exhaustive `match`. Effects must be declared as `effects {}`. The current parser and constructor decisions are described in [docs/grammar.md](docs/grammar.md).

## Bootstrap

Install the .NET 10.0.401 SDK, then from this directory:

```sh
dotnet build al.slnx --configuration Release
dotnet run --project src/Al --configuration Release -- check examples/pure/src/main.al --json
dotnet run --project src/Al --configuration Release -- run examples/pure/src/main.al
dotnet run --project src/Al --configuration Release -- run examples/types/src/main.al
dotnet run --project src/Al --configuration Release -- test
dotnet run --project tests/Al.IntegrationTests --configuration Release
```

`al check FILE` checks the source and returns 1 for invalid programs. `al check FILE --json` prints stable diagnostic codes, source ranges, and severity, including an empty diagnostics array on success. `al test` checks the active fixtures against their exact expected diagnostic-code lists.

`al build FILE` compiles a program with a supported `main() -> i32|bool|Text` as an executable. If that entrypoint is absent, it builds a library DLL. Artifacts are stored in a unique `out/<source-name>-<id>/` directory beside the input file. `al run FILE` requires the supported entrypoint, prints its returned value and a newline, and exits 0 on success. Checked i32 arithmetic overflow reports a generic runtime fault and exits 70.

The CLI build/run/test commands work for the implemented source subset. Language-level CLI and web declarations remain incomplete; fixtures 14–19 are still pending and are not counted as passing checks.
