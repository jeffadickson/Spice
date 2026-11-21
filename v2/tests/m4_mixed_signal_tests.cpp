#include "core/circuit.hpp"
#include "devices/linear_devices.hpp"
#include "mixed/mixed_signal.hpp"

#include <cmath>
#include <iostream>
#include <string>
#include <vector>

#if defined(SPICE_V2_USE_CATCH2)
#  include <catch2/catch_test_macros.hpp>
#else
namespace {
int g_failed_checks = 0;
std::vector<std::pair<std::string, void (*)()>>& test_registry() {
    static std::vector<std::pair<std::string, void (*)()>> tests;
    return tests;
}
struct TestRegistrar {
    TestRegistrar(const char* name, void (*fn)()) { test_registry().emplace_back(name, fn); }
};
}  // namespace

#  define SPICE_CAT_IMPL(a, b) a##b
#  define SPICE_CAT(a, b) SPICE_CAT_IMPL(a, b)
#  define TEST_CASE(name, tags)                                                         \
      static void SPICE_CAT(spice_m4_fn_, __LINE__)();                                  \
      static const TestRegistrar SPICE_CAT(spice_m4_reg_, __LINE__)(                    \
          name, &SPICE_CAT(spice_m4_fn_, __LINE__));                                    \
      static void SPICE_CAT(spice_m4_fn_, __LINE__)()
#  define REQUIRE(expr)                                                                 \
      do {                                                                              \
          if (!(expr)) {                                                                \
              std::cerr << "FAIL " << __FILE__ << ":" << __LINE__ << " REQUIRE("        \
                        << #expr << ")\n";                                              \
              ++g_failed_checks;                                                        \
              return;                                                                   \
          }                                                                             \
      } while (false)
#endif

namespace {

using namespace spice;
using namespace spice::mixed;

std::size_t find_signal(const devices::CompiledPlan& plan, const std::string& id) {
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (plan.signals[i].id == id) return i;
    }
    return plan.signals.size();
}

}  // namespace

TEST_CASE("IEEE 1364 4-state Logic4 truth tables, combinational gates, and rising-edge DFF", "[m4][digital]") {
    REQUIRE(logic4_char(Logic4::L0) == '0');
    REQUIRE(logic4_char(Logic4::L1) == '1');
    REQUIRE(logic4_char(Logic4::X) == 'x');
    REQUIRE(logic4_char(Logic4::Z) == 'z');

    // Controlling-value semantics: 0 & X = 0, 1 | X = 1, 1 & X = X, 0 | X = X.
    REQUIRE(logic_and(Logic4::L0, Logic4::X) == Logic4::L0);
    REQUIRE(logic_and(Logic4::L1, Logic4::X) == Logic4::X);
    REQUIRE(logic_or(Logic4::L1, Logic4::Z) == Logic4::L1);
    REQUIRE(logic_or(Logic4::L0, Logic4::Z) == Logic4::X);
    REQUIRE(logic_nand(Logic4::L1, Logic4::L1) == Logic4::L0);
    REQUIRE(logic_nor(Logic4::L0, Logic4::L0) == Logic4::L1);
    REQUIRE(logic_xor(Logic4::L1, Logic4::L0) == Logic4::L1);
    REQUIRE(logic_xor(Logic4::L1, Logic4::L1) == Logic4::L0);
    REQUIRE(logic_not(Logic4::Z) == Logic4::X);

    // 2-stage toggle DFF divider (q_bar -> d) clocked at 1 MHz.
    DigitalEngine dig;
    const auto clk = dig.add_net("clk", Logic4::L0);
    const auto q = dig.add_net("q", Logic4::L0);
    const auto q_bar = dig.add_net("q_bar", Logic4::L1);
    dig.add_clock("CLK1", clk, 1.0e-6, 0.5e-6, Logic4::L0);
    dig.add_dff("U_DFF", clk, q_bar, q, q_bar, 5.0e-9);

    dig.initialize(3.0e-6);
    dig.advance_to(0.6e-6); /* After 1st rising edge at 0.5 us */
    REQUIRE(dig.net_value(q) == Logic4::L1);
    REQUIRE(dig.net_value(q_bar) == Logic4::L0);

    dig.advance_to(1.6e-6); /* After 2nd rising edge at 1.5 us */
    REQUIRE(dig.net_value(q) == Logic4::L0);
    REQUIRE(dig.net_value(q_bar) == Logic4::L1);

    dig.advance_to(2.6e-6); /* After 3rd rising edge at 2.5 us */
    REQUIRE(dig.net_value(q) == Logic4::L1);
}

TEST_CASE("Mixed-signal Schmitt-trigger RC relaxation oscillator co-simulation and unified VCD export", "[m4][mixed][vcd]") {
    const core::NodeId gnd{0};

    // Analog RC network: Vdrv (driven by D2A) -> R1 (1k) -> cap (1 nF to gnd), tau = 1 us.
    core::Circuit c;
    c.name = "mixed_relaxation_osc";
    const auto n_drv = c.get_or_add_node("drv");
    const auto n_cap = c.get_or_add_node("cap");
    c.elements.push_back(core::VoltageSourceSpec{"Vdrv", n_drv, gnd, 0.0, {0.0, 0.0}, std::nullopt});
    c.elements.push_back(core::ResistorSpec{"R1", n_drv, n_cap, 1000.0});
    c.elements.push_back(core::CapacitorSpec{"C1", n_cap, gnd, 1.0e-9, std::nullopt});

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(c, plan).ok());

    // Digital feedback loop: A2D Schmitt trigger (0.8V..2.5V) -> NOT gate (10 ns) -> D2A (0V..3.3V, 5 ns ramp).
    DigitalEngine dig;
    const auto n_cmp = dig.add_net("cmp", Logic4::L0);
    const auto n_out = dig.add_net("drv_dig", Logic4::L1);
    dig.add_gate("U_INV", GateKind::Not, {n_cmp}, n_out, 10.0e-9);

    const std::vector<A2DConnect> a2d = {
        A2DConnect{"A2D_SCHMITT", n_cap, gnd, n_cmp, 0.8, 2.5, 0.0},
    };
    const std::vector<D2AConnect> d2a = {
        D2AConnect{"D2A_DRV", n_out, std::nullopt, "Vdrv", 0.0, 3.3, 1.65, 5.0e-9, 5.0e-9},
    };

    MixedTransientOptions opts{};
    opts.t_stop_s = 12.0e-6;
    opts.max_step_s = 50.0e-9;
    opts.method = analysis::IntegrationMethod::Trapezoidal;

    MixedTransientResult res;
    REQUIRE(run_mixed_transient(plan, dig, a2d, d2a, opts, res).ok());

    // Genuine self-oscillating mixed-signal loop: multiple A2D crossings and D2A transitions!
    REQUIRE(res.a2d_crossings >= 6u);
    REQUIRE(res.d2a_transitions >= 6u);
    REQUIRE(res.digital_events >= 12u);

    const std::size_t idx_cap = find_signal(plan, "v(cap)");
    double v_min = 1e9, v_max = -1e9;
    for (const auto& s : res.analog.samples) {
        v_min = std::min(v_min, s.values[idx_cap]);
        v_max = std::max(v_max, s.values[idx_cap]);
    }
    REQUIRE(v_max > 2.45 && v_max < 3.35);
    REQUIRE(v_min < 0.85 && v_min > -0.05);

    // Unified IEEE 1364 VCD output includes both analog real signals and digital wire signals.
    const std::string vcd = format_mixed_vcd(plan, dig, res);
    REQUIRE(vcd.find("$var real 64") != std::string::npos);
    REQUIRE(vcd.find("v(cap)") != std::string::npos);
    REQUIRE(vcd.find("$var wire 1") != std::string::npos);
    REQUIRE(vcd.find("drv_dig") != std::string::npos);
    REQUIRE(vcd.find("$enddefinitions $end") != std::string::npos);
}

TEST_CASE("Full 6-bit mixed-signal SAR ADC recovers exact quantized digital codes across input range", "[m4][mixed][sar_adc]") {
    const core::NodeId gnd{0};
    constexpr double v_ref = 3.2; /* 6-bit LSB = 3.2 / 64 = 0.05 V */
    constexpr int n_bits = 6;

    auto run_sar_conversion = [&](double v_in, int& out_code) -> bool {
        const double v_ref_val = v_ref;
        // Analog front-end: Vin (input), Vdac (Wreal D2A driver) -> Rdac (1k) -> Cdac (10pF, tau = 10 ns).
        core::Circuit c;
        c.name = "sar_adc_6bit";
        const auto n_vin = c.get_or_add_node("vin");
        const auto n_dac_src = c.get_or_add_node("dac_src");
        const auto n_dac = c.get_or_add_node("dac");
        c.elements.push_back(core::VoltageSourceSpec{"Vin", n_vin, gnd, v_in, {0.0, 0.0}, std::nullopt});
        c.elements.push_back(core::VoltageSourceSpec{"Vdac", n_dac_src, gnd, 0.5 * v_ref_val, {0.0, 0.0}, std::nullopt});
        c.elements.push_back(core::ResistorSpec{"Rdac", n_dac_src, n_dac, 1000.0});
        c.elements.push_back(core::CapacitorSpec{"Cdac", n_dac, gnd, 10.0e-12, std::nullopt});

        devices::CompiledPlan plan;
        if (!devices::compile_circuit(c, plan).ok()) return false;

        DigitalEngine dig;
        const auto clk = dig.add_net("clk", Logic4::L0);
        const auto cmp = dig.add_net("cmp", Logic4::L0);
        const auto dac_wreal = dig.add_wreal("dac_wreal", 0.5 * v_ref_val);
        std::vector<NetId> bits;
        for (int b = 0; b < n_bits; ++b) {
            bits.push_back(dig.add_net("bit" + std::to_string(b), Logic4::L0));
        }

        // 1 MHz SAR clock: rising edges at 0.5, 1.5, 2.5, 3.5, 4.5, 5.5 us (500 ns = 50*tau settling per bit).
        dig.add_clock("SAR_CLK", clk, 1.0e-6, 0.5e-6, Logic4::L0);

        struct SarState {
            int current_bit{n_bits - 1};
            int code_acc{0};
            bool done{false};
        };
        auto st = std::make_shared<SarState>();
        dig.add_callback("U_SAR_FSM", {clk}, {}, [st, clk, cmp, dac_wreal, bits, v_ref_val](DigitalEngine& eng, double now_s) {
            if (eng.net_value(clk) != Logic4::L1 || st->done) return;
            const int bit = st->current_bit;
            const bool keep = (eng.net_value(cmp) == Logic4::L1);
            if (keep) {
                st->code_acc |= (1 << bit);
                eng.schedule_net(bits[static_cast<std::size_t>(bit)], Logic4::L1, now_s + 2.0e-9);
            } else {
                eng.schedule_net(bits[static_cast<std::size_t>(bit)], Logic4::L0, now_s + 2.0e-9);
            }
            if (bit == 0) {
                st->done = true;
                const double final_dac = (static_cast<double>(st->code_acc) / 64.0) * v_ref_val;
                eng.schedule_wreal(dac_wreal, final_dac, now_s + 5.0e-9);
            } else {
                st->current_bit = bit - 1;
                const int trial_code = st->code_acc | (1 << st->current_bit);
                const double next_dac = (static_cast<double>(trial_code) / 64.0) * v_ref_val;
                eng.schedule_wreal(dac_wreal, next_dac, now_s + 5.0e-9);
            }
        });

        // Analog comparator A2D: v(vin) - v(dac) >= 0 -> cmp = '1', < 0 -> cmp = '0'.
        const std::vector<A2DConnect> a2d = {
            A2DConnect{"A2D_CMP", n_vin, n_dac, cmp, 0.0, 0.0, 1.0e-9},
        };
        // Wreal D2A DAC driver with 5 ns linear slew ramp.
        const std::vector<D2AConnect> d2a = {
            D2AConnect{"D2A_DAC", std::nullopt, dac_wreal, "Vdac", 0.0, v_ref, 0.5 * v_ref, 5.0e-9, 5.0e-9},
        };

        MixedTransientOptions opts{};
        opts.t_stop_s = 6.2e-6;
        opts.max_step_s = 25.0e-9;
        opts.method = analysis::IntegrationMethod::BackwardEuler;

        MixedTransientResult res;
        if (!run_mixed_transient(plan, dig, a2d, d2a, opts, res).ok()) return false;
        if (!st->done) return false;

        int decoded = 0;
        for (int b = 0; b < n_bits; ++b) {
            if (dig.net_value(bits[static_cast<std::size_t>(b)]) == Logic4::L1) {
                decoded |= (1 << b);
            }
        }
        out_code = decoded;
        return true;
    };

    // Verify 3 distinct input voltages across the 0..3.2 V range (LSB = 0.05 V).
    const std::vector<std::pair<double, int>> cases = {
        {0.425, 8},   /* 8 * 0.05 = 0.400 V <= 0.425 V < 0.450 V */
        {1.625, 32},  /* 32 * 0.05 = 1.600 V <= 1.625 V < 1.650 V */
        {2.875, 57},  /* 57 * 0.05 = 2.850 V <= 2.875 V < 2.900 V */
    };
    for (const auto& [vin, expected_code] : cases) {
        int actual_code = -1;
        REQUIRE(run_sar_conversion(vin, actual_code));
        REQUIRE(actual_code == expected_code);
    }
}

#if !defined(SPICE_V2_USE_CATCH2)
int main() {
    for (const auto& [name, fn] : test_registry()) {
        const int before = g_failed_checks;
        fn();
        std::cout << (g_failed_checks == before ? "[PASS] " : "[FAIL] ") << name << "\n";
    }
    if (g_failed_checks != 0) {
        std::cerr << g_failed_checks << " check(s) failed.\n";
        return 1;
    }
    std::cout << "All " << test_registry().size() << " M4a mixed-signal co-simulation tests passed.\n";
    return 0;
}
#endif
