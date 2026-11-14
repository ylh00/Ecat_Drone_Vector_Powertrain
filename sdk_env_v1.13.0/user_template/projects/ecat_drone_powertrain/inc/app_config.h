/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * ecat_drone_powertrain - global application configuration
 *
 * Project:  EtherCAT slave for a dual BLDC / dual bus-servo drone powertrain
 * MCU    :  HPM5E3Y (board ecat_drone_5e3y)
 * SDK    :  hpm_sdk v1.13.0
 */

#ifndef APP_CONFIG_H
#define APP_CONFIG_H

#include "board.h"

/* ------------------------------------------------------------------------- */
/* Firmware identification                                                    */
/* ------------------------------------------------------------------------- */
#define APP_FW_NAME              "ecat_drone_powertrain"
#define APP_FW_VERSION_MAJOR     (1U)
#define APP_FW_VERSION_MINOR     (0U)
#define APP_FW_VERSION_PATCH     (0U)
#define APP_FW_BUILD_DATE        __DATE__
#define APP_FW_BUILD_TIME        __TIME__

/* EtherCAT identity (must match SSC config / ESI) */
#define APP_ECAT_VENDOR_ID       (0x00000E5AU)  /* HPMicro test vendor id, change to your ETG id */
#define APP_ECAT_PRODUCT_CODE    (0x0000E3A1U)
#define APP_ECAT_REVISION_NO     (0x00010001U)
#define APP_ECAT_SERIAL_NO       (0x00000001U)

/* ------------------------------------------------------------------------- */
/* Feature switches                                                           */
/* ------------------------------------------------------------------------- */
#define APP_CFG_ENABLE_DSHOT          (1)
#define APP_CFG_ENABLE_SERVO          (1)
#define APP_CFG_ENABLE_RGB_LED        (1)
#define APP_CFG_ENABLE_ADC_MON        (1)
#define APP_CFG_ENABLE_USB_CDC        (1)
#define APP_CFG_ENABLE_AM32_TUNING    (1)
#define APP_CFG_ENABLE_ECAT           (1)

/* ------------------------------------------------------------------------- */
/* Task / loop timing                                                         */
/* ------------------------------------------------------------------------- */
#define APP_CFG_BOARD_TICK_MS         (1U)     /* 1 ms board software timer         */
#define APP_CFG_CONTROL_LOOP_HZ       (1000U)  /* main control loop rate            */
#define APP_CFG_DSHOT_UPDATE_HZ       (1000U)  /* DShot command update rate         */
#define APP_CFG_SERVO_POLL_HZ         (100U)   /* bus servo polling rate            */
#define APP_CFG_ADC_SAMPLE_DIV        (1U)     /* sample ADC every control loop     */
#define APP_CFG_LED_UPDATE_HZ         (50U)    /* RGB LED animation rate            */

/* ------------------------------------------------------------------------- */
/* DShot                                                                      */
/* ------------------------------------------------------------------------- */
#define APP_CFG_DSHOT_MOTORS          (2U)
/* Throttle received over EtherCAT is 1000..2000us, mapped to DShot 48..2047 */
#define APP_CFG_DSHOT_MIN_THROTTLE    (48U)
#define APP_CFG_DSHOT_MAX_THROTTLE    (2047U)
#define APP_CFG_DSHOT_CMD_MIN_US      (1000U)
#define APP_CFG_DSHOT_CMD_MAX_US      (2000U)
#define APP_CFG_DSHOT_TLM_TIMEOUT_US  (70U)    /* ESC response gap (worst case)     */
#define APP_CFG_DSHOT_TLM_VALID_MS    (100U)   /* telemetry considered valid        */

/* ------------------------------------------------------------------------- */
/* S288 bus servo                                                             */
/* ------------------------------------------------------------------------- */
#define APP_CFG_SERVO_COUNT           (2U)
#define APP_CFG_SERVO_DEFAULT_BAUD    (6000000U) /* J288/S288: fixed 6.0 Mbps, 8N1 */
#define APP_CFG_SERVO_UART_TIMEOUT_US (3000U)
#define APP_CFG_SERVO_ANGLE_MIN_D10   (-3600)  /* -360.0 deg */
#define APP_CFG_SERVO_ANGLE_MAX_D10   (3600)   /* +360.0 deg */
#define APP_CFG_SERVO_ANGLE_DEFAULT_D10 (0)
#define APP_CFG_SERVO_ANGLE_SPAN_D10  (3600)   /* full scale used if servo range is 0..360 */

/* ------------------------------------------------------------------------- */
/* RGB status LED                                                             */
/* ------------------------------------------------------------------------- */
#define APP_CFG_LED_BRIGHTNESS_DEFAULT (128U)

/* ------------------------------------------------------------------------- */
/* ADC monitoring scaling (fixed point)                                       */
/* ------------------------------------------------------------------------- */
/* These are defaults only, the real values are runtime parameters.           */
#define APP_CFG_ADC_SYS_V_MV_PER_LSB_NUM  (1)     /* voltage  = raw * num / den  */
#define APP_CFG_ADC_SYS_V_MV_PER_LSB_DEN  (10)
#define APP_CFG_ADC_SYS_I_MA_PER_LSB_NUM  (1)     /* current  = (raw - ofs) * n/d */
#define APP_CFG_ADC_SYS_I_MA_PER_LSB_DEN  (10)
#define APP_CFG_ADC_ESC_I_MA_PER_LSB_NUM  (1)
#define APP_CFG_ADC_ESC_I_MA_PER_LSB_DEN  (10)

/* ------------------------------------------------------------------------- */
/* USB CDC / host tool                                                        */
/* ------------------------------------------------------------------------- */
#define APP_CFG_USB_VID               (0x0A4DU)  /* HPMicro                           */
#define APP_CFG_USB_PID               (0xE3A2U)
#define APP_CFG_USB_CDC_RX_BUF_SIZE   (1024U)
#define APP_CFG_USB_CDC_TX_BUF_SIZE   (1024U)
#define APP_CFG_HOST_PROTO_MAX_PAYLOAD (512U)

/* ------------------------------------------------------------------------- */
/* EtherCAT process data sizes (see ecat_pdo.h)                               */
/* ------------------------------------------------------------------------- */
#define APP_ECAT_RXPDO_SIZE           (22U)
#define APP_ECAT_TXPDO_SIZE           (48U)

/* EtherCAT timing defaults (overridden by parameters) */
#define APP_CFG_ECAT_SYNC0_PERIOD_US_DEFAULT (1000U)
#define APP_CFG_ECAT_WATCHDOG_MS_DEFAULT     (100U)

/* ------------------------------------------------------------------------- */
/* Common helpers                                                             */
/* ------------------------------------------------------------------------- */
#define APP_ARRAY_SIZE(x) (sizeof(x) / sizeof((x)[0]))
#define APP_MIN(a, b)     (((a) < (b)) ? (a) : (b))
#define APP_MAX(a, b)     (((a) > (b)) ? (a) : (b))
#define APP_CLAMP(x, lo, hi) APP_MIN(APP_MAX((x), (lo)), (hi))

#endif /* APP_CONFIG_H */
