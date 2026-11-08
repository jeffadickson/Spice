/* Force-include for legacy device translation units only. Apply redirection
 * after libc and ngspice headers so fortified stdio cannot bypass the host. */
#include "ngspice/ngspice.h"
#undef printf
#undef fprintf
#undef fclose
int win_x_printf(const char *format, ...);
int win_x_fprintf(FILE *stream, const char *format, ...);
int win_x_fclose(FILE *stream);
#define printf win_x_printf
#define fprintf win_x_fprintf
#define fclose win_x_fclose
