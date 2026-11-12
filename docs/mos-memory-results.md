# MOS memory execution checkpoint

The original `general/mosmem.cir` now completes its requested 0–2 us transient
through the normal requested-analysis frontend with the explicitly selected Faer
CPU provider. This is execution progress, not a second stock-ngspice parity
closure. The stock-reference comparison remains failed. The original
`regression/pz/ac-resistance.cir` is separately verified at checkpoint `9427e23`.

## Changes and numerical scope

MOS1 lowering now retains the default 1 pS body-junction conductance as explicit
branches. Full-source Gmin startup avoids source-scaling through the memory
latch's bifurcation. This follows the junction treatment inspected in pinned
RSpice `29c28a022625fd7cc2c56ead1cce2d3066663074`,
`crates/rspice-core-devices/src/device/mosfet/classic/junctions.rs`, and
[ngspice MOS1 load](https://raw.githubusercontent.com/ngspice/ngspice/master/src/spicelib/devices/mos1/mos1load.c).
Temporary continuation shunts are removed before physical acceptance.

The native runner implements endpoint nonlinear trapezoidal equations,
first-order restarts at startup/source corners, periodic PULSE corner discovery,
and bounded adaptive retries. Rejected trials publish no state, source history,
midpoints, observer output or restart evidence. Accepted half-step history feeds
independent KCL and charged-junction terminal-current reconstruction. Failed
provider calls remain counted. LU refinement also projects exact homogeneous
constraint chains before applying its unchanged full-system backward-error gate.

The zero-oxide MOS1 requested profile uses voltage relative/absolute budgets
1e-7/1e-9 V and current relative/absolute budgets 1e-4/1e-9 A. Every branch current
remains in error control. First-order restart trials receive a quarter of the
current budget; this tested numerical policy is not a global error bound.
The authored `abstol=1u` remains on the request, while the effective Newton
current bound is capped at the stricter 1e-12 A consistency floor and reported
explicitly. Independent KCL and reference comparison bounds are unchanged.

Meyer-charge MOS and the native BSIM provider remain outside this nonlinear
trapezoidal profile and refuse explicit admission. Explicit charged-junction
initial states and nonlinear energy auditing remain unavailable. Faer is an
explicit selection; no provider fallback occurs. Dense LU still fails a later
high-accuracy memory ramp and is not qualified for the complete deck.

## Reference evidence

The original deck SHA-256 is
`6d5bd5a8e00f1f51f1c1c465bf8f80851cbc7c731623f11a4db1052389c39667`.
The RSpice corpus pin is the revision above. The default ngspice RAW has only
150 points and fails the frozen comparison on several internal voltages and
source currents. No samples or currents were excluded to turn it into a pass.

Two diagnostic ngspice runs retain the physical circuit but deliberately change
numerical controls: TMAX 0.3125 ns, with respectively RELTOL/ABSTOL/VNTOL
1e-5/1e-8/1e-8 and 1e-6/1e-10/1e-10. Both reach the complete 2 us stop; all
13 mapped root voltages/source currents agree against each profile using the
existing 1e-6 V, 1e-9 A and 1e-4 relative comparison limits. These modified
reference inputs are labelled `reference_original_input=false`; they do not
increase the frozen original-default agreement count. Raw source-current
interpolation near corners is sensitive to the native time grid. Refined
agreement alone does not establish the entire cause of the default mismatch.
An additional native-versus-native comparison on the retained engine grid
finds only 5/13 stock-reference signals within those same bounds against the
strict-fine reference, while mid-fine versus strict-fine agrees on 13/13.
Thus the stock reference itself is outside the frozen accuracy limits under
numerical refinement; reproducing its coarse solution is not an established
correctness target. Its original-default qualification row nevertheless stays
failed rather than being silently replaced by the refined result.

## Requirements and verification

Requirements and implementation invariants are in `mos-memory-execution.sdoc`;
bounded source hashes, actual counts, coverage scopes and limitations are in
`../evidence/mos-memory-execution.json`. Requirement status remains Draft rather
than asserting general model-family qualification.

Regression verification covers independent scalar charge equations, source
corner ringing, decimal grid/corner roundoff, malformed histories, unsupported
model admission, failed-trial rollback, actual solve accounting, accepted
midpoint cardinality and corrupted audit inputs. A captured 37-by-37 first-ramp
system tests LU constraint projection against the original matrix and Faer.
The Rust charge property uses 64 cases with seed 2026111202. Managed frontend
coverage includes the preparation and provider-selection changes; native
integration tests skipped by the managed suite are separately exercised by the
real deck runs, not counted as instrumented managed coverage.

An interrupted in-place mutation attempt exceeded its process-tree memory cap.
Source restoration was checked against pre-run SHA-256 backups. A subsequent
baseline exposed stale mutated build outputs after restoration preserved older
source timestamps. Refreshing unchanged source timestamps forced the normal
library rebuild; the final unmutated baseline must pass before counting any
mutants. Cargo-mutants' external jobserver was disabled and Cargo received
`-j1` explicitly. Retain these interrupted logs as tooling/resource evidence,
not as numerical failures or completed mutation coverage.

## Reproduction

Use the repository's pinned tools and existing artifact-aware build setup.
Validate the checkout, corpus, engine, oracle and output parent before use, and
arm a process-tree memory watchdog before compiling or running a campaign.
Keep TEMP/TMP, target, managed intermediates, raw results and coverage on the
validated E: artifact root. Do not use a shared mutable build/output directory
for concurrent jobs.

`scripts/Run-RequestedCorpus.cs` accepts repository, corpus, engine, ngspice,
inventory, fresh output directory, optional per-case seconds, and optional
`lu`/`faer`. Its backend defaults to LU for compatibility. The retained two-deck
inventory and exact invocation are recorded in the bounded evidence manifest.
Run the unmodified default reference first; keep refined numerical-reference
experiments separate. No CI, push, canonical-main update or automation restart
is part of this checkpoint.
