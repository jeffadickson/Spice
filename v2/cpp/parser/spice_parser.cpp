#include "parser/spice_parser.hpp"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <complex>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <limits>
#include <numbers>
#include <sstream>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace spice::parser {

namespace {

using core::Circuit;
using core::Diagnostic;
using core::NodeId;
using core::StatusCode;

struct LogicalLine {
    std::size_t line_no{0};
    std::string text;
};

std::string to_lower(std::string_view sv) {
    std::string out;
    out.reserve(sv.size());
    for (char c : sv) {
        out.push_back(static_cast<char>(std::tolower(static_cast<unsigned char>(c))));
    }
    return out;
}

std::string trim(std::string_view sv) {
    std::size_t a = 0;
    while (a < sv.size() && std::isspace(static_cast<unsigned char>(sv[a]))) ++a;
    std::size_t b = sv.size();
    while (b > a && std::isspace(static_cast<unsigned char>(sv[b - 1u]))) --b;
    return std::string(sv.substr(a, b - a));
}

Diagnostic line_err(std::size_t line_no, StatusCode code, const std::string& msg) {
    return Diagnostic::error(code, "line " + std::to_string(line_no) + ": " + msg);
}

std::string strip_inline_comment(const std::string& line) {
    bool in_quote = false;
    bool in_brace = false;
    for (std::size_t i = 0; i < line.size(); ++i) {
        const char c = line[i];
        if (c == '"' || c == '\'') in_quote = !in_quote;
        else if (!in_quote && c == '{') in_brace = true;
        else if (!in_quote && c == '}') in_brace = false;
        else if (!in_quote && !in_brace && c == ';') return line.substr(0, i);
    }
    return line;
}

/* Preprocess raw SPICE text into logical lines (handling + continuations and * comments). */
void preprocess_lines(const std::string& text, std::string& out_title, std::vector<LogicalLine>& out_lines) {
    out_lines.clear();
    out_title.clear();

    std::istringstream iss(text);
    std::string raw;
    std::size_t line_no = 0;
    bool title_consumed = false;
    while (std::getline(iss, raw)) {
        ++line_no;
        if (!raw.empty() && raw.back() == '\r') raw.pop_back();
        const std::string trimmed = trim(raw);

        if (!title_consumed) {
            title_consumed = true;
            if (!trimmed.empty() && trimmed[0] != '.' && trimmed[0] != '*') {
                out_title = trimmed;
                continue;
            }
            if (trimmed.empty()) {
                continue;
            }
        }

        if (trimmed.empty() || trimmed[0] == '*') continue;
        const std::string no_comment = trim(strip_inline_comment(trimmed));
        if (no_comment.empty()) continue;

        if (no_comment[0] == '+' && !out_lines.empty()) {
            out_lines.back().text.push_back(' ');
            out_lines.back().text += trim(no_comment.substr(1u));
        } else {
            out_lines.push_back(LogicalLine{line_no, no_comment});
        }
    }
}

/*
 * Tokenize a SPICE line while keeping braced `{expr}`, quoted `"..."`, and
 * parenthesized `( ... )` groups intact when needed.
 */
std::vector<std::string> tokenize_spice_line(const std::string& line) {
    std::vector<std::string> tokens;
    std::size_t i = 0;
    const std::size_t n = line.size();
    while (i < n) {
        while (i < n && (std::isspace(static_cast<unsigned char>(line[i])) || line[i] == ',')) ++i;
        if (i >= n) break;

        std::string tok;
        int paren_depth = 0;
        int brace_depth = 0;
        bool in_quote = false;
        while (i < n) {
            const char c = line[i];
            if (in_quote) {
                tok.push_back(c);
                if (c == '"') in_quote = false;
                ++i;
                continue;
            }
            if (c == '"') {
                in_quote = true;
                tok.push_back(c);
                ++i;
                continue;
            }
            if (c == '{') {
                ++brace_depth;
                tok.push_back(c);
                ++i;
                continue;
            }
            if (c == '}') {
                if (brace_depth > 0) --brace_depth;
                tok.push_back(c);
                ++i;
                continue;
            }
            if (c == '(') {
                ++paren_depth;
                tok.push_back(c);
                ++i;
                continue;
            }
            if (c == ')') {
                if (paren_depth > 0) --paren_depth;
                tok.push_back(c);
                ++i;
                continue;
            }
            if (paren_depth == 0 && brace_depth == 0 &&
                (std::isspace(static_cast<unsigned char>(c)) || c == ',')) {
                break;
            }
            tok.push_back(c);
            ++i;
        }
        if (!tok.empty()) tokens.push_back(std::move(tok));
    }
    return tokens;
}

std::string unquote(std::string_view sv) {
    if (sv.size() >= 2u && ((sv.front() == '"' && sv.back() == '"') ||
                            (sv.front() == '\'' && sv.back() == '\''))) {
        return std::string(sv.substr(1u, sv.size() - 2u));
    }
    return std::string(sv);
}

/* Parse a SPICE number with optional engineering suffix (f, p, n, u, m, k, meg, g, t). */
bool parse_spice_number_prefix(std::string_view sv, std::size_t& pos, double& out_val) {
    const std::size_t start = pos;
    const std::size_t n = sv.size();
    bool saw_digit = false;
    while (pos < n && std::isdigit(static_cast<unsigned char>(sv[pos]))) {
        saw_digit = true;
        ++pos;
    }
    if (pos < n && sv[pos] == '.') {
        ++pos;
        while (pos < n && std::isdigit(static_cast<unsigned char>(sv[pos]))) {
            saw_digit = true;
            ++pos;
        }
    }
    if (!saw_digit) {
        pos = start;
        return false;
    }
    if (pos < n && (sv[pos] == 'e' || sv[pos] == 'E')) {
        const std::size_t exp_pos = pos;
        ++pos;
        if (pos < n && (sv[pos] == '+' || sv[pos] == '-')) ++pos;
        std::size_t dstart = pos;
        while (pos < n && std::isdigit(static_cast<unsigned char>(sv[pos]))) ++pos;
        if (pos == dstart) pos = exp_pos;
    }

    const std::string num_str(sv.substr(start, pos - start));
    char* endptr = nullptr;
    double val = std::strtod(num_str.c_str(), &endptr);
    if (endptr == num_str.c_str() || !std::isfinite(val)) {
        pos = start;
        return false;
    }

    /* Optional engineering suffix (only alphabetic characters immediately following). */
    const std::size_t suf_start = pos;
    while (pos < n && std::isalpha(static_cast<unsigned char>(sv[pos]))) ++pos;
    if (pos > suf_start) {
        const std::string suf = to_lower(sv.substr(suf_start, pos - suf_start));
        double mult = 1.0;
        if (suf.rfind("meg", 0) == 0) mult = 1.0e6;
        else if (suf.rfind("mil", 0) == 0) mult = 25.4e-6;
        else if (suf[0] == 'f') mult = 1.0e-15;
        else if (suf[0] == 'p') mult = 1.0e-12;
        else if (suf[0] == 'n') mult = 1.0e-9;
        else if (suf[0] == 'u') mult = 1.0e-6;
        else if (suf[0] == 'm') mult = 1.0e-3;
        else if (suf[0] == 'k') mult = 1.0e3;
        else if (suf[0] == 'g') mult = 1.0e9;
        else if (suf[0] == 't') mult = 1.0e12;
        else {
            pos = start;
            return false;
        }
        val *= mult;
    }
    out_val = val;
    return std::isfinite(out_val);
}

using ParamMap = std::unordered_map<std::string, double>;

/* Compile-time constant expression evaluator for .PARAM and {expr} values. */
class ParamExprParser {
public:
    ParamExprParser(std::string_view src, const ParamMap& params) : src_(src), params_(params) {}

    bool parse(double& out_val, std::string& out_err) {
        skip_ws();
        if (!parse_add(out_val, out_err)) return false;
        skip_ws();
        if (pos_ != src_.size()) {
            out_err = "unexpected trailing characters '" + std::string(src_.substr(pos_)) + "'";
            return false;
        }
        if (!std::isfinite(out_val)) {
            out_err = "parameter expression evaluated to non-finite value";
            return false;
        }
        return true;
    }

private:
    void skip_ws() {
        while (pos_ < src_.size() && std::isspace(static_cast<unsigned char>(src_[pos_]))) ++pos_;
    }

    bool parse_add(double& out, std::string& err) {
        if (!parse_mul(out, err)) return false;
        for (;;) {
            skip_ws();
            if (pos_ < src_.size() && (src_[pos_] == '+' || src_[pos_] == '-')) {
                const char op = src_[pos_++];
                double rhs = 0.0;
                if (!parse_mul(rhs, err)) return false;
                out = (op == '+') ? (out + rhs) : (out - rhs);
            } else {
                break;
            }
        }
        return true;
    }

    bool parse_mul(double& out, std::string& err) {
        if (!parse_unary(out, err)) return false;
        for (;;) {
            skip_ws();
            if (pos_ < src_.size() && (src_[pos_] == '*' || src_[pos_] == '/')) {
                if (src_[pos_] == '*' && pos_ + 1u < src_.size() && src_[pos_ + 1u] == '*') {
                    break; /* Handle ** in pow. */
                }
                const char op = src_[pos_++];
                double rhs = 0.0;
                if (!parse_unary(rhs, err)) return false;
                if (op == '/') {
                    if (rhs == 0.0) {
                        err = "division by zero in parameter expression";
                        return false;
                    }
                    out /= rhs;
                } else {
                    out *= rhs;
                }
            } else {
                break;
            }
        }
        return true;
    }

    bool parse_unary(double& out, std::string& err) {
        skip_ws();
        if (pos_ < src_.size() && src_[pos_] == '+') {
            ++pos_;
            return parse_unary(out, err);
        }
        if (pos_ < src_.size() && src_[pos_] == '-') {
            ++pos_;
            if (!parse_unary(out, err)) return false;
            out = -out;
            return true;
        }
        return parse_pow(out, err);
    }

    bool parse_pow(double& out, std::string& err) {
        if (!parse_primary(out, err)) return false;
        skip_ws();
        if (pos_ < src_.size() && (src_[pos_] == '^' ||
                                   (src_[pos_] == '*' && pos_ + 1u < src_.size() && src_[pos_ + 1u] == '*'))) {
            pos_ += (src_[pos_] == '^') ? 1u : 2u;
            double exp_v = 0.0;
            if (!parse_unary(exp_v, err)) return false;
            out = std::pow(out, exp_v);
            if (!std::isfinite(out)) {
                err = "invalid power operation in parameter expression";
                return false;
            }
        }
        return true;
    }

    bool parse_primary(double& out, std::string& err) {
        skip_ws();
        if (pos_ >= src_.size()) {
            err = "unexpected end of parameter expression";
            return false;
        }
        if (src_[pos_] == '(') {
            ++pos_;
            if (!parse_add(out, err)) return false;
            skip_ws();
            if (pos_ >= src_.size() || src_[pos_] != ')') {
                err = "missing closing ')' in parameter expression";
                return false;
            }
            ++pos_;
            return true;
        }
        if (std::isdigit(static_cast<unsigned char>(src_[pos_])) || src_[pos_] == '.') {
            if (!parse_spice_number_prefix(src_, pos_, out)) {
                err = "invalid numeric literal or suffix in parameter expression";
                return false;
            }
            return true;
        }
        if (std::isalpha(static_cast<unsigned char>(src_[pos_])) || src_[pos_] == '_') {
            const std::size_t start = pos_;
            while (pos_ < src_.size() &&
                   (std::isalnum(static_cast<unsigned char>(src_[pos_])) || src_[pos_] == '_')) {
                ++pos_;
            }
            const std::string ident = to_lower(src_.substr(start, pos_ - start));
            skip_ws();
            if (pos_ < src_.size() && src_[pos_] == '(') {
                ++pos_;
                std::vector<double> args;
                skip_ws();
                if (pos_ < src_.size() && src_[pos_] != ')') {
                    for (;;) {
                        double a = 0.0;
                        if (!parse_add(a, err)) return false;
                        args.push_back(a);
                        skip_ws();
                        if (pos_ < src_.size() && src_[pos_] == ',') {
                            ++pos_;
                            continue;
                        }
                        break;
                    }
                }
                if (pos_ >= src_.size() || src_[pos_] != ')') {
                    err = "missing ')' after function '" + ident + "'";
                    return false;
                }
                ++pos_;
                return eval_func(ident, args, out, err);
            }
            if (ident == "pi") {
                out = std::numbers::pi;
                return true;
            }
            if (ident == "e") {
                out = std::numbers::e;
                return true;
            }
            const auto it = params_.find(ident);
            if (it == params_.end()) {
                err = "unresolved parameter '" + ident + "'";
                return false;
            }
            out = it->second;
            return true;
        }
        err = std::string("unexpected character '") + src_[pos_] + "' in parameter expression";
        return false;
    }

    static bool eval_func(const std::string& fn, const std::vector<double>& a, double& out, std::string& err) {
        if (a.size() == 1u) {
            if (fn == "sin") { out = std::sin(a[0]); return true; }
            if (fn == "cos") { out = std::cos(a[0]); return true; }
            if (fn == "tan") { out = std::tan(a[0]); return true; }
            if (fn == "exp") { out = std::exp(a[0]); return true; }
            if (fn == "log" || fn == "ln") {
                if (a[0] <= 0.0) { err = "log domain error"; return false; }
                out = std::log(a[0]);
                return true;
            }
            if (fn == "log10") {
                if (a[0] <= 0.0) { err = "log10 domain error"; return false; }
                out = std::log10(a[0]);
                return true;
            }
            if (fn == "sqrt") {
                if (a[0] < 0.0) { err = "sqrt domain error"; return false; }
                out = std::sqrt(a[0]);
                return true;
            }
            if (fn == "abs") { out = std::abs(a[0]); return true; }
        } else if (a.size() == 2u) {
            if (fn == "pow") { out = std::pow(a[0], a[1]); return std::isfinite(out); }
            if (fn == "min") { out = std::min(a[0], a[1]); return true; }
            if (fn == "max") { out = std::max(a[0], a[1]); return true; }
        }
        err = "unknown or wrong-arity function '" + fn + "' in parameter expression";
        return false;
    }

    std::string_view src_;
    const ParamMap& params_;
    std::size_t pos_{0};
};

Diagnostic eval_value(
    std::size_t line_no,
    const std::string& token,
    const ParamMap& params,
    double& out_val) {
    std::string_view sv(token);
    if (sv.size() >= 2u && sv.front() == '{' && sv.back() == '}') {
        sv = sv.substr(1u, sv.size() - 2u);
    }
    ParamExprParser p(sv, params);
    std::string err;
    if (!p.parse(out_val, err)) {
        return line_err(line_no, StatusCode::InvalidArgument, err + " in '" + token + "'");
    }
    return Diagnostic::success();
}

struct ModelDef {
    enum class Kind { Diode, Switch, Mosfet, Bjt, Jfet, Osdi };
    Kind kind{Kind::Diode};
    core::DiodeSpec diode_template{};
    core::SwitchSpec switch_template{};
    core::MosfetSpec mosfet_template{};
    core::BjtSpec bjt_template{};
    core::JfetSpec jfet_template{};
    std::string osdi_library_path;
    std::string osdi_model_name;
    std::vector<std::pair<std::string, double>> osdi_model_params;
};

struct SubcktDef {
    std::string name;
    std::vector<std::string> ports;
    std::vector<std::pair<std::string, std::string>> default_params;
    std::vector<LogicalLine> body;
};

struct ElaboratorState {
    Circuit circuit;
    std::vector<AnalysisCommand> analyses;
    std::vector<MeasCommand> measurements;
    DeckOptions options;
    ParamMap global_params;
    std::unordered_set<std::string> global_nodes;
    std::unordered_map<std::string, ModelDef> models;
    std::unordered_map<std::string, SubcktDef> subckts;
    std::vector<std::string> registered_osdi_libs;
    ParseOptions parse_opts;
    std::vector<std::string> subckt_stack;
    std::vector<std::string> include_stack;

    NodeId resolve_node(
        const std::string& raw_node,
        const std::string& scope_prefix,
        const std::unordered_map<std::string, NodeId>& port_map) {
        const std::string low = to_lower(raw_node);
        if (low == "0" || low == "gnd") return NodeId{0};
        if (global_nodes.find(low) != global_nodes.end()) {
            return circuit.get_or_add_node(low);
        }
        if (!port_map.empty()) {
            const auto it = port_map.find(low);
            if (it != port_map.end()) return it->second;
        }
        const std::string scoped = scope_prefix.empty() ? low : (scope_prefix + "." + low);
        return circuit.get_or_add_node(scoped);
    }

    static std::string scoped_id(const std::string& raw_id, const std::string& scope_prefix) {
        return scope_prefix.empty() ? raw_id : (scope_prefix + "." + raw_id);
    }

    /* Rewrite v(node), v(a,b), i(src) inside a behavioral expression for subcircuit scope. */
    std::string rewrite_behavioral_expr(
        const std::string& expr,
        const std::string& scope_prefix,
        const std::unordered_map<std::string, NodeId>& port_map) {
        if (scope_prefix.empty()) return expr;
        std::string out;
        out.reserve(expr.size() + 16u);
        std::size_t i = 0;
        const std::size_t n = expr.size();
        while (i < n) {
            if ((expr[i] == 'v' || expr[i] == 'V' || expr[i] == 'i' || expr[i] == 'I') &&
                (i == 0u || (!std::isalnum(static_cast<unsigned char>(expr[i - 1u])) && expr[i - 1u] != '_'))) {
                std::size_t j = i + 1u;
                while (j < n && std::isspace(static_cast<unsigned char>(expr[j]))) ++j;
                if (j < n && expr[j] == '(') {
                    const bool is_v = (expr[i] == 'v' || expr[i] == 'V');
                    std::size_t close = expr.find(')', j + 1u);
                    if (close != std::string::npos) {
                        const std::string inside = expr.substr(j + 1u, close - j - 1u);
                        out.push_back(expr[i]);
                        out.push_back('(');
                        if (is_v) {
                            const std::size_t comma = inside.find(',');
                            if (comma == std::string::npos) {
                                const NodeId nid = resolve_node(trim(inside), scope_prefix, port_map);
                                out += circuit.nodes[nid.value];
                            } else {
                                const NodeId n1 = resolve_node(trim(inside.substr(0, comma)), scope_prefix, port_map);
                                const NodeId n2 = resolve_node(trim(inside.substr(comma + 1u)), scope_prefix, port_map);
                                out += circuit.nodes[n1.value];
                                out.push_back(',');
                                out += circuit.nodes[n2.value];
                            }
                        } else {
                            out += scoped_id(trim(inside), scope_prefix);
                        }
                        out.push_back(')');
                        i = close + 1u;
                        continue;
                    }
                }
            }
            out.push_back(expr[i++]);
        }
        return out;
    }
};

/* Parse key=value assignments from a slice of tokens. */
Diagnostic parse_param_assignments(
    std::size_t line_no,
    const std::vector<std::string>& tokens,
    std::size_t start_idx,
    ParamMap& params) {
    for (std::size_t i = start_idx; i < tokens.size(); ++i) {
        const std::string& tok = tokens[i];
        const std::size_t eq = tok.find('=');
        if (eq == std::string::npos || eq == 0u || eq + 1u >= tok.size()) {
            return line_err(line_no, StatusCode::InvalidArgument, "expected name=value in .PARAM, got '" + tok + "'");
        }
        const std::string key = to_lower(tok.substr(0, eq));
        const std::string val_str = tok.substr(eq + 1u);
        double val = 0.0;
        if (auto d = eval_value(line_no, val_str, params, val); !d.ok()) return d;
        params[key] = val;
    }
    return Diagnostic::success();
}

/* Parse PULSE(...), SIN(...), or PWL(...) waveform token(s). */
Diagnostic parse_waveform_spec(
    std::size_t line_no,
    const std::string& wf_kind_low,
    const std::vector<std::string>& args,
    const ParamMap& params,
    core::Waveform& out_wf) {
    std::vector<double> vals;
    vals.reserve(args.size());
    for (const auto& a : args) {
        double v = 0.0;
        if (auto d = eval_value(line_no, a, params, v); !d.ok()) return d;
        vals.push_back(v);
    }

    if (wf_kind_low == "pulse") {
        if (vals.size() < 2u || vals.size() > 7u) {
            return line_err(line_no, StatusCode::InvalidArgument, "PULSE requires 2..7 parameters");
        }
        core::PulseWaveform p{};
        p.v1 = vals[0];
        p.v2 = vals[1];
        if (vals.size() > 2u) p.delay_s = vals[2];
        if (vals.size() > 3u) p.rise_s = vals[3];
        if (vals.size() > 4u) p.fall_s = vals[4];
        if (vals.size() > 5u) p.width_s = vals[5];
        if (vals.size() > 6u) p.period_s = vals[6];
        out_wf = p;
        return Diagnostic::success();
    }
    if (wf_kind_low == "sin") {
        if (vals.size() < 3u || vals.size() > 6u) {
            return line_err(line_no, StatusCode::InvalidArgument, "SIN requires 3..6 parameters");
        }
        core::SineWaveform s{};
        s.offset = vals[0];
        s.amplitude = vals[1];
        s.freq_hz = vals[2];
        if (vals.size() > 3u) s.delay_s = vals[3];
        if (vals.size() > 4u) s.damping_per_s = vals[4];
        if (vals.size() > 5u) s.phase_deg = vals[5];
        out_wf = s;
        return Diagnostic::success();
    }
    if (wf_kind_low == "pwl") {
        if (vals.size() < 2u || (vals.size() % 2u) != 0u) {
            return line_err(line_no, StatusCode::InvalidArgument, "PWL requires an even number of (time, value) pairs");
        }
        core::PwlWaveform pwl{};
        for (std::size_t i = 0; i < vals.size(); i += 2u) {
            pwl.points.push_back(core::PwlPoint{vals[i], vals[i + 1u]});
        }
        out_wf = std::move(pwl);
        return Diagnostic::success();
    }
    if (wf_kind_low == "exp") {
        if (vals.size() < 2u || vals.size() > 6u) {
            return line_err(line_no, StatusCode::InvalidArgument, "EXP requires 2..6 parameters");
        }
        core::ExpWaveform e{};
        e.v1 = vals[0];
        e.v2 = vals[1];
        if (vals.size() > 2u) e.td1_s = vals[2];
        if (vals.size() > 3u) e.tau1_s = vals[3];
        if (vals.size() > 4u) e.td2_s = vals[4];
        else e.td2_s = e.td1_s + 10.0 * e.tau1_s;
        if (vals.size() > 5u) e.tau2_s = vals[5];
        else e.tau2_s = e.tau1_s;
        out_wf = e;
        return Diagnostic::success();
    }
    if (wf_kind_low == "sffm") {
        if (vals.size() < 3u || vals.size() > 5u) {
            return line_err(line_no, StatusCode::InvalidArgument, "SFFM requires 3..5 parameters");
        }
        core::SffmWaveform s{};
        s.offset = vals[0];
        s.amplitude = vals[1];
        s.carrier_hz = vals[2];
        if (vals.size() > 3u) s.mod_index = vals[3];
        if (vals.size() > 4u) s.signal_hz = vals[4];
        out_wf = s;
        return Diagnostic::success();
    }
    return line_err(line_no, StatusCode::Unsupported, "unsupported transient waveform '" + wf_kind_low + "'");
}

/* Parse V or I source specification tokens starting after p, n. */
Diagnostic parse_independent_source_tail(
    std::size_t line_no,
    const std::vector<std::string>& tokens,
    std::size_t start_idx,
    const ParamMap& params,
    double& out_dc,
    std::complex<double>& out_ac,
    std::optional<core::Waveform>& out_wf) {
    out_dc = 0.0;
    out_ac = {0.0, 0.0};
    out_wf.reset();

    bool dc_set = false;
    std::size_t i = start_idx;
    while (i < tokens.size()) {
        const std::string& tok = tokens[i];
        const std::string low = to_lower(tok);

        if (low == "dc") {
            if (i + 1u >= tokens.size()) {
                return line_err(line_no, StatusCode::InvalidArgument, "DC keyword missing value");
            }
            if (auto d = eval_value(line_no, tokens[i + 1u], params, out_dc); !d.ok()) return d;
            dc_set = true;
            i += 2u;
            continue;
        }
        if (low == "ac") {
            if (i + 1u >= tokens.size()) {
                return line_err(line_no, StatusCode::InvalidArgument, "AC keyword missing magnitude");
            }
            double mag = 0.0;
            if (auto d = eval_value(line_no, tokens[i + 1u], params, mag); !d.ok()) return d;
            double phase_deg = 0.0;
            i += 2u;
            if (i < tokens.size()) {
                double maybe_phase = 0.0;
                if (eval_value(line_no, tokens[i], params, maybe_phase).ok()) {
                    phase_deg = maybe_phase;
                    ++i;
                }
            }
            const double rad = phase_deg * std::numbers::pi / 180.0;
            out_ac = std::complex<double>{mag * std::cos(rad), mag * std::sin(rad)};
            continue;
        }

        /* Check waveform: PULSE(...), SIN(...), PWL(...), EXP(...), SFFM(...) */
        const std::size_t lp = tok.find('(');
        std::string wf_name = (lp == std::string::npos) ? low : to_lower(tok.substr(0, lp));
        if (wf_name == "pulse" || wf_name == "sin" || wf_name == "pwl" || wf_name == "exp" || wf_name == "sffm") {
            std::string arg_str;
            if (lp != std::string::npos) {
                if (tok.back() != ')') {
                    return line_err(line_no, StatusCode::InvalidArgument, "unterminated '(' in " + wf_name);
                }
                arg_str = tok.substr(lp + 1u, tok.size() - lp - 2u);
                ++i;
            } else if (i + 1u < tokens.size() && !tokens[i + 1u].empty() && tokens[i + 1u].front() == '(') {
                const std::string& grp = tokens[i + 1u];
                if (grp.back() != ')') {
                    return line_err(line_no, StatusCode::InvalidArgument, "unterminated '(' in " + wf_name);
                }
                arg_str = grp.substr(1u, grp.size() - 2u);
                i += 2u;
            } else {
                /* Bare argument list until end or next keyword */
                ++i;
                while (i < tokens.size()) {
                    const std::string nxt = to_lower(tokens[i]);
                    if (nxt == "dc" || nxt == "ac") break;
                    if (!arg_str.empty()) arg_str.push_back(' ');
                    arg_str += tokens[i++];
                }
            }
            const auto args = tokenize_spice_line(arg_str);
            core::Waveform wf;
            if (auto d = parse_waveform_spec(line_no, wf_name, args, params, wf); !d.ok()) return d;
            out_wf = std::move(wf);
            continue;
        }

        if (!dc_set) {
            if (auto d = eval_value(line_no, tok, params, out_dc); !d.ok()) return d;
            dc_set = true;
            ++i;
            continue;
        }
        return line_err(line_no, StatusCode::InvalidArgument, "unexpected token '" + tok + "' in source");
    }
    return Diagnostic::success();
}

Diagnostic parse_model_directive(
    std::size_t line_no,
    const std::vector<std::string>& tokens,
    const ParamMap& params,
    ElaboratorState& state) {
    if (tokens.size() < 3u) {
        return line_err(line_no, StatusCode::InvalidArgument, ".MODEL requires name and type");
    }
    const std::string model_name = to_lower(tokens[1]);
    std::string type_tok = tokens[2];
    std::string param_body;

    const std::size_t lp = type_tok.find('(');
    if (lp != std::string::npos) {
        if (type_tok.back() != ')') {
            return line_err(line_no, StatusCode::InvalidArgument, "unterminated '(' in .MODEL");
        }
        param_body = type_tok.substr(lp + 1u, type_tok.size() - lp - 2u);
        type_tok = type_tok.substr(0, lp);
    }
    for (std::size_t i = 3u; i < tokens.size(); ++i) {
        std::string t = tokens[i];
        if (!t.empty() && t.front() == '(' && t.back() == ')') {
            t = t.substr(1u, t.size() - 2u);
        }
        if (!param_body.empty()) param_body.push_back(' ');
        param_body += t;
    }

    const std::string type_low = to_lower(type_tok);
    const auto kv_tokens = tokenize_spice_line(param_body);

    if (type_low == "d") {
        ModelDef def{};
        def.kind = ModelDef::Kind::Diode;
        def.diode_template.gmin_siemens = 0.0;
        for (const auto& kv : kv_tokens) {
            const std::size_t eq = kv.find('=');
            if (eq == std::string::npos) {
                return line_err(line_no, StatusCode::InvalidArgument, "expected key=value in .MODEL D, got '" + kv + "'");
            }
            const std::string k = to_lower(kv.substr(0, eq));
            double v = 0.0;
            if (auto d = eval_value(line_no, kv.substr(eq + 1u), params, v); !d.ok()) return d;
            if (k == "is") def.diode_template.is_amps = v;
            else if (k == "n") def.diode_template.n_emission = v;
            else if (k == "vt") def.diode_template.vt_volts = v;
            else if (k == "cjo") def.diode_template.cjo_farads = v;
            else if (k == "vj") def.diode_template.vj_volts = v;
            else if (k == "m") def.diode_template.m_grading = v;
            else if (k == "tt") def.diode_template.tt_s = v;
            else if (k == "gmin") def.diode_template.gmin_siemens = v;
            else {
                return line_err(line_no, StatusCode::InvalidArgument, "unsupported diode .MODEL parameter '" + k + "'");
            }
        }
        state.models[model_name] = std::move(def);
        return Diagnostic::success();
    }

    if (type_low == "sw" || type_low == "vswitch") {
        ModelDef def{};
        def.kind = ModelDef::Kind::Switch;
        for (const auto& kv : kv_tokens) {
            const std::size_t eq = kv.find('=');
            if (eq == std::string::npos) {
                return line_err(line_no, StatusCode::InvalidArgument, "expected key=value in .MODEL SW");
            }
            const std::string k = to_lower(kv.substr(0, eq));
            double v = 0.0;
            if (auto d = eval_value(line_no, kv.substr(eq + 1u), params, v); !d.ok()) return d;
            if (k == "vt") def.switch_template.vt_volts = v;
            else if (k == "vh") def.switch_template.vh_volts = v;
            else if (k == "ron") def.switch_template.ron_ohms = v;
            else if (k == "roff") def.switch_template.roff_ohms = v;
            else {
                return line_err(line_no, StatusCode::InvalidArgument, "unsupported SW .MODEL parameter '" + k + "'");
            }
        }
        state.models[model_name] = std::move(def);
        return Diagnostic::success();
    }

    if (type_low == "nmos" || type_low == "pmos") {
        ModelDef def{};
        def.kind = ModelDef::Kind::Mosfet;
        def.mosfet_template.is_pmos = (type_low == "pmos");
        def.mosfet_template.vth0_volts = def.mosfet_template.is_pmos ? -0.7 : 0.7;
        def.mosfet_template.u0_m2_per_vs = def.mosfet_template.is_pmos ? 0.015 : 0.045;
        bool vth_explicit = false;
        for (const auto& kv : kv_tokens) {
            const std::size_t eq = kv.find('=');
            if (eq == std::string::npos) {
                return line_err(line_no, StatusCode::InvalidArgument, "expected key=value in .MODEL " + type_tok);
            }
            const std::string k = to_lower(kv.substr(0, eq));
            double v = 0.0;
            if (auto d = eval_value(line_no, kv.substr(eq + 1u), params, v); !d.ok()) return d;
            if (k == "level") {
                const int lvl = static_cast<int>(std::llround(v));
                if (lvl != 1 && lvl != 49 && lvl != 54) {
                    return line_err(
                        line_no,
                        StatusCode::Unsupported,
                        "unsupported MOSFET LEVEL=" + std::to_string(lvl) +
                            " in .MODEL '" + model_name +
                            "' (supported native levels: 1, 49/BSIM3v3, 54/BSIM4; or use .OSDI)");
                }
                def.mosfet_template.level = lvl;
            } else if (k == "vto" || k == "vth0") {
                def.mosfet_template.vth0_volts = v;
                vth_explicit = true;
            } else if (k == "kp") {
                def.mosfet_template.kp_a_per_v2 = v;
            } else if (k == "gamma") {
                def.mosfet_template.gamma_v12 = v;
            } else if (k == "phi") {
                def.mosfet_template.phi_volts = v;
            } else if (k == "lambda") {
                def.mosfet_template.lambda_per_v = v;
            } else if (k == "tox" || k == "toxe") {
                def.mosfet_template.tox_m = v;
            } else if (k == "u0" || k == "uo") {
                /* Standard SPICE BSIM/MOS decks specify U0 in cm^2/(V*s) (e.g. 450); accept SI <= 1.0 too. */
                def.mosfet_template.u0_m2_per_vs = (v > 1.0) ? (v * 1.0e-4) : v;
            } else if (k == "vsat") {
                def.mosfet_template.vsat_m_per_s = v;
            } else if (k == "k1") {
                def.mosfet_template.k1_v12 = v;
            } else if (k == "k2") {
                def.mosfet_template.k2 = v;
            } else if (k == "ua") {
                def.mosfet_template.ua_m_per_v = v;
            } else if (k == "ub") {
                def.mosfet_template.ub_m2_per_v2 = v;
            } else if (k == "uc") {
                def.mosfet_template.uc_m_per_v2 = v;
            } else if (k == "eta0") {
                def.mosfet_template.eta0 = v;
            } else if (k == "pclm") {
                def.mosfet_template.pclm = v;
            } else if (k == "voff") {
                def.mosfet_template.voff_volts = v;
            } else if (k == "nfactor") {
                def.mosfet_template.nfactor = v;
            } else if (k == "rdsw") {
                def.mosfet_template.rdsw_ohm_um = v;
            } else if (k == "delta") {
                def.mosfet_template.delta_vds = v;
            } else if (k == "cgso") {
                def.mosfet_template.cgso_f_per_m = v;
            } else if (k == "cgdo") {
                def.mosfet_template.cgdo_f_per_m = v;
            } else if (k == "cgbo") {
                def.mosfet_template.cgbo_f_per_m = v;
            } else if (k == "cbd") {
                def.mosfet_template.cbd_farads = v;
            } else if (k == "cbs") {
                def.mosfet_template.cbs_farads = v;
            } else if (k == "is" || k == "js") {
                def.mosfet_template.js_amps = v;
            } else if (k == "version") {
                /* Accept BSIM3/BSIM4 version metadata stamp (e.g. VERSION=3.3 or 4.8). */
            } else {
                return line_err(
                    line_no, StatusCode::InvalidArgument,
                    "unsupported MOSFET .MODEL parameter '" + k + "'");
            }
        }
        if (!vth_explicit && (def.mosfet_template.level == 49 || def.mosfet_template.level == 54)) {
            def.mosfet_template.vth0_volts = def.mosfet_template.is_pmos ? -0.4 : 0.4;
        }
        state.models[model_name] = std::move(def);
        return Diagnostic::success();
    }

    if (type_low == "npn" || type_low == "pnp") {
        ModelDef def{};
        def.kind = ModelDef::Kind::Bjt;
        def.bjt_template.is_pnp = (type_low == "pnp");
        for (const auto& kv : kv_tokens) {
            const std::size_t eq = kv.find('=');
            if (eq == std::string::npos) {
                return line_err(line_no, StatusCode::InvalidArgument, "expected key=value in .MODEL " + type_tok);
            }
            const std::string k = to_lower(kv.substr(0, eq));
            double v = 0.0;
            if (auto d = eval_value(line_no, kv.substr(eq + 1u), params, v); !d.ok()) return d;
            if (k == "is") def.bjt_template.is_amps = v;
            else if (k == "bf") def.bjt_template.bf = v;
            else if (k == "br") def.bjt_template.br = v;
            else if (k == "nf") def.bjt_template.nf = v;
            else if (k == "nr") def.bjt_template.nr = v;
            else if (k == "vaf" || k == "va") def.bjt_template.vaf_volts = v;
            else if (k == "var") def.bjt_template.var_volts = v;
            else if (k == "ikf") def.bjt_template.ikf_amps = v;
            else if (k == "ikr") def.bjt_template.ikr_amps = v;
            else if (k == "ise") def.bjt_template.ise_amps = v;
            else if (k == "ne") def.bjt_template.ne = v;
            else if (k == "isc") def.bjt_template.isc_amps = v;
            else if (k == "nc") def.bjt_template.nc = v;
            else if (k == "cje") def.bjt_template.cje_farads = v;
            else if (k == "vje") def.bjt_template.vje_volts = v;
            else if (k == "mje") def.bjt_template.mje = v;
            else if (k == "tf") def.bjt_template.tf_s = v;
            else if (k == "cjc") def.bjt_template.cjc_farads = v;
            else if (k == "vjc") def.bjt_template.vjc_volts = v;
            else if (k == "mjc") def.bjt_template.mjc = v;
            else if (k == "tr") def.bjt_template.tr_s = v;
            else {
                return line_err(
                    line_no, StatusCode::InvalidArgument,
                    "unsupported BJT .MODEL parameter '" + k + "'");
            }
        }
        state.models[model_name] = std::move(def);
        return Diagnostic::success();
    }

    if (type_low == "njf" || type_low == "pjf") {
        ModelDef def{};
        def.kind = ModelDef::Kind::Jfet;
        def.jfet_template.is_pjf = (type_low == "pjf");
        for (const auto& kv : kv_tokens) {
            const std::size_t eq = kv.find('=');
            if (eq == std::string::npos) {
                return line_err(line_no, StatusCode::InvalidArgument, "expected key=value in .MODEL " + type_tok);
            }
            const std::string k = to_lower(kv.substr(0, eq));
            double v = 0.0;
            if (auto d = eval_value(line_no, kv.substr(eq + 1u), params, v); !d.ok()) return d;
            if (k == "vto") def.jfet_template.vto_volts = v;
            else if (k == "beta") def.jfet_template.beta_a_per_v2 = v;
            else if (k == "lambda") def.jfet_template.lambda_per_v = v;
            else if (k == "is") def.jfet_template.is_amps = v;
            else if (k == "cgs") def.jfet_template.cgs_farads = v;
            else if (k == "cgd") def.jfet_template.cgd_farads = v;
            else if (k == "pb") def.jfet_template.pb_volts = v;
            else {
                return line_err(
                    line_no, StatusCode::InvalidArgument,
                    "unsupported JFET .MODEL parameter '" + k + "'");
            }
        }
        state.models[model_name] = std::move(def);
        return Diagnostic::success();
    }

    if (type_low == "osdi") {
        ModelDef def{};
        def.kind = ModelDef::Kind::Osdi;
        for (const auto& kv : kv_tokens) {
            const std::size_t eq = kv.find('=');
            if (eq == std::string::npos) {
                return line_err(line_no, StatusCode::InvalidArgument, "expected key=value in .MODEL OSDI");
            }
            const std::string k = to_lower(kv.substr(0, eq));
            const std::string v_raw = kv.substr(eq + 1u);
            if (k == "lib" || k == "library") {
                def.osdi_library_path = unquote(v_raw);
            } else if (k == "model") {
                def.osdi_model_name = unquote(v_raw);
            } else {
                double v = 0.0;
                if (auto d = eval_value(line_no, v_raw, params, v); !d.ok()) return d;
                def.osdi_model_params.emplace_back(k, v);
            }
        }
        if (def.osdi_library_path.empty() && !state.registered_osdi_libs.empty()) {
            def.osdi_library_path = state.registered_osdi_libs.back();
        }
        if (def.osdi_library_path.empty() || def.osdi_model_name.empty()) {
            return line_err(line_no, StatusCode::InvalidArgument, ".MODEL OSDI requires lib and model");
        }
        state.models[model_name] = std::move(def);
        return Diagnostic::success();
    }

    return line_err(line_no, StatusCode::Unsupported, "unsupported .MODEL type '" + type_tok + "'");
}

Diagnostic elaborate_lines(
    const std::vector<LogicalLine>& lines,
    const std::string& scope_prefix,
    const std::unordered_map<std::string, NodeId>& port_map,
    ParamMap local_params,
    ElaboratorState& state);

Diagnostic elaborate_element_line(
    const LogicalLine& line,
    const std::vector<std::string>& tokens,
    const std::string& scope_prefix,
    const std::unordered_map<std::string, NodeId>& port_map,
    const ParamMap& params,
    ElaboratorState& state) {
    const std::string& raw_name = tokens[0];
    const char prefix = static_cast<char>(std::toupper(static_cast<unsigned char>(raw_name[0])));
    const std::string elem_id = ElaboratorState::scoped_id(raw_name, scope_prefix);

    auto req_tokens = [&](std::size_t min_n, const char* kind) -> Diagnostic {
        if (tokens.size() < min_n) {
            return line_err(line.line_no, StatusCode::InvalidArgument,
                            std::string(kind) + " '" + raw_name + "' has too few tokens");
        }
        return Diagnostic::success();
    };

    switch (prefix) {
        case 'R': {
            if (auto d = req_tokens(4u, "Resistor"); !d.ok()) return d;
            double ohms = 0.0;
            if (auto d = eval_value(line.line_no, tokens[3], params, ohms); !d.ok()) return d;
            state.circuit.elements.push_back(core::ResistorSpec{
                elem_id,
                state.resolve_node(tokens[1], scope_prefix, port_map),
                state.resolve_node(tokens[2], scope_prefix, port_map),
                ohms});
            return Diagnostic::success();
        }
        case 'C': {
            if (auto d = req_tokens(4u, "Capacitor"); !d.ok()) return d;
            double f = 0.0;
            if (auto d = eval_value(line.line_no, tokens[3], params, f); !d.ok()) return d;
            std::optional<double> ic;
            for (std::size_t i = 4u; i < tokens.size(); ++i) {
                const std::string low = to_lower(tokens[i]);
                if (low.rfind("ic=", 0) == 0) {
                    double v = 0.0;
                    if (auto d = eval_value(line.line_no, tokens[i].substr(3u), params, v); !d.ok()) return d;
                    ic = v;
                } else {
                    return line_err(line.line_no, StatusCode::InvalidArgument, "unexpected capacitor option '" + tokens[i] + "'");
                }
            }
            state.circuit.elements.push_back(core::CapacitorSpec{
                elem_id,
                state.resolve_node(tokens[1], scope_prefix, port_map),
                state.resolve_node(tokens[2], scope_prefix, port_map),
                f,
                ic});
            return Diagnostic::success();
        }
        case 'L': {
            if (auto d = req_tokens(4u, "Inductor"); !d.ok()) return d;
            double h = 0.0;
            if (auto d = eval_value(line.line_no, tokens[3], params, h); !d.ok()) return d;
            std::optional<double> ic;
            for (std::size_t i = 4u; i < tokens.size(); ++i) {
                const std::string low = to_lower(tokens[i]);
                if (low.rfind("ic=", 0) == 0) {
                    double v = 0.0;
                    if (auto d = eval_value(line.line_no, tokens[i].substr(3u), params, v); !d.ok()) return d;
                    ic = v;
                } else {
                    return line_err(line.line_no, StatusCode::InvalidArgument, "unexpected inductor option '" + tokens[i] + "'");
                }
            }
            state.circuit.elements.push_back(core::InductorSpec{
                elem_id,
                state.resolve_node(tokens[1], scope_prefix, port_map),
                state.resolve_node(tokens[2], scope_prefix, port_map),
                h,
                ic});
            return Diagnostic::success();
        }
        case 'K': {
            if (auto d = req_tokens(4u, "Mutual inductance"); !d.ok()) return d;
            double k = 0.0;
            if (auto d = eval_value(line.line_no, tokens[3], params, k); !d.ok()) return d;
            state.circuit.elements.push_back(core::MutualInductanceSpec{
                elem_id,
                ElaboratorState::scoped_id(tokens[1], scope_prefix),
                ElaboratorState::scoped_id(tokens[2], scope_prefix),
                k});
            return Diagnostic::success();
        }
        case 'V': {
            if (auto d = req_tokens(3u, "Voltage source"); !d.ok()) return d;
            double dc = 0.0;
            std::complex<double> ac{0.0, 0.0};
            std::optional<core::Waveform> wf;
            if (auto d = parse_independent_source_tail(line.line_no, tokens, 3u, params, dc, ac, wf); !d.ok()) {
                return d;
            }
            state.circuit.elements.push_back(core::VoltageSourceSpec{
                elem_id,
                state.resolve_node(tokens[1], scope_prefix, port_map),
                state.resolve_node(tokens[2], scope_prefix, port_map),
                dc,
                ac,
                std::move(wf)});
            return Diagnostic::success();
        }
        case 'I': {
            if (auto d = req_tokens(3u, "Current source"); !d.ok()) return d;
            double dc = 0.0;
            std::complex<double> ac{0.0, 0.0};
            std::optional<core::Waveform> wf;
            if (auto d = parse_independent_source_tail(line.line_no, tokens, 3u, params, dc, ac, wf); !d.ok()) {
                return d;
            }
            state.circuit.elements.push_back(core::CurrentSourceSpec{
                elem_id,
                state.resolve_node(tokens[1], scope_prefix, port_map),
                state.resolve_node(tokens[2], scope_prefix, port_map),
                dc,
                ac,
                std::move(wf)});
            return Diagnostic::success();
        }
        case 'G': {
            if (auto d = req_tokens(6u, "VCCS"); !d.ok()) return d;
            double gm = 0.0;
            if (auto d = eval_value(line.line_no, tokens[5], params, gm); !d.ok()) return d;
            state.circuit.elements.push_back(core::VccsSpec{
                elem_id,
                state.resolve_node(tokens[1], scope_prefix, port_map),
                state.resolve_node(tokens[2], scope_prefix, port_map),
                state.resolve_node(tokens[3], scope_prefix, port_map),
                state.resolve_node(tokens[4], scope_prefix, port_map),
                gm});
            return Diagnostic::success();
        }
        case 'E': {
            if (auto d = req_tokens(6u, "VCVS"); !d.ok()) return d;
            double gain = 0.0;
            if (auto d = eval_value(line.line_no, tokens[5], params, gain); !d.ok()) return d;
            state.circuit.elements.push_back(core::VcvsSpec{
                elem_id,
                state.resolve_node(tokens[1], scope_prefix, port_map),
                state.resolve_node(tokens[2], scope_prefix, port_map),
                state.resolve_node(tokens[3], scope_prefix, port_map),
                state.resolve_node(tokens[4], scope_prefix, port_map),
                gain});
            return Diagnostic::success();
        }
        case 'F': {
            if (auto d = req_tokens(5u, "CCCS"); !d.ok()) return d;
            double gain = 0.0;
            if (auto d = eval_value(line.line_no, tokens[4], params, gain); !d.ok()) return d;
            state.circuit.elements.push_back(core::CccsSpec{
                elem_id,
                state.resolve_node(tokens[1], scope_prefix, port_map),
                state.resolve_node(tokens[2], scope_prefix, port_map),
                ElaboratorState::scoped_id(tokens[3], scope_prefix),
                gain});
            return Diagnostic::success();
        }
        case 'H': {
            if (auto d = req_tokens(5u, "CCVS"); !d.ok()) return d;
            double rm = 0.0;
            if (auto d = eval_value(line.line_no, tokens[4], params, rm); !d.ok()) return d;
            state.circuit.elements.push_back(core::CcvsSpec{
                elem_id,
                state.resolve_node(tokens[1], scope_prefix, port_map),
                state.resolve_node(tokens[2], scope_prefix, port_map),
                ElaboratorState::scoped_id(tokens[3], scope_prefix),
                rm});
            return Diagnostic::success();
        }
        case 'D': {
            if (auto d = req_tokens(4u, "Diode"); !d.ok()) return d;
            const std::string mname = to_lower(tokens[3]);
            const auto mit = state.models.find(mname);
            if (mit == state.models.end() || mit->second.kind != ModelDef::Kind::Diode) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "unresolved diode .MODEL '" + tokens[3] + "'");
            }
            core::DiodeSpec spec = mit->second.diode_template;
            spec.id = elem_id;
            spec.p = state.resolve_node(tokens[1], scope_prefix, port_map);
            spec.n = state.resolve_node(tokens[2], scope_prefix, port_map);
            state.circuit.elements.push_back(std::move(spec));
            return Diagnostic::success();
        }
        case 'B': {
            if (auto d = req_tokens(4u, "Behavioral source"); !d.ok()) return d;
            /* Reconstruct everything after the 2 node tokens verbatim from the line. */
            std::string expr_tail;
            for (std::size_t i = 3u; i < tokens.size(); ++i) {
                if (i > 3u) expr_tail.push_back(' ');
                expr_tail += tokens[i];
            }
            const std::string low = to_lower(expr_tail);
            bool is_voltage = false;
            std::string rhs_expr;
            if (low.rfind("v=", 0) == 0) {
                is_voltage = true;
                rhs_expr = expr_tail.substr(2u);
            } else if (low.rfind("i=", 0) == 0) {
                is_voltage = false;
                rhs_expr = expr_tail.substr(2u);
            } else {
                return line_err(line.line_no, StatusCode::InvalidArgument, "B-source requires V=<expr> or I=<expr>");
            }
            rhs_expr = trim(rhs_expr);
            if (rhs_expr.size() >= 2u && rhs_expr.front() == '{' && rhs_expr.back() == '}') {
                rhs_expr = rhs_expr.substr(1u, rhs_expr.size() - 2u);
            }
            rhs_expr = state.rewrite_behavioral_expr(rhs_expr, scope_prefix, port_map);
            state.circuit.elements.push_back(core::BehavioralSourceSpec{
                elem_id,
                state.resolve_node(tokens[1], scope_prefix, port_map),
                state.resolve_node(tokens[2], scope_prefix, port_map),
                is_voltage,
                std::move(rhs_expr)});
            return Diagnostic::success();
        }
        case 'S': {
            if (auto d = req_tokens(6u, "Voltage-controlled switch"); !d.ok()) return d;
            const std::string mname = to_lower(tokens[5]);
            const auto mit = state.models.find(mname);
            if (mit == state.models.end() || mit->second.kind != ModelDef::Kind::Switch) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "unresolved switch .MODEL '" + tokens[5] + "'");
            }
            core::SwitchSpec spec = mit->second.switch_template;
            spec.id = elem_id;
            spec.p = state.resolve_node(tokens[1], scope_prefix, port_map);
            spec.n = state.resolve_node(tokens[2], scope_prefix, port_map);
            spec.cp = state.resolve_node(tokens[3], scope_prefix, port_map);
            spec.cn = state.resolve_node(tokens[4], scope_prefix, port_map);
            state.circuit.elements.push_back(std::move(spec));
            return Diagnostic::success();
        }
        case 'T': {
            if (auto d = req_tokens(6u, "Transmission line"); !d.ok()) return d;
            core::TransmissionLineSpec spec{};
            spec.id = elem_id;
            spec.p1 = state.resolve_node(tokens[1], scope_prefix, port_map);
            spec.n1 = state.resolve_node(tokens[2], scope_prefix, port_map);
            spec.p2 = state.resolve_node(tokens[3], scope_prefix, port_map);
            spec.n2 = state.resolve_node(tokens[4], scope_prefix, port_map);
            bool has_z0 = false, has_td = false;
            for (std::size_t i = 5u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                if (eq == std::string::npos) {
                    return line_err(line.line_no, StatusCode::InvalidArgument, "expected Z0=... TD=... in T element");
                }
                const std::string k = to_lower(tokens[i].substr(0, eq));
                double v = 0.0;
                if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), params, v); !d.ok()) return d;
                if (k == "z0" || k == "zo") {
                    spec.z0_ohms = v;
                    has_z0 = true;
                } else if (k == "td") {
                    spec.td_s = v;
                    has_td = true;
                } else {
                    return line_err(line.line_no, StatusCode::InvalidArgument, "unsupported T parameter '" + k + "'");
                }
            }
            if (!has_z0 || !has_td) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "T element requires both Z0 and TD");
            }
            state.circuit.elements.push_back(std::move(spec));
            return Diagnostic::success();
        }
        case 'M': {
            if (auto d = req_tokens(6u, "MOSFET"); !d.ok()) return d;
            const std::string mname = to_lower(tokens[5]);
            const auto mit = state.models.find(mname);
            if (mit == state.models.end()) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "unresolved MOSFET .MODEL '" + tokens[5] + "'");
            }
            const auto nd = state.resolve_node(tokens[1], scope_prefix, port_map);
            const auto ng = state.resolve_node(tokens[2], scope_prefix, port_map);
            const auto ns = state.resolve_node(tokens[3], scope_prefix, port_map);
            const auto nb = state.resolve_node(tokens[4], scope_prefix, port_map);

            if (mit->second.kind == ModelDef::Kind::Osdi) {
                core::OsdiInstanceSpec inst{};
                inst.id = elem_id;
                inst.library_path = mit->second.osdi_library_path;
                inst.model_name = mit->second.osdi_model_name;
                inst.model_params = mit->second.osdi_model_params;
                inst.terminals = {nd, ng, ns, nb};
                for (std::size_t i = 6u; i < tokens.size(); ++i) {
                    const std::size_t eq = tokens[i].find('=');
                    if (eq == std::string::npos) {
                        return line_err(line.line_no, StatusCode::InvalidArgument, "expected key=value in M instance");
                    }
                    double v = 0.0;
                    if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), params, v); !d.ok()) return d;
                    inst.instance_params.emplace_back(to_lower(tokens[i].substr(0, eq)), v);
                }
                state.circuit.elements.push_back(std::move(inst));
                return Diagnostic::success();
            }
            if (mit->second.kind != ModelDef::Kind::Mosfet) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "model '" + tokens[5] + "' is not a MOSFET/OSDI model");
            }
            core::MosfetSpec spec = mit->second.mosfet_template;
            spec.id = elem_id;
            spec.d = nd;
            spec.g = ng;
            spec.s = ns;
            spec.b = nb;
            for (std::size_t i = 6u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                if (eq == std::string::npos) {
                    return line_err(line.line_no, StatusCode::InvalidArgument, "expected key=value in M instance");
                }
                const std::string k = to_lower(tokens[i].substr(0, eq));
                double v = 0.0;
                if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), params, v); !d.ok()) return d;
                if (k == "w") spec.w_m = v;
                else if (k == "l") spec.l_m = v;
                else if (k == "m" || k == "nf") spec.m_mult *= v;
                else if (k == "ad" || k == "as" || k == "pd" || k == "ps" || k == "nrd" || k == "nrs") {
                    /* Accept standard SPICE MOSFET diffusion geometry attributes. */
                } else {
                    return line_err(line.line_no, StatusCode::InvalidArgument, "unsupported MOSFET instance parameter '" + k + "'");
                }
            }
            state.circuit.elements.push_back(std::move(spec));
            return Diagnostic::success();
        }
        case 'Q': {
            if (auto d = req_tokens(5u, "BJT"); !d.ok()) return d;
            /* Support both 3-terminal Q c b e model [area/kv...] and 4-terminal Q c b e ns model [...]. */
            std::size_t model_idx = 4u;
            if (tokens.size() >= 6u && state.models.find(to_lower(tokens[4])) == state.models.end() &&
                state.models.find(to_lower(tokens[5])) != state.models.end()) {
                model_idx = 5u;
            }
            const std::string mname = to_lower(tokens[model_idx]);
            const auto mit = state.models.find(mname);
            if (mit == state.models.end()) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "unresolved BJT .MODEL '" + tokens[model_idx] + "'");
            }
            const auto nc = state.resolve_node(tokens[1], scope_prefix, port_map);
            const auto nb = state.resolve_node(tokens[2], scope_prefix, port_map);
            const auto ne = state.resolve_node(tokens[3], scope_prefix, port_map);
            if (mit->second.kind == ModelDef::Kind::Osdi) {
                core::OsdiInstanceSpec inst{};
                inst.id = elem_id;
                inst.library_path = mit->second.osdi_library_path;
                inst.model_name = mit->second.osdi_model_name;
                inst.model_params = mit->second.osdi_model_params;
                inst.terminals = {nc, nb, ne};
                if (model_idx == 5u) inst.terminals.push_back(state.resolve_node(tokens[4], scope_prefix, port_map));
                for (std::size_t i = model_idx + 1u; i < tokens.size(); ++i) {
                    const std::size_t eq = tokens[i].find('=');
                    if (eq == std::string::npos) {
                        double area_v = 1.0;
                        if (auto d = eval_value(line.line_no, tokens[i], params, area_v); !d.ok()) return d;
                        inst.instance_params.emplace_back("area", area_v);
                    } else {
                        double v = 0.0;
                        if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), params, v); !d.ok()) return d;
                        inst.instance_params.emplace_back(to_lower(tokens[i].substr(0, eq)), v);
                    }
                }
                state.circuit.elements.push_back(std::move(inst));
                return Diagnostic::success();
            }
            if (mit->second.kind != ModelDef::Kind::Bjt) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "model '" + tokens[model_idx] + "' is not a BJT/OSDI model");
            }
            core::BjtSpec spec = mit->second.bjt_template;
            spec.id = elem_id;
            spec.c = nc;
            spec.b = nb;
            spec.e = ne;
            for (std::size_t i = model_idx + 1u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                if (eq == std::string::npos) {
                    if (auto d = eval_value(line.line_no, tokens[i], params, spec.area); !d.ok()) return d;
                } else {
                    const std::string k = to_lower(tokens[i].substr(0, eq));
                    double v = 0.0;
                    if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), params, v); !d.ok()) return d;
                    if (k == "area") spec.area = v;
                    else if (k == "m") spec.m_mult = v;
                    else return line_err(line.line_no, StatusCode::InvalidArgument, "unsupported BJT instance parameter '" + k + "'");
                }
            }
            state.circuit.elements.push_back(std::move(spec));
            return Diagnostic::success();
        }
        case 'J': {
            if (auto d = req_tokens(5u, "JFET"); !d.ok()) return d;
            const std::string mname = to_lower(tokens[4]);
            const auto mit = state.models.find(mname);
            if (mit == state.models.end()) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "unresolved JFET .MODEL '" + tokens[4] + "'");
            }
            const auto nd = state.resolve_node(tokens[1], scope_prefix, port_map);
            const auto ng = state.resolve_node(tokens[2], scope_prefix, port_map);
            const auto ns = state.resolve_node(tokens[3], scope_prefix, port_map);
            if (mit->second.kind == ModelDef::Kind::Osdi) {
                core::OsdiInstanceSpec inst{};
                inst.id = elem_id;
                inst.library_path = mit->second.osdi_library_path;
                inst.model_name = mit->second.osdi_model_name;
                inst.model_params = mit->second.osdi_model_params;
                inst.terminals = {nd, ng, ns};
                state.circuit.elements.push_back(std::move(inst));
                return Diagnostic::success();
            }
            if (mit->second.kind != ModelDef::Kind::Jfet) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "model '" + tokens[4] + "' is not a JFET/OSDI model");
            }
            core::JfetSpec spec = mit->second.jfet_template;
            spec.id = elem_id;
            spec.d = nd;
            spec.g = ng;
            spec.s = ns;
            for (std::size_t i = 5u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                if (eq == std::string::npos) {
                    if (auto d = eval_value(line.line_no, tokens[i], params, spec.area); !d.ok()) return d;
                } else {
                    const std::string k = to_lower(tokens[i].substr(0, eq));
                    double v = 0.0;
                    if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), params, v); !d.ok()) return d;
                    if (k == "area") spec.area = v;
                    else if (k == "m") spec.m_mult = v;
                    else return line_err(line.line_no, StatusCode::InvalidArgument, "unsupported JFET instance parameter '" + k + "'");
                }
            }
            state.circuit.elements.push_back(std::move(spec));
            return Diagnostic::success();
        }
        case 'X': {
            if (auto d = req_tokens(3u, "Subcircuit/OSDI instance"); !d.ok()) return d;
            /* Separate optional PARAMS: or trailing key=value overrides. */
            std::size_t split = tokens.size();
            for (std::size_t i = 1u; i < tokens.size(); ++i) {
                if (to_lower(tokens[i]) == "params:" || tokens[i].find('=') != std::string::npos) {
                    split = i;
                    break;
                }
            }
            if (split < 3u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "X instance missing subcircuit or model name");
            }
            const std::string target_name = to_lower(tokens[split - 1u]);
            std::vector<std::pair<std::string, std::string>> inst_kv;
            for (std::size_t i = split; i < tokens.size(); ++i) {
                if (to_lower(tokens[i]) == "params:") continue;
                const std::size_t eq = tokens[i].find('=');
                if (eq == std::string::npos) {
                    return line_err(line.line_no, StatusCode::InvalidArgument, "expected key=value override in X instance");
                }
                inst_kv.emplace_back(to_lower(tokens[i].substr(0, eq)), tokens[i].substr(eq + 1u));
            }

            /* 1. Check OSDI .MODEL first. */
            if (const auto mit = state.models.find(target_name);
                mit != state.models.end() && mit->second.kind == ModelDef::Kind::Osdi) {
                core::OsdiInstanceSpec inst{};
                inst.id = elem_id;
                inst.library_path = mit->second.osdi_library_path;
                inst.model_name = mit->second.osdi_model_name;
                inst.model_params = mit->second.osdi_model_params;
                for (std::size_t i = 1u; i + 1u < split; ++i) {
                    inst.terminals.push_back(state.resolve_node(tokens[i], scope_prefix, port_map));
                }
                for (const auto& [k, v_str] : inst_kv) {
                    double v = 0.0;
                    if (auto d = eval_value(line.line_no, v_str, params, v); !d.ok()) return d;
                    inst.instance_params.emplace_back(k, v);
                }
                state.circuit.elements.push_back(std::move(inst));
                return Diagnostic::success();
            }

            /* 2. Otherwise instantiate .SUBCKT hierarchically. */
            const auto sit = state.subckts.find(target_name);
            if (sit == state.subckts.end()) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "unresolved .SUBCKT or OSDI model '" + tokens[split - 1u] + "'");
            }
            const SubcktDef& def = sit->second;
            const std::size_t n_conns = split - 2u;
            if (n_conns != def.ports.size()) {
                return line_err(line.line_no, StatusCode::InvalidArgument,
                                "subcircuit '" + def.name + "' expects " + std::to_string(def.ports.size()) +
                                    " ports, got " + std::to_string(n_conns));
            }
            if (state.subckt_stack.size() >= state.parse_opts.max_subckt_depth) {
                return line_err(line.line_no, StatusCode::ResourceExhausted, "subcircuit nesting depth exceeded");
            }
            if (std::find(state.subckt_stack.begin(), state.subckt_stack.end(), target_name) !=
                state.subckt_stack.end()) {
                return line_err(line.line_no, StatusCode::InvalidArgument, "cyclic .SUBCKT instantiation of '" + def.name + "'");
            }

            std::unordered_map<std::string, NodeId> child_ports;
            for (std::size_t i = 0; i < def.ports.size(); ++i) {
                child_ports[def.ports[i]] = state.resolve_node(tokens[1u + i], scope_prefix, port_map);
            }

            ParamMap child_params = params;
            for (const auto& [k, v_str] : def.default_params) {
                double v = 0.0;
                if (auto d = eval_value(line.line_no, v_str, child_params, v); !d.ok()) return d;
                child_params[k] = v;
            }
            for (const auto& [k, v_str] : inst_kv) {
                double v = 0.0;
                if (auto d = eval_value(line.line_no, v_str, params, v); !d.ok()) return d;
                child_params[k] = v;
            }

            state.subckt_stack.push_back(target_name);
            const auto d = elaborate_lines(def.body, elem_id, child_ports, std::move(child_params), state);
            state.subckt_stack.pop_back();
            return d;
        }
        default:
            return line_err(line.line_no, StatusCode::Unsupported, "unsupported SPICE element '" + raw_name + "'");
    }
}

Diagnostic parse_frequency_grid(
    std::size_t line_no,
    const std::string& kind_tok,
    const std::string& n_tok,
    const std::string& fstart_tok,
    const std::string& fstop_tok,
    const ParamMap& params,
    std::vector<double>& out_freqs) {
    const std::string kind = to_lower(kind_tok);
    double n_dbl = 0.0, fstart = 0.0, fstop = 0.0;
    if (auto d = eval_value(line_no, n_tok, params, n_dbl); !d.ok()) return d;
    if (auto d = eval_value(line_no, fstart_tok, params, fstart); !d.ok()) return d;
    if (auto d = eval_value(line_no, fstop_tok, params, fstop); !d.ok()) return d;
    const auto n_pts = static_cast<std::size_t>(std::llround(n_dbl));
    if (n_pts == 0u || fstart <= 0.0 || fstop < fstart) {
        return line_err(line_no, StatusCode::InvalidArgument, "invalid frequency range or point count");
    }

    out_freqs.clear();
    if (kind == "lin") {
        if (n_pts == 1u) {
            out_freqs.push_back(fstart);
        } else {
            for (std::size_t i = 0; i < n_pts; ++i) {
                const double t = static_cast<double>(i) / static_cast<double>(n_pts - 1u);
                out_freqs.push_back(fstart + t * (fstop - fstart));
            }
        }
        return Diagnostic::success();
    }
    if (kind == "dec" || kind == "oct") {
        const double base = (kind == "dec") ? 10.0 : 2.0;
        const double span = std::log(fstop / fstart) / std::log(base);
        const std::size_t total =
            std::max<std::size_t>(1u, static_cast<std::size_t>(std::ceil(span * static_cast<double>(n_pts))));
        for (std::size_t i = 0; i <= total; ++i) {
            const double f = fstart * std::pow(base, static_cast<double>(i) / static_cast<double>(n_pts));
            if (f > fstop * (1.0 + 1.0e-12)) break;
            out_freqs.push_back(std::min(f, fstop));
        }
        if (out_freqs.empty() || out_freqs.back() < fstop * (1.0 - 1.0e-12)) {
            out_freqs.push_back(fstop);
        }
        return Diagnostic::success();
    }
    return line_err(line_no, StatusCode::InvalidArgument, "unsupported frequency sweep type '" + kind_tok + "'");
}

/* Parse v(out) or v(out_p, out_n) or bare node name into (out_p, out_n). */
Diagnostic parse_voltage_port_token(
    std::size_t line_no,
    const std::string& tok,
    const std::string& scope_prefix,
    const std::unordered_map<std::string, NodeId>& port_map,
    ElaboratorState& state,
    NodeId& out_p,
    NodeId& out_n) {
    const std::string low = to_lower(tok);
    if (low.size() >= 4u && low[0] == 'v' && low[1] == '(' && low.back() == ')') {
        const std::string inner = low.substr(2u, low.size() - 3u);
        const std::size_t comma = inner.find(',');
        if (comma == std::string::npos) {
            out_p = state.resolve_node(trim(inner), scope_prefix, port_map);
            out_n = NodeId{0};
            return Diagnostic::success();
        }
        out_p = state.resolve_node(trim(inner.substr(0, comma)), scope_prefix, port_map);
        out_n = state.resolve_node(trim(inner.substr(comma + 1u)), scope_prefix, port_map);
        return Diagnostic::success();
    }
    if (!low.empty()) {
        out_p = state.resolve_node(low, scope_prefix, port_map);
        out_n = NodeId{0};
        return Diagnostic::success();
    }
    return line_err(line_no, StatusCode::InvalidArgument, "invalid output port expression '" + tok + "'");
}

/* Preprocess included file lines (does NOT swallow line 1 as a deck title). */
void preprocess_included_lines(const std::string& text, std::vector<LogicalLine>& out_lines) {
    std::istringstream iss(text);
    std::string raw;
    std::size_t line_no = 0;
    while (std::getline(iss, raw)) {
        ++line_no;
        if (!raw.empty() && raw.back() == '\r') raw.pop_back();
        const std::string trimmed = trim(raw);
        if (trimmed.empty() || trimmed[0] == '*') continue;
        if (trimmed[0] == '+' && !out_lines.empty()) {
            out_lines.back().text.push_back(' ');
            out_lines.back().text += trim(trimmed.substr(1u));
        } else {
            out_lines.push_back(LogicalLine{line_no, trimmed});
        }
    }
}

Diagnostic load_include_file(
    std::size_t line_no,
    const std::string& raw_path,
    const std::optional<std::string>& lib_section,
    const std::string& scope_prefix,
    const std::unordered_map<std::string, NodeId>& port_map,
    ParamMap& local_params,
    ElaboratorState& state) {
    namespace fs = std::filesystem;
    fs::path p = unquote(raw_path);
    if (p.is_relative() && !state.parse_opts.base_dir.empty()) {
        p = fs::path(state.parse_opts.base_dir) / p;
    }
    const std::string norm = p.lexically_normal().string();
    if (state.include_stack.size() >= state.parse_opts.max_include_depth) {
        return line_err(line_no, StatusCode::ResourceExhausted, ".INCLUDE/.LIB nesting depth exceeded");
    }
    if (std::find(state.include_stack.begin(), state.include_stack.end(), norm) != state.include_stack.end()) {
        return line_err(line_no, StatusCode::InvalidArgument, "cyclic .INCLUDE/.LIB of '" + norm + "'");
    }
    std::ifstream ifs(p);
    if (!ifs) {
        return line_err(line_no, StatusCode::InvalidArgument, "cannot open .INCLUDE/.LIB file '" + norm + "'");
    }
    std::ostringstream oss;
    oss << ifs.rdbuf();
    std::vector<LogicalLine> inc_lines;
    preprocess_included_lines(oss.str(), inc_lines);

    if (lib_section.has_value()) {
        const std::string want = to_lower(*lib_section);
        std::vector<LogicalLine> sec_lines;
        bool in_sec = false;
        bool found = false;
        for (const auto& l : inc_lines) {
            const auto toks = tokenize_spice_line(l.text);
            if (!toks.empty() && to_lower(toks[0]) == ".lib" && toks.size() >= 2u) {
                if (to_lower(toks[1]) == want) {
                    in_sec = true;
                    found = true;
                }
                continue;
            }
            if (!toks.empty() && to_lower(toks[0]) == ".endl") {
                if (in_sec) in_sec = false;
                continue;
            }
            if (in_sec) sec_lines.push_back(l);
        }
        if (!found) {
            return line_err(line_no, StatusCode::InvalidArgument,
                            ".LIB section '" + *lib_section + "' not found in '" + norm + "'");
        }
        inc_lines = std::move(sec_lines);
    }

    const std::string prev_base = state.parse_opts.base_dir;
    state.parse_opts.base_dir = p.parent_path().string();
    state.include_stack.push_back(norm);
    const auto d = elaborate_lines(inc_lines, scope_prefix, port_map, local_params, state);
    state.include_stack.pop_back();
    state.parse_opts.base_dir = prev_base;
    return d;
}

Diagnostic elaborate_lines(
    const std::vector<LogicalLine>& lines,
    const std::string& scope_prefix,
    const std::unordered_map<std::string, NodeId>& port_map,
    ParamMap local_params,
    ElaboratorState& state) {
    /* First pass in scope: process .INCLUDE, .LIB, .GLOBAL, .PARAM, .OSDI, .MODEL, and .SUBCKT. */
    std::vector<LogicalLine> remaining;
    remaining.reserve(lines.size());

    for (std::size_t idx = 0; idx < lines.size(); ++idx) {
        const auto& line = lines[idx];
        const auto tokens = tokenize_spice_line(line.text);
        if (tokens.empty()) continue;

        if (tokens[0][0] == '.') {
            const std::string cmd = to_lower(tokens[0]);
            if (cmd == ".end") break;
            if (cmd == ".include" || cmd == ".inc") {
                if (tokens.size() < 2u) {
                    return line_err(line.line_no, StatusCode::InvalidArgument, ".INCLUDE requires file path");
                }
                if (auto d = load_include_file(line.line_no, tokens[1], std::nullopt, scope_prefix, port_map, local_params, state);
                    !d.ok()) {
                    return d;
                }
                continue;
            }
            if (cmd == ".lib") {
                if (tokens.size() < 2u) {
                    return line_err(line.line_no, StatusCode::InvalidArgument, ".LIB requires file path [section]");
                }
                const std::optional<std::string> sec = (tokens.size() >= 3u) ? std::optional<std::string>{tokens[2]} : std::nullopt;
                if (auto d = load_include_file(line.line_no, tokens[1], sec, scope_prefix, port_map, local_params, state);
                    !d.ok()) {
                    return d;
                }
                continue;
            }
            if (cmd == ".global") {
                for (std::size_t i = 1u; i < tokens.size(); ++i) {
                    state.global_nodes.insert(to_lower(tokens[i]));
                }
                continue;
            }
            if (cmd == ".param") {
                if (auto d = parse_param_assignments(line.line_no, tokens, 1u, local_params); !d.ok()) return d;
                if (scope_prefix.empty()) {
                    for (const auto& [k, v] : local_params) state.global_params[k] = v;
                }
                continue;
            }
            if (cmd == ".osdi") {
                if (tokens.size() < 2u) {
                    return line_err(line.line_no, StatusCode::InvalidArgument, ".OSDI requires library path");
                }
                state.registered_osdi_libs.push_back(unquote(tokens[1]));
                continue;
            }
            if (cmd == ".model") {
                if (auto d = parse_model_directive(line.line_no, tokens, local_params, state); !d.ok()) return d;
                continue;
            }
            if (cmd == ".subckt") {
                if (tokens.size() < 3u) {
                    return line_err(line.line_no, StatusCode::InvalidArgument, ".SUBCKT requires name and at least one port");
                }
                SubcktDef def{};
                def.name = to_lower(tokens[1]);
                bool in_params = false;
                for (std::size_t i = 2u; i < tokens.size(); ++i) {
                    if (to_lower(tokens[i]) == "params:") {
                        in_params = true;
                        continue;
                    }
                    const std::size_t eq = tokens[i].find('=');
                    if (in_params || eq != std::string::npos) {
                        if (eq == std::string::npos) {
                            return line_err(line.line_no, StatusCode::InvalidArgument, "expected k=v in .SUBCKT PARAMS:");
                        }
                        def.default_params.emplace_back(to_lower(tokens[i].substr(0, eq)), tokens[i].substr(eq + 1u));
                    } else {
                        def.ports.push_back(to_lower(tokens[i]));
                    }
                }
                bool found_ends = false;
                int depth = 1;
                while (++idx < lines.size()) {
                    const auto body_toks = tokenize_spice_line(lines[idx].text);
                    if (!body_toks.empty()) {
                        const std::string bcmd = to_lower(body_toks[0]);
                        if (bcmd == ".subckt") ++depth;
                        else if (bcmd == ".ends") {
                            --depth;
                            if (depth == 0) {
                                found_ends = true;
                                break;
                            }
                        }
                    }
                    def.body.push_back(lines[idx]);
                }
                if (!found_ends) {
                    return line_err(line.line_no, StatusCode::InvalidArgument, "unterminated .SUBCKT '" + def.name + "' (missing .ENDS)");
                }
                state.subckts[def.name] = std::move(def);
                continue;
            }
            if (cmd == ".ends") {
                return line_err(line.line_no, StatusCode::InvalidArgument, "stray .ENDS without matching .SUBCKT");
            }
            remaining.push_back(line);
        } else {
            remaining.push_back(line);
        }
    }

    /* Second pass: elaborate elements first, then top-level analysis & control directives. */
    std::vector<LogicalLine> control_lines;
    for (const auto& line : remaining) {
        const auto tokens = tokenize_spice_line(line.text);
        if (tokens.empty()) continue;
        if (tokens[0][0] == '.') {
            control_lines.push_back(line);
        } else {
            if (auto d = elaborate_element_line(line, tokens, scope_prefix, port_map, local_params, state); !d.ok()) {
                return d;
            }
        }
    }

    for (const auto& line : control_lines) {
        const auto tokens = tokenize_spice_line(line.text);
        if (tokens.empty()) continue;
        const std::string cmd = to_lower(tokens[0]);
        if (!scope_prefix.empty()) {
            return line_err(line.line_no, StatusCode::InvalidArgument, "analysis/control directive '" + cmd + "' not allowed inside .SUBCKT");
        }

        if (cmd == ".op") {
            state.analyses.push_back(OpCommand{});
        } else if (cmd == ".dc") {
            if (tokens.size() != 5u && tokens.size() != 9u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".DC requires <src> <start> <stop> <step> [src2 start2 stop2 step2]");
            }
            DcCommand dc{};
            dc.source_id = tokens[1];
            if (auto d = eval_value(line.line_no, tokens[2], local_params, dc.start); !d.ok()) return d;
            if (auto d = eval_value(line.line_no, tokens[3], local_params, dc.stop); !d.ok()) return d;
            if (auto d = eval_value(line.line_no, tokens[4], local_params, dc.step); !d.ok()) return d;
            if (tokens.size() == 9u) {
                dc.source2_id = tokens[5];
                if (auto d = eval_value(line.line_no, tokens[6], local_params, dc.start2); !d.ok()) return d;
                if (auto d = eval_value(line.line_no, tokens[7], local_params, dc.stop2); !d.ok()) return d;
                if (auto d = eval_value(line.line_no, tokens[8], local_params, dc.step2); !d.ok()) return d;
            }
            state.analyses.push_back(std::move(dc));
        } else if (cmd == ".ac") {
            if (tokens.size() != 5u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".AC requires <LIN|DEC|OCT> <n> <fstart> <fstop>");
            }
            AcCommand ac{};
            if (auto d = parse_frequency_grid(line.line_no, tokens[1], tokens[2], tokens[3], tokens[4], local_params, ac.frequencies_hz);
                !d.ok()) {
                return d;
            }
            state.analyses.push_back(std::move(ac));
        } else if (cmd == ".tran") {
            if (tokens.size() < 3u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".TRAN requires <tstep> <tstop> [tstart [tmax]] [UIC]");
            }
            TranCommand tr{};
            if (auto d = eval_value(line.line_no, tokens[1], local_params, tr.step_s); !d.ok()) return d;
            if (auto d = eval_value(line.line_no, tokens[2], local_params, tr.stop_s); !d.ok()) return d;
            int numeric_extras = 0;
            for (std::size_t i = 3u; i < tokens.size(); ++i) {
                if (to_lower(tokens[i]) == "uic") {
                    tr.uic = true;
                } else {
                    double v = 0.0;
                    if (auto d = eval_value(line.line_no, tokens[i], local_params, v); !d.ok()) return d;
                    ++numeric_extras;
                    if (numeric_extras == 2) tr.max_step_s = v;
                }
            }
            state.analyses.push_back(std::move(tr));
        } else if (cmd == ".tf") {
            if (tokens.size() != 3u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".TF requires v(out[,ref]) <input_src>");
            }
            TfCommand tf{};
            if (auto d = parse_voltage_port_token(line.line_no, tokens[1], scope_prefix, port_map, state, tf.out_p, tf.out_n); !d.ok()) return d;
            tf.input_source_id = tokens[2];
            state.analyses.push_back(std::move(tf));
        } else if (cmd == ".noise") {
            if (tokens.size() != 7u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".NOISE requires v(out[,ref]) <src> <LIN|DEC|OCT> <n> <fstart> <fstop>");
            }
            NoiseCommand ns{};
            if (auto d = parse_voltage_port_token(line.line_no, tokens[1], scope_prefix, port_map, state, ns.out_p, ns.out_n); !d.ok()) return d;
            ns.input_source_id = tokens[2];
            if (auto d = parse_frequency_grid(line.line_no, tokens[3], tokens[4], tokens[5], tokens[6], local_params, ns.frequencies_hz);
                !d.ok()) {
                return d;
            }
            state.analyses.push_back(std::move(ns));
        } else if (cmd == ".sens") {
            if (tokens.size() < 2u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".SENS requires v(out[,ref])");
            }
            SensCommand sn{};
            if (auto d = parse_voltage_port_token(line.line_no, tokens[1], scope_prefix, port_map, state, sn.out_p, sn.out_n); !d.ok()) return d;
            state.analyses.push_back(std::move(sn));
        } else if (cmd == ".dcmatch") {
            if (tokens.size() < 2u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".DCMATCH requires v(out[,ref]) [sigma=<val>]");
            }
            DcMatchCommand dm{};
            if (auto d = parse_voltage_port_token(line.line_no, tokens[1], scope_prefix, port_map, state, dm.out_p, dm.out_n); !d.ok()) return d;
            for (std::size_t i = 2u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                if (eq != std::string::npos && to_lower(tokens[i].substr(0, eq)) == "sigma") {
                    if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), local_params, dm.resistor_rel_sigma); !d.ok()) return d;
                }
            }
            state.analyses.push_back(std::move(dm));
        } else if (cmd == ".sp") {
            if (tokens.size() < 6u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".SP requires <LIN|DEC|OCT> <n> <fstart> <fstop> PORT=n1 [PORT=n2] [Z0=50]");
            }
            SpCommand sp{};
            if (auto d = parse_frequency_grid(line.line_no, tokens[1], tokens[2], tokens[3], tokens[4], local_params, sp.frequencies_hz);
                !d.ok()) {
                return d;
            }
            double z0 = 50.0;
            for (std::size_t i = 5u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                if (eq != std::string::npos) {
                    const std::string k = to_lower(tokens[i].substr(0, eq));
                    const std::string val = tokens[i].substr(eq + 1u);
                    if (k == "z0" || k == "r0") {
                        if (auto d = eval_value(line.line_no, val, local_params, z0); !d.ok()) return d;
                    } else if (k == "port" || k == "p") {
                        NodeId pp{0}, pn{0};
                        if (auto d = parse_voltage_port_token(line.line_no, val, scope_prefix, port_map, state, pp, pn); !d.ok()) return d;
                        sp.ports.push_back(SpPortDecl{pp, pn, z0});
                    }
                } else {
                    NodeId pp{0}, pn{0};
                    if (auto d = parse_voltage_port_token(line.line_no, tokens[i], scope_prefix, port_map, state, pp, pn); !d.ok()) return d;
                    sp.ports.push_back(SpPortDecl{pp, pn, z0});
                }
            }
            for (auto& pt : sp.ports) pt.z0_ohms = z0;
            if (sp.ports.empty()) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".SP requires at least one PORT");
            }
            state.analyses.push_back(std::move(sp));
        } else if (cmd == ".stb") {
            if (tokens.size() != 6u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".STB requires <probe_vsrc> <LIN|DEC|OCT> <n> <fstart> <fstop>");
            }
            StbCommand stb{};
            stb.probe_source_id = tokens[1];
            if (auto d = parse_frequency_grid(line.line_no, tokens[2], tokens[3], tokens[4], tokens[5], local_params, stb.frequencies_hz);
                !d.ok()) {
                return d;
            }
            state.analyses.push_back(std::move(stb));
        } else if (cmd == ".pz") {
            if (tokens.size() < 5u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".PZ requires <in_p> <in_n> <out_p> <out_n> [VOL|CUR] [PZ|POL|ZER]");
            }
            PzCommand pz{};
            pz.in_p = state.resolve_node(tokens[1], scope_prefix, port_map);
            pz.in_n = state.resolve_node(tokens[2], scope_prefix, port_map);
            pz.out_p = state.resolve_node(tokens[3], scope_prefix, port_map);
            pz.out_n = state.resolve_node(tokens[4], scope_prefix, port_map);
            for (std::size_t i = 5u; i < tokens.size(); ++i) {
                const std::string t = to_lower(tokens[i]);
                if (t == "pol") { pz.compute_poles = true; pz.compute_zeros = false; }
                else if (t == "zer") { pz.compute_poles = false; pz.compute_zeros = true; }
                else if (t == "pz") { pz.compute_poles = true; pz.compute_zeros = true; }
            }
            state.analyses.push_back(std::move(pz));
        } else if (cmd == ".four") {
            if (tokens.size() < 3u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".FOUR requires <freq_hz> <signal> [harmonics]");
            }
            FourCommand f{};
            if (auto d = eval_value(line.line_no, tokens[1], local_params, f.fundamental_hz); !d.ok()) return d;
            f.signal_id = to_lower(tokens[2]);
            if (tokens.size() >= 4u) {
                double nh = 9.0;
                if (auto d = eval_value(line.line_no, tokens[3], local_params, nh); !d.ok()) return d;
                f.harmonics = static_cast<std::size_t>(std::max(1.0, std::round(nh)));
            }
            state.analyses.push_back(std::move(f));
        } else if (cmd == ".fft") {
            if (tokens.size() < 2u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".FFT requires <signal> [N=1024]");
            }
            FftCommand f{};
            f.signal_id = to_lower(tokens[1]);
            for (std::size_t i = 2u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                const std::string vstr = (eq == std::string::npos) ? tokens[i] : tokens[i].substr(eq + 1u);
                double n_val = 1024.0;
                if (auto d = eval_value(line.line_no, vstr, local_params, n_val); !d.ok()) return d;
                f.n_points = static_cast<std::size_t>(std::llround(n_val));
            }
            state.analyses.push_back(std::move(f));
        } else if (cmd == ".mc") {
            if (tokens.size() < 2u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".MC requires <runs> [seed=42] [sigma=0.05]");
            }
            McCommand mc{};
            double runs_d = 16.0;
            if (auto d = eval_value(line.line_no, tokens[1], local_params, runs_d); !d.ok()) return d;
            mc.runs = static_cast<std::size_t>(std::max(1.0, std::round(runs_d)));
            for (std::size_t i = 2u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                if (eq != std::string::npos) {
                    const std::string k = to_lower(tokens[i].substr(0, eq));
                    double v = 0.0;
                    if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), local_params, v); !d.ok()) return d;
                    if (k == "seed") mc.seed = static_cast<std::uint64_t>(std::llround(v));
                    else if (k == "sigma") mc.resistor_rel_sigma = v;
                }
            }
            state.analyses.push_back(std::move(mc));
        } else if (cmd == ".hb") {
            if (tokens.size() < 2u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".HB requires <freq_hz> [H=7]");
            }
            HbCommand hb{};
            if (auto d = eval_value(line.line_no, tokens[1], local_params, hb.fundamental_hz); !d.ok()) return d;
            for (std::size_t i = 2u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                const std::string vstr = (eq == std::string::npos) ? tokens[i] : tokens[i].substr(eq + 1u);
                double h_val = 7.0;
                if (auto d = eval_value(line.line_no, vstr, local_params, h_val); !d.ok()) return d;
                hb.harmonics = static_cast<std::size_t>(std::max(1.0, std::round(h_val)));
            }
            state.analyses.push_back(std::move(hb));
        } else if (cmd == ".pss") {
            if (tokens.size() < 2u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".PSS requires <freq_hz> [STEPS=64]");
            }
            PssCommand pss{};
            if (auto d = eval_value(line.line_no, tokens[1], local_params, pss.fundamental_hz); !d.ok()) return d;
            for (std::size_t i = 2u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                const std::string vstr = (eq == std::string::npos) ? tokens[i] : tokens[i].substr(eq + 1u);
                double s_val = 64.0;
                if (auto d = eval_value(line.line_no, vstr, local_params, s_val); !d.ok()) return d;
                pss.steps_per_period = static_cast<std::size_t>(std::max(4.0, std::round(s_val)));
            }
            state.analyses.push_back(std::move(pss));
        } else if (cmd == ".pac") {
            if (tokens.size() < 3u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".PAC requires <f_small_hz> <input_src>");
            }
            PacCommand pac{};
            if (auto d = eval_value(line.line_no, tokens[1], local_params, pac.small_signal_freq_hz); !d.ok()) return d;
            pac.input_source_id = tokens[2];
            state.analyses.push_back(std::move(pac));
        } else if (cmd == ".pxf") {
            if (tokens.size() < 3u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".PXF requires <f_small_hz> v(out[,ref]) [SB=0]");
            }
            PxfCommand pxf{};
            if (auto d = eval_value(line.line_no, tokens[1], local_params, pxf.small_signal_freq_hz); !d.ok()) return d;
            if (auto d = parse_voltage_port_token(line.line_no, tokens[2], scope_prefix, port_map, state, pxf.out_p, pxf.out_n); !d.ok()) return d;
            if (tokens.size() >= 4u) {
                const std::size_t eq = tokens[3].find('=');
                const std::string vstr = (eq == std::string::npos) ? tokens[3] : tokens[3].substr(eq + 1u);
                double sb_val = 0.0;
                if (auto d = eval_value(line.line_no, vstr, local_params, sb_val); !d.ok()) return d;
                pxf.output_sideband = static_cast<int>(std::llround(sb_val));
            }
            state.analyses.push_back(std::move(pxf));
        } else if (cmd == ".pnoise") {
            if (tokens.size() < 3u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".PNOISE requires <f_small_hz> v(out[,ref]) [SB=0]");
            }
            PnoiseCommand pn{};
            if (auto d = eval_value(line.line_no, tokens[1], local_params, pn.small_signal_freq_hz); !d.ok()) return d;
            if (auto d = parse_voltage_port_token(line.line_no, tokens[2], scope_prefix, port_map, state, pn.out_p, pn.out_n); !d.ok()) return d;
            if (tokens.size() >= 4u) {
                const std::size_t eq = tokens[3].find('=');
                const std::string vstr = (eq == std::string::npos) ? tokens[3] : tokens[3].substr(eq + 1u);
                double sb_val = 0.0;
                if (auto d = eval_value(line.line_no, vstr, local_params, sb_val); !d.ok()) return d;
                pn.output_sideband = static_cast<int>(std::llround(sb_val));
            }
            state.analyses.push_back(std::move(pn));
        } else if (cmd == ".temp") {
            if (tokens.size() < 2u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".TEMP requires temperature in Celsius");
            }
            if (auto d = eval_value(line.line_no, tokens[1], local_params, state.options.temperature_c); !d.ok()) return d;
        } else if (cmd == ".ic" || cmd == ".nodeset") {
            for (std::size_t i = 1u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                if (eq == std::string::npos) continue;
                NodeId np{0}, nn{0};
                if (auto d = parse_voltage_port_token(line.line_no, tokens[i].substr(0, eq), scope_prefix, port_map, state, np, nn); !d.ok()) return d;
                double val = 0.0;
                if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), local_params, val); !d.ok()) return d;
                if (cmd == ".ic") state.options.initial_conditions.emplace_back(np, val);
                else state.options.nodesets.emplace_back(np, val);
            }
        } else if (cmd == ".save" || cmd == ".probe" || cmd == ".print" || cmd == ".plot") {
            for (std::size_t i = 1u; i < tokens.size(); ++i) {
                state.options.saved_signals.push_back(to_lower(tokens[i]));
            }
        } else if (cmd == ".meas" || cmd == ".measure") {
            if (tokens.size() < 5u) {
                return line_err(line.line_no, StatusCode::InvalidArgument, ".MEAS requires <domain> <name> <MAX|MIN|AVG|PP|FIND> <signal> [AT=<x>]");
            }
            MeasCommand mc{};
            mc.domain = to_lower(tokens[1]);
            mc.name = tokens[2];
            mc.func = to_lower(tokens[3]);
            mc.signal = to_lower(tokens[4]);
            for (std::size_t i = 5u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                if (eq != std::string::npos && to_lower(tokens[i].substr(0, eq)) == "at") {
                    double at_val = 0.0;
                    if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), local_params, at_val); !d.ok()) return d;
                    mc.at_x = at_val;
                }
            }
            state.measurements.push_back(std::move(mc));
        } else if (cmd == ".options") {
            for (std::size_t i = 1u; i < tokens.size(); ++i) {
                const std::size_t eq = tokens[i].find('=');
                if (eq == std::string::npos) continue;
                const std::string k = to_lower(tokens[i].substr(0, eq));
                const std::string v = to_lower(tokens[i].substr(eq + 1u));
                if (k == "method") {
                    if (v == "trap" || v == "trapezoidal") state.options.method = "trap";
                    else if (v == "be" || v == "euler") state.options.method = "be";
                    else if (v == "gear" || v == "bdf2") state.options.method = "bdf2";
                    else return line_err(line.line_no, StatusCode::InvalidArgument, "unsupported .OPTIONS method=" + v);
                } else if (k == "reltol") {
                    double rt = 0.0;
                    if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), local_params, rt); !d.ok()) return d;
                    state.options.reltol = rt;
                } else if (k == "temp") {
                    if (auto d = eval_value(line.line_no, tokens[i].substr(eq + 1u), local_params, state.options.temperature_c); !d.ok()) return d;
                }
            }
        } else {
            return line_err(line.line_no, StatusCode::Unsupported, "unsupported SPICE directive '" + tokens[0] + "'");
        }
    }
    return Diagnostic::success();
}

}  // namespace

Diagnostic parse_spice_deck(
    const std::string& netlist_text,
    ParsedDeck& out_deck,
    const ParseOptions& opts) {
    ElaboratorState state{};
    state.parse_opts = opts;

    std::vector<LogicalLine> lines;
    std::string title;
    preprocess_lines(netlist_text, title, lines);
    state.circuit.name = title.empty() ? "unnamed_deck" : title;

    std::unordered_map<std::string, NodeId> empty_ports;
    if (auto d = elaborate_lines(lines, "", empty_ports, state.global_params, state); !d.ok()) {
        return d;
    }
    if (auto d = state.circuit.validate(opts.max_unknowns); !d.ok()) {
        return d;
    }

    ParsedDeck result{};
    result.title = std::move(title);
    result.circuit = std::move(state.circuit);
    result.analyses = std::move(state.analyses);
    result.measurements = std::move(state.measurements);
    result.options = std::move(state.options);
    out_deck = std::move(result);
    return Diagnostic::success();
}

}  // namespace spice::parser
