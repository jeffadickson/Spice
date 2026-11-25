#include "devices/linear_devices.hpp"
#include "devices/expr_vm.hpp"
#include "devices/semiconductor_devices.hpp"
#include "osdi/osdi_loader.hpp"

#include <algorithm>
#include <cmath>
#include <numbers>
#include <optional>
#include <unordered_map>
#include <utility>

namespace spice::devices {

namespace {

using core::CscPattern;
using core::Diagnostic;
using core::NodeId;
using core::SlotId;
using core::StatusCode;
using core::UnknownId;
using dae::AnalysisMode;
using dae::Device;
using dae::DeviceDescriptor;
using dae::EvalContext;
using dae::StampTarget;

using CoordList = std::vector<std::pair<std::uint32_t, std::uint32_t>>;

void add_if_present(CoordList& coords, std::optional<UnknownId> r, std::optional<UnknownId> c) {
    if (r.has_value() && c.has_value()) {
        coords.emplace_back(r->value, c->value);
    }
}

bool bind_optional(
    const CscPattern& pat,
    std::optional<UnknownId> r,
    std::optional<UnknownId> c,
    std::optional<SlotId>& out) {
    out.reset();
    if (!r.has_value() || !c.has_value()) return true;
    SlotId slot{};
    if (!pat.find_slot(r->value, c->value, slot)) return false;
    out = slot;
    return true;
}

double node_voltage(std::span<const double> x, std::optional<UnknownId> u) noexcept {
    return u.has_value() ? x[u->value] : 0.0;
}

bool stamp_f_opt(StampTarget& t, std::optional<UnknownId> r, double val) noexcept {
    return !r.has_value() || t.add_f(*r, val);
}
bool stamp_q_opt(StampTarget& t, std::optional<UnknownId> r, double val) noexcept {
    return !r.has_value() || t.add_q(*r, val);
}
bool stamp_df_opt(StampTarget& t, std::optional<SlotId> s, double val) noexcept {
    return !s.has_value() || t.add_df(*s, val);
}
bool stamp_dq_opt(StampTarget& t, std::optional<SlotId> s, double val) noexcept {
    return !s.has_value() || t.add_dq(*s, val);
}

/* Base helper for independent sources supporting DC sweep override and source continuation. */
class OverridableSourceDevice : public Device {
public:
    virtual void set_dc_override(std::optional<double> value) noexcept = 0;
    virtual void set_transient_override(std::optional<double> value) noexcept = 0;
    virtual void set_source_scale(double scale) noexcept = 0;
};

/* 1. Resistor: F_p = G*(v_p - v_n), F_n = -F_p, Q = 0 */
class ResistorDevice final : public Device {
public:
    ResistorDevice(std::optional<UnknownId> p, std::optional<UnknownId> n, double ohms)
        : p_(p), n_(n), g_(1.0 / ohms) {
        desc_.kind_id = "spice.resistor";
        desc_.display_name = "Resistor";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}};
    }

    static void record_stencil(CoordList& coords, std::optional<UnknownId> p, std::optional<UnknownId> n) {
        add_if_present(coords, p, p);
        add_if_present(coords, p, n);
        add_if_present(coords, n, p);
        add_if_present(coords, n, n);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, p_, pp_) || !bind_optional(pat, p_, n_, pn_) ||
            !bind_optional(pat, n_, p_, np_) || !bind_optional(pat, n_, n_, nn_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing resistor CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double v = node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_);
        const double i = g_ * v;
        if (!stamp_f_opt(t, p_, i) || !stamp_f_opt(t, n_, -i) ||
            !stamp_df_opt(t, pp_, g_) || !stamp_df_opt(t, pn_, -g_) ||
            !stamp_df_opt(t, np_, -g_) || !stamp_df_opt(t, nn_, g_)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "resistor stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    double g_;
    std::optional<SlotId> pp_, pn_, np_, nn_;
};

/* 2. Capacitor: F = 0, Q_p = C*(v_p - v_n), Q_n = -Q_p */
class CapacitorDevice final : public Device {
public:
    CapacitorDevice(std::optional<UnknownId> p, std::optional<UnknownId> n, double farads)
        : p_(p), n_(n), c_(farads) {
        desc_.kind_id = "spice.capacitor";
        desc_.display_name = "Capacitor";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}};
    }

    static void record_stencil(CoordList& coords, std::optional<UnknownId> p, std::optional<UnknownId> n) {
        add_if_present(coords, p, p);
        add_if_present(coords, p, n);
        add_if_present(coords, n, p);
        add_if_present(coords, n, n);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, p_, pp_) || !bind_optional(pat, p_, n_, pn_) ||
            !bind_optional(pat, n_, p_, np_) || !bind_optional(pat, n_, n_, nn_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing capacitor CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double v = node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_);
        const double q = c_ * v;
        if (!stamp_q_opt(t, p_, q) || !stamp_q_opt(t, n_, -q) ||
            !stamp_dq_opt(t, pp_, c_) || !stamp_dq_opt(t, pn_, -c_) ||
            !stamp_dq_opt(t, np_, -c_) || !stamp_dq_opt(t, nn_, c_)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "capacitor stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    double c_;
    std::optional<SlotId> pp_, pn_, np_, nn_;
};

/*
 * 3. Inductor: branch current i_L flows p -> n.
 *    KCL:    F_p = +i_L,  F_n = -i_L
 *    Branch: F_br = (v_p - v_n), Q_br = -L * i_L
 *    Universal DAE F + dQ/dt = 0 -> v_p - v_n - L * di_L/dt = 0.
 */
class InductorDevice final : public Device {
public:
    InductorDevice(std::optional<UnknownId> p, std::optional<UnknownId> n, UnknownId br, double henries)
        : p_(p), n_(n), br_(br), l_(henries) {
        desc_.kind_id = "spice.inductor";
        desc_.display_name = "Inductor";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        UnknownId br) {
        add_if_present(coords, p, br);
        add_if_present(coords, n, br);
        add_if_present(coords, br, p);
        add_if_present(coords, br, n);
        add_if_present(coords, br, br);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, br_, p_br_) || !bind_optional(pat, n_, br_, n_br_) ||
            !bind_optional(pat, br_, p_, br_p_) || !bind_optional(pat, br_, n_, br_n_) ||
            !bind_optional(pat, br_, br_, br_br_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing inductor CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double i_l = ctx.x[br_.value];
        const double v = node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_);
        if (!stamp_f_opt(t, p_, i_l) || !stamp_f_opt(t, n_, -i_l) ||
            !t.add_f(br_, v) || !t.add_q(br_, -l_ * i_l) ||
            !stamp_df_opt(t, p_br_, 1.0) || !stamp_df_opt(t, n_br_, -1.0) ||
            !stamp_df_opt(t, br_p_, 1.0) || !stamp_df_opt(t, br_n_, -1.0) ||
            !stamp_dq_opt(t, br_br_, -l_)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "inductor stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    UnknownId br_;
    double l_;
    std::optional<SlotId> p_br_, n_br_, br_p_, br_n_, br_br_;
};

/*
 * 4. Independent Voltage Source: branch current i_V flows p -> n.
 *    KCL:    F_p = +i_V, F_n = -i_V
 *    Branch: F_br = (v_p - v_n) - V_src(t) = 0
 */
class VoltageSourceDevice final : public OverridableSourceDevice {
public:
    VoltageSourceDevice(
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        UnknownId br,
        core::VoltageSourceSpec spec)
        : p_(p), n_(n), br_(br), spec_(std::move(spec)) {
        desc_.kind_id = "spice.vsrc";
        desc_.display_name = "Independent Voltage Source";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        UnknownId br) {
        add_if_present(coords, p, br);
        add_if_present(coords, n, br);
        add_if_present(coords, br, p);
        add_if_present(coords, br, n);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, br_, p_br_) || !bind_optional(pat, n_, br_, n_br_) ||
            !bind_optional(pat, br_, p_, br_p_) || !bind_optional(pat, br_, n_, br_n_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing voltage source CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const bool is_tran = (ctx.mode == AnalysisMode::Transient);
        double raw_vsrc = 0.0;
        if (is_tran && tran_override_.has_value()) {
            raw_vsrc = *tran_override_;
        } else if (!is_tran && dc_override_.has_value()) {
            raw_vsrc = *dc_override_;
        } else {
            raw_vsrc = core::source_value_at(spec_, ctx.time_s, is_tran);
        }
        const double vsrc = source_scale_ * raw_vsrc;
        const double i_v = ctx.x[br_.value];
        const double v = node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_);
        if (!stamp_f_opt(t, p_, i_v) || !stamp_f_opt(t, n_, -i_v) ||
            !t.add_f(br_, v - vsrc) ||
            !stamp_df_opt(t, p_br_, 1.0) || !stamp_df_opt(t, n_br_, -1.0) ||
            !stamp_df_opt(t, br_p_, 1.0) || !stamp_df_opt(t, br_n_, -1.0)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "voltage source stamp failed");
        }
        return Diagnostic::success();
    }

    void set_dc_override(std::optional<double> value) noexcept override { dc_override_ = value; }
    void set_transient_override(std::optional<double> value) noexcept override { tran_override_ = value; }
    void set_source_scale(double scale) noexcept override { source_scale_ = scale; }
    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    UnknownId br_;
    core::VoltageSourceSpec spec_;
    std::optional<double> dc_override_;
    std::optional<double> tran_override_;
    double source_scale_{1.0};
    std::optional<SlotId> p_br_, n_br_, br_p_, br_n_;
};

/*
 * 5. Independent Current Source: current I_src flows p -> n (leaves p, enters n).
 *    KCL: F_p = +I_src(t), F_n = -I_src(t)
 */
class CurrentSourceDevice final : public OverridableSourceDevice {
public:
    CurrentSourceDevice(
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        core::CurrentSourceSpec spec)
        : p_(p), n_(n), spec_(std::move(spec)) {
        desc_.kind_id = "spice.isrc";
        desc_.display_name = "Independent Current Source";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}};
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }
    Diagnostic bind_slots(const CscPattern&) override { return Diagnostic::success(); }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const bool is_tran = (ctx.mode == AnalysisMode::Transient);
        double raw_isrc = 0.0;
        if (is_tran && tran_override_.has_value()) {
            raw_isrc = *tran_override_;
        } else if (!is_tran && dc_override_.has_value()) {
            raw_isrc = *dc_override_;
        } else {
            raw_isrc = core::source_value_at(spec_, ctx.time_s, is_tran);
        }
        const double isrc = source_scale_ * raw_isrc;
        if (!stamp_f_opt(t, p_, isrc) || !stamp_f_opt(t, n_, -isrc)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "current source stamp failed");
        }
        return Diagnostic::success();
    }

    void set_dc_override(std::optional<double> value) noexcept override { dc_override_ = value; }
    void set_transient_override(std::optional<double> value) noexcept override { tran_override_ = value; }
    void set_source_scale(double scale) noexcept override { source_scale_ = scale; }
    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    core::CurrentSourceSpec spec_;
    std::optional<double> dc_override_;
    std::optional<double> tran_override_;
    double source_scale_{1.0};
};

/* 6. VCCS: current p -> n equals gm * (v_cp - v_cn) */
class VccsDevice final : public Device {
public:
    VccsDevice(
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        std::optional<UnknownId> cp,
        std::optional<UnknownId> cn,
        double gm)
        : p_(p), n_(n), cp_(cp), cn_(cn), gm_(gm) {
        desc_.kind_id = "spice.vccs";
        desc_.display_name = "Voltage-Controlled Current Source";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}, {"cp", "ctrl_pos"}, {"cn", "ctrl_neg"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        std::optional<UnknownId> cp,
        std::optional<UnknownId> cn) {
        add_if_present(coords, p, cp);
        add_if_present(coords, p, cn);
        add_if_present(coords, n, cp);
        add_if_present(coords, n, cn);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, cp_, p_cp_) || !bind_optional(pat, p_, cn_, p_cn_) ||
            !bind_optional(pat, n_, cp_, n_cp_) || !bind_optional(pat, n_, cn_, n_cn_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing VCCS CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double i = gm_ * (node_voltage(ctx.x, cp_) - node_voltage(ctx.x, cn_));
        if (!stamp_f_opt(t, p_, i) || !stamp_f_opt(t, n_, -i) ||
            !stamp_df_opt(t, p_cp_, gm_) || !stamp_df_opt(t, p_cn_, -gm_) ||
            !stamp_df_opt(t, n_cp_, -gm_) || !stamp_df_opt(t, n_cn_, gm_)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "VCCS stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_, cp_, cn_;
    double gm_;
    std::optional<SlotId> p_cp_, p_cn_, n_cp_, n_cn_;
};

/* 7. VCVS: v_p - v_n - gain * (v_cp - v_cn) = 0 */
class VcvsDevice final : public Device {
public:
    VcvsDevice(
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        std::optional<UnknownId> cp,
        std::optional<UnknownId> cn,
        UnknownId br,
        double gain)
        : p_(p), n_(n), cp_(cp), cn_(cn), br_(br), gain_(gain) {
        desc_.kind_id = "spice.vcvs";
        desc_.display_name = "Voltage-Controlled Voltage Source";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}, {"cp", "ctrl_pos"}, {"cn", "ctrl_neg"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        std::optional<UnknownId> cp,
        std::optional<UnknownId> cn,
        UnknownId br) {
        add_if_present(coords, p, br);
        add_if_present(coords, n, br);
        add_if_present(coords, br, p);
        add_if_present(coords, br, n);
        add_if_present(coords, br, cp);
        add_if_present(coords, br, cn);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, br_, p_br_) || !bind_optional(pat, n_, br_, n_br_) ||
            !bind_optional(pat, br_, p_, br_p_) || !bind_optional(pat, br_, n_, br_n_) ||
            !bind_optional(pat, br_, cp_, br_cp_) || !bind_optional(pat, br_, cn_, br_cn_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing VCVS CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double i_e = ctx.x[br_.value];
        const double f_br = (node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_)) -
                            gain_ * (node_voltage(ctx.x, cp_) - node_voltage(ctx.x, cn_));
        if (!stamp_f_opt(t, p_, i_e) || !stamp_f_opt(t, n_, -i_e) || !t.add_f(br_, f_br) ||
            !stamp_df_opt(t, p_br_, 1.0) || !stamp_df_opt(t, n_br_, -1.0) ||
            !stamp_df_opt(t, br_p_, 1.0) || !stamp_df_opt(t, br_n_, -1.0) ||
            !stamp_df_opt(t, br_cp_, -gain_) || !stamp_df_opt(t, br_cn_, gain_)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "VCVS stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_, cp_, cn_;
    UnknownId br_;
    double gain_;
    std::optional<SlotId> p_br_, n_br_, br_p_, br_n_, br_cp_, br_cn_;
};

/* 8. CCCS: current p -> n equals gain * i(ctrl_branch) */
class CccsDevice final : public Device {
public:
    CccsDevice(std::optional<UnknownId> p, std::optional<UnknownId> n, UnknownId ctrl_br, double gain)
        : p_(p), n_(n), ctrl_br_(ctrl_br), gain_(gain) {
        desc_.kind_id = "spice.cccs";
        desc_.display_name = "Current-Controlled Current Source";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        UnknownId ctrl_br) {
        add_if_present(coords, p, ctrl_br);
        add_if_present(coords, n, ctrl_br);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, ctrl_br_, p_c_) || !bind_optional(pat, n_, ctrl_br_, n_c_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing CCCS CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double i = gain_ * ctx.x[ctrl_br_.value];
        if (!stamp_f_opt(t, p_, i) || !stamp_f_opt(t, n_, -i) ||
            !stamp_df_opt(t, p_c_, gain_) || !stamp_df_opt(t, n_c_, -gain_)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "CCCS stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    UnknownId ctrl_br_;
    double gain_;
    std::optional<SlotId> p_c_, n_c_;
};

/* 9. CCVS: v_p - v_n - ohms * i(ctrl_branch) = 0 */
class CcvsDevice final : public Device {
public:
    CcvsDevice(
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        UnknownId br,
        UnknownId ctrl_br,
        double ohms)
        : p_(p), n_(n), br_(br), ctrl_br_(ctrl_br), ohms_(ohms) {
        desc_.kind_id = "spice.ccvs";
        desc_.display_name = "Current-Controlled Voltage Source";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        UnknownId br,
        UnknownId ctrl_br) {
        add_if_present(coords, p, br);
        add_if_present(coords, n, br);
        add_if_present(coords, br, p);
        add_if_present(coords, br, n);
        add_if_present(coords, br, ctrl_br);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, br_, p_br_) || !bind_optional(pat, n_, br_, n_br_) ||
            !bind_optional(pat, br_, p_, br_p_) || !bind_optional(pat, br_, n_, br_n_) ||
            !bind_optional(pat, br_, ctrl_br_, br_ctrl_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing CCVS CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double i_h = ctx.x[br_.value];
        const double f_br =
            (node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_)) - ohms_ * ctx.x[ctrl_br_.value];
        if (!stamp_f_opt(t, p_, i_h) || !stamp_f_opt(t, n_, -i_h) || !t.add_f(br_, f_br) ||
            !stamp_df_opt(t, p_br_, 1.0) || !stamp_df_opt(t, n_br_, -1.0) ||
            !stamp_df_opt(t, br_p_, 1.0) || !stamp_df_opt(t, br_n_, -1.0) ||
            !stamp_df_opt(t, br_ctrl_, -ohms_)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "CCVS stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    UnknownId br_, ctrl_br_;
    double ohms_;
    std::optional<SlotId> p_br_, n_br_, br_p_, br_n_, br_ctrl_;
};

/* 10. Mutual Inductance K: Q_br1 -= M * i_2, Q_br2 -= M * i_1, where M = k * sqrt(L1 * L2). */
class MutualInductanceDevice final : public Device {
public:
    MutualInductanceDevice(UnknownId br1, UnknownId br2, double mutual_henries)
        : br1_(br1), br2_(br2), m_(mutual_henries) {
        desc_.kind_id = "spice.mutual_inductance";
        desc_.display_name = "Mutual Inductance";
    }

    static void record_stencil(CoordList& coords, UnknownId br1, UnknownId br2) {
        coords.emplace_back(br1.value, br2.value);
        coords.emplace_back(br2.value, br1.value);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, br1_, br2_, s12_) || !bind_optional(pat, br2_, br1_, s21_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing mutual inductance CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double i1 = ctx.x[br1_.value];
        const double i2 = ctx.x[br2_.value];
        if (!t.add_q(br1_, -m_ * i2) || !t.add_q(br2_, -m_ * i1) ||
            !stamp_dq_opt(t, s12_, -m_) || !stamp_dq_opt(t, s21_, -m_)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "mutual inductance stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    UnknownId br1_, br2_;
    double m_;
    std::optional<SlotId> s12_, s21_;
};

/*
 * 11. Full Discrete SPICE Diode: Shockley + Recombination (ISR/NR) + High-Injection (IKF) +
 *     Reverse Breakdown / Zener (BV/IBV/NBV) + Series Resistance Rs (internal node) +
 *     Depletion (CJO/VJ/M/FC) & Transit-Time (TT) charge Q(V) + SPICE3f5 Temperature Scaling.
 * Smooth C^1 linear continuation above exp_arg = 40 prevents numeric overflow while preserving
 * exact analytical derivatives across forward conduction and Zener breakdown.
 */
class DiodeDevice final : public Device {
public:
    DiodeDevice(
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        core::DiodeSpec spec,
        std::optional<UnknownId> p_int = std::nullopt)
        : p_(p), n_(n), p_int_(p_int), spec_(std::move(spec)) {
        desc_.kind_id = "spice.diode";
        desc_.display_name = "SPICE Diode";
        desc_.pins = {{"p", "anode"}, {"n", "cathode"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        std::optional<UnknownId> p_int = std::nullopt) {
        if (p_int.has_value()) {
            /* Series resistor between p and p_int; intrinsic junction between p_int and n. */
            add_if_present(coords, p, p);
            add_if_present(coords, p, p_int);
            add_if_present(coords, p_int, p);
            add_if_present(coords, p_int, p_int);
            add_if_present(coords, p_int, n);
            add_if_present(coords, n, p_int);
            add_if_present(coords, n, n);
        } else {
            add_if_present(coords, p, p);
            add_if_present(coords, p, n);
            add_if_present(coords, n, p);
            add_if_present(coords, n, n);
        }
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (p_int_.has_value()) {
            if (!bind_optional(pat, p_, p_, pp_) || !bind_optional(pat, p_, p_int_, p_pi_) ||
                !bind_optional(pat, p_int_, p_, pi_p_) || !bind_optional(pat, p_int_, p_int_, pi_pi_) ||
                !bind_optional(pat, p_int_, n_, pi_n_) || !bind_optional(pat, n_, p_int_, n_pi_) ||
                !bind_optional(pat, n_, n_, nn_)) {
                return Diagnostic::error(StatusCode::InvalidArgument, "missing diode (Rs) CSC slot");
            }
        } else {
            if (!bind_optional(pat, p_, p_, pp_) || !bind_optional(pat, p_, n_, pn_) ||
                !bind_optional(pat, n_, p_, np_) || !bind_optional(pat, n_, n_, nn_)) {
                return Diagnostic::error(StatusCode::InvalidArgument, "missing diode CSC slot");
            }
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        constexpr double kBoltzQ = 8.617333262e-5;
        const double t_k = std::max(50.0, ctx.temperature_k);
        const double tnom_k = spec_.tnom_c + 273.15;
        const double dt = t_k - tnom_k;
        const bool temp_changed = (std::abs(dt) > 1.0e-9);

        const double vt =
            (spec_.vt_explicit || !temp_changed) ? spec_.vt_volts : std::max(0.005, kBoltzQ * t_k);
        const double nvt = spec_.n_emission * vt;
        const double scale = spec_.area * spec_.m_mult;

        /* SPICE3f5 temperature scaling (exact identity when T == Tnom). */
        auto bandgap_ev = [&](double tk) noexcept {
            const double shift =
                7.02e-4 * ((tk * tk) / (tk + 1108.0) - (tnom_k * tnom_k) / (tnom_k + 1108.0));
            return std::max(0.1, spec_.eg_ev - shift);
        };
        auto scale_is = [&](double is_nom, double n_em) noexcept {
            if (is_nom <= 0.0 || !temp_changed) return is_nom;
            const double ratio = t_k / tnom_k;
            const double vt_t = kBoltzQ * t_k;
            const double vt_nom = kBoltzQ * tnom_k;
            const double arg = std::clamp((spec_.eg_ev / vt_nom - spec_.eg_ev / vt_t) / std::max(0.1, n_em), -60.0, 60.0);
            return is_nom * std::pow(ratio, spec_.xti / std::max(0.1, n_em)) * std::exp(arg);
        };

        const double is_eff = scale * scale_is(spec_.is_amps, spec_.n_emission);
        const double isr_eff = scale * scale_is(spec_.isr_amps, spec_.nr);

        double vj_t = spec_.vj_volts;
        double cjo_t = scale * spec_.cjo_farads;
        if (temp_changed && spec_.cjo_farads > 0.0) {
            const double ratio = t_k / tnom_k;
            const double eg_t = bandgap_ev(t_k);
            const double vt_t = kBoltzQ * t_k;
            vj_t = std::max(0.05, ratio * spec_.vj_volts - 3.0 * vt_t * std::log(ratio) - (ratio * spec_.eg_ev - eg_t));
            const double c_fac = 1.0 + spec_.m_grading * (4.0e-4 * dt + (1.0 - vj_t / spec_.vj_volts));
            cjo_t = std::max(0.0, cjo_t * c_fac);
        }

        const double bv_t =
            (spec_.bv_volts > 0.0)
                ? std::max(0.05, spec_.bv_volts * (1.0 + spec_.tbv1 * dt + spec_.tbv2 * dt * dt))
                : 0.0;

        const std::optional<UnknownId> anode_int = p_int_.has_value() ? p_int_ : p_;
        const double vd = node_voltage(ctx.x, anode_int) - node_voltage(ctx.x, n_);
        if (!std::isfinite(vd)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite diode voltage");
        }

        /* 1. Forward Shockley conduction with C^1 exp continuation. */
        const double arg = vd / nvt;
        double id_raw = 0.0;
        double gd_raw = 0.0;
        constexpr double kExpMaxArg = 40.0;
        if (arg <= kExpMaxArg) {
            const double ev = std::exp(std::max(arg, -80.0));
            id_raw = is_eff * std::expm1(std::max(arg, -80.0));
            gd_raw = (arg < -80.0) ? 0.0 : (is_eff * ev) / nvt;
        } else {
            const double ev = std::exp(kExpMaxArg);
            const double v_lim = kExpMaxArg * nvt;
            gd_raw = (is_eff * ev) / nvt;
            id_raw = is_eff * std::expm1(kExpMaxArg) + gd_raw * (vd - v_lim);
        }

        /* 2. High-level injection knee (IKF): I_fwd = I_raw / sqrt(1 + max(I_raw, 0) / I_kf). */
        double id_shockley = id_raw;
        double gd_shockley = gd_raw;
        if (spec_.ikf_amps > 0.0 && id_raw > 0.0) {
            const double ikf_eff = scale * spec_.ikf_amps;
            const double r = id_raw / ikf_eff;
            const double s = std::sqrt(1.0 + r);
            id_shockley = id_raw / s;
            gd_shockley = gd_raw * (1.0 + 0.5 * r) / (s * (1.0 + r));
        }

        /* 3. Recombination current (ISR, NR). */
        double id_rec = 0.0;
        double gd_rec = 0.0;
        if (isr_eff > 0.0) {
            const double nrvt = spec_.nr * vt;
            const double rarg = vd / nrvt;
            if (rarg <= kExpMaxArg) {
                const double ev = std::exp(std::max(rarg, -80.0));
                id_rec = isr_eff * std::expm1(std::max(rarg, -80.0));
                gd_rec = (rarg < -80.0) ? 0.0 : (isr_eff * ev) / nrvt;
            } else {
                const double ev = std::exp(kExpMaxArg);
                gd_rec = (isr_eff * ev) / nrvt;
                id_rec = isr_eff * std::expm1(kExpMaxArg) + gd_rec * (vd - kExpMaxArg * nrvt);
            }
        }

        /* 4. Reverse breakdown / Zener avalanche (BV, IBV, NBV). */
        double id_bv = 0.0;
        double gd_bv = 0.0;
        if (bv_t > 0.0) {
            const double nbv_vt = spec_.nbv * vt;
            const double ibv_eff = scale * spec_.ibv_amps;
            const double barg = -(vd + bv_t) / nbv_vt;
            if (barg > -60.0) {
                if (barg <= kExpMaxArg) {
                    const double eb = std::exp(barg);
                    id_bv = -ibv_eff * eb;
                    gd_bv = (ibv_eff * eb) / nbv_vt;
                } else {
                    const double eb = std::exp(kExpMaxArg);
                    gd_bv = (ibv_eff * eb) / nbv_vt;
                    id_bv = -ibv_eff * eb * (1.0 + (barg - kExpMaxArg));
                }
            }
        }

        const double i_total = id_shockley + id_rec + id_bv + spec_.gmin_siemens * vd;
        const double g_total = gd_shockley + gd_rec + gd_bv + spec_.gmin_siemens;

        /* 5. Transit charge Q_tt = tt * I_shockley + SPICE depletion charge Q_dep(FC). */
        double q_total = spec_.tt_s * id_shockley;
        double c_total = spec_.tt_s * gd_shockley;

        if (cjo_t > 0.0) {
            const double fc = spec_.fc;
            const double fcpb = fc * vj_t;
            const double one_m_m = 1.0 - spec_.m_grading;
            if (vd < fcpb) {
                const double s = std::max(1.0 - vd / vj_t, 1.0e-6);
                const double s_pow = std::pow(s, -spec_.m_grading);
                c_total += cjo_t * s_pow;
                q_total += cjo_t * vj_t * (1.0 - std::pow(s, one_m_m)) / one_m_m;
            } else {
                const double f1 = vj_t * (1.0 - std::pow(1.0 - fc, one_m_m)) / one_m_m;
                const double f2 = std::pow(1.0 - fc, 1.0 + spec_.m_grading);
                const double f3 = 1.0 - fc * (1.0 + spec_.m_grading);
                const double dv = vd - fcpb;
                c_total += (cjo_t / f2) * (f3 + (spec_.m_grading * vd) / vj_t);
                q_total += cjo_t * f1 +
                           (cjo_t / f2) *
                               (f3 * dv + (spec_.m_grading / (2.0 * vj_t)) *
                                              (vd * vd - fcpb * fcpb));
            }
        }

        if (p_int_.has_value()) {
            const double rs_t =
                std::max(1.0e-9, (spec_.rs_ohms / scale) * (1.0 + spec_.trs1 * dt + spec_.trs2 * dt * dt));
            const double g_rs = 1.0 / rs_t;
            const double i_rs = g_rs * (node_voltage(ctx.x, p_) - node_voltage(ctx.x, p_int_));

            if (!stamp_f_opt(t, p_, i_rs) || !stamp_f_opt(t, p_int_, -i_rs + i_total) ||
                !stamp_f_opt(t, n_, -i_total) ||
                !stamp_q_opt(t, p_int_, q_total) || !stamp_q_opt(t, n_, -q_total) ||
                !stamp_df_opt(t, pp_, g_rs) || !stamp_df_opt(t, p_pi_, -g_rs) ||
                !stamp_df_opt(t, pi_p_, -g_rs) || !stamp_df_opt(t, pi_pi_, g_rs + g_total) ||
                !stamp_df_opt(t, pi_n_, -g_total) || !stamp_df_opt(t, n_pi_, -g_total) ||
                !stamp_df_opt(t, nn_, g_total) ||
                !stamp_dq_opt(t, pi_pi_, c_total) || !stamp_dq_opt(t, pi_n_, -c_total) ||
                !stamp_dq_opt(t, n_pi_, -c_total) || !stamp_dq_opt(t, nn_, c_total)) {
                return Diagnostic::error(StatusCode::NumericalFailure, "diode (Rs) stamp failed");
            }
            return Diagnostic::success();
        }

        if (!stamp_f_opt(t, p_, i_total) || !stamp_f_opt(t, n_, -i_total) ||
            !stamp_q_opt(t, p_, q_total) || !stamp_q_opt(t, n_, -q_total) ||
            !stamp_df_opt(t, pp_, g_total) || !stamp_df_opt(t, pn_, -g_total) ||
            !stamp_df_opt(t, np_, -g_total) || !stamp_df_opt(t, nn_, g_total) ||
            !stamp_dq_opt(t, pp_, c_total) || !stamp_dq_opt(t, pn_, -c_total) ||
            !stamp_dq_opt(t, np_, -c_total) || !stamp_dq_opt(t, nn_, c_total)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "diode stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_, p_int_;
    core::DiodeSpec spec_;
    std::optional<SlotId> pp_, pn_, np_, nn_;
    std::optional<SlotId> p_pi_, pi_p_, pi_pi_, pi_n_, n_pi_;
};

/*
 * 12. Compiled Behavioral Source (B_i / B_v) using exact forward-mode autodiff.
 * - Current mode: F_p = +expr(x, t), F_n = -expr(x, t)
 * - Voltage mode: KCL F_p = +i_B, F_n = -i_B; Branch F_br = (v_p - v_n) - expr(x, t) = 0
 */
class BehavioralSourceDevice final : public Device {
public:
    BehavioralSourceDevice(
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        std::optional<UnknownId> br,
        bool is_voltage,
        CompiledExpr expr)
        : p_(p), n_(n), br_(br), is_voltage_(is_voltage), expr_(std::move(expr)) {
        desc_.kind_id = is_voltage ? "spice.bsrc_v" : "spice.bsrc_i";
        desc_.display_name = is_voltage ? "Behavioral Voltage Source" : "Behavioral Current Source";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        std::optional<UnknownId> br,
        bool is_voltage,
        const CompiledExpr& expr) {
        if (is_voltage) {
            add_if_present(coords, p, br);
            add_if_present(coords, n, br);
            add_if_present(coords, br, p);
            add_if_present(coords, br, n);
            for (UnknownId u : expr.referenced_unknowns()) {
                add_if_present(coords, br, u);
            }
        } else {
            for (UnknownId u : expr.referenced_unknowns()) {
                add_if_present(coords, p, u);
                add_if_present(coords, n, u);
            }
        }
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        const auto& refs = expr_.referenced_unknowns();
        if (is_voltage_) {
            if (!bind_optional(pat, p_, br_, p_br_) || !bind_optional(pat, n_, br_, n_br_) ||
                !bind_optional(pat, br_, p_, br_p_) || !bind_optional(pat, br_, n_, br_n_)) {
                return Diagnostic::error(StatusCode::InvalidArgument, "missing B_v CSC slot");
            }
            br_ref_slots_.resize(refs.size());
            for (std::size_t k = 0; k < refs.size(); ++k) {
                if (!bind_optional(pat, br_, refs[k], br_ref_slots_[k])) {
                    return Diagnostic::error(StatusCode::InvalidArgument, "missing B_v derivative CSC slot");
                }
            }
        } else {
            p_ref_slots_.resize(refs.size());
            n_ref_slots_.resize(refs.size());
            for (std::size_t k = 0; k < refs.size(); ++k) {
                if (!bind_optional(pat, p_, refs[k], p_ref_slots_[k]) ||
                    !bind_optional(pat, n_, refs[k], n_ref_slots_[k])) {
                    return Diagnostic::error(StatusCode::InvalidArgument, "missing B_i derivative CSC slot");
                }
            }
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        ExprEvalResult eval{};
        if (auto d = expr_.evaluate(ctx.x, ctx.time_s, eval); !d.ok()) return d;

        if (is_voltage_) {
            const double i_b = ctx.x[br_->value];
            const double f_br = (node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_)) - eval.value;
            if (!stamp_f_opt(t, p_, i_b) || !stamp_f_opt(t, n_, -i_b) || !t.add_f(*br_, f_br) ||
                !stamp_df_opt(t, p_br_, 1.0) || !stamp_df_opt(t, n_br_, -1.0) ||
                !stamp_df_opt(t, br_p_, 1.0) || !stamp_df_opt(t, br_n_, -1.0)) {
                return Diagnostic::error(StatusCode::NumericalFailure, "B_v stamp failed");
            }
            for (std::size_t k = 0; k < eval.partials.size(); ++k) {
                if (!stamp_df_opt(t, br_ref_slots_[k], -eval.partials[k])) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "B_v Jacobian stamp failed");
                }
            }
        } else {
            if (!stamp_f_opt(t, p_, eval.value) || !stamp_f_opt(t, n_, -eval.value)) {
                return Diagnostic::error(StatusCode::NumericalFailure, "B_i residual stamp failed");
            }
            for (std::size_t k = 0; k < eval.partials.size(); ++k) {
                if (!stamp_df_opt(t, p_ref_slots_[k], eval.partials[k]) ||
                    !stamp_df_opt(t, n_ref_slots_[k], -eval.partials[k])) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "B_i Jacobian stamp failed");
                }
            }
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_, br_;
    bool is_voltage_;
    CompiledExpr expr_;
    std::optional<SlotId> p_br_, n_br_, br_p_, br_n_;
    std::vector<std::optional<SlotId>> br_ref_slots_;
    std::vector<std::optional<SlotId>> p_ref_slots_;
    std::vector<std::optional<SlotId>> n_ref_slots_;
};

class SwitchDevice final : public Device {
public:
    SwitchDevice(
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        std::optional<UnknownId> cp,
        std::optional<UnknownId> cn,
        const core::SwitchSpec& spec)
        : p_(p),
          n_(n),
          cp_(cp),
          cn_(cn),
          vt_(spec.vt_volts),
          vh_(spec.vh_volts),
          ln_gon_(std::log(1.0 / spec.ron_ohms)),
          ln_goff_(std::log(1.0 / spec.roff_ohms)),
          hysteresis_(spec.hysteresis),
          initial_on_(spec.initial_on),
          invert_(spec.invert),
          accepted_on_(spec.initial_on),
          trial_on_(spec.initial_on) {
        desc_.kind_id = "spice.switch";
        desc_.display_name = "Voltage-Controlled Switch";
        desc_.pins = {{"p", "passive"}, {"n", "passive"}, {"cp", "ctrl_pos"}, {"cn", "ctrl_neg"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        std::optional<UnknownId> cp,
        std::optional<UnknownId> cn) {
        add_if_present(coords, p, p);
        add_if_present(coords, p, n);
        add_if_present(coords, n, p);
        add_if_present(coords, n, n);
        add_if_present(coords, p, cp);
        add_if_present(coords, p, cn);
        add_if_present(coords, n, cp);
        add_if_present(coords, n, cn);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, p_, pp_) || !bind_optional(pat, p_, n_, pn_) ||
            !bind_optional(pat, n_, p_, np_) || !bind_optional(pat, n_, n_, nn_) ||
            !bind_optional(pat, p_, cp_, pcp_) || !bind_optional(pat, p_, cn_, pcn_) ||
            !bind_optional(pat, n_, cp_, ncp_) || !bind_optional(pat, n_, cn_, ncn_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing switch CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double vd = node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_);
        const double vc_raw = node_voltage(ctx.x, cp_) - node_voltage(ctx.x, cn_);
        const double pol = invert_ ? -1.0 : 1.0;
        const double vc = pol * vc_raw;
        const double vt_eff = pol * vt_;

        double sigma = 0.0;
        double dsigma_dvc = 0.0;
        if (!hysteresis_) {
            const double u = std::clamp((vc - vt_eff) / vh_, -30.0, 30.0);
            const double th = std::tanh(u);
            sigma = 0.5 * (1.0 + th);
            dsigma_dvc = pol * 0.5 * (1.0 - th * th) / vh_;
        } else {
            /* Committed two-state hysteresis: switches ON at vt + vh, OFF at vt - vh. */
            const double thresh = accepted_on_ ? (vt_eff - vh_) : (vt_eff + vh_);
            const double edge_w = std::max(1.0e-5, 0.02 * vh_);
            const double u = std::clamp((vc - thresh) / edge_w, -30.0, 30.0);
            const double th = std::tanh(u);
            sigma = 0.5 * (1.0 + th);
            dsigma_dvc = pol * 0.5 * (1.0 - th * th) / edge_w;
            if (!accepted_on_ && vc >= vt_eff + vh_) {
                trial_on_ = true;
            } else if (accepted_on_ && vc <= vt_eff - vh_) {
                trial_on_ = false;
            } else {
                trial_on_ = accepted_on_;
            }
        }

        const double g = std::exp((1.0 - sigma) * ln_goff_ + sigma * ln_gon_);
        const double i = g * vd;
        const double dg_dvc = g * (ln_gon_ - ln_goff_) * dsigma_dvc;
        const double di_dvc = vd * dg_dvc;

        if (!stamp_f_opt(t, p_, i) || !stamp_f_opt(t, n_, -i) ||
            !stamp_df_opt(t, pp_, g) || !stamp_df_opt(t, pn_, -g) ||
            !stamp_df_opt(t, np_, -g) || !stamp_df_opt(t, nn_, g) ||
            !stamp_df_opt(t, pcp_, di_dvc) || !stamp_df_opt(t, pcn_, -di_dvc) ||
            !stamp_df_opt(t, ncp_, -di_dvc) || !stamp_df_opt(t, ncn_, di_dvc)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "switch stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override { accepted_on_ = trial_on_; }
    void rollback_trial() noexcept override { trial_on_ = accepted_on_; }
    void reset_history() noexcept override {
        accepted_on_ = initial_on_;
        trial_on_ = initial_on_;
    }

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_, cp_, cn_;
    double vt_, vh_, ln_gon_, ln_goff_;
    bool hysteresis_{false};
    bool initial_on_{false};
    bool invert_{false};
    bool accepted_on_{false};
    bool trial_on_{false};
    std::optional<SlotId> pp_, pn_, np_, nn_, pcp_, pcn_, ncp_, ncn_;
};

/*
 * 13b. Current-Controlled Switch (W element): controlled by branch current of a named source.
 * Supports both smooth log-conductance tanh transition and transactional hysteresis.
 */
class CurrentSwitchDevice final : public Device {
public:
    CurrentSwitchDevice(
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        UnknownId ctrl_br,
        const core::CurrentSwitchSpec& spec)
        : p_(p),
          n_(n),
          ctrl_br_(ctrl_br),
          it_(spec.it_amps),
          ih_(spec.ih_amps),
          ln_gon_(std::log(1.0 / spec.ron_ohms)),
          ln_goff_(std::log(1.0 / spec.roff_ohms)),
          hysteresis_(spec.hysteresis),
          initial_on_(spec.initial_on),
          invert_(spec.invert),
          accepted_on_(spec.initial_on),
          trial_on_(spec.initial_on) {
        desc_.kind_id = "spice.cswitch";
        desc_.display_name = "Current-Controlled Switch";
        desc_.pins = {{"p", "passive"}, {"n", "passive"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        UnknownId ctrl_br) {
        add_if_present(coords, p, p);
        add_if_present(coords, p, n);
        add_if_present(coords, n, p);
        add_if_present(coords, n, n);
        add_if_present(coords, p, ctrl_br);
        add_if_present(coords, n, ctrl_br);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, p_, pp_) || !bind_optional(pat, p_, n_, pn_) ||
            !bind_optional(pat, n_, p_, np_) || !bind_optional(pat, n_, n_, nn_) ||
            !bind_optional(pat, p_, ctrl_br_, p_ctrl_) || !bind_optional(pat, n_, ctrl_br_, n_ctrl_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing current switch CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double vd = node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_);
        const double ic_raw = ctx.x[ctrl_br_.value];
        const double pol = invert_ ? -1.0 : 1.0;
        const double ic = pol * ic_raw;
        const double it_eff = pol * it_;

        double sigma = 0.0;
        double dsigma_dic = 0.0;
        if (!hysteresis_) {
            const double u = std::clamp((ic - it_eff) / ih_, -30.0, 30.0);
            const double th = std::tanh(u);
            sigma = 0.5 * (1.0 + th);
            dsigma_dic = pol * 0.5 * (1.0 - th * th) / ih_;
        } else {
            const double thresh = accepted_on_ ? (it_eff - ih_) : (it_eff + ih_);
            const double edge_w = std::max(1.0e-7, 0.02 * ih_);
            const double u = std::clamp((ic - thresh) / edge_w, -30.0, 30.0);
            const double th = std::tanh(u);
            sigma = 0.5 * (1.0 + th);
            dsigma_dic = pol * 0.5 * (1.0 - th * th) / edge_w;
            if (!accepted_on_ && ic >= it_eff + ih_) {
                trial_on_ = true;
            } else if (accepted_on_ && ic <= it_eff - ih_) {
                trial_on_ = false;
            } else {
                trial_on_ = accepted_on_;
            }
        }

        const double g = std::exp((1.0 - sigma) * ln_goff_ + sigma * ln_gon_);
        const double i = g * vd;
        const double di_dic = vd * g * (ln_gon_ - ln_goff_) * dsigma_dic;

        if (!stamp_f_opt(t, p_, i) || !stamp_f_opt(t, n_, -i) ||
            !stamp_df_opt(t, pp_, g) || !stamp_df_opt(t, pn_, -g) ||
            !stamp_df_opt(t, np_, -g) || !stamp_df_opt(t, nn_, g) ||
            !stamp_df_opt(t, p_ctrl_, di_dic) || !stamp_df_opt(t, n_ctrl_, -di_dic)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "current switch stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override { accepted_on_ = trial_on_; }
    void rollback_trial() noexcept override { trial_on_ = accepted_on_; }
    void reset_history() noexcept override {
        accepted_on_ = initial_on_;
        trial_on_ = initial_on_;
    }

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    UnknownId ctrl_br_;
    double it_, ih_, ln_gon_, ln_goff_;
    bool hysteresis_{false};
    bool initial_on_{false};
    bool invert_{false};
    bool accepted_on_{false};
    bool trial_on_{false};
    std::optional<SlotId> pp_, pn_, np_, nn_, p_ctrl_, n_ctrl_;
};

/*
 * 13c. Jiles-Atherton Saturable Magnetic Core Inductor:
 *   KCL:    F_p = +i_L,  F_n = -i_L
 *   Branch: F_br = (v_p - v_n) - R_w * i_L,  Q_br = -(Phi(i_L) + L_leak * i_L)
 * Supports anhysteretic Langevin saturation (k = 0), optional air gap demagnetization,
 * and full transient B-H hysteresis (k > 0) with exact implicit dPhi/di_L and rollback.
 */
class SaturableCoreDevice final : public Device {
public:
    SaturableCoreDevice(
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        UnknownId br,
        core::SaturableCoreSpec spec)
        : p_(p), n_(n), br_(br), spec_(std::move(spec)) {
        desc_.kind_id = "spice.saturable_core";
        desc_.display_name = "Saturable Magnetic Core Inductor (Jiles-Atherton)";
        desc_.pins = {{"p", "positive"}, {"n", "negative"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p,
        std::optional<UnknownId> n,
        UnknownId br) {
        add_if_present(coords, p, br);
        add_if_present(coords, n, br);
        add_if_present(coords, br, p);
        add_if_present(coords, br, n);
        add_if_present(coords, br, br);
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p_, br_, p_br_) || !bind_optional(pat, n_, br_, n_br_) ||
            !bind_optional(pat, br_, p_, br_p_) || !bind_optional(pat, br_, n_, br_n_) ||
            !bind_optional(pat, br_, br_, br_br_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing saturable core CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        constexpr double kMu0 = 4.0 * std::numbers::pi * 1.0e-7;
        const double i_l = ctx.x[br_.value];
        const double v = node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_);
        if (!std::isfinite(i_l) || !std::isfinite(v)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite saturable core state");
        }

        const double l_tot = spec_.path_m + spec_.gap_m;
        const double gap_frac = spec_.gap_m / l_tot;
        const double core_frac = spec_.path_m / l_tot;
        const double alpha_eff = spec_.alpha_interdomain - gap_frac;
        const double dh0_di = spec_.turns / l_tot;
        const double h0 = dh0_di * i_l;

        /* Langevin function L(u) = coth(u) - 1/u and derivative L'(u) with smooth Taylor series near 0. */
        auto langevin = [](double u, double& l_val, double& dl_du) noexcept {
            const double au = std::abs(u);
            if (au < 1.0e-3) {
                const double u2 = u * u;
                l_val = u * (1.0 / 3.0 - u2 * (1.0 / 45.0 - (2.0 / 945.0) * u2));
                dl_du = 1.0 / 3.0 - u2 * (1.0 / 15.0 - (2.0 / 189.0) * u2);
            } else if (au > 40.0) {
                l_val = (u > 0.0 ? 1.0 : -1.0) - 1.0 / u;
                dl_du = 1.0 / (u * u);
            } else {
                const double th = std::tanh(u);
                const double cth = 1.0 / th;
                const double sh = std::sinh(u);
                l_val = cth - 1.0 / u;
                dl_du = 1.0 / (u * u) - 1.0 / (sh * sh);
            }
        };

        auto eval_man = [&](double he, double& man, double& dman_dhe) noexcept {
            double lv = 0.0, dlv = 0.0;
            langevin(he / spec_.a_a_per_m, lv, dlv);
            man = spec_.ms_a_per_m * lv;
            dman_dhe = (spec_.ms_a_per_m / spec_.a_a_per_m) * dlv;
        };

        double m_val = 0.0;
        double dm_dh0 = 0.0;

        const bool use_hyst =
            (spec_.k_a_per_m > 0.0 && ctx.mode == AnalysisMode::Transient && has_accepted_state_);

        if (!use_hyst) {
            /* Solve implicit anhysteretic magnetization M = M_an(h0 + alpha_eff * M). */
            double lv0 = 0.0, dlv0 = 0.0;
            langevin(h0 / spec_.a_a_per_m, lv0, dlv0);
            m_val = spec_.ms_a_per_m * lv0;
            for (int iter = 0; iter < 24; ++iter) {
                double man = 0.0, dman = 0.0;
                eval_man(h0 + alpha_eff * m_val, man, dman);
                const double res = m_val - man;
                const double dres = 1.0 - alpha_eff * dman;
                const double step = -res / std::max(0.01, dres);
                m_val += step;
                if (std::abs(res) <= 1.0e-12 * (spec_.ms_a_per_m + 1.0)) break;
            }
            double man_f = 0.0, dman_f = 0.0;
            eval_man(h0 + alpha_eff * m_val, man_f, dman_f);
            dm_dh0 = dman_f / std::max(0.01, 1.0 - alpha_eff * dman_f);
        } else {
            /*
             * Implicit Jiles-Atherton hysteresis step from accepted (h0_prev_, m_prev_):
             *   R(M) = (M - m_prev_) - c*(M_an(He) - m_an_prev_)
             *          - (1 - c)*(M_an(He) - M) * s(dh0, M_an - M) / k = 0
             * where s >= 0 activates irreversible domain-wall unpinning when dh0 aligns with (M_an - M).
             */
            const double c_rev = spec_.c_reversibility;
            const double k_pin = spec_.k_a_per_m;
            const double dh0 = h0 - h0_prev_;
            const double h_eps = std::max(1.0e-6, 1.0e-3 * k_pin);
            const double th_h = std::tanh(std::clamp(dh0 / h_eps, -30.0, 30.0));
            const double abs_dh0 = dh0 * th_h;
            const double dabs_dh0 = th_h + (dh0 / h_eps) * (1.0 - th_h * th_h);

            const double pin_scale = std::max(1.0e-12, 1.0e-4 * spec_.ms_a_per_m * k_pin);
            m_val = m_prev_;
            for (int iter = 0; iter < 30; ++iter) {
                double man = 0.0, dman = 0.0;
                eval_man(h0 + alpha_eff * m_val, man, dman);
                const double diff = man - m_val;
                const double prod = dh0 * diff;
                const double u_p = std::clamp(prod / pin_scale, -30.0, 30.0);
                const double th_p = std::tanh(u_p);
                const double gate = 0.5 * (1.0 + th_p);
                const double dgate_dprod = 0.5 * (1.0 - th_p * th_p) / pin_scale;

                const double irr = (1.0 - c_rev) * diff * gate * (abs_dh0 / k_pin);
                const double res = (m_val - m_prev_) - c_rev * (man - m_an_prev_) - irr;

                const double dman_dm = dman * alpha_eff;
                const double ddiff_dm = dman_dm - 1.0;
                const double dgate_dm = dgate_dprod * dh0 * ddiff_dm;
                const double dirr_dm = (1.0 - c_rev) * (abs_dh0 / k_pin) * (ddiff_dm * gate + diff * dgate_dm);
                const double dres_dm = 1.0 - c_rev * dman_dm - dirr_dm;

                const double step = -res / std::max(0.05, dres_dm);
                m_val += std::clamp(step, -0.5 * spec_.ms_a_per_m, 0.5 * spec_.ms_a_per_m);
                if (std::abs(res) <= 1.0e-11 * (spec_.ms_a_per_m + 1.0)) break;
            }

            /* Exact implicit derivative dM/dH0 = -(dR/dH0) / (dR/dM). */
            double man = 0.0, dman = 0.0;
            eval_man(h0 + alpha_eff * m_val, man, dman);
            const double diff = man - m_val;
            const double u_p = std::clamp((dh0 * diff) / pin_scale, -30.0, 30.0);
            const double th_p = std::tanh(u_p);
            const double gate = 0.5 * (1.0 + th_p);
            const double dgate_dprod = 0.5 * (1.0 - th_p * th_p) / pin_scale;

            const double dman_dm = dman * alpha_eff;
            const double ddiff_dm = dman_dm - 1.0;
            const double dirr_dm =
                (1.0 - c_rev) * (abs_dh0 / k_pin) * (ddiff_dm * gate + diff * dgate_dprod * dh0 * ddiff_dm);
            const double dres_dm = std::max(0.05, 1.0 - c_rev * dman_dm - dirr_dm);

            const double dprod_dh0 = diff + dh0 * dman;
            const double dgate_dh0 = dgate_dprod * dprod_dh0;
            const double dirr_dh0 =
                ((1.0 - c_rev) / k_pin) *
                (dman * gate * abs_dh0 + diff * dgate_dh0 * abs_dh0 + diff * gate * dabs_dh0);
            const double dres_dh0 = -c_rev * dman - dirr_dh0;
            dm_dh0 = std::max(0.0, -dres_dh0 / dres_dm);
        }

        double man_now = 0.0, dman_now = 0.0;
        eval_man(h0 + alpha_eff * m_val, man_now, dman_now);
        h0_trial_ = h0;
        m_trial_ = m_val;
        m_an_trial_ = man_now;
        has_trial_ = true;

        const double b_tesla = kMu0 * (h0 + core_frac * m_val);
        const double db_di = kMu0 * dh0_di * (1.0 + core_frac * dm_dh0);
        const double phi = spec_.turns * spec_.area_m2 * b_tesla + spec_.l_leak_henries * i_l;
        const double l_diff = spec_.turns * spec_.area_m2 * db_di + spec_.l_leak_henries;

        const double f_br = v - spec_.r_winding_ohms * i_l;
        if (!stamp_f_opt(t, p_, i_l) || !stamp_f_opt(t, n_, -i_l) ||
            !t.add_f(br_, f_br) || !t.add_q(br_, -phi) ||
            !stamp_df_opt(t, p_br_, 1.0) || !stamp_df_opt(t, n_br_, -1.0) ||
            !stamp_df_opt(t, br_p_, 1.0) || !stamp_df_opt(t, br_n_, -1.0) ||
            !stamp_df_opt(t, br_br_, -spec_.r_winding_ohms) ||
            !stamp_dq_opt(t, br_br_, -l_diff)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "saturable core stamp failed");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override {
        if (!has_trial_) return;
        h0_prev_ = h0_trial_;
        m_prev_ = m_trial_;
        m_an_prev_ = m_an_trial_;
        has_accepted_state_ = true;
        has_trial_ = false;
    }

    void rollback_trial() noexcept override { has_trial_ = false; }

    void reset_history() noexcept override {
        has_accepted_state_ = false;
        has_trial_ = false;
        h0_prev_ = 0.0;
        m_prev_ = 0.0;
        m_an_prev_ = 0.0;
    }

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    UnknownId br_;
    core::SaturableCoreSpec spec_;
    std::optional<SlotId> p_br_, n_br_, br_p_, br_n_, br_br_;
    bool has_accepted_state_{false};
    bool has_trial_{false};
    double h0_prev_{0.0};
    double m_prev_{0.0};
    double m_an_prev_{0.0};
    double h0_trial_{0.0};
    double m_trial_{0.0};
    double m_an_trial_{0.0};
};

/*
 * 14. Ideal Lossless Transmission Line (T element):
 *   - DC OP/Sweep: V1 - V2 = 0, I1 + I2 = 0 (zero-loss short)
 *   - Transient: Exact Bergeron method-of-characteristics with committed delay history & rollback
 *   - AC / .SP: Exact complex ABCD equations (non-singular at DC and half-wave multiples)
 */
class TransmissionLineDevice final : public Device {
public:
    TransmissionLineDevice(
        std::optional<UnknownId> p1,
        std::optional<UnknownId> n1,
        std::optional<UnknownId> p2,
        std::optional<UnknownId> n2,
        UnknownId br1,
        UnknownId br2,
        const core::TransmissionLineSpec& spec)
        : p1_(p1),
          n1_(n1),
          p2_(p2),
          n2_(n2),
          br1_(br1),
          br2_(br2),
          z0_(spec.z0_ohms),
          y0_(1.0 / spec.z0_ohms),
          td_(spec.td_s) {
        desc_.kind_id = "spice.tline";
        desc_.display_name = "Ideal Lossless Transmission Line";
        desc_.pins = {{"p1", "port1_pos"}, {"n1", "port1_neg"}, {"p2", "port2_pos"}, {"n2", "port2_neg"}};
    }

    static void record_stencil(
        CoordList& coords,
        std::optional<UnknownId> p1,
        std::optional<UnknownId> n1,
        std::optional<UnknownId> p2,
        std::optional<UnknownId> n2,
        UnknownId br1,
        UnknownId br2) {
        add_if_present(coords, p1, br1);
        add_if_present(coords, n1, br1);
        add_if_present(coords, p2, br2);
        add_if_present(coords, n2, br2);

        const UnknownId rows[2] = {br1, br2};
        for (UnknownId r : rows) {
            add_if_present(coords, r, p1);
            add_if_present(coords, r, n1);
            add_if_present(coords, r, p2);
            add_if_present(coords, r, n2);
            coords.emplace_back(r.value, br1.value);
            coords.emplace_back(r.value, br2.value);
        }
    }

    const DeviceDescriptor& descriptor() const noexcept override { return desc_; }

    Diagnostic bind_slots(const CscPattern& pat) override {
        if (!bind_optional(pat, p1_, br1_, p1_br1_) || !bind_optional(pat, n1_, br1_, n1_br1_) ||
            !bind_optional(pat, p2_, br2_, p2_br2_) || !bind_optional(pat, n2_, br2_, n2_br2_) ||
            !bind_optional(pat, br1_, p1_, b1_p1_) || !bind_optional(pat, br1_, n1_, b1_n1_) ||
            !bind_optional(pat, br1_, p2_, b1_p2_) || !bind_optional(pat, br1_, n2_, b1_n2_) ||
            !bind_optional(pat, br1_, br1_, b1_b1_) || !bind_optional(pat, br1_, br2_, b1_b2_) ||
            !bind_optional(pat, br2_, p1_, b2_p1_) || !bind_optional(pat, br2_, n1_, b2_n1_) ||
            !bind_optional(pat, br2_, p2_, b2_p2_) || !bind_optional(pat, br2_, n2_, b2_n2_) ||
            !bind_optional(pat, br2_, br1_, b2_b1_) || !bind_optional(pat, br2_, br2_, b2_b2_)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "missing transmission line CSC slot");
        }
        return Diagnostic::success();
    }

    void reset_history() noexcept override {
        history_.clear();
        has_trial_ = false;
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double v1 = node_voltage(ctx.x, p1_) - node_voltage(ctx.x, n1_);
        const double v2 = node_voltage(ctx.x, p2_) - node_voltage(ctx.x, n2_);
        const double i1 = ctx.x[br1_.value];
        const double i2 = ctx.x[br2_.value];

        /* KCL port current stamps apply in all modes. */
        if (!stamp_f_opt(t, p1_, i1) || !stamp_f_opt(t, n1_, -i1) ||
            !stamp_f_opt(t, p2_, i2) || !stamp_f_opt(t, n2_, -i2) ||
            !stamp_df_opt(t, p1_br1_, 1.0) || !stamp_df_opt(t, n1_br1_, -1.0) ||
            !stamp_df_opt(t, p2_br2_, 1.0) || !stamp_df_opt(t, n2_br2_, -1.0)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "tline KCL stamp failed");
        }

        if (ctx.mode != AnalysisMode::Transient) {
            /* DC / AcLinearize baseline: V1 - V2 = 0, I1 + I2 = 0. */
            if (!t.add_f(br1_, v1 - v2) || !t.add_f(br2_, i1 + i2) ||
                !stamp_df_opt(t, b1_p1_, 1.0) || !stamp_df_opt(t, b1_n1_, -1.0) ||
                !stamp_df_opt(t, b1_p2_, -1.0) || !stamp_df_opt(t, b1_n2_, 1.0) ||
                !stamp_df_opt(t, b2_b1_, 1.0) || !stamp_df_opt(t, b2_b2_, 1.0)) {
                return Diagnostic::error(StatusCode::NumericalFailure, "tline DC stamp failed");
            }
            return Diagnostic::success();
        }

        /* Transient Bergeron method-of-characteristics:
         *   I1(t) - Y0*V1(t) + I_h1(t) = 0,  I_h1(t) = Y0*V2(t - Td) + I2(t - Td)
         *   I2(t) - Y0*V2(t) + I_h2(t) = 0,  I_h2(t) = Y0*V1(t - Td) + I1(t - Td)
         */
        double ih1 = 0.0;
        double ih2 = 0.0;
        if (history_.empty()) {
            /* t = 0 startup evaluation: seed steady-state characteristic waves from current x. */
            ih1 = y0_ * v2 + i2;
            ih2 = y0_ * v1 + i1;
        } else {
            const HistSample delayed = sample_history(ctx.time_s - td_);
            ih1 = y0_ * delayed.v2 + delayed.i2;
            ih2 = y0_ * delayed.v1 + delayed.i1;
        }

        trial_sample_ = HistSample{ctx.time_s, v1, v2, i1, i2};
        has_trial_ = true;

        if (!t.add_f(br1_, i1 - y0_ * v1 + ih1) ||
            !t.add_f(br2_, i2 - y0_ * v2 + ih2) ||
            !stamp_df_opt(t, b1_b1_, 1.0) ||
            !stamp_df_opt(t, b1_p1_, -y0_) || !stamp_df_opt(t, b1_n1_, y0_) ||
            !stamp_df_opt(t, b2_b2_, 1.0) ||
            !stamp_df_opt(t, b2_p2_, -y0_) || !stamp_df_opt(t, b2_n2_, y0_)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "tline transient Bergeron stamp failed");
        }
        return Diagnostic::success();
    }

    void overwrite_ac_matrix(
        double omega,
        std::span<std::complex<double>> a_vals,
        std::span<const std::size_t> remap) const noexcept {
        using C = std::complex<double>;
        const double theta = omega * td_;
        const double c = std::cos(theta);
        const double s = std::sin(theta);

        auto set_slot = [&](std::optional<SlotId> sl, C val) {
            if (!sl) return;
            const std::size_t idx = remap.empty() ? sl->value : remap[sl->value];
            a_vals[idx] = val;
        };

        /* Row br1: V1 - cos(theta)*V2 + j*Z0*sin(theta)*I2 = 0 */
        set_slot(b1_p1_, C{1.0, 0.0});
        set_slot(b1_n1_, C{-1.0, 0.0});
        set_slot(b1_p2_, C{-c, 0.0});
        set_slot(b1_n2_, C{c, 0.0});
        set_slot(b1_b1_, C{0.0, 0.0});
        set_slot(b1_b2_, C{0.0, z0_ * s});

        /* Row br2: I1 - j*Y0*sin(theta)*V2 + cos(theta)*I2 = 0 */
        set_slot(b2_p1_, C{0.0, 0.0});
        set_slot(b2_n1_, C{0.0, 0.0});
        set_slot(b2_p2_, C{0.0, -y0_ * s});
        set_slot(b2_n2_, C{0.0, y0_ * s});
        set_slot(b2_b1_, C{1.0, 0.0});
        set_slot(b2_b2_, C{c, 0.0});
    }

    void commit_trial() noexcept override {
        if (!has_trial_) return;
        if (!history_.empty() && std::abs(history_.back().t - trial_sample_.t) < 1.0e-18) {
            history_.back() = trial_sample_;
        } else {
            history_.push_back(trial_sample_);
        }
        has_trial_ = false;
    }

    void rollback_trial() noexcept override { has_trial_ = false; }

private:
    struct HistSample {
        double t{0.0};
        double v1{0.0};
        double v2{0.0};
        double i1{0.0};
        double i2{0.0};
    };

    HistSample sample_history(double t_query) const noexcept {
        if (t_query <= history_.front().t) return history_.front();
        if (t_query >= history_.back().t) return history_.back();
        auto it = std::lower_bound(
            history_.begin(), history_.end(), t_query,
            [](const HistSample& a, double t) { return a.t < t; });
        if (it == history_.begin()) return *it;
        const auto prev = it - 1;
        const double dt = it->t - prev->t;
        if (dt <= 0.0) return *it;
        const double f = (t_query - prev->t) / dt;
        return HistSample{
            t_query,
            prev->v1 + f * (it->v1 - prev->v1),
            prev->v2 + f * (it->v2 - prev->v2),
            prev->i1 + f * (it->i1 - prev->i1),
            prev->i2 + f * (it->i2 - prev->i2)};
    }

    DeviceDescriptor desc_;
    std::optional<UnknownId> p1_, n1_, p2_, n2_;
    UnknownId br1_, br2_;
    double z0_, y0_, td_;
    std::optional<SlotId> p1_br1_, n1_br1_, p2_br2_, n2_br2_;
    std::optional<SlotId> b1_p1_, b1_n1_, b1_p2_, b1_n2_, b1_b1_, b1_b2_;
    std::optional<SlotId> b2_p1_, b2_n1_, b2_p2_, b2_n2_, b2_b1_, b2_b2_;
    std::vector<HistSample> history_;
    HistSample trial_sample_{};
    bool has_trial_{false};
};

CscPattern build_csc_pattern(std::uint32_t dim, CoordList& coords) {
    /* Sort by (col, row) and deduplicate to form canonical CSC structure. */
    std::sort(coords.begin(), coords.end(), [](const auto& a, const auto& b) {
        return (a.second != b.second) ? (a.second < b.second) : (a.first < b.first);
    });
    coords.erase(std::unique(coords.begin(), coords.end()), coords.end());

    CscPattern pat{};
    pat.dimension = dim;
    pat.col_ptr.assign(static_cast<std::size_t>(dim) + 1u, 0u);
    pat.row_idx.reserve(coords.size());
    for (const auto& [r, c] : coords) {
        pat.row_idx.push_back(r);
        ++pat.col_ptr[static_cast<std::size_t>(c) + 1u];
    }
    for (std::uint32_t c = 0; c < dim; ++c) {
        pat.col_ptr[static_cast<std::size_t>(c) + 1u] += pat.col_ptr[c];
    }
    return pat;
}

}  // namespace

Diagnostic CompiledPlan::evaluate_all(const EvalContext& ctx, StampTarget& target) const {
    std::fill(target.f.begin(), target.f.end(), 0.0);
    std::fill(target.q.begin(), target.q.end(), 0.0);
    std::fill(target.df_dx.begin(), target.df_dx.end(), 0.0);
    std::fill(target.dq_dx.begin(), target.dq_dx.end(), 0.0);
    if (!target.f_lim.empty()) std::fill(target.f_lim.begin(), target.f_lim.end(), 0.0);
    if (!target.q_lim.empty()) std::fill(target.q_lim.begin(), target.q_lim.end(), 0.0);
    if (target.step_limited != nullptr) *target.step_limited = false;
    for (const auto& dev : devices) {
        if (auto d = dev->evaluate(ctx, target); !d.ok()) return d;
    }
    return Diagnostic::success();
}

void CompiledPlan::fill_ac_rhs(std::span<std::complex<double>> rhs) const noexcept {
    std::fill(rhs.begin(), rhs.end(), std::complex<double>{0.0, 0.0});
    for (const auto& elem : circuit.elements) {
        if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem)) {
            if (const auto it = branch_unknowns.find(v->id); it != branch_unknowns.end()) {
                rhs[it->second.value] += v->ac_phasor;
            }
        } else if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem)) {
            if (auto p = node_unknown(i->p)) rhs[p->value] -= i->ac_phasor;
            if (auto n = node_unknown(i->n)) rhs[n->value] += i->ac_phasor;
        }
    }
}

std::vector<double> CompiledPlan::collect_breakpoints(double t_start, double t_stop) const {
    std::vector<double> pts;
    for (const auto& elem : circuit.elements) {
        if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem)) {
            if (v->transient_waveform.has_value()) {
                core::collect_waveform_breakpoints(*v->transient_waveform, t_start, t_stop, pts);
            }
        } else if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem)) {
            if (i->transient_waveform.has_value()) {
                core::collect_waveform_breakpoints(*i->transient_waveform, t_start, t_stop, pts);
            }
        }
    }
    std::sort(pts.begin(), pts.end());
    std::vector<double> unique_pts;
    const double merge_eps = 1.0e-12 * std::max(1.0, std::abs(t_stop - t_start));
    for (double t : pts) {
        if (unique_pts.empty() || t - unique_pts.back() > merge_eps) {
            unique_pts.push_back(t);
        }
    }
    return unique_pts;
}

Diagnostic CompiledPlan::set_dc_source_override(
    const std::string& source_id,
    std::optional<double> value) {
    const auto it = source_device_indices.find(source_id);
    if (it == source_device_indices.end()) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "unknown independent source for DC sweep", source_id);
    }
    auto* src = dynamic_cast<OverridableSourceDevice*>(devices[it->second].get());
    if (src == nullptr) {
        return Diagnostic::error(StatusCode::InvalidArgument, "element is not an independent source", source_id);
    }
    src->set_dc_override(value);
    return Diagnostic::success();
}

Diagnostic CompiledPlan::set_transient_source_override(
    const std::string& source_id,
    std::optional<double> value) {
    const auto it = source_device_indices.find(source_id);
    if (it == source_device_indices.end()) {
        return Diagnostic::error(
            StatusCode::InvalidArgument, "unknown independent source for transient override", source_id);
    }
    auto* src = dynamic_cast<OverridableSourceDevice*>(devices[it->second].get());
    if (src == nullptr) {
        return Diagnostic::error(StatusCode::InvalidArgument, "element is not an independent source", source_id);
    }
    src->set_transient_override(value);
    return Diagnostic::success();
}

void CompiledPlan::set_source_scale(double scale) noexcept {
    for (const auto& [_, idx] : source_device_indices) {
        if (auto* src = dynamic_cast<OverridableSourceDevice*>(devices[idx].get())) {
            src->set_source_scale(scale);
        }
    }
}

void CompiledPlan::commit_all_trials() noexcept {
    for (auto& dev : devices) dev->commit_trial();
}

void CompiledPlan::rollback_all_trials() noexcept {
    for (auto& dev : devices) dev->rollback_trial();
}

void CompiledPlan::reset_transient_history() noexcept {
    for (auto& dev : devices) {
        dev->reset_history();
    }
}

void CompiledPlan::overwrite_frequency_dependent_ac(
    double omega,
    std::span<std::complex<double>> a_vals,
    std::span<const std::size_t> slot_remap) const noexcept {
    for (std::size_t idx : tline_device_indices) {
        if (const auto* tl = dynamic_cast<const TransmissionLineDevice*>(devices[idx].get())) {
            tl->overwrite_ac_matrix(omega, a_vals, slot_remap);
        }
    }
}

Diagnostic compile_circuit(
    const core::Circuit& circuit,
    CompiledPlan& out_plan,
    std::uint64_t max_unknowns) {
    if (auto d = circuit.validate(max_unknowns); !d.ok()) return d;

    CompiledPlan plan{};
    plan.circuit = circuit;

    /* 1. Assign non-ground circuit node voltage unknowns. */
    std::unordered_map<std::string, std::int32_t> node_name_to_unknown;
    node_name_to_unknown.emplace("0", -1);
    for (std::size_t i = 1; i < circuit.nodes.size(); ++i) {
        node_name_to_unknown.emplace(circuit.nodes[i], static_cast<std::int32_t>(i - 1u));
        plan.signals.push_back(core::SignalDescriptor{"v(" + circuit.nodes[i] + ")", "voltage", "V"});
    }

    /* 1b. Preload OSDI libraries and allocate any internal device node unknowns right after
     *     external circuit nodes so all node voltages occupy [0 .. node_unknown_count - 1]. */
    std::unordered_map<std::string, std::shared_ptr<osdi::OsdiLibrary>> loaded_libs;
    std::unordered_map<std::string, osdi::PreparedOsdiInstance> osdi_prepared;
    std::unordered_map<std::string, std::vector<UnknownId>> osdi_internal_nodes;
    std::unordered_map<std::string, UnknownId> diode_int_nodes;
    struct ThreeIntNodes {
        std::optional<UnknownId> n0;
        std::optional<UnknownId> n1;
        std::optional<UnknownId> n2;
    };
    std::unordered_map<std::string, ThreeIntNodes> bjt_int_nodes;
    std::unordered_map<std::string, ThreeIntNodes> vdmos_int_nodes;
    std::unordered_map<std::string, ThreeIntNodes> mesfet_int_nodes;
    std::unordered_map<std::string, double> inductor_henries;

    auto alloc_internal_node = [&](const std::string& elem_id, const std::string& suffix) -> UnknownId {
        const auto u = UnknownId{static_cast<std::uint32_t>(plan.signals.size())};
        plan.signals.push_back(core::SignalDescriptor{"v(" + elem_id + ":" + suffix + ")", "voltage", "V"});
        return u;
    };

    for (const auto& elem : circuit.elements) {
        if (const auto* l = std::get_if<core::InductorSpec>(&elem)) {
            inductor_henries.emplace(l->id, l->henries);
        } else if (const auto* o = std::get_if<core::OsdiInstanceSpec>(&elem)) {
            plan.has_nonlinear = true;
            plan.has_osdi = true;
            auto it = loaded_libs.find(o->library_path);
            if (it == loaded_libs.end()) {
                std::shared_ptr<osdi::OsdiLibrary> lib;
                if (auto d = osdi::OsdiLibrary::load(o->library_path, lib); !d.ok()) return d;
                it = loaded_libs.emplace(o->library_path, std::move(lib)).first;
            }
            osdi::PreparedOsdiInstance prep{};
            if (auto d = osdi::prepare_osdi_instance(
                    o->id,
                    it->second,
                    o->model_name,
                    static_cast<std::uint32_t>(o->terminals.size()),
                    o->model_params,
                    o->instance_params,
                    300.15,
                    prep);
                !d.ok()) {
                return d;
            }
            std::vector<UnknownId> internals;
            internals.reserve(prep.uncollapsed_internal_nodes.size());
            for (std::size_t k = 0; k < prep.uncollapsed_internal_nodes.size(); ++k) {
                internals.push_back(alloc_internal_node(o->id, prep.uncollapsed_internal_names[k]));
            }
            osdi_internal_nodes.emplace(o->id, std::move(internals));
            osdi_prepared.emplace(o->id, std::move(prep));
        } else if (const auto* d_spec = std::get_if<core::DiodeSpec>(&elem)) {
            plan.has_nonlinear = true;
            if (d_spec->rs_ohms > 0.0) {
                diode_int_nodes.emplace(d_spec->id, alloc_internal_node(d_spec->id, "int"));
            }
        } else if (const auto* q = std::get_if<core::BjtSpec>(&elem)) {
            plan.has_nonlinear = true;
            ThreeIntNodes tin{};
            if (q->rc_ohms > 0.0) tin.n0 = alloc_internal_node(q->id, "ci");
            if (q->rb_ohms > 0.0) tin.n1 = alloc_internal_node(q->id, "bi");
            if (q->re_ohms > 0.0) tin.n2 = alloc_internal_node(q->id, "ei");
            bjt_int_nodes.emplace(q->id, tin);
        } else if (const auto* vm = std::get_if<core::VdmosSpec>(&elem)) {
            plan.has_nonlinear = true;
            ThreeIntNodes tin{};
            if (vm->rd_ohms > 0.0) tin.n0 = alloc_internal_node(vm->id, "di");
            if (vm->rg_ohms > 0.0) tin.n1 = alloc_internal_node(vm->id, "gi");
            if (vm->rs_ohms > 0.0) tin.n2 = alloc_internal_node(vm->id, "si");
            vdmos_int_nodes.emplace(vm->id, tin);
        } else if (const auto* z = std::get_if<core::MesfetSpec>(&elem)) {
            plan.has_nonlinear = true;
            ThreeIntNodes tin{};
            if (z->rd_ohms > 0.0) tin.n0 = alloc_internal_node(z->id, "di");
            if (z->rs_ohms > 0.0) tin.n1 = alloc_internal_node(z->id, "si");
            mesfet_int_nodes.emplace(z->id, tin);
        } else if (std::holds_alternative<core::BehavioralSourceSpec>(elem) ||
                   std::holds_alternative<core::SwitchSpec>(elem) ||
                   std::holds_alternative<core::CurrentSwitchSpec>(elem) ||
                   std::holds_alternative<core::MosfetSpec>(elem) ||
                   std::holds_alternative<core::JfetSpec>(elem) ||
                   std::holds_alternative<core::SaturableCoreSpec>(elem)) {
            plan.has_nonlinear = true;
        }
    }
    plan.node_unknown_count = plan.signals.size();

    /* 2. Assign branch current unknowns for V, L, SaturableCore, E, H, and voltage B_v. */
    for (const auto& elem : circuit.elements) {
        bool has_branch = std::holds_alternative<core::VoltageSourceSpec>(elem) ||
                          std::holds_alternative<core::InductorSpec>(elem) ||
                          std::holds_alternative<core::SaturableCoreSpec>(elem) ||
                          std::holds_alternative<core::VcvsSpec>(elem) ||
                          std::holds_alternative<core::CcvsSpec>(elem);
        if (const auto* b = std::get_if<core::BehavioralSourceSpec>(&elem); b != nullptr && b->is_voltage) {
            has_branch = true;
        }
        if (has_branch) {
            const auto& id = core::element_id(elem);
            const auto br = UnknownId{static_cast<std::uint32_t>(plan.signals.size())};
            plan.branch_unknowns.emplace(id, br);
            plan.signals.push_back(core::SignalDescriptor{"i(" + id + ")", "current", "A"});
        } else if (const auto* tl = std::get_if<core::TransmissionLineSpec>(&elem)) {
            const auto br1 = UnknownId{static_cast<std::uint32_t>(plan.signals.size())};
            plan.branch_unknowns.emplace(tl->id + ":1", br1);
            plan.signals.push_back(core::SignalDescriptor{"i(" + tl->id + ":1)", "current", "A"});
            const auto br2 = UnknownId{static_cast<std::uint32_t>(plan.signals.size())};
            plan.branch_unknowns.emplace(tl->id + ":2", br2);
            plan.signals.push_back(core::SignalDescriptor{"i(" + tl->id + ":2)", "current", "A"});
        }
    }

    const auto dim = static_cast<std::uint32_t>(plan.signals.size());
    if (dim > max_unknowns) {
        return Diagnostic::error(StatusCode::ResourceExhausted, "compiled unknowns exceed caller budget");
    }

    CoordList coords;
    coords.reserve(circuit.elements.size() * 6u + (plan.has_nonlinear ? plan.node_unknown_count : 0u));

    /* Reserve node diagonals when nonlinear so Gmin continuation stamps in O(N) without rebuilding CSC. */
    if (plan.has_nonlinear) {
        for (std::size_t u = 0; u < plan.node_unknown_count; ++u) {
            coords.emplace_back(static_cast<std::uint32_t>(u), static_cast<std::uint32_t>(u));
        }
    }

    auto resolve_node = [&](const std::string& name, std::int32_t& out_u) -> bool {
        const auto it = node_name_to_unknown.find(name);
        if (it == node_name_to_unknown.end()) return false;
        out_u = it->second;
        return true;
    };
    auto resolve_branch = [&](const std::string& name, std::uint32_t& out_u) -> bool {
        const auto it = plan.branch_unknowns.find(name);
        if (it == plan.branch_unknowns.end()) return false;
        out_u = it->second.value;
        return true;
    };

    /* 3. Instantiate DAE devices and collect sparse stencils. */
    for (const auto& elem : circuit.elements) {
        if (const auto* r = std::get_if<core::ResistorSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(r->p);
            auto n = CompiledPlan::node_unknown(r->n);
            ResistorDevice::record_stencil(coords, p, n);
            plan.devices.push_back(std::make_unique<ResistorDevice>(p, n, r->ohms));
        } else if (const auto* c = std::get_if<core::CapacitorSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(c->p);
            auto n = CompiledPlan::node_unknown(c->n);
            CapacitorDevice::record_stencil(coords, p, n);
            plan.devices.push_back(std::make_unique<CapacitorDevice>(p, n, c->farads));
        } else if (const auto* l = std::get_if<core::InductorSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(l->p);
            auto n = CompiledPlan::node_unknown(l->n);
            auto br = plan.branch_unknowns.at(l->id);
            InductorDevice::record_stencil(coords, p, n, br);
            plan.devices.push_back(std::make_unique<InductorDevice>(p, n, br, l->henries));
        } else if (const auto* sc = std::get_if<core::SaturableCoreSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(sc->p);
            auto n = CompiledPlan::node_unknown(sc->n);
            auto br = plan.branch_unknowns.at(sc->id);
            SaturableCoreDevice::record_stencil(coords, p, n, br);
            plan.devices.push_back(std::make_unique<SaturableCoreDevice>(p, n, br, *sc));
        } else if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(v->p);
            auto n = CompiledPlan::node_unknown(v->n);
            auto br = plan.branch_unknowns.at(v->id);
            VoltageSourceDevice::record_stencil(coords, p, n, br);
            plan.source_device_indices.emplace(v->id, plan.devices.size());
            plan.devices.push_back(std::make_unique<VoltageSourceDevice>(p, n, br, *v));
        } else if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(i->p);
            auto n = CompiledPlan::node_unknown(i->n);
            plan.source_device_indices.emplace(i->id, plan.devices.size());
            plan.devices.push_back(std::make_unique<CurrentSourceDevice>(p, n, *i));
        } else if (const auto* g = std::get_if<core::VccsSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(g->p);
            auto n = CompiledPlan::node_unknown(g->n);
            auto cp = CompiledPlan::node_unknown(g->cp);
            auto cn = CompiledPlan::node_unknown(g->cn);
            VccsDevice::record_stencil(coords, p, n, cp, cn);
            plan.devices.push_back(std::make_unique<VccsDevice>(p, n, cp, cn, g->siemens));
        } else if (const auto* e = std::get_if<core::VcvsSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(e->p);
            auto n = CompiledPlan::node_unknown(e->n);
            auto cp = CompiledPlan::node_unknown(e->cp);
            auto cn = CompiledPlan::node_unknown(e->cn);
            auto br = plan.branch_unknowns.at(e->id);
            VcvsDevice::record_stencil(coords, p, n, cp, cn, br);
            plan.devices.push_back(std::make_unique<VcvsDevice>(p, n, cp, cn, br, e->gain));
        } else if (const auto* f = std::get_if<core::CccsSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(f->p);
            auto n = CompiledPlan::node_unknown(f->n);
            auto ctrl = plan.branch_unknowns.at(f->control_source_id);
            CccsDevice::record_stencil(coords, p, n, ctrl);
            plan.devices.push_back(std::make_unique<CccsDevice>(p, n, ctrl, f->gain));
        } else if (const auto* h = std::get_if<core::CcvsSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(h->p);
            auto n = CompiledPlan::node_unknown(h->n);
            auto br = plan.branch_unknowns.at(h->id);
            auto ctrl = plan.branch_unknowns.at(h->control_source_id);
            CcvsDevice::record_stencil(coords, p, n, br, ctrl);
            plan.devices.push_back(std::make_unique<CcvsDevice>(p, n, br, ctrl, h->ohms));
        } else if (const auto* k = std::get_if<core::MutualInductanceSpec>(&elem)) {
            const double m_val =
                k->coupling_k * std::sqrt(inductor_henries.at(k->inductor1_id) * inductor_henries.at(k->inductor2_id));
            const auto br1 = plan.branch_unknowns.at(k->inductor1_id);
            const auto br2 = plan.branch_unknowns.at(k->inductor2_id);
            MutualInductanceDevice::record_stencil(coords, br1, br2);
            plan.devices.push_back(std::make_unique<MutualInductanceDevice>(br1, br2, m_val));
        } else if (const auto* d_spec = std::get_if<core::DiodeSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(d_spec->p);
            auto n = CompiledPlan::node_unknown(d_spec->n);
            std::optional<UnknownId> p_int;
            if (auto dit = diode_int_nodes.find(d_spec->id); dit != diode_int_nodes.end()) {
                p_int = dit->second;
            }
            DiodeDevice::record_stencil(coords, p, n, p_int);
            plan.devices.push_back(std::make_unique<DiodeDevice>(p, n, *d_spec, p_int));
        } else if (const auto* b = std::get_if<core::BehavioralSourceSpec>(&elem)) {
            CompiledExpr expr{};
            if (auto d = CompiledExpr::compile(b->expression, resolve_node, resolve_branch, expr);
                !d.ok()) {
                return d;
            }
            auto p = CompiledPlan::node_unknown(b->p);
            auto n = CompiledPlan::node_unknown(b->n);
            std::optional<UnknownId> br;
            if (b->is_voltage) br = plan.branch_unknowns.at(b->id);
            BehavioralSourceDevice::record_stencil(coords, p, n, br, b->is_voltage, expr);
            plan.devices.push_back(
                std::make_unique<BehavioralSourceDevice>(p, n, br, b->is_voltage, std::move(expr)));
        } else if (const auto* sw = std::get_if<core::SwitchSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(sw->p);
            auto n = CompiledPlan::node_unknown(sw->n);
            auto cp = CompiledPlan::node_unknown(sw->cp);
            auto cn = CompiledPlan::node_unknown(sw->cn);
            SwitchDevice::record_stencil(coords, p, n, cp, cn);
            plan.devices.push_back(std::make_unique<SwitchDevice>(p, n, cp, cn, *sw));
        } else if (const auto* csw = std::get_if<core::CurrentSwitchSpec>(&elem)) {
            auto p = CompiledPlan::node_unknown(csw->p);
            auto n = CompiledPlan::node_unknown(csw->n);
            auto ctrl = plan.branch_unknowns.at(csw->control_source_id);
            CurrentSwitchDevice::record_stencil(coords, p, n, ctrl);
            plan.devices.push_back(std::make_unique<CurrentSwitchDevice>(p, n, ctrl, *csw));
        } else if (const auto* tl = std::get_if<core::TransmissionLineSpec>(&elem)) {
            auto p1 = CompiledPlan::node_unknown(tl->p1);
            auto n1 = CompiledPlan::node_unknown(tl->n1);
            auto p2 = CompiledPlan::node_unknown(tl->p2);
            auto n2 = CompiledPlan::node_unknown(tl->n2);
            auto br1 = plan.branch_unknowns.at(tl->id + ":1");
            auto br2 = plan.branch_unknowns.at(tl->id + ":2");
            TransmissionLineDevice::record_stencil(coords, p1, n1, p2, n2, br1, br2);
            plan.tline_device_indices.push_back(plan.devices.size());
            plan.devices.push_back(
                std::make_unique<TransmissionLineDevice>(p1, n1, p2, n2, br1, br2, *tl));
        } else if (const auto* m = std::get_if<core::MosfetSpec>(&elem)) {
            auto md = CompiledPlan::node_unknown(m->d);
            auto mg = CompiledPlan::node_unknown(m->g);
            auto ms = CompiledPlan::node_unknown(m->s);
            auto mb = CompiledPlan::node_unknown(m->b);
            record_mosfet_stencil(coords, md, mg, ms, mb);
            plan.devices.push_back(create_mosfet_device(md, mg, ms, mb, *m));
        } else if (const auto* vm = std::get_if<core::VdmosSpec>(&elem)) {
            auto md = CompiledPlan::node_unknown(vm->d);
            auto mg = CompiledPlan::node_unknown(vm->g);
            auto ms = CompiledPlan::node_unknown(vm->s);
            const auto& tin = vdmos_int_nodes.at(vm->id);
            record_vdmos_stencil(coords, md, mg, ms, tin.n0, tin.n1, tin.n2);
            plan.devices.push_back(create_vdmos_device(md, mg, ms, *vm, tin.n0, tin.n1, tin.n2));
        } else if (const auto* q = std::get_if<core::BjtSpec>(&elem)) {
            auto qc = CompiledPlan::node_unknown(q->c);
            auto qb = CompiledPlan::node_unknown(q->b);
            auto qe = CompiledPlan::node_unknown(q->e);
            auto qs = CompiledPlan::node_unknown(q->s);
            const auto& tin = bjt_int_nodes.at(q->id);
            record_bjt_stencil(coords, qc, qb, qe, qs, tin.n0, tin.n1, tin.n2);
            plan.devices.push_back(create_bjt_device(qc, qb, qe, *q, qs, tin.n0, tin.n1, tin.n2));
        } else if (const auto* j = std::get_if<core::JfetSpec>(&elem)) {
            auto jd = CompiledPlan::node_unknown(j->d);
            auto jg = CompiledPlan::node_unknown(j->g);
            auto js = CompiledPlan::node_unknown(j->s);
            record_jfet_stencil(coords, jd, jg, js);
            plan.devices.push_back(create_jfet_device(jd, jg, js, *j));
        } else if (const auto* z = std::get_if<core::MesfetSpec>(&elem)) {
            auto zd = CompiledPlan::node_unknown(z->d);
            auto zg = CompiledPlan::node_unknown(z->g);
            auto zs = CompiledPlan::node_unknown(z->s);
            const auto& tin = mesfet_int_nodes.at(z->id);
            record_mesfet_stencil(coords, zd, zg, zs, tin.n0, tin.n1);
            plan.devices.push_back(create_mesfet_device(zd, zg, zs, *z, tin.n0, tin.n1));
        } else if (const auto* o = std::get_if<core::OsdiInstanceSpec>(&elem)) {
            std::vector<std::optional<UnknownId>> term_unks;
            term_unks.reserve(o->terminals.size());
            for (NodeId nid : o->terminals) term_unks.push_back(CompiledPlan::node_unknown(nid));

            const auto& int_unks = osdi_internal_nodes.at(o->id);
            std::unique_ptr<Device> dev;
            if (auto d = osdi::finalize_osdi_instance(
                    std::move(osdi_prepared.at(o->id)), term_unks, int_unks, coords, dev);
                !d.ok()) {
                return d;
            }
            plan.devices.push_back(std::move(dev));
        }
    }

    plan.pattern = build_csc_pattern(dim, coords);
    if (auto d = plan.pattern.validate(max_unknowns); !d.ok()) return d;

    /* Pre-bind node diagonal slots for O(N) Gmin continuation. */
    plan.node_diag_slots.resize(plan.node_unknown_count);
    for (std::size_t u = 0; u < plan.node_unknown_count; ++u) {
        SlotId slot{};
        if (plan.pattern.find_slot(static_cast<std::uint32_t>(u), static_cast<std::uint32_t>(u), slot)) {
            plan.node_diag_slots[u] = slot;
        }
    }

    /* 4. Pre-bind every DAE device to exact CSC slots once. */
    for (auto& dev : plan.devices) {
        if (auto d = dev->bind_slots(plan.pattern); !d.ok()) return d;
    }

    out_plan = std::move(plan);
    return Diagnostic::success();
}

}  // namespace spice::devices
