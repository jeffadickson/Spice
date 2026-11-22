#include "analysis/analyses.hpp"
#include "devices/linear_devices.hpp"
#include "parser/spice_parser.hpp"

#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <numbers>
#include <sstream>
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
      static void SPICE_CAT(spice_m2b_p_fn_, __LINE__)();                               \
      static const TestRegistrar SPICE_CAT(spice_m2b_p_reg_, __LINE__)(                 \
          name, &SPICE_CAT(spice_m2b_p_fn_, __LINE__));                                 \
      static void SPICE_CAT(spice_m2b_p_fn_, __LINE__)()
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

bool approx(double actual, double expected, double abs_tol, double rel_tol = 1.0e-9) {
    return std::abs(actual - expected) <= abs_tol + rel_tol * std::abs(expected);
}

std::size_t find_signal_index(const devices::CompiledPlan& plan, const std::string& sig_id) {
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (plan.signals[i].id == sig_id) return i;
    }
    return plan.signals.size();
}

#if defined(_WIN32)
constexpr const char* kNullDev = "NUL";
#else
constexpr const char* kNullDev = "/dev/null";
#endif

int run_cli_command(const std::string& cmd) {
#if defined(_WIN32)
    const std::string wrapped = "\"" + cmd + "\"";
    return std::system(wrapped.c_str());
#else
    const int rc = std::system(cmd.c_str());
    return (rc >= 0) ? ((rc >> 8) & 0xFF) : -1;
#endif
}

}  // namespace

TEST_CASE("Variable-step BDF2 and adaptive LTE transient achieve 2nd-order accuracy and step adaptation", "[m2b][lte][bdf2]") {
    // RC decay from V(0)=5V with tau = R*C = 1 ms over [0, 10 ms]: V(t) = 5 * exp(-t / 1ms).
    core::Circuit c;
    c.name = "rc_decay_adaptive";
    const auto n_out = c.get_or_add_node("out");
    const core::NodeId gnd{0};
    c.elements.push_back(core::ResistorSpec{"R1", n_out, gnd, 1000.0});
    c.elements.push_back(core::CapacitorSpec{"C1", n_out, gnd, 1.0e-6, 5.0});

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(c, plan).ok());
    const std::size_t out_idx = find_signal_index(plan, "v(out)");
    REQUIRE(out_idx < plan.signals.size());

    // 1. Verify BDF2 2nd-order convergence on fixed steps (h vs h/2 reduces error by ~4x).
    double err_coarse = 0.0;
    double err_fine = 0.0;
    for (double h : {2.0e-4, 1.0e-4}) {
        analysis::TransientOptions opts{};
        opts.step_s = h;
        opts.stop_s = 2.0e-3;
        opts.method = analysis::IntegrationMethod::Bdf2;
        opts.startup = analysis::StartupMode::Uic;
        opts.adaptive = false;
        opts.common.enable_audits = true;
        analysis::TransientResult res;
        REQUIRE(analysis::run_transient(plan, opts, res).ok());
        REQUIRE(res.audit.has_value());
        const double v_end = res.samples.back().values[out_idx];
        const double v_exact = 5.0 * std::exp(-2.0);
        const double err = std::abs(v_end - v_exact);
        if (h > 1.5e-4) err_coarse = err;
        else err_fine = err;
    }
    REQUIRE(err_coarse < 2.0e-2);
    REQUIRE(err_fine < err_coarse / 3.0);

    // 2. Adaptive LTE on both BDF2 and Trapezoidal across 10*tau: starts small, grows h dramatically in tail!
    for (auto method : {analysis::IntegrationMethod::Bdf2, analysis::IntegrationMethod::Trapezoidal,
                        analysis::IntegrationMethod::BackwardEuler}) {
        analysis::TransientOptions opts{};
        opts.step_s = 1.0e-5;
        opts.max_step_s = 2.0e-3;
        opts.stop_s = 10.0e-3;
        opts.method = method;
        opts.startup = analysis::StartupMode::Uic;
        opts.adaptive = true;
        opts.lte_reltol = 2.0e-4;
        opts.lte_abstol = 1.0e-5;
        opts.common.enable_audits = false;

        analysis::TransientResult res;
        REQUIRE(analysis::run_transient(plan, opts, res).ok());
        REQUIRE(res.accepted_steps > 5u);
        // Step size must grow significantly once decay slows down.
        REQUIRE(res.max_step_used_s > 5.0 * res.min_step_used_s);

        // Verify accuracy against exact 5*exp(-t/1ms) at every accepted sample.
        double max_abs_err = 0.0;
        for (const auto& s : res.samples) {
            const double exact = 5.0 * std::exp(-s.time_s / 1.0e-3);
            max_abs_err = std::max(max_abs_err, std::abs(s.values[out_idx] - exact));
        }
        const double bound = (method == analysis::IntegrationMethod::BackwardEuler) ? 3.5e-2 : 5.0e-3;
        REQUIRE(max_abs_err < bound);
    }

    // 3. Nonlinear pulse-driven diode circuit with adaptive BDF2 hits exact PULSE breakpoints cleanly.
    core::Circuit nl;
    nl.name = "pulse_diode_adaptive";
    const auto n_in = nl.get_or_add_node("in");
    const auto n_d = nl.get_or_add_node("d");
    core::PulseWaveform pulse;
    pulse.v1 = 0.0;
    pulse.v2 = 5.0;
    pulse.delay_s = 2.0e-6;
    pulse.rise_s = 1.0e-6;
    pulse.fall_s = 1.0e-6;
    pulse.width_s = 10.0e-6;
    pulse.period_s = 0.0;
    nl.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, 0.0, {0.0, 0.0}, pulse});
    nl.elements.push_back(core::ResistorSpec{"R1", n_in, n_d, 1000.0});
    core::DiodeSpec d{};
    d.id = "D1";
    d.p = n_d;
    d.n = gnd;
    d.is_amps = 1.0e-14;
    d.cjo_farads = 20.0e-12;
    d.tt_s = 5.0e-9;
    d.gmin_siemens = 0.0;
    nl.elements.push_back(d);

    devices::CompiledPlan nl_plan;
    REQUIRE(devices::compile_circuit(nl, nl_plan).ok());
    analysis::TransientOptions nl_opts{};
    nl_opts.step_s = 0.5e-6;
    nl_opts.max_step_s = 2.0e-6;
    nl_opts.stop_s = 20.0e-6;
    nl_opts.method = analysis::IntegrationMethod::Bdf2;
    nl_opts.adaptive = true;
    nl_opts.lte_reltol = 1.0e-3;
    nl_opts.lte_abstol = 1.0e-5;
    nl_opts.common.enable_audits = false;

    analysis::TransientResult nl_res;
    REQUIRE(analysis::run_transient(nl_plan, nl_opts, nl_res).ok());
    bool hit_2us = false;
    bool hit_3us = false;
    for (const auto& s : nl_res.samples) {
        if (std::abs(s.time_s - 2.0e-6) < 1.0e-12) hit_2us = true;
        if (std::abs(s.time_s - 3.0e-6) < 1.0e-12) hit_3us = true;
    }
    REQUIRE(hit_2us);
    REQUIRE(hit_3us);
}

TEST_CASE("Native SPICE3f5 parser elaborates .SUBCKT, .PARAM, .MODEL D, B-sources, K, and OSDI decks", "[m2b][parser]") {
    // 1. Hierarchical .SUBCKT + .PARAM + {expr} + continuation + inline comments + .OP + .AC + .TRAN.
    const std::string subckt_deck = R"(Hierarchical RC Stage Test
* Two cascaded subcircuit stages
.PARAM RBASE=1k CBASE=1u VSUPPLY={2.5 * 2}
.SUBCKT RCSTAGE in out PARAMS: RVAL=1k CVAL=1u
R1 in out {RVAL} ; series resistor
C1 out 0 {CVAL}
.ENDS RCSTAGE

Vin in 0 DC {VSUPPLY}
+ AC 1 0
X1 in mid RCSTAGE PARAMS: RVAL={RBASE} CVAL={CBASE}
X2 mid out RCSTAGE PARAMS: RVAL={RBASE * 2} CVAL={CBASE / 2}
Rload out 0 1k
.OP
.AC DEC 5 10 10k
.END
)";

    parser::ParsedDeck deck;
    REQUIRE(parser::parse_spice_deck(subckt_deck, deck).ok());
    REQUIRE(deck.title == "Hierarchical RC Stage Test");
    REQUIRE(deck.analyses.size() == 2u);

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(deck.circuit, plan).ok());
    analysis::AnalysisCommonOptions op_opts{};
    analysis::OpResult op;
    REQUIRE(analysis::run_op(plan, op_opts, op).ok());
    REQUIRE(op.audit.has_value());
    REQUIRE(op.audit->all_checked_passed());

    // Total series R = 1k + 2k + 1k = 4k, Vin = 5V -> V(out) = 5 * 1/4 = 1.25V, V(mid) = 5 * 3/4 = 3.75V.
    const std::size_t idx_mid = find_signal_index(plan, "v(mid)");
    const std::size_t idx_out = find_signal_index(plan, "v(out)");
    REQUIRE(idx_mid < plan.signals.size());
    REQUIRE(idx_out < plan.signals.size());
    REQUIRE(approx(op.values[idx_mid], 3.75, 1.0e-12));
    REQUIRE(approx(op.values[idx_out], 1.25, 1.0e-12));

    const auto& ac_cmd = std::get<parser::AcCommand>(deck.analyses[1]);
    REQUIRE(ac_cmd.frequencies_hz.size() >= 15u);
    analysis::AcOptions ac_opts{};
    ac_opts.frequencies_hz = ac_cmd.frequencies_hz;
    analysis::AcResult ac_res;
    REQUIRE(analysis::run_ac(plan, ac_opts, ac_res).ok());
    REQUIRE(ac_res.audit.has_value());
    REQUIRE(ac_res.audit->all_checked_passed());

    // 2. Nonlinear Diode + B-source + .OPTIONS METHOD=BDF2 + .DC + .TRAN deck.
    const std::string nl_deck = R"(Diode and Behavioral Source Deck
.MODEL D1N4148 D(IS=1e-14 N=1 CJO=10p TT=2n)
V1 in 0 DC 5 SIN(0 5 1k)
R1 in d 1k
D1 d 0 D1N4148
B1 buf 0 V={2.0 * v(d)}
Rbuf buf 0 10k
.OPTIONS METHOD=BDF2 RELTOL=1e-6
.OP
.DC V1 -1 3 1
.TRAN 25u 500u
.END
)";
    parser::ParsedDeck nl_parsed;
    REQUIRE(parser::parse_spice_deck(nl_deck, nl_parsed).ok());
    REQUIRE(nl_parsed.options.method == "bdf2");
    REQUIRE(nl_parsed.analyses.size() == 3u);

    devices::CompiledPlan nl_plan;
    REQUIRE(devices::compile_circuit(nl_parsed.circuit, nl_plan).ok());
    analysis::OpResult nl_op;
    analysis::AnalysisCommonOptions nl_common{};
    nl_common.enable_audits = false;
    REQUIRE(analysis::run_op(nl_plan, nl_common, nl_op).ok());
    const double vd = nl_op.values[find_signal_index(nl_plan, "v(d)")];
    const double vbuf = nl_op.values[find_signal_index(nl_plan, "v(buf)")];
    REQUIRE(vd > 0.65 && vd < 0.72);
    REQUIRE(approx(vbuf, 2.0 * vd, 1.0e-11));

    // 3. Real OSDI shared library instantiated directly from SPICE deck via .OSDI + .MODEL OSDI.
    const std::string osdi_deck =
        "OSDI SPICE Deck\n"
        ".OSDI \"" + std::string(SPICE_TEST_OSDI_DIODE_PATH) + "\"\n"
        ".MODEL MOD_D OSDI(model=osdi_test_diode is=2e-14 vt=0.026 cjo=25p)\n"
        "V1 in 0 3.3\n"
        "R1 in d 500\n"
        "X1 d 0 MOD_D area=2.0\n"
        ".OP\n"
        ".END\n";
    parser::ParsedDeck osdi_parsed;
    REQUIRE(parser::parse_spice_deck(osdi_deck, osdi_parsed).ok());
    devices::CompiledPlan osdi_plan;
    REQUIRE(devices::compile_circuit(osdi_parsed.circuit, osdi_plan).ok());
    REQUIRE(osdi_plan.has_osdi);
    analysis::OpResult osdi_op;
    REQUIRE(analysis::run_op(osdi_plan, nl_common, osdi_op).ok());
    const double vd_osdi = osdi_op.values[find_signal_index(osdi_plan, "v(d)")];
    const double id_exact = 2.0 * 2.0e-14 * std::expm1(vd_osdi / 0.026);
    REQUIRE(approx(id_exact, (3.3 - vd_osdi) / 500.0, 1.0e-12, 1.0e-12));

    // 4. Fail-closed negative parser checks (line numbers reported, never silent).
    {
        parser::ParsedDeck bad;
        auto d = parser::parse_spice_deck("Bad Cycle\n.SUBCKT A 1 2\nX1 1 2 A\n.ENDS\nXtop n1 0 A\n.END\n", bad);
        REQUIRE(!d.ok());
        REQUIRE(d.message.find("line ") != std::string::npos);
    }
    {
        parser::ParsedDeck bad;
        auto d = parser::parse_spice_deck("Missing Ends\n.SUBCKT A 1 2\nR1 1 2 1k\n.END\n", bad);
        REQUIRE(!d.ok());
        REQUIRE(d.message.find("line ") != std::string::npos);
    }
    {
        parser::ParsedDeck bad;
        auto d = parser::parse_spice_deck("Bad Model\nV1 1 0 1\nD1 1 0 UNKNOWN_D\n.END\n", bad);
        REQUIRE(!d.ok());
        REQUIRE(d.message.find("line ") != std::string::npos);
    }
    {
        parser::ParsedDeck bad;
        auto d = parser::parse_spice_deck("Bad Directive\n.BOGUS 123\n.END\n", bad);
        REQUIRE(!d.ok());
        REQUIRE(d.code == core::StatusCode::Unsupported);
    }
    {
        parser::ParsedDeck bad;
        auto d = parser::parse_spice_deck("Div Zero Param\n.PARAM X={1/0}\nR1 1 0 {X}\n.END\n", bad);
        REQUIRE(!d.ok());
    }
}

TEST_CASE("Native spice_v2_cli executes check and run subcommands with JSON/CSV export and exit codes", "[m2b][cli]") {
    namespace fs = std::filesystem;
    const std::string uniq = std::to_string(reinterpret_cast<std::uintptr_t>(&g_failed_checks)) + "_" +
                             std::to_string(std::hash<std::string>{}(SPICE_V2_CLI_PATH));
    const fs::path tmp_dir = fs::temp_directory_path() / ("spice_v2_cli_test_" + uniq);
    fs::create_directories(tmp_dir);
    const fs::path deck_path = tmp_dir / "rc_cli.cir";
    const fs::path bad_path = tmp_dir / "bad_cli.cir";
    const fs::path json_path = tmp_dir / "out.json";
    const fs::path csv_path = tmp_dir / "out.csv";

    {
        std::ofstream ofs(deck_path);
        ofs << "CLI RC Test\n"
            << "V1 in 0 DC 5 PULSE(0 5 0 1u 1u 50u 100u)\n"
            << "R1 in out 1k\n"
            << "C1 out 0 10n\n"
            << ".OP\n"
            << ".TRAN 2u 40u\n"
            << ".END\n";
    }
    {
        std::ofstream ofs(bad_path);
        ofs << "Bad Deck\n.UNKNOWN_DIRECTIVE 1\n.END\n";
    }

    const std::string cli = std::string("\"") + SPICE_V2_CLI_PATH + "\"";
    REQUIRE(run_cli_command(cli + " version > " + kNullDev) == 0);
    REQUIRE(run_cli_command(cli + " check \"" + deck_path.string() + "\" > " + kNullDev) == 0);
    REQUIRE(run_cli_command(cli + " run \"" + deck_path.string() + "\" --json \"" + json_path.string() +
                            "\" --csv \"" + csv_path.string() + "\" --method bdf2 --adaptive > " + kNullDev) == 0);

    REQUIRE(fs::exists(json_path));
    REQUIRE(fs::exists(csv_path));
    {
        std::ifstream ifs(json_path);
        std::ostringstream oss;
        oss << ifs.rdbuf();
        const std::string content = oss.str();
        REQUIRE(content.find("\"title\": \"CLI RC Test\"") != std::string::npos);
        REQUIRE(content.find("\"kind\": \"op\"") != std::string::npos);
        REQUIRE(content.find("\"kind\": \"tran\"") != std::string::npos);
    }

    // Negative exit codes: parse failure => 3, invalid usage => 2.
    REQUIRE(run_cli_command(cli + " check \"" + bad_path.string() + "\" 2>" + kNullDev) == 3);
    REQUIRE(run_cli_command(cli + " bogus_cmd 2>" + kNullDev) == 2);

    fs::remove_all(tmp_dir);
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
    std::cout << "All " << test_registry().size() << " M2b parser/LTE/CLI tests passed.\n";
    return 0;
}
#endif
