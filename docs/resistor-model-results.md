# Bounded generated resistor-model preparation

The helper prepares ordinary scalar values, constant R models and explicit
sheet geometry into one positive finite resistance in ohms. It adds no native
model kernel, dependency or bridge changes. The parent worktree owns public
selection integration. The canonical main checkout's unknown native sheet
resistor WIP was inspected for context and not edited or staged.

## Reuse and semantics

The existing generated passive grammar retains assignment tails that the old
scalar PrimitiveExtraction did not expose. A generated resistorModelProfile
now recognizes an identifier followed by assignment contexts through the shared
semantic-entry token adapter. PrimitiveSyntax retains those source-located
fields without broadening its existing Supported flag. No card Split, second
lexer, string parameter parser or new expression evaluator was introduced.
Existing ParameterEvaluator handles finite expressions and retained environments.
Custom code is limited to scalar precedence, model allowlists and geometry.

Primary source is ngspice commit e8285746c73a8229cf922487c8ccfcf6bea61bce:
[resistor temperature/conductance update](https://github.com/ngspice/ngspice/blob/e8285746c73a8229cf922487c8ccfcf6bea61bce/src/spicelib/devices/res/restemp.c)
and [resistor defaults](https://github.com/ngspice/ngspice/blob/e8285746c73a8229cf922487c8ccfcf6bea61bce/src/spicelib/devices/res/ressetup.c).
Sheet resistance is RSH*(L-2*SHORT)/(W-2*NARROW), with zero etch defaults.
This subset requires explicit positive L/W and positive effective dimensions;
native model L/W defaults are intentionally outside this profile. Finite
negative etching is allowed when the effective dimensions remain positive,
as verified by a native 300-ohm preflight. Model parameters use declaration
model.ScopeId; geometry uses actual instance overrides retained by ForInstance.

Native name-collision probes establish ordinary precedence: a bare rm with
.param rm=1200 and .model rm R(R=500) gives 1200 ohms, as does {rm}; a numeric
1k with a same-named model stays 1000 ohms. Only an unresolved bare identifier
enters model lookup. Constant models allow only positive R and no instance
overrides; sheet models allow RSH/SHORT/NARROW and instance L/W. Duplicates,
unknown or opaque terms, invalid bindings/environments, zero/nonpositive or
nonfinite geometry, temperature, noise, AC resistance and multiplicity refuse
atomically. Combined constant-plus-sheet models are outside this bounded profile.

## Executed verification

Before implementation the new tests failed compilation because
ResistorPreparation did not exist. After implementation, 30 focused tests
passed with zero skips. This includes 29 pure tests and one original-card
native oracle. Tests cover scalar/model collisions, units, invalid/duplicate
parameters, missing values, nonpositive geometry, effective-dimension zeros,
overflow, declaration scope under child shadowing, actual W call override,
foreign objects and bare CR source ranges. An explicit discovered Fact runs
128 FsCheck sheet-geometry cases with new Rnd(20261106UL), using its generated
valid gamma. No even explicit replay gamma or PropertyAttribute is used.

Original R/model fixtures produce 500-ohm constant, 600-ohm sheet and 1200-ohm
etched resistances. Each divider is compared with native ngspice in OP,
100 Hz complex AC and a ramp transient, on both LU and Faer typed-element
requests: 78 output comparisons at absolute 1e-9 and independent analytical
divider references. Exact original decks, requests, responses and summaries
are linked through evidence/resistor-model.json. Emission is test-only;
parent public bridge selection tests remain a separate verification scope.

Before the underflow followup, full frontend regression passed 246, failed 0, skipped 13 opt-in tests,
259 total. Syntax regression passed 406, failed 0, skipped one unsupported
exFAT file-link case, 407 total. One existing xUnit2013 warning is unrelated.
Final Coverlet measured lowering 49/49 lines and 86/92 branches, generated-token
metadata adapter 7/7 lines and 2/4 branches. Generated ANTLR code and unrelated
shared extraction glue are not included in those focused coverage claims.
Some defensive finite/binding/parser-budget paths remain unmeasured.

Stryker.NET 5.0.0 executed four selected geometry arithmetic mutants, all
killed with no selected survivors or timeouts. The mutants change the short
reduction subtraction/multiplication and the final ratio/multiplication. It
discovered all 259 tests. Of 540 generated project mutants, 455 were excluded
by span filters and 81 had compile errors outside those spans. These are not
tested mutants; the bounded 4/4 result is not a project-wide mutation score.
The disposable snapshot and exact replacements are recorded in the JSON.

Source review then identified the native sheet-branch predicate L*W*RSH>0.
For L=W=1e-200 and RSH=100, its first product underflows; native ngspice warns
and substitutes 1 mOhm while a ratio-only helper would return 100 ohms. The
new negative regression was observed red (18 passed/1 failed), then a native
predicate guard made the helper refuse this profile explicitly. A later
arithmetic mutation exposed the separate final-multiplier underflow boundary:
L=W=1e-160 and RSH=1e-100 also produces the native fallback. That original
native deck is preserved and an additional negative test challenges the guard.
No 1 mOhm fallback or approximation is emitted by this explicit sheet subset.

The full frozen scan checked all 928 hashes with no changes, accepted 924
strict-UTF8 roots, refused 34 during resource/syntax preparation and skipped
four invalid UTF8 roots: 890 expanded roots. It found 16 resolved generated
R-model reference candidates in nine roots. Eleven constant-model instances
prepare across four roots; no sheet-model corpus instance prepares. Five
refusals comprise two model-parameter profiles, one instance-parameter profile,
one missing parameter and one invalid parameter environment. These counts
include repeated and inactive lexical sources and default lexical environments.
They are not full-circuit execution counts. Changed numerical admission is
rechecked on all nine candidate roots with the same frozen hashes; the original
full scan's metadata/extraction code is unchanged by the underflow guard.
After the guard change, an eight-mutant run killed seven and left one guard
multiplier mutant alive. The native second-multiplier underflow witness above
closed that specific test gap: the four guard mutants were rerun and all
killed. The four formula mutants were already killed on the identical final
production helper hash. These campaigns overlap; they are not twelve unique
mutants or a single eight-mutant run after the additive test. The first run
generated 544 mutants, excluding 455 and rejecting 81 compile errors outside
scope; the guard rerun excluded 459 and rejected 81. Final full frontend counts
are 248 passed, 0 failed, 13 skipped, 261 total; final native-focused counts are 32
passed, 0 failed, 0 skipped. Candidate-root revalidation preserved all 16 candidate
outcomes and all 928 input hashes, under the final helper. The inspector supports
an explicitly labelled prior-census root filter, retaining full input hash
checks; any metadata-universe change requires another complete scan.

Real StrictDoc 0.30.4 parsed 27 documents, 349 unique IDs and 338 resolved
parent relations. SEM-RESISTOR-MODEL-001 remains Draft. All raw artifacts stay
under E:/Artifacts/spice/behavior-xspice-nov2/resistor-model; no CI or pushes ran.

## Reproduction

Check the checkout, artifact parent and pinned jar directories before use.
Use distinct supported --artifacts-path, GrammarOutput and results-directory
paths for each build/coverage/mutation run. The evidence JSON records the
exact tested source hashes and report locations. Set SPICE_CORPUS_ENGINE,
SPICE_CORPUS_NGSPICE and an existing SPICE_CORPUS_TEST_ROOT for the opt-in
FullyQualifiedName~ResistorModel tests. The inspector uses the existing frozen
inventory and resource policy and writes only a fresh output JSON.
