# Ordered follow-up self-review

This is an agent self-review, not an external expert review.

- **Rollback:** current native Stryker catches all three selected observation
  catch mutations without CorpusRunner compiler rollback. Other generated
  CompileErrors remain separate; this does not qualify numerical state rollback.
- **Currents:** V/L/E/H selection matches existing typed branch signals. Exact
  branch aliases cannot accept unrelated device columns. RL impedance checks
  both phasor components; E/H cases check p-to-n polarity. Thirty selected
  observation/alias mutations were killed. Other terminal currents remain open.
- **Analysis syntax:** optional zero start and TMAX are validated explicitly.
  Repeated cards and AC+TRAN still fail. OP plus another selected analysis is
  partial corpus coverage. The stop-field mutant exposed accidental equality
  with a default; independent stop variation now catches it. The original
  four-killed/one-survivor report and two-killed replay remain separate records.
- **UIC classification:** valid ngspice initialization syntax used to reach the
  number parser. Final CLI/regression checks require Unsupported, not InvalidInput.
  No UIC initialization behavior was implemented. The final branch was not
  included in the earlier native mutation snapshot; this limitation is explicit.
- **Models:** the new R model resolves one finite positive resistance. No model
  is dropped to increase passes, no default missing engineering value is invented,
  and unknown physics fails. Cross-family name uniqueness is a stricter bounded
  contract; its rejection is not proof that an upstream deck is invalid ngspice.
- **Inventory:** declarations are counted per family/deck, with title/comments
  excluded, duplicate declarations deduplicated and unreadable files explicit.
  Family counts overlap; they are neither instance counts nor compatibility wins.
  The helper uses OS-appropriate path comparison; Linux execution is unavailable.

Verification: 440 Rust regression passes and one ignored before the final UIC
classification branch; seven final parser tests after that correction; 95 managed
passes with real tools, final clippy and real StrictDoc/Sphinx. Focused line/branch
coverage reports retain their scope and hashes. Rust branch metrics, fresh Linux,
coverage-guided fuzzing, a new soak, full SPICE and production qualification remain
unavailable. Seeded properties and malformed-input cases ran; no formal proof is
claimed. Raw build/mutation/oracle artifacts remain local.

Next model work should minimize NMOS/PMOS/NPN examples and separate LEVEL and
parameter demands before choosing a semiconductor model contract. The declaration
inventory shows NMOS in 91 decks, PMOS in 82, NPN in 77 and VDMOS in 50, with overlap.
Geometry-dependent R models and multi-analysis execution remain separate gaps.
