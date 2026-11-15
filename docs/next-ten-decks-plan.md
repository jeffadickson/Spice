# Next ten original decks: bounded execution plan

Goal: ten additional previously non-agreeing originals, with unmodified inputs and stock ngspice references. Exclude the preceding batch and any current-baseline successes. Keep numerical/oracle uncertainty visible.

The first screen found two dynamic-function preparation failures, four diode numerical mismatches, and four already-agreeing candidates. Replace successes; adjudicate diode discrepancies independently before choosing a code fix. Source/analysis boundaries remain unchanged unless separately qualified.

Design for dynamic scalar voltage functions: extend the existing generated-AST single-control compiler. Bind formal expression nodes in immutable lexical frames, parse function bodies with the same ANTLR expression grammar, preserve physical argument/body ranges, and reuse existing bounded expression/native voltage stamps. No textual substitution, alternate lexer, new dependency or native runtime migration. Keep one effective differential-voltage input and existing OP/DC scope. Bound total visits, nesting and active calls; validate the emitted native budget. Recursion, arity, duplicate/ambiguous functions and unsupported models fail atomically.

- [ ] Reproduce original failures and retain stock defaults; replace already-agreeing selections.
- [ ] Add failing dynamic-function tests; implement bounded compiler frames; verify six function/hierarchy original DC programs.
- [ ] Find and fix remaining shared blockers until ten current-baseline failures are independently qualified; never fabricate the count.
- [ ] Verify LU/Faer plus appropriate Intel original decks and mandatory circuit/probe gates. Retain RSpice third checks and exact hashes.
- [ ] Run relevant regression/property/malformed/mutation checks, actual line/branch coverage and StrictDoc; obtain one read-only final review.
- [ ] Record bounded evidence, updated burndown, one results issue; commit/push verified development checkpoints while preserving canonical main and all unrelated work.

All bulk artifacts: unique E: run recorded in ignored work/next-ten-artifact-path.txt. Existing source/tool pins and numerical bounds apply. No new soak or CI. The budget counter is a possible small formal-proof candidate, but proving a counter alone would not prove ANTLR/native model correctness; executable boundary/property checks remain the acceptance evidence here.

## Verified checkpoint
All ten previously refused originals execute on LU/Faer (350 DC points each). Four agree with stock-default ngspice; six disagree and remain open. Exact polynomial checks pass for all ten, and tightened ngspice diagnostic copies agree. RSpice repeats the same six default-reference disagreements. See dynamic-bilinear-results.md and evidence/dynamic-bilinear-decks.json. No ten-pass claim.
