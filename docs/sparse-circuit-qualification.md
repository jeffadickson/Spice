# Sparse circuit qualification

`tests/sparse_qualification.rs` checks RC ladders of one through eight stages at
three scales and four frequencies, using a load-to-input continued-fraction
impedance oracle. A 32-case Proptest campaign (seed 2026103004) varies stage count
and scale continuously. A separate series-RLC impedance check verifies voltage,
source-current and signed inductor-current phasors, including near resonance.
The test provider's dense entry point panics, making silent dense fallback visible.

Fixed BE and trapezoidal trajectories agree with the dense reference. Exact
binary steps of 1/1024 s use one prepared factor; a PWL breakpoint at 1.5 steps
introduces a second numeric factor. Accepted intervals independently determine
the expected solve count when the initial state is explicit. The test does not
equate an arbitrary floating-point step with a nearby cache identity.

Fault injection includes a provider returning a stale factor while falsely
claiming the new matrix identity. The owning residual check must reject its
output. Other tests reject short, nonfinite, wrong finite and explicitly failed
complex results and rank-deficient parallel ideal sources. No fallback may hide
these errors. These checks qualify owned dispatch/verification behavior, not
every behavior of the numerical vendor or a new complex factor cache.

Native mutation outcomes, replay paths and scoped coverage limitations are in
`evidence/five-area-sparse.json`. The GNU regression compiler and nightly MSVC
coverage compiler differ; focused mapped-line figures must not be compared with
historical full-suite percentages. Qualified branch coverage remains unavailable.
