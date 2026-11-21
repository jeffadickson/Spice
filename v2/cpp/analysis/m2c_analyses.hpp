#pragma once

#include "analysis/analyses.hpp"

#include <complex>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace spice::analysis {

/*
 * M2c Core Analog Analyses (ARCH-CAP-001, PLAN-V2-007):
 *   - .TF    : Small-signal DC transfer gain, input resistance, output resistance
 *   - .NOISE : Adjoint thermal (Johnson-Nyquist) & diode shot noise PSD + integrated RMS
 *   - .SENS  : Single-adjoint-solve exact analytical DC parameter sensitivity
 *   - .SP    : Multi-port scattering matrix S(f) + Touchstone v1.1 (.sNp) formatter
 *   - .FOUR  : Final-period Fourier harmonic decomposition & Total Harmonic Distortion (THD)
 *   - .FFT   : Uniformly resampled Radix-2 Cooley-Tukey spectral analysis (optional Hann window)
 *   - .MC    : Deterministic seeded Monte Carlo tolerance campaign with summary statistics
 */

/* ----- 1. Small-Signal DC Transfer Function (.TF) ----- */
struct TfOptions {
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    std::string input_source_id;
    AnalysisCommonOptions common{};
};

struct TfResult {
    std::string backend_name;
    double transfer_gain{0.0};     /* dV_out / dV_in (V/V) or dV_out / dI_in (Ohm) */
    double input_resistance{0.0};  /* Ohm */
    double output_resistance{0.0}; /* Ohm */
    std::vector<double> operating_point;
};

[[nodiscard]] core::Diagnostic run_tf(
    devices::CompiledPlan& plan,
    const TfOptions& opts,
    TfResult& out_result);

/* ----- 2. Adjoint Small-Signal Noise (.NOISE) ----- */
struct NoiseOptions {
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    std::string input_source_id;
    std::vector<double> frequencies_hz;
    double temperature_k{300.15};
    AnalysisCommonOptions common{};
};

struct DeviceNoiseContribution {
    std::string element_id;
    std::string mechanism; /* "thermal" or "shot" */
    double output_psd_v2_per_hz{0.0};
};

struct NoisePoint {
    double frequency_hz{0.0};
    double onoise_v_per_sqrt_hz{0.0};
    double inoise_v_per_sqrt_hz{0.0};
    double transfer_gain_mag{0.0};
    std::vector<DeviceNoiseContribution> contributions;
};

struct NoiseResult {
    std::string backend_name;
    std::vector<NoisePoint> points;
    double integrated_onoise_vrms{0.0};
    double integrated_inoise_vrms{0.0};
};

[[nodiscard]] core::Diagnostic run_noise(
    devices::CompiledPlan& plan,
    const NoiseOptions& opts,
    NoiseResult& out_result);

/* ----- 3. Adjoint DC Sensitivity (.SENS) ----- */
struct SensOptions {
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    AnalysisCommonOptions common{};
};

struct SensitivityEntry {
    std::string element_id;
    std::string parameter_name;
    double nominal_value{0.0};
    double d_out_d_param{0.0};      /* V / param_unit */
    double normalized_sens{0.0};    /* (param / v_out) * d_out_d_param */
};

struct SensResult {
    std::string backend_name;
    double nominal_output_volts{0.0};
    std::vector<SensitivityEntry> entries;
};

[[nodiscard]] core::Diagnostic run_sens(
    devices::CompiledPlan& plan,
    const SensOptions& opts,
    SensResult& out_result);

/* ----- 4. Multi-Port S-Parameters (.SP) & Touchstone v1.1 ----- */
struct PortSpec {
    core::NodeId node_p{0};
    core::NodeId node_n{0};
    double z0_ohms{50.0};
};

struct SpOptions {
    std::vector<PortSpec> ports;
    std::vector<double> frequencies_hz;
    AnalysisCommonOptions common{};
};

struct SpPoint {
    double frequency_hz{0.0};
    /* Row-major N x N scattering matrix: s_matrix[i * N + j] = S_{i+1, j+1}. */
    std::vector<std::complex<double>> s_matrix;
};

struct SpResult {
    std::string backend_name;
    std::size_t port_count{0};
    double reference_z0_ohms{50.0};
    std::vector<SpPoint> points;
};

[[nodiscard]] core::Diagnostic run_sp(
    devices::CompiledPlan& plan,
    const SpOptions& opts,
    SpResult& out_result);

[[nodiscard]] std::string format_touchstone(const SpResult& sp);

/* ----- 5. Fourier Harmonic Analysis (.FOUR) & Radix-2 FFT (.FFT) ----- */
struct FourierHarmonic {
    std::size_t harmonic{0}; /* 0 = DC, 1 = fundamental, ... */
    double frequency_hz{0.0};
    double magnitude{0.0};
    double phase_deg{0.0};
    double normalized_mag{0.0};
};

struct FourierResult {
    double fundamental_hz{0.0};
    double dc_component{0.0};
    double thd_percent{0.0};
    std::vector<FourierHarmonic> harmonics;
};

[[nodiscard]] core::Diagnostic compute_fourier(
    const TransientResult& tran,
    std::size_t signal_index,
    double fundamental_hz,
    std::size_t num_harmonics,
    FourierResult& out_result);

struct FftResult {
    std::size_t fft_size{0};
    double sample_rate_hz{0.0};
    std::vector<double> frequencies_hz;
    std::vector<std::complex<double>> spectrum;
    std::vector<double> magnitude;
    std::vector<double> magnitude_db;
};

[[nodiscard]] core::Diagnostic compute_fft(
    const TransientResult& tran,
    std::size_t signal_index,
    std::size_t fft_size_power_of_two,
    bool apply_hann_window,
    FftResult& out_result);

/* ----- 6. Deterministic Monte Carlo Tolerance Analysis (.MC) ----- */
enum class ToleranceDistribution {
    Gaussian, /* relative_tol = 1-sigma fractional std dev */
    Uniform   /* relative_tol = half-width fractional bound [-tol, +tol] */
};

struct ElementTolerance {
    std::string element_id;
    double relative_tol{0.0}; /* e.g., 0.05 = 5% */
    ToleranceDistribution distribution{ToleranceDistribution::Gaussian};
};

struct MonteCarloOptions {
    std::size_t trials{100};
    std::uint64_t seed{42};
    std::vector<ElementTolerance> tolerances;
    AnalysisCommonOptions common{};
};

struct SignalStatistics {
    std::string signal_id;
    double mean{0.0};
    double std_dev{0.0};
    double min_value{0.0};
    double max_value{0.0};
};

struct MonteCarloResult {
    std::size_t completed_trials{0};
    std::uint64_t seed{0};
    std::vector<std::vector<double>> trial_op_values;
    std::vector<SignalStatistics> stats;
};

[[nodiscard]] core::Diagnostic run_monte_carlo_op(
    const core::Circuit& nominal_circuit,
    const MonteCarloOptions& opts,
    MonteCarloResult& out_result);

/* ----- 7. Tian Exact Loop-Gain Stability Analysis (.STB) ----- */
struct StbOptions {
    std::string probe_source_id; /* 0 V probe voltage source inserted in feedback loop */
    std::vector<double> frequencies_hz;
    AnalysisCommonOptions common{};
};

struct StbPoint {
    double frequency_hz{0.0};
    std::complex<double> loop_gain{0.0, 0.0};
    double magnitude_db{0.0};
    double phase_deg{0.0};
};

struct StbResult {
    std::string backend_name;
    std::vector<StbPoint> points;
    std::optional<double> unity_gain_freq_hz;
    std::optional<double> phase_margin_deg;
    std::optional<double> phase_crossover_freq_hz;
    std::optional<double> gain_margin_db;
};

[[nodiscard]] core::Diagnostic run_stb(
    devices::CompiledPlan& plan,
    const StbOptions& opts,
    StbResult& out_result);

/* ----- 8. Pole-Zero Analysis (.PZ) via Shift-Invert + Francis Double-Shift QR ----- */
struct PzOptions {
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    std::string input_source_id;
    double shift_rad_per_s{1.0}; /* Real shift sigma for nonsingular (G + sigma*C) */
    AnalysisCommonOptions common{};
};

struct PzResult {
    std::vector<std::complex<double>> poles_rad_per_s;
    std::vector<std::complex<double>> zeros_rad_per_s;
};

[[nodiscard]] core::Diagnostic run_pz(
    devices::CompiledPlan& plan,
    const PzOptions& opts,
    PzResult& out_result);

/* ----- 9. Adjoint DC Statistical Mismatch Analysis (.DCMATCH) ----- */
struct DcMatchOptions {
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    /* Per-element 1-sigma fractional mismatch tolerances (e.g. 0.01 = 1% 1-sigma). */
    std::vector<ElementTolerance> mismatches;
    AnalysisCommonOptions common{};
};

struct DcMatchContributor {
    std::string element_id;
    std::string parameter_name;
    double sigma_output_volts{0.0};
    double variance_share_percent{0.0};
};

struct DcMatchResult {
    double nominal_output_volts{0.0};
    double total_sigma_volts{0.0};
    double three_sigma_volts{0.0};
    std::vector<DcMatchContributor> contributors;
};

[[nodiscard]] core::Diagnostic run_dcmatch(
    devices::CompiledPlan& plan,
    const DcMatchOptions& opts,
    DcMatchResult& out_result);

/* ----- 10. SPICE3f5 RAW (ASCII & Binary) and IEEE 1364 VCD Exporters ----- */
[[nodiscard]] std::string format_spice_raw_ascii(
    const devices::CompiledPlan& plan,
    const TransientResult& tran);

[[nodiscard]] std::vector<std::uint8_t> format_spice_raw_binary(
    const devices::CompiledPlan& plan,
    const TransientResult& tran);

[[nodiscard]] std::string format_vcd(
    const devices::CompiledPlan& plan,
    const TransientResult& tran,
    double logic_threshold_volts = 0.5);

}  // namespace spice::analysis
