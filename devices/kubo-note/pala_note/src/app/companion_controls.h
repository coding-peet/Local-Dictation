#pragma once

#include <cstdint>

enum class CompanionAction { None, Louder, Quieter, Exit };

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
