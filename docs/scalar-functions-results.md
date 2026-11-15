# Ten additional original DC programs

Ten previously refusing, unchanged files now execute every requested DC coordinate and agree with stock ngspice47+ on LU, Faer and the physical Intel backend. RSpice's retained live-reference runner passes the same ten. This is ten related original files, not ten new topologies or model families.

Implementation checkpoint: `02f1a1322ee34862d5dfaa576f787917dd4f7b96`. Results and prioritized follow-ups: [issue51](https://github.com/jeffadickson/Spice/issues/51).

Run: `Spice-ten-more-20261115T045430Z`, November14 Pacific. [Bounded evidence](../evidence/scalar-functions-decks.json), [selected-cohort burndown](../evidence/scalar-functions-burndown.svg), [requirements](scalar-functions.sdoc), [portable input inventory](../compat/ngspice/scalar-functions-inventory.json).

| Original retained RSpice path | Before | Completed DC points |
|---|---|---:|
| `tests/xyce/Netlists/BSRC/bsrc1_m.cir` | Quoted expression refused | 12 |
| `tests/xyce/Netlists/BSRC/bsrc2_m.cir` | Quoted expression refused | 12 |
| `tests/xyce/Netlists/BSRC/bsrc3_m.cir` | Quoted expression refused | 12 |
| `tests/xyce/Netlists/BSRC/bsrc4_m.cir` | Quoted expression refused | 12 |
| `tests/xyce/Netlists/PARAMS2/Params_A2.cir` | Function card refused | 6 |
| `tests/xyce/Netlists/PARAMS2/Params_A8.cir` | Function card refused | 6 |
| `tests/xyce/Netlists/PARAMS2/Params_B2.cir` | Function card refused | 6 |
| `tests/xyce/Netlists/PARAMS2/Params_B7.cir` | Function card refused | 6 |
| `tests/xyce/Netlists/PARAMS2/Params_B8.cir` | Function card refused | 6 |
| `tests/xyce/Netlists/PARAMS2/Params_B9.cir` | Function card refused | 6 |

These files belong to the retained Xyce test tree at RSpice29c28a022625fd7cc2c56ead1cce2d3066663074. They are outside the historical113-file `tests/ngspice` cohort: this checkpoint claims zero additional numerical passes for that older cohort. Already passing BSRC baseline variants and the previous five VCCS passes were excluded.

The generated ANTLR parser now retains quoted B laws as expressions instead of scalar names. Positive explicit B-current M scales gains and offsets through existing affine stamps. Ordinary declared hierarchy parameter M is evaluated where used; it is not implicitly applied as a Xyce hierarchy multiplier. No Rust equations, native schema or dependencies changed.

Scalar `.func` metadata reuses generated header/body contexts. Evaluation supports device/model values, parameters and instance overrides, nearest function definitions, nested calls, formal shadowing and custom functions shadowing builtins. Ambiguous definitions and malformed calls refuse atomically. Limits are32 active calls,8192 aggregate nodes across parameter dependency edges, and existing per-expression character/node/depth bounds. Voltage-dependent user function lowering remains a separate gap.

RSpice source guidance included `netlist/source_map.rs::param_context_in`, the subcircuit-body function shadowing regression in `netlist.rs`, and its source multiplicity parsing. No third-party source was copied. Independent native probes settled subtle macro semantics: free parameters can observe the calling parameter environment; nested function names use the definition namespace; declared `sin(x){2*x}` overrides builtin sin. The probe inputs, outputs and checksums remain in the raw run. A fresh reviewer withdrew an initial scope concern after these native results, identified an existing diagnostic regression and a dependency-edge budget bypass; both accepted defects were fixed and regression-tested.

## Numerical evidence

Each provider completed84 requested DC coordinates and288 mapped physical signal samples. No modified originals, weaker tolerances, grid replay, omitted requested analyses or startup samples were used. Bounds remain1e-6V,1e-9A and1e-4relative. `.print` formatting is not reproduced; its physical node/source-current observables are covered by the comparison maps. Behavioral I(B) probes are not newly qualified.

| Provider | Largest voltage error | Largest current error |
|---|---:|---:|
| LU | 1.78e-15V | 3.47e-18A |
| Faer | 2.85e-14V | 3.47e-18A |
| Intel | 2.85e-14V | 2.90e-15A |

The separate authored [scalar-functions example](../examples/scalar-functions.cir) also agrees on all three providers for OP,5-point DC,7-frequency AC and101 transient samples, with three mapped signals. It is not an additional original deck. Intel's13 actual circuit/provider tests and physical sparse probe passed on UHD630, driver31.0.101.2141; no GPU speedup or general transient qualification is inferred.

## Verification and limits

- Frontend suite:1015passed,40prerequisite skips. Syntax suite:541passed,1filesystem-link skip. The29 new focused cases cover positive/negative laws, scope, function depth32/33, alternating parameter/function chains and aggregate node exhaustion. Existing bounded malformed-input property ran512cases, replay362,20261031. New FsCheck properties each ran128cases, seeds2026111503 and2026111505; inherited affine property seed2026111501 also ran128cases.
- Microsoft.CodeCoverage18.5.1, including captured local bodies in Frontend/Syntax:10495/13131lines (79.93%),6993/10603branches (65.95%). Separate syntax Coverlet scope:7413/11390lines (65.08%),2768/4792branches (57.76%). These scopes are not merged. Native Rust/GPU-driver code was not newly instrumented; Linux was not executed.
- Real StrictDoc parses78documents with550unique requirement IDs. Requirements remain Draft outside these evidenced subsets.
- Stryker5.0 could not instrument the selected syntax scope successfully. Raw report:2182CompileError and1946Ignored slots, including whole-method/out-of-filter rollback entries; five slots on the requested boundary line are CompileError. No executable mutant score or successful automated mutation coverage is claimed. That pre-shadow snapshot is preserved unchanged.
- Three isolated faults on the final production snapshot compiled and were detected: depth comparison (1failed/9passed), builtin shadow bypass (1failed/9passed), and current multiplication changed to division (3failed/5passed). Exact restored snapshots passed10syntax and8current-law tests. An earlier invalid shadow fault did not compile; it is retained as an invalid attempt, not a kill.

## Reproduction and follow-up

The reusable coordinator is `Simulator.RequestedQualification.RequestedCorpus.RunAsync`, also exposed by `scripts/Run-RequestedCorpus.cs`. Its argument order is checkout, pinned corpus root, qualified native executable, ngspice executable, inventory, fresh results directory, budget seconds and provider. Use `compat/ngspice/scalar-functions-inventory.json` for the ten originals. Retain the source pin, hashes and unmodified native defaults.

The actual run invoked that API from frozen managed DLLs built in the NTFS checkout, passing600seconds and each of `lu`, `faer`, `intel`. Raw `AcceptedPhysics.ps1`, `AcceptedFull.ps1`, `Intel.ps1` and `ManualFaultsV2.ps1` record exact invocations under `E:/Artifacts/Spice-ten-more-20261115T045430Z`. All bulk outputs/TEMP/TMP were on E:, with4GiB per-tree/6GiB aggregate guards and4GiB OS reserve. Mutation and ordinary build outputs were isolated. The frozen native engine's51 prior Rust/configuration hashes still match; no fresh native model build is claimed.

Next priorities are dynamic user-function lowering for Bsrc_A1/A2 and other genuinely new circuit behavior, remaining historical-corpus model/analysis blockers, and fixing Stryker's instrumentation limitation. Nonpositive B M, voltage-source M and general nonlinear current laws remain explicit refusals. The wider syntax-only screen omitted resource expansion and is selection data, not a full corpus execution count. Canonical main and unknown sheet-resistor work were preserved; no CI or new soak was launched.
