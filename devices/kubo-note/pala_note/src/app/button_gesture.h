#pragma once

#include <stdint.h>

enum class ButtonAction { None, Single, Double, Triple, Hold };

class ButtonGesture {
 public:
  constexpr void reset() { *this = ButtonGesture{}; }

  constexpr bool pending() const { return taps_ || (!held_ && (rawDown_ || settledDown_)); }

  constexpr ButtonAction edge(bool down, uint32_t at, uint8_t completeAt) {
    const ButtonAction action = poll(at, completeAt);
    if (rawDown_ != down) {
      rawDown_ = down;
      rawAt_ = at;
    }
    return action;
  }

  constexpr ButtonAction poll(uint32_t now, uint8_t completeAt) {
    if (settledDown_ != rawDown_ && now - rawAt_ >= kDebounceMs) {
      settledDown_ = rawDown_;
      if (settledDown_) {
        pressedAt_ = rawAt_;
      } else {
        if (!held_) {
          releasedAt_ = rawAt_;
          if (++taps_ == completeAt) return finishTaps();
        }
        held_ = false;
      }
    }
    if (settledDown_ && rawDown_ && !held_ && now - pressedAt_ >= kHoldMs) {
      held_ = true;
      taps_ = 0;
      return ButtonAction::Hold;
    }
    // A press that started inside the window still counts when released later.
    if (taps_ && !settledDown_ && !rawDown_ && now - releasedAt_ >= kInterTapMs) {
      return finishTaps();
    }
    return ButtonAction::None;
  }

 private:
  constexpr ButtonAction finishTaps() {
    const uint8_t count = taps_;
    taps_ = 0;
    return count == 1 ? ButtonAction::Single : count == 2 ? ButtonAction::Double : ButtonAction::Triple;
  }

  static constexpr uint32_t kDebounceMs = 14;
  static constexpr uint32_t kHoldMs = 320;
  static constexpr uint32_t kInterTapMs = 360;
  uint32_t rawAt_ = 0;
  uint32_t pressedAt_ = 0;
  uint32_t releasedAt_ = 0;
  uint8_t taps_ = 0;
  bool rawDown_ = false;
  bool settledDown_ = false;
  bool held_ = false;
};
