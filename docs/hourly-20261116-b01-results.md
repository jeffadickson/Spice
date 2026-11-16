# Hourly batch 20261116-b01: four original AC decks agree

Of ten newly selected original files, four now execute every requested analysis and agree with ngspice on Faer: diffpair_spiceMan, diffpair_spiceManLIN, diffpair_spiceManOld and rca3040. These are three frequency grids of one differential pair and one wideband amplifier. The other six remain preparation refusals. This is the first ten additions beyond the frozen thirty, not completion of the eighty-deck target.

The shared blocker was DEVICE NEWEXCESSPHASE=0/1. Its auxiliary phase states can be eliminated when every admitted BJT has no authored PTF: TF alone is storage, with zero phase delay. The frontend checks the whole root card and models, records this equivalence explicitly, and refuses duplicates, nested settings, companions, unsupported selector values and any authored PTF. This does not implement nonzero excess phase. Requirement FRONTEND-BJT-PHASE-001 remains Draft.

Source guidance: ngspice bjttemp.c computes TF times PTF in radians; Xyce N_DEV_BJT.C has decoupled zero-delay auxiliary equations. Pinned RSpice bjt_excess_phase.rs preserves authored phase. No vendor implementation was copied. See https://raw.githubusercontent.com/Xyce/Xyce/master/src/DeviceModelPKG/OpenModels/N_DEV_BJT.C .

Qualification preserves original input hashes and all requested AC grids (81, 100, 1 and 101 points). Full mapped complex voltage and supported branch-current signals meet the existing voltage absolute 1e-6, current absolute 1e-9 and relative 1e-4 bounds. Direct unchanged-original ngspice invocation also produces exactly the same physical vectors and real frequency coordinates as the projection. Tiny imaginary scalar-frequency metadata is excluded by coordinate semantics. LU agrees on three; RCA3040 refuses componentwise backward-error refinement at its first point. Faer passes all four. RSpice independently passes these four cases.

Fresh frontend suite: 1109 passed, 42 skipped, zero failures. Includes 64 FsCheck selector cases, replay 2161,20261117, plus rejection and identical-request regressions. Microsoft coverage across Client, Frontend, Syntax and RequestedQualification: 24100/29304 lines (82.24%), 11756/16799 branches (69.98%). This is not Rust or whole-repository coverage. StrictDoc SDReader parsed 85 documents and 571 unique UIDs. Intel circuit and low-level FP64 probes passed; these four original decks were not qualified on Intel. No fresh mutation or broad fuzz campaign ran; Stryker was not located in the checked configured paths.

Median child-process times in milliseconds, three measured samples after warmup:

| Original | Ours prepared | ngspice original | RSpice original |
|---|---:|---:|---:|
| diffpair_spiceMan |35.89|120.77|70.69|
| diffpair_spiceManLIN |39.29|109.78|70.73|
| diffpair_spiceManOld |34.44|93.89|66.89|
| rca3040 |697.02|101.12|115.01|

Ours excludes SPICE preparation; RSpice uses its cached debug core. These are not comparable release end-to-end speed claims. RCA3040 is substantially slower and is a profiling target. All timing inputs and executable hashes are retained.

Remaining six: numerical continuation options on two opamps, MOS model profile, two analytic BJT ramp settings/budgets, and BJT_OFF model/nonlinear/OFF behavior. Do not discard physics or weaken tolerances to admit these. The original thirty retain their separate six executions and zero strict stock agreements.

Bounded evidence: evidence/hourly-20261116-b01.json, inventory under compat/ngspice, and updated burndown SVG. Raw artifacts: E:/Artifacts/Spice-hourly-20261116-b01-050144. Production hashes identify the tested dirty tree based on a854c7a; no Rust implementation changed. The misleading after-lu diagnostic directory actually used Faer; only after-lu-actual establishes LU results. An accidental old RSpice selection is excluded; rspice-b01 is the corrected selection.
