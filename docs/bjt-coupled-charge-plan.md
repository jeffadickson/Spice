# Coupled BJT forward Early charge

Continue the selected Rust model architecture and existing reuse assessment in
semiconductor-reuse-assessment.md. The pinned RSpice intrinsic charge evaluator
and ngspice bjtload.c are equation references, not runtime dependencies. Reusing
the existing junction storage and committed-history controller is preferable to
a second BJT-specific integrator or a transport-only approximation.

For the bounded VAF-only profile (no VAR, IKF/IKR, XTF or excess phase), forward
transport is multiplied by A=1-Vbc/VAF. Forward diffusion charge at Vbe>0 is
TF*If*A; its two partials are TF*Gf*A and -TF*If/VAF. At Vbe<=0 the existing
unmodulated diffusion law applies. Depletion and reverse diffusion are unchanged.
Use normalized oriented junctions for both NPN and PNP. Keep series resistors
at the same intrinsic nodes used by conduction and charge.

Extend charge-only companions with optional immutable control metadata. The
same charge evaluator must reach sparse AC, BE/trapezoidal Newton, accepted and
midpoint observable histories. Independent audits must evaluate the coupled
charge from terminal values. No history is mutated during model evaluation.
Reject nonpositive Early factors and invalid/missing control nodes; do not clamp.
General high-injection Gummel–Poon remains outside this VAF-only slice.

Verify derivatives, common-mode/terminal aliasing, control-only AC, PNP, series
resistance, transient refinement, malformed wire input and rejected-step history;
then compare original decks with all requested analyses against stock ngspice.
No additional original is counted from authored fixtures or partial execution.
