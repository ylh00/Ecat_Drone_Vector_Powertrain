# Copyright (c) 2026 HPMicro
# SPDX-License-Identifier: BSD-3-Clause

sdk_compile_definitions_ifdef(BUILD_FOR_SECONDARY_CORE BOARD_RUNNING_CORE=HPM_CORE1)

sdk_inc(.)
sdk_src(pinmux.c)
sdk_src(clock.c)
sdk_src(board.c)

# The EtherCAT slave controller of this board drives its on-chip MII PHY.
sdk_compile_definitions(-DBOARD_ECAT_USE_INTERNAL_PHY=1)
