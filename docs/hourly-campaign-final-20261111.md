The final scheduled session replayed the four original campaign target files
at committed source e1cf1936ed71e6fa3fdb54c44ea518fb332d2f02 and corpus pin
29c28a022625fd7cc2c56ead1cce2d3066663074. The checkout was clean during this
replay, and the coordinator's immutable-source gate passed. Runtime was
6.1154024 seconds. This is a bounded final replay, not a full-corpus run or
20 hours of continuous test coverage.

Two original decks run and agree with ngspice: regression/misc/if-elseif.cir
(two mapped observables) and regression/misc/log-functions-1.cir (14 mapped
observables). Both real reporting executions exit zero without diagnostics.
The original inputs, their requested OP analyses and existing tolerances are
retained. These two decks are the campaign's total new agreeing original decks;
there are zero additional original completions in this final wake.

Two targets remain incomplete. general/mosmem.cir fails transient initialization
with numerical_failure: source continuation cannot reduce below minimum step
0.0001 after failure at scale 0.4188232421875; Newton iteration limit exceeded.
No successful transient samples or ngspice agreement are claimed. Earlier
strict Faer/Gmin diagnostic execution did not establish agreement with the
original default oracle and remains separate evidence. The requested profile also currently uses backward Euler when no method option
is supplied. Startup failure does not qualify default integration-method
compatibility; that requires separate evidence after initialization succeeds.

regression/pz/ac-resistance.cir is still refused before analysis dispatch. Its
reported resistor error comes from strict fallback preparation after control
admission fails; requested AC resistor support itself already exists. The real
remaining implementation needs are ordered OP/AC/PZ phase execution, complex
reporting vectors bound to the correct analysis plot, and control PZ dispatch.
Simply relaxing the admission guard would risk evaluating assertions against
the wrong result. The separately authored loaded-RC root example agrees with
ngspice but is not substituted for completion of this original script.

Six local commits preserve the verified work since 36dd277: e3395dd (netlist
conditions), 66158d4 (phase-scoped reverse-junction progress), 68c67e6 (MOS1
forward-body DC/AC conventions), cdd4560 (bounded scalar compose/log reporting),
a984f11 (behavioral DC voltage expressions), and e1cf193 (loaded RC small-signal
PZ). Their individual requirements, negative/property/mutation checks and
coverage scopes remain in their committed evidence. Historical final checks
include 109 native tests, 828 managed tests with 40 explicit prerequisite skips,
and 61 StrictDoc documents parsed. Managed coverage was 20719/26408 lines and
5935/9468 branches including generated code. Native coverage was integration-
only with branch metrics unavailable. No new code changes, coverage, mutation
or long fuzz campaign are claimed from this final read-only replay.

A prior pinned RSpice conformance matrix marks mosmem as a smoke contract.
That is not evidence that every requested analysis numerically agrees with
ngspice. A fresh RSpice replay was not started because memory admission was
refused; the executable's argument probe is not a deck test. Preserve this
scope distinction when selecting reference cases for subsequent fixes.

All workloads are serialized and guarded. The final replay used a 192 MiB
process-tree/aggregate limit, 450 MiB OS headroom, a 64 MiB C: floor, an eight-
minute job cap and an explicit finish deadline before the user hard stop.
Observed peak was 143687680 bytes. Initial admission refusals are unattempted
work, not passing tests. New raw outputs, logs, snapshots, package restore and
scratch remain on E:. The hourly automation is PAUSED and no owned workload
is left running. No source/main changes, pushes or CI dispatches occurred.

The authorization window's hard stop is 2026-11-11T23:53:39Z. This final
scheduled session closes before that stop; the window is not extended and no
further hourly wake is requested. Bounded final data and input hashes are in
evidence/hourly-campaign-final-20261111.json, with raw replay artifacts under
E:/Artifacts/Spice-hourly-final-e1cf193. Future work should resume explicitly
from the clean fix/deck-blocker-implementation branch and address the two open
failure families without counting authored or selected-analysis fixtures as
new original-deck compatibility.
