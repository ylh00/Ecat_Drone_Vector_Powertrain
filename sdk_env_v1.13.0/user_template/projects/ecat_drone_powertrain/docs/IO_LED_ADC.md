# RGB status LED + ADC monitoring (`led_rgb.c` / `adc_mon.c`)

IO/analog note for the **ecat_drone_5e3y** (HPM5E3Y) project.
Files: `src/led_rgb.c`, `inc/led_rgb.h`, `src/adc_mon.c`, `inc/adc_mon.h`.

---

## 1. RGB status LED

### 1.1 Hardware

One RGB LED device (common anode, **active low** – pin low = LED on) is driven by
three PWM1 outputs:

| LED colour | PWM output | Pad  | PWMv2 comparator | Counter |
|---|---|---|---|---|
| Green | PWM1.P4 | PF12 | CMP8 (a) / CMP9 (b)  | counter 2 |
| Red   | PWM1.P5 | PF13 | CMP10 (a) / CMP11 (b) | counter 2 |
| Blue  | PWM1.P6 | PF14 | CMP12 (a) / CMP13 (b) | counter 3 |

The comparator/counter mapping is **fixed in silicon** (HPM5E00 user manual,
chapter 37 "PWMv2", PWM generation / trigger sections):

* `CMP[4k .. 4k+3]` belong to counter `k`; output `n` uses `CMP[2n]` (point *a*)
  and `CMP[2n+1]` (point *b*).
* Output 4/5 are the two outputs of sub-module 2 (counter 2), output 6 is the
  first output of sub-module 3 (counter 3).

The three channels therefore span **two counters**.  Both counters are loaded
with the same reload value for `BOARD_RGB_LED_FREQ_HZ` (20 kHz) and are reset /
enabled / started **synchronously** (`pwmv2_reset_multi_counter_sync`,
`pwmv2_enable_multi_counter_sync`, `pwmv2_start_pwm_output_sync` with mask
`(1 << pwm_counter_2) | (1 << pwm_counter_3)`), so green/red/blue can not
drift or beat against each other.

The board package (`boards/ecat_drone_5e3y/pinmux.c`, `init_pins()`) already
configures PF12/PF13/PF14 as `PWM1_P_4/5/6`, no pin setup is done in
`led_rgb.c`.

### 1.2 PWM configuration

* PWMV2 PWM1, counters 2 + 3, 20 kHz (`BOARD_RGB_LED_FREQ_HZ`), edge aligned.
* Reload = `board_init_pwm_clock(HPM_PWM1) / 20 kHz - 1`; the PWM1 clock is
  queried from the board package (100 MHz assumption if it cannot be
  determined, warning is printed).
* Two comparators per output (same scheme as
  `hpm_sdk/samples/rgb_led/src/rgb_led.c`, PWMv2 branch):
  * point *a* = shadow register 1, constant 0 → output rises at counter 0;
  * point *b* = shadow register 2/3/4 → high while `CNT < b`; the effective
    duty cycle of the LED is derived from *b*.
* Comparators/reload are fed from PWMv2 shadow registers.  At run time only the
  three *b* shadow values are rewritten; the PWMv2 hardware latches them at the
  next counter reload (`pwm_shadow_register_update_on_reload`), so a colour
  change never produces a glitch.
* Polarity: `BOARD_RGB_LED_ACTIVE_LOW = 1` is handled in software, and the
  runtime parameter `PARAM_ECAT_LED_INVERT` (sampled on every animation step)
  can flip the effective polarity **without rebuilding**, in case the LED
  device on the board is not actually active low / for a board respin.
* Brightness: `duty = gamma(RGB_channel * brightness / 255)`, then mapped to
  the comparator value according to the effective polarity.
  `PARAM_LED_BRIGHTNESS` (0..255, default `APP_CFG_LED_BRIGHTNESS_DEFAULT=128`)
  is applied through `led_set_brightness()` from `param_apply()`.
* Optional gamma: compile-time switch `LED_RGB_USE_GAMMA` (default 0) in
  `inc/led_rgb.h`; 1 applies an interpolated 32-point x^2.2 curve (visually
  linear ramp, dimmer at low brightness).  Enable with
  `-DLED_RGB_USE_GAMMA=1` if desired.

### 1.3 Status codes and animations

`led_task()` is paced internally from the 1 kHz `g_app_tick_ms` counter to
`APP_CFG_LED_UPDATE_HZ` (50 Hz); it only writes 3 shadow registers per step.
`led_set_code()` just stores the code (cheap, called from the 1 kHz control
task by `app_task.c`).  Priority order: host test pattern > `led_set_rgb()`
override > status animation.  An RGB override (`led_set_rgb()` / host
`LED_SET` command) stays active until the board status code changes or
`led_clear_rgb()` is called (it is not cleared by the repeated `led_set_code()`
calls of the control task with the same code); a host `LED_TEST` pattern has the
highest priority for its programmed duration.

| Code | Colour | Pattern | Meaning |
|---|---|---|---|
| `LED_CODE_BOOT` | white | breathing 1.2 s | firmware just started |
| `LED_CODE_SELF_TEST` | blue | blink 100 ms / 50 ms | peripheral self test |
| `LED_CODE_ECAT_WAIT` | yellow | blink 1 s / 500 ms | no EtherCAT master / EEPROM |
| `LED_CODE_ECAT_PREOP` | yellow | double flash 80 ms, period 1 s | slave in PRE-OP |
| `LED_CODE_ECAT_SAFEOP` | cyan | blink 500 ms / 250 ms | slave in SAFE-OP |
| `LED_CODE_OP_IDLE` | green | breathing 2 s | OP, outputs disabled |
| `LED_CODE_RUN` | green | solid | OP, outputs enabled (running) |
| `LED_CODE_RUN_ACTIVE` | green | fast pulse 125 ms / 30 ms | motor activity |
| `LED_CODE_FAULT` | red | blink 250 ms / 125 ms | fault latched |
| `LED_CODE_USB_ACTIVE` | blue | short pulse every 1.2 s | PC tool connected |
| `LED_CODE_DFU` | magenta | breathing 0.8 s | USB DFU / bootloader |

The animation table is `s_animations[]` in `src/led_rgb.c`; colours are
0..255 base colours, patterns are `SOLID / BLINK / DOUBLE_BLINK / BREATH`.

`led_get_status_word()` returns `(code & 0xFF) | (brightness << 8)`; it is
exported in the EtherCAT TxPDO (`g_ecat_txpdo.led_status`) and via the host
protocol.

Host test patterns (`led_test_pattern(pattern, duration_ms)`, used by the
`LED_TEST` host command, `duration_ms = 0` stops the test):

| Pattern | Effect |
|---|---|
| `LED_TEST_OFF` (0) | all LEDs off (valid as a test, duration > 0) |
| `LED_TEST_RED` (1) | solid red |
| `LED_TEST_GREEN` (2) | solid green |
| `LED_TEST_BLUE` (3) | solid blue |
| `LED_TEST_WHITE` (4) | solid white |
| `LED_TEST_RGB_CYCLE` (5) | red / green / blue / yellow / cyan / magenta, 0.5 s each |
| `LED_TEST_WHITE_BLINK` (6) | 1 Hz white blink |
| `LED_TEST_WHITE_BREATH` (7) | 2 s white breathing ramp |

### 1.4 Bring-up checklist (LED)

1. Scope PF12/PF13/PF14: 20 kHz square wave, 0 V … 3.3 V, duty changes with the
   displayed colour; check that the three channels stay phase locked.
2. Verify the LED is really common anode/active low: if the polarity is
   inverted, set `PARAM_ECAT_LED_INVERT = 1` (host tool) – no rebuild needed.
3. `led_task()` must be called from the main loop (it is, in `main.c`).

---

## 2. ADC monitoring

### 2.1 Channel / pad mapping (fixed in silicon)

The HPM5E00 analogue inputs are available on the PF pads only (datasheet
"引脚及功能描述", analog function column).  The board uses these pads for the
four monitored signals; the same pad can be routed to ADC0 and ADC1 but the
board uses one instance per signal:

| Signal | ADC instance | Channel | Pad | Board macro |
|---|---|---|---|---|
| System current | ADC0 | IN1 | PF26 | `BOARD_ADC0_SYS_CURRENT_CH` |
| System voltage | ADC0 | IN8 | PF16 | `BOARD_ADC0_SYS_VOLTAGE_CH` |
| ESC1 current | ADC1 | IN3 | PF20 | `BOARD_ADC1_ESC1_CURRENT_CH` |
| ESC2 current | ADC1 | IN13 | PF19 | `BOARD_ADC1_ESC2_CURRENT_CH` |

`pinmux.c init_pins()` already configures PF16/PF19/PF20/PF26 as analog inputs;
`board_init_adc_clock(BOARD_ADCx_BASE, true)` provides the ADC clock from AHB
(the conversion clock is divided down to `ADC16_SOC_CONV_CLK_FREQ_MAX`,
50 MHz, by `adc16_calc_clock_divider()`).

### 2.2 ADC configuration

* **ADC16 sequence mode, software trigger, 16 bit** (per
  `hpm_sdk/samples/drivers/adc/adc16`,
  `init_common_config()` + `init_sequence_config()` + SW trigger variant).
* One sequence per instance, 2 entries, fixed queue order:
  * ADC0: `{ IN1, IN8 }`
  * ADC1: `{ IN3, IN13 }`
* `seq_int_en` only on the last queue entry (single complete status);
  `CONT_EN = 0`, `RESTART_EN = 0`, `SW_TRIG_EN = 1`, `HW_TRIG_EN = 0`.
* Results are written by the **ADC internal DMA** (`SEQ_DMA_ADDR`); no
  HDMA/XDMA channel and no DMAMUX request is used, so there is no DMA
  resource conflict with the servo or DShot drivers.  The two result buffers
  are 4-byte aligned, in the `.noncacheable` section (CPU data cache is on).
* `adc_mon_task()` is a non-blocking state machine:
  * every `APP_CFG_ADC_SAMPLE_DIV` ms (default 1 ms → 1 kSPS) it triggers both
    sequences and returns;
  * on the following calls it polls `INT_STS[SEQ_CMPT]`; when both instances
    report completion it reads the DMA buffers, converts and processes them;
  * a missing completion after `ADC_MON_TIMEOUT_MS` (2 ms) is counted in
    `adc_mon_data_t.error_count`, the ADC DMA is restarted and a new sequence
    is triggered on the next call.  A software trigger conflict
    (`SEQ_SW_CFLCT`) is handled the same way.
* `adc_mon_get()` returns a snapshot (`adc_mon_data_t`): raw counts, converted
  system voltage (0.1 V), system current (0.01 A), ESC currents (mA), alarm
  bits, sample/error counters and the timestamp of the last update.

### 2.3 Scaling math

All conversions use the runtime parameters (`adc_mon_apply_params()` caches them
and is called from `param_apply()`):

```
voltage_mV   = raw * PARAM_ADC_SYS_V_SCALE       / 1000 + PARAM_ADC_SYS_V_OFFSET_MV
current_mA   = raw * PARAM_ADC_SYS_I_SCALE       / 1000 + PARAM_ADC_SYS_I_OFFSET_MA
esc1_mA      = raw * PARAM_ADC_ESC1_I_SCALE      / 1000 + PARAM_ADC_ESC_I_OFFSET_MA
esc2_mA      = raw * PARAM_ADC_ESC2_I_SCALE      / 1000 + PARAM_ADC_ESC_I_OFFSET_MA

sys_voltage_dv = voltage_mV / 100                    (0.1 V units, clamped >= 0)
sys_current_ca = current_mA / 10                     (0.01 A units)
esc_current_ma = esc_mA                              (clamped >= 0)
```

`*_SCALE` is **uV per LSB (voltage)** / **uA per LSB (current)** and is signed
`int32_t` so a bipolar sensor can be handled with a large positive offset.
If a scale parameter is 0 (not calibrated yet) the compile-time defaults from
`app_config.h` (`APP_CFG_ADC_*_PER_LSB_*`) are used and a warning is logged.

Alarm bits (`adc_mon_data_t.alarm`, also `g_app_state.adc_alarm`):
`ADC_ALARM_OVER_VOLTAGE`, `ADC_ALARM_UNDER_VOLTAGE`, `ADC_ALARM_OC_SYSTEM`,
`ADC_ALARM_OC_ESC1`, `ADC_ALARM_OC_ESC2`.  They are computed with **~5 %
hysteresis**: trip at the threshold, clear when the value is back by 1/20 of
the threshold.  A threshold of 0 disables that alarm
(`PARAM_ADC_VOLT_MIN_DV = 0` disables under-voltage).  `app_task.c` turns the
alarm bits into latched faults only if `PARAM_ADC_ENABLE_PROTECTION != 0`.

### 2.4 Calibration procedure

The scaling is board-specific (shunt / divider / Hall sensor); calibrate once
per hardware revision and store the parameters with the host tool
(`PARAM_SAVE`).

1. **Voltage (ADC0 IN8, PF16)** – two point calibration with a lab supply /
   multimeter:
   * apply a known voltage `V1` (e.g. 12 V), read raw `r1` (host tool telemetry
     or debugger on `adc_mon_data_t.raw.raw_sys_voltage`);
   * apply `V2` (e.g. 48 V), read raw `r2`;
   * `PARAM_ADC_SYS_V_SCALE = (V2_mV - V1_mV) * 1000 / (r2 - r1)`  [uV/LSB]
   * `PARAM_ADC_SYS_V_OFFSET_MV = V1_mV - r1 * SCALE / 1000`.
   * Example: ideal 3.3 V reference gives 50350 uV/LSB at the pad; with a
     1:21 divider `SCALE ≈ 1057` uV/LSB (≈ 100 V full scale) – verify against
     the actual schematic divider before use.
2. **System current (ADC0 IN1, PF26)** – zero + one load point:
   * with no current: `PARAM_ADC_SYS_I_OFFSET_MA = -(r0 * SCALE) / 1000`
     (so that 0 A is reported), where `SCALE` comes from the sensor data sheet
     (e.g. 10 mV/A / 50.35 uV per LSB ≈ 199 uA/LSB for a 10 mV/A Hall sensor);
   * apply a known current `I` and trim `SCALE` so that the reported value
     matches: `SCALE = (I_meas_mA - offset) * 1000 / r`.
3. **ESC currents (ADC1 IN3 PF20 / IN13 PF19)** – same as system current, the
   offset is shared (`PARAM_ADC_ESC_I_OFFSET_MA`), each channel has its own
   scale (`PARAM_ADC_ESC1_I_SCALE`, `PARAM_ADC_ESC2_I_SCALE`).
4. **Alarm thresholds** – set
   `PARAM_ADC_VOLT_MIN_DV` / `PARAM_ADC_VOLT_MAX_DV` (0.1 V),
   `PARAM_ADC_CURR_MAX_CA` (0.01 A), `PARAM_ADC_ESC_CURR_MAX_MA` (mA).
   Then enable `PARAM_ADC_ENABLE_PROTECTION = 1` and verify on the bench that
   the fault latches at the threshold and clears ~5 % below it.
5. Sanity check: with the load disconnected the raw counts should sit near the
   sensor zero point; a stuck 0x0000 / 0xFFFF raw value usually means the pad
   is not configured as analog or the signal is not populated.

### 2.5 Bring-up checklist (ADC)

1. Check the ADC clock: debug log prints the ADC channel configuration on
   startup (`adc_mon_init()`).
2. Read `raw_sys_voltage` with a known supply and compare with the expected
   divider output (scope at PF16).
3. Verify the sample counter increases at ≈ 1000/s (`sample_count` in the
   host telemetry) and `error_count` stays 0.
4. Verify the four `adc_ch` values the ADC reports match the configured queue
   (mismatch increments `error_count`).

---

## 3. SDK references

| Reference | Path / location |
|---|---|
| RGB LED PWMv2 setup (adapted) | `hpm_sdk/samples/rgb_led/src/rgb_led.c:109-216` (`config_pwm`, `update_rgb_led`, PWMv2 branch) |
| PWMv2 edge-aligned waveform / shadow reload method | `hpm_sdk/samples/drivers/pwmv2/pwm_output/src/pwm.c` (`generate_edge_aligned_waveform`, lines 32-139) |
| PWMv2 driver API | `hpm_sdk/drivers/inc/hpm_pwmv2_drv.h` (`pwmv2_deinit`:267, `pwmv2_issue_shadow_register_lock_event`:286, `pwmv2_enable_multi_counter_sync`:319, `pwmv2_reset_multi_counter_sync`:344, `pwmv2_start_pwm_output_sync`:356, `pwmv2_set_shadow_val`:391, `pwmv2_select_cmp_source`:2049, `pwmv2_cmp_update_trig_time`:2036, `pwmv2_counter_select_data_offset_from_shadow_value`:1645, `pwmv2_set_reload_update_time`:1540, `pwmv2_channel_enable_output`:595) |
| PWMv2 register/field definitions | `hpm_sdk/soc/HPM5E00/ip/hpm_pwmv2_regs.h` |
| PWMv2 functional description | HPM5E00 user manual Rev0.4, chapter 37 (esp. 37.4.5 "PWM 生成", 37.4.6 burst, 37.6.14 `CNT_RELOAD_WORK`, `CMP_CFG` update timing) |
| ADC16 sequence mode + internal DMA (adapted) | `hpm_sdk/samples/drivers/adc/adc16/src/adc16.c:130-151` (`app_adc16_setup_clock`), `:220-264` (`process_seq_data`), `:414-443` (`init_common_config`), `:613-734` (`init_sequence_config`, `sequence_handler`) |
| ADC16 driver API | `hpm_sdk/drivers/inc/hpm_adc16_drv.h` (`adc16_init`:361, `adc16_init_channel`:372, `adc16_set_seq_config`:444, `adc16_init_seq_dma`:576, `adc16_trigger_seq_by_sw`:785, `adc16_get_status_flags`:630, `adc16_clear_status_flags`:718, `adc16_calc_clock_divider`:834) |
| ADC16 sequence DMA data layout | HPM5E00 user manual Rev0.4, chapter 60 (sequence mode DMA buffer structure), `ADC16_Type` in `hpm_sdk/soc/HPM5E00/ip/hpm_adc16_regs.h` |
| ADC channel <-> pad mapping | HPM5E00 datasheet Rev0.4, "引脚及功能描述" pin table (pages ~49-52): PF16=ADC0/1_IN08, PF19=ADC0/1_IN13, PF20=ADC0/1_IN03, PF26=ADC0/1_IN01 |
| Board macros | `boards/ecat_drone_5e3y/board.h` (`BOARD_RGB_*`, `BOARD_ADC0_*`, `BOARD_ADC1_*`) |

## 4. Open items / hardware validation

1. **Schematic check of the four ADC pads**: the mapping in section 2.1 is the
   only one possible with the analog-capable pads routed by `pinmux.c`
   (PF16/PF19/PF20/PF26) and matches the customer channel assignment, but the
   actual sensor networks (divider / shunt / Hall sensor gain) must be taken
   from the schematic for the `PARAM_ADC_*_SCALE` values.
2. **LED polarity**: confirmed active low via `BOARD_RGB_LED_ACTIVE_LOW`;
   `PARAM_ECAT_LED_INVERT` allows flipping it on the bench without a rebuild
   if the fitted LED device differs.
3. **20 kHz RGB**: verify with a scope that PWM1 counters 2 and 3 actually run
   at the returned `board_init_pwm_clock()` frequency (the board clock tree
   implementation is responsible for gating/enabling `clock_pwm1`).
4. **ADC16 sequence trigger**: software triggered only; if a hardware trigger
   (e.g. PWM SOC) is preferred later, `adc16_set_seq_config()` +
   `hw_trig_en` and the TRGM route must be added (currently not used to avoid
   sharing TRGM/DMA with the DShot/servo modules).
5. `adc_mon_task()` currently converts at 1 kSPS; raise
   `APP_CFG_ADC_SAMPLE_DIV` (or change the pacing) if faster monitoring is
   required.
