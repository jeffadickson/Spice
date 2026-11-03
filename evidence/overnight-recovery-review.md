# Independent recovery review

Reviewer: a separate coding agent, not an external human expert. Reviews were
read-only source, checksum and retained execution-log inspections; the reviewer
did not rerun builds or tests.

- Rust survivor slice `6f9900b`: no test-semantics defect; an important exact-byte
  provenance ambiguity was found. `b9e6693` records observed working bytes and
  canonical Git blobs separately, and normalization was independently verified.
- Windows physical-path slice `ea19c2e`: an important new preflight timeout ignored
  remaining campaign time. `e484037` shares cancellation across input, exit and
  pipe drainage, refuses expired dispatch and bounds cleanup separately. All six
  canonical source hashes matched. The finding is closed.
- Newton correction `c7761dd`: source, analytical assertions and bounded evidence
  had no important or critical finding. Wider execution subsequently exposed a
  PNP Gmin regression; the prototype's 529 passing native tests are explicitly
  insufficient qualification rather than a final success claim.
- Numerical phase guard `27a8288`: no important or critical finding. Existing
  voltage-row tolerances select the fixed merit for each correction, feasible
  systems retain legacy descent, and final convergence/budgets remain unchanged.
  All four canonical follow-up hashes matched. The old eight-correction PNP
  requirement was retained, not increased to hide the regression.
- Oracle helper `32bf2e7`: no important or critical finding; original ngspice
  input, independent divider law, typed identities and finite-unit checks are
  retained. Its original recorded producer is a prototype; the separate final
  42835c20 engine replay is necessary for a final-source claim.
- Shared launcher deadline `488e121`: no important or critical finding. Parent
  and child use the same earlier-of absolute deadline, with five seconds inside
  it for cleanup. Three canonical source hashes and the final preflight's raw
  summary/manifest hashes matched. The preflight is clearly limited to bounded
  property evidence with maintained mutations skipped.

All important findings above are closed. The final full native-suite result was
still pending at review time; its later actual result must be verified separately
and must not be inferred from these source reviews. Requirements remain Draft
where bounded evidence does not establish broader M1 qualification.
