#include "analysis/rf_steady_state.hpp"

#include "solver/newton.hpp"

#include <algorithm>
#include <cmath>
#include <complex>
#include <limits>
#include <memory>
#include <numbers>
#include <utility>
#include <vector>

namespace spice::analysis {

namespace {

using Complex = std::complex<double>;
using core::Diagnostic;
using core::StatusCode;
using dae::AnalysisMode;
using dae::EvalContext;
using dae::StampTarget;

/* Dense partial-pivoting linear solve A * x = b (row-major n x n). */
bool solve_dense_real(
    std::size_t n,
    std::vector<double> a,
    std::vector<double> b,
    std::vector<double>& out_x) {
    for (std::size_t k = 0; k < n; ++k) {
        std::size_t piv = k;
        double max_v = std::abs(a[k * n + k]);
        for (std::size_t i = k + 1u; i < n; ++i) {
            const double v = std::abs(a[i * n + k]);
            if (v > max_v) {
                max_v = v;
                piv = i;
            }
        }
        if (!std::isfinite(max_v) || max_v < 1.0e-18) return false;
        if (piv != k) {
            for (std::size_t j = k; j < n; ++j) std::swap(a[k * n + j], a[piv * n + j]);
            std::swap(b[k], b[piv]);
        }
        const double diag = a[k * n + k];
        for (std::size_t i = k + 1u; i < n; ++i) {
            const double f = a[i * n + k] / diag;
            a[i * n + k] = 0.0;
            for (std::size_t j = k + 1u; j < n; ++j) a[i * n + j] -= f * a[k * n + j];
            b[i] -= f * b[k];
        }
    }
    out_x.assign(n, 0.0);
    for (std::size_t ii = n; ii-- > 0;) {
        double s = b[ii];
        for (std::size_t j = ii + 1u; j < n; ++j) s -= a[ii * n + j] * out_x[j];
        out_x[ii] = s / a[ii * n + ii];
        if (!std::isfinite(out_x[ii])) return false;
    }
    return true;
}

double inf_norm(const std::vector<double>& v) noexcept {
    double m = 0.0;
    for (double x : v) m = std::max(m, std::abs(x));
    return m;
}

/* CSC SpMV: y = A * x */
void csc_matvec(
    const core::CscPattern& pat,
    std::span<const double> a_vals,
    std::span<const double> x,
    std::vector<double>& y) {
    y.assign(pat.dimension, 0.0);
    for (std::uint32_t c = 0; c < pat.dimension; ++c) {
        const double xc = x[c];
        if (xc == 0.0) continue;
        for (std::size_t k = pat.col_ptr[c]; k < pat.col_ptr[c + 1u]; ++k) {
            y[pat.row_idx[k]] += a_vals[k] * xc;
        }
    }
}

}  // namespace

/* ==================== 1. Harmonic Balance (.HB) ==================== */
Diagnostic run_hb(
    devices::CompiledPlan& plan,
    const HbOptions& opts,
    HbResult& out_result) {
    if (!std::isfinite(opts.fundamental_hz) || opts.fundamental_hz <= 0.0 || opts.harmonics == 0u) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".HB requires positive fundamental_hz and harmonics >= 1");
    }
    if (!plan.tline_device_indices.empty()) {
        return Diagnostic::error(
            StatusCode::Unsupported, ".HB currently requires lumped/OSDI DAE devices (delay T-lines not yet supported in HB)");
    }

    const std::size_t n = plan.unknown_count();
    const std::size_t h_max = opts.harmonics;
    const std::size_t k_modes = 2u * h_max + 1u; /* [dc, cos1, sin1, ..., cosH, sinH] */
    const std::size_t n_hb = n * k_modes;
    if (n_hb > 1500u) {
        return Diagnostic::error(StatusCode::ResourceExhausted, ".HB dense Fourier-Newton budget exceeded (N*(2H+1) > 1500)");
    }

    const double omega0 = 2.0 * std::numbers::pi * opts.fundamental_hz;
    const double period = 1.0 / opts.fundamental_hz;

    /* Build exact collocation IFT (gamma: k_modes x k_modes), DFT (gamma_inv), and (Omega * gamma_inv). */
    std::vector<double> gamma(k_modes * k_modes, 0.0);
    std::vector<double> gamma_inv(k_modes * k_modes, 0.0);
    std::vector<double> omega_gamma_inv(k_modes * k_modes, 0.0);
    std::vector<double> times(k_modes, 0.0);

    for (std::size_t m = 0; m < k_modes; ++m) {
        const double th = 2.0 * std::numbers::pi * static_cast<double>(m) / static_cast<double>(k_modes);
        times[m] = period * static_cast<double>(m) / static_cast<double>(k_modes);

        gamma[m * k_modes + 0u] = 1.0;
        gamma_inv[0u * k_modes + m] = 1.0 / static_cast<double>(k_modes);

        for (std::size_t h = 1u; h <= h_max; ++h) {
            const std::size_t ic = 2u * h - 1u;
            const std::size_t is = 2u * h;
            const double c = std::cos(static_cast<double>(h) * th);
            const double s = std::sin(static_cast<double>(h) * th);
            gamma[m * k_modes + ic] = c;
            gamma[m * k_modes + is] = s;

            const double scale = 2.0 / static_cast<double>(k_modes);
            gamma_inv[ic * k_modes + m] = scale * c;
            gamma_inv[is * k_modes + m] = scale * s;

            /* d/dt (a_h cos + b_h sin) = (+h*w0*b_h) cos + (-h*w0*a_h) sin */
            const double hw = static_cast<double>(h) * omega0;
            omega_gamma_inv[ic * k_modes + m] = +hw * scale * s;
            omega_gamma_inv[is * k_modes + m] = -hw * scale * c;
        }
    }

    /* Seed DC harmonic from DC operating point. */
    std::vector<double> x_hat(n_hb, 0.0);
    {
        AnalysisCommonOptions op_opts = opts.common;
        op_opts.enable_audits = false;
        OpResult op{};
        if (run_op(plan, op_opts, op).ok()) {
            for (std::size_t i = 0; i < n; ++i) {
                x_hat[i * k_modes + 0u] = op.values[i];
            }
        }
    }

    const std::size_t nnz = plan.pattern.nnz();
    std::vector<std::vector<double>> x_t(k_modes, std::vector<double>(n, 0.0));
    std::vector<std::vector<double>> f_t(k_modes, std::vector<double>(n, 0.0));
    std::vector<std::vector<double>> q_t(k_modes, std::vector<double>(n, 0.0));
    std::vector<std::vector<double>> g_t(k_modes, std::vector<double>(nnz, 0.0));
    std::vector<std::vector<double>> c_t(k_modes, std::vector<double>(nnz, 0.0));

    auto eval_hb_residual = [&](const std::vector<double>& xh, std::vector<double>& out_r, bool need_jac) -> Diagnostic {
        for (std::size_t m = 0; m < k_modes; ++m) {
            for (std::size_t i = 0; i < n; ++i) {
                double v = 0.0;
                for (std::size_t k = 0; k < k_modes; ++k) {
                    v += gamma[m * k_modes + k] * xh[i * k_modes + k];
                }
                x_t[m][i] = v;
            }
            EvalContext ctx{times[m], 300.15, AnalysisMode::Transient, x_t[m]};
            StampTarget tgt{f_t[m], q_t[m], g_t[m], c_t[m]};
            if (auto d = plan.evaluate_all(ctx, tgt); !d.ok()) return d;
            (void)need_jac;
        }

        out_r.assign(n_hb, 0.0);
        for (std::size_t i = 0; i < n; ++i) {
            for (std::size_t k = 0; k < k_modes; ++k) {
                double val = 0.0;
                for (std::size_t m = 0; m < k_modes; ++m) {
                    val += gamma_inv[k * k_modes + m] * f_t[m][i] +
                           omega_gamma_inv[k * k_modes + m] * q_t[m][i];
                }
                out_r[i * k_modes + k] = val;
            }
        }
        return Diagnostic::success();
    };

    std::vector<double> residual(n_hb, 0.0);
    if (auto d = eval_hb_residual(x_hat, residual, true); !d.ok()) return d;
    double res_norm = inf_norm(residual);

    std::vector<double> jac(n_hb * n_hb, 0.0);
    std::vector<double> neg_res(n_hb, 0.0);
    std::vector<double> dx(n_hb, 0.0);
    std::vector<double> x_trial(n_hb, 0.0);
    std::vector<double> r_trial(n_hb, 0.0);

    bool converged = (res_norm <= opts.residual_tol);
    std::size_t iter = 0;
    while (!converged && iter < opts.max_iterations) {
        ++iter;
        std::fill(jac.begin(), jac.end(), 0.0);

        /* Exact analytical Fourier-Newton Jacobian over CSC nonzeros:
         *   J_{(i, k1), (j, k2)} = sum_m (Gamma^-1[k1,m]*G_ij(t_m) + (Omega*Gamma^-1)[k1,m]*C_ij(t_m)) * Gamma[m,k2].
         */
        for (std::uint32_t col = 0; col < plan.unknown_count(); ++col) {
            for (std::size_t slot = plan.pattern.col_ptr[col]; slot < plan.pattern.col_ptr[col + 1u]; ++slot) {
                const std::uint32_t row = plan.pattern.row_idx[slot];
                for (std::size_t k1 = 0; k1 < k_modes; ++k1) {
                    const std::size_t r_idx = static_cast<std::size_t>(row) * k_modes + k1;
                    for (std::size_t k2 = 0; k2 < k_modes; ++k2) {
                        const std::size_t c_idx = static_cast<std::size_t>(col) * k_modes + k2;
                        double acc = 0.0;
                        for (std::size_t m = 0; m < k_modes; ++m) {
                            acc += (gamma_inv[k1 * k_modes + m] * g_t[m][slot] +
                                    omega_gamma_inv[k1 * k_modes + m] * c_t[m][slot]) *
                                   gamma[m * k_modes + k2];
                        }
                        jac[r_idx * n_hb + c_idx] += acc;
                    }
                }
            }
        }

        for (std::size_t i = 0; i < n_hb; ++i) neg_res[i] = -residual[i];
        if (!solve_dense_real(n_hb, jac, neg_res, dx)) {
            return Diagnostic::error(StatusCode::NumericalFailure, ".HB Fourier-Newton Jacobian is singular");
        }

        /* Clamp step for steep exponential devices and backtrack if needed. */
        double step_scale = 1.0;
        const double max_dx = inf_norm(dx);
        if (max_dx > 0.5) step_scale = 0.5 / max_dx;

        bool accepted = false;
        double trial_norm = res_norm;
        for (int ls = 0; ls < 12; ++ls) {
            for (std::size_t i = 0; i < n_hb; ++i) x_trial[i] = x_hat[i] + step_scale * dx[i];
            if (eval_hb_residual(x_trial, r_trial, true).ok()) {
                trial_norm = inf_norm(r_trial);
                if (trial_norm < res_norm || trial_norm <= opts.residual_tol) {
                    accepted = true;
                    break;
                }
            }
            step_scale *= 0.5;
        }
        if (!accepted) {
            /* Take damped step and continue to allow Newton basin entry. */
            for (std::size_t i = 0; i < n_hb; ++i) x_trial[i] = x_hat[i] + step_scale * dx[i];
            if (auto d = eval_hb_residual(x_trial, r_trial, true); !d.ok()) return d;
            trial_norm = inf_norm(r_trial);
        }

        x_hat.swap(x_trial);
        residual.swap(r_trial);
        res_norm = trial_norm;
        if (res_norm <= opts.residual_tol && step_scale * max_dx <= opts.update_tol) {
            converged = true;
        }
    }

    if (!converged) {
        return Diagnostic::error(StatusCode::NumericalFailure, ".HB failed to converge within max_iterations");
    }

    HbResult res{};
    res.converged = true;
    res.iterations = iter;
    res.fundamental_hz = opts.fundamental_hz;
    res.harmonics = h_max;
    res.final_residual_norm = res_norm;
    res.phasors.assign(n, std::vector<Complex>(h_max + 1u, Complex{0.0, 0.0}));
    for (std::size_t i = 0; i < n; ++i) {
        res.phasors[i][0] = Complex{x_hat[i * k_modes + 0u], 0.0};
        for (std::size_t h = 1u; h <= h_max; ++h) {
            const double a_h = x_hat[i * k_modes + (2u * h - 1u)];
            const double b_h = x_hat[i * k_modes + (2u * h)];
            res.phasors[i][h] = Complex{a_h, -b_h};
        }
    }
    res.waveform.reserve(k_modes);
    for (std::size_t m = 0; m < k_modes; ++m) {
        res.waveform.push_back(TransientSample{times[m], x_t[m]});
    }

    out_result = std::move(res);
    return Diagnostic::success();
}

/* ==================== 2. Driven Periodic Steady-State Shooting (.PSS) ==================== */
Diagnostic run_pss(
    devices::CompiledPlan& plan,
    const PssOptions& opts,
    PssResult& out_result) {
    if (!std::isfinite(opts.fundamental_hz) || opts.fundamental_hz <= 0.0 || opts.steps_per_period < 4u) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".PSS requires positive fundamental_hz and steps_per_period >= 4");
    }
    if (!plan.tline_device_indices.empty()) {
        return Diagnostic::error(StatusCode::Unsupported, ".PSS shooting currently requires lumped/OSDI DAE devices");
    }

    const std::size_t n = plan.unknown_count();
    const double period = 1.0 / opts.fundamental_hz;
    const double h = period / static_cast<double>(opts.steps_per_period);
    const bool use_trap = (opts.method == IntegrationMethod::Trapezoidal);
    const double alpha = use_trap ? (2.0 / h) : (1.0 / h);

    std::unique_ptr<linalg::RealLinearSolver> lin_solver;
    if (auto d = linalg::create_real_solver(opts.common.backend, lin_solver); !d.ok()) return d;
    if (auto d = lin_solver->analyze(plan.pattern); !d.ok()) return d;

    /* Initialize x0 from DC operating point. */
    std::vector<double> x0(n, 0.0);
    {
        AnalysisCommonOptions op_opts = opts.common;
        op_opts.enable_audits = false;
        OpResult op{};
        if (run_op(plan, op_opts, op).ok()) x0 = std::move(op.values);
    }

    const std::size_t nnz = plan.pattern.nnz();
    std::vector<double> f_prev(n, 0.0), q_prev(n, 0.0), f_curr(n, 0.0), q_curr(n, 0.0);
    std::vector<double> g_prev(nnz, 0.0), c_prev(nnz, 0.0), g_curr(nnz, 0.0), c_curr(nnz, 0.0);
    std::vector<double> j_step(nnz, 0.0), history(n, 0.0), rhs_s(n, 0.0), col_out(n, 0.0);

    std::vector<TransientSample> traj;
    std::vector<double> monodromy(n * n, 0.0); /* Column-major: col c stored at sens[c][row] */
    std::vector<std::vector<double>> sens_prev(n, std::vector<double>(n, 0.0));
    std::vector<std::vector<double>> sens_curr(n, std::vector<double>(n, 0.0));

    auto shoot_one_period = [&](const std::vector<double>& x_start,
                                std::vector<double>& x_end,
                                bool compute_monodromy) -> Diagnostic {
        traj.clear();
        traj.reserve(opts.steps_per_period + 1u);

        std::vector<double> x_prev = x_start;
        {
            EvalContext ctx0{0.0, 300.15, AnalysisMode::Transient, x_prev};
            StampTarget tgt0{f_prev, q_prev, g_prev, c_prev};
            if (auto d = plan.evaluate_all(ctx0, tgt0); !d.ok()) return d;
            plan.commit_all_trials();
        }
        traj.push_back(TransientSample{0.0, x_prev});

        if (compute_monodromy) {
            for (std::size_t c = 0; c < n; ++c) {
                std::fill(sens_prev[c].begin(), sens_prev[c].end(), 0.0);
                sens_prev[c][c] = 1.0;
            }
        }

        std::vector<double> x_curr(n, 0.0);
        for (std::size_t step = 1u; step <= opts.steps_per_period; ++step) {
            const double t_curr = static_cast<double>(step) * h;
            if (use_trap) {
                for (std::size_t i = 0; i < n; ++i) history[i] = f_prev[i] - alpha * q_prev[i];
            } else {
                for (std::size_t i = 0; i < n; ++i) history[i] = -alpha * q_prev[i];
            }

            x_curr = x_prev;
            solver::NewtonProblem prob{};
            prob.time_s = t_curr;
            prob.mode = AnalysisMode::Transient;
            prob.alpha = alpha;
            prob.history = history;

            solver::NewtonReport nrep{};
            if (auto d = solver::solve_newton(plan, *lin_solver, prob, opts.common.newton_options, x_curr, nrep);
                !d.ok()) {
                plan.rollback_all_trials();
                return d;
            }

            EvalContext ctx_c{t_curr, 300.15, AnalysisMode::Transient, x_curr};
            StampTarget tgt_c{f_curr, q_curr, g_curr, c_curr};
            if (auto d = plan.evaluate_all(ctx_c, tgt_c); !d.ok()) {
                plan.rollback_all_trials();
                return d;
            }
            plan.commit_all_trials();

            if (compute_monodromy) {
                /* Exact tangent propagation: (G_k + alpha C_k) S_k = (alpha C_{k-1} [- G_{k-1}]) S_{k-1}. */
                for (std::size_t k = 0; k < nnz; ++k) j_step[k] = g_curr[k] + alpha * c_curr[k];
                if (auto d = lin_solver->factor(j_step); !d.ok()) return d;

                std::vector<double> rhs_mat(nnz, 0.0);
                if (use_trap) {
                    for (std::size_t k = 0; k < nnz; ++k) rhs_mat[k] = alpha * c_prev[k] - g_prev[k];
                } else {
                    for (std::size_t k = 0; k < nnz; ++k) rhs_mat[k] = alpha * c_prev[k];
                }

                for (std::size_t c = 0; c < n; ++c) {
                    csc_matvec(plan.pattern, rhs_mat, sens_prev[c], rhs_s);
                    if (auto d = lin_solver->solve(rhs_s, sens_curr[c]); !d.ok()) return d;
                }
                sens_prev.swap(sens_curr);
            }

            traj.push_back(TransientSample{t_curr, x_curr});
            x_prev.swap(x_curr);
            f_prev.swap(f_curr);
            q_prev.swap(q_curr);
            g_prev.swap(g_curr);
            c_prev.swap(c_curr);
        }

        x_end = x_prev;
        if (compute_monodromy) {
            for (std::size_t r = 0; r < n; ++r) {
                for (std::size_t c = 0; c < n; ++c) {
                    monodromy[r * n + c] = sens_prev[c][r];
                }
            }
        }
        return Diagnostic::success();
    };

    /* Do one warm-up cycle from DC OP so initial algebraic/reactive jump settles near limit cycle. */
    std::vector<double> x_end(n, 0.0);
    if (auto d = shoot_one_period(x0, x_end, false); !d.ok()) return d;
    x0 = x_end;

    bool converged = false;
    std::size_t iter = 0;
    double err_norm = 0.0;

    while (iter < opts.max_shooting_iterations) {
        ++iter;
        if (auto d = shoot_one_period(x0, x_end, true); !d.ok()) return d;

        std::vector<double> r_shoot(n, 0.0);
        for (std::size_t i = 0; i < n; ++i) r_shoot[i] = x_end[i] - x0[i];
        err_norm = inf_norm(r_shoot);
        if (err_norm <= opts.periodic_tol) {
            converged = true;
            break;
        }

        /* Solve (M - I) * dx0 = -(x(T) - x0). */
        std::vector<double> m_minus_i = monodromy;
        for (std::size_t i = 0; i < n; ++i) m_minus_i[i * n + i] -= 1.0;
        std::vector<double> neg_r(n, 0.0);
        for (std::size_t i = 0; i < n; ++i) neg_r[i] = -r_shoot[i];

        std::vector<double> dx0(n, 0.0);
        if (!solve_dense_real(n, std::move(m_minus_i), std::move(neg_r), dx0)) {
            return Diagnostic::error(StatusCode::NumericalFailure, ".PSS shooting Jacobian (M - I) is singular");
        }

        double scale = 1.0;
        const double max_dx = inf_norm(dx0);
        if (max_dx > 1.0) scale = 1.0 / max_dx;

        std::vector<double> x0_trial(n, 0.0);
        std::vector<double> x_end_trial(n, 0.0);
        bool step_ok = false;
        for (int ls = 0; ls < 8; ++ls) {
            for (std::size_t i = 0; i < n; ++i) x0_trial[i] = x0[i] + scale * dx0[i];
            if (shoot_one_period(x0_trial, x_end_trial, false).ok()) {
                double trial_err = 0.0;
                for (std::size_t i = 0; i < n; ++i) {
                    trial_err = std::max(trial_err, std::abs(x_end_trial[i] - x0_trial[i]));
                }
                if (trial_err < err_norm || trial_err <= opts.periodic_tol) {
                    x0.swap(x0_trial);
                    step_ok = true;
                    break;
                }
            }
            scale *= 0.5;
        }
        if (!step_ok) {
            for (std::size_t i = 0; i < n; ++i) x0[i] += scale * dx0[i];
        }
    }

    if (!converged) {
        return Diagnostic::error(StatusCode::NumericalFailure, ".PSS shooting did not converge within max_shooting_iterations");
    }

    PssResult res{};
    res.converged = true;
    res.shooting_iterations = iter;
    res.fundamental_hz = opts.fundamental_hz;
    res.periodicity_error = err_norm;
    res.waveform = std::move(traj);
    out_result = std::move(res);
    return Diagnostic::success();
}

}  // namespace spice::analysis
