/*
 * Brings the ABI structs into Swift without their prototypes: this library provides those symbols
 * itself, with @_cdecl, so importing declarations for them as well would only invite a clash.
 *
 * The canonical header is reached by a relative include rather than copied, because two copies of
 * a struct layout is exactly the bug BM_VERSION exists to catch.
 */
#ifndef BM_BRIDGE_H
#define BM_BRIDGE_H

#define BM_TYPES_ONLY
#include "../../../../include/bm.h"

#endif
