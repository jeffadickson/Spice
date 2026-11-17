# Issue 61 active follow-up

User objective: address findings and close https://github.com/jeffadickson/Spice/issues/61 only when complete. The original run is old committed main0face5844c108bbf80e2af39a2d9ec275c76f703, NOT this development branch. Preserve canonical main and unknown resistor work. No CI/main merge. Fifty-deck objective remains2/50; this task adds no decks.

Checkout J:/dev/spice-deck-next (C:/dev alias), branch fix/deck-blocker-implementation. Completed development checkpoints:0030bd2 managed transport,0ca6eee analytical energy-audit regression,96b64ec mutation aggregation/observation tooling. All bulk/temp/logs under E:/Artifacts/Spice-issue61-20261117T133906Z (pointer work/issue61-artifacts.txt). Follow latest AGENTS: Windows manages memory/paging; no general caps or free-RAM admission gates. Observe mutation workers, intervene only on actual positively identified runaway growth; no such intervention observed so far. Retain deadlines/concurrency/locks.

## Completed evidence

- Managed Stryker5.0 final fm/20261117T135705Z-ddd2abae:204candidates,190killed,zero survivors/timeouts/uncovered,1compile-invalid,13tool-filtered. Normal Client92tests pass, Client-only90.34%line/92.57%branch. Bounded evidence/issue61-managed.json has exact candidate IDs/filter reasons, source/report hashes. Historical managed run preceded memory correction; do not rewrite its historical guards.
- Old full-suite raw audit misses: energy product audit.rs384:28 and energy-scale accumulator411:22. Current exact counterparts511:28 and543:22 both caught. New independent Shockley equilibrium, signed energy/tolerance, corruption and charged NotChecked checks pass. Native full proof current-audit-lean/20261117T145923Z-974ec93c:4killed,zero survivors/timeout/invalid/unattempted. Two candidates are explicit field-deletion extras outside original277; they survived narrow audit-only tests, then were caught by full aggregate. Never label those extras compile-invalid. Aggregate baseline42lib+617integration pass,5ignored. Standalone audit13pass, fixed RC property seed2026103006/32cases.
- Coverage audit-coverage-retry:src/audit.rs282/579mappedlines48.70%; selected audit integration only, not whole-project coverage. Stable Rust branches unavailable. First collection lacked Proptest rlib; preparation/retry retained. Pester inventory/options4pass, observer functional4pass, CSC standalone3pass, helper self-tests3pass. Observer parent-only command coverage3/117(2.56%) cannot instrument launched child bodies; Framework supervisor line/branch coverage unmeasured. See evidence/issue61-mutation-tooling.json.
- Intel circuit gate13pass,zero failed (real/complex solves, properties, divider, CLI lifecycle); trusted DLL SHA4353e81a729f56f106212c8799b23af3e74a6b1d20ab21a00c335bcfb870e955. Artifacts Spice-intel-circuit-tests-66a90adb46fd4874a3f83114b5943306. Intel UHD630 SpMV probe6scenarios/11,880,960values,maxabs0,seed18117,9repetitions; separate matrix probe, no whole-deck acceleration claim. Probe artifacts Spice-parallel-Intel-1325b53d55f84ecda3f694da73b6dc2f.
- Real StrictDoc parser97documents/583UIDssuccess (strictdoc-after-resume.json). Read-only review found no blockers in Client, observer lifecycle fixes, worker/profile controls or audit test. No Linux/full-corpus/long-fuzz claim for this task.

## Active serialized replay: do not duplicate

Original277pending identities come from E:/Artifacts/Spice-nightly-20261117T080301Z-0face58/fm/20261117T080340Z-69652ceb/rust/mutants.out. Old clean source checkout C:/dev/sp61-old-134641 pinned0face58; production Rust unchanged. Only qualification Cargo/profile/aggregate/CSC harness adaptations. Original5invalid diagnostic details retained in original-invalid-classified.json and bounded tooling evidence; invalid Result construction/type errors are not test kills.

Before library tier,16unique original pending candidates completed across completed-before-memory-policy-transition.json and completed-before-tiered-replay.json, including the2raw full-suite misses above. Remaining261 dispatched in9batches of at most32originals plus2explicit cargo-mutants27.1.0 field-deletion extras. Extras are NOT in original300inventory; deduplicate and report separately. Native inventories fail closed on missing/extra names.

Active library-tier worker:OldReplayTieredV3.ps1, session90474, observer observed-tiered-v3, rootPID24060/start2026-11-17T14:54:09.0040080Z. Use stored PID AND start ticks, never PID alone. First batch20261117T145410Z-e824c123 completed34nativecandidates(32original+2extras):4caught,17scopedmisses,13invalid. Batch1started15:08:13Z. Read old-replay-progress.json and actual native outcomes for latest status, not this snapshot. `rust-framework/<run>/rust/mutants.out/outcomes.json` is authoritative. Runs use lib first; EVERY miss needs full integration confirmation before calling it a full-suite survivor. Counts here are not all277completed qualification.

Queued full-confirmation worker:OldReplayFullConfirmation.ps1, session5197, observer observed-full-confirmation, rootPID56212/start2026-11-17T15:03:35.2393325Z. It asynchronously waits for identified library worker, requires all277unique originals accounted, then replays EVERY library miss in bounded full lib,mutation_suite batches, including two extras. On unfinished/failed library stage it stops honestly. Progress full-confirm-progress.json appears when dispatched; completion state is completed_needs_findings_review, NOT issue closure. Do not kill the queued waiter or start another confirmation job.

Immutable helperV3:FrameworkQualifiedV3.cs plus framework-qualified-v3-build/bin/FrameworkQualifiedV3.cs/release/FrameworkQualifiedV3.dll. Both workers use it. Mutation cache rust-framework/rust-target is separate from normal qualification-target and current-audit-lean/rust-target. Clean owned package in ACTUAL profile before baseline. V3 allows at most300seconds for cleanup inside remaining budget; a60second cleanup failed before baseline in20261117T145004Z-75c60761 and remains setup failure. Default test/product profiles unchanged; opt-in mutants uses debug0/incrementalfalse/codegen16. Observed exFAT hard-link fallback motivated it; no whole-campaign speedup claim, since test target also changed.

Historical retries preserve evidence: wrong old feature insertion, wrong-profile clean, stopped package-cache copy before mutants, policy-transition restart retaining6outcomes, active observerv1 snapshot, lock-rejected full-proof preflight and run-specific JSON-DateTime comparison failure. Current proof eventually completed correctly above. Interrupted candidates are never kills. Do not borrow correspondence from failed runs.

## Next actions

1. Keep monitoring both owned workers/native results without duplicating, and preserve raw old-source outcomes. Full-suite misses need current-source disposition, not retroactive relabeling.
2. Current native inventory current-native-inventory.json has6244entries/SHAab10e493bf6217b4ee9dc5cf03ba84608beda9901475604810bce7d34e0d28a3. Conservative current-context-mapping.json matches254/300 by native identity and unchanged five-line context;46require manual review. This is not qualification. In particular the scale-accumulator mapping needed manual source inspection and was independently verified at current543:22. Never reuse the earlier loose description-only mapping (ambiguous998matches).
3. When full confirmation finishes, investigate real survivors. Map exact current candidates using native file/genre/replacement/function/context and manual source review as needed; record missing/changed mappings explicitly. Add meaningful regressions/StrictDoc and run native current-source proofs. Existing current-audit-lean cache may be reused only serialized with correct clean/profile.
4. Qualify current development scope separately, finish bounded manifest/report and review, commit/push verified checkpoints, update issue61 checklists/comment, and close ONLY when findings complete. Managed three checklist items checked; Rust/current-qualification remain open. No claim all shards/fullSPICE/GPU/production.
## Latest checkpoint: after176c187 (see artifact findings-progress.json)

All277original pending candidates have native outcomes. Library tier finished16:08:46UTC. Its261originals had23caught/84invalid/154scopedmisses;18repeated supplemental fields are excluded. The earlier16full-suite outcomes were13caught/1invalid/2misses. Full confirmation of every154librarymiss is running through5batches in OldReplayFullConfirmation.ps1, same rootPID56212/start15:03:35.2393325Z. Old full baseline is23library+523integration pass/2ignored (NOT current42+617). Read Read-ReplayProgress.ps1 for actual progress/new raw survivors. Do not duplicate the jobs or close61 yet.

New current fixes:abcca9d campaign inclusive64boundary (5tests, native1killed,31/37mappedcampaign lines);176c187 controlled-source isolated index/gain validation (21tests, native1killed,65/194mappedcircuit lines). Both pushed. New uncommitted temporal tests pass:initial_newton12 and nonlinear_half_step1; current exact initial178:56 nativekill20261117T165255Z-4af4bc0b. Old exact half302:23 killed20261117T170308Z-d545e9ae with the shared new fixture in immutable copied old source half-old-source; current nonlinear_trial macro argument has no direct cargo-mutants27.1 counterpart. Record that limitation, not a fabricated current kill.

Reviewer caught a wrong proposed time-guard waiver: report73is the stop-time connective, not neighboring71finiteness. Optional Z3 prototype's encoded property was valid for the WRONG OPERATOR. Nothing was committed/waived/closed from it. Own draft code/docs/test/manifest archived and hash-verified before removal from repo under rejected-neighbor-proof; invalidated-disposition.json is authoritative. Do not restore that waiver or optional dependency. Native diffs/spans must bind dispositions; whole-fragment presence/hand-counted lines are insufficient. Correct new stop-time test in analysis_audits14passes; exact current73mutant is killed20261117T172012Z-ada898ba.

Other pending work:analyses10tests pass after correcting an unsupported device-looking title to the already admitted RC header (initial failure retained). New tests check geometric INTERIORS and exact1MiB native input acceptance, not just endpoints/oversize. Need exact current grid17:77 and netlist14:19 proofs. Fileglob src/{grid,netlist}.rs works and emits two mandatory netlist676/677extras; include both and tests analyses,ngspice_analysis_cards to kill them in scope. Current src/newton.rs has ONLY appended cfg(test) tests:distinct zero-magnitude A/V row floors and isolated source/Gmin refusal before provider calls;44libtests pass. CurrentNewtonProof.ps1/session54608 replays exact601:42 and1173:46, inspect final status before claiming success. Production expressions/line positions unchanged.

Potential conditional equivalents require actual native diff review:netlist362minus4->plus4 changes evenness by8 under len>=6 and valid Vec capacity; old newton112firststar divides literal polarity±1 (not gm), now implemented in stamp_four_terminal. No artificial test or test-kill claim. Remaining raw survivors must be inspected rather than guessed.

Requirements now real-parsed101documents/590UIDs (strictdoc-additional-fixes.json); new docs native-input-grids.sdoc and initial-and-half-qualification.sdoc. Existing newton-merit/fourth-slice trace updates. Measure-RustIntegrationCoverage remains normal isolated target; collect refreshed audit/initial/half/analyses and report scopes. New cfg(test) code can be functionally verified by native --lib; stable Rust branches unavailable. Final aggregate/Intel gates still needed after all fixes. Independent charged-diode ngspice47+LU/Faer gate separately passed1test, not an original deck.

A standalone dotnet-coverage18.5.1 tool was installed in the artifact root only (no global PATH/product dependency change) during rejected prototype evaluation; it may be reused. Rejected-prototype92.65%line/68.75%branch applies only to that abandoned optional runner, not simulator qualification. All its raw outputs remain local. No general RAM caps; source/main/unknown work preserved. Fifty objective2/50.
## 17:54 UTC continuation

Verified checkpoints e4fac39 (temporal boundaries, exact old nonlinear half-step
proof) and701efec (shared Newton full-MNA validator, dimensional row floors).
The shared validator now serves solve_from, solve_with_rhs and trial assembly;
existing caller diagnostics are preserved. Current exact row-floor and helper
mutations both killed in20261117T174257Z-8585e69e. Original masked duplicated
guard remains a raw survivor, not retroactively a kill.

Normal current aggregate:45unit+628integration passing,5explicit integration
skips. Separate ngspice charged-diode oracle1pass and18property methods pass
(seed2026111701,128cases requested per generated property; individual methods
can retain their own pinned seed). New waveform boundary/sign tests6pass.

Sampling adjacent endpoint and overflow-width bugs were reproduced before
fixing; twelve sampling tests pass, mapped sampling line coverage77/81 (95.06%).
Current final coverage reports are under final-coverage-*, final-sampling-fixed-
coverage and waveform-final-coverage. Stable branch coverage is unavailable.
Nine grid/parser/sampling native candidates are still running in
20261117T174625Z-861121ae; queue WaveformBoundaryProof.ps1 only after its cache
lock releases. Never run two jobs against current-audit-lean/rust-target.

Old full confirmation remains active;147/154 last checked,14raw survivors.
New original waveform candidates are221:34 (equal exponential delays) and295:73
(sine phase sign); both have independent regressions, native proof queued.
Final original cohort consolidation and issue closure remain pending. Conditional
equivalence argument/diff hashes for exactly two candidates are in
issue61-conditional-equivalents.md and the paired evidence JSON, reviewed
read-only with no blocker. No discarded neighboring-predicate proof is accepted.
Intel circuit and separate SpMV refresh still running; do not claim results
from the earlier run. All bulk outputs remain under the E: issue61 root.

## Final disposition

Supersedes the partial counts above: original277 all completed; old300 totals
191caught,90compile-invalid,19raw survivors,0unattempted. All154 library misses
received full confirmation (137caught,17raw survivors). The19findings now have
15regression dispositions,one shared-validator replacement and three conditional
equivalence dispositions. Targeted native followups24/24caught including explicitly
labeled old-snapshot half-step proof; no current macro mutation is claimed.

Final checks45unit+632integration pass,5explicit integration skips; separate
ngspice1pass,property18pass,Intel15pass,Pester8pass,StrictDoc104documents596UID.
Waveform8tests/4nativekills. Separate Intel SpMV6scenarios11,880,960values,maxabs0.
See issue61-resolution.md and evidence/issue61-replay-closure.json for limits.
All observers/workers finished; no workload was killed for memory. Only the
historical initial transition artifacts retain prior policy evidence.

Verified product checkpoints0030bd2,0ca6eee,96b64ec,347a569,abcca9d,176c187,
e4fac39,701efec,5184457,2ba0a15. Final evidence commit/push and issue closure follow;
main remains0face58 and its unknown local resistor/documentation work is preserved.
No new original corpus decks; fifty-deck tally stays2/50. No continuation campaign
needs launching for issue61. Raw E: artifacts must remain local and preserved.
