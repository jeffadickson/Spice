# Generated frontend simulation checkpoint — 2026-11-01

The generated frontend now prepares real typed requests for the existing Rust
engine. This is an R/V/I constant-DC operating-point slice, including hierarchical
ports, lexical parameters/overrides and explicitly expanded include/library files.
Model, waveform, AC/transient and general global-node lowering remain open.

## Executed evidence

- Native Rust executable built from unchanged Rust source at base `344cb5b`.
- 33 frontend tests passed; 339 syntax/semantic tests passed and one link fixture
  skipped because E: uses exFAT. FsCheck frontend replays `901,20261101` (64
  isolation cases) and `903,20261101` (128 bounded arbitrary-text cases) passed.
  Existing syntax randomized/malformed-input properties also ran. No separate
  coverage-guided fuzz campaign or Rust mutation campaign ran for this slice.
- Four named fixtures (`flat`, `hierarchy`, `signed-voltage`, `signed-current`)
  agree with analytical values, native ngspice 47+ and independently authored
  typed requests on `lu`, `qr`, `faer`: 12 comparisons and 24 Rust solves.
  Tolerance: 1e-10 absolute plus 1e-10 relative. The hierarchy includes separate
  parameter/include and selected-library files. This is finite Windows evidence.
- **Coverage is incomplete.** The collector reports 801/813 lines (98.5%) and
  962/1104 branches (87.1%) only for its instrumented Syntax/Frontend subset.
  Raw Cobertura omits captured local functions, including bridge `Expand` and
  `Node` and ResourceResolver internals. The Frontend 100% line / 95% branch
  figure measures outer validation/results/records, not core lowering. Treat
  those percentages as a partial denominator, not full handwritten coverage.
  Generated ANTLR and Client are excluded; Rust, file-app and corpus coordinator
  were not measured. Closure-inclusive coverage remains an explicit tooling gap.
  Related upstream report: https://github.com/coverlet-coverage/coverlet/issues/2045;
  exact causation here is not established. This also limits comparison with prior
  semantic reports. Numerical, mutation and property evidence remains separate.
- StrictDoc 0.30.4 parsed 21 documents, 317 unique IDs and 305 resolved parent
  relations. SEM-BRIDGE-001 and SEM-REBIN-001 remain Draft. Sphinx 9.1.0 built
  with warnings as errors and compiled Syntax/Frontend/Client XML documentation.
- The runnable `examples/FrontendOperatingPoint.cs` returned 3 V and 2.4 V for
  the hierarchical divider. Public interfaces use the existing Client contract.

## Full corpus and remaining common blockers

The primary run completed 928 rows in 380.637 seconds: 916 accepted structures,
four encoding refusals and eight previously adjudicated invalid/non-deck inputs.
The explicit legacy replay accepted four distinct Latin-1 inputs; its 18 repeated
repository decks are not counted twice. Acceptance gate: 920/920 declared-valid
samples, zero outstanding. This remains a syntax gate, not simulation coverage.

Resource rebinding across those 920 valid decks resolved 834 and explicitly
refused 86. Refusal counts: conditional resources 34, resource syntax 15,
resource parse budget 14, denied path 10, unavailable I/O 5, end in fragment 4,
invalid UTF-8 4. These policy/errors need separate dispositions; do not relax
filesystem policy merely to turn them green.

On the same 834 resolved decks, source-only references total 19,335 (3,216 bound,
15,791 deferred). Expanded references total 19,971 (19,541 bound, six deferred,
328 unsupported, 66 missing, 30 invalid). Expansion adds references, so these
reference denominators differ. Symbolic binding is not execution readiness.
Original unexpanded observations across all valid decks remain 76,632 references.

Next priorities: classify conditional resource selection; inspect the 15 resource
syntax and 14 parse-budget refusals against the generated grammar; reconcile
missing/invalid model references; then extend the bridge by explicit existing
engine model/analysis contracts with named oracles. Retain denied-path and
encoding policy distinctions. Do not resume sample-by-sample speculative support.

## Mutation disposition

The first Stryker.NET 5.0.0 Basic adapter campaign produced 57 killed, 17 surviving,
seven compile errors and six block-filter ignores. Added numerical/budget and
header regressions followed actual red failures. The full follow-up produced
70 killed, four surviving, seven compile errors and six ignores; no timeout or
uncovered mutants. This full run used the 31-test snapshot, before the last
arbitrary-input and public-result tests. Exact-span confirmation is recorded in
`evidence/simulation-bridge-verification.json`; it is not a new full score.

Two reviewed survivors are observationally equivalent under current invariants:
frame offset `frames-1` to `frames+1` remains a unique private key with a bounded
frame count; changing `!Supported || Value == null` to AND preserves behavior
because extraction always publishes `Value == null` exactly when unsupported.
Revisit both if these internal invariants change. Public result consistency and
one-node budget validation received explicit new contract tests.

## Provenance, partial trials and reproduction

Bounded companions: `evidence/simulation-bridge-{oracles,corpus,coverage,
requirements,engineering,verification}.json` and the gate burndown SVG. Reports
identify source hashes beyond the dirty base commit, loaded managed assembly
hashes and the actual native executable hashes. Corpus rows retain every resolved
resource path/section/SHA256 locally. Raw artifacts remain under
`E:\Artifacts\Spice-simulation-bridge-20261101T162640Z`.

Retained unsuccessful trials include unavailable executable at initial startup,
wrong Faer wire key, signed-DC extraction refusal, a source-changed corpus trial
(correctly rejected by provenance guard), and empty mutation-span selections.
None is counted as successful qualification.

See `docs/semantic-frontend.md` for checked-directory setup/build commands and
artifact redirection. The mutation spans in Stryker.NET are character offsets,
not line numbers ([upstream configuration](https://stryker-mutator.io/docs/stryker-net/configuration/)).
New Frontend dependency references are Syntax + Client; there is no added parser,
solver or product dependency on ngspice. File-app helpers executed managed rather
than NativeAOT. No CI, new endurance soak, Linux/GPU or production qualification
is claimed.

Completed corpus results and prioritized fix checklist:
[GitHub issue #15](https://github.com/jeffadickson/Spice/issues/15).
Implementation checkpoint: `6487691` (executed source hashes identify the dirty
`344cb5b` build snapshots; the native Rust source was unchanged).

Exact character-span follow-up completed: **22 killed, zero survivors**, with
seven compile errors and 58 out-of-span ignores. Prior survivor IDs 0 and 20
were explicitly killed. This selected run is not a replacement full mutation
score. The remaining full-run candidates 46 and 64 have the equivalent-invariant
dispositions above; seven compile errors and six original block-filter ignores
remain unavailable mutation evidence.
