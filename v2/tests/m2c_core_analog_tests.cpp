#include "analysis/analyses.hpp"
#include "analysis/m2c_analyses.hpp"
#include "core/circuit.hpp"
#include "devices/linear_devices.hpp"
#include "parser/spice_parser.hpp"

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
      static void SPICE_CAT(spice_m2c_fn_, __LINE__)();                                 \
      static const TestRegistrar SPICE_CAT(spice_m2c_reg_, __LINE__)(                   \
          name, &SPICE_CAT(spice_m2c_fn_, __LINE__));                                   \
      static void SPICE_CAT(spice_m2c_fn_, __LINE__)()
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

bool approx(double actual, double expected, double abs_tol, double rel_tol = 1.0e-10) {
    return std::abs(actual - expected) <= abs_tol + rel_tol * std::abs(expected);
}

std::size_t find_signal(const devices::CompiledPlan& plan, const std::string& id) {
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (plan.signals[i].id == id) return i;
    }
    return plan.signals.size();
}

}  // namespace

TEST_CASE("Smooth voltage-controlled switch (S) parses from SPICE3f5 and matches exact log-conductance curve", "[m2c][switch]") {
    const std::string deck = R"(Smooth Switch Test
.MODEL SW1 SW(VT=2.5 VH=0.2 RON=10 ROFF=1MEG)
Vsrc in 0 10
Vctrl ctrl 0 0
S1 in out ctrl 0 SW1
Rload out 0 1k
.END
)";
    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(deck, parsed).ok());

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());
    REQUIRE(plan.has_nonlinear);

    analysis::DcSweepOptions sw{};
    sw.source_id = "Vctrl";
    sw.start = 0.0;
    sw.stop = 5.0;
    sw.step = 2.5;
    sw.common.enable_audits = false;
    analysis::DcSweepResult res;
    REQUIRE(analysis::run_dc_sweep(plan, sw, res).ok());
    REQUIRE(res.points.size() == 3u);

    const std::size_t idx_out = find_signal(plan, "v(out)");
    REQUIRE(idx_out < plan.signals.size());

    // Vctrl = 0 V -> switch OFF (R_off = 1M): V(out) = 10 * 1k / (1M + 1k).
    REQUIRE(approx(res.points[0].values[idx_out], 10.0 * 1000.0 / (1.0e6 + 1000.0), 1.0e-7, 1.0e-7));
    // Vctrl = 2.5 V = VT -> geometric mean R_mid = sqrt(10 * 1e6) = sqrt(1e7).
    const double r_mid = std::sqrt(10.0 * 1.0e6);
    REQUIRE(approx(res.points[1].values[idx_out], 10.0 * 1000.0 / (r_mid + 1000.0), 1.0e-10, 1.0e-10));
    // Vctrl = 5 V -> switch ON (R_on = 10): V(out) = 10 * 1k / (10 + 1k).
    REQUIRE(approx(res.points[2].values[idx_out], 10.0 * 1000.0 / (10.0 + 1000.0), 1.0e-7, 1.0e-7));
}

TEST_CASE(".TF computes exact transfer gain, Rin, and Rout on linear amplifier and nonlinear diode stages", "[m2c][tf]") {
    // 1. Resistor divider + VCVS amplifier with output resistance:
    //    V1 -> R1(2k) -> mid -> R2(3k) -> 0; E1(gain=4) -> amp -> Rout(100) -> out -> Rload(400) -> 0.
    //    H = (3/5) * 4 * (400 / 500) = 1.92 V/V, Rin = 5k Ohm, Rout = 100 || 400 = 80 Ohm.
    core::Circuit c;
    c.name = "tf_amp";
    const auto n_in = c.get_or_add_node("in");
    const auto n_mid = c.get_or_add_node("mid");
    const auto n_amp = c.get_or_add_node("amp");
    const auto n_out = c.get_or_add_node("out");
    const core::NodeId gnd{0};
    c.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, 1.0, {0.0, 0.0}, std::nullopt});
    c.elements.push_back(core::ResistorSpec{"R1", n_in, n_mid, 2000.0});
    c.elements.push_back(core::ResistorSpec{"R2", n_mid, gnd, 3000.0});
    c.elements.push_back(core::VcvsSpec{"E1", n_amp, gnd, n_mid, gnd, 4.0});
    c.elements.push_back(core::ResistorSpec{"Rout", n_amp, n_out, 100.0});
    c.elements.push_back(core::ResistorSpec{"Rload", n_out, gnd, 400.0});

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(c, plan).ok());
    analysis::TfOptions opts{};
    opts.out_p = n_out;
    opts.out_n = gnd;
    opts.input_source_id = "V1";
    analysis::TfResult tf;
    REQUIRE(analysis::run_tf(plan, opts, tf).ok());
    REQUIRE(approx(tf.transfer_gain, 1.92, 1.0e-12));
    REQUIRE(approx(tf.input_resistance, 5000.0, 1.0e-10));
    REQUIRE(approx(tf.output_resistance, 80.0, 1.0e-10));

    // 2. Nonlinear diode stage: V1(5V) -> R(1k) -> d -> D1 -> 0.
    //    Small-signal Rout = R || (1/gd), gain = (1/gd) / (R + 1/gd), Rin = R + 1/gd.
    core::Circuit dc;
    dc.name = "tf_diode";
    const auto d_in = dc.get_or_add_node("in");
    const auto d_out = dc.get_or_add_node("d");
    dc.elements.push_back(core::VoltageSourceSpec{"V1", d_in, gnd, 5.0, {0.0, 0.0}, std::nullopt});
    dc.elements.push_back(core::ResistorSpec{"R1", d_in, d_out, 1000.0});
    core::DiodeSpec ds{};
    ds.id = "D1";
    ds.p = d_out;
    ds.n = gnd;
    ds.is_amps = 1.0e-14;
    ds.vt_volts = 0.025851991;
    ds.gmin_siemens = 0.0;
    dc.elements.push_back(ds);

    devices::CompiledPlan dplan;
    REQUIRE(devices::compile_circuit(dc, dplan).ok());
    analysis::TfOptions dopts{};
    dopts.out_p = d_out;
    dopts.out_n = gnd;
    dopts.input_source_id = "V1";
    analysis::TfResult dtf;
    REQUIRE(analysis::run_tf(dplan, dopts, dtf).ok());

    const double vd = dtf.operating_point[find_signal(dplan, "v(d)")];
    const double gd = (ds.is_amps / ds.vt_volts) * std::exp(vd / ds.vt_volts);
    const double rd = 1.0 / gd;
    REQUIRE(approx(dtf.output_resistance, (1000.0 * rd) / (1000.0 + rd), 1.0e-11));
    REQUIRE(approx(dtf.input_resistance, 1000.0 + rd, 1.0e-9));
    REQUIRE(approx(dtf.transfer_gain, rd / (1000.0 + rd), 1.0e-11));
}

TEST_CASE(".NOISE matches exact Johnson-Nyquist kT/C thermal noise and diode Schottky shot noise", "[m2c][noise]") {
    constexpr double kB = 1.380649e-23;
    constexpr double q = 1.602176634e-19;
    constexpr double temp_k = 300.0;

    // 1. RC lowpass thermal noise: S_v(f) = 4*kB*T*R / (1 + (2*pi*f*R*C)^2), integral = sqrt(kB*T/C).
    constexpr double r = 1000.0;
    constexpr double cap = 1.0e-9;
    core::Circuit rc;
    rc.name = "rc_noise";
    const auto n_in = rc.get_or_add_node("in");
    const auto n_out = rc.get_or_add_node("out");
    const core::NodeId gnd{0};
    rc.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, 0.0, {1.0, 0.0}, std::nullopt});
    rc.elements.push_back(core::ResistorSpec{"R1", n_in, n_out, r});
    rc.elements.push_back(core::CapacitorSpec{"C1", n_out, gnd, cap, std::nullopt});

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(rc, plan).ok());

    analysis::NoiseOptions nopts{};
    nopts.out_p = n_out;
    nopts.out_n = gnd;
    nopts.input_source_id = "V1";
    nopts.temperature_k = temp_k;
    // Dense log-spaced grid from 1 Hz to 100 MHz (fc = 159.15 kHz) for accurate kT/C integral.
    for (int i = 0; i <= 400; ++i) {
        nopts.frequencies_hz.push_back(std::pow(10.0, 8.0 * static_cast<double>(i) / 400.0));
    }
    analysis::NoiseResult nres;
    REQUIRE(analysis::run_noise(plan, nopts, nres).ok());
    REQUIRE(nres.points.size() == nopts.frequencies_hz.size());

    const double expected_flat_inoise = std::sqrt(4.0 * kB * temp_k * r);
    for (const auto& pt : nres.points) {
        const double w = 2.0 * std::numbers::pi * pt.frequency_hz;
        const double expected_onoise = std::sqrt((4.0 * kB * temp_k * r) / (1.0 + (w * r * cap) * (w * r * cap)));
        REQUIRE(approx(pt.onoise_v_per_sqrt_hz, expected_onoise, 1.0e-20, 1.0e-11));
        REQUIRE(approx(pt.inoise_v_per_sqrt_hz, expected_flat_inoise, 1.0e-20, 1.0e-11));
    }
    // Integrated RMS thermal noise across RC filter matches sqrt(kB * T / C) within 0.3% (finite band truncation).
    const double kt_over_c_rms = std::sqrt(kB * temp_k / cap);
    REQUIRE(approx(nres.integrated_onoise_vrms, kt_over_c_rms, 0.0, 3.0e-3));

    // 2. Diode shot + resistor thermal noise at DC bias.
    core::Circuit dc;
    dc.name = "diode_noise";
    const auto d_in = dc.get_or_add_node("in");
    const auto d_out = dc.get_or_add_node("d");
    dc.elements.push_back(core::VoltageSourceSpec{"V1", d_in, gnd, 5.0, {1.0, 0.0}, std::nullopt});
    dc.elements.push_back(core::ResistorSpec{"R1", d_in, d_out, 1000.0});
    core::DiodeSpec ds{};
    ds.id = "D1";
    ds.p = d_out;
    ds.n = gnd;
    ds.is_amps = 1.0e-14;
    ds.vt_volts = 0.025851991;
    ds.gmin_siemens = 0.0;
    dc.elements.push_back(ds);

    devices::CompiledPlan dplan;
    REQUIRE(devices::compile_circuit(dc, dplan).ok());
    analysis::OpResult op;
    analysis::AnalysisCommonOptions op_opts{};
    op_opts.enable_audits = false;
    REQUIRE(analysis::run_op(dplan, op_opts, op).ok());
    const double vd = op.values[find_signal(dplan, "v(d)")];
    const double id = ds.is_amps * std::expm1(vd / ds.vt_volts);
    const double gd = (ds.is_amps / ds.vt_volts) * std::exp(vd / ds.vt_volts);
    const double req = 1.0 / (1.0 / 1000.0 + gd);
    const double expected_psd = req * req * (4.0 * kB * temp_k / 1000.0 + 2.0 * q * id);

    analysis::NoiseOptions dnopts{};
    dnopts.out_p = d_out;
    dnopts.out_n = gnd;
    dnopts.input_source_id = "V1";
    dnopts.temperature_k = temp_k;
    dnopts.frequencies_hz = {1000.0};
    analysis::NoiseResult dnres;
    REQUIRE(analysis::run_noise(dplan, dnopts, dnres).ok());
    REQUIRE(approx(dnres.points[0].onoise_v_per_sqrt_hz, std::sqrt(expected_psd), 1.0e-20, 1.0e-11));
}

TEST_CASE(".SENS adjoint DC sensitivities match central finite differences on nonlinear diode divider", "[m2c][sens]") {
    core::Circuit c;
    c.name = "sens_diode";
    const auto n_in = c.get_or_add_node("in");
    const auto n_d = c.get_or_add_node("d");
    const core::NodeId gnd{0};
    c.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, 5.0, {0.0, 0.0}, std::nullopt});
    c.elements.push_back(core::ResistorSpec{"R1", n_in, n_d, 1000.0});
    c.elements.push_back(core::ResistorSpec{"R2", n_d, gnd, 2000.0});
    core::DiodeSpec ds{};
    ds.id = "D1";
    ds.p = n_d;
    ds.n = gnd;
    ds.is_amps = 1.0e-14;
    ds.vt_volts = 0.025851991;
    ds.gmin_siemens = 0.0;
    c.elements.push_back(ds);

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(c, plan).ok());
    analysis::SensOptions sopts{};
    sopts.out_p = n_d;
    sopts.out_n = gnd;
    analysis::SensResult sres;
    REQUIRE(analysis::run_sens(plan, sopts, sres).ok());
    REQUIRE(sres.entries.size() == 4u);

    auto eval_vd = [&](const core::Circuit& mod) -> double {
        devices::CompiledPlan p;
        if (!devices::compile_circuit(mod, p).ok()) return std::numeric_limits<double>::quiet_NaN();
        analysis::OpResult op;
        analysis::AnalysisCommonOptions co{};
        co.enable_audits = false;
        if (!analysis::run_op(p, co, op).ok()) return std::numeric_limits<double>::quiet_NaN();
        return op.values[find_signal(p, "v(d)")];
    };

    for (const auto& entry : sres.entries) {
        const double eps = std::max(1.0e-18, std::abs(entry.nominal_value) * 1.0e-6);
        core::Circuit cp = c, cm = c;
        for (auto& e : cp.elements) {
            if (core::element_id(e) != entry.element_id) continue;
            if (auto* v = std::get_if<core::VoltageSourceSpec>(&e)) v->dc_volts += eps;
            else if (auto* r = std::get_if<core::ResistorSpec>(&e)) r->ohms += eps;
            else if (auto* d = std::get_if<core::DiodeSpec>(&e)) d->is_amps += eps;
        }
        for (auto& e : cm.elements) {
            if (core::element_id(e) != entry.element_id) continue;
            if (auto* v = std::get_if<core::VoltageSourceSpec>(&e)) v->dc_volts -= eps;
            else if (auto* r = std::get_if<core::ResistorSpec>(&e)) r->ohms -= eps;
            else if (auto* d = std::get_if<core::DiodeSpec>(&e)) d->is_amps -= eps;
        }
        const double vp = eval_vd(cp);
        const double vm = eval_vd(cm);
        REQUIRE(std::isfinite(vp) && std::isfinite(vm));
        const double fd = (vp - vm) / (2.0 * eps);
        REQUIRE(approx(entry.d_out_d_param, fd, 1.0e-7, 1.0e-5));
    }
}

TEST_CASE(".SP 2-port S-parameters match exact analytical scattering matrix and format Touchstone v1.1", "[m2c][sp]") {
    // 2-port series resistor R = 50 Ohm between port 1 and port 2, plus shunt C = 10 pF at port 2, Z0 = 50 Ohm.
    constexpr double z0 = 50.0;
    constexpr double r_ser = 50.0;
    constexpr double c_shunt = 10.0e-12;
    core::Circuit c;
    c.name = "two_port_rc";
    const auto p1 = c.get_or_add_node("p1");
    const auto p2 = c.get_or_add_node("p2");
    const core::NodeId gnd{0};
    c.elements.push_back(core::ResistorSpec{"R1", p1, p2, r_ser});
    c.elements.push_back(core::CapacitorSpec{"C1", p2, gnd, c_shunt, std::nullopt});

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(c, plan).ok());

    analysis::SpOptions sopts{};
    sopts.ports = {{p1, gnd, z0}, {p2, gnd, z0}};
    sopts.frequencies_hz = {0.0, 100.0e6, 1.0e9};
    analysis::SpResult sp;
    REQUIRE(analysis::run_sp(plan, sopts, sp).ok());
    REQUIRE(sp.port_count == 2u);
    REQUIRE(sp.points.size() == 3u);

    for (const auto& pt : sp.points) {
        const double w = 2.0 * std::numbers::pi * pt.frequency_hz;
        // Exact ABCD -> S for series R followed by shunt Y = j*w*C:
        //   A = 1 + j*w*C*R, B = R, C_abcd = j*w*C, D = 1
        const std::complex<double> y{0.0, w * c_shunt};
        const std::complex<double> a = 1.0 + r_ser * y;
        const std::complex<double> b = r_ser;
        const std::complex<double> cc = y;
        const std::complex<double> d = 1.0;
        const std::complex<double> denom = a + b / z0 + cc * z0 + d;
        const std::complex<double> s11_exp = (a + b / z0 - cc * z0 - d) / denom;
        const std::complex<double> s12_exp = 2.0 * (a * d - b * cc) / denom;
        const std::complex<double> s21_exp = 2.0 / denom;
        const std::complex<double> s22_exp = (-a + b / z0 - cc * z0 + d) / denom;

        const auto s11 = pt.s_matrix[0];
        const auto s12 = pt.s_matrix[1];
        const auto s21 = pt.s_matrix[2];
        const auto s22 = pt.s_matrix[3];
        REQUIRE(std::abs(s11 - s11_exp) < 1.0e-12);
        REQUIRE(std::abs(s12 - s12_exp) < 1.0e-12);
        REQUIRE(std::abs(s21 - s21_exp) < 1.0e-12);
        REQUIRE(std::abs(s22 - s22_exp) < 1.0e-12);
        // Passive reciprocal network checks:
        REQUIRE(std::abs(s12 - s21) < 1.0e-12);
        REQUIRE(std::norm(s11) + std::norm(s21) <= 1.0 + 1.0e-12);
    }

    const std::string s2p = analysis::format_touchstone(sp);
    REQUIRE(s2p.find("# HZ S RI R 50") != std::string::npos);
    REQUIRE(s2p.find("100000000") != std::string::npos);
}

TEST_CASE(".FOUR and .FFT recover exact harmonic magnitudes, 10% THD, and spectral peaks", "[m2c][four][fft]") {
    // Synthesize exact transient samples: y(t) = 0.5 + 2.0*sin(2*pi*1k*t) + 0.2*sin(2*pi*3k*t)
    // Exact THD = 100 * (0.2 / 2.0) = 10.0%.
    analysis::TransientResult tr{};
    constexpr double f0 = 1000.0;
    constexpr std::size_t n_samp = 2049u;
    constexpr double t_stop = 4.0e-3; /* 4 full cycles */
    tr.samples.reserve(n_samp);
    for (std::size_t i = 0; i < n_samp; ++i) {
        const double t = t_stop * static_cast<double>(i) / static_cast<double>(n_samp - 1u);
        const double y = 0.5 + 2.0 * std::sin(2.0 * std::numbers::pi * f0 * t) +
                         0.2 * std::sin(2.0 * std::numbers::pi * 3.0 * f0 * t);
        tr.samples.push_back(analysis::TransientSample{t, {y}});
    }

    analysis::FourierResult four;
    REQUIRE(analysis::compute_fourier(tr, 0u, f0, 5u, four).ok());
    REQUIRE(approx(four.dc_component, 0.5, 1.0e-4));
    REQUIRE(approx(four.harmonics[1].magnitude, 2.0, 1.0e-3));
    REQUIRE(approx(four.harmonics[2].magnitude, 0.0, 1.0e-3));
    REQUIRE(approx(four.harmonics[3].magnitude, 0.2, 1.0e-3));
    REQUIRE(approx(four.thd_percent, 10.0, 0.05));

    analysis::FftResult fft;
    REQUIRE(analysis::compute_fft(tr, 0u, 512u, false, fft).ok());
    // 4 full cycles over 512 samples -> fundamental at bin 4 (1 kHz), 3rd harmonic at bin 12 (3 kHz).
    REQUIRE(approx(fft.frequencies_hz[4], 1000.0, 1.0e-9));
    REQUIRE(approx(fft.magnitude[4], 2.0, 1.0e-2));
    REQUIRE(approx(fft.frequencies_hz[12], 3000.0, 1.0e-9));
    REQUIRE(approx(fft.magnitude[12], 0.2, 1.0e-2));
}

TEST_CASE(".MC deterministic seeded Monte Carlo produces reproducible statistics matching analytic variance", "[m2c][mc]") {
    core::Circuit c;
    c.name = "mc_divider";
    const auto n_in = c.get_or_add_node("in");
    const auto n_out = c.get_or_add_node("out");
    const core::NodeId gnd{0};
    c.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, 10.0, {0.0, 0.0}, std::nullopt});
    c.elements.push_back(core::ResistorSpec{"R1", n_in, n_out, 1000.0});
    c.elements.push_back(core::ResistorSpec{"R2", n_out, gnd, 1000.0});

    analysis::MonteCarloOptions opts{};
    opts.trials = 64u;
    opts.seed = 1337u;
    opts.tolerances = {
        {"R1", 0.05, analysis::ToleranceDistribution::Gaussian},
        {"R2", 0.05, analysis::ToleranceDistribution::Gaussian}};
    opts.common.enable_audits = false;

    analysis::MonteCarloResult r1, r2;
    REQUIRE(analysis::run_monte_carlo_op(c, opts, r1).ok());
    REQUIRE(analysis::run_monte_carlo_op(c, opts, r2).ok());
    REQUIRE(r1.completed_trials == 64u);
    REQUIRE(r1.trial_op_values == r2.trial_op_values);

    // Find v(out) stats: nominal 5.0 V, analytic 1-sigma ~= 5.0 * 0.5 * sqrt(2) * 0.05 ~= 0.1768 V.
    bool found = false;
    for (const auto& st : r1.stats) {
        if (st.signal_id == "v(out)") {
            found = true;
            REQUIRE(st.mean > 4.8 && st.mean < 5.2);
            REQUIRE(st.std_dev > 0.10 && st.std_dev < 0.28);
            REQUIRE(st.min_value < st.mean && st.max_value > st.mean);
        }
    }
    REQUIRE(found);
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
    std::cout << "All " << test_registry().size() << " M2c core analog tests passed.\n";
    return 0;
}
#endif
