/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Small logging helper on top of the UART0 debug console.
 */

#include <stdarg.h>
#include <stdio.h>
#include "app_log.h"

static uint8_t s_log_level[LOG_MOD_MAX];

static const char *const s_log_mod_name[LOG_MOD_MAX] = {
    "MAIN", "ECAT", "DSHOT", "SERVO", "LED", "ADC", "USB", "PARAM", "AM32",
};

void log_init(void)
{
    for (uint8_t i = 0; i < (uint8_t)LOG_MOD_MAX; i++) {
        s_log_level[i] = (uint8_t)APP_LOG_LEVEL;
    }
}

void log_set_level(log_module_t mod, uint8_t level)
{
    if ((uint32_t)mod < (uint32_t)LOG_MOD_MAX) {
        s_log_level[mod] = level;
    }
}

uint8_t log_get_level(log_module_t mod)
{
    return ((uint32_t)mod < (uint32_t)LOG_MOD_MAX) ? s_log_level[mod] : 0U;
}

void log_printf(log_module_t mod, uint8_t level, const char *fmt, ...)
{
    va_list ap;
    const char *prefix;

    if (((uint32_t)mod >= (uint32_t)LOG_MOD_MAX) || (level > s_log_level[mod]) || (level == 0U)) {
        return;
    }

    switch (level) {
    case 1:
        prefix = "E";
        break;
    case 2:
        prefix = "W";
        break;
    case 3:
        prefix = "I";
        break;
    default:
        prefix = "D";
        break;
    }

    printf("[%s][%s] ", prefix, s_log_mod_name[mod]);
    va_start(ap, fmt);
    vprintf(fmt, ap);
    va_end(ap);
    printf("\r\n");
}
