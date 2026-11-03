# Original BJT amplifier initialization diagnosis

The frozen final census at `f64edbd` has failed original amplifier requests in rows 0458 (`examples/xspice/original-examples/spice3.deck`) and 0521 (`examples/xspice/various/xspice_c1.cir`). Their retained public-wire requests are byte-identical: SHA256 `97b9ba3ba7f15112e01ecde0053e877367ff1a4417a102f7c74109bfaf62cd0e`. Original classifications remain failed. This is a bounded convergence diagnosis, not a native-waveform comparison or new numerical-agreement claim.

The direct configured BE request initializes its nonlinear OP with the existing default 50-iteration policy. Explicit public OP/direct50 reproduces `Newton iteration limit exceeded` on both rows. Nominal sources equal time-zero overrides in these exact requests; the sine has zero offset/phase/delay and its circuit source is 0 V.

| Public initialization experiment | Observed result |
| --- | --- |
| Direct50 | Newton limit, both rows |
| Source stepping, 50 per stage | Converges, four accepted stages, 105 linear solves, final source scale1 |
| Gmin stepping, 50 per stage | Newton limit, both rows |
| Direct200 | Converges in98 iterations, both rows |
| Gmin stepping, 200 per stage | Converges on canonical row0458, 11 stages, mandatory final unregularized solve confirmed |

The helper reconstructs an explicit state through public descriptor identities/units, in the documented node/branch order. It never substitutes zero for a missing value. These default BJTs have no junction-storage companions; their real external10uF coupling capacitor remains active. This does not qualify explicit-state initialization for charged junction profiles, which have separate refusal limits.

Source-stepped LU state is accepted and one10us BE step succeeds. LU also completes64 steps to0.64ms. A128-step/1.28ms prefix and the original200-step/2ms run fail with `LU refinement did not meet componentwise backward error`. A direct200-derived state also fails full LU. This is a distinct later provider refusal, not an initial OP Newton-limit failure. The earliest failing step was not localized; the64/128 observations are retained public prefix experiments, not a universal bound or proof of monotonic failure.

Existing faer plus source50 OP plus explicit public warm state completes all200 original10us BE steps to2ms (201 samples,1226 step linear solves), with15 physical checks passed and global accuracy not checked. Waveform, step, stop and method are unchanged; backend and initialization policy are explicit experimental changes. No ngspice rerun, original-grid numerical comparison or original request pass is claimed.

Minimal integration options for issue#29 are: expose an explicit initial-OP Newton policy reusing existing options, or use the current two-request public warm-start path for eligible profiles. The latter already demonstrates convergence here. Track the LU residual refusal separately with the frozen128-step request; do not weaken its acceptance or silently add fallback. The runner remains a thin default-policy adapter, and this diagnostic changes no production policy.

`scripts/Diagnose-BjtInitialization.cs` is a reusable bounded diagnostic mode: arguments are existing frozen census, existing copied engine and fresh output. It reuses `SimulationClient` and typed public requests, with120-second cooperative experiment/30-second call budgets. The25 calls are finite scenarios; expected failures are retained. Raw requests/responses and immutable hashes remain under the artifact path in `evidence/bjt-corpus-initialization.json`.

No further refinement/native campaign was run after the requested conclusion. Coverage for this diagnostic-only helper was not collected; no coverage, mutation or fuzz success is inferred. Earlier runner/model coverage remains separate historical scope. Requirements remain Draft and numerical qualification remains open.
