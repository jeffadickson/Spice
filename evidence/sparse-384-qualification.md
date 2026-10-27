# Sparse AC checkpoint 384

Campaign steps 41–50 (375–384), source fa964ad plus documentation/evidence.
Fifty of the interpreted 100 implementation steps are complete; final review
remains pending. checkpoint-384.json records 288 Rust tests, 29 managed
conformance checks, 58 xUnit cases, build/fmt/Clippy and .NET analyzer/XMLDoc gates.
One legacy endurance test remains intentionally ignored.

Canonical CSC union and scaled combinations preserve structural cancellation
slots. Complex G+j*omega*Q assembly accepts genuine complex excitation. Faer and
nalgebra LU factor once per complex RHS system; faer AC uses direct CSC. Only
Unsupported permits a same-provider dense reference fallback. Independent sparse
componentwise backward error must pass 1e-10 before results are exposed.
Diode incremental conductances stamp directly after bounded DC bias; the
nonlinear DC controller still uses its existing reference Jacobians.

A new regression first failed with a 1e308 nominal source and 1+2j override:
subtracting the nominal RHS erased the real amplitude. Direct selected-amplitude
assembly fixes voltage and current overrides on both providers. Full-grid
validation now precedes nonlinear bias/provider work. An independently derived
two-diode matrix checks terminal signs and derivatives; malformed/provider-error
and residual checks challenge incorrect outputs. Proptest's new RC transfer
property passed 4096 configured cases with the persisted default seed
0x5350494345333535, shrinking enabled, across dense LU and faer. No new
counterexample was reported by that run.

Fresh Linux instrumentation on fa964ad measured 4760/5288 owned Rust lines
(90.0151%). Every recorded src/test hash matches this checkpoint's Rust source.
Rust branch coverage is unavailable. The 68 LLVM mapping warnings were inspected:
two repeated zero-hash tracing_core symbols, no owned/unexpected symbols.
See sparse-384-rust-coverage.json and sparse-384-llvm-diagnostics.json.
Managed source did not change: checkpoint 374's 161/169 client lines and
109/112 mapped branches remain historical matching-source evidence, not a new
coverage execution. Helpers/vendor internals/GPU are outside this Rust measure.

The actual cargo-mutants 27.1.0 AC run is still active on its isolated fa964ad
snapshot. sparse-384-mutation-partial.json preserves a bounded, hashed snapshot:
80 inventoried, 21 completed, two caught, five survived and 14 unviable at that
snapshot, no timeout. Surviving grid/source-budget boundaries are concrete test
gaps for the next tranche. Compile errors and unattempted candidates are not
kills; no complete mutation score is claimed. Its final result will be retained
with later checkpoint evidence. Stryker was not rerun for unchanged managed code.

All 13 StrictDoc files parse using 0.30.4; 228 unique IDs and 214 parent links
resolve. SPARSE-001 through SPARSE-005 remain Draft. Sphinx 9.1.0 builds with
warnings as errors and 221 generated XMLDoc members.

Existing named ngspice comparisons were actually rerun against installed 47+
(compiled 2026-10-24): res_simple source current and res_partition AC voltage
match at 1e-12; lowpass AC components pass 5e-8, constrained by rounded print
output. Requests select dense LU, so these are not direct-faer external oracle
evidence. Executable hashes/results are in sparse-384-existing-oracles.json.

Prepared complex factor reuse, broader AC circuits/named oracles, sparse
transient, further models and the final deep review remain. The strict bridge
convergence failure and earlier partial campaigns remain preserved. No full
SPICE, GPU, large-circuit performance or production qualification is claimed.
