/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Application shared state + fault handling
 */

#include "app_state.h"
#include "app_log.h"

app_state_data_t g_app_state;

void app_state_set_fault(uint16_t fault)
{
    uint16_t previous = g_app_state.faults;

    g_app_state.faults = (uint16_t)(previous | fault);
    if ((fault != 0U) && (previous == 0U)) {
        LOGM_W(LOG_MOD_MAIN, "fault raised: 0x%04X", fault);
    }
    if (g_app_state.faults != 0U) {
        g_app_state.state = APP_STATE_FAULT;
    }
}

void app_state_clear_fault(uint16_t fault)
{
    g_app_state.faults = (uint16_t)(g_app_state.faults & (uint16_t)~fault);
}

void app_state_clear_all_faults(void)
{
    g_app_state.faults = 0U;
    g_app_state.adc_alarm = 0U;
}
