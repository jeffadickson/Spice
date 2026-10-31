# Corpus and mutation common-cause priorities

User objective: reduce surviving mutations and identify shared corpus blockers
that impede many circuits. Baseline: 25518f3, tracked run
20261030T234800Z-d07887b8. Exact diagnostic groups and examples are in
../evidence/corpus-common-blockers.json. These are first reported blockers;
removing one does not establish support for that entire deck.

1. **Outcome and observation contracts.** Many focused survivors arose from
   missing tests of the same exit precedence, analysis insertion, observation
   terminator, and identifier validation. Add requirement-driven tables and
   malformed mixed-valid/invalid identifiers, then replay matching native
   Stryker identities. Retain compile errors separately. NGC-CORPUS-002/003.
   **Mutation-tooling blocker:** Stryker SafeMode rolls back 378 generated
   CorpusRunner mutations because an instrumented path leaves `observed`
   unassigned (CS0165). The precise mutation remains to isolate; rollback marks
   the group unavailable rather than proving each mutation invalid. This occurs
   even in the focused policy selection.
   Those are unavailable mutation evidence, not 378 product failures or kills.
   Address instrumentation/definite-assignment interaction separately without
   suppressing errors or inventing coverage. Rust also generates ten invalid
   SimResult constructor replacements in this scalar selection.
2. **Bounded scalar syntax.** Repeated `1ps`, `1NS`, `.01s`, `100kHz` failures
   can be addressed in the existing shared number parser. NGC-033 accepts only
   optional s/Hz decorations with existing scale factors; unknown tails and
   expressions remain rejected. Analytical/negative tests reproduce RED/GREEN;
   512 seeded Proptest cases challenge scale preservation. Recompare the full
   tracked corpus to measure actual transitions, not assumed passes.
3. **Endpoint representation equality.** The scalar-only rerun imported the
   original res_simple.cir but failed on 6.000000000000001e-9 versus the printed
   6.000000e-9. NGC-CORPUS-004 permits only a four-ULP endpoint alignment;
   five ULPs and larger excursions still fail. Policy v2 starts a distinct series.
4. **Analysis cards and source syntax.** 49 baseline decks stop at the
   exactly-one-tran guard; parenthesized source values recur too. Minimize
   representative decks first. Distinguish repeated analyses, unsupported
   analysis options and waveform syntax before changing lowering. A multi-
   analysis request needs explicit typed results and oracle matching; do not
   silently choose one and declare the entire deck passing.
5. **Model families.** 324 decks first stop at non-diode .model cards, plus
   49 at unsupported bounded model kinds. This is the largest reported barrier,
   but is a substantially larger numerical/model contract than scalar syntax.
   Group exact model names/kinds and minimize representative BJT/MOS/JFET cases
   before selecting a bounded model or separately qualified OSDI adapter.
   Do not drop model cards or substitute an ideal device to increase pass counts.
6. **Other grammar and input diagnostics.** Repeated first-element shape,
   expressions, hierarchy, continuation and encoding errors need independent
   reproducers. Some are unsupported valid ngspice, others malformed decks or
   auxiliary assets. Avoid assigning one generic parser fix to unrelated cases.

Items 1–3 are this checkpoint's bounded implementation, excluding the recorded
mutation-tooling blocker. Items 4–6
remain prioritized engineering work, not implemented or verified closure.
No source push, CI dispatch, new soak, full SPICE, GPU or production claim.
Reproduction commands, source hashes, actual transitions, mutation dispositions
and coverage limits are recorded in ../evidence/corpus-common-causes.json.

Qualification depth remains a separate priority: the corpus profile compares node
voltages, not every original .print observable. For voltage-driven res_simple.cir,
matching v(1)=1 V alone does not independently qualify its requested I(V1) or the
resistor's conductance. Add named current oracles before claiming that coverage.
