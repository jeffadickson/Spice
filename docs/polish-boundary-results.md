# Homogeneous polishing and refinement boundaries

Base `73cc342`; changes append four unit tests only. Production prefixes of
backend.rs and backend_faer.rs remain identical to frozen source. Equations,
epsilon admission, provider selection and acceptance tolerances are unchanged.

Six original survivors were replayed: **five caught, one missed**, no invalids
or timeouts. The missed203:96 dominant-tie comparison is reviewed as numerical
equivalence under the current validated finite CSC algorithm: a tied nonzero
maximum magnitude m leaves another coefficient of magnitude m. Either choice
fails other_max<=m*epsilon, so that row is never eligible for polishing. Unique
maxima and excluded zero coefficients are unaffected. Retain the raw miss;
this is not a general future-algorithm or arbitrary-input equivalence claim.

The three complex-polish kills challenge dominant selection and both changed
coefficient-times-value operators. The independent system is
x0+r(1+j)x1=0 with x1=2-j, hence x0=-r(3+j). Proptest ran64generated dyadic
gaps55..100, seed2026111931; exact state assertions precede independent all-row
verification. A tied row remains unchanged and still fails final verification.

The real correction-response kill is observable even though a later verifier
also rejects the bad state: empty/overlong finite corrections must stop after
one callback, while a valid zero correction takes the ordinary three-call
stagnation budget. The independent dense-helper kill uses finite cancelling
products: the1e100scale reports exact zero error; the1e308absolute scale must
remain unavailable as positive infinity rather than a false zero.

The isolated replay package was cleaned first; the unmutated baseline freshly
compiled the owned library and passed4focused unit tests with55filtered. This
is current-source evidence, not a rewrite or retest of all4,411frozen candidates.

Regression: **59unit+726integration passed;7ignored**. Instrumented focused4passed.
Regression/native environment seed2026111932. Focused unit coverage measures
backend.rs **26/428lines(6.07%)** and backend_faer.rs **104/526(19.77%)**. These
file denominators INCLUDE unit-test lines, including other unit tests not run
in this focused measurement. They are not comparable to integration-only
production percentages. Branch/main/vendor/GPU coverage is unmeasured here.
The actual owned library was instrumented directly, using exact Cargo-reported
vendor externs; no numeric shims or mirrored helper implementations were used.

The required actual Intel routine15tests and UHD630FP64/finite/shape/corruption
probe passed, driver31.0.101.2141, probe seed18117. Real StrictDoc parsed128
documents/670uniqueUIDs. Read-only simulated review found no important issue in
complex algebra, terminal-response behavior, overflow or the bounded tie proof.

Limits: private helper/controller scope, not new factorization, ngspice/deck,
Linux, Stryker, standalone fuzz, universal GPU, performance or convergence
qualification. Existing invalids, timeout and other survivors remain separate.
Raw: E:/Artifacts/Spice-polish-contracts-20261119T220725Z. Bounded evidence:
evidence/polish-boundary-results.json. Requirements SOLVER-POLISH-PRODUCTS-001,
SOLVER-CORRECTION-TERMINAL-001 and SOLVER-RESIDUAL-SCALE-001 remain Draft.
