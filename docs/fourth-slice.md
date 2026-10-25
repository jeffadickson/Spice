<!-- Readable projection; authoritative source: fourth-slice.sdoc -->

## Fourth Reference Slice — Nonlinear Diode Operating Points

The engine now has a bounded Shockley-diode DC operating-point path. It uses analytic residuals and Jacobians with damped Newton iterations, explicit finite-domain checks, and bounded iteration counts.

It supports only DC diode operating points. Nonlinear AC bias linearization and nonlinear transient stepping are explicitly rejected until they have their own validated implementations.

The result manifest exposes Newton iteration evidence for nonlinear operating points. Typed C# requests can choose an explicit bounded Newton policy.
