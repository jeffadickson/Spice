# Sparse complex refinement arithmetic checkpoint

Base `cd114d4`, November 19, 2026. One unit test was added; production
refinement equations, tolerances, factorization and feature configuration are
unchanged. Requirement: `FAER-COMPLEX-CORRECTION-001` (Draft).

The previous tests emphasized rejected factors and homogeneous constraints.
They did not independently check a successful, nonzero, coupled complex
correction. The new test expands `A=[[2,j],[0,3]]` by hand: known
`x=[1+2j,3-j]`, RHS `[3+7j,9-3j]`, initial `[.25+.5j,1-.25j]`, residual
`[2.25+5j,6-2.25j]`, correction `[.75+1.5j,2-.75j]`. Binary-quarter arithmetic
makes exact comparisons appropriate. The callback checks the original residual,
and the result and call count check additive correction. This tests the internal
factor boundary, not the accuracy of Faer's actual factorization.

Five original frozen survivors at `backend_faer.rs:163` and `:170` were replayed
with cargo-mutants 27.1.0: **5 caught, 0 missed, invalid or timed out**. Original
diffs match the current production expressions. The owned package was cleaned
in the separate replay target; the baseline freshly compiled the owned unit
executable and passed the focused test. The campaign target was never reused.

Normal verification: **52 unit and 693 integration tests passed, 7 ignored**;
environment seed `2026111911`. Instrumented complex-refinement tests: **9 passed**,
including the existing 64-case property with fixed seed `2026111308`.
Scoped `backend_faer.rs` line coverage is **201/483 (41.61%)**, explicitly
including its unit-test module. This is not whole-program or production-only
coverage. Branch coverage, vendor/native/GPU coverage and main are unmeasured.
No new standalone fuzz campaign, Stryker run, ngspice comparison or original
corpus-deck progress is claimed.

Intel routine verification passed **15 tests**, followed by the separate FP64
SpMV/finite/shape/corruption probe on Intel UHD Graphics 630, driver
31.0.101.2141, seed 18117. These are the bounded routine gates, not an
Intel-enabled mutation replay or general GPU/deck qualification.

The first coverage-driver attempt could not access a private real-library
validator. Its compile failure is retained separately. The successful second
attempt instruments the actual owned library unit executable with real vendor
dependencies, without a validator shim. A read-only simulated code review found
no important issue in the hand-expanded equations or scope.

The frozen campaign is historical: its original survivors are not rewritten as
caught by current-source replays. At the initial review, 83 Intel-backend
survivors shared an absent `intel-opencl` feature in the recorded native command.
These remain pending an enabled-profile mutation replay, not equivalent or
caught. Routine Intel checks alone do not close those candidates. Three Faer
compile-invalid cases are genuine Rust type errors (E0308/E0277), retained as
invalid; no artificial trait implementation or filter was added.

Raw logs, native outcomes, reviewed diffs, source/executable hashes, coverage
attempts and commands: `E:/Artifacts/Spice-faer-refinement-20261119T105614Z`.
Bounded evidence: `evidence/faer-refinement-arithmetic-results.json`.
