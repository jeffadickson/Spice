#include "analysis/analyses.hpp"
#include "devices/expr_vm.hpp"
#include "devices/linear_devices.hpp"
#include "parser/spice_parser.hpp"
#include "spice/capi.h"

#include <cmath>
#include <cstdint>
#include <iostream>
#include <random>
#include <string>
#include <vector>

#if defined(SPICE_V2_USE_CATCH2)
#  include <catch2/catch_test_macros.hpp>
#else
namespace {
int g_failed_checks = 0;
std::vector<std::pair<std::string, void (*)()>>& test_registry() {
    static std::vector<std::pair<std::string, void (*)()>> tests;
    return tests;
}
struct TestRegistrar {
    TestRegistrar(const char* name, void (*fn)()) { test_registry().emplace_back(name, fn); }
};
}  // namespace

#  define SPICE_CAT_IMPL(a, b) a##b
#  define SPICE_CAT(a, b) SPICE_CAT_IMPL(a, b)
#  define TEST_CASE(name, tags)                                                         \
      static void SPICE_CAT(spice_fz_fn_, __LINE__)();                                  \
      static const TestRegistrar SPICE_CAT(spice_fz_reg_, __LINE__)(                    \
          name, &SPICE_CAT(spice_fz_fn_, __LINE__));                                    \
      static void SPICE_CAT(spice_fz_fn_, __LINE__)()
#  define REQUIRE(expr)                                                                 \
      do {                                                                              \
          if (!(expr)) {                                                                \
              std::cerr << "FAIL " << __FILE__ << ":" << __LINE__ << " REQUIRE("        \
                        << #expr << ")\n";                                              \
              ++g_failed_checks;                                                        \
              return;                                                                   \
          }                                                                             \
      } while (false)
#endif

namespace {

constexpr std::uint64_t kSeed = 0x5EED2026ULL;

std::string mutate_bytes(const std::string& base, std::mt19937_64& rng) {
    std::string s = base;
    const int ops = 1 + static_cast<int>(rng() % 6u);
    static const char kAlphabet[] =
        "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
        " \t\n.,;:(){}[]+-*/^=?|&!<>\"'\\_$@#%\0";
    constexpr std::size_t kAlphaLen = sizeof(kAlphabet) - 1u;

    for (int op = 0; op < ops; ++op) {
        const std::uint64_t kind = rng() % 5u;
        if (kind == 0u && !s.empty()) {
            const std::size_t p = rng() % s.size();
            s[p] = kAlphabet[rng() % kAlphaLen];
        } else if (kind == 1u && s.size() < 512u) {
            const std::size_t p = s.empty() ? 0u : (rng() % (s.size() + 1u));
            s.insert(p, 1u, kAlphabet[rng() % kAlphaLen]);
        } else if (kind == 2u && !s.empty()) {
            const std::size_t p = rng() % s.size();
            const std::size_t len = 1u + (rng() % std::min<std::size_t>(16u, s.size() - p));
            s.erase(p, len);
        } else if (kind == 3u && s.size() >= 4u) {
            const std::size_t p = rng() % s.size();
            s.resize(p);
        } else if (!s.empty() && s.size() < 256u) {
            const std::size_t p = rng() % s.size();
            const std::size_t len = 1u + (rng() % std::min<std::size_t>(24u, s.size() - p));
            s.insert(rng() % (s.size() + 1u), s.substr(p, len));
        }
    }
    return s;
}

}  // namespace

TEST_CASE("Seeded fuzz corpus (3000 mutated SPICE3f5/Spectre decks) never crashes or returns nonfinite OP", "[fuzz][parser]") {
    const std::vector<std::string> seeds = {
        "Divider\n.PARAM r=1k\n.FUNC f(x) {x*2}\nV1 in 0 DC 5\nR1 in out {f(r)}\nR2 out 0 {r}\n.OP\n.END\n",
        "BSIM4\n.MODEL n NMOS(LEVEL=54 VTH0=0.35)\nVdd vdd 0 DC 1.2\nMn out in 0 0 n W=1u L=65n\n.DC Vdd 0 1.2 0.2\n.END\n",
        "LTRA\n.MODEL l1 LTRA(R=10 L=250n C=100p LEN=1 NSEGS=4)\nV1 1 0 DC 1\nO1 1 0 2 0 l1\nR1 2 0 50\n.END\n",
        "CPL\n.MODEL p1 CPL(R=5 L=300n LM=50n C=100p CM=20p LEN=0.1 NSEGS=4)\nP1 a b 0 c d 0 p1\n.END\n",
        "IfBlock\n.PARAM k=1\n.IF (k==1)\nV1 1 0 1\n.ELSE\nV1 1 0 2\n.ENDIF\nR1 1 0 1k\n.STEP PARAM k LIST 1 2\n.END\n",
        "// Spectre\nsimulator lang=spectre\nparameters r=1k\nV1 (1 0) vsource dc=5\nR1 (1 0) resistor r=r\ndc1 dc\n",
        "TRNOISE\nV1 1 0 DC 0 TRNOISE(0.1 1u 0 0 7)\nR1 1 0 1k\n.TRAN 1u 10u\n.END\n",
        "Subckt\n.SUBCKT s a b\nR1 a b 1k\n.ENDS\nV1 1 0 5\nX1 1 2 s\nR2 2 0 1k\n.END\n",
    };

    std::mt19937_64 rng(kSeed);
    spice::parser::ParseOptions opts{};
    opts.max_unknowns = 5000u;
    opts.max_subckt_depth = 8u;

    std::size_t valid_count = 0;
    for (int iter = 0; iter < 3000; ++iter) {
        const std::string& base = seeds[static_cast<std::size_t>(rng() % seeds.size())];
        const std::string mutated = mutate_bytes(base, rng);
        spice::parser::ParsedDeck deck{};
        const auto d = spice::parser::parse_spice_deck(mutated, deck, opts);
        if (d.ok()) {
            ++valid_count;
            spice::devices::CompiledPlan plan{};
            if (spice::devices::compile_circuit(deck.circuit, plan, 5000u).ok() && plan.unknown_count() <= 128u) {
                spice::analysis::OpResult op{};
                spice::analysis::AnalysisCommonOptions copts{};
                copts.enable_audits = false;
                if (spice::analysis::run_op(plan, copts, op).ok()) {
                    for (double v : op.values) {
                        REQUIRE(std::isfinite(v));
                    }
                }
            }
        }
    }
    REQUIRE(valid_count > 0u);
}

TEST_CASE("Seeded fuzz corpus (1500 mutated SPEF + B-source expressions + C ABI calls) fails closed cleanly", "[fuzz][spef][capi]") {
    const std::string spef_seed =
        "*SPEF \"IEEE 1481-1999\"\n*C_UNIT 1 PF\n*R_UNIT 1 OHM\n*NAME_MAP\n*1 out\n"
        "*D_NET *1 10.0\n*CAP\n1 *1:1 5.0\n2 *1:1 *1:2 2.0\n*RES\n1 *1:1 *1:2 50.0\n*END\n";

    const std::vector<std::string> expr_seeds = {
        "sin(v(in)) + exp(-v(out)/0.026)",
        "v(in,out) * 2.5 + sqrt(abs(v(in)) + 1e-6)",
        "tanh(10*(v(in)-0.5)) / (1 + v(out)^2)",
    };

    std::mt19937_64 rng(kSeed ^ 0xA5A5A5A5ULL);

    for (int i = 0; i < 1000; ++i) {
        spice::core::Circuit c{};
        c.name = "fuzz_spef";
        const auto n1 = c.get_or_add_node("out");
        c.elements.push_back(spice::core::ResistorSpec{"R1", n1, spice::core::NodeId{0}, 1000.0});
        spice::parser::SpefSummary sum{};
        (void)spice::parser::parse_spef_and_annotate(mutate_bytes(spef_seed, rng), c, sum);
    }

    for (int i = 0; i < 500; ++i) {
        const std::string& base = expr_seeds[static_cast<std::size_t>(rng() % expr_seeds.size())];
        spice::devices::CompiledExpr ce{};
        auto resolve_node = [](const std::string& name, std::int32_t& out_u) -> bool {
            if (name == "0") { out_u = -1; return true; }
            out_u = (name == "in") ? 0 : 1;
            return true;
        };
        auto resolve_branch = [](const std::string&, std::uint32_t& out_u) -> bool {
            out_u = 1u;
            return true;
        };
        if (spice::devices::CompiledExpr::compile(mutate_bytes(base, rng), resolve_node, resolve_branch, ce).ok()) {
            const double x[2] = {0.5, 0.25};
            spice::devices::ExprEvalResult res{};
            (void)ce.evaluate(x, 0.0, res);
        }
    }

    /* Fuzz C ABI spice_circuit_parse_spice with null/empty/malformed inputs. */
    spice_engine_options_t eopts{};
    eopts.struct_size = sizeof(spice_engine_options_t);
    eopts.abi_major = SPICE_CAPI_ABI_MAJOR;
    eopts.abi_minor = SPICE_CAPI_ABI_MINOR;
    eopts.enable_audits = 1u;
    spice_engine_t* engine = nullptr;
    REQUIRE(spice_engine_create(&eopts, &engine) == SPICE_STATUS_OK);
    spice_circuit_t* circ = nullptr;
    REQUIRE(spice_circuit_parse_spice(engine, nullptr, nullptr, &circ) == SPICE_STATUS_INVALID_ARGUMENT);
    for (int i = 0; i < 200; ++i) {
        const std::string mutated = mutate_bytes("T\nV1 1 0 1\nR1 1 0 1k\n.END\n", rng);
        spice_circuit_t* c = nullptr;
        if (spice_circuit_parse_spice(engine, mutated.c_str(), nullptr, &c) == SPICE_STATUS_OK) {
            REQUIRE(c != nullptr);
            spice_circuit_destroy(c);
        }
    }
    spice_engine_destroy(engine);
}

#if !defined(SPICE_V2_USE_CATCH2)
int main() {
    for (const auto& [name, fn] : test_registry()) {
        const int before = g_failed_checks;
        fn();
        if (g_failed_checks == before) {
            std::cout << "PASS: " << name << " (seed=0x" << std::hex << kSeed << std::dec << ")\n";
        }
    }
    if (g_failed_checks != 0) {
        std::cerr << g_failed_checks << " check(s) failed (seed=0x" << std::hex << kSeed << std::dec << ").\n";
        return 1;
    }
    std::cout << "All " << test_registry().size() << " fuzz & malformed-input stress suites passed.\n";
    return 0;
}
#endif
