# Qualification implementation campaign — 2026-10-26

User-authorized scope: continuation hardening, mutation-driven test gaps, direct
sparse AC, direct sparse transient, and bounded model expansion. Build and verify
after every ten substantive iterations; push each completed checkpoint to GitHub.
Perform a deep final review, implement accepted findings and push the final fixes.
No GitHub Actions dispatch is authorized by this plan.

The request contains an allocation mismatch: five areas times ten is 50, while
the requested final hundred plus review implies 100 + 1. A clarification prompt is open. Working assumption follows the user's final hundred-plus-review target: 20 iterations per area, 100 implementation/qualification iterations plus one final review/fix iteration. This is an explicit interpretation, not a claimed user reply. Adjust if corrected. Do not invent iteration counts or treat review as external experts.

Repository base: 1b8885e (iteration 334). Campaign steps 1–10 correspond to repository
iterations 335–344; the first full build/checkpoint is 344. Further checkpoints
advance by ten. The active M1 objective and all previously documented limitations
remain in force. Per-step focused tests do not replace checkpoint verification.

Initial block (convergence): finite norm handling; nested policy validation;
provider correction contracts; unsupported-policy rejection; finite BE inputs;
thermal-domain damping; multi-diode chain evidence; bridge-rectifier evidence;
continuation stage/work diagnostics; mutation/property/coverage checkpoint.
Iterations 335–344 are complete; checkpoint evidence is in
../evidence/continuation-344-qualification.md. Nine generated mutation survivors
and the tight bridge counterexample remain open. Next: 345–354 continuation work.

Coverage is measured for the completed checkpoint source, with line/branch tooling
limits reported. Failures and minimized counterexamples remain evidence. Use the
existing pinned faer/nalgebra providers; external source/model references are
recorded in checkpoint evidence. Preserve StrictDoc and existing engineering docs;
any wider Sphinx migration needs a recorded decision rather than replacing them
implicitly during numerical changes.

Library/reference decision for this campaign: reuse pinned faer 0.24.4 for sparse
real/complex factorization and the existing nalgebra dense providers for independent
bounded comparisons. This preserves adapter boundaries and avoids a new native ABI
and platform-packaging dependency while those existing CPU contracts are qualified.
Primary references reviewed on 2026-10-26: https://docs.rs/faer/0.24.4/faer/ and the
ngspice development manual https://ngspice.sourceforge.io/docs/ngspice-manual.pdf
(version 47+, September 28, 2026). The manual is an equation/convergence reference;
its development version is not a claim about the installed ngspice oracle version.

Working allocation: continuation 335–354; test gaps 355–374; sparse AC 375–394; sparse transient 395–414; bounded models 415–434; final deep review/fixes 435. Checkpoints/pushes: 344, 354, 364, 374, 384, 394, 404, 414, 424 and 434, then final review push.

