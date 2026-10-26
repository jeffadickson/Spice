# Consistency audit qualification: iterations 311-320

This ten-iteration slice adds independent physical consistency checks. It does
not complete M1 or establish full SPICE compatibility, model validity, global
waveform accuracy, large-circuit performance or GPU qualification.

## Delivered behavior

- 311: CONS-001 explicit passed/failed/not_checked reports with dimensional bounds
  and JSON-safe failure metrics.
- 312: CONS-002 element-level DC currents and KCL at all nodes, including ground.
  No assembled matrix, numerical solver or production diode evaluator is used.
- 313: CONS-003 independent V/VCVS/CCVS and DC inductor voltage constraints.
- 314: CONS-004 absorbed-power balance, including explicit fixed-Gmin shunts.
  A regularized solve explicitly leaves unregularized physics not_checked.
- 315: CONS-005 polarity-normalized parallel voltage-source contradictions and
  advisory missing-DC-reference-path diagnostics. This is not general rank analysis.
- 316: CONS-006 backward-Euler step KCL, capacitor current, inductor voltage and
  discrete energy checks. The identity includes numerical dissipation: for each
  capacitor, C*Vnext*(Vnext-Vprevious) equals stored-energy change plus
  0.5*C*(deltaV)^2; the analogous inductor term uses L*Inext*deltaI.
- 317: CONS-007 successful manifests include consistency and diagnostics fields;
  failed physical checks return numerical_failure. Typed C# records expose these
  additive fields. Audits run before derived diode signals are appended.
- 318: CONS-008 tracing 0.1.44 and tracing-subscriber 0.3.23, locked in Cargo.lock.
  The CLI emits JSON events to stderr; stdout remains the protocol. The library
  installs no subscriber. RUST_LOG filters events (warn fallback); request/backend
  spans, compile size, preflight/audit events and continuation stages are recorded.
  Newton residual iteration events require trace. No raw decks, full requests or
  solution arrays are dumped. Framework configuration follows the upstream docs:
  https://docs.rs/tracing-subscriber/0.3.23/tracing_subscriber/fmt/ .
- 319: CONS-009 opt-in Rust comparison of already computed coarse/finer waveforms
  at exactly shared coarse timestamps. It reports observed change, requires
  finite increasing axes and equal stop times, and does not silently rerun or
  interpolate. It is not a rigorous global error estimator.
- 320: CONS-010 full checkpoint, adversarial/mutation checks, coverage availability,
  documentation and handoff.

## Bounds and usage

Automatic DC/legacy BE audits use absolute bounds of 1e-10 A, 1e-8 V, 1e-10 W,
1e-12 J and relative 1e-8 times the sum of absolute physical terms. Each result
retains its actual bound. Overflow fails with null metrics. Rust callers can use
AuditOptions for explicit standalone audits; automatic API policy is fixed.
Transient aggregation retains the worst normalized discrepancy per identifier,
including the accepted timestamp, with evidence memory bounded by circuit size.
Audits add CPU work; no performance threshold has been qualified.

For debugging in PowerShell, set `$env:RUST_LOG='circuit_simulator=debug'` before
running the CLI. Use `circuit_simulator=trace` only when Newton iteration events
are needed, and `off` to silence logs. The default does not log each accepted
sample. Verbose logs remain subject to the managed client's existing stderr
budget; tracing is diagnostic evidence, not result data or a correctness oracle.

AC and configured-transient independent physical audits are explicitly
not_checked; their existing numerical residual/controller checks remain active.
In particular, this energy profile is not applied to trapezoidal integration,
adaptive histories or overridden waveforms. Global accuracy remains not_checked
in automatic manifests even when the physical checks pass. Additional named
ngspice oracles and direct sparse AC/transient qualification remain active work.

## Reproduction and verification

Use the repository toolchain/environment described in continuation-qualification.md.
The host compiler is rustc 1.99.1 (58cf8f9c3), GNU Windows; .NET uses global.json.

- `cargo test --locked --test consistency`: 13 focused tests passed.
- `dotnet run --file scripts/Checkpoint.cs -- 320`: 242 Rust tests and 28 managed checks passed, as did build, formatting and Clippy with warnings denied. Exact commands and source hashes are retained in checkpoint-320.json.
- StrictDoc 0.30.4 SDReader parsed all 12 docs/*.sdoc files; 204 unique IDs and
  185 resolved parent references were checked. No HTML export is claimed.
- Property corpus: fixed LCG seed 0x41554449545f3331, multiplier
  6364136223846793005, increment 1. 128 resistor dividers use independent analytic
  states; source-current perturbations must fail. 512 raw-bit state perturbations
  exercise finite/nonfinite and overflow handling without panics or invalid JSON.
  No failing seed was observed. This is bounded fuzz-smoke, not coverage-guided fuzzing.
- All 22/22 selected mutations were killed; no surviving, invalid or timed-out mutants. Results and exact source hashes are retained in mutation.json. New
  operators corrupt KCL polarity, omit capacitor current, bypass API audit rejection,
  or accept all finite discrepancies. Mutations run in a copied ignored sandbox.
- Managed coverage rerun with dotnet-coverage 18.12: 140/151 owned client source lines (92.7152%) and 51/66 mapped branches (77.2727%). Reports and source hashes are in managed-coverage.json and managed-coverage.cobertura.xml. Generated metadata/framework code, tests and Rust are excluded; changed-line coverage is not separately calculated.
- Rust coverage was attempted again: the MSVC nightly has profiler_builtins but link.exe is missing. All owned Rust, including the new audit/topology/logging code and changed tests, remains unmeasured for line/branch coverage. See coverage.json. C# script/F# example coverage is not collected. cargo-nextest is unavailable, so cargo test includes the doctest phase.

No new external ngspice run, coverage-guided fuzz campaign, performance benchmark
or hardware GPU test was performed in this slice. Passing audits establish
consistency of the supported equations, not that a chosen model represents a
real device or that a solution is unique.
