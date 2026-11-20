#include "audit/physical_audit.hpp"

#include <algorithm>
#include <cmath>
#include <numbers>

namespace spice::audit {

namespace {

using Complex = std::complex<double>;

double node_v(std::span<const double> x, core::NodeId n) noexcept {
    return n.is_ground() ? 0.0 : x[n.value - 1u];
}

Complex node_vc(std::span<const Complex> x, core::NodeId n) noexcept {
    return n.is_ground() ? Complex{0.0, 0.0} : x[n.value - 1u];
}

AuditCheck make_check(
    std::string id,
    double discrepancy,
    double tolerance,
    std::string unit,
    std::string reason = {}) {
    if (!std::isfinite(discrepancy) || !std::isfinite(tolerance) || tolerance < 0.0) {
        return AuditCheck{
            std::move(id),
            CheckStatus::Failed,
            0.0,
            0.0,
            std::move(unit),
            "nonfinite discrepancy or invalid tolerance"};
    }
    const CheckStatus st = (discrepancy <= tolerance) ? CheckStatus::Passed : CheckStatus::Failed;
    return AuditCheck{std::move(id), st, discrepancy, tolerance, std::move(unit), std::move(reason)};
}

AuditCheck global_accuracy_not_checked() {
    return AuditCheck{
        "global_accuracy",
        CheckStatus::NotChecked,
        0.0,
        0.0,
        "",
        "physical consistency does not bound continuous-time global error (AUDIT-005)"};
}

}  // namespace

bool AuditReport::any_failed() const noexcept {
    return std::any_of(checks.begin(), checks.end(), [](const AuditCheck& c) {
        return c.status == CheckStatus::Failed;
    });
}

bool AuditReport::all_checked_passed() const noexcept {
    bool saw_checked = false;
    for (const auto& c : checks) {
        if (c.status == CheckStatus::Failed) return false;
        if (c.status == CheckStatus::Passed) saw_checked = true;
    }
    return saw_checked;
}

void AuditReport::merge_worst(const AuditReport& other) {
    for (const auto& incoming : other.checks) {
        auto it = std::find_if(checks.begin(), checks.end(), [&](const AuditCheck& c) {
            return c.id == incoming.id;
        });
        if (it == checks.end()) {
            checks.push_back(incoming);
            continue;
        }
        if (incoming.status == CheckStatus::NotChecked) continue;
        if (it->status == CheckStatus::NotChecked) {
            *it = incoming;
            continue;
        }
        const double cur_ratio =
            (it->tolerance > 0.0) ? (it->discrepancy / it->tolerance) : it->discrepancy;
        const double new_ratio =
            (incoming.tolerance > 0.0) ? (incoming.discrepancy / incoming.tolerance) : incoming.discrepancy;
        if (incoming.status == CheckStatus::Failed && it->status != CheckStatus::Failed) {
            *it = incoming;
        } else if (incoming.status == it->status && new_ratio > cur_ratio) {
            *it = incoming;
        }
    }
}

AuditReport audit_dc(
    const devices::CompiledPlan& plan,
    std::span<const double> x,
    const AuditOptions& opts) {
    AuditReport report;
    const auto& ckt = plan.circuit;
    const std::size_t n_nodes = ckt.nodes.size();
    if (x.size() != plan.unknown_count()) {
        report.checks.push_back(
            AuditCheck{"dc_state_shape", CheckStatus::Failed, 0.0, 0.0, "", "solution length mismatch"});
        return report;
    }

    std::vector<double> kcl_sum(n_nodes, 0.0);
    std::vector<double> kcl_scale(n_nodes, 0.0);
    double max_v_err = 0.0;
    double max_v_scale = 0.0;
    double total_power = 0.0;
    double power_scale = 0.0;

    auto accum_branch = [&](core::NodeId p, core::NodeId n, double i_pn) {
        kcl_sum[p.value] += i_pn;
        kcl_sum[n.value] -= i_pn;
        kcl_scale[p.value] += std::abs(i_pn);
        kcl_scale[n.value] += std::abs(i_pn);
        const double vp = node_v(x, p);
        const double vn = node_v(x, n);
        const double pwr = (vp - vn) * i_pn;
        total_power += pwr;
        power_scale += std::abs(pwr);
    };

    for (const auto& elem : ckt.elements) {
        if (const auto* r = std::get_if<core::ResistorSpec>(&elem)) {
            const double i = (node_v(x, r->p) - node_v(x, r->n)) / r->ohms;
            accum_branch(r->p, r->n, i);
        } else if (std::holds_alternative<core::CapacitorSpec>(elem)) {
            /* Ideal DC capacitor carries zero steady-state current. */
        } else if (const auto* l = std::get_if<core::InductorSpec>(&elem)) {
            const double i_l = x[plan.branch_unknowns.at(l->id).value];
            accum_branch(l->p, l->n, i_l);
            const double vp = node_v(x, l->p);
            const double vn = node_v(x, l->n);
            max_v_err = std::max(max_v_err, std::abs(vp - vn));
            max_v_scale = std::max(max_v_scale, std::abs(vp) + std::abs(vn));
        } else if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem)) {
            const double i_v = x[plan.branch_unknowns.at(v->id).value];
            accum_branch(v->p, v->n, i_v);
            const double vp = node_v(x, v->p);
            const double vn = node_v(x, v->n);
            max_v_err = std::max(max_v_err, std::abs((vp - vn) - v->dc_volts));
            max_v_scale = std::max(max_v_scale, std::abs(vp) + std::abs(vn) + std::abs(v->dc_volts));
        } else if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem)) {
            accum_branch(i->p, i->n, i->dc_amps);
        } else if (const auto* g = std::get_if<core::VccsSpec>(&elem)) {
            const double i = g->siemens * (node_v(x, g->cp) - node_v(x, g->cn));
            accum_branch(g->p, g->n, i);
        } else if (const auto* e = std::get_if<core::VcvsSpec>(&elem)) {
            const double i_e = x[plan.branch_unknowns.at(e->id).value];
            accum_branch(e->p, e->n, i_e);
            const double target_v = e->gain * (node_v(x, e->cp) - node_v(x, e->cn));
            const double actual_v = node_v(x, e->p) - node_v(x, e->n);
            max_v_err = std::max(max_v_err, std::abs(actual_v - target_v));
            max_v_scale = std::max(max_v_scale, std::abs(actual_v) + std::abs(target_v));
        } else if (const auto* f = std::get_if<core::CccsSpec>(&elem)) {
            const double i = f->gain * x[plan.branch_unknowns.at(f->control_source_id).value];
            accum_branch(f->p, f->n, i);
        } else if (const auto* h = std::get_if<core::CcvsSpec>(&elem)) {
            const double i_h = x[plan.branch_unknowns.at(h->id).value];
            accum_branch(h->p, h->n, i_h);
            const double target_v = h->ohms * x[plan.branch_unknowns.at(h->control_source_id).value];
            const double actual_v = node_v(x, h->p) - node_v(x, h->n);
            max_v_err = std::max(max_v_err, std::abs(actual_v - target_v));
            max_v_scale = std::max(max_v_scale, std::abs(actual_v) + std::abs(target_v));
        } else if (const auto* d_spec = std::get_if<core::DiodeSpec>(&elem)) {
            const double vd = node_v(x, d_spec->p) - node_v(x, d_spec->n);
            const double nvt = d_spec->n_emission * d_spec->vt_volts;
            const double id = d_spec->is_amps * std::expm1(vd / nvt) + d_spec->gmin_siemens * vd;
            accum_branch(d_spec->p, d_spec->n, id);
        }
    }

    double worst_kcl_err = 0.0;
    double worst_kcl_tol = opts.current_abs_tol;
    double worst_kcl_ratio = -1.0;
    for (std::size_t n = 0; n < n_nodes; ++n) {
        const double err = std::abs(kcl_sum[n]);
        const double tol = opts.current_abs_tol + opts.rel_tol * kcl_scale[n];
        const double ratio = err / tol;
        if (ratio > worst_kcl_ratio) {
            worst_kcl_ratio = ratio;
            worst_kcl_err = err;
            worst_kcl_tol = tol;
        }
    }

    report.checks.push_back(make_check("dc_kcl", worst_kcl_err, worst_kcl_tol, "A"));
    report.checks.push_back(make_check(
        "dc_voltage_constraints", max_v_err, opts.voltage_abs_tol + opts.rel_tol * max_v_scale, "V"));
    report.checks.push_back(make_check(
        "dc_power_balance", std::abs(total_power), opts.power_abs_tol + opts.rel_tol * power_scale, "W"));
    report.checks.push_back(global_accuracy_not_checked());
    return report;
}

AuditReport audit_ac(
    const devices::CompiledPlan& plan,
    std::span<const Complex> x,
    double frequency_hz,
    const AuditOptions& opts) {
    AuditReport report;
    const auto& ckt = plan.circuit;
    const std::size_t n_nodes = ckt.nodes.size();
    if (x.size() != plan.unknown_count() || !std::isfinite(frequency_hz) || frequency_hz < 0.0) {
        report.checks.push_back(
            AuditCheck{"ac_state_shape", CheckStatus::Failed, 0.0, 0.0, "", "invalid AC state or frequency"});
        return report;
    }

    const Complex jw{0.0, 2.0 * std::numbers::pi * frequency_hz};
    std::vector<Complex> kcl_sum(n_nodes, Complex{0.0, 0.0});
    std::vector<double> kcl_scale(n_nodes, 0.0);
    double max_v_err = 0.0;
    double max_v_scale = 0.0;
    Complex total_power{0.0, 0.0};
    double power_scale = 0.0;

    auto accum_branch = [&](core::NodeId p, core::NodeId n, Complex i_pn) {
        kcl_sum[p.value] += i_pn;
        kcl_sum[n.value] -= i_pn;
        kcl_scale[p.value] += std::abs(i_pn);
        kcl_scale[n.value] += std::abs(i_pn);
        const Complex v_pn = node_vc(x, p) - node_vc(x, n);
        const Complex pwr = v_pn * std::conj(i_pn);
        total_power += pwr;
        power_scale += std::abs(pwr);
    };

    for (const auto& elem : ckt.elements) {
        if (const auto* r = std::get_if<core::ResistorSpec>(&elem)) {
            accum_branch(r->p, r->n, (node_vc(x, r->p) - node_vc(x, r->n)) / r->ohms);
        } else if (const auto* c = std::get_if<core::CapacitorSpec>(&elem)) {
            accum_branch(c->p, c->n, jw * c->farads * (node_vc(x, c->p) - node_vc(x, c->n)));
        } else if (const auto* l = std::get_if<core::InductorSpec>(&elem)) {
            const Complex i_l = x[plan.branch_unknowns.at(l->id).value];
            accum_branch(l->p, l->n, i_l);
            const Complex v_actual = node_vc(x, l->p) - node_vc(x, l->n);
            const Complex v_target = jw * l->henries * i_l;
            max_v_err = std::max(max_v_err, std::abs(v_actual - v_target));
            max_v_scale = std::max(max_v_scale, std::abs(v_actual) + std::abs(v_target));
        } else if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem)) {
            const Complex i_v = x[plan.branch_unknowns.at(v->id).value];
            accum_branch(v->p, v->n, i_v);
            const Complex v_actual = node_vc(x, v->p) - node_vc(x, v->n);
            max_v_err = std::max(max_v_err, std::abs(v_actual - v->ac_phasor));
            max_v_scale = std::max(max_v_scale, std::abs(v_actual) + std::abs(v->ac_phasor));
        } else if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem)) {
            accum_branch(i->p, i->n, i->ac_phasor);
        } else if (const auto* g = std::get_if<core::VccsSpec>(&elem)) {
            accum_branch(g->p, g->n, g->siemens * (node_vc(x, g->cp) - node_vc(x, g->cn)));
        } else if (const auto* e = std::get_if<core::VcvsSpec>(&elem)) {
            const Complex i_e = x[plan.branch_unknowns.at(e->id).value];
            accum_branch(e->p, e->n, i_e);
            const Complex v_actual = node_vc(x, e->p) - node_vc(x, e->n);
            const Complex v_target = e->gain * (node_vc(x, e->cp) - node_vc(x, e->cn));
            max_v_err = std::max(max_v_err, std::abs(v_actual - v_target));
            max_v_scale = std::max(max_v_scale, std::abs(v_actual) + std::abs(v_target));
        } else if (const auto* f = std::get_if<core::CccsSpec>(&elem)) {
            accum_branch(f->p, f->n, f->gain * x[plan.branch_unknowns.at(f->control_source_id).value]);
        } else if (const auto* h = std::get_if<core::CcvsSpec>(&elem)) {
            const Complex i_h = x[plan.branch_unknowns.at(h->id).value];
            accum_branch(h->p, h->n, i_h);
            const Complex v_actual = node_vc(x, h->p) - node_vc(x, h->n);
            const Complex v_target = h->ohms * x[plan.branch_unknowns.at(h->control_source_id).value];
            max_v_err = std::max(max_v_err, std::abs(v_actual - v_target));
            max_v_scale = std::max(max_v_scale, std::abs(v_actual) + std::abs(v_target));
        }
    }

    double worst_kcl_err = 0.0;
    double worst_kcl_tol = opts.current_abs_tol;
    double worst_kcl_ratio = -1.0;
    for (std::size_t n = 0; n < n_nodes; ++n) {
        const double err = std::abs(kcl_sum[n]);
        const double tol = opts.current_abs_tol + opts.rel_tol * kcl_scale[n];
        const double ratio = err / tol;
        if (ratio > worst_kcl_ratio) {
            worst_kcl_ratio = ratio;
            worst_kcl_err = err;
            worst_kcl_tol = tol;
        }
    }

    report.checks.push_back(make_check("ac_kcl", worst_kcl_err, worst_kcl_tol, "A"));
    report.checks.push_back(make_check(
        "ac_voltage_constraints", max_v_err, opts.voltage_abs_tol + opts.rel_tol * max_v_scale, "V"));
    report.checks.push_back(make_check(
        "ac_complex_power_balance", std::abs(total_power), opts.power_abs_tol + opts.rel_tol * power_scale, "VA"));
    report.checks.push_back(global_accuracy_not_checked());
    return report;
}

AuditReport audit_transient_step(
    const devices::CompiledPlan& plan,
    std::span<const double> prev_x,
    std::span<const double> curr_x,
    double prev_t,
    double curr_t,
    TransientAuditMethod method,
    const AuditOptions& opts) {
    AuditReport report;
    const auto& ckt = plan.circuit;
    const double h = curr_t - prev_t;
    if (prev_x.size() != plan.unknown_count() || curr_x.size() != plan.unknown_count() ||
        !std::isfinite(h) || h <= 0.0) {
        report.checks.push_back(
            AuditCheck{"tran_step_shape", CheckStatus::Failed, 0.0, 0.0, "", "invalid transient step state"});
        return report;
    }

    const std::size_t n_nodes = ckt.nodes.size();
    std::vector<double> kcl_sum(n_nodes, 0.0);
    std::vector<double> kcl_scale(n_nodes, 0.0);
    double max_v_err = 0.0;
    double max_v_scale = 0.0;

    /* For Backward Euler, discrete step energy satisfies:
     *   sum_elements (v_n * i_n * h) = 0, where for C:
     *     v_n * C*(v_n - v_{n-1}) = 0.5*C*(v_n^2 - v_{n-1}^2) + 0.5*C*(v_n - v_{n-1})^2
     *   and for L:
     *     i_n * L*(i_n - i_{n-1}) = 0.5*L*(i_n^2 - i_{n-1}^2) + 0.5*L*(i_n - i_{n-1})^2.
     * For Trapezoidal, midpoint average satisfies:
     *   sum_elements (0.5*(v_n + v_{n-1}) * i_avg * h) = 0 (exact zero numerical dissipation). */
    double energy_balance = 0.0;
    double energy_scale = 0.0;

    auto accum_kcl = [&](core::NodeId p, core::NodeId n, double i_pn) {
        kcl_sum[p.value] += i_pn;
        kcl_sum[n.value] -= i_pn;
        kcl_scale[p.value] += std::abs(i_pn);
        kcl_scale[n.value] += std::abs(i_pn);
    };

    for (const auto& elem : ckt.elements) {
        if (const auto* r = std::get_if<core::ResistorSpec>(&elem)) {
            const double v_curr = node_v(curr_x, r->p) - node_v(curr_x, r->n);
            const double i_curr = v_curr / r->ohms;
            if (method == TransientAuditMethod::BackwardEuler) {
                accum_kcl(r->p, r->n, i_curr);
                const double e = v_curr * i_curr * h;
                energy_balance += e;
                energy_scale += std::abs(e);
            } else {
                const double v_prev = node_v(prev_x, r->p) - node_v(prev_x, r->n);
                const double i_avg = 0.5 * (v_curr + v_prev) / r->ohms;
                accum_kcl(r->p, r->n, i_avg);
                const double e = 0.5 * (v_curr + v_prev) * i_avg * h;
                energy_balance += e;
                energy_scale += std::abs(e);
            }
        } else if (const auto* c = std::get_if<core::CapacitorSpec>(&elem)) {
            const double v_prev = node_v(prev_x, c->p) - node_v(prev_x, c->n);
            const double v_curr = node_v(curr_x, c->p) - node_v(curr_x, c->n);
            const double i_cap = c->farads * (v_curr - v_prev) / h;
            accum_kcl(c->p, c->n, i_cap);
            const double d_stored = 0.5 * c->farads * (v_curr * v_curr - v_prev * v_prev);
            const double diss =
                (method == TransientAuditMethod::BackwardEuler)
                    ? 0.5 * c->farads * (v_curr - v_prev) * (v_curr - v_prev)
                    : 0.0;
            energy_balance += d_stored + diss;
            energy_scale += std::abs(d_stored) + std::abs(diss);
        } else if (const auto* l = std::get_if<core::InductorSpec>(&elem)) {
            const auto br = plan.branch_unknowns.at(l->id).value;
            const double i_prev = prev_x[br];
            const double i_curr = curr_x[br];
            const double v_prev = node_v(prev_x, l->p) - node_v(prev_x, l->n);
            const double v_curr = node_v(curr_x, l->p) - node_v(curr_x, l->n);
            const double v_ind = l->henries * (i_curr - i_prev) / h;
            if (method == TransientAuditMethod::BackwardEuler) {
                accum_kcl(l->p, l->n, i_curr);
                max_v_err = std::max(max_v_err, std::abs(v_curr - v_ind));
                max_v_scale = std::max(max_v_scale, std::abs(v_curr) + std::abs(v_ind));
                const double d_stored = 0.5 * l->henries * (i_curr * i_curr - i_prev * i_prev);
                const double diss = 0.5 * l->henries * (i_curr - i_prev) * (i_curr - i_prev);
                energy_balance += d_stored + diss;
                energy_scale += std::abs(d_stored) + std::abs(diss);
            } else {
                const double i_avg = 0.5 * (i_curr + i_prev);
                const double v_avg = 0.5 * (v_curr + v_prev);
                accum_kcl(l->p, l->n, i_avg);
                max_v_err = std::max(max_v_err, std::abs(v_avg - v_ind));
                max_v_scale = std::max(max_v_scale, std::abs(v_avg) + std::abs(v_ind));
                const double d_stored = 0.5 * l->henries * (i_curr * i_curr - i_prev * i_prev);
                energy_balance += d_stored;
                energy_scale += std::abs(d_stored);
            }
        } else if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem)) {
            const auto br = plan.branch_unknowns.at(v->id).value;
            const double i_prev = prev_x[br];
            const double i_curr = curr_x[br];
            const double v_curr = node_v(curr_x, v->p) - node_v(curr_x, v->n);
            const double v_src_curr = core::source_value_at(*v, curr_t, true);
            max_v_err = std::max(max_v_err, std::abs(v_curr - v_src_curr));
            max_v_scale = std::max(max_v_scale, std::abs(v_curr) + std::abs(v_src_curr));
            if (method == TransientAuditMethod::BackwardEuler) {
                accum_kcl(v->p, v->n, i_curr);
                const double e = v_curr * i_curr * h;
                energy_balance += e;
                energy_scale += std::abs(e);
            } else {
                const double v_prev = node_v(prev_x, v->p) - node_v(prev_x, v->n);
                const double i_avg = 0.5 * (i_curr + i_prev);
                accum_kcl(v->p, v->n, i_avg);
                const double e = 0.5 * (v_curr + v_prev) * i_avg * h;
                energy_balance += e;
                energy_scale += std::abs(e);
            }
        } else if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem)) {
            const double i_curr = core::source_value_at(*i, curr_t, true);
            if (method == TransientAuditMethod::BackwardEuler) {
                accum_kcl(i->p, i->n, i_curr);
                const double v_curr = node_v(curr_x, i->p) - node_v(curr_x, i->n);
                const double e = v_curr * i_curr * h;
                energy_balance += e;
                energy_scale += std::abs(e);
            } else {
                const double i_prev = core::source_value_at(*i, prev_t, true);
                const double i_avg = 0.5 * (i_curr + i_prev);
                accum_kcl(i->p, i->n, i_avg);
                const double v_avg = 0.5 * ((node_v(curr_x, i->p) - node_v(curr_x, i->n)) +
                                            (node_v(prev_x, i->p) - node_v(prev_x, i->n)));
                const double e = v_avg * i_avg * h;
                energy_balance += e;
                energy_scale += std::abs(e);
            }
        } else if (const auto* g = std::get_if<core::VccsSpec>(&elem)) {
            const double i_curr = g->siemens * (node_v(curr_x, g->cp) - node_v(curr_x, g->cn));
            if (method == TransientAuditMethod::BackwardEuler) {
                accum_kcl(g->p, g->n, i_curr);
                const double v_curr = node_v(curr_x, g->p) - node_v(curr_x, g->n);
                const double e = v_curr * i_curr * h;
                energy_balance += e;
                energy_scale += std::abs(e);
            } else {
                const double i_prev = g->siemens * (node_v(prev_x, g->cp) - node_v(prev_x, g->cn));
                const double i_avg = 0.5 * (i_curr + i_prev);
                accum_kcl(g->p, g->n, i_avg);
                const double v_avg = 0.5 * ((node_v(curr_x, g->p) - node_v(curr_x, g->n)) +
                                            (node_v(prev_x, g->p) - node_v(prev_x, g->n)));
                const double e = v_avg * i_avg * h;
                energy_balance += e;
                energy_scale += std::abs(e);
            }
        } else if (const auto* e = std::get_if<core::VcvsSpec>(&elem)) {
            const auto br = plan.branch_unknowns.at(e->id).value;
            const double v_curr = node_v(curr_x, e->p) - node_v(curr_x, e->n);
            const double v_target = e->gain * (node_v(curr_x, e->cp) - node_v(curr_x, e->cn));
            max_v_err = std::max(max_v_err, std::abs(v_curr - v_target));
            max_v_scale = std::max(max_v_scale, std::abs(v_curr) + std::abs(v_target));
            if (method == TransientAuditMethod::BackwardEuler) {
                accum_kcl(e->p, e->n, curr_x[br]);
                const double en = v_curr * curr_x[br] * h;
                energy_balance += en;
                energy_scale += std::abs(en);
            } else {
                const double i_avg = 0.5 * (curr_x[br] + prev_x[br]);
                accum_kcl(e->p, e->n, i_avg);
                const double v_avg = 0.5 * (v_curr + (node_v(prev_x, e->p) - node_v(prev_x, e->n)));
                const double en = v_avg * i_avg * h;
                energy_balance += en;
                energy_scale += std::abs(en);
            }
        } else if (const auto* f = std::get_if<core::CccsSpec>(&elem)) {
            const auto ctrl = plan.branch_unknowns.at(f->control_source_id).value;
            if (method == TransientAuditMethod::BackwardEuler) {
                const double i_curr = f->gain * curr_x[ctrl];
                accum_kcl(f->p, f->n, i_curr);
                const double v_curr = node_v(curr_x, f->p) - node_v(curr_x, f->n);
                const double en = v_curr * i_curr * h;
                energy_balance += en;
                energy_scale += std::abs(en);
            } else {
                const double i_avg = 0.5 * f->gain * (curr_x[ctrl] + prev_x[ctrl]);
                accum_kcl(f->p, f->n, i_avg);
                const double v_avg = 0.5 * ((node_v(curr_x, f->p) - node_v(curr_x, f->n)) +
                                            (node_v(prev_x, f->p) - node_v(prev_x, f->n)));
                const double en = v_avg * i_avg * h;
                energy_balance += en;
                energy_scale += std::abs(en);
            }
        } else if (const auto* h_src = std::get_if<core::CcvsSpec>(&elem)) {
            const auto br = plan.branch_unknowns.at(h_src->id).value;
            const auto ctrl = plan.branch_unknowns.at(h_src->control_source_id).value;
            const double v_curr = node_v(curr_x, h_src->p) - node_v(curr_x, h_src->n);
            const double v_target = h_src->ohms * curr_x[ctrl];
            max_v_err = std::max(max_v_err, std::abs(v_curr - v_target));
            max_v_scale = std::max(max_v_scale, std::abs(v_curr) + std::abs(v_target));
            if (method == TransientAuditMethod::BackwardEuler) {
                accum_kcl(h_src->p, h_src->n, curr_x[br]);
                const double en = v_curr * curr_x[br] * h;
                energy_balance += en;
                energy_scale += std::abs(en);
            } else {
                const double i_avg = 0.5 * (curr_x[br] + prev_x[br]);
                accum_kcl(h_src->p, h_src->n, i_avg);
                const double v_avg = 0.5 * (v_curr + (node_v(prev_x, h_src->p) - node_v(prev_x, h_src->n)));
                const double en = v_avg * i_avg * h;
                energy_balance += en;
                energy_scale += std::abs(en);
            }
        }
    }

    double worst_kcl_err = 0.0;
    double worst_kcl_tol = opts.current_abs_tol;
    double worst_kcl_ratio = -1.0;
    for (std::size_t n = 0; n < n_nodes; ++n) {
        const double err = std::abs(kcl_sum[n]);
        const double tol = opts.current_abs_tol + opts.rel_tol * kcl_scale[n];
        const double ratio = err / tol;
        if (ratio > worst_kcl_ratio) {
            worst_kcl_ratio = ratio;
            worst_kcl_err = err;
            worst_kcl_tol = tol;
        }
    }

    report.checks.push_back(make_check("tran_kcl", worst_kcl_err, worst_kcl_tol, "A"));
    report.checks.push_back(make_check(
        "tran_voltage_constraints", max_v_err, opts.voltage_abs_tol + opts.rel_tol * max_v_scale, "V"));
    report.checks.push_back(make_check(
        "tran_discrete_energy", std::abs(energy_balance), opts.power_abs_tol + opts.rel_tol * energy_scale, "J"));
    report.checks.push_back(global_accuracy_not_checked());
    return report;
}

}  // namespace spice::audit
