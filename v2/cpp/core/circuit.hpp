#pragma once

#include "core/types.hpp"

#include <complex>
#include <cstdint>
#include <optional>
#include <string>
#include <variant>
#include <vector>

namespace spice::core {

/*
 * Source waveforms (SI units: seconds, volts/amperes, hertz, degrees).
 * Breakpoints are reported explicitly to transient integration (ARCH-EQ-005).
 */
struct DcWaveform {
    double value{0.0};
};

struct PulseWaveform {
    double v1{0.0};
    double v2{0.0};
    double delay_s{0.0};
    double rise_s{0.0};
    double fall_s{0.0};
    double width_s{0.0};
    double period_s{0.0}; /* 0 means non-periodic single pulse */
};

struct SineWaveform {
    double offset{0.0};
    double amplitude{0.0};
    double freq_hz{0.0};
    double delay_s{0.0};
    double damping_per_s{0.0};
    double phase_deg{0.0};
};

struct PwlPoint {
    double time_s{0.0};
    double value{0.0};
};

struct PwlWaveform {
    std::vector<PwlPoint> points;
};

struct ExpWaveform {
    double v1{0.0};
    double v2{0.0};
    double td1_s{0.0};
    double tau1_s{1.0e-9};
    double td2_s{0.0};
    double tau2_s{1.0e-9};
};

struct SffmWaveform {
    double offset{0.0};
    double amplitude{0.0};
    double carrier_hz{0.0};
    double mod_index{0.0};
    double signal_hz{0.0};
};

/*
 * Deterministic transient Gaussian noise waveform (SPICE TRNOISE(NA NT [NALPHA NAMP [SEED]])).
 * Evaluates piecewise-linear interpolation between deterministic SplitMix64 + Box-Muller
 * Gaussian samples N(0, na_rms^2) at grid times t_k = k * nt_s (thread-safe, rollback-safe).
 */
struct TrNoiseWaveform {
    double na_rms{0.0};
    double nt_s{1.0e-9};
    double nalpha{0.0};
    double namp{0.0};
    std::uint64_t seed{1337u};
};

using Waveform = std::variant<
    DcWaveform,
    PulseWaveform,
    SineWaveform,
    PwlWaveform,
    ExpWaveform,
    SffmWaveform,
    TrNoiseWaveform>;

[[nodiscard]] Diagnostic validate_waveform(const Waveform& wf, const std::string& subject);
[[nodiscard]] double evaluate_waveform(const Waveform& wf, double time_s) noexcept;
void collect_waveform_breakpoints(
    const Waveform& wf,
    double t_start,
    double t_stop,
    std::vector<double>& out_breakpoints);

/*
 * Canonical linear element specifications (SI passive sign convention:
 * positive current flows from node p to node n).
 */
struct ResistorSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double ohms{0.0};
};

struct CapacitorSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double farads{0.0};
    std::optional<double> ic_volts;
};

struct InductorSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double henries{0.0};
    std::optional<double> ic_amps;
};

struct VoltageSourceSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double dc_volts{0.0};
    std::complex<double> ac_phasor{0.0, 0.0};
    std::optional<Waveform> transient_waveform;
};

struct CurrentSourceSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double dc_amps{0.0};
    std::complex<double> ac_phasor{0.0, 0.0};
    std::optional<Waveform> transient_waveform;
};

struct VccsSpec {
    std::string id;
    NodeId p;
    NodeId n;
    NodeId cp;
    NodeId cn;
    double siemens{0.0};
};

struct VcvsSpec {
    std::string id;
    NodeId p;
    NodeId n;
    NodeId cp;
    NodeId cn;
    double gain{0.0};
};

struct CccsSpec {
    std::string id;
    NodeId p;
    NodeId n;
    std::string control_source_id;
    double gain{0.0};
};

struct CcvsSpec {
    std::string id;
    NodeId p;
    NodeId n;
    std::string control_source_id;
    double ohms{0.0};
};

/* Coupled mutual inductance M = coupling_k * sqrt(L1 * L2), |k| < 1. */
struct MutualInductanceSpec {
    std::string id;
    std::string inductor1_id;
    std::string inductor2_id;
    double coupling_k{0.0};
};

/* Nonlinear SPICE diode with Shockley conduction + depletion/transit charge Q(V). */
struct DiodeSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double is_amps{1.0e-14};
    double n_emission{1.0};
    double vt_volts{0.025851991}; /* kT/q at 300 K */
    double cjo_farads{0.0};       /* Zero-bias junction capacitance */
    double vj_volts{1.0};         /* Built-in junction potential */
    double m_grading{0.5};        /* Grading coefficient (0 < m < 1) */
    double tt_s{0.0};             /* Transit time */
    double gmin_siemens{1.0e-12}; /* Parallel numerical conductance inside model if desired (0 allowed) */
};

/* Compiled behavioral voltage (B_v) or current (B_i) source with autodiff Jacobians. */
struct BehavioralSourceSpec {
    std::string id;
    NodeId p;
    NodeId n;
    bool is_voltage{false}; /* true: V(p)-V(n)=expr; false: I(p->n)=expr */
    std::string expression;
};

/* Smooth C-infinity voltage-controlled switch (S element) with log-conductance tanh transition. */
struct SwitchSpec {
    std::string id;
    NodeId p;
    NodeId n;
    NodeId cp;
    NodeId cn;
    double vt_volts{0.5};    /* Threshold control voltage */
    double vh_volts{0.1};    /* Smooth transition half-width (> 0) */
    double ron_ohms{1.0};    /* On-state resistance (> 0) */
    double roff_ohms{1.0e9}; /* Off-state resistance (> 0) */
};

/* Ideal lossless transmission line (T element): exact Bergeron delay in TRAN, exact ABCD in AC/SP. */
struct TransmissionLineSpec {
    std::string id;
    NodeId p1;
    NodeId n1;
    NodeId p2;
    NodeId n2;
    double z0_ohms{50.0};
    double td_s{1.0e-9};
};

/*
 * Native MOSFET primitive (M element):
 *   - level == 1 : Shichman-Hodges + body effect + lambda + Meyer/overlap capacitances
 *   - level == 49: BSIM3v3 smooth unified subthreshold/strong-inversion/mobility/vsat/DIBL/CLM core
 *   - level == 54: BSIM4 smooth unified subthreshold/strong-inversion/mobility/vsat/DIBL/CLM/Rdsw core
 * Unlisted levels fail closed cleanly at validation/compilation (ARCH-CAP-004).
 */
struct MosfetSpec {
    std::string id;
    NodeId d;
    NodeId g;
    NodeId s;
    NodeId b;
    bool is_pmos{false};
    int level{1};
    double w_m{1.0e-6};
    double l_m{1.0e-6};
    double m_mult{1.0};
    double vth0_volts{0.7};     /* Magnitude threshold voltage (positive for NMOS, negative or abs for PMOS via sign) */
    double kp_a_per_v2{2.0e-5}; /* Level-1 transconductance parameter (A/V^2) */
    double gamma_v12{0.0};      /* Level-1 body effect (V^1/2) */
    double phi_volts{0.6};      /* Surface potential 2*phi_F (V) */
    double lambda_per_v{0.0};   /* Level-1 channel-length modulation (1/V) */
    /* BSIM3v3 / BSIM4 physical parameters */
    double tox_m{4.1e-9};       /* Gate oxide thickness (m) */
    double u0_m2_per_vs{0.045}; /* Low-field mobility (m^2 / (V*s), i.e. cm^2/Vs * 1e-4) */
    double vsat_m_per_s{1.0e5}; /* Saturation velocity (m/s) */
    double k1_v12{0.53};        /* First-order body-bias coefficient (V^1/2) */
    double k2{0.0};             /* Second-order body-bias coefficient */
    double ua_m_per_v{1.0e-9};  /* First-order vertical-field mobility degradation */
    double ub_m2_per_v2{1.0e-18}; /* Second-order mobility degradation */
    double uc_m_per_v2{0.0};    /* Body-bias mobility degradation */
    double eta0{0.08};          /* DIBL coefficient */
    double pclm{1.3};           /* Channel-length modulation factor */
    double voff_volts{-0.08};   /* Subthreshold offset voltage (V) */
    double nfactor{1.0};        /* Subthreshold swing factor */
    double rdsw_ohm_um{200.0};  /* Parasitic S/D resistance per unit width (Ohm*um) */
    double delta_vds{0.01};     /* Smooth Vds -> Vdsat transition parameter (V) */
    /* Overlap & junction capacitances and bulk diodes */
    double cgso_f_per_m{2.0e-10};
    double cgdo_f_per_m{2.0e-10};
    double cgbo_f_per_m{0.0};
    double cbd_farads{0.0};
    double cbs_farads{0.0};
    double js_amps{1.0e-14};
};

/* Native Gummel-Poon BJT primitive (Q element, NPN or PNP). */
struct BjtSpec {
    std::string id;
    NodeId c;
    NodeId b;
    NodeId e;
    bool is_pnp{false};
    double area{1.0};
    double m_mult{1.0};
    double is_amps{1.0e-16};
    double bf{100.0};
    double br{1.0};
    double nf{1.0};
    double nr{1.0};
    double vaf_volts{0.0}; /* 0 = infinite Early voltage */
    double var_volts{0.0}; /* 0 = infinite reverse Early voltage */
    double ikf_amps{0.0};  /* 0 = infinite high-injection knee */
    double ikr_amps{0.0};
    double ise_amps{0.0};
    double ne{1.5};
    double isc_amps{0.0};
    double nc{2.0};
    double cje_farads{0.0};
    double vje_volts{0.75};
    double mje{0.33};
    double tf_s{0.0};
    double cjc_farads{0.0};
    double vjc_volts{0.75};
    double mjc{0.33};
    double tr_s{0.0};
};

/* Native Shichman-Hodges JFET primitive (J element, NJF or PJF). */
struct JfetSpec {
    std::string id;
    NodeId d;
    NodeId g;
    NodeId s;
    bool is_pjf{false};
    double area{1.0};
    double m_mult{1.0};
    double vto_volts{-2.0};
    double beta_a_per_v2{1.0e-4};
    double lambda_per_v{0.0};
    double is_amps{1.0e-14};
    double cgs_farads{0.0};
    double cgd_farads{0.0};
    double pb_volts{1.0};
};

/* Dynamically loaded OSDI 0.3/0.4 compiled Verilog-A model instance (ARCH-MOD-001). */
struct OsdiInstanceSpec {
    std::string id;
    std::string library_path;
    std::string model_name;
    std::vector<NodeId> terminals;
    std::vector<std::pair<std::string, double>> model_params;
    std::vector<std::pair<std::string, double>> instance_params;
};

using ElementSpec = std::variant<
    ResistorSpec,
    CapacitorSpec,
    InductorSpec,
    VoltageSourceSpec,
    CurrentSourceSpec,
    VccsSpec,
    VcvsSpec,
    CccsSpec,
    CcvsSpec,
    MutualInductanceSpec,
    DiodeSpec,
    BehavioralSourceSpec,
    SwitchSpec,
    TransmissionLineSpec,
    MosfetSpec,
    BjtSpec,
    JfetSpec,
    OsdiInstanceSpec>;

struct SignalDescriptor {
    std::string id;        /* e.g. "v(out)" or "i(V1)" */
    std::string quantity;  /* "voltage" or "current" */
    std::string si_unit;   /* "V" or "A" */
};

/*
 * Immutable canonical circuit IR (ARCH-STATE-001, ARCH-STATE-003).
 * Node 0 is always ground ("0"). No arbitrary 128-node/256-unknown cap.
 */
struct Circuit {
    std::string name;
    std::vector<std::string> nodes{"0"};
    std::vector<ElementSpec> elements;

    /* Helper: find or append a node name and return its NodeId. */
    [[nodiscard]] NodeId get_or_add_node(const std::string& node_name);

    /* Validate all topology, parameter domain, reference, and budget invariants. */
    [[nodiscard]] Diagnostic validate(std::uint64_t max_unknowns = 1'000'000) const;
};

[[nodiscard]] const std::string& element_id(const ElementSpec& e) noexcept;
[[nodiscard]] double source_value_at(const VoltageSourceSpec& s, double time_s, bool is_transient) noexcept;
[[nodiscard]] double source_value_at(const CurrentSourceSpec& s, double time_s, bool is_transient) noexcept;

}  // namespace spice::core
