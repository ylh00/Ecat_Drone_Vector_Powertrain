/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * S288 / J288 brushless bus servo driver (half duplex UART)
 *
 *   Servo #1 : UART2 (PC08 TX / PC09 RX), direction switch PC16
 *   Servo #2 : UART3 (PC14 TX / PC15 RX), direction switch PD00
 *
 * The servo bus is single wire.  A SPDT analogue switch selects whether the
 * MCU UART TX or RX pin is connected to the servo signal line, controlled by
 * the direction GPIO.  The active level for "transmit" is a runtime parameter
 * (PARAM_SERVO_DIR_TX_LEVELn) so the same firmware works for either switch
 * polarity.
 *
 * Physical layer : 1-Wire single bus, UART TTL timing, 6.0 Mbps 8N1
 *                 (fixed by the servo; PARAM_SERVO_BAUD must be 6000000)
 * Bus capacity   : 15 ids per bus (0..14), id 15 = broadcast (no response)
 * Command frame  : 20 bytes (0xFE 0xEE, id/mode/timeout, 12 byte control data,
 *                  CRC32 over the first 16 bytes)
 * Feedback frame : 26 bytes (0xFC 0xEE, id/mode/timeout, 19 byte feedback data,
 *                  CRC32 over bytes 2..21)
 *
 * Frame / register details are documented in docs/SERVO_S288.md which is
 * derived from the "无刷数字舵机 J288/S288 使用手册" (protocol section pp.7-10,
 * fault code table p.11).
 *
 * Notes about the API:
 *   - The S288 protocol is a pure PD control / feedback protocol.  There is NO
 *     register read / write, NO set-id and NO bus-scan command in the
 *     documented protocol (those functions exist in the Unitree PC "舵机调试
 *     助手" which uses undocumented frames).  servo_write_register() and
 *     servo_read_register() therefore return -1 (unsupported) and changing the
 *     servo id requires the PC tool; see docs/SERVO_S288.md.
 *   - servo_scan_bus() still works: it sends a normal control frame to every
 *     id and watches for a response.  It is a NON BLOCKING state machine:
 *     call it repeatedly with the same arguments; it returns
 *     SERVO_SCAN_RUNNING (0) while a scan is in progress and
 *     SERVO_SCAN_DONE (1) exactly once when the id bitmap is valid.
 */

#ifndef SERVO_S288_H
#define SERVO_S288_H

#include <stdint.h>
#include "app_config.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Return codes of servo_scan_bus() */
#define SERVO_SCAN_RUNNING   (0)
#define SERVO_SCAN_DONE      (1)
/* generic error codes of the servo API */
#define SERVO_ERR_UNSUPPORTED (-1)
#define SERVO_ERR_INVALID_ARG (-2)
#define SERVO_ERR_IO          (-3)

/* Control modes of the S288 command frame (frame byte 2, bits [6:4]) */
#define SERVO_CTRL_MODE_LOCK    (0U) /* stop / locked (damping, torque off)  */
#define SERVO_CTRL_MODE_FOC     (1U) /* FOC closed loop (position control)   */

typedef enum {
    SERVO_MODE_IDLE = 0,
    SERVO_MODE_POSITION,
    SERVO_MODE_SPEED,
} servo_mode_t;

typedef struct {
    int16_t  pos_d10;        /* actual angle, 0.1 deg                            */
    int16_t  speed_rpm;      /* speed feedback, rpm                              */
    int16_t  current_ma;     /* current feedback, mA (estimated from torque, see
                              * docs/SERVO_S288.md - the S288 does not report the
                              * motor current)                                  */
    uint16_t voltage_mv;     /* bus voltage, mV                                  */
    uint8_t  temp_c;         /* temperature, degC (max of housing/winding)       */
    uint8_t  error;          /* low byte of the S288 MError word (see docs)      */
    uint8_t  online;         /* 1 = recent valid response                        */
    uint8_t  enabled;        /* 1 = servo is enabled (torque on)                 */
    uint16_t ok_count;
    uint16_t err_count;
    uint32_t last_ok_tick_ms;

    /* ---- extended (raw) feedback, appended for the host tool / debugging -- */
    int16_t  torque_cnm;     /* rotor torque, 0.01 Nm                            */
    int16_t  speed_raw;      /* raw rotor speed (2.56/2*pi = 1 rad/s)            */
    int32_t  pos_raw;        /* raw rotor position (32768/2*pi = 1 rad)          */
    uint16_t out_pos;        /* 13 bit output encoder position (8192 = 1 turn)   */
    uint8_t  warn;           /* ExFlag warning bits (3 bit)                      */
    uint8_t  timeout_flag;   /* 1 = servo reports timeout protection active      */
    uint32_t m_error;        /* full 32 bit S288 MError word                     */
} servo_status_t;

/* Initialize both UARTs, direction GPIOs and the polling state machine. */
void servo_init(void);

/* Set the target angle of one servo, unit 0.1 deg.  The value is applied with
 * the per servo gain / offset / min / max parameters, then converted to the
 * rotor side position command of the S288 protocol. */
void servo_set_target_d10(uint8_t ch, int16_t angle_d10);

/* Enable / disable torque output.
 * enable = 1 : FOC closed loop position control (frame mode = 1)
 * enable = 0 : locked / damped, no position control (frame mode = 0) */
void servo_set_enable(uint8_t ch, uint8_t enable);

/* Ask the servo to clear its timeout protection (sends one control frame with
 * the timeout enable bit = 0).  Hardware error bits in MError cannot be
 * cleared over the protocol. */
void servo_clear_fault(uint8_t ch);

/* Non blocking polling task, call from the main loop. */
void servo_task(void);

/* Read the latest status of one servo */
void servo_get_status(uint8_t ch, servo_status_t *status);

/* Change the UART baud rate for both servo buses (applied immediately).
 * Reinitializes both UARTs; the servo only supports 6000000 baud (8N1). */
void servo_set_baudrate(uint32_t baud);

/* Direct register access helpers.  NOT SUPPORTED by the S288/J288 protocol
 * (there is no register read/write command); both functions return -1.  They
 * are kept so the host protocol layer can report "unsupported". */
int servo_write_register(uint8_t ch, uint8_t id, uint16_t reg, const uint8_t *data, uint8_t len);
int servo_read_register(uint8_t ch, uint8_t id, uint16_t reg, uint8_t *data, uint8_t len);

/* Scan a bus for responding servo ids, NON BLOCKING.
 *
 *   ch        : servo bus index (0 = UART2, 1 = UART3)
 *   id_min/max: inclusive raw id range to probe (0..253, the S288 only uses
 *               0..14; ids outside 0..15 are reported as "absent" without
 *               sending a frame)
 *   id_bitmap : 32 byte bitmap, bit (id & 7) of byte (id >> 3); only valid
 *               after SERVO_SCAN_DONE is returned
 *
 * Return: SERVO_SCAN_RUNNING (0) while the scan is running (call again),
 *         SERVO_SCAN_DONE (1) exactly once when the bitmap is complete,
 *         SERVO_ERR_INVALID_ARG (-2) on bad arguments.
 * Starting a new scan while another one is running replaces it.  The polling
 * of the normal servo position control is suspended while a scan runs. */
int servo_scan_bus(uint8_t ch, uint8_t id_min, uint8_t id_max, uint8_t *id_bitmap);

#ifdef __cplusplus
}
#endif

#endif /* SERVO_S288_H */
