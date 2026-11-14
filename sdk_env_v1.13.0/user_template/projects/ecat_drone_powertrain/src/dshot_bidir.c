/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Bidirectional DShot (BDShot) driver for two AM32 ESCs.
 *
 * Board: ecat_drone_5e3y (HPM5E3Y), ESC lines PWM0.P2 (PF02) / PWM0.P3 (PF03)
 *
 * ===========================================================================
 * 1. TX path (PWMV2 output compare + XDMA fixed burst)
 * ===========================================================================
 * - PWM0 counter 1 drives the two ESC outputs (HPM5E00 UM 37.4.8: outputs 2/3
 *   belong to counter 1 and compare points CMP[4..7]):
 *       ESC1 (P2, output 2): a = CMP[4] -> SHADOW_VAL[0], b = CMP[5] -> SHADOW_VAL[2]
 *       ESC2 (P3, output 3): c = CMP[6] -> SHADOW_VAL[1], d = CMP[7] -> SHADOW_VAL[3]
 *   An output is high while a <= CNT < b (X) / c <= CNT < d (Y); the output
 *   polarity is inverted (OUT_POLARITY = 1) so the line idles HIGH and a DShot
 *   bit becomes an active LOW pulse (BDShot requirement).
 * - Counter reload value comes from SHADOW_VAL[4] (CNT_CFG1.CNT_IN_OFF = 4) and
 *   equals one command bit period T (DSHOT600 => 1.667us).
 * - For each bit period the pulse end compare values (SHADOW_VAL[2] and
 *   SHADOW_VAL[3]) are updated by one XDMA fixed burst of 2 words:
 *       HPM_XDMA ch0, dest = &SHADOW_VAL[2], burst = 2 words, destination fixed
 *       burst (dest increments inside the burst, wraps back at burst end).
 *   The burst is requested once per bit period through PWM0 DMA request line 0
 *   enabled on CMP[16] (mapped to counter 1, compare value 4 ticks after the
 *   reload).  CMP[5]/CMP[7] latch their shadow value on the counter reload, so
 *   a burst fired in bit period k is applied in bit period k+1.
 *   Routing: PWM0 DMA0 -> TRGM DMACFG_0 (HPM_TRGM0_DMA_SRC_PWM0_0) ->
 *   DMAMUX MOT_0 -> HPM_XDMA channel 0.
 * - Bit 0 of each frame is written to SHADOW_VAL[2]/[3] by the CPU before the
 *   counter starts; the DMA buffer starts with bit 1 and its last (16th) burst
 *   only programs the idle value 0, so it can never leak into a later frame.
 * - The counter runs in burst mode (CNT_BURST = 18 -> at least 19 bit periods,
 *   i.e. 16 data bits plus idle padding) and stops by itself.  The padding
 *   makes the design insensitive to the exact burst semantics and guarantees an
 *   idle value (0 pulse width) in the compare registers when the counter stops.
 * - The PWM burst-end interrupt (IRQn_PWM0) signals "TX done": it switches both
 *   pads from the PWM function to the TRGM input function, arms the capture and
 *   starts the RX window.
 *
 * ===========================================================================
 * 2. RX path (TRGM -> GPTMR1 capture, decode in dshot_task)
 * ===========================================================================
 * - TRGM0 inputs P02/P03 are routed to the GPTMR1 capture inputs 2/3
 *   (TRGOCFG[0x3A] / TRGOCFG[0x3B] = trgm_output_same_as_input).
 * - GPTMR1 channel2/3 run free (32 bit) and capture BOTH edges; the capture
 *   interrupt stores the counter values.  The edge direction is not needed for
 *   the BDShot decode (bits are differential: a transition inside a bit slot is
 *   a logical 1, no transition a logical 0, and the first edge of a frame is
 *   always the start bit).
 * - The decoder (dshot_decode_edges) converts the edge timestamps into slot
 *   counts, rebuilds the 21 bit wire sequence (start bit + 20 GCR bits), maps
 *   the four 5 bit GCR quintets back to nibbles, validates the INVERTED 4 bit
 *   CRC and finally converts the 12 bit telemetry payload into eRPM
 *   (eRPM = 60e6 / period_us, 0x0FFF = stopped).
 * - It does not assume the first gap is exactly one bit slot (GCR quintets may
 *   start with zeros): the 1/2/3 slot candidates allowed by the first gap are
 *   tried and the one that passes GCR validity + CRC with the smallest timing
 *   residual wins.
 * - GPTMR1 channel 0 is a free running 32 bit tick source used for frame pacing
 *   and timeouts; the capture channels use their own counters.  All are clocked
 *   by BOARD_DSHOT_CAP_GPTMR_CLK_NAME (frequency queried at runtime).
 *
 * ===========================================================================
 * 3. State machine / timing
 * ===========================================================================
 *   dshot_task() (main loop)              IRQ context
 *   ------------------------              --------------------------------
 *   IDLE -- pacing elapsed --> start frame (short critical section)
 *                                         PWM burst end -> switch pads to RX
 *   RX -- window elapsed --> decode, switch pads back to PWM, IDLE
 *   TX -- watchdog timeout --> force back to IDLE (error recovery)
 *
 * The TX -> RX -> capture sequence never blocks the main loop: only the short
 * critical section that starts the burst and the two ISRs run in hardware/IRQ
 * context, dshot_task() only decodes once per frame.  dshot_task() must be
 * called every main loop iteration; the requested 1 kHz frame rate is enforced
 * internally with the GPTMR1 time base, so the loop rate only affects latency.
 *
 * No dynamic memory is used.
 *
 * ===========================================================================
 * 4. Bring-up notes (full check list in docs/DSHOT_AM32.md)
 * ===========================================================================
 * - PF02/PF03 must idle HIGH after dshot_init().  If they idle low check
 *   PWM[2]/PWM[3].CFG0.OUT_POLARITY and the PWM counter state.
 * - AM32/BLHeli32 bootloaders can be activated by the signal line level at ESC
 *   power-up; power the ESCs after the board (board level topic).
 * - External pull-ups on the two ESC lines are recommended by the BDShot spec;
 *   the internal pull-up is enabled as a fallback only.
 */

#include <stdint.h>
#include <stddef.h>
#include <string.h>

#include "board.h"
#include "hpm_common.h"
#include "hpm_interrupt.h"
#include "hpm_clock_drv.h"
#include "hpm_pwmv2_drv.h"
#include "hpm_dmav2_drv.h"
#include "hpm_dmamux_drv.h"
#include "hpm_trgm_drv.h"
#include "hpm_gptmr_drv.h"
#include "hpm_gpio_drv.h"
#include "app_task.h"       /* g_app_tick_ms */
#include "dshot.h"

/* ------------------------------------------------------------------------- */
/* Compile time configuration                                                */
/* ------------------------------------------------------------------------- */
#define DSHOT_PWM                 BOARD_DSHOT_PWM
#define DSHOT_PWM_CLK_NAME        BOARD_DSHOT_PWM_CLOCK_NAME
#define DSHOT_PWM_IRQ             BOARD_DSHOT_PWM_IRQ
#define DSHOT_PWM_COUNTER         BOARD_DSHOT_PWM_COUNTER
#define DSHOT_CAP                 BOARD_DSHOT_CAP_GPTMR
#define DSHOT_CAP_CLK_NAME        BOARD_DSHOT_CAP_GPTMR_CLK_NAME
#define DSHOT_CAP_IRQ             BOARD_DSHOT_CAP_GPTMR_IRQ
#define DSHOT_TRGM                BOARD_DSHOT_TRGM

#define DSHOT_ESC_COUNT           APP_CFG_DSHOT_MOTORS

#define DSHOT_ESC1_PWM_CHANNEL    ((pwm_channel_t)BOARD_DSHOT_ESC1_PWM_CHANNEL)
#define DSHOT_ESC2_PWM_CHANNEL    ((pwm_channel_t)BOARD_DSHOT_ESC2_PWM_CHANNEL)
#define DSHOT_ESC1_CAP_CHANNEL    ((uint8_t)BOARD_DSHOT_CAP_ESC1_CHANNEL)
#define DSHOT_ESC2_CAP_CHANNEL    ((uint8_t)BOARD_DSHOT_CAP_ESC2_CHANNEL)

/* compare point indices (outputs 2/3 use CMP[4..7], see UM 37.4.8) */
#define DSHOT_ESC1_CMP_A          ((uint8_t)BOARD_DSHOT_ESC1_CMP_INDEX)        /* 4 */
#define DSHOT_ESC1_CMP_B          ((uint8_t)(BOARD_DSHOT_ESC1_CMP_INDEX + 1U)) /* 5 */
#define DSHOT_ESC2_CMP_A          ((uint8_t)BOARD_DSHOT_ESC2_CMP_INDEX)        /* 6 */
#define DSHOT_ESC2_CMP_B          ((uint8_t)(BOARD_DSHOT_ESC2_CMP_INDEX + 1U)) /* 7 */
/* free compare point >= 16, may be mapped to any counter; only used as the
 * periodic DMA request source, it never drives an output pin */
#define DSHOT_DMA_TRIGGER_CMP     (16U)
#define DSHOT_DMA_TRIGGER_OFFSET  (4U)   /* ticks after the counter reload */

/* shadow value indices (SHADOW_VAL[] registers) */
#define DSHOT_SHADOW_ESC1_A       (0U)
#define DSHOT_SHADOW_ESC2_A       (1U)
#define DSHOT_SHADOW_ESC1_B       (2U)   /* DMA destination, contiguous with 3 */
#define DSHOT_SHADOW_ESC2_B       (3U)
#define DSHOT_SHADOW_RELOAD       (4U)
#define DSHOT_SHADOW_DMA_TRIGGER  (5U)

/* TX DMA: XDMA (not used by any other module), TRGM DMACFG_0 -> MOT_0 */
#define DSHOT_TX_DMA              HPM_XDMA
#define DSHOT_TX_DMA_CH           (0U)
#define DSHOT_TRGM_DMA_CFG        TRGM_DMACFG_0
#define DSHOT_TRGM_DMA_SRC        HPM_TRGM0_DMA_SRC_PWM0_0
#define DSHOT_DMAMUX_SRC          HPM_DMA_SRC_MOT_0

/* bit periods emitted per frame: 16 data + padding; CNT_BURST = periods - 1 */
#define DSHOT_TX_PERIODS          (19U)
#define DSHOT_CNT_BURST           (DSHOT_TX_PERIODS - 1U)

/* one DMA burst = 2 words = the two ESC compare values of one bit */
#define DSHOT_BITS                (16U)
#define DSHOT_TX_BURST_WORDS      (2U)
#define DSHOT_TX_BUF_WORDS        (DSHOT_BITS * DSHOT_TX_BURST_WORDS)

/* free running 32 bit time base channel on GPTMR1 */
#define DSHOT_TIMEBASE_CH         (0U)

/* AM32 DShot programming script queue: 16 steps = 4 pending EEPROM bytes */
#define DSHOT_PROG_QUEUE_LEN      (16U)

#define DSHOT_RATE_DEFAULT        (600U)
#define DSHOT_UPDATE_HZ_DEFAULT   (APP_CFG_DSHOT_UPDATE_HZ)
#define DSHOT_TLM_TIMEOUT_US_DEF  (APP_CFG_DSHOT_TLM_TIMEOUT_US)

/* a frame is considered lost if the burst end interrupt does not arrive within
 * this many nominal frame durations */
#define DSHOT_TX_WATCHDOG_FRAMES  (3U)

/* A special DShot command is only accepted again if it changes or after this
 * many frames.  This protects the ESC from a "stuck" command byte in a
 * fieldbus PDO (e.g. a permanently set SAVE_SETTINGS value would otherwise
 * rewrite the ESC flash every few milliseconds). */
#define DSHOT_COMMAND_HOLDOFF_FRAMES (500U)

/* ------------------------------------------------------------------------- */
/* Types                                                                     */
/* ------------------------------------------------------------------------- */
typedef struct {
    uint16_t value;         /* 11 bit DShot throttle field                    */
    uint8_t  repeat;        /* consecutive frames carrying this value         */
} dshot_prog_step_t;

typedef struct {
    /* user configuration */
    uint16_t throttle;      /* raw DShot throttle 0..2047                     */
    uint16_t cmd_value;     /* pending special command (11 bit)               */
    uint8_t  cmd_left;      /* frames left for the pending special command    */
    uint16_t cmd_holdoff;   /* frames before the same command is accepted again */
    uint16_t last_cmd;      /* last accepted special command                  */

    /* AM32 programming script (ring buffer) */
    dshot_prog_step_t prog[DSHOT_PROG_QUEUE_LEN];
    uint8_t  prog_head;
    uint8_t  prog_tail;
    uint8_t  prog_count;

    /* telemetry */
    dshot_telemetry_t tlm;

    /* RX capture state (written from the GPTMR1 ISR) */
    volatile uint8_t  rx_armed;
    volatile uint8_t  rx_edges;
    volatile uint32_t rx_last_ts;
    volatile uint32_t rx_edge_ts[DSHOT_CAPTURE_EDGES_MAX];
} dshot_channel_t;

/* ------------------------------------------------------------------------- */
/* Static state                                                              */
/* ------------------------------------------------------------------------- */
static uint8_t  s_initialized;
static uint8_t  s_enabled;
static uint16_t s_rate;
static uint16_t s_tlm_timeout_us;
static uint16_t s_update_hz;

static uint32_t s_pwm_freq_hz;
static uint32_t s_timebase_freq_hz;

/* derived timing values (recomputed by ds_apply_config) */
static uint32_t s_pwm_period_ticks;   /* command bit period T, in PWM ticks     */
static uint32_t s_pwm_reload;         /* T - 1                                  */
static uint32_t s_pwm_bit1_ticks;     /* 0.75 * T: width for a DShot '1'        */
static uint32_t s_pwm_bit0_ticks;     /* 0.375 * T: width for a DShot '0'       */
static uint32_t s_slot_ticks;         /* telemetry bit slot, GPTMR1 ticks       */
static uint32_t s_max_gap_ticks;      /* max plausible gap between two edges    */
static uint32_t s_rx_window_ticks;    /* ESC response + frame + margin          */
static uint32_t s_tx_duration_ticks;  /* nominal TX burst duration              */
static uint32_t s_update_period_ticks;/* enforced frame pacing                  */
static uint8_t  s_config_dirty;       /* timing changed, HW not updated yet     */

static volatile uint8_t s_state;      /* dshot_state_t                          */
static volatile uint32_t s_last_frame_ts;
static volatile uint32_t s_tx_start_ts;
static volatile uint32_t s_rx_start_ts;

static uint8_t  s_4way_mask;          /* bit n = ESC n line parked as GPIO      */

static dshot_channel_t s_ch[DSHOT_ESC_COUNT];

/* DMA source buffer: [bit k * 2 + esc] = pulse width << 8 (SHADOW_VAL format).
 * Must be in a non-cacheable memory region (XDMA does not snoop the CPU cache).
 * The last burst (indices 30/31) always carries the idle value 0. */
ATTR_PLACE_AT_NONCACHEABLE_BSS_WITH_ALIGNMENT(32) static uint32_t s_tx_dma_buf[DSHOT_TX_BUF_WORDS];
static dma_channel_config_t s_tx_dma_cfg;

/* GCR quintet -> nibble lookup.  Only the 16 listed entries are valid. */
static const uint8_t s_gcr_rev[32] = {
    [0x19] = 0x0U, [0x1B] = 0x1U, [0x12] = 0x2U, [0x13] = 0x3U,
    [0x1D] = 0x4U, [0x15] = 0x5U, [0x16] = 0x6U, [0x17] = 0x7U,
    [0x1A] = 0x8U, [0x09] = 0x9U, [0x0A] = 0xAU, [0x0B] = 0xBU,
    [0x1E] = 0xCU, [0x0D] = 0xDU, [0x0E] = 0xEU, [0x0F] = 0xFU,
};

static const uint32_t s_gcr_valid_mask =
    (1UL << 0x19) | (1UL << 0x1B) | (1UL << 0x12) | (1UL << 0x13) |
    (1UL << 0x1D) | (1UL << 0x15) | (1UL << 0x16) | (1UL << 0x17) |
    (1UL << 0x1A) | (1UL << 0x09) | (1UL << 0x0A) | (1UL << 0x0B) |
    (1UL << 0x1E) | (1UL << 0x0D) | (1UL << 0x0E) | (1UL << 0x0F);

/* ------------------------------------------------------------------------- */
/* Forward declarations                                                      */
/* ------------------------------------------------------------------------- */
static void ds_apply_config(void);
static void ds_apply_dirty_config(void);
static void ds_service_frame(void);
static void ds_start_frame(void);
static void ds_switch_to_rx(void);
static void ds_finish_rx(void);
static void ds_recover_tx_timeout(void);
static void ds_decode_channel(uint8_t ch);
static uint32_t ds_timebase_raw_local(void);

/* ------------------------------------------------------------------------- */
/* Small helpers                                                             */
/* ------------------------------------------------------------------------- */
static uint8_t ds_esc_cap_channel(uint8_t esc)
{
    return (esc == 0U) ? DSHOT_ESC1_CAP_CHANNEL : DSHOT_ESC2_CAP_CHANNEL;
}

static pwm_channel_t ds_esc_pwm_channel(uint8_t esc)
{
    return (esc == 0U) ? DSHOT_ESC1_PWM_CHANNEL : DSHOT_ESC2_PWM_CHANNEL;
}

static uint8_t ds_esc_pad(uint8_t esc)
{
    return (uint8_t)((esc == 0U) ? BOARD_DSHOT_ESC1_PAD : BOARD_DSHOT_ESC2_PAD);
}

static uint8_t ds_esc_pin(uint8_t esc)
{
    return (uint8_t)((esc == 0U) ? BOARD_DSHOT_ESC1_GPIO_PIN : BOARD_DSHOT_ESC2_GPIO_PIN);
}

/* ------------------------------------------------------------------------- */
/* Pad / GPIO helpers                                                        */
/* ------------------------------------------------------------------------- */
/* Pull-up so that the line stays idle HIGH while the pad is switched to the
 * TRGM input function (telemetry window) and between frames. */
static void ds_pad_set_pullup(uint8_t esc)
{
    uint32_t pad = ds_esc_pad(esc);
    uint32_t ctl = HPM_IOC->PAD[pad].PAD_CTL;

    ctl &= ~(IOC_PAD_PAD_CTL_PE_MASK | IOC_PAD_PAD_CTL_PS_MASK);
    ctl |= IOC_PAD_PAD_CTL_PE_SET(1) | IOC_PAD_PAD_CTL_PS_SET(1);
    HPM_IOC->PAD[pad].PAD_CTL = ctl;
}

static void ds_pad_set_pwm(uint8_t esc)
{
    uint8_t pad = ds_esc_pad(esc);
    uint32_t func = (esc == 0U) ? BOARD_DSHOT_ESC1_PAD_FUNC : BOARD_DSHOT_ESC2_PAD_FUNC;

    HPM_IOC->PAD[pad].FUNC_CTL = func;
}

static void ds_pad_set_trgm(uint8_t esc)
{
    uint8_t pad = ds_esc_pad(esc);
    uint32_t func = (esc == 0U) ? BOARD_DSHOT_ESC1_PAD_IN_FUNC : BOARD_DSHOT_ESC2_PAD_IN_FUNC;

    HPM_IOC->PAD[pad].FUNC_CTL = func;
}

static void ds_pad_set_gpio(uint8_t esc)
{
    uint8_t pad = ds_esc_pad(esc);

    HPM_IOC->PAD[pad].FUNC_CTL = (esc == 0U) ? IOC_PF02_FUNC_CTL_GPIO_F_02
                                             : IOC_PF03_FUNC_CTL_GPIO_F_03;
}

/* ------------------------------------------------------------------------- */
/* Protocol helpers (pure functions, no hardware access)                      */
/* ------------------------------------------------------------------------- */
uint8_t dshot_crc_bidir(uint16_t data12)
{
    uint32_t v = (uint32_t)data12 & 0x0FFFU;

    v = ~(v ^ (v >> 4) ^ (v >> 8));
    return (uint8_t)(v & 0x0FU);
}

uint16_t dshot_make_frame(uint16_t throttle, uint8_t telemetry_request)
{
    uint16_t v = (uint16_t)((((uint32_t)throttle & 0x07FFU) << 1) | (telemetry_request & 0x01U));

    return (uint16_t)((v << 4) | dshot_crc_bidir(v));
}

uint32_t dshot_telemetry_to_erpm(uint16_t telemetry12)
{
    uint32_t exponent;
    uint32_t mantissa;
    uint32_t period_us;

    if ((telemetry12 & 0x0FFFU) == DSHOT_TELEMETRY_STOPPED) {
        return 0U;
    }
    exponent = ((uint32_t)telemetry12 >> 9) & 0x07U;
    mantissa = (uint32_t)telemetry12 & 0x01FFU;
    period_us = mantissa << exponent;
    if (period_us == 0U) {
        return 0U;
    }
    return 60000000UL / period_us;
}

/* Try one slot width candidate.  Returns 1 on success and fills frame plus the
 * timing residual (sum of |interval - n*slot| over all intervals). */
static uint8_t ds_decode_candidate(const uint32_t *delta, uint8_t edge_count,
                                   uint32_t slot_ticks, dshot_rx_frame_t *frame,
                                   uint32_t *residual)
{
    uint32_t wire = 1U;     /* start bit = 1 (first slot always toggles) */
    uint32_t nbits = 1U;
    uint16_t value = 0U;
    uint16_t telemetry;
    uint16_t crc;
    uint32_t res = 0U;
    uint8_t i;
    uint8_t q;

    for (i = 0U; (uint8_t)(i + 1U) < edge_count; i++) {
        uint32_t d = delta[i + 1U] - delta[i];
        uint32_t n = (d + (slot_ticks >> 1)) / slot_ticks;   /* rounded */
        uint32_t err;

        if ((n < 1U) || (n > 3U)) {
            return 0U;
        }
        err = (d > (n * slot_ticks)) ? (d - (n * slot_ticks)) : ((n * slot_ticks) - d);
        /* allow 25% of a slot of timing error (ESC clock tolerance) */
        if ((err * 4U) > slot_ticks) {
            return 0U;
        }
        if ((nbits + n) > 21U) {
            return 0U;
        }
        /* transition after n slots: n-1 zeros followed by a one */
        wire = (wire << n) | 1U;
        nbits += n;
        res += err;
    }

    if (nbits > 21U) {
        return 0U;
    }
    /* trailing zeros of the last transitions, GCR allows at most 2 */
    if ((21U - nbits) > 3U) {
        return 0U;
    }
    wire <<= (21U - nbits);
    wire &= 0x1FFFFFU;      /* start bit + 20 GCR bits */

    for (q = 0U; q < 4U; q++) {
        uint32_t quintet = (wire >> ((3U - q) * 5U)) & 0x1FU;

        if ((s_gcr_valid_mask & (1UL << quintet)) == 0U) {
            return 0U;
        }
        value = (uint16_t)((value << 4) | s_gcr_rev[quintet]);
    }

    telemetry = (uint16_t)(value >> 4);
    crc = (uint16_t)(value & 0x0FU);
    if (dshot_crc_bidir(telemetry) != crc) {
        return 0U;
    }

    frame->value16 = value;
    frame->telemetry = telemetry;
    frame->period_us = 0U;
    frame->erpm = dshot_telemetry_to_erpm(telemetry);
    if (frame->erpm != 0U) {
        uint32_t exponent = ((uint32_t)telemetry >> 9) & 0x07U;
        uint32_t mantissa = (uint32_t)telemetry & 0x01FFU;
        uint32_t period = mantissa << exponent;

        frame->period_us = (period > 0xFFFFU) ? 0xFFFFU : (uint16_t)period;
    }
    frame->edges = edge_count;
    frame->valid = 1U;
    *residual = res;
    return 1U;
}

int dshot_decode_edges(const uint32_t *timestamps, uint8_t edge_count,
                       uint32_t slot_ticks_nominal, dshot_rx_frame_t *frame)
{
    uint32_t delta[DSHOT_CAPTURE_EDGES_MAX];
    uint32_t widths[6];
    dshot_rx_frame_t candidate;
    uint8_t width_count = 0U;
    uint8_t n;
    uint8_t i;
    uint8_t src;
    uint8_t nslot;
    uint8_t w;
    uint32_t best_residual = 0xFFFFFFFFUL;
    uint8_t found = 0U;

    if ((timestamps == NULL) || (frame == NULL) ||
        (slot_ticks_nominal == 0U) || (edge_count < 3U)) {
        return -1;
    }
    n = (edge_count > (uint8_t)DSHOT_CAPTURE_EDGES_MAX) ? (uint8_t)DSHOT_CAPTURE_EDGES_MAX : edge_count;
    for (i = 0U; i < n; i++) {
        delta[i] = timestamps[i] - timestamps[0];   /* 32 bit wrap safe */
    }

    /* Candidate slot widths are derived from the first AND the second gap:
     * the first gap may span 1..3 slots (leading GCR bits may be 0) and the
     * second gap is an independent estimate, which makes the decoder robust
     * against edge jitter and ESC clock error. */
    for (src = 0U; src < 2U; src++) {
        uint32_t gap;

        if ((src == 0U) && (n < 2U)) {
            continue;
        }
        if ((src == 1U) && (n < 3U)) {
            continue;
        }
        gap = (src == 0U) ? delta[1] : (delta[2] - delta[1]);
        for (nslot = 1U; nslot <= 3U; nslot++) {
            uint32_t width = (gap + (nslot / 2U)) / nslot;
            uint8_t duplicate = 0U;

            if (width < 2U) {
                continue;
            }
            /* reject candidates more than 33% off the nominal slot width */
            if ((width > ((slot_ticks_nominal * 4U) / 3U)) ||
                (width < ((slot_ticks_nominal * 3U) / 4U))) {
                continue;
            }
            for (w = 0U; w < width_count; w++) {
                uint32_t diff = (width > widths[w]) ? (width - widths[w]) : (widths[w] - width);
                uint32_t tolerance = widths[w] / 64U;

                if (tolerance == 0U) {
                    tolerance = 1U;
                }
                if (diff <= tolerance) {
                    duplicate = 1U;
                    break;
                }
            }
            if ((duplicate == 0U) && (width_count < (uint8_t)APP_ARRAY_SIZE(widths))) {
                widths[width_count] = width;
                width_count++;
            }
        }
    }

    for (w = 0U; w < width_count; w++) {
        uint32_t residual = 0U;

        if (ds_decode_candidate(delta, n, widths[w], &candidate, &residual) != 0U) {
            if (residual < best_residual) {
                best_residual = residual;
                *frame = candidate;
                found = 1U;
            }
        }
    }

    return (found != 0U) ? 0 : -1;
}

/* ------------------------------------------------------------------------- */
/* Time base                                                                 */
/* ------------------------------------------------------------------------- */
uint32_t dshot_timebase_raw(void)
{
    return ds_timebase_raw_local();
}

uint32_t dshot_timebase_freq_hz(void)
{
    return s_timebase_freq_hz;
}

static uint32_t ds_timebase_raw_local(void)
{
    return gptmr_channel_get_counter(DSHOT_CAP, DSHOT_TIMEBASE_CH, gptmr_counter_type_normal);
}

static uint32_t ds_us_to_ticks(uint32_t us)
{
    return (uint32_t)(((uint64_t)us * (uint64_t)s_timebase_freq_hz) / 1000000ULL);
}

/* ------------------------------------------------------------------------- */
/* Initialization                                                            */
/* ------------------------------------------------------------------------- */
static void ds_pwm_init(void)
{
    pwmv2_config_t pwm_cfg;
    pwmv2_cmp_config_t cmp_cfg;
    uint8_t esc;

    pwmv2_deinit(DSHOT_PWM);

    /* The shadow registers stay unlocked for the whole runtime: the XDMA burst
     * must be able to write SHADOW_VAL[2]/[3] with the next bit's pulse width. */
    pwmv2_shadow_register_unlock(DSHOT_PWM);
    pwmv2_shadow_unlock_bit_mask(DSHOT_PWM, 0xFFFFFFFFUL);

    memset(&cmp_cfg, 0, sizeof(cmp_cfg));
    cmp_cfg.cmp_source = cmp_value_from_shadow_val;
    cmp_cfg.cmp = 0U;
    cmp_cfg.enable_half_cmp = false;
    cmp_cfg.enable_hrcmp = false;

    /* fixed compare points a / c = 0: output high at the bit period start */
    cmp_cfg.cmp_source_index = DSHOT_SHADOW_ESC1_A;
    cmp_cfg.update_trigger = pwm_shadow_register_update_on_shlk;
    pwmv2_config_cmp(DSHOT_PWM, DSHOT_ESC1_CMP_A, &cmp_cfg);
    cmp_cfg.cmp_source_index = DSHOT_SHADOW_ESC2_A;
    pwmv2_config_cmp(DSHOT_PWM, DSHOT_ESC2_CMP_A, &cmp_cfg);

    /* variable compare points b / d: latched at the counter reload, therefore a
     * value written by the DMA in period k is applied in period k+1 */
    cmp_cfg.update_trigger = pwm_shadow_register_update_on_reload;
    cmp_cfg.cmp_source_index = DSHOT_SHADOW_ESC1_B;
    pwmv2_config_cmp(DSHOT_PWM, DSHOT_ESC1_CMP_B, &cmp_cfg);
    cmp_cfg.cmp_source_index = DSHOT_SHADOW_ESC2_B;
    pwmv2_config_cmp(DSHOT_PWM, DSHOT_ESC2_CMP_B, &cmp_cfg);

    /* dedicated compare point (>= 16) mapped to counter 1: periodic DMA request
     * a few ticks after the reload, so the value written by the DMA is stable
     * well before the next reload latches it */
    cmp_cfg.cmp = DSHOT_DMA_TRIGGER_OFFSET;
    cmp_cfg.cmp_source_index = DSHOT_SHADOW_DMA_TRIGGER;
    cmp_cfg.cmp_use_counter = DSHOT_PWM_COUNTER;
    cmp_cfg.update_trigger = pwm_shadow_register_update_on_shlk;
    pwmv2_config_cmp(DSHOT_PWM, DSHOT_DMA_TRIGGER_CMP, &cmp_cfg);

    /* counter 1: reload value from SHADOW_VAL[4], reload update, burst mode */
    pwmv2_counter_select_data_offset_from_shadow_value(DSHOT_PWM, DSHOT_PWM_COUNTER, DSHOT_SHADOW_RELOAD);
    pwmv2_set_reload_update_time(DSHOT_PWM, DSHOT_PWM_COUNTER, pwm_reload_update_on_reload);
    pwmv2_set_counter_burst(DSHOT_PWM, DSHOT_PWM_COUNTER, DSHOT_CNT_BURST);

    /* both outputs: inverted (line idles HIGH), edge aligned, no four-cmp mode */
    pwmv2_get_default_config(&pwm_cfg);
    pwm_cfg.enable_output = true;
    pwm_cfg.invert_output = true;
    pwm_cfg.enable_four_cmp = false;
    pwm_cfg.enable_async_fault = false;
    pwm_cfg.enable_sync_fault = false;
    pwm_cfg.enable_polarity_shadow = false;

    for (esc = 0U; esc < DSHOT_ESC_COUNT; esc++) {
        pwmv2_config_pwm(DSHOT_PWM, ds_esc_pwm_channel(esc), &pwm_cfg, false);
        pwmv2_channel_enable_output(DSHOT_PWM, ds_esc_pwm_channel(esc));
    }

    /* latch the initial (idle) compare values into the working registers, then
     * keep the shadow registers writable for the DMA */
    pwmv2_set_shadow_val(DSHOT_PWM, DSHOT_SHADOW_ESC1_B, 0U, 0U, false);
    pwmv2_set_shadow_val(DSHOT_PWM, DSHOT_SHADOW_ESC2_B, 0U, 0U, false);
    pwmv2_issue_shadow_register_lock_event(DSHOT_PWM);
    pwmv2_shadow_register_unlock(DSHOT_PWM);
    pwmv2_shadow_unlock_bit_mask(DSHOT_PWM, 0xFFFFFFFFUL);

    pwmv2_clear_burstend__irq_status(DSHOT_PWM, 0x0FU);
    pwmv2_enable_burstend_irq(DSHOT_PWM, DSHOT_PWM_COUNTER);
}

static void ds_dma_init(void)
{
    dma_default_channel_config(DSHOT_TX_DMA, &s_tx_dma_cfg);
    s_tx_dma_cfg.priority = DMA_CHANNEL_PRIORITY_HIGH;
    s_tx_dma_cfg.src_mode = DMA_HANDSHAKE_MODE_NORMAL;
    s_tx_dma_cfg.dst_mode = DMA_HANDSHAKE_MODE_HANDSHAKE;
    s_tx_dma_cfg.src_width = DMA_TRANSFER_WIDTH_WORD;
    s_tx_dma_cfg.dst_width = DMA_TRANSFER_WIDTH_WORD;
    s_tx_dma_cfg.src_addr_ctrl = DMA_ADDRESS_CONTROL_INCREMENT;
    s_tx_dma_cfg.dst_addr_ctrl = DMA_ADDRESS_CONTROL_FIXED;
    s_tx_dma_cfg.src_burst_size = DMA_NUM_TRANSFER_PER_BURST_2T;
    s_tx_dma_cfg.burst_opt = DMA_SRC_BURST_OPT_STANDAND_SIZE;
    s_tx_dma_cfg.handshake_opt = DMA_HANDSHAKE_OPT_ONE_BURST;
    s_tx_dma_cfg.en_src_burst_in_fixed_trans = false;
    s_tx_dma_cfg.en_dst_burst_in_fixed_trans = true;
    s_tx_dma_cfg.interrupt_mask = DMA_INTERRUPT_MASK_ALL;   /* polled / unused */
    s_tx_dma_cfg.linked_ptr = 0U;
    s_tx_dma_cfg.src_addr = (uint32_t)&s_tx_dma_buf[0];
    s_tx_dma_cfg.dst_addr = (uint32_t)&DSHOT_PWM->SHADOW_VAL[DSHOT_SHADOW_ESC1_B];
    s_tx_dma_cfg.size_in_byte = (uint32_t)sizeof(s_tx_dma_buf);

    dma_disable_channel(DSHOT_TX_DMA, DSHOT_TX_DMA_CH);

    /* PWM0 DMA request line 0 -> TRGM DMACFG_0 -> MOT_0 -> DMAMUX -> XDMA ch0 */
    trgm_dma_request_config(DSHOT_TRGM, DSHOT_TRGM_DMA_CFG, DSHOT_TRGM_DMA_SRC);
    dmamux_config(HPM_DMAMUX, DMA_SOC_CHN_TO_DMAMUX_CHN(DSHOT_TX_DMA, DSHOT_TX_DMA_CH),
                  DSHOT_DMAMUX_SRC, true);
    (void)pwmv2_enable_dma_at_compare_point(DSHOT_PWM, pwm_dma_0, DSHOT_DMA_TRIGGER_CMP);
}

static void ds_trgm_init(void)
{
    trgm_output_t cfg;

    cfg.invert = false;
    cfg.type = trgm_output_same_as_input;
    cfg.input = BOARD_DSHOT_TRGM_IN_ESC1;
    trgm_output_config(DSHOT_TRGM, BOARD_DSHOT_TRGM_OUT_ESC1, &cfg);
    cfg.input = BOARD_DSHOT_TRGM_IN_ESC2;
    trgm_output_config(DSHOT_TRGM, BOARD_DSHOT_TRGM_OUT_ESC2, &cfg);
}

static void ds_gptmr_init(void)
{
    gptmr_channel_config_t cfg;
    uint8_t esc;

    /* channel 0: free running 32 bit time base for pacing / timeouts */
    gptmr_channel_get_default_config(DSHOT_CAP, &cfg);
    cfg.mode = gptmr_work_mode_no_capture;
    cfg.dma_request_event = gptmr_dma_request_disabled;
    cfg.reload = 0xFFFFFFFFUL;
    cfg.cmp[0] = 0xFFFFFFFFUL;
    cfg.cmp[1] = 0xFFFFFFFFUL;
    cfg.enable_cmp_output = false;
    cfg.enable_sync_follow_previous_channel = false;
    (void)gptmr_channel_config(DSHOT_CAP, DSHOT_TIMEBASE_CH, &cfg, false);
    gptmr_channel_reset_count(DSHOT_CAP, DSHOT_TIMEBASE_CH);
    gptmr_start_counter(DSHOT_CAP, DSHOT_TIMEBASE_CH);

    /* channel 2/3: capture both edges of the ESC telemetry line */
    for (esc = 0U; esc < DSHOT_ESC_COUNT; esc++) {
        uint8_t cap_ch = ds_esc_cap_channel(esc);

        gptmr_channel_get_default_config(DSHOT_CAP, &cfg);
        cfg.mode = gptmr_work_mode_capture_at_both_edge;
        cfg.dma_request_event = gptmr_dma_request_disabled;
        cfg.reload = 0xFFFFFFFFUL;      /* full 32 bit free running counter */
        cfg.cmp[0] = 0xFFFFFFFFUL;
        cfg.cmp[1] = 0xFFFFFFFFUL;
        cfg.enable_cmp_output = false;
        cfg.enable_software_sync = false;
        (void)gptmr_channel_config(DSHOT_CAP, cap_ch, &cfg, false);
        gptmr_channel_reset_count(DSHOT_CAP, cap_ch);
        gptmr_start_counter(DSHOT_CAP, cap_ch);
        gptmr_clear_status(DSHOT_CAP, GPTMR_CH_CAP_STAT_MASK(cap_ch));
    }

    gptmr_enable_irq(DSHOT_CAP, GPTMR_CH_CAP_IRQ_MASK(DSHOT_ESC1_CAP_CHANNEL) |
                               GPTMR_CH_CAP_IRQ_MASK(DSHOT_ESC2_CAP_CHANNEL));
}

void dshot_init(void)
{
    uint8_t esc;

    memset(s_ch, 0, sizeof(s_ch));
    memset(s_tx_dma_buf, 0, sizeof(s_tx_dma_buf));

    s_enabled = 0U;
    s_rate = DSHOT_RATE_DEFAULT;
    s_tlm_timeout_us = DSHOT_TLM_TIMEOUT_US_DEF;
    s_update_hz = DSHOT_UPDATE_HZ_DEFAULT;
    s_4way_mask = 0U;
    s_state = DSHOT_STATE_UNINIT;
    s_config_dirty = 0U;
    s_last_frame_ts = 0U;

    for (esc = 0U; esc < DSHOT_ESC_COUNT; esc++) {
        s_ch[esc].tlm.state = DSHOT_STATE_UNINIT;
    }

    /* clocks: PWM0 and GPTMR1 are on group 0 (see the board clock package) */
    clock_add_to_group(DSHOT_PWM_CLK_NAME, 0);
    s_pwm_freq_hz = clock_get_frequency(DSHOT_PWM_CLK_NAME);
    clock_add_to_group(DSHOT_CAP_CLK_NAME, 0);
    s_timebase_freq_hz = clock_get_frequency(DSHOT_CAP_CLK_NAME);
    if (s_pwm_freq_hz == 0U) {
        s_pwm_freq_hz = 100000000UL;
    }
    if (s_timebase_freq_hz == 0U) {
        s_timebase_freq_hz = 100000000UL;
    }

    for (esc = 0U; esc < DSHOT_ESC_COUNT; esc++) {
        ds_pad_set_pullup(esc);
        ds_pad_set_pwm(esc);
    }

    ds_trgm_init();
    ds_gptmr_init();
    ds_pwm_init();
    ds_dma_init();

    ds_apply_config();
    ds_apply_dirty_config();

    pwmv2_clear_burstend__irq_status(DSHOT_PWM, 0x0FU);
    intc_m_enable_irq_with_priority(DSHOT_PWM_IRQ, 1);
    intc_m_enable_irq_with_priority(DSHOT_CAP_IRQ, 1);

    s_initialized = 1U;
    s_state = DSHOT_STATE_IDLE;
    for (esc = 0U; esc < DSHOT_ESC_COUNT; esc++) {
        s_ch[esc].tlm.state = DSHOT_STATE_IDLE;
    }
}

/* ------------------------------------------------------------------------- */
/* Configuration                                                             */
/* ------------------------------------------------------------------------- */
static void ds_apply_config(void)
{
    uint32_t rate_hz;
    uint32_t period;
    uint32_t min_period;
    uint64_t tmp;

    if ((s_pwm_freq_hz == 0U) || (s_timebase_freq_hz == 0U) || (s_rate == 0U) || (s_update_hz == 0U)) {
        return;
    }

    rate_hz = (uint32_t)s_rate * 1000UL;
    period = (s_pwm_freq_hz + (rate_hz / 2U)) / rate_hz;   /* PWM ticks per bit */
    if (period < 16U) {
        period = 16U;
    }
    if (period > 0x00FFFFFFUL) {
        period = 0x00FFFFFFUL;
    }
    s_pwm_period_ticks = period;
    s_pwm_reload = period - 1U;
    s_pwm_bit1_ticks = (period * 3U) / 4U;    /* 75% of T */
    s_pwm_bit0_ticks = (period * 3U) / 8U;    /* 37.5% of T */
    if (s_pwm_bit0_ticks < 2U) {
        s_pwm_bit0_ticks = 2U;
    }
    if (s_pwm_bit1_ticks <= s_pwm_bit0_ticks) {
        s_pwm_bit1_ticks = s_pwm_bit0_ticks + 1U;
    }

    /* telemetry bit slot is 5/4 of the command bit period */
    s_slot_ticks = (uint32_t)((((uint64_t)s_timebase_freq_hz * 4ULL) +
                               (uint64_t)((5UL * rate_hz) / 2UL)) /
                              (uint64_t)(5UL * rate_hz));
    if (s_slot_ticks < 4U) {
        s_slot_ticks = 4U;
    }
    s_max_gap_ticks = s_slot_ticks * 6UL;

    /* TX burst duration in GPTMR1 ticks */
    tmp = ((uint64_t)DSHOT_TX_PERIODS * (uint64_t)s_pwm_period_ticks *
           (uint64_t)s_timebase_freq_hz) / (uint64_t)s_pwm_freq_hz;
    s_tx_duration_ticks = (uint32_t)tmp;
    if (s_tx_duration_ticks == 0U) {
        s_tx_duration_ticks = 1U;
    }

    /* RX window: worst case ESC response gap + 21 telemetry bits + margin */
    s_rx_window_ticks = ds_us_to_ticks(s_tlm_timeout_us) + (24U * s_slot_ticks);

    /* frame pacing: requested update rate, but one TX+RX cycle must fit */
    min_period = s_tx_duration_ticks + s_rx_window_ticks + (s_timebase_freq_hz / 200U);
    tmp = (uint64_t)s_timebase_freq_hz / (uint64_t)s_update_hz;
    s_update_period_ticks = (uint32_t)tmp;
    if (s_update_period_ticks < min_period) {
        s_update_period_ticks = min_period;
    }

    s_config_dirty = 1U;
}

/* Update rate dependent hardware values; only called while the driver is idle
 * (a running burst must not be reconfigured). */
static void ds_apply_dirty_config(void)
{
    if (s_config_dirty == 0U) {
        return;
    }
    pwmv2_shadow_register_unlock(DSHOT_PWM);
    pwmv2_shadow_unlock_bit_mask(DSHOT_PWM, 0xFFFFFFFFUL);
    pwmv2_set_shadow_val(DSHOT_PWM, DSHOT_SHADOW_RELOAD, s_pwm_reload, 0U, false);
    pwmv2_set_shadow_val(DSHOT_PWM, DSHOT_SHADOW_DMA_TRIGGER, DSHOT_DMA_TRIGGER_OFFSET, 0U, false);
    pwmv2_issue_shadow_register_lock_event(DSHOT_PWM);
    pwmv2_shadow_register_unlock(DSHOT_PWM);
    pwmv2_shadow_unlock_bit_mask(DSHOT_PWM, 0xFFFFFFFFUL);
    s_config_dirty = 0U;
}

void dshot_set_rate(uint16_t rate)
{
    switch (rate) {
    case 150U:
    case 300U:
    case 600U:
    case 1200U:
        s_rate = rate;
        break;
    default:
        s_rate = DSHOT_RATE_DEFAULT;
        break;
    }
    if (s_initialized != 0U) {
        ds_apply_config();
    }
}

uint16_t dshot_get_rate(void)
{
    return s_rate;
}

void dshot_set_tlm_timeout_us(uint16_t timeout_us)
{
    if (timeout_us < 20U) {
        timeout_us = 20U;
    }
    if (timeout_us > 200U) {
        timeout_us = 200U;
    }
    s_tlm_timeout_us = timeout_us;
    if (s_initialized != 0U) {
        ds_apply_config();
    }
}

uint16_t dshot_get_tlm_timeout_us(void)
{
    return s_tlm_timeout_us;
}

void dshot_set_update_hz(uint16_t hz)
{
    if (hz < 100U) {
        hz = 100U;
    }
    s_update_hz = hz;
    if (s_initialized != 0U) {
        ds_apply_config();
    }
}

uint32_t dshot_get_telemetry_slot_ticks(void)
{
    return s_slot_ticks;
}

uint16_t dshot_get_bit_period_ns(void)
{
    uint32_t rate_hz = (uint32_t)s_rate * 1000UL;

    if (rate_hz == 0U) {
        return 0U;
    }
    return (uint16_t)(1000000000UL / rate_hz);
}

/* ------------------------------------------------------------------------- */
/* Frame generation                                                          */
/* ------------------------------------------------------------------------- */
/* Next 11 bit throttle field for one ESC.  Priority:
 * AM32 programming script > pending special command > throttle. */
static uint16_t ds_next_value11(uint8_t ch)
{
    dshot_channel_t *c = &s_ch[ch];

    if (c->prog_count != 0U) {
        dshot_prog_step_t *step = &c->prog[c->prog_head];
        uint16_t value = step->value;

        if (step->repeat > 1U) {
            step->repeat--;
        } else {
            c->prog_head = (uint8_t)((c->prog_head + 1U) % DSHOT_PROG_QUEUE_LEN);
            c->prog_count--;
        }
        return value;
    }
    if (c->cmd_left != 0U) {
        c->cmd_left--;
        return c->cmd_value;
    }
    if (s_enabled == 0U) {
        return (uint16_t)DSHOT_CMD_MOTOR_STOP;
    }
    return c->throttle;
}

/* Accept one special command.  A command that is still pending is not
 * restarted, and the same command value is rate limited so a stuck fieldbus
 * command byte cannot loop the ESC command (see DSHOT_COMMAND_HOLDOFF_FRAMES). */
static void ds_queue_command(uint8_t ch, uint16_t cmd)
{
    dshot_channel_t *c = &s_ch[ch];

    if (c->prog_count != 0U) {
        return;                         /* AM32 script has priority, no mixing */
    }
    if (c->cmd_left != 0U) {
        return;                         /* a command sequence is still running */
    }
    if ((c->cmd_holdoff != 0U) && (c->last_cmd == cmd)) {
        return;                         /* same command, rate limited          */
    }
    c->cmd_value = cmd;
    c->cmd_left = (uint8_t)DSHOT_COMMAND_REPEAT_DEFAULT;
    c->last_cmd = cmd;
    c->cmd_holdoff = (uint16_t)DSHOT_COMMAND_HOLDOFF_FRAMES;
}

static void ds_build_tx_buffer(void)
{
    uint8_t esc;
    uint8_t i;

    for (esc = 0U; esc < DSHOT_ESC_COUNT; esc++) {
        uint16_t value11;
        uint16_t frame;
        uint32_t width0;
        uint8_t bit;

        if (s_ch[esc].cmd_holdoff != 0U) {
            s_ch[esc].cmd_holdoff--;
        }

        value11 = ds_next_value11(esc);
        frame = dshot_make_frame(value11, 1U);

        /* bit 0 (MSB) is preloaded into the compare shadow by the CPU */
        width0 = ((frame & 0x8000U) != 0U) ? s_pwm_bit1_ticks : s_pwm_bit0_ticks;
        pwmv2_set_shadow_val(DSHOT_PWM, (uint8_t)(DSHOT_SHADOW_ESC1_B + esc), width0, 0U, false);

        /* buffer entry k carries bit (k + 1): burst k is requested in bit
         * period k and latched for bit period k + 1 */
        for (bit = 0U; bit < (uint8_t)(DSHOT_BITS - 1U); bit++) {
            uint16_t mask = (uint16_t)(0x4000U >> bit);
            uint32_t width = ((frame & mask) != 0U) ? s_pwm_bit1_ticks : s_pwm_bit0_ticks;

            s_tx_dma_buf[(2U * (uint8_t)bit) + esc] = (width << 8);
        }
    }
    /* the last (16th) burst only programs the idle value 0 */
    for (i = (uint8_t)(DSHOT_BITS - 1U) * DSHOT_TX_BURST_WORDS; i < DSHOT_TX_BUF_WORDS; i++) {
        s_tx_dma_buf[i] = 0U;
    }
}

static void ds_start_frame(void)
{
    uint32_t mstatus;

    ds_build_tx_buffer();

    mstatus = disable_global_irq(CSR_MSTATUS_MIE_MASK);

    dma_disable_channel(DSHOT_TX_DMA, DSHOT_TX_DMA_CH);
    s_tx_dma_cfg.src_addr = (uint32_t)&s_tx_dma_buf[0];
    (void)dma_setup_channel(DSHOT_TX_DMA, DSHOT_TX_DMA_CH, &s_tx_dma_cfg, false);

    pwmv2_clear_burstend__irq_status(DSHOT_PWM, 0x0FU);
    pwmv2_reset_counter(DSHOT_PWM, DSHOT_PWM_COUNTER);
    pwmv2_enable_counter(DSHOT_PWM, DSHOT_PWM_COUNTER);
    /* the output starts at the next reload; that reload also latches the compare
     * values (bit 0), afterwards the first DMA burst programs bit 1 */
    pwmv2_start_pwm_output(DSHOT_PWM, DSHOT_PWM_COUNTER);
    dma_enable_channel(DSHOT_TX_DMA, DSHOT_TX_DMA_CH);

    s_last_frame_ts = ds_timebase_raw_local();
    s_tx_start_ts = s_last_frame_ts;
    s_state = DSHOT_STATE_TX;

    restore_global_irq(mstatus);
}

/* ------------------------------------------------------------------------- */
/* Interrupts                                                                */
/* ------------------------------------------------------------------------- */
SDK_DECLARE_EXT_ISR_M(DSHOT_PWM_IRQ, dshot_pwm_isr)
void dshot_pwm_isr(void)
{
    uint32_t mask = (1UL << (uint32_t)DSHOT_PWM_COUNTER);

    if ((pwmv2_get_burstend_irq_status(DSHOT_PWM) & mask) != 0U) {
        pwmv2_clear_burstend__irq_status(DSHOT_PWM, mask);
        pwmv2_disable_counter(DSHOT_PWM, DSHOT_PWM_COUNTER);
        dma_disable_channel(DSHOT_TX_DMA, DSHOT_TX_DMA_CH);
        ds_switch_to_rx();
    } else {
        pwmv2_clear_burstend__irq_status(DSHOT_PWM, 0x0FU);
        s_ch[0].tlm.state = DSHOT_STATE_ERROR;
    }
}

SDK_DECLARE_EXT_ISR_M(DSHOT_CAP_IRQ, dshot_capture_isr)
void dshot_capture_isr(void)
{
    uint32_t status = gptmr_get_status(DSHOT_CAP);
    uint8_t esc;

    for (esc = 0U; esc < DSHOT_ESC_COUNT; esc++) {
        uint8_t cap_ch = ds_esc_cap_channel(esc);
        dshot_channel_t *c = &s_ch[esc];

        if ((status & GPTMR_CH_CAP_STAT_MASK(cap_ch)) == 0U) {
            continue;
        }
        gptmr_clear_status(DSHOT_CAP, GPTMR_CH_CAP_STAT_MASK(cap_ch));
        if (c->rx_armed == 0U) {
            continue;
        }

        /* "capture at both edges": CAPPOS holds the latest rising edge, CAPNEG
         * the latest falling edge.  The absolute edge direction is not needed
         * for the differential decode, only the time order, so both registers
         * are checked against the last accepted timestamp and whatever is
         * plausible is appended (also covers a missed ISR entry). */
        {
            uint32_t pos = gptmr_channel_get_counter(DSHOT_CAP, cap_ch, gptmr_counter_type_rising_edge);
            uint32_t neg = gptmr_channel_get_counter(DSHOT_CAP, cap_ch, gptmr_counter_type_falling_edge);
            uint32_t last = c->rx_last_ts;
            uint32_t d_pos = pos - last;
            uint32_t d_neg;
            uint8_t n = c->rx_edges;

            if ((d_pos != 0U) && (d_pos <= s_max_gap_ticks) &&
                (n < (uint8_t)DSHOT_CAPTURE_EDGES_MAX)) {
                c->rx_edge_ts[n] = pos;
                n++;
                c->rx_last_ts = pos;
                last = pos;
            }
            d_neg = neg - last;
            if ((d_neg != 0U) && (d_neg <= s_max_gap_ticks) &&
                (n < (uint8_t)DSHOT_CAPTURE_EDGES_MAX)) {
                c->rx_edge_ts[n] = neg;
                n++;
                c->rx_last_ts = neg;
            }
            c->rx_edges = n;
        }
    }
}

/* ------------------------------------------------------------------------- */
/* RX handling                                                               */
/* ------------------------------------------------------------------------- */
static void ds_switch_to_rx(void)
{
    uint8_t esc;

    if (s_state == DSHOT_STATE_RX) {
        return;
    }

    for (esc = 0U; esc < DSHOT_ESC_COUNT; esc++) {
        uint8_t cap_ch = ds_esc_cap_channel(esc);
        dshot_channel_t *c = &s_ch[esc];

        c->rx_armed = 0U;
        c->rx_edges = 0U;
        ds_pad_set_trgm(esc);
        c->rx_last_ts = gptmr_channel_get_counter(DSHOT_CAP, cap_ch, gptmr_counter_type_normal);
        gptmr_clear_status(DSHOT_CAP, GPTMR_CH_CAP_STAT_MASK(cap_ch));
        c->rx_armed = 1U;
    }
    s_rx_start_ts = ds_timebase_raw_local();
    s_state = DSHOT_STATE_RX;
}

static void ds_decode_channel(uint8_t ch)
{
    dshot_channel_t *c = &s_ch[ch];
    dshot_telemetry_t *tlm = &c->tlm;
    uint8_t edges = c->rx_edges;
    dshot_rx_frame_t frame;
    uint32_t ts[DSHOT_CAPTURE_EDGES_MAX];
    uint8_t i;

    tlm->state = s_state;

    if (edges >= 3U) {
        for (i = 0U; i < edges; i++) {
            ts[i] = c->rx_edge_ts[i];
        }
        if (dshot_decode_edges(ts, edges, s_slot_ticks, &frame) == 0) {
            tlm->valid = 1U;
            tlm->erpm = frame.erpm;
            tlm->period_us = (frame.erpm == 0U) ? 0U : frame.period_us;
            tlm->crc_errors = 0U;
            tlm->ok_count++;
            tlm->last_ok_tick_ms = g_app_tick_ms;
            return;
        }
    }

    /* no frame / truncated frame / bad GCR or CRC */
    tlm->valid = 0U;
    if (edges >= 3U) {
        tlm->err_count++;
        if (tlm->crc_errors < 0xFFFFU) {
            tlm->crc_errors++;
        }
    } else {
        tlm->timeout_count++;
    }
}

static void ds_finish_rx(void)
{
    uint8_t esc;

    for (esc = 0U; esc < DSHOT_ESC_COUNT; esc++) {
        s_ch[esc].rx_armed = 0U;
        ds_decode_channel(esc);
        /* hand the line back to the PWM (actively drives idle HIGH) */
        if ((s_4way_mask & (uint8_t)(1UL << esc)) == 0U) {
            ds_pad_set_pwm(esc);
        }
    }
    s_state = DSHOT_STATE_IDLE;
}

static void ds_recover_tx_timeout(void)
{
    uint8_t esc;

    pwmv2_disable_counter(DSHOT_PWM, DSHOT_PWM_COUNTER);
    pwmv2_clear_burstend__irq_status(DSHOT_PWM, 0x0FU);
    dma_disable_channel(DSHOT_TX_DMA, DSHOT_TX_DMA_CH);

    for (esc = 0U; esc < DSHOT_ESC_COUNT; esc++) {
        s_ch[esc].rx_armed = 0U;
        s_ch[esc].tlm.valid = 0U;
        s_ch[esc].tlm.timeout_count++;
        if ((s_4way_mask & (uint8_t)(1UL << esc)) == 0U) {
            ds_pad_set_pwm(esc);
        }
    }
    s_state = DSHOT_STATE_IDLE;
}

/* Frame level state machine that does not need the main loop: RX window end
 * and TX watchdog.  Also used while waiting for a frame to finish before the
 * AM32 bridge parks the line. */
static void ds_service_frame(void)
{
    uint32_t now = ds_timebase_raw_local();

    switch (s_state) {
    case DSHOT_STATE_RX:
        if ((uint32_t)(now - s_rx_start_ts) >= s_rx_window_ticks) {
            ds_finish_rx();
        }
        break;
    case DSHOT_STATE_TX:
        if ((uint32_t)(now - s_tx_start_ts) >
            (s_tx_duration_ticks * DSHOT_TX_WATCHDOG_FRAMES)) {
            ds_recover_tx_timeout();
        }
        break;
    default:
        break;
    }
}

/* ------------------------------------------------------------------------- */
/* Public control API                                                        */
/* ------------------------------------------------------------------------- */
void dshot_enable(uint8_t enable)
{
    s_enabled = (enable != 0U) ? 1U : 0U;
}

uint8_t dshot_get_channel_count(void)
{
    return (uint8_t)DSHOT_ESC_COUNT;
}

void dshot_set_throttle(uint8_t ch, uint16_t throttle)
{
    if (ch >= DSHOT_ESC_COUNT) {
        return;
    }
    if (throttle > DSHOT_THROTTLE_MAX) {
        throttle = DSHOT_THROTTLE_MAX;
    }
    s_ch[ch].throttle = throttle;
}

void dshot_send_command(uint8_t ch, uint16_t cmd)
{
    uint8_t i;

    if (cmd > DSHOT_THROTTLE_MAX) {
        return;
    }
    if (ch == 0xFFU) {
        for (i = 0U; i < DSHOT_ESC_COUNT; i++) {
            ds_queue_command(i, cmd);
        }
    } else if (ch < DSHOT_ESC_COUNT) {
        ds_queue_command(ch, cmd);
    } else {
        /* invalid channel: ignore */
    }
}

uint16_t dshot_us_to_throttle(uint16_t us)
{
    uint32_t span_in = (uint32_t)APP_CFG_DSHOT_CMD_MAX_US - (uint32_t)APP_CFG_DSHOT_CMD_MIN_US;
    uint32_t span_out = (uint32_t)DSHOT_THROTTLE_MAX - (uint32_t)APP_CFG_DSHOT_MIN_THROTTLE;
    uint32_t value;

    if (us <= APP_CFG_DSHOT_CMD_MIN_US) {
        return (uint16_t)APP_CFG_DSHOT_MIN_THROTTLE;
    }
    if (us >= APP_CFG_DSHOT_CMD_MAX_US) {
        return (uint16_t)DSHOT_THROTTLE_MAX;
    }
    value = ((uint32_t)(us - APP_CFG_DSHOT_CMD_MIN_US) * span_out) / span_in;
    value += APP_CFG_DSHOT_MIN_THROTTLE;
    return (uint16_t)value;
}

void dshot_get_telemetry(uint8_t ch, dshot_telemetry_t *tlm)
{
    if ((tlm == NULL) || (ch >= DSHOT_ESC_COUNT)) {
        return;
    }
    *tlm = s_ch[ch].tlm;
    tlm->state = s_state;
}

uint8_t dshot_is_4way_active(void)
{
    return (s_4way_mask != 0U) ? 1U : 0U;
}

void dshot_task(void)
{
    uint32_t now;

    if (s_initialized == 0U) {
        return;
    }

    ds_service_frame();

    if ((s_state == DSHOT_STATE_IDLE) && (s_config_dirty != 0U)) {
        ds_apply_dirty_config();
    }

    if ((s_state != DSHOT_STATE_IDLE) || (s_4way_mask != 0U)) {
        return;
    }

    now = ds_timebase_raw_local();
    if ((uint32_t)(now - s_last_frame_ts) >= s_update_period_ticks) {
        ds_start_frame();
    }
}

/* ------------------------------------------------------------------------- */
/* AM32 DShot programming mode (write-only path)                             */
/* ------------------------------------------------------------------------- */
static uint8_t ds_prog_push(uint8_t esc, uint16_t value, uint8_t repeat)
{
    dshot_channel_t *c = &s_ch[esc];

    if (c->prog_count >= (uint8_t)DSHOT_PROG_QUEUE_LEN) {
        return 0U;
    }
    c->prog[c->prog_tail].value = value;
    c->prog[c->prog_tail].repeat = repeat;
    c->prog_tail = (uint8_t)((c->prog_tail + 1U) % DSHOT_PROG_QUEUE_LEN);
    c->prog_count++;
    return 1U;
}

int dshot_prog_write_byte(uint8_t esc, uint8_t addr, uint8_t value)
{
    if (esc >= DSHOT_ESC_COUNT) {
        return -1;
    }
    /* one EEPROM byte needs 4 queue entries: 36 x6, address, value, 37 */
    if ((uint8_t)(DSHOT_PROG_QUEUE_LEN - s_ch[esc].prog_count) < 4U) {
        return -1;
    }
    (void)ds_prog_push(esc, DSHOT_CMD_AM32_ENTER_PROG, DSHOT_COMMAND_REPEAT_DEFAULT);
    (void)ds_prog_push(esc, (uint16_t)addr, 1U);
    (void)ds_prog_push(esc, (uint16_t)value, 1U);
    (void)ds_prog_push(esc, DSHOT_CMD_AM32_PROG_COMMIT, 1U);
    return 0;
}

int dshot_prog_save(uint8_t esc)
{
    if (esc >= DSHOT_ESC_COUNT) {
        return -1;
    }
    if ((uint8_t)(DSHOT_PROG_QUEUE_LEN - s_ch[esc].prog_count) < 1U) {
        return -1;
    }
    (void)ds_prog_push(esc, DSHOT_CMD_SAVE_SETTINGS, DSHOT_COMMAND_REPEAT_DEFAULT);
    return 0;
}

uint8_t dshot_prog_busy(uint8_t esc)
{
    if (esc >= DSHOT_ESC_COUNT) {
        return 0U;
    }
    return (s_ch[esc].prog_count != 0U) ? 1U : 0U;
}

void dshot_prog_abort(uint8_t esc)
{
    if (esc >= DSHOT_ESC_COUNT) {
        return;
    }
    s_ch[esc].prog_head = 0U;
    s_ch[esc].prog_tail = 0U;
    s_ch[esc].prog_count = 0U;
}

/* ------------------------------------------------------------------------- */
/* Line parking for the AM32 4-way bridge                                    */
/* ------------------------------------------------------------------------- */
void dshot_line_set_gpio(uint8_t esc, uint8_t output, uint8_t level)
{
    uint8_t pin;

    if (esc >= DSHOT_ESC_COUNT) {
        return;
    }
    pin = ds_esc_pin(esc);
    ds_pad_set_gpio(esc);
    if (output != 0U) {
        gpio_set_pin_output(HPM_GPIO0, BOARD_DSHOT_GPIO_PORT_INDEX, pin);
        gpio_write_pin(HPM_GPIO0, BOARD_DSHOT_GPIO_PORT_INDEX, pin, (level != 0U) ? 1 : 0);
    } else {
        gpio_set_pin_input(HPM_GPIO0, BOARD_DSHOT_GPIO_PORT_INDEX, pin);
    }
}

uint8_t dshot_line_read_gpio(uint8_t esc)
{
    if (esc >= DSHOT_ESC_COUNT) {
        return 1U;
    }
    return (uint8_t)gpio_read_pin(HPM_GPIO0, BOARD_DSHOT_GPIO_PORT_INDEX, ds_esc_pin(esc));
}

int dshot_line_park(uint8_t esc)
{
    uint32_t start;
    uint32_t limit;

    if (esc >= DSHOT_ESC_COUNT) {
        return AM32_4WAY_ERR_ARG;
    }
    if ((s_4way_mask != 0U) && (s_4way_mask != (uint8_t)(1UL << esc))) {
        return AM32_4WAY_ERR_BUSY;
    }

    s_4way_mask |= (uint8_t)(1UL << esc);

    /* wait for an in-flight frame (bounded to well below the AM32 timeout) */
    start = ds_timebase_raw_local();
    limit = s_update_period_ticks + s_rx_window_ticks + (s_timebase_freq_hz / 1000U);
    while (s_state != DSHOT_STATE_IDLE) {
        ds_service_frame();
        if ((uint32_t)(ds_timebase_raw_local() - start) > limit) {
            s_4way_mask &= (uint8_t)~(1UL << esc);
            return AM32_4WAY_ERR_NOT_READY;
        }
    }

    pwmv2_disable_counter(DSHOT_PWM, DSHOT_PWM_COUNTER);
    pwmv2_channel_disable_output(DSHOT_PWM, ds_esc_pwm_channel(esc));
    dshot_line_set_gpio(esc, 1U, 1U);     /* idle high (UART idle level) */

    s_ch[esc].rx_armed = 0U;
    s_ch[esc].cmd_left = 0U;
    dshot_prog_abort(esc);
    return 0;
}

void dshot_line_release(uint8_t esc)
{
    if (esc >= DSHOT_ESC_COUNT) {
        return;
    }
    dshot_line_set_gpio(esc, 1U, 1U);
    pwmv2_channel_enable_output(DSHOT_PWM, ds_esc_pwm_channel(esc));
    ds_pad_set_pwm(esc);

    s_4way_mask &= (uint8_t)~(1UL << esc);
    s_ch[esc].rx_armed = 0U;
    if (s_4way_mask == 0U) {
        s_last_frame_ts = ds_timebase_raw_local();
        s_state = DSHOT_STATE_IDLE;
    }
}
