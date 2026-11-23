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
    if (waiting == 0) {
      following_ = false;
      waitingForBluetooth_ = false;
      bluetoothAttempted_ = false;
    } else if (!waitingForBluetooth_) {
      waitingForBluetooth_ = true;
      waitingSince_ = now;
    }
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

  constexpr void bluetoothAttempted(uint32_t now) {
    bluetoothAttempted_ = true;
    bluetoothAttemptAt_ = now;
  }

  constexpr bool keepAwakeForBluetooth(uint32_t now) const {
    return waitingForBluetooth_ && now - waitingSince_ < 45000UL;
  }

  constexpr bool needsBluetoothFollowup(uint32_t now) const {
    const uint32_t retryMs = keepAwakeForBluetooth(now) ? 12000UL : 60000UL;
    return waitingForBluetooth_ && now - receivedAt_ >= 8000UL &&
           (!bluetoothAttempted_ || now - bluetoothAttemptAt_ >= retryMs);
  }

 private:
  uint32_t submittedAt_ = 0;
  uint32_t receivedAt_ = 0;
  uint32_t attemptedAt_ = 0;
  uint32_t waitingSince_ = 0;
  uint32_t bluetoothAttemptAt_ = 0;
  bool attempted_ = false;
  bool following_ = false;
  bool received_ = false;
  bool waitingForBluetooth_ = false;
  bool bluetoothAttempted_ = false;
};
