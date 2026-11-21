#include "devices/linear_devices.hpp"
#include "devices/expr_vm.hpp"
#include "osdi/osdi_loader.hpp"

#include <algorithm>
#include <cmath>
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
        const double raw_vsrc =
            dc_override_.has_value() && !is_tran ? *dc_override_ : core::source_value_at(spec_, ctx.time_s, is_tran);
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
    void set_source_scale(double scale) noexcept override { source_scale_ = scale; }
    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    UnknownId br_;
    core::VoltageSourceSpec spec_;
    std::optional<double> dc_override_;
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
        const double raw_isrc =
            dc_override_.has_value() && !is_tran ? *dc_override_ : core::source_value_at(spec_, ctx.time_s, is_tran);
        const double isrc = source_scale_ * raw_isrc;
        if (!stamp_f_opt(t, p_, isrc) || !stamp_f_opt(t, n_, -isrc)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "current source stamp failed");
        }
        return Diagnostic::success();
    }

    void set_dc_override(std::optional<double> value) noexcept override { dc_override_ = value; }
    void set_source_scale(double scale) noexcept override { source_scale_ = scale; }
    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_;
    core::CurrentSourceSpec spec_;
    std::optional<double> dc_override_;
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
 * 11. Nonlinear SPICE Diode: Shockley conduction + SPICE depletion & transit-time charge Q(V).
 * Smooth linear continuation above exp_arg = 40 prevents numeric overflow during wild trial
 * steps while preserving C1 continuity (identical I and dI/dV at the join).
 */
class DiodeDevice final : public Device {
public:
    DiodeDevice(std::optional<UnknownId> p, std::optional<UnknownId> n, core::DiodeSpec spec)
        : p_(p), n_(n), spec_(std::move(spec)), nvt_(spec_.n_emission * spec_.vt_volts) {
        desc_.kind_id = "spice.diode";
        desc_.display_name = "SPICE Diode";
        desc_.pins = {{"p", "anode"}, {"n", "cathode"}};
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
            return Diagnostic::error(StatusCode::InvalidArgument, "missing diode CSC slot");
        }
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& t) override {
        const double vd = node_voltage(ctx.x, p_) - node_voltage(ctx.x, n_);
        if (!std::isfinite(vd)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite diode voltage");
        }
        const double arg = vd / nvt_;
        double id_shockley = 0.0;
        double gd_shockley = 0.0;
        constexpr double kExpMaxArg = 40.0;
        if (arg <= kExpMaxArg) {
            const double ev = std::exp(arg);
            id_shockley = spec_.is_amps * std::expm1(arg);
            gd_shockley = (spec_.is_amps * ev) / nvt_;
        } else {
            const double ev = std::exp(kExpMaxArg);
            const double v_lim = kExpMaxArg * nvt_;
            gd_shockley = (spec_.is_amps * ev) / nvt_;
            id_shockley = spec_.is_amps * std::expm1(kExpMaxArg) + gd_shockley * (vd - v_lim);
        }

        const double i_total = id_shockley + spec_.gmin_siemens * vd;
        const double g_total = gd_shockley + spec_.gmin_siemens;

        /* Transit charge Q_tt = tt * I_shockley + SPICE depletion charge Q_dep (fc = 0.5). */
        double q_total = spec_.tt_s * id_shockley;
        double c_total = spec_.tt_s * gd_shockley;

        if (spec_.cjo_farads > 0.0) {
            constexpr double fc = 0.5;
            const double fcpb = fc * spec_.vj_volts;
            const double one_m_m = 1.0 - spec_.m_grading;
            if (vd < fcpb) {
                const double s = 1.0 - vd / spec_.vj_volts;
                const double s_pow = std::pow(s, -spec_.m_grading);
                c_total += spec_.cjo_farads * s_pow;
                q_total += spec_.cjo_farads * spec_.vj_volts * (1.0 - std::pow(s, one_m_m)) / one_m_m;
            } else {
                const double f1 = spec_.vj_volts * (1.0 - std::pow(1.0 - fc, one_m_m)) / one_m_m;
                const double f2 = std::pow(1.0 - fc, 1.0 + spec_.m_grading);
                const double f3 = 1.0 - fc * (1.0 + spec_.m_grading);
                const double dv = vd - fcpb;
                c_total += (spec_.cjo_farads / f2) * (f3 + (spec_.m_grading * vd) / spec_.vj_volts);
                q_total += spec_.cjo_farads * f1 +
                           (spec_.cjo_farads / f2) *
                               (f3 * dv + (spec_.m_grading / (2.0 * spec_.vj_volts)) *
                                              (vd * vd - fcpb * fcpb));
            }
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
    std::optional<UnknownId> p_, n_;
    core::DiodeSpec spec_;
    double nvt_;
    std::optional<SlotId> pp_, pn_, np_, nn_;
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
          ln_goff_(std::log(1.0 / spec.roff_ohms)) {
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
        const double vc = node_voltage(ctx.x, cp_) - node_voltage(ctx.x, cn_);
        const double u = std::clamp((vc - vt_) / vh_, -30.0, 30.0);
        const double th = std::tanh(u);
        const double sigma = 0.5 * (1.0 + th);
        const double dsigma_dvc = 0.5 * (1.0 - th * th) / vh_;
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

    void commit_trial() noexcept override {}
    void rollback_trial() noexcept override {}

private:
    DeviceDescriptor desc_;
    std::optional<UnknownId> p_, n_, cp_, cn_;
    double vt_, vh_, ln_gon_, ln_goff_;
    std::optional<SlotId> pp_, pn_, np_, nn_, pcp_, pcn_, ncp_, ncn_;
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

    /* 1b. Preload OSDI libraries and allocate any internal OSDI node unknowns right after
     *     external circuit nodes so all node voltages occupy [0 .. node_unknown_count - 1]. */
    std::unordered_map<std::string, std::shared_ptr<osdi::OsdiLibrary>> loaded_libs;
    std::unordered_map<std::string, std::vector<UnknownId>> osdi_internal_nodes;
    std::unordered_map<std::string, double> inductor_henries;

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
            std::uint32_t n_terms = 0, n_int = 0;
            if (auto d = osdi::query_osdi_model_topology(*it->second, o->model_name, n_terms, n_int);
                !d.ok()) {
                return d;
            }
            if (o->terminals.size() != n_terms) {
                return Diagnostic::error(
                    StatusCode::InvalidArgument, "OSDI terminal count mismatch", o->id);
            }
            std::vector<UnknownId> internals;
            internals.reserve(n_int);
            for (std::uint32_t k = 0; k < n_int; ++k) {
                const auto u = UnknownId{static_cast<std::uint32_t>(plan.signals.size())};
                internals.push_back(u);
                plan.signals.push_back(core::SignalDescriptor{
                    "v(" + o->id + ":int" + std::to_string(k) + ")", "voltage", "V"});
            }
            osdi_internal_nodes.emplace(o->id, std::move(internals));
        } else if (std::holds_alternative<core::DiodeSpec>(elem) ||
                   std::holds_alternative<core::BehavioralSourceSpec>(elem) ||
                   std::holds_alternative<core::SwitchSpec>(elem)) {
            plan.has_nonlinear = true;
        }
    }
    plan.node_unknown_count = plan.signals.size();

    /* 2. Assign branch current unknowns for V, L, E, H, and voltage behavioral sources B_v. */
    for (const auto& elem : circuit.elements) {
        bool has_branch = std::holds_alternative<core::VoltageSourceSpec>(elem) ||
                          std::holds_alternative<core::InductorSpec>(elem) ||
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
            DiodeDevice::record_stencil(coords, p, n);
            plan.devices.push_back(std::make_unique<DiodeDevice>(p, n, *d_spec));
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
        } else if (const auto* o = std::get_if<core::OsdiInstanceSpec>(&elem)) {
            const auto& lib = loaded_libs.at(o->library_path);
            std::vector<std::optional<UnknownId>> term_unks;
            term_unks.reserve(o->terminals.size());
            for (NodeId nid : o->terminals) term_unks.push_back(CompiledPlan::node_unknown(nid));

            const auto& int_unks = osdi_internal_nodes.at(o->id);
            std::vector<std::optional<UnknownId>> all_unks = term_unks;
            for (UnknownId u : int_unks) all_unks.push_back(u);

            if (auto d = osdi::collect_osdi_stencil(*lib, o->model_name, all_unks, coords); !d.ok()) {
                return d;
            }
            osdi::OsdiDeviceConfig cfg{};
            cfg.instance_id = o->id;
            cfg.library = lib;
            cfg.model_name = o->model_name;
            cfg.terminal_unknowns = std::move(term_unks);
            cfg.internal_unknowns = int_unks;
            cfg.model_params = o->model_params;
            cfg.instance_params = o->instance_params;

            std::unique_ptr<Device> dev;
            if (auto d = osdi::create_osdi_device(cfg, dev); !d.ok()) return d;
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
