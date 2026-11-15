# Dynamic functions and bilinear deck results

Ten unchanged originals previously refused preparation. They now execute every requested DC analysis on LU and Faer: 350 points per provider. SUBCKT/subckt_k0 through k3 agree with stock-default ngspice. BSRC/Bsrc_A1/A2 and SUBCKT/subckt_i1/i2/i3/i3_hs execute but disagree at the unchanged 1e-6 V, 1e-9 A and 1e-4 relative bounds. **Four new stock-reference passes, six unresolved comparisons.**

All ten agree with independent hash-pinned polynomial equations. The six nonlinear examples also agree with tightened-ngspice diagnostic copies; those modified copies are not original-default passes. For example Bsrc_A1 at input 5 V has exact output 513 V; default ngspice gives 512.8 V. RSpice's runner also reports mismatches on those six originals. This points to default Newton stopping accuracy in the reference, but does not justify weakening comparison bounds or declaring original-default agreement.

The generated ANTLR expression tree now binds bounded user functions, preserving native-adjudicated macro formal/parameter behavior and physical source origins. Bilinear voltage products reuse the native current multiplier with an explicit zero factor offset and SI normalization. Legacy omitted offsets remain one. Direct one-term sensing avoids a redundant voltage-source chain that caused LU backward-error refusal. Cubic/divisor/general function products and unqualified AC/transient requests remain explicit refusals.

## Verification

Managed frontend: 1038 passed, 40 skipped; syntax: 541 passed, one skipped. Configured native regression: 628 passed, five ignored. Frontend-run coverage of Frontend/Syntax/Client: 22595/27373 lines (82.54%), 10277/14445 branches (71.15%). Separate syntax coverage is not merged. Targeted actual native-law LLVM coverage: 38/38 lines, 35/35 regions, 4/4 functions; branch measurement unavailable because no branch counters were emitted. Integration/native driver code is not covered by that figure.

Native law properties ran 128 cases with seeds 2026111511 and 2026111513; existing law seeds 202611131101/1102 also ran. The managed regression includes established FsCheck and malformed-input campaigns (seeds 2026111501/03/05 and 362,20261031). Six exact arithmetic mutants were selected: four caught, two unattempted when the OS-headroom watchdog stopped the run. No complete mutation score or new Stryker score is claimed. Earlier CLI/missing-path-dependency baseline failures are retained as failed setup attempts, not kills.

The actual Intel circuit routine and physical Intel UHD630 probe passed. A ten-original Intel replay was stopped after two cases by the same OS-headroom guard; it is incomplete and cannot establish ten GPU passes or a speedup. Unknown applications and campaigns were preserved.

Real StrictDoc parsed 79 documents and 554 unique IDs. The snapshot helper's bounded self-test verified local native Cargo dependency/schema inclusion and nested target exclusion. Its line/branch coverage is unavailable. Read-only review's two proposed lexical-scope corrections were withdrawn after stock-ngspice probes confirmed the existing 4 V/80 V behavior; regressions retain both.

## Reproduction and provenance

Base source 5c0b29b5f25ceeef838ae67e7e26f2e35014a568 plus the checkpoint changes; exact executable, input and report hashes are in ../evidence/dynamic-bilinear-decks.json. Inventory: ../compat/ngspice/dynamic-bilinear-inventory.json. All raw outputs, supported build targets, TEMP/TMP, logs and coverage stay under E:/Artifacts/Spice-next-ten-20261115T061009Z. The raw directory retains guarded PowerShell launchers and exact commands, frozen managed assemblies, request/response maps, unchanged native RAW/logs, RSpice protocol results and failed/interrupted attempts.

Run the existing requested-corpus coordinator with that inventory and backend lu/faer/intel, then scripts/Check-PolynomialCorpus.cs with inventory, summary and output arguments. Its optional fourth argument is the separately labelled refined native directory. Run scripts/FrameworkMutations.cs --rust-inputs-self-test for snapshot regression; normal campaigns must isolate Cargo output and include native path dependencies. Use the repository SDK pin and supported E: output settings.

No full corpus rerun, Linux, general Xyce compatibility, full SPICE, complete native coverage, generalized nonlinear AC/transient or production GPU qualification is claimed. Original diode Params_C5/C5_hs/D5/D5_hs mismatches found in the initial screen remain unresolved and were not counted. Canonical main and its unrelated changes were preserved.
