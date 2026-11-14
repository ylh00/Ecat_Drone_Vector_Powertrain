# S288 / J288 bus servo driver (`src/servo_s288.c`)

Driver for the two Unitree (宇树) **J288/S288** brushless bus servos of the
`ecat_drone_5e3y` EtherCAT powertrain board.

| | Servo #1 | Servo #2 |
|---|---|---|
| UART | UART2 | UART3 |
| TX pad (MCU → switch) | **PC08** | **PC15** |
| RX pad (switch → MCU) | **PC09** | **PC14** |
| Direction switch GPIO | **PC16** | **PD00** |
| Direction switch active level | `PARAM_SERVO_DIR_TX_LEVEL1` | `PARAM_SERVO_DIR_TX_LEVEL2` |
| Factory ID parameter | `PARAM_SERVO_ID1` | `PARAM_SERVO_ID2` |
| DMA channels (HPM_HDMA) | TX ch4 / RX ch5 | TX ch6 / RX ch7 |
| IRQ | `IRQn_UART2` | `IRQn_UART3` |
| Shared DMA IRQ | `IRQn_HDMA` | `IRQn_HDMA` |

Source of truth for the protocol: `无刷数字舵机J288S288使用手册.pdf`,
sections 五 (encoder), 七 (wiring / physical layer), 九 (protocol), 十 (fault
codes), 十一 (warning codes).  Page references below are the PDF pages.

---

## 1. Hardware / wiring

```
                     SPDT analogue switch
 MCU UART TX pad ──>o\                        ┌──────────────┐
                      \─── servo SIGNAL wire ─│  S288 / J288 │
 MCU UART RX pad <──o/        (1-Wire)        └──────────────┘
                      ^
                      │ control GPIO (PC16 / PD00)
```

* The bus is a **1-Wire single wire, half duplex** connection (manual p.5 §七).
  The servo signal line carries standard TTL UART timing in both directions.
* Because the board uses a separate TX and RX pad (not a wired-OR / open drain
  single pad), an **SPDT analogue switch** (`HC1G3157`-class) selects whether the
  MCU TX or RX pad is connected to the servo signal.  The switch is controlled by
  one GPIO; the firmware treats `PARAM_SERVO_DIR_TX_LEVELn` as the level that
  selects **TX**, the opposite level selects **RX**.  `BOARD_SERVO_DIR_SETUP_US`
  (2 µs) is waited after every switch-over.
* Power: 12 V / 25.2 V on the PH2.0 connector (manual p.3 §三); signal is 3.3 V
  TTL.  Check the switch VCC/level translation on the schematic before the first
  power-up (open question 1 below).
* One servo per bus on this board.  The protocol itself supports 15 ids
  (0…14, 15 = broadcast) on one bus (manual p.5); the driver polls exactly one
  configured id per bus, `servo_scan_bus()` can discover the id.
* Servo ids must be **unique per bus**.  The id is **not** configurable over the
  documented protocol - use the Unitree PC tool "舵机调试助手" + 1-Wire/USB
  adapter (manual p.6 §八, p.6 图2) once and store the id in `PARAM_SERVO_IDn`.

## 2. Electrical / UART configuration

* **6.0 Mbps, 8N1, no parity** (manual p.5 §七 and p.7 §九).  `PARAM_SERVO_BAUD`
  must be 6000000 for a real servo; the driver reads it at init and supports
  other rates only for experiments / bench bring-up.
* HPM5E00 UART baud = `src_clk / (osc * div)`, `osc` = 8…32 (even),
  `div` = 1…65535, ≤3 % frequency error (`hpm_uart_drv.c`,
  `uart_calculate_baudrate()`).
  Board clock: **UART2/3 = 48 MHz** (PLL0CLK0 480 MHz / 10) →
  **6.000 Mbps exact** with `osc = 8, div = 1`.
  `servo_uart_clock_setup()` verifies the clock at runtime and, if a future
  board revision changes it to something that cannot produce the requested
  baud, falls back to `PLL0CLK0 / 1` (480 MHz → `osc = 8, div = 10`).
* RX DMA: HPM_HDMA, one channel per direction per bus, buffers placed in the
  `.noncacheable` section (`ATTR_PLACE_AT_NONCACHEABLE`, HPM5E00 has a 32 KB
  L1 D-cache; `hpm_sdk/samples/drivers/uart/uart_dma` uses the same pattern).
* DMA/DMAMUX/IRQ setup follows
  `hpm_sdk/samples/drivers/uart/uart_dma/src/uart.c` and
  `hpm_sdk/samples/drivers/uart/uart_hardware_rx_idle/src/uart.c`
  (`dma_default_handshake_config()` + `dma_setup_handshake()`,
  `dmamux_config()`, `SDK_DECLARE_EXT_ISR_M()`).
* IRQ priorities: UART2/UART3 = 2, HDMA = 3.  The UART IRQ is short (flag
  handling + bus turn-around), the DMA IRQ only records transfer completion.

## 3. Protocol

### 3.1 Command frame (host → servo) — 20 bytes, manual p.8

| Offset | Type | Field | Unit / notes |
|---|---|---|---|
| 0 | u8[2] | `head` | `0xFE 0xEE`, **included in the CRC** |
| 2 | packed u8 | `id:4 \| mode:3 \| timeout:1` | id 0…14, 15 = broadcast; mode 0 = locked/damped, 1 = FOC closed loop; timeout 0 = protection disabled, 1 = enabled (default, 1 s) |
| 3 | u8 | reserved | 0 |
| 4 | i16 LE | `tor_des` | rotor torque feed-forward, 256000 = 1 Nm |
| 6 | i16 LE | `spd_des` | rotor speed feed-forward, 2.56/2π = 1 rad/s |
| 8 | i32 LE | `pos_des` | rotor position, 32768/2π = 1 rad |
| 12 | i16 LE | `k_pos` | rotor stiffness, 1280000 = 1 Nm/rad |
| 14 | i16 LE | `k_spd` | rotor damping, 128000000 = 1 Nm/(rad/s) |
| 16 | u32 LE | `CRC32` | over bytes **0…15** (4 words, head included) |

The driver sends a **pure position PD control frame**: `tor_des = 0`,
`spd_des = 0`, `pos_des` from the target angle, `k_pos`/`k_spd` from
`PARAM_SERVO_KPn`/`PARAM_SERVO_KDn`.  All 20 bytes are transmitted by the TX
DMA, the CRC is computed by `servo_crc32()`.

### 3.2 Feedback frame (servo → host) — 26 bytes, manual p.9

| Offset | Type | Field | Unit / notes |
|---|---|---|---|
| 0 | u8[2] | `head` | `0xFC 0xEE`, **NOT included in the CRC** |
| 2 | packed u8 | `id:4 \| mode:3 \| timeout:1` | mode / timeout flag of the servo; timeout = 1 means the servo reports timeout protection triggered |
| 3 | i8 | `temp` | housing temperature, °C |
| 4 | u8 | `sensor` | winding temperature, °C |
| 5 | u8 | `vol` | bus voltage, 0.5 V/LSB (255 = 127.5 V) |
| 6 | i16 LE | `torque` | rotor torque, 256000 = 1 Nm |
| 8 | i16 LE | `speed` | rotor speed, 2.56/2π = 1 rad/s |
| 10 | i32 LE | `pos` | rotor position, 32768/2π = 1 rad |
| 14 | u32 LE | `MError` | fault bitfield (manual p.11, see §3.4) |
| 18 | u16 LE | `OutPos:13 \| ExFlag:3` | output encoder 8192 = 1 turn; ExFlag = 3 bit warning code |
| 20 | u8 | `ExSensor2` | reserved (manual: `res` low byte) |
| 21 | u8 | `ExCom` | reserved (manual: `res` high byte) |
| 22 | u32 LE | `CRC32` | over bytes **2…21** (5 words, head excluded) |

**Important detail about the manual's C struct:** `MotorData_t` has a
`uint8_t NoUse[2]` member in front of `head[2]` with the comment "does not exist
on the wire, alignment".  This is why the feedback CRC covers 20 bytes starting
at byte 2 of the wire frame (5 complete 32 bit words) while the command CRC
covers 16 bytes from byte 0: both CRC windows are word aligned in the original
struct definition.  The wire frame itself is exactly 26 bytes with no padding.
This was independently confirmed against the vendor reference implementation
(`crc32_lookup(data[2:22])` in `python/servo_demo.py`, and
`crc32_lookup(&recv.mode, sizeof(...)-8)` in `stm32/firmware/App/protocol.c`).

### 3.3 CRC32 (`crc32_core()`, manual pp.9-10)

```
polynomial = 0x04C11DB7, init = 0xFFFFFFFF, MSB first, no reflection,
no final XOR; the CRC is computed on little-endian 32 bit words, i.e. the
bytes inside every 4 byte group are consumed in reverse order (bit31 first).
```

`servo_crc32()` implements exactly the manual's loop:
`for each word: for bits 0..31: crc = (crc<<1) ^ ((crc_msb ^ data_bit) ? poly : 0)`.
Equivalence with the vendor table driven `crc32_lookup()` is unit tested
(600 random frames + the worked examples below).

### 3.4 Fault and warning codes (manual p.11)

`MError` (32 bit): `0x01` over current, `0x02` transient over voltage,
`0x04` persistent over voltage, `0x08` under voltage, `0x10` chip over
temperature, `0x20` MOS over/under temperature, `0x40` MOS temperature
abnormal, `0x80` housing over/under temperature, `0x100` housing temperature
abnormal, `0x200` winding over temperature, `0x400` rotor encoder 1,
`0x800` rotor encoder 2, `0x1000` output encoder, `0x2000` stored data error,
`0x4000` abnormal reset, `0x8000` waiting unlock, `0x10000` driver validation,
`0x20000` calibration mode, `0x40000` communication CRC error, `0x80000`
driver version too low, `0x100000` firmware model error, `0x200000` encoder
angle abnormal.

`ExFlag` (3 bit warning code): `0x01` low supply voltage warning, other values
"unknown warning" (manual p.11 表2/表1 are swapped in the extracted text; the
bit meaning is identical to the fault table).

The driver exports the **low byte** of `MError` in the legacy
`servo_status_t.error` field (the 8-bit TxPDO field) and the full 32 bit word
in the appended `m_error` field.

### 3.5 Mode / enable mapping

| API call | Frame | Meaning |
|---|---|---|
| `servo_set_enable(ch, 1)` | mode = 1 (FOC closed loop), `k_pos`/`k_spd` from params, `pos_des` = target | normal position servo operation |
| `servo_set_enable(ch, 0)` | mode = 0 (locked/damped), `k_pos = k_spd = 0` | torque output disabled, rotor damped/locked |
| `servo_clear_fault(ch)` | one frame with **timeout bit = 0** | the S288 clears an active timeout protection when it receives a control frame with the timeout bit cleared (manual p.8); MError hardware faults are not clearable over the protocol |
| normal polling | timeout bit = 1 | 1 s timeout protection stays armed; we poll at `PARAM_SERVO_POLL_HZ` so the servo never trips it during normal operation |
| id 15 (`PARAM_SERVO_IDn = 15`) | id = 15 | broadcast, the servo(s) never answer; no response is expected/awaited |

The frame also always contains `tor_des = 0` and `spd_des = 0`; the protocol
of this servo generation has no torque limit / feed-forward parameter that maps
to `PARAM_SERVO_TORQUE_LIMITn` (open question 6).

### 3.6 Worked example frames (actual bytes)

Default gains: `PARAM_SERVO_KP = 25600000` → `k_pos = 307`
(`Kp = 20.0 Nm/rad` at the output), `PARAM_SERVO_KD = 128000000` →
`k_spd = 1539` (`Kd = 1.0 Nm/(rad/s)` at the output).

Command, servo id 1, FOC closed loop, timeout protection on, target +30.0 deg
(`pos_des = 787398`):

```
FE EE 91 00 00 00 00 00 C6 03 0C 00 33 01 03 06 BA 6A 44 45
^head ^mode=0x91 (id=1, mode=1, timeout=1)          ^CRC32 = 0x45446ABA
```

More command examples:

```
id1 FOC timeout   -30.0 deg, default gains : FE EE 91 00 00 00 00 00 3A FC F3 FF 33 01 03 06 74 79 57 F7
id2 LOCK timeout  -30.0 deg (disabled)     : FE EE 82 00 00 00 00 00 3A FC F3 FF 00 00 00 00 66 CD 5B BC
id1 FOC timeout = 0 (clear protection)     : FE EE 11 00 00 00 00 00 00 00 00 00 33 01 03 06 C1 2D F2 8C
broadcast id 15 LOCK (no response)         : FE EE 8F 00 00 00 00 00 00 00 00 00 00 00 00 00 0F 42 AF 86
```

Feedback example (id 1, output encoder 4096 = 180.0 deg, speed 1280 raw =
104 rpm, torque 12800 raw = 1442 (0.01 Nm output), 48 × 0.5 V = 24.0 V,
housing/winding 25/40 °C, MError = 0x00040001, ExFlag = 1):

```
FC EE 11 19 28 30 00 32 00 05 00 40 00 00 01 00 04 00 00 30 00 00 27 65 98 1F
```

(XX = value byte, everything after byte 2 is covered by the CRC, the head is
not.)

## 4. Software design

### 4.1 Non-blocking state machine (per bus, both buses in parallel)

```
IDLE ──(poll tick)──> TX_SETUP ──(>= 2 µs)──> TX (DMA writes 20 B)
   ^                                             │
   │                        UART TX idle IRQ (end of last stop bit)
   │                                             v
   └── frame parsed <── RX (RX DMA armed for 26 B, RX idle + 3 ms timeout)
```

* `servo_task()` is called from the main loop; it never blocks longer than the
  2 µs direction switch set-up.  All waiting is state based, timed with the
  RISC-V core cycle counter (`hpm_csr_get_core_cycle()`), so no busy waits.
* **Turn-around** happens in the UART IRQ: `HPM_IP_FEATURE_UART_TX_IDLE_DETECT`
  detects that the TX line has been idle (high) for `SERVO_TX_IDLE_THRESHOLD_BITS`
  bit times after the frame; the handler immediately switches the SPDT to RX
  and arms the RX DMA.  A stale/pre-frame idle flag is filtered with a minimum
  frame duration check (`min_tx_cycles`).
* **Fallback** in the TX state: if the TX idle interrupt does not fire (feature
  missing / misconfigured), the main loop turns the bus around when the TX DMA
  transfer complete flag **and** UART `TEMT` (transmitter empty) are set.
  This costs a main-loop iteration of latency and is only a safety net.
* **RX**: one DMA channel is armed for exactly 26 bytes, so "transfer complete"
  means "complete response frame".  In addition the RX line idle detector and a
  3 ms timeout (`APP_CFG_SERVO_UART_TIMEOUT_US`) abort short/missing responses.
  The frame is CRC checked and parsed in the main loop (not in the ISR).
* Poll scheduling uses `g_app_tick_ms` (1 ms board tick) with
  `PARAM_SERVO_POLL_HZ` (10…1000 Hz, default 100 Hz).  A changed target angle or
  enable state is sent with the next main-loop iteration.  Bus scans run
  back-to-back with a 1 ms gap.
* A missing / not responding servo is handled gracefully: transaction timeout →
  `err_count++`, `online = 0` after 3 consecutive errors, no fault bits are set
  by the driver itself (the fault logic lives in `app_task.c`).

### 4.2 Timing budget at 6 Mbps

| Phase | Time |
|---|---|
| direction switch set-up | 2.0 µs |
| command frame 20 B (8N1) | 33.3 µs |
| TX idle threshold (2 bits) | 0.33 µs |
| feedback frame 26 B | 43.3 µs |
| **one poll transaction** | **≈ 79 µs** (0.8 % of a 100 Hz period) |
| response timeout | 3000 µs |
| scan of ids 0…14 | ≤ 16 × (79 µs … 3 ms) ≈ 1.3 ms worst case 48 ms |

### 4.3 Parameter mapping

| Parameter | Used for |
|---|---|
| `PARAM_SERVO_BAUD` | UART baud (must be 6000000), applied by `servo_set_baudrate()` (called from `param_apply()`) |
| `PARAM_SERVO_IDn` | servo id per bus (0…14, 15 = broadcast) |
| `PARAM_SERVO_DIR_TX_LEVELn` | SPDT switch level that selects TX (the opposite level selects RX) |
| `PARAM_SERVO_ANGLE_GAINn` | target gain, 100 = 1.00x, applied in `servo_set_target_d10()` |
| `PARAM_SERVO_ANGLE_OFSn` | target offset in 0.1 deg, applied after the gain |
| `PARAM_SERVO_ANGLE_MINn` / `MAXn` | target clamp in 0.1 deg, applied last |
| `PARAM_SERVO_POLL_HZn` | polling rate (10…1000 Hz) |
| `PARAM_SERVO_KPn` | `k_pos` field = `param / RATIO²` (`RATIO = 70070/243`), default 25600000 → 307 |
| `PARAM_SERVO_KDn` | `k_spd` field = `param / RATIO²`, default 128000000 → 1539 |
| `PARAM_SERVO_TORQUE_LIMITn` | **not used** - the documented protocol has no torque limit field |
| `PARAM_SERVO_ENABLE_ON_BOOT` | applied by the control task (`app_task.c`), not by the driver |

Conversion of the target angle (0.1 deg at the output) to the rotor position
command: `pos_des = round(angle_d10 * 32768 * RATIO / 3600)`
(90.0 deg → 2 362 195, 360.0 deg → 9 448 781).

### 4.4 Telemetry mapping (`servo_status_t`)

| Field | Source |
|---|---|
| `pos_d10` | output encoder `OutPos` (13 bit, 8192 = one output turn): `pos_d10 = round(OutPos * 3600 / 8192)` → 0…3599 (0.1 deg) |
| `speed_rpm` | rotor speed raw × `60 / (2.56 · RATIO)` (Q16 = 5327), i.e. output rpm |
| `torque_cnm` | rotor torque raw / 256000 × `RATIO`, in 0.01 Nm at the output (Q16 = 7382) |
| `current_ma` | the S288 does **not** report the phase current. Estimated from the rotor torque with an assumed torque constant `Kt = 0.1 Nm/A` (`I = τ_rotor / Kt`); constant `SERVO_MOTOR_KT_NM_PER_A` in the source. Recalibrate if the real Kt is known |
| `voltage_mv` | `vol` × 500 mV |
| `temp_c` | `max(housing temp (int8, clamped to 0), winding temp)` |
| `error` | low byte of `MError` (8 bit TxPDO field) |
| `online` / `enabled` | response within 3 polls / mode field = FOC from the feedback |
| `ok_count` / `err_count` / `last_ok_tick_ms` | driver bookkeeping |
| `m_error`, `warn`, `timeout_flag`, `out_pos`, `pos_raw`, `speed_raw`, `torque_cnm` | appended raw diagnostic fields (host tool) |

**Position convention:** the output side is absolute only within one output turn
(the 13 bit output encoder).  The rotor position (`pos_raw`) is absolute only
within one rotor turn *and* only counts rotor revolutions since power-on
(manual p.4 §五 explains the single-turn absolute encoder).  One output turn =
288.35 rotor turns, so the rotor position cannot be used to reconstruct the
output angle; the driver therefore reports the output encoder value for
`pos_d10` (0…3600).  The master should treat the servo angle as a
single-turn/wrapped quantity.  Values outside ±360.0 deg in
`PARAM_SERVO_ANGLE_MINn/MAXn` are accepted by the parameter system but the
feedback can only represent one turn.

## 5. API / usage

The complete API is in `inc/servo_s288.h` (extended compatibly; the functions
called by `app_task.c`, `param.c` and `host_proto_app.c` are unchanged):

```c
servo_init();                                   /* UARTs, GPIOs, DMA, IRQs     */
servo_set_target_d10(ch, angle_d10);            /* 0.1 deg, gain/ofs/clamp     */
servo_set_enable(ch, enable);                   /* 0.1 -> FOC, 0 -> locked     */
servo_clear_fault(ch);                          /* clears servo timeout prot.  */
servo_task();                                   /* non-blocking state machine  */
servo_get_status(ch, &status);
servo_set_baudrate(baud);                       /* from param_apply()          */
servo_scan_bus(ch, id_min, id_max, id_bitmap);  /* NON blocking, see below     */
servo_write_register(...);                      /* -1: not supported           */
servo_read_register(...);                       /* -1: not supported           */
```

### 5.1 Bus scan (`servo_scan_bus`)

The protocol has no scan command, so the driver probes every id by sending a
normal control frame and waiting for a (CRC valid, matching id) response:

* `id_bitmap` = 32 bytes, bit `id & 7` of byte `id >> 3`; take a `memset(0)`
  before the first call (the driver also clears it when it starts a scan).
* Return: `SERVO_SCAN_RUNNING (0)` while running (call again with the same
  arguments), `SERVO_SCAN_DONE (1)` exactly once when the bitmap is valid,
  `SERVO_ERR_INVALID_ARG (-2)` / `SERVO_ERR_IO (-3)` on errors.
* Only ids 0…14 are addressable by the S288; ids 15…253 are reported as absent
  without transmitting (id 15 is broadcast, the servo never answers).
* Normal position control polling is suspended while a scan runs and resumed
  when it finishes.

### 5.2 Unsupported functions

The documented protocol has **no register read/write and no set-id command**
(only the Unitree PC debug assistant has those, with undocumented frames).
`servo_write_register()` / `servo_read_register()` return
`SERVO_ERR_UNSUPPORTED (-1)`; the host protocol layer maps
`HOST_CMD_SERVO_RAW` / `HOST_CMD_SERVO_SET_ID` to "unsupported".  Change the
servo id with the Unitree PC tool once (see §1).

## 6. Verification

### 6.1 Compile

```powershell
python D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\scripts\check_syntax.py `
    --build-dir D:\Ecat_Drone_Vector_Powertrain\.work\build_dev src\servo_s288.c
# -> All 1 file(s) OK  (no warnings, -Wall)
```

### 6.2 Frame unit test (no hardware needed)

`.work/servo_test/test_servo_frames.py` re-implements the protocol model and
checks it against `src/servo_s288.c`:

* CRC32: manual word-based `crc32_core()` ≡ vendor `crc32_lookup()` on 600
  random command/feedback frames (and the unaligned feedback window at byte 2).
* the frame constants/offsets/scaling factors parsed out of the C source match
  the protocol model (catches accidental drift),
* command frames: 20 B, head, packed `id/mode/timeout`, little-endian multi
  byte fields, negative positions (two's complement), broadcast id 15, locked
  mode, clear-timeout frame, gain conversion, known CRC,
* feedback frames: 26 B, CRC scope (head excluded), output-encoder position
  (4096 → 180.0 deg), rpm/torque/current/voltage/temperature conversion, all 22
  documented `MError` bits, rejection of bad head / bad CRC / wrong id,
* scan bitmap layout and id range semantics,
* timing budget at 6 Mbps.

```powershell
python D:\Ecat_Drone_Vector_Powertrain\.work\servo_test\test_servo_frames.py
# -> RESULT: 58 passed, 0 failed / ALL TESTS PASSED
```

`gen_doc_frames.py` regenerates the worked examples of §3.6.

### 6.3 Bench / scope verification (must be done on hardware)

1. **UART clock**: at the debug console the driver logs
   `S288: UART2/UART3 @ 6000000 baud`; if UART init failed a `[E]` line is
   logged.  Verify with the scope: PC08 (servo 1 TX) idle high, a 20 byte burst
   of 33.3 µs = 6.000 Mbps (166.7 ns/bit) once per poll period (10 ms at
   100 Hz).
2. **Direction switch**: probe PC16 (servo 1) / PD00 (servo 2) together with
   PC08/PC09.  The GPIO must switch to the TX level before the first start bit,
   stay there for the whole 20 byte frame and switch back to RX within ~1 bit
   time after the last stop bit.  Check the switch polarity with a multimeter
   first and set `PARAM_SERVO_DIR_TX_LEVELn` accordingly (default = 1).
3. **Response**: with the servo powered (12 V/25.2 V) and the correct ID
   configured, PC09/PC14 must show the 26 byte response frame (43.3 µs) starting
   shortly after the command.  On the scope measure the gap between the last
   command stop bit and the first response start bit; it must be larger than
   the turn-around time (~2.3 µs).  If the first response byte is lost
   (`err_count` increases, `online=0`) reduce `SERVO_TX_IDLE_THRESHOLD_BITS`
   (2 bits) / check the interrupt, see open question 2.
4. **Bus scan**: power exactly one servo, set its id to e.g. 5, run the host
   tool scan over ids 0…14 → only bit 5 set.  Scan of an empty bus must finish
   with an all-zero bitmap (≈ 48 ms worst case).
5. **Position loop**: enable the servo, command a slow angle ramp, check the
   logged `pos_d10` follows the target (the 0.1 deg values are output units).
   Tune `PARAM_SERVO_KPn/KDn` if the servo oscillates or is too soft.
6. **Missing servo**: unplug the servo, the bus must keep polling without
   blocking the main loop; `online` goes 0 after 3 polls, no fault is raised by
   the driver.

### 6.4 Calibration

The protocol scaling is fixed by the servo firmware, so only the mechanical
zero and the control gains need calibration on the machine:

1. **Output zero (angle offset)**: enable the servo in FOC mode, command
   `0.0 deg` (`servo_set_target_d10(ch, 0)`), let it settle and read
   `pos_d10` from the host telemetry.  Set `PARAM_SERVO_ANGLE_OFSn =
   -pos_d10` (0.1 deg units, modulo 3600) so that the mechanical zero of the
   mechanism corresponds to target 0.  Verify by commanding ±(a few hundred)
   units and comparing the sign of the measured position with the mechanism.
2. **Angle gain**: if the measured output angle differs from the commanded one
   by a constant factor (mechanical linkage ratio), set
   `PARAM_SERVO_ANGLE_GAINn = 100 * measured / commanded` and re-check two
   points.  `PARAM_SERVO_ANGLE_MINn/MAXn` limit the commanded range of the
   mechanism (with the gain/offset already applied).
3. **Voltage**: `vol` is fixed at 0.5 V/LSB in the servo firmware; verify
   `voltage_mv` against a multimeter at the servo connector (no calibration
   parameter needed, this is a plausibility check of the wiring).
4. **Temperature**: `temp_c` is `max(housing, winding)`; check against ambient
   and, under load, against an IR thermometer on the housing.  The protocol has
   no offset for the temperature.
5. **Torque / current estimate**: `torque_cnm` is exact protocol scaling
   (`raw/256000 * RATIO`).  `current_ma` is an *estimate* `τ_rotor / Kt` with
   `SERVO_MOTOR_KT_NM_PER_A = 0.1` in the source.  To calibrate it, lock the
   rotor (or hold a known load), measure the real phase current with a clamp
   meter / shunt and adjust that constant.
6. **Position gain / damping** (`PARAM_SERVO_KPn/KDn`, sent as `k_pos/k_spd =
   param / RATIO²`): start with the defaults (Kp = 20 Nm/rad, Kd =
   1 Nm/(rad/s) at the output) and a small step target; increase `KPn` until a
   small overshoot appears, then increase `KDn` until it is damped.  The int16
   protocol fields clamp at 32767, which corresponds to Kp ≈ 2128 Nm/rad /
   Kd ≈ 21.3 Nm/(rad/s) at the output (vendor limit) - larger parameter values
   are silently clamped on the wire.
7. **One output turn check**: with the output shaft on a reference mark, rotate
   exactly one revolution and verify `pos_d10` wraps 0 → 3599 (`OutPos`
   0 → 8191) and that 3600 commanded units equal one output turn.

## 7. Open questions / risks

1. **SPDT switch polarity / level**: the default `PARAM_SERVO_DIR_TX_LEVELn`
   is 1 (`BOARD_SERVO_DIR_TX_LEVEL_DEFAULT`).  Verify against the schematic /
   measure PC16 and PD00 before powering the servo - a wrong level drives both
   servo and MCU TX onto the wire.
2. **Turn-around timing** is the main hardware risk: the S288 response may
   start before the switch + TX-idle detection have turned the bus around.  The
   driver switches within ~2.3 µs of the last stop bit (2 µs set-up + 0.33 µs
   idle threshold).  If the first response byte is lost, options: lower
   `SERVO_TX_IDLE_THRESHOLD_BITS`, select `uart_rxline_idle_cond_*` differently,
   or check the actual switch propagation delay.  The main-loop fallback
   (TEMT) is only a safety net and is slower.
3. **Servo ID assignment**: not possible over the documented protocol (only with
   the Unitree PC 1-Wire adapter).  The factory default id of a new servo must
   be determined with the PC tool or `servo_scan_bus()`; document the procedure
   for production.
4. **6 Mbps signal integrity**: 6 Mbps over the PH2.0 cable + SPDT switch needs a
   short, well terminated signal path; check rise time / overshoot on the
   scope.  A too long cable / wrong switch can corrupt frames (CRC errors show
   up as `err_count`).
5. **Current estimate** assumes `Kt = 0.1 Nm/A`; the S288 has no current
   telemetry.  If the application needs real current, it must be measured
   externally or the servo firmware protocol extended.
6. **Torque limit**: `PARAM_SERVO_TORQUE_LIMITn` cannot be transmitted with this
   protocol version (no torque-limit field in the 20 byte frame); the driver
   ignores it.  Torque limiting has to be done by the master (position target
   limits) or by a servo firmware feature.
7. **Rotor position only relative to power-on** (single-turn absolute encoder,
   manual p.4); `OutPos` (13 bit output encoder) is the only absolute output
   angle and wraps every turn.
8. **PD gains** default to `Kp = 20 Nm/rad`, `Kd = 1 Nm/(rad/s)` at the output
   (`k_pos = 307`, `k_spd = 1539`).  These are conservative starting values, not
   tuned on the real mechanism.
9. **Multi servo per bus**: the driver polls one id per bus.  If more servos are
   put on one bus, a round-robin scheduler must be added (the protocol supports
   15 ids, manual p.5).
10. **Fault/status mapping**: only the low byte of `MError` fits the current
    8 bit TxPDO field; the full word is available in the extended status struct
    but not yet in the EtherCAT PDO (contract decision, `ecat_pdo.h` is Lead
    owned).

## 8. References

* `无刷数字舵机J288S288使用手册.pdf` - §五 p.4 (single turn absolute encoder),
  §七 p.5 (1-Wire, 6 Mbps, 15 ids), §八 p.6 (PC tool, wiring figure), §九
  pp.7-10 (command/feedback frames, CRC), §十/十一 p.11 (fault/warning codes).
* Unitree official reference implementation:
  <https://github.com/unitreerobotics/digital_servo> -
  `specs/protocol.md` (protocol summary), `python/servo_demo.py`
  (crc32_lookup + conversion formulas), `stm32/firmware/App/protocol.c`
  (command build / feedback extract).
* hpm_sdk v1.13.0: `samples/drivers/uart/uart_dma/src/uart.c`,
  `samples/drivers/uart/uart_hardware_rx_idle/src/uart.c`,
  `samples/drivers/uart/uart_irq/src/uart.c`,
  `samples/drivers/dmav2/dmav2_uart_rx_circle_transfer/src/dma.c`,
  `drivers/inc/hpm_uart_drv.h`, `drivers/inc/hpm_dmav2_drv.h`,
  `drivers/inc/hpm_dmamux_drv.h`, `drivers/inc/hpm_gpio_drv.h`,
  `soc/HPM5E00/HPM5E31/hpm_clock_drv.h`.
* HPM5E00 user manual §48 (UART, IDLE_CFG/IIR2 RX/TX idle detection,
  pp.774-787 of the PDF) for the TX/RX idle detection used for the bus
  turn-around.
