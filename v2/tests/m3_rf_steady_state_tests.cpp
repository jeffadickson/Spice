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
    std::cout << "All " << test_registry().size() << " M3 RF steady-state (.HB/.PSS) tests passed.\n";
    return 0;
}
#endif
