# Foreign interop contract and planned proof

**Status: planned, not started.** No C ABI proof, foreign syntax, package support, or normal-command import path is implemented. This plan preserves the C#/.NET V1 runtime, compiler, and output backend; it does not add a second backend.

## Shared boundary contract

Use one declared contract for .NET adapters and C ABI calls/exports. It will identify:

- Exact language signature and stable identity; pinned library hash, library/symbol identity, target OS/architecture ABI, and calling convention.
- Value representation, ownership and lifetime rules, and error translation. Runtime exceptions must not cross a C ABI boundary.
- Declared effects and required capabilities, separately from trusted foreign behavior claims.
- Buffer length/encoding and callback direction/lifetime rules as future contract areas; they are outside the scalar proof.

The compiler may check declarations and call/signature agreement once the feature is implemented, but cannot prove foreign code follows its declaration. Importing a package does not grant ambient capabilities. No arbitrary .NET assembly or direct .NET type access follows from C ABI support.

## ABI1 proof target (not started)

The first proof is closed to by-value `i32` data, with no general pointers, buffers, strings, callbacks, aggregates, managed references, resource transfer, or `Result` mapping:

1. Build a tiny C library from source with an `int32_t` identity function. A checked language caller invokes it through typed foreign IR; unsupported or mismatched scalar signatures are rejected.
2. Export a pure checked language `i32` transform through a NativeAOT shared library. Its C ABI returns status and writes the result through a caller-owned synchronous `int32_t` out-parameter. Translate checked runtime overflow to failure status; do not let exceptions cross the ABI. The out-parameter is not retained.
3. Call the exported function from C and Python hosts. After language control flow is available, separately demonstrate a meaningful pure-language validation library callable from both hosts. The scalar transform alone is not a validation/classification example.

The proof does not add import/export syntax, package loading, production effect/capability/resource enforcement, or support to normal `lang` commands. The C identity import's empty-effect/purity declaration is a trusted assertion, not a compiler-proved fact. Until the corresponding models are supported, reject unsupported effect, capability, or ownership profiles rather than silently ignoring them. The contract records these fields, but this scalar proof does not establish their enforcement. Richer effect/capability checking waits for M2; richer resource ownership/lifetime semantics wait for MS1. Limit resource ownership decisions to lexical scoped handles; do not infer general borrowing for ordinary GC-managed values.

## Deployment and evidence

NativeAOT is a .NET deployment mode for a C-callable library, not a C compiler backend and not a no-GC runtime. The library is self-contained and needs no installed .NET runtime; only shared libraries are officially supported, static libraries are not, and unloading with `FreeLibrary` or `dlclose` is unsupported. See Microsoft's [Native AOT library guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries).

Pin SDK/toolchain, host and input versions, and the target OS/architecture ABI. On Windows and Linux, record raw host-call timings, artifact size, build cost, and deployment limitations. This is narrow proof data, not completion of PERF1 or a numerical performance promise. NativeAOT remains GC-managed; C ABI is not a backend switch. Require broader semantically equivalent evidence and a separate decision before changing backends.
