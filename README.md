# lang

**A language for software written with agents and reviewed by people.**

`lang` is an experimental general-purpose language that makes types, errors, and side effects visible to both developers and tools. Its compiler checks exhaustive matches and declared effects, then provides structured diagnostics and machine-readable API and audit reports. You can use the same toolchain to build libraries, typed command-line tools, and small web apps.

The name is temporary, and the language is pre-1.0. Syntax and tooling may change without migration support.

## Goals and contributions

The goal is one practical toolchain for reliable libraries, CLI tools, and small web apps. Code should be easy for agents to write and for people to inspect, with compiler feedback that exposes errors, effects, and API boundaries.

Focused issues, examples, documentation, and pull requests are welcome. Contributions and feedback may be used directly as input to future AI-assisted updates to this project. Please share only material you are comfortable having incorporated into those updates.

## Try it

Install the .NET SDK version pinned in [global.json](global.json), then run from the repository root:

```sh
dotnet build lang.slnx --configuration Release
dotnet run --project src/Lang --configuration Release -- run examples/pure/src/main.lang
dotnet run --project src/Lang --configuration Release -- test examples/text-validation
```

The first program prints a value; the last command runs the validation library's language tests.

The CLI provides `lang new`, `lang add`, `lang check`, `lang build`, `lang run`, `lang test`, `lang lock`, `lang audit`, `lang inspect effects`, and `lang inspect api`. See the [grammar](docs/grammar.md) for command forms and options.

## Explore

- [Text validation library](examples/text-validation): generic types, typed errors, and tests.
- [File scanner CLI](examples/scan-cli): typed arguments, generated help, and explicit file-read access.
- [Web app](examples/web): typed routes, JSON and HTML responses, and SQLite.

For the implemented syntax and limits, see the [grammar](docs/grammar.md). The [roadmap](docs/roadmap.md) tracks what works today and what remains planned. The [product brief](docs/PRD.md) explains the longer-term goals.
