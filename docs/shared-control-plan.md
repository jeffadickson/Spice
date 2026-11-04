# Shared controls and grammar implementation map

Execute issue34 shared-control work using the existing ANTLR4 C# grammar,
immutable declaration catalog and typed requested-analysis compiler. No new
parser framework, command interpreter or numerical dependency is needed. The
user authorized implementation; continue inline without another approval gate.

## Design

1. Add generated semantic productions for simple-target `let`, named `define`
   functions and `unlet`. Admit these only after the last requested analysis/run,
   recording each as unexecuted postprocessing with physical ranges. Refuse
   earlier declarations, substitutions, separators, observable clobbering and
   unlet of anything except suffix-created local vectors. Keep full control
   execution distinct from ordinary analysis execution. Accept display/listing
   as reporting observations and bounded literal xbrushwidth as reporting state.
2. Enumerate the actual generated parser rule names and join a reviewed
   implementation assessment. Report syntax recognition, extraction, preparation,
   execution, requirements/evidence and limitations independently. Unknown rules
   are unassessed; a generic grammar rule never implies all constructs execute.
   Reconcile the older capability catalog's stale 'ANTLR elaboration absent'
   assessments against current bounded implementations.
3. Replay original prepared/refused controls, then run a frozen corpus census.
   Report actual transitions, newly exposed blockers and unchanged numerical
   discrepancies; update bounded evidence, burndown and one results issue.

## Test and delivery steps

- [x] Add failing regressions for suffix extraction/admission, prefix/interleaved
  refusal, root/axis clobbering, malformed functions, unknown unlet targets,
  literal reporting settings and native unchanged physical OP/current results.
- [x] Implement generated metadata and atomic bounded admission; run frontend
  and syntax suites with negative and seeded property checks.
- [x] Add grammar-map tests for missing/stale/duplicate assessments, real generated
  rules and clear unsupported/unassessed/executed distinctions. Generate JSON/HTML.
- [x] Collect coverage, scoped Stryker results, StrictDoc and Sphinx evidence;
  review and commit coherent checkpoints. Publish corpus results after execution.

Review focus: a later run after a declaration; a let target aliasing a root node;
an unlet changing existing physical vectors; a malformed body falling through the
generic control grammar; a newly added grammar rule missing from assessments.

The active two-hour soak remains pinned6411105 in its separate checkout/cache.
Canonical main's seven unrelated WIP files and raw historical evidence stay intact.
All new bulk outputs use the unique E: root in ignored work/control-artifacts.txt.
