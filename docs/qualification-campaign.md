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
Iterations 335–354 are complete; checkpoint evidence is in
../evidence/continuation-354-qualification.md. A new native mutation run is still
active, and LLVM mapping warnings and the tight bridge counterexample remain open.
Next: 355–374 library-based testing and mutation-driven gaps.
Checkpoint 364 completes 355–364. See ../evidence/testing-364-qualification.md;
resume at 365 for remaining generated-parser/numerical properties, overnight
library adoption and mutation follow-up before sparse AC work.

Checkpoint 374 completes 365–374 and the testing allocation. Read
../evidence/testing-374-qualification.md. Next: 375–394 direct sparse AC.
The strict bridge remains open; generated mutation survivors and compile-error
limitations are retained. The final review should examine legacy absolute_tolerance
semantics and standalone managed-mutation native-resource immutability rather than
assuming those broader contracts have been qualified by these finite runs.

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

Checkpoint 384 completes the first sparse AC tranche; see
../evidence/sparse-384-qualification.md. Resume at 385: resolve actual AC mutation
boundary gaps, widen analytical/provider cases and named external oracles.
The isolated 20-minute cargo-mutants AC run remains active on fa964ad; retain its
final summary at the next checkpoint. Fresh coverage source/test hashes match.

