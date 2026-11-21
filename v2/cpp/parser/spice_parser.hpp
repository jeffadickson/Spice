#pragma once

#include "core/circuit.hpp"
#include "core/types.hpp"

#include <cstddef>
#include <optional>
#include <string>
#include <utility>
#include <variant>
#include <vector>

namespace spice::parser {

/*
 * Native C++20 SPICE3f5 / RSpice-Compatible Netlist Parser & Hierarchical Elaborator.
 *
 * Supports:
 *   - Elements: R, C, L, K, V, I (DC/AC/PULSE/SIN/PWL/EXP/SFFM), E, F, G, H, D, B, S, T,
 *               M (Level 1 + BSIM3v3 LEVEL=49 + BSIM4 LEVEL=54 + OSDI dispatch),
 *               Q (Gummel-Poon NPN/PNP + OSDI dispatch),
 *               J (Shichman-Hodges NJF/PJF + OSDI dispatch),
 *               X (.SUBCKT flattening + .OSDI model dispatch).
 *   - Directives: .SUBCKT/.ENDS, .PARAM, .INCLUDE, .LIB, .GLOBAL, .MODEL (D/SW/NMOS/PMOS/
 *                 NPN/PNP/NJF/PJF/OSDI), .OSDI, .TEMP, .IC, .NODESET, .OPTIONS,
 *                 .PRINT/.PLOT/.SAVE/.PROBE, .MEAS, .END.
 *   - Analyses: .OP, .DC (single & nested 2-source), .AC, .TRAN, .TF, .NOISE, .SENS,
 *               .DCMATCH, .SP, .STB, .PZ, .FOUR, .FFT, .MC, .HB, .PSS, .PAC, .PXF, .PNOISE.
 *
 * Strictly fail-closed: unknown directives, unlisted MOSFET levels, unknown model parameters,
 * unresolved subcircuits, cyclic includes/subckts, and malformed expressions return
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
    /* Optional nested second sweep source (SPICE3f5 .DC src1 start1 stop1 step1 src2 start2 stop2 step2). */
    std::optional<std::string> source2_id;
    double start2{0.0};
    double stop2{0.0};
    double step2{0.0};
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

struct TfCommand {
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    std::string input_source_id;
};

struct NoiseCommand {
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    std::string input_source_id;
    std::vector<double> frequencies_hz;
};

struct SensCommand {
    core::NodeId out_p{0};
    core::NodeId out_n{0};
};

struct DcMatchCommand {
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    double resistor_rel_sigma{0.01};
};

struct SpPortDecl {
    core::NodeId p{0};
    core::NodeId n{0};
    double z0_ohms{50.0};
};

struct SpCommand {
    std::vector<SpPortDecl> ports;
    std::vector<double> frequencies_hz;
};

struct StbCommand {
    std::string probe_source_id;
    std::vector<double> frequencies_hz;
};

struct PzCommand {
    core::NodeId in_p{0};
    core::NodeId in_n{0};
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    bool compute_poles{true};
    bool compute_zeros{true};
};

struct FourCommand {
    double fundamental_hz{0.0};
    std::string signal_id;
    std::size_t harmonics{9};
};

struct FftCommand {
    std::string signal_id;
    std::size_t n_points{1024};
};

struct McCommand {
    std::size_t runs{16};
    std::uint64_t seed{42};
    double resistor_rel_sigma{0.05};
};

struct HbCommand {
    double fundamental_hz{0.0};
    std::size_t harmonics{7};
};

struct PssCommand {
    double fundamental_hz{0.0};
    std::size_t steps_per_period{64};
};

struct PacCommand {
    double small_signal_freq_hz{0.0};
    std::string input_source_id;
};

struct PxfCommand {
    double small_signal_freq_hz{0.0};
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    int output_sideband{0};
};

struct PnoiseCommand {
    double small_signal_freq_hz{0.0};
    core::NodeId out_p{0};
    core::NodeId out_n{0};
    int output_sideband{0};
};

using AnalysisCommand = std::variant<
    OpCommand,
    DcCommand,
    AcCommand,
    TranCommand,
    TfCommand,
    NoiseCommand,
    SensCommand,
    DcMatchCommand,
    SpCommand,
    StbCommand,
    PzCommand,
    FourCommand,
    FftCommand,
    McCommand,
    HbCommand,
    PssCommand,
    PacCommand,
    PxfCommand,
    PnoiseCommand>;

/* Declarative post-processing measurement (.MEAS TRAN|DC|AC|OP <name> MAX|MIN|AVG|PP|FIND <signal> [AT=<t>]). */
struct MeasCommand {
    std::string domain;   /* "tran", "dc", "ac", "op" */
    std::string name;     /* Measurement identifier */
    std::string func;     /* "max", "min", "avg", "pp", "find" */
    std::string signal;   /* e.g. "v(out)" */
    std::optional<double> at_x;
};

struct DeckOptions {
    std::string method; /* "", "trap", "be", "bdf2" */
    std::optional<double> reltol;
    double temperature_c{27.0};
    std::vector<std::pair<core::NodeId, double>> initial_conditions; /* .IC */
    std::vector<std::pair<core::NodeId, double>> nodesets;           /* .NODESET */
    std::vector<std::string> saved_signals;                          /* .SAVE / .PROBE / .PRINT */
};

struct ParsedDeck {
    std::string title;
    core::Circuit circuit;
    std::vector<AnalysisCommand> analyses;
    std::vector<MeasCommand> measurements;
    DeckOptions options;
};

struct ParseOptions {
    std::size_t max_subckt_depth{32};
    std::size_t max_include_depth{16};
    std::uint64_t max_unknowns{1'000'000};
    std::string base_dir; /* Optional directory for resolving relative .INCLUDE / .LIB paths */
};

[[nodiscard]] core::Diagnostic parse_spice_deck(
    const std::string& netlist_text,
    ParsedDeck& out_deck,
    const ParseOptions& opts = {});

}  // namespace spice::parser
