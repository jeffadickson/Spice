<!-- Readable projection; authoritative source: implementation-plan.sdoc -->

## Circuit Simulator - Implementation Plan and Work Packages

Version 0.1, planning baseline, 2026-10-24.

Purpose: turn the architecture into executable work with explicit prerequisites, reviewable deliverables and measurable exit gates. This plan is authoritative inside the local repository together with architecture.sdoc and development-standards.sdoc. Earlier external drafts are historical and are not automatically imported into release scope.

This plan was adopted before implementation. A bounded reference prototype now exists; implementation-status.md records partial progress. Every full work package remains Proposed until its exit criteria are evidenced. Dependencies describe logical prerequisites, not completed work. Parent relations link work packages to the architecture requirements they deliver; they do not prove satisfaction. Work-package IDs remain stable as estimates and scope evolve.

Metadata convention in each COMMENT: Phase, Depends-on, Accountable-role, Deliverables, Verification and Estimate. Depends-on is a comma-separated list of PLAN-* IDs or none. This convention supports a derived backlog.json; changing that export does not change the authoritative plan. Dependencies must resolve and form an acyclic graph.

Roles are responsibilities, not staffed positions: core engineer, numerical lead, interoperability engineer, model engineer and verification lead. One person may hold several roles. No hiring or agent delegation is authorized by this plan.

Scheduling is gate-based. M0 is initially budgeted at 4-8 engineer-weeks across focused experiments, with re-estimation after evidence. A useful qualified M1 remains a rough 2-4 engineer / 12-24 month planning envelope dependent on model/dialect scope and specialist availability. Per-package estimates below are sizing labels, not additive calendar promises: Small = about 1 engineer-week, Medium = 2-4 engineer-weeks, Large = 1-3 engineer-months, Research = time-box then re-estimate. Backend/model breadth and advanced numerical research are intentionally deferred. Do not interpret these labels as committed delivery dates.

Current decisions: Rust-owned core is the recommended baseline; C/C++ shims reuse numerical libraries; C#/F# are first-class API/automation clients; C# may host a future GUI. Final language/backend/DSL selections require M0 evidence. Starlark is a candidate simulation-definition frontend, not an approved mandatory dependency or Python scripting default.


## 1. Milestone gates and execution strategy

M0 feasibility exit: equation and matrix conventions reviewed; actual CPU real/complex solve and C#/F# round trip demonstrated; provider/license and runtime packaging decisions recorded; DAE/compiled-model/GPU/DSL experiments classified as feasible, deferred or rejected. No native GPU tests are reported as passed without hardware. A deferred advanced experiment must not block a CPU vertical slice unless it invalidates a core contract.

M1a operating-point slice: a managed client submits a structured resistor/source circuit, obtains a converged operating point and a typed manifest, observes a singular/invalid case, and cancels a bounded worker job. CI retains actual coverage and regression evidence.

M1b analog slice: the same architecture supports AC and transient RC/RLC plus qualified nonlinear devices, explicit rollback, streamed Arrow results and measurements/assertions. C# and F# examples run on the declared supported-platform matrix. There is no GUI dependency.

M1 qualification exit: analytic/convergence/fault/API suites pass approved fixture bounds; coverage, mutation, fuzz and random/property reports are retained; at least two independent linear providers pass the shared subset before interchangeable-backend claims; release scope, licenses and known limitations are explicit. Model validity is distinct from numerical convergence. No universal coverage percentage or speedup is assumed.

M2 production extension exit: compiled models, GPU adapter, selected DSL, statistics and additional analyses are individually evidence-gated. Unsupported combinations remain unsupported. A CPU release need not wait for every M2 feature.

M3 research: periodic/RF, distributed single-circuit solving, full mixed-signal kernels and broad dialect/vendor parity require separate scoped plans. The GUI is a separate consumer after the API is useful, not a prerequisite for simulation.

Execution principle: use a small number of modules/crates first and enforce dependency direction. Split packages when boundaries are proven. Each work package closes with a reviewable change, linked requirements, tests, comments and actual coverage where code is changed. Infrastructure, numerical accuracy and fault testing grow with each slice rather than being postponed to a final test phase.


## 2. M0 foundations and feasibility

**PLAN-001**

## Establish repository, toolchains and verification harness

The implementation team shall establish pinned Rust/.NET/native build tooling, minimal module boundaries, automated StrictDoc checks and executable test/coverage reporting before substantial simulator code is added.


Phase: M0
Depends-on: none
Accountable-role: core engineer and verification lead
Deliverables: toolchain/dependency policy, minimal Rust workspace and managed test client, documented PowerShell entry points, CI jobs, dependency-direction checks and coverage/mutation tool evaluation. Choose a source license before distributing implementation dependencies; retain component/transitive license records. Repo/docs creation in this planning baseline does not complete the implementation package.
Verification: Build clean CPU-only examples, execute meaningful smoke/failure tests, parse all StrictDoc files and publish real coverage scopes/tool versions. Check Windows x86-64 and Linux x86-64 first; explicitly review macOS arm64 support before the M1 release profile is frozen.
Estimate: Small to Medium; revisit after toolchain/native packaging inspection.

Parent: ARCH-DEP-001
Parent: ARCH-VERIFY-003

**PLAN-002**

## Freeze equation, unit and model-state conventions

The numerical lead shall specify and demonstrate residual, charge, derivative, polarity, dimensional scaling, limiting correction and accepted/trial state conventions using independently derived small devices and circuits.


Phase: M0
Depends-on: PLAN-001
Accountable-role: numerical lead
Deliverables: lower-level StrictDoc equations/contracts, R/C/L/source and diode reference fixtures, derivative checks, known singular cases, initial-condition semantics and fixture-specific error bounds.
Verification: Compare assembled equations, Jacobians and charge derivatives to analytic/independent references; detect deliberate sign and derivative mutations. Record why solver tolerances do not imply global waveform accuracy.
Estimate: Medium; limit initial devices to those needed for the first analog slice.

Parent: ARCH-EQ-001
Parent: ARCH-MOD-002

**PLAN-003**

## Evaluate CPU providers and Rust/C++ integration

The team shall exercise at least two candidate independent linear providers through narrow adapters and use actual integration, ownership, packaging and workload evidence to confirm the implementation-language and initial CPU-backend decisions.


Phase: M0
Depends-on: PLAN-001, PLAN-002
Accountable-role: numerical lead and interoperability engineer
Deliverables: sparse real/complex matrix corpus; CPU direct-provider spikes, preferably KLU plus an independently implemented candidate; license/build/index/precision review; measured coarse-call adapter cost; Rust versus C++ decision record. Do not count two wrappers of the same factorization as independent providers.
Verification: Check residuals, transpose/complex behavior, nonsymmetric/indefinite/singular cases and repeated-value/structural invalidation. Publish clean-build and end-to-end measurements; select rather than assume native-library compatibility.
Estimate: Medium, time-box unsuccessful provider builds and record rejection reasons.

Parent: ARCH-LANG-001
Parent: ARCH-PORT-002

**PLAN-004**

## Prove Rust-to-.NET result and lifecycle contracts

The team shall demonstrate typed request/result interchange, explicit ownership and cancellation between the candidate native core and both C# and F# clients before stabilizing the public interface.


Phase: M0
Depends-on: PLAN-001
Accountable-role: interoperability engineer
Deliverables: versioned schema sketches/golden fixtures, minimal C ABI/worker round trip, SafeHandle disposal tests, bounded event queue and Arrow .NET batch reading experiment. This spike may use a deterministic fixture operation; label it as a fixture rather than a simulator.
Verification: Test malformed lengths, unknown versions, stale handles, slow readers, client disposal, native exceptions/panics and worker crash classification. Run C# file-based and F# Interactive examples without Python.
Estimate: Medium.

Parent: ARCH-API-001
Parent: ARCH-API-002

**PLAN-005**

## Validate external nonlinear/DAE integration fit

The numerical lead shall time-box an external nonlinear or DAE integration experiment and establish whether its callback, initialization and controller-ownership semantics fit nonlinear-charge circuit equations.


Phase: M0
Depends-on: PLAN-002, PLAN-003
Accountable-role: numerical lead
Deliverables: SUNDIALS or PETSc adapter experiment, documented residual/Jacobian mapping, one nonlinear-charge fixture, algebraic constraint and explicit-time cases, integration-versus-reimplementation decision. Deferred adoption does not imply rejection of the role interface.
Verification: Independently differentiate the actual continuous-residual callback and compare transient/event/initialization results. Confirm exactly one Newton and one timestep controller own their respective histories.
Estimate: Research; initially 1-2 engineer-weeks, then adopt, defer or narrow scope.

Parent: ARCH-PORT-004
Parent: ARCH-PORT-005

**PLAN-006**

## Evaluate compiled-model, GPU and DSL boundaries

The team shall conduct separately bounded model-plugin, GPU-provider and definition-frontend experiments before committing to their M2 implementations, recording unsupported hardware/runtime and licensing constraints truthfully.


Phase: M0
Depends-on: PLAN-002, PLAN-003, PLAN-004
Accountable-role: model engineer, numerical lead and interoperability engineer
Deliverables: pinned OSDI descriptor/evaluation mapping for one model; a real GPU solve experiment if supported hardware exists; Starlark/F# authoring comparison yielding the same canonical plan. Record separate adopt/defer/reject decisions. C#/F# remains the trusted automation path.
Verification: Check model charge/derivatives and rollback semantics; include transfers in GPU measurements; test DSL import/expansion limits, units and source mapping. A missing GPU is an explicit unexecuted experiment, not a simulated hardware pass.
Estimate: Research; separate short time boxes totaling initially 2-3 engineer-weeks; can continue after the CPU slice begins if no core-contract risk remains.

Parent: ARCH-MOD-003
Parent: ARCH-GPU-002
Parent: ARCH-DSL-001

## 3. M1a operating-point vertical slice

**PLAN-007**

## Implement immutable circuit and plan compilation

The compiler shall turn validated structured circuit definitions into immutable elaborated circuits and execution plans with typed identities, unknown/state layout, deterministic sparse stamps and invalidation keys.


Phase: M1a
Depends-on: PLAN-002, PLAN-003, PLAN-004
Accountable-role: core engineer
Deliverables: minimal sim-types/IR/compiler modules, quantity validation, topology diagnostics, signal catalog, model bindings, source spans and structural/numeric identity rules. Start with structured input rather than broad netlist compatibility.
Verification: Test invalid references/units, floating islands, ideal constraints, stamp bounds, equivalent node/hierarchy transformations and structural-parameter invalidation using seeded property tests and fuzz fixtures.
Estimate: Medium to Large.

Parent: ARCH-STATE-002
Parent: ARCH-STATE-003

**PLAN-008**

## Productionize the first CPU backend

The selected CPU sparse adapter shall implement validated patterns, factor/solve lifecycle, session ownership, bounded resource/error handling and effective numerical reporting behind the circuit-independent API.


Phase: M1a
Depends-on: PLAN-003
Accountable-role: numerical lead
Deliverables: backend factory/capabilities, direct-linear session, pattern/value invalidation, real/complex support needed for M1, native lifecycle tests and CPU-only packaging.
Verification: Pass the shared matrix corpus and fault/lifetime tests, compare residuals to independent references and report wrapper/native coverage separately. Never report a vendor success code as circuit convergence.
Estimate: Medium to Large, depending on native packaging.

Parent: ARCH-LIN-001
Parent: ARCH-LIN-003
Parent: ARCH-LIN-004

**PLAN-009**

## Implement baseline device evaluation and assembly

Built-in resistor, source, capacitor, inductor and initial nonlinear-device models shall use the declared model contract and instance-local residual/charge/derivative buffers with deterministic assembly.


Phase: M1a
Depends-on: PLAN-002, PLAN-007
Accountable-role: model engineer
Deliverables: model descriptors, validated parameters/domains, local/global bindings, state layout and observables. Qualify R/source DC first, then C/L dynamic contributions and diode behavior; additional transistor profiles require separate reviewed equations and corpus scope.
Verification: Compare device values/derivatives independently, test sign/polarity and malformed model descriptors, and retain mutation tests that kill incorrect stamp/charge contributions.
Estimate: Large; sequence device subsets rather than wait for the complete list.

Parent: ARCH-MOD-001
Parent: ARCH-EQ-001

**PLAN-010**

## Deliver operating point and bounded nonlinear solve

Operating-point analysis shall combine qualified model equations and linear solves with scaled physical residual/update checks, finite-value validation, bounded continuation and recorded convergence diagnostics.


Phase: M1a
Depends-on: PLAN-007, PLAN-008, PLAN-009
Accountable-role: numerical lead
Deliverables: DC driver, qualified Newton implementation or adopted controller, source/gmin recovery policy, explicit initial guesses and regularization records. Declare controller choice using M0 evidence; PLAN-005 is required only if that external controller is selected.
Verification: Check analytic linear/diode circuits, tiny-update/large-residual rejection, overflow, singularity, hard starts and multiple solutions without uniqueness claims.
Estimate: Large.

Parent: ARCH-EQ-002
Parent: ARCH-EQ-003

**PLAN-011**

## Implement bounded worker jobs and typed results

The runtime shall expose validated requests, legal job transitions, cancellation, quotas, structured errors and truthful result manifests through a local worker and trusted in-process facade.


Phase: M1a
Depends-on: PLAN-004, PLAN-007, PLAN-010
Accountable-role: core engineer and interoperability engineer
Deliverables: job/attempt IDs, CPU thread/resource budgets, local IPC version/frame policy, C ABI ownership and managed disposal/cancellation, minimal manifest and measurement descriptors. Native model sandbox claims are deferred until PLAN-019 passes.
Verification: Run a C#/F# resistor/diode case; test invalid/singular inputs, terminal-state races, late cancellation, blocked consumers, worker crashes and stale handles. Publish API conformance and coverage evidence.
Estimate: Large.

Parent: ARCH-RUN-001
Parent: ARCH-API-003
Parent: ARCH-API-004

## 4. M1b AC, transient and usable results

**PLAN-012**

## Implement AC from operating-point linearization

AC analysis shall use the recorded operating point and the same device derivatives to assemble and solve the complex small-signal system on explicit frequency axes.


Phase: M1b
Depends-on: PLAN-008, PLAN-009, PLAN-010, PLAN-011
Accountable-role: numerical lead
Deliverables: frequency grids, complex source/excitation rules, G+j*omega*C assembly, axis/signal descriptors and phase/reference conventions.
Verification: Compare analytic RC/RLC/controlled-source transfer functions and complex values, reject unsupported model capabilities and verify source polarity and frequency units.
Estimate: Medium to Large.

Parent: ARCH-EQ-001
Parent: ARCH-DATA-001

**PLAN-013**

## Implement transient integration and transactional state

Transient analysis shall support documented initial conditions, adaptive error-controlled integration, event breakpoints and full accepted/trial state rollback with exactly one numerical controller owner.


Phase: M1b
Depends-on: PLAN-009, PLAN-010, PLAN-011
Accountable-role: numerical lead and model engineer
Deliverables: start with backward Euler, then qualified trapezoidal/BDF scope; predictors, history buffers, local error/scaling, source breakpoints, model events and requested output-grid semantics. External integrator adoption additionally requires PLAN-005 evidence.
Verification: Check analytic RC/RLC, charged initial states, stiff/switching cases, event-edge timing and forced rejection with identical accepted-state replay. Retain state-commit and error-control mutation checks.
Estimate: Large; high numerical risk and likely multiple qualification iterations.

Parent: ARCH-EQ-004
Parent: ARCH-EQ-005
Parent: ARCH-PORT-004

**PLAN-014**

## Deliver streaming, measurement and durable evidence

The results layer shall provide bounded Arrow batches, selective recording, typed measurements/assertions and integrity-checked manifests with correct partial/failure and publication semantics.


Phase: M1b
Depends-on: PLAN-011, PLAN-012, PLAN-013
Accountable-role: interoperability engineer and verification lead
Deliverables: Arrow stream/file distinction, batch envelopes, signal/axis/complex schemas, measurement windows/interpolation, assertion indeterminate policy, staged artifacts, hashes and a managed reader. Add Parquet only when campaign workflows justify it.
Verification: Test independent Arrow readers, throttling/memory bounds, missing/duplicate batches, disk-full, corrupted artifacts, incomplete windows and atomic manifest publication. Run the RC transient through C# and F# end to end.
Estimate: Large.

Parent: ARCH-DATA-001
Parent: ARCH-DATA-002
Parent: ARCH-DATA-003

**PLAN-015**

## Add a scoped SPICE compatibility frontend

A documented SPICE subset shall translate to canonical inputs with source-mapped hierarchy/parameter diagnostics and explicit dialect/profile provenance, without importing a legacy runtime.


Phase: M1b
Depends-on: PLAN-007, PLAN-010
Accountable-role: core engineer
Deliverables: lexer/parser, include policy, hierarchical parameters, suffix rules, construct support table and minimal interoperability corpus. Broad LTspice/PSpice control-language parity remains outside M1.
Verification: Fuzz lexical/expression/include boundaries, test M versus MEG and expansion errors, compare equivalent text/JSON plans and report parse/elaborate/solve compatibility separately.
Estimate: Large, scope constrained by a reviewed construct whitelist.

Parent: ARCH-COMPAT-001

## 5. M1 qualification and backend variety

**PLAN-016**

## Qualify a second independent provider and selection policy

The solver registry shall expose at least two independently qualified linear providers on their common supported subset, with explicit capabilities, validated options and composition/fallback provenance.


Phase: M1
Depends-on: PLAN-003, PLAN-008, PLAN-012, PLAN-013
Accountable-role: numerical lead
Deliverables: second adapter, shared conformance results, roles/options schema, session/cache validation and backend selection/reporting. An iterative provider is not presented as a drop-in direct solver without convergence evidence.
Verification: Compare circuit/matrix residuals and outcomes, test unsupported scalar/index/placement combinations, misspelled options and changed-build cache invalidation. Distinguish changed convergence paths from semantic incompatibility.
Estimate: Medium to Large.

Parent: ARCH-PORT-001
Parent: ARCH-PORT-002
Parent: ARCH-PORT-003
Parent: ARCH-PORT-006

**PLAN-017**

## Maintain continuous numerical and adversarial qualification

Every implemented slice shall extend an independently justified numerical, stateful, malformed-input, fuzz, mutation and randomized/property corpus and report actual code coverage and limitations.


Phase: M1
Depends-on: PLAN-001, PLAN-002
Accountable-role: verification lead
Deliverables: ongoing matrix/device/circuit/API suites, bounded PR fuzz/property runs, documented longer manually or CI-triggered campaigns, minimized replay fixtures, coverage scopes/exclusions and reviewed project thresholds. This package begins early and does not defer testing until release.
Verification: Demonstrate detection of stamp-sign, derivative, stale-factor, missing-rollback and false-complete-result mutations; adjudicate independent-oracle disagreements rather than auto-update golden values. Separate GPU/native coverage from host wrapper coverage.
Estimate: Ongoing; reserve explicit capacity in every code work package.

Parent: ARCH-VERIFY-001
Parent: ARCH-VERIFY-002
Parent: ARCH-VERIFY-003

**PLAN-018**

## Publish the first qualified CPU release profile

The first release shall claim only supported and evidenced models, analyses, APIs, backends and platforms, with clean packaging, executable documentation, coverage/qualification reports and known limitations.


Phase: M1
Depends-on: PLAN-011, PLAN-012, PLAN-013, PLAN-014, PLAN-015, PLAN-016, PLAN-017
Accountable-role: core engineer and verification lead
Deliverables: frozen M1 profile, license/notices/SBOM, pinned package builds, Rust/C#/F# examples, capability query, baseline performance/memory report and requirement-evidence inventory. Native plugin/GPU/DSL features may remain experimental or absent.
Verification: Install on every claimed platform without developer native-library paths; run offline CPU/API/failure smoke tests, full approved qualification and previous-supported-schema tests if applicable. Block known correctness-critical defects and unsupported evidence claims.
Estimate: Medium to Large after component gates pass; release repair time is not assumed zero.

Parent: ARCH-RELEASE-001

## 6. M2 extensions after the CPU baseline

**PLAN-019**

## Qualify compiled-model packages and native isolation

Compiled models shall be integrated through a pinned OSDI adapter with verified equation/state mappings, versioned packages, thread-safety policy and enforced worker trust controls before untrusted plugin use is supported.


Phase: M2
Depends-on: PLAN-006, PLAN-009, PLAN-013, PLAN-014, PLAN-017
Accountable-role: model engineer and interoperability engineer
Deliverables: ABI mapping specification, package hashes/licenses, model-domain corpus, library lifetime/serialization policy and per-platform sandbox support matrix. A subprocess alone is not a security sandbox.
Verification: Compare IV/charge/derivatives/transient/noise where claimed, force rejected steps, load mismatched descriptors and crash/hang/access-forbidden-path fixture plugins. Refuse untrusted mode when configured OS restrictions cannot be enforced.
Estimate: Large plus model-family-specific qualification.

Parent: ARCH-MOD-003
Parent: ARCH-MOD-004
Parent: ARCH-MOD-005
Parent: ARCH-MOD-006

**PLAN-020**

## Qualify optional GPU linear and batched execution

GPU execution shall use qualified optional adapters with explicit model/assembly/solve placement, measured selection thresholds, asynchronous ownership and safe failed-device recovery.


Phase: M2
Depends-on: PLAN-006, PLAN-008, PLAN-013, PLAN-014, PLAN-016, PLAN-017
Accountable-role: numerical lead
Deliverables: one cuDSS or Ginkgo GPU adapter first, real hardware capability/build matrix, transfer-inclusive cold/warm benchmarks, host/device budgets, attempt/checkpoint policy and no-GPU package tests. CPU OSDI code remains CPU code unless independently GPU-compiled and qualified.
Verification: Compare residuals/waveforms to CPU within approved bounds, measure memory and transfer costs, inject device/OOM/cancellation faults and verify attempts/sample offsets. Do not impose a common adaptive timestep on unrelated cases merely to batch them.
Estimate: Large after hardware and package feasibility are demonstrated; GPU model kernels are a separate later proposal.

Parent: ARCH-GPU-001
Parent: ARCH-GPU-002
Parent: ARCH-GPU-003
Parent: ARCH-GPU-004

**PLAN-021**

## Implement the selected definition DSL and behavioral IR

The selected bounded definition frontend shall compile to the same canonical circuit/experiment plan as .NET/JSON, while behavioral equations use a separately validated differentiable expression representation outside the definition interpreter.


Phase: M2
Depends-on: PLAN-006, PLAN-007, PLAN-013, PLAN-014, PLAN-017
Accountable-role: core engineer and model engineer
Deliverables: pinned Starlark or alternative decision, versioned host modules, unit-aware constructors, authorized imports, budgets/source diagnostics, expression IR/domain/derivative policy and equivalent C#/F# authoring examples.
Verification: Compare RC/subcircuit/sweep/assertion plans across frontends, fuzz imports and expansion limits and independently test nonlinear behavioral derivatives. Prove no definition-interpreter callback occurs in the Newton/device hot loop.
Estimate: Large; stabilize a narrow domain library before inventing new grammar or an editor.

Parent: ARCH-DSL-001
Parent: ARCH-DSL-002
Parent: ARCH-DSL-003
Parent: ARCH-DSL-004
Parent: ARCH-DSL-005

**PLAN-022**

## Deliver reproducible campaigns and StressForge mapping

Campaign execution shall support explicit parameter/corner assignments, stable seeds, bounded resources, incremental outcomes, retained retries and resume from verified completed cases, with actual StressForge compatibility claimed only after repository contract review.


Phase: M2
Depends-on: PLAN-014, PLAN-016, PLAN-017
Accountable-role: interoperability engineer
Deliverables: campaign plan/manifest, C#/F# driver, typed assertion/evidence outcomes, statistical policy for supported distributions and pinned actual StressForge mapping when accessible. Campaign restart is distinct from arbitrary mid-transient resume.
Verification: Compare serial/parallel/reordered assignments, kill/restart the campaign, corrupt an artifact and inspect attempt accounting and pass/fail/indeterminate/infrastructure_error distinctions. Test against the actual external project's pinned integration suite before any compatibility claim.
Estimate: Large; external project scope remains uncertain until its API is reviewed.

Parent: ARCH-RUN-002
Parent: ARCH-DATA-003

**PLAN-023**

## Expand analyses and model families by demand

Noise, sensitivity, electrothermal and additional compact/power-model work shall each receive explicit lower-level requirements, a qualified domain and independent reference evidence before stable capability claims.


Phase: M2
Depends-on: PLAN-018, PLAN-019
Accountable-role: numerical lead and model engineer
Deliverables: prioritized family/analysis subplans, noise correlation/unit contracts and model-domain/license records. Periodic/RF/distributed work remains a separately approved M3 scope.
Verification: Run analysis-specific independent references, derivative/noise/energy checks and API schemas; publish unsupported cases and model-versus-numerical accuracy limits.
Estimate: Research to Large per analysis/model family; no bundled completion date.

Parent: ARCH-ADOPT-001
Parent: ARCH-RELEASE-001

## 7. First execution queue, risks and evidence policy

First queue: PLAN-001 -> PLAN-002; start PLAN-004 once tooling exists; then PLAN-003 -> PLAN-007/008/009 -> PLAN-010 -> PLAN-011. PLAN-005/006 resolve advanced contract risk in bounded experiments, not by growing a second full simulator. PLAN-017 begins with the first contracts and follows every slice. After the M1a managed operating-point example works, prioritize PLAN-012/013/014; complete compatibility and second-provider work before the relevant M1 release claims.

Primary risks and responses:

- Incorrect charge/Jacobian/limiting semantics: independently derive equations and retain mutation/derivative tests before broad device coverage.
- External DAE fit: verify the continuous residual/Jacobian mapping and controller ownership; keep a qualified Rust baseline if adoption is deferred.
- Native packaging/licenses: inspect exact components and platforms early; optional GPU dependencies must not block a free CPU build.
- GPU acceleration disappointment: benchmark transfers and total workflow; prioritize batches and retain strict CPU selection.
- Plugin trust: native crash isolation and OS sandboxing are different; publish per-platform enforcement capability.
- Scope growth: freeze M1 device/dialect whitelist; do not make GUI, all GPU vendors or broad vendor-model parity prerequisites for first value.
- DSL overdesign: prototype host bindings and canonical equivalence first; keep trusted C#/F# orchestration available.
- Coverage theater: retain actual scope/exclusions and numerical oracles; high line coverage is not a convergence or accuracy guarantee.

Evidence convention after implementation starts: keep small reviewed manifests/reports/replay fixtures under evidence/ or tests/, identify source/tool/build/model/provider versions and hashes, and store large generated artifacts outside Git with reproducible acquisition/retention rules. A release evidence index maps requirements, tests, coverage and numerical/benchmark reports to the same source revision. No scheduling automation or cloud resources are created by this plan.

Planning validation is limited to document parsing, ID uniqueness, resolved parent/dependency links, acyclic work-package dependencies and inventory consistency. Prototype test/managed coverage results and Rust coverage limitations are recorded separately under evidence/. No native/GPU qualification or performance claim is made. Tool installation and compiler presence on this machine do not establish supported release platforms.
