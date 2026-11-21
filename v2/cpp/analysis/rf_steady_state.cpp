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

namespace {

constexpr double kBoltzmannRf = 1.380649e-23;     /* J/K (exact SI 2019) */
constexpr double kElemChargeRf = 1.602176634e-19; /* C (exact SI 2019) */

/* Dense partial-pivoting complex linear solve A * x = b (row-major n x n). */
bool solve_dense_complex(
    std::size_t n,
    std::vector<Complex> a,
    std::vector<Complex> b,
    std::vector<Complex>& out_x) {
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
        const Complex diag = a[k * n + k];
        for (std::size_t i = k + 1u; i < n; ++i) {
            const Complex f = a[i * n + k] / diag;
            a[i * n + k] = {0.0, 0.0};
            for (std::size_t j = k + 1u; j < n; ++j) a[i * n + j] -= f * a[k * n + j];
            b[i] -= f * b[k];
        }
    }
    out_x.assign(n, Complex{0.0, 0.0});
    for (std::size_t ii = n; ii-- > 0;) {
        Complex s = b[ii];
        for (std::size_t j = ii + 1u; j < n; ++j) s -= a[ii * n + j] * out_x[j];
        out_x[ii] = s / a[ii * n + ii];
        if (!std::isfinite(out_x[ii].real()) || !std::isfinite(out_x[ii].imag())) return false;
    }
    return true;
}

struct ConversionMatrixData {
    std::size_t n{0};
    std::size_t h_max{0};
    std::size_t s_count{0}; /* 2*H + 1 */
    std::size_t dim{0};     /* n * s_count */
    std::vector<Complex> y_conv; /* Row-major dim x dim */
};

/*
 * Build exact frequency-domain conversion matrix Y_conv around a converged .HB steady state:
 *   Y_conv[(row*S + idx1), (col*S + idx2)] = G_hat(k1 - k2) + j*(w_s + k1*w_0)*C_hat(k1 - k2),
 * where sidebands k1, k2 run over [-H, +H] mapped to idx = k + H.
 */
Diagnostic build_conversion_matrix(
    devices::CompiledPlan& plan,
    const HbResult& hb,
    double small_signal_freq_hz,
    double temperature_k,
    ConversionMatrixData& out_cm) {
    if (!hb.converged || hb.fundamental_hz <= 0.0 || hb.harmonics == 0u) {
        return Diagnostic::error(StatusCode::InvalidArgument, "periodic small-signal analysis requires a converged .HB operating point");
    }
    const std::size_t n = plan.unknown_count();
    const std::size_t h_max = hb.harmonics;
    const std::size_t s_count = 2u * h_max + 1u;
    if (hb.waveform.size() != s_count) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".HB waveform sample count does not match 2*H + 1");
    }
    const std::size_t dim = n * s_count;
    if (dim > 1500u) {
        return Diagnostic::error(StatusCode::ResourceExhausted, "conversion matrix budget exceeded (N*(2H+1) > 1500)");
    }
    if (!std::isfinite(small_signal_freq_hz) || small_signal_freq_hz < 0.0) {
        return Diagnostic::error(StatusCode::InvalidArgument, "small_signal_freq_hz must be finite and >= 0");
    }

    const std::size_t nnz = plan.pattern.nnz();
    std::vector<std::vector<double>> g_t(s_count, std::vector<double>(nnz, 0.0));
    std::vector<std::vector<double>> c_t(s_count, std::vector<double>(nnz, 0.0));
    std::vector<double> f_tmp(n, 0.0), q_tmp(n, 0.0);

    for (std::size_t m = 0; m < s_count; ++m) {
        if (hb.waveform[m].values.size() != n) {
            return Diagnostic::error(StatusCode::InvalidArgument, ".HB waveform unknown dimension mismatch");
        }
        EvalContext ctx{hb.waveform[m].time_s, temperature_k, AnalysisMode::Transient, hb.waveform[m].values};
        StampTarget tgt{f_tmp, q_tmp, g_t[m], c_t[m]};
        if (auto d = plan.evaluate_all(ctx, tgt); !d.ok()) return d;
    }

    /* Precompute DFT twiddle table W[delta_mod][m] = (1/S) * exp(-j * 2*pi * delta * m / S). */
    std::vector<std::vector<Complex>> twiddle(s_count, std::vector<Complex>(s_count));
    const double inv_s = 1.0 / static_cast<double>(s_count);
    for (std::size_t r = 0; r < s_count; ++r) {
        for (std::size_t m = 0; m < s_count; ++m) {
            const double ang = -2.0 * std::numbers::pi * static_cast<double>(r * m) * inv_s;
            twiddle[r][m] = Complex{inv_s * std::cos(ang), inv_s * std::sin(ang)};
        }
    }

    const double omega0 = 2.0 * std::numbers::pi * hb.fundamental_hz;
    const double omegas = 2.0 * std::numbers::pi * small_signal_freq_hz;

    out_cm.n = n;
    out_cm.h_max = h_max;
    out_cm.s_count = s_count;
    out_cm.dim = dim;
    out_cm.y_conv.assign(dim * dim, Complex{0.0, 0.0});

    std::vector<Complex> g_hat(s_count);
    std::vector<Complex> c_hat(s_count);
    const int h_int = static_cast<int>(h_max);
    const int s_int = static_cast<int>(s_count);

    for (std::uint32_t col = 0; col < plan.unknown_count(); ++col) {
        for (std::size_t slot = plan.pattern.col_ptr[col]; slot < plan.pattern.col_ptr[col + 1u]; ++slot) {
            const std::uint32_t row = plan.pattern.row_idx[slot];

            for (std::size_t r = 0; r < s_count; ++r) {
                Complex gs{0.0, 0.0};
                Complex cs{0.0, 0.0};
                for (std::size_t m = 0; m < s_count; ++m) {
                    gs += twiddle[r][m] * g_t[m][slot];
                    cs += twiddle[r][m] * c_t[m][slot];
                }
                g_hat[r] = gs;
                c_hat[r] = cs;
            }

            for (std::size_t idx1 = 0; idx1 < s_count; ++idx1) {
                const int k1 = static_cast<int>(idx1) - h_int;
                const double w_side = omegas + static_cast<double>(k1) * omega0;
                const std::size_t r_full = static_cast<std::size_t>(row) * s_count + idx1;
                for (std::size_t idx2 = 0; idx2 < s_count; ++idx2) {
                    const int k2 = static_cast<int>(idx2) - h_int;
                    int diff = (k1 - k2) % s_int;
                    if (diff < 0) diff += s_int;
                    const std::size_t r_mod = static_cast<std::size_t>(diff);
                    const std::size_t c_full = static_cast<std::size_t>(col) * s_count + idx2;
                    out_cm.y_conv[r_full * dim + c_full] +=
                        g_hat[r_mod] + Complex{0.0, w_side} * c_hat[r_mod];
                }
            }
        }
    }
    return Diagnostic::success();
}

std::vector<Complex> transpose_dense_complex(std::size_t dim, const std::vector<Complex>& m) {
    std::vector<Complex> mt(dim * dim);
    for (std::size_t r = 0; r < dim; ++r) {
        for (std::size_t c = 0; c < dim; ++c) {
            mt[c * dim + r] = m[r * dim + c];
        }
    }
    return mt;
}

double node_val_rf(const std::vector<double>& x, core::NodeId node) noexcept {
    if (auto u = devices::CompiledPlan::node_unknown(node)) return x[u->value];
    return 0.0;
}

}  // namespace

/* ==================== 3. Periodic Small-Signal AC (.PAC) ==================== */
Diagnostic run_pac(
    devices::CompiledPlan& plan,
    const HbResult& hb_operating_point,
    const PacOptions& opts,
    PacResult& out_result) {
    ConversionMatrixData cm{};
    if (auto d = build_conversion_matrix(plan, hb_operating_point, opts.small_signal_freq_hz, 300.15, cm); !d.ok()) {
        return d;
    }

    std::vector<Complex> rhs(cm.dim, Complex{0.0, 0.0});
    const std::size_t idx0 = cm.h_max; /* sideband k = 0 (at f_s) */
    bool found_src = false;
    for (const auto& elem : plan.circuit.elements) {
        if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem); v != nullptr && v->id == opts.input_source_id) {
            const auto br = plan.branch_unknowns.at(v->id);
            rhs[static_cast<std::size_t>(br.value) * cm.s_count + idx0] = Complex{1.0, 0.0};
            found_src = true;
            break;
        }
        if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem); i != nullptr && i->id == opts.input_source_id) {
            if (auto u = devices::CompiledPlan::node_unknown(i->p)) {
                rhs[static_cast<std::size_t>(u->value) * cm.s_count + idx0] -= Complex{1.0, 0.0};
            }
            if (auto u = devices::CompiledPlan::node_unknown(i->n)) {
                rhs[static_cast<std::size_t>(u->value) * cm.s_count + idx0] += Complex{1.0, 0.0};
            }
            found_src = true;
            break;
        }
    }
    if (!found_src) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".PAC unknown independent source '" + opts.input_source_id + "'");
    }

    std::vector<Complex> sol;
    if (!solve_dense_complex(cm.dim, std::move(cm.y_conv), std::move(rhs), sol)) {
        return Diagnostic::error(StatusCode::NumericalFailure, ".PAC conversion matrix is singular");
    }

    PacResult res{};
    res.fundamental_hz = hb_operating_point.fundamental_hz;
    res.small_signal_freq_hz = opts.small_signal_freq_hz;
    res.sidebands.reserve(cm.s_count);
    const int h_int = static_cast<int>(cm.h_max);
    for (std::size_t idx = 0; idx < cm.s_count; ++idx) {
        const int k = static_cast<int>(idx) - h_int;
        PacSideband sb{};
        sb.sideband_index = k;
        sb.frequency_hz = opts.small_signal_freq_hz + static_cast<double>(k) * hb_operating_point.fundamental_hz;
        sb.values.resize(cm.n);
        for (std::size_t u = 0; u < cm.n; ++u) {
            sb.values[u] = sol[u * cm.s_count + idx];
        }
        res.sidebands.push_back(std::move(sb));
    }

    out_result = std::move(res);
    return Diagnostic::success();
}

/* ==================== 4. Periodic Transfer Function (.PXF) ==================== */
Diagnostic run_pxf(
    devices::CompiledPlan& plan,
    const HbResult& hb_operating_point,
    const PxfOptions& opts,
    PxfResult& out_result) {
    if (opts.out_p.value >= plan.circuit.nodes.size() || opts.out_n.value >= plan.circuit.nodes.size() ||
        opts.out_p == opts.out_n) {
        return Diagnostic::error(StatusCode::InvalidArgument, "invalid .PXF output port nodes");
    }
    const int h_int = static_cast<int>(hb_operating_point.harmonics);
    if (opts.output_sideband < -h_int || opts.output_sideband > h_int) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".PXF output_sideband out of [-H, +H] range");
    }

    ConversionMatrixData cm{};
    if (auto d = build_conversion_matrix(plan, hb_operating_point, opts.small_signal_freq_hz, 300.15, cm); !d.ok()) {
        return d;
    }

    const std::size_t out_idx = static_cast<std::size_t>(opts.output_sideband + h_int);
    std::vector<Complex> e_out(cm.dim, Complex{0.0, 0.0});
    if (auto u = devices::CompiledPlan::node_unknown(opts.out_p)) {
        e_out[static_cast<std::size_t>(u->value) * cm.s_count + out_idx] = Complex{1.0, 0.0};
    }
    if (auto u = devices::CompiledPlan::node_unknown(opts.out_n)) {
        e_out[static_cast<std::size_t>(u->value) * cm.s_count + out_idx] = Complex{-1.0, 0.0};
    }

    std::vector<Complex> z;
    if (!solve_dense_complex(cm.dim, transpose_dense_complex(cm.dim, cm.y_conv), std::move(e_out), z)) {
        return Diagnostic::error(StatusCode::NumericalFailure, ".PXF adjoint conversion matrix is singular");
    }

    PxfResult res{};
    res.output_frequency_hz =
        opts.small_signal_freq_hz + static_cast<double>(opts.output_sideband) * hb_operating_point.fundamental_hz;

    for (const auto& elem : plan.circuit.elements) {
        if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem)) {
            const auto br = plan.branch_unknowns.at(v->id);
            for (std::size_t idx = 0; idx < cm.s_count; ++idx) {
                const int k_in = static_cast<int>(idx) - h_int;
                const Complex gain = z[static_cast<std::size_t>(br.value) * cm.s_count + idx];
                res.transfers.push_back(PxfSourceTransfer{
                    v->id,
                    k_in,
                    opts.small_signal_freq_hz + static_cast<double>(k_in) * hb_operating_point.fundamental_hz,
                    gain});
            }
        } else if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem)) {
            for (std::size_t idx = 0; idx < cm.s_count; ++idx) {
                const int k_in = static_cast<int>(idx) - h_int;
                Complex gain{0.0, 0.0};
                if (auto u = devices::CompiledPlan::node_unknown(i->p)) {
                    gain -= z[static_cast<std::size_t>(u->value) * cm.s_count + idx];
                }
                if (auto u = devices::CompiledPlan::node_unknown(i->n)) {
                    gain += z[static_cast<std::size_t>(u->value) * cm.s_count + idx];
                }
                res.transfers.push_back(PxfSourceTransfer{
                    i->id,
                    k_in,
                    opts.small_signal_freq_hz + static_cast<double>(k_in) * hb_operating_point.fundamental_hz,
                    gain});
            }
        }
    }

    out_result = std::move(res);
    return Diagnostic::success();
}

/* ==================== 5. Periodic Cyclostationary Noise (.PNOISE) ==================== */
Diagnostic run_pnoise(
    devices::CompiledPlan& plan,
    const HbResult& hb_operating_point,
    const PnoiseOptions& opts,
    PnoiseResult& out_result) {
    if (!std::isfinite(opts.temperature_k) || opts.temperature_k <= 0.0) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".PNOISE temperature_k must be positive");
    }
    if (opts.out_p.value >= plan.circuit.nodes.size() || opts.out_n.value >= plan.circuit.nodes.size() ||
        opts.out_p == opts.out_n) {
        return Diagnostic::error(StatusCode::InvalidArgument, "invalid .PNOISE output port nodes");
    }
    const int h_int = static_cast<int>(hb_operating_point.harmonics);
    if (opts.output_sideband < -h_int || opts.output_sideband > h_int) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".PNOISE output_sideband out of [-H, +H] range");
    }

    ConversionMatrixData cm{};
    if (auto d = build_conversion_matrix(plan, hb_operating_point, opts.small_signal_freq_hz, opts.temperature_k, cm);
        !d.ok()) {
        return d;
    }

    const std::size_t out_idx = static_cast<std::size_t>(opts.output_sideband + h_int);
    std::vector<Complex> e_out(cm.dim, Complex{0.0, 0.0});
    if (auto u = devices::CompiledPlan::node_unknown(opts.out_p)) {
        e_out[static_cast<std::size_t>(u->value) * cm.s_count + out_idx] = Complex{1.0, 0.0};
    }
    if (auto u = devices::CompiledPlan::node_unknown(opts.out_n)) {
        e_out[static_cast<std::size_t>(u->value) * cm.s_count + out_idx] = Complex{-1.0, 0.0};
    }

    std::vector<Complex> z;
    if (!solve_dense_complex(cm.dim, transpose_dense_complex(cm.dim, cm.y_conv), std::move(e_out), z)) {
        return Diagnostic::error(StatusCode::NumericalFailure, ".PNOISE adjoint conversion matrix is singular");
    }

    auto z_node = [&](core::NodeId node, std::size_t sb_idx) noexcept -> Complex {
        if (auto u = devices::CompiledPlan::node_unknown(node)) {
            return z[static_cast<std::size_t>(u->value) * cm.s_count + sb_idx];
        }
        return {0.0, 0.0};
    };

    double total_psd = 0.0;
    const double inv_s = 1.0 / static_cast<double>(cm.s_count);

    for (const auto& elem : plan.circuit.elements) {
        if (const auto* r = std::get_if<core::ResistorSpec>(&elem)) {
            if (r->ohms <= 0.0) continue;
            const double si = 4.0 * kBoltzmannRf * opts.temperature_k / r->ohms;
            double tf_sum = 0.0;
            for (std::size_t idx = 0; idx < cm.s_count; ++idx) {
                const Complex tf = z_node(r->p, idx) - z_node(r->n, idx);
                tf_sum += std::norm(tf);
            }
            total_psd += tf_sum * si;
        } else if (const auto* d = std::get_if<core::DiodeSpec>(&elem)) {
            /*
             * Exact cyclostationary shot-noise Parseval identity over the HB time grid:
             *   S_v,out = (1/S) * sum_m (2*q*|I_d(t_m)|) * |sum_{k=-H..+H} (Z_{p,k} - Z_{n,k}) * exp(-j*k*theta_m)|^2.
             * Guaranteed non-negative and reduces identically to stationary .NOISE when I_d(t) is constant.
             */
            const double nvt = d->n_emission * d->vt_volts;
            double diode_psd = 0.0;
            for (std::size_t m = 0; m < cm.s_count; ++m) {
                const double vd = node_val_rf(hb_operating_point.waveform[m].values, d->p) -
                                  node_val_rf(hb_operating_point.waveform[m].values, d->n);
                const double id = d->is_amps * std::expm1(std::min(vd / nvt, 80.0));
                const double si_m = 2.0 * kElemChargeRf * std::abs(id);

                const double theta_m = 2.0 * std::numbers::pi * static_cast<double>(m) * inv_s;
                Complex h_m{0.0, 0.0};
                for (std::size_t idx = 0; idx < cm.s_count; ++idx) {
                    const int k = static_cast<int>(idx) - h_int;
                    const double ang = -static_cast<double>(k) * theta_m;
                    const Complex tf = z_node(d->p, idx) - z_node(d->n, idx);
                    h_m += tf * Complex{std::cos(ang), std::sin(ang)};
                }
                diode_psd += inv_s * si_m * std::norm(h_m);
            }
            total_psd += diode_psd;
        }
    }

    PnoiseResult res{};
    res.output_frequency_hz =
        opts.small_signal_freq_hz + static_cast<double>(opts.output_sideband) * hb_operating_point.fundamental_hz;
    res.total_psd_v2_per_hz = std::max(0.0, total_psd);
    res.onoise_v_per_sqrt_hz = std::sqrt(res.total_psd_v2_per_hz);
    out_result = std::move(res);
    return Diagnostic::success();
}

/* ==================== 6. Autonomous Oscillator .PSS ==================== */
Diagnostic run_autonomous_pss(
    devices::CompiledPlan& plan,
    const AutonomousPssOptions& opts,
    AutonomousPssResult& out_result) {
    if (!std::isfinite(opts.initial_freq_hz) || opts.initial_freq_hz <= 0.0 || opts.steps_per_period < 8u) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "autonomous .PSS requires positive initial_freq_hz and steps_per_period >= 8");
    }
    const std::size_t n = plan.unknown_count();
    if (opts.phase_unknown_index >= n) {
        return Diagnostic::error(StatusCode::InvalidArgument, "autonomous .PSS phase_unknown_index out of range");
    }
    if (!plan.tline_device_indices.empty()) {
        return Diagnostic::error(StatusCode::Unsupported, "autonomous .PSS currently requires lumped/OSDI DAE devices");
    }

    const bool use_trap = (opts.method == IntegrationMethod::Trapezoidal);
    std::unique_ptr<linalg::RealLinearSolver> lin_solver;
    if (auto d = linalg::create_real_solver(opts.common.backend, lin_solver); !d.ok()) return d;
    if (auto d = lin_solver->analyze(plan.pattern); !d.ok()) return d;

    std::vector<double> x0(n, 0.0);
    if (!opts.initial_state.empty()) {
        if (opts.initial_state.size() != n) {
            return Diagnostic::error(StatusCode::InvalidArgument, "autonomous .PSS initial_state size mismatch");
        }
        x0 = opts.initial_state;
    } else {
        AnalysisCommonOptions op_opts = opts.common;
        op_opts.enable_audits = false;
        OpResult op{};
        if (run_op(plan, op_opts, op).ok()) x0 = std::move(op.values);
        x0[opts.phase_unknown_index] = opts.phase_target_value;
    }

    const std::size_t nnz = plan.pattern.nnz();
    std::vector<double> f_prev(n, 0.0), q_prev(n, 0.0), f_curr(n, 0.0), q_curr(n, 0.0);
    std::vector<double> g_prev(nnz, 0.0), c_prev(nnz, 0.0), g_curr(nnz, 0.0), c_curr(nnz, 0.0);
    std::vector<double> j_step(nnz, 0.0), history(n, 0.0), rhs_s(n, 0.0);
    std::vector<std::vector<double>> sens_prev(n, std::vector<double>(n, 0.0));
    std::vector<std::vector<double>> sens_curr(n, std::vector<double>(n, 0.0));

    auto shoot_period = [&](const std::vector<double>& x_start,
                            double period_s,
                            std::vector<double>& x_end,
                            std::vector<double>* out_monodromy,
                            std::vector<TransientSample>* out_traj) -> Diagnostic {
        if (!std::isfinite(period_s) || period_s <= 0.0) {
            return Diagnostic::error(StatusCode::NumericalFailure, "autonomous .PSS period became non-positive");
        }
        const double h = period_s / static_cast<double>(opts.steps_per_period);
        const double alpha = use_trap ? (2.0 / h) : (1.0 / h);
        const bool do_mono = (out_monodromy != nullptr);

        if (out_traj != nullptr) {
            out_traj->clear();
            out_traj->reserve(opts.steps_per_period + 1u);
        }

        std::vector<double> x_prev = x_start;
        {
            EvalContext ctx0{0.0, 300.15, AnalysisMode::Transient, x_prev};
            StampTarget tgt0{f_prev, q_prev, g_prev, c_prev};
            if (auto d = plan.evaluate_all(ctx0, tgt0); !d.ok()) return d;
            plan.commit_all_trials();
        }
        if (out_traj != nullptr) out_traj->push_back(TransientSample{0.0, x_prev});

        if (do_mono) {
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

            if (do_mono) {
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

            if (out_traj != nullptr) out_traj->push_back(TransientSample{t_curr, x_curr});
            x_prev.swap(x_curr);
            f_prev.swap(f_curr);
            q_prev.swap(q_curr);
            g_prev.swap(g_curr);
            c_prev.swap(c_curr);
        }

        x_end = x_prev;
        if (do_mono) {
            out_monodromy->assign(n * n, 0.0);
            for (std::size_t r = 0; r < n; ++r) {
                for (std::size_t c = 0; c < n; ++c) {
                    (*out_monodromy)[r * n + c] = sens_prev[c][r];
                }
            }
        }
        return Diagnostic::success();
    };

    double period_s = 1.0 / opts.initial_freq_hz;
    const std::size_t n_aug = n + 1u;
    std::vector<double> x_end(n, 0.0);
    std::vector<double> monodromy;
    std::vector<TransientSample> traj;

    bool converged = false;
    std::size_t iter = 0;
    double err_norm = 0.0;

    while (iter < opts.max_shooting_iterations) {
        ++iter;
        if (auto d = shoot_period(x0, period_s, x_end, &monodromy, &traj); !d.ok()) return d;

        std::vector<double> r_aug(n_aug, 0.0);
        for (std::size_t i = 0; i < n; ++i) r_aug[i] = x_end[i] - x0[i];
        r_aug[n] = x0[opts.phase_unknown_index] - opts.phase_target_value;

        err_norm = inf_norm(r_aug);
        if (err_norm <= opts.periodic_tol) {
            converged = true;
            break;
        }

        /* Central finite-difference dx(T)/dT for the unknown oscillation period column. */
        const double d_t = std::max(1.0e-6 * period_s, 1.0e-15);
        std::vector<double> x_plus(n, 0.0), x_minus(n, 0.0);
        if (auto d = shoot_period(x0, period_s + d_t, x_plus, nullptr, nullptr); !d.ok()) return d;
        if (auto d = shoot_period(x0, period_s - d_t, x_minus, nullptr, nullptr); !d.ok()) return d;

        std::vector<double> jac(n_aug * n_aug, 0.0);
        for (std::size_t r = 0; r < n; ++r) {
            for (std::size_t c = 0; c < n; ++c) {
                jac[r * n_aug + c] = monodromy[r * n + c] - ((r == c) ? 1.0 : 0.0);
            }
            jac[r * n_aug + n] = (x_plus[r] - x_minus[r]) / (2.0 * d_t);
        }
        jac[n * n_aug + opts.phase_unknown_index] = 1.0;

        std::vector<double> neg_r(n_aug, 0.0);
        for (std::size_t i = 0; i < n_aug; ++i) neg_r[i] = -r_aug[i];

        std::vector<double> delta(n_aug, 0.0);
        if (!solve_dense_real(n_aug, std::move(jac), std::move(neg_r), delta)) {
            return Diagnostic::error(
                StatusCode::NumericalFailure, "autonomous .PSS augmented shooting Jacobian is singular");
        }

        /* Damp large state or period jumps to stay on the non-trivial limit cycle. */
        double scale = 1.0;
        const double max_dt = std::abs(delta[n]);
        if (max_dt > 0.25 * period_s) scale = std::min(scale, (0.25 * period_s) / max_dt);
        double max_dx = 0.0;
        for (std::size_t i = 0; i < n; ++i) max_dx = std::max(max_dx, std::abs(delta[i]));
        if (max_dx > 1.0) scale = std::min(scale, 1.0 / max_dx);

        std::vector<double> x0_trial(n, 0.0);
        std::vector<double> x_end_trial(n, 0.0);
        bool accepted = false;
        for (int ls = 0; ls < 10; ++ls) {
            const double t_trial = period_s + scale * delta[n];
            if (t_trial > 0.0) {
                for (std::size_t i = 0; i < n; ++i) x0_trial[i] = x0[i] + scale * delta[i];
                if (shoot_period(x0_trial, t_trial, x_end_trial, nullptr, nullptr).ok()) {
                    double trial_err = std::abs(x0_trial[opts.phase_unknown_index] - opts.phase_target_value);
                    for (std::size_t i = 0; i < n; ++i) {
                        trial_err = std::max(trial_err, std::abs(x_end_trial[i] - x0_trial[i]));
                    }
                    if (trial_err < err_norm || trial_err <= opts.periodic_tol) {
                        x0.swap(x0_trial);
                        period_s = t_trial;
                        accepted = true;
                        break;
                    }
                }
            }
            scale *= 0.5;
        }
        if (!accepted) {
            const double t_trial = period_s + scale * delta[n];
            if (t_trial <= 0.0) {
                return Diagnostic::error(StatusCode::NumericalFailure, "autonomous .PSS period step failed");
            }
            for (std::size_t i = 0; i < n; ++i) x0[i] += scale * delta[i];
            period_s = t_trial;
        }
    }

    if (!converged) {
        return Diagnostic::error(
            StatusCode::NumericalFailure, "autonomous .PSS failed to converge within max_shooting_iterations");
    }

    AutonomousPssResult res{};
    res.converged = true;
    res.shooting_iterations = iter;
    res.period_s = period_s;
    res.oscillation_freq_hz = 1.0 / period_s;
    res.periodicity_error = err_norm;
    res.waveform = std::move(traj);
    out_result = std::move(res);
    return Diagnostic::success();
}

}  // namespace spice::analysis
