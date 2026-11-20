#pragma once

#include "core/types.hpp"

#include <complex>
#include <cstdint>
#include <optional>
#include <string>
#include <variant>
#include <vector>

namespace spice::core {

/*
 * Source waveforms (SI units: seconds, volts/amperes, hertz, degrees).
 * Breakpoints are reported explicitly to transient integration (ARCH-EQ-005).
 */
struct DcWaveform {
    double value{0.0};
};

struct PulseWaveform {
    double v1{0.0};
    double v2{0.0};
    double delay_s{0.0};
    double rise_s{0.0};
    double fall_s{0.0};
    double width_s{0.0};
    double period_s{0.0}; /* 0 means non-periodic single pulse */
};

struct SineWaveform {
    double offset{0.0};
    double amplitude{0.0};
    double freq_hz{0.0};
    double delay_s{0.0};
    double damping_per_s{0.0};
    double phase_deg{0.0};
};

struct PwlPoint {
    double time_s{0.0};
    double value{0.0};
};

struct PwlWaveform {
    std::vector<PwlPoint> points;
};

using Waveform = std::variant<DcWaveform, PulseWaveform, SineWaveform, PwlWaveform>;

[[nodiscard]] Diagnostic validate_waveform(const Waveform& wf, const std::string& subject);
[[nodiscard]] double evaluate_waveform(const Waveform& wf, double time_s) noexcept;
void collect_waveform_breakpoints(
    const Waveform& wf,
    double t_start,
    double t_stop,
    std::vector<double>& out_breakpoints);

/*
 * Canonical linear element specifications (SI passive sign convention:
 * positive current flows from node p to node n).
 */
struct ResistorSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double ohms{0.0};
};

struct CapacitorSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double farads{0.0};
    std::optional<double> ic_volts;
};

struct InductorSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double henries{0.0};
    std::optional<double> ic_amps;
};

struct VoltageSourceSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double dc_volts{0.0};
    std::complex<double> ac_phasor{0.0, 0.0};
    std::optional<Waveform> transient_waveform;
};

struct CurrentSourceSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double dc_amps{0.0};
    std::complex<double> ac_phasor{0.0, 0.0};
    std::optional<Waveform> transient_waveform;
};

struct VccsSpec {
    std::string id;
    NodeId p;
    NodeId n;
    NodeId cp;
    NodeId cn;
    double siemens{0.0};
};

struct VcvsSpec {
    std::string id;
    NodeId p;
    NodeId n;
    NodeId cp;
    NodeId cn;
    double gain{0.0};
};

struct CccsSpec {
    std::string id;
    NodeId p;
    NodeId n;
    std::string control_source_id;
    double gain{0.0};
};

struct CcvsSpec {
    std::string id;
    NodeId p;
    NodeId n;
    std::string control_source_id;
    double ohms{0.0};
};

using ElementSpec = std::variant<
    ResistorSpec,
    CapacitorSpec,
    InductorSpec,
    VoltageSourceSpec,
    CurrentSourceSpec,
    VccsSpec,
    VcvsSpec,
    CccsSpec,
    CcvsSpec>;

struct SignalDescriptor {
    std::string id;        /* e.g. "v(out)" or "i(V1)" */
    std::string quantity;  /* "voltage" or "current" */
    std::string si_unit;   /* "V" or "A" */
};

/*
 * Immutable canonical circuit IR (ARCH-STATE-001, ARCH-STATE-003).
 * Node 0 is always ground ("0"). No arbitrary 128-node/256-unknown cap.
 */
struct Circuit {
    std::string name;
    std::vector<std::string> nodes{"0"};
    std::vector<ElementSpec> elements;

    /* Helper: find or append a node name and return its NodeId. */
    [[nodiscard]] NodeId get_or_add_node(const std::string& node_name);

    /* Validate all topology, parameter domain, reference, and budget invariants. */
    [[nodiscard]] Diagnostic validate(std::uint64_t max_unknowns = 1'000'000) const;
};

[[nodiscard]] const std::string& element_id(const ElementSpec& e) noexcept;
[[nodiscard]] double source_value_at(const VoltageSourceSpec& s, double time_s, bool is_transient) noexcept;
[[nodiscard]] double source_value_at(const CurrentSourceSpec& s, double time_s, bool is_transient) noexcept;

}  // namespace spice::core
