#include "spice/capi.h"

#include "analysis/analyses.hpp"
#include "analysis/m2c_analyses.hpp"
#include "analysis/rf_steady_state.hpp"
#include "core/circuit.hpp"
#include "devices/linear_devices.hpp"
#include "linalg/solver.hpp"
#include "parser/spice_parser.hpp"

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <cstring>
#include <exception>
#include <memory>
#include <optional>
#include <string>
#include <vector>

namespace {

constexpr std::uint64_t kEngineMagic = 0x5350494345563201ULL;
constexpr std::uint64_t kCircuitMagic = 0x5350494345563202ULL;
constexpr std::uint64_t kResultMagic = 0x5350494345563203ULL;
constexpr std::uint64_t kDefaultMaxUnknowns = 1'000'000ULL;

spice_status_t map_status(spice::core::StatusCode code) noexcept {
    switch (code) {
        case spice::core::StatusCode::Ok: return SPICE_STATUS_OK;
        case spice::core::StatusCode::InvalidArgument: return SPICE_STATUS_INVALID_ARGUMENT;
        case spice::core::StatusCode::Unsupported: return SPICE_STATUS_UNSUPPORTED;
        case spice::core::StatusCode::ResourceExhausted: return SPICE_STATUS_RESOURCE_EXHAUSTED;
        case spice::core::StatusCode::NumericalFailure: return SPICE_STATUS_NUMERICAL_FAILURE;
    }
    return SPICE_STATUS_INTERNAL_ERROR;
}

bool map_backend(spice_backend_t b, spice::linalg::BackendPreference& out) noexcept {
    switch (b) {
        case SPICE_BACKEND_AUTO: out = spice::linalg::BackendPreference::Auto; return true;
        case SPICE_BACKEND_KLU: out = spice::linalg::BackendPreference::Klu; return true;
        case SPICE_BACKEND_SPARSE_LU: out = spice::linalg::BackendPreference::SparseLu; return true;
        case SPICE_BACKEND_DENSE_LU: out = spice::linalg::BackendPreference::DenseLu; return true;
    }
    return false;
}

spice::analysis::IntegrationMethod map_method(spice_integration_method_t m) noexcept {
    if (m == SPICE_METHOD_BACKWARD_EULER) return spice::analysis::IntegrationMethod::BackwardEuler;
    if (m == SPICE_METHOD_BDF2) return spice::analysis::IntegrationMethod::Bdf2;
    return spice::analysis::IntegrationMethod::Trapezoidal;
}

std::optional<spice::core::NodeId> find_node_icase(const spice::core::Circuit& ckt, const char* name) {
    if (name == nullptr) return std::nullopt;
    std::string want(name);
    for (char& c : want) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    if (want == "0" || want == "gnd") return spice::core::NodeId{0};
    for (std::uint32_t i = 0; i < ckt.nodes.size(); ++i) {
        std::string cur = ckt.nodes[i];
        for (char& c : cur) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
        if (cur == want) return spice::core::NodeId{i};
    }
    return std::nullopt;
}

}  // namespace

struct spice_engine {
    std::uint64_t magic{kEngineMagic};
    bool audits_enabled{false};
    std::uint64_t max_unknowns{kDefaultMaxUnknowns};
    mutable std::string last_error;

    [[nodiscard]] bool valid() const noexcept { return magic == kEngineMagic; }
};

struct spice_circuit {
    std::uint64_t magic{kCircuitMagic};
    spice::core::Circuit circuit;

    [[nodiscard]] bool valid() const noexcept { return magic == kCircuitMagic; }
};

struct spice_result {
    std::uint64_t magic{kResultMagic};
    bool is_complex{false};
    bool audit_passed{false};
    std::vector<std::string> signal_names;
    std::vector<double> axis;
    std::vector<double> real_values;
    std::vector<double> complex_interleaved;

    [[nodiscard]] bool valid() const noexcept { return magic == kResultMagic; }
};

extern "C" {

spice_status_t spice_engine_create(
    const spice_engine_options_t* options,
    spice_engine_t** out_engine) {
    try {
        if (options == nullptr || out_engine == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        *out_engine = nullptr;
        if (options->struct_size != sizeof(spice_engine_options_t) ||
            options->abi_major != SPICE_CAPI_ABI_MAJOR ||
            options->abi_minor > SPICE_CAPI_ABI_MINOR) {
            return SPICE_STATUS_ABI_MISMATCH;
        }
        auto engine = std::make_unique<spice_engine>();
        engine->audits_enabled = (options->enable_audits != 0u);
        engine->max_unknowns =
            (options->max_unknowns == 0u) ? kDefaultMaxUnknowns : options->max_unknowns;
        *out_engine = engine.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

void spice_engine_destroy(spice_engine_t* engine) {
    if (engine == nullptr || !engine->valid()) return;
    engine->magic = 0;
    delete engine;
}

spice_status_t spice_engine_capabilities(
    const spice_engine_t* engine,
    spice_capabilities_t* out_capabilities) {
    try {
        if (engine == nullptr || !engine->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (out_capabilities == nullptr) {
            engine->last_error = "out_capabilities must not be null";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }
        if (out_capabilities->struct_size != sizeof(spice_capabilities_t)) {
            engine->last_error = "spice_capabilities_t struct_size mismatch";
            return SPICE_STATUS_ABI_MISMATCH;
        }
        out_capabilities->abi_major = SPICE_CAPI_ABI_MAJOR;
        out_capabilities->abi_minor = SPICE_CAPI_ABI_MINOR;
        out_capabilities->klu_available = spice::linalg::klu_is_linked() ? 1u : 0u;
        out_capabilities->osdi_available = 1u;    /* Generic OSDI 0.3/0.4 loader active in M2b */
        out_capabilities->audits_available = 1u;  /* Active in M2a */
        out_capabilities->default_max_unknowns = engine->max_unknowns;
        engine->last_error.clear();
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_engine_last_error(
    const spice_engine_t* engine,
    char* buffer,
    size_t buffer_bytes,
    size_t* out_required_bytes) {
    try {
        if (engine == nullptr || !engine->valid()) return SPICE_STATUS_INVALID_HANDLE;
        const size_t required = engine->last_error.size() + 1u;
        if (out_required_bytes != nullptr) *out_required_bytes = required;
        if (buffer == nullptr || buffer_bytes == 0u) return SPICE_STATUS_OK;
        const size_t to_copy = std::min(engine->last_error.size(), buffer_bytes - 1u);
        if (to_copy > 0u) std::memcpy(buffer, engine->last_error.data(), to_copy);
        buffer[to_copy] = '\0';
        return (buffer_bytes >= required) ? SPICE_STATUS_OK : SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_create(const char* name, spice_circuit_t** out_circuit) {
    try {
        if (name == nullptr || out_circuit == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        *out_circuit = nullptr;
        auto c = std::make_unique<spice_circuit>();
        c->circuit.name = name;
        *out_circuit = c.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

void spice_circuit_destroy(spice_circuit_t* circuit) {
    if (circuit == nullptr || !circuit->valid()) return;
    circuit->magic = 0;
    delete circuit;
}

spice_status_t spice_circuit_add_resistor(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double ohms) {
    try {
        if (circuit == nullptr || !circuit->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (id == nullptr || node_p == nullptr || node_n == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        const auto p = circuit->circuit.get_or_add_node(node_p);
        const auto n = circuit->circuit.get_or_add_node(node_n);
        circuit->circuit.elements.push_back(spice::core::ResistorSpec{id, p, n, ohms});
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_add_capacitor(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double farads,
    int has_ic,
    double ic_volts) {
    try {
        if (circuit == nullptr || !circuit->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (id == nullptr || node_p == nullptr || node_n == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        const auto p = circuit->circuit.get_or_add_node(node_p);
        const auto n = circuit->circuit.get_or_add_node(node_n);
        circuit->circuit.elements.push_back(spice::core::CapacitorSpec{
            id, p, n, farads, has_ic != 0 ? std::optional<double>{ic_volts} : std::nullopt});
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_add_inductor(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double henries,
    int has_ic,
    double ic_amps) {
    try {
        if (circuit == nullptr || !circuit->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (id == nullptr || node_p == nullptr || node_n == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        const auto p = circuit->circuit.get_or_add_node(node_p);
        const auto n = circuit->circuit.get_or_add_node(node_n);
        circuit->circuit.elements.push_back(spice::core::InductorSpec{
            id, p, n, henries, has_ic != 0 ? std::optional<double>{ic_amps} : std::nullopt});
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_add_voltage_source(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double dc_volts,
    double ac_real,
    double ac_imag) {
    try {
        if (circuit == nullptr || !circuit->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (id == nullptr || node_p == nullptr || node_n == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        const auto p = circuit->circuit.get_or_add_node(node_p);
        const auto n = circuit->circuit.get_or_add_node(node_n);
        circuit->circuit.elements.push_back(
            spice::core::VoltageSourceSpec{id, p, n, dc_volts, {ac_real, ac_imag}, std::nullopt});
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_add_current_source(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double dc_amps,
    double ac_real,
    double ac_imag) {
    try {
        if (circuit == nullptr || !circuit->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (id == nullptr || node_p == nullptr || node_n == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        const auto p = circuit->circuit.get_or_add_node(node_p);
        const auto n = circuit->circuit.get_or_add_node(node_n);
        circuit->circuit.elements.push_back(
            spice::core::CurrentSourceSpec{id, p, n, dc_amps, {ac_real, ac_imag}, std::nullopt});
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_add_vccs(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    const char* ctrl_p,
    const char* ctrl_n,
    double siemens) {
    try {
        if (circuit == nullptr || !circuit->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (id == nullptr || node_p == nullptr || node_n == nullptr || ctrl_p == nullptr ||
            ctrl_n == nullptr) {
            return SPICE_STATUS_INVALID_ARGUMENT;
        }
        const auto p = circuit->circuit.get_or_add_node(node_p);
        const auto n = circuit->circuit.get_or_add_node(node_n);
        const auto cp = circuit->circuit.get_or_add_node(ctrl_p);
        const auto cn = circuit->circuit.get_or_add_node(ctrl_n);
        circuit->circuit.elements.push_back(spice::core::VccsSpec{id, p, n, cp, cn, siemens});
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_add_vcvs(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    const char* ctrl_p,
    const char* ctrl_n,
    double gain) {
    try {
        if (circuit == nullptr || !circuit->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (id == nullptr || node_p == nullptr || node_n == nullptr || ctrl_p == nullptr ||
            ctrl_n == nullptr) {
            return SPICE_STATUS_INVALID_ARGUMENT;
        }
        const auto p = circuit->circuit.get_or_add_node(node_p);
        const auto n = circuit->circuit.get_or_add_node(node_n);
        const auto cp = circuit->circuit.get_or_add_node(ctrl_p);
        const auto cn = circuit->circuit.get_or_add_node(ctrl_n);
        circuit->circuit.elements.push_back(spice::core::VcvsSpec{id, p, n, cp, cn, gain});
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_add_cccs(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    const char* control_source_id,
    double gain) {
    try {
        if (circuit == nullptr || !circuit->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (id == nullptr || node_p == nullptr || node_n == nullptr || control_source_id == nullptr) {
            return SPICE_STATUS_INVALID_ARGUMENT;
        }
        const auto p = circuit->circuit.get_or_add_node(node_p);
        const auto n = circuit->circuit.get_or_add_node(node_n);
        circuit->circuit.elements.push_back(spice::core::CccsSpec{id, p, n, control_source_id, gain});
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_add_ccvs(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    const char* control_source_id,
    double ohms) {
    try {
        if (circuit == nullptr || !circuit->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (id == nullptr || node_p == nullptr || node_n == nullptr || control_source_id == nullptr) {
            return SPICE_STATUS_INVALID_ARGUMENT;
        }
        const auto p = circuit->circuit.get_or_add_node(node_p);
        const auto n = circuit->circuit.get_or_add_node(node_n);
        circuit->circuit.elements.push_back(spice::core::CcvsSpec{id, p, n, control_source_id, ohms});
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_add_diode(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double is_amps,
    double n_emission,
    double cjo_farads) {
    try {
        if (circuit == nullptr || !circuit->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (id == nullptr || node_p == nullptr || node_n == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        spice::core::DiodeSpec d{};
        d.id = id;
        d.p = circuit->circuit.get_or_add_node(node_p);
        d.n = circuit->circuit.get_or_add_node(node_n);
        d.is_amps = is_amps;
        d.n_emission = n_emission;
        d.cjo_farads = cjo_farads;
        d.gmin_siemens = 0.0;
        circuit->circuit.elements.push_back(std::move(d));
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_circuit_parse_spice(
    const spice_engine_t* engine,
    const char* spice_netlist,
    const char* base_dir,
    spice_circuit_t** out_circuit) {
    try {
        if (engine == nullptr || !engine->valid()) return SPICE_STATUS_INVALID_HANDLE;
        if (spice_netlist == nullptr || out_circuit == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        *out_circuit = nullptr;

        spice::parser::ParseOptions popts{};
        popts.max_unknowns = engine->max_unknowns;
        if (base_dir != nullptr && base_dir[0] != '\0') {
            popts.base_dir = base_dir;
        }
        spice::parser::ParsedDeck deck{};
        if (auto d = spice::parser::parse_spice_deck(spice_netlist, deck, popts); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        auto ckt = std::make_unique<spice_circuit>();
        ckt->circuit = std::move(deck.circuit);
        engine->last_error.clear();
        *out_circuit = ckt.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_run_op(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    spice_result_t** out_result) {
    try {
        if (engine == nullptr || !engine->valid() || circuit == nullptr || !circuit->valid()) {
            return SPICE_STATUS_INVALID_HANDLE;
        }
        if (out_result == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        *out_result = nullptr;

        spice::linalg::BackendPreference pref{};
        if (!map_backend(backend, pref)) {
            engine->last_error = "invalid backend selector";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }

        spice::devices::CompiledPlan plan{};
        if (auto d = spice::devices::compile_circuit(circuit->circuit, plan, engine->max_unknowns);
            !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        spice::analysis::AnalysisCommonOptions opts{};
        opts.backend = pref;
        opts.enable_audits = engine->audits_enabled;

        spice::analysis::OpResult op_res{};
        if (auto d = spice::analysis::run_op(plan, opts, op_res); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        auto res = std::make_unique<spice_result>();
        res->is_complex = false;
        res->audit_passed = op_res.audit.has_value() && op_res.audit->all_checked_passed();
        for (const auto& s : plan.signals) res->signal_names.push_back(s.id);
        res->axis = {0.0};
        res->real_values = std::move(op_res.values);
        engine->last_error.clear();
        *out_result = res.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_run_ac(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    const double* frequencies_hz,
    size_t frequency_count,
    spice_result_t** out_result) {
    try {
        if (engine == nullptr || !engine->valid() || circuit == nullptr || !circuit->valid()) {
            return SPICE_STATUS_INVALID_HANDLE;
        }
        if (out_result == nullptr || frequencies_hz == nullptr || frequency_count == 0u) {
            return SPICE_STATUS_INVALID_ARGUMENT;
        }
        *out_result = nullptr;

        spice::linalg::BackendPreference pref{};
        if (!map_backend(backend, pref)) {
            engine->last_error = "invalid backend selector";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }

        spice::devices::CompiledPlan plan{};
        if (auto d = spice::devices::compile_circuit(circuit->circuit, plan, engine->max_unknowns);
            !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        spice::analysis::AcOptions ac_opts{};
        ac_opts.frequencies_hz.assign(frequencies_hz, frequencies_hz + frequency_count);
        ac_opts.common.backend = pref;
        ac_opts.common.enable_audits = engine->audits_enabled;

        spice::analysis::AcResult ac_res{};
        if (auto d = spice::analysis::run_ac(plan, ac_opts, ac_res); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        auto res = std::make_unique<spice_result>();
        res->is_complex = true;
        res->audit_passed = ac_res.audit.has_value() && ac_res.audit->all_checked_passed();
        for (const auto& s : plan.signals) res->signal_names.push_back(s.id);
        res->axis.reserve(ac_res.points.size());
        res->complex_interleaved.reserve(ac_res.points.size() * plan.signals.size() * 2u);
        for (const auto& pt : ac_res.points) {
            res->axis.push_back(pt.frequency_hz);
            for (const auto& z : pt.values) {
                res->complex_interleaved.push_back(z.real());
                res->complex_interleaved.push_back(z.imag());
            }
        }
        engine->last_error.clear();
        *out_result = res.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_run_transient(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    double step_s,
    double stop_s,
    spice_integration_method_t method,
    spice_startup_mode_t startup,
    spice_result_t** out_result) {
    try {
        if (engine == nullptr || !engine->valid() || circuit == nullptr || !circuit->valid()) {
            return SPICE_STATUS_INVALID_HANDLE;
        }
        if (out_result == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        *out_result = nullptr;

        spice::linalg::BackendPreference pref{};
        if (!map_backend(backend, pref)) {
            engine->last_error = "invalid backend selector";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }

        spice::devices::CompiledPlan plan{};
        if (auto d = spice::devices::compile_circuit(circuit->circuit, plan, engine->max_unknowns);
            !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        spice::analysis::TransientOptions tran_opts{};
        tran_opts.step_s = step_s;
        tran_opts.stop_s = stop_s;
        tran_opts.method = map_method(method);
        tran_opts.startup = (startup == SPICE_STARTUP_UIC)
                                ? spice::analysis::StartupMode::Uic
                                : spice::analysis::StartupMode::OperatingPoint;
        tran_opts.common.backend = pref;
        tran_opts.common.enable_audits = engine->audits_enabled;

        spice::analysis::TransientResult tran_res{};
        if (auto d = spice::analysis::run_transient(plan, tran_opts, tran_res); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        auto res = std::make_unique<spice_result>();
        res->is_complex = false;
        res->audit_passed = tran_res.audit.has_value() && tran_res.audit->all_checked_passed();
        for (const auto& s : plan.signals) res->signal_names.push_back(s.id);
        res->axis.reserve(tran_res.samples.size());
        res->real_values.reserve(tran_res.samples.size() * plan.signals.size());
        for (const auto& smp : tran_res.samples) {
            res->axis.push_back(smp.time_s);
            res->real_values.insert(res->real_values.end(), smp.values.begin(), smp.values.end());
        }
        engine->last_error.clear();
        *out_result = res.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_run_dc_sweep(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    const char* source_id,
    double start_val,
    double stop_val,
    double step_val,
    spice_result_t** out_result) {
    try {
        if (engine == nullptr || !engine->valid() || circuit == nullptr || !circuit->valid()) {
            return SPICE_STATUS_INVALID_HANDLE;
        }
        if (source_id == nullptr || out_result == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        *out_result = nullptr;

        spice::linalg::BackendPreference pref{};
        if (!map_backend(backend, pref)) {
            engine->last_error = "invalid backend selector";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }

        spice::devices::CompiledPlan plan{};
        if (auto d = spice::devices::compile_circuit(circuit->circuit, plan, engine->max_unknowns); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        spice::analysis::DcSweepOptions opts{};
        opts.source_id = source_id;
        opts.start = start_val;
        opts.stop = stop_val;
        opts.step = step_val;
        opts.common.backend = pref;
        opts.common.enable_audits = engine->audits_enabled;

        spice::analysis::DcSweepResult dc_res{};
        if (auto d = spice::analysis::run_dc_sweep(plan, opts, dc_res); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        auto res = std::make_unique<spice_result>();
        res->is_complex = false;
        res->audit_passed = true;
        for (const auto& s : plan.signals) res->signal_names.push_back(s.id);
        res->axis.reserve(dc_res.points.size());
        res->real_values.reserve(dc_res.points.size() * plan.signals.size());
        for (const auto& pt : dc_res.points) {
            res->axis.push_back(pt.sweep_value);
            res->real_values.insert(res->real_values.end(), pt.values.begin(), pt.values.end());
        }
        engine->last_error.clear();
        *out_result = res.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_run_noise(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    const char* out_p,
    const char* out_n,
    const char* input_source_id,
    const double* frequencies_hz,
    size_t frequency_count,
    double temperature_k,
    spice_result_t** out_result) {
    try {
        if (engine == nullptr || !engine->valid() || circuit == nullptr || !circuit->valid()) {
            return SPICE_STATUS_INVALID_HANDLE;
        }
        if (out_p == nullptr || out_n == nullptr || input_source_id == nullptr ||
            frequencies_hz == nullptr || frequency_count == 0u || out_result == nullptr) {
            return SPICE_STATUS_INVALID_ARGUMENT;
        }
        *out_result = nullptr;

        spice::linalg::BackendPreference pref{};
        if (!map_backend(backend, pref)) {
            engine->last_error = "invalid backend selector";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }

        const auto np = find_node_icase(circuit->circuit, out_p);
        const auto nn = find_node_icase(circuit->circuit, out_n);
        if (!np.has_value() || !nn.has_value()) {
            engine->last_error = "unknown noise output node";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }

        spice::devices::CompiledPlan plan{};
        if (auto d = spice::devices::compile_circuit(circuit->circuit, plan, engine->max_unknowns); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        spice::analysis::NoiseOptions opts{};
        opts.out_p = *np;
        opts.out_n = *nn;
        opts.input_source_id = input_source_id;
        opts.frequencies_hz.assign(frequencies_hz, frequencies_hz + frequency_count);
        opts.temperature_k = (temperature_k > 0.0) ? temperature_k : 300.15;
        opts.common.backend = pref;
        opts.common.enable_audits = engine->audits_enabled;

        spice::analysis::NoiseResult nr{};
        if (auto d = spice::analysis::run_noise(plan, opts, nr); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        auto res = std::make_unique<spice_result>();
        res->is_complex = false;
        res->audit_passed = true;
        res->signal_names = {"onoise_v_per_sqrt_hz", "inoise_v_per_sqrt_hz", "transfer_gain_mag"};
        res->axis.reserve(nr.points.size());
        res->real_values.reserve(nr.points.size() * 3u);
        for (const auto& pt : nr.points) {
            res->axis.push_back(pt.frequency_hz);
            res->real_values.push_back(pt.onoise_v_per_sqrt_hz);
            res->real_values.push_back(pt.inoise_v_per_sqrt_hz);
            res->real_values.push_back(pt.transfer_gain_mag);
        }
        engine->last_error.clear();
        *out_result = res.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_run_hb(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    double fundamental_hz,
    size_t harmonics,
    spice_result_t** out_result) {
    try {
        if (engine == nullptr || !engine->valid() || circuit == nullptr || !circuit->valid()) {
            return SPICE_STATUS_INVALID_HANDLE;
        }
        if (out_result == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        *out_result = nullptr;

        spice::linalg::BackendPreference pref{};
        if (!map_backend(backend, pref)) {
            engine->last_error = "invalid backend selector";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }

        spice::devices::CompiledPlan plan{};
        if (auto d = spice::devices::compile_circuit(circuit->circuit, plan, engine->max_unknowns); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        spice::analysis::HbOptions opts{};
        opts.fundamental_hz = fundamental_hz;
        opts.harmonics = harmonics;
        opts.common.backend = pref;
        opts.common.enable_audits = engine->audits_enabled;

        spice::analysis::HbResult hb{};
        if (auto d = spice::analysis::run_hb(plan, opts, hb); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        auto res = std::make_unique<spice_result>();
        res->is_complex = true;
        res->audit_passed = hb.converged;
        const std::size_t n_sig = plan.signals.size();
        const std::size_t n_harm = hb.harmonics + 1u;
        for (const auto& s : plan.signals) res->signal_names.push_back(s.id);
        res->axis.reserve(n_harm);
        res->complex_interleaved.reserve(2u * n_harm * n_sig);
        for (std::size_t h = 0; h < n_harm; ++h) {
            res->axis.push_back(static_cast<double>(h) * hb.fundamental_hz);
            for (std::size_t s = 0; s < n_sig; ++s) {
                const auto v = hb.phasors[s][h];
                res->complex_interleaved.push_back(v.real());
                res->complex_interleaved.push_back(v.imag());
            }
        }
        engine->last_error.clear();
        *out_result = res.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_run_pss(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    double fundamental_hz,
    size_t steps_per_period,
    spice_integration_method_t method,
    spice_result_t** out_result) {
    try {
        if (engine == nullptr || !engine->valid() || circuit == nullptr || !circuit->valid()) {
            return SPICE_STATUS_INVALID_HANDLE;
        }
        if (out_result == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
        *out_result = nullptr;

        spice::linalg::BackendPreference pref{};
        if (!map_backend(backend, pref)) {
            engine->last_error = "invalid backend selector";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }

        spice::devices::CompiledPlan plan{};
        if (auto d = spice::devices::compile_circuit(circuit->circuit, plan, engine->max_unknowns); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        spice::analysis::PssOptions opts{};
        opts.fundamental_hz = fundamental_hz;
        opts.steps_per_period = steps_per_period;
        opts.method = (method == SPICE_METHOD_BACKWARD_EULER)
                          ? spice::analysis::IntegrationMethod::BackwardEuler
                          : spice::analysis::IntegrationMethod::Trapezoidal;
        opts.common.backend = pref;
        opts.common.enable_audits = engine->audits_enabled;

        spice::analysis::PssResult pss{};
        if (auto d = spice::analysis::run_pss(plan, opts, pss); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        auto res = std::make_unique<spice_result>();
        res->is_complex = false;
        res->audit_passed = pss.converged;
        for (const auto& s : plan.signals) res->signal_names.push_back(s.id);
        res->axis.reserve(pss.waveform.size());
        res->real_values.reserve(pss.waveform.size() * plan.signals.size());
        for (const auto& smp : pss.waveform) {
            res->axis.push_back(smp.time_s);
            res->real_values.insert(res->real_values.end(), smp.values.begin(), smp.values.end());
        }
        engine->last_error.clear();
        *out_result = res.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_run_sp(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    const char* const* port_p_nodes,
    const char* const* port_n_nodes,
    size_t port_count,
    double z0_ohms,
    const double* frequencies_hz,
    size_t frequency_count,
    spice_result_t** out_result) {
    try {
        if (engine == nullptr || !engine->valid() || circuit == nullptr || !circuit->valid()) {
            return SPICE_STATUS_INVALID_HANDLE;
        }
        if (port_p_nodes == nullptr || port_n_nodes == nullptr || port_count == 0u ||
            frequencies_hz == nullptr || frequency_count == 0u || out_result == nullptr) {
            return SPICE_STATUS_INVALID_ARGUMENT;
        }
        *out_result = nullptr;

        spice::linalg::BackendPreference pref{};
        if (!map_backend(backend, pref)) {
            engine->last_error = "invalid backend selector";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }

        spice::analysis::SpOptions opts{};
        opts.frequencies_hz.assign(frequencies_hz, frequencies_hz + frequency_count);
        opts.common.backend = pref;
        opts.common.enable_audits = engine->audits_enabled;
        for (std::size_t i = 0; i < port_count; ++i) {
            const auto np = find_node_icase(circuit->circuit, port_p_nodes[i]);
            const auto nn = find_node_icase(circuit->circuit, port_n_nodes[i]);
            if (!np.has_value() || !nn.has_value()) {
                engine->last_error = "unknown S-parameter port node";
                return SPICE_STATUS_INVALID_ARGUMENT;
            }
            opts.ports.push_back(spice::analysis::PortSpec{*np, *nn, z0_ohms});
        }

        spice::devices::CompiledPlan plan{};
        if (auto d = spice::devices::compile_circuit(circuit->circuit, plan, engine->max_unknowns); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        spice::analysis::SpResult sp{};
        if (auto d = spice::analysis::run_sp(plan, opts, sp); !d.ok()) {
            engine->last_error = d.message;
            return map_status(d.code);
        }

        auto res = std::make_unique<spice_result>();
        res->is_complex = true;
        res->audit_passed = true;
        for (std::size_t r = 0; r < port_count; ++r) {
            for (std::size_t c = 0; c < port_count; ++c) {
                res->signal_names.push_back("S" + std::to_string(r + 1u) + std::to_string(c + 1u));
            }
        }
        res->axis.reserve(sp.points.size());
        res->complex_interleaved.reserve(2u * sp.points.size() * port_count * port_count);
        for (const auto& pt : sp.points) {
            res->axis.push_back(pt.frequency_hz);
            for (const auto& v : pt.s_matrix) {
                res->complex_interleaved.push_back(v.real());
                res->complex_interleaved.push_back(v.imag());
            }
        }
        engine->last_error.clear();
        *out_result = res.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

void spice_result_destroy(spice_result_t* result) {
    if (result == nullptr || !result->valid()) return;
    result->magic = 0;
    delete result;
}

spice_status_t spice_result_is_complex(const spice_result_t* result, int* out_is_complex) {
    if (result == nullptr || !result->valid()) return SPICE_STATUS_INVALID_HANDLE;
    if (out_is_complex == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
    *out_is_complex = result->is_complex ? 1 : 0;
    return SPICE_STATUS_OK;
}

spice_status_t spice_result_signal_count(const spice_result_t* result, size_t* out_count) {
    if (result == nullptr || !result->valid()) return SPICE_STATUS_INVALID_HANDLE;
    if (out_count == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
    *out_count = result->signal_names.size();
    return SPICE_STATUS_OK;
}

spice_status_t spice_result_signal_name(
    const spice_result_t* result,
    size_t signal_index,
    const char** out_name) {
    if (result == nullptr || !result->valid()) return SPICE_STATUS_INVALID_HANDLE;
    if (out_name == nullptr || signal_index >= result->signal_names.size()) {
        return SPICE_STATUS_INVALID_ARGUMENT;
    }
    *out_name = result->signal_names[signal_index].c_str();
    return SPICE_STATUS_OK;
}

spice_status_t spice_result_sample_count(const spice_result_t* result, size_t* out_count) {
    if (result == nullptr || !result->valid()) return SPICE_STATUS_INVALID_HANDLE;
    if (out_count == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
    *out_count = result->axis.size();
    return SPICE_STATUS_OK;
}

spice_status_t spice_result_axis_data(const spice_result_t* result, const double** out_axis) {
    if (result == nullptr || !result->valid()) return SPICE_STATUS_INVALID_HANDLE;
    if (out_axis == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
    *out_axis = result->axis.data();
    return SPICE_STATUS_OK;
}

spice_status_t spice_result_real_data(const spice_result_t* result, const double** out_values) {
    if (result == nullptr || !result->valid()) return SPICE_STATUS_INVALID_HANDLE;
    if (out_values == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
    if (result->is_complex) return SPICE_STATUS_INVALID_ARGUMENT;
    *out_values = result->real_values.data();
    return SPICE_STATUS_OK;
}

spice_status_t spice_result_complex_data(
    const spice_result_t* result,
    const double** out_interleaved_re_im) {
    if (result == nullptr || !result->valid()) return SPICE_STATUS_INVALID_HANDLE;
    if (out_interleaved_re_im == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
    if (!result->is_complex) return SPICE_STATUS_INVALID_ARGUMENT;
    *out_interleaved_re_im = result->complex_interleaved.data();
    return SPICE_STATUS_OK;
}

spice_status_t spice_result_audit_passed(const spice_result_t* result, int* out_passed) {
    if (result == nullptr || !result->valid()) return SPICE_STATUS_INVALID_HANDLE;
    if (out_passed == nullptr) return SPICE_STATUS_INVALID_ARGUMENT;
    *out_passed = result->audit_passed ? 1 : 0;
    return SPICE_STATUS_OK;
}

}  // extern "C"
