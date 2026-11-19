# Independent audit admission and MOS1 charge checks

Development base: af30897036f173920b35f8c5dc91b630b060ac98. Production code is
unchanged. Frozen epoch 30a5a9a remains separate and its raw outcomes are not
rewritten by later test evidence.

The first three survivors exposed missing independent negative cases:

- Nonfinite assertion values need Indeterminate, rather than finite failure.
- Audit discrepancy and tolerance must each be valid; infinity is not a usable bound.
- Artifact length and digest must each match. Same-length valid JSON tampering
  isolates the digest; changing only the marker byte count isolates length.

Their three selected native counterparts are caught by the new tests. Exact
stage: current-audit-lean/20261119T015543Z-8ad01d17 under the artifact parent
E:/Artifacts/Spice-issue61-20261117T133906Z. Profile mutants, default features,
targets consistency,analyses,storage_power; two compiler/test workers, one
mutant at a time. Normal regression seed: 2026111902.

A second shared gap affects MOS1 transient KCL. A manufactured deep-accumulation
state has Cgb=3 nF and Cgs=Cgd=0, with zero channel conduction. An independent
gate step gives I=Cox*deltaV/h. Supplying source currents must balance the gate
and bulk, and the relative KCL bound scales with their absolute current sum.
Three voltage steps (0.25, 1 and 2 V), common-mode offsets and timesteps challenge voltage differences,
charge signs, timestep scaling and tolerance scaling. Perturbing the supplying
gate current must fail. Expected values never call the production Meyer routine.
The first 24 native MOS replays caught 23, leaving the multiply-versus-divide
voltage-step mutation at audit.rs:294:83: a unit step made both expressions
identical. The non-unit steps were added afterward and that exact name was
replayed separately: it was caught in native run 20261119T021327Z-7178cd98.
Thus 27 distinct counterparts were caught across these three stages; the first
24-case run's 23-caught/one-missed outcome remains in the evidence. No timeouts
or compile-invalid cases occurred in these targeted replays.

Full baseline: 51 unit plus 662 integration passed, six explicitly ignored.
Focused analyses 11, consistency 15 and storage_power 7 passed. Existing
property tests ran; there is no new standalone fuzz-engine or Stryker campaign.
Intel routine checks and the separate SpMV probe passed; this is not arbitrary
GPU-deck qualification or an independent ngspice comparison.

Scoped line coverage: audit.rs 427/579 (consistency), assertion.rs 10/11
(analyses), artifact.rs 90/99 (storage_power). Stable Rust branch coverage is
unavailable. These are distinct named suites, not a combined overall score.
Initial direct-rustc coverage compilation lacked CARGO_BIN_EXE_circuit-simulator.
The retained failed attempt is unavailable evidence. The successful retry used
the recorded unmutated CLI from the separate normal cache (Cargo profile mutants);
child CLI code is uninstrumented and not
included in the library coverage. Coverage helper seed is 88320261103.
The later non-unit-step rerun also required setting that variable in its new
PowerShell process; both failed attempts are retained, not counted as coverage.

Reproduction uses supported per-tool E: output settings. Check every existing
directory and use a fresh coverage leaf:

```powershell
foreach ($directory in @($checkout, $runRoot, $normalTarget)) {
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { throw "Missing $directory" }
}
if (!(Test-Path -LiteralPath $cliBinary -PathType Leaf)) { throw 'Missing verified normal CLI' }
$env:TEMP = Join-Path $runRoot 'temp'
if (!(Test-Path -LiteralPath $env:TEMP -PathType Container)) { throw 'Missing E: scratch directory' }
$env:TMP = $env:TEMP
$env:CODEX_ARTIFACT_ROOT = $runRoot
[Environment]::SetEnvironmentVariable('CARGO_BIN_EXE_circuit-simulator', $cliBinary, 'Process')
./scripts/Measure-RustIntegrationCoverage.ps1 -Repository $checkout -TargetDirectory $normalTarget -Output $freshCoverage -Test consistency
```

The CLI binary hash is recorded in the JSON; it must not come from the mutant
target. Keep mutation and normal targets isolated. Full baseline invocation is
`cargo test --profile mutants --lib --test mutation_suite --features mutation-suite`
with CARGO_TARGET_DIR pointed at the normal qualification cache on E:.

The File::flush replacement remains a reviewed target-specific equivalence,
not a killed mutant. The four-case standard-library flush probe was rerun on
the recorded compiler/platform; separate sync_all calls remain intact. Finite
visibility observations are not power-loss durability proof.

Raw artifacts: E:/Artifacts/Spice-epoch-triage-20261119T015337Z. StrictDoc 0.31.0
parsed 111 requirement documents. Read-only source review found no important
issues. Native BSIM audit paths, other MOS regions and newly arriving survivors
remain separate work; do not claim full model or SPICE compatibility.
