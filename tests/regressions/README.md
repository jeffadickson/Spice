# Property counterexamples

Proptest persists minimized failing cases in property_campaign.txt. Retain any
new failure, its printed seed and the tested source hash before fixing it.
This directory contains no invented failing case. Replay a checkpoint with
PROPTEST_RNG_SEED=6003378856557491509 (the hexadecimal default in the source is
authoritative) or use the exact printed failing seed.
