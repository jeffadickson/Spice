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

/* Check whether all elements in `plan` are covered by the standalone physical audit equations. */
bool plan_supports_dc_audit(const devices::CompiledPlan& plan) noexcept {
    for (const auto& e : plan.circuit.elements) {
        if (std::holds_alternative<core::BehavioralSourceSpec>(e) ||
            std::holds_alternative<core::OsdiInstanceSpec>(e)) {
            return false;
        }
    }
    return true;
}

bool plan_supports_linear_ac_or_tran_audit(const devices::CompiledPlan& plan) noexcept {
    if (plan.has_nonlinear) return false;
    for (const auto& e : plan.circuit.elements) {
        if (std::holds_alternative<core::MutualInductanceSpec>(e)) return false;
    }
    return true;
}

audit::AuditReport make_unavailable_audit(const std::string& reason) {
    audit::AuditReport rep;
    rep.checks.push_back(audit::AuditCheck{
        "physical_audit", audit::CheckStatus::NotChecked, 0.0, 0.0, "", reason});
    rep.checks.push_back(audit::AuditCheck{
        "global_accuracy",
        audit::CheckStatus::NotChecked,
        0.0,
        0.0,
        "",
        "physical consistency does not bound continuous-time global error (AUDIT-005)"});
    return rep;
}

/* Solve consistent t=0 state for transient startup (OperatingPoint or UIC). */
Diagnostic compute_transient_startup(
    devices::CompiledPlan& plan,
    StartupMode mode,
    const AnalysisCommonOptions& common,
    std::vector<double>& out_x0) {
    const std::uint32_t dim = plan.unknown_count();
    if (mode == StartupMode::OperatingPoint) {
        std::unique_ptr<linalg::RealLinearSolver> solver;
        if (auto d = linalg::create_real_solver(common.backend, solver); !d.ok()) return d;
        if (auto d = solver->analyze(plan.pattern); !d.ok()) return d;

        if (plan.has_nonlinear) {
            out_x0.assign(dim, 0.0);
            solver::NewtonProblem prob{};
            prob.time_s = 0.0;
            prob.mode = AnalysisMode::Transient;
            prob.alpha = 0.0;
            solver::NewtonReport nrep{};
            return solver::solve_newton(plan, *solver, prob, common.newton_options, out_x0, nrep);
        }

        std::vector<double> x_zero(dim, 0.0);
        std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
        std::vector<double> g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);
        EvalContext ctx{0.0, 300.15, AnalysisMode::Transient, x_zero};
        StampTarget target{f0, q0, g_vals, c_vals};
        if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;

        std::vector<double> rhs(dim);
        for (std::uint32_t i = 0; i < dim; ++i) rhs[i] = -f0[i];

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
     * and L with current source at ic_amps (default 0), evaluate independent sources at t=0. */
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
        } else if (std::holds_alternative<core::MutualInductanceSpec>(elem)) {
            /* Mutual inductance contributes only to dQ/dt, zero in t=0 algebraic UIC network. */
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
    for (std::size_t i = 0; i < plan.node_unknown_count && i < uic_op.values.size(); ++i) {
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
        } else if (const auto* b = std::get_if<core::BehavioralSourceSpec>(&elem); b != nullptr && b->is_voltage) {
            out_x0[plan.branch_unknowns.at(b->id).value] =
                uic_op.values[uic_plan.branch_unknowns.at(b->id).value];
        }
    }
    return Diagnostic::success();
}

}  // namespace

Diagnostic run_op(
    devices::CompiledPlan& plan,
    const AnalysisCommonOptions& opts,
    OpResult& out_result) {
    const std::uint32_t dim = plan.unknown_count();

    std::unique_ptr<linalg::RealLinearSolver> solver;
    if (auto d = linalg::create_real_solver(opts.backend, solver); !d.ok()) return d;
    if (auto d = solver->analyze(plan.pattern); !d.ok()) return d;

    OpResult res{};
    res.backend_name = solver->name();

    if (plan.has_nonlinear) {
        std::vector<double> x(dim, 0.0);
        solver::NewtonProblem prob{};
        prob.time_s = 0.0;
        prob.mode = AnalysisMode::OperatingPoint;
        prob.alpha = 0.0;
        solver::NewtonReport nrep{};
        if (auto d = solver::solve_newton(plan, *solver, prob, opts.newton_options, x, nrep); !d.ok()) {
            return d;
        }
        res.values = std::move(x);
        res.backward_error = nrep.worst_backward_error;
        res.newton = nrep;
        res.solver_stats = solver->stats();
    } else {
        std::vector<double> x_zero(dim, 0.0);
        std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
        std::vector<double> g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);
        EvalContext ctx{0.0, 300.15, AnalysisMode::OperatingPoint, x_zero};
        StampTarget target{f0, q0, g_vals, c_vals};
        if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;

        std::vector<double> rhs(dim);
        for (std::uint32_t i = 0; i < dim; ++i) rhs[i] = -f0[i];

        if (auto d = solver->factor(g_vals); !d.ok()) return d;
        std::vector<double> x(dim, 0.0);
        if (auto d = solver->solve(rhs, x); !d.ok()) return d;

        const double berr = linalg::real_backward_error(plan.pattern, g_vals, x, rhs);
        if (berr > opts.max_backward_error) {
            return Diagnostic::error(
                StatusCode::NumericalFailure, "DC operating-point backward error exceeded tolerance");
        }
        res.values = std::move(x);
        res.backward_error = berr;
        res.solver_stats = solver->stats();
    }

    if (opts.enable_audits) {
        if (plan_supports_dc_audit(plan)) {
            auto report = audit::audit_dc(plan, res.values, opts.audit_options);
            if (report.any_failed()) {
                return Diagnostic::error(StatusCode::NumericalFailure, "independent DC physical audit failed");
            }
            res.audit = std::move(report);
        } else {
            res.audit = make_unavailable_audit(
                "opaque OSDI or behavioral devices excluded from standalone DC physical audit");
        }
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
    std::unique_ptr<linalg::RealLinearSolver> solver;
    if (auto d = linalg::create_real_solver(opts.common.backend, solver); !d.ok()) return d;
    if (auto d = solver->analyze(plan.pattern); !d.ok()) return d;

    DcSweepResult res{};
    res.backend_name = solver->name();
    const std::size_t n_pts = static_cast<std::size_t>(std::floor(approx_steps + 1.0e-12)) + 1u;
    res.points.reserve(n_pts);

    if (plan.has_nonlinear) {
        std::vector<double> x_guess(dim, 0.0);
        for (std::size_t k = 0; k < n_pts; ++k) {
            double v = opts.start + static_cast<double>(k) * opts.step;
            if ((opts.step > 0.0 && v > opts.stop) || (opts.step < 0.0 && v < opts.stop)) v = opts.stop;
            if (auto d = plan.set_dc_source_override(opts.source_id, v); !d.ok()) return d;

            solver::NewtonProblem prob{};
            prob.time_s = 0.0;
            prob.mode = AnalysisMode::DcSweep;
            prob.alpha = 0.0;
            solver::NewtonReport nrep{};
            if (auto d = solver::solve_newton(plan, *solver, prob, opts.common.newton_options, x_guess, nrep);
                !d.ok()) {
                return d;
            }
            res.worst_backward_error = std::max(res.worst_backward_error, nrep.worst_backward_error);
            res.points.push_back(DcSweepPoint{v, x_guess});
        }
    } else {
        std::vector<double> x_zero(dim, 0.0);
        std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
        std::vector<double> g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);
        EvalContext ctx{0.0, 300.15, AnalysisMode::DcSweep, x_zero};
        StampTarget target{f0, q0, g_vals, c_vals};
        if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;
        if (auto d = solver->factor(g_vals); !d.ok()) return d;

        std::vector<double> rhs(dim);
        for (std::size_t k = 0; k < n_pts; ++k) {
            double v = opts.start + static_cast<double>(k) * opts.step;
            if ((opts.step > 0.0 && v > opts.stop) || (opts.step < 0.0 && v < opts.stop)) v = opts.stop;
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
    }

    res.solver_stats = solver->stats();
    out_result = std::move(res);
    return Diagnostic::success();
}

Diagnostic run_ac(
    devices::CompiledPlan& plan,
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
    std::vector<double> x_op(dim, 0.0);

    /* For nonlinear circuits, compute the DC operating point first and linearize G(x_op), C(x_op). */
    if (plan.has_nonlinear) {
        AnalysisCommonOptions op_opts = opts.common;
        op_opts.enable_audits = false;
        OpResult op{};
        if (auto d = run_op(plan, op_opts, op); !d.ok()) return d;
        x_op = std::move(op.values);
    }

    std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
    std::vector<double> g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);
    EvalContext ctx{0.0, 300.15, AnalysisMode::AcLinearize, x_op};
    StampTarget target{f0, q0, g_vals, c_vals};
    if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;

    std::vector<Complex> rhs(dim);
    plan.fill_ac_rhs(rhs);

    std::unique_ptr<linalg::ComplexLinearSolver> solver;
    if (auto d = linalg::create_complex_solver(opts.common.backend, solver); !d.ok()) return d;
    if (auto d = solver->analyze(plan.pattern); !d.ok()) return d;

    AcResult res{};
    res.backend_name = solver->name();
    res.operating_point = std::move(x_op);
    res.points.reserve(opts.frequencies_hz.size());

    std::vector<Complex> a_vals(plan.pattern.nnz());
    const bool can_audit_ac = plan_supports_linear_ac_or_tran_audit(plan);
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

        if (opts.common.enable_audits && can_audit_ac) {
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
        res.audit = can_audit_ac
                        ? std::move(merged_audit)
                        : make_unavailable_audit(
                              "nonlinear/coupled small-signal AC excluded from standalone linear AC audit");
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
    if (opts.adaptive) {
        if (!std::isfinite(opts.min_step_s) || opts.min_step_s <= 0.0 ||
            !std::isfinite(opts.max_step_s) || opts.max_step_s < 0.0 ||
            !std::isfinite(opts.lte_reltol) || opts.lte_reltol <= 0.0 ||
            !std::isfinite(opts.lte_abstol) || opts.lte_abstol <= 0.0) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "invalid adaptive transient LTE or step bounds");
        }
    }

    const double max_step =
        (opts.adaptive && opts.max_step_s > 0.0) ? opts.max_step_s : opts.step_s;
    const double min_step = std::min(opts.min_step_s, max_step);
    const double eps = 1.0e-12 * std::max(1.0, opts.stop_s);
    std::vector<double> breakpoints = plan.collect_breakpoints(0.0, opts.stop_s);

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
    res.samples.push_back(TransientSample{0.0, x_prev});

    const bool can_audit_tran =
        plan_supports_linear_ac_or_tran_audit(plan) && opts.method != IntegrationMethod::Bdf2;
    audit::AuditReport merged_audit{};

    /* Multi-step state history for BDF2 and Lagrange predictor/LTE estimation. */
    std::vector<double> x_prev2(dim, 0.0), x_prev3(dim, 0.0);
    std::vector<double> q_prev2(dim, 0.0);
    double t_prev = 0.0;
    double t_prev2 = 0.0;
    double t_prev3 = 0.0;
    std::size_t steps_since_restart = 0;

    std::vector<double> x_curr(dim, 0.0), x_pred(dim, 0.0);
    std::vector<double> f_curr(dim, 0.0), q_curr(dim, 0.0);
    std::vector<double> history(dim, 0.0);

    /* Linear fast-path scratch buffers (reuses LU factorization whenever alpha matches). */
    std::vector<double> x_zero(dim, 0.0);
    std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
    std::vector<double> a_vals(plan.pattern.nnz(), 0.0);
    std::vector<double> rhs(dim, 0.0);
    bool has_factor = false;
    double factored_alpha = 0.0;

    double h_desired = std::min(opts.step_s, max_step);
    std::size_t bp_idx = 0;

    while (t_prev + eps < opts.stop_s) {
        if (res.accepted_steps + res.rejected_steps >= opts.max_steps) {
            return Diagnostic::error(StatusCode::ResourceExhausted, "transient exceeded max_steps budget");
        }

        while (bp_idx < breakpoints.size() && breakpoints[bp_idx] <= t_prev + eps) {
            ++bp_idx;
        }
        const double next_bp =
            (bp_idx < breakpoints.size()) ? breakpoints[bp_idx] : opts.stop_s;
        const double target_t = std::min(next_bp, opts.stop_s);
        const bool target_is_bp = (bp_idx < breakpoints.size() && next_bp < opts.stop_s - eps);

        double h = opts.adaptive ? std::clamp(h_desired, min_step, max_step) : opts.step_s;
        bool truncated_to_target = false;
        bool hit_breakpoint = false;
        if (t_prev + h >= target_t - eps) {
            const double rem = target_t - t_prev;
            if (std::abs(rem - h) > 1.0e-9 * h) {
                h = rem;
                truncated_to_target = true;
            }
            hit_breakpoint = target_is_bp;
        }
        if (h <= 0.0) {
            break;
        }
        const double t_curr = (t_prev + h >= target_t - eps) ? target_t : (t_prev + h);

        /* Compute integration formula coefficients: F(x_n, t_n) + alpha * Q(x_n, t_n) + history = 0. */
        double alpha = 0.0;
        const bool use_bdf2_step =
            (opts.method == IntegrationMethod::Bdf2 && steps_since_restart >= 1u);
        const bool use_be_step =
            (opts.method == IntegrationMethod::BackwardEuler) ||
            (opts.method == IntegrationMethod::Bdf2 && steps_since_restart == 0u);

        if (use_be_step) {
            alpha = 1.0 / h;
            for (std::uint32_t i = 0; i < dim; ++i) {
                history[i] = -alpha * q_prev[i];
            }
        } else if (use_bdf2_step) {
            const double h_prev = t_prev - t_prev2;
            const double r = h / h_prev;
            const double a0 = (1.0 + 2.0 * r) / (h * (1.0 + r));
            const double a1 = -(1.0 + r) / h;
            const double a2 = (r * r) / (h * (1.0 + r));
            alpha = a0;
            for (std::uint32_t i = 0; i < dim; ++i) {
                history[i] = a1 * q_prev[i] + a2 * q_prev2[i];
            }
        } else {
            /* Trapezoidal: F_n + (2/h) Q_n + (F_{n-1} - (2/h) Q_{n-1}) = 0. */
            alpha = 2.0 / h;
            for (std::uint32_t i = 0; i < dim; ++i) {
                history[i] = f_prev[i] - alpha * q_prev[i];
            }
        }

        /* Compute Lagrange polynomial predictor for Milne LTE estimation. */
        double lte_scale = 0.0;
        int order = use_be_step ? 1 : 2;
        bool can_estimate_lte = false;
        if (steps_since_restart == 0u) {
            x_pred = x_prev;
        } else if (steps_since_restart == 1u || use_be_step) {
            const double h_prev = t_prev - t_prev2;
            const double slope_scale = h / h_prev;
            for (std::uint32_t i = 0; i < dim; ++i) {
                x_pred[i] = x_prev[i] + slope_scale * (x_prev[i] - x_prev2[i]);
            }
            if (use_be_step) {
                lte_scale = h / (h + h_prev);
                can_estimate_lte = true;
            }
        } else {
            const double d12 = t_prev - t_prev2;
            const double d13 = t_prev - t_prev3;
            const double d23 = t_prev2 - t_prev3;
            const double dt1 = t_curr - t_prev;
            const double dt2 = t_curr - t_prev2;
            const double dt3 = t_curr - t_prev3;
            const double l1 = (dt2 * dt3) / (d12 * d13);
            const double l2 = -(dt1 * dt3) / (d12 * d23);
            const double l3 = (dt1 * dt2) / (d13 * d23);
            for (std::uint32_t i = 0; i < dim; ++i) {
                x_pred[i] = l1 * x_prev[i] + l2 * x_prev2[i] + l3 * x_prev3[i];
            }
            if (use_bdf2_step) {
                const double r = h / d12;
                lte_scale = ((1.0 + r) * (1.0 + r) * h * h) / ((1.0 + 2.0 * r) * dt2 * dt3);
            } else {
                lte_scale = (h * h) / (2.0 * dt2 * dt3);
            }
            can_estimate_lte = true;
        }

        bool step_solve_ok = true;
        Diagnostic solve_diag = Diagnostic::success();

        if (plan.has_nonlinear) {
            /* Use safe previous accepted state as Newton warm-start to prevent exponential overshoot. */
            x_curr = x_prev;
            solver::NewtonProblem prob{};
            prob.time_s = t_curr;
            prob.mode = AnalysisMode::Transient;
            prob.alpha = alpha;
            prob.history = history;

            solver::NewtonReport nrep{};
            solve_diag = solver::solve_newton(plan, *solver, prob, opts.common.newton_options, x_curr, nrep);
            if (!solve_diag.ok()) {
                plan.rollback_all_trials();
                step_solve_ok = false;
            } else {
                res.total_newton_iterations += nrep.iterations;
                res.worst_backward_error = std::max(res.worst_backward_error, nrep.worst_backward_error);
            }
        } else {
            EvalContext ctx_zero{t_curr, 300.15, AnalysisMode::Transient, x_zero};
            StampTarget target_zero{f0, q0, g_vals, c_vals};
            if (auto d = plan.evaluate_all(ctx_zero, target_zero); !d.ok()) return d;

            if (!has_factor || std::abs(alpha - factored_alpha) > 1.0e-11 * std::max(1.0, std::abs(factored_alpha))) {
                for (std::size_t k = 0; k < a_vals.size(); ++k) {
                    a_vals[k] = g_vals[k] + alpha * c_vals[k];
                }
                if (auto d = solver->factor(a_vals); !d.ok()) return d;
                has_factor = true;
                factored_alpha = alpha;
            }

            for (std::uint32_t i = 0; i < dim; ++i) {
                rhs[i] = -f0[i] - history[i];
            }

            if (auto d = solver->solve(rhs, x_curr); !d.ok()) return d;
            const double berr = linalg::real_backward_error(plan.pattern, a_vals, x_curr, rhs);
            if (berr > opts.common.max_backward_error) {
                plan.rollback_all_trials();
                return Diagnostic::error(
                    StatusCode::NumericalFailure, "transient linear backward error exceeded tolerance");
            }
            res.worst_backward_error = std::max(res.worst_backward_error, berr);
        }

        if (!step_solve_ok) {
            if (opts.adaptive && h > min_step * 1.001) {
                ++res.rejected_steps;
                h_desired = std::max(min_step, 0.5 * h);
                continue;
            }
            return solve_diag;
        }

        /* Adaptive LTE acceptance test (ARCH-EQ-004). */
        double err_norm = 0.0;
        if (opts.adaptive && can_estimate_lte) {
            for (std::uint32_t i = 0; i < dim; ++i) {
                const double lte_i = lte_scale * std::abs(x_curr[i] - x_pred[i]);
                const double scale_i =
                    opts.lte_abstol + opts.lte_reltol * std::max(std::abs(x_curr[i]), std::abs(x_prev[i]));
                err_norm = std::max(err_norm, lte_i / scale_i);
            }
            if (err_norm > 1.0 && h > min_step * 1.001) {
                plan.rollback_all_trials();
                ++res.rejected_steps;
                const double factor = std::clamp(
                    0.85 * std::pow(err_norm, -1.0 / static_cast<double>(order + 1)), 0.1, 0.5);
                h_desired = std::max(min_step, h * factor);
                continue;
            }
        }

        EvalContext ctx_curr{t_curr, 300.15, AnalysisMode::Transient, x_curr};
        StampTarget target_curr{f_curr, q_curr, g_vals, c_vals};
        if (auto d = plan.evaluate_all(ctx_curr, target_curr); !d.ok()) {
            plan.rollback_all_trials();
            return d;
        }

        if (opts.common.enable_audits && can_audit_tran) {
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
        ++res.accepted_steps;
        res.min_step_used_s = (res.accepted_steps == 1u) ? h : std::min(res.min_step_used_s, h);
        res.max_step_used_s = std::max(res.max_step_used_s, h);

        /* Advance step-size proposal only on non-truncated steps so breakpoints don't collapse h. */
        if (opts.adaptive && can_estimate_lte && !truncated_to_target) {
            if (err_norm < 1.0e-12) {
                h_desired = std::min(max_step, 2.0 * h);
            } else {
                const double factor = std::clamp(
                    0.9 * std::pow(err_norm, -1.0 / static_cast<double>(order + 1)), 0.5, 2.0);
                h_desired = std::clamp(h * factor, min_step, max_step);
            }
        }

        /* Rotate multi-step history buffers. */
        x_prev3.swap(x_prev2);
        t_prev3 = t_prev2;
        x_prev2.swap(x_prev);
        q_prev2.swap(q_prev);
        t_prev2 = t_prev;
        x_prev.swap(x_curr);
        f_prev.swap(f_curr);
        q_prev.swap(q_curr);
        t_prev = t_curr;

        if (hit_breakpoint) {
            steps_since_restart = 0u;
        } else {
            ++steps_since_restart;
        }
    }

    res.solver_stats = solver->stats();
    if (opts.common.enable_audits) {
        if (can_audit_tran) {
            res.audit = std::move(merged_audit);
        } else if (opts.method == IntegrationMethod::Bdf2) {
            res.audit = make_unavailable_audit(
                "BDF2 multi-step transient excluded from 1-step BE/Trapezoidal discrete energy audit");
        } else {
            res.audit = make_unavailable_audit(
                "nonlinear/coupled transient excluded from standalone linear energy audit");
        }
    }
    out_result = std::move(res);
    return Diagnostic::success();
}

}  // namespace spice::analysis
