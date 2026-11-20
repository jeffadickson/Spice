#include "analysis/analyses.hpp"
#include "audit/physical_audit.hpp"
#include "core/circuit.hpp"
#include "devices/linear_devices.hpp"
#include "linalg/solver.hpp"
#include "spice/capi.h"

#include <chrono>
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
      static void SPICE_CAT(spice_m2a_fn_, __LINE__)();                                 \
      static const TestRegistrar SPICE_CAT(spice_m2a_reg_, __LINE__)(                   \
          name, &SPICE_CAT(spice_m2a_fn_, __LINE__));                                   \
      static void SPICE_CAT(spice_m2a_fn_, __LINE__)()
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

using namespace spice::core;
using namespace spice::devices;
using namespace spice::linalg;
using namespace spice::analysis;
using namespace spice::audit;

namespace {

std::size_t find_signal(const CompiledPlan& plan, const std::string& name) {
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (plan.signals[i].id == name) return i;
    }
    return plan.signals.size();
}

Circuit make_rc_lowpass(double r_ohms, double c_farads, double v_dc, Complex v_ac) {
    Circuit ckt{};
    ckt.name = "rc_lowpass";
    const NodeId n0 = NodeId{0};
    const NodeId n_in = ckt.get_or_add_node("in");
    const NodeId n_out = ckt.get_or_add_node("out");
    ckt.elements.push_back(VoltageSourceSpec{"V1", n_in, n0, v_dc, v_ac, std::nullopt});
    ckt.elements.push_back(ResistorSpec{"R1", n_in, n_out, r_ohms});
    ckt.elements.push_back(CapacitorSpec{"C1", n_out, n0, c_farads, std::nullopt});
    return ckt;
}

}  // namespace

TEST_CASE("RC lowpass matches analytic OP, AC, and BE/Trapezoidal transient on DenseLu and SparseLu", "[m2a]") {
    constexpr double R = 1000.0;
    constexpr double C = 1.0e-6;
    const double tau = R * C;

    /* 1. OP + AC across both independent linear solvers. */
    Circuit ckt = make_rc_lowpass(R, C, 5.0, Complex{1.0, 0.0});
    CompiledPlan plan{};
    REQUIRE(compile_circuit(ckt, plan).ok());
    const std::size_t idx_out = find_signal(plan, "v(out)");
    const std::size_t idx_iv = find_signal(plan, "i(V1)");
    REQUIRE(idx_out < plan.signals.size());
    REQUIRE(idx_iv < plan.signals.size());

    for (BackendPreference pref : {BackendPreference::DenseLu, BackendPreference::SparseLu}) {
        AnalysisCommonOptions op_opts{};
        op_opts.backend = pref;
        op_opts.enable_audits = true;
        OpResult op{};
        REQUIRE(run_op(plan, op_opts, op).ok());
        REQUIRE(std::abs(op.values[idx_out] - 5.0) < 1e-13);
        REQUIRE(std::abs(op.values[idx_iv]) < 1e-14);
        REQUIRE(op.audit.has_value());
        REQUIRE(op.audit->all_checked_passed());

        AcOptions ac_opts{};
        ac_opts.common.backend = pref;
        ac_opts.common.enable_audits = true;
        ac_opts.frequencies_hz = {10.0, 1.0 / (2.0 * std::numbers::pi * tau), 1000.0, 10000.0};
        AcResult ac{};
        REQUIRE(run_ac(plan, ac_opts, ac).ok());
        REQUIRE(ac.solver_stats.symbolic_analyses == 1u);
        REQUIRE(ac.audit.has_value());
        REQUIRE(ac.audit->all_checked_passed());
        for (const auto& pt : ac.points) {
            const double w = 2.0 * std::numbers::pi * pt.frequency_hz;
            const Complex expected = Complex{1.0, 0.0} / Complex{1.0, w * tau};
            REQUIRE(std::abs(pt.values[idx_out] - expected) < 1e-12);
        }
    }

    /* 2. Step response from UIC (C starts at 0V, V1=1V): verify exact discrete BE & Trap recurrences. */
    Circuit step_ckt = make_rc_lowpass(R, C, 1.0, Complex{0.0, 0.0});
    std::get<CapacitorSpec>(step_ckt.elements[2]).ic_volts = 0.0;
    CompiledPlan step_plan{};
    REQUIRE(compile_circuit(step_ckt, step_plan).ok());
    const std::size_t s_out = find_signal(step_plan, "v(out)");

    constexpr double h = 1.0e-4; /* h/tau = 0.1 */
    constexpr double stop = 1.0e-3; /* 10 exact steps */
    {
        TransientOptions topts{};
        topts.step_s = h;
        topts.stop_s = stop;
        topts.method = IntegrationMethod::BackwardEuler;
        topts.startup = StartupMode::Uic;
        topts.common.backend = BackendPreference::SparseLu;
        topts.common.enable_audits = true;
        TransientResult tr{};
        REQUIRE(run_transient(step_plan, topts, tr).ok());
        REQUIRE(tr.samples.size() == 11u);
        /* Exact numeric factor reuse: constant step h -> 1 numeric factorization for all 10 steps! */
        REQUIRE(tr.solver_stats.numeric_factorizations == 1u);
        REQUIRE(tr.solver_stats.solves == 10u);
        REQUIRE(tr.audit.has_value());
        REQUIRE(tr.audit->all_checked_passed());
        const double decay_be = 1.0 / (1.0 + h / tau);
        for (std::size_t k = 0; k < tr.samples.size(); ++k) {
            const double expected = 1.0 - std::pow(decay_be, static_cast<double>(k));
            REQUIRE(std::abs(tr.samples[k].values[s_out] - expected) < 1e-12);
        }
    }
    {
        TransientOptions topts{};
        topts.step_s = h;
        topts.stop_s = stop;
        topts.method = IntegrationMethod::Trapezoidal;
        topts.startup = StartupMode::Uic;
        topts.common.backend = BackendPreference::SparseLu;
        topts.common.enable_audits = true;
        TransientResult tr{};
        REQUIRE(run_transient(step_plan, topts, tr).ok());
        REQUIRE(tr.samples.size() == 11u);
        REQUIRE(tr.solver_stats.numeric_factorizations == 1u);
        REQUIRE(tr.audit.has_value());
        REQUIRE(tr.audit->all_checked_passed());
        const double decay_trap = (1.0 - 0.5 * h / tau) / (1.0 + 0.5 * h / tau);
        for (std::size_t k = 0; k < tr.samples.size(); ++k) {
            const double expected = 1.0 - std::pow(decay_trap, static_cast<double>(k));
            REQUIRE(std::abs(tr.samples[k].values[s_out] - expected) < 1e-12);
        }
    }
}

TEST_CASE("Series RLC matches analytic complex impedance and UIC underdamped ringdown", "[m2a]") {
    constexpr double R = 10.0;
    constexpr double L = 1.0e-3;
    constexpr double C = 1.0e-6;
    /* Resonant frequency f0 = 1/(2*pi*sqrt(LC)) = 5032.9212104487 Hz. */
    const double f0 = 1.0 / (2.0 * std::numbers::pi * std::sqrt(L * C));

    Circuit ckt{};
    ckt.name = "series_rlc";
    const NodeId n0{0}, n1 = ckt.get_or_add_node("n1"), n2 = ckt.get_or_add_node("n2"),
                 n3 = ckt.get_or_add_node("n3");
    ckt.elements.push_back(VoltageSourceSpec{"V1", n1, n0, 0.0, Complex{1.0, 0.0}, std::nullopt});
    ckt.elements.push_back(ResistorSpec{"R1", n1, n2, R});
    ckt.elements.push_back(InductorSpec{"L1", n2, n3, L, 0.0});
    ckt.elements.push_back(CapacitorSpec{"C1", n3, n0, C, 1.0}); /* 1V initial charge */

    CompiledPlan plan{};
    REQUIRE(compile_circuit(ckt, plan).ok());
    const std::size_t idx_n3 = find_signal(plan, "v(n3)");
    const std::size_t idx_il = find_signal(plan, "i(L1)");

    /* AC at exact resonance: Z_total = R (reactances cancel), so i(L1) = 1/R + 0j. */
    AcOptions ac_opts{};
    ac_opts.frequencies_hz = {f0 * 0.5, f0, f0 * 2.0};
    ac_opts.common.backend = BackendPreference::SparseLu;
    ac_opts.common.enable_audits = true;
    AcResult ac{};
    REQUIRE(run_ac(plan, ac_opts, ac).ok());
    REQUIRE(std::abs(ac.points[1].values[idx_il] - Complex{1.0 / R, 0.0}) < 1e-12);
    REQUIRE(ac.audit->all_checked_passed());

    /* Compare SparseLu vs DenseLu on AC across all frequencies. */
    ac_opts.common.backend = BackendPreference::DenseLu;
    AcResult ac_dense{};
    REQUIRE(run_ac(plan, ac_opts, ac_dense).ok());
    for (std::size_t i = 0; i < ac.points.size(); ++i) {
        for (std::size_t j = 0; j < plan.unknown_count(); ++j) {
            REQUIRE(std::abs(ac.points[i].values[j] - ac_dense.points[i].values[j]) < 1e-12);
        }
    }

    /* UIC transient: V1=0, C=1V, L=0A -> analytical underdamped cosine decay! */
    TransientOptions topts{};
    topts.step_s = 1.0e-6;
    topts.stop_s = 2.0e-4;
    topts.method = IntegrationMethod::Trapezoidal;
    topts.startup = StartupMode::Uic;
    topts.common.backend = BackendPreference::SparseLu;
    topts.common.enable_audits = true;
    TransientResult tr{};
    REQUIRE(run_transient(plan, topts, tr).ok());
    REQUIRE(tr.audit->all_checked_passed());

    /* Analytic solution: alpha = R/(2L) = 5000, wd = sqrt(1/(LC) - alpha^2). */
    const double alpha = R / (2.0 * L);
    const double wd = std::sqrt(1.0 / (L * C) - alpha * alpha);
    const double t_end = tr.samples.back().time_s;
    const double v_analytic =
        std::exp(-alpha * t_end) * (std::cos(wd * t_end) + (alpha / wd) * std::sin(wd * t_end));
    REQUIRE(std::abs(tr.samples.back().values[idx_n3] - v_analytic) < 2.0e-3);
}

TEST_CASE("All four controlled sources (VCCS, VCVS, CCCS, CCVS) agree with exact DC/AC math", "[m2a]") {
    /* Circuit:
     *   V1(in, 0) = 2V -> R1(in, 0) = 1k -> i(V1) = -2 mA (leaves V1+ into R1 -> i_V1 = -2 mA).
     *   E1(eout, 0, in, 0) gain=3       -> v(eout) = 6 V, R_e(eout, 0) = 1k.
     *   G1(gout, 0, in, 0) gm=5mS       -> injects 10 mA from gout->0 -> with R_g=200 Ohm, v(gout) = -2 V.
     *   F1(fout, 0, ctrl=V1) gain=4     -> current fout->0 = 4 * (-2mA) = -8mA -> with R_f=500 Ohm, v(fout) = +4 V.
     *   H1(hout, 0, ctrl=V1) rm=1000    -> v(hout) = 1000 * (-2mA) = -2 V, R_h(hout, 0) = 1k. */
    Circuit ckt{};
    ckt.name = "controlled_sources";
    const NodeId n0{0}, n_in = ckt.get_or_add_node("in"), n_e = ckt.get_or_add_node("eout"),
                 n_g = ckt.get_or_add_node("gout"), n_f = ckt.get_or_add_node("fout"),
                 n_h = ckt.get_or_add_node("hout");
    ckt.elements.push_back(VoltageSourceSpec{"V1", n_in, n0, 2.0, Complex{1.0, 0.0}, std::nullopt});
    ckt.elements.push_back(ResistorSpec{"R1", n_in, n0, 1000.0});
    ckt.elements.push_back(VcvsSpec{"E1", n_e, n0, n_in, n0, 3.0});
    ckt.elements.push_back(ResistorSpec{"Re", n_e, n0, 1000.0});
    ckt.elements.push_back(VccsSpec{"G1", n_g, n0, n_in, n0, 5.0e-3});
    ckt.elements.push_back(ResistorSpec{"Rg", n_g, n0, 200.0});
    ckt.elements.push_back(CccsSpec{"F1", n_f, n0, "V1", 4.0});
    ckt.elements.push_back(ResistorSpec{"Rf", n_f, n0, 500.0});
    ckt.elements.push_back(CcvsSpec{"H1", n_h, n0, "V1", 1000.0});
    ckt.elements.push_back(ResistorSpec{"Rh", n_h, n0, 1000.0});

    CompiledPlan plan{};
    REQUIRE(compile_circuit(ckt, plan).ok());
    OpResult op{};
    AnalysisCommonOptions opts{};
    opts.backend = BackendPreference::SparseLu;
    opts.enable_audits = true;
    REQUIRE(run_op(plan, opts, op).ok());
    REQUIRE(op.audit->all_checked_passed());

    REQUIRE(std::abs(op.values[find_signal(plan, "v(in)")] - 2.0) < 1e-13);
    REQUIRE(std::abs(op.values[find_signal(plan, "i(V1)")] - (-2.0e-3)) < 1e-15);
    REQUIRE(std::abs(op.values[find_signal(plan, "v(eout)")] - 6.0) < 1e-13);
    REQUIRE(std::abs(op.values[find_signal(plan, "v(gout)")] - (-2.0)) < 1e-13);
    REQUIRE(std::abs(op.values[find_signal(plan, "v(fout)")] - 4.0) < 1e-13);
    REQUIRE(std::abs(op.values[find_signal(plan, "v(hout)")] - (-2.0)) < 1e-13);

    /* DC sweep of V1 from 0V to 5V by 1V reuses numeric factors (1 factorization, 6 solves). */
    DcSweepOptions sw{};
    sw.source_id = "V1";
    sw.start = 0.0;
    sw.stop = 5.0;
    sw.step = 1.0;
    sw.common.backend = BackendPreference::SparseLu;
    DcSweepResult sw_res{};
    REQUIRE(run_dc_sweep(plan, sw, sw_res).ok());
    REQUIRE(sw_res.points.size() == 6u);
    REQUIRE(sw_res.solver_stats.numeric_factorizations == 1u);
    REQUIRE(sw_res.solver_stats.solves == 6u);
    REQUIRE(std::abs(sw_res.points[5].values[find_signal(plan, "v(eout)")] - 15.0) < 1e-12);
}

TEST_CASE("PULSE and PWL transient waveforms hit exact source breakpoints", "[m2a]") {
    Circuit ckt{};
    ckt.name = "pwl_breakpoints";
    const NodeId n0{0}, n_in = ckt.get_or_add_node("in"), n_out = ckt.get_or_add_node("out");
    PwlWaveform pwl{{{0.0, 0.0}, {1.3e-4, 3.0}, {2.7e-4, 1.0}, {5.0e-4, 1.0}}};
    ckt.elements.push_back(VoltageSourceSpec{"V1", n_in, n0, 0.0, {0.0, 0.0}, pwl});
    ckt.elements.push_back(ResistorSpec{"R1", n_in, n_out, 1000.0});
    ckt.elements.push_back(CapacitorSpec{"C1", n_out, n0, 1.0e-7, std::nullopt});

    CompiledPlan plan{};
    REQUIRE(compile_circuit(ckt, plan).ok());
    TransientOptions topts{};
    topts.step_s = 1.0e-4; /* Notice 1.3e-4 and 2.7e-4 are NOT multiples of 1e-4! */
    topts.stop_s = 5.0e-4;
    topts.method = IntegrationMethod::Trapezoidal;
    topts.common.enable_audits = true;
    TransientResult tr{};
    REQUIRE(run_transient(plan, topts, tr).ok());
    REQUIRE(tr.audit->all_checked_passed());

    bool hit_130u = false, hit_270u = false;
    const std::size_t idx_in = find_signal(plan, "v(in)");
    for (const auto& s : tr.samples) {
        if (std::abs(s.time_s - 1.3e-4) < 1e-15) {
            hit_130u = true;
            REQUIRE(std::abs(s.values[idx_in] - 3.0) < 1e-13);
        }
        if (std::abs(s.time_s - 2.7e-4) < 1e-15) {
            hit_270u = true;
            REQUIRE(std::abs(s.values[idx_in] - 1.0) < 1e-13);
        }
    }
    REQUIRE(hit_130u);
    REQUIRE(hit_270u);
}

TEST_CASE("Independent physical audits detect perturbed voltages and currents (AUDIT-002..005)", "[audit]") {
    Circuit ckt = make_rc_lowpass(1000.0, 1.0e-6, 5.0, Complex{1.0, 0.0});
    /* Add a parallel load resistor so DC current flows. */
    ckt.elements.push_back(ResistorSpec{"Rload", NodeId{2}, NodeId{0}, 1000.0});
    CompiledPlan plan{};
    REQUIRE(compile_circuit(ckt, plan).ok());

    OpResult op{};
    AnalysisCommonOptions opts{};
    opts.enable_audits = true;
    REQUIRE(run_op(plan, opts, op).ok());
    REQUIRE(op.audit->all_checked_passed());

    /* Verify AUDIT-005: global_accuracy is explicitly NotChecked. */
    bool saw_global_not_checked = false;
    for (const auto& c : op.audit->checks) {
        if (c.id == "global_accuracy" && c.status == CheckStatus::NotChecked) {
            saw_global_not_checked = true;
        }
    }
    REQUIRE(saw_global_not_checked);

    /* Perturb v(out) by 1 mV -> independent DC KCL and power checks MUST fail! */
    std::vector<double> bad_x = op.values;
    bad_x[find_signal(plan, "v(out)")] += 1.0e-3;
    AuditReport bad_dc = audit_dc(plan, bad_x);
    REQUIRE(bad_dc.any_failed());

    /* Perturb AC complex voltage -> independent AC audit MUST fail! */
    AcOptions ac_opts{};
    ac_opts.frequencies_hz = {1000.0};
    AcResult ac{};
    REQUIRE(run_ac(plan, ac_opts, ac).ok());
    auto bad_ac_x = ac.points[0].values;
    bad_ac_x[find_signal(plan, "v(out)")] += Complex{1.0e-3, -1.0e-3};
    REQUIRE(audit_ac(plan, bad_ac_x, 1000.0).any_failed());
}

TEST_CASE("Singular circuit and unavailable strict KLU fail closed cleanly", "[m2a]") {
    /* Conflicting parallel ideal voltage sources -> singular MNA matrix. */
    Circuit singular{};
    singular.name = "conflicting_vsources";
    const NodeId n0{0}, n1 = singular.get_or_add_node("n1");
    singular.elements.push_back(VoltageSourceSpec{"V1", n1, n0, 5.0, {0.0, 0.0}, std::nullopt});
    singular.elements.push_back(VoltageSourceSpec{"V2", n1, n0, 3.0, {0.0, 0.0}, std::nullopt});
    CompiledPlan plan{};
    REQUIRE(compile_circuit(singular, plan).ok());

    OpResult op{};
    AnalysisCommonOptions opts{};
    opts.backend = BackendPreference::SparseLu;
    REQUIRE(run_op(plan, opts, op).code == StatusCode::NumericalFailure);
    opts.backend = BackendPreference::DenseLu;
    REQUIRE(run_op(plan, opts, op).code == StatusCode::NumericalFailure);

    if (!klu_is_linked()) {
        std::unique_ptr<RealLinearSolver> klu;
        REQUIRE(create_real_solver(BackendPreference::Klu, klu).code == StatusCode::Unsupported);
    }
}

TEST_CASE("1,000-node RC ladder solves OP, 50-point AC, and 200-step transient rapidly on SparseLu", "[m2a][scale]") {
    constexpr std::uint32_t N = 1000;
    Circuit ladder{};
    ladder.name = "rc_ladder_1000";
    const NodeId n0{0};
    std::vector<NodeId> nodes;
    nodes.reserve(N + 1u);
    nodes.push_back(n0);
    auto make_name = [](const char* prefix, std::uint32_t i) {
        std::string s(prefix);
        s += std::to_string(i);
        return s;
    };
    for (std::uint32_t i = 1; i <= N; ++i) {
        nodes.push_back(ladder.get_or_add_node(make_name("n", i)));
    }
    ladder.elements.push_back(VoltageSourceSpec{"Vin", nodes[1], n0, 1.0, Complex{1.0, 0.0}, std::nullopt});
    for (std::uint32_t i = 1; i < N; ++i) {
        ladder.elements.push_back(ResistorSpec{make_name("R", i), nodes[i], nodes[i + 1u], 10.0});
        ladder.elements.push_back(CapacitorSpec{make_name("C", i), nodes[i + 1u], n0, 1.0e-9, 0.0});
    }
    ladder.elements.push_back(ResistorSpec{"Rload", nodes[N], n0, 1000.0});

    const auto t0 = std::chrono::steady_clock::now();
    CompiledPlan plan{};
    REQUIRE(compile_circuit(ladder, plan).ok());
    REQUIRE(plan.unknown_count() == N + 1u);

    /* OP check: total series R = 999 * 10 = 9990 Ohm, load = 1000 Ohm -> v(n1000) = 1000 / 10990. */
    OpResult op{};
    AnalysisCommonOptions op_opts{};
    op_opts.backend = BackendPreference::SparseLu;
    op_opts.enable_audits = true;
    REQUIRE(run_op(plan, op_opts, op).ok());
    const double expected_end_v = 1000.0 / (999.0 * 10.0 + 1000.0);
    REQUIRE(std::abs(op.values[find_signal(plan, "v(n1000)")] - expected_end_v) < 1e-11);
    REQUIRE(op.audit->all_checked_passed());

    /* 50-frequency AC sweep. */
    AcOptions ac_opts{};
    ac_opts.common.backend = BackendPreference::SparseLu;
    ac_opts.common.enable_audits = false; /* Benchmark path: opt-out of audits (AUDIT-001). */
    for (int i = 0; i < 50; ++i) {
        ac_opts.frequencies_hz.push_back(10.0 * std::pow(10.0, 5.0 * static_cast<double>(i) / 49.0));
    }
    AcResult ac{};
    REQUIRE(run_ac(plan, ac_opts, ac).ok());
    REQUIRE(ac.points.size() == 50u);

    /* 200-step transient from UIC (1 numeric factorization reused across all 200 steps!). */
    TransientOptions tran_opts{};
    tran_opts.step_s = 1.0e-7;
    tran_opts.stop_s = 2.0e-5;
    tran_opts.method = IntegrationMethod::Trapezoidal;
    tran_opts.startup = StartupMode::Uic;
    tran_opts.common.backend = BackendPreference::SparseLu;
    tran_opts.common.enable_audits = false;
    TransientResult tr{};
    REQUIRE(run_transient(plan, tran_opts, tr).ok());
    REQUIRE(tr.samples.size() == 201u);
    REQUIRE(tr.solver_stats.numeric_factorizations == 1u);
    REQUIRE(tr.solver_stats.solves == 200u);

    const auto t1 = std::chrono::steady_clock::now();
    const double elapsed_ms = std::chrono::duration<double, std::milli>(t1 - t0).count();
    std::cout << "[BENCH] 1,000-node RC ladder (compile + OP+audit + 50-pt AC + 200-step TRAN): "
              << elapsed_ms << " ms\n";
}

TEST_CASE("Flat C ABI executes OP, AC, and transient end-to-end with contiguous buffers", "[capi][m2a]") {
    spice_engine_options_t opts{};
    opts.struct_size = sizeof(spice_engine_options_t);
    opts.abi_major = SPICE_CAPI_ABI_MAJOR;
    opts.abi_minor = SPICE_CAPI_ABI_MINOR;
    opts.enable_audits = 1u;

    spice_engine_t* eng = nullptr;
    REQUIRE(spice_engine_create(&opts, &eng) == SPICE_STATUS_OK);

    spice_circuit_t* ckt = nullptr;
    REQUIRE(spice_circuit_create("capi_rc", &ckt) == SPICE_STATUS_OK);
    REQUIRE(spice_circuit_add_voltage_source(ckt, "V1", "in", "0", 10.0, 1.0, 0.0) == SPICE_STATUS_OK);
    REQUIRE(spice_circuit_add_resistor(ckt, "R1", "in", "out", 1000.0) == SPICE_STATUS_OK);
    REQUIRE(spice_circuit_add_resistor(ckt, "R2", "out", "0", 1000.0) == SPICE_STATUS_OK);
    REQUIRE(spice_circuit_add_capacitor(ckt, "C1", "out", "0", 1.0e-6, 1, 0.0) == SPICE_STATUS_OK);

    /* OP */
    spice_result_t* op_res = nullptr;
    REQUIRE(spice_run_op(eng, ckt, SPICE_BACKEND_AUTO, &op_res) == SPICE_STATUS_OK);
    size_t n_sig = 0, n_smp = 0;
    int audited = 0;
    const double* vals = nullptr;
    REQUIRE(spice_result_signal_count(op_res, &n_sig) == SPICE_STATUS_OK);
    REQUIRE(spice_result_sample_count(op_res, &n_smp) == SPICE_STATUS_OK);
    REQUIRE(spice_result_audit_passed(op_res, &audited) == SPICE_STATUS_OK);
    REQUIRE(spice_result_real_data(op_res, &vals) == SPICE_STATUS_OK);
    REQUIRE(n_sig == 3u);
    REQUIRE(n_smp == 1u);
    REQUIRE(audited == 1);
    /* v(in)=10, v(out)=5, i(V1)=-5mA */
    REQUIRE(std::abs(vals[0] - 10.0) < 1e-12);
    REQUIRE(std::abs(vals[1] - 5.0) < 1e-12);
    REQUIRE(std::abs(vals[2] - (-5.0e-3)) < 1e-14);
    spice_result_destroy(op_res);

    /* AC */
    const double freqs[2] = {100.0, 1000.0};
    spice_result_t* ac_res = nullptr;
    REQUIRE(spice_run_ac(eng, ckt, SPICE_BACKEND_AUTO, freqs, 2, &ac_res) == SPICE_STATUS_OK);
    const double* zvals = nullptr;
    REQUIRE(spice_result_complex_data(ac_res, &zvals) == SPICE_STATUS_OK);
    REQUIRE(spice_result_audit_passed(ac_res, &audited) == SPICE_STATUS_OK);
    REQUIRE(audited == 1);
    /* Verify real-data getter refuses complex result cleanly. */
    REQUIRE(spice_result_real_data(ac_res, &vals) == SPICE_STATUS_INVALID_ARGUMENT);
    spice_result_destroy(ac_res);

    /* Transient UIC */
    spice_result_t* tr_res = nullptr;
    REQUIRE(spice_run_transient(
                eng, ckt, SPICE_BACKEND_AUTO, 1.0e-4, 1.0e-3,
                SPICE_METHOD_TRAPEZOIDAL, SPICE_STARTUP_UIC, &tr_res) == SPICE_STATUS_OK);
    REQUIRE(spice_result_sample_count(tr_res, &n_smp) == SPICE_STATUS_OK);
    REQUIRE(n_smp == 11u);
    REQUIRE(spice_result_audit_passed(tr_res, &audited) == SPICE_STATUS_OK);
    REQUIRE(audited == 1);
    spice_result_destroy(tr_res);

    spice_circuit_destroy(ckt);
    spice_engine_destroy(eng);
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
    std::cout << "All " << test_registry().size() << " Spice V2 M2a tests passed.\n";
    return 0;
}
#endif
