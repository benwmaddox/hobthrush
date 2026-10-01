# Managed .NET adapters and foreign interop

**Status:** Milestone #748 adds a narrow catalog-gated managed adapter contract for one SHA-256 text operation. Broader adapter package support remains planned. The independent C ABI proof is planned, not started. Normal Hobthrush source has no arbitrary CLR, C#, reflection, or assembly-import feature.

**AI review principle:** Accepting or changing a trusted adapter requires separate AI systems to inspect its implementation and test outcomes; declarations and hashes alone are not enough. The current compiler does not automate that review process or sandbox adapter code.

## Managed .NET adapter package contract (planned)

A managed adapter is a reviewed package that exposes a small, closed set of Hobthrush declarations over a pinned .NET implementation. It does not make .NET types available to ordinary source. Packages and source providers remain separate from the language: only explicit dependency-management operations may materialize external sources; checks, builds, runs, tests, and audits consume a locked local snapshot without contacting a provider.

### Implemented slice: catalogued SHA-256 text

Only `kind = "lib"` packages may declare one `[managed_adapter]` table. The table has exactly these keys:

```toml
[managed_adapter]
bridge_id = "hob.sha256-text.v1"
target_framework = "net10.0"
assembly_path = "managed/Hob.ManagedAdapters.dll"
assembly_sha256 = "<64 lowercase hexadecimal digits>"
```

The bridge ID must name a compiler-supported catalog entry. The assembly path is package-relative and normalized; the digest pins its bytes. The catalog supplies the assembly identity, portability target `portable-anycpu-il`, operation map, selected dependency closure, and closure hash. A package cannot choose a CLR type or method, RID, native asset, or closure. Lock schema v3 binds the declaration and catalog-resolved adapter provenance to the package graph.

The package exposes a bodyless adapter declaration whose source function name is chosen by the package author and whose operation ID is catalog-selected:

```hob
pub adapter fn hash_utf8(value: Text) -> Text effects {} = "sha256.text.hash_utf8";
```

`adapter` is contextual in this declaration form. This first operation is synchronous, non-generic, exactly `Text -> Text`, and claims no effects or capabilities. Calls use normal qualified package references. The compiler checks the declaration against the bridge catalog; generated code calls only the catalogued wrapper.

Audit schema v9 and build receipt schema v3 include an always-present `managed_adapters` array. Each entry records logical package/source identity, bridge ID and contract version, target framework `net10.0` and portability target `portable-anycpu-il`, operation IDs and closed signatures/effects/capabilities, relative assembly identities/paths/hashes, the catalog closure hash, and `assurance: "claim_only"`. Git source identity in this adapter array uses its stable package ID and exact commit rather than serializing a URL that could contain a local path. No adapter provenance field contains a cache, workspace, or absolute assembly path. The receipt repeats this array and hashes the canonical audit snapshot. A catalog-linked adapter descriptor remains represented when no checked adapter declaration is exposed; that entry has `operations: []`. An audit graph with no adapter descriptor has an empty array.

These declarations and hashes are evidence for review. They do not prove implementation behavior. The compiler reads pinned assembly bytes and metadata to validate identity and hashes but does not load or execute adapter implementation code. At runtime, generated application code loads and calls the catalogued assembly in the application process, where it has full process authority; the contract provides no operating-system sandbox.

### Broader contract still planned

Future bridge entries may add operations only with an explicit reviewed catalog contract. Before accepting them, the contract must define:

- **Closed Hobthrush exports:** each operation has a stable ID and exact fully qualified Hobthrush name, parameter and result types, async status, declared effects, and required capability parameters. Signatures use supported Hobthrush types and opaque Hobthrush resource types, never CLR types, reflection handles, delegates, or arbitrary generic CLR values. The export set is explicit; a package cannot expose extra assembly members.
- **Typed failures:** expected failures map to the declared closed Hobthrush error type, normally through `Result<T, E>`. .NET exceptions do not cross the wrapper boundary as Hobthrush values. Unexpected faults remain host faults and are not relabeled as declared typed errors.
- **Effects and capabilities:** each operation declares its effect set and required capability values. The compiler checks calls against these declarations and root manifest grants. Audit and receipt assurances stay `claim_only`; hashes, declarations, compiler checks, and tests do not prove safety or contain trusted code.
- **Ownership and lifetime:** metadata defines input/output/handle ownership, copying or retention, lexical resource lifetime, disposal on success/error/cancellation, and transfer rules. Callbacks, unmanaged buffers, retained references, and cross-scope resource escape remain rejected until a separate contract supports them. Managed GC lifetime does not replace disposal of scoped handles.
- **Runtime compatibility:** each catalog entry defines supported target frameworks, runtime identifiers, operating systems, architectures, and shared/framework-dependent runtime assumptions. An adapter is unavailable for a target unless its pinned runtime assets are compatible.
- **Implementation identity:** every selected managed/native/runtime asset has an identity and SHA-256. A deterministic closure hash covers the complete selected dependency closure, not only the top-level DLL. Normal builds and runs do not probe unlisted assemblies, download code, or restore ambient NuGet packages.
- **Wrapper tests:** each operation has tests for representative success, declared failures, effect/capability behavior, and supported lifetime/cancellation paths. Results are review evidence, not proof.

Capabilities constrain checked Hobthrush code and organize trusted host integrations. They do not contain malicious managed code in the same process. Independent AI reviewers must inspect the adapter implementation as well as its claims.

## C ABI proof target (not started)

The separate ABI1 proof is closed to by-value `i32`, with no general pointers, buffers, strings, callbacks, aggregates, managed references, resource transfer, or `Result` mapping:

1. Build a tiny C library from source with an `int32_t` identity function. A checked Hobthrush caller invokes it through typed foreign IR; unsupported or mismatched scalar signatures are rejected.
2. Export a pure checked Hob `i32` transform through a NativeAOT shared library. Its C ABI returns status and writes the result through a caller-owned synchronous `int32_t` out-parameter. Translate checked runtime overflow to failure status; do not let exceptions cross the ABI. The out-parameter is not retained.
3. Call the exported function from C and Python hosts. After language control flow is available, separately demonstrate a meaningful pure-language validation library callable from both hosts. The scalar transform alone is not a validation/classification example.

This proof does not implement arbitrary foreign imports in normal `hob` commands or replace the managed adapter package contract. The C identity import's empty-effect declaration is a trusted assertion, not a compiler-proven fact. Unsupported effect, capability, and ownership profiles must be rejected rather than silently ignored. Richer effect/capability checking depends on M2; richer resource lifetime semantics depend on MS1. Limit resource ownership to explicit scoped handles; do not infer general borrowing for GC-managed values.

## Deployment and evidence

NativeAOT is a .NET deployment mode for a C-callable library, not a C compiler backend and not a no-GC runtime. The library is self-contained and needs no installed .NET runtime; only shared libraries are officially supported, static libraries are not, and unloading with `FreeLibrary` or `dlclose` is unsupported. See Microsoft's [Native AOT library guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries).

Pin SDK/toolchain, host and input versions, and target OS/architecture ABI. On Windows and Linux, record raw host-call timings, artifact size, build cost, and deployment limitations. This is narrow proof data, not completion of PERF1 or a numerical performance promise. NativeAOT remains GC-managed; C ABI is not a backend switch. Require broader semantically equivalent evidence and a separate decision before changing backends.
