<!-- Readable projection; authoritative source: second-slice.sdoc -->

## Second Reference Slice - Solver Variety and Dynamic Circuits

Iterations 51-100 extend the bounded reference engine. Builds occur at 60, 70, 80, 90 and 100. No GPU, native model/plugin, nonlinear transistor or full-release qualification is implied.


**REF2-001**

## Independent sparse solver path

The bounded reference engine shall support a second independent numerical library through a sparse adapter, explicit capabilities, validated layouts, residual checks and cross-provider tests, while retaining its stated circuit-size limits.


Planned source: src/sparse.rs, src/backend_faer.rs, tests/providers.rs.
The existing circuit assembler remains bounded dense storage; sparse factorization alone is not a large-circuit scalability claim.

Parent: ARCH-PORT-002

**REF2-002**

## Reusable immutable numeric factors

A prepared factorization shall own its matrix identity and reject invalid RHS inputs; callers shall not reuse numeric factors after matrix changes.


Planned source: src/linalg.rs, src/backend.rs, src/backend_faer.rs.
Verification includes repeated RHS, altered matrices and independently checked residuals.

Parent: ARCH-LIN-003

**REF2-003**

## Linear dynamic-circuit extension

The next dynamic path shall expose explicit initial-state, time-source, integration and resource semantics and verify RLC and adaptive behavior against analytic references.


Adaptive linear integration does not establish nonlinear timestep/model rollback correctness. Unavailable features remain explicit.

Parent: ARCH-EQ-004

**REF2-004**

## Managed contracts and truthful evidence

The managed API and verification tooling shall preserve typed request/results, explicit failures, reproducible campaigns and actual coverage scope without counting historical evidence as current verification.


Verification: Rust, C# and F# contract runs, mutation and bounded fuzz/property tests, hash-matched current evidence.

Parent: ARCH-API-002
Parent: ARCH-VERIFY-003
