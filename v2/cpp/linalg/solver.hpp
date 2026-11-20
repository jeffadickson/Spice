#pragma once

#include "core/types.hpp"

#include <complex>
#include <cstddef>
#include <memory>
#include <span>
#include <string>
#include <vector>

namespace spice::linalg {

using Complex = std::complex<double>;

/*
 * Circuit-independent linear solver contracts (ARCH-LIN-001 .. ARCH-LIN-005).
 *
 * Lifecycle:
 *   1. analyze(pattern) -> validates CSC pattern and caches symbolic structure.
 *   2. factor(values)   -> computes numeric factors for the analyzed pattern.
 *   3. solve(rhs, x)    -> solves A * x = rhs using active numeric factors.
 *
 * Any call to analyze() invalidates previous numeric factors. Calling solve()
 * before a successful factor() returns an explicit error (ARCH-LIN-003).
 */

struct SolverStats {
    std::size_t symbolic_analyses{0};
    std::size_t numeric_factorizations{0};
    std::size_t solves{0};
};

class RealLinearSolver {
public:
    virtual ~RealLinearSolver() = default;
    [[nodiscard]] virtual std::string name() const = 0;
    [[nodiscard]] virtual core::Diagnostic analyze(const core::CscPattern& pattern) = 0;
    [[nodiscard]] virtual core::Diagnostic factor(std::span<const double> values) = 0;
    [[nodiscard]] virtual core::Diagnostic solve(
        std::span<const double> rhs,
        std::span<double> out_x) = 0;
    [[nodiscard]] virtual SolverStats stats() const noexcept = 0;
};

class ComplexLinearSolver {
public:
    virtual ~ComplexLinearSolver() = default;
    [[nodiscard]] virtual std::string name() const = 0;
    [[nodiscard]] virtual core::Diagnostic analyze(const core::CscPattern& pattern) = 0;
    [[nodiscard]] virtual core::Diagnostic factor(std::span<const Complex> values) = 0;
    [[nodiscard]] virtual core::Diagnostic solve(
        std::span<const Complex> rhs,
        std::span<Complex> out_x) = 0;
    [[nodiscard]] virtual SolverStats stats() const noexcept = 0;
};

/*
 * Independent componentwise scaled backward error (ARCH-LIN-004):
 *   max_i |r_i| / ( (|A|*|x|)_i + |b_i| + floor )
 * Returns +Inf if x or residual contains any nonfinite value.
 */
[[nodiscard]] double real_backward_error(
    const core::CscPattern& pattern,
    std::span<const double> values,
    std::span<const double> x,
    std::span<const double> rhs) noexcept;

[[nodiscard]] double complex_backward_error(
    const core::CscPattern& pattern,
    std::span<const Complex> values,
    std::span<const Complex> x,
    std::span<const Complex> rhs) noexcept;

enum class BackendPreference {
    Auto,     /* KLU if linked, otherwise SparseLu */
    Klu,      /* Strict SuiteSparse KLU; fails Unsupported if not linked */
    SparseLu, /* Pure C++20 left-looking sparse LU */
    DenseLu   /* Dense partial-pivoting reference solver */
};

[[nodiscard]] bool klu_is_linked() noexcept;

[[nodiscard]] core::Diagnostic create_real_solver(
    BackendPreference preference,
    std::unique_ptr<RealLinearSolver>& out_solver);

[[nodiscard]] core::Diagnostic create_complex_solver(
    BackendPreference preference,
    std::unique_ptr<ComplexLinearSolver>& out_solver);

}  // namespace spice::linalg
