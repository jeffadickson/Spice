#pragma once

#include "core/circuit.hpp"
#include "core/types.hpp"

#include <cstddef>
#include <optional>
#include <string>
#include <variant>
#include <vector>

namespace spice::parser {

/*
 * Native C++20 SPICE3f5 Netlist Parser & Hierarchical Elaborator (PLAN-V2-006).
 *
 * Parses canonical SPICE3f5 decks (title, comments, + continuations, .PARAM,
 * {expr}, .MODEL D/OSDI, .SUBCKT/.ENDS + X flattening, R/C/L/K/V/I/E/F/G/H/D/B,
 * .OP/.DC/.AC/.TRAN/.OPTIONS/.END) directly into an immutable spice::core::Circuit
 * and typed analysis command list.
 *
 * Strictly fail-closed: unknown directives, unsupported models, unresolved
 * subcircuits, cyclic instantiation, and malformed expressions return
 * Diagnostic errors with 1-based source line numbers.
 */

struct OpCommand {
    int reserved{0};
};

struct DcCommand {
    std::string source_id;
    double start{0.0};
    double stop{0.0};
    double step{0.0};
};

struct AcCommand {
    std::vector<double> frequencies_hz;
};

struct TranCommand {
    double step_s{0.0};
    double stop_s{0.0};
    double max_step_s{0.0};
    bool uic{false};
};

using AnalysisCommand = std::variant<OpCommand, DcCommand, AcCommand, TranCommand>;

struct DeckOptions {
    std::string method; /* "", "trap", "be", "bdf2" */
    std::optional<double> reltol;
};

struct ParsedDeck {
    std::string title;
    core::Circuit circuit;
    std::vector<AnalysisCommand> analyses;
    DeckOptions options;
};

struct ParseOptions {
    std::size_t max_subckt_depth{32};
    std::uint64_t max_unknowns{1'000'000};
};

[[nodiscard]] core::Diagnostic parse_spice_deck(
    const std::string& netlist_text,
    ParsedDeck& out_deck,
    const ParseOptions& opts = {});

}  // namespace spice::parser
