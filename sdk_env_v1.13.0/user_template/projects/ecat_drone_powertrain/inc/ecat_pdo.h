/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * EtherCAT process data definition (RxPDO / TxPDO)
 *
 * The layout below is the contract between:
 *   - the EtherCAT slave stack application layer (APPL_InputMapping / APPL_OutputMapping)
 *   - the control task (app_task.c)
 *   - the PC host tool (only for documentation, the PC sees it through the ESI)
 *   - the SSC configuration (Config/HPM_ECAT_DRONE_Config.xml) and ESI file
 *
 * The structs are packed and use little-endian integers exactly like the
 * EtherCAT process image, so they can be copied with memcpy().
 */

#ifndef ECAT_PDO_H
#define ECAT_PDO_H

#include <stdint.h>
#include "app_config.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ==========================================================================
 * RxPDO : master -> slave (outputs), 0x1600, 20 bytes
 * ========================================================================== */
typedef struct __attribute__((packed)) {
    uint16_t control_word;       /* offset  0: bit0 run/enable, bit1 clear faults,
                                                bit2 emergency stop (0 = stop),
                                                bit3 servo output enable,
                                                bit4 dshot 3D mode request     */
    uint16_t esc_cmd_us[2];      /* offset  2: ESC1/ESC2 throttle command 1000..2000 us */
    int16_t  servo_angle_d10[2]; /* offset  6: servo1/servo2 target angle, unit 0.1 deg */
    uint16_t servo_ctrl_word;    /* offset 10: bit0 servo1 enable, bit1 servo2 enable,
                                                bit2 clear servo fault,
                                                bit3..7 reserved                 */
    uint16_t dshot_cmd[2];       /* offset 12: raw DShot command (0 = none) for ESC1/ESC2
                                                (e.g. 12 = save settings)       */
    uint16_t reserved0;          /* offset 16 */
    uint32_t cycle_counter;      /* offset 18: master write counter for link debug */
} ecat_rxpdo_t;

/* ==========================================================================
 * TxPDO : slave -> master (inputs), 0x1A00, 48 bytes
 * ========================================================================== */
typedef struct __attribute__((packed)) {
    uint16_t status_word;        /* offset  0: bit0 ECAT in OP, bit1 output enabled,
                                                bit2 fault active, bit3 ESC1 telemetry ok,
                                                bit4 ESC2 telemetry ok,
                                                bit5 servo1 online, bit6 servo2 online,
                                                bit7 ADC alarm, bit8 LED test          */
    uint16_t fault_code;         /* offset  2: fault bitfield, see app_fault_t          */

    uint32_t esc_erpm[2];        /* offset  4: electrical RPM from bidirectional DShot   */
    uint16_t esc_tlm_period_us[2]; /* offset 12: raw eRPM period (0 = no telemetry)       */

    int16_t  servo_pos_d10[2];   /* offset 16: servo actual angle, 0.1 deg               */
    int16_t  servo_speed_rpm[2]; /* offset 20: servo speed, rpm                          */
    int16_t  servo_current_ma[2];/* offset 24: servo current, mA                         */
    uint8_t  servo_temp_c[2];    /* offset 28: servo temperature, degC                   */
    uint8_t  servo_error[2];     /* offset 30: servo error/status byte from the S288      */

    uint16_t sys_voltage_dv;     /* offset 32: system voltage, 0.1 V                     */
    int16_t  sys_current_ca;     /* offset 34: system current, 0.01 A                    */
    uint16_t esc_current_ma[2];  /* offset 36: ESC1/ESC2 current, mA                      */

    uint16_t loop_counter;       /* offset 40: increments every control loop              */
    uint16_t led_status;         /* offset 42: current RGB state / status code            */
    uint16_t ecat_link_status;   /* offset 44: bit0 port0 link, bit1 port1 link,
                                                bit2..7 AL state                       */
    uint16_t reserved0;          /* offset 46 */
} ecat_txpdo_t;

/* Compile time size checks */
typedef char ecat_rxpdo_size_check[(sizeof(ecat_rxpdo_t) == APP_ECAT_RXPDO_SIZE) ? 1 : -1];
typedef char ecat_txpdo_size_check[(sizeof(ecat_txpdo_t) == APP_ECAT_TXPDO_SIZE) ? 1 : -1];

/* ==========================================================================
 * status_word bits
 * ========================================================================== */
#define ECAT_STS_OP                (1U << 0)
#define ECAT_STS_OUTPUT_ENABLED    (1U << 1)
#define ECAT_STS_FAULT_ACTIVE      (1U << 2)
#define ECAT_STS_ESC1_TLM_OK       (1U << 3)
#define ECAT_STS_ESC2_TLM_OK       (1U << 4)
#define ECAT_STS_SERVO1_ONLINE     (1U << 5)
#define ECAT_STS_SERVO2_ONLINE     (1U << 6)
#define ECAT_STS_ADC_ALARM         (1U << 7)
#define ECAT_STS_LED_TEST          (1U << 8)

/* ==========================================================================
 * control_word bits
 * ========================================================================== */
#define ECAT_CTRL_RUN              (1U << 0)
#define ECAT_CTRL_CLEAR_FAULT      (1U << 1)
#define ECAT_CTRL_EMERGENCY_STOP   (1U << 2)
#define ECAT_CTRL_SERVO_ENABLE     (1U << 3)
#define ECAT_CTRL_DSHOT_3D         (1U << 4)

/* ==========================================================================
 * fault codes (fault_code bitfield)
 * ========================================================================== */
typedef enum {
    APP_FAULT_NONE              = 0x0000,
    APP_FAULT_ESC1_NO_TLM       = 0x0001,
    APP_FAULT_ESC2_NO_TLM       = 0x0002,
    APP_FAULT_ESC1_NO_CMD       = 0x0004,
    APP_FAULT_ESC2_NO_CMD       = 0x0008,
    APP_FAULT_SERVO1_OFFLINE    = 0x0010,
    APP_FAULT_SERVO2_OFFLINE    = 0x0020,
    APP_FAULT_SERVO1_ERROR      = 0x0040,
    APP_FAULT_SERVO2_ERROR      = 0x0080,
    APP_FAULT_OVER_VOLTAGE      = 0x0100,
    APP_FAULT_UNDER_VOLTAGE     = 0x0200,
    APP_FAULT_OVER_CURRENT_SYS  = 0x0400,
    APP_FAULT_OVER_CURRENT_ESC1 = 0x0800,
    APP_FAULT_OVER_CURRENT_ESC2 = 0x1000,
    APP_FAULT_ECAT_OP_TIMEOUT   = 0x2000,
} app_fault_t;

/* ==========================================================================
 * Process image globals
 *   g_ecat_rxpdo is written by the slave stack (APPL_OutputMapping) and read
 *   by the control task.  g_ecat_txpdo is written by the control task and
 *   read by the slave stack (APPL_InputMapping).
 * ========================================================================== */
extern volatile ecat_rxpdo_t g_ecat_rxpdo;
extern volatile ecat_txpdo_t g_ecat_txpdo;

/* True when the slave stack has reached OP state and process data is valid */
extern volatile uint8_t g_ecat_op_active;

#ifdef __cplusplus
}
#endif

#endif /* ECAT_PDO_H */
