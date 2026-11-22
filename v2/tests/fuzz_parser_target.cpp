#include "core/circuit.hpp"
#include "parser/spice_parser.hpp"

#include <cstddef>
#include <cstdint>
#include <string>

extern "C" int LLVMFuzzerTestOneInput(const std::uint8_t* data, std::size_t size) {
    if (data == nullptr || size == 0u || size > 4096u) {
        return 0;
    }
    const std::string text(reinterpret_cast<const char*>(data), size);
    spice::parser::ParseOptions opts{};
    opts.max_unknowns = 2000u;
    opts.max_subckt_depth = 6u;
    opts.max_include_depth = 0u; /* Never touch filesystem during fuzzing. */

    spice::parser::ParsedDeck deck{};
    (void)spice::parser::parse_spice_deck(text, deck, opts);

    /* Also fuzz SPEF parasitic annotator on the same byte stream. */
    spice::core::Circuit c{};
    c.name = "f";
    (void)c.get_or_add_node("out");
    spice::parser::SpefSummary sum{};
    (void)spice::parser::parse_spef_and_annotate(text, c, sum);
    return 0;
}
