#if PALA_USB_CARD_SERVICE
#include "usb_storage_boot.h"
#include <Preferences.h>
#include <esp_attr.h>
#include <esp_system.h>

namespace {
constexpr uint32_t serviceCookie = 0x4D534331;
RTC_NOINIT_ATTR uint32_t immediateBoot;
bool restartPending = false;
constexpr char prefsName[] = "usb-card";
constexpr char nextKey[] = "next";
}

void requestUsbStorageRestart() { restartPending = true; }
bool usbStorageRestartPending() { return restartPending; }
void markImmediateUsbStorageBoot() { immediateBoot = serviceCookie; }

bool setNextUsbStorageBoot(bool enabled) {
  Preferences prefs;
  if (!prefs.begin(prefsName, false)) return false;
  if (!enabled) return !prefs.isKey(nextKey) || prefs.remove(nextKey);
  return prefs.putUInt(nextKey, serviceCookie) == sizeof(serviceCookie);
}

bool takeUsbStorageBoot() {
  const bool immediate = esp_reset_reason() == ESP_RST_SW && immediateBoot == serviceCookie;
  immediateBoot = 0;
  Preferences prefs;
  const bool next = prefs.begin(prefsName, true) && prefs.getUInt(nextKey, 0) == serviceCookie;
  prefs.end();
  // Consume even if both triggers apply. If NVS cannot clear an armed boot,
  // keep the card in service rather than allowing recovery to modify evidence.
  if (next) setNextUsbStorageBoot(false);
  return immediate || next;
}
#endif
