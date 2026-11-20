#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace spice::core {

/*
 * Strongly typed identifiers (ARCH-STATE-003) prevent accidental mixing of
 * node indices, MNA unknown indices, device instances, and sparse CSC slots.
 */
struct NodeId {
    std::uint32_t value{0};
    constexpr bool operator==(const NodeId&) const = default;
    constexpr bool is_ground() const noexcept { return value == 0; }
};

struct UnknownId {
    std::uint32_t value{0};
    constexpr bool operator==(const UnknownId&) const = default;
};

struct DeviceId {
    std::uint32_t value{0};
    constexpr bool operator==(const DeviceId&) const = default;
};

struct SlotId {
    std::size_t value{0};
    constexpr bool operator==(const SlotId&) const = default;
};

enum class StatusCode : std::uint32_t {
    Ok = 0,
    InvalidArgument = 1,
    Unsupported = 2,
    ResourceExhausted = 3,
    NumericalFailure = 4
};

struct Diagnostic {
    StatusCode code{StatusCode::Ok};
    std::string message;
    std::string subject;

    [[nodiscard]] bool ok() const noexcept { return code == StatusCode::Ok; }
    static Diagnostic success() { return {}; }
    static Diagnostic error(StatusCode c, std::string msg, std::string subj = {}) {
        return Diagnostic{c, std::move(msg), std::move(subj)};
    }
};

/*
 * Circuit-independent Compressed Sparse Column (CSC) pattern contract
 * (ARCH-LIN-001, ARCH-LIN-005).
 *
 * Invariants enforced by validate():
 * - dimension > 0 and <= max_dimension (explicit caller budget, no 256 cap).
 * - col_ptr.size() == dimension + 1, col_ptr[0] == 0, non-decreasing.
 * - row_idx.size() == col_ptr.back().
 * - Within every column, row indices are strictly increasing and < dimension
 *   (no out-of-range entries, no unsorted entries, no duplicate slots).
 */
struct CscPattern {
    std::uint32_t dimension{0};
    std::vector<std::size_t> col_ptr;
    std::vector<std::uint32_t> row_idx;

    [[nodiscard]] std::size_t nnz() const noexcept { return row_idx.size(); }

    /* Validate all CSC structural invariants before passing to any solver. */
    [[nodiscard]] Diagnostic validate(std::uint64_t max_dimension = 1'000'000) const;

    /* Locate the unique CSC value slot for (row, col), or return false if absent. */
    [[nodiscard]] bool find_slot(std::uint32_t row, std::uint32_t col, SlotId& out_slot) const noexcept;
};

}  // namespace spice::core
