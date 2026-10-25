
# Reference Engine - Implemented Scope and Acceptance Contracts
Version 0.1. This is a bounded prototype, not an M1 release. Implemented contracts below have source/test mappings in COMMENT fields. Verification is claimed only by the actual checkpoint evidence. Architecture requirements remain Proposed; a small implementation does not satisfy their full qualification scope.


**REF-001**

### Validated bounded linear circuits
The reference engine shall validate explicit ground, unique bounded labels, terminal indices and finite SI parameters for resistors, capacitors and independent DC voltage/current sources before matrix assembly.

Implementation: src/circuit.rs, src/compile.rs, src/model.rs.
Verification: tests/dc.rs, tests/hardening.rs, tests/properties.rs.

Parent requirement: ARCH-STATE-003

**REF-002**

### Replaceable reference solves with residual checks
The reference engine shall offer per-engine solver registration and CPU dense LU/QR adapters, reject unsupported complex requests and verify returned physical residuals independently.

Implementation: src/linalg.rs, src/backend.rs, src/registry.rs, src/dc.rs, src/ac.rs.
Verification: tests/dc.rs, tests/faults.rs, tests/hardening.rs, tests/analyses.rs.
Both adapters currently use nalgebra; two independent production solver providers have NOT been qualified. Pattern/factor reuse, sparse and GPU backends are not implemented.

Parent requirement: ARCH-LIN-001
Parent requirement: ARCH-LIN-004

**REF-003**

### Explicit fixed-step transient profile
The reference transient engine shall use backward Euler from zero stored-energy initial state with constant sources, bounded steps/output retention, cooperative cancellation and commit only after solve/residual acceptance.

Implementation: src/transient.rs.
Verification: tests/transient.rs, tests/faults.rs, tests/hardening.rs.
Adaptive error control, nonlinear rollback, event processing and persisted partial/checkpoint output are not implemented. Output begins after the first positive step, not at an invented time-zero sample.

Parent requirement: ARCH-EQ-004

**REF-004**

### Complex linear AC profile
The reference AC engine shall assemble G+j*omega*C for positive bounded frequency arrays and expose real/imaginary binary64 values with frequency units, checking the backend solution residual.

Implementation: src/ac.rs, src/api.rs.
Verification: tests/analyses.rs, tests/hardening.rs.
Existing real source values are reused as real AC amplitudes; separate DC/AC source phase and amplitude specifications are deferred. AC support is provider-dependent.

Parent requirement: ARCH-EQ-001

**REF-005**

### JSON process transport and managed clients
The reference CLI shall accept at most 1 MiB of versioned JSON input and emit one JSON success/failure envelope with explicit completeness; C#/F# clients shall access it without a shell and use explicit cancellation/disposal.

Implementation: src/api.rs, src/main.rs, dotnet/Simulator.Client/Client.cs.
Verification: tests/contracts.rs, dotnet/Simulator.Conformance/Program.cs, examples/Run.cs, examples/Run.fsx, examples/BuildCircuit.fsx.
This is one-shot JSON process transport, not Arrow streaming, persistent worker isolation, sandboxing, a C ABI or the complete typed SDK. Native executable code is trusted.

Parent requirement: ARCH-API-001
Parent requirement: ARCH-API-002

**REF-006**

### Measurable and reproducible validation
The prototype shall retain numerical regression results, reproducible bounded random/fuzz-smoke campaigns, isolated deliberate code-mutation outcomes and actual coverage metrics or explicit tooling limitations.

Implementation: tests/, scripts/Mutation.cs, scripts/Test-Coverage.ps1.
Verification: evidence/checkpoint-*.json, evidence/mutation.json, evidence/coverage.json.
Bounded fuzz smoke is not coverage-guided libFuzzer qualification. Mutation evidence covers seven chosen operators, not all possible mutants. Windows GNU Rust coverage is unavailable when profiler_builtins is missing; managed coverage does not measure Rust code.

Parent requirement: ARCH-VERIFY-002
Parent requirement: ARCH-VERIFY-003

**REF-007**

### Separate authoring and behavioral expression experiments
The experimental F# builder shall produce the canonical request contract, and the host-owned behavioral expression IR shall evaluate bounded value/derivative expressions without calling a general scripting interpreter.

Implementation: examples/BuildCircuit.fsx, src/expression.rs.
Verification: F# builder execution, tests/expression.rs.
No Starlark/Rhai interpreter, native model plugin, complete units DSL, GPU code generation or integrated behavioral-source model is implemented.

Parent requirement: ARCH-DSL-001
Parent requirement: ARCH-DSL-004