#pragma once

#include <stdint.h>

// The ESP32 Serial/JTAG monitor reports recent SOF packets. Brief gaps
// should not discard the active transfer or change the pairing attachment.
class UsbDataPresence {
 public:
  constexpr bool update(bool hasSof, uint32_t now) {
    if (hasSof) {
      connected_ = true;
      waitingForDetach_ = false;
    } else if (connected_) {
      if (!waitingForDetach_) {
        absentSince_ = now;
        waitingForDetach_ = true;
      } else if (now - absentSince_ >= 250) {
        connected_ = false;
      }
    }
    return connected_;
  }

 private:
  uint32_t absentSince_ = 0;
  bool connected_ = false;
  bool waitingForDetach_ = false;
};

enum class WifiBacklog { Unchecked, Ready, Busy, Unavailable };
enum class BacklogLink { Usb, Wifi, Bluetooth, Deferred };

constexpr WifiBacklog classifyWifiReadiness(int status, bool hasReadyField, bool ready) {
  if (!hasReadyField) return WifiBacklog::Unavailable;
  if (status == 200 && ready) return WifiBacklog::Ready;
  if (status == 503 && !ready) return WifiBacklog::Busy;
  return WifiBacklog::Unavailable;
}

constexpr bool resumeBacklogAfterUsbDetach(bool idle, bool hadSession, bool wasProbing) {
  return idle && (hadSession || wasProbing);
}

constexpr bool tryPreferredBluetooth(bool preferred, bool alreadyTried, bool audioQueued) {
  return preferred && !alreadyTried && audioQueued;
}

constexpr BacklogLink pairedBacklogAfterWifi(WifiBacklog wifi, bool wifiWorkDue, bool audioQueued) {
  if (wifi == WifiBacklog::Ready && wifiWorkDue) return BacklogLink::Wifi;
  if (wifi == WifiBacklog::Unavailable && audioQueued) return BacklogLink::Bluetooth;
  return BacklogLink::Deferred;
}

struct WifiBacklogSelection {
  int index = -1;
  WifiBacklog status = WifiBacklog::Unavailable;
};

template <typename Probe>
constexpr WifiBacklogSelection selectWifiBacklog(int count, int previous, Probe probe) {
  WifiBacklogSelection busy;
  if (previous >= 0 && previous < count) {
    const auto status = probe(previous);
    if (status == WifiBacklog::Ready) return {previous, status};
    if (status == WifiBacklog::Busy) busy = {previous, status};
  }
  for (int index = 0; index < count; ++index) {
    if (index == previous) continue;
    const auto status = probe(index);
    if (status == WifiBacklog::Ready) return {index, status};
    if (status == WifiBacklog::Busy && busy.index < 0) busy = {index, status};
  }
  return busy;
}


constexpr BacklogLink backlogLink(bool usbAuthenticated, bool usbData,
                                  bool usbProbeOpen, WifiBacklog wifi) {
  if (usbAuthenticated || (usbData && usbProbeOpen)) return BacklogLink::Usb;
  if (wifi == WifiBacklog::Busy) return BacklogLink::Deferred;
  if (wifi == WifiBacklog::Unavailable) return BacklogLink::Bluetooth;
  return BacklogLink::Wifi;
}

constexpr const char* backlogTransportLabel(BacklogLink link) {
  switch (link) {
    case BacklogLink::Usb: return "USB";
    case BacklogLink::Wifi: return "Wi-Fi";
    case BacklogLink::Bluetooth: return "BT";
    case BacklogLink::Deferred: return nullptr;
  }
  return nullptr;
}
