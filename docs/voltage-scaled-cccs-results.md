# Voltage-scaled CCCS prerequisite checkpoint — 2026-11-13

No new original deck closes here. Sol count stays five. The current frontend
still refuses unchanged general/rtlinv.cir; its stock full-program output also
contains obsolete-lvlcod and missing-print-vector diagnostics. Fresh temper-1
and temper-3 already pass and are not recounted. Temper-2's original ratio
assertion passes but one stock voltage observer fails existing bounds. A 64-step
Shockley/KCL bisection using effective request parameters agrees with our 40C
voltage 0.4052203046185694; stock default is 0.4053120303623846 (about−3.15uA
physical residual). A separate tighter-control stock diagnostic agrees within
2.8e-11 across the ten points. That diagnostic is expressly uncounted; no deck,
solver tolerance or acceptance bound was weakened to manufacture closure.

Pinned RSpice core-devices BJT intrinsic/charge.rs shows transport 1/qb and,
for forward bias, TF*IFI/qb diffusion charge with a Vbc derivative. Enabling VA
by merely updating CCCS gains would drop dynamic physics. BJT admission and
production lowering remain unchanged. Future work must resolve the piecewise
forward-charge convention, base-resistance assumptions, PNP signs, shared charge
Jacobians/history, dialect options and full original-observer qualification.

The implemented native/API prerequisite is Iout=gain*Icontrol*(1+k*Vcontrol),
with exact partials gain*(1+k*Vcontrol) and gain*Icontrol*k. It reuses branch
resolution and four-terminal stamping; branch identity is resolved once before
Newton. Controls are high impedance; no branch unknown or charge is added.
Finite signed data remain signed; nonfinite/overflow data and missing branch
controls fail explicitly. Sparse AC evaluates both partials at actual converged
bias. Independent DC audit reconstructs current without using the production
contribution/Jacobian; nonlinear AC physical audit explicitly remains not_checked.
This is neither a complete BJT nor native transient qualification.

Fresh Rust 1.99.1/MSVC debug runtime was built in a unique E target. 49 relevant
native tests pass: new product 8, resistance 8, behavioral voltage 7, switch 6,
audits 12, contracts 3 and sparse AC 5. Proptest1.9 runs 128 derivative/current cases
with seed202611131101 and128 typed-boundary fuzz cases with seed202611131102.
The latter generates node indices and signed IEEE coefficients. Other regression
seeds are recorded. No coverage-guided fuzz process ran. Two native mutants
(drop voltage partial/freeze modulation) failed5/8 and7/8 tests; a wrong .NET
wire discriminator failed its wire test. Restored regression passes 49. The
persisted seed and both minimized counterexamples are retained.

The generated .NET DTO test passes 1; full frontend regression passes 926 with 40
optional integrations skipped. Syntax source is unchanged; its prior531/1 result
is historical, not rerun here. The actual public C# client runs authored OP and
separately driven AC current/voltage controls on LU and Faer. All five native
observers agree in each of six pairs (30 comparisons), maximum error3.55e-15.
A missing-branch failure returns a structured incomplete error; a later valid
operation succeeds through the same client. The reusable repository helper is
scripts/Test-VoltageScaledCccsOracle.ps1, with an explicit opt-in Pester companion.
Two Pester integration tests pass, including preservation of existing output.
Schema validation passes six actual requests and rejects four malformed cases.
The directly runnable typed request is examples/voltage-scaled-cccs.json.

Coverage: native focused8-test scope1755/7902 owned lines across48 files,
including29/29 model lines; stable Rust branch coverage is unavailable. Focused
client scope8463/13909 lines and279/840 branches; frontend/transitive scope
21845/27740 lines and6500/10287 branches. These include generated/transitive code
and are different scopes. Pester measures131/141 commands (92.91%); JaCoCo maps
54/54 lines but coalesces multiple commands per line, including ten unexecuted
throw commands. It supplies no branch metric. Vendor C/C++, Linux and GPU remain
unmeasured. Coverage is not full model qualification.

Normal Clippy completes with the same four findings in unchanged bsim330.rs139,
configured.rs468 and expression.rs96/108; no clean strict-Clippy claim. Real
StrictDoc 0.30.4 parses 69 documents. Failed temporary helper attempts (reserved
PowerShell Error variable and wrong optional Parse argument) were corrected
before actual API/Pester/current-refusal checks; raw attempts remain in E.

Reproduce with the seven named cargo integration targets, the existing
Measure-RustIntegrationCoverage helper, Client wire test, frontend regression,
and Test-VoltageScaledCccsOracle using fresh E output, compiled managed binaries,
new engine and pinned ngspice paths. Pester requires the four documented
SPICE_CPP_ORACLE_* environment paths. Exact commands, hashes, seeds, controls,
observer bounds and raw locations are in evidence/voltage-scaled-cccs.json.
Artifacts: E:/Artifacts/Spice-sol-earlytransport-a1cd63e-20261113T1110.
No pushes, CI, merges, schematic changes or custom RAM caps were used. MOS
stock-default mismatch, RC mismatch and full BJT Early/charge behavior remain open.
