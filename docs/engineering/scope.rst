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
terminal-failure contracts cover a finite domain; wider AC corpus, factor reuse,
sparse transient and additional model profiles remain active campaign work.
Dense fallback/reference paths retain the
256-unknown bound. No GPU implementation, global convergence, full SPICE parity,
large-circuit performance or production qualification is claimed.

Consistency checks and finite oracle sweeps provide evidence within their stated
domains; they do not bound every physical/model error. Preserve failures and
unsupported outcomes rather than substituting zero or hiding provider errors.
