/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Bidirectional DShot (BDShot) driver for 2 AM32 ESCs
 *
 *   TX : PWM0.P2 (PF02) / PWM0.P3 (PF03), 16 bit command frame, inverted line
 *        (idle high), inverted CRC (BDShot)
 *   RX : the same two pads are switched to TRGM input function while the ESC
 *        telemetry frame is received, GPTMR1 channel2/channel3 capture both
 *        edges and the frame is decoded (differential -> GCR -> CRC -> eRPM).
 *
 * The driver is fully non blocking: dshot_task() polls a small state machine
 * and must be called from the main control loop (every loop iteration).  The
 * time critical TX -> direction switch -> capture part is handled by the PWM
 * burst-end interrupt and the GPTMR1 capture interrupt, it does not depend on
 * the main loop rate.
 *
 * AM32 configuration: the same signal line is used by the "4-way interface"
 * of the BLHeli/AM32 bootloader.  am32_4way_*() bit-bang that protocol on the
 * ESC line so the PC tool can read/write the AM32 EEPROM through the board.
 *
 * ---------------------------------------------------------------------------
 * Hardware resources used (HPM5E00 / board ecat_drone_5e3y)
 *   PWM0 counter1, outputs P2 (ESC1) and P3 (ESC2)  [see UM 37.4.8:
 *        outputs 0/1 -> counter0/CMP0..3, outputs 2/3 -> counter1/CMP4..7,
 *        outputs 4/5 -> counter2/CMP8..11, outputs 6/7 -> counter3/CMP12..15]
 *   PWM0 counter1 dedicated compare point CMP[16] (+4 ticks after reload)
 *        -> PWM DMA request channel 0 (pwm_dma_0)
 *        -> TRGM DMACFG_0 (HPM_TRGM0_DMA_SRC_PWM0_0) -> DMAMUX MOT_0
 *        -> HPM_XDMA channel 0 (fixed burst of 2 words into SHADOW_VAL[2..3])
 *   TRGM0 input P02/P03 -> TRGM0 output GPTMR1_CAPT_2/3 (TRGOCFG[0x3A/0x3B])
 *   GPTMR1 channel2 = ESC1 capture, channel3 = ESC2 capture,
 *   GPTMR1 channel0 = free running 32 bit time base (pacing / timeouts)
 *   IRQ: IRQn_PWM0 (burst end), IRQn_GPTMR1 (capture)
 * ---------------------------------------------------------------------------
 */

#ifndef DSHOT_H
#define DSHOT_H

#include <stdint.h>
#include "app_config.h"

#ifdef __cplusplus
extern "C" {
#endif

/* DShot special commands (subset used by this project, AM32 compatible) */
#define DSHOT_CMD_MOTOR_STOP            (0U)
#define DSHOT_CMD_BEACON1               (1U)
#define DSHOT_CMD_BEACON2               (2U)
#define DSHOT_CMD_BEACON3               (3U)
#define DSHOT_CMD_BEACON4               (4U)
#define DSHOT_CMD_BEACON5               (5U)
#define DSHOT_CMD_ESC_INFO              (6U)
#define DSHOT_CMD_SPIN_DIRECTION_1      (7U)
#define DSHOT_CMD_SPIN_DIRECTION_2      (8U)
#define DSHOT_CMD_3D_MODE_OFF           (9U)
#define DSHOT_CMD_3D_MODE_ON            (10U)
#define DSHOT_CMD_SETTINGS_REQUEST      (11U)
#define DSHOT_CMD_SAVE_SETTINGS         (12U)
#define DSHOT_CMD_EXTENDED_TELEMETRY    (13U)
#define DSHOT_CMD_SPIN_DIRECTION_NORMAL (20U)
#define DSHOT_CMD_SPIN_DIRECTION_REVERSED (21U)

/* AM32 private DShot "programming mode" commands (see docs/DSHOT_AM32.md).
 * 36 = enter programming mode, then one frame with the EEPROM address, one
 * frame with the new value, one frame with the magic 37 to commit the byte.
 * The byte is only written to RAM eepromBuffer; send command 12 (save
 * settings) six times afterwards to commit it to the ESC flash. */
#define DSHOT_CMD_AM32_ENTER_PROG       (36U)
#define DSHOT_CMD_AM32_PROG_COMMIT      (37U)

/* Maximum number of captured telemetry edges kept per ESC.  The wire sequence
 * has 21 differential bits; GCR allows at most 2 consecutive zero bits, so at
 * most 21 toggles (edges) can be observed.  24 leaves a little headroom for
 * noise/debugging. */
#define DSHOT_CAPTURE_EDGES_MAX         (24U)

/* 12 bit telemetry payload value that means "motor stopped" */
#define DSHOT_TELEMETRY_STOPPED         (0x0FFFU)
/* Maximum raw DShot throttle value (11 bit) */
#define DSHOT_THROTTLE_MAX              (2047U)
/* Default number of consecutive frames used for one special DShot command:
 * AM32 requires at least 6 identical frames (command_count >= 6). */
#define DSHOT_COMMAND_REPEAT_DEFAULT    (6U)

typedef enum {
    DSHOT_STATE_UNINIT = 0,
    DSHOT_STATE_TX,
    DSHOT_STATE_SWITCH_TO_RX,
    DSHOT_STATE_WAIT_ESC,     /* waiting for the first falling edge        */
    DSHOT_STATE_RX,
    DSHOT_STATE_DECODE,
    DSHOT_STATE_ERROR,
    DSHOT_STATE_IDLE = 7,     /* driver idle, next frame is scheduled      */
} dshot_state_t;

typedef struct {
    uint32_t erpm;           /* electrical rpm, 0 if the ESC reports "stopped"   */
    uint16_t period_us;      /* raw telemetry period, 0 = no valid frame         */
    uint8_t  valid;          /* 1 = the last received frame passed the CRC       */
    uint8_t  state;          /* dshot_state_t of the driver                      */
    uint16_t crc_errors;     /* consecutive decode errors (saturating)           */
    uint32_t ok_count;
    uint32_t err_count;
    uint32_t timeout_count;
    uint32_t last_ok_tick_ms;
} dshot_telemetry_t;

/* Result of one decoded BDShot frame (used by the decoder and by the
 * host-side simulation / unit test). */
typedef struct {
    uint16_t value16;        /* received 16 bit value: 12 bit payload << 4 | CRC */
    uint16_t telemetry;      /* 12 bit telemetry payload (eee mmmmmmmmm)         */
    uint16_t period_us;      /* (m << e), 0 when the ESC reported "stopped"      */
    uint32_t erpm;           /* 60000000 / period_us, 0 when stopped             */
    uint8_t  edges;          /* number of edge timestamps the decode used        */
    uint8_t  valid;          /* 1 = GCR decodable and inverted CRC matched       */
} dshot_rx_frame_t;

/* Initialize PWM/TRGM/GPTMR/DMA resources, both ESCs are stopped afterwards. */
void dshot_init(void);

/* Enable/disable the DShot output (ESC commands are sent as MOTOR_STOP when
 * disabled).  Frames keep being sent so the ESCs do not time out/disarm. */
void dshot_enable(uint8_t enable);

/* Number of DShot channels supported (2) */
uint8_t dshot_get_channel_count(void);

/* Set the throttle of one ESC (0..2047 raw DShot throttle value).
 * Use dshot_us_to_throttle() to convert a 1000..2000us command. */
void dshot_set_throttle(uint8_t ch, uint16_t throttle);

/* Send a special DShot command (e.g. DSHOT_CMD_SAVE_SETTINGS).  The command is
 * repeated over the next DSHOT_COMMAND_REPEAT_DEFAULT frames (AM32/BLHeli32
 * require several identical frames before they accept a command).
 * ch = 0..1 for a single ESC, 0xFF for all ESCs. */
void dshot_send_command(uint8_t ch, uint16_t cmd);

/* Convert a 1000..2000us throttle command to a raw DShot throttle value */
uint16_t dshot_us_to_throttle(uint16_t us);

/* Read the latest telemetry of one ESC */
void dshot_get_telemetry(uint8_t ch, dshot_telemetry_t *tlm);

/* Non blocking state machine, call from the main loop. */
void dshot_task(void);

/* True when the AM32 4-way bridge owns the ESC line (DShot output paused). */
uint8_t dshot_is_4way_active(void);

/* Set the telemetry timeout (us) and DShot rate (150/300/600/1200) at runtime */
void dshot_set_rate(uint16_t rate);
void dshot_set_tlm_timeout_us(uint16_t timeout_us);
/* Requested DShot frame update rate (Hz).  The driver automatically enforces a
 * lower bound so that one TX+RX cycle always fits; default is
 * APP_CFG_DSHOT_UPDATE_HZ. */
void dshot_set_update_hz(uint16_t hz);

/* Current configuration (for the host tool / telemetry) */
uint16_t dshot_get_rate(void);
uint16_t dshot_get_tlm_timeout_us(void);
/* Nominal telemetry bit slot in GPTMR capture ticks (debug/bench) */
uint32_t dshot_get_telemetry_slot_ticks(void);
/* Nominal command bit period in us (e.g. 1.667us for DSHOT600) */
uint16_t dshot_get_bit_period_ns(void);

/* ---------------------------------------------------------------------------
 * Protocol helpers (pure functions, no hardware access - also used by the
 * host side simulation in .work/dshot_test)
 * ------------------------------------------------------------------------- */

/* BDShot inverted 4 bit CRC over a 12 bit value:
 *   crc = ~(data ^ (data >> 4) ^ (data >> 8)) & 0x0F */
uint8_t dshot_crc_bidir(uint16_t data12);

/* Build the 16 bit command frame (MSB first on the wire):
 *   v   = (throttle << 1) | telemetry_request
 *   crc = dshot_crc_bidir(v)
 *   frame = (v << 4) | crc */
uint16_t dshot_make_frame(uint16_t throttle, uint8_t telemetry_request);

/* Convert a 12 bit telemetry payload into eRPM (0 for 0x0FFF / period 0) */
uint32_t dshot_telemetry_to_erpm(uint16_t telemetry12);

/* Decode one captured wire sequence.
 *   timestamps[]        absolute capture counter values (any phase, 32 bit wrap
 *                       is handled internally by using deltas to timestamps[0])
 *   edge_count          number of valid entries in timestamps[]
 *   slot_ticks_nominal  nominal telemetry bit slot length in the same ticks as
 *                       the timestamps (used to reject candidates > 33% off)
 *   frame               decoded result
 * Returns 0 on success, -1 when no candidate GCR/CRC decode was possible.
 * The decoder does not assume the first interval is exactly one bit slot:
 * GCR quintets may start with one or two zero bits, so up to 3 slot-width
 * candidates are tried and validated with GCR validity + inverted CRC. */
int dshot_decode_edges(const uint32_t *timestamps, uint8_t edge_count,
                       uint32_t slot_ticks_nominal, dshot_rx_frame_t *frame);

/* ---------------------------------------------------------------------------
 * AM32 four-way interface bridge
 *   The PC host tool speaks the BLHeli/AM32 4-way interface.  The board acts as
 *   a transparent bit-banged bridge between USB CDC and the ESC signal line:
 *   it only moves raw bytes at 19200 8N1 (idle high, LSB first, half duplex),
 *   the frame container (ESC + CMD + LEN + PARAM + CRC16) is owned by the PC.
 *   All functions are blocking with short timeouts and return 0 on success.
 * ------------------------------------------------------------------------- */
int am32_4way_begin(uint8_t esc);
int am32_4way_end(uint8_t esc);
/* Send in_len bytes and receive the ESC answer (half duplex, the ESC answers
 * after the last request byte).
 *
 *   out_len is the CAPACITY of out[].  The function sends all in_len bytes and
 *   then receives until either out_len bytes were received or the line has
 *   been idle for a few byte times (frame complete) - so short ACK frames
 *   return quickly instead of waiting for the full capacity.
 *
 * Returns the number of bytes actually received (>= 0) or a negative
 * AM32_4WAY_ERR_* code.  A full AM32 EEPROM read response is ~197 bytes, so
 * out_len may be up to 208.  Worst case blocking time is ~150 ms (a 197 byte
 * response at 19200 baud takes ~103 ms). */
int am32_4way_xfer(uint8_t esc, const uint8_t *in, uint8_t in_len,
                   uint8_t *out, uint8_t out_len);
/* Raw line level helpers (used by the 4-way implementation) */
void am32_4way_set_idle(uint8_t esc);

/* Error codes returned by am32_4way_xfer()/am32_4way_begin() (< 0) */
#define AM32_4WAY_ERR_ARG       (-1)   /* bad esc / buffer / length          */
#define AM32_4WAY_ERR_BUSY      (-2)   /* another ESC line is already parked */
#define AM32_4WAY_ERR_NOT_READY (-3)   /* DShot is still busy (frame in flight) */
#define AM32_4WAY_ERR_TIMEOUT   (-4)   /* no response / start bit timeout     */
#define AM32_4WAY_ERR_FRAME     (-5)   /* bad start/stop bit, overrun         */

/* 4-way bridge state for HOST_CMD_AM32_STATUS:
 *   0 = idle, 1 = 4-way active (line parked), 2 = error */
#define AM32_4WAY_STATE_IDLE    (0U)
#define AM32_4WAY_STATE_ACTIVE  (1U)
#define AM32_4WAY_STATE_ERROR   (2U)

uint8_t am32_4way_get_state(void);
/* 0 while no error occurred, otherwise the last negative AM32_4WAY_ERR_* code */
int am32_4way_last_error(void);
/* Which ESC (0/1) currently owns the line, 0xFF = none */
uint8_t am32_4way_get_active_esc(void);

/* ---------------------------------------------------------------------------
 * AM32 DShot programming mode (write-only, much simpler than the 4-way
 * bootloader bridge, shares the normal DShot TX path)
 *
 *   dshot_prog_write_byte(esc, addr, value):
 *       36,36,36,36,36,36 (enter programming mode)
 *       addr              (11 bit DShot throttle field = EEPROM address 0..191)
 *       value             (11 bit, new byte value 0..255)
 *       37                (commit byte into the ESC RAM eeprom buffer)
 *   dshot_prog_save(esc):
 *       12,12,12,12,12,12 (DSHOT_CMD_SAVE_SETTINGS - write RAM -> ESC flash)
 *
 * The calls are non blocking: they append frames to a small per-ESC script
 * queue that dshot_task() plays out at the configured update rate (~1 kHz, so
 * one byte takes ~9 ms, the whole 192 byte EEPROM ~1.8 s + save ~6 ms).
 * Requirements: the ESC must be armed but the motor must NOT turn (throttle 0
 * frames sent for a while), and no other commands may interleave.  The queue
 * is 16 steps deep (up to 4 pending bytes) - poll dshot_prog_busy() or use the
 * -1 return value to retry.
 * Returns 0 when the script was queued, -1 when invalid/busy.
 * ------------------------------------------------------------------------- */
int dshot_prog_write_byte(uint8_t esc, uint8_t addr, uint8_t value);
int dshot_prog_save(uint8_t esc);
uint8_t dshot_prog_busy(uint8_t esc);
/* Abort a running/pending programming script (e.g. on AM32_END) */
void dshot_prog_abort(uint8_t esc);

/* Internal helpers implemented in dshot_bidir.c and used by am32_4way.c.
 * dshot_line_park() stops the DShot state machine, waits for an in-flight
 * frame, disables the PWM outputs and switches the pad to GPIO output at the
 * requested idle level; dshot_line_release() restores the PWM output. */
int dshot_line_park(uint8_t esc);
void dshot_line_release(uint8_t esc);
/* Configure the ESC pad as GPIO (output=1 pushes level, output=0 = input with
 * pull-up) or read back the current line level (1 = high). */
void dshot_line_set_gpio(uint8_t esc, uint8_t output, uint8_t level);
uint8_t dshot_line_read_gpio(uint8_t esc);
/* Free running 32 bit time base (GPTMR1 channel0) and its frequency in Hz.
 * Used for the 19200 baud 4-way bit-bang timing. */
uint32_t dshot_timebase_raw(void);
uint32_t dshot_timebase_freq_hz(void);

#ifdef __cplusplus
}
#endif

#endif /* DSHOT_H */

