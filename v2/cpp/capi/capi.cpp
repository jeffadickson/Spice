#include "spice/capi.h"

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <exception>
#include <memory>
#include <string>

namespace {

constexpr std::uint64_t kEngineMagic = 0x5350494345563201ULL;
constexpr std::uint64_t kDefaultMaxUnknowns = 1'000'000ULL;

}  // namespace

struct spice_engine {
    std::uint64_t magic{kEngineMagic};
    bool audits_enabled{false};
    std::uint64_t max_unknowns{kDefaultMaxUnknowns};
    mutable std::string last_error;

    [[nodiscard]] bool valid() const noexcept { return magic == kEngineMagic; }
};

extern "C" {

spice_status_t spice_engine_create(
    const spice_engine_options_t* options,
    spice_engine_t** out_engine) {
    try {
        if (options == nullptr || out_engine == nullptr) {
            return SPICE_STATUS_INVALID_ARGUMENT;
        }
        *out_engine = nullptr;

        if (options->struct_size != sizeof(spice_engine_options_t)) {
            return SPICE_STATUS_ABI_MISMATCH;
        }
        if (options->abi_major != SPICE_CAPI_ABI_MAJOR ||
            options->abi_minor > SPICE_CAPI_ABI_MINOR) {
            return SPICE_STATUS_ABI_MISMATCH;
        }

        auto engine = std::make_unique<spice_engine>();
        engine->audits_enabled = (options->enable_audits != 0u);
        engine->max_unknowns =
            (options->max_unknowns == 0u) ? kDefaultMaxUnknowns : options->max_unknowns;
        *out_engine = engine.release();
        return SPICE_STATUS_OK;
    } catch (const std::bad_alloc&) {
        return SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

void spice_engine_destroy(spice_engine_t* engine) {
    if (engine == nullptr) {
        return;
    }
    if (!engine->valid()) {
        /* Refuse to delete an uninitialized or already-invalidated sentinel. */
        return;
    }
    engine->magic = 0;
    delete engine;
}

spice_status_t spice_engine_capabilities(
    const spice_engine_t* engine,
    spice_capabilities_t* out_capabilities) {
    try {
        if (engine == nullptr || !engine->valid()) {
            return SPICE_STATUS_INVALID_HANDLE;
        }
        if (out_capabilities == nullptr) {
            engine->last_error = "out_capabilities must not be null";
            return SPICE_STATUS_INVALID_ARGUMENT;
        }
        if (out_capabilities->struct_size != sizeof(spice_capabilities_t)) {
            engine->last_error = "spice_capabilities_t struct_size mismatch";
            return SPICE_STATUS_ABI_MISMATCH;
        }

        out_capabilities->abi_major = SPICE_CAPI_ABI_MAJOR;
        out_capabilities->abi_minor = SPICE_CAPI_ABI_MINOR;
#if defined(SPICE_V2_HAS_KLU)
        out_capabilities->klu_available = 1u;
#else
        out_capabilities->klu_available = 0u;
#endif
        /* Reported honestly: OSDI and physical audits are wired in Slices M2a/M2b. */
        out_capabilities->osdi_available = 0u;
        out_capabilities->audits_available = 0u;
        out_capabilities->default_max_unknowns = engine->max_unknowns;
        engine->last_error.clear();
        return SPICE_STATUS_OK;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

spice_status_t spice_engine_last_error(
    const spice_engine_t* engine,
    char* buffer,
    size_t buffer_bytes,
    size_t* out_required_bytes) {
    try {
        if (engine == nullptr || !engine->valid()) {
            return SPICE_STATUS_INVALID_HANDLE;
        }
        const size_t required = engine->last_error.size() + 1u;
        if (out_required_bytes != nullptr) {
            *out_required_bytes = required;
        }
        if (buffer == nullptr || buffer_bytes == 0u) {
            return SPICE_STATUS_OK;
        }
        const size_t to_copy = std::min(engine->last_error.size(), buffer_bytes - 1u);
        if (to_copy > 0u) {
            std::memcpy(buffer, engine->last_error.data(), to_copy);
        }
        buffer[to_copy] = '\0';
        return (buffer_bytes >= required) ? SPICE_STATUS_OK : SPICE_STATUS_RESOURCE_EXHAUSTED;
    } catch (...) {
        return SPICE_STATUS_INTERNAL_ERROR;
    }
}

}  // extern "C"
