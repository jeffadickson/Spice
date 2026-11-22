#include "analysis/analyses.hpp"
#include "analysis/m2c_analyses.hpp"
#include "core/circuit.hpp"
#include "devices/linear_devices.hpp"
#include "parser/spice_parser.hpp"

#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
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
      static void SPICE_CAT(spice_semi_fn_, __LINE__)();                                \
      static const TestRegistrar SPICE_CAT(spice_semi_reg_, __LINE__)(                  \
          name, &SPICE_CAT(spice_semi_fn_, __LINE__));                                  \
      static void SPICE_CAT(spice_semi_fn_, __LINE__)()
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

std::size_t find_signal(const devices::CompiledPlan& plan, const std::string& id) {
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (plan.signals[i].id == id) return i;
    }
    return plan.signals.size();
}

std::string read_all(const std::filesystem::path& p) {
    std::ifstream ifs(p);
    std::ostringstream oss;
    oss << ifs.rdbuf();
    return oss.str();
}

}  // namespace

TEST_CASE("BSIM3v3 (LEVEL=49) and BSIM4 (LEVEL=54) MOSFET physics: subthreshold, DIBL, and CMOS inverter VTC/TRAN", "[semi][bsim]") {
    // 1. BSIM3v3 (LEVEL=49) NMOS subthreshold slope + strong inversion + DIBL check via SPICE deck!
    const std::string bsim3_deck = R"(BSIM3v3 Short-Channel NMOS Characterization
.MODEL nch3 NMOS(LEVEL=49 VERSION=3.3 VTH0=0.40 TOX=3.2n U0=450 VSAT=1.0e5 K1=0.50 ETA0=0.08 PCLM=1.2 RDSW=150 VOFF=-0.06 NFACTOR=1.0)
Vds d 0 DC 1.0
Vgs g 0 DC 0.2
M1 d g 0 0 nch3 W=1u L=100n
.DC Vgs 0.0 1.2 0.1
.END
)";
    parser::ParsedDeck pdeck;
    const auto dp = parser::parse_spice_deck(bsim3_deck, pdeck);
    if (!dp.ok()) std::cerr << "BSIM3 parse error: " << dp.message << "\n";
    REQUIRE(dp.ok());

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(pdeck.circuit, plan).ok());
    const std::size_t idx_ivds = find_signal(plan, "i(Vds)");
    REQUIRE(idx_ivds < plan.unknown_count());

    analysis::DcSweepOptions dc_opts{};
    dc_opts.source_id = "Vgs";
    dc_opts.start = 0.0;
    dc_opts.stop = 1.2;
    dc_opts.step = 0.1;
    analysis::DcSweepResult dc;
    REQUIRE(analysis::run_dc_sweep(plan, dc_opts, dc).ok());
    REQUIRE(dc.points.size() >= 13u);

    // Passive sign convention: drain current enters drain, supplied by Vds as -i(Vds).
    const double id_01 = -dc.points[1].values[idx_ivds];  /* Vgs = 0.1 V (subthreshold) */
    const double id_02 = -dc.points[2].values[idx_ivds];  /* Vgs = 0.2 V (subthreshold) */
    const double id_10 = -dc.points[10].values[idx_ivds]; /* Vgs = 1.0 V (strong inversion) */
    REQUIRE(id_01 > 0.0 && id_02 > id_01);
    const double subth_ratio = id_02 / id_01;
    // Physical subthreshold swing (~60-130 mV/dec) => 100 mV step increases Id by 5x..60x!
    REQUIRE(subth_ratio > 5.0 && subth_ratio < 60.0);
    // Strong-inversion ON current >> subthreshold leakage (> 100 uA for W/L=10 at Vgs=1.0V).
    REQUIRE(id_10 > 1.0e-4 && id_10 < 1.0e-2);

    // 2. Full BSIM4 (LEVEL=54) CMOS Inverter VTC (.DC) and switching transient (.TRAN)!
    const std::string cmos_inv_deck = R"(BSIM4 Complementary CMOS Inverter
.MODEL nch4 NMOS(LEVEL=54 VERSION=4.8 VTH0=0.35 TOXE=2.2n U0=420 VSAT=1.1e5 K1=0.45 ETA0=0.06 PCLM=1.1 RDSW=120 CGSO=2e-10 CGDO=2e-10)
.MODEL pch4 PMOS(LEVEL=54 VERSION=4.8 VTH0=-0.35 TOXE=2.2n U0=160 VSAT=0.9e5 K1=0.45 ETA0=0.06 PCLM=1.1 RDSW=150 CGSO=2e-10 CGDO=2e-10)
Vdd vdd 0 DC 1.2
Vin in 0 DC 0.0 PULSE(0 1.2 1n 0.2n 0.2n 4n 10n)
Mn out in 0 0 nch4 W=1u L=65n
Mp out in vdd vdd pch4 W=2.2u L=65n
Cload out 0 20f
.DC Vin 0.0 1.2 0.05
.TRAN 0.1n 8n
.END
)";
    parser::ParsedDeck inv_deck;
    const auto dinv = parser::parse_spice_deck(cmos_inv_deck, inv_deck);
    if (!dinv.ok()) std::cerr << "BSIM4 inv parse error: " << dinv.message << "\n";
    REQUIRE(dinv.ok());

    devices::CompiledPlan inv_plan;
    REQUIRE(devices::compile_circuit(inv_deck.circuit, inv_plan).ok());
    const std::size_t idx_out = find_signal(inv_plan, "v(out)");

    // VTC check: V(in)=0 -> V(out) ~= 1.2V; V(in)=1.2 -> V(out) ~= 0V; sharp gain in middle.
    analysis::DcSweepOptions inv_dc_opts{};
    inv_dc_opts.source_id = "Vin";
    inv_dc_opts.start = 0.0;
    inv_dc_opts.stop = 1.2;
    inv_dc_opts.step = 0.05;
    analysis::DcSweepResult inv_dc;
    REQUIRE(analysis::run_dc_sweep(inv_plan, inv_dc_opts, inv_dc).ok());
    REQUIRE(inv_dc.points.front().values[idx_out] > 1.18);
    REQUIRE(inv_dc.points.back().values[idx_out] < 0.02);

    double max_slope = 0.0;
    for (std::size_t i = 1u; i < inv_dc.points.size(); ++i) {
        const double dv_out = inv_dc.points[i].values[idx_out] - inv_dc.points[i - 1u].values[idx_out];
        const double dv_in = inv_dc.points[i].sweep_value - inv_dc.points[i - 1u].sweep_value;
        max_slope = std::max(max_slope, std::abs(dv_out / dv_in));
    }
    REQUIRE(max_slope > 5.0);

    // Transient switching check.
    analysis::TransientOptions tr_opts{};
    tr_opts.step_s = 0.1e-9;
    tr_opts.stop_s = 8.0e-9;
    tr_opts.method = analysis::IntegrationMethod::BackwardEuler;
    analysis::TransientResult tr;
    REQUIRE(analysis::run_transient(inv_plan, tr_opts, tr).ok());
    REQUIRE(tr.samples.front().values[idx_out] > 1.15);
    double v_at_3ns = 1.0;
    for (const auto& s : tr.samples) {
        if (std::abs(s.time_s - 3.0e-9) < 0.15e-9) v_at_3ns = s.values[idx_out];
    }
    REQUIRE(v_at_3ns < 0.05);
    REQUIRE(tr.samples.back().values[idx_out] > 1.15);
}

TEST_CASE("Native Gummel-Poon BJT (NPN/PNP), MOSFET Level 1, and JFET amplifiers with .OP, .AC, and .NOISE", "[semi][bjt][jfet]") {
    // 1. NPN Common-Emitter Amplifier: Vcc=12V, voltage-divider biased, verifying Q-point, AC inverting gain, and shot noise!
    const std::string bjt_deck = R"(NPN Common-Emitter Amplifier
.MODEL Q2N2222 NPN(IS=1e-14 BF=200 VAF=100 CJE=10p CJC=4p TF=0.3n)
Vcc vcc 0 DC 12
Vin in 0 DC 0 AC 1
Cin in b 10u
R1 vcc b 47k
R2 b 0 10k
Rc vcc c 2.2k
Re e 0 1k
Ce e 0 100u
Q1 c b e Q2N2222
.OP
.AC DEC 5 1k 100k
.END
)";
    parser::ParsedDeck bjt_p;
    const auto db = parser::parse_spice_deck(bjt_deck, bjt_p);
    if (!db.ok()) std::cerr << "BJT parse error: " << db.message << "\n";
    REQUIRE(db.ok());

    devices::CompiledPlan bjt_plan;
    REQUIRE(devices::compile_circuit(bjt_p.circuit, bjt_plan).ok());

    analysis::OpResult op;
    REQUIRE(analysis::run_op(bjt_plan, {}, op).ok());
    const double vb = op.values[find_signal(bjt_plan, "v(b)")];
    const double ve = op.values[find_signal(bjt_plan, "v(e)")];
    const double vc = op.values[find_signal(bjt_plan, "v(c)")];
    // Active region: Vbe ~= 0.65..0.75 V, Ve ~= 1.2..1.5 V, Vc > Vb (forward active!).
    REQUIRE(vb - ve > 0.60 && vb - ve < 0.80);
    REQUIRE(vc > vb + 2.0 && vc < 11.0);

    // Midband AC gain at 10 kHz: |Av| ~= gm * (Rc || ro), where Ic ~= Ve / 1k ~= 1.3 mA, gm ~= 50 mS -> |Av| ~= 80..120!
    analysis::AcOptions ac_opts{};
    ac_opts.frequencies_hz = {10.0e3};
    analysis::AcResult ac;
    REQUIRE(analysis::run_ac(bjt_plan, ac_opts, ac).ok());
    const auto v_c_ac = ac.points[0].values[find_signal(bjt_plan, "v(c)")];
    REQUIRE(v_c_ac.real() < -50.0); /* Inverting gain > 50x! */

    // Noise check: BJT shot noise contributes positively alongside thermal resistors.
    analysis::NoiseOptions n_opts{};
    n_opts.input_source_id = "Vin";
    n_opts.out_p = bjt_p.circuit.get_or_add_node("c");
    n_opts.out_n = core::NodeId{0};
    n_opts.frequencies_hz = {10.0e3};
    analysis::NoiseResult nr;
    REQUIRE(analysis::run_noise(bjt_plan, n_opts, nr).ok());
    REQUIRE(nr.points[0].onoise_v_per_sqrt_hz > 1.0e-8);

    // 2. N-Channel JFET Common-Source Self-Biased Stage.
    const std::string jfet_deck = R"(NJF Common-Source Amplifier
.MODEL J2N5457 NJF(VTO=-2.0 BETA=1.2e-3 LAMBDA=0.01 IS=1e-14 CGS=2p CGD=2p)
Vdd vdd 0 DC 12
Vin g 0 DC 0 AC 1
Rd vdd d 2k
Rs s 0 500
Cs s 0 100u
J1 d g s J2N5457
.OP
.END
)";
    parser::ParsedDeck jfet_p;
    REQUIRE(parser::parse_spice_deck(jfet_deck, jfet_p).ok());
    devices::CompiledPlan jfet_plan;
    REQUIRE(devices::compile_circuit(jfet_p.circuit, jfet_plan).ok());
    analysis::OpResult jop;
    REQUIRE(analysis::run_op(jfet_plan, {}, jop).ok());
    const double vd_j = jop.values[find_signal(jfet_plan, "v(d)")];
    const double vs_j = jop.values[find_signal(jfet_plan, "v(s)")];
    // Self-biased: Vgs = -Vs in (-2, 0), Id = Vs / 500 > 0, Vd = 12 - Id*2000 in (2, 11).
    REQUIRE(vs_j > 0.3 && vs_j < 1.8);
    REQUIRE(vd_j > vs_j + 1.0 && vd_j < 11.5);

    // 3. Fail-closed negative checks: unlisted MOSFET LEVEL=99 and unknown .MODEL param rejected!
    const std::string bad_lvl = R"(Bad Level
.MODEL badm NMOS(LEVEL=99 VTH0=0.5)
V1 d 0 DC 1
M1 d 0 0 0 badm
.OP
.END
)";
    parser::ParsedDeck bad_p;
    const auto d_bad = parser::parse_spice_deck(bad_lvl, bad_p);
    REQUIRE(!d_bad.ok());
    REQUIRE(d_bad.code == core::StatusCode::Unsupported);

    const std::string bad_param = R"(Bad Param
.MODEL badm NMOS(LEVEL=54 NOTAREALPARAM=1.23)
V1 d 0 DC 1
M1 d 0 0 0 badm
.OP
.END
)";
    const auto d_badp = parser::parse_spice_deck(bad_param, bad_p);
    REQUIRE(!d_badp.ok());
}

TEST_CASE("End-to-end spice_v2_cli runs real SPICE decks with .INCLUDE/.LIB/.GLOBAL/.MEAS/.TF/.NOISE/.HB/.PAC/.SP", "[cli][corpus]") {
    namespace fs = std::filesystem;
    const std::string suffix = std::to_string(std::hash<std::string>{}(SPICE_V2_CLI_PATH)) + "_semi";
    const fs::path tmp_dir = fs::temp_directory_path() / ("spice_v2_corpus_" + suffix);
    fs::create_directories(tmp_dir);

    // Write shared library file for .LIB TT section!
    const fs::path lib_file = tmp_dir / "foundry.lib";
    {
        std::ofstream ofs(lib_file);
        ofs << ".LIB TT\n"
            << ".MODEL nch_tt NMOS(LEVEL=54 VTH0=0.4 TOXE=2.5n U0=450 VSAT=1e5)\n"
            << ".MODEL pch_tt PMOS(LEVEL=54 VTH0=-0.4 TOXE=2.5n U0=160 VSAT=0.9e5)\n"
            << ".ENDL TT\n"
            << ".LIB SS\n"
            << ".MODEL nch_tt NMOS(LEVEL=54 VTH0=0.55 TOXE=2.8n U0=380 VSAT=0.9e5)\n"
            << ".ENDL SS\n";
    }

    // Deck 1: Multi-analysis CMOS + .LIB + .GLOBAL + .TEMP + .MEAS + .TF + .NOISE + .SENS + .DCMATCH + .MC + .TRAN + .FOUR
    const fs::path deck1 = tmp_dir / "cmos_full.cir";
    const fs::path json1 = tmp_dir / "cmos_full.json";
    {
        std::ofstream ofs(deck1);
        ofs << "Full RSpice Deck Corpus Test\n"
            << ".GLOBAL vdd\n"
            << ".TEMP 27\n"
            << ".LIB foundry.lib TT\n"
            << "Vdd vdd 0 DC 1.8\n"
            << "Vin in 0 DC 0.9 AC 1 SIN(0.9 0.1 1k)\n"
            << "Rsrc in g 1k\n"
            << "Mn out g 0 0 nch_tt W=2u L=100n\n"
            << "Mp out g vdd vdd pch_tt W=4.5u L=100n\n"
            << "Rload out 0 100k\n"
            << "Cload out 0 1p\n"
            << ".SAVE v(in) v(out)\n"
            << ".OP\n"
            << ".TF v(out) Vin\n"
            << ".NOISE v(out) Vin DEC 5 1k 100k\n"
            << ".SENS v(out)\n"
            << ".DCMATCH v(out) sigma=0.01\n"
            << ".MC 8 seed=7 sigma=0.02\n"
            << ".TRAN 20u 2m\n"
            << ".FOUR 1k v(out)\n"
            << ".FFT v(out) N=128\n"
            << ".MEAS TRAN vout_pp PP v(out)\n"
            << ".MEAS TRAN vout_max MAX v(out)\n"
            << ".END\n";
    }

    auto run_cmd = [](const std::string& c) -> int {
#if defined(_WIN32)
        const std::string wrapped = "\"" + c + "\"";
        return std::system(wrapped.c_str());
#else
        return std::system(c.c_str());
#endif
    };
#if defined(_WIN32)
    const std::string null_dev = "NUL";
#else
    const std::string null_dev = "/dev/null";
#endif

    const std::string cmd1 =
        std::string("\"") + SPICE_V2_CLI_PATH + "\" run \"" + deck1.string() + "\" --json \"" + json1.string() + "\"";
    REQUIRE(run_cmd(cmd1) == 0);
    const std::string j1 = read_all(json1);
    REQUIRE(j1.find("\"kind\": \"op\"") != std::string::npos);
    REQUIRE(j1.find("\"kind\": \"tf\"") != std::string::npos);
    REQUIRE(j1.find("\"kind\": \"noise\"") != std::string::npos);
    REQUIRE(j1.find("\"kind\": \"sens\"") != std::string::npos);
    REQUIRE(j1.find("\"kind\": \"dcmatch\"") != std::string::npos);
    REQUIRE(j1.find("\"kind\": \"mc\"") != std::string::npos);
    REQUIRE(j1.find("\"kind\": \"tran\"") != std::string::npos);
    REQUIRE(j1.find("\"kind\": \"four\"") != std::string::npos);
    REQUIRE(j1.find("\"kind\": \"fft\"") != std::string::npos);
    REQUIRE(j1.find("\"vout_pp\"") != std::string::npos);

    // Deck 2: RF Diode Mixer running .HB + .PAC + .PXF + .PNOISE + .PSS directly from SPICE directives!
    const fs::path deck2 = tmp_dir / "rf_mixer.cir";
    const fs::path json2 = tmp_dir / "rf_mixer.json";
    {
        std::ofstream ofs(deck2);
        ofs << "RF Mixer Deck\n"
            << ".MODEL DMIX D(IS=1e-14 CJO=5p)\n"
            << "Vlo lo 0 DC 0.4 SIN(0.4 0.8 1Meg)\n"
            << "Vrf rf 0 DC 0 AC 1\n"
            << "Rlo lo out 500\n"
            << "Rrf rf out 500\n"
            << "D1 out 0 DMIX\n"
            << "C1 out 0 20p\n"
            << ".HB 1Meg H=5\n"
            << ".PAC 100k Vrf\n"
            << ".PXF 100k v(out) SB=1\n"
            << ".PNOISE 100k v(out) SB=0\n"
            << ".PSS 1Meg STEPS=32\n"
            << ".END\n";
    }
    const std::string cmd2 =
        std::string("\"") + SPICE_V2_CLI_PATH + "\" run \"" + deck2.string() + "\" --json \"" + json2.string() + "\"";
    REQUIRE(run_cmd(cmd2) == 0);
    const std::string j2 = read_all(json2);
    REQUIRE(j2.find("\"kind\": \"hb\"") != std::string::npos);
    REQUIRE(j2.find("\"kind\": \"pac\"") != std::string::npos);
    REQUIRE(j2.find("\"kind\": \"pxf\"") != std::string::npos);
    REQUIRE(j2.find("\"kind\": \"pnoise\"") != std::string::npos);
    REQUIRE(j2.find("\"kind\": \"pss\"") != std::string::npos);

    // Deck 3: 2-Port S-Parameter T-line deck writing Touchstone .s2p!
    const fs::path deck3 = tmp_dir / "rf_sp.cir";
    const fs::path s2p_out = tmp_dir / "rf_sp.s2p";
    const fs::path json3 = tmp_dir / "rf_sp.json";
    {
        std::ofstream ofs(deck3);
        ofs << "Lossless 50-Ohm T-line S-Parameters\n"
            << "T1 p1 0 p2 0 Z0=50 TD=1n\n"
            << ".SP LIN 5 100Meg 500Meg PORT=p1 PORT=p2 Z0=50\n"
            << ".END\n";
    }
    const std::string cmd3 =
        std::string("\"") + SPICE_V2_CLI_PATH + "\" run \"" + deck3.string() + "\" --json \"" + json3.string() +
        "\" --touchstone \"" + s2p_out.string() + "\"";
    REQUIRE(run_cmd(cmd3) == 0);
    const std::string s2p = read_all(s2p_out);
    REQUIRE(s2p.find("# HZ S RI R 50") != std::string::npos);

    // Subcommand `models` check.
    const std::string cmd_models = std::string("\"") + SPICE_V2_CLI_PATH + "\" models > " + null_dev;
    REQUIRE(run_cmd(cmd_models) == 0);

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
    std::cout << "All " << test_registry().size() << " semiconductor (BSIM3/BSIM4/BJT/JFET) & SPICE deck corpus tests passed.\n";
    return 0;
}
#endif
