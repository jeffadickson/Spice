# Dense LU zero-source correction

The differential gain oracle exposed a real numerical defect before any kernel
change: nalgebra LU returned input voltage `1.1102230246251565e-16 V` for an
independent source constrained to zero. Its CSC row had RHS zero, sum and scale
both equal to that small voltage, so the existing componentwise backward error
was exactly one. The analytical circuit output was otherwise correct. Faer
satisfied the same fixture without correction. The native regression and an
independent direct RHS sweep both failed before the fix.

`DenseLu` now reuses its existing factor for at most three same-precision
residual corrections in direct and prepared real paths. The existing DenseQr
pattern supplies the approach; nalgebra 0.34.1 supplies factor solves. There is
no new dependency, provider fallback or relaxed tolerance. A finite candidate
must not worsen componentwise error, and the final result must satisfy the
original `1e-10` policy. Uncorrectable, nonfinite and singular outcomes remain
explicit failures. The sparse verifier is unchanged.

Focused native regression passed three tests: both-provider analytical transient,
11 source RHS values per provider, and a bounded 128-case Proptest campaign
(seed `57120261103`). Three private countertests passed for wrong factors,
singular/nonfinite solves, malformed input, and exact zero-constraint rejection.
The original A-card oracle then passed 78 voltage comparisons against ngspice-47+
across three parameter/port profiles and DC/AC/ramp transient on both providers.
It includes the formerly failing nonzero input/output references.

Factor corrections are internal operations of one logical linear system solve.
The existing `integration.linear_solves` counter counts outer requested system
solves and does not expose factor-internal correction counts. Prepared factor
identity and reuse counters retain their existing meaning. There is no measured
speed claim, formal convergence proof, broad XSPICE or arbitrary-condition-number
qualification here.

Artifacts remain under `E:/Artifacts/spice/behavior-xspice-nov2`. The original
failure is in `gain-oracle-ff7e0b905b2243298f069eb76869a9c0`; the corrected
nonzero-reference native comparison is in
`gain-oracle-32e5cc65aac6406a8d17bfd7123ffb9a`. Expected output is
`2.3 - 2000*t` V, where `t` is seconds. The test source and requirement
`SOLVER-LU-REFINE-001` retain reproduction inputs and limits.

Regular native builds used the pinned Rust 1.99.1 MSVC toolchain. The E: exFAT
incremental cache emitted the expected unsupported-hard-link warning and copied
files instead. Instrumented builds use a separate target and disable incremental
caching; they do not change the production toolchain pin. Native line/branch
coverage and the bounded two-mutant correction campaign are recorded separately
when their actual runs complete.
