#include "analysis/analyses.hpp"

#include <algorithm>
#include <cmath>
#include <numbers>

namespace spice::analysis {

namespace {

using core::Diagnostic;
using core::StatusCode;
using dae::AnalysisMode;
using dae::EvalContext;
using dae::StampTarget;
using linalg::Complex;

struct OverrideGuard {
    devices::CompiledPlan& plan;
    std::string source_id;
    ~OverrideGuard() { (void)plan.set_dc_source_override(source_id, std::nullopt); }
};

/* Solve consistent t=0 state for transient startup (OperatingPoint or UIC). */
Diagnostic compute_transient_startup(
    const devices::CompiledPlan& plan,
    StartupMode mode,
    const AnalysisCommonOptions& common,
    std::vector<double>& out_x0) {
    const std::uint32_t dim = plan.unknown_count();
    if (mode == StartupMode::OperatingPoint) {
        /* Build temporary linear system at t=0 using transient t=0 source values. */
        std::vector<double> x_zero(dim, 0.0);
        std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
        std::vector<double> g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);
        EvalContext ctx{0.0, 300.15, AnalysisMode::Transient, x_zero};
        StampTarget target{f0, q0, g_vals, c_vals};
        if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;

        std::vector<double> rhs(dim);
        for (std::uint32_t i = 0; i < dim; ++i) rhs[i] = -f0[i];

        std::unique_ptr<linalg::RealLinearSolver> solver;
        if (auto d = linalg::create_real_solver(common.backend, solver); !d.ok()) return d;
        if (auto d = solver->analyze(plan.pattern); !d.ok()) return d;
        if (auto d = solver->factor(g_vals); !d.ok()) return d;
        out_x0.assign(dim, 0.0);
        if (auto d = solver->solve(rhs, out_x0); !d.ok()) return d;
        const double berr = linalg::real_backward_error(plan.pattern, g_vals, out_x0, rhs);
        if (berr > common.max_backward_error) {
            return Diagnostic::error(
                StatusCode::NumericalFailure, "transient operating-point startup backward error exceeded");
        }
        return Diagnostic::success();
    }

    /* UIC startup (ARCH-EQ-003): replace C with voltage source at ic_volts (default 0)
     * and L with current source at ic_amps (default 0), evaluate independent sources at t=0,
     * solve the algebraic startup network, and map back into the full MNA vector. */
    core::Circuit uic_ckt{};
    uic_ckt.name = plan.circuit.name + "_uic_t0";
    uic_ckt.nodes = plan.circuit.nodes;

    for (const auto& elem : plan.circuit.elements) {
        if (const auto* c = std::get_if<core::CapacitorSpec>(&elem)) {
            uic_ckt.elements.push_back(core::VoltageSourceSpec{
                "__uic_c_" + c->id, c->p, c->n, c->ic_volts.value_or(0.0), {0.0, 0.0}, std::nullopt});
        } else if (const auto* l = std::get_if<core::InductorSpec>(&elem)) {
            uic_ckt.elements.push_back(core::CurrentSourceSpec{
                "__uic_l_" + l->id, l->p, l->n, l->ic_amps.value_or(0.0), {0.0, 0.0}, std::nullopt});
        } else if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem)) {
            uic_ckt.elements.push_back(core::VoltageSourceSpec{
                v->id, v->p, v->n, core::source_value_at(*v, 0.0, true), {0.0, 0.0}, std::nullopt});
        } else if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem)) {
            uic_ckt.elements.push_back(core::CurrentSourceSpec{
                i->id, i->p, i->n, core::source_value_at(*i, 0.0, true), {0.0, 0.0}, std::nullopt});
        } else {
            uic_ckt.elements.push_back(elem);
        }
    }

    devices::CompiledPlan uic_plan{};
    if (auto d = devices::compile_circuit(uic_ckt, uic_plan, std::max<std::uint64_t>(dim * 2u, 1024u));
        !d.ok()) {
        return d;
    }

    AnalysisCommonOptions uic_opts = common;
    uic_opts.enable_audits = false;
    OpResult uic_op{};
    if (auto d = run_op(uic_plan, uic_opts, uic_op); !d.ok()) return d;

    out_x0.assign(dim, 0.0);
    const std::size_t n_node_unknowns = plan.circuit.nodes.size() - 1u;
    for (std::size_t i = 0; i < n_node_unknowns; ++i) {
        out_x0[i] = uic_op.values[i];
    }
    for (const auto& elem : plan.circuit.elements) {
        if (const auto* l = std::get_if<core::InductorSpec>(&elem)) {
            out_x0[plan.branch_unknowns.at(l->id).value] = l->ic_amps.value_or(0.0);
        } else if (std::holds_alternative<core::VoltageSourceSpec>(elem) ||
                   std::holds_alternative<core::VcvsSpec>(elem) ||
                   std::holds_alternative<core::CcvsSpec>(elem)) {
            const auto& id = core::element_id(elem);
            out_x0[plan.branch_unknowns.at(id).value] = uic_op.values[uic_plan.branch_unknowns.at(id).value];
        }
    }
    return Diagnostic::success();
}

}  // namespace

Diagnostic run_op(
    const devices::CompiledPlan& plan,
    const AnalysisCommonOptions& opts,
    OpResult& out_result) {
    const std::uint32_t dim = plan.unknown_count();
    std::vector<double> x_zero(dim, 0.0);
    std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
    std::vector<double> g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);

    EvalContext ctx{0.0, 300.15, AnalysisMode::OperatingPoint, x_zero};
    StampTarget target{f0, q0, g_vals, c_vals};
    if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;

    std::vector<double> rhs(dim);
    for (std::uint32_t i = 0; i < dim; ++i) rhs[i] = -f0[i];

    std::unique_ptr<linalg::RealLinearSolver> solver;
    if (auto d = linalg::create_real_solver(opts.backend, solver); !d.ok()) return d;
    if (auto d = solver->analyze(plan.pattern); !d.ok()) return d;
    if (auto d = solver->factor(g_vals); !d.ok()) return d;

    std::vector<double> x(dim, 0.0);
    if (auto d = solver->solve(rhs, x); !d.ok()) return d;

    const double berr = linalg::real_backward_error(plan.pattern, g_vals, x, rhs);
    if (berr > opts.max_backward_error) {
        return Diagnostic::error(
            StatusCode::NumericalFailure, "DC operating-point backward error exceeded tolerance");
    }

    OpResult res{};
    res.backend_name = solver->name();
    res.values = std::move(x);
    res.backward_error = berr;
    res.solver_stats = solver->stats();

    if (opts.enable_audits) {
        auto report = audit::audit_dc(plan, res.values, opts.audit_options);
        if (report.any_failed()) {
            return Diagnostic::error(StatusCode::NumericalFailure, "independent DC physical audit failed");
        }
        res.audit = std::move(report);
    }

    out_result = std::move(res);
    return Diagnostic::success();
}

Diagnostic run_dc_sweep(
    devices::CompiledPlan& plan,
    const DcSweepOptions& opts,
    DcSweepResult& out_result) {
    if (!std::isfinite(opts.start) || !std::isfinite(opts.stop) || !std::isfinite(opts.step) ||
        opts.step == 0.0) {
        return Diagnostic::error(StatusCode::InvalidArgument, "invalid DC sweep bounds or step");
    }
    const double span = opts.stop - opts.start;
    if ((span > 0.0 && opts.step < 0.0) || (span < 0.0 && opts.step > 0.0)) {
        return Diagnostic::error(StatusCode::InvalidArgument, "DC sweep step sign disagrees with range");
    }
    const double approx_steps = std::abs(span / opts.step);
    if (!std::isfinite(approx_steps) || approx_steps + 1.0 > static_cast<double>(opts.max_points)) {
        return Diagnostic::error(StatusCode::ResourceExhausted, "DC sweep exceeds max_points budget");
    }

    if (auto d = plan.set_dc_source_override(opts.source_id, opts.start); !d.ok()) return d;
    OverrideGuard guard{plan, opts.source_id};

    const std::uint32_t dim = plan.unknown_count();
    std::vector<double> x_zero(dim, 0.0);
    std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
    std::vector<double> g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);

    /* Factor linear Jacobian G once and reuse numeric factors across every sweep point. */
    EvalContext ctx{0.0, 300.15, AnalysisMode::DcSweep, x_zero};
    StampTarget target{f0, q0, g_vals, c_vals};
    if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;

    std::unique_ptr<linalg::RealLinearSolver> solver;
    if (auto d = linalg::create_real_solver(opts.common.backend, solver); !d.ok()) return d;
    if (auto d = solver->analyze(plan.pattern); !d.ok()) return d;
    if (auto d = solver->factor(g_vals); !d.ok()) return d;

    DcSweepResult res{};
    res.backend_name = solver->name();
    std::vector<double> rhs(dim);
    const std::size_t n_pts = static_cast<std::size_t>(std::floor(approx_steps + 1.0e-12)) + 1u;
    res.points.reserve(n_pts);

    for (std::size_t k = 0; k < n_pts; ++k) {
        double v = opts.start + static_cast<double>(k) * opts.step;
        if ((opts.step > 0.0 && v > opts.stop) || (opts.step < 0.0 && v < opts.stop)) {
            v = opts.stop;
        }
        if (auto d = plan.set_dc_source_override(opts.source_id, v); !d.ok()) return d;
        if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;
        for (std::uint32_t i = 0; i < dim; ++i) rhs[i] = -f0[i];

        std::vector<double> x(dim, 0.0);
        if (auto d = solver->solve(rhs, x); !d.ok()) return d;
        const double berr = linalg::real_backward_error(plan.pattern, g_vals, x, rhs);
        if (berr > opts.common.max_backward_error) {
            return Diagnostic::error(
                StatusCode::NumericalFailure, "DC sweep backward error exceeded tolerance");
        }
        res.worst_backward_error = std::max(res.worst_backward_error, berr);
        res.points.push_back(DcSweepPoint{v, std::move(x)});
    }

    res.solver_stats = solver->stats();
    out_result = std::move(res);
    return Diagnostic::success();
}

Diagnostic run_ac(
    const devices::CompiledPlan& plan,
    const AcOptions& opts,
    AcResult& out_result) {
    if (opts.frequencies_hz.empty()) {
        return Diagnostic::error(StatusCode::InvalidArgument, "AC requires at least one frequency");
    }
    for (double f : opts.frequencies_hz) {
        if (!std::isfinite(f) || f < 0.0) {
            return Diagnostic::error(StatusCode::InvalidArgument, "AC frequencies must be finite and >= 0");
        }
    }

    const std::uint32_t dim = plan.unknown_count();
    std::vector<double> x_zero(dim, 0.0);
    std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
    std::vector<double> g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);

    EvalContext ctx{0.0, 300.15, AnalysisMode::AcLinearize, x_zero};
    StampTarget target{f0, q0, g_vals, c_vals};
    if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;

    std::vector<Complex> rhs(dim);
    plan.fill_ac_rhs(rhs);

    std::unique_ptr<linalg::ComplexLinearSolver> solver;
    if (auto d = linalg::create_complex_solver(opts.common.backend, solver); !d.ok()) return d;
    if (auto d = solver->analyze(plan.pattern); !d.ok()) return d;

    AcResult res{};
    res.backend_name = solver->name();
    res.points.reserve(opts.frequencies_hz.size());

    std::vector<Complex> a_vals(plan.pattern.nnz());
    audit::AuditReport merged_audit{};

    for (double freq_hz : opts.frequencies_hz) {
        const double omega = 2.0 * std::numbers::pi * freq_hz;
        for (std::size_t k = 0; k < a_vals.size(); ++k) {
            a_vals[k] = Complex{g_vals[k], omega * c_vals[k]};
        }
        if (auto d = solver->factor(a_vals); !d.ok()) return d;

        std::vector<Complex> x(dim);
        if (auto d = solver->solve(rhs, x); !d.ok()) return d;

        const double berr = linalg::complex_backward_error(plan.pattern, a_vals, x, rhs);
        if (berr > opts.common.max_backward_error) {
            return Diagnostic::error(
                StatusCode::NumericalFailure, "AC complex backward error exceeded tolerance");
        }
        res.worst_backward_error = std::max(res.worst_backward_error, berr);

        if (opts.common.enable_audits) {
            auto pt_audit = audit::audit_ac(plan, x, freq_hz, opts.common.audit_options);
            if (pt_audit.any_failed()) {
                return Diagnostic::error(StatusCode::NumericalFailure, "independent AC physical audit failed");
            }
            merged_audit.merge_worst(pt_audit);
        }
        res.points.push_back(AcPoint{freq_hz, std::move(x)});
    }

    res.solver_stats = solver->stats();
    if (opts.common.enable_audits) {
        res.audit = std::move(merged_audit);
    }
    out_result = std::move(res);
    return Diagnostic::success();
}

Diagnostic run_transient(
    devices::CompiledPlan& plan,
    const TransientOptions& opts,
    TransientResult& out_result) {
    if (!std::isfinite(opts.step_s) || opts.step_s <= 0.0 ||
        !std::isfinite(opts.stop_s) || opts.stop_s <= 0.0) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "transient step_s and stop_s must be finite and positive");
    }

    /* Build exact time grid merging nominal steps with all source breakpoints (ARCH-EQ-005). */
    std::vector<double> breakpoints = plan.collect_breakpoints(0.0, opts.stop_s);
    std::vector<double> time_grid;
    time_grid.push_back(0.0);
    const double eps = 1.0e-12 * std::max(1.0, opts.stop_s);
    std::size_t bp_idx = 0;
    double t = 0.0;
    while (t + eps < opts.stop_s) {
        if (time_grid.size() > opts.max_steps) {
            return Diagnostic::error(StatusCode::ResourceExhausted, "transient exceeded max_steps budget");
        }
        double t_next = std::min(t + opts.step_s, opts.stop_s);
        while (bp_idx < breakpoints.size() && breakpoints[bp_idx] <= t + eps) {
            ++bp_idx;
        }
        if (bp_idx < breakpoints.size() && breakpoints[bp_idx] < t_next - eps) {
            t_next = breakpoints[bp_idx];
        }
        time_grid.push_back(t_next);
        t = t_next;
    }

    const std::uint32_t dim = plan.unknown_count();
    std::vector<double> x_prev;
    if (auto d = compute_transient_startup(plan, opts.startup, opts.common, x_prev); !d.ok()) {
        return d;
    }

    std::vector<double> f_prev(dim, 0.0), q_prev(dim, 0.0);
    std::vector<double> g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);
    {
        EvalContext ctx0{0.0, 300.15, AnalysisMode::Transient, x_prev};
        StampTarget target0{f_prev, q_prev, g_vals, c_vals};
        if (auto d = plan.evaluate_all(ctx0, target0); !d.ok()) return d;
        plan.commit_all_trials();
    }

    std::unique_ptr<linalg::RealLinearSolver> solver;
    if (auto d = linalg::create_real_solver(opts.common.backend, solver); !d.ok()) return d;
    if (auto d = solver->analyze(plan.pattern); !d.ok()) return d;

    TransientResult res{};
    res.backend_name = solver->name();
    res.samples.reserve(time_grid.size());
    res.samples.push_back(TransientSample{0.0, x_prev});

    std::vector<double> x_zero(dim, 0.0);
    std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
    std::vector<double> a_vals(plan.pattern.nnz(), 0.0);
    std::vector<double> rhs(dim, 0.0);
    std::vector<double> x_curr(dim, 0.0);
    std::vector<double> f_curr(dim, 0.0), q_curr(dim, 0.0);

    bool has_factor = false;
    double factored_h = 0.0;
    audit::AuditReport merged_audit{};

    for (std::size_t step_idx = 1; step_idx < time_grid.size(); ++step_idx) {
        const double t_prev = time_grid[step_idx - 1u];
        const double t_curr = time_grid[step_idx];
        const double h = t_curr - t_prev;

        /* Evaluate source contribution F(0, t_curr) and linear G, C. */
        EvalContext ctx_zero{t_curr, 300.15, AnalysisMode::Transient, x_zero};
        StampTarget target_zero{f0, q0, g_vals, c_vals};
        if (auto d = plan.evaluate_all(ctx_zero, target_zero); !d.ok()) return d;

        const double alpha =
            (opts.method == IntegrationMethod::BackwardEuler) ? (1.0 / h) : (2.0 / h);

        /* Exact-step numeric factor reuse whenever h is unchanged. */
        if (!has_factor || std::abs(h - factored_h) > 1.0e-14 * std::max(1.0, factored_h)) {
            for (std::size_t k = 0; k < a_vals.size(); ++k) {
                a_vals[k] = g_vals[k] + alpha * c_vals[k];
            }
            if (auto d = solver->factor(a_vals); !d.ok()) return d;
            has_factor = true;
            factored_h = h;
        }

        if (opts.method == IntegrationMethod::BackwardEuler) {
            /* (G + C/h) * x_n = -F(0, t_n) + Q(x_{n-1})/h */
            for (std::uint32_t i = 0; i < dim; ++i) {
                rhs[i] = -f0[i] + alpha * q_prev[i];
            }
        } else {
            /* Trapezoidal: (G + 2C/h) * x_n = -F(0, t_n) - F(x_{n-1}, t_{n-1}) + (2/h) * Q(x_{n-1}) */
            for (std::uint32_t i = 0; i < dim; ++i) {
                rhs[i] = -f0[i] - f_prev[i] + alpha * q_prev[i];
            }
        }

        if (auto d = solver->solve(rhs, x_curr); !d.ok()) return d;
        const double berr = linalg::real_backward_error(plan.pattern, a_vals, x_curr, rhs);
        if (berr > opts.common.max_backward_error) {
            plan.rollback_all_trials();
            return Diagnostic::error(
                StatusCode::NumericalFailure, "transient linear backward error exceeded tolerance");
        }
        res.worst_backward_error = std::max(res.worst_backward_error, berr);

        /* Re-evaluate exact F(x_curr, t_curr) and Q(x_curr) for next step history & audit. */
        EvalContext ctx_curr{t_curr, 300.15, AnalysisMode::Transient, x_curr};
        StampTarget target_curr{f_curr, q_curr, g_vals, c_vals};
        if (auto d = plan.evaluate_all(ctx_curr, target_curr); !d.ok()) {
            plan.rollback_all_trials();
            return d;
        }

        if (opts.common.enable_audits) {
            const auto audit_method =
                (opts.method == IntegrationMethod::BackwardEuler)
                    ? audit::TransientAuditMethod::BackwardEuler
                    : audit::TransientAuditMethod::Trapezoidal;
            auto step_audit = audit::audit_transient_step(
                plan, x_prev, x_curr, t_prev, t_curr, audit_method, opts.common.audit_options);
            if (step_audit.any_failed()) {
                plan.rollback_all_trials();
                return Diagnostic::error(
                    StatusCode::NumericalFailure, "independent transient physical audit failed");
            }
            merged_audit.merge_worst(step_audit);
        }

        plan.commit_all_trials();
        res.samples.push_back(TransientSample{t_curr, x_curr});
        x_prev.swap(x_curr);
        f_prev.swap(f_curr);
        q_prev.swap(q_curr);
    }

    res.solver_stats = solver->stats();
    if (opts.common.enable_audits) {
        res.audit = std::move(merged_audit);
    }
    out_result = std::move(res);
    return Diagnostic::success();
}

}  // namespace spice::analysis
