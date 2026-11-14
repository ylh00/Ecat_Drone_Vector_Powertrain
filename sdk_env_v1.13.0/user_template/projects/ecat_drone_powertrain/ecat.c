/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * EtherCAT slave stack startup glue for the ecat_drone_powertrain project.
 *
 * Adapted from hpm_sdk/samples/ethercat/ecat_io/ecat.c.  The sample contains
 * its own main() and runs the slave stack in a blocking while loop; this
 * project has its own bare metal super loop in src/main.c, therefore the same
 * sequence is provided as three functions that src/ecat_app.c calls:
 *
 *   ecat_ssc_init()  - MainInit(), EEPROM emulation callbacks, mapping sizes
 *   ecat_ssc_loop()  - one iteration of MainLoop()
 *   ecat_ssc_stop()  - leave the stack main loop
 *
 * The functions are implemented in this file (instead of the project main.c)
 * so that the SSC integration is kept in one place, exactly mirroring the
 * SDK sample.
 */

#include <stdio.h>

#include "board.h"
#include "app_config.h"
#include "app_log.h"
#include "ecat_config.h"
#include "hpm_ecat_hw.h"

#include "ecat_def.h"
#include "ecatappl.h"
#include "ecatslv.h"
#include "applInterface.h"

#include "ecat_app.h"
#include "ecat_pdo.h"

/* Initialize the slave stack and calculate the process data sizes.
 * Called once from ecat_app_init() after the ESC hardware is initialized. */
void ecat_ssc_init(void)
{
    LOGM_I(LOG_MOD_ECAT, "slave stack init (MainInit)");

    /* SSC stack initialization: sets up the object dictionary, the mailbox,
     * the SYNC manager and the EEPROM handling of the slave stack. */
    MainInit();

#if defined(ESC_EEPROM_EMULATION) && ESC_EEPROM_EMULATION
    /* Serve the ESC EEPROM commands from the flash EEPROM emulation.
     * pAPPL_EEPROM_Store flushes a pending flash write (wear levelling). */
    pAPPL_EEPROM_Read   = ecat_eeprom_emulation_read;
    pAPPL_EEPROM_Write  = ecat_eeprom_emulation_write;
    pAPPL_EEPROM_Reload = ecat_eeprom_emulation_reload;
    pAPPL_EEPROM_Store  = ecat_app_eeprom_store;
#endif

    /* Create the basic mapping: the application layer returns the fixed
     * process data sizes of inc/ecat_pdo.h (RxPDO 22 bytes, TxPDO 48 bytes). */
    (void)APPL_GenerateMapping(&nPdInputSize, &nPdOutputSize);

    LOGM_I(LOG_MOD_ECAT, "process data: TxPDO %u bytes, RxPDO %u bytes",
           (unsigned)nPdInputSize, (unsigned)nPdOutputSize);

    if ((nPdInputSize != APP_ECAT_TXPDO_SIZE) || (nPdOutputSize != APP_ECAT_RXPDO_SIZE)) {
        LOGM_E(LOG_MOD_ECAT,
               "process data size mismatch (expected TxPDO %u / RxPDO %u), check the SSC mapping!",
               (unsigned)APP_ECAT_TXPDO_SIZE, (unsigned)APP_ECAT_RXPDO_SIZE);
    }

    /* Set stack run flag: MainLoop() is executed from ecat_app_task() */
    bRunApplication = TRUE;
}

/* Run one iteration of the slave stack main loop. */
void ecat_ssc_loop(void)
{
    if (bRunApplication == TRUE) {
        MainLoop();
    }
}

/* Stop the slave stack main loop (used before a controlled reset). */
void ecat_ssc_stop(void)
{
    bRunApplication = FALSE;
}
