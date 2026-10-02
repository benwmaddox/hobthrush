# Hobthrush

**A language for AI-built software under explicit constraints.**

Named for the hobthrush of northern English folklore: a household spirit said to quietly complete useful work unseen. [Read about the tradition](https://www.ryedalefolkmuseum.co.uk/the-helpful-hobs-of-the-north-york-moors/).

![Watercolor of two hobthrushes building and independently checking a footbridge by moonlight](docs/assets/hobthrush-helpers.png)

The command is `hob`, source files use `.hob`, and packages use `hob.toml` and `hob.lock`. The language is pre-1.0; syntax and tooling may change without migration support.

## Goals and contributions

Hobthrush is an experimental language designed for AI systems to read, write, and maintain reliable software. Enforceable compiler rules, machine-readable contracts, and black-box outcome tests keep work on task. People set high-level goals and priorities; code inspection or intervention in an individual change should be rare. See the [project goals](docs/project-goals.md), [product brief](docs/PRD.md), and [roadmap](docs/roadmap.md).

Under the [proposed stewardship model](docs/public-ai-stewardship.md), general funding supports AI review of requests. Once AI review accepts a fix or feature, sponsors can directly promote it to funded work and supply implementation capacity. Sponsorship signals importance, but correctness fixes remain first and payment cannot buy acceptance. Most feature-specific funding should support ongoing maintenance; bug-fix funding is exempt. Up to 10% of funds may support human project management.

Issues, examples, documentation, and code contributions are inputs to the AI-operated development process. AI systems decide how to use them against project goals and constraints, and may incorporate them directly into updates. Please share only material you are comfortable having incorporated.

## Core decisions

- **Constrain interfaces for AI work.** The compiler checks types, exhaustive matches, declared effects, and capability use. Public APIs and machine-readable reports expose contracts so agents can use components as black boxes.
- **Make failure explicit.** `Option<T>` represents absence and `Result<T, E>` expected errors; exhaustive `match` expressions require handling every union case.
- **Keep values simple and effects controlled.** Ordinary values are immutable; `var` only rebinds a local. I/O requires explicit capabilities; supported scoped resources use lexical lifetimes. The runtime uses .NET garbage collection; see the [memory and performance contract](docs/memory-and-performance.md).
- **Start with the .NET platform.** The current bootstrap compiler is written in C# and emits C# for .NET. Native AOT is an optional, garbage-collected publish mode; a Hobthrush-written compiler is a later roadmap goal.
- **Pin dependencies and expose trust boundaries.** Git dependencies are locked to exact commits and content hashes. Catalogued .NET adapters have declared signatures and provenance, but run as trusted code with full process authority; audit reports are not a sandbox. See the [foreign interop contract](docs/foreign-interop.md).
- **Put AI in charge of detailed engineering.** AI systems write and maintain the compiler and standard library; other AI systems independently review changes. Compiler checks, black-box tests, and CI gates constrain acceptance. People set high-level goals and priorities; code is written primarily for AI to read and modify, and routine human review of changes is not assumed.
- **Keep the language surface stable as it matures.** After 1.0, changes to public syntax and semantics should be rare. Compiler, runtime, and tool internals may improve more often while preserving observable contracts. AI systems review and coordinate compatible standard-library growth, the preferred place for new general capabilities when they fit.

## Try it

Install the .NET SDK version pinned in [global.json](global.json), then run from the repository root:

```sh
dotnet build hobthrush.slnx --configuration Release
dotnet run --project src/Hob --configuration Release -- run examples/pure/src/main.hob
dotnet run --project src/Hob --configuration Release -- test examples/text-validation
```

The first program prints a value; the last command runs the validation library's language tests.

Use `hob new` to create a package, `hob add` to add dependencies, and `hob lock` to pin them. `hob fmt FILE_OR_PACKAGE` formats supported `.hob` source, and `hob fmt FILE_OR_PACKAGE --check` verifies canonical formatting without writing. `hob check`, `hob build`, `hob run`, and `hob test` check, build, run, and test your code. `hob inspect api`, `hob inspect effects`, and `hob audit` expose compiler-derived metadata. See the [command reference](docs/grammar.md) for arguments and limits.

## Explore

- [Text validation library](examples/text-validation): generic types, typed errors, and tests.
- [File scanner CLI](examples/scan-cli): typed arguments, generated help, and explicit file-read access.
- [Web app](examples/web): typed routes, JSON and HTML responses, and SQLite.

For the implemented syntax and limits, see the [grammar](docs/grammar.md). The [roadmap](docs/roadmap.md) tracks what works today and what remains planned. The [product brief](docs/PRD.md) explains the longer-term goals.
