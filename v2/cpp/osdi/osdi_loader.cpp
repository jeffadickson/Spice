#include "osdi/osdi_loader.hpp"
#include "osdi/osdi_abi.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <dlfcn.h>
#endif

namespace spice::osdi {

namespace {

using core::CscPattern;
using core::Diagnostic;
using core::SlotId;
using core::StatusCode;
using core::UnknownId;
using dae::AnalysisMode;
using dae::Device;
using dae::DeviceDescriptor;
using dae::EvalContext;
using dae::StampTarget;

constexpr std::uint32_t kMaxDescriptors = 1024u;
constexpr std::uint32_t kMaxBufferBytes = 16u * 1024u * 1024u;

bool iequals(const char* a, const std::string& b) noexcept {
    if (a == nullptr) return false;
    std::size_t i = 0;
    while (a[i] != '\0' && i < b.size()) {
        if (std::tolower(static_cast<unsigned char>(a[i])) !=
            std::tolower(static_cast<unsigned char>(b[i]))) {
            return false;
        }
        ++i;
    }
    return a[i] == '\0' && i == b.size();
}

void* open_native_lib(const std::string& path, std::string& out_err) {
#if defined(_WIN32)
    int wlen = MultiByteToWideChar(CP_UTF8, 0, path.c_str(), -1, nullptr, 0);
    if (wlen <= 0) {
        out_err = "invalid UTF-8 library path";
        return nullptr;
    }
    std::wstring wpath(static_cast<std::size_t>(wlen), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, path.c_str(), -1, wpath.data(), wlen);
    HMODULE h = LoadLibraryW(wpath.c_str());
    if (h == nullptr) {
        out_err = "LoadLibraryW failed for " + path;
    }
    return reinterpret_cast<void*>(h);
#else
    void* h = dlopen(path.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (h == nullptr) {
        const char* msg = dlerror();
        out_err = msg != nullptr ? std::string(msg) : ("dlopen failed for " + path);
    }
    return h;
#endif
}

void* lookup_symbol(void* handle, const char* name) noexcept {
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(reinterpret_cast<HMODULE>(handle), name));
#else
    return dlsym(handle, name);
#endif
}

void close_native_lib(void* handle) noexcept {
    if (handle == nullptr) return;
#if defined(_WIN32)
    FreeLibrary(reinterpret_cast<HMODULE>(handle));
#else
    dlclose(handle);
#endif
}

/* Concrete spice::dae::Device wrapping one validated OSDI 0.3/0.4 model instance. */
class OsdiDevice final : public Device {
public:
    OsdiDevice(
        std::shared_ptr<OsdiLibrary> lib,
        const SpiceOsdiDescriptor* desc,
        std::vector<std::optional<UnknownId>> node_unknowns,
        std::vector<std::uint8_t> model_data,
        std::vector<std::uint8_t> instance_data)
        : lib_(std::move(lib)),
          desc_(desc),
          node_unknowns_(std::move(node_unknowns)),
          model_data_(std::move(model_data)),
          instance_data_(std::move(instance_data)),
          accepted_instance_data_(instance_data_),
          prev_state_(desc->num_states, 0.0),
          next_state_(desc->num_states, 0.0),
          accepted_state_(desc->num_states, 0.0) {
        device_desc_.kind_id = std::string("osdi.") + (desc->name != nullptr ? desc->name : "unknown");
        device_desc_.display_name = desc->name != nullptr ? desc->name : "OSDI Device";
        for (std::uint32_t i = 0; i < desc->num_terminals; ++i) {
            const char* nname = (desc->nodes != nullptr && desc->nodes[i].name != nullptr)
                                    ? desc->nodes[i].name
                                    : "term";
            device_desc_.pins.push_back({nname, "terminal"});
        }
        device_desc_.supports_ac = true;
        device_desc_.thread_safe_eval = false; /* Conservative per ARCH-MOD-004 */
    }

    const DeviceDescriptor& descriptor() const noexcept override { return device_desc_; }

    Diagnostic bind_slots(const CscPattern& pattern) override {
        /* Map each OSDI node index -> compact dense evaluation buffer index (0 = ground sink,
         * 1..num_nodes = local nodes) so OSDI load_residual_* writes only to owned bounded arrays. */
        local_node_slot_.resize(desc_->num_nodes);
        for (std::uint32_t i = 0; i < desc_->num_nodes; ++i) {
            local_node_slot_[i] = i + 1u; /* slot 0 reserved as dummy sink */
        }
        if (desc_->node_mapping_offset + desc_->num_nodes * sizeof(std::uint32_t) > instance_data_.size()) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "OSDI node_mapping_offset exceeds instance_size");
        }
        std::memcpy(
            instance_data_.data() + desc_->node_mapping_offset,
            local_node_slot_.data(),
            desc_->num_nodes * sizeof(std::uint32_t));
        accepted_instance_data_ = instance_data_;

        /* Pre-bind every OSDI Jacobian entry to its CSC SlotId (or nullopt if grounded). */
        jac_resist_buf_.assign(desc_->num_jacobian_entries, 0.0);
        jac_react_buf_.assign(desc_->num_jacobian_entries, 0.0);
        jac_slots_.resize(desc_->num_jacobian_entries);

        for (std::uint32_t j = 0; j < desc_->num_jacobian_entries; ++j) {
            const auto& entry = desc_->jacobian_entries[j];
            if (entry.nodes.node_1 >= desc_->num_nodes || entry.nodes.node_2 >= desc_->num_nodes) {
                return Diagnostic::error(
                    StatusCode::InvalidArgument, "OSDI Jacobian node index out of range");
            }
            const auto r_unk = node_unknowns_[entry.nodes.node_1];
            const auto c_unk = node_unknowns_[entry.nodes.node_2];
            if (r_unk.has_value() && c_unk.has_value()) {
                SlotId slot{};
                if (!pattern.find_slot(r_unk->value, c_unk->value, slot)) {
                    return Diagnostic::error(
                        StatusCode::InvalidArgument, "OSDI Jacobian CSC slot missing from pattern");
                }
                jac_slots_[j] = slot;
            } else {
                jac_slots_[j] = std::nullopt;
            }

            /* Bind OSDI's resist and react destination pointers to our owned local buffers. */
            if ((entry.flags & SPICE_OSDI_JACOBIAN_ENTRY_RESIST) != 0u) {
                const std::size_t off =
                    desc_->jacobian_ptr_resist_offset + static_cast<std::size_t>(j) * sizeof(double*);
                if (off + sizeof(double*) > instance_data_.size()) {
                    return Diagnostic::error(
                        StatusCode::InvalidArgument, "OSDI resist Jacobian ptr offset out of range");
                }
                double* ptr = &jac_resist_buf_[j];
                std::memcpy(instance_data_.data() + off, &ptr, sizeof(double*));
            }
            if ((entry.flags & SPICE_OSDI_JACOBIAN_ENTRY_REACT) != 0u) {
                const std::size_t off = entry.react_ptr_off;
                if (off + sizeof(double*) > instance_data_.size()) {
                    return Diagnostic::error(
                        StatusCode::InvalidArgument, "OSDI react Jacobian ptr offset out of range");
                }
                double* ptr = &jac_react_buf_[j];
                std::memcpy(instance_data_.data() + off, &ptr, sizeof(double*));
            }
        }
        accepted_instance_data_ = instance_data_;
        bound_ = true;
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& target) override {
        if (!bound_) {
            return Diagnostic::error(StatusCode::InvalidArgument, "OSDI device not bound");
        }

        /* Gather local node voltages into OSDI prev_solve buffer (index 0 = ground 0V). */
        std::vector<double> local_x(desc_->num_nodes + 1u, 0.0);
        for (std::uint32_t i = 0; i < desc_->num_nodes; ++i) {
            if (node_unknowns_[i].has_value()) {
                const std::uint32_t u = node_unknowns_[i]->value;
                if (u >= ctx.x.size() || !std::isfinite(ctx.x[u])) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite input to OSDI eval");
                }
                local_x[local_node_slot_[i]] = ctx.x[u];
            }
        }

        SpiceOsdiSimInfo sim_info{};
        sim_info.abstime = ctx.time_s;
        sim_info.prev_solve = local_x.data();
        sim_info.prev_state = prev_state_.empty() ? nullptr : prev_state_.data();
        sim_info.next_state = next_state_.empty() ? nullptr : next_state_.data();
        sim_info.flags = SPICE_OSDI_CALC_RESIST_RESIDUAL | SPICE_OSDI_CALC_REACT_RESIDUAL |
                         SPICE_OSDI_CALC_RESIST_JACOBIAN | SPICE_OSDI_CALC_REACT_JACOBIAN;
        switch (ctx.mode) {
            case AnalysisMode::OperatingPoint:
            case AnalysisMode::DcSweep:
                sim_info.flags |= SPICE_OSDI_ANALYSIS_DC | SPICE_OSDI_ANALYSIS_STATIC;
                break;
            case AnalysisMode::AcLinearize:
                sim_info.flags |= SPICE_OSDI_ANALYSIS_AC;
                break;
            case AnalysisMode::Transient:
            case AnalysisMode::HarmonicBalance:
                sim_info.flags |= SPICE_OSDI_ANALYSIS_TRAN;
                break;
        }

        const std::uint32_t ret_flags =
            desc_->eval(nullptr, instance_data_.data(), model_data_.data(), &sim_info);
        if ((ret_flags & SPICE_OSDI_EVAL_RET_FLAG_FATAL) != 0u) {
            return Diagnostic::error(
                StatusCode::NumericalFailure, "OSDI model reported fatal evaluation error");
        }

        /* Load resist residual -> target.f, react residual -> target.q. */
        std::vector<double> f_local(desc_->num_nodes + 1u, 0.0);
        std::vector<double> q_local(desc_->num_nodes + 1u, 0.0);
        if (desc_->load_residual_resist != nullptr) {
            desc_->load_residual_resist(instance_data_.data(), model_data_.data(), f_local.data());
        }
        if (desc_->load_residual_react != nullptr) {
            desc_->load_residual_react(instance_data_.data(), model_data_.data(), q_local.data());
        }

        for (std::uint32_t i = 0; i < desc_->num_nodes; ++i) {
            if (!node_unknowns_[i].has_value()) continue;
            const std::uint32_t loc = local_node_slot_[i];
            if (!target.add_f(*node_unknowns_[i], f_local[loc]) ||
                !target.add_q(*node_unknowns_[i], q_local[loc])) {
                return Diagnostic::error(
                    StatusCode::NumericalFailure, "nonfinite OSDI residual contribution");
            }
        }

        /* Load resist Jacobian -> target.df_dx, react Jacobian (alpha=1) -> target.dq_dx. */
        std::fill(jac_resist_buf_.begin(), jac_resist_buf_.end(), 0.0);
        std::fill(jac_react_buf_.begin(), jac_react_buf_.end(), 0.0);
        if (desc_->load_jacobian_resist != nullptr) {
            desc_->load_jacobian_resist(instance_data_.data(), model_data_.data());
        }
        if (desc_->load_jacobian_react != nullptr) {
            desc_->load_jacobian_react(instance_data_.data(), model_data_.data(), 1.0);
        }

        for (std::uint32_t j = 0; j < desc_->num_jacobian_entries; ++j) {
            if (!jac_slots_[j].has_value()) continue;
            const auto flags = desc_->jacobian_entries[j].flags;
            if ((flags & SPICE_OSDI_JACOBIAN_ENTRY_RESIST) != 0u) {
                if (!target.add_df(*jac_slots_[j], jac_resist_buf_[j])) {
                    return Diagnostic::error(
                        StatusCode::NumericalFailure, "nonfinite OSDI resist Jacobian");
                }
            }
            if ((flags & SPICE_OSDI_JACOBIAN_ENTRY_REACT) != 0u) {
                if (!target.add_dq(*jac_slots_[j], jac_react_buf_[j])) {
                    return Diagnostic::error(
                        StatusCode::NumericalFailure, "nonfinite OSDI react Jacobian");
                }
            }
        }

        return Diagnostic::success();
    }

    void commit_trial() noexcept override {
        accepted_instance_data_ = instance_data_;
        accepted_state_ = next_state_;
        prev_state_ = next_state_;
    }

    void rollback_trial() noexcept override {
        instance_data_ = accepted_instance_data_;
        next_state_ = accepted_state_;
        prev_state_ = accepted_state_;
    }

private:
    std::shared_ptr<OsdiLibrary> lib_; /* Retains dlopen/LoadLibrary lifetime (ARCH-MOD-002). */
    const SpiceOsdiDescriptor* desc_;
    DeviceDescriptor device_desc_;
    std::vector<std::optional<UnknownId>> node_unknowns_;
    std::vector<std::uint32_t> local_node_slot_;
    std::vector<std::uint8_t> model_data_;
    std::vector<std::uint8_t> instance_data_;
    std::vector<std::uint8_t> accepted_instance_data_;
    std::vector<double> prev_state_;
    std::vector<double> next_state_;
    std::vector<double> accepted_state_;
    std::vector<double> jac_resist_buf_;
    std::vector<double> jac_react_buf_;
    std::vector<std::optional<SlotId>> jac_slots_;
    bool bound_{false};
};

}  // namespace

OsdiLibrary::~OsdiLibrary() {
    close_native_lib(native_handle_);
    native_handle_ = nullptr;
}

Diagnostic OsdiLibrary::load(const std::string& path, std::shared_ptr<OsdiLibrary>& out_lib) {
    out_lib.reset();
    if (path.empty()) {
        return Diagnostic::error(StatusCode::InvalidArgument, "OSDI library path must not be empty");
    }
    std::string err;
    void* handle = open_native_lib(path, err);
    if (handle == nullptr) {
        return Diagnostic::error(StatusCode::InvalidArgument, "failed to open OSDI library: " + err, path);
    }

    auto lib = std::shared_ptr<OsdiLibrary>(new OsdiLibrary());
    lib->native_handle_ = handle;

    const auto* maj = static_cast<const std::uint32_t*>(lookup_symbol(handle, "OSDI_VERSION_MAJOR"));
    const auto* min = static_cast<const std::uint32_t*>(lookup_symbol(handle, "OSDI_VERSION_MINOR"));
    const auto* num = static_cast<const std::uint32_t*>(lookup_symbol(handle, "OSDI_NUM_DESCRIPTORS"));
    const void* descs = lookup_symbol(handle, "OSDI_DESCRIPTORS");

    if (maj == nullptr || min == nullptr || num == nullptr || descs == nullptr) {
        return Diagnostic::error(
            StatusCode::Unsupported, "OSDI library missing required ABI symbols", path);
    }
    if (*maj != 0u || (*min != 3u && *min != 4u)) {
        return Diagnostic::error(
            StatusCode::Unsupported,
            "unsupported OSDI ABI version " + std::to_string(*maj) + "." + std::to_string(*min),
            path);
    }
    if (*num == 0u || *num > kMaxDescriptors) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "invalid OSDI_NUM_DESCRIPTORS count", path);
    }

    lib->major_ = *maj;
    lib->minor_ = *min;
    lib->num_descriptors_ = *num;
    lib->descriptors_ = descs;
    out_lib = std::move(lib);
    return Diagnostic::success();
}

std::vector<std::string> OsdiLibrary::model_names() const {
    std::vector<std::string> names;
    const auto* arr = static_cast<const SpiceOsdiDescriptor*>(descriptors_);
    if (arr == nullptr) return names;
    names.reserve(num_descriptors_);
    for (std::uint32_t i = 0; i < num_descriptors_; ++i) {
        if (arr[i].name != nullptr) names.emplace_back(arr[i].name);
    }
    return names;
}

const void* OsdiLibrary::find_descriptor(const std::string& model_name) const noexcept {
    const auto* arr = static_cast<const SpiceOsdiDescriptor*>(descriptors_);
    if (arr == nullptr) return nullptr;
    for (std::uint32_t i = 0; i < num_descriptors_; ++i) {
        if (iequals(arr[i].name, model_name)) return &arr[i];
    }
    return nullptr;
}

Diagnostic query_osdi_model_topology(
    const OsdiLibrary& library,
    const std::string& model_name,
    std::uint32_t& out_num_terminals,
    std::uint32_t& out_num_internal_nodes) {
    const auto* desc = static_cast<const SpiceOsdiDescriptor*>(library.find_descriptor(model_name));
    if (desc == nullptr) {
        return Diagnostic::error(StatusCode::InvalidArgument, "OSDI model not found: " + model_name);
    }
    if (desc->num_terminals == 0u || desc->num_nodes < desc->num_terminals) {
        return Diagnostic::error(StatusCode::InvalidArgument, "malformed OSDI node counts");
    }
    out_num_terminals = desc->num_terminals;
    out_num_internal_nodes = desc->num_nodes - desc->num_terminals;
    return Diagnostic::success();
}

Diagnostic collect_osdi_stencil(
    const OsdiLibrary& library,
    const std::string& model_name,
    const std::vector<std::optional<UnknownId>>& node_unknowns,
    std::vector<std::pair<std::uint32_t, std::uint32_t>>& out_coords) {
    const auto* desc = static_cast<const SpiceOsdiDescriptor*>(library.find_descriptor(model_name));
    if (desc == nullptr) {
        return Diagnostic::error(StatusCode::InvalidArgument, "OSDI model not found: " + model_name);
    }
    if (node_unknowns.size() != desc->num_nodes) {
        return Diagnostic::error(StatusCode::InvalidArgument, "OSDI node unknown mapping count mismatch");
    }
    for (std::uint32_t j = 0; j < desc->num_jacobian_entries; ++j) {
        const auto& e = desc->jacobian_entries[j];
        if (e.nodes.node_1 >= desc->num_nodes || e.nodes.node_2 >= desc->num_nodes) {
            return Diagnostic::error(StatusCode::InvalidArgument, "OSDI Jacobian node out of range");
        }
        const auto r = node_unknowns[e.nodes.node_1];
        const auto c = node_unknowns[e.nodes.node_2];
        if (r.has_value() && c.has_value()) {
            out_coords.emplace_back(r->value, c->value);
        }
    }
    return Diagnostic::success();
}

Diagnostic create_osdi_device(
    const OsdiDeviceConfig& config,
    std::unique_ptr<Device>& out_device) {
    out_device.reset();
    if (!config.library) {
        return Diagnostic::error(StatusCode::InvalidArgument, "OSDI library handle is null");
    }
    const auto* desc =
        static_cast<const SpiceOsdiDescriptor*>(config.library->find_descriptor(config.model_name));
    if (desc == nullptr) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "OSDI model not found: " + config.model_name, config.instance_id);
    }
    if (desc->eval == nullptr || desc->setup_model == nullptr || desc->setup_instance == nullptr) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "OSDI descriptor missing required callbacks", config.model_name);
    }
    if (config.terminal_unknowns.size() != desc->num_terminals) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "OSDI terminal count mismatch", config.instance_id);
    }
    const std::uint32_t expected_internal = desc->num_nodes - desc->num_terminals;
    if (config.internal_unknowns.size() != expected_internal) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "OSDI internal node count mismatch", config.instance_id);
    }
    if (desc->model_size == 0u || desc->model_size > kMaxBufferBytes ||
        desc->instance_size == 0u || desc->instance_size > kMaxBufferBytes) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "OSDI model or instance buffer size out of bounds", config.model_name);
    }

    std::vector<std::uint8_t> model_data(desc->model_size, 0u);
    std::vector<std::uint8_t> instance_data(desc->instance_size, 0u);

    /* Apply model and instance parameters with fail-closed allowlist checking (ARCH-MOD-001). */
    auto apply_param = [&](const std::string& name, double val, bool is_instance) -> Diagnostic {
        if (desc->access == nullptr || desc->param_opvar == nullptr) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "OSDI model exposes no parameter accessor", name);
        }
        const std::uint32_t total = desc->num_params;
        for (std::uint32_t id = 0; id < total; ++id) {
            const auto& p = desc->param_opvar[id];
            const std::uint32_t kind = p.flags & SPICE_OSDI_PARA_KIND_MASK;
            if (is_instance && kind != SPICE_OSDI_PARA_KIND_INST) continue;
            if (!is_instance && kind != SPICE_OSDI_PARA_KIND_MODEL) continue;
            bool matched = false;
            if (p.name != nullptr) {
                for (std::uint32_t a = 0; a <= p.num_alias; ++a) {
                    if (iequals(p.name[a], name)) {
                        matched = true;
                        break;
                    }
                }
            }
            if (!matched) continue;
            if ((p.flags & SPICE_OSDI_PARA_TY_MASK) != SPICE_OSDI_PARA_TY_REAL) {
                return Diagnostic::error(
                    StatusCode::Unsupported, "only real64 OSDI parameters currently supported", name);
            }
            const std::uint32_t flags =
                SPICE_OSDI_ACCESS_FLAG_SET | (is_instance ? SPICE_OSDI_ACCESS_FLAG_INSTANCE : 0u);
            void* ptr = desc->access(
                is_instance ? instance_data.data() : nullptr, model_data.data(), id, flags);
            if (ptr == nullptr) {
                return Diagnostic::error(
                    StatusCode::InvalidArgument, "OSDI access returned null for parameter", name);
            }
            *static_cast<double*>(ptr) = val;
            return Diagnostic::success();
        }
        return Diagnostic::error(
            StatusCode::InvalidArgument,
            "unknown OSDI parameter rejected (fail-closed): " + name,
            config.instance_id);
    };

    for (const auto& [pname, pval] : config.model_params) {
        if (auto d = apply_param(pname, pval, false); !d.ok()) return d;
    }
    for (const auto& [pname, pval] : config.instance_params) {
        if (auto d = apply_param(pname, pval, true); !d.ok()) return d;
    }

    SpiceOsdiSimParas sim_paras{};
    SpiceOsdiInitInfo init_info{};
    desc->setup_model(nullptr, model_data.data(), &sim_paras, &init_info);
    if ((init_info.flags & SPICE_OSDI_EVAL_RET_FLAG_FATAL) != 0u || init_info.num_errors > 0u) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "OSDI setup_model reported fatal error", config.model_name);
    }

    init_info = SpiceOsdiInitInfo{};
    desc->setup_instance(
        nullptr,
        instance_data.data(),
        model_data.data(),
        config.temperature_k,
        desc->num_terminals,
        &sim_paras,
        &init_info);
    if ((init_info.flags & SPICE_OSDI_EVAL_RET_FLAG_FATAL) != 0u || init_info.num_errors > 0u) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "OSDI setup_instance reported fatal error", config.instance_id);
    }

    std::vector<std::optional<UnknownId>> all_nodes = config.terminal_unknowns;
    for (UnknownId u : config.internal_unknowns) all_nodes.push_back(u);

    out_device = std::make_unique<OsdiDevice>(
        config.library, desc, std::move(all_nodes), std::move(model_data), std::move(instance_data));
    return Diagnostic::success();
}

}  // namespace spice::osdi
