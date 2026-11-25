#pragma once

#include "core/types.hpp"

#include <cmath>
#include <cstddef>
#include <optional>
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
    double temperature_k{300.15};     /* 27 C nominal SI reference */
    AnalysisMode mode{AnalysisMode::OperatingPoint};
    std::span<const double> x;        /* Full MNA unknown vector (size == dimension) */
    std::size_t newton_iter{0};       /* Newton iteration index (0 triggers OSDI INIT_LIM) */
    bool enable_limiting{false};      /* True during Newton iterations; false for AC/FD */
    bool is_line_search_probe{false}; /* True during backtracking probe (preserve Newton prev_state) */
};

/*
 * Pre-allocated accumulation buffers for one DAE evaluation pass.
 * Devices stamp only into validated unknown rows and pre-bound CSC slots;
 * they never see raw third-party solver types or allocate in the hot loop.
 */
struct StampTarget {
    std::span<double> f;           /* Static residual vector F(x, t) */
    std::span<double> q;           /* Stored charge/flux vector Q(x, t) */
    std::span<double> df_dx;       /* CSC values for G = dF/dx */
    std::span<double> dq_dx;       /* CSC values for C = dQ/dx */
    std::span<double> f_lim{};     /* Optional OSDI resistive limiting RHS: J_r*(x_solve - x_lim) */
    std::span<double> q_lim{};     /* Optional OSDI reactive limiting RHS: J_c*(x_solve - x_lim) */
    bool* step_limited{nullptr};   /* Set true if any device applied voltage limiting ($limit) */

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

    [[nodiscard]] bool add_f_lim(core::UnknownId row, double value) noexcept {
        if (f_lim.empty()) return true;
        if (row.value >= f_lim.size() || !std::isfinite(value)) return false;
        f_lim[row.value] += value;
        return true;
    }

    [[nodiscard]] bool add_q_lim(core::UnknownId row, double value) noexcept {
        if (q_lim.empty()) return true;
        if (row.value >= q_lim.size() || !std::isfinite(value)) return false;
        q_lim[row.value] += value;
        return true;
    }

    void mark_limited() noexcept {
        if (step_limited != nullptr) *step_limited = true;
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

/* Small-signal current noise source between (n1, n2) in A^2/Hz for adjoint .NOISE projection. */
struct NoiseSourceContribution {
    std::string device_id;
    std::string source_name;
    std::optional<core::UnknownId> n1;
    std::optional<core::UnknownId> n2;
    double psd_a2_per_hz{0.0};
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

    /* Emit device-internal small-signal current noise PSDs (A^2/Hz) at operating point x_op. */
    virtual void evaluate_noise(
        double /*freq_hz*/,
        double /*temperature_k*/,
        std::span<const double> /*x_op*/,
        std::vector<NoiseSourceContribution>& /*out_sources*/) const {}

    /* Promote trial state to accepted state after step acceptance. */
    virtual void commit_trial() noexcept = 0;

    /* Discard trial state and restore last accepted state after step rejection. */
    virtual void rollback_trial() noexcept = 0;

    /* Reset any transient history/hysteresis state before starting a fresh transient run. */
    virtual void reset_history() noexcept {}
};

}  // namespace spice::dae
