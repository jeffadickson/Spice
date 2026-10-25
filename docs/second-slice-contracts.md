<!-- Readable projection; authoritative source: second-slice-contracts.sdoc -->

## Extended Reference Engine - Acceptance Contracts

Implemented scope for iterations 51-100, subject to current evidence. The compiler remains bounded dense storage even when its adapter uses real sparse factorization. These contracts do not claim complete production qualification or every planned capability.


**REF2-005**

## Scoped numerical provider agreement

The bounded engine shall compare independent faer sparse and nalgebra dense solves on nonsymmetric/indefinite real and RC complex systems and validate each result with a backend-independent residual.


Implementation: src/backend_faer.rs, src/backend.rs, src/dc.rs, src/ac.rs, src/sparse.rs.
Verification: tests/providers.rs, tests/experiment.rs, tests/edge_cases.rs.
No GPU, general ill-conditioned-matrix guarantee or large-circuit memory scalability is claimed.

Parent: REF2-001

**REF2-006**

## Consistent linear dynamic analyses

Configured transient runs shall support signed inductor flux, consistent explicit or operating-point initial states, PWL/sine sources, breakpoint clipping, bounded backward-Euler/trapezoidal steps and optional local step-doubling control with accepted-state-only publication.


Implementation: src/compile.rs, src/waveform.rs, src/sources.rs, src/configured.rs, src/integrator.rs, src/adaptive.rs.
Verification: tests/dynamic.rs, tests/adaptive.rs, tests/edge_cases.rs.
Local error estimates are not global waveform bounds. Native/nonlinear history rollback and all higher-index DAE topologies are not qualified.

Parent: REF2-003

**REF2-007**

## Typed experimental API and validity-aware measurements

C#/F# callers shall have typed circuit/analysis/result DTOs; measurements shall retain units and unavailable reasons, and missing or incompatible-unit assertions shall be indeterminate instead of passing.


Implementation: dotnet/Simulator.Client/Requests.cs, Results.cs, Client.cs, src/experiment.rs.
Verification: dotnet/Simulator.Conformance/Program.cs, tests/experiment.rs, tests/storage_power.rs, examples/Typed.cs, examples/TypedRlc.fsx.
The DTOs cover the experimental JSON profile, not the entire planned API, Arrow or a C ABI.

Parent: REF2-004

**REF2-008**

## Local result artifact integrity

A trusted local artifact publisher shall reject invalid IDs and overwrites, bound serialized result bytes, publish its manifest only after data closure/integrity recording, and verify byte count/hash/schema on read.


Implementation: src/artifact.rs.
Verification: tests/storage_power.rs.
This is trusted local filesystem publication, not hostile filesystem sandboxing, signed provenance, object-storage transactions, power-loss durability proof or untrusted worker isolation.

Parent: ARCH-DATA-003

**REF2-009**

## Physical diagnostic and expression domain checks

The prototype shall expose signed DC device power diagnostics and reject invalid/nonfinite scalar-expression domains while checking new derivatives independently.


Implementation: src/power.rs, src/expression.rs.
Verification: tests/storage_power.rs, tests/expression.rs.
Expression evaluation remains an isolated scalar experiment, not an integrated nonlinear device model or GPU compiler.

Parent: ARCH-VERIFY-001

**REF2-010**

## Stable fixed-step endpoint handling

Configured fixed-step runs shall use an absolute nominal time grid with explicit endpoint-roundoff snapping before source-breakpoint clipping, avoiding artificial tiny terminal steps without weakening numerical residual acceptance.


Implementation: src/configured.rs.
Verification: tests/endpoint.rs compares dense and sparse RLC runs with exactly 200 nominal steps; examples/TypedRlc.fsx exercises the managed boundary. Actual source corners still take precedence over endpoint snapping.

Parent: REF2-003
