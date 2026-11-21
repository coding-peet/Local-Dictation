#pragma once

#include <cstddef>

// The order is stored with uppercase enabled pages and lowercase hidden pages.
// Keep the full permutation so reenabling a page restores its saved position.
constexpr char kDefaultDisplayPages[] = "TECNm";
constexpr int kDisplayPageCount = sizeof(kDefaultDisplayPages) - 1;
constexpr int kLegacyDisplayPageCount = kDisplayPageCount - 1;

enum class DisplayPage : char { Tasks = 'T', Events = 'E', CurrentMonth = 'C', NextMonth = 'N', MonthAfterNext = 'M' };

constexpr char displayPageId(char code) {
  return code >= 'a' && code <= 'z' ? code - 'a' + 'A' : code;
}

constexpr bool validDisplayPages(const char* order, std::size_t length) {
  if (!order || (length != kDisplayPageCount && length != kLegacyDisplayPageCount)) return false;

  for (std::size_t index = 0; index < length; ++index) {
    const char page = displayPageId(order[index]);
    const bool thirdMonth = page == 'M' && length == kDisplayPageCount;
    if (!thirdMonth && page != 'T' && page != 'E' && page != 'C' && page != 'N') return false;
    for (std::size_t prior = 0; prior < index; ++prior) {
      if (displayPageId(order[prior]) == page) return false;
    }
  }
  return true;
}

constexpr int nextDisplayPageIndex(const char* order, int after) {
  for (int index = after + 1; index < kDisplayPageCount; ++index) {
    if (order[index] == displayPageId(order[index])) return index;
  }
  return -1;
}
