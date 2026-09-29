# Managed .NET adapters and foreign interop

**Status:** The managed adapter contract below is a planned package contract. Adapter package loading and execution are not implemented. The independent C ABI proof is also planned, not started. Normal Lang source has no arbitrary CLR, C#, reflection, or assembly-import feature.

## Managed .NET adapter package contract (planned)

A managed adapter is a reviewed package that exposes a small, closed set of Lang declarations over a pinned .NET implementation. It does not make .NET types available to ordinary source. Packages and source providers remain separate from the language: only explicit dependency-management operations may materialize external sources; checks, builds, runs, tests, and audits consume a locked local snapshot without contacting a provider.

An adapter package contract must include the following metadata before the compiler or runtime may accept it:

- **Reviewed bridge identity:** a stable `bridge_id` identifying the reviewed host bridge implementation and contract revision. The bridge ID is selected from an explicit trusted catalog; package metadata cannot invent or override a bridge implementation.
- **Closed Lang exports:** each export has a stable operation ID and exact fully qualified Lang name, parameter and result types, async status, declared effects, and required capability parameters. Signatures contain only supported Lang types and opaque Lang resource types, never CLR types, reflection handles, delegates, or arbitrary generic CLR values. The export set is explicit; a package cannot expose extra assembly members.
- **Typed failures:** expected failures map to the declared closed Lang error type, normally through `Result<T, E>`. .NET exceptions must not cross the wrapper boundary as Lang values. Unexpected faults remain host faults and must not be relabeled as a declared typed error.
- **Effects and capabilities:** each operation declares its effect set and the capability values it needs. The compiler checks calls against these declarations and root manifest grants. Audit and receipt output labels adapter assurances `claim_only`; hashes, declarations, compiler checks, and tests do not prove that implementation behavior matches its claims or sandbox it.
- **Ownership and lifetime:** metadata states how every input, output, and opaque handle is owned, whether values are copied or retained, the lexical lifetime of resources, disposal behavior on success/error/cancellation, and any transfer rules. Callbacks, unmanaged buffers, retained references, and cross-scope resource escape are rejected until a separate contract supports them. Managed GC lifetime is not a substitute for disposing scoped handles.
- **Runtime compatibility:** the package pins its target framework (initially `net10.0`), supported runtime identifiers/operating systems/architectures, and any required shared or framework-dependent runtime assumptions. An adapter is unavailable for a build target unless its declared runtime assets are compatible with that TFM and RID.
- **Implementation identity:** the package names every loaded managed assembly and every selected native/runtime asset with a SHA-256. A deterministic closure hash covers the complete selected dependency closure for the declared TFM/RID, not only the top-level adapter DLL. Resolution may use only that pinned closure; no unlisted assembly probing, dynamic download, or ambient NuGet restore is allowed during normal builds or runs.
- **Wrapper tests:** every exported operation has wrapper tests for representative success, declared failures, capability/effect behavior, and supported lifetime/cancellation paths. Tests run against the pinned closure. Their results are useful review evidence, not a proof of safety or correctness.

The adapter package schema is not frozen and no manifest syntax is implemented. The proposed fields describe the review contract, not a currently accepted `lang.toml`. Before implementation, its lock, audit, and receipt representations must carry stable source identity, exact assembly and closure hashes, bridge ID, and target compatibility without physical cache paths.

Capabilities constrain checked Lang code and organize trusted host integrations. They do not contain malicious managed code in the same process. A trusted adapter can use the full authority of the process; reviewers must inspect and test the implementation as well as its declarations. First-party adapters and current audit trust entries have the same claim-only limit.

## C ABI proof target (not started)

The separate ABI1 proof is closed to by-value `i32`, with no general pointers, buffers, strings, callbacks, aggregates, managed references, resource transfer, or `Result` mapping:

1. Build a tiny C library from source with an `int32_t` identity function. A checked Lang caller invokes it through typed foreign IR; unsupported or mismatched scalar signatures are rejected.
2. Export a pure checked Lang `i32` transform through a NativeAOT shared library. Its C ABI returns status and writes the result through a caller-owned synchronous `int32_t` out-parameter. Translate checked runtime overflow to failure status; do not let exceptions cross the ABI. The out-parameter is not retained.
3. Call the exported function from C and Python hosts. After language control flow is available, separately demonstrate a meaningful pure-language validation library callable from both hosts. The scalar transform alone is not a validation/classification example.

This proof does not implement managed adapter packages, package loading, production effect/capability/resource enforcement, or imports in normal `lang` commands. The C identity import's empty-effect declaration is a trusted assertion, not a compiler-proven fact. Unsupported effect, capability, and ownership profiles must be rejected rather than silently ignored. Richer effect/capability checking depends on M2; richer resource lifetime semantics depend on MS1. Limit resource ownership to explicit scoped handles; do not infer general borrowing for GC-managed values.

## Deployment and evidence

NativeAOT is a .NET deployment mode for a C-callable library, not a C compiler backend and not a no-GC runtime. The library is self-contained and needs no installed .NET runtime; only shared libraries are officially supported, static libraries are not, and unloading with `FreeLibrary` or `dlclose` is unsupported. See Microsoft's [Native AOT library guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries).

Pin SDK/toolchain, host and input versions, and target OS/architecture ABI. On Windows and Linux, record raw host-call timings, artifact size, build cost, and deployment limitations. This is narrow proof data, not completion of PERF1 or a numerical performance promise. NativeAOT remains GC-managed; C ABI is not a backend switch. Require broader semantically equivalent evidence and a separate decision before changing backends.