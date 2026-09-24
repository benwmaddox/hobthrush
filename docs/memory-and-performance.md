# Memory semantics and performance evaluation

This contract records the accepted .NET implementation baseline and the decisions that remain open. It informs [the PRD](PRD.md) and [the roadmap](roadmap.md); it does not implement a runtime or benchmark suite.

## Accepted V1 commitments

- Keep the compiler bootstrap/build tool in C#, emit C#, and use the pinned .NET SDK/runtime. A later lang-authored compiler does not require replacing the first-party runtime.
- Use the .NET GC-managed runtime for allocations; ordinary source values may lower to references or inline/value types. Scoped resource handles (such as files, transactions, and response bodies) require deterministic cleanup on success, error, and cancellation; garbage collection is not resource-scope cleanup.
- Define portable source semantics independently of the selected C# representation. A source struct is not promised to be a C# record, class, or value type.
- Generated heap-allocated C# records are a current prototype, not a public allocation, identity, or layout guarantee. Do not restrict source syntax solely to suit a possible lowering.
- Scoped resource checks do not imply a general borrow checker, ownership proof, deterministic execution, real-time behavior, or security sandbox. Keep the foreign/adapter trust boundary explicit.

## MS1: memory-semantics design (not started)

MS1 must finish before SH1 and before adding mutable-sharing or resource-lifetime semantics that would lock choices. It is a decision gate, not an implemented ownership model. Its decision matrix must cover:

- Assignment and parameter passing; copying versus sharing; identity and equality; and mutation, including collection and closure behavior.
- Single-owner/borrow/escape rules for lexical scoped resource handles only, cleanup on success/error/cancellation, and interaction with foreign adapters. Do not extend this into general ownership or borrow rules for ordinary GC-managed objects.
- Positive and negative language-level conformance programs for each selected rule. Mark unresolved choices as pending rather than deriving source behavior from current emitted records.
- Focused prototypes and conformance cases comparing reference-record and inline/value-type representations. They must show equivalent behavior before a lowering change is adopted; MS1 does not require two full production lowerings or backends.

## PERF1: performance evaluation (not started)

PERF1 evaluates choices; it sets no speed, memory, latency, startup, build-time, or published-size target. Use pure-library/compiler workloads, CLI cold-start and file-processing workloads, and web/SQLite workloads when those applications exist. Evaluate the default .NET deployment against NativeAOT and compare reference-record with efficient value-type lowering under equivalent behavior. NativeAOT remains garbage-collected; record compatibility warnings/errors and unsupported adapters. This is not a commitment to a second backend or to a runtime rewrite.

Pin the hardware, OS, .NET SDK, workload and input versions. Record commands, warmup, repetitions, and raw results. Measure startup, bytes allocated per operation and collection counts, steady and peak memory, throughput, p95/p99 latency, published size, and build cost. Preserve semantic equivalence in comparisons. Path or line-ending normalization may be narrow and documented; it must not hide behavior or diagnostics differences. A directly authored C# baseline may be useful; Rust comparison is optional and only meaningful for a fair equivalent workload. Make no Rust parity or numerical performance claim until the data supports it.

PERF1 measurements are required to inform representation and deployment decisions, not to pass a numeric performance gate. Continue to meet the three application acceptance gates in the PRD. No formal-proof, hard real-time, or security-sandbox guarantee follows from these measurements.
