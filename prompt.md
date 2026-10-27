# Spice qualification handoff — survivor triage after completed campaign

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
bounded semiconductor models. Automatic AC/configured physical audits, full DAE
qualification, coverage-guided fuzzing, broad SPICE syntax/devices, GPU and
production release gates remain open. The unitless legacy absolute_tolerance is
validated compatibility data; actual convergence uses voltage/current tolerances.
