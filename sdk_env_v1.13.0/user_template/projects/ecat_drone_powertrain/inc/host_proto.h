/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Host tool protocol over USB CDC-ACM (virtual COM port)
 *
 * Frame format (little endian, all multi byte fields):
 *
 *   +------+------+-----+-----+-----+-----+===============+------+------+
 *   | 0xA5 | 0x5A | CMD | SEQ | LEN_L| LEN_H|   PAYLOAD     | CRC_L| CRC_H|
 *   +------+------+-----+-----+-----+-----+===============+------+------+
 *     sync   sync  1B    1B    2B           LEN bytes       2B
 *
 *   CRC16: CCITT-FALSE, over CMD..PAYLOAD (header after sync + payload),
 *          polynomial 0x1021, init 0xFFFF, no reflection, no final xor.
 *
 *   LEN  : 0..APP_CFG_HOST_PROTO_MAX_PAYLOAD
 *   SEQ  : echoed by the device in the response
 *
 * Every request is answered by exactly one response frame with the same CMD and
 * SEQ; the first payload byte is the status code (host_status_t).
 *
 * NOTE: the same protocol is documented for the C# host tool in
 *       docs/HOST_PROTOCOL.md - keep them in sync.
 */

#ifndef HOST_PROTO_H
#define HOST_PROTO_H

#include <stdint.h>
#include "app_config.h"

#ifdef __cplusplus
extern "C" {
#endif

#define HOST_PROTO_SYNC0        (0xA5U)
#define HOST_PROTO_SYNC1        (0x5AU)
#define HOST_PROTO_HEADER_SIZE  (6U)
#define HOST_PROTO_CRC_SIZE     (2U)
#define HOST_PROTO_OVERHEAD     (HOST_PROTO_HEADER_SIZE + HOST_PROTO_CRC_SIZE)

/* Magic value returned by HOST_CMD_PING (payload is u32 magic + u16 major +
 * u16 minor + u16 patch).  The bytes on the wire are 'P','I','N','G'. */
#define HOST_PROTO_PING_MAGIC   (0x474E4950UL)

/* Payload size (status byte included) of the responses. */
#define HOST_PROTO_STATUS_SIZE  (1U)
#define HOST_TELEMETRY_RSP_SIZE (HOST_PROTO_STATUS_SIZE + HOST_TELEMETRY_SIZE)
#define HOST_PARAM_DESC_RSP_SIZE (HOST_PROTO_STATUS_SIZE + HOST_PARAM_DESC_SIZE)

/* Maximum number of parameter descriptors in one HOST_CMD_LIST_PARAMS response:
 * status(1) + N * 70 must stay below APP_CFG_HOST_PROTO_MAX_PAYLOAD (512). */
#define HOST_PROTO_LIST_PARAMS_MAX (7U)

/* Maximum raw bytes the board reads back for one HOST_CMD_AM32_XFER.  A full
 * AM32 EEPROM read response is 197 bytes (ESC + CMD + LEN=192 + payload + CRC16
 * = 3 + 192 + 2), so the raw pipe capacity must be >= 197. */
#define HOST_PROTO_AM32_XFER_MAX_IN  (256U)
#define HOST_PROTO_AM32_XFER_MAX_OUT (208U)

typedef enum {
    HOST_STATUS_OK              = 0x00,
    HOST_STATUS_ERR_UNKNOWN_CMD = 0x01,
    HOST_STATUS_ERR_LENGTH      = 0x02,
    HOST_STATUS_ERR_CRC         = 0x03,
    HOST_STATUS_ERR_PARAM_ID    = 0x04,
    HOST_STATUS_ERR_PARAM_RANGE = 0x05,
    HOST_STATUS_ERR_PARAM_RO    = 0x06,
    HOST_STATUS_ERR_BUSY        = 0x07,
    HOST_STATUS_ERR_NOT_READY   = 0x08,
    HOST_STATUS_ERR_TIMEOUT     = 0x09,
    HOST_STATUS_ERR_UNSUPPORTED = 0x0A,
    HOST_STATUS_ERR_FLASH       = 0x0B,
} host_status_t;

typedef enum {
    HOST_CMD_PING = 0x01,            /* req: none            rsp: 4B magic + fw version */
    HOST_CMD_GET_VERSION = 0x02,     /* rsp: u16 major, u16 minor, u16 patch, char[16] date */
    HOST_CMD_GET_TELEMETRY = 0x03,   /* rsp: app_telemetry blob (see docs)                  */
    HOST_CMD_GET_ECAT_STATUS = 0x04, /* rsp: u8 al_state, u8 link, u16 fault, u32 cycles     */
    HOST_CMD_GET_PARAM = 0x10,       /* req: u16 id          rsp: i32 value                  */
    HOST_CMD_SET_PARAM = 0x11,       /* req: u16 id, i32 value  rsp: none                    */
    HOST_CMD_GET_PARAM_DESC = 0x12,  /* req: u16 id          rsp: param_desc_t serialized    */
    HOST_CMD_PARAM_SAVE = 0x13,      /* req: none            rsp: none                      */
    HOST_CMD_PARAM_RESET = 0x14,     /* req: none            rsp: none                      */
    HOST_CMD_PARAM_GET_COUNT = 0x15, /* rsp: u16 count                                      */
    HOST_CMD_LIST_PARAMS = 0x16,     /* req: u16 start, u8 count  rsp: packed descriptors    */

    HOST_CMD_DSHOT_TEST = 0x20,      /* req: u8 esc, u16 throttle  rsp: none                 */
    HOST_CMD_DSHOT_GET_TLM = 0x21,   /* req: u8 esc          rsp: telemetry of one ESC       */
    HOST_CMD_DSHOT_CMD = 0x22,       /* req: u8 esc, u16 dshot cmd  rsp: none                */
    HOST_CMD_SERVO_SCAN = 0x30,      /* req: u8 bus(0/1), u8 id_min, u8 id_max rsp: bitmap   */
    HOST_CMD_SERVO_MOVE_TEST = 0x31, /* req: u8 bus, u8 id, i16 angle_d10     rsp: none       */
    HOST_CMD_SERVO_READ = 0x32,      /* req: u8 bus, u8 id    rsp: servo status blob          */
    HOST_CMD_SERVO_SET_ID = 0x33,    /* req: u8 bus, u8 old id, u8 new id     rsp: none       */
    HOST_CMD_SERVO_RAW = 0x34,       /* req: u8 bus, u16 reg, u8 len ... generic register access */
    HOST_CMD_ADC_GET = 0x40,         /* rsp: adc_mon blob                                    */
    HOST_CMD_LED_SET = 0x41,         /* req: u8 r, u8 g, u8 b  rsp: none                     */
    HOST_CMD_LED_TEST = 0x42,        /* req: u8 pattern, u16 duration_ms  rsp: none           */

    HOST_CMD_AM32_BEGIN = 0x50,      /* req: u8 esc  rsp: none
                                      * only allowed when ECAT is not in OP and the
                                      * motors are stopped, else NOT_READY           */
    HOST_CMD_AM32_XFER = 0x51,       /* req: u8 esc, u8 in_len, u8[in_len] in
                                      * rsp: u8 out_len, u8[out_len] out
                                      * raw 4-way bytes, board sends in[] then
                                      * reads the ESC answer until the frame is
                                      * complete / the line goes idle (max 208 B),
                                      * out_len = bytes actually received.
                                      * Blocking, up to ~150 ms worst case.       */
    HOST_CMD_AM32_END = 0x52,        /* req: u8 esc  rsp: none  (release line)      */
    HOST_CMD_AM32_STATUS = 0x53,     /* req: u8 esc  rsp: u8 state, u8 last_error
                                      * state: 0=idle 1=4way active 2=error         */
    HOST_CMD_AM32_PROG_WRITE = 0x54, /* req: u8 esc, u8 addr(0..191), u8 value
                                      * AM32 DShot programming mode: queue one EEPROM
                                      * byte (36x6, addr, value, 37), non blocking.
                                      * ERR_BUSY = script queue full, retry the same
                                      * request. Gated like AM32_BEGIN.               */
    HOST_CMD_AM32_PROG_SAVE = 0x55,  /* req: u8 esc  rsp: none
                                      * queue save-to-flash (12 x 6). ERR_BUSY while
                                      * the byte script is still running; wait ~50 ms
                                      * after OK before reading back / power cycling. */
    HOST_CMD_BOOTLOADER = 0x60,      /* req: u32 magic 0x424F4F54  rsp: none (enters DFU)    */
    HOST_CMD_RESET = 0x61,           /* req: none            rsp: none then reset            */
} host_cmd_t;

/* Telemetry blob returned by HOST_CMD_GET_TELEMETRY (packed, 54 bytes).
 *
 * This struct is the contract between the firmware, the PC host tool and the
 * documentation.  Keep the offsets stable!  Both sides must use the same
 * layout (little endian, packed).
 */
typedef struct __attribute__((packed)) {
    uint16_t status_word;              /* offset  0 */
    uint16_t fault_code;               /* offset  2 */
    uint32_t esc_erpm[2];              /* offset  4  raw electrical rpm from the
                                        *           BDShot telemetry, 0 = stopped */
    uint16_t esc_tlm_period_us[2];     /* offset 12  raw eRPM period           */
    int16_t  servo_pos_d10[2];         /* offset 16  0.1 deg                   */
    int16_t  servo_speed_rpm[2];       /* offset 20                            */
    int16_t  servo_current_ma[2];      /* offset 24                            */
    uint8_t  servo_temp_c[2];          /* offset 28                            */
    uint8_t  servo_error[2];           /* offset 30                            */
    uint16_t sys_voltage_dv;           /* offset 32  0.1 V                     */
    int16_t  sys_current_ca;           /* offset 34  0.01 A                    */
    uint16_t esc_current_ma[2];        /* offset 36                            */
    uint16_t loop_counter;             /* offset 40                            */
    uint16_t led_status;               /* offset 42                            */
    uint16_t ecat_link_status;         /* offset 44                            */
    uint32_t host_rx_frames;           /* offset 46                            */
    uint32_t host_crc_errors;          /* offset 50                            */
} host_telemetry_t;

#define HOST_TELEMETRY_SIZE (sizeof(host_telemetry_t))

/* Parameter descriptor serialized by HOST_CMD_GET_PARAM_DESC / LIST_PARAMS
 * (70 bytes, little endian, packed):
 *   u16 id; u8 type; u8 persist; u8 readonly; u8 len; i32 min; i32 max;
 *   i32 def; i32 value; char name[16]; char desc[32];
 */
#define HOST_PARAM_DESC_OFF_ID       (0U)
#define HOST_PARAM_DESC_OFF_TYPE     (2U)
#define HOST_PARAM_DESC_OFF_PERSIST  (3U)
#define HOST_PARAM_DESC_OFF_READONLY (4U)
#define HOST_PARAM_DESC_OFF_LEN      (5U)
#define HOST_PARAM_DESC_OFF_MIN      (6U)
#define HOST_PARAM_DESC_OFF_MAX      (10U)
#define HOST_PARAM_DESC_OFF_DEF      (14U)
#define HOST_PARAM_DESC_OFF_VALUE    (18U)
#define HOST_PARAM_DESC_OFF_NAME     (22U)
#define HOST_PARAM_DESC_OFF_DESC     (38U)
#define HOST_PARAM_DESC_NAME_LEN     (16U)
#define HOST_PARAM_DESC_DESC_LEN     (32U)
#define HOST_PARAM_DESC_SIZE         (70U)

/* The same 70 byte descriptor as a packed struct (convenience for the firmware
 * and for C/C++ host tools).  The C# host tool uses the offset macros above. */
typedef struct __attribute__((packed)) {
    uint16_t id;                    /* offset  0 */
    uint8_t  type;                  /* offset  2  param_type_t                */
    uint8_t  persist;               /* offset  3  1 = stored in flash          */
    uint8_t  readonly;              /* offset  4  1 = host must not write      */
    uint8_t  len;                   /* offset  5  strlen(name)                 */
    int32_t  min;                   /* offset  6  */
    int32_t  max;                   /* offset 10  */
    int32_t  def;                   /* offset 14  */
    int32_t  value;                 /* offset 18  current value                */
    char     name[HOST_PARAM_DESC_NAME_LEN]; /* offset 22, NUL terminated      */
    char     desc[HOST_PARAM_DESC_DESC_LEN]; /* offset 38, NUL terminated UTF-8 */
} host_param_desc_t;

/* Response blob of HOST_CMD_DSHOT_GET_TLM (26 bytes, packed) */
typedef struct __attribute__((packed)) {
    uint32_t erpm;                  /* raw electrical rpm, 0 = stopped         */
    uint16_t period_us;             /* raw telemetry period, 0 = no frame      */
    uint8_t  valid;                 /* 1 = last frame passed CRC               */
    uint8_t  state;                 /* dshot_state_t                           */
    uint16_t crc_errors;            /* consecutive decode errors (saturating)  */
    uint32_t ok_count;
    uint32_t err_count;
    uint32_t timeout_count;
    uint32_t last_ok_tick_ms;
} host_dshot_tlm_t;

/* Response blob of HOST_CMD_SERVO_READ (20 bytes, packed) */
typedef struct __attribute__((packed)) {
    int16_t  pos_d10;               /* actual angle, 0.1 deg                   */
    int16_t  speed_rpm;
    int16_t  current_ma;
    uint16_t voltage_mv;
    uint8_t  temp_c;
    uint8_t  error;
    uint8_t  online;
    uint8_t  enabled;
    uint16_t ok_count;
    uint16_t err_count;
    uint32_t last_ok_tick_ms;
} host_servo_status_t;

/* Response blob of HOST_CMD_ADC_GET (24 bytes, packed) */
typedef struct __attribute__((packed)) {
    uint16_t raw_sys_current;       /* offset  0 */
    uint16_t raw_sys_voltage;       /* offset  2 */
    uint16_t raw_esc_current[2];    /* offset  4 */
    uint16_t sys_voltage_dv;        /* offset  8, 0.1 V                        */
    int16_t  sys_current_ca;        /* offset 10, 0.01 A                       */
    uint16_t esc_current_ma[2];     /* offset 12                               */
    uint8_t  alarm;                 /* offset 16, ADC_ALARM_* bitfield         */
    uint8_t  reserved0;             /* offset 17, always 0                     */
    uint16_t sample_count;          /* offset 18                               */
    uint32_t last_update_ms;        /* offset 20                               */
} host_adc_blob_t;

/* Response blob of HOST_CMD_AM32_STATUS */
typedef struct __attribute__((packed)) {
    uint8_t state;                  /* 0=idle 1=4way active 2=error            */
    uint8_t last_error;             /* host_status_t / negative driver error   */
} host_am32_status_t;

/* ---------------------------------------------------------------------------
 * Response payload layouts (after the mandatory status byte, little endian):
 *
 *   PING                u32 magic, u16 major, u16 minor, u16 patch
 *   GET_VERSION         u16 major, u16 minor, u16 patch, char build_date[16]
 *   GET_TELEMETRY       host_telemetry_t
 *   GET_ECAT_STATUS     u8 al_state, u8 link, u16 fault, u32 ecat_cycles
 *   GET_PARAM           i32 value
 *   SET_PARAM           -
 *   GET_PARAM_DESC      host_param_desc_t
 *   PARAM_SAVE          -              (erases/programs the flash sector)
 *   PARAM_RESET         -              (RAM only, then param_apply())
 *   PARAM_GET_COUNT     u16 count
 *   LIST_PARAMS         u16 start, u8 count -> N * host_param_desc_t, N <= 7
 *   DSHOT_TEST          -
 *   DSHOT_GET_TLM       host_dshot_tlm_t
 *   DSHOT_CMD           -
 *   SERVO_SCAN          u8 bitmap[32]  (bit id & 7 of byte id >> 3)
 *   SERVO_MOVE_TEST     -
 *   SERVO_READ          host_servo_status_t
 *   SERVO_SET_ID        -
 *   SERVO_RAW           req: u8 bus, u8 id, u16 reg, u8 wr_len, u8 rd_len,
 *                            u8[wr_len] data
 *                       rsp: u8 rd_len_actual, u8[rd_len_actual] data
 *   ADC_GET             host_adc_blob_t
 *   LED_SET             -
 *   LED_TEST            -
 *   AM32_BEGIN          -
 *   AM32_XFER           u8 out_len, u8[out_len] out (raw 4-way response bytes)
 *   AM32_END            -
 *   AM32_STATUS         host_am32_status_t
 *   AM32_PROG_WRITE     -              (DShot programming mode, one EEPROM byte)
 *   AM32_PROG_SAVE      -              (DShot programming mode, save to flash)
 *   BOOTLOADER          -              (device replies, then reboots to DFU)
 *   RESET               -              (device replies, then reboots)
 * ------------------------------------------------------------------------- */

/* Compute CRC16-CCITT-FALSE */
uint16_t host_proto_crc16(const uint8_t *data, uint32_t len);

/* Run the host protocol engine: consume received USB bytes, run the bounded
 * command dispatcher and queue the responses.  Called from
 * usb_cdc_app_task(), never blocks longer than one command handler. */
void host_proto_app_task(void);

/* Statistics for the telemetry blob (frame level, not USB level). */
void host_proto_app_get_stats(uint32_t *rx_frames, uint32_t *crc_errors);

/* Non-zero while the PC host tool owns the DShot/servo outputs for a bench
 * test: the control loop then skips commanding the ESCs and servos.  The lease
 * expires after PARAM_USB_PROTO_TIMEOUT_MS without a refresh and is never
 * granted while the EtherCAT master is in OP (fails safe). */
uint8_t host_proto_app_bench_active(void);

#ifdef __cplusplus
}
#endif

#endif /* HOST_PROTO_H */
