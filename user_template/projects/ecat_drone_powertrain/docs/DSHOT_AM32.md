# Bidirectional DShot + AM32 configuration — design and bring-up note

Scope: `src/dshot_bidir.c`, `src/am32_4way.c`, `inc/dshot.h` for the
`ecat_drone_powertrain` firmware (board `ecat_drone_5e3y`, HPM5E3Y, PWMV2 +
DMAV2 (XDMA) + TRGM + GPTMR1).

References used:

* HPMicro KB «双向 DSHOT（BDShot）收发实现概要设计» (`.work/refs/01_hpmicro_bdshot_design.md`)
* HPMicro KB «单向 DSHOT 输出实现概要设计» (`.work/refs/02_hpmicro_dshot_oneway_design.md`)
* HPM5E00 User Manual Rev0.4 §37 (PWMV2), §38 (TRGM), §34/35 (GPTMR/DMA), §26 (IOC/GPIO)
* SDK samples: `samples/drivers/pwmv2/pwm_output`, `samples/drivers/dmav2/dmav2_fixed_burst_swap`,
  `samples/drivers/dma/dma_use_gptmr_event_transfer`, `samples/drivers/gptmr/input_capture`
* `.work/refs/DSHOT_AM32_NOTES.md` (protocol research) and the AM32 sources in `.work/refs/am32/`

---

## 1. Hardware resource map (nothing else in the firmware uses these)

| Resource | Use |
|---|---|
| PWM0 counter **1** | one command bit period T (DSHOT600: 1.667 µs) as counter reload |
| PWM0 output 2 (PWM[2], PF02) | ESC1, compare points CMP[4] = a, CMP[5] = b |
| PWM0 output 3 (PWM[3], PF03) | ESC2, compare points CMP[6] = c, CMP[7] = d |
| PWM0 CMP[16] | periodic DMA request source, mapped to counter 1, compare value 4 ticks |
| XDMA channel 0 | PWM → compare shadow burst (2 words per bit period) |
| TRGM DMACFG_0 | `HPM_TRGM0_DMA_SRC_PWM0_0` (PWM0 DMA request line 0) → `HPM_DMA_SRC_MOT_0` |
| TRGM TRGOCFG[0x3A], TRGOCFG[0x3B] | TRGM input P02/P03 → GPTMR1 capture inputs 2/3 |
| GPTMR1 channel 2 / 3 | ESC1 / ESC2 telemetry edge capture (both edges, 32 bit free run) |
| GPTMR1 channel 0 | free running 32 bit time base: frame pacing, RX window, AM32 bit-bang |
| IRQn_PWM0, IRQn_GPTMR1 | burst-end and capture interrupts (priority 1) |
| IOC PF02/PF03 | PWM0 alt (TX) ↔ TRGM alt (RX) ↔ GPIO (AM32 4-way) |

Why counter 1 and not counter 0 — HPM5E00 UM §37.4.8:

```
outputs 0/1 -> counter 0, compare values 0,1,2,3
outputs 2/3 -> counter 1, compare values 4,5,6,7   <-- ESC1/ESC2 (PWM0.P2/P3)
outputs 4/5 -> counter 2, compare values 8,9,10,11
outputs 6/7 -> counter 3, compare values 12,13,14,15
```

`board.h` provides `BOARD_DSHOT_PWM_COUNTER = pwm_counter_1` and
`BOARD_DSHOT_ESC1_CMP_INDEX = 4`, `BOARD_DSHOT_ESC2_CMP_INDEX = 6`.

---

## 2. TX path — PWMV2 output compare + XDMA fixed burst

### 2.1 Waveform

An output X is high while `a <= CNT < b` (Y: `c <= CNT < d`), the reload resets
it to the initial (low) level.  `OUT_POLARITY = 1` inverts the pad, so the line
**idles HIGH** and a DShot bit is an active **low** pulse:

| bit | pulse (low) width | compare value |
|---|---|---|
| 1 | 0.75 T | `b = (T*3)/4` |
| 0 | 0.375 T | `b = (T*3)/8` |

with `a = c = 0`.  `T = round(f_pwm0 / dshot_rate)` PWM ticks
(100 MHz / DSHOT600 → 167 ticks; DSHOT1200 on a 24 MHz clock still gives
20 ticks, i.e. 7 ticks for a '0' — 8-bit DAC resolution is enough).

### 2.2 Comparing / shadow values

| shadow | used by | content |
|---|---|---|
| `SHADOW_VAL[0]` | CMP[4] (ESC1 a) | 0 |
| `SHADOW_VAL[1]` | CMP[6] (ESC2 c) | 0 |
| `SHADOW_VAL[2]` | CMP[5] (ESC1 b) | pulse width of the current bit (DMA target) |
| `SHADOW_VAL[3]` | CMP[7] (ESC2 d) | pulse width of the current bit (DMA target) |
| `SHADOW_VAL[4]` | counter 1 reload | `T - 1` |
| `SHADOW_VAL[5]` | CMP[16] DMA trigger | 4 ticks |

CMP[5] and CMP[7] are configured with
`update_trigger = pwm_shadow_register_update_on_reload`: a value written by the
DMA during bit period *k* is latched for bit period *k+1*.  CMP[4]/CMP[6] are
latched once at init (`on_shlk`).  The shadow registers are kept unlocked
(`WORK_CTRL0` key + `UNLOCK` mask = all ones) for the entire runtime because the
DMA must be able to write them.

### 2.3 DMA (1 channel serves both ESCs)

```
PWM0 counter1 reload
   -> CMP[16] compare event (4 ticks after reload)
   -> PWM0 DMA request line 0  (DMA_EN.DMA0_SEL = 16)
   -> TRGM DMACFG_0 (HPM_TRGM0_DMA_SRC_PWM0_0)
   -> DMAMUX MOT_0 -> HPM_XDMA channel 0
   -> one fixed burst of 2 words: SHADOW_VAL[2] (ESC1), SHADOW_VAL[3] (ESC2)
```

`dma_channel_config_t`: `src=WORD/INCREMENT/NORMAL`, `dst=WORD/HANDSHAKE/FIXED`,
`en_dst_burst_in_fixed_trans = true`, `src_burst_size = 2T`,
`handshake_opt = ONE_BURST`, size = 16 bursts × 2 words = 128 bytes.
The source buffer is `ATTR_PLACE_AT_NONCACHEABLE_BSS_WITH_ALIGNMENT(32)`
(64 KB AXI-SRAM non-cacheable region, i.e. same bus domain as XDMA — cache
coherency is not an issue; the HPMicro KB recommendation to prefer HDMA+HRAM
does not apply because the board-level DMA budget assigns XDMA to DShot).

Buffer layout `buf[2*k + esc]` = pulse width << 8:

```
burst 0 : bit1 ESC1 | bit1 ESC2
burst 1 : bit2 ESC1 | bit2 ESC2
...
burst 14: bit15 ESC1| bit15 ESC2
burst 15: 0         | 0            (idle padding)
```

### 2.4 Frame start / frame end

1. `dshot_task()` builds the frame for both ESCs:
   `v = (value11 << 1) | tlm_req`, `crc = ~(v ^ (v>>4) ^ (v>>8)) & 0xF`,
   `frame = (v << 4) | crc` (inverted CRC, BDShot), `value11` = throttle or
   pending special command / AM32 programming script.
2. Bit 0 is written to `SHADOW_VAL[2]/[3]` by the CPU.
3. Short critical section (`disable_global_irq`): disable/rearm the DMA channel,
   clear burst-end status, reset counter 1, `pwmv2_enable_counter()`,
   `pwmv2_start_pwm_output()` (output starts at the next reload → that reload
   also latches bit 0), then enable the DMA channel.  The critical section
   removes the only race between "burst armed" and "output started".
4. Counter 1 runs `CNT_BURST = 18` (19 bit periods: 16 data + idle padding) and
   stops by itself; the compare registers hold pulse width 0 in the padding
   periods, so the line stays idle HIGH.
5. `IRQn_PWM0` burst-end ISR: stop counter/DMA, switch both pads from PWM alt to
   TRGM alt, arm the capture, start the RX window.

### 2.5 Frame pacing

`dshot_task()` is called from the main loop; it starts a frame when
`now - last_frame >= s_update_period_ticks`.  The requested rate is
`APP_CFG_DSHOT_UPDATE_HZ` (1 kHz, can be changed with
`dshot_set_update_hz()`), and the driver enforces a lower bound of
`TX duration + RX window + 0.5 %` so the frames can never overlap.  Main loop
jitter only delays the frame start, never the TX→RX sequence (which is pure
hardware + ISR).

---

## 3. RX path — TRGM → GPTMR1 capture → software decode

### 3.1 Capture

* Pads PF02/PF03 are switched to `TRGM_P_02 / TRGM_P_03` (ALT 17) in the
  burst-end ISR; internal pull-up stays enabled (external pull-ups recommended).
* TRGM: `TRGOCFG[0x3A] <- HPM_TRGM0_INPUT_SRC_TRGM0_P02`,
  `TRGOCFG[0x3B] <- HPM_TRGM0_INPUT_SRC_TRGM0_P03`,
  type `trgm_output_same_as_input` (level pass-through, no edge/invert).
* GPTMR1 channel 2/3: `capture_at_both_edge`, 32 bit free running counter.
* The capture ISR reads CAPPOS and CAPNEG, compares both against the last
  accepted timestamp and appends the plausible value(s).  This also recovers
  if one ISR entry was delayed and two edges are pending.  The absolute edge
  direction is not needed: BDShot bits are differential and the first edge of a
  frame is always the start bit.
* RX window = `tlm_timeout (default 70 µs) + 24 * slot` (DSHOT600: ~102 µs).
  After it expires `dshot_task()` decodes, stores telemetry and gives the pads
  back to the PWM function (actively drives idle HIGH).
* A `last_ok`/`valid` flag: `dshot_telemetry_t.valid` becomes 0 on timeout/bad
  CRC, so `app_task.c` can detect telemetry loss; the last good eRPM/period are
  kept for display.

### 3.2 Decode pipeline

```
edge timestamps
  -> delta[] to the first edge (32 bit wrap safe)
  -> candidate slot widths from the first and the second gap (each 1..3 slots,
     because a GCR quintet may start with zeros); candidates must be within
     ±33 % of the nominal slot (timebase_freq*4/(5*dshot_rate))
  -> per candidate: interval -> round(interval/width) slots, must be 1..3 and
     within 25 % of a slot (GCR guarantees at most 2 consecutive zero bits)
  -> rebuild the 21 bit wire sequence: start bit 1, then for every interval
     "n-1 zeros + 1", trailing zeros to 21 bits
  -> 20 bit GCR -> 4 x 5 bit quintets -> nibbles (invalid quintet = reject)
  -> telemetry12 = value16 >> 4, crc = value16 & 0xF,
     accept only if dshot_crc_bidir(telemetry12) == crc    (INVERTED CRC!)
  -> period_us = m << e          (e = tele >> 9, m = tele & 0x1FF)
     eRPM = 60e6 / period_us     (0x0FFF = stopped -> eRPM 0)
```

The best candidate is chosen by the smallest total timing residual.  A wrong
value can only pass if it also passes the GCR validity and the 4 bit CRC, which
the host simulation below exhaustively covers.

Decode examples (from the simulation): 7 pole pairs, 5000 mechanical RPM =
35000 eRPM → period 1714 µs → telemetry `0x5AD` → decoded 34965 eRPM
(4995 mech RPM, 0.1 % quantization).

### 3.3 Timing budget (DSHOT600, 1 kHz update)

| segment | duration |
|---|---|
| TX 19 bit periods | 31.7 µs (16 data + padding) |
| pad switch | ~1 µs |
| ESC response gap (worst case) | up to 70 µs (timeout parameter) |
| RX frame 21 slots @ 1.333 µs | 28 µs |
| decode | < 10 µs (main loop) |
| **total** | **< 140 µs of the 1 ms period** |

DSHOT150/300 still fit 1 kHz with the enforced minimum period
(DSHOT300 RX window 134 µs; DSHOT150 198 µs).

---

## 4. AM32 configuration — two independent paths

### 4.1 Path A — 4-way bit-bang bridge (`am32_4way_*.c`)

The board is a **transparent raw byte pipe** at 19200 8N1 (52.08 µs/bit, idle
HIGH, LSB first) between USB CDC and the ESC line.  The **PC host tool owns the
frame container and the CRC**; the board only moves bytes.

API / host protocol contract:

| HOST command | behaviour |
|---|---|
| `HOST_CMD_AM32_BEGIN(esc)` | `am32_4way_begin()`: waits for the in-flight DShot frame, disables the PWM output, switches the pad to GPIO idle HIGH; DShot traffic stops for all ESCs; returns `HOST_STATUS_OK` or `HOST_STATUS_ERR_NOT_READY` |
| `HOST_CMD_AM32_XFER(esc, in_len, in[])` | `am32_4way_xfer()`: sends all `in_len` bytes, then receives into `out[]` until `out_len` (capacity, max 208) bytes or the line has been idle ~1.5 ms (frame complete); returns **actual received count** in the response |
| `HOST_CMD_AM32_END(esc)` | `am32_4way_end()`: pad back to PWM0 alt, PWM output re-enabled, DShot frames resume |
| `HOST_CMD_AM32_STATUS(esc)` | `u8 state, u8 last_error`; see the table below |

`HOST_CMD_AM32_STATUS` response (`host_am32_status_t`, see `host_proto.h`):
`u8 state, u8 last_error`.

| state | meaning |
|---|---|
| 0 | idle (DShot owns the line) |
| 1 | 4-way active, line parked as GPIO |
| 2 | error (last operation failed) |

`last_error` is a `host_status_t` value (as tracked by `src/host_proto_app.c`):
`0` = none, `0x09` (`HOST_STATUS_ERR_TIMEOUT`) = the last BEGIN/XFER failed.
The detailed driver error code is available in the firmware through
`am32_4way_last_error()` (`AM32_4WAY_ERR_*`, see `inc/dshot.h`) and can be
logged by the host protocol layer for diagnosis:

| `AM32_4WAY_ERR_*` | meaning |
|---|---|
| -1 | bad ESC / buffer / length |
| -2 | busy - the other ESC already owns the line |
| -3 | not ready - a DShot frame did not finish in time (retry) |
| -4 | timeout - no start bit / no response |
| -5 | frame error - start/stop bit level violation (line noise) |

Notes / limitations:

* Blocking: a 192 byte EEPROM read answers ~197 raw bytes = ~103 ms; the hard
  cap is 150 ms (`AM32_4WAY_TOTAL_TIMEOUT_US`).  The command must therefore be
  used from the maintenance path, never from the 1 kHz control loop.
* The other ESC loses its DShot signal during a 4-way session and disarms after
  ~0.5 s (AM32 signal timeout).  That is intentional: configuration is a
  ground-only operation.  After `AM32_END` the normal 1 kHz DShot stream
  re-arms both ESCs with throttle 0.
* The bridge only supports the *application* response timing.  Flash
  erase/program ACKs can take up to 3 s (bootloader side) — a firmware update
  through this pipe is **not** supported by the 150 ms cap; use the AM32
  bootloader / a real USB-UART adapter for that.
* The ESC must be in 4-way/bootloader mode: sending the escape sequence is the
  PC tool's job (the board does not interpret the 4-way container).

### 4.2 Path B — AM32 DShot programming mode (write-only, recommended for settings)

AM32's firmware implements a private EEPROM programming mode over normal DShot
frames (verified in `.work/refs/am32/am32_Src_dshot.c:110-128, 226-229, 204-208`).
The 11 bit "throttle" field of the frame carries the data; the telemetry request
bit must be high (BDShot).  One EEPROM byte:

| step | frames (11 bit DShot value) | note |
|---|---|---|
| enter programming mode | **36** × 6 (exactly 6 consecutive frames!) | requires `armed && running == 0` |
| address | `addr` (0..191) × 1 | EEPROM byte offset |
| value | `value` (0..255) × 1 | new byte |
| commit | **37** × 1 | writes `eepromBuffer[addr] = value` (RAM only) |

After the whole EEPROM was transferred, send **12** (`DSHOT_CMD_SAVE_SETTINGS`)
6 consecutive frames: AM32 writes the RAM buffer into its flash and beeps.

The comments of the AM32 source in `.work/refs/am32/am32_Src_dshot.c` confirm:
the programming values are consumed before the normal command handling, exactly
6 identical command frames are required, and a CRC error aborts the programming
mode.  There is **no read-back** primitive in this path — use it when the PC only
has to write a known parameter set (it can send a complete 192 byte image).

Board-side API (non blocking, plays one frame per DShot period, ~1 kHz):

```c
dshot_prog_write_byte(esc, addr, value);  /* queues 36x6, addr, value, 37 (~9 ms) */
dshot_prog_save(esc);                     /* queues 12x6 (~6 ms)                  */
dshot_prog_busy(esc);                     /* 0 = queue empty                      */
dshot_prog_abort(esc);                    /* drop pending script                  */
```

Queue depth is 16 steps = 4 pending bytes; the host tool should push a few bytes
at a time (checking `dshot_prog_busy()` / the `-1` return) and call
`dshot_prog_save()` after the last byte.  A full 192 byte write takes ~1.8 s.
Requirements: EtherCAT not in OP, motors stopped, throttle 0 frames sent before
(so AM32 is armed + detects BDShot: the line is idle HIGH between frames).
Do not interleave other DShot commands while the script is running.

Which path should the host tool use?

| | Path A: 4-way bit-bang | Path B: DShot programming |
|---|---|---|
| needs ESC bootloader/escape | yes (PC implementation) | no |
| read back settings | yes (whatever the bootloader protocol supports) | **no** |
| can write a full image / flash | yes in principle (slow ACKs > 150 ms are not supported here) | EEPROM only (192 B) |
| impact on the other ESC | DShot stops for both during the session | none (frames continue) |
| extra board logic | 19200 bit-bang + line parking | frame script queue (~60 lines) |
| recommended for | EEPROM read-back / diagnostics | parameter write (the normal tuning flow) |

---

## 5. Parameters / tuning

| parameter | effect |
|---|---|
| `PARAM_DSHOT_RATE` | 150/300/600/1200 → `dshot_set_rate()` (recomputes T, pulse widths, telemetry slot, RX window) |
| `PARAM_DSHOT_TLM_TIMEOUT_US` | ESC response gap 20..200 µs (default 70) → `dshot_set_tlm_timeout_us()` |
| `PARAM_DSHOT_UPDATE_HZ` | frame rate (the driver enforces the minimum that fits TX+RX) |
| `PARAM_DSHOT_POLE_PAIRS` | only used by the host/app to convert eRPM→RPM (7 default) |
| `PARAM_DSHOT_DIRECTION*`, `3D_MODE*`, throttle min/max | applied by `app_task.c` |

`dshot_get_bit_period_ns()` / `dshot_get_telemetry_slot_ticks()` are debug
helpers for the bench (expected pulse width / capture tick count).

---

## 6. AM32 4-way — required ESC state / power-cycle

* The ESC must be in **bootloader / 4-way mode**.  The escape sequence and the
  bootloader framing are owned by the PC tool.  On BLHeli_32/AM32 the bootloader
  can additionally be forced at power-up by the signal line level; the board
  drives the line HIGH while parked, so if a power cycle is part of the
  procedure it must be done **while the board holds the required level** (the
  PC tool can call BEGIN before asking the user to power the ESC).
* `AM32_BEGIN` is rejected while EtherCAT is in OP and while the motors are
  running (host protocol dispatcher / `app_task.c` bench lease); the AM32 bridge
  must only be used on the bench with the props removed.
* After `AM32_END` the DShot stream restarts with throttle 0; the ESC re-arms
  itself after a few frames.

---

## 7. Hardware bring-up / validation checklist (no hardware was available for this delivery)

Measurement order — every step is independent and quick:

1. **Power-up line level**: scope PF02/PF03 before `dshot_init()`.  Both should
   idle HIGH (external pull-up; internal pull-up enabled by the driver).  A
   glitch at ESC power-up can trigger the AM32 bootloader — power the ESCs after
   the board if that happens.
2. **Idle level after init**: after `dshot_init()` both lines must be driven
   HIGH (PWM inverted idle).  If low: check `PWM[2]/PWM[3].CFG0.OUT_POLARITY`
   (`PWMV2_PWM_CFG0_OUT_POLARITY_SET(1)`), `HIGHZ_EN_N` in CFG1 and the counter
   state (`pwmv2_deinit` must not have run after init).
3. **TX frame**: scope one line, throttle 1000 µs / 1500 µs.  Expect 16 low
   pulses, 1.667 µs apart (DSHOT600), width 625 ns for '1' / 312 ns for '0',
   idle HIGH between frames.  Verify the frame with a logic analyser DShot
   decoder (e.g. Saleae DShot analyser) — should report throttle 48 / 1023.
   `dshot_get_bit_period_ns()` must return 1666.
4. **DMA data correctness**: with a 1023 throttle the frame must be
   `(1023<<1)|1 = 0x7FF` → `0x7FF0`; the scope duty must alternate accordingly.
   If bits are shifted by one, check the CMP[16] trigger offset
   (`DSHOT_DMA_TRIGGER_OFFSET`), the reload latch (`update_trigger = reload`)
   and the burst-data command (dst fixburst, 2 words).
5. **RX capture**: connect the ESC, motor stopped.  `dshot_get_telemetry()`
   must report `valid=1`, `erpm=0`, `period_us=0` (0x0FFF frame) within ~1 ms;
   `timeout_count` must stay 0.  If `err_count` rises: measure the first edge
   delay (should be ~30 µs after TX), the slot width on the scope (1.333 µs @
   DSHOT600) and compare with `dshot_get_telemetry_slot_ticks()`.  Check TRGM
   routing (`TRGOCFG[0x3A/0x3B]`) and that PF02/PF03 actually switch to the TRGM
   function during RX (visible as a 100 ns glitch on the pad when the iomux is
   written; the line stays high via the pull-up).
6. **Running telemetry**: spin a motor with a known setup, e.g. 7 pole pairs at
   5000 mechanical RPM = 35000 eRPM.  `GET_TELEMETRY`/`HOST_CMD_DSHOT_GET_TLM`
   should report ~34965 eRPM (telemetry `0x5AD`, period 1716 µs, 0.1 %).  Cross
   check: `RPM = eRPM * 2 / pole_pairs`.
7. **Rate changes**: repeat 2-6 for DSHOT300 and DSHOT1200 with
   `PARAM_DSHOT_RATE` (expect the same frames at 3.33 µs / 0.83 µs bit period;
   DSHOT1200 requires a PWM0 clock fast enough — ≥ 48 MHz for a 20 tick period).
8. **AM32 4-way**: BEGIN on a bench ESC → scope: line idle HIGH; XFER of a
   request must show 52 µs/bit bytes and the ESC answer; `AM32_STATUS` must be
   `state=1`.  A 192 byte EEPROM read must return 197 bytes.  END must return
   the line to DShot (frames visible again within 1 ms).
9. **AM32 DShot programming**: with an AM32 ESC armed and motor stopped, write
   one byte with `dshot_prog_write_byte()` + `dshot_prog_save()`; the ESC beeps
   (save) and the new setting is visible after a power cycle.

Items that need the scope/logic analyser (no automated alternative):
step 3/4 (pulse widths + bit pattern), step 5 (edge delay / slot width),
step 8 (baud rate, half-duplex turnaround).

---

## 8. Host side simulation (`.work/dshot_test/dshot_sim.py`)

Pure Python port of `dshot_make_frame` / `dshot_crc_bidir` /
`dshot_telemetry_to_erpm` / `dshot_decode_edges` + a synthetic frame generator
(16 bit value → GCR → differential 21 bit line → edge timestamps with jitter).
Run: `python sdk_env_v1.13.0\tools\python3\python.exe .work\dshot_test\dshot_sim.py` → **ALL TESTS PASSED**.

Results:

```
1. CRC / frame builder: frame(0, tlm=1) = 0x001E, frame(0, tlm=0) = 0x000F,
   all 4096 12-bit payloads round-trip the inverted CRC.                     OK
2. eRPM mapping table (pole pairs = 7):
       1000 mech RPM -> tele 0xB0C -> 6996 eRPM  (0.06%)
       5000 mech RPM -> tele 0x5AD -> 34965 eRPM (0.10%)
      10000 mech RPM -> tele 0x3AD -> 69930 eRPM (0.10%)
   stopped -> tele 0xFFF -> 0 eRPM.                                          OK
3. 2000 random clean frames decode exactly.                                  OK
4. Jitter / ESC clock error, 3000 frames each:
       clean, +-2% clock, +-5% clock, 3 tick jitter, 2% + 4 tick:
          3000/3000 decoded, 0 wrong values
       harsh 5% clock + 8 tick jitter:
          2976/3000 decoded (99.2%), 0 wrong values                          OK
5. Damaged frames (missing edge, extra noise edge, truncated, random):
       never accepted a wrong value (all rejected).                          OK
6. First-gap ambiguity: all 4096 payloads decode; first gap is 1 slot for
   2560 payloads and 2 slots for 1536 (never 3 for valid frames).            OK
7. TX pulse widths: 100MHz/DSHOT600 -> 125/167 = 74.9% and 62/167 = 37.1%;
   also checked 24 MHz clock and DSHOT1200 (20 tick period).                 OK
8. Timing table (100 MHz GPTMR1): slot ticks and RX windows per DShot rate
   (DSHOT600: 133 ticks = 1.33 us, window 101.9 us; DSHOT150: 197.9 us).    OK
```

The "wrong value = 0" property is the important one: a corrupted frame is
always rejected (CRC + GCR validity), never silently decoded into a wrong eRPM.

---

## 9. Known risks / open items

1. **PWMV2 shadow semantics**: the design relies on
   `CMP_UPDATE_TIME = 010b` ("latch at related counter reload") while the
   shadow registers stay unlocked.  This is documented in UM §37.6.20 but not
   demonstrated by an SDK sample; if the first scope picture shows a one-bit
   shift, try `pwm_shadow_register_update_on_trigmux` driven by the CMP[16]
   trigger (hardware trigger) as the alternative.
2. **Start-of-frame race**: the "output start" and "DMA armed" ordering is
   protected by a short critical section; verify that the very first bit is
   never lost on an EtherCAT-heavy system (scope + DShot decoder).
3. **AM32 / BLHeli bootloader entry at power-up** is line-level dependent; a
   glitch on PF02/PF03 at ESC power-up can trap the ESC in the bootloader.  Board
   fix (if observed): keep the ESC lines low until the MCU is up, or power the
   ESCs after the board.
4. **No read-back** in the DShot programming path; the 4-way path read-back
   needs the PC tool's bootloader implementation and was not tested on hardware.
5. **1 kHz update rate**: hardware-driven TX/RX is independent of the main loop;
   if the main loop stalls > 1 ms the frame rate drops (AM32 tolerates this
   down to the ~100 ms signal timeout) but no frame is ever corrupted by CPU
   load.
6. The AM32 DShot programming mode is present in the AM32 `main` branch
   (`.work/refs/am32/am32_Src_dshot.c`); verify it exists in the ESC firmware
   version actually shipped (commands 36/37 are not part of the BLHeli_32
   standard).
7. `NRST`/`BOOT` interactions of the internal PHY etc. are outside this module.
