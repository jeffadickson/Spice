#pragma once

#include "devices/linear_devices.hpp"

#include <complex>
#include <span>
#include <string>
#include <vector>

namespace spice::audit {

enum class CheckStatus {
    Passed,
    Failed,
    NotChecked
};

struct AuditOptions {
    double current_abs_tol{1.0e-11};  /* A */
    double voltage_abs_tol{1.0e-11};  /* V */
    double power_abs_tol{1.0e-11};    /* W or J */
    double rel_tol{1.0e-8};
};

struct AuditCheck {
    std::string id;
    CheckStatus status{CheckStatus::NotChecked};
    double discrepancy{0.0};
    double tolerance{0.0};
    std::string unit;
    std::string reason;
};

struct AuditReport {
    std::vector<AuditCheck> checks;

    [[nodiscard]] bool any_failed() const noexcept;
    [[nodiscard]] bool all_checked_passed() const noexcept;
    void merge_worst(const AuditReport& other);
};

enum class TransientAuditMethod {
    BackwardEuler,
    Trapezoidal
};

/* Independent DC KCL (all nodes incl. ground), voltage constraints, and power conservation (AUDIT-002). */
[[nodiscard]] AuditReport audit_dc(
    const devices::CompiledPlan& plan,
    std::span<const double> x,
    const AuditOptions& opts = {});

/* Independent complex AC KCL, voltage constraints, and Tellegen complex power balance (AUDIT-003). */
[[nodiscard]] AuditReport audit_ac(
    const devices::CompiledPlan& plan,
    std::span<const std::complex<double>> x,
    double frequency_hz,
    const AuditOptions& opts = {});

/* Independent discrete transient KCL, source constraints, and energy balance incl. BE dissipation (AUDIT-004). */
[[nodiscard]] AuditReport audit_transient_step(
    const devices::CompiledPlan& plan,
    std::span<const double> prev_x,
    std::span<const double> curr_x,
    double prev_t,
    double curr_t,
    TransientAuditMethod method,
    const AuditOptions& opts = {});

}  // namespace spice::audit
