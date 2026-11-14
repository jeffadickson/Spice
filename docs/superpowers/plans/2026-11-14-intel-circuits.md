# Intel circuit execution implementation plan

**Goal:** Execute complete named circuit programs with a real Intel GPU solve
and compare accuracy and measured cost to CPU/ngspice/RSpice.

**Spec:** docs/intel-circuit-design.md

**Architecture:** Optional ViennaCL C++/OpenCL adapter behind a safe Rust linear
provider; existing compilation, assembly, analyses and public managed frontend.

- [x] First establish failing provider tests: zero diagonal, shape, finite,
  singular, known-solution real and complex systems.
- [x] Build the pinned MIT dependency and narrow ABI on E:, explicitly selecting
  the Intel FP64 GPU. Integrate behind a Cargo feature; preserve CPU defaults.
- [x] Run unit/property/negative tests and independent residual checks, selected
  mutations and measured owned-code coverage with limitations.
- [x] Execute original deck programs through CPU/GPU and both reference tools;
  retain full signals, original hashes, failures, cold and warm timing scopes.
- [x] Implement two additional original corpus blockers in an independent
  frontend workstream; verify all requested analyses without weakened bounds.
- [x] Add StrictDoc traceability, review, commit and push verified checkpoints.

Execution is not universal acceptance: the final selected run retains GPU
deadlines, CPU transient discrepancies and upstream smoke-only contracts.
See docs/intel-circuit-results.md for the measured outcome and limitations.
Checkpoints 1ab34d0, 1b2d5ab and 40baa6c were pushed and the remote head verified.
Results and prioritized remaining work are tracked in repository issue #48.
