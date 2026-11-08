/* The model is an explicitly trusted platform-native OSDI module. */
#ifdef _WIN32
#include <windows.h>
#define HOST_EXPORT __declspec(dllexport)
#define HOST_PATH wchar_t
#define module_handle HMODULE
#define open_module(path) LoadLibraryExW(path, NULL, LOAD_LIBRARY_SEARCH_SYSTEM32)
#define symbol(module, name) GetProcAddress(module, name)
#define close_module(module) FreeLibrary(module)
#define case_compare _stricmp
#else
#include <dlfcn.h>
#include <strings.h>
#define HOST_EXPORT __attribute__((visibility("default")))
#define HOST_PATH char
#define module_handle void *
#define open_module(path) dlopen(path, RTLD_NOW | RTLD_LOCAL)
#define symbol(module, name) dlsym(module, name)
#define close_module(module) dlclose(module)
#define case_compare strcasecmp
#endif
#include <math.h>
#include <stdlib.h>
#include <string.h>
#include "osdilegacy.h"

static bool set_param(const OsdiDescriptor04 *d, void *inst, void *model,
                      const char *name, double value, bool instance) {
    for (uint32_t i = 0; i < d->num_params; i++) {
        OsdiParamOpvar p = d->param_opvar[i];
        for (uint32_t j = 0; j <= p.num_alias; j++) {
            if (case_compare(p.name[j], name)) continue;
            void *slot = d->access(inst, model, i,
                ACCESS_FLAG_SET | (instance ? ACCESS_FLAG_INSTANCE : 0));
            if (!slot) return false;
            if ((p.flags & PARA_TY_MASK) == PARA_TY_REAL) *(double *)slot = value;
            else if ((p.flags & PARA_TY_MASK) == PARA_TY_INT) *(int32_t *)slot = (int32_t)value;
            else return false;
            return true;
        }
    }
    return false;
}

static const OsdiDescriptor04 *descriptor;

HOST_EXPORT int bsim330_open(const HOST_PATH *path) {
    module_handle lib = open_module(path);
    if (!lib) return 1;
    const uint32_t *major = (void *)symbol(lib, "OSDI_VERSION_MAJOR");
    const uint32_t *minor = (void *)symbol(lib, "OSDI_VERSION_MINOR");
    const uint32_t *size = (void *)symbol(lib, "OSDI_DESCRIPTOR_SIZE");
    const uint32_t *count = (void *)symbol(lib, "OSDI_NUM_DESCRIPTORS");
    const OsdiDescriptor04 *d = (void *)symbol(lib, "OSDI_DESCRIPTORS");
    if (!major || !minor || !size || !count || !d || *major != 0 || *minor != 4 ||
        *size != 352 || *count != 1 || !d->name || strcmp(d->name, "sp_bsim3v3") ||
        d->num_terminals != 4 || d->num_states >= 32) {
        close_module(lib);
        return 2;
    }
    descriptor = d;
    return 0;
}

HOST_EXPORT int bsim330_trial(const double p[11], const double v[4],
                                       double output[8]) {
    const OsdiDescriptor04 *d = descriptor;
    if (!d) return 3;
    void *model = calloc(1, d->model_size), *inst = calloc(1, d->instance_size);
    int status = 4;
    if (!model || !inst) goto finish;
    const char *keys[] = {"type","tox","nch","nsub","vth0","w","l","pd","ps","ad","as"};
    for (int i = 0; i < 11; i++)
        if (!set_param(d, inst, model, keys[i], p[i], i >= 5)) goto finish;

    char *names[] = {"gmin", NULL}, *string_names[] = {NULL};
    double values[] = {1e-12};
    OsdiSimParas params = {names, values, string_names, NULL};
    OsdiInitInfo model_init = {0}, instance_init = {0};
    status = 5;
    d->setup_model(NULL, model, &params, &model_init);
    free(model_init.errors);
    if (model_init.flags || model_init.num_errors) goto finish;
    d->setup_instance(NULL, inst, model, 300.15, 4, &params, &instance_init);
    free(instance_init.errors);
    if (instance_init.flags || instance_init.num_errors) goto finish;

    // A grounded eliminated internal unknown uses index zero in the trial vector.
    uint32_t *mapping = (uint32_t *)((char *)inst + d->node_mapping_offset);
    bool *collapsed = (bool *)((char *)inst + d->collapsed_offset);
    for (uint32_t i = 0; i < d->num_nodes; i++) mapping[i] = i + 1;
    status = 6;
    for (uint32_t i = 0; i < d->num_collapsible; i++) {
        if (!collapsed[i]) continue;
        uint32_t a = d->collapsible[i].node_1, b = d->collapsible[i].node_2;
        uint32_t from = mapping[a], to = b == UINT32_MAX ? 0 : mapping[b];
        if (from < to) { uint32_t tmp = from; from = to; to = tmp; }
        if (from <= 4 && from != to) goto finish;
        for (uint32_t j = 0; j < d->num_nodes; j++)
            if (mapping[j] == from) mapping[j] = to;
    }
    for (uint32_t i = 0; i < d->num_nodes; i++) if (mapping[i] > 4) goto finish;
    uint32_t *indices = (uint32_t *)((char *)inst + d->state_idx_off);
    for (uint32_t i = 0; i < d->num_states; i++) indices[i] = i;

    double voltage[] = {0, v[0], v[1], v[2], v[3]};
    double prev[32] = {0}, next[32] = {0}, i[5] = {0}, q[5] = {0};
    OsdiSimInfo info = {params, 0, voltage, prev, next,
        CALC_RESIST_RESIDUAL | CALC_REACT_RESIDUAL | ANALYSIS_DC | ANALYSIS_STATIC};
    status = 7;
    if (d->eval(NULL, inst, model, &info)) goto finish;
    d->load_residual_resist(inst, model, i);
    d->load_residual_react(inst, model, q);
    for (int k = 0; k < 4; k++) {
        output[k] = i[k + 1];
        output[k + 4] = q[k + 1];
    }
    status = 0;
finish:
    free(inst);
    free(model);
    return status;
}
