# Reference-expression audit qualification

Base: 7af85bc2bc4de22f8f33c8de919df6088a681823. Production code is unchanged.
New public-audit tests supply manufactured states, not solved states or values
from the production AD evaluator. Eighteen expression forms have independent
analytical resistance values; ideal source currents balance both the resistance
and a VCCS. Three common control offsets and a deliberately perturbed supplying
current challenge signed control differences and the resulting physical checks.
An additional fixture loads a 1 V sensing source with 4 ohms, giving a signed
source current of -0.25 A. A gain of -2 and voltage factor0.75+0.5*Vcontrol
then fix the voltage-scaled CCCS output independently, including nonzero control
negative terminals and translated control potentials.

The same suite accepts depth 64 and exactly 1024 nodes, rejects depth 65 and
1025 nodes, checks invalid domains, and checks three non-unit Gmin conductances.
The known circuit is a 2 V source and 2 ohm load, with the explicit shunt current
added to its 1 A load current. This is regularized physics, not qualification of
the corresponding unregularized state.

## Why some traversal mutations can remain equivalent

The source path is public audit -> validate_state -> Circuit::validate ->
Expr::validate, before dc_currents or constraint_checks reconstructs values.
Configured audits validate the original circuit and clone it while changing
only independent voltage/current source scalars. They preserve expression trees.
All current reference-expression callers therefore receive trees of depth at
most 64 and at most 1024 nodes.

On those immutable trees, weakening the private traversal guard cannot admit
an oversized tree; none reaches that helper. Keeping its node counter at 1024,
or increasing it by at most 1024, changes no evaluated value. Replacing a tracked
depth increment with multiplication by one makes tracked depth no greater than
actual depth. The original guard would not reject any of these admitted calls.
Each surviving diff is assessed against this invariant and kept as a raw native
MissedMutant, with a separate bounded public-equivalence disposition.

This is source review supported by finite boundary execution, not formal proof,
permission to remove defensive guards, or a blanket skip for future mutations.
Tightening the limit at depth 64 is observable and is caught. Changes that
decrement depth can reject valid calls and are also challenged by root-operator
fixtures. Actual multiplication/division mutations in expression values are
distinct from bookkeeping changes and need numerical tests.

Reassess equivalence if callers, validation limits, mutability, override behavior
or evaluator semantics change. No mutation filters or production guards changed.

## Evidence and limits

Raw run: E:/Artifacts/Spice-epoch-triage-20261119T025238Z.
Native selection: current-audit-lean/20261119T025750Z-f1728e0a under
E:/Artifacts/Spice-issue61-20261117T133906Z. The bounded JSON records terminal
counts, exact original names, frozen diff hashes and per-case dispositions.
Later test outcomes do not rewrite the frozen 30a5a9a epoch or retest all 4411.

The 47-case stage recorded 24 caught, 22 missed and one tool-Unviable attempt.
The latter was Windows access denial while removing circuit-simulator.exe, not
a Rust syntax/type failure. No matching live runner image remained at review.
One focused retry compiled but survived: the target was a voltage-scaled CCCS,
while the initial fixtures covered plain VCCS. The separately loaded sensing
source fixture was then added, and an exact replay caught that mutation.
Final disposition: 25 distinct caught counterparts and 22 reviewed equivalents
across 49 attempts, preserving all three native stages. No timeouts occurred.

Stages under the same cache parent:

- 20261119T025750Z-f1728e0a: 47 cases, 24 caught/22 missed/one I/O build failure.
- 20261119T032354Z-829d130d: focused retry compiled, one missed.
- 20261119T032729Z-838455e5: nonzero sensed-current fixture, one caught.

The access-denial cause beyond that filesystem operation is unresolved. No
permanent Windows runner fix is claimed; a future qualified helper may need a
bounded sharing retry. The active frozen helper/configuration remain immutable.

Five new tests pass. Full baseline: 51 unit plus 667 integration passed, six
explicitly ignored. Existing property tests ran with environment seed2026111903;
there is no new standalone fuzz engine, Stryker run or ngspice comparison.
Intel's 15 routine tests and separate SpMV probe pass. The six ignored tests
are three native BSIM/SOI tests, two ngspice-oracle tests and a separately
launched endurance batch. They and remaining native audit cases are unavailable
evidence, not passing device-model coverage. No new original decks or universal
GPU claims follow.

Scoped integration line coverage: audit.rs 246/579 under audit_reference_contracts.
Stable Rust branch coverage is unavailable; test source, vendor libraries and
executable main are outside that measurement. The scope differs from previous
audit suites and is not a comparable overall coverage increase or decrease.
The coverage helper uses seed88320261103. StrictDoc0.31.0 parsed112 documents
with623uniqueUIDs. Read-only review found no important test or call-path issues;
the final per-diff dispositions are retained for independent inspection.
