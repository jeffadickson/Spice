<!-- Readable projection; authoritative source: fourth-slice.sdoc -->

## Fourth Reference Slice — Nonlinear Diode Operating Points

The engine now has a bounded Shockley-diode DC operating-point path. It uses analytic residuals and Jacobians with damped Newton iterations, explicit finite-domain checks, and bounded iteration counts.

It supports only DC diode operating points. Nonlinear AC bias linearization and nonlinear transient stepping are explicitly rejected until they have their own validated implementations.

The result manifest exposes Newton iteration evidence for nonlinear operating points. Typed C# requests can choose an explicit bounded Newton policy.

The C# and JSON contracts reject unknown diode fields and preserve diode current direction from p to n.

`prompt.md` is the continuation entry point. It records the implemented scope,
intentional boundaries, validation commands, and next milestones. This readable
note projects StrictDoc requirement `REF4-005`; the `.sdoc` source is authoritative.

The fixed-step transient entry point now supports diode circuits through bounded
backward-Euler Newton steps. Configured/adaptive nonlinear transient stepping,
source breakpoints, and small-signal AC bias linearization remain future work.

Opt-in DC source continuation scales independent-source excitation through
bounded stages, warm-starts each stage from the prior accepted state, and records
accepted/rejected stage evidence. It is not yet a general homotopy, Gmin, or
nonlinear-transient recovery mechanism.
