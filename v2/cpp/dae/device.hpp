#pragma once

#include "core/types.hpp"

#include <cmath>
#include <cstddef>
#include <span>
#include <string>
#include <vector>

namespace spice::dae {

/*
 * Universal Modified-Nodal DAE Contract (ARCH-EQ-001, ARCH-MOD-001).
 *
 * Every built-in primitive, behavioral B-source, and OSDI compact model
 * implements the same continuous-time equation contribution:
 *
 *     F(x, t, p) + dQ(x, t, p)/dt = 0
 *
 * Sign and unit conventions (SI everywhere):
 * - Currents leaving a node into a branch are positive in KCL rows (A).
 * - Branch voltage equations use volts (V).
 * - Charges Q have coulombs (C) for KCL rows and flux linkages (Wb) for
 *   inductive branch rows.
 * - dF/dx is conductance/dimensionless Jacobian; dQ/dx is capacitance/inductance.
 */

enum class AnalysisMode {
    OperatingPoint,
    DcSweep,
    AcLinearize,
    Transient,
    HarmonicBalance
};

struct PinDescriptor {
    std::string name;
    std::string role;  /* e.g. "positive", "negative", "gate", "drain", "source", "bulk" */
};

struct DeviceDescriptor {
    std::string kind_id;       /* Stable canonical ID, e.g. "spice.resistor", "osdi.bsim4" */
    std::string display_name;
    std::vector<PinDescriptor> pins;
    bool supports_ac{true};
    bool supports_noise{false};
    bool supports_hb{false};
    bool thread_safe_eval{true};
};

struct EvalContext {
    double time_s{0.0};
    double temperature_k{300.15};  /* 27 C nominal SI reference */
    AnalysisMode mode{AnalysisMode::OperatingPoint};
    std::span<const double> x;     /* Full MNA unknown vector (size == dimension) */
};

/*
 * Pre-allocated accumulation buffers for one DAE evaluation pass.
 * Devices stamp only into validated unknown rows and pre-bound CSC slots;
 * they never see raw third-party solver types or allocate in the hot loop.
 */
struct StampTarget {
    std::span<double> f;      /* Static residual vector F(x, t) */
    std::span<double> q;      /* Stored charge/flux vector Q(x, t) */
    std::span<double> df_dx;  /* CSC values for G = dF/dx */
    std::span<double> dq_dx;  /* CSC values for C = dQ/dx */

    [[nodiscard]] bool add_f(core::UnknownId row, double value) noexcept {
        if (row.value >= f.size() || !std::isfinite(value)) return false;
        f[row.value] += value;
        return true;
    }

    [[nodiscard]] bool add_q(core::UnknownId row, double value) noexcept {
        if (row.value >= q.size() || !std::isfinite(value)) return false;
        q[row.value] += value;
        return true;
    }

    [[nodiscard]] bool add_df(core::SlotId slot, double value) noexcept {
        if (slot.value >= df_dx.size() || !std::isfinite(value)) return false;
        df_dx[slot.value] += value;
        return true;
    }

    [[nodiscard]] bool add_dq(core::SlotId slot, double value) noexcept {
        if (slot.value >= dq_dx.size() || !std::isfinite(value)) return false;
        dq_dx[slot.value] += value;
        return true;
    }
};

/*
 * Universal Device interface shared by all built-in and OSDI models.
 * Trial state written during evaluate() must not overwrite accepted history
 * until commit_trial() is called; rollback_trial() must restore accepted state
 * identically after any rejected Newton or timestep attempt (ARCH-EQ-004).
 */
class Device {
public:
    virtual ~Device() = default;

    [[nodiscard]] virtual const DeviceDescriptor& descriptor() const noexcept = 0;

    /* Pre-bind device stamp locations to validated CSC matrix slots once per plan. */
    [[nodiscard]] virtual core::Diagnostic bind_slots(const core::CscPattern& pattern) = 0;

    /* Evaluate F, Q, dF/dx, dQ/dx at ctx.x into pre-bound slots. */
    [[nodiscard]] virtual core::Diagnostic evaluate(
        const EvalContext& ctx,
        StampTarget& target) = 0;

    /* Promote trial state to accepted state after step acceptance. */
    virtual void commit_trial() noexcept = 0;

    /* Discard trial state and restore last accepted state after step rejection. */
    virtual void rollback_trial() noexcept = 0;

    /* Reset any transient history/hysteresis state before starting a fresh transient run. */
    virtual void reset_history() noexcept {}
};

}  // namespace spice::dae
