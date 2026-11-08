#include <windows.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>
#include "osdilegacy.h"

static void require(int ok, const char *message) {
    if (!ok) { fprintf(stderr, "%s\n", message); exit(1); }
}

static void set_param(const OsdiDescriptor04 *d, void *inst, void *model,
                      const char *name, double value, bool instance) {
    for (uint32_t i = 0; i < d->num_params; i++) {
        OsdiParamOpvar p = d->param_opvar[i];
        for (uint32_t j = 0; j <= p.num_alias; j++) {
            if (_stricmp(p.name[j], name)) continue;
            void *slot = d->access(inst, model, i,
                ACCESS_FLAG_SET | (instance ? ACCESS_FLAG_INSTANCE : 0));
            require(slot != NULL, "parameter slot unavailable");
            if ((p.flags & PARA_TY_MASK) == PARA_TY_REAL) *(double *)slot = value;
            else if ((p.flags & PARA_TY_MASK) == PARA_TY_INT) *(int32_t *)slot = (int32_t)value;
            else require(0, "parameter type is not numeric");
            return;
        }
    }
    require(0, name);
}

static void evaluate(const OsdiDescriptor04 *d, int polarity) {
    void *model = calloc(1, d->model_size);
    void *inst = calloc(1, d->instance_size);
    require(model && inst, "allocation failed");
    set_param(d, inst, model, "type", polarity, false);
    set_param(d, inst, model, "tox", 3.5e-9, false);
    set_param(d, inst, model, "nch", polarity > 0 ? 2.4e17 : 2.5e17, false);
    set_param(d, inst, model, "nsub", 5e16, false);
    set_param(d, inst, model, "vth0", polarity * .15, false);
    set_param(d, inst, model, "w", 1e-6, true);
    set_param(d, inst, model, "l", .35e-6, true);
    set_param(d, inst, model, "pd", 1.5e-6, true);
    set_param(d, inst, model, "ps", 1.5e-6, true);
    set_param(d, inst, model, "ad", 1.5e-12, true);
    set_param(d, inst, model, "as", 1.5e-12, true);

    char *names[] = {"gmin", NULL};
    char *string_names[] = {NULL};
    double values[] = {1e-12};
    OsdiSimParas params = {names, values, string_names, NULL};
    OsdiInitInfo model_init = {0}, instance_init = {0};
    d->setup_model(NULL, model, &params, &model_init);
    require(!model_init.flags && !model_init.num_errors, "model rejected");
    d->setup_instance(NULL, inst, model, 300.15, 4, &params, &instance_init);
    require(!instance_init.flags && !instance_init.num_errors, "instance rejected");

    // The fixture uses no series resistance or NQS. Every internal node must collapse.
    uint32_t *mapping = (uint32_t *)((char *)inst + d->node_mapping_offset);
    bool *collapsed = (bool *)((char *)inst + d->collapsed_offset);
    for (uint32_t i = 0; i < d->num_nodes; i++) mapping[i] = i + 1;
    for (uint32_t i = 0; i < d->num_collapsible; i++) {
        if (!collapsed[i]) continue;
        uint32_t a = d->collapsible[i].node_1;
        uint32_t b = d->collapsible[i].node_2;
        uint32_t from = mapping[a], to = b == UINT32_MAX ? 0 : mapping[b];
        if (from < to) { uint32_t tmp = from; from = to; to = tmp; }
        require(from > 4 || from == to, "unexpected terminal collapse");
        for (uint32_t j = 0; j < d->num_nodes; j++)
            if (mapping[j] == from) mapping[j] = to;
    }
    for (uint32_t i = 0; i < d->num_nodes; i++) require(mapping[i] <= 4, "uncollapsed node");
    require(d->num_states < 32, "too many states");
    uint32_t *state_index = (uint32_t *)((char *)inst + d->state_idx_off);
    for (uint32_t i = 0; i < d->num_states; i++) state_index[i] = i;
    for (int k = -1; k <= 1; k++) {
        double v[] = {0, .8 * polarity, (.8 + k * .0001) * polarity, 0, 0};
        double prev[32] = {0}, next[32] = {0}, current[5] = {0}, charge[5] = {0};
        OsdiSimInfo info = {params, 0, v, prev, next,
            CALC_RESIST_RESIDUAL | CALC_REACT_RESIDUAL | ANALYSIS_DC | ANALYSIS_STATIC};
        require(!d->eval(NULL, inst, model, &info), "native evaluation failed");
        d->load_residual_resist(inst, model, current);
        d->load_residual_react(inst, model, charge);
        double net_current = 0, net_charge = 0;
        for (int i = 0; i < 5; i++) {
            require(isfinite(current[i]) && isfinite(charge[i]), "nonfinite result");
            net_current += current[i]; net_charge += charge[i];
        }
        require(fabs(net_current) < 1e-15 && fabs(net_charge) < 1e-24, "terminal conservation failed");
        printf("%d,%.8g,%.17g,%.17g,%.17g,%.17g\n", polarity, v[2],
            current[1], charge[2], net_current, net_charge);
    }
    free(inst); free(model);
}

int wmain(int argc, wchar_t **argv) {
    require(argc == 2, "pass the absolute path to the verified model DLL");
    HMODULE dll = LoadLibraryExW(argv[1], NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    require(dll != NULL, "cannot load module");
    const uint32_t *major = (void *)GetProcAddress(dll, "OSDI_VERSION_MAJOR");
    const uint32_t *minor = (void *)GetProcAddress(dll, "OSDI_VERSION_MINOR");
    const uint32_t *size = (void *)GetProcAddress(dll, "OSDI_DESCRIPTOR_SIZE");
    const uint32_t *count = (void *)GetProcAddress(dll, "OSDI_NUM_DESCRIPTORS");
    // This pinned Windows module appends eight bytes to the header descriptor.
    // The probe consumes only the common prefix and loads only the trusted fixture.
    require(major && minor && size && count && *major == 0 && *minor == 4 &&
        *size == 352 && *count == 1, "unexpected OSDI ABI");
    const OsdiDescriptor04 *d = (void *)GetProcAddress(dll, "OSDI_DESCRIPTORS");
    require(d && !strcmp(d->name, "sp_bsim3v3") && d->num_terminals == 4, "wrong model");
    puts("polarity,gate_v,drain_a,gate_c,net_a,net_c");
    evaluate(d, 1); evaluate(d, -1);
    FreeLibrary(dll);
    return 0;
}
