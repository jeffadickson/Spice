#ifndef SPICE_V2_OSDI_ABI_H
#define SPICE_V2_OSDI_ABI_H

/*
 * OSDI 0.3 / 0.4 Binary Interface Definitions (ARCH-MOD-001).
 *
 * Self-contained C ABI header matching the OpenVAF / OSDI specification for
 * dynamically loaded Verilog-A compact models. Confined strictly to cpp/osdi;
 * never exposed in public or core circuit headers.
 */

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

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

/* Jacobian entry flags */
#define SPICE_OSDI_JACOBIAN_ENTRY_RESIST       0x01u
#define SPICE_OSDI_JACOBIAN_ENTRY_REACT        0x02u
#define SPICE_OSDI_JACOBIAN_ENTRY_RESIST_CONST 0x04u
#define SPICE_OSDI_JACOBIAN_ENTRY_REACT_CONST  0x08u

/* Evaluation request flags */
#define SPICE_OSDI_CALC_RESIST_RESIDUAL 0x0001u
#define SPICE_OSDI_CALC_REACT_RESIDUAL  0x0002u
#define SPICE_OSDI_CALC_RESIST_JACOBIAN 0x0004u
#define SPICE_OSDI_CALC_REACT_JACOBIAN  0x0008u
#define SPICE_OSDI_CALC_NOISE           0x0010u
#define SPICE_OSDI_ANALYSIS_NOISE       0x0100u
#define SPICE_OSDI_ANALYSIS_DC          0x0200u
#define SPICE_OSDI_ANALYSIS_AC          0x0400u
#define SPICE_OSDI_ANALYSIS_TRAN        0x0800u
#define SPICE_OSDI_ANALYSIS_IC          0x1000u
#define SPICE_OSDI_ANALYSIS_STATIC      0x2000u

/* Evaluation return flags */
#define SPICE_OSDI_EVAL_RET_FLAG_LIM    0x01u
#define SPICE_OSDI_EVAL_RET_FLAG_FATAL  0x02u
#define SPICE_OSDI_EVAL_RET_FLAG_FINISH 0x04u

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

typedef struct SpiceOsdiInitError {
    uint32_t code;
    uint32_t param_id;
} SpiceOsdiInitError;

typedef struct SpiceOsdiInitInfo {
    uint32_t flags;
    uint32_t num_errors;
    SpiceOsdiInitError* errors;
} SpiceOsdiInitInfo;

/*
 * Core OSDI 0.3/0.4 descriptor table exported by compiled Verilog-A libraries.
 * Note: exact field order matches the subset used by Spice V2's OSDI loader.
 */
typedef struct SpiceOsdiDescriptor {
    const char* name;
    uint32_t num_nodes;
    uint32_t num_terminals;
    const SpiceOsdiNode* nodes;

    uint32_t num_jacobian_entries;
    const SpiceOsdiJacobianEntry* jacobian_entries;

    uint32_t num_params;
    uint32_t num_instance_params;
    const SpiceOsdiParamOpvar* param_opvar;

    uint32_t node_mapping_offset;
    uint32_t jacobian_ptr_resist_offset;
    uint32_t num_states;
    uint32_t state_idx_off;

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

    void (*load_residual_resist)(void* inst, void* model, double* dst);
    void (*load_residual_react)(void* inst, void* model, double* dst);
    void (*load_jacobian_resist)(void* inst, void* model);
    void (*load_jacobian_react)(void* inst, void* model, double alpha);
} SpiceOsdiDescriptor;

#ifdef __cplusplus
}
#endif

#endif /* SPICE_V2_OSDI_ABI_H */
