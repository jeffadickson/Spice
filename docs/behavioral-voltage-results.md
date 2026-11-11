The requested-analysis path now lowers bounded single-control behavioral voltage
expressions to a typed model tree. It reuses the generated ANTLR expression
syntax and existing Rust automatic derivatives. The native DC solver stamps the
constitutive equation and obtains a linear-constraint seed before evaluating
logarithms. That seed is temporary: the full physical equations and independent
constraint, KCL and power audits determine acceptance. Source branch currents
are preserved and compared against the oracle.

The unchanged original `regression/misc/log-functions-1.cir` from corpus commit
`29c28a022625fd7cc2c56ead1cce2d3066663074` now executes its one requested OP.
All 14 mapped voltages and currents agree with ngspice-47+: maximum voltage
error 1.1102230246251565e-16 V; current errors zero. Comparison bounds remain
1e-6 V / 1e-9 A absolute and 1e-4 relative. The reporting evaluator executes
all original control assertions with exit zero and no diagnostics. Separately,
ngspice executes the entire original file with exit zero and `INFO: success`.
Individual-analysis comparison alone is not evidence of control-program parity.
The input hash and separate control outcomes are retained in the evidence.

Verification: 109 selected native tests pass, including seven new public-wire
behavioral tests. Their property covers 64 positive inputs, Proptest seed
2026111101. The full managed frontend suite passes 819 tests with 40 explicit
external-prerequisite skips; 13 focused behavioral cases pass. A zero-control-
slope mutation is killed by the independent centered-difference Jacobian test
(six pass, one fail). Source restoration is hash-verified, then the broader
native suite and final original-deck comparison are rerun on the restored build.
Malformed operation/field, domain, depth, emitted-tree budget, unsupported
analysis and corrupted-output cases are exercised. No new long fuzz campaign,
full cargo-mutants campaign or Stryker campaign ran for this checkpoint.

Coverlet measured 20705/26394 lines (78.44%) and 5924/9452 branches (62.67%) in
its managed suite scope, including generated code. The new compiler class has
79.45% line and 46.66% branch coverage. Native integration-only LLVM coverage
measured expression.rs 43/75 lines, newton.rs 304/1021, compile.rs 101/287 and
audit.rs 215/545. This narrow test intentionally does not exercise the entire
simulator; stable Rust branch coverage is unavailable. These scopes cannot be
compared as project-wide coverage or correctness scores. All 60 StrictDoc files
parse with the real reader.

The runtime profile supports one differential control voltage, finite constants,
addition, multiplication, division, exp and ln; frontend subtraction and log10
are desugared into that tree. Depth is limited to 64, nodes to 1024 and source
text to 4096 characters. AC, transient, behavioral current, multiple independent
control pairs, arbitrary functions and time-dependent runtime expressions are
explicitly unsupported. The legacy selected affine preparation API retains its
previous contract. A singular linear seed or an invalid expression domain is an
explicit failure, not a guarantee of convergence or a substituted numerical floor.
See the authored example `examples/behavioral-log-op.cir` and stable requirements
in `behavioral-voltage-op.sdoc`.

Reproduction uses `cargo test --lib --test behavioral_voltage --test initial_newton
--test nonlinear --test mos1_channel --test pspice_switch --test sparse_ac_circuits
--test sparse_dynamic --test switch_stagnation_20261103 -- --test-threads=1`, with
one build worker and E: target/TEMP/TMP. Managed builds use the pinned SDK,
ANTLR 4.13.1, `--artifacts-path`, `-p:UseSharedCompilation=false` and
`--disable-build-servers -m:1`; tests use the serial runsettings and Coverlet.
The existing `RequestedCorpus.RunAsync` coordinator executes the unchanged
original inventory row; the full ngspice command is `ngspice_con.exe -n -b
<original-file>`. Exact paths, hashes, tools and bounded outcomes are in
`evidence/behavioral-voltage.json`; bulk logs remain local under
`E:/Artifacts/Spice-behavioral-cdd4560`.

All heavy jobs were serialized under process-tree/aggregate memory guards with
450 MiB OS headroom and a 64 MiB C: free-space floor. Several admission attempts
were refused; earlier guard-killed attempts are incomplete checks. Final native
regression peak was 835981312 bytes against an 850 MiB cap. The final native
executable is preserved separately from the mutable incremental build cache.
This checkpoint adds one original agreeing deck, bringing this campaign's
verified new-deck count to two. It does not resolve mosmem or general behavioral
source compatibility. No RSpice runtime execution, Linux, GPU or production
qualification is claimed.
