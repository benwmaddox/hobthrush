# Roadmap and current claims

The source PRD lives outside this repository. The package and executable names here are `al`, and source files use `.al`.

## M0

- [x] Pin .NET 10 LTS and create a buildable C# repository.
- [x] Draft grammar, diagnostic registry, and 20 canonical source fixtures.
- [ ] Make all fixtures executable compiler checks. The first parser slice only covers pure functions; pending fixtures are explicit in `fixtures/manifest.json`.
- [ ] Add Windows and Linux CI.

## M1

- [x] Parse, check, and run a small pure function slice through generated C#.
- [ ] Modules and package imports; generic APIs and minimal static traits.
- [ ] Structs, tagged unions, exhaustive `match`, `Option` and `Result`.
- [ ] Typed semantic IR independent of C# and full source mapping.

Subsequent gates are effect inference/audit, capabilities, CLI and packages, web, and SQLite. The current compiler does not claim these checks.

## Initial syntax decisions

The tool and extension are `al` and `.al`. A module header uses `module dotted.name;`. A package root is `al.toml`. Function effects follow the return type (`effects { ... }`). Values are immutable by default. The initial integer slice uses `i32`; arithmetic uses checked operations.
