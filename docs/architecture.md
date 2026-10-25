<!-- Readable projection; authoritative source: architecture.sdoc -->

## Rust Circuit Simulator - Clean Architecture and Interface Contracts

Version 0.4, proposed architecture, 2026-10-24. A bounded reference prototype now exists; docs/implementation-status.md and prototype-contracts.sdoc define its limited implemented scope. Proposed architecture capabilities are not all implemented.

This is a standalone design for a new Rust circuit simulator. It defines crate responsibilities, numerical contracts, reusable CPU/GPU solver adapters, model plugins, job execution and Rust/.NET interfaces. It is not an implementation, benchmark result, complete device-model specification or compatibility claim.

The architecture reuses numerical libraries and model standards through narrow adapters. It does not embed ngspice, copy its internal object model, adopt a command interpreter as the core API or expose XSPICE data structures. ngspice and Xyce may be separate test-oracle executables. Their historical numerical behavior must be understood through reference cases rather than discarded because its implementation looks old.

Earlier exploratory system requirements outside this repository are historical background, not imported requirements. This document deliberately supersedes their Python-first interface direction: Rust is the implementation/public native library; C# and F# are first-class scripting and SDK clients; PowerShell is the shell/orchestration default. Python is not a required product dependency. StrictDoc itself may require its own Python runtime as a documentation tool.

ARCH-* identifiers are the authoritative requirements in this document. All are Proposed. COMMENT fields give acceptance methods, not completed verification evidence. Local Parent relations connect detailed requirements to architecture goals. Implementation/test file links will be added when those files exist; no fictitious coverage or implementation links are claimed.

M0 = numerical and interoperability experiments; M1 = qualified CPU DC/AC/transient engine with .NET access; M2 = qualified compiled-model and optional GPU backends; M3 = additional analyses justified by actual workloads. GPU backend interfaces are designed at M0/M1; universal GPU execution and broad vendor-model parity are not M1 promises.


## 1. Architecture goals and fixed boundaries

**ARCH-GOAL-001**

## Independent core

The simulator shall use a Rust-owned numerical and circuit core with no dependency on a legacy simulator runtime, graphical interface or command interpreter.


Verification: Build and execute the headless native engine without legacy simulator libraries, GUI libraries or a shell interpreter; inspect its dependency graph.


**ARCH-GOAL-002**

## Replaceable numerical backends

The simulator shall expose circuit-independent numerical contracts through which qualified CPU and GPU solver libraries can be exchanged without modifying device equations or public request schemas.


Verification: Run the same qualified circuit and matrix cases through independently implemented backend adapters and compare outcomes and residuals.


**ARCH-GOAL-003**

## Independent model ecosystem

The simulator shall support built-in Rust models and versioned compiled-model adapters without allowing model-specific layouts, syntax or native library types into the numerical core.


Verification: Load a test model through the plugin adapter and inspect core interfaces for forbidden model/plugin types.


**ARCH-GOAL-004**

## Automation-first interfaces

The simulator shall expose typed requests, job states, signals, measurements, diagnostics and results to Rust, C# and F# clients without requiring console-output parsing.


Verification: Execute an end-to-end RC case from each client and recover typed results and failure reasons.


**ARCH-GOAL-005**

## Qualified behavior and honest outcomes

The simulator shall distinguish numerical correctness, model validity, execution completeness and assertion outcomes and shall associate release claims with reproducible verification evidence.


Verification: Inspect evidence for analytic, nonconvergent, cancelled, out-of-domain and completed-but-failing cases.


The central data flow is:

Text dialect, structured request or bounded simulation-definition DSL -> parsed input -> elaborated immutable circuit -> compiled execution plan -> analysis session -> model evaluation and matrix assembly -> numerical backend -> accepted samples -> measurements and result publication.

The command line, C ABI, worker protocol and .NET SDK are separate front doors to the same application service. Only the composition root selects concrete model registries, solver adapters, schedulers and sinks. Nothing below that root discovers arbitrary global services.

Do not create a universal mutable Circuit object that contains parser state, GUI settings, model pointers, matrices, solver handles, output buffers and user callbacks. These have different lifetimes and belong to different layers.


## 2. Crates, dependency direction and composition

Proposed workspace layout; boundaries may initially be Rust modules where separate crates provide no immediate benefit. The dependency direction is mandatory even when packaging is consolidated.

- sim-types: typed IDs, units/quantities, source spans, diagnostics and small value types; no runtime or GPU dependencies.
- sim-ir: parsed-independent circuit definitions, hierarchy, canonical expressions and parameter assignments; depends on sim-types.
- sim-netlist: lexing/parsing and explicit dialect adapters; produces sim-ir input objects; never executes arbitrary shell commands.
- sim-dsl: optional bounded Starlark-style definition frontend and host bindings; produces the same canonical input/plan contracts as netlists and structured builders. It never owns the numerical loop.
- sim-elaborate: resolves hierarchy, parameters, models and topology into an immutable ElaboratedCircuit.
- sim-model-api: Rust model descriptors, bound-instance contracts, evaluation buffers and trial/accepted state semantics; depends on sim-types, not analyses or solver adapters.
- sim-models: built-in devices implementing sim-model-api.
- sim-plugin-osdi: native OSDI adapter, ABI checking and symbol translation; depends on sim-model-api and a narrow native binding module.
- sim-linalg-api: CSR/CSC layouts, matrix identity, backend capabilities, linear sessions and errors; independent of circuit/device types.
- sim-backend-cpu: selected sparse CPU adapter plus a tiny dense reference backend used only for tests and small diagnostic problems.
- sim-backend-cudss and sim-backend-ginkgo: optional separate adapters; vendor headers and C++ are confined here.
- sim-numerics: scaling, Newton control, continuation, integration coefficients, timestep selection and residual checks; uses abstract equation/model and linear-algebra contracts.
- sim-compile: maps elaborated instances to unknowns, state layouts, sparsity slots and analysis plans; no concrete numerical backend dependency.
- sim-analysis: operating point, DC sweep, AC and transient drivers; depends on compiled plans, model API and sim-numerics.
- sim-results: signal descriptors, batch sink, measurements, manifests and Arrow encoding. Analysis consumes its minimal sink interface; storage never calls analysis back.
- sim-contracts: versioned serializable request/result DTOs and validation; no native handles.
- sim-runtime: job lifecycle, quotas, cancellation and bounded queues; treats an analysis run as an application operation.
- sim-service: the composition root that connects contracts, elaboration, compilation, analyses and concrete adapters.
- sim-capi: thin C ABI over sim-service with opaque handles and explicit buffer ownership.
- sim-worker and sim-cli: executable hosts over sim-service; worker is the optional process isolation boundary.
- dotnet/Simulator: managed C# SDK using source-generated interop where applicable, SafeHandle and Arrow .NET; callable from F#.
- scripts/: C# file-based applications or F# Interactive scripts for substantive automation; PowerShell for shell/file orchestration.
- docs/: authoritative StrictDoc architecture/requirements, decisions, schemas and qualification policy.
- tests/: matrix, device, analytic-circuit, API, plugin, malformed-input, fuzz, property, mutation and benchmark corpora.

Dependency spine: contracts -> service -> analysis -> numerics -> linalg-api -> types. Concrete backend crates depend on linalg-api; linalg-api never depends on adapters. The service owns adapter construction. Model API and compiled plans are separate dependencies of analysis. No reverse edge from a backend/model to sim-service, parser, worker or SDK is allowed.

Native external libraries are dependencies of adapter crates, not source-code templates for the entire application. This keeps future backend replacement local and avoids making every device a wrapper around a particular solver library.


**ARCH-DEP-001**

## Enforced dependency direction

Workspace checks shall reject dependency cycles and dependencies from core model or numerical API crates on parsers, concrete backend crates, GUI code, worker transports or SDKs.


Verification: Inspect Cargo metadata in CI and run a deliberately forbidden dependency fixture against the policy check.

Parent: ARCH-GOAL-001

**ARCH-DEP-002**

## Explicit construction and optional dependencies

The service composition root shall construct per-engine registries and backend factories explicitly. A CPU-only build shall not require GPU runtime initialization, driver installation or vendor GPU redistributables.


Verification: Install and run the CPU package on a no-GPU machine and execute concurrent engines with different registries.

Parent: ARCH-GOAL-001
Parent: ARCH-GOAL-002

## 3. Circuit representation and execution ownership

ParsedDeck holds syntax and source spans. ElaboratedCircuit holds resolved topology, model references and effective parameters. ExecutionPlan holds immutable unknown ordering, sparse pattern, stamp maps, signal catalog and state layout. Arc may share these immutable objects across compatible runs.

RunSession exclusively owns time, solution vectors, trial/accepted device state, integration history, random streams, event queue, solver sessions and result writer. ModelPackage owns immutable descriptors and native library lifetime. BackendSession owns native handles, device context, workspace and pending operations; it is not implicitly Send or Sync.

Bindings from device local terminals to global unknowns and sparse slots are computed once. A model evaluates into instance-local slots; deterministic assembly combines those contributions. A model never receives a raw global matrix or arbitrary write access to another instance's state.

Structural identity includes topology, unknown/state layout, selected model versions, structural parameter choices and declared sparsity. Numeric identity includes effective scalar parameters and current evaluation coefficients. A parameter can be structural even if its syntax looks numeric: geometry or a model flag can introduce internal states. The descriptor classifies it; the caller does not guess.


**ARCH-STATE-001**

## Session-local mutable state

All mutable simulation and solver state shall belong to a run or explicitly serialized backend session; no mutable process-global circuit state shall be used.


Verification: Run independent circuits concurrently under race detection where available and compare with isolated serial results.

Parent: ARCH-GOAL-001

**ARCH-STATE-002**

## Correct reuse and invalidation

The compiler shall compute structural and numeric invalidation keys and invalidate all affected model bindings, sparsity, factors, operating points and measurements after a relevant change.


Verification: Mutate each parameter class and compare reused execution against a fresh compile, including a model parameter that changes internal state count.

Parent: ARCH-GOAL-005

**ARCH-STATE-003**

## Typed indices and bounded loading

The core shall distinguish node, instance, unknown, state, signal and sparse-slot identifiers, validate serialized indices before allocation or access, and enforce declared include/depth/size limits during loading.


Verification: Fuzz identifiers, sparse indices and deep hierarchy; assert classified rejection without panics, root escapes or unbounded allocation.

Parent: ARCH-GOAL-001
Parent: ARCH-GOAL-005

## 4. Equation and analysis contracts

Use a modified-nodal differential-algebraic formulation:

F(x, t, p) + dQ(x, t, p)/dt = 0.

x contains node potentials, branch currents and model-internal unknowns. F contains static equations and signed source contributions; Q contains stored charge/flux contributions in the corresponding equations. Algebraic rows may have zero Q. Units are tracked by unknown/equation descriptors rather than assuming every row is a voltage equation.

Each model returns F, Q, dF/dx and dQ/dx contributions for its declared local stencil, plus optional observables, events and noise sources. For integration coefficient alpha, the transient Newton Jacobian is dF/dx + alpha*dQ/dx with history terms in the residual. DC uses F=0. AC linearizes at the recorded operating point: (G + j*omega*C) * dx = b, where G=dF/dx and C=dQ/dx. The published device/sign conventions must agree with this definition.

The analysis driver chooses what problem to solve. Newton/continuation chooses iterations. Integration chooses coefficients, predictors and local error tests. The linear backend solves Ax=b. None of these layers silently changes another layer's convergence policy.

Baseline drivers: operating point; nested DC sweep; complex AC; transient. Future periodic/RF analyses reuse equation evaluation rather than requiring separate per-analysis device implementations. Noise is a capability of a model descriptor with an explicit noise-source/correlation contract, not something inferred from DC derivatives.

Limiting may be needed for robust iteration. If limited evaluations alter the linearized residual, the model contract includes the equivalent correction consistent with its Jacobian; final convergence is checked against the physical, unmodified equations. Detailed limiting/correction mathematics are a required M0 design artifact, not delegated to individual adapters to invent.


**ARCH-EQ-001**

## Analysis-independent model equations

Models shall expose residual, charge and derivative contributions independent of DC/AC/transient driver implementations, using one documented sign, unit and source-excitation convention.


Verification: Derive RC/RLC DC, AC and transient equations from the same model evaluations and compare analytic results.

Parent: ARCH-GOAL-003

**ARCH-EQ-002**

## Central convergence policy

Newton control shall apply both scaled physical residual and update checks, bounded continuation attempts, finite-value checks and explicit regularization reporting; a linear backend success alone shall never imply circuit convergence.


Verification: Test tiny-update/large-residual, singular, overflow, regularized and bistable cases and inspect convergence histories.

Parent: ARCH-GOAL-005

**ARCH-EQ-003**

## Explicit initialization and tolerances

Analyses shall expose operating-point initialization, initial guesses, forced initial conditions and bypass modes separately and shall record effective dimensional absolute tolerances and relative tolerances.


Verification: Compare precharged capacitor/inductor cases in every initialization mode and inspect effective settings in results.

Parent: ARCH-GOAL-005

**ARCH-EQ-004**

## Transient acceptance and rollback

A transient driver shall commit a step only after nonlinear convergence and its integration error test pass, and shall restore all trial state, random state and events after rejection.


Verification: Force rejections and compare against direct execution of the same accepted step sequence, including history-dependent devices.

Parent: ARCH-GOAL-005

**ARCH-EQ-005**

## Discontinuity and sample semantics

The transient engine shall schedule known source breakpoints, process model events with documented simultaneous-event ordering and distinguish accepted solver samples from resampled outputs with explicit left/right event semantics.


Verification: Exercise PWL corners, pulse edges and simultaneous switch crossings and compare recorded and interpolated outputs.

Parent: ARCH-GOAL-005

## 5. Reusable linear solver API

Choose small object-safe Rust interfaces over a generic framework that exposes every vendor method. The following is the direct-solver lifecycle sketch, not compilable or stabilized Rust and not a mandatory interface for iterative solvers:

BackendFactory.capabilities() -> BackendCapabilities
BackendFactory.create(DeviceChoice, ResourceBudget) -> LinearSession
LinearSession.analyze(Pattern, MatrixProperties, SolveOptions) -> PatternHandle
LinearSession.factor(PatternHandle, MatrixValues) -> FactorHandle
LinearSession.refactor(FactorHandle, NewValues) -> FactorHandle or ReanalyzeRequired
LinearSession.solve(FactorHandle, RHS, SolveOperation) -> Solution and LinearReport
LinearSession.synchronize(OperationToken) -> Completion

Handles are backend/session-scoped, nonserializable and lifetime checked. A backend that cannot safely refactor returns an explicit capability/result and performs a fresh factorization. Pattern reuse is not numerical-factor reuse. Changing matrix values usually invalidates numerical factors even when the sparse pattern is identical. Pivot failures can require a fresh analysis.

Pattern is a validated canonical CSR or CSC structure with dimensions, index base/width, sorting and duplicate policy. Device stamp maps reference this canonical pattern. Each adapter owns any CSR/CSC conversion, index-width conversion, permutation, scaling and host/device transfers. It reports the costs. Baseline values are real64 and complex64, meaning binary64 real and binary64 imaginary components; no reduced precision is implicit.

MatrixProperties must distinguish general unsymmetric, symmetric indefinite, Hermitian and positive-definite claims. General circuit MNA is not presumed SPD, so CG/Cholesky are not default substitutes for general sparse LU. Matrix dimension, structural singularity, numerical singularity and ill conditioning are different outcomes.

Iterative backends implement a separate IterativeSession contract with operator/preconditioner setup and reuse, true residual checks, iteration limits and a NotConverged outcome. They do not implement fake analyze/factor operations merely to resemble direct solvers. DirectSession and IterativeSession share value/layout descriptors, error classes, placement and result reporting. A direct fallback is a declared policy with a recorded attempt, not a hidden conversion of failure to success. Multiple RHS and transpose/conjugate-transpose solves are capabilities useful for AC and sensitivities.

LinearReport includes backend/version/device, timings, allocations, permutation/scaling choices, pivot/refinement diagnostics where exposed, residual norms and precision. The core independently computes a physical residual when needed rather than trusting an opaque vendor success status.


**ARCH-LIN-001**

## Circuit-independent solver contract

Linear backend contracts shall accept validated matrices, right-hand sides, numerical options and resource limits without device, netlist, analysis-driver or public API objects.


Verification: Run the backend conformance suite as a standalone matrix application with no circuit crates linked.

Parent: ARCH-GOAL-002

**ARCH-LIN-002**

## Capabilities and matrix classes

Each backend shall declare supported matrix classes, real/complex precision, index limits, solve operations, batching, threading, determinism and asynchronous behavior and reject unsupported combinations before execution.


Verification: Run nonsymmetric, indefinite, complex, oversized-index and unsupported-operation fixtures through every adapter.

Parent: ARCH-GOAL-002

**ARCH-LIN-003**

## Cache validity and handle ownership

Solver handles shall enforce session ownership and explicit invalidation after structural/value changes, failed updates or device reset; cached symbolic work shall never authorize stale numeric-factor reuse.


Verification: Mutate values and structure, cross-use handles and inject refactor/device failures; compare to independent fresh solves.

Parent: ARCH-GOAL-002
Parent: ARCH-GOAL-005

**ARCH-LIN-004**

## Numerical backend verification

Each backend shall pass a shared matrix suite covering residuals, transpose/complex behavior, singularity and ill conditioning before use in qualified circuit analyses; reduced or mixed precision shall require explicit opt-in and separate qualification.


Verification: Compare known solutions and normalized backward error against independent references and retain per-backend evidence.

Parent: ARCH-GOAL-005

## 6. Backend candidates and adoption decisions

These are evaluated candidates, not selected/pinned production dependencies. Capability and license claims must be checked against the exact adopted release and transitive dependencies.

CPU sparse direct: evaluate SuiteSparse KLU first because it is aimed at sparse unsymmetric circuit-simulation systems. Keep its native interface in sim-backend-cpu. SuiteSparse is a collection with component-specific licenses; do not assume the whole collection has a single permissive license. Evaluate a maintained Rust-native sparse direct implementation as an alternative if its numerical coverage, complex support and maintenance pass the same corpus. A tiny dense test solver is not a production sparse replacement.

GPU sparse direct: evaluate NVIDIA cuDSS for general real/complex sparse factor/solve, pattern reuse and batching. It has vendor-specific runtime and redistribution conditions; optional proprietary backend availability must not compromise a free CPU product. Verify platforms, GPU precision and index restrictions against the pinned release. Do not begin with deprecated sparse interfaces when the vendor directs new adoption to cuDSS.

Portable CPU/GPU sparse iterative: evaluate Ginkgo with an owned narrow C ABI shim over its C++ API. Ginkgo supports CUDA, HIP, SYCL and OpenMP execution. Portability does not imply a universal direct-LU replacement: solver/preconditioner choice must fit general nonsymmetric circuit matrices and pass convergence tests.

Alternative integration experiment: SUNDIALS offers GPU-aware vector/matrix/solver components and time/nonlinear integration families. It could become an optional alternative time-integrator adapter after an index/initialization/error-control experiment. It must not become a second owner of the same timestep history. M1 retains one Rust-owned integration controller; adopting SUNDIALS later requires an explicit decision that assigns controller ownership and event/rollback semantics.

.NET custom kernels: ILGPU is an option for separate C#/F# analysis or experimental kernels, not the default dependency of the Rust core and not a complete sparse solver. Requiring a managed runtime in the core merely to access a GPU would reverse the intended SDK boundary.

Decision gates: numerical qualification -> license/distribution approval -> supported-platform packaging -> fault/ownership checks -> representative end-to-end benchmark -> production stability flag. Do not promise that a single adapter will work with every vendor GPU on Windows, Linux and macOS.

Solver portfolio expansion: compatibility is a first-class architecture goal rather than a promise to ship every candidate in the first release. Use a SolverRegistry of descriptors/factories with distinct role interfaces:

- DirectLinearSolver: sparse factor/refactor/solve; candidates KLU, another qualified SuiteSparse component and cuDSS.
- IterativeLinearSolver and Preconditioner: operator application, setup/update and convergence reporting; candidates Ginkgo and PETSc KSP/PC.
- NonlinearSolver: residual/Jacobian callbacks, globalization and convergence; candidates Rust-owned control, SUNDIALS KINSOL and PETSc SNES.
- DaeIntegrator: residual R(t,x,xdot), consistent initialization, Jacobian callback, event and accepted-step handling; candidates Rust-owned integration, SUNDIALS IDA/IDAS and PETSc TS methods that actually support the circuit's DAE class.
- Future OptimizationSolver: objective/constraint evaluation outside individual circuit solves; PETSc TAO is a candidate if optimization requirements justify it. Eigenvalue and frequency-domain specialized solvers get their own role contracts when required rather than being forced into Ax=b.

PETSc provides several solver families in one framework, but adopting one optional adapter does not require making PETSc the core circuit representation. Its process initialization, MPI/communicator lifetime, scalar/index configuration and threading rules belong to the adapter/worker policy. Incompatible runtime-global configurations may require separate workers. Its availability and GPU paths depend on the exact build; they are not universal capabilities.

The DAE bridge defines R(t,x,xdot)=F(x,t)+Q_x(x,t)*xdot+Q_t(x,t). For nonlinear charge models, a library callback requesting the partial derivative of R at fixed xdot generally needs derivatives of Q_x*xdot, not merely G+cj*C. An alternative charge-augmented formulation can avoid this mismatch at the cost of extra unknowns. The M0 adapter experiment must choose and verify a mathematically consistent formulation, including explicit time dependence, algebraic rows, constraints and initialization. A callback shape that looks compatible is not enough. IDA is not interchangeable with an ODE-only integrator, and not every PETSc TS method supports every DAE/index structure.

Solver composition is capability-based, not a Cartesian product. A DAE integrator may own its nonlinear solve and accept only specific linear backends; a nonlinear package may require its own vector type, preconditioner or GPU memory layout. A factory describes supported compositions. If an external package owns timestep acceptance/history, the Rust analysis driver becomes its supervisory adapter and must not simultaneously advance a separate integrator. If it owns Newton iteration, the circuit layer supplies physical residuals, scaling/limits and acceptance validation without running a second competing Newton controller.

The selection request specifies roles, backend IDs/versions, placement, accuracy policy, resource limits, fallback policy and validated namespaced options. Vendor options are typed or schema-validated, logged after secret redaction where needed, and recorded as effective settings. Unknown options fail instead of being ignored. Backend replacement does not change circuit semantics, but can change internal step sequences, convergence paths and whether a difficult problem converges; this is measured and reported rather than advertised as identical behavior.

Keep a portable common surface and optional capability extensions. Do not reduce every backend to the least capable library, and do not leak vendor handles into public application code. Start with statically linked Rust adapters or reviewed C/C++ shims. A future external solver plugin uses a separately versioned C ABI/worker contract, never Rust's unstable dynamic ABI. Backend identity, build features and transitive numerical libraries are part of provenance and cache validity.

Reuse substantially reduces implementation of sparse factorization, preconditioning, nonlinear globalization, integration/error estimation and accelerator execution. It does not supply circuit equations, charge-conserving models, accurate derivatives, dialect interpretation, device limiting, consistent initial states, event semantics, trustworthy results or qualification. Those remain simulator responsibilities. Code volume falls; integration correctness still requires specialist numerical work. No schedule reduction is claimed until the M0 experiments measure actual fit.


**ARCH-PORT-001**

## Multiple solver roles

The solver registry shall support separate direct-linear, iterative-linear/preconditioner, nonlinear and DAE role contracts, with optional extensions and no requirement that every provider implement every role.


Verification: Register providers supporting different roles, enumerate their capabilities and reject requests for unavailable roles before solving.

Parent: ARCH-GOAL-002

**ARCH-PORT-002**

## Qualified provider variety

Before claiming interchangeable linear backends, the project shall qualify at least two independent providers on their shared supported circuit/matrix subset and retain provider-specific extensions and limitations.


Verification: Execute the shared real/complex nonsymmetric conformance corpus and publish residual, outcome, accuracy and unsupported-feature comparisons with exact provider versions.

Parent: ARCH-GOAL-002
Parent: ARCH-GOAL-005

**ARCH-PORT-003**

## Validated solver compositions

The planner shall validate provider combinations against scalar/index types, memory placement, callback requirements, matrix/DAE class, initialization, thread/runtime constraints and supported inner-solver hooks before creating a run.


Verification: Test incompatible GPU layouts, ODE-only versus DAE requests, unavailable preconditioners and incompatible runtime builds; verify actionable rejection.

Parent: ARCH-GOAL-002

**ARCH-PORT-004**

## One owner per numerical controller

An execution plan shall assign exactly one owner to nonlinear iteration and exactly one owner to timestep/error-control history, and shall define compatible circuit limiting, event, state rollback and physical acceptance hooks for external controllers.


Verification: Inspect ownership plans and run rejected-step, event, limiting and cancellation fixtures through a reference and external controller without duplicated commits or competing advancement.

Parent: ARCH-GOAL-002
Parent: ARCH-GOAL-005

**ARCH-PORT-005**

## Correct external DAE formulation

An external DAE adapter shall document and independently verify residual, Jacobian, charge, explicit-time, algebraic-constraint and initialization mappings for its selected formulation; it shall not assume the transient discretized Jacobian is identical to the library's continuous-residual Jacobian callback.


Verification: Compare callback derivatives to independent differentiation on voltage-dependent charge models and explicit-time sources; check algebraic constraints and accepted transient results against reference cases.

Parent: ARCH-GOAL-002
Parent: ARCH-GOAL-005

**ARCH-PORT-006**

## Validated effective solver configuration

Solver configuration shall use versioned provider identities, capability-negotiated roles and validated namespaced options, reject unknown settings and record effective composition, controller ownership and build/runtime dependencies in result provenance.


Verification: Submit misspelled options, conflicting controllers and changed provider builds; inspect errors, effective manifests and cache invalidation.

Parent: ARCH-GOAL-002
Parent: ARCH-GOAL-004

**ARCH-ADOPT-001**

## Backend adoption evidence

Every production numerical dependency shall have a recorded version, component/transitive license review, platform matrix, numerical qualification and reproducible benchmark report; no backend shall be selected solely from advertised speed or API similarity.


Verification: Review the adoption decision and ensure all required evidence exists before enabling the backend as stable.

Parent: ARCH-GOAL-002
Parent: ARCH-GOAL-005

## 7. GPU execution, batching and memory

GPU support has two separate axes. Solver placement determines where a matrix is factored and solved. Model placement determines where F/Q/derivatives are evaluated and assembled. CPU model evaluation plus a GPU solve is a supported hybrid plan, with transfers measured at every Newton step. An OSDI CPU shared library does not become GPU executable by selecting a GPU solver.

M1 compiles the solver abstraction and measures CPU workloads. M2 first qualifies a hybrid sparse-direct GPU adapter and independent-case batching. GPU model kernels are a later capability for qualified built-ins or separately compiled model packages. A plugin descriptor declares CPU-only, GPU-capable or both, supported precision and device backends; the planner rejects impossible all-GPU requests.

The first GPU win may be many independent sweep/Monte Carlo cases rather than one small sparse circuit. A campaign scheduler controls CPU threads, GPU memory and device queues. It groups compatible cases for uniform batching where useful and preserves distinct case IDs, adaptive histories, failures and seeds. No common adaptive timestep is imposed on unrelated circuits solely to simplify batching without a documented algorithm and accuracy proof.

HostBuffer and DeviceBuffer are separate ownership concepts. Copies and completion tokens make transfer/synchronization explicit. Native device pointers never escape to ordinary SDK consumers. A buffer or plugin library cannot be dropped while an operation that uses it remains pending. Drop-based cleanup is supplemented by explicit close/error reporting for operations whose cleanup can fail.

Automatic selection uses measured workload thresholds, memory estimates and cached device capability, not merely GPU presence. Include assembly, transfer, factorization, solve, synchronization, result copies, startup/JIT and cold/warm effects. Minimize transfers only after correctness is established.

A worker owns its device context or an explicitly serialized shared session. A fatal device error poisons that session and all dependent handles. Recovery starts from a fully accepted checkpoint in a new valid session; if full replay state is unavailable, mark the attempt failed. A CPU retry is a distinct recorded attempt. A vendor kernel may be noninterruptible: hard deadlines require worker termination and explicit device-cleanup policy rather than a false prompt-cancellation guarantee.


**ARCH-GPU-001**

## Explicit placement plan

Execution plans shall separately declare model-evaluation, assembly and linear-solve placement, reject unsupported combinations and report selected devices, transfers and fallback decisions.


Verification: Test CPU-only plugins with a GPU linear solver, unsupported all-GPU requests and a machine without a GPU.

Parent: ARCH-GOAL-002
Parent: ARCH-GOAL-003

**ARCH-GPU-002**

## Measured GPU selection

Automatic backend selection shall require validated memory estimates and end-to-end workload thresholds; explicit CPU/GPU selection shall remain available with actionable errors rather than silent substitution when strict selection is requested.


Verification: Benchmark single circuits and batches across sizes, include cold/warm and transfer costs, and test strict selection under insufficient memory.

Parent: ARCH-GOAL-002

**ARCH-GPU-003**

## Asynchronous lifetime safety

GPU buffers, native handles and plugin packages shall remain owned until dependent operations complete, with bounded queue/memory use and explicit synchronization before host reads or cleanup.


Verification: Inject delayed completion, cancellation and allocation errors and inspect use-after-free, leak and stale-read behavior with applicable GPU diagnostics.

Parent: ARCH-GOAL-002
Parent: ARCH-GOAL-005

**ARCH-GPU-004**

## Safe backend recovery

Device failure shall invalidate affected sessions and factors; a fallback shall restart from a complete accepted checkpoint or fresh run, retain separate attempt provenance and never duplicate already published samples as newly accepted data.


Verification: Inject failure between factorization, solve and output commit; verify attempt IDs, data offsets and completeness after CPU retry.

Parent: ARCH-GOAL-005

## 8. Model API, compiled plugins and trust

The internal ModelDefinition/BoundModel API is ordinary versioned-together Rust; no stable Rust dynamic ABI is promised. External native plugins use a versioned C ABI, initially through the OSDI adapter and later a project-specific ABI only if a real gap justifies maintaining one.

Model definition phase: enumerate terminals, parameters/defaults/units/domains, internal unknowns and states, sparse dependencies, observables, events, noise capabilities, limiting behavior, supported precision and thread-safety/reentrancy. Binding phase: resolve parameters, allocate instance layout and precompute topology/stamp maps. Evaluation phase: consume immutable context plus owned trial-state region and write bounded instance-local output buffers. Commit/rollback phase: promote or discard trial state.

Evaluation context contains time, current solution values, effective temperature, parameter view and explicit previous/accepted state. It is not a service locator. Models cannot call a solver, write result files, fetch network data or mutate another instance. Native code cannot be prevented from violating these rules in-process; trust boundaries are enforced by worker isolation and operating-system policy, not the Rust trait.

OSDI adapter translates descriptors, setup, evaluation flags, limits, Jacobian/charge layouts, noise and state semantics into the internal model API. Exact mapping requires qualification against a pinned ABI/compiler and reference model. Unsupported capability returns Unsupported, not zeros. Use batched calls or prebound per-model loops rather than virtual dispatch/allocation for each individual matrix entry.

An immutable model package includes model/compiler/ABI versions, hashes, dependencies, licensing, qualified domain and test provenance. The registry resolves one exact model version per circuit; no invisible download or latest-version substitution occurs at runtime. Host packages differ by OS/architecture and are not inherently portable GPU code.

Default managed-client execution uses a worker process. An untrusted native plugin is loaded inside that worker, not inside the client or supervisor. Operating-system sandboxing must separately restrict files, network, child processes and resources. If a supported platform cannot enforce the configured sandbox, reject untrusted execution or require explicit trusted mode. A subprocess alone is crash isolation, not a security sandbox.

For performance, isolate the entire run containing native models rather than an IPC round-trip per device per Newton iteration. Trusted in-process Rust/C clients can opt in to direct loading. A plugin access violation, abort or corrupted heap cannot be made safe merely by catch_unwind.


**ARCH-MOD-001**

## Bounded declarative model interface

Model interfaces shall declare topology, state, sparsity and capabilities before solving and restrict evaluation outputs to validated local residual, charge, derivative, event and observable buffers.


Verification: Check resistor/diode/transistor descriptors, inconsistent sparsity and out-of-range output fixtures and compare derivatives independently.

Parent: ARCH-GOAL-003

**ARCH-MOD-002**

## Trial state and side effects

Model evaluation shall not perform externally visible side effects and shall support complete trial-state rollback, deterministic seeded random streams and documented limiting corrections where used.


Verification: Force step rejection and repeated evaluations and verify identical accepted state, output and random progression.

Parent: ARCH-GOAL-003
Parent: ARCH-GOAL-005

**ARCH-MOD-003**

## Stable native boundary

Native plugin adapters shall validate ABI/version, sizes, alignment, ownership and capabilities, retain library lifetime through all model instances and expose no vendor-native structures outside the adapter.


Verification: Reject incompatible ABI and descriptor fixtures; destroy engine/instances in varied orders under native memory diagnostics.

Parent: ARCH-GOAL-003

**ARCH-MOD-004**

## Thread-safety declarations

The scheduler shall obey model reentrancy and thread-safety declarations and shall serialize or process-isolate nonreentrant model implementations instead of marking them unconditionally Send/Sync.


Verification: Run a deliberately nonreentrant fixture concurrently and verify correct serialization/isolation and numerical agreement.

Parent: ARCH-GOAL-003

**ARCH-MOD-005**

## Native execution isolation

Untrusted native plugins shall execute only under a supported enforced worker sandbox; trusted in-process loading shall require explicit caller selection and shall document its crash/memory-corruption risk.


Verification: Crash, hang and attempt forbidden filesystem/network access from a fixture plugin; confirm supervisor survival and policy enforcement or explicit refusal.

Parent: ARCH-GOAL-003
Parent: ARCH-GOAL-005

**ARCH-MOD-006**

## Pinned package provenance

Model resolution shall use explicit content/version identities and record compiler, ABI, license and qualification metadata; unsupported or encrypted models shall fail explicitly.


Verification: Alter a package, remove capability metadata and submit an encrypted model; inspect errors and recorded hashes.

Parent: ARCH-GOAL-003

## 9. Job runtime, safety and reproducibility

Job states: queued -> compiling -> running -> succeeded or failed or resource_exhausted. Cancellation moves queued/compiling/running to cancelling -> cancelled; cancellation racing completion resolves to exactly one terminal state through an atomic transition. Compile failure can terminate without entering running. Limits during any active phase produce a classified terminal result. Progress percentages are optional; simulated time and phase/counters are factual alternatives.

There are distinct compile, run, analysis and attempt IDs. An attempt records a specific backend/settings execution. Backend failure and numerical-setting retries retain history. Campaign seeds derive from stable case and instance identities; schedule order must not change the intended sample assignment. Cross-backend numeric results are tolerance-compared, not assumed bit-identical.

Cancellation is cooperative at safe points; parser and iterative loops poll it. Blocking native calls may finish before acknowledging cancellation. Hard limits use a worker supervisor with bounded grace periods and separate killed-worker outcomes. Cleanup cannot guarantee instant recovery of a hung external driver.

Memory and output budgets include model state, sparse factors, device workspace, queue capacity and recorded data. Bound includes, paths, hierarchy, expression size, worker count and nested backend threads. No network access or shell commands are part of ordinary circuit evaluation.

Checkpoints, when supported, include accepted solution, time, integration order/history, model state, event queue, RNG state and exact engine/model/backend compatibility identities. Persisting only x and t is insufficient. Never serialize native handles or raw pointers. M1 may support campaign restart without promising arbitrary mid-transient process resume.


**ARCH-RUN-001**

## Classified lifecycle and limits

Every job shall end in exactly one terminal state with classified diagnostics, applicable resource limits and complete/partial/unavailable result status, including cancellation and native worker failure.


Verification: Exercise state races, limits in each phase, late cancellation, crashed workers and output failures.

Parent: ARCH-GOAL-004
Parent: ARCH-GOAL-005

**ARCH-RUN-002**

## Reproducibility and complete checkpoints

Results shall record effective settings, seeds and version/content identities; any supported resumed run shall restore complete accepted algorithm/model state and validate compatibility before execution.


Verification: Compare schedule-reordered campaigns and interrupted/resumed fixtures under the declared reproducibility envelope; reject incompatible checkpoints.

Parent: ARCH-GOAL-005

## 10. Rust, C and .NET API contracts

Rust facade semantic sketch:

EngineBuilder::new().models(registry).backends(backends).limits(policy).build()
Engine::capabilities() -> Capabilities
Engine::compile(CircuitInput, CompileOptions) -> CompiledCircuit or Diagnostics
Engine::submit(CompiledCircuit, RunRequest) -> JobHandle
JobHandle::events() -> bounded sequenced event stream
JobHandle::cancel() -> acknowledgement of request, not proof of immediate termination
JobHandle::wait() -> terminal ResultManifest
ResultReader::signals()/measurement(id)/batches(selection)

Illustrative names only; no code is claimed to compile. The synchronous core does not require a specific asynchronous Rust runtime. The host may wrap it in asynchronous scheduling. Public error types are structured and serializable; backend-specific strings are supplemental diagnostics.

C ABI baseline: versioned sim_* functions, fixed-width status codes, opaque engine/job/result handles, caller-provided or library-owned byte buffers with matching release function. Exported calls return errors; no Rust panic or C++ exception crosses the ABI. Abort/access violations require worker isolation. Handles are process-local, validated against stale use and never serialized. Callback buffers are borrowed only for the call unless copied or transferred through an explicit ownership function.

.NET namespace proposal: Simulator.Client, CircuitInput, SimulationRequest, SimulationJob, SimulationResult, SignalDescriptor and MeasurementResult. A C# class library exposes Task-returning job operations, CancellationToken request propagation, IAsyncEnumerable event/batch streams, SafeHandle for in-process native handles and IDisposable/IAsyncDisposable for resource ownership. F# consumes the same package without a separate engine implementation. Runtime assets are packaged by supported RID and tested without developer SDK/native search-path assumptions.

Two explicit transports implement the same client contract: LocalWorker (default isolation) and InProcess (trusted, C ABI). The worker uses version-negotiated length-framed JSON control messages and Arrow IPC waveform streams over authenticated/permission-restricted local IPC. Resource and frame limits are validated before allocation. Remote transport is optional later; it must add authentication, encrypted transport, authorization and immutable idempotent submissions rather than expose the local pipe protocol to the network unchanged.

Request envelope contains schema_version, case_id, circuit source/IR, analysis plans, parameter assignments, model identities, numerical options, resource limits, recording policy, seed and execution placement policy. Explicit strict_gpu differs from auto. Compile-time signal selectors resolve to signal IDs that appear in the manifest. Never accept an arbitrary executable command as a simulation request.

StressForge is an external orchestrator adapter: map its real case IDs/parameters/seeds/assertions to this contract after repository review. Execution success is separate from assertion pass. Nonconvergence or missing evidence normally yields indeterminate, not pass. No existing StressForge API compatibility is claimed.


**ARCH-API-001**

## Shared typed application service

Rust, CLI, C ABI and .NET interfaces shall call the same validated application service and preserve consistent capabilities, effective settings and structured outcome/error semantics.


Verification: Run golden request/result cases through each interface, including unsupported features, convergence failure and cancellation.

Parent: ARCH-GOAL-004

**ARCH-API-002**

## First-class C# and F# access

The .NET SDK shall provide typed asynchronous control, bounded result/event streaming, cancellation and deterministic disposal, with maintained C# file-based and F# Interactive examples and no Python dependency.


Verification: Execute both language examples against local-worker and trusted in-process transports and test slow consumers and disposal during a run.

Parent: ARCH-GOAL-004

**ARCH-API-003**

## Safe versioned C interface

The C ABI shall use explicit ownership and version negotiation, validate buffer sizes/handles and contain language-level unwinding; it shall not promise containment of arbitrary native memory corruption.


Verification: Exercise stale handles, malformed lengths, injected panics/exceptions, double-release policy and older supported client versions under applicable native diagnostics.

Parent: ARCH-GOAL-004
Parent: ARCH-GOAL-005

**ARCH-API-004**

## Bounded versioned worker protocol

The worker protocol shall negotiate versions/capabilities, enforce frame and queue limits, preserve IDs/sequence ordering and use local IPC access controls; unknown incompatible requests shall fail before execution.


Verification: Fuzz frames, lengths, ordering and versions; test unauthorized local access and throttled clients.

Parent: ARCH-GOAL-004

## 11. Result data, streaming and publication

ResultManifest includes execution status, completeness, case/analysis/attempt IDs, accepted ranges, axes, signals, measurements, diagnostics, provenance and artifacts. JSON uses explicit validity fields rather than NaN/Infinity tokens. Model validity warnings remain visible even when the numerical run converges.

Each signal has stable scoped ID, display hierarchy, physical quantity, unit, dtype, axis IDs and polarity/reference direction. AC values preserve real/imaginary binary64 components. Each transient case keeps its own adaptive axis; sweep results may be ragged. Interpolation and discontinuity policy are explicit. A presentation layer may downsample for display, but it must not replace full requested analysis data silently.

Accepted-step batches flow through a minimal ResultSink contract with bounded buffers. Arrow IPC streams serve live clients; Arrow IPC files serve durable individual runs; Parquet is an optional later archival/campaign format. .NET uses Arrow .NET; batch buffers have explicit borrowed/owned lifetime and cannot outlive their owner accidentally. Ordinary results are host data; device-memory zero-copy is not presumed supported across the SDK boundary.

Each batch envelope contains run/analysis/attempt/schema IDs, sequence, sample offset and count. Retry creates a distinct attempt stream. Backpressure follows a selected bounded block/spill/fail policy; no silent drops. Blocked output pauses simulation at safe points and still permits cancellation. Measurements may be computed online to avoid storing unnecessary waveforms, but must record window, input signals, units, interpolation and completeness.

Artifacts are staged in a run directory. Flush/close and hash them before atomically publishing a completed manifest on supported local filesystems. Object storage requires a separate commit protocol. On write failure the result is failed/partial, not succeeded with dangling artifact paths. Paths in imported manifests are untrusted and resolved under authorized roots.


**ARCH-DATA-001**

## Self-describing result schemas

Results shall use versioned manifests and signal/axis descriptors that preserve quantity, units, complex components, hierarchy, references, validity and adaptive sampling without reliance on console output or column position.


Verification: Read real/complex/ragged/partial golden results using Rust and .NET readers and reconstruct axes after column reordering.

Parent: ARCH-GOAL-004

**ARCH-DATA-002**

## Bounded lossless streaming

Requested accepted samples shall be streamed with IDs, ordering and explicit buffer ownership under a bounded backpressure policy; cancellation/failure shall terminate the stream with completeness metadata.


Verification: Throttle a reader, interrupt a run and detect missing/duplicate batches while measuring memory bounds.

Parent: ARCH-GOAL-004
Parent: ARCH-GOAL-005

**ARCH-DATA-003**

## Durable truthful publication

A completed manifest shall reference only closed integrity-checked artifacts and shall record provenance and measurement completeness; interrupted, nonconverged or invalid-evidence outcomes shall not appear as qualified assertion passes.


Verification: Inject disk-full, corruption and interruption during publication and test missing-crossing, model-domain and partial-window assertions.

Parent: ARCH-GOAL-005

## 12. Compatibility without architectural inheritance

Dialect adapters translate a documented subset into the canonical IR. Compatibility options live in an explicit CompatibilityProfile, affect elaboration or source semantics deliberately and are included in provenance. They are not global booleans scattered through device and solver code.

Unsupported syntax fails at the source span. Translation may produce a warning when meaning is changed, but must not pretend exact compatibility. Vendor control languages and encrypted models are separate capabilities; no command shell is required for ordinary simulation. Keep a corpus with parse, elaborate, solve and numerical-match outcomes and denominators.

Future mixed-signal support belongs in a separate event-kernel/co-simulation package with explicit time synchronization, rollback and algebraic-loop rules. Do not recreate a general XSPICE-style extension engine before actual requirements exist. Future schematic capture and plotting consume the public API rather than acquiring access to internal solver/model state.

ngspice/Xyce oracle runners are test-only process adapters using pinned binaries, known options and legally usable inputs. Disagreement is investigated using equations, independent references and conservation checks. Matching a historical simulator is not by itself proof of correctness.


**ARCH-COMPAT-001**

## Compatibility confined to adapters

Syntax/dialect behavior shall be implemented through explicit profiles and canonical-IR translation without legacy runtime types or scattered hidden solver/model switches; unsupported constructs shall produce source-located diagnostics.


Verification: Run ambiguous dialect fixtures through each profile and inspect the canonical IR, warnings and effective provenance.

Parent: ARCH-GOAL-001

## 13. Verification, comments and code coverage

Code comments explain public interfaces, assumptions, units, sign conventions, ownership, unsafe invariants and non-obvious numerical choices. StrictDoc requirements link to implementation/tests once they exist. Architecture decisions document why a backend/model/algorithm was adopted and its rejected alternatives where relevant.

Testing layers:

1. Matrix adapter conformance: small independently solved systems, nonsymmetric/indefinite/complex matrices, singularity, index conversion, stale handles and device errors.
2. Device qualification: analytic equations, IV/CV/charge/noise behavior and independent derivative checks over qualified domains.
3. Circuit qualification: analytic RC/RLC, conservation, stiff/nonlinear/discontinuous/singular/multistable cases and independently adjudicated ngspice/Xyce comparisons.
4. Stateful testing: rejected steps, initial conditions, event ordering, retries, cancellation and checkpoint replay.
5. Interface conformance: golden Rust/C/.NET requests and results, malformed frames and partial data.
6. Property/random tests: node-renaming and hierarchy invariants, valid matrix generators, seeded campaigns and .NET FsCheck client properties. Retain seeds and minimized examples. Reject generated invalid circuits explicitly rather than assume every random topology is solvable.
7. Fuzzing: parsers, expressions, model descriptors, protocol frames and artifact readers with bounded budgets. Untrusted native model fuzzing runs in isolated workers.
8. Mutation testing: alter source/dialect semantics, stamp signs, derivative entries, convergence predicates, state commit order and result completeness to check that tests detect faults. Investigate equivalent/surviving mutants and keep working source clean.
9. Hardware CI: real supported GPUs, no-device machines, driver/runtime combinations and numerical comparison to CPU references. CPU emulation cannot establish GPU driver correctness.

Prefer cargo-nextest for Rust execution, with separate runners where needed for doctests; Pester for PowerShell, FsCheck for .NET properties. Select Rust/.NET mutation and coverage tools during M0 based on supported toolchains and mixed native coverage. No specific coverage percentage is assumed approved. Report line/branch coverage where supported and separate core Rust, owned native shim, .NET and device-kernel measurement. Vendor source is inventoried separately, not represented as covered by testing the wrapper.

Coverage does not prove numerical accuracy. Record actual reports, exclusions, tool/revision and unavailable metrics. A mutation score does not replace analytic/reference tests. Benchmarks record full workflow and memory, not only kernels. Current prototype coverage and testing limitations are recorded under evidence/; neither those results nor this architecture establish complete simulator qualification.


**ARCH-VERIFY-001**

## Independent numerical and stateful tests

Qualification shall combine independent numerical references, conservation, derivative checks, state/rollback tests and multi-interface conformance; a single legacy simulator shall not be the universal oracle.


Verification: Review fixture provenance and demonstrate detection of representative stamp-sign, stale-state and complex-solve defects.

Parent: ARCH-GOAL-005

**ARCH-VERIFY-002**

## Adversarial and randomized testing

The verification strategy shall apply malformed-input tests, fuzzing, mutation and randomized/property-based testing to relevant boundaries and logic, preserve reproducible minimized failures and report executed budgets and limitations.


Verification: Replay retained seeded failures, fuzz regressions and mutation reports and reconcile them with the release evidence.

Parent: ARCH-GOAL-005

**ARCH-VERIFY-003**

## Coverage and traceability evidence

Code changes shall include actual coverage metrics, line/branch data where supported, scopes/exclusions and limitations, with requirement-to-implementation/test traceability and meaningful updated code comments.


Verification: Inspect retained coverage artifacts and links for the changed revision; explicitly report unmeasured native/GPU paths rather than infer their coverage.

Parent: ARCH-GOAL-005

## 14. Implementation sequence and exit gates

M0 feasibility spikes (time-boxed experiments, not production claims):

- Validate DAE sign/charge conventions and limiting corrections on analytic devices.
- Compare sparse CPU candidates and license/distribution constraints using real/complex general matrices.
- Bind a pinned compiled-model ABI and compare one reference model across an independent implementation.
- Exercise a tiny cuDSS or Ginkgo adapter and measure transfer-inclusive behavior on available hardware.
- Compare a second independent linear provider and trial a SUNDIALS or PETSc nonlinear/DAE adapter; verify nonlinear-charge residual/Jacobian mapping and single-controller ownership before committing to reuse above the linear layer.
- Round-trip Rust -> C ABI/worker -> C#/F# -> Arrow result and verify cancellation/ownership.
- Decide sandbox policy by supported operating system and native dependency packaging.

M0 exit: numerical conventions, trait/ABI specifications, initial schemas, backend/model shortlist, platform/license decisions, test/coverage tool choices and measured feasibility evidence reviewed. No advanced GPU promise without available hardware.

M1 walking skeleton: immutable IR and plan; R/C/L and sources, then qualified diode/transistor subset; CPU sparse direct adapter; operating point -> AC -> transient with rollback; structured errors/results; worker and .NET SDK. Keep a small but difficult qualification corpus from the start. Achieve end-to-end usable simulations before broad model/dialect breadth.

M1 exit: analytic and convergence suite passes approved fixture bounds; C#/F# examples run on declared platforms; malformed inputs and ownership faults are controlled; coverage/mutation/fuzz/random reports exist; no known correctness-critical defects. The supported profile explicitly lists models/dialects/analysis limitations.

M2: qualify OSDI model packages, optional GPU sparse/batched execution, larger corpus, noise/statistical workflows and performance thresholds. Evaluate GPU model kernels only for profiled model-evaluation bottlenecks. M2 exit requires real hardware and real model evidence, bounded recovery, reproducibility envelope and deployment/license approval.

M3: choose periodic/RF, distributed single-circuit solving or mixed-signal packages based on demand and separate numerical design reviews. No automatic migration of legacy feature lists into the core.

First vertical slice: a C# or F# script submits an RC step request, compiles one canonical plan, executes CPU transient, reads voltage batches and a measurement, cancels a second run, and receives a truthful partial manifest. The same request later runs through a GPU-capable plan without changing its circuit semantics or result schema.


**ARCH-RELEASE-001**

## Evidence-gated stability

Releases shall mark only qualified model/backend/analysis combinations stable and publish capability profiles, known limitations, coverage and numerical/performance evidence for their exact versions and supported platforms.


Verification: Compare stability flags and distribution contents to the adoption and release evidence; reject undocumented combinations.

Parent: ARCH-GOAL-005

## 15. Language decision and simulation-definition DSL

Provisional recommendation, not a claim that language choice has been approved: Rust for the owned core/runtime, narrow C/C++ native adapters for established numerical libraries, C# for GUI and the managed SDK, F# or C# for trusted automation, and PowerShell for shell orchestration. A pure-Rust implementation of every dependency is not a goal.

Rust versus C++ is primarily an ownership, integration and team-productivity decision here. Rust provides compiler-checked ownership for the code it controls, which is useful for concurrent sessions, asynchronous buffer lifetimes, cancellation and plugin interfaces. C++ provides direct access to a broad native numerical/GPU ecosystem and may reduce adapter work when most development happens inside those frameworks. Neither language guarantees a better solver or universally faster simulation. Both can call the same native numerical libraries; matrix layout, algorithms, transfers, batching and profiling determine the measured outcome.

The current boundaries favor Rust because vendor libraries live behind a few matrix/model interfaces. Prefer coarse matrix/batch calls over crossing a language boundary for each coefficient. C ABI shims are suitable for packaged cross-language boundaries; CXX is an option for narrow internal Rust/C++ bridges, not an automatic stable public plugin ABI. Native implementations still require ownership and error audits and worker isolation where untrusted.

Reconsider a C++ core if the available team is substantially more productive in C++, required numerical APIs cannot be wrapped cleanly, or custom C++/CUDA device kernels dominate the implementation. Measure build/package complexity and actual adapter overhead in M0 rather than decide from language microbenchmarks. C# GUI choice does not force either native core language; it uses the same C ABI or worker service in both cases.

Assumption: the user's reference to a scripting language means Starlark. Starlark is a Python-like embedded configuration language, not a requirement to install or run Python. Its similarity to Python syntax does not change the C#/F# automation default. The proposed simulation dialect supplies domain-specific host functions and unit-aware values; it reuses a language implementation instead of inventing a complete parser, evaluator, debugger and editor ecosystem.

Starlark is intended for deterministic structured definitions. The Rust implementation provides host-type integration and tooling components, but advertises extensions and upstream API evolution. Pin the implementation and explicitly select the allowed language profile; do not assume every implementation enforces the same recursion, top-level syntax or numeric rules. Hermetic behavior depends on the host functions/module loader we expose. Bounded language constructs do not remove the need for CPU, memory and expansion limits.

Candidate comparison:

- Starlark with a simulation library: preferred experiment for reproducible circuits, parameter grids, analysis plans, measurement definitions and assertions; Rust embedding available. Needs domain units, controlled imports, resource limits, pinned profile and source-mapped diagnostics.
- Rhai: Rust-native embedding alternative for more imperative procedural scripting. Its loops require explicitly configured operation limits, and host functions require their own budgets. Do not select it merely to recreate unrestricted control scripts inside the simulator.
- F# embedded DSL: attractive first-party trusted authoring path using records/builders, types and units of measure where applicable; reuses .NET tooling and feeds the same contract. Arbitrary .fsx execution is trusted application execution, not a hermetic untrusted configuration sandbox.
- Brand-new language: defer unless user studies demonstrate a concrete semantic/syntax gap that host bindings cannot solve. It creates ongoing grammar, tooling, compatibility and security obligations.

Recommended processing stages: bounded evaluation of definition modules -> immutable CircuitDefinition and ExperimentPlan -> dimension/topology/model validation -> elaborated circuit and compiled execution plan -> runtime execution -> versioned results. The language frontend can be replaced without changing the numerical core. GUI editing and C#/F# builders produce the same objects. Preserve source spans through generated subcircuits and parameter expansions.

Illustrative proposed Starlark host API only; these functions do not exist yet and this is not a tested executable example:

load("@sim/v1", "circuit", "voltage_step", "resistor", "capacitor", "experiment", "transient", "value_at", "within")

rc = circuit(
    name = "rc_step",
    elements = [
        voltage_step("V1", positive = "in", negative = "0", low = "0 V", high = "1 V", rise = "1 ns"),
        resistor("R1", positive = "in", negative = "out", resistance = "1 kohm"),
        capacitor("C1", positive = "out", negative = "0", capacitance = "1 uF"),
    ],
)

startup = transient(id = "startup", stop = "5 ms", max_step = "10 us", record = ["v(out)"])
final_voltage = value_at(id = "final_voltage", analysis = "startup", signal = "v(out)", at = "5 ms", interpolation = "linear")

plan = experiment(
    circuit = rc,
    analyses = [startup],
    measurements = [final_voltage],
    assertions = [within(measurement = "final_voltage", low = "0.99 V", high = "1 V", missing = "indeterminate")],
)

The host parses quantity strings into dimensioned values; bare strings are not passed unvalidated to solvers. Structured JSON and .NET builders use the same units and validation rules. The unit grammar must specify engineering prefixes, dimensional compatibility and absolute versus delta temperatures. Standard or host numeric support must be tested, not guessed from an old Bazel language subset.

Definition evaluation does not call solve, read result files, access the network or mutate a live job. It returns an immutable plan. Sweeps and Monte Carlo are declarative plan nodes with explicit seeds and bounded expansion. Adaptive optimization or interactive experimentation runs in the trusted .NET orchestrator or a separately specified bounded workflow engine, not as hidden interpreter callbacks during Newton iteration.

Behavioral source/model equations require another boundary: parse expressions into a typed differentiable expression IR with supported operators, domain checks and derivatives, then interpret efficiently or compile for a qualified CPU/GPU target. Do not use general Starlark/Rhai callbacks at every model evaluation or estimate derivatives by blindly rerunning arbitrary scripts. Verilog-A/OSDI remains the path for reusable compact device models. Definition DSL, behavioral equation IR and model ABI are separate artifacts with separate compatibility rules.

The @sim/v1 module is a host-provided versioned capability, not a network fetch. User modules load only from authorized roots or pinned packages; loader policy, source content, implementation/profile, host-library version, parameters and relevant external asset hashes contribute to canonical provenance. A fixed script alone does not guarantee deterministic numerical results across devices, solver versions or parallel reductions.

M0 comparison: author the same RC, nested subcircuit, sweep and assertion scenario through Starlark, F# and JSON; verify canonical equivalence, import/resource restrictions and diagnostics. Add one nonlinear behavioral source to test expression/derivative IR separately. Compare editor experience, dependency/build size, execution budgets and source mapping. Select and stabilize the DSL only after this evidence. The prototype has a tested F# builder and a separate experimental expression tree; Starlark, hierarchy and integrated behavioral devices remain unimplemented.


**ARCH-LANG-001**

## Evidence-based implementation language decision

The implementation-language decision shall compare Rust and C++ using actual solver/model adapter, ownership, packaging, team-productivity and workload measurements while preserving a language-independent C ABI/worker boundary for C#/F# clients.


Verification: Review the M0 decision record and native/managed vertical-slice evidence; distinguish measured outcomes from language preferences.

Parent: ARCH-GOAL-001
Parent: ARCH-GOAL-004

**ARCH-DSL-001**

## Canonical definition frontend

A simulation-definition DSL shall produce the same validated canonical circuit and experiment objects as structured inputs and C#/F# builders without owning solver execution or exposing native handles.


Verification: Compare canonical plans, effective parameters and validation outcomes for equivalent DSL, JSON and .NET scenarios.

Parent: ARCH-GOAL-001
Parent: ARCH-GOAL-004

**ARCH-DSL-002**

## Bounded hermetic language profile

The DSL host shall pin its evaluator/profile, restrict host functions/imports to declared capabilities, enforce time/memory/operation/expansion limits and reject unauthorized filesystem, network, environment or process access.


Verification: Exercise recursive/large allocations, huge expansions, forbidden imports, path escapes and expensive host calls with reproducible fuzz and budget tests.

Parent: ARCH-GOAL-005

**ARCH-DSL-003**

## Dimension-aware definitions and diagnostics

DSL quantities, references and generated objects shall undergo dimensional, topology and model validation with source locations retained across imports and expansions, using the same numeric/unit semantics as other frontends.


Verification: Test prefix ambiguities, incompatible units, temperature distinctions, unresolved references and errors inside generated subcircuits.

Parent: ARCH-GOAL-004
Parent: ARCH-GOAL-005

**ARCH-DSL-004**

## Compiled behavioral equation boundary

Behavioral equations shall use a separately validated expression IR with derivative/domain semantics and qualified execution targets; the general definition interpreter shall not be invoked inside the Newton/device-evaluation hot loop.


Verification: Inspect execution paths and compare nonlinear expression values/derivatives against independent analytic or differentiation references on CPU and any claimed GPU target.

Parent: ARCH-GOAL-003
Parent: ARCH-GOAL-005

**ARCH-DSL-005**

## Reproducible authoring and explicit trust

Compiled plans shall record source/import hashes, evaluator/profile and host-library versions, parameter assignments and seeds; trusted C#/F# orchestration shall remain distinct from execution of untrusted definition modules.


Verification: Re-evaluate pinned inputs, invalidate caches after module/library changes and verify the untrusted path cannot invoke trusted script execution.

Parent: ARCH-GOAL-004
Parent: ARCH-GOAL-005

## 16. Open decisions and source references

Open decisions before implementation commitment:

- Project license and compatible CPU/model/backend distribution policy.
- Initial CPU solver and exact real/complex/index capabilities; optional Rust-native alternative.
- Baseline model/dialect whitelist and limits on behavioral expressions.
- Exact internal model trait, compiled ABI version and limiting-correction semantics.
- Supported operating systems, .NET target/RIDs, Rust MSRV and enforceable sandbox policies.
- Numerical defaults, dimensional tolerances, convergence corpus and reference hardware.
- Exact coverage/mutation tooling, CI budgets and project thresholds.
- Exact worker/schema versions and actual StressForge contract mapping.
- GPU hardware available for qualification and measured selection thresholds.
- Final Rust versus C++ decision after adapter/team evaluation; simulation DSL choice/profile and behavioral expression IR boundaries.

Sources consulted 2026-10-24. Architectural recommendations are project design decisions, not claims made by these sources. Pin adopted versions; documentation alone does not qualify an adapter.

S1 SuiteSparse official repository: https://github.com/DrTimothyAldenDavis/SuiteSparse . KLU and component-level sparse solver/licensing information.

S2 NVIDIA cuDSS: https://docs.nvidia.com/cuda/cudss/latest/index.html and https://docs.nvidia.com/cuda/cudss/latest/general.html . Sparse direct solver capabilities, phased execution, supported configurations and asynchronous/native failure behavior.

S3 Ginkgo: https://ginkgo-project.github.io/ and https://github.com/ginkgo-project/ginkgo . Sparse linear algebra and CUDA/HIP/SYCL/OpenMP executor support. Check pinned release documentation rather than assume development-branch APIs are stable.

S4 SUNDIALS GPU model: https://sundials.readthedocs.io/en/latest/sundials/GPU_link.html . Vector/matrix/solver abstraction and explicit host/device data coherency responsibilities.

S5 Compiled model interoperability: https://ngspice.sourceforge.io/osdi.html . OSDI loads compiled Verilog-A compact models; reuse the standard through an adapter, not the host simulator runtime. Exact ABI mapping remains to be qualified.

S6 Arrow .NET: https://github.com/apache/arrow-dotnet and Arrow format: https://arrow.apache.org/docs/format/Columnar.html . Columnar transport and managed reader foundation.

S7 ILGPU: https://ilgpu.net/ . Optional managed GPU kernel tooling, distinct from a complete sparse solver or mandatory Rust-core dependency.

S8 StrictDoc: https://strictdoc.readthedocs.io/en/stable/stable/docs/strictdoc_01_user_guide.html . SDoc requirements, sections and traceability. This document targets the locally verified StrictDoc 0.30.4 composite-section grammar.

S9 PETSc solver families: https://petsc.org/release/overview/ . KSP/PC linear solvers/preconditioners, SNES nonlinear solvers, TS time integration and TAO optimization; capabilities depend on selected methods and configured build.

S10 SUNDIALS package roles: https://sundials.readthedocs.io/en/latest/ . IDA/IDAS for DAE integration and KINSOL for nonlinear systems; callback/initialization compatibility still requires simulator-specific qualification.

S11 Starlark specification: https://github.com/bazelbuild/starlark/blob/master/spec.md and Rust implementation: https://github.com/facebook/starlark-rust . Configuration-language intent, host embedding and implementation-specific extensions/tooling.

S12 Rhai resource limits: https://rhai.rs/book/safety/max-operations.html . Operation budgets require explicit configuration and do not automatically bound expensive host calls.

S13 Rust ownership: https://doc.rust-lang.org/book/ch04-01-what-is-ownership.html and CXX interop: https://cxx.rs/reference.html . Ownership and narrow native bridging; neither establishes application numerical correctness or native-plugin safety.

Maintenance rule: preserve ARCH-* IDs. When a decision changes, update this document and relevant lower-level specifications together. Add implementation/evidence links only when the referenced artifacts exist. Do not regenerate this architecture from an old requirements generator that retains superseded language/interface choices.
