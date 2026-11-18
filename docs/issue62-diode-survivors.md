# Issue62: nongrounded diode-current mutation findings

Base `66d7f74`, development branch `fix/deck-blocker-implementation`.
Both production expressions already subtract Vn from Vp. No numerical code,
model or convergence tolerance changed. Four new analytic/property checks and
one explicit ngspice test strengthen qualification of the public observations.

The old-main api.rs:327:21/352:21 survivors correspond to current351:21/388:21.
Exact original/current native diffs are hashed in
`evidence/issue62-diode-survivors.json`. Final maintained cargo-mutants27.1.0
replay:2 candidates,2 caught,zero survivor/timeout/invalid/unattempted. The
existing development AC test also catches the AC mutation; this is not a claim
that both were previously uncovered on the development branch.

## Independent expectations

Directly forced terminals vary common mode across -0.4,+0.2,+1V and differential
bias across reverse/forward values. Tests independently evaluate Shockley
current,constant-depletion charge plus transit storage,raw backward-Euler
zero-charge history,and complex admittance including distinct terminal phasors.
LU and Faer pass. Common-mode Proptest uses fixed seed2026111801,64 configured
cases; the existing charge-derivative property retains seed2026110201/128cases.

The separate-process ngspice47+ authored RC fixture uses operating-point startup
and a differential PWL ramp under three common-mode offsets,with the same
nonlinear junction parameters and numerical bounds as the existing oracle.
Six DC,six AC and30 selected transient comparisons pass across LU/Faer.
Requested times are1,3,5,7,10ns. Native and reference must reach10ns; nearest
time distances are bounded to1e-16s and1e-12s respectively. Explicit AC counts/
frequencies prevent empty or substituted results from passing.

| Quantity | Largest absolute difference | Existing bound |
|---|---:|---:|
| DC differential voltage |8.261e-10V|2e-8V|
| DC terminal current |4.561e-13A|2e-11A|
| AC voltage component |6.995e-10V|2e-6V|
| AC current component |4.995e-13A|2e-9A|
| Selected transient differential voltage |5.998e-5V|0.002V|
| Selected transient terminal current |5.998e-8A|2e-6A|

Two initial authored UIC reference attempts aborted in ngspice at the ideal
common-mode branch,despite process exit0. They remain failed reference evidence,
not passing simulation coverage. The final OP-start/ramp fixture is explicitly
different; no user/original corpus input was altered or counted as progress.
The original grounded oracle also passes unchanged.

## Verification and limits

45 unit+636 aggregate integration tests pass;6 explicit skips. Both external
oracle tests were additionally enabled and pass. Three native model-provider
checks and the separate endurance batch remain unmeasured in that aggregate.
Intel routine gates15pass; separate UHD630 SpMV6scenarios/11,880,960values have
max absolute error0. These do not qualify the new floating diode fixtures on
GPU or establish a whole-deck speedup. Real StrictDoc:105documents/598UID.

Mapped line coverage from the named spice_diode test: API257/393 (65.39%),
SPICE diode63/85 (74.12%). Stable Rust branch coverage is unavailable. These
are selected integration scopes,not whole-library coverage. Existing managed
Client and cancellation-test hashes still match issue61's evidence; no fresh
managed mutation result is claimed here.

Raw inputs,outputs,failed attempts,commands,tool/binary hashes and source-bound
native reports are recorded in the paired JSON and the E: artifact root. Normal
qualification and mutable mutation targets stay separate. No blanket RAM caps,
main merge,CI dispatch or new original-deck agreement. The fifty target stays2/50.
Issue62 remains open for273 originally unattempted candidates; this replay
addresses only the two identified survivors and does not close that broader gap.
