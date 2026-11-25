#pragma once

#include <cstdint>

struct HeapUsage {
  uint32_t usedBytes = 0;
  uint32_t peakBytes = 0;
  uint32_t totalBytes = 0;

  constexpr int usedPercent() const { return percent(usedBytes); }
  constexpr int peakPercent() const { return percent(peakBytes); }

 private:
  constexpr int percent(uint32_t bytes) const {
    if (!totalBytes) return -1;
    return static_cast<int>((uint64_t{bytes} * 100 + totalBytes / 2) / totalBytes);
  }
};

struct MemoryTelemetry {
  HeapUsage internal;
  HeapUsage psram;
};

constexpr HeapUsage heapUsageFromFreeBytes(uint32_t total, uint32_t free, uint32_t lowestFree,
                                          uint32_t retainedPeak = 0) {
  const uint32_t used = free < total ? total - free : 0;
  const uint32_t bootPeak = lowestFree < total ? total - lowestFree : 0;
  const uint32_t previousPeak = retainedPeak < total ? retainedPeak : total;
  const uint32_t peak = bootPeak > previousPeak ? bootPeak : previousPeak;
  return {used, peak > used ? peak : used, total};
}

MemoryTelemetry readMemoryTelemetry();
void retainMemoryPeaksForSleep();
