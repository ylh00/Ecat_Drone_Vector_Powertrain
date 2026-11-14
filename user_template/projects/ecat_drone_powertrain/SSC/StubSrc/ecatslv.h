/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * SSC API STUB - see the explanation in SSC/StubSrc/ecat_def.h.
 *
 * Minimal equivalent of the SSC generated header "ecatslv.h": AL status codes
 * returned by the application state transition callbacks.
 */

#ifndef ECATSLV_H
#define ECATSLV_H

#include "ecat_def.h"
#include "esc.h"

#ifdef __cplusplus
extern "C" {
#endif

/* AL status codes (ETG.1000 / ETG.1020), values are the AL Status Code
 * written to ESC register 0x0134 on a refused state transition. */
#define ALSTATUSCODE_NOERROR                (0x0000U)
#define ALSTATUSCODE_UNKNOWN                (0x0001U)
#define ALSTATUSCODE_INVALIDSTATECHANGE     (0x0011U)
#define ALSTATUSCODE_UNKNOWNFMMU            (0x0017U)
#define ALSTATUSCODE_INVALIDOUTPUTMAPPING   (0x001DU)
#define ALSTATUSCODE_INVALIDINPUTMAPPING    (0x001EU)
#define ALSTATUSCODE_INVALIDMAILBOXCONFIG   (0x001FU)

/* The application cannot confirm the state transition immediately; the
 * transition is completed later by calling ECAT_StateChange(). */
#define NOERROR_INWORK                      (0x0001U)

#ifdef __cplusplus
}
#endif

#endif /* ECATSLV_H */
