# Binding, parameters and resources implementation plan

Goal: execute stages 2-4 of semantic-frontend-plan.md with independent outcomes.
Retain ANTLR 4.13.1, Rust and CPU provider adapters; do not change numerical import.
User authorizes this sequence. Existing semantic plan is the design baseline.

Reuse: generated contexts, immutable BCL collections, Math and strict UTF-8 I/O.
SpiceSharpParser 3.4.1 remains an independent reference; its runtime/parser coupling
is not an adapter for our source-located immutable tree. No replacement lexer or
parser will be written. Parameter arithmetic consumes generated expression trees.
References: ngspice manual subcircuit parameters/symbol scope/include/lib sections;
ANTLR 4.13.1 C# target docs; prior pinned three-parser/SpiceSharp evaluation.
These are scoped integration semantics, not full ngspice elaboration conformance.

Artifacts: all new generated/build/coverage/mutation/temp outputs on E: using
supported MSBuild and tool switches; source worktree remains on C:.

## 1. Instance and port binding
- [ ] Add failing tests for ordered X positional ports, defaults/overrides,
  lexical/forward lookup, numeric names, arity, missing/ambiguous references,
  duplicate ports/instance IDs, recursive declarations, source preservation.
- [ ] Extract immutable instance headers from generated contexts during opt-in
  declaration export. X target is the final positional reference before named
  assignments/params marker. Complex/opaque headers are explicitly unsupported.
- [ ] Bind X formal/actual ports and bounded fixed model-family contracts.
  Q/M variable terminals must be model-aware or deferred, never guessed.
  Preserve missing/ambiguous/deferred distinctions and source locations.
- [ ] Regress, property-test, collect line/branch coverage, mutate, document
  named ngspice evidence, add Draft StrictDoc IDs, commit checkpoint.

## 2. Parameter evaluation
- [ ] Add failing dependency, suffix, scope, default/override, repeated-name,
  cycle, missing/opaque, domain/nonfinite and atomic-failure tests.
- [ ] Consume the existing generated ANTLR expression grammar, implementing
  finite numeric semantics with bounded nodes/depth/assignments. No custom lexing.
  Unsupported functions/operators/dialects get explicit outcomes.
- [ ] Return immutable effective values only on success; preserve inputs on error.
  Document precise supported dependency/default/override profile and uncertainty.
- [ ] Verify against independent analytical and named ngspice fixtures; measure
  coverage, relevant mutations/properties; update requirements, commit checkpoint.

## 3. Include/library resolution
- [ ] Add temporary filesystem tests for relative paths, selected library
  sections, provenance, denied roots, links, cycles, encodings and resource caps.
- [ ] Implement opt-in resource reader using explicit allowed roots and strict
  UTF-8; no shell/control execution, remote fetch or implicit search paths.
  Reject symlink/reparse traversal, enforce bytes/files/depth, and publish no
  partial resolved graph on failure. Preserve per-file source identities.
- [ ] Keep resource graph/source maps separate from syntax-only parsing and
  numerical execution. Report unresolved conditionals and dialects explicitly.
- [ ] Execute full managed regression, coverage, mutations, StrictDoc and named
  reference fixtures; final independent review, fix findings, commit/push.

Review focus: generated-tree ambiguities; shadowed/forward definitions; invalid
states disguised as success; resource escapes/cycles; scope/default precedence.
No historical iteration quota, new soak or CI dispatch; commits use [skip ci].

Ruling: keep the stages separate in the API and corpus evidence. Includes produce
expanded text plus exact per-file segments; binding and parameter evaluation are
explicit subsequent calls. This preserves syntax-only no-I/O behavior.
