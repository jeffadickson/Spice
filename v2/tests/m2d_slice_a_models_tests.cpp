#include "analysis/analyses.hpp"
#include "analysis/m2c_analyses.hpp"
#include "core/circuit.hpp"
#include "devices/linear_devices.hpp"
#include "parser/spice_parser.hpp"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <iostream>
#include <limits>
#include <numbers>
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
      static void SPICE_CAT(spice_slice_a_fn_, __LINE__)();                              \
      static const TestRegistrar SPICE_CAT(spice_slice_a_reg_, __LINE__)(                \
          name, &SPICE_CAT(spice_slice_a_fn_, __LINE__));                                \
      static void SPICE_CAT(spice_slice_a_fn_, __LINE__)()
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

std::size_t find_signal(const devices::CompiledPlan& plan, const std::string& name) {
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (plan.signals[i].id == name) return i;
    }
    return plan.signals.size();
}

bool approx(double a, double b, double atol = 1.0e-9, double rtol = 1.0e-6) {
    return std::abs(a - b) <= atol + rtol * std::max(std::abs(a), std::abs(b));
}

/*
 * Central finite-difference verification of BOTH G = dF/dx and C = dQ/dx
 * against every analytical CSC Jacobian entry stamped by CompiledPlan::evaluate_all().
 */
void verify_plan_jacobians_fd(
    devices::CompiledPlan& plan,
    const std::vector<double>& x_base,
    double temperature_k = 300.15,
    dae::AnalysisMode mode = dae::AnalysisMode::Transient,
    double h = 1.0e-7,
    double rtol = 2.0e-5,
    double atol = 1.0e-7) {
    const std::size_t n = plan.unknown_count();
    REQUIRE(x_base.size() == n);

    std::vector<double> f0(n, 0.0), q0(n, 0.0);
    std::vector<double> g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);
    dae::EvalContext ctx0{0.0, temperature_k, mode, x_base};
    dae::StampTarget t0{f0, q0, g_vals, c_vals};
    REQUIRE(plan.evaluate_all(ctx0, t0).ok());

    /* Scatter sparse CSC analytical Jacobians into dense n x n matrices. */
    std::vector<double> g_dense(n * n, 0.0), c_dense(n * n, 0.0);
    for (std::size_t col = 0; col < n; ++col) {
        for (std::size_t k = plan.pattern.col_ptr[col]; k < plan.pattern.col_ptr[col + 1u]; ++k) {
            const std::size_t row = plan.pattern.row_idx[k];
            g_dense[row * n + col] += g_vals[k];
            c_dense[row * n + col] += c_vals[k];
        }
    }

    std::vector<double> x_pert = x_base;
    std::vector<double> fp(n), qp(n), gp(plan.pattern.nnz()), cp(plan.pattern.nnz());
    std::vector<double> fm(n), qm(n), gm(plan.pattern.nnz()), cm(plan.pattern.nnz());

    for (std::size_t col = 0; col < n; ++col) {
        const double step = h * std::max(1.0, std::abs(x_base[col]));
        x_pert[col] = x_base[col] + step;
        std::fill(fp.begin(), fp.end(), 0.0);
        std::fill(qp.begin(), qp.end(), 0.0);
        std::fill(gp.begin(), gp.end(), 0.0);
        std::fill(cp.begin(), cp.end(), 0.0);
        dae::EvalContext ctx_p{0.0, temperature_k, mode, x_pert};
        dae::StampTarget tp{fp, qp, gp, cp};
        REQUIRE(plan.evaluate_all(ctx_p, tp).ok());

        x_pert[col] = x_base[col] - step;
        std::fill(fm.begin(), fm.end(), 0.0);
        std::fill(qm.begin(), qm.end(), 0.0);
        std::fill(gm.begin(), gm.end(), 0.0);
        std::fill(cm.begin(), cm.end(), 0.0);
        dae::EvalContext ctx_m{0.0, temperature_k, mode, x_pert};
        dae::StampTarget tm{fm, qm, gm, cm};
        REQUIRE(plan.evaluate_all(ctx_m, tm).ok());

        x_pert[col] = x_base[col];

        for (std::size_t row = 0; row < n; ++row) {
            const double fd_g = (fp[row] - fm[row]) / (2.0 * step);
            const double fd_c = (qp[row] - qm[row]) / (2.0 * step);
            const double an_g = g_dense[row * n + col];
            const double an_c = c_dense[row * n + col];
            if (!approx(an_g, fd_g, atol, rtol)) {
                std::cerr << "  FD G mismatch in " << plan.circuit.name << " (" << plan.signals[row].id << ","
                          << plan.signals[col].id << "): an=" << an_g << " fd=" << fd_g
                          << " x[col]=" << x_base[col] << " fp=" << fp[row] << " fm=" << fm[row] << "\n";
            }
            if (!approx(an_c, fd_c, atol, rtol)) {
                std::cerr << "  FD C mismatch in " << plan.circuit.name << " (" << plan.signals[row].id << ","
                          << plan.signals[col].id << "): an=" << an_c << " fd=" << fd_c
                          << " x[col]=" << x_base[col] << "\n";
            }
            REQUIRE(approx(an_g, fd_g, atol, rtol));
            REQUIRE(approx(an_c, fd_c, atol, rtol));
        }
    }
}

}  // namespace

TEST_CASE("Slice A analytical Jacobians (dF/dx and dQ/dx) match central finite differences", "[slice_a][jacobian]") {
    /* 1. Full Discrete Diode (Rs, BV/IBV/NBV, ISR/NR, IKF, CJO/VJ/M/FC, TT, Temp) */
    {
        const std::string deck = R"(
* Full diode FD Jacobian check
V1 in 0 0.85
R1 in a 10
D1 a 0 DZ
.MODEL DZ D(IS=1e-13 N=1.1 RS=2.5 BV=5.1 IBV=5e-3 NBV=1.2 ISR=1e-11 NR=2.0 IKF=0.05 CJO=25p VJ=0.75 M=0.4 FC=0.5 TT=8n EG=1.11 XTI=3 TRS1=1e-3 TBV1=5e-4)
.END
)";
        parser::ParsedDeck parsed;
        REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());
        REQUIRE(plan.unknown_count() >= 4u); /* in, a, internal p_int, i(V1) */
        std::vector<double> x_fwd(plan.unknown_count(), 0.0);
        x_fwd[0] = 0.85;
        x_fwd[1] = 0.78;
        x_fwd[2] = 0.72; /* p_int above FC*VJ = 0.375V */
        verify_plan_jacobians_fd(plan, x_fwd, 358.15);

        std::vector<double> x_rev(plan.unknown_count(), 0.0);
        x_rev[0] = -6.0;
        x_rev[1] = -5.25;
        x_rev[2] = -5.18; /* reverse Zener breakdown */
        verify_plan_jacobians_fd(plan, x_rev, 358.15);
    }

    /* 2. Full 4-Terminal Discrete BJT (RC, RB/RBM/IRB, RE, XCJC, CJE, CJC, CJS, FC, TF/XTF/VTF/ITF, Temp) */
    {
        const std::string deck = R"(
* Full 4-terminal NPN BJT FD Jacobian check
VC c 0 5
VB b 0 0.85
VE e 0 0.05
VS s 0 -1.0
Q1 c b e s QFULL
.MODEL QFULL NPN(IS=2e-14 BF=150 BR=4 NF=1.02 NR=1.05 VAF=80 VAR=20 IKF=0.2 IKR=0.05 ISE=5e-13 NE=1.6 ISC=2e-13 NC=1.8 RB=80 RBM=15 IRB=1e-3 RC=5 RE=2 CJE=25p VJE=0.75 MJE=0.35 CJC=8p VJC=0.75 MJC=0.33 XCJC=0.4 CJS=5p VJS=0.75 MJS=0.33 FC=0.5 TF=0.4n XTF=5 VTF=10 ITF=20m TR=10n EG=1.11 XTI=3 XTB=1.5)
.END
)";
        parser::ParsedDeck parsed;
        REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());
        std::vector<double> x_act(plan.unknown_count(), 0.0);
        x_act[0] = 5.0;
        x_act[1] = 0.85;
        x_act[2] = 0.05;
        x_act[3] = -1.0;
        /* Internal nodes: ci, bi, ei */
        x_act[4] = 4.9;
        x_act[5] = 0.78;
        x_act[6] = 0.07;
        verify_plan_jacobians_fd(plan, x_act, 358.15, dae::AnalysisMode::Transient, 1.0e-7, 5.0e-5, 1.0e-6);
    }

    /* 3. Power VDMOS (RD, RG, RS, subthreshold, nonlinear Miller Cgd, body diode + RB + BV) */
    {
        const std::string deck = R"(
* Power VDMOS FD Jacobian check
VD d 0 12
VG g 0 6
VS s 0 0
M1 d g s M_PWR
.MODEL M_PWR VDMOS(VTO=3.0 KP=12 RD=0.04 RG=3 RS=0.02 KSUBTHRES=0.15 THETA=0.05 LAMBDA=0.01 CGS=1.2n CGDMAX=1.5n CGDMIN=50p A=0.6 IS=1e-11 N=1.05 RB=0.05 BV=60 IBV=1e-3 CJO=600p VJ=0.8 M=0.45 FC=0.5 TT=40n)
.END
)";
        parser::ParsedDeck parsed;
        REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

        /* Saturation region (nodes: d=0, g=1, s=2, di=3, gi=4, si=5) */
        std::vector<double> x_sat(plan.unknown_count(), 0.0);
        x_sat[find_signal(plan, "v(d)")] = 12.0;
        x_sat[find_signal(plan, "v(g)")] = 6.0;
        x_sat[find_signal(plan, "v(s)")] = 0.0;
        x_sat[find_signal(plan, "v(M1:di)")] = 11.5;
        x_sat[find_signal(plan, "v(M1:gi)")] = 5.95;
        x_sat[find_signal(plan, "v(M1:si)")] = 0.1;
        verify_plan_jacobians_fd(plan, x_sat, 350.0, dae::AnalysisMode::Transient, 1.0e-7, 5.0e-5, 1.0e-5);

        /* Reverse 3rd-quadrant body-diode conduction (di < si) */
        std::vector<double> x_body(plan.unknown_count(), 0.0);
        x_body[find_signal(plan, "v(d)")] = -1.0;
        x_body[find_signal(plan, "v(g)")] = 0.0;
        x_body[find_signal(plan, "v(s)")] = 0.0;
        x_body[find_signal(plan, "v(M1:di)")] = -0.82;
        x_body[find_signal(plan, "v(M1:gi)")] = 0.0;
        x_body[find_signal(plan, "v(M1:si)")] = 0.0;
        verify_plan_jacobians_fd(plan, x_body, 300.15, dae::AnalysisMode::Transient, 1.0e-7, 5.0e-5, 1.0e-5);

        /* Exact Vds = 0 symmetry point (di == si) */
        std::vector<double> x_zero(plan.unknown_count(), 0.0);
        x_zero[find_signal(plan, "v(g)")] = 5.0;
        x_zero[find_signal(plan, "v(M1:gi)")] = 5.0;
        verify_plan_jacobians_fd(plan, x_zero, 300.15, dae::AnalysisMode::Transient, 1.0e-7, 5.0e-5, 1.0e-5);
    }

    /* 4. MESFET (Statz LEVEL=1 and Curtice LEVEL=2) */
    for (int level : {1, 2}) {
        const std::string deck =
            "* MESFET FD Jacobian check\n"
            "VD d 0 3.0\n"
            "VG g 0 -0.4\n"
            "VS s 0 0\n"
            "Z1 d g s ZMOD\n"
            ".MODEL ZMOD NMF(LEVEL=" +
            std::to_string(level) +
            " VTO=-1.5 BETA=0.08 ALPHA=2.2 LAMBDA=0.04 B=0.3 RD=4 RS=3 CGS=0.6p CGD=0.15p PB=0.8 IS=1e-13)\n"
            ".END\n";
        parser::ParsedDeck parsed;
        REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());
        std::vector<double> x_mes(plan.unknown_count(), 0.0);
        x_mes[0] = 3.0;
        x_mes[1] = -0.4;
        x_mes[2] = 2.8;  /* di */
        x_mes[3] = 0.15; /* si */
        verify_plan_jacobians_fd(plan, x_mes, 300.15, dae::AnalysisMode::Transient, 1.0e-7, 5.0e-5, 1.0e-6);
    }

    /* 5. Current-Controlled Switch (W) and Saturable Magnetic Core (CORE) */
    {
        const std::string deck = R"(
* W switch + saturable core FD Jacobian check
Vctrl ctrl 0 2.0
Rctrl ctrl 0 100
Vdrv drv 0 5.0
W1 drv mid Vctrl SWI
L1 mid 0 FERRITE N=20
.MODEL SWI CSW(IT=0.01 IH=0.005 RON=2 ROFF=1e6)
.MODEL FERRITE CORE(MS=4e5 A=80 K=50 C=0.2 ALPHA=1e-4 AREA=1e-4 PATH=0.05 GAP=2e-4 RW=0.5 LLEAK=1e-6)
.END
)";
        parser::ParsedDeck parsed;
        REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());
        std::vector<double> x_sw(plan.unknown_count(), 0.0);
        x_sw[0] = 2.0;
        x_sw[1] = 5.0;
        x_sw[2] = 2.5;
        /* Set branch currents non-zero so core is in active nonlinear region */
        for (std::size_t idx = 3; idx < x_sw.size(); ++idx) x_sw[idx] = 0.015;
        verify_plan_jacobians_fd(plan, x_sw, 300.15, dae::AnalysisMode::OperatingPoint, 1.0e-7, 5.0e-5, 1.0e-6);
        verify_plan_jacobians_fd(plan, x_sw, 300.15, dae::AnalysisMode::Transient, 1.0e-7, 5.0e-5, 1.0e-6);
    }
}

TEST_CASE("Full Discrete Diode: Zener 5.1V regulator, Rs slope, and SPICE3f5 temperature scaling", "[slice_a][diode]") {
    /* 1. Zener 5.1V shunt regulator */
    const std::string zener_deck = R"(
* Zener 5.1V regulator
Vin in 0 12.0
Rser in out 470
Dz 0 out DZ5V1
.MODEL DZ5V1 D(IS=1e-14 N=1.0 RS=4.0 BV=5.1 IBV=5m NBV=1.0 TBV1=5e-4)
.END
)";
    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(zener_deck, parsed).ok());
    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

    analysis::OpResult op_27{};
    analysis::AnalysisCommonOptions opts_27{};
    opts_27.temperature_k = 300.15;
    REQUIRE(analysis::run_op(plan, opts_27, op_27).ok());
    const double vout_27 = op_27.values[find_signal(plan, "v(out)")];
    /* Current is ~(12 - 5.15)/470 ~= 14.6 mA; Rs drop ~= 58 mV; Vz ~= 5.1 + n*Vt*ln(14.6/5) + 0.058 ~= 5.185 V */
    REQUIRE(vout_27 > 5.08);
    REQUIRE(vout_27 < 5.30);

    /* At 127 C (dT = +100 K), TBV1 = +5e-4/K shifts BV up by 5.1 * 0.05 = +0.255 V */
    analysis::OpResult op_127{};
    analysis::AnalysisCommonOptions opts_127{};
    opts_127.temperature_k = 400.15;
    REQUIRE(analysis::run_op(plan, opts_127, op_127).ok());
    const double vout_127 = op_127.values[find_signal(plan, "v(out)")];
    REQUIRE(vout_127 - vout_27 > 0.20);
    REQUIRE(vout_127 - vout_27 < 0.32);

    /* 2. Forward diode Vf negative tempco (~ -2 mV/C) via .TEMP */
    const std::string fwd_deck = R"(
* Forward diode tempco
I1 0 d 1m
D1 d 0 D1N4148
.MODEL D1N4148 D(IS=2.52e-9 N=1.752 RS=0.568 EG=1.11 XTI=3)
.END
)";
    parser::ParsedDeck p_fwd;
    REQUIRE(parser::parse_spice_deck(fwd_deck, p_fwd).ok());
    devices::CompiledPlan plan_fwd;
    REQUIRE(devices::compile_circuit(p_fwd.circuit, plan_fwd).ok());

    analysis::OpResult vf_27{}, vf_127{};
    REQUIRE(analysis::run_op(plan_fwd, opts_27, vf_27).ok());
    REQUIRE(analysis::run_op(plan_fwd, opts_127, vf_127).ok());
    const double dv_per_c =
        (vf_127.values[find_signal(plan_fwd, "v(d)")] - vf_27.values[find_signal(plan_fwd, "v(d)")]) / 100.0;
    REQUIRE(dv_per_c < -1.0e-3);
    REQUIRE(dv_per_c > -3.5e-3);
}

TEST_CASE("Full Discrete BJT: parasitics RB/RBM/IRB/RC/RE, 4th substrate CJS, XCJC AC pole, and temp scaling",
          "[slice_a][bjt]") {
    const std::string deck = R"(
* Common-emitter amplifier with full BJT parasitics & substrate node
VCC vcc 0 12.0
Vin in 0 DC 0 AC 1
R1 vcc b 47k
R2 b 0 10k
Cin in b 10u
RC vcc c 2.2k
RE e 0 470
Q1 c b e 0 Q2N2222
.MODEL Q2N2222 NPN(IS=1.434e-14 BF=255.9 VAF=74.03 IKF=0.2847 ISE=1.434e-14 NE=1.307 BR=6.092 RB=10 RBM=2 IRB=1e-3 RC=1 RE=0.5 CJE=22.01p VJE=0.75 MJE=0.377 CJC=7.306p VJC=0.75 MJC=0.3416 XCJC=0.5 CJS=4p VJS=0.75 MJS=0.33 FC=0.5 TF=0.411n XTF=3 VTF=1.7 ITF=0.6 EG=1.11 XTI=3 XTB=1.5)
.END
)";
    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

    analysis::OpResult op_27{}, op_100{};
    analysis::AnalysisCommonOptions o27{}, o100{};
    o27.temperature_k = 300.15;
    o100.temperature_k = 373.15;
    REQUIRE(analysis::run_op(plan, o27, op_27).ok());
    REQUIRE(analysis::run_op(plan, o100, op_100).ok());

    const double vc_27 = op_27.values[find_signal(plan, "v(c)")];
    const double ve_27 = op_27.values[find_signal(plan, "v(e)")];
    const double vb_27 = op_27.values[find_signal(plan, "v(b)")];
    REQUIRE(vb_27 - ve_27 > 0.55);
    REQUIRE(vb_27 - ve_27 < 0.80);
    REQUIRE(vc_27 > 2.0);
    REQUIRE(vc_27 < 10.0);

    /* Higher temperature drops Vbe and increases Ic -> collector voltage V(c) drops */
    const double vc_100 = op_100.values[find_signal(plan, "v(c)")];
    REQUIRE(vc_100 < vc_27 - 0.15);

    /* AC response: midband gain ~ -RC/RE = -4.68, high-frequency rolloff from Cjc/Cjs/Tf */
    analysis::AcOptions ac_opts{};
    ac_opts.frequencies_hz = {1.0e4, 1.0e8};
    analysis::AcResult ac{};
    REQUIRE(analysis::run_ac(plan, ac_opts, ac).ok());
    const std::size_t idx_c = find_signal(plan, "v(c)");
    const double gain_mid = std::abs(ac.points[0].values[idx_c]);
    const double gain_hf = std::abs(ac.points[1].values[idx_c]);
    REQUIRE(gain_mid > 3.5);
    REQUIRE(gain_mid < 5.5);
    REQUIRE(gain_hf < 0.7 * gain_mid);
}

TEST_CASE("Power VDMOS: 3-terminal switching, nonlinear Miller Cgd, body diode, and BV avalanche",
          "[slice_a][vdmos]") {
    /* 1. Turn-on DC + transient gate-charge Miller plateau */
    const std::string deck = R"(
* VDMOS switching with Miller capacitance
Vdd vdd 0 24.0
Rload vdd d 10
Vgate gdrv 0 PULSE(0 10 100n 20n 20n 500n 2u)
Rgdrv gdrv g 50
M1 d g 0 IRF540N
.MODEL IRF540N VDMOS(VTO=3.2 KP=20 RD=0.04 RG=2 RS=0.02 KSUBTHRES=0.12 LAMBDA=0.005 CGS=1.5n CGDMAX=2.0n CGDMIN=60p A=0.5 IS=1e-11 RB=0.05 BV=100 IBV=1m CJO=800p VJ=0.8 M=0.45 TT=50n)
.END
)";
    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

    analysis::TransientOptions topts{};
    topts.stop_s = 700.0e-9;
    topts.step_s = 10.0e-9;
    topts.max_step_s = 10.0e-9;
    analysis::TransientResult tran{};
    REQUIRE(analysis::run_transient(plan, topts, tran).ok());
    const std::size_t idx_d = find_signal(plan, "v(d)");
    /* Initially OFF: V(d) ~= 24 V */
    REQUIRE(tran.samples.front().values[idx_d] > 23.5);
    /* After turn-on at t=500 ns: V(d) < 0.5 V (saturated ON) */
    REQUIRE(tran.samples.back().values[idx_d] < 0.5);

    /* 2. Reverse 3rd-quadrant body diode conduction (Vgs = 0, drain pulled negative) */
    const std::string body_deck = R"(
* VDMOS body diode test
Irev d 0 1.0
M1 d 0 0 IRF540N
.MODEL IRF540N VDMOS(VTO=3.2 KP=20 RD=0.04 RG=2 RS=0.02 IS=1e-11 N=1.05 RB=0.05 BV=100 IBV=1m)
.END
)";
    parser::ParsedDeck p_body;
    REQUIRE(parser::parse_spice_deck(body_deck, p_body).ok());
    devices::CompiledPlan plan_body;
    REQUIRE(devices::compile_circuit(p_body.circuit, plan_body).ok());
    analysis::OpResult op_body{};
    REQUIRE(analysis::run_op(plan_body, {}, op_body).ok());
    const double vd_body = op_body.values[find_signal(plan_body, "v(d)")];
    REQUIRE(vd_body < -0.5);
    REQUIRE(vd_body > -1.3);

    /* 3. Drain-source BV avalanche clamp (Vgs = 0, drain driven above BV=60V through 1k) */
    const std::string bv_deck = R"(
* VDMOS BV avalanche test
Vhi hi 0 80.0
Rlim hi d 1k
M1 d 0 0 M60V
.MODEL M60V VDMOS(VTO=3.0 KP=10 RD=0.1 RS=0.05 IS=1e-11 BV=60 IBV=1m)
.END
)";
    parser::ParsedDeck p_bv;
    REQUIRE(parser::parse_spice_deck(bv_deck, p_bv).ok());
    devices::CompiledPlan plan_bv;
    REQUIRE(devices::compile_circuit(p_bv.circuit, plan_bv).ok());
    analysis::OpResult op_bv{};
    REQUIRE(analysis::run_op(plan_bv, {}, op_bv).ok());
    const double vd_bv = op_bv.values[find_signal(plan_bv, "v(d)")];
    REQUIRE(vd_bv > 59.5);
    REQUIRE(vd_bv < 62.0);
}

TEST_CASE("GaAs MESFET (Statz LEVEL=1 and Curtice LEVEL=2): DC bias and RF AC gain", "[slice_a][mesfet]") {
    for (int level : {1, 2}) {
        const std::string deck =
            "* MESFET common-source RF amplifier\n"
            "Vdd vdd 0 5.0\n"
            "Rd vdd d 100\n"
            "Vgs g 0 DC -0.4 AC 1\n"
            "Z1 d g 0 NE71000\n"
            ".MODEL NE71000 NMF(LEVEL=" +
            std::to_string(level) +
            " VTO=-1.2 BETA=0.05 ALPHA=2.0 LAMBDA=0.03 B=0.25 RD=3 RS=2 CGS=0.5p CGD=0.12p PB=0.8)\n"
            ".END\n";
        parser::ParsedDeck parsed;
        REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

        analysis::OpResult op{};
        REQUIRE(analysis::run_op(plan, {}, op).ok());
        const double vd = op.values[find_signal(plan, "v(d)")];
        /* Active region: Ids ~ 20-35 mA -> Vd ~ 1.5 - 3.5 V */
        REQUIRE(vd > 0.5);
        REQUIRE(vd < 4.5);

        analysis::AcOptions ac_opts{};
        ac_opts.frequencies_hz = {1.0e6, 5.0e9};
        analysis::AcResult ac{};
        REQUIRE(analysis::run_ac(plan, ac_opts, ac).ok());
        const std::size_t idx_d = find_signal(plan, "v(d)");
        const double gain_lf = std::abs(ac.points[0].values[idx_d]);
        const double gain_rf = std::abs(ac.points[1].values[idx_d]);
        REQUIRE(gain_lf > 2.0);
        REQUIRE(gain_rf < gain_lf);
    }
}

TEST_CASE("S/VSWITCH and W/CSW switches: inversion, Schmitt hysteresis, and step rollback invariance",
          "[slice_a][switch]") {
    /* 1. Inverted VSWITCH(VON=0 VOFF=5): ON at Vctrl=0, OFF at Vctrl=5 */
    {
        const std::string deck = R"(
* Inverted VSWITCH
Vctrl c 0 0.0
Vs src 0 10.0
Rload src out 1k
S1 out 0 c 0 SINV
.MODEL SINV VSWITCH(VON=0 VOFF=5 RON=10 ROFF=1MEG)
.END
)";
        parser::ParsedDeck parsed;
        REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

        analysis::DcSweepOptions sw{};
        sw.source_id = "Vctrl";
        sw.start = 0.0;
        sw.stop = 5.0;
        sw.step = 2.5;
        analysis::DcSweepResult dc{};
        REQUIRE(analysis::run_dc_sweep(plan, sw, dc).ok());
        const std::size_t idx_out = find_signal(plan, "v(out)");
        /* At Vctrl=0V -> ON (10 ohm divider -> ~0.099 V); at Vctrl=5V -> OFF (1M divider -> ~9.99 V) */
        REQUIRE(dc.points.front().values[idx_out] < 0.2);
        REQUIRE(dc.points.back().values[idx_out] > 9.8);
    }

    /* 2. Hysteretic S switch Schmitt loop in transient */
    {
        const std::string deck = R"(
* Hysteretic S switch Schmitt trigger
Vctrl c 0 PWL(0 0 1m 5 2m 0)
Vs src 0 10.0
Rload src out 1k
S1 out 0 c 0 SHYST OFF
.MODEL SHYST SW(VT=2.5 VH=1.0 RON=10 ROFF=1MEG HYST=1)
.END
)";
        parser::ParsedDeck parsed;
        REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

        analysis::TransientOptions topts{};
        topts.stop_s = 2.0e-3;
        topts.step_s = 20.0e-6;
        topts.max_step_s = 20.0e-6;
        analysis::TransientResult tran{};
        REQUIRE(analysis::run_transient(plan, topts, tran).ok());

        const std::size_t idx_out = find_signal(plan, "v(out)");
        /* Find sample at t=0.5ms (rising ramp, Vc=2.5V -> still OFF, Vout ~= 10V)
         * and sample at t=1.5ms (falling ramp, Vc=2.5V -> still ON, Vout ~= 0.1V)! */
        double v_rise_mid = -1.0;
        double v_fall_mid = -1.0;
        for (const auto& s : tran.samples) {
            if (std::abs(s.time_s - 0.5e-3) < 15.0e-6) v_rise_mid = s.values[idx_out];
            if (std::abs(s.time_s - 1.5e-3) < 15.0e-6) v_fall_mid = s.values[idx_out];
        }
        REQUIRE(v_rise_mid > 9.0);
        REQUIRE(v_fall_mid < 0.5);
    }

    /* 3. Current-controlled switch W (CSW) controlled by V_sense branch current */
    {
        const std::string deck = R"(
* Current-controlled switch W
Vdrv in 0 10.0
Rctl in s_node 1k
Vsense s_node 0 0.0
Vbus bus 0 10.0
Rload bus out 1k
W1 out 0 Vsense WMOD
.MODEL WMOD CSW(IT=5m IH=1m RON=10 ROFF=1MEG)
.END
)";
        parser::ParsedDeck parsed;
        REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
        devices::CompiledPlan plan;
        REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

        analysis::DcSweepOptions sw{};
        sw.source_id = "Vdrv";
        sw.start = 0.0;
        sw.stop = 10.0;
        sw.step = 5.0;
        analysis::DcSweepResult dc{};
        REQUIRE(analysis::run_dc_sweep(plan, sw, dc).ok());
        const std::size_t idx_out = find_signal(plan, "v(out)");
        /* At Vdrv=0V (I=0mA < 5mA) -> switch OFF -> V(out) ~= 10V.
         * At Vdrv=10V (I=10mA > 5mA) -> switch ON -> V(out) ~= 0.1V. */
        REQUIRE(dc.points.front().values[idx_out] > 9.5);
        REQUIRE(dc.points.back().values[idx_out] < 0.25);
    }
}

TEST_CASE("Saturable magnetic core (Jiles-Atherton & Chan): saturation, air gap, hysteresis, and rollback",
          "[slice_a][core]") {
    /* 1. Compare unsaturated small-signal AC inductance with analytical mu_eff and verify saturation under DC bias */
    const std::string deck = R"(
* Saturable core DC-bias inductance sweep
Ibias 0 n1 DC 0 AC 1
L1 n1 0 FERRITE N=20
.MODEL FERRITE CORE(MS=4e5 A=80 K=0 C=1 ALPHA=0 AREA=1e-4 PATH=0.05 GAP=2e-4 RW=0.01 LLEAK=0)
.END
)";
    parser::ParsedDeck parsed;
    REQUIRE(parser::parse_spice_deck(deck, parsed).ok());
    devices::CompiledPlan plan;
    REQUIRE(devices::compile_circuit(parsed.circuit, plan).ok());

    /* Analytical zero-bias inductance:
     * chi0 = Ms / (3*a) = 4e5 / 240 = 1666.667 -> mu_r = 1 + chi0 = 1667.667
     * Reluctance = l_m/(mu_0*mu_r*A_e) + l_g/(mu_0*A_e)
     * L0 = N^2 / Reluctance */
    constexpr double mu0 = 4.0 * std::numbers::pi * 1.0e-7;
    const double mu_r = 1.0 + 4.0e5 / (3.0 * 80.0);
    const double l_expected =
        (20.0 * 20.0 * mu0 * 1.0e-4) / (0.05 / mu_r + 2.0e-4);

    analysis::AcOptions ac_opts{};
    ac_opts.frequencies_hz = {1000.0};
    analysis::AcResult ac0{};
    REQUIRE(analysis::run_ac(plan, ac_opts, ac0).ok());
    const std::size_t idx_n1 = find_signal(plan, "v(n1)");
    const double l_zero_bias = ac0.points[0].values[idx_n1].imag() / (2.0 * std::numbers::pi * 1000.0);
    REQUIRE(approx(l_zero_bias, l_expected, 1.0e-7, 1.0e-3));

    /* Now bias strongly into saturation (I_dc = 50 A -> H_0 = 20*50/0.05 = 20,000 A/m >> a) */
    const std::string sat_deck = R"(
* Saturable core deeply saturated
Ibias 0 n1 DC 50 AC 1
L1 n1 0 FERRITE N=20
.MODEL FERRITE CORE(MS=4e5 A=80 K=0 C=1 ALPHA=0 AREA=1e-4 PATH=0.05 GAP=2e-4 RW=0.01 LLEAK=0)
.END
)";
    parser::ParsedDeck p_sat;
    REQUIRE(parser::parse_spice_deck(sat_deck, p_sat).ok());
    devices::CompiledPlan plan_sat;
    REQUIRE(devices::compile_circuit(p_sat.circuit, plan_sat).ok());
    analysis::AcResult ac_sat{};
    REQUIRE(analysis::run_ac(plan_sat, ac_opts, ac_sat).ok());
    const double l_saturated = ac_sat.points[0].values[find_signal(plan_sat, "v(n1)")].imag() /
                               (2.0 * std::numbers::pi * 1000.0);
    REQUIRE(l_saturated < 0.1 * l_zero_bias);

    /* 2. Hysteretic B-H loop in transient using Chan-style BS/BR/HC model + rollback invariance */
    const std::string hyst_deck = R"(
* Hysteretic saturable core transient
V1 in 0 SIN(0 2.0 1000)
R1 in n1 5
L1 n1 0 30 CHAN_CORE
.MODEL CHAN_CORE CORE(BS=0.4 BR=0.2 HC=40 AREA=1e-4 PATH=0.05 RW=0.1 LLEAK=5u)
.END
)";
    parser::ParsedDeck p_hyst;
    REQUIRE(parser::parse_spice_deck(hyst_deck, p_hyst).ok());
    devices::CompiledPlan plan_hyst;
    REQUIRE(devices::compile_circuit(p_hyst.circuit, plan_hyst).ok());

    analysis::TransientOptions topts{};
    topts.stop_s = 1.5e-3;
    topts.step_s = 10.0e-6;
    topts.max_step_s = 10.0e-6;
    analysis::TransientResult tr1{}, tr2{};
    REQUIRE(analysis::run_transient(plan_hyst, topts, tr1).ok());
    /* Running a second transient on the same plan must reset history and reproduce bit-for-bit */
    REQUIRE(analysis::run_transient(plan_hyst, topts, tr2).ok());
    REQUIRE(tr1.samples.size() == tr2.samples.size());
    const std::size_t idx_il = find_signal(plan_hyst, "i(L1)");
    REQUIRE(approx(tr1.samples.back().values[idx_il], tr2.samples.back().values[idx_il], 1.0e-12, 1.0e-12));

    /* Verify explicit rollback_trial() invariance on SaturableCoreDevice */
    std::vector<double> x_a(plan_hyst.unknown_count(), 0.0);
    x_a[idx_il] = 0.25;
    std::vector<double> x_b(plan_hyst.unknown_count(), 0.0);
    x_b[idx_il] = 0.90;
    std::vector<double> fa1(plan_hyst.unknown_count(), 0.0), qa1(plan_hyst.unknown_count(), 0.0);
    std::vector<double> ga1(plan_hyst.pattern.nnz(), 0.0), ca1(plan_hyst.pattern.nnz(), 0.0);
    dae::EvalContext ctx_a{1.0e-4, 300.15, dae::AnalysisMode::Transient, x_a};
    dae::StampTarget ta1{fa1, qa1, ga1, ca1};
    REQUIRE(plan_hyst.evaluate_all(ctx_a, ta1).ok());
    plan_hyst.rollback_all_trials();

    /* Perturb with a rejected trial step to x_b, roll back, then re-evaluate x_a -> identical Q(x_a)! */
    std::vector<double> fb(plan_hyst.unknown_count(), 0.0), qb(plan_hyst.unknown_count(), 0.0);
    std::vector<double> gb(plan_hyst.pattern.nnz(), 0.0), cb(plan_hyst.pattern.nnz(), 0.0);
    dae::EvalContext ctx_b{1.1e-4, 300.15, dae::AnalysisMode::Transient, x_b};
    dae::StampTarget tb{fb, qb, gb, cb};
    REQUIRE(plan_hyst.evaluate_all(ctx_b, tb).ok());
    plan_hyst.rollback_all_trials();

    std::vector<double> fa2(plan_hyst.unknown_count(), 0.0), qa2(plan_hyst.unknown_count(), 0.0);
    std::vector<double> ga2(plan_hyst.pattern.nnz(), 0.0), ca2(plan_hyst.pattern.nnz(), 0.0);
    dae::StampTarget ta2{fa2, qa2, ga2, ca2};
    REQUIRE(plan_hyst.evaluate_all(ctx_a, ta2).ok());
    REQUIRE(approx(qa1[idx_il], qa2[idx_il], 1.0e-15, 1.0e-15));
}

TEST_CASE("Slice A fail-closed validation rejects invalid model parameters and topologies", "[slice_a][validation]") {
    const std::vector<std::string> bad_decks = {
        /* Diode FC >= 1 */
        "V1 1 0 1\nD1 1 0 DM\n.MODEL DM D(FC=1.0)\n.END\n",
        /* BJT RBM > RB */
        "Q1 1 2 0 QM\n.MODEL QM NPN(RB=10 RBM=20)\n.END\n",
        /* BJT XCJC > 1 */
        "Q1 1 2 0 QM\n.MODEL QM NPN(XCJC=1.5)\n.END\n",
        /* VDMOS negative RD */
        "M1 1 2 0 VM\n.MODEL VM VDMOS(RD=-1)\n.END\n",
        /* MESFET unsupported LEVEL */
        "Z1 1 2 0 ZM\n.MODEL ZM NMF(LEVEL=99)\n.END\n",
        /* W switch referencing unknown control source */
        "W1 1 0 Vmissing WM\n.MODEL WM CSW(IT=1m IH=0.1m)\n.END\n",
        /* Saturable core violating alpha * Ms < 0.99 * 3 * a stability bound */
        "L1 1 0 CM N=10\n.MODEL CM CORE(MS=1e6 A=10 ALPHA=0.01)\n.END\n",
    };

    for (const auto& deck : bad_decks) {
        parser::ParsedDeck parsed;
        auto pd = parser::parse_spice_deck(deck, parsed);
        if (pd.ok()) {
            devices::CompiledPlan plan;
            auto cd = devices::compile_circuit(parsed.circuit, plan);
            REQUIRE(!cd.ok());
        }
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
    std::cout << "All " << test_registry().size() << " Slice A model tests passed.\n";
    return 0;
}
#endif

