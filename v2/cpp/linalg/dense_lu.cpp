#include "linalg/solver.hpp"

#include <algorithm>
#include <cmath>
#include <limits>
#include <numeric>
#include <vector>

namespace spice::linalg {

namespace {

constexpr double kSingularPivotTol = 1.0e-30;
constexpr double kBackwardErrorFloor = 1.0e-30;
constexpr double kRefinementTrigger = 1.0e-12;
constexpr int kMaxRefinementSteps = 3;

/* Dense LU with partial pivoting (row-major n x n), supporting real and complex. */
template <typename Scalar>
class DenseLuImpl final {
public:
    core::Diagnostic analyze(const core::CscPattern& pattern) {
        auto diag = pattern.validate();
        if (!diag.ok()) {
            analyzed_ = false;
            factored_ = false;
            return diag;
        }
        pattern_ = pattern;
        n_ = pattern.dimension;
        analyzed_ = true;
        factored_ = false;
        ++stats_.symbolic_analyses;
        return core::Diagnostic::success();
    }

    core::Diagnostic factor(std::span<const Scalar> values) {
        if (!analyzed_) {
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "factor() called before analyze()");
        }
        if (values.size() != pattern_.nnz()) {
            factored_ = false;
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "CSC value count mismatch");
        }
        orig_values_.assign(values.begin(), values.end());
        lu_.assign(static_cast<std::size_t>(n_) * n_, Scalar{0});
        for (std::uint32_t col = 0; col < n_; ++col) {
            for (std::size_t k = pattern_.col_ptr[col]; k < pattern_.col_ptr[col + 1]; ++k) {
                const Scalar v = values[k];
                if (!is_finite_scalar(v)) {
                    factored_ = false;
                    return core::Diagnostic::error(
                        core::StatusCode::NumericalFailure, "nonfinite matrix entry");
                }
                lu_[static_cast<std::size_t>(pattern_.row_idx[k]) * n_ + col] = v;
            }
        }

        piv_.resize(n_);
        std::iota(piv_.begin(), piv_.end(), std::uint32_t{0});

        for (std::uint32_t k = 0; k < n_; ++k) {
            std::uint32_t pivot_row = k;
            double max_abs = std::abs(lu_[static_cast<std::size_t>(k) * n_ + k]);
            for (std::uint32_t r = k + 1; r < n_; ++r) {
                const double cand = std::abs(lu_[static_cast<std::size_t>(r) * n_ + k]);
                if (cand > max_abs) {
                    max_abs = cand;
                    pivot_row = r;
                }
            }
            if (!std::isfinite(max_abs) || max_abs <= kSingularPivotTol) {
                factored_ = false;
                return core::Diagnostic::error(
                    core::StatusCode::NumericalFailure, "singular matrix in DenseLu");
            }
            if (pivot_row != k) {
                std::swap(piv_[k], piv_[pivot_row]);
                for (std::uint32_t c = 0; c < n_; ++c) {
                    std::swap(
                        lu_[static_cast<std::size_t>(k) * n_ + c],
                        lu_[static_cast<std::size_t>(pivot_row) * n_ + c]);
                }
            }
            const Scalar diag = lu_[static_cast<std::size_t>(k) * n_ + k];
            for (std::uint32_t r = k + 1; r < n_; ++r) {
                Scalar& factor_entry = lu_[static_cast<std::size_t>(r) * n_ + k];
                factor_entry /= diag;
                if (factor_entry == Scalar{0}) continue;
                for (std::uint32_t c = k + 1; c < n_; ++c) {
                    lu_[static_cast<std::size_t>(r) * n_ + c] -=
                        factor_entry * lu_[static_cast<std::size_t>(k) * n_ + c];
                }
            }
        }

        factored_ = true;
        ++stats_.numeric_factorizations;
        return core::Diagnostic::success();
    }

    core::Diagnostic solve(std::span<const Scalar> rhs, std::span<Scalar> out_x) {
        if (!factored_) {
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "solve() called without valid numeric factors");
        }
        if (rhs.size() != n_ || out_x.size() != n_) {
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "RHS or output vector dimension mismatch");
        }
        for (const Scalar& v : rhs) {
            if (!is_finite_scalar(v)) {
                return core::Diagnostic::error(
                    core::StatusCode::NumericalFailure, "nonfinite RHS entry");
            }
        }

        solve_with_factors(rhs, out_x);
        for (const Scalar& v : out_x) {
            if (!is_finite_scalar(v)) {
                return core::Diagnostic::error(
                    core::StatusCode::NumericalFailure, "nonfinite DenseLu solution");
            }
        }

        /* Optional same-precision iterative refinement if backward error > 1e-12. */
        double best_err = compute_backward_error(out_x, rhs);
        if (best_err > kRefinementTrigger) {
            std::vector<Scalar> residual(n_);
            std::vector<Scalar> dx(n_);
            std::vector<Scalar> candidate(n_);
            for (int iter = 0; iter < kMaxRefinementSteps && best_err > kRefinementTrigger; ++iter) {
                compute_residual(out_x, rhs, residual);
                solve_with_factors(residual, dx);
                bool finite = true;
                for (std::uint32_t i = 0; i < n_; ++i) {
                    candidate[i] = out_x[i] + dx[i];
                    if (!is_finite_scalar(candidate[i])) {
                        finite = false;
                        break;
                    }
                }
                if (!finite) break;
                const double cand_err = compute_backward_error(candidate, rhs);
                if (cand_err < best_err) {
                    std::copy(candidate.begin(), candidate.end(), out_x.begin());
                    best_err = cand_err;
                } else {
                    break;
                }
            }
        }

        ++stats_.solves;
        return core::Diagnostic::success();
    }

    [[nodiscard]] SolverStats stats() const noexcept { return stats_; }

private:
    static bool is_finite_scalar(double v) noexcept { return std::isfinite(v); }
    static bool is_finite_scalar(Complex v) noexcept {
        return std::isfinite(v.real()) && std::isfinite(v.imag());
    }

    void solve_with_factors(std::span<const Scalar> rhs, std::span<Scalar> out_x) const {
        for (std::uint32_t i = 0; i < n_; ++i) {
            out_x[i] = rhs[piv_[i]];
        }
        /* Forward substitution: L * y = P * b (unit diagonal L). */
        for (std::uint32_t i = 0; i < n_; ++i) {
            Scalar sum = out_x[i];
            for (std::uint32_t j = 0; j < i; ++j) {
                sum -= lu_[static_cast<std::size_t>(i) * n_ + j] * out_x[j];
            }
            out_x[i] = sum;
        }
        /* Backward substitution: U * x = y. */
        for (std::uint32_t idx = n_; idx-- > 0;) {
            Scalar sum = out_x[idx];
            for (std::uint32_t j = idx + 1; j < n_; ++j) {
                sum -= lu_[static_cast<std::size_t>(idx) * n_ + j] * out_x[j];
            }
            out_x[idx] = sum / lu_[static_cast<std::size_t>(idx) * n_ + idx];
        }
    }

    void compute_residual(
        std::span<const Scalar> x,
        std::span<const Scalar> rhs,
        std::vector<Scalar>& out_res) const {
        out_res.assign(rhs.begin(), rhs.end());
        for (std::uint32_t col = 0; col < n_; ++col) {
            const Scalar xj = x[col];
            for (std::size_t k = pattern_.col_ptr[col]; k < pattern_.col_ptr[col + 1]; ++k) {
                out_res[pattern_.row_idx[k]] -= orig_values_[k] * xj;
            }
        }
    }

    double compute_backward_error(
        std::span<const Scalar> x,
        std::span<const Scalar> rhs) const noexcept {
        if constexpr (std::is_same_v<Scalar, double>) {
            return real_backward_error(pattern_, orig_values_, x, rhs);
        } else {
            return complex_backward_error(pattern_, orig_values_, x, rhs);
        }
    }

    core::CscPattern pattern_;
    std::uint32_t n_{0};
    bool analyzed_{false};
    bool factored_{false};
    std::vector<Scalar> orig_values_;
    std::vector<Scalar> lu_;
    std::vector<std::uint32_t> piv_;
    SolverStats stats_;
};

class DenseRealSolver final : public RealLinearSolver {
public:
    std::string name() const override { return "dense_lu"; }
    core::Diagnostic analyze(const core::CscPattern& pattern) override { return impl_.analyze(pattern); }
    core::Diagnostic factor(std::span<const double> values) override { return impl_.factor(values); }
    core::Diagnostic solve(std::span<const double> rhs, std::span<double> out_x) override {
        return impl_.solve(rhs, out_x);
    }
    SolverStats stats() const noexcept override { return impl_.stats(); }

private:
    DenseLuImpl<double> impl_;
};

class DenseComplexSolver final : public ComplexLinearSolver {
public:
    std::string name() const override { return "dense_lu"; }
    core::Diagnostic analyze(const core::CscPattern& pattern) override { return impl_.analyze(pattern); }
    core::Diagnostic factor(std::span<const Complex> values) override { return impl_.factor(values); }
    core::Diagnostic solve(std::span<const Complex> rhs, std::span<Complex> out_x) override {
        return impl_.solve(rhs, out_x);
    }
    SolverStats stats() const noexcept override { return impl_.stats(); }

private:
    DenseLuImpl<Complex> impl_;
};

}  // namespace

double real_backward_error(
    const core::CscPattern& pattern,
    std::span<const double> values,
    std::span<const double> x,
    std::span<const double> rhs) noexcept {
    const std::uint32_t n = pattern.dimension;
    if (n == 0 || values.size() != pattern.nnz() || x.size() != n || rhs.size() != n) {
        return std::numeric_limits<double>::infinity();
    }
    std::vector<double> ax(n, 0.0);
    std::vector<double> abs_ax(n, 0.0);
    for (std::uint32_t col = 0; col < n; ++col) {
        const double xj = x[col];
        if (!std::isfinite(xj)) return std::numeric_limits<double>::infinity();
        for (std::size_t k = pattern.col_ptr[col]; k < pattern.col_ptr[col + 1]; ++k) {
            const double aij = values[k];
            if (!std::isfinite(aij)) return std::numeric_limits<double>::infinity();
            const std::uint32_t r = pattern.row_idx[k];
            ax[r] += aij * xj;
            abs_ax[r] += std::abs(aij) * std::abs(xj);
        }
    }
    double worst = 0.0;
    for (std::uint32_t i = 0; i < n; ++i) {
        if (!std::isfinite(rhs[i]) || !std::isfinite(ax[i]) || !std::isfinite(abs_ax[i])) {
            return std::numeric_limits<double>::infinity();
        }
        const double resid = std::abs(ax[i] - rhs[i]);
        const double scale = abs_ax[i] + std::abs(rhs[i]) + kBackwardErrorFloor;
        const double err = resid / scale;
        if (!std::isfinite(err)) return std::numeric_limits<double>::infinity();
        worst = std::max(worst, err);
    }
    return worst;
}

double complex_backward_error(
    const core::CscPattern& pattern,
    std::span<const Complex> values,
    std::span<const Complex> x,
    std::span<const Complex> rhs) noexcept {
    const std::uint32_t n = pattern.dimension;
    if (n == 0 || values.size() != pattern.nnz() || x.size() != n || rhs.size() != n) {
        return std::numeric_limits<double>::infinity();
    }
    std::vector<Complex> ax(n, Complex{0.0, 0.0});
    std::vector<double> abs_ax(n, 0.0);
    for (std::uint32_t col = 0; col < n; ++col) {
        const Complex xj = x[col];
        if (!std::isfinite(xj.real()) || !std::isfinite(xj.imag())) {
            return std::numeric_limits<double>::infinity();
        }
        for (std::size_t k = pattern.col_ptr[col]; k < pattern.col_ptr[col + 1]; ++k) {
            const Complex aij = values[k];
            if (!std::isfinite(aij.real()) || !std::isfinite(aij.imag())) {
                return std::numeric_limits<double>::infinity();
            }
            const std::uint32_t r = pattern.row_idx[k];
            ax[r] += aij * xj;
            abs_ax[r] += std::abs(aij) * std::abs(xj);
        }
    }
    double worst = 0.0;
    for (std::uint32_t i = 0; i < n; ++i) {
        if (!std::isfinite(rhs[i].real()) || !std::isfinite(rhs[i].imag()) ||
            !std::isfinite(ax[i].real()) || !std::isfinite(ax[i].imag()) ||
            !std::isfinite(abs_ax[i])) {
            return std::numeric_limits<double>::infinity();
        }
        const double resid = std::abs(ax[i] - rhs[i]);
        const double scale = abs_ax[i] + std::abs(rhs[i]) + kBackwardErrorFloor;
        const double err = resid / scale;
        if (!std::isfinite(err)) return std::numeric_limits<double>::infinity();
        worst = std::max(worst, err);
    }
    return worst;
}

std::unique_ptr<RealLinearSolver> make_dense_real_solver() {
    return std::make_unique<DenseRealSolver>();
}

std::unique_ptr<ComplexLinearSolver> make_dense_complex_solver() {
    return std::make_unique<DenseComplexSolver>();
}

}  // namespace spice::linalg
