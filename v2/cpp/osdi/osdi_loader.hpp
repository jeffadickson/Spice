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
 * Generic OSDI 0.3/0.4 dynamic model loader (ARCH-MOD-001, ARCH-MOD-002).
 *
 * - Loads compiled Verilog-A shared libraries (.so / .dll) dynamically.
 * - Validates ABI version (0.3 or 0.4), descriptor tables, terminal counts,
 *   and parameter allowlists; fails closed on any mismatch.
 * - Retains ref-counted native library lifetime for all live device instances.
 * - Bridges any OSDI model onto spice::dae::Device with full trial/rollback.
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
    [[nodiscard]] std::vector<std::string> model_names() const;

    /* Internal descriptor lookup used by create_osdi_device(). */
    [[nodiscard]] const void* find_descriptor(const std::string& model_name) const noexcept;

private:
    OsdiLibrary() = default;

    void* native_handle_{nullptr};
    std::uint32_t major_{0};
    std::uint32_t minor_{0};
    std::uint32_t num_descriptors_{0};
    const void* descriptors_{nullptr};
};

struct OsdiDeviceConfig {
    std::string instance_id;
    std::shared_ptr<OsdiLibrary> library;
    std::string model_name;
    /* Terminal unknown IDs (nullopt for ground node 0). */
    std::vector<std::optional<core::UnknownId>> terminal_unknowns;
    /* Internal node unknown IDs allocated by the compiler (size = num_nodes - num_terminals). */
    std::vector<core::UnknownId> internal_unknowns;
    std::vector<std::pair<std::string, double>> model_params;
    std::vector<std::pair<std::string, double>> instance_params;
    double temperature_k{300.15};
};

/* Query how many terminals and internal nodes a model requires before compiling unknowns. */
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

/* Instantiate a validated, fail-closed OSDI Device implementing spice::dae::Device. */
[[nodiscard]] core::Diagnostic create_osdi_device(
    const OsdiDeviceConfig& config,
    std::unique_ptr<dae::Device>& out_device);

}  // namespace spice::osdi
