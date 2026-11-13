# Coupled forward Early charge — 2026-11-13

The VAF-only, low-injection BJT composition now uses native transport and the
corresponding forward diffusion charge. It is no longer limited to an outer
selected-DC gain iteration. Both NPN and PNP use the same normalized intrinsic
BE/BC nodes, including fixed RB/RC/RE. Absent VAF retains the previous profile.
VAR, high injection, bias-dependent transit time, variable base resistance,
leakage, temperature scaling and excess phase remain outside this contract.

For positive normalized Vbe, Qbe includes TF*If*(1-Vbc/VAF). Its two partials
reach sparse AC and BE/trapezoidal Newton assembly. Reverse BE keeps the previous
unmodulated diffusion charge; depletion and reverse diffusion are unchanged.
The same evaluator reconstructs accepted/midpoint terminal currents. Independent
fixed-step audits reconstruct the coupled charge without calling that evaluator.
Nonpositive Early factors and invalid controls refuse explicitly. This does not
establish universal numerical convergence or full Gummel–Poon compatibility.

Equation guidance was inspected in pinned RSpice
29c28a022625fd7cc2c56ead1cce2d3066663074, intrinsic BJT charge.rs, and
[ngspice bjtload.c](https://raw.githubusercontent.com/ngspice/ngspice/master/src/spicelib/devices/bjt/bjtload.c).
The existing junction implementation and integration controller were reused;
no new simulator runtime or model dependency was imported.

## Sparse AC issue found during end-to-end verification

The new ordinary amplifier example initially failed Faer's unchanged 1e-10
componentwise residual gate. Actual debug traces isolated a reverse-junction
sensor current: factor roundoff was about 2.46e-20 A while its correct AC current
was about 2.37e-54 A. Increasing correction count from three to 24 did not help;
the experiment is retained and the budget was restored to three.

The provider now performs one bounded homogeneous-row correction where one
coefficient dominates all others by at least one binary64 epsilon. It computes
the dominant unknown from ALL remaining products; no tiny coefficient is dropped
and no artificial Gmin is introduced. Exact singleton zero constraints are a
special case. Every row still must pass the original residual check. Scaled
complex division avoids squaring tiny/huge divisors; unavailable optional
corrections are skipped and verification remains authoritative.

Read-only automated review found that extreme complex divisors needed this
normalization; the finding was corrected and regressions added. Review is source
inspection by another agent, not an external expert or numerical proof.

## Executed checks

- 77 focused native integration tests passed, including nine coupled-charge
  cases. Eight complex-provider unit/property tests passed; 26 unrelated library
  tests were filtered from that unit command.
- 929 frontend tests passed with 40 external-prerequisite skips. Ten targeted
  integration cases then passed with no skips: eight ngspice oracle cases cover
  both polarities/providers, storage/substrate combinations, series resistance,
  OP, complex AC and coarse/fine BE transients; two selected Early-current cases
  check independent analytical transport ratios.
- Charge properties ran 128 cases at seed 2026111307; coefficient-scale
  properties ran 64 at seed 2026111308. Minimized mutation counterexamples are
  retained in the two regression files. These are bounded properties, not a
  long coverage-guided fuzz campaign.
- Pinned cargo-mutants 27.1.0 caught the one selected zero-bias boundary mutant.
  Two charge mutants failed four/five tests; a dropped-small-coefficient mutant
  failed three provider tests. All source was restored and positive checks rerun.
  This is not broad Rust or Stryker survivor qualification.
- Real JSON Schema validation accepted one exact typed coupled companion and
  rejected six malformed variants. It does not establish circuit solvability;
  other existing element schema gaps remain separate work.

Measured native integration coverage is 2863/7454 owned lines, including 80/85
lines in spice_diode.rs and 165/307 in backend_faer.rs. Stable Rust branch metrics
are unavailable. Frontend/transitive managed coverage is 21976/27886 lines and
6521/10311 branches. Generated/referenced code is included in that managed scope;
the scopes differ. Provider unit tests were executed separately and are not
included in the named native integration coverage. Browser/MCP, Linux, GPU and
optional compact-model modules were not qualified by this change.

## Deck outcomes and remaining work

The new authored examples/bjt-early-charge.cir executes OP, AC and transient and
agrees with ngspice at existing observer bounds. It is an authored example, not a
new original corpus deck. Its public AC request also runs on LU/Faer, including
a higher reverse-bias supply case, without relaxing acceptance.

Original RCA3040 now prepares and executes all three requested analyses; its
full 101-frequency AC sweep agrees. Its DC comparison still has an original-grid
discrepancy and its transient comparison fails, with maximum observed voltage
error about 1.273 V. The generated transient uses fixed 2 ns backward Euler;
stock ngspice uses its own controller. That is an observed profile difference,
not a proven sole cause or an excuse to weaken bounds. RTL inverter still refuses
physical/directive handling (stock full output also has missing-vector errors),
and diffpair's complete requested program remains refused. None is counted.

The historical campaign total therefore remains five fully agreeing original
decks. This work completes the bounded charge slice and adds a substantial AC
analysis result; it does not invent another completed original or close the
remaining general compatibility work. StrictDoc requirements remain Draft.

Exact hashes, commands, phase-specific checks and raw paths are retained in
evidence/bjt-coupled-charge.json. Bulk output is
E:/Artifacts/Spice-bjt-coupled-8c54991-20261113. No main merge, push or CI dispatch
was performed. Windows execution only.
