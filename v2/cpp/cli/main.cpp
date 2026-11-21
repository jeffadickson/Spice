#include "analysis/analyses.hpp"
#include "analysis/m2c_analyses.hpp"
#include "analysis/rf_steady_state.hpp"
#include "devices/linear_devices.hpp"
#include "parser/spice_parser.hpp"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <optional>
#include <sstream>
#include <string>
#include <vector>

namespace {

constexpr int kExitOk = 0;
constexpr int kExitUsage = 2;
constexpr int kExitParseOrValidate = 3;
constexpr int kExitSolverError = 4;
constexpr int kExitAuditError = 5;

std::string to_lower_str(std::string s) {
    for (char& c : s) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return s;
}

std::string json_escape(const std::string& s) {
    std::string out;
    out.reserve(s.size() + 8u);
    for (char c : s) {
        switch (c) {
            case '"': out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\n': out += "\\n"; break;
            case '\r': out += "\\r"; break;
            case '\t': out += "\\t"; break;
            default: out.push_back(c); break;
        }
    }
    return out;
}

bool read_file(const std::string& path, std::string& out_content) {
    std::ifstream ifs(path);
    if (!ifs) return false;
    std::ostringstream oss;
    oss << ifs.rdbuf();
    out_content = oss.str();
    return true;
}

std::optional<std::size_t> find_signal_index(
    const spice::devices::CompiledPlan& plan,
    const std::string& raw_sig) {
    const std::string want = to_lower_str(raw_sig);
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (to_lower_str(plan.signals[i].id) == want) return i;
    }
    return std::nullopt;
}

std::string find_pz_input_source(
    const spice::core::Circuit& circuit,
    spice::core::NodeId in_p,
    spice::core::NodeId in_n) {
    for (const auto& elem : circuit.elements) {
        if (const auto* v = std::get_if<spice::core::VoltageSourceSpec>(&elem)) {
            if ((v->p == in_p && v->n == in_n) || (v->p == in_n && v->n == in_p)) return v->id;
        } else if (const auto* i = std::get_if<spice::core::CurrentSourceSpec>(&elem)) {
            if ((i->p == in_p && i->n == in_n) || (i->p == in_n && i->n == in_p)) return i->id;
        }
    }
    for (const auto& elem : circuit.elements) {
        if (const auto* v = std::get_if<spice::core::VoltageSourceSpec>(&elem)) return v->id;
        if (const auto* i = std::get_if<spice::core::CurrentSourceSpec>(&elem)) return i->id;
    }
    return {};
}

void print_usage() {
    std::cerr
        << "Usage:\n"
        << "  spice_v2_cli version\n"
        << "  spice_v2_cli models\n"
        << "  spice_v2_cli check <deck.cir>\n"
        << "  spice_v2_cli run <deck.cir> [--json <out.json>] [--csv <out.csv>] "
           "[--raw <out.raw>] [--vcd <out.vcd>] [--touchstone <out.sNp>] "
           "[--no-audit] [--method trap|be|bdf2] [--adaptive]\n";
}

}  // namespace

int main(int argc, char** argv) {
    if (argc < 2) {
        print_usage();
        return kExitUsage;
    }

    const std::string cmd = argv[1];
    if (cmd == "version") {
        std::cout << "spice_v2_cli 0.2.0 (C++20 DAE + SparseLu + BSIM3/4 + BJT + JFET + OSDI 0.3/0.4 + M2c/M3 RF)\n";
        return kExitOk;
    }
    if (cmd == "models") {
        std::cout
            << "Built-in Primitives: R, C, L, K, V, I (DC/AC/PULSE/SIN/PWL/EXP/SFFM), E, F, G, H, B (autodiff), S (SW), T (lossless)\n"
            << "Built-in Semiconductors:\n"
            << "  D  - Diode (Shockley + depletion/transit charge)\n"
            << "  M  - MOSFET LEVEL=1 (Shichman-Hodges), LEVEL=49 (BSIM3v3 core), LEVEL=54 (BSIM4 core)\n"
            << "  Q  - BJT Gummel-Poon (NPN/PNP with Early effect, high-level injection, depletion/diffusion charge)\n"
            << "  J  - JFET Shichman-Hodges (NJF/PJF)\n"
            << "External Compiled Verilog-A:\n"
            << "  .OSDI + .MODEL <name> OSDI(lib=... model=...) -> usable from X, M, Q, J cards (OSDI 0.3/0.4 ABI)\n"
            << "Analyses: .OP, .DC (1D/2D), .AC, .TRAN, .TF, .NOISE, .SENS, .DCMATCH, .SP, .STB, .PZ, .FOUR, .FFT, .MC, .HB, .PSS, .PAC, .PXF, .PNOISE\n";
        return kExitOk;
    }

    if (cmd != "check" && cmd != "run") {
        std::cerr << "error: unknown subcommand '" << cmd << "'\n";
        print_usage();
        return kExitUsage;
    }

    if (argc < 3) {
        std::cerr << "error: missing deck path\n";
        print_usage();
        return kExitUsage;
    }

    const std::string deck_path = argv[2];
    std::string json_out_path;
    std::string csv_out_path;
    std::string raw_out_path;
    std::string vcd_out_path;
    std::string touchstone_out_path;
    bool enable_audits = true;
    bool adaptive = false;
    std::string method_override;

    for (int i = 3; i < argc; ++i) {
        const std::string arg = argv[i];
        if (arg == "--no-audit") {
            enable_audits = false;
        } else if (arg == "--adaptive") {
            adaptive = true;
        } else if (arg == "--json" && i + 1 < argc) {
            json_out_path = argv[++i];
        } else if (arg == "--csv" && i + 1 < argc) {
            csv_out_path = argv[++i];
        } else if (arg == "--raw" && i + 1 < argc) {
            raw_out_path = argv[++i];
        } else if (arg == "--vcd" && i + 1 < argc) {
            vcd_out_path = argv[++i];
        } else if (arg == "--touchstone" && i + 1 < argc) {
            touchstone_out_path = argv[++i];
        } else if (arg == "--method" && i + 1 < argc) {
            method_override = argv[++i];
            if (method_override != "trap" && method_override != "be" && method_override != "bdf2") {
                std::cerr << "error: unsupported --method '" << method_override << "'\n";
                return kExitUsage;
            }
        } else {
            std::cerr << "error: unrecognized option '" << arg << "'\n";
            print_usage();
            return kExitUsage;
        }
    }

    std::string text;
    if (!read_file(deck_path, text)) {
        std::cerr << "error: failed to read deck '" << deck_path << "'\n";
        return kExitParseOrValidate;
    }

    spice::parser::ParseOptions parse_opts{};
    parse_opts.base_dir = std::filesystem::path(deck_path).parent_path().string();

    spice::parser::ParsedDeck deck;
    if (const auto d = spice::parser::parse_spice_deck(text, deck, parse_opts); !d.ok()) {
        std::cerr << "parse error: " << d.message << "\n";
        return kExitParseOrValidate;
    }

    spice::devices::CompiledPlan plan;
    if (const auto d = spice::devices::compile_circuit(deck.circuit, plan); !d.ok()) {
        std::cerr << "compile error: " << d.message << "\n";
        return kExitParseOrValidate;
    }

    if (cmd == "check") {
        std::cout << "OK: title=\"" << deck.title << "\" nodes=" << deck.circuit.nodes.size()
                  << " elements=" << deck.circuit.elements.size()
                  << " unknowns=" << plan.unknown_count()
                  << " nnz=" << plan.pattern.nnz()
                  << " analyses=" << deck.analyses.size() << "\n";
        return kExitOk;
    }

    std::vector<spice::parser::AnalysisCommand> analyses = deck.analyses;
    if (analyses.empty()) {
        analyses.emplace_back(std::in_place_type<spice::parser::OpCommand>);
    }

    spice::analysis::AnalysisCommonOptions common{};
    common.enable_audits = enable_audits;
    if (deck.options.reltol.has_value()) {
        common.newton_options.reltol = *deck.options.reltol;
    }
    const double temp_k = deck.options.temperature_c + 273.15;

    const std::string effective_method =
        !method_override.empty() ? method_override : deck.options.method;
    auto integration_method = spice::analysis::IntegrationMethod::Trapezoidal;
    if (effective_method == "be") {
        integration_method = spice::analysis::IntegrationMethod::BackwardEuler;
    } else if (effective_method == "bdf2") {
        integration_method = spice::analysis::IntegrationMethod::Bdf2;
    }

    std::ostringstream json;
    json << std::setprecision(15);
    json << "{\n"
         << "  \"title\": \"" << json_escape(deck.title) << "\",\n"
         << "  \"unknowns\": " << plan.unknown_count() << ",\n"
         << "  \"signals\": [";
    for (std::size_t i = 0; i < plan.signals.size(); ++i) {
        if (i > 0u) json << ", ";
        json << "\"" << json_escape(plan.signals[i].id) << "\"";
    }
    json << "],\n"
         << "  \"analyses\": [\n";

    std::ostringstream csv;
    csv << std::setprecision(15);
    bool csv_written = false;

    std::optional<spice::analysis::OpResult> last_op;
    std::optional<spice::analysis::DcSweepResult> last_dc;
    std::optional<spice::analysis::AcResult> last_ac;
    std::optional<spice::analysis::TransientResult> last_tran;
    std::optional<spice::analysis::HbResult> last_hb;

    for (std::size_t aidx = 0; aidx < analyses.size(); ++aidx) {
        if (aidx > 0u) json << ",\n";
        const auto& acmd = analyses[aidx];

        if (std::holds_alternative<spice::parser::OpCommand>(acmd)) {
            spice::analysis::OpResult res;
            const auto d = spice::analysis::run_op(plan, common, res);
            if (!d.ok()) {
                std::cerr << "OP error: " << d.message << "\n";
                return (d.message.find("audit") != std::string::npos) ? kExitAuditError : kExitSolverError;
            }
            json << "    {\"kind\": \"op\", \"backend\": \"" << json_escape(res.backend_name)
                 << "\", \"values\": [";
            for (std::size_t i = 0; i < res.values.size(); ++i) {
                if (i > 0u) json << ", ";
                json << res.values[i];
            }
            json << "]}";
            if (!csv_written && !csv_out_path.empty()) {
                csv << "signal,value\n";
                for (std::size_t i = 0; i < plan.signals.size(); ++i) {
                    csv << plan.signals[i].id << "," << res.values[i] << "\n";
                }
                csv_written = true;
            }
            last_op = std::move(res);
        } else if (const auto* dc = std::get_if<spice::parser::DcCommand>(&acmd)) {
            std::size_t total_points = 0;
            if (dc->source2_id.has_value()) {
                const double dir2 = (dc->stop2 >= dc->start2) ? 1.0 : -1.0;
                const double s2 = std::abs(dc->step2) * dir2;
                if (s2 == 0.0) {
                    std::cerr << "DC error: secondary sweep step cannot be zero\n";
                    return kExitParseOrValidate;
                }
                for (double v2 = dc->start2; (dir2 > 0.0 ? v2 <= dc->stop2 + 1e-12 : v2 >= dc->stop2 - 1e-12); v2 += s2) {
                    if (auto d = plan.set_dc_source_override(*dc->source2_id, v2); !d.ok()) {
                        std::cerr << "DC error: " << d.message << "\n";
                        return kExitSolverError;
                    }
                    spice::analysis::DcSweepOptions opts{};
                    opts.source_id = dc->source_id;
                    opts.start = dc->start;
                    opts.stop = dc->stop;
                    opts.step = dc->step;
                    opts.common = common;
                    spice::analysis::DcSweepResult res;
                    if (auto d = spice::analysis::run_dc_sweep(plan, opts, res); !d.ok()) {
                        (void)plan.set_dc_source_override(*dc->source2_id, std::nullopt);
                        std::cerr << "DC error: " << d.message << "\n";
                        return kExitSolverError;
                    }
                    total_points += res.points.size();
                    last_dc = std::move(res);
                }
                (void)plan.set_dc_source_override(*dc->source2_id, std::nullopt);
            } else {
                spice::analysis::DcSweepOptions opts{};
                opts.source_id = dc->source_id;
                opts.start = dc->start;
                opts.stop = dc->stop;
                opts.step = dc->step;
                opts.common = common;
                spice::analysis::DcSweepResult res;
                if (auto d = spice::analysis::run_dc_sweep(plan, opts, res); !d.ok()) {
                    std::cerr << "DC error: " << d.message << "\n";
                    return kExitSolverError;
                }
                total_points = res.points.size();
                if (!csv_written && !csv_out_path.empty()) {
                    csv << "sweep";
                    for (const auto& sig : plan.signals) csv << "," << sig.id;
                    csv << "\n";
                    for (const auto& pt : res.points) {
                        csv << pt.sweep_value;
                        for (double v : pt.values) csv << "," << v;
                        csv << "\n";
                    }
                    csv_written = true;
                }
                last_dc = std::move(res);
            }
            json << "    {\"kind\": \"dc\", \"source\": \"" << json_escape(dc->source_id)
                 << "\", \"points\": " << total_points << "}";
        } else if (const auto* ac = std::get_if<spice::parser::AcCommand>(&acmd)) {
            spice::analysis::AcOptions opts{};
            opts.frequencies_hz = ac->frequencies_hz;
            opts.common = common;
            spice::analysis::AcResult res;
            const auto d = spice::analysis::run_ac(plan, opts, res);
            if (!d.ok()) {
                std::cerr << "AC error: " << d.message << "\n";
                return (d.message.find("audit") != std::string::npos) ? kExitAuditError : kExitSolverError;
            }
            json << "    {\"kind\": \"ac\", \"points\": " << res.points.size() << "}";
            if (!csv_written && !csv_out_path.empty()) {
                csv << "freq_hz";
                for (const auto& sig : plan.signals) csv << "," << sig.id << "_re," << sig.id << "_im";
                csv << "\n";
                for (const auto& pt : res.points) {
                    csv << pt.frequency_hz;
                    for (const auto& v : pt.values) csv << "," << v.real() << "," << v.imag();
                    csv << "\n";
                }
                csv_written = true;
            }
            last_ac = std::move(res);
        } else if (const auto* tr = std::get_if<spice::parser::TranCommand>(&acmd)) {
            spice::analysis::TransientOptions opts{};
            opts.step_s = tr->step_s;
            opts.stop_s = tr->stop_s;
            opts.max_step_s = tr->max_step_s;
            opts.method = integration_method;
            opts.startup = tr->uic ? spice::analysis::StartupMode::Uic
                                   : spice::analysis::StartupMode::OperatingPoint;
            opts.adaptive = adaptive;
            opts.common = common;
            spice::analysis::TransientResult res;
            const auto d = spice::analysis::run_transient(plan, opts, res);
            if (!d.ok()) {
                std::cerr << "TRAN error: " << d.message << "\n";
                return (d.message.find("audit") != std::string::npos) ? kExitAuditError : kExitSolverError;
            }
            json << "    {\"kind\": \"tran\", \"samples\": " << res.samples.size()
                 << ", \"accepted_steps\": " << res.accepted_steps
                 << ", \"rejected_steps\": " << res.rejected_steps << "}";
            if (!raw_out_path.empty()) {
                std::ofstream rofs(raw_out_path);
                if (!rofs) {
                    std::cerr << "error: failed to write RAW output '" << raw_out_path << "'\n";
                    return kExitUsage;
                }
                rofs << spice::analysis::format_spice_raw_ascii(plan, res);
            }
            if (!vcd_out_path.empty()) {
                std::ofstream vofs(vcd_out_path);
                if (!vofs) {
                    std::cerr << "error: failed to write VCD output '" << vcd_out_path << "'\n";
                    return kExitUsage;
                }
                vofs << spice::analysis::format_vcd(plan, res);
            }
            if (!csv_written && !csv_out_path.empty()) {
                csv << "time_s";
                for (const auto& sig : plan.signals) csv << "," << sig.id;
                csv << "\n";
                for (const auto& s : res.samples) {
                    csv << s.time_s;
                    for (double v : s.values) csv << "," << v;
                    csv << "\n";
                }
                csv_written = true;
            }
            last_tran = std::move(res);
        } else if (const auto* tf = std::get_if<spice::parser::TfCommand>(&acmd)) {
            spice::analysis::TfOptions opts{};
            opts.out_p = tf->out_p;
            opts.out_n = tf->out_n;
            opts.input_source_id = tf->input_source_id;
            opts.common = common;
            spice::analysis::TfResult res;
            if (auto d = spice::analysis::run_tf(plan, opts, res); !d.ok()) {
                std::cerr << "TF error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"tf\", \"gain\": " << res.transfer_gain
                 << ", \"rin\": " << res.input_resistance
                 << ", \"rout\": " << res.output_resistance << "}";
        } else if (const auto* ns = std::get_if<spice::parser::NoiseCommand>(&acmd)) {
            spice::analysis::NoiseOptions opts{};
            opts.out_p = ns->out_p;
            opts.out_n = ns->out_n;
            opts.input_source_id = ns->input_source_id;
            opts.frequencies_hz = ns->frequencies_hz;
            opts.temperature_k = temp_k;
            opts.common = common;
            spice::analysis::NoiseResult res;
            if (auto d = spice::analysis::run_noise(plan, opts, res); !d.ok()) {
                std::cerr << "NOISE error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"noise\", \"points\": " << res.points.size()
                 << ", \"onoise_rms\": " << res.integrated_onoise_vrms
                 << ", \"inoise_rms\": " << res.integrated_inoise_vrms << "}";
        } else if (const auto* sn = std::get_if<spice::parser::SensCommand>(&acmd)) {
            spice::analysis::SensOptions opts{};
            opts.out_p = sn->out_p;
            opts.out_n = sn->out_n;
            opts.common = common;
            spice::analysis::SensResult res;
            if (auto d = spice::analysis::run_sens(plan, opts, res); !d.ok()) {
                std::cerr << "SENS error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"sens\", \"nominal_v\": " << res.nominal_output_volts
                 << ", \"entries\": " << res.entries.size() << "}";
        } else if (const auto* dm = std::get_if<spice::parser::DcMatchCommand>(&acmd)) {
            spice::analysis::DcMatchOptions opts{};
            opts.out_p = dm->out_p;
            opts.out_n = dm->out_n;
            opts.common = common;
            for (const auto& elem : deck.circuit.elements) {
                if (const auto* r = std::get_if<spice::core::ResistorSpec>(&elem)) {
                    opts.mismatches.push_back(spice::analysis::ElementTolerance{
                        r->id, dm->resistor_rel_sigma, spice::analysis::ToleranceDistribution::Gaussian});
                }
            }
            spice::analysis::DcMatchResult res;
            if (auto d = spice::analysis::run_dcmatch(plan, opts, res); !d.ok()) {
                std::cerr << "DCMATCH error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"dcmatch\", \"nominal_v\": " << res.nominal_output_volts
                 << ", \"sigma_v\": " << res.total_sigma_volts << "}";
        } else if (const auto* sp = std::get_if<spice::parser::SpCommand>(&acmd)) {
            spice::analysis::SpOptions opts{};
            opts.frequencies_hz = sp->frequencies_hz;
            opts.common = common;
            for (const auto& pt : sp->ports) {
                opts.ports.push_back(spice::analysis::PortSpec{pt.p, pt.n, pt.z0_ohms});
            }
            spice::analysis::SpResult res;
            if (auto d = spice::analysis::run_sp(plan, opts, res); !d.ok()) {
                std::cerr << "SP error: " << d.message << "\n";
                return kExitSolverError;
            }
            if (!touchstone_out_path.empty()) {
                std::ofstream tofs(touchstone_out_path);
                if (!tofs) {
                    std::cerr << "error: failed to write Touchstone output '" << touchstone_out_path << "'\n";
                    return kExitUsage;
                }
                tofs << spice::analysis::format_touchstone(res);
            }
            json << "    {\"kind\": \"sp\", \"ports\": " << res.port_count
                 << ", \"points\": " << res.points.size() << "}";
        } else if (const auto* stb = std::get_if<spice::parser::StbCommand>(&acmd)) {
            spice::analysis::StbOptions opts{};
            opts.probe_source_id = stb->probe_source_id;
            opts.frequencies_hz = stb->frequencies_hz;
            opts.common = common;
            spice::analysis::StbResult res;
            if (auto d = spice::analysis::run_stb(plan, opts, res); !d.ok()) {
                std::cerr << "STB error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"stb\", \"points\": " << res.points.size();
            if (res.phase_margin_deg.has_value()) json << ", \"phase_margin_deg\": " << *res.phase_margin_deg;
            if (res.gain_margin_db.has_value()) json << ", \"gain_margin_db\": " << *res.gain_margin_db;
            json << "}";
        } else if (const auto* pz = std::get_if<spice::parser::PzCommand>(&acmd)) {
            spice::analysis::PzOptions opts{};
            opts.out_p = pz->out_p;
            opts.out_n = pz->out_n;
            opts.input_source_id = find_pz_input_source(deck.circuit, pz->in_p, pz->in_n);
            opts.common = common;
            spice::analysis::PzResult res;
            if (auto d = spice::analysis::run_pz(plan, opts, res); !d.ok()) {
                std::cerr << "PZ error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"pz\", \"poles\": " << res.poles_rad_per_s.size()
                 << ", \"zeros\": " << res.zeros_rad_per_s.size() << "}";
        } else if (const auto* four = std::get_if<spice::parser::FourCommand>(&acmd)) {
            if (!last_tran.has_value()) {
                std::cerr << "FOUR error: .FOUR requires a preceding .TRAN analysis in the deck\n";
                return kExitParseOrValidate;
            }
            const auto sig_idx = find_signal_index(plan, four->signal_id);
            if (!sig_idx.has_value()) {
                std::cerr << "FOUR error: unknown signal '" << four->signal_id << "'\n";
                return kExitParseOrValidate;
            }
            spice::analysis::FourierResult res;
            if (auto d = spice::analysis::compute_fourier(*last_tran, *sig_idx, four->fundamental_hz, four->harmonics, res);
                !d.ok()) {
                std::cerr << "FOUR error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"four\", \"dc\": " << res.dc_component
                 << ", \"thd_percent\": " << res.thd_percent << "}";
        } else if (const auto* fft = std::get_if<spice::parser::FftCommand>(&acmd)) {
            if (!last_tran.has_value()) {
                std::cerr << "FFT error: .FFT requires a preceding .TRAN analysis in the deck\n";
                return kExitParseOrValidate;
            }
            const auto sig_idx = find_signal_index(plan, fft->signal_id);
            if (!sig_idx.has_value()) {
                std::cerr << "FFT error: unknown signal '" << fft->signal_id << "'\n";
                return kExitParseOrValidate;
            }
            spice::analysis::FftResult res;
            if (auto d = spice::analysis::compute_fft(*last_tran, *sig_idx, fft->n_points, true, res); !d.ok()) {
                std::cerr << "FFT error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"fft\", \"bins\": " << res.frequencies_hz.size() << "}";
        } else if (const auto* mc = std::get_if<spice::parser::McCommand>(&acmd)) {
            spice::analysis::MonteCarloOptions opts{};
            opts.trials = mc->runs;
            opts.seed = mc->seed;
            opts.common = common;
            for (const auto& elem : deck.circuit.elements) {
                if (const auto* r = std::get_if<spice::core::ResistorSpec>(&elem)) {
                    opts.tolerances.push_back(spice::analysis::ElementTolerance{
                        r->id, mc->resistor_rel_sigma, spice::analysis::ToleranceDistribution::Gaussian});
                }
            }
            spice::analysis::MonteCarloResult res;
            if (auto d = spice::analysis::run_monte_carlo_op(deck.circuit, opts, res); !d.ok()) {
                std::cerr << "MC error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"mc\", \"trials\": " << res.completed_trials
                 << ", \"seed\": " << res.seed << "}";
        } else if (const auto* hb = std::get_if<spice::parser::HbCommand>(&acmd)) {
            spice::analysis::HbOptions opts{};
            opts.fundamental_hz = hb->fundamental_hz;
            opts.harmonics = hb->harmonics;
            opts.common = common;
            spice::analysis::HbResult res;
            if (auto d = spice::analysis::run_hb(plan, opts, res); !d.ok()) {
                std::cerr << "HB error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"hb\", \"fundamental_hz\": " << res.fundamental_hz
                 << ", \"harmonics\": " << res.harmonics
                 << ", \"iterations\": " << res.iterations << "}";
            last_hb = std::move(res);
        } else if (const auto* pss = std::get_if<spice::parser::PssCommand>(&acmd)) {
            spice::analysis::PssOptions opts{};
            opts.fundamental_hz = pss->fundamental_hz;
            opts.steps_per_period = pss->steps_per_period;
            opts.method = integration_method;
            opts.common = common;
            spice::analysis::PssResult res;
            if (auto d = spice::analysis::run_pss(plan, opts, res); !d.ok()) {
                std::cerr << "PSS error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"pss\", \"fundamental_hz\": " << res.fundamental_hz
                 << ", \"iterations\": " << res.shooting_iterations
                 << ", \"periodicity_error\": " << res.periodicity_error << "}";
        } else if (const auto* pac = std::get_if<spice::parser::PacCommand>(&acmd)) {
            if (!last_hb.has_value()) {
                std::cerr << "PAC error: .PAC requires a preceding .HB analysis in the deck\n";
                return kExitParseOrValidate;
            }
            spice::analysis::PacOptions opts{};
            opts.small_signal_freq_hz = pac->small_signal_freq_hz;
            opts.input_source_id = pac->input_source_id;
            opts.common = common;
            spice::analysis::PacResult res;
            if (auto d = spice::analysis::run_pac(plan, *last_hb, opts, res); !d.ok()) {
                std::cerr << "PAC error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"pac\", \"sidebands\": " << res.sidebands.size() << "}";
        } else if (const auto* pxf = std::get_if<spice::parser::PxfCommand>(&acmd)) {
            if (!last_hb.has_value()) {
                std::cerr << "PXF error: .PXF requires a preceding .HB analysis in the deck\n";
                return kExitParseOrValidate;
            }
            spice::analysis::PxfOptions opts{};
            opts.small_signal_freq_hz = pxf->small_signal_freq_hz;
            opts.out_p = pxf->out_p;
            opts.out_n = pxf->out_n;
            opts.output_sideband = pxf->output_sideband;
            opts.common = common;
            spice::analysis::PxfResult res;
            if (auto d = spice::analysis::run_pxf(plan, *last_hb, opts, res); !d.ok()) {
                std::cerr << "PXF error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"pxf\", \"output_freq_hz\": " << res.output_frequency_hz
                 << ", \"transfers\": " << res.transfers.size() << "}";
        } else if (const auto* pn = std::get_if<spice::parser::PnoiseCommand>(&acmd)) {
            if (!last_hb.has_value()) {
                std::cerr << "PNOISE error: .PNOISE requires a preceding .HB analysis in the deck\n";
                return kExitParseOrValidate;
            }
            spice::analysis::PnoiseOptions opts{};
            opts.small_signal_freq_hz = pn->small_signal_freq_hz;
            opts.out_p = pn->out_p;
            opts.out_n = pn->out_n;
            opts.output_sideband = pn->output_sideband;
            opts.temperature_k = temp_k;
            opts.common = common;
            spice::analysis::PnoiseResult res;
            if (auto d = spice::analysis::run_pnoise(plan, *last_hb, opts, res); !d.ok()) {
                std::cerr << "PNOISE error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"pnoise\", \"onoise_v_per_sqrt_hz\": " << res.onoise_v_per_sqrt_hz << "}";
        }
    }

    json << "\n  ]";

    /* Evaluate .MEAS declarative measurements if present. */
    if (!deck.measurements.empty()) {
        json << ",\n  \"measurements\": {";
        bool first_m = true;
        for (const auto& m : deck.measurements) {
            const auto sig_idx = find_signal_index(plan, m.signal);
            if (!sig_idx.has_value()) continue;
            std::optional<double> val;

            auto eval_series = [&](const std::vector<double>& xs, const std::vector<double>& ys) {
                if (ys.empty()) return;
                if (m.func == "max") {
                    val = *std::max_element(ys.begin(), ys.end());
                } else if (m.func == "min") {
                    val = *std::min_element(ys.begin(), ys.end());
                } else if (m.func == "pp") {
                    const auto [mn, mx] = std::minmax_element(ys.begin(), ys.end());
                    val = *mx - *mn;
                } else if (m.func == "avg") {
                    double sum = 0.0;
                    for (double y : ys) sum += y;
                    val = sum / static_cast<double>(ys.size());
                } else if (m.func == "find" && m.at_x.has_value()) {
                    double best_d = std::numeric_limits<double>::infinity();
                    double best_y = ys.front();
                    for (std::size_t i = 0; i < xs.size(); ++i) {
                        const double d = std::abs(xs[i] - *m.at_x);
                        if (d < best_d) {
                            best_d = d;
                            best_y = ys[i];
                        }
                    }
                    val = best_y;
                }
            };

            if (m.domain == "tran" && last_tran.has_value()) {
                std::vector<double> xs, ys;
                xs.reserve(last_tran->samples.size());
                ys.reserve(last_tran->samples.size());
                for (const auto& s : last_tran->samples) {
                    xs.push_back(s.time_s);
                    ys.push_back(s.values[*sig_idx]);
                }
                eval_series(xs, ys);
            } else if (m.domain == "dc" && last_dc.has_value()) {
                std::vector<double> xs, ys;
                for (const auto& p : last_dc->points) {
                    xs.push_back(p.sweep_value);
                    ys.push_back(p.values[*sig_idx]);
                }
                eval_series(xs, ys);
            } else if (m.domain == "ac" && last_ac.has_value()) {
                std::vector<double> xs, ys;
                for (const auto& p : last_ac->points) {
                    xs.push_back(p.frequency_hz);
                    ys.push_back(std::abs(p.values[*sig_idx]));
                }
                eval_series(xs, ys);
            } else if (m.domain == "op" && last_op.has_value()) {
                val = last_op->values[*sig_idx];
            }

            if (val.has_value()) {
                if (!first_m) json << ", ";
                json << "\"" << json_escape(m.name) << "\": " << *val;
                first_m = false;
            }
        }
        json << "}";
    }
    json << "\n}\n";

    if (!json_out_path.empty()) {
        std::ofstream ofs(json_out_path);
        if (!ofs) {
            std::cerr << "error: failed to write JSON output '" << json_out_path << "'\n";
            return kExitUsage;
        }
        ofs << json.str();
    } else {
        std::cout << json.str();
    }

    if (!csv_out_path.empty() && csv_written) {
        std::ofstream ofs(csv_out_path);
        if (!ofs) {
            std::cerr << "error: failed to write CSV output '" << csv_out_path << "'\n";
            return kExitUsage;
        }
        ofs << csv.str();
    }

    return kExitOk;
}
