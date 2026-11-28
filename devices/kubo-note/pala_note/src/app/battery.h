#pragma once

#include <cstdint>

struct BatteryReading {
  uint16_t millivolts = 0;
  int percent = -1;
  bool valid = false;
  uint32_t sampledAtMs = 0;  // Monotonic time of the most recent valid conversion.
};

constexpr int batteryBarCount(int percent) {
  return percent < 0 ? -1 : percent > 66 ? 3 : percent > 33 ? 2 : percent > 10 ? 1 : 0;
}

void batteryBegin();
BatteryReading readBattery();

