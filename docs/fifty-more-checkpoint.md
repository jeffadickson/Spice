# Fifty more decks: verified checkpoint

Two newly agreeing original inputs toward the target of fifty. Forty-eight remain. These are related UA741T macromodel filter examples, not two additional semiconductor model families.

| Original | Before | Faer | LU | Original AC frequencies |
|---|---|---|---|---:|
| ACtests/SANDLER8/sandler8.cir | missing_parameter | All 14 observables agree | AC residual failure | 67 |
| ACtests/SANDLER9/sandler9.cir | missing_parameter | All 11 observables agree | Agrees | 121 |

Input hashes and bounded verification records: ../evidence/fifty-more-checkpoint.json. Both Faer comparisons have zero missing/failed observables and no excluded samples. Existing 1 uV / 1 nA absolute and 1e-4 relative comparison bounds were unchanged. Original bytes and all requested analyses were retained. A named backend agreement is not universal backend or full SPICE qualification.

The bridge now evaluates finite affine I(name) sums through existing F/H elements, preserving coefficients, offsets, lexical branch binding and zero/cancelled reference validation. Mixed voltage/current laws, nonlinear current operations and explicit branch-current alias multipliers remain refused. Differential affine current laws use an internal voltage evaluator before unit transconductance instead of separate cancelling common-mode current injections. Native Newton linear residuals retain product/addition rounding remainders using standard fused arithmetic. DEC sweeps of at least one decade now match the retained ngspice endpoint-normalized construction; shorter DEC and OCT semantics stay separate. StrictDoc requirements remain Draft.

Both original BUG_1152 Butterworth transients now execute but remain unqualified because the native saved TSTART window does not cover every comparison coordinate. No extrapolation exception, omitted sample, relaxed tolerance or altered input was used to turn them into passes. The interrupted 113-file execution baseline is incomplete. A separate preparation-only scan of 113 inputs reports 36 prepared and 77 refused; these are not simulation success counts. A hash-deduplicated branch-current candidate scan finds only the two Chebyshev inputs newly preparing in that subset.

Verification: managed 1,143 passed / 42 explicit integration-dependent skips; Rust 42 unit and 66 targeted integration tests passed. Two authored affine-current fixtures separately agree in OP/AC/transient on LU and Faer, and the existing analytical/native affine-source oracle method passes. None of these authored fixtures count toward fifty. Five selected cargo-mutants changes were caught after a passing baseline; nineteen other listed helper candidates were not attempted. Stryker's separate initial-test stall supplies no C# mutation score. Seeds, source/tool hashes and coverage scopes are retained in the evidence JSON. Real StrictDoc parsing accepted 92 documents and 578 unique UIDs. Managed measured coverage is 24,187/29,387 lines and 11,890/16,935 branches. Rust newton.rs coverage is 715/1,113 lines through initial_newton integration only; stable branch coverage is unavailable. No dedicated new fuzz campaign ran.

Fresh-context read-only code review found no substantive defect. Its two minor follow-ups remain open: execute the high-common-mode differential fixture numerically, and broaden affine-current hierarchy/forward-reference/division/budget tests. Existing finite checks do not establish requirement closure or general conditioning guarantees.

| Three-run median child wall time | Ours, prepared release | ngspice, original | RSpice cached debug probe |
|---|---:|---:|---:|
| SANDLER8 | 430.59 ms | 170.36 ms | 118.20 ms |
| SANDLER9 | 225.74 ms | 173.97 ms | 109.18 ms |

Ours excludes SPICE preparation; ngspice includes parsing/RAW output; RSpice uses a cached debug core. Its timing invocation completes but is not an additional numerical equivalence oracle. These measurements do not meet the faster-than-ngspice goal and do not support a general speed ranking. Intel attempts qualify neither original: the low-pass attempt timed out and the high-pass attempt returned numerical_failure. Existing small Intel gates passing does not change that result.

Raw artifacts stay E:/Artifacts/Spice-fifty-residual-20261117T031043Z and E:/Artifacts/Spice-hourly-20261116-b03-152359. Use scripts/Run-RequestedCorpus.cs with explicit engine, oracle, frozen inventory, backend and fresh E: output; the checked-in authored inventory is compat/ngspice/affine-branch-current-inventory.json. The release engine hash/profile/features are bound by build-manifest.json. Source stays on the existing NTFS development branch; canonical main, unrelated work, CI and soaks are unchanged.
