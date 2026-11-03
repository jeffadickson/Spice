# Bounded MOS LEVEL=1 implementation plan

Approved scope: native Shichman-Hodges current and analytic four-terminal Jacobian; generated NMOS/PMOS explicit L/W geometry; physical body-junction depletion and gate overlap charge composed from qualified primitives. Nonzero TOX/intrinsic Meyer charge, higher levels and unimplemented parameters are explicit refusals. This is a bounded profile, not all LEVEL=1 compatibility.

Reuse existing SPICE junction Q, constant overlap C, zero-volt drain sense, nonlinear Newton, AC and accepted-step BE history. Use exact SpiceSharp 3.2.3 MIT source/defaults as equation reference, with retained attribution; add no production dependency.

- [x] Observe unknown native MOS channel tag fail before implementation.
- [x] Qualify channel current/Jacobian, reverse mode and polarity against independent equations and numerical derivatives.
- [x] Integrate native IR/compiler/Newton/AC/audit/power/observables with explicit unavailable audits.
- [x] Add strict generated binder, source defaults, geometry and body/overlap composition; reject nonzero TOX.
- [x] Compare generated DC/complex AC/nonzero-charge BE against ngspice for both polarities/providers.
- [x] Capture scoped coverage, negative/property/mutation evidence, StrictDoc and documentation; commit with open limitations.

Mutation disposition: Stryker binder 128 killed, 58 survived, 8 uncovered, 19 compiler-invalid, 2 ignored; survivors remain open. The two-mutant native attempt was stopped during its broad baseline build with no mutants evaluated, after verifying source restoration. Kernel measured 67/68 lines; stable branch maps unavailable. The direct-audit domain regression is a separate later negative fix, not retroactively included in that coverage run.
