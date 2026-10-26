# Continuation checkpoint 354

Campaign steps 11–20 complete the planned continuation allocation. Parent a31c1a0
plus the checkpoint patch; exact tested source/test hashes are in checkpoint-354.json
and continuation-354-rust-coverage.json. This is 20 of 100 implementation steps,
not completion of the overall campaign or its final review.

Changes reject overflowed tolerance scales, preserve terminal provider errors,
detect rounded source/Gmin stagnation, check the last permitted correction before
budget failure, and retain all rejected provider work. New physical oracles cover
36 signed antiparallel-diode and 36 parallel-aggregation/permutation solves, in
addition to the prior chain/bridge corpus. Recoverable fault fixtures verify both
singular and numerical retry paths; terminal fixtures verify five error classes
without retries. A one-correction DC regression failed before the budget fix
(352-red.log); both DC and BE now pass with exactly one provider invocation.

Full checkpoint: build, 266 Rust tests, fmt, Clippy warnings denied, managed build,
29 conformance checks and six xUnit cases passed. The focused nonlinear launcher
now includes library, continuation, hardening and coupled-network tests and passed.
An initial Clippy test-module placement failure was corrected before the full rerun.
All 12 StrictDoc documents parse; new REF4-017/018 are derived Draft contracts.

Linux instrumented tests passed and owned source/test SHA-256 values match this
checkpoint. LLVM exported 4,523/5,002 lines (90.42%) but warned that 62 functions
have mismatched data. The same warning was present in checkpoint 344. These are
provisional instrumentation summaries pending attribution of that warning, not
complete qualified coverage. Rust branch coverage and current Windows Rust
instrumentation remain unavailable. Managed coverage remains 141/151 mapped owned
lines, 52/66 mapped branches; Checkpoint.cs 40/41 lines, 21/30 branches.

Follow-up at checkpoint 364: llvm-mapping-354.json attributes all 62 records to
two duplicated zero-hash tracing_core dependency functions, with no owned-symbol
or unexpected mismatch. The warning remains part of the raw export evidence.

Cargo-mutants 27.1.0 is running a new limited_candidate campaign with all unit and
integration tests, one worker and a 20-minute bound. No result counts are claimed
at publication of this checkpoint. Raw run:
work/framework-mutations/20261026T223639Z-47d7e857. Its snapshot precedes the purely
organizational unit-module move required by Clippy; retain its exact source hash
when publishing the eventual results. Previous focused results remain 24 caught,
nine survivors of 33. Stryker was not rerun in this block.

The 1e-16 A bridge stage-budget counterexample remains open. No ngspice run,
general convergence, sparse nonlinear assembly, GPU or production qualification
is inferred. Native library internals and complete orchestration paths remain
outside coverage; helper execution was managed, not NativeAOT.

Reproduction uses Checkpoint.cs -- 354, scripts/Test-Nonlinear.ps1, and
scripts/Test-Coverage.ps1 with the pinned Linux toolchain as at checkpoint 344.
Raw logs/collector files remain in ignored build/qualification-campaign. The prior
WSL instrumentation patch was preserved on a named local coverage branch before
updating that native-filesystem checkout; no source changes were discarded.
