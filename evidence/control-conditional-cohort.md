# Terminal conditional reporting cohort

The frozen bb19799 census identified 14 prepared paths blocked at a control if.
This replay selected exactly those paths using their original SHA-256 identities.
The base was dedfb67 with the conditional source patch applied. The runner
verified the source and binary hashes during execution. The ngspice source
inventory is e8285746c73a8229cf922487c8ccfcf6bea61bce, and the reference
executable reported ngspice 47+ with KLU.

Eight additional paths executed their original requested analyses and agreed
with ngspice. Both RC AC paths include the original and its parallel mirror.

| Paths | Analysis |
| --- | --- |
| examples/measure/rc-meas-ac-control.sp and paranoia_parallel mirror | AC |
| tests/regression/lib-processing/ex1a.cir, ex1b.cir, ex2a.cir, ex3a.cir | OP |
| tests/regression/misc/bugs-2.cir | DC |
| tests/regression/parser/xpressn-2.cir | OP |

The three control_structs decks if-test-1, new-check-3, and new-check-4 and
their mirrors still refuse. Their controls exercise missing conditions,
substitution and additional expression syntax. No full 928-path census was
rerun. Agreement applies to individual physical analyses with recorded
reporting omissions. It does not qualify evaluated vector expressions,
substituted output, branch decisions or exit statuses.

Raw logs, commands, hashes, source ranges and numerical comparisons remain
under E:/Artifacts/Spice-control-if/cohort-final. Summary SHA-256 is
d91270b0fa3aad2459de8613b3363d74deac617e418a7eca93fc2b94686440e6.
Inventory SHA-256 is 495394d6ac8aab5a07420572cd5edd7d799b3167ae3ca14250094153ed15726d.

Frontend verification passed 535 tests with 36 opt-in skips. Syntax
verification passed 454 with one platform skip. StrictDoc parsed 51 documents.
Direct deletion of the terminal ordering gate was detected by the conditional
quit-before-later-OP test. Coverlet measured 93.22% lines and 84.14% branches
for RequestedControlPreparation. The generated command-extraction closure
measured 100% lines and 95.09% branches. These are scoped metrics.
