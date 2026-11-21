#include "../pala_note/src/app/home_bridge_status.h"

namespace {
constexpr bool verifiesOnlyForTheCurrentWake() {
  HomeBridgeStatus boot;

  if (boot.latest() != HomeBridgeLink::None || boot.needsRedraw()) return false;
  boot.verified(HomeBridgeLink::Wifi);
  if (boot.latest() != HomeBridgeLink::Wifi || !boot.needsRedraw()) return false;
  boot.drawn(HomeBridgeLink::Wifi);
  boot.verified(HomeBridgeLink::Wifi);
  if (boot.needsRedraw()) return false;

  boot.verified(HomeBridgeLink::Usb);
  if (boot.latest() != HomeBridgeLink::Usb || !boot.needsRedraw()) return false;
  boot.verified(HomeBridgeLink::Bluetooth);
  boot.drawn(HomeBridgeLink::Usb);
  if (boot.latest() != HomeBridgeLink::Bluetooth || !boot.needsRedraw()) return false;
  boot.drawn(HomeBridgeLink::Bluetooth);
  if (boot.needsRedraw()) return false;

  boot.reset();
  return boot.latest() == HomeBridgeLink::None && !boot.needsRedraw();
}

static_assert(verifiesOnlyForTheCurrentWake());
}
