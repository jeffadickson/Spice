#pragma once

#include "analysis/analyses.hpp"

#include <cstddef>
#include <cstdint>
#include <functional>
#include <optional>
#include <queue>
#include <string>
#include <utility>
#include <vector>

namespace spice::mixed {

/*
 * M4a Mixed-Signal Event-Driven Co-Simulation Kernel (ARCH-MS-001, PLAN-V2-009):
 *   - Strictly decoupled from the analog DAE Newton hot loop: digital logic runs in its own
 *     deterministic discrete-event scheduler over 4-state Logic4 and Wreal nets.
 *   - Connects to any CompiledPlan analog DAE via automatic A2DConnect (hysteresis + interpolated
 *     crossing time) and D2AConnect (finite-ramp Thevenin driver via transient source overrides).
 *   - Emits unified IEEE 1364 VCD combining analog real signals, Wreal nets, and Logic4 nets.
 */

enum class Logic4 : std::uint8_t {
    L0 = 0,
    L1 = 1,
    X = 2,
    Z = 3,
};

[[nodiscard]] char logic4_char(Logic4 v) noexcept;
[[nodiscard]] Logic4 logic_not(Logic4 a) noexcept;
[[nodiscard]] Logic4 logic_and(Logic4 a, Logic4 b) noexcept;
[[nodiscard]] Logic4 logic_or(Logic4 a, Logic4 b) noexcept;
[[nodiscard]] Logic4 logic_nand(Logic4 a, Logic4 b) noexcept;
[[nodiscard]] Logic4 logic_nor(Logic4 a, Logic4 b) noexcept;
[[nodiscard]] Logic4 logic_xor(Logic4 a, Logic4 b) noexcept;

struct NetId {
    std::uint32_t value{0};
    bool operator==(const NetId&) const noexcept = default;
};

struct WrealId {
    std::uint32_t value{0};
    bool operator==(const WrealId&) const noexcept = default;
};

enum class GateKind : std::uint8_t {
    Buf = 0,
    Not = 1,
    And = 2,
    Or = 3,
    Nand = 4,
    Nor = 5,
    Xor = 6,
};

struct DigitalTransition {
    double time_s{0.0};
    Logic4 value{Logic4::X};
};

struct WrealTransition {
    double time_s{0.0};
    double value{0.0};
};

class DigitalEngine {
public:
    using Callback = std::function<void(DigitalEngine&, double now_s)>;

    NetId add_net(std::string name, Logic4 initial_value = Logic4::X);
    WrealId add_wreal(std::string name, double initial_value = 0.0);

    void add_gate(
        std::string id,
        GateKind kind,
        std::vector<NetId> inputs,
        NetId output,
        double delay_s = 0.0);

    void add_dff(
        std::string id,
        NetId clk,
        NetId d,
        NetId q,
        std::optional<NetId> q_bar = std::nullopt,
        double clk_to_q_delay_s = 0.0);

    void add_clock(
        std::string id,
        NetId net,
        double period_s,
        double first_edge_s = 0.0,
        Logic4 initial_value = Logic4::L0);

    /* Register a deterministic behavioral callback sensitive to one or more digital/wreal nets. */
    void add_callback(
        std::string id,
        std::vector<NetId> sensitive_nets,
        std::vector<WrealId> sensitive_wreals,
        Callback fn);

    /* Schedule a future (or zero-delay next-delta) transition on a Logic4 or Wreal net. */
    void schedule_net(NetId net, Logic4 value, double at_time_s);
    void schedule_wreal(WrealId net, double value, double at_time_s);

    /* Initialize clocks and evaluate t = 0 combinational propagation. */
    void initialize(double t_stop_s);

    /* Process all scheduled events with timestamp <= target_time_s in deterministic (time, delta, seq) order. */
    std::size_t advance_to(double target_time_s);

    /* Earliest pending event timestamp, if any. */
    [[nodiscard]] std::optional<double> next_event_time() const noexcept;

    [[nodiscard]] Logic4 net_value(NetId net) const noexcept { return nets_[net.value].value; }
    [[nodiscard]] double wreal_value(WrealId net) const noexcept { return wreals_[net.value].value; }
    [[nodiscard]] const std::string& net_name(NetId net) const noexcept { return nets_[net.value].name; }
    [[nodiscard]] const std::string& wreal_name(WrealId net) const noexcept { return wreals_[net.value].name; }
    [[nodiscard]] std::size_t net_count() const noexcept { return nets_.size(); }
    [[nodiscard]] std::size_t wreal_count() const noexcept { return wreals_.size(); }
    [[nodiscard]] const std::vector<DigitalTransition>& net_history(NetId net) const noexcept {
        return nets_[net.value].history;
    }
    [[nodiscard]] const std::vector<WrealTransition>& wreal_history(WrealId net) const noexcept {
        return wreals_[net.value].history;
    }
    [[nodiscard]] double current_time() const noexcept { return current_time_s_; }
    [[nodiscard]] std::size_t total_events_processed() const noexcept { return events_processed_; }

private:
    struct NetState {
        std::string name;
        Logic4 value{Logic4::X};
        std::vector<DigitalTransition> history;
        std::vector<std::size_t> fanout_gates;
        std::vector<std::size_t> fanout_dffs;
        std::vector<std::size_t> fanout_callbacks;
    };

    struct WrealState {
        std::string name;
        double value{0.0};
        std::vector<WrealTransition> history;
        std::vector<std::size_t> fanout_callbacks;
    };

    struct GateDef {
        std::string id;
        GateKind kind{GateKind::Buf};
        std::vector<NetId> inputs;
        NetId output{0};
        double delay_s{0.0};
    };

    struct DffDef {
        std::string id;
        NetId clk{0};
        NetId d{0};
        NetId q{0};
        std::optional<NetId> q_bar;
        double clk_to_q_delay_s{0.0};
    };

    struct ClockDef {
        std::string id;
        NetId net{0};
        double period_s{1.0e-6};
        double first_edge_s{0.0};
        Logic4 initial_value{Logic4::L0};
    };

    struct CallbackDef {
        std::string id;
        Callback fn;
    };

    struct Event {
        double time_s{0.0};
        std::uint32_t delta{0};
        std::uint64_t seq{0};
        bool is_wreal{false};
        std::uint32_t target_id{0};
        Logic4 logic_val{Logic4::X};
        double real_val{0.0};

        bool operator>(const Event& o) const noexcept {
            if (time_s != o.time_s) return time_s > o.time_s;
            if (delta != o.delta) return delta > o.delta;
            return seq > o.seq;
        }
    };

    void evaluate_gate(std::size_t gate_idx, double now_s);

    std::vector<NetState> nets_;
    std::vector<WrealState> wreals_;
    std::vector<GateDef> gates_;
    std::vector<DffDef> dffs_;
    std::vector<ClockDef> clocks_;
    std::vector<CallbackDef> callbacks_;
    std::priority_queue<Event, std::vector<Event>, std::greater<Event>> queue_;

    double current_time_s_{0.0};
    std::uint32_t current_delta_{0};
    std::uint64_t next_seq_{1};
    std::size_t events_processed_{0};
    bool initialized_{false};
};

/* Automatic Analog-to-Digital Connect Module (hysteresis comparator + interpolated crossing time). */
struct A2DConnect {
    std::string id;
    core::NodeId analog_p{0};
    core::NodeId analog_n{0};
    NetId digital_net{0};
    double v_low{0.8};
    double v_high{2.0};
    double delay_s{0.0};
};

/* Automatic Digital-to-Analog Connect Module (finite-ramp Thevenin driver via analog independent source). */
struct D2AConnect {
    std::string id;
    std::optional<NetId> digital_net; /* Set for Logic4 driver */
    std::optional<WrealId> wreal_net; /* Set for Wreal (e.g., DAC) driver */
    std::string analog_source_id;     /* Independent VoltageSourceSpec id in CompiledPlan */
    double v_low{0.0};
    double v_high{3.3};
    double v_x{1.65};
    double t_rise_s{1.0e-9};
    double t_fall_s{1.0e-9};
};

struct MixedTransientOptions {
    double t_stop_s{0.0};
    double max_step_s{0.0};
    analysis::IntegrationMethod method{analysis::IntegrationMethod::BackwardEuler};
    analysis::AnalysisCommonOptions common{};
};

struct MixedTransientResult {
    analysis::TransientResult analog;
    std::size_t analog_steps{0};
    std::size_t digital_events{0};
    std::size_t a2d_crossings{0};
    std::size_t d2a_transitions{0};
};

/* Synchronized analog-digital co-simulation transient loop. */
[[nodiscard]] core::Diagnostic run_mixed_transient(
    devices::CompiledPlan& plan,
    DigitalEngine& digital,
    const std::vector<A2DConnect>& a2d_links,
    const std::vector<D2AConnect>& d2a_links,
    const MixedTransientOptions& opts,
    MixedTransientResult& out_result);

/* Unified IEEE 1364 VCD output combining analog real signals, Wreal nets, and 4-state Logic4 nets. */
[[nodiscard]] std::string format_mixed_vcd(
    const devices::CompiledPlan& plan,
    const DigitalEngine& digital,
    const MixedTransientResult& result,
    double timescale_s = 1.0e-12);

}  // namespace spice::mixed
