# Newton voltage-feasibility phase correction — November 3

The unconditional physical-row merit prototype 20f5385 is not qualified: its
Rust suite passed 529/2 ignored, but the unchanged full managed native suite
reported 477 passed/2 failed/9 skipped. Both failures were PNP Gmin recovery
(lu/faer, eight iterations per stage). Original requests/responses remain under
the parent artifact root. This report supersedes that prototype's policy.

Actual diagnostic replay of its immutable a5eb... engine fails at the 4.768371582e-7 S
Gmin stage after 9.536743164e-7 S passed. Its voltage-defined branch constraints
are exactly feasible already. Physical scaling changed relative descent of
artificial-shunt/CCCS node equations unnecessarily, leaving a 7.75e-12 A residual
after the eight-correction budget. No budget, generator or acceptance adjustment
is justified. The retained exact PNP JSON fixture now tests this gap directly.

Corrected model-independent policy: apply frozen physical row merit only while
at least one voltage-defined branch constraint exceeds its existing strict
scaled tolerance. Once those linear constraints are feasible, use the original
strict scaled-residual descent. Current-driven/no-branch and feasible zero-source
systems therefore retain their existing descent path. DC and backward-Euler use
the same selector; final residual/update acceptance and Gmin removal are unchanged.
This avoids combining incompatible merit-decrease conditions with an OR rule.

`limited_candidate` applies one shared scale alpha*limit in [0,1] to the complete
MNA correction. For exact linear constraint corrections, the candidate constraint
residual is (1-scale)*old residual, so a feasible constraint remains feasible.
The implementation rechecks actual floating-point feasibility each iteration;
this algebra and finite tests are not a global nonlinear convergence proof.

Actual RED: new PNP Rust regression fails on 20f5385 for both lu/faer Gmin while
source recovery passes. Actual focused GREEN after the phase guard: 23 unit +
two analytical integration tests passed. This includes strict inclusive feasibility,
common-scale preservation, no-branch/current-driven/zero-source legacy selection,
PNP source and Gmin eight-iteration recovery on both providers, inverse Ebers–Moll
current/voltage, final artificial-shunt removal and impossible-supply negatives.
The original switch minimized case still converges in three corrections on both
providers with residual 4.336808689942018e-19. New wider coverage, mutation, native
oracle and full-regression outcomes must be recorded separately; historical
prototype results are not reassigned to this corrected source.

Artifacts: E:/Artifacts/Spice-fix-20261103T140559Z/rust-switch-replay/pnp-gmin.
`red-rust.log` and `green-phaseguard-e-temp.log` retain actual RED/GREEN. An initial
phase-guard build was interrupted before tests to redirect TMP/TEMP to E; it is
not counted as a test outcome. Subsequent compiler, test temp data and logs use
E, with existing exFAT incremental hard-link fallback warnings preserved.

Reproduce from an existing checkout, after setting an existing unique E artifact
root and CARGO_TARGET_DIR: `cargo test --locked --lib --test switch_stagnation_20261103 -- --nocapture`.
Source/test/fixture canonical Git-blob and tested-worktree hashes are recorded
separately in the adjacent provenance JSON. Requirements remain Draft.
