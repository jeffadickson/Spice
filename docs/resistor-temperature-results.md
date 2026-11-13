# Resistor temperature checkpoint

The unchanged original `regression/temper/temper-res-1.cir` now executes its
complete ten-point DC temperature sweep and original assertions. All six mapped
voltage/current signals agree with stock ngspice at all ten points: zero voltage
error and at most 1.735e-18 A current error. The existing comparison bounds remain
1e-6 V, 1e-9 A and relative 1e-4. Each original reporting error is
2.220446049250313e-16 against the unchanged 1e-14 assertion, success_count is 1,
and both our complete reporting program and the separate full original ngspice
execution exit 0 with INFO success. This adds one original deck; the Sol campaign
total is two. The authored example is separately verified and is not counted.

The requested resistor compiler now prepares TC1/TC2/TCE using the existing
per-Celsius circuit compilation path. Instance coefficients override model
coefficients independently; explicit TCE, including zero, overrides the polynomial.
Model TNOM defaults to 27 C. The same factor applies to DC and explicit AC
resistance before scaling/multiplicity; a regression checks both while preserving
the DC preparation. Invalid factors retain an actionable temperature-factor error
rather than falling through to an unrelated invalid-resistance diagnosis.

The generated control lexer admits word AND/OR only in its control semantic mode,
with existing precedence and real-vector rules. The existing evaluator uses
Math.Pow for real ^/** with scalar broadcasting; domain, overflow and nonreal
inputs refuse. No expression parser, product dependency or Rust device model was
added. Ordinary deck identifiers retain their existing spellings. Instance
TEMP/DTEMP, higher TC orders, arbitrary temperature directives, noise and general
complex power remain outside this bounded profile.

Reuse follows the retained user decision in docs/spicesharp-evaluation.md.
Existing generated metadata, ParameterEvaluator, per-point requested compilation
and finite result validation remain shared. Pinned RSpice model_resolution/
resistors.rs guided coefficient precedence and units. The supplementary current
[ngspice conductance implementation](https://raw.githubusercontent.com/imr/ngspice/master/src/spicelib/devices/res/restemp.c)
confirms applying the factor to explicit AC resistance too. No third-party source
was copied. Source pin, input and guidance hashes are in the evidence JSON.

Final restored verification: syntax 518 passed/1 skipped; frontend 875 passed/40
skipped; 65 documents parsed using real StrictDoc 0.30.4. The suites include 22
coefficient cases, 13 reporting cases, 128 FsCheck temperature properties
(5004,2026111301) and 512 bounded malformed-text cases (362,20261031). The latter
are generative fuzzing, not a coverage-guided fuzz campaign. Earlier red tests,
a temporary compiler variable-name failure and the intermediate power-refused
original program are preserved as failed attempts, not agreements.

Coverlet syntax scope: 7075/10925 lines and 2652/4633 branches. Frontend scope:
21182/26987 lines and 6279/9992 branches. These include generated and referenced
managed code and are different scopes, not whole-product scores. The lowering
class has 98/98 measured lines and 177/196 branches; defensive paths remain
unmeasured. The syntax skip is the unavailable filesystem-link prerequisite;
frontend skips retain their external engine/provider prerequisites. The explicit
original deck replay is independent evidence despite those opt-in suite skips.
No Rust build or native coverage was added; the certified behavioral engine
snapshot is hashed separately from the current source.

Pinned Stryker.NET 5.0.0 ran on an isolated E: snapshot with one worker. The
coefficient span generated 20 candidates: 18 killed, 2 compile errors, no
survivors or timeouts. The two invalid conditional replacements remove the tce
out-variable declaration. Whole instrumentation also records 2633 compile errors
and 3033 ignored candidates; these are not test-detected kills or a project-wide
mutation result. An earlier run had 15 kills and 3 survivors; diagnostic-provenance
regressions detect all three on replay. Two additional manual reporting mutants
(power operand swap and OR changed to AND) were killed by five and three tests.
Production source hashes were checked unchanged/restored, then both full suites
were rerun. The first Stryker attempt's missing snapshot ANTLR setup is retained.

The working checkout was clean at 60574be before this change; canonical main and
unknown files were preserved. All build/package scratch, TEMP/TMP, coverage,
Stryker source copies, tool installation and raw reports are under E: paths.
No custom RAM admission or memory caps were applied; jobs were serialized with
process-tree deadlines (600 seconds for Stryker/final coordinator, 300 for managed
checks, 180 for original replay/documentation). Bulk reproduction scripts and
reports are in E:/Artifacts/Spice-sol-temperres-60574be-20261113T0705; execute
FinalVerify.ps1 through Timed.ps1 there, with the retained pinned E: package cache,
corpus and native executable hashes in evidence/resistor-temperature.json.

No push, merge, CI, schematic or VeloSim changes were made. MOS memory's stock
reference mismatch remains open and is not counted as a completed original.
