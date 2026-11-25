#pragma once

#include "audit/physical_audit.hpp"
#include "devices/linear_devices.hpp"
#include "linalg/solver.hpp"
#include "solver/newton.hpp"

#include <complex>
#include <cstddef>
#include <optional>
#include <string>
#include <vector>

namespace spice::analysis {

struct AnalysisCommonOptions {
    linalg::BackendPreference backend{linalg::BackendPreference::Auto};
    bool enable_audits{true};
    audit::AuditOptions audit_options{};
    solver::NewtonOptions newton_options{};
    double max_backward_error{1.0e-10};
    double temperature_k{300.15}; /* 27 C nominal SI reference */
};

struct OpResult {
    std::string backend_name;
    std::vector<double> values;
    double backward_error{0.0};
    linalg::SolverStats solver_stats{};
    std::optional<solver::NewtonReport> newton;
    std::optional<audit::AuditReport> audit;
};

struct DcSweepOptions {
    std::string source_id;
    double start{0.0};
    double stop{0.0};
    double step{0.0};
    std::size_t max_points{100'000};
    AnalysisCommonOptions common{};
};

struct DcSweepPoint {
    double sweep_value{0.0};
    std::vector<double> values;
};

struct DcSweepResult {
    std::string backend_name;
    std::vector<DcSweepPoint> points;
    double worst_backward_error{0.0};
    linalg::SolverStats solver_stats{};
};

struct AcOptions {
    std::vector<double> frequencies_hz;
    AnalysisCommonOptions common{};
};

struct AcPoint {
    double frequency_hz{0.0};
    std::vector<std::complex<double>> values;
};

struct AcResult {
    std::string backend_name;
    std::vector<AcPoint> points;
    std::vector<double> operating_point;
    double worst_backward_error{0.0};
    linalg::SolverStats solver_stats{};
    std::optional<audit::AuditReport> audit;
};

enum class IntegrationMethod {
    BackwardEuler,
    Trapezoidal,
    Bdf2
};

enum class StartupMode {
    OperatingPoint,
    Uic
};

struct TransientOptions {
    double step_s{0.0};
    double stop_s{0.0};
    IntegrationMethod method{IntegrationMethod::Trapezoidal};
    StartupMode startup{StartupMode::OperatingPoint};
    std::size_t max_steps{1'000'000};
    /* Adaptive Local Truncation Error (LTE) step control (ARCH-EQ-004). */
    bool adaptive{false};
    double min_step_s{1.0e-15};
    double max_step_s{0.0}; /* 0 => default to step_s */
    double lte_reltol{1.0e-3};
    double lte_abstol{1.0e-6};
    AnalysisCommonOptions common{};
};

struct TransientSample {
    double time_s{0.0};
    std::vector<double> values;
};

struct TransientResult {
    std::string backend_name;
    std::vector<TransientSample> samples;
    double worst_backward_error{0.0};
    linalg::SolverStats solver_stats{};
    std::size_t total_newton_iterations{0};
    std::size_t accepted_steps{0};
    std::size_t rejected_steps{0};
    double min_step_used_s{0.0};
    double max_step_used_s{0.0};
    std::optional<audit::AuditReport> audit;
};

[[nodiscard]] core::Diagnostic run_op(
    devices::CompiledPlan& plan,
    const AnalysisCommonOptions& opts,
    OpResult& out_result);

[[nodiscard]] core::Diagnostic run_dc_sweep(
    devices::CompiledPlan& plan,
    const DcSweepOptions& opts,
    DcSweepResult& out_result);

[[nodiscard]] core::Diagnostic run_ac(
    devices::CompiledPlan& plan,
    const AcOptions& opts,
    AcResult& out_result);

[[nodiscard]] core::Diagnostic run_transient(
    devices::CompiledPlan& plan,
    const TransientOptions& opts,
    TransientResult& out_result);

}  // namespace spice::analysis
