#pragma once

#include "devices/linear_devices.hpp"
#include "linalg/solver.hpp"

#include <cstddef>
#include <span>
#include <vector>

namespace spice::solver {

/*
 * Central Dimensioned Newton & Continuation Controller (ARCH-EQ-002).
 *
 * Solves the general modified-nodal DAE residual:
 *   R(x) = F(x, t) + alpha * Q(x, t) + history = 0
 * with analytical Jacobian:
 *   J(x) = dF/dx + alpha * dQ/dx + gmin * I_nodes
 *
 * Convergence requires BOTH:
 *   - Dimensional residual tolerance (iabstol for KCL rows, vabstol for voltage rows)
 *   - Dimensional update tolerance (vabstol for node voltages, iabstol for branch currents)
 *   - Independent linear backward-error acceptance (<= max_backward_error)
 *   - Mandatory physical final solve at gmin = 0 and source_scale = 1.0.
 */

struct NewtonOptions {
    std::size_t max_iterations{100};
    double vabstol{1.0e-10};           /* V */
    double iabstol{1.0e-12};           /* A */
    double reltol{1.0e-8};
    double max_backward_error{1.0e-10};
    double max_voltage_step{0.5};      /* Smooth step-size limiter for steep exponentials (V) */
    bool enable_gmin_stepping{true};
    bool enable_source_stepping{true};
};

struct NewtonReport {
    bool converged{false};
    std::size_t iterations{0};
    std::size_t linear_solves{0};
    bool used_gmin_continuation{false};
    bool used_source_continuation{false};
    double final_gmin_siemens{0.0};
    double max_scaled_residual{0.0};
    double max_scaled_update{0.0};
    double worst_backward_error{0.0};
};

struct NewtonProblem {
    double time_s{0.0};
    double temperature_k{300.15};
    dae::AnalysisMode mode{dae::AnalysisMode::OperatingPoint};
    double alpha{0.0};                   /* 0 for DC, 1/h for BE, 2/h for Trap */
    double f_scale{1.0};                 /* 1.0 for DC/BE, 1.0 for Trap when history = F_prev - 2/h Q_prev */
    std::span<const double> history;     /* Optional length=dim additive constant vector */
};

[[nodiscard]] core::Diagnostic solve_newton(
    devices::CompiledPlan& plan,
    linalg::RealLinearSolver& linear_solver,
    const NewtonProblem& problem,
    const NewtonOptions& opts,
    std::vector<double>& in_out_x,
    NewtonReport& out_report);

}  // namespace spice::solver
