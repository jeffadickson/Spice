# Coupled-charge AC phase regression

The frozen 30a5a9a epoch exposed three survivors in public current reporting:
api.rs:420 negative sign, api.rs:420 multiplication, and api.rs:423 addition.
The previous control-only AC test supplied a real collector phasor, leaving the
real current contribution from imaginary control excitation unchallenged.

The new public API test uses the independent product rule
`dQ/dVc = TF*IS*expm1(VBE/VT)/VA = k` and `I = j*omega*k*Vc`.
It checks both complex components and terminal KCL with LU and Faer, three
phasors and two frequencies, without weakening existing bounds. Production
physics and reporting code were already correct; this checkpoint adds tests.

All three corresponding native cargo-mutants 27.1.0 replays were caught on
later development source. The frozen campaign's raw outcomes remain historical
and unchanged. Replay source base: 0a911a46dfe8bc9af82b4a07f680f34634c2062b
plus the recorded test hash. Profile mutants, default features, integration
target bjt_coupled_charge, two build/test workers, seed 2026111901.

Focused AC suite: 10 passed. Aggregate: 51 unit and 658 integration passed,
six explicitly ignored. Existing product-rule property tests ran; no new
standalone fuzz engine, Stryker campaign or ngspice deck comparison ran here.
The reference is the analytical charge derivative, not another implementation.
Scoped integration coverage: api.rs 276/393 lines. Stable Rust branch coverage
is unavailable. This scope differs from the earlier API-admission suite and
must not be read as a comparable overall-coverage increase.

Raw evidence: E:/Artifacts/Spice-epoch-triage-20261119T004906Z.
Native replay: E:/Artifacts/Spice-issue61-20261117T133906Z/current-audit-lean/20261119T005020Z-8ada84cf.
Actual Intel routine verification: 15 tests passed with provider SHA256
4353e81a729f56f106212c8799b23af3e74a6b1d20ab21a00c335bcfb870e955;
the separate Intel SpMV probe also passed. These bounded checks do not extend
the AC fixture's CPU-provider qualification to arbitrary GPU decks.

Reproduce from the checkout after checking the existing E: artifact parent and
choosing isolated normal and mutation targets:

```powershell
cargo test --profile mutants --test bjt_coupled_charge
cargo test --profile mutants --lib --test mutation_suite --features mutation-suite
./scripts/Measure-RustIntegrationCoverage.ps1 -Repository $checkout -TargetDirectory $normalTarget -Output $freshCoverage -Test bjt_coupled_charge
./scripts/Test-IntelCircuits.ps1 -Provider $trustedProvider -ArtifactRoot $runRoot -TargetDirectory $normalTarget
./scripts/Run-ParallelProbe.ps1 -Probe Intel -ArtifactRoot $runRoot
```

Set TEMP/TMP, CODEX_ARTIFACT_ROOT and CARGO_TARGET_DIR explicitly to the chosen
existing E: directories before running. Keep native mutant target/cache separate.
The publication metadata suite adds all seven invalid combinations of complete,
status and schema, asserting InvalidInput before any root is created. Its six
tests pass and the artifact.rs:61 OR-to-AND mutant is caught in native replay
20261119T010920Z-a9a04d67 under the same mutation cache parent. Scoped artifact.rs
line coverage is 90/99; branch coverage is unavailable. The existing valid
roundtrip remains the positive control. This is test hardening, not changed
publication behavior. Read-only review found no important correctness issues.

No new original deck, full Gummel-Poon, universal GPU or performance claim.
Other arriving survivors remain pending. The previously documented flush
replacement must remain a reviewed target-specific equivalence, not a kill.
