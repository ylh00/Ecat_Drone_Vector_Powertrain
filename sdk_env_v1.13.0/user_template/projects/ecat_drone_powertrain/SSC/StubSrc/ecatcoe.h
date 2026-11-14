/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * SSC API STUB - see the explanation in SSC/StubSrc/ecat_def.h.
 *
 * Minimal equivalent of the SSC generated header "ecatcoe.h".  The
 * ABORTIDX_* identifiers are the values returned by the object read/write
 * callbacks to select the CoE SDO abort code (SSC internal table index).
 * The names match the SSC 5.13 API; the numeric values are irrelevant for the
 * stub build (no SDO traffic) but must come from the real header when the
 * licensed stack is used, which is guaranteed by the include order.
 */

#ifndef ECATCOE_H
#define ECATCOE_H

#include "ecat_def.h"

/* Object access abort reasons (returned by the ObjRead / ObjWrite callbacks) */
#define ABORTIDX_NONE                       (0U)
#define ABORTIDX_UNSUPPORTED_ACCESS         (1U)
#define ABORTIDX_SUBINDEX_NOT_EXISTING      (2U)
#define ABORTIDX_PARAM_IS_INCOMPATIBLE      (3U)
#define ABORTIDX_VALUE_EXCEEDED             (4U)
#define ABORTIDX_NO_DATA                    (5U)
#define ABORTIDX_HARDWARE_ERROR             (6U)
#define ABORTIDX_DATA_TYPE_MISMATCH         (7U)

#endif /* ECATCOE_H */
