/*
 * Negative OSDI fixture exporting unsupported ABI version 9.9 to verify
 * fail-closed ABI version checking (ARCH-MOD-001, DEV-TEST-003).
 */
#include <stdint.h>

#if defined(_WIN32)
#  define OSDI_EXPORT __declspec(dllexport)
#else
#  define OSDI_EXPORT __attribute__((visibility("default")))
#endif

OSDI_EXPORT const uint32_t OSDI_VERSION_MAJOR = 9u;
OSDI_EXPORT const uint32_t OSDI_VERSION_MINOR = 9u;
OSDI_EXPORT const uint32_t OSDI_NUM_DESCRIPTORS = 0u;
OSDI_EXPORT const void* OSDI_DESCRIPTORS = (const void*)0;
