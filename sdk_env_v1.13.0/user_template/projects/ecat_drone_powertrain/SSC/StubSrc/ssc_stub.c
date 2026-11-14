/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * ============================================================================
 *  ##########  SSC (Beckhoff EtherCAT Slave Stack) API STUB  ################
 * ============================================================================
 *
 * The Beckhoff EtherCAT Slave Stack Code (SSC) is not shipped with hpm_sdk for
 * licensing reasons.  This file implements the small part of the SSC API that
 * the ecat_drone_powertrain firmware uses, so that the complete project can be
 * compiled, linked and flashed without the licensed stack:
 *
 *   - the stack entry points MainInit() / MainLoop() / PDI_Isr() / Sync0_Isr()
 *     / Sync1_Isr() / ECAT_CheckTimer() and the stack globals
 *     (bRunApplication, nPdInputSize, nPdOutputSize, EepromReadSize, ...)
 *   - an optional AL state machine emulation (ECAT_SSC_STUB_TEST_MODE) that
 *     drives the application callbacks (APPL_Start... / APPL_Stop... /
 *     APPL_Application / APPL_InputMapping / APPL_OutputMapping) exactly like
 *     the real stack does, so the application state machine can be bench
 *     tested without a master.
 *
 * LIMITATIONS - the stub is NOT an EtherCAT slave stack:
 *   - no mailbox, no CoE/SDO, no process data exchange, no DC, no watchdog
 *   - nothing is received from / transmitted to an EtherCAT master
 *   - g_ecat_op_active is only driven in ECAT_SSC_STUB_TEST_MODE; in a normal
 *     build the slave stays in INIT (no EtherCAT communication)
 *
 * To use the real stack: generate it with the Beckhoff SSC Tool using
 * SSC/Config/HPM_ECAT_DRONE_Config.xml + SSC/ecat_drone.xlsx and copy the
 * generated sources into SSC/Src/ - see SSC/README.md.  CMake then compiles
 * the real stack instead of this file and SSC/Src headers take precedence.
 */

#include <stdio.h>
#include <string.h>

#include "board.h"
#include "hpm_esc_drv.h"

#include "ecat_config.h"
#include "hpm_ecat_hw.h"
#include "ecat_def.h"
#include "ecatappl.h"
#include "ecatslv.h"
#include "applInterface.h"
#include "ecat_pdo.h"

/* ==========================================================================
 * Slave stack globals (normally defined by the generated ecatappl.c)
 * ========================================================================== */
BOOLEAN bRunApplication = FALSE;
UINT16  nPdInputSize  = 0U;
UINT16  nPdOutputSize = 0U;
UINT8   EepromReadSize = 8U;

UINT16 (*pAPPL_EEPROM_Read)(UINT32 wordaddr)  = NULL;
UINT16 (*pAPPL_EEPROM_Write)(UINT32 wordaddr) = NULL;
UINT16 (*pAPPL_EEPROM_Reload)(void)           = NULL;
void   (*pAPPL_EEPROM_Store)(void)            = NULL;

/* ==========================================================================
 * Stub local state
 * ========================================================================== */
static UINT16 s_al_state   = 1U; /* INIT */
static UINT32 s_stub_ms    = 0U;
static UINT32 s_next_state_ms = 0U;

/* Process data shadow buffers: the stub is the "ESC memory side" here.  They
 * only exercise the PDO copy functions of the application in test mode. */
static UINT8 s_pd_input_shadow[APP_ECAT_TXPDO_SIZE];   /* slave -> master */
static UINT8 s_pd_output_shadow[APP_ECAT_RXPDO_SIZE];  /* master -> slave */

/* Forward declaration of the stub state machine helpers */
#if ECAT_SSC_STUB_TEST_MODE
static void stub_enter_state(UINT16 new_state);
#endif

/* ==========================================================================
 * Stack entry points
 * ========================================================================== */
void MainInit(void)
{
    EepromReadSize = 8U; /* flash EEPROM emulation supports 8 byte reads */

    s_al_state      = 1U; /* INIT */
    s_stub_ms       = 0U;
    s_next_state_ms = ECAT_SSC_STUB_TEST_PERIOD_MS;

    memset(s_pd_input_shadow, 0, sizeof(s_pd_input_shadow));
    memset(s_pd_output_shadow, 0, sizeof(s_pd_output_shadow));

    g_ecat_op_active = 0U;

    printf("[ECAT] SSC API stub active: no EtherCAT communication in this build!\n");
#if ECAT_SSC_STUB_TEST_MODE
    printf("[ECAT] stub test mode ENABLED: emulating the AL state machine\n");
#endif
}

void MainLoop(void)
{
    UINT16 int_mask = 0U;

    if (bRunApplication == FALSE) {
        return;
    }

#if ECAT_SSC_STUB_TEST_MODE
    /* Emulate a master: INIT -> PREOP -> SAFEOP -> OP -> SAFEOP -> PREOP -> ... */
    if (HW_GetTimer() >= s_next_state_ms) {
        s_next_state_ms = HW_GetTimer() + ECAT_SSC_STUB_TEST_PERIOD_MS;

        switch (s_al_state) {
        case 1U:
            stub_enter_state(2U); /* INIT   -> PREOP  */
            break;
        case 2U:
            stub_enter_state(4U); /* PREOP  -> SAFEOP */
            break;
        case 4U:
            stub_enter_state(8U); /* SAFEOP -> OP     */
            break;
        case 8U:
            stub_enter_state(4U); /* OP     -> SAFEOP */
            break;
        default:
            stub_enter_state(1U); /* back to INIT     */
            break;
        }
    }
#else
    /* No master: the ESC stays in INIT.  Never override the AL state unless the
     * ESC really reports a valid state set by an external master/stack. */
    {
        UINT16 hw_al_state = (UINT16)ESC_AL_STAT_AS_GET(HPM_ESC->AL_STAT);

        if ((hw_al_state >= 2U) && (hw_al_state <= 8U)) {
            s_al_state = hw_al_state;
        }
    }
#endif

    g_ecat_op_active = (s_al_state == 8U) ? 1U : 0U;

    /* Process data exchange: only the copy functions of the application are
     * exercised here, the shadow buffers are NOT connected to any network. */
    if (s_al_state >= 4U) {
        APPL_InputMapping((UINT16 *)s_pd_input_shadow);
    }
    if (s_al_state == 8U) {
        APPL_OutputMapping((UINT16 *)s_pd_output_shadow);
    }

    APPL_Application();

    (void)int_mask;
}

void PDI_Isr(void)
{
    /* The real stack processes mailbox/AL events here. */
    APPL_Application();
}

void Sync0_Isr(void)
{
    /* The real stack calls APPL_Application() from the SYNC0 interrupt. */
    APPL_Application();
}

void Sync1_Isr(void)
{
    /* The real stack calls the application from the SYNC1 interrupt. */
    APPL_Application();
}

void ECAT_CheckTimer(void)
{
    s_stub_ms++;

    /* The real stack checks the process data watchdog and the AL state machine
     * timing here; the stub only keeps a free running millisecond counter. */
}

/* ==========================================================================
 * Stub AL state machine (test mode only)
 *   Calls the application state transition callbacks in the same order as the
 *   real slave stack, so the application layer is exercised end to end.
 * ========================================================================== */
#if ECAT_SSC_STUB_TEST_MODE
static void stub_enter_state(UINT16 new_state)
{
    UINT16 int_mask = 0U;

    if (new_state == s_al_state) {
        return;
    }

    /* Leave the current state (reverse order of the entry calls) */
    switch (s_al_state) {
    case 8U: /* OP -> SAFEOP */
        (void)APPL_StopOutputHandler();
        (void)APPL_StopInputHandler();
        break;
    case 4U: /* SAFEOP -> PREOP */
        (void)APPL_StopInputHandler();
        break;
    case 2U: /* PREOP -> INIT */
        (void)APPL_StopMailboxHandler();
        break;
    default:
        break;
    }

    s_al_state = new_state;

    /* Enter the new state */
    switch (new_state) {
    case 2U: /* INIT -> PREOP */
        (void)APPL_StartMailboxHandler();
        break;
    case 4U: /* PREOP -> SAFEOP */
        (void)APPL_StartInputHandler(&int_mask);
        break;
    case 8U: /* SAFEOP -> OP */
        (void)APPL_StartOutputHandler();
        break;
    default: /* -> INIT */
        break;
    }

    printf("[ECAT] stub: AL state -> %u\n", (unsigned)new_state);
}
#endif /* ECAT_SSC_STUB_TEST_MODE */
