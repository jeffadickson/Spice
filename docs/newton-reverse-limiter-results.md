# MOS memory startup qualification — transient remains open

This is a verified startup checkpoint from `e3395dd` plus this change. The original
`general/mosmem.cir` still does **not** complete its requested transient. The
hourly campaign remains at one newly executing and agreeing original deck.

The bounded startup experiment found that the old symmetric two-thermal-voltage
diode limiter takes 101 corrections to establish the first Gmin stage of the
MOS memory topology. A strict Faer/Gmin operating-point request consequently
failed with 50 corrections per stage and completed with a 500-correction ceiling.
It took 201 actual linear solves, including final physical Gmin removal.

Applying a larger reverse-voltage bound unconditionally passed 78 focused Rust
tests but regressed that previously successful startup. That experiment was
rejected. The revised policy applies the reverse bound only while independent
voltage constraints are infeasible, using the existing feasibility phase. Once
they are feasible, conservative thermal limiting remains active. This revision
completes the strict startup with a 50-correction ceiling: nine corrections in
the first stage and 109 total linear solves. Its final state is exactly identical
to the prior 500-correction result, with final residual 3.3881317890172014e-21 and
mandatory unregularized Gmin removal. These are work counts, not a runtime speedup
claim. RSpice semiconductor limiting and pinned ngspice DEVpnjlim were inspected
as reference behavior; their complete device-local policies were not ported.

The final post-mutation phase-specific revision passed 25 library tests,
11 initial-Newton tests, eight MOS1 tests and 35 nonlinear tests, including the
new retained MOS startup fixture: 79 passes, no skips or failures. The new reverse
limiter property ran with 256 requested cases and seed `2026111107`.
Disabling the production feasibility gates was detected by two integration
regressions: initial-Newton recorded 10 passes/one failure and nonlinear recorded
34 passes/one failure. The original source was restored and its SHA-256 checked.
This is one deliberate manual mutant, not a complete cargo-mutants campaign.

The subsequent PSpice switch, sparse AC circuit, sparse dynamic and switch/PNP
stagnation regressions passed 20 more tests, including source and Gmin recovery
on both providers with eight-correction stages. The checkpoint therefore has
99 native passes and no skips across those named suites; it is not a complete
Rust workspace test or a long fuzz campaign. That broader run used property seed
`2026111108` and peaked at 835121152 bytes under its 850 MiB aggregate tree cap.

The unchanged ngspice transient completed 150 points. Comparing the selected
strict startup with its time-zero point gave nine of 13 mapped observables within
the existing bounds. Four voltages failed; the largest difference was
0.0024476710625420672 V at original node 5. All four independent-source currents
were within their existing bounds. The primary default-ngspice discrepancy is
retained. A separate tighter ngspice operating-point reference (RELTOL 1e-10,
ABSTOL 1e-15 A, VNTOL 1e-12 V, GMIN zero) agrees on all 13 startup observables:
maximum voltage difference 1.271593941254423e-12 V and current difference
2.4923097440010533e-17 A. This supports the strict physical startup solution but
cannot replace the original reference or establish full transient agreement.
An intermediate chat update prematurely claimed all 13 default comparisons
agreed; that statement was corrected after inspecting the complete result.

The original default source-continuation request still fails near source scale
0.4188232421875. Diagnostic Gmin transient requests on both LU and Faer reach a
later nonlinear transient line-search failure. No completed engine waveform
exists for comparison. Further work must identify the failing time/step and
trial-model behavior rather than count startup success as deck compatibility.

Raw evidence, old/new/phase engine snapshots, exact requests, guard logs, reference
RAW files and F# comparison source are under
`E:/Artifacts/Spice-hourly-mosmem-e3395dd`. The previous engine was preserved
before its incremental target was rebuilt. LLVM instrumentation is available
through the existing `Measure-RustIntegrationCoverage.ps1` helper. Coverage of
the rejected unscoped prototype measured 738/996 lines (74.10%) in newton.rs;
that source hash differs from the final phase-specific revision. Fresh coverage
of the final phase-specific source measured 740/1000 lines (74.00%) in newton.rs
through the 35-test nonlinear integration suite; other native test suites,
the executable main and a full-project denominator are outside that scope. Stable Rust
branch coverage is unavailable. cargo-nextest and cargo-llvm-cov commands are
absent; ordinary cargo test and the existing direct LLVM helper are used.

Memory guards enforced one aggregate process tree with 450 MiB additional
startup headroom. Earlier 750 MiB native and 512 MiB managed attempts were killed
on budget breaches, preserved as incomplete evidence. The admitted final
phase-specific native run used an 850 MiB cap and peaked at 845574144 bytes.
Earlier verification was deferred when available RAM fell below the guarded
admission threshold. The subsequent admitted post-mutation run passed all 79
tests and rebuilt the normal executable. The final coverage run peaked at
669863936 bytes under the same 850 MiB cap. Requirements were parsed with the
real StrictDoc tooling. The ignored hourly progress bookmark records remaining
transient work. No full original-deck success, GPU, Linux or production
qualification is claimed.
