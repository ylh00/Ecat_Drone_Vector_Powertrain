/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * SSC API STUB - see the explanation in SSC/StubSrc/ecat_def.h.
 *
 * Minimal equivalent of the SSC generated header "applInterface.h": the
 * application callbacks that the slave stack calls.  The prototypes are
 * identical to the SSC 5.13 application interface (the SSC Tool generates the
 * same prototypes into the application header from the application xlsx).
 */

#ifndef APPLINTERFACE_H
#define APPLINTERFACE_H

#include "ecat_def.h"
#include "ecatslv.h"
#include "ecatcoe.h"

#ifdef __cplusplus
extern "C" {
#endif

/* --------------------------------------------------------------------------
 * State transition callbacks (called by the slave stack)
 * -------------------------------------------------------------------------- */
void   APPL_AckErrorInd(UINT16 stateTrans);

UINT16 APPL_StartMailboxHandler(void);
UINT16 APPL_StopMailboxHandler(void);
UINT16 APPL_StartInputHandler(UINT16 *pIntMask);
UINT16 APPL_StopInputHandler(void);
UINT16 APPL_StartOutputHandler(void);
UINT16 APPL_StopOutputHandler(void);

/* --------------------------------------------------------------------------
 * Process data callbacks (called by the slave stack)
 * -------------------------------------------------------------------------- */
UINT16 APPL_GenerateMapping(UINT16 *pInputSize, UINT16 *pOutputSize);
void   APPL_InputMapping(UINT16 *pData);
void   APPL_OutputMapping(UINT16 *pData);
void   APPL_Application(void);

#if defined(EXPLICIT_DEVICE_ID) && EXPLICIT_DEVICE_ID
UINT16 APPL_GetDeviceID(void);
#endif

/* --------------------------------------------------------------------------
 * CoE manufacturer object access hooks.
 * These are HPMicro application functions (not part of the SSC API); the
 * object dictionary generated from SSC/ecat_drone.xlsx references them in the
 * "CoeRead"/"CoeWrite" columns of the parameter objects (0x2000..0x20FF).
 * -------------------------------------------------------------------------- */
UINT8 ecat_coe_obj_read(UINT16 index, UINT8 subindex, UINT32 size,
                        UINT16 MBXMEM *pData, UINT8 bCompleteAccess);
UINT8 ecat_coe_obj_write(UINT16 index, UINT8 subindex, UINT32 size,
                         UINT16 MBXMEM *pData, UINT8 bCompleteAccess);

#ifdef __cplusplus
}
#endif

#endif /* APPLINTERFACE_H */
