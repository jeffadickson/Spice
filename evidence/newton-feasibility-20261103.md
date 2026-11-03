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
strict scaled-residual descent. Systems without voltage-defined branch rows, and systems with already-feasible
branch constraints, retain their existing descent path regardless of device family. DC and backward-Euler use
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

Fresh normal dev-profile production build from 03b9554d083556e52ff98e3e70f77cd72d48d984
completed exit 0 and was copied immutably to the raw root's
native-feasibility/circuit-simulator.exe. SHA256:
42835c20d6ee316cb88b656849e0f5a6b18272d89fedc2ca62420c7a206caaab.
This is the corrected feasibility policy, distinct from the failed a5eb prototype.
Actual build/source/log hashes are retained in adjacent native JSON. No wider
qualification is inferred merely from producing the binary.

Final-source reproduction (PowerShell in a separate clean existing checkout;
filters refer to the 03b9554 Rust source content):

```powershell
$repo=(Get-Location).Path
$artifact=$env:SPICE_NEWTON_REPLAY_ROOT
if(-not(Test-Path -LiteralPath $repo -PathType Container)){throw 'Missing checkout'}
if(-not(Test-Path -LiteralPath $artifact -PathType Container)){throw 'Set existing unique E artifact root'}
$temp=Join-Path $artifact 'temp'
if(-not(Test-Path -LiteralPath $temp -PathType Container)){New-Item -ItemType Directory -Path $temp|Out-Null}
$env:TMP=$temp;$env:TEMP=$temp
$env:CARGO_TARGET_DIR=Join-Path $artifact 'target';$env:CARGO_BUILD_JOBS='2'
$env:PROPTEST_RNG_SEED='3401923026981217896';$env:PROPTEST_CASES='512'
cargo test --locked --lib --test property_campaign --test switch_stagnation_20261103
Remove-Item Env:PROPTEST_RNG_SEED;Remove-Item Env:PROPTEST_CASES
if(-not(Test-Path -LiteralPath $env:SPICE_CARGO_MUTANTS -PathType Leaf)){throw 'Set pinned cargo-mutants path'}
& $env:SPICE_CARGO_MUTANTS mutants --dir $repo --no-config --in-place `
 --timeout 180 --build-timeout 240 -C '--lib' -C '--test' -C switch_stagnation_20261103 `
 -f src/newton.rs -F '(src/newton.rs:372:12: replace > with ==|src/newton.rs:430:33: replace / with \*|src/newton.rs:830:32: replace < with >)' `
 --output (Join-Path $artifact 'mutation-final')
./scripts/Measure-RustIntegrationCoverage.ps1 -Repository $repo -TargetDirectory $env:CARGO_TARGET_DIR `
 -Output (Join-Path $artifact 'coverage-final') -Test switch_stagnation_20261103
cargo test --locked
```

Fresh corrected-source results (03b9554 Rust bytes): pending seed
3401923026981217896 with 512 cases in each of 18 properties passed, alongside
23 unit and two analytical tests (43 target tests, plus retained replay cases).
Final cargo-mutants 27.1.0 selected three operators: feasibility > to ==,
merit / to *, and DC progress < to >. All three compiled and were caught;
zero missed, timed out, unviable or unattempted. Post-mutation source restoration
was checked byte-for-byte against the canonical 03b9554 newton.rs SHA256.

Fresh LLVM integration coverage passed both analytical tests. Owned newton.rs:
530/898 mapped lines (59.02%). Stable instrumentation provides no measured
branch maps. This coverage excludes unit/test/vendor source and unrelated
integration targets; the earlier one-test prototype percentage is a different
scope and is not compared as an improvement. Per-file source hashes and metrics
are retained in adjacent coverage JSON.

The unchanged managed BjtContinuationNativeTests also passed 4/4, zero skipped,
on the corrected engine with prerequisite enabled. The root-orchestrated actual
log is E:/Artifacts/Spice-fix-20261103T140559Z/pnp-final-native-enabled.log,
SHA256 4e4be0e20892b15224306e162381514fc095dca2961f59711cd2702da07e30fb.
A separate direct replay of all eight original positive NPN/PNP source/Gmin
requests on both providers also passed; adjacent native-replay JSON retains the
exact original request hashes and analytical discrepancies. These scopes are
reported separately rather than counted as additional managed test rows.

The duplicate EOF blank line in the draft requirement was removed after the
focused run. Its original observed input hash remains in provenance history;
a fresh StrictDoc parse of the final documentation passed 47 documents,
399 unique IDs and 374 resolved parents. Numerical source/test bytes did not change.

Final full corrected-source Rust regression completed exit 0: 532 passed,
zero failed, two explicitly ignored across 64 target result lines (including
zero doctests). Source is 03b9554's Rust bytes, source SHA256 38d6604ce5fa694e805f4cfe6a05a156f4b2db1a8101f1c897d6af770f8a04e0.
Full raw-log SHA256: ccd335b9f10e0f452e74c41d4a40f42aad2444ae12951fbd5744caeb225a6685.
The ignored tests are separately launched endurance and external-ngspice oracle
fixtures, not passes. Adjacent regression JSON retains command/environment/counts.

Clippy 0.1.99 with warnings denied passed the changed library + analytical test
scope (exit 0). The broader all-target attempt failed one unchanged baseline
single_element_loop at tests/spice_diode.rs:114; its file bytes exactly match
e679def. No allowance or unrelated source fix was introduced, and all-target
Clippy success is not claimed. Both actual command/log hashes and scope are
retained in adjacent Clippy JSON.
