# Implementation status after iterations 151–200

The fourth slice adds a **bounded nonlinear diode DC reference path** with analytic damped Newton iterations, alongside the existing scalar sweep work. It does not complete the 23-package production plan.

| Plan area | Implemented evidence | Remaining |
|---|---|---|
| Solver variety | nalgebra dense and independent faer sparse LU; real/complex conformance and prepared RHS reuse | broad difficult-matrix corpus, native KLU/cuDSS/Ginkgo adapters, large-circuit sparse assembly |
| Device equations | R/C/L, a Shockley diode, independent sources, signed flux and branch currents, DC power diagnostics | additional semiconductor models, production limiting/Jacobians and full device/plugin API |
| Dynamic analyses | configured initial states, PWL/sine sources, trapezoidal/BE, adaptive step doubling, accepted observers | nonlinear/multistep history, full event/co-simulation rules and complete DAE topology qualification |
| API/data | typed C#/F# experimental JSON contract, measurements/assertions, capability discovery | Arrow, C ABI, persistent sandboxed worker, complete stable API/migration policy |
| Campaigns | bounded sequential cases, scalar sweeps/grids, seeded sampling recipes, per-point failures and programmatic result rows | parallel resource scheduling, statistical qualification, retries/resume, batch CLI transport, actual StressForge contract |
| Persistence | bounded trusted-local artifact commit/read with hashes | hostile storage policies, remote/object storage, power-loss durability qualification |
| Authoring | typed .NET/F# builders and experimental scalar expression derivatives/domains | selected Starlark frontend, full units system and integrated behavioral models |
| Verification | analytic/provider/metamorphic/fuzz-smoke/mutation tests; scoped managed coverage | supported-host Rust coverage, coverage-guided fuzzing, full native/GPU evidence |
| Release | no production release claim | license/platform/packaging and all declared M1 exit gates |

Most work packages retain Proposed status because their complete exit criteria are not satisfied. Current checkpoint and source-hash reports, rather than implementation counts, define what has been tested.
