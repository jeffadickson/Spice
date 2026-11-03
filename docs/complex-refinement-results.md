# Sparse complex correction: measured scope

The frozen 12x12 NPN small-signal matrix reproduced a genuine Faer numerical
failure independently of the newer semiconductor IR. At 10 MHz, the reverse-BC
sense-current row had residual approximately `-5.43e-21 + 2.34e-20j A` against a
componentwise scale of `3.20e-20 A`. Faer lost relative accuracy in this tiny
current beside larger currents. The LU reference passed the original checker.
The fixture retains the Faer-selected DC bias, frequency and request SHA256;
it does not independently qualify the source transistor model.

Faer now reuses its original sparse complex LU factor for at most three
same-precision residual corrections. Candidates must be finite and nonworsening.
The independent `1e-10` componentwise acceptance threshold is unchanged. There
is no second factorization, dense/provider fallback or altered model equation.
Uncorrectable outcomes remain explicit failures.

The existing AC verification body now belongs to the canonical solver contract.
`complex_backward_error` returns a finite score, which can exceed the acceptance
limit; it is not an acceptance verdict. `verify_complex_solution` applies the
original threshold, and `ac::verify_complex_system` preserves its public
signature as a forwarding entry. Wrong-shape/nonfinite RHS and solutions retain
validation; the original residual failure class/message is retained.

## Verification

The frozen matrix failed before the implementation and passed afterward.
Eighteen library tests and three new complex tests passed, including wrong-factor,
malformed/nonfinite correction and callback-error countertests. The new genuine
complex-RHS property test ran a configured 128 Proptest cases with seed
`57120261105`, checked against independent LU linearity and the strict residual.
Another 50 focused numerical regressions passed across controlled-source AC,
matrix conformance, sparse AC/transient and factor-identity/failure contracts.
These are 71 focused native tests, zero failures or skips, not a full native-suite
or complete semiconductor qualification run. The parent owns original NPN/PNP
ngspice comparisons and full merged regressions.

Nightly branch instrumentation passed 18 library and three integration tests.
The production sparse-correction instantiation measured 28/32 lines and 3/4 LLVM
conditional branches. The canonical score measured 22/26 and 2/4; its acceptance
wrapper measured 14/14 and 2/2. Separate unit callback instantiations exercise
error paths; their metrics are not summed into production-instantiation coverage.
Some overflow/guard paths remain unmeasured in this instrumented scope. LLVM
conditional-branch coverage is not exhaustive pattern-match or MCDC coverage.

Factor corrections are internal to one logical AC point solve. Existing logical
analysis counts do not enumerate each correction. The same-precision budget can
stagnate or fail; no global convergence, conditioning, performance or model-parity
claim is made. There is no new standalone fuzz campaign; fixed negatives and the
bounded library property campaign are the added input checks.

Artifacts are under `E:/Artifacts/spice/behavior-xspice-nov2`. Exact source hashes,
fixture provenance, compiler versions, scopes and raw reports are in
`evidence/complex-refinement-results.json`. Production remains pinned to Rust
1.99.1. Coverage used the existing Rust 1.101.0-nightly tool only, in a separate
target/profile. Earlier real-LU coverage objects were archived before target
reuse; coverage profiles are selected per binary rather than merging unrelated
build-script profiles or different crate instantiations.

## Reproduction

```powershell
$repo = Join-Path $env:USERPROFILE 'Documents/Codex/2026-10-24/how/outputs/rust-circuit-simulator/work/behavior-xspice'
if (!(Test-Path -LiteralPath $repo -PathType Container)) { throw 'Missing checkout' }
Set-Location -LiteralPath $repo
$artifacts = if ($env:CODEX_ARTIFACT_ROOT) { $env:CODEX_ARTIFACT_ROOT } else { 'E:\Artifacts' }
if (!(Test-Path -LiteralPath $artifacts -PathType Container)) { throw 'Missing artifact root' }
$target = Join-Path $artifacts 'spice/behavior-xspice-nov2/native-target'
if (!(Test-Path -LiteralPath (Split-Path $target -Parent) -PathType Container)) { throw 'Missing project artifact parent' }
if (!(Test-Path -LiteralPath $target -PathType Container)) { New-Item -ItemType Directory -Path $target | Out-Null }
$env:CARGO_TARGET_DIR = $target
$env:CARGO_INCREMENTAL = '0'
cargo test --locked --jobs 4 --lib --test faer_complex_refinement
```

The bounded cargo-mutants campaign caught both selected arithmetic defects: complex residual subtraction changed to addition, and correction addition changed to subtraction. No selected mutant survived, timed out or was unviable. Its baseline was explicitly skipped after the separately recorded 71-test focused baseline; no whole-helper mutation score is claimed. StrictDoc validation passed 25 documents, 347 unique IDs and 336 resolved parents.
