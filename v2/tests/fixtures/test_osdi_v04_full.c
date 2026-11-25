/*
 * Cross-Platform OSDI 0.4 Full-Feature C Fixture (Linux + Windows MSVC).
 * Exercises every Slice B OSDI 0.4 feature without requiring OpenVAF at CTest time:
 *   - Collapsible internal node `ai` (collapsed to `a` when rs == 0, uncollapsed when rs > 0)
 *   - OSDI_LIM_TABLE ("pnjlim") + load_limit_rhs_resist + EVAL_RET_FLAG_LIM
 *   - OSDI_LOG callback invocation
 *   - $simparam("gmin") lookup from SpiceOsdiSimParas
 *   - load_noise (shot + thermal_rs)
 */
#include "osdi/osdi_abi.h"

#include <math.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

#if defined(_WIN32)
#  define OSDI_EXPORT __declspec(dllexport)
#else
#  define OSDI_EXPORT __attribute__((visibility("default")))
#endif

typedef double (*PnjLimFn)(bool init, bool* check, double vnew, double vold, double vt, double vcrit);

OSDI_EXPORT SpiceOsdiLogFn OSDI_LOG = NULL;
OSDI_EXPORT const uint32_t OSDI_LIM_TABLE_LEN = 1u;
OSDI_EXPORT SpiceOsdiLimFunction OSDI_LIM_TABLE[1] = {
    {"pnjlim", 2u, NULL},
};

typedef struct V04DiodeModel {
    double is_amps;
    double n_em;
    double rs_ohms;
    double cjo_f;
    int has_is;
    int has_n;
    int has_rs;
    int has_cjo;
} V04DiodeModel;

typedef struct V04DiodeInst {
    uint32_t node_mapping[3];
    uint32_t state_idx[1];
    bool collapsed[1];
    double* jac_resist[7];
    double* jac_react[7];
    double temp_k;
    double id;
    double gd;
    double qd;
    double cd;
    double i_rs;
    double g_rs;
    double lim_rhs_ai;
    double lim_rhs_c;
} V04DiodeInst;

static const char* const kIsNames[] = {"is"};
static const char* const kNNames[] = {"n"};
static const char* const kRsNames[] = {"rs"};
static const char* const kCjoNames[] = {"cjo"};

static const SpiceOsdiParamOpvar kParams[4] = {
    {kIsNames, 0u, "Saturation current", "A", SPICE_OSDI_PARA_TY_REAL | SPICE_OSDI_PARA_KIND_MODEL, 0u},
    {kNNames, 0u, "Emission coeff", "", SPICE_OSDI_PARA_TY_REAL | SPICE_OSDI_PARA_KIND_MODEL, 0u},
    {kRsNames, 0u, "Series resistance", "Ohm", SPICE_OSDI_PARA_TY_REAL | SPICE_OSDI_PARA_KIND_MODEL, 0u},
    {kCjoNames, 0u, "Junction capacitance", "F", SPICE_OSDI_PARA_TY_REAL | SPICE_OSDI_PARA_KIND_MODEL, 0u},
};

static const SpiceOsdiNode kNodes[3] = {
    {"a", "V", "A", 0u, 0u, 0u, 0u, false},
    {"c", "V", "A", 0u, 0u, 0u, 0u, false},
    {"ai", "V", "A", 0u, 0u, 0u, 0u, false},
};

static const SpiceOsdiNodePair kCollapsible[1] = {
    {0u, 2u},
};

static const SpiceOsdiNoiseSource kNoiseSources[2] = {
    {"shot", {2u, 1u}},
    {"thermal_rs", {0u, 2u}},
};

static const SpiceOsdiJacobianEntry kJacEntries[7] = {
    {{0u, 0u}, (uint32_t)offsetof(V04DiodeInst, jac_react[0]), SPICE_OSDI_JACOBIAN_ENTRY_RESIST},
    {{0u, 2u}, (uint32_t)offsetof(V04DiodeInst, jac_react[1]), SPICE_OSDI_JACOBIAN_ENTRY_RESIST},
    {{2u, 0u}, (uint32_t)offsetof(V04DiodeInst, jac_react[2]), SPICE_OSDI_JACOBIAN_ENTRY_RESIST},
    {{2u, 2u}, (uint32_t)offsetof(V04DiodeInst, jac_react[3]),
     SPICE_OSDI_JACOBIAN_ENTRY_RESIST | SPICE_OSDI_JACOBIAN_ENTRY_REACT},
    {{2u, 1u}, (uint32_t)offsetof(V04DiodeInst, jac_react[4]),
     SPICE_OSDI_JACOBIAN_ENTRY_RESIST | SPICE_OSDI_JACOBIAN_ENTRY_REACT},
    {{1u, 2u}, (uint32_t)offsetof(V04DiodeInst, jac_react[5]),
     SPICE_OSDI_JACOBIAN_ENTRY_RESIST | SPICE_OSDI_JACOBIAN_ENTRY_REACT},
    {{1u, 1u}, (uint32_t)offsetof(V04DiodeInst, jac_react[6]),
     SPICE_OSDI_JACOBIAN_ENTRY_RESIST | SPICE_OSDI_JACOBIAN_ENTRY_REACT},
};

static double lookup_simparam(const SpiceOsdiSimParas* sp, const char* key, double fallback) {
    if (sp == NULL || sp->names == NULL || sp->vals == NULL) return fallback;
    for (size_t i = 0; sp->names[i] != NULL; ++i) {
        if (strcmp(sp->names[i], key) == 0) return sp->vals[i];
    }
    return fallback;
}

static void* v04_access(void* inst_v, void* model_v, uint32_t id, uint32_t flags) {
    (void)inst_v;
    V04DiodeModel* m = (V04DiodeModel*)model_v;
    const int is_set = (flags & SPICE_OSDI_ACCESS_FLAG_SET) != 0u;
    switch (id) {
        case 0u:
            if (is_set) m->has_is = 1;
            return &m->is_amps;
        case 1u:
            if (is_set) m->has_n = 1;
            return &m->n_em;
        case 2u:
            if (is_set) m->has_rs = 1;
            return &m->rs_ohms;
        case 3u:
            if (is_set) m->has_cjo = 1;
            return &m->cjo_f;
        default:
            return NULL;
    }
}

static void v04_setup_model(
    void* handle,
    void* model_v,
    const SpiceOsdiSimParas* sim_params,
    SpiceOsdiInitInfo* res) {
    (void)sim_params;
    V04DiodeModel* m = (V04DiodeModel*)model_v;
    if (!m->has_is) m->is_amps = 1.0e-14;
    if (!m->has_n) m->n_em = 1.0;
    if (!m->has_rs) m->rs_ohms = 0.0;
    if (!m->has_cjo) m->cjo_f = 0.0;
    if (OSDI_LOG != NULL) {
        OSDI_LOG(handle, "v04_diode setup_model ok", SPICE_OSDI_LOG_LVL_INFO);
    }
    if (m->is_amps <= 0.0 || m->n_em <= 0.0 || m->rs_ohms < 0.0 || m->cjo_f < 0.0) {
        res->flags = SPICE_OSDI_EVAL_RET_FLAG_FATAL;
    }
}

static void v04_setup_instance(
    void* handle,
    void* inst_v,
    void* model_v,
    double temperature,
    uint32_t num_terminals,
    const SpiceOsdiSimParas* sim_params,
    SpiceOsdiInitInfo* res) {
    (void)handle;
    (void)num_terminals;
    (void)sim_params;
    (void)res;
    V04DiodeInst* inst = (V04DiodeInst*)inst_v;
    const V04DiodeModel* m = (const V04DiodeModel*)model_v;
    inst->temp_k = temperature;
    inst->collapsed[0] = (m->rs_ohms <= 0.0);
}

static uint32_t v04_eval(
    void* handle,
    void* inst_v,
    void* model_v,
    const SpiceOsdiSimInfo* info) {
    (void)handle;
    V04DiodeInst* inst = (V04DiodeInst*)inst_v;
    const V04DiodeModel* m = (const V04DiodeModel*)model_v;
    const double gmin = lookup_simparam(&info->paras, "gmin", 1.0e-12);
    const double va = info->prev_solve[inst->node_mapping[0]];
    const double vc = info->prev_solve[inst->node_mapping[1]];
    const double vai = info->prev_solve[inst->node_mapping[2]];
    const double vd_raw = vai - vc;
    const double vt = 8.617333262e-5 * inst->temp_k;
    const double nvt = m->n_em * vt;
    const double vcrit = nvt * log(nvt / (1.4142135623730951 * m->is_amps));

    double vd = vd_raw;
    uint32_t ret = 0u;
    if ((info->flags & SPICE_OSDI_ENABLE_LIM) != 0u && OSDI_LIM_TABLE[0].func_ptr != NULL &&
        info->prev_state != NULL && info->next_state != NULL) {
        PnjLimFn fn = NULL;
        memcpy(&fn, &OSDI_LIM_TABLE[0].func_ptr, sizeof(fn));
        const bool init = ((info->flags & SPICE_OSDI_INIT_LIM) != 0u);
        bool checked = false;
        const uint32_t sidx = inst->state_idx[0];
        vd = fn(init, &checked, vd_raw, info->prev_state[sidx], nvt, vcrit);
        info->next_state[sidx] = vd;
        if (checked) ret |= SPICE_OSDI_EVAL_RET_FLAG_LIM;
    }

    const double arg = vd / nvt;
    if (arg > 40.0) {
        const double e40 = exp(40.0);
        inst->gd = (m->is_amps * e40) / nvt + gmin;
        inst->id = m->is_amps * (e40 * (1.0 + arg - 40.0) - 1.0) + gmin * vd;
    } else if (arg < -80.0) {
        inst->gd = gmin;
        inst->id = -m->is_amps + gmin * vd;
    } else {
        const double ev = exp(arg);
        inst->gd = (m->is_amps * ev) / nvt + gmin;
        inst->id = m->is_amps * expm1(arg) + gmin * vd;
    }
    inst->qd = m->cjo_f * vd;
    inst->cd = m->cjo_f;

    /* Limiting RHS = G(v_lim) * (v_solve - v_lim) */
    const double lim_delta = inst->gd * (vd_raw - vd);
    inst->lim_rhs_ai = lim_delta;
    inst->lim_rhs_c = -lim_delta;

    if (!inst->collapsed[0]) {
        inst->g_rs = 1.0 / m->rs_ohms;
        inst->i_rs = (va - vai) * inst->g_rs;
    } else {
        inst->g_rs = 0.0;
        inst->i_rs = 0.0;
    }
    return ret;
}

static void v04_load_noise(void* inst_v, void* model_v, double freq, double* noise_dens) {
    (void)freq;
    const V04DiodeInst* inst = (const V04DiodeInst*)inst_v;
    const V04DiodeModel* m = (const V04DiodeModel*)model_v;
    noise_dens[0] = 2.0 * 1.602176634e-19 * fabs(inst->id);
    noise_dens[1] = (!inst->collapsed[0] && m->rs_ohms > 0.0)
                        ? (4.0 * 1.380649e-23 * inst->temp_k / m->rs_ohms)
                        : 0.0;
}

static void v04_load_res_resist(void* inst_v, void* model_v, double* dst) {
    (void)model_v;
    const V04DiodeInst* inst = (const V04DiodeInst*)inst_v;
    if (!inst->collapsed[0]) {
        dst[inst->node_mapping[0]] += inst->i_rs;
        dst[inst->node_mapping[2]] -= inst->i_rs;
    }
    dst[inst->node_mapping[2]] += inst->id;
    dst[inst->node_mapping[1]] -= inst->id;
}

static void v04_load_res_react(void* inst_v, void* model_v, double* dst) {
    (void)model_v;
    const V04DiodeInst* inst = (const V04DiodeInst*)inst_v;
    dst[inst->node_mapping[2]] += inst->qd;
    dst[inst->node_mapping[1]] -= inst->qd;
}

static void v04_load_lim_rhs_resist(void* inst_v, void* model_v, double* dst) {
    (void)model_v;
    const V04DiodeInst* inst = (const V04DiodeInst*)inst_v;
    dst[inst->node_mapping[2]] += inst->lim_rhs_ai;
    dst[inst->node_mapping[1]] += inst->lim_rhs_c;
}

static void v04_load_jac_resist(void* inst_v, void* model_v) {
    (void)model_v;
    const V04DiodeInst* inst = (const V04DiodeInst*)inst_v;
    if (!inst->collapsed[0]) {
        *inst->jac_resist[0] += inst->g_rs;
        *inst->jac_resist[1] -= inst->g_rs;
        *inst->jac_resist[2] -= inst->g_rs;
        *inst->jac_resist[3] += inst->g_rs;
    }
    *inst->jac_resist[3] += inst->gd;
    *inst->jac_resist[4] -= inst->gd;
    *inst->jac_resist[5] -= inst->gd;
    *inst->jac_resist[6] += inst->gd;
}

static void v04_load_jac_react(void* inst_v, void* model_v, double alpha) {
    (void)model_v;
    const V04DiodeInst* inst = (const V04DiodeInst*)inst_v;
    const double g = alpha * inst->cd;
    *inst->jac_react[3] += g;
    *inst->jac_react[4] -= g;
    *inst->jac_react[5] -= g;
    *inst->jac_react[6] += g;
}

OSDI_EXPORT const uint32_t OSDI_VERSION_MAJOR = 0u;
OSDI_EXPORT const uint32_t OSDI_VERSION_MINOR = 4u;
OSDI_EXPORT const uint32_t OSDI_NUM_DESCRIPTORS = 1u;
OSDI_EXPORT const uint32_t OSDI_DESCRIPTOR_SIZE = (uint32_t)sizeof(SpiceOsdiDescriptor);

OSDI_EXPORT const SpiceOsdiDescriptor OSDI_DESCRIPTORS[1] = {
    {
        "osdi_v04_diode",
        3u,                                          /* num_nodes */
        2u,                                          /* num_terminals */
        kNodes,                                      /* nodes */
        7u,                                          /* num_jacobian_entries */
        kJacEntries,                                 /* jacobian_entries */
        1u,                                          /* num_collapsible */
        kCollapsible,                                /* collapsible */
        (uint32_t)offsetof(V04DiodeInst, collapsed), /* collapsed_offset */
        kNoiseSources,                               /* noise_sources */
        2u,                                          /* num_noise_src */
        4u,                                          /* num_params */
        0u,                                          /* num_instance_params */
        0u,                                          /* num_opvars */
        kParams,                                     /* param_opvar */
        (uint32_t)offsetof(V04DiodeInst, node_mapping),
        (uint32_t)offsetof(V04DiodeInst, jac_resist),
        1u,                                          /* num_states */
        (uint32_t)offsetof(V04DiodeInst, state_idx),
        0u,                                          /* bound_step_offset */
        (uint32_t)sizeof(V04DiodeInst),
        (uint32_t)sizeof(V04DiodeModel),
        v04_access,
        v04_setup_model,
        v04_setup_instance,
        v04_eval,
        v04_load_noise,
        v04_load_res_resist,
        v04_load_res_react,
        v04_load_lim_rhs_resist,
        NULL,                                        /* load_limit_rhs_react */
        NULL,                                        /* load_spice_rhs_dc */
        NULL,                                        /* load_spice_rhs_tran */
        v04_load_jac_resist,
        v04_load_jac_react,
        NULL,                                        /* load_jacobian_tran */
        NULL,                                        /* given_flag_model */
        NULL,                                        /* given_flag_instance */
        7u,                                          /* num_resistive_jacobian_entries */
        4u,                                          /* num_reactive_jacobian_entries */
        NULL,                                        /* write_jacobian_array_resist */
        NULL,                                        /* write_jacobian_array_react */
        0u,                                          /* num_inputs */
        NULL,                                        /* inputs */
        NULL,                                        /* load_jacobian_with_offset_resist */
        NULL,                                        /* load_jacobian_with_offset_react */
        NULL,                                        /* unknown_nature */
        NULL,                                        /* residual_nature */
        NULL,                                        /* noise_source_type */
        NULL,                                        /* load_noise_params */
        0u,                                          /* absdelay_count */
        NULL,                                        /* absdelay_info */
    },
};
