# RMS stability and experiment integrity implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair representable RMS amplitude failures and qualify experiment admission/window survivors.

**Architecture:** Preserve the public measurement/result contracts. Replace only
the private RMS accumulation with standard-library stable norm operations;
keep validation, window clipping and downstream assertion behavior separate.

**Tech Stack:** Rust standard library, existing Proptest and cargo-mutants;
PowerShell orchestration, existing StrictDoc/coverage/Intel tools.

**Spec:** docs/rms-stability-design.md

## Global constraints

Keep frozen4411 source/config/helper/scores and canonical main unchanged. All
bulk outputs go to the run-specific E: directory, with isolated Cargo caches.
No general memory caps, CI or main merge. Commit/push verified branch work only.

## Tasks

- [x] Add tests/experiment_boundary_contracts.rs: inclusive capacities and
  assertion bounds, independently translated constant windows, extreme constant
  and triangular RMS data; register in tests/mutation_suite.rs.
- [x] Run focused tests on unmodified experiment.rs; retain failing amplitude
  evidence. These synthetic recorded-data tests do not claim circuit execution.
- [x] Replace private RMS calculation in src/experiment.rs per design, retaining
  stop-start normalization. Run the focused tests and negative contracts.
- [x] Add stable StrictDoc IDs for admission/window/RMS guarantees. Map the
  original RMS denominator mutant to its actual new source site explicitly.
- [x] Replay affected native candidates after owned-package clean, preserving
  actual missed/equivalent/invalid/timeout outcomes rather than forcing kills.
- [x] Run full tests, Intel routine/probe, real StrictDoc and scoped coverage;
  obtain read-only review and record numerical/tool/source limitations.
- [x] Prepare final handoff/evidence for the verified checkpoint.

Publication is the final external action: commit with skip-ci, push the
development branch, update the existing results issue and release the triage
lease. Its actual commit/remote receipt belongs in the ignored completion
bookmark and results issue, not a self-referential source commit.

Ruling: qualify two endpoint-inclusion mutations on a disposable pre-fix3523ca4
source archive, using only the unchanged triangular regression. This preserves
their old-source counterexample separately from current zero-duration equivalence.
Primary/frozen checkouts remain untouched by that reference run.

Ruling: preserve exactly constant clipped records after ordering validation.
Their RMS identity is exact and avoids new roundoff in equal-bound assertions;
the nonconstant triangle still independently verifies duration normalization.
