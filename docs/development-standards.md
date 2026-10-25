<!-- Readable projection; authoritative source: development-standards.sdoc -->

## General Development Standards

General Development Standards
Version 1.1 — 2026-10-24

Purpose: capture the user's standing development preferences for general use across projects. This document contains no simulator-specific requirements.

Authority: explicit user instructions and applicable higher-priority environment instructions take precedence. Existing project constraints should be reconciled with these defaults and any material conflict explained. Required tool/runtime exceptions are allowed by the language policy; they do not change the default.

Interpretation: shall is normative. Prefer describes the default choice when applicable, not a mandate to introduce a dependency into every project. Always requires either the requested practice or an explicit, truthful account of a genuine tooling limitation. A skipped test or unavailable coverage measurement is not a pass.

Testing strategy: select meaningful checks for the code and interfaces involved. Not every change requires every technique, but every project should consider mutation testing, malformed inputs, fuzzing and randomized/property-based tests. No universal coverage or mutation-score threshold has been approved.

Status: these are recorded user standards, not claims that any existing project already complies. Verification fields describe how to assess compliance. Specific implementations and evidence links belong to the adopting project's StrictDoc traceability records.


## Languages and scripting

**DEV-LANG-001**

## C# and F# scripting

The project shall use C# or F# for scripts and automation by default. Use another language when explicitly requested or required by a tool or workflow.


Verification: Inspect new scripts and document any required exception.


**DEV-LANG-002**

## PowerShell shell operations

The project shall use PowerShell and native PowerShell cmdlets for shell commands and text/file operations by default. Avoid sed, grep, awk and other Unix utilities unless explicitly requested or required by a tool or workflow.


Verification: Review shell commands and verify they execute in the declared PowerShell environment.


**DEV-LANG-003**

## Lightweight .NET scripts

The project shall prefer C# file-based applications and F# Interactive for applicable standalone scripts. Pin or declare the required .NET SDK and package dependencies.


Verification: Execute documented script entry points in a clean supported environment.


## Preferred tools

**DEV-TOOL-001**

## PowerShell tests

The project shall prefer Pester for testing PowerShell scripts and modules when applicable.


Verification: Run relevant Pester tests and preserve their outcome report.


**DEV-TOOL-002**

## .NET property tests

The project shall prefer FsCheck for .NET property-based and randomized testing when applicable.


Verification: Run documented properties and demonstrate replay of a recorded seed or counterexample.


**DEV-TOOL-003**

## Rust test execution

The project shall prefer cargo-nextest for Rust test execution when applicable. Use additional test runners for test categories it does not execute.


Verification: Report the runner and test categories executed, including any separate documentation tests.


**DEV-TOOL-004**

## Columnar data

The project shall prefer Apache Arrow .NET for applicable columnar data interchange and DuckDB for querying Parquet results. Choose these tools based on actual data and access requirements.


Verification: Verify representative data interchange or queries using documented schemas and supported versions.


## Comments, requirements and traceability

**DEV-DOC-001**

## Meaningful code comments

The project shall always comment code meaningfully. Document public interfaces, intent, assumptions, invariants, numerical conventions where relevant, and non-obvious logic. Avoid comments that merely repeat the code.


Verification: Review changed code for adequate explanations of behavior and constraints.


**DEV-DOC-002**

## Accurate comments

The project shall update comments and interface documentation whenever the corresponding behavior, assumptions or constraints change.


Verification: Compare changed behavior with its comments and documented examples.


**DEV-DOC-003**

## StrictDoc requirements

The project shall always use StrictDoc for software requirements. Keep the authoritative requirements in version-controlled SDoc files with stable unique requirement IDs.


Verification: Parse the documents with the declared StrictDoc version and check ID uniqueness.


**DEV-DOC-004**

## Traceability

The project shall maintain traceability from requirements to implementation and verification evidence. Mark proposed, implemented and verified claims accurately; do not equate a written requirement with implemented behavior.


Verification: Inspect requirement-to-code/test links and evidence for claimed verification.


**DEV-DOC-005**

## Verifiable statements

The project shall give each software requirement clear behavior and proposed verification criteria. Record unresolved decisions and assumptions rather than hiding them in implementation.


Verification: Review requirements for observable acceptance criteria and explicit open decisions.


## Code coverage

**DEV-COV-001**

## Coverage reporting

The project shall always collect and report code coverage metrics for code changes. Include line and branch coverage where supported. Explicitly identify unmeasured code and tooling limitations.


Verification: Preserve coverage reports and identify the measured revision, test suite and tool.


**DEV-COV-002**

## Coverage scope

The project shall report coverage scope, exclusions and available changed-code coverage. Keep generated, vendor and handwritten code distinguishable in the report.


Verification: Inspect instrumentation configuration and exclusions against changed files.


**DEV-COV-003**

## Coverage integrity

The project shall never invent coverage results or present unavailable measurements as zero or complete. Treat coverage as evidence of execution, not proof of correctness.


Verification: Compare reported metrics to actual tool output and explicitly state missing measurements.


**DEV-COV-004**

## Project coverage gates

The project shall define project-specific coverage thresholds and regression policies explicitly before enforcing them. Do not assume a universal percentage target.


Verification: Inspect the documented project policy and its automated checks, or record the unresolved threshold decision.


## Testing strategy

**DEV-TEST-001**

## Mutation testing

The project shall use mutation testing on relevant code to check whether tests detect deliberately changed behavior. Report killed, surviving, timed-out and invalid mutants and document reviewed exclusions.


Verification: Run the selected mutation tool and investigate surviving mutants rather than relying only on the score.


**DEV-TEST-002**

## Mutation isolation

The project shall perform mutation testing in disposable or isolated working state and retain the intended source unchanged after the run.


Verification: Check the working state before and after mutation execution and verify cleanup.


**DEV-TEST-003**

## Malformed inputs and negative cases

The project shall test malformed, missing, invalid, oversized and boundary inputs at relevant interfaces, including expected error behavior and unintended side effects.


Verification: Run negative cases and assert explicit errors, preserved invariants and appropriate resource bounds.


**DEV-TEST-004**

## Input fuzzing

The project shall fuzz relevant parsers, protocols, file readers and other input surfaces using declared time/resource budgets and suitable valid and malformed input corpora.


Verification: Record targets, tool/version, budgets, corpus and discovered failures; replay retained regressions.


**DEV-TEST-005**

## Randomized and property-based tests

The project shall use randomized and property-based testing for relevant logic. State the properties, input domains, generators and sample budgets, including boundary cases.


Verification: Run the properties and inspect their domain coverage and counterexamples.


**DEV-TEST-006**

## Reproducible failures

The project shall retain failing seeds, minimized counterexamples, relevant environment/tool versions and replay commands. Convert confirmed bugs into regression tests.


Verification: Replay a retained failure and verify its regression test detects the original defect.


**DEV-TEST-007**

## Meaningful oracles

The project shall test externally observable behavior, invariants and independent reference results. Avoid tests that only reproduce the implementation logic.


Verification: Review test oracles and demonstrate detection of a representative behavioral defect.


**DEV-TEST-008**

## Applicable test strategy

The project shall include mutation, malformed-input, fuzz and randomized/property-based testing in the project test strategy. Apply each method to relevant code and report what ran, what did not run and why.


Verification: Compare the test strategy to changed interfaces and the actual execution report.


**DEV-TEST-009**

## Bounded and repeatable runs

The project shall use bounded local verification runs and documented broader CI or scheduled campaigns where needed. Keep discovered failures reproducible and distinguish deterministic regressions from statistical outcomes.


Verification: Verify budgets, seeds and replay instructions; inspect failure classification.


## GPU awareness and numerical library reuse

**DEV-GPU-001**

## GPU-aware design

The project shall evaluate GPU acceleration for computationally intensive workloads and keep compute-backend selection separate from application logic. Do not introduce GPU dependencies into workloads without a demonstrated need.


Verification: Document candidate workloads and the decision to enable, defer or reject acceleration.


**DEV-GPU-002**

## Reuse qualified solvers

The project shall evaluate maintained existing numerical libraries before implementing custom GPU solvers. Assess matrix/problem class, precision, conditioning, convergence, licensing, redistribution, supported devices and operating systems.


Verification: Record candidate comparisons and qualify the selected pinned library on representative problems.


**DEV-GPU-003**

## Explicit capabilities and selection

The project shall discover available compute devices and backend capabilities and support explicit CPU, GPU and automatic selection where acceleration is implemented. Report the selected backend and explain unsupported requests or fallback.


Verification: Test no-GPU, unsupported-device, explicit-selection and automatic-selection scenarios.


**DEV-GPU-004**

## CPU reference and recovery

The project shall retain a qualified CPU execution path for accelerated functionality and define safe fallback or explicit failure behavior for unavailable devices, out-of-memory and device errors. Prevent retries from duplicating externally visible side effects.


Verification: Compare CPU and GPU results and inject device-unavailable, allocation and execution failures.


**DEV-GPU-005**

## Numerical equivalence

The project shall qualify GPU results against independent references and the CPU path using problem-specific tolerances and residual checks. Declare precision, mixed-precision policy and reproducibility limits; do not assume bitwise CPU/GPU equivalence.


Verification: Test ill-conditioned, boundary, nonfinite and representative cases and retain error/residual reports.


**DEV-GPU-006**

## Measured benefit

The project shall measure end-to-end acceleration including transfers, initialization, compilation, synchronization and peak host/device memory. Separate cold and warm measurements and establish workload thresholds before automatic GPU selection.


Verification: Publish reproducible CPU/GPU benchmarks across representative sizes and batch counts without relying on kernel-only timing.


**DEV-GPU-007**

## Memory and interoperability

The project shall define host/device ownership, lifetimes, transfer boundaries, synchronization, cancellation and resource budgets for GPU adapters. Prefer batching and data reuse where measured to help.


Verification: Exercise repeated runs, cancellation, bounded-memory operation and adapter lifetime/error paths.


**DEV-GPU-008**

## GPU verification evidence

The project shall run hardware-backed GPU conformance tests where supported and report device, driver, runtime and library versions. Separate host coverage from device-kernel coverage and apply relevant fuzz, randomized and mutation tests without claiming unmeasured kernel coverage.


Verification: Retain hardware test and coverage reports; explicitly report missing hardware and device instrumentation limits.


**DEV-GPU-009**

## Portable acceleration boundaries

The project shall treat AVX-512, AMX and GPU execution as optional backend capabilities, not application requirements. It shall preserve a qualified portable CPU path; prohibit vendor or architecture-specific types from core/public contracts; and introduce automatic accelerated selection only after representative transfer-inclusive measurements establish explicit workload thresholds.


Verification: Build and run the common conformance corpus on a baseline CPU-only host; inspect core/public dependencies for accelerator-specific imports; compare explicit portable and accelerated selections on qualified matrices and retained circuit fixtures.


## Delivery reporting

**DEV-REPORT-001**

## Delivery evidence

The project shall report what changed, why, tests executed, coverage metrics, mutation/fuzz/random-test outcomes where applicable, and material limitations. Do not claim unexecuted checks passed.


Verification: Reconcile the delivery report with retained test and coverage artifacts.
