# Analog summer: measured helper evidence

The bounded helper prepares 2..16 voltage inputs and one voltage output, reusing
existing generated ANTLR port/value rules, ParameterEvaluator and typed VCVS and
constant voltage elements. No dependency, parser replacement or native model
kernel was added. Parent worktree owns actual bridge integration.

## Source and admission contract

The primary source is ngspice commit e8285746c73a8229cf922487c8ccfcf6bea61bce:
[summer interface](https://github.com/ngspice/ngspice/blob/e8285746c73a8229cf922487c8ccfcf6bea61bce/src/xspice/icm/analog/summer/ifspec.ifs)
and [implementation](https://github.com/ngspice/ngspice/blob/e8285746c73a8229cf922487c8ccfcf6bea61bce/src/xspice/icm/analog/summer/cfunc.mod).
These define out_gain*sum(in_gain[i]*(input[i]+in_offset[i]))+out_offset.
Input gain/offset defaults are one/zero per input; output defaults are one/zero.
Explicit arrays must match input count. Original ngspice preflight confirmed
weighted and differential semantics and rejected short and scalar array values.

The subset preserves ordered differential controls and scalar output polarity.
It refuses current/digital/event types, inversions, nested vectors, null ports,
arrays of wrong size, unknown/duplicate parameters, unsupported expressions and
nonfinite arithmetic. Event-domain real_gain is a different model and refused.
Generated scalar-vector recognition requires comma or original-token whitespace
between values; arithmetic members use existing braced expression syntax.
The added compact `[1-2]` and `[1+2]` negative tests were observed failing before
this separator predicate, then passing afterward. No custom token scanner or
array splitter was introduced. Shared port source capture now uses the existing
physical offset map, including bare CR continuation locations.

## Executed verification

At 5e32b0b, the focused suite passed 29/29, including one opt-in native oracle
with three original A/model profiles: defaults, weighted arrays with nonzero
input/output offsets, and differential inputs/output with reference voltages.
Each runs DC, 100 Hz complex AC and ramp transient against native ngspice and
both LU/Faer typed-element requests: 78 comparisons, also checked analytically
at absolute 1e-9. Differential output crosses zero during the ramp. Exact decks,
requests, responses and summaries are in the three summer-oracle folders named
in evidence/xspice-summer.json. Native helper emission is test-only; parent
integration must be verified separately through public selection requests.

The final focused rerun passed 32/32. Three followup tests exercise model lexical scope under a shadowing instance,
foreign environments/instances, and the 16/17 input budget boundary. The final
full frontend run passed 217, failed 0, skipped 12 opt-in tests (229 total).
The independent syntax run passed 406, failed 0, skipped one unsupported exFAT
file-link contract (407 total). One existing xUnit2013 warning remains unrelated.
FsCheck executes explicitly as an ordinary discovered Fact: 128 weighted-sum
cases, Rnd seed 20261104 with its generated valid gamma. This is a bounded
campaign, not generic behavioral-expression or code-model conformance.

Coverlet final scope: summer lowering 49/49 lines, 67/76 branches; generated-token
scalar vector adapter 6/6 lines, 2/4 branches; shared XSPICE extraction 17/17 lines,
32/34 branches. Shared extraction includes existing gain tests. Generated ANTLR
code is not part of these focused claims. Remaining branches include defensive
parser-budget/null, binding/opaque and arithmetic-overflow paths. Raw report
path and hashes are retained in the evidence JSON; coverage is not proof.

Stryker.NET 5.0.0 ran three selected arithmetic mutants, all killed: input
coefficient multiplication changed to division, and the two output-offset
arithmetic operators changed independently. No selected mutant survived or
timed out. Each disposable run generated 484 project mutants; coefficient run
ignored 422 and had 61 compile errors outside its span, offset run ignored 421
and had 61 compile errors outside its span. These exclusions are not tested
mutants and the local 3/3 result is not a whole-project mutation score. The
coefficient snapshot discovered 226 tests; the later offset snapshot discovered
229 after the three pure followup tests. Both contain the same production helper
hash. Exact reports and replacements are in evidence/xspice-summer.json.

The frozen 928-root inspection checks source hashes before candidate selection:
15 literal-selected roots, no changed inputs or resource/syntax refusals, all
12 generated lexical summer instances prepared. Ten have two inputs and
explicit arrays, two have four inputs and omitted arrays. Six example variants
are mirrored under paranoia_parallel. Other code models remain unsupported;
there is no measured increase in complete-circuit execution from this helper.

Real StrictDoc 0.30.4 parsed 26 documents, 348 unique IDs and 337 resolved parent
relations. SEM-XSPICE-SUMMER-001 remains Draft. Native/library build artifacts
and raw logs remain local on E:, not in Git; no CI or pushes were performed.

## Reproduction

From this checkout, use the pinned ANTLR jar and independent fresh E: paths:
`dotnet test dotnet/Simulator.Frontend.Tests/Simulator.Frontend.Tests.csproj
--artifacts-path <build> -p:GrammarOutput=<grammar> -p:AntlrJar=<pinned-jar>
--collect:"XPlat Code Coverage" --results-directory <coverage>`.
For native oracles set SPICE_CORPUS_ENGINE, SPICE_CORPUS_NGSPICE and the existing
SPICE_CORPUS_TEST_ROOT directory, then filter FullyQualifiedName~XspiceSummer.
Check directories before use; CODEX_ARTIFACT_ROOT, TMP and TEMP must use canonical
Windows paths for the repository filesystem tests on this machine.
`dotnet run --file scripts/Inspect-XspiceSummerProfiles.cs -- <checkout>
<ngspice-checkout> <frozen-inventory> <fresh-json>` checks the measured instances.
The inspector needs the same GrammarOutput/AntlrJar properties as tests.
