# Generated frontend to Rust operating-point bridge

Continuation of approved semantic-frontend-plan stages4-5. Retain ANTLR and Rust;
reuse public Simulator.Client requests/transport and existing CPU providers.
The custom work is generated-tree adaptation and bounded hierarchy lowering,
not a parser, solver or new model implementation. Prior SpiceSharp/ngspice
assessments remain the reuse baseline; no new package is selected.

Architecture: Syntax exports immutable primitive/scoped-card metadata and a
finite scalar evaluator. New Simulator.Frontend depends on Syntax and Client;
neither dependency points back. Flatten R/V/I constant-DC profiles into typed
operating-point requests. Resource loading remains explicit and syntax-only
parsing remains I/O free. Includes/library sections and X overrides precede
flattening. Unsupported active statements fail before any numerical execution.

Acceptance: analytical and ngspice hierarchical divider/signed-source cases,
text-versus-structured engine equivalence, instance-private node isolation,
source provenance, recursion/depth/node/element caps, duplicate IDs, invalid or
unknown values, unsupported controls/devices/analyses, and atomic failure.
Root/global0 uses reference index0. All other nodes/element IDs use injective
frame/name encoding; root-node lookup is returned to callers. No ground aliases,
.global, model lowering, behavioral sources, AC or transient bridge claim.

- [x] Add failing primitive/scalar/bridge tests; run missing-API baseline.
- [x] Implement generated metadata and pure R/V/I operating-point lowering.
- [x] Execute typed Rust requests and independent named ngspice comparisons.
- [x] Add resource-backed corpus rebinding as a distinct opt-in profile; report
      resource failures separately from syntax/binding/parameter/support.
- [x] Collect scoped coverage, library mutation/property/negative evidence,
      StrictDoc and docs; fresh review, fixes, small commits and normal push.

All new artifacts on E:, no CI or endurance soak; preserve main's unrelated work.

Ruling: the first bridge requires a globally definite catalog. Existing global
resource/conditional deferral remains conservative even in unused definitions;
only inactive non-declaration device bodies are ignored. Reachability-aware
conditional/declaration narrowing remains a later qualification profile.

Evidence: docs/simulation-bridge-verification.md and issue #15. Final verification
publication follows the implemented checkpoint; all requirements remain Draft.
