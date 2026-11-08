#include "ngspice/ngspice.h"
#include "ngspice/cktdefs.h"
#include "ngspice/devdefs.h"
#include "ngspice/sperror.h"
#include "b3soifddef.h"
#include <stdarg.h>
#undef printf
#undef fprintf
#undef fclose
#undef fopen
#undef fputc
#undef puts
#undef vfprintf
#undef vprintf
#undef putchar

/* FD t3 static trial using the Berkeley BSIM Research Group model.
 * This harness neither initializes nor links the ngspice analysis engine. */
enum { DIM = 16, STATES = 128 };
static double conductance[DIM][DIM];
static CKTnode internal_nodes[DIM];
static int next_node = 5;
double CONSTroot2 = 1.4142135623730951;
double CONSTvt0 = 0.025864925786328753;
static void error_message(int kind, const char *format, ...) {
    va_list arguments;
    (void)kind;
    va_start(arguments, format);
    vfprintf(stderr, format, arguments);
    va_end(arguments);
    fputc('\n', stderr);
}
static IFfrontEnd frontend = {.IFerrorf = error_message};
IFfrontEnd *SPfrontEnd = &frontend;
void *tmalloc(size_t size) { return calloc(1, size); }
void txfree(const void *value) { free((void *)value); }
void controlled_exit(int status) { exit(status == 0 ? 99 : status); }
FILE *newfopen(const char *name, const char *mode) { return fopen(name, mode); }
int win_x_fclose(FILE *stream) { return fclose(stream); }
int win_x_fprintf(FILE *stream, const char *format, ...) {
    va_list args; va_start(args, format); int rc = vfprintf(stream, format, args); va_end(args); return rc;
}
int win_x_printf(const char *format, ...) {
    va_list args; va_start(args, format); int rc = vprintf(format, args); va_end(args); return rc;
}
int CKTmkVolt(CKTcircuit *ckt, CKTnode **node, IFuid name, char *suffix) {
    (void)ckt; (void)name; (void)suffix;
    if (next_node >= DIM) return E_NOMEM;
    *node = &internal_nodes[next_node];
    (*node)->number = next_node++;
    return OK;
}
int CKTdltNNum(CKTcircuit *ckt, int node) { (void)ckt; (void)node; return OK; }
int CKTinst2Node(CKTcircuit *ckt, void *inst, int pin, CKTnode **node, IFuid *name) {
    (void)ckt; (void)inst; (void)pin; (void)node; (void)name; return E_BADPARM;
}
double *SMPmakeElt(SMPmatrix *matrix, int row, int col) {
    (void)matrix;
    if (row < 0 || row >= DIM || col < 0 || col >= DIM) return NULL;
    return &conductance[row][col];
}
int NIintegrate(CKTcircuit *ckt, double *g, double *i, double cap, int state) {
    (void)ckt; (void)g; (void)i; (void)cap; (void)state;
    return E_BADPARM; /* Only static DC loads are supported in this harness. */
}

extern IFparm B3SOIFDmPTable[];
extern int B3SOIFDmPTSize;
static const struct { const char *name; double value; } model_card[] = {
#include "fd-card.inc"
};
static int stamp(B3SOIFDmodel *model, B3SOIFDinstance *device, CKTcircuit *ckt,
                 double *voltage, double *rhs, double *state) {
    memset(conductance, 0, sizeof conductance);
    memset(rhs, 0, DIM * sizeof(double));
    state[device->B3SOIFDvg] = voltage[2];
    state[device->B3SOIFDvd] = voltage[1];
    state[device->B3SOIFDvs] = voltage[3];
    state[device->B3SOIFDve] = voltage[4];
    return B3SOIFDload((GENmodel *)model, ckt);
}
int main(void) {
    char device_name[] = "m1", model_name[] = "n1";
    B3SOIFDmodel model = {0};
    B3SOIFDinstance device = {
        .B3SOIFDdNode=1,.B3SOIFDgNode=2,.B3SOIFDsNode=3,.B3SOIFDeNode=4,
        .B3SOIFDbNodeExt=-1,.B3SOIFDtempNodeExt=-1,.B3SOIFDpNodeExt=-1
    };
    model.gen.GENinstances = &device.gen;
    model.gen.GENmodName = model_name;
    device.gen.GENmodPtr = &model.gen;
    device.gen.GENname = device_name;
    IFvalue parameter = {.iValue=1};
    if (B3SOIFDmParam(B3SOIFD_MOD_NMOS, &parameter, (GENmodel *)&model)) return 1;
    for (size_t i = 0; i < sizeof model_card / sizeof model_card[0]; i++) {
        bool found = false;
        for (int j = 0; j < B3SOIFDmPTSize; j++) {
            const IFparm *field = &B3SOIFDmPTable[j];
            if (strcmp(field->keyword, model_card[i].name)) continue;
            if ((field->dataType & IF_VARTYPES) == IF_REAL) parameter.rValue = model_card[i].value;
            else if ((field->dataType & IF_VARTYPES) == IF_INTEGER) parameter.iValue = (int)model_card[i].value;
            else return 2;
            if (B3SOIFDmParam(field->id, &parameter, (GENmodel *)&model)) return 3;
            found = true;
            break;
        }
        if (!found) { fprintf(stderr, "unmapped model term %s\n", model_card[i].name); return 10; }
    }
    device.B3SOIFDw = 10e-6;
    device.B3SOIFDl = 0.25e-6;
    device.B3SOIFDwGiven = device.B3SOIFDlGiven = 1;
    double rhs[DIM] = {0}, voltage[DIM] = {0}, state[STATES] = {0};
    CKTcircuit ckt = {0};
    ckt.CKTtemp = ckt.CKTnomTemp = 300.15;
    ckt.CKTgmin = 1e-25;
    ckt.CKTreltol = 1e-3;
    ckt.CKTabstol = 1e-12;
    ckt.CKTvoltTol = 1e-6;
    ckt.CKTmode = MODEDCOP | MODEINITFLOAT;
    ckt.CKTrhs = rhs;
    ckt.CKTrhsOld = voltage;
    ckt.CKTstate0 = state;
    int state_count = 0;
    if (B3SOIFDsetup(NULL, (GENmodel *)&model, &ckt, &state_count)) return 4;
    if (state_count > STATES || next_node != 5) return 5;
    if (B3SOIFDtemp((GENmodel *)&model, &ckt)) return 6;
    puts("vg,vd,id,ig,is,ie");
    for (int g = 1; g <= 6; g++) {
        voltage[2] = 0.5 * g;
        for (int j = 0; j <= 300; j++) {
            voltage[1] = 0.01 * j;
            if (stamp(&model, &device, &ckt, voltage, rhs, state)) return 7;
            printf("%.17g,%.17g", voltage[2], voltage[1]);
            for (int row = 1; row <= 4; row++) {
                // The linearized model writes J and Jv - I. Recover signed I.
                double residual = -rhs[row];
                for (int col = 1; col <= 4; col++) residual += conductance[row][col] * voltage[col];
                if (!isfinite(residual)) return 8;
                printf(",%.17g", residual);
            }
            putchar('\n');
        }
    }
    return 0;
}
