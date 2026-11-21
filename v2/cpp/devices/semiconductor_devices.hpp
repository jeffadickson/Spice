#pragma once

#include "core/circuit.hpp"
#include "dae/device.hpp"

#include <memory>
#include <optional>
#include <utility>
#include <vector>

namespace spice::devices {

using StencilCoords = std::vector<std::pair<std::uint32_t, std::uint32_t>>;

/* Record CSC stencils and instantiate native semiconductor DAE devices (ARCH-CAP-004). */

void record_mosfet_stencil(
    StencilCoords& coords,
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s,
    std::optional<core::UnknownId> b);

[[nodiscard]] std::unique_ptr<dae::Device> create_mosfet_device(
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s,
    std::optional<core::UnknownId> b,
    core::MosfetSpec spec);

void record_bjt_stencil(
    StencilCoords& coords,
    std::optional<core::UnknownId> c,
    std::optional<core::UnknownId> b,
    std::optional<core::UnknownId> e);

[[nodiscard]] std::unique_ptr<dae::Device> create_bjt_device(
    std::optional<core::UnknownId> c,
    std::optional<core::UnknownId> b,
    std::optional<core::UnknownId> e,
    core::BjtSpec spec);

void record_jfet_stencil(
    StencilCoords& coords,
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s);

[[nodiscard]] std::unique_ptr<dae::Device> create_jfet_device(
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s,
    core::JfetSpec spec);

}  // namespace spice::devices
