#include "core/types.hpp"
#include "dae/device.hpp"
#include "spice/capi.h"

#include <cmath>
#include <cstdint>
#include <iostream>
#include <limits>
#include <string>
#include <vector>

#if defined(SPICE_V2_USE_CATCH2)
#  include <catch2/catch_approx.hpp>
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
      static void SPICE_CAT(spice_test_fn_, __LINE__)();                                \
      static const TestRegistrar SPICE_CAT(spice_test_reg_, __LINE__)(                  \
          name, &SPICE_CAT(spice_test_fn_, __LINE__));                                  \
      static void SPICE_CAT(spice_test_fn_, __LINE__)()
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

using namespace spice::core;
using namespace spice::dae;

/*
 * Minimal test-only grounded-or-floating 2-terminal RC parallel fixture
 * implementing the universal DAE Device interface (ARCH-EQ-001, ARCH-EQ-004):
 *   F_p =  G * (v_p - v_n),   F_n = -F_p
 *   Q_p =  C * (v_p - v_n),   Q_n = -Q_p
 *   trial_charge tracks trial vs. accepted history for rollback verification.
 */
class TestRcDevice final : public Device {
public:
    TestRcDevice(UnknownId pos, UnknownId neg, double ohms, double farads)
        : pos_(pos), neg_(neg), conductance_(1.0 / ohms), capacitance_(farads) {
        descriptor_.kind_id = "test.rc_parallel";
        descriptor_.display_name = "Test Parallel RC";
        descriptor_.pins = {{"p", "positive"}, {"n", "negative"}};
        descriptor_.supports_ac = true;
    }

    const DeviceDescriptor& descriptor() const noexcept override { return descriptor_; }

    Diagnostic bind_slots(const CscPattern& pattern) override {
        if (!pattern.find_slot(pos_.value, pos_.value, pp_) ||
            !pattern.find_slot(pos_.value, neg_.value, pn_) ||
            !pattern.find_slot(neg_.value, pos_.value, np_) ||
            !pattern.find_slot(neg_.value, neg_.value, nn_)) {
            return Diagnostic::error(
                StatusCode::InvalidArgument,
                "RC device stamp slots missing from CSC pattern",
                descriptor_.kind_id);
        }
        bound_ = true;
        return Diagnostic::success();
    }

    Diagnostic evaluate(const EvalContext& ctx, StampTarget& target) override {
        if (!bound_) {
            return Diagnostic::error(StatusCode::InvalidArgument, "device not bound");
        }
        if (pos_.value >= ctx.x.size() || neg_.value >= ctx.x.size()) {
            return Diagnostic::error(StatusCode::InvalidArgument, "unknown index out of bounds");
        }
        const double v = ctx.x[pos_.value] - ctx.x[neg_.value];
        const double i = conductance_ * v;
        const double q = capacitance_ * v;
        trial_charge_ = q;

        if (!target.add_f(pos_, i) || !target.add_f(neg_, -i) ||
            !target.add_q(pos_, q) || !target.add_q(neg_, -q) ||
            !target.add_df(pp_, conductance_) || !target.add_df(pn_, -conductance_) ||
            !target.add_df(np_, -conductance_) || !target.add_df(nn_, conductance_) ||
            !target.add_dq(pp_, capacitance_) || !target.add_dq(pn_, -capacitance_) ||
            !target.add_dq(np_, -capacitance_) || !target.add_dq(nn_, capacitance_)) {
            return Diagnostic::error(StatusCode::NumericalFailure, "nonfinite or out-of-range stamp");
        }
        return Diagnostic::success();
    }

    void commit_trial() noexcept override { accepted_charge_ = trial_charge_; }
    void rollback_trial() noexcept override { trial_charge_ = accepted_charge_; }

    [[nodiscard]] double accepted_charge() const noexcept { return accepted_charge_; }
    [[nodiscard]] double trial_charge() const noexcept { return trial_charge_; }

private:
    DeviceDescriptor descriptor_;
    UnknownId pos_;
    UnknownId neg_;
    double conductance_;
    double capacitance_;
    SlotId pp_{}, pn_{}, np_{}, nn_{};
    bool bound_{false};
    double accepted_charge_{0.0};
    double trial_charge_{0.0};
};

}  // namespace

TEST_CASE("C ABI enforces version, struct size, null checks and handle lifecycle", "[capi]") {
    spice_engine_t* engine = nullptr;
    REQUIRE(spice_engine_create(nullptr, &engine) == SPICE_STATUS_INVALID_ARGUMENT);
    REQUIRE(engine == nullptr);

    spice_engine_options_t bad_size{};
    bad_size.struct_size = 4u;
    bad_size.abi_major = SPICE_CAPI_ABI_MAJOR;
    bad_size.abi_minor = SPICE_CAPI_ABI_MINOR;
    REQUIRE(spice_engine_create(&bad_size, &engine) == SPICE_STATUS_ABI_MISMATCH);
    REQUIRE(engine == nullptr);

    spice_engine_options_t bad_major{};
    bad_major.struct_size = sizeof(spice_engine_options_t);
    bad_major.abi_major = SPICE_CAPI_ABI_MAJOR + 1u;
    bad_major.abi_minor = 0u;
    REQUIRE(spice_engine_create(&bad_major, &engine) == SPICE_STATUS_ABI_MISMATCH);

    spice_engine_options_t bad_minor{};
    bad_minor.struct_size = sizeof(spice_engine_options_t);
    bad_minor.abi_major = SPICE_CAPI_ABI_MAJOR;
    bad_minor.abi_minor = SPICE_CAPI_ABI_MINOR + 1u;
    REQUIRE(spice_engine_create(&bad_minor, &engine) == SPICE_STATUS_ABI_MISMATCH);

    spice_engine_options_t ok_opts{};
    ok_opts.struct_size = sizeof(spice_engine_options_t);
    ok_opts.abi_major = SPICE_CAPI_ABI_MAJOR;
    ok_opts.abi_minor = SPICE_CAPI_ABI_MINOR;
    ok_opts.enable_audits = 1u;
    ok_opts.max_unknowns = 50'000u;
    REQUIRE(spice_engine_create(&ok_opts, &engine) == SPICE_STATUS_OK);
    REQUIRE(engine != nullptr);

    /* Null & wrong-size capability struct rejection populates last_error. */
    REQUIRE(spice_engine_capabilities(engine, nullptr) == SPICE_STATUS_INVALID_ARGUMENT);
    size_t required = 0;
    REQUIRE(spice_engine_last_error(engine, nullptr, 0, &required) == SPICE_STATUS_OK);
    REQUIRE(required > 1u);
    char small_buf[4]{};
    REQUIRE(spice_engine_last_error(engine, small_buf, sizeof(small_buf), &required) ==
            SPICE_STATUS_RESOURCE_EXHAUSTED);
    std::string full_buf(required, '\0');
    REQUIRE(spice_engine_last_error(engine, full_buf.data(), full_buf.size(), nullptr) ==
            SPICE_STATUS_OK);
    REQUIRE(std::string(full_buf.c_str()) == "out_capabilities must not be null");

    spice_capabilities_t bad_caps{};
    bad_caps.struct_size = 1u;
    REQUIRE(spice_engine_capabilities(engine, &bad_caps) == SPICE_STATUS_ABI_MISMATCH);

    spice_capabilities_t caps{};
    caps.struct_size = sizeof(spice_capabilities_t);
    REQUIRE(spice_engine_capabilities(engine, &caps) == SPICE_STATUS_OK);
    REQUIRE(caps.abi_major == SPICE_CAPI_ABI_MAJOR);
    REQUIRE(caps.abi_minor == SPICE_CAPI_ABI_MINOR);
    REQUIRE(caps.default_max_unknowns == 50'000u);
    REQUIRE(caps.osdi_available == 1u);
    REQUIRE(caps.audits_available == 1u);

    /* Null handle validation. */
    REQUIRE(spice_engine_capabilities(nullptr, &caps) == SPICE_STATUS_INVALID_HANDLE);
    REQUIRE(spice_engine_last_error(nullptr, nullptr, 0, nullptr) == SPICE_STATUS_INVALID_HANDLE);

    spice_engine_destroy(engine);
    spice_engine_destroy(nullptr);
}

TEST_CASE("CSC pattern validation enforces invariants and lifts v1 256-unknown cap", "[core]") {
    /* Zero dimension rejected. */
    CscPattern empty{};
    REQUIRE(empty.validate().code == StatusCode::InvalidArgument);

    /* Oversized against explicit caller budget rejected. */
    CscPattern over_budget{};
    over_budget.dimension = 100;
    REQUIRE(over_budget.validate(50).code == StatusCode::ResourceExhausted);

    /* Unsorted / duplicate column rows rejected. */
    CscPattern dup{};
    dup.dimension = 2;
    dup.col_ptr = {0, 2, 2};
    dup.row_idx = {0, 0};
    REQUIRE(dup.validate().code == StatusCode::InvalidArgument);

    /* Out-of-bounds row index rejected. */
    CscPattern oob{};
    oob.dimension = 2;
    oob.col_ptr = {0, 1, 1};
    oob.row_idx = {2};
    REQUIRE(oob.validate().code == StatusCode::InvalidArgument);

    /* Non-zero col_ptr[0] and mismatched col_ptr.back() rejected. */
    CscPattern bad_start{};
    bad_start.dimension = 1;
    bad_start.col_ptr = {1, 1};
    REQUIRE(bad_start.validate().code == StatusCode::InvalidArgument);

    CscPattern bad_end{};
    bad_end.dimension = 1;
    bad_end.col_ptr = {0, 2};
    bad_end.row_idx = {0};
    REQUIRE(bad_end.validate().code == StatusCode::InvalidArgument);

    /* 1,024-unknown diagonal pattern validates cleanly (v1 256-unknown cap removed). */
    CscPattern large{};
    large.dimension = 1024;
    large.col_ptr.resize(1025);
    large.row_idx.resize(1024);
    for (std::uint32_t i = 0; i < 1024; ++i) {
        large.col_ptr[i] = i;
        large.row_idx[i] = i;
    }
    large.col_ptr[1024] = 1024;
    REQUIRE(large.validate().ok());
    SlotId slot{};
    REQUIRE(large.find_slot(777, 777, slot));
    REQUIRE(slot.value == 777u);
    REQUIRE(!large.find_slot(777, 778, slot));
}

TEST_CASE("Universal DAE Device stamps F, Q, dF/dx, dQ/dx and enforces trial rollback", "[dae]") {
    /* 2x2 dense-in-CSC pattern for unknowns 0 and 1. */
    CscPattern pattern{};
    pattern.dimension = 2;
    pattern.col_ptr = {0, 2, 4};
    pattern.row_idx = {0, 1, 0, 1};
    REQUIRE(pattern.validate().ok());

    TestRcDevice dev(UnknownId{0}, UnknownId{1}, 1000.0 /* Ohms */, 2.0e-6 /* Farads */);
    REQUIRE(dev.bind_slots(pattern).ok());

    std::vector<double> f(2, 0.0), q(2, 0.0), df(4, 0.0), dq(4, 0.0);
    StampTarget target{f, q, df, dq};
    const std::vector<double> x = {3.5, 1.0};
    EvalContext ctx{};
    ctx.x = x;
    REQUIRE(dev.evaluate(ctx, target).ok());

    /* Analytical checks: v = 2.5 V, G = 1e-3 S -> I = 2.5 mA, Q = 5 uC. */
    REQUIRE(std::abs(f[0] - 2.5e-3) < 1e-15);
    REQUIRE(std::abs(f[1] + 2.5e-3) < 1e-15);
    REQUIRE(std::abs(q[0] - 5.0e-6) < 1e-18);
    REQUIRE(std::abs(q[1] + 5.0e-6) < 1e-18);
    REQUIRE(std::abs(df[0] - 1.0e-3) < 1e-15);
    REQUIRE(std::abs(df[1] + 1.0e-3) < 1e-15);
    REQUIRE(std::abs(df[2] + 1.0e-3) < 1e-15);
    REQUIRE(std::abs(df[3] - 1.0e-3) < 1e-15);
    REQUIRE(std::abs(dq[0] - 2.0e-6) < 1e-18);
    REQUIRE(std::abs(dq[1] + 2.0e-6) < 1e-18);

    /* Commit accepted state, then trial a different voltage and rollback. */
    dev.commit_trial();
    REQUIRE(std::abs(dev.accepted_charge() - 5.0e-6) < 1e-18);

    std::fill(f.begin(), f.end(), 0.0);
    std::fill(q.begin(), q.end(), 0.0);
    std::fill(df.begin(), df.end(), 0.0);
    std::fill(dq.begin(), dq.end(), 0.0);
    const std::vector<double> x_rejected = {10.0, 0.0};
    ctx.x = x_rejected;
    REQUIRE(dev.evaluate(ctx, target).ok());
    REQUIRE(std::abs(dev.trial_charge() - 20.0e-6) < 1e-18);
    dev.rollback_trial();
    REQUIRE(std::abs(dev.trial_charge() - 5.0e-6) < 1e-18);
    REQUIRE(std::abs(dev.accepted_charge() - 5.0e-6) < 1e-18);

    /* Nonfinite state rejected before poisoning stamp buffers. */
    const std::vector<double> x_nan = {std::numeric_limits<double>::quiet_NaN(), 0.0};
    ctx.x = x_nan;
    REQUIRE(dev.evaluate(ctx, target).code == StatusCode::NumericalFailure);
}

#if !defined(SPICE_V2_USE_CATCH2)
int main() {
    for (const auto& [name, fn] : test_registry()) {
        const int before = g_failed_checks;
        fn();
        if (g_failed_checks == before) {
            std::cout << "PASS: " << name << "\n";
        }
    }
    if (g_failed_checks != 0) {
        std::cerr << g_failed_checks << " check(s) failed.\n";
        return 1;
    }
    std::cout << "All " << test_registry().size() << " Spice V2 tests passed.\n";
    return 0;
}
#endif
