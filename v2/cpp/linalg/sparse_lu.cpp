#include "linalg/solver.hpp"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <memory>
#include <vector>

namespace spice::linalg {

namespace {

constexpr double kSparsePivotTol = 1.0e-30;
constexpr double kSparseRefineTrigger = 1.0e-12;
constexpr int kSparseMaxRefine = 3;

/*
 * Pure C++20 left-looking Gilbert-Peierls sparse LU solver with partial pivoting
 * (real64 & complex64). Runs O(nnz(L+U)) per factorization and O(nnz(L+U)) per
 * solve, enabling 1,000-10,000+ unknown circuit solves in milliseconds even when
 * external SuiteSparse KLU is not linked, and providing a second independent
 * sparse reference alongside KLU (ARCH-GOAL-002, ARCH-LIN-002).
 */
template <typename Scalar>
class SparseLuImpl final {
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
        for (const Scalar& v : values) {
            if (!is_finite_scalar(v)) {
                factored_ = false;
                return core::Diagnostic::error(
                    core::StatusCode::NumericalFailure, "nonfinite matrix entry");
            }
        }

        orig_values_.assign(values.begin(), values.end());

        /* L is stored by columns (strictly lower triangular, unit diagonal implicit).
         * U is stored by columns (upper triangular, diagonal stored separately in u_diag_).
         * row_perm_[orig_row] = pivot_step (or kUnassigned if row not yet pivoted). */
        constexpr std::uint32_t kUnassigned = std::numeric_limits<std::uint32_t>::max();
        row_perm_.assign(n_, kUnassigned);
        inv_row_perm_.assign(n_, kUnassigned);

        l_cols_.assign(n_, {});
        u_cols_.assign(n_, {});
        u_diag_.assign(n_, Scalar{0});

        std::vector<Scalar> work(n_, Scalar{0});
        std::vector<std::uint32_t> visited_stamp(n_, 0);
        std::vector<std::uint32_t> stack;
        std::vector<std::uint32_t> topo;
        std::vector<std::uint32_t> dfs_pos(n_, 0);
        std::uint32_t stamp = 0;

        for (std::uint32_t col = 0; col < n_; ++col) {
            ++stamp;
            topo.clear();

            /* Symbolic reach in L for column `col` (DFS over already-pivoted rows). */
            for (std::size_t k = pattern_.col_ptr[col]; k < pattern_.col_ptr[col + 1]; ++k) {
                const std::uint32_t row = pattern_.row_idx[k];
                work[row] = values[k];
                if (visited_stamp[row] == stamp) continue;

                const std::uint32_t p = row_perm_[row];
                if (p == kUnassigned) {
                    visited_stamp[row] = stamp;
                    continue;
                }

                /* Iterative DFS from pivoted row `row` (factor column `p`). */
                stack.clear();
                stack.push_back(row);
                visited_stamp[row] = stamp;
                dfs_pos[row] = 0;

                while (!stack.empty()) {
                    const std::uint32_t cur_row = stack.back();
                    const std::uint32_t cur_p = row_perm_[cur_row];
                    const auto& lcol = l_cols_[cur_p];
                    std::uint32_t& pos = dfs_pos[cur_row];
                    bool pushed = false;
                    while (pos < lcol.size()) {
                        const std::uint32_t next_row = lcol[pos++].first;
                        if (visited_stamp[next_row] != stamp) {
                            visited_stamp[next_row] = stamp;
                            if (row_perm_[next_row] != kUnassigned) {
                                dfs_pos[next_row] = 0;
                                stack.push_back(next_row);
                                pushed = true;
                                break;
                            }
                        }
                    }
                    if (!pushed) {
                        topo.push_back(cur_row);
                        stack.pop_back();
                    }
                }
            }

            /* Numeric sparse triangular solve: L * x = A(:, col) in reverse post-order. */
            for (std::size_t idx = topo.size(); idx-- > 0;) {
                const std::uint32_t r = topo[idx];
                const std::uint32_t p = row_perm_[r];
                const Scalar xj = work[r];
                if (xj == Scalar{0}) continue;
                for (const auto& [l_row, l_val] : l_cols_[p]) {
                    work[l_row] -= l_val * xj;
                }
            }

            /* Separate upper entries (already pivoted) and unpivoted candidates. */
            std::uint32_t pivot_row = kUnassigned;
            double max_abs = -1.0;

            /* Collect all rows touched during reach: pivoted ones go to U, unpivoted to L. */
            auto& ucol = u_cols_[col];
            auto& lcol = l_cols_[col];
            ucol.reserve(topo.size());

            for (std::size_t idx = topo.size(); idx-- > 0;) {
                const std::uint32_t r = topo[idx];
                const Scalar val = work[r];
                if (val != Scalar{0}) {
                    ucol.emplace_back(row_perm_[r], val);
                }
                work[r] = Scalar{0};
                /* Also collect unpivoted neighbors reached from `l_cols_[p]`. */
                const std::uint32_t p = row_perm_[r];
                for (const auto& [l_row, _] : l_cols_[p]) {
                    if (row_perm_[l_row] == kUnassigned && visited_stamp[l_row] == stamp) {
                        const Scalar lv = work[l_row];
                        const double av = std::abs(lv);
                        if (av > max_abs) {
                            max_abs = av;
                            pivot_row = l_row;
                        }
                        if (lv != Scalar{0}) {
                            lcol.emplace_back(l_row, lv);
                        }
                        work[l_row] = Scalar{0};
                        visited_stamp[l_row] = 0; /* Consume once */
                    }
                }
            }
            for (std::size_t k = pattern_.col_ptr[col]; k < pattern_.col_ptr[col + 1]; ++k) {
                const std::uint32_t r = pattern_.row_idx[k];
                if (row_perm_[r] == kUnassigned && visited_stamp[r] == stamp) {
                    const Scalar lv = work[r];
                    const double av = std::abs(lv);
                    if (av > max_abs) {
                        max_abs = av;
                        pivot_row = r;
                    }
                    if (lv != Scalar{0}) {
                        lcol.emplace_back(r, lv);
                    }
                    work[r] = Scalar{0};
                    visited_stamp[r] = 0;
                }
            }

            if (pivot_row == kUnassigned || !std::isfinite(max_abs) || max_abs <= kSparsePivotTol) {
                factored_ = false;
                return core::Diagnostic::error(
                    core::StatusCode::NumericalFailure, "singular matrix in SparseLu");
            }

            row_perm_[pivot_row] = col;
            inv_row_perm_[col] = pivot_row;

            /* Extract pivot value from lcol and scale remaining strictly lower entries. */
            Scalar pivot_val{0};
            std::size_t write_idx = 0;
            for (std::size_t i = 0; i < lcol.size(); ++i) {
                if (lcol[i].first == pivot_row) {
                    pivot_val = lcol[i].second;
                } else {
                    lcol[write_idx++] = lcol[i];
                }
            }
            lcol.resize(write_idx);
            u_diag_[col] = pivot_val;
            for (auto& entry : lcol) {
                entry.second /= pivot_val;
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
                    core::StatusCode::NumericalFailure, "nonfinite SparseLu solution");
            }
        }

        double best_err = compute_backward_error(out_x, rhs);
        if (best_err > kSparseRefineTrigger) {
            std::vector<Scalar> residual(n_);
            std::vector<Scalar> dx(n_);
            std::vector<Scalar> candidate(n_);
            for (int iter = 0; iter < kSparseMaxRefine && best_err > kSparseRefineTrigger; ++iter) {
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
        /* Forward solve L * y = rhs in original-row space (L entries are indexed by original row). */
        std::vector<Scalar> y(rhs.begin(), rhs.end());
        for (std::uint32_t col = 0; col < n_; ++col) {
            const std::uint32_t piv_row = inv_row_perm_[col];
            const Scalar y_col = y[piv_row];
            out_x[col] = y_col;
            if (y_col == Scalar{0}) continue;
            for (const auto& [l_row, l_val] : l_cols_[col]) {
                y[l_row] -= l_val * y_col;
            }
        }
        /* Backward solve U * x = P * y (U rows are indexed by pivot step `0..col-1`). */
        for (std::uint32_t col = n_; col-- > 0;) {
            const Scalar x_col = out_x[col] / u_diag_[col];
            out_x[col] = x_col;
            if (x_col == Scalar{0}) continue;
            for (const auto& [u_row, u_val] : u_cols_[col]) {
                out_x[u_row] -= u_val * x_col;
            }
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
    std::vector<std::uint32_t> row_perm_;
    std::vector<std::uint32_t> inv_row_perm_;
    std::vector<std::vector<std::pair<std::uint32_t, Scalar>>> l_cols_;
    std::vector<std::vector<std::pair<std::uint32_t, Scalar>>> u_cols_;
    std::vector<Scalar> u_diag_;
    SolverStats stats_;
};

class SparseRealSolver final : public RealLinearSolver {
public:
    std::string name() const override { return "sparse_lu"; }
    core::Diagnostic analyze(const core::CscPattern& pattern) override { return impl_.analyze(pattern); }
    core::Diagnostic factor(std::span<const double> values) override { return impl_.factor(values); }
    core::Diagnostic solve(std::span<const double> rhs, std::span<double> out_x) override {
        return impl_.solve(rhs, out_x);
    }
    SolverStats stats() const noexcept override { return impl_.stats(); }

private:
    SparseLuImpl<double> impl_;
};

class SparseComplexSolver final : public ComplexLinearSolver {
public:
    std::string name() const override { return "sparse_lu"; }
    core::Diagnostic analyze(const core::CscPattern& pattern) override { return impl_.analyze(pattern); }
    core::Diagnostic factor(std::span<const Complex> values) override { return impl_.factor(values); }
    core::Diagnostic solve(std::span<const Complex> rhs, std::span<Complex> out_x) override {
        return impl_.solve(rhs, out_x);
    }
    SolverStats stats() const noexcept override { return impl_.stats(); }

private:
    SparseLuImpl<Complex> impl_;
};

}  // namespace

std::unique_ptr<RealLinearSolver> make_sparse_real_solver() {
    return std::make_unique<SparseRealSolver>();
}

std::unique_ptr<ComplexLinearSolver> make_sparse_complex_solver() {
    return std::make_unique<SparseComplexSolver>();
}

}  // namespace spice::linalg
