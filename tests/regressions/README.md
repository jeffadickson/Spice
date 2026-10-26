# Property counterexamples

Proptest persists minimized failing cases in property_campaign.txt. Retain any
new failure, its printed seed and the tested source hash before fixing it.
This directory contains no invented failing case. Replay a checkpoint with
PROPTEST_RNG_SEED=6003378856557491509 (the hexadecimal default in the source is
authoritative) or use the exact printed failing seed.

The persisted divider case exposed dense-QR backward-error rejection and now
passes bounded refinement. The reverse-bias derivative case exposed amplification
of rounding in the finite-difference oracle; its explicit roundoff allowance is
separate from production solver tolerances. The near-zero diode cancellation
failure was already reproduced by a persisted seed; its actual minimized input
is retained in evidence/counterexamples/diode-near-zero.json.
