# Issue 34 implementation progress

The execution plan is `issue34-execution-plan.md`; the full baseline remains
928 inputs /81 prepared /21 executed /9 agreeing on frozen `fc5db25` until a new
complete census is published. Finite fixtures below do not replace that census.

## Ground semantics checkpoint

`GroundAliasPolicy` and immutable `GroundBinding` reuse generated ANTLR metadata.
Ground binding is separate from MNA emission. Ordinary selected preparation
normalizes exact case-insensitive `gnd` references before node allocation and
formal-port substitution. Automatically grounded formals ignore their actual
without creating a floating unknown. Strict legacy R/V/I lowering is unchanged.

The explicit policies are Automatic, Ngspice, Pspice and Disabled. Automatic
chooses the PSpice exception when an active VSWITCH requires native `psa` mode.
Disabled matches external `no_auto_gnd`; ordinary `GND` can then be a driven node.
The PSpice header predicate follows the inspected native plain-identifier rule,
including names and parameter text. Unqualified nested/protected-global contexts
and native-unreplaced bracket-adjacent aliases remain explicit refusals.

Native investigation also exposed invalid zero controls after normalization.
Bound ground-voltage terms are now omitted exactly; differential zero controls
become zero voltage constraints or zero currents, retaining their physical source
kind and branch identity. Introduced sources receive explicit zero AC excitation.
Engine validation and numerical tolerances were not relaxed.

Current checks: **498 frontend tests passed;20 external-prerequisite skips**.
The focused group passed 53 tests, including 200 FsCheck cases (replay
`363,20261103`).42 native original-fixture OP/AC comparisons pass across LU and
faer, including formal modes, globals, header exceptions and zero controls.
The public `examples/GroundPolicy.cs` executes and independently checks the 2 V
reference plus 1 V source giving 3 V output under Disabled normalization.

Scoped Coverlet mappings: GroundBinding 37/37 lines,77/80 branches;
OperatingPointBridge 72/72 mapped lines,88/94 mapped branches. Captured emitter
functions are not all represented by this collector, so these are **not**
whole-emitter coverage percentages. No new Rust coverage applies to this C# fix.

Stryker.NET 5.0.0 selected GroundBinding:76 killed,3 survived,1 uncovered,
3 compile errors,7 framework-filtered. The three survivors weaken defensive
range boundaries that valid generated vector ranges do not reach; they remain
survivors, not kills or a formal equivalence result. One nested-vector continuation
path is unmeasured. Compiler-invalid candidates remain unavailable. Broader
frontend mutants generated outside this file are not qualified by this campaign.

Requirements: SEM-GROUND-ALIAS-001/002/003. The latter two remain Draft pending
original-corpus reconciliation. Real StrictDoc parses 49 documents,404 IDs and 379
parent links. No full SPICE, GPU, Linux or production qualification is claimed.

## Current BJT diagnosis

The bounded public-API replay on the current qualified engine confirms the same
two distinct failure classes in both retained original amplifier requests:
default initial OP hits the Newton limit; source continuation recovers bias;
the full LU warm transient later fails componentwise backward-error refinement.
Faer completes the 200-step warm transient. This is convergence evidence only,
not original-waveform agreement. The next numerical change must expose explicit
initial-OP policy without weakening final residuals or hiding provider failures.

## Independent native sampling experiment

`scripts/Probe-NativeTimeGrid.cs` reuses the existing bounded ASCII RAW reader and
an analytical 1 V/1 kHz sine circuit. Native's ordinary 208-point grid misses 199 of
201 requested times; linear interpolation gives 0.0003978907140934629 V maximum
error. A disconnected-from-original-nodes auxiliary PWL voltage clock creates
1004 native points, includes all 201 requested times, and yields
3.497202527569243e-15 V maximum sine error. The original physical cards and `.tran`
settings remain unchanged in this finite experiment.

That reproduces the previously observed simple-meas error using a memoryless
ideal source, independently of our simulator. This is **not yet a production
oracle change**. Before adopting it, qualify auxiliary-node isolation, identifier
collisions, axes/budgets, invariance and nontrivial dynamic circuits; retain all
instrumentation and refuse missing exact points. No failed historical comparison
was reclassified. The probe's mapped coverage is 46/46 lines and 41/60 branches;
malformed/native-failure paths are not fully covered.

Raw inputs, requests, responses, native RAW/logs, coverage and mutation reports
remain under `E:/Artifacts/Spice-issue34-20261103T195804Z`. Bounded evidence:
`evidence/issue34-ground-checkpoint.json`. Existing main sheet-resistor changes
remain untouched; no CI dispatched.

## Frozen ground checkpoint census

Clean revision `26cfad91fd398fa14ed1de1ae8974f454a348409` completed the same
928-input inventory in 906.024 seconds, with provenance_valid=true. Counts:
916 syntax accepted,85 circuit prepared,23 requested analyses executed,
11 native numerical agreements. Compared with fc5db25 this is +4 preparations,
+2 executions and +2 agreements. Twelve numerical disagreements remain;
905 inputs have no native-agreement evidence. The two ordinary rc-meas-ac.sp
paths now execute and agree; both control-program variants prepare but remain
explicit requested-preparation refusals. These are file counts, including mirrors,
not unique circuit counts. Raw summary, per-case evidence and burndown are in
`ground-census` beneath the current E: artifact directory. New BJT work is excluded
from this immutable checkpoint.

## Explicit initial-bias checkpoint

Configured transient requests now expose optional initial_newton policy, with
actual initialization evidence returned in IntegrationInfo.InitialNewton. Selected
nonlinear frontend requests explicitly use the existing50-iteration/64-stage
source continuation defaults. Subsequent step settings and provider remain
unchanged. Different time-zero/nominal RHS refuses continuation; explicit state,
fixed Gmin and linear/inapplicable policy combinations refuse. Direct initial
bias continues honoring actual source overrides. New StrictDoc contracts
SIM-TRAN-INITIAL-NEWTON-001/002 remain Draft pending waveform qualification.

The retained original amplifier requests0458/0521 now complete200steps/201samples
to0.002s with faer, without injecting a warm state. Initial continuation reaches
source_scale1 in4accepted stages,105provider corrections and4.405e-19 initial
residual. Both LU runs still fail later componentwise backward-error refinement.
No original census result or native-waveform agreement is promoted by this replay.

Verification:540Rust regression tests passed,2ignored; the expanded initial-bias
suite has10passing tests including64Proptest cases, replay3420261103. A changing
PWL current with one-iteration initial policy requires multiple later corrections;
a recording provider checks exact solve accounting. Staged Gmin asserts physical
shunt removal. Managed frontend regression:486passed,34prerequisite skips;
this differs in prerequisite availability from the earlier ground checkpoint.
Five focused cargo-mutants27.1.0 candidates were caught, none survived/timed out/
failed compilation in the final selected campaign. Two interrupted launcher
attempts tested no candidates and remain raw incomplete evidence. Scope is five
guards, not all configured-transient mutations. The fresh read-only review found
no confirmed defect; both identified test gaps were addressed by changing-source
and provider-accounting regressions.

The faer JSON example transient-initial-bias.request.json independently agrees
with inverse Shockley at0.5356964219V. No C# mutation campaign for the new dispatch
was run; Rust branch coverage is unavailable. Exact current coverage and hashes
are retained separately in evidence/issue34-initial-bias.json. No CI or automatic
backend substitution was used. Full native BJT waveform qualification and the
remaining issue34 items stay open.
