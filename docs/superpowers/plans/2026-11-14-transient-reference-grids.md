# RC/RLC transient qualification repair

Goal: distinguish integration physics, adaptive-controller choices and reference
sampling error without relaxing bounds or changing default simulation behavior.

Investigation: both archived free runs meet closed-form ramp bounds at every
sample. Linear interpolation of coarse ngspice points introduces false-looking
differences; stock RLC also misses the analytical voltage bound at 40 points.
Pinned RSpice's execution runner replays ngspice's accepted grid for pointwise
comparison. Thus its passing result was controlled-grid evidence, not proof of
better free-running accuracy. No defective core equation has been established.

Chosen implementation: optional explicit accepted-time grid on the existing
configured linear transient path, exclusive with adaptive stepping. Retain
default free runs and their strict original comparisons. A separate replay
uses the exact reference times and is labelled physics parity, never a newly
agreeing stock-default/adaptive deck. Reuse the existing stepper and providers;
no new numerical algorithm or dependency. Reject nonlinear replay, malformed
grids, missing source corners and conflicting/budget-exceeding controls before
publishing state. Preserve original input/request and replay hashes.

- [x] Add and observe failing accepted-grid API tests.
- [x] Implement bounded explicit grid selection and managed wire metadata.
- [x] Add StrictDoc requirements and deterministic/property/negative checks.
- [x] Replay fresh RC/RLC stock ngspice grids with LU/Faer; independently check
  all free-run voltages and currents against analytical ramp responses.
- [x] Run relevant suite, coverage, mutation checks and routine Intel checks.
- [x] Review, record honest limitations, commit and push implementation (`e44aefd`).

Artifacts: E:/Artifacts/Spice-RC-RLC-76d5a20-20261115T011507Z.
Canonical main and unrelated VeloSim work remain untouched.
