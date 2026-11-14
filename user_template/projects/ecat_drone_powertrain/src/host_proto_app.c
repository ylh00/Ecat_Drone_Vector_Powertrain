/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Host protocol engine (USB CDC-ACM, PC tuning tool)
 *
 * Frame format, CRC and command ids are defined in inc/host_proto.h.  This
 * module implements:
 *   - the byte wise frame parser with resynchronisation and CRC checking,
 *   - the bounded command dispatcher for every HOST_CMD_* command,
 *   - the response framing (status byte first, then the command payload),
 *   - the PC bench override lease (DShot / servo tests own the outputs while
 *     the EtherCAT master is not in OP),
 *   - the asynchronous S288 bus scan state machine,
 *   - the AM32 4-way raw byte bridge,
 *   - the flash parameter save/reset commands,
 *   - the BOOTLOADER (DFU) / RESET commands.
 *
 * Everything runs from usb_cdc_app_task() which is called once per main loop
 * iteration; the parser is limited to HOST_PROTO_RX_BUDGET bytes and at most
 * HOST_PROTO_MAX_DISPATCH commands per call, so the EtherCAT slave stack is
 * never starved.  Only two handlers are inherently blocking and documented as
 * such: PARAM_SAVE (one 4 KB flash sector erase/program, a few ms) and the AM32
 * bridge commands (bit-banged 4-way interface, up to ~150 ms for an EEPROM
 * read); both are refused while the EtherCAT master is in OP.
 */

#include <stdbool.h>
#include <stddef.h>
#include <string.h>

#include "board.h"
#include "app_config.h"
#include "app_log.h"
#include "app_state.h"
#include "app_task.h"
#include "ecat_pdo.h"
#include "param.h"
#include "host_proto.h"
#include "usb_cdc_app.h"
#include "dshot.h"
#include "servo_s288.h"
#include "led_rgb.h"
#include "adc_mon.h"
#include "ecat_app.h"

/* BOOTLOADER (DFU) command: ROM API / reset controller */
#include "hpm_pdgo_drv.h"
#include "hpm_ppor_drv.h"

/* ------------------------------------------------------------------------- */
/* Compile time checks of the wire layouts                                   */
/* ------------------------------------------------------------------------- */

_Static_assert(sizeof(host_telemetry_t) == 54U, "host_telemetry_t must be 54 bytes");
_Static_assert(offsetof(host_telemetry_t, host_rx_frames) == 46U, "telemetry layout changed");
_Static_assert(offsetof(host_telemetry_t, host_crc_errors) == 50U, "telemetry layout changed");
_Static_assert(sizeof(host_param_desc_t) == HOST_PARAM_DESC_SIZE, "param desc size mismatch");
_Static_assert(offsetof(host_param_desc_t, type) == HOST_PARAM_DESC_OFF_TYPE, "param desc layout");
_Static_assert(offsetof(host_param_desc_t, len) == HOST_PARAM_DESC_OFF_LEN, "param desc layout");
_Static_assert(offsetof(host_param_desc_t, min) == HOST_PARAM_DESC_OFF_MIN, "param desc layout");
_Static_assert(offsetof(host_param_desc_t, value) == HOST_PARAM_DESC_OFF_VALUE, "param desc layout");
_Static_assert(offsetof(host_param_desc_t, name) == HOST_PARAM_DESC_OFF_NAME, "param desc layout");
_Static_assert(offsetof(host_param_desc_t, desc) == HOST_PARAM_DESC_OFF_DESC, "param desc layout");
_Static_assert(sizeof(host_dshot_tlm_t) == 26U, "dshot tlm blob size mismatch");
_Static_assert(sizeof(host_servo_status_t) == 20U, "servo status blob size mismatch");
_Static_assert(sizeof(host_adc_blob_t) == 24U, "adc blob size mismatch");
_Static_assert(sizeof(host_am32_status_t) == 2U, "am32 status blob size mismatch");

/* ------------------------------------------------------------------------- */
/* Constants                                                                 */
/* ------------------------------------------------------------------------- */

/* DShot throttle command range of the PC tool (us) and special command range */
#define HOST_DSHOT_MIN_US           (1000U)
#define HOST_DSHOT_MAX_US           (2000U)
#define HOST_DSHOT_MAX_CMD          (2047U)

/* Maximum time a PC bench test may own the outputs without a refresh */
#define HOST_BENCH_MIN_TIMEOUT_MS   (100U)
#define HOST_BENCH_MAX_TIMEOUT_MS   (10000U)

/* Servo scan: the board polls servo_scan_bus() from this task; a full 0..253
 * scan must finish well within this guard time. */
#define HOST_SERVO_SCAN_TIMEOUT_MS  (10000U)

/* Delay between answering a reboot request and actually resetting, so the
 * response frame can leave the USB TX queue. */
#define HOST_REBOOT_DELAY_MS        (30U)

/* A frame that stops in the middle (host crash, unplug, noise) is discarded
 * after this time so the parser cannot be wedged by a truncated frame. */
#define HOST_PROTO_FRAME_TIMEOUT_MS (500U)

/* Parser budget per task call: keeps the main loop latency bounded even when
 * the host floods the link or sends noise. */
#define HOST_PROTO_RX_BUDGET        (1024U)
#define HOST_PROTO_MAX_DISPATCH     (2U)
#define HOST_PROTO_RX_CHUNK         (64U)

/* Maximum payload a handler may put into a response (status byte included in
 * the frame payload, the data buffer is one byte smaller). */
#define HOST_RSP_DATA_MAX           (APP_CFG_HOST_PROTO_MAX_PAYLOAD - HOST_PROTO_STATUS_SIZE)

/* 4-way interface magic used by the SDK DFU trigger (see
 * hpm_sdk/samples/cherryusb/device/dfu/common/hpm_dfu_trigger.c).  It is
 * consumed by the CherryUSB DFU bootloader when the application is built as
 * flash_dfu; without that bootloader the magic is ignored. */
#define HOST_DFU_TRIGGER_MAGIC      (0x55464455UL)
#define HOST_DFU_TRIGGER_GPR_INDEX  (0U)

/* ------------------------------------------------------------------------- */
/* State                                                                     */
/* ------------------------------------------------------------------------- */

/* Frame parser */
typedef enum {
    HOST_PARSE_SYNC0 = 0,
    HOST_PARSE_SYNC1,
    HOST_PARSE_CMD,
    HOST_PARSE_SEQ,
    HOST_PARSE_LEN_L,
    HOST_PARSE_LEN_H,
    HOST_PARSE_PAYLOAD,
    HOST_PARSE_CRC_L,
    HOST_PARSE_CRC_H,
} host_parse_state_t;

static struct {
    host_parse_state_t state;
    uint8_t  cmd;
    uint8_t  seq;
    uint16_t len;
    uint16_t index;
    uint16_t crc_calc;
    uint16_t crc_recv;
    uint8_t  oversize;
    uint32_t last_tick_ms;
} s_parser;

static uint8_t s_rx_payload[APP_CFG_HOST_PROTO_MAX_PAYLOAD];
static uint8_t s_rsp_data[HOST_RSP_DATA_MAX];
static uint8_t s_tx_frame[APP_CFG_HOST_PROTO_MAX_PAYLOAD + HOST_PROTO_OVERHEAD + HOST_PROTO_STATUS_SIZE];

/* Protocol statistics (reported in the telemetry blob) */
static uint32_t s_stat_rx_frames;
static uint32_t s_stat_crc_errors;
static uint32_t s_stat_len_errors;
static uint32_t s_stat_tx_dropped_frames;

/* Bench override lease */
static volatile uint8_t s_bench_active;
static uint32_t s_bench_lease_tick;
/* Throttle set by the host bench test per ESC (0 = stopped).  The AM32 DShot
 * programming mode requires both ESCs at zero throttle. */
static uint16_t s_bench_throttle[APP_CFG_DSHOT_MOTORS];
/* 1 = EEPROM bytes were queued since the last save (so a repeated
 * AM32_PROG_SAVE is a no-op instead of another ESC flash write). */
static uint8_t s_prog_dirty[APP_CFG_DSHOT_MOTORS];

/* Asynchronous servo scan */
typedef enum {
    HOST_SCAN_IDLE = 0,
    HOST_SCAN_RUNNING,
    HOST_SCAN_DONE,
    HOST_SCAN_ERROR,
} host_scan_state_t;

static struct {
    host_scan_state_t state;
    uint8_t bus;
    uint8_t id_min;
    uint8_t id_max;
    uint8_t bitmap[32];
    uint32_t started_tick;
    uint8_t error;
} s_scan;

/* AM32 4-way bridge */
static struct {
    uint8_t state;       /* 0 = idle, 1 = active, 2 = error                  */
    uint8_t esc;
    uint8_t last_error;  /* host_status_t of the last failed operation        */
} s_am32;

/* Deferred reboot (response must be transmitted first) */
static uint8_t  s_reboot_pending;
static uint8_t  s_reboot_to_dfu;
static uint32_t s_reboot_tick;

/* ------------------------------------------------------------------------- */
/* Small helpers                                                             */
/* ------------------------------------------------------------------------- */

static uint16_t host_rd_u16(const uint8_t *p)
{
    return (uint16_t)((uint16_t)p[0] | ((uint16_t)p[1] << 8));
}

static uint32_t host_rd_u32(const uint8_t *p)
{
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

static int16_t host_rd_i16(const uint8_t *p)
{
    return (int16_t)host_rd_u16(p);
}

static int32_t host_rd_i32(const uint8_t *p)
{
    return (int32_t)host_rd_u32(p);
}

static void host_wr_u16(uint8_t *p, uint16_t v)
{
    p[0] = (uint8_t)(v & 0xFFU);
    p[1] = (uint8_t)(v >> 8);
}

static void host_wr_u32(uint8_t *p, uint32_t v)
{
    p[0] = (uint8_t)(v & 0xFFU);
    p[1] = (uint8_t)((v >> 8) & 0xFFU);
    p[2] = (uint8_t)((v >> 16) & 0xFFU);
    p[3] = (uint8_t)((v >> 24) & 0xFFU);
}

static void host_wr_i32(uint8_t *p, int32_t v)
{
    host_wr_u32(p, (uint32_t)v);
}

/* Copy a string into a fixed size, NUL terminated field (zero padded). */
static void host_copy_str(char *dst, uint16_t dst_size, const char *src)
{
    uint16_t i;

    memset(dst, 0, dst_size);
    if (src == NULL) {
        return;
    }
    for (i = 0U; (i + 1U) < dst_size; i++) {
        dst[i] = src[i];
        if (src[i] == '\0') {
            return;
        }
    }
}

static uint8_t host_str_len(const char *s)
{
    uint8_t n = 0U;

    if (s == NULL) {
        return 0U;
    }
    while ((s[n] != '\0') && (n < 255U)) {
        n++;
    }
    return n;
}

/* ------------------------------------------------------------------------- */
/* CRC16-CCITT-FALSE (poly 0x1021, init 0xFFFF, no reflection, no final xor) */
/* ------------------------------------------------------------------------- */

static uint16_t host_crc16_byte(uint16_t crc, uint8_t byte)
{
    uint8_t i;

    crc ^= (uint16_t)((uint16_t)byte << 8);
    for (i = 0U; i < 8U; i++) {
        if ((crc & 0x8000U) != 0U) {
            crc = (uint16_t)((uint16_t)(crc << 1) ^ 0x1021U);
        } else {
            crc = (uint16_t)(crc << 1);
        }
    }
    return crc;
}

uint16_t host_proto_crc16(const uint8_t *data, uint32_t len)
{
    uint16_t crc = 0xFFFFU;
    uint32_t i;

    if (data == NULL) {
        return crc;
    }
    for (i = 0U; i < len; i++) {
        crc = host_crc16_byte(crc, data[i]);
    }
    return crc;
}

/* ------------------------------------------------------------------------- */
/* Response framing                                                          */
/* ------------------------------------------------------------------------- */

static void host_emit_response(uint8_t cmd, uint8_t seq, uint8_t status,
                               const uint8_t *data, uint16_t data_len)
{
    uint16_t payload_len;
    uint16_t frame_len;
    uint16_t crc;

    if (data_len > HOST_RSP_DATA_MAX) {
        data = NULL;
        data_len = 0U;
        status = HOST_STATUS_ERR_LENGTH;
    }
    payload_len = (uint16_t)(HOST_PROTO_STATUS_SIZE + data_len);
    frame_len = (uint16_t)(HOST_PROTO_OVERHEAD + payload_len);

    if (usb_cdc_app_tx_free() < frame_len) {
        /* Keep the byte stream frame aligned: drop the whole response and let
         * the host tool retry after its timeout. */
        s_stat_tx_dropped_frames++;
        LOGM_W(LOG_MOD_USB, "TX ring full, response for cmd 0x%02X dropped", cmd);
        return;
    }

    s_tx_frame[0] = HOST_PROTO_SYNC0;
    s_tx_frame[1] = HOST_PROTO_SYNC1;
    s_tx_frame[2] = cmd;
    s_tx_frame[3] = seq;
    host_wr_u16(&s_tx_frame[4], payload_len);
    s_tx_frame[HOST_PROTO_HEADER_SIZE] = status;
    if ((data != NULL) && (data_len > 0U)) {
        memcpy(&s_tx_frame[HOST_PROTO_HEADER_SIZE + HOST_PROTO_STATUS_SIZE], data, data_len);
    }
    crc = host_proto_crc16(&s_tx_frame[2],
                           (uint32_t)HOST_PROTO_HEADER_SIZE - 2U + payload_len);
    s_tx_frame[HOST_PROTO_HEADER_SIZE + payload_len] = (uint8_t)(crc & 0xFFU);
    s_tx_frame[HOST_PROTO_HEADER_SIZE + payload_len + 1U] = (uint8_t)(crc >> 8);

    (void)usb_cdc_app_write(s_tx_frame, frame_len);
}

/* ------------------------------------------------------------------------- */
/* Bench override lease                                                      */
/* ------------------------------------------------------------------------- */

/* Abort any pending AM32 DShot programming script (used on lease expiry and
 * when the EtherCAT master takes over the outputs). */
static void host_am32_prog_abort_all(void)
{
    uint8_t esc;

    for (esc = 0U; esc < APP_CFG_DSHOT_MOTORS; esc++) {
        if (dshot_prog_busy(esc) != 0U) {
            dshot_prog_abort(esc);
            LOGM_W(LOG_MOD_USB, "AM32 programming script on ESC %u aborted", (unsigned)esc);
        }
        /* after an abort the ESC RAM buffer state is unknown: force the host to
         * rewrite the image before the next save is accepted */
        s_prog_dirty[esc] = 0U;
    }
}

static void host_bench_release(void)
{
    if (s_bench_active == 0U) {
        return;
    }
    s_bench_active = 0U;
    host_am32_prog_abort_all();
    memset(s_bench_throttle, 0, sizeof(s_bench_throttle));
    /* fail safe: stop both ESCs and disable both servos when the lease ends */
    dshot_set_throttle(0U, DSHOT_CMD_MOTOR_STOP);
    dshot_set_throttle(1U, DSHOT_CMD_MOTOR_STOP);
    dshot_enable(0U);
    servo_set_enable(0U, 0U);
    servo_set_enable(1U, 0U);
    LOGM_I(LOG_MOD_USB, "bench override lease ended, outputs released");
}

static void host_bench_refresh(void)
{
    s_bench_lease_tick = g_app_tick_ms;
    s_bench_active = 1U;
}

uint8_t host_proto_app_bench_active(void)
{
    uint32_t timeout;

    if (s_bench_active == 0U) {
        return 0U;
    }
    if (g_ecat_op_active != 0U) {
        /* the EtherCAT master has the outputs, never fight with it */
        host_bench_release();
        return 0U;
    }
    timeout = (uint32_t)param_get(PARAM_USB_PROTO_TIMEOUT_MS);
    if (timeout < HOST_BENCH_MIN_TIMEOUT_MS) {
        timeout = HOST_BENCH_MIN_TIMEOUT_MS;
    }
    if (timeout > HOST_BENCH_MAX_TIMEOUT_MS) {
        timeout = HOST_BENCH_MAX_TIMEOUT_MS;
    }
    if ((uint32_t)(g_app_tick_ms - s_bench_lease_tick) > timeout) {
        host_bench_release();
        return 0U;
    }
    return 1U;
}

/* ------------------------------------------------------------------------- */
/* Servo scan state machine                                                  */
/* ------------------------------------------------------------------------- */

static void host_scan_reset(void)
{
    memset(&s_scan, 0, sizeof(s_scan));
    s_scan.state = HOST_SCAN_IDLE;
}

/* Poll the servo driver while a scan is in progress.  Called from
 * host_proto_app_task() (and thus at least once per main loop iteration). */
static void host_scan_poll(void)
{
    int rc;

    if (s_scan.state != HOST_SCAN_RUNNING) {
        return;
    }
    rc = servo_scan_bus(s_scan.bus, s_scan.id_min, s_scan.id_max, s_scan.bitmap);
    if (rc == 1) {
        s_scan.state = HOST_SCAN_DONE;
        LOGM_I(LOG_MOD_USB, "servo scan bus %u id %u..%u done",
               (unsigned)s_scan.bus, (unsigned)s_scan.id_min, (unsigned)s_scan.id_max);
    } else if (rc < 0) {
        s_scan.state = HOST_SCAN_ERROR;
        s_scan.error = HOST_STATUS_ERR_TIMEOUT;
        LOGM_W(LOG_MOD_USB, "servo scan failed (%d)", rc);
    } else if ((uint32_t)(g_app_tick_ms - s_scan.started_tick) > HOST_SERVO_SCAN_TIMEOUT_MS) {
        s_scan.state = HOST_SCAN_ERROR;
        s_scan.error = HOST_STATUS_ERR_TIMEOUT;
        LOGM_W(LOG_MOD_USB, "servo scan timeout");
    } else {
        /* still running */
    }
}

/* ------------------------------------------------------------------------- */
/* Deferred reboot                                                           */
/* ------------------------------------------------------------------------- */

static void host_request_reboot(uint8_t to_dfu)
{
    s_reboot_to_dfu = to_dfu;
    s_reboot_tick = g_app_tick_ms;
    s_reboot_pending = 1U;
}

static void host_system_reset_imm(void)
{
    LOGM_W(LOG_MOD_USB, "host requested a system reset");
    ppor_reset_mask_set_source_enable(HPM_PPOR, (uint32_t)ppor_reset_software);
    ppor_sw_reset(HPM_PPOR, 24U);
    for (;;) {
        /* wait for the reset to take effect */
    }
}

static void host_enter_dfu_imm(void)
{
    LOGM_W(LOG_MOD_USB, "rebooting into the DFU bootloader");

    /* Same trigger the SDK CherryUSB DFU example uses: write a magic to a
     * retention GPR and reset; the DFU bootloader checks it on startup. */
    if (!pdgo_is_retention_mode_enabled(HPM_PDGO)) {
        pdgo_enable_retention_mode(HPM_PDGO);
    }
    pdgo_write_gpr(HPM_PDGO, HOST_DFU_TRIGGER_GPR_INDEX, HOST_DFU_TRIGGER_MAGIC);

    ppor_reset_mask_set_source_enable(HPM_PPOR, (uint32_t)ppor_reset_software);
    ppor_sw_reset(HPM_PPOR, 24U);
    for (;;) {
        /* wait for the reset to take effect */
    }
}

/* ------------------------------------------------------------------------- */
/* Parameter descriptor serialisation                                        */
/* ------------------------------------------------------------------------- */

static void host_fill_param_desc(int32_t id, const param_desc_t *desc, host_param_desc_t *out)
{
    memset(out, 0, sizeof(*out));
    out->id = (uint16_t)id;
    out->type = (uint8_t)desc->type;
    out->persist = desc->persist;
    out->readonly = desc->readonly;
    host_copy_str(out->name, sizeof(out->name), desc->name);
    out->len = host_str_len(out->name);
    out->min = desc->min;
    out->max = desc->max;
    out->def = desc->def;
    out->value = param_get((param_id_t)id);
    host_copy_str(out->desc, sizeof(out->desc), desc->desc);
}

/* ------------------------------------------------------------------------- */
/* Command handlers                                                          */
/* ------------------------------------------------------------------------- */

static uint8_t host_cmd_ping(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    (void)in;
    (void)in_len;

    host_wr_u32(&out[0], HOST_PROTO_PING_MAGIC);
    host_wr_u16(&out[4], (uint16_t)APP_FW_VERSION_MAJOR);
    host_wr_u16(&out[6], (uint16_t)APP_FW_VERSION_MINOR);
    host_wr_u16(&out[8], (uint16_t)APP_FW_VERSION_PATCH);
    *out_len = 10U;
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_get_version(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    (void)in;
    (void)in_len;

    host_wr_u16(&out[0], (uint16_t)APP_FW_VERSION_MAJOR);
    host_wr_u16(&out[2], (uint16_t)APP_FW_VERSION_MINOR);
    host_wr_u16(&out[4], (uint16_t)APP_FW_VERSION_PATCH);
    host_copy_str((char *)&out[6], 16U, APP_FW_BUILD_DATE);
    *out_len = 22U;
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_get_telemetry(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    host_telemetry_t tlm;
    ecat_txpdo_t pdo;
    uint32_t frames = 0U;
    uint32_t crcs = 0U;
    uint32_t drops = 0U;
    uint32_t i;

    (void)in;
    (void)in_len;

    /* consistent snapshot of the TxPDO (written by the 1 kHz control task) */
    for (i = 0U; i < sizeof(pdo); i++) {
        ((uint8_t *)&pdo)[i] = ((const uint8_t *)&g_ecat_txpdo)[i];
    }

    memset(&tlm, 0, sizeof(tlm));
    tlm.status_word = pdo.status_word;
    tlm.fault_code = g_app_state.faults;
    for (i = 0U; i < APP_CFG_DSHOT_MOTORS; i++) {
        tlm.esc_erpm[i] = pdo.esc_erpm[i];
        tlm.esc_tlm_period_us[i] = pdo.esc_tlm_period_us[i];
    }
    for (i = 0U; i < APP_CFG_SERVO_COUNT; i++) {
        tlm.servo_pos_d10[i] = pdo.servo_pos_d10[i];
        tlm.servo_speed_rpm[i] = pdo.servo_speed_rpm[i];
        tlm.servo_current_ma[i] = pdo.servo_current_ma[i];
        tlm.servo_temp_c[i] = pdo.servo_temp_c[i];
        tlm.servo_error[i] = pdo.servo_error[i];
    }
    tlm.sys_voltage_dv = pdo.sys_voltage_dv;
    tlm.sys_current_ca = pdo.sys_current_ca;
    for (i = 0U; i < APP_CFG_DSHOT_MOTORS; i++) {
        tlm.esc_current_ma[i] = pdo.esc_current_ma[i];
    }
    tlm.loop_counter = (uint16_t)g_app_state.loop_counter;
    tlm.led_status = pdo.led_status;
    tlm.ecat_link_status = pdo.ecat_link_status;

    usb_cdc_app_get_stats(&frames, &crcs, &drops);
    tlm.host_rx_frames = frames;
    tlm.host_crc_errors = crcs;

    memcpy(out, &tlm, sizeof(tlm));
    *out_len = (uint16_t)sizeof(tlm);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_get_ecat_status(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t port0 = 0U;
    uint8_t port1 = 0U;

    (void)in;
    (void)in_len;

    ecat_app_get_link_status(&port0, &port1);
    out[0] = ecat_app_get_al_state();
    out[1] = (uint8_t)((port0 != 0U ? 0x01U : 0x00U) | (port1 != 0U ? 0x02U : 0x00U));
    host_wr_u16(&out[2], g_app_state.faults);
    host_wr_u32(&out[4], g_app_state.ecat_cycle_miss);
    *out_len = 8U;
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_get_param(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint16_t id;
    const param_desc_t *desc;

    if (in_len < 2U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    id = host_rd_u16(in);
    desc = param_get_desc((param_id_t)id);
    if (desc == NULL) {
        return HOST_STATUS_ERR_PARAM_ID;
    }
    host_wr_i32(out, param_get((param_id_t)id));
    *out_len = 4U;
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_set_param(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint16_t id;
    int32_t value;
    int rc;

    (void)out;
    (void)out_len;

    if (in_len < 6U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    id = host_rd_u16(in);
    value = host_rd_i32(&in[2]);

    rc = param_set((param_id_t)id, value);
    switch (rc) {
    case PARAM_OK:
        param_apply();
        return HOST_STATUS_OK;
    case PARAM_ERR_ID:
        return HOST_STATUS_ERR_PARAM_ID;
    case PARAM_ERR_READONLY:
        return HOST_STATUS_ERR_PARAM_RO;
    case PARAM_ERR_FLASH:
        return HOST_STATUS_ERR_FLASH;
    case PARAM_ERR_RANGE:
    case PARAM_ERR_TYPE:
    default:
        return HOST_STATUS_ERR_PARAM_RANGE;
    }
}

static uint8_t host_cmd_get_param_desc(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint16_t id;
    const param_desc_t *desc;
    host_param_desc_t packed;

    if (in_len < 2U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    id = host_rd_u16(in);
    desc = param_get_desc((param_id_t)id);
    if (desc == NULL) {
        return HOST_STATUS_ERR_PARAM_ID;
    }
    host_fill_param_desc((int32_t)id, desc, &packed);
    memcpy(out, &packed, sizeof(packed));
    *out_len = (uint16_t)sizeof(packed);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_param_save(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    int rc;

    (void)in;
    (void)in_len;
    (void)out;
    (void)out_len;

    /* A flash sector erase/program runs with interrupts disabled and stalls the
     * XPI fetch for several milliseconds, which would break the EtherCAT
     * process data timing.  Refuse while the slave is in OP: the host tool must
     * leave OP (or the master stop) before saving parameters. */
    if (g_ecat_op_active != 0U) {
        return HOST_STATUS_ERR_NOT_READY;
    }

    LOGM_I(LOG_MOD_USB, "saving parameters to flash ...");
    rc = param_save();
    if (rc != PARAM_OK) {
        LOGM_E(LOG_MOD_USB, "parameter flash save failed (%d)", rc);
        return HOST_STATUS_ERR_FLASH;
    }
    LOGM_I(LOG_MOD_USB, "parameters saved");
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_param_reset(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    (void)in;
    (void)in_len;
    (void)out;
    (void)out_len;

    param_reset_defaults();
    param_apply();
    LOGM_I(LOG_MOD_USB, "parameters reset to defaults (RAM only)");
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_param_count(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    (void)in;
    (void)in_len;

    host_wr_u16(out, param_count());
    *out_len = 2U;
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_list_params(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint16_t start;
    uint8_t count;
    uint8_t n = 0U;
    uint16_t total = param_count();

    if (in_len < 3U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    start = host_rd_u16(in);
    count = in[2];

    if (start >= total) {
        *out_len = 0U;
        return HOST_STATUS_OK;
    }
    if (count > HOST_PROTO_LIST_PARAMS_MAX) {
        count = HOST_PROTO_LIST_PARAMS_MAX;
    }
    while ((n < count) && ((uint16_t)(start + n) < total)) {
        const param_desc_t *desc = param_get_desc((param_id_t)(start + n));
        host_param_desc_t packed;

        if (desc == NULL) {
            break;
        }
        host_fill_param_desc((int32_t)(start + n), desc, &packed);
        memcpy(&out[(uint32_t)n * sizeof(packed)], &packed, sizeof(packed));
        n++;
    }
    *out_len = (uint16_t)((uint16_t)n * (uint16_t)sizeof(host_param_desc_t));
    return HOST_STATUS_OK;
}

/* --- DShot --------------------------------------------------------------- */

static uint8_t host_dshot_check_ready(uint8_t esc)
{
    if (esc >= APP_CFG_DSHOT_MOTORS) {
        return HOST_STATUS_ERR_PARAM_RANGE;
    }
    if (g_ecat_op_active != 0U) {
        return HOST_STATUS_ERR_NOT_READY;
    }
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_dshot_test(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t esc;
    uint16_t throttle;
    uint16_t dshot_value;
    uint8_t status;

    (void)out;
    (void)out_len;

    if (in_len < 3U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    esc = in[0];
    throttle = host_rd_u16(&in[1]);

    status = host_dshot_check_ready(esc);
    if (status != HOST_STATUS_OK) {
        return status;
    }
    if ((throttle != 0U) &&
        ((throttle < HOST_DSHOT_MIN_US) || (throttle > HOST_DSHOT_MAX_US))) {
        return HOST_STATUS_ERR_PARAM_RANGE;
    }

    dshot_value = (throttle == 0U) ? DSHOT_CMD_MOTOR_STOP : dshot_us_to_throttle(throttle);
    host_bench_refresh();
    dshot_enable(1U);
    dshot_set_throttle(esc, dshot_value);
    s_bench_throttle[esc] = dshot_value;
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_dshot_get_tlm(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t esc;
    dshot_telemetry_t tlm;
    host_dshot_tlm_t packed;

    if (in_len < 1U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    esc = in[0];
    if (esc >= APP_CFG_DSHOT_MOTORS) {
        return HOST_STATUS_ERR_PARAM_RANGE;
    }
    memset(&tlm, 0, sizeof(tlm));
    dshot_get_telemetry(esc, &tlm);

    memset(&packed, 0, sizeof(packed));
    packed.erpm = tlm.erpm;
    packed.period_us = tlm.period_us;
    packed.valid = tlm.valid;
    packed.state = tlm.state;
    packed.crc_errors = tlm.crc_errors;
    packed.ok_count = tlm.ok_count;
    packed.err_count = tlm.err_count;
    packed.timeout_count = tlm.timeout_count;
    packed.last_ok_tick_ms = tlm.last_ok_tick_ms;
    memcpy(out, &packed, sizeof(packed));
    *out_len = (uint16_t)sizeof(packed);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_dshot_cmd(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t esc;
    uint16_t cmd;

    (void)out;
    (void)out_len;

    if (in_len < 3U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    esc = in[0];
    cmd = host_rd_u16(&in[1]);

    /* 0xFF = broadcast to both ESCs */
    if ((esc != 0xFFU) && (esc >= APP_CFG_DSHOT_MOTORS)) {
        return HOST_STATUS_ERR_PARAM_RANGE;
    }
    if (cmd > HOST_DSHOT_MAX_CMD) {
        return HOST_STATUS_ERR_PARAM_RANGE;
    }
    if (g_ecat_op_active != 0U) {
        return HOST_STATUS_ERR_NOT_READY;
    }

    host_bench_refresh();
    dshot_enable(1U);
    dshot_send_command(esc, cmd);
    return HOST_STATUS_OK;
}

/* --- bus servo ----------------------------------------------------------- */

static uint8_t host_servo_check_id(uint8_t bus, uint8_t id)
{
    if (bus >= APP_CFG_SERVO_COUNT) {
        return HOST_STATUS_ERR_PARAM_RANGE;
    }
    if (id != (uint8_t)param_get(PARAM_SERVO_ID(bus))) {
        /* the PC must configure the matching PARAM_SERVO_IDn first */
        return HOST_STATUS_ERR_PARAM_ID;
    }
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_servo_scan(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t bus;
    uint8_t id_min;
    uint8_t id_max;
    int rc;

    if (in_len < 3U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    bus = in[0];
    id_min = in[1];
    id_max = in[2];
    if ((bus >= APP_CFG_SERVO_COUNT) || (id_min > id_max) || (id_max > 253U)) {
        return HOST_STATUS_ERR_PARAM_RANGE;
    }

    /* already an open result for the same request? -> deliver it */
    if (s_scan.state == HOST_SCAN_DONE) {
        if ((s_scan.bus == bus) && (s_scan.id_min == id_min) && (s_scan.id_max == id_max)) {
            memcpy(out, s_scan.bitmap, sizeof(s_scan.bitmap));
            *out_len = (uint16_t)sizeof(s_scan.bitmap);
            host_scan_reset();
            return HOST_STATUS_OK;
        }
        host_scan_reset();
    }
    if (s_scan.state == HOST_SCAN_ERROR) {
        uint8_t err = s_scan.error;

        if ((s_scan.bus == bus) && (s_scan.id_min == id_min) && (s_scan.id_max == id_max)) {
            host_scan_reset();
            return err;
        }
        host_scan_reset();
    }
    if (s_scan.state == HOST_SCAN_RUNNING) {
        /* the host retries until the scan is finished */
        return HOST_STATUS_ERR_BUSY;
    }

    /* start a new scan */
    memset(s_scan.bitmap, 0, sizeof(s_scan.bitmap));
    s_scan.bus = bus;
    s_scan.id_min = id_min;
    s_scan.id_max = id_max;
    s_scan.started_tick = g_app_tick_ms;
    s_scan.error = HOST_STATUS_ERR_TIMEOUT;
    rc = servo_scan_bus(bus, id_min, id_max, s_scan.bitmap);
    if (rc == 1) {
        memcpy(out, s_scan.bitmap, sizeof(s_scan.bitmap));
        *out_len = (uint16_t)sizeof(s_scan.bitmap);
        host_scan_reset();
        return HOST_STATUS_OK;
    }
    if (rc < 0) {
        host_scan_reset();
        return HOST_STATUS_ERR_TIMEOUT;
    }
    s_scan.state = HOST_SCAN_RUNNING;
    return HOST_STATUS_ERR_BUSY;
}

static uint8_t host_cmd_servo_move_test(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t bus;
    uint8_t id;
    int16_t angle;
    uint8_t status;

    (void)out;
    (void)out_len;

    if (in_len < 4U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    bus = in[0];
    id = in[1];
    angle = host_rd_i16(&in[2]);

    status = host_servo_check_id(bus, id);
    if (status != HOST_STATUS_OK) {
        return status;
    }
    if (g_ecat_op_active != 0U) {
        return HOST_STATUS_ERR_NOT_READY;
    }

    host_bench_refresh();
    servo_set_enable(bus, 1U);
    servo_set_target_d10(bus, angle);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_servo_read(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t bus;
    uint8_t id;
    uint8_t status;
    servo_status_t st;
    host_servo_status_t packed;

    if (in_len < 2U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    bus = in[0];
    id = in[1];
    status = host_servo_check_id(bus, id);
    if (status != HOST_STATUS_OK) {
        return status;
    }
    memset(&st, 0, sizeof(st));
    servo_get_status(bus, &st);

    packed.pos_d10 = st.pos_d10;
    packed.speed_rpm = st.speed_rpm;
    packed.current_ma = st.current_ma;
    packed.voltage_mv = st.voltage_mv;
    packed.temp_c = st.temp_c;
    packed.error = st.error;
    packed.online = st.online;
    packed.enabled = st.enabled;
    packed.ok_count = st.ok_count;
    packed.err_count = st.err_count;
    packed.last_ok_tick_ms = st.last_ok_tick_ms;
    memcpy(out, &packed, sizeof(packed));
    *out_len = (uint16_t)sizeof(packed);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_servo_set_id(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    (void)in;
    (void)in_len;
    (void)out;
    (void)out_len;
    /* The S288/J288 protocol has no register/EEPROM access command, so the
     * servo id cannot be changed through the bus by this firmware. */
    return HOST_STATUS_ERR_UNSUPPORTED;
}

static uint8_t host_cmd_servo_raw(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t bus;
    uint8_t id;
    uint16_t reg;
    uint8_t wr_len;
    uint8_t rd_len;
    uint8_t status;
    uint8_t actual = 0U;

    if (in_len < 6U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    bus = in[0];
    id = in[1];
    reg = host_rd_u16(&in[2]);
    wr_len = in[4];
    rd_len = in[5];
    if ((wr_len > 16U) || (rd_len > 16U) || (in_len < (uint16_t)(6U + wr_len))) {
        return HOST_STATUS_ERR_LENGTH;
    }
    status = host_servo_check_id(bus, id);
    if (status != HOST_STATUS_OK) {
        return status;
    }

    if (wr_len > 0U) {
        if (servo_write_register(bus, id, reg, &in[6], wr_len) < 0) {
            return HOST_STATUS_ERR_UNSUPPORTED;
        }
    }
    if (rd_len > 0U) {
        int rc;

        memset(out, 0, rd_len);
        rc = servo_read_register(bus, id, reg, &out[1], rd_len);
        if (rc < 0) {
            return HOST_STATUS_ERR_UNSUPPORTED;
        }
        actual = (uint8_t)rc;
    }
    out[0] = actual;
    *out_len = (uint16_t)(1U + actual);
    return HOST_STATUS_OK;
}

/* --- ADC / LED ----------------------------------------------------------- */

static uint8_t host_cmd_adc_get(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    adc_mon_data_t adc;
    host_adc_blob_t packed;

    (void)in;
    (void)in_len;

    memset(&adc, 0, sizeof(adc));
    adc_mon_get(&adc);

    memset(&packed, 0, sizeof(packed));
    packed.raw_sys_current = adc.raw.raw_sys_current;
    packed.raw_sys_voltage = adc.raw.raw_sys_voltage;
    packed.raw_esc_current[0] = adc.raw.raw_esc_current[0];
    packed.raw_esc_current[1] = adc.raw.raw_esc_current[1];
    packed.sys_voltage_dv = adc.sys_voltage_dv;
    packed.sys_current_ca = adc.sys_current_ca;
    packed.esc_current_ma[0] = adc.esc_current_ma[0];
    packed.esc_current_ma[1] = adc.esc_current_ma[1];
    packed.alarm = adc.alarm;
    packed.sample_count = adc.sample_count;
    packed.last_update_ms = adc.last_update_ms;
    memcpy(out, &packed, sizeof(packed));
    *out_len = (uint16_t)sizeof(packed);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_led_set(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    (void)out;
    (void)out_len;

    if (in_len < 3U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    led_set_rgb(in[0], in[1], in[2]);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_led_test(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    (void)out;
    (void)out_len;

    if (in_len < 3U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    led_test_pattern(in[0], host_rd_u16(&in[1]));
    return HOST_STATUS_OK;
}

/* --- AM32 4-way bridge --------------------------------------------------- */

static uint8_t host_am32_available(void)
{
#if APP_CFG_ENABLE_AM32_TUNING
    if (param_get(PARAM_AM32_ENABLE_PASSTHROUGH) == 0) {
        return 0U;
    }
    return 1U;
#else
    return 0U;
#endif
}

static uint8_t host_cmd_am32_begin(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t esc;
    int rc;

    (void)out;
    (void)out_len;

    if (in_len < 1U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    esc = in[0];
    if (host_am32_available() == 0U) {
        return HOST_STATUS_ERR_UNSUPPORTED;
    }
    if (esc >= APP_CFG_DSHOT_MOTORS) {
        return HOST_STATUS_ERR_PARAM_RANGE;
    }
    if ((g_ecat_op_active != 0U) || (g_app_state.output_enabled != 0U)) {
        return HOST_STATUS_ERR_NOT_READY;
    }
    if (s_am32.state == 1U) {
        return HOST_STATUS_ERR_BUSY;
    }
    /* the DShot programming mode must not be active when the 4-way bridge
     * parks the line (dshot_line_park() would abort it mid-save) */
    if ((dshot_prog_busy(0U) != 0U) || (dshot_prog_busy(1U) != 0U)) {
        return HOST_STATUS_ERR_BUSY;
    }

    rc = am32_4way_begin(esc);
    if (rc != 0) {
        s_am32.state = 2U;
        s_am32.last_error = HOST_STATUS_ERR_TIMEOUT;
        LOGM_W(LOG_MOD_USB, "AM32 begin on ESC %u failed (%d)", (unsigned)esc, rc);
        return HOST_STATUS_ERR_TIMEOUT;
    }
    s_am32.state = 1U;
    s_am32.esc = esc;
    s_am32.last_error = 0U;
    /* entering 4-way mode parks the line and aborts any pending programming
     * script in the driver: its RAM buffer is no longer valid */
    s_prog_dirty[0] = 0U;
    s_prog_dirty[1] = 0U;
    LOGM_I(LOG_MOD_USB, "AM32 4-way bridge acquired ESC %u", (unsigned)esc);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_am32_xfer(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t esc;
    uint8_t tx_len;
    uint8_t rx_buf[HOST_PROTO_AM32_XFER_MAX_OUT];
    int rc;

    if (in_len < 2U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    esc = in[0];
    tx_len = in[1];
    if (tx_len > HOST_PROTO_AM32_XFER_MAX_IN) {
        return HOST_STATUS_ERR_LENGTH;
    }
    if (in_len < (uint16_t)(2U + tx_len)) {
        return HOST_STATUS_ERR_LENGTH;
    }
    if (host_am32_available() == 0U) {
        return HOST_STATUS_ERR_UNSUPPORTED;
    }
    if ((s_am32.state != 1U) || (s_am32.esc != esc)) {
        return HOST_STATUS_ERR_NOT_READY;
    }

    memset(rx_buf, 0, sizeof(rx_buf));
    rc = am32_4way_xfer(esc, &in[2], tx_len, rx_buf, sizeof(rx_buf));
    if (rc < 0) {
        s_am32.state = 2U;
        s_am32.last_error = HOST_STATUS_ERR_TIMEOUT;
        LOGM_W(LOG_MOD_USB, "AM32 xfer failed (%d)", rc);
        return HOST_STATUS_ERR_TIMEOUT;
    }
    if (rc > (int)sizeof(rx_buf)) {
        rc = (int)sizeof(rx_buf);
    }
    out[0] = (uint8_t)rc;
    if (rc > 0) {
        memcpy(&out[1], rx_buf, (uint32_t)rc);
    }
    *out_len = (uint16_t)(1U + (uint16_t)rc);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_am32_end(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    uint8_t esc;
    int rc;

    (void)out;
    (void)out_len;

    if (in_len < 1U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    esc = in[0];
    if ((s_am32.state != 1U) || (s_am32.esc != esc)) {
        return HOST_STATUS_ERR_NOT_READY;
    }
    rc = am32_4way_end(esc);
    s_am32.state = 0U;
    s_am32.esc = 0U;
    if (rc != 0) {
        s_am32.last_error = HOST_STATUS_ERR_TIMEOUT;
        return HOST_STATUS_ERR_TIMEOUT;
    }
    LOGM_I(LOG_MOD_USB, "AM32 4-way bridge released ESC %u", (unsigned)esc);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_am32_status(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    if (in_len < 1U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    out[0] = s_am32.state;
    out[1] = s_am32.last_error;
    *out_len = 2U;
    return HOST_STATUS_OK;
}

/* Gating shared by the AM32 DShot programming commands */
static uint8_t host_am32_prog_check(uint8_t esc)
{
    if (host_am32_available() == 0U) {
        return HOST_STATUS_ERR_UNSUPPORTED;
    }
    if (esc >= APP_CFG_DSHOT_MOTORS) {
        return HOST_STATUS_ERR_PARAM_RANGE;
    }
    if ((g_ecat_op_active != 0U) || (g_app_state.output_enabled != 0U)) {
        return HOST_STATUS_ERR_NOT_READY;
    }
    if (s_am32.state == 1U) {
        /* the 4-way bridge owns the ESC line, do not interleave */
        return HOST_STATUS_ERR_BUSY;
    }
    if (s_bench_throttle[esc] != 0U) {
        /* AM32 programming mode needs the ESC armed but the motor stopped */
        LOGM_W(LOG_MOD_USB, "AM32 programming refused: ESC %u throttle is not zero",
               (unsigned)esc);
        return HOST_STATUS_ERR_NOT_READY;
    }
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_am32_prog_write(const uint8_t *in, uint16_t in_len,
                                        uint8_t *out, uint16_t *out_len)
{
    uint8_t esc;
    uint8_t addr;
    uint8_t value;
    uint8_t status;
    int rc;

    (void)out;
    (void)out_len;

    if (in_len < 3U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    esc = in[0];
    addr = in[1];
    value = in[2];

    status = host_am32_prog_check(esc);
    if (status != HOST_STATUS_OK) {
        return status;
    }
    if (addr > 191U) { /* AM32 EEPROM is 192 bytes */
        return HOST_STATUS_ERR_PARAM_RANGE;
    }

    rc = dshot_prog_write_byte(esc, addr, value);
    if (rc == 0) {
        s_prog_dirty[esc] = 1U;
        return HOST_STATUS_OK;
    }
    if (rc == -1) {
        /* queue full or script still running: host retries the same request */
        return HOST_STATUS_ERR_BUSY;
    }
    return HOST_STATUS_ERR_UNSUPPORTED;
}

static uint8_t host_cmd_am32_prog_save(const uint8_t *in, uint16_t in_len,
                                       uint8_t *out, uint16_t *out_len)
{
    uint8_t esc;
    uint8_t status;
    int rc;

    (void)out;
    (void)out_len;

    if (in_len < 1U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    esc = in[0];

    status = host_am32_prog_check(esc);
    if (status != HOST_STATUS_OK) {
        return status;
    }
    if (s_prog_dirty[esc] == 0U) {
        /* nothing queued since the last save: idempotent OK, do not erase/
         * program the ESC flash again just because the host polled 0x55 twice */
        return HOST_STATUS_OK;
    }
    if (dshot_prog_busy(esc) != 0U) {
        /* wait until the queued EEPROM bytes have been shifted out, then retry;
         * only the first accepted 0x55 queues the save sequence (~6 ms) */
        return HOST_STATUS_ERR_BUSY;
    }

    rc = dshot_prog_save(esc);
    if (rc == 0) {
        s_prog_dirty[esc] = 0U;
        LOGM_I(LOG_MOD_USB, "AM32 save-settings queued for ESC %u", (unsigned)esc);
        return HOST_STATUS_OK;
    }
    if (rc == -1) {
        return HOST_STATUS_ERR_BUSY;
    }
    return HOST_STATUS_ERR_UNSUPPORTED;
}

/* --- bootloader / reset -------------------------------------------------- */

static uint8_t host_cmd_bootloader(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    (void)out;
    (void)out_len;

    if (in_len < 4U) {
        return HOST_STATUS_ERR_LENGTH;
    }
    if (host_rd_u32(in) != 0x424F4F54UL) { /* 'BOOT' */
        return HOST_STATUS_ERR_PARAM_RANGE;
    }
    LOGM_W(LOG_MOD_USB, "BOOTLOADER command accepted, rebooting to DFU");
    host_request_reboot(1U);
    return HOST_STATUS_OK;
}

static uint8_t host_cmd_reset(const uint8_t *in, uint16_t in_len, uint8_t *out, uint16_t *out_len)
{
    (void)in;
    (void)in_len;
    (void)out;
    (void)out_len;

    LOGM_W(LOG_MOD_USB, "RESET command accepted");
    host_request_reboot(0U);
    return HOST_STATUS_OK;
}

/* ------------------------------------------------------------------------- */
/* Dispatcher                                                                */
/* ------------------------------------------------------------------------- */

static void host_dispatch(uint8_t cmd, uint8_t seq, const uint8_t *payload, uint16_t len)
{
    uint8_t status = HOST_STATUS_ERR_UNKNOWN_CMD;
    uint16_t out_len = 0U;

    switch (cmd) {
    case HOST_CMD_PING:
        status = host_cmd_ping(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_GET_VERSION:
        status = host_cmd_get_version(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_GET_TELEMETRY:
        status = host_cmd_get_telemetry(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_GET_ECAT_STATUS:
        status = host_cmd_get_ecat_status(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_GET_PARAM:
        status = host_cmd_get_param(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_SET_PARAM:
        status = host_cmd_set_param(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_GET_PARAM_DESC:
        status = host_cmd_get_param_desc(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_PARAM_SAVE:
        status = host_cmd_param_save(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_PARAM_RESET:
        status = host_cmd_param_reset(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_PARAM_GET_COUNT:
        status = host_cmd_param_count(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_LIST_PARAMS:
        status = host_cmd_list_params(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_DSHOT_TEST:
        status = host_cmd_dshot_test(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_DSHOT_GET_TLM:
        status = host_cmd_dshot_get_tlm(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_DSHOT_CMD:
        status = host_cmd_dshot_cmd(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_SERVO_SCAN:
        status = host_cmd_servo_scan(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_SERVO_MOVE_TEST:
        status = host_cmd_servo_move_test(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_SERVO_READ:
        status = host_cmd_servo_read(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_SERVO_SET_ID:
        status = host_cmd_servo_set_id(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_SERVO_RAW:
        status = host_cmd_servo_raw(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_ADC_GET:
        status = host_cmd_adc_get(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_LED_SET:
        status = host_cmd_led_set(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_LED_TEST:
        status = host_cmd_led_test(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_AM32_BEGIN:
        status = host_cmd_am32_begin(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_AM32_XFER:
        status = host_cmd_am32_xfer(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_AM32_END:
        status = host_cmd_am32_end(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_AM32_STATUS:
        status = host_cmd_am32_status(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_AM32_PROG_WRITE:
        status = host_cmd_am32_prog_write(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_AM32_PROG_SAVE:
        status = host_cmd_am32_prog_save(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_BOOTLOADER:
        status = host_cmd_bootloader(payload, len, s_rsp_data, &out_len);
        break;
    case HOST_CMD_RESET:
        status = host_cmd_reset(payload, len, s_rsp_data, &out_len);
        break;
    default:
        status = HOST_STATUS_ERR_UNKNOWN_CMD;
        out_len = 0U;
        break;
    }

    host_emit_response(cmd, seq, status, s_rsp_data, out_len);
}

/* ------------------------------------------------------------------------- */
/* Frame parser                                                              */
/* ------------------------------------------------------------------------- */

static void host_parser_reset(void)
{
    s_parser.state = HOST_PARSE_SYNC0;
    s_parser.cmd = 0U;
    s_parser.seq = 0U;
    s_parser.len = 0U;
    s_parser.index = 0U;
    s_parser.crc_calc = 0xFFFFU;
    s_parser.crc_recv = 0U;
    s_parser.oversize = 0U;
    s_parser.last_tick_ms = g_app_tick_ms;
}

/* Feed one received byte into the parser.  Returns 1 when a complete frame has
 * been handled (and a response queued). */
static uint8_t host_parser_feed(uint8_t byte)
{
    switch (s_parser.state) {
    case HOST_PARSE_SYNC0:
        if (byte == HOST_PROTO_SYNC0) {
            s_parser.state = HOST_PARSE_SYNC1;
        }
        break;

    case HOST_PARSE_SYNC1:
        if (byte == HOST_PROTO_SYNC1) {
            s_parser.crc_calc = 0xFFFFU;
            s_parser.state = HOST_PARSE_CMD;
        } else if (byte != HOST_PROTO_SYNC0) {
            s_parser.state = HOST_PARSE_SYNC0;
        } else {
            /* two sync0 bytes in a row: stay here, resync on the second one */
        }
        break;

    case HOST_PARSE_CMD:
        s_parser.cmd = byte;
        s_parser.crc_calc = host_crc16_byte(s_parser.crc_calc, byte);
        s_parser.state = HOST_PARSE_SEQ;
        break;

    case HOST_PARSE_SEQ:
        s_parser.seq = byte;
        s_parser.crc_calc = host_crc16_byte(s_parser.crc_calc, byte);
        s_parser.state = HOST_PARSE_LEN_L;
        break;

    case HOST_PARSE_LEN_L:
        s_parser.len = byte;
        s_parser.crc_calc = host_crc16_byte(s_parser.crc_calc, byte);
        s_parser.state = HOST_PARSE_LEN_H;
        break;

    case HOST_PARSE_LEN_H:
        s_parser.len = (uint16_t)(s_parser.len | ((uint16_t)byte << 8));
        s_parser.crc_calc = host_crc16_byte(s_parser.crc_calc, byte);
        s_parser.index = 0U;
        s_parser.oversize = (s_parser.len > APP_CFG_HOST_PROTO_MAX_PAYLOAD) ? 1U : 0U;
        s_parser.state = (s_parser.len == 0U) ? HOST_PARSE_CRC_L : HOST_PARSE_PAYLOAD;
        break;

    case HOST_PARSE_PAYLOAD:
        if (s_parser.oversize == 0U) {
            s_rx_payload[s_parser.index] = byte;
        }
        s_parser.crc_calc = host_crc16_byte(s_parser.crc_calc, byte);
        s_parser.index++;
        if (s_parser.index >= s_parser.len) {
            s_parser.state = HOST_PARSE_CRC_L;
        }
        break;

    case HOST_PARSE_CRC_L:
        s_parser.crc_recv = byte;
        s_parser.state = HOST_PARSE_CRC_H;
        break;

    case HOST_PARSE_CRC_H:
        s_parser.crc_recv = (uint16_t)(s_parser.crc_recv | ((uint16_t)byte << 8));
        s_parser.state = HOST_PARSE_SYNC0;
        if (s_parser.crc_recv != s_parser.crc_calc) {
            s_stat_crc_errors++;
            if (s_parser.oversize != 0U) {
                s_stat_len_errors++;
                host_emit_response(s_parser.cmd, s_parser.seq, HOST_STATUS_ERR_LENGTH, NULL, 0U);
            } else {
                host_emit_response(s_parser.cmd, s_parser.seq, HOST_STATUS_ERR_CRC, NULL, 0U);
            }
            return 1U;
        }
        if (s_parser.oversize != 0U) {
            s_stat_len_errors++;
            host_emit_response(s_parser.cmd, s_parser.seq, HOST_STATUS_ERR_LENGTH, NULL, 0U);
            return 1U;
        }
        s_stat_rx_frames++;
        host_dispatch(s_parser.cmd, s_parser.seq, s_rx_payload, s_parser.len);
        return 1U;

    default:
        host_parser_reset();
        break;
    }
    return 0U;
}

/* ------------------------------------------------------------------------- */
/* Public API                                                                */
/* ------------------------------------------------------------------------- */

void host_proto_app_task(void)
{
    static uint8_t chunk[HOST_PROTO_RX_CHUNK];
    static uint32_t chunk_len;
    static uint32_t chunk_pos;
    uint32_t budget = HOST_PROTO_RX_BUDGET;
    uint8_t dispatched = 0U;

    /* keep the asynchronous servo scan running */
    host_scan_poll();

    /* expire the bench override lease */
    (void)host_proto_app_bench_active();

    /* safety: the EtherCAT master took over -> never keep an AM32 programming
     * script running on the ESC lines */
    if ((g_ecat_op_active != 0U) &&
        ((dshot_prog_busy(0U) != 0U) || (dshot_prog_busy(1U) != 0U))) {
        host_am32_prog_abort_all();
    }

    /* deferred BOOTLOADER / RESET command (after the response was transmitted) */
    if ((s_reboot_pending != 0U) &&
        ((uint32_t)(g_app_tick_ms - s_reboot_tick) >= HOST_REBOOT_DELAY_MS)) {
        s_reboot_pending = 0U;
        if (s_reboot_to_dfu != 0U) {
            host_enter_dfu_imm();
        } else {
            host_system_reset_imm();
        }
    }

    /* drop a truncated frame after a timeout (host crash / unplug / noise) */
    if ((s_parser.state != HOST_PARSE_SYNC0) &&
        ((uint32_t)(g_app_tick_ms - s_parser.last_tick_ms) > HOST_PROTO_FRAME_TIMEOUT_MS)) {
        s_stat_len_errors++;
        host_parser_reset();
    }

    while (budget > 0U) {
        if (chunk_pos >= chunk_len) {
            chunk_len = usb_cdc_app_read(chunk, (uint32_t)sizeof(chunk));
            chunk_pos = 0U;
            if (chunk_len == 0U) {
                break;
            }
        }
        while ((chunk_pos < chunk_len) && (budget > 0U)) {
            uint8_t byte = chunk[chunk_pos];

            chunk_pos++;
            budget--;
            s_parser.last_tick_ms = g_app_tick_ms;
            if (host_parser_feed(byte) != 0U) {
                dispatched++;
                if (dispatched >= HOST_PROTO_MAX_DISPATCH) {
                    return;
                }
            }
        }
    }
}

void host_proto_app_get_stats(uint32_t *rx_frames, uint32_t *crc_errors)
{
    if (rx_frames != NULL) {
        *rx_frames = s_stat_rx_frames;
    }
    if (crc_errors != NULL) {
        *crc_errors = s_stat_crc_errors + s_stat_len_errors;
    }
}

