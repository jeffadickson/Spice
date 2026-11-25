#include "analysis/analyses.hpp"
#include "analysis/m2c_analyses.hpp"
#include "core/circuit.hpp"
#include "devices/linear_devices.hpp"
#include "parser/spice_parser.hpp"

#include <cmath>
#include <cstddef>
#include <iostream>
#include <string>
#include <utility>
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
#  define TEST_CASE(name, tags)                                                          \
      static void SPICE_CAT(spice_osdi_v04_fn_, __LINE__)();                             \
      static const TestRegistrar SPICE_CAT(spice_osdi_v04_reg_, __LINE__)(               \
          name, &SPICE_CAT(spice_osdi_v04_fn_, __LINE__));                               \
      static void SPICE_CAT(spice_osdi_v04_fn_, __LINE__)()
#  define REQUIRE(expr)                                                                  \
      do {                                                                               \
          if (!(expr)) {                                                                 \
              std::cerr << "FAIL " << __FILE__ << ":" << __LINE__ << " REQUIRE("         \
                        << #expr << ")\n";                                               \
              ++g_failed_checks;                                                         \
              return;                                                                    \
          }                                                                              \
      } while (false)
#endif

namespace {

using namespace spice;

std::size_t find_signal(const devices::CompiledPlan& plan, const std::string& id) {
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (plan.signals[i].id == id) return i;
    }
    return plan.signals.size();
}

bool approx(double a, double b, double atol = 1.0e-9, double rtol = 1.0e-6) {
    return std::abs(a - b) <= atol + rtol * std::max(std::abs(a), std::abs(b));
}

}  // namespace

TEST_CASE("OSDI 0.4 node collapsing removes internal node when RS=0 and allocates it when RS>0",
          "[osdi][v04][collapse]") {
    /* 1. RS = 0 -> internal node `ai` MUST collapse onto `a` (only 3 unknowns: v(in), v(out), i(V1)) */
    const std::string deck_rs0 =
        "* OSDI 0.4 collapsed RS=0\n"
        ".OSDI \"" SPICE_TEST_OSDI_V04_PATH "\"\n"
        ".MODEL D0 OSDI(model=osdi_v04_diode is=1e-14 n=1.0 rs=0 cjo=10p)\n"
        "V1 in 0 DC 5\n"
        "R1 in out 1k\n"
        "D1 out 0 D0\n"
        ".END\n";
    parser::ParsedDeck p0;
    REQUIRE(parser::parse_spice_deck(deck_rs0, p0).ok());
    devices::CompiledPlan plan0;
    REQUIRE(devices::compile_circuit(p0.circuit, plan0).ok());
    REQUIRE(plan0.unknown_count() == 3u);
    REQUIRE(find_signal(plan0, "v(D1:ai)") == plan0.signals.size());

    analysis::OpResult op0{};
    REQUIRE(analysis::run_op(plan0, {}, op0).ok());
    const double vout0 = op0.values[find_signal(plan0, "v(out)")];
    REQUIRE(vout0 > 0.68);
    REQUIRE(vout0 < 0.71);

    /* 2. RS = 10 -> internal node `ai` MUST NOT collapse (4 unknowns: v(in), v(out), v(D1:ai), i(V1)) */
    const std::string deck_rs10 =
        "* OSDI 0.4 uncollapsed RS=10\n"
        ".OSDI \"" SPICE_TEST_OSDI_V04_PATH "\"\n"
        ".MODEL D10 OSDI(model=osdi_v04_diode is=1e-14 n=1.0 rs=10 cjo=10p)\n"
        "V1 in 0 DC 5\n"
        "R1 in out 1k\n"
        "D1 out 0 D10\n"
        ".END\n";
    parser::ParsedDeck p10;
    REQUIRE(parser::parse_spice_deck(deck_rs10, p10).ok());
    devices::CompiledPlan plan10;
    REQUIRE(devices::compile_circuit(p10.circuit, plan10).ok());
    REQUIRE(plan10.unknown_count() == 4u);
    const std::size_t idx_ai = find_signal(plan10, "v(D1:ai)");
    REQUIRE(idx_ai < plan10.signals.size());

    analysis::OpResult op10{};
    REQUIRE(analysis::run_op(plan10, {}, op10).ok());
    const double vout10 = op10.values[find_signal(plan10, "v(out)")];
    const double vai10 = op10.values[idx_ai];
    const double i_d = (5.0 - vout10) / 1000.0;
    REQUIRE(approx(vout10 - vai10, 10.0 * i_d, 1.0e-10, 1.0e-9));
}

TEST_CASE("OSDI 0.4 pnjlim + lim_rhs converges robustly under extreme overdrive and matches .NOISE analytically",
          "[osdi][v04][limit][noise]") {
    constexpr double kB = 1.380649e-23;
    constexpr double q = 1.602176634e-19;
    constexpr double temp_k = 300.15;

    /* High overdrive (50 V across 1k + diode) exercises pnjlim + load_limit_rhs_resist */
    const std::string deck =
        "* OSDI 0.4 pnjlim + load_noise\n"
        ".OSDI \"" SPICE_TEST_OSDI_V04_PATH "\"\n"
        ".MODEL D10 OSDI(model=osdi_v04_diode is=1e-14 n=1.0 rs=10 cjo=0)\n"
        "V1 in 0 DC 50 AC 1\n"
        "R1 in out 1k\n"
        "D1 out 0 D10\n"
        ".END\n";
    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

    analysis::OpResult op{};
    REQUIRE(analysis::run_op(plan, {}, op).ok());
    const double vout = op.values[find_signal(plan, "v(out)")];
    const double vai = op.values[find_signal(plan, "v(D1:ai)")];
    const double id = (50.0 - vout) / 1000.0;
    REQUIRE(id > 0.048);
    REQUIRE(approx(vout - vai, 10.0 * id, 1.0e-9, 1.0e-9));

    /* Exact analytical noise check for R1=1k + RS=10 + diode dynamic resistance rd = Vt/Id:
     * Output voltage PSD at node `out` =
     *   (R1 || (RS + rd))^2 * [ 4*kB*T/R1 + (4*kB*T*RS + 2*q*Id*rd^2) / (RS + rd)^2 ] */
    const double vt = 8.617333262e-5 * temp_k;
    const double gd = (1.0e-14 * std::exp(vai / vt)) / vt + 1.0e-12;
    const double rd = 1.0 / gd;
    const double r_branch = 10.0 + rd;
    const double z_out = (1000.0 * r_branch) / (1000.0 + r_branch);
    const double psd_r1 = z_out * z_out * (4.0 * kB * temp_k / 1000.0);
    /* Transfer from thermal_rs current source (out -> ai) to out: z_out - z_ai_from_out.
     * Exact Thevenin: v_rs = 4*kB*T*RS in series with rd, driving R1/(R1 + RS + rd). */
    const double tf_series = 1000.0 / (1000.0 + r_branch);
    const double psd_rs = tf_series * tf_series * (4.0 * kB * temp_k * 10.0);
    const double psd_shot = tf_series * tf_series * (rd * rd * 2.0 * q * id);
    const double expected_onoise = std::sqrt(psd_r1 + psd_rs + psd_shot);

    analysis::NoiseOptions nopts{};
    nopts.input_source_id = "V1";
    nopts.out_p = core::NodeId{2}; /* out */
    nopts.out_n = core::NodeId{0};
    nopts.temperature_k = temp_k;
    nopts.frequencies_hz = {1000.0};
    analysis::NoiseResult nres{};
    REQUIRE(analysis::run_noise(plan, nopts, nres).ok());
    REQUIRE(approx(nres.points[0].onoise_v_per_sqrt_hz, expected_onoise, 1.0e-15, 1.0e-5));
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
    std::cout << "All " << test_registry().size() << " OSDI 0.4 hardening tests passed.\n";
    return 0;
}
#endif
