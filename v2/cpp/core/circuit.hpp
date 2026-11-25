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

/* Nonlinear SPICE diode with Shockley conduction, reverse breakdown (Zener BV),
 * series resistance Rs (internal node), recombination/high-injection, depletion/transit
 * charge Q(V), and SPICE3f5 temperature scaling (ARCH-CAP-004). */
struct DiodeSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double is_amps{1.0e-14};
    double n_emission{1.0};
    double vt_volts{0.025851991}; /* kT/q at 300 K (used if vt_explicit or T == tnom) */
    double cjo_farads{0.0};       /* Zero-bias junction capacitance */
    double vj_volts{1.0};         /* Built-in junction potential */
    double m_grading{0.5};        /* Grading coefficient (0 < m < 1) */
    double tt_s{0.0};             /* Transit time */
    double gmin_siemens{1.0e-12}; /* Parallel numerical conductance inside model if desired (0 allowed) */
    /* Extended discrete diode physics (backward-compatible defaults) */
    double area{1.0};
    double m_mult{1.0};
    double rs_ohms{0.0};          /* Series ohmic resistance (allocates internal node when > 0) */
    double bv_volts{0.0};         /* Reverse breakdown (Zener) voltage magnitude (0 = disabled) */
    double ibv_amps{1.0e-3};      /* Reverse breakdown current at Vd = -BV */
    double nbv{1.0};              /* Reverse breakdown emission coefficient */
    double fc{0.5};               /* Forward-bias depletion capacitance coefficient (0 < fc < 1) */
    double isr_amps{0.0};         /* Recombination saturation current */
    double nr{2.0};               /* Recombination emission coefficient */
    double ikf_amps{0.0};         /* High-level injection knee current (0 = infinite) */
    double eg_ev{1.11};           /* Activation energy / bandgap (eV) */
    double xti{3.0};              /* Saturation current temperature exponent */
    double tnom_c{27.0};          /* Nominal parameter measurement temperature (C) */
    double trs1{0.0};             /* Linear Rs temperature coefficient (1/C) */
    double trs2{0.0};             /* Quadratic Rs temperature coefficient (1/C^2) */
    double tbv1{0.0};             /* Linear BV temperature coefficient (1/C) */
    double tbv2{0.0};             /* Quadratic BV temperature coefficient (1/C^2) */
    double kf{0.0};               /* Flicker noise coefficient */
    double af{1.0};               /* Flicker noise exponent */
    bool vt_explicit{false};      /* True if VT was explicitly fixed instead of scaling with T */
};

/* Compiled behavioral voltage (B_v) or current (B_i) source with autodiff Jacobians. */
struct BehavioralSourceSpec {
    std::string id;
    NodeId p;
    NodeId n;
    bool is_voltage{false}; /* true: V(p)-V(n)=expr; false: I(p->n)=expr */
    std::string expression;
};

/* Voltage-controlled switch (S element): supports both smooth C-infinity log-conductance
 * transition (vh < 0 or hysteresis=false) and transactional two-state hysteresis (hysteresis=true)
 * with PSpice VSWITCH VON/VOFF polarity support. */
struct SwitchSpec {
    std::string id;
    NodeId p;
    NodeId n;
    NodeId cp;
    NodeId cn;
    double vt_volts{0.5};    /* Threshold control voltage */
    double vh_volts{0.1};    /* Transition / hysteresis half-width (> 0) */
    double ron_ohms{1.0};    /* On-state resistance (> 0) */
    double roff_ohms{1.0e9}; /* Off-state resistance (> 0) */
    bool hysteresis{false};  /* True: commit/rollback hysteretic latch at vt +/- vh */
    bool initial_on{false};  /* Initial latch state for hysteretic switch (IC=ON) */
    bool invert{false};      /* True for normally-closed VSWITCH (VON < VOFF) */
};

/* Current-controlled switch (W element): controlled by branch current of a named source. */
struct CurrentSwitchSpec {
    std::string id;
    NodeId p;
    NodeId n;
    std::string control_source_id;
    double it_amps{0.0};     /* Threshold control current (A) */
    double ih_amps{0.01};    /* Transition / hysteresis half-width (> 0, A) */
    double ron_ohms{1.0};    /* On-state resistance (> 0) */
    double roff_ohms{1.0e9}; /* Off-state resistance (> 0) */
    bool hysteresis{false};  /* True: commit/rollback hysteretic latch at it +/- ih */
    bool initial_on{false};  /* Initial latch state (IC=ON) */
    bool invert{false};      /* True for normally-closed ISWITCH (ION < IOFF) */
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

/* Native Power VDMOS MOSFET (M element with .MODEL ... VDMOS):
 * includes parasitic Rd/Rs/Rg internal nodes, nonlinear Miller Cgd(Vgd),
 * constant Cgs, and antiparallel body diode with depletion/transit charge & BV. */
struct VdmosSpec {
    std::string id;
    NodeId d;
    NodeId g;
    NodeId s;
    bool is_pchan{false};
    double m_mult{1.0};
    double vto_volts{2.0};
    double kp_a_per_v2{1.0};
    double lambda_per_v{0.0};
    double theta_per_v{0.0};       /* Transverse-field mobility degradation (1/V) */
    double ksubthres_volts{0.05};  /* Smooth subthreshold transition voltage (V) */
    double rd_ohms{0.0};           /* Drain ohmic resistance (internal node when > 0) */
    double rs_ohms{0.0};           /* Source ohmic resistance (internal node when > 0) */
    double rg_ohms{0.0};           /* Gate ohmic resistance (internal node when > 0) */
    double rb_ohms{0.0};           /* Body diode series resistance (Ohm) */
    double cgs_farads{0.0};        /* Constant gate-source capacitance (F) */
    double cgdmax_farads{0.0};     /* Zero/forward-bias gate-drain Miller capacitance (F) */
    double cgdmin_farads{0.0};     /* High-Vds gate-drain Miller capacitance (F) */
    double a_cgd{1.0};             /* Miller Cgd transition steepness (1/V) */
    double cjo_farads{0.0};        /* Body diode zero-bias junction capacitance (F) */
    double vj_volts{0.75};
    double m_grading{0.5};
    double fc{0.5};
    double is_amps{1.0e-14};       /* Body diode saturation current (A) */
    double n_emission{1.0};        /* Body diode emission coefficient */
    double tt_s{0.0};              /* Body diode transit time (s) */
    double bv_volts{0.0};          /* Body diode breakdown voltage (0 = disabled) */
    double ibv_amps{1.0e-3};
    double tnom_c{27.0};
    double vtotc_v_per_k{0.0};     /* Linear Vto tempco (V/K) */
    double kptc{0.0};              /* Mobility tempco exponent: KP(T) = KP * (T/Tnom)^(-kptc) */
    double eg_ev{1.11};
    double xti{3.0};
};

/* Native Gummel-Poon BJT primitive (Q element, NPN or PNP) with parasitic RC/RB/RE
 * internal nodes, current-dependent Rbb(Ib), XCJC split, substrate CJS, bias-dependent TF,
 * and SPICE3f5 temperature scaling. */
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
    /* Extended discrete BJT parasitics, capacitances & temperature scaling */
    NodeId s{0};           /* Optional 4th substrate terminal (default ground 0) */
    double rb_ohms{0.0};   /* Zero-bias base resistance (internal node b_int when > 0) */
    double rbm_ohms{0.0};  /* Minimum high-current base resistance (0 => equals rb_ohms) */
    double irb_amps{0.0};  /* Current where Rbb falls halfway to Rbm (0 => constant Rb) */
    double rc_ohms{0.0};   /* Collector ohmic resistance (internal node c_int when > 0) */
    double re_ohms{0.0};   /* Emitter ohmic resistance (internal node e_int when > 0) */
    double fc{0.5};        /* Forward-bias depletion capacitance coefficient */
    double xcjc{1.0};      /* Fraction of Cjc connected to internal base b_int (0..1) */
    double cjs_farads{0.0}; /* Collector-substrate zero-bias capacitance (F) */
    double vjs_volts{0.75};
    double mjs{0.0};
    double xtf{0.0};       /* Forward transit time bias-dependence coefficient */
    double vtf_volts{0.0}; /* Forward transit time Vbc dependence voltage (0 = infinite) */
    double itf_amps{0.0};  /* Forward transit time high-current knee (A) */
    double eg_ev{1.11};
    double xti{3.0};
    double xtb{0.0};       /* Forward & reverse beta temperature exponent */
    double tnom_c{27.0};
    double kf{0.0};
    double af{1.0};
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

/* Native Statz/Raytheon (LEVEL=1) & Curtice (LEVEL=2) GaAs/GaN MESFET primitive (Z element). */
struct MesfetSpec {
    std::string id;
    NodeId d;
    NodeId g;
    NodeId s;
    bool is_pmf{false};
    int level{1};             /* 1 = Statz/Raytheon (SPICE3f5), 2 = Curtice quadratic */
    double area{1.0};
    double m_mult{1.0};
    double vto_volts{-2.0};
    double beta_a_per_v2{2.5e-3};
    double b_doping{0.3};     /* Statz doping tail parameter (1/V) */
    double alpha_per_v{2.0};  /* Saturation parameter (1/V) */
    double lambda_per_v{0.0};
    double rd_ohms{0.0};      /* Drain ohmic resistance (internal node when > 0) */
    double rs_ohms{0.0};      /* Source ohmic resistance (internal node when > 0) */
    double is_amps{1.0e-14};  /* Schottky gate diode saturation current (A) */
    double n_emission{1.0};
    double cgs_farads{0.0};
    double cgd_farads{0.0};
    double pb_volts{0.8};
    double fc{0.5};
    double tnom_c{27.0};
};

/* Native Jiles-Atherton saturable magnetic core inductor (L element with .MODEL ... CORE).
 * Models anhysteretic Langevin saturation, optional air gap, and full transient B-H hysteresis
 * (k_a_per_m > 0) with transactional commit/rollback state (ARCH-EQ-004, ARCH-CAP-004). */
struct SaturableCoreSpec {
    std::string id;
    NodeId p;
    NodeId n;
    double turns{10.0};            /* Winding turns N (> 0) */
    double area_m2{1.0e-4};        /* Core cross-sectional area A_e (m^2) */
    double path_m{0.05};           /* Effective magnetic path length l_e (m) */
    double gap_m{0.0};             /* Optional air gap length l_g (m) */
    double ms_a_per_m{3.5e5};      /* Saturation magnetization M_s (A/m) */
    double a_a_per_m{50.0};        /* Anhysteretic shape parameter a (A/m) */
    double k_a_per_m{0.0};         /* Pinning/coercivity loss parameter k (A/m; 0 = anhysteretic) */
    double c_reversibility{0.2};   /* Domain wall flexing reversibility c (0 <= c <= 1) */
    double alpha_interdomain{1.0e-4}; /* Mean-field interdomain coupling alpha (>= 0) */
    double r_winding_ohms{0.0};    /* Optional series winding resistance (Ohm) */
    double l_leak_henries{0.0};    /* Optional linear leakage inductance (H) */
    std::optional<double> ic_amps;
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
    CurrentSwitchSpec,
    TransmissionLineSpec,
    MosfetSpec,
    VdmosSpec,
    BjtSpec,
    JfetSpec,
    MesfetSpec,
    SaturableCoreSpec,
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
