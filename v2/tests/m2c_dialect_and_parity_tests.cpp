#include "analysis/analyses.hpp"
#include "analysis/m2c_analyses.hpp"
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
      static void SPICE_CAT(spice_dp_fn_, __LINE__)();                                  \
      static const TestRegistrar SPICE_CAT(spice_dp_reg_, __LINE__)(                    \
          name, &SPICE_CAT(spice_dp_fn_, __LINE__));                                    \
      static void SPICE_CAT(spice_dp_fn_, __LINE__)()
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

using namespace spice;

namespace {

bool approx(double a, double b, double tol) {
    return std::isfinite(a) && std::isfinite(b) && std::abs(a - b) <= tol;
}

std::size_t find_sig(const devices::CompiledPlan& plan, const std::string& id) {
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (plan.signals[i].id == id) return i;
    }
    return plan.signals.size();
}

}  // namespace

TEST_CASE(".FUNC user functions and nested .IF/.ELSEIF/.ELSE/.ENDIF elaborate deterministically and fail closed", "[m2c][func][if]") {
    const std::string deck_hi =
        "Func and If Test\n"
        ".PARAM mode=2 rbase=1k\n"
        ".FUNC par(a, b) {(a * b) / (a + b)}\n"
        ".FUNC scale(x) = x > 1 ? 2 * x : x\n"
        "V1 in 0 DC 12\n"
        "R1 in out {rbase}\n"
        ".IF (mode == 1)\n"
        "R2 out 0 {rbase}\n"
        ".ELSEIF (mode == 2)\n"
        "R2 out 0 {scale(par(2k, 2k))}\n"
        ".ELSE\n"
        "R2 out 0 {3 * rbase}\n"
        ".ENDIF\n"
        ".OP\n"
        ".END\n";

    parser::ParsedDeck d2;
    REQUIRE(parser::parse_spice_deck(deck_hi, d2).ok());
    devices::CompiledPlan plan2;
    REQUIRE(devices::compile_circuit(d2.circuit, plan2).ok());
    analysis::OpResult op2;
    REQUIRE(analysis::run_op(plan2, {}, op2).ok());
    /* mode=2 => par(2k,2k)=1k => scale(1k)=2k => V(out) = 12 * 2k/(1k+2k) = 8.0 V */
    REQUIRE(approx(op2.values[find_sig(plan2, "v(out)")], 8.0, 1.0e-11));

    /* Override mode=3 via param_overrides => hits .ELSE branch (R2=3k => V(out) = 9.0 V). */
    parser::ParseOptions opts3{};
    opts3.param_overrides["mode"] = 3.0;
    parser::ParsedDeck d3;
    REQUIRE(parser::parse_spice_deck(deck_hi, d3, opts3).ok());
    devices::CompiledPlan plan3;
    REQUIRE(devices::compile_circuit(d3.circuit, plan3).ok());
    analysis::OpResult op3;
    REQUIRE(analysis::run_op(plan3, {}, op3).ok());
    REQUIRE(approx(op3.values[find_sig(plan3, "v(out)")], 9.0, 1.0e-11));

    /* Fail closed on cyclic .FUNC recursion. */
    const std::string cyclic_func =
        "Cyclic Func\n"
        ".FUNC f(x) {f(x + 1)}\n"
        "V1 in 0 DC 1\n"
        "R1 in 0 {f(1)}\n"
        ".END\n";
    parser::ParsedDeck bad1;
    REQUIRE(!parser::parse_spice_deck(cyclic_func, bad1).ok());

    /* Fail closed on unclosed .IF block. */
    const std::string unclosed_if =
        "Unclosed If\n"
        ".PARAM flag=1\n"
        ".IF (flag > 0)\n"
        "V1 in 0 DC 1\n"
        "R1 in 0 1k\n"
        ".END\n";
    parser::ParsedDeck bad2;
    REQUIRE(!parser::parse_spice_deck(unclosed_if, bad2).ok());
}

TEST_CASE(".STEP PARAM (linear/LIST/DEC) and .STEP TEMP execute across parser and spice_v2_cli", "[m2c][step][cli]") {
    const std::string deck =
        "Step Sweep Divider\n"
        ".PARAM r2=1k\n"
        "V1 in 0 DC 10\n"
        "R1 in out 1k\n"
        "R2 out 0 {r2}\n"
        ".STEP PARAM r2 LIST 1k 3k 9k\n"
        ".OP\n"
        ".END\n";

    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
    REQUIRE(parsed.steps.size() == 1u);
    std::vector<double> vals;
    REQUIRE(parser::expand_step_values(parsed.steps[0], vals).ok());
    REQUIRE(vals.size() == 3u);
    REQUIRE(approx(vals[0], 1000.0, 1e-9));
    REQUIRE(approx(vals[1], 3000.0, 1e-9));
    REQUIRE(approx(vals[2], 9000.0, 1e-9));

    const double expected_vout[3] = {5.0, 7.5, 9.0};
    for (std::size_t i = 0; i < vals.size(); ++i) {
        parser::ParseOptions sopts{};
        sopts.param_overrides["r2"] = vals[i];
        parser::ParsedDeck sd;
        REQUIRE(parser::parse_spice_deck(deck, sd, sopts).ok());
        devices::CompiledPlan sp;
        REQUIRE(devices::compile_circuit(sd.circuit, sp).ok());
        analysis::OpResult op;
        REQUIRE(analysis::run_op(sp, {}, op).ok());
        REQUIRE(approx(op.values[find_sig(sp, "v(out)")], expected_vout[i], 1.0e-11));
    }

    /* Also test .STEP DEC and .STEP TEMP expansion. */
    parser::StepSpec dec_st{};
    dec_st.kind = parser::StepKind::Param;
    dec_st.target_name = "r2";
    dec_st.sweep_type = parser::StepSweepType::Decade;
    dec_st.start = 100.0;
    dec_st.stop = 10000.0;
    dec_st.step_or_points = 1.0;
    std::vector<double> dec_vals;
    REQUIRE(parser::expand_step_values(dec_st, dec_vals).ok());
    REQUIRE(dec_vals.size() == 3u);
    REQUIRE(approx(dec_vals[1], 1000.0, 1.0e-9));

    /* End-to-end CLI execution of .STEP emitting steps JSON array. */
    namespace fs = std::filesystem;
    const fs::path tmp_dir = fs::temp_directory_path() / "spice_v2_dialect_step_test";
    fs::create_directories(tmp_dir);
    const fs::path cir_path = tmp_dir / "step.cir";
    const fs::path json_path = tmp_dir / "step.json";
    {
        std::ofstream ofs(cir_path);
        ofs << deck;
    }
    const std::string cmd = std::string("\"") + SPICE_V2_CLI_PATH + "\" run \"" +
                            cir_path.string() + "\" --json \"" + json_path.string() + "\"";
#if defined(_WIN32)
    const std::string win_cmd = "\"" + cmd + "\"";
    REQUIRE(std::system(win_cmd.c_str()) == 0);
#else
    REQUIRE(std::system(cmd.c_str()) == 0);
#endif
    std::string js;
    {
        std::ifstream ifs(json_path);
        std::ostringstream oss;
        oss << ifs.rdbuf();
        js = oss.str();
    }
    REQUIRE(js.find("\"step_target\": \"r2\"") != std::string::npos);
    REQUIRE(js.find("\"steps\": [") != std::string::npos);
    REQUIRE(js.find("7.5") != std::string::npos);
    std::error_code rm_ec;
    fs::remove_all(tmp_dir, rm_ec);
}

TEST_CASE("TRNOISE produces deterministic calibrated Gaussian transient noise attenuated by RC filter", "[m2c][trnoise]") {
    const std::string deck_a =
        "TRNOISE RC Filter\n"
        "Vn in 0 DC 0 TRNOISE(0.5 1u 0 0 42)\n"
        "R1 in out 1k\n"
        "C1 out 0 10n\n"
        ".TRAN 1u 500u\n"
        ".END\n";

    parser::ParsedDeck d1, d2;
    REQUIRE(parser::parse_spice_deck(deck_a, d1).ok());
    REQUIRE(parser::parse_spice_deck(deck_a, d2).ok());

    devices::CompiledPlan p1, p2;
    REQUIRE(devices::compile_circuit(d1.circuit, p1).ok());
    REQUIRE(devices::compile_circuit(d2.circuit, p2).ok());

    analysis::TransientOptions topts{};
    topts.step_s = 1.0e-6;
    topts.stop_s = 500.0e-6;
    topts.method = analysis::IntegrationMethod::Trapezoidal;

    analysis::TransientResult r1, r2;
    REQUIRE(analysis::run_transient(p1, topts, r1).ok());
    REQUIRE(analysis::run_transient(p2, topts, r2).ok());
    REQUIRE(r1.samples.size() == r2.samples.size());

    const std::size_t idx_in = find_sig(p1, "v(in)");
    const std::size_t idx_out = find_sig(p1, "v(out)");
    double sum_in2 = 0.0, sum_out2 = 0.0;
    std::size_t n_pts = 0;
    for (std::size_t i = 1u; i < r1.samples.size(); ++i) {
        /* Exact bitwise determinism across identical runs. */
        REQUIRE(r1.samples[i].values[idx_in] == r2.samples[i].values[idx_in]);
        REQUIRE(r1.samples[i].values[idx_out] == r2.samples[i].values[idx_out]);
        const double vin = r1.samples[i].values[idx_in];
        const double vout = r1.samples[i].values[idx_out];
        sum_in2 += vin * vin;
        sum_out2 += vout * vout;
        ++n_pts;
    }
    const double rms_in = std::sqrt(sum_in2 / static_cast<double>(n_pts));
    const double rms_out = std::sqrt(sum_out2 / static_cast<double>(n_pts));
    /* Input grid-sampled Gaussian RMS should match sigma = 0.5 V within ~12%. */
    REQUIRE(rms_in > 0.42 && rms_in < 0.58);
    /* RC low-pass (tau = 10us >> nt = 1us) must strongly attenuate broadband noise RMS! */
    REQUIRE(rms_out < 0.45 * rms_in);
    REQUIRE(rms_out > 0.01);

    /* Different seed produces a distinct noise stream. */
    const std::string deck_b =
        "TRNOISE Seed B\n"
        "Vn in 0 DC 0 TRNOISE(0.5 1u 0 0 99)\n"
        "R1 in 0 1k\n"
        ".TRAN 1u 20u\n"
        ".END\n";
    parser::ParsedDeck db;
    REQUIRE(parser::parse_spice_deck(deck_b, db).ok());
    devices::CompiledPlan pb;
    REQUIRE(devices::compile_circuit(db.circuit, pb).ok());
    analysis::TransientOptions topts_b = topts;
    topts_b.stop_s = 20.0e-6;
    analysis::TransientResult rb;
    REQUIRE(analysis::run_transient(pb, topts_b, rb).ok());
    REQUIRE(std::abs(rb.samples[5].values[find_sig(pb, "v(in)")] - r1.samples[5].values[idx_in]) > 1.0e-3);
}

TEST_CASE("Lossy transmission line (LTRA/TXL) matches exact DC drop and matched AC telegrapher attenuation", "[m2c][ltra][txl]") {
    /*
     * 50-Ohm lossy line: L = 250 nH/m, C = 100 pF/m => Z0 = sqrt(L/C) = 50 Ohm, v = 2e8 m/s.
     * R = 20 Ohm/m, G = 0, LEN = 1 m, NSEGS = 40.
     * Exact DC resistance: R_tot = 20 Ohm => with Rload = 50 Ohm, V(out)/V(in) = 50 / 70.
     * Exact high-frequency low-loss attenuation: alpha ~= R / (2*Z0) = 20 / 100 = 0.2 Np/m
     * => |V(out)/V(in)| ~= exp(-0.2) = 0.81873075 at f = 50 MHz (wavelength = 4 m, dx = 0.025 m << lambda).
     */
    const std::string deck =
        "Lossy LTRA Line\n"
        ".MODEL lossy50 LTRA(R=20 L=250n G=0 C=100p LEN=1.0 NSEGS=40)\n"
        "V1 in 0 DC 7 AC 1\n"
        "O1 in 0 out 0 lossy50\n"
        "Rload out 0 50\n"
        ".OP\n"
        ".AC LIN 1 50Meg 50Meg\n"
        ".END\n";

    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

    analysis::OpResult op;
    REQUIRE(analysis::run_op(plan, {}, op).ok());
    const std::size_t idx_out = find_sig(plan, "v(out)");
    REQUIRE(approx(op.values[idx_out], 7.0 * (50.0 / 70.0), 1.0e-10));

    analysis::AcOptions ac_opts{};
    ac_opts.frequencies_hz = {50.0e6};
    analysis::AcResult ac;
    REQUIRE(analysis::run_ac(plan, ac_opts, ac).ok());
    const double mag = std::abs(ac.points[0].values[idx_out]);
    const double expected_att = std::exp(-0.2); /* ~0.81873 */
    REQUIRE(approx(mag, expected_att, 1.5e-2));
}

TEST_CASE("Coupled transmission line (CPL) produces physical NEXT/FEXT crosstalk and zero crosstalk when uncoupled", "[m2c][cpl]") {
    /* Coupled 2-conductor line driven by a fast 1V step on aggressor line 1, victim line 2 terminated in 50 Ohm both ends. */
    const std::string cpl_deck =
        "Coupled CPL Crosstalk\n"
        ".MODEL pair CPL(R=5 L=300n LM=90n C=120p CM=30p G=0 GM=0 LEN=0.2 NSEGS=16)\n"
        "Vagg drv 0 DC 0 PULSE(0 1 0 0.2n 0.2n 5n 20n)\n"
        "Rs drv a_in 50\n"
        "R_near v_in 0 50\n"
        "P1 a_in v_in 0 a_out v_out 0 pair\n"
        "R_far_a a_out 0 50\n"
        "R_far_v v_out 0 50\n"
        ".TRAN 0.05n 4n\n"
        ".END\n";

    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(cpl_deck, parsed).ok());
    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

    analysis::TransientOptions topts{};
    topts.step_s = 0.05e-9;
    topts.stop_s = 4.0e-9;
    topts.method = analysis::IntegrationMethod::Trapezoidal;
    analysis::TransientResult tr;
    REQUIRE(analysis::run_transient(plan, topts, tr).ok());

    const std::size_t idx_aout = find_sig(plan, "v(a_out)");
    const std::size_t idx_vin = find_sig(plan, "v(v_in)");
    const std::size_t idx_vout = find_sig(plan, "v(v_out)");
    double peak_agg = 0.0, peak_next = 0.0, peak_fext = 0.0;
    for (const auto& s : tr.samples) {
        peak_agg = std::max(peak_agg, std::abs(s.values[idx_aout]));
        peak_next = std::max(peak_next, std::abs(s.values[idx_vin]));
        peak_fext = std::max(peak_fext, std::abs(s.values[idx_vout]));
    }
    REQUIRE(peak_agg > 0.4);
    /* Physical coupling (k_L = 0.30, k_C = 0.25) induces clear non-zero NEXT and FEXT. */
    REQUIRE(peak_next > 0.02);
    REQUIRE(peak_fext > 0.005);

    /* Uncoupled control (LM=0, CM=0) must produce identically zero victim voltage! */
    const std::string uncoupled_deck =
        "Uncoupled CPL Control\n"
        ".MODEL pair0 CPL(R=5 L=300n LM=0 C=120p CM=0 G=0 GM=0 LEN=0.2 NSEGS=8)\n"
        "Vagg drv 0 DC 0 PULSE(0 1 0 0.2n 0.2n 5n 20n)\n"
        "Rs drv a_in 50\n"
        "R_near v_in 0 50\n"
        "P1 a_in v_in 0 a_out v_out 0 pair0\n"
        "R_far_a a_out 0 50\n"
        "R_far_v v_out 0 50\n"
        ".END\n";
    parser::ParsedDeck p0;
    REQUIRE(parser::parse_spice_deck(uncoupled_deck, p0).ok());
    devices::CompiledPlan pl0;
    REQUIRE(devices::compile_circuit(p0.circuit, pl0).ok());
    analysis::TransientResult tr0;
    REQUIRE(analysis::run_transient(pl0, topts, tr0).ok());
    const std::size_t i_vin0 = find_sig(pl0, "v(v_in)");
    double max_v0 = 0.0;
    for (const auto& s : tr0.samples) max_v0 = std::max(max_v0, std::abs(s.values[i_vin0]));
    REQUIRE(max_v0 < 1.0e-12);
}

TEST_CASE("Spectre dialect subset parses parameters, BSIM4 models, subckts, and solves accurately", "[m2c][spectre]") {
    const std::string spectre_deck =
        "// Spectre CMOS Inverter & RC Load\n"
        "simulator lang=spectre\n"
        "parameters vdd_val=1.2 rload=10k\n"
        "model nch bsim4 type=n vth0=0.35 toxe=2.2n u0=420 vsat=1.1e5\n"
        "model pch bsim4 type=p vth0=-0.35 toxe=2.2n u0=160 vsat=0.9e5\n"
        "subckt inv (in out vdd gnd)\n"
        "  Mn (out in gnd gnd) nch w=1u l=65n\n"
        "  Mp (out in vdd vdd) pch w=2.2u l=65n\n"
        "ends inv\n"
        "Vdd (vdd 0) vsource dc=vdd_val\n"
        "Vin (in 0) vsource dc=0\n"
        "X1 (in out vdd 0) inv\n"
        "R1 (out 0) resistor r=rload\n"
        "dc1 dc dev=Vin start=0 stop=1.2 step=0.2\n";

    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(spectre_deck, parsed).ok());
    REQUIRE(parsed.detected_dialect == parser::Dialect::Spectre);
    REQUIRE(parsed.analyses.size() == 1u);
    REQUIRE(std::holds_alternative<parser::DcCommand>(parsed.analyses[0]));

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());
    const auto& dc_cmd = std::get<parser::DcCommand>(parsed.analyses[0]);
    analysis::DcSweepOptions opts{};
    opts.source_id = dc_cmd.source_id;
    opts.start = dc_cmd.start;
    opts.stop = dc_cmd.stop;
    opts.step = dc_cmd.step;
    analysis::DcSweepResult res;
    REQUIRE(analysis::run_dc_sweep(plan, opts, res).ok());
    REQUIRE(res.points.size() == 7u);
    const std::size_t idx_out = find_sig(plan, "v(out)");
    /* Vin=0 -> output high (~1.2V), Vin=1.2 -> output low (~0V). */
    REQUIRE(res.points.front().values[idx_out] > 1.15);
    REQUIRE(res.points.back().values[idx_out] < 0.05);
}

TEST_CASE("IEEE 1481 SPEF parasitic extraction annotates R/C and shifts AC pole accurately", "[m2c][spef]") {
    /* Ideal divider with R1=1k, R2=1k has flat AC gain = 0.5 at 1 MHz (no caps). */
    const std::string base_deck =
        "SPEF Base Divider\n"
        "V1 in 0 DC 0 AC 1\n"
        "R1 in out 1k\n"
        "R2 out 0 1k\n"
        ".END\n";

    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(base_deck, parsed).ok());

    /* Annotate SPEF adding C_out = 1 / (2*pi*500*1e6) ~= 318.309886 pF on net 'out' (exact -3 dB at 1 MHz!)
     * plus a parasitic RC tap branch and coupling cap. */
    const std::string spef =
        "*SPEF \"IEEE 1481-1999\"\n"
        "*DESIGN \"divider\"\n"
        "*C_UNIT 1 PF\n"
        "*R_UNIT 1 OHM\n"
        "*NAME_MAP\n"
        "*1 out\n"
        "*2 in\n"
        "*D_NET *1 318.309886\n"
        "*CAP\n"
        "1 *1:1 318.309886\n"
        "2 *1:2 10.0\n"
        "*RES\n"
        "1 *1:1 *1:2 100.0\n"
        "*END\n";

    parser::SpefSummary summary{};
    REQUIRE(parser::parse_spef_and_annotate(spef, parsed.circuit, summary).ok());
    REQUIRE(summary.nets_annotated == 1u);
    REQUIRE(summary.capacitors_added == 2u);
    REQUIRE(summary.resistors_added == 1u);
    REQUIRE(approx(summary.total_capacitance_f, 328.309886e-12, 1.0e-15));
    REQUIRE(approx(summary.total_resistance_ohms, 100.0, 1.0e-12));

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());
    analysis::AcOptions ac_opts{};
    ac_opts.frequencies_hz = {1.0e6};
    analysis::AcResult ac;
    REQUIRE(analysis::run_ac(plan, ac_opts, ac).ok());
    const double mag = std::abs(ac.points[0].values[find_sig(plan, "v(out)")]);
    /* Without SPEF: mag = 0.5. With 318.31 pF + 10pF tap on Rth = 500 Ohm at 1 MHz: mag ~= 0.5 / sqrt(2) ~= 0.35. */
    REQUIRE(mag > 0.32 && mag < 0.37);

    /* Fail closed on negative SPEF resistance. */
    const std::string bad_spef =
        "*R_UNIT 1 OHM\n"
        "*D_NET out 1.0\n"
        "*RES\n"
        "1 out:1 out:2 -50\n"
        "*END\n";
    parser::SpefSummary bad_sum{};
    REQUIRE(!parser::parse_spef_and_annotate(bad_spef, parsed.circuit, bad_sum).ok());
}

#if !defined(SPICE_V2_USE_CATCH2)
int main() {
    for (const auto& [name, fn] : test_registry()) {
        const int before = g_failed_checks;
        fn();
        if (g_failed_checks == before) {
            std::cout << "PASS: " << name << "\n";
        }
    }
    if (g_failed_checks != 0) {
        std::cerr << g_failed_checks << " check(s) failed.\n";
        return 1;
    }
    std::cout << "All " << test_registry().size() << " M2c dialect & parity tests passed.\n";
    return 0;
}
#endif
