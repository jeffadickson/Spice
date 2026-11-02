# TABLE grammar and actual numerical status — 2026-11-01 Pacific

The fresh numerical corpus run executed selected analyses for **3 of 910 files**:
one passed the complete selected comparison profile and two remain partial.
This is the older Rust numerical importer, not the generated frontend corpus.
**920/920 root syntax acceptance does not mean 920 files simulate.** The generated
frontend has four named R/V/I DC fixtures; a whole-corpus execution count through
that frontend remains unmeasured. TABLE recognition adds no simulation support.

| Profile | Actual result | Scope |
| --- | --- | --- |
| Numerical corpus | 1 compared pass, 2 partial, 907 refused/unsupported | 910 unique tracked inputs, existing numerical importer |
| Generated root syntax | 920 accepted | 928 primary rows, four explicit legacy replays, eight adjudicated exclusions |
| Resource resolution | 890 resolved, 30 refused | Same 920-root cohort |
| Reached vendor recognition | 13 accepted, 1 refused | Fourteen explicit library files, no execution |
| Generated DC bridge | 4 fixtures, 12 comparisons, 24 Rust solves pass | LU/QR/Faer, analytical/ngspice/independent typed requests |
| Repeated numerical benchmark | 5 named cases pass | Divider, RC AC/transient, two nonlinear clamps; one warmup and three measured repeats |
| TABLE native controls | 10 accepted and numerically matched | Native ngspice versus analytical outputs; no Rust TABLE implementation |

[Parser/resource results and fix checklist: issue #19](https://github.com/jeffadickson/Spice/issues/19).

## Numerical evidence and the next shared blockers

The completed numerical matrix is run `20261102T015858Z-06e9dc72`, lasting
958.683 seconds. It has 666 unsupported, 230 importer rejections, 11 input/resource
refusals, two partial comparisons and one compared pass; no unfinished inputs or
observed numerical mismatches. All 910 input hashes match the prior matrix.

- `tests/resistance/res_simple.cir`: selected configured transient comparison passes.
- `tests/filters/lowpass.cir`: selected AC passes; additional declared analyses remain unexecuted.
- `tests/resistance/res_partition.cir`: selected AC passes; additional declared analyses remain unexecuted.

All three use LU and Faer. They compare 460 signal/coordinate/provider observations
with zero failed points. These are repeated observations, not 460 circuits or
complete waveform guarantees. Bounds remain explicit: voltage absolute 1e-5 V,
current absolute 1e-9 A, relative 1e-4; sampling/axis/interpolation policy is in the
bounded report. The fully compared file is a selected-profile pass, not universal
SPICE conformance. Results/fix checklist: [issue #18](https://github.com/jeffadickson/Spice/issues/18).

The largest exact diagnostic group affects 368 files: the bounded importer accepts
only D and constant-R model families. Another 117 report unsupported element/directive
at line 1; these are importer diagnostics, not proof the valid input is wrong.
Further common groups include unsupported bounded model kind (59), primitive
header shape (53), empty continuation (34) and unsupported options (32).

Next numerical integration should use the generated frontend in the corpus runner,
first measuring lowering/refusals and qualifying supported primitive DC decks,
then adding analyses/model contracts with independent oracles. Re-reading accepted
syntax through the older narrow importer will not measure the new frontend's
capabilities. Its whole-corpus numerical support is explicitly unmeasured today.
Every completed gap checkpoint must repeat the numerical matrix and named ngspice
checks and lead with unique files fully/partially compared and unavailable work.

The five-case repeated benchmark passed correctness checks. Timings are retained
per case/provider as process-launch-inclusive medians/ranges. The Rust executable
is a reused Windows debug build; ngspice is separately built, other jobs ran
concurrently, and peak-memory observations were unavailable. These timings are
not a release speed ranking or an isolated numerical-kernel benchmark. Rust source,
Cargo.toml and Cargo.lock are unchanged from its prior `344cb5b` build checkpoint;
executable hashes remain explicit. No fresh Rust build/coverage is claimed.

## Implemented grammar gap

ANTLR remains the parser. Contextual E/G `TABLE {expression}` tails recognize the
optional equals separator and continued coordinates. TABLE is not a global reserved
word: an ordinary control node or T device named `table` keeps its spelling.
The observer records `source_form:table` with the original physical source range.

Native source `src/frontend/inpcom.c` TABLE translation (around lines 6245 onward)
uses `gettok_node` for coordinates: commas and parentheses act as separators.
Executed controls confirmed both MCP6041 wrapper spellings and a missing closing
pair parenthesis are accepted by ngspice. The generated tableCoordinate/tablePair/
tableSeparator productions model this locally, requiring a positive even count.
Ordinary controlling expressions and unrelated expressions retain strict delimiters.
No handwritten lexer, source rewrite, new dependency or interpolation model was added.

Native baseline: one of six frontend probes accepted while all six native values
matched. Four later legacy-wrapper/bare-coordinate controls bring delivery to ten
of ten frontend/native/analytical matches. Reached library acceptance rises from
3/14 to 13/14. TL072.301 line 43 remains refused for terminal U+001A; inferred Draft
PRS-EOF-001 requires explicit native/platform qualification, not arbitrary control
character removal. The previous scaled LIMIT discrepancy remains unresolved and
was not retested or silently changed by this TABLE slice.

## Corpus, tests and limitations

The primary generated replay completed 928 rows in 467.100 seconds: 916 accepted,
four strict encoding refusals and eight pre-adjudicated invalid/non-deck exclusions.
Four unique legacy replays produce the 920/920 gate; 18 repeated repository rows
are not counted twice. All 928 primary root hashes match the previous replay.
Resources improve 879 to 890, leaving denied_path 13, resource_io 5,
end_in_fragment 4, invalid_utf8 4, conditional_resources 3 and resource_syntax 1.
Current source/corpus are hash-pinned dirty-base executions, not clean release builds.
Failed historical resource graphs did not hash every transitive source, so root
hash equality is not proof of identical historical transitive content.

Within the same new 890-deck resolved cohort, original references total 62,067
and expanded references total 64,422. Expansion yields 63,274 bound, 938 unsupported,
86 deferred, 66 missing, 30 invalid and 28 ambiguous references. These include
unused definitions, have differing denominators, and are not simulation counts.
Two earlier parser trials were intentionally interrupted after review/native
findings; raw logs remain local and those trials are not completed-run evidence.

Delivery suites pass **406 Syntax + 35 Frontend + 95 Benchmarks = 536 tests**,
with one explicit exFAT link-fixture skip. Nineteen syntax cases and two frontend
TABLE-refusal cases were added. Red evidence includes five initial TABLE cases,
two name-collision regressions and six native legacy-coordinate cases. FsCheck
runs 128 TABLE prediction/range cases with replay `906,20261101`, alongside existing
semantic, malformed-input and numerical properties. No separate coverage-guided
fuzz or soak was started.

The first benchmark test run had eight missing transport-fixture executable
failures after artifact relocation. GetTargetPath-based fixture lookup fixes the
source/bin assumption; the final full 95-case suite passes. Two intermediate
project-file attempts failed before tests (XML comment and wildcard expansion),
were corrected, and are retained in local logs. All bulk artifacts remain on E.

Stryker.NET 5.0.0 Basic produced no viable selected mutants: 150 compile errors,
609 ignores, no score. Standard follow-up killed all three viable selected observer
mutants (751-753), with 683 compile errors and 1056 ignores recorded separately.
The selected FeatureExtraction source is byte-identical to delivery; the snapshot
uses the earlier strict TABLE grammar and 404 tests. This is observer verification,
not final generated grammar/ATN or whole-assembly mutation qualification. A first
Standard CLI attempt omitted its test-project argument and performed no analysis.

Coverlet 10.1.0's incomplete handwritten Syntax/Frontend subset measures 812/824
lines (98.5%) and 976/1116 branches (87.4%). Captured local functions/callbacks remain
omitted, including resolver/bridge core functions. Handwritten benchmark tests
measure 413/811 lines (50.9%) and 434/942 branches (46.0%); command entrypoints have
zero collector coverage, despite separate real CLI execution. Generated serializers
are excluded from those benchmark totals. Generated parser-method coverage is
retained separately and does not cover serialized lexer ATN transitions or complete
productions. No new Rust/native-oracle/file-app/Linux/GPU coverage is claimed.

Real StrictDoc parses 21 documents, 325 IDs and 313 parent relations. PRS-TABLE-001,
PRS-TABLE-002 and SEM-EVIDENCE-001 remain Draft with bounded implementation/evidence;
PRS-EOF-001 is inferred/unimplemented Draft. Sphinx builds with warnings as errors
and compiled API XML. AI read-only source review caught the identifier collisions
and an unbounded pre-check vendor read; fixes include red name tests and a bounded
stream read plus executed 5 MiB refusal fixture. This is not external human review.

Implementation commits: `bf11350` and `14e541b`. Final verification checked 265 source/API fingerprint entries across reports, with 380 compiled API XML entries in the Sphinx build.

## Reproduction and handoff

Use the checked-directory PowerShell setup in `docs/semantic-frontend.md`. Raw files
are in `E:\Artifacts\Spice-table-20261102T015724Z`; only bounded reports are in Git.
The UTC run directory date is November 2; the local run date is November 1 Pacific.
Set TEMP/TMP to the existing unique artifact directory. Build Syntax/Frontend tests
with their separate `--artifacts-path`, `GrammarOutput` and pinned `AntlrJar` paths;
collect Coverlet output in separate results directories. The helper
`scripts/QualifyTables.cs` accepts checkout, ngspice executable, fresh output directory
and optional corpus root; its syntax gate and numerical status are separate fields.
Check both for numerical qualification. Its vendor profile only recognizes files.

Run `Run-SyntaxCorpus.ps1 -CollectSemantics -RequireValidSamples` with the explicit
corpus/permitted roots, four workers, 180-second workers, a 30-minute budget and
4 MiB/two-million-token limits. Run the Benchmark helper with `--corpus-root`, explicit
`--engine`, `--ngspice`, E: `--output`, 1800-second budget and issue publication;
its raw files now follow that output. For repeated cases select one warmup, three
iterations and an E: `--report`; raw benchmark files follow the report parent.
The generated DC bridge helper remains a separate four-fixture regression.

Continue with explicit DOS EOF qualification and generated-frontend corpus lowering/
simulation measurement, then model/analysis contracts prioritized by observed
blockers. Preserve unrelated main sheet-resistor work. No CI, long campaign, full
SPICE, GPU, Linux or production qualification was performed or claimed here.
