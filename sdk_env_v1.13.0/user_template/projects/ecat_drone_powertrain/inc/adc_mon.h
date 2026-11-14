/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * ADC monitoring
 *
 *   ADC0 IN1  : system current
 *   ADC0 IN8  : system voltage
 *   ADC1 IN3  : ESC1 current sense
 *   ADC1 IN13 : ESC2 current sense
 *
 * The channel -> pad mapping is fixed in silicon (HPM5E00 datasheet "引脚及
 * 功能描述", analog function column) and the board routes the four analog
 * signals to these pads:
 *
 *   ADC0 IN1  -> PF26   (BOARD_ADC0_SYS_CURRENT_CH)
 *   ADC0 IN8  -> PF16   (BOARD_ADC0_SYS_VOLTAGE_CH)
 *   ADC1 IN3  -> PF20   (BOARD_ADC1_ESC1_CURRENT_CH)
 *   ADC1 IN13 -> PF19   (BOARD_ADC1_ESC2_CURRENT_CH)
 *
 * All four pads are configured as analog inputs by the board package
 * (pinmux.c init_pins()), no pad setup is required in this module.
 *
 * Conversion from raw counts to physical units uses the runtime parameters
 * PARAM_ADC_SYS_V_SCALE (uV/LSB) / PARAM_ADC_SYS_I_SCALE (uA/LSB) /
 * PARAM_ADC_ESCn_I_SCALE (uA/LSB) plus the PARAM_ADC_*_OFFSET_* offsets.
 */

#ifndef ADC_MON_H
#define ADC_MON_H

#include <stdint.h>
#include "app_config.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    uint16_t raw_sys_current;
    uint16_t raw_sys_voltage;
    uint16_t raw_esc_current[APP_CFG_DSHOT_MOTORS];
} adc_mon_raw_t;

typedef struct {
    adc_mon_raw_t raw;
    uint16_t sys_voltage_dv;    /* 0.1 V                                        */
    int16_t  sys_current_ca;    /* 0.01 A                                       */
    uint16_t esc_current_ma[APP_CFG_DSHOT_MOTORS];
    uint8_t  alarm;             /* bitfield: 1 over voltage, 2 under voltage,
                                   4 over current system, 8/16 ESC over current */
    uint16_t sample_count;
    uint16_t error_count;       /* ADC sequence timeouts / trigger conflicts     */
    uint32_t last_update_ms;
} adc_mon_data_t;

#define ADC_ALARM_OVER_VOLTAGE   (1U << 0)
#define ADC_ALARM_UNDER_VOLTAGE  (1U << 1)
#define ADC_ALARM_OC_SYSTEM      (1U << 2)
#define ADC_ALARM_OC_ESC1        (1U << 3)
#define ADC_ALARM_OC_ESC2        (1U << 4)

/* Initialize ADC0/ADC1 in continuous (sequence) mode with software trigger. */
void adc_mon_init(void);

/* Sample all four channels and update the converted values.  Non blocking. */
void adc_mon_task(void);

/* Get a snapshot of the latest converted values. */
void adc_mon_get(adc_mon_data_t *data);

/* Recompute the conversion constants from the parameter table. */
void adc_mon_apply_params(void);

#ifdef __cplusplus
}
#endif

#endif /* ADC_MON_H */
