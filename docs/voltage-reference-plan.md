# Voltage-reference grammar continuation

Continuation of issue #16 and draft PRS-REF-001. Keep the selected ANTLR 4.13.1
framework and existing numerical code. ngspice src/frontend/parse.c protects node
spellings inside v(...) before expression parsing; its parse-bison.y retains
ordinary arithmetic operators. This is primary-source inspection, not a claim
that all ngspice expression contexts share one implementation.

Chosen approach: reuse the existing generated lexical-mode pattern. In expression
mode, a v + optional logical whitespace/continuations + opening-parenthesis token enters a
reference-name mode. Its one/two node arguments preserve punctuation and quoted
names; the closing parenthesis returns to ordinary expression mode. The parser
has a dedicated voltageReference production. Count its composite opener in the
existing 128-group guard. Feature observations still report the v call with its
original source range. Parameter evaluation remains explicitly unsupported for
signal references. No handwritten lexer, token rewrite or new dependency.

Alternatives: widening all expression identifiers would blur arithmetic and node
names; preprocessing source strings would damage exact source/provenance. The
mode approach stays in the approved generated grammar and existing boundaries.

Acceptance: common vendor V(VC+,VC-), V(REF+,REF-) and V(IN1+,IN1-) spellings;
case/spacing, numeric-looking and bus/path names; nested arithmetic and function
calls; physical continuations; quoted names where independently qualified;
malformed reference delimiters/ordinary arithmetic refusal; source ranges;
SLL/LL agreement; nesting budgets; no signal evaluation or partial catalog.

- [x] Clean baseline; new bulk outputs under E: unique artifact directory.
- [x] Red syntax regressions and named native ngspice reference probes.
- [x] Minimal generated lexer/parser fix plus metadata/nesting guard integration.
- [ ] Full regressions, seeded input/property checks, scoped Stryker boundaries,
      partial coverage with known captured-function exclusions, and named probes.
- [ ] Frozen-source full resource corpus + root syntax/legacy acceptance gate.
- [ ] StrictDoc/Sphinx, review fixes, updated results issue/chart and handoff.
- [ ] Verified small commits/pushes and main integration, preserving unrelated work.

Files: Grammar/SpiceLexer.g4, Grammar/SpiceParser.g4, DeckSyntax.cs group guard,
FeatureExtraction.cs observation, VoltageReferenceTests.cs, native qualification
file-app and bounded evidence. Syntax acceptance stays separate from model,
behavioral-source and AC/transient simulation support. Requirement remains Draft
until its bounded evidence is recorded; no full dialect/production closure.

Review finding resolved: the opener now reuses LOGICAL_SPACE for physical
continuations, including intervening comment records. Numeric LIMIT controls
remain explicitly unresolved (syntax acceptance is a separate result). The
next inferred syntax requirement is PRS-TABLE-001, not implemented here.
