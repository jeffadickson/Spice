# Grammar gap closure plan — issue 11

The user explicitly authorizes completing syntax recognition for valid samples
before simulation integration. Retain the selected ANTLR 4.13.1 framework and
the prior reuse assessment; do not reimplement recognition or add a simulator.

1. Implement XSPICE A-card ports from the ngspice manual and pinned frontend:
   modifiers, scalar/differential/vector ports, inversion, null connections,
   model reference. Add valid and malformed family tests first.
2. Fix shared lexical/context productions: identifiers, paths, continuations,
   parameter/expression forms, directives and remaining device families. Derive
   rules from reference syntax, not permissive accept-any-line fallbacks.
3. Rerun all 910 inventory rows and tracked repository decks with explicit,
   configurable resource limits. Adjudicate every remaining input against
   reference source/manual evidence; do not relabel grammar gaps as invalid.
4. Retain a per-input acceptance baseline and non-deck/invalid/unavailable
   adjudications. Verify valid syntax and malformed negatives independently of
   elaboration, model availability and numerical execution. No include/control
   side effects are permitted.
5. Collect coverage, seeded properties and relevant mutation evidence, parse
   StrictDoc, review changes, publish coherent commits with [skip ci], and update
   issue 11 plus the parser-specific burndown. Preserve unrelated sheet work.

Exit condition: every valid input in the declared sample inventory has actual
syntax acceptance evidence. Encoding, external fragments/scripts, malformed
fixtures and resource limits need explicit dispositions, never invented passes.
The objective remains open if valid samples still lack acceptance evidence.
