#include "analysis/analyses.hpp"
#include "devices/linear_devices.hpp"
#include "parser/spice_parser.hpp"

#include <cmath>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

namespace {

constexpr int kExitOk = 0;
constexpr int kExitUsage = 2;
constexpr int kExitParseOrValidate = 3;
constexpr int kExitSolverError = 4;
constexpr int kExitAuditError = 5;

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

void print_usage() {
    std::cerr
        << "Usage:\n"
        << "  spice_v2_cli version\n"
        << "  spice_v2_cli check <deck.cir>\n"
        << "  spice_v2_cli run <deck.cir> [--json <out.json>] [--csv <out.csv>] "
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
        std::cout << "spice_v2_cli 0.1.0 (C++20 DAE + SparseLu + OSDI 0.3/0.4 + SPICE3f5)\n";
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

    spice::parser::ParsedDeck deck;
    if (const auto d = spice::parser::parse_spice_deck(text, deck); !d.ok()) {
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

    /* Execute requested analyses (default to .OP if deck specifies none). */
    std::vector<spice::parser::AnalysisCommand> analyses = deck.analyses;
    if (analyses.empty()) {
        analyses.emplace_back(std::in_place_type<spice::parser::OpCommand>);
    }

    spice::analysis::AnalysisCommonOptions common{};
    common.enable_audits = enable_audits;
    if (deck.options.reltol.has_value()) {
        common.newton_options.reltol = *deck.options.reltol;
    }

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
        } else if (const auto* dc = std::get_if<spice::parser::DcCommand>(&acmd)) {
            spice::analysis::DcSweepOptions opts{};
            opts.source_id = dc->source_id;
            opts.start = dc->start;
            opts.stop = dc->stop;
            opts.step = dc->step;
            opts.common = common;
            spice::analysis::DcSweepResult res;
            const auto d = spice::analysis::run_dc_sweep(plan, opts, res);
            if (!d.ok()) {
                std::cerr << "DC error: " << d.message << "\n";
                return kExitSolverError;
            }
            json << "    {\"kind\": \"dc\", \"source\": \"" << json_escape(dc->source_id)
                 << "\", \"points\": " << res.points.size() << "}";
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
        }
    }
    json << "\n  ]\n}\n";

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
