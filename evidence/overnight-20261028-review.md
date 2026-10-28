# October 28 survivor review

The original overnight run tested clean source `7ce9bb3062e5f75560cab2d3dde91c2909af6f6f`, shard 12/16. This follow-up adds twelve Rust tests and one managed liveness test; it does not claim another completed overnight campaign or broader SPICE qualification.

## Dispositions

Of the 22 original Rust survivors, **20 are caught** by the native cargo-mutants replay. Two remain conditional equivalents, with source-specific explanations and reopening conditions in `overnight-20261028-survivors.json`: the interpolation lower-bracket equality is handled earlier, and the private correction wrapper's name is unobserved by its callers. No assertion was added merely to force a private implementation detail to match itself.

The selected native replay attempted 24 candidates: 22 caught, two missed, no timeout, unviable or unattempted candidates. The two additional importer candidates are separate from the original cohort and were caught by existing tests. Source hashes and native names are retained alongside the dispositions. Reproduce with:

```powershell
dotnet run --file scripts/FrameworkMutations.cs -- --tool rust --minutes 35 --workers 1 --rust-test lib,overnight_survivors_20261028,ngspice_compat --rust-names-file evidence/overnight-20261028-selected-names.json
```

Stryker.NET's final full run generated 200 candidates: 88 killed, one survived, one timed out, 103 compile errors and seven ignored. The EOF-loop mutant (182) is **Killed in the focused liveness test**, but remains **Timeout in the full run**. The independent two-second test deadline makes the focused check terminate. Full-suite execution/order remains unresolved; timeout is not relabelled as a kill. The BOM decode-only survivor (194) remains conditionally equivalent under the current reader contract. See `overnight-20261028-managed-replay.json`.

The focused run used Stryker's native character-span mutation selection and `FullyQualifiedName~SmallSuccessfulEnvelopeCompletesBeforeItsOwnDeadline`; its other survivors/no-coverage results reflect that deliberately narrow test scope. Full and focused counts must not be combined. Stryker SafeMode rolled back the RunJsonAsync region after CS0165 definite-assignment errors involving `versionValue` and `completion`: the 103 compile-error candidates are unavailable mutation evidence, not necessarily intrinsically invalid mutations. Native configuration semantics: https://stryker-mutator.io/docs/stryker-net/configuration/ .

## Verification and limits

Windows and Linux regression executions each passed 379 tests with one ignored. Proptest requested 16,384 cases per platform (16 properties, 1,024 each), seed `202610280610`; running the same seed twice is not twice as many distinct inputs. Formatting and Clippy warnings-as-errors passed. Managed tests passed 71/71, including FsCheck's 256 cases with replay `362,20261027`. Pester passed 22/22. Requirements and engineering documentation are checked separately in their JSON evidence.

Rust instrumentation reports 5,424/5,830 owned-source lines (93.04%), 511/554 functions and 8,209/8,809 regions. Branch coverage is unavailable. **The denominator differs from prior evidence**, including missing adaptive-module mappings despite unchanged source. Reordering objects and rebuilding the owned package did not restore those mappings. Do not compare the percentage as an improvement or treat it as complete source mapping. LLVM reported 81 mismatches, restricted by the diagnostic review to five known tracing dependency symbols; this does not resolve the separate adaptive mapping gap. The native Linux checkout has a historical Git HEAD; current source-file hashes, rather than that HEAD, identify the tested overlay.

Managed Coverlet reports 166/174 lines and 109/112 branches for the client. Replay-helper coverage reports 32/33 lines and 29/40 branches. Neither measures the entire repository. GPU, vendor-library internals, native plugins, unselected mutation shards and a new five-hour run are unavailable evidence. cargo-nextest was unavailable in this environment; regressions used cargo test. Raw artifacts remain under ignored work directories; no CI or source push was dispatched.

## Follow-up

Investigate the full-suite EOF timeout, repair or isolate Stryker's definite-assignment rollback coverage, and resolve the adaptive LLVM mapping discrepancy before comparing coverage trends. Reopen conditional equivalence decisions whenever source hashes, callers or input preconditions change. Preserve the nightly campaign and remote-results workflow.
