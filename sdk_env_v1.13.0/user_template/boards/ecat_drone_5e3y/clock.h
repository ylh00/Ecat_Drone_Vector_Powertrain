
/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 *
 * Board clock tree for ecat_drone_5e3y (HPM5E3Y)
 *
 */

#ifndef HPM_CLOCK_H
#define HPM_CLOCK_H

#ifdef __cplusplus
extern "C" {
#endif

void init_board_clock(void);
void init_board_clock_source(void);
void init_uart0_clock(void);
void init_uart2_clock(void);
void init_uart3_clock(void);
void init_i2c3_clock(void);
void init_spi1_clock(void);
void init_adc0_bus_clock(void);
void init_adc0_analog_clock(void);
void init_adc1_bus_clock(void);
void init_adc1_analog_clock(void);
void init_can0_clock(void);
void init_can1_clock(void);
void init_can2_clock(void);
void init_eth0_clock(void);
void init_esc0_clock(void);
void init_gptmr0_clock(void);
void init_gptmr1_clock(void);
void init_ptmr_clock(void);
void init_usb0_clock(void);

#ifdef __cplusplus
}
#endif
#endif /* HPM_CLOCK_H */
