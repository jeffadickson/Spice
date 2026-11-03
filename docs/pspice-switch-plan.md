# PSpice VSWITCH bug883 vertical slice

Reuse decision: retain existing Rust Newton, sparse AC and generated ANTLR
frontend; no new solver/parser or dependency. ngspice pswitch source and bug883
provide independent equations and oracle. SpiceSharp remains an optional third
reference; no production runtime dependency is introduced.

Implement a high-impedance voltage-controlled smooth resistance. Let
`t=(Vc-VOFF)/(VON-VOFF)` clamped to[0,1], `h=t*t*(3-2*t)`, and
`log(R)=log(ROFF)+(log(RON)-log(ROFF))*h`. Current is Vout/R.
The Jacobian contains both dI/dVout and dI/dVc with the same signs in DC and
AC. No hidden state/hysteresis. Use finite RON>=1mOhm, RON<=ROFF<=1TOhm,
finite separated thresholds |VON-VOFF|>=1pV; reject out-of-profile input rather
than silently applying ngspice clamps/threshold perturbation. Require explicit
four model assignments initially. Reject unsupported model/instance parameters.
High-impedance control has no intrinsic loading; oracle uses ideal fixed control
sources, making ngspice's default1TOhm input load irrelevant to output voltage.

1. Add Rust model/public element, validate and stamp residual/Jacobian and direct
   sparse AC, update audits/power/variation refusal and unsafe analysis guards.
2. Lower bound generated S-card/model metadata through scoped parameters and ports.
3. Add bug883 two-file fixture, native comparisons and derivative/property/negative
   tests including control-input AC gain. Keep syntax acceptance separate.
4. StrictDoc, actual coverage and bounded evidence; independent review; commit/push.

Full XSPICE, hysteretic SW, temperature, noise and broad transient qualification
are outside this slice. The original control script is not executed by our
frontend; selected OP/AC probes are explicit analysis operations.
