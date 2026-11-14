/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * AM32 / BLHeli 4-way interface bridge (bit-banged 19200 8N1 half duplex)
 *
 * ===========================================================================
 * Architecture
 * ===========================================================================
 * The PC host tool is the master of the 4-way protocol: it sends the complete
 * request frame as raw bytes and the ESC answers with a response frame.  The
 * board is a transparent byte pipe between USB CDC and the ESC signal line:
 *
 *   PC  -- HOST_CMD_AM32_BEGIN(esc) --> board parks the ESC line as GPIO
 *   PC  -- HOST_CMD_AM32_XFER(esc, request bytes) --> am32_4way_xfer()
 *              board sends the bytes at 19200 8N1 (idle high, LSB first) and
 *              then clocks in the ESC answer until the line is idle again
 *   PC  -- HOST_CMD_AM32_END(esc) --> board gives the line back to DShot
 *
 * The board does NOT interpret the 4-way frame container.  The wire protocol
 * (from the BLHeli/AM32 documentation and the AVRootloader bootloader of
 * BLHeli_32/AM32) is:
 *   - 19200 baud, 8 data bits, no parity, 1 stop bit, LSB first, idle HIGH
 *   - start bit = 0, stop bit = 1
 *   - the ESC receives a request and answers after the last request byte
 *   - response length varies with the command (an EEPROM read answers with
 *     ~197 raw bytes, simple commands only ACK with 1 byte), so the receive
 *     path stops as soon as the line has been idle for a few byte times
 *   - worst case blocking time is bounded by AM32_4WAY_TOTAL_TIMEOUT_US
 *     (~150 ms; a 192 byte EEPROM response takes ~103 ms at 19200 baud)
 *
 * The ESC must be in bootloader / 4-way mode before the frame container is
 * useful.  Which bytes make the ESC enter that mode is owned by the PC tool
 * (the BLHeli/AM32 escape sequence) - the board only moves bytes.
 *
 * ===========================================================================
 * Required ESC / system state
 * ===========================================================================
 * - The DShot state machine is paused while the bridge is active
 *   (dshot_is_4way_active() == 1).  DShot frames stop for ALL ESCs during a
 *   4-way session: the other ESC loses its signal after ~0.5 s and disarms.
 *   That is acceptable because the 4-way interface is a maintenance function;
 *   after AM32_END the normal DShot traffic re-arms both ESCs (throttle 0).
 * - The AM32 ESC must be armed but the motor must be stopped when talking to
 *   the *application* firmware (the DShot programming path in dshot.h).  The
 *   4-way path talks to the bootloader, which is only reachable through the
 *   escape sequence from the PC tool; see docs/DSHOT_AM32.md.
 * - The caller (host protocol dispatcher) must reject BEGIN while EtherCAT is
 *   in OP: the motor outputs must never move while the bridge owns the line.
 *
 * ===========================================================================
 * Electrical details
 * ===========================================================================
 * - TX: the pad is configured as GPIO output; a byte is emitted bit by bit with
 *   the GPTMR1 channel0 free running counter as time base.  The line is
 *   released (GPIO input with pull-up, idle high) after the last stop bit.
 * - RX: the pad stays a GPIO input with pull-up; the start bit is detected by
 *   polling for a falling edge, the ten bit times are sampled at 0.75T, 1.75T,
 *   ... 9.75T after the detected edge (middle of each bit cell) and the start /
 *   stop bit levels are validated.
 * - The bit timing uses the same time base as the DShot capture, so no extra
 *   timer is needed.  Interrupt latency is irrelevant for 19200 baud because
 *   the samples are taken in the middle of a 52 us bit cell.
 */

#include <stdint.h>
#include <stddef.h>

#include "board.h"
#include "hpm_common.h"
#include "dshot.h"

/* ------------------------------------------------------------------------- */
/* Protocol constants                                                        */
/* ------------------------------------------------------------------------- */
#define AM32_4WAY_BAUD              (19200UL)
#define AM32_4WAY_BIT_TIME_US       (52UL)     /* 1/19200 = 52.08us            */
/* stop receiving when the line has been idle for this long after the last byte
 * (the ESC sends its response bytes back to back, so this is very conservative)
 * -> short ACK frames return quickly instead of waiting for the full capacity */
#define AM32_4WAY_GAP_TIMEOUT_US    (1500UL)
/* hard upper bound for one request/response exchange */
#define AM32_4WAY_TOTAL_TIMEOUT_US  (150000UL)
/* the ACK of a flash erase/program can take seconds - not supported here, see
 * docs/DSHOT_AM32.md (do not use the bridge for ESC firmware updates) */
#define AM32_4WAY_MAX_RESPONSE      (208U)

/* ------------------------------------------------------------------------- */
/* Static state                                                              */
/* ------------------------------------------------------------------------- */
static uint8_t  s_state = AM32_4WAY_STATE_IDLE;
static uint8_t  s_active_esc = 0xFFU;
static int32_t  s_last_error;
static uint32_t s_bit_ticks;
static uint32_t s_gap_timeout_ticks;
static uint32_t s_total_timeout_ticks;

/* ------------------------------------------------------------------------- */
/* Low level bit-bang helpers                                                */
/* ------------------------------------------------------------------------- */
static void am32_line_out(uint8_t esc, uint8_t level)
{
    dshot_line_set_gpio(esc, 1U, level);
}

static void am32_line_in(uint8_t esc)
{
    dshot_line_set_gpio(esc, 0U, 1U);
}

static uint8_t am32_line_read(uint8_t esc)
{
    return dshot_line_read_gpio(esc);
}

/* Wait until the free running time base reached "target" (32 bit wrap safe). */
static void am32_wait_until(uint32_t target)
{
    while ((int32_t)(dshot_timebase_raw() - target) < 0) {
        /* busy wait: 19200 baud bit cells are 52us long, polling is accurate
         * enough and there is no other use for the CPU in this maintenance path */
    }
}

static uint32_t am32_us_to_ticks(uint32_t us)
{
    uint32_t freq = dshot_timebase_freq_hz();

    return (uint32_t)(((uint64_t)us * (uint64_t)freq) / 1000000ULL);
}

/* Send one byte: start bit, 8 data bits LSB first, stop bit.
 * t_next is the absolute time of the next start bit (in/out). */
static void am32_putc(uint8_t esc, uint8_t value, uint32_t *t_next)
{
    uint32_t t = *t_next;
    uint8_t i;

    am32_wait_until(t);
    am32_line_out(esc, 0U);                     /* start bit */
    for (i = 0U; i < 8U; i++) {
        t += s_bit_ticks;
        am32_wait_until(t);
        am32_line_out(esc, (uint8_t)((value >> i) & 0x01U));
    }
    t += s_bit_ticks;
    am32_wait_until(t);
    am32_line_out(esc, 1U);                     /* stop bit */
    *t_next = t + s_bit_ticks;
}

/* Receive one byte.  Returns 0 on success, AM32_4WAY_ERR_TIMEOUT when no start
 * bit appeared within timeout_ticks and AM32_4WAY_ERR_FRAME when the start or
 * stop bit level is invalid. */
static int am32_getc(uint8_t esc, uint8_t *value, uint32_t timeout_ticks)
{
    uint32_t start;
    uint32_t t;
    uint32_t bits = 0U;
    uint8_t i;

    start = dshot_timebase_raw();
    while (am32_line_read(esc) != 0U) {
        if ((uint32_t)(dshot_timebase_raw() - start) > timeout_ticks) {
            return AM32_4WAY_ERR_TIMEOUT;
        }
    }

    /* The detected edge is slightly late, sample 0.75 bit later and then once
     * per bit: bit0 = start bit, bit1..8 = data, bit9 = stop bit. */
    t = dshot_timebase_raw() + ((s_bit_ticks * 3U) / 4U);
    am32_wait_until(t);
    for (i = 0U; i < 10U; i++) {
        if (am32_line_read(esc) != 0U) {
            bits |= (1UL << i);
        }
        t += s_bit_ticks;
        if (i < 9U) {
            am32_wait_until(t);
        }
    }

    if (((bits & 0x001U) != 0U) || ((bits & 0x200U) == 0U)) {
        return AM32_4WAY_ERR_FRAME;
    }
    *value = (uint8_t)(bits >> 1);
    return 0;
}

/* ------------------------------------------------------------------------- */
/* Public API                                                                */
/* ------------------------------------------------------------------------- */
int am32_4way_begin(uint8_t esc)
{
    uint32_t freq;
    int rc;

    if (esc >= dshot_get_channel_count()) {
        s_last_error = AM32_4WAY_ERR_ARG;
        return AM32_4WAY_ERR_ARG;
    }
    if ((s_state == AM32_4WAY_STATE_ACTIVE) && (s_active_esc != esc)) {
        s_last_error = AM32_4WAY_ERR_BUSY;
        return AM32_4WAY_ERR_BUSY;
    }

    freq = dshot_timebase_freq_hz();
    if (freq == 0U) {
        s_last_error = AM32_4WAY_ERR_NOT_READY;
        return AM32_4WAY_ERR_NOT_READY;
    }
    s_bit_ticks = (uint32_t)(((uint64_t)freq + (AM32_4WAY_BAUD / 2U)) / AM32_4WAY_BAUD);
    if (s_bit_ticks < 16U) {
        s_last_error = AM32_4WAY_ERR_NOT_READY;
        return AM32_4WAY_ERR_NOT_READY;
    }
    s_gap_timeout_ticks = am32_us_to_ticks(AM32_4WAY_GAP_TIMEOUT_US);
    s_total_timeout_ticks = am32_us_to_ticks(AM32_4WAY_TOTAL_TIMEOUT_US);

    rc = dshot_line_park(esc);
    if (rc != 0) {
        s_state = AM32_4WAY_STATE_ERROR;
        s_active_esc = 0xFFU;
        s_last_error = rc;
        return rc;
    }

    am32_line_out(esc, 1U);         /* idle high, UART stop level */
    s_active_esc = esc;
    s_state = AM32_4WAY_STATE_ACTIVE;
    s_last_error = 0;
    return 0;
}

int am32_4way_end(uint8_t esc)
{
    if (esc >= dshot_get_channel_count()) {
        s_last_error = AM32_4WAY_ERR_ARG;
        return AM32_4WAY_ERR_ARG;
    }
    if ((s_state == AM32_4WAY_STATE_ACTIVE) && (s_active_esc == esc)) {
        dshot_line_release(esc);
    }
    if (s_state != AM32_4WAY_STATE_ACTIVE) {
        s_state = AM32_4WAY_STATE_IDLE;
        s_active_esc = 0xFFU;
    }
    return 0;
}

int am32_4way_xfer(uint8_t esc, const uint8_t *in, uint8_t in_len,
                   uint8_t *out, uint8_t out_len)
{
    uint32_t t;
    uint32_t deadline;
    uint32_t received = 0U;
    uint8_t i;

    if ((esc >= dshot_get_channel_count()) ||
        ((in == NULL) && (in_len != 0U)) ||
        ((out == NULL) && (out_len != 0U))) {
        s_last_error = AM32_4WAY_ERR_ARG;
        return AM32_4WAY_ERR_ARG;
    }
    if (out_len > AM32_4WAY_MAX_RESPONSE) {
        out_len = AM32_4WAY_MAX_RESPONSE;
    }
    if ((s_state != AM32_4WAY_STATE_ACTIVE) || (s_active_esc != esc)) {
        s_last_error = AM32_4WAY_ERR_NOT_READY;
        return AM32_4WAY_ERR_NOT_READY;
    }

    /* ---- transmit the request ---- */
    am32_line_out(esc, 1U);
    t = dshot_timebase_raw();
    for (i = 0U; i < in_len; i++) {
        am32_putc(esc, in[i], &t);
    }
    /* release the line right after the stop bit of the last byte; the pull-up
     * keeps the idle level high while the ESC starts its answer */
    am32_wait_until(t - s_bit_ticks);
    am32_line_in(esc);

    /* ---- receive the answer ---- */
    deadline = dshot_timebase_raw() + s_total_timeout_ticks;
    while (received < (uint32_t)out_len) {
        uint32_t now = dshot_timebase_raw();
        uint32_t timeout;
        uint8_t value = 0U;
        int rc;

        if ((int32_t)(now - deadline) >= 0) {
            break;                          /* hard cap reached */
        }
        if (received == 0U) {
            timeout = deadline - now;       /* first byte: full remaining budget */
        } else {
            timeout = s_gap_timeout_ticks;  /* frame finished when the line idles */
        }
        rc = am32_getc(esc, &value, timeout);
        if (rc != 0) {
            if ((received > 0U) && (rc == AM32_4WAY_ERR_TIMEOUT)) {
                break;                      /* frame complete: short ACK etc. */
            }
            am32_line_out(esc, 1U);
            s_last_error = rc;
            return rc;
        }
        out[received] = value;
        received++;
    }

    am32_line_out(esc, 1U);                 /* leave the line idle high */
    s_last_error = 0;
    return (int)received;
}

void am32_4way_set_idle(uint8_t esc)
{
    if ((s_state == AM32_4WAY_STATE_ACTIVE) && (s_active_esc == esc)) {
        am32_line_out(esc, 1U);
    }
}

uint8_t am32_4way_get_state(void)
{
    return s_state;
}

int am32_4way_last_error(void)
{
    return (int)s_last_error;
}

uint8_t am32_4way_get_active_esc(void)
{
    return s_active_esc;
}
