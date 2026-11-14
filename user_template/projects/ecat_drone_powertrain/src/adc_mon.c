/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * ADC monitoring for the ecat_drone_5e3y board.
 *
 *   ADC0 IN1  -> PF26 : system current
 *   ADC0 IN8  -> PF16 : system voltage
 *   ADC1 IN3  -> PF20 : ESC1 current sense
 *   ADC1 IN13 -> PF19 : ESC2 current sense
 *
 * The channel <-> pad mapping is fixed in silicon (HPM5E00 datasheet analog
 * function column); the four pads are configured as analog inputs by the board
 * package (pinmux.c init_pins()), nothing has to be done here.
 *
 * Implementation notes
 * --------------------
 * - ADC16 sequence mode, software trigger, 16 bit resolution.  Every ADC
 *   instance converts its two channels in one sequence:
 *       ADC0 queue = { IN1, IN8 },  ADC1 queue = { IN3, IN13 }
 *   This follows hpm_sdk/samples/drivers/adc/adc16 (init_common_config() and
 *   init_sequence_config(), SW trigger variant).
 * - The ADC16 sequence engine stores the results itself with the ADC internal
 *   DMA (SEQ_DMA_ADDR).  No HDMA/XDMA channel or DMAMUX request is used, so
 *   this module does not share DMA resources with the servo / DShot drivers.
 *   Because the CPU data cache is enabled the result buffers are placed in the
 *   non-cacheable section (same attribute as the SDK ADC16 sample).
 * - adc_mon_task() is a non-blocking state machine: it triggers a new sequence
 *   once per millisecond and polls INT_STS[SEQ_CMPT] on the following calls
 *   (results are ready after a few microseconds).  A missing completion is
 *   counted as an error and the sequence is restarted.
 * - Scaling and the alarm thresholds come from the parameter system
 *   (PARAM_ADC_*), adc_mon_apply_params() is called from param_apply().
 */

#include "board.h"
#include "hpm_adc16_drv.h"
#include "hpm_clock_drv.h"

#include "adc_mon.h"
#include "app_task.h"
#include "app_log.h"
#include "param.h"

/* ------------------------------------------------------------------------- */
/* Configuration                                                              */
/* ------------------------------------------------------------------------- */

/* Two sequence entries per ADC instance (see the channel mapping above) */
#define ADC_MON_SEQ_LEN             (2U)

/* ADC sample time (in ADC clock cycles), same value as the SDK adc16 sample */
#define ADC_MON_SAMPLE_CYCLE        (20U)

/* Requested conversion rate: every APP_CFG_ADC_SAMPLE_DIV ms (the control loop
 * runs at 1 kHz, APP_CFG_ADC_SAMPLE_DIV = 1 -> 1 kSPS). */
#define ADC_MON_SAMPLE_PERIOD_MS    (APP_CFG_ADC_SAMPLE_DIV)

/* A sequence takes a few microseconds; if it did not complete within this time
 * something is wrong and the sequence is restarted. */
#define ADC_MON_TIMEOUT_MS          (2U)

/* Alarm hysteresis: trip at the threshold, clear when the value is back by
 * ~5 % of the threshold (1/20). */
#define ADC_MON_HYST_DIV            (20)

/* Fallback scaling values from app_config.h if the parameter is not set */
#define ADC_MON_V_SCALE_FALLBACK_UV \
    ((int32_t)(((int64_t)APP_CFG_ADC_SYS_V_MV_PER_LSB_NUM * 1000) / (APP_CFG_ADC_SYS_V_MV_PER_LSB_DEN)))
#define ADC_MON_I_SCALE_FALLBACK_UA \
    ((int32_t)(((int64_t)APP_CFG_ADC_SYS_I_MA_PER_LSB_NUM * 1000) / (APP_CFG_ADC_SYS_I_MA_PER_LSB_DEN)))
#define ADC_MON_ESC_I_SCALE_FALLBACK_UA \
    ((int32_t)(((int64_t)APP_CFG_ADC_ESC_I_MA_PER_LSB_NUM * 1000) / (APP_CFG_ADC_ESC_I_MA_PER_LSB_DEN)))

/* ------------------------------------------------------------------------- */
/* Module state                                                               */
/* ------------------------------------------------------------------------- */

typedef enum {
    ADC_MON_STATE_IDLE = 0,     /* waiting for the next sample period          */
    ADC_MON_STATE_WAIT,         /* sequence triggered, waiting for completion  */
} adc_mon_state_t;

typedef struct {
    ADC16_Type *base;
    const uint8_t *channels;    /* sequence queue, ADC_MON_SEQ_LEN entries      */
    uint32_t *dma_buff;         /* non-cacheable result buffer                  */
    adc16_dma_config_t dma_cfg;
} adc_mon_instance_t;

/* Calibrated conversion constants (cached from the parameter table) */
typedef struct {
    int32_t v_scale_uv;                              /* uV per LSB          */
    int32_t v_offset_mv;                             /* mV                  */
    int32_t i_scale_ua;                              /* uA per LSB          */
    int32_t i_offset_ma;                             /* mA                  */
    int32_t esc_i_scale_ua[APP_CFG_DSHOT_MOTORS];    /* uA per LSB          */
    int32_t esc_i_offset_ma;                         /* mA                  */
    int32_t volt_min_dv;                             /* 0.1 V, 0 = off      */
    int32_t volt_max_dv;                             /* 0.1 V, 0 = off      */
    int32_t curr_max_ca;                             /* 0.01 A, 0 = off     */
    int32_t esc_curr_max_ma;                         /* mA, 0 = off         */
} adc_mon_params_t;

/* ADC result buffers: written by the ADC internal DMA, read by the CPU, so they
 * must not be cached.  ADC_SOC_DMA_ADDR_ALIGNMENT is 4 bytes. */
ATTR_PLACE_AT_NONCACHEABLE_WITH_ALIGNMENT(ADC_SOC_DMA_ADDR_ALIGNMENT)
static uint32_t s_adc0_dma_buff[ADC_MON_SEQ_LEN];
ATTR_PLACE_AT_NONCACHEABLE_WITH_ALIGNMENT(ADC_SOC_DMA_ADDR_ALIGNMENT)
static uint32_t s_adc1_dma_buff[ADC_MON_SEQ_LEN];

static const uint8_t s_adc0_channels[ADC_MON_SEQ_LEN] = {
    (uint8_t)BOARD_ADC0_SYS_CURRENT_CH,
    (uint8_t)BOARD_ADC0_SYS_VOLTAGE_CH,
};

static const uint8_t s_adc1_channels[ADC_MON_SEQ_LEN] = {
    (uint8_t)BOARD_ADC1_ESC1_CURRENT_CH,
    (uint8_t)BOARD_ADC1_ESC2_CURRENT_CH,
};

static adc_mon_instance_t s_adc_instances[] = {
    {
        .base = BOARD_ADC0_BASE,
        .channels = s_adc0_channels,
        .dma_buff = s_adc0_dma_buff,
    },
    {
        .base = BOARD_ADC1_BASE,
        .channels = s_adc1_channels,
        .dma_buff = s_adc1_dma_buff,
    },
};

/* Clock names are needed to check whether the ADC conversion clock comes from
 * the AHB bus (SEL_SYNC_AHB bit in ADC_CFG0). */
static const clock_name_t s_adc_clock_names[] = {
    BOARD_ADC0_CLK_NAME,
    BOARD_ADC1_CLK_NAME,
};

#define ADC_MON_INSTANCE_COUNT  (APP_ARRAY_SIZE(s_adc_instances))

static adc_mon_data_t    s_data;
static adc_mon_params_t  s_params;
static adc_mon_state_t   s_state = ADC_MON_STATE_IDLE;
static uint32_t          s_sample_period_ms = ADC_MON_SAMPLE_PERIOD_MS;
static uint32_t          s_last_sample_ms;
static uint32_t          s_trigger_ms;
static uint16_t          s_error_count;
static bool              s_initialized;

/* ------------------------------------------------------------------------- */
/* Calibration helpers                                                        */
/* ------------------------------------------------------------------------- */

/* raw * (uV or uA / LSB) -> mV or mA plus the offset */
static int32_t adc_mon_scale_milli(uint16_t raw, int32_t scale_u_per_lsb, int32_t offset_milli)
{
    int64_t value = ((int64_t)raw * (int64_t)scale_u_per_lsb) / 1000;

    value += offset_milli;
    return (int32_t)value;
}

/* Over-threshold alarm with hysteresis (trip above the limit, clear ~5 % below) */
static bool adc_mon_alarm_over(int32_t value, int32_t limit, bool previous)
{
    if (limit <= 0) {
        return false;                       /* threshold 0 = alarm disabled  */
    }
    if (value > limit) {
        return true;
    }
    if (value < (limit - (limit / ADC_MON_HYST_DIV))) {
        return false;
    }
    return previous;
}

/* Under-threshold alarm with hysteresis (trip below the limit, clear ~5 % above) */
static bool adc_mon_alarm_under(int32_t value, int32_t limit, bool previous)
{
    if (limit <= 0) {
        return false;                       /* threshold 0 = alarm disabled  */
    }
    if (value < limit) {
        return true;
    }
    if (value > (limit + (limit / ADC_MON_HYST_DIV))) {
        return false;
    }
    return previous;
}

/* ------------------------------------------------------------------------- */
/* ADC sequence handling                                                      */
/* ------------------------------------------------------------------------- */

static void adc_mon_clear_sequence_flags(void)
{
    for (uint32_t i = 0U; i < ADC_MON_INSTANCE_COUNT; i++) {
        adc16_clear_status_flags(s_adc_instances[i].base,
                                 ADC16_INT_STS_SEQ_CMPT_MASK |
                                 ADC16_INT_STS_SEQ_CVC_MASK |
                                 ADC16_INT_STS_SEQ_SW_CFLCT_MASK);
    }
}

static bool adc_mon_sequence_complete(const adc_mon_instance_t *inst)
{
    return ADC16_INT_STS_SEQ_CMPT_GET(adc16_get_status_flags(inst->base)) != 0U;
}

/* Trigger both ADC sequences; returns false on a software trigger conflict */
static bool adc_mon_start_sequences(void)
{
    bool ok = true;

    for (uint32_t i = 0U; i < ADC_MON_INSTANCE_COUNT; i++) {
        adc_mon_instance_t *inst = &s_adc_instances[i];

        adc16_clear_status_flags(inst->base,
                                 ADC16_INT_STS_SEQ_CMPT_MASK |
                                 ADC16_INT_STS_SEQ_CVC_MASK |
                                 ADC16_INT_STS_SEQ_SW_CFLCT_MASK);
        if (adc16_trigger_seq_by_sw(inst->base) != status_success) {
            ok = false;
        }
    }
    return ok;
}

/* Abort a stuck sequence and put the ADC DMA back to the buffer start */
static void adc_mon_abort_sequences(void)
{
    for (uint32_t i = 0U; i < ADC_MON_INSTANCE_COUNT; i++) {
        adc_mon_instance_t *inst = &s_adc_instances[i];

        adc16_clear_status_flags(inst->base, adc16_get_status_flags(inst->base));
        (void)adc16_init_seq_dma(inst->base, &inst->dma_cfg);
    }
}

/* Convert one raw sample set and update the public data structure */
static void adc_mon_update(const adc_mon_raw_t *raw)
{
    int32_t voltage_mv;
    int32_t voltage_dv;
    int32_t current_ma;
    int32_t esc_ma[APP_CFG_DSHOT_MOTORS];
    uint8_t alarm = 0U;

    voltage_mv = adc_mon_scale_milli(raw->raw_sys_voltage, s_params.v_scale_uv, s_params.v_offset_mv);
    voltage_dv = voltage_mv / 100;
    current_ma = adc_mon_scale_milli(raw->raw_sys_current, s_params.i_scale_ua, s_params.i_offset_ma);
    for (uint32_t ch = 0U; ch < APP_CFG_DSHOT_MOTORS; ch++) {
        esc_ma[ch] = adc_mon_scale_milli(raw->raw_esc_current[ch],
                                         s_params.esc_i_scale_ua[ch],
                                         s_params.esc_i_offset_ma);
    }

    s_data.raw = *raw;
    s_data.sys_voltage_dv = (uint16_t)APP_CLAMP(voltage_dv, 0, 65535);
    s_data.sys_current_ca = (int16_t)APP_CLAMP(current_ma / 10, -32768, 32767);
    for (uint32_t ch = 0U; ch < APP_CFG_DSHOT_MOTORS; ch++) {
        s_data.esc_current_ma[ch] = (uint16_t)APP_CLAMP(esc_ma[ch], 0, 65535);
    }

    /* Alarm bits, computed with hysteresis so that they do not chatter around
     * the threshold.  PARAM_ADC_ENABLE_PROTECTION selects whether app_task.c
     * turns them into latched faults. */
    if (adc_mon_alarm_over(voltage_dv, s_params.volt_max_dv,
                           (s_data.alarm & ADC_ALARM_OVER_VOLTAGE) != 0U)) {
        alarm |= ADC_ALARM_OVER_VOLTAGE;
    }
    if (adc_mon_alarm_under(voltage_dv, s_params.volt_min_dv,
                            (s_data.alarm & ADC_ALARM_UNDER_VOLTAGE) != 0U)) {
        alarm |= ADC_ALARM_UNDER_VOLTAGE;
    }
    if (adc_mon_alarm_over(current_ma / 10, s_params.curr_max_ca,
                           (s_data.alarm & ADC_ALARM_OC_SYSTEM) != 0U)) {
        alarm |= ADC_ALARM_OC_SYSTEM;
    }
    if (adc_mon_alarm_over(esc_ma[0], s_params.esc_curr_max_ma,
                           (s_data.alarm & ADC_ALARM_OC_ESC1) != 0U)) {
        alarm |= ADC_ALARM_OC_ESC1;
    }
    if (adc_mon_alarm_over(esc_ma[1], s_params.esc_curr_max_ma,
                           (s_data.alarm & ADC_ALARM_OC_ESC2) != 0U)) {
        alarm |= ADC_ALARM_OC_ESC2;
    }
    s_data.alarm = alarm;

    s_data.sample_count++;
    s_data.error_count = s_error_count;
    s_data.last_update_ms = g_app_tick_ms;
}

/* Read the DMA buffers once both sequences are complete */
static void adc_mon_process(void)
{
    const adc16_seq_dma_data_t *adc0 = (const adc16_seq_dma_data_t *)s_adc_instances[0].dma_buff;
    const adc16_seq_dma_data_t *adc1 = (const adc16_seq_dma_data_t *)s_adc_instances[1].dma_buff;
    adc_mon_raw_t raw;
    bool channel_ok = true;

    /* The sequence order is fixed by the queue configuration, but check the
     * channel number reported by the ADC to catch a wrong configuration. */
    channel_ok = (adc0[0].adc_ch == BOARD_ADC0_SYS_CURRENT_CH) &&
                 (adc0[1].adc_ch == BOARD_ADC0_SYS_VOLTAGE_CH) &&
                 (adc1[0].adc_ch == BOARD_ADC1_ESC1_CURRENT_CH) &&
                 (adc1[1].adc_ch == BOARD_ADC1_ESC2_CURRENT_CH);
    if (!channel_ok) {
        s_error_count++;
    }

    raw.raw_sys_current = adc0[0].result;           /* ADC0 IN1               */
    raw.raw_sys_voltage = adc0[1].result;           /* ADC0 IN8               */
    raw.raw_esc_current[0] = adc1[0].result;        /* ADC1 IN3               */
    raw.raw_esc_current[1] = adc1[1].result;        /* ADC1 IN13              */
    adc_mon_update(&raw);
}

/* ------------------------------------------------------------------------- */
/* Initialization                                                             */
/* ------------------------------------------------------------------------- */

static hpm_stat_t adc_mon_init_instance(adc_mon_instance_t *inst, clock_name_t clk_name)
{
    hpm_stat_t stat;
    adc16_config_t cfg;
    adc16_channel_config_t ch_cfg;
    adc16_seq_config_t seq_cfg;
    uint32_t input_hz;
    uint32_t clk_div;

    input_hz = board_init_adc_clock(inst->base, true);
    if (input_hz == 0U) {
        LOGM_E(LOG_MOD_ADC, "ADC clock initialization failed");
        return status_fail;
    }
    if (adc16_calc_clock_divider(input_hz, ADC16_SOC_CONV_CLK_FREQ_MAX, &clk_div) != status_success) {
        LOGM_E(LOG_MOD_ADC, "ADC conversion clock divider out of range (input %lu Hz)",
               (unsigned long)input_hz);
        return status_invalid_argument;
    }

    /* Common configuration: 16 bit, sequence mode, clock from the board clock
     * tree.  SEL_SYNC_AHB must match the ADC clock source selected by
     * board_init_adc_clock(ptr, true). */
    adc16_get_default_config(&cfg);
    cfg.res = adc16_res_16_bits;
    cfg.conv_mode = adc16_conv_mode_sequence;
    cfg.adc_clk_div = clk_div;
    cfg.sel_sync_ahb = (clock_get_source(clk_name) == clk_adc_src_ahb0);
    stat = adc16_init(inst->base, &cfg);
    if (stat != status_success) {
        LOGM_E(LOG_MOD_ADC, "ADC initialization failed (%d)", (int)stat);
        return stat;
    }

    adc16_get_channel_default_config(&ch_cfg);
    ch_cfg.sample_cycle = ADC_MON_SAMPLE_CYCLE;
    for (uint32_t i = 0U; i < ADC_MON_SEQ_LEN; i++) {
        ch_cfg.ch = inst->channels[i];
        stat = adc16_init_channel(inst->base, &ch_cfg);
        if (stat != status_success) {
            LOGM_E(LOG_MOD_ADC, "ADC channel %u initialization failed (%d)",
                   (unsigned)inst->channels[i], (int)stat);
            return stat;
        }
    }

    /* One software trigger shall convert the complete queue, then the ADC
     * waits for the next trigger (CONT_EN = 1, RESTART_EN = 0; same setting as
     * the SDK adc16 sample).  CONT_EN = 0 would convert only one channel per
     * trigger. */
    seq_cfg.seq_len = ADC_MON_SEQ_LEN;
    seq_cfg.restart_en = false;
    seq_cfg.cont_en = true;
    seq_cfg.sw_trig_en = true;
    seq_cfg.hw_trig_en = false;
    for (uint32_t i = 0U; i < ADC_MON_SEQ_LEN; i++) {
        seq_cfg.queue[i].ch = inst->channels[i];
        seq_cfg.queue[i].seq_int_en = false;
    }
    /* single conversion complete status on the last entry of the queue */
    seq_cfg.queue[ADC_MON_SEQ_LEN - 1U].seq_int_en = true;
    stat = adc16_set_seq_config(inst->base, &seq_cfg);
    if (stat != status_success) {
        LOGM_E(LOG_MOD_ADC, "ADC sequence configuration failed (%d)", (int)stat);
        return stat;
    }

    /* ADC internal DMA: results are written into the non-cacheable buffer */
    inst->dma_cfg.start_addr = (uint32_t *)core_local_mem_to_sys_address(BOARD_RUNNING_CORE,
                                                                         (uint32_t)inst->dma_buff);
    inst->dma_cfg.buff_len_in_4bytes = ADC_MON_SEQ_LEN;
    inst->dma_cfg.stop_en = false;
    inst->dma_cfg.stop_pos = 0U;
    stat = adc16_init_seq_dma(inst->base, &inst->dma_cfg);
    if (stat != status_success) {
        LOGM_E(LOG_MOD_ADC, "ADC sequence DMA configuration failed (%d)", (int)stat);
        return stat;
    }

    adc16_clear_status_flags(inst->base, adc16_get_status_flags(inst->base));
    return status_success;
}

/* ------------------------------------------------------------------------- */
/* Public API                                                                 */
/* ------------------------------------------------------------------------- */

void adc_mon_init(void)
{
    s_initialized = false;
    s_state = ADC_MON_STATE_IDLE;
    s_error_count = 0U;
    s_data = (adc_mon_data_t){0};

    for (uint32_t i = 0U; i < ADC_MON_INSTANCE_COUNT; i++) {
        if (adc_mon_init_instance(&s_adc_instances[i], s_adc_clock_names[i]) != status_success) {
            LOGM_E(LOG_MOD_ADC, "ADC%d monitoring initialization failed", (int)i);
            return;
        }
    }

    s_sample_period_ms = (ADC_MON_SAMPLE_PERIOD_MS != 0U) ? ADC_MON_SAMPLE_PERIOD_MS : 1U;
    s_last_sample_ms = g_app_tick_ms;
    adc_mon_apply_params();
    s_initialized = true;

    LOGM_I(LOG_MOD_ADC, "ADC0 IN%u/IN%u, ADC1 IN%u/IN%u, 16 bit sequence mode",
           (unsigned)BOARD_ADC0_SYS_CURRENT_CH, (unsigned)BOARD_ADC0_SYS_VOLTAGE_CH,
           (unsigned)BOARD_ADC1_ESC1_CURRENT_CH, (unsigned)BOARD_ADC1_ESC2_CURRENT_CH);
}

void adc_mon_task(void)
{
    uint32_t now;

    if (!s_initialized) {
        return;
    }
    now = g_app_tick_ms;

    if (s_state == ADC_MON_STATE_WAIT) {
        if (adc_mon_sequence_complete(&s_adc_instances[0]) &&
            adc_mon_sequence_complete(&s_adc_instances[1])) {
            adc_mon_process();
            adc_mon_clear_sequence_flags();
            s_state = ADC_MON_STATE_IDLE;
        } else if ((uint32_t)(now - s_trigger_ms) >= ADC_MON_TIMEOUT_MS) {
            s_error_count++;
            adc_mon_abort_sequences();
            s_state = ADC_MON_STATE_IDLE;
        }
        return;
    }

    /* IDLE: start the next sequence when the sample period has elapsed */
    if ((uint32_t)(now - s_last_sample_ms) < s_sample_period_ms) {
        return;
    }
    s_last_sample_ms = now;
    if (adc_mon_start_sequences()) {
        s_trigger_ms = now;
        s_state = ADC_MON_STATE_WAIT;
    } else {
        /* A software trigger conflict means the previous sequence is still
         * running; abort it and try again on the next call. */
        s_error_count++;
        adc_mon_abort_sequences();
    }
}

void adc_mon_get(adc_mon_data_t *data)
{
    if (data == NULL) {
        return;
    }
    *data = s_data;
}

void adc_mon_apply_params(void)
{
    int32_t scale;

    scale = param_get(PARAM_ADC_SYS_V_SCALE);
    if (scale <= 0) {
        scale = ADC_MON_V_SCALE_FALLBACK_UV;
        LOGM_W(LOG_MOD_ADC, "PARAM_ADC_SYS_V_SCALE not calibrated, using %ld uV/LSB",
               (long)scale);
    }
    s_params.v_scale_uv = scale;
    s_params.v_offset_mv = param_get(PARAM_ADC_SYS_V_OFFSET_MV);

    scale = param_get(PARAM_ADC_SYS_I_SCALE);
    if (scale <= 0) {
        scale = ADC_MON_I_SCALE_FALLBACK_UA;
        LOGM_W(LOG_MOD_ADC, "PARAM_ADC_SYS_I_SCALE not calibrated, using %ld uA/LSB",
               (long)scale);
    }
    s_params.i_scale_ua = scale;
    s_params.i_offset_ma = param_get(PARAM_ADC_SYS_I_OFFSET_MA);

    for (uint32_t ch = 0U; ch < APP_CFG_DSHOT_MOTORS; ch++) {
        /* PARAM_ADC_ESC1_I_SCALE / PARAM_ADC_ESC2_I_SCALE are consecutive */
        scale = param_get((param_id_t)((int)PARAM_ADC_ESC1_I_SCALE + (int)ch));
        if (scale <= 0) {
            scale = ADC_MON_ESC_I_SCALE_FALLBACK_UA;
            LOGM_W(LOG_MOD_ADC, "PARAM_ADC_ESC%u_I_SCALE not calibrated, using %ld uA/LSB",
                   (unsigned)(ch + 1U), (long)scale);
        }
        s_params.esc_i_scale_ua[ch] = scale;
    }
    s_params.esc_i_offset_ma = param_get(PARAM_ADC_ESC_I_OFFSET_MA);

    s_params.volt_min_dv = param_get(PARAM_ADC_VOLT_MIN_DV);
    s_params.volt_max_dv = param_get(PARAM_ADC_VOLT_MAX_DV);
    s_params.curr_max_ca = param_get(PARAM_ADC_CURR_MAX_CA);
    s_params.esc_curr_max_ma = param_get(PARAM_ADC_ESC_CURR_MAX_MA);
}
