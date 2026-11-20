#ifndef SPICE_V2_CAPI_H
#define SPICE_V2_CAPI_H

/*
 * Spice V2 Stable Flat C ABI (ARCH-API-003).
 *
 * Ownership and safety contract:
 * - All exported calls return a fixed-width spice_status_t; C++ exceptions never
 *   cross this boundary.
 * - Handles are process-local, validated on entry, and must be released exactly
 *   once via their matching *_destroy function.
 * - Caller-supplied option and capability structs carry struct_size for safe
 *   forward-compatible ABI checks.
 */

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#  if defined(SPICE_CAPI_BUILDING_DLL)
#    define SPICE_CAPI_EXPORT __declspec(dllexport)
#  else
#    define SPICE_CAPI_EXPORT __declspec(dllimport)
#  endif
#else
#  define SPICE_CAPI_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define SPICE_CAPI_ABI_MAJOR 2u
#define SPICE_CAPI_ABI_MINOR 0u

typedef enum spice_status_t {
    SPICE_STATUS_OK = 0,
    SPICE_STATUS_INVALID_ARGUMENT = 1,
    SPICE_STATUS_ABI_MISMATCH = 2,
    SPICE_STATUS_INVALID_HANDLE = 3,
    SPICE_STATUS_UNSUPPORTED = 4,
    SPICE_STATUS_RESOURCE_EXHAUSTED = 5,
    SPICE_STATUS_NUMERICAL_FAILURE = 6,
    SPICE_STATUS_CANCELLED = 7,
    SPICE_STATUS_INTERNAL_ERROR = 8
} spice_status_t;

typedef struct spice_engine spice_engine_t;

typedef struct spice_engine_options_t {
    uint32_t struct_size;       /* Must equal sizeof(spice_engine_options_t). */
    uint32_t abi_major;         /* Must equal SPICE_CAPI_ABI_MAJOR. */
    uint32_t abi_minor;         /* Must be <= SPICE_CAPI_ABI_MINOR. */
    uint32_t enable_audits;     /* Non-zero enables opt-in physical audits. */
    uint64_t max_unknowns;      /* Explicit caller budget; 0 means engine default. */
} spice_engine_options_t;

typedef struct spice_capabilities_t {
    uint32_t struct_size;       /* Must equal sizeof(spice_capabilities_t). */
    uint32_t abi_major;
    uint32_t abi_minor;
    uint32_t klu_available;     /* 1 if SuiteSparse KLU adapter is linked, else 0. */
    uint32_t osdi_available;    /* 1 if generic OSDI 0.3/0.4 loader is active, else 0. */
    uint32_t audits_available;  /* 1 if independent physical audits are active, else 0. */
    uint64_t default_max_unknowns;
} spice_capabilities_t;

/* Create an isolated simulator engine instance with explicit options. */
SPICE_CAPI_EXPORT spice_status_t spice_engine_create(
    const spice_engine_options_t* options,
    spice_engine_t** out_engine);

/* Destroy an engine handle. Passing NULL is a safe no-op. */
SPICE_CAPI_EXPORT void spice_engine_destroy(spice_engine_t* engine);

/* Query truthful runtime capabilities for this build and engine instance. */
SPICE_CAPI_EXPORT spice_status_t spice_engine_capabilities(
    const spice_engine_t* engine,
    spice_capabilities_t* out_capabilities);

/* Copy the engine's last diagnostic message into caller-owned UTF-8 buffer. */
SPICE_CAPI_EXPORT spice_status_t spice_engine_last_error(
    const spice_engine_t* engine,
    char* buffer,
    size_t buffer_bytes,
    size_t* out_required_bytes);

#ifdef __cplusplus
}
#endif

#endif /* SPICE_V2_CAPI_H */
