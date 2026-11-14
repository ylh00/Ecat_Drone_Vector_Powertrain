/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Application control task: EtherCAT process data -> DShot / servo / LED,
 * telemetry -> EtherCAT TxPDO.
 */

#include "board.h"
#include "app_task.h"
#include "app_config.h"
#include "app_log.h"
#include "app_state.h"
#include "ecat_pdo.h"
#include "param.h"
#include "dshot.h"
#include "servo_s288.h"
#include "led_rgb.h"
#include "adc_mon.h"
#include "usb_cdc_app.h"
#include "ecat_app.h"
#include "host_proto.h"

volatile uint32_t g_app_tick_ms = 0;

/* Pacing state for the 1 kHz control loop */
static uint32_t s_next_control_tick = 0;
static uint32_t s_last_ecat_cycle = 0;
static uint32_t s_ecat_comm_timeout_ms = 0;
static uint8_t  s_last_state = 0xFF;

/* ------------------------------------------------------------------------- */
/* Helpers                                                                    */
/* ------------------------------------------------------------------------- */
uint16_t app_task_cmd_us_to_dshot(uint16_t us, uint8_t ch)
{
    int32_t min_t = param_get(PARAM_DSHOT_MIN_THROTTLE);
    int32_t max_t = param_get(PARAM_DSHOT_MAX_THROTTLE);
    int32_t throttle;
    int32_t direction = param_get(PARAM_DSHOT_DIRECTION(ch));

    us = (uint16_t)APP_CLAMP(us, APP_CFG_DSHOT_CMD_MIN_US, APP_CFG_DSHOT_CMD_MAX_US);

    /* linear mapping 1000..2000 us -> min_throttle..max_throttle */
    throttle = (int32_t)min_t +
               (((int32_t)us - (int32_t)APP_CFG_DSHOT_CMD_MIN_US) * (max_t - min_t)) /
               ((int32_t)APP_CFG_DSHOT_CMD_MAX_US - (int32_t)APP_CFG_DSHOT_CMD_MIN_US);

    if (direction != 0) {
        /* reverse the throttle range, keep idle at min throttle */
        throttle = (int32_t)min_t + (max_t - throttle);
    }
    if (throttle < 0) {
        throttle = 0;
    }
    if (throttle > 2047) {
        throttle = 2047;
    }
    /* 3D mode: throttle range is split around 48..1047 / 1048..2047, handled by
     * the ESC itself; the raw value is passed through unchanged here. */
    (void)param_get(PARAM_DSHOT_3D_MODE(ch));

    return (uint16_t)throttle;
}

static void app_task_update_state(void)
{
    app_state_t new_state;

    if (g_app_state.faults != 0U) {
        new_state = APP_STATE_FAULT;
    } else if (!g_ecat_op_active) {
        uint8_t al = ecat_app_get_al_state();
        switch (al) {
        case ECAT_AL_STATE_PREOP:
            new_state = APP_STATE_ECAT_PREOP;
            break;
        case ECAT_AL_STATE_SAFEOP:
            new_state = APP_STATE_ECAT_SAFEOP;
            break;
        case ECAT_AL_STATE_OP:
            new_state = APP_STATE_ECAT_OP_IDLE;
            break;
        default:
            new_state = APP_STATE_ECAT_WAIT;
            break;
        }
    } else if (g_app_state.output_enabled) {
        new_state = APP_STATE_RUN;
    } else {
        new_state = APP_STATE_ECAT_OP_IDLE;
    }

    if ((uint8_t)new_state != s_last_state) {
        s_last_state = (uint8_t)new_state;
        LOGM_I(LOG_MOD_MAIN, "state -> %u (ecat OP=%u, out=%u, fault=0x%04X)",
               (unsigned)new_state, (unsigned)g_ecat_op_active,
               (unsigned)g_app_state.output_enabled, (unsigned)g_app_state.faults);
    }
    g_app_state.state = new_state;

    /* RGB LED follows the board state */
    switch (new_state) {
    case APP_STATE_ECAT_WAIT:
        led_set_code(LED_CODE_ECAT_WAIT);
        break;
    case APP_STATE_ECAT_PREOP:
        led_set_code(LED_CODE_ECAT_PREOP);
        break;
    case APP_STATE_ECAT_SAFEOP:
        led_set_code(LED_CODE_ECAT_SAFEOP);
        break;
    case APP_STATE_ECAT_OP_IDLE:
        led_set_code(LED_CODE_OP_IDLE);
        break;
    case APP_STATE_RUN:
        led_set_code(LED_CODE_RUN);
        break;
    case APP_STATE_FAULT:
        led_set_code(LED_CODE_FAULT);
        break;
    default:
        led_set_code(LED_CODE_BOOT);
        break;
    }
}

/* ------------------------------------------------------------------------- */
/* Public API                                                                 */
/* ------------------------------------------------------------------------- */
void app_task_tick_isr(void)
{
    g_app_tick_ms++;
}

void app_task_build_telemetry(void)
{
    dshot_telemetry_t tlm[APP_CFG_DSHOT_MOTORS];
    servo_status_t servo[APP_CFG_SERVO_COUNT];
    adc_mon_data_t adc;
    uint8_t al_state = ecat_app_get_al_state();
    uint8_t port0 = 0, port1 = 0;

    for (uint8_t ch = 0; ch < APP_CFG_DSHOT_MOTORS; ch++) {
        dshot_get_telemetry(ch, &tlm[ch]);
        g_ecat_txpdo.esc_erpm[ch] = tlm[ch].erpm;
        g_ecat_txpdo.esc_tlm_period_us[ch] = tlm[ch].period_us;
    }
    for (uint8_t ch = 0; ch < APP_CFG_SERVO_COUNT; ch++) {
        servo_get_status(ch, &servo[ch]);
        g_ecat_txpdo.servo_pos_d10[ch] = servo[ch].pos_d10;
        g_ecat_txpdo.servo_speed_rpm[ch] = servo[ch].speed_rpm;
        g_ecat_txpdo.servo_current_ma[ch] = servo[ch].current_ma;
        g_ecat_txpdo.servo_temp_c[ch] = servo[ch].temp_c;
        g_ecat_txpdo.servo_error[ch] = servo[ch].error;
        g_app_state.servo_online[ch] = servo[ch].online;
    }
    adc_mon_get(&adc);
    g_ecat_txpdo.sys_voltage_dv = adc.sys_voltage_dv;
    g_ecat_txpdo.sys_current_ca = adc.sys_current_ca;
    g_ecat_txpdo.esc_current_ma[0] = adc.esc_current_ma[0];
    g_ecat_txpdo.esc_current_ma[1] = adc.esc_current_ma[1];

    ecat_app_get_link_status(&port0, &port1);
    g_app_state.ecat_link_port0 = port0;
    g_app_state.ecat_link_port1 = port1;

    g_ecat_txpdo.fault_code = g_app_state.faults;
    g_ecat_txpdo.loop_counter = (uint16_t)g_app_state.loop_counter;
    g_ecat_txpdo.led_status = led_get_status_word();
    g_ecat_txpdo.ecat_link_status = (uint16_t)((port0 ? 0x01U : 0U) |
                                               (port1 ? 0x02U : 0U) |
                                               ((uint16_t)(al_state & 0x0FU) << 4));

    /* status word */
    uint16_t status = 0;
    if (al_state == ECAT_AL_STATE_OP) {
        status |= ECAT_STS_OP;
    }
    if (g_app_state.output_enabled) {
        status |= ECAT_STS_OUTPUT_ENABLED;
    }
    if (g_app_state.faults != 0U) {
        status |= ECAT_STS_FAULT_ACTIVE;
    }
    if (g_app_state.esc_tlm_ok[0]) {
        status |= ECAT_STS_ESC1_TLM_OK;
    }
    if (g_app_state.esc_tlm_ok[1]) {
        status |= ECAT_STS_ESC2_TLM_OK;
    }
    if (g_app_state.servo_online[0]) {
        status |= ECAT_STS_SERVO1_ONLINE;
    }
    if (g_app_state.servo_online[1]) {
        status |= ECAT_STS_SERVO2_ONLINE;
    }
    if (g_app_state.adc_alarm != 0U) {
        status |= ECAT_STS_ADC_ALARM;
    }
    g_ecat_txpdo.status_word = status;
}

static void app_task_protection(void)
{
    adc_mon_data_t adc;
    uint16_t new_faults = 0;

    adc_mon_get(&adc);
    if (param_get(PARAM_ADC_ENABLE_PROTECTION) != 0) {
        if (adc.alarm & ADC_ALARM_OVER_VOLTAGE) {
            new_faults |= APP_FAULT_OVER_VOLTAGE;
        }
        if (adc.alarm & ADC_ALARM_UNDER_VOLTAGE) {
            new_faults |= APP_FAULT_UNDER_VOLTAGE;
        }
        if (adc.alarm & ADC_ALARM_OC_SYSTEM) {
            new_faults |= APP_FAULT_OVER_CURRENT_SYS;
        }
        if (adc.alarm & ADC_ALARM_OC_ESC1) {
            new_faults |= APP_FAULT_OVER_CURRENT_ESC1;
        }
        if (adc.alarm & ADC_ALARM_OC_ESC2) {
            new_faults |= APP_FAULT_OVER_CURRENT_ESC2;
        }
    }
    g_app_state.adc_alarm = adc.alarm;

    /* ESC telemetry loss is only a warning while the motors are running */
    for (uint8_t ch = 0; ch < APP_CFG_DSHOT_MOTORS; ch++) {
        dshot_telemetry_t tlm;
        dshot_get_telemetry(ch, &tlm);
        g_app_state.esc_tlm_ok[ch] = tlm.valid;
        if (g_app_state.output_enabled && (tlm.valid == 0U)) {
            new_faults |= (ch == 0U) ? APP_FAULT_ESC1_NO_TLM : APP_FAULT_ESC2_NO_TLM;
        }
    }

    if (new_faults != 0U) {
        app_state_set_fault(new_faults);
    }
}

/* One 1 kHz control iteration */
static void app_task_control_1ms(void)
{
    ecat_rxpdo_t rx;
    uint16_t ctrl;
    uint8_t outputs_enabled = 0;

    /* Take a consistent snapshot of the process data */
    for (uint32_t i = 0; i < sizeof(ecat_rxpdo_t); i++) {
        ((uint8_t *)&rx)[i] = ((const uint8_t *)&g_ecat_rxpdo)[i];
    }
    ctrl = rx.control_word;

    /* ------------------------------------------------------------------
     * Host tool bench / test mode
     *   While the PC tool owns the outputs (DShot test, servo test, AM32
     *   configuration) the control loop must not override the commands with
     *   the (stale) EtherCAT process data.  Telemetry is still refreshed so
     *   the host tool can display live values.  host_proto_app_bench_active()
     *   only returns 1 while the tester lease is valid and EtherCAT is not in
     *   OP, so a live master is never disturbed.
     * ------------------------------------------------------------------ */
    if (host_proto_app_bench_active() != 0U) {
        g_app_state.output_enabled = 0U;
        app_task_protection();
        app_task_update_state();
        app_task_build_telemetry();
        g_app_state.loop_counter++;
        return;
    }

    /* EtherCAT communication watchdog: if the master stops updating the
     * outputs while the slave is in OP, disable the motors.
     * Outside OP (INIT/PREOP/SAFEOP) the outputs are disabled anyway and the
     * master may legitimately stop sending process data, so the watchdog must
     * not latch a fault there. */
    if (g_ecat_op_active == 0U) {
        s_last_ecat_cycle = rx.cycle_counter;
        s_ecat_comm_timeout_ms = param_get(PARAM_ECAT_WATCHDOG_MS);
        app_state_clear_fault(APP_FAULT_ECAT_OP_TIMEOUT); /* auto recover on OP re-entry */
    } else if (rx.cycle_counter != s_last_ecat_cycle) {
        s_last_ecat_cycle = rx.cycle_counter;
        s_ecat_comm_timeout_ms = param_get(PARAM_ECAT_WATCHDOG_MS);
    } else if (s_ecat_comm_timeout_ms > 0U) {
        s_ecat_comm_timeout_ms--;
        if (s_ecat_comm_timeout_ms == 0U) {
            app_state_set_fault(APP_FAULT_ECAT_OP_TIMEOUT);
        }
    }

    if ((ctrl & ECAT_CTRL_CLEAR_FAULT) != 0U) {
        app_state_clear_all_faults();
        servo_clear_fault(0);
        servo_clear_fault(1);
    }

    if ((ctrl & ECAT_CTRL_EMERGENCY_STOP) != 0U) {
        outputs_enabled = 0;
    } else if ((ctrl & ECAT_CTRL_RUN) != 0U) {
        outputs_enabled = 1;
    } else {
        outputs_enabled = 0;
    }

    if (g_ecat_op_active == 0U) {
        outputs_enabled = 0;
    }
    if (g_app_state.faults != 0U) {
        outputs_enabled = 0;
    }
    g_app_state.output_enabled = outputs_enabled;
    dshot_enable(outputs_enabled);

    /* ---- ESC throttle commands ---------------------------------------- */
    if (outputs_enabled != 0U) {
        for (uint8_t ch = 0; ch < APP_CFG_DSHOT_MOTORS; ch++) {
            dshot_set_throttle(ch, app_task_cmd_us_to_dshot(rx.esc_cmd_us[ch], ch));
            if (rx.dshot_cmd[ch] != 0U) {
                dshot_send_command(ch, rx.dshot_cmd[ch]);
            }
        }
    } else {
        for (uint8_t ch = 0; ch < APP_CFG_DSHOT_MOTORS; ch++) {
            dshot_set_throttle(ch, DSHOT_CMD_MOTOR_STOP);
        }
    }

    /* ---- bus servo commands ------------------------------------------- */
    {
        uint8_t servo_en = (((ctrl & ECAT_CTRL_SERVO_ENABLE) != 0U) &&
                            ((rx.servo_ctrl_word & 0x01U) != 0U)) ? 1U : 0U;
        uint8_t servo2_en = (((ctrl & ECAT_CTRL_SERVO_ENABLE) != 0U) &&
                             ((rx.servo_ctrl_word & 0x02U) != 0U)) ? 1U : 0U;

        servo_set_enable(0, (servo_en != 0U) && (outputs_enabled != 0U));
        servo_set_enable(1, (servo2_en != 0U) && (outputs_enabled != 0U));

        if (outputs_enabled != 0U) {
            servo_set_target_d10(0, rx.servo_angle_d10[0]);
            servo_set_target_d10(1, rx.servo_angle_d10[1]);
        }
    }

    /* ---- housekeeping -------------------------------------------------- */
    app_task_protection();
    app_task_update_state();
    app_task_build_telemetry();

    g_app_state.loop_counter++;
}

void app_task_run(void)
{
    if ((int32_t)(g_app_tick_ms - s_next_control_tick) >= 0) {
        s_next_control_tick += (1000U / APP_CFG_CONTROL_LOOP_HZ);
        app_task_control_1ms();
    }
}

void app_task_init(void)
{
    log_init();
    LOGM_I(LOG_MOD_MAIN, "parameter system init");
    param_init();

    LOGM_I(LOG_MOD_MAIN, "ADC monitor init");
    adc_mon_init();

    LOGM_I(LOG_MOD_MAIN, "RGB LED init");
    led_init();

    LOGM_I(LOG_MOD_MAIN, "bus servo init");
    servo_init();

    LOGM_I(LOG_MOD_MAIN, "bidirectional DShot init");
    dshot_init();

    param_apply();

    LOGM_I(LOG_MOD_MAIN, "USB CDC init");
    if (usb_cdc_app_init() != 0) {
        LOGM_W(LOG_MOD_MAIN, "USB CDC start failed (host tool not available)");
    }

    g_app_state.state = APP_STATE_SELF_TEST;
    led_set_code(LED_CODE_SELF_TEST);
    s_next_control_tick = g_app_tick_ms;
    board_timer_create(APP_CFG_BOARD_TICK_MS, app_task_tick_isr);
}
