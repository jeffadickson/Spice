# Semiconductor junction implementation plan

Goal: qualify real nonlinear diode charge through existing native DC/AC/backward-Euler execution, then reuse the same infrastructure for BJT/MOS families. Architecture: retain Rust core/providers, immutable validated model specs, local analytic I/Q/derivatives. Source: exact MIT SpiceSharp 3.2.3 equations, attribution retained; no simulator runtime dependency. Reuse assessment: semiconductor-reuse-assessment.md.

- [x] Add independent depletion integral/diffusion derivative, continuity, malformed/domain and serialized analysis tests.
- [x] Add SpiceDiodeModel and profile with validated IS/NVT/CJO/VJ/M/FC/TT, exact Q and dQ. Explicitly refuse unsupported parameters in frontend. BV/RS/temperature need separate verified closures.
- [x] Stamp nonlinear Q differences and derivatives in backward Euler; AC uses bias dQ; preserve accepted state and static constant-C behavior.
- [x] Expose typed native and .NET element, semiconductor frontend binder, source attribution and StrictDoc requirements.
- [x] Verify representative DC/AC/transient against ngspice, derivatives/continuity/property/negative tests, scoped mutation and coverage; record exact limits.
- [ ] Extend qualified model parameters (RS explicit private-node topology, BV matching and temperature); qualify and integrate BJT and explicitly named MOS levels with independent current and charge checks. Diode-only completion does not close this parent scope.
