# Resource policy continuation — 2026-11-01

Continuation of issue #15 and the generated-frontend plan. Preserve ANTLR,
existing Syntax/Frontend/Client boundaries and the Rust solver. No new parser,
lexer, expression engine or dependency is needed. Current ngspice frontend
`src/frontend/inp.c` handles `.csparam` by creating control vectors after input
parameter processing; it is not a conditional include directive.

Approaches: raising global defaults hides caller policy and increases ordinary
interactive allocation; adding another resource scanner duplicates grammar work.
Chosen bounded fix: retain defaults, propagate a caller-supplied immutable
`ParserLimits` through resource parsing and expanded parsing, and stop treating
`.csparam` alone as conditional-resource control. Real conditionals and all
confinement/encoding/file/byte/depth policies remain conservative.

Acceptance: selected limits apply to root and included fragments; insufficient
limits refuse atomically; defaults unchanged; `.csparam` plus include resolves
without evaluating/exporting it; real conditional includes still refuse even
beside `.csparam`; semantic evaluation remains deferred where appropriate.
Qualify changed corpus outcomes with source/resource hashes and same inventory.

- [x] Baseline Syntax suite in isolated linked worktree; new raw artifacts E:.
- [x] Add failing budget propagation and export-versus-conditional tests.
- [x] Implement ResourcePolicy.ParsingLimits and corpus propagation, minimal guard fix.
- [x] Run Syntax/Frontend regressions, malformed/property tests and Stryker resource slice.
- [x] Run full resource corpus + legacy acceptance gate and update results issue/chart.
- [x] Parse StrictDoc and build Sphinx; review evidence including incomplete coverage.
- [x] Commit and push coherent checkpoints, preserving main's unrelated work.

Files: ResourceResolver.cs policy/use; Simulator.Syntax.Corpus/Program.cs explicit
caller limits/report; ResourceResolutionTests.cs boundary/source-map invariants;
docs/semantic-elaboration.sdoc draft requirements; bounded evidence and handoff.
Captured-function coverage remains unavailable under the current collector; no
complete denominator or full SPICE/model/AC/transient qualification is claimed.

Review extension: cap exported grammar-gap messages at512 characters, preserve
unknown budget line0, and verify CR/CRLF offsets, prefix budgets, three-message
limits, nested conditionals and immediate post-.end cards. Targeted mutation
follow-up is still running; checked regression step does not assert its outcome.

Completed results: docs/resource-policy-results.md; issue #16. The new
PRS-REF-001 requirement captures the next inferred vendor grammar gap and
remains unimplemented. No closure-inclusive coverage or full-SPICE claim.
