#include "osdi/osdi_loader.hpp"
#include "osdi/osdi_abi.h"

#include <algorithm>
#include <array>
#include <cctype>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
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
using dae::NoiseSourceContribution;
using dae::StampTarget;

constexpr std::uint32_t kMaxDescriptors = 1024u;
constexpr std::uint32_t kMaxBufferBytes = 16u * 1024u * 1024u;
constexpr std::uint32_t kOsdi03DescriptorSize =
    static_cast<std::uint32_t>(offsetof(SpiceOsdiDescriptor, given_flag_model));
constexpr std::uint32_t kInvalidNode = std::numeric_limits<std::uint32_t>::max();

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

/* ==================== Canonical SPICE3f5 / OSDI Limiting Functions ==================== */

/*
 * Canonical SPICE3f5 DEVpnjlim for OSDI $limit(v, "pnjlim", vt, vcrit).
 * Exact C calling convention emitted by OpenVAF:
 *   double pnjlim(bool init, bool* check, double vnew, double vold, double vt, double vcrit)
 */
double osdi_pnjlim(bool init, bool* check, double vnew, double vold, double vt, double vcrit) noexcept {
    if (init) {
        if (check != nullptr) *check = false;
        return vnew;
    }
    const double vt_safe = std::max(vt, 1.0e-6);
    if ((vnew > vcrit) && (std::abs(vnew - vold) > (vt_safe + vt_safe))) {
        if (vold > 0.0) {
            const double arg = 1.0 + (vnew - vold) / vt_safe;
            if (arg > 0.0) {
                vnew = vold + vt_safe * std::log(arg);
            } else {
                vnew = vcrit;
            }
        } else {
            vnew = vt_safe * std::log(std::max(1.0e-30, vnew / vt_safe));
        }
        if (check != nullptr) *check = true;
    } else if (vnew < 0.0) {
        const double arg = (vold > 0.0) ? (-vold - 1.0) : (2.0 * vold - 1.0);
        if (vnew < arg) {
            vnew = arg;
            if (check != nullptr) *check = true;
        } else {
            if (check != nullptr) *check = false;
        }
    } else {
        if (check != nullptr) *check = false;
    }
    return vnew;
}

/* Canonical SPICE3f5 DEVfetlim for OSDI $limit(v, "fetlim", vto). */
double osdi_fetlim(bool init, bool* check, double vnew, double vold, double vto) noexcept {
    if (init) {
        if (check != nullptr) *check = false;
        return vnew;
    }
    const double vorig = vnew;
    const double tsthi = std::abs(2.0 * (vold - vto)) + 2.0;
    const double tstlo = 0.5 * tsthi;
    const double tox = vto + 3.5;
    const double delv = vnew - vold;

    if (vold >= vto) {
        if (vold >= tox) {
            if (delv <= 0.0) {
                if (vnew >= tox) {
                    if (-delv > tstlo) vnew = vold - tstlo;
                } else {
                    vnew = std::max(vnew, vto + 2.0);
                }
            } else if (delv >= tsthi) {
                vnew = vold + tsthi;
            }
        } else {
            if (delv <= 0.0) {
                vnew = std::max(vnew, vto - 0.5);
            } else {
                vnew = std::min(vnew, vto + 4.0);
            }
        }
    } else {
        if (delv <= 0.0) {
            if (-delv > tsthi) vnew = vold - tsthi;
        } else {
            const double vtemp = vto + 0.5;
            if (vnew > vtemp) vnew = vtemp;
        }
    }
    if (check != nullptr) *check = (vnew != vorig);
    return vnew;
}

/* Canonical SPICE3f5 DEVlimvds for OSDI $limit(v, "limvds"). */
double osdi_limvds(bool init, bool* check, double vnew, double vold) noexcept {
    if (init) {
        if (check != nullptr) *check = false;
        return vnew;
    }
    const double vorig = vnew;
    if (vold >= 3.5) {
        if (vnew > vold) {
            vnew = std::min(vnew, 3.0 * vold + 2.0);
        } else if (vnew < 3.5) {
            vnew = std::max(vnew, 2.0);
        }
    } else {
        if (vnew > vold) {
            vnew = std::min(vnew, 4.0);
        } else {
            vnew = std::max(vnew, -0.5);
        }
    }
    if (check != nullptr) *check = (vnew != vorig);
    return vnew;
}

void osdi_log_noop(void* /*handle*/, const char* /*msg*/, std::uint32_t /*lvl*/) noexcept {}

/* Null-terminated $simparam table for setup_model / setup_instance / eval. */
struct SimParamBlock {
    static constexpr std::size_t kNumReal = 14u;
    std::array<const char*, kNumReal + 1u> names{
        "gmin", "abstol", "iabstol", "vntol", "vabstol", "reltol",
        "temp", "tnom", "scale", "shrink", "imax", "sourceScaleFactor",
        "iteration", "analysis_type", nullptr};
    std::array<double, kNumReal> vals{};
    std::array<const char*, 2u> names_str{"simulator", nullptr};
    std::array<const char*, 2u> vals_str{"spice_v2", nullptr};

    SpiceOsdiSimParas build(
        double temperature_k,
        double iteration,
        double analysis_type) noexcept {
        vals[0] = 1.0e-12;         /* gmin */
        vals[1] = 1.0e-12;         /* abstol */
        vals[2] = 1.0e-12;         /* iabstol */
        vals[3] = 1.0e-6;          /* vntol */
        vals[4] = 1.0e-6;          /* vabstol */
        vals[5] = 1.0e-3;          /* reltol */
        vals[6] = temperature_k - 273.15; /* temp (C) */
        vals[7] = 27.0;            /* tnom (C) */
        vals[8] = 1.0;             /* scale */
        vals[9] = 0.0;             /* shrink */
        vals[10] = 1.0;            /* imax */
        vals[11] = 1.0;            /* sourceScaleFactor */
        vals[12] = iteration;      /* iteration */
        vals[13] = analysis_type;  /* analysis_type */
        SpiceOsdiSimParas p{};
        p.names = names.data();
        p.vals = vals.data();
        p.names_str = names_str.data();
        p.vals_str = vals_str.data();
        return p;
    }
};

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
    /* Ensure libm math symbols (log, exp, pow, sqrt, tanh) are globally visible
     * for OpenVAF-compiled .osdi libraries that omit DT_NEEDED libm.so.6. */
    static std::once_flag libm_once;
    std::call_once(libm_once, []() {
        (void)dlopen("libm.so.6", RTLD_LAZY | RTLD_GLOBAL);
    });
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

/* Write a numeric parameter into model/instance data via desc->access(), handling REAL & INT. */
Diagnostic apply_osdi_params(
    const SpiceOsdiDescriptor* desc,
    void* inst_ptr,
    void* model_ptr,
    const std::vector<std::pair<std::string, double>>& kv_list,
    bool is_instance_list,
    const std::string& instance_id) {
    for (const auto& [pname, pval] : kv_list) {
        if (!std::isfinite(pval)) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "nonfinite OSDI parameter '" + pname + "'", instance_id);
        }
        bool matched = false;
        for (std::uint32_t id = 0; id < desc->num_params; ++id) {
            const auto& p = desc->param_opvar[id];
            const std::uint32_t kind = p.flags & SPICE_OSDI_PARA_KIND_MASK;
            if (kind == SPICE_OSDI_PARA_KIND_OPVAR) continue;
            if (is_instance_list && kind != SPICE_OSDI_PARA_KIND_INST) continue;
            if (!is_instance_list && kind != SPICE_OSDI_PARA_KIND_MODEL && kind != SPICE_OSDI_PARA_KIND_INST) {
                continue;
            }
            const std::uint32_t n_names = p.num_alias + 1u;
            bool name_hit = false;
            for (std::uint32_t a = 0; a < n_names; ++a) {
                if (p.name != nullptr && iequals(p.name[a], pname)) {
                    name_hit = true;
                    break;
                }
            }
            if (!name_hit) continue;

            const std::uint32_t ty = p.flags & SPICE_OSDI_PARA_TY_MASK;
            std::uint32_t access_flags = SPICE_OSDI_ACCESS_FLAG_SET;
            if (kind == SPICE_OSDI_PARA_KIND_INST) access_flags |= SPICE_OSDI_ACCESS_FLAG_INSTANCE;
            void* slot = desc->access(inst_ptr, model_ptr, id, access_flags);
            if (slot == nullptr) {
                return Diagnostic::error(
                    StatusCode::InvalidArgument,
                    "OSDI access() returned null for parameter '" + pname + "'",
                    instance_id);
            }
            if (ty == SPICE_OSDI_PARA_TY_REAL) {
                std::memcpy(slot, &pval, sizeof(double));
            } else if (ty == SPICE_OSDI_PARA_TY_INT) {
                const std::int32_t ival = static_cast<std::int32_t>(std::llround(pval));
                std::memcpy(slot, &ival, sizeof(std::int32_t));
            } else {
                return Diagnostic::error(
                    StatusCode::Unsupported,
                    "string OSDI parameter '" + pname + "' cannot be assigned a numeric value",
                    instance_id);
            }
            matched = true;
            break;
        }
        if (!matched) {
            return Diagnostic::error(
                StatusCode::InvalidArgument,
                "unknown OSDI " + std::string(is_instance_list ? "instance" : "model") +
                    " parameter '" + pname + "'",
                instance_id);
        }
    }
    return Diagnostic::success();
}

/* Concrete spice::dae::Device wrapping one validated OSDI 0.3/0.4 model instance. */
class OsdiDevice final : public Device {
public:
    OsdiDevice(
        std::string instance_id,
        std::shared_ptr<OsdiLibrary> lib,
        const SpiceOsdiDescriptor* desc,
        std::vector<std::optional<UnknownId>> node_unknowns,
        std::vector<std::uint8_t> model_data,
        std::vector<std::uint8_t> instance_data,
        double setup_temp_k)
        : instance_id_(std::move(instance_id)),
          lib_(std::move(lib)),
          desc_(desc),
          node_unknowns_(std::move(node_unknowns)),
          model_data_(std::move(model_data)),
          instance_data_(std::move(instance_data)),
          accepted_instance_data_(instance_data_),
          prev_state_(desc->num_states, 0.0),
          next_state_(desc->num_states, 0.0),
          accepted_state_(desc->num_states, 0.0),
          setup_temp_k_(setup_temp_k) {
        device_desc_.kind_id = std::string("osdi.") + (desc->name != nullptr ? desc->name : "unknown");
        device_desc_.display_name = desc->name != nullptr ? desc->name : "OSDI Device";
        for (std::uint32_t i = 0; i < desc->num_terminals; ++i) {
            const char* nname = (desc->nodes != nullptr && desc->nodes[i].name != nullptr)
                                    ? desc->nodes[i].name
                                    : "term";
            device_desc_.pins.push_back({nname, "terminal"});
        }
        device_desc_.supports_ac = true;
        device_desc_.supports_noise = (desc->num_noise_src > 0u && desc->load_noise != nullptr);
        device_desc_.thread_safe_eval = false; /* Conservative per ARCH-MOD-004 */
    }

    const DeviceDescriptor& descriptor() const noexcept override { return device_desc_; }

    Diagnostic bind_slots(const CscPattern& pattern) override {
        /* Map each OSDI node -> compact dense local slot (0 = ground sink, 1..U = unique unknowns).
         * Collapsed nodes sharing an UnknownId map to the SAME local slot so residuals accumulate
         * without double-counting and prev_solve always presents identical potentials. */
        unique_unknowns_.clear();
        local_node_slot_.assign(desc_->num_nodes, 0u);
        for (std::uint32_t i = 0; i < desc_->num_nodes; ++i) {
            if (!node_unknowns_[i].has_value()) {
                local_node_slot_[i] = 0u; /* ground sink */
                continue;
            }
            const UnknownId target_u = *node_unknowns_[i];
            std::uint32_t found_slot = 0u;
            for (std::size_t k = 0; k < unique_unknowns_.size(); ++k) {
                if (unique_unknowns_[k] == target_u) {
                    found_slot = static_cast<std::uint32_t>(k + 1u);
                    break;
                }
            }
            if (found_slot == 0u) {
                unique_unknowns_.push_back(target_u);
                found_slot = static_cast<std::uint32_t>(unique_unknowns_.size());
            }
            local_node_slot_[i] = found_slot;
        }

        if (desc_->node_mapping_offset + desc_->num_nodes * sizeof(std::uint32_t) > instance_data_.size()) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "OSDI node_mapping_offset exceeds instance_size", instance_id_);
        }
        if (desc_->num_states > 0u &&
            desc_->state_idx_off + desc_->num_states * sizeof(std::uint32_t) > instance_data_.size()) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "OSDI state_idx_off exceeds instance_size", instance_id_);
        }

        /* Pre-bind every OSDI Jacobian entry to its post-collapse CSC SlotId (or nullopt if grounded). */
        jac_resist_buf_.assign(desc_->num_jacobian_entries, 0.0);
        jac_react_buf_.assign(desc_->num_jacobian_entries, 0.0);
        jac_slots_.resize(desc_->num_jacobian_entries);

        for (std::uint32_t j = 0; j < desc_->num_jacobian_entries; ++j) {
            const auto& entry = desc_->jacobian_entries[j];
            if (entry.nodes.node_1 >= desc_->num_nodes || entry.nodes.node_2 >= desc_->num_nodes) {
                return Diagnostic::error(
                    StatusCode::InvalidArgument, "OSDI Jacobian node index out of range", instance_id_);
            }
            const auto r_unk = node_unknowns_[entry.nodes.node_1];
            const auto c_unk = node_unknowns_[entry.nodes.node_2];
            if (r_unk.has_value() && c_unk.has_value()) {
                SlotId slot{};
                if (!pattern.find_slot(r_unk->value, c_unk->value, slot)) {
                    return Diagnostic::error(
                        StatusCode::InvalidArgument, "OSDI Jacobian CSC slot missing from pattern", instance_id_);
                }
                jac_slots_[j] = slot;
            } else {
                jac_slots_[j] = std::nullopt;
            }
        }

        if (auto d = write_instance_bindings(instance_data_); !d.ok()) return d;
        accepted_instance_data_ = instance_data_;
        bound_ = true;
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& target) override {
        if (!bound_) {
            return Diagnostic::error(StatusCode::InvalidArgument, "OSDI device not bound", instance_id_);
        }

        /* If analysis temperature changed (e.g. .TEMP / .STEP TEMP), re-run setup_instance seamlessly. */
        if (std::abs(ctx.temperature_k - setup_temp_k_) > 1.0e-6) {
            if (auto d = re_setup_temperature(ctx.temperature_k); !d.ok()) return d;
        }

        const std::size_t local_dim = unique_unknowns_.size() + 1u;
        local_x_.assign(local_dim, 0.0);
        for (std::size_t k = 0; k < unique_unknowns_.size(); ++k) {
            const std::uint32_t u = unique_unknowns_[k].value;
            if (u >= ctx.x.size() || !std::isfinite(ctx.x[u])) {
                return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite input to OSDI eval", instance_id_);
            }
            local_x_[k + 1u] = ctx.x[u];
        }

        const double analysis_code = (ctx.mode == AnalysisMode::Transient) ? 2.0
                                     : (ctx.mode == AnalysisMode::AcLinearize) ? 1.0
                                                                               : 0.0;
        SimParamBlock sp_block{};
        SpiceOsdiSimInfo sim_info{};
        sim_info.paras = sp_block.build(ctx.temperature_k, static_cast<double>(ctx.newton_iter), analysis_code);
        sim_info.abstime = ctx.time_s;
        sim_info.prev_solve = local_x_.data();
        sim_info.prev_state = prev_state_.empty() ? nullptr : prev_state_.data();
        sim_info.next_state = next_state_.empty() ? nullptr : next_state_.data();

        std::uint32_t eval_flags = SPICE_OSDI_CALC_RESIST_RESIDUAL | SPICE_OSDI_CALC_REACT_RESIDUAL |
                                   SPICE_OSDI_CALC_RESIST_JACOBIAN | SPICE_OSDI_CALC_REACT_JACOBIAN;
        const bool use_limiting = ctx.enable_limiting && desc_->num_states > 0u;
        if (use_limiting) {
            eval_flags |= SPICE_OSDI_ENABLE_LIM | SPICE_OSDI_CALC_RESIST_LIM_RHS | SPICE_OSDI_CALC_REACT_LIM_RHS;
            if (ctx.newton_iter == 0u) {
                eval_flags |= SPICE_OSDI_INIT_LIM;
            }
        }
        switch (ctx.mode) {
            case AnalysisMode::OperatingPoint:
            case AnalysisMode::DcSweep:
                eval_flags |= SPICE_OSDI_ANALYSIS_DC | SPICE_OSDI_ANALYSIS_STATIC;
                break;
            case AnalysisMode::AcLinearize:
                eval_flags |= SPICE_OSDI_ANALYSIS_AC;
                if (desc_->num_noise_src > 0u) eval_flags |= SPICE_OSDI_CALC_NOISE;
                break;
            case AnalysisMode::Transient:
            case AnalysisMode::HarmonicBalance:
                eval_flags |= SPICE_OSDI_ANALYSIS_TRAN;
                break;
        }
        sim_info.flags = eval_flags;

        const std::uint32_t ret_flags =
            desc_->eval(nullptr, instance_data_.data(), model_data_.data(), &sim_info);
        if ((ret_flags & SPICE_OSDI_EVAL_RET_FLAG_FATAL) != 0u) {
            return Diagnostic::error(
                StatusCode::NumericalFailure, "OSDI model reported fatal evaluation error", instance_id_);
        }

        const bool was_limited = ((ret_flags & SPICE_OSDI_EVAL_RET_FLAG_LIM) != 0u);
        if (was_limited) {
            target.mark_limited();
        }
        /* Advance Newton limiting state on primary Newton iterations (not line-search probes). */
        if (use_limiting && !ctx.is_line_search_probe && !next_state_.empty()) {
            prev_state_ = next_state_;
        }

        /* Load resist residual -> target.f, react residual -> target.q, and limiting RHS when limited. */
        f_local_.assign(local_dim, 0.0);
        q_local_.assign(local_dim, 0.0);
        if (desc_->load_residual_resist != nullptr) {
            desc_->load_residual_resist(instance_data_.data(), model_data_.data(), f_local_.data());
        }
        if (desc_->load_residual_react != nullptr) {
            desc_->load_residual_react(instance_data_.data(), model_data_.data(), q_local_.data());
        }

        const bool load_lim = use_limiting && was_limited;
        if (load_lim) {
            flim_local_.assign(local_dim, 0.0);
            qlim_local_.assign(local_dim, 0.0);
            if (desc_->load_limit_rhs_resist != nullptr) {
                desc_->load_limit_rhs_resist(instance_data_.data(), model_data_.data(), flim_local_.data());
            }
            if (desc_->load_limit_rhs_react != nullptr) {
                desc_->load_limit_rhs_react(instance_data_.data(), model_data_.data(), qlim_local_.data());
            }
        }

        for (std::size_t k = 0; k < unique_unknowns_.size(); ++k) {
            const UnknownId unk = unique_unknowns_[k];
            const std::size_t loc = k + 1u;
            if (!target.add_f(unk, f_local_[loc]) || !target.add_q(unk, q_local_[loc])) {
                return Diagnostic::error(
                    StatusCode::NumericalFailure, "nonfinite OSDI residual contribution", instance_id_);
            }
            if (load_lim) {
                if (!target.add_f_lim(unk, flim_local_[loc]) || !target.add_q_lim(unk, qlim_local_[loc])) {
                    return Diagnostic::error(
                        StatusCode::NumericalFailure, "nonfinite OSDI limiting RHS contribution", instance_id_);
                }
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
                        StatusCode::NumericalFailure, "nonfinite OSDI resist Jacobian", instance_id_);
                }
            }
            if ((flags & SPICE_OSDI_JACOBIAN_ENTRY_REACT) != 0u) {
                if (!target.add_dq(*jac_slots_[j], jac_react_buf_[j])) {
                    return Diagnostic::error(
                        StatusCode::NumericalFailure, "nonfinite OSDI react Jacobian", instance_id_);
                }
            }
        }

        return Diagnostic::success();
    }

    void evaluate_noise(
        double freq_hz,
        double temperature_k,
        std::span<const double> x_op,
        std::vector<NoiseSourceContribution>& out_sources) const override {
        if (!bound_ || desc_->num_noise_src == 0u || desc_->load_noise == nullptr ||
            desc_->noise_sources == nullptr) {
            return;
        }

        /* Side-effect-free evaluation on a local scratch copy of instance_data_. */
        std::vector<std::uint8_t> tmp_inst = instance_data_;
        std::vector<double> tmp_prev_state = prev_state_;
        std::vector<double> tmp_next_state = next_state_;

        const std::size_t local_dim = unique_unknowns_.size() + 1u;
        std::vector<double> tmp_x(local_dim, 0.0);
        for (std::size_t k = 0; k < unique_unknowns_.size(); ++k) {
            const std::uint32_t u = unique_unknowns_[k].value;
            if (u < x_op.size() && std::isfinite(x_op[u])) {
                tmp_x[k + 1u] = x_op[u];
            }
        }

        SimParamBlock sp_block{};
        SpiceOsdiSimInfo sim_info{};
        sim_info.paras = sp_block.build(temperature_k, 0.0, 1.0);
        sim_info.abstime = 0.0;
        sim_info.prev_solve = tmp_x.data();
        sim_info.prev_state = tmp_prev_state.empty() ? nullptr : tmp_prev_state.data();
        sim_info.next_state = tmp_next_state.empty() ? nullptr : tmp_next_state.data();
        sim_info.flags = SPICE_OSDI_CALC_NOISE | SPICE_OSDI_ANALYSIS_NOISE | SPICE_OSDI_ANALYSIS_AC;

        const std::uint32_t ret =
            desc_->eval(nullptr, tmp_inst.data(), const_cast<std::uint8_t*>(model_data_.data()), &sim_info);
        if ((ret & SPICE_OSDI_EVAL_RET_FLAG_FATAL) != 0u) return;

        std::vector<double> noise_dens(desc_->num_noise_src, 0.0);
        desc_->load_noise(
            tmp_inst.data(),
            const_cast<std::uint8_t*>(model_data_.data()),
            freq_hz,
            noise_dens.data());

        for (std::uint32_t k = 0; k < desc_->num_noise_src; ++k) {
            const double psd = noise_dens[k];
            if (!std::isfinite(psd) || psd <= 0.0) continue;
            const auto& ns = desc_->noise_sources[k];
            const std::optional<UnknownId> n1 =
                (ns.nodes.node_1 < desc_->num_nodes) ? node_unknowns_[ns.nodes.node_1] : std::nullopt;
            const std::optional<UnknownId> n2 =
                (ns.nodes.node_2 < desc_->num_nodes) ? node_unknowns_[ns.nodes.node_2] : std::nullopt;
            if (n1 == n2) continue; /* Collapsed branch contributes zero differential noise */
            const std::string src_name =
                (ns.name != nullptr && ns.name[0] != '\0') ? std::string(ns.name) : ("noise_" + std::to_string(k));
            out_sources.push_back(NoiseSourceContribution{instance_id_, src_name, n1, n2, psd});
        }
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

    void reset_history() noexcept override {
        std::fill(prev_state_.begin(), prev_state_.end(), 0.0);
        std::fill(next_state_.begin(), next_state_.end(), 0.0);
        std::fill(accepted_state_.begin(), accepted_state_.end(), 0.0);
    }

private:
    Diagnostic write_instance_bindings(std::vector<std::uint8_t>& inst_buf) {
        std::memcpy(
            inst_buf.data() + desc_->node_mapping_offset,
            local_node_slot_.data(),
            desc_->num_nodes * sizeof(std::uint32_t));

        if (desc_->num_states > 0u) {
            std::vector<std::uint32_t> state_idx(desc_->num_states);
            for (std::uint32_t k = 0; k < desc_->num_states; ++k) state_idx[k] = k;
            std::memcpy(
                inst_buf.data() + desc_->state_idx_off,
                state_idx.data(),
                desc_->num_states * sizeof(std::uint32_t));
        }

        for (std::uint32_t j = 0; j < desc_->num_jacobian_entries; ++j) {
            const auto& entry = desc_->jacobian_entries[j];
            if ((entry.flags & SPICE_OSDI_JACOBIAN_ENTRY_RESIST) != 0u) {
                const std::size_t off =
                    desc_->jacobian_ptr_resist_offset + static_cast<std::size_t>(j) * sizeof(double*);
                if (off + sizeof(double*) > inst_buf.size()) {
                    return Diagnostic::error(
                        StatusCode::InvalidArgument, "OSDI resist Jacobian ptr offset out of range", instance_id_);
                }
                double* ptr = &jac_resist_buf_[j];
                std::memcpy(inst_buf.data() + off, &ptr, sizeof(double*));
            }
            if ((entry.flags & SPICE_OSDI_JACOBIAN_ENTRY_REACT) != 0u) {
                const std::size_t off = entry.react_ptr_off;
                if (off + sizeof(double*) > inst_buf.size()) {
                    return Diagnostic::error(
                        StatusCode::InvalidArgument, "OSDI react Jacobian ptr offset out of range", instance_id_);
                }
                double* ptr = &jac_react_buf_[j];
                std::memcpy(inst_buf.data() + off, &ptr, sizeof(double*));
            }
        }
        return Diagnostic::success();
    }

    Diagnostic re_setup_temperature(double new_temp_k) {
        if (desc_->setup_instance == nullptr) {
            setup_temp_k_ = new_temp_k;
            return Diagnostic::success();
        }
        SimParamBlock sp_block{};
        SpiceOsdiSimParas sp = sp_block.build(new_temp_k, 0.0, 0.0);
        SpiceOsdiInitInfo ii{};
        desc_->setup_instance(
            nullptr,
            instance_data_.data(),
            model_data_.data(),
            new_temp_k,
            desc_->num_terminals,
            &sp,
            &ii);
        if (ii.num_errors != 0u || (ii.flags & SPICE_OSDI_EVAL_RET_FLAG_FATAL) != 0u) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "OSDI setup_instance failed on temperature update", instance_id_);
        }
        if (auto d = write_instance_bindings(instance_data_); !d.ok()) return d;
        accepted_instance_data_ = instance_data_;
        setup_temp_k_ = new_temp_k;
        return Diagnostic::success();
    }

    std::string instance_id_;
    std::shared_ptr<OsdiLibrary> lib_; /* Retains dlopen/LoadLibrary lifetime (ARCH-MOD-002). */
    const SpiceOsdiDescriptor* desc_;
    DeviceDescriptor device_desc_;
    std::vector<std::optional<UnknownId>> node_unknowns_;
    std::vector<UnknownId> unique_unknowns_;
    std::vector<std::uint32_t> local_node_slot_;
    std::vector<std::uint8_t> model_data_;
    std::vector<std::uint8_t> instance_data_;
    std::vector<std::uint8_t> accepted_instance_data_;
    std::vector<double> prev_state_;
    std::vector<double> next_state_;
    std::vector<double> accepted_state_;
    std::vector<double> local_x_;
    std::vector<double> f_local_;
    std::vector<double> q_local_;
    std::vector<double> flim_local_;
    std::vector<double> qlim_local_;
    std::vector<double> jac_resist_buf_;
    std::vector<double> jac_react_buf_;
    std::vector<std::optional<SlotId>> jac_slots_;
    double setup_temp_k_{300.15};
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

    /* Stride-safe descriptor size (OSDI_DESCRIPTOR_SIZE is exported by OpenVAF 0.3/0.4+). */
    const auto* dsz = static_cast<const std::uint32_t*>(lookup_symbol(handle, "OSDI_DESCRIPTOR_SIZE"));
    const std::uint32_t stride = (dsz != nullptr) ? *dsz : kOsdi03DescriptorSize;
    if (stride < kOsdi03DescriptorSize || stride > 65536u) {
        return Diagnostic::error(
            StatusCode::Unsupported,
            "invalid OSDI_DESCRIPTOR_SIZE=" + std::to_string(stride) +
                " (minimum " + std::to_string(kOsdi03DescriptorSize) + ")",
            path);
    }

    /* Wire optional OSDI_LOG callback so Verilog-A $display/$strobe/$warning never crashes. */
    if (void* log_sym = lookup_symbol(handle, "OSDI_LOG"); log_sym != nullptr) {
        SpiceOsdiLogFn fn = &osdi_log_noop;
        std::memcpy(log_sym, &fn, sizeof(SpiceOsdiLogFn));
    }

    /* Wire optional OSDI_LIM_TABLE entries (pnjlim, fetlim, limvds). */
    const auto* lim_len = static_cast<const std::uint32_t*>(lookup_symbol(handle, "OSDI_LIM_TABLE_LEN"));
    auto* lim_table = static_cast<SpiceOsdiLimFunction*>(lookup_symbol(handle, "OSDI_LIM_TABLE"));
    if (lim_len != nullptr && lim_table != nullptr && *lim_len > 0u) {
        for (std::uint32_t i = 0; i < *lim_len; ++i) {
            if (lim_table[i].name == nullptr) continue;
            if (iequals(lim_table[i].name, "pnjlim")) {
                lim_table[i].func_ptr = reinterpret_cast<void*>(&osdi_pnjlim);
            } else if (iequals(lim_table[i].name, "fetlim")) {
                lim_table[i].func_ptr = reinterpret_cast<void*>(&osdi_fetlim);
            } else if (iequals(lim_table[i].name, "limvds")) {
                lim_table[i].func_ptr = reinterpret_cast<void*>(&osdi_limvds);
            } else {
                return Diagnostic::error(
                    StatusCode::Unsupported,
                    "unsupported OSDI $limit function '" + std::string(lim_table[i].name) + "'",
                    path);
            }
        }
    }

    lib->major_ = *maj;
    lib->minor_ = *min;
    lib->num_descriptors_ = *num;
    lib->descriptor_size_ = stride;
    lib->descriptors_ = descs;
    out_lib = std::move(lib);
    return Diagnostic::success();
}

std::vector<std::string> OsdiLibrary::model_names() const {
    std::vector<std::string> names;
    if (descriptors_ == nullptr || descriptor_size_ < kOsdi03DescriptorSize) return names;
    const auto* base = static_cast<const std::uint8_t*>(descriptors_);
    names.reserve(num_descriptors_);
    for (std::uint32_t i = 0; i < num_descriptors_; ++i) {
        const auto* d = reinterpret_cast<const SpiceOsdiDescriptor*>(
            base + static_cast<std::size_t>(i) * descriptor_size_);
        if (d->name != nullptr) names.emplace_back(d->name);
    }
    return names;
}

const void* OsdiLibrary::find_descriptor(const std::string& model_name) const noexcept {
    if (descriptors_ == nullptr || descriptor_size_ < kOsdi03DescriptorSize) return nullptr;
    const auto* base = static_cast<const std::uint8_t*>(descriptors_);
    for (std::uint32_t i = 0; i < num_descriptors_; ++i) {
        const auto* d = reinterpret_cast<const SpiceOsdiDescriptor*>(
            base + static_cast<std::size_t>(i) * descriptor_size_);
        if (iequals(d->name, model_name)) return d;
    }
    return nullptr;
}

Diagnostic prepare_osdi_instance(
    const std::string& instance_id,
    std::shared_ptr<OsdiLibrary> library,
    const std::string& model_name,
    std::uint32_t num_terminals,
    const std::vector<std::pair<std::string, double>>& model_params,
    const std::vector<std::pair<std::string, double>>& instance_params,
    double temperature_k,
    PreparedOsdiInstance& out_prepared) {
    out_prepared = PreparedOsdiInstance{};
    if (!library) {
        return Diagnostic::error(StatusCode::InvalidArgument, "null OSDI library", instance_id);
    }
    const auto* desc = static_cast<const SpiceOsdiDescriptor*>(library->find_descriptor(model_name));
    if (desc == nullptr) {
        return Diagnostic::error(StatusCode::InvalidArgument, "OSDI model not found: " + model_name, instance_id);
    }
    if (num_terminals != desc->num_terminals) {
        return Diagnostic::error(
            StatusCode::InvalidArgument,
            "OSDI terminal count mismatch for model '" + model_name + "' (expected " +
                std::to_string(desc->num_terminals) + ", got " + std::to_string(num_terminals) + ")",
            instance_id);
    }
    if (desc->num_nodes < desc->num_terminals || desc->eval == nullptr || desc->access == nullptr) {
        return Diagnostic::error(StatusCode::InvalidArgument, "malformed OSDI descriptor", instance_id);
    }
    if (desc->instance_size == 0u || desc->instance_size > kMaxBufferBytes ||
        desc->model_size == 0u || desc->model_size > kMaxBufferBytes) {
        return Diagnostic::error(StatusCode::ResourceExhausted, "OSDI model/instance buffer size out of bounds", instance_id);
    }

    std::vector<std::uint8_t> model_data(desc->model_size, 0u);
    std::vector<std::uint8_t> instance_data(desc->instance_size, 0u);

    if (auto d = apply_osdi_params(desc, instance_data.data(), model_data.data(), model_params, false, instance_id);
        !d.ok()) {
        return d;
    }
    if (auto d = apply_osdi_params(desc, instance_data.data(), model_data.data(), instance_params, true, instance_id);
        !d.ok()) {
        return d;
    }

    SimParamBlock sp_block{};
    SpiceOsdiSimParas sim_paras = sp_block.build(temperature_k, 0.0, 0.0);

    SpiceOsdiInitInfo model_init{};
    if (desc->setup_model != nullptr) {
        desc->setup_model(nullptr, model_data.data(), &sim_paras, &model_init);
        if (model_init.num_errors != 0u || (model_init.flags & SPICE_OSDI_EVAL_RET_FLAG_FATAL) != 0u) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "OSDI setup_model reported initialization error", instance_id);
        }
    }

    SpiceOsdiInitInfo inst_init{};
    if (desc->setup_instance != nullptr) {
        desc->setup_instance(
            nullptr,
            instance_data.data(),
            model_data.data(),
            temperature_k,
            desc->num_terminals,
            &sim_paras,
            &inst_init);
        if (inst_init.num_errors != 0u || (inst_init.flags & SPICE_OSDI_EVAL_RET_FLAG_FATAL) != 0u) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "OSDI setup_instance reported initialization error", instance_id);
        }
    }

    /* Union-Find node collapsing over desc->collapsible[0..num_collapsible-1].
     * Always prefer ground (kInvalidNode) or lowest terminal index as canonical representative. */
    std::vector<std::uint32_t> parent(desc->num_nodes);
    for (std::uint32_t i = 0; i < desc->num_nodes; ++i) parent[i] = i;

    auto find_root = [&](std::uint32_t x) -> std::uint32_t {
        if (x == kInvalidNode) return kInvalidNode;
        while (parent[x] != x && parent[x] != kInvalidNode) {
            if (parent[parent[x]] != kInvalidNode) parent[x] = parent[parent[x]];
            x = parent[x];
        }
        return parent[x];
    };

    if (desc->num_collapsible > 0u && desc->collapsible != nullptr) {
        if (desc->collapsed_offset + desc->num_collapsible * sizeof(bool) > instance_data.size()) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "OSDI collapsed_offset exceeds instance_size", instance_id);
        }
        const auto* collapsed = reinterpret_cast<const bool*>(instance_data.data() + desc->collapsed_offset);
        for (std::uint32_t c = 0; c < desc->num_collapsible; ++c) {
            if (!collapsed[c]) continue;
            const std::uint32_t n1 = desc->collapsible[c].node_1;
            const std::uint32_t n2 = desc->collapsible[c].node_2;
            if (n1 >= desc->num_nodes) continue;
            const std::uint32_t r1 = find_root(n1);
            const std::uint32_t r2 = (n2 == kInvalidNode || n2 >= desc->num_nodes) ? kInvalidNode : find_root(n2);
            if (r1 == r2) continue;
            if (r2 == kInvalidNode) {
                if (r1 != kInvalidNode) parent[r1] = kInvalidNode;
            } else if (r1 == kInvalidNode) {
                parent[r2] = kInvalidNode;
            } else if (r1 < r2) {
                parent[r2] = r1;
            } else {
                parent[r1] = r2;
            }
        }
    }

    out_prepared.instance_id = instance_id;
    out_prepared.library = std::move(library);
    out_prepared.desc = desc;
    out_prepared.num_terminals = desc->num_terminals;
    out_prepared.num_nodes = desc->num_nodes;
    out_prepared.setup_temperature_k = temperature_k;
    out_prepared.model_data = std::move(model_data);
    out_prepared.instance_data = std::move(instance_data);
    out_prepared.canonical_node.resize(desc->num_nodes);

    for (std::uint32_t i = 0; i < desc->num_nodes; ++i) {
        out_prepared.canonical_node[i] = find_root(i);
    }
    for (std::uint32_t i = desc->num_terminals; i < desc->num_nodes; ++i) {
        if (out_prepared.canonical_node[i] == i) {
            out_prepared.uncollapsed_internal_nodes.push_back(i);
            const std::string nname = (desc->nodes != nullptr && desc->nodes[i].name != nullptr &&
                                       desc->nodes[i].name[0] != '\0')
                                          ? std::string(desc->nodes[i].name)
                                          : ("int" + std::to_string(i - desc->num_terminals));
            out_prepared.uncollapsed_internal_names.push_back(nname);
        }
    }
    return Diagnostic::success();
}

Diagnostic finalize_osdi_instance(
    PreparedOsdiInstance prepared,
    const std::vector<std::optional<UnknownId>>& terminal_unknowns,
    const std::vector<UnknownId>& uncollapsed_internal_unknowns,
    std::vector<std::pair<std::uint32_t, std::uint32_t>>& out_coords,
    std::unique_ptr<Device>& out_device) {
    out_device.reset();
    const auto* desc = static_cast<const SpiceOsdiDescriptor*>(prepared.desc);
    if (desc == nullptr) {
        return Diagnostic::error(StatusCode::InvalidArgument, "unprepared OSDI instance");
    }
    if (terminal_unknowns.size() != prepared.num_terminals ||
        uncollapsed_internal_unknowns.size() != prepared.uncollapsed_internal_nodes.size()) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "OSDI finalization unknown count mismatch", prepared.instance_id);
    }

    /* Resolve every OSDI node 0..num_nodes-1 through its collapsed canonical root. */
    std::vector<std::optional<UnknownId>> root_to_unk(prepared.num_nodes, std::nullopt);
    for (std::uint32_t i = 0; i < prepared.num_terminals; ++i) {
        root_to_unk[i] = terminal_unknowns[i];
    }
    for (std::size_t k = 0; k < prepared.uncollapsed_internal_nodes.size(); ++k) {
        root_to_unk[prepared.uncollapsed_internal_nodes[k]] = uncollapsed_internal_unknowns[k];
    }

    std::vector<std::optional<UnknownId>> final_node_unknowns(prepared.num_nodes, std::nullopt);
    for (std::uint32_t i = 0; i < prepared.num_nodes; ++i) {
        const std::uint32_t r = prepared.canonical_node[i];
        if (r == kInvalidNode || r >= prepared.num_nodes) {
            final_node_unknowns[i] = std::nullopt;
        } else {
            final_node_unknowns[i] = root_to_unk[r];
        }
    }

    /* Collect post-collapse CSC stencil coordinates. */
    for (std::uint32_t j = 0; j < desc->num_jacobian_entries; ++j) {
        const auto& e = desc->jacobian_entries[j];
        if (e.nodes.node_1 >= desc->num_nodes || e.nodes.node_2 >= desc->num_nodes) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "OSDI Jacobian node out of range", prepared.instance_id);
        }
        const auto r = final_node_unknowns[e.nodes.node_1];
        const auto c = final_node_unknowns[e.nodes.node_2];
        if (r.has_value() && c.has_value()) {
            out_coords.emplace_back(r->value, c->value);
        }
    }

    out_device = std::make_unique<OsdiDevice>(
        std::move(prepared.instance_id),
        std::move(prepared.library),
        desc,
        std::move(final_node_unknowns),
        std::move(prepared.model_data),
        std::move(prepared.instance_data),
        prepared.setup_temperature_k);
    return Diagnostic::success();
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
    PreparedOsdiInstance prep{};
    if (auto d = prepare_osdi_instance(
            config.instance_id,
            config.library,
            config.model_name,
            static_cast<std::uint32_t>(config.terminal_unknowns.size()),
            config.model_params,
            config.instance_params,
            config.temperature_k,
            prep);
        !d.ok()) {
        return d;
    }
    std::vector<UnknownId> internal_subset;
    internal_subset.reserve(prep.uncollapsed_internal_nodes.size());
    for (std::uint32_t idx : prep.uncollapsed_internal_nodes) {
        const std::size_t rel = idx - prep.num_terminals;
        if (rel >= config.internal_unknowns.size()) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "OSDI internal unknown count mismatch", config.instance_id);
        }
        internal_subset.push_back(config.internal_unknowns[rel]);
    }
    std::vector<std::pair<std::uint32_t, std::uint32_t>> dummy_coords;
    return finalize_osdi_instance(
        std::move(prep), config.terminal_unknowns, internal_subset, dummy_coords, out_device);
}

}  // namespace spice::osdi
