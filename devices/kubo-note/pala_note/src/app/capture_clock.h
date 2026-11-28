#pragma once
#include <cstdint>
#include <ctime>
#include <string>

namespace capture_clock {
// Offset wall time to the REC edge, including a millis() wrap within the take.
inline uint32_t atEdge(bool trusted, int64_t seconds, uint32_t micros,
                       uint32_t sampledMillis, uint32_t edgeMillis) {
  constexpr int64_t earliest = 1704067200;  // 2024-01-01
  constexpr int64_t end = 4102444800;       // 2100-01-01
  if (!trusted || seconds < earliest || seconds >= end || micros >= 1000000) return 0;
  const auto edge = (seconds * 1000 + micros / 1000 - (sampledMillis - edgeMillis)) / 1000;
  return edge >= earliest ? static_cast<uint32_t>(edge) : 0;
}

// Keep RTC time tied to the monotonic clock. A correction after the press
// can establish time for subsequent takes, but cannot date an earlier one.
struct Anchor {
  int64_t wallUs = 0, monotonicUs = 0, validFromMs = 0;

  void sync(int64_t wall, int64_t monotonic, bool boot, bool stepped) {
    const int64_t correction = wall - (wallUs + monotonic - monotonicUs);
    if (!wallUs || stepped || correction < -1000000 || correction > 1000000)
      validFromMs = boot ? 0 : monotonic / 1000 + 1;
    wallUs = wall;
    monotonicUs = monotonic;
  }

  uint32_t epoch(int64_t nowUs, uint32_t edgeMs) const {
    const int64_t nowMs = nowUs / 1000;
    const uint32_t age = static_cast<uint32_t>(nowMs) - edgeMs;
    if (!wallUs || nowMs - age < validFromMs) return 0;
    const int64_t wall = wallUs + nowUs - monotonicUs;
    return atEdge(true, wall / 1000000, wall % 1000000, static_cast<uint32_t>(nowMs), edgeMs);
  }
};

inline std::string captureUtc(uint32_t seconds) {
  const auto epoch = static_cast<std::time_t>(seconds);
  if (epoch < 1600000000) return {};
  std::tm fields{};
#ifdef _WIN32
  if (gmtime_s(&fields, &epoch) != 0) return {};
#else
  if (!gmtime_r(&epoch, &fields)) return {};
#endif
  char stamp[24]{};
  return std::strftime(stamp, sizeof(stamp), "%Y-%m-%dT%H:%M:%SZ", &fields) ? stamp : "";
}
}
