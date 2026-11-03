# Bounded node .IC and transient reporting windows

The requested-analysis frontend now handles root node-voltage `.IC` for a bounded
linear R/C/L/V/I/E/G profile, and nonzero `.TRAN TSTART` reporting windows. This is a
further vertical slice, not complete initial-condition/UIC or original control-program
support. Device `IC=` fields, UIC, F/H initial-state profiles, nonlinear devices and
charged-diode initial states remain explicit refusals or unqualified work.

The existing generated assignment/call/value contexts retain `.ic v(node)=scalar`
fields and physical ranges. The existing parameter evaluator computes SI volts. No
new SPICE lexer or text splitter was added. Unsupported targets/scopes, duplicate
aliases, missing/reference nodes, targets without capacitor dynamics, and direct
ideal voltage-source constraints are refused atomically. Indirect overconstraints
can fail the actual temporary bias solve; that failure publishes no transient state.

Each admitted transient with node ICs receives a separate temporary OP request with
explicit voltage clamps. The public client solves that circuit. Every original state
component is read by public `v(wire-node)` or `i(wire-element)` descriptor identity,
quantity and unit. No missing state defaults to zero. Clamp branches are excluded.
The original physical circuit and origin/node metadata remain available in
`RequestedAnalysisPlan.CircuitPreparation`.

`TransientConfiguration.InitialState` now documents the existing wire order explicitly:
non-ground `Circuit.Nodes` voltages first, followed by V/L/E/H branch currents in
`Circuit.Elements` order, in volts and P-to-N amperes. This matches source inspection
of `src/compile.rs`; native execution verifies V/L/E layouts in this slice. H initial
state is not qualified here. Runtime still validates explicit-state dimensions,
finiteness and algebraic consistency. The managed adapter does not access internal
Rust solver/device state.

`RequestedAnalysis.PrepareExecutionAsync` returns the actual typed transient request
after the optional public-client clamp solve, plus the separately retained bias
response. `ExecuteAsync` uses the same operation and preserves later independent
analyses after an initialization failure. Raw `RequestedPointResult.Response` retains
all engine history from zero. `ReportedManifest` is a separate window view with only
accepted samples at or after TSTART; it introduces no synthetic TSTART point and does
not resample output. The default integration ceiling is
`min(TSTEP,(TSTOP-TSTART)/50)`, with original TSTEP/TSTOP still controlling source
waveform defaults. Distinct source DC/time-zero bias remains under the existing
explicit transient-profile refusal.

The qualification coordinator consumes these APIs, records IC metadata and reporting
start separately, saves the optional initialization request/response, and compares the
reported window. Native projections retain original `.IC` and original `.TRAN`
arguments, including parameter expressions. Native independently evaluates them;
no evaluated initializer or timing field is copied into the native deck. Source gates
also now include Rust sources/Cargo manifests alongside managed sources, assemblies,
executables, inventory and loaded resources.

The owned native fixtures cover RC discharge, RLC with computed nonzero initial
inductor current, E-source branch layout, and a nonzero reporting window. One separate
reporting-window fixture intentionally remains unqualified: its first engine-reported
sample is1.1us but native's first stored point is1.1024us. The comparator refuses
extrapolation and retains the complete raw data. A second window fixture has its
entire reported grid bracketed by original native samples and agrees. No sample was
silently discarded to manufacture agreement.

The RC execution also checks the independent continuum solution `exp(-t/(RC))` against
a derived first-order backward-Euler bound and analytical source-node KCL. A seeded
FsCheck campaign varies explicit IC values and checks original circuit preservation
and unique added branch identity. See `evidence/requested-initial-conditions.json` for
actual counts, source hashes, coverage, seeds and limitations. Prior requested-analysis
and coordinator reports are historical evidence for their explicitly hashed source
snapshots; this document supersedes their blanket node-IC/TSTART refusals.

Primary semantics reference: [ngspice47+ manual](https://ngspice.sourceforge.io/docs/ngspice-manual.pdf),
sections11.2.2 and11.3.10, inspected2026-11-02. The manual distinguishes forced non-UIC
initial bias from UIC device-state initialization and distinguishes integration from
stored-output windows. This implementation does not substitute one interpretation
for the other.

Verification uses the existing frontend test project, declared native executable
paths and an existing E: artifact directory. Check directories with
`Test-Path -LiteralPath $path -PathType Container` before use/creation, check existing
executables/jar with `-PathType Leaf`, and choose distinct supported
`--artifacts-path`, `GrammarOutput` and `--results-directory` subtrees. No CI or push
was dispatched; no new dependency was added. Helpers execute managed, without a
NativeAOT claim.

The integrated affine-lowering review also exposed a DC identity defect: an internal
`B1$offset` source could appear eligible for `.dc`, and duplicate synthetic/user
origins could throw. DC binding now first requires an original root V/I primitive,
then exactly one origin matching its physical range, then a matching typed wire
source. Synthetic names cannot create eligibility. Ambiguities and mapping gaps
publish diagnostics and no partial program; no `SingleOrDefault` exception escapes.
This does not retroactively change the source of an already frozen census.
