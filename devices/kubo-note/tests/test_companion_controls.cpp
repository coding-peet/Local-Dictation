#include "companion_controls.h"

constexpr uint32_t hold = 3000;
using Action = CompanionAction;

constexpr bool entryRequiresReleaseAndChordWins() {
  CompanionControls control;
  if (control.poll(10, true, true, hold) != Action::None) return false;
  if (control.poll(5000, true, false, hold) != Action::None) return false;
  if (control.poll(5600, false, false, hold) != Action::None) return false;
  if (control.poll(6000, true, false, hold) != Action::None) return false;
  if (control.poll(6080, true, true, hold) != Action::None) return false;
  if (control.poll(9079, true, true, hold) != Action::None) return false;
  return control.poll(9080, true, true, hold) == Action::Exit;
}

constexpr bool staggeredReleaseAndFastTap() {
  CompanionControls control;
  control.poll(500, false, false, hold);
  control.poll(600, true, false, hold);
  if (control.poll(650, true, true, hold) != Action::None) return false;
  if (control.poll(800, false, true, hold) != Action::None) return false;
  if (control.poll(1200, false, true, hold) != Action::None) return false;
  if (control.poll(1220, false, false, hold) != Action::None) return false;
  if (control.poll(1260, false, true, hold) != Action::None) return false;
  if (control.poll(1300, false, false, hold) != Action::None) return false;
  if (control.poll(1399, false, false, hold) != Action::None) return false;
  if (control.poll(1400, false, false, hold) != Action::Quieter) return false;
  return control.poll(2000, false, false, hold) == Action::None;
}

constexpr bool wrapAndNoRepeatWhileHeld() {
  CompanionControls control;
  control.poll(0xffffff00, false, false, hold);
  control.poll(0xfffffff0, true, false, hold);
  if (control.poll(123, true, false, hold) != Action::None) return false;
  if (control.poll(124, true, false, hold) != Action::Louder) return false;
  return control.poll(900, true, false, hold) == Action::None;
}

static_assert(entryRequiresReleaseAndChordWins());
static_assert(staggeredReleaseAndFastTap());
static_assert(wrapAndNoRepeatWhileHeld());
constexpr bool interruptedChordCannotAccumulateHoldTime() {
  CompanionControls control;
  control.poll(100, false, false, hold);
  control.poll(200, true, true, hold);
  if (control.poll(3000, true, false, hold) != Action::None) return false;
  if (control.poll(3200, true, true, hold) != Action::None) return false;
  if (control.poll(6199, true, true, hold) != Action::None) return false;
  return control.poll(6200, true, true, hold) == Action::Exit;
}
static_assert(interruptedChordCannotAccumulateHoldTime());

constexpr bool volumeOverlayRestartsAndExpiresAcrossClockWrap() {
  CompanionDisplayState display;
  if (display.volumeVisible()) return false;
  display.showVolume(0xfffffff0u);
  if (!display.volumeVisible() || display.dismissExpiredVolume(1000)) return false;
  display.showVolume(1000);
  if (display.dismissExpiredVolume(2799)) return false;
  if (!display.dismissExpiredVolume(2800) || display.volumeVisible()) return false;
  return !display.dismissExpiredVolume(2801);
}

constexpr bool batterySamplesOnlyRedrawOnVisibleChange() {
  CompanionDisplayState display;
  if (!display.updateBattery(100, 86)) return false;
  if (display.batterySampleDue(60099) || !display.batterySampleDue(60100)) return false;
  if (display.updateBattery(60100, 70) || display.batteryPercent() != 70) return false;
  if (display.updateBattery(120100, -1) || display.batteryPercent() != 70) return false;
  if (!display.updateBattery(180100, 66) || display.batteryPercent() != 66) return false;
  display.updateBattery(0xfffffff0u, 66);
  return !display.batterySampleDue(59983) && display.batterySampleDue(59984);
}

static_assert(volumeOverlayRestartsAndExpiresAcrossClockWrap());
static_assert(batterySamplesOnlyRedrawOnVisibleChange());
