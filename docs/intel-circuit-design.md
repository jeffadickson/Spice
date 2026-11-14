# Intel circuit solve experiment

User outcome: run unchanged circuit decks through an actual Intel GPU linear
provider, compare all requested analyses to LU/Faer, ngspice and RSpice, and
measure end-to-end cost. CPU assembly, Newton control and time integration stay
owned by the existing engine; only the linear solve changes. No CPU-solve
fallback may be reported as GPU execution.

## Reuse decision

The existing OpenCL probe only multiplies CSR matrices. The bounded next step
reuses ViennaCL 1.7.1 GMRES through a narrow C++ C ABI, isolated in an optional
Rust host crate. Its general nonsymmetric solver can handle MNA voltage-source
constraints, unlike CG (requires SPD) or ViennaCL's unpivoted dense LU. Retain
the existing physical residual audit; iteration convergence alone is insufficient.
Use row/column equilibration without dropping matrix entries. Complex AC uses
the mathematically equivalent real block system; original unknown limit 256.

Primary sources inspected November 14, 2026:
- https://viennacl.sourceforge.net/doc/manual-algorithms.html
- https://github.com/viennacl/viennacl-dev/tree/release-1.7.1
- https://www.boost.org/library/latest/compute/
- https://github.com/ginkgo-project/ginkgo/blob/develop/INSTALL.md

ViennaCL exact pin: 77e9049d5622d46b6801a725917a931eda096f76, MIT/X11;
retain LICENSE with each compiled provider. Headers and downloaded sources stay
on E:. This is a legacy dependency experiment, not a maintenance endorsement.
Ginkgo/SYCL remains a better maintained candidate for larger follow-up work,
but its compiler/runtime installation is not required to test today's existing
OpenCL device. Boost.Compute provides primitives rather than this solver.
No custom Krylov or GPU factorization algorithm will be invented here.

## Boundaries and acceptance

The optional `intel-opencl` Cargo feature adds an explicitly selected `intel`
provider. Loading a trusted absolute provider path is process configuration,
never a deck operation. The safe Rust core validates shapes/finiteness and
componentwise residuals; FFI is confined to a separate crate. The native module
selects only Intel GPU devices with FP64, serializes its context, catches C++
exceptions and reports errors. No default backend or existing CPU behavior
changes. Missing runtime is an error, not a CPU fallback.

Verify zero-diagonal MNA, real/complex solves, malformed/singular systems,
random known solutions and deliberately incorrect outputs. Compare unchanged
decks at existing bounds; record every failure and omitted analysis. Measure
cold process and warm execution separately; include transfers and context
initialization in the relevant scope. A finite successful cohort does not
qualify arbitrary semiconductor convergence, GPU acceleration or production use.

The independent corpus work selects two unpassed original decks and fixes
shared frontend/model blockers without changing their inputs or oracle controls.
