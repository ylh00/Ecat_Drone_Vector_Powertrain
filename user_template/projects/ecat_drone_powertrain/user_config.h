/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * Configuration of the EEPROM emulation component for the EtherCAT EEPROM.
 * (same purpose as hpm_sdk/samples/ethercat/ecat_io/user_config.h)
 */

/* this file is for eeprom emulation component */

#ifndef _USER_CONFIG_H
#define _USER_CONFIG_H

#include "ecat_def.h"

#ifdef __cplusplus
extern "C" {
#endif

#define E2P_DEBUG_LEVEL        E2P_DEBUG_LEVEL_WARN /* if use E2P_DEBUG_LEVEL_INFO, excessive printf */
#define E2P_MAX_VAR_CNT        (ESC_EEPROM_SIZE)

#ifdef __cplusplus
}
#endif

#endif
