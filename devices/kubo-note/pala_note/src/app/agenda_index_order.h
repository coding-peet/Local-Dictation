#pragma once

#include <algorithm>
#include <cstdint>
#include <cstring>

namespace agenda_index {
struct Key {
  uint32_t note = 0;
  uint32_t ordinal = 0;
  uint32_t offset = 0;
  char due[21]{};
  uint8_t flags = 0;
  uint8_t reserved[2]{};
};
static_assert(sizeof(Key) == 36);

inline bool recentBefore(const Key& a, const Key& b) {
  return a.note != b.note ? a.note < b.note : a.ordinal < b.ordinal;
}

inline bool dueBefore(const Key& a, const Key& b) {
  const int date = std::strcmp(a.due, b.due);
  return date ? date < 0 : recentBefore(a, b);
}

struct MergeRun {
  uint32_t endLeft = 0, endRight = 0, left = 0, right = 0;

  void begin(uint32_t first, uint32_t width, uint32_t count) {
    left = first;
    endLeft = std::min(count, first + width);
    right = endLeft;
    endRight = std::min(count, endLeft + width);
  }

  // Input uses independent seek/read functions so each stream can keep its SD
  // cursor positioned. Only two keys are needed to merge arbitrarily long runs.
  template <class Left, class Right, class Write>
  bool step(uint32_t budget, Left&& readLeft, Right&& readRight, Write&& write) {
    Key a{}, b{};
    bool hasA = left < endLeft;
    bool hasB = right < endRight;
    if ((hasA && !readLeft(left, a)) || (hasB && !readRight(right, b))) return false;
    while (budget-- && (hasA || hasB)) {
      if (hasA && (!hasB || !dueBefore(b, a))) {
        if (!write(a)) return false;
        hasA = ++left < endLeft;
        if (hasA && !readLeft(left, a)) return false;
      } else {
        if (!write(b)) return false;
        hasB = ++right < endRight;
        if (hasB && !readRight(right, b)) return false;
      }
    }
    return true;
  }

  bool done() const { return left == endLeft && right == endRight; }
};

template <class Read>
bool lowerBoundDue(uint32_t count, const char* due, Read&& read, uint32_t& position) {
  position = 0;
  while (position < count) {
    const uint32_t mid = position + (count - position) / 2;
    Key key{};
    if (!read(mid, key)) return false;
    if (std::strcmp(key.due, due) < 0) position = mid + 1;
    else count = mid;
  }
  return true;
}
}  // namespace agenda_index
