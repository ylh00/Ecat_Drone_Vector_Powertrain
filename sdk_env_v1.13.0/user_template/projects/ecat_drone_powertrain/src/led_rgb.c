/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * RGB status LED driver for the ecat_drone_5e3y board.
 *
 * Hardware
 * --------
 * One RGB LED device (common anode / active low) driven by three PWM1 outputs:
 *   PWM1.P4 (PF12) -> green
 *   PWM1.P5 (PF13) -> red
 *   PWM1.P6 (PF14) -> blue
 * The pads are configured by the board package (pinmux.c init_pins()) during
 * board_init(), no pin setup is required here.
 *
 * PWMv2 channel/comparator/counter mapping (fixed in silicon, HPM5E00 user
 * manual chapter "PWMv2", PWM generation / trigger output sections):
 *   CMP[4k..4k+3] belong to counter k, output n uses CMP[2n] (a) / CMP[2n+1] (b)
 *   -> PWM1.P4 (green): CMP8/9  on counter 2 (BOARD_RGB_LED_GREEN_CMP_INDEX)
 *   -> PWM1.P5 (red)  : CMP10/11 on counter 2 (BOARD_RGB_LED_RED_CMP_INDEX)
 *   -> PWM1.P6 (blue) : CMP12/13 on counter 3 (BOARD_RGB_LED_BLUE_CMP_INDEX)
 * The RGB LED therefore spans two counters, so both counters are loaded with
 * the same reload value (BOARD_RGB_LED_FREQ_HZ) and are reset / enabled /
 * started synchronously through CNT_GLBCFG (pwmv2_*_multi_counter_sync), which
 * keeps the three colours locked together (no drift/beating).
 *
 * Waveform: comparator a is held at 0 and comparator b carries the duty cycle,
 * so the output is high while CNT < b (see "PWM generation", b <= T case).
 * BOARD_RGB_LED_ACTIVE_LOW (and the PARAM_ECAT_LED_INVERT parameter) decide
 * whether a high PWM level means "LED on" or "LED off".
 *
 * The PWM setup follows hpm_sdk/samples/rgb_led/src/rgb_led.c (PWMv2 branch):
 * two comparators per output, comparator values taken from shadow registers
 * and latched on the counter reload event.  The synchronous multi-counter
 * start is the same approach as
 * hpm_sdk/samples/drivers/pwmv2/pwm_output/src/pwm.c.  The animation engine is
 * application specific.
 *
 * Timing: led_task() is paced internally with the 1 kHz g_app_tick_ms tick to
 * APP_CFG_LED_UPDATE_HZ (50 Hz), so it is very cheap to call from the main
 * loop.  led_set_code() only updates a variable and is safe to call from the
 * 1 kHz control task.
 */

#include "board.h"
#include "hpm_pwmv2_drv.h"
#include "hpm_clock_drv.h"

#include "led_rgb.h"
#include "app_task.h"
#include "app_log.h"
#include "param.h"

/* ------------------------------------------------------------------------- */
/* Configuration                                                              */
/* ------------------------------------------------------------------------- */

#define LED_RGB_CHANNELS        (3U)
#define LED_RGB_IDX_GREEN       (0U)
#define LED_RGB_IDX_RED         (1U)
#define LED_RGB_IDX_BLUE        (2U)

/* Shadow register allocation (28 shadow registers are available).
 *   0      : counter reload value (shared by both counters)
 *   1      : compare point "a" (always 0)
 *   2..4   : compare point "b" (duty cycle) of green / red / blue            */
#define LED_RGB_SHADOW_RELOAD   (0U)
#define LED_RGB_SHADOW_CMP_A    (1U)
#define LED_RGB_SHADOW_CMP_B    (2U)

/* 24 bit integer part of the counter/reload value (see SHADOW_VAL layout) */
#define LED_RGB_MAX_PERIOD_TICKS (0xFFFFFFUL)
/* keep enough duty resolution for the 8 bit colour components */
#define LED_RGB_MIN_PERIOD_TICKS (256UL)

/* Animation update period derived from APP_CFG_LED_UPDATE_HZ */
#define LED_RGB_UPDATE_PERIOD_MS (1000U / (APP_CFG_LED_UPDATE_HZ))

/* Fallback PWM1 clock if the board package cannot report it (100 MHz) */
#define LED_RGB_PWM_CLOCK_FALLBACK_HZ (100000000UL)

/* Double flash pattern of the PRE-OP animation (two 80 ms pulses 200 ms apart) */
#define LED_RGB_DOUBLE_ON_MS    (80UL)
#define LED_RGB_DOUBLE_GAP_MS   (200UL)

/* Both counters must be reset/enabled/started together (no colour drift) */
#define LED_RGB_COUNTER_MASK \
    ((uint8_t)((1UL << (uint32_t)BOARD_RGB_LED_COUNTER_GREEN_RED) | \
               (1UL << (uint32_t)BOARD_RGB_LED_COUNTER_BLUE)))

/* ------------------------------------------------------------------------- */
/* Animation description                                                      */
/* ------------------------------------------------------------------------- */

typedef enum {
    LED_ANIM_SOLID = 0,
    LED_ANIM_BLINK,          /* on_ms / period_ms square wave                 */
    LED_ANIM_DOUBLE_BLINK,   /* two short flashes at the start of the period  */
    LED_ANIM_BREATH,         /* symmetric triangle ramp over the period       */
} led_anim_pattern_t;

typedef struct {
    uint8_t r;               /* base colour, 0..255 */
    uint8_t g;
    uint8_t b;
    uint8_t pattern;         /* led_anim_pattern_t */
    uint16_t period_ms;
    uint16_t on_ms;          /* BLINK: on time, 0 = use a default            */
} led_anim_t;

static const led_anim_t s_animations[LED_CODE_MAX] = {
    [LED_CODE_BOOT]        = {.r = 255U, .g = 255U, .b = 255U, .pattern = LED_ANIM_BREATH,       .period_ms = 1200U},
    [LED_CODE_SELF_TEST]   = {.r = 0U,   .g = 0U,   .b = 255U, .pattern = LED_ANIM_BLINK,        .period_ms = 100U,  .on_ms = 50U},
    [LED_CODE_ECAT_WAIT]   = {.r = 255U, .g = 255U, .b = 0U,   .pattern = LED_ANIM_BLINK,        .period_ms = 1000U, .on_ms = 500U},
    [LED_CODE_ECAT_PREOP]  = {.r = 255U, .g = 255U, .b = 0U,   .pattern = LED_ANIM_DOUBLE_BLINK, .period_ms = 1000U},
    [LED_CODE_ECAT_SAFEOP] = {.r = 0U,   .g = 255U, .b = 255U, .pattern = LED_ANIM_BLINK,        .period_ms = 500U,  .on_ms = 250U},
    [LED_CODE_OP_IDLE]     = {.r = 0U,   .g = 255U, .b = 0U,   .pattern = LED_ANIM_BREATH,       .period_ms = 2000U},
    [LED_CODE_RUN]         = {.r = 0U,   .g = 255U, .b = 0U,   .pattern = LED_ANIM_SOLID,        .period_ms = 0U},
    [LED_CODE_RUN_ACTIVE]  = {.r = 0U,   .g = 255U, .b = 0U,   .pattern = LED_ANIM_BLINK,        .period_ms = 125U,  .on_ms = 30U},
    [LED_CODE_FAULT]       = {.r = 255U, .g = 0U,   .b = 0U,   .pattern = LED_ANIM_BLINK,        .period_ms = 250U,  .on_ms = 125U},
    [LED_CODE_USB_ACTIVE]  = {.r = 0U,   .g = 0U,   .b = 255U, .pattern = LED_ANIM_BLINK,        .period_ms = 1200U, .on_ms = 80U},
    [LED_CODE_DFU]         = {.r = 255U, .g = 0U,   .b = 255U, .pattern = LED_ANIM_BREATH,       .period_ms = 800U},
};

/* One PWM output of the RGB device */
typedef struct {
    pwm_channel_t channel;   /* PWM1 output channel                          */
    uint8_t cmp_index;       /* comparator "a"; comparator "b" = index + 1   */
    uint8_t shadow_index;    /* shadow register driving comparator "b"       */
} led_pwm_channel_t;

static const led_pwm_channel_t s_led_channels[LED_RGB_CHANNELS] = {
    [LED_RGB_IDX_GREEN] = {
        .channel      = (pwm_channel_t)BOARD_RGB_LED_GREEN_CHANNEL,
        .cmp_index    = (uint8_t)BOARD_RGB_LED_GREEN_CMP_INDEX,
        .shadow_index = (uint8_t)LED_RGB_SHADOW_CMP_B,
    },
    [LED_RGB_IDX_RED] = {
        .channel      = (pwm_channel_t)BOARD_RGB_LED_RED_CHANNEL,
        .cmp_index    = (uint8_t)BOARD_RGB_LED_RED_CMP_INDEX,
        .shadow_index = (uint8_t)(LED_RGB_SHADOW_CMP_B + 1U),
    },
    [LED_RGB_IDX_BLUE] = {
        .channel      = (pwm_channel_t)BOARD_RGB_LED_BLUE_CHANNEL,
        .cmp_index    = (uint8_t)BOARD_RGB_LED_BLUE_CMP_INDEX,
        .shadow_index = (uint8_t)(LED_RGB_SHADOW_CMP_B + 2U),
    },
};

/* Compile time sanity checks of the board configuration */
_Static_assert((BOARD_RGB_LED_GREEN_CHANNEL < 8U) &&
               (BOARD_RGB_LED_RED_CHANNEL < 8U) &&
               (BOARD_RGB_LED_BLUE_CHANNEL < 8U),
               "RGB LED PWM channels must be in 0..7");
_Static_assert((BOARD_RGB_LED_COUNTER_GREEN_RED == pwm_counter_2) &&
               (BOARD_RGB_LED_COUNTER_BLUE == pwm_counter_3),
               "unexpected PWM1 counter mapping for the RGB LED");

/* ------------------------------------------------------------------------- */
/* Module state                                                               */
/* ------------------------------------------------------------------------- */

static uint32_t s_period_ticks;      /* PWM period in timer ticks             */
static uint32_t s_reload;            /* counter reload value (period - 1)     */
static uint8_t  s_brightness = APP_CFG_LED_BRIGHTNESS_DEFAULT;
static bool     s_pwm_high_is_on;    /* true when a high PWM level = LED on   */
static bool     s_initialized;

static led_code_t s_code = LED_CODE_BOOT;
static uint32_t   s_code_start_ms;
static uint32_t   s_last_update_ms;
static uint32_t   s_update_period_ms = LED_RGB_UPDATE_PERIOD_MS;

static bool     s_override;
static uint8_t  s_override_rgb[LED_RGB_CHANNELS];

static bool     s_test_active;
static uint8_t  s_test_pattern;
static uint32_t s_test_start_ms;
static uint32_t s_test_end_ms;

/* ------------------------------------------------------------------------- */
/* Gamma correction (optional)                                                */
/* ------------------------------------------------------------------------- */

#if LED_RGB_USE_GAMMA
/* Approximate x^2.2 curve sampled at 32 points (0..31 -> 0..255). */
static const uint8_t s_gamma_table[32] = {
    0U,   0U,   1U,   2U,   3U,   5U,   7U,   10U,  13U,  17U,  21U,  26U,
    32U,  38U,  44U,  52U,  60U,  68U,  77U,  87U,  97U,  108U, 120U, 132U,
    145U, 159U, 173U, 188U, 204U, 220U, 237U, 255U
};

/* Linear interpolation between the 32 gamma table points */
static uint8_t led_gamma_apply(uint8_t value)
{
    uint32_t index = (uint32_t)value * (APP_ARRAY_SIZE(s_gamma_table) - 1U);
    uint32_t pos = index / 255U;
    uint32_t frac = index % 255U;
    uint32_t low = s_gamma_table[pos];
    uint32_t high = s_gamma_table[(pos + 1U) < APP_ARRAY_SIZE(s_gamma_table) ? (pos + 1U) : pos];

    return (uint8_t)(low + (((high - low) * frac) / 255U));
}
#endif /* LED_RGB_USE_GAMMA */

/* ------------------------------------------------------------------------- */
/* Low level PWM helpers                                                      */
/* ------------------------------------------------------------------------- */

/* Only one shadow value per channel is updated at run time; the PWMv2 compare
 * value is latched on the next counter reload (glitch free), see the update
 * trigger configured in led_init(). */
static void led_pwm_write_level(uint8_t idx, uint8_t level)
{
    uint32_t on_ticks = ((uint32_t)level * s_reload) / 255U;
    uint32_t cmp = s_pwm_high_is_on ? on_ticks : (s_reload - on_ticks);

    pwmv2_set_shadow_val(BOARD_RGB_PWM, s_led_channels[idx].shadow_index, cmp, 0, false);
}

/* Apply brightness (and the optional gamma curve) to one colour component */
static uint8_t led_scale(uint8_t colour)
{
    uint32_t value = ((uint32_t)colour * (uint32_t)s_brightness) / 255U;

#if LED_RGB_USE_GAMMA
    value = led_gamma_apply((uint8_t)value);
#endif
    return (uint8_t)value;
}

/* Write the mixed colour to the three PWM channels */
static void led_write_rgb(uint8_t r, uint8_t g, uint8_t b)
{
    led_pwm_write_level(LED_RGB_IDX_GREEN, led_scale(g));
    led_pwm_write_level(LED_RGB_IDX_RED, led_scale(r));
    led_pwm_write_level(LED_RGB_IDX_BLUE, led_scale(b));
}

/* The LED polarity is a runtime parameter (PARAM_ECAT_LED_INVERT) so that a
 * different LED wiring can be supported without a new build.  It is sampled on
 * every animation step (50 Hz), which is cheap. */
static void led_update_polarity(void)
{
    bool active_low = (BOARD_RGB_LED_ACTIVE_LOW != 0U);
    bool invert = (param_get(PARAM_ECAT_LED_INVERT) != 0);

    /* Active low: LED on when the pad is low.  With no logical inversion a
     * high PWM level therefore means "off", with the inversion it means "on"
     * (the inversion flips the effective polarity). */
    s_pwm_high_is_on = active_low ? invert : (!invert);
}

/* ------------------------------------------------------------------------- */
/* Animation helpers                                                          */
/* ------------------------------------------------------------------------- */

/* Symmetric triangle ramp, 0 at the period boundaries and 255 in the middle */
static uint8_t led_triangle_level(uint32_t elapsed_ms, uint32_t period_ms)
{
    uint32_t phase;
    uint32_t half;

    if (period_ms < 2U) {
        return 255U;
    }
    phase = elapsed_ms % period_ms;
    half = period_ms / 2U;
    if (phase < half) {
        return (uint8_t)((phase * 255U) / half);
    }
    return (uint8_t)(((period_ms - phase) * 255U) / half);
}

/* Animation intensity multiplier (0..255) for the elapsed time in the state */
static uint8_t led_anim_level(const led_anim_t *anim, uint32_t elapsed_ms)
{
    uint32_t period;
    uint32_t phase;

    switch (anim->pattern) {
    case LED_ANIM_BLINK:
        period = (anim->period_ms != 0U) ? anim->period_ms : 1000U;
        phase = elapsed_ms % period;
        return (phase < anim->on_ms) ? 255U : 0U;

    case LED_ANIM_DOUBLE_BLINK:
        period = (anim->period_ms != 0U) ? anim->period_ms : 1000U;
        phase = elapsed_ms % period;
        if (phase < LED_RGB_DOUBLE_ON_MS) {
            return 255U;
        }
        if ((phase >= LED_RGB_DOUBLE_GAP_MS) &&
            (phase < (LED_RGB_DOUBLE_GAP_MS + LED_RGB_DOUBLE_ON_MS))) {
            return 255U;
        }
        return 0U;

    case LED_ANIM_BREATH:
        return led_triangle_level(elapsed_ms, anim->period_ms);

    case LED_ANIM_SOLID:
    default:
        return 255U;
    }
}

/* Colour of a host requested test pattern */
static void led_test_frame(uint8_t pattern, uint32_t elapsed_ms,
                           uint8_t *r, uint8_t *g, uint8_t *b)
{
    uint8_t level;

    *r = 0U;
    *g = 0U;
    *b = 0U;
    switch (pattern) {
    case LED_TEST_RED:
        *r = 255U;
        break;
    case LED_TEST_GREEN:
        *g = 255U;
        break;
    case LED_TEST_BLUE:
        *b = 255U;
        break;
    case LED_TEST_WHITE:
        *r = 255U;
        *g = 255U;
        *b = 255U;
        break;
    case LED_TEST_RGB_CYCLE:
        switch ((elapsed_ms / 500U) % 6U) {
        case 0U:
            *r = 255U;
            break;
        case 1U:
            *g = 255U;
            break;
        case 2U:
            *b = 255U;
            break;
        case 3U:
            *r = 255U;
            *g = 255U;
            break;
        case 4U:
            *g = 255U;
            *b = 255U;
            break;
        default:
            *r = 255U;
            *b = 255U;
            break;
        }
        break;
    case LED_TEST_WHITE_BLINK:
        if (((elapsed_ms / 500U) & 1U) == 0U) {
            *r = 255U;
            *g = 255U;
            *b = 255U;
        }
        break;
    case LED_TEST_WHITE_BREATH:
        level = led_triangle_level(elapsed_ms, 2000U);
        *r = level;
        *g = level;
        *b = level;
        break;
    case LED_TEST_OFF:
    default:
        break;
    }
}

/* ------------------------------------------------------------------------- */
/* PWM initialization                                                         */
/* ------------------------------------------------------------------------- */

static void led_init_counter(pwm_counter_t counter)
{
    pwmv2_counter_select_data_offset_from_shadow_value(BOARD_RGB_PWM, counter, LED_RGB_SHADOW_RELOAD);
    pwmv2_set_reload_update_time(BOARD_RGB_PWM, counter, pwm_reload_update_on_shlk);
    pwmv2_counter_burst_disable(BOARD_RGB_PWM, counter);
}

/* ------------------------------------------------------------------------- */
/* Public API                                                                 */
/* ------------------------------------------------------------------------- */

void led_init(void)
{
    uint32_t pwm_freq;
    uint32_t off_cmp;
    uint8_t i;

    /* Defaults from the parameter system (param_init() runs before led_init()) */
    s_brightness = (uint8_t)APP_CLAMP(param_get(PARAM_LED_BRIGHTNESS), 0, 255);
    led_update_polarity();

    pwm_freq = board_init_pwm_clock(BOARD_RGB_PWM);
    if (pwm_freq == 0U) {
        pwm_freq = clock_get_frequency(BOARD_RGB_PWM_CLOCK_NAME);
    }
    if (pwm_freq == 0U) {
        pwm_freq = LED_RGB_PWM_CLOCK_FALLBACK_HZ;
        LOGM_W(LOG_MOD_LED, "PWM1 clock unavailable, assuming %lu Hz", (unsigned long)pwm_freq);
    }

    s_period_ticks = pwm_freq / BOARD_RGB_LED_FREQ_HZ;
    if (s_period_ticks < LED_RGB_MIN_PERIOD_TICKS) {
        LOGM_W(LOG_MOD_LED, "PWM1 clock %lu Hz too low for %lu Hz, period clamped",
               (unsigned long)pwm_freq, (unsigned long)BOARD_RGB_LED_FREQ_HZ);
        s_period_ticks = LED_RGB_MIN_PERIOD_TICKS;
    }
    if (s_period_ticks > LED_RGB_MAX_PERIOD_TICKS) {
        s_period_ticks = LED_RGB_MAX_PERIOD_TICKS;
    }
    s_reload = s_period_ticks - 1U;
    s_update_period_ms = (LED_RGB_UPDATE_PERIOD_MS != 0U) ? LED_RGB_UPDATE_PERIOD_MS : 1U;

    /* Clean PWMv2 state.  The shadow registers are written "on the fly" and the
     * compare values are taken over at the counter reload, exactly like the
     * SDK rgb_led sample does it. */
    pwmv2_deinit(BOARD_RGB_PWM);
    pwmv2_disable_shadow_lock_feature(BOARD_RGB_PWM);

    /* Both RGB counters are reset / enabled / started as one unit so the
     * three colours cannot drift apart. */
    pwmv2_disable_multi_counter_sync(BOARD_RGB_PWM, LED_RGB_COUNTER_MASK);
    pwmv2_reset_multi_counter_sync(BOARD_RGB_PWM, LED_RGB_COUNTER_MASK);
    led_init_counter((pwm_counter_t)BOARD_RGB_LED_COUNTER_GREEN_RED);
    led_init_counter((pwm_counter_t)BOARD_RGB_LED_COUNTER_BLUE);

    for (i = 0U; i < LED_RGB_CHANNELS; i++) {
        const led_pwm_channel_t *ch = &s_led_channels[i];
        uint8_t cmp_a = ch->cmp_index;
        uint8_t cmp_b = (uint8_t)(ch->cmp_index + 1U);

        /* comparator a = 0 (rising edge), comparator b = duty cycle */
        pwmv2_select_cmp_source(BOARD_RGB_PWM, cmp_a, cmp_value_from_shadow_val, LED_RGB_SHADOW_CMP_A);
        pwmv2_select_cmp_source(BOARD_RGB_PWM, cmp_b, cmp_value_from_shadow_val, ch->shadow_index);
        pwmv2_cmp_update_trig_time(BOARD_RGB_PWM, cmp_a, pwm_shadow_register_update_on_shlk);
        pwmv2_cmp_update_trig_time(BOARD_RGB_PWM, cmp_b, pwm_shadow_register_update_on_shlk);

        pwmv2_disable_four_cmp(BOARD_RGB_PWM, ch->channel);
    }

    /* Initial (LED off) shadow values + one shadow lock event to load them */
    off_cmp = s_pwm_high_is_on ? 0U : s_reload;
    pwmv2_set_shadow_val(BOARD_RGB_PWM, LED_RGB_SHADOW_RELOAD, s_reload, 0, false);
    pwmv2_set_shadow_val(BOARD_RGB_PWM, LED_RGB_SHADOW_CMP_A, 0U, 0, false);
    for (i = 0U; i < LED_RGB_CHANNELS; i++) {
        pwmv2_set_shadow_val(BOARD_RGB_PWM, s_led_channels[i].shadow_index, off_cmp, 0, false);
    }
    pwmv2_issue_shadow_register_lock_event(BOARD_RGB_PWM);

    /* From now on the compare values follow the shadow register at reload */
    for (i = 0U; i < LED_RGB_CHANNELS; i++) {
        uint8_t cmp_a = s_led_channels[i].cmp_index;

        pwmv2_cmp_update_trig_time(BOARD_RGB_PWM, cmp_a, pwm_shadow_register_update_on_reload);
        pwmv2_cmp_update_trig_time(BOARD_RGB_PWM, (uint8_t)(cmp_a + 1U),
                                   pwm_shadow_register_update_on_reload);
    }

    /* Enable the outputs and start both counters synchronously */
    for (i = 0U; i < LED_RGB_CHANNELS; i++) {
        pwmv2_channel_enable_output(BOARD_RGB_PWM, s_led_channels[i].channel);
    }
    pwmv2_enable_multi_counter_sync(BOARD_RGB_PWM, LED_RGB_COUNTER_MASK);
    pwmv2_start_pwm_output_sync(BOARD_RGB_PWM, LED_RGB_COUNTER_MASK);

    s_code = LED_CODE_BOOT;
    s_code_start_ms = g_app_tick_ms;
    s_last_update_ms = g_app_tick_ms;
    s_override = false;
    s_test_active = false;
    s_initialized = true;

    LOGM_I(LOG_MOD_LED, "PWM1 %lu Hz, reload %lu, %s, brightness %u",
           (unsigned long)pwm_freq, (unsigned long)s_reload,
           (BOARD_RGB_LED_ACTIVE_LOW != 0U) ? "active low" : "active high",
           (unsigned)s_brightness);
}

void led_set_rgb(uint8_t r, uint8_t g, uint8_t b)
{
    s_override_rgb[LED_RGB_IDX_GREEN] = g;
    s_override_rgb[LED_RGB_IDX_RED] = r;
    s_override_rgb[LED_RGB_IDX_BLUE] = b;
    s_override = true;
}

void led_clear_rgb(void)
{
    s_override = false;
}

void led_set_code(led_code_t code)
{
    if ((uint32_t)code >= (uint32_t)LED_CODE_MAX) {
        return;
    }
    /* app_task.c calls led_set_code() on every 1 kHz control iteration, so an
     * RGB override survives as long as the status code itself does not change. */
    if (code != s_code) {
        s_override = false;
        s_code = code;
        s_code_start_ms = g_app_tick_ms;
    }
}

led_code_t led_get_code(void)
{
    return s_code;
}

uint16_t led_get_status_word(void)
{
    return (uint16_t)(((uint16_t)s_code & 0xFFU) | ((uint16_t)s_brightness << 8));
}

void led_set_brightness(uint8_t brightness)
{
    s_brightness = brightness;
}

void led_task(void)
{
    const led_anim_t *anim;
    uint32_t now;
    uint32_t elapsed;
    uint8_t level;
    uint8_t r;
    uint8_t g;
    uint8_t b;

    if (!s_initialized) {
        return;
    }

    now = g_app_tick_ms;
    if ((now - s_last_update_ms) < s_update_period_ms) {
        return;
    }
    s_last_update_ms = now;

    led_update_polarity();

    /* 1) host test pattern has the highest priority */
    if (s_test_active) {
        if ((int32_t)(now - s_test_end_ms) < 0) {
            led_test_frame(s_test_pattern, now - s_test_start_ms, &r, &g, &b);
            led_write_rgb(r, g, b);
            return;
        }
        s_test_active = false;
    }

    /* 2) explicit RGB colour (host tool / direct call) */
    if (s_override) {
        led_write_rgb(s_override_rgb[LED_RGB_IDX_RED],
                      s_override_rgb[LED_RGB_IDX_GREEN],
                      s_override_rgb[LED_RGB_IDX_BLUE]);
        return;
    }

    /* 3) state animation */
    anim = &s_animations[(uint32_t)s_code];
    elapsed = now - s_code_start_ms;
    level = led_anim_level(anim, elapsed);
    r = (uint8_t)(((uint32_t)anim->r * level) / 255U);
    g = (uint8_t)(((uint32_t)anim->g * level) / 255U);
    b = (uint8_t)(((uint32_t)anim->b * level) / 255U);
    led_write_rgb(r, g, b);
}

void led_test_pattern(uint8_t pattern, uint16_t duration_ms)
{
    uint32_t now = g_app_tick_ms;

    if ((duration_ms == 0U) || (pattern > LED_TEST_MAX)) {
        s_test_active = false;
        return;
    }
    s_test_pattern = pattern;
    s_test_start_ms = now;
    s_test_end_ms = now + (uint32_t)duration_ms;
    s_test_active = true;
}
