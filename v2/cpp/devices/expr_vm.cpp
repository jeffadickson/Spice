#include "devices/expr_vm.hpp"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdlib>
#include <limits>
#include <string_view>

namespace spice::devices {

namespace {

using core::Diagnostic;
using core::StatusCode;
using core::UnknownId;

constexpr std::size_t kMaxExprDepth = 64u;
constexpr std::size_t kMaxInstructions = 1024u;

std::string to_lower_ascii(std::string_view sv) {
    std::string out(sv.size(), '\0');
    for (std::size_t i = 0; i < sv.size(); ++i) {
        out[i] = static_cast<char>(std::tolower(static_cast<unsigned char>(sv[i])));
    }
    return out;
}

class ExprParser {
public:
    ExprParser(
        std::string_view src,
        const CompiledExpr::ResolveNodeFn& resolve_node,
        const CompiledExpr::ResolveBranchFn& resolve_branch,
        std::vector<CompiledExpr::Instruction>& code,
        std::vector<UnknownId>& refs)
        : src_(src),
          resolve_node_(resolve_node),
          resolve_branch_(resolve_branch),
          code_(code),
          refs_(refs) {}

    Diagnostic parse() {
        skip_ws();
        if (eof()) {
            return Diagnostic::error(StatusCode::InvalidArgument, "empty behavioral expression");
        }
        if (auto d = parse_add_sub(0); !d.ok()) return d;
        skip_ws();
        if (!eof()) {
            return Diagnostic::error(
                StatusCode::InvalidArgument, "unexpected trailing token in expression");
        }
        return Diagnostic::success();
    }

private:
    bool eof() const noexcept { return pos_ >= src_.size(); }
    char peek() const noexcept { return eof() ? '\0' : src_[pos_]; }

    void skip_ws() noexcept {
        while (!eof() && std::isspace(static_cast<unsigned char>(src_[pos_]))) {
            ++pos_;
        }
    }

    Diagnostic emit(CompiledExpr::Instruction ins) {
        if (code_.size() >= kMaxInstructions) {
            return Diagnostic::error(
                StatusCode::ResourceExhausted, "behavioral expression exceeds instruction budget");
        }
        code_.push_back(ins);
        return Diagnostic::success();
    }

    std::uint32_t intern_unknown(UnknownId u) {
        for (std::uint32_t i = 0; i < refs_.size(); ++i) {
            if (refs_[i] == u) return i;
        }
        const auto idx = static_cast<std::uint32_t>(refs_.size());
        refs_.push_back(u);
        return idx;
    }

    Diagnostic parse_add_sub(std::size_t depth) {
        if (depth > kMaxExprDepth) {
            return Diagnostic::error(StatusCode::ResourceExhausted, "expression nesting too deep");
        }
        if (auto d = parse_mul_div(depth + 1); !d.ok()) return d;
        while (true) {
            skip_ws();
            const char ch = peek();
            if (ch == '+' || ch == '-') {
                ++pos_;
                if (auto d = parse_mul_div(depth + 1); !d.ok()) return d;
                if (auto d = emit({ch == '+' ? CompiledExpr::Op::Add : CompiledExpr::Op::Sub, 0, 0.0});
                    !d.ok()) {
                    return d;
                }
            } else {
                break;
            }
        }
        return Diagnostic::success();
    }

    Diagnostic parse_mul_div(std::size_t depth) {
        if (auto d = parse_unary(depth + 1); !d.ok()) return d;
        while (true) {
            skip_ws();
            const char ch = peek();
            if (ch == '*' || ch == '/') {
                ++pos_;
                if (auto d = parse_unary(depth + 1); !d.ok()) return d;
                if (auto d = emit({ch == '*' ? CompiledExpr::Op::Mul : CompiledExpr::Op::Div, 0, 0.0});
                    !d.ok()) {
                    return d;
                }
            } else {
                break;
            }
        }
        return Diagnostic::success();
    }

    Diagnostic parse_unary(std::size_t depth) {
        if (depth > kMaxExprDepth) {
            return Diagnostic::error(StatusCode::ResourceExhausted, "expression nesting too deep");
        }
        skip_ws();
        if (peek() == '+') {
            ++pos_;
            return parse_unary(depth + 1);
        }
        if (peek() == '-') {
            ++pos_;
            if (auto d = parse_unary(depth + 1); !d.ok()) return d;
            return emit({CompiledExpr::Op::Neg, 0, 0.0});
        }
        return parse_power(depth + 1);
    }

    Diagnostic parse_power(std::size_t depth) {
        if (auto d = parse_primary(depth + 1); !d.ok()) return d;
        skip_ws();
        if (peek() == '^') {
            ++pos_;
            if (auto d = parse_unary(depth + 1); !d.ok()) return d;
            return emit({CompiledExpr::Op::Pow, 0, 0.0});
        }
        return Diagnostic::success();
    }

    std::string parse_ident() {
        skip_ws();
        const std::size_t start = pos_;
        while (!eof()) {
            const unsigned char c = static_cast<unsigned char>(src_[pos_]);
            if (std::isalnum(c) || c == '_' || c == '.' || c == ':') {
                ++pos_;
            } else {
                break;
            }
        }
        return std::string(src_.substr(start, pos_ - start));
    }

    Diagnostic parse_primary(std::size_t depth) {
        if (depth > kMaxExprDepth) {
            return Diagnostic::error(StatusCode::ResourceExhausted, "expression nesting too deep");
        }
        skip_ws();
        if (eof()) {
            return Diagnostic::error(StatusCode::InvalidArgument, "unexpected end of expression");
        }
        const char ch = peek();
        if (ch == '(') {
            ++pos_;
            if (auto d = parse_add_sub(depth + 1); !d.ok()) return d;
            skip_ws();
            if (peek() != ')') {
                return Diagnostic::error(StatusCode::InvalidArgument, "missing closing ')'");
            }
            ++pos_;
            return Diagnostic::success();
        }
        if (std::isdigit(static_cast<unsigned char>(ch)) || ch == '.') {
            return parse_number();
        }
        if (std::isalpha(static_cast<unsigned char>(ch)) || ch == '_') {
            return parse_symbol_or_call(depth);
        }
        return Diagnostic::error(StatusCode::InvalidArgument, "unexpected character in expression");
    }

    Diagnostic parse_number() {
        const char* start_ptr = src_.data() + pos_;
        char* end_ptr = nullptr;
        double val = std::strtod(start_ptr, &end_ptr);
        if (end_ptr == start_ptr || !std::isfinite(val)) {
            return Diagnostic::error(StatusCode::InvalidArgument, "invalid numeric literal");
        }
        pos_ = static_cast<std::size_t>(end_ptr - src_.data());

        /* Optional SPICE engineering suffix (case-insensitive). */
        const std::size_t suf_start = pos_;
        while (!eof() && std::isalpha(static_cast<unsigned char>(src_[pos_]))) {
            ++pos_;
        }
        if (pos_ > suf_start) {
            const std::string suf = to_lower_ascii(src_.substr(suf_start, pos_ - suf_start));
            double scale = 1.0;
            if (suf == "f") scale = 1e-15;
            else if (suf == "p") scale = 1e-12;
            else if (suf == "n") scale = 1e-9;
            else if (suf == "u") scale = 1e-6;
            else if (suf == "m") scale = 1e-3;
            else if (suf == "k") scale = 1e3;
            else if (suf == "meg") scale = 1e6;
            else if (suf == "g") scale = 1e9;
            else if (suf == "t") scale = 1e12;
            else {
                return Diagnostic::error(StatusCode::InvalidArgument, "unknown engineering suffix: " + suf);
            }
            val *= scale;
        }
        return emit({CompiledExpr::Op::PushConst, 0, val});
    }

    Diagnostic parse_symbol_or_call(std::size_t depth) {
        const std::string raw = parse_ident();
        const std::string lower = to_lower_ascii(raw);
        skip_ws();

        if (peek() != '(') {
            if (lower == "time") {
                return emit({CompiledExpr::Op::PushTime, 0, 0.0});
            }
            if (lower == "pi") {
                return emit({CompiledExpr::Op::PushConst, 0, 3.14159265358979323846});
            }
            return Diagnostic::error(StatusCode::InvalidArgument, "unknown symbol: " + raw);
        }

        /* Function or signal reference: name(...) */
        ++pos_; /* consume '(' */

        if (lower == "v") {
            const std::string n1 = parse_ident();
            skip_ws();
            std::int32_t u1 = -1;
            if (n1.empty() || !resolve_node_(n1, u1)) {
                return Diagnostic::error(StatusCode::InvalidArgument, "unknown node in v(): " + n1);
            }
            if (u1 < 0) {
                if (auto d = emit({CompiledExpr::Op::PushConst, 0, 0.0}); !d.ok()) return d;
            } else {
                const auto slot = intern_unknown(UnknownId{static_cast<std::uint32_t>(u1)});
                if (auto d = emit({CompiledExpr::Op::PushUnknown, slot, 0.0}); !d.ok()) return d;
            }
            if (peek() == ',') {
                ++pos_;
                const std::string n2 = parse_ident();
                skip_ws();
                std::int32_t u2 = -1;
                if (n2.empty() || !resolve_node_(n2, u2)) {
                    return Diagnostic::error(StatusCode::InvalidArgument, "unknown second node in v(): " + n2);
                }
                if (u2 < 0) {
                    if (auto d = emit({CompiledExpr::Op::PushConst, 0, 0.0}); !d.ok()) return d;
                } else {
                    const auto slot = intern_unknown(UnknownId{static_cast<std::uint32_t>(u2)});
                    if (auto d = emit({CompiledExpr::Op::PushUnknown, slot, 0.0}); !d.ok()) return d;
                }
                if (auto d = emit({CompiledExpr::Op::Sub, 0, 0.0}); !d.ok()) return d;
            }
            if (peek() != ')') {
                return Diagnostic::error(StatusCode::InvalidArgument, "missing ')' in v()");
            }
            ++pos_;
            return Diagnostic::success();
        }

        if (lower == "i") {
            const std::string br_name = parse_ident();
            skip_ws();
            if (peek() != ')') {
                return Diagnostic::error(StatusCode::InvalidArgument, "missing ')' in i()");
            }
            ++pos_;
            std::uint32_t u = 0;
            if (br_name.empty() || !resolve_branch_(br_name, u)) {
                return Diagnostic::error(StatusCode::InvalidArgument, "unknown branch in i(): " + br_name);
            }
            const auto slot = intern_unknown(UnknownId{u});
            return emit({CompiledExpr::Op::PushUnknown, slot, 0.0});
        }

        /* Unary math functions */
        auto parse_unary_fn = [&](CompiledExpr::Op op) -> Diagnostic {
            if (auto d = parse_add_sub(depth + 1); !d.ok()) return d;
            skip_ws();
            if (peek() != ')') {
                return Diagnostic::error(StatusCode::InvalidArgument, "missing ')' in function call");
            }
            ++pos_;
            return emit({op, 0, 0.0});
        };
        /* Binary math functions */
        auto parse_binary_fn = [&](CompiledExpr::Op op) -> Diagnostic {
            if (auto d = parse_add_sub(depth + 1); !d.ok()) return d;
            skip_ws();
            if (peek() != ',') {
                return Diagnostic::error(StatusCode::InvalidArgument, "expected ',' in binary function");
            }
            ++pos_;
            if (auto d = parse_add_sub(depth + 1); !d.ok()) return d;
            skip_ws();
            if (peek() != ')') {
                return Diagnostic::error(StatusCode::InvalidArgument, "missing ')' in binary function");
            }
            ++pos_;
            return emit({op, 0, 0.0});
        };

        if (lower == "sin") return parse_unary_fn(CompiledExpr::Op::Sin);
        if (lower == "cos") return parse_unary_fn(CompiledExpr::Op::Cos);
        if (lower == "tan") return parse_unary_fn(CompiledExpr::Op::Tan);
        if (lower == "exp") return parse_unary_fn(CompiledExpr::Op::Exp);
        if (lower == "log" || lower == "ln") return parse_unary_fn(CompiledExpr::Op::Log);
        if (lower == "sqrt") return parse_unary_fn(CompiledExpr::Op::Sqrt);
        if (lower == "abs") return parse_unary_fn(CompiledExpr::Op::Abs);
        if (lower == "tanh") return parse_unary_fn(CompiledExpr::Op::Tanh);
        if (lower == "pow") return parse_binary_fn(CompiledExpr::Op::Pow);
        if (lower == "min") return parse_binary_fn(CompiledExpr::Op::Min);
        if (lower == "max") return parse_binary_fn(CompiledExpr::Op::Max);

        return Diagnostic::error(StatusCode::InvalidArgument, "unsupported function: " + raw);
    }

    std::string_view src_;
    std::size_t pos_{0};
    const CompiledExpr::ResolveNodeFn& resolve_node_;
    const CompiledExpr::ResolveBranchFn& resolve_branch_;
    std::vector<CompiledExpr::Instruction>& code_;
    std::vector<UnknownId>& refs_;
};

}  // namespace

Diagnostic CompiledExpr::compile(
    const std::string& source,
    const ResolveNodeFn& resolve_node,
    const ResolveBranchFn& resolve_branch,
    CompiledExpr& out_expr) {
    CompiledExpr expr;
    ExprParser parser(source, resolve_node, resolve_branch, expr.code_, expr.referenced_unknowns_);
    if (auto d = parser.parse(); !d.ok()) return d;
    out_expr = std::move(expr);
    return Diagnostic::success();
}

Diagnostic CompiledExpr::evaluate(
    std::span<const double> x,
    double time_s,
    ExprEvalResult& out_result) const {
    const std::size_t n_refs = referenced_unknowns_.size();

    /* Vector-mode forward autodiff: each stack entry carries (val, d/dx_ref[0..n_refs-1]). */
    struct Jet {
        double val{0.0};
        std::vector<double> d;
    };

    std::vector<Jet> stack;
    stack.reserve(16);

    auto make_const = [&](double v) -> Jet { return Jet{v, std::vector<double>(n_refs, 0.0)}; };

    for (const auto& ins : code_) {
        switch (ins.op) {
            case Op::PushConst:
                stack.push_back(make_const(ins.imm));
                break;
            case Op::PushTime:
                stack.push_back(make_const(time_s));
                break;
            case Op::PushUnknown: {
                const UnknownId u = referenced_unknowns_[ins.operand];
                if (u.value >= x.size() || !std::isfinite(x[u.value])) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "invalid unknown in expression");
                }
                Jet j = make_const(x[u.value]);
                j.d[ins.operand] = 1.0;
                stack.push_back(std::move(j));
                break;
            }
            case Op::Neg: {
                auto& a = stack.back();
                a.val = -a.val;
                for (double& dk : a.d) dk = -dk;
                break;
            }
            case Op::Add: {
                Jet b = std::move(stack.back());
                stack.pop_back();
                auto& a = stack.back();
                a.val += b.val;
                for (std::size_t k = 0; k < n_refs; ++k) a.d[k] += b.d[k];
                break;
            }
            case Op::Sub: {
                Jet b = std::move(stack.back());
                stack.pop_back();
                auto& a = stack.back();
                a.val -= b.val;
                for (std::size_t k = 0; k < n_refs; ++k) a.d[k] -= b.d[k];
                break;
            }
            case Op::Mul: {
                Jet b = std::move(stack.back());
                stack.pop_back();
                auto& a = stack.back();
                const double av = a.val, bv = b.val;
                a.val = av * bv;
                for (std::size_t k = 0; k < n_refs; ++k) a.d[k] = a.d[k] * bv + av * b.d[k];
                break;
            }
            case Op::Div: {
                Jet b = std::move(stack.back());
                stack.pop_back();
                auto& a = stack.back();
                if (b.val == 0.0) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "division by zero in expression");
                }
                const double av = a.val, bv = b.val;
                a.val = av / bv;
                const double inv_b2 = 1.0 / (bv * bv);
                for (std::size_t k = 0; k < n_refs; ++k) {
                    a.d[k] = (a.d[k] * bv - av * b.d[k]) * inv_b2;
                }
                break;
            }
            case Op::Pow: {
                Jet b = std::move(stack.back());
                stack.pop_back();
                auto& a = stack.back();
                const double av = a.val, bv = b.val;
                const double pv = std::pow(av, bv);
                if (!std::isfinite(pv)) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite pow() in expression");
                }
                a.val = pv;
                const bool b_has_deriv = std::any_of(
                    b.d.begin(), b.d.end(), [](double v) { return v != 0.0; });
                const double d_da = (bv == 0.0) ? 0.0 : bv * std::pow(av, bv - 1.0);
                const double d_db = b_has_deriv ? (pv * std::log(av)) : 0.0;
                if (!std::isfinite(d_da) || (b_has_deriv && !std::isfinite(d_db))) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite pow() derivative");
                }
                for (std::size_t k = 0; k < n_refs; ++k) {
                    a.d[k] = d_da * a.d[k] + (b_has_deriv ? d_db * b.d[k] : 0.0);
                }
                break;
            }
            case Op::Sin: {
                auto& a = stack.back();
                const double c = std::cos(a.val);
                a.val = std::sin(a.val);
                for (double& dk : a.d) dk *= c;
                break;
            }
            case Op::Cos: {
                auto& a = stack.back();
                const double s = -std::sin(a.val);
                a.val = std::cos(a.val);
                for (double& dk : a.d) dk *= s;
                break;
            }
            case Op::Tan: {
                auto& a = stack.back();
                const double c = std::cos(a.val);
                const double sec2 = 1.0 / (c * c);
                a.val = std::tan(a.val);
                if (!std::isfinite(a.val) || !std::isfinite(sec2)) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "tan() singularity");
                }
                for (double& dk : a.d) dk *= sec2;
                break;
            }
            case Op::Exp: {
                auto& a = stack.back();
                if (a.val > 700.0) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "exp() overflow in expression");
                }
                const double ev = std::exp(a.val);
                a.val = ev;
                for (double& dk : a.d) dk *= ev;
                break;
            }
            case Op::Log: {
                auto& a = stack.back();
                if (a.val <= 0.0) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "log() domain error (x <= 0)");
                }
                const double inv = 1.0 / a.val;
                a.val = std::log(a.val);
                for (double& dk : a.d) dk *= inv;
                break;
            }
            case Op::Sqrt: {
                auto& a = stack.back();
                if (a.val < 0.0) {
                    return Diagnostic::error(StatusCode::NumericalFailure, "sqrt() domain error (x < 0)");
                }
                const double sv = std::sqrt(a.val);
                const double slope = (sv > 0.0) ? (0.5 / sv) : 0.0;
                a.val = sv;
                for (double& dk : a.d) dk *= slope;
                break;
            }
            case Op::Abs: {
                auto& a = stack.back();
                const double sgn = (a.val > 0.0) ? 1.0 : ((a.val < 0.0) ? -1.0 : 0.0);
                a.val = std::abs(a.val);
                for (double& dk : a.d) dk *= sgn;
                break;
            }
            case Op::Tanh: {
                auto& a = stack.back();
                const double th = std::tanh(a.val);
                const double sech2 = 1.0 - th * th;
                a.val = th;
                for (double& dk : a.d) dk *= sech2;
                break;
            }
            case Op::Min: {
                Jet b = std::move(stack.back());
                stack.pop_back();
                auto& a = stack.back();
                if (b.val < a.val) a = std::move(b);
                break;
            }
            case Op::Max: {
                Jet b = std::move(stack.back());
                stack.pop_back();
                auto& a = stack.back();
                if (b.val > a.val) a = std::move(b);
                break;
            }
        }
        if (!std::isfinite(stack.back().val)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite expression intermediate");
        }
        for (double dk : stack.back().d) {
            if (!std::isfinite(dk)) {
                return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite expression derivative");
            }
        }
    }

    if (stack.size() != 1u) {
        return Diagnostic::error(StatusCode::InvalidArgument, "malformed expression bytecode stack");
    }
    out_result.value = stack.back().val;
    out_result.partials = std::move(stack.back().d);
    return Diagnostic::success();
}

}  // namespace spice::devices
