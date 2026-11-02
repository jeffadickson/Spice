# Corpus source execution slice

Continue the approved generated-frontend execution plan. User priority is actual
corpus execution rather than mutation-score improvement. Reuse the retained
ANTLR 4.13.1 grammar/runtime, parameter evaluator, typed Client and Rust providers;
prior SpiceSharp/ngspice reuse assessments remain applicable. No new dependency,
parser migration or numerical solver is needed.

- Add a bounded generated grammar entry for DC/AC source headers over existing
  generated tokens. Keep general syntax acceptance separate and unchanged.
- Retain independent DC, AC magnitude and phase syntax with original ranges.
  Support DC assignment, signed values, DC/AC order and AC-only explicit amplitude.
  AC-only DC defaults to zero by the named source contract; waveforms stay refused.
- Selected static DC evaluates source clauses and records AC excitation omission;
  strict R/V/I lowering remains unchanged. Add a Draft StrictDoc contract.
- Check representative valid/malformed/hierarchical headers with targeted tests,
  then execute the original928-row corpus on both providers against original-source
  ngspice. Report actual upstream file gains, unresolved causes and tolerances.
- Iterate on common blockers revealed by that run, publish a results issue/chart,
  bounded evidence and verified commits. Bulk artifacts E:, main WIP preserved.

Follow-up selection: actual AC/transient execution remains the next analysis
slice; do not call this static profile full execution of input control programs.
Verification is bounded: targeted regressions, measured coverage, StrictDoc and
native comparisons; no new long mutation campaign or soak in this task.
