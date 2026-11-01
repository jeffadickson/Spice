Latest semantic frontend checkpoint: read docs/semantic-frontend.md,
docs/semantic-frontend-results.md and evidence/semantic-frontend-verification.json.
Instance/port binding, bounded parameter environments and opt-in include/library
resolution are implemented as separate operations; no Rust importer replacement,
topology flattening or model/device-value lowering. All920 required valid inputs
parse across928 inventory inputs,4 explicit legacy replays and8 exclusions.
Observed reference counts:76632 total,3220 bound,73070 deferred,264 unsupported,
48 missing,30 invalid. These include unused definitions and are not active solves.
Root parameter profiles:35 evaluated,232 deferred,7 unsupported,646 no observations.
Next: resource-backed bulk rebinding, then the supported typed Rust bridge and
named primitive-equivalence oracles. SEM-BIND/PARAM/RESOURCE-001 remain Draft.
339 tests pass,1 exFAT link-test skip; handwritten line739/750 and branch883/1016.
14 named ngspice checks pass; StrictDoc315 IDs and Sphinx358 XML entries pass.
Stryker snapshots retain survivors/uncovered/invalid/ignored outcomes; no broad
mutation closure. Raw artifacts and all new build/temp/report outputs on E:.
Keep source worktrees on their existing filesystem; do not assume NTFS on exFAT.
Preserve unrelated sheet-resistor work; no CI or new soak. Full SPICE, Linux,
NativeAOT, GPU and production qualification remain unavailable.
Latest native survivor follow-up: see docs/rust-survivors-20261101.md and
evidence/rust-survivors-20261101.json. The November 1 fourteen-mutant cohort
now has 13 actual kills and one retained raw survivor, reviewed equivalent only
to the stable measurement InvalidInput/no-extrapolation contract (context text
and scans differ). Tests changed; production behavior did not. Final Windows
MSVC regression: 459 passed, one explicitly ignored endurance batch; proptest
seed 2026110101, requested cases 256. Focused line coverage 1071/6211 (17.24%),
branch coverage unavailable. StrictDoc: 19 documents, 312 IDs, 300 parents.
Original 99 unviable and 19 unattempted candidates remain unavailable evidence.
No whole-shard score, Linux/.NET/GPU/production qualification or new soak is
claimed. Preserve unrelated sheet-resistor work and engineering-docs evidence.
Latest declaration-catalog checkpoint: read docs/semantic-frontend-plan.md,
docs/declaration-catalog.md and evidence/declaration-catalog.json. All 916
required inputs exported catalogs; 903 have unambiguous observed definitions
and 13 retain conservative duplicate-definition ambiguity. There are 30070
scopes, 29154 subcircuits, 1591 models, 578 scope parameters, 17817 assigned
model parameters, 293 unresolved resources and 189 deferred contexts; 31323
self-lookups had zero inconsistencies. These are not instantiated topology,
parameter values, bound references or simulation support.
DeclarationCatalog provides immutable source-located metadata and nearest-scope
lookup. Parameter candidates remain ordered syntax. Resource/condition deferral
is globally conservative, not a qualified ngspice visibility policy. SEM-CAT-001
and SEM-CAT-002 remain Draft. See declaration-catalog-verification.json and the
mutation ledger for actual verification and remaining gaps.
Next: explicit typed X-call/model-aware instance headers and bounded reference/
port binding with named ngspice fixtures, before expression evaluation, resource
loading or numerical/model expansion. Rust and drop-in solvers remain retained.
Preserve unrelated sheet-resistor work and engineering-docs evidence; no CI or
new soak. Check source/history and scoped hashes before claiming qualification.
Latest capability-inventory checkpoint: read docs/capability-inventory.md,
docs/capability-inventory-results.md and evidence/capability-inventory.json.
The opt-in generated-tree observer inventories all 916 declared valid inputs
from the 924-row scope, with eight hash-pinned exclusions and explicit legacy
replay; 3618 feature kinds are observations, not execution support. Source and
binary identities remain in the bounded report. Registry assessments pin the
committed bc48c64 baseline, excluding unrelated sheet-resistor edits.
Next implementation priority is typed/source-located semantic binding and
parameter/subcircuit elaboration with an explicit external-resource policy,
then representative model/LEVEL/parameter profiles. Includes are not expanded,
control bodies and quoted languages remain opaque, and no numerical model was
added. The new assessment SVG is a baseline, separate from numerical burndowns.
See capability-inventory-verification.json and capability-inventory-mutations.json
for actual tests, scopes, coverage, replay outcomes and remaining mutation gaps.
Preserve unrelated sheet-resistor work and engineering-docs evidence. No CI,
new soak, Linux, GPU, NativeAOT or full SPICE qualification is authorized by this
inventory checkpoint. Check actual git history/status before continuing.
Latest authorized parser-only work: read docs/parser-only-results.md,
docs/parser-only-plan.md, docs/parser-grammar-inventory.md and
evidence/parser-only-corpus.json. Compared ngspice, XDM C++ and SpiceSharpParser C#;
selected pinned ANTLR 4.13.1 generated C# lexer/parser. Rust and solver adapters
are retained. Production numerical importer is not replaced. Syntax parsing never
loads includes, resolves models, invokes solvers or executes control commands.
Final checkpoint campaign: 924/924 attempted (910 upstream plus 14 repository),
493 structurally accepted (479 upstream plus all 14 repository), 181 explicit gaps,
231 current-grammar rejections, seven resource refusals, eight timeouts, four UTF-8
refusals. No numerical support/conformance follows from structural acceptance.
Previous replay had 494 accepted; the final 3-second profile timed out on one
previously accepted input and four deep inputs before their resource checks.
Input/source/DLL hashes and actual corpus revision are retained; tested source
base is 054c3ee plus scoped uncommitted runner bytes. See current Git history for
the final publication checkpoint rather than treating the base as the entire tree.
Sixty tests passed; FsCheck generated 128 valid / 512 malformed cases with replay
192,20261031 / 362,20261031. Handwritten coverage 107/107 lines, 83/90 branches;
generated 2066/2983 lines, 303/454 branches. CLI coverage is separate.
Broad Stryker: 123 killed, 34 survived, three uncovered, one timeout, 28 compile
errors, six ignored. Focused stronger guard replay killed four of five selected
mutants; retain separate scopes. Ledger leaves 33 survivors, two uncovered and
one timeout open; do not call these all passing or dismiss them as equivalent.
StrictDoc intended snapshot: 16 documents / 299 IDs / 287 parents; Sphinx passed.
PRS-001..004 remain Draft. Unrelated sheet-resistor draft and artifacts remain
local/unqualified and must not be swept into parser commits.
Next grammar priorities: XSPICE percent-typed ports/A devices, identifiers,
directive/source expression families and P/Y/U/N device productions. Adjudicate
grammar rejections before calling upstream samples malformed. Expand the bounded
worker profile deliberately when investigating unavailable evidence. Normal
scripts/Test.ps1 includes syntax tests; -SyntaxOnly avoids numerical execution,
and -SyntaxCorpus opts into the upstream sample campaign. Java 11+ is required
for regeneration; scripts/Initialize-SyntaxParser.ps1 verifies the generator.
No fresh Rust numerical, Linux, GPU, NativeAOT or performance qualification.
GitHub's push workflow was found active; use [skip ci] checkpoint messages to
honor the instruction not to dispatch CI. Do not change CI settings or restart
historical iteration quotas. Raw logs/decks/binaries remain local.

Previous user-directed reuse assessment: read docs/assessments/spicesharp-evaluation.html
and docs/spicesharp-evaluation.md. Canonical guidance and project AGENTS now require
reuse-before-build evidence. The user explicitly retains Rust and drop-in solvers;
no production migration or parser replacement was authorized by this assessment.
SpiceSharp 3.2.3 / parser 3.4.1 trial: 488/910 structurally accepted with processors
disabled; nine owned numerical groups have seven analytical passes and two retained
disagreements. Direct API confirms correct RL complex current; parser IR export is
wrong for that case. Diode drift is explained by legacy constants, not silently
accepted under the original bounds. BSIM4 level 54 rejects. Engine has June 2026
maintenance but October 2024 engine release; parser has October 2026 documentation
activity and July 2026 release. See exact snapshots/provenance, not timeless claims.
Next priority is grammar/AST/elaboration plus selective reuse/reference integration,
not more string-card exceptions. Uncommitted sheet-model work remains preserved and
unqualified. No fresh Rust/GPU/Linux/NativeAOT/performance or upstream mutation proof.
Ordered follow-up checkpoint: rollback reverified (78efbe2), V/L/E/H MNA-current
comparisons implemented (c33de35), optional transient syntax/default-stop mutation
gap and UIC classification corrected (cfba1ce). Read docs/corpus-followup-contracts.md,
evidence/ordered-followup-review.md, analysis-cards-verification.json and
resistor-model-verification.json. NGC-CORPUS-008, NGC-037 and NGC-038 remain Draft.
The new named R family accepts only positive constant R=; geometry, temperature,
noise, multiplicity and semiconductor families remain unsupported. Model inventory
counts overlap: NMOS 91 decks, PMOS 82, NPN 77, VDMOS 50. Minimize LEVEL/parameter
profiles before selecting the next semiconductor implementation.
Final corpus run 20261031T055611Z-019f8631 / issue #10: one full selected-profile
pass, two partial, 666 unsupported, 230 import-rejected, 11 input/resource rejected.
909 remain; v4 is a separate comparison scope. Intermediate issue #9 is preserved;
12 UIC cases are now correctly Unsupported rather than malformed scalar input.
Full regression before final UIC diagnostic correction: 440 passed, one ignored.
Final correction: seven parser checks and 95 managed checks passed; final clippy,
StrictDoc (289 IDs) and Sphinx passed. Scoped coverage and exact native mutation
replays are linked in the evidence; no native Rust branch or fresh Linux result.
Current user authorizes normal checkpoint pushes; no CI dispatch/new soak.
Raw artifacts remain local. This does not establish full SPICE/model qualification.
Latest parser checkpoint supersedes the NGC-034 pending note below. NGC-034/035/036
are implemented with bounded tests; remain Draft rather than broad acceptance.
Read docs/ngspice-source-metadata.md and evidence/ngspice-source-metadata-verification.json.
Corrected release suite: 434 passed, one ignored; managed 89 passed. Initial
2mA regression and interrupted debug attempts are preserved honestly. Exact scalar
division replay killed after an initial compiler failure. Corpus issue #8 records
one pass, two partial, 694 unsupported, 202 rejected, 11 resource/input rejected.
No increase in full-pass count. Prioritize shared model/directive/source grammar
blockers using per-deck diagnostics; retain fail-closed behavior and add StrictDoc
requirements for newly discovered missing behavior. Push completed checkpoints;
CI remains undispatched. Raw artifacts local; no full SPICE/GPU/Linux qualification.
Current source-current checkpoint: NGC-CORPUS-005/006/007 and NGC-034 are
recorded in ngspice-compatibility.sdoc. Read corpus-preflight-verification.json,
corpus-current-verification.json and corpus-current-mutation-replay.json.
Preflight CS0165 is reproduced and fixed; the three selected native mutations
are killed. The 378-identity Runner rollback is gone. 104 other CompileError
statuses remain distinct; three exact examples independently reproduce ordinary
C# type/pattern failures. Do not report them as kills or a blanket remaining bug.
Current profile v3 compares node voltages and independent voltage-source currents
with units and dimensional bounds, including exact source#branch aliases.
Issue #6 records one profile pass, two partial, 693 unsupported, 203 rejected,
11 resource/encoding-rejected (909 remaining). res_simple matches all seven
-100 microampere points on both CPU providers. Broader currents remain unqualified.
The 86-test managed suite passes; 57 corpus tests plus an eight-row offline CLI
matrix supply scoped coverage. Four real-oracle facts explicitly skip if tools
are absent. Current-scope mutation identities compose 83 original kills plus
seven new replay kills, five conditional survivors and two uncovered overflow
guards; do not invent a combined whole-run score or close older scopes.
NGC-034 remains unimplemented at this checkpoint; it is the next bounded source
syntax gap. User authorized pushing completed Spice checkpoints in this task.
CI remains undispatched, raw artifacts local, and no full SPICE/GPU/production,
fresh Linux or full-soak qualification is claimed.

Latest bounded checkpoint: 427 Rust tests pass (one ignored), 75 managed tests pass;
StrictDoc parses 280 IDs. Final Reference mutation scope: 46 killed, two conditional
survivors, one liveness timeout; separate scope from the 74-killed policy replay.
Final corpus policy v2: one node-voltage profile pass, two partial, 693 unsupported,
203 import-rejected, 11 resource/encoding rejected; 909 remain. Issue #5 contains
the updated priorities and new policy baseline. Current requirements stay Draft.

Current corpus common-cause follow-up: read docs/corpus-common-failure-priorities.md,
evidence/corpus-common-causes.json and evidence/corpus-common-mutation-replay.json.
The focused policy replay catches 34/36 prior survivors and all three uncovered
mutants; two survivors retain conditional equivalence assumptions, not proof.
378 Stryker SafeMode compile errors remain unavailable evidence outside this
selection. Rust scalar mutation replay catches two arithmetic mutants; ten
constructor replacements are unviable. Prior broader mutation cohorts stay open.
Bounded s/Hz scalar decorations (NGC-033) advance first diagnostics for 129 of
910 tracked decks in the scalar-only run, exposing deeper blockers. The next
transient endpoint fix (NGC-CORPUS-004) uses an explicit four-ULP bound and policy
v2, with separate history series. Read the finalized run evidence for actual
pass/failure counts rather than inferring original-deck support from imports.
No new full soak, Linux execution, CI dispatch or source/main push is implied.
Raw logs and upstream deck bodies remain local; default corpus invocations publish
one results issue each. Preserve all earlier failure runs and policy histories.

Current handoff: inspect the actual git status/history and
`docs/five-area-completion-plan.md` before continuing. The five bounded feature
checkpoints and their exact source/verification limits are in
`evidence/five-area-*.json`; use those newer reports over the historical managed
counts below. The execution ledger is local `work/five-area/progress.md`; raw
logs remain local. No full SPICE, GPU, universal convergence, complete mutation
coverage or production qualification is claimed. Preserve unknown work and the
repository's existing CI policy. Task-specific push/upload permission must be
checked against the current user request.

Historical handoff follows.
Latest October 30 managed follow-up: read evidence/managed-20261030-review.md and
its stable 19-outcome manifest before proceeding. Final Windows Stryker: 202
candidates, 181 killed, three survivors, three timeouts, one compile error,
one uncovered, 13 ignored. Focused Linux replay kills all four selected cleanup
mutations, including the exit-wait deletion that survives Windows. Across both
scopes, eight of the original ten survivors and three original timeouts are caught.
Current 80-test suite passes Windows/Linux. Coverage: client 171/179 lines,
112/114 branches; synthetic fixture Windows 54/56 lines, 48/50 branches.
Open: three deliberate liveness timeouts, interactive Windows console evidence,
conditional decode-only BOM equivalence, defensive startup-null branch, and
pre-ack startup-fault fixture cleanup qualification. Compile errors are not kills.
Source guard changed equivalently; original compile-error identities are not
relabeled killed. No full soak, push or CI dispatch occurred. Older counts below
are historical. Keep the existing scheduled campaign/review and broader M1 scope.
October 30 follow-up: see evidence/soak-20261030-fixes-review.md before continuing.
The historical fd34c53 six-hour soak completed; current finite fixes add bounded
CSC liveness evidence, independent waveform checks, exact Rust selector support,
managed mutation guard initialization, and inherited-pipe/owned-worker cleanup.
Final managed replay: 201 generated, 169 killed, 10 survived, six timeout, two
compile error, one uncovered, 13 ignored. Retain the outstanding error-envelope,
lifecycle and diagnostic-contract gaps; do not report all mutants resolved.
A full-duration run on this new checkpoint is still pending. No push or CI was
authorized for this follow-up. Older counts below retain historical scope.
# Spice qualification handoff — survivor triage after completed campaign

October 29 follow-up: read evidence/overnight-20261029-review.md and the stable
24-survivor inventory first. Source/test checkpoint 3e6d8f0 adds 17 tests: 22
original identities are caught across recorded GNU/MSVC replay scopes; one
defensive ID guard is masked downstream and one pulse guard is conditionally
equivalent. Both platforms passed 396 tests with one ignore. Coverage remains
qualified by LLVM mapping warnings. Managed EOF timeout/Stryker rollback remain
open. The coverage fix from origin/main was merged locally; no push/CI dispatch
was performed. A new five-hour run of this test checkpoint is still pending.

October 28 follow-up: read evidence/overnight-20261028-review.md and its linked
native replay/source-hash evidence first. Of the new 22 Rust survivors, 20 are
caught and two retain conditional-equivalence dispositions. Managed EOF mutation
182 is killed in isolation but still times out in the full suite; Stryker's
103 SafeMode compile errors and the adaptive LLVM mapping discrepancy remain
explicit verification gaps. This work does not restart the completed quota.

The October 27 follow-up addresses overnight results; it does not restart the
completed iteration quota below. Read evidence/overnight-survivor-triage.json,
evidence/overnight-managed-survivor-triage.json and the triage evidence reports.
Stable IDs retain original source/run identity and actual replay outcomes.
Resource failures and unattempted work require replay; they are not kills.
Raw artifacts remain in ignored work/. No new full overnight run or source push
is implied by the follow-up. Preserve the existing scheduled campaign and review.

Follow-up source checkpoints are d77bd6d (campaign/publication controls) and
216859d (nonlinear initialization solve accounting plus 22 regression tests).
The original 35-survivor cohort now has 31 caught and four retained conditional
equivalents; two supplemental importer mutations are caught by existing tests.
Read evidence/overnight-survivor-review.md and evidence/triage-survivor-replay.json
for exact scopes, hashes, interrupted work and limitations. Use the current-name
selection in docs/mutation-frameworks.md; historical subsets have different scope
or locations. Reopen equivalence dispositions when source/preconditions change.
The next scheduled campaign must supply fresh full-duration confirmation.

The user-authorized five-area campaign is complete under the documented 100+1
interpretation: repository iterations 335–434 plus final review/fixes 435. Read
AGENTS.md, docs/implementation-plan.sdoc, docs/architecture.sdoc,
docs/development-standards.sdoc, docs/qualification-campaign.md, current Git state
and evidence/final-435-review.md before new changes. Do not restart the completed
quota or infer full M1 closure from this finite campaign.

The active engineering objective remains convergence hardening, direct sparse
assembly/CPU-provider qualification, bounded model contracts and selected ngspice
corpus/oracle growth. Use PowerShell and C#/F#, small verified commits, StrictDoc
stable IDs/traceability, meaningful mutation/property/malformed-input checks and
honest coverage. Preserve unknown local work. GitHub CI remains undispatched.
Future push/iteration cadence must follow the new task rather than replaying this
completed request. The existing Spice results-branch workflow remains authorized
only within its documented scope; raw artifacts stay local.

Implemented finite slices:

- Source/Gmin continuation validates nested policies, finite scales, correction
  shapes, terminal failures and representable progress; staged Gmin always ends
  with the unregularized physical solve. Preserve the tight bridge counterexample.
- Direct canonical CSC AC/linear BE/trapezoidal, genuine complex excitation,
  independent residuals, owned faer real factors and exact-step cache. Prepared
  complex reuse and general rank qualification remain open; dense references and
  nonlinear Newton retain the bounded reference path.
- Explicit diode_with_capacitance composes Shockley conduction with positive
  constant Q=C*V. Independent derivatives, history/domain/unit/audit, generated,
  composition and named ngspice checks exist. i_conduction(id) excludes displacement.
  No depletion/transport/series or general temperature-scaled IS is implemented.
- The ngspice importer now preserves DC bias independently of AC excitation and
  assigns zero AC drive to DC-only sources. CJO/CJ0 requires explicit M=0 and
  zero supported RS/TT. Original diode AC/transient oracles retain nominal
  temperature, interpolation/rounding/BE bounds; UIC importer support is not implied.
- Typed C# profile/phasors preserve legacy diode shape and Ac constructor and
  deconstruction signatures. Core serialization uses explicit generated metadata;
  trimming/AOT analyzers pass and a Windows x64 TypedCharge native publish/run
  asserts independent equations. Whole-SDK/Linux AOT remains unqualified.

Final measured evidence:

- 345 Rust tests, 29 managed conformance checks, 70 Windows and 70 Linux xUnit cases.
- Proptest: 16 properties at 4096 requested cases each, 65536 total requested;
  default seed 0x5350494345333535, replay/shrink work counted separately.
- Owned Rust lines 5342/5783 (92.37%); native branches unavailable. All 77 LLVM
  warnings match five attributed dependency symbols, no owned/unclassified symbols.
- Owned client Coverlet: 166/174 lines, 109/112 branches. Helper/vendor scopes are
  separate. Coverage alone is not correctness or qualification.
- Final Stryker: 200 candidates, 88 caught, one survived, one timeout, 103 compile
  errors, seven ignored. The BOM-flag survivor is likely equivalent for decoding
  only, not formally proved. Do not count timeouts/invalid/ignored as caught.
  Client/test snapshot hashes match final source; the frozen native dependency
  has a distinct recorded binary hash from a later ordinary build. Do not equate
  binary identities or infer deterministic builds from source identity.
- Earlier native selections remain source-scoped: charge ten caught/four unviable
  of fourteen, initial signs three caught of three, dynamic operators twenty caught
  of twenty. Preserve historical AC/Newton survivors and all outcome categories.
- StrictDoc 0.30.4: 14 documents, 256 unique IDs, 243 resolved parent relations.
  scripts/ValidateRequirements.cs runs the real parser plus global identity checks;
  successful parsing does not establish behavioral closure. Sphinx 9.1 projects
  230 compiled XMLDoc entries with warnings as errors.
- Nine synthetic remote-publication Pester tests pass. No new full-hour/five-hour
  campaign or full remote workflow is claimed by these short checks.

For reproduction, locate the existing checkout on this host; verify its directory
before changing into it. From that checkout, use scripts/HostTools.ps1 and the
pinned runtime, then scripts/Checkpoint.cs -- 435, scripts/ManagedCoverage.cs and
scripts/ValidateRequirements.cs -- --python <configured StrictDoc Python> --report
<existing evidence directory>/requirements.json. Native coverage uses
scripts/Test-Coverage.ps1 with the existing WSL native checkout/toolchain. Keep
Windows/Linux outputs isolated. Read docs/mutation-frameworks.md, property-testing.md
and remote-runs.md for replay, setup, configurable budgets and result publication.

The historical e85c1e2 one-hour campaign completed 3599.04 seconds and 50,987,008
custom seeded cases, 22/22 selected custom mutants, with its own published results
branch. Preserve the two failed partial predecessors. Never relabel that hour as
cargo-mutants/Stryker evidence. Nightly midnight five-hour scheduling is unchanged.

Concrete future work: broaden difficult topology/oracle evidence, triage retained
mutation survivors/invalid generation, qualify additional numerical/provider error
paths and native branches where supported, add prepared complex reuse or general
rank diagnostics only with requirements/evidence, then consider further explicitly
bounded semiconductor models. That historical checkpoint preceded AUD-AC-001
and AUD-CONFIG-001: automatic linear AC and fixed configured profiles are now
implemented. Nonlinear AC bias-dependent audits, adaptive midpoint-dependent
physical audits, full DAE qualification, coverage-guided fuzzing, broad SPICE
syntax/devices, GPU and production release gates remain open. The unitless legacy absolute_tolerance is
validated compatibility data; actual convergence uses voltage/current tolerances.

## Parser-only continuation (2026-10-31, issue #11)

The user confirmed keeping ANTLR after the bounded Sly comparison. Read
docs/sly-trial.md and evidence/sly-trial.json before revisiting framework choices.
The Sly experiment is isolated research for possible future .NET DSLs; do not
turn it into a production parser migration without a new user decision.

Read docs/parser-gap-results.md, docs/parser-grammar-inventory.md and
compat/ngspice/parser-input-adjudications.json before further parser work.
ANTLR 4.13.1 remains the selected framework. Keep generated syntax acceptance
independent of the Rust importer, simulation capabilities, model resolution,
include loading and control execution. Use the authoritative inventory and the
completion gate rather than inferring invalid syntax from a parser rejection.
Strict UTF-8 and explicit legacy-byte results are separate input profiles.
The bounded reports retain exact source/input/binary hashes; raw campaigns and
coverage/mutation artifacts remain local. Do not treat profile-only mutation
kills as closure of the historical broad survivor ledger or claim complete
ngspice grammar/semantic conformance from structural acceptance.
