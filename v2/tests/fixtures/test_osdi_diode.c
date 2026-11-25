/*
 * Test OSDI 0.3 Shared Library Fixture: Nonlinear Shockley + Parallel-C Diode.
 * Compiled into a real .so/.dll and dynamically loaded via dlopen/LoadLibrary
 * to qualify Spice V2's generic OSDI loader end-to-end (ARCH-MOD-001).
 */
#include "osdi/osdi_abi.h"

#include <math.h>
#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#  define OSDI_EXPORT __declspec(dllexport)
#else
#  define OSDI_EXPORT __attribute__((visibility("default")))
#endif

typedef struct DiodeModelData {
    double is_amps;
    double vt_volts;
    double c_farads;
    int has_is;
    int has_vt;
    int has_c;
} DiodeModelData;

typedef struct DiodeInstData {
    uint32_t node_mapping[2];
    double* jac_resist[4];
    double* jac_react[4];
    double area;
    int has_area;
    double id;
    double gd;
    double qd;
    double cd;
} DiodeInstData;

static const char* const kIsNames[] = {"is"};
static const char* const kVtNames[] = {"vt"};
static const char* const kCNames[] = {"cjo"};
static const char* const kAreaNames[] = {"area"};

static const SpiceOsdiParamOpvar kParams[4] = {
    {kIsNames, 0u, "Saturation current", "A", SPICE_OSDI_PARA_TY_REAL | SPICE_OSDI_PARA_KIND_MODEL, 0u},
    {kVtNames, 0u, "Thermal voltage", "V", SPICE_OSDI_PARA_TY_REAL | SPICE_OSDI_PARA_KIND_MODEL, 0u},
    {kCNames, 0u, "Parallel capacitance", "F", SPICE_OSDI_PARA_TY_REAL | SPICE_OSDI_PARA_KIND_MODEL, 0u},
    {kAreaNames, 0u, "Instance area scale", "", SPICE_OSDI_PARA_TY_REAL | SPICE_OSDI_PARA_KIND_INST, 0u},
};

static const SpiceOsdiNode kNodes[2] = {
    {"a", "V", "A", 0u, 0u, 0u, 0u, false},
    {"c", "V", "A", 0u, 0u, 0u, 0u, false},
};

static const SpiceOsdiJacobianEntry kJacEntries[4] = {
    {{0u, 0u}, (uint32_t)offsetof(DiodeInstData, jac_react[0]),
     SPICE_OSDI_JACOBIAN_ENTRY_RESIST | SPICE_OSDI_JACOBIAN_ENTRY_REACT},
    {{0u, 1u}, (uint32_t)offsetof(DiodeInstData, jac_react[1]),
     SPICE_OSDI_JACOBIAN_ENTRY_RESIST | SPICE_OSDI_JACOBIAN_ENTRY_REACT},
    {{1u, 0u}, (uint32_t)offsetof(DiodeInstData, jac_react[2]),
     SPICE_OSDI_JACOBIAN_ENTRY_RESIST | SPICE_OSDI_JACOBIAN_ENTRY_REACT},
    {{1u, 1u}, (uint32_t)offsetof(DiodeInstData, jac_react[3]),
     SPICE_OSDI_JACOBIAN_ENTRY_RESIST | SPICE_OSDI_JACOBIAN_ENTRY_REACT},
};

static void* diode_access(void* inst_v, void* model_v, uint32_t id, uint32_t flags) {
    DiodeModelData* m = (DiodeModelData*)model_v;
    DiodeInstData* inst = (DiodeInstData*)inst_v;
    const int is_set = (flags & SPICE_OSDI_ACCESS_FLAG_SET) != 0u;
    switch (id) {
        case 0u:
            if (is_set) m->has_is = 1;
            return &m->is_amps;
        case 1u:
            if (is_set) m->has_vt = 1;
            return &m->vt_volts;
        case 2u:
            if (is_set) m->has_c = 1;
            return &m->c_farads;
        case 3u:
            if (inst == NULL) return NULL;
            if (is_set) inst->has_area = 1;
            return &inst->area;
        default:
            return NULL;
    }
}

static void diode_setup_model(
    void* handle,
    void* model_v,
    const SpiceOsdiSimParas* sim_params,
    SpiceOsdiInitInfo* res) {
    (void)handle;
    (void)sim_params;
    DiodeModelData* m = (DiodeModelData*)model_v;
    if (!m->has_is) m->is_amps = 1.0e-14;
    if (!m->has_vt) m->vt_volts = 0.025851991;
    if (!m->has_c) m->c_farads = 0.0;
    if (m->is_amps <= 0.0 || m->vt_volts <= 0.0 || m->c_farads < 0.0) {
        res->flags = SPICE_OSDI_EVAL_RET_FLAG_FATAL;
    }
}

static void diode_setup_instance(
    void* handle,
    void* inst_v,
    void* model_v,
    double temperature,
    uint32_t num_terminals,
    const SpiceOsdiSimParas* sim_params,
    SpiceOsdiInitInfo* res) {
    (void)handle;
    (void)model_v;
    (void)temperature;
    (void)num_terminals;
    (void)sim_params;
    DiodeInstData* inst = (DiodeInstData*)inst_v;
    if (!inst->has_area) inst->area = 1.0;
    if (inst->area <= 0.0) {
        res->flags = SPICE_OSDI_EVAL_RET_FLAG_FATAL;
    }
}

static uint32_t diode_eval(
    void* handle,
    void* inst_v,
    void* model_v,
    const SpiceOsdiSimInfo* info) {
    (void)handle;
    DiodeInstData* inst = (DiodeInstData*)inst_v;
    const DiodeModelData* m = (const DiodeModelData*)model_v;
    const double va = info->prev_solve[inst->node_mapping[0]];
    const double vc = info->prev_solve[inst->node_mapping[1]];
    const double vd = va - vc;
    const double arg = vd / m->vt_volts;
    if (arg > 700.0) {
        return SPICE_OSDI_EVAL_RET_FLAG_FATAL;
    }
    const double is_eff = m->is_amps * inst->area;
    const double c_eff = m->c_farads * inst->area;
    const double ev = exp(arg);
    inst->id = is_eff * expm1(arg);
    inst->gd = (is_eff * ev) / m->vt_volts;
    inst->qd = c_eff * vd;
    inst->cd = c_eff;
    return 0u;
}

static void diode_load_res_resist(void* inst_v, void* model_v, double* dst) {
    (void)model_v;
    const DiodeInstData* inst = (const DiodeInstData*)inst_v;
    dst[inst->node_mapping[0]] += inst->id;
    dst[inst->node_mapping[1]] -= inst->id;
}

static void diode_load_res_react(void* inst_v, void* model_v, double* dst) {
    (void)model_v;
    const DiodeInstData* inst = (const DiodeInstData*)inst_v;
    dst[inst->node_mapping[0]] += inst->qd;
    dst[inst->node_mapping[1]] -= inst->qd;
}

static void diode_load_jac_resist(void* inst_v, void* model_v) {
    (void)model_v;
    const DiodeInstData* inst = (const DiodeInstData*)inst_v;
    *inst->jac_resist[0] += inst->gd;
    *inst->jac_resist[1] -= inst->gd;
    *inst->jac_resist[2] -= inst->gd;
    *inst->jac_resist[3] += inst->gd;
}

static void diode_load_jac_react(void* inst_v, void* model_v, double alpha) {
    (void)model_v;
    const DiodeInstData* inst = (const DiodeInstData*)inst_v;
    const double g = alpha * inst->cd;
    *inst->jac_react[0] += g;
    *inst->jac_react[1] -= g;
    *inst->jac_react[2] -= g;
    *inst->jac_react[3] += g;
}

OSDI_EXPORT const uint32_t OSDI_VERSION_MAJOR = 0u;
OSDI_EXPORT const uint32_t OSDI_VERSION_MINOR = 3u;
OSDI_EXPORT const uint32_t OSDI_NUM_DESCRIPTORS = 1u;
OSDI_EXPORT const uint32_t OSDI_DESCRIPTOR_SIZE = (uint32_t)sizeof(SpiceOsdiDescriptor);

OSDI_EXPORT const SpiceOsdiDescriptor OSDI_DESCRIPTORS[1] = {
    {
        "osdi_test_diode",
        2u,                                           /* num_nodes */
        2u,                                           /* num_terminals */
        kNodes,                                       /* nodes */
        4u,                                           /* num_jacobian_entries */
        kJacEntries,                                  /* jacobian_entries */
        0u,                                           /* num_collapsible */
        NULL,                                         /* collapsible */
        0u,                                           /* collapsed_offset */
        NULL,                                         /* noise_sources */
        0u,                                           /* num_noise_src */
        4u,                                           /* num_params */
        1u,                                           /* num_instance_params */
        0u,                                           /* num_opvars */
        kParams,                                      /* param_opvar */
        (uint32_t)offsetof(DiodeInstData, node_mapping),
        (uint32_t)offsetof(DiodeInstData, jac_resist),
        0u,                                           /* num_states */
        0u,                                           /* state_idx_off */
        0u,                                           /* bound_step_offset */
        (uint32_t)sizeof(DiodeInstData),
        (uint32_t)sizeof(DiodeModelData),
        diode_access,
        diode_setup_model,
        diode_setup_instance,
        diode_eval,
        NULL,                                         /* load_noise */
        diode_load_res_resist,
        diode_load_res_react,
        NULL,                                         /* load_limit_rhs_resist */
        NULL,                                         /* load_limit_rhs_react */
        NULL,                                         /* load_spice_rhs_dc */
        NULL,                                         /* load_spice_rhs_tran */
        diode_load_jac_resist,
        diode_load_jac_react,
        NULL,                                         /* load_jacobian_tran */
        NULL,                                         /* given_flag_model */
        NULL,                                         /* given_flag_instance */
        4u,                                           /* num_resistive_jacobian_entries */
        4u,                                           /* num_reactive_jacobian_entries */
        NULL,                                         /* write_jacobian_array_resist */
        NULL,                                         /* write_jacobian_array_react */
        0u,                                           /* num_inputs */
        NULL,                                         /* inputs */
        NULL,                                         /* load_jacobian_with_offset_resist */
        NULL,                                         /* load_jacobian_with_offset_react */
        NULL,                                         /* unknown_nature */
        NULL,                                         /* residual_nature */
        NULL,                                         /* noise_source_type */
        NULL,                                         /* load_noise_params */
        0u,                                           /* absdelay_count */
        NULL,                                         /* absdelay_info */
    },
};
