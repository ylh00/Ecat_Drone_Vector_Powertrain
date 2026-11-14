/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * RGB status LED driver (PWM1 P4/P5/P6 -> green / red / blue)
 *
 * The three LEDs are one RGB device; the firmware shows the board state with
 * colours and animation patterns.  A status code is also exported to the
 * EtherCAT TxPDO and the host tool.
 */

#ifndef LED_RGB_H
#define LED_RGB_H

#include <stdint.h>
#include "app_config.h"
#include "app_state.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Status codes shown by the LED and reported in led_status */
typedef enum {
    LED_CODE_BOOT = 0,          /* white breathing                               */
    LED_CODE_SELF_TEST,         /* blue blinking                                 */
    LED_CODE_ECAT_WAIT,         /* yellow slow blink (no master)                 */
    LED_CODE_ECAT_PREOP,        /* yellow double blink                           */
    LED_CODE_ECAT_SAFEOP,       /* cyan blink                                    */
    LED_CODE_OP_IDLE,           /* green slow breathing (OP, outputs disabled)   */
    LED_CODE_RUN,               /* green solid (running)                         */
    LED_CODE_RUN_ACTIVE,        /* green fast pulse with motor activity          */
    LED_CODE_FAULT,             /* red blinking                                  */
    LED_CODE_USB_ACTIVE,        /* blue pulse when the PC tool is connected      */
    LED_CODE_DFU,               /* magenta breathing                             */
    LED_CODE_MAX
} led_code_t;

/*
 * Compile time option: 1 = apply an approximate gamma 2.2 curve to the three
 * PWM channels before the duty cycle is calculated (recommended for a visually
 * linear brightness ramp, it makes the pattern somewhat dimmer at low
 * brightness values).  0 = linear duty cycle (default, maximum brightness).
 * Can be overridden from the build system: -DLED_RGB_USE_GAMMA=1.
 */
#ifndef LED_RGB_USE_GAMMA
#define LED_RGB_USE_GAMMA (0)
#endif

/* Test patterns used by led_test_pattern() */
#define LED_TEST_OFF            (0U)    /* all LEDs off                          */
#define LED_TEST_RED            (1U)    /* solid red                             */
#define LED_TEST_GREEN          (2U)    /* solid green                           */
#define LED_TEST_BLUE           (3U)    /* solid blue                            */
#define LED_TEST_WHITE          (4U)    /* solid white                           */
#define LED_TEST_RGB_CYCLE      (5U)    /* red / green / blue / yellow / cyan / magenta */
#define LED_TEST_WHITE_BLINK    (6U)    /* 1 Hz white blink                      */
#define LED_TEST_WHITE_BREATH   (7U)    /* 2 s white breathing ramp              */
#define LED_TEST_MAX            (LED_TEST_WHITE_BREATH)

/* Initialize the PWM outputs and the animation engine */
void led_init(void);

/* Directly set a RGB colour (0..255 per channel), bypasses the state display
 * until the status code changes (led_set_code() with a different code) or
 * led_clear_rgb() is called. */
void led_set_rgb(uint8_t r, uint8_t g, uint8_t b);

/* Drop a led_set_rgb() override and return to the status code animation */
void led_clear_rgb(void);

/* Select the status code that should be displayed.  Cheap: only stores the
 * code, the animation is evaluated later by led_task().  Safe to call from the
 * 1 kHz control task.  A code change cancels a led_set_rgb() override. */
void led_set_code(led_code_t code);

/* Current status code */
led_code_t led_get_code(void);

/* Raw 16 bit value exported to the EtherCAT TxPDO (code | brightness << 8) */
uint16_t led_get_status_word(void);

/* Non blocking animation task, call at APP_CFG_LED_UPDATE_HZ */
void led_task(void);

/* Set brightness 0..255 */
void led_set_brightness(uint8_t brightness);

/* Test pattern (used by the host tool), duration_ms = 0 means stop the test.
 * The test pattern has the highest priority and overrides both the state
 * animation and a led_set_rgb() colour until it expires. */
void led_test_pattern(uint8_t pattern, uint16_t duration_ms);

#ifdef __cplusplus
}
#endif

#endif /* LED_RGB_H */
