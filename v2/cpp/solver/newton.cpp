#include "solver/newton.hpp"

#include <algorithm>
#include <cmath>
#include <limits>
#include <vector>

namespace spice::solver {

namespace {

using core::Diagnostic;
using core::StatusCode;
using dae::EvalContext;
using dae::StampTarget;

struct SourceScaleGuard {
    devices::CompiledPlan& plan;
    ~SourceScaleGuard() { plan.set_source_scale(1.0); }
};

struct NewtonScratch {
    std::vector<double> f;
    std::vector<double> q;
    std::vector<double> f_lim;
    std::vector<double> q_lim;
    std::vector<double> g;
    std::vector<double> c;
    std::vector<double> jac;
    std::vector<double> res;
    std::vector<double> rhs;
    std::vector<double> dx;
    std::vector<double> x_cand;
    bool step_limited{false};

    void resize(std::uint32_t dim, std::size_t nnz) {
        f.resize(dim);
        q.resize(dim);
        f_lim.resize(dim);
        q_lim.resize(dim);
        g.resize(nnz);
        c.resize(nnz);
        jac.resize(nnz);
        res.resize(dim);
        rhs.resize(dim);
        dx.resize(dim);
        x_cand.resize(dim);
    }
};

/* Compute dimensional scaled infinity norm of residual R(x) (ARCH-EQ-002). */
double scaled_residual_norm(
    const devices::CompiledPlan& plan,
    std::span<const double> res,
    std::span<const double> x,
    const NewtonOptions& opts) noexcept {
    const std::size_t n_nodes = plan.node_unknown_count;
    double worst = 0.0;
    for (std::size_t i = 0; i < res.size(); ++i) {
        if (!std::isfinite(res[i])) return std::numeric_limits<double>::infinity();
        const double abstol = (i < n_nodes) ? opts.iabstol : opts.vabstol;
        const double scale = abstol + opts.reltol * std::max(1.0, std::abs(x[i]));
        worst = std::max(worst, std::abs(res[i]) / scale);
    }
    return worst;
}

/* Compute dimensional scaled infinity norm of Newton update dx (ARCH-EQ-002). */
double scaled_update_norm(
    const devices::CompiledPlan& plan,
    std::span<const double> x,
    std::span<const double> dx,
    const NewtonOptions& opts) noexcept {
    const std::size_t n_nodes = plan.node_unknown_count;
    double worst = 0.0;
    for (std::size_t i = 0; i < dx.size(); ++i) {
        if (!std::isfinite(dx[i])) return std::numeric_limits<double>::infinity();
        const double abstol = (i < n_nodes) ? opts.vabstol : opts.iabstol;
        const double mag = std::max(std::abs(x[i]), std::abs(x[i] + dx[i]));
        const double scale = abstol + opts.reltol * mag;
        worst = std::max(worst, std::abs(dx[i]) / scale);
    }
    return worst;
}

Diagnostic evaluate_residual_and_jacobian(
    const devices::CompiledPlan& plan,
    const NewtonProblem& problem,
    double gmin,
    std::span<const double> x,
    std::size_t newton_iter,
    bool enable_limiting,
    bool is_line_search_probe,
    NewtonScratch& s) {
    EvalContext ctx{problem.time_s, problem.temperature_k, problem.mode, x,
                    newton_iter, enable_limiting, is_line_search_probe};
    s.step_limited = false;
    StampTarget target{s.f, s.q, s.g, s.c, s.f_lim, s.q_lim, &s.step_limited};
    if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;

    const std::uint32_t dim = plan.unknown_count();
    const bool has_hist = (problem.history.size() == dim);
    for (std::uint32_t i = 0; i < dim; ++i) {
        double r = problem.f_scale * s.f[i] + problem.alpha * s.q[i];
        if (has_hist) r += problem.history[i];
        if (gmin > 0.0 && i < plan.node_unknown_count) {
            r += gmin * x[i];
        }
        if (!std::isfinite(r)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite Newton residual");
        }
        s.res[i] = r;
    }

    const std::size_t nnz = plan.pattern.nnz();
    for (std::size_t k = 0; k < nnz; ++k) {
        const double jv = problem.f_scale * s.g[k] + problem.alpha * s.c[k];
        if (!std::isfinite(jv)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite Newton Jacobian");
        }
        s.jac[k] = jv;
    }
    if (gmin > 0.0) {
        for (std::size_t i = 0; i < plan.node_diag_slots.size(); ++i) {
            if (plan.node_diag_slots[i].has_value()) {
                s.jac[plan.node_diag_slots[i]->value] += gmin;
            }
        }
    }
    return Diagnostic::success();
}

/* Single damped Newton stage at fixed (gmin, source_scale). */
Diagnostic run_newton_stage(
    const devices::CompiledPlan& plan,
    linalg::RealLinearSolver& linear_solver,
    const NewtonProblem& problem,
    const NewtonOptions& opts,
    double gmin,
    std::vector<double>& x,
    NewtonScratch& s,
    NewtonReport& report) {
    const std::uint32_t dim = plan.unknown_count();

    for (std::size_t iter = 0; iter < opts.max_iterations; ++iter) {
        if (auto d = evaluate_residual_and_jacobian(
                plan, problem, gmin, x, iter, plan.has_osdi, false, s);
            !d.ok()) {
            return d;
        }
        const bool was_limited = s.step_limited;
        const double res_norm = scaled_residual_norm(plan, s.res, x, opts);
        if (!std::isfinite(res_norm)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite scaled residual norm");
        }

        /* Include OSDI limiting RHS: J * dx = -(F(v_lim) + F_lim + alpha*(Q(v_lim) + Q_lim) + hist)
         * where F_lim = G(v_lim) * (x_solve - v_lim), Q_lim = C(v_lim) * (x_solve - v_lim). */
        for (std::uint32_t i = 0; i < dim; ++i) {
            const double lim_corr = problem.f_scale * s.f_lim[i] + problem.alpha * s.q_lim[i];
            s.rhs[i] = -(s.res[i] + lim_corr);
        }
        if (auto d = linear_solver.factor(s.jac); !d.ok()) return d;
        if (auto d = linear_solver.solve(s.rhs, s.dx); !d.ok()) return d;
        ++report.linear_solves;

        const double berr = linalg::real_backward_error(plan.pattern, s.jac, s.dx, s.rhs);
        report.worst_backward_error = std::max(report.worst_backward_error, berr);
        if (!std::isfinite(berr) || berr > opts.max_backward_error) {
            return Diagnostic::error(
                StatusCode::NumericalFailure, "Newton linear backward error exceeded tolerance");
        }

        const double upd_norm = scaled_update_norm(plan, x, s.dx, opts);
        ++report.iterations;

        /* Cap maximum node-voltage step size to avoid exponential overflow, then backtrack if needed. */
        double step_scale = 1.0;
        if (opts.max_voltage_step > 0.0) {
            double max_dv = 0.0;
            for (std::size_t i = 0; i < plan.node_unknown_count; ++i) {
                max_dv = std::max(max_dv, std::abs(s.dx[i]));
            }
            if (max_dv > opts.max_voltage_step) {
                step_scale = opts.max_voltage_step / max_dv;
            }
        }

        if (was_limited) {
            /* When OSDI $limit is active, res(x) was evaluated at v_lim != x; apply the limited
             * Newton step directly and force another iteration before testing convergence. */
            for (std::uint32_t i = 0; i < dim; ++i) {
                x[i] += step_scale * s.dx[i];
            }
        } else {
            /* Backtracking line search on scaled residual norm. */
            bool accepted = false;
            double best_cand_norm = std::numeric_limits<double>::infinity();
            std::vector<double> best_cand;

            double lambda = step_scale;
            for (int ls = 0; ls < 12; ++ls) {
                for (std::uint32_t i = 0; i < dim; ++i) {
                    s.x_cand[i] = x[i] + lambda * s.dx[i];
                }
                if (auto d = evaluate_residual_and_jacobian(
                        plan, problem, gmin, s.x_cand, iter + 1u, false, true, s);
                    d.ok()) {
                    const double cand_norm = scaled_residual_norm(plan, s.res, s.x_cand, opts);
                    if (std::isfinite(cand_norm)) {
                        if (cand_norm < best_cand_norm) {
                            best_cand_norm = cand_norm;
                            best_cand = s.x_cand;
                        }
                        if (cand_norm <= res_norm * (1.0 - 1.0e-4 * lambda) || cand_norm <= 1.0) {
                            x.swap(s.x_cand);
                            accepted = true;
                            break;
                        }
                    }
                }
                lambda *= 0.5;
            }
            if (!accepted) {
                if (!best_cand.empty()) {
                    x = std::move(best_cand);
                } else {
                    return Diagnostic::error(
                        StatusCode::NumericalFailure, "Newton line search failed to find finite candidate");
                }
            }
        }

        /* Re-verify true physical (unlimited) residual and update criteria at updated point x. */
        if (auto d = evaluate_residual_and_jacobian(
                plan, problem, gmin, x, iter + 1u, false, true, s);
            !d.ok()) {
            return d;
        }
        const double post_res_norm = scaled_residual_norm(plan, s.res, x, opts);
        report.max_scaled_residual = post_res_norm;
        report.max_scaled_update = upd_norm;

        if (!was_limited && post_res_norm <= 1.0 && upd_norm <= 1.0 && step_scale == 1.0) {
            return Diagnostic::success();
        }
    }

    return Diagnostic::error(
        StatusCode::NumericalFailure, "Newton iteration budget exhausted without convergence");
}

}  // namespace

Diagnostic solve_newton(
    devices::CompiledPlan& plan,
    linalg::RealLinearSolver& linear_solver,
    const NewtonProblem& problem,
    const NewtonOptions& opts,
    std::vector<double>& in_out_x,
    NewtonReport& out_report) {
    const std::uint32_t dim = plan.unknown_count();
    if (in_out_x.size() != dim) in_out_x.assign(dim, 0.0);

    out_report = NewtonReport{};
    NewtonScratch scratch;
    scratch.resize(dim, plan.pattern.nnz());

    /* Stage 1: Direct physical Newton (gmin = 0, source_scale = 1.0). */
    std::vector<double> x = in_out_x;
    if (auto d = run_newton_stage(plan, linear_solver, problem, opts, 0.0, x, scratch, out_report);
        d.ok()) {
        in_out_x = std::move(x);
        out_report.converged = true;
        out_report.final_gmin_siemens = 0.0;
        return Diagnostic::success();
    }

    /* Stage 2: Staged Gmin stepping ending with mandatory physical gmin = 0 solve (ARCH-EQ-002). */
    if (opts.enable_gmin_stepping) {
        out_report.used_gmin_continuation = true;
        std::vector<double> xg = in_out_x;
        const double gmin_schedule[] = {1.0e-3, 1.0e-6, 1.0e-9, 1.0e-12, 0.0};
        bool stage_ok = true;
        for (double gmin : gmin_schedule) {
            if (auto d = run_newton_stage(plan, linear_solver, problem, opts, gmin, xg, scratch, out_report);
                !d.ok()) {
                stage_ok = false;
                break;
            }
        }
        if (stage_ok) {
            in_out_x = std::move(xg);
            out_report.converged = true;
            out_report.final_gmin_siemens = 0.0;
            return Diagnostic::success();
        }
    }

    /* Stage 3: Source stepping from 0 -> 1.0, ending at exact source_scale = 1.0, gmin = 0. */
    if (opts.enable_source_stepping && problem.mode != dae::AnalysisMode::Transient) {
        out_report.used_source_continuation = true;
        SourceScaleGuard guard{plan};
        std::vector<double> xs(dim, 0.0);
        const double scale_schedule[] = {0.0, 0.1, 0.25, 0.5, 0.75, 1.0};
        bool stage_ok = true;
        for (double sc : scale_schedule) {
            plan.set_source_scale(sc);
            if (auto d = run_newton_stage(plan, linear_solver, problem, opts, 0.0, xs, scratch, out_report);
                !d.ok()) {
                stage_ok = false;
                break;
            }
        }
        if (stage_ok) {
            in_out_x = std::move(xs);
            out_report.converged = true;
            out_report.final_gmin_siemens = 0.0;
            return Diagnostic::success();
        }
    }

    out_report.converged = false;
    return Diagnostic::error(
        StatusCode::NumericalFailure, "Newton and continuation recovery failed to converge");
}

}  // namespace spice::solver
