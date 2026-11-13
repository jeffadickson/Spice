# Sol checkpoint — November 13: post-OP pulse parameter state

Read docs/source-pulse-alter-results.md and evidence/source-pulse-alter.json.
Unchanged original regression/misc/alter-vec.cir now completes OP (two observers,
zero error), original 1e-15 assertions and full stock ngspice. Sol total four.
Scope is metadata/reporting only: no new physics or future-analysis source alter.
Single-analysis failed terminal assertions now prevent whole-program agreement;
authored negative retains numerical agreement but fails program status.
Final syntax531/1skip, frontend916/40skip, StrictDoc67; Stryker selected8 killed
plus stale-read and failed-assertion gate mutants detected/restored. Exact coverages/provenance in
evidence. No native rebuild, MOS stock closure, pushes or CI. Preserve campaign
hard stop and no-memory-cap policy in ignored work/hourly-deck-progress.json.
# Sol checkpoint — November 13: affine behavioral temperature

Read docs/behavioral-temperature-results.md and evidence/behavioral-temperature.json.
Original regression/misc/asrc-tc-1.cir now completes OP+AC, all six observables per
analysis, and original 1e-9 assertions against full stock ngspice. Sol total three.
Requested affine TC1/TC2 and one qualified literal root .temp reuse existing
linear stamps; conditional early quit 1 now preserves assertion failure before
later reporting. Successful/unconditional early exits remain unsupported.
The authored false assertion is correctly refused as program agreement; the TC2
example agrees separately. Final syntax 520/1 skipped, frontend 905/40 skipped,
66 real StrictDoc docs; clean-baseline Stryker span 18 killed, 0 survivors, plus
3 killed boundary mutants. Initial failed-baseline attempts remain failed evidence.
No native rebuild, MOS stock closure, pushes or CI. Preserve current campaign
hard stop and no-memory-cap policy from ignored work/hourly-deck-progress.json.
# Sol checkpoint — November 13: resistor temperature sweep

Read docs/resistor-temperature-results.md and evidence/resistor-temperature.json.
The unchanged original regression/temper/temper-res-1.cir now agrees on all six
signals at all ten DC temperature points, with original 1e-14 assertions and full
stock-ngspice program both passing. One additional original; Sol total is two.
Requested TC1/TC2/TCE and model TNOM use existing per-point preparation; bounded
real power and control-only AND/OR aliases complete its original reporting.
Final syntax 518/1 skipped, frontend 875/40 skipped, 65 real StrictDoc documents.
Stryker selected span: 18 killed, 2 invalid, zero survivors; two reporting mutants
killed. See exact coverage, hashes, raw evidence and bounded limitations there.
No new native build or MOS memory stock-default closure. Keep current user campaign
deadline and no-memory-cap policy from ignored work/hourly-deck-progress.json.
No push or CI; canonical main remains untouched.
# Sol checkpoint — November 13: complete real-vector reporting

Read docs/real-vector-echo-results.md and evidence/real-vector-echo.json.
Original regression/model/special-names-1.cir now executes its full assertions
and agrees on all nine OP observables with stock ngspice. One new original
completion in the newly authorized Sol campaign, reporting-only repair;
physical OP already agreed before this change. Earlier ac-zero and PZ baselines
already passed and were not counted. Stock mosmem agreement remains open.
Keep the current campaign authorization/deadline from the user and ignored
work/hourly-deck-progress.json; old expired campaign instructions below are
historical. No push or CI. User removed custom memory caps/RAM admission for
this campaign; keep serialized jobs with timed process-tree cleanup and E: outputs.

# Manual two-deck checkpoint — November 12

The user explicitly resumed work to finish ac-resistance and mosmem after the
hourly campaign expired. Do not restart that automation. Branch
fix/deck-blocker-implementation remains isolated; canonical main is untouched.

Read docs/ordered-control-results.md and evidence/ordered-control-ac-resistance.json
for verified commit 9427e23: the original ac-resistance OP/AC/PZ program executes,
its original assertions return success_count=3, and mapped results agree with
ngspice. Read docs/mos-memory-results.md, docs/mos-memory-execution.sdoc and
evidence/mos-memory-execution.json for the subsequent MOS execution checkpoint.
The original memory transient now completes with explicitly selected Faer;
all 13 signals agree against two separately labelled refined numerical ngspice
profiles, but its stock/default ngspice comparison remains failed. Do not call
this two stock-reference closures. Dense LU still fails the later memory ramp.

Keep all bulk artifacts under the recorded E: run root. Preserve failed trials,
the default-reference mismatch, hashes, refined input identities and coverage
limitations. No push or CI dispatch is authorized by this manual checkpoint.

# Historical hourly campaign closure — November 11

The hourly automation spice-hourly-deck-fixes-20-hours is PAUSED. Do not resume
it automatically or extend the hard stop 2026-11-11T23:53:39Z. Resume engineering
work only from a new user instruction. No campaign workers remain running.

Read docs/hourly-campaign-final-20261111.md and
evidence/hourly-campaign-final-20261111.json. A fresh clean-source replay at
e1cf193 confirmed exactly two newly agreeing original decks: if-elseif.cir
(two observables) and log-functions-1.cir (14 observables), with actual reporting
exit 0 for each. mosmem still fails source-continuation initialization at scale
0.4188232421875; ac-resistance still refuses its multi-analysis control program.
The authored loaded-RC PZ example is not counted as a new original deck.

The branch fix/deck-blocker-implementation contains the verified checkpoints
and final handoff; canonical main is untouched. Nothing was pushed and no CI
was dispatched. Bulk artifacts stay E:. Final raw replay is under
E:/Artifacts/Spice-hourly-final-e1cf193. Preserve the pinned E: NuGet packages,
source/binary hashes, earlier failures, coverage scopes and actual limitations.
The final replay changed no implementation, so it adds no new coverage or
mutation claim beyond the checkpoint evidence linked below.

# Latest checkpoint — loaded RC and AC-resistance poles, November 11

Read docs/pole-zero-ac-resistance.sdoc, its results document and
evidence/pole-zero-ac-resistance.json. The bounded analytical PZ helper now
uses validated requested passive preparation, retains the DC circuit and
substitutes AC resistances only in its local root calculation. A grounded
loaded RC section has pole -(Gseries+Gshunt)/C and no finite zeros. The corpus
coordinator preserves this preparation instead of strict OP fallback.

All 33 focused PZ cases and 828 full frontend tests pass, with 40 prerequisite
skips. The FsCheck property runs 64 cases (5001,20261111), one wrong AC-factor
mutant is killed, the restored suite is rerun, and 61 StrictDoc files parse.
The authored examples/loaded-rc-pz.cir passes through the normal coordinator
and agrees exactly with ngspice at -500000 inverse seconds. This is NOT a new
agreeing original RSpice deck. The unchanged original ac-resistance.cir still
refuses control preparation: ordered analysis phases, complex reporting and
control PZ dispatch remain open. Campaign new-original total stays two.

Former pinned NuGet runtime/analyzer files were absent from C:. A locked restore
now lives under E:/Artifacts/Spice-ac-resistance-a984f11/packages, with HTTP
cache and scratch also E:. Build assets use that path; do not silently switch
back to the missing former cache. Keep the serial watchdog and OS headroom.
The hard campaign deadline is 2026-11-11T23:53:39Z. The next hourly wake is the
last expected before it: disable the hourly automation before returning from
that final bounded session, and leave no workload running at the deadline.
No pushes or CI; preserve canonical main and unknown work.

# Latest checkpoint — bounded behavioral voltage DC, November 11

Read docs/behavioral-voltage-op.sdoc, docs/behavioral-voltage-results.md and
evidence/behavioral-voltage.json. The generated requested-analysis compiler
now preserves one differential control voltage in a typed expression tree;
the Rust engine stamps its real constitutive residual and analytic Jacobian.
A linear-constraint seed establishes source voltages before domain-sensitive
logarithms, followed by full physical convergence and independent auditing.

The unchanged original regression/misc/log-functions-1.cir now agrees with
ngspice on all 14 mapped observables. Its complete control assertions exit zero
on our reporting evaluator and on the separate full ngspice execution. This
adds one original agreeing deck; campaign total is two. Verified restored-source
checks: 109 native tests; 819 managed tests with 40 prerequisite skips; 13 focused
cases; 64 Proptest cases seed 2026111101; one killed slope mutant; 60 StrictDoc files.
Measured coverage, exact source/binary hashes, tolerances and raw E: paths are
in the evidence. This is DC-only single-control voltage behavior; dynamic,
current-valued and arbitrary multi-control behavioral sources remain open.

The final restored engine snapshot is under
E:/Artifacts/Spice-behavioral-cdd4560/verified-engine. Earlier mutation-built
executables and guard-killed runs are not final verification. Preserve the
hourly campaign deadline 2026-11-11T23:53:39Z; no pushes or CI. Next revalidate
regression/pz/ac-resistance.cir against this checkpoint, then continue shared
circuit behavior and the unresolved mosmem convergence mismatch. Keep all bulk
outputs and scratch on E:, with admitted process-tree/aggregate memory guards.

# Latest checkpoint — bounded compose-values reporting, November 11

Read docs/control-compose-values.sdoc, docs/control-compose-values-results.md
and evidence/control-compose-values.json. Generated control syntax and the real
reporting evaluator now support ordered scalar compose-values, ln/log/log10 and
integer vector indexing. Unresolved bound-name subtraction uses the existing
arithmetic lexer before expression parsing; exact vectors and probe terminal
names retain priority. A rejected atom-level prototype changed division
precedence; its failed qualification output is preserved on E:.

Final verification: 495 syntax tests pass with one explicit platform skip;
806 frontend tests pass with 40 external-prerequisite skips. The composition
property runs 128 cases, replay 385,20261111. One wrong-log-base manual mutant
was killed and source restored. All 59 StrictDoc files parse. Coverlet measured
syntax 6927/10656 lines and 2563/4421 branches; the broader frontend run measured
19967/25578 lines and 5813/9262 branches. Generated code is included and these
different scopes are not comparable project-wide coverage scores.

The unchanged examples/compose-values.cir qualification fixture has two OP
observables agreeing with ngspice and both complete reporting programs exit
zero. Its explicit relative assertions remain 1e-15. Check the actual
control-report.json exit and diagnostics: row-level individual-analysis
agreement alone does not establish reporting/assertion success. The final raw
report is E:/Artifacts/Spice-compose-68c67e6/replay-certified. Earlier numeric
agreement with failed reporting is retained as failed program evidence.

The unchanged original regression/misc/log-functions-1.cir is still refused at
non_affine_behavior; compose is no longer a blocker. No additional original
deck is counted. Campaign cumulative completed-and-agreeing originals remains
one. Native src/expression.rs already has a bounded single-input AD experiment
including logarithm but is explicitly not integrated as a nonlinear device.
Trace and reuse its contracts before implementing behavioral source physics;
do not silently fold arbitrary node-dependent expressions to constants.

All raw artifacts are E:/Artifacts/Spice-compose-68c67e6. Shared-host memory
remains scarce. Use measured budgets and OS headroom, serialized builds and
optional dotnet/qualification-serial.runsettings. Direct VSTest needs the
existing Coverlet collector's explicit TestAdapterPath for coverage. Initial
admission refusals, watchdog terminations and collector-discovery failures are
not passing campaigns. Canonical main and unknown changes remain untouched.
Do not push or dispatch CI. Hard stop remains 2026-11-11T23:53:39Z.

# Previous checkpoint — MOS1 forward body behavior, November 11

Read docs/mos1-forward-body-results.md, docs/mos1-forward-body.sdoc and
evidence/mos1-forward-body.json. MOS1 now supports positive effective body bias:
exact piecewise Newton slopes, a separately named ngspice-compatible AC body
convention in both assembly and reporting, matching Meyer threshold and
independent current audit. The bounded backtracking candidate limit is24;
correction/provider budgets and acceptance tolerances are unchanged.

Actual Windows checks:102 named native tests pass;22 independent ngspice
channel-isolation references yield52 passing signal comparisons on LU/Faer.
The128-case MOS property seed2026110202 includes positive body bias. One manual
public-AC-reporting mutant is detected. Scoped mos1.rs coverage134/141lines;
stable Rust branch coverage unavailable. All58StrictDoc documents parse.
Raw files and final engine stay E:/Artifacts/Spice-mos1-forward-66158d4.

The memory circuit now completes101 samples through2us with an explicitly
selected strict Faer/Gmin profile, but its default original request still fails
source continuation near0.4188232421875. Default-current Gmin initialization
also leaves residuals that independent auditing refuses. The selected full
waveform does not agree on all13 available default-ngspice signals: maximum
voltage difference3.40mV and source-current difference0.586mA. Do not count this
as another fully agreeing original deck. Next investigate bounded initialization
recovery and residual/update tolerance consistency, then integration/default
charge differences with explicit reference uncertainty. Completed-original count
remains1; preserve main, no push/CI, hard stop2026-11-11T23:53:39Z.

# Previous checkpoint — MOS startup convergence, November 11

The phase-specific reverse-junction limiter is qualified in
docs/newton-reverse-limiter-results.md and evidence/hourly-mosmem-startup.json.
Strict Faer/Gmin startup of the original mosmem topology now finishes with a
50-correction stage ceiling: first stage9 instead of101, total109 instead of201
linear solves, identical physical state and final Gmin removal.99 named native
checks pass, including the retained startup fixture and switch/PNP/sparse
regressions. One manual gate mutant is detected by two integration regressions.
Current newton.rs LLVM scope is740/1000lines; stable branch maps are unavailable.
All57StrictDoc documents parse. Raw data stays E:/Artifacts/Spice-hourly-mosmem-e3395dd.

This does not unlock the full original mosmem transient. Its source continuation
still fails near scale0.4188232421875; diagnostic Gmin startup reaches a later
transient line-search failure. Locate the failing time/step next. Default-ngspice
time-zero comparison agrees9/13, with four voltage discrepancies up to2.45mV.
The separate tight OP reference agrees13/13 within1.28pV and0.025fA; retain both
results and do not substitute the secondary reference for default conformance.
Campaign completed-original-deck count remains1. Preserve canonical main and
the20-hour hard deadline2026-11-11T23:53:39Z; local verified commits only, no CI/push.

# Previous checkpoint — hourly original-deck campaign, November 10

Use C:/dev/spice-deck-next, branch fix/deck-blocker-implementation. Preserve
canonical main and unknown changes; this campaign authorizes local verified
commits, no pushes or CI. It has a hard stop 2026-11-11T23:53:39Z.

Read docs/netlist-conditionals-results.md, docs/netlist-conditionals.sdoc and
evidence/hourly-netlist-conditionals.json. Original RSpice
regression/misc/if-elseif.cir now selects its nested 300-ohm branch, executes OP,
agrees on voltage/current with ngspice, and executes its original assertions with
out=300, INFO success and exit zero. Captured test.log is owned data, not a file
write. Root branch selection reuses generated arguments and ParameterEvaluator;
name#branch aliases reuse the signed numerical source-current signal.

One newly executing/agreeing deck in this campaign. Syntax 477 passed/1 platform
skip; frontend 802 passed/40 external-prerequisite skips; 80 FsCheck cases replay
883,20261111; one deliberate truth-inversion mutant killed by seven regressions.
Scoped class coverage and resource-killed/stale-expectation attempts are retained
honestly in the results. Artifacts remain E:/Artifacts/Spice-hourly-if-36dd277.

Next select another genuinely failing original deck. mosmem source-continuation
failure, ac-resistance mixed control/PZ dispatch and log-functions behavioral
evaluation remain open. Conditional parameters, instance-scoped decisions,
conditional includes and temperature-dependent netlist branches are outside the
new bounded profile. Do not count syntax acceptance as numerical progress or
rerun prior passing decks as newly unlocked. See ignored
work/hourly-deck-progress.json and recheck resources/active jobs before dispatch.

# Previous work — shared controls and grammar implementation map

This section supersedes older next-step directions below. Execute issue34 using
 docs/shared-control-plan.md. Branch feature/issue34-shared-controls in
 work/simulation-bridge. Artifacts are recorded in ignored work/control-artifacts.txt
 (E:/Artifacts/Spice-controls-20261104T011818Z).

Generated let/define/unlet metadata now supports a bounded reporting suffix after
 the final analysis/run, explicitly postprocessing_not_executed. Root/axis
 clobbering, nonlocal deletion, earlier/interleaved declarations and unsupported
 effects refuse atomically. Display/listing and literal xbrushwidth are reporting
 omissions. No arbitrary control interpreter/vector evaluation is claimed.

The grammar implementation inventory enumerates 93 actual productions and includes
 a separate 111 historical control-heading view. 90 reviewed associations include
 structural infrastructure, bounded profiles, refusals and unexecuted observations;
 they are not 90 fully implemented features. Concrete unmapped headings remain visible.
 The historical capability labels were reconciled with current generated routes.

Read docs/shared-control-results.md and evidence/shared-control-corpus.json.
 The clean bb19799 census completed 928 paths: 85 prepared, 25 executed and 12
 native agreements. Thirteen numeric discrepancies remain. Prior 26cfad9 had
 23 executed/11 agreements. Next reached control blockers: if14, earlier
 declarations12 and unsafe/unsupported tokens9. Do not infer gains from syntax reach.

The separate user-authorized two-hour soak stays pinned 6411105, with its own E:
 output/cache and canonical lock; new frontend work does not qualify that run.
 Preserve canonical main's seven unrelated WIP files; CI stays off.

# Active work — issue34 implementation

The user now authorizes execution of issue34's prioritized plan, superseding the
measurement-only scope below. Read docs/issue34-execution-plan.md,
docs/issue34-progress.md and evidence/issue34-ground-checkpoint.json.
Active branch: feature/issue34-common-semantics-nov3 in work/simulation-bridge.
Artifacts: E:/Artifacts/Spice-issue34-20261103T195804Z.

Ground normalization now has explicit default/PSpice/disabled policies and exact
zero-control reductions. 53 focused checks, 498 broader frontend passes and 42
native OP/AC comparisons pass (20 broader prerequisite skips). GroundBinding
Stryker: 76 killed / 3 survived / 1 uncovered / 3 compile errors / 7 filtered. Coverage limitations
and unqualified ground contexts remain explicit. Original ground corpus replay
and a new full census are pending; keep the 928/81/21/9 baseline historical.

Next: original-ground replay, then explicit initial-OP Newton policy and original
BJT waveform qualification, keeping LU refinement failures separate. The public
BJT diagnosis still shows initial source recovery and later LU refusal; faer warm
transient completes. An isolated native breakpoint-clock experiment fixes the
analytical sine interpolation discrepancy, but production-oracle integration needs
isolation/budget/invariance tests before use. Continue shared-control, model-family,
XSPICE and resource work under the issue plan; do not claim all are complete.

Preserve canonical main's seven unknown sheet-resistor/documentation files. No CI.

# Previous handoff — November 3 full requested-analysis census

User authorized measurement/prioritization steps 1–2 only. Read
docs/requested-corpus-fc5db25.md and evidence/requested-fc5db25.json first.
Results checklist: https://github.com/jeffadickson/Spice/issues/34.
The complete 928-input immutable fc5db25 census took 957.417 seconds, provenance
valid: 916 syntax accepted, 81 physically prepared, 21 original requested
executions, 9 ngspice agreements. Four original BJT transient executions still
fail Newton; 12 completed comparisons remain unqualified. No simulator feature
was changed. Raw artifacts: E:/Artifacts/Spice-census-20261103T191728Z.
Source checkout: C:/dev/spice-census-fc5db25. Keep both immutable; bulk stays on E:.

The bounded readiness ranking observes 218 MOS-profile, 206 active-XSPICE,
180 options-policy, 116 BJT-profile and 71 diode-profile roots; groups overlap.
89 graphs are incomplete and 22 observation lists truncated. These are not
predicted unlocks. Ground normalization and original BJT initialization remain
near-executable follow-ups; analysis/directive semantics and explicitly named
model profiles are shared work. Do not restart old iteration quotas or claim
full compatibility. Reporting helper tests/coverage/mutations are separate from
simulator qualification. Preserve the seven unknown canonical-main sheet-resistor
files listed in the older handoff; no CI was dispatched.

# Previous handoff — November 3 overnight recovery

Read docs/overnight-recovery-results.md and docs/overnight-recovery-plan.md first.
This section supersedes older next-step directions below. Preserve the active M1
objective and the seven unknown canonical-main sheet-resistor/documentation files.

All new bulk artifacts are under E:/Artifacts/Spice-fix-20261103T140559Z.
Three targeted Rust survivors are caught by new boundary/independent-physics
regressions. Physical E work roots, real fixture preflight, shared cancellation
and shared absolute parent/child deadlines fix the Windows campaign failures.
No registry/global build-root migration or existing artifact removal occurred.

The preflight exposed a reversed-switch Newton failure. Final globalization uses
frozen dimensional row merit while linear voltage constraints are infeasible,
then legacy descent after feasibility; strict final tolerances, iteration budgets
and mandatory final Gmin removal are unchanged. The rejected unconditional-merit
prototype regressed PNP Gmin recovery and remains recorded, not hidden.

Final actual checks: 532 Rust passed/2 explicitly ignored; 479 frontend/native
oracle tests passed/9 external-prerequisite skips; original NPN/PNP source/Gmin
recovery passes with eight-correction stages on LU/faer. Final engine42835c20...
agrees with ngspice47+ and the analytical minimized switch voltage/current.
Scoped coverage and mutation reports retain compiler-invalid/filtered/unattempted
candidates and unavailable stable-Rust branch maps. Do not claim full mutation
closure from the three-survivor replay or narrow Stryker proof.

Run outcomes: issue30 original failed night; issue31 failed switch-property
preflight; issue32 partial outer-deadline run; issue33 final clean property-only
preflight. The last returned normally at its effective time budget: 44 completed
batches/101376 completed requested cases, no pending batch or recorded numerical
failure. This is bounded evidence, not a full overnight or production qualification.
The previously pinned8ecc0ab soak does not qualify current main. Keep CI off and
raw artifacts local; use existing results-branch policy for authorized remote runs.

---
# Latest handoff — five-area compatibility checkpoint, November 3

This section supersedes capability counts and next-step directions in the older
historical checkpoints below. Read docs/corpus-compatibility-implementation-plan.md
and evidence/five-area-integrated-verification.json. Generated preparation now
connects F/H, active globals, AM/SFFM, scoped constant/sheet R, affine B, analog
XSPICE gain/summer and bounded D/Q/M models to original requested OP/DC/AC/TRAN.
The diode and bounded BJT/MOS1 profiles include actual storage; intrinsic MOS
Meyer charge, broad Gummel–Poon and arbitrary control/event programs remain gaps.
Explicit unsupported parameter contracts remain intentional.

Product checkpoint f64edbd: full Rust521 passed/2ignored, frontend465 passed/
10skipped plus one previously skipped original-control test passed separately,
syntax433 passed/1exFAT symlink skip. Component coverage/mutations/property seeds
and limitations are retained; do not describe outer bridge coverage as complete
captured-helper coverage. Supplemental stop-grid replay passed8tests with native.

Read docs/requested-corpus-20261103.md and issues27/28: the first full928 requested
campaign executed18 and agreed4; its separate identity-fixed18cohort agreed7 and
retained11 numerical discrepancies. These are different analysis scopes from
older selectedOP counts. Ten stateless sine/gain failures are diagnosed oracle
interpolation effects; primary statuses remain unchanged. One RC-ramp BE accuracy
gap remains. The supplemental stop-grid POC changes native internal step history
and does not qualify arbitrary charged/nonlinear comparisons.

The final frozen full928 campaign at f64edbd completed834.140seconds with matching provenance:85prepared,23completed requested executions and9native agreements;14unqualified comparisons and4additional Newton-failed amplifier transients remain. Read issue29 and evidence/requested-final-f64edbd.json. Subsequentadd9c12corrects two accepted floating-GND circuits by explicit unsupported_ground_alias refusal; both original files replayed with zero executed operations. The full23/9counts remain historical before this guard. Fullfrontend now479passed/9unavailable skips, with8stopgrid tests included. See evidence/ground-alias-admission.json. CurrentStrictDoc45documents/394IDs/369parents and Sphinx/APIbuild passed. Reproduce original amplifier failure separation with scripts/Diagnose-BjtInitialization.cs and docs/bjt-corpus-initialization.md: source50 OP recovers, LU later fails residual, explicit faer warmstart completes200steps.25calls freshly replayed; no new native-agreement claim. Its raw evidence is under
E:\Artifacts\Spice-compatibility-20261103T043926Z\requested-final-f64edbd.
Read its actual summary/provenance before reporting results. Running or exhausted
work is not a passing run. Use scripts/Run-RequestedDeck.cs for bounded standalone original-deck execution; see docs/requested-deck-runner.md. The earlier23:00–06:00 soak stays pinned at8ecc0ab;
do not retarget or confuse it with new-feature qualification. Preserve canonical
main's seven unknown sheet-resistor/documentation files. No CI dispatch, GPU,
Linux or production qualification is implied. Bulk artifacts stay on E:.

---
# Current handoff — November2 five-checkpoint simulation work

Read AGENTS.md, docs/implementation-plan.sdoc, architecture.sdoc,
development-standards.sdoc, git status/history and the named result evidence.
Active objective remains convergence hardening, direct sparse assembly/CPU
provider qualification, bounded model-contract expansion and named ngspice
corpus/oracle growth with StrictDoc traceability. Prioritize real simulation
progress over mutation-score detours. Syntax acceptance is not simulation support.

Approved next-five plan: docs/next-five-execution-plan.md. Checkpoints1–4 pushed:
-86d193d original928-input readiness census:38solves/37native matches/10AC.
-2fb0699 waveform slice:51solves/50native matches/14selected AC files.
-5ac67e5 independent third reference:36agreeing OP files (12withAC),13unavailable,
 1parameter associativity discrepancy. Named bug883 separately agrees in three-way
 OP/AC. Reference-only long-IL-branch patch is explicit; no production dependency.
-9a434d0 named transient/current comparisons:3of4 fixtures agree across all three;
 delayed phaseSIN retains a1V/1mA SpiceSharp discrepancy.58checks/54boundpasses/
 4discrepancies; control-load analytical KCL and step refinement are explicit.

Read docs/corpus-waveform-results.md, third-reference-results.md,
transient-current-results.md and their evidence JSON. Frozen928 inventory has
8pinned exclusions,30resource refusals,839lowering refusals and1solved zero-port
case lacking root observables. No complete original control/sweep programs,
whole-corpus transient, fullSPICE/GPU/production qualification is claimed.

Checkpoint5 preflight: docs/verified-soak-window.md and
 evidence/verified-soak-preflight.json.16Pester contracts passed, including shared
actual-worker locking, outer bootstrap deadline and post-dispatch reporting
failure cleanup; six clock contracts;18Proptest methods at256requested cases,
seed88320261103. Latest full frontend142passed; syntax406passed/1exFAT skip;
Rust full482passed before the additional2property methods. StrictDoc21documents/
342IDs/331parents. Sphinx/API documentation builds with warnings as errors.
Actual overnight420-minute completion remains pending for authorized11pm–6am
Pacific window. Resolve scheduler metadata and ignored work/verified-soak-active.json;
read real E: summary, mutation logs, source hashes, actual duration and replay seeds.
Scheduled/startup status is never a completed run. Preserve any active campaign.

Use PowerShell/C#/F#, small verified commits/pushes, E: bulk artifacts, no CI.
Preserve main's seven unrelated sheet-resistor WIP paths. Do not reset/discard them.
Next evidence review should distinguish third-reference defects/unavailable inputs,
actual simulator gaps, explicit skips and budget exhaustion. Prioritize recurring
XSPICE/model-family/physical-option/resource blockers from readiness reports.

# Archived checkpoint notes (historical; counts superseded above)
## November 2 PSpice VSWITCH bug883 implementation

Smooth bounded S/VSWITCH now lowers through generated model binding and retained
lexical parameter snapshots to Rust DC/Newton and sparse AC. Read
`docs/pspice-switch-results.md`, `docs/pspice-switch-plan.md`, requirements
SEM-VSWITCH-001/002 and `evidence/pspice-switch-results.json` for exact scope.
Original named two-file bug883 reproducer is under compat/ngspice; expected
DC OUT0.05V and AC gain0.5+j0. Control-input AC derivative is also qualified.
High-impedance control differs from ngspice default1TOhm input loading; only
ideal-control-source output-voltage comparisons are qualified. No hysteretic SW,
general XSPICE, temperature/noise or broad transient qualification is claimed.
Configured initial-state checking now uses complete nonlinear residuals before
observers, fixing a missing-device-current hazard found in static review.
CLI selected-static-op is accepted; selected-linear-op remains a legacy alias.
No original928-row census rerun: historical35 DC solves/34 native matches/7 AC
files remain historical counts. SpiceSharp third-reference switch run is pending.
Do not launch more mutation-score work in preference to simulation progress.
Preserve unrelated main sheet-resistor WIP and keep bulk artifacts on E:.

## November 2 runtime readiness checkpoint

Pending full corpus rerun: linear E/G generated metadata now lowers through the
public Vcvs/Vccs wire types; CircuitReadiness reports multiple known structural
blockers with explicit incomplete/truncated/nonexhaustive flags. Read
`docs/runtime-readiness-controls-plan.md` and
`evidence/runtime-readiness-checkpoint.json` before claiming corpus growth.
Last completed corpus counts remain35 DC solves/34 native matches/7 selected AC.
Next: full corpus readiness census, controlled-source AC comparisons, model
families; bug883 PSpice VSWITCH is an important named end-to-end milestone.
SpiceSharp third-reference recurring integration remains pending. Preserve main's
unrelated sheet-resistor WIP. No new soak has been started by this checkpoint.

# Current continuation — November 2 corpus runtime priority

User priority: make more corpus files simulate; do not substitute mutation-score
work for runtime implementation. Read docs/corpus-source-execution-results.md,
evidence/corpus-sources-results.json and issue#21.
35 files solve selected DC on LU/Faer (up17),34 match native ngspice,1zero-port
has no root reference.29upstream paths+6repo examples;30distinct root hashes.
Seven of these also match selected1/100/1000Hz AC probes (138complex checks).
Generated DC/AC source clauses, explicit all-source AC replacements and qualified
acct/noacct/noinit reporting flags now feed retained Rust solvers. StrictLower
unchanged. No original control/sweep/transient/fullSPICE qualification.
928-row final census:34compared,1no-root,855lower refused,30resource refused,
8exclusions; no observed discrepancy/unfinished inputs.482.NET tests pass,1exFAT
skip. Coverage incomplete886/898lines98.6%,1074/1220branches88%; captured locals,
CLI coordinators and fresh native coverage unavailable. No new mutation/soak.
StrictDoc21docs/332IDs/320parents; SEM-SOURCE/REPORT-OPTIONS/ACPROBE remainDraft.
Raw artifacts E:/Artifacts/Spice-corpus-sources-20261102T152846Z.
Next runtime gaps: controlled/model families, waveform/transient execution and
explicit physical-option/global-node semantics. Continue native comparisons and
report unique solved/compared file counts. Preserve unrelated main sheet-resistor
WIP. Older checkpoint notes follow.
# Current continuation — November 2 Rust survivor follow-up

Read docs/rust-survivors-20261102.md and evidence/rust-survivors-20261102*.json.
Original overnight shard10/16 cohort:17 Rust survivors replayed;16 killed and
one raw AC guard survivor retained as conditional stable-InvalidInput equivalence,
not a formal proof. Two separately tracked importer time-grid field deletions
also killed. No invalid/timeouts/unattempted in this scoped replay; the original
whole shard's71 invalid and51 unattempted candidates remain unavailable evidence.
Production numerical/parser behavior unchanged; new independent tests cover
branch-current units, inclusive AC residual bound, BE intervals, VCVS/audit merge,
validation operands, window knots, tree node budget, log/interpolation endpoints,
PWL initialization, Newton defaults/identity and nominal-source negative times.
Final Rust regression475 passed/1 ignored endurance batch. Clippy -Dwarnings,
owned rustfmt, StrictDoc21 docs/329 IDs/317 parents, and5 paired ngspice fixtures
pass. Focused integration coverage1989/6142 lines32.38%; branch/private-unit/full
suite coverage unavailable. Proptest seed2026110201;256 cases per property in
regression/coverage,128 in mutation replay. Raw evidence E:/Artifacts/Spice-rust-survivors-20261102T142355Z.
Source/test checkpoint79caf69. Preserve unrelated main sheet-resistor WIP.
Next actual simulation work remains source DC/AC excitation profiles, selected
linear AC/transient and bounded models via generated frontend plus native oracles.
The17-file selected-DC frontend census is unchanged. No new long soak, .NET
mutation, Linux/GPU/NativeAOT/full-SPICE qualification. Earlier notes follow.
# Current continuation — actual frontend execution, November 1 Pacific

Read docs/frontend-corpus-simulation-results.md and evidence/frontend-execution-*.
Generated frontend now executes17 files on both Rust LU/Faer:13 ngspice sample
paths +4 repository examples,14 distinct root-byte hashes.16 independently match
native root voltages (96 provider/node checks); one zero-port file solves but has
no exposed root reference and remains unqualified. This is selected static DC,
NOT execution of original control/AC/transient programs.17 is not a direct update
to the old numerical importer's separate3-file comparison cohort.
OperatingPointSelection.Select explicitly prepares ideal R/C/L and constant V/I,
records all omissions with scope/range/reason and refuses unqualified directives
including globally physical effects in unused bodies. Strict Lower remains R/V/I.
Harness Run-FrontendCorpus.cs accepts an explicit profile/admission budget, hashes
actual source/assemblies/inputs, never executes original controls, and compares
expanded original source to typed requests. Native17-digit print fixes echo6-digit
rounding; zero root observables are unavailable, not mismatch. Bad provider data
are retained per backend; six synthetic fault modes never qualify Rust.
Final928-row census:16 compared,1 no-root-reference,873 lower refused,30 resource
refused,8 prior exclusions; no unfinished inputs or observed discrepancies.
Next:source DC/AC excitation profiles, selected linear AC/transient through this
path, then bounded model families using actual failures/ngspice oracles. Avoid
another grammar-only campaign unless a concrete execution case requires it.
406 Syntax +57 Frontend tests pass,1 exFAT skip. FsCheck907/908,20261101 each128
cases. StrictDoc328 IDs/316 parents and Sphinx pass. Coverage incomplete (captured
functions omitted); file-app coverage unavailable. Stryker initial22 kills/2survive,
zero-L guard replay catches89 (4kills);75 remains conditional equivalent under
internal Supported iff Value invariant, not formal proof.13compile errors remain.
All bulk artifacts E:/Artifacts/Spice-frontend-simulation-20261102T025251Z.
Preserve unrelated main sheet-resistor WIP. No CI,soak,new Rust build,GPU/Linux,
NativeAOT,full SPICE or production qualification. Older notes follow.

# Current continuation — November 1, 2026 Pacific

TABLE and explicit numerical-count checkpoint: read docs/table-syntax-results.md
and evidence/table-*.json. Contextual generated E/G TABLE recognition now supports
native coordinate separators, including MCP6041 wrappers. Syntax gate920/920;
resources890/920 (up11), vendor recognition13/14. TL072 terminal DOS EOF remains
inferred Draft PRS-EOF-001. TABLE adds no interpolation or model runtime.
Fresh numerical importer corpus:3/910 execute selected analyses;1 compared pass,
2 partial,666 unsupported,230 import rejected,11 input/resource refused. No
unfinished inputs or observed numeric mismatches; issue#18. Whole-corpus execution
through the generated frontend remains UNMEASURED. Next numerical integration must
measure that path instead of re-reading accepted decks through the old importer.
Repeat numerical matrix and named ngspice checks at each completed gap checkpoint;
lead with unique file counts and separate syntax/resource/binding/fixture metrics.
Named DC4 fixtures/12 comparisons/24 solves and repeated benchmark5 cases pass;
native TABLE10/10 values match. Debug process timings are not release rankings.
Delivery536 tests pass,1 exFAT skip; StrictDoc325 IDs/313 parents and Sphinx pass.
Coverage remains incomplete: captured functions/callbacks omitted; benchmark CLI
entrypoints have zero collector coverage despite executed CLI runs. Selected
Standard observer mutations3 killed,683 compile errors,1056 ignored; earlier
strict-grammar snapshot, byte-identical observer, no grammar/ATN mutation closure.
All bulk artifacts E:/Artifacts/Spice-table-20261102T015724Z; preserve unrelated
main sheet-resistor work. No CI, soak, full SPICE/GPU/Linux/production claim.

## Historical checkpoint notes

Older next-step/count statements below are superseded where the current checkpoint differs.

Semantic corpus results/fix checklist: https://github.com/jeffadickson/Spice/issues/14
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

Current frontend bridge checkpoint: see docs/simulation-bridge-plan.md,
docs/simulation-bridge.sdoc and docs/semantic-frontend.md. The generated frontend
now has a separate Simulator.Frontend R/V/I DC adapter to existing typed requests;
keep syntax, resource resolution, binding, lowering and numerical evidence apart.
Use faer as the wire backend key. No model/AC/transient bridge support is implied.
Continue bulk verification artifacts under CODEX_ARTIFACT_ROOT / E:\Artifacts.

Resource-policy checkpoint (550365f + evidence): see docs/resource-policy-results.md
and issue #16. Explicit parser budgets now reach resources/expanded parsing;
csparam exports alone no longer block unconditional includes. 876/920 valid root
decks resolve; 44 explicit resource refusals remain. Root syntax gate is920/920,
not vendor-fragment or numerical qualification. Next shared grammar target is
inferred Draft PRS-REF-001: punctuated node arguments in voltage references,
such as V(VC+,VC-), in reached vendor libraries. Keep syntax, resource, binding,
parameter and simulation results separate. Captured-function coverage still
unmeasured; all bulk artifacts remain on E: under the configured artifact root.

Voltage-reference grammar checkpoint: see docs/voltage-reference-results.md and
issue #17. Generated v(...) node contexts now preserve punctuation and physical
continuations. Root syntax remains 920/920; resources resolve 879/920. Next shared
grammar target is inferred Draft PRS-TABLE-001 (E/G TABLE tails), with separate
legacy MCP6041 grouping and TL072 DOS EOF adjudication. Native syntax agrees on
11 named probes; ten numeric values match, one scaled LIMIT result remains
unqualified. Existing DC regression still passes 12 comparisons/24 Rust solves.
Do not equate these stages or claim signal evaluation/behavioral support. Keep
bulk artifacts on E: and retain captured-function/lexer-ATN coverage limitations.

November2 waveform checkpoint: original928 inventory now51 selected static solves/50 native matches and14 selected AC files. Read docs/corpus-waveform-results.md and evidence/corpus-waveform-results.json. Original programs and broad transients remain unqualified; third-reference/current-transient work is next. Preserve main's sheet-resistor WIP.

Third reference checkpoint: read docs/third-reference-results.md. Original928 cohort:36 OP agreements (12withAC),13 unavailable,1 associativity discrepancy. Named bug883 independently three-wayOP+AC passed. Patched-reference flavor is explicit, never stock or production. No silent numerical equation patches.

Current/transient checkpoint: read docs/transient-current-results.md and evidence/transient-current-results.json. Four named fixtures executed16Rust+8native+8Sharp requests; three agree across all engines, delayed-phaseSIN retains1V/1mA Sharp discrepancy.58checks/54boundpasses/4discrepancies. Switch control-load analytical checks and step refinement are explicit. This does not increase original928 static/AC counts.

Overnight window: docs/verified-soak-window.md and evidence/verified-soak-preflight.json describe the pinned clean-checkout launcher and16contract tests. New18-method property campaign includes delayed-phaseSIN and analytic switch divider. Actual420-minute completion remains pending; read real E: summary, seeds, mutations and duration. Preserve canonical worker lock and existing main WIP.

Issue34 current checkpoint: see docs/issue34-progress.md and
 evidence/issue34-initial-bias.json. Optional transient initial_newton is now a
real Rust/client feature; rebuild the engine before executing newly selected
nonlinear transient requests (the earlier ground-only engine cannot accept that
field). Both retained original BJT requests complete200steps on faer without
injected warm state; LU still fails later componentwise refinement. Full native
waveform agreement remains open. Do not promote this replay into frozen corpus
counts. The clean26cfad9 ground census is85prepared/23executed/11agreements among
928paths and is tracked in issue35. Next: resolve faithful native transient-grid
comparison and the LU failure, then bounded shared controls (prepared refusal
observations let16/define8/unsafe9), named model/XSPICE subsets and resource triage.
Artifacts: E:/Artifacts/Spice-issue34-20261103T195804Z. Preserve canonical main's
seven unrelated sheet-resistor/WIP files and all historical/raw evidence. CI off.

Manual two-deck checkpoint (hourly automation remains paused): original ac-resistance now agrees for OP/AC/PZ and original assertions. Read docs/ordered-control-results.md and evidence/ordered-control-ac-resistance.json. Next finish original general/mosmem.cir startup and full transient comparison, without altered inputs or weaker tolerances. Preserve canonical main; local commits only; E: artifacts.
