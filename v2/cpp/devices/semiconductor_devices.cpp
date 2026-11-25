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
 * K-variable forward-mode Dual number for machine-precision analytical Jacobians
 * dF/dx and dQ/dx across multi-terminal + internal-node nonlinear semiconductor equations.
 */
template <std::size_t K>
struct DualN {
    double v{0.0};
    std::array<double, K> d{};

    static DualN constant(double c) noexcept {
        DualN r{};
        r.v = c;
        return r;
    }
    static DualN variable(double val, std::size_t idx) noexcept {
        DualN r{};
        r.v = val;
        r.d[idx] = 1.0;
        return r;
    }

    DualN operator-() const noexcept {
        DualN r{};
        r.v = -v;
        for (std::size_t i = 0; i < K; ++i) r.d[i] = -d[i];
        return r;
    }

    DualN operator+(const DualN& o) const noexcept {
        DualN r{};
        r.v = v + o.v;
        for (std::size_t i = 0; i < K; ++i) r.d[i] = d[i] + o.d[i];
        return r;
    }
    DualN operator-(const DualN& o) const noexcept {
        DualN r{};
        r.v = v - o.v;
        for (std::size_t i = 0; i < K; ++i) r.d[i] = d[i] - o.d[i];
        return r;
    }
    DualN operator*(const DualN& o) const noexcept {
        DualN r{};
        r.v = v * o.v;
        for (std::size_t i = 0; i < K; ++i) r.d[i] = v * o.d[i] + o.v * d[i];
        return r;
    }
    DualN operator/(const DualN& o) const noexcept {
        const double inv = 1.0 / o.v;
        const double inv2 = inv * inv;
        DualN r{};
        r.v = v * inv;
        for (std::size_t i = 0; i < K; ++i) r.d[i] = (d[i] * o.v - v * o.d[i]) * inv2;
        return r;
    }
};

using Dual4 = DualN<4>;
using Dual5 = DualN<5>;
using Dual6 = DualN<6>;
using Dual7 = DualN<7>;

template <std::size_t K>
DualN<K> operator+(double c, const DualN<K>& x) noexcept {
    return DualN<K>::constant(c) + x;
}
template <std::size_t K>
DualN<K> operator+(const DualN<K>& x, double c) noexcept {
    return x + DualN<K>::constant(c);
}
template <std::size_t K>
DualN<K> operator-(double c, const DualN<K>& x) noexcept {
    return DualN<K>::constant(c) - x;
}
template <std::size_t K>
DualN<K> operator-(const DualN<K>& x, double c) noexcept {
    return x - DualN<K>::constant(c);
}
template <std::size_t K>
DualN<K> operator*(double c, const DualN<K>& x) noexcept {
    DualN<K> r{};
    r.v = c * x.v;
    for (std::size_t i = 0; i < K; ++i) r.d[i] = c * x.d[i];
    return r;
}
template <std::size_t K>
DualN<K> operator*(const DualN<K>& x, double c) noexcept {
    return c * x;
}
template <std::size_t K>
DualN<K> operator/(const DualN<K>& x, double c) noexcept {
    return (1.0 / c) * x;
}
template <std::size_t K>
DualN<K> operator/(double c, const DualN<K>& x) noexcept {
    return DualN<K>::constant(c) / x;
}

template <std::size_t K>
DualN<K> d_sqrt(const DualN<K>& x) noexcept {
    const double s = std::sqrt(std::max(x.v, 1.0e-30));
    const double k = 0.5 / s;
    DualN<K> r{};
    r.v = s;
    for (std::size_t i = 0; i < K; ++i) r.d[i] = k * x.d[i];
    return r;
}

/* Overflow-safe exponential with C^1 linear continuation above x_max = 40. */
template <std::size_t K>
DualN<K> d_safe_exp(const DualN<K>& x, double x_max = 40.0) noexcept {
    DualN<K> r{};
    if (x.v <= x_max) {
        const double e = std::exp(std::max(x.v, -80.0));
        const double de = (x.v < -80.0) ? 0.0 : e;
        r.v = e;
        for (std::size_t i = 0; i < K; ++i) r.d[i] = de * x.d[i];
        return r;
    }
    const double em = std::exp(x_max);
    r.v = em * (1.0 + (x.v - x_max));
    for (std::size_t i = 0; i < K; ++i) r.d[i] = em * x.d[i];
    return r;
}

/* Overflow-safe log(1 + exp(x)) = softplus(x) with exact sigmoid derivative. */
template <std::size_t K>
DualN<K> d_softplus(const DualN<K>& x) noexcept {
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
    DualN<K> r{};
    r.v = val;
    for (std::size_t i = 0; i < K; ++i) r.d[i] = sig * x.d[i];
    return r;
}

template <std::size_t K>
DualN<K> d_tanh(const DualN<K>& x) noexcept {
    const double xc = std::clamp(x.v, -30.0, 30.0);
    const double th = std::tanh(xc);
    const double dth = (std::abs(x.v) > 30.0) ? 0.0 : (1.0 - th * th);
    DualN<K> r{};
    r.v = th;
    for (std::size_t i = 0; i < K; ++i) r.d[i] = dth * x.d[i];
    return r;
}

/* Overflow-safe ln(cosh(x)) with exact derivative tanh(x). */
template <std::size_t K>
DualN<K> d_log_cosh(const DualN<K>& x) noexcept {
    const double ax = std::abs(x.v);
    const double val = ax + std::log1p(std::exp(-2.0 * ax)) - std::log(2.0);
    const double th = std::tanh(std::clamp(x.v, -30.0, 30.0));
    DualN<K> r{};
    r.v = val;
    for (std::size_t i = 0; i < K; ++i) r.d[i] = th * x.d[i];
    return r;
}

/* Smooth lower floor: smooth_max(x, floor_val, eps) = floor + 0.5*((x-f) + sqrt((x-f)^2 + eps^2)). */
template <std::size_t K>
DualN<K> d_smooth_floor(const DualN<K>& x, double floor_val, double eps = 1.0e-3) noexcept {
    const DualN<K> dx = x - floor_val;
    return floor_val + 0.5 * (dx + d_sqrt(dx * dx + eps * eps));
}

/* SPICE3f5 silicon bandgap Varshni temperature model referenced to eg_nom at tnom_k. */
double spice_bandgap_ev(double t_k, double eg_nom_ev, double tnom_k) noexcept {
    const double shift =
        7.02e-4 * ((t_k * t_k) / (t_k + 1108.0) - (tnom_k * tnom_k) / (tnom_k + 1108.0));
    return std::max(0.1, eg_nom_ev - shift);
}

/* SPICE3f5 saturation current temperature scaling: exact identity when t_k == tnom_k. */
double spice_scale_is(
    double is_nom,
    double t_k,
    double tnom_k,
    double eg_nom_ev,
    double xti,
    double n_em) noexcept {
    if (is_nom <= 0.0 || std::abs(t_k - tnom_k) < 1.0e-9) return is_nom;
    const double ratio = t_k / tnom_k;
    const double vt_t = kBoltzmannQ * t_k;
    const double vt_nom = kBoltzmannQ * tnom_k;
    const double exp_arg = std::clamp((eg_nom_ev / vt_nom - eg_nom_ev / vt_t) / std::max(0.1, n_em), -60.0, 60.0);
    return is_nom * std::pow(ratio, xti / std::max(0.1, n_em)) * std::exp(exp_arg);
}

/* SPICE3f5 junction potential & zero-bias capacitance temperature scaling. */
void spice_scale_vj_cjo(
    double vj_nom,
    double cjo_nom,
    double m_grad,
    double t_k,
    double tnom_k,
    double eg_nom_ev,
    double& out_vj,
    double& out_cjo) noexcept {
    if (std::abs(t_k - tnom_k) < 1.0e-9) {
        out_vj = vj_nom;
        out_cjo = cjo_nom;
        return;
    }
    const double ratio = t_k / tnom_k;
    const double vt_t = kBoltzmannQ * t_k;
    const double eg_t = spice_bandgap_ev(t_k, eg_nom_ev, tnom_k);
    const double vj_t = ratio * vj_nom - 3.0 * vt_t * std::log(ratio) - (ratio * eg_nom_ev - eg_t);
    out_vj = std::max(0.05, vj_t);
    const double factor = 1.0 + m_grad * (4.0e-4 * (t_k - tnom_k) + (1.0 - out_vj / vj_nom));
    out_cjo = std::max(0.0, cjo_nom * factor);
}

/* Smooth PN junction current I = Is * (safe_exp(v / nvt) - 1) + gmin * v. */
template <std::size_t K>
DualN<K> pn_junction_current(const DualN<K>& v, double is_amps, double nvt, double gmin = 1.0e-12) noexcept {
    if (is_amps <= 0.0) return gmin * v;
    const DualN<K> arg = v / nvt;
    if (arg.v < 0.0) {
        /* Exact expm1 near zero for zero cancellation error. */
        const double val = is_amps * std::expm1(std::max(arg.v, -80.0));
        const double g = (arg.v < -80.0) ? 0.0 : (is_amps / nvt) * std::exp(arg.v);
        DualN<K> r{};
        r.v = val;
        for (std::size_t i = 0; i < K; ++i) r.d[i] = g * v.d[i];
        return r + gmin * v;
    }
    return is_amps * (d_safe_exp(arg, 40.0) - 1.0) + gmin * v;
}

/* Smooth reverse breakdown current (negative current when v < -bv_volts). */
template <std::size_t K>
DualN<K> pn_breakdown_current(const DualN<K>& v, double bv_volts, double ibv_amps, double nvt) noexcept {
    if (bv_volts <= 0.0 || ibv_amps <= 0.0) return DualN<K>::constant(0.0);
    const DualN<K> arg = -(v + bv_volts) / nvt;
    if (arg.v < -60.0) return DualN<K>::constant(0.0);
    return -ibv_amps * d_safe_exp(arg, 40.0);
}

/* Smooth PN depletion charge Q_dep(v) with C^1 continuation above fc*vj. */
template <std::size_t K>
DualN<K> pn_depletion_charge(const DualN<K>& v, double cj0, double vj, double mj, double fc = 0.5) noexcept {
    if (cj0 <= 0.0) return DualN<K>::constant(0.0);
    const double v_fc = fc * vj;
    const double one_m_fc = 1.0 - fc;
    if (v.v <= v_fc) {
        const double base = std::max(1.0 - v.v / vj, 1.0e-6);
        const double q = (cj0 * vj / (1.0 - mj)) * (1.0 - std::pow(base, 1.0 - mj));
        const double c = cj0 * std::pow(base, -mj);
        DualN<K> r{};
        r.v = q;
        for (std::size_t i = 0; i < K; ++i) r.d[i] = c * v.d[i];
        return r;
    }
    const double q_fc = (cj0 * vj / (1.0 - mj)) * (1.0 - std::pow(one_m_fc, 1.0 - mj));
    const double c_fc = cj0 * std::pow(one_m_fc, -mj);
    const double dc_fc = (cj0 * mj / vj) * std::pow(one_m_fc, -mj - 1.0);
    const DualN<K> dv = v - v_fc;
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

template <std::size_t K, std::size_t D = K>
Diagnostic stamp_terminal_duals(
    StampTarget& t,
    const std::array<std::optional<UnknownId>, K>& terms,
    const std::array<std::array<std::optional<SlotId>, K>, K>& slots,
    const std::array<DualN<D>, K>& f_terms,
    const std::array<DualN<D>, K>& q_terms) {
    static_assert(D >= K, "DualN derivative width must cover terminal block");
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
        core::BjtSpec spec,
        std::optional<UnknownId> s,
        std::optional<UnknownId> ci,
        std::optional<UnknownId> bi,
        std::optional<UnknownId> ei)
        : terms_{c, b, e, s, ci, bi, ei}, spec_(std::move(spec)) {
        desc_.kind_id = "spice.bjt.gp";
        desc_.display_name = spec_.is_pnp ? "PNP BJT (Gummel-Poon)" : "NPN BJT (Gummel-Poon)";
        desc_.pins = {{"c", "collector"}, {"b", "base"}, {"e", "emitter"}, {"s", "substrate"}};
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        return bind_dense_block<7>(pat, terms_, slots_);
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& target) override {
        const double sgn = spec_.is_pnp ? -1.0 : 1.0;
        const double t_k = std::max(50.0, ctx.temperature_k);
        const double tnom_k = spec_.tnom_c + 273.15;
        const double vt = std::max(0.005, kBoltzmannQ * t_k);
        const double scale = spec_.area * spec_.m_mult;

        /* SPICE3f5 temperature scaling (exact identity when t_k == tnom_k). */
        const double t_ratio = t_k / tnom_k;
        const double beta_t_scale =
            (std::abs(spec_.xtb) > 0.0 && std::abs(t_k - tnom_k) > 1.0e-9)
                ? std::pow(t_ratio, spec_.xtb)
                : 1.0;
        const double bf_t = std::max(1.0e-3, spec_.bf * beta_t_scale);
        const double br_t = std::max(1.0e-3, spec_.br * beta_t_scale);
        const double is_t = spice_scale_is(spec_.is_amps, t_k, tnom_k, spec_.eg_ev, spec_.xti, 1.0);
        const double ise_t =
            (spec_.ise_amps > 0.0)
                ? (spice_scale_is(spec_.ise_amps, t_k, tnom_k, spec_.eg_ev, spec_.xti, spec_.ne) / beta_t_scale)
                : 0.0;
        const double isc_t =
            (spec_.isc_amps > 0.0)
                ? (spice_scale_is(spec_.isc_amps, t_k, tnom_k, spec_.eg_ev, spec_.xti, spec_.nc) / beta_t_scale)
                : 0.0;

        double vje_t = spec_.vje_volts;
        double cje_t = spec_.cje_farads;
        spice_scale_vj_cjo(spec_.vje_volts, spec_.cje_farads, spec_.mje, t_k, tnom_k, spec_.eg_ev, vje_t, cje_t);

        double vjc_t = spec_.vjc_volts;
        double cjc_t = spec_.cjc_farads;
        spice_scale_vj_cjo(spec_.vjc_volts, spec_.cjc_farads, spec_.mjc, t_k, tnom_k, spec_.eg_ev, vjc_t, cjc_t);

        double vjs_t = spec_.vjs_volts;
        double cjs_t = spec_.cjs_farads;
        spice_scale_vj_cjo(spec_.vjs_volts, spec_.cjs_farads, spec_.mjs, t_k, tnom_k, spec_.eg_ev, vjs_t, cjs_t);

        /* Terminal & internal node indices: 0=c, 1=b, 2=e, 3=s, 4=ci, 5=bi, 6=ei */
        const bool has_rc = (spec_.rc_ohms > 0.0 && terms_[4].has_value());
        const bool has_rb = (spec_.rb_ohms > 0.0 && terms_[5].has_value());
        const bool has_re = (spec_.re_ohms > 0.0 && terms_[6].has_value());

        const Dual7 vc_ext = sgn * Dual7::variable(node_v(ctx.x, terms_[0]), 0);
        const Dual7 vb_ext = sgn * Dual7::variable(node_v(ctx.x, terms_[1]), 1);
        const Dual7 ve_ext = sgn * Dual7::variable(node_v(ctx.x, terms_[2]), 2);
        const Dual7 vs_ext = sgn * Dual7::variable(node_v(ctx.x, terms_[3]), 3);
        const Dual7 vc_int = has_rc ? (sgn * Dual7::variable(node_v(ctx.x, terms_[4]), 4)) : vc_ext;
        const Dual7 vb_int = has_rb ? (sgn * Dual7::variable(node_v(ctx.x, terms_[5]), 5)) : vb_ext;
        const Dual7 ve_int = has_re ? (sgn * Dual7::variable(node_v(ctx.x, terms_[6]), 6)) : ve_ext;

        const Dual7 vbe = vb_int - ve_int;
        const Dual7 vbc = vb_int - vc_int;
        const Dual7 vbc_ext = vb_ext - vc_int;
        const Dual7 vsc = vs_ext - vc_int;

        const double is_s = scale * is_t;
        const Dual7 ifwd = pn_junction_current(vbe, is_s, spec_.nf * vt, 0.0);
        const Dual7 irev = pn_junction_current(vbc, is_s, spec_.nr * vt, 0.0);

        /* Gummel-Poon Early voltage (q1) and high-level injection knee (q2). */
        Dual7 inv_q1 = Dual7::constant(1.0);
        if (spec_.vaf_volts > 0.0) inv_q1 = inv_q1 - vbc / spec_.vaf_volts;
        if (spec_.var_volts > 0.0) inv_q1 = inv_q1 - vbe / spec_.var_volts;
        const Dual7 q1 = 1.0 / d_smooth_floor(inv_q1, 0.1, 0.02);

        Dual7 q2 = Dual7::constant(0.0);
        if (spec_.ikf_amps > 0.0) q2 = q2 + ifwd / (scale * spec_.ikf_amps);
        if (spec_.ikr_amps > 0.0) q2 = q2 + irev / (scale * spec_.ikr_amps);
        const Dual7 qb = 0.5 * q1 * (1.0 + d_sqrt(d_smooth_floor(1.0 + 4.0 * q2, 1.0e-4, 1.0e-4)));

        const Dual7 it = (ifwd - irev) / qb;

        Dual7 ib_be = ifwd / bf_t + 1.0e-12 * vbe;
        if (ise_t > 0.0) {
            ib_be = ib_be + pn_junction_current(vbe, scale * ise_t, spec_.ne * vt, 0.0);
        }
        Dual7 ib_bc = irev / br_t + 1.0e-12 * vbc;
        if (isc_t > 0.0) {
            ib_bc = ib_bc + pn_junction_current(vbc, scale * isc_t, spec_.nc * vt, 0.0);
        }

        const Dual7 ic_int = it - ib_bc;
        const Dual7 ib_int = ib_be + ib_bc;
        const Dual7 ie_int = -(ic_int + ib_int);

        /* Bias-dependent forward transit time TF_eff (XTF, VTF, ITF). */
        Dual7 tf_eff = Dual7::constant(spec_.tf_s);
        if (spec_.tf_s > 0.0 && spec_.xtf > 0.0) {
            const Dual7 if_pos = d_smooth_floor(ifwd, 0.0, 1.0e-12);
            Dual7 ratio_itf = Dual7::constant(1.0);
            if (spec_.itf_amps > 0.0) {
                ratio_itf = if_pos / (if_pos + scale * spec_.itf_amps);
            }
            Dual7 vtf_exp = Dual7::constant(1.0);
            if (spec_.vtf_volts > 0.0) {
                vtf_exp = d_safe_exp(vbc / (1.44 * spec_.vtf_volts), 20.0);
            }
            tf_eff = spec_.tf_s * (1.0 + spec_.xtf * ratio_itf * ratio_itf * vtf_exp);
        }

        /* Depletion + diffusion charges with XCJC external/internal split and substrate CJS. */
        const Dual7 qbe = pn_depletion_charge(vbe, scale * cje_t, vje_t, spec_.mje, spec_.fc) +
                          tf_eff * (ifwd / qb);
        const Dual7 qbc_int =
            pn_depletion_charge(vbc, scale * spec_.xcjc * cjc_t, vjc_t, spec_.mjc, spec_.fc) +
            spec_.tr_s * irev;
        const Dual7 qbc_ext =
            (spec_.xcjc < 1.0 && cjc_t > 0.0)
                ? pn_depletion_charge(vbc_ext, scale * (1.0 - spec_.xcjc) * cjc_t, vjc_t, spec_.mjc, spec_.fc)
                : Dual7::constant(0.0);
        const Dual7 qcs =
            (cjs_t > 0.0)
                ? pn_depletion_charge(vsc, scale * cjs_t, vjs_t, spec_.mjs, spec_.fc)
                : Dual7::constant(0.0);

        /* Assemble terminal and internal-node KCL currents & charges (strictly conserving sum=0). */
        std::array<Dual7, 7> f_terms{};
        std::array<Dual7, 7> q_terms{};

        const std::size_t idx_c = has_rc ? 4u : 0u;
        const std::size_t idx_b = has_rb ? 5u : 1u;
        const std::size_t idx_e = has_re ? 6u : 2u;

        f_terms[idx_c] = f_terms[idx_c] + ic_int;
        f_terms[idx_b] = f_terms[idx_b] + ib_int;
        f_terms[idx_e] = f_terms[idx_e] + ie_int;

        q_terms[idx_b] = q_terms[idx_b] + qbe + qbc_int;
        q_terms[idx_e] = q_terms[idx_e] - qbe;
        q_terms[idx_c] = q_terms[idx_c] - qbc_int;

        /* External B-Ci split capacitance (1 - XCJC) * CJC */
        q_terms[1] = q_terms[1] + qbc_ext;
        q_terms[idx_c] = q_terms[idx_c] - qbc_ext;

        /* Collector-substrate capacitance CJS (between S and Ci) */
        q_terms[3] = q_terms[3] + qcs;
        q_terms[idx_c] = q_terms[idx_c] - qcs;

        /* Parasitic ohmic resistances RC, RE, and current-dependent RBB(Ib). */
        if (has_rc) {
            const double rc_eff = spec_.rc_ohms / scale;
            const Dual7 i_rc = (vc_ext - vc_int) / rc_eff;
            f_terms[0] = f_terms[0] + i_rc;
            f_terms[4] = f_terms[4] - i_rc;
        }
        if (has_re) {
            const double re_eff = spec_.re_ohms / scale;
            const Dual7 i_re = (ve_ext - ve_int) / re_eff;
            f_terms[2] = f_terms[2] + i_re;
            f_terms[6] = f_terms[6] - i_re;
        }
        if (has_rb) {
            const double rb_eff = spec_.rb_ohms / scale;
            const double rbm_eff = (spec_.rbm_ohms > 0.0 ? std::min(spec_.rbm_ohms, spec_.rb_ohms) : spec_.rb_ohms) / scale;
            Dual7 rbb = Dual7::constant(rb_eff);
            if (spec_.irb_amps > 0.0 && rbm_eff < rb_eff) {
                const Dual7 ib_mag = d_sqrt(ib_int * ib_int + 1.0e-30);
                rbb = rbm_eff + (rb_eff - rbm_eff) / (1.0 + ib_mag / (scale * spec_.irb_amps));
            }
            const Dual7 i_rb = (vb_ext - vb_int) / rbb;
            f_terms[1] = f_terms[1] + i_rb;
            f_terms[5] = f_terms[5] - i_rb;
        }

        for (std::size_t i = 0; i < 7u; ++i) {
            f_terms[i] = sgn * f_terms[i];
            q_terms[i] = sgn * q_terms[i];
        }
        return stamp_terminal_duals<7>(target, terms_, slots_, f_terms, q_terms);
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::array<std::optional<UnknownId>, 7> terms_;
    std::array<std::array<std::optional<SlotId>, 7>, 7> slots_;
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
        return stamp_terminal_duals<3, 4>(target, terms_, slots_, f_terms, q_terms);
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::array<std::optional<UnknownId>, 3> terms_;
    std::array<std::array<std::optional<SlotId>, 3>, 3> slots_;
    core::JfetSpec spec_;
};

/* ==================== 4. Native Power VDMOS Device (N-Chan / P-Chan) ==================== */
class VdmosDevice final : public Device {
public:
    VdmosDevice(
        std::optional<UnknownId> d,
        std::optional<UnknownId> g,
        std::optional<UnknownId> s,
        core::VdmosSpec spec,
        std::optional<UnknownId> di,
        std::optional<UnknownId> gi,
        std::optional<UnknownId> si)
        : terms_{d, g, s, di, gi, si}, spec_(std::move(spec)) {
        desc_.kind_id = "spice.vdmos";
        desc_.display_name = spec_.is_pchan ? "P-Channel Power VDMOS" : "N-Channel Power VDMOS";
        desc_.pins = {{"d", "drain"}, {"g", "gate"}, {"s", "source"}};
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        return bind_dense_block<6>(pat, terms_, slots_);
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& target) override {
        const double sgn = spec_.is_pchan ? -1.0 : 1.0;
        const double t_k = std::max(50.0, ctx.temperature_k);
        const double tnom_k = spec_.tnom_c + 273.15;
        const double dt = t_k - tnom_k;
        const double vt = std::max(0.005, kBoltzmannQ * t_k);
        const double mult = spec_.m_mult;

        /* Temperature-scaled VDMOS parameters. */
        const double vto_mag = std::max(0.05, std::abs(spec_.vto_volts) + spec_.vtotc_v_per_k * dt);
        const double kp_t =
            (std::abs(spec_.kptc) > 0.0 && std::abs(dt) > 1.0e-9)
                ? spec_.kp_a_per_v2 * std::pow(t_k / tnom_k, -spec_.kptc)
                : spec_.kp_a_per_v2;
        const double is_t = spice_scale_is(spec_.is_amps, t_k, tnom_k, spec_.eg_ev, spec_.xti, spec_.n_emission);
        double vj_t = spec_.vj_volts;
        double cjo_t = spec_.cjo_farads;
        spice_scale_vj_cjo(spec_.vj_volts, spec_.cjo_farads, spec_.m_grading, t_k, tnom_k, spec_.eg_ev, vj_t, cjo_t);

        /* Indices: 0=d, 1=g, 2=s, 3=di, 4=gi, 5=si */
        const bool has_rd = (spec_.rd_ohms > 0.0 && terms_[3].has_value());
        const bool has_rg = (spec_.rg_ohms > 0.0 && terms_[4].has_value());
        const bool has_rs = (spec_.rs_ohms > 0.0 && terms_[5].has_value());

        const Dual6 vd_ext = sgn * Dual6::variable(node_v(ctx.x, terms_[0]), 0);
        const Dual6 vg_ext = sgn * Dual6::variable(node_v(ctx.x, terms_[1]), 1);
        const Dual6 vs_ext = sgn * Dual6::variable(node_v(ctx.x, terms_[2]), 2);
        const Dual6 vd_int = has_rd ? (sgn * Dual6::variable(node_v(ctx.x, terms_[3]), 3)) : vd_ext;
        const Dual6 vg_int = has_rg ? (sgn * Dual6::variable(node_v(ctx.x, terms_[4]), 4)) : vg_ext;
        const Dual6 vs_int = has_rs ? (sgn * Dual6::variable(node_v(ctx.x, terms_[5]), 5)) : vs_ext;

        /* Symmetric drain/source swap for reverse channel conduction. */
        const bool reversed = (vd_int.v < vs_int.v);
        const Dual6 vd_ch = reversed ? vs_int : vd_int;
        const Dual6 vs_ch = reversed ? vd_int : vs_int;
        const Dual6 vgs = vg_int - vs_ch;
        const Dual6 vds = vd_ch - vs_ch;

        /* Smooth subthreshold + strong-inversion VDMOS channel with THETA and LAMBDA.
         * Use vds_eff = s(vov) - s(vov - vds) so vds_eff(0) == 0 identically and the
         * channel current is strictly C^1 across the drain-source symmetry reversal at Vds = 0. */
        const double ksub = spec_.ksubthres_volts;
        const Dual6 vov = ksub * d_softplus((vgs - vto_mag) / ksub);
        const Dual6 s_vov = 0.5 * (vov + d_sqrt(vov * vov + 1.0e-6));
        const Dual6 s_diff = 0.5 * ((vov - vds) + d_sqrt((vov - vds) * (vov - vds) + 1.0e-6));
        const Dual6 vds_eff = s_vov - s_diff;
        const Dual6 mob_factor = 1.0 / (1.0 + spec_.theta_per_v * vov);
        const Dual6 ids_chan =
            mult * kp_t * mob_factor * (vov * vds_eff - 0.5 * vds_eff * vds_eff) *
                (1.0 + spec_.lambda_per_v * vds) +
            1.0e-12 * vds;

        /* Antiparallel body diode (conducts source -> drain for N-channel, i.e. v_bd = vs_int - vd_int). */
        const Dual6 v_bd_raw = vs_int - vd_int;
        const double nvt_bd = spec_.n_emission * vt;
        Dual6 v_bd = v_bd_raw;
        if (spec_.rb_ohms > 0.0 && is_t > 0.0) {
            /* Solve local 1D junction voltage v_j + (rb/mult) * I_d(v_j) = v_bd_raw with exact implicit derivative. */
            const double rb_eff = spec_.rb_ohms / mult;
            const double is_eff = mult * is_t;
            double vj_loc = std::min(v_bd_raw.v, 0.8);
            for (int iter = 0; iter < 16; ++iter) {
                const double arg = vj_loc / nvt_bd;
                double id_loc = 0.0;
                double gd_loc = 0.0;
                if (arg <= 40.0) {
                    const double ev = std::exp(std::max(arg, -80.0));
                    id_loc = is_eff * std::expm1(std::max(arg, -80.0));
                    gd_loc = (arg < -80.0) ? 0.0 : (is_eff * ev / nvt_bd);
                } else {
                    const double ev = std::exp(40.0);
                    gd_loc = (is_eff * ev) / nvt_bd;
                    id_loc = is_eff * std::expm1(40.0) + gd_loc * (vj_loc - 40.0 * nvt_bd);
                }
                if (spec_.bv_volts > 0.0) {
                    const double barg = -(vj_loc + spec_.bv_volts) / nvt_bd;
                    if (barg > -60.0) {
                        if (barg <= 40.0) {
                            const double eb = std::exp(barg);
                            id_loc -= mult * spec_.ibv_amps * eb;
                            gd_loc += (mult * spec_.ibv_amps * eb) / nvt_bd;
                        } else {
                            const double eb = std::exp(40.0);
                            const double gb = (mult * spec_.ibv_amps * eb) / nvt_bd;
                            id_loc -= mult * spec_.ibv_amps * eb * (1.0 + (barg - 40.0));
                            gd_loc += gb;
                        }
                    }
                }
                const double res = vj_loc + rb_eff * id_loc - v_bd_raw.v;
                const double dres = 1.0 + rb_eff * gd_loc;
                const double dv = -res / dres;
                vj_loc += std::clamp(dv, -0.5, 0.5);
                if (std::abs(res) <= 1.0e-13 * (1.0 + std::abs(v_bd_raw.v))) break;
            }
            const double arg_f = vj_loc / nvt_bd;
            double gd_f = (arg_f <= 40.0)
                              ? ((arg_f < -80.0) ? 0.0 : (is_eff * std::exp(arg_f) / nvt_bd))
                              : (is_eff * std::exp(40.0) / nvt_bd);
            if (spec_.bv_volts > 0.0) {
                const double barg_f = -(vj_loc + spec_.bv_volts) / nvt_bd;
                if (barg_f > -60.0) {
                    gd_f += (mult * spec_.ibv_amps * std::exp(std::min(barg_f, 40.0))) / nvt_bd;
                }
            }
            const double dvj_dvbd = 1.0 / (1.0 + rb_eff * gd_f);
            v_bd.v = vj_loc;
            for (std::size_t k = 0; k < 6u; ++k) v_bd.d[k] = dvj_dvbd * v_bd_raw.d[k];
        }

        const Dual6 i_bd_fwd = pn_junction_current(v_bd, mult * is_t, nvt_bd, 0.0);
        const Dual6 i_bd_bv = pn_breakdown_current(v_bd, spec_.bv_volts, mult * spec_.ibv_amps, nvt_bd);
        const Dual6 i_bd = i_bd_fwd + i_bd_bv;

        /* Nonlinear charge-conserving VDMOS capacitances (Cgs, nonlinear Miller Cgd(Vgd), body Cds). */
        const Dual6 v_gs_int = vg_int - vs_int;
        const Dual6 v_gd_int = vg_int - vd_int;
        const Dual6 q_gs = (mult * spec_.cgs_farads) * v_gs_int;

        Dual6 q_gd = Dual6::constant(0.0);
        if (spec_.cgdmax_farads > 0.0 || spec_.cgdmin_farads > 0.0) {
            const double cmin = mult * spec_.cgdmin_farads;
            const double cmax = mult * std::max(spec_.cgdmax_farads, spec_.cgdmin_farads);
            const double a = spec_.a_cgd;
            if (v_gd_int.v <= 0.0) {
                /* Cgd(v) = cmin + (cmax - cmin) * (1 + tanh(a * v)) -> exact C^1 integral Q_gd(v). */
                q_gd = cmin * v_gd_int + (cmax - cmin) * (v_gd_int + d_log_cosh(a * v_gd_int) / a);
            } else {
                q_gd = cmax * v_gd_int;
            }
        }
        const Dual6 q_bd =
            pn_depletion_charge(v_bd, mult * cjo_t, vj_t, spec_.m_grading, spec_.fc) +
            spec_.tt_s * i_bd_fwd;

        /* Intrinsic terminal currents & charges at (di, gi, si). */
        const Dual6 id_int_chan = reversed ? (-ids_chan) : ids_chan;
        const Dual6 id_int = id_int_chan - i_bd;
        const Dual6 is_int = -id_int_chan + i_bd;

        std::array<Dual6, 6> f_terms{};
        std::array<Dual6, 6> q_terms{};

        const std::size_t idx_d = has_rd ? 3u : 0u;
        const std::size_t idx_g = has_rg ? 4u : 1u;
        const std::size_t idx_s = has_rs ? 5u : 2u;

        f_terms[idx_d] = f_terms[idx_d] + id_int;
        f_terms[idx_s] = f_terms[idx_s] + is_int;

        q_terms[idx_g] = q_terms[idx_g] + q_gs + q_gd;
        q_terms[idx_d] = q_terms[idx_d] - q_gd - q_bd;
        q_terms[idx_s] = q_terms[idx_s] - q_gs + q_bd;

        if (has_rd) {
            const Dual6 i_rd = (vd_ext - vd_int) / (spec_.rd_ohms / mult);
            f_terms[0] = f_terms[0] + i_rd;
            f_terms[3] = f_terms[3] - i_rd;
        }
        if (has_rg) {
            const Dual6 i_rg = (vg_ext - vg_int) / (spec_.rg_ohms / mult);
            f_terms[1] = f_terms[1] + i_rg;
            f_terms[4] = f_terms[4] - i_rg;
        }
        if (has_rs) {
            const Dual6 i_rs = (vs_ext - vs_int) / (spec_.rs_ohms / mult);
            f_terms[2] = f_terms[2] + i_rs;
            f_terms[5] = f_terms[5] - i_rs;
        }

        for (std::size_t i = 0; i < 6u; ++i) {
            f_terms[i] = sgn * f_terms[i];
            q_terms[i] = sgn * q_terms[i];
        }
        return stamp_terminal_duals<6>(target, terms_, slots_, f_terms, q_terms);
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::array<std::optional<UnknownId>, 6> terms_;
    std::array<std::array<std::optional<SlotId>, 6>, 6> slots_;
    core::VdmosSpec spec_;
};

/* ==================== 5. Native Statz / Curtice MESFET Device (NMF / PMF) ==================== */
class MesfetDevice final : public Device {
public:
    MesfetDevice(
        std::optional<UnknownId> d,
        std::optional<UnknownId> g,
        std::optional<UnknownId> s,
        core::MesfetSpec spec,
        std::optional<UnknownId> di,
        std::optional<UnknownId> si)
        : terms_{d, g, s, di, si}, spec_(std::move(spec)) {
        desc_.kind_id = (spec_.level == 2) ? "spice.mesfet.curtice" : "spice.mesfet.statz";
        desc_.display_name = spec_.is_pmf ? "P-Channel MESFET" : "N-Channel MESFET";
        desc_.pins = {{"d", "drain"}, {"g", "gate"}, {"s", "source"}};
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        return bind_dense_block<5>(pat, terms_, slots_);
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& target) override {
        const double sgn = spec_.is_pmf ? -1.0 : 1.0;
        const double vt = std::max(0.005, kBoltzmannQ * std::max(50.0, ctx.temperature_k));
        const double scale = spec_.area * spec_.m_mult;

        /* Indices: 0=d, 1=g, 2=s, 3=di, 4=si */
        const bool has_rd = (spec_.rd_ohms > 0.0 && terms_[3].has_value());
        const bool has_rs = (spec_.rs_ohms > 0.0 && terms_[4].has_value());

        const Dual5 vd_ext = sgn * Dual5::variable(node_v(ctx.x, terms_[0]), 0);
        const Dual5 vg = sgn * Dual5::variable(node_v(ctx.x, terms_[1]), 1);
        const Dual5 vs_ext = sgn * Dual5::variable(node_v(ctx.x, terms_[2]), 2);
        const Dual5 vd_int = has_rd ? (sgn * Dual5::variable(node_v(ctx.x, terms_[3]), 3)) : vd_ext;
        const Dual5 vs_int = has_rs ? (sgn * Dual5::variable(node_v(ctx.x, terms_[4]), 4)) : vs_ext;

        const bool reversed = (vd_int.v < vs_int.v);
        const Dual5 vd = reversed ? vs_int : vd_int;
        const Dual5 vs = reversed ? vd_int : vs_int;

        const Dual5 vgs = vg - vs;
        const Dual5 vgd = vg - vd;
        const Dual5 vds = vd - vs;

        const Dual5 vov = d_smooth_floor(vgs - spec_.vto_volts, 0.0, 2.0e-3);
        Dual5 ids = Dual5::constant(0.0);
        if (spec_.level == 2) {
            /* Curtice quadratic MESFET: Beta * Vov^2 * (1 + Lambda*Vds) * tanh(Alpha*Vds). */
            ids = scale * spec_.beta_a_per_v2 * (vov * vov) * (1.0 + spec_.lambda_per_v * vds) *
                      d_tanh(spec_.alpha_per_v * vds) +
                  1.0e-12 * vds;
        } else {
            /* Statz/Raytheon MESFET (SPICE3f5): Beta * Vov^2 / (1 + B*Vov) * (1 + Lambda*Vds) * Ksat. */
            const Dual5 u = spec_.alpha_per_v * vds;
            Dual5 ksat = Dual5::constant(1.0);
            if (u.v < 3.0) {
                const Dual5 one_m = 1.0 - u / 3.0;
                ksat = 1.0 - one_m * one_m * one_m;
            }
            ids = scale * (spec_.beta_a_per_v2 * vov * vov / (1.0 + spec_.b_doping * vov)) *
                      (1.0 + spec_.lambda_per_v * vds) * ksat +
                  1.0e-12 * vds;
        }

        const double nvt = spec_.n_emission * vt;
        const Dual5 igs = pn_junction_current(vgs, scale * spec_.is_amps, nvt, 1.0e-12);
        const Dual5 igd = pn_junction_current(vgd, scale * spec_.is_amps, nvt, 1.0e-12);

        const Dual5 qgs = pn_depletion_charge(vgs, scale * spec_.cgs_farads, spec_.pb_volts, 0.5, spec_.fc);
        const Dual5 qgd = pn_depletion_charge(vgd, scale * spec_.cgd_farads, spec_.pb_volts, 0.5, spec_.fc);

        const Dual5 i_d_eff = reversed ? (-ids - igs) : (ids - igd);
        const Dual5 i_g_eff = igs + igd;
        const Dual5 i_s_eff = reversed ? (ids - igd) : (-ids - igs);

        const Dual5 q_d_eff = reversed ? (-qgs) : (-qgd);
        const Dual5 q_g_eff = qgs + qgd;
        const Dual5 q_s_eff = reversed ? (-qgd) : (-qgs);

        std::array<Dual5, 5> f_terms{};
        std::array<Dual5, 5> q_terms{};

        const std::size_t idx_d = has_rd ? 3u : 0u;
        const std::size_t idx_s = has_rs ? 4u : 2u;

        f_terms[idx_d] = f_terms[idx_d] + i_d_eff;
        f_terms[1] = f_terms[1] + i_g_eff;
        f_terms[idx_s] = f_terms[idx_s] + i_s_eff;

        q_terms[idx_d] = q_terms[idx_d] + q_d_eff;
        q_terms[1] = q_terms[1] + q_g_eff;
        q_terms[idx_s] = q_terms[idx_s] + q_s_eff;

        if (has_rd) {
            const Dual5 i_rd = (vd_ext - vd_int) / (spec_.rd_ohms / scale);
            f_terms[0] = f_terms[0] + i_rd;
            f_terms[3] = f_terms[3] - i_rd;
        }
        if (has_rs) {
            const Dual5 i_rs = (vs_ext - vs_int) / (spec_.rs_ohms / scale);
            f_terms[2] = f_terms[2] + i_rs;
            f_terms[4] = f_terms[4] - i_rs;
        }

        for (std::size_t i = 0; i < 5u; ++i) {
            f_terms[i] = sgn * f_terms[i];
            q_terms[i] = sgn * q_terms[i];
        }
        return stamp_terminal_duals<5>(target, terms_, slots_, f_terms, q_terms);
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::array<std::optional<UnknownId>, 5> terms_;
    std::array<std::array<std::optional<SlotId>, 5>, 5> slots_;
    core::MesfetSpec spec_;
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
    std::optional<core::UnknownId> e,
    std::optional<core::UnknownId> s,
    std::optional<core::UnknownId> ci,
    std::optional<core::UnknownId> bi,
    std::optional<core::UnknownId> ei) {
    record_dense_block<7>(coords, {c, b, e, s, ci, bi, ei});
}

std::unique_ptr<dae::Device> create_bjt_device(
    std::optional<core::UnknownId> c,
    std::optional<core::UnknownId> b,
    std::optional<core::UnknownId> e,
    core::BjtSpec spec,
    std::optional<core::UnknownId> s,
    std::optional<core::UnknownId> ci,
    std::optional<core::UnknownId> bi,
    std::optional<core::UnknownId> ei) {
    return std::make_unique<BjtDevice>(c, b, e, std::move(spec), s, ci, bi, ei);
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

void record_vdmos_stencil(
    StencilCoords& coords,
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s,
    std::optional<core::UnknownId> di,
    std::optional<core::UnknownId> gi,
    std::optional<core::UnknownId> si) {
    record_dense_block<6>(coords, {d, g, s, di, gi, si});
}

std::unique_ptr<dae::Device> create_vdmos_device(
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s,
    core::VdmosSpec spec,
    std::optional<core::UnknownId> di,
    std::optional<core::UnknownId> gi,
    std::optional<core::UnknownId> si) {
    return std::make_unique<VdmosDevice>(d, g, s, std::move(spec), di, gi, si);
}

void record_mesfet_stencil(
    StencilCoords& coords,
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s,
    std::optional<core::UnknownId> di,
    std::optional<core::UnknownId> si) {
    record_dense_block<5>(coords, {d, g, s, di, si});
}

std::unique_ptr<dae::Device> create_mesfet_device(
    std::optional<core::UnknownId> d,
    std::optional<core::UnknownId> g,
    std::optional<core::UnknownId> s,
    core::MesfetSpec spec,
    std::optional<core::UnknownId> di,
    std::optional<core::UnknownId> si) {
    return std::make_unique<MesfetDevice>(d, g, s, std::move(spec), di, si);
}

}  // namespace spice::devices
