/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Application shared runtime state
 */

#ifndef APP_STATE_H
#define APP_STATE_H

#include <stdint.h>
#include "app_config.h"
#include "ecat_pdo.h"

#ifdef __cplusplus
extern "C" {
#endif

/* High level board state, used by the RGB LED status display and the host tool */
typedef enum {
    APP_STATE_BOOT = 0,      /* firmware just started                          */
    APP_STATE_SELF_TEST,     /* peripherals being initialized                  */
    APP_STATE_ECAT_WAIT,     /* waiting for EtherCAT master / EEPROM           */
    APP_STATE_ECAT_PREOP,    /* slave in PRE-OP                                */
    APP_STATE_ECAT_SAFEOP,   /* slave in SAFE-OP                               */
    APP_STATE_ECAT_OP_IDLE,  /* OP but outputs disabled (no run command)       */
    APP_STATE_RUN,           /* OP and outputs enabled                          */
    APP_STATE_FAULT,         /* fault latched, outputs disabled                */
} app_state_t;

typedef struct {
    volatile app_state_t state;
    volatile uint8_t  ecat_al_state;      /* raw AL state from the SSC (1..8)      */
    volatile uint8_t  ecat_link_port0;
    volatile uint8_t  ecat_link_port1;
    volatile uint16_t faults;             /* app_fault_t bitfield                   */
    volatile uint8_t  output_enabled;     /* 1 = outputs driven by master command  */
    volatile uint8_t  esc_tlm_ok[APP_CFG_DSHOT_MOTORS];
    volatile uint8_t  servo_online[APP_CFG_SERVO_COUNT];
    volatile uint8_t  adc_alarm;
    volatile uint32_t loop_counter;
    volatile uint32_t uptime_ms;
    volatile uint32_t ecat_cycle_miss;    /* DC sync jitter / missed cycles        */
} app_state_data_t;

extern app_state_data_t g_app_state;

/* Set / clear a fault bit, automatically moves to APP_STATE_FAULT when a bit
 * is set (unless the fault is non critical). */
void app_state_set_fault(uint16_t fault);
void app_state_clear_fault(uint16_t fault);
void app_state_clear_all_faults(void);
static inline uint8_t app_state_has_fault(void) { return (g_app_state.faults != 0U); }

#ifdef __cplusplus
}
#endif

#endif /* APP_STATE_H */
