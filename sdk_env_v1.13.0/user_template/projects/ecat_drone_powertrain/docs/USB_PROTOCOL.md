# USB host protocol — ecat_drone_powertrain

PC tuning tool <-> board communication over the USB0 CDC-ACM virtual COM port.

* Firmware: `ecat_drone_powertrain` (HPM5E3Y, board `ecat_drone_5e3y`)
* Headers that define the contract: `inc/host_proto.h` (framing/commands),
  `inc/param.h` (parameter ids), `inc/usb_cdc_app.h` (transport API).
* USB identity: VID `0x0A4D`, PID `0xE3A2`, product string
  `ecat_drone_powertrain`, manufacturer `HPMicro`.
* The CDC-ACM line coding is ignored by the firmware (full USB bulk speed).
  The host tool opens the port at any rate (e.g. 115200 8N1) and only uses
  the byte stream.

All multi-byte integers are **little endian**. Fixed size strings are UTF-8,
NUL terminated and zero padded.

---

## 1. Frame format

```
+------+------+-----+-----+-------+-------+===============+-------+-------+
| 0xA5 | 0x5A | CMD | SEQ | LEN_L | LEN_H |    PAYLOAD    | CRC_L | CRC_H |
+------+------+-----+-----+-------+-------+===============+-------+-------+
  sync   sync   1B    1B    2B              LEN bytes        2B
```

* `LEN` = payload length, `0 .. 512` (`APP_CFG_HOST_PROTO_MAX_PAYLOAD`).
* `SEQ` is chosen by the host and echoed in the response. The device also
  echoes `CMD`.
* `CRC16` = **CRC16-CCITT-FALSE** over `CMD | SEQ | LEN_L | LEN_H | PAYLOAD`
  (that is, over every byte after the two sync bytes and before the CRC):
  polynomial `0x1021`, init `0xFFFF`, no bit reflection, no final XOR.
  Reference check value: CRC of the ASCII bytes `123456789` is `0x29B1`.
* Every request is answered by **exactly one** response frame with the same
  CMD and SEQ. The first response payload byte is the status code below; the
  command answer follows it.
* A request that fails the CRC is still answered with status
  `HOST_STATUS_ERR_CRC` (so the host can count link errors) but is not
  executed.
* The device resynchronises by searching for `A5 5A` again after a bad sync
  byte or a bad CRC; the host should do the same. See section 12.

### Status codes (`payload[0]` of every response)

| Value | Name                        | Meaning                                      |
|-------|-----------------------------|----------------------------------------------|
| 0x00  | HOST_STATUS_OK              | success                                      |
| 0x01  | HOST_STATUS_ERR_UNKNOWN_CMD | command id not implemented                   |
| 0x02  | HOST_STATUS_ERR_LENGTH      | request payload length wrong                 |
| 0x03  | HOST_STATUS_ERR_CRC         | frame CRC wrong (request not executed)       |
| 0x04  | HOST_STATUS_ERR_PARAM_ID    | parameter id / servo id invalid              |
| 0x05  | HOST_STATUS_ERR_PARAM_RANGE | value out of range                           |
| 0x06  | HOST_STATUS_ERR_PARAM_RO    | parameter is read only                       |
| 0x07  | HOST_STATUS_ERR_BUSY        | operation in progress (retry, e.g. scan)     |
| 0x08  | HOST_STATUS_ERR_NOT_READY   | refused for safety (EtherCAT OP / outputs on)|
| 0x09  | HOST_STATUS_ERR_TIMEOUT     | device/servo/ESC did not answer              |
| 0x0A  | HOST_STATUS_ERR_UNSUPPORTED | command not supported by this firmware       |
| 0x0B  | HOST_STATUS_ERR_FLASH       | parameter flash save/load failed             |

When an error occurs the response carries only the status byte (`LEN = 1`).

---

## 2. Commands

`req` = request payload, `rsp` = response payload after the status byte.

### Identification / status

| CMD  | Name                | Request                    | Response |
|------|---------------------|----------------------------|----------|
| 0x01 | HOST_CMD_PING       | –                          | `u32 magic`, `u16 fw_major`, `u16 fw_minor`, `u16 fw_patch` |
| 0x02 | HOST_CMD_GET_VERSION| –                          | `u16 major`, `u16 minor`, `u16 patch`, `char build_date[16]` |
| 0x03 | HOST_CMD_GET_TELEMETRY | –                       | 54-byte `host_telemetry_t` (section 3) |
| 0x04 | HOST_CMD_GET_ECAT_STATUS | –                      | `u8 al_state`, `u8 link`, `u16 fault_code`, `u32 ecat_cycle_miss` |

* `magic` is `HOST_PROTO_PING_MAGIC = 0x474E4950`, i.e. the bytes
  `50 49 4E 47` = `"PING"` on the wire.
* `link` bit0 = port0 link, bit1 = port1 link.
* `ecat_cycle_miss` = missed EtherCAT cycles / DC sync jitter counter.

### Parameters (`inc/param.h`)

| CMD  | Name                     | Request                 | Response |
|------|--------------------------|-------------------------|----------|
| 0x10 | HOST_CMD_GET_PARAM       | `u16 id`                | `i32 value` |
| 0x11 | HOST_CMD_SET_PARAM       | `u16 id`, `i32 value`   | – (device applies it immediately) |
| 0x12 | HOST_CMD_GET_PARAM_DESC  | `u16 id`                | 70-byte descriptor (section 4) |
| 0x13 | HOST_CMD_PARAM_SAVE      | –                       | – (erases/programs the parameter flash sector) |
| 0x14 | HOST_CMD_PARAM_RESET     | –                       | – (RAM defaults only, call SAVE to persist) |
| 0x15 | HOST_CMD_PARAM_GET_COUNT | –                       | `u16 count` |
| 0x16 | HOST_CMD_LIST_PARAMS     | `u16 start`, `u8 count` | `count * 70` bytes of descriptors |

* `id` is `param_id_t` from `inc/param.h`; there are currently **60**
  parameters (`param_count()` / `HOST_CMD_PARAM_GET_COUNT`).
* `LIST_PARAMS` returns **at most 7 descriptors per response**
  (`HOST_PROTO_LIST_PARAMS_MAX`); the host must iterate with `start` until it
  has all parameters (e.g. 0..6, 7..13, ...). The number of descriptors in
  the response is `(LEN - 1) / 70`.
* `start` beyond the parameter count returns status OK with `LEN = 1`.

### DShot / ESC

| CMD  | Name                   | Request                    | Response |
|------|------------------------|----------------------------|----------|
| 0x20 | HOST_CMD_DSHOT_TEST    | `u8 esc`, `u16 throttle_us`| – |
| 0x21 | HOST_CMD_DSHOT_GET_TLM | `u8 esc`                   | 26-byte `host_dshot_tlm_t` |
| 0x22 | HOST_CMD_DSHOT_CMD     | `u8 esc`, `u16 dshot_cmd`  | – |

* `esc` = 0 or 1; for DSHOT_CMD `esc = 0xFF` broadcasts to both ESCs.
* `throttle_us` = 0 (stop) or 1000..2000 µs; the firmware maps it to the raw
  DShot throttle range (PARAM_DSHOT_MIN/MAX_THROTTLE) like the EtherCAT path.
* DSHOT_TEST / DSHOT_CMD are **refused with ERR_NOT_READY while the EtherCAT
  master is in OP**, and while a bench lease is active they also acquire a
  fresh lease (section 8).
* DShot telemetry blob (`host_dshot_tlm_t`, 26 bytes):

| Offset | Type   | Field |
|--------|--------|-------|
| 0      | u32    | erpm (raw electrical rpm, 0 = stopped) |
| 4      | u16    | period_us (raw eRPM period, 0 = no frame) |
| 6      | u8     | valid (1 = last frame CRC ok) |
| 7      | u8     | state (`dshot_state_t`) |
| 8      | u16    | crc_errors (consecutive decode errors) |
| 10     | u32    | ok_count |
| 14     | u32    | err_count |
| 18     | u32    | timeout_count |
| 22     | u32    | last_ok_tick_ms |

### S288 bus servo

| CMD  | Name                      | Request                                  | Response |
|------|---------------------------|------------------------------------------|----------|
| 0x30 | HOST_CMD_SERVO_SCAN       | `u8 bus`, `u8 id_min`, `u8 id_max`       | `u8 bitmap[32]` |
| 0x31 | HOST_CMD_SERVO_MOVE_TEST  | `u8 bus`, `u8 id`, `i16 angle_d10`       | – |
| 0x32 | HOST_CMD_SERVO_READ       | `u8 bus`, `u8 id`                        | 20-byte `host_servo_status_t` |
| 0x33 | HOST_CMD_SERVO_SET_ID     | `u8 bus`, `u8 old_id`, `u8 new_id`       | – (currently ERR_UNSUPPORTED) |
| 0x34 | HOST_CMD_SERVO_RAW        | `u8 bus`, `u8 id`, `u16 reg`, `u8 wr_len`, `u8 rd_len`, `u8[wr_len] data` | `u8 rd_len_actual`, `u8[...] data` |

* `bus` = 0 (UART2, servo 1) or 1 (UART3, servo 2).
* `id` must match the configured `PARAM_SERVO_ID1` / `PARAM_SERVO_ID2`
  (macro `PARAM_SERVO_ID(ch)`), otherwise `ERR_PARAM_ID` is returned
  (prevents driving the wrong servo).
* `bitmap` bit `id & 7` of byte `id >> 3` is set for each responding servo id
  (ids 0..253). **SERVO_SCAN is asynchronous**: the first request starts the
  scan and returns `HOST_STATUS_ERR_BUSY`; the host retries the same request
  until it gets `OK` (or `ERR_TIMEOUT` after ~10 s) — section 9.
* `angle_d10` = target angle in 0.1°, the firmware applies
  gain/offset/min/max parameters. Refused with `ERR_NOT_READY` in EtherCAT OP.
* `SERVO_SET_ID` / `SERVO_RAW` require S288 register access, which the bus
  protocol does not provide; they currently answer `ERR_UNSUPPORTED`.
* Servo status blob (`host_servo_status_t`, 20 bytes):

| Offset | Type | Field |
|--------|------|-------|
| 0      | i16  | pos_d10 (actual angle, 0.1°) |
| 2      | i16  | speed_rpm |
| 4      | i16  | current_ma |
| 6      | u16  | voltage_mv |
| 8      | u8   | temp_c |
| 9      | u8   | error |
| 10     | u8   | online |
| 11     | u8   | enabled |
| 12     | u16  | ok_count |
| 14     | u16  | err_count |
| 16     | u32  | last_ok_tick_ms |

### ADC / RGB LED

| CMD  | Name                | Request                   | Response |
|------|---------------------|---------------------------|----------|
| 0x40 | HOST_CMD_ADC_GET    | –                         | 24-byte `host_adc_blob_t` |
| 0x41 | HOST_CMD_LED_SET    | `u8 r`, `u8 g`, `u8 b`    | – |
| 0x42 | HOST_CMD_LED_TEST   | `u8 pattern`, `u16 duration_ms` | – (0 ms stops the test) |

ADC blob (`host_adc_blob_t`, 24 bytes):

| Offset | Type | Field |
|--------|------|-------|
| 0      | u16  | raw_sys_current |
| 2      | u16  | raw_sys_voltage |
| 4      | u16  | raw_esc_current[2] |
| 8      | u16  | sys_voltage_dv (0.1 V) |
| 10     | i16  | sys_current_ca (0.01 A) |
| 12     | u16  | esc_current_ma[2] |
| 16     | u8   | alarm (`ADC_ALARM_*` bitfield) |
| 17     | u8   | reserved0 (0) |
| 18     | u16  | sample_count |
| 20     | u32  | last_update_ms |

### AM32 ESC configuration bridge

| CMD  | Name                     | Request                              | Response |
|------|--------------------------|--------------------------------------|----------|
| 0x50 | HOST_CMD_AM32_BEGIN      | `u8 esc`                             | – |
| 0x51 | HOST_CMD_AM32_XFER       | `u8 esc`, `u8 in_len`, `u8[in_len] in` | `u8 out_len`, `u8[out_len] out` |
| 0x52 | HOST_CMD_AM32_END        | `u8 esc`                             | – |
| 0x53 | HOST_CMD_AM32_STATUS     | `u8 esc`                             | `u8 state`, `u8 last_error` |
| 0x54 | HOST_CMD_AM32_PROG_WRITE | `u8 esc`, `u8 addr` (0..191), `u8 value` | – (queued; `ERR_BUSY` = retry) |
| 0x55 | HOST_CMD_AM32_PROG_SAVE  | `u8 esc`                             | – (queued; wait ~50 ms) |

The board is a **raw byte pipe** to the ESC signal line for the 4-way bridge:
the PC builds the complete BLHeli/AM32 4-way frame (ESC, CMD, LEN, PARAM...,
CRC16-XMODEM) and parses the raw response bytes itself. See section 8.

`AM32_STATUS.state`: 0 = idle, 1 = 4-way bridge active, 2 = error;
`last_error` = last `host_status_t` error code.

**The raw 4-way bridge is read-only in practice**: AM32 flash erase/program
commands are acknowledged only after the ESC flash operation, which can take
**> 1 s** — far beyond the bridge's 150 ms budget. Use `AM32_BEGIN`/`XFER`/
`AM32_END` for identification and EEPROM **read-back**, and
`AM32_PROG_WRITE` / `AM32_PROG_SAVE` (DShot programming mode) for **writes**.

### Bootloader / reset

| CMD  | Name                  | Request                       | Response |
|------|-----------------------|-------------------------------|----------|
| 0x60 | HOST_CMD_BOOTLOADER   | `u32 magic = 0x424F4F54` ("BOOT") | – then reboot into DFU |
| 0x61 | HOST_CMD_RESET        | –                             | – then reboot |

The response is transmitted first; the device reboots ~30 ms later. The DFU
command writes the SDK DFU trigger magic to a retention register and resets;
it only enters DFU mode if the board runs the CherryUSB DFU bootloader
(`flash_dfu` build). Otherwise it is a normal reboot.

---

## 3. Telemetry blob (`host_telemetry_t`, 54 bytes)

Returned by `HOST_CMD_GET_TELEMETRY` directly after the status byte, so the
response frame payload is `1 + 54 = 55` bytes (`LEN = 55`).

| Offset | Type    | Field                | Unit / meaning |
|--------|---------|----------------------|----------------|
| 0      | u16     | status_word          | same bits as EtherCAT TxPDO status word |
| 2      | u16     | fault_code           | `app_fault_t` bitfield (app_state.h) |
| 4      | u32[2]  | esc_erpm             | raw electrical rpm from BDShot (0 = stopped) |
| 12     | u16[2]  | esc_tlm_period_us    | raw eRPM period, 0 = no telemetry |
| 16     | i16[2]  | servo_pos_d10        | servo angle, 0.1° |
| 20     | i16[2]  | servo_speed_rpm      | servo speed |
| 24     | i16[2]  | servo_current_ma     | servo current |
| 28     | u8[2]   | servo_temp_c         | servo temperature |
| 30     | u8[2]   | servo_error          | S288 error/status byte |
| 32     | u16     | sys_voltage_dv       | system voltage, 0.1 V |
| 34     | i16     | sys_current_ca       | system current, 0.01 A |
| 36     | u16[2]  | esc_current_ma       | ESC currents |
| 40     | u16     | loop_counter         | control loop counter (wraps) |
| 42     | u16     | led_status           | `led_get_status_word()` |
| 44     | u16     | ecat_link_status     | bit0/1 port link, bits 4..7 AL state |
| 46     | u32     | host_rx_frames       | valid protocol frames received |
| 50     | u32     | host_crc_errors      | frames dropped for CRC/length errors |

Mechanical motor speed = `esc_erpm / PARAM_DSHOT_POLE_PAIRS` rpm.

The `status_word` / `fault_code` / `servo_*` / ADC fields are a copy of the
last TxPDO built by the 1 kHz control task.

---

## 4. Parameter descriptor (`host_param_desc_t`, 70 bytes)

Serialized by `HOST_CMD_GET_PARAM_DESC` (one descriptor) and
`HOST_CMD_LIST_PARAMS` (N descriptors, N ≤ 7) after the status byte.

| Offset | Type    | Field    | Meaning |
|--------|---------|----------|---------|
| 0      | u16     | id       | `param_id_t` |
| 2      | u8      | type     | 0=u8, 1=u16, 2=u32, 3=i16, 4=i32, 5=str |
| 3      | u8      | persist  | 1 = stored in flash |
| 4      | u8      | readonly | 1 = host must not write |
| 5      | u8      | len      | strlen(name), 0..15 |
| 6      | i32     | min      | minimum value |
| 10     | i32     | max      | maximum value |
| 14     | i32     | def      | factory default |
| 18     | i32     | value    | current value |
| 22     | char[16]| name     | ASCII name, NUL terminated |
| 38     | char[32]| desc     | short UTF-8 description, NUL terminated |

Note that the naming convention encodes the group: `ecat_*`, `dshot_*`,
`servo_*`, `adc_*`, `led_*`, `usb_*`, `am32_*`.

A write is successful when `HOST_CMD_SET_PARAM` answers `HOST_STATUS_OK`; the
device applies the value to the hardware immediately (`param_apply()`).
`HOST_CMD_PARAM_RESET` only resets the RAM values (and applies them);
`HOST_CMD_PARAM_SAVE` writes them to flash.

---

## 5. Parameter storage in flash (limits the host tool should know)

* One dedicated 4 KB sector at XIP offset `PARAM_FLASH_OFFSET = 0x7E000`
  (address `0x8007E000`), size `0x1000`.
* The stored block is exactly one 256-byte NOR flash page:
  `u32 magic` (`0x41524950`), `u16 version` (=1), `u16 count` (=60),
  `u16 crc16`, `u16 reserved`, `u32 pad`, then 60 × `i32` values.
  The CRC uses the same CRC16-CCITT-FALSE as the protocol, computed over the
  whole page with the CRC field set to `0xFFFF`.
* Write policy: erase sector → program 256 bytes → read back and verify. One
  `HOST_CMD_PARAM_SAVE` = one sector erase (~a few ms, interrupts disabled).
  Do not auto-save on every parameter edit; save when the user presses
  "Save to flash".
* Corrupted / blank / older-version blocks are detected (magic, version,
  count, CRC) and ignored; the firmware then runs with defaults from
  `inc/app_config.h` and the host can repair the block with a Save.
* **The application image must stay below `0x7E000` (504 KB)** because of this
  sector, and the EtherCAT EEPROM emulation at `0x80000` must not be touched
  either. The linker will fail if the image grows into the parameter sector.
* `persist = 0` parameters (e.g. `usb_auto_tlm_hz`) are not restored from
  flash; they always start at their default after a reset.
* `usb_auto_tlm_hz` is currently **reserved**: the firmware only answers
  GET_TELEMETRY requests and never sends unsolicited telemetry frames.  The
  host tool should poll at its own rate (10..20 Hz recommended).
* `usb_proto_to_ms` (PARAM_USB_PROTO_TIMEOUT_MS) is the bench override lease
  timeout and the expected host command timeout, default 1000 ms.

---

## 6. Bench override lease (DSHOT_TEST / SERVO_MOVE_TEST)

The 1 kHz control loop normally owns the ESC/servo outputs and sends
MOTOR_STOP while the EtherCAT master is not in OP. To let the PC tool test
motors/servos anyway, the first DSHOT_TEST / DSHOT_CMD / SERVO_MOVE_TEST
command acquires a **bench override lease**:

* The control loop pauses its DShot/servo commands while the lease is active.
* The lease expires after `PARAM_USB_PROTO_TIMEOUT_MS` (default 1000 ms)
  without another test command, and immediately if the EtherCAT master enters
  OP. On expiry the firmware sends MOTOR_STOP to both ESCs and disables both
  servos.
* The host must therefore **refresh the lease** (e.g. repeat the test command
  or keep sending throttle updates at ≥ 2 Hz) while a bench test runs.
* Bench commands are refused with `ERR_NOT_READY` while `g_ecat_op_active`
  is true or the EtherCAT-commanded outputs are enabled.

---

## 7. Typical host sequences

### Connection / identify

1. Open the COM port (DTR asserted by the host; the firmware tracks DTR).
2. `HOST_CMD_PING` → check magic + firmware version.
3. `HOST_CMD_GET_VERSION` → build date string.
4. `HOST_CMD_PARAM_GET_COUNT` + `HOST_CMD_LIST_PARAMS` (7 at a time) to build
   the parameter grid.
5. Poll `HOST_CMD_GET_TELEMETRY` (10..20 Hz) for the dashboard.

### Edit + persist a parameter

1. `HOST_CMD_SET_PARAM(id, value)` → wait for status. Parameter ranges are
   validated by the device (`ERR_PARAM_RANGE`, `ERR_PARAM_RO`, `ERR_PARAM_ID`).
2. Optionally `HOST_CMD_GET_PARAM(id)` to read back.
3. `HOST_CMD_PARAM_SAVE` → `HOST_STATUS_ERR_FLASH` on failure, retry once.
   Expect a few ms of command latency (flash erase/program).

---

## 8. AM32 configuration: read via 4-way, write via DShot programming

AM32 ESCs have two ways to reach their configuration EEPROM through the board:

* **read-back / identify** — raw 4-way bridge (`AM32_BEGIN` / `AM32_XFER` /
  `AM32_END`), raw BLHeli/AM32 bytes, up to 208 bytes per transfer;
* **write** — AM32 DShot programming mode (`AM32_PROG_WRITE` /
  `AM32_PROG_SAVE`), one EEPROM byte per command, no escape/bootloader needed.

Shared preconditions (enforced by the firmware; violations answer
`HOST_STATUS_ERR_NOT_READY` or `ERR_BUSY`):

* EtherCAT master **not in OP**, outputs disabled, motors stopped.
* `PARAM_AM32_ENABLE_PASSTHROUGH = 1`.
* The ESC must be powered, armed (idle DShot frames) and not spinning.
* Do not interleave the two mechanisms: while a programming script is queued
  `AM32_BEGIN` answers `ERR_BUSY`, and while the 4-way bridge is active the
  programming commands answer `ERR_BUSY`.

### 8.1 Read-back via the raw 4-way bridge (works)

The board does **not** interpret the BLHeli/AM32 4-way protocol on this path;
it forwards raw bytes and returns the raw response. The PC tool implements the
4-way container (`ESC + CMD + LEN + PARAM + CRC16-XMODEM`) and the response
parsing.

1. `HOST_CMD_AM32_BEGIN(esc)` → OK (DShot output is paused, 4-way bridge owns
   the signal line).
2. `HOST_CMD_AM32_XFER(esc, request_bytes)`:
   * device sends `request_bytes` (typically 3..4 bytes) on the ESC line and
     then reads the answer until either 208 bytes have been received or the
     line goes idle (inter-byte timeout ~1.5 ms), whichever comes first;
   * response: `u8 out_len` + the raw bytes received (e.g. 197 bytes for an
     EEPROM read: `ESC + CMD + LEN=192 + 192 data + CRC16`).
   * The call blocks for up to ~150 ms (bit-banged at
     `PARAM_AM32_4WAY_BAUD`, default 19200; a 197-byte EEPROM read takes
     ~103 ms). This is inherent to the 4-way interface; do not call it while a
     motor is spinning or while the EtherCAT master needs the bus.
   * Errors: `ERR_TIMEOUT` (ESC did not answer / line error), `ERR_NOT_READY`
     (bridge not acquired or wrong ESC), `ERR_LENGTH`.
3. `HOST_CMD_AM32_END(esc)` → OK (line released, DShot output resumes).

`HOST_CMD_AM32_STATUS(esc)` returns the bridge state at any time. On a
timeout the bridge is marked as error (state 2) and must be released with
AM32_END before a new BEGIN.

**Important limitation:** 4-way erase/program (EEPROM write) commands are
acknowledged only after the ESC flash operation and can take **> 1 s**, which
is outside the bridge's read budget. The raw 4-way bridge is therefore
read-only in practice — use the DShot programming mode below for writes.

### 8.2 Write via AM32 DShot programming mode

The driver queues DShot command frames (`36,36,36,36,36,36, addr, value, 37`)
on the normal ESC signal line; the ESC must be armed with zero throttle. Each
EEPROM byte takes ~9 DShot frames ≈ 9 ms at the default 1 kHz update rate, so
a full 192-byte image takes ≈ 1.8 s plus the save (~6 ms). The driver's script
queue is 16 frames deep (≈ 4 EEPROM bytes).

Host sequence per ESC:

1. Optional: read the current EEPROM with 8.1 and keep it as a backup /
   for a read-modify-write of the 192-byte image. End with `AM32_END`.
2. For `addr = 0 .. 191`: `HOST_CMD_AM32_PROG_WRITE(esc, addr, value)`.
   * Send at most ~4 queued bytes ahead; pace the commands ~10..15 ms apart
     (or send freely and retry on `ERR_BUSY` every ~5..10 ms with the same
     request — recommended implementation).
   * `HOST_STATUS_OK` = byte queued; `ERR_BUSY` = queue/script busy, retry;
     `ERR_NOT_READY` = ECAT OP / outputs on / ESC throttle not zero / 4-way
     bridge active; `ERR_PARAM_RANGE` = bad esc or addr > 191.
3. `HOST_CMD_AM32_PROG_SAVE(esc)` — **send once**, retry only while it answers
   `ERR_BUSY` (the device only accepts the save after the last queued byte has
   been shifted out; `ERR_BUSY` = keep polling, ~20..50 ms interval).
   `HOST_STATUS_OK` means the save sequence was **queued** (it plays out over
   the next ~6 ms) or there was nothing new to save (idempotent — a repeated
   `0x55` after a successful save is a no-op, it never queues a second flash
   write). After OK wait **~50 ms** so the ESC finishes its flash write.
   Do not send `AM32_BEGIN`, change the throttle or let the EtherCAT master
   enter OP during that ~6 ms save window (the firmware refuses `AM32_BEGIN`
   while the script is busy and aborts only on safety events).
4. Optional verify: read the EEPROM back with 8.1 and compare with the
   intended image.

Example frames (`PROG_WRITE` esc=0, addr=0x5A, value=0x12, SEQ=0x10; then
`PROG_SAVE` esc=0, SEQ=0x11):

```
A5 5A 54 10 03 00 00 5A 12 CE FD
A5 5A 54 10 01 00 00 AF A1
A5 5A 55 11 01 00 00 4A 7D
A5 5A 55 11 01 00 00 4A 7D
```

The last two lines are the save request and its OK response (the same bytes,
different direction). Older host tools that do not know 0x54/0x55 can be used
safely: the firmware answers unknown commands with `ERR_UNKNOWN_CMD`.

---

## 9. Servo scan retry sequence

`HOST_CMD_SERVO_SCAN` is asynchronous because a full bus scan does not fit in
one main-loop iteration:

1. Host: `SERVO_SCAN(bus, id_min, id_max)`.
2. Device: starts the scan, answers `HOST_STATUS_ERR_BUSY`.
3. Host: repeat the identical request every ~20..50 ms.
4. Device: while running answers `ERR_BUSY`; when finished answers
   `HOST_STATUS_OK` + the 32-byte id bitmap; after a 10 s guard timeout it
   answers `ERR_TIMEOUT`.
5. Only after OK/ERR_TIMEOUT may the host issue a new scan with different
   parameters.

---

## 10. Error recovery / resynchronisation

* If a response is missing (host timeout), simply retry the request with the
  same or a new SEQ. All commands are idempotent except the reboot commands,
  PARAM_SAVE and the AM32 bridge (whose state machine is exposed through
  AM32_STATUS).
* On CRC errors the device answers with `HOST_STATUS_ERR_CRC`; the host
  should count them, drop the response and continue. Persistent CRC errors
  usually mean a baud/framing problem or that the firmware is stalled - try
  PING, reconnect the port, or power-cycle.
* If the host sees garbage, scan the received stream for `A5 5A` and restart
  parsing at the candidate frame start. A frame with a bad CRC must be
  discarded (the device answers with ERR_CRC and does not execute it).
* A request with `LEN > 512` is answered with `ERR_LENGTH` and dropped.
* A frame that is truncated in the middle (host crash, unplug, noise) is
  discarded by the device after 500 ms; the host does not need to send a
  recovery sequence, just continue with a fresh frame (`A5 5A ...`).
* If the USB port is reopened (DTR toggles), the firmware flushes its RX/TX
  rings; the host should redo the PING/version handshake.
* The firmware never sends unsolicited frames; every response belongs to the
  last request. (Timestamps are not included; use GET_TELEMETRY's
  last_ok_tick_ms / loop_counter for liveness.)

---

## 11. Wire examples

All examples below are complete frames including sync and CRC.

PING request (SEQ = 0x2A):

```
A5 5A 01 2A 00 00 73 B3
```

PING response (status OK, magic "PING", fw 1.0.0):

```
A5 5A 01 2A 0B 00 00 50 49 4E 47 01 00 00 00 00 00 E6 36
```

GET_TELEMETRY request (SEQ = 0x01):

```
A5 5A 03 01 00 00 2C 28
```

GET_TELEMETRY response (SEQ = 0x01, status OK + 54 zeroed telemetry bytes,
`LEN = 55 = 0x37`; 63 bytes total):

```
A5 5A 03 01 37 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 2E DD
```

SET_PARAM request: set `PARAM_DSHOT_RATE` (id 9, see `inc/param.h`) to 600
(SEQ = 0x07):

```
A5 5A 11 07 06 00 09 00 58 02 00 00 F0 FD
```

SET_PARAM response (status OK):

```
A5 5A 11 07 01 00 00 1A D9
```

PARAM_GET_COUNT request (SEQ = 0x02):

```
A5 5A 15 02 00 00 42 4D
```

PARAM_GET_COUNT response (status OK, 60 parameters = 0x003C):

```
A5 5A 15 02 02 00 3C 00 86 BE
```

Caveat on the SET_PARAM example: parameter ids are defined by
`inc/param.h`; in this firmware `PARAM_DSHOT_RATE` is id **9**
(0 = `PARAM_ECAT_NODE_ALIAS`). The host tool must not hardcode ids across
firmware versions - always use `GET_PARAM_DESC`/`LIST_PARAMS` names or the
`param_id_t` enum values.

---

## 12. Contract notes for the PC host tool

* Telemetry blob = 54 bytes, parameter descriptor = 70 bytes (both are
  compile-time asserted in the firmware).
* Response payload always starts with the 1-byte status.
* CRC16-CCITT-FALSE, poly 0x1021, init 0xFFFF, no reflection, no final XOR.
* Frame sync bytes are `A5 5A`; length is little endian after CMD and SEQ.
* `LIST_PARAMS` needs chunked requests (max 7 descriptors per response).
* `SERVO_SCAN` must be retried until it answers OK/ERR_TIMEOUT.
* `AM32_XFER` response is `out_len` + raw bytes (up to 208).
* The raw 4-way bridge is read-only in practice (flash erase/program ACKs can
  exceed the 150 ms budget). EEPROM writes use `AM32_PROG_WRITE` (one byte per
  command, retry on `ERR_BUSY`, ~10..15 ms pacing or ≤4 bytes queued ahead)
  followed by a single `AM32_PROG_SAVE` (retry only on `ERR_BUSY`; OK = save
  queued or already saved — repeated SAVE is idempotent), then wait ~50 ms and
  do a 4-way read-back for verification; a full 192-byte image takes ≈ 1.8 s.
* Reboot/DFU commands answer first and reboot ~30 ms later; the host should
  close the port afterwards.
* New parameters are appended to `param_id_t` so that existing ids stay
  stable; new commands get new ids. The PC tool should tolerate unknown
  commands (`ERR_UNKNOWN_CMD`) and unknown parameter ids.
