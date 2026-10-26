# Continuation qualification checkpoint 310

Scope: REF4-013, extending REF4-008 and REF4-012. This is bounded CPU
continuation evidence, not a full M1, production, general SPICE, or GPU qualification.

## Independent circuit evidence

`tests/continuation.rs` uses a current source feeding one Shockley diode, with
Is = 1e-12 A and Vt = 0.02585 V. At I = 1e-6 A and a zero initial state,
direct Newton fails under the eight-iteration policy. Source continuation and
Gmin continuation recover across nalgebra dense LU, nalgebra dense QR and faer
sparse LU. Source stepping must reject at least one stage before recovering.
These providers still receive bounded dense Newton Jacobians; this does not
qualify direct sparse nonlinear assembly or sparse AC/transient.

The independent oracle is V = Vt * ln(1 + I/Is), checked within 2e-7 V,
and physical KCL Is * expm1(V/Vt) - I, checked within 2e-12 A. The final
Gmin warm-start shunt is 1e-6 S, large enough that retaining it violates the
physical oracle. The test also checks descending recorded conductances,
increasing source scales, final nominal excitation, and bounded stage counts.

A reverse current of -1e-6 A exceeds the diode's reverse saturation current.
A fixed 1e-3 S shunt permits a regularized solution, but Gmin continuation
must return a numerical failure when its final physical solve cannot converge.
Separate tests require source/Gmin stage-budget exhaustion to return failure.
Malformed policies include nonfinite/nonpositive conductances, invalid reduction
factors and out-of-range stage budgets.

The deterministic property corpus uses LCG seed `0x434f4e54494e5545`, multiplier
6364136223846793005, increment 1, and the high 53 bits to sample 64 log-uniform
currents in [1e-7, 1e-5] A. Both policies (128 solves) must satisfy the same
independent voltage and physical KCL bounds. No failing seed was observed.
This small topology and parameter domain do not establish universal convergence.

## Reproduction and verification

On this host, set CARGO_HOME and RUSTUP_HOME to the existing sibling
`../../work/rust-runtime/cargo` and `../../work/rust-runtime/rustup` directories,
and prepend the cargo bin directory and `C:/dev/tools/msys64/ucrt64/bin` to PATH.
The existing `stable-x86_64-pc-windows-gnu` toolchain reports rustc 1.99.1
(58cf8f9c3, LLVM 23.1.1), matching the repository's pinned compiler version.

- `cargo test --locked --test continuation`: five tests passed.
- `dotnet run --file scripts/Checkpoint.cs -- 310`: 229 Rust tests and 26 managed
  checks passed; build, formatting and Clippy with warnings denied passed.
  Full-suite runs include existing malformed-input, fuzz-smoke and seeded properties.
- `cargo-nextest` is unavailable; the checkpoint used cargo test, including doctests.
- StrictDoc 0.30.4 SDReader parsed all 11 docs/*.sdoc files; 194 unique requirement IDs and 174 resolved parent references were checked. HTML export could not
  start because its environment lacks html2pdf4doc; no HTML export success is claimed.
- `scripts/Test-Coverage.ps1` was rerun: the nightly MSVC coverage toolchain has
  profiler_builtins but cannot link because link.exe is missing. Rust line and branch
  metrics, including changed-test coverage, remain unavailable. No current managed
  coverage was collected; historical managed percentages do not measure these tests.
- All 18 selected mutants were killed (zero surviving, invalid or timed-out mutants in the completed run); results are retained in evidence/mutation.json. The campaign copies
  source/tests into an ignored sandbox and restores it; working product source is
  unchanged. Added operators retain the final Gmin shunt or bypass the stage-budget
  guard. The pre-existing stamp-sign operator was repaired to match add_stamp after
  the sparse-assembly refactor; its initial missing-target run was not counted.

No coverage-guided fuzz campaign, new external ngspice execution, performance
benchmark or hardware GPU check was run for this checkpoint. Direct sparse
AC/transient, broader difficult topologies, additional named ngspice oracles and
bounded model-contract expansion remain active qualification work.
