#pragma once

#include "core/types.hpp"

#include <cstddef>
#include <cstdint>
#include <functional>
#include <span>
#include <string>
#include <vector>

namespace spice::devices {

/*
 * Compiled Behavioral Expression VM with Forward-Mode Automatic Differentiation
 * (ARCH-DSL-002, ARCH-CAP-004).
 *
 * Parses SPICE-style behavioral formulas once at compile time into a compact
 * stack bytecode program. Never invokes an external script interpreter in the
 * Newton/device hot loop.
 *
 * Supported syntax:
 * - Numeric literals with SPICE engineering suffixes (f, p, n, u, m, k, meg, g, t)
 * - Operators: +, -, *, /, ^ (right-associative power), unary +, -
 * - Functions: sin, cos, tan, exp, log, ln, sqrt, abs, tanh, pow(a,b), min(a,b), max(a,b)
 * - Signals: v(node), v(p, n), i(branch_id), time
 */

struct ExprEvalResult {
    double value{0.0};
    /* Partial derivatives aligned 1:1 with CompiledExpr::referenced_unknowns(). */
    std::vector<double> partials;
};

class CompiledExpr {
public:
    enum class Op : std::uint8_t {
        PushConst,
        PushTime,
        PushUnknown, /* operand = index into referenced_unknowns_ */
        Neg,
        Add,
        Sub,
        Mul,
        Div,
        Pow,
        Sin,
        Cos,
        Tan,
        Exp,
        Log,
        Sqrt,
        Abs,
        Tanh,
        Min,
        Max
    };

    struct Instruction {
        Op op{Op::PushConst};
        std::uint32_t operand{0};
        double imm{0.0};
    };

    using ResolveNodeFn = std::function<bool(const std::string& node_name, std::int32_t& out_unknown_or_neg1_for_gnd)>;
    using ResolveBranchFn = std::function<bool(const std::string& branch_name, std::uint32_t& out_unknown)>;

    [[nodiscard]] static core::Diagnostic compile(
        const std::string& source,
        const ResolveNodeFn& resolve_node,
        const ResolveBranchFn& resolve_branch,
        CompiledExpr& out_expr);

    [[nodiscard]] const std::vector<core::UnknownId>& referenced_unknowns() const noexcept {
        return referenced_unknowns_;
    }

    /* Evaluate value and exact analytical partials w.r.t. every referenced unknown. */
    [[nodiscard]] core::Diagnostic evaluate(
        std::span<const double> x,
        double time_s,
        ExprEvalResult& out_result) const;

private:
    std::vector<Instruction> code_;
    std::vector<core::UnknownId> referenced_unknowns_;
};

}  // namespace spice::devices
