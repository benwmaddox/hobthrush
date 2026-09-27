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

`lang build FILE` compiles a program with a supported `main() -> i32|bool|Text` as an executable. If that entrypoint is absent, it builds a library DLL. Artifacts are stored in a unique `out/<source-name>-<id>/` directory beside the input file. `lang run FILE` requires the supported entrypoint, prints its returned value and a newline, and exits 0 on success. Checked i32 arithmetic overflow reports a generic runtime fault and exits 70.

`lang check PACKAGE_DIRECTORY`, `lang build PACKAGE_DIRECTORY`, and `lang run PACKAGE_DIRECTORY` load a package from its root `lang.toml`. The current package slice supports strict `lib` and `cli` manifests, module files under the declared `source_root`, same-package imports, and local path dependencies on library packages. A CLI package names one `entry_module`; `lang run` uses that module's supported `main() -> i32|bool|Text`. A library package has no entry module. The example in [`examples/library-package`](examples/library-package) imports its sibling [`examples/text-validation`](examples/text-validation) library through a path dependency and also imports a module from its own package.

The manifest has exactly these keys and TOML string values:

```toml
name = "validation-example"
version = "0.1.0"
kind = "cli"
source_root = "src"
entry_module = "app.main"

[dependencies]
validation = "../text-validation"
```

For `kind = "lib"`, omit `entry_module`; for `kind = "cli"`, it is required. The manifest reader accepts blank lines, comments outside quoted values, and simple double-quoted strings without escapes. It requires a filesystem-safe package name, a non-empty version string, one of the two supported kinds, and a normalized forward-slash relative `source_root` beneath the package root. `version` is recorded as a string; semver validation is not implemented. Text such as `#` inside a quoted value remains part of that value. Module headers map to source paths relative to `source_root`: `module text.validation;` belongs in `src/text/validation.lang`. A trailing `[dependencies]` table declares identifier aliases mapped to relative package directories; its assignments must be simple quoted paths and the target package must have `kind = "lib"`. For example, `validation = "../text-validation"` makes the sibling package available as `validation`. Imports list the symbols a module uses: `import text.validation { validate, Error };` for a same-package module, or `import validation::text.validation { normalize, NormalizeError };` through a declared dependency alias. Each symbol must be public in the named module; imports are explicit and do not pass through transitively. Package paths are local filesystem paths resolved relative to the declaring manifest. Git sources, registries, caches, `lang add` or other package-install commands, and build receipts are not implemented; path dependencies work offline.

Run `lang lock PACKAGE_DIRECTORY` to resolve a package's complete local dependency graph and write its canonical `lang.lock`. The lock uses portable relative paths, records package names and versions, and hashes normalized manifests and dependency source content. `check`, `build`, `run`, and `test` require a current lock whenever the graph has dependencies; a missing, malformed, or stale lock reports `E_LOCK` and directs you to run `lang lock`. Dependency source line-ending-only rewrites and files under `out/` do not stale the lock. Dependency-free packages need no lockfile.

Workstation GC is the V1 default runtime policy, explicitly configured for the compiler and generated applications. Normal `lang build` and `lang run` stay managed. To publish an executable from a source file or CLI package with Native AOT, use `lang build FILE_OR_PACKAGE --aot --rid RID`; build `win-x64` on Windows or `linux-x64` on Linux with the matching .NET Native AOT toolchain. The AOT build requires the supported zero-argument `main() -> i32|bool|Text` entrypoint and writes a native executable into the usual unique output directory, reporting its absolute path as `Built native executable: <path>`. SDK publish output, including compatibility warnings, remains visible on stderr as unstructured text; a successful publish does not guarantee a warning-free build. The executable remains GC-managed. Library-only packages and the compiler tool are not AOT targets; shared-library exports belong to the separate ABI1 proof. `E_BUILD_TARGET` reports an unsupported RID, a host OS/RID mismatch, a missing/malformed AOT option combination, or AOT requested for a program without a supported entrypoint. An unrelated malformed CLI invocation retains normal usage handling.

The CLI build/run/test commands work for the implemented source subset. [`examples/text-validation`](examples/text-validation) is a pure `lib` package precursor that exercises `NormalizeError`, `normalize`, generic `require<T, E>`, and managed tests for empty/nonempty input, trimming, and typed `Option` values. Tests contain typed local setup and one final boolean `assert`; they are checked during normal library checks and builds, and `lang test PACKAGE_DIRECTORY` runs only that package's tests. Dependency tests are typechecked but are not run by the root package command. Runtime results are plain PASS/FAIL lines with source locations and a summary; there is no JSON result format, property-testing framework, or AOT test runner. Path dependencies remain local-only; the final maintained validation library still needs broader source support. Generic inference and its current limits are described in [docs/grammar.md](docs/grammar.md). Capability values can be used by library functions, but application manifests do not yet grant or inject them. Effect inspection, build receipts, and adapters beyond `FsRead.read_text` are deferred. Language-level CLI and web declarations remain incomplete; fixtures 15–19 are pending, fixture 14 covers transitive effect rejection, fixtures 26–30 cover control-flow and comparison checks, and fixtures 31–36 cover generic function diagnostics.
