#pragma once

#include <stdint.h>

struct IdleSleepPolicy {
  uint32_t homeMs;
  uint32_t queuedMs;
  uint32_t companionMs;
};

// Zero restores the built-in timeouts. Positive values set one idle timeout.
constexpr int kMaxIdleSleepMinutes = 99;
constexpr bool validIdleSleepMinutes(int minutes) {
  return minutes >= 0 && minutes <= kMaxIdleSleepMinutes;
}

constexpr IdleSleepPolicy idleSleepPolicy(int minutes) {
  if (!validIdleSleepMinutes(minutes) || minutes == 0) {
    return {60000UL, 180000UL, 300000UL};
  }
  const uint32_t timeoutMs = static_cast<uint32_t>(minutes) * 60000UL;
  return {timeoutMs, timeoutMs, timeoutMs};
}
