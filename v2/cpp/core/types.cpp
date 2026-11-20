#include "core/types.hpp"

#include <algorithm>
#include <string>

namespace spice::core {

Diagnostic CscPattern::validate(std::uint64_t max_dimension) const {
    if (dimension == 0) {
        return Diagnostic::error(
            StatusCode::InvalidArgument,
            "CSC pattern dimension must be positive",
            "CscPattern.dimension");
    }
    if (dimension > max_dimension) {
        return Diagnostic::error(
            StatusCode::ResourceExhausted,
            "CSC pattern dimension exceeds caller budget",
            "CscPattern.dimension");
    }
    if (col_ptr.size() != static_cast<std::size_t>(dimension) + 1) {
        return Diagnostic::error(
            StatusCode::InvalidArgument,
            "CSC col_ptr length must equal dimension + 1",
            "CscPattern.col_ptr");
    }
    if (col_ptr.front() != 0) {
        return Diagnostic::error(
            StatusCode::InvalidArgument,
            "CSC col_ptr[0] must be zero",
            "CscPattern.col_ptr");
    }
    if (col_ptr.back() != row_idx.size()) {
        return Diagnostic::error(
            StatusCode::InvalidArgument,
            "CSC col_ptr.back() must match row_idx.size()",
            "CscPattern.row_idx");
    }

    for (std::uint32_t col = 0; col < dimension; ++col) {
        const std::size_t start = col_ptr[col];
        const std::size_t end = col_ptr[col + 1];
        if (end < start || end > row_idx.size()) {
            return Diagnostic::error(
                StatusCode::InvalidArgument,
                "CSC col_ptr must be monotonically non-decreasing within row_idx bounds",
                "CscPattern.col_ptr[" + std::to_string(col) + "]");
        }
        for (std::size_t k = start; k < end; ++k) {
            const std::uint32_t row = row_idx[k];
            if (row >= dimension) {
                return Diagnostic::error(
                    StatusCode::InvalidArgument,
                    "CSC row index out of bounds",
                    "CscPattern.row_idx[" + std::to_string(k) + "]");
            }
            if (k > start && row <= row_idx[k - 1]) {
                return Diagnostic::error(
                    StatusCode::InvalidArgument,
                    "CSC column rows must be strictly increasing without duplicates",
                    "CscPattern.col[" + std::to_string(col) + "]");
            }
        }
    }
    return Diagnostic::success();
}

bool CscPattern::find_slot(std::uint32_t row, std::uint32_t col, SlotId& out_slot) const noexcept {
    if (col >= dimension || row >= dimension || col_ptr.size() != static_cast<std::size_t>(dimension) + 1) {
        return false;
    }
    const std::size_t start = col_ptr[col];
    const std::size_t end = col_ptr[col + 1];
    if (end < start || end > row_idx.size()) {
        return false;
    }
    const auto begin_it = row_idx.begin() + static_cast<std::ptrdiff_t>(start);
    const auto end_it = row_idx.begin() + static_cast<std::ptrdiff_t>(end);
    const auto it = std::lower_bound(begin_it, end_it, row);
    if (it == end_it || *it != row) {
        return false;
    }
    out_slot = SlotId{static_cast<std::size_t>(it - row_idx.begin())};
    return true;
}

}  // namespace spice::core
