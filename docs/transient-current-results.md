# Named transient and signed-current comparisons

Four original-source fixtures now run through explicit frontend transient
selection, both Rust CPU providers, native ngspice and the isolated SpiceSharp
reference. Each uses two timesteps, operating-point initialization and101 named
common time coordinates. This is a separate qualification set, not four more
files added to the frozen928 corpus census.

| Fixture | Result |
|---|---|
| PULSE drive through midpoint PSpice switch and RC load |Three-way agreement|
| PWL switch control ramp, finite control load and RC output |Three-way agreement after timestep refinement|
| Delayed phase90 SIN into resistor |Rust/ngspice agreement; SpiceSharp differs before delay|
| EXP drive into RC divider |Three-way agreement|

There were16 Rust requests,8 native requests and8 SpiceSharp requests. All
executed, but execution alone does not establish numerical agreement. Of58
aggregate observable checks,54 pass their declared bounds and4 retain the
SpiceSharp delayed-SIN discrepancy (both providers, voltage and source current).
The overall coordinator deliberately exits nonzero when that mismatch exists.

Source current is positive from the source's positive terminal to negative
terminal. An independent analytical check verifies control-source current
`-V(control)/20000` for both switch fixtures on every engine, bound0.1nA.
The20kOhm control resistor is explicitly present in all source decks. Ngspice's
additional default1TOhm input loading remains different from Rust's high-impedance
control port; this check qualifies the explicit load by composition, not intrinsic
loading equivalence. Native runs disable personal startup files and explicitly
select PSpice compatibility (`-n -D ngbehavior=psa`).

Rust uses backward Euler. Native and managed references retain their independent
integration implementations. Reference traces are linearly interpolated onto the
common grid; time ordering, vector shape, finite values and V/A descriptors are
checked. Extrapolation is refused; endpoint clamping is limited to a1ps rounding
guard. No input `.tran`, `.IC`, UIC or control program is silently executed.

The dynamic bounds are0.012V and12microamps for these named circuits, alongside
much tighter provider agreement (10nV/0.1nA) and analytical control-load checks.
The first ramp run at10us/5us had12.346microamp native current error, just outside
the12microamp bound. Its raw failure is retained. Reducing to5us/2.5us reduced the
error below the unchanged bound; measured coarse/fine errors and refinement
deltas are stored for each observable. This finite refinement evidence is not
a general error estimator or a global accuracy proof.

The delayed-SIN fixture is minimal: one voltage source and one1kOhm resistor.
Ngspice and fixed Rust hold1V/-1mA before the1ms delay. SpiceSharp exports a
value differing by1V/1mA there. Its numerical equations were not patched, the
comparison was not weakened, and the discrepancy remains visible in the report.

Verification: the coordinator's trace self-test rejects duplicate/reversed times,
nonfinite/wrong-width/single-point traces and out-of-range interpolation.
Coverlet measured95.83% lines/65.9% branches in the coordinator on this run;
unexercised protocol-failure branches, upstream reference internals, Linux and
whole-corpus transient coverage remain unavailable. Exact source, executable,
reference dependency and patch identities are in
`evidence/transient-current-results.json`. Earlier failure logs and full arrays
stay in its E: artifact directory. Requirements SEM-TRANSIENT-SELECT-001 and
SEM-TRANSIENT-REFERENCE-001 remain Draft.

After building the generated frontend with the repository's pinned ANTLR setup,
run the built `Run-TransientReferences.dll` with Rust executable, ngspice
executable, third-reference worker DLL, patch manifest (or `-` for stock), and a
fresh output directory whose parent exists. `--self-test` runs trace contracts.
The coordinator makes no performance, full-SPICE, GPU or production claim.
