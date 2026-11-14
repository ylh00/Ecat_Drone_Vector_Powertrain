/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * ============================================================================
 *  ############  SSC APPLICATION PROGRAMMING INTERFACE STUB  ##############
 * ============================================================================
 *
 * This header is part of the *compile/link validation stub* of the Beckhoff
 * EtherCAT Slave Stack Code (SSC).  The real SSC must be generated with the
 * Beckhoff SSC Tool from
 *      SSC/Config/HPM_ECAT_DRONE_Config.xml   (stack configuration)
 *      SSC/ecat_drone.xlsx                    (application description)
 * and copied into SSC/Src/ - see SSC/README.md.
 *
 * When SSC/Src/ exists, CMake compiles the real stack instead of
 * SSC/StubSrc/ssc_stub.c, and the include order in the project CMakeLists.txt
 * makes the real SSC headers (SSC/Src) take precedence over this stub
 * directory.  This header therefore only needs to provide the subset of the
 * SSC 5.13 API that the hardware abstraction layer (port/) and the application
 * (ecat.c, src/ecat_app.c) use.
 *
 * The stub is NOT an EtherCAT implementation.  It performs no EtherCAT
 * communication at all.
 */

#ifndef ECAT_DEF_H
#define ECAT_DEF_H

#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <stdbool.h>

/* --------------------------------------------------------------------------
 * SSC basic types (the SSC Tool writes the same mapping into the generated
 * ecat_def.h, see the UINT8/... settings of the SSC configuration).
 * -------------------------------------------------------------------------- */
typedef uint8_t   UINT8;
typedef uint16_t  UINT16;
typedef uint32_t  UINT32;
typedef uint64_t  UINT64;
typedef int8_t    INT8;
typedef int16_t   INT16;
typedef int32_t   INT32;
typedef int64_t   INT64;
typedef uint8_t   BOOLEAN;
typedef char      CHAR;
typedef unsigned char UCHAR;
typedef uint16_t  USHORT;

#ifndef TRUE
#define TRUE  (1)
#endif
#ifndef FALSE
#define FALSE (0)
#endif

/* --------------------------------------------------------------------------
 * Memory qualifiers / ESC memory access type.
 *   MEM_ADDR is the base data type used for the ESC memory pointer (all ESC
 *   accesses in port/hpm_ecat_hw.h are done with explicit casts, so the access
 *   width only has to be a valid C type; the HPM ESC is a 32-bit accessible
 *   peripheral).
 *   ESCMEM / MBXMEM / OBJMEM / OBJCONST are memory qualifiers; they are empty
 *   on the HPM RISC-V controller (identical to the generated SSC settings).
 * -------------------------------------------------------------------------- */
#define MEM_ADDR     UINT32
#define ESCMEM
#define MBXMEM
#define OBJMEM
#define OBJCONST

#define MEMCPY(dst, src, len)   memcpy((dst), (src), (len))
#define MEMSET(dst, val, len)   memset((dst), (val), (len))
#define SIZEOF(x)               sizeof(x)

/* ESC memory copy macros used by the HAL (port/hpm_ecat_hw.h).  The ESC is
 * mapped as normal cacheable memory on HPM5E00 and byte accesses are
 * supported (HPM_IP_FEATURE_ESC_BYTE_READ == 1), so a normal memcpy is used. */
#define ESCMEMCPY(dst, src, len)    memcpy((dst), (src), (len))
#define ESCMBXMEMCPY(dst, src, len) memcpy((dst), (src), (len))

/* --------------------------------------------------------------------------
 * Slave stack configuration switches.
 * These MUST match the settings in SSC/Config/HPM_ECAT_DRONE_Config.xml.
 * The real generated ecat_def.h defines the same symbols; this stub only
 * provides them because there is no generated stack in the repository.
 * -------------------------------------------------------------------------- */
#define ESC_EEPROM_EMULATION          (1)
#define ESC_EEPROM_SIZE               (0x800)   /* 2 Kbyte EEPROM image      */
#define EEPROM_WRITE_SIZE             (0x2)     /* one EEPROM word per access */

#define COE_SUPPORTED                 (1)
#define COMPLETE_ACCESS_SUPPORTED     (1)
#define SEGMENTED_SDO_SUPPORTED       (1)
#define SDO_RES_INTERFACE             (1)
#define MAILBOX_QUEUE                 (1)
#define AOE_SUPPORTED                 (0)
#define EOE_SUPPORTED                 (0)
#define FOE_SUPPORTED                 (0)
#define SOE_SUPPORTED                 (0)
#define VOE_SUPPORTED                 (0)
#define DIAGNOSIS_SUPPORTED           (0)
#define EMERGENCY_SUPPORTED           (0)
#define BOOTSTRAPMODE_SUPPORTED       (0)
#define ESC_SM_WD_SUPPORTED           (1)
#define OP_PD_REQUIRED                (1)
#define UC_SET_ECAT_LED               (0)
#define ESC_SUPPORT_ECAT_LED          (1)
#define ECAT_TIMER_INT                (1)
#define EXPLICIT_DEVICE_ID            (1)
#define DC_SUPPORTED                  (1)
#define MAX_PD_INPUT_SIZE             (0x40)
#define MAX_PD_OUTPUT_SIZE            (0x40)
#define USE_DEFAULT_MAIN              (0)

/* Size of one EEPROM read access in bytes (2, 4 or 8).  The application
 * selects 8 for the flash emulation (see ecat.c / MainInit of the stub). */
extern UINT8 EepromReadSize;

#endif /* ECAT_DEF_H */
