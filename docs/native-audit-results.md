# Native terminal audit qualification

Verified on base7550704 plus the hashed test/requirement changes: all14 targeted
mutants caught in an eight-minute native run, including13 raw frozen-epoch
survivor counterparts and one additional charge-subtraction mutation. No missed,
invalid or timed-out mutants in the successful stage. Earlier launch failures
remain separate. Production source and the frozen4411 campaign were unchanged.
Ordinary regression:51 unit and672 integration passed, seven explicitly ignored.
The two existing native bulk tests and this new native test passed separately.
StrictDoc0.31.0 parsed114 documents/629 unique IDs. Intel15 routine tests and the
separate UHD630 FP64 SpMV probe passed. Review's source-power gap was fixed and
rechecked. Two newer charge-boundary survivors remain pending, not equivalent.

The native audit lane explicitly executes an ignored test; ordinary `cargo test`
does not establish native coverage. Requirements: `AUDIT-NATIVE-TERMINALS-001`
and `AUDIT-NATIVE-ENERGY-QUAL-001` in `native-audit-contracts.sdoc`.

`native_audit_contracts` manufactures clamping-source states from real D/G/S/B
currents and charges, then checks audit wiring independently of the assembled
solver matrix. Each node's tolerance has a separately specified physical scale.
Two backward-Euler steps and four deliberately corrupted terminal currents
challenge displacement signs, timestep division and terminal aggregation.
A common-mode translation makes source power nonzero. Ideal gate and near-zero
bulk conduction power remain a limit. Nonlinear stored energy stays NotChecked.
This is integration qualification, not an independent proof of model equations.

The existing two ignored bulk tests in `tests/bsim330.rs` additionally execute
ngspice-derived DC and charge goldens, derivatives, history rejection and 64
randomized conservation cases with fixed seed330. SOI dynamic charge is outside
this checkpoint. No new original corpus deck is claimed.

## Provider provenance

Raw root: `E:/Artifacts/Spice-native-audit-20261119T050702Z`.
`provider-provenance.json` binds the tracked adapter, model, compiler, headers
and test hashes. The current `scripts/Bsim330Host.c` was built using installed
Visual Studio2022 Build Tools with `/W4 /O2 /LD`, against existing ngspice OSDI
headers. Bulk output and scratch remained in the raw root on E:.

Exact model source was recovered from the primary VACASK repository:
<https://raw.githubusercontent.com/robtaylor/VACASK/main/devices/spice/bsim3v3.va>.
Its SHA256 is `14e9d752a6db896959f3c97a32112899365e7528221e308250cee9cbc8141f84`.
The source credits Arpad Buermen2025, AGPL-3.0-or-later, generated from ngspice.
Source and generated binary remain local; this checkpoint redistributes neither.

OpenVAF-reloaded20260616-3-g0e83f1ed came from the primary download index:
<https://fides.fe.uni-lj.si/openvaf/download/>. Archive filename:
`openvaf-reloaded-20260616-3-g0e83f1ed-win_x64.zip`, SHA256
`7ebe8923faa0f1e70d462b958a91d0d681a06c2d63b4a07af0a8e16f45453f69`.
The compiler executable SHA256 is
`5ad1c62f8effe61cd613a14995d1ec22ade97361ac29d25c1c22f0239435a655`.
The newly generated module SHA256 is
`11d9e3df00edf728db2fc531efc030685d6cc70098a8550e3f13d1513238fd17`.
It differs from the historical module; identical source/compiler version does
not establish byte-identical builds. Actual independent goldens were rerun.

## Reproduction and isolation

Use the repository's installed pinned tools and existing qualified provider.
Check directories before use. The normal target must differ from every mutant
target. With `$repo`, `$run` and `$target` set to existing validated directories:

```powershell
foreach ($path in @($repo,$run,$target)) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "Missing $path" }
}
$env:TEMP = Join-Path $run 'temp'
if (-not (Test-Path -LiteralPath $env:TEMP -PathType Container)) {
    New-Item -ItemType Directory -Path $env:TEMP | Out-Null
}
$env:TMP = $env:TEMP
$env:SPICE_BSIM330_HOST = Join-Path $run 'host.dll'
$env:SPICE_BSIM330_MODEL = Join-Path $run 'bsim3v3.osdi'
foreach ($path in @($env:SPICE_BSIM330_HOST,$env:SPICE_BSIM330_MODEL)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing $path" }
}
$env:CARGO_TARGET_DIR = $target
Push-Location -LiteralPath $repo
try {
    cargo test --profile mutants --test bsim330 bsim330_ -- --ignored --test-threads=1
    if ($LASTEXITCODE) { throw 'Provider baseline failed' }
    cargo test --profile mutants --test native_audit_contracts -- --ignored --test-threads=1
    if ($LASTEXITCODE) { throw 'Native audit failed' }
} finally { Pop-Location }
```

For targeted cargo-mutants27.1, use `--gitignore true` to exclude held scratch
locks and an isolated mutant target. `--cargo-arg=--test` plus
`--cargo-arg=native_audit_contracts` restrict compilation. The trailing arguments
are `-- -- --ignored --test-threads=1`: one separator belongs to cargo-mutants,
the next to Cargo. Check the unmutated baseline actually executed the fixture.
The initial snapshot-lock failure, an argument-validation error requiring an
explicit Git-ignore Boolean, and a missing-separator baseline failure were
retained, not counted as mutation outcomes. No frozen epoch helper,
configuration or source was changed to add this explicit lane.

Coverage's ordinary no-argument execution correctly ignores this fixture. The
instrumented binary was explicitly rerun with `--ignored --test-threads=1`, then
profiles were merged/exported with matching LLVM tools. Use `coverage-final/`
`native-summary.json` and `native-test.log`, not its initial ignored test log.
Owned audit scope:278/579 lines. Stable Rust branch coverage, external native
model/vendor code, test source and executable main are unmeasured. This scope
cannot be compared as an overall trend with other named integration suites.

Existing randomized properties ran; no fresh standalone fuzz engine, Stryker,
SOI lane, Linux execution, full BSIM qualification or speedup is claimed.
See `evidence/native-audit-results.json` for final targeted outcomes and hashes.
