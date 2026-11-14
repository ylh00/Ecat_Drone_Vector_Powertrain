/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Application task layer: converts the EtherCAT process data into commands for
 * the DShot / servo / LED modules, runs the 1 kHz control loop and builds the
 * TxPDO (telemetry) that is sent back to the master.
 */

#ifndef APP_TASK_H
#define APP_TASK_H

#include <stdint.h>
#include "app_config.h"
#include "ecat_pdo.h"
#include "app_state.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 1 ms tick incremented from the board timer callback (interrupt context). */
extern volatile uint32_t g_app_tick_ms;

/* Initialize all application modules in the correct order. */
void app_task_init(void);

/* Main control loop, called from the main loop as fast as possible.  Internally
 * paced to APP_CFG_CONTROL_LOOP_HZ using g_app_tick_ms. */
void app_task_run(void);

/* Called from the 1 ms board timer ISR: increments g_app_tick_ms and sets the
 * flag that app_task_run() uses for pacing. */
void app_task_tick_isr(void);

/* Build the TxPDO from the module telemetry (also used by the host tool) */
void app_task_build_telemetry(void);

/* Convert a 1000..2000us throttle command to the DShot throttle value */
uint16_t app_task_cmd_us_to_dshot(uint16_t us, uint8_t ch);

#ifdef __cplusplus
}
#endif

#endif /* APP_TASK_H */
