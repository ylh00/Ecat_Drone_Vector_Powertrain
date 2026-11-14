/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Board support header for ecat_drone_5e3y
 *
 * Hardware summary (see README_zh.rst for the complete pin table):
 *   SoC        : HPM5E3Y (EtherCAT slave MCU, internal XPI NOR flash + internal MII PHY)
 *   ESC        : ESC0, 2 x MII ports driven by the two on-chip MII PHYs
 *   Debug log  : UART0 (PA00/PA01), 115200 8N1
 *   Servo #1   : UART2 (PC08/PC09) + direction switch PC16   (S288 bus servo)
 *   Servo #2   : UART3 (PC14/PC15) + direction switch PD00   (S288 bus servo)
 *   ESC #1     : bidirectional DShot on PWM0.P2 (PF02)
 *   ESC #2     : bidirectional DShot on PWM0.P3 (PF03)
 *   Status LED : RGB, PWM1.P4 (green, PF12), PWM1.P5 (red, PF13), PWM1.P6 (blue, PF14)
 *   ADC0 IN1   : system current            ADC0 IN8  : system voltage
 *   ADC1 IN3   : ESC1 phase/DC current     ADC1 IN13 : ESC2 phase/DC current
 *   USB0       : CDC-ACM virtual COM port for the PC tuning tool
 */

#ifndef _HPM_BOARD_H
#define _HPM_BOARD_H
#include <stdio.h>
#include "hpm_common.h"
#include "hpm_soc.h"
#include "hpm_soc_feature.h"
#include "hpm_clock_drv.h"
#include "pinmux.h"
#include "hpm_trgm_drv.h"
#include "hpm_gptmr_drv.h"
#if !defined(CONFIG_NDEBUG_CONSOLE) || !CONFIG_NDEBUG_CONSOLE
#include "hpm_debug_console.h"
#endif

#define BOARD_NAME          "ecat_drone_5e3y"
#define BOARD_UF2_SIGNATURE (0x0A4D5048UL)
#define BOARD_DFU_SIGNATURE (0x48504D21UL)
#define BOARD_CPU_FREQ      (480000000UL)

#define SEC_CORE_IMG_START CORE1_ILM_LOCAL_BASE

#ifndef BOARD_RUNNING_CORE
#define BOARD_RUNNING_CORE HPM_CORE0
#endif

/* ==========================================================================
 * Debug console (UART0)
 * ========================================================================== */
#if !defined(CONFIG_NDEBUG_CONSOLE) || !CONFIG_NDEBUG_CONSOLE
#ifndef BOARD_CONSOLE_TYPE
#define BOARD_CONSOLE_TYPE CONSOLE_TYPE_UART
#endif

#if BOARD_CONSOLE_TYPE == CONSOLE_TYPE_UART
#ifndef BOARD_CONSOLE_UART_BASE
#define BOARD_CONSOLE_UART_BASE       HPM_UART0
#define BOARD_CONSOLE_UART_CLK_NAME   clock_uart0
#define BOARD_CONSOLE_UART_IRQ        IRQn_UART0
#define BOARD_CONSOLE_UART_TX_DMA_REQ HPM_DMA_SRC_UART0_TX
#define BOARD_CONSOLE_UART_RX_DMA_REQ HPM_DMA_SRC_UART0_RX
#endif
#define BOARD_CONSOLE_UART_BAUDRATE (115200UL)
#endif
#endif

/* ==========================================================================
 * UART section
 * ========================================================================== */
#define BOARD_APP_UART_BASE       HPM_UART0
#define BOARD_APP_UART_IRQ        IRQn_UART0
#define BOARD_APP_UART_BAUDRATE   (115200UL)
#define BOARD_APP_UART_CLK_NAME   clock_uart0
#define BOARD_APP_UART_RX_DMA_REQ HPM_DMA_SRC_UART0_RX
#define BOARD_APP_UART_TX_DMA_REQ HPM_DMA_SRC_UART0_TX

/* ==========================================================================
 * S288 bus servo section
 *   UART2/UART3 are connected to a single-pole double-throw (SPDT) analogue
 *   switch that selects either the TX or the RX pin of the transceiver.
 *   The switch is controlled by a GPIO; the active level for "TX mode" is a
 *   runtime parameter (default: high = TX) and can be changed by the PC tool.
 * ========================================================================== */
#define BOARD_SERVO1_UART_BASE       HPM_UART2
#define BOARD_SERVO1_UART_IRQ        IRQn_UART2
#define BOARD_SERVO1_UART_CLK_NAME   clock_uart2
#define BOARD_SERVO1_UART_RX_DMA_REQ HPM_DMA_SRC_UART2_RX
#define BOARD_SERVO1_UART_TX_DMA_REQ HPM_DMA_SRC_UART2_TX

#define BOARD_SERVO2_UART_BASE       HPM_UART3
#define BOARD_SERVO2_UART_IRQ        IRQn_UART3
#define BOARD_SERVO2_UART_CLK_NAME   clock_uart3
#define BOARD_SERVO2_UART_RX_DMA_REQ HPM_DMA_SRC_UART3_RX
#define BOARD_SERVO2_UART_TX_DMA_REQ HPM_DMA_SRC_UART3_TX

/* UART2 direction switch: PC16 */
#define BOARD_SERVO1_DIR_GPIO       HPM_GPIO0
#define BOARD_SERVO1_DIR_GPIO_INDEX GPIO_DO_GPIOC
#define BOARD_SERVO1_DIR_GPIO_PIN   (16U)
/* UART3 direction switch: PD00 */
#define BOARD_SERVO2_DIR_GPIO       HPM_GPIO0
#define BOARD_SERVO2_DIR_GPIO_INDEX GPIO_DO_GPIOD
#define BOARD_SERVO2_DIR_GPIO_PIN   (0U)

/* Default switch polarity: 1 = TX path selected (parameter can override it) */
#define BOARD_SERVO_DIR_TX_LEVEL_DEFAULT (1U)
/* Time to wait after switching direction before the first byte / before sampling */
#define BOARD_SERVO_DIR_SETUP_US        (2U)

/* ==========================================================================
 * Bidirectional DShot section (PWM0 P2/P3 + GPTMR1 capture via TRGM)
 *
 * PWMV2 comparator/counter mapping (UM chapter PWM):
 *   CMP[0..3]  -> counter0 (PWM0/PWM1 outputs)
 *   CMP[4..7]  -> counter1 (PWM2/PWM3 outputs)
 *   CMP[8..11] -> counter2 (PWM4/PWM5 outputs)
 *   CMP[12..15]-> counter3 (PWM6/PWM7 outputs)
 *   CMP[16..23]-> free, can select any counter
 * Output channel n uses CMP[2n] / CMP[2n+1], so PWM0.P2/P3 are driven by
 * counter1 with CMP4..CMP7.
 * ========================================================================== */
#define BOARD_DSHOT_PWM              HPM_PWM0
#define BOARD_DSHOT_PWM_CLOCK_NAME   clock_pwm0
#define BOARD_DSHOT_PWM_IRQ          IRQn_PWM0
#define BOARD_DSHOT_PWM_COUNTER      pwm_counter_1

#define BOARD_DSHOT_ESC1_PWM_CHANNEL (2U)   /* PWM0.P2 -> PF02 */
#define BOARD_DSHOT_ESC2_PWM_CHANNEL (3U)   /* PWM0.P3 -> PF03 */
#define BOARD_DSHOT_ESC1_CMP_INDEX   (4U)   /* CMP[4]/CMP[5] on counter1 */
#define BOARD_DSHOT_ESC2_CMP_INDEX   (6U)   /* CMP[6]/CMP[7] on counter1 */

#define BOARD_DSHOT_ESC1_PAD         IOC_PAD_PF02
#define BOARD_DSHOT_ESC2_PAD         IOC_PAD_PF03
#define BOARD_DSHOT_ESC1_PAD_FUNC    IOC_PF02_FUNC_CTL_PWM0_P_2
#define BOARD_DSHOT_ESC2_PAD_FUNC    IOC_PF03_FUNC_CTL_PWM0_P_3
/* same pads switched to TRGM input function while the ESC telemetry is received */
#define BOARD_DSHOT_ESC1_PAD_IN_FUNC IOC_PF02_FUNC_CTL_TRGM_P_02
#define BOARD_DSHOT_ESC2_PAD_IN_FUNC IOC_PF03_FUNC_CTL_TRGM_P_03

#define BOARD_DSHOT_GPIO_PORT_INDEX  GPIO_DI_GPIOF
#define BOARD_DSHOT_ESC1_GPIO_PIN    (2U)
#define BOARD_DSHOT_ESC2_GPIO_PIN    (3U)

#define BOARD_DSHOT_TRGM             HPM_TRGM0
#define BOARD_DSHOT_TRGM_IN_ESC1     HPM_TRGM0_INPUT_SRC_TRGM0_P02
#define BOARD_DSHOT_TRGM_IN_ESC2     HPM_TRGM0_INPUT_SRC_TRGM0_P03
#define BOARD_DSHOT_TRGM_OUT_ESC1    HPM_TRGM0_OUTPUT_SRC_GPTMR1_CAPT_2
#define BOARD_DSHOT_TRGM_OUT_ESC2    HPM_TRGM0_OUTPUT_SRC_GPTMR1_CAPT_3

#define BOARD_DSHOT_CAP_GPTMR            HPM_GPTMR1
#define BOARD_DSHOT_CAP_GPTMR_CLK_NAME   clock_gptmr1
#define BOARD_DSHOT_CAP_GPTMR_IRQ        IRQn_GPTMR1
#define BOARD_DSHOT_CAP_ESC1_CHANNEL     (2U)
#define BOARD_DSHOT_CAP_ESC2_CHANNEL     (3U)

/* DShot command rate: DSHOT600 is the default (bit period 1.667us) */
#define BOARD_DSHOT_RATE_DEFAULT         (600U)
#define BOARD_DSHOT_MOTOR_POLE_PAIRS_DEFAULT (7U)

/* ==========================================================================
 * RGB status LED section (PWM1 P4/P5/P6, common-anode / active-low per LED)
 *   PWM1.P4 (green) -> CMP[8]/CMP[9]   on counter2
 *   PWM1.P5 (red)   -> CMP[10]/CMP[11] on counter2
 *   PWM1.P6 (blue)  -> CMP[12]/CMP[13] on counter3
 * counter2 and counter3 must run at the same frequency and be started
 * synchronously (see pwmv2_enable_multi_counter_sync/pwmv2_start_pwm_output_sync).
 * ========================================================================== */
#define BOARD_RGB_PWM                HPM_PWM1
#define BOARD_RGB_PWM_CLOCK_NAME     clock_pwm1
#define BOARD_RGB_LED_GREEN_CHANNEL  (4U)   /* PWM1.P4 -> PF12 */
#define BOARD_RGB_LED_RED_CHANNEL    (5U)   /* PWM1.P5 -> PF13 */
#define BOARD_RGB_LED_BLUE_CHANNEL   (6U)   /* PWM1.P6 -> PF14 */
#define BOARD_RGB_LED_GREEN_CMP_INDEX (8U)
#define BOARD_RGB_LED_RED_CMP_INDEX   (10U)
#define BOARD_RGB_LED_BLUE_CMP_INDEX  (12U)
#define BOARD_RGB_LED_COUNTER_GREEN_RED  pwm_counter_2
#define BOARD_RGB_LED_COUNTER_BLUE       pwm_counter_3
#define BOARD_RGB_LED_FREQ_HZ        (20000U)
/* 1 = LED is switched on by driving the pin low (common anode) */
#define BOARD_RGB_LED_ACTIVE_LOW     (1U)

/* ==========================================================================
 * ADC section (ADC16)
 *   ADC0 IN1  : system current
 *   ADC0 IN8  : system voltage
 *   ADC1 IN3  : ESC1 current sense
 *   ADC1 IN13 : ESC2 current sense
 * ========================================================================== */
#define BOARD_ADC0_BASE          HPM_ADC0
#define BOARD_ADC0_CLK_NAME      (clock_adc0)
#define BOARD_ADC0_SYS_CURRENT_CH  (1U)
#define BOARD_ADC0_SYS_VOLTAGE_CH  (8U)

#define BOARD_ADC1_BASE          HPM_ADC1
#define BOARD_ADC1_CLK_NAME      (clock_adc1)
#define BOARD_ADC1_ESC1_CURRENT_CH (3U)
#define BOARD_ADC1_ESC2_CURRENT_CH (13U)

#define BOARD_ADC_RES_BITS       (16U)

/* ==========================================================================
 * USB section (USB0, device only, CDC-ACM virtual COM port)
 * ========================================================================== */
#define BOARD_USB_BASE           HPM_USB0
#define BOARD_USB_IRQ            IRQn_USB0
#define BOARD_USB_CLK_NAME       clock_usb0
#define BOARD_USB_ID_GPIO_CTRL   HPM_GPIO0
#define BOARD_USB_ID_GPIO_INDEX  GPIO_DI_GPIOF
#define BOARD_USB_ID_GPIO_PIN    (22U)

/* ==========================================================================
 * DMA section
 * ========================================================================== */
#define BOARD_APP_DMA1      HPM_XDMA
#define BOARD_APP_DMA0      HPM_HDMA
#define BOARD_APP_DMA1_IRQ  IRQn_XDMA
#define BOARD_APP_DMA0_IRQ  IRQn_HDMA
#define BOARD_APP_DMAMUX    HPM_DMAMUX

/* ==========================================================================
 * Timer section
 *   GPTMR0 channel0 : board_delay_us/ms
 *   GPTMR0 channel1 : 1ms software timer (board_timer_create)
 *   GPTMR1 channel2 : DShot ESC1 telemetry capture
 *   GPTMR1 channel3 : DShot ESC2 telemetry capture
 * ========================================================================== */
#define BOARD_DELAY_TIMER          (HPM_GPTMR0)
#define BOARD_DELAY_TIMER_CH       0
#define BOARD_DELAY_TIMER_CLK_NAME (clock_gptmr0)

#define BOARD_CALLBACK_TIMER          (HPM_GPTMR0)
#define BOARD_CALLBACK_TIMER_CH       1
#define BOARD_CALLBACK_TIMER_IRQ      IRQn_GPTMR0
#define BOARD_CALLBACK_TIMER_CLK_NAME (clock_gptmr0)

/* ==========================================================================
 * GPIO / fast GPIO section
 * ========================================================================== */
#define BOARD_APP_GPIO_CTRL        HPM_GPIO0
#define BOARD_APP_GPIO_INDEX       GPIO_DI_GPIOC
#define BOARD_APP_GPIO_PIN         21
#define BOARD_APP_GPIO_IRQ         IRQn_GPIO0_C
#define BOARD_BUTTON_PRESSED_VALUE 0

#define BOARD_APP_GPIOM_BASE            HPM_GPIOM
#define BOARD_APP_GPIOM_USING_CTRL      HPM_FGPIO
#define BOARD_APP_GPIOM_USING_CTRL_NAME gpiom_core0_fast

/* ==========================================================================
 * Nor flash section
 * ========================================================================== */
#define BOARD_FLASH_BASE_ADDRESS (0x80000000UL)
#define BOARD_FLASH_SIZE         (1 * SIZE_1MB)
/* the EtherCAT EEPROM is emulated in the upper half of the internal flash */
#define BOARD_ECAT_FLASH_EMULATE_EEPROM_ADDR (0x80000)

#define BOARD_APP_XPI_NOR_XPI_BASE     (HPM_XPI0)
#define BOARD_APP_XPI_NOR_CFG_OPT_HDR  (0xfcf90002U)
#define BOARD_APP_XPI_NOR_CFG_OPT_OPT0 (0x00000006U)
#define BOARD_APP_XPI_NOR_CFG_OPT_OPT1 (0x00001000U)

/* ==========================================================================
 * EtherCAT section
 * ========================================================================== */
#define BOARD_ECAT_ESC              HPM_ESC
#define BOARD_ECAT_SUPPORT_PORT1    (1)   /* RJ45 #1: internal MII PHY0 */
#define BOARD_ECAT_SUPPORT_PORT2    (0)   /* RJ45 #2: internal MII PHY1 */
#define BOARD_ECAT_SUPPORT_RUN_ERROR_LED (0) /* RUN/ERR are shown on the RGB LED */

/* NMII link status inputs, value must match the "y" of IOC_PAxx_FUNC_CTL_ESC0_CTR_y.
 * Verified with "HPM5E00 片内 MII PHY 使用指南" (v1.1) chapter 3:
 *   PA25 = PHY1 LED1 -> ESC0_CTR_0, PA28 = PHY0 LED1 -> ESC0_CTR_1
 *   -> LINK0 (port0/PHY0) uses CTR_1, LINK1 (port1/PHY1) uses CTR_0. */
#define BOARD_ECAT_NMII_LINK0_CTRL_INDEX (1)
#define BOARD_ECAT_NMII_LINK1_CTRL_INDEX (0)

/* The on-chip MII PHY link status is active low on this board; adjust after
 * bring-up if the PHY datasheet / measurement says otherwise. */
#define BOARD_ECAT_PORT0_LINK_INVERT false
#define BOARD_ECAT_PORT1_LINK_INVERT false
#define BOARD_ECAT_PORT2_LINK_INVERT false

/* Internal MII PHY resets are controlled with dedicated GPIOs (PV12 / PW12) */
#define BOARD_ECAT_PHY0_RESET_GPIO            HPM_GPIO0
#define BOARD_ECAT_PHY0_RESET_GPIO_PORT_INDEX GPIO_DO_GPIOV
#define BOARD_ECAT_PHY0_RESET_PIN_INDEX       (12U)

#define BOARD_ECAT_PHY1_RESET_GPIO            HPM_GPIO0
#define BOARD_ECAT_PHY1_RESET_GPIO_PORT_INDEX GPIO_DO_GPIOW
#define BOARD_ECAT_PHY1_RESET_PIN_INDEX       (12U)

/* unused on this board (only 2 ports) */
#define BOARD_ECAT_PHY2_RESET_GPIO            HPM_GPIO0
#define BOARD_ECAT_PHY2_RESET_GPIO_PORT_INDEX GPIO_DO_GPIOW
#define BOARD_ECAT_PHY2_RESET_PIN_INDEX       (12U)

#define BOARD_ECAT_PHY_RESET_LEVEL (0)
#define BOARD_ECAT_PHY_RESET_HOLD_TIME_MS (10)
#define BOARD_ECAT_PHY_RESET_WAIT_TIME_MS (30)

/* Internal MII PHY address strapping.  The SDK ecat_io example uses the
 * HPM5E00EVK addresses (PHY ADDR_OFFSET + port index).  The guide
 * "HPM5E00 片内 MII PHY 使用指南" v1.1 states that the example modifications are
 * based on these same addresses, so keep the EVK convention:
 *   MDIO address of port0 PHY = BOARD_ECAT_PHY_ADDR_OFFSET + BOARD_ECAT_PORT0_PHY_ADDR = 1
 *   MDIO address of port1 PHY = BOARD_ECAT_PHY_ADDR_OFFSET + BOARD_ECAT_PORT1_PHY_ADDR = 2
 * Verify with an MDIO scan during bring-up. */
#define BOARD_ECAT_PHY_ADDR_OFFSET (1U)
#define BOARD_ECAT_PORT0_PHY_ADDR  (0U)
#define BOARD_ECAT_PORT1_PHY_ADDR  (1U)
#define BOARD_ECAT_PORT2_PHY_ADDR  (2U)

#ifndef BOARD_SHOW_CLOCK
#define BOARD_SHOW_CLOCK 1
#endif
#ifndef BOARD_SHOW_BANNER
#define BOARD_SHOW_BANNER 1
#endif

#if defined(__cplusplus)
extern "C" {
#endif /* __cplusplus */

typedef void (*board_timer_cb)(void);

/* basic board setup */
void board_init(void);
void board_init_console(void);
void board_print_clock_freq(void);
void board_print_banner(void);
void board_init_uart(UART_Type *ptr);
void board_init_clock(void);
uint32_t board_init_uart_clock(UART_Type *ptr);
uint32_t board_init_adc_clock(void *ptr, bool clk_src_bus);
uint32_t board_init_pwm_clock(PWMV2_Type *ptr);

/* pin initialization (implemented in board.c, uses pinmux.c tables) */
void init_uart_pins(UART_Type *ptr);
void init_pwm_pins(PWMV2_Type *ptr);
void init_servo_dir_pins(void);
void init_rgb_led_pins(void);
void init_usb_pins(USB_Type *ptr);
void init_adc_pins(void);

/* peripheral initialization helpers */
void board_init_usb(USB_Type *ptr);
void board_init_adc16_pins(void);
void board_init_pwm_pins(PWMV2_Type *ptr);
void board_init_rgb_led_pins(void);
void board_init_servo_pins(void);
void board_init_ethercat(ESC_Type *ptr);
void board_ecat_phy_reset(ESC_Type *ptr);

/* LEDs */
void board_init_led_pins(void);
void board_led_write(uint8_t state);
void board_led_toggle(void);

/* misc */
void board_init_pmp(void);
void board_delay_us(uint32_t us);
void board_delay_ms(uint32_t ms);
void board_timer_create(uint32_t ms, board_timer_cb cb);
void board_ungate_mchtmr_at_lp_mode(void);
uint32_t board_init_gptmr_clock(GPTMR_Type *ptr);

#if defined(__cplusplus)
}
#endif
#endif /* _HPM_BOARD_H */
