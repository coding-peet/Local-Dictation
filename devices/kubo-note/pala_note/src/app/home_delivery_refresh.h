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

  constexpr bool needsRoutineRefresh(uint32_t now) const {
    return !received_ || now - receivedAt_ >= 300000UL;
  }

  constexpr bool needsFollowup(uint32_t now) const {
    const uint32_t elapsed = now - submittedAt_;
    return following_ && elapsed >= 15000UL && elapsed < 60000UL;
  }

 private:
  uint32_t submittedAt_ = 0;
  uint32_t receivedAt_ = 0;
  bool following_ = false;
  bool received_ = false;
};
