# al

`al` is an agent-first general-purpose language under construction. The executable is `al` and source files use `.al`. This repository is independent of Stasis.

The first implemented slice checks and runs small pure integer functions. The broader language, effects, capabilities, packages, CLI and web targets are specified in [docs/roadmap.md](docs/roadmap.md) and are not yet implemented.

## Bootstrap

Install the .NET 10.0.401 SDK, then from this directory:

```sh
dotnet build al.slnx
dotnet run --project src/Al -- check examples/pure/src/main.al --json
dotnet run --project src/Al -- run examples/pure/src/main.al
dotnet run --project src/Al -- test
```

`al check` returns 1 for invalid source and prints diagnostics with stable codes, source ranges, and severity. `al test` runs the currently supported fixture cases. Unsupported language features are tracked separately in the fixture manifest and never reported as passing checks.
