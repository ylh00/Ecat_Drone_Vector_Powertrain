/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * S288 / J288 brushless bus servo driver (half duplex single wire UART)
 *
 *   Servo #1 : UART2 (PC08 TXD / PC09 RXD), direction switch PC16
 *   Servo #2 : UART3 (PC15 TXD / PC14 RXD), direction switch PD00
 *
 * The servo bus is a 1-Wire single bus with UART TTL timing.  On this board an
 * SPDT analogue switch selects whether the MCU TX or RX pin is connected to
 * the servo signal line; the switch is controlled by a GPIO and its active
 * level for "transmit" comes from PARAM_SERVO_DIR_TX_LEVELn.
 *
 * Protocol (无刷数字舵机 J288/S288 使用手册, section 七/九/十/十一, pp.5-11):
 *   command  : 20 bytes, head FE EE, id:4 | mode:3 | timeout:1, res,
 *              int16 tor_des, int16 spd_des, int32 pos_des, int16 k_pos,
 *              int16 k_spd, uint32 CRC32 (CRC over bytes 0..15)
 *   feedback : 26 bytes, head FC EE, id:4 | mode:3 | timeout:1, int8 temp,
 *              uint8 sensor, uint8 vol, int16 torque, int16 speed, int32 pos,
 *              uint32 MError, uint16 OutPos:13 | ExFlag:3, uint8 ExSensor2,
 *              uint8 ExCom, uint32 CRC32 (CRC over bytes 2..21)
 *   all multi byte fields are little endian, the CRC is the bitwise
 *   "crc32_core()" of the manual (poly 0x04C11DB7, init 0xFFFFFFFF, MSB first,
 *   no reflection, no final xor, one little endian 32 bit word at a time).
 *
 * Transfer scheme (non blocking, both buses in parallel):
 *   IDLE --(poll tick)--> switch SPDT to TX --> TX_SETUP (>=2 us) -->
 *   TX DMA (20 byte frame) --> UART TX idle interrupt (line high for 2 bits
 *   after the last stop bit) --> switch SPDT to RX + arm RX DMA (26 bytes) -->
 *   RX DMA transfer complete interrupt (or RX idle / timeout) --> parse frame
 *   in the main loop --> IDLE
 * A fallback in TX state also detects the end of transmission with the DMA
 * transfer complete flag + UART TEMT status in case the TX idle interrupt is
 * not available/does not fire.
 *
 * SDK references this driver is based on:
 *   hpm_sdk/samples/drivers/uart/uart_hardware_rx_idle/src/uart.c
 *   hpm_sdk/samples/drivers/uart/uart_dma/src/uart.c
 *   hpm_sdk/samples/drivers/dmav2/dmav2_uart_rx_circle_transfer/src/dma.c
 *   hpm_sdk/samples/drivers/uart/uart_irq/src/uart.c
 * The register / bitfield observations are documented in docs/SERVO_S288.md.
 */

#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "board.h"
#include "app_config.h"
#include "app_log.h"
#include "app_task.h"
#include "param.h"
#include "servo_s288.h"

#include "hpm_clock_drv.h"
#include "hpm_csr_drv.h"
#include "hpm_dmav2_drv.h"
#include "hpm_dmamux_drv.h"
#include "hpm_gpio_drv.h"
#include "hpm_interrupt.h"
#include "hpm_uart_drv.h"

#if (APP_CFG_SERVO_COUNT != 2U)
#error "servo_s288.c implements exactly the two servo buses of ecat_drone_5e3y"
#endif

/* ==========================================================================
 * Protocol constants (manual section 九, pp.7-10)
 * ========================================================================== */
#define SERVO_CMD_HEAD0             (0xFEU)
#define SERVO_CMD_HEAD1             (0xEEU)
#define SERVO_FBK_HEAD0             (0xFCU)
#define SERVO_FBK_HEAD1             (0xEEU)

#define SERVO_CMD_SIZE              (20U)
#define SERVO_FBK_SIZE              (26U)

/* command frame byte offsets */
#define SERVO_CMD_OFS_HEAD          (0U)
#define SERVO_CMD_OFS_MODE          (2U)
#define SERVO_CMD_OFS_RES           (3U)
#define SERVO_CMD_OFS_TOR_DES       (4U)
#define SERVO_CMD_OFS_SPD_DES       (6U)
#define SERVO_CMD_OFS_POS_DES       (8U)
#define SERVO_CMD_OFS_K_POS         (12U)
#define SERVO_CMD_OFS_K_SPD         (14U)
#define SERVO_CMD_OFS_CRC           (16U)
#define SERVO_CMD_CRC_WORDS         (4U)  /* bytes 0..15 */

/* feedback frame byte offsets */
#define SERVO_FBK_OFS_HEAD          (0U)
#define SERVO_FBK_OFS_MODE          (2U)
#define SERVO_FBK_OFS_TEMP          (3U)
#define SERVO_FBK_OFS_SENSOR        (4U)
#define SERVO_FBK_OFS_VOL           (5U)
#define SERVO_FBK_OFS_TORQUE        (6U)
#define SERVO_FBK_OFS_SPEED         (8U)
#define SERVO_FBK_OFS_POS           (10U)
#define SERVO_FBK_OFS_MERROR        (14U)
#define SERVO_FBK_OFS_OUTPOS        (18U)
#define SERVO_FBK_OFS_EXSENSOR2     (20U)
#define SERVO_FBK_OFS_EXCOM         (21U)
#define SERVO_FBK_OFS_CRC           (22U)
#define SERVO_FBK_CRC_OFS           (2U)  /* the head is not part of the CRC  */
#define SERVO_FBK_CRC_WORDS         (5U)  /* bytes 2..21                      */

#define SERVO_OUTPOS_MASK           (0x1FFFU) /* OutPos : 13 bits            */
#define SERVO_EXFLAG_SHIFT          (13U)     /* ExFlag  : 3 bits            */

#define SERVO_ID_MAX                (14U)  /* 0..14 addressable, 15 = broadcast */
#define SERVO_ID_BROADCAST          (15U)

/* ==========================================================================
 * Mechanical / protocol scaling
 *
 * The J288/S288 has a 288.35:1 gear box (RATIO = 70070 / 243).  The protocol
 * uses rotor side units: 32768/2pi = 1 rad (position), 2.56/2pi = 1 rad/s
 * (speed), 256000 = 1 N.m (torque), 1280000 = 1 (k_pos), 128000000 = 1 (k_spd).
 * ========================================================================== */
#define SERVO_GEAR_RATIO_NUM        (70070LL)
#define SERVO_GEAR_RATIO_DEN        (243LL)
/* (70070/243)^2, used to convert the user facing Kp/Kd parameters to the
 * int16 protocol fields (see servo_gain_param_to_field()) */
#define SERVO_GEAR_RATIO_SQ         (83148LL)

/* 0.1 deg (output side) -> rotor position raw: rot = deg/1800*pi*RATIO*32768/2pi */
#define SERVO_CMD_POS_NUM           (32768LL * SERVO_GEAR_RATIO_NUM) /* 2296053760 */
#define SERVO_CMD_POS_DEN           (3600LL * SERVO_GEAR_RATIO_DEN)  /* 874800     */

/* feedback scaling (all integer, Q16 multipliers) */
#define SERVO_FBK_SPEED_RPM_Q16     (5327L) /* raw speed * 60/(2.56*RATIO), Q16     */
#define SERVO_FBK_TORQUE_CNM_Q16    (7382L) /* raw torque / 256000 * RATIO, 0.01Nm  */
/* 13 bit output encoder (8192 = one output turn = 3600 units of 0.1 deg) */
#define SERVO_OUTPOS_TO_D10_NUM     (225L)
#define SERVO_OUTPOS_TO_D10_SHIFT   (9U)    /* 225 / 512 = 3600 / 8192           */
/* The S288 does not report the motor current.  Estimate it from the rotor
 * torque with an assumed torque constant of 0.1 N.m/A:
 *   I[mA] = torque_raw / 256000 [N.m] / 0.1 [N.m/A] * 1000 = torque_raw / 25.6
 * Adjust SERVO_MOTOR_KT_NM_PER_A if the real motor constant is known. */
#define SERVO_MOTOR_KT_NM_PER_A     (0.1)
#define SERVO_FBK_CURRENT_NUM       (10L)   /* torque_raw * 1000 / 25600 = *10/256 */
#define SERVO_FBK_CURRENT_DEN       (256L)

/* ==========================================================================
 * UART / DMA / timing configuration
 * ========================================================================== */
/* HDMA channels reserved for the servo driver (see TEAM_CONTEXT / Lead) */
#define SERVO1_TX_DMA_CH            (4U)
#define SERVO1_RX_DMA_CH            (5U)
#define SERVO2_TX_DMA_CH            (6U)
#define SERVO2_RX_DMA_CH            (7U)

#define SERVO_UART_IRQ_PRIORITY     (2U)
#define SERVO_DMA_IRQ_PRIORITY      (3U)

#define SERVO_US_TO_CYCLES(us)      ((uint32_t)((us) * (BOARD_CPU_FREQ / 1000000U)))
#define SERVO_DIR_SETUP_CYCLES      SERVO_US_TO_CYCLES(BOARD_SERVO_DIR_SETUP_US)
#define SERVO_RX_TIMEOUT_CYCLES     SERVO_US_TO_CYCLES(APP_CFG_SERVO_UART_TIMEOUT_US)
#define SERVO_TX_START_WAIT_CYCLES  SERVO_US_TO_CYCLES(3U) /* bounded wait, see below */
#define SERVO_RX_IDLE_THRESHOLD_BITS (20U) /* 20 bit times: no response yet      */
#define SERVO_TX_IDLE_THRESHOLD_BITS (2U)  /* 2 bit times after the last stop bit */
#define SERVO_ONLINE_LOST_CNT       (3U)   /* consecutive errors -> online = 0   */

#define SERVO_BITMAP_BYTES          (32U)

#if defined(HPM_IP_FEATURE_UART_RX_IDLE_DETECT) && (HPM_IP_FEATURE_UART_RX_IDLE_DETECT == 1)
#define SERVO_HAS_RX_IDLE           (1)
#define SERVO_UART_IRQ_RX_IDLE      (uart_intr_rx_line_idle)
#else
#define SERVO_HAS_RX_IDLE           (0)
#define SERVO_UART_IRQ_RX_IDLE      (0U)
#endif

#if defined(HPM_IP_FEATURE_UART_TX_IDLE_DETECT) && (HPM_IP_FEATURE_UART_TX_IDLE_DETECT == 1)
#define SERVO_HAS_TX_IDLE           (1)
#define SERVO_UART_IRQ_TX_IDLE      (uart_intr_tx_line_idle)
#else
#define SERVO_HAS_TX_IDLE           (0)
#define SERVO_UART_IRQ_TX_IDLE      (0U)
#endif

/* ==========================================================================
 * Hardware description of the two servo buses (board.h contract)
 * ========================================================================== */
typedef struct {
    UART_Type *uart;
    uint32_t uart_irq;
    clock_name_t clk_name;
    uint8_t rx_dma_req;
    uint8_t tx_dma_req;
    DMAV2_Type *dma;
    uint8_t tx_dma_ch;
    uint8_t rx_dma_ch;
    GPIO_Type *dir_gpio;
    uint32_t dir_port;
    uint8_t dir_pin;
    uint32_t pad_tx;
    uint32_t func_tx;
    uint32_t pad_rx;
    uint32_t func_rx;
    uint32_t pad_dir;
    uint32_t func_dir;
} servo_bus_hw_t;

static const servo_bus_hw_t s_servo_hw[APP_CFG_SERVO_COUNT] = {
    {
        .uart = BOARD_SERVO1_UART_BASE,
        .uart_irq = (uint32_t)BOARD_SERVO1_UART_IRQ,
        .clk_name = BOARD_SERVO1_UART_CLK_NAME,
        .rx_dma_req = (uint8_t)BOARD_SERVO1_UART_RX_DMA_REQ,
        .tx_dma_req = (uint8_t)BOARD_SERVO1_UART_TX_DMA_REQ,
        .dma = BOARD_APP_DMA0,
        .tx_dma_ch = SERVO1_TX_DMA_CH,
        .rx_dma_ch = SERVO1_RX_DMA_CH,
        .dir_gpio = BOARD_SERVO1_DIR_GPIO,
        .dir_port = (uint32_t)BOARD_SERVO1_DIR_GPIO_INDEX,
        .dir_pin = (uint8_t)BOARD_SERVO1_DIR_GPIO_PIN,
        .pad_tx = (uint32_t)IOC_PAD_PC08,
        .func_tx = IOC_PC08_FUNC_CTL_UART2_TXD,
        .pad_rx = (uint32_t)IOC_PAD_PC09,
        .func_rx = IOC_PC09_FUNC_CTL_UART2_RXD,
        .pad_dir = (uint32_t)IOC_PAD_PC16,
        .func_dir = IOC_PC16_FUNC_CTL_GPIO_C_16,
    },
    {
        .uart = BOARD_SERVO2_UART_BASE,
        .uart_irq = (uint32_t)BOARD_SERVO2_UART_IRQ,
        .clk_name = BOARD_SERVO2_UART_CLK_NAME,
        .rx_dma_req = (uint8_t)BOARD_SERVO2_UART_RX_DMA_REQ,
        .tx_dma_req = (uint8_t)BOARD_SERVO2_UART_TX_DMA_REQ,
        .dma = BOARD_APP_DMA0,
        .tx_dma_ch = SERVO2_TX_DMA_CH,
        .rx_dma_ch = SERVO2_RX_DMA_CH,
        .dir_gpio = BOARD_SERVO2_DIR_GPIO,
        .dir_port = (uint32_t)BOARD_SERVO2_DIR_GPIO_INDEX,
        .dir_pin = (uint8_t)BOARD_SERVO2_DIR_GPIO_PIN,
        .pad_tx = (uint32_t)IOC_PAD_PC15,
        .func_tx = IOC_PC15_FUNC_CTL_UART3_TXD,
        .pad_rx = (uint32_t)IOC_PAD_PC14,
        .func_rx = IOC_PC14_FUNC_CTL_UART3_RXD,
        .pad_dir = (uint32_t)IOC_PAD_PD00,
        .func_dir = IOC_PD00_FUNC_CTL_GPIO_D_00,
    },
};

/* ==========================================================================
 * Bus state machine
 * ========================================================================== */
typedef enum {
    SERVO_BUS_STATE_IDLE = 0,   /* RX path selected, waiting for the poll tick */
    SERVO_BUS_STATE_TX_SETUP,   /* SPDT switched to TX, waiting setup time     */
    SERVO_BUS_STATE_TX,         /* command frame being transmitted             */
    SERVO_BUS_STATE_RX,         /* waiting for the 26 byte feedback frame      */
} servo_bus_state_t;

typedef struct {
    const servo_bus_hw_t *hw;
    uint8_t index;
    uint8_t init_ok;
    uint8_t *tx_frame;          /* 20 byte command frame (non cacheable)       */
    uint8_t *rx_frame;          /* 26 byte feedback frame (non cacheable)      */
    /* command cache (written by the API, read by servo_task()) */
    int16_t target_d10;         /* target angle after gain/offset/clamp        */
    uint8_t enable;             /* 1 = FOC position control, 0 = locked        */
    uint8_t clear_timeout;      /* one shot: send a frame with timeout = 0     */
    /* runtime state */
    uint8_t state;
    uint8_t dir_tx_level;
    uint8_t txn_id;             /* servo id of the running transaction         */
    uint8_t txn_is_scan;        /* transaction belongs to a bus scan           */
    uint8_t expect_response;
    volatile uint8_t tx_done;   /* TX DMA transfer complete (ISR)              */
    volatile uint8_t rx_done;   /* RX DMA transfer complete (ISR)              */
    volatile uint8_t rx_idle;   /* UART RX line idle seen while in RX (ISR)    */
    volatile uint8_t dma_error; /* DMA error / abort (ISR)                     */
    uint8_t consecutive_err;    /* consecutive failed transactions             */
    uint32_t phase_start_cycles;
    uint32_t tx_start_cycles;
    uint32_t rx_start_cycles;
    uint32_t min_tx_cycles;     /* 20 bytes * 10 bit times / baud              */
    uint32_t tx_timeout_cycles;
    uint32_t next_poll_tick_ms;
    /* scan context */
    uint8_t scan_active;
    uint8_t scan_done_pending;
    uint8_t scan_id;
    uint8_t scan_id_min;
    uint8_t scan_id_max;
    uint8_t *scan_bitmap;
    /* latest status */
    servo_status_t status;
} servo_bus_t;

static servo_bus_t s_servo_bus[APP_CFG_SERVO_COUNT];
/* DMA buffers have to be in the non cacheable region (L1 D-cache on HPM5E00) */
ATTR_PLACE_AT_NONCACHEABLE static uint8_t s_servo_tx_frame[APP_CFG_SERVO_COUNT][SERVO_CMD_SIZE];
ATTR_PLACE_AT_NONCACHEABLE static uint8_t s_servo_rx_frame[APP_CFG_SERVO_COUNT][SERVO_FBK_SIZE];

static uint32_t s_servo_baud;
static uint8_t s_servo_initialized;

/* ==========================================================================
 * Small helpers
 * ========================================================================== */
static inline uint32_t servo_cycles_now(void)
{
    return (uint32_t)hpm_csr_get_core_cycle();
}

static inline uint32_t servo_cycles_since(uint32_t start_cycles)
{
    return servo_cycles_now() - start_cycles;
}

static inline void servo_put_le16(uint8_t *dst, int16_t value)
{
    uint16_t v = (uint16_t)value;
    dst[0] = (uint8_t)(v & 0xFFU);
    dst[1] = (uint8_t)((v >> 8) & 0xFFU);
}

static inline void servo_put_le32(uint8_t *dst, uint32_t value)
{
    dst[0] = (uint8_t)(value & 0xFFU);
    dst[1] = (uint8_t)((value >> 8) & 0xFFU);
    dst[2] = (uint8_t)((value >> 16) & 0xFFU);
    dst[3] = (uint8_t)((value >> 24) & 0xFFU);
}

static inline int16_t servo_get_le16(const uint8_t *src)
{
    return (int16_t)((uint16_t)src[0] | ((uint16_t)src[1] << 8));
}

static inline uint32_t servo_get_le32(const uint8_t *src)
{
    return (uint32_t)src[0] | ((uint32_t)src[1] << 8) |
           ((uint32_t)src[2] << 16) | ((uint32_t)src[3] << 24);
}

/* Multiply a signed 16 bit value by a Q16 factor with rounding. */
static inline int32_t servo_mul_q16(int32_t value, int32_t q16)
{
    int64_t tmp = ((int64_t)value * q16);
    return (int32_t)((tmp + ((tmp >= 0) ? 32768 : -32768)) >> 16);
}

/*
 * CRC32 of the S288 protocol, "crc32_core()" of the manual (section 九, p.9-10):
 * poly 0x04C11DB7, init 0xFFFFFFFF, MSB first, no reflection, no final xor.
 * The manual computes the CRC on little endian 32 bit words, i.e. the bytes of
 * every 4 byte word are consumed in reversed order.  `words` is the number of
 * 32 bit words covered by the CRC (4 for a command frame, 5 for feedback).
 */
static uint32_t servo_crc32(const uint8_t *data, uint32_t words)
{
    const uint32_t polynomial = 0x04C11DB7U;
    uint32_t crc = 0xFFFFFFFFU;

    for (uint32_t i = 0; i < words; i++) {
        uint32_t word = (uint32_t)data[4U * i] |
                        ((uint32_t)data[4U * i + 1U] << 8) |
                        ((uint32_t)data[4U * i + 2U] << 16) |
                        ((uint32_t)data[4U * i + 3U] << 24);
        uint32_t xbit = 0x80000000U;
        for (uint32_t bits = 0; bits < 32U; bits++) {
            if ((crc & 0x80000000U) != 0U) {
                crc = (crc << 1) ^ polynomial;
            } else {
                crc <<= 1;
            }
            if ((word & xbit) != 0U) {
                crc ^= polynomial;
            }
            xbit >>= 1;
        }
    }
    return crc;
}

/* 0.1 deg at the output shaft -> rotor side position command of the protocol. */
static int32_t servo_angle_d10_to_pos_raw(int32_t angle_d10)
{
    int64_t raw;
    int64_t num = (int64_t)angle_d10 * SERVO_CMD_POS_NUM;

    raw = (num >= 0) ? ((num + (SERVO_CMD_POS_DEN / 2)) / SERVO_CMD_POS_DEN)
                     : ((num - (SERVO_CMD_POS_DEN / 2)) / SERVO_CMD_POS_DEN);
    if (raw > INT32_MAX) {
        raw = INT32_MAX;
    } else if (raw < INT32_MIN) {
        raw = INT32_MIN;
    }
    return (int32_t)raw;
}

/*
 * Convert a PARAM_SERVO_KPn / PARAM_SERVO_KDn value to the int16 protocol
 * field.  The parameters are the user facing gains scaled so that
 * 1280000 = 1.0 (Kp) and 128000000 = 1.0 (Kd), exactly like the vendor demo:
 *   k_pos_field = Kp / RATIO^2 * 1280000
 *   k_spd_field = Kd / RATIO^2 * 128000000
 * With param = gain * scale both reduce to param / RATIO^2.
 */
static int32_t servo_gain_param_to_field(int32_t param)
{
    int32_t field;
    if (param <= 0) {
        return 0;
    }
    field = (int32_t)((int64_t)param / SERVO_GEAR_RATIO_SQ);
    if (field > INT16_MAX) {
        field = INT16_MAX;
    }
    return field;
}

/* ==========================================================================
 * Hardware init helpers
 * ========================================================================== */
static void servo_set_dir(servo_bus_t *bus, bool tx)
{
    uint8_t level = tx ? bus->dir_tx_level : (uint8_t)(bus->dir_tx_level ^ 1U);
    gpio_write_pin(bus->hw->dir_gpio, bus->hw->dir_port, bus->hw->dir_pin, level);
}

static void servo_init_pinmux(const servo_bus_hw_t *hw)
{
    HPM_IOC->PAD[hw->pad_tx].FUNC_CTL = hw->func_tx;
    HPM_IOC->PAD[hw->pad_rx].FUNC_CTL = hw->func_rx;
    HPM_IOC->PAD[hw->pad_dir].FUNC_CTL = hw->func_dir;
}

static void servo_init_dir_gpio(servo_bus_t *bus)
{
    const servo_bus_hw_t *hw = bus->hw;
    uint8_t rx_level = (uint8_t)(bus->dir_tx_level ^ 1U);

    gpio_set_pin_output_with_initial(hw->dir_gpio, hw->dir_port, hw->dir_pin,
                                     rx_level);
}

static void servo_init_dma(const servo_bus_hw_t *hw)
{
    uint32_t rx_mux = (uint32_t)DMA_SOC_CHN_TO_DMAMUX_CHN(hw->dma, hw->rx_dma_ch);
    uint32_t tx_mux = (uint32_t)DMA_SOC_CHN_TO_DMAMUX_CHN(hw->dma, hw->tx_dma_ch);

    dmamux_config(BOARD_APP_DMAMUX, (uint8_t)rx_mux, hw->rx_dma_req, true);
    dmamux_config(BOARD_APP_DMAMUX, (uint8_t)tx_mux, hw->tx_dma_req, true);
}

/* Does the HPM UART driver accept `baud` with a source clock of `freq` Hz?
 * Mirrors uart_calculate_baudrate() in hpm_uart_drv.c (osc 8..32 even,
 * divider 1..65535, <= 3% frequency error). */
static bool servo_uart_baud_supported(uint32_t freq, uint32_t baud)
{
    if ((freq == 0U) || (baud < 200U)) {
        return false;
    }
    if (freq < (baud * 8U)) {
        return false;
    }
    if ((freq / 0xFFFFU) > (baud * 32U)) {
        return false;
    }
    for (uint32_t osc = 8U; osc <= 32U; osc += 2U) {
        uint32_t scale = baud * osc;
        uint32_t div = (freq + (scale / 2U)) / scale;
        uint32_t actual;
        uint32_t delta;

        if ((div < 1U) || (div > 0xFFFFU)) {
            continue;
        }
        actual = div * scale;
        delta = (actual > freq) ? (actual - freq) : (freq - actual);
        if (((uint64_t)delta * 100U) <= ((uint64_t)freq * 3U)) {
            return true;
        }
    }
    return false;
}

/*
 * Configure the UART clock of one servo bus.  The default UART clock of
 * HPM5E00 (clock preset) is PLL1CLK0/4 = 80 MHz, which cannot generate
 * exactly 6.000 Mbps; this board configures UART2/3 to 48 MHz (PLL0CLK0/10)
 * which gives 6.000 Mbps with osc = 8, div = 1.  Should that clock ever be
 * changed the driver falls back to PLL0CLK0 / 1 (480 MHz, osc = 8, div = 10).
 */
static uint32_t servo_uart_clock_setup(clock_name_t clk_name, uint32_t baud)
{
    uint32_t freq;

    clock_add_to_group(clk_name, BOARD_RUNNING_CORE);
    freq = clock_get_frequency(clk_name);
    if (!servo_uart_baud_supported(freq, baud)) {
        if (clock_set_source_divider(clk_name, clk_src_pll0_clk0, 1U) == status_success) {
            freq = clock_get_frequency(clk_name);
        }
    }
    return freq;
}

static hpm_stat_t servo_uart_init(servo_bus_t *bus, uint32_t baud)
{
    UART_Type *uart = bus->hw->uart;
    uart_config_t config;
    uint32_t src_freq;
    hpm_stat_t stat;

    src_freq = servo_uart_clock_setup(bus->hw->clk_name, baud);
    uart_default_config(uart, &config);
    config.baudrate = baud;
    config.src_freq_in_hz = src_freq;
    config.fifo_enable = true;
    config.dma_enable = true;
    config.tx_fifo_level = uart_tx_fifo_trg_not_full;
    config.rx_fifo_level = uart_rx_fifo_trg_not_empty;
#if SERVO_HAS_RX_IDLE
    config.rxidle_config.detect_enable = true;
    config.rxidle_config.detect_irq_enable = false;
    config.rxidle_config.idle_cond = uart_rxline_idle_cond_rxline_logic_one;
    config.rxidle_config.threshold = SERVO_RX_IDLE_THRESHOLD_BITS;
#endif
#if SERVO_HAS_TX_IDLE
    config.txidle_config.detect_enable = true;
    config.txidle_config.detect_irq_enable = false;
    config.txidle_config.idle_cond = uart_rxline_idle_cond_state_machine_idle;
    config.txidle_config.threshold = SERVO_TX_IDLE_THRESHOLD_BITS;
#endif
    stat = uart_init(uart, &config);
    if (stat != status_success) {
        return stat;
    }
    /* frame duration of a 20 byte / 10 bit frame, used for sanity checks and
     * as the hard timeout of the transmit phase */
    bus->min_tx_cycles = (uint32_t)(((uint64_t)SERVO_CMD_SIZE * 10U * BOARD_CPU_FREQ) / baud);
    bus->tx_timeout_cycles = bus->min_tx_cycles + SERVO_US_TO_CYCLES(1000U);
    return status_success;
}

/* ==========================================================================
 * Low level transaction control
 * ========================================================================== */
static void servo_bus_stop(servo_bus_t *bus)
{
    UART_Type *uart = bus->hw->uart;

#if SERVO_HAS_TX_IDLE
    uart_disable_irq(uart, SERVO_UART_IRQ_TX_IDLE);
#endif
#if SERVO_HAS_RX_IDLE
    uart_disable_irq(uart, SERVO_UART_IRQ_RX_IDLE);
#endif
    dma_disable_channel(bus->hw->dma, bus->hw->tx_dma_ch);
    dma_disable_channel(bus->hw->dma, bus->hw->rx_dma_ch);
    dma_clear_transfer_status(bus->hw->dma, bus->hw->tx_dma_ch);
    dma_clear_transfer_status(bus->hw->dma, bus->hw->rx_dma_ch);
    uart_reset_rx_fifo(uart);
    servo_set_dir(bus, false);
    bus->tx_done = 0U;
    bus->rx_done = 0U;
    bus->rx_idle = 0U;
    bus->dma_error = 0U;
}

static hpm_stat_t servo_bus_start_tx_dma(servo_bus_t *bus)
{
    UART_Type *uart = bus->hw->uart;
    dma_handshake_config_t config;

    dma_default_handshake_config(bus->hw->dma, &config);
    config.ch_index = bus->hw->tx_dma_ch;
    config.dst = (uint32_t)&uart->THR;
    config.dst_fixed = true;
    config.src = core_local_mem_to_sys_address(BOARD_RUNNING_CORE,
                                              (uint32_t)bus->tx_frame);
    config.src_fixed = false;
    config.data_width = DMA_TRANSFER_WIDTH_BYTE;
    config.size_in_byte = SERVO_CMD_SIZE;
    if (dma_setup_handshake(bus->hw->dma, &config, true) != status_success) {
        return status_fail;
    }
#if SERVO_HAS_TX_IDLE
    /* Wait (bounded, <= 3 us) until the frame has started on the wire and clear
     * a possibly stale TX idle flag before the interrupt is armed, so that the
     * interrupt can only be caused by the end of this frame. */
    {
        uint32_t start = servo_cycles_now();
        while (uart_is_txline_idle(uart) &&
               (servo_cycles_since(start) < SERVO_TX_START_WAIT_CYCLES)) {
        }
        uart_clear_txline_idle_flag(uart);
    }
#endif
    bus->tx_start_cycles = servo_cycles_now();
    bus->state = SERVO_BUS_STATE_TX;
#if SERVO_HAS_TX_IDLE
    uart_enable_irq(uart, SERVO_UART_IRQ_TX_IDLE);
#endif
    return status_success;
}

static void servo_bus_start_rx(servo_bus_t *bus)
{
    UART_Type *uart = bus->hw->uart;
    dma_handshake_config_t config;

    dma_disable_channel(bus->hw->dma, bus->hw->rx_dma_ch);
    dma_clear_transfer_status(bus->hw->dma, bus->hw->rx_dma_ch);

    /* select the receive path of the SPDT switch */
    servo_set_dir(bus, false);
    uart_reset_rx_fifo(uart);
#if defined(HPM_IP_FEATURE_UART_RX_LINE_ERROR_DETECT) && (HPM_IP_FEATURE_UART_RX_LINE_ERROR_DETECT == 1)
    uart_clear_rx_fifo_error_flag(uart);
    uart_clear_rx_overrun_flag(uart);
#endif
#if SERVO_HAS_RX_IDLE
    uart_clear_rxline_idle_flag(uart);
#endif
    /* arm the RX DMA for exactly one 26 byte feedback frame; the transfer
     * complete interrupt therefore coincides with a complete response */
    dma_default_handshake_config(bus->hw->dma, &config);
    config.ch_index = bus->hw->rx_dma_ch;
    config.dst = core_local_mem_to_sys_address(BOARD_RUNNING_CORE,
                                              (uint32_t)bus->rx_frame);
    config.dst_fixed = false;
    config.src = (uint32_t)&uart->RBR;
    config.src_fixed = true;
    config.data_width = DMA_TRANSFER_WIDTH_BYTE;
    config.size_in_byte = SERVO_FBK_SIZE;
    (void)dma_setup_handshake(bus->hw->dma, &config, true);

    bus->rx_done = 0U;
    bus->rx_idle = 0U;
    bus->dma_error = 0U;
    bus->rx_start_cycles = servo_cycles_now();
    bus->state = SERVO_BUS_STATE_RX;
#if SERVO_HAS_RX_IDLE
    uart_enable_irq(uart, SERVO_UART_IRQ_RX_IDLE);
#endif
}

/* ==========================================================================
 * Forward declarations
 * ========================================================================== */
static void servo_bus_finish_no_response(servo_bus_t *bus);

/* ==========================================================================
 * Command frame build / feedback frame parse
 * ========================================================================== */
static void servo_bus_build_command(servo_bus_t *bus, uint8_t id)
{
    uint8_t *frame = bus->tx_frame;
    uint8_t mode = (bus->enable != 0U) ? SERVO_CTRL_MODE_FOC : SERVO_CTRL_MODE_LOCK;
    uint8_t timeout_en = (bus->clear_timeout != 0U) ? 0U : 1U;
    int32_t k_pos = 0;
    int32_t k_spd = 0;

    if (bus->enable != 0U) {
        k_pos = servo_gain_param_to_field(param_get(PARAM_SERVO_KP(bus->index)));
        k_spd = servo_gain_param_to_field(param_get(PARAM_SERVO_KD(bus->index)));
    }
    bus->clear_timeout = 0U;

    frame[SERVO_CMD_OFS_HEAD] = SERVO_CMD_HEAD0;
    frame[SERVO_CMD_OFS_HEAD + 1U] = SERVO_CMD_HEAD1;
    frame[SERVO_CMD_OFS_MODE] = (uint8_t)((id & 0x0FU) |
                                          ((mode & 0x07U) << 4) |
                                          ((timeout_en & 0x01U) << 7));
    frame[SERVO_CMD_OFS_RES] = 0U;
    servo_put_le16(&frame[SERVO_CMD_OFS_TOR_DES], 0); /* torque feed forward   */
    servo_put_le16(&frame[SERVO_CMD_OFS_SPD_DES], 0); /* speed feed forward    */
    servo_put_le32(&frame[SERVO_CMD_OFS_POS_DES],
                   (uint32_t)servo_angle_d10_to_pos_raw(bus->target_d10));
    servo_put_le16(&frame[SERVO_CMD_OFS_K_POS], (int16_t)k_pos);
    servo_put_le16(&frame[SERVO_CMD_OFS_K_SPD], (int16_t)k_spd);
    servo_put_le32(&frame[SERVO_CMD_OFS_CRC],
                   servo_crc32(frame, SERVO_CMD_CRC_WORDS));

    bus->status.enabled = bus->enable;
}

static bool servo_bus_feedback_valid(const servo_bus_t *bus)
{
    const uint8_t *frame = bus->rx_frame;
    uint32_t crc_rx;
    uint32_t crc_calc;

    if ((frame[SERVO_FBK_OFS_HEAD] != SERVO_FBK_HEAD0) ||
        (frame[SERVO_FBK_OFS_HEAD + 1U] != SERVO_FBK_HEAD1)) {
        return false;
    }
    crc_rx = servo_get_le32(&frame[SERVO_FBK_OFS_CRC]);
    crc_calc = servo_crc32(&frame[SERVO_FBK_CRC_OFS], SERVO_FBK_CRC_WORDS);
    if (crc_rx != crc_calc) {
        return false;
    }
    /* the response comes from the servo that was addressed */
    if ((frame[SERVO_FBK_OFS_MODE] & 0x0FU) != bus->txn_id) {
        return false;
    }
    return true;
}

static void servo_bus_update_status_ok(servo_bus_t *bus)
{
    const uint8_t *frame = bus->rx_frame;
    servo_status_t *st = &bus->status;
    int8_t temp = (int8_t)frame[SERVO_FBK_OFS_TEMP];
    uint8_t sensor = frame[SERVO_FBK_OFS_SENSOR];
    uint16_t temp_c = (temp < 0) ? 0U : (uint16_t)(uint8_t)temp;
    int16_t torque_raw = servo_get_le16(&frame[SERVO_FBK_OFS_TORQUE]);
    int16_t speed_raw = servo_get_le16(&frame[SERVO_FBK_OFS_SPEED]);
    uint16_t outpos_exflag = (uint16_t)servo_get_le16(&frame[SERVO_FBK_OFS_OUTPOS]);
    uint8_t timeout_flag = (uint8_t)((frame[SERVO_FBK_OFS_MODE] >> 7) & 0x01U);

    if (sensor > temp_c) {
        temp_c = sensor;
    }
    st->temp_c = (uint8_t)temp_c;
    st->voltage_mv = (uint16_t)((uint16_t)frame[SERVO_FBK_OFS_VOL] * 500U);
    st->torque_cnm = (int16_t)servo_mul_q16(torque_raw, SERVO_FBK_TORQUE_CNM_Q16);
    st->speed_raw = speed_raw;
    st->speed_rpm = (int16_t)servo_mul_q16(speed_raw, SERVO_FBK_SPEED_RPM_Q16);
    st->pos_raw = (int32_t)servo_get_le32(&frame[SERVO_FBK_OFS_POS]);
    st->out_pos = (uint16_t)(outpos_exflag & SERVO_OUTPOS_MASK);
    st->warn = (uint8_t)((outpos_exflag >> SERVO_EXFLAG_SHIFT) & 0x07U);
    st->pos_d10 = (int16_t)((((int32_t)st->out_pos * SERVO_OUTPOS_TO_D10_NUM) +
                             (1L << (SERVO_OUTPOS_TO_D10_SHIFT - 1U))) >>
                            SERVO_OUTPOS_TO_D10_SHIFT);
    st->m_error = servo_get_le32(&frame[SERVO_FBK_OFS_MERROR]);
    st->error = (uint8_t)(st->m_error & 0xFFU);
    st->current_ma = (int16_t)(((int32_t)torque_raw * SERVO_FBK_CURRENT_NUM) /
                               SERVO_FBK_CURRENT_DEN);
    st->timeout_flag = timeout_flag;
    st->enabled = (uint8_t)(((frame[SERVO_FBK_OFS_MODE] >> 4) & 0x07U) ==
                            SERVO_CTRL_MODE_FOC);
    st->online = 1U;
    st->ok_count++;
    st->last_ok_tick_ms = g_app_tick_ms;
    bus->consecutive_err = 0U;

    if (timeout_flag != 0U) {
        /* the servo reports that its timeout protection triggered: it has to
         * be cleared by sending a control frame with the timeout bit = 0 */
        bus->clear_timeout = 1U;
    }
}

static void servo_bus_update_status_error(servo_bus_t *bus)
{
    bus->status.err_count++;
    if (bus->consecutive_err < 0xFFU) {
        bus->consecutive_err++;
    }
    if (bus->consecutive_err >= SERVO_ONLINE_LOST_CNT) {
        bus->status.online = 0U;
    }
}

/* ==========================================================================
 * Scan state machine
 * ========================================================================== */
static void servo_scan_advance(servo_bus_t *bus)
{
    if (bus->scan_active == 0U) {
        return;
    }
    if (bus->scan_id > bus->scan_id_max) {
        bus->scan_active = 0U;
        bus->scan_done_pending = 1U;
    }
}

/* Get the next S288 addressable id of the scan range (0..14).  Ids 15..253
 * cannot be addressed by the servo protocol and are reported as absent. */
static bool servo_scan_take_id(servo_bus_t *bus, uint8_t *id)
{
    while (bus->scan_id <= bus->scan_id_max) {
        uint8_t candidate = bus->scan_id;
        bus->scan_id++;
        if (candidate <= SERVO_ID_MAX) {
            *id = candidate;
            return true;
        }
    }
    bus->scan_active = 0U;
    bus->scan_done_pending = 1U;
    return false;
}

/* ==========================================================================
 * Interrupt handlers
 * ========================================================================== */
SDK_DECLARE_EXT_ISR_M(BOARD_SERVO1_UART_IRQ, servo1_uart_isr)
SDK_DECLARE_EXT_ISR_M(BOARD_SERVO2_UART_IRQ, servo2_uart_isr)
SDK_DECLARE_EXT_ISR_M(BOARD_APP_DMA0_IRQ, servo_dma_isr)

static void servo_uart_isr(servo_bus_t *bus)
{
    UART_Type *uart = bus->hw->uart;

#if SERVO_HAS_RX_IDLE
    if (uart_is_rxline_idle(uart)) {
        uart_clear_rxline_idle_flag(uart);
        if (bus->state == SERVO_BUS_STATE_RX) {
            bus->rx_idle = 1U;
        }
    }
#endif
#if SERVO_HAS_TX_IDLE
    if (uart_is_txline_idle(uart)) {
        uart_clear_txline_idle_flag(uart);
        uart_disable_irq(uart, SERVO_UART_IRQ_TX_IDLE);
        if (bus->state == SERVO_BUS_STATE_TX) {
            if (servo_cycles_since(bus->tx_start_cycles) >= bus->min_tx_cycles) {
                /* end of the command frame: turn the bus around immediately */
                if (bus->expect_response != 0U) {
                    servo_bus_start_rx(bus);
                } else {
                    /* broadcast command: the servos do not answer */
                    servo_bus_finish_no_response(bus);
                }
            } else {
                /* the idle flag was set before the frame really started; the
                 * end of frame will set it again */
                uart_clear_txline_idle_flag(uart);
                uart_enable_irq(uart, SERVO_UART_IRQ_TX_IDLE);
            }
        }
    }
#endif
}

void servo1_uart_isr(void)
{
    servo_uart_isr(&s_servo_bus[0]);
}

void servo2_uart_isr(void)
{
    servo_uart_isr(&s_servo_bus[1]);
}

void servo_dma_isr(void)
{
    for (uint32_t i = 0; i < APP_CFG_SERVO_COUNT; i++) {
        servo_bus_t *bus = &s_servo_bus[i];
        uint32_t stat;

        stat = dma_check_transfer_status(bus->hw->dma, bus->hw->tx_dma_ch);
        if ((stat & (DMA_CHANNEL_STATUS_ERROR | DMA_CHANNEL_STATUS_ABORT)) != 0U) {
            bus->dma_error = 1U;
        }
        if ((stat & DMA_CHANNEL_STATUS_TC) != 0U) {
            bus->tx_done = 1U;
        }

        stat = dma_check_transfer_status(bus->hw->dma, bus->hw->rx_dma_ch);
        if ((stat & (DMA_CHANNEL_STATUS_ERROR | DMA_CHANNEL_STATUS_ABORT)) != 0U) {
            bus->dma_error = 1U;
        }
        if ((stat & DMA_CHANNEL_STATUS_TC) != 0U) {
            bus->rx_done = 1U;
        }
    }
}

/* ==========================================================================
 * Main loop state machine
 * ========================================================================== */
static void servo_schedule_next_poll(servo_bus_t *bus)
{
    uint32_t period_ms;

    if (bus->scan_active != 0U) {
        /* probe the scan ids back to back */
        period_ms = 1U;
    } else {
        int32_t hz = param_get(PARAM_SERVO_POLL_HZ);
        if (hz < 10) {
            hz = 10;
        } else if (hz > 1000) {
            hz = 1000;
        }
        period_ms = 1000U / (uint32_t)hz;
        if (period_ms == 0U) {
            period_ms = 1U;
        }
    }
    bus->next_poll_tick_ms = g_app_tick_ms + period_ms;
}

static void servo_bus_begin_transaction(servo_bus_t *bus)
{
    uint8_t id = SERVO_ID_BROADCAST;
    int32_t config_id;

    bus->tx_done = 0U;
    bus->rx_done = 0U;
    bus->rx_idle = 0U;
    bus->dma_error = 0U;

    if (bus->scan_active != 0U) {
        if (!servo_scan_take_id(bus, &id)) {
            servo_schedule_next_poll(bus);
            return;
        }
        bus->txn_is_scan = 1U;
    } else {
        config_id = param_get(PARAM_SERVO_ID(bus->index));
        if (config_id < 0) {
            config_id = 0;
        } else if (config_id > (int32_t)SERVO_ID_BROADCAST) {
            config_id = (int32_t)SERVO_ID_BROADCAST;
        }
        id = (uint8_t)config_id;
        bus->txn_is_scan = 0U;
    }

    bus->txn_id = id;
    bus->expect_response = (id <= SERVO_ID_MAX) ? 1U : 0U;
    bus->dir_tx_level = (uint8_t)(param_get(PARAM_SERVO_DIR_TX_LEVEL(bus->index)) != 0);
    servo_bus_build_command(bus, id);

    /* stop a possibly still running RX activity and release the old state */
    servo_bus_stop(bus);

    /* switch the SPDT analogue switch to the transmit path and give it the
     * setup time before the first start bit is sent */
    servo_set_dir(bus, true);
    bus->phase_start_cycles = servo_cycles_now();
    bus->state = SERVO_BUS_STATE_TX_SETUP;
}

static void servo_bus_finish_no_response(servo_bus_t *bus)
{
    /* broadcast command: the servos never answer, do not touch the counters */
    servo_bus_stop(bus);
    bus->state = SERVO_BUS_STATE_IDLE;
    servo_schedule_next_poll(bus);
}

static void servo_bus_finish_error(servo_bus_t *bus)
{
    uint32_t key = disable_global_irq(CSR_MSTATUS_MIE_MASK);

    servo_bus_stop(bus);
    restore_global_irq(key);

    servo_bus_update_status_error(bus);
    if (bus->txn_is_scan != 0U) {
        servo_scan_advance(bus);
    }
    bus->state = SERVO_BUS_STATE_IDLE;
    servo_schedule_next_poll(bus);
}

static void servo_bus_finish_rx(servo_bus_t *bus)
{
    uint32_t received;
    uint8_t dma_error;
    bool valid;
    uint32_t key;

    key = disable_global_irq(CSR_MSTATUS_MIE_MASK);
    dma_error = bus->dma_error;
    received = SERVO_FBK_SIZE -
               dma_get_remaining_transfer_size(bus->hw->dma, bus->hw->rx_dma_ch);
    servo_bus_stop(bus);
    restore_global_irq(key);

    valid = ((dma_error == 0U) && (received == SERVO_FBK_SIZE) &&
             servo_bus_feedback_valid(bus));
    if (valid) {
        if (bus->txn_is_scan != 0U) {
            uint8_t id = bus->txn_id;
            if (bus->scan_bitmap != NULL) {
                bus->scan_bitmap[id >> 3] |= (uint8_t)(1U << (id & 7U));
            }
            servo_scan_advance(bus);
        } else {
            servo_bus_update_status_ok(bus);
        }
    } else {
        servo_bus_update_status_error(bus);
        if (bus->txn_is_scan != 0U) {
            servo_scan_advance(bus);
        }
    }
    bus->state = SERVO_BUS_STATE_IDLE;
    servo_schedule_next_poll(bus);
}

static void servo_bus_tx_poll(servo_bus_t *bus)
{
    UART_Type *uart = bus->hw->uart;
    uint32_t elapsed = servo_cycles_since(bus->tx_start_cycles);
    bool tx_finished;

    if (elapsed > bus->tx_timeout_cycles) {
        servo_bus_finish_error(bus);
        return;
    }

    /* Fallback for the case that the UART TX idle interrupt is not available
     * or does not fire: the DMA transfer complete flag only tells us that all
     * bytes have been written to the FIFO, so wait for the transmitter to be
     * empty (last stop bit shifted out) before turning the bus around. */
    tx_finished = (bus->tx_done != 0U) && (elapsed >= bus->min_tx_cycles) &&
                  uart_check_status(uart, uart_stat_transmitter_empty);
    if (tx_finished) {
        uint32_t key = disable_global_irq(CSR_MSTATUS_MIE_MASK);
        if (bus->state == SERVO_BUS_STATE_TX) {
            if (bus->expect_response != 0U) {
                servo_bus_start_rx(bus);
            } else {
                servo_bus_finish_no_response(bus);
            }
        }
        restore_global_irq(key);
    }
}

static void servo_bus_rx_poll(servo_bus_t *bus)
{
    uint32_t received;

    if (bus->rx_done != 0U) {
        servo_bus_finish_rx(bus);
        return;
    }
#if SERVO_HAS_RX_IDLE
    if (bus->rx_idle != 0U) {
        received = SERVO_FBK_SIZE -
                   dma_get_remaining_transfer_size(bus->hw->dma, bus->hw->rx_dma_ch);
        if (received > 0U) {
            /* the line went idle in the middle of a response frame */
            servo_bus_finish_error(bus);
            return;
        }
    }
#endif
    if (servo_cycles_since(bus->rx_start_cycles) > SERVO_RX_TIMEOUT_CYCLES) {
        servo_bus_finish_error(bus);
    }
}

static void servo_bus_task(servo_bus_t *bus)
{
    if (bus->init_ok == 0U) {
        return;
    }
    switch (bus->state) {
    case SERVO_BUS_STATE_IDLE:
        if ((int32_t)(g_app_tick_ms - bus->next_poll_tick_ms) >= 0) {
            servo_bus_begin_transaction(bus);
        }
        break;
    case SERVO_BUS_STATE_TX_SETUP:
        if (servo_cycles_since(bus->phase_start_cycles) >= SERVO_DIR_SETUP_CYCLES) {
            if (servo_bus_start_tx_dma(bus) != status_success) {
                servo_bus_finish_error(bus);
            }
        }
        break;
    case SERVO_BUS_STATE_TX:
        servo_bus_tx_poll(bus);
        break;
    case SERVO_BUS_STATE_RX:
        servo_bus_rx_poll(bus);
        break;
    default:
        bus->state = SERVO_BUS_STATE_IDLE;
        break;
    }
}

/* ==========================================================================
 * Public API
 * ========================================================================== */
void servo_init(void)
{
    uint32_t baud;

    memset(s_servo_bus, 0, sizeof(s_servo_bus));

    for (uint32_t i = 0; i < APP_CFG_SERVO_COUNT; i++) {
        servo_bus_t *bus = &s_servo_bus[i];

        bus->hw = &s_servo_hw[i];
        bus->index = (uint8_t)i;
        bus->state = SERVO_BUS_STATE_IDLE;
        bus->target_d10 = (int16_t)APP_CFG_SERVO_ANGLE_DEFAULT_D10;
        bus->dir_tx_level = BOARD_SERVO_DIR_TX_LEVEL_DEFAULT;
        bus->tx_frame = s_servo_tx_frame[i];
        bus->rx_frame = s_servo_rx_frame[i];
        bus->next_poll_tick_ms = g_app_tick_ms;
        bus->consecutive_err = SERVO_ONLINE_LOST_CNT;

        servo_init_pinmux(bus->hw);
        servo_init_dir_gpio(bus);
        servo_set_dir(bus, false);
        servo_init_dma(bus->hw);
    }

    s_servo_initialized = 1U;

    baud = (uint32_t)param_get(PARAM_SERVO_BAUD);
    if ((baud < 9600U) || (baud > 12000000U)) {
        baud = APP_CFG_SERVO_DEFAULT_BAUD;
    }
    servo_set_baudrate(baud);

    intc_m_enable_irq_with_priority(BOARD_SERVO1_UART_IRQ, SERVO_UART_IRQ_PRIORITY);
    intc_m_enable_irq_with_priority(BOARD_SERVO2_UART_IRQ, SERVO_UART_IRQ_PRIORITY);
    intc_m_enable_irq_with_priority(BOARD_APP_DMA0_IRQ, SERVO_DMA_IRQ_PRIORITY);

    LOGM_I(LOG_MOD_SERVO, "S288: UART2/UART3 @ %u baud, id1=%d id2=%d",
           (unsigned)baud,
           (int)param_get(PARAM_SERVO_ID(0)),
           (int)param_get(PARAM_SERVO_ID(1)));
    if ((s_servo_bus[0].init_ok == 0U) || (s_servo_bus[1].init_ok == 0U)) {
        LOGM_E(LOG_MOD_SERVO, "S288: UART init failed, check the UART clock");
    }
}

void servo_set_target_d10(uint8_t ch, int16_t angle_d10)
{
    servo_bus_t *bus;
    int32_t gain;
    int32_t offset;
    int32_t min_d10;
    int32_t max_d10;
    int32_t applied;

    if (ch >= APP_CFG_SERVO_COUNT) {
        return;
    }
    bus = &s_servo_bus[ch];
    gain = param_get(PARAM_SERVO_ANGLE_GAIN(ch));
    offset = param_get(PARAM_SERVO_ANGLE_OFS(ch));
    min_d10 = param_get(PARAM_SERVO_ANGLE_MIN(ch));
    max_d10 = param_get(PARAM_SERVO_ANGLE_MAX(ch));

    applied = (((int32_t)angle_d10 * gain) / 100) + offset;
    if (applied < min_d10) {
        applied = min_d10;
    }
    if (applied > max_d10) {
        applied = max_d10;
    }
    if (applied > INT16_MAX) {
        applied = INT16_MAX;
    } else if (applied < INT16_MIN) {
        applied = INT16_MIN;
    }

    if ((int16_t)applied != bus->target_d10) {
        bus->target_d10 = (int16_t)applied;
        /* send the new setpoint with the next servo_task() call */
        if (bus->state == SERVO_BUS_STATE_IDLE) {
            bus->next_poll_tick_ms = g_app_tick_ms;
        }
    }
}

void servo_set_enable(uint8_t ch, uint8_t enable)
{
    servo_bus_t *bus;

    if (ch >= APP_CFG_SERVO_COUNT) {
        return;
    }
    bus = &s_servo_bus[ch];
    enable = (enable != 0U) ? 1U : 0U;
    if (bus->enable != enable) {
        bus->enable = enable;
        if (bus->state == SERVO_BUS_STATE_IDLE) {
            bus->next_poll_tick_ms = g_app_tick_ms;
        }
    }
}

void servo_clear_fault(uint8_t ch)
{
    if (ch >= APP_CFG_SERVO_COUNT) {
        return;
    }
    /* the S288 clears its timeout protection when it receives a control frame
     * with the timeout enable bit = 0 (manual section 九, p.8) */
    s_servo_bus[ch].clear_timeout = 1U;
    if (s_servo_bus[ch].state == SERVO_BUS_STATE_IDLE) {
        s_servo_bus[ch].next_poll_tick_ms = g_app_tick_ms;
    }
}

void servo_get_status(uint8_t ch, servo_status_t *status)
{
    if (status == NULL) {
        return;
    }
    if (ch >= APP_CFG_SERVO_COUNT) {
        memset(status, 0, sizeof(*status));
        return;
    }
    *status = s_servo_bus[ch].status;
}

void servo_task(void)
{
    if (s_servo_initialized == 0U) {
        return;
    }
    for (uint32_t i = 0; i < APP_CFG_SERVO_COUNT; i++) {
        servo_bus_task(&s_servo_bus[i]);
    }
}

void servo_set_baudrate(uint32_t baud)
{
    if (baud < 9600U) {
        baud = APP_CFG_SERVO_DEFAULT_BAUD;
    }
    if (s_servo_initialized == 0U) {
        s_servo_baud = baud;
        return;
    }
    if ((baud == s_servo_baud) &&
        (s_servo_bus[0].init_ok != 0U) && (s_servo_bus[1].init_ok != 0U)) {
        return;
    }
    s_servo_baud = baud;
    for (uint32_t i = 0; i < APP_CFG_SERVO_COUNT; i++) {
        servo_bus_t *bus = &s_servo_bus[i];
        uint32_t key = disable_global_irq(CSR_MSTATUS_MIE_MASK);
        hpm_stat_t stat;

        servo_bus_stop(bus);
        restore_global_irq(key);
        bus->state = SERVO_BUS_STATE_IDLE;
        stat = servo_uart_init(bus, baud);
        bus->init_ok = (stat == status_success) ? 1U : 0U;
        bus->next_poll_tick_ms = g_app_tick_ms;
    }
}

int servo_write_register(uint8_t ch, uint8_t id, uint16_t reg,
                         const uint8_t *data, uint8_t len)
{
    (void)ch;
    (void)id;
    (void)reg;
    (void)data;
    (void)len;
    /* the S288/J288 protocol (manual section 九, pp.7-10) has no register
     * read/write command; ids are configured with the Unitree PC tool */
    return SERVO_ERR_UNSUPPORTED;
}

int servo_read_register(uint8_t ch, uint8_t id, uint16_t reg,
                        uint8_t *data, uint8_t len)
{
    (void)ch;
    (void)id;
    (void)reg;
    (void)data;
    (void)len;
    return SERVO_ERR_UNSUPPORTED;
}

int servo_scan_bus(uint8_t ch, uint8_t id_min, uint8_t id_max,
                   uint8_t *id_bitmap)
{
    servo_bus_t *bus;

    if ((ch >= APP_CFG_SERVO_COUNT) || (id_bitmap == NULL) || (id_min > id_max)) {
        return SERVO_ERR_INVALID_ARG;
    }
    bus = &s_servo_bus[ch];
    if (bus->init_ok == 0U) {
        return SERVO_ERR_IO;
    }
    if (bus->scan_done_pending != 0U) {
        bus->scan_done_pending = 0U;
        return SERVO_SCAN_DONE;
    }
    if ((bus->scan_active != 0U) &&
        (bus->scan_id_min == id_min) &&
        (bus->scan_id_max == id_max) &&
        (bus->scan_bitmap == id_bitmap)) {
        return SERVO_SCAN_RUNNING;
    }

    /* start (or restart) the scan */
    memset(id_bitmap, 0, SERVO_BITMAP_BYTES);
    bus->scan_bitmap = id_bitmap;
    bus->scan_id_min = id_min;
    bus->scan_id_max = id_max;
    bus->scan_id = id_min;
    bus->scan_active = 1U;
    bus->scan_done_pending = 0U;
    if (id_min > SERVO_ID_MAX) {
        /* no S288 addressable id in the requested range */
        bus->scan_active = 0U;
        bus->scan_done_pending = 1U;
    } else {
        /* a normal poll transaction may still be running; it is not part of
         * the scan, servo_task() starts the first scan probe afterwards */
        if (bus->state == SERVO_BUS_STATE_IDLE) {
            bus->next_poll_tick_ms = g_app_tick_ms;
        }
    }
    return SERVO_SCAN_RUNNING;
}
