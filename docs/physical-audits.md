# Independent AC and configured transient checks

Successful manifests automatically include element-equation checks in
`consistency`, before derived signals are added. A failed check prevents a
successful result. The checks use the original circuit and complete MNA state;
they do not read assembled matrices, prepared factors or call a solver again.
This makes them useful against an assembly or provider error shared by the
solver's own residual computation, although finite tests cannot prove correctness.

Linear AC reconstructs resistor, capacitor and inductor currents and all four
controlled-source equations. Every node, including the reference, gets a KCL
check in amperes; voltage-defined branches get constraints in volts. Source
phasors replace nominal values directly. Absorbed complex power uses **peak**
phasors, `S = V * conjugate(I) / 2`; real and imaginary balances use watts and
vars respectively. Numerical bounds are explicit absolute-plus-relative sums of
physical magnitudes. Nonfinite intermediate discrepancies fail with nullable
metrics, preserving valid JSON. A nonlinear AC result does not expose its bias,
so its independent physical audit is explicitly `not_checked`.

Configured fixed-step BE uses new endpoint source values and includes its
discrete numerical dissipation: capacitor `C*Vnew*deltaV` and inductor
`L*Inew*deltaI`. These equal stored-energy change plus the BE dissipation terms;
requiring exact continuous-energy conservation would incorrectly reject BE.
Linear trapezoidal uses midpoint voltage/current, so the storage terms are
`C*Vmid*deltaV` and `L*Imid*deltaI`, with source endpoint values averaged. Energy
checks use joules, not watts. BE supports the existing constant-capacitance
Shockley diode profile. Nonlinear trapezoidal remains outside the integrator's
supported profile.

Adaptive step doubling returns the winning endpoint but does not retain its
accepted midpoint history. Applying the full coarse interval's BE/trapezoidal
identity to that endpoint would be wrong; physical interval auditing therefore
reports `not_checked` for adaptive results. `global_accuracy` is always
`not_checked`: physical balance and backward error do not bound waveform error.

The report retains the worst normalized discrepancy per identifier, with its
frequency or accepted time in the reason field, rather than retaining one audit
record per sample. Configure `RUST_LOG=circuit_simulator=debug` for existing
structured tracing diagnostics on stderr; stdout remains the JSON protocol.
`physical_audit_completed` records the manifest check count. The logging
regression parses every emitted stderr line and verifies stdout independently.

Use the existing `compat/benchmarks/rc-ac.json` and `rc-transient.json` inputs to
observe the new automatic checks. Verification and scope are in
`evidence/five-area-audits.json`: seeded numerical properties, deliberately
corrupted physical states, selected native mutations and scoped mapped-line
coverage. Unsupported models, vendor internals, GPU and production qualification
are not inferred from these results.

The three new Rust qualification properties use fixed 32-case seeds for replay. The existing overnight changing-seed loop continues to count only its property_campaign inventory; these new focused properties are not represented as changing-seed overnight cases.
