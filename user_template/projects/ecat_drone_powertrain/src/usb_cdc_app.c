/*
 * Copyright (c) 2026 HPMicro
 *
 * SPDX-License-Identifier: BSD-3-Clause
 *
 * USB CDC-ACM virtual COM port for the PC tuning tool.
 *
 * The device stack is CherryUSB (middleware/cherryusb) and this file is based
 * on the HPM SDK sample
 *   hpm_sdk/samples/cherryusb/device/cdc_acm/cdc_acm_vcom/src/cdc_acm.c
 * adapted to the project API:
 *   - the UART echo test is removed,
 *   - received bytes are stored in a ring buffer and consumed by the host
 *     protocol engine (host_proto_app.c),
 *   - outgoing bytes go through a TX ring buffer that is flushed from the
 *     USB interrupt / usb_cdc_app_task(), so usb_cdc_app_write() never blocks,
 *   - connection (DTR) tracking and transport statistics are added.
 *
 * Data flow:
 *   USB OUT EP --> usbd_cdc_acm_bulk_out() --> RX ring buffer
 *   usb_cdc_app_read() <-- host_proto_app_task()
 *   host_proto_app.c --> usb_cdc_app_write() --> TX ring buffer
 *   TX ring buffer --> usbd_ep_start_write() --> USB IN EP (completion IRQ)
 */

#include <stdbool.h>
#include <string.h>

#include "board.h"
#include "app_config.h"
#include "app_log.h"
#include "usb_cdc_app.h"
#include "host_proto.h"
#include "usb_config.h"

#include "usbd_core.h"
#include "usbd_cdc_acm.h"
#include "usb_ringbuffer.h"

/* ------------------------------------------------------------------------- */
/* USB device configuration                                                  */
/* ------------------------------------------------------------------------- */

#define USB_CDC_BUS_ID      (0U)
#define CDC_IN_EP           (0x81U)
#define CDC_OUT_EP          (0x01U)
#define CDC_INT_EP          (0x83U)
#define USB_CONFIG_SIZE     (9U + CDC_ACM_DESCRIPTOR_LEN)

/* The EP max packet size is needed for the read request length.  Both HS and
 * FS configurations are provided, the actual one is selected by the stack. */
#define USB_CDC_MAX_MPS     (512U)

/* ------------------------------------------------------------------------- */
/* Descriptors (same as the CherryUSB CDC ACM vcom sample)                   */
/* ------------------------------------------------------------------------- */

static const uint8_t device_descriptor[] = {
    USB_DEVICE_DESCRIPTOR_INIT(USB_2_0, 0xEF, 0x02, 0x01, USBD_VID, USBD_PID, 0x0100, 0x01)
};

static const uint8_t config_descriptor_hs[] = {
    USB_CONFIG_DESCRIPTOR_INIT(USB_CONFIG_SIZE, 0x02, 0x01, USB_CONFIG_BUS_POWERED, USBD_MAX_POWER),
    CDC_ACM_DESCRIPTOR_INIT(0x00, CDC_INT_EP, CDC_OUT_EP, CDC_IN_EP, USB_BULK_EP_MPS_HS, 0x02),
};

static const uint8_t config_descriptor_fs[] = {
    USB_CONFIG_DESCRIPTOR_INIT(USB_CONFIG_SIZE, 0x02, 0x01, USB_CONFIG_BUS_POWERED, USBD_MAX_POWER),
    CDC_ACM_DESCRIPTOR_INIT(0x00, CDC_INT_EP, CDC_OUT_EP, CDC_IN_EP, USB_BULK_EP_MPS_FS, 0x02),
};

static const uint8_t device_quality_descriptor[] = {
    USB_DEVICE_QUALIFIER_DESCRIPTOR_INIT(USB_2_0, 0xEF, 0x02, 0x01, 0x01),
};

static const uint8_t other_speed_config_descriptor_hs[] = {
    USB_OTHER_SPEED_CONFIG_DESCRIPTOR_INIT(USB_CONFIG_SIZE, 0x02, 0x01, USB_CONFIG_BUS_POWERED, USBD_MAX_POWER),
    CDC_ACM_DESCRIPTOR_INIT(0x00, CDC_INT_EP, CDC_OUT_EP, CDC_IN_EP, USB_BULK_EP_MPS_FS, 0x02),
};

static const uint8_t other_speed_config_descriptor_fs[] = {
    USB_OTHER_SPEED_CONFIG_DESCRIPTOR_INIT(USB_CONFIG_SIZE, 0x02, 0x01, USB_CONFIG_BUS_POWERED, USBD_MAX_POWER),
    CDC_ACM_DESCRIPTOR_INIT(0x00, CDC_INT_EP, CDC_OUT_EP, CDC_IN_EP, USB_BULK_EP_MPS_HS, 0x02),
};

static const char *string_descriptors[] = {
    (const char[]){ 0x09, 0x04 },       /* Langid            */
    CONFIG_USBDEV_MANUFACTURER_STRING,  /* Manufacturer      */
    CONFIG_USBDEV_PRODUCT_STRING,       /* Product           */
    CONFIG_USBDEV_SERIAL_STRING,        /* Serial number     */
};

static const uint8_t *device_descriptor_callback(uint8_t speed)
{
    (void)speed;
    return device_descriptor;
}

static const uint8_t *config_descriptor_callback(uint8_t speed)
{
    if (speed == USB_SPEED_HIGH) {
        return config_descriptor_hs;
    } else if (speed == USB_SPEED_FULL) {
        return config_descriptor_fs;
    } else {
        return NULL;
    }
}

static const uint8_t *device_quality_descriptor_callback(uint8_t speed)
{
    (void)speed;
    return device_quality_descriptor;
}

static const uint8_t *other_speed_config_descriptor_callback(uint8_t speed)
{
    if (speed == USB_SPEED_HIGH) {
        return other_speed_config_descriptor_hs;
    } else if (speed == USB_SPEED_FULL) {
        return other_speed_config_descriptor_fs;
    } else {
        return NULL;
    }
}

static const char *string_descriptor_callback(uint8_t speed, uint8_t index)
{
    (void)speed;
    if (index >= (sizeof(string_descriptors) / sizeof(string_descriptors[0]))) {
        return NULL;
    }
    return string_descriptors[index];
}

static const struct usb_descriptor cdc_descriptor = {
    .device_descriptor_callback = device_descriptor_callback,
    .config_descriptor_callback = config_descriptor_callback,
    .device_quality_descriptor_callback = device_quality_descriptor_callback,
    .other_speed_descriptor_callback = other_speed_config_descriptor_callback,
    .string_descriptor_callback = string_descriptor_callback,
};

/* ------------------------------------------------------------------------- */
/* Buffers and state                                                         */
/* ------------------------------------------------------------------------- */

/* USB DMA buffers must be aligned; they live in the non cacheable section
 * (same attribute as the CherryUSB sample). */
USB_NOCACHE_RAM_SECTION USB_MEM_ALIGNX static uint8_t s_ep_out_buffer[USB_CDC_MAX_MPS];

/* Ring buffer pools: not DMA targets directly (only plain RAM) */
static uint8_t s_rx_pool[APP_CFG_USB_CDC_RX_BUF_SIZE];
static uint8_t s_tx_pool[APP_CFG_USB_CDC_TX_BUF_SIZE];

static usb_ringbuffer_t s_rx_ring;
static usb_ringbuffer_t s_tx_ring;

static volatile uint8_t s_configured;   /* USB configuration selected           */
static volatile uint8_t s_dtr;          /* host opened the virtual COM port     */

static volatile uint8_t s_tx_busy;      /* a bulk IN transfer is in flight      */
static volatile uint32_t s_tx_xfer_len; /* length of the in flight transfer     */

/* Transport statistics */
static volatile uint32_t s_stat_rx_bytes;
static volatile uint32_t s_stat_tx_bytes;
static volatile uint32_t s_stat_rx_overflows;
static volatile uint32_t s_stat_tx_drops;

static struct usbd_interface s_intf0;
static struct usbd_interface s_intf1;

static void usb_cdc_tx_kick(void);

/* ------------------------------------------------------------------------- */
/* Endpoint callbacks (called from the USB interrupt)                        */
/* ------------------------------------------------------------------------- */

void usbd_cdc_acm_bulk_out(uint8_t busid, uint8_t ep, uint32_t nbytes)
{
    uint32_t written;

    s_stat_rx_bytes += nbytes;
    written = usb_ringbuffer_write(&s_rx_ring, s_ep_out_buffer, nbytes);
    if (written < nbytes) {
        s_stat_rx_overflows += (nbytes - written);
        LOGM_W(LOG_MOD_USB, "RX ring overflow, dropped %u bytes",
               (unsigned)(nbytes - written));
    }

    /* re-arm the OUT endpoint for the next packet */
    (void)usbd_ep_start_read(busid, ep, s_ep_out_buffer, usbd_get_ep_mps(busid, ep));
}

void usbd_cdc_acm_bulk_in(uint8_t busid, uint8_t ep, uint32_t nbytes)
{
    (void)busid;
    (void)ep;
    (void)nbytes;

    if (s_tx_busy) {
        s_stat_tx_bytes += s_tx_xfer_len;
        (void)usb_ringbuffer_linear_read_done(&s_tx_ring, s_tx_xfer_len);
        s_tx_xfer_len = 0U;
        s_tx_busy = 0U;
    }
    /* send the next chunk of the response (if any) */
    usb_cdc_tx_kick();
}

static struct usbd_endpoint cdc_out_ep = {
    .ep_addr = CDC_OUT_EP,
    .ep_cb = usbd_cdc_acm_bulk_out,
};

static struct usbd_endpoint cdc_in_ep = {
    .ep_addr = CDC_IN_EP,
    .ep_cb = usbd_cdc_acm_bulk_in,
};

/* ------------------------------------------------------------------------- */
/* CDC ACM class callbacks                                                   */
/* ------------------------------------------------------------------------- */

void usbd_cdc_acm_set_dtr(uint8_t busid, uint8_t intf, bool dtr)
{
    (void)busid;
    (void)intf;

    s_dtr = dtr ? 1U : 0U;
    if (s_dtr != 0U) {
        LOGM_I(LOG_MOD_USB, "host opened the CDC port");
    } else {
        /* drop partially received frames so the next session starts clean */
        usb_cdc_app_flush();
        LOGM_I(LOG_MOD_USB, "host closed the CDC port");
    }
}

/* ------------------------------------------------------------------------- */
/* USB event handler                                                         */
/* ------------------------------------------------------------------------- */

static void usb_cdc_event_handler(uint8_t busid, uint8_t event)
{
    switch (event) {
    case USBD_EVENT_CONFIGURED:
        s_configured = 1U;
        usb_cdc_app_flush();
        (void)usbd_ep_start_read(busid, CDC_OUT_EP, s_ep_out_buffer,
                                 usbd_get_ep_mps(busid, CDC_OUT_EP));
        LOGM_I(LOG_MOD_USB, "USB configured (VID:PID %04X:%04X)",
               (unsigned)USBD_VID, (unsigned)USBD_PID);
        break;
    case USBD_EVENT_DISCONNECTED:
    case USBD_EVENT_RESET:
        s_configured = 0U;
        s_dtr = 0U;
        s_tx_busy = 0U;
        s_tx_xfer_len = 0U;
        usb_ringbuffer_reset(&s_rx_ring);
        usb_ringbuffer_reset(&s_tx_ring);
        break;
    case USBD_EVENT_SUSPEND:
    case USBD_EVENT_RESUME:
    case USBD_EVENT_CONNECTED:
    default:
        break;
    }
}

/* ------------------------------------------------------------------------- */
/* TX engine                                                                 */
/* ------------------------------------------------------------------------- */

/* Start the next bulk IN transfer when the line is idle.  Runs with the global
 * interrupt state saved/restored so the completion ISR cannot race the
 * busy/transfer state. */
static void usb_cdc_tx_kick(void)
{
    uint32_t irq_state;
    uint32_t len = 0U;
    void *ptr;

    if ((s_configured == 0U) || (s_dtr == 0U)) {
        return;
    }

    irq_state = disable_global_irq(CSR_MSTATUS_MIE_MASK);
    if (s_tx_busy == 0U) {
        ptr = usb_ringbuffer_linear_read_setup(&s_tx_ring, &len);
        if ((ptr != NULL) && (len > 0U)) {
            s_tx_xfer_len = len;
            s_tx_busy = 1U;
            if (usbd_ep_start_write(USB_CDC_BUS_ID, CDC_IN_EP, (const uint8_t *)ptr, len) != 0) {
                s_tx_busy = 0U;
                s_tx_xfer_len = 0U;
                s_stat_tx_drops += len;
            }
        }
    }
    restore_global_irq(irq_state);
}

/* ------------------------------------------------------------------------- */
/* Public API                                                                */
/* ------------------------------------------------------------------------- */

int usb_cdc_app_init(void)
{
    int ret;

    (void)usb_ringbuffer_init(&s_rx_ring, s_rx_pool, sizeof(s_rx_pool));
    (void)usb_ringbuffer_init(&s_tx_ring, s_tx_pool, sizeof(s_tx_pool));

    s_configured = 0U;
    s_dtr = 0U;
    s_tx_busy = 0U;
    s_tx_xfer_len = 0U;
    s_stat_rx_bytes = 0U;
    s_stat_tx_bytes = 0U;
    s_stat_rx_overflows = 0U;
    s_stat_tx_drops = 0U;

    /* board level USB init: pins, PHY/clock and VBUS configuration (same call
     * the CherryUSB cdc_acm_vcom sample main() does before cdc_acm_init) */
    board_init_usb((USB_Type *)CONFIG_HPM_USBD_BASE);
    intc_set_irq_priority((uint32_t)CONFIG_HPM_USBD_IRQn, 2);

    usbd_desc_register(USB_CDC_BUS_ID, &cdc_descriptor);
    usbd_add_interface(USB_CDC_BUS_ID, usbd_cdc_acm_init_intf(USB_CDC_BUS_ID, &s_intf0));
    usbd_add_interface(USB_CDC_BUS_ID, usbd_cdc_acm_init_intf(USB_CDC_BUS_ID, &s_intf1));
    usbd_add_endpoint(USB_CDC_BUS_ID, &cdc_out_ep);
    usbd_add_endpoint(USB_CDC_BUS_ID, &cdc_in_ep);

    ret = usbd_initialize(USB_CDC_BUS_ID, (uintptr_t)CONFIG_HPM_USBD_BASE, usb_cdc_event_handler);
    if (ret != 0) {
        LOGM_E(LOG_MOD_USB, "usbd_initialize failed (%d)", ret);
        return -1;
    }

    LOGM_I(LOG_MOD_USB, "CDC ACM ready, VID:PID %04X:%04X product \"%s\"",
           (unsigned)USBD_VID, (unsigned)USBD_PID, CONFIG_USBDEV_PRODUCT_STRING);
    return 0;
}

void usb_cdc_app_task(void)
{
    /* The USB stack itself is interrupt driven; the task only runs the host
     * protocol engine (bounded, non blocking) and defensively kicks the TX
     * engine in case a completion interrupt was lost. */
    if (s_configured == 0U) {
        return;
    }

    host_proto_app_task();

    if ((s_tx_busy == 0U) && (usb_ringbuffer_get_used(&s_tx_ring) != 0U)) {
        usb_cdc_tx_kick();
    }
}

uint8_t usb_cdc_app_connected(void)
{
    return (uint8_t)((s_configured != 0U) && (s_dtr != 0U));
}

int usb_cdc_app_write(const uint8_t *data, uint32_t len)
{
    uint32_t written;

    if ((data == NULL) || (len == 0U)) {
        return 0;
    }
    if (usb_cdc_app_connected() == 0U) {
        return -1;
    }

    written = usb_ringbuffer_write(&s_tx_ring, (void *)data, len);
    if (written < len) {
        s_stat_tx_drops += (len - written);
        LOGM_W(LOG_MOD_USB, "TX ring full, dropped %u bytes",
               (unsigned)(len - written));
    }
    usb_cdc_tx_kick();

    return (int)written;
}

void usb_cdc_app_get_stats(uint32_t *rx_frames, uint32_t *crc_errors, uint32_t *tx_drops)
{
    uint32_t frames = 0U;
    uint32_t crc = 0U;

    host_proto_app_get_stats(&frames, &crc);

    if (rx_frames != NULL) {
        *rx_frames = frames;
    }
    if (crc_errors != NULL) {
        *crc_errors = crc;
    }
    if (tx_drops != NULL) {
        *tx_drops = s_stat_tx_drops;
    }
}

void usb_cdc_app_get_usb_stats(uint32_t *rx_bytes, uint32_t *tx_bytes,
                               uint32_t *rx_overflows, uint32_t *tx_drops)
{
    if (rx_bytes != NULL) {
        *rx_bytes = s_stat_rx_bytes;
    }
    if (tx_bytes != NULL) {
        *tx_bytes = s_stat_tx_bytes;
    }
    if (rx_overflows != NULL) {
        *rx_overflows = s_stat_rx_overflows;
    }
    if (tx_drops != NULL) {
        *tx_drops = s_stat_tx_drops;
    }
}

uint32_t usb_cdc_app_rx_available(void)
{
    return usb_ringbuffer_get_used(&s_rx_ring);
}

uint32_t usb_cdc_app_tx_free(void)
{
    return usb_ringbuffer_get_free(&s_tx_ring);
}

uint32_t usb_cdc_app_read(uint8_t *dst, uint32_t max_len)
{
    if ((dst == NULL) || (max_len == 0U)) {
        return 0U;
    }
    return usb_ringbuffer_read(&s_rx_ring, dst, max_len);
}

void usb_cdc_app_flush(void)
{
    usb_ringbuffer_reset(&s_rx_ring);
    usb_ringbuffer_reset(&s_tx_ring);
    s_tx_busy = 0U;
    s_tx_xfer_len = 0U;
}
