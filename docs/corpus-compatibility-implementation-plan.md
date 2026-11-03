# Corpus compatibility implementation plan

Goal: execute substantially more original circuits and their requested analyses
with independent numerical evidence. The approved scope is all five areas below;
finishing one bounded slice does not close the whole semiconductor/XSPICE scope.
Architecture: preserve the generated ANTLR frontend, typed immutable public API,
Rust numerical core and qualified CPU providers. Reuse existing stamps/models and
maintained libraries after concrete license/behavior assessment; no production
legacy runtime or silent SpiceSharp migration. PowerShell/C#/F# orchestration,
StrictDoc, bounded parallel builds, E: bulk outputs and no CI dispatch.

## 1. Common preparation semantics
- [ ] Expose existing F/H stamps via typed generated control references, resolving
  forward names within each instance; reject nonbranch/missing controllers atomically.
- [ ] Bind active .global declarations across frames. Verify precedence and unused
  declarations against ngspice before deciding alias semantics.
- [ ] Inspect actual refused expressions/source forms, passive variants and30
  resource failures. Preserve main's unrelated sheet-resistor WIP; don't recreate it.
- [ ] Red/green negatives, randomized hierarchy/sign tests, native DC/AC/transient
  checks and fresh original928 inventory execution. Record blocker overlap.

## 2. Semiconductor contracts
- [ ] Record reuse assessment and representative executable POC. Thevenin kernels
  compile but license notice gaps and charge derivative omissions prevent automatic
  production adoption; inspect exact MIT SpiceSharp source for equation-port reuse.
- [ ] Implement genuine nonlinear diode depletion/diffusion Q, bias-dependent AC C,
  BE accepted-charge differences and rollback, with explicit parameter contracts.
- [ ] Add BJT and explicitly named MOS levels with DC, AC derivatives and real
  transient charge/state semantics. Do not count a zero-charge toy as broad parity.
- [ ] Independent derivatives/charge integrals, native original model cards and
  requested-analysis comparisons. Unsupported parameters remain named refusals.

## 3. Requested analyses
- [ ] Prepare ordered original .op/.dc/.ac/.tran with preserved arguments, grids,
  units, source locations and repeated-analysis IDs; execute via public client.
- [ ] Match requested start/stop/step/defaults and initial conditions explicitly.
  Fresh OP per DC point is an initial named strategy, not hysteresis parity.
- [ ] Record native grid anomalies as discrepancies, not silent substitutions.
- [ ] Extend census to separate parses, prepares, requested execution and agreement.

## 4. Behavioral/XSPICE subsets
- [ ] Generated typed voltage ports and genuine affine gain model: reuse VCVS and
  an offset source, with zero AC excitation for a constant synthetic bias.
- [ ] Extend measured analog/behavioral subsets through existing generated AST;
  no ad hoc token scanner, ignored model parameters or invented digital dynamics.
- [ ] Native original A/B cards and negative port/model tests; explicit unsupported
  nonlinear/digital/stateful families remain tracked, not marked implemented.

## 5. Convergence qualification
- [ ] Challenge newly admitted nonlinear circuits with direct/source/Gmin recovery,
  scaled residuals, final removal of artificial shunts and transactional rollback.
- [ ] Compare difficult circuits to native references, retain replay inputs/seeds,
  check timestep refinement and distinguish model validity from convergence.
- [ ] Keep source-hash provenance, scoped coverage, relevant mutations and stable
  StrictDoc IDs. Commit/push coherent verified checkpoints and one results issue
  per logical corpus campaign; update the compatibility burndown.

Execution uses separate worktrees for independent requested-analysis and model
work, with integration/review gates at shared interfaces. The fixed8ecc0ab
11pm soak remains pinned until a later source is independently verified; it does
not qualify new work merely because main advances. Raw artifacts remain local.
