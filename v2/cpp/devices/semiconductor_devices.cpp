#include "devices/semiconductor_devices.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>

namespace spice::devices {

namespace {

using core::CscPattern;
using core::Diagnostic;
using core::SlotId;
using core::StatusCode;
using core::UnknownId;
using dae::Device;
using dae::DeviceDescriptor;
using dae::EvalContext;
using dae::StampTarget;

constexpr double kEps0 = 8.8541878128e-12;    /* Vacuum permittivity (F/m) */
constexpr double kEpsOx = 3.9 * kEps0;        /* SiO2 permittivity (F/m) */
constexpr double kBoltzmannQ = 8.617333262e-5; /* k_B / q (V/K) */

/*
 * 4-variable forward-mode Dual number for machine-precision analytical Jacobians
 * dF/dx and dQ/dx across 3- and 4-terminal nonlinear semiconductor equations.
 */
struct Dual4 {
    double v{0.0};
    std::array<double, 4> d{0.0, 0.0, 0.0, 0.0};

    static Dual4 constant(double c) noexcept { return Dual4{c, {0.0, 0.0, 0.0, 0.0}}; }
    static Dual4 variable(double val, std::size_t idx) noexcept {
        Dual4 r{val, {0.0, 0.0, 0.0, 0.0}};
        r.d[idx] = 1.0;
        return r;
    }

    Dual4 operator-() const noexcept { return Dual4{-v, {-d[0], -d[1], -d[2], -d[3]}}; }

    Dual4 operator+(const Dual4& o) const noexcept {
        return Dual4{v + o.v, {d[0] + o.d[0], d[1] + o.d[1], d[2] + o.d[2], d[3] + o.d[3]}};
    }
    Dual4 operator-(const Dual4& o) const noexcept {
        return Dual4{v - o.v, {d[0] - o.d[0], d[1] - o.d[1], d[2] - o.d[2], d[3] - o.d[3]}};
    }
    Dual4 operator*(const Dual4& o) const noexcept {
        return Dual4{
            v * o.v,
            {v * o.d[0] + o.v * d[0],
             v * o.d[1] + o.v * d[1],
             v * o.d[2] + o.v * d[2],
             v * o.d[3] + o.v * d[3]}};
    }
    Dual4 operator/(const Dual4& o) const noexcept {
        const double inv = 1.0 / o.v;
        const double inv2 = inv * inv;
        return Dual4{
            v * inv,
            {(d[0] * o.v - v * o.d[0]) * inv2,
             (d[1] * o.v - v * o.d[1]) * inv2,
             (d[2] * o.v - v * o.d[2]) * inv2,
             (d[3] * o.v - v * o.d[3]) * inv2}};
    }
};

Dual4 operator+(double c, const Dual4& x) noexcept { return Dual4::constant(c) + x; }
Dual4 operator+(const Dual4& x, double c) noexcept { return x + Dual4::constant(c); }
Dual4 operator-(double c, const Dual4& x) noexcept { return Dual4::constant(c) - x; }
Dual4 operator-(const Dual4& x, double c) noexcept { return x - Dual4::constant(c); }
Dual4 operator*(double c, const Dual4& x) noexcept {
    return Dual4{c * x.v, {c * x.d[0], c * x.d[1], c * x.d[2], c * x.d[3]}};
}
Dual4 operator*(const Dual4& x, double c) noexcept { return c * x; }
Dual4 operator/(const Dual4& x, double c) noexcept { return (1.0 / c) * x; }
Dual4 operator/(double c, const Dual4& x) noexcept { return Dual4::constant(c) / x; }

Dual4 d_sqrt(const Dual4& x) noexcept {
    const double s = std::sqrt(std::max(x.v, 1.0e-30));
    const double k = 0.5 / s;
    return Dual4{s, {k * x.d[0], k * x.d[1], k * x.d[2], k * x.d[3]}};
}

/* Overflow-safe exponential with C^1 linear continuation above x_max = 40. */
Dual4 d_safe_exp(const Dual4& x, double x_max = 40.0) noexcept {
    if (x.v <= x_max) {
        const double e = std::exp(std::max(x.v, -80.0));
        const double de = (x.v < -80.0) ? 0.0 : e;
        return Dual4{e, {de * x.d[0], de * x.d[1], de * x.d[2], de * x.d[3]}};
    }
    const double em = std::exp(x_max);
    return Dual4{
        em * (1.0 + (x.v - x_max)),
        {em * x.d[0], em * x.d[1], em * x.d[2], em * x.d[3]}};
}

/* Overflow-safe log(1 + exp(x)) = softplus(x) with exact sigmoid derivative. */
Dual4 d_softplus(const Dual4& x) noexcept {
    double val = 0.0;
    double sig = 0.0;
    if (x.v > 35.0) {
        val = x.v;
        sig = 1.0;
    } else if (x.v < -35.0) {
        const double e = std::exp(x.v);
        val = e;
        sig = e;
    } else {
        const double e = std::exp(x.v);
        val = std::log1p(e);
        sig = e / (1.0 + e);
    }
    return Dual4{val, {sig * x.d[0], sig * x.d[1], sig * x.d[2], sig * x.d[3]}};
}

/* Smooth lower floor: smooth_max(x, floor_val, eps) = floor + 0.5*((x-f) + sqrt((x-f)^2 + eps^2)). */
Dual4 d_smooth_floor(const Dual4& x, double floor_val, double eps = 1.0e-3) noexcept {
    const Dual4 dx = x - floor_val;
    return floor_val + 0.5 * (dx + d_sqrt(dx * dx + eps * eps));
}

/* Smooth PN junction current I = Is * (safe_exp(v / nvt) - 1) + gmin * v. */
Dual4 pn_junction_current(const Dual4& v, double is_amps, double nvt, double gmin = 1.0e-12) noexcept {
    if (is_amps <= 0.0) return gmin * v;
    const Dual4 arg = v / nvt;
    if (arg.v < 0.0) {
        /* Exact expm1 near zero for zero cancellation error. */
        const double val = is_amps * std::expm1(std::max(arg.v, -80.0));
        const double g = (arg.v < -80.0) ? 0.0 : (is_amps / nvt) * std::exp(arg.v);
        return Dual4{val, {g * v.d[0], g * v.d[1], g * v.d[2], g * v.d[3]}} + gmin * v;
    }
    return is_amps * (d_safe_exp(arg, 40.0) - 1.0) + gmin * v;
}

/* Smooth PN depletion charge Q_dep(v) with C^1 continuation above fc*vj. */
Dual4 pn_depletion_charge(const Dual4& v, double cj0, double vj, double mj, double fc = 0.5) noexcept {
    if (cj0 <= 0.0) return Dual4::constant(0.0);
    const double v_fc = fc * vj;
    const double one_m_fc = 1.0 - fc;
    if (v.v <= v_fc) {
        const double base = std::max(1.0 - v.v / vj, 1.0e-6);
        const double q = (cj0 * vj / (1.0 - mj)) * (1.0 - std::pow(base, 1.0 - mj));
        const double c = cj0 * std::pow(base, -mj);
        return Dual4{q, {c * v.d[0], c * v.d[1], c * v.d[2], c * v.d[3]}};
    }
    const double q_fc = (cj0 * vj / (1.0 - mj)) * (1.0 - std::pow(one_m_fc, 1.0 - mj));
    const double c_fc = cj0 * std::pow(one_m_fc, -mj);
    const double dc_fc = (cj0 * mj / vj) * std::pow(one_m_fc, -mj - 1.0);
    const Dual4 dv = v - v_fc;
    return q_fc + c_fc * dv + 0.5 * dc_fc * dv * dv;
}

double node_v(std::span<const double> x, std::optional<UnknownId> u) noexcept {
    return u.has_value() ? x[u->value] : 0.0;
}

template <std::size_t K>
void record_dense_block(StencilCoords& coords, const std::array<std::optional<UnknownId>, K>& terms) {
    for (std::size_t r = 0; r < K; ++r) {
        if (!terms[r].has_value()) continue;
        for (std::size_t c = 0; c < K; ++c) {
            if (!terms[c].has_value()) continue;
            coords.emplace_back(terms[r]->value, terms[c]->value);
        }
    }
}

template <std::size_t K>
Diagnostic bind_dense_block(
    const CscPattern& pat,
    const std::array<std::optional<UnknownId>, K>& terms,
    std::array<std::array<std::optional<SlotId>, K>, K>& out_slots) {
    for (std::size_t r = 0; r < K; ++r) {
        for (std::size_t c = 0; c < K; ++c) {
            out_slots[r][c].reset();
            if (!terms[r].has_value() || !terms[c].has_value()) continue;
            SlotId slot{};
            if (!pat.find_slot(terms[r]->value, terms[c]->value, slot)) {
                return Diagnostic::error(StatusCode::InvalidArgument, "missing semiconductor CSC slot");
            }
            out_slots[r][c] = slot;
        }
    }
    return Diagnostic::success();
}

template <std::size_t K>
Diagnostic stamp_terminal_duals(
    StampTarget& t,
    const std::array<std::optional<UnknownId>, K>& terms,
    const std::array<std::array<std::optional<SlotId>, K>, K>& slots,
    const std::array<Dual4, K>& f_terms,
    const std::array<Dual4, K>& q_terms) {
    for (std::size_t r = 0; r < K; ++r) {
        if (!terms[r].has_value()) continue;
        if (!t.add_f(*terms[r], f_terms[r].v) || !t.add_q(*terms[r], q_terms[r].v)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "semiconductor residual stamp failed");
        }
        for (std::size_t c = 0; c < K; ++c) {
            if (!slots[r][c].has_value()) continue;
            if (!t.add_df(*slots[r][c], f_terms[r].d[c]) || !t.add_dq(*slots[r][c], q_terms[r].d[c])) {
                return Diagnostic::error(StatusCode::NumericalFailure, "semiconductor Jacobian stamp failed");
            }
        }
    }
    return Diagnostic::success();
}

/* ==================== 1. Native MOSFET Device (Level 1 + BSIM3v3/BSIM4) ==================== */
class MosfetDevice final : public Device {
public:
    MosfetDevice(
        std::optional<UnknownId> d,
        std::optional<UnknownId> g,
        std::optional<UnknownId> s,
        std::optional<UnknownId> b,
        core::MosfetSpec spec)
        : terms_{d, g, s, b}, spec_(std::move(spec)) {
        desc_.kind_id = (spec_.level == 54)   ? "spice.mosfet.bsim4"
                        : (spec_.level == 49) ? "spice.mosfet.bsim3"
                                              : "spice.mosfet.level1";
        desc_.display_name = spec_.is_pmos ? "PMOS Transistor" : "NMOS Transistor";
        desc_.pins = {{"d", "drain"}, {"g", "gate"}, {"s", "source"}, {"b", "bulk"}};
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        return bind_dense_block<4>(pat, terms_, slots_);
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& target) override {
        const double sgn = spec_.is_pmos ? -1.0 : 1.0;
        const double vt = std::max(0.01, kBoltzmannQ * ctx.temperature_k);

        /* Polarity-normalized terminal voltages in Dual4 space (indices 0=d, 1=g, 2=s, 3=b). */
        const Dual4 vd_raw = sgn * Dual4::variable(node_v(ctx.x, terms_[0]), 0);
        const Dual4 vg_raw = sgn * Dual4::variable(node_v(ctx.x, terms_[1]), 1);
        const Dual4 vs_raw = sgn * Dual4::variable(node_v(ctx.x, terms_[2]), 2);
        const Dual4 vb_raw = sgn * Dual4::variable(node_v(ctx.x, terms_[3]), 3);

        /* Automatic symmetric drain/source swap for Vds < 0. */
        const bool reversed = (vd_raw.v < vs_raw.v);
        const Dual4 vd = reversed ? vs_raw : vd_raw;
        const Dual4 vs = reversed ? vd_raw : vs_raw;
        const Dual4 vg = vg_raw;
        const Dual4 vb = vb_raw;

        const Dual4 vgs = vg - vs;
        const Dual4 vds = vd - vs;
        const Dual4 vbs = vb - vs;
        const Dual4 vbd = vb - vd;

        const double vth0_mag = std::abs(spec_.vth0_volts);
        const double mult = spec_.m_mult;
        const double w = spec_.w_m;
        const double l = spec_.l_m;
        const double cox = kEpsOx / spec_.tox_m;

        Dual4 ids_chan = Dual4::constant(0.0);
        Dual4 qg_int = Dual4::constant(0.0);
        Dual4 qd_int = Dual4::constant(0.0);
        Dual4 qs_int = Dual4::constant(0.0);
        Dual4 qb_int = Dual4::constant(0.0);

        if (spec_.level == 1) {
            /* Shichman-Hodges Level 1 with body effect, lambda, and Meyer channel capacitances. */
            const Dual4 phi_m_vbs = d_smooth_floor(spec_.phi_volts - vbs, 0.02, 5.0e-3);
            const Dual4 vth = vth0_mag + spec_.gamma_v12 * (d_sqrt(phi_m_vbs) - std::sqrt(spec_.phi_volts));
            const Dual4 vov = vgs - vth;
            const double beta = mult * spec_.kp_a_per_v2 * (w / l);

            /* Smooth subthreshold transition width (1 mV) to avoid kink at Vgs = Vth. */
            const Dual4 vov_pos = d_smooth_floor(vov, 0.0, 1.0e-3);
            const Dual4 vds_eff = vov_pos - 0.5 * (vov_pos - vds + d_sqrt((vov_pos - vds) * (vov_pos - vds) + 1.0e-6));
            ids_chan = beta * (vov_pos * vds_eff - 0.5 * vds_eff * vds_eff) * (1.0 + spec_.lambda_per_v * vds) +
                       1.0e-12 * vds;

            /* Meyer charge approximation (conserves Qg + Qd + Qs + Qb = 0). */
            const double c_ox_tot = mult * cox * w * l;
            const double alpha_inv = 1.0 / (1.0 + std::exp(-std::clamp((vov.v) / 0.05, -40.0, 40.0)));
            const double cgs_m = c_ox_tot * (0.5 * alpha_inv);
            const double cgd_m = c_ox_tot * (0.5 * alpha_inv * (vds.v < vov_pos.v ? 1.0 : 0.0));
            const double cgb_m = c_ox_tot * (1.0 - alpha_inv);
            qg_int = cgs_m * (vg - vs) + cgd_m * (vg - vd) + cgb_m * (vg - vb);
            qs_int = -cgs_m * (vg - vs);
            qd_int = -cgd_m * (vg - vd);
            qb_int = -cgb_m * (vg - vb);
        } else {
            /*
             * BSIM3v3 (LEVEL=49) & BSIM4 (LEVEL=54) smooth unified C-infinity compact core:
             *   - Unified subthreshold / strong-inversion Vgsteff (BSIM Eq. 2.4.1)
             *   - Body effect (K1, K2) + Short-channel DIBL (ETA0)
             *   - Vertical-field mobility degradation (U0, UA, UB, UC)
             *   - Velocity saturation (VSAT) + smooth Vdseff + Rdsw + CLM Early voltage (PCLM)
             *   - Charge-conserving Ward-Dutton intrinsic channel charge partition (Qg+Qd+Qs+Qb = 0)
             */
            const double phi_s = std::max(0.4, spec_.phi_volts);
            const Dual4 sqrt_phi_vbs = d_sqrt(d_smooth_floor(phi_s - vbs, 0.05, 5.0e-3));
            const double sqrt_phi = std::sqrt(phi_s);

            /* Short-channel DIBL scaling (stronger at short L, vanishes at long L). */
            const double dibl_geom = 1.0 / (1.0 + l / 1.0e-7);
            const Dual4 vth = vth0_mag + spec_.k1_v12 * (sqrt_phi_vbs - sqrt_phi) - spec_.k2 * vbs -
                              spec_.eta0 * dibl_geom * vds;

            /* Subthreshold ideality factor n >= 1.05 from depletion capacitance Cdep/Cox. */
            const double cdep_over_cox = std::clamp(0.15 * spec_.k1_v12 / std::max(0.3, sqrt_phi), 0.05, 1.5);
            const double n_sub = std::max(1.05, 1.0 + spec_.nfactor * cdep_over_cox);
            const double two_n_vt = 2.0 * n_sub * vt;

            /* Exact BSIM3/BSIM4 unified Vgsteff:
             *   Strong inversion -> Vgs - Vth; Subthreshold -> exp((Vgs - Vth) / (n * vt)).
             */
            const Dual4 num = two_n_vt * d_softplus((vgs - vth) / two_n_vt);
            const double denom_c = 2.0 * n_sub / std::max(0.1, cdep_over_cox);
            const Dual4 den = 1.0 + denom_c * d_safe_exp(-(vgs - vth - 2.0 * spec_.voff_volts) / two_n_vt, 35.0);
            const Dual4 vgsteff = num / den;

            /* Vertical-field mobility degradation u_eff. */
            const Dual4 e_eff = (vgsteff + 2.0 * vth0_mag) / spec_.tox_m;
            const Dual4 mob_deg = d_smooth_floor(
                1.0 + (spec_.ua_m_per_v + spec_.uc_m_per_v2 * vbs) * e_eff + spec_.ub_m2_per_v2 * e_eff * e_eff,
                1.0,
                0.02);
            const Dual4 u_eff = spec_.u0_m2_per_vs / mob_deg;

            /* Bulk charge factor Abulk & velocity saturation Vdsat. */
            const Dual4 abulk = 1.0 + spec_.k1_v12 / (2.0 * sqrt_phi_vbs);
            const Dual4 esat_l = (2.0 * spec_.vsat_m_per_s * l) / u_eff;
            const Dual4 vgst_2vt = vgsteff + 2.0 * vt;
            const Dual4 vdsat = (esat_l * vgst_2vt) / (abulk * esat_l + vgst_2vt);

            /* Smooth Vdseff transitioning from Vds (linear) to Vdsat (saturation). */
            const double delta = std::max(1.0e-4, spec_.delta_vds);
            const Dual4 diff = vdsat - vds - delta;
            const Dual4 vdseff = vdsat - 0.5 * (diff + d_sqrt(diff * diff + 4.0 * delta * vdsat));

            /* Intrinsic channel current Ids0. */
            const Dual4 ids0 =
                (w / l) * u_eff * cox * vgsteff *
                (1.0 - (abulk * vdseff) / (2.0 * vgst_2vt)) *
                vdseff / (1.0 + vdseff / esat_l);

            /* Series S/D resistance Rdsw (BSIM4/BSIM3 parasitic resistance degradation). */
            const double rds = (spec_.rdsw_ohm_um * 1.0e-6) / w;
            const Dual4 ids_rds = ids0 / (1.0 + (rds * ids0) / (vdseff + 1.0e-4));

            /* Early voltage channel-length modulation (PCLM). */
            const double l_scale = std::max(0.5, l / 1.0e-7);
            const Dual4 va_clm = ((esat_l + vdsat) / std::max(0.05, spec_.pclm)) * l_scale + 0.5;
            const Dual4 clm_factor = 1.0 + (vds - vdseff) / va_clm;

            ids_chan = mult * ids_rds * clm_factor + 1.0e-12 * vds;

            /* Ward-Dutton charge-conserving intrinsic channel charges (40/60 partition in saturation). */
            const double c_ox_tot = mult * cox * w * l;
            const Dual4 q_inv = c_ox_tot * vgsteff;
            const Dual4 sat_ratio = vdseff / (vdsat + 1.0e-4);
            const Dual4 d_part = 0.5 - 0.1 * sat_ratio; /* 0.5 in linear -> 0.4 in saturation */
            const Dual4 s_part = 1.0 - d_part;          /* 0.5 in linear -> 0.6 in saturation */
            qd_int = -d_part * q_inv;
            qs_int = -s_part * q_inv;
            const Dual4 qb_dep = -0.15 * c_ox_tot * (vg - vb);
            qb_int = qb_dep;
            qg_int = -(qd_int + qs_int + qb_int); /* Exact Qg + Qd + Qs + Qb = 0! */
        }

        /* Overlap capacitances + bulk junction capacitances & diodes. */
        const double cgso = mult * spec_.cgso_f_per_m * w;
        const double cgdo = mult * spec_.cgdo_f_per_m * w;
        const double cgbo = mult * spec_.cgbo_f_per_m * l;
        const Dual4 q_gs_ov = cgso * (vg - vs);
        const Dual4 q_gd_ov = cgdo * (vg - vd);
        const Dual4 q_gb_ov = cgbo * (vg - vb);
        const Dual4 q_bs_j = pn_depletion_charge(vbs, mult * spec_.cbs_farads, 0.8, 0.5);
        const Dual4 q_bd_j = pn_depletion_charge(vbd, mult * spec_.cbd_farads, 0.8, 0.5);

        const Dual4 ibs = pn_junction_current(vbs, mult * spec_.js_amps, vt, 1.0e-12);
        const Dual4 ibd = pn_junction_current(vbd, mult * spec_.js_amps, vt, 1.0e-12);

        /* Terminal currents (passive sign convention: current entering each terminal). */
        const Dual4 i_d_eff = ids_chan - ibd;
        const Dual4 i_s_eff = -ids_chan - ibs;
        const Dual4 i_b_eff = ibs + ibd;
        const Dual4 i_g_eff = Dual4::constant(0.0);

        const Dual4 q_g_eff = qg_int + q_gs_ov + q_gd_ov + q_gb_ov;
        const Dual4 q_d_eff = qd_int - q_gd_ov - q_bd_j;
        const Dual4 q_s_eff = qs_int - q_gs_ov - q_bs_j;
        const Dual4 q_b_eff = qb_int - q_gb_ov + q_bs_j + q_bd_j;

        /* Unswap drain/source if Vds < 0 and restore PMOS polarity sign. */
        const std::array<Dual4, 4> f_terms = {
            sgn * (reversed ? i_s_eff : i_d_eff),
            sgn * i_g_eff,
            sgn * (reversed ? i_d_eff : i_s_eff),
            sgn * i_b_eff,
        };
        const std::array<Dual4, 4> q_terms = {
            sgn * (reversed ? q_s_eff : q_d_eff),
            sgn * q_g_eff,
            sgn * (reversed ? q_d_eff : q_s_eff),
            sgn * q_b_eff,
        };

        return stamp_terminal_duals<4>(target, terms_, slots_, f_terms, q_terms);
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::array<std::optional<UnknownId>, 4> terms_;
    std::array<std::array<std::optional<SlotId>, 4>, 4> slots_;
    core::MosfetSpec spec_;
};

/* ==================== 2. Native Gummel-Poon BJT Device (NPN / PNP) ==================== */
class BjtDevice final : public Device {
public:
    BjtDevice(
        std::optional<UnknownId> c,
        std::optional<UnknownId> b,
        std::optional<UnknownId> e,
        core::BjtSpec spec)
        : terms_{c, b, e}, spec_(std::move(spec)) {
        desc_.kind_id = "spice.bjt.gp";
        desc_.display_name = spec_.is_pnp ? "PNP BJT (Gummel-Poon)" : "NPN BJT (Gummel-Poon)";
        desc_.pins = {{"c", "collector"}, {"b", "base"}, {"e", "emitter"}};
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        return bind_dense_block<3>(pat, terms_, slots_);
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& target) override {
        const double sgn = spec_.is_pnp ? -1.0 : 1.0;
        const double vt = std::max(0.01, kBoltzmannQ * ctx.temperature_k);
        const double scale = spec_.area * spec_.m_mult;

        /* Indices: 0=c, 1=b, 2=e */
        const Dual4 vc = sgn * Dual4::variable(node_v(ctx.x, terms_[0]), 0);
        const Dual4 vb = sgn * Dual4::variable(node_v(ctx.x, terms_[1]), 1);
        const Dual4 ve = sgn * Dual4::variable(node_v(ctx.x, terms_[2]), 2);

        const Dual4 vbe = vb - ve;
        const Dual4 vbc = vb - vc;

        const double is_s = scale * spec_.is_amps;
        const Dual4 ifwd = pn_junction_current(vbe, is_s, spec_.nf * vt, 0.0);
        const Dual4 irev = pn_junction_current(vbc, is_s, spec_.nr * vt, 0.0);

        /* Gummel-Poon Early voltage (q1) and high-level injection knee (q2). */
        Dual4 inv_q1 = Dual4::constant(1.0);
        if (spec_.vaf_volts > 0.0) inv_q1 = inv_q1 - vbc / spec_.vaf_volts;
        if (spec_.var_volts > 0.0) inv_q1 = inv_q1 - vbe / spec_.var_volts;
        const Dual4 q1 = 1.0 / d_smooth_floor(inv_q1, 0.1, 0.02);

        Dual4 q2 = Dual4::constant(0.0);
        if (spec_.ikf_amps > 0.0) q2 = q2 + ifwd / (scale * spec_.ikf_amps);
        if (spec_.ikr_amps > 0.0) q2 = q2 + irev / (scale * spec_.ikr_amps);
        const Dual4 qb = 0.5 * q1 * (1.0 + d_sqrt(d_smooth_floor(1.0 + 4.0 * q2, 1.0e-4, 1.0e-4)));

        const Dual4 it = (ifwd - irev) / qb;

        Dual4 ib_be = ifwd / spec_.bf + 1.0e-12 * vbe;
        if (spec_.ise_amps > 0.0) {
            ib_be = ib_be + pn_junction_current(vbe, scale * spec_.ise_amps, spec_.ne * vt, 0.0);
        }
        Dual4 ib_bc = irev / spec_.br + 1.0e-12 * vbc;
        if (spec_.isc_amps > 0.0) {
            ib_bc = ib_bc + pn_junction_current(vbc, scale * spec_.isc_amps, spec_.nc * vt, 0.0);
        }

        const Dual4 ic = it - ib_bc;
        const Dual4 ib = ib_be + ib_bc;
        const Dual4 ie = -(ic + ib);

        /* Depletion + diffusion transit-time charges (conserves Qc + Qb + Qe = 0). */
        const Dual4 qbe = pn_depletion_charge(vbe, scale * spec_.cje_farads, spec_.vje_volts, spec_.mje) +
                          spec_.tf_s * (ifwd / qb);
        const Dual4 qbc = pn_depletion_charge(vbc, scale * spec_.cjc_farads, spec_.vjc_volts, spec_.mjc) +
                          spec_.tr_s * irev;

        const std::array<Dual4, 3> f_terms = {sgn * ic, sgn * ib, sgn * ie};
        const std::array<Dual4, 3> q_terms = {sgn * (-qbc), sgn * (qbe + qbc), sgn * (-qbe)};
        return stamp_terminal_duals<3>(target, terms_, slots_, f_terms, q_terms);
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::array<std::optional<UnknownId>, 3> terms_;
    std::array<std::array<std::optional<SlotId>, 3>, 3> slots_;
    core::BjtSpec spec_;
};

/* ==================== 3. Native Shichman-Hodges JFET Device (NJF / PJF) ==================== */
class JfetDevice final : public Device {
public:
    JfetDevice(
        std::optional<UnknownId> d,
        std::optional<UnknownId> g,
        std::optional<UnknownId> s,
        core::JfetSpec spec)
        : terms_{d, g, s}, spec_(std::move(spec)) {
        desc_.kind_id = "spice.jfet";
        desc_.display_name = spec_.is_pjf ? "P-Channel JFET" : "N-Channel JFET";
        desc_.pins = {{"d", "drain"}, {"g", "gate"}, {"s", "source"}};
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        return bind_dense_block<3>(pat, terms_, slots_);
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& target) override {
        const double sgn = spec_.is_pjf ? -1.0 : 1.0;
        const double vt = std::max(0.01, kBoltzmannQ * ctx.temperature_k);
        const double scale = spec_.area * spec_.m_mult;

        /* Indices: 0=d, 1=g, 2=s */
        const Dual4 vd_raw = sgn * Dual4::variable(node_v(ctx.x, terms_[0]), 0);
        const Dual4 vg = sgn * Dual4::variable(node_v(ctx.x, terms_[1]), 1);
        const Dual4 vs_raw = sgn * Dual4::variable(node_v(ctx.x, terms_[2]), 2);

        const bool reversed = (vd_raw.v < vs_raw.v);
        const Dual4 vd = reversed ? vs_raw : vd_raw;
        const Dual4 vs = reversed ? vd_raw : vs_raw;

        const Dual4 vgs = vg - vs;
        const Dual4 vgd = vg - vd;
        const Dual4 vds = vd - vs;

        /* Shichman-Hodges JFET conduction with smooth cutoff and saturation transition. */
        const Dual4 vov = d_smooth_floor(vgs - spec_.vto_volts, 0.0, 2.0e-3);
        const Dual4 vds_eff = vov - 0.5 * (vov - vds + d_sqrt((vov - vds) * (vov - vds) + 1.0e-6));
        const Dual4 ids = scale * spec_.beta_a_per_v2 *
                              (2.0 * vov * vds_eff - vds_eff * vds_eff) * (1.0 + spec_.lambda_per_v * vds) +
                          1.0e-12 * vds;

        const Dual4 igs = pn_junction_current(vgs, scale * spec_.is_amps, vt, 1.0e-12);
        const Dual4 igd = pn_junction_current(vgd, scale * spec_.is_amps, vt, 1.0e-12);

        const Dual4 qgs = pn_depletion_charge(vgs, scale * spec_.cgs_farads, spec_.pb_volts, 0.5);
        const Dual4 qgd = pn_depletion_charge(vgd, scale * spec_.cgd_farads, spec_.pb_volts, 0.5);

        const Dual4 i_d_eff = ids - igd;
        const Dual4 i_g_eff = igs + igd;
        const Dual4 i_s_eff = -ids - igs;

        const std::array<Dual4, 3> f_terms = {
            sgn * (reversed ? i_s_eff : i_d_eff),
            sgn * i_g_eff,
            sgn * (reversed ? i_d_eff : i_s_eff),
        };
        const std::array<Dual4, 3> q_terms = {
            sgn * (reversed ? -qgs : -qgd),
            sgn * (qgs + qgd),
            sgn * (reversed ? -qgd : -qgs),
        };
        return stamp_terminal_duals<3>(target, terms_, slots_, f_terms, q_terms);
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::array<std::optional<UnknownId>, 3> terms_;
    std::array<std::array<std::optional<SlotId>, 3>, 3> slots_;
    core::JfetSpec spec_;
};

}  // namespace

void record_mosfet_stencil(
    StencilCoords& coords,
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s,
    std::optional<core::UnknownId> b) {
    record_dense_block<4>(coords, {d, g, s, b});
}

std::unique_ptr<dae::Device> create_mosfet_device(
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s,
    std::optional<core::UnknownId> b,
    core::MosfetSpec spec) {
    return std::make_unique<MosfetDevice>(d, g, s, b, std::move(spec));
}

void record_bjt_stencil(
    StencilCoords& coords,
    std::optional<core::UnknownId> c,
    std::optional<core::UnknownId> b,
    std::optional<core::UnknownId> e) {
    record_dense_block<3>(coords, {c, b, e});
}

std::unique_ptr<dae::Device> create_bjt_device(
    std::optional<core::UnknownId> c,
    std::optional<core::UnknownId> b,
    std::optional<core::UnknownId> e,
    core::BjtSpec spec) {
    return std::make_unique<BjtDevice>(c, b, e, std::move(spec));
}

void record_jfet_stencil(
    StencilCoords& coords,
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s) {
    record_dense_block<3>(coords, {d, g, s});
}

std::unique_ptr<dae::Device> create_jfet_device(
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s,
    core::JfetSpec spec) {
    return std::make_unique<JfetDevice>(d, g, s, std::move(spec));
}

}  // namespace spice::devices
