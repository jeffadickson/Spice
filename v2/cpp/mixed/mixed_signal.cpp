#include "mixed/mixed_signal.hpp"

#include "solver/newton.hpp"

#include <algorithm>
#include <cmath>
#include <iomanip>
#include <limits>
#include <memory>
#include <sstream>
#include <utility>

namespace spice::mixed {

using core::Diagnostic;
using core::StatusCode;
using dae::AnalysisMode;
using dae::EvalContext;
using dae::StampTarget;

/* ==================== IEEE 1364 4-State Logic Primitives ==================== */

char logic4_char(Logic4 v) noexcept {
    switch (v) {
        case Logic4::L0: return '0';
        case Logic4::L1: return '1';
        case Logic4::X: return 'x';
        case Logic4::Z: return 'z';
    }
    return 'x';
}

Logic4 logic_not(Logic4 a) noexcept {
    if (a == Logic4::L0) return Logic4::L1;
    if (a == Logic4::L1) return Logic4::L0;
    return Logic4::X;
}

Logic4 logic_and(Logic4 a, Logic4 b) noexcept {
    if (a == Logic4::L0 || b == Logic4::L0) return Logic4::L0;
    if (a == Logic4::L1 && b == Logic4::L1) return Logic4::L1;
    return Logic4::X;
}

Logic4 logic_or(Logic4 a, Logic4 b) noexcept {
    if (a == Logic4::L1 || b == Logic4::L1) return Logic4::L1;
    if (a == Logic4::L0 && b == Logic4::L0) return Logic4::L0;
    return Logic4::X;
}

Logic4 logic_nand(Logic4 a, Logic4 b) noexcept {
    return logic_not(logic_and(a, b));
}

Logic4 logic_nor(Logic4 a, Logic4 b) noexcept {
    return logic_not(logic_or(a, b));
}

Logic4 logic_xor(Logic4 a, Logic4 b) noexcept {
    if (a == Logic4::L0 && b == Logic4::L0) return Logic4::L0;
    if (a == Logic4::L1 && b == Logic4::L1) return Logic4::L0;
    if ((a == Logic4::L0 && b == Logic4::L1) || (a == Logic4::L1 && b == Logic4::L0)) return Logic4::L1;
    return Logic4::X;
}

/* ==================== Event-Driven Digital Kernel ==================== */

NetId DigitalEngine::add_net(std::string name, Logic4 initial_value) {
    const NetId id{static_cast<std::uint32_t>(nets_.size())};
    NetState st{};
    st.name = std::move(name);
    st.value = initial_value;
    st.history.push_back(DigitalTransition{0.0, initial_value});
    nets_.push_back(std::move(st));
    return id;
}

WrealId DigitalEngine::add_wreal(std::string name, double initial_value) {
    const WrealId id{static_cast<std::uint32_t>(wreals_.size())};
    WrealState st{};
    st.name = std::move(name);
    st.value = initial_value;
    st.history.push_back(WrealTransition{0.0, initial_value});
    wreals_.push_back(std::move(st));
    return id;
}

void DigitalEngine::add_gate(
    std::string id,
    GateKind kind,
    std::vector<NetId> inputs,
    NetId output,
    double delay_s) {
    const std::size_t idx = gates_.size();
    for (NetId in : inputs) {
        nets_[in.value].fanout_gates.push_back(idx);
    }
    gates_.push_back(GateDef{std::move(id), kind, std::move(inputs), output, std::max(0.0, delay_s)});
}

void DigitalEngine::add_dff(
    std::string id,
    NetId clk,
    NetId d,
    NetId q,
    std::optional<NetId> q_bar,
    double clk_to_q_delay_s) {
    const std::size_t idx = dffs_.size();
    nets_[clk.value].fanout_dffs.push_back(idx);
    dffs_.push_back(DffDef{std::move(id), clk, d, q, q_bar, std::max(0.0, clk_to_q_delay_s)});
}

void DigitalEngine::add_clock(
    std::string id,
    NetId net,
    double period_s,
    double first_edge_s,
    Logic4 initial_value) {
    clocks_.push_back(ClockDef{std::move(id), net, period_s, first_edge_s, initial_value});
}

void DigitalEngine::add_callback(
    std::string id,
    std::vector<NetId> sensitive_nets,
    std::vector<WrealId> sensitive_wreals,
    Callback fn) {
    const std::size_t idx = callbacks_.size();
    for (NetId n : sensitive_nets) nets_[n.value].fanout_callbacks.push_back(idx);
    for (WrealId w : sensitive_wreals) wreals_[w.value].fanout_callbacks.push_back(idx);
    callbacks_.push_back(CallbackDef{std::move(id), std::move(fn)});
}

void DigitalEngine::schedule_net(NetId net, Logic4 value, double at_time_s) {
    const double t = std::max(current_time_s_, at_time_s);
    const std::uint32_t delta = (t <= current_time_s_) ? (current_delta_ + 1u) : 0u;
    queue_.push(Event{t, delta, next_seq_++, false, net.value, value, 0.0});
}

void DigitalEngine::schedule_wreal(WrealId net, double value, double at_time_s) {
    const double t = std::max(current_time_s_, at_time_s);
    const std::uint32_t delta = (t <= current_time_s_) ? (current_delta_ + 1u) : 0u;
    queue_.push(Event{t, delta, next_seq_++, true, net.value, Logic4::X, value});
}

void DigitalEngine::evaluate_gate(std::size_t gate_idx, double now_s) {
    const auto& g = gates_[gate_idx];
    if (g.inputs.empty()) return;
    Logic4 out = nets_[g.inputs[0].value].value;
    switch (g.kind) {
        case GateKind::Buf:
            break;
        case GateKind::Not:
            out = logic_not(out);
            break;
        case GateKind::And:
            for (std::size_t i = 1u; i < g.inputs.size(); ++i) out = logic_and(out, nets_[g.inputs[i].value].value);
            break;
        case GateKind::Or:
            for (std::size_t i = 1u; i < g.inputs.size(); ++i) out = logic_or(out, nets_[g.inputs[i].value].value);
            break;
        case GateKind::Nand:
            for (std::size_t i = 1u; i < g.inputs.size(); ++i) out = logic_and(out, nets_[g.inputs[i].value].value);
            out = logic_not(out);
            break;
        case GateKind::Nor:
            for (std::size_t i = 1u; i < g.inputs.size(); ++i) out = logic_or(out, nets_[g.inputs[i].value].value);
            out = logic_not(out);
            break;
        case GateKind::Xor:
            for (std::size_t i = 1u; i < g.inputs.size(); ++i) out = logic_xor(out, nets_[g.inputs[i].value].value);
            break;
    }
    if (out != nets_[g.output.value].value || g.delay_s > 0.0) {
        schedule_net(g.output, out, now_s + g.delay_s);
    }
}

void DigitalEngine::initialize(double t_stop_s) {
    if (initialized_) return;
    initialized_ = true;
    current_time_s_ = 0.0;
    current_delta_ = 0;

    /* Pre-schedule periodic clock toggles over [0, t_stop_s]. */
    for (const auto& clk : clocks_) {
        if (clk.period_s <= 0.0) continue;
        const double half = 0.5 * clk.period_s;
        nets_[clk.net.value].value = clk.initial_value;
        nets_[clk.net.value].history.back().value = clk.initial_value;
        Logic4 cur = clk.initial_value;
        double t = (clk.first_edge_s > 0.0) ? clk.first_edge_s : half;
        while (t <= t_stop_s + 1.0e-15) {
            cur = (cur == Logic4::L0) ? Logic4::L1 : Logic4::L0;
            schedule_net(clk.net, cur, t);
            t += half;
        }
    }

    /* Evaluate all combinational gates once at t = 0. */
    for (std::size_t i = 0; i < gates_.size(); ++i) {
        evaluate_gate(i, 0.0);
    }
    advance_to(0.0);
}

std::optional<double> DigitalEngine::next_event_time() const noexcept {
    if (queue_.empty()) return std::nullopt;
    return queue_.top().time_s;
}

std::size_t DigitalEngine::advance_to(double target_time_s) {
    std::size_t processed = 0;
    constexpr std::size_t kMaxDeltaPerTimestamp = 10'000u;
    std::size_t delta_guard = 0;

    while (!queue_.empty() && queue_.top().time_s <= target_time_s + 1.0e-15) {
        const Event ev = queue_.top();
        queue_.pop();

        if (ev.time_s > current_time_s_) {
            current_time_s_ = ev.time_s;
            current_delta_ = ev.delta;
            delta_guard = 0;
        } else {
            current_delta_ = std::max(current_delta_, ev.delta);
            if (++delta_guard > kMaxDeltaPerTimestamp) break;
        }
        ++processed;
        ++events_processed_;

        if (!ev.is_wreal) {
            auto& net = nets_[ev.target_id];
            const Logic4 prev = net.value;
            if (prev == ev.logic_val) continue;
            net.value = ev.logic_val;
            if (!net.history.empty() && std::abs(net.history.back().time_s - current_time_s_) <= 1.0e-15) {
                net.history.back().value = ev.logic_val;
            } else {
                net.history.push_back(DigitalTransition{current_time_s_, ev.logic_val});
            }

            for (std::size_t g_idx : net.fanout_gates) {
                evaluate_gate(g_idx, current_time_s_);
            }
            if (prev == Logic4::L0 && ev.logic_val == Logic4::L1) {
                for (std::size_t d_idx : net.fanout_dffs) {
                    const auto& dff = dffs_[d_idx];
                    const Logic4 d_val = nets_[dff.d.value].value;
                    schedule_net(dff.q, d_val, current_time_s_ + dff.clk_to_q_delay_s);
                    if (dff.q_bar.has_value()) {
                        schedule_net(*dff.q_bar, logic_not(d_val), current_time_s_ + dff.clk_to_q_delay_s);
                    }
                }
            }
            for (std::size_t cb_idx : net.fanout_callbacks) {
                callbacks_[cb_idx].fn(*this, current_time_s_);
            }
        } else {
            auto& w = wreals_[ev.target_id];
            if (w.value == ev.real_val) continue;
            w.value = ev.real_val;
            if (!w.history.empty() && std::abs(w.history.back().time_s - current_time_s_) <= 1.0e-15) {
                w.history.back().value = ev.real_val;
            } else {
                w.history.push_back(WrealTransition{current_time_s_, ev.real_val});
            }
            for (std::size_t cb_idx : w.fanout_callbacks) {
                callbacks_[cb_idx].fn(*this, current_time_s_);
            }
        }
    }

    if (target_time_s > current_time_s_) {
        current_time_s_ = target_time_s;
        current_delta_ = 0;
    }
    return processed;
}

/* ==================== Synchronized Mixed-Signal Co-Simulation Loop ==================== */

namespace {

double node_v(const std::vector<double>& x, core::NodeId node) noexcept {
    if (auto u = devices::CompiledPlan::node_unknown(node)) return x[u->value];
    return 0.0;
}

struct D2ARampState {
    double v_start{0.0};
    double v_target{0.0};
    double t_start{0.0};
    double t_end{0.0};

    [[nodiscard]] double voltage_at(double t) const noexcept {
        if (t <= t_start || t_end <= t_start) return v_target;
        if (t >= t_end) return v_target;
        const double frac = (t - t_start) / (t_end - t_start);
        return v_start + frac * (v_target - v_start);
    }

    void retarget(double t_now, double new_target, double t_rise, double t_fall) noexcept {
        const double v_now = voltage_at(t_now);
        v_start = v_now;
        v_target = new_target;
        t_start = t_now;
        const double ramp = (new_target >= v_now) ? t_rise : t_fall;
        t_end = t_now + std::max(0.0, ramp);
    }
};

double d2a_target_voltage(const D2AConnect& link, const DigitalEngine& digital) noexcept {
    if (link.wreal_net.has_value()) {
        return digital.wreal_value(*link.wreal_net);
    }
    if (link.digital_net.has_value()) {
        const Logic4 v = digital.net_value(*link.digital_net);
        if (v == Logic4::L1) return link.v_high;
        if (v == Logic4::L0) return link.v_low;
        return link.v_x;
    }
    return link.v_low;
}

struct SourceOverrideGuard {
    devices::CompiledPlan& plan;
    const std::vector<D2AConnect>& links;
    ~SourceOverrideGuard() {
        for (const auto& link : links) {
            (void)plan.set_dc_source_override(link.analog_source_id, std::nullopt);
            (void)plan.set_transient_source_override(link.analog_source_id, std::nullopt);
        }
    }
};

}  // namespace

Diagnostic run_mixed_transient(
    devices::CompiledPlan& plan,
    DigitalEngine& digital,
    const std::vector<A2DConnect>& a2d_links,
    const std::vector<D2AConnect>& d2a_links,
    const MixedTransientOptions& opts,
    MixedTransientResult& out_result) {
    if (!std::isfinite(opts.t_stop_s) || opts.t_stop_s <= 0.0 ||
        !std::isfinite(opts.max_step_s) || opts.max_step_s <= 0.0) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "mixed-signal transient requires positive t_stop_s and max_step_s");
    }

    for (const auto& a2d : a2d_links) {
        if (a2d.analog_p.value >= plan.circuit.nodes.size() || a2d.analog_n.value >= plan.circuit.nodes.size() ||
            a2d.digital_net.value >= digital.net_count() || a2d.v_high < a2d.v_low) {
            return Diagnostic::error(StatusCode::InvalidArgument, "invalid A2DConnect configuration: " + a2d.id);
        }
    }
    for (const auto& d2a : d2a_links) {
        const bool has_dig = d2a.digital_net.has_value();
        const bool has_wr = d2a.wreal_net.has_value();
        if (has_dig == has_wr) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "D2AConnect must bind exactly one of digital_net or wreal_net: " + d2a.id);
        }
        if ((has_dig && d2a.digital_net->value >= digital.net_count()) ||
            (has_wr && d2a.wreal_net->value >= digital.wreal_count())) {
            return Diagnostic::error(StatusCode::InvalidArgument, "D2AConnect net id out of range: " + d2a.id);
        }
        if (plan.source_device_indices.find(d2a.analog_source_id) == plan.source_device_indices.end()) {
            return Diagnostic::error(
                StatusCode::InvalidArgument,
                "D2AConnect analog_source_id not found in CompiledPlan: " + d2a.analog_source_id);
        }
    }

    SourceOverrideGuard guard{plan, d2a_links};
    plan.reset_transient_history();
    digital.initialize(opts.t_stop_s);

    /* Initialize D2A ramps from t = 0 digital/wreal values and seed initial DC OP. */
    std::vector<D2ARampState> d2a_states(d2a_links.size());
    for (std::size_t i = 0; i < d2a_links.size(); ++i) {
        const double v0 = d2a_target_voltage(d2a_links[i], digital);
        d2a_states[i] = D2ARampState{v0, v0, 0.0, 0.0};
        if (auto d = plan.set_dc_source_override(d2a_links[i].analog_source_id, v0); !d.ok()) return d;
        if (auto d = plan.set_transient_source_override(d2a_links[i].analog_source_id, v0); !d.ok()) return d;
    }

    const std::size_t n = plan.unknown_count();
    const std::size_t nnz = plan.pattern.nnz();
    std::unique_ptr<linalg::RealLinearSolver> lin_solver;
    if (auto d = linalg::create_real_solver(opts.common.backend, lin_solver); !d.ok()) return d;
    if (auto d = lin_solver->analyze(plan.pattern); !d.ok()) return d;

    std::vector<double> x_prev(n, 0.0);
    {
        analysis::AnalysisCommonOptions op_opts = opts.common;
        op_opts.enable_audits = false;
        analysis::OpResult op{};
        if (auto d = analysis::run_op(plan, op_opts, op); !d.ok()) return d;
        x_prev = std::move(op.values);
    }
    for (const auto& link : d2a_links) {
        (void)plan.set_dc_source_override(link.analog_source_id, std::nullopt);
    }

    /* Sync initial A2D logic states from analog x(0). */
    std::vector<Logic4> a2d_last(a2d_links.size(), Logic4::X);
    for (std::size_t i = 0; i < a2d_links.size(); ++i) {
        const double v0 = node_v(x_prev, a2d_links[i].analog_p) - node_v(x_prev, a2d_links[i].analog_n);
        Logic4 init_l = digital.net_value(a2d_links[i].digital_net);
        if (v0 >= a2d_links[i].v_high) {
            init_l = Logic4::L1;
        } else if (v0 <= a2d_links[i].v_low) {
            init_l = Logic4::L0;
        } else if (init_l == Logic4::X || init_l == Logic4::Z) {
            init_l = Logic4::L0;
        }
        a2d_last[i] = init_l;
        if (init_l != digital.net_value(a2d_links[i].digital_net)) {
            digital.schedule_net(a2d_links[i].digital_net, init_l, 0.0);
        }
    }
    digital.advance_to(0.0);

    std::vector<double> f_prev(n, 0.0), q_prev(n, 0.0), g_tmp(nnz, 0.0), c_tmp(nnz, 0.0);
    std::vector<double> f_curr(n, 0.0), q_curr(n, 0.0), history(n, 0.0), x_curr(n, 0.0);
    {
        EvalContext ctx0{0.0, 300.15, AnalysisMode::Transient, x_prev};
        StampTarget tgt0{f_prev, q_prev, g_tmp, c_tmp};
        if (auto d = plan.evaluate_all(ctx0, tgt0); !d.ok()) return d;
        plan.commit_all_trials();
    }

    const std::vector<double> analog_bps = plan.collect_breakpoints(0.0, opts.t_stop_s);
    std::size_t bp_idx = 0;
    const bool use_trap = (opts.method == analysis::IntegrationMethod::Trapezoidal);

    MixedTransientResult res{};
    res.analog.backend_name = lin_solver->name();
    res.analog.samples.push_back(analysis::TransientSample{0.0, x_prev});

    double t_curr = 0.0;
    const double t_eps = 1.0e-13 * std::max(1.0, opts.t_stop_s);
    constexpr std::size_t kMaxSteps = 2'000'000u;

    while (t_curr + t_eps < opts.t_stop_s && res.analog_steps < kMaxSteps) {
        double t_next = std::min(t_curr + opts.max_step_s, opts.t_stop_s);

        /* Land cleanly on next analog breakpoint, next digital event, and next D2A ramp corner. */
        while (bp_idx < analog_bps.size() && analog_bps[bp_idx] <= t_curr + t_eps) ++bp_idx;
        if (bp_idx < analog_bps.size() && analog_bps[bp_idx] < t_next) {
            t_next = analog_bps[bp_idx];
        }
        if (auto next_ev = digital.next_event_time();
            next_ev.has_value() && *next_ev > t_curr + t_eps && *next_ev < t_next) {
            t_next = *next_ev;
        }
        for (const auto& st : d2a_states) {
            if (st.t_end > t_curr + t_eps && st.t_end < t_next) {
                t_next = st.t_end;
            }
        }

        double h = t_next - t_curr;
        bool step_ok = false;
        for (int retry = 0; retry < 12; ++retry) {
            t_next = t_curr + h;
            for (std::size_t i = 0; i < d2a_links.size(); ++i) {
                const double v_drv = d2a_states[i].voltage_at(t_next);
                if (auto d = plan.set_transient_source_override(d2a_links[i].analog_source_id, v_drv); !d.ok()) {
                    return d;
                }
            }

            const double alpha = use_trap ? (2.0 / h) : (1.0 / h);
            if (use_trap) {
                for (std::size_t i = 0; i < n; ++i) history[i] = f_prev[i] - alpha * q_prev[i];
            } else {
                for (std::size_t i = 0; i < n; ++i) history[i] = -alpha * q_prev[i];
            }

            x_curr = x_prev;
            solver::NewtonProblem prob{};
            prob.time_s = t_next;
            prob.mode = AnalysisMode::Transient;
            prob.alpha = alpha;
            prob.history = history;

            solver::NewtonReport nrep{};
            if (solver::solve_newton(plan, *lin_solver, prob, opts.common.newton_options, x_curr, nrep).ok()) {
                EvalContext ctx_c{t_next, 300.15, AnalysisMode::Transient, x_curr};
                StampTarget tgt_c{f_curr, q_curr, g_tmp, c_tmp};
                if (plan.evaluate_all(ctx_c, tgt_c).ok()) {
                    plan.commit_all_trials();
                    step_ok = true;
                    break;
                }
            }
            plan.rollback_all_trials();
            h *= 0.5;
        }
        if (!step_ok) {
            return Diagnostic::error(StatusCode::NumericalFailure, "mixed-signal analog step failed to converge");
        }

        /* Inspect A2D connect modules for threshold crossings across [t_curr, t_next]. */
        for (std::size_t i = 0; i < a2d_links.size(); ++i) {
            const auto& a2d = a2d_links[i];
            const double v0 = node_v(x_prev, a2d.analog_p) - node_v(x_prev, a2d.analog_n);
            const double v1 = node_v(x_curr, a2d.analog_p) - node_v(x_curr, a2d.analog_n);

            if (a2d_last[i] != Logic4::L1 && v1 >= a2d.v_high) {
                double frac = 1.0;
                if (v1 > v0 && v0 < a2d.v_high) {
                    frac = std::clamp((a2d.v_high - v0) / (v1 - v0), 0.0, 1.0);
                }
                const double t_cross = t_curr + frac * (t_next - t_curr);
                a2d_last[i] = Logic4::L1;
                digital.schedule_net(a2d.digital_net, Logic4::L1, t_cross + a2d.delay_s);
                ++res.a2d_crossings;
            } else if (a2d_last[i] != Logic4::L0 && v1 <= a2d.v_low) {
                double frac = 1.0;
                if (v1 < v0 && v0 > a2d.v_low) {
                    frac = std::clamp((a2d.v_low - v0) / (v1 - v0), 0.0, 1.0);
                }
                const double t_cross = t_curr + frac * (t_next - t_curr);
                a2d_last[i] = Logic4::L0;
                digital.schedule_net(a2d.digital_net, Logic4::L0, t_cross + a2d.delay_s);
                ++res.a2d_crossings;
            }
        }

        /* Advance discrete-event digital kernel to t_next and retarget any changed D2A ramps. */
        digital.advance_to(t_next);
        for (std::size_t i = 0; i < d2a_links.size(); ++i) {
            const double new_target = d2a_target_voltage(d2a_links[i], digital);
            if (std::abs(new_target - d2a_states[i].v_target) > 1.0e-15) {
                d2a_states[i].retarget(t_next, new_target, d2a_links[i].t_rise_s, d2a_links[i].t_fall_s);
                ++res.d2a_transitions;
            }
        }

        t_curr = t_next;
        ++res.analog_steps;
        res.analog.samples.push_back(analysis::TransientSample{t_curr, x_curr});
        x_prev.swap(x_curr);
        f_prev.swap(f_curr);
        q_prev.swap(q_curr);
    }

    res.digital_events = digital.total_events_processed();
    out_result = std::move(res);
    return Diagnostic::success();
}

/* ==================== Unified IEEE 1364 VCD Formatter ==================== */

std::string format_mixed_vcd(
    const devices::CompiledPlan& plan,
    const DigitalEngine& digital,
    const MixedTransientResult& result,
    double timescale_s) {
    auto vcd_id = [](std::size_t idx) -> std::string {
        std::string s;
        do {
            s.push_back(static_cast<char>('!' + (idx % 94u)));
            idx /= 94u;
        } while (idx > 0u);
        return s;
    };

    const double ts = (std::isfinite(timescale_s) && timescale_s > 0.0) ? timescale_s : 1.0e-12;
    std::ostringstream out;
    out << "$version Spice V2 Mixed-Signal Co-Simulation $end\n";
    out << "$timescale 1ps $end\n";
    out << "$scope module top $end\n";

    std::size_t sym_idx = 0;
    const std::size_t n_analog = plan.signals.size();
    std::vector<std::string> analog_syms(n_analog);
    for (std::size_t i = 0; i < n_analog; ++i) {
        analog_syms[i] = vcd_id(sym_idx++);
        out << "$var real 64 " << analog_syms[i] << " " << plan.signals[i].id << " $end\n";
    }
    const std::size_t n_wreal = digital.wreal_count();
    std::vector<std::string> wreal_syms(n_wreal);
    for (std::size_t i = 0; i < n_wreal; ++i) {
        wreal_syms[i] = vcd_id(sym_idx++);
        out << "$var real 64 " << wreal_syms[i] << " " << digital.wreal_name(WrealId{static_cast<std::uint32_t>(i)})
            << " $end\n";
    }
    const std::size_t n_dig = digital.net_count();
    std::vector<std::string> dig_syms(n_dig);
    for (std::size_t i = 0; i < n_dig; ++i) {
        dig_syms[i] = vcd_id(sym_idx++);
        out << "$var wire 1 " << dig_syms[i] << " " << digital.net_name(NetId{static_cast<std::uint32_t>(i)})
            << " $end\n";
    }
    out << "$upscope $end\n";
    out << "$enddefinitions $end\n";

    struct VcdEvent {
        std::uint64_t tick{0};
        std::uint8_t kind{0}; /* 0=analog sample, 1=wreal, 2=digital */
        std::size_t idx{0};
        std::size_t sub_idx{0};
    };
    std::vector<VcdEvent> timeline;
    for (std::size_t i = 0; i < result.analog.samples.size(); ++i) {
        const auto tick = static_cast<std::uint64_t>(std::llround(std::max(0.0, result.analog.samples[i].time_s) / ts));
        timeline.push_back(VcdEvent{tick, 0, i, 0});
    }
    for (std::size_t w = 0; w < n_wreal; ++w) {
        const auto& h = digital.wreal_history(WrealId{static_cast<std::uint32_t>(w)});
        for (std::size_t j = 0; j < h.size(); ++j) {
            const auto tick = static_cast<std::uint64_t>(std::llround(std::max(0.0, h[j].time_s) / ts));
            timeline.push_back(VcdEvent{tick, 1, w, j});
        }
    }
    for (std::size_t d = 0; d < n_dig; ++d) {
        const auto& h = digital.net_history(NetId{static_cast<std::uint32_t>(d)});
        for (std::size_t j = 0; j < h.size(); ++j) {
            const auto tick = static_cast<std::uint64_t>(std::llround(std::max(0.0, h[j].time_s) / ts));
            timeline.push_back(VcdEvent{tick, 2, d, j});
        }
    }

    std::stable_sort(timeline.begin(), timeline.end(), [](const VcdEvent& a, const VcdEvent& b) {
        return a.tick < b.tick;
    });

    out << std::setprecision(12);
    std::optional<std::uint64_t> last_tick;
    for (const auto& ev : timeline) {
        if (!last_tick.has_value() || *last_tick != ev.tick) {
            out << "#" << ev.tick << "\n";
            last_tick = ev.tick;
        }
        if (ev.kind == 0) {
            const auto& vals = result.analog.samples[ev.idx].values;
            for (std::size_t i = 0; i < std::min(n_analog, vals.size()); ++i) {
                out << "r" << vals[i] << " " << analog_syms[i] << "\n";
            }
        } else if (ev.kind == 1) {
            const auto& h = digital.wreal_history(WrealId{static_cast<std::uint32_t>(ev.idx)});
            out << "r" << h[ev.sub_idx].value << " " << wreal_syms[ev.idx] << "\n";
        } else {
            const auto& h = digital.net_history(NetId{static_cast<std::uint32_t>(ev.idx)});
            out << logic4_char(h[ev.sub_idx].value) << dig_syms[ev.idx] << "\n";
        }
    }

    return out.str();
}

}  // namespace spice::mixed
