/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Very small logging helper on top of hpm_debug_console (UART0).
 *
 * Log output must not be used from interrupt context that is timing critical
 * (DShot/ECAT sync).  The control task prints only on state changes.
 */

#ifndef APP_LOG_H
#define APP_LOG_H

#include <stdio.h>
#include "app_config.h"

#ifdef __cplusplus
extern "C" {
#endif

#ifndef APP_LOG_LEVEL
#define APP_LOG_LEVEL (3) /* 0=off 1=error 2=warn 3=info 4=debug */
#endif

#define APP_LOG_TAG "APP"

#if APP_LOG_LEVEL >= 1
#define LOGE(fmt, ...) printf("[E][" APP_LOG_TAG "] " fmt "\r\n", ##__VA_ARGS__)
#else
#define LOGE(fmt, ...) do {} while (0)
#endif

#if APP_LOG_LEVEL >= 2
#define LOGW(fmt, ...) printf("[W][" APP_LOG_TAG "] " fmt "\r\n", ##__VA_ARGS__)
#else
#define LOGW(fmt, ...) do {} while (0)
#endif

#if APP_LOG_LEVEL >= 3
#define LOGI(fmt, ...) printf("[I][" APP_LOG_TAG "] " fmt "\r\n", ##__VA_ARGS__)
#else
#define LOGI(fmt, ...) do {} while (0)
#endif

#if APP_LOG_LEVEL >= 4
#define LOGD(fmt, ...) printf("[D][" APP_LOG_TAG "] " fmt "\r\n", ##__VA_ARGS__)
#else
#define LOGD(fmt, ...) do {} while (0)
#endif

#define LOG_RAW(fmt, ...) printf(fmt, ##__VA_ARGS__)

typedef enum {
    LOG_MOD_MAIN = 0,
    LOG_MOD_ECAT,
    LOG_MOD_DSHOT,
    LOG_MOD_SERVO,
    LOG_MOD_LED,
    LOG_MOD_ADC,
    LOG_MOD_USB,
    LOG_MOD_PARAM,
    LOG_MOD_AM32,
    LOG_MOD_MAX
} log_module_t;

void log_init(void);
void log_set_level(log_module_t mod, uint8_t level);
uint8_t log_get_level(log_module_t mod);
/* Module prefixed log, level checked against the module level */
void log_printf(log_module_t mod, uint8_t level, const char *fmt, ...);

#define LOGM_E(mod, fmt, ...) log_printf(mod, 1, fmt, ##__VA_ARGS__)
#define LOGM_W(mod, fmt, ...) log_printf(mod, 2, fmt, ##__VA_ARGS__)
#define LOGM_I(mod, fmt, ...) log_printf(mod, 3, fmt, ##__VA_ARGS__)
#define LOGM_D(mod, fmt, ...) log_printf(mod, 4, fmt, ##__VA_ARGS__)

#ifdef __cplusplus
}
#endif

#endif /* APP_LOG_H */
