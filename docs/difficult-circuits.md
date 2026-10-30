# Coupled continuation qualification

`tests/difficult_continuation.rs` adds a two-node resistor ladder with opposed
Shockley diode pairs at both nodes. Nested scalar bisection brackets each root
independently of the production MNA Jacobian and Newton controller. The finite
matrix is five signed drives, three impedance scales, three CPU solvers and two
continuation policies: 90 physical operating points, plus 32 Proptest cases at seed 2026103005 over drive/impedance (six solver/policy combinations each). Source current and both
nonlinear node currents are reconstructed directly; absorbed power must also balance. A negative test rejects an unfinished Gmin schedule before physical success.

The Gmin warm-start deliberately stops at 1e-4 S, large enough that retaining it
would fail the physical checks. The report's stage list contains regularized
solves; `final_unregularized` describes the separate final physical solve.

The successful policy uses 256 Newton iterations and 1e-11 A absolute tolerance.
An initial 128-iteration/1e-13 A trial exhausted 128 source-stage attempts at
scale 0.56640625 for -30 V and 10/33 ohm series resistances. Increasing a budget
and choosing a documented tolerance qualifies the successful profile; it does
not fix or conceal that stricter profile's failure, establish a roundoff floor,
or imply convergence for every valid circuit.

The paired benchmark now includes `coupled-clamp-positive` and
`coupled-clamp-negative`: ngspice OP comparisons at 27 C with explicitly matched
Shockley parameters. These compare node b within 3 uV plus relative tolerance.
They do not compare arbitrary SPICE diode defaults. Run the existing benchmark
wrapper described in [benchmarks.md](benchmarks.md); all five cases must pass
their accuracy gates before timing summaries are eligible. Raw traces remain
local; bounded evidence and coverage limitations are in
`evidence/five-area-continuation.json`.
