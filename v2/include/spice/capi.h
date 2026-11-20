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
 * - Returned data buffers owned by spice_result_t remain valid until
 *   spice_result_destroy() is called.
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

typedef enum spice_backend_t {
    SPICE_BACKEND_AUTO = 0,
    SPICE_BACKEND_KLU = 1,
    SPICE_BACKEND_SPARSE_LU = 2,
    SPICE_BACKEND_DENSE_LU = 3
} spice_backend_t;

typedef enum spice_integration_method_t {
    SPICE_METHOD_BACKWARD_EULER = 0,
    SPICE_METHOD_TRAPEZOIDAL = 1
} spice_integration_method_t;

typedef enum spice_startup_mode_t {
    SPICE_STARTUP_OP = 0,
    SPICE_STARTUP_UIC = 1
} spice_startup_mode_t;

typedef struct spice_engine spice_engine_t;
typedef struct spice_circuit spice_circuit_t;
typedef struct spice_result spice_result_t;

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

/* Engine lifecycle & diagnostics */
SPICE_CAPI_EXPORT spice_status_t spice_engine_create(
    const spice_engine_options_t* options,
    spice_engine_t** out_engine);

SPICE_CAPI_EXPORT void spice_engine_destroy(spice_engine_t* engine);

SPICE_CAPI_EXPORT spice_status_t spice_engine_capabilities(
    const spice_engine_t* engine,
    spice_capabilities_t* out_capabilities);

SPICE_CAPI_EXPORT spice_status_t spice_engine_last_error(
    const spice_engine_t* engine,
    char* buffer,
    size_t buffer_bytes,
    size_t* out_required_bytes);

/* Circuit construction (node "0" is always ground; other node names auto-register). */
SPICE_CAPI_EXPORT spice_status_t spice_circuit_create(
    const char* name,
    spice_circuit_t** out_circuit);

SPICE_CAPI_EXPORT void spice_circuit_destroy(spice_circuit_t* circuit);

SPICE_CAPI_EXPORT spice_status_t spice_circuit_add_resistor(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double ohms);

SPICE_CAPI_EXPORT spice_status_t spice_circuit_add_capacitor(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double farads,
    int has_ic,
    double ic_volts);

SPICE_CAPI_EXPORT spice_status_t spice_circuit_add_inductor(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double henries,
    int has_ic,
    double ic_amps);

SPICE_CAPI_EXPORT spice_status_t spice_circuit_add_voltage_source(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double dc_volts,
    double ac_real,
    double ac_imag);

SPICE_CAPI_EXPORT spice_status_t spice_circuit_add_current_source(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    double dc_amps,
    double ac_real,
    double ac_imag);

SPICE_CAPI_EXPORT spice_status_t spice_circuit_add_vccs(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    const char* ctrl_p,
    const char* ctrl_n,
    double siemens);

SPICE_CAPI_EXPORT spice_status_t spice_circuit_add_vcvs(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    const char* ctrl_p,
    const char* ctrl_n,
    double gain);

SPICE_CAPI_EXPORT spice_status_t spice_circuit_add_cccs(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    const char* control_source_id,
    double gain);

SPICE_CAPI_EXPORT spice_status_t spice_circuit_add_ccvs(
    spice_circuit_t* circuit,
    const char* id,
    const char* node_p,
    const char* node_n,
    const char* control_source_id,
    double ohms);

/* Analysis execution */
SPICE_CAPI_EXPORT spice_status_t spice_run_op(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    spice_result_t** out_result);

SPICE_CAPI_EXPORT spice_status_t spice_run_ac(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    const double* frequencies_hz,
    size_t frequency_count,
    spice_result_t** out_result);

SPICE_CAPI_EXPORT spice_status_t spice_run_transient(
    const spice_engine_t* engine,
    const spice_circuit_t* circuit,
    spice_backend_t backend,
    double step_s,
    double stop_s,
    spice_integration_method_t method,
    spice_startup_mode_t startup,
    spice_result_t** out_result);

/* Result inspection (contiguous row-major buffers: sample * signal_count + signal_idx). */
SPICE_CAPI_EXPORT void spice_result_destroy(spice_result_t* result);

SPICE_CAPI_EXPORT spice_status_t spice_result_signal_count(
    const spice_result_t* result,
    size_t* out_count);

SPICE_CAPI_EXPORT spice_status_t spice_result_signal_name(
    const spice_result_t* result,
    size_t signal_index,
    const char** out_name);

SPICE_CAPI_EXPORT spice_status_t spice_result_sample_count(
    const spice_result_t* result,
    size_t* out_count);

/* Borrowed pointer to axis values (time_s for OP/TRAN, frequency_hz for AC). */
SPICE_CAPI_EXPORT spice_status_t spice_result_axis_data(
    const spice_result_t* result,
    const double** out_axis);

/* Borrowed pointer to real values (length = sample_count * signal_count) for OP/TRAN. */
SPICE_CAPI_EXPORT spice_status_t spice_result_real_data(
    const spice_result_t* result,
    const double** out_values);

/* Borrowed pointer to interleaved (re, im) complex values (2 * sample_count * signal_count) for AC. */
SPICE_CAPI_EXPORT spice_status_t spice_result_complex_data(
    const spice_result_t* result,
    const double** out_interleaved_re_im);

SPICE_CAPI_EXPORT spice_status_t spice_result_audit_passed(
    const spice_result_t* result,
    int* out_passed);

#ifdef __cplusplus
}
#endif

#endif /* SPICE_V2_CAPI_H */
