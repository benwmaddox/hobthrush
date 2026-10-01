# Hobthrush

**An AI-built language for reliable software under clear constraints.**

Named for a folklore helper that quietly finishes people's work while they sleep.

![Watercolor of hobthrushes quietly crafting and checking wooden work by lamplight](docs/assets/hobthrush-helpers.png)

Hobthrush is an experimental general-purpose language that makes types, errors, and side effects visible to both developers and tools. Its compiler checks exhaustive matches and declared effects, then provides structured diagnostics and machine-readable API and audit reports. You can use the same toolchain to build libraries, typed command-line tools, and small web apps.

The command is `hob`, source files use `.hob`, and packages use `hob.toml` and `hob.lock`. The language is pre-1.0; syntax and tooling may change without migration support.

## Goals and contributions

The goal is one practical toolchain for reliable libraries, CLI tools, and small web apps. Code should be easy for agents to write and for people to inspect, with compiler feedback that exposes errors, effects, and API boundaries.

We want AI systems to design and implement components within strong, explicit constraints. A component should approach a black box for its callers: a clear interface and checked contract let them use it without following every internal implementation detail.

The intended development process is heavily AI-driven, including the core compiler and standard libraries. AI systems design, build, and test changes, with independent review by other AI systems. Human guidance stays at a high level: people collaborate on project goals, guide priorities, and oversee consequential decisions. AI systems carry out day-to-day development and review.

Under the [proposed stewardship model](docs/public-ai-stewardship.md), general funding supports categorizing and reviewing bugs and feature requests. Approved items are prioritized and open for sponsorship. Dedicated funds can then support specific features once their designs are approved and align with project goals. Up to 10% of funds may support human management of the project. Funding supports the work; acceptance still depends on independent AI review and technical checks.

Focused issues, examples, documentation, and pull requests are welcome. Contributions and feedback may be used directly as input to future AI-assisted updates to this project. Please share only material you are comfortable having incorporated into those updates.

## Try it

Install the .NET SDK version pinned in [global.json](global.json), then run from the repository root:

```sh
dotnet build hobthrush.slnx --configuration Release
dotnet run --project src/Hob --configuration Release -- run examples/pure/src/main.hob
dotnet run --project src/Hob --configuration Release -- test examples/text-validation
```

The first program prints a value; the last command runs the validation library's language tests.

Use `hob new` to create a package, `hob add` to add dependencies, and `hob lock` to pin them. `hob check`, `hob build`, `hob run`, and `hob test` check, build, run, and test your code. `hob inspect api`, `hob inspect effects`, and `hob audit` expose compiler-derived metadata. See the [command reference](docs/grammar.md) for arguments and limits.

## Explore

- [Text validation library](examples/text-validation): generic types, typed errors, and tests.
- [File scanner CLI](examples/scan-cli): typed arguments, generated help, and explicit file-read access.
- [Web app](examples/web): typed routes, JSON and HTML responses, and SQLite.

For the implemented syntax and limits, see the [grammar](docs/grammar.md). The [roadmap](docs/roadmap.md) tracks what works today and what remains planned. The [product brief](docs/PRD.md) explains the longer-term goals.
