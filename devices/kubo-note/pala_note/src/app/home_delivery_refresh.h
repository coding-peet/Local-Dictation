#pragma once

#include <cstdint>

class HomeDeliveryRefresh {
 public:
  constexpr void submitted(uint32_t now) {
    following_ = true;
    submittedAt_ = now;
  }

  constexpr void received(uint32_t now, unsigned waiting) {
    received_ = true;
    receivedAt_ = now;
    if (waiting == 0) following_ = false;
  }

  constexpr void attempted(uint32_t now) {
    attempted_ = true;
    attemptedAt_ = now;
  }

  constexpr bool needsRoutineRefresh(uint32_t now) const {
    constexpr uint32_t interval = 120000;
    return (!received_ || now - receivedAt_ >= interval) &&
           (!attempted_ || now - attemptedAt_ >= interval);
  }

  constexpr bool needsFollowup(uint32_t now) const {
    const uint32_t elapsed = now - submittedAt_;
    return following_ && elapsed >= 15000UL && elapsed < 60000UL;
  }

 private:
  uint32_t submittedAt_ = 0;
  uint32_t receivedAt_ = 0;
  uint32_t attemptedAt_ = 0;
  bool attempted_ = false;
  bool following_ = false;
  bool received_ = false;
};
