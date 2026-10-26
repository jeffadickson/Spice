# Generated mutation qualification (iterations 330–333)

Requirements MUT-001 and MUT-002 are in docs/consistency.sdoc. Setup, individual
Rust/.NET commands, the combined campaign and Git transport are documented in
[mutation-frameworks.md](../docs/mutation-frameworks.md). Versions are pinned:
cargo-mutants 27.1.0, Stryker.NET 5.0.0. Windows and Ubuntu 24.04 WSL2 tool
acquisition/version checks passed; full Linux framework execution is not claimed.

The existing 28 real-engine managed checks now run through both the console and
six discoverable xUnit cases. Checkpoint 330 passed 242 Rust tests (one ignored),
formatting, Clippy with denied warnings, console checks and xUnit. Twelve tooling
contracts passed, including malformed budgets/shards/test names, actual disjoint
Rust candidate partitions, local-Git publication/rejection, and a synthetic clean
supervisor fixture. The latter uses stub framework reports and a tiny Rust crate;
its counters establish orchestration only, not numerical or framework coverage.
All four deliberate provenance/accounting guard mutations were detected. Native
frameworks are separate from those four guards and the 22 numerical regressions.
Twelve StrictDoc documents parsed; 213 unique IDs and 196 parent links resolved.

## Actual mutation evidence

| Run | Recorded result | Limit |
|---|---|---|
| Rust inventory | 3,264 candidates enumerated | Enumeration is not tested coverage |
| Rust quantity smoke | 36 selected; 5 killed, 30 unviable, 1 unattempted | Eight-minute partial run, analyses integration target only |
| Completed Stryker snapshot | 117 generated; 63 killed, 21 survived, 3 timed out, 15 uncovered, 5 compile errors, 10 ignored | Client.cs only; survivors await review |
| Full-suite nine-minute probe | Rust compilation consumed its remaining budget | Partial evidence, zero recorded Rust mutants |
| Later .NET-only six-minute probe | Deadline expired without a native outcome report | Counts unavailable, not a measured zero |

Exact source hashes, native report/log hashes, scope and survivor locations are in
framework-rust-smoke.json and framework-dotnet-smoke.json. The completed managed
snapshot predates the final tool-selector wrapper changes; it is retained rather
than replaced by unavailable later counts. Standalone development runs used dirty
working source and explicit hashes; they are not claimed as clean release builds.
Native JSON/logs/diffs remain in ignored local work paths.

The sampled Rust compile failures include E0599/E0271/E0277: generated constructors
such as SimResult::new cannot compile for our Result alias. These are unviable
framework mutations, not test-detected bugs. Alias-aware generator tuning remains
open. Do not infer a 100% mutation score from the five detected mutations. Stryker
scores may count timeouts; the separate kill/timeout/uncovered categories above
are authoritative. Survivors may indicate equivalent behavior or missing tests
and must be adjudicated rather than automatically suppressed.

A clean-source twelve-minute real endurance attempt on 76259ff expired during the
selected-mutation phase: status failed, mutation_status incomplete_timeout, zero
seeded cases and no generated framework phase. This valid failure report was
published to remote-runs/windows-x64-framework-integration-12m/20261026T172936Z-96c5ad0f-76259ff70e6f.
It is an endurance-only import, not a repeated full core/coverage workflow. Its raw
artifacts remain local. The positive synthetic supervisor contract separately
verified framework category/hash/version/status propagation followed by seeded
batches. A completed real full generated-mutation-plus-fuzz overnight run remains
pending; neither budget expiration nor a configured schedule proves qualification.

## Measured coverage

Current owned C# metrics and exact hashes are in framework-tooling-coverage.json.
Filtered mapped observations are retained in framework-tooling-coverage.fragments.xml;
that is an XML fragment archive, not a standalone Cobertura report. Complete native
collector reports remain in the ignored build paths recorded by the JSON report.

| Scope | Lines | Mapped branches |
|---|---:|---:|
| Framework supervisor, bounded runs/input contracts | 100.00% | 70.59% |
| Endurance, synthetic orchestration fixture | 90.20% | 67.54% |
| Remote publisher, local-Git import contracts | 75.82% | 68.03% |
| Four-guard mutation driver | 100.00% | 65.00% |
| Shared real-engine conformance checks | 100.00% | 67.65% |
| Owned managed client, six xUnit cases | 141/151 (93.38%) | 52/66 (78.79%) |

Multi-statement source lines and per-line maximum merging of duplicate mapped
state-machine locations limit interpretation. Full line coverage does not imply
all branches, failure paths or specifications were checked. Framework/vendor
internals and generated JSON metadata are excluded. PowerShell command tracing
hit 105/412 commands (25.49%) across six orchestration scripts; this is not line or
branch coverage. Native installers and subprocess launcher paths remain largely
unmeasured. Checkpoint/selected numerical mutation drivers and F# examples are
unmeasured by this instrumentation. Rust product coverage remains the separate
Linux 89.95% line measurement; no Rust branch metric is inferred. Windows Build
Tools/SDK installation passed a native compile/link/run probe, but instrumented
Windows Rust coverage has not yet been rerun.

The simulator remains a bounded CPU prototype. This tooling does not establish
full SPICE, GPU, exhaustive mutation, convergence or production qualification.
