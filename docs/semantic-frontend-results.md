# Semantic frontend verification checkpoint

The three bounded profiles are implemented: source-located instance/port binding,
finite parameter environments and explicit include/library resolution. They do
not replace the Rust importer, flatten topology or implement model equations.
See [contracts/reproduction](semantic-frontend.md) and
[bounded evidence](../evidence/semantic-frontend-verification.json).

## Corpus acceptance and observed gaps

The final frozen-source campaign attempted **928 inputs**. All **920 required
valid inputs** parse, including four explicitly adjudicated legacy-encoding
replays. Eight exclusions remain explicit; the UTF-8 primary accepted 916 and
refused four legacy byte streams. The replay also rechecks repository fixtures;
aggregation deduplicates scope/path keys. Two preliminary campaigns were stopped
for review/operator corrections and are not passing/completed evidence.

Across the 920 accepted observations:

| Stage | Actual evidence | Limits |
|---|---|---|
| References | 76632 observed;3220 bound,73070 deferred,264 unsupported,48 missing,30 invalid | X/D/Q/M/J/S/W profiles; unused declarations included; no active topology |
| Input reference profiles |190 fully bound observed-reference sets;351 incomplete;379 have no observed references | Independent denominator 920; no circuit/solve claim |
| Port maps |10019 ordered symbolic connections | No hierarchy/global/ground lowering |
| Root parameters |35 evaluated roots,140 values;232 deferred;7 unsupported expressions;646 with no root parameters observed | Scope evaluation only; model/device values not lowered |
| Resource resolution | Filesystem/source-map/negative fixtures and original-versus-expanded ngspice checks | Corpus observer deliberately performs no file loading |

The dominant measured blocker is provisional resource/conditional context:
73070 references, including71610 X references. Global conservative deferral can
hold otherwise observed local definitions provisional; it is not proof all those
references are independently missing. Next priority is a separately bounded bulk
resource-resolution/rebinding profile with explicit roots, then narrower
conditional-scope semantics. Do not start thousands of isolated model patches
from these aggregate counts.

The 249 unsupported model contracts and 15 opaque headers need family/terminal/
flag minimization before expanding profiles. Missing 48 and arity 30 outcomes are
profile findings requiring named adjudication, not a verdict that upstream decks
are invalid. Four duplicate-instance graph diagnostics remain visible. Seven
root-expression gaps concern the Monte-Carlo/measure/numparam string/vector and
XSPICE data examples (including mirrored copies); random, user-function and
non-scalar dialects remain explicit capabilities.

[Snapshot chart](assessments/semantic-frontend-burndown.svg) and
[corpus evidence](../evidence/semantic-frontend-corpus.json) retain separate
stage denominators. This is a semantic-obligation snapshot, not numerical v4
burndown or a historical mutation-score trend.

## Executed verification

- 339 managed tests passed; one symbolic-link fixture explicitly skipped because
  E: is exFAT. FsCheck adds 384 configured cases with replays 801,20261101;
  802,20261101;803,20261101. Existing malformed-input/property suites also ran.
- Handwritten Simulator.Syntax coverage:739/750 lines (98.5%) and883/1016 branches
  (86.9%). Generated ANTLR code is excluded from this number; CLI/wrapper/helper
  coverage and Linux execution are unmeasured. Coverage does not establish correctness.
- 14 named ngspice checks pass: numeric scaling/operator/default profiles, signed
  port ordering, nested overrides, zero-port invocation, arity/missing-reference
  rejections, and original/expanded resource decks. Source/executable/log hashes
  are retained. Positive scalar comparisons and expected failures are not 14
  independent complete-circuit conformance groups.
- Real StrictDoc 0.30.4 parses 20 documents, 315 stable IDs and 303 parent links.
  Requirements remain Draft. Sphinx 9.1.0 builds with warnings as errors and projects
  358 compiled Client/Syntax XML documentation entries. The managed source example
  runs successfully; no NativeAOT or performance claim follows.

## Mutation outcomes and remaining work

Stryker.NET 5.0.0 Basic ran on disposable E: snapshots. These are separate scopes:

| Snapshot/profile | Killed | Survived | Uncovered | Compile errors | Ignored |
|---|---:|---:|---:|---:|---:|
| Initial binding files |33|10|5|18|8|
| Strengthened binding files |41|4|3|18|8|
| Initial parameter file |50|25|22|16|13|
| Initial resource file |58|30|5|34|0|
| Narrow binding classification replay |1|0|0|4|43|
| Narrow parameter ownership/budget replay |1|1|0|16|111|
| Narrow resource EOF/flat-section replay |17|19|6|35|90|

Counts above are for the selected files; the JSON also retains the larger
framework instrumentation universe. Ignored includes source-span exclusions;
compile errors are invalid instrumentation, never detected mutants. No timeout
was reported. Do not sum rows, compare different scopes as a score trend or call
remaining survivors/uncovered branches passing. New operator/negative tests are
later than some snapshots; exact source hashes and open identities remain in the
ledger, with no broad closure inferred from inspection. The budget boundary and
resource clipping/defensive branches need further targeted evidence. Link/race
coverage is unavailable here.

## Review and handoff

A fresh separate AI-agent source review (not outside human experts) found lost
nested overrides, incorrect dependency ranges, foreign-binding ownership and
nested-library scope loss. Each was reproduced by a failing test and corrected.
Independent ngspice checks also exposed unary/power, AND/OR and conditional
associativity gaps; ANTLR productions were corrected. Final source review reported
no remaining blockers; raw execution evidence remains separate from inspection.

Next delivery: resource-backed corpus rebinding, then the supported typed Rust
bridge and named primitive-equivalence oracles. Model families/LEVELs, active
conditionals, global-node semantics, flattened topology, whole-SPICE/GPU/production
qualification remain open. Preserve the unrelated sheet-resistor work and original
Rust mutation unavailable categories. No CI or new endurance soak was dispatched.

Results/fix checklist: [GitHub issue #14](https://github.com/jeffadickson/Spice/issues/14).
