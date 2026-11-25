#ifndef SPICE_V2_OSDI_ABI_H
#define SPICE_V2_OSDI_ABI_H

/*
 * Official Binary-Compatible OSDI 0.3 / 0.4 Interface Definitions (ARCH-MOD-001).
 *
 * Exact C ABI layout matching OpenVAF / OpenVAF-Reloaded osdi_0_3.h & osdi_0_4.h
 * for dynamically loaded Verilog-A compact models (.osdi / .so / .dll).
 * Confined strictly to cpp/osdi; never exposed in public or core circuit headers.
 */

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define SPICE_OSDI_VERSION_MAJOR_CURR 0u
#define SPICE_OSDI_VERSION_MINOR_CURR 4u

/* Parameter type & kind bitflags */
#define SPICE_OSDI_PARA_TY_MASK   0x03u
#define SPICE_OSDI_PARA_TY_REAL   0x00u
#define SPICE_OSDI_PARA_TY_INT    0x01u
#define SPICE_OSDI_PARA_TY_STR    0x02u

#define SPICE_OSDI_PARA_KIND_MASK  (0x03u << 30)
#define SPICE_OSDI_PARA_KIND_MODEL (0x00u << 30)
#define SPICE_OSDI_PARA_KIND_INST  (0x01u << 30)
#define SPICE_OSDI_PARA_KIND_OPVAR (0x02u << 30)

#define SPICE_OSDI_ACCESS_FLAG_READ     0x00u
#define SPICE_OSDI_ACCESS_FLAG_SET      0x01u
#define SPICE_OSDI_ACCESS_FLAG_INSTANCE 0x04u

/* Official OSDI 0.3/0.4 Jacobian entry flags */
#define SPICE_OSDI_JACOBIAN_ENTRY_RESIST_CONST 0x01u
#define SPICE_OSDI_JACOBIAN_ENTRY_REACT_CONST  0x02u
#define SPICE_OSDI_JACOBIAN_ENTRY_RESIST       0x04u
#define SPICE_OSDI_JACOBIAN_ENTRY_REACT        0x08u

/* Official OSDI 0.3/0.4 evaluation request flags */
#define SPICE_OSDI_CALC_RESIST_RESIDUAL 0x00001u
#define SPICE_OSDI_CALC_REACT_RESIDUAL  0x00002u
#define SPICE_OSDI_CALC_RESIST_JACOBIAN 0x00004u
#define SPICE_OSDI_CALC_REACT_JACOBIAN  0x00008u
#define SPICE_OSDI_CALC_NOISE           0x00010u
#define SPICE_OSDI_CALC_OP              0x00020u
#define SPICE_OSDI_CALC_RESIST_LIM_RHS  0x00040u
#define SPICE_OSDI_CALC_REACT_LIM_RHS   0x00080u
#define SPICE_OSDI_ENABLE_LIM           0x00100u
#define SPICE_OSDI_INIT_LIM             0x00200u
#define SPICE_OSDI_ANALYSIS_NOISE       0x00400u
#define SPICE_OSDI_ANALYSIS_DC          0x00800u
#define SPICE_OSDI_ANALYSIS_AC          0x01000u
#define SPICE_OSDI_ANALYSIS_TRAN        0x02000u
#define SPICE_OSDI_ANALYSIS_IC          0x04000u
#define SPICE_OSDI_ANALYSIS_STATIC      0x08000u
#define SPICE_OSDI_ANALYSIS_NODESET     0x10000u

/* Evaluation return flags */
#define SPICE_OSDI_EVAL_RET_FLAG_LIM    0x01u
#define SPICE_OSDI_EVAL_RET_FLAG_FATAL  0x02u
#define SPICE_OSDI_EVAL_RET_FLAG_FINISH 0x04u
#define SPICE_OSDI_EVAL_RET_FLAG_STOP   0x08u

/* Log level flags */
#define SPICE_OSDI_LOG_LVL_MASK    7u
#define SPICE_OSDI_LOG_LVL_DEBUG   0u
#define SPICE_OSDI_LOG_LVL_DISPLAY 1u
#define SPICE_OSDI_LOG_LVL_INFO    2u
#define SPICE_OSDI_LOG_LVL_WARN    3u
#define SPICE_OSDI_LOG_LVL_ERR     4u
#define SPICE_OSDI_LOG_LVL_FATAL   5u
#define SPICE_OSDI_LOG_FMT_ERR     16u

#define SPICE_OSDI_INIT_ERR_OUT_OF_BOUNDS 1u

#define SPICE_OSDI_NOISE_TYPE_WHITE   0u
#define SPICE_OSDI_NOISE_TYPE_FLICKER 1u
#define SPICE_OSDI_NOISE_TYPE_TABLE   2u

typedef struct SpiceOsdiLimFunction {
    const char* name;
    uint32_t num_args;
    void* func_ptr;
} SpiceOsdiLimFunction;

typedef struct SpiceOsdiSimParas {
    const char* const* names;
    const double* vals;
    const char* const* names_str;
    const char* const* vals_str;
} SpiceOsdiSimParas;

typedef struct SpiceOsdiSimInfo {
    SpiceOsdiSimParas paras;
    double abstime;
    const double* prev_solve;
    double* prev_state;
    double* next_state;
    uint32_t flags;
} SpiceOsdiSimInfo;

typedef union SpiceOsdiInitErrorPayload {
    uint32_t parameter_id;
} SpiceOsdiInitErrorPayload;

typedef struct SpiceOsdiInitError {
    uint32_t code;
    SpiceOsdiInitErrorPayload payload;
} SpiceOsdiInitError;

typedef struct SpiceOsdiInitInfo {
    uint32_t flags;
    uint32_t num_errors;
    SpiceOsdiInitError* errors;
} SpiceOsdiInitInfo;

typedef struct SpiceOsdiNodePair {
    uint32_t node_1;
    uint32_t node_2;
} SpiceOsdiNodePair;

typedef struct SpiceOsdiJacobianEntry {
    SpiceOsdiNodePair nodes;
    uint32_t react_ptr_off;
    uint32_t flags;
} SpiceOsdiJacobianEntry;

typedef struct SpiceOsdiNode {
    const char* name;
    const char* units;
    const char* residual_units;
    uint32_t resist_residual_off;
    uint32_t react_residual_off;
    uint32_t resist_limit_rhs_off;
    uint32_t react_limit_rhs_off;
    bool is_flow;
} SpiceOsdiNode;

typedef struct SpiceOsdiParamOpvar {
    const char* const* name;
    uint32_t num_alias;
    const char* description;
    const char* units;
    uint32_t flags;
    uint32_t len;
} SpiceOsdiParamOpvar;

typedef struct SpiceOsdiNoiseSource {
    const char* name;
    SpiceOsdiNodePair nodes;
} SpiceOsdiNoiseSource;

typedef struct SpiceOsdiAbsDelayInfo {
    uint32_t input_node_1;
    uint32_t input_node_2;
    uint32_t output_node;
    uint32_t delay_offset;
    uint32_t max_delay_offset;
} SpiceOsdiAbsDelayInfo;

typedef struct SpiceOsdiNatureRef {
    uint32_t ref_type;
    uint32_t index;
} SpiceOsdiNatureRef;

/*
 * Official OSDI 0.3/0.4 Descriptor Layout.
 * Fields from `name` through `load_jacobian_tran` form the exact OSDI 0.3 prefix;
 * fields from `given_flag_model` onward are OSDI 0.4 extensions.
 * Libraries export `OSDI_DESCRIPTOR_SIZE` for stride-safe traversal across versions.
 */
typedef struct SpiceOsdiDescriptor {
    const char* name;

    uint32_t num_nodes;
    uint32_t num_terminals;
    const SpiceOsdiNode* nodes;

    uint32_t num_jacobian_entries;
    const SpiceOsdiJacobianEntry* jacobian_entries;

    uint32_t num_collapsible;
    const SpiceOsdiNodePair* collapsible;
    uint32_t collapsed_offset;

    const SpiceOsdiNoiseSource* noise_sources;
    uint32_t num_noise_src;

    uint32_t num_params;
    uint32_t num_instance_params;
    uint32_t num_opvars;
    const SpiceOsdiParamOpvar* param_opvar;

    uint32_t node_mapping_offset;
    uint32_t jacobian_ptr_resist_offset;

    uint32_t num_states;
    uint32_t state_idx_off;

    uint32_t bound_step_offset;

    uint32_t instance_size;
    uint32_t model_size;

    void* (*access)(void* inst, void* model, uint32_t id, uint32_t flags);

    void (*setup_model)(void* handle, void* model, const SpiceOsdiSimParas* sim_params, SpiceOsdiInitInfo* res);
    void (*setup_instance)(
        void* handle,
        void* inst,
        void* model,
        double temperature,
        uint32_t num_terminals,
        const SpiceOsdiSimParas* sim_params,
        SpiceOsdiInitInfo* res);

    uint32_t (*eval)(void* handle, void* inst, void* model, const SpiceOsdiSimInfo* info);
    void (*load_noise)(void* inst, void* model, double freq, double* noise_dens);
    void (*load_residual_resist)(void* inst, void* model, double* dst);
    void (*load_residual_react)(void* inst, void* model, double* dst);
    void (*load_limit_rhs_resist)(void* inst, void* model, double* dst);
    void (*load_limit_rhs_react)(void* inst, void* model, double* dst);
    void (*load_spice_rhs_dc)(void* inst, void* model, double* dst, double* prev_solve);
    void (*load_spice_rhs_tran)(void* inst, void* model, double* dst, double* prev_solve, double alpha);
    void (*load_jacobian_resist)(void* inst, void* model);
    void (*load_jacobian_react)(void* inst, void* model, double alpha);
    void (*load_jacobian_tran)(void* inst, void* model, double alpha);

    /* OSDI 0.4+ extensions (only accessed when descriptor_size >= sizeof(SpiceOsdiDescriptor)) */
    uint32_t (*given_flag_model)(void* model, uint32_t id);
    uint32_t (*given_flag_instance)(void* inst, uint32_t id);
    uint32_t num_resistive_jacobian_entries;
    uint32_t num_reactive_jacobian_entries;
    void (*write_jacobian_array_resist)(void* inst, void* model, double* destination);
    void (*write_jacobian_array_react)(void* inst, void* model, double* destination);
    uint32_t num_inputs;
    const SpiceOsdiNodePair* inputs;
    void (*load_jacobian_with_offset_resist)(void* inst, void* model, size_t offset);
    void (*load_jacobian_with_offset_react)(void* inst, void* model, size_t offset);
    const SpiceOsdiNatureRef* unknown_nature;
    const SpiceOsdiNatureRef* residual_nature;
    const uint32_t* noise_source_type;
    void (*load_noise_params)(void* inst, void* model, double* power, double* exponent);
    uint32_t absdelay_count;
    const SpiceOsdiAbsDelayInfo* absdelay_info;
} SpiceOsdiDescriptor;

typedef void (*SpiceOsdiLogFn)(void* handle, const char* msg, uint32_t lvl);

#ifdef __cplusplus
}
#endif

#endif /* SPICE_V2_OSDI_ABI_H */
