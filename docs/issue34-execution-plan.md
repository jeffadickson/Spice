# Issue 34 execution plan and ledger

Accepted task: execute the prioritized fixes in results issue 34. Baseline main is
`d1e46f1`; the last frozen numerical census tested `fc5db25` (928 inputs,
81 physical preparations,21 requested executions,9 agreements). Counts below
must come from new executions, not projected unlocks. Existing canonical-main
sheet-resistor work remains outside this isolated branch.

## Ordered work and acceptance

1. **Ground semantics.** Reuse generated node/binding metadata and the existing
   frontend lowering boundary. Add an explicit bounded ground policy covering
   default ngspice, PSpice formal-port exceptions and disabled automatic aliases.
   Never rewrite arbitrary source text or publish a ground alias as a floating
   root observable. Qualify root/global/subcircuit/behavioral references, ignored
   actual ports and original affected AC decks against native ngspice. Keep
   unsupported dialect/global/nesting interactions explicit.
2. **Original BJT initialization.** Replay the retained original requests with
   the current engine. Separate initial OP failure from later LU refinement
   refusal. Reuse existing public Newton continuation, state initialization and
   qualified CPU providers; preserve original analysis settings and strict
   residual checks. Actual full-waveform native agreement is the acceptance gate.
3. **Shared analysis/directive semantics.** Inventory the reached 56 prepared
   request refusals and 180 options observations. Extend bounded typed operations
   in the existing compiler, separating reporting options from numerical policy.
   No arbitrary input command execution or blanket option omission. Add named
   malformed/mixed-program regressions and measure actual corpus transitions.
4. **Semiconductor profiles.** Identify active levels/parameters rather than
   infer missing behavior from family presence. Reuse existing model adapters
   and prior reuse assessments; revisit established model/library integration
   before broader model work. Qualify each admitted subset DC/AC/transient against
   independent equations and original ngspice decks. Refuse unsupported profiles.
5. **XSPICE subsets.** Break 206 observations into named families and port/event
   contracts. Reuse generated catalogs and existing analog gain/summer lowering.
   Evaluate reusable integration before a mixed-signal kernel. Only measured
   bounded families may become supported; inventory is not implementation.
6. **Twelve disagreements.** Recheck the interpolation cohort and the two remaining
   transient discrepancies independently. Correct comparison/integration causes;
   never relax tolerances or substitute zero/missing observables to obtain passes.
7. **Resource/encoding/exclusions.** Triage 22 resource refusals,4 UTF-8 failures
   and 8 pinned exclusions. Separate intentional policy refusals, missing external
   assets, supported encodings and genuine defects; preserve source hashes and
   file-access budgets. Do not classify unavailable evidence as passing.

Each coherent fix gets StrictDoc traceability, failing-then-passing regressions,
appropriate coverage/property/mutation checks, native comparisons and a small
commit. Full frozen-corpus reruns update the chart and one results issue per run.
One fresh final branch review follows implementation. Bulk output stays in the
unique E: directory recorded in ignored `work/issue34-artifacts.txt`; no CI.

## Reuse and design rulings

- Ground handling belongs in semantic node binding, using the existing ANTLR
  catalog and frontend/client APIs. No new parser framework or runtime dependency
  is justified. Existing ngspice source is an independent specification/oracle,
  not code copied into the Rust solver.
- Native source inspected: ngspice `e8285746`, `src/frontend/inpcom.c`,
  `inp_fix_gnd_name`. Default normalization precedes elaboration; PSpice skips
  bodies whose subcircuit header names `gnd`; external `no_auto_gnd` disables it.
- Executed three native OP probes before implementation: default output 0 V,
  PSpice 1.5 V, no-auto 1.5 V for the same formal-ground divider. Raw inputs/RAW/logs
  are retained under the current E: artifact root. These finite cases do not
  establish every compatibility mode or nested-header interaction.
- Ruling: the issue authorizes implementation without repeated approval gates.
  Larger family work still needs a concrete reuse assessment before substantial
  custom model code; do not silently convert this task into a migration.

## Ledger

- Started from clean isolated `work/simulation-bridge`, branch
  `feature/issue34-common-semantics-nov3`; canonical main's seven unknown files
  remain untouched.
- Ground RED: existing four interim-refusal tests now require correct normalized
  preparation, plus an ignored-actual formal-port regression. Results retained
  in `ground-red.log`; implementation follows observed failures.
- Ground implementation now has 53 focused passing tests,42 native OP/AC checks
  across two providers and 498 passing broader frontend tests (20 prerequisite
  skips). See issue 34-progress.md for exact coverage/mutation scope and limits.
- Ruling: native square-bracket adjacency is not automatic ground normalization.
  Refuse unqualified vectors rather than changing native topology. Match PSpice's
  actual header identifier boundaries, not only formal names. Generated metadata
  and standard .NET regex suffice; no independent SPICE lexer was introduced.
- Ruling: oracle sampling work may precede broader model additions because the
  analytical sine probe reproduces the existing comparison error exactly. The
  proposed auxiliary breakpoint clock is experimental until isolation and native
  dynamic invariance are qualified; no historical comparison is waived.
- Initial-bias checkpoint exposes existing bounded recovery, retaining actual
  Newton evidence and original analysis/provider settings. Original faer BJT
  requests complete; LU refinement and native-waveform agreement remain open.
  Fresh review's changing-source/provider-accounting test gaps were addressed.
  Shared control blockers among prepared refusals remain let16,define8 and
  unsafe token9 observations; counts overlap and do not promise unlocks.
