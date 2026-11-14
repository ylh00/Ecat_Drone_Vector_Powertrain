/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * SSC API STUB - see the explanation in SSC/StubSrc/ecat_def.h.
 *
 * Minimal equivalent of the SSC generated header "ecatappl.h": the slave stack
 * entry points, global state and the EEPROM access callbacks used by the
 * hardware abstraction layer (port/hpm_ecat_hw.c) and by ecat.c.
 */

#ifndef ECATAPPL_H
#define ECATAPPL_H

#include "ecat_def.h"

#ifdef __cplusplus
extern "C" {
#endif

/* --------------------------------------------------------------------------
 * Slave stack globals (defined in ecatappl.c of the generated stack / in
 * SSC/StubSrc/ssc_stub.c for the stub build).
 * -------------------------------------------------------------------------- */
extern BOOLEAN bRunApplication;     /* TRUE while the stack main loop runs     */
extern UINT16  nPdInputSize;        /* size of the TxPDO (slave -> master)     */
extern UINT16  nPdOutputSize;       /* size of the RxPDO (master -> slave)     */

/* --------------------------------------------------------------------------
 * Slave stack entry points
 * -------------------------------------------------------------------------- */
void MainInit(void);
void MainLoop(void);
void PDI_Isr(void);
void Sync0_Isr(void);
void Sync1_Isr(void);
void ECAT_CheckTimer(void);

/* --------------------------------------------------------------------------
 * Optional EEPROM access callbacks (SSC HW interface).  The application wires
 * them to the flash EEPROM emulation in ecat.c when ESC_EEPROM_EMULATION == 1.
 * -------------------------------------------------------------------------- */
extern UINT16 (*pAPPL_EEPROM_Read)(UINT32 wordaddr);
extern UINT16 (*pAPPL_EEPROM_Write)(UINT32 wordaddr);
extern UINT16 (*pAPPL_EEPROM_Reload)(void);
extern void   (*pAPPL_EEPROM_Store)(void);

#ifdef __cplusplus
}
#endif

#endif /* ECATAPPL_H */
