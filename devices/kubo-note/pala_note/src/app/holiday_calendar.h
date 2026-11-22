#pragma once

#include "date_validation.h"
#include <array>
#include <string_view>

constexpr size_t kHolidaysPerYear = 16;
constexpr size_t kPriorDecemberDays = 6;
constexpr size_t kMaximumHolidayDates = kPriorDecemberDays + 2 * kHolidaysPerYear;
constexpr size_t kHolidaySnapshotMaxBytes = 9 + 4 * kMaximumHolidayDates;

struct HolidayCalendar {
  int year = 0;
  std::array<int, kMaximumHolidayDates> dates{};
  size_t count = 0;

  constexpr bool contains(int year, int month, int day) const {
    const int date = 10000 * year + 100 * month + day;
    for (size_t i = 0; i < count; ++i) {
      if (dates[i] >= date) return dates[i] == date;
    }
    return false;
  }
};

constexpr int holidayDecimal(std::string_view text) {
  int value = 0;
  for (char digit : text) {
    if (digit < '0' || digit > '9') return -1;
    value = 10 * value + digit - '0';
  }
  return value;
}

constexpr bool parseHolidaySnapshot(std::string_view payload, HolidayCalendar& calendar) {
  // Two-letter country, current year, and three lists of packed MMDD dates.
  // Only the preceding December 26-31 can appear in the first list.
  if (payload.size() < 9 || payload.size() > kHolidaySnapshotMaxBytes ||
      payload[0] < 'A' || payload[0] > 'Z' ||
      payload[1] < 'A' || payload[1] > 'Z' || payload[2] != ',' || payload[7] != ',') return false;
  HolidayCalendar candidate{};
  candidate.year = holidayDecimal(payload.substr(3, 4));
  if (!validCalendarDate(candidate.year, 1, 1)) return false;
  payload.remove_prefix(8);

  for (int offset = -1; offset <= 1; ++offset) {
    const auto separator = payload.find(',');
    if ((offset < 1) != (separator != std::string_view::npos)) return false;
    const auto days = payload.substr(0, separator);
    const size_t limit = offset < 0 ? kPriorDecemberDays : kHolidaysPerYear;
    if (days.size() % 4 || days.size() > 4 * limit || (offset >= 0 && days.empty())) return false;
    const int year = candidate.year + offset;
    int last = offset < 0 ? 1225 : 0;
    for (size_t i = 0; i < days.size(); i += 4) {
      const int date = holidayDecimal(days.substr(i, 4));
      if (date <= last || !validCalendarDate(year, date / 100, date % 100)) return false;
      candidate.dates[candidate.count++] = 10000 * year + date;
      last = date;
    }
    if (offset < 1) payload.remove_prefix(separator + 1);
  }
  calendar = candidate;
  return true;
}
