# Hobthrush

**An AI-built language for reliable software under clear constraints.**

Named for the hobthrush of northern English folklore: a household spirit said to quietly complete useful work unseen. [Read about the tradition](https://www.ryedalefolkmuseum.co.uk/the-helpful-hobs-of-the-north-york-moors/).

![Watercolor of two hobthrushes building and independently checking a footbridge by moonlight](docs/assets/hobthrush-helpers.png)

The command is `hob`, source files use `.hob`, and packages use `hob.toml` and `hob.lock`. The language is pre-1.0; syntax and tooling may change without migration support.

## Goals and contributions

Hobthrush is an experimental general-purpose language for reliable libraries, CLI tools, and small web apps. Goals and current scope are described in the [product brief](docs/PRD.md) and [roadmap](docs/roadmap.md).

Under the [proposed stewardship model](docs/public-ai-stewardship.md), general funding supports categorizing and reviewing bugs and feature requests. Approved items are prioritized and open for sponsorship. Dedicated funds can then support specific features once their designs are approved and align with project goals. Up to 10% of funds may support human management of the project. Funding supports the work; acceptance still depends on independent AI review and technical checks.

Focused issues, examples, documentation, and pull requests are welcome. Contributions and feedback may be used directly as input to future AI-assisted updates to this project. Please share only material you are comfortable having incorporated into those updates.

## Core decisions

- **Constrain interfaces for AI work.** The compiler checks types, exhaustive matches, declared effects, and capability use. Public APIs and machine-readable reports expose contracts so agents can use components as black boxes.
- **Make failure explicit.** `Option<T>` represents absence and `Result<T, E>` expected errors; exhaustive `match` expressions require handling every union case.
- **Keep values simple and effects controlled.** Ordinary values are immutable; `var` only rebinds a local. I/O requires explicit capabilities; supported scoped resources use lexical lifetimes. The runtime uses .NET garbage collection; see the [memory and performance contract](docs/memory-and-performance.md).
- **Start with the .NET platform.** The current bootstrap compiler is written in C# and emits C# for .NET. Native AOT is an optional, garbage-collected publish mode; a Hobthrush-written compiler is a later roadmap goal.
- **Pin dependencies and expose trust boundaries.** Git dependencies are locked to exact commits and content hashes. Catalogued .NET adapters have declared signatures and provenance, but run as trusted code with full process authority; audit reports are not a sandbox. See the [foreign interop contract](docs/foreign-interop.md).
- **Use one toolchain and an AI review loop.** The intended scope is libraries, CLI tools, and small web apps. The development model is for AI systems to build and test the core compiler and standard libraries, with independent review by other AI systems. People set goals and priorities at a high level.

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
