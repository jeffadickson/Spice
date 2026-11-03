# Runtime readiness and controlled-source vertical slice

Approved outcome: audit syntax-to-runtime information, report multiple known
blockers per circuit, and complete a circuit family through native comparison.
Use the existing ANTLR token/tree machinery and existing Rust VCVS/VCCS models;
no parser rewrite, solver reimplementation or new dependency is needed. Existing
SpiceSharp/ngspice reuse assessments remain applicable to the retained Rust choice.

1. Collect active structural capability/binding issues without first-error bailout.
   Report graph incompleteness and numerical issues not exhaustively evaluated.
   Grammar acceptance is never an execution capability claim.
2. Give linear E/G sources typed output/control terminals and scoped gain fields,
   then map them to existing Client/Rust Vcvs/Vccs wire elements. Keep strict
   legacy Lower unchanged, refuse TABLE/POLY/behavioral variants explicitly.
3. Check gain polarity, hierarchy, DC and complex AC against analytical/native
   fixtures, then rerun the original928-row corpus and selected AC probes.
4. Add Draft StrictDoc requirements, bounded coverage/regressions and a corpus
   issue/chart. Preserve unrelated main WIP. All bulk artifacts on E:.

Runtime implementation is the priority. No additional long mutation/soak
campaign is launched for the offered3–8p.m. window. Small targeted checks and
native comparisons accompany the feature; broad model qualification stays open.

## Checkpoint status

E/G typed handoff and bounded readiness reporting are implemented. Owned native
DC fixtures compare both CPU providers against ngspice, including negative gain,
negative transconductance and hierarchical parameter binding. This checkpoint does
not close the full original928-row audit or complex AC control-source qualification.
The previous35 DC solves/34 native matches/7 selected AC files remain the last
completed corpus totals. Bug883 VSWITCH and a recurring SpiceSharp third-reference
adapter are still pending. No new soak was started.

Static independent review found no blocking defect. Follow-ups: include instance
paths in model observations, add dedicated observation/graph-limit boundary tests,
and extend structural diagnostics beyond the first duplicate-element failure.
Numeric validation remains nonexhaustive. No new mutation or long fuzz campaign
was run; negative profiles and existing randomized/property regression tests run
with the managed suites. Sphinx was not regenerated for this bounded checkpoint.
Evidence and package-level coverage scope: evidence/runtime-readiness-checkpoint.json.
