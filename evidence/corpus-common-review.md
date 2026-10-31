# Common-cause checkpoint self-review

This is an AI self-review, not an external expert or independent review.

Checked the complete source diff, requirement/test/evidence links, current git
state, final corpus source/executable hashes and separate policy histories.

- Shared scalar parsing reuses Rust number parsing and the existing scale table.
  Only s/Hz decorations are added; expressions/unknown tails remain errors.
  Case-folded MS stays milli; MEGHz stays mega. Overflow/nonfinite cases fail.
- Endpoint alignment validates finite nonnegative time first and permits only
  four adjacent doubles outside either endpoint. Five ULPs and larger gaps fail.
  Interior interpolation and the independently declared AC bounds remain intact.
- Multi-row AC tests now distinguish nearest from farthest/lowest-frequency
  selection. Tiny positive reference axes challenge zero/negative guards without
  a distant reference sample masking missing validation. Inclusive-bound tests
  challenge comparison strictness rather than repeating the selection algorithm.
- Policy v2 starts a different history series. Scalar-only issue #4 records the
  intermediate failed comparison; issue #5 records the final finite profile.
  Rejected/unsupported/partial outcomes remain remaining work.
- A v(1)=1 V match for the driven-resistor sample does not qualify I(V1) or
  independently establish its conductance. No whole-SPICE claim follows.
- Native mutation categories remain distinct. The 378 SafeMode rollback statuses
  are unavailable evidence; exact instrumented trigger remains open. Historical
  broader mutation scopes are not closed by these focused replays. Conditional
  numerical equivalence dispositions are assumptions, not formal proofs.

No fresh Linux/native branch/full-duration soak/CI evidence was obtained. No source
push or CI dispatch. Raw native reports, logs and upstream deck bodies stay local.
The finalized counts, source hashes, replay seeds and residual mutation identities
are in corpus-common-causes.json and corpus-common-mutation-replay.json.
