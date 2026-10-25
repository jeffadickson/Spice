# Implementation backlog

Derived from docs/implementation-plan.sdoc. All packages are Proposed; no implementation is complete. Dependencies are logical prerequisites, not elapsed-time estimates.

| Package | Phase | Work | Depends on | Size |
|---|---|---|---|---|
| PLAN-001 | M0 | Establish repository, toolchains and verification harness |  | Small to Medium |
| PLAN-002 | M0 | Freeze equation, unit and model-state conventions | PLAN-001 | Medium |
| PLAN-003 | M0 | Evaluate CPU providers and Rust/C++ integration | PLAN-001, PLAN-002 | Medium, time-box unsuccessful provider builds and record rejection reasons. |
| PLAN-004 | M0 | Prove Rust-to-.NET result and lifecycle contracts | PLAN-001 | Medium. |
| PLAN-005 | M0 | Validate external nonlinear/DAE integration fit | PLAN-002, PLAN-003 | Research |
| PLAN-006 | M0 | Evaluate compiled-model, GPU and DSL boundaries | PLAN-002, PLAN-003, PLAN-004 | Research |
| PLAN-007 | M1a | Implement immutable circuit and plan compilation | PLAN-002, PLAN-003, PLAN-004 | Medium to Large. |
| PLAN-008 | M1a | Productionize the first CPU backend | PLAN-003 | Medium to Large, depending on native packaging. |
| PLAN-009 | M1a | Implement baseline device evaluation and assembly | PLAN-002, PLAN-007 | Large |
| PLAN-010 | M1a | Deliver operating point and bounded nonlinear solve | PLAN-007, PLAN-008, PLAN-009 | Large. |
| PLAN-011 | M1a | Implement bounded worker jobs and typed results | PLAN-004, PLAN-007, PLAN-010 | Large. |
| PLAN-012 | M1b | Implement AC from operating-point linearization | PLAN-008, PLAN-009, PLAN-010, PLAN-011 | Medium to Large. |
| PLAN-013 | M1b | Implement transient integration and transactional state | PLAN-009, PLAN-010, PLAN-011 | Large |
| PLAN-014 | M1b | Deliver streaming, measurement and durable evidence | PLAN-011, PLAN-012, PLAN-013 | Large. |
| PLAN-015 | M1b | Add a scoped SPICE compatibility frontend | PLAN-007, PLAN-010 | Large, scope constrained by a reviewed construct whitelist. |
| PLAN-016 | M1 | Qualify a second independent provider and selection policy | PLAN-003, PLAN-008, PLAN-012, PLAN-013 | Medium to Large. |
| PLAN-017 | M1 | Maintain continuous numerical and adversarial qualification | PLAN-001, PLAN-002 | Ongoing |
| PLAN-018 | M1 | Publish the first qualified CPU release profile | PLAN-011, PLAN-012, PLAN-013, PLAN-014, PLAN-015, PLAN-016, PLAN-017 | Medium to Large after component gates pass |
| PLAN-019 | M2 | Qualify compiled-model packages and native isolation | PLAN-006, PLAN-009, PLAN-013, PLAN-014, PLAN-017 | Large plus model-family-specific qualification. |
| PLAN-020 | M2 | Qualify optional GPU linear and batched execution | PLAN-006, PLAN-008, PLAN-013, PLAN-014, PLAN-016, PLAN-017 | Large after hardware and package feasibility are demonstrated |
| PLAN-021 | M2 | Implement the selected definition DSL and behavioral IR | PLAN-006, PLAN-007, PLAN-013, PLAN-014, PLAN-017 | Large |
| PLAN-022 | M2 | Deliver reproducible campaigns and StressForge mapping | PLAN-014, PLAN-016, PLAN-017 | Large |
| PLAN-023 | M2 | Expand analyses and model families by demand | PLAN-018, PLAN-019 | Research to Large per analysis/model family |