#pragma once

#include "core/circuit.hpp"
#include "dae/device.hpp"

#include <complex>
#include <cstdint>
#include <memory>
#include <optional>
#include <span>
#include <string>
#include <unordered_map>
#include <vector>

namespace spice::devices {

/*
 * Immutable compiled circuit plan mapping a validated Circuit into:
 * - Unknown ordering: non-ground node voltages v(1..N-1), then branch currents.
 * - Canonical CSC sparsity pattern constructed directly from device stencils.
 * - Pre-bound universal DAE Device instances (zero matrix-slot lookups in hot loop).
 */
struct CompiledPlan {
    core::Circuit circuit;
    core::CscPattern pattern;
    std::vector<core::SignalDescriptor> signals;
    std::unordered_map<std::string, core::UnknownId> branch_unknowns;
    std::vector<std::unique_ptr<dae::Device>> devices;

    [[nodiscard]] std::uint32_t unknown_count() const noexcept { return pattern.dimension; }

    /* Map non-ground NodeId -> UnknownId (returns nullopt for ground node 0). */
    [[nodiscard]] static std::optional<core::UnknownId> node_unknown(core::NodeId n) noexcept {
        if (n.is_ground()) return std::nullopt;
        return core::UnknownId{n.value - 1u};
    }

    /* Evaluate all pre-bound DAE devices into (f, q, df_dx, dq_dx). */
    [[nodiscard]] core::Diagnostic evaluate_all(
        const dae::EvalContext& ctx,
        dae::StampTarget& target) const;

    /* Populate the AC small-signal RHS b_ac for (G + j*omega*C) * x = b_ac. */
    void fill_ac_rhs(std::span<std::complex<double>> rhs) const noexcept;

    /* Collect sorted unique source breakpoints strictly inside (t_start, t_stop). */
    [[nodiscard]] std::vector<double> collect_breakpoints(double t_start, double t_stop) const;

    /* Override a single DC voltage/current source value temporarily during a DC sweep. */
    [[nodiscard]] core::Diagnostic set_dc_source_override(
        const std::string& source_id,
        std::optional<double> value);

    void commit_all_trials() noexcept;
    void rollback_all_trials() noexcept;

    /* Indices into `devices` for independent sources (used by DC sweeps). */
    std::unordered_map<std::string, std::size_t> source_device_indices;
};

/* Compile a Circuit into a pre-bound CompiledPlan with validated CSC sparsity. */
[[nodiscard]] core::Diagnostic compile_circuit(
    const core::Circuit& circuit,
    CompiledPlan& out_plan,
    std::uint64_t max_unknowns = 1'000'000);

}  // namespace spice::devices
