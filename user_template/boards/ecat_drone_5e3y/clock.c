/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 *
 * Board clock tree for ecat_drone_5e3y (HPM5E3Y, built with the HPM5E31 SoC
 * support files of HPM SDK v1.13.0)
 *
 * Derived from hpm_sdk/boards/hpm5e00evk/clock.c.  The clocks of peripherals
 * which are not used on this board (ENET reference clock, QEI/QEO, PLB, ACMP,
 * OWR, PPI, EUI, extra UART/I2C/SPI/CAN instances, ...) have been removed.
 *
 * UART2 / UART3 (S288/J288 bus servos):
 *   The servos require exactly 6.0 Mbps.  Both UART instances are clocked by
 *   PLL0 clk0 (480 MHz, set up by init_board_clock_source()) divided by 10,
 *   i.e. 48 MHz.  The UART baudrate generator then produces 6.0 Mbps exactly
 *   with OSR = 8 and DIV = 1:  48000000 / (8 * 1) = 6000000 bps
 *   (see uart_calculate_baudrate() in hpm_sdk/drivers/src/hpm_uart_drv.c).
 *   Other standard baudrates (115200, 921600, ...) are also within tolerance
 *   from 48 MHz.
 *
 * GPTMR:
 *   GPTMR0 is used by board.c (ch0: board_delay_us/ms, ch1: board_timer_create)
 *   and GPTMR1 is used by the bidirectional DShot capture.  The GPTMR clock
 *   sources are left at the SDK clock preset values; board.c converts time to
 *   ticks with clock_get_frequency(), so the delay code is preset independent.
 */

#include "clock.h"
#include "hpm_soc.h"
#include "hpm_clock_drv.h"
#include "hpm_pllctlv2_drv.h"


void init_board_clock(void)
{
    clock_add_to_group(clock_cpu0, 0);

    clock_add_to_group(clock_mchtmr0, 0);

    clock_add_to_group(clock_ahb0, 0);

    clock_add_to_group(clock_axif, 0);

    clock_add_to_group(clock_axis, 0);

    clock_add_to_group(clock_axic, 0);

    clock_add_to_group(clock_rom0, 0);

    clock_add_to_group(clock_xpi0, 0);

    clock_add_to_group(clock_lmm0, 0);

    clock_add_to_group(clock_ram0, 0);

    clock_add_to_group(clock_hdma, 0);

    clock_add_to_group(clock_xdma, 0);

    clock_add_to_group(clock_gpio, 0);

    clock_add_to_group(clock_ptpc, 0);

    clock_add_to_group(clock_pwm0, 0);

    clock_add_to_group(clock_pwm1, 0);
}

void init_board_clock_source(void)
{
    /* Generate pll0 code */
    pllctlv2_init_pll_with_freq(HPM_PLLCTLV2, PLLCTLV2_PLL_PLL0, 960000000);

    /* Generate clk0_pll0 code */
    pllctlv2_set_postdiv(HPM_PLLCTLV2, PLLCTLV2_PLL_PLL0, pllctlv2_clk0, pllctlv2_div_2p0);

    /* Generate clk1_pll0 code */
    pllctlv2_set_postdiv(HPM_PLLCTLV2, PLLCTLV2_PLL_PLL0, pllctlv2_clk1, pllctlv2_div_2p4);

    /* Generate clk_top_cpu0 code */
    clock_set_source_divider(clock_cpu0, clk_src_pll0_clk0, 1);

    /* Generate clk_top_mct0 code */
    clock_set_source_divider(clock_mchtmr0, clk_src_osc24m, 1);
}

void init_uart0_clock(void)
{
    /* UART0 debug console (@115200 by default) */
    clock_add_to_group(clock_uart0, 0);
}

void init_uart2_clock(void)
{
    /* 48 MHz = PLL0 clk0 (480 MHz) / 10.  Needed for the exact 6.0 Mbps of the
     * S288/J288 bus servo: 48 MHz / (OSR 8 * DIV 1) = 6.0 Mbps.
     * NOTE: if this divider is changed, the S288 bus servo baudrate is lost. */
    clock_set_source_divider(clock_uart2, clk_src_pll0_clk0, 10);
    clock_add_to_group(clock_uart2, 0);
}

void init_uart3_clock(void)
{
    /* 48 MHz = PLL0 clk0 (480 MHz) / 10, see init_uart2_clock() */
    clock_set_source_divider(clock_uart3, clk_src_pll0_clk0, 10);
    clock_add_to_group(clock_uart3, 0);
}

void init_i2c3_clock(void)
{
    clock_add_to_group(clock_i2c3, 0);
}

void init_spi1_clock(void)
{
    clock_add_to_group(clock_spi1, 0);
}

void init_adc0_bus_clock(void)
{
    /* Generate clk_top_adc0 code */
    clock_set_adc_source(clock_adc0, clk_adc_src_ahb0);
    clock_set_source_divider(clock_ahb0, clk_src_pll1_clk0, 2);

    clock_add_to_group(clock_adc0, 0);
}

void init_adc0_analog_clock(void)
{
    /* Generate clk_top_adc0 code */
    clock_set_adc_source(clock_adc0, clk_adc_src_ana0);
    clock_set_source_divider(clock_ana0, clk_src_pll1_clk0, 4);

    clock_add_to_group(clock_adc0, 0);
}

void init_adc1_bus_clock(void)
{
    /* Generate clk_top_adc1 code */
    clock_set_adc_source(clock_adc1, clk_adc_src_ahb0);
    clock_set_source_divider(clock_ahb0, clk_src_pll1_clk0, 2);

    clock_add_to_group(clock_adc1, 0);
}

void init_adc1_analog_clock(void)
{
    /* Generate clk_top_adc1 code */
    clock_set_adc_source(clock_adc1, clk_adc_src_ana1);
    clock_set_source_divider(clock_ana1, clk_src_pll1_clk0, 4);

    clock_add_to_group(clock_adc1, 0);
}

void init_can0_clock(void)
{
    /* Generate clk_top_can0 code */
    clock_set_source_divider(clock_can0, clk_src_pll1_clk0, 5);

    clock_add_to_group(clock_can0, 0);
}

void init_can1_clock(void)
{
    /* Generate clk_top_can1 code */
    clock_set_source_divider(clock_can1, clk_src_pll1_clk0, 5);

    clock_add_to_group(clock_can1, 0);
}

void init_can2_clock(void)
{
    /* Generate clk_top_can2 code */
    clock_set_source_divider(clock_can2, clk_src_pll1_clk0, 5);

    clock_add_to_group(clock_can2, 0);
}

void init_eth0_clock(void)
{
    /* ENET is not used on this board, kept only for the board API completeness */
    clock_add_to_group(clock_eth0, 0);
}

void init_esc0_clock(void)
{
    /* ESC core (100 MHz) and PHY reference clock (25 MHz) gates are enabled by
     * ecat_hardware_init() through esc_config_eeprom_and_clock(), see
     * hpm_sdk/samples/ethercat/port/hpm_ecat_hw.c */
    clock_add_to_group(clock_esc0, 0);
}

void init_gptmr0_clock(void)
{
    /* GPTMR0 ch0 = board delay, ch1 = 1 ms software timer (see board.c) */
    clock_add_to_group(clock_gptmr0, 0);
}

void init_gptmr1_clock(void)
{
    /* GPTMR1 ch2/ch3 = bidirectional DShot telemetry capture inputs */
    clock_add_to_group(clock_gptmr1, 0);
}

void init_ptmr_clock(void)
{
    clock_add_to_group(clock_ptmr, 0);
}

void init_usb0_clock(void)
{
    /* USB0 device (CDC-ACM virtual COM port for the PC tuning tool) */
    clock_add_to_group(clock_usb0, 0);
}
