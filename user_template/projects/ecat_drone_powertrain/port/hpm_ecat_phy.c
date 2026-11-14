/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * PHY handling for the on-chip (SIP) MII PHY of the HPM5E3Y.
 *
 * Adapted from hpm_sdk/samples/ethercat/port/hpm_ecat_phy.c:
 *   - reset GPIOs / level / timing come from the board package (board.h)
 *     instead of the hard coded EVK JL1111 values
 *   - the JL1111 page-7 LED mode + MDIO broadcast filter configuration is kept
 *     unchanged (the on-chip PHY is the same JLSemi PHY die, and the internal
 *     MII PHY usage guide does not request any PHY register configuration
 *     change)
 *   - the PHY MDIO address is DISCOVERED instead of assumed: the address is
 *     strapped on the board (PHY LED0/LED1 pins PA26/PA29 for PHY0 and
 *     PA25/PA28 for PHY1, see the HPM5E3Y datasheet) and the actual strapping
 *     of this board was not verified yet.  If no PHY answers at the expected
 *     addresses, all MDIO addresses 0..7 are scanned and the responding ones
 *     are used.  A missing MDIO answer never fails the EtherCAT initialization:
 *     MDIO is only the management path, the MII data path is independent and
 *     the PHY uses its default (auto negotiation) settings.
 *   - ecat_phy_mdio_probe() prints the detected PHY identities and link state
 *     as a bring-up diagnostic
 */

#include <stdio.h>

#include "hpm_ecat_phy.h"
#include "hpm_gpio_drv.h"
#include "board.h"

void ecat_phy_reset(void)
{
    gpio_write_pin(BOARD_ECAT_PHY0_RESET_GPIO, BOARD_ECAT_PHY0_RESET_GPIO_PORT_INDEX, BOARD_ECAT_PHY0_RESET_PIN_INDEX, BOARD_ECAT_PHY_RESET_LEVEL);
#if BOARD_ECAT_SUPPORT_PORT1
    gpio_write_pin(BOARD_ECAT_PHY1_RESET_GPIO, BOARD_ECAT_PHY1_RESET_GPIO_PORT_INDEX, BOARD_ECAT_PHY1_RESET_PIN_INDEX, BOARD_ECAT_PHY_RESET_LEVEL);
#endif
#if BOARD_ECAT_SUPPORT_PORT2
    gpio_write_pin(BOARD_ECAT_PHY2_RESET_GPIO, BOARD_ECAT_PHY2_RESET_GPIO_PORT_INDEX, BOARD_ECAT_PHY2_RESET_PIN_INDEX, BOARD_ECAT_PHY_RESET_LEVEL);
#endif
    board_delay_ms(BOARD_ECAT_PHY_RESET_HOLD_TIME_MS);
    gpio_write_pin(BOARD_ECAT_PHY0_RESET_GPIO, BOARD_ECAT_PHY0_RESET_GPIO_PORT_INDEX, BOARD_ECAT_PHY0_RESET_PIN_INDEX, !BOARD_ECAT_PHY_RESET_LEVEL);
#if BOARD_ECAT_SUPPORT_PORT1
    gpio_write_pin(BOARD_ECAT_PHY1_RESET_GPIO, BOARD_ECAT_PHY1_RESET_GPIO_PORT_INDEX, BOARD_ECAT_PHY1_RESET_PIN_INDEX, !BOARD_ECAT_PHY_RESET_LEVEL);
#endif
#if BOARD_ECAT_SUPPORT_PORT2
    gpio_write_pin(BOARD_ECAT_PHY2_RESET_GPIO, BOARD_ECAT_PHY2_RESET_GPIO_PORT_INDEX, BOARD_ECAT_PHY2_RESET_PIN_INDEX, !BOARD_ECAT_PHY_RESET_LEVEL);
#endif
    board_delay_ms(BOARD_ECAT_PHY_RESET_WAIT_TIME_MS);
}

void ecat_phy_keep_reset(void)
{
    gpio_write_pin(BOARD_ECAT_PHY0_RESET_GPIO, BOARD_ECAT_PHY0_RESET_GPIO_PORT_INDEX, BOARD_ECAT_PHY0_RESET_PIN_INDEX, BOARD_ECAT_PHY_RESET_LEVEL);
#if BOARD_ECAT_SUPPORT_PORT1
    gpio_write_pin(BOARD_ECAT_PHY1_RESET_GPIO, BOARD_ECAT_PHY1_RESET_GPIO_PORT_INDEX, BOARD_ECAT_PHY1_RESET_PIN_INDEX, BOARD_ECAT_PHY_RESET_LEVEL);
#endif
#if BOARD_ECAT_SUPPORT_PORT2
    gpio_write_pin(BOARD_ECAT_PHY2_RESET_GPIO, BOARD_ECAT_PHY2_RESET_GPIO_PORT_INDEX, BOARD_ECAT_PHY2_RESET_PIN_INDEX, BOARD_ECAT_PHY_RESET_LEVEL);
#endif
    board_delay_ms(BOARD_ECAT_PHY_RESET_HOLD_TIME_MS);
}

/* config PHY LED mode (JL1111 Page 7 Register 19, bits[5:4] to 0) */
static hpm_stat_t ecat_jl1111_phy_config_led_mode(ESC_Type *ptr, uint8_t phy_addr)
{
    hpm_stat_t stat;

    /* config LED mode to 00 */
    stat = esc_mdio_write(ptr, phy_addr, JL11X1_PAGESEL_REG_OFFSET, JL11X1_WOLEN_REG_OFFSET / 32);
    if (stat != status_success) {
        return stat;
    }

    stat = esc_mdio_write(ptr, phy_addr, JL11X1_WOLEN_REG_OFFSET % 32, JL11X1_WOLEN_REG_VALUE);
    if (stat != status_success) {
        return stat;
    }

    stat = esc_mdio_write(ptr, phy_addr, JL11X1_PAGESEL_REG_OFFSET, 0);
    if (stat != status_success) {
        return stat;
    }

    return status_success;
}

static hpm_stat_t ecat_jl1111_phy_disable_broadcast_response(ESC_Type *ptr, uint8_t phy_addr, uint8_t phy_offset)
{
    hpm_stat_t stat;

    /* foreach phyid which is not broadcast, disable broadcast */
    if (phy_addr + phy_offset > 0) {
        stat = esc_mdio_write(ptr, phy_addr, JL11X1_PAGESEL_REG_OFFSET, 128);
        if (stat != status_success) {
            return stat;
        }

        uint16_t value = (uint16_t)(((uint16_t)(phy_addr + phy_offset) << 5) | 0x1F);
        stat = esc_mdio_write(ptr, phy_addr, 19, value);
        if (stat != status_success) {
            return stat;
        }

        stat = esc_mdio_write(ptr, phy_addr, JL11X1_PAGESEL_REG_OFFSET, 0);
        if (stat != status_success) {
            return stat;
        }
    }

    return status_success;
}

/* --------------------------------------------------------------------------
 * MDIO address discovery
 *   The MDIO address of the on-chip PHYs is strapped with the PHY LED0/LED1
 *   pins (HPM5E3Y datasheet): PHY0 -> PA29/PA28, PHY1 -> PA26/PA25.
 *   BOARD_ECAT_PHY_ADDR_OFFSET + BOARD_ECAT_PORTn_PHY_ADDR is the expected
 *   address, BOARD_ECAT_PORTn_PHY_ADDR is the address used by the ecat_io
 *   sample.  Both are tried, then all remaining addresses 0..7 are scanned.
 * -------------------------------------------------------------------------- */
static bool ecat_phy_mdio_alive(ESC_Type *ptr, uint8_t phy_addr)
{
    uint16_t id1 = 0U;

    if (esc_mdio_read(ptr, phy_addr, JL11X1_PHY_ID1_REG_OFFSET, &id1) != status_success) {
        return false;
    }
    /* 0x0000/0xFFFF are not valid PHY identifier values */
    return ((id1 != 0x0000U) && (id1 != 0xFFFFU));
}

static bool ecat_phy_find_mdio_addr(ESC_Type *ptr, uint8_t port, uint8_t *phy_addr)
{
    uint8_t port_addr;
    uint8_t preferred_addr;
    uint8_t sample_addr;

    if (phy_addr == NULL) {
        return false;
    }

    port_addr = (port == 0U) ? (uint8_t)BOARD_ECAT_PORT0_PHY_ADDR : (uint8_t)BOARD_ECAT_PORT1_PHY_ADDR;
    preferred_addr = (uint8_t)(BOARD_ECAT_PHY_ADDR_OFFSET + port_addr);
    sample_addr = port_addr;

    if (ecat_phy_mdio_alive(ptr, preferred_addr)) {
        *phy_addr = preferred_addr;
        return true;
    }
    if ((sample_addr != preferred_addr) && ecat_phy_mdio_alive(ptr, sample_addr)) {
        *phy_addr = sample_addr;
        return true;
    }
    for (uint8_t addr = 0U; addr <= 0x07U; addr++) {
        if ((addr == preferred_addr) || (addr == sample_addr)) {
            continue;
        }
        if (ecat_phy_mdio_alive(ptr, addr)) {
            *phy_addr = addr;
            return true;
        }
    }

    return false;
}

static void ecat_phy_config_port(ESC_Type *ptr, uint8_t port)
{
    uint8_t phy_addr = 0U;
    uint8_t sample_addr;
    uint8_t broadcast_offset;
    hpm_stat_t stat;

    if (!ecat_phy_find_mdio_addr(ptr, port, &phy_addr)) {
        printf("[ECAT] PHY port%u: no answer on MDIO, PHY register config skipped "
               "(PHY default settings / auto negotiation stay active)\n", (unsigned)port);
        return;
    }

    printf("[ECAT] PHY port%u: MDIO address %u\n", (unsigned)port, (unsigned)phy_addr);

    /* If the PHY answered at the address used by the ecat_io sample
     * (BOARD_ECAT_PORTn_PHY_ADDR), keep the sample behaviour for the MDIO
     * broadcast filter (own address = port address + PHY address offset).
     * If it answered at the board.h "expected" address (offset + port address)
     * or at a scanned address, that address already is the PHY's own address. */
    sample_addr = (port == 0U) ? (uint8_t)BOARD_ECAT_PORT0_PHY_ADDR : (uint8_t)BOARD_ECAT_PORT1_PHY_ADDR;
    broadcast_offset = (phy_addr == sample_addr) ? (uint8_t)BOARD_ECAT_PHY_ADDR_OFFSET : 0U;

    stat = ecat_jl1111_phy_config_led_mode(ptr, phy_addr);
    if (stat == status_success) {
        stat = ecat_jl1111_phy_disable_broadcast_response(ptr, phy_addr, broadcast_offset);
    }
    if (stat != status_success) {
        /* non fatal: the PHY keeps its default settings, the MII data path and
         * the EtherCAT communication are not affected */
        printf("[ECAT] PHY port%u: register configuration failed (0x%08X), keep PHY defaults\n",
               (unsigned)port, (unsigned)stat);
    }
}

hpm_stat_t ecat_phy_config(ESC_Type *ptr)
{
    /* The PHY configuration is a best effort operation: never fail the ESC
     * initialization because of it. */
    ecat_phy_config_port(ptr, 0U);
#if defined(BOARD_ECAT_SUPPORT_PORT1) && BOARD_ECAT_SUPPORT_PORT1
    ecat_phy_config_port(ptr, 1U);
#endif
#if defined(BOARD_ECAT_SUPPORT_PORT2) && BOARD_ECAT_SUPPORT_PORT2
    ecat_phy_config_port(ptr, 2U);
#endif
    return status_success;
}

/* ==========================================================================
 * Non fatal MDIO probe: print every PHY that answers on MDIO address 0..7.
 * Used as a bring-up diagnostic from ecat_app_init().
 * ========================================================================== */
hpm_stat_t ecat_phy_mdio_probe(ESC_Type *ptr)
{
    uint8_t found = 0U;

    printf("[ECAT] MDIO scan (addresses 0..7):\n");

    for (uint8_t addr = 0U; addr <= 0x07U; addr++) {
        uint16_t id1 = 0U, id2 = 0U, bmsr = 0U;

        if (esc_mdio_read(ptr, addr, JL11X1_PHY_ID1_REG_OFFSET, &id1) != status_success) {
            continue;
        }
        if ((id1 == 0x0000U) || (id1 == 0xFFFFU)) {
            continue;
        }
        (void)esc_mdio_read(ptr, addr, JL11X1_PHY_ID2_REG_OFFSET, &id2);
        (void)esc_mdio_read(ptr, addr, JL11X1_BMSR_REG_OFFSET, &bmsr);

        printf("[ECAT]   PHY at MDIO addr %u: ID1=0x%04X ID2=0x%04X BMSR=0x%04X link=%u autoneg_done=%u\n",
               (unsigned)addr, (unsigned)id1, (unsigned)id2, (unsigned)bmsr,
               (unsigned)((bmsr & 0x0004U) ? 1U : 0U),
               (unsigned)((bmsr & 0x0020U) ? 1U : 0U));
        found++;
    }

    if (found == 0U) {
        printf("[ECAT]   no PHY answered on MDIO (check PHY reset strobes PV12/PW12, "
               "refclk 25 MHz and MDIO pull-up)\n");
        return status_fail;
    }
    return status_success;
}
