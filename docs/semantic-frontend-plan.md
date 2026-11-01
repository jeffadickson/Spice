# Semantic frontend delivery plan

User direction: continue from the qualified parser and corpus capability inventory. Retain ANTLR, Rust and drop-in solvers. This plan delivers successive verified slices; completing the catalog does not complete elaboration.

## Reuse decision and boundaries

Reuse the selected generated ANTLR tree and BCL immutable collections/dictionaries. Existing SpiceSharp 3.2.3 / SpiceSharp-Parser 3.4.1 executable assessment, ngspice e8285746 source inspection and three-reference parser assessment remain the reuse baseline (docs/spicesharp-evaluation.md and parser-only-plan.md). They do not provide an independently qualified source-located IR adapter for the retained Rust core. No new parser, expression evaluator or simulator dependency is added for the catalog. Custom work is domain representation and scoped lookup, not a replacement parsing engine. Before expression evaluation/model equations, repeat the proportionate reuse check for that specific contract.

Pinned ngspice primary source: src/frontend/subckt.c (nested subcircuits, model-name translation and expansion). Its implementation shows model-aware terminal interpretation and expansion; observing generic syntax is not sufficient to choose all instance references. No full ngspice scope/default/duplicate-definition compatibility is inferred from source inspection.

## Sequence and exit gates

1. **Declaration catalog (current slice).** Export immutable lexical scopes, model/subcircuit declarations and ordered parameter syntax directly from generated contexts, with physical source ranges. Provide case-insensitive local/ancestor lookup, forward declarations, shadowing, explicit ambiguity and deferred external-resource outcomes. Parameter values remain syntax. Gate: independent fixtures, malformed/resource rejection, seeded hierarchy/renaming properties, coverage, scoped mutation, StrictDoc and a bounded corpus catalog run. No instance binding/flattening claim.
2. **Instance binding.** Specify explicit typed headers for X calls and model-aware device references; preserve terminals/assignments/source ranges. Gate: named ngspice reference fixtures for port mapping, model references, arity, missing/ambiguous definitions and recursion. Do not guess model references from arbitrary text tails.
3. **Parameter elaboration.** Define expression IR, SPICE suffix rules, ordered/default/override policies, scope and cycle limits. Gate: pinned independent references and valid/invalid dependency graphs, including M versus MEG, overrides and rollback on failure. General functions/quoted dialects remain explicit capabilities.
4. **External resources.** Define permitted roots, encoding, include/library section semantics, cycles, byte/depth/file-count budgets and source provenance before opening files. Gate: temporary isolated filesystem fixtures, denied roots, missing files, cycles and source-map checks on supported platforms.
5. **Core bridge.** Translate the supported elaborated subset into existing typed Rust requests. Gate: text-versus-structured equivalence plus ngspice voltages/currents/AC/transient comparisons with declared bounds. Fail capability checks before numerical execution.
6. **Selected model profiles.** Minimize family/LEVEL/assigned-parameter profiles and evaluate reuse/adapters before numerical work. Gate each explicit model/analysis combination independently; no family-wide qualification from one passing circuit.

Stages stay separate in reports: syntax accepted, declarations catalogued, references bound, parameters elaborated, circuit supported, solve completed, oracle matched. Each has its own denominator and failure/deferred outcomes. Counts overlap and do not predict unlocked simulations.

## Current catalog contract

Names use ordinal case-insensitive comparison. Model and subcircuit declarations live in their lexical declaration scope; a subcircuit body owns a child scope. Forward lookup uses all declarations in each scope. Nearest scope shadows ancestors; same-scope duplicates are ambiguous, with both locations retained, not silently first/last selected. This is an explicit conservative catalog policy, not an assertion that ngspice rejects every duplicate.

Parameter assignments remain ordered source syntax; repeated parameter assignments are retained, not arbitrarily deduplicated or evaluated. Model parameters and subcircuit header defaults remain attached to their declarations. Include/library declarations are recorded without I/O; an unresolved name is deferred when external resources may provide it. Quoted/control bodies and function headers are not semantically interpreted. Catalog completion never means an executable circuit.

## Tasks

- [x] Add failing catalog/scoped-lookup tests, including nesting, shadows, forward declarations, duplicate ambiguity, ordered parameters, external deferral, source ranges, opaque commands and rejected input.
- [x] Implement opt-in declaration export during the existing generated-tree traversal and pure bounded lookup. Keep ordinary syntax-only behavior unchanged.
- [x] Add an isolated corpus catalog profile with source/input/binary hashes and honest status counts; retain raw per-input data on E:.
- [x] Add stable StrictDoc requirements, execute regression/property/negative/coverage/mutation checks, review, commit and push with CI undispatched. Preserve unrelated sheet-resistor work.

Review: grouped duplicate-heavy self-checks replace repeated candidate scans. Conditional deferral is explicitly documented as globally conservative. Catalog gate checks payloads/self-lookups and impossible count combinations. Initial test-oracle corrections used structural immutable-array comparison and explicit null-root-parent handling; these are retained as test-development failures, not production defect claims.

Current slice evidence: all 916 required inputs catalogued; 268 tests and nine gate fixtures pass. Exact scopes, seeds, coverage and unresolved mutation outcomes are retained in evidence/declaration-catalog-verification.json. Stage 1 is implemented within that profile. Stages 2-4 now have bounded implemented profiles and separate evidence in docs/semantic-frontend.md and semantic-frontend-results.md; their Draft requirements and broad conformance remain open. Stages 5-6 are not implemented or implied by these checkpoints.
