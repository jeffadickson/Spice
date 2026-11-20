#include "analysis/analyses.hpp"
#include "audit/physical_audit.hpp"
#include "core/circuit.hpp"
#include "devices/expr_vm.hpp"
#include "devices/linear_devices.hpp"
#include "osdi/osdi_loader.hpp"
#include "solver/newton.hpp"

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
      static void SPICE_CAT(spice_m2b_fn_, __LINE__)();                                 \
      static const TestRegistrar SPICE_CAT(spice_m2b_reg_, __LINE__)(                   \
          name, &SPICE_CAT(spice_m2b_fn_, __LINE__));                                   \
      static void SPICE_CAT(spice_m2b_fn_, __LINE__)()
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

bool approx(double actual, double expected, double abs_tol, double rel_tol = 1.0e-12) {
    const double diff = std::abs(actual - expected);
    return diff <= abs_tol + rel_tol * std::abs(expected);
}

std::size_t require_node_unknown(const devices::CompiledPlan& /*plan*/, core::NodeId node) {
    const auto unk = devices::CompiledPlan::node_unknown(node);
    return unk.has_value() ? unk->value : 0u;
}

}  // namespace

TEST_CASE("Mutual inductance AC step-up transformer matches coupled impedance equations", "[m2b][mutual]") {
    core::Circuit c;
    c.name = "coupled_transformer";
    const auto n_in = c.get_or_add_node("in");
    const auto n_pri = c.get_or_add_node("pri");
    const auto n_sec = c.get_or_add_node("sec");
    const core::NodeId gnd{0};

    constexpr double r_src = 1.0;
    constexpr double r_load = 1.0e6;
    constexpr double l1 = 1.0e-3;
    constexpr double l2 = 4.0e-3;
    constexpr double k = 0.9;
    constexpr double freq_hz = 1000.0;

    c.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, 0.0, {1.0, 0.0}, std::nullopt});
    c.elements.push_back(core::ResistorSpec{"Rsrc", n_in, n_pri, r_src});
    c.elements.push_back(core::InductorSpec{"L1", n_pri, gnd, l1, std::nullopt});
    c.elements.push_back(core::InductorSpec{"L2", n_sec, gnd, l2, std::nullopt});
    c.elements.push_back(core::ResistorSpec{"Rload", n_sec, gnd, r_load});
    c.elements.push_back(core::MutualInductanceSpec{"K1", "L1", "L2", k});

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(c, plan).ok());

    analysis::AcOptions opts;
    opts.frequencies_hz = {freq_hz};
    opts.common.enable_audits = false;
    analysis::AcResult res;
    REQUIRE(analysis::run_ac(plan, opts, res).ok());
    REQUIRE(res.points.size() == 1u);

    const double w = 2.0 * std::numbers::pi * freq_hz;
    const double m = k * std::sqrt(l1 * l2);
    const std::complex<double> jw{0.0, w};
    // Exact 2-loop coupled transformer:
    //   (R_src + jw L1) I1 + jw M I2 = 1
    //   jw M I1 + (R_load + jw L2) I2 = 0
    //   V_sec = -R_load * I2  (since L2 current flows from n_sec -> gnd, so Rload current = -I2)
    const std::complex<double> z11 = r_src + jw * l1;
    const std::complex<double> z22 = r_load + jw * l2;
    const std::complex<double> z12 = jw * m;
    const std::complex<double> det = z11 * z22 - z12 * z12;
    const std::complex<double> i2 = -z12 / det;
    const std::complex<double> expected_vsec = -r_load * i2;
    const std::complex<double> expected_vpri = 1.0 - r_src * (z22 / det);

    const auto v_pri = res.points[0].values[require_node_unknown(plan, n_pri)];
    const auto v_sec = res.points[0].values[require_node_unknown(plan, n_sec)];
    REQUIRE(std::abs(v_pri - expected_vpri) < 1.0e-12);
    REQUIRE(std::abs(v_sec - expected_vsec) < 1.0e-12);

    // Near open-circuit secondary: |V_sec / V_pri| ~= k * sqrt(L2 / L1) = 1.8.
    const double ratio = std::abs(v_sec / v_pri);
    REQUIRE(approx(ratio, k * std::sqrt(l2 / l1), 1.0e-4));

    // Negative validation: |k| >= 1 must fail closed.
    core::Circuit bad = c;
    bad.elements.back() = core::MutualInductanceSpec{"K1", "L1", "L2", 1.0};
    REQUIRE(!bad.validate().ok());
}

TEST_CASE("Compiled behavioral expression VM autodiff matches finite differences and solves nonlinear B-sources", "[m2b][expr]") {
    // Direct unit check on CompiledExpr covering every operator, function, suffix, and autodiff.
    devices::CompiledExpr expr;
    const auto resolve_node = [](const std::string& name, std::int32_t& out) -> bool {
        if (name == "0") {
            out = -1;
            return true;
        }
        if (name == "a") {
            out = 0;
            return true;
        }
        if (name == "b") {
            out = 1;
            return true;
        }
        return false;
    };
    const auto resolve_branch = [](const std::string& name, std::uint32_t& out) -> bool {
        if (name == "V1") {
            out = 2;
            return true;
        }
        return false;
    };

    const std::string formula =
        "1.5m * v(a)^2 + 2u * exp(v(a, b)) + sin(v(a)) * cos(v(b)) + tanh(v(b)) + "
        "sqrt(abs(v(a)) + 1.0) + log(v(b) + 2.0) + pow(v(a) + 1.0, 1.5) + "
        "min(v(a), v(b)) + max(v(a), v(b)) + 0.25 * i(V1) + 10k * time - 0.5 * tan(0.2 * v(a))";
    REQUIRE(devices::CompiledExpr::compile(formula, resolve_node, resolve_branch, expr).ok());
    REQUIRE(expr.referenced_unknowns().size() == 3u);

    const std::vector<double> x = {0.7, 0.3, -0.4};
    const double t = 1.2e-6;
    devices::ExprEvalResult base;
    REQUIRE(expr.evaluate(x, t, base).ok());
    REQUIRE(base.partials.size() == 3u);

    const double eps = 1.0e-7;
    for (std::size_t idx = 0; idx < 3u; ++idx) {
        std::vector<double> xp = x;
        std::vector<double> xm = x;
        xp[idx] += eps;
        xm[idx] -= eps;
        devices::ExprEvalResult rp;
        devices::ExprEvalResult rm;
        REQUIRE(expr.evaluate(xp, t, rp).ok());
        REQUIRE(expr.evaluate(xm, t, rm).ok());
        const double fd = (rp.value - rm.value) / (2.0 * eps);
        REQUIRE(approx(base.partials[idx], fd, 1.0e-7, 1.0e-7));
    }

    // Fail-closed negative checks: domain errors and syntax/resolution errors.
    {
        devices::CompiledExpr div0;
        REQUIRE(devices::CompiledExpr::compile("1.0 / v(a)", resolve_node, resolve_branch, div0).ok());
        devices::ExprEvalResult r;
        const std::vector<double> x0 = {0.0, 0.0, 0.0};
        REQUIRE(!div0.evaluate(x0, 0.0, r).ok());
    }
    {
        devices::CompiledExpr bad_log;
        REQUIRE(devices::CompiledExpr::compile("log(v(a))", resolve_node, resolve_branch, bad_log).ok());
        devices::ExprEvalResult r;
        const std::vector<double> xn = {-1.0, 0.0, 0.0};
        REQUIRE(!bad_log.evaluate(xn, 0.0, r).ok());
    }
    {
        devices::CompiledExpr bad_sqrt;
        REQUIRE(devices::CompiledExpr::compile("sqrt(v(a))", resolve_node, resolve_branch, bad_sqrt).ok());
        devices::ExprEvalResult r;
        const std::vector<double> xn = {-0.5, 0.0, 0.0};
        REQUIRE(!bad_sqrt.evaluate(xn, 0.0, r).ok());
    }
    {
        devices::CompiledExpr bad_sym;
        REQUIRE(!devices::CompiledExpr::compile("v(nonexistent) + 1", resolve_node, resolve_branch, bad_sym).ok());
        REQUIRE(!devices::CompiledExpr::compile("1.0 + * 2.0", resolve_node, resolve_branch, bad_sym).ok());
        REQUIRE(!devices::CompiledExpr::compile("5q * v(a)", resolve_node, resolve_branch, bad_sym).ok());
    }

    // Circuit-level nonlinear B_i + B_v solved via Newton OP:
    //   I1 = 6 A into node x; B1 sinks I = v(x)^3 + v(x) -> v(x)^3 + v(x) = 6 => exact v(x) = 1.634365293...
    //   Verify exact root: v^3 + v - 6 = 0; B2 voltage source drives node y = 2*v(x) + 1.
    core::Circuit c;
    c.name = "nonlinear_b_sources";
    const auto nx = c.get_or_add_node("x");
    const auto ny = c.get_or_add_node("y");
    const core::NodeId gnd{0};
    c.elements.push_back(core::CurrentSourceSpec{"I1", gnd, nx, 6.0, {0.0, 0.0}, std::nullopt});
    c.elements.push_back(core::BehavioralSourceSpec{"B1", nx, gnd, false, "v(x)^3 + v(x)"});
    c.elements.push_back(core::BehavioralSourceSpec{"B2", ny, gnd, true, "2.0 * v(x) + 1.0"});
    c.elements.push_back(core::ResistorSpec{"Rload", ny, gnd, 1000.0});

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(c, plan).ok());
    REQUIRE(plan.has_nonlinear);

    analysis::AnalysisCommonOptions opts;
    opts.enable_audits = false;
    analysis::OpResult op;
    REQUIRE(analysis::run_op(plan, opts, op).ok());
    REQUIRE(op.newton.has_value());
    REQUIRE(op.newton->converged);
    REQUIRE(op.newton->final_gmin_siemens == 0.0);

    const double vx = op.values[require_node_unknown(plan, nx)];
    const double vy = op.values[require_node_unknown(plan, ny)];
    REQUIRE(std::abs(vx * vx * vx + vx - 6.0) < 1.0e-12);
    REQUIRE(approx(vy, 2.0 * vx + 1.0, 1.0e-12));
}

TEST_CASE("Built-in SPICE diode OP, DC sweep, AC small-signal capacitance, transient rectifier, and continuation", "[m2b][diode]") {
    core::Circuit c;
    c.name = "diode_r_circuit";
    const auto n_in = c.get_or_add_node("in");
    const auto n_d = c.get_or_add_node("d");
    const core::NodeId gnd{0};

    constexpr double vs = 5.0;
    constexpr double r = 1000.0;
    core::DiodeSpec d;
    d.id = "D1";
    d.p = n_d;
    d.n = gnd;
    d.is_amps = 1.0e-14;
    d.n_emission = 1.0;
    d.vt_volts = 0.025851991;
    d.cjo_farads = 20.0e-12;
    d.vj_volts = 0.8;
    d.m_grading = 0.5;
    d.tt_s = 5.0e-9;
    d.gmin_siemens = 0.0;

    c.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, vs, {1.0, 0.0}, std::nullopt});
    c.elements.push_back(core::ResistorSpec{"R1", n_in, n_d, r});
    c.elements.push_back(d);

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(c, plan).ok());
    REQUIRE(plan.has_nonlinear);

    // 1. DC Operating Point + independent physical audit.
    analysis::AnalysisCommonOptions op_opts;
    op_opts.enable_audits = true;
    analysis::OpResult op;
    REQUIRE(analysis::run_op(plan, op_opts, op).ok());
    REQUIRE(op.newton.has_value());
    REQUIRE(op.newton->converged);
    REQUIRE(op.newton->final_gmin_siemens == 0.0);
    REQUIRE(op.audit.has_value());
    REQUIRE(op.audit->all_checked_passed());

    const double vd = op.values[require_node_unknown(plan, n_d)];
    const double id_expected = d.is_amps * std::expm1(vd / (d.n_emission * d.vt_volts));
    const double ir_expected = (vs - vd) / r;
    REQUIRE(vd > 0.65 && vd < 0.72);
    REQUIRE(approx(id_expected, ir_expected, 1.0e-12, 1.0e-12));

    // 2. Nonlinear DC sweep from -2 V to +2 V (warm-started Newton).
    analysis::DcSweepOptions sw_opts;
    sw_opts.source_id = "V1";
    sw_opts.start = -2.0;
    sw_opts.stop = 2.0;
    sw_opts.step = 0.5;
    sw_opts.common.enable_audits = false;
    analysis::DcSweepResult sw;
    REQUIRE(analysis::run_dc_sweep(plan, sw_opts, sw).ok());
    REQUIRE(sw.points.size() == 9u);
    // At reverse bias V = -2 V, diode current is ~-Is so vd ~= -2 V.
    REQUIRE(approx(sw.points.front().values[require_node_unknown(plan, n_d)], -2.0, 1.0e-9));
    // At forward bias V = +2 V, diode clamps near ~0.66 V.
    const double vd_2v = sw.points.back().values[require_node_unknown(plan, n_d)];
    REQUIRE(vd_2v > 0.6 && vd_2v < 0.72);

    // 3. DC-biased small-signal AC verifying both conductance g_d and depletion+diffusion capacitance C_d.
    analysis::AcOptions ac_opts;
    ac_opts.frequencies_hz = {1.0e3, 1.0e6, 50.0e6};
    ac_opts.common.enable_audits = false;
    analysis::AcResult ac;
    REQUIRE(analysis::run_ac(plan, ac_opts, ac).ok());
    REQUIRE(ac.points.size() == 3u);

    const double nvt = d.n_emission * d.vt_volts;
    const double gd = (d.is_amps / nvt) * std::exp(vd / nvt);
    // Forward-bias depletion capacitance (linearized above fc*vj = 0.5*0.8 = 0.4 V) + transit diff cap:
    const double fc = 0.5;
    const double f1 = std::pow(1.0 - fc, -d.m_grading);
    const double c_dep = d.cjo_farads * f1 * (1.0 + d.m_grading * (vd - fc * d.vj_volts) / ((1.0 - fc) * d.vj_volts));
    const double c_diff = d.tt_s * gd;
    const double c_tot = c_dep + c_diff;
    for (std::size_t i = 0; i < ac_opts.frequencies_hz.size(); ++i) {
        const double w = 2.0 * std::numbers::pi * ac_opts.frequencies_hz[i];
        const std::complex<double> yd{gd, w * c_tot};
        const std::complex<double> expected_vd = (1.0 / r) / (1.0 / r + yd);
        const auto got_vd = ac.points[i].values[require_node_unknown(plan, n_d)];
        REQUIRE(std::abs(got_vd - expected_vd) < 1.0e-11);
    }

    // 4. Stiff continuation stress test: V = 50 V forward diode (Newton must converge with final_gmin == 0).
    core::Circuit stiff = c;
    std::get<core::VoltageSourceSpec>(stiff.elements[0]).dc_volts = 50.0;
    devices::CompiledPlan stiff_plan;
    REQUIRE(devices::compile_circuit(stiff, stiff_plan).ok());
    analysis::OpResult stiff_op;
    REQUIRE(analysis::run_op(stiff_plan, op_opts, stiff_op).ok());
    REQUIRE(stiff_op.newton.has_value());
    REQUIRE(stiff_op.newton->converged);
    REQUIRE(stiff_op.newton->final_gmin_siemens == 0.0);
    REQUIRE(stiff_op.audit.has_value());
    REQUIRE(stiff_op.audit->all_checked_passed());
    const double vd_stiff = stiff_op.values[require_node_unknown(stiff_plan, n_d)];
    REQUIRE(approx(d.is_amps * std::expm1(vd_stiff / nvt), (50.0 - vd_stiff) / r, 1.0e-11, 1.0e-11));

    // 5. Nonlinear transient (Backward Euler and Trapezoidal) half-wave diode rectifier + RC load.
    core::Circuit rect;
    rect.name = "half_wave_rectifier";
    const auto r_in = rect.get_or_add_node("in");
    const auto r_out = rect.get_or_add_node("out");
    core::SineWaveform sine;
    sine.offset = 0.0;
    sine.amplitude = 5.0;
    sine.freq_hz = 1000.0;
    rect.elements.push_back(core::VoltageSourceSpec{"Vin", r_in, gnd, 0.0, {0.0, 0.0}, sine});
    core::DiodeSpec d_rect = d;
    d_rect.p = r_in;
    d_rect.n = r_out;
    d_rect.cjo_farads = 10.0e-12;
    d_rect.tt_s = 1.0e-9;
    rect.elements.push_back(d_rect);
    rect.elements.push_back(core::ResistorSpec{"Rload", r_out, gnd, 1000.0});
    rect.elements.push_back(core::CapacitorSpec{"Cload", r_out, gnd, 10.0e-6, std::nullopt});

    for (auto method : {analysis::IntegrationMethod::BackwardEuler, analysis::IntegrationMethod::Trapezoidal}) {
        devices::CompiledPlan rect_plan;
        REQUIRE(devices::compile_circuit(rect, rect_plan).ok());
        analysis::TransientOptions tr_opts;
        tr_opts.step_s = 25.0e-6;
        tr_opts.stop_s = 1.0e-3;
        tr_opts.method = method;
        tr_opts.startup = analysis::StartupMode::OperatingPoint;
        tr_opts.common.enable_audits = false;
        analysis::TransientResult tr;
        REQUIRE(analysis::run_transient(rect_plan, tr_opts, tr).ok());
        REQUIRE(tr.samples.size() >= 40u);
        REQUIRE(tr.total_newton_iterations > 0u);

        const std::size_t out_idx = require_node_unknown(rect_plan, r_out);
        double v_peak = -1e9;
        double v_end = tr.samples.back().values[out_idx];
        for (const auto& s : tr.samples) {
            v_peak = std::max(v_peak, s.values[out_idx]);
        }
        // Positive half-cycle charges output capacitor to ~5 V - V_diode (~4.1-4.4 V).
        REQUIRE(v_peak > 4.0 && v_peak < 4.5);
        // During negative half-cycle, diode blocks and RC (tau = 10 ms) holds capacitor > 3.5 V.
        REQUIRE(v_end > 3.5 && v_end < v_peak);
    }
}

TEST_CASE("Generic OSDI 0.3 dynamic loader executes compiled shared library in OP, AC, and transient and fails closed on bad inputs", "[m2b][osdi]") {
    const std::string good_path = SPICE_TEST_OSDI_DIODE_PATH;
    const std::string bad_abi_path = SPICE_TEST_OSDI_BAD_ABI_PATH;

    // 1. Direct loader inspection on real compiled test_osdi_diode shared library.
    std::shared_ptr<osdi::OsdiLibrary> lib;
    REQUIRE(osdi::OsdiLibrary::load(good_path, lib).ok());
    REQUIRE(lib != nullptr);
    REQUIRE(lib->abi_major() == 0u);
    REQUIRE(lib->abi_minor() == 3u);
    const auto names = lib->model_names();
    REQUIRE(names.size() == 1u);
    REQUIRE(names[0] == "osdi_test_diode");

    // 2. End-to-end R + OSDI diode circuit with custom model & instance parameters.
    constexpr double vs = 3.3;
    constexpr double r = 500.0;
    constexpr double is_param = 2.0e-14;
    constexpr double vt_param = 0.026;
    constexpr double cjo_param = 50.0e-12;
    constexpr double area_param = 2.5;

    core::Circuit c;
    c.name = "osdi_diode_circuit";
    const auto n_in = c.get_or_add_node("in");
    const auto n_d = c.get_or_add_node("d");
    const core::NodeId gnd{0};
    c.elements.push_back(core::VoltageSourceSpec{"V1", n_in, gnd, vs, {1.0, 0.0}, std::nullopt});
    c.elements.push_back(core::ResistorSpec{"R1", n_in, n_d, r});
    core::OsdiInstanceSpec inst;
    inst.id = "X1";
    inst.library_path = good_path;
    inst.model_name = "osdi_test_diode";
    inst.terminals = {n_d, gnd};
    inst.model_params = {{"is", is_param}, {"vt", vt_param}, {"cjo", cjo_param}};
    inst.instance_params = {{"area", area_param}};
    c.elements.push_back(inst);

    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(c, plan).ok());
    REQUIRE(plan.has_nonlinear);
    REQUIRE(plan.has_osdi);

    // 2a. DC OP matches exact Shockley equation with area scaling.
    analysis::AnalysisCommonOptions op_opts;
    op_opts.enable_audits = false;
    analysis::OpResult op;
    REQUIRE(analysis::run_op(plan, op_opts, op).ok());
    REQUIRE(op.newton.has_value());
    REQUIRE(op.newton->converged);
    REQUIRE(op.newton->final_gmin_siemens == 0.0);

    const double vd = op.values[require_node_unknown(plan, n_d)];
    const double id_exact = area_param * is_param * std::expm1(vd / vt_param);
    REQUIRE(approx(id_exact, (vs - vd) / r, 1.0e-12, 1.0e-12));

    // 2b. DC-biased small-signal AC matches exact parallel g_d + j*w*(area*cjo).
    analysis::AcOptions ac_opts;
    ac_opts.frequencies_hz = {1.0e4, 10.0e6, 100.0e6};
    ac_opts.common.enable_audits = false;
    analysis::AcResult ac;
    REQUIRE(analysis::run_ac(plan, ac_opts, ac).ok());
    REQUIRE(ac.points.size() == 3u);

    const double gd = (area_param * is_param / vt_param) * std::exp(vd / vt_param);
    const double cd = area_param * cjo_param;
    for (std::size_t i = 0; i < ac_opts.frequencies_hz.size(); ++i) {
        const double w = 2.0 * std::numbers::pi * ac_opts.frequencies_hz[i];
        const std::complex<double> yd{gd, w * cd};
        const std::complex<double> expected = (1.0 / r) / (1.0 / r + yd);
        const auto got = ac.points[i].values[require_node_unknown(plan, n_d)];
        REQUIRE(std::abs(got - expected) < 1.0e-11);
    }

    // 2c. Nonlinear transient step response on OSDI diode matches built-in DiodeSpec to < 1e-10!
    core::PulseWaveform pulse;
    pulse.v1 = 0.0;
    pulse.v2 = 3.3;
    pulse.delay_s = 1.0e-9;
    pulse.rise_s = 2.0e-9;
    pulse.fall_s = 2.0e-9;
    pulse.width_s = 20.0e-9;
    pulse.period_s = 0.0;

    core::Circuit c_osdi_tr = c;
    std::get<core::VoltageSourceSpec>(c_osdi_tr.elements[0]).dc_volts = 0.0;
    std::get<core::VoltageSourceSpec>(c_osdi_tr.elements[0]).transient_waveform = pulse;

    core::Circuit c_builtin_tr;
    c_builtin_tr.name = "builtin_diode_ref";
    const auto b_in = c_builtin_tr.get_or_add_node("in");
    const auto b_d = c_builtin_tr.get_or_add_node("d");
    c_builtin_tr.elements.push_back(core::VoltageSourceSpec{"V1", b_in, gnd, 0.0, {0.0, 0.0}, pulse});
    c_builtin_tr.elements.push_back(core::ResistorSpec{"R1", b_in, b_d, r});
    core::DiodeSpec d_ref;
    d_ref.id = "D1";
    d_ref.p = b_d;
    d_ref.n = gnd;
    d_ref.is_amps = area_param * is_param;
    d_ref.n_emission = 1.0;
    d_ref.vt_volts = vt_param;
    d_ref.cjo_farads = 0.0;
    d_ref.gmin_siemens = 0.0;
    c_builtin_tr.elements.push_back(d_ref);
    c_builtin_tr.elements.push_back(core::CapacitorSpec{"Cpar", b_d, gnd, area_param * cjo_param, std::nullopt});

    devices::CompiledPlan plan_osdi_tr;
    devices::CompiledPlan plan_ref_tr;
    REQUIRE(devices::compile_circuit(c_osdi_tr, plan_osdi_tr).ok());
    REQUIRE(devices::compile_circuit(c_builtin_tr, plan_ref_tr).ok());

    analysis::TransientOptions tr_opts;
    tr_opts.step_s = 0.5e-9;
    tr_opts.stop_s = 20.0e-9;
    tr_opts.method = analysis::IntegrationMethod::Trapezoidal;
    tr_opts.startup = analysis::StartupMode::OperatingPoint;
    tr_opts.common.enable_audits = false;

    analysis::TransientResult tr_osdi;
    analysis::TransientResult tr_ref;
    REQUIRE(analysis::run_transient(plan_osdi_tr, tr_opts, tr_osdi).ok());
    REQUIRE(analysis::run_transient(plan_ref_tr, tr_opts, tr_ref).ok());
    REQUIRE(tr_osdi.samples.size() == tr_ref.samples.size());
    const std::size_t idx_osdi = require_node_unknown(plan_osdi_tr, n_d);
    const std::size_t idx_ref = require_node_unknown(plan_ref_tr, b_d);
    for (std::size_t i = 0; i < tr_osdi.samples.size(); ++i) {
        REQUIRE(approx(tr_osdi.samples[i].values[idx_osdi], tr_ref.samples[i].values[idx_ref], 1.0e-10, 1.0e-10));
    }

    // 3. Fail-closed negative OSDI checks (ARCH-MOD-002).
    {
        std::shared_ptr<osdi::OsdiLibrary> bad_lib;
        const auto diag = osdi::OsdiLibrary::load(bad_abi_path, bad_lib);
        REQUIRE(!diag.ok());
        REQUIRE(diag.code == core::StatusCode::Unsupported);
    }
    {
        std::shared_ptr<osdi::OsdiLibrary> missing_lib;
        const auto diag = osdi::OsdiLibrary::load("/nonexistent/path/missing.osdi", missing_lib);
        REQUIRE(!diag.ok());
    }
    {
        core::Circuit bad_param = c;
        std::get<core::OsdiInstanceSpec>(bad_param.elements[2]).model_params.emplace_back("unknown_param", 1.0);
        devices::CompiledPlan bad_plan;
        const auto diag = devices::compile_circuit(bad_param, bad_plan);
        REQUIRE(!diag.ok());
        REQUIRE(diag.code == core::StatusCode::InvalidArgument);
    }
    {
        core::Circuit bad_terms = c;
        std::get<core::OsdiInstanceSpec>(bad_terms.elements[2]).terminals = {n_d};
        devices::CompiledPlan bad_plan;
        const auto diag = devices::compile_circuit(bad_terms, bad_plan);
        REQUIRE(!diag.ok());
        REQUIRE(diag.code == core::StatusCode::InvalidArgument);
    }
    {
        core::Circuit bad_model = c;
        std::get<core::OsdiInstanceSpec>(bad_model.elements[2]).model_name = "does_not_exist";
        devices::CompiledPlan bad_plan;
        const auto diag = devices::compile_circuit(bad_model, bad_plan);
        REQUIRE(!diag.ok());
        REQUIRE(diag.code == core::StatusCode::InvalidArgument);
    }
    {
        // Model setup_model returns FATAL when is <= 0.
        core::Circuit bad_val = c;
        std::get<core::OsdiInstanceSpec>(bad_val.elements[2]).model_params = {{"is", -1.0}};
        devices::CompiledPlan bad_plan;
        const auto diag = devices::compile_circuit(bad_val, bad_plan);
        REQUIRE(!diag.ok());
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
    std::cout << "All " << test_registry().size() << " M2b nonlinear & OSDI tests passed.\n";
    return 0;
}
#endif
