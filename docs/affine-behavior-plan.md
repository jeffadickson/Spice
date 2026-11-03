# Affine behavioral-source implementation plan

> For agentic workers: execute inline with the executing-plans workflow. Preserve the parent simulation bridge worktree and all unrelated main changes.

**Goal:** Prepare a bounded, measured voltage/current B-source subset through generated syntax and existing typed linear runtime elements.

**Architecture:** Decompose composite B headers by reusing the generated SpiceLexer BODY mode under an explicit semantic-header flag and a generated header profile. Derive only finite affine voltage forms from the existing generated expression tree, reusing ParameterEvaluator for constants. The parent integrates prepared relations into VCVS-series or VCCS-parallel elements with explicit zero AC excitation for offsets.

**Tech stack:** Existing C#/.NET 10, ANTLR 4.13.1, immutable syntax metadata, FsCheck, existing Rust typed linear elements; no new dependency or handwritten lexer/parser.

**Spec:** This document's approved design below records the parent agent's requested generated-header entry and affine runtime reuse. Requirements remain Draft until named native comparisons pass.

## Design and boundaries

`BehavioralSourceSyntax` retains name, lexical scope, ordered output terminals,
quantity (voltage/current), expression SourceSyntax, Supported and Range.
`AffineVoltageExpressionEvaluator` prepares constant offset and at most 16 node
voltage coefficients. It admits addition/subtraction, unary signs, multiplication
by a constant and division by a nonzero constant. Constant subtrees use the
existing scalar evaluator. `v(node[,node])` retains differential sign. Conditionals,
current/time references, nonlinear products, nonconstant denominators, unknown
functions of voltages and opaque B parameters remain explicit atomic refusals.
Quoted node semantics remain unsupported until independently qualified.

`BehavioralSourceLowering.Prepare` returns a typed prepared voltage/current
relation or BridgeDiagnostics. It does not execute syntax or alter the native IR.
No generic nonlinear expression evaluator or model kernel is added.

## Tasks

- [x] Add failing metadata, affine algebra and negative tests, including physical ranges, current/voltage quantity and malformed/opaque tails; run the missing API baseline.
- [x] Add generated semantic-header flag/profile and typed extraction without changing default deck recognition. Regenerate and run syntax regression tests.
- [x] Add bounded affine derivation over generated AST with existing constant evaluation; run fixed cases plus explicit FsCheck Fact with a valid reproducible Rnd seed.
- [x] Add frontend preparation helper and send exact integration API to the parent. Preserve atomic refusals and instance environments.
- [ ] Execute original B-card DC/AC/transient comparisons against the typed existing runtime, measure coverage and run bounded meaningful mutations. Record actual corpus profile counts separately from complete-circuit uplift.
- [ ] Add StrictDoc requirements and measured evidence, review examples/docs, verify and commit coherent changes. No CI or pushes.

Bulk artifacts use a unique E: subtree. Coverage collection must not overlap a
build into the same managed output. Native LU regression is an independent
preceding change and has separate evidence.
