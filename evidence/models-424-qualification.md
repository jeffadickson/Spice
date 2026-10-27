# Bounded model checkpoint 424

Source bfdb4ab includes iterations 415–423. Checkpoint 424 passes the complete
build, formatting, Clippy with warnings as errors, 339 Rust tests, 29 managed
conformance checks and 64 xUnit cases on Windows; the same 64 cases pass on Linux.
Fourteen real StrictDoc documents parse. Sphinx 9.1 builds with warnings as errors
and projects 228 compiled XML documentation entries.

The explicit constant-charge diode composes existing Shockley conduction with
Q=C*V, dQ/dV=C and E=C*V²/2. Independent Jacobian, sign, units, malformed input,
previous-history overflow, physical audit and typed-client equations are tested.
Two new Proptest properties ran 4096 requested cases each with default seed
0x5350494345333535 during iteration 421; checkpoints run the full property suite.
No new minimized counterexample was produced by those executions.

The original named diode AC oracle passes six comparisons (three frequencies,
LU/faer), maximum component difference 4.628e-7 against a declared 1e-6 bound.
It exposed and fixed an importer defect: explicit AC magnitude had replaced DC
bias. DC values now remain independent and DC-only sources have zero AC drive.
The deck requires explicit M=0, zero RS/TT, and declared nominal temperature;
this evidence does not qualify depletion, transport or temperature-scaled IS.

Fresh Linux Rust coverage is 5290/5738 owned lines (92.19%); branch coverage is
unavailable. All 77 LLVM warnings are the same five dependency symbols attributed
at checkpoint 414 (74 tracing_core records, three tracing span records), no owned
or unclassified symbols. Source hashes match this Windows checkout exactly.
Coverlet measures 164/172 owned client lines and 109/112 branches; native/vendor,
generated code and file-app helper coverage are outside that managed metric.

Cargo-mutants 27.1.0 completed the src/charge.rs selection in 210.135 seconds:
14 candidates, ten caught, four unviable, zero survivors/timeouts/unattempted.
Unviable SimResult alias replacements are not kills. This is a primitive-scoped
selection, not whole-core mutation coverage. Stryker was not rerun here; earlier
client-only results and their survivors/compile errors remain historical.
Raw artifacts remain local. JSON reports retain commands, tool/source hashes and
scope. No full SPICE, GPU, global convergence or production qualification follows.
