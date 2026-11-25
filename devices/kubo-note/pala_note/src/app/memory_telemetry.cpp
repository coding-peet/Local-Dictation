#include "memory_telemetry.h"

#include <esp_attr.h>
#include <esp_heap_caps.h>
#include <esp_system.h>

namespace {

struct RetainedPeaks {
  uint32_t internal;
  uint32_t psram;
};

RTC_DATA_ATTR RetainedPeaks sleepPeaks{};

HeapUsage readHeap(uint32_t capabilities, uint32_t retainedPeak) {
  const auto total = heap_caps_get_total_size(capabilities);
  const auto free = heap_caps_get_free_size(capabilities);
  // ESP-IDF adds region low-water marks. For memory split across regions,
  // this is a conservative peak estimate even if those peaks were not simultaneous.
  const auto lowest = heap_caps_get_minimum_free_size(capabilities);
  return heapUsageFromFreeBytes(total, free, lowest, retainedPeak);
}

}  // namespace

MemoryTelemetry readMemoryTelemetry() {
  const auto previous = esp_reset_reason() == ESP_RST_DEEPSLEEP ? sleepPeaks : RetainedPeaks{};
  return {
    readHeap(MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT, previous.internal),
    readHeap(MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT, previous.psram)
  };
}

void retainMemoryPeaksForSleep() {
  const MemoryTelemetry memory = readMemoryTelemetry();
  sleepPeaks = {memory.internal.peakBytes, memory.psram.peakBytes};
}
