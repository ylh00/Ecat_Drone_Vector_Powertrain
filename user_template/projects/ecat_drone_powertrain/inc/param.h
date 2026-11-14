/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Runtime parameter system
 *
 * All tunable values (EtherCAT settings, DShot settings, servo settings, ADC
 * scaling, LED brightness, ...) live in one table.  The table is shared by:
 *   - the EtherCAT slave stack (applied when entering OP / on demand)
 *   - the USB CDC host protocol (PC tuning tool)
 *   - the control task
 *
 * Storage: internal flash, one 4KB sector dedicated to parameters (see
 * PARAM_FLASH_OFFSET).  The table is guarded by a magic + CRC.
 *
 * Array parameters (per motor / per servo) are declared as consecutive ids,
 * use PARAM_xxx(ch) macros to address one element.
 */

#ifndef PARAM_H
#define PARAM_H

#include <stdint.h>
#include "app_config.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Flash offset of the parameter sector (inside the application region,
 * keep it outside the EtherCAT EEPROM emulation area at 0x80000).
 *
 * 0x7E000..0x7EFFF is one 4 KB sector of the internal 1 MB XPI NOR flash,
 * starting at the XIP address 0x8007E000.  Only a single small blob (header +
 * PARAM_COUNT * 4 bytes values + CRC, ~250 bytes) is stored, so the erase /
 * program wear is spread over the whole sector by the flash controller but the
 * sector must still not be written more often than needed (each param_save()
 * costs one sector erase).  The application image must stay below 0x7E000
 * (504 KB) - see the link time check in the linker script and docs/USB_PROTOCOL.md. */
#define PARAM_FLASH_OFFSET        (0x7E000U)
#define PARAM_FLASH_SIZE          (0x1000U)
#define PARAM_FLASH_MAGIC         (0x41524950U) /* 'PARI' + 'A' tag            */
#define PARAM_FLASH_VERSION       (1U)          /* bump when the table changes */
#define PARAM_HEADER_SIZE         (16U)

typedef enum {
    PARAM_TYPE_U8 = 0,
    PARAM_TYPE_U16,
    PARAM_TYPE_U32,
    PARAM_TYPE_I16,
    PARAM_TYPE_I32,
    PARAM_TYPE_STR  /* fixed 16 byte string, unused for numeric parameters */
} param_type_t;

typedef enum {
    /* ---- EtherCAT slave settings ---------------------------------------- */
    PARAM_ECAT_NODE_ALIAS = 0,   /* u16   0..65535   station alias             */
    PARAM_ECAT_DC_MODE,          /* u8    0=off, 1=SYNC0, 2=SYNC1, 3=DC SYNC0  */
    PARAM_ECAT_SYNC0_PERIOD_US,  /* u32   125..10000000                        */
    PARAM_ECAT_SYNC1_PERIOD_US,  /* u32   125..10000000                        */
    PARAM_ECAT_DC_SHIFT_NS,      /* i32   -1000000..1000000                    */
    PARAM_ECAT_WATCHDOG_MS,      /* u16   10..1000  master data watchdog        */
    PARAM_ECAT_SDO_TIMEOUT_MS,   /* u16   100..60000                           */
    PARAM_ECAT_ENABLE_COE,       /* u8    0/1                                   */
    PARAM_ECAT_LED_INVERT,       /* u8    0/1  RGB status LED invert            */

    /* ---- DShot / ESC ----------------------------------------------------- */
    PARAM_DSHOT_RATE,            /* u16   150/300/600/1200                      */
    PARAM_DSHOT_UPDATE_HZ,       /* u16   200..8000                             */
    PARAM_DSHOT_POLE_PAIRS,      /* u16   1..32                                 */
    PARAM_DSHOT_DIRECTION1,      /* u8    0=normal, 1=reversed                  */
    PARAM_DSHOT_DIRECTION2,
    PARAM_DSHOT_3D_MODE1,        /* u8    0=normal, 1=3D mode                   */
    PARAM_DSHOT_3D_MODE2,
    PARAM_DSHOT_TLM_TIMEOUT_US,  /* u16   20..200                               */
    PARAM_DSHOT_MIN_THROTTLE,    /* u16   0..200                                */
    PARAM_DSHOT_MAX_THROTTLE,    /* u16   1800..2047                            */
    PARAM_DSHOT_START_US,        /* u16   800..1200  throttle sent while arming */

    /* ---- S288 bus servo -------------------------------------------------- */
    PARAM_SERVO_BAUD,            /* u32   9600..6000000  (S288/J288: 6000000)   */
    PARAM_SERVO_ID1,             /* u8    0..253                                */
    PARAM_SERVO_ID2,
    PARAM_SERVO_DIR_TX_LEVEL1,   /* u8    0/1  SPDT switch level for TX mode     */
    PARAM_SERVO_DIR_TX_LEVEL2,
    PARAM_SERVO_ANGLE_MIN1,      /* i16  -36000..36000  0.1 deg                 */
    PARAM_SERVO_ANGLE_MIN2,
    PARAM_SERVO_ANGLE_MAX1,      /* i16  -36000..36000  0.1 deg                 */
    PARAM_SERVO_ANGLE_MAX2,
    PARAM_SERVO_ANGLE_GAIN1,     /* i16  10..10000 (100 = 1.00x)                */
    PARAM_SERVO_ANGLE_GAIN2,
    PARAM_SERVO_ANGLE_OFS1,      /* i16  -18000..18000  0.1 deg                 */
    PARAM_SERVO_ANGLE_OFS2,
    PARAM_SERVO_POLL_HZ,         /* u16   10..1000                              */
    PARAM_SERVO_TORQUE_LIMIT1,   /* u8    0..100  (%)                           */
    PARAM_SERVO_TORQUE_LIMIT2,
    PARAM_SERVO_ENABLE_ON_BOOT,  /* u8    0/1                                   */

    /* ---- ADC monitoring -------------------------------------------------- */
    PARAM_ADC_SYS_V_SCALE,       /* i32   uV per LSB,  e.g. 10000 = 10.0 mV/LSB */
    PARAM_ADC_SYS_V_OFFSET_MV,   /* i32   mV                                    */
    PARAM_ADC_SYS_I_SCALE,       /* i32   uA per LSB                            */
    PARAM_ADC_SYS_I_OFFSET_MA,   /* i32   mA                                    */
    PARAM_ADC_ESC1_I_SCALE,      /* i32   uA per LSB                            */
    PARAM_ADC_ESC2_I_SCALE,      /* i32   uA per LSB                            */
    PARAM_ADC_ESC_I_OFFSET_MA,   /* i32   mA                                    */
    PARAM_ADC_VOLT_MIN_DV,       /* u16   0.1 V  under voltage trip             */
    PARAM_ADC_VOLT_MAX_DV,       /* u16   0.1 V  over voltage trip              */
    PARAM_ADC_CURR_MAX_CA,       /* i16   0.01 A system over current trip       */
    PARAM_ADC_ESC_CURR_MAX_MA,   /* u16   mA     per ESC over current trip      */
    PARAM_ADC_ENABLE_PROTECTION, /* u8    0/1                                   */

    /* ---- RGB status LED -------------------------------------------------- */
    PARAM_LED_BRIGHTNESS,        /* u16   0..255                                */
    PARAM_LED_STATE_MASK,        /* u32   which status animations are shown     */

    /* ---- USB / host tool ------------------------------------------------- */
    PARAM_USB_PROTO_TIMEOUT_MS,  /* u16   100..10000                            */
    PARAM_USB_AUTO_TELEMETRY_HZ, /* u16   0..1000  0 = only on request          */

    /* ---- AM32 ESC tuning ------------------------------------------------- */
    PARAM_AM32_4WAY_NUM_RETRIES, /* u8    0..10                                 */
    PARAM_AM32_4WAY_BAUD,        /* u32   9600..1000000                         */
    PARAM_AM32_ENABLE_PASSTHROUGH,/* u8   0/1  allow PC to talk to AM32 via board */

    /* ---- S288 position / speed loop gains (appended to keep ids stable) -- */
    PARAM_SERVO_KP1,             /* i32   0..100000000   k_pos: 1280000 = 1.0   */
    PARAM_SERVO_KP2,
    PARAM_SERVO_KD1,             /* i32   0..1000000000  k_spd: 128000000 = 1.0 */
    PARAM_SERVO_KD2,

    /* ---- keep last ------------------------------------------------------- */
    PARAM_COUNT
} param_id_t;

/* Array parameter helpers: PARAM_DSHOT_DIRECTION(ch) = id of motor ch (0/1) */
#define PARAM_DSHOT_DIRECTION(ch)     (PARAM_DSHOT_DIRECTION1 + (ch))
#define PARAM_DSHOT_3D_MODE(ch)       (PARAM_DSHOT_3D_MODE1 + (ch))
#define PARAM_SERVO_ID(ch)            (PARAM_SERVO_ID1 + (ch))
#define PARAM_SERVO_DIR_TX_LEVEL(ch)  (PARAM_SERVO_DIR_TX_LEVEL1 + (ch))
#define PARAM_SERVO_ANGLE_MIN(ch)     (PARAM_SERVO_ANGLE_MIN1 + (ch))
#define PARAM_SERVO_ANGLE_MAX(ch)     (PARAM_SERVO_ANGLE_MAX1 + (ch))
#define PARAM_SERVO_ANGLE_GAIN(ch)    (PARAM_SERVO_ANGLE_GAIN1 + (ch))
#define PARAM_SERVO_ANGLE_OFS(ch)     (PARAM_SERVO_ANGLE_OFS1 + (ch))
#define PARAM_SERVO_TORQUE_LIMIT(ch)  (PARAM_SERVO_TORQUE_LIMIT1 + (ch))
#define PARAM_SERVO_KP(ch)            (PARAM_SERVO_KP1 + (ch))
#define PARAM_SERVO_KD(ch)            (PARAM_SERVO_KD1 + (ch))

typedef struct {
    const char *name;
    param_type_t type;
    int32_t min;
    int32_t max;
    int32_t def;
    uint8_t persist;   /* 1 = stored in flash, 0 = runtime only               */
    uint8_t readonly;  /* 1 = host tool must not write                        */
    const char *desc;  /* short human readable description (UTF-8)            */
} param_desc_t;

/* Parameter system error codes */
#define PARAM_OK              (0)
#define PARAM_ERR_ID          (-1)
#define PARAM_ERR_RANGE       (-2)
#define PARAM_ERR_READONLY    (-3)
#define PARAM_ERR_TYPE        (-4)
#define PARAM_ERR_FLASH       (-5)

/* Initialize the parameter system: load from flash (or defaults on error). */
void param_init(void);

/* Load parameter values into RAM. Returns PARAM_OK on success. */
int param_load(void);

/* Store the current parameter values into flash.  Returns PARAM_OK on success. */
int param_save(void);

/* Reset all parameters to their default values (RAM only, call param_save()
 * to make it persistent). */
void param_reset_defaults(void);

/* Get/set a single parameter.  param_set() validates the value range. */
int32_t param_get(param_id_t id);
int param_set(param_id_t id, int32_t value);

/* Get the descriptor of a parameter (NULL if the id is invalid). */
const param_desc_t *param_get_desc(param_id_t id);

/* Number of parameter entries (PARAM_COUNT). */
uint16_t param_count(void);

/* Apply parameters to the modules that cache them (DShot, servo, LED, ECAT).
 * Called after param_load() and after the host tool changed a parameter. */
void param_apply(void);

#ifdef __cplusplus
}
#endif

#endif /* PARAM_H */
