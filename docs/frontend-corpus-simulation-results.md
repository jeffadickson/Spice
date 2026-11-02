# Generated frontend: actual circuit execution — November 1, 2026 Pacific

**17 corpus files now execute selected static DC operating points through the
generated frontend and Rust, on both LU and Faer.** Thirteen are upstream ngspice
sample paths and four are repository examples. Sixteen files independently match
ngspice at all exposed root nodes; the zero-port example solves but exposes no
root-node voltage for this profile, so its numerical agreement is unavailable.
This is not complete execution of their original analysis/control programs.

The 17 file paths have 14 distinct root-byte hashes; three upstream parallel-tree
copies are counted as files, not claimed as distinct circuit designs. The 16
compared files have 13 distinct root-byte hashes. Transitive source fingerprints
are retained separately. This is a new selected-DC cohort, not a direct improvement
of the older three-file AC/transient importer comparison count.

| Final generated execution census | Files |
| --- | ---: |
| Compared selected DC on both providers | 16 |
| Solvers completed, root reference unavailable | 1 |
| Lowering refused | 873 |
| Resource policy refused | 30 |
| Prior hash-pinned invalid/non-deck exclusions | 8 |
| Inventory total | 928 |

There are 920 valid root inputs after exclusions. No inputs remain unfinished;
no engine failures or numerical discrepancies were observed in this final census.
**34 actual Rust provider solves** completed. There are **96 voltage/node/provider
comparisons with zero failed points**, not 96 circuits. The corpus requests include
two capacitors, 40 resistors, 14 current sources and 30 voltage sources across the
17 LU requests. Inductor DC behavior is independently checked in owned analytical
integration fixtures, not claimed exercised by these corpus requests.

[Campaign results and fix checklist](https://github.com/jeffadickson/Spice/issues/20). Source checkpoints: `2f4978c` and `ab356f7`.

## What changed

The generated frontend now has an actual opt-in execution harness,
`scripts/Run-FrontendCorpus.cs`, using the public Client wire API and retained
Rust executable. It resolves resources explicitly, parses/binds/evaluates with
the existing ANTLR frontend, prepares typed requests and executes LU/Faer. It does
not send accepted input back through the old numerical text importer.

`OperatingPointBridge.Lower` keeps its strict R/V/I semantics. The new explicit
`OperatingPointSelection.Select` operation prepares static DC for ideal R/C/L
and constant independent V/I. It records all excluded analyses, input controls
and named read-only output directives as immutable scope/name/range/reason data.
Unknown extensions remain refused even in unused bodies because they may have
global physical effects. `.global`, `.ic`, `.options`, source waveforms/AC headers
and model-device semantics are not silently ignored or replaced with zero.
R/C/L values must be positive SI values. Failure returns no partial request or
omission plan. Existing Rust equations provide capacitor-open/inductor-short DC;
no solver, parser or numerical model was reimplemented.

The independent oracle consumes expanded original source. Only the recorded AST
ranges are blanked before injecting the harness's own OP queries; physical device
and parameter values are never copied from the lowered request to manufacture a
matching reference. Original control programs never execute. Tests verify their
marker remains in original expanded source and disappears from native input.

Provider data must have matching case/provider CPU identity, finite samples,
unique signal IDs and the expected root-voltage units. Bad results remain failed
per backend without suppressing the other backend. Missing or invalid oracle
observations and absence of exposed root nodes remain unavailable, not passes.
Absolute voltage bound is **1e-9 V + 1e-8 times the reference magnitude**.

## Executed sample paths

Upstream ngspice (all 13 have native root-voltage agreement):

- `examples/control_structs/if-batchmode.cir`
- `examples/control_structs/if-test-1.cir`
- `examples/various/param_sweep.cir`
- The corresponding three `paranoia_parallel` paths
- `tests/regression/lib-processing/ex1a.cir`, `ex1b.cir`, `ex2a.cir`, `ex3a.cir`
- `tests/regression/lib-processing/scope-1.cir`, `scope-2.cir`, `scope-3.cir`

Repository examples: `compat/ngspice/semantic-nested.cir`,
`semantic-parameters.cir`, `semantic-port-order.cir` match; `semantic-zero-port.cir`
completes both solvers but has no exposed root observables. This harness does not
yet qualify private-node voltages or source/inductor currents, complete waveforms,
original scripts, AC/transient analysis selection or model families.

## What actual execution taught us

The first strict generated-frontend baseline had zero eligible corpus files:
890 lowering refusals, 30 resource refusals and eight exclusions. Named fixtures
alone had hidden this integration gap. Explicit selected static-DC semantics
unblock real inputs without claiming their other requested operations ran.

The first selected trial solved 17 files but reported two apparent mismatches:

- ngspice `echo $&vector` rounded a repeating fraction to `47.6667`, while both
  Rust providers returned `47.666666666666664`. An independent native precision
  control returned `4.76666666666666643e+01` with `print` and `numdgt=17`.
  A failing repeating-fraction integration regression preceded the fix. The
  numerical bound was preserved; it was not increased to hide formatting loss.
- An empty root-observable set was incorrectly labelled a mismatch. A separate
  red zero-port regression preceded explicit `solver_completed_no_root_observables`.

Additional red tests exposed per-provider malformed-result classification and
admission-budget validation. Synthetic malformed/missing/unit/non-finite/wrong
case/wrong provider data and a deliberate large numerical discrepancy are never
counted as simulator evidence. The synthetic executable is visibly test-only.

The final replay took 314.614 seconds, excluding build/startup provenance work.
Earlier completed phases remain local historical evidence, including their false
mismatch classifications; they are not retroactively edited into passing reports.
Root graph hashes match all 890 successfully resolved inputs. Source, loaded
assembly, harness, inventory and native executable fingerprints identify the
actual execution independently of the eventual publication commits.

## Tests, coverage, mutations and documentation

- **406 Syntax + 57 Frontend tests pass**, with one exFAT link-fixture skip.
  Frontend integration includes strict/selected analytical circuits, hierarchical
  resources, signed sources, punctuated nodes, RLC steady behavior, malformed and
  changed inputs, singular failure, unexecuted controls, fractions, zero-port
  unavailability, six synthetic result faults and invalid budgets.
- FsCheck runs 128 positive selected-input cases with replay `907,20261101` and
  128 bounded arbitrary-input cases with `908,20261101`, alongside existing
  binding/scalar/transport properties. No separate coverage-guided fuzz or soak.
- The existing four-fixture strict DC regression passes 12 comparisons/24 solves.
  The five-case repeated benchmark passes one warmup plus three measured repeats.
  These are separate regression cohorts and do not inflate corpus file counts.
- Collector-reported handwritten subset: **843/855 lines (98.5%) and 1004/1146
  branches (87.6%)**. The denominator remains incomplete because Coverlet 10.1.0
  omits captured core local functions/callbacks. File-app/synthetic executable
  child coverage was not collected. Generated ANTLR code is excluded; there is
  no whole grammar/serialized-ATN or new native/Rust/Linux/GPU coverage claim.
- Initial scoped Stryker.NET Basic: 22 killed, two survivors, 13 compile errors,
  75 ignored. A new zero-inductance test catches original survivor89 in a
  guard-only replay: four killed, zero survivors, 13 compile errors, 95 ignored.
  Original survivor75 is retained only as a conditional equivalence under the
  internal extractor invariant `Supported iff Value is non-null`; not formal
  proof or closure under future API/producer changes. No whole-shard score.
- StrictDoc 0.30.4 parses **21 documents, 328 IDs, 316 parent relations**.
  SEM-SELECT-001, SEM-EXEC-001 and SEM-ORACLE-001 remain Draft with bounded evidence.
  Sphinx builds with warnings as errors and compiled API XML. AI read-only review
  findings on result classification and documentation were fixed; this is not
  external human review. A real StrictDoc separator error was fixed and rechecked.

## Reproduction and next work

Raw artifacts: `E:\Artifacts\Spice-frontend-simulation-20261102T025251Z`. The UTC
folder date is November 2; the local execution date is November 1 Pacific. The
qualification used the isolated `work/simulation-bridge` checkout and excludes
main's preserved uncommitted sheet-resistor work. Rust source/Cargo inputs are
unchanged from the reused `344cb5b` debug-build checkpoint. No fresh native build,
NativeAOT qualification or release-performance ranking is claimed.

Use the checked-directory setup in `docs/semantic-frontend.md`. Build the helper
with `--artifacts-path <E>/build`, `GrammarOutput` and the existing pinned ANTLR jar.
Pass checkout, corpus, Rust executable, ngspice executable, completed syntax
inventory JSON, fresh output, `selected-linear-op` and admission seconds (`1800`
for this run). Default/explicit `strict-rvi-op` preserves the strict baseline.
Budget admission is checked between inputs; child operations are independently
bounded to 30 seconds and may finish the current row after admission expires.

Build `tests/fixtures/FrontendCorpusFaultEngine.cs` into a separate E: artifact
path for negative tests. Set SPICE_FRONTEND_CORPUS_DLL, SPICE_FRONTEND_REPOSITORY,
SPICE_CORPUS_ENGINE, SPICE_CORPUS_NGSPICE, SPICE_CORPUS_TEST_ROOT and the synthetic
SPICE_FRONTEND_FAULT_ENGINE path before the full Frontend test suite. Missing
external prerequisites are explicit skips. Synthetic outputs never qualify Rust.

Next simulation work: independently represented DC/AC source profiles, then
selected linear AC and transient execution through this same path, followed by
bounded model contracts. First diagnostics include 365 unsupported active-card,
351 unqualified-directive, 82 primitive-header, 55 empty-circuit, ten missing-
parameter, eight unsupported-expression and two override refusals. These are
first diagnostics, may mask further gaps and are not independent root causes.
Prioritize executable representative inputs and ngspice references; grammar
expansion resumes only when a concrete execution input actually requires it.
