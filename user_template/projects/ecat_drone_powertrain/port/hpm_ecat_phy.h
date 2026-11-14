/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * PHY handling for the on-chip (SIP) MII PHY of the HPM5E3Y.
 *
 * Adapted from hpm_sdk/samples/ethercat/port/hpm_ecat_phy.h.
 *
 * The HPM5E3Y integrates two JLSemi MII PHYs.  Per the "HPM5E00 片内 MII PHY
 * 使用指南" (internal MII PHY usage guide, v1.1) no special PHY software
 * configuration is required: the PHY page-7 LED configuration and the MDIO
 * broadcast filter setup of the external JL1111 PHY work identically on the
 * on-chip PHY die, therefore ecat_phy_config() is kept identical to the SDK
 * sample.  Only the reset GPIOs (PV12 / PW12) and the reset timing come from
 * the board package (board.h).
 */

#ifndef HPM_ECAT_PHY_H
#define HPM_ECAT_PHY_H

#include "ecat_config.h"
#include "hpm_esc_drv.h"

/* JL1111 (and the compatible on-chip JLSemi PHY) register definitions */
#define JL11X1_PAGESEL_REG_OFFSET    (31)
#define JL11X1_WOLEN_REG_OFFSET      (0xF3) /* PHY LED mode configuration register address */
#define JL11X1_WOLEN_REG_VALUE       (0)
#define JL11X1_PHY_ID1_REG_OFFSET    (2)    /* PHY identifier 1                     */
#define JL11X1_PHY_ID2_REG_OFFSET    (3)    /* PHY identifier 2                     */
#define JL11X1_BMSR_REG_OFFSET       (1)    /* basic mode status register           */

/* PHY reset timing and polarity come from the board package (board.h):
 *   BOARD_ECAT_PHY_RESET_LEVEL           reset active level (0 = low)
 *   BOARD_ECAT_PHY_RESET_HOLD_TIME_MS    reset assert time  (>= 200 ns required)
 *   BOARD_ECAT_PHY_RESET_WAIT_TIME_MS    wait after reset release (>= 5 ms
 *                                        required before the first MDIO access,
 *                                        datasheet power-up requirement)
 * The board values (10 ms / 30 ms) are more conservative than the minimums. */

#ifdef __cplusplus
extern "C" {
#endif


void ecat_phy_reset(void);
void ecat_phy_keep_reset(void);
hpm_stat_t ecat_phy_config(ESC_Type *ptr);

/* Non fatal MDIO probe of the on-chip PHYs, used for bring-up logging only.
 * Returns status_success if at least one PHY answered on the MDIO bus. */
hpm_stat_t ecat_phy_mdio_probe(ESC_Type *ptr);

#ifdef __cplusplus
}
#endif


#endif /* HPM_ECAT_PHY_H */
