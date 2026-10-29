# October 29 survivor follow-up

The overnight source was clean `e4c7b8f5b8fe6ddc6fc1384a154ea32e78df473c`, shard 3/16. Its 24 Rust survivors have stable identities in `overnight-20261029-survivors.json`. Different nightly shards are different populations; totals are not trend comparisons.

This follow-up adds 15 integration tests and two private unit tests, with inferred Draft requirement QUAL-EDGE-002. Tests challenge actual artifact read/write caps, independent marker fields, audit bounds and ranking, refinement scaling, expression depth/domain/derivatives, dense and exact-boundary sparse residuals, parser and scalar limits, waveform domains, prepared factor identity, Newton current units/reporting, and source-continuation recovery. The final-stage injection requires a retry at scale 0.875 after failure at full excitation. Production numerical behavior was unchanged; the pre-existing coverage-tool fix from origin/main was merged locally.

## Replay evidence

**Original cohort: 22 caught, one masked defensive guard, one conditional equivalent.** R29-SURV-019 loses an early invalid-ID rejection but still rejects invalid IDs through downstream validation on bounded valid circuits. Error text and clone work can differ; this is not full semantic equivalence and remains a defensive-validation gap. R29-SURV-024 changes a positive-rise guard to nonnegative: at zero rise the preceding phase<rise condition is false for the validated nonnegative phase. Neither is counted as caught. Full reasoning and reopening conditions are retained per identity.

The first GNU Windows replay selected 26 candidates: 14 caught, 12 survived, no timeout/unviable/unattempted candidates. Two are supplemental importer mutations, not members of the 24-survivor cohort. The later strengthened replay uses pinned MSVC Windows Rust. Both compilers report Rust 1.99.1, compiler commit 58cf8f9c3; their separate binaries and targets are recorded in `overnight-20261029-windows-compilers.json`. Do not combine raw pass totals as distinct mutations or describe the two executions as identical environments.

The strengthened replay completed 18 selected candidates: **16 caught, two survived, zero timeout/unviable/unattempted**. Its scope overlaps the first run and includes both importer candidates, which are caught separately. Across the two scopes, 22 original cohort identities have actual test-detected kills. `overnight-20261029-replay-summary.json` retains exact counts, elapsed times and report hashes. Source/test checkpoint: `3e6d8f0`; raw runs: `work/framework-mutations/20261029T130526Z-c5345b4e` and `work/framework-mutations/20261029T132214Z-cde048e3`.

The JSON inventory retains each actual replay, native outcome/checksum, log, failing test names where available, and tested source hash. First and final input inventories retain the different test snapshots. The final replay's 79 owned Rust source/test files match the final source hash list and Linux coverage overlay. All original production lines were preserved; unit modules were appended. No ambiguous relocation was guessed: original line prefixes were checked, and the framework independently verified the exact native name inventory.

The initial 24-name attempt failed closed when cargo-mutants 27.1.0 also selected two importer field-deletion candidates; no mutants executed in that attempt. A concurrent replay attempt was rejected by the exclusive Rust lock before creating a run. The strengthened replay subsequently waited for the first to complete. Neither event is a simulator failure. Raw selection bytes are retained as `selection-input.json` in the ignored run directories; Git newline normalization can change the version-controlled file's byte checksum without changing its names.

Reproduce the whole selected cohort from the repository root using the existing host/tool setup:

```powershell
dotnet run --file scripts/FrameworkMutations.cs -- --tool rust --minutes 35 --workers 1 --rust-test lib,overnight_survivors_20261029,ngspice_compat --rust-names-file evidence/overnight-20261029-selected-names.json
```

## Verification scope

Windows and native Linux executions each passed **396 tests, zero failed, one intentionally ignored**. The fixed Proptest campaign used 16 properties, 1,024 requested cases each, seed `202610290601`: 16,384 requested cases per platform, not additive distinct inputs across repeated or platform executions. Formatting and all-target Clippy warnings-as-errors passed. Existing malformed-input and bounded fuzz-smoke tests ran within the regression suite; no new coverage-guided fuzz campaign or five-hour run is claimed. cargo-nextest was unavailable; cargo test was used.

Linux exported coverage is **5,475/5,865 lines (93.35%)**, 514/556 functions and 8,286/8,866 regions. Scope is owned src files, including their cfg(test) modules; integration-test/dependency files are excluded. This is not production-only coverage. Branch coverage is unavailable. LLVM reports **83 mapping mismatches**; exported totals are not qualified complete source mapping and do not resolve the prior adaptive-module gap. The Linux checkout has a historical Git HEAD; the verified 79 source hashes identify the actual overlay. There is no new Windows instrumented, managed, PowerShell, GPU, vendor-internal or plugin coverage claim. The upstream prerequisite fixture renames tools and was not rerun against active shared toolchains.

StrictDoc and Sphinx results are retained separately. Requirements remain Draft; finite scenarios, source review and selected mutations do not close general M1 qualification or establish universal SPICE compatibility, convergence or production readiness.

## Managed outcomes and remaining work

The overnight Stryker report still has 88 killed, one survived, one timeout, 103 compile errors and seven ignored. Client and transport-test source hashes match the previous review. Mutant 194 is the conditional decode-only BOM-emission-flag equivalent. Mutant 182 remains a **full-suite Timeout**; the previous matching-source focused Killed result is historical evidence, not a fresh full-suite kill. Stryker's definite-assignment/SafeMode rollback region remains unavailable mutation coverage. No managed source was changed or new managed execution claimed in this follow-up.

Reopen source-reviewed dispositions when source, validation preconditions or callers change. Preserve the full-suite EOF timeout, Stryker rollback and LLVM mapping gaps as concrete follow-up work. A fresh overnight run of this new test checkpoint is still pending. Raw reports/logs remain local; CI was not dispatched and source was not pushed.
