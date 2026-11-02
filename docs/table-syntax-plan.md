# TABLE syntax and numerical reporting checkpoint

Plan: preserve ANTLR and the existing Rust engine. Implement inferred PRS-TABLE-001
using generated context-specific E/G productions; no interpolation/runtime support.
Record syntax separately from execution and independent ngspice value agreement.

1. Red regressions: selected E/G TABLE, optional equals, continuations, coordinate
   pairs; malformed arity/delimiters and ordinary TABLE identifiers. Seeded SLL/LL.
2. Native ngspice probes before implementation; minimal generated grammar change.
3. Full syntax/frontend tests, coverage, selected mutations, StrictDoc and docs.
4. Fresh numerical corpus comparison and named bridge/native regressions; raw
   artifacts on E. Preserve unsupported/rejected/partial results and provenance.
5. Full parser corpus/resource replay; stage counts, burndown, results issue,
   final source review, verified commits/push. No CI, soak or model execution.

Reporting rule: every completed gap checkpoint reruns named ngspice numerical
regressions and the corpus numerical matrix. Lead with unique files compared fully,
partially, mismatching, refused and unattempted, separate from syntax/resources.
Timing comparisons need equivalent analyses, repeated warm runs and same build
profile; a debug-engine smoke run does not substantiate performance rankings.

Ruling: reuse the established importer corpus runner as a separate measurement;
its support is distinct from the generated frontend. Store raw runs beneath its
explicit output directory (E), instead of its former fixed source/build path.

Review ruling: TABLE is contextual text before a brace, never a global token.
Two executed red regressions protect a T device named table and E control node
named table. The generated predicate reserves only the braced TABLE form.


Native evidence ruling: inpcom.c lines 6245 onward and four executed controls
show coordinate parentheses are separators. Six new red tests challenge MCP6041
wrappers/bare coordinates; generated local separator/pair productions now model
this behavior. Ordinary expressions remain strict. PRS-TABLE-002 is inferred Draft.


Completed delivery:536 tests pass,1 skip; native10 values match; syntax920/920;
resources890/920; numerical corpus3/910 selected execution(1full-profile,2partial).
See docs/table-syntax-results.md and bounded evidence/table-* for exact scopes,
coverage omissions, mutation outcomes and interrupted trial dispositions.
