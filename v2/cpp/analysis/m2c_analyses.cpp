#include "analysis/m2c_analyses.hpp"

#include <algorithm>
#include <cmath>
#include <complex>
#include <iomanip>
#include <limits>
#include <memory>
#include <numbers>
#include <random>
#include <sstream>
#include <unordered_map>
#include <utility>
#include <vector>

namespace spice::analysis {

namespace {

using Complex = std::complex<double>;
using core::CscPattern;
using core::Diagnostic;
using core::NodeId;
using core::SlotId;
using core::StatusCode;
using core::UnknownId;
using dae::AnalysisMode;
using dae::EvalContext;
using dae::StampTarget;

constexpr double kBoltzmann = 1.380649e-23;    /* J/K (exact SI 2019) */
constexpr double kElemCharge = 1.602176634e-19; /* C (exact SI 2019) */

struct TransposeMap {
    CscPattern pattern;
    std::vector<std::size_t> src_to_dst;
};

/* Build exact CSC transpose pattern and O(nnz) value mapping for adjoint solves Y^T * z = e. */
TransposeMap build_csc_transpose(const CscPattern& pat) {
    const std::uint32_t n = pat.dimension;
    TransposeMap out{};
    out.pattern.dimension = n;
    out.pattern.col_ptr.assign(static_cast<std::size_t>(n) + 1u, 0u);
    const std::size_t nnz = pat.nnz();
    out.pattern.row_idx.resize(nnz);
    out.src_to_dst.resize(nnz);

    for (std::size_t k = 0; k < nnz; ++k) {
        ++out.pattern.col_ptr[static_cast<std::size_t>(pat.row_idx[k]) + 1u];
    }
    for (std::uint32_t c = 0; c < n; ++c) {
        out.pattern.col_ptr[static_cast<std::size_t>(c) + 1u] += out.pattern.col_ptr[c];
    }

    std::vector<std::size_t> cursor = out.pattern.col_ptr;
    for (std::uint32_t c = 0; c < n; ++c) {
        for (std::size_t k = pat.col_ptr[c]; k < pat.col_ptr[c + 1u]; ++k) {
            const std::uint32_t r = pat.row_idx[k];
            const std::size_t dst = cursor[r]++;
            out.pattern.row_idx[dst] = c;
            out.src_to_dst[k] = dst;
        }
    }
    return out;
}

double node_val(std::span<const double> x, NodeId node) noexcept {
    if (auto u = devices::CompiledPlan::node_unknown(node)) {
        return x[u->value];
    }
    return 0.0;
}

Complex node_val_c(std::span<const Complex> x, NodeId node) noexcept {
    if (auto u = devices::CompiledPlan::node_unknown(node)) {
        return x[u->value];
    }
    return {0.0, 0.0};
}

Diagnostic linearize_at_op(
    devices::CompiledPlan& plan,
    const AnalysisCommonOptions& common,
    double temperature_k,
    std::vector<double>& out_x_op,
    std::vector<double>& out_g,
    std::vector<double>& out_c) {
    const std::uint32_t dim = plan.unknown_count();
    out_x_op.assign(dim, 0.0);
    if (plan.has_nonlinear) {
        AnalysisCommonOptions op_opts = common;
        op_opts.enable_audits = false;
        OpResult op{};
        if (auto d = run_op(plan, op_opts, op); !d.ok()) return d;
        out_x_op = std::move(op.values);
    } else {
        /* Still compute exact DC operating point for sensitivities / diode current if needed. */
        AnalysisCommonOptions op_opts = common;
        op_opts.enable_audits = false;
        OpResult op{};
        if (auto d = run_op(plan, op_opts, op); !d.ok()) return d;
        out_x_op = std::move(op.values);
    }

    std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
    out_g.assign(plan.pattern.nnz(), 0.0);
    out_c.assign(plan.pattern.nnz(), 0.0);
    EvalContext ctx{0.0, temperature_k, AnalysisMode::AcLinearize, out_x_op};
    StampTarget target{f0, q0, out_g, out_c};
    return plan.evaluate_all(ctx, target);
}

/* Fill unit excitation RHS for a named independent voltage or current source. */
Diagnostic build_source_unit_rhs(
    const devices::CompiledPlan& plan,
    const std::string& source_id,
    std::vector<double>& out_rhs,
    bool& out_is_voltage,
    std::optional<UnknownId>& out_vsrc_branch,
    NodeId& out_p,
    NodeId& out_n) {
    out_rhs.assign(plan.unknown_count(), 0.0);
    out_is_voltage = false;
    out_vsrc_branch.reset();
    out_p = NodeId{0};
    out_n = NodeId{0};

    for (const auto& elem : plan.circuit.elements) {
        if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem); v != nullptr && v->id == source_id) {
            const auto br = plan.branch_unknowns.at(v->id);
            out_rhs[br.value] = 1.0;
            out_is_voltage = true;
            out_vsrc_branch = br;
            out_p = v->p;
            out_n = v->n;
            return Diagnostic::success();
        }
        if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem); i != nullptr && i->id == source_id) {
            if (auto u = devices::CompiledPlan::node_unknown(i->p)) out_rhs[u->value] -= 1.0;
            if (auto u = devices::CompiledPlan::node_unknown(i->n)) out_rhs[u->value] += 1.0;
            out_is_voltage = false;
            out_p = i->p;
            out_n = i->n;
            return Diagnostic::success();
        }
    }
    return Diagnostic::error(StatusCode::InvalidArgument, "unknown independent source '" + source_id + "'");
}

double sample_signal_linear(const TransientResult& tran, std::size_t sig_idx, double t) {
    const auto& s = tran.samples;
    if (t <= s.front().time_s) return s.front().values[sig_idx];
    if (t >= s.back().time_s) return s.back().values[sig_idx];

    auto it = std::lower_bound(
        s.begin(), s.end(), t,
        [](const TransientSample& a, double val) { return a.time_s < val; });
    if (it == s.begin()) return it->values[sig_idx];
    const auto prev = it - 1;
    const double dt = it->time_s - prev->time_s;
    if (dt <= 0.0) return it->values[sig_idx];
    const double frac = (t - prev->time_s) / dt;
    return prev->values[sig_idx] + frac * (it->values[sig_idx] - prev->values[sig_idx]);
}

bool is_power_of_two(std::size_t n) noexcept {
    return n >= 2u && (n & (n - 1u)) == 0u;
}

/* In-place iterative Radix-2 Cooley-Tukey FFT (forward sign: exp(-j*2*pi*k*n/N)). */
void radix2_fft_inplace(std::vector<Complex>& a) {
    const std::size_t n = a.size();
    for (std::size_t i = 1, j = 0; i < n; ++i) {
        std::size_t bit = n >> 1u;
        for (; (j & bit) != 0u; bit >>= 1u) j ^= bit;
        j ^= bit;
        if (i < j) std::swap(a[i], a[j]);
    }
    for (std::size_t len = 2; len <= n; len <<= 1u) {
        const double ang = -2.0 * std::numbers::pi / static_cast<double>(len);
        const Complex wlen{std::cos(ang), std::sin(ang)};
        for (std::size_t i = 0; i < n; i += len) {
            Complex w{1.0, 0.0};
            for (std::size_t j = 0; j < len / 2u; ++j) {
                const Complex u = a[i + j];
                const Complex v = a[i + j + len / 2u] * w;
                a[i + j] = u + v;
                a[i + j + len / 2u] = u - v;
                w *= wlen;
            }
        }
    }
}

}  // namespace

/* ==================== 1. .TF ==================== */
Diagnostic run_tf(
    devices::CompiledPlan& plan,
    const TfOptions& opts,
    TfResult& out_result) {
    if (opts.out_p.value >= plan.circuit.nodes.size() || opts.out_n.value >= plan.circuit.nodes.size() ||
        opts.out_p == opts.out_n) {
        return Diagnostic::error(StatusCode::InvalidArgument, "invalid .TF output port nodes");
    }

    std::vector<double> x_op, g_vals, c_vals;
    if (auto d = linearize_at_op(plan, opts.common, 300.15, x_op, g_vals, c_vals); !d.ok()) return d;

    std::vector<double> rhs_in;
    bool is_vsrc = false;
    std::optional<UnknownId> vsrc_br;
    NodeId in_p{0}, in_n{0};
    if (auto d = build_source_unit_rhs(plan, opts.input_source_id, rhs_in, is_vsrc, vsrc_br, in_p, in_n);
        !d.ok()) {
        return d;
    }

    std::unique_ptr<linalg::RealLinearSolver> solver;
    if (auto d = linalg::create_real_solver(opts.common.backend, solver); !d.ok()) return d;
    if (auto d = solver->analyze(plan.pattern); !d.ok()) return d;
    if (auto d = solver->factor(g_vals); !d.ok()) return d;

    const std::uint32_t dim = plan.unknown_count();
    std::vector<double> dx_in(dim, 0.0);
    if (auto d = solver->solve(rhs_in, dx_in); !d.ok()) return d;

    /* Output resistance: inject 1 A test current into output port (out_p -> out_n) with sources zeroed. */
    std::vector<double> rhs_out(dim, 0.0);
    if (auto u = devices::CompiledPlan::node_unknown(opts.out_p)) rhs_out[u->value] += 1.0;
    if (auto u = devices::CompiledPlan::node_unknown(opts.out_n)) rhs_out[u->value] -= 1.0;
    std::vector<double> dx_out(dim, 0.0);
    if (auto d = solver->solve(rhs_out, dx_out); !d.ok()) return d;

    TfResult res{};
    res.backend_name = solver->name();
    res.operating_point = std::move(x_op);
    res.transfer_gain = node_val(dx_in, opts.out_p) - node_val(dx_in, opts.out_n);
    res.output_resistance = node_val(dx_out, opts.out_p) - node_val(dx_out, opts.out_n);

    if (is_vsrc) {
        /* Passive sign convention: branch current flows from p -> n inside source, so supplied current is -i_br. */
        const double i_supplied = -dx_in[vsrc_br->value];
        res.input_resistance =
            (std::abs(i_supplied) > 1.0e-30) ? (1.0 / i_supplied) : std::numeric_limits<double>::infinity();
    } else {
        /* Current source I flows from p -> n; test +1 A from n -> p gives V(n)-V(p) = -(V(p)-V(n)). */
        res.input_resistance = node_val(dx_in, in_n) - node_val(dx_in, in_p);
    }

    out_result = std::move(res);
    return Diagnostic::success();
}

/* ==================== 2. .NOISE ==================== */
Diagnostic run_noise(
    devices::CompiledPlan& plan,
    const NoiseOptions& opts,
    NoiseResult& out_result) {
    if (opts.frequencies_hz.empty()) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".NOISE requires at least one frequency");
    }
    if (!std::isfinite(opts.temperature_k) || opts.temperature_k <= 0.0) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".NOISE temperature_k must be positive");
    }
    if (opts.out_p.value >= plan.circuit.nodes.size() || opts.out_n.value >= plan.circuit.nodes.size() ||
        opts.out_p == opts.out_n) {
        return Diagnostic::error(StatusCode::InvalidArgument, "invalid .NOISE output port nodes");
    }

    std::vector<double> x_op, g_vals, c_vals;
    if (auto d = linearize_at_op(plan, opts.common, opts.temperature_k, x_op, g_vals, c_vals); !d.ok()) {
        return d;
    }

    std::vector<double> rhs_in_real;
    bool is_vsrc = false;
    std::optional<UnknownId> vsrc_br;
    NodeId in_p{0}, in_n{0};
    if (auto d = build_source_unit_rhs(plan, opts.input_source_id, rhs_in_real, is_vsrc, vsrc_br, in_p, in_n);
        !d.ok()) {
        return d;
    }

    const std::uint32_t dim = plan.unknown_count();
    const TransposeMap tmap = build_csc_transpose(plan.pattern);

    std::unique_ptr<linalg::ComplexLinearSolver> adj_solver;
    if (auto d = linalg::create_complex_solver(opts.common.backend, adj_solver); !d.ok()) return d;
    if (auto d = adj_solver->analyze(tmap.pattern); !d.ok()) return d;

    std::vector<Complex> e_out(dim, Complex{0.0, 0.0});
    if (auto u = devices::CompiledPlan::node_unknown(opts.out_p)) e_out[u->value] = {1.0, 0.0};
    if (auto u = devices::CompiledPlan::node_unknown(opts.out_n)) e_out[u->value] = {-1.0, 0.0};

    std::vector<Complex> a_T_vals(plan.pattern.nnz());
    std::vector<Complex> z(dim);

    NoiseResult res{};
    res.backend_name = adj_solver->name();
    res.points.reserve(opts.frequencies_hz.size());

    for (double f_hz : opts.frequencies_hz) {
        if (!std::isfinite(f_hz) || f_hz < 0.0) {
            return Diagnostic::error(StatusCode::InvalidArgument, ".NOISE frequencies must be finite and >= 0");
        }
        const double omega = 2.0 * std::numbers::pi * f_hz;
        for (std::size_t k = 0; k < plan.pattern.nnz(); ++k) {
            a_T_vals[tmap.src_to_dst[k]] = Complex{g_vals[k], omega * c_vals[k]};
        }
        if (auto d = adj_solver->factor(a_T_vals); !d.ok()) return d;
        if (auto d = adj_solver->solve(e_out, z); !d.ok()) return d;

        /* Transfer gain from input source to output port via reciprocity: H(jw) = z^T * b_in! */
        Complex h_gain{0.0, 0.0};
        for (std::uint32_t i = 0; i < dim; ++i) {
            h_gain += z[i] * rhs_in_real[i];
        }
        const double gain_mag = std::abs(h_gain);

        NoisePoint pt{};
        pt.frequency_hz = f_hz;
        pt.transfer_gain_mag = gain_mag;
        double total_psd = 0.0;

        for (const auto& elem : plan.circuit.elements) {
            if (const auto* r = std::get_if<core::ResistorSpec>(&elem)) {
                if (r->ohms <= 0.0) continue;
                const double si = 4.0 * kBoltzmann * opts.temperature_k / r->ohms;
                const Complex tf = node_val_c(z, r->p) - node_val_c(z, r->n);
                const double out_psd = std::norm(tf) * si;
                total_psd += out_psd;
                pt.contributions.push_back(DeviceNoiseContribution{r->id, "thermal", out_psd});
            } else if (const auto* d = std::get_if<core::DiodeSpec>(&elem)) {
                const double vd = node_val(x_op, d->p) - node_val(x_op, d->n);
                const double nvt = d->n_emission * d->vt_volts;
                const double id = d->is_amps * std::expm1(std::min(vd / nvt, 80.0));
                const double si = 2.0 * kElemCharge * std::abs(id);
                const Complex tf = node_val_c(z, d->p) - node_val_c(z, d->n);
                const double out_psd = std::norm(tf) * si;
                total_psd += out_psd;
                pt.contributions.push_back(DeviceNoiseContribution{d->id, "shot", out_psd});
            }
        }

        pt.onoise_v_per_sqrt_hz = std::sqrt(std::max(0.0, total_psd));
        pt.inoise_v_per_sqrt_hz =
            (gain_mag > 1.0e-30) ? (pt.onoise_v_per_sqrt_hz / gain_mag) : std::numeric_limits<double>::infinity();
        res.points.push_back(std::move(pt));
    }

    /* Integrate total RMS noise across frequency grid via trapezoidal rule on PSD (V^2/Hz). */
    double o_var = 0.0;
    double i_var = 0.0;
    for (std::size_t k = 1u; k < res.points.size(); ++k) {
        const double df = res.points[k].frequency_hz - res.points[k - 1u].frequency_hz;
        if (df <= 0.0) continue;
        const double op0 = res.points[k - 1u].onoise_v_per_sqrt_hz;
        const double op1 = res.points[k].onoise_v_per_sqrt_hz;
        o_var += 0.5 * (op0 * op0 + op1 * op1) * df;

        const double ip0 = res.points[k - 1u].inoise_v_per_sqrt_hz;
        const double ip1 = res.points[k].inoise_v_per_sqrt_hz;
        if (std::isfinite(ip0) && std::isfinite(ip1)) {
            i_var += 0.5 * (ip0 * ip0 + ip1 * ip1) * df;
        }
    }
    res.integrated_onoise_vrms = std::sqrt(std::max(0.0, o_var));
    res.integrated_inoise_vrms = std::sqrt(std::max(0.0, i_var));

    out_result = std::move(res);
    return Diagnostic::success();
}

/* ==================== 3. .SENS ==================== */
Diagnostic run_sens(
    devices::CompiledPlan& plan,
    const SensOptions& opts,
    SensResult& out_result) {
    if (opts.out_p.value >= plan.circuit.nodes.size() || opts.out_n.value >= plan.circuit.nodes.size() ||
        opts.out_p == opts.out_n) {
        return Diagnostic::error(StatusCode::InvalidArgument, "invalid .SENS output port nodes");
    }

    std::vector<double> x_op, g_vals, c_vals;
    if (auto d = linearize_at_op(plan, opts.common, 300.15, x_op, g_vals, c_vals); !d.ok()) return d;

    const std::uint32_t dim = plan.unknown_count();
    const TransposeMap tmap = build_csc_transpose(plan.pattern);
    std::vector<double> g_T_vals(plan.pattern.nnz());
    for (std::size_t k = 0; k < plan.pattern.nnz(); ++k) {
        g_T_vals[tmap.src_to_dst[k]] = g_vals[k];
    }

    std::unique_ptr<linalg::RealLinearSolver> adj_solver;
    if (auto d = linalg::create_real_solver(opts.common.backend, adj_solver); !d.ok()) return d;
    if (auto d = adj_solver->analyze(tmap.pattern); !d.ok()) return d;
    if (auto d = adj_solver->factor(g_T_vals); !d.ok()) return d;

    std::vector<double> e_out(dim, 0.0);
    if (auto u = devices::CompiledPlan::node_unknown(opts.out_p)) e_out[u->value] = 1.0;
    if (auto u = devices::CompiledPlan::node_unknown(opts.out_n)) e_out[u->value] = -1.0;

    std::vector<double> lam(dim, 0.0);
    if (auto d = adj_solver->solve(e_out, lam); !d.ok()) return d;

    SensResult res{};
    res.backend_name = adj_solver->name();
    res.nominal_output_volts = node_val(x_op, opts.out_p) - node_val(x_op, opts.out_n);

    auto add_entry = [&](const std::string& id, const std::string& pname, double nom, double sens) {
        const double norm = (std::abs(res.nominal_output_volts) > 1.0e-30)
                                ? (nom / res.nominal_output_volts) * sens
                                : 0.0;
        res.entries.push_back(SensitivityEntry{id, pname, nom, sens, norm});
    };

    for (const auto& elem : plan.circuit.elements) {
        if (const auto* r = std::get_if<core::ResistorSpec>(&elem)) {
            const double vr = node_val(x_op, r->p) - node_val(x_op, r->n);
            const double dlam = node_val(lam, r->p) - node_val(lam, r->n);
            /* F_p = +vr/R, dF_p/dR = -vr/R^2 -> dV_out/dR = -lam^T dF/dR = +dlam * vr / R^2. */
            add_entry(r->id, "ohms", r->ohms, dlam * vr / (r->ohms * r->ohms));
        } else if (const auto* v = std::get_if<core::VoltageSourceSpec>(&elem)) {
            /* F_br = V(p) - V(n) - V_dc -> dF_br/dV_dc = -1 -> dV_out/dV_dc = +lam_br. */
            const auto br = plan.branch_unknowns.at(v->id);
            add_entry(v->id, "dc_volts", v->dc_volts, lam[br.value]);
        } else if (const auto* i = std::get_if<core::CurrentSourceSpec>(&elem)) {
            /* F_p = +I_dc, F_n = -I_dc -> dV_out/dI_dc = -(lam_p - lam_n). */
            const double dlam = node_val(lam, i->p) - node_val(lam, i->n);
            add_entry(i->id, "dc_amps", i->dc_amps, -dlam);
        } else if (const auto* g = std::get_if<core::VccsSpec>(&elem)) {
            /* F_p = +gm * V_c -> dV_out/dgm = -(lam_p - lam_n) * V_c. */
            const double vc = node_val(x_op, g->cp) - node_val(x_op, g->cn);
            const double dlam = node_val(lam, g->p) - node_val(lam, g->n);
            add_entry(g->id, "siemens", g->siemens, -dlam * vc);
        } else if (const auto* e = std::get_if<core::VcvsSpec>(&elem)) {
            /* F_br = V(p)-V(n) - A*V_c -> dF_br/dA = -V_c -> dV_out/dA = +lam_br * V_c. */
            const auto br = plan.branch_unknowns.at(e->id);
            const double vc = node_val(x_op, e->cp) - node_val(x_op, e->cn);
            add_entry(e->id, "gain", e->gain, lam[br.value] * vc);
        } else if (const auto* d = std::get_if<core::DiodeSpec>(&elem)) {
            /* F_p = +Is * expm1(Vd/nVt) -> dF_p/dIs = expm1(Vd/nVt) -> dV_out/dIs = -dlam * expm1. */
            const double vd = node_val(x_op, d->p) - node_val(x_op, d->n);
            const double dlam = node_val(lam, d->p) - node_val(lam, d->n);
            const double ex = std::expm1(std::min(vd / (d->n_emission * d->vt_volts), 80.0));
            add_entry(d->id, "is_amps", d->is_amps, -dlam * ex);
        }
    }

    out_result = std::move(res);
    return Diagnostic::success();
}

/* ==================== 4. .SP & Touchstone ==================== */
Diagnostic run_sp(
    devices::CompiledPlan& plan,
    const SpOptions& opts,
    SpResult& out_result) {
    if (opts.ports.empty() || opts.frequencies_hz.empty()) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".SP requires at least one port and one frequency");
    }
    for (const auto& port : opts.ports) {
        if (port.node_p.value >= plan.circuit.nodes.size() || port.node_n.value >= plan.circuit.nodes.size() ||
            port.node_p == port.node_n || !std::isfinite(port.z0_ohms) || port.z0_ohms <= 0.0) {
            return Diagnostic::error(StatusCode::InvalidArgument, "invalid .SP port specification");
        }
    }

    const std::uint32_t dim = plan.unknown_count();
    std::vector<double> x_op(dim, 0.0), g_vals(plan.pattern.nnz(), 0.0), c_vals(plan.pattern.nnz(), 0.0);
    if (plan.has_nonlinear) {
        if (auto d = linearize_at_op(plan, opts.common, 300.15, x_op, g_vals, c_vals); !d.ok()) return d;
    } else {
        std::vector<double> f0(dim, 0.0), q0(dim, 0.0);
        EvalContext ctx{0.0, 300.15, AnalysisMode::AcLinearize, x_op};
        StampTarget target{f0, q0, g_vals, c_vals};
        if (auto d = plan.evaluate_all(ctx, target); !d.ok()) return d;
    }

    /* Build augmented CSC pattern guaranteed to include every port termination admittance slot. */
    std::vector<std::pair<std::uint32_t, std::uint32_t>> coords;
    coords.reserve(plan.pattern.nnz() + opts.ports.size() * 4u);
    for (std::uint32_t c = 0; c < dim; ++c) {
        for (std::size_t k = plan.pattern.col_ptr[c]; k < plan.pattern.col_ptr[c + 1u]; ++k) {
            coords.emplace_back(plan.pattern.row_idx[k], c);
        }
    }
    auto add_opt = [&](std::optional<UnknownId> r, std::optional<UnknownId> c) {
        if (r && c) coords.emplace_back(r->value, c->value);
    };
    for (const auto& port : opts.ports) {
        auto p = devices::CompiledPlan::node_unknown(port.node_p);
        auto n = devices::CompiledPlan::node_unknown(port.node_n);
        add_opt(p, p);
        add_opt(p, n);
        add_opt(n, p);
        add_opt(n, n);
    }
    std::sort(coords.begin(), coords.end(), [](const auto& a, const auto& b) {
        return (a.second != b.second) ? (a.second < b.second) : (a.first < b.first);
    });
    coords.erase(std::unique(coords.begin(), coords.end()), coords.end());

    CscPattern aug_pat{};
    aug_pat.dimension = dim;
    aug_pat.col_ptr.assign(static_cast<std::size_t>(dim) + 1u, 0u);
    aug_pat.row_idx.reserve(coords.size());
    for (const auto& [r, c] : coords) {
        aug_pat.row_idx.push_back(r);
        ++aug_pat.col_ptr[static_cast<std::size_t>(c) + 1u];
    }
    for (std::uint32_t c = 0; c < dim; ++c) {
        aug_pat.col_ptr[static_cast<std::size_t>(c) + 1u] += aug_pat.col_ptr[c];
    }

    std::vector<std::size_t> base_to_aug(plan.pattern.nnz());
    for (std::uint32_t c = 0; c < dim; ++c) {
        for (std::size_t k = plan.pattern.col_ptr[c]; k < plan.pattern.col_ptr[c + 1u]; ++k) {
            SlotId slot{};
            (void)aug_pat.find_slot(plan.pattern.row_idx[k], c, slot);
            base_to_aug[k] = slot.value;
        }
    }

    struct PortSlots {
        std::optional<SlotId> pp, pn, np, nn;
        double y0{0.0};
    };
    std::vector<PortSlots> port_slots(opts.ports.size());
    for (std::size_t i = 0; i < opts.ports.size(); ++i) {
        auto p = devices::CompiledPlan::node_unknown(opts.ports[i].node_p);
        auto n = devices::CompiledPlan::node_unknown(opts.ports[i].node_n);
        port_slots[i].y0 = 1.0 / opts.ports[i].z0_ohms;
        auto find_opt = [&](std::optional<UnknownId> r, std::optional<UnknownId> c, std::optional<SlotId>& out) {
            if (r && c) {
                SlotId s{};
                if (aug_pat.find_slot(r->value, c->value, s)) out = s;
            }
        };
        find_opt(p, p, port_slots[i].pp);
        find_opt(p, n, port_slots[i].pn);
        find_opt(n, p, port_slots[i].np);
        find_opt(n, n, port_slots[i].nn);
    }

    std::unique_ptr<linalg::ComplexLinearSolver> solver;
    if (auto d = linalg::create_complex_solver(opts.common.backend, solver); !d.ok()) return d;
    if (auto d = solver->analyze(aug_pat); !d.ok()) return d;

    const std::size_t n_ports = opts.ports.size();
    std::vector<Complex> a_vals(aug_pat.nnz());
    std::vector<Complex> rhs(dim), x(dim);

    SpResult res{};
    res.backend_name = solver->name();
    res.port_count = n_ports;
    res.reference_z0_ohms = opts.ports.front().z0_ohms;
    res.points.reserve(opts.frequencies_hz.size());

    for (double f_hz : opts.frequencies_hz) {
        if (!std::isfinite(f_hz) || f_hz < 0.0) {
            return Diagnostic::error(StatusCode::InvalidArgument, ".SP frequencies must be finite and >= 0");
        }
        const double omega = 2.0 * std::numbers::pi * f_hz;
        std::fill(a_vals.begin(), a_vals.end(), Complex{0.0, 0.0});
        for (std::size_t k = 0; k < plan.pattern.nnz(); ++k) {
            a_vals[base_to_aug[k]] = Complex{g_vals[k], omega * c_vals[k]};
        }
        for (const auto& ps : port_slots) {
            if (ps.pp) a_vals[ps.pp->value] += ps.y0;
            if (ps.pn) a_vals[ps.pn->value] -= ps.y0;
            if (ps.np) a_vals[ps.np->value] -= ps.y0;
            if (ps.nn) a_vals[ps.nn->value] += ps.y0;
        }
        if (auto d = solver->factor(a_vals); !d.ok()) return d;

        SpPoint pt{};
        pt.frequency_hz = f_hz;
        pt.s_matrix.assign(n_ports * n_ports, Complex{0.0, 0.0});

        for (std::size_t j = 0; j < n_ports; ++j) {
            std::fill(rhs.begin(), rhs.end(), Complex{0.0, 0.0});
            const double i_norton = 2.0 / std::sqrt(opts.ports[j].z0_ohms);
            if (auto u = devices::CompiledPlan::node_unknown(opts.ports[j].node_p)) rhs[u->value] = {i_norton, 0.0};
            if (auto u = devices::CompiledPlan::node_unknown(opts.ports[j].node_n)) rhs[u->value] = {-i_norton, 0.0};

            if (auto d = solver->solve(rhs, x); !d.ok()) return d;
            for (std::size_t i = 0; i < n_ports; ++i) {
                const Complex v_i = node_val_c(x, opts.ports[i].node_p) - node_val_c(x, opts.ports[i].node_n);
                Complex s_ij = v_i / std::sqrt(opts.ports[i].z0_ohms);
                if (i == j) s_ij -= Complex{1.0, 0.0};
                pt.s_matrix[i * n_ports + j] = s_ij;
            }
        }
        res.points.push_back(std::move(pt));
    }

    out_result = std::move(res);
    return Diagnostic::success();
}

std::string format_touchstone(const SpResult& sp) {
    std::ostringstream oss;
    oss << std::setprecision(12);
    oss << "! Spice V2 Touchstone v1.1 S-Parameter Export (" << sp.port_count << "-port)\n";
    oss << "# HZ S RI R " << sp.reference_z0_ohms << "\n";
    const std::size_t n = sp.port_count;
    for (const auto& pt : sp.points) {
        oss << pt.frequency_hz;
        if (n == 2u) {
            /* Touchstone 2-port standard order: S11 S21 S12 S22. */
            const std::size_t order[4] = {0u, 2u, 1u, 3u};
            for (std::size_t idx : order) {
                oss << " " << pt.s_matrix[idx].real() << " " << pt.s_matrix[idx].imag();
            }
            oss << "\n";
        } else {
            for (std::size_t i = 0; i < n; ++i) {
                if (i > 0u) oss << "   ";
                for (std::size_t j = 0; j < n; ++j) {
                    const auto& s = pt.s_matrix[i * n + j];
                    oss << " " << s.real() << " " << s.imag();
                }
                oss << "\n";
            }
        }
    }
    return oss.str();
}

/* ==================== 5. .FOUR & .FFT ==================== */
Diagnostic compute_fourier(
    const TransientResult& tran,
    std::size_t signal_index,
    double fundamental_hz,
    std::size_t num_harmonics,
    FourierResult& out_result) {
    if (tran.samples.size() < 2u || signal_index >= tran.samples.front().values.size()) {
        return Diagnostic::error(StatusCode::InvalidArgument, "invalid transient samples or signal index for .FOUR");
    }
    if (!std::isfinite(fundamental_hz) || fundamental_hz <= 0.0 || num_harmonics == 0u) {
        return Diagnostic::error(StatusCode::InvalidArgument, "invalid fundamental frequency or harmonic count");
    }
    const double period = 1.0 / fundamental_hz;
    const double t_end = tran.samples.back().time_s;
    const double t_start = t_end - period;
    if (t_start < tran.samples.front().time_s - 1.0e-12 * period) {
        return Diagnostic::error(StatusCode::InvalidArgument, "transient span shorter than one fundamental period");
    }

    constexpr std::size_t kGrid = 1024u;
    std::vector<double> y(kGrid);
    for (std::size_t i = 0; i < kGrid; ++i) {
        const double t = t_start + (static_cast<double>(i) / static_cast<double>(kGrid)) * period;
        y[i] = sample_signal_linear(tran, signal_index, t);
    }

    FourierResult res{};
    res.fundamental_hz = fundamental_hz;
    double sum0 = 0.0;
    for (double v : y) sum0 += v;
    res.dc_component = sum0 / static_cast<double>(kGrid);
    res.harmonics.push_back(FourierHarmonic{0u, 0.0, std::abs(res.dc_component), 0.0, 0.0});

    double fund_mag = 0.0;
    double harm_sq_sum = 0.0;
    for (std::size_t h = 1u; h <= num_harmonics; ++h) {
        double a_h = 0.0;
        double b_h = 0.0;
        for (std::size_t i = 0; i < kGrid; ++i) {
            const double theta =
                2.0 * std::numbers::pi * static_cast<double>(h) * static_cast<double>(i) / static_cast<double>(kGrid);
            a_h += y[i] * std::cos(theta);
            b_h += y[i] * std::sin(theta);
        }
        a_h *= 2.0 / static_cast<double>(kGrid);
        b_h *= 2.0 / static_cast<double>(kGrid);
        const double mag = std::hypot(a_h, b_h);
        /* SPICE .FOUR convention: y(t) = DC + sum mag * sin(h*w*t + phase). */
        const double phase_deg = std::atan2(a_h, b_h) * 180.0 / std::numbers::pi;
        if (h == 1u) {
            fund_mag = mag;
        } else {
            harm_sq_sum += mag * mag;
        }
        const double norm = (fund_mag > 1.0e-30) ? (mag / fund_mag) : 0.0;
        res.harmonics.push_back(
            FourierHarmonic{h, static_cast<double>(h) * fundamental_hz, mag, phase_deg, norm});
    }
    res.thd_percent = (fund_mag > 1.0e-30) ? (100.0 * std::sqrt(harm_sq_sum) / fund_mag) : 0.0;

    out_result = std::move(res);
    return Diagnostic::success();
}

Diagnostic compute_fft(
    const TransientResult& tran,
    std::size_t signal_index,
    std::size_t fft_size_power_of_two,
    bool apply_hann_window,
    FftResult& out_result) {
    if (tran.samples.size() < 2u || signal_index >= tran.samples.front().values.size()) {
        return Diagnostic::error(StatusCode::InvalidArgument, "invalid transient samples or signal index for .FFT");
    }
    if (!is_power_of_two(fft_size_power_of_two)) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".FFT size must be a power of two >= 2");
    }
    const double t0 = tran.samples.front().time_s;
    const double t1 = tran.samples.back().time_s;
    const double span = t1 - t0;
    if (span <= 0.0) {
        return Diagnostic::error(StatusCode::InvalidArgument, "transient span must be positive for .FFT");
    }

    const std::size_t n = fft_size_power_of_two;
    const double dt = span / static_cast<double>(n);
    std::vector<Complex> buf(n);
    double win_sum = 0.0;
    for (std::size_t i = 0; i < n; ++i) {
        const double t = t0 + static_cast<double>(i) * dt;
        const double w = apply_hann_window
                             ? 0.5 * (1.0 - std::cos(2.0 * std::numbers::pi * static_cast<double>(i) / static_cast<double>(n)))
                             : 1.0;
        win_sum += w;
        buf[i] = Complex{sample_signal_linear(tran, signal_index, t) * w, 0.0};
    }

    radix2_fft_inplace(buf);

    const std::size_t n_bins = n / 2u + 1u;
    FftResult res{};
    res.fft_size = n;
    res.sample_rate_hz = 1.0 / dt;
    res.frequencies_hz.resize(n_bins);
    res.spectrum.resize(n_bins);
    res.magnitude.resize(n_bins);
    res.magnitude_db.resize(n_bins);

    for (std::size_t k = 0; k < n_bins; ++k) {
        res.frequencies_hz[k] = static_cast<double>(k) * (res.sample_rate_hz / static_cast<double>(n));
        const double scale = (k == 0u || k == n / 2u) ? (1.0 / win_sum) : (2.0 / win_sum);
        res.spectrum[k] = buf[k] * scale;
        const double mag = std::abs(res.spectrum[k]);
        res.magnitude[k] = mag;
        res.magnitude_db[k] = 20.0 * std::log10(std::max(mag, 1.0e-30));
    }

    out_result = std::move(res);
    return Diagnostic::success();
}

/* ==================== 6. .MC (Monte Carlo) ==================== */
Diagnostic run_monte_carlo_op(
    const core::Circuit& nominal_circuit,
    const MonteCarloOptions& opts,
    MonteCarloResult& out_result) {
    if (opts.trials == 0u) {
        return Diagnostic::error(StatusCode::InvalidArgument, ".MC requires at least 1 trial");
    }
    for (const auto& tol : opts.tolerances) {
        if (tol.element_id.empty() || !std::isfinite(tol.relative_tol) || tol.relative_tol < 0.0) {
            return Diagnostic::error(StatusCode::InvalidArgument, "invalid .MC tolerance specification");
        }
    }

    std::mt19937_64 rng(opts.seed);
    std::unordered_map<std::string, ElementTolerance> tol_map;
    for (const auto& tol : opts.tolerances) {
        tol_map[tol.element_id] = tol;
    }

    MonteCarloResult res{};
    res.seed = opts.seed;
    res.trial_op_values.reserve(opts.trials);
    std::vector<std::string> signal_ids;

    for (std::size_t trial = 0; trial < opts.trials; ++trial) {
        core::Circuit perturbed = nominal_circuit;
        for (auto& elem : perturbed.elements) {
            const std::string& id = core::element_id(elem);
            const auto it = tol_map.find(id);
            if (it == tol_map.end()) continue;

            double delta = 0.0;
            if (it->second.distribution == ToleranceDistribution::Gaussian) {
                std::normal_distribution<double> dist(0.0, it->second.relative_tol);
                delta = dist(rng);
            } else {
                std::uniform_real_distribution<double> dist(-it->second.relative_tol, it->second.relative_tol);
                delta = dist(rng);
            }
            const double factor = std::max(0.05, 1.0 + delta);

            if (auto* r = std::get_if<core::ResistorSpec>(&elem)) r->ohms *= factor;
            else if (auto* c = std::get_if<core::CapacitorSpec>(&elem)) c->farads *= factor;
            else if (auto* l = std::get_if<core::InductorSpec>(&elem)) l->henries *= factor;
            else if (auto* v = std::get_if<core::VoltageSourceSpec>(&elem)) v->dc_volts *= factor;
            else if (auto* i = std::get_if<core::CurrentSourceSpec>(&elem)) i->dc_amps *= factor;
            else if (auto* d = std::get_if<core::DiodeSpec>(&elem)) d->is_amps *= factor;
        }

        devices::CompiledPlan plan;
        if (auto d = devices::compile_circuit(perturbed, plan); !d.ok()) return d;
        if (trial == 0u) {
            signal_ids.reserve(plan.signals.size());
            for (const auto& s : plan.signals) signal_ids.push_back(s.id);
        }

        OpResult op{};
        if (auto d = run_op(plan, opts.common, op); !d.ok()) return d;
        res.trial_op_values.push_back(std::move(op.values));
    }

    res.completed_trials = res.trial_op_values.size();
    const std::size_t n_sig = signal_ids.size();
    res.stats.resize(n_sig);
    for (std::size_t s = 0; s < n_sig; ++s) {
        double sum = 0.0;
        double mn = std::numeric_limits<double>::infinity();
        double mx = -std::numeric_limits<double>::infinity();
        for (const auto& row : res.trial_op_values) {
            const double v = row[s];
            sum += v;
            mn = std::min(mn, v);
            mx = std::max(mx, v);
        }
        const double mean = sum / static_cast<double>(res.completed_trials);
        double var_sum = 0.0;
        for (const auto& row : res.trial_op_values) {
            const double diff = row[s] - mean;
            var_sum += diff * diff;
        }
        const double sd =
            (res.completed_trials > 1u) ? std::sqrt(var_sum / static_cast<double>(res.completed_trials - 1u)) : 0.0;
        res.stats[s] = SignalStatistics{signal_ids[s], mean, sd, mn, mx};
    }

    out_result = std::move(res);
    return Diagnostic::success();
}

}  // namespace spice::analysis
