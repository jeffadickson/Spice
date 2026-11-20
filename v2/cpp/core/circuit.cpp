#include "core/circuit.hpp"

#include <algorithm>
#include <cmath>
#include <numbers>
#include <set>

namespace spice::core {

namespace {

constexpr std::size_t kMaxBreakpointsPerSource = 100'000u;

template <class... Ts>
struct Overloaded : Ts... {
    using Ts::operator()...;
};
template <class... Ts>
Overloaded(Ts...) -> Overloaded<Ts...>;

bool is_finite_complex(std::complex<double> z) noexcept {
    return std::isfinite(z.real()) && std::isfinite(z.imag());
}

}  // namespace

Diagnostic validate_waveform(const Waveform& wf, const std::string& subject) {
    return std::visit(
        Overloaded{
            [&](const DcWaveform& w) -> Diagnostic {
                if (!std::isfinite(w.value)) {
                    return Diagnostic::error(StatusCode::InvalidArgument, "nonfinite DC value", subject);
                }
                return Diagnostic::success();
            },
            [&](const PulseWaveform& w) -> Diagnostic {
                if (!std::isfinite(w.v1) || !std::isfinite(w.v2) || !std::isfinite(w.delay_s) ||
                    !std::isfinite(w.rise_s) || !std::isfinite(w.fall_s) || !std::isfinite(w.width_s) ||
                    !std::isfinite(w.period_s) || w.delay_s < 0.0 || w.rise_s < 0.0 ||
                    w.fall_s < 0.0 || w.width_s < 0.0 || w.period_s < 0.0) {
                    return Diagnostic::error(
                        StatusCode::InvalidArgument, "invalid PULSE waveform parameters", subject);
                }
                if (w.period_s > 0.0 && w.rise_s + w.width_s + w.fall_s > w.period_s) {
                    return Diagnostic::error(
                        StatusCode::InvalidArgument, "PULSE edges exceed period", subject);
                }
                return Diagnostic::success();
            },
            [&](const SineWaveform& w) -> Diagnostic {
                if (!std::isfinite(w.offset) || !std::isfinite(w.amplitude) || !std::isfinite(w.freq_hz) ||
                    !std::isfinite(w.delay_s) || !std::isfinite(w.damping_per_s) ||
                    !std::isfinite(w.phase_deg) || w.freq_hz < 0.0 || w.delay_s < 0.0) {
                    return Diagnostic::error(
                        StatusCode::InvalidArgument, "invalid SIN waveform parameters", subject);
                }
                return Diagnostic::success();
            },
            [&](const PwlWaveform& w) -> Diagnostic {
                if (w.points.empty()) {
                    return Diagnostic::error(
                        StatusCode::InvalidArgument, "PWL waveform must have at least one point", subject);
                }
                for (std::size_t i = 0; i < w.points.size(); ++i) {
                    if (!std::isfinite(w.points[i].time_s) || !std::isfinite(w.points[i].value)) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "nonfinite PWL point", subject);
                    }
                    if (i > 0 && w.points[i].time_s < w.points[i - 1].time_s) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "PWL times must be non-decreasing", subject);
                    }
                }
                return Diagnostic::success();
            }},
        wf);
}

double evaluate_waveform(const Waveform& wf, double time_s) noexcept {
    return std::visit(
        Overloaded{
            [](const DcWaveform& w) noexcept -> double { return w.value; },
            [time_s](const PulseWaveform& w) noexcept -> double {
                if (time_s <= w.delay_s) return w.v1;
                double t = time_s - w.delay_s;
                if (w.period_s > 0.0) {
                    t = std::fmod(t, w.period_s);
                    if (t < 0.0) t += w.period_s;
                }
                if (w.rise_s > 0.0 && t < w.rise_s) {
                    return w.v1 + (w.v2 - w.v1) * (t / w.rise_s);
                }
                if (t < w.rise_s + w.width_s) {
                    return w.v2;
                }
                const double fall_start = w.rise_s + w.width_s;
                if (w.fall_s > 0.0 && t < fall_start + w.fall_s) {
                    return w.v2 + (w.v1 - w.v2) * ((t - fall_start) / w.fall_s);
                }
                return w.v1;
            },
            [time_s](const SineWaveform& w) noexcept -> double {
                const double phase_rad = w.phase_deg * (std::numbers::pi / 180.0);
                if (time_s < w.delay_s) {
                    return w.offset + w.amplitude * std::sin(phase_rad);
                }
                const double dt = time_s - w.delay_s;
                return w.offset + w.amplitude * std::exp(-w.damping_per_s * dt) *
                                      std::sin(2.0 * std::numbers::pi * w.freq_hz * dt + phase_rad);
            },
            [time_s](const PwlWaveform& w) noexcept -> double {
                if (w.points.empty()) return 0.0;
                if (time_s <= w.points.front().time_s) return w.points.front().value;
                if (time_s >= w.points.back().time_s) return w.points.back().value;
                for (std::size_t i = 1; i < w.points.size(); ++i) {
                    const auto& a = w.points[i - 1];
                    const auto& b = w.points[i];
                    if (time_s <= b.time_s) {
                        const double dt = b.time_s - a.time_s;
                        if (dt <= 0.0) return b.value;
                        return a.value + (b.value - a.value) * ((time_s - a.time_s) / dt);
                    }
                }
                return w.points.back().value;
            }},
        wf);
}

void collect_waveform_breakpoints(
    const Waveform& wf,
    double t_start,
    double t_stop,
    std::vector<double>& out_breakpoints) {
    auto push_if_in_range = [&](double t) {
        if (t > t_start && t < t_stop && std::isfinite(t)) {
            out_breakpoints.push_back(t);
        }
    };
    std::visit(
        Overloaded{
            [](const DcWaveform&) {},
            [&](const PulseWaveform& w) {
                const double offsets[4] = {
                    0.0,
                    w.rise_s,
                    w.rise_s + w.width_s,
                    w.rise_s + w.width_s + w.fall_s};
                if (w.period_s <= 0.0) {
                    for (double off : offsets) push_if_in_range(w.delay_s + off);
                    return;
                }
                std::size_t added = 0;
                for (std::size_t k = 0; added < kMaxBreakpointsPerSource; ++k) {
                    const double base = w.delay_s + static_cast<double>(k) * w.period_s;
                    if (base >= t_stop) break;
                    for (double off : offsets) {
                        push_if_in_range(base + off);
                        ++added;
                    }
                }
            },
            [&](const SineWaveform& w) {
                if (w.delay_s > 0.0) push_if_in_range(w.delay_s);
            },
            [&](const PwlWaveform& w) {
                for (const auto& pt : w.points) push_if_in_range(pt.time_s);
            }},
        wf);
}

NodeId Circuit::get_or_add_node(const std::string& node_name) {
    for (std::uint32_t i = 0; i < nodes.size(); ++i) {
        if (nodes[i] == node_name) return NodeId{i};
    }
    const auto id = static_cast<std::uint32_t>(nodes.size());
    nodes.push_back(node_name);
    return NodeId{id};
}

const std::string& element_id(const ElementSpec& e) noexcept {
    return std::visit([](const auto& spec) -> const std::string& { return spec.id; }, e);
}

double source_value_at(const VoltageSourceSpec& s, double time_s, bool is_transient) noexcept {
    if (is_transient && s.transient_waveform.has_value()) {
        return evaluate_waveform(*s.transient_waveform, time_s);
    }
    return s.dc_volts;
}

double source_value_at(const CurrentSourceSpec& s, double time_s, bool is_transient) noexcept {
    if (is_transient && s.transient_waveform.has_value()) {
        return evaluate_waveform(*s.transient_waveform, time_s);
    }
    return s.dc_amps;
}

Diagnostic Circuit::validate(std::uint64_t max_unknowns) const {
    if (nodes.empty() || nodes.front() != "0") {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "nodes[0] must be ground \"0\"", "Circuit.nodes");
    }
    std::set<std::string> seen_nodes;
    for (const auto& n : nodes) {
        if (n.empty() || !seen_nodes.insert(n).second) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "node names must be nonempty and unique", n);
        }
    }

    std::set<std::string> seen_ids;
    std::set<std::string> branch_source_ids;
    std::set<std::string> inductor_ids;
    std::uint64_t branch_count = 0;
    for (const auto& e : elements) {
        const auto& id = element_id(e);
        if (id.empty() || !seen_ids.insert(id).second) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "element IDs must be nonempty and unique", id);
        }
        if (std::holds_alternative<InductorSpec>(e)) {
            inductor_ids.insert(id);
        }
        if (std::holds_alternative<VoltageSourceSpec>(e) ||
            std::holds_alternative<InductorSpec>(e) ||
            std::holds_alternative<VcvsSpec>(e) ||
            std::holds_alternative<CcvsSpec>(e)) {
            ++branch_count;
            branch_source_ids.insert(id);
        } else if (const auto* b = std::get_if<BehavioralSourceSpec>(&e); b != nullptr && b->is_voltage) {
            ++branch_count;
            branch_source_ids.insert(id);
        }
    }

    const std::uint64_t total_unknowns = (nodes.size() - 1u) + branch_count;
    if (total_unknowns == 0) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "circuit must have at least one unknown", name);
    }
    if (total_unknowns > max_unknowns) {
        return Diagnostic::error(
            StatusCode::ResourceExhausted, "circuit unknowns exceed caller budget", name);
    }

    const auto check_node = [&](NodeId id, const std::string& subj) -> Diagnostic {
        if (id.value >= nodes.size()) {
            return Diagnostic::error(StatusCode::InvalidArgument, "node index out of bounds", subj);
        }
        return Diagnostic::success();
    };

    for (const auto& e : elements) {
        Diagnostic d = std::visit(
            Overloaded{
                [&](const ResistorSpec& r) -> Diagnostic {
                    if (auto x = check_node(r.p, r.id); !x.ok()) return x;
                    if (auto x = check_node(r.n, r.id); !x.ok()) return x;
                    if (r.p == r.n || !std::isfinite(r.ohms) || r.ohms == 0.0) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid resistor terminals or ohms", r.id);
                    }
                    return Diagnostic::success();
                },
                [&](const CapacitorSpec& c) -> Diagnostic {
                    if (auto x = check_node(c.p, c.id); !x.ok()) return x;
                    if (auto x = check_node(c.n, c.id); !x.ok()) return x;
                    if (c.p == c.n || !std::isfinite(c.farads) || c.farads <= 0.0) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid capacitor terminals or farads", c.id);
                    }
                    if (c.ic_volts.has_value() && !std::isfinite(*c.ic_volts)) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "nonfinite capacitor IC", c.id);
                    }
                    return Diagnostic::success();
                },
                [&](const InductorSpec& l) -> Diagnostic {
                    if (auto x = check_node(l.p, l.id); !x.ok()) return x;
                    if (auto x = check_node(l.n, l.id); !x.ok()) return x;
                    if (l.p == l.n || !std::isfinite(l.henries) || l.henries <= 0.0) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid inductor terminals or henries", l.id);
                    }
                    if (l.ic_amps.has_value() && !std::isfinite(*l.ic_amps)) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "nonfinite inductor IC", l.id);
                    }
                    return Diagnostic::success();
                },
                [&](const VoltageSourceSpec& v) -> Diagnostic {
                    if (auto x = check_node(v.p, v.id); !x.ok()) return x;
                    if (auto x = check_node(v.n, v.id); !x.ok()) return x;
                    if (v.p == v.n || !std::isfinite(v.dc_volts) || !is_finite_complex(v.ac_phasor)) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid voltage source", v.id);
                    }
                    if (v.transient_waveform.has_value()) {
                        return validate_waveform(*v.transient_waveform, v.id);
                    }
                    return Diagnostic::success();
                },
                [&](const CurrentSourceSpec& i) -> Diagnostic {
                    if (auto x = check_node(i.p, i.id); !x.ok()) return x;
                    if (auto x = check_node(i.n, i.id); !x.ok()) return x;
                    if (i.p == i.n || !std::isfinite(i.dc_amps) || !is_finite_complex(i.ac_phasor)) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid current source", i.id);
                    }
                    if (i.transient_waveform.has_value()) {
                        return validate_waveform(*i.transient_waveform, i.id);
                    }
                    return Diagnostic::success();
                },
                [&](const VccsSpec& g) -> Diagnostic {
                    if (auto x = check_node(g.p, g.id); !x.ok()) return x;
                    if (auto x = check_node(g.n, g.id); !x.ok()) return x;
                    if (auto x = check_node(g.cp, g.id); !x.ok()) return x;
                    if (auto x = check_node(g.cn, g.id); !x.ok()) return x;
                    if (g.p == g.n || g.cp == g.cn || !std::isfinite(g.siemens)) {
                        return Diagnostic::error(StatusCode::InvalidArgument, "invalid VCCS", g.id);
                    }
                    return Diagnostic::success();
                },
                [&](const VcvsSpec& v) -> Diagnostic {
                    if (auto x = check_node(v.p, v.id); !x.ok()) return x;
                    if (auto x = check_node(v.n, v.id); !x.ok()) return x;
                    if (auto x = check_node(v.cp, v.id); !x.ok()) return x;
                    if (auto x = check_node(v.cn, v.id); !x.ok()) return x;
                    if (v.p == v.n || v.cp == v.cn || !std::isfinite(v.gain)) {
                        return Diagnostic::error(StatusCode::InvalidArgument, "invalid VCVS", v.id);
                    }
                    return Diagnostic::success();
                },
                [&](const CccsSpec& f) -> Diagnostic {
                    if (auto x = check_node(f.p, f.id); !x.ok()) return x;
                    if (auto x = check_node(f.n, f.id); !x.ok()) return x;
                    if (f.p == f.n || !std::isfinite(f.gain) ||
                        !branch_source_ids.contains(f.control_source_id)) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid CCCS or unknown control branch", f.id);
                    }
                    return Diagnostic::success();
                },
                [&](const CcvsSpec& h) -> Diagnostic {
                    if (auto x = check_node(h.p, h.id); !x.ok()) return x;
                    if (auto x = check_node(h.n, h.id); !x.ok()) return x;
                    if (h.p == h.n || !std::isfinite(h.ohms) ||
                        !branch_source_ids.contains(h.control_source_id)) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid CCVS or unknown control branch", h.id);
                    }
                    return Diagnostic::success();
                },
                [&](const MutualInductanceSpec& k) -> Diagnostic {
                    if (k.inductor1_id == k.inductor2_id ||
                        !inductor_ids.contains(k.inductor1_id) ||
                        !inductor_ids.contains(k.inductor2_id) ||
                        !std::isfinite(k.coupling_k) || std::abs(k.coupling_k) >= 1.0) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid mutual inductance K (|k| must be < 1)", k.id);
                    }
                    return Diagnostic::success();
                },
                [&](const DiodeSpec& d_spec) -> Diagnostic {
                    if (auto x = check_node(d_spec.p, d_spec.id); !x.ok()) return x;
                    if (auto x = check_node(d_spec.n, d_spec.id); !x.ok()) return x;
                    if (d_spec.p == d_spec.n || !std::isfinite(d_spec.is_amps) || d_spec.is_amps <= 0.0 ||
                        !std::isfinite(d_spec.n_emission) || d_spec.n_emission <= 0.0 ||
                        !std::isfinite(d_spec.vt_volts) || d_spec.vt_volts <= 0.0 ||
                        !std::isfinite(d_spec.cjo_farads) || d_spec.cjo_farads < 0.0 ||
                        !std::isfinite(d_spec.vj_volts) || d_spec.vj_volts <= 0.0 ||
                        !std::isfinite(d_spec.m_grading) || d_spec.m_grading <= 0.0 || d_spec.m_grading >= 1.0 ||
                        !std::isfinite(d_spec.tt_s) || d_spec.tt_s < 0.0 ||
                        !std::isfinite(d_spec.gmin_siemens) || d_spec.gmin_siemens < 0.0) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid diode parameters or terminals", d_spec.id);
                    }
                    return Diagnostic::success();
                },
                [&](const BehavioralSourceSpec& b) -> Diagnostic {
                    if (auto x = check_node(b.p, b.id); !x.ok()) return x;
                    if (auto x = check_node(b.n, b.id); !x.ok()) return x;
                    if (b.p == b.n || b.expression.empty()) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid behavioral source", b.id);
                    }
                    return Diagnostic::success();
                },
                [&](const OsdiInstanceSpec& o) -> Diagnostic {
                    if (o.library_path.empty() || o.model_name.empty() || o.terminals.empty()) {
                        return Diagnostic::error(
                            StatusCode::InvalidArgument, "invalid OSDI instance specification", o.id);
                    }
                    for (NodeId t : o.terminals) {
                        if (auto x = check_node(t, o.id); !x.ok()) return x;
                    }
                    for (const auto& [pname, pval] : o.model_params) {
                        if (pname.empty() || !std::isfinite(pval)) {
                            return Diagnostic::error(
                                StatusCode::InvalidArgument, "invalid OSDI model param", o.id);
                        }
                    }
                    for (const auto& [pname, pval] : o.instance_params) {
                        if (pname.empty() || !std::isfinite(pval)) {
                            return Diagnostic::error(
                                StatusCode::InvalidArgument, "invalid OSDI instance param", o.id);
                        }
                    }
                    return Diagnostic::success();
                }},
            e);
        if (!d.ok()) return d;
    }

    return Diagnostic::success();
}

}  // namespace spice::core
