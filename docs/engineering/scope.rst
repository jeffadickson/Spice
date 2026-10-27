Numerical and provider scope
============================

The current linear model uses SI resistor, capacitor, inductor and independent
source values. A bounded Shockley diode supports analytic current/Jacobian
evaluation, damped Newton DC, bias-linearized AC and backward-Euler dynamics.
Other model families and broader integration policies require separate contracts.

Source continuation returns nominal excitation. Staged Gmin removes its shunt
before returning the physical solution; explicitly requested fixed Gmin remains
reported. Finite norms, computed tolerance scales, provider correction dimensions,
terminal error classes and representable recovery progress are checked.

Faer 0.24.4 and nalgebra 0.34.1 remain the CPU dependencies. Direct sparse DC
assembly is implemented. AC now assembles complex CSC directly from G and Q,
including diode incremental conductance after bounded DC bias. Faer accepts a
genuine complex RHS with one factorization per frequency; independent sparse
backward-error checks reject bad provider results. Analytical RC generation and
terminal-failure contracts cover a finite domain; named ngspice resistor/lowpass
and original RLC/VCCS comparisons add finite external evidence.
Prepared complex factor reuse remains unimplemented.
Configured linear BE/trapezoidal and legacy linear BE now assemble CSC directly.
Faer prepared real factors own canonical numerical identity; configured stepping
retains two exact-step systems and verifies each returned vector independently.
An exact binary RC example requires one factor and three reuses. Adaptive and PWL
finite cases exercise rejected-history isolation and changing RHS values.
Named RC ramp and signed RLC decay comparisons against ngspice add finite transient evidence with explicit interpolation/print limits. Exact empty numeric rows/columns fail before faer factorization; this is not a general rank test. Wider model profiles remain active campaign work.
Dense fallback/reference paths retain the
256-unknown bound. No GPU implementation, global convergence, full SPICE parity,
large-circuit performance or production qualification is claimed.

Consistency checks and finite oracle sweeps provide evidence within their stated
domains; they do not bound every physical/model error. Preserve failures and
unsupported outcomes rather than substituting zero or hiding provider errors.
