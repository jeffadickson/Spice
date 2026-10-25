# Implementation status after 50 passes

The first executable slice is a **linear, small-circuit reference implementation**, not completion of the full 23-package plan.

| Plan area | Current evidence | Remaining |
|---|---|---|
| PLAN-001 foundations | Rust/.NET build harness, lockfile, local tests and CI configuration | remotely exercised platform matrix, complete coverage setup and distribution license |
| PLAN-002 equations | Linear R/C/source stamps and analytic checks | inductors, diode/charge limiting, nonlinear state and reviewed full conventions |
| PLAN-003/008/016 solvers | nalgebra dense LU/QR and registry, independent residual checks | sparse backends and two independent qualified provider libraries |
| PLAN-004/011 API | bounded one-shot JSON process calls from C#/F# | C ABI, persistent worker lifecycle, sandbox, complete typed contracts |
| PLAN-007/009 circuit/model boundary | validated immutable plan and minimal linear contribution contract | hierarchy/structural models, complete DAE device interface and plugins |
| PLAN-010/012/013 analyses | linear DC, complex AC, fixed-step RC transient | nonlinear operating point, adaptive/error-controlled transient and event/rollback suite |
| PLAN-014 results | JSON signals, hashes and completeness; measurement/assertion helpers | Arrow streaming, durable commit protocol and integrated measurements |
| PLAN-015 input | tiny explicit R/C/V/I parser | broad SPICE profiles/includes/subcircuits and legally usable vendor corpus |
| PLAN-017 tests | analytic/negative/random/fuzz-smoke/mutation and managed coverage | coverage-guided fuzzing, broader mutation/coverage, native/GPU qualification |
| PLAN-018 release | no release claim | all required profile/packaging/qualification gates |
| PLAN-019..023 extensions | F# authoring and expression IR experiments only | plugins, GPU, selected DSL, campaigns and advanced analyses |

The original work-package status fields remain Proposed to avoid treating partial prototype evidence as completion. This table is the more granular progress view. No external runtime source or historical simulator implementation has been inherited.

