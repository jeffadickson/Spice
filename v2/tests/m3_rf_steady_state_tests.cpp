#include "analysis/analyses.hpp"
#include "analysis/m2c_analyses.hpp"
#include "analysis/rf_steady_state.hpp"
#include "core/circuit.hpp"
#include "devices/linear_devices.hpp"

#include <cmath>
#include <complex>
#include <iostream>
#include <numbers>
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
      static void SPICE_CAT(spice_m3_fn_, __LINE__)();                                  \
      static const TestRegistrar SPICE_CAT(spice_m3_reg_, __LINE__)(                    \
          name, &SPICE_CAT(spice_m3_fn_, __LINE__));                                    \
      static void SPICE_CAT(spice_m3_fn_, __LINE__)()
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

std::size_t find_signal(const devices::CompiledPlan& plan, const std::string& id) {
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (plan.signals[i].id == id) return i;
    }
    return plan.signals.size();
}

}  // namespace

TEST_CASE(".HB Harmonic Balance matches exact linear AC phasor and nonlinear cubic/diode harmonics", "[m3][hb]") {
    const core::NodeId gnd{0};

    // 1. Linear RC lowpass driven by 2 V peak sine at f0 = 1 kHz:
    //    V(in, t) = 2 * sin(2*pi*f0*t) -> phasor a1 - j*b1 = 0 - j*2.
    //    Exact V(out) phasor = (0 - j*2) / (1 + j*w0*R*C), zero DC and zero higher harmonics.
    {
        constexpr double r = 1000.0;
        constexpr double cap = 1.0e-6;
        constexpr double f0 = 1000.0;
        core::Circuit c;
        c.name = "hb_rc";
        const auto n_in = c.get_or_add_node("in");
        const auto n_out = c.get_or_add_node("out");
        core::SineWaveform sine{0.0, 2.0, f0, 0.0, 0.0, 0.0};
        c.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, 0.0, {0.0, 0.0}, sine});
        c.elements.push_back(core::ResistorSpec{"R1", n_in, n_out, r});
        c.elements.push_back(core::CapacitorSpec{"C1", n_out, gnd, cap, std::nullopt});

        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(c, plan).ok());

        analysis::HbOptions opts{};
        opts.fundamental_hz = f0;
        opts.harmonics = 5u;
        analysis::HbResult hb;
        REQUIRE(analysis::run_hb(plan, opts, hb).ok());
        REQUIRE(hb.converged);
        REQUIRE(hb.final_residual_norm < 1.0e-11);

        const std::size_t idx_out = find_signal(plan, "v(out)");
        const double w0 = 2.0 * std::numbers::pi * f0;
        const std::complex<double> vin_phasor{0.0, -2.0}; /* 2*sin(wt) = Re((0 - j2) e^{jwt}) */
        const std::complex<double> expected_v1 = vin_phasor / std::complex<double>{1.0, w0 * r * cap};
        REQUIRE(std::abs(hb.phasors[idx_out][0]) < 1.0e-12);
        REQUIRE(std::abs(hb.phasors[idx_out][1] - expected_v1) < 1.0e-11);
        for (std::size_t h = 2u; h <= 5u; ++h) {
            REQUIRE(std::abs(hb.phasors[idx_out][h]) < 1.0e-11);
        }
    }

    // 2. Nonlinear diode + RC rectifier/harmonic generator: cross-verify .HB against .PSS shooting!
    {
        constexpr double f0 = 1.0e3;
        core::Circuit nl;
        nl.name = "hb_vs_pss_diode";
        const auto n_in = nl.get_or_add_node("in");
        const auto n_d = nl.get_or_add_node("d");
        core::SineWaveform sine{0.5, 1.5, f0, 0.0, 0.0, 0.0};
        nl.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, 0.5, {0.0, 0.0}, sine});
        nl.elements.push_back(core::ResistorSpec{"R1", n_in, n_d, 1000.0});
        nl.elements.push_back(core::CapacitorSpec{"C1", n_d, gnd, 0.1e-6, std::nullopt});
        core::DiodeSpec d{};
        d.id = "D1";
        d.p = n_d;
        d.n = gnd;
        d.is_amps = 1.0e-14;
        d.vt_volts = 0.025851991;
        d.cjo_farads = 10.0e-12;
        d.gmin_siemens = 0.0;
        nl.elements.push_back(d);

        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(nl, plan).ok());
        const std::size_t idx_d = find_signal(plan, "v(d)");

        analysis::HbOptions hb_opts{};
        hb_opts.fundamental_hz = f0;
        hb_opts.harmonics = 11u;
        hb_opts.residual_tol = 1.0e-10;
        analysis::HbResult hb;
        REQUIRE(analysis::run_hb(plan, hb_opts, hb).ok());
        REQUIRE(hb.converged);

        analysis::PssOptions pss_opts{};
        pss_opts.fundamental_hz = f0;
        pss_opts.steps_per_period = 256u;
        pss_opts.method = analysis::IntegrationMethod::Trapezoidal;
        pss_opts.periodic_tol = 1.0e-10;
        analysis::PssResult pss;
        REQUIRE(analysis::run_pss(plan, pss_opts, pss).ok());
        REQUIRE(pss.converged);
        REQUIRE(pss.periodicity_error < 1.0e-9);
        REQUIRE(pss.shooting_iterations <= 12u);

        // Extract harmonics from .PSS steady-state waveform via .FOUR and compare against .HB!
        analysis::TransientResult pss_tr{};
        pss_tr.samples = pss.waveform;
        analysis::FourierResult four;
        REQUIRE(analysis::compute_fourier(pss_tr, idx_d, f0, 5u, four).ok());

        REQUIRE(approx(hb.phasors[idx_d][0].real(), four.dc_component, 2.0e-3));
        REQUIRE(approx(std::abs(hb.phasors[idx_d][1]), four.harmonics[1].magnitude, 2.0e-3));
        REQUIRE(approx(std::abs(hb.phasors[idx_d][2]), four.harmonics[2].magnitude, 2.0e-3));
        REQUIRE(approx(std::abs(hb.phasors[idx_d][3]), four.harmonics[3].magnitude, 2.0e-3));
    }
}

TEST_CASE(".PSS Shooting rapidly converges slow-RC diode peak detector and executes over real OSDI 0.3 model", "[m3][pss][osdi]") {
    const core::NodeId gnd{0};

    // 1. High-time-constant peak detector (tau = R*C = 20ms = 20 periods at f0 = 1 kHz):
    //    Would take > 100 transient cycles to settle, but exact monodromy shooting converges in <= 12 iterations!
    {
        constexpr double f0 = 1000.0;
        core::Circuit c;
        c.name = "slow_rc_peak_detector";
        const auto n_in = c.get_or_add_node("in");
        const auto n_a = c.get_or_add_node("a");
        const auto n_out = c.get_or_add_node("out");
        core::SineWaveform sine{0.0, 5.0, f0, 0.0, 0.0, 0.0};
        c.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, 0.0, {0.0, 0.0}, sine});
        c.elements.push_back(core::ResistorSpec{"Rs", n_in, n_a, 10.0});
        core::DiodeSpec d{};
        d.id = "D1";
        d.p = n_a;
        d.n = n_out;
        d.is_amps = 1.0e-14;
        d.vt_volts = 0.025851991;
        d.gmin_siemens = 0.0;
        c.elements.push_back(d);
        c.elements.push_back(core::ResistorSpec{"Rload", n_out, gnd, 2000.0});
        c.elements.push_back(core::CapacitorSpec{"Cload", n_out, gnd, 10.0e-6, std::nullopt});

        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(c, plan).ok());

        analysis::PssOptions opts{};
        opts.fundamental_hz = f0;
        opts.steps_per_period = 64u;
        opts.method = analysis::IntegrationMethod::BackwardEuler;
        opts.periodic_tol = 1.0e-8;
        opts.max_shooting_iterations = 25u;
        analysis::PssResult pss;
        const auto d_pss = analysis::run_pss(plan, opts, pss);
        if (!d_pss.ok()) std::cerr << "PSS error: " << d_pss.message << "\n";
        REQUIRE(d_pss.ok());
        REQUIRE(pss.converged);
        REQUIRE(pss.periodicity_error < 1.0e-8);
        REQUIRE(pss.shooting_iterations <= 15u);

        const std::size_t idx_out = find_signal(plan, "v(out)");
        const double v0 = pss.waveform.front().values[idx_out];
        const double vT = pss.waveform.back().values[idx_out];
        REQUIRE(approx(v0, vT, 1.0e-7));
        // Steady-state rectified capacitor voltage sits near 5V - V_diode (~4.0..4.5V).
        REQUIRE(v0 > 3.8 && v0 < 4.6);
    }

    // 2. Real compiled OSDI 0.3 diode (libtest_osdi_diode.so) solved directly in .HB and .PSS!
    {
        constexpr double f0 = 1.0e6;
        core::Circuit oc;
        oc.name = "osdi_rf_mixer_diode";
        const auto n_in = oc.get_or_add_node("in");
        const auto n_d = oc.get_or_add_node("d");
        core::SineWaveform sine{0.4, 0.8, f0, 0.0, 0.0, 0.0};
        oc.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, 0.4, {0.0, 0.0}, sine});
        oc.elements.push_back(core::ResistorSpec{"R1", n_in, n_d, 500.0});
        core::OsdiInstanceSpec inst{};
        inst.id = "X1";
        inst.library_path = SPICE_TEST_OSDI_DIODE_PATH;
        inst.model_name = "osdi_test_diode";
        inst.terminals = {n_d, gnd};
        inst.model_params = {{"is", 1.0e-14}, {"vt", 0.025851991}, {"cjo", 20.0e-12}};
        inst.instance_params = {{"area", 1.5}};
        oc.elements.push_back(inst);

        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(oc, plan).ok());
        REQUIRE(plan.has_osdi);
        const std::size_t idx_d = find_signal(plan, "v(d)");

        analysis::HbOptions hb_opts{};
        hb_opts.fundamental_hz = f0;
        hb_opts.harmonics = 9u;
        analysis::HbResult hb;
        REQUIRE(analysis::run_hb(plan, hb_opts, hb).ok());
        REQUIRE(hb.converged);
        // Nonlinear diode generates real 2nd harmonic distortion (> 5 mV).
        REQUIRE(std::abs(hb.phasors[idx_d][2]) > 5.0e-3);

        analysis::PssOptions pss_opts{};
        pss_opts.fundamental_hz = f0;
        pss_opts.steps_per_period = 128u;
        pss_opts.method = analysis::IntegrationMethod::Trapezoidal;
        analysis::PssResult pss;
        REQUIRE(analysis::run_pss(plan, pss_opts, pss).ok());
        REQUIRE(pss.converged);

        analysis::TransientResult pss_tr{};
        pss_tr.samples = pss.waveform;
        analysis::FourierResult four;
        REQUIRE(analysis::compute_fourier(pss_tr, idx_d, f0, 3u, four).ok());
        REQUIRE(approx(hb.phasors[idx_d][0].real(), four.dc_component, 2.0e-3));
        REQUIRE(approx(std::abs(hb.phasors[idx_d][1]), four.harmonics[1].magnitude, 2.0e-3));
        REQUIRE(approx(std::abs(hb.phasors[idx_d][2]), four.harmonics[2].magnitude, 2.0e-3));
    }

    // 3. Fail-closed negative check: T-line in .HB/.PSS returns Unsupported cleanly.
    {
        core::Circuit tl;
        tl.name = "tline_hb_reject";
        const auto p1 = tl.get_or_add_node("p1");
        const auto p2 = tl.get_or_add_node("p2");
        tl.elements.push_back(core::VoltageSourceSpec{"V1", p1, gnd, 0.0, {0.0, 0.0}, std::nullopt});
        tl.elements.push_back(core::TransmissionLineSpec{"T1", p1, gnd, p2, gnd, 50.0, 1.0e-9});
        tl.elements.push_back(core::ResistorSpec{"R1", p2, gnd, 50.0});
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(tl, plan).ok());

        analysis::HbOptions hb_opts{};
        hb_opts.fundamental_hz = 1.0e6;
        analysis::HbResult hb;
        const auto dh = analysis::run_hb(plan, hb_opts, hb);
        REQUIRE(!dh.ok());
        REQUIRE(dh.code == core::StatusCode::Unsupported);

        analysis::PssOptions pss_opts{};
        pss_opts.fundamental_hz = 1.0e6;
        analysis::PssResult pss;
        const auto dp = analysis::run_pss(plan, pss_opts, pss);
        REQUIRE(!dp.ok());
        REQUIRE(dp.code == core::StatusCode::Unsupported);
    }
}

TEST_CASE(".PAC frequency conversion and .PXF adjoint reciprocity match on a pumped diode mixer", "[m3][pac][pxf]") {
    const core::NodeId gnd{0};
    constexpr double f_lo = 1.0e6;
    constexpr double f_rf = 100.0e3;

    // Pumped diode mixer: LO drives diode conductance g(t) periodically at f_lo = 1 MHz;
    // small-signal RF input Vrf at f_rf = 100 kHz mixes into sidebands f_rf + k*f_lo!
    core::Circuit c;
    c.name = "pumped_diode_mixer";
    const auto n_lo = c.get_or_add_node("lo");
    const auto n_rf = c.get_or_add_node("rf");
    const auto n_out = c.get_or_add_node("out");

    core::SineWaveform lo_wave{0.4, 0.8, f_lo, 0.0, 0.0, 0.0};
    c.elements.push_back(core::VoltageSourceSpec{"Vlo", n_lo, gnd, 0.4, {0.0, 0.0}, lo_wave});
    c.elements.push_back(core::VoltageSourceSpec{"Vrf", n_rf, gnd, 0.0, {1.0, 0.0}, std::nullopt});
    c.elements.push_back(core::ResistorSpec{"Rlo", n_lo, n_out, 500.0});
    c.elements.push_back(core::ResistorSpec{"Rrf", n_rf, n_out, 500.0});
    c.elements.push_back(core::CapacitorSpec{"Cload", n_out, gnd, 20.0e-12, std::nullopt});
    core::DiodeSpec d{};
    d.id = "Dmix";
    d.p = n_out;
    d.n = gnd;
    d.is_amps = 1.0e-14;
    d.vt_volts = 0.025851991;
    d.cjo_farads = 5.0e-12;
    d.gmin_siemens = 0.0;
    c.elements.push_back(d);

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(c, plan).ok());
    const std::size_t idx_out = find_signal(plan, "v(out)");

    analysis::HbOptions hb_opts{};
    hb_opts.fundamental_hz = f_lo;
    hb_opts.harmonics = 7u;
    hb_opts.residual_tol = 1.0e-10;
    analysis::HbResult hb;
    REQUIRE(analysis::run_hb(plan, hb_opts, hb).ok());
    REQUIRE(hb.converged);

    analysis::PacOptions pac_opts{};
    pac_opts.small_signal_freq_hz = f_rf;
    pac_opts.input_source_id = "Vrf";
    analysis::PacResult pac;
    REQUIRE(analysis::run_pac(plan, hb, pac_opts, pac).ok());
    REQUIRE(pac.sidebands.size() == 2u * hb_opts.harmonics + 1u);

    auto find_pac_sb = [&](int k) -> const analysis::PacSideband* {
        for (const auto& sb : pac.sidebands) {
            if (sb.sideband_index == k) return &sb;
        }
        return nullptr;
    };

    const auto* sb0 = find_pac_sb(0);
    const auto* sbp1 = find_pac_sb(+1);
    const auto* sbm1 = find_pac_sb(-1);
    REQUIRE(sb0 != nullptr && sbp1 != nullptr && sbm1 != nullptr);
    REQUIRE(approx(sbp1->frequency_hz, f_rf + f_lo, 1.0e-6));
    REQUIRE(approx(sbm1->frequency_hz, f_rf - f_lo, 1.0e-6));

    // Real nonlinear frequency conversion: strong passband AND non-zero +/-1 LO mixing sidebands!
    REQUIRE(std::abs(sb0->values[idx_out]) > 0.05);
    REQUIRE(std::abs(sbp1->values[idx_out]) > 0.01);
    REQUIRE(std::abs(sbm1->values[idx_out]) > 0.01);

    // Exact complex adjoint reciprocity check:
    //   .PXF(output at k_out) evaluated for input source "Vrf" at sideband k_in = 0
    //   MUST equal .PAC(input Vrf at k_in = 0) at output sideband k_out to machine precision!
    for (int k_out : {-2, -1, 0, +1, +2}) {
        analysis::PxfOptions pxf_opts{};
        pxf_opts.small_signal_freq_hz = f_rf;
        pxf_opts.out_p = n_out;
        pxf_opts.out_n = gnd;
        pxf_opts.output_sideband = k_out;
        analysis::PxfResult pxf;
        REQUIRE(analysis::run_pxf(plan, hb, pxf_opts, pxf).ok());
        REQUIRE(approx(pxf.output_frequency_hz, f_rf + static_cast<double>(k_out) * f_lo, 1.0e-6));

        bool found = false;
        for (const auto& tr : pxf.transfers) {
            if (tr.source_id == "Vrf" && tr.input_sideband == 0) {
                const auto* sb = find_pac_sb(k_out);
                REQUIRE(sb != nullptr);
                REQUIRE(std::abs(tr.transfer_gain - sb->values[idx_out]) < 1.0e-11);
                found = true;
                break;
            }
        }
        REQUIRE(found);
    }
}

TEST_CASE(".PNOISE matches stationary .NOISE at zero LO and captures cyclostationary shot-noise folding under pump", "[m3][pnoise]") {
    const core::NodeId gnd{0};
    constexpr double f_lo = 1.0e6;
    constexpr double f_out = 50.0e3;
    constexpr double temp_k = 300.15;

    auto build_diode_circuit = [&](double lo_amp) {
        core::Circuit c;
        c.name = "pnoise_diode";
        const auto n_in = c.get_or_add_node("in");
        const auto n_out = c.get_or_add_node("out");
        core::SineWaveform lo{0.7, lo_amp, f_lo, 0.0, 0.0, 0.0};
        c.elements.push_back(core::VoltageSourceSpec{"Vlo", n_in, gnd, 0.7, {1.0, 0.0}, lo});
        c.elements.push_back(core::ResistorSpec{"R1", n_in, n_out, 1000.0});
        c.elements.push_back(core::CapacitorSpec{"C1", n_out, gnd, 50.0e-12, std::nullopt});
        core::DiodeSpec d{};
        d.id = "D1";
        d.p = n_out;
        d.n = gnd;
        d.is_amps = 1.0e-14;
        d.vt_volts = 0.025851991;
        d.gmin_siemens = 0.0;
        c.elements.push_back(d);
        return c;
    };

    // 1. Zero-LO stationary limit: .PNOISE must equal small-signal .NOISE to < 1e-11 relative!
    {
        auto c0 = build_diode_circuit(0.0);
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(c0, plan).ok());
        const auto n_out = core::NodeId{2}; /* nodes: 0="0", 1="in", 2="out" */

        analysis::NoiseOptions n_opts{};
        n_opts.input_source_id = "Vlo";
        n_opts.out_p = n_out;
        n_opts.out_n = gnd;
        n_opts.frequencies_hz = {f_out};
        n_opts.temperature_k = temp_k;
        analysis::NoiseResult nr;
        REQUIRE(analysis::run_noise(plan, n_opts, nr).ok());

        analysis::HbOptions hb_opts{};
        hb_opts.fundamental_hz = f_lo;
        hb_opts.harmonics = 5u;
        analysis::HbResult hb;
        REQUIRE(analysis::run_hb(plan, hb_opts, hb).ok());

        analysis::PnoiseOptions pn_opts{};
        pn_opts.small_signal_freq_hz = f_out;
        pn_opts.out_p = n_out;
        pn_opts.out_n = gnd;
        pn_opts.output_sideband = 0;
        pn_opts.temperature_k = temp_k;
        analysis::PnoiseResult pnr;
        REQUIRE(analysis::run_pnoise(plan, hb, pn_opts, pnr).ok());

        REQUIRE(pnr.onoise_v_per_sqrt_hz > 0.0);
        REQUIRE(approx(pnr.onoise_v_per_sqrt_hz, nr.points[0].onoise_v_per_sqrt_hz, 1.0e-16, 1.0e-11));
    }

    // 2. Strongly pumped diode: cyclostationary noise folding produces finite, positive noise
    //    distinct from the quiescent zero-LO value!
    {
        auto c_pump = build_diode_circuit(0.6);
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(c_pump, plan).ok());
        const auto n_out = core::NodeId{2};

        analysis::HbOptions hb_opts{};
        hb_opts.fundamental_hz = f_lo;
        hb_opts.harmonics = 7u;
        analysis::HbResult hb;
        REQUIRE(analysis::run_hb(plan, hb_opts, hb).ok());

        analysis::PnoiseOptions pn_opts{};
        pn_opts.small_signal_freq_hz = f_out;
        pn_opts.out_p = n_out;
        pn_opts.out_n = gnd;
        pn_opts.output_sideband = 0;
        pn_opts.temperature_k = temp_k;
        analysis::PnoiseResult pnr;
        REQUIRE(analysis::run_pnoise(plan, hb, pn_opts, pnr).ok());
        REQUIRE(std::isfinite(pnr.onoise_v_per_sqrt_hz));
        REQUIRE(pnr.onoise_v_per_sqrt_hz > 1.0e-11);
    }
}

TEST_CASE("Autonomous Oscillator .PSS locks onto Van der Pol LC tank limit cycle and unknown oscillation frequency", "[m3][pss][oscillator]") {
    const core::NodeId gnd{0};
    // Parallel LC tank + cubic Van der Pol negative-resistance B-source:
    //   C * dv/dt + i_L + (-g_neg * v + g_sat * v^3) = 0,  L * di_L/dt = v
    //   L = 10 uH, C = 10 nF => f_0 = 1 / (2*pi*sqrt(L*C)) = 503,292.12 Hz, Z_0 = sqrt(L/C) = 31.62 Ohm.
    //   With g_neg = 0.005 S (epsilon = g_neg * Z_0 = 0.158 << 1) and g_sat = 0.005 S/V^2,
    //   harmonic-balance limit-cycle peak amplitude is A ~= sqrt(4/3) * sqrt(g_neg/g_sat) ~= 1.155 V!
    constexpr double l_val = 10.0e-6;
    constexpr double c_val = 10.0e-9;
    const double f_tank = 1.0 / (2.0 * std::numbers::pi * std::sqrt(l_val * c_val));

    core::Circuit osc;
    osc.name = "vdp_lc_oscillator";
    const auto n_tank = osc.get_or_add_node("tank");
    osc.elements.push_back(core::InductorSpec{"L1", n_tank, gnd, l_val, std::nullopt});
    osc.elements.push_back(core::CapacitorSpec{"C1", n_tank, gnd, c_val, std::nullopt});
    // B_i flows from p -> n: I(tank -> gnd) = -0.005*v(tank) + 0.005*v(tank)^3.
    osc.elements.push_back(core::BehavioralSourceSpec{
        "Bneg", n_tank, gnd, false, "-0.005*v(tank) + 0.005*v(tank)*v(tank)*v(tank)"});

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(osc, plan).ok());
    const std::size_t idx_v = find_signal(plan, "v(tank)");
    const std::size_t idx_il = find_signal(plan, "i(L1)");
    REQUIRE(idx_v < plan.unknown_count());
    REQUIRE(idx_il < plan.unknown_count());

    // Start away from exact period (10% off initial frequency guess!) and at zero-crossing phase anchor v(tank) = 0,
    // with non-zero inductor current i_L(0) ~= A / Z_0 ~= 0.036 A so Newton locks onto the limit cycle!
    analysis::AutonomousPssOptions opts{};
    opts.initial_freq_hz = f_tank * 0.90;
    opts.phase_unknown_index = idx_v;
    opts.phase_target_value = 0.0;
    opts.initial_state.assign(plan.unknown_count(), 0.0);
    opts.initial_state[idx_v] = 0.0;
    opts.initial_state[idx_il] = 0.035;
    opts.steps_per_period = 64u;
    opts.periodic_tol = 1.0e-8;
    opts.max_shooting_iterations = 25u;
    opts.method = analysis::IntegrationMethod::Trapezoidal;

    analysis::AutonomousPssResult res;
    const auto d = analysis::run_autonomous_pss(plan, opts, res);
    if (!d.ok()) std::cerr << "Autonomous PSS error: " << d.message << "\n";
    REQUIRE(d.ok());
    REQUIRE(res.converged);
    REQUIRE(res.periodicity_error < 1.0e-7);

    // Recovered oscillation frequency must be within 1% of ideal LC tank resonance!
    REQUIRE(approx(res.oscillation_freq_hz, f_tank, 0.01 * f_tank));
    REQUIRE(approx(res.waveform.front().values[idx_v], 0.0, 1.0e-7));

    // Confirm genuine self-sustained oscillation (peak amplitude near sqrt(4/3) ~= 1.155 V, not trivial 0).
    double v_peak = 0.0;
    for (const auto& s : res.waveform) {
        v_peak = std::max(v_peak, std::abs(s.values[idx_v]));
    }
    REQUIRE(v_peak > 1.0 && v_peak < 1.35);
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
    std::cout << "All " << test_registry().size() << " M3 RF (.HB/.PSS/.PAC/.PXF/.PNOISE/Oscillator) tests passed.\n";
    return 0;
}
#endif
