#include "../pala_note/src/app/backlog_transport.h"

static_assert(backlogLink(true, false, false, WifiBacklog::Unavailable) == BacklogLink::Usb);
static_assert(backlogLink(false, true, true, WifiBacklog::Unchecked) == BacklogLink::Usb);
static_assert(backlogLink(false, false, true, WifiBacklog::Unchecked) == BacklogLink::Wifi);
static_assert(backlogLink(false, true, false, WifiBacklog::Unchecked) == BacklogLink::Wifi);
static_assert(backlogLink(false, false, false, WifiBacklog::Ready) == BacklogLink::Wifi);
static_assert(backlogLink(false, false, false, WifiBacklog::Busy) == BacklogLink::Deferred);
static_assert(backlogLink(false, true, false, WifiBacklog::Unavailable) == BacklogLink::Bluetooth);


constexpr bool triesLastWorkingBridgeFirst() {
  int calls = 0;
  int first = -1;
  const auto selection = selectWifiBacklog(3, 2, [&](int index) {
    if (calls++ == 0) first = index;
    return WifiBacklog::Ready;
  });
  return calls == 1 && first == 2 && selection.index == 2 && selection.status == WifiBacklog::Ready;
}

static_assert(triesLastWorkingBridgeFirst());
static_assert(selectWifiBacklog(3, 2, [](int index) {
  return index == 2 ? WifiBacklog::Unavailable : WifiBacklog::Ready;
}).index == 0);
static_assert(selectWifiBacklog(3, 2, [](int index) {
  return index == 2 ? WifiBacklog::Busy : WifiBacklog::Ready;
}).index == 0);
static_assert(selectWifiBacklog(3, 2, [](int index) {
  return index == 2 || index == 0 ? WifiBacklog::Busy : WifiBacklog::Unavailable;
}).index == 2);
static_assert(selectWifiBacklog(0, -1, [](int) {
  return WifiBacklog::Ready;
}).status == WifiBacklog::Unavailable);

static_assert(classifyWifiReadiness(200, true, true) == WifiBacklog::Ready);
static_assert(classifyWifiReadiness(503, true, false) == WifiBacklog::Busy);
static_assert(classifyWifiReadiness(503, false, false) == WifiBacklog::Unavailable);
static_assert(classifyWifiReadiness(503, true, true) == WifiBacklog::Unavailable);
static_assert(classifyWifiReadiness(200, true, false) == WifiBacklog::Unavailable);
static_assert(classifyWifiReadiness(200, false, false) == WifiBacklog::Unavailable);
static_assert(classifyWifiReadiness(502, true, false) == WifiBacklog::Unavailable);
static_assert(classifyWifiReadiness(401, true, true) == WifiBacklog::Unavailable);

constexpr bool confirmsUsbDetachOnlyAfterContinuousLoss() {
  UsbDataPresence signal;
  if (signal.update(false, 0) || !signal.update(true, 1)) return false;
  // Interrupted runs of missing SOFs cannot discard the current transfer.
  if (!signal.update(false, 2) || !signal.update(false, 151) || !signal.update(true, 152)) return false;
  if (!signal.update(false, 500) || !signal.update(false, 749)) return false;
  if (signal.update(false, 750) || signal.update(false, 751)) return false;
  return signal.update(true, 752);
}
static_assert(confirmsUsbDetachOnlyAfterContinuousLoss());

constexpr bool usbDetachConfirmationSurvivesMillisWrap() {
  UsbDataPresence signal;
  return signal.update(true, 0xfffffff0u) && signal.update(false, 0xfffffff8u) &&
         signal.update(false, 241) && !signal.update(false, 242);
}
static_assert(usbDetachConfirmationSurvivesMillisWrap());

static_assert(resumeBacklogAfterUsbDetach(true, true, false));
static_assert(resumeBacklogAfterUsbDetach(true, false, true));
static_assert(!resumeBacklogAfterUsbDetach(true, false, false));
static_assert(!resumeBacklogAfterUsbDetach(false, true, true));

// A Wi-Fi retry delay is not a prohibition on a paired recording transfer.
static_assert(pairedBacklogAfterWifi(WifiBacklog::Unavailable, false, true) == BacklogLink::Bluetooth);
static_assert(pairedBacklogAfterWifi(WifiBacklog::Busy, false, true) == BacklogLink::Deferred);
static_assert(pairedBacklogAfterWifi(WifiBacklog::Ready, false, true) == BacklogLink::Deferred);
static_assert(pairedBacklogAfterWifi(WifiBacklog::Ready, true, true) == BacklogLink::Wifi);
static_assert(pairedBacklogAfterWifi(WifiBacklog::Unavailable, true, false) == BacklogLink::Deferred);
static_assert(pairedBacklogAfterWifi(WifiBacklog::Unchecked, true, true) == BacklogLink::Deferred);

static_assert(tryPreferredBluetooth(true, false, true));
static_assert(!tryPreferredBluetooth(true, true, true));
static_assert(!tryPreferredBluetooth(false, false, true));
static_assert(!tryPreferredBluetooth(true, false, false));
