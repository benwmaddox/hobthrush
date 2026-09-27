# Memory semantics and performance evaluation

This contract records the accepted .NET implementation baseline and the decisions that remain open. It informs [the PRD](PRD.md) and [the roadmap](roadmap.md); it does not implement a runtime or benchmark suite.

## Accepted V1 commitments

- Keep the compiler bootstrap/build tool in C#, emit C#, and use the pinned .NET SDK/runtime. A later lang-authored compiler does not require replacing the first-party runtime.
- Use the .NET GC-managed runtime with Workstation GC as the V1 default, explicitly selected for the compiler and generated applications. Native AOT remains GC-managed under the same policy. Scoped resource handles (such as files, transactions, and response bodies) require deterministic cleanup on success, error, and cancellation; garbage collection is not resource-scope cleanup.
- Define portable source semantics independently of the selected C# representation. A source struct is not promised to be a C# record, class, or value type.
- Generated heap-allocated C# records are a current prototype, not a public allocation, identity, or layout guarantee. Do not restrict source syntax solely to suit a possible lowering.
- Scoped resource checks do not imply a general borrow checker, ownership proof, deterministic execution, real-time behavior, or security sandbox. Keep the foreign/adapter trust boundary explicit.

## MS1: memory-semantics design (not started)

MS1 must finish before SH1 and before adding mutable-sharing or resource-lifetime semantics that would lock choices. It is a decision gate, not an implemented ownership model. Its decision matrix must cover:

- Assignment and parameter passing; copying versus sharing; identity and equality; and mutation, including collection and closure behavior.
- Single-owner/borrow/escape rules for lexical scoped resource handles only, cleanup on success/error/cancellation, and interaction with foreign adapters. Do not extend this into general ownership or borrow rules for ordinary GC-managed objects.
- Positive and negative language-level conformance programs for each selected rule. Mark unresolved choices as pending rather than deriving source behavior from current emitted records.

## PERF1: performance evaluation (not started)

PERF1 measures representative applications without setting a speed, memory, latency, startup, build-time, or published-size target. Use executable CLI workloads and, when available and compatible, web/SQLite applications. Compare the ordinary managed build with an optional Native AOT executable built from the same source and using the same Workstation GC policy on the matching host OS. Native AOT is a deployment publish mode for executable applications only: it requires a supported runtime identifier (RID) and native toolchain, remains garbage-collected, and may reject code or adapters that depend on unsupported dynamic features. Preserve successful publish warnings when recording compatibility limits; these remain unstructured SDK text rather than language diagnostics, and successful publishing is not a no-warning guarantee. The compiler tool and shared-library exports are outside this deployment path; the latter belong to the separate ABI1 proof. This is not a second backend or a runtime rewrite.

Pin the hardware, OS, .NET SDK, RID, native toolchain, workload and input versions. Record commands, warmup, repetitions, and raw results. Measure startup, bytes allocated per operation and collection counts, steady and peak memory, throughput, p95/p99 latency, published size, and build cost. Preserve program behavior in comparisons. Path or line-ending normalization may be narrow and documented; it must not hide behavior or diagnostics differences. Report compatibility limits and unsupported adapters. These measurements support deployment choices; they establish no cross-language comparison or numerical performance guarantee.

PERF1 measurements inform deployment decisions; they are not a numeric performance gate. Continue to meet the three application acceptance gates in the PRD. No formal-proof, hard real-time, or security-sandbox guarantee follows from these measurements.
