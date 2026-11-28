#pragma once
#include <cstdint>

// Both inputs are sampled alongside microphone blocks. The locking press
// belongs to REC until released, as does the upper button that started it.
class RecordingLock {
 public:
  struct State {
    bool offer, locked, stop;
  };
  State update(uint32_t elapsed, bool upper, bool lower) {
    if (!initialized_) {
      initialized_ = true;
      lowerEligible_ = !lower;
    }
    if (lower != lowerRaw_) {
      lowerRaw_ = lower;
      lowerChange_ = elapsed;
      if (lower) lowerEligible_ = elapsed >= 5000;
    }
    if (upper != upperRaw_) {
      upperRaw_ = upper;
      upperChange_ = elapsed;
    }
    if (!locked_) {
      if (!upper) return {false, false, true};
      if (elapsed >= 5000 && lower && lowerEligible_ && elapsed - lowerChange_ >= debounce) {
        locked_ = true;
      }
    } else {
      if (!upper && elapsed - upperChange_ >= debounce) upperArmed_ = true;
      if (!lower && elapsed - lowerChange_ >= debounce) lowerArmed_ = true;
      if ((upperArmed_ && upper && elapsed - upperChange_ >= debounce) ||
          (lowerArmed_ && lower && elapsed - lowerChange_ >= debounce))
        return {false, true, true};
    }
    return {elapsed >= 5000 && !locked_, locked_, false};
  }

 private:
  static constexpr uint32_t debounce = 25;
  bool initialized_ = false, lowerEligible_ = false, locked_ = false;
  bool lowerRaw_ = false, upperRaw_ = true, upperArmed_ = false, lowerArmed_ = false;
  uint32_t lowerChange_ = 0, upperChange_ = 0;
};
