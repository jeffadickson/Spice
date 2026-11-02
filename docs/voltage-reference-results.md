# Voltage-reference grammar results — 2026-11-01 Pacific

Implementation checkpoint: `2b997ef`. ANTLR now recognizes node spellings in
voltage-reference expressions, including `V(VC+,VC-)`, `V(REF+,REF-)`, bus/path
names, numeric-looking nodes and quoted names. It preserves physical source
ranges and handles continuations before the opening parenthesis and inside the
argument list. Ordinary arithmetic remains strict. Composite reference openers
count toward the existing group budget. Parameter evaluation still refuses
signal references; no Rust behavioral-source support was added.

The implementation reuses generated lexical modes and a dedicated parser
production. No handwritten lexer, source rewriting or new dependency was added.
ngspice source inspection (`src/frontend/parse.c`, `parse-bison.y`) and actual
named native probes informed the bounded profile. The approved ANTLR/Rust/reuse
choices remain unchanged.

## Completed profiles

The root replay completed 928 inputs in 359.103 seconds: 916 accepted structures,
four encoding refusals and eight prior adjudicated exclusions. Four distinct
Latin-1 replays bring the valid root gate to **920/920**; the 18 repeated
repository rows are not counted twice. Root acceptance does not certify reached
vendor libraries or numerical execution.

Resource resolution improves **876 → 879**, leaving **41 refusals**: 12 syntax,
13 denied paths, five unavailable I/O, four fragment ends, four strict UTF-8
legacy-resource refusals and three conditional-profile refusals. Same root input
hashes and inventory as the previous run; no speedup claim. The corpus checkout
is dirty, and current reached-file fingerprints are recorded. The three newly
resolved inputs are `555-timer-2.cir` in `examples/p-to-n-examples`,
`examples/probe` and `paranoia_parallel/examples/pton`.

For the same 879 resolved decks, references total 62,010 before expansion and
63,938 afterward. Bound references rise from 3,216 to 62,913; deferred references
fall from 58,466 to 86. Expanded outcomes also include 815 unsupported, 28
ambiguous, 66 missing and 30 invalid references. Expansion changes denominators;
these observations are not simulation counts or directly comparable cohort
coverage to the previous 876-deck profile.

Direct recognition of 14 previously failing reached library files now accepts
three. Eleven still fail for newly exposed TABLE syntax/grouping or DOS EOF
causes. Shared next gap **PRS-TABLE-001** is inferred and unimplemented: E/G TABLE,
optional equals and continued coordinate pairs. MCP6041 non-balanced wrappers
need independent adjudication; TL072.301 retains a terminal control-Z profile
question. None is declared invalid solely from our parser's rejection.

## Native syntax versus numeric evidence

All **11 named inputs** are structurally accepted by the frontend and complete
native ngspice operating points with finite output and no detected native errors.
Ten match independently chosen analytical voltages. The scaled
`LIMIT(.5*V(VC+,VC-),-1,1)` control does **not**: expected 1, native result 0.5.
Default and explicit PSpice-mode trials preserve this discrepancy. Constant
LIMIT, basic scaling and min/max controls match; unscaled differential LIMIT
also has an unexpected result. Cause remains unresolved. The helper gates syntax
agreement separately and reports `numerical_status=mismatch_observed`; it does
not turn the mismatched control into a numeric pass.

The first baseline accepted only three of ten frontend syntax probes while
ngspice accepted all ten. A later continued-opener probe adds the eleventh case.
The helper can also run the separate reached-library recognition profile with an
explicit corpus root. Vendor files are never executed by that profile.

The existing R/V/I DC bridge remains independently qualified on its selected
fixtures: **12 comparisons / 24 Rust solves** passed against analytical values,
ngspice and separate typed requests across LU, QR and Faer. That regression does
not qualify LIMIT, TABLE, behavioral sources or arbitrary vendor models.

## Tests, mutation, coverage and requirements

- 387 Syntax/semantic tests and 33 Frontend tests passed; one link fixture skipped
  on the exFAT artifact volume. Focused reference regressions failed 14 cases
  before implementation. Two continued-opener cases failed before the review fix.
- FsCheck runs 128 reference prediction/range cases with replay `905,20261101`;
  existing malformed-input and semantic properties also ran. No separate
  coverage-guided fuzz or long soak was started.
- Initial scoped Stryker.NET Basic run: eight killed, one survived, 149
  assembly-level generation compile errors, 600 out-of-span ignores. Interleaved
  nested-call budget tests then killed all **eight guard-span mutants**, including
  survivor 76. The observer mutant killed in the first selection is outside that
  follow-up. Final follow-up has 149 compile errors and 601 ignores. Those are
  unavailable/unselected evidence, not kills or a whole-assembly/grammar score.
- Handwritten coverage is **incomplete**: the collector-reported subset is
  811/823 lines (98.5%) and 974/1114 branches (87.4%). Captured core functions and
  callbacks remain omitted. Generated C# lexer/parser and reference-method metrics
  are recorded separately; they do not measure serialized lexer ATN transitions
  or complete grammar coverage. Rust, native oracle and file-app coverage absent.
- Real StrictDoc 0.30.4 parses 21 documents, 322 stable IDs and 310 parent links.
  PRS-REF-001 remains Draft with bounded implementation/evidence; PRS-TABLE-001
  remains an inferred unimplemented Draft. Sphinx 9.1.0 built with warnings as
  errors and compiled API XML documentation.
- AI source review identified the continued-opener gap. Existing LOGICAL_SPACE
  reuse fixed it; follow-up inspection found no further substantive source
  defects. This was source inspection, not an external human review.

## Reproduction and continuation

[Issue #17](https://github.com/jeffadickson/Spice/issues/17) tracks this completed
replay and remaining causes. Bounded evidence is `evidence/voltage-reference-*.json`
and the matching burndown SVG. Raw artifacts remain local under
`E:\Artifacts\Spice-voltage-reference-20261102T003915Z` (UTC directory date;
Pacific run date is November 1). Source and executable hashes identify dirty-base
`884a597` execution independently of the final implementation commit.

Use the checked-directory setup in `docs/semantic-frontend.md`, then run the full
syntax tests with E: artifacts/grammar output and the pinned ANTLR jar. Run
`scripts/QualifyVoltageReferences.cs` with checkout, native ngspice executable,
fresh output directory and optional explicit corpus root. The output contains
separate syntax/native/numeric/library fields. Replay `Run-SyntaxCorpus.ps1`
with semantics, permitted roots, four workers, 180-second workers and a 30-minute
campaign, using 4 MiB input/2 million token limits. Preserve raw fingerprints and
separate stage outcomes. C# file-app helpers ran managed, not NativeAOT.

No whole-corpus numerical, model/behavioral/TABLE/AC/transient text bridge,
Linux/GPU or production qualification is claimed. Next work: the shared TABLE
syntax requirement and independent legacy-input/native-control investigations.
