/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * ecat_drone_powertrain - main entry
 *
 * EtherCAT slave for a dual BLDC (bidirectional DShot) / dual S288 bus servo
 * drone powertrain unit.
 *
 * Startup order:
 *   1. board_init()          - clocks, console, pins
 *   2. app_task_init()       - parameter system + all application modules
 *   3. ecat_app_init()       - ESC hardware + SSC slave stack
 *   4. super loop
 *
 * The super loop is intentionally bare metal (no RTOS): the EtherCAT slave
 * stack needs a fast, low latency main loop.  All application modules are
 * non blocking state machines.
 */

#include "board.h"
#include "app_config.h"
#include "app_log.h"
#include "app_state.h"
#include "app_task.h"
#include "ecat_app.h"
#include "dshot.h"
#include "servo_s288.h"
#include "led_rgb.h"
#include "adc_mon.h"
#include "usb_cdc_app.h"
#include "param.h"

/* Main loop statistics, visible to the host tool */
volatile uint32_t g_main_loop_count = 0;
volatile uint32_t g_main_loop_max_us = 0;

int main(void)
{
    uint32_t loop_start_us;
    uint32_t loop_us;

    /* ------------------------------------------------------------------ */
    /* Board initialization (clock tree, console, pinmux, delays)          */
    /* ------------------------------------------------------------------ */
    board_init();

    LOGI("==================================================");
    LOGI(" %s v%u.%u.%u", APP_FW_NAME, APP_FW_VERSION_MAJOR,
         APP_FW_VERSION_MINOR, APP_FW_VERSION_PATCH);
    LOGI(" board : %s", BOARD_NAME);
    LOGI(" built : %s %s", APP_FW_BUILD_DATE, APP_FW_BUILD_TIME);
    LOGI("==================================================");

    /* ------------------------------------------------------------------ */
    /* Application modules                                                  */
    /* ------------------------------------------------------------------ */
    app_task_init();

    LOGI("initializing EtherCAT slave ...");
    ecat_app_init();
    LOGI("initialization done, entering main loop");

    /* ------------------------------------------------------------------ */
    /* Super loop                                                           */
    /* ------------------------------------------------------------------ */
    for (;;) {
        loop_start_us = (uint32_t)hpm_csr_get_core_cycle();

        /* EtherCAT slave stack: keep it as the highest priority work */
        ecat_app_task();

        /* DShot TX/RX state machine (non blocking, must run >= 20 kHz) */
        dshot_task();

        /* S288 bus servo polling state machine */
        servo_task();

        /* ADC monitoring */
        adc_mon_task();

        /* RGB status LED animation */
        led_task();

        /* USB CDC + host protocol */
        usb_cdc_app_task();

        /* Control loop (internally paced to APP_CFG_CONTROL_LOOP_HZ) */
        app_task_run();

        /* Measure the loop duration in CPU cycles (@480MHz -> 480 cycles/us) */
        loop_us = ((uint32_t)hpm_csr_get_core_cycle() - loop_start_us) / (BOARD_CPU_FREQ / 1000000U);
        if (loop_us > g_main_loop_max_us) {
            g_main_loop_max_us = loop_us;
        }
        g_main_loop_count++;
    }

    return 0;
}
