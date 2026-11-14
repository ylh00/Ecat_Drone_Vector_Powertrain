/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * USB CDC-ACM application layer (virtual COM port for the PC tuning tool)
 * plus the host protocol dispatcher.
 *
 * The USB device stack used is CherryUSB (middleware/cherryusb), device class
 * CDC ACM, see the hpm_sdk cherryusb sample:
 *   hpm_sdk/samples/cherryusb/device/cdc_acm/cdc_acm_vcom
 * The CherryUSB configuration for this board lives in port/usb_config.h.
 *
 * Data flow:
 *   USB IRQ --> usbd_cdc_acm_bulk_out()  --> RX ring buffer (APP_CFG_USB_CDC_RX_BUF_SIZE)
 *   usb_cdc_app_task() --> host_proto_app_task() --> usb_cdc_app_write()
 *   usb_cdc_app_write() --> TX ring buffer (APP_CFG_USB_CDC_TX_BUF_SIZE)
 *   USB IRQ <-- usbd_ep_start_write()     <-- TX flush from usb_cdc_app_write()
 *
 * The USB stack itself is interrupt driven; usb_cdc_app_task() only drains the
 * RX ring into the protocol engine and kicks the TX engine.  It never blocks.
 */

#ifndef USB_CDC_APP_H
#define USB_CDC_APP_H

#include <stdint.h>
#include "app_config.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Initialize the USB device controller + CDC ACM class.
 * Returns 0 on success, <0 when the USB stack could not be started.
 * Safe to call when the USB hardware is missing: the caller only logs a
 * warning and the rest of the firmware keeps running. */
int usb_cdc_app_init(void);

/* Poll the USB stack, must be called from the main loop.
 * Also runs the host protocol engine (host_proto_app_task()). */
void usb_cdc_app_task(void);

/* True when the CDC ACM port is enumerated and the host has opened it (DTR). */
uint8_t usb_cdc_app_connected(void);

/* Send raw bytes to the host (non blocking). Returns bytes queued or <0. */
int usb_cdc_app_write(const uint8_t *data, uint32_t len);

/* Statistics for the host tool telemetry frame:
 *   rx_frames   : protocol frames successfully received (host_proto_app.c)
 *   crc_errors  : frames dropped because of a CRC / sync error
 *   tx_drops    : bytes dropped because the TX ring buffer was full
 * Any pointer may be NULL. */
void usb_cdc_app_get_stats(uint32_t *rx_frames, uint32_t *crc_errors, uint32_t *tx_drops);

/* USB (transport) level statistics, any pointer may be NULL. */
void usb_cdc_app_get_usb_stats(uint32_t *rx_bytes, uint32_t *tx_bytes,
                               uint32_t *rx_overflows, uint32_t *tx_drops);

/* Number of bytes currently waiting in the RX ring buffer. */
uint32_t usb_cdc_app_rx_available(void);

/* Number of bytes that can be queued in the TX ring buffer right now.  The
 * host protocol engine uses it to drop (and let the host retry) a response
 * instead of sending a partial frame. */
uint32_t usb_cdc_app_tx_free(void);

/* Pull up to max_len received bytes from the RX ring buffer (non blocking).
 * Returns the number of bytes copied into dst (0 when nothing is buffered). */
uint32_t usb_cdc_app_read(uint8_t *dst, uint32_t max_len);

/* Drop all buffered RX/TX data (called when the host re-opens the port). */
void usb_cdc_app_flush(void);

#ifdef __cplusplus
}
#endif

#endif /* USB_CDC_APP_H */
