# Intel circuit execution assessment — November 14, 2026

Follow-up: [the RC/RLC reference study](rc-rlc-transient-results.md) corrects the
interpretation of the transient gaps below. Both free-running engine traces
satisfy independent analytical bounds; replaying the native grid also agrees.
RSpice's passing pointwise comparison uses a controlled reference grid. The
original measurements remain historical evidence, not proof of worse engine
integration accuracy. GPU performance conclusions are unchanged.

**The Intel GPU solves real DC and AC circuit requests correctly in the measured
subset, but does not help these small decks. Keep LU/Faer as the default.**
The experimental provider remains explicit and optional. It is not production
GPU qualification, a replacement for the CPU solvers, or a GPU implementation of
the entire simulator.

## What actually ran

Physical device: Intel UHD Graphics 630, Windows x64, driver 31.0.101.2141.
The provider selects an Intel GPU with native FP64 through OpenCL; CPU fallback
is forbidden. CPU assembly, models, Newton control, integration and audits remain
in the Rust engine. ViennaCL 1.7.1 supplies general preconditioned GMRES and GPU
operations; small host GMRES operations remain CPU work. Complex AC is an exact
real block embedding. Original circuit capacity stays 256 unknowns.

The final five-file replay has valid source/runtime gates for LU, Faer and Intel.
Every requested analysis is attempted; an incomplete program is not called
passing. Numerical bounds remain voltage `1e-6 V`, current `1e-9 A`, relative
`1e-4`; the original-system GPU backward-error gate remains `1e-10`.

| Input | LU and Faer versus stock ngspice | Intel versus stock ngspice | RSpice versus fresh ngspice |
|---|---|---|---|
| Authored `examples/intel-rc.cir` | OP/AC agree; transient executes but disagrees | OP/AC agree; transient times out | OP/AC/transient agree |
| Authored `examples/intel-rlc.cir` | OP/AC agree; transient executes but disagrees | OP/AC agree; transient times out | OP/AC/transient agree |
| Original `tests/ngspice/regression/misc/asrc-tc-1.cir` | OP/AC and control assertions agree | OP agrees; AC times out, program incomplete | Upstream SMOKE only; no numerical qualification |
| Original `tests/ngspice/regression/misc/bugs-2.cir` | Five-point DC and indexed reporting agree | Five-point DC and reporting agree | Upstream SMOKE only; no numerical qualification |
| Original `tests/xyce/Netlists/RESISTOR/resistor_neg.cir` | Six-point DC agrees | Six-point DC agrees | DC sweep agrees |

The two newly completed unchanged original programs are `bugs-2.cir` and
`resistor_neg.cir`, committed in `1ab34d0` and `1b2d5ab`. The latter is an Xyce
fixture retained in the RSpice repository, **outside** its historical 113-file
ngspice cohort. No whole-corpus total increase is inferred from this selected
run. See [the two-deck assessment](two-original-decks-results.md).

The final selected run therefore has five fully executed programs and three
fully agreeing programs on each CPU solver; Intel has two fully executed,
fully agreeing programs and three incomplete programs. Partial OP/AC agreement
is not complete-deck agreement. RSpice has three numerical comparisons and two
non-numerical smoke contracts. Its conformance runner does not implement those
original control programs; a reported SMOKE pass is not a third numerical check.

All standard Intel timeouts use the existing 30-second per-operation deadline.
A separate RC transient diagnostic also failed to finish within 300 seconds
(observed transport duration 302.26 seconds including termination). The owned
benchmark was stopped after preserving that result, avoiding three redundant
five-minute timeout repetitions. It is not a completed transient or a passing
latency sample. Other projects and active hosts were preserved.

## Does it help?

No benefit was measured. The completed final timing replay used exactly the
same circuit JSON and executable, changing only `backend`. Each provider had one
excluded warmup followed by three measured fresh processes, with a private E:
shader cache shared inside the run. All 18 measured executions succeeded; all
12 Faer/Intel response comparisons against LU agreed on complete matching grids
and every signal. These are one-shot process latencies, including startup,
assembly, transfers, solve, audits and cleanup—not kernel timings.

| Request | LU median (range), s | Faer median (range), s | Intel median (range), s |
|---|---:|---:|---:|
| RC AC, three frequencies | 0.111 (0.080–0.208) | 0.265 (0.200–0.440) | 16.201 (13.375–19.297) |
| Negative-resistance DC endpoint | 0.309 (0.272–0.406) | 0.093 (0.090–0.118) | 2.162 (1.982–2.227) |

Rust engine and CPU libraries are **dev/unoptimized**; the separate C++ provider
is Release `/O2 /fp:precise`. The host was not quiescent, and earlier AC samples
varied from 1.48 to 6.08 seconds on Intel. Three repetitions are diagnostic, not
a statistical release benchmark. These limits prevent a hardware-wide or
optimized-solver speed ranking, but the tested path offers no practical benefit
on these small one-shot circuits. No comparable standalone RSpice performance
number is claimed: the available runner also launches ngspice and archives its
reference. Its elapsed time is not an isolated RSpice solve time.

A future acceleration experiment would need persistent/batched execution and a
representative larger workload before spending more on GPU optimization. Simply
enabling parallelism or GPU kernels is not evidence of a speedup. The Intel
provider should not silently replace LU/Faer.

## What the comparison exposed

The CPU RC transient retains 149 samples versus 108 native points, with maximum
output-voltage error `1.1585323752122352e-5 V` and current error
`1.1585323752117229e-8 A`. There are 64 failed output-voltage samples and 15 failed
current samples at the unchanged bounds; no startup points are excluded.
The RLC transient has 206 engine samples, maximum output-voltage error
`1.295747463331276e-5 V` (156 failed samples) and inductor-current error
`1.2449073095805205e-8 A` (88 failed samples). LU and Faer show the same gap.
RSpice's fresh comparisons pass both fixtures. This is the next useful shared
numerical investigation: isolate integration/startup and reference-sampling
effects with independent analytical RC/RLC solutions before changing equations
or tolerances. The present evidence does not establish a sole cause.

The first GPU strategy also exposed real integration failures: unpreconditioned
GMRES returned a bad solution; context teardown initially failed; and normwise
convergence did not ensure componentwise physical accuracy. They were rejected,
not hidden by CPU fallback. The final adapter uses row/column equilibration,
row-norm preconditioning, exact homogeneous singleton constraints, and up to
three GPU-solved residual corrections with an internal `1e-12` margin. Every
result still passes native and independent safe-Rust original-system gates.
The noticed context-release header overlay is narrowly documented in the native
README; numerical dependency code is unchanged. Raw failed attempts remain local.

## Verification and limits

- Physical Intel contract: one test containing real/complex source-constrained
  systems, a full divider request, malformed/nonfinite/inconsistent inputs and
  16 Proptest cases at seed `2026111404`, dimensions 2–8. It passes.
- Eight provider regressions, four CLI regressions (including actual metadata
  process exit), and two safe-gate tests pass. An obsolete pre-existing CLI
  expectation for configured trapezoidal diode support was corrected to match
  the implemented capability; no production diode behavior changed here.
- Pester: 21 passed, zero failed/skipped; 203/216 commands (93.98%) across the
  two owned launcher/comparison helpers. Checks include wrong voltages,
  missing data, unknown axes, empty observables, different grids and false CPU
  fallback. Exact counts and command coverage are recorded in the evidence.
  PowerShell command coverage is not Rust/C++ branch coverage.
- The initial narrow LLVM gate run measures 38/100 adapter lines. A subsequent
  instrumented physical contract passes and measures 82/87 production adapter
  lines (94.25%), and 939/7,543 owned-library lines across 49 files in that
  one-test scope. These scopes differ and are not whole-suite coverage. Stable
  Rust branch metrics and host-FFI/native/driver/kernel coverage are unavailable.
- One disposable manual gate fault—removing nonfinite residual-scale rejection—
  is detected (one failed, one passed); the unchanged control passes both tests.
  This is not a cargo-mutants campaign or a mutation score. The frontend's two
  additional manual faults and Stryker initial-build timeout are documented in
  the two-deck assessment. No Stryker mutants were assessed in that attempt.
- Frontend/Syntax suite: 960 passed, 40 explicit external-prerequisite skips.
  Supplemental Microsoft coverage is 10,225/12,882 lines and 7,012/10,649
  branches. Coverlet's captured-local-function omissions are preserved as a
  separate scope, not equated with those numbers.
- Real StrictDoc parsing passed all 74 documents. Requirements remain Draft.
  Read-only reviews found six actionable GPU/tooling issues, all addressed:
  nonfinite residual scale, runtime hash capture, dependency-source cleanliness,
  mutable snapshot-library attribution, mixed response shapes, and replay
  input/runtime provenance. Follow-up source review and negative tests verify
  the dispositions; these were not external expert reviews.

No Linux GPU, other GPU, nonlinear semiconductor GPU deck, SOI, full-SPICE,
optimized-release performance, or production qualification is claimed. The
256-unknown dense host contract and per-solve host/device overhead remain.
The schematic UI was not changed or qualified for this provider by this task.

## Provenance and reproduction

Raw root: `E:/Artifacts/Spice-Intel-decks-42e5356-20261114`.
The exact source states are base `42e53560273a2cd29e398e5b70e8c0114ff3bcb2`
plus recorded dirty source hashes, subsequently committed checkpoints. Original
RSpice source pin: `29c28a022625fd7cc2c56ead1cce2d3066663074`.
Oracle: ngspice 47+, compiled October 24 with KLU. Reused RSpice runner and
oracle hashes are in `evidence/intel-circuits.json`; no fresh clean RSpice build
is claimed.

Native dependency: ViennaCL 1.7.1 at
`77e9049d5622d46b6801a725917a931eda096f76`, MIT/X11. Build manifests retain
supplier header hashes, owned overlay hash and redistribution notice. The
diagnostic engine was linked from the Cargo-owned library graph into a fresh
executable; this is not a normal clean Cargo binary build.

Core reproduction entry points (all bulk destinations must be fresh children of
an existing writable E: artifact root; configure the process-tree guard first):

```powershell
if (!(Test-Path -LiteralPath $repository -PathType Container)) { throw 'Missing source' }
if (!(Test-Path -LiteralPath $artifactRoot -PathType Container)) { throw 'Missing artifact root' }
& (Join-Path $repository 'scripts/Build-IntelProvider.ps1') -ViennaClRoot $viennaCl -OutputDirectory $native
& (Join-Path $repository 'scripts/Build-IntelEngine.ps1') -TargetDirectory $target -OutputDirectory $snapshot
& (Join-Path $repository 'scripts/Compare-IntelDecks.ps1') -Checkout $repository -ManagedBin $managedBin -Engine $engine -Provider $provider -Corpus $corpus -Ngspice $ngspice -Inventory $inventory -OutputDirectory $replay
& (Join-Path $repository 'scripts/Compare-RSpiceDecks.ps1') -Checkout $repository -Corpus $corpus -Runner $rspiceRunner -Ngspice $ngspice -Inventory $inventory -OutputDirectory $referenceReplay
& (Join-Path $repository 'scripts/Measure-IntelDeckLatency.ps1') -Engine $engine -Provider $provider -Requests $requests -OutputDirectory $timing
& (Join-Path $repository 'scripts/Compare-IntelReplay.ps1') -MeasurementDirectory $timing
```

`observed-inventory.json` identifies all five exact inputs. The two authored
fixtures gained observation-only `.print` cards before the final replay to make
RSpice's reference tables available; their prior hashes/results are retained.
Original corpus bytes, numerical controls and acceptance bounds were unchanged.
The final replay's `corpus_commit` field is empty because its helper originally
trusted an assumed grandparent rather than the actual Git root. Exact selected
input hashes still match. The pinned corpus commit was independently rechecked
and recorded in `corpus-pin-verification.json`; the helper now discovers the
actual repository root. This is a metadata limitation, not fabricated provenance.
`decks-v3`, `rspice-v2`, `snapshot-tests-v3`, `latency-v2`, the interrupted
`latency-v1`, manual fault and coverage directories are retained separately.

Bounded evidence and the three selected-cohort stage charts are committed under
`evidence/`. These charts cover five selected inputs, not overall corpus parity.
Canonical main, its unknown sheet-resistor changes and VeloSim are untouched.
No CI or new endurance campaign was dispatched.

Checkpoints `1ab34d0`, `1b2d5ab` and `40baa6c` were pushed to
`fix/deck-blocker-implementation` and its remote head verified. Results and
prioritized follow-up are in [issue #48](https://github.com/jeffadickson/Spice/issues/48).
The development branch's numerical entry points now include actual Intel
circuit tests as well as the matrix probe. Canonical main and its nightly
workflow were not updated or represented as running this new optional provider.
