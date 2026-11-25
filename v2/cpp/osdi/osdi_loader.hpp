#pragma once

#include "core/types.hpp"
#include "dae/device.hpp"

#include <cstdint>
#include <memory>
#include <optional>
#include <string>
#include <utility>
#include <vector>

namespace spice::osdi {

/*
 * Generic Production OSDI 0.3/0.4 Dynamic Model Loader (ARCH-MOD-001, ARCH-MOD-002).
 *
 * - Dynamically loads compiled Verilog-A shared libraries (.osdi / .so / .dll).
 * - Binary-compatible with OpenVAF / OpenVAF-Reloaded OSDI 0.3 and 0.4 ABI:
 *   stride-safe OSDI_DESCRIPTOR_SIZE traversal, OSDI_LIM_TABLE (pnjlim/fetlim/limvds),
 *   OSDI_LOG hook, $simparam table, node collapsing (collapsed_offset), lim_rhs
 *   Newton integration, and load_noise small-signal noise PSD projection.
 * - Fails closed on ABI version mismatches, unknown parameters, or setup errors.
 * - Retains ref-counted native library lifetime for all live device instances.
 */

class OsdiLibrary {
public:
    ~OsdiLibrary();
    OsdiLibrary(const OsdiLibrary&) = delete;
    OsdiLibrary& operator=(const OsdiLibrary&) = delete;

    [[nodiscard]] static core::Diagnostic load(
        const std::string& path,
        std::shared_ptr<OsdiLibrary>& out_lib);

    [[nodiscard]] std::uint32_t abi_major() const noexcept { return major_; }
    [[nodiscard]] std::uint32_t abi_minor() const noexcept { return minor_; }
    [[nodiscard]] std::uint32_t descriptor_size() const noexcept { return descriptor_size_; }
    [[nodiscard]] std::vector<std::string> model_names() const;

    /* Internal stride-safe descriptor lookup used by OSDI loader routines. */
    [[nodiscard]] const void* find_descriptor(const std::string& model_name) const noexcept;

private:
    OsdiLibrary() = default;

    void* native_handle_{nullptr};
    std::uint32_t major_{0};
    std::uint32_t minor_{0};
    std::uint32_t num_descriptors_{0};
    std::uint32_t descriptor_size_{0};
    const void* descriptors_{nullptr};
};

struct OsdiDeviceConfig {
    std::string instance_id;
    std::shared_ptr<OsdiLibrary> library;
    std::string model_name;
    /* Terminal unknown IDs (nullopt for ground node 0). */
    std::vector<std::optional<core::UnknownId>> terminal_unknowns;
    /* Internal node unknown IDs allocated by the compiler. */
    std::vector<core::UnknownId> internal_unknowns;
    std::vector<std::pair<std::string, double>> model_params;
    std::vector<std::pair<std::string, double>> instance_params;
    double temperature_k{300.15};
};

/*
 * Stage-1 prepared OSDI instance after parameter binding, setup_model, setup_instance,
 * and Union-Find node collapsing. The compiler allocates MNA unknowns ONLY for
 * `uncollapsed_internal_nodes` (avoiding singular rows when RS=0, RB=0, etc.).
 */
struct PreparedOsdiInstance {
    std::string instance_id;
    std::shared_ptr<OsdiLibrary> library;
    const void* desc{nullptr};
    std::uint32_t num_terminals{0};
    std::uint32_t num_nodes{0};
    double setup_temperature_k{300.15};
    std::vector<std::uint8_t> model_data;
    std::vector<std::uint8_t> instance_data;
    /* Union-Find canonical node index for each OSDI node (UINT32_MAX = collapsed to ground). */
    std::vector<std::uint32_t> canonical_node;
    /* Surviving uncollapsed internal node indices in [num_terminals, num_nodes) requiring MNA unknowns. */
    std::vector<std::uint32_t> uncollapsed_internal_nodes;
    /* Human-readable names of surviving uncollapsed internal nodes. */
    std::vector<std::string> uncollapsed_internal_names;
};

/*
 * Stage 1: Validate model, apply params, execute setup_model & setup_instance,
 * and compute post-collapse internal nodes before MNA unknown allocation.
 */
[[nodiscard]] core::Diagnostic prepare_osdi_instance(
    const std::string& instance_id,
    std::shared_ptr<OsdiLibrary> library,
    const std::string& model_name,
    std::uint32_t num_terminals,
    const std::vector<std::pair<std::string, double>>& model_params,
    const std::vector<std::pair<std::string, double>>& instance_params,
    double temperature_k,
    PreparedOsdiInstance& out_prepared);

/*
 * Stage 2: Map collapsed OSDI nodes onto final MNA UnknownIds, emit post-collapse
 * CSC stencil coordinates, and construct the live spice::dae::Device.
 */
[[nodiscard]] core::Diagnostic finalize_osdi_instance(
    PreparedOsdiInstance prepared,
    const std::vector<std::optional<core::UnknownId>>& terminal_unknowns,
    const std::vector<core::UnknownId>& uncollapsed_internal_unknowns,
    std::vector<std::pair<std::uint32_t, std::uint32_t>>& out_coords,
    std::unique_ptr<dae::Device>& out_device);

/* Query how many terminals and total internal nodes a model declares (pre-collapse). */
[[nodiscard]] core::Diagnostic query_osdi_model_topology(
    const OsdiLibrary& library,
    const std::string& model_name,
    std::uint32_t& out_num_terminals,
    std::uint32_t& out_num_internal_nodes);

/* Record all CSC Jacobian coordinates needed by an OSDI instance before building CscPattern. */
[[nodiscard]] core::Diagnostic collect_osdi_stencil(
    const OsdiLibrary& library,
    const std::string& model_name,
    const std::vector<std::optional<core::UnknownId>>& node_unknowns,
    std::vector<std::pair<std::uint32_t, std::uint32_t>>& out_coords);

/* Direct single-step OSDI device instantiation (for unit tests without compiler collapsing). */
[[nodiscard]] core::Diagnostic create_osdi_device(
    const OsdiDeviceConfig& config,
    std::unique_ptr<dae::Device>& out_device);

}  // namespace spice::osdi
