#pragma once

#include <cstdint>
#include "battery.h"

enum class CompanionAction { None, Louder, Quieter, Exit };

class CompanionDisplayState {
 public:
  constexpr int batteryPercent() const { return batteryPercent_; }
  constexpr bool batterySampleDue(uint32_t now) const { return now - batterySampledAt_ >= 60000UL; }

  constexpr bool updateBattery(uint32_t now, int percent) {
    batterySampledAt_ = now;
    // Keep the last valid reading and refresh only when the visible bars change.
    if (percent < 0) return false;
    const bool changed = batteryBarCount(percent) != batteryBarCount(batteryPercent_);
    batteryPercent_ = percent;
    return changed;
  }

  constexpr bool volumeVisible() const { return volumeVisible_; }
  constexpr void showVolume(uint32_t now) {
    volumeVisible_ = true;
    volumeShownAt_ = now;
  }

  constexpr bool dismissExpiredVolume(uint32_t now) {
    if (!volumeVisible_ || now - volumeShownAt_ < 1800UL) return false;
    volumeVisible_ = false;
    return true;
  }

 private:
  int batteryPercent_ = -1;
  uint32_t batterySampledAt_ = 0;
  uint32_t volumeShownAt_ = 0;
  bool volumeVisible_ = false;
};

class CompanionControls {
 public:
  constexpr void reset() { *this = CompanionControls{}; }

  constexpr CompanionAction poll(uint32_t now, bool upper, bool lower, uint32_t holdMs) {
    if (!armed_) {
      armed_ = !upper && !lower;
      return CompanionAction::None;
    }

    if (upper && lower) {
      pending_ = CompanionAction::None;
      if (!bothWereDown_) chordAt_ = now;
      bothWereDown_ = true;
      chord_ = true;
      return now - chordAt_ >= holdMs ? CompanionAction::Exit : CompanionAction::None;
    }

    bothWereDown_ = false;
    if (chord_) {
      // A partially released chord must not become a volume adjustment.
      chord_ = upper || lower;
      upperWasDown_ = false;
      lowerWasDown_ = false;
      return CompanionAction::None;
    }

    if (upper && !upperWasDown_) start(now, CompanionAction::Louder);
    else if (lower && !lowerWasDown_) start(now, CompanionAction::Quieter);
    upperWasDown_ = upper;
    lowerWasDown_ = lower;
    if (pending_ == CompanionAction::None || now - singleAt_ < 140) return CompanionAction::None;
    const CompanionAction action = pending_;
    pending_ = CompanionAction::None;
    return action;
  }

 private:
  constexpr void start(uint32_t now, CompanionAction action) {
    singleAt_ = now;
    pending_ = action;
  }

  CompanionAction pending_ = CompanionAction::None;
  uint32_t singleAt_ = 0;
  uint32_t chordAt_ = 0;
  bool armed_ = false;
  bool chord_ = false;
  bool bothWereDown_ = false;
  bool upperWasDown_ = false;
  bool lowerWasDown_ = false;
};
