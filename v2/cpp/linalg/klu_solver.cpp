#include "linalg/solver.hpp"

#include <cmath>
#include <cstdint>
#include <limits>
#include <memory>
#include <vector>

#if defined(SPICE_V2_HAS_KLU)
#  include <klu.h>
#endif

namespace spice::linalg {

std::unique_ptr<RealLinearSolver> make_dense_real_solver();
std::unique_ptr<ComplexLinearSolver> make_dense_complex_solver();
std::unique_ptr<RealLinearSolver> make_sparse_real_solver();
std::unique_ptr<ComplexLinearSolver> make_sparse_complex_solver();

#if defined(SPICE_V2_HAS_KLU)
namespace {

class KluRealSolver final : public RealLinearSolver {
public:
    KluRealSolver() { klu_defaults(&common_); }
    ~KluRealSolver() override { reset_handles(); }

    std::string name() const override { return "klu"; }

    core::Diagnostic analyze(const core::CscPattern& pattern) override {
        auto diag = pattern.validate();
        if (!diag.ok()) {
            reset_handles();
            return diag;
        }
        if (pattern.dimension > static_cast<std::uint32_t>(std::numeric_limits<int>::max()) ||
            pattern.nnz() > static_cast<std::size_t>(std::numeric_limits<int>::max())) {
            reset_handles();
            return core::Diagnostic::error(
                core::StatusCode::ResourceExhausted, "matrix exceeds 32-bit KLU index range");
        }

        reset_handles();
        n_ = static_cast<int>(pattern.dimension);
        ap_.resize(pattern.col_ptr.size());
        for (std::size_t i = 0; i < pattern.col_ptr.size(); ++i) {
            ap_[i] = static_cast<int>(pattern.col_ptr[i]);
        }
        ai_.resize(pattern.row_idx.size());
        for (std::size_t i = 0; i < pattern.row_idx.size(); ++i) {
            ai_[i] = static_cast<int>(pattern.row_idx[i]);
        }

        symbolic_ = klu_analyze(n_, ap_.data(), ai_.data(), &common_);
        if (symbolic_ == nullptr || common_.status != KLU_OK) {
            reset_handles();
            return core::Diagnostic::error(
                core::StatusCode::NumericalFailure, "klu_analyze failed");
        }
        ++stats_.symbolic_analyses;
        return core::Diagnostic::success();
    }

    core::Diagnostic factor(std::span<const double> values) override {
        if (symbolic_ == nullptr) {
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "factor() called before analyze()");
        }
        if (values.size() != ai_.size()) {
            free_numeric();
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "CSC value count mismatch");
        }
        for (double v : values) {
            if (!std::isfinite(v)) {
                free_numeric();
                return core::Diagnostic::error(
                    core::StatusCode::NumericalFailure, "nonfinite matrix entry");
            }
        }
        ax_.assign(values.begin(), values.end());

        /* Attempt fast refactorization first when numeric factors already exist,
         * falling back cleanly to fresh klu_factor if pivots shift. */
        if (numeric_ != nullptr) {
            const int ok = klu_refactor(ap_.data(), ai_.data(), ax_.data(), symbolic_, numeric_, &common_);
            if (ok != 0 && common_.status == KLU_OK) {
                ++stats_.numeric_factorizations;
                return core::Diagnostic::success();
            }
            free_numeric();
        }
        numeric_ = klu_factor(ap_.data(), ai_.data(), ax_.data(), symbolic_, &common_);
        if (numeric_ == nullptr || common_.status != KLU_OK) {
            free_numeric();
            return core::Diagnostic::error(
                core::StatusCode::NumericalFailure, "klu_factor failed (singular or ill-conditioned)");
        }
        ++stats_.numeric_factorizations;
        return core::Diagnostic::success();
    }

    core::Diagnostic solve(std::span<const double> rhs, std::span<double> out_x) override {
        if (numeric_ == nullptr) {
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "solve() called without valid KLU factors");
        }
        if (rhs.size() != static_cast<std::size_t>(n_) ||
            out_x.size() != static_cast<std::size_t>(n_)) {
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "RHS or output dimension mismatch");
        }
        std::copy(rhs.begin(), rhs.end(), out_x.begin());
        const int ok = klu_solve(symbolic_, numeric_, n_, 1, out_x.data(), &common_);
        if (ok == 0 || common_.status != KLU_OK) {
            return core::Diagnostic::error(
                core::StatusCode::NumericalFailure, "klu_solve failed");
        }
        ++stats_.solves;
        return core::Diagnostic::success();
    }

    SolverStats stats() const noexcept override { return stats_; }

private:
    void free_numeric() noexcept {
        if (numeric_ != nullptr) {
            klu_free_numeric(&numeric_, &common_);
            numeric_ = nullptr;
        }
    }
    void reset_handles() noexcept {
        free_numeric();
        if (symbolic_ != nullptr) {
            klu_free_symbolic(&symbolic_, &common_);
            symbolic_ = nullptr;
        }
        n_ = 0;
    }

    klu_common common_{};
    klu_symbolic* symbolic_{nullptr};
    klu_numeric* numeric_{nullptr};
    int n_{0};
    std::vector<int> ap_;
    std::vector<int> ai_;
    std::vector<double> ax_;
    SolverStats stats_;
};

class KluComplexSolver final : public ComplexLinearSolver {
public:
    KluComplexSolver() { klu_defaults(&common_); }
    ~KluComplexSolver() override { reset_handles(); }

    std::string name() const override { return "klu"; }

    core::Diagnostic analyze(const core::CscPattern& pattern) override {
        auto diag = pattern.validate();
        if (!diag.ok()) {
            reset_handles();
            return diag;
        }
        if (pattern.dimension > static_cast<std::uint32_t>(std::numeric_limits<int>::max()) ||
            pattern.nnz() > static_cast<std::size_t>(std::numeric_limits<int>::max())) {
            reset_handles();
            return core::Diagnostic::error(
                core::StatusCode::ResourceExhausted, "matrix exceeds 32-bit KLU index range");
        }
        reset_handles();
        n_ = static_cast<int>(pattern.dimension);
        ap_.resize(pattern.col_ptr.size());
        for (std::size_t i = 0; i < pattern.col_ptr.size(); ++i) {
            ap_[i] = static_cast<int>(pattern.col_ptr[i]);
        }
        ai_.resize(pattern.row_idx.size());
        for (std::size_t i = 0; i < pattern.row_idx.size(); ++i) {
            ai_[i] = static_cast<int>(pattern.row_idx[i]);
        }
        symbolic_ = klu_analyze(n_, ap_.data(), ai_.data(), &common_);
        if (symbolic_ == nullptr || common_.status != KLU_OK) {
            reset_handles();
            return core::Diagnostic::error(
                core::StatusCode::NumericalFailure, "klu_analyze failed");
        }
        ++stats_.symbolic_analyses;
        return core::Diagnostic::success();
    }

    core::Diagnostic factor(std::span<const Complex> values) override {
        if (symbolic_ == nullptr) {
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "factor() called before analyze()");
        }
        if (values.size() != ai_.size()) {
            free_numeric();
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "CSC value count mismatch");
        }
        ax_.resize(values.size() * 2u);
        for (std::size_t i = 0; i < values.size(); ++i) {
            if (!std::isfinite(values[i].real()) || !std::isfinite(values[i].imag())) {
                free_numeric();
                return core::Diagnostic::error(
                    core::StatusCode::NumericalFailure, "nonfinite complex matrix entry");
            }
            ax_[2u * i] = values[i].real();
            ax_[2u * i + 1u] = values[i].imag();
        }
        if (numeric_ != nullptr) {
            const int ok = klu_z_refactor(ap_.data(), ai_.data(), ax_.data(), symbolic_, numeric_, &common_);
            if (ok != 0 && common_.status == KLU_OK) {
                ++stats_.numeric_factorizations;
                return core::Diagnostic::success();
            }
            free_numeric();
        }
        numeric_ = klu_z_factor(ap_.data(), ai_.data(), ax_.data(), symbolic_, &common_);
        if (numeric_ == nullptr || common_.status != KLU_OK) {
            free_numeric();
            return core::Diagnostic::error(
                core::StatusCode::NumericalFailure, "klu_z_factor failed");
        }
        ++stats_.numeric_factorizations;
        return core::Diagnostic::success();
    }

    core::Diagnostic solve(std::span<const Complex> rhs, std::span<Complex> out_x) override {
        if (numeric_ == nullptr) {
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "solve() called without valid KLU factors");
        }
        if (rhs.size() != static_cast<std::size_t>(n_) ||
            out_x.size() != static_cast<std::size_t>(n_)) {
            return core::Diagnostic::error(
                core::StatusCode::InvalidArgument, "RHS or output dimension mismatch");
        }
        std::vector<double> buf(static_cast<std::size_t>(n_) * 2u);
        for (int i = 0; i < n_; ++i) {
            buf[2u * static_cast<std::size_t>(i)] = rhs[static_cast<std::size_t>(i)].real();
            buf[2u * static_cast<std::size_t>(i) + 1u] = rhs[static_cast<std::size_t>(i)].imag();
        }
        const int ok = klu_z_solve(symbolic_, numeric_, n_, 1, buf.data(), &common_);
        if (ok == 0 || common_.status != KLU_OK) {
            return core::Diagnostic::error(
                core::StatusCode::NumericalFailure, "klu_z_solve failed");
        }
        for (int i = 0; i < n_; ++i) {
            out_x[static_cast<std::size_t>(i)] =
                Complex{buf[2u * static_cast<std::size_t>(i)], buf[2u * static_cast<std::size_t>(i) + 1u]};
        }
        ++stats_.solves;
        return core::Diagnostic::success();
    }

    SolverStats stats() const noexcept override { return stats_; }

private:
    void free_numeric() noexcept {
        if (numeric_ != nullptr) {
            klu_free_numeric(&numeric_, &common_);
            numeric_ = nullptr;
        }
    }
    void reset_handles() noexcept {
        free_numeric();
        if (symbolic_ != nullptr) {
            klu_free_symbolic(&symbolic_, &common_);
            symbolic_ = nullptr;
        }
        n_ = 0;
    }

    klu_common common_{};
    klu_symbolic* symbolic_{nullptr};
    klu_numeric* numeric_{nullptr};
    int n_{0};
    std::vector<int> ap_;
    std::vector<int> ai_;
    std::vector<double> ax_;
    SolverStats stats_;
};

}  // namespace
#endif

bool klu_is_linked() noexcept {
#if defined(SPICE_V2_HAS_KLU)
    return true;
#else
    return false;
#endif
}

core::Diagnostic create_real_solver(
    BackendPreference preference,
    std::unique_ptr<RealLinearSolver>& out_solver) {
    out_solver.reset();
    switch (preference) {
        case BackendPreference::DenseLu:
            out_solver = make_dense_real_solver();
            return core::Diagnostic::success();
        case BackendPreference::SparseLu:
            out_solver = make_sparse_real_solver();
            return core::Diagnostic::success();
        case BackendPreference::Klu:
#if defined(SPICE_V2_HAS_KLU)
            out_solver = std::make_unique<KluRealSolver>();
            return core::Diagnostic::success();
#else
            return core::Diagnostic::error(
                core::StatusCode::Unsupported, "SuiteSparse KLU is not linked in this build");
#endif
        case BackendPreference::Auto:
#if defined(SPICE_V2_HAS_KLU)
            out_solver = std::make_unique<KluRealSolver>();
#else
            out_solver = make_sparse_real_solver();
#endif
            return core::Diagnostic::success();
    }
    return core::Diagnostic::error(core::StatusCode::InvalidArgument, "unknown backend preference");
}

core::Diagnostic create_complex_solver(
    BackendPreference preference,
    std::unique_ptr<ComplexLinearSolver>& out_solver) {
    out_solver.reset();
    switch (preference) {
        case BackendPreference::DenseLu:
            out_solver = make_dense_complex_solver();
            return core::Diagnostic::success();
        case BackendPreference::SparseLu:
            out_solver = make_sparse_complex_solver();
            return core::Diagnostic::success();
        case BackendPreference::Klu:
#if defined(SPICE_V2_HAS_KLU)
            out_solver = std::make_unique<KluComplexSolver>();
            return core::Diagnostic::success();
#else
            return core::Diagnostic::error(
                core::StatusCode::Unsupported, "SuiteSparse KLU is not linked in this build");
#endif
        case BackendPreference::Auto:
#if defined(SPICE_V2_HAS_KLU)
            out_solver = std::make_unique<KluComplexSolver>();
#else
            out_solver = make_sparse_complex_solver();
#endif
            return core::Diagnostic::success();
    }
    return core::Diagnostic::error(core::StatusCode::InvalidArgument, "unknown backend preference");
}

}  // namespace spice::linalg
