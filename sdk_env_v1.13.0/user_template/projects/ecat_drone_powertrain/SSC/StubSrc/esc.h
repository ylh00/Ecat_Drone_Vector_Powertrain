/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * SSC API STUB - see the explanation in SSC/StubSrc/ecat_def.h.
 *
 * Minimal equivalent of the SSC generated header "esc.h".  Only the ESC
 * register offsets and EEPROM configuration helpers referenced by the
 * hardware abstraction layer are provided here.  All other ESC access is done
 * through the HPM SDK driver registers (hpm_esc_drv.h / hpm_esc_regs.h).
 */

#ifndef ESC_H
#define ESC_H

#include "ecat_def.h"

/* ESC AL event request register (0x0220, 32 bit).  The HAL reads the low word
 * of this register in the PDI interrupt handler (HW_GetALEventRegister). */
#define ESC_AL_EVENT_OFFSET             (0x0220U)

/* ESC AL event mask register (0x0204, 32 bit) */
#define ESC_AL_EVENTMASK_OFFSET         (0x0204U)

/* ESC DC SYNC status byte (0x098E).  The HAL acknowledges SYNC0/SYNC1 by
 * reading this register (a 32 bit read covers 0x098C..0x098F). */
#define ESC_DC_SYNC_STATUS              (0x098EU)

/* Note: the ESC EEPROM configuration helpers ESC_ESС_CFG_ELDAP_GET /
 * ESC_ESС_CFG_ELPx_GET are provided by the HPM SDK ESC register header
 * (hpm_esc_regs.h) and must NOT be redefined here. */

#endif /* ESC_H */
