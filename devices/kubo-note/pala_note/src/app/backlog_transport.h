#pragma once

enum class WifiBacklog { Unchecked, Ready, Busy, Unavailable };
enum class BacklogLink { Usb, Wifi, Bluetooth, Deferred };

constexpr BacklogLink backlogLink(bool usbAuthenticated, bool usbData,
                                  bool usbProbeOpen, WifiBacklog wifi) {
  if (usbAuthenticated || (usbData && usbProbeOpen)) return BacklogLink::Usb;
  if (wifi == WifiBacklog::Busy) return BacklogLink::Deferred;
  if (wifi == WifiBacklog::Unavailable) return BacklogLink::Bluetooth;
  return BacklogLink::Wifi;
}
