#pragma once

#include "analysis/analyses.hpp"

#include <complex>
#include <cstddef>
#include <string>
#include <vector>

namespace spice::analysis {

/*
 * M3 RF Steady-State & Periodic Analyses over Universal DAE (ARCH-CAP-002, PLAN-V2-008):
 *   - .HB  (Harmonic Balance): exact spectral collocation + analytical Fourier-Newton
 *          Jacobian J_HB = (I x Gamma^-1) G (I x Gamma) + (I x Omega*Gamma^-1) C (I x Gamma)
 *   - .PSS (Driven Periodic Steady-State Shooting): exact shooting boundary condition
 *          x(T; x0) - x0 = 0 with analytical tangent monodromy matrix M = dx(T)/dx0
 *
 * Works identically on any DAE device (linear, nonlinear Diode, B-source, Switch, OSDI).
 */

struct HbOptions {
    double fundamental_hz{0.0};
    std::size_t harmonics{7}; /* H >= 1; K = 2*H + 1 real Fourier modes per unknown */
    std::size_t max_iterations{60};
    double residual_tol{1.0e-9};
    double update_tol{1.0e-9};
    AnalysisCommonOptions common{};
};

struct HbResult {
    bool converged{false};
    std::size_t iterations{0};
    double fundamental_hz{0.0};
    std::size_t harmonics{0};
    double final_residual_norm{0.0};
    /*
     * Single-sided complex Fourier coefficients per unknown:
     *   phasors[unknown_idx][h] for h = 0..H:
     *   x_i(t) = Re(phasors[i][0]) + sum_{h=1..H} (a_h cos(h*w*t) + b_h sin(h*w*t))
     *   where phasors[i][h] = a_h - j*b_h (so |phasors[i][h]| = peak harmonic amplitude).
     */
    std::vector<std::vector<std::complex<double>>> phasors;
    /* Reconstructed steady-state time-domain samples over one full period [0, T). */
    std::vector<TransientSample> waveform;
};

[[nodiscard]] core::Diagnostic run_hb(
    devices::CompiledPlan& plan,
    const HbOptions& opts,
    HbResult& out_result);

struct PssOptions {
    double fundamental_hz{0.0};
    std::size_t steps_per_period{64};
    std::size_t max_shooting_iterations{30};
    double periodic_tol{1.0e-9};
    IntegrationMethod method{IntegrationMethod::BackwardEuler};
    AnalysisCommonOptions common{};
};

struct PssResult {
    bool converged{false};
    std::size_t shooting_iterations{0};
    double fundamental_hz{0.0};
    double periodicity_error{0.0}; /* ||x(T) - x(0)||_inf */
    /* Steady-state periodic trajectory over [0, T] (length = steps_per_period + 1). */
    std::vector<TransientSample> waveform;
    /* Floquet multipliers (eigenvalues of monodromy matrix M = dx(T)/dx(0), optional diagnostic). */
    std::vector<std::complex<double>> floquet_multipliers;
};

[[nodiscard]] core::Diagnostic run_pss(
    devices::CompiledPlan& plan,
    const PssOptions& opts,
    PssResult& out_result);

/* ----- 3. Periodic Small-Signal AC (.PAC) over Converged .HB Steady State ----- */
struct PacOptions {
    double small_signal_freq_hz{0.0}; /* f_s offset frequency (Hz) */
    std::string input_source_id;      /* Independent V/I source excited with unit tone at f_s */
    AnalysisCommonOptions common{};
};

struct PacSideband {
    int sideband_index{0}; /* k in [-H, +H] */
    double frequency_hz{0.0}; /* f_s + k * f_0 */
    std::vector<std::complex<double>> values; /* Per-unknown complex phasor at this sideband */
};

struct PacResult {
    double fundamental_hz{0.0};
    double small_signal_freq_hz{0.0};
    std::vector<PacSideband> sidebands;
};

[[nodiscard]] core::Diagnostic run_pac(
    devices::CompiledPlan& plan,
    const HbResult& hb_operating_point,
    const PacOptions& opts,
    PacResult& out_result);

/* ----- 4. Periodic Transfer Function (.PXF) via Adjoint Conversion Matrix ----- */
struct PxfOptions {
    double small_signal_freq_hz{0.0};
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    int output_sideband{0}; /* k_out in [-H, +H] */
    AnalysisCommonOptions common{};
};

struct PxfSourceTransfer {
    std::string source_id;
    int input_sideband{0};
    double input_frequency_hz{0.0};
    std::complex<double> transfer_gain{0.0, 0.0};
};

struct PxfResult {
    double output_frequency_hz{0.0};
    std::vector<PxfSourceTransfer> transfers;
};

[[nodiscard]] core::Diagnostic run_pxf(
    devices::CompiledPlan& plan,
    const HbResult& hb_operating_point,
    const PxfOptions& opts,
    PxfResult& out_result);

/* ----- 5. Periodic Cyclostationary Noise (.PNOISE) Folding All Sidebands ----- */
struct PnoiseOptions {
    double small_signal_freq_hz{0.0};
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    int output_sideband{0};
    double temperature_k{300.15};
    AnalysisCommonOptions common{};
};

struct PnoiseResult {
    double output_frequency_hz{0.0};
    double onoise_v_per_sqrt_hz{0.0};
    double total_psd_v2_per_hz{0.0};
};

[[nodiscard]] core::Diagnostic run_pnoise(
    devices::CompiledPlan& plan,
    const HbResult& hb_operating_point,
    const PnoiseOptions& opts,
    PnoiseResult& out_result);

/* ----- 6. Autonomous Oscillator .PSS (Unknown Period T + Phase Anchor) ----- */
struct AutonomousPssOptions {
    double initial_freq_hz{0.0};       /* Initial guess for oscillation frequency f0 = 1/T */
    std::size_t phase_unknown_index{0}; /* Unknown index anchored to phase_target_value at t = 0 */
    double phase_target_value{0.0};
    std::vector<double> initial_state;  /* Optional non-trivial initial state near limit cycle */
    std::size_t steps_per_period{64};
    std::size_t max_shooting_iterations{30};
    double periodic_tol{1.0e-7};
    IntegrationMethod method{IntegrationMethod::Trapezoidal};
    AnalysisCommonOptions common{};
};

struct AutonomousPssResult {
    bool converged{false};
    std::size_t shooting_iterations{0};
    double oscillation_freq_hz{0.0};
    double period_s{0.0};
    double periodicity_error{0.0};
    std::vector<TransientSample> waveform;
};

[[nodiscard]] core::Diagnostic run_autonomous_pss(
    devices::CompiledPlan& plan,
    const AutonomousPssOptions& opts,
    AutonomousPssResult& out_result);

}  // namespace spice::analysis
