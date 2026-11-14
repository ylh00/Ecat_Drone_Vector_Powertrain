/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Runtime parameter system
 *
 * The parameter table is a flat, order independent list of parameter
 * descriptors (one entry per param_id_t value, addressed with designated
 * initializers so the table can never get out of sync with the enum).  The
 * current values live in a RAM array; param_apply() pushes the values that the
 * peripheral modules cache into those modules.
 *
 * Persistent storage
 * ------------------
 * The parameter block is stored in the last 4 KB sector of the application
 * flash area (PARAM_FLASH_OFFSET, mapped at BOARD_FLASH_BASE_ADDRESS +
 * PARAM_FLASH_OFFSET for flash_xip builds):
 *
 *   offset  0 : u32 magic (PARAM_FLASH_MAGIC)
 *   offset  4 : u16 version (PARAM_FLASH_VERSION)
 *   offset  6 : u16 count (PARAM_COUNT)
 *   offset  8 : u32 reserved
 *   offset 12 : u32 pad (keeps the value array 4 byte aligned)
 *   offset 16 : i32 values[PARAM_COUNT]
 *   offset 16 + 4*PARAM_COUNT : u16 crc16 over all preceding bytes
 *
 * The block is written with erase-then-program using the HPM SDK ROM API XPI
 * NOR driver, see
 *   hpm_sdk/samples/rom_api/xpi_nor_api/src/xpi_nor_api_demo.c
 *   hpm_sdk/soc/HPM5E00/HPM5E31/hpm_romapi.h (rom_xpi_nor_auto_config /
 *   rom_xpi_nor_erase_sector / rom_xpi_nor_program / rom_xpi_nor_read)
 *
 * A corrupted, blank or older-version block is detected with magic, version,
 * count and CRC checks; in that case the defaults from app_config.h are used
 * and the block is left untouched (the host tool can then Save to repair it).
 *
 * The application image must end below PARAM_FLASH_OFFSET (0x7E000 = 504 KB);
 * the EtherCAT EEPROM emulation uses 0x80000 and must not be touched either.
 *
 * NOTE: erase/program interrupt the XIP fetch of the running code for a few
 * milliseconds, therefore interrupts are disabled around the flash operation
 * and param_save() must only be called when the machine is safe to stall
 * (host tool command, motors stopped).
 */

#include <stdbool.h>
#include <stddef.h>
#include <string.h>

#include "board.h"
#include "app_config.h"
#include "app_log.h"
#include "param.h"
#include "host_proto.h"

/* Module headers whose cached settings are refreshed by param_apply() */
#include "dshot.h"
#include "servo_s288.h"
#include "led_rgb.h"
#include "adc_mon.h"
#include "ecat_app.h"

/* ROM API for the XPI NOR flash driver */
#include "hpm_romapi.h"

/* ------------------------------------------------------------------------- */
/* Parameter table                                                           */
/* ------------------------------------------------------------------------- */

/* Defaults derived from app_config.h / board.h where such a macro exists. */
#define PARAM_DEF_ADC_SYS_V_SCALE (APP_CFG_ADC_SYS_V_MV_PER_LSB_NUM * 1000 / APP_CFG_ADC_SYS_V_MV_PER_LSB_DEN)
#define PARAM_DEF_ADC_SYS_I_SCALE (APP_CFG_ADC_SYS_I_MA_PER_LSB_NUM * 1000 / APP_CFG_ADC_SYS_I_MA_PER_LSB_DEN)
#define PARAM_DEF_ADC_ESC_I_SCALE (APP_CFG_ADC_ESC_I_MA_PER_LSB_NUM * 1000 / APP_CFG_ADC_ESC_I_MA_PER_LSB_DEN)

static const param_desc_t s_param_desc[PARAM_COUNT] = {
    /* ---- EtherCAT slave settings ---------------------------------------- */
    [PARAM_ECAT_NODE_ALIAS] = {
        "ecat_node_alias", PARAM_TYPE_U16, 0, 65535, 0, 1, 0,
        "EtherCAT station alias (0 = from EEPROM)" },
    [PARAM_ECAT_DC_MODE] = {
        "ecat_dc_mode", PARAM_TYPE_U8, 0, 3, 0, 1, 0,
        "DC mode: 0=off 1=SYNC0 2=SYNC1 3=DC SYNC0" },
    [PARAM_ECAT_SYNC0_PERIOD_US] = {
        "ecat_sync0_us", PARAM_TYPE_U32, 125, 10000000, APP_CFG_ECAT_SYNC0_PERIOD_US_DEFAULT, 1, 0,
        "SYNC0 period in us (DC mode >= 1)" },
    [PARAM_ECAT_SYNC1_PERIOD_US] = {
        "ecat_sync1_us", PARAM_TYPE_U32, 125, 10000000, APP_CFG_ECAT_SYNC0_PERIOD_US_DEFAULT, 1, 0,
        "SYNC1 period in us (DC mode 2/3)" },
    [PARAM_ECAT_DC_SHIFT_NS] = {
        "ecat_dc_shift", PARAM_TYPE_I32, -1000000, 1000000, 0, 1, 0,
        "DC SYNC0 shift in ns" },
    [PARAM_ECAT_WATCHDOG_MS] = {
        "ecat_wdg_ms", PARAM_TYPE_U16, 10, 1000, APP_CFG_ECAT_WATCHDOG_MS_DEFAULT, 1, 0,
        "master process data watchdog in ms" },
    [PARAM_ECAT_SDO_TIMEOUT_MS] = {
        "ecat_sdo_to_ms", PARAM_TYPE_U16, 100, 60000, 1000, 1, 0,
        "CoE SDO timeout in ms" },
    [PARAM_ECAT_ENABLE_COE] = {
        "ecat_enable_coe", PARAM_TYPE_U8, 0, 1, 1, 1, 0,
        "enable the CoE mailbox (0/1)" },
    [PARAM_ECAT_LED_INVERT] = {
        "ecat_led_invert", PARAM_TYPE_U8, 0, 1, 0, 1, 0,
        "invert the RUN/ERR LED mapping (0/1)" },

    /* ---- DShot / ESC ----------------------------------------------------- */
    [PARAM_DSHOT_RATE] = {
        "dshot_rate", PARAM_TYPE_U16, 150, 1200, BOARD_DSHOT_RATE_DEFAULT, 1, 0,
        "DShot rate: 150/300/600/1200" },
    [PARAM_DSHOT_UPDATE_HZ] = {
        "dshot_update_hz", PARAM_TYPE_U16, 200, 8000, APP_CFG_DSHOT_UPDATE_HZ, 1, 0,
        "DShot throttle update rate in Hz" },
    [PARAM_DSHOT_POLE_PAIRS] = {
        "dshot_poles", PARAM_TYPE_U16, 1, 32, BOARD_DSHOT_MOTOR_POLE_PAIRS_DEFAULT, 1, 0,
        "motor pole pairs (eRPM -> RPM)" },
    [PARAM_DSHOT_DIRECTION1] = {
        "dshot_dir1", PARAM_TYPE_U8, 0, 1, 0, 1, 0,
        "ESC1 direction: 0=normal 1=reversed" },
    [PARAM_DSHOT_DIRECTION2] = {
        "dshot_dir2", PARAM_TYPE_U8, 0, 1, 0, 1, 0,
        "ESC2 direction: 0=normal 1=reversed" },
    [PARAM_DSHOT_3D_MODE1] = {
        "dshot_3d1", PARAM_TYPE_U8, 0, 1, 0, 1, 0,
        "ESC1 3D mode enable (0/1)" },
    [PARAM_DSHOT_3D_MODE2] = {
        "dshot_3d2", PARAM_TYPE_U8, 0, 1, 0, 1, 0,
        "ESC2 3D mode enable (0/1)" },
    [PARAM_DSHOT_TLM_TIMEOUT_US] = {
        "dshot_tlm_to_us", PARAM_TYPE_U16, 20, 200, APP_CFG_DSHOT_TLM_TIMEOUT_US, 1, 0,
        "ESC telemetry gap timeout in us" },
    [PARAM_DSHOT_MIN_THROTTLE] = {
        "dshot_min_thr", PARAM_TYPE_U16, 0, 200, APP_CFG_DSHOT_MIN_THROTTLE, 1, 0,
        "DShot throttle value for 1000 us" },
    [PARAM_DSHOT_MAX_THROTTLE] = {
        "dshot_max_thr", PARAM_TYPE_U16, 1800, 2047, APP_CFG_DSHOT_MAX_THROTTLE, 1, 0,
        "DShot throttle value for 2000 us" },
    [PARAM_DSHOT_START_US] = {
        "dshot_start_us", PARAM_TYPE_U16, 800, 1200, 1000, 1, 0,
        "throttle command sent while arming (us)" },

    /* ---- S288 bus servo -------------------------------------------------- */
    [PARAM_SERVO_BAUD] = {
        "servo_baud", PARAM_TYPE_U32, 9600, 6000000, APP_CFG_SERVO_DEFAULT_BAUD, 1, 0,
        "S288 bus baud rate (J288/S288: 6000000)" },
    [PARAM_SERVO_ID1] = {
        "servo_id1", PARAM_TYPE_U8, 0, 253, 1, 1, 0,
        "servo 1 (UART2) S288 id" },
    [PARAM_SERVO_ID2] = {
        "servo_id2", PARAM_TYPE_U8, 0, 253, 1, 1, 0,
        "servo 2 (UART3) S288 id" },
    [PARAM_SERVO_DIR_TX_LEVEL1] = {
        "servo_tx_lvl1", PARAM_TYPE_U8, 0, 1, BOARD_SERVO_DIR_TX_LEVEL_DEFAULT, 1, 0,
        "servo 1 direction switch TX level (0/1)" },
    [PARAM_SERVO_DIR_TX_LEVEL2] = {
        "servo_tx_lvl2", PARAM_TYPE_U8, 0, 1, BOARD_SERVO_DIR_TX_LEVEL_DEFAULT, 1, 0,
        "servo 2 direction switch TX level (0/1)" },
    [PARAM_SERVO_ANGLE_MIN1] = {
        "servo_min1", PARAM_TYPE_I16, -36000, 36000, APP_CFG_SERVO_ANGLE_MIN_D10, 1, 0,
        "servo 1 minimum angle, 0.1 deg" },
    [PARAM_SERVO_ANGLE_MIN2] = {
        "servo_min2", PARAM_TYPE_I16, -36000, 36000, APP_CFG_SERVO_ANGLE_MIN_D10, 1, 0,
        "servo 2 minimum angle, 0.1 deg" },
    [PARAM_SERVO_ANGLE_MAX1] = {
        "servo_max1", PARAM_TYPE_I16, -36000, 36000, APP_CFG_SERVO_ANGLE_MAX_D10, 1, 0,
        "servo 1 maximum angle, 0.1 deg" },
    [PARAM_SERVO_ANGLE_MAX2] = {
        "servo_max2", PARAM_TYPE_I16, -36000, 36000, APP_CFG_SERVO_ANGLE_MAX_D10, 1, 0,
        "servo 2 maximum angle, 0.1 deg" },
    [PARAM_SERVO_ANGLE_GAIN1] = {
        "servo_gain1", PARAM_TYPE_I16, 10, 10000, 100, 1, 0,
        "servo 1 angle gain (100 = 1.00x)" },
    [PARAM_SERVO_ANGLE_GAIN2] = {
        "servo_gain2", PARAM_TYPE_I16, 10, 10000, 100, 1, 0,
        "servo 2 angle gain (100 = 1.00x)" },
    [PARAM_SERVO_ANGLE_OFS1] = {
        "servo_ofs1", PARAM_TYPE_I16, -18000, 18000, 0, 1, 0,
        "servo 1 angle offset, 0.1 deg" },
    [PARAM_SERVO_ANGLE_OFS2] = {
        "servo_ofs2", PARAM_TYPE_I16, -18000, 18000, 0, 1, 0,
        "servo 2 angle offset, 0.1 deg" },
    [PARAM_SERVO_POLL_HZ] = {
        "servo_poll_hz", PARAM_TYPE_U16, 10, 1000, APP_CFG_SERVO_POLL_HZ, 1, 0,
        "bus servo polling rate in Hz" },
    [PARAM_SERVO_TORQUE_LIMIT1] = {
        "servo_torque1", PARAM_TYPE_U8, 0, 100, 100, 1, 0,
        "servo 1 torque limit in percent" },
    [PARAM_SERVO_TORQUE_LIMIT2] = {
        "servo_torque2", PARAM_TYPE_U8, 0, 100, 100, 1, 0,
        "servo 2 torque limit in percent" },
    [PARAM_SERVO_ENABLE_ON_BOOT] = {
        "servo_en_boot", PARAM_TYPE_U8, 0, 1, 0, 1, 0,
        "enable servo torque at boot (0/1)" },

    /* ---- ADC monitoring -------------------------------------------------- */
    [PARAM_ADC_SYS_V_SCALE] = {
        "adc_v_scale", PARAM_TYPE_I32, 1, 1000000, PARAM_DEF_ADC_SYS_V_SCALE, 1, 0,
        "system voltage scale in uV per ADC LSB" },
    [PARAM_ADC_SYS_V_OFFSET_MV] = {
        "adc_v_ofs_mv", PARAM_TYPE_I32, -100000, 100000, 0, 1, 0,
        "system voltage offset in mV" },
    [PARAM_ADC_SYS_I_SCALE] = {
        "adc_i_scale", PARAM_TYPE_I32, 1, 1000000, PARAM_DEF_ADC_SYS_I_SCALE, 1, 0,
        "system current scale in uA per ADC LSB" },
    [PARAM_ADC_SYS_I_OFFSET_MA] = {
        "adc_i_ofs_ma", PARAM_TYPE_I32, -100000, 100000, 0, 1, 0,
        "system current offset in mA" },
    [PARAM_ADC_ESC1_I_SCALE] = {
        "adc_esc1_scale", PARAM_TYPE_I32, 1, 1000000, PARAM_DEF_ADC_ESC_I_SCALE, 1, 0,
        "ESC1 current scale in uA per ADC LSB" },
    [PARAM_ADC_ESC2_I_SCALE] = {
        "adc_esc2_scale", PARAM_TYPE_I32, 1, 1000000, PARAM_DEF_ADC_ESC_I_SCALE, 1, 0,
        "ESC2 current scale in uA per ADC LSB" },
    [PARAM_ADC_ESC_I_OFFSET_MA] = {
        "adc_esc_ofs_ma", PARAM_TYPE_I32, -100000, 100000, 0, 1, 0,
        "ESC current offset in mA" },
    [PARAM_ADC_VOLT_MIN_DV] = {
        "adc_volt_min_dv", PARAM_TYPE_U16, 0, 10000, 0, 1, 0,
        "under voltage trip, 0.1 V (0 = disabled)" },
    [PARAM_ADC_VOLT_MAX_DV] = {
        "adc_volt_max_dv", PARAM_TYPE_U16, 0, 10000, 0, 1, 0,
        "over voltage trip, 0.1 V (0 = disabled)" },
    [PARAM_ADC_CURR_MAX_CA] = {
        "adc_curr_max_ca", PARAM_TYPE_I16, 0, 32000, 0, 1, 0,
        "system over current trip, 0.01 A (0 = off)" },
    [PARAM_ADC_ESC_CURR_MAX_MA] = {
        "adc_esc_imax_ma", PARAM_TYPE_U16, 0, 65535, 0, 1, 0,
        "per ESC over current trip in mA (0 = off)" },
    [PARAM_ADC_ENABLE_PROTECTION] = {
        "adc_protect_en", PARAM_TYPE_U8, 0, 1, 0, 1, 0,
        "enable ADC based protection (0=off until calibrated)" },

    /* ---- RGB status LED -------------------------------------------------- */
    [PARAM_LED_BRIGHTNESS] = {
        "led_brightness", PARAM_TYPE_U16, 0, 255, APP_CFG_LED_BRIGHTNESS_DEFAULT, 1, 0,
        "RGB status LED brightness 0..255" },
    [PARAM_LED_STATE_MASK] = {
        "led_state_mask", PARAM_TYPE_U32, 0, 0xFFFF, 0xFFFF, 1, 0,
        "bitmask of LED states that may be shown" },

    /* ---- USB / host tool ------------------------------------------------- */
    [PARAM_USB_PROTO_TIMEOUT_MS] = {
        "usb_proto_to_ms", PARAM_TYPE_U16, 100, 10000, 1000, 1, 0,
        "host command / bench lease timeout in ms" },
    [PARAM_USB_AUTO_TELEMETRY_HZ] = {
        "usb_auto_tlm_hz", PARAM_TYPE_U16, 0, 1000, 0, 0, 0,
        "auto telemetry rate in Hz (0 = on request)" },

    /* ---- AM32 ESC tuning ------------------------------------------------- */
    [PARAM_AM32_4WAY_NUM_RETRIES] = {
        "am32_retries", PARAM_TYPE_U8, 0, 10, 3, 1, 0,
        "AM32 4-way bridge retry count" },
    [PARAM_AM32_4WAY_BAUD] = {
        "am32_baud", PARAM_TYPE_U32, 9600, 1000000, 19200, 1, 0,
        "AM32 4-way bit-bang baud rate" },
    [PARAM_AM32_ENABLE_PASSTHROUGH] = {
        "am32_passthru", PARAM_TYPE_U8, 0, 1, 1, 1, 0,
        "allow the PC to access AM32 through the board" },

    /* ---- S288 position / speed loop gains -------------------------------- */
    [PARAM_SERVO_KP1] = {
        "servo_kp1", PARAM_TYPE_I32, 0, 100000000, 25600000, 1, 0,
        "servo 1 position gain (1280000 = 1.0)" },
    [PARAM_SERVO_KP2] = {
        "servo_kp2", PARAM_TYPE_I32, 0, 100000000, 25600000, 1, 0,
        "servo 2 position gain (1280000 = 1.0)" },
    [PARAM_SERVO_KD1] = {
        "servo_kd1", PARAM_TYPE_I32, 0, 1000000000, 128000000, 1, 0,
        "servo 1 speed gain (128000000 = 1.0)" },
    [PARAM_SERVO_KD2] = {
        "servo_kd2", PARAM_TYPE_I32, 0, 1000000000, 128000000, 1, 0,
        "servo 2 speed gain (128000000 = 1.0)" },
};

/* Compile time consistency checks: the table must have exactly one entry per
 * enum value (designated initializers) and the per channel id stretches must
 * stay adjacent, otherwise PARAM_xxx(ch) arithmetic breaks. */
_Static_assert(APP_ARRAY_SIZE(s_param_desc) == (uint32_t)PARAM_COUNT,
               "parameter table out of sync with param_id_t");
_Static_assert(PARAM_DSHOT_DIRECTION1 + 1 == PARAM_DSHOT_DIRECTION2, "array param mismatch");
_Static_assert(PARAM_DSHOT_3D_MODE1 + 1 == PARAM_DSHOT_3D_MODE2, "array param mismatch");
_Static_assert(PARAM_SERVO_ID1 + 1 == PARAM_SERVO_ID2, "array param mismatch");
_Static_assert(PARAM_SERVO_DIR_TX_LEVEL1 + 1 == PARAM_SERVO_DIR_TX_LEVEL2, "array param mismatch");
_Static_assert(PARAM_SERVO_ANGLE_MIN1 + 1 == PARAM_SERVO_ANGLE_MIN2, "array param mismatch");
_Static_assert(PARAM_SERVO_ANGLE_MAX1 + 1 == PARAM_SERVO_ANGLE_MAX2, "array param mismatch");
_Static_assert(PARAM_SERVO_ANGLE_GAIN1 + 1 == PARAM_SERVO_ANGLE_GAIN2, "array param mismatch");
_Static_assert(PARAM_SERVO_ANGLE_OFS1 + 1 == PARAM_SERVO_ANGLE_OFS2, "array param mismatch");
_Static_assert(PARAM_SERVO_TORQUE_LIMIT1 + 1 == PARAM_SERVO_TORQUE_LIMIT2, "array param mismatch");
_Static_assert(PARAM_SERVO_KP1 + 1 == PARAM_SERVO_KP2, "array param mismatch");
_Static_assert(PARAM_SERVO_KD1 + 1 == PARAM_SERVO_KD2, "array param mismatch");
_Static_assert(PARAM_SERVO_ID1 == PARAM_SERVO_ID(0), "array param macro mismatch");
_Static_assert(PARAM_SERVO_ANGLE_MAX(1) == PARAM_SERVO_ANGLE_MAX2, "array param macro mismatch");
_Static_assert(PARAM_SERVO_KP(1) == PARAM_SERVO_KP2, "array param macro mismatch");
_Static_assert(PARAM_SERVO_KD(1) == PARAM_SERVO_KD2, "array param macro mismatch");

/* ------------------------------------------------------------------------- */
/* Persistent storage layout                                                 */
/* ------------------------------------------------------------------------- */

/* NOTE: not __packed - the natural alignment (4) keeps every field word
 * aligned for the ROM API flash write, and the field layout is fixed by the
 * explicit pad field.
 *
 * The blob is exactly one 256 byte NOR flash page: header 16 bytes + one i32
 * per parameter + CRC inside the header.  The CRC field itself is set to
 * 0xFFFF while the CRC is calculated over the complete blob. */
typedef struct {
    uint32_t magic;                    /* offset  0: PARAM_FLASH_MAGIC          */
    uint16_t version;                  /* offset  4: PARAM_FLASH_VERSION        */
    uint16_t count;                    /* offset  6: PARAM_COUNT                */
    uint16_t crc;                      /* offset  8: CRC16 of the whole blob    */
    uint16_t hdr_reserved;             /* offset 10                             */
    uint32_t pad;                      /* offset 12: keeps values aligned       */
    int32_t  values[PARAM_COUNT];      /* offset 16                             */
} param_flash_blob_t;

_Static_assert(offsetof(param_flash_blob_t, values) == PARAM_HEADER_SIZE,
               "param flash header size mismatch");
_Static_assert((offsetof(param_flash_blob_t, crc) & 1U) == 0U,
               "param flash CRC must be 2 byte aligned");
_Static_assert(sizeof(param_flash_blob_t) == 256U,
               "param flash blob must be exactly one 256 byte NOR flash page");
_Static_assert(sizeof(param_flash_blob_t) <= PARAM_FLASH_SIZE,
               "param flash blob does not fit into the parameter sector");

#define PARAM_FLASH_XIP_ADDR ((uint32_t)(BOARD_FLASH_BASE_ADDRESS + PARAM_FLASH_OFFSET))

/* ------------------------------------------------------------------------- */
/* State                                                                     */
/* ------------------------------------------------------------------------- */

static int32_t s_param_value[PARAM_COUNT];

/* Cached XPI NOR configuration, configured on the first param_save() */
static xpi_nor_config_t s_xpi_nor_config;
static bool s_xpi_nor_config_valid;

/* Statistics: number of successful / failed flash loads and saves */
static uint32_t s_flash_load_count;
static uint32_t s_flash_error_count;

/* ------------------------------------------------------------------------- */
/* Helpers                                                                   */
/* ------------------------------------------------------------------------- */

static const param_desc_t *param_desc_of(int32_t id)
{
    if ((id < 0) || (id >= (int32_t)PARAM_COUNT)) {
        return NULL;
    }
    return &s_param_desc[id];
}

/* Check that a value is representable in the descriptor's C type.
 * Returns PARAM_OK or PARAM_ERR_TYPE. */
static int param_check_type(const param_desc_t *desc, int32_t value)
{
    switch (desc->type) {
    case PARAM_TYPE_U8:
        if ((value < 0) || (value > 0xFF)) {
            return PARAM_ERR_TYPE;
        }
        break;
    case PARAM_TYPE_U16:
        if ((value < 0) || (value > 0xFFFF)) {
            return PARAM_ERR_TYPE;
        }
        break;
    case PARAM_TYPE_U32:
        if (value < 0) {
            return PARAM_ERR_TYPE;
        }
        break;
    case PARAM_TYPE_I16:
        if ((value < (int32_t)INT16_MIN) || (value > (int32_t)INT16_MAX)) {
            return PARAM_ERR_TYPE;
        }
        break;
    case PARAM_TYPE_I32:
        break;
    default:
        return PARAM_ERR_TYPE;
    }
    return PARAM_OK;
}

/* Validate a value for one parameter (type, range and parameter specific
 * constraints).  Returns PARAM_OK when the value can be stored. */
static int param_check_value(int32_t id, int32_t value)
{
    const param_desc_t *desc = param_desc_of(id);
    int rc;

    if (desc == NULL) {
        return PARAM_ERR_ID;
    }
    rc = param_check_type(desc, value);
    if (rc != PARAM_OK) {
        return rc;
    }
    if ((value < desc->min) || (value > desc->max)) {
        return PARAM_ERR_RANGE;
    }
    if (id == (int32_t)PARAM_DSHOT_RATE) {
        if ((value != 150) && (value != 300) && (value != 600) && (value != 1200)) {
            return PARAM_ERR_RANGE;
        }
    }
    return PARAM_OK;
}

/* Keep a value inside the descriptor range (used when loading flash data). */
static int32_t param_clamp(const param_desc_t *desc, int32_t value)
{
    if (param_check_type(desc, value) != PARAM_OK) {
        return desc->def;
    }
    if (value < desc->min) {
        return desc->min;
    }
    if (value > desc->max) {
        return desc->max;
    }
    return value;
}

/* ------------------------------------------------------------------------- */
/* Flash access (SDK ROM API XPI NOR driver)                                 */
/* ------------------------------------------------------------------------- */

/* Read the raw parameter block from flash into blob (direct XIP read, the
 * flash is mapped at BOARD_FLASH_BASE_ADDRESS). */
static void param_flash_read_raw(param_flash_blob_t *blob)
{
    memcpy(blob, (const void *)PARAM_FLASH_XIP_ADDR, sizeof(*blob));
}

/* CRC16-CCITT-FALSE over the complete blob with the CRC field replaced by the
 * 0xFFFF placeholder (so the CRC can be calculated before it is stored). */
static uint16_t param_blob_crc(const param_flash_blob_t *blob)
{
    param_flash_blob_t tmp;

    memcpy(&tmp, blob, sizeof(tmp));
    tmp.crc = 0xFFFFU;
    return host_proto_crc16((const uint8_t *)&tmp, (uint32_t)sizeof(tmp));
}

/* Verify magic/version/count/CRC of a block.  Returns PARAM_OK if usable. */
static int param_flash_verify(const param_flash_blob_t *blob)
{
    if (blob->magic != PARAM_FLASH_MAGIC) {
        return PARAM_ERR_FLASH;
    }
    if (blob->version != PARAM_FLASH_VERSION) {
        return PARAM_ERR_FLASH;
    }
    if (blob->count != (uint16_t)PARAM_COUNT) {
        return PARAM_ERR_FLASH;
    }
    if (param_blob_crc(blob) != blob->crc) {
        return PARAM_ERR_FLASH;
    }
    return PARAM_OK;
}

/*
 * Erase the parameter sector and program + verify the parameter block.
 *
 * The HPM SDK ROM API takes care of switching the XPI controller between XIP
 * and APB access; interrupts are disabled because no code may be fetched from
 * the flash while it is erased/programmed.
 */
static int param_flash_store(const param_flash_blob_t *blob)
{
    xpi_nor_config_option_t option;
    xpi_xfer_channel_t channel = xpi_xfer_channel_auto;
    XPI_Type *base = BOARD_APP_XPI_NOR_XPI_BASE;
    hpm_stat_t status;
    uint32_t irq_state;
    int rc = PARAM_OK;

    option.header.U = BOARD_APP_XPI_NOR_CFG_OPT_HDR;
    option.option0.U = BOARD_APP_XPI_NOR_CFG_OPT_OPT0;
    option.option1.U = BOARD_APP_XPI_NOR_CFG_OPT_OPT1;

    irq_state = disable_global_irq(CSR_MSTATUS_MIE_MASK);

    if (!s_xpi_nor_config_valid) {
        status = rom_xpi_nor_auto_config(base, &s_xpi_nor_config, &option);
        if (status != status_success) {
            restore_global_irq(irq_state);
            return PARAM_ERR_FLASH;
        }
        s_xpi_nor_config_valid = true;
    }

    /* one sector erase: 4 KB @ PARAM_FLASH_OFFSET */
    status = rom_xpi_nor_erase(base, channel, &s_xpi_nor_config,
                               PARAM_FLASH_OFFSET, PARAM_FLASH_SIZE);
    if (status == status_success) {
        status = rom_xpi_nor_program(base, channel, &s_xpi_nor_config,
                                     (const uint32_t *)blob,
                                     PARAM_FLASH_OFFSET, (uint32_t)sizeof(*blob));
    }

    restore_global_irq(irq_state);
    fencei();

    if (status != status_success) {
        s_flash_error_count++;
        return PARAM_ERR_FLASH;
    }

    /* read back and verify */
    {
        param_flash_blob_t check;

        memset(&check, 0xFF, sizeof(check));
        status = rom_xpi_nor_read(base, channel, &s_xpi_nor_config,
                                  (uint32_t *)&check, PARAM_FLASH_OFFSET,
                                  (uint32_t)sizeof(check));
        if ((status != status_success) || (memcmp(&check, blob, sizeof(check)) != 0) ||
            (param_flash_verify(&check) != PARAM_OK)) {
            s_flash_error_count++;
            rc = PARAM_ERR_FLASH;
        }
    }

    return rc;
}

/* ------------------------------------------------------------------------- */
/* Public API                                                                */
/* ------------------------------------------------------------------------- */

void param_init(void)
{
    uint32_t i;
    int rc;

    /* runtime defaults first, flash values are applied on top */
    param_reset_defaults();

    /* sanity check the descriptor table (catches a missing designated
     * initializer / a truncated name at runtime) */
    for (i = 0; i < (uint32_t)PARAM_COUNT; i++) {
        if ((s_param_desc[i].name == NULL) || (s_param_desc[i].name[0] == '\0') ||
            (s_param_desc[i].desc == NULL)) {
            LOGM_E(LOG_MOD_PARAM, "parameter %u has no descriptor", (unsigned)i);
        }
    }

    rc = param_load();
    if (rc == PARAM_OK) {
        LOGM_I(LOG_MOD_PARAM, "%u parameters loaded from flash (data version %u)",
               (unsigned)PARAM_COUNT, (unsigned)PARAM_FLASH_VERSION);
    } else {
        LOGM_W(LOG_MOD_PARAM, "no valid parameter block in flash, using defaults");
    }
    LOGM_I(LOG_MOD_PARAM, "flash sector: offset 0x%06X size %u, blob %u bytes",
           (unsigned)PARAM_FLASH_OFFSET, (unsigned)PARAM_FLASH_SIZE,
           (unsigned)sizeof(param_flash_blob_t));
}

int param_load(void)
{
    param_flash_blob_t blob;
    uint32_t i;

    param_flash_read_raw(&blob);
    if (param_flash_verify(&blob) != PARAM_OK) {
        s_flash_error_count++;
        return PARAM_ERR_FLASH;
    }

    for (i = 0; i < (uint32_t)PARAM_COUNT; i++) {
        if (s_param_desc[i].persist != 0U) {
            s_param_value[i] = param_clamp(&s_param_desc[i], blob.values[i]);
        } else {
            s_param_value[i] = s_param_desc[i].def;
        }
    }
    s_flash_load_count++;
    return PARAM_OK;
}

int param_save(void)
{
    param_flash_blob_t blob;
    uint32_t i;

    memset(&blob, 0xFF, sizeof(blob));
    blob.magic = PARAM_FLASH_MAGIC;
    blob.version = PARAM_FLASH_VERSION;
    blob.count = (uint16_t)PARAM_COUNT;
    blob.hdr_reserved = 0U;
    blob.pad = 0U;
    blob.crc = 0xFFFFU;
    for (i = 0; i < (uint32_t)PARAM_COUNT; i++) {
        blob.values[i] = s_param_value[i];
    }
    blob.crc = param_blob_crc(&blob);
    return param_flash_store(&blob);
}

void param_reset_defaults(void)
{
    uint32_t i;

    for (i = 0; i < (uint32_t)PARAM_COUNT; i++) {
        s_param_value[i] = s_param_desc[i].def;
    }
}

int32_t param_get(param_id_t id)
{
    if ((int32_t)id < 0 || (int32_t)id >= (int32_t)PARAM_COUNT) {
        return 0;
    }
    return s_param_value[(int32_t)id];
}

int param_set(param_id_t id, int32_t value)
{
    const param_desc_t *desc = param_desc_of((int32_t)id);
    int rc;

    if (desc == NULL) {
        return PARAM_ERR_ID;
    }
    if (desc->readonly != 0U) {
        return PARAM_ERR_READONLY;
    }
    rc = param_check_value((int32_t)id, value);
    if (rc != PARAM_OK) {
        return rc;
    }
    s_param_value[(int32_t)id] = value;
    return PARAM_OK;
}

const param_desc_t *param_get_desc(param_id_t id)
{
    return param_desc_of((int32_t)id);
}

uint16_t param_count(void)
{
    return (uint16_t)PARAM_COUNT;
}

void param_apply(void)
{
#if APP_CFG_ENABLE_DSHOT
    dshot_set_rate((uint16_t)param_get(PARAM_DSHOT_RATE));
    dshot_set_tlm_timeout_us((uint16_t)param_get(PARAM_DSHOT_TLM_TIMEOUT_US));
#endif
#if APP_CFG_ENABLE_SERVO
    servo_set_baudrate((uint32_t)param_get(PARAM_SERVO_BAUD));
#endif
#if APP_CFG_ENABLE_RGB_LED
    led_set_brightness((uint8_t)param_get(PARAM_LED_BRIGHTNESS));
#endif
#if APP_CFG_ENABLE_ADC_MON
    adc_mon_apply_params();
#endif
#if APP_CFG_ENABLE_ECAT
    ecat_app_apply_params();
#endif
    LOGM_D(LOG_MOD_PARAM, "parameters applied to modules");
}
