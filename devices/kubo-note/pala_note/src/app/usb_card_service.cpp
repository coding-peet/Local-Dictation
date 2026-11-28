#include "usb_card_service.h"

#if PALA_USB_CARD_SERVICE
#include "../../config.h"
#include <Arduino.h>
#include <atomic>
#include <cstdio>
#include <new>
#include "driver/sdmmc_host.h"
#include "esp_heap_caps.h"
#include "esp_mac.h"
#include "esp_system.h"
#include "esp32-hal-tinyusb.h"
#include "hal/usb_serial_jtag_ll.h"
#include "sdmmc_cmd.h"
#include "soc/rtc_cntl_reg.h"

namespace {
using namespace usb_card_service;
struct RawCard {
  sdmmc_card_t device{};
  alignas(4) uint8_t scratch[kBufferBytes];

  bool read(uint32_t block, uint8_t* data, uint32_t count) {
    return sdmmc_read_sectors(&device, data, block, count) == ESP_OK;
  }
  bool write(uint32_t block, const uint8_t* data, uint32_t count) {
    return sdmmc_write_sectors(&device, data, block, count) == ESP_OK;
  }
};

// Allocate the sector buffer only on the dedicated boot. The recorder uses
// the same internal RAM pool for capture and must not pay for this service.
RawCard* card = nullptr;
Medium medium;
std::atomic<bool> ejectComplete{false};

bool openCard() {
  void* storage = heap_caps_malloc(sizeof(RawCard), MALLOC_CAP_DMA | MALLOC_CAP_INTERNAL);
  if (!storage) return false;
  card = new (storage) RawCard{};
  sdmmc_host_t host = SDMMC_HOST_DEFAULT();
  host.flags = SDMMC_HOST_FLAG_1BIT | SDMMC_HOST_FLAG_DEINIT_ARG;
  sdmmc_slot_config_t slot = SDMMC_SLOT_CONFIG_DEFAULT();
  slot.width = 1;
  slot.clk = gpio_num_t(SD_CLK);
  slot.cmd = gpio_num_t(SD_CMD);
  slot.d0 = gpio_num_t(SD_D0);
  slot.flags |= SDMMC_SLOT_FLAG_INTERNAL_PULLUP;
  return sdmmc_host_init() == ESP_OK &&
         sdmmc_host_init_slot(host.slot, &slot) == ESP_OK &&
         sdmmc_card_init(&host, &card->device) == ESP_OK &&
         card->device.csd.sector_size == kSectorBytes && card->device.csd.capacity > 0;
}

uint16_t descriptor(uint8_t* destination, uint8_t* interfaceNumber) {
  const uint8_t ep = tinyusb_get_free_duplex_endpoint();
  if (!ep) return 0;
  const uint8_t desc[] = {TUD_MSC_DESCRIPTOR(*interfaceNumber, 0, ep, uint8_t(0x80 | ep), 64)};
  ++*interfaceNumber;
  std::memcpy(destination, desc, sizeof(desc));
  return sizeof(desc);
}

bool ready(uint8_t lun) {
  if (lun == 0 && medium.present()) return true;
  tud_msc_set_sense(lun, SCSI_SENSE_NOT_READY, 0x3a, 0);
  return false;
}

bool syncCard(uint8_t lun) {
  if (sdmmc_get_status(&card->device) == ESP_OK) return true;
  tud_msc_set_sense(lun, SCSI_SENSE_MEDIUM_ERROR, 0x0c, 0);
  return false;
}

int32_t io(uint8_t lun, uint32_t lba, uint32_t offset, uint8_t* bytes, uint32_t size, bool write) {
  if (!ready(lun)) return -1;
  const int32_t result = transfer(*card, card->device.csd.capacity, lba, offset,
                                  bytes, size, write, card->scratch);
  if (result == kBadRange) tud_msc_set_sense(lun, SCSI_SENSE_ILLEGAL_REQUEST, 0x21, 0);
  if (result == kMediaError) tud_msc_set_sense(lun, SCSI_SENSE_MEDIUM_ERROR, write ? 0x0c : 0x11, 0);
  return result;
}

void restoreSerialPhy() {
  // The PHY override lives in the RTC domain across software resets.
  REG_CLR_BIT(RTC_CNTL_USB_CONF_REG, RTC_CNTL_SW_HW_USB_PHY_SEL |
              RTC_CNTL_SW_USB_PHY_SEL | RTC_CNTL_USB_PAD_ENABLE);
  usb_serial_jtag_ll_phy_enable_pad(true);
}
}  // namespace

extern "C" bool tud_msc_test_unit_ready_cb(uint8_t lun) { return ready(lun); }

// The bundled Arduino HAL also provides a weak, always-read-only fallback.
// Keep our public symbol strong even though the TinyUSB header declares its
// optional callback weak.
extern "C" bool palaMscIsWritable(uint8_t lun) asm("tud_msc_is_writable_cb");
bool palaMscIsWritable(uint8_t lun) { return ready(lun); }

extern "C" void tud_msc_inquiry_cb(uint8_t, uint8_t vendor[8], uint8_t product[16], uint8_t revision[4]) {
  std::memcpy(vendor, "Pala    ", 8);
  std::memcpy(product, "Pala Note SD    ", 16);
  std::memcpy(revision, "1.00", 4);
}

extern "C" void tud_msc_capacity_cb(uint8_t lun, uint32_t* count, uint16_t* size) {
  *count = ready(lun) ? uint32_t(card->device.csd.capacity) : 0;
  *size = *count ? usb_card_service::kSectorBytes : 0;
}

extern "C" int32_t tud_msc_read10_cb(uint8_t lun, uint32_t lba, uint32_t offset, void* data, uint32_t size) {
  return io(lun, lba, offset, static_cast<uint8_t*>(data), size, false);
}

extern "C" int32_t tud_msc_write10_cb(uint8_t lun, uint32_t lba, uint32_t offset, uint8_t* data, uint32_t size) {
  return io(lun, lba, offset, data, size, true);
}

extern "C" int32_t tud_msc_scsi_cb(uint8_t lun, const uint8_t cmd[16], void*, uint16_t) {
  if (!ready(lun)) return -1;
  // SYNCHRONIZE CACHE(10). Sector writes complete synchronously, with no
  // device-side writeback cache. Check that the card still reports success.
  if (cmd[0] == 0x35) return syncCard(lun) ? 0 : -1;
  tud_msc_set_sense(lun, SCSI_SENSE_ILLEGAL_REQUEST, 0x20, 0);
  return -1;
}

extern "C" bool tud_msc_prevent_allow_medium_removal_cb(uint8_t lun, uint8_t prevent, uint8_t) {
  if (!ready(lun)) return false;
  medium.preventRemoval(prevent != 0);
  return true;
}

extern "C" bool tud_msc_start_stop_cb(uint8_t lun, uint8_t, bool start, bool loadEject) {
  if (!ready(lun)) return false;
  if (loadEject && !start && !syncCard(lun)) return false;
  if (medium.startStop(start, loadEject)) return true;
  tud_msc_set_sense(lun, SCSI_SENSE_ILLEGAL_REQUEST, 0x53, 0x02); // removal prevented
  return false;
}

extern "C" void tud_msc_scsi_complete_cb(uint8_t lun, const uint8_t cmd[16]) {
  // Invoked after the host has received the SCSI command status wrapper.
  if (lun == 0 && medium.ejectStatusSent(cmd[0], cmd[4])) ejectComplete.store(true);
}

void runUsbCardService() {
  uint8_t mac[6];
  if (!openCard() || esp_efuse_mac_get_default(mac) != ESP_OK ||
      tinyusb_enable_interface(USB_INTERFACE_MSC, TUD_MSC_DESC_LEN, descriptor) != ESP_OK) {
    esp_restart();
  }
  char serial[13];
  std::snprintf(serial, sizeof(serial), "%02X%02X%02X%02X%02X%02X",
                mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
  tinyusb_device_config_t usb{};
  usb.vid = USB_ESPRESSIF_VID;
  usb.usb_version = 0x0200;
  usb.fw_version = 0x0100;
  usb.usb_attributes = TUSB_DESC_CONFIG_ATT_SELF_POWERED;
  usb.usb_power_ma = 500;
  usb.manufacturer_name = "Pala";
  usb.pid = 0x0002;
  usb.product_name = "Pala Note SD";
  usb.serial_number = serial;
  // Class, subclass and protocol are specified by the single MSC interface.
  usb_serial_jtag_ll_phy_enable_pad(false);
  delay(200);  // let the host see the serial personality disconnect
  if (tinyusb_init(&usb) == ESP_OK) {
    while (!ejectComplete.load()) delay(25);
    tud_disconnect();
    delay(200);
  }
  restoreSerialPhy();
  esp_restart();
}
#endif
