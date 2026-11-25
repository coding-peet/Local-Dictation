#pragma once

#include <cstdint>
#include <string_view>

constexpr int64_t kHomeUsageMinimumUtc = 1704067200;
constexpr int kHomeWeeklyGaugeSegments = 7;

struct HomeCodexDisplay {
  int remaining = -1;
  int segments = 0;
  int filled = 0;

  constexpr bool segmentFilled(int index) const {
    if (segments == kHomeWeeklyGaugeSegments) return index >= segments - filled;
    return index < filled;
  }

  constexpr bool operator!=(const HomeCodexDisplay& other) const {
    return remaining != other.remaining || segments != other.segments || filled != other.filled;
  }
};

struct HomeCodexWindow {
  int remaining = -1;
  uint32_t resetsAt = 0;

  constexpr bool isLive(int64_t utc, uint32_t sampleExpiresAt) const {
    return remaining >= 0 && utc < (resetsAt ? resetsAt : sampleExpiresAt);
  }
};

struct HomeDeliveryCounts {
  int generalOpen = -1;
  int waiting = -1;
  char lastDelivery[6]{};
  uint32_t sampleExpiresAt = 0;
  HomeCodexWindow fiveHours;
  HomeCodexWindow weekly;

  constexpr HomeCodexDisplay codexAt(int64_t utc, bool preferWeekly) const {
    if (utc < kHomeUsageMinimumUtc) return {};
    const bool useWeekly = preferWeekly || !fiveHours.isLive(utc, sampleExpiresAt);
    const auto& window = useWeekly ? weekly : fiveHours;
    if (!window.isLive(utc, sampleExpiresAt)) return {};
    if (!window.resetsAt) return {window.remaining, 0, 0};
    const int segments = useWeekly ? kHomeWeeklyGaugeSegments : 5;
    const int secondsPerSegment = useWeekly ? 86400 : 3600;
    const int64_t filled = (window.resetsAt - utc + secondsPerSegment - 1) / secondsPerSegment;
    return {window.remaining, segments, filled > segments ? segments : static_cast<int>(filled)};
  }
};

constexpr int homeCount(std::string_view value) {
  if (value.empty() || value.size() > 9) return -1;
  int count = 0;
  for (char digit : value) {
    if (digit < '0' || digit > '9') return -1;
    count = count * 10 + (digit - '0');
  }
  return count;
}

constexpr bool parseHomeUtc(std::string_view text, uint32_t& result) {
  if (text == "0") {
    result = 0;
    return true;
  }
  if (text.size() != 10) return false;
  uint64_t value = 0;
  for (char digit : text) {
    if (digit < '0' || digit > '9') return false;
    value = value * 10 + (digit - '0');
  }
  if (value < kHomeUsageMinimumUtc || value > UINT32_MAX) return false;
  result = static_cast<uint32_t>(value);
  return true;
}

constexpr bool parseHomeCodexWindow(std::string_view percent, std::string_view resets, HomeCodexWindow& result) {
  const int remaining = homeCount(percent);
  uint32_t reset = 0;
  if (!parseHomeUtc(resets, reset) || remaining > 100 ||
      (remaining < 0 && (percent != "-1" || reset))) return false;
  result = {remaining, reset};
  return true;
}

constexpr bool parseHomeDashboard(std::string_view payload, HomeDeliveryCounts& result) {
  const auto version = payload.substr(0, 3);
  const int fieldCount = version == "h4," ? 8 : version == "h3," ? 5 : version == "h2," ? 3 : 0;
  if (!fieldCount) return false;
  std::string_view fields[8]{};
  auto start = std::size_t{3};
  for (int i = 0; i < fieldCount; ++i) {
    const auto end = payload.find(',', start);
    if ((end != std::string_view::npos) != (i + 1 < fieldCount)) return false;
    fields[i] = payload.substr(start, end - start);
    start = end + 1;
  }
  HomeDeliveryCounts parsed;
  parsed.generalOpen = homeCount(fields[0]);
  parsed.waiting = homeCount(fields[1]);
  if ((parsed.generalOpen < 0 && fields[0] != "-1") || parsed.waiting < 0) return false;
  const auto last = fields[2];
  if (last != "-") {
    if (last.size() != 5 || last[2] != ':' || homeCount(last.substr(0, 2)) < 0 ||
        homeCount(last.substr(0, 2)) > 23 || homeCount(last.substr(3, 2)) < 0 ||
        homeCount(last.substr(3, 2)) > 59) return false;
    for (std::size_t i = 0; i < last.size(); ++i) parsed.lastDelivery[i] = last[i];
  }
  if (version == "h3,") {
    // The previous format carried one percentage without its window type.
    if (!parseHomeUtc(fields[4], parsed.sampleExpiresAt) || !parsed.sampleExpiresAt ||
        !parseHomeCodexWindow(fields[3], "0", parsed.fiveHours) || parsed.fiveHours.remaining < 0) return false;
  } else if (version == "h4,") {
    if (!parseHomeUtc(fields[3], parsed.sampleExpiresAt) || !parsed.sampleExpiresAt ||
        !parseHomeCodexWindow(fields[4], fields[5], parsed.fiveHours) ||
        !parseHomeCodexWindow(fields[6], fields[7], parsed.weekly)) return false;
  }
  result = parsed;
  return true;
}
