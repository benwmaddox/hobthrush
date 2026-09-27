# lang

`lang` is a temporary working name for an agent-first general-purpose language under construction; the permanent name is pending. The executable is `lang` and source files use `.lang`. This repository is independent of Stasis.

The implemented language slice supports `i32`, `bool`, `Text`, immutable non-generic nominal structs, declared tagged unions, generic functions with argument-based type inference, `Option<T>`, `Result<T, E>`, payload and field checks, exhaustive `match`, scoped `if`/`else`, comparisons, and checked arithmetic. `Text.length` counts Unicode scalar values and `Text.trim()` removes leading and trailing Unicode whitespace. Every function declares an upper bound with `effects { ... }`; the compiler checks inferred direct and transitive effects against it. The current adapter slice is `FsRead.read_text`, which returns `Result<Text, FsError>` and requires the opaque `FsRead` capability as a parameter. `fs.read` is the only effect currently produced by an operation. See [docs/grammar.md](docs/grammar.md) for the syntax and limits.

## Bootstrap

Install the .NET 10.0.401 SDK, then from this directory:

```sh
dotnet build lang.slnx --configuration Release
dotnet run --project src/Lang --configuration Release -- check examples/pure/src/main.lang --json
dotnet run --project src/Lang --configuration Release -- run examples/pure/src/main.lang
dotnet run --project src/Lang --configuration Release -- run examples/types/src/main.lang
dotnet run --project src/Lang --configuration Release -- run examples/structs/src/main.lang
dotnet run --project src/Lang --configuration Release -- lock examples/library-package
dotnet run --project src/Lang --configuration Release -- check examples/library-package
dotnet run --project src/Lang --configuration Release -- build examples/library-package
dotnet run --project src/Lang --configuration Release -- run examples/library-package
dotnet run --project src/Lang --configuration Release -- check examples/text-validation
dotnet run --project src/Lang --configuration Release -- build examples/text-validation
dotnet run --project src/Lang --configuration Release -- test examples/text-validation
dotnet run --project src/Lang --configuration Release -- test
dotnet run --project tests/Lang.IntegrationTests --configuration Release
```

`lang check FILE` checks the source and returns 1 for invalid programs. `lang check FILE --json` prints stable diagnostic codes, source ranges, and severity, including an empty diagnostics array on success. `lang test FILE_OR_PACKAGE` typechecks and runs the source's managed language tests. Bare `lang test` keeps the compiler fixture mode and compares active fixtures against their exact expected diagnostic-code lists.

Foreign interop is not implemented in normal `lang` commands. A planned, not-started roadmap gate covers a narrow C ABI proof and shared boundary contract; see [docs/foreign-interop.md](docs/foreign-interop.md).

`lang build FILE` compiles a program with a supported `main() -> i32|bool|Text` or one typed command declaration as an executable. If both entry forms are absent, it builds a library DLL. Command builds also emit `command-schema.json` beside the executable. Artifacts are stored in a unique `out/<source-name>-<id>/` directory beside the input file. `lang run FILE` runs the supported main entry; `lang run FILE -- APPLICATION_ARGS` dispatches application arguments to a source file's typed command. Main runs print their returned value and a newline, and exit 0 on success. Checked i32 arithmetic overflow reports a generic runtime fault and exits 70.

`lang check PACKAGE_DIRECTORY`, `lang build PACKAGE_DIRECTORY`, and `lang run PACKAGE_DIRECTORY` load a package from its root `lang.toml`. The current package slice supports strict `lib` and `cli` manifests, module files under the declared `source_root`, fully qualified declaration references, and local path dependencies on library packages. A CLI package names one `entry_module`; that module may provide the existing supported `main() -> i32|bool|Text` or one typed command declaration. Main-based CLI packages remain supported. A library package has no entry module. The example in [`examples/library-package`](examples/library-package) uses `self::...` references for its own modules and a direct `validation::...` reference for its sibling [`examples/text-validation`](examples/text-validation) library.

The manifest has exactly these keys and TOML string values:

```toml
name = "validation-example"
version = "0.1.0"
kind = "cli"
source_root = "src"
entry_module = "app::main"

[dependencies]
validation = "../text-validation"
```

For `kind = "lib"`, omit `entry_module`; for `kind = "cli"`, it is required. Module paths and entry modules use `::`: `module app::main;` belongs in `src/app/main.lang` when `source_root = "src"`, and its manifest entry is `app::main`. User declarations always use a qualified reference with a root, module path, and declaration name. Use `self::catalog::message::Greeting` for a declaration in the current package, or `validation::text::validation::NormalizeError` for a declaration in the directly declared `validation` dependency. The alias `self` is reserved and cannot be used as a dependency alias; bare `self` follows ordinary local-name rules and is a namespace root only when followed by `::`. Aliases name only direct dependencies and are not re-exported transitively. `::` separates namespace components; `.` remains for value fields, member operations, and union variants, as in `self::catalog::message::Message.Ready(value)`. Built-in types and constructors such as `Option<T>`, `Some`, `None`, `Ok`, `Err`, and `FsError` keep their short forms. Local variables also remain ordinary unqualified expressions. There is no source-level import declaration; a legacy top-level `import` is a syntax error. Private declarations are module-local; a qualified reference that crosses a module or package boundary to a private declaration reports `E_ACCESS_PRIVATE`. Unknown aliases, modules, or declarations report `E_NAME_UNRESOLVED`. Package paths are local filesystem paths resolved relative to the declaring manifest. Git sources, registries, caches, `lang add` or other package-install commands, and build receipts are not implemented; path dependencies work offline.

Run `lang lock PACKAGE_DIRECTORY` to resolve a package's complete local dependency graph and write its canonical `lang.lock`. The lock uses portable relative paths, records package names and versions, and hashes normalized manifests and dependency source content. `check`, `build`, `run`, and `test` require a current lock whenever the graph has dependencies; a missing, malformed, or stale lock reports `E_LOCK` and directs you to run `lang lock`. Dependency source line-ending-only rewrites and files under `out/` do not stale the lock. Dependency-free packages need no lockfile.

Workstation GC is the V1 default runtime policy, explicitly configured for the compiler and generated applications. Normal `lang build` and `lang run` stay managed. To publish an executable from a source file or CLI package with Native AOT, use `lang build FILE_OR_PACKAGE --aot --rid RID`; build `win-x64` on Windows or `linux-x64` on Linux with the matching .NET Native AOT toolchain. The AOT build requires either a supported zero-argument `main() -> i32|bool|Text` or a typed command in a standalone source module or CLI package's entry module. It writes a native executable into the usual unique output directory, reporting its absolute path as `Built native executable: <path>`; typed-command builds also retain `command-schema.json` beside it. SDK publish output, including compatibility warnings, remains visible on stderr as unstructured text; a successful publish does not guarantee a warning-free build. The executable remains GC-managed. Library-only packages and the compiler tool are not AOT targets; shared-library exports belong to the separate ABI1 proof. `E_BUILD_TARGET` reports an unsupported RID, a host OS/RID mismatch, a missing/malformed AOT option combination, or AOT requested for a program without a supported entrypoint. An unrelated malformed CLI invocation retains normal usage handling.

The CLI build/run/test commands work for the implemented source subset. [`examples/text-validation`](examples/text-validation) is a pure `lib` package precursor that exercises `NormalizeError`, `normalize`, generic `require<T, E>`, and managed tests for empty/nonempty input, trimming, and typed `Option` values. Tests contain typed local setup and one final boolean `assert`; they are checked during normal library checks and builds, and `lang test PACKAGE_DIRECTORY` runs only that package's tests. Dependency tests are typechecked but are not run by the root package command. Runtime results are plain PASS/FAIL lines with source locations and a summary; there is no JSON result format, property-testing framework, or AOT test runner. Path dependencies remain local-only; the final maintained validation library still needs broader source support. Generic inference and its current limits are described in [docs/grammar.md](docs/grammar.md). Capability values can be used by library functions, but application manifests do not yet grant or inject them. Effect inspection, build receipts, and adapters beyond `FsRead.read_text` are deferred.
The PR1 typed CLI command layer is implemented for one command in an executable source module or CLI package's entry module. A declaration gives each argument, option, and flag help text; the compiler checks its generated `<PascalCaseName>Args` type against a fully qualified handler returning `Result<Text, E>` and a pure `E -> Text` error formatter. Handlers retain ordinary effect declaration and inference checks. `lang run FILE_OR_PACKAGE -- APPLICATION_ARGS` forwards arguments to a typed command parser. Top-level and command `--help` exit 0; parse failures use stable `CLI_*` codes and exit 2; `Ok(Text)` writes stdout and exits 0; formatted `Err(E)` writes stderr and exits 3; unexpected runtime faults exit 70. Builds with typed commands emit deterministic `command-schema.json` beside the executable artifact. See [docs/grammar.md](docs/grammar.md) for the declaration syntax and runtime contract. This command layer does not grant or inject `FsRead`; its args-only handler signature cannot receive the capability yet. Manifest capability grants/injection, effect inspection, build receipts, additional adapters, the maintained file-scanning example, and its audit remain future work. Fixture 17 is active; fixtures 15, 16, 18, and 19 remain pending. Fixtures 14, 26–30, and 31–36 cover effects, control-flow/comparisons, and generic-function diagnostics.
